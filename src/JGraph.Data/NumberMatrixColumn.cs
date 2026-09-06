using System.Globalization;

namespace JGraph.Data;

/// <summary>A table variable with multiple numeric columns, stored in column-major order.</summary>
public sealed class NumberMatrixColumn(string name, double[] values, int rows, int width) : TableColumn(name, rows)
{
    public double[] Values { get; } = values;
    public int Width { get; } = width;
    public override ColumnType Type => ColumnType.Number;
    public override double GetNumber(int row) => Values[row];
    public override bool IsMissing(int row) => double.IsNaN(GetNumber(row));
    public override string GetText(int row) => string.Join(" ", Enumerable.Range(0, Width).Select(c => Values[c * RowCount + row].ToString("R", CultureInfo.InvariantCulture)));
}
