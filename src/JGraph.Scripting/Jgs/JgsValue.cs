using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Data;

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

/// <summary>
/// A dynamically-typed JGS runtime value: a null, a double, a boolean, a string, an array of values, a
/// data <see cref="Table"/>, or a callable function. Numbers and booleans are stored inline; the other
/// kinds hold a reference. Values are immutable except that an <see cref="JgsType.Array"/>'s elements can
/// be replaced in place (indexed assignment).
/// </summary>
internal sealed class JgsValue
{
    /// <summary>The shared null value.</summary>
    public static readonly JgsValue Null = new(JgsType.Null, 0, null);

    /// <summary>The shared true value.</summary>
    public static readonly JgsValue True = new(JgsType.Bool, 1, null);

    /// <summary>The shared false value.</summary>
    public static readonly JgsValue False = new(JgsType.Bool, 0, null);

    private readonly double _number;
    private readonly object? _reference;

    private JgsValue(JgsType type, double number, object? reference)
    {
        Type = type;
        _number = number;
        _reference = reference;
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

    /// <summary>The backing array (mutable in place for indexed assignment).</summary>
    public JgsValue[] AsArray => (JgsValue[])_reference!;

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
        JgsType.Array => AllTruthy(AsArray),
        _ => true,
    };

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
        JgsType.Array => FormatArray(AsArray),
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

    private static string FormatArray(JgsValue[] elements)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < elements.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(elements[i].Display());
        }

        return sb.Append(']').ToString();
    }
}
