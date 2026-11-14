namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reading the generated tables in <c>JgsTimeZoneNames.cs</c>: a named zone as R2025b matches,
/// canonicalises and abbreviates it (open item 311, ADR 0350).
/// </summary>
internal static partial class JgsTimeZoneNames
{
    private static readonly Lazy<Dictionary<string, (string Stored, bool Warns)>> Names = new(() =>
    {
        var names = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        foreach (string line in NameTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ');
            names[parts[0]] = (parts[1] == "=" ? parts[0] : parts[1], parts.Length > 2 && parts[2] == "!");
        }

        return names;
    });

    private static readonly Lazy<Dictionary<string, (string? Standard, string? Daylight)>> Abbreviations = new(() =>
    {
        var abbreviations = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (string line in AbbreviationTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ');
            abbreviations[parts[0]] = (parts[1] == "-" ? null : parts[1], parts[2] == "-" ? null : parts[2]);
        }

        return abbreviations;
    });

    /// <summary>
    /// The name R2025b stores for <paramref name="name"/>, matched with its case, and whether it
    /// warns that the name does not specify a single zone; false when R2025b refuses the name.
    /// </summary>
    /// <remarks>
    /// R2025b stores ICU's canonical name, which is the CLDR one: <c>Asia/Kolkata</c> is stored as
    /// <c>Asia/Calcutta</c> and <c>Europe/Kyiv</c> as <c>Europe/Kiev</c> with no word said, while an
    /// old-style alias such as <c>US/Eastern</c>, <c>EST</c> or <c>GB</c> is stored canonically with
    /// the warning. A Windows zone id is refused.
    /// </remarks>
    public static bool TryLookup(string name, out string stored, out bool warns)
    {
        if (Names.Value.TryGetValue(name, out (string Stored, bool Warns) entry))
        {
            stored = entry.Stored;
            warns = entry.Warns;
            return true;
        }

        stored = name;
        warns = false;
        return false;
    }

    /// <summary>
    /// The 'en' short name R2025b's <c>z</c> prints for a stored zone, standard or daylight; null
    /// where the locale has none, and R2025b prints the offset instead.
    /// </summary>
    public static string? Abbreviation(string stored, bool daylight) =>
        Abbreviations.Value.TryGetValue(stored, out (string? Standard, string? Daylight) pair)
            ? (daylight ? pair.Daylight : pair.Standard)
            : null;

    /// <summary>Every name R2025b stores a named zone under, each once.</summary>
    public static IEnumerable<string> StoredNames => Names.Value.Values.Select(static v => v.Stored).Distinct(StringComparer.Ordinal);

    /// <summary>R2025b's warning for a name it reads as another, in its words (the property is a link).</summary>
    public static string NonstandardWarning(string given, string stored) =>
        $"'{given}' does not specify a single time zone unambiguously. It will be treated as '{stored}'. " +
        "See the <a href=\"matlab:doc('datetime.TimeZone')\">datetime.TimeZone property</a> for details about specifying time zones.";

    /// <summary>The identifier of that warning.</summary>
    public const string NonstandardWarningId = "MATLAB:datetime:NonstandardTimeZone";

    /// <summary>
    /// The system's zone under R2025b's name for it, which is what <c>'local'</c> stores: the IANA
    /// name the system maps to (on Windows through the CLDR table .NET carries), read through the
    /// table like any written name. UTC when the system's zone has no name R2025b knows.
    /// </summary>
    public static string LocalName()
    {
        TimeZoneInfo local = TimeZoneInfo.Local;
        string id = local.Id;
        if (!local.HasIanaId && TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out string? iana))
        {
            id = iana;
        }

        return TryLookup(id, out string stored, out _) ? stored : "UTC";
    }
}
