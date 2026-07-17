namespace JGraph.Data;

/// <summary>
/// A UI-free grid projection of tabular data for the Data Viewer: column headers, formatted cell text,
/// and row-window paging so a million-row table never materializes as a million UI rows. Works over a
/// <see cref="Table"/> or a plain numeric array (shown as an index/value grid, MATLAB-style).
/// </summary>
public sealed class TableGridAdapter
{
    /// <summary>How many rows a page holds at most — the UI shows one page at a time.</summary>
    public const int PageSize = 10_000;

    private readonly Func<int, int, string> _cell;

    private TableGridAdapter(string title, IReadOnlyList<string> columnNames, int rowCount, Func<int, int, string> cell)
    {
        Title = title;
        ColumnNames = columnNames;
        RowCount = rowCount;
        _cell = cell;
    }

    /// <summary>A short caption for the viewer ("table 200×3", "array 1×64").</summary>
    public string Title { get; }

    /// <summary>The column headers, left to right.</summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>The total number of data rows (across all pages).</summary>
    public int RowCount { get; }

    /// <summary>How many pages the data spans (at least 1).</summary>
    public int PageCount => Math.Max(1, (RowCount + PageSize - 1) / PageSize);

    /// <summary>Views <paramref name="table"/> as a grid.</summary>
    public static TableGridAdapter ForTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new TableGridAdapter(
            $"table {table.RowCount}×{table.ColumnCount}",
            table.ColumnNames,
            table.RowCount,
            (row, column) => table.Columns[column].GetText(row));
    }

    /// <summary>Views a numeric array as an index/value grid.</summary>
    public static TableGridAdapter ForArray(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new TableGridAdapter(
            $"array 1×{values.Count}",
            new[] { "Index", "Value" },
            values.Count,
            (row, column) => column == 0
                ? row.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : values[row].ToString("G", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The formatted text of one cell (0-based absolute row and column).</summary>
    public string GetText(int row, int column)
    {
        if (row < 0 || row >= RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if (column < 0 || column >= ColumnNames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        return _cell(row, column);
    }

    /// <summary>
    /// One page of rows as formatted text, each row one string per column. Page 0 starts at row 0;
    /// the last page may be short. An empty grid yields an empty page.
    /// </summary>
    /// <param name="page">The 0-based page index (clamped into range).</param>
    /// <param name="firstRow">The absolute 0-based row index of the page's first row.</param>
    public IReadOnlyList<string[]> GetPage(int page, out int firstRow)
    {
        int clamped = Math.Clamp(page, 0, PageCount - 1);
        firstRow = clamped * PageSize;
        int count = Math.Min(PageSize, RowCount - firstRow);
        if (count <= 0)
        {
            return Array.Empty<string[]>();
        }

        var rows = new List<string[]>(count);
        for (int r = 0; r < count; r++)
        {
            var cells = new string[ColumnNames.Count];
            for (int c = 0; c < cells.Length; c++)
            {
                cells[c] = _cell(firstRow + r, c);
            }

            rows.Add(cells);
        }

        return rows;
    }
}
