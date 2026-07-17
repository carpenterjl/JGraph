namespace JGraph.Data;

/// <summary>
/// One named column of a <see cref="Table"/>. A column is immutable and holds <see cref="RowCount"/>
/// values of a single <see cref="ColumnType"/>. Every column exposes a numeric view via
/// <see cref="GetNumber"/> (the plottable representation) and a display view via <see cref="GetText"/>
/// (for previews); <see cref="IsMissing"/> reports gaps.
/// </summary>
public abstract class TableColumn
{
    private protected TableColumn(string name, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, "Row count cannot be negative.");
        }

        Name = name;
        RowCount = rowCount;
    }

    /// <summary>The column's name (as read from a header or generated).</summary>
    public string Name { get; }

    /// <summary>The inferred type of the column's values.</summary>
    public abstract ColumnType Type { get; }

    /// <summary>The number of rows (shared by every column of the owning table).</summary>
    public int RowCount { get; }

    /// <summary>Whether the value at <paramref name="row"/> is missing (empty/unparseable).</summary>
    public abstract bool IsMissing(int row);

    /// <summary>
    /// The plottable numeric value at <paramref name="row"/>: the value itself for
    /// <see cref="ColumnType.Number"/>, the OLE automation date for <see cref="ColumnType.DateTime"/>,
    /// or the row's category index for <see cref="ColumnType.Text"/>. Missing values return NaN.
    /// </summary>
    public abstract double GetNumber(int row);

    /// <summary>
    /// A culture-invariant display string for <paramref name="row"/> used by import previews; an empty
    /// string for a missing value.
    /// </summary>
    public abstract string GetText(int row);
}
