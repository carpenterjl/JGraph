using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A datetime's <c>TimeZone</c> as R2025b reads and names it, for the constructor's option and the
/// property's setter alike (open item 209), and the format letters R2025b refuses outright.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The zone name a <c>TimeZone</c> value stands for: empty for none, R2025b's name for UTC or a
    /// fixed offset (<c>+0200</c> is <c>+02:00</c>), or R2025b's canonical name for a named zone.
    /// </summary>
    /// <remarks>
    /// R2025b's <c>verifyTimeZone</c> takes a string scalar, a character vector or a duration, which
    /// it writes <c>+hh:mm:ss</c> and reads as any other offset (so <c>hours(24)</c> is refused as
    /// <c>'+24:00:00'</c>); a duration with a fraction of a second is refused first. A spelling it
    /// cannot read is <c>MATLAB:datetime:UnknownTimeZone</c>, and a GMT-prefixed or aliased fixed
    /// offset is warned about as not following daylight saving.
    /// </remarks>
    private static string ReadTimeZoneArgument(JgsValue written, int line, int col)
    {
        string given;
        if (IsTextScalar(written))
        {
            given = TextOf(written);
        }
        else if (written.IsDuration && TimeMs(written) is [double ms] && !double.IsNaN(ms))
        {
            if (System.Math.Floor(ms / 1000) * 1000 < ms)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:datetime:InvalidDurationTZPrecision",
                    "A duration ISO 8601 offset for 'TimeZone' cannot have fractional seconds. Use the round function to round the duration to the nearest whole number of seconds.");
            }

            given = DurationZoneText(ms);
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:datetime:InvalidTimeZone",
                "A time zone must be specified as a string scalar, non-NaN duration scalar, or character vector.");
        }

        if (given.Length == 0)
        {
            return given;
        }

        // 'local', in any case, is the system's zone under R2025b's name for it (ADR 0350).
        if (string.Equals(given, "local", StringComparison.OrdinalIgnoreCase))
        {
            return JgsTimeZoneNames.LocalName();
        }

        switch (JgsTimeZones.Read(given, out string name, out _, out string? warnAs))
        {
            case ZoneReading.Fixed:
                if (warnAs is not null)
                {
                    WarnAboutZone(JgsTimeZones.FixedZoneWarningId, JgsTimeZones.FixedZoneWarning(warnAs, name));
                }

                return name;

            // A named zone is matched with its case and stored under R2025b's canonical name,
            // with a warning for an alias that does not name a single zone (ADR 0350).
            case ZoneReading.NotFixed when JgsTimeZoneNames.TryLookup(given, out string stored, out bool warns)
                                           && JgsTime.TryResolveZone(stored, out _):
                if (warns)
                {
                    WarnAboutZone(JgsTimeZoneNames.NonstandardWarningId, JgsTimeZoneNames.NonstandardWarning(given, stored));
                }

                return stored;

            default:
                throw UnknownTimeZone(given, line, col);
        }
    }

    /// <summary>
    /// The display tag and culture <c>string(t, fmt, locale)</c> and <c>char(t, fmt, locale)</c>
    /// format with: an empty second argument keeps the value's own format, text replaces it (a
    /// datetime's refused zone letters refused as the <c>Format</c> setter refuses them), and
    /// anything else is <c>MATLAB:datetime:InvalidFormat</c> (ADR 0350).
    /// </summary>
    private static (JgsTimeTag Tag, CultureInfo Culture) TimeFormatArguments(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsTimeTag tag = args[0].TimeTag!;
        if (args.Count > 1 && !IsEmptyValue(args[1]))
        {
            if (!IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:datetime:InvalidFormat",
                    "A datetime format must be specified as a non-empty and non-missing string or character row vector.");
            }

            string format = TextOf(args[1]);
            if (format.Length > 0)
            {
                if (args[0].IsDatetime)
                {
                    RefuseUnsupportedZoneLetters(format, line, col);
                }

                tag = tag with { Format = format };
            }
        }

        CultureInfo culture = args.Count > 2 && !IsEmptyValue(args[2])
            ? CultureInfo.GetCultureInfo(Str(verb, args, 2, line, col).Replace('_', '-'))
            : CultureInfo.InvariantCulture;
        return (tag, culture);
    }

    /// <summary>
    /// <c>string(t, fmt)</c> and <c>string(t, fmt, locale)</c>: each moment in that format, a
    /// missing one the missing string, the array's shape kept (ADR 0350).
    /// </summary>
    private static JgsValue FormattedTimeStrings(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("string", args, 1, 3, line, col);
        JgsValue time = args[0];
        (JgsTimeTag tag, CultureInfo culture) = TimeFormatArguments("string", args, line, col);
        var texts = new JgsValue[time.ArrayLength];
        for (int i = 0; i < texts.Length; i++)
        {
            double ms = time.ElementAt(i).AsNumber;
            texts[i] = JgsValue.Str(JgsRunningDialect.ThreadIsMatlab && double.IsNaN(ms)
                ? MissingSentinel
                : JgsTime.Format(ms, tag, culture));
        }

        return ShapedLike(time, texts).MarkStringArray();
    }

    /// <summary>R2025b's refusal of a zone it cannot read.</summary>
    private static JgsRuntimeException UnknownTimeZone(string given, int line, int col) =>
        new(line, col, JgsTimeZones.UnknownZoneId, JgsTimeZones.UnknownZoneMessage(given));

    /// <summary>A duration written as <c>string(tz, "hh:mm:ss")</c> with a plus sign when not negative.</summary>
    private static string DurationZoneText(double ms)
    {
        long total = (long)System.Math.Round(System.Math.Abs(ms) / 1000);
        string sign = ms < 0 ? "-" : "+";
        return string.Create(CultureInfo.InvariantCulture,
            $"{sign}{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}");
    }

    /// <summary>A warning from the zone reader, to the run on this thread (none outside a run).</summary>
    private static void WarnAboutZone(string identifier, string text)
    {
        if ((JgsLifetime.Current?.Interpreter.Host ?? JgsCallbackDispatcher.Current?.Interpreter?.Host) is { } host)
        {
            Warn(host, identifier, text);
        }
    }

    /// <summary>
    /// Refuses the zone letters R2025b refuses when a format is set (probe209b): <c>O</c> in any
    /// count as an unsupported symbol, and four or more <c>z</c> as the unsupported identifier
    /// <c>zzzz</c>. Quoted text is literal and is not read.
    /// </summary>
    private static void RefuseUnsupportedZoneLetters(string format, int line, int col)
    {
        int i = 0;
        while (i < format.Length)
        {
            char c = format[i];
            if (c == '\'')
            {
                int close = format.IndexOf('\'', i + 1);
                i = close < 0 ? format.Length : close + 1;
                continue;
            }

            int run = 1;
            while (i + run < format.Length && format[i + run] == c)
            {
                run++;
            }

            if (c == 'O')
            {
                throw new JgsRuntimeException(line, col, "MATLAB:datetime:UnsupportedSymbol",
                    $"The format '{format}' contains an unsupported symbol: 'O'. See the <a href=\"matlab:doc('datetime.Format')\">datetime.Format property</a> for a complete description of the identifiers used in datetime formats.");
            }

            if (c == 'z' && run >= 4)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:datetime:UnsupportedIdentifier",
                    $"The format '{format}' contains an unsupported identifier: 'zzzz'. See the <a href=\"matlab:doc('datetime.Format')\">datetime.Format property</a> for a complete description of the identifiers used in datetime formats.");
            }

            i += run;
        }
    }
}
