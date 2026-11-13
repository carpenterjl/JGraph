using System.Globalization;
using System.Runtime.CompilerServices;
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
        // Each value remembers the argument it came from (open item 184): R2025b's %s takes the run
        // of character codes left in one numeric argument, so fprintf('arg%d\t%s\n', 2, [2 3]) writes
        // both codes under the one %s, while sprintf('%s,%s;', 65, 66, 67) is 'A,B;C,'. An empty
        // argument is one value that writes nothing (open item 295), marked EmptyGroup.
        var stream = new List<JgsValue>();
        var groups = new List<int>();
        int group = 0;
        void Add(JgsValue value, int from)
        {
            stream.Add(value);
            groups.Add(from);
        }

        void AddEmpty() => Add(JgsValue.Str(string.Empty), EmptyGroup);

        foreach (JgsValue arg in args)
        {
            group++;

            // A char matrix feeds one piece of text, not one value per code point (M105) — and the
            // text is its storage order, so fprintf('%s', ['a  '; 'bcd']) prints 'ab c d' exactly as
            // MATLAB's does. Spreading it would have handed %s the numbers underneath.
            if (arg.IsCharMatrix)
            {
                string text = arg.CharMatrixText();
                if (text.Length == 0)
                {
                    AddEmpty();
                }
                else
                {
                    Add(JgsValue.Str(text), group);
                }
            }
            else if (arg.IsTime)
            {
                // A datetime or duration prints as its text, one piece per element (open item 134:
                // sprintf('%s', seconds(1.4142135624)) is '1.4142 sec' in R2025b).
                int count = JgsBuiltins.IsCalendarDuration(arg) ? arg.AsStructArray.Length : arg.ArrayLength;
                if (count == 0)
                {
                    AddEmpty();
                }

                for (int i = 0; i < count; i++)
                {
                    Add(JgsValue.Str(JgsBuiltins.TimeText(arg, i)), group++);
                }
            }
            else if (arg.Type == JgsType.Array)
            {
                JgsValue[] elements = arg.BoxedElements();
                if (elements.Length == 0)
                {
                    AddEmpty();
                }
                else if (arg.IsStringArray)
                {
                    foreach (JgsValue element in elements)
                    {
                        Add(element, group++); // each string is its own piece of text
                    }
                }
                else
                {
                    foreach (JgsValue element in elements)
                    {
                        Add(element, group);
                    }
                }
            }
            else if (arg.Type == JgsType.String && arg.AsString.Length == 0)
            {
                AddEmpty(); // '' takes a conversion and writes nothing, %d's included (measured)
            }
            else
            {
                Add(arg, group);
            }
        }

        var sb = new StringBuilder(format.Length + 16);
        int at = 0;
        do
        {
            int before = at;
            Emit(sb, format, stream, ref at, stopWhenExhausted: true, matlabRules: true, groups);
            if (at == before)
            {
                break; // the format consumes nothing — one pass is the whole answer
            }
        }
        while (at < stream.Count);

        return sb.ToString();
    }

    /// <summary>The group of a value that stands for an empty argument: one conversion's worth of nothing.</summary>
    private const int EmptyGroup = -1;

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
    /// <paramref name="groups"/>, on MATLAB's road only, runs beside the list and says which argument
    /// each value came from, or <see cref="EmptyGroup"/> for an empty argument.
    /// </summary>
    private static void Emit(StringBuilder sb, string format, List<JgsValue> args, ref int argIndex,
        bool stopWhenExhausted, bool matlabRules, List<int>? groups = null)
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
                if (TakeStarValue(format, args, groups, ref argIndex, i, stopWhenExhausted) is not { } star)
                {
                    return; // MATLAB stops where a '*' finds no value, as a conversion would
                }

                width = star;
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
                    if (TakeStarValue(format, args, groups, ref argIndex, i, stopWhenExhausted) is not { } star)
                    {
                        return;
                    }

                    precision = Math.Max(0, star);
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

            var spec = new Spec(leftAlign, zeroPad, forceSign, spaceSign, alternate, width, precision);
            int next = position >= 0 ? position : argIndex;
            if (groups is not null && next < args.Count && groups[next] == EmptyGroup)
            {
                if (position >= 0)
                {
                    return; // R2025b writes nothing at all for sprintf('%1$d|', [])
                }

                // An empty argument fills one conversion with nothing, padded to its width, whatever
                // the conversion (open item 295: sprintf('%.17g ', []) is ' ', '%05d|' is '00000|').
                argIndex++;
                sb.Append(PadText(string.Empty, spec, spec.ZeroPad ? '0' : ' '));
                continue;
            }

            // Text met by a numeric conversion becomes its character codes, one value each; so does
            // text met by %c, which takes one character (sprintf('%c|', 'abc') is 'a|b|c|', measured).
            if (position < 0 && matlabRules && argIndex < args.Count && args[argIndex].Type == JgsType.String
                && (verb is not ('s' or 'c') || (verb == 'c' && args[argIndex].AsString.Length > 1)))
            {
                string text = args[argIndex].AsString;
                int from = groups?[argIndex] ?? 0;
                args.RemoveAt(argIndex);
                groups?.RemoveAt(argIndex);
                for (int k = text.Length - 1; k >= 0; k--)
                {
                    args.Insert(argIndex, JgsValue.Number(text[k]));
                    groups?.Insert(argIndex, from);
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

            // %s over numbers takes the whole run of character codes left in the argument at hand
            // (open item 184): '%s|' of [65 66 67.5 68] is 'AB|6.750000e+01|D|'. A value that is not a
            // code is left to Render, which writes it as %e.
            if (groups is not null && position < 0 && verb == 's' && CodeOf(args[argIndex]) is not null)
            {
                int run = groups[argIndex];
                var text = new StringBuilder();
                while (argIndex < args.Count && groups[argIndex] == run && CodeOf(args[argIndex]) is { } code)
                {
                    text.Append(code);
                    argIndex++;
                }

                string body = text.ToString();
                if (spec.Precision >= 0 && body.Length > spec.Precision)
                {
                    body = body[..spec.Precision];
                }

                sb.Append(PadText(body, spec, spec.ZeroPad ? '0' : ' '));
                continue;
            }

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
    /// <remarks>
    /// On MATLAB's road a '*' with no value left stops the pass (null), and one that meets an empty
    /// argument takes it as no width at all (measured: sprintf('%*d|', 5, []) is five blanks and a bar).
    /// </remarks>
    private static int? TakeStarValue(string format, List<JgsValue> args, List<int>? groups, ref int argIndex, int at,
        bool stopWhenExhausted)
    {
        if (argIndex >= args.Count)
        {
            if (stopWhenExhausted)
            {
                return null;
            }

            throw new FormatException($"sprintf format needs a width for the '*' at position {at + 1}.");
        }

        if (groups is not null && groups[argIndex] == EmptyGroup)
        {
            argIndex++;
            return 0;
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
                // MATLAB reads a number under %s or %c as a character code, and one that is not a
                // code — a fraction, a negative, NaN, past 65535 — as if %e had been asked for, flags
                // and width included (measured for %c too, open item 184: '%+c|' of 2.5 is '+2.500000e+00|').
                double code = arg.AsNumber;
                if (matlabRules && !IsCharacterCode(code))
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

        return RenderNumber(verb, spec, arg.AsNumber, matlabRules);
    }

    /// <summary>One number written the way a numeric specifier asks, width and all.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static string RenderNumber(char verb, in Spec spec, double value, bool matlabRules)
    {
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

    /// <summary>
    /// Whether <paramref name="value"/> is a whole number a MATLAB character can stand for: 0 to
    /// 65535, since a MATLAB char is one UTF-16 unit (measured: sprintf('%s', 65536) is
    /// '6.553600e+04', and 55296, a lone surrogate, is a character).
    /// </summary>
    private static bool IsCharacterCode(double value) =>
        value == Math.Floor(value) && value >= 0 && value <= 0xFFFF;

    /// <summary>The character a number, logical or complex value (by its real part) stands for under %s, or null when it is not a code.</summary>
    private static char? CodeOf(JgsValue value)
    {
        double number = value.Type switch
        {
            JgsType.Number or JgsType.Bool => value.AsNumber,
            JgsType.Complex => value.AsComplex.Real,
            _ => double.NaN,
        };

        return IsCharacterCode(number) ? (char)(int)number : null;
    }

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
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static string RenderFloat(char verb, in Spec spec, double value)
    {
        // The sign is taken off first so that the zero padding can slot in behind it, and so that a
        // negative zero keeps its minus — sprintf('%+.2f', -0) is '-0.00' in MATLAB, not '+0.00'.
        bool negative = double.IsNegative(value);
        double magnitude = Math.Abs(value);
        int places = spec.Precision < 0 ? 6 : spec.Precision;

        string digits = verb switch
        {
            'f' => magnitude.ToString(places < FixedPictures.Length ? FixedPictures[places] : "F" + places, CultureInfo.InvariantCulture),
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
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

    /// <summary>
    /// One number as <c>num2str(x)</c> and <c>string(x)</c> write it (ADR 0156): <c>%g</c> at the
    /// precision num2str.m picks for a lone element — as many digits as a whole number has, or four
    /// past a fraction's leading digit (five at least, sixteen at most) — with a negative zero
    /// written as <c>0</c> and the infinities spelt. The same text the aligned-row road wrote for a
    /// one-element row, written once as a function of the double rather than through a row list, a
    /// column width and a format string parsed per number.
    /// </summary>
    /// <remarks>
    /// NaN is spelt <c>NaN</c> here, as num2str spells it; <c>string(NaN)</c> is the missing string,
    /// and that decision belongs to the caller, which asks before it gets here. A whole number of
    /// fewer than sixteen digits whose digit count fits the precision is written from its integer,
    /// which is what <c>%g</c> writes for an exact integer it has room to write in full; everything
    /// else, and any whole number where the logarithm's floor undercounts its digits, goes through
    /// <see cref="FormatGeneral"/> exactly as the format string did.
    /// </remarks>
    internal static string FormatScalarGeneral(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        if (value == 0)
        {
            return "0"; // num2str drops the sign of a zero before it writes a word for it
        }

        double magnitude = Math.Abs(value);
        int exponent = (int)Math.Floor(Math.Log10(magnitude));
        bool whole = value == Math.Floor(value);
        int precision = whole
            ? Math.Min(16, Math.Max(1, exponent + 1))
            : Math.Min(16, Math.Max(5, exponent + 5));

        if (whole && magnitude < 1e15)
        {
            long integer = (long)magnitude;
            if (DecimalDigits(integer) <= precision)
            {
                string written = integer.ToString(CultureInfo.InvariantCulture);
                return value < 0 ? "-" + written : written;
            }
        }

        string digits = FormatGeneral(magnitude, precision);
        return value < 0 ? "-" + digits : digits;
    }

    /// <summary>
    /// The one precision <c>string(x)</c> writes every element of a real array at (open item 3,
    /// measured in R2025b): <c>ceil(log10(m)) + 4</c> significant digits for the largest finite
    /// magnitude <c>m</c> in the array, five at least and sixteen at most. So
    /// <c>string([9016.99437494 12345.5])</c> is <c>"9016.99437" "12345.5"</c>, where each element
    /// alone would be written to its own precision.
    /// </summary>
    internal static int ArrayPrecision(ReadOnlySpan<double> values)
    {
        double largest = 0;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                largest = Math.Max(largest, Math.Abs(value));
            }
        }

        if (largest == 0)
        {
            return 5;
        }

        // ceil(log10(m)), exact at the powers of ten, where the floating logarithm may land either side.
        int exponent = (int)Math.Floor(Math.Log10(largest));
        if (Math.Pow(10, exponent) > largest)
        {
            exponent--;
        }
        else if (Math.Pow(10, exponent + 1) <= largest)
        {
            exponent++;
        }

        int ceiling = Math.Pow(10, exponent) == largest ? exponent : exponent + 1;
        return Math.Clamp(ceiling + 4, 5, 16);
    }

    /// <summary>One element of a real array at the array's precision: <see cref="FormatScalarGeneral"/>'s spelling, at a precision it is given.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static string FormatAtPrecision(double value, int precision)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        if (value == 0)
        {
            return "0";
        }

        double magnitude = Math.Abs(value);
        if (value == Math.Floor(value) && magnitude < 1e15)
        {
            long integer = (long)magnitude;
            if (DecimalDigits(integer) <= precision)
            {
                string written = integer.ToString(CultureInfo.InvariantCulture);
                return value < 0 ? "-" + written : written;
            }
        }

        string digits = FormatGeneral(magnitude, precision);
        return value < 0 ? "-" + digits : digits;
    }

    /// <summary>How many decimal digits a positive integer below 10^15 has.</summary>
    private static int DecimalDigits(long integer)
    {
        int count = 1;
        for (long bound = 10; integer >= bound && count < 19; bound *= 10)
        {
            count++;
        }

        return count;
    }

    /// <summary>The "F0" to "F31" pictures <see cref="RenderFloat"/> asks .NET for, made once (I7, ADR 0326).</summary>
    private static readonly string[] FixedPictures = MakeFixedPictures();

    private static string[] MakeFixedPictures()
    {
        var pictures = new string[32];
        for (int places = 0; places < pictures.Length; places++)
        {
            pictures[places] = "F" + places.ToString(CultureInfo.InvariantCulture);
        }

        return pictures;
    }

    /// <summary>
    /// A format with exactly one numeric conversion and nothing that reads the argument list but
    /// that conversion — no <c>*</c>, no <c>n$</c> — parsed once (I7, ADR 0326): the literal text
    /// either side of it with <c>%%</c> already folded, the conversion and its flags. Applied to one
    /// number, it writes what <see cref="FormatMatlab"/> writes for that format and that number.
    /// </summary>
    internal sealed class SingleNumeric
    {
        private readonly string _prefix;
        private readonly string _suffix;
        private readonly char _verb;
        private readonly Spec _spec;

        private SingleNumeric(string prefix, string suffix, char verb, Spec spec)
        {
            _prefix = prefix;
            _suffix = suffix;
            _verb = verb;
            _spec = spec;
        }

        /// <summary>The format applied to <paramref name="value"/> under MATLAB's rules.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public string Format(double value) =>
            string.Concat(_prefix, RenderNumber(_verb, _spec, value, matlabRules: true), _suffix);

        /// <summary>
        /// The parsed format, or null for anything else: no conversion or more than one, a text
        /// conversion, a star, a position, a repeated flag, a format ending inside a specifier.
        /// Those keep <see cref="FormatMatlab"/>'s own reading and its own errors.
        /// </summary>
        public static SingleNumeric? TryParse(string format)
        {
            var prefix = new StringBuilder();
            var suffix = new StringBuilder();
            StringBuilder literal = prefix;
            bool found = false;
            char verb = '\0';
            Spec spec = default;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c != '%')
                {
                    literal.Append(c);
                    continue;
                }

                if (i + 1 < format.Length && format[i + 1] == '%')
                {
                    literal.Append('%');
                    i++;
                    continue;
                }

                if (found)
                {
                    return null;
                }

                i++;
                int digitsAt = i;
                while (digitsAt < format.Length && char.IsAsciiDigit(format[digitsAt]))
                {
                    digitsAt++;
                }

                if (digitsAt > i && digitsAt < format.Length && format[digitsAt] == '$')
                {
                    return null;
                }

                bool leftAlign = false, zeroPad = false, forceSign = false, spaceSign = false, alternate = false;
                while (i < format.Length)
                {
                    char flag = format[i];
                    ref bool slot = ref leftAlign;
                    if (flag == '-')
                    {
                        slot = ref leftAlign;
                    }
                    else if (flag == '0')
                    {
                        slot = ref zeroPad;
                    }
                    else if (flag == '+')
                    {
                        slot = ref forceSign;
                    }
                    else if (flag == ' ')
                    {
                        slot = ref spaceSign;
                    }
                    else if (flag == '#')
                    {
                        slot = ref alternate;
                    }
                    else
                    {
                        break;
                    }

                    if (slot)
                    {
                        return null;
                    }

                    slot = true;
                    i++;
                }

                if (i < format.Length && format[i] == '*')
                {
                    return null;
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
                    if (i < format.Length && format[i] == '*')
                    {
                        return null;
                    }

                    precision = 0;
                    while (i < format.Length && char.IsAsciiDigit(format[i]))
                    {
                        precision = (precision * 10) + (format[i] - '0');
                        i++;
                    }
                }

                while (i < format.Length && format[i] is 'l' or 'h' or 'L')
                {
                    i++;
                }

                if (i >= format.Length
                    || format[i] is not ('d' or 'i' or 'f' or 'e' or 'g' or 'x' or 'o' or 'u' or 'X' or 'E' or 'G'))
                {
                    return null;
                }

                verb = format[i];
                spec = new Spec(leftAlign, zeroPad, forceSign, spaceSign, alternate, width, precision);
                found = true;
                literal = suffix;
            }

            return found ? new SingleNumeric(prefix.ToString(), suffix.ToString(), verb, spec) : null;
        }
    }

    /// <summary>The "G1" to "G17" pictures <see cref="FormatGeneral"/> asks .NET for, made once.</summary>
    private static readonly string[] GeneralPictures = MakeGeneralPictures();

    private static string[] MakeGeneralPictures()
    {
        var pictures = new string[18];
        for (int digits = 1; digits < pictures.Length; digits++)
        {
            pictures[digits] = "G" + digits.ToString(CultureInfo.InvariantCulture);
        }

        return pictures;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static string FormatGeneral(double value, int precision)
    {
        // %g: shortest of fixed/scientific at the given significant digits (default 6, like C, and
        // a precision of zero asks for one digit rather than none — %.0g of 1.5 is 2).
        int digits = precision < 0 ? 6 : Math.Max(1, precision);
        string picture = digits < GeneralPictures.Length ? GeneralPictures[digits] : "G" + digits;
        string text = value.ToString(picture, CultureInfo.InvariantCulture);
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
