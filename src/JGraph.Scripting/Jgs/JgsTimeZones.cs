using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// How R2025b reads a <c>TimeZone</c> spelling that names a fixed offset, and the name it gives the
/// zone it makes (open item 209).
/// </summary>
/// <remarks>
/// <para>
/// R2025b's <c>verifyTimeZone</c> hands the text to a built-in canonicaliser, so the rule below is
/// measured rather than transcribed (probe209, probe209b and probe209c: about two hundred spellings
/// through the constructor and the setter). A fixed offset is named <c>+HH:mm</c>, or
/// <c>+HH:mm:ss</c> when its seconds are not zero, and a zero offset is <c>+00:00</c> whichever sign
/// it was written with. Each offset is up to 23:59:59 either way.
/// </para>
/// <list type="bullet">
/// <item><c>UTC</c> in any case, and <c>GMT</c> exactly, are <c>UTC</c>; <c>Z</c> and <c>z</c> are
/// <c>+00:00</c>.</item>
/// <item>A sign alone in front reads two-digit fields with colons (<c>+HH:mm</c>,
/// <c>+HH:mm:ss</c>) or one to six digits without (<c>H</c>, <c>HH</c>, <c>Hmm</c>, <c>HHmm</c>,
/// <c>Hmmss</c>, <c>HHmmss</c>, so <c>+020</c> is <c>+00:20</c>).</item>
/// <item><c>UTC</c> (any case) and a sign read an hour of one or two digits and optional
/// two-digit fields with colons (<c>UTC+2</c>, <c>UTC+2:00:30</c>), but not the digits-only forms
/// past two (<c>UTC+0200</c> is refused); a zero offset is <c>+00:00</c>.</item>
/// <item><c>UT</c> and <c>GMT</c> (any case) and a sign read both: the hour of one or two digits
/// with colons, or one to six digits. A zero offset after <c>UT</c> is <c>+00:00</c>; after
/// <c>GMT</c> it is <c>UTC</c>, and any other offset after <c>GMT</c> is warned about as not
/// following daylight saving, under its <c>GMT+HH:mm</c> name. <c>UT</c>, <c>gmt</c> and other
/// spellings of GMT but the exact one, with nothing after them, are <c>+00:00</c>.</item>
/// <item>The tz database's fixed zones: <c>Etc/GMT</c>, <c>Etc/GMT0</c>, <c>Etc/GMT+0</c>,
/// <c>Etc/GMT-0</c>, <c>Etc/Greenwich</c>, <c>Etc/UTC</c>, <c>Etc/UCT</c>, <c>Etc/Universal</c> and
/// <c>Etc/Zulu</c> are <c>+00:00</c>; <c>Etc/GMT-N</c> (N to 14) and <c>Etc/GMT+N</c> (N to 12) are
/// the offset of the opposite sign, as POSIX writes them; <c>UCT</c>, <c>Zulu</c>,
/// <c>Universal</c>, <c>Greenwich</c>, <c>GMT0</c>, <c>GMT+0</c> and <c>GMT-0</c> are <c>+00:00</c>
/// with the warning under the name as written. These are matched with their case.</item>
/// </list>
/// <para>
/// Anything else that starts like an offset — a space before or after, <c>+2:00</c>, <c>+02:0</c>,
/// <c>+02:00:00.5</c>, <c>02:00</c>, <c>+24:00</c> — is refused as
/// <c>MATLAB:datetime:UnknownTimeZone</c>. A name that is none of these is left to the zone database.
/// </para>
/// </remarks>
internal static class JgsTimeZones
{
    /// <summary>The identifier R2025b refuses an unreadable zone with.</summary>
    public const string UnknownZoneId = "MATLAB:datetime:UnknownTimeZone";

    /// <summary>The identifier of the warning a GMT-prefixed or aliased fixed offset gets.</summary>
    public const string FixedZoneWarningId = "MATLAB:datetime:NonstandardTimeZoneFixed";

    /// <summary>R2025b's refusal of a zone it cannot read, with the name as it was given.</summary>
    public static string UnknownZoneMessage(string given) =>
        $"Unable to create the specified time zone: '{given}'. See the <a href=\"matlab:doc('datetime.TimeZone')\">datetime.TimeZone property</a> for details about specifying time zones.";

    /// <summary>R2025b's warning that a fixed offset given a zone-like name follows no daylight saving.</summary>
    public static string FixedZoneWarning(string alias, string name) =>
        $"'{alias}' specifies a time zone with a fixed offset from UTC, {name}. This zone does not follow daylight saving time, and so may give unexpected results. See the <a href=\"matlab:doc('datetime.TimeZone')\">datetime.TimeZone property</a> for details about specifying time zones.";

    private static readonly HashSet<string> SilentZeroAliases = new(StringComparer.Ordinal)
    {
        "Etc/GMT", "Etc/GMT0", "Etc/GMT+0", "Etc/GMT-0", "Etc/Greenwich", "Etc/UTC", "Etc/UCT", "Etc/Universal", "Etc/Zulu",
    };

    private static readonly HashSet<string> WarnedZeroAliases = new(StringComparer.Ordinal)
    {
        "UCT", "Zulu", "Universal", "Greenwich", "GMT0", "GMT+0", "GMT-0",
    };

    /// <summary>
    /// Reads <paramref name="given"/> as R2025b reads a fixed-offset or UTC spelling.
    /// </summary>
    /// <param name="given">The text exactly as the script wrote it, untrimmed.</param>
    /// <param name="name">The name R2025b gives the zone: <c>UTC</c>, <c>+HH:mm</c> or <c>+HH:mm:ss</c>.</param>
    /// <param name="offset">The zone's offset from UTC (zero for <c>UTC</c>).</param>
    /// <param name="warnAs">The name the daylight-saving warning quotes, or null when none is due.</param>
    /// <returns>
    /// <see cref="ZoneReading.Fixed"/> when the spelling is read, <see cref="ZoneReading.Refused"/>
    /// when it looks like an offset but is not one R2025b reads, and <see cref="ZoneReading.NotFixed"/>
    /// when it is some other name, for the zone database.
    /// </returns>
    public static ZoneReading Read(string given, out string name, out TimeSpan offset, out string? warnAs)
    {
        name = string.Empty;
        offset = TimeSpan.Zero;
        warnAs = null;

        if (string.Equals(given, "UTC", StringComparison.OrdinalIgnoreCase) || given == "GMT")
        {
            name = "UTC";
            return ZoneReading.Fixed;
        }

        if (given is "Z" or "z" || SilentZeroAliases.Contains(given))
        {
            name = "+00:00";
            return ZoneReading.Fixed;
        }

        if (WarnedZeroAliases.Contains(given))
        {
            name = "+00:00";
            warnAs = given;
            return ZoneReading.Fixed;
        }

        if (given.StartsWith("Etc/GMT", StringComparison.Ordinal))
        {
            return ReadEtc(given[7..], out name, out offset);
        }

        if (given.Length > 0 && given[0] is '+' or '-')
        {
            return Finish(TryBody(given, allowBasic: true, twoDigitHour: true, out offset), offset, ref name);
        }

        if (given.StartsWith("UTC", StringComparison.OrdinalIgnoreCase))
        {
            string rest = given[3..];
            return rest.Length > 0 && rest[0] is '+' or '-'
                ? Finish(TryBody(rest, allowBasic: false, twoDigitHour: false, out offset), offset, ref name)
                : ZoneReading.Refused;
        }

        if (given.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
        {
            string rest = given[3..];
            if (rest.Length == 0)
            {
                // Only the exact spelling is UTC (above); gmt, Gmt and the rest are a zero offset.
                name = "+00:00";
                return ZoneReading.Fixed;
            }

            if (rest[0] is not ('+' or '-') || !TryBody(rest, allowBasic: true, twoDigitHour: false, out offset))
            {
                return ZoneReading.Refused;
            }

            if (offset == TimeSpan.Zero)
            {
                name = "UTC";
                return ZoneReading.Fixed;
            }

            name = NameOf(offset);
            warnAs = "GMT" + name;
            return ZoneReading.Fixed;
        }

        if (given.StartsWith("UT", StringComparison.OrdinalIgnoreCase))
        {
            string rest = given[2..];
            if (rest.Length == 0)
            {
                name = "+00:00";
                return ZoneReading.Fixed;
            }

            return rest[0] is '+' or '-'
                ? Finish(TryBody(rest, allowBasic: true, twoDigitHour: false, out offset), offset, ref name)
                : ZoneReading.NotFixed;
        }

        // A name that cannot be a zone database id is refused here too, so the database lookup never
        // sees text that only a trim would make readable.
        return given.Length == 0 || char.IsWhiteSpace(given[0]) || char.IsWhiteSpace(given[^1])
            ? ZoneReading.Refused
            : ZoneReading.NotFixed;
    }

    /// <summary>
    /// The offset a fixed-offset spelling stands for, whatever spelling it is: R2025b's name for it,
    /// or any other spelling <see cref="Read"/> accepts, such as the <c>+HH:mm:ss</c> a datetime
    /// from Python crosses with (ADR 0269).
    /// </summary>
    public static bool TryOffsetOf(string name, out TimeSpan offset)
    {
        if (Read(name, out _, out offset, out _) == ZoneReading.Fixed)
        {
            return true;
        }

        offset = TimeSpan.Zero;
        return false;
    }

    /// <summary>R2025b's name for a fixed offset: <c>+HH:mm</c>, with <c>:ss</c> when there are seconds.</summary>
    public static string NameOf(TimeSpan offset)
    {
        long total = (long)System.Math.Round(offset.TotalSeconds);
        char sign = total < 0 ? '-' : '+';
        total = System.Math.Abs(total);
        long hours = total / 3600, minutes = total / 60 % 60, seconds = total % 60;
        return seconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{hours:00}:{minutes:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{hours:00}:{minutes:00}:{seconds:00}");
    }

    private static ZoneReading Finish(bool read, TimeSpan offset, ref string name)
    {
        if (!read)
        {
            return ZoneReading.Refused;
        }

        name = NameOf(offset);
        return ZoneReading.Fixed;
    }

    /// <summary><c>Etc/GMT-N</c> and <c>Etc/GMT+N</c>, POSIX's sign: <c>Etc/GMT-2</c> is two hours east.</summary>
    private static ZoneReading ReadEtc(string rest, out string name, out TimeSpan offset)
    {
        name = string.Empty;
        offset = TimeSpan.Zero;
        if (rest.Length < 2 || rest[0] is not ('+' or '-') || rest[1] == '0'
            || !int.TryParse(rest.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || hours > (rest[0] == '-' ? 14 : 12))
        {
            return ZoneReading.Refused;
        }

        offset = TimeSpan.FromHours(rest[0] == '-' ? hours : -hours);
        name = NameOf(offset);
        return ZoneReading.Fixed;
    }

    /// <summary>
    /// A sign and the offset's fields. With colons: the hour (two digits when
    /// <paramref name="twoDigitHour"/>, otherwise one or two), then two-digit minutes and optionally
    /// two-digit seconds. Without (only when <paramref name="allowBasic"/>, and always for one or two
    /// digits): one to six digits, the hour taking one digit when the count is odd.
    /// </summary>
    private static bool TryBody(string text, bool allowBasic, bool twoDigitHour, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (text.Length < 2 || text[0] is not ('+' or '-'))
        {
            return false;
        }

        ReadOnlySpan<char> body = text.AsSpan(1);
        foreach (char c in body)
        {
            if (c is not (':' or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        int hours, minutes = 0, seconds = 0;
        if (body.Contains(':'))
        {
            string[] parts = body.ToString().Split(':');
            if (parts.Length > 3 || parts[0].Length is < 1 or > 2 || (twoDigitHour && parts[0].Length != 2))
            {
                return false;
            }

            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length != 2)
                {
                    return false;
                }
            }

            hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
            seconds = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
        }
        else
        {
            int n = body.Length;
            if (n > 6 || (!allowBasic && n > 2))
            {
                return false;
            }

            int hourDigits = n <= 2 ? n : 2 - (n % 2);
            hours = int.Parse(body[..hourDigits], CultureInfo.InvariantCulture);
            if (n > 2)
            {
                minutes = int.Parse(body.Slice(hourDigits, 2), CultureInfo.InvariantCulture);
            }

            if (n > 4)
            {
                seconds = int.Parse(body.Slice(hourDigits + 2, 2), CultureInfo.InvariantCulture);
            }
        }

        if (hours > 23 || minutes > 59 || seconds > 59)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, seconds);
        if (text[0] == '-')
        {
            offset = -offset;
        }

        return true;
    }

    // --- Formatting a zone's offset (open item 209) ----------------------------------------------

    /// <summary>
    /// An offset in one of ICU's ISO 8601 forms, as R2025b's x, X and Z letters print it: the hour,
    /// then minutes and seconds up to <paramref name="maxField"/> (1 minutes, 2 seconds), trailing
    /// zero fields dropped down to <paramref name="minField"/> (0 hours, 1 minutes), with or without
    /// colons, and <c>Z</c> for a zero offset when <paramref name="utcZ"/> (zero after the seconds are
    /// dropped, for the forms that drop them). A sign is minus only when a printed field is not zero.
    /// </summary>
    public static string Iso(TimeSpan offset, int minField, int maxField, bool colons, bool utcZ)
    {
        long total = (long)System.Math.Round(offset.TotalSeconds);
        if (utcZ && (total == 0 || (maxField == 1 && total / 60 == 0)))
        {
            return "Z";
        }

        bool negative = total < 0;
        total = System.Math.Abs(total);
        long[] fields = [total / 3600, total / 60 % 60, total % 60];
        int last = maxField;
        while (last > minField && fields[last] == 0)
        {
            last--;
        }

        if (negative)
        {
            negative = false;
            for (int i = 0; i <= last; i++)
            {
                negative |= fields[i] != 0;
            }
        }

        var built = new System.Text.StringBuilder(9);
        built.Append(negative ? '-' : '+').Append(fields[0].ToString("00", CultureInfo.InvariantCulture));
        for (int i = 1; i <= last; i++)
        {
            if (colons)
            {
                built.Append(':');
            }

            built.Append(fields[i].ToString("00", CultureInfo.InvariantCulture));
        }

        return built.ToString();
    }

    /// <summary>The long localized form R2025b prints for ZZZZ: <c>UTC</c>, or <c>UTC+HH:mm</c> with <c>:ss</c> when there are seconds.</summary>
    public static string LongGmt(TimeSpan offset) =>
        System.Math.Round(offset.TotalSeconds) == 0 ? "UTC" : "UTC" + NameOf(offset);

    /// <summary>
    /// The short form R2025b prints for z on a fixed-offset zone: <c>GMT</c> for a zero offset,
    /// otherwise <c>UTC</c>, the sign and the hour without its leading zero, then <c>:mm</c> when the
    /// minutes or seconds are not zero and <c>:ss</c> when the seconds are not.
    /// </summary>
    public static string ShortGmt(TimeSpan offset)
    {
        long total = (long)System.Math.Round(offset.TotalSeconds);
        if (total == 0)
        {
            return "GMT";
        }

        char sign = total < 0 ? '-' : '+';
        total = System.Math.Abs(total);
        long hours = total / 3600, minutes = total / 60 % 60, seconds = total % 60;
        string text = string.Create(CultureInfo.InvariantCulture, $"UTC{sign}{hours}");
        if (minutes != 0 || seconds != 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $":{minutes:00}");
        }

        if (seconds != 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $":{seconds:00}");
        }

        return text;
    }
}

/// <summary>What <see cref="JgsTimeZones.Read"/> made of a zone spelling.</summary>
internal enum ZoneReading
{
    /// <summary>Not a fixed-offset spelling: a name for the zone database.</summary>
    NotFixed,

    /// <summary>A fixed offset (or UTC), read and named.</summary>
    Fixed,

    /// <summary>A spelling R2025b refuses as an unknown time zone.</summary>
    Refused,
}
