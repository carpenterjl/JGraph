using System.Globalization;

namespace JGraph.Data;

/// <summary>
/// A <see cref="TableColumn"/> of real numbers backed by a <see cref="double"/> array. NaN marks a
/// missing value. The array is used directly (not copied) so a number column becomes a zero-copy plot
/// series (see <see cref="TableSeries"/>).
/// </summary>
public sealed class NumberColumn : TableColumn
{
    private readonly double[] _values;

    /// <summary>Creates a number column over <paramref name="values"/> (used directly, not copied).</summary>
    public NumberColumn(string name, double[] values)
        : base(name, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _values = values;
    }

    public override ColumnType Type => ColumnType.Number;

    /// <summary>The raw values, including NaN for missing entries.</summary>
    public ReadOnlySpan<double> Values => _values;

    internal double[] Storage => _values;

    public override bool IsMissing(int row) => double.IsNaN(_values[row]);

    public override double GetNumber(int row) => _values[row];

    public override string GetText(int row)
    {
        double value = _values[row];
        return double.IsNaN(value) ? string.Empty : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
