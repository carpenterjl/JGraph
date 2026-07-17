namespace JGraph.Data.Import;

/// <summary>
/// A declarative description of the plots to build from a <see cref="Table"/>: the plot kind, the X
/// column (null for implicit row indices), one or more Y columns, and — for error bars — the error
/// column. Consumed by the plot builder in the objects layer so the import wizard can be tested without
/// referencing the plot types.
/// </summary>
public sealed record TablePlotSpec(
    Table Table,
    ImportPlotKind Kind,
    string? XColumn,
    IReadOnlyList<string> YColumns,
    string? ErrorColumn = null,
    int HistogramBins = 10);
