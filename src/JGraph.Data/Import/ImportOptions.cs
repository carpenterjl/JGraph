using System.Globalization;

namespace JGraph.Data.Import;

/// <summary>
/// Options controlling how delimited text or a workbook is parsed. Every property defaults to null/zero
/// meaning "auto-detect"; set one to override the corresponding heuristic. Options are immutable — build
/// a new instance (via <c>with</c>) to change a setting.
/// </summary>
public sealed record ImportOptions
{
    /// <summary>The field delimiter; null auto-detects among comma, semicolon, tab, and pipe. Ignored for xlsx.</summary>
    public char? Delimiter { get; init; }

    /// <summary>Whether the first row is a header; null auto-detects.</summary>
    public bool? HasHeader { get; init; }

    /// <summary>The culture used to parse numbers and dates; null auto-detects (invariant vs comma-decimal).</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>The number of leading raw lines (or rows) to discard before parsing.</summary>
    public int SkipRows { get; init; }

    /// <summary>For xlsx, the worksheet to read (case-insensitive); null reads the first worksheet.</summary>
    public string? SheetName { get; init; }
}
