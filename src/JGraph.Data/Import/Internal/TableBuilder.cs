using System.Globalization;

namespace JGraph.Data.Import.Internal;

/// <summary>
/// Turns a grid of raw string cells (from either the delimited-text tokenizer or the xlsx decoder) into
/// a typed <see cref="Table"/>: it detects/consumes a header row, names and pads columns, and infers
/// each column's type. Optional per-column <see cref="ColumnType"/> hints let the xlsx reader force a
/// column that Excel formatted as a date, or that Excel typed as text, past the generic inference.
/// </summary>
internal static class TableBuilder
{
    private const int MaxRaggedWarnings = 20;

    public static (Table Table, bool HasHeader, List<string> Warnings) Build(
        IReadOnlyList<string?[]> records,
        bool? hasHeaderOverride,
        CultureInfo culture,
        ColumnType?[]? columnHints = null)
    {
        var warnings = new List<string>();

        if (records.Count == 0)
        {
            throw new ImportException("No rows were found to import.");
        }

        int width = 0;
        foreach (string?[] record in records)
        {
            width = System.Math.Max(width, record.Length);
        }

        if (width == 0)
        {
            throw new ImportException("No columns were found to import.");
        }

        bool hasHeader = hasHeaderOverride ?? HeaderDetector.Detect(records, culture, width);

        string?[]? headerRow = hasHeader ? records[0] : null;
        int firstDataRow = hasHeader ? 1 : 0;
        int rowCount = records.Count - firstDataRow;

        // Warn about ragged rows (over the data region only).
        int raggedReported = 0;
        int raggedTotal = 0;
        for (int r = firstDataRow; r < records.Count; r++)
        {
            if (records[r].Length != width)
            {
                raggedTotal++;
                if (raggedReported < MaxRaggedWarnings)
                {
                    warnings.Add(
                        $"Row {r + 1}: {records[r].Length} field(s), expected {width} — padded with missing values.");
                    raggedReported++;
                }
            }
        }

        if (raggedTotal > raggedReported)
        {
            warnings.Add($"…and {raggedTotal - raggedReported} more row(s) with an unexpected field count.");
        }

        string[] names = BuildColumnNames(headerRow, width);

        var columns = new TableColumn[width];
        for (int c = 0; c < width; c++)
        {
            var cells = new string?[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                string?[] record = records[firstDataRow + r];
                cells[r] = c < record.Length ? record[c] : null;
            }

            ColumnType? hint = columnHints is not null && c < columnHints.Length ? columnHints[c] : null;
            columns[c] = BuildColumn(names[c], cells, culture, hint);
        }

        return (new Table(columns), hasHeader, warnings);
    }

    private static TableColumn BuildColumn(string name, string?[] cells, CultureInfo culture, ColumnType? hint)
    {
        switch (hint)
        {
            case ColumnType.Text:
                return ColumnTypeInference.BuildText(name, cells);
            case ColumnType.DateTime:
                if (ColumnTypeInference.TryBuildDateTime(name, cells, culture, out DateTimeColumn? dateTime))
                {
                    return dateTime!;
                }

                return ColumnTypeInference.BuildText(name, cells);
            case ColumnType.Number:
                if (ColumnTypeInference.TryBuildNumber(name, cells, culture, out NumberColumn? number))
                {
                    return number!;
                }

                return ColumnTypeInference.BuildText(name, cells);
            default:
                return ColumnTypeInference.Infer(name, cells, culture);
        }
    }

    private static string[] BuildColumnNames(string?[]? headerRow, int width)
    {
        var names = new string[width];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int c = 0; c < width; c++)
        {
            string raw = headerRow is not null && c < headerRow.Length ? headerRow[c] ?? string.Empty : string.Empty;
            raw = raw.Trim();
            string baseName = raw.Length > 0 ? raw : $"Column{c + 1}";

            string name = baseName;
            int suffix = 2;
            while (!seen.Add(name))
            {
                name = $"{baseName}_{suffix}";
                suffix++;
            }

            names[c] = name;
        }

        return names;
    }
}
