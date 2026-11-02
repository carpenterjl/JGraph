using System.Globalization;

namespace JGraph.Scripting.Editing;

/// <summary>
/// The Go To dialog's rules (R2025b's <c>rtc/plugins/goto/view/GotoDialog</c> and <c>GotoUtilities</c>): OK takes what JavaScript's
/// <c>Number()</c> reads as a whole number above zero - so "3.0", " 5 ", "5e0" and "0x5" pass and "3.5", "abc" and "" do not - and the line
/// chosen is held to the text's lines.
/// </summary>
public static class GoToLineRules
{
    /// <summary>The dialog's title.</summary>
    public const string Title = "Go To";

    /// <summary>The field's label.</summary>
    public const string LineLabel = "Line number:";

    /// <summary>What a value the dialog refuses says (<c>gotoDialog.lineNumberInvalid</c>).</summary>
    public const string InvalidMessage = "The value entered is not valid.";

    /// <summary>The line <paramref name="text"/> asks for, or null when the dialog refuses it (<c>_validateInput</c>).</summary>
    public static long? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        double? value = JavaScriptNumber(text);
        if (value is not { } number || number <= 0 || double.IsInfinity(number) || Math.Floor(number) != number || number > long.MaxValue)
        {
            return null;
        }

        return (long)number;
    }

    /// <summary>Whether the dialog's OK takes <paramref name="text"/>.</summary>
    public static bool IsValid(string text) => Parse(text) is not null;

    /// <summary>The line gone to among <paramref name="lineCount"/> lines (<c>_getCorrectedLineOrColumn</c>): at least 1, at most the last.</summary>
    public static int Clamp(long line, int lineCount) => (int)Math.Min(Math.Max(line, 1), Math.Max(1, lineCount));

    // The white space Number() trims: the ASCII kinds, no-break space, the byte order mark and the two separators.
    private static readonly char[] JavaScriptWhiteSpace = [' ', '\t', '\n', '\r', '\v', '\f', (char)0x00A0, (char)0xFEFF, (char)0x2028, (char)0x2029];

    /// <summary>What JavaScript's <c>Number(text)</c> answers: null for NaN.</summary>
    internal static double? JavaScriptNumber(string text)
    {
        string trimmed = text.Trim(JavaScriptWhiteSpace);
        if (trimmed.Length == 0)
        {
            return 0;
        }

        if (trimmed is "Infinity" or "+Infinity")
        {
            return double.PositiveInfinity;
        }

        if (trimmed == "-Infinity")
        {
            return double.NegativeInfinity;
        }

        if (trimmed.Length > 2 && trimmed[0] == '0' && char.ToLowerInvariant(trimmed[1]) is 'x' or 'o' or 'b')
        {
            int radix = char.ToLowerInvariant(trimmed[1]) switch { 'x' => 16, 'o' => 8, _ => 2 };
            double value = 0;
            foreach (char c in trimmed[2..])
            {
                int digit = c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'f' => c - 'a' + 10,
                    >= 'A' and <= 'F' => c - 'A' + 10,
                    _ => 99,
                };
                if (digit >= radix)
                {
                    return null;
                }

                value = value * radix + digit;
            }

            return value;
        }

        // A decimal literal: an optional sign, digits with an optional point either side, an optional exponent.
        int i = 0;
        if (trimmed[i] is '+' or '-')
        {
            i++;
        }

        int digits = 0;
        while (i < trimmed.Length && char.IsAsciiDigit(trimmed[i]))
        {
            i++;
            digits++;
        }

        if (i < trimmed.Length && trimmed[i] == '.')
        {
            i++;
            while (i < trimmed.Length && char.IsAsciiDigit(trimmed[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return null;
        }

        if (i < trimmed.Length && trimmed[i] is 'e' or 'E')
        {
            i++;
            if (i < trimmed.Length && trimmed[i] is '+' or '-')
            {
                i++;
            }

            int exponentDigits = 0;
            while (i < trimmed.Length && char.IsAsciiDigit(trimmed[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                return null;
            }
        }

        if (i != trimmed.Length)
        {
            return null;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
    }
}
