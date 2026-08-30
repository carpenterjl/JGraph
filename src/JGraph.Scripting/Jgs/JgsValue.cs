using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Data;
using JGraph.Imaging;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>The runtime type of a <see cref="JgsValue"/>.</summary>
internal enum JgsType
{
    Null,
    Number,
    Complex,
    Bool,
    String,
    Array,
    Table,
    Image,
    Function,

    /// <summary>A MATLAB cell array: a list whose elements may be of any type, written <c>{1, 'two'}</c>.</summary>
    Cell,

    /// <summary>A MATLAB struct: named fields, written <c>s.field</c>.</summary>
    Struct,

    /// <summary>A sparse matrix (M42): compressed sparse column storage, built by <c>sparse</c>/<c>sprand</c>.</summary>
    Sparse,

    /// <summary>An instance of a user class (M68), defined by a <c>classdef</c> file.</summary>
    Object,
}

/// <summary>The element kind of a packed array: MATLAB doubles or a MATLAB-style logical mask.</summary>
internal enum JgsPackedKind : byte
{
    /// <summary>Elements read as <see cref="JgsType.Number"/> values.</summary>
    Number,

    /// <summary>Elements read as <see cref="JgsType.Bool"/> values (stored as 0.0 / 1.0).</summary>
    Bool,
}

/// <summary>
/// A dynamically-typed JGS runtime value: a null, a double, a boolean, a string, an array of values, a
/// data <see cref="Table"/>, or a callable function. Numbers and booleans are stored inline; the other
/// kinds hold a reference. Values are immutable except that an <see cref="JgsType.Array"/>'s elements can
/// be replaced in place (indexed assignment).
/// </summary>
/// <remarks>
/// A homogeneous numeric array may be <em>packed</em>: <see cref="Type"/> is still
/// <see cref="JgsType.Array"/>, but the reference slot holds a flat <see cref="NumericBuffer"/>
/// instead of a <c>JgsValue[]</c> — 8 bytes per element instead of a heap object each. Exactly one
/// wrapper ever exists per buffer (aliases share the wrapper, which is what gives arrays their
/// reference semantics), so <see cref="DemoteToBoxed"/> can swap the representation in place and
/// every alias sees the demotion. Code that has not been taught about packing must go through
/// <see cref="BoxedElements"/> / <see cref="ElementAt"/> / <see cref="ArrayLength"/>;
/// <see cref="AsArray"/> throws for packed values so a missed call site fails loudly instead of
/// silently misbehaving.
/// <para>
/// An array also carries a <see cref="Rows"/>-by-<see cref="Cols"/> shape over that flat storage,
/// column-major (ADR 0043). A value built by <see cref="Array"/> or <see cref="Packed"/> is a row —
/// 1-by-n — so nothing that does not ask for a shape sees one.
/// </para>
/// </remarks>
internal sealed class JgsValue
{
    /// <summary>The shared null value.</summary>
    public static readonly JgsValue Null = new(JgsType.Null, 0, null);

    /// <summary>The shared true value.</summary>
    public static readonly JgsValue True = new(JgsType.Bool, 1, null);

    /// <summary>The shared false value.</summary>
    public static readonly JgsValue False = new(JgsType.Bool, 0, null);

    private readonly double _number;
    private object? _reference; // mutable ONLY by DemoteToBoxed, TryGrowInPlace and CompactInPlace
    private readonly JgsPackedKind _packedKind;
    private int _rows; // mutable ONLY by Reshape and TryGrowInPlace
    private int _cols;

    // Growth capacity (M41): 0 means compact storage — the buffer is exactly Rows*Cols, column-major.
    // Non-zero means a packed numeric buffer laid out with this column stride, so the buffer holds
    // spare rows and columns and A(i, j) writes past the edge can grow the logical shape without
    // copying anything. Only TryGrowInPlace installs a stride; CompactInPlace and DemoteToBoxed
    // remove it, and AsBuffer compacts on sight so no raw-buffer consumer can ever see the slack.
    private int _strideRows;

    // The numeric class this value remembers being asked for (M47, ADR 0050). Double for everything
    // that never went through a class constructor, which is why nothing that ignores the tag ever
    // sees one. Mutable ONLY by SetNumericClass, at mint time, exactly like Reshape.
    private JgsNumericClass _numericClass;

    // The class name a struct answers to (M62). Null for every ordinary struct; set only where a
    // builtin mints something that is an object in MATLAB and a struct here — today that is
    // MException alone. M68 replaces this with a real object type, and the field names do not move.
    private string? _className;

    // Whether this array is a MATLAB string array (M63). False for every other array, which is why
    // nothing that ignores the tag ever sees one. Mutable ONLY by MarkStringArray, at mint time,
    // exactly like SetNumericClass.
    private bool _isStringArray;

    // Whether this array is a MATLAB char matrix (M105): a 2-D array whose elements are code points
    // rather than numbers. False for every other array. Mutable ONLY by MarkCharMatrix, at mint
    // time, exactly like MarkStringArray.
    private bool _isCharMatrix;

    // What kind of time this array is holding, and how it displays (M64). Null for every array that
    // is not a datetime or a duration — which is nearly all of them, and is why the numeric storage
    // underneath goes on behaving exactly as it did. Mutable ONLY by MarkTime, at mint time.
    private JgsTimeTag? _time;

    // N-D shape (M41, ADR 0044): null for every 2-D value. When set (always length >= 3, trailing
    // singletons trimmed), it is the true size of the array, and _rows/_cols hold MATLAB's own 2-D
    // view of it — dims[0] rows by prod(dims[1..]) columns — so every two-subscript reader sees
    // exactly the fold MATLAB defines and only size/ndims/N-subscript indexing consult the truth.
    private int[]? _dims;

    private JgsValue(JgsType type, double number, object? reference, JgsPackedKind packedKind = JgsPackedKind.Number)
    {
        Type = type;
        _number = number;
        _reference = reference;
        _packedKind = packedKind;

        // An array (or cell) with no shape asked for is a row: 1-by-n, which is what a flat
        // literal means. Cells gained their shape in M41 so cell(r, c) can carry it the same way.
        _rows = 1;
        _cols = type is JgsType.Array or JgsType.Cell ? ElementCount(reference) : 0;
    }

    private JgsValue(JgsType type, object? reference, JgsPackedKind packedKind, int rows, int cols)
        : this(type, 0, reference, packedKind)
    {
        int count = ElementCount(reference);
        if ((long)rows * cols != count)
        {
            throw new ArgumentException($"A {rows}x{cols} shape does not describe {count} elements.", nameof(rows));
        }

        _rows = rows;
        _cols = cols;
    }

    private static int ElementCount(object? reference) => reference switch
    {
        NumericBuffer buffer => buffer.Length,
        JgsPackedComplex complex => complex.Length,
        JgsValue[] elements => elements.Length,
        JgsStructArray structs => structs.Length,
        _ => 0,
    };

    /// <summary>The runtime kind of this value.</summary>
    public JgsType Type { get; }

    /// <summary>Wraps a number.</summary>
    public static JgsValue Number(double value) => new(JgsType.Number, value, null);

    /// <summary>
    /// Wraps a complex number. A value with zero imaginary part normalizes to a plain
    /// <see cref="JgsType.Number"/>, so real-valued results of complex math flow back into every
    /// numeric path (comparisons, plotting, indexing) without special cases.
    /// </summary>
    public static JgsValue ComplexNum(Complex value) =>
        value.Imaginary == 0.0 ? Number(value.Real) : new(JgsType.Complex, 0, value);

    /// <summary>Returns the shared boolean value for <paramref name="value"/>.</summary>
    public static JgsValue Bool(bool value) => value ? True : False;

    /// <summary>Wraps a string.</summary>
    public static JgsValue Str(string value) => new(JgsType.String, 0, value);

    /// <summary>Wraps an array (the array is used directly, not copied).</summary>
    public static JgsValue Array(JgsValue[] elements) => new(JgsType.Array, 0, elements);

    /// <summary>
    /// Wraps a packed numeric buffer as an array value. The buffer must be freshly created for this
    /// wrapper: the single-wrapper invariant (one <see cref="JgsValue"/> per buffer, ever) is what
    /// keeps aliasing and in-place demotion correct.
    /// </summary>
    public static JgsValue Packed(NumericBuffer buffer, JgsPackedKind kind = JgsPackedKind.Number) =>
        new(JgsType.Array, 0, buffer, kind);

    /// <summary>
    /// Wraps a packed complex array (planar re/im). The same single-wrapper invariant applies to
    /// the payload and both of its planes.
    /// </summary>
    public static JgsValue PackedComplexArray(JgsPackedComplex payload) =>
        new(JgsType.Array, 0, payload);

    /// <summary>
    /// Wraps a packed buffer as a <paramref name="rows"/>-by-<paramref name="cols"/> matrix. Elements
    /// are stored column-major, which is what MATLAB means by linear order: element <c>k</c> of
    /// <c>A(:)</c> is <c>A(k % rows, k / rows)</c>, so <c>A(:)</c> is a buffer clone rather than a
    /// gather and a MAT-file's own column-major payload writes straight out.
    /// </summary>
    public static JgsValue Shaped(NumericBuffer buffer, int rows, int cols, JgsPackedKind kind = JgsPackedKind.Number) =>
        new(JgsType.Array, buffer, kind, rows, cols);

    /// <summary>Wraps a boxed element array as a column-major matrix (the array is used directly).</summary>
    public static JgsValue Shaped(JgsValue[] elements, int rows, int cols) =>
        new(JgsType.Array, elements, JgsPackedKind.Number, rows, cols);

    /// <summary>Wraps a packed planar complex payload as a column-major matrix.</summary>
    public static JgsValue ShapedComplex(JgsPackedComplex payload, int rows, int cols) =>
        new(JgsType.Array, payload, JgsPackedKind.Number, rows, cols);

    /// <summary>Wraps a data table.</summary>
    public static JgsValue Table(Table table) => new(JgsType.Table, 0, table);

    /// <summary>
    /// Wraps an image. The buffer is used directly (not copied); the single-wrapper convention means
    /// each image value owns its <see cref="ImageBuffer"/>, which the runtime disposes when the value
    /// leaves a completed run's locals.
    /// </summary>
    public static JgsValue Image(ImageBuffer image) => new(JgsType.Image, 0, image);

    /// <summary>Wraps a callable function.</summary>
    public static JgsValue Function(IJgsCallable callable) => new(JgsType.Function, 0, callable);

    /// <summary>
    /// Wraps a sparse matrix. <see cref="JGraph.Numerics.Sparse.CscMatrix"/> is immutable, so
    /// bindings share the instance freely — no copy-on-assign bookkeeping is needed.
    /// </summary>
    public static JgsValue Sparse(JGraph.Numerics.Sparse.CscMatrix matrix) => new(JgsType.Sparse, 0, matrix);

    /// <summary>The sparse payload (valid only for <see cref="JgsType.Sparse"/>).</summary>
    public JGraph.Numerics.Sparse.CscMatrix AsSparse => (JGraph.Numerics.Sparse.CscMatrix)_reference!;

    /// <summary>
    /// Wraps a cell array (the array is used directly, not copied). Like an ordinary array it is
    /// mutable in place, so aliases in JGS see each other's writes; MATLAB copies on assignment.
    /// </summary>
    public static JgsValue Cell(JgsValue[] elements) => new(JgsType.Cell, 0, elements);

    /// <summary>Wraps a struct's fields (the dictionary is used directly, not copied).</summary>
    public static JgsValue Struct(Dictionary<string, JgsValue> fields) =>
        new(JgsType.Struct, new JgsStructArray([fields]), JgsPackedKind.Number, 1, 1);

    /// <summary>An empty struct, ready for fields to be assigned.</summary>
    public static JgsValue EmptyStruct() => Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal));

    /// <summary>Wraps an instance of a user class (M68). The instance is held, not copied.</summary>
    public static JgsValue Object(JgsObject instance) => new(JgsType.Object, 0, instance);

    /// <summary>The object payload (valid only for <see cref="JgsType.Object"/>).</summary>
    public JgsObject AsObject => (JgsObject)_reference!;

    /// <summary>
    /// Wraps a struct array as a <paramref name="rows"/>-by-<paramref name="cols"/> value (M65). The
    /// payload is used directly, so a caller holding it can write an element's field and the value
    /// sees the write — the reference semantics <c>S(k).f = v</c> depends on.
    /// </summary>
    public static JgsValue StructArray(JgsStructArray elements, int rows, int cols) =>
        new(JgsType.Struct, elements, JgsPackedKind.Number, rows, cols);

    /// <summary>A struct array of the given elements, as a row.</summary>
    public static JgsValue StructArray(Dictionary<string, JgsValue>[] elements) =>
        StructArray(new JgsStructArray(elements), elements.Length == 0 ? 0 : 1, elements.Length);

    /// <summary>The cell array's elements (valid only for <see cref="JgsType.Cell"/>).</summary>
    public JgsValue[] AsCell => (JgsValue[])_reference!;

    /// <summary>The struct payload (valid only for <see cref="JgsType.Struct"/>).</summary>
    public JgsStructArray AsStructArray => (JgsStructArray)_reference!;

    /// <summary>
    /// The struct's fields, in insertion order — element one's for a struct array.
    /// </summary>
    /// <remarks>
    /// Reading the first element is what the ~60 call sites that predate M65 already did, because a
    /// struct array was a cell and they asked its first entry; every one of them is testing whether
    /// an options bag or a tagged struct carries a field, and every element carries the same fields.
    /// The places where the difference between one struct and many genuinely matters — the field
    /// write, the field read, <c>class</c>, <c>numel</c>, display — check
    /// <see cref="IsStructArray"/> first and never arrive here.
    /// <para>
    /// An empty struct array answers with a fresh dictionary carrying its field names, so a reader
    /// sees the shape an element would have had. Writes into that dictionary go nowhere, which is
    /// why nothing writes through this: the write paths hold the payload itself.
    /// </para>
    /// </remarks>
    public Dictionary<string, JgsValue> AsStruct
    {
        get
        {
            JgsStructArray payload = AsStructArray;
            return payload.Length > 0 ? payload.Elements[0] : payload.NewElement();
        }
    }

    /// <summary>Whether this is a struct value that is not a 1-by-1 (M65).</summary>
    public bool IsStructArray => Type == JgsType.Struct && AsStructArray.Length != 1;

    /// <summary>The numeric value (valid for <see cref="JgsType.Number"/> and <see cref="JgsType.Bool"/>).</summary>
    public double AsNumber => _number;

    /// <summary>The boolean value.</summary>
    public bool AsBool => _number != 0;

    /// <summary>The complex value (valid for <see cref="JgsType.Complex"/>; a Number reads as re+0i).</summary>
    public Complex AsComplex => Type == JgsType.Complex ? (Complex)_reference! : new Complex(_number, 0);

    /// <summary>The string value.</summary>
    public string AsString => (string)_reference!;

    /// <summary>
    /// The backing array (mutable in place for indexed assignment). Throws for a packed array —
    /// callers that can meet a packed value use <see cref="BoxedElements"/>, <see cref="ElementAt"/>,
    /// or <see cref="ArrayLength"/> instead, so an unmigrated call site fails loudly.
    /// </summary>
    public JgsValue[] AsArray => _reference is NumericBuffer or JgsPackedComplex
        ? throw new InvalidOperationException("A packed array was accessed as boxed elements — this call site must use BoxedElements/ElementAt/ArrayLength.")
        : (JgsValue[])_reference!;

    /// <summary>
    /// Compacts a growth-capacity buffer (see <see cref="TryGrowInPlace"/>) back to exactly
    /// Rows*Cols. This is the guard that lets every raw-buffer consumer stay ignorant of capacity:
    /// the only way to reach the buffer is through <see cref="AsBuffer"/>, and by then it is compact.
    /// </summary>
    private void CompactInPlace()
    {
        var strided = (NumericBuffer)_reference!;
        NumericBuffer compact = JgsPacking.Allocate(_rows * _cols);
        Span<double> source = strided.AsSpan();
        Span<double> destination = compact.AsSpan();
        for (int c = 0; c < _cols; c++)
        {
            source.Slice(c * _strideRows, _rows).CopyTo(destination.Slice(c * _rows, _rows));
        }

        GC.KeepAlive(strided);
        _reference = compact;
        _strideRows = 0;
        strided.Dispose();
    }

    /// <summary>The storage slot of logical column-major element <paramref name="index"/>.</summary>
    private int StorageSlot(int index) =>
        _strideRows == 0 ? index : (index % _rows) + (index / _rows * _strideRows);

    /// <summary>
    /// Writes logical element <paramref name="index"/> of a packed real buffer, capacity-aware —
    /// the indexed-assignment path must not go through <see cref="AsBuffer"/>, whose compaction
    /// guard would undo the amortized growth it exists to make fast.
    /// </summary>
    internal void SetPackedNumber(int index, double value)
    {
        var buffer = (NumericBuffer)_reference!;
        buffer.AsSpan()[StorageSlot(index)] = value;
    }

    /// <summary>
    /// Grows the logical shape in place to <paramref name="newRows"/>-by-<paramref name="newCols"/>,
    /// zero-filling, with geometric over-allocation so a loop that grows a matrix one row and column
    /// at a time costs amortized O(1) per element instead of a full copy per step. Only valid for a
    /// packed real buffer; anything else returns false and the caller rebuilds the slow way. The
    /// caller owns the semantics question: mutating in place is only safe where the wrapper is
    /// uniquely owned, which MATLAB's copy-on-assign guarantees and JGS's reference semantics do not.
    /// </summary>
    internal bool TryGrowInPlace(int newRows, int newCols)
    {
        if (_reference is not NumericBuffer buffer || Type != JgsType.Array || _dims is not null)
        {
            return false;
        }

        int stride = _strideRows == 0 ? _rows : _strideRows;
        int capCols = stride == 0 ? 0 : buffer.Length / stride;
        if (_strideRows > 0 && newRows <= stride && newCols <= capCols)
        {
            // Fits in the slack. The buffer was zero-filled when the capacity was allocated and
            // logical writes never touch the slack, so the newly exposed cells are already zero.
            _rows = newRows;
            _cols = newCols;
            return true;
        }

        // Only a dimension that is actually growing earns slack — a row vector must not be handed
        // spare rows it will never use.
        int capRows = newRows <= stride ? stride : GrownDimension(newRows, stride);
        int grownCols = newCols <= capCols ? capCols : GrownDimension(newCols, capCols);
        if ((long)capRows * grownCols > 64_000_000)
        {
            // Past ~512 MB the slack itself is the problem; fall back to exact dimensions.
            capRows = newRows;
            grownCols = newCols;
            if ((long)capRows * grownCols > int.MaxValue)
            {
                return false;
            }
        }

        NumericBuffer grown = JgsPacking.Allocate(capRows * grownCols);
        Span<double> destination = grown.AsSpan();
        destination.Clear();
        Span<double> source = buffer.AsSpan();
        for (int c = 0; c < _cols; c++)
        {
            source.Slice(c * stride, _rows).CopyTo(destination.Slice(c * capRows, _rows));
        }

        GC.KeepAlive(buffer);
        _reference = grown;
        _strideRows = capRows == newRows && grown.Length == newRows * newCols ? 0 : capRows;
        _rows = newRows;
        _cols = newCols;
        buffer.Dispose();
        return true;
    }

    /// <summary>Half-again growth with a small floor, so tiny matrices do not realloc per step.</summary>
    private static int GrownDimension(int needed, int current) =>
        System.Math.Max(needed, current + (current >> 1) + 8);

    /// <summary>Whether this array value is backed by a packed real-number buffer.</summary>
    public bool IsPacked => _reference is NumericBuffer;

    /// <summary>Whether this array value is backed by a packed planar complex payload.</summary>
    public bool IsPackedComplex => _reference is JgsPackedComplex;

    /// <summary>
    /// Whether these two values are backed by the same storage — the same buffer, the same complex
    /// payload, or the same boxed element array — so that a write through one would be seen through
    /// the other.
    /// </summary>
    /// <remarks>
    /// Asked by <see cref="Interpreter.CopyForBinding"/>'s elision (M109) to prove an operator's
    /// answer is not its own operand wearing a new wrapper. It reads <c>_reference</c> directly
    /// rather than through <see cref="AsBuffer"/> on purpose: <c>AsBuffer</c> compacts growth
    /// capacity, which reallocates, and a question about identity must not move anything.
    /// </remarks>
    public bool SharesStorageWith(JgsValue other)
    {
        if (_reference is null || other._reference is null)
        {
            return false;
        }

        if (ReferenceEquals(_reference, other._reference))
        {
            return true;
        }

        // A complex value's storage is two buffers behind one payload object, so two distinct
        // payloads can still be one plane apiece of the same numbers.
        NumericBuffer? mineReal = _reference as NumericBuffer;
        NumericBuffer? theirsReal = other._reference as NumericBuffer;
        JgsPackedComplex? minePlanes = _reference as JgsPackedComplex;
        JgsPackedComplex? theirsPlanes = other._reference as JgsPackedComplex;

        if (minePlanes is not null && theirsPlanes is not null)
        {
            return ReferenceEquals(minePlanes.Re, theirsPlanes.Re) || ReferenceEquals(minePlanes.Re, theirsPlanes.Im)
                || ReferenceEquals(minePlanes.Im, theirsPlanes.Re) || ReferenceEquals(minePlanes.Im, theirsPlanes.Im);
        }

        if (minePlanes is not null && theirsReal is not null)
        {
            return ReferenceEquals(minePlanes.Re, theirsReal) || ReferenceEquals(minePlanes.Im, theirsReal);
        }

        if (theirsPlanes is not null && mineReal is not null)
        {
            return ReferenceEquals(theirsPlanes.Re, mineReal) || ReferenceEquals(theirsPlanes.Im, mineReal);
        }

        return false;
    }

    /// <summary>
    /// The packed buffer (valid only when <see cref="IsPacked"/>). Compacts growth capacity first,
    /// so raw-buffer consumers always see exactly Rows*Cols column-major elements.
    /// </summary>
    public NumericBuffer AsBuffer
    {
        get
        {
            if (_strideRows > 0)
            {
                CompactInPlace();
            }

            return (NumericBuffer)_reference!;
        }
    }

    /// <summary>The packed complex payload (valid only when <see cref="IsPackedComplex"/>).</summary>
    public JgsPackedComplex AsPackedComplex => (JgsPackedComplex)_reference!;

    /// <summary>The element kind of a packed array (valid only when <see cref="IsPacked"/>).</summary>
    public JgsPackedKind PackedKind => _packedKind;

    /// <summary>Element count of an array value, packed or boxed. Growth capacity does not count.</summary>
    public int ArrayLength => _reference switch
    {
        NumericBuffer buffer => _strideRows > 0 ? _rows * _cols : buffer.Length,
        JgsPackedComplex complex => complex.Length,

        // A struct array counts its elements too (M82). It reached the cast below and threw, which
        // nothing noticed while every caller was asking about a numeric array — and stopped being
        // true the moment a calendarDuration wore a time tag over struct storage.
        JgsStructArray structs => structs.Length,
        _ => AsArray.Length,
    };

    /// <summary>Row count of an array value. A value built without a shape is a row, so this is 1.</summary>
    public int Rows => _rows;

    /// <summary>Column count of an array value; <c>Rows * Cols</c> is always <see cref="ArrayLength"/>.</summary>
    public int Cols => _cols;

    /// <summary>
    /// Whether this array is a matrix rather than a row: more than one row, which is the only case
    /// where the distinction between linear and two-subscript indexing can be observed.
    /// </summary>
    public bool IsShaped => _rows != 1;

    /// <summary>Whether this array carries three or more dimensions.</summary>
    public bool IsNd => _dims is not null;

    /// <summary>How many dimensions the array has (2 for everything that is not N-D).</summary>
    public int DimCount => _dims?.Length ?? 2;

    /// <summary>The array's size per dimension; a fresh copy, safe for callers to keep.</summary>
    public int[] Dims => _dims is int[] dims ? (int[])dims.Clone() : [_rows, _cols];

    /// <summary>
    /// Changes the shape in place to an arbitrary dimension list (column-major order unchanged).
    /// Trailing singleton dimensions beyond the second are trimmed, so <c>reshape(x, [n 1 1])</c>
    /// is the n-by-1 column it means; two significant dimensions land back in the plain 2-D shape.
    /// </summary>
    public void ReshapeDims(IReadOnlyList<int> size)
    {
        int significant = size.Count;
        while (significant > 2 && size[significant - 1] == 1)
        {
            significant--;
        }

        if (significant <= 2)
        {
            int rows = significant > 0 ? size[0] : 1;
            int cols = significant > 1 ? size[1] : 1;
            Reshape(rows, cols);
            return;
        }

        if (_strideRows > 0)
        {
            CompactInPlace();
        }

        long product = 1;
        var dims = new int[significant];
        for (int i = 0; i < significant; i++)
        {
            dims[i] = size[i];
            product *= size[i];
        }

        if (product != ArrayLength)
        {
            throw new InvalidOperationException(
                $"A {string.Join("x", dims)} shape does not describe {ArrayLength} elements.");
        }

        _dims = dims;
        _rows = dims[0];
        int fold = 1;
        for (int i = 1; i < significant; i++)
        {
            fold *= dims[i];
        }

        _cols = fold;
    }

    /// <summary>
    /// The numeric class this value carries — <see cref="JgsNumericClass.Double"/> unless a class
    /// constructor, an arithmetic result or an indexing read stamped it (M47).
    /// </summary>
    public JgsNumericClass NumericClass => _numericClass;

    /// <summary>
    /// Records the numeric class of a freshly-minted value. Mint-time only: a value already bound to
    /// a name must never change class under it, so every caller stamps a wrapper it has just built.
    /// </summary>
    public void SetNumericClass(JgsNumericClass numericClass) => _numericClass = numericClass;

    /// <summary>
    /// The class a struct answers to when it stands in for a MATLAB object, or null for the ordinary
    /// case. Only <c>class</c> and <c>isa</c> read it; everything else treats the value as the struct
    /// it is, which is exactly why <c>ME.message</c> needed no special case to work.
    /// </summary>
    /// <remarks>
    /// An object answers with its own class rather than with the tag, so that every reader of this
    /// property — <c>class</c>, <c>isa</c>, the handle-class rule, the error messages — learnt about
    /// user classes the moment the type existed, without any of them being edited (M68).
    /// </remarks>
    public string? ClassName => _reference is JgsObject instance ? instance.Class.Name : _className;

    /// <summary>Records the class name of a freshly-minted struct. Mint-time only, like <see cref="SetNumericClass"/>.</summary>
    public void SetClassName(string? className) => _className = className;

    /// <summary>
    /// Whether this value is a MATLAB string array (M63): an <see cref="JgsType.Array"/> of
    /// <see cref="JgsType.String"/> elements that remembers being written with double quotes. A string
    /// <em>scalar</em> is the 1-by-1 case, which is MATLAB's own model rather than a convenience —
    /// <c>numel("abc")</c> is 1 because the string is one element, where <c>numel('abc')</c> is 3
    /// because the char row is three of them.
    /// </summary>
    public bool IsStringArray => _isStringArray;

    /// <summary>
    /// Marks a freshly-minted array as a string array and hands it back, so a mint site reads as one
    /// expression. Mint-time only, like <see cref="SetNumericClass"/>: a value already bound to a name
    /// must never change class under it.
    /// </summary>
    public JgsValue MarkStringArray()
    {
        _isStringArray = true;
        return this;
    }

    /// <summary>
    /// A string scalar: the 1-by-1 string array a double-quoted literal means. Every call site that
    /// wants MATLAB's <c>string("x")</c> goes through here rather than building the array by hand, so
    /// the shape and the tag can never disagree.
    /// </summary>
    public static JgsValue StringScalar(string text) => Array([Str(text)]).MarkStringArray();

    /// <summary>
    /// Whether this value is a MATLAB char matrix (M105): a 2-D array of <em>code points</em>, one
    /// element per character, which is what <c>char('a', 'bcd')</c> and <c>['ab'; 'cd']</c> build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A char <em>row</em> is <see cref="JgsType.String"/> and always was — the whole text surface is
    /// built on it. This tag is only for the stack of them, and it says the elements underneath are
    /// characters rather than numbers. Storage is an ordinary numeric array, exactly as it is for the
    /// integer classes, which is what makes <c>A(2, 3)</c>, <c>A(:)</c>, <c>A'</c>, <c>double(A)</c>,
    /// <c>A == ' '</c> and <c>size(A)</c> all correct without a line of their own: they are the array
    /// machinery that was already there, reading a real 2-D shape.
    /// </para>
    /// <para>
    /// Before M105 a char matrix was an N-by-1 array of char <em>rows</em>, which is why
    /// <c>class</c> answered <c>double</c> and <c>size</c> answered N-by-1: nothing was 2-D about it,
    /// and <c>A(:, 2)</c> raised an index error rather than answering a column.
    /// </para>
    /// </remarks>
    public bool IsCharMatrix => _isCharMatrix;

    /// <summary>
    /// Marks a freshly-minted array as a char matrix and hands it back. Mint-time only, like
    /// <see cref="MarkStringArray"/>.
    /// </summary>
    public JgsValue MarkCharMatrix()
    {
        _isCharMatrix = true;
        return this;
    }

    /// <summary>
    /// The char matrix over <paramref name="rows"/>, space-padded to the longest — MATLAB's own rule,
    /// and the only way a stack of unequal rows can be rectangular. Every mint site goes through here
    /// rather than building the array by hand, so the shape, the padding and the tag cannot disagree.
    /// </summary>
    public static JgsValue CharMatrix(string[] rows)
    {
        int width = 0;
        foreach (string row in rows)
        {
            width = System.Math.Max(width, row.Length);
        }

        // Column-major, which is the storage every other array uses: element (r, c) sits at c*N + r.
        var codes = new double[rows.Length * width];
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            for (int c = 0; c < width; c++)
            {
                codes[(c * rows.Length) + r] = c < row.Length ? row[c] : ' ';
            }
        }

        JgsValue matrix = JgsMatrix.FromColumnMajor(codes, rows.Length, width);
        matrix.Reshape(rows.Length, width);
        return matrix.MarkCharMatrix();
    }

    /// <summary>
    /// A char matrix read in storage order, which is the column-major run of its characters — what
    /// <c>A(:)'</c> spells, and what <c>fprintf('%s', A)</c> prints. For <c>['a  '; 'bcd']</c> that
    /// is <c>"ab c d"</c> and not either of the rows.
    /// </summary>
    public string CharMatrixText()
    {
        int count = ArrayLength;
        var run = new char[count];
        for (int i = 0; i < count; i++)
        {
            run[i] = (char)(int)ElementAt(i).AsNumber;
        }

        return new string(run);
    }

    /// <summary>The rows of a char matrix, read back as text — the inverse of <see cref="CharMatrix"/>.</summary>
    public string[] CharMatrixRows()
    {
        int height = Rows;
        int width = Cols;
        var rows = new string[height];
        for (int r = 0; r < height; r++)
        {
            var row = new char[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = (char)(int)ElementAt((c * height) + r).AsNumber;
            }

            rows[r] = new string(row);
        }

        return rows;
    }

    /// <summary>A string array over <paramref name="elements"/> (each of which must be a string).</summary>
    public static JgsValue StringArray(JgsValue[] elements) => Array(elements).MarkStringArray();

    /// <summary>A string array laid out column-major as <paramref name="rows"/>-by-<paramref name="cols"/>.</summary>
    public static JgsValue StringArray(JgsValue[] elements, int rows, int cols) =>
        Shaped(elements, rows, cols).MarkStringArray();

    /// <summary>
    /// What kind of time this value holds, or null when it is not a time at all (M64). A datetime and
    /// a duration are both an ordinary numeric array of milliseconds underneath — every one of
    /// indexing, growth, reshaping, masks and concatenation is the array machinery that was already
    /// there — so this tag is the whole of what makes one a time.
    /// </summary>
    public JgsTimeTag? TimeTag => _time;

    /// <summary>Whether this value is a datetime or a duration.</summary>
    public bool IsTime => _time is not null;

    /// <summary>Whether this value is a <c>datetime</c>.</summary>
    public bool IsDatetime => _time is { Kind: JgsTimeKind.Datetime };

    /// <summary>Whether this value is a <c>duration</c>.</summary>
    public bool IsDuration => _time is { Kind: JgsTimeKind.Duration };

    /// <summary>
    /// Marks a freshly-minted array as a time and hands it back. Mint-time only, like
    /// <see cref="MarkStringArray"/> and <see cref="SetNumericClass"/>.
    /// </summary>
    public JgsValue MarkTime(JgsTimeTag tag)
    {
        _time = tag;
        return this;
    }

    /// <summary>Gives this array the shape (2-D or N-D) of <paramref name="source"/>.</summary>
    internal void TakeShapeOf(JgsValue source)
    {
        if (source._dims is int[] dims)
        {
            ReshapeDims(dims);
        }
        else
        {
            Reshape(source._rows, source._cols);
        }
    }

    /// <summary>
    /// Changes the shape in place, keeping the elements and their column-major order. Every alias
    /// shares this wrapper (single-wrapper invariant), so all names see the new shape.
    /// </summary>
    public void Reshape(int rows, int cols)
    {
        if (_strideRows > 0)
        {
            CompactInPlace();
        }

        int count = ArrayLength;
        if ((long)rows * cols != count)
        {
            throw new InvalidOperationException($"A {rows}x{cols} shape does not describe {count} elements.");
        }

        _dims = null; // a two-dimensional reshape flattens away any higher shape
        _rows = rows;
        _cols = cols;
    }

    /// <summary>The column-major position of <c>(row, col)</c> in this array's storage.</summary>
    public int LinearIndex(int row, int col) => row + (col * _rows);

    /// <summary>Element <paramref name="index"/> of an array value, packed or boxed (0-based).</summary>
    public JgsValue ElementAt(int index)
    {
        switch (_reference)
        {
            case NumericBuffer buffer:
                double raw = buffer.AsSpan()[StorageSlot(index)];
                return _packedKind == JgsPackedKind.Bool ? Bool(raw != 0) : Number(raw);
            case JgsPackedComplex complex:
                // ComplexNum normalizes zero-imaginary entries to numbers, matching the mixed
                // Number/Complex elements the boxed representation holds.
                return ComplexNum(new Complex(complex.Re.AsSpan()[index], complex.Im.AsSpan()[index]));
            default:
                return AsArray[index];
        }
    }

    /// <summary>
    /// The elements of an array value as a <c>JgsValue[]</c>: the live backing array when boxed, a
    /// fresh materialized copy when packed. Read-only use only — writes to a materialized copy are
    /// lost, which is exactly the bug the throwing <see cref="AsArray"/> exists to surface.
    /// </summary>
    public JgsValue[] BoxedElements() =>
        _reference is NumericBuffer or JgsPackedComplex ? MaterializeBoxed() : AsArray;

    /// <summary>A fresh boxed copy of a packed array's elements (the packed form is untouched).</summary>
    public JgsValue[] MaterializeBoxed()
    {
        if (_reference is JgsPackedComplex complex)
        {
            var boxed = new JgsValue[complex.Length];
            Span<double> re = complex.Re.AsSpan();
            Span<double> im = complex.Im.AsSpan();
            for (int i = 0; i < boxed.Length; i++)
            {
                boxed[i] = ComplexNum(new Complex(re[i], im[i]));
            }

            GC.KeepAlive(complex);
            return boxed;
        }

        var buffer = (NumericBuffer)_reference!;
        Span<double> span = buffer.AsSpan();
        int count = ArrayLength; // logical: growth capacity must not leak into the boxed copy
        var elements = new JgsValue[count];
        if (_packedKind == JgsPackedKind.Bool)
        {
            for (int i = 0; i < count; i++)
            {
                elements[i] = Bool(span[StorageSlot(i)] != 0);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                elements[i] = Number(span[StorageSlot(i)]);
            }
        }

        GC.KeepAlive(buffer);
        return elements;
    }

    /// <summary>
    /// Converts a packed array to boxed in place, e.g. when a script writes a non-numeric value into
    /// one of its slots. Every alias shares this wrapper (single-wrapper invariant), so all names see
    /// the demoted array; the backing storage is disposed. No-op for already-boxed arrays. The shape
    /// rides on the wrapper, not the storage, so it survives the swap untouched.
    /// </summary>
    public void DemoteToBoxed()
    {
        if (_reference is NumericBuffer buffer)
        {
            _reference = MaterializeBoxed(); // stride-aware, so the boxed copy is exactly logical
            _strideRows = 0;
            buffer.Dispose();
        }
        else if (_reference is JgsPackedComplex complex)
        {
            _reference = MaterializeBoxed();
            complex.Dispose();
        }
    }

    /// <summary>The table value.</summary>
    public Table AsTable => (Table)_reference!;

    /// <summary>The image value.</summary>
    public ImageBuffer AsImage => (ImageBuffer)_reference!;

    /// <summary>The callable value.</summary>
    public IJgsCallable AsCallable => (IJgsCallable)_reference!;

    /// <summary>
    /// Whether the value is considered true in a boolean context. An array is truthy only when it is
    /// non-empty and every element is truthy (MATLAB semantics), so `if mask { … }` asks "all matched?"
    /// rather than "is the mask non-empty?". Use `length(a) &gt; 0` to test emptiness.
    /// </summary>
    public bool IsTruthy => Type switch
    {
        JgsType.Null => false,
        JgsType.Bool => _number != 0,
        JgsType.Number => _number != 0,
        JgsType.Complex => true, // zero-imaginary values normalize to Number, so any Complex is nonzero
        JgsType.String => AsString.Length > 0,
        JgsType.Array => _reference switch
        {
            // AsBuffer (not the pattern variable) so growth capacity is compacted out of the fold.
            NumericBuffer => PackedMath.AllNonZero(AsBuffer), // empty false, NaN nonzero — the boxed fold
            JgsPackedComplex complex => AllComplexNonZero(complex),
            _ => AllTruthy(AsArray),
        },
        _ => true,
    };

    /// <summary>An element is falsy only when both planes are exactly zero (it reads as Number 0).</summary>
    private static bool AllComplexNonZero(JgsPackedComplex complex)
    {
        if (complex.Length == 0)
        {
            return false;
        }

        Span<double> re = complex.Re.AsSpan();
        Span<double> im = complex.Im.AsSpan();
        for (int i = 0; i < re.Length; i++)
        {
            if (re[i] == 0 && im[i] == 0)
            {
                return false;
            }
        }

        GC.KeepAlive(complex);
        return true;
    }

    private static bool AllTruthy(JgsValue[] elements)
    {
        if (elements.Length == 0)
        {
            return false;
        }

        foreach (JgsValue element in elements)
        {
            if (!element.IsTruthy)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Shallow value equality, the semantics of scalar <c>==</c>: by value for numbers, logicals and
    /// strings, reference identity for arrays, tables and functions. Values of unrelated types are
    /// unequal, never an error. See the <c>isequal</c> builtin for deep equality.
    /// </summary>
    /// <remarks>
    /// A logical compares equal to the number it stands for, so <c>true == 1</c> — MATLAB treats
    /// logicals as numeric, ordering comparisons here already did, and a mask that could not be
    /// checked against <c>[1 0]</c> was the sharpest edge left in the model. NaN equals nothing,
    /// itself included, which is what every other language and MATLAB both say.
    /// </remarks>
    public static bool AreEqual(JgsValue left, JgsValue right)
    {
        if (left.Type is JgsType.Number or JgsType.Bool && right.Type is JgsType.Number or JgsType.Bool)
        {
            return left._number == right._number; // '==' on doubles, so NaN is unequal to itself
        }

        if (left.Type != right.Type)
        {
            return false;
        }

        return left.Type switch
        {
            JgsType.Null => true,
            JgsType.Complex => left.AsComplex.Equals(right.AsComplex),
            JgsType.String => string.Equals(left.AsString, right.AsString, StringComparison.Ordinal),
            _ => ReferenceEquals(left, right),
        };
    }

    /// <summary>The user-facing name of the value's type, for error messages.</summary>
    public string TypeName => Type switch
    {
        JgsType.Null => "null",
        JgsType.Number => "number",
        JgsType.Complex => "complex",
        JgsType.Bool => "bool",
        JgsType.String => "string",
        JgsType.Array when _time is { Kind: JgsTimeKind.Datetime } => "datetime",
        JgsType.Array when _time is not null => "duration",
        JgsType.Array => _isStringArray ? "string array" : "array",
        JgsType.Table => "table",
        JgsType.Image => "image",
        JgsType.Function => "function",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Sparse => "sparse",
        JgsType.Object => AsObject.Class.Name,
        _ => "value",
    };

    /// <summary>Formats the value for <c>print</c> and string concatenation.</summary>
    public string Display() => Type switch
    {
        JgsType.Null => "null",
        JgsType.Number => FormatNumber(_number),
        JgsType.Complex => FormatComplex(AsComplex),
        JgsType.Bool => _number != 0 ? "true" : "false",
        JgsType.String => AsString,
        JgsType.Array when _time is not null => FormatTime(this),
        JgsType.Array when _isCharMatrix => FormatCharMatrix(this),
        JgsType.Array => _isStringArray ? FormatStringArray(this) : FormatArray(this),
        JgsType.Table => $"table[{AsTable.RowCount}x{AsTable.ColumnCount}]",
        JgsType.Image => FormatImage(AsImage),
        JgsType.Function => $"fn {AsCallable.Name}",
        JgsType.Cell => FormatCell(AsCell),

        // A calendarDuration is a struct array wearing a time tag (M82), and it shows itself as the
        // length of time it is rather than as the three numbers it keeps that length in.
        JgsType.Struct when _time is not null => FormatTime(this),
        JgsType.Struct => FormatStructValue(this),
        JgsType.Sparse => FormatSparse(AsSparse),
        JgsType.Object => FormatObject(AsObject),
        _ => "value",
    };

    /// <summary>
    /// Formats an object as its class name followed by its properties, which is what MATLAB shows for
    /// a class that does not define its own <c>disp</c>. A class that does define one is displayed by
    /// that method instead, and the interpreter asks it before it ever reaches here.
    /// </summary>
    private static string FormatObject(JgsObject instance)
    {
        var sb = new StringBuilder(instance.Class.Name);
        sb.Append(" with properties:");
        foreach (ClassProperty property in instance.Class.Properties)
        {
            if (!instance.Fields.TryGetValue(property.Spec.Name, out JgsValue? held))
            {
                continue;
            }

            sb.Append("\n    ").Append(property.Spec.Name).Append(": ").Append(Truncate(held.Display()));
        }

        return sb.ToString();
    }

    /// <summary>One line of a property's value, shortened when it is longer than a display wants.</summary>
    private static string Truncate(string text)
    {
        string line = text.ReplaceLineEndings(" ");
        return line.Length <= 60 ? line : string.Concat(line.AsSpan(0, 57), "...");
    }

    /// <summary>Formats a sparse matrix the way MATLAB does: one <c>(r,c)  v</c> line per nonzero.</summary>
    private static string FormatSparse(JGraph.Numerics.Sparse.CscMatrix matrix)
    {
        var sb = new StringBuilder();
        sb.Append(matrix.Rows).Append('x').Append(matrix.Cols)
          .Append(" sparse double, ").Append(matrix.NonZeroCount).Append(" nonzeros");
        int shown = 0;
        for (int c = 0; c < matrix.Cols && shown < DisplayMaxElements; c++)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1] && shown < DisplayMaxElements; i++)
            {
                sb.Append("\n   (").Append(matrix.RowIndices[i] + 1).Append(',').Append(c + 1)
                  .Append(")  ").Append(FormatNumber(matrix.Values[i]));
                shown++;
            }
        }

        if (shown < matrix.NonZeroCount)
        {
            sb.Append("\n   ... (").Append(matrix.NonZeroCount - shown).Append(" more)");
        }

        return sb.ToString();
    }

    /// <summary>Formats a cell array as MATLAB writes one: <c>{1, 'two'}</c>, capped like an array.</summary>
    private static string FormatCell(JgsValue[] elements)
    {
        var sb = new StringBuilder("{");
        int shown = Math.Min(elements.Length, DisplayMaxElements);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(elements[i].Type == JgsType.String ? $"'{elements[i].AsString}'" : elements[i].Display());
        }

        if (shown < elements.Length)
        {
            sb.Append(", … (").Append(elements.Length).Append(" elements)");
        }

        return sb.Append('}').ToString();
    }

    /// <summary>
    /// A scalar struct writes its fields; anything else writes its size and field names, the way
    /// MATLAB does — dumping every element of a thousand-region <c>regionprops</c> result is not a
    /// display, it is a wall.
    /// </summary>
    private static string FormatStructValue(JgsValue value)
    {
        JgsStructArray payload = value.AsStructArray;
        if (payload.Length == 1)
        {
            return FormatStruct(payload.Elements[0]);
        }

        string size = string.Join('x', value.Dims);
        string[] fields = payload.FieldNames;
        return fields.Length == 0
            ? $"{size} struct array with no fields"
            : $"{size} struct array with fields: {string.Join(", ", fields)}";
    }

    private static string FormatStruct(Dictionary<string, JgsValue> fields)
    {
        var sb = new StringBuilder("struct(");
        bool first = true;
        foreach ((string name, JgsValue value) in fields)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(name).Append(": ").Append(value.Display());
        }

        return sb.Append(')').ToString();
    }

    /// <summary>A constant-size label like <c>image[480x640x3]</c> — never dumps pixels.</summary>
    private static string FormatImage(ImageBuffer image) => image.Channels == 1
        ? $"image[{image.Height}x{image.Width}]"
        : $"image[{image.Height}x{image.Width}x{image.Channels}]";

    /// <summary>
    /// One number as display text. The three values that have a name rather than digits are spelled
    /// the way MATLAB spells them — <c>Inf</c>, <c>-Inf</c>, <c>NaN</c> — and everything else is laid
    /// out by whichever precision <c>format</c> has selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET calls the first two "Infinity" and "-Infinity", and every one of the precision modes would
    /// have handed that spelling straight through, because a custom numeric format string answers the
    /// culture's symbol for a non-finite double rather than laying out digits. So <c>x = Inf</c>
    /// echoed <c>x = Infinity</c> while <c>sprintf</c> and <c>num2str</c> in the same session wrote
    /// <c>Inf</c> — one program with two spellings for one value. This is the single funnel every
    /// display reaches, so naming them here covers the echo, <c>disp</c>, an array, a cell, a struct
    /// field, a sparse entry and both halves of a complex number at once.
    /// </para>
    /// <para>
    /// It belongs here and not one level down in <see cref="JgsNumberFormat.Format"/>, even though
    /// that is where the precision modes live, because <c>writematrix</c> and <c>writecell</c> share
    /// that helper to write a CSV — and this program's own <c>readmatrix</c> parses ".NET"'s
    /// "Infinity" and not "Inf", so naming them there would write a file JGraph could no longer read
    /// back. That the file also disagrees with MATLAB, which writes and reads <c>Inf</c>, is a
    /// separate defect on the reader and is not this one's to fix.
    /// </para>
    /// </remarks>
    private static string FormatNumber(double value) =>
        double.IsNaN(value) ? "NaN"
        : double.IsPositiveInfinity(value) ? "Inf"
        : double.IsNegativeInfinity(value) ? "-Inf"
        : JgsNumberFormat.Format(value);

    /// <summary>Formats like MATLAB: <c>1.2i</c> when purely imaginary, else <c>0.5+1.2i</c> / <c>0.5-1.2i</c>.</summary>
    private static string FormatComplex(Complex value)
    {
        string imaginary = FormatNumber(Math.Abs(value.Imaginary)) + "i";
        if (value.Real == 0)
        {
            return value.Imaginary < 0 ? "-" + imaginary : imaginary;
        }

        return FormatNumber(value.Real) + (value.Imaginary < 0 ? "-" : "+") + imaginary;
    }

    /// <summary>Small arrays format in full; above the cap, a short prefix and the element count.</summary>
    private const int DisplayMaxElements = 1000;
    private const int DisplayPrefixElements = 10;

    /// <summary>
    /// Formats an array (packed or boxed) with bounded work: a million-sample signal displays as its
    /// first few elements plus a count, never a megabyte string that gets truncated downstream.
    /// </summary>
    private static string FormatArray(JgsValue array)
    {
        if (array.IsShaped)
        {
            return FormatMatrix(array);
        }

        int count = array.ArrayLength;
        int shown = count <= DisplayMaxElements ? count : DisplayPrefixElements;
        var sb = new StringBuilder("[");
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(array.ElementAt(i).Display());
        }

        return shown < count
            ? sb.Append(", …] (").Append(count).Append(" elements)").ToString()
            : sb.Append(']').ToString();
    }

    /// <summary>
    /// A matrix prints the way it was written — rows separated by semicolons — because a column-major
    /// element run would be unreadable for the one value whose whole point is its layout.
    /// </summary>
    /// <summary>
    /// Formats a string array (M63). A string <em>scalar</em> formats as its bare text, which is what
    /// keeps <c>disp</c>, <c>sprintf</c>, and every builtin that reaches for <see cref="Display"/> as
    /// its last resort working the moment <c>"..."</c> stops being a char. Anything larger shows its
    /// elements quoted, because the quotes are the only thing on the page that says string rather
    /// than cell.
    /// </summary>
    /// <summary>
    /// Formats a char matrix (M105) as its rows rather than as the code points underneath — which is
    /// the whole reason the tag exists on the display path at all. The layout is the one every other
    /// matrix here uses, rows separated by semicolons, because this display is JGraph's own and not
    /// MATLAB's block form.
    /// </summary>
    private static string FormatCharMatrix(JgsValue array) =>
        string.Concat("[", string.Join("; ", array.CharMatrixRows()), "]");

    private static string FormatStringArray(JgsValue array)
    {
        int count = array.ArrayLength;
        if (count == 1)
        {
            return array.ElementAt(0).Display();
        }

        int shown = count <= DisplayMaxElements ? count : DisplayPrefixElements;
        var sb = new StringBuilder("[");
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('"').Append(array.ElementAt(i).Display()).Append('"');
        }

        if (shown < count)
        {
            sb.Append(", ... (").Append(count - shown).Append(" more)");
        }

        return sb.Append(']').ToString();
    }

    /// <summary>
    /// Formats a datetime or a duration (M64) through its own <see cref="JgsTimeTag.Format"/>. A
    /// scalar formats bare, for the same reason a string scalar does: everything that falls back on
    /// <see cref="Display"/> — <c>disp</c>, <c>sprintf</c>'s <c>%s</c>, a title — wants the text and
    /// not a one-element list containing it.
    /// </summary>
    private static string FormatTime(JgsValue value)
    {
        JgsTimeTag tag = value._time!;

        // A calendarDuration keeps three numbers per element in a struct array rather than one
        // millisecond count, so its length and its elements are read from there (M82).
        bool calendar = tag.Kind == JgsTimeKind.CalendarDuration;
        int count = calendar ? value.AsStructArray.Length : value.ArrayLength;
        string One(int index) => calendar
            ? JgsBuiltins.TimeText(value, index)
            : JgsTime.Format(value.ElementAt(index).AsNumber, tag);

        if (count == 1)
        {
            return One(0);
        }

        if (count == 0)
        {
            return "[]";
        }

        int shown = count <= DisplayMaxElements ? count : DisplayPrefixElements;
        var sb = new StringBuilder("[");
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(One(i));
        }

        if (shown < count)
        {
            sb.Append(", ... (").Append(count - shown).Append(" more)");
        }

        return sb.Append(']').ToString();
    }

    private static string FormatMatrix(JgsValue matrix)
    {
        int rows = matrix.Rows;
        int cols = matrix.Cols;
        if ((long)rows * cols > DisplayMaxElements)
        {
            return $"[{rows}x{cols} matrix]";
        }

        var sb = new StringBuilder("[");
        for (int r = 0; r < rows; r++)
        {
            if (r > 0)
            {
                sb.Append("; ");
            }

            for (int c = 0; c < cols; c++)
            {
                if (c > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(matrix.ElementAt((c * rows) + r).Display());
            }
        }

        return sb.Append(']').ToString();
    }
}
