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
    public static JgsValue Struct(Dictionary<string, JgsValue> fields) => new(JgsType.Struct, 0, fields);

    /// <summary>An empty struct, ready for fields to be assigned.</summary>
    public static JgsValue EmptyStruct() => Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal));

    /// <summary>The cell array's elements (valid only for <see cref="JgsType.Cell"/>).</summary>
    public JgsValue[] AsCell => (JgsValue[])_reference!;

    /// <summary>The struct's fields, in insertion order (valid only for <see cref="JgsType.Struct"/>).</summary>
    public Dictionary<string, JgsValue> AsStruct => (Dictionary<string, JgsValue>)_reference!;

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
        JgsType.Array => "array",
        JgsType.Table => "table",
        JgsType.Image => "image",
        JgsType.Function => "function",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Sparse => "sparse",
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
        JgsType.Array => FormatArray(this),
        JgsType.Table => $"table[{AsTable.RowCount}x{AsTable.ColumnCount}]",
        JgsType.Image => FormatImage(AsImage),
        JgsType.Function => $"fn {AsCallable.Name}",
        JgsType.Cell => FormatCell(AsCell),
        JgsType.Struct => FormatStruct(AsStruct),
        JgsType.Sparse => FormatSparse(AsSparse),
        _ => "value",
    };

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

    private static string FormatNumber(double value) => JgsNumberFormat.Format(value);

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
