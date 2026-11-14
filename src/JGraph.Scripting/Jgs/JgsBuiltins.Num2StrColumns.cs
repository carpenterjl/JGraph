using System.Globalization;
using System.Numerics;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>num2str</c>'s column layout as R2025b's num2str.m writes it, for a complex array and for a
/// format with one conversion (open item 316, ADR 0359): each element printed into a cell of its
/// own, a column right-justified, every column padded on the left to one width, and the result's
/// shared blank columns trimmed.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>num2str(z)</c>, <c>num2str(z, n)</c> and <c>num2str(z, fmt)</c> of a complex array: the real
    /// parts right-justified and the imaginary parts, each with its sign and <c>i</c>, left-justified
    /// after them, every column as wide as the widest or as num2str.m's forced width, a blank after
    /// each column unless a format was given, and the shared blanks trimmed.
    /// </summary>
    private static JgsValue Num2StrComplex(JgsValue subject, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int rows = JgsMatrix.RowCount(subject);
        int cols = subject.Type is JgsType.Complex or JgsType.Number or JgsType.Bool ? 1
            : JgsMatrix.IsNested(subject) ? JgsMatrix.ColCount(subject)
            : subject.ArrayLength / Math.Max(1, rows);
        var values = new Complex[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Complex z = subject.Type is JgsType.Complex or JgsType.Number or JgsType.Bool
                    ? subject.AsComplex
                    : (JgsMatrix.IsNested(subject) ? subject.ElementAt(r).ElementAt(c) : subject.ElementAt((c * rows) + r)).AsComplex;

                // 0 + x: a negative zero in either part is written as the zero it is.
                values[r, c] = new Complex(z.Real == 0 ? 0 : z.Real, z.Imaginary == 0 ? 0 : z.Imaginary);
            }
        }

        string format;
        double forceWidth;
        bool padWithSpace = true;
        if (args.Count < 2)
        {
            // The width of the elements, NaN and Inf counting as three digits (num2str.m's 314).
            double xmax = 0;
            bool whole = true;
            foreach (Complex z in values)
            {
                bool finite = double.IsFinite(z.Real) && double.IsFinite(z.Imaginary);
                xmax = Math.Max(xmax, finite ? Complex.Abs(z) : 314);
                whole &= Num2StrWhole(z.Real) && Num2StrWhole(z.Imaginary);
            }

            whole &= Math.BitIncrement(xmax) - xmax <= 1; // eps(xmax) <= 1
            if (whole)
            {
                double digits = Math.Min(12, Math.Floor(Math.Log10(xmax)) + 1);
                forceWidth = digits + 1;
                format = "%d";
            }
            else
            {
                int digits = xmax == 0 ? 1 : (int)Math.Min(12, Math.Max(1, Math.Floor(Math.Log10(xmax)) + 1)) + 4;
                forceWidth = digits + 6;
                format = "%." + digits.ToString(CultureInfo.InvariantCulture) + "g";
            }
        }
        else if (args[1].Type == JgsType.String)
        {
            format = args[1].AsString;
            forceWidth = 0;
            padWithSpace = false;
            if (!format.Contains('%', StringComparison.Ordinal))
            {
                throw Num2StrFormatInvalid(format, line, col);
            }
        }
        else if (args[1].Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
        {
            int digits = (int)Math.Round(args[1].AsComplex.Real, MidpointRounding.AwayFromZero);
            forceWidth = digits + 6;
            format = "%." + digits.ToString(CultureInfo.InvariantCulture) + "g";
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:num2str:invalidSecondArgument",
                "Second argument to num2str must be a character vector, string scalar, or numeric.");
        }

        forceWidth = (2 * forceWidth) + 2;
        var columns = new string[cols][];
        var widths = new int[cols];
        for (int c = 0; c < cols; c++)
        {
            var reals = new string[rows];
            var imaginaries = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                reals[r] = Num2StrCell(format, values[r, c].Real, imaginary: false, line, col);
                imaginaries[r] = Num2StrCell(format, values[r, c].Imaginary, imaginary: true, line, col);
            }

            string[] realColumn = Num2StrJustify(reals, left: false);
            string[] imaginaryColumn = Num2StrJustify(imaginaries, left: true);
            columns[c] = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                columns[c][r] = realColumn[r] + imaginaryColumn[r];
            }

            widths[c] = rows == 0 ? 0 : columns[c][0].Length;
        }

        return Num2StrJoin(Num2StrPadded(columns, widths, forceWidth, padWithSpace, rows));
    }

    /// <summary>
    /// <c>num2str(A, fmt)</c> of a real array. A format with one conversion is printed an element at a
    /// time and laid out in num2str.m's columns, right-justified (left when the conversion is
    /// left-aligned in a width) and trimmed: <c>num2str([1; 10], '%d')</c> is <c>[' 1'; '10']</c>.
    /// Any other format keeps the row-at-a-time printing (open item 316, ADR 0359).
    /// </summary>
    private static string[] Num2StrFormatted(string format, double[][] rows, int line, int col)
    {
        if (Num2StrConversions(format) is not [var spec])
        {
            return FormattedRows(format, rows, line, col);
        }

        int cols = rows.Length == 0 ? 0 : rows[0].Length;
        var columns = new string[cols][];
        var widths = new int[cols];
        bool left = spec.LeftInWidth;
        if (cols == 1)
        {
            columns[0] = Num2StrJustify(FormattedRows(format, rows, line, col), left);
            widths[0] = rows.Length == 0 ? 0 : columns[0][0].Length;
        }
        else
        {
            for (int c = 0; c < cols; c++)
            {
                var cells = new string[rows.Length];
                for (int r = 0; r < rows.Length; r++)
                {
                    cells[r] = Num2StrCell(format, rows[r][c], imaginary: false, line, col);
                }

                columns[c] = Num2StrJustify(cells, left);
                widths[c] = rows.Length == 0 ? 0 : columns[c][0].Length;
            }
        }

        return Num2StrPadded(columns, widths, 0, padWithSpace: false, rows.Length);
    }

    /// <summary>Whether a part is whole as <c>isequaln(x, fix(x))</c> reads it: NaN and Inf are.</summary>
    private static bool Num2StrWhole(double value) => double.IsNaN(value) || value == Math.Truncate(value);

    /// <summary>R2025b's refusal of a format with no conversion in it.</summary>
    private static JgsRuntimeException Num2StrFormatInvalid(string format, int line, int col) =>
        new(line, col, "MATLAB:num2str:fmtInvalid", $"'{format}' is an invalid format.");

    /// <summary>
    /// One element as sprintfc prints it for num2str. A real part is the format applied to the value.
    /// An imaginary part is each conversion without its flags and width, signed, and followed by
    /// <c>i</c>: <c>'%10.5f'</c> of 0.1 is <c>+0.10000i</c>. The sign is a plus unless the text has
    /// a minus of its own; an unsigned conversion (<c>u o x X</c>) always takes the plus, and
    /// <c>c</c> and <c>s</c> take none (probe_sprintfc).
    /// </summary>
    private static string Num2StrCell(string format, double value, bool imaginary, int line, int col)
    {
        try
        {
            if (!imaginary)
            {
                return JgsSprintf.FormatMatlab(format, [JgsValue.Number(value)]);
            }

            var built = new StringBuilder(format.Length + 4);
            int at = 0;
            bool used = false;
            foreach (Num2StrSpec spec in Num2StrConversions(format))
            {
                built.Append(JgsSprintf.FormatMatlab(format[at..spec.Start], []));
                if (used)
                {
                    return built.ToString(); // a second conversion finds no value: printing stops
                }

                string text = JgsSprintf.FormatMatlab(spec.Stripped, [JgsValue.Number(value)]);
                string sign = spec.Verb switch
                {
                    'u' or 'o' or 'x' or 'X' => "+",
                    'c' or 's' => string.Empty,
                    _ => text.StartsWith('-') ? string.Empty : "+",
                };
                built.Append(sign).Append(text).Append('i');
                used = true;
                at = spec.Start + spec.Length;
            }

            built.Append(JgsSprintf.FormatMatlab(format[at..], []));
            return built.ToString();
        }
        catch (FormatException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>
    /// A column of cells as one width: left-justified (char's padding), or right-justified as
    /// <c>strjust</c> does it, trailing blanks moved to the front.
    /// </summary>
    private static string[] Num2StrJustify(string[] cells, bool left)
    {
        int width = 0;
        foreach (string cell in cells)
        {
            width = Math.Max(width, cell.Length);
        }

        var justified = new string[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            justified[i] = left ? cells[i].PadRight(width) : cells[i].TrimEnd(' ').PadLeft(width);
        }

        return justified;
    }

    /// <summary>
    /// Every column padded on the left to the widest column or the forced width, a blank after each
    /// when <paramref name="padWithSpace"/>, the columns joined into rows.
    /// </summary>
    private static string[] Num2StrPadded(string[][] columns, int[] widths, double forceWidth, bool padWithSpace, int rowCount)
    {
        double widest = forceWidth;
        foreach (int width in widths)
        {
            widest = Math.Max(widest, width);
        }

        var text = new StringBuilder[rowCount];
        for (int r = 0; r < rowCount; r++)
        {
            text[r] = new StringBuilder();
        }

        for (int c = 0; c < columns.Length; c++)
        {
            int pad = (int)Math.Max(0, widest - widths[c]);
            for (int r = 0; r < rowCount; r++)
            {
                text[r].Append(' ', pad).Append(columns[c][r]);
                if (padWithSpace)
                {
                    text[r].Append(' ');
                }
            }
        }

        return Num2StrTrimmed(Array.ConvertAll(text, static b => b.ToString()));
    }

    /// <summary><c>strtrim</c> of a char matrix: the blank columns every row shares at either end removed.</summary>
    private static string[] Num2StrTrimmed(string[] rows)
    {
        if (rows.Length == 0)
        {
            return rows;
        }

        int lead = int.MaxValue;
        int trail = int.MaxValue;
        foreach (string row in rows)
        {
            int l = 0;
            while (l < row.Length && Array.IndexOf(TextWhitespace, row[l]) >= 0)
            {
                l++;
            }

            int t = 0;
            while (t < row.Length - l && Array.IndexOf(TextWhitespace, row[row.Length - 1 - t]) >= 0)
            {
                t++;
            }

            lead = Math.Min(lead, l);
            trail = Math.Min(trail, l == row.Length ? row.Length : t);
        }

        var trimmed = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            int keep = Math.Max(0, rows[i].Length - lead - trail);
            trimmed[i] = keep == 0 ? string.Empty : rows[i].Substring(lead, keep);
        }

        return trimmed;
    }

    /// <summary>One row as char, several as a char matrix.</summary>
    private static JgsValue Num2StrJoin(string[] text) =>
        text.Length == 1 ? JgsValue.Str(text[0]) : JgsValue.CharMatrix(text);

    /// <summary>A conversion in a num2str format: where it is, its verb, and its imaginary-part form.</summary>
    private readonly record struct Num2StrSpec(int Start, int Length, char Verb, string Stripped, bool LeftInWidth);

    /// <summary>
    /// The conversions of a format, <c>%%</c> skipped: each one's position, verb, the conversion
    /// without flags or width (kept: <c>#</c>, the precision, a length letter), and whether it is
    /// left-aligned in a width, which is when num2str.m keeps a column left-justified.
    /// </summary>
    private static List<Num2StrSpec> Num2StrConversions(string format)
    {
        var specs = new List<Num2StrSpec>();
        int i = 0;
        while (i < format.Length)
        {
            if (format[i] != '%')
            {
                i++;
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                i += 2;
                continue;
            }

            int start = i++;
            int digitsAt = i;
            while (digitsAt < format.Length && char.IsAsciiDigit(format[digitsAt]))
            {
                digitsAt++;
            }

            if (digitsAt > i && digitsAt < format.Length && format[digitsAt] == '$')
            {
                i = digitsAt + 1; // a position names its argument; it does not change the layout
            }

            bool leftAlign = false;
            bool alternate = false;
            while (i < format.Length && format[i] is '-' or '+' or ' ' or '0' or '#')
            {
                leftAlign |= format[i] == '-';
                alternate |= format[i] == '#';
                i++;
            }

            bool hasWidth = false;
            if (i < format.Length && format[i] == '*')
            {
                hasWidth = true;
                i++;
            }
            else
            {
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    hasWidth = true;
                    i++;
                }
            }

            int precisionAt = i;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                if (i < format.Length && format[i] == '*')
                {
                    i++;
                }
                else
                {
                    while (i < format.Length && char.IsAsciiDigit(format[i]))
                    {
                        i++;
                    }
                }
            }

            int lengthAt = i;
            while (i < format.Length && format[i] is 'l' or 'h' or 'L' or 'q' or 'j' or 'z' or 't')
            {
                i++;
            }

            if (i >= format.Length)
            {
                break; // an unfinished conversion: sprintf's own reading decides
            }

            char verb = format[i];
            i++;
            string stripped = "%" + (alternate ? "#" : string.Empty) + format[precisionAt..lengthAt] + format[lengthAt..i];
            specs.Add(new Num2StrSpec(start, i - start, verb, stripped, leftAlign && hasWidth));
        }

        return specs;
    }
}
