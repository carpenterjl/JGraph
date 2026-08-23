using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The column-aware scanner behind <c>textscan</c> (M76): it reads a format repeatedly and keeps
/// one column of values per conversion in it, which is the shape <c>textscan</c> answers in and the
/// one thing the flat <c>sscanf</c> engine beside it cannot produce.
/// </summary>
/// <remarks>
/// <para>
/// <c>sscanf</c> and <c>fscanf</c> read a stream of values and are right to: their answer is one
/// array, and which conversion produced which element is not a question they are asked.
/// <c>textscan</c> reads a table, and a table's columns have different types — a name column is
/// text where the column beside it is a number — so the two engines stay separate rather than one
/// growing a mode.
/// </para>
/// <para>
/// What is read is bounded by the format matching, so the scanner stops where the data stops
/// looking like the format. That is what makes a header line detectable, and what lets the caller
/// put a file's read position back to exactly where the scan finished.
/// </para>
/// </remarks>
internal static class JgsTextScanner
{
    /// <summary>One conversion in a compiled format.</summary>
    private readonly record struct Conversion(char Kind, int Width, bool Skipped, string Set, bool Negated);

    /// <summary>One piece of a compiled format: a conversion, or literal text that must be matched.</summary>
    private readonly record struct Piece(Conversion? Conversion, string Literal);

    /// <summary>The options <c>textscan</c> takes as trailing name/value pairs.</summary>
    internal sealed class Options
    {
        public IReadOnlyList<string> Delimiters { get; init; } = [];

        public int HeaderLines { get; init; }

        public string Whitespace { get; init; } = " \t";

        public double EmptyValue { get; init; } = double.NaN;

        public bool CollectOutput { get; init; }
    }

    /// <summary>
    /// Reads <paramref name="text"/> under <paramref name="format"/> at most
    /// <paramref name="repetitions"/> times, and answers one column per conversion.
    /// </summary>
    internal static (List<JgsValue> Columns, int Consumed) Scan(
        string text, string format, int repetitions, Options options, int line, int col)
    {
        List<Piece> pieces = Compile(format, line, col);
        int conversions = 0;
        foreach (Piece piece in pieces)
        {
            if (piece.Conversion is { Skipped: false })
            {
                conversions++;
            }
        }

        if (conversions == 0)
        {
            throw new JgsRuntimeException(line, col,
                "textscan: the format has no conversions, so there is nothing for it to read.");
        }

        var numbers = new List<double>[conversions];
        var strings = new List<string>[conversions];
        var kinds = new char[conversions];
        for (int i = 0; i < conversions; i++)
        {
            numbers[i] = [];
            strings[i] = [];
        }

        int at = SkipHeaderLines(text, options.HeaderLines);
        int done = 0;

        while (at < text.Length && done < repetitions)
        {
            int before = at;
            int column = 0;
            bool complete = true;

            foreach (Piece piece in pieces)
            {
                if (piece.Conversion is not { } conversion)
                {
                    if (!MatchLiteral(text, ref at, piece.Literal, options))
                    {
                        complete = false;
                        break;
                    }

                    continue;
                }

                SkipWhitespaceAndDelimiters(text, ref at, options, conversion.Kind);
                if (at >= text.Length && conversion.Kind != 'c')
                {
                    complete = false;
                    break;
                }

                bool read = ReadOne(text, ref at, conversion, options,
                    out double number, out string word);
                if (!read)
                {
                    complete = false;
                    break;
                }

                if (conversion.Skipped)
                {
                    continue;
                }

                kinds[column] = conversion.Kind;
                if (conversion.Kind is 's' or 'q' or 'c' or '[')
                {
                    strings[column].Add(word);
                }
                else
                {
                    numbers[column].Add(number);
                }

                column++;
            }

            if (!complete || at == before)
            {
                // A partial record is not a record. The position goes back to where it started so
                // that the caller's file is left pointing at the text the scan could not read.
                at = before;
                break;
            }

            done++;
        }

        var answer = new List<JgsValue>(conversions);
        for (int i = 0; i < conversions; i++)
        {
            answer.Add(ColumnValue(kinds[i], numbers[i], strings[i]));
        }

        if (options.CollectOutput)
        {
            answer = Collected(answer, kinds);
        }

        return (answer, at);
    }

    /// <summary>One column as the value it stands for: a cell of text, or a numeric column.</summary>
    private static JgsValue ColumnValue(char kind, List<double> numbers, List<string> words)
    {
        if (kind is 's' or 'q' or 'c' or '[')
        {
            var cells = new JgsValue[words.Count];
            for (int i = 0; i < words.Count; i++)
            {
                cells[i] = JgsValue.Str(words[i]);
            }

            JgsValue cell = JgsValue.Cell(cells);
            if (cells.Length > 1)
            {
                cell.Reshape(cells.Length, 1);
            }

            return cell;
        }

        var column = new JgsValue[numbers.Count];
        for (int i = 0; i < numbers.Count; i++)
        {
            column[i] = JgsValue.Number(numbers[i]);
        }

        JgsValue value = JgsValue.Array(column);
        if (column.Length > 1)
        {
            value.Reshape(column.Length, 1);
        }

        // MATLAB's textscan reads %d and %u into integer classes rather than doubles, which is how
        // a script can tell a count column from a measurement one.
        if (kind is 'd' or 'u')
        {
            value.SetNumericClass(kind == 'd' ? JgsNumericClass.Int32 : JgsNumericClass.UInt32);
        }

        return value;
    }

    /// <summary>
    /// Neighbouring columns of the same kind gathered into one array — MATLAB's
    /// <c>'CollectOutput'</c>, which is what makes a table of numbers one matrix instead of many
    /// columns.
    /// </summary>
    private static List<JgsValue> Collected(List<JgsValue> columns, char[] kinds)
    {
        var gathered = new List<JgsValue>();
        int i = 0;
        while (i < columns.Count)
        {
            bool numeric = kinds[i] is not ('s' or 'q' or 'c' or '[');
            int run = 1;
            while (i + run < columns.Count
                && (kinds[i + run] is not ('s' or 'q' or 'c' or '[')) == numeric)
            {
                run++;
            }

            if (!numeric || run == 1)
            {
                for (int k = 0; k < run; k++)
                {
                    gathered.Add(columns[i + k]);
                }
            }
            else
            {
                int rows = columns[i].ArrayLength;
                var flat = new JgsValue[rows * run];
                for (int c = 0; c < run; c++)
                {
                    for (int r = 0; r < rows && r < columns[i + c].ArrayLength; r++)
                    {
                        flat[(c * rows) + r] = columns[i + c].ElementAt(r);
                    }
                }

                JgsValue merged = JgsValue.Array(flat);
                merged.Reshape(rows, run);
                gathered.Add(merged);
            }

            i += run;
        }

        return gathered;
    }

    private static int SkipHeaderLines(string text, int count)
    {
        int at = 0;
        for (int i = 0; i < count && at < text.Length; i++)
        {
            int newline = text.IndexOf('\n', at);
            at = newline < 0 ? text.Length : newline + 1;
        }

        return at;
    }

    private static bool MatchLiteral(string text, ref int at, string literal, Options options)
    {
        foreach (char expected in literal)
        {
            if (char.IsWhiteSpace(expected))
            {
                while (at < text.Length && char.IsWhiteSpace(text[at]))
                {
                    at++;
                }

                continue;
            }

            SkipWhitespaceAndDelimiters(text, ref at, options, expected);
            if (at >= text.Length || text[at] != expected)
            {
                return false;
            }

            at++;
        }

        return true;
    }

    private static void SkipWhitespaceAndDelimiters(string text, ref int at, Options options, char kind)
    {
        if (kind == 'c')
        {
            return; // %c takes the next character whatever it is, whitespace included
        }

        bool moved = true;
        while (moved && at < text.Length)
        {
            moved = false;
            while (at < text.Length && (options.Whitespace.Contains(text[at]) || text[at] is '\r' or '\n'))
            {
                at++;
                moved = true;
            }

            foreach (string delimiter in options.Delimiters)
            {
                if (delimiter.Length > 0 && at + delimiter.Length <= text.Length
                    && string.CompareOrdinal(text, at, delimiter, 0, delimiter.Length) == 0)
                {
                    at += delimiter.Length;
                    moved = true;
                    break;
                }
            }
        }
    }

    /// <summary>Reads one conversion's worth of text, answering whether there was any.</summary>
    private static bool ReadOne(string text, ref int at, Conversion conversion, Options options,
        out double number, out string word)
    {
        number = 0;
        word = string.Empty;
        int limit = conversion.Width > 0
            ? System.Math.Min(text.Length, at + conversion.Width)
            : text.Length;

        switch (conversion.Kind)
        {
            case 'c':
                if (at >= text.Length)
                {
                    return false;
                }

                word = text[at++].ToString();
                return true;

            case 'q':
            {
                if (at < text.Length && text[at] == '"')
                {
                    at++;
                    var quoted = new StringBuilder();
                    while (at < text.Length && text[at] != '"')
                    {
                        quoted.Append(text[at++]);
                    }

                    if (at < text.Length)
                    {
                        at++; // the closing quote
                    }

                    word = quoted.ToString();
                    return true;
                }

                goto case 's';
            }

            case 's':
            {
                int start = at;
                while (at < limit && !IsBreak(text[at], options))
                {
                    at++;
                }

                word = text[start..at];
                return word.Length > 0;
            }

            case '[':
            {
                int start = at;
                while (at < limit && conversion.Set.Contains(text[at]) != conversion.Negated)
                {
                    at++;
                }

                word = text[start..at];
                return word.Length > 0;
            }

            default:
            {
                int start = at;
                if (at < limit && (text[at] == '-' || text[at] == '+'))
                {
                    at++;
                }

                while (at < limit && (char.IsAsciiDigit(text[at]) || text[at] == '.'))
                {
                    at++;
                }

                if (at < limit && (text[at] == 'e' || text[at] == 'E'))
                {
                    int save = at;
                    at++;
                    if (at < limit && (text[at] == '-' || text[at] == '+'))
                    {
                        at++;
                    }

                    if (at < limit && char.IsAsciiDigit(text[at]))
                    {
                        while (at < limit && char.IsAsciiDigit(text[at]))
                        {
                            at++;
                        }
                    }
                    else
                    {
                        at = save;
                    }
                }

                string digits = text[start..at];
                if (digits.Length == 0 || digits is "-" or "+" or ".")
                {
                    // An empty field is what the EmptyValue option is for; without one it is simply
                    // not a number and the record stops here.
                    at = start;
                    if (double.IsNaN(options.EmptyValue))
                    {
                        return false;
                    }

                    number = options.EmptyValue;
                    return true;
                }

                number = double.Parse(digits, CultureInfo.InvariantCulture);
                return true;
            }
        }
    }

    private static bool IsBreak(char character, Options options)
    {
        if (char.IsWhiteSpace(character) || options.Whitespace.Contains(character))
        {
            return true;
        }

        foreach (string delimiter in options.Delimiters)
        {
            if (delimiter.Length > 0 && delimiter[0] == character)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Turns a format into the literals and conversions it is made of, once.</summary>
    private static List<Piece> Compile(string format, int line, int col)
    {
        var pieces = new List<Piece>();
        var literal = new StringBuilder();

        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                literal.Append(format[i]);
                continue;
            }

            if (literal.Length > 0)
            {
                pieces.Add(new Piece(null, literal.ToString()));
                literal.Clear();
            }

            i++;
            if (i < format.Length && format[i] == '%')
            {
                literal.Append('%');
                continue;
            }

            bool skipped = false;
            if (i < format.Length && format[i] == '*')
            {
                skipped = true;
                i++;
            }

            int width = 0;
            while (i < format.Length && char.IsAsciiDigit(format[i]))
            {
                width = (width * 10) + (format[i] - '0');
                i++;
            }

            while (i < format.Length && format[i] is 'l' or 'h')
            {
                i++;
            }

            if (i >= format.Length)
            {
                throw new JgsRuntimeException(line, col, "textscan: the format ends in an unfinished conversion.");
            }

            char kind = format[i];
            if (kind == '[')
            {
                int close = format.IndexOf(']', i + 1);
                if (close < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "textscan: a %[...] conversion has no closing bracket.");
                }

                string set = format[(i + 1)..close];
                bool negated = set.StartsWith('^');
                pieces.Add(new Piece(new Conversion('[', width, skipped, negated ? set[1..] : set, negated), string.Empty));
                i = close;
                continue;
            }

            if (kind is not ('d' or 'u' or 'f' or 'n' or 'g' or 'e' or 's' or 'q' or 'c'))
            {
                throw new JgsRuntimeException(line, col,
                    $"textscan does not support the conversion '%{kind}'. It reads " +
                    "%d, %u, %f, %n, %g, %e, %s, %q, %c and %[...].");
            }

            pieces.Add(new Piece(new Conversion(kind, width, skipped, string.Empty, false), string.Empty));
        }

        if (literal.Length > 0)
        {
            pieces.Add(new Piece(null, literal.ToString()));
        }

        return pieces;
    }
}
