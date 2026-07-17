namespace JGraph.Data.Import;

/// <summary>Where an import wizard obtained its data.</summary>
public enum ImportSourceKind
{
    /// <summary>No source has been loaded yet.</summary>
    None,

    /// <summary>A delimited-text file (CSV/TSV/…).</summary>
    DelimitedFile,

    /// <summary>An <c>.xlsx</c> workbook.</summary>
    XlsxFile,

    /// <summary>Text pasted from the clipboard.</summary>
    ClipboardText,
}

/// <summary>The kind of plot to build from imported columns.</summary>
public enum ImportPlotKind
{
    Line,
    Scatter,
    Bar,
    Stem,
    Histogram,
    ErrorBar,
}

/// <summary>Where the imported plots should be placed.</summary>
public enum ImportTarget
{
    /// <summary>Create a new figure for the imported plots.</summary>
    NewFigure,

    /// <summary>Append the imported plots to the current axes.</summary>
    CurrentAxes,
}
