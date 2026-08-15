using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>Which of MATLAB's two time types an array is carrying (M64).</summary>
internal enum JgsTimeKind : byte
{
    /// <summary>A point in time: <c>datetime</c>.</summary>
    Datetime,

    /// <summary>A signed length of time: <c>duration</c>.</summary>
    Duration,
}

/// <summary>
/// What a time-tagged array remembers about itself: which of the two types it is, the format it
/// displays in, and — for a datetime — the zone it was read in.
/// </summary>
/// <remarks>
/// One record rather than three fields on <see cref="JgsValue"/>, because the three always travel
/// together: every path that carries the tag across a copy carries all of it or none, and a record
/// makes that impossible to get half-right.
/// </remarks>
/// <param name="Kind">Datetime or duration.</param>
/// <param name="Format">The display format, in MATLAB's own tokens.</param>
/// <param name="TimeZone">The zone name for a zoned datetime, or null for an unzoned one.</param>
internal sealed record JgsTimeTag(JgsTimeKind Kind, string Format, string? TimeZone = null);

/// <summary>
/// The arithmetic, formatting and parsing behind <c>datetime</c> and <c>duration</c> (M64).
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage is milliseconds, and the epoch is 1899-12-30.</b> A datetime is the number of
/// milliseconds since that instant; a duration is a signed count of milliseconds. Two consequences
/// pay for the choice. Whole milliseconds are exact in a double out to about 285,000 years, so
/// <c>seconds(1) + seconds(2) == seconds(3)</c> holds — which it does not if a duration is stored in
/// days, where the three sixteenths of a millisecond that fall off the end of the division make the
/// comparison false. And the epoch is the one <see cref="JGraph.Core.Model.DateTimeAxis"/> already
/// uses, so a datetime reaches the graphics pipeline by a single divide and reaches
/// <c>datenum</c> by a divide and an addition.
/// </para>
/// <para>
/// Nothing here knows about <see cref="JgsValue"/>'s shape or storage: a time value is an ordinary
/// numeric array wearing a <see cref="JgsTimeTag"/>, so indexing, growth, reshaping, masks and
/// concatenation are the array machinery that was already there, and only meaning is added.
/// </para>
/// </remarks>
internal static class JgsTime
{
    /// <summary>Milliseconds in a day — the divisor between this storage and an axis value.</summary>
    public const double MsPerDay = 86_400_000.0;

    /// <summary>Milliseconds in an hour.</summary>
    public const double MsPerHour = 3_600_000.0;

    /// <summary>Milliseconds in a minute.</summary>
    public const double MsPerMinute = 60_000.0;

    /// <summary>Milliseconds in a second.</summary>
    public const double MsPerSecond = 1000.0;

    /// <summary>
    /// The days MATLAB's serial date number counts before this epoch, so
    /// <c>datenum(1970, 1, 1) == 719529</c> here as it does there.
    /// </summary>
    public const double DatenumOffset = 693960.0;

    /// <summary>The instant milliseconds are counted from: the OLE automation epoch.</summary>
    public static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>MATLAB's default datetime display when a value has a time of day.</summary>
    public const string DefaultDatetimeFormat = "dd-MMM-uuuu HH:mm:ss";

    /// <summary>MATLAB's default datetime display when every value falls on midnight.</summary>
    public const string DateOnlyFormat = "dd-MMM-uuuu";

    /// <summary>MATLAB's default duration display.</summary>
    public const string DefaultDurationFormat = "hh:mm:ss";

    /// <summary>The missing datetime, <c>NaT</c> — and, in a duration, the missing length.</summary>
    public const double NotATime = double.NaN;

    // --- Conversion --------------------------------------------------------------------------

    /// <summary>The instant <paramref name="ms"/> stands for.</summary>
    public static DateTime ToDateTime(double ms) =>
        Epoch.AddTicks((long)System.Math.Round(ms * TimeSpan.TicksPerMillisecond));

    /// <summary>The storage value for <paramref name="moment"/>.</summary>
    public static double FromDateTime(DateTime moment) =>
        (moment - Epoch).Ticks / (double)TimeSpan.TicksPerMillisecond;

    /// <summary>The storage value for a year/month/day and an optional time of day.</summary>
    /// <remarks>
    /// Months and days past the end of their range roll over, which is MATLAB's own behaviour and the
    /// reason <c>datetime(2024, 13, 1)</c> is January 2025 rather than an error. Building from
    /// January the 1st and adding is what gives that for free.
    /// </remarks>
    public static double FromComponents(double year, double month, double day, double hour, double minute, double second)
    {
        if (double.IsNaN(year) || double.IsNaN(month) || double.IsNaN(day)
            || double.IsNaN(hour) || double.IsNaN(minute) || double.IsNaN(second))
        {
            return NotATime;
        }

        var start = new DateTime((int)System.Math.Clamp(year, 1, 9999), 1, 1);
        double ms = FromDateTime(start.AddMonths((int)month - 1));
        return ms
            + ((day - 1) * MsPerDay)
            + (hour * MsPerHour)
            + (minute * MsPerMinute)
            + (second * MsPerSecond);
    }

    /// <summary>The axis value (OLE automation date) for a datetime's storage.</summary>
    public static double ToAxisValue(double ms) => ms / MsPerDay;

    /// <summary>The MATLAB serial date number for a datetime's storage.</summary>
    public static double ToDatenum(double ms) => (ms / MsPerDay) + DatenumOffset;

    /// <summary>The storage for a MATLAB serial date number.</summary>
    public static double FromDatenum(double serial) => (serial - DatenumOffset) * MsPerDay;

    // --- Formatting --------------------------------------------------------------------------

    /// <summary>Renders one storage value through its tag's own format.</summary>
    public static string Format(double ms, JgsTimeTag tag) =>
        tag.Kind == JgsTimeKind.Datetime ? FormatDatetime(ms, tag.Format) : FormatDuration(ms, tag.Format);

    private static string FormatDatetime(double ms, string format)
    {
        if (double.IsNaN(ms))
        {
            return "NaT";
        }

        DateTime moment = ToDateTime(ms);
        try
        {
            return moment.ToString(ToNetFormat(format), CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return moment.ToString(ToNetFormat(DefaultDatetimeFormat), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Translates MATLAB's date format tokens into .NET's. Only the tokens that differ are touched,
    /// which is why a format a script wrote for <c>datestr</c> in .NET tokens keeps working.
    /// </summary>
    /// <remarks>
    /// The substitution runs left to right over the string rather than as a set of replacements,
    /// because a replacement pass would rewrite the output of an earlier one: turning <c>uuuu</c>
    /// into <c>yyyy</c> and then <c>a</c> into <c>tt</c> is safe, but only because nothing produces
    /// an <c>a</c>. Scanning once removes the need to reason about that at all.
    /// </remarks>
    public static string ToNetFormat(string format)
    {
        var built = new System.Text.StringBuilder(format.Length + 4);
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i];
            int run = 1;
            while (i + run < format.Length && format[i + run] == c)
            {
                run++;
            }

            switch (c)
            {
                case 'u':
                    built.Append('y', run);          // uuuu is MATLAB's year; .NET spells it yyyy
                    break;
                case 'S':
                    built.Append('f', run);          // SSS is fractional seconds; .NET spells it fff
                    break;
                case 'E':
                    built.Append('d', System.Math.Max(run, 3));  // EEE is the day name, .NET's ddd
                    break;
                case 'a':
                    built.Append("tt");              // a is AM/PM, .NET's tt
                    break;
                default:
                    built.Append(c, run);
                    break;
            }

            i += run;
        }

        return built.ToString();
    }

    private static string FormatDuration(double ms, string format)
    {
        if (double.IsNaN(ms))
        {
            return "NaN";
        }

        string sign = ms < 0 ? "-" : string.Empty;
        double magnitude = System.Math.Abs(ms);

        // The single-unit formats say how much of one unit the whole span is, so they do not divide
        // into parts at all: duration(25, 0, 0) in 'h' is 25, not 1 with a day carried off it.
        switch (format)
        {
            case "s":
                return Trimmed(ms / MsPerSecond) + " sec";
            case "m":
                return Trimmed(ms / MsPerMinute) + " min";
            case "h":
                return Trimmed(ms / MsPerHour) + " hr";
            case "d":
                return Trimmed(ms / MsPerDay) + " day";
            case "y":
                return Trimmed(ms / (MsPerDay * 365.2425)) + " yr";
        }

        double totalSeconds = magnitude / MsPerSecond;
        long wholeSeconds = (long)System.Math.Floor(totalSeconds);
        double fraction = totalSeconds - wholeSeconds;
        long seconds = wholeSeconds % 60;
        long totalMinutes = wholeSeconds / 60;
        long minutes = totalMinutes % 60;
        long totalHours = totalMinutes / 60;

        string secondText = fraction > 0
            ? (seconds + fraction).ToString("00.###", CultureInfo.InvariantCulture)
            : seconds.ToString("00", CultureInfo.InvariantCulture);

        return format switch
        {
            "mm:ss" => $"{sign}{totalMinutes:00}:{secondText}",
            "dd:hh:mm:ss" =>
                $"{sign}{totalHours / 24:00}:{totalHours % 24:00}:{minutes:00}:{secondText}",

            // hh:mm:ss, and anything unrecognized. Hours are the whole span rather than a time of
            // day, so a duration of twenty-five hours reads 25:00:00 and not 01:00:00.
            _ => $"{sign}{totalHours:00}:{minutes:00}:{secondText}",
        };
    }

    private static string Trimmed(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    // --- Parsing -----------------------------------------------------------------------------

    /// <summary>
    /// Reads <paramref name="text"/> as a datetime, either in an explicit
    /// <paramref name="inputFormat"/> or by trying the shapes MATLAB recognizes without being told.
    /// </summary>
    /// <returns>True when the text was a date, with the storage value in <paramref name="ms"/>.</returns>
    public static bool TryParse(string text, string? inputFormat, out double ms)
    {
        ms = NotATime;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (string.Equals(trimmed, "NaT", StringComparison.OrdinalIgnoreCase))
        {
            return true;    // NaT is a datetime; its value is the missing one.
        }

        if (inputFormat is not null)
        {
            if (DateTime.TryParseExact(trimmed, ToNetFormat(inputFormat), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime exact))
            {
                ms = FromDateTime(exact);
                return true;
            }

            return false;
        }

        // The formats MATLAB tries for itself, most specific first. ISO order comes before the
        // day-first spellings so that 2024-01-15 is never read as a day of month.
        string[] guesses =
        [
            "uuuu-MM-dd HH:mm:ss", "uuuu-MM-dd'T'HH:mm:ss", "uuuu-MM-dd",
            "dd-MMM-uuuu HH:mm:ss", "dd-MMM-uuuu",
            "MM/dd/uuuu HH:mm:ss", "MM/dd/uuuu",
            "HH:mm:ss",
        ];

        foreach (string guess in guesses)
        {
            if (DateTime.TryParseExact(trimmed, ToNetFormat(guess), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsed))
            {
                ms = FromDateTime(parsed);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads <paramref name="text"/> as a duration written <c>hh:mm:ss</c>, <c>dd:hh:mm:ss</c> or
    /// <c>mm:ss</c>, which is what <c>duration('01:30:00')</c> means.
    /// </summary>
    public static bool TryParseDuration(string text, out double ms)
    {
        ms = NotATime;
        string trimmed = text.Trim();
        bool negative = trimmed.StartsWith('-');
        if (negative)
        {
            trimmed = trimmed[1..];
        }

        string[] parts = trimmed.Split(':');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        double total = parts.Length switch
        {
            2 => (values[0] * MsPerMinute) + (values[1] * MsPerSecond),
            3 => (values[0] * MsPerHour) + (values[1] * MsPerMinute) + (values[2] * MsPerSecond),
            _ => (values[0] * MsPerDay) + (values[1] * MsPerHour)
                 + (values[2] * MsPerMinute) + (values[3] * MsPerSecond),
        };

        ms = negative ? -total : total;
        return true;
    }

    // --- Tag defaults ------------------------------------------------------------------------

    /// <summary>
    /// The tag a freshly built datetime takes: date-only when every value falls on midnight, and the
    /// full display otherwise, which is the rule MATLAB's own constructors follow.
    /// </summary>
    public static JgsTimeTag DatetimeTag(ReadOnlySpan<double> values, string? timeZone = null)
    {
        foreach (double ms in values)
        {
            if (!double.IsNaN(ms) && System.Math.Abs(ms % MsPerDay) > 0)
            {
                return new JgsTimeTag(JgsTimeKind.Datetime, DefaultDatetimeFormat, timeZone);
            }
        }

        return new JgsTimeTag(JgsTimeKind.Datetime, DateOnlyFormat, timeZone);
    }

    /// <summary>The tag a freshly built duration takes.</summary>
    public static JgsTimeTag DurationTag(string format = DefaultDurationFormat) =>
        new(JgsTimeKind.Duration, format);
}
