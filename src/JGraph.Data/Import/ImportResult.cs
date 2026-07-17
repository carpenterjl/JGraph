using System.Globalization;

namespace JGraph.Data.Import;

/// <summary>
/// The outcome of a successful import: the parsed <see cref="Table"/> plus the settings that were
/// actually used (as detected or overridden) and any non-fatal <see cref="Warnings"/>.
/// </summary>
public sealed class ImportResult
{
    public ImportResult(
        Table table,
        char delimiter,
        bool hasHeader,
        CultureInfo culture,
        IReadOnlyList<string> warnings)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Delimiter = delimiter;
        HasHeader = hasHeader;
        Culture = culture ?? throw new ArgumentNullException(nameof(culture));
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>The parsed table.</summary>
    public Table Table { get; }

    /// <summary>The delimiter used ('\0' for xlsx, which has no delimiter).</summary>
    public char Delimiter { get; }

    /// <summary>Whether the first row was treated as a header.</summary>
    public bool HasHeader { get; }

    /// <summary>The culture used to parse numbers and dates.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Human-readable notes about recoverable issues (e.g. padded short rows, skipped error cells).</summary>
    public IReadOnlyList<string> Warnings { get; }
}
