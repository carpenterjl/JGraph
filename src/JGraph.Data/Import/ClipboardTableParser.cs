namespace JGraph.Data.Import;

/// <summary>
/// Parses a table pasted as text (e.g. a range copied from Excel, which arrives tab-delimited). When a
/// tab is present it is used as the delimiter; otherwise the usual auto-detection applies.
/// </summary>
public static class ClipboardTableParser
{
    /// <summary>A cheap heuristic for whether pasted text looks like a table (to enable a Paste command).</summary>
    public static bool LooksLikeTable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains('\t'))
        {
            return true;
        }

        bool multiline = text.Contains('\n');
        return multiline && (text.Contains(',') || text.Contains(';') || text.Contains('|'));
    }

    /// <summary>Parses pasted text into a table.</summary>
    public static ImportResult Parse(string text, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new ImportOptions();
        if (options.Delimiter is null && text.Contains('\t'))
        {
            options = options with { Delimiter = '\t' };
        }

        return DelimitedTextReader.Parse(text, options);
    }
}
