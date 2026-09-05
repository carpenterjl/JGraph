using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The formatter behind the JGS and MATLAB <c>sprintf</c>/<c>fprintf</c> builtins: a fixed C/MATLAB
/// subset — <c>%d %i %f %e %g %s %x %%</c> and friends — each taking the printf flags
/// (<c>-</c> left align, <c>+</c> always sign, a space where the sign would go, <c>0</c> zero pad,
/// <c>#</c> alternate form) in any order, then an optional width (<c>%8d</c>) and precision
/// (<c>%.2f</c>, <c>%.3g</c>), either of which a <c>*</c> may take from the argument list. A
/// specifier may also name its argument by position (<c>%2$d</c>), and carries C's length
/// modifiers (<c>%ld</c>) without effect. Invariant culture throughout. Anything else is a runtime
/// error rather than a silent pass-through, so typos surface immediately.
/// </summary>
internal static class JgsSprintf
{
    /// <summary>Formats <paramref name="format"/> with <paramref name="args"/>; throws <see cref="FormatException"/> with a user-facing message on any misuse.</summary>
    public static string Format(string format, IReadOnlyList<JgsValue> args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int argIndex = 0;
        var list = new List<JgsValue>(args);
        Emit(sb, format, list, ref argIndex, stopWhenExhausted: false, matlabRules: false);

        if (argIndex < list.Count)
        {
            throw new FormatException($"sprintf got {list.Count - argIndex} more argument(s) than the format uses.");
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

    /// <summary>
    /// One pass over the format, consuming values from <paramref name="argIndex"/> on. The list is
    /// writable because MATLAB hands a numeric conversion the character codes of a text argument one
    /// at a time — <c>sprintf('%d', 'ab')</c> is <c>9798</c> — so a text value met by <c>%d</c> is
    /// spread into its codes in place and the rest of the format goes on over them.
    /// </summary>
    private static void Emit(StringBuilder sb, string format, List<JgsValue> args, ref int argIndex,
        bool stopWhenExhausted, bool matlabRules)
    {
        bool positional = false;
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

            // '%2$d' names its argument by position instead of taking the next one. The whole format
            // then has to say where every value comes from, so a positional pass never cycles.
            int position = -1;
            int digitsAt = i;
            int candidate = 0;
            while (digitsAt < format.Length && char.IsAsciiDigit(format[digitsAt]))
            {
                candidate = (candidate * 10) + (format[digitsAt] - '0');
                digitsAt++;
            }

            if (digitsAt > i && digitsAt < format.Length && format[digitsAt] == '$')
            {
                if (candidate < 1 || candidate > args.Count)
                {
                    throw new FormatException(
                        $"sprintf format asks for argument {candidate} in \"{format[specStart..(digitsAt + 1)]}\", but {args.Count} were given.");
                }

                position = candidate - 1;
                positional = true;
                i = digitsAt + 1;
            }

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

            // C's length modifiers: every value is a double here, so '%ld' is '%d'.
            while (i < format.Length && format[i] is 'l' or 'h' or 'L')
            {
                i++;
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

            if (position < 0 && matlabRules && verb is not ('s' or 'c') && argIndex < args.Count
                && args[argIndex].Type == JgsType.String)
            {
                // Text met by a numeric conversion becomes its character codes, one value each; an
                // empty text is no values at all.
                string text = args[argIndex].AsString;
                args.RemoveAt(argIndex);
                for (int k = text.Length - 1; k >= 0; k--)
                {
                    args.Insert(argIndex, JgsValue.Number(text[k]));
                }
            }

            if (position < 0 && argIndex >= args.Count)
            {
                if (stopWhenExhausted)
                {
                    return; // MATLAB stops mid-format when the values run out
                }

                throw new FormatException($"sprintf format needs more arguments: nothing left for \"{format[specStart..(i + 1)]}\".");
            }

            var spec = new Spec(leftAlign, zeroPad, forceSign, spaceSign, alternate, width, precision);
            JgsValue arg = position >= 0 ? args[position] : args[argIndex++];
            sb.Append(Render(verb, spec, arg, matlabRules));
        }

        if (positional)
        {
            argIndex = args.Count; // every value was reachable by number; none is left over or unread
        }
    }

    /// <summary>
    /// Reads the value a <c>*</c> stands in for, from the argument list rather than the format. A
    /// negative width means left-aligned, which is C's rule and the reason this hands the sign back
    /// rather than dropping it.
    /// </summary>
    private static int TakeStarValue(string format, List<JgsValue> args, ref int argIndex, int at)
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
        if (matlabRules && arg.Type == JgsType.Complex)
        {
            arg = JgsValue.Number(arg.AsComplex.Real); // MATLAB prints the real part and drops the rest
        }

        // %c is a single character: from a number it is the code point, and from text it is the text
        // itself, which is how MATLAB lets sprintf('%c', 'abc') print all three. Text takes no sign,
        // but it does take the zero flag — MATLAB pads '%06s' with zeros, on whichever side it aligns.
        if (verb is 's' or 'c')
        {
            string body;
            if (arg.Type == JgsType.String)
            {
                body = arg.AsString;
            }
            else if (arg.Type is JgsType.Number or JgsType.Bool)
            {
                // MATLAB reads a number under %s as a character code, and one that is not a code —
                // a fraction, a negative — as if %e had been asked for, flags and width included.
                double code = arg.AsNumber;
                if (verb == 's' && matlabRules && !IsCharacterCode(code))
                {
                    return Render('e', spec, arg, matlabRules); // Inf and NaN keep their spelling
                }

                body = verb == 's' && !matlabRules ? arg.Display() : CharacterOf(code);
            }
            else if (matlabRules)
            {
                throw new FormatException($"sprintf \"%{verb}\" needs text or a number, but got a {arg.TypeName}.");
            }
            else
            {
                body = arg.Display();
            }

            // A precision on %s is the most of the text to write, which is how '%.3s' clips a label.
            if (verb == 's' && spec.Precision >= 0 && body.Length > spec.Precision)
            {
                body = body[..spec.Precision];
            }

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

    /// <summary>Whether <paramref name="value"/> is a whole number a character can stand for.</summary>
    private static bool IsCharacterCode(double value) =>
        value == Math.Floor(value) && value >= 0 && value <= 0x10FFFF;

    /// <summary>The character with code <paramref name="value"/>, past the basic plane included.</summary>
    private static string CharacterOf(double value) =>
        value is >= 0 and <= 0x10FFFF and (< 0xD800 or > 0xDFFF) && value == Math.Floor(value)
            ? char.ConvertFromUtf32((int)value)
            : ((char)(int)value).ToString();

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
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (unsigned)
        {
            // Outside MATLAB's rules a negative can still arrive here: %u answers its magnitude and the
            // base conversions answer its bits, which is what C writes and what JGS has always written.
            magnitude = rounded >= 0 ? (ulong)rounded
                : verb == 'u' ? (ulong)(-rounded)
                : unchecked((ulong)(long)rounded);
        }
        else
        {
            // The magnitude is taken as a ulong so that -2^63 prints in digits like everything else;
            // a double beyond the range (under JGS's rules, which never fall back to %e) saturates.
            negative = rounded < 0;
            double size = Math.Abs(rounded);
            magnitude = size >= 18446744073709551616.0 ? ulong.MaxValue : (ulong)size;
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

    /// <summary>
    /// <c>%e</c>: one digit, the decimals asked for, and an exponent of at least two digits. .NET's
    /// "E" format writes the exact digits to any precision where a custom picture rounds at fifteen,
    /// so it is the picture's three-digit exponent that is trimmed here rather than the digits.
    /// </summary>
    private static string Scientific(double magnitude, int places)
    {
        string text = magnitude.ToString("E" + places, CultureInfo.InvariantCulture);
        int at = text.IndexOf('E', StringComparison.Ordinal);
        string exponent = text[(at + 2)..].TrimStart('0');
        return text[..at] + "e" + text[at + 1] + exponent.PadLeft(2, '0');
    }

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
