using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Data;
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
    Function,
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
    private object? _reference; // mutable ONLY by DemoteToBoxed
    private readonly JgsPackedKind _packedKind;

    private JgsValue(JgsType type, double number, object? reference, JgsPackedKind packedKind = JgsPackedKind.Number)
    {
        Type = type;
        _number = number;
        _reference = reference;
        _packedKind = packedKind;
    }

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

    /// <summary>Wraps a data table.</summary>
    public static JgsValue Table(Table table) => new(JgsType.Table, 0, table);

    /// <summary>Wraps a callable function.</summary>
    public static JgsValue Function(IJgsCallable callable) => new(JgsType.Function, 0, callable);

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

    /// <summary>Whether this array value is backed by a packed real-number buffer.</summary>
    public bool IsPacked => _reference is NumericBuffer;

    /// <summary>Whether this array value is backed by a packed planar complex payload.</summary>
    public bool IsPackedComplex => _reference is JgsPackedComplex;

    /// <summary>The packed buffer (valid only when <see cref="IsPacked"/>).</summary>
    public NumericBuffer AsBuffer => (NumericBuffer)_reference!;

    /// <summary>The packed complex payload (valid only when <see cref="IsPackedComplex"/>).</summary>
    public JgsPackedComplex AsPackedComplex => (JgsPackedComplex)_reference!;

    /// <summary>The element kind of a packed array (valid only when <see cref="IsPacked"/>).</summary>
    public JgsPackedKind PackedKind => _packedKind;

    /// <summary>Element count of an array value, packed or boxed.</summary>
    public int ArrayLength => _reference switch
    {
        NumericBuffer buffer => buffer.Length,
        JgsPackedComplex complex => complex.Length,
        _ => AsArray.Length,
    };

    /// <summary>Element <paramref name="index"/> of an array value, packed or boxed (0-based).</summary>
    public JgsValue ElementAt(int index)
    {
        switch (_reference)
        {
            case NumericBuffer buffer:
                double raw = buffer.AsSpan()[index];
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
        var elements = new JgsValue[span.Length];
        if (_packedKind == JgsPackedKind.Bool)
        {
            for (int i = 0; i < span.Length; i++)
            {
                elements[i] = Bool(span[i] != 0);
            }
        }
        else
        {
            for (int i = 0; i < span.Length; i++)
            {
                elements[i] = Number(span[i]);
            }
        }

        GC.KeepAlive(buffer);
        return elements;
    }

    /// <summary>
    /// Converts a packed array to boxed in place, e.g. when a script writes a non-numeric value into
    /// one of its slots. Every alias shares this wrapper (single-wrapper invariant), so all names see
    /// the demoted array; the backing storage is disposed. No-op for already-boxed arrays.
    /// </summary>
    public void DemoteToBoxed()
    {
        if (_reference is NumericBuffer buffer)
        {
            _reference = MaterializeBoxed();
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
            NumericBuffer buffer => PackedMath.AllNonZero(buffer), // empty false, NaN nonzero — the boxed fold
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
    /// Shallow value equality, the semantics of scalar <c>==</c>: matching type and value for
    /// null/number/bool/string, reference identity for arrays, tables, and functions. Values of
    /// different types are unequal, never an error. See the <c>isequal</c> builtin for deep equality.
    /// </summary>
    public static bool AreEqual(JgsValue left, JgsValue right)
    {
        if (left.Type != right.Type)
        {
            return false;
        }

        return left.Type switch
        {
            JgsType.Null => true,
            JgsType.Number => left.AsNumber.Equals(right.AsNumber),
            JgsType.Complex => left.AsComplex.Equals(right.AsComplex),
            JgsType.Bool => left.AsBool == right.AsBool,
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
        JgsType.Function => "function",
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
        JgsType.Function => $"fn {AsCallable.Name}",
        _ => "value",
    };

    private static string FormatNumber(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

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
}
