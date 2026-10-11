using System.Globalization;
using System.Text;

namespace JGraph.Maths.Ticks;

/// <summary>What is wrong with a tick label format, if anything.</summary>
public enum TickFormatProblem
{
    /// <summary>A format a ruler can write its numbers with.</summary>
    None,

    /// <summary>A conversion that does not write a number, such as <c>%s</c> or <c>%c</c>.</summary>
    UnsupportedType,

    /// <summary>No conversion at all, more than one, or one cut off in the middle.</summary>
    Invalid,

    /// <summary>A width or precision given as <c>*</c>, to be read from an argument.</summary>
    DynamicNumber,
}

/// <summary>
/// A ruler's tick label format (open item 97). A format holding a printf conversion — MATLAB's
/// <c>TickLabelFormat</c>, such as <c>'%.2f'</c>, <c>'$%,.2f'</c> or <c>'%g\x00B0'</c> — is written
/// the way R2025b writes it; anything else is the .NET custom format a figure saved before this kept
/// in the same property, and is still honoured.
/// </summary>
/// <remarks>
/// The printf reading replaced a translation into .NET custom formats, which could not say several of
/// MATLAB's: <c>'%g%%'</c> (percentage) came out multiplied by a hundred, because .NET's <c>%</c> scales,
/// and <c>'%+g'</c>, <c>'%5.1f'</c> and <c>'%,.2f'</c> lost their flags or were refused. Measured
/// against R2025b in probe_97c.
/// </remarks>
public static class TickFormats
{
    /// <summary>Whether a format is MATLAB's printf kind: it holds a conversion other than <c>%%</c>.</summary>
    public static bool IsPrintf(string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        for (int i = 0; i < format.Length - 1; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }

            if (format[i + 1] == '%')
            {
                i++;
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>One value written in a format of either kind.</summary>
    public static string Apply(double value, string format) => IsPrintf(format)
        ? Printf(value, format)
        : value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>
    /// A row of ticks' labels: in <paramref name="format"/>, or as R2025b writes them when there is
    /// none. R2025b's automatic labels and its <c>%g</c> with no precision are one rule
    /// (<see cref="AutoDecimals"/>), which needs the whole row, so a row is written at once.
    /// </summary>
    public static string[] Labels(IReadOnlyList<double> values, string? format)
    {
        var labels = new string[values.Count];
        if (!string.IsNullOrEmpty(format) && !IsPrintf(format))
        {
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = values[i].ToString(format, CultureInfo.CurrentCulture);
            }

            return labels;
        }

        int decimals = AutoDecimals(values);
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = string.IsNullOrEmpty(format) ? Fixed(values[i], decimals) : Printf(values[i], format, decimals);
        }

        return labels;
    }

    /// <summary>
    /// How many decimals R2025b writes a row of ticks with: four, or as many more as it takes to tell
    /// every tick from its neighbours, trailing zeros then dropped (probe_97b: 1/3 is 0.3333 and
    /// 12345.678 over 10⁴ is 1.2346, while ticks half a step of 10⁻⁵ apart are 1.234565).
    /// </summary>
    public static int AutoDecimals(IReadOnlyList<double> values)
    {
        for (int decimals = 4; decimals < 15; decimals++)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool distinct = true;
            for (int i = 0; i < values.Count && distinct; i++)
            {
                distinct = seen.Add(Fixed(values[i], decimals));
            }

            if (distinct)
            {
                return decimals;
            }
        }

        return 15;
    }

    /// <summary>A value at so many decimals with the trailing zeros dropped, and no sign on a zero.</summary>
    private static string Fixed(double value, int decimals)
    {
        if (!double.IsFinite(value))
        {
            return double.IsNaN(value) ? "NaN" : value > 0 ? "Inf" : "-Inf";
        }

        string written = Trim(value.ToString("F" + decimals, CultureInfo.InvariantCulture));
        return written == "-0" ? "0" : written;
    }

    /// <summary>
    /// What R2025b would refuse a ruler's <c>TickLabelFormat</c> or a heatmap's <c>CellLabelFormat</c>
    /// for (probe_103): a printf conversion that writes no number (<c>%s</c>, <c>%x</c>) is the wrong
    /// type, a <c>*</c> width is not a literal, and anything else that is not one conversion is invalid.
    /// </summary>
    public static TickFormatProblem Check(string format)
    {
        int conversions = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                i++;
                continue;
            }

            int cursor = SkipSpec(format, i + 1);
            if (cursor >= format.Length)
            {
                return TickFormatProblem.Invalid;
            }

            if (format[cursor] == '*')
            {
                return TickFormatProblem.DynamicNumber;
            }

            if ("fFgGeEdi".IndexOf(format[cursor]) < 0)
            {
                return "ouxXcs".IndexOf(format[cursor]) >= 0 ? TickFormatProblem.UnsupportedType : TickFormatProblem.Invalid;
            }

            conversions++;
            i = cursor;
        }

        return conversions == 1 ? TickFormatProblem.None : TickFormatProblem.Invalid;
    }

    /// <summary>
    /// One value in a printf format: the text around the conversion with its <c>%%</c> and
    /// <c>\xHHHH</c> escapes read, and the conversion's flags (<c>+ - space 0 # ,</c>), width and
    /// precision honoured. <c>%e</c> writes the power as R2025b's tick labels do, <c>1.5\times10^{3}</c>,
    /// which the TeX the labels are drawn through shows as a superscript.
    /// </summary>
    public static string Printf(double value, string format, int autoDecimals = -1) =>
        Write(value, format, autoDecimals, plain: false);

    /// <summary>
    /// One value as R2025b's <c>sprintf</c> writes it, which is how a heatmap writes its cell labels
    /// (open item 103, probe_103): <c>%e</c> is <c>1.500000e+03</c>, <c>%E</c> and <c>%G</c> write a capital
    /// E, a <c>%d</c> or <c>%i</c> given a value with a fraction writes it as <c>%e</c> does, and
    /// <c>\n</c> and <c>\t</c> are a newline and a tab.
    /// </summary>
    public static string Sprintf(double value, string format) =>
        Write(value, format, autoDecimals: -1, plain: true);

    private static string Write(double value, string format, int autoDecimals, bool plain)
    {
        var sb = new StringBuilder(format.Length + 8);
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '\\' && i + 1 < format.Length)
            {
                i = Escape(format, i, sb, plain);
                continue;
            }

            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                sb.Append('%');
                i++;
                continue;
            }

            int cursor = SkipSpec(format, i + 1);
            if (cursor >= format.Length)
            {
                sb.Append(format, i, format.Length - i);
                break;
            }

            sb.Append(Convert(value, format[(i + 1)..cursor], format[cursor], autoDecimals, plain));
            i = cursor;
        }

        return sb.ToString();
    }

    private static int SkipSpec(string format, int at)
    {
        while (at < format.Length && "+- 0#,.123456789".IndexOf(format[at]) >= 0)
        {
            at++;
        }

        return at;
    }

    /// <summary>
    /// A backslash escape: <c>\x</c> with its hex digits, or a backslash before itself; and, written as
    /// <c>sprintf</c> writes, <c>\n</c>, <c>\t</c> and <c>\r</c>.
    /// </summary>
    private static int Escape(string format, int at, StringBuilder sb, bool plain)
    {
        char next = format[at + 1];
        if (next == '\\')
        {
            sb.Append('\\');
            return at + 1;
        }

        if (plain && next is 'n' or 't' or 'r')
        {
            sb.Append(next switch { 'n' => '\n', 't' => '\t', _ => '\r' });
            return at + 1;
        }

        if (next == 'x')
        {
            int end = at + 2;
            while (end < format.Length && Uri.IsHexDigit(format[end]))
            {
                end++;
            }

            if (end > at + 2)
            {
                sb.Append((char)int.Parse(format.AsSpan(at + 2, end - at - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                return end - 1;
            }
        }

        sb.Append('\\');
        return at;
    }

    private static string Convert(double value, string spec, char conversion, int autoDecimals, bool plain)
    {
        // sprintf writes a whole-number conversion given a fraction as %e at its default precision.
        if (plain && conversion is 'd' or 'i' && double.IsFinite(value) && value != System.Math.Round(value))
        {
            conversion = 'e';
        }

        bool plus = false, space = false, left = false, zero = false, alternate = false, group = false;
        int i = 0;
        for (; i < spec.Length && "+- 0#,".IndexOf(spec[i]) >= 0; i++)
        {
            switch (spec[i])
            {
                case '+': plus = true; break;
                case ' ': space = true; break;
                case '-': left = true; break;
                case '0': zero = true; break;
                case '#': alternate = true; break;
                default: group = true; break;
            }
        }

        int width = 0;
        for (; i < spec.Length && char.IsDigit(spec[i]); i++)
        {
            width = (width * 10) + (spec[i] - '0');
        }

        int precision = -1;
        if (i < spec.Length && spec[i] == '.')
        {
            precision = 0;
            for (i++; i < spec.Length && char.IsDigit(spec[i]); i++)
            {
                precision = (precision * 10) + (spec[i] - '0');
            }
        }

        string body;
        bool negative = value < 0;
        double magnitude = System.Math.Abs(value);
        if (double.IsNaN(value))
        {
            body = "NaN";
            negative = false;
        }
        else if (double.IsInfinity(value))
        {
            body = "Inf";
        }
        else
        {
            body = conversion switch
            {
                'f' or 'F' => Group(magnitude.ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture), group),
                'e' or 'E' when plain => Scientific(magnitude, precision < 0 ? 6 : precision, conversion == 'E'),
                'e' or 'E' => Exponential(magnitude, precision < 0 ? 6 : precision),
                // A %g with no precision over a row of ticks is R2025b's automatic rule (Labels).
                'g' or 'G' when precision < 0 && autoDecimals >= 0 => Group(Fixed(magnitude, autoDecimals), group),
                'g' or 'G' => General(magnitude, precision < 0 ? 6 : System.Math.Max(precision, 1), alternate, plain && conversion == 'G'),
                _ => Group(System.Math.Round(magnitude, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture), group),
            };

            // A value that rounds to zero is written without its sign, as printf writes -0.001 at %.1f.
            if (negative && body.All(static ch => ch is '0' or '.' or ','))
            {
                negative = false;
            }
        }

        string sign = negative ? "-" : plus ? "+" : space ? " " : string.Empty;
        int pad = width - sign.Length - body.Length;
        if (pad <= 0)
        {
            return sign + body;
        }

        return left ? sign + body + new string(' ', pad)
            : zero && double.IsFinite(value) ? sign + new string('0', pad) + body
            : new string(' ', pad) + sign + body;
    }

    /// <summary>The integer part of a written number in groups of three, for the <c>,</c> flag.</summary>
    private static string Group(string digits, bool group)
    {
        if (!group)
        {
            return digits;
        }

        int point = digits.IndexOf('.');
        string whole = point < 0 ? digits : digits[..point];
        var sb = new StringBuilder(digits.Length + (whole.Length / 3));
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0)
            {
                sb.Append(',');
            }

            sb.Append(whole[i]);
        }

        return point < 0 ? sb.ToString() : sb.Append(digits, point, digits.Length - point).ToString();
    }

    private static string Exponential(double magnitude, int precision)
    {
        string written = magnitude.ToString("E" + precision, CultureInfo.InvariantCulture); // 1.50E+003
        int e = written.IndexOf('E');
        int power = int.Parse(written.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return written[..e] + "\\times10^{" + power.ToString(CultureInfo.InvariantCulture) + "}";
    }

    /// <summary>C's <c>%e</c>: <c>1.500000e+03</c>, the power signed and at least two digits.</summary>
    private static string Scientific(double magnitude, int precision, bool upper)
    {
        string written = magnitude.ToString("E" + precision, CultureInfo.InvariantCulture); // 1.50E+003
        int e = written.IndexOf('E');
        int power = int.Parse(written.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return written[..e] + (upper ? "E" : "e") + (power < 0 ? "-" : "+")
            + System.Math.Abs(power).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>C's <c>%g</c>: fixed or exponential by the power, trailing zeros dropped unless <c>#</c>.</summary>
    private static string General(double magnitude, int precision, bool alternate, bool upper = false)
    {
        if (magnitude == 0)
        {
            return alternate ? "0." + new string('0', precision - 1) : "0";
        }

        string rounded = magnitude.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
        int power = int.Parse(rounded.AsSpan(rounded.IndexOf('E') + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string written;
        if (power < -4 || power >= precision)
        {
            string mantissa = rounded[..rounded.IndexOf('E')];
            written = (alternate ? mantissa : Trim(mantissa)) + (upper ? "E" : "e") + (power < 0 ? "-" : "+")
                + System.Math.Abs(power).ToString("00", CultureInfo.InvariantCulture);
        }
        else
        {
            string fixedForm = magnitude.ToString("F" + System.Math.Max(precision - 1 - power, 0), CultureInfo.InvariantCulture);
            written = alternate ? fixedForm : Trim(fixedForm);
        }

        return written;
    }

    private static string Trim(string digits) =>
        digits.Contains('.') ? digits.TrimEnd('0').TrimEnd('.') : digits;
}
