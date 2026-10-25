using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A logical array, dense or sparse, as R2025b's: flat data read by rows unless
/// <c>columnMajorOrder</c> is true (<c>k8_client</c>: <c>logical.rows.asym.true</c>), a bool converting
/// to a scalar and back, <see cref="ToVector"/> in MATLAB's column order.
/// </summary>
public class MWLogicalArray : MWIndexArray
{
    private bool[] _values;
    private bool _sparse;
    private int _nonZeroMax;

    /// <summary>The empty logical, 0-by-0.</summary>
    public MWLogicalArray()
        : this([0, 0], [])
    {
    }

    /// <summary>A row-by-column array of false.</summary>
    public MWLogicalArray(int row, int column)
        : this([row, column])
    {
    }

    /// <summary>An array of false of this size.</summary>
    public MWLogicalArray(int[] dimensions)
        : base(dimensions) => _values = new bool[NumberOfElements];

    /// <summary>A logical scalar.</summary>
    public MWLogicalArray(bool logicalValue)
        : this([1, 1], [logicalValue])
    {
    }

    /// <summary>A rows-by-columns logical matrix, its data read by rows.</summary>
    public MWLogicalArray(int rows, int columns, bool[] boolArray)
        : this(rows, columns, boolArray, false)
    {
    }

    /// <summary>A rows-by-columns logical matrix, its data read by columns when <paramref name="columnMajorOrder"/>.</summary>
    public MWLogicalArray(int rows, int columns, bool[] boolArray, bool columnMajorOrder)
        : this([rows, columns], Flat(rows, columns, boolArray, columnMajorOrder))
    {
    }

    /// <summary>A logical array of a .NET bool array, its shape the array's (a vector is a row).</summary>
    public MWLogicalArray(Array boolArray)
        : this(Shaped(boolArray))
    {
    }

    private MWLogicalArray(int[] dims, bool[] values)
        : base(dims) => _values = values;

    private MWLogicalArray((int[] Dims, bool[] Values) made)
        : this(made.Dims, made.Values)
    {
    }

    /// <summary>An element, as a 1-by-1 logical, at MATLAB's 1-based subscripts.</summary>
    public new MWLogicalArray this[params int[] indices]
    {
        get => (MWLogicalArray)GetElement(indices);
        set => SetElement(indices, value);
    }

    /// <summary>The empty logical.</summary>
    public static MWLogicalArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Logical;

    /// <summary>Whether the array has one element.</summary>
    public bool IsScalar => NumberOfElements == 1;

    /// <inheritdoc />
    public override bool IsSparse => _sparse;

    /// <inheritdoc />
    public override int NonZeroMaxStorage => _sparse ? Math.Max(_nonZeroMax, _values.Count(static v => v)) : 0;

    /// <inheritdoc />
    public override object Clone() => new MWLogicalArray(Dimensions, (bool[])_values.Clone()) { _sparse = _sparse, _nonZeroMax = _nonZeroMax };

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>An all-false sparse matrix with room for <paramref name="nonZeroMax"/> values.</summary>
    public static MWLogicalArray MakeSparse(int rows, int columns, int nonZeroMax) =>
        new([rows, columns], new bool[rows * columns]) { _sparse = true, _nonZeroMax = nonZeroMax };

    /// <summary>A sparse logical matrix of the values at the 1-based rows and columns, as small as holds them.</summary>
    public static MWLogicalArray MakeSparse(int[] rowIndex, int[] columnIndex, bool[] dataArray) =>
        MakeSparse(rowIndex.Length > 0 ? rowIndex.Max() : 0, columnIndex.Length > 0 ? columnIndex.Max() : 0, rowIndex, columnIndex, dataArray, 0);

    /// <summary>A rows-by-columns sparse logical matrix.</summary>
    public static MWLogicalArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, bool[] dataArray) =>
        MakeSparse(rows, columns, rowIndex, columnIndex, dataArray, 0);

    /// <summary>A rows-by-columns sparse logical matrix with room for <paramref name="nonZeroMax"/> values.</summary>
    public static MWLogicalArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, bool[] dataArray, int nonZeroMax)
    {
        ArgumentNullException.ThrowIfNull(rowIndex);
        ArgumentNullException.ThrowIfNull(columnIndex);
        ArgumentNullException.ThrowIfNull(dataArray);
        MWLogicalArray made = MakeSparse(rows, columns, nonZeroMax);
        for (int k = 0; k < rowIndex.Length; k++)
        {
            if (rowIndex[k] < 1 || rowIndex[k] > rows || columnIndex[k] < 1 || columnIndex[k] > columns)
            {
                throw InvalidIndex();
            }

            made._values[(rowIndex[k] - 1) + ((columnIndex[k] - 1) * rows)] |= dataArray[k];
        }

        return made;
    }

    /// <summary>The elements as a .NET bool array shaped as the array is.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        return NativeConversion.Shaped(Dimensions, typeof(bool), i => _values[i]);
    }

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>The elements in MATLAB's column order.</summary>
    public bool[] ToVector()
    {
        CheckDisposed();
        return (bool[])_values.Clone();
    }

    /// <summary>A logical scalar.</summary>
    public static implicit operator MWLogicalArray(bool value) => new(value);

    /// <summary>A scalar's value; false for any other array, as R2025b's (<c>logical.cast.of.matrix</c>).</summary>
    public static implicit operator bool(MWLogicalArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.CheckDisposed();
        return value._values.Length == 1 && value._values[0];
    }

    private protected override string Text() => _sparse
        ? MatlabDisplay.Sparse(Dims[0], Dims[1], [.. _values.Select(static v => v ? 1.0 : 0.0)], null, logical: true)
        : MatlabDisplay.Logical(Dims, _values);

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();

        // JGraph has no sparse logical: the matrix goes in dense, its values the same.
        return new RuntimeLogical(_values, Dims);
    }

    internal static MWLogicalArray FromRuntime(RuntimeLogical value) => new([.. value.Dimensions], [.. value.Elements]);

    private protected override MWArray GetElement(int[] indices)
    {
        CheckDisposed();
        return new MWLogicalArray(_values[Linear(indices)]);
    }

    private protected override void SetElement(int[] indices, MWArray value)
    {
        CheckDisposed();
        int at = Linear(indices);
        _values[at] = value switch
        {
            MWLogicalArray logical => logical._values.Length > 0 && logical._values[0],
            MWNumericArray number => number.FirstReal != 0,
            _ => throw new ArgumentException("The value must be a logical array"),
        };
    }

    private static bool[] Flat(int rows, int columns, bool[] data, bool columnMajor)
    {
        ArgumentNullException.ThrowIfNull(data);
        if ((long)rows * columns != data.Length)
        {
            throw new ArgumentException("The data array must have (row * columns) elements", "boolArray");
        }

        var values = new bool[data.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                values[r + (c * rows)] = data[columnMajor ? r + (c * rows) : (r * columns) + c];
            }
        }

        return values;
    }

    private static (int[], bool[]) Shaped(Array array)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.GetType().GetElementType() != typeof(bool))
        {
            throw new ArgumentException("The data array type is invalid");
        }

        (int[] dims, object[] values) = NativeConversion.UnshapedObjects(array, rowMajor: true);
        return (dims, [.. values.Select(static v => (bool)v)]);
    }
}
