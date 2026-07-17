using System.Globalization;

namespace JGraph.Data.Import.Internal;

/// <summary>
/// Decides whether the first record of a grid is a header row. The primary signal: some column whose
/// body parses uniformly as numbers/dates but whose first cell is non-empty and does not — the classic
/// "label over a numeric column" shape. For an all-text table (no numeric/date column), the first row
/// is a header only when its cells are all non-empty and distinct.
/// </summary>
internal static class HeaderDetector
{
    public static bool Detect(IReadOnlyList<string?[]> records, CultureInfo culture, int width)
    {
        if (records.Count < 2)
        {
            return false; // a single row is data, not a header
        }

        string?[] first = records[0];
        bool anyNumericOrDateColumn = false;

        for (int c = 0; c < width; c++)
        {
            bool bodyIsNumericOrDate = ColumnBodyIsNumericOrDate(records, c, culture);
            if (!bodyIsNumericOrDate)
            {
                continue;
            }

            anyNumericOrDateColumn = true;

            string? firstCell = c < first.Length ? first[c] : null;
            if (string.IsNullOrEmpty(firstCell))
            {
                continue;
            }

            bool firstParses = ColumnTypeInference.TryParseNumber(firstCell, culture, out _)
                || ColumnTypeInference.TryParseDate(firstCell, culture, out _);
            if (!firstParses)
            {
                return true; // a label sitting over a numeric/date column
            }
        }

        if (anyNumericOrDateColumn)
        {
            return false; // has numeric/date columns but the first row parses like data
        }

        // All-text table: treat the first row as a header when it is fully populated and distinct.
        return FirstRowIsDistinctAndComplete(first, width);
    }

    private static bool ColumnBodyIsNumericOrDate(IReadOnlyList<string?[]> records, int column, CultureInfo culture)
    {
        bool anyValue = false;
        for (int r = 1; r < records.Count; r++)
        {
            string?[] record = records[r];
            string? cell = column < record.Length ? record[column] : null;
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            anyValue = true;
            bool parses = ColumnTypeInference.TryParseNumber(cell, culture, out _)
                || ColumnTypeInference.TryParseDate(cell, culture, out _);
            if (!parses)
            {
                return false;
            }
        }

        return anyValue;
    }

    private static bool FirstRowIsDistinctAndComplete(string?[] first, int width)
    {
        if (first.Length < width)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < width; c++)
        {
            string? cell = first[c];
            if (string.IsNullOrWhiteSpace(cell) || !seen.Add(cell.Trim()))
            {
                return false;
            }
        }

        return true;
    }
}
