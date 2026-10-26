using System.Collections.ObjectModel;

namespace JGraph.Runtime;

/// <summary>
/// A value passed to or returned from a deployed function, in a form a host can read without the
/// engine: its MATLAB class, its size, and its elements in MATLAB's column-major order.
/// </summary>
/// <remarks>
/// The model is lossless for what JGraph holds: a value read out of a session and passed back in is
/// the same value. Numbers are doubles underneath, as they are in JGraph, so an <c>int64</c> or
/// <c>uint64</c> is exact up to 2^53 and a larger one is refused rather than rounded (the interop
/// plan's <c>JGraph:interop:int64Precision</c>). A value with no data form here (an object, a
/// function handle, a table, a datetime, a categorical) comes back as a <see cref="RuntimeOpaque"/>
/// that only the component that made it can take back.
/// </remarks>
public abstract class RuntimeValue : IEquatable<RuntimeValue>
{
    private readonly int[] _dimensions;

    private protected RuntimeValue(string className, IReadOnlyList<int> dimensions)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(dimensions);
        ClassName = className;
        _dimensions = Shape(dimensions);
        Count = 1;
        foreach (int d in _dimensions)
        {
            Count = checked(Count * d);
        }
    }

    /// <summary>The value's MATLAB class, as <c>class</c> answers it.</summary>
    public string ClassName { get; }

    /// <summary>The size of each dimension, at least two, with trailing singleton dimensions after the second dropped.</summary>
    public IReadOnlyList<int> Dimensions => Array.AsReadOnly(_dimensions);

    /// <summary>The number of elements.</summary>
    public int Count { get; }

    /// <summary>Whether the value has no elements.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>A double scalar.</summary>
    public static RuntimeNumeric Scalar(double value) => new(RuntimeNumeric.DoubleClass, [value], null, [1, 1]);

    /// <summary>A logical scalar.</summary>
    public static RuntimeLogical Scalar(bool value) => new([value], [1, 1]);

    /// <summary>A char row vector (<c>''</c>, 0-by-0, when the text is empty).</summary>
    public static RuntimeChar Text(string text) => RuntimeChar.Row(text);

    /// <summary>A string scalar; null is <c>&lt;missing&gt;</c>.</summary>
    public static RuntimeString String(string? text) => new([text], [1, 1]);

    /// <summary>The empty double, <c>[]</c>.</summary>
    public static RuntimeNumeric Empty() => new(RuntimeNumeric.DoubleClass, [], null, [0, 0]);

    /// <inheritdoc />
    public bool Equals(RuntimeValue? other) =>
        other is not null && other.GetType() == GetType() && other.ClassName == ClassName
        && other._dimensions.AsSpan().SequenceEqual(_dimensions) && SameElements(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RuntimeValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ClassName);
        foreach (int d in _dimensions)
        {
            hash.Add(d);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => $"{string.Join("x", _dimensions)} {ClassName}";

    /// <summary>Whether <paramref name="other"/>, of the same type, class and size, holds the same elements.</summary>
    private protected abstract bool SameElements(RuntimeValue other);

    /// <summary>
    /// MATLAB's shape for a size: at least two dimensions, none negative, and trailing singletons
    /// past the second dropped, as <c>size</c> reports them.
    /// </summary>
    private static int[] Shape(IReadOnlyList<int> dimensions)
    {
        int length = dimensions.Count;
        while (length > 2 && dimensions[length - 1] == 1)
        {
            length--;
        }

        var shape = new int[Math.Max(length, 2)];
        for (int i = 0; i < shape.Length; i++)
        {
            int d = i < length ? dimensions[i] : 1;
            ArgumentOutOfRangeException.ThrowIfNegative(d, nameof(dimensions));
            shape[i] = d;
        }

        if (dimensions.Count == 1)
        {
            // A single size is a column of that many, as zeros(n, 1) is.
            shape[1] = 1;
        }

        return shape;
    }

    private protected void CheckCount(int length, string what)
    {
        if (length != Count)
        {
            throw new ArgumentException($"{length} {what} do not fill a {string.Join("-by-", _dimensions)} array.");
        }
    }
}

/// <summary>A numeric array: <c>double</c>, <c>single</c> or an integer class, real or complex.</summary>
public sealed class RuntimeNumeric : RuntimeValue
{
    internal const string DoubleClass = "double";

    /// <summary>The numeric classes, in mxClassID's order.</summary>
    public static IReadOnlyList<string> Classes { get; } =
        ["double", "single", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64"];

    private const double ExactLimit = 9007199254740992.0; // 2^53

    private readonly double[] _real;
    private readonly double[]? _imaginary;

    /// <summary>Creates a numeric array from its elements in column-major order.</summary>
    /// <param name="className">One of <see cref="Classes"/>.</param>
    /// <param name="real">The real parts.</param>
    /// <param name="imaginary">The imaginary parts, or null for a real array. Only <c>double</c> and <c>single</c> can be complex.</param>
    /// <param name="dimensions">The size.</param>
    /// <exception cref="ArgumentException">The class is not numeric, the elements do not fill the size, or a value cannot be held in the class.</exception>
    public RuntimeNumeric(string className, IReadOnlyList<double> real, IReadOnlyList<double>? imaginary, IReadOnlyList<int> dimensions)
        : base(className, dimensions)
    {
        ArgumentNullException.ThrowIfNull(real);
        if (!Classes.Contains(className))
        {
            throw new ArgumentException($"'{className}' is not a numeric class.", nameof(className));
        }

        CheckCount(real.Count, "elements");
        if (imaginary is not null)
        {
            CheckCount(imaginary.Count, "imaginary parts");
            if (className is not ("double" or "single"))
            {
                throw new ArgumentException($"A {className} array cannot be complex.", nameof(imaginary));
            }
        }

        _real = Held(className, real);
        _imaginary = imaginary is null ? null : Held(className, imaginary);
    }

    /// <summary>A double array from its column-major elements.</summary>
    public static RuntimeNumeric Of(double[] columnMajor, params int[] dimensions) =>
        new(DoubleClass, columnMajor, null, dimensions.Length == 0 ? [1, columnMajor.Length] : dimensions);

    /// <summary>A single array from its column-major elements.</summary>
    public static RuntimeNumeric Single(float[] columnMajor, params int[] dimensions) =>
        From("single", columnMajor, static x => x, dimensions);

    /// <summary>An int8 array from its column-major elements.</summary>
    public static RuntimeNumeric Int8(sbyte[] columnMajor, params int[] dimensions) => From("int8", columnMajor, static x => x, dimensions);

    /// <summary>A uint8 array from its column-major elements.</summary>
    public static RuntimeNumeric UInt8(byte[] columnMajor, params int[] dimensions) => From("uint8", columnMajor, static x => x, dimensions);

    /// <summary>An int16 array from its column-major elements.</summary>
    public static RuntimeNumeric Int16(short[] columnMajor, params int[] dimensions) => From("int16", columnMajor, static x => x, dimensions);

    /// <summary>A uint16 array from its column-major elements.</summary>
    public static RuntimeNumeric UInt16(ushort[] columnMajor, params int[] dimensions) => From("uint16", columnMajor, static x => x, dimensions);

    /// <summary>An int32 array from its column-major elements.</summary>
    public static RuntimeNumeric Int32(int[] columnMajor, params int[] dimensions) => From("int32", columnMajor, static x => x, dimensions);

    /// <summary>A uint32 array from its column-major elements.</summary>
    public static RuntimeNumeric UInt32(uint[] columnMajor, params int[] dimensions) => From("uint32", columnMajor, static x => x, dimensions);

    /// <summary>An int64 array from its column-major elements; each must be within 2^53 of zero.</summary>
    public static RuntimeNumeric Int64(long[] columnMajor, params int[] dimensions) =>
        From("int64", columnMajor, static x => x is >= -(1L << 53) and <= 1L << 53 ? x : throw Inexact("int64", x), dimensions);

    /// <summary>A uint64 array from its column-major elements; each must be at most 2^53.</summary>
    public static RuntimeNumeric UInt64(ulong[] columnMajor, params int[] dimensions) =>
        From("uint64", columnMajor, static x => x <= 1UL << 53 ? x : throw Inexact("uint64", x), dimensions);

    /// <summary>A complex double array from its column-major elements.</summary>
    public static RuntimeNumeric Complex(System.Numerics.Complex[] columnMajor, params int[] dimensions) =>
        new(DoubleClass,
            [.. columnMajor.Select(static c => c.Real)],
            [.. columnMajor.Select(static c => c.Imaginary)],
            dimensions.Length == 0 ? [1, columnMajor.Length] : dimensions);

    /// <summary>The real parts, column-major.</summary>
    public ReadOnlyCollection<double> Real => Array.AsReadOnly(_real);

    /// <summary>The imaginary parts, column-major, or null for a real array.</summary>
    public ReadOnlyCollection<double>? Imaginary => _imaginary is null ? null : Array.AsReadOnly(_imaginary);

    /// <summary>Whether the array is complex.</summary>
    public bool IsComplex => _imaginary is not null;

    /// <summary>The sole element of a real scalar.</summary>
    /// <exception cref="InvalidOperationException">The array is not a real scalar.</exception>
    public double ScalarValue => Count == 1 && _imaginary is null
        ? _real[0]
        : throw new InvalidOperationException($"A {this} is not a real scalar.");

    private protected override bool SameElements(RuntimeValue other)
    {
        var that = (RuntimeNumeric)other;
        return _real.AsSpan().SequenceEqual(that._real)
            && (_imaginary is null ? that._imaginary is null : that._imaginary is not null && _imaginary.AsSpan().SequenceEqual(that._imaginary));
    }

    private static RuntimeNumeric From<T>(string className, T[] columnMajor, Func<T, double> widen, int[] dimensions)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        return new RuntimeNumeric(className, [.. columnMajor.Select(widen)], null, dimensions.Length == 0 ? [1, columnMajor.Length] : dimensions);
    }

    private static ArgumentException Inexact(string className, object x) =>
        new($"{x} cannot be held exactly: JGraph keeps {Article(className)} {className} as a double, exact to 2^53.");

    private static string Article(string className) => className.StartsWith('u') ? "a" : "an";

    /// <summary>The elements as the class holds them, refusing any the class cannot.</summary>
    private static double[] Held(string className, IReadOnlyList<double> values)
    {
        var held = new double[values.Count];
        for (int i = 0; i < held.Length; i++)
        {
            double x = values[i];
            held[i] = className switch
            {
                "double" => x,
                "single" => (float)x,
                _ => Integer(className, x),
            };
        }

        return held;
    }

    private static double Integer(string className, double x)
    {
        (double low, double high) = className switch
        {
            "int8" => (sbyte.MinValue, sbyte.MaxValue),
            "uint8" => (byte.MinValue, byte.MaxValue),
            "int16" => (short.MinValue, short.MaxValue),
            "uint16" => (ushort.MinValue, ushort.MaxValue),
            "int32" => (int.MinValue, int.MaxValue),
            "uint32" => (uint.MinValue, uint.MaxValue),
            "int64" => (-ExactLimit, ExactLimit),
            _ => (0, ExactLimit),
        };

        if (double.IsNaN(x) || x != Math.Floor(x) || x < low || x > high)
        {
            throw new ArgumentException(
                className is "int64" or "uint64" && x == Math.Floor(x)
                    ? $"{x} cannot be held exactly: JGraph keeps {Article(className)} {className} as a double, exact to 2^53."
                    : $"{x} is not a {className} value.");
        }

        return x;
    }
}

/// <summary>A logical array.</summary>
public sealed class RuntimeLogical : RuntimeValue
{
    private readonly bool[] _elements;

    /// <summary>Creates a logical array from its elements in column-major order.</summary>
    public RuntimeLogical(IReadOnlyList<bool> columnMajor, IReadOnlyList<int> dimensions)
        : base("logical", dimensions)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        CheckCount(columnMajor.Count, "elements");
        _elements = [.. columnMajor];
    }

    /// <summary>The elements, column-major.</summary>
    public ReadOnlyCollection<bool> Elements => Array.AsReadOnly(_elements);

    private protected override bool SameElements(RuntimeValue other) => _elements.AsSpan().SequenceEqual(((RuntimeLogical)other)._elements);
}

/// <summary>A char array: text one row per line of a matrix, its characters stored column-major.</summary>
public sealed class RuntimeChar : RuntimeValue
{
    private readonly string _characters;

    /// <summary>Creates a char array from its characters in column-major order.</summary>
    public RuntimeChar(string columnMajor, IReadOnlyList<int> dimensions)
        : base("char", dimensions)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        CheckCount(columnMajor.Length, "characters");
        _characters = columnMajor;
    }

    /// <summary>A row of text; empty text is <c>''</c>, 0-by-0.</summary>
    public static RuntimeChar Row(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new RuntimeChar(text, text.Length == 0 ? [0, 0] : [1, text.Length]);
    }

    /// <summary>A char matrix of equal-length rows.</summary>
    public static RuntimeChar Matrix(params string[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        if (rows.Any(r => r.Length != width))
        {
            throw new ArgumentException("The rows of a char matrix must all be the same length.", nameof(rows));
        }

        var columnMajor = new char[rows.Length * width];
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < width; c++)
            {
                columnMajor[(c * rows.Length) + r] = rows[r][c];
            }
        }

        return new RuntimeChar(new string(columnMajor), [rows.Length, width]);
    }

    /// <summary>The characters, column-major.</summary>
    public string Characters => _characters;

    /// <summary>The rows of a two-dimensional char array.</summary>
    /// <exception cref="InvalidOperationException">The array has more than two dimensions.</exception>
    public IReadOnlyList<string> Rows
    {
        get
        {
            if (Dimensions.Count > 2)
            {
                throw new InvalidOperationException("An N-D char array has no rows.");
            }

            int height = Dimensions[0];
            int width = Dimensions[1];
            var rows = new string[height];
            for (int r = 0; r < height; r++)
            {
                var row = new char[width];
                for (int c = 0; c < width; c++)
                {
                    row[c] = _characters[(c * height) + r];
                }

                rows[r] = new string(row);
            }

            return rows;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Dimensions.Count == 2 && Dimensions[0] <= 1 ? _characters : base.ToString();

    private protected override bool SameElements(RuntimeValue other) => _characters == ((RuntimeChar)other)._characters;
}

/// <summary>A string array; a null element is <c>&lt;missing&gt;</c>.</summary>
public sealed class RuntimeString : RuntimeValue
{
    private readonly string?[] _elements;

    /// <summary>Creates a string array from its elements in column-major order.</summary>
    public RuntimeString(IReadOnlyList<string?> columnMajor, IReadOnlyList<int> dimensions)
        : base("string", dimensions)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        CheckCount(columnMajor.Count, "strings");
        _elements = [.. columnMajor];
    }

    /// <summary>The elements, column-major; null is <c>&lt;missing&gt;</c>.</summary>
    public ReadOnlyCollection<string?> Elements => Array.AsReadOnly(_elements);

    private protected override bool SameElements(RuntimeValue other) => _elements.AsSpan().SequenceEqual(((RuntimeString)other)._elements);
}

/// <summary>A cell array.</summary>
public sealed class RuntimeCell : RuntimeValue
{
    private readonly RuntimeValue[] _elements;

    /// <summary>Creates a cell array from its elements in column-major order.</summary>
    public RuntimeCell(IReadOnlyList<RuntimeValue> columnMajor, IReadOnlyList<int> dimensions)
        : base("cell", dimensions)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        CheckCount(columnMajor.Count, "elements");
        _elements = [.. columnMajor];
        if (Array.Exists(_elements, static e => e is null))
        {
            throw new ArgumentException("A cell's element cannot be null; use RuntimeValue.Empty().", nameof(columnMajor));
        }
    }

    /// <summary>A 1-by-n cell row.</summary>
    public static RuntimeCell Row(params RuntimeValue[] elements) => new(elements, [1, elements.Length]);

    /// <summary>The elements, column-major.</summary>
    public ReadOnlyCollection<RuntimeValue> Elements => Array.AsReadOnly(_elements);

    private protected override bool SameElements(RuntimeValue other) => _elements.AsSpan().SequenceEqual(((RuntimeCell)other)._elements);
}

/// <summary>A struct array: every element has the same fields.</summary>
public sealed class RuntimeStruct : RuntimeValue
{
    private readonly string[] _fieldNames;
    private readonly IReadOnlyDictionary<string, RuntimeValue>[] _elements;

    /// <summary>Creates a struct array from its elements in column-major order.</summary>
    /// <param name="fieldNames">The field names, in order.</param>
    /// <param name="columnMajor">Each element's fields; each must name exactly <paramref name="fieldNames"/>.</param>
    /// <param name="dimensions">The size.</param>
    public RuntimeStruct(IReadOnlyList<string> fieldNames, IReadOnlyList<IReadOnlyDictionary<string, RuntimeValue>> columnMajor, IReadOnlyList<int> dimensions)
        : base("struct", dimensions)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        ArgumentNullException.ThrowIfNull(columnMajor);
        CheckCount(columnMajor.Count, "elements");
        _fieldNames = [.. fieldNames];
        if (_fieldNames.Distinct(StringComparer.Ordinal).Count() != _fieldNames.Length)
        {
            throw new ArgumentException("A struct's field names must be distinct.", nameof(fieldNames));
        }

        _elements = new IReadOnlyDictionary<string, RuntimeValue>[columnMajor.Count];
        for (int e = 0; e < _elements.Length; e++)
        {
            IReadOnlyDictionary<string, RuntimeValue> element = columnMajor[e];
            if (element.Count != _fieldNames.Length || _fieldNames.Any(f => !element.TryGetValue(f, out RuntimeValue? v) || v is null))
            {
                throw new ArgumentException($"Element {e + 1} does not have exactly the fields {string.Join(", ", _fieldNames)}.", nameof(columnMajor));
            }

            _elements[e] = new ReadOnlyDictionary<string, RuntimeValue>(_fieldNames.ToDictionary(f => f, f => element[f], StringComparer.Ordinal));
        }
    }

    /// <summary>A 1-by-1 struct with these fields, in this order.</summary>
    public static RuntimeStruct Scalar(params (string Name, RuntimeValue Value)[] fields) =>
        new([.. fields.Select(static f => f.Name)],
            [fields.ToDictionary(static f => f.Name, static f => f.Value, StringComparer.Ordinal)],
            [1, 1]);

    /// <summary>The field names, in order.</summary>
    public ReadOnlyCollection<string> FieldNames => Array.AsReadOnly(_fieldNames);

    /// <summary>The elements, column-major.</summary>
    public ReadOnlyCollection<IReadOnlyDictionary<string, RuntimeValue>> Elements => Array.AsReadOnly(_elements);

    private protected override bool SameElements(RuntimeValue other)
    {
        var that = (RuntimeStruct)other;
        if (!_fieldNames.AsSpan().SequenceEqual(that._fieldNames))
        {
            return false;
        }

        for (int e = 0; e < _elements.Length; e++)
        {
            foreach (string field in _fieldNames)
            {
                if (!_elements[e][field].Equals(that._elements[e][field]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>A sparse double or logical matrix in compressed sparse column form.</summary>
public sealed class RuntimeSparse : RuntimeValue
{
    private readonly int[] _rowIndices;
    private readonly int[] _columnStarts;
    private readonly double[] _values;

    /// <summary>Creates a sparse matrix.</summary>
    /// <param name="rows">The row count.</param>
    /// <param name="columns">The column count.</param>
    /// <param name="rowIndices">The zero-based row of each stored value.</param>
    /// <param name="columnStarts">Where each column's values start in <paramref name="values"/>, with one more entry at the end.</param>
    /// <param name="values">The stored values, column by column.</param>
    public RuntimeSparse(int rows, int columns, IReadOnlyList<int> rowIndices, IReadOnlyList<int> columnStarts, IReadOnlyList<double> values)
        : base(RuntimeNumeric.DoubleClass, [rows, columns])
    {
        ArgumentNullException.ThrowIfNull(rowIndices);
        ArgumentNullException.ThrowIfNull(columnStarts);
        ArgumentNullException.ThrowIfNull(values);
        if (columnStarts.Count != columns + 1 || rowIndices.Count != values.Count || columnStarts[^1] != values.Count || columnStarts[0] != 0)
        {
            throw new ArgumentException("The compressed columns do not describe the values.");
        }

        for (int c = 0; c < columns; c++)
        {
            if (columnStarts[c] > columnStarts[c + 1])
            {
                throw new ArgumentException("The column starts must not decrease.", nameof(columnStarts));
            }
        }

        if (rowIndices.Any(r => r < 0 || r >= rows))
        {
            throw new ArgumentException("A row index lies outside the matrix.", nameof(rowIndices));
        }

        _rowIndices = [.. rowIndices];
        _columnStarts = [.. columnStarts];
        _values = [.. values];
    }

    /// <summary>The zero-based row of each stored value.</summary>
    public ReadOnlyCollection<int> RowIndices => Array.AsReadOnly(_rowIndices);

    /// <summary>Where each column's values start, with one more entry at the end.</summary>
    public ReadOnlyCollection<int> ColumnStarts => Array.AsReadOnly(_columnStarts);

    /// <summary>The stored values, column by column.</summary>
    public ReadOnlyCollection<double> Values => Array.AsReadOnly(_values);

    private protected override bool SameElements(RuntimeValue other)
    {
        var that = (RuntimeSparse)other;
        return _rowIndices.AsSpan().SequenceEqual(that._rowIndices)
            && _columnStarts.AsSpan().SequenceEqual(that._columnStarts)
            && _values.AsSpan().SequenceEqual(that._values);
    }
}

/// <summary>
/// A value with no data form outside the engine (an object, a function handle, a table, a datetime,
/// a categorical): its class and size can be read, and the component that returned it can be given
/// it back unchanged.
/// </summary>
public sealed class RuntimeOpaque : RuntimeValue
{
    internal RuntimeOpaque(string className, IReadOnlyList<int> dimensions, object owner, object held)
        : base(className, dimensions)
    {
        Owner = owner;
        Held = held;
    }

    /// <summary>The component that returned the value; only it can take the value back.</summary>
    internal object Owner { get; }

    /// <summary>The engine's own value.</summary>
    internal object Held { get; }

    private protected override bool SameElements(RuntimeValue other) => ReferenceEquals(Held, ((RuntimeOpaque)other).Held);
}
