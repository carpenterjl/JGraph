using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The backslash escapes MATLAB's formatting functions decode in a format string, and what happens to
/// the ones they do not recognise.
/// </summary>
/// <remarks>
/// <para>
/// MATLAB's quotes decode nothing — <c>'a\n'</c> is three characters, an <c>a</c>, a backslash and an
/// <c>n</c> — so the decoding belongs to <c>sprintf</c> and its family rather than to the lexer. That
/// makes an unrecognised escape a formatting fault and not a syntax error, and MATLAB treats it as
/// one: it warns, keeps everything the format produced up to that point, and abandons the rest of the
/// format string. See ADR 0148.
/// </para>
/// <para>
/// The accepted set is C's, minus <c>\%</c> — which MATLAB rejects, <c>%%</c> being how a per cent
/// sign is written — and with <c>\"</c> and <c>\'</c> kept even though MATLAB's quotes never needed
/// them. Both numeric forms take as many digits as follow rather than C's two and three, so
/// <c>'\x41B'</c> is one character (U+041B) and not <c>A</c> followed by <c>B</c>, and both cap at
/// <c>0xFFFF</c>.
/// </para>
/// </remarks>
internal static class JgsFormatEscapes
{
    /// <summary>The highest character code either numeric escape may name — measured, not assumed.</summary>
    private const int HighestCode = 0xFFFF;

    /// <summary>
    /// Why a format string stopped early: MATLAB's own identifier and its own message, both measured
    /// from R2025b, because a script may well be branching on the first and reading the second.
    /// </summary>
    internal readonly record struct Fault(string Identifier, string Message);

    /// <summary>
    /// The escapes in <paramref name="format"/> decoded. An escape MATLAB does not recognise ends the
    /// format there: the text decoded so far is the answer and <paramref name="fault"/> says what was
    /// wrong with it. A format with no fault decodes whole and leaves <paramref name="fault"/> null.
    /// </summary>
    public static string Decode(string format, out Fault? fault)
    {
        fault = null;
        if (!format.Contains('\\', StringComparison.Ordinal))
        {
            return format;
        }

        var sb = new StringBuilder(format.Length);
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '\\')
            {
                sb.Append(format[i]);
                continue;
            }

            if (i + 1 >= format.Length)
            {
                // MATLAB spells this one out separately, spacing and all, rather than reporting the
                // character after the backslash — there is not one.
                fault = new Fault(
                    "MATLAB:printf:NoControlCharacterInFormat",
                    "A lone trailing backslash, '\\' , is not a valid control character. "
                    + "See 'doc sprintf' for control characters valid in the format string.");
                break;
            }

            char next = format[i + 1];
            switch (next)
            {
                case 'n': sb.Append('\n'); i++; continue;
                case 't': sb.Append('\t'); i++; continue;
                case 'r': sb.Append('\r'); i++; continue;
                case 'a': sb.Append('\a'); i++; continue;
                case 'b': sb.Append('\b'); i++; continue;
                case 'f': sb.Append('\f'); i++; continue;
                case 'v': sb.Append('\v'); i++; continue;
                case '\\': sb.Append('\\'); i++; continue;
                case '"': sb.Append('"'); i++; continue;
                case '\'': sb.Append('\''); i++; continue;

                case 'x':
                {
                    int at = i + 2;
                    if (at >= format.Length || !char.IsAsciiHexDigit(format[at]))
                    {
                        fault = new Fault(
                            "MATLAB:printf:HexCharCodeInvalid",
                            "Valid hexadecimal digits are 0-9 and A-F.");
                        break;
                    }

                    long value = 0;
                    while (at < format.Length && char.IsAsciiHexDigit(format[at]))
                    {
                        value = Saturated(value, 16, HexDigit(format[at]));
                        at++;
                    }

                    if (value > HighestCode)
                    {
                        fault = new Fault(
                            "MATLAB:printf:HexCharCodeOutOfRange",
                            "The hex value specified is outside the range of the character set.");
                        break;
                    }

                    sb.Append((char)value);
                    i = at - 1;
                    continue;
                }

                case >= '0' and <= '7':
                {
                    int at = i + 1;
                    long value = 0;
                    while (at < format.Length && format[at] is >= '0' and <= '7')
                    {
                        value = Saturated(value, 8, format[at] - '0');
                        at++;
                    }

                    if (value > HighestCode)
                    {
                        fault = new Fault(
                            "MATLAB:printf:OctalCharCodeOutOfRange",
                            "The octal value specified is outside the range of the character set.");
                        break;
                    }

                    sb.Append((char)value);
                    i = at - 1;
                    continue;
                }

                case '8' or '9':
                    // A backslash and a digit is an octal escape as far as MATLAB is concerned, so an
                    // 8 or a 9 in the first position is a bad octal digit rather than a bad escape.
                    // Later in the run it is not: '\08' is a NUL and a literal '8'.
                    fault = new Fault(
                        "MATLAB:printf:OctalCharCodeInvalid",
                        "Valid octal digits are 0-7.");
                    break;

                default:
                    fault = new Fault(
                        "MATLAB:printf:BadEscapeSequenceInFormat",
                        $"Escaped character '\\{next}' is not valid. "
                        + "See 'doc sprintf' for supported special characters.");
                    break;
            }

            break; // every path that reaches here set a fault, and a fault ends the format
        }

        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="value"/> with one more digit on the end, stopped once it is past the highest
    /// code either escape may name. A format may write as many digits as it likes — MATLAB reads all
    /// of them and then complains about the value — so the accumulation has to survive
    /// <c>'\x1FFFFFFFFFFFFFFFF'</c> without wrapping round into a code that looks legal.
    /// </summary>
    private static long Saturated(long value, int radix, int digit) =>
        value > HighestCode ? value : (value * radix) + digit;

    private static int HexDigit(char c) =>
        c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a') + 10;
}
