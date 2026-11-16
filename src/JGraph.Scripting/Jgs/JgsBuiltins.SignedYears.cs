using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A datetime read from text whose year .NET's parser cannot hold (open item 347, ADR 0384; probe p8d2
/// n1 to n23): a year 0, before it or past 9999. R2025b's <c>u</c> takes a signed year (<c>-0005</c>,
/// <c>-5</c> under <c>u</c>, <c>-05</c> under <c>uu</c>, five digits under <c>uuuu</c>), <c>y</c> an
/// unsigned one, and <c>G</c> the eras <c>BCE</c> and <c>CE</c> (year 5 BCE is the year -4); a plus sign,
/// <c>BC</c> and <c>AD</c> are no parse. The text is matched against a pattern made from the format and
/// the moment built from its fields.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The moment <paramref name="text"/> spells in <paramref name="format"/> when its year is out of .NET's range, or null.</summary>
    private static double? SignedYearMoment(string text, string format)
    {
        var pattern = new StringBuilder("^");
        int i = 0;
        bool signed = false;
        while (i < format.Length)
        {
            char c = format[i];
            if (c == '\'')
            {
                int close = format.IndexOf('\'', i + 1);
                int end = close < 0 ? format.Length : close;
                pattern.Append(Regex.Escape(format[(i + 1)..end]));
                i = System.Math.Min(format.Length, end + 1);
                continue;
            }

            int run = 1;
            while (i + run < format.Length && format[i + run] == c)
            {
                run++;
            }

            i += run;
            string? piece = c switch
            {
                'u' => "(?<year>-?\\d+)",
                'y' => "(?<year>\\d+)",
                'M' or 'L' => run >= 4 ? "(?<monthname>[A-Za-z]+)" : run == 3 ? "(?<monthname>[A-Za-z]{3})" : "(?<month>\\d{1,2})",
                'd' => "(?<day>\\d{1,2})",
                'H' or 'h' or 'k' or 'K' => "(?<hour>\\d{1,2})",
                'm' => "(?<minute>\\d{1,2})",
                's' => "(?<second>\\d{1,2})",
                'S' => "(?<fraction>\\d+)",
                'a' => "(?<meridiem>[AaPp][Mm])",
                'G' => "(?<era>BCE|CE)",
                'E' or 'e' => "[A-Za-z]+",
                _ when char.IsAsciiLetter(c) => null,
                _ => Regex.Escape(new string(c, run)),
            };

            if (piece is null || (pattern.ToString().Contains("(?<year>", StringComparison.Ordinal) && piece.Contains("(?<year>", StringComparison.Ordinal)))
            {
                return null;
            }

            signed |= c == 'u';
            pattern.Append(piece);
        }

        Match match = Regex.Match(text.Trim(), pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
        if (!match.Success || !match.Groups["year"].Success
            || !long.TryParse(match.Groups["year"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long year))
        {
            return null;
        }

        if (match.Groups["era"].Success && match.Groups["era"].Value == "BCE")
        {
            year = 1 - year;
        }

        if (year is >= 1 and <= 9999 && !(signed && match.Groups["year"].Value.StartsWith('-')))
        {
            return null; // .NET reads it, or refused it for a reason of its own
        }

        int month = 1;
        if (match.Groups["month"].Success)
        {
            month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        }
        else if (match.Groups["monthname"].Success)
        {
            string name = match.Groups["monthname"].Value;
            DateTimeFormatInfo names = CultureInfo.InvariantCulture.DateTimeFormat;
            month = Array.FindIndex(names.AbbreviatedMonthNames, n => n.Length > 0 && string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) + 1;
            if (month == 0)
            {
                month = Array.FindIndex(names.MonthNames, n => n.Length > 0 && string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) + 1;
            }
        }

        double Field(string group) => match.Groups[group].Success ? double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        double day = match.Groups["day"].Success ? Field("day") : 1;
        double hour = Field("hour");
        if (match.Groups["meridiem"].Success)
        {
            bool afternoon = char.ToUpperInvariant(match.Groups["meridiem"].Value[0]) == 'P';
            hour = (hour % 12) + (afternoon ? 12 : 0);
        }

        double minute = Field("minute");
        double second = Field("second");
        if (match.Groups["fraction"].Success)
        {
            second += double.Parse("0." + match.Groups["fraction"].Value, CultureInfo.InvariantCulture);
        }

        // The same day of the four-hundred-year cycle .NET can hold says whether the date exists.
        int standIn = (int)(((year % 400) + 400) % 400) + 2000;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(standIn, month) || hour > 23 || minute > 59 || second >= 60)
        {
            return null;
        }

        return JgsTime.FromComponents(year, month, day, hour, minute, second);
    }

    /// <summary>The shapes R2025b reads a signed year in without an InputFormat (probe p8d2 n21).</summary>
    private static readonly string[] SignedYearGuesses = ["uuuu-MM-dd HH:mm:ss", "uuuu-MM-dd", "dd-MMM-uuuu HH:mm:ss", "dd-MMM-uuuu"];
}
