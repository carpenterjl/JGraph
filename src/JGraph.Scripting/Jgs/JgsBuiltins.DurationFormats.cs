namespace JGraph.Scripting.Jgs;

/// <summary>
/// The display and input formats R2025b lets a duration have (open item 342, ADR 0375, probes p7e2
/// and p7e4): a display format is one of the single units <c>y</c>, <c>d</c>, <c>h</c>, <c>m</c>,
/// <c>s</c>, or one of the clocks <c>dd:hh:mm:ss</c>, <c>hh:mm:ss</c>, <c>mm:ss</c>, <c>hh:mm</c>, the
/// first three with a dot and up to nine <c>S</c> after them; every other text, the empty one and a
/// different case are <c>MATLAB:duration:UnrecognizedFormat</c>. An input format is one of the four
/// clocks, the same fractions allowed, and anything else <c>MATLAB:duration:UnrecognizedInputFormat</c>.
/// The MATLAB dialect only; JGS keeps reading an unknown format as <c>hh:mm:ss</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Whether R2025b takes <paramref name="format"/> as a duration's display format.</summary>
    private static bool IsDurationDisplayFormat(string format) =>
        format is "y" or "d" or "h" or "m" or "s" || IsDurationClockFormat(format);

    /// <summary>Whether <paramref name="format"/> is one of the four clocks, with a fraction where R2025b allows one.</summary>
    private static bool IsDurationClockFormat(string format)
    {
        if (format == "hh:mm")
        {
            return true;
        }

        int dot = format.IndexOf('.');
        string clock = dot < 0 ? format : format[..dot];
        if (clock is not ("dd:hh:mm:ss" or "hh:mm:ss" or "mm:ss"))
        {
            return false;
        }

        if (dot < 0)
        {
            return true;
        }

        string fraction = format[(dot + 1)..];
        return fraction.Length <= 9 && fraction.All(static c => c == 'S');
    }

    /// <summary>
    /// A duration's display format as the <c>Format</c> setter, the constructor's <c>'Format'</c> and
    /// <c>char</c>, <c>string</c> and <c>cellstr</c> read it in the MATLAB dialect: text R2025b takes, or
    /// its refusal. Anything but text is <c>MATLAB:duration:InvalidFormat</c>.
    /// </summary>
    private static string DurationDisplayFormat(JgsValue written, int line, int col)
    {
        if (!IsTextScalar(written))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:duration:InvalidFormat",
                "A duration display format must be a character vector or string scalar.");
        }

        string format = TextOf(written);
        RefuseDurationDisplayFormat(format, line, col);
        return format;
    }

    /// <summary>R2025b's refusal of a duration display format it does not take, in the MATLAB dialect.</summary>
    private static void RefuseDurationDisplayFormat(string format, int line, int col)
    {
        if (JgsRunningDialect.ThreadIsMatlab && !IsDurationDisplayFormat(format))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:duration:UnrecognizedFormat", $"The format '{format}' is not valid.");
        }
    }

    /// <summary>R2025b's refusal of a duration <c>InputFormat</c> it does not take, in the MATLAB dialect.</summary>
    private static void RefuseDurationInputFormat(JgsValue written, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return;
        }

        if (!IsTextScalar(written))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:duration:InvalidInputFormat",
                "A duration InputFormat must be a character vector or string scalar.");
        }

        string format = TextOf(written);
        if (!IsDurationClockFormat(format))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:duration:UnrecognizedInputFormat",
                $"Unsupported format '{format}'. See the documentation of 'InputFormat' for valid formats.");
        }
    }
}
