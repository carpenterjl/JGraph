using JGraph.Data.Import;

namespace JGraph.Data;

/// <summary>
/// An immutable, column-oriented table of imported data: a set of equally-sized, uniquely-named
/// <see cref="TableColumn"/>s. A table is a data <em>source</em> (like <c>ArrayDataSeries</c>), not part
/// of the plot model; a column pair becomes a plot series via <see cref="TableSeries"/>. Construct one
/// directly, or read one from a file with <see cref="ReadCsv"/>/<see cref="ReadXlsx"/>/<see cref="Parse"/>.
/// </summary>
public sealed class Table
{
    private readonly TableColumn[] _columns;
    private readonly Dictionary<string, int> _byName;

    /// <summary>
    /// Creates a table from <paramref name="columns"/> (used directly, not copied). Every column must
    /// share the same <see cref="TableColumn.RowCount"/>, and names must be unique (case-insensitive).
    /// </summary>
    /// <exception cref="ArgumentException">Columns differ in length or share a name.</exception>
    public Table(IReadOnlyList<TableColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        _columns = new TableColumn[columns.Count];
        _byName = new Dictionary<string, int>(columns.Count, StringComparer.OrdinalIgnoreCase);

        int rowCount = columns.Count > 0 ? columns[0].RowCount : 0;
        for (int i = 0; i < columns.Count; i++)
        {
            TableColumn column = columns[i] ?? throw new ArgumentException("Columns cannot be null.", nameof(columns));
            if (column.RowCount != rowCount)
            {
                throw new ArgumentException(
                    $"All columns must have the same row count; '{column.Name}' has {column.RowCount}, expected {rowCount}.",
                    nameof(columns));
            }

            if (!_byName.TryAdd(column.Name, i))
            {
                throw new ArgumentException($"Duplicate column name '{column.Name}'.", nameof(columns));
            }

            _columns[i] = column;
        }

        RowCount = rowCount;
    }

    /// <summary>The number of rows.</summary>
    public int RowCount { get; }

    /// <summary>The number of columns.</summary>
    public int ColumnCount => _columns.Length;

    /// <summary>The columns, in order.</summary>
    public IReadOnlyList<TableColumn> Columns => _columns;

    /// <summary>The column names, in order.</summary>
    public IReadOnlyList<string> ColumnNames => Array.ConvertAll(_columns, c => c.Name);

    /// <summary>The column with the given name (case-insensitive).</summary>
    /// <exception cref="KeyNotFoundException">No column has that name.</exception>
    public TableColumn this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            if (_byName.TryGetValue(name, out int index))
            {
                return _columns[index];
            }

            throw new KeyNotFoundException(
                $"No column named '{name}'. Available columns: {string.Join(", ", ColumnNames)}.");
        }
    }

    /// <summary>The column at <paramref name="index"/>.</summary>
    public TableColumn this[int index] => _columns[index];

    /// <summary>Looks up a column by name (case-insensitive) without throwing.</summary>
    public bool TryGetColumn(string name, out TableColumn column)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_byName.TryGetValue(name, out int index))
        {
            column = _columns[index];
            return true;
        }

        column = null!;
        return false;
    }

    /// <summary>Reads a table from a delimited-text (CSV/TSV) file.</summary>
    public static Table ReadCsv(string path, ImportOptions? options = null) =>
        DelimitedTextReader.Read(path, options).Table;

    /// <summary>Reads a table from the first (or named) worksheet of an <c>.xlsx</c> workbook.</summary>
    public static Table ReadXlsx(string path, ImportOptions? options = null) =>
        XlsxReader.Read(path, options).Table;

    /// <summary>Parses a table from delimited text held in a string.</summary>
    public static Table Parse(string text, ImportOptions? options = null) =>
        DelimitedTextReader.Parse(text, options).Table;
}
