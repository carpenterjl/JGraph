using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The formatter behind the JGS <c>sprintf</c> builtin: a fixed C/MATLAB subset —
/// <c>%d %i %f %e %g %s %x %%</c>, with optional width (<c>%8d</c>, zero-padded <c>%08d</c>,
/// left-aligned <c>%-8s</c>) and precision (<c>%.2f</c>, <c>%.3g</c>). Invariant culture throughout.
/// Anything else is a runtime error rather than a silent pass-through, so typos surface immediately.
/// </summary>
internal static class JgsSprintf
{
    /// <summary>Formats <paramref name="format"/> with <paramref name="args"/>; throws <see cref="FormatException"/> with a user-facing message on any misuse.</summary>
    public static string Format(string format, IReadOnlyList<JgsValue> args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int argIndex = 0;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
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

            int specStart = i;
            i++; // past '%'

            bool leftAlign = false;
            bool zeroPad = false;
            while (i < format.Length && (format[i] == '-' || format[i] == '0'))
            {
                if (format[i] == '-')
                {
                    leftAlign = true;
                }
                else
                {
                    zeroPad = true;
                }

                i++;
            }

            int width = 0;
            while (i < format.Length && char.IsAsciiDigit(format[i]))
            {
                width = (width * 10) + (format[i] - '0');
                i++;
            }

            int precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                precision = 0;
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    precision = (precision * 10) + (format[i] - '0');
                    i++;
                }
            }

            if (i >= format.Length)
            {
                throw new FormatException($"sprintf format ends inside the specifier \"{format[specStart..]}\".");
            }

            char verb = format[i];
            if (verb is not ('d' or 'i' or 'f' or 'e' or 'g' or 's' or 'x'))
            {
                throw new FormatException($"sprintf does not support the specifier \"%{verb}\" (supported: %d %i %f %e %g %s %x %%).");
            }

            if (argIndex >= args.Count)
            {
                throw new FormatException($"sprintf format needs more arguments: nothing left for \"{format[specStart..(i + 1)]}\".");
            }

            JgsValue arg = args[argIndex++];
            string text = FormatOne(verb, precision, arg);

            if (zeroPad && !leftAlign && text.Length < width && verb is not 's')
            {
                // Re-pad after any leading sign so -007 comes out right.
                bool negative = text.StartsWith('-');
                string digits = negative ? text[1..] : text;
                text = (negative ? "-" : "") + digits.PadLeft(width - (negative ? 1 : 0), '0');
            }
            else if (text.Length < width)
            {
                text = leftAlign ? text.PadRight(width) : text.PadLeft(width);
            }

            sb.Append(text);
        }

        if (argIndex < args.Count)
        {
            throw new FormatException($"sprintf got {args.Count - argIndex} more argument(s) than the format uses.");
        }

        return sb.ToString();
    }

    private static string FormatOne(char verb, int precision, JgsValue arg)
    {
        if (verb == 's')
        {
            return arg.Display();
        }

        if (arg.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new FormatException($"sprintf \"%{verb}\" needs a number, but got a {arg.TypeName}.");
        }

        double value = arg.AsNumber;
        return verb switch
        {
            'd' or 'i' => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture),
            'f' => value.ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture),
            'e' => value.ToString("0." + new string('0', precision < 0 ? 6 : precision) + "e+00", CultureInfo.InvariantCulture),
            'g' => FormatGeneral(value, precision),
            'x' => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString("x", CultureInfo.InvariantCulture),
            _ => throw new FormatException($"sprintf does not support the specifier \"%{verb}\"."),
        };
    }

    private static string FormatGeneral(double value, int precision)
    {
        // %g: shortest of fixed/scientific at the given significant digits (default 6, like C).
        int digits = precision <= 0 ? 6 : precision;
        return value.ToString("G" + digits, CultureInfo.InvariantCulture)
            .Replace("E+0", "e+", StringComparison.Ordinal)
            .Replace("E-0", "e-", StringComparison.Ordinal)
            .Replace("E+", "e+", StringComparison.Ordinal)
            .Replace("E-", "e-", StringComparison.Ordinal);
    }
}
