using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The formatter behind the JGS and MATLAB <c>sprintf</c>/<c>fprintf</c> builtins: a fixed C/MATLAB
/// subset — <c>%d %i %f %e %g %s %x %%</c> and friends — each taking the printf flags
/// (<c>-</c> left align, <c>+</c> always sign, a space where the sign would go, <c>0</c> zero pad,
/// <c>#</c> alternate form) in any order, then an optional width (<c>%8d</c>) and precision
/// (<c>%.2f</c>, <c>%.3g</c>), either of which a <c>*</c> may take from the argument list.
/// Invariant culture throughout. Anything else is a runtime error rather than a silent
/// pass-through, so typos surface immediately.
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
        Emit(sb, format, args, ref argIndex, stopWhenExhausted: false, matlabRules: false);

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
            Emit(sb, format, stream, ref at, stopWhenExhausted: true, matlabRules: true);
            if (at == before)
            {
                break; // the format consumes nothing — one pass is the whole answer
            }
        }
        while (at < stream.Count);

        return sb.ToString();
    }

    /// <summary>
    /// Everything one specifier says about the value it prints, gathered between the <c>%</c> and the
    /// conversion character. A width or precision of <c>-1</c> means the format did not give one.
    /// </summary>
    private readonly record struct Spec(
        bool LeftAlign,
        bool ZeroPad,
        bool ForceSign,
        bool SpaceSign,
        bool Alternate,
        int Width,
        int Precision);

    /// <summary>One pass over the format, consuming values from <paramref name="argIndex"/> on.</summary>
    private static void Emit(StringBuilder sb, string format, IReadOnlyList<JgsValue> args, ref int argIndex,
        bool stopWhenExhausted, bool matlabRules)
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

            // The flags come first and in any order, so '%-+d' and '%+-d' are the same specifier.
            // MATLAB allows each of them once: it silently abandons the rest of the format on a
            // repeat, and this says so instead, which is the habit of every other misuse here.
            bool leftAlign = false;
            bool zeroPad = false;
            bool forceSign = false;
            bool spaceSign = false;
            bool alternate = false;
            while (i < format.Length)
            {
                char flag = format[i];
                bool already;
                if (flag == '-')
                {
                    already = leftAlign;
                    leftAlign = true;
                }
                else if (flag == '0')
                {
                    already = zeroPad;
                    zeroPad = true;
                }
                else if (flag == '+')
                {
                    already = forceSign;
                    forceSign = true;
                }
                else if (flag == ' ')
                {
                    already = spaceSign;
                    spaceSign = true;
                }
                else if (flag == '#')
                {
                    already = alternate;
                    alternate = true;
                }
                else
                {
                    break;
                }

                if (already)
                {
                    throw new FormatException(
                        $"sprintf repeats the '{flag}' flag in the specifier \"{format[specStart..(i + 1)]}\".");
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
                    $"sprintf does not support the specifier \"{format[specStart..(i + 1)]}\" (supported: "
                    + "%c %d %i %e %E %f %g %G %o %s %u %x %X %%, each taking the flags - + 0 # and space, "
                    + "then a width and a .precision).");
            }

            if (argIndex >= args.Count)
            {
                if (stopWhenExhausted)
                {
                    return; // MATLAB stops mid-format when the values run out
                }

                throw new FormatException($"sprintf format needs more arguments: nothing left for \"{format[specStart..(i + 1)]}\".");
            }

            var spec = new Spec(leftAlign, zeroPad, forceSign, spaceSign, alternate, width, precision);
            sb.Append(Render(verb, spec, args[argIndex++], matlabRules));
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

    /// <summary>One value written the way its specifier asks, width and all.</summary>
    private static string Render(char verb, in Spec spec, JgsValue arg, bool matlabRules)
    {
        // %c is a single character: from a number it is the code point, and from text it is the text
        // itself, which is how MATLAB lets sprintf('%c', 'abc') print all three. Text takes no sign,
        // but it does take the zero flag — MATLAB pads '%06s' with zeros, on whichever side it aligns.
        if (verb is 's' or 'c')
        {
            string body = verb == 's' ? arg.Display()
                : arg.Type == JgsType.String ? arg.AsString
                : arg.Type is JgsType.Number or JgsType.Bool ? ((char)(int)arg.AsNumber).ToString()
                : arg.Display();

            return PadText(body, spec, spec.ZeroPad ? '0' : ' ');
        }

        if (arg.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new FormatException($"sprintf \"%{verb}\" needs a number, but got a {arg.TypeName}.");
        }

        double value = arg.AsNumber;

        // Infinity and NaN are written the way MATLAB writes them, whichever numeric specifier asked
        // for them: .NET spells the first one "Infinity", and the integer specifiers would try to cast
        // it to a whole number and answer a large negative one. A sign flag reaches the infinities but
        // never NaN, and the zero flag reaches neither — a leading zero would read as a digit.
        if (!double.IsFinite(value))
        {
            return double.IsNaN(value)
                ? PadNumber(string.Empty, string.Empty, "NaN", spec, zeroFill: false)
                : PadNumber(SignOf(value < 0, spec), string.Empty, "Inf", spec, zeroFill: false);
        }

        bool unsigned = verb is 'u' or 'o' or 'x' or 'X';
        if (unsigned || verb is 'd' or 'i')
        {
            // MATLAB's own rule, and the one thing about its printf that surprises everybody: a value
            // the named conversion cannot hold is written as %e instead, keeping every flag, the width
            // and the precision it was given. So sprintf('%+d', 2.5) is '+2.500000e+00', and '%+u' of
            // the same value gains the sign that %u itself would have ignored. JGS keeps its older
            // reading, where a fractional value simply rounds.
            if (matlabRules && !FitsWholeNumber(value, unsigned))
            {
                return RenderFloat('e', spec, value);
            }

            return RenderInteger(verb, spec, value, unsigned);
        }

        return RenderFloat(verb, spec, value);
    }

    /// <summary>Whether MATLAB's integer conversions can hold <paramref name="value"/> as written.</summary>
    private static bool FitsWholeNumber(double value, bool unsigned) =>
        value == Math.Floor(value)
        && (unsigned
            ? value >= 0 && value < 18446744073709551616.0
            : value >= -9223372036854775808.0 && value < 9223372036854775808.0);

    /// <summary>The sign a signed conversion writes: a minus always, else a plus or a space if asked.</summary>
    private static string SignOf(bool negative, in Spec spec) =>
        negative ? "-"
        : spec.ForceSign ? "+"
        : spec.SpaceSign ? " "
        : string.Empty;

    /// <summary><c>%d %i %u %o %x %X</c>: the digits, a precision that is a minimum digit count, and <c>#</c>'s prefix.</summary>
    private static string RenderInteger(char verb, in Spec spec, double value, bool unsigned)
    {
        bool negative = false;
        ulong magnitude;
        if (unsigned)
        {
            // Outside MATLAB's rules a negative can still arrive here: %u answers its magnitude and the
            // base conversions answer its bits, which is what C writes and what JGS has always written.
            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            magnitude = rounded >= 0 ? (ulong)rounded
                : verb == 'u' ? (ulong)(-rounded)
                : unchecked((ulong)(long)rounded);
        }
        else
        {
            long whole = (long)Math.Round(value, MidpointRounding.AwayFromZero);
            negative = whole < 0;
            magnitude = negative ? unchecked((ulong)(-(whole + 1))) + 1UL : (ulong)whole;
        }

        string digits = verb switch
        {
            'x' => magnitude.ToString("x", CultureInfo.InvariantCulture),
            'X' => magnitude.ToString("X", CultureInfo.InvariantCulture),
            'o' => Convert.ToString(unchecked((long)magnitude), 8),
            _ => magnitude.ToString(CultureInfo.InvariantCulture),
        };

        // A precision on an integer is a minimum number of digits, not a number of decimals — and a
        // zero written to no places at all is nothing, which is how C reads '%.0d' of 0.
        if (spec.Precision == 0 && magnitude == 0)
        {
            digits = string.Empty;
        }
        else if (spec.Precision > digits.Length)
        {
            digits = digits.PadLeft(spec.Precision, '0');
        }

        string prefix = string.Empty;
        if (spec.Alternate)
        {
            if (verb == 'o' && !digits.StartsWith('0'))
            {
                digits = "0" + digits;
            }
            else if (verb is 'x' or 'X' && magnitude != 0)
            {
                prefix = verb == 'x' ? "0x" : "0X";
            }
        }

        // The unsigned conversions take no sign at all, so '%+x' is '%x'. And a precision turns the
        // zero flag off, because the precision has already said how many digits there are to be.
        return PadNumber(
            unsigned ? string.Empty : SignOf(negative, spec),
            prefix,
            digits,
            spec,
            zeroFill: spec.ZeroPad && !spec.LeftAlign && spec.Precision < 0);
    }

    /// <summary><c>%f %e %E %g %G</c>: the magnitude written to the asked-for places, then its sign.</summary>
    private static string RenderFloat(char verb, in Spec spec, double value)
    {
        // The sign is taken off first so that the zero padding can slot in behind it, and so that a
        // negative zero keeps its minus — sprintf('%+.2f', -0) is '-0.00' in MATLAB, not '+0.00'.
        bool negative = double.IsNegative(value);
        double magnitude = Math.Abs(value);
        int places = spec.Precision < 0 ? 6 : spec.Precision;

        string digits = verb switch
        {
            'f' => magnitude.ToString("F" + places, CultureInfo.InvariantCulture),
            'e' => Scientific(magnitude, places),
            'E' => Scientific(magnitude, places).ToUpperInvariant(),
            'g' => spec.Alternate ? GeneralAlternate(magnitude, spec.Precision) : FormatGeneral(magnitude, spec.Precision),
            _ => (spec.Alternate ? GeneralAlternate(magnitude, spec.Precision) : FormatGeneral(magnitude, spec.Precision))
                .ToUpperInvariant(),
        };

        // '#' on a fixed or scientific conversion asks only that the point be written even where no
        // decimals follow it, so '%#.0f' of 1 is '1.' and '%#.0e' of 1 is '1.e+00'. On %g it asks for
        // more, and GeneralAlternate has already done it.
        if (spec.Alternate && verb is 'f' or 'e' or 'E')
        {
            digits = ForcePoint(digits);
        }

        return PadNumber(
            SignOf(negative, spec),
            string.Empty,
            digits,
            spec,
            zeroFill: spec.ZeroPad && !spec.LeftAlign);
    }

    /// <summary>Text and characters: padded on the aligned side with whatever <paramref name="pad"/> says.</summary>
    private static string PadText(string body, in Spec spec, char pad) =>
        body.Length >= spec.Width ? body
        : spec.LeftAlign ? body.PadRight(spec.Width, pad)
        : body.PadLeft(spec.Width, pad);

    /// <summary>
    /// A number assembled in the order C writes it: the sign, then any <c>0x</c> prefix, then the
    /// digits. Zero padding goes between the prefix and the digits so that '%#08x' of 255 is
    /// '0x0000ff' and '%05d' of -42 is '-0042'; left-aligning turns it back into spaces.
    /// </summary>
    private static string PadNumber(string sign, string prefix, string digits, in Spec spec, bool zeroFill)
    {
        int have = sign.Length + prefix.Length + digits.Length;
        if (have >= spec.Width)
        {
            return sign + prefix + digits;
        }

        if (spec.LeftAlign)
        {
            return (sign + prefix + digits).PadRight(spec.Width);
        }

        return zeroFill
            ? sign + prefix + new string('0', spec.Width - have) + digits
            : (sign + prefix + digits).PadLeft(spec.Width);
    }

    /// <summary>Writes a decimal point where a conversion left none, before the exponent if there is one.</summary>
    private static string ForcePoint(string text)
    {
        if (text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        int at = text.IndexOfAny(['e', 'E']);
        return at < 0 ? text + "." : text[..at] + "." + text[at..];
    }

    /// <summary><c>%e</c>: one digit, the decimals asked for, and an exponent of at least two digits.</summary>
    private static string Scientific(double magnitude, int places) =>
        magnitude.ToString("0." + new string('0', places) + "e+00", CultureInfo.InvariantCulture);

    private static string FormatGeneral(double value, int precision)
    {
        // %g: shortest of fixed/scientific at the given significant digits (default 6, like C, and
        // a precision of zero asks for one digit rather than none — %.0g of 1.5 is 2).
        int digits = precision < 0 ? 6 : Math.Max(1, precision);
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

    /// <summary>
    /// <c>%#g</c>: C's own choice between fixed and scientific — fixed while the exponent sits in
    /// <c>[-4, significant digits)</c> — but with the trailing zeros kept and the point always
    /// written, which is the whole of what '#' asks of a general conversion.
    /// </summary>
    private static string GeneralAlternate(double magnitude, int precision)
    {
        int significant = precision < 0 ? 6 : Math.Max(1, precision);
        string scientific = magnitude.ToString("E" + (significant - 1), CultureInfo.InvariantCulture);
        int exponent = magnitude == 0
            ? 0
            : int.Parse(
                scientific[(scientific.IndexOf('E', StringComparison.Ordinal) + 1)..],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);

        string text = exponent >= -4 && exponent < significant
            ? magnitude.ToString("F" + (significant - 1 - exponent), CultureInfo.InvariantCulture)
            : Scientific(magnitude, significant - 1);

        return ForcePoint(text);
    }
}
