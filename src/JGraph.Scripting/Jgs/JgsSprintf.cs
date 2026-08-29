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
    /// <summary>
    /// How many values one pass over <paramref name="format"/> consumes. <c>compose</c> needs it to
    /// hand out its values in groups: a format with two specifiers makes one answer from two values,
    /// not two answers from one each.
    /// </summary>
    public static int SpecifierCount(string format)
    {
        int count = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                i++; // an escaped percent is a character, not a slot
                continue;
            }

            count++;
            while (i + 1 < format.Length && !char.IsAsciiLetter(format[i + 1]))
            {
                i++;
            }
        }

        return count;
    }

    /// <summary>Formats <paramref name="format"/> with <paramref name="args"/>; throws <see cref="FormatException"/> with a user-facing message on any misuse.</summary>
    public static string Format(string format, IReadOnlyList<JgsValue> args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int argIndex = 0;
        Emit(sb, format, args, ref argIndex, stopWhenExhausted: false);

        if (argIndex < args.Count)
        {
            throw new FormatException($"sprintf got {args.Count - argIndex} more argument(s) than the format uses.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// MATLAB's reading (M43): array arguments flatten into one value stream, and the format
    /// repeats until every value is consumed — <c>sprintf('%d,', 1:5)</c> is <c>1,2,3,4,5,</c>.
    /// A pass that runs out of values mid-format stops there, as MATLAB does.
    /// </summary>
    public static string FormatMatlab(string format, IReadOnlyList<JgsValue> args)
    {
        var stream = new List<JgsValue>();
        foreach (JgsValue arg in args)
        {
            // A char matrix feeds one piece of text, not one value per code point (M105) — and the
            // text is its storage order, so fprintf('%s', ['a  '; 'bcd']) prints 'ab c d' exactly as
            // MATLAB's does. Spreading it would have handed %s the numbers underneath.
            if (arg.IsCharMatrix)
            {
                stream.Add(JgsValue.Str(arg.CharMatrixText()));
            }
            else if (arg.Type == JgsType.Array)
            {
                stream.AddRange(arg.BoxedElements());
            }
            else
            {
                stream.Add(arg);
            }
        }

        var sb = new StringBuilder(format.Length + 16);
        int at = 0;
        do
        {
            int before = at;
            Emit(sb, format, stream, ref at, stopWhenExhausted: true);
            if (at == before)
            {
                break; // the format consumes nothing — one pass is the whole answer
            }
        }
        while (at < stream.Count);

        return sb.ToString();
    }

    /// <summary>One pass over the format, consuming values from <paramref name="argIndex"/> on.</summary>
    private static void Emit(StringBuilder sb, string format, IReadOnlyList<JgsValue> args, ref int argIndex,
        bool stopWhenExhausted)
    {
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

            // A '*' takes the width from the argument list rather than the format, which is what lets
            // a script compute a column width instead of hard-coding one (M63).
            int width = 0;
            if (i < format.Length && format[i] == '*')
            {
                width = TakeStarValue(format, args, ref argIndex, i);
                if (width < 0)
                {
                    leftAlign = true;
                    width = -width;
                }

                i++;
            }
            else
            {
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    width = (width * 10) + (format[i] - '0');
                    i++;
                }
            }

            int precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                if (i < format.Length && format[i] == '*')
                {
                    precision = Math.Max(0, TakeStarValue(format, args, ref argIndex, i));
                    i++;
                }
                else
                {
                    precision = 0;
                    while (i < format.Length && char.IsAsciiDigit(format[i]))
                    {
                        precision = (precision * 10) + (format[i] - '0');
                        i++;
                    }
                }
            }

            if (i >= format.Length)
            {
                throw new FormatException($"sprintf format ends inside the specifier \"{format[specStart..]}\".");
            }

            char verb = format[i];
            if (verb is not ('d' or 'i' or 'f' or 'e' or 'g' or 's' or 'x' or 'o'
                or 'c' or 'u' or 'X' or 'E' or 'G'))
            {
                throw new FormatException(
                    $"sprintf does not support the specifier \"%{verb}\" (supported: %c %d %i %e %E %f %g %G %o %s %u %x %X %%).");
            }

            if (argIndex >= args.Count)
            {
                if (stopWhenExhausted)
                {
                    return; // MATLAB stops mid-format when the values run out
                }

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
    }

    /// <summary>
    /// Reads the value a <c>*</c> stands in for, from the argument list rather than the format. A
    /// negative width means left-aligned, which is C's rule and the reason this hands the sign back
    /// rather than dropping it.
    /// </summary>
    private static int TakeStarValue(string format, IReadOnlyList<JgsValue> args, ref int argIndex, int at)
    {
        if (argIndex >= args.Count)
        {
            throw new FormatException($"sprintf format needs a width for the '*' at position {at + 1}.");
        }

        JgsValue given = args[argIndex++];
        if (given.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new FormatException($"sprintf '*' needs a number for the width, but got a {given.TypeName}.");
        }

        return (int)given.AsNumber;
    }

    private static string FormatOne(char verb, int precision, JgsValue arg)
    {
        if (verb == 's')
        {
            return arg.Display();
        }

        // %c is a single character: from a number it is the code point, and from text it is the text
        // itself, which is how MATLAB lets sprintf('%c', 'abc') print all three.
        if (verb == 'c')
        {
            return arg.Type == JgsType.String ? arg.AsString
                : arg.Type is JgsType.Number or JgsType.Bool ? ((char)(int)arg.AsNumber).ToString()
                : arg.Display();
        }

        if (arg.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new FormatException($"sprintf \"%{verb}\" needs a number, but got a {arg.TypeName}.");
        }

        double value = arg.AsNumber;

        // Infinity and NaN are written the way MATLAB writes them, whichever numeric specifier asked
        // for them: .NET spells the first one "Infinity", and the integer specifiers would try to cast
        // it to a whole number and answer a large negative one.
        if (!double.IsFinite(value))
        {
            return double.IsNaN(value) ? "NaN" : value > 0 ? "Inf" : "-Inf";
        }

        return verb switch
        {
            'd' or 'i' => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture),

            // %u is unsigned, so a negative value is an error in C and simply the magnitude here —
            // MATLAB prints the number rather than a wrapped one, and so does this.
            'u' => ((ulong)Math.Abs(Math.Round(value, MidpointRounding.AwayFromZero))).ToString(CultureInfo.InvariantCulture),
            'f' => value.ToString("F" + (precision < 0 ? 6 : precision), CultureInfo.InvariantCulture),
            'e' => value.ToString("0." + new string('0', precision < 0 ? 6 : precision) + "e+00", CultureInfo.InvariantCulture),
            'E' => value.ToString("0." + new string('0', precision < 0 ? 6 : precision) + "E+00", CultureInfo.InvariantCulture),
            'g' => FormatGeneral(value, precision),
            'G' => FormatGeneral(value, precision).ToUpperInvariant(),
            'x' => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString("x", CultureInfo.InvariantCulture),
            'X' => ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString("X", CultureInfo.InvariantCulture),
            'o' => Convert.ToString((long)Math.Round(value, MidpointRounding.AwayFromZero), 8),
            _ => throw new FormatException($"sprintf does not support the specifier \"%{verb}\"."),
        };
    }

    private static string FormatGeneral(double value, int precision)
    {
        // %g: shortest of fixed/scientific at the given significant digits (default 6, like C).
        int digits = precision <= 0 ? 6 : precision;
        string text = value.ToString("G" + digits, CultureInfo.InvariantCulture);
        int at = text.IndexOf('E', StringComparison.Ordinal);
        if (at < 0)
        {
            return text;
        }

        // C — and MATLAB after it — writes at least two exponent digits, where .NET's "G" writes as
        // few as one. %g of 1.2345e-5 reads 1.2345e-05 everywhere else, so the zero is padding the
        // exponent to its minimum width, not a digit to be trimmed.
        string sign = text[at + 1] == '-' ? "-" : "+";
        string exponent = text[(at + 2)..].TrimStart('0');
        return text[..at] + "e" + sign + exponent.PadLeft(2, '0');
    }
}
