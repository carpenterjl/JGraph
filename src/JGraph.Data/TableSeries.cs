using JGraph.Core.Data;

namespace JGraph.Data;

/// <summary>
/// Bridges a <see cref="Table"/> to the plotting layer by turning a column pair into an
/// <see cref="IDataSeries"/>. Number and date/time columns are exposed with zero copy (the series
/// shares the column's backing array); text columns are projected onto their category indices.
/// </summary>
public static class TableSeries
{
    /// <summary>
    /// Creates a plot series of <paramref name="yColumn"/> against <paramref name="xColumn"/>. When
    /// <paramref name="xColumn"/> is null the X coordinates are the implicit row indices 0, 1, 2, ….
    /// A text X column yields the row's category index (pair the axis with its
    /// <see cref="TextColumn.Categories"/>).
    /// </summary>
    public static IDataSeries Create(Table table, string? xColumn, string yColumn)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(yColumn);

        double[] ys = GetNumbers(table, yColumn);
        if (xColumn is null)
        {
            return ArrayDataSeries.FromValues(ys);
        }

        double[] xs = GetNumbers(table, xColumn);
        return new ArrayDataSeries(xs, ys);
    }

    /// <summary>
    /// The numeric values of a column: the backing array (not copied) for number/date columns, or a
    /// freshly computed array of category indices for a text column. Useful for auxiliary series data
    /// such as error magnitudes.
    /// </summary>
    public static double[] GetNumbers(Table table, string column)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);

        TableColumn col = table[column];
        switch (col)
        {
            case NumberColumn number:
                return number.Storage;
            case DateTimeColumn dateTime:
                return dateTime.Storage;
            default:
                var values = new double[col.RowCount];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = col.GetNumber(i);
                }

                return values;
        }
    }
}
