using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's month and weekday names per locale, read from the recorded table
/// (<c>JgsLocaleNames.Table.cs</c>). .NET's culture data is a different CLDR from R2025b's ICU and
/// differs in places: French abbreviations carry a period there (<c>janv.</c>, R2025b's
/// <c>janv</c>), a Russian or Polish wide month is the nominative there and the genitive in R2025b,
/// and .NET has no narrow month or weekday at all (open item 316, ADR 0359).
/// </summary>
internal static partial class JgsLocaleNames
{
    /// <summary>A locale's names: months January first, weekdays Sunday first, then AM and PM.</summary>
    internal sealed record Names(
        string[] MonthsShort, string[] MonthsWide, string[] MonthsNarrow,
        string[] DaysShort, string[] DaysWide, string[] DaysNarrow, string[] Periods);

    private static readonly Lazy<Dictionary<string, Names>> Table = new(Parse);

    /// <summary>
    /// The names R2025b prints for a culture, or null for a locale the table does not hold. The
    /// invariant culture, which formatting without a locale uses, reads as R2025b's <c>en_US</c>.
    /// </summary>
    public static Names? For(CultureInfo culture)
    {
        string key = culture.Name.Length == 0 ? "en_US" : culture.Name.Replace('-', '_');
        return Table.Value.TryGetValue(key, out Names? names) ? names : null;
    }

    private static Dictionary<string, Names> Parse()
    {
        var fields = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in NameTable.Split('\n'))
        {
            string row = line.TrimEnd('\r');
            if (row.Length == 0)
            {
                continue;
            }

            string[] parts = row.Split('|');
            if (!fields.TryGetValue(parts[0], out Dictionary<string, string[]>? locale))
            {
                locale = new Dictionary<string, string[]>(StringComparer.Ordinal);
                fields[parts[0]] = locale;
            }

            locale[parts[1]] = parts[2].Split(';');
        }

        var table = new Dictionary<string, Names>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, Dictionary<string, string[]> f) in fields)
        {
            table[name] = new Names(f["MMM"], f["MMMM"], f["MMMMM"], f["eee"], f["eeee"], f["eeeee"], f["a"]);
        }

        return table;
    }
}
