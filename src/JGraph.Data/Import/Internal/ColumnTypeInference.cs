using System.Globalization;
using JGraph.Core.Model;

namespace JGraph.Data.Import.Internal;

/// <summary>
/// Infers a <see cref="TableColumn"/> from a column of raw string cells (null or empty = missing).
/// Numbers are tried before dates so an integer column never becomes a date; if neither fits, the
/// column is text. Also exposes the number/date parse helpers used by header detection.
/// </summary>
internal static class ColumnTypeInference
{
    /// <summary>Infers and builds a column, letting the cell values decide the type.</summary>
    public static TableColumn Infer(string name, IReadOnlyList<string?> cells, CultureInfo culture)
    {
        if (TryBuildNumber(name, cells, culture, out NumberColumn? number))
        {
            return number!;
        }

        if (TryBuildDateTime(name, cells, culture, out DateTimeColumn? dateTime))
        {
            return dateTime!;
        }

        return BuildText(name, cells);
    }

    /// <summary>Builds a number column when every non-empty cell parses as a number.</summary>
    public static bool TryBuildNumber(string name, IReadOnlyList<string?> cells, CultureInfo culture, out NumberColumn? column)
    {
        bool anyValue = false;
        foreach (string? cell in cells)
        {
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            anyValue = true;
            if (!TryParseNumber(cell, culture, out _))
            {
                column = null;
                return false;
            }
        }

        if (!anyValue)
        {
            column = null;
            return false;
        }

        var values = new double[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            string? cell = cells[i];
            values[i] = string.IsNullOrEmpty(cell) ? double.NaN : ParseNumber(cell, culture);
        }

        column = new NumberColumn(name, values);
        return true;
    }

    /// <summary>Builds a date column when every non-empty cell parses as a date/time.</summary>
    public static bool TryBuildDateTime(string name, IReadOnlyList<string?> cells, CultureInfo culture, out DateTimeColumn? column)
    {
        bool anyValue = false;
        foreach (string? cell in cells)
        {
            if (string.IsNullOrEmpty(cell))
            {
                continue;
            }

            anyValue = true;
            if (!TryParseDate(cell, culture, out _))
            {
                column = null;
                return false;
            }
        }

        if (!anyValue)
        {
            column = null;
            return false;
        }

        var values = new double[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            string? cell = cells[i];
            if (string.IsNullOrEmpty(cell))
            {
                values[i] = double.NaN;
                continue;
            }

            TryParseDate(cell, culture, out DateTime dt);
            values[i] = DateTimeAxis.ToValue(dt);
        }

        column = new DateTimeColumn(name, values);
        return true;
    }

    /// <summary>Builds a text column, treating empty cells as missing (null).</summary>
    public static TextColumn BuildText(string name, IReadOnlyList<string?> cells)
    {
        var strings = new string?[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            string? cell = cells[i];
            strings[i] = string.IsNullOrEmpty(cell) ? null : cell;
        }

        return new TextColumn(name, strings);
    }

    public static bool TryParseNumber(string text, CultureInfo culture, out double value) =>
        double.TryParse(text, NumberStyles.Float, culture, out value);

    private static double ParseNumber(string text, CultureInfo culture) =>
        double.Parse(text, NumberStyles.Float, culture);

    public static bool TryParseDate(string text, CultureInfo culture, out DateTime value)
    {
        if (DateTime.TryParse(text, culture, DateTimeStyles.None, out value))
        {
            return true;
        }

        // ISO-8601 fallback (invariant), for files that use canonical timestamps regardless of culture.
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
