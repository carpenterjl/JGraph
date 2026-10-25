using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A cell array, as R2025b's: an element is an MWArray (the empty double where none was set), read
/// and written at MATLAB's 1-based subscripts, each a copy; <see cref="ToArray"/> answers the native
/// API's values; <see cref="ToString"/> is MATLAB's older cell display, <c>[5]    'two'    [2x2 double]</c>
/// (<c>k8_client</c>).
/// </summary>
public class MWCellArray : MWArray
{
    private MWArray[] _cells;

    /// <summary>The empty cell, 0-by-0.</summary>
    public MWCellArray()
        : this([0, 0])
    {
    }

    /// <summary>A rows-by-columns cell of empty doubles.</summary>
    public MWCellArray(int rows, int columns)
        : this([rows, columns])
    {
    }

    /// <summary>A cell of empty doubles of this size.</summary>
    public MWCellArray(int[] dimensions)
        : base(dimensions)
    {
        _cells = new MWArray[NumberOfElements];
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new MWNumericArray();
        }
    }

    /// <summary>A cell of empty doubles of the size the numeric array lists.</summary>
    public MWCellArray(MWNumericArray dimensions)
        : this([.. ((double[])(dimensions ?? throw new ArgumentNullException(nameof(dimensions))).ToVector(MWArrayComponent.Real)).Select(static d => (int)d)])
    {
    }

    /// <summary>A column cell of a char matrix's rows.</summary>
    public MWCellArray(MWCharArray strings)
        : this([(strings ?? throw new ArgumentNullException(nameof(strings))).ToStringArrayIfIs2DArray().Length, 1])
    {
        string[] rows = strings.ToStringArrayIfIs2DArray();
        for (int r = 0; r < rows.Length; r++)
        {
            _cells[r] = new MWCharArray(rows[r]);
        }
    }

    private MWCellArray(int[] dims, MWArray[] cells)
        : base(dims) => _cells = cells;

    /// <summary>An element, a copy of what the cell holds, at MATLAB's 1-based subscripts.</summary>
    public new MWArray this[params int[] indices]
    {
        get => GetElement(indices);
        set => SetElement(indices, value);
    }

    /// <summary>The empty cell.</summary>
    public static MWCellArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Cell;

    /// <summary>The elements, for the display and the conversions.</summary>
    internal IReadOnlyList<MWArray> Cells => _cells;

    /// <inheritdoc />
    public override object Clone() => new MWCellArray(Dimensions, [.. _cells.Select(static c => (MWArray)c.Clone())]);

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>The elements as the native API's values, shaped as the cell is.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        return NativeConversion.Shaped(Dimensions, typeof(object), i => NativeConversion.ToNative(_cells[i].ToRuntime()));
    }

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    private protected override string Text() => LegacyDisplay.Cell(this);

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return new RuntimeCell([.. _cells.Select(static c => c.ToRuntime())], Dims);
    }

    internal static MWCellArray FromRuntime(RuntimeCell value) =>
        new([.. value.Dimensions], [.. value.Elements.Select(MWArray.FromRuntime)]);

    private protected override MWArray GetElement(int[] indices)
    {
        CheckDisposed();
        return (MWArray)_cells[Linear(indices)].Clone();
    }

    private protected override void SetElement(int[] indices, MWArray value)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(value);
        _cells[Linear(indices)] = (MWArray)value.Clone();
    }
}
