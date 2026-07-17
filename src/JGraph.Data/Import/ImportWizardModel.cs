using System.Globalization;

namespace JGraph.Data.Import;

/// <summary>
/// The UI-free state model behind the data-import wizard. It loads a source (a delimited-text file, an
/// xlsx workbook, or pasted text), re-parses whenever a parse option changes, and tracks the
/// column-to-plot mapping. All the wizard's decisions — default mapping, which plot kinds are valid for
/// the current mapping, and whether a plot can be built — live here so they can be unit-tested without a
/// WPF host. A thin view model wraps this and forwards property changes to the UI.
/// </summary>
public sealed class ImportWizardModel
{
    private readonly HashSet<string> _selectedY = new(StringComparer.Ordinal);

    private string? _clipboardText;
    private char? _delimiter;
    private bool? _hasHeader;
    private CultureInfo? _culture;
    private int _skipRows;
    private string? _sheetName;

    private string? _xColumn;
    private string? _errorColumn;
    private ImportPlotKind _plotKind = ImportPlotKind.Line;
    private int _histogramBins = 10;

    /// <summary>Raised after every (re)parse, so a view can refresh its preview and column lists.</summary>
    public event EventHandler? Parsed;

    /// <summary>Where the current data came from.</summary>
    public ImportSourceKind SourceKind { get; private set; } = ImportSourceKind.None;

    /// <summary>The loaded file path, or null for pasted text / no source.</summary>
    public string? FilePath { get; private set; }

    /// <summary>The worksheet names of a loaded xlsx workbook (empty otherwise).</summary>
    public IReadOnlyList<string> SheetNames { get; private set; } = Array.Empty<string>();

    /// <summary>The last successful parse result, or null when the source failed to parse.</summary>
    public ImportResult? Result { get; private set; }

    /// <summary>The error message from the last failed parse, or null on success.</summary>
    public string? Error { get; private set; }

    /// <summary>Where the built plots should go.</summary>
    public ImportTarget Target { get; set; } = ImportTarget.NewFigure;

    // ----- Parse options (each triggers a re-parse) -----

    public char? Delimiter
    {
        get => _delimiter;
        set { if (_delimiter != value) { _delimiter = value; Reparse(); } }
    }

    public bool? HasHeader
    {
        get => _hasHeader;
        set { if (_hasHeader != value) { _hasHeader = value; Reparse(); } }
    }

    public CultureInfo? Culture
    {
        get => _culture;
        set { if (!Equals(_culture, value)) { _culture = value; Reparse(); } }
    }

    public int SkipRows
    {
        get => _skipRows;
        set { int v = System.Math.Max(0, value); if (_skipRows != v) { _skipRows = v; Reparse(); } }
    }

    public string? SheetName
    {
        get => _sheetName;
        set { if (_sheetName != value) { _sheetName = value; Reparse(); } }
    }

    // ----- Column mapping -----

    /// <summary>The X column, or null to plot against row indices.</summary>
    public string? XColumn
    {
        get => _xColumn;
        set { _xColumn = value; SnapPlotKind(); }
    }

    /// <summary>The selected Y columns, in table column order.</summary>
    public IReadOnlyList<string> YColumns => OrderByTable(_selectedY);

    /// <summary>The error column for an error-bar plot, or null.</summary>
    public string? ErrorColumn
    {
        get => _errorColumn;
        set { _errorColumn = value; SnapPlotKind(); }
    }

    /// <summary>The plot kind to build.</summary>
    public ImportPlotKind PlotKind
    {
        get => _plotKind;
        set => _plotKind = value;
    }

    /// <summary>The bin count used when <see cref="PlotKind"/> is <see cref="ImportPlotKind.Histogram"/>.</summary>
    public int HistogramBins
    {
        get => _histogramBins;
        set => _histogramBins = System.Math.Max(1, value);
    }

    /// <summary>All column names (candidate X columns); the view also offers a "row index" choice for null.</summary>
    public IReadOnlyList<string> XColumnChoices =>
        Result is null ? Array.Empty<string>() : Result.Table.ColumnNames;

    /// <summary>The numeric (number/date) column names — the legal Y and error columns.</summary>
    public IReadOnlyList<string> NumericColumnNames
    {
        get
        {
            if (Result is null)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (TableColumn column in Result.Table.Columns)
            {
                if (column.Type is ColumnType.Number or ColumnType.DateTime)
                {
                    names.Add(column.Name);
                }
            }

            return names;
        }
    }

    /// <summary>The plot kinds valid for the current column mapping.</summary>
    public IReadOnlyList<ImportPlotKind> AllowedPlotKinds
    {
        get
        {
            var kinds = new List<ImportPlotKind>();
            if (Result is null || _selectedY.Count == 0)
            {
                return kinds;
            }

            IReadOnlyList<string> ys = YColumns;
            bool allNumericY = ys.All(IsNumeric);
            bool xIsText = _xColumn is not null && IsType(_xColumn, ColumnType.Text);

            kinds.Add(ImportPlotKind.Line);
            kinds.Add(ImportPlotKind.Scatter);
            kinds.Add(ImportPlotKind.Stem);

            if (allNumericY)
            {
                kinds.Add(ImportPlotKind.Histogram);
            }

            if (ys.Count == 1 && (_xColumn is null || xIsText || IsType(_xColumn, ColumnType.Number)))
            {
                kinds.Add(ImportPlotKind.Bar);
            }

            if (ys.Count == 1 && !xIsText && HasSpareNumberColumn(ys[0]))
            {
                kinds.Add(ImportPlotKind.ErrorBar);
            }

            return kinds;
        }
    }

    /// <summary>Whether a plot can be built from the current mapping.</summary>
    public bool CanBuild
    {
        get
        {
            if (Result is null || _selectedY.Count == 0 || !AllowedPlotKinds.Contains(_plotKind))
            {
                return false;
            }

            if (_plotKind == ImportPlotKind.ErrorBar)
            {
                return _errorColumn is not null && IsType(_errorColumn, ColumnType.Number)
                    && !_selectedY.Contains(_errorColumn);
            }

            return true;
        }
    }

    /// <summary>Loads a file, dispatching to the delimited-text or xlsx reader by extension.</summary>
    public void LoadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        FilePath = path;
        _clipboardText = null;

        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            SourceKind = ImportSourceKind.XlsxFile;
            try
            {
                SheetNames = XlsxReader.GetSheetNames(path);
            }
            catch (ImportException)
            {
                SheetNames = Array.Empty<string>();
            }

            _sheetName = SheetNames.Count > 0 ? SheetNames[0] : null;
        }
        else
        {
            SourceKind = ImportSourceKind.DelimitedFile;
            SheetNames = Array.Empty<string>();
            _sheetName = null;
        }

        Reparse();
    }

    /// <summary>Loads a table from pasted clipboard text.</summary>
    public void LoadClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SourceKind = ImportSourceKind.ClipboardText;
        FilePath = null;
        SheetNames = Array.Empty<string>();
        _sheetName = null;
        _clipboardText = text;
        Reparse();
    }

    /// <summary>Re-parses the current source with the current options and resets the mapping.</summary>
    public void Reparse()
    {
        if (SourceKind == ImportSourceKind.None)
        {
            Result = null;
            Error = null;
            return;
        }

        try
        {
            Result = ParseCurrentSource();
            Error = null;
        }
        catch (ImportException ex)
        {
            Result = null;
            Error = ex.Message;
        }

        ResetMapping();
        Parsed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds or removes a Y column from the selection.</summary>
    public void SetYColumnSelected(string column, bool selected)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (selected)
        {
            _selectedY.Add(column);
        }
        else
        {
            _selectedY.Remove(column);
        }

        SnapPlotKind();
    }

    /// <summary>Whether a Y column is currently selected.</summary>
    public bool IsYColumnSelected(string column) => _selectedY.Contains(column);

    /// <summary>Builds the plot specification for the current mapping.</summary>
    public TablePlotSpec BuildSpec()
    {
        if (!CanBuild || Result is null)
        {
            throw new InvalidOperationException("The current selection cannot build a plot.");
        }

        return new TablePlotSpec(Result.Table, _plotKind, _xColumn, YColumns, _errorColumn, _histogramBins);
    }

    private ImportResult ParseCurrentSource() => SourceKind switch
    {
        ImportSourceKind.DelimitedFile => DelimitedTextReader.Read(FilePath!, BuildOptions()),
        ImportSourceKind.XlsxFile => XlsxReader.Read(FilePath!, BuildOptions()),
        ImportSourceKind.ClipboardText => ClipboardTableParser.Parse(_clipboardText!, BuildOptions()),
        _ => throw new ImportException("No source has been loaded."),
    };

    private ImportOptions BuildOptions() => new()
    {
        Delimiter = _delimiter,
        HasHeader = _hasHeader,
        Culture = _culture,
        SkipRows = _skipRows,
        SheetName = _sheetName,
    };

    private void ResetMapping()
    {
        _selectedY.Clear();
        _xColumn = null;
        _errorColumn = null;

        if (Result is null)
        {
            return;
        }

        Table table = Result.Table;

        // Default X: the first date/time column, else the first number column, else row index (null).
        foreach (TableColumn column in table.Columns)
        {
            if (column.Type == ColumnType.DateTime)
            {
                _xColumn = column.Name;
                break;
            }
        }

        if (_xColumn is null)
        {
            foreach (TableColumn column in table.Columns)
            {
                if (column.Type == ColumnType.Number)
                {
                    _xColumn = column.Name;
                    break;
                }
            }
        }

        // Default Y: every number column that is not the X column.
        foreach (TableColumn column in table.Columns)
        {
            if (column.Type == ColumnType.Number && column.Name != _xColumn)
            {
                _selectedY.Add(column.Name);
            }
        }

        SnapPlotKind();
    }

    private void SnapPlotKind()
    {
        IReadOnlyList<ImportPlotKind> allowed = AllowedPlotKinds;
        if (allowed.Count > 0 && !allowed.Contains(_plotKind))
        {
            _plotKind = allowed[0];
        }
    }

    private IReadOnlyList<string> OrderByTable(HashSet<string> names)
    {
        if (Result is null || names.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ordered = new List<string>();
        foreach (string columnName in Result.Table.ColumnNames)
        {
            if (names.Contains(columnName))
            {
                ordered.Add(columnName);
            }
        }

        return ordered;
    }

    private bool HasSpareNumberColumn(string yColumn)
    {
        if (Result is null)
        {
            return false;
        }

        foreach (TableColumn column in Result.Table.Columns)
        {
            if (column.Type == ColumnType.Number && column.Name != yColumn && column.Name != _xColumn)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsNumeric(string columnName) =>
        Result is not null && Result.Table.TryGetColumn(columnName, out TableColumn column)
        && column.Type is ColumnType.Number or ColumnType.DateTime;

    private bool IsType(string columnName, ColumnType type) =>
        Result is not null && Result.Table.TryGetColumn(columnName, out TableColumn column) && column.Type == type;
}
