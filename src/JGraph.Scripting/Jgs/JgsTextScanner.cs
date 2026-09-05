using System.Globalization;
using System.Linq;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The column-aware scanner behind <c>textscan</c>: it reads a format repeatedly and keeps one
/// column of values per conversion in it, which is the shape <c>textscan</c> answers in and the one
/// thing the flat <c>sscanf</c> engine beside it cannot produce.
/// </summary>
/// <remarks>
/// <para>
/// Rebuilt against MATLAB R2025b for the string-function audit. The rules that matter most are the
/// ones about what sits <em>between</em> fields. A delimiter that is followed by another delimiter,
/// by the end of a line or by the end of the text marks an empty field, which is read as
/// <c>'EmptyValue'</c> (NaN, or 0 for an integer conversion); a delimiter at the very start of a line
/// does the same; but a line break with no delimiter before it is only a line break, so a record may
/// continue on the next line and a final partial record is kept as far as it got. Whitespace is
/// skipped before every field and never marks one, except that a character named as a delimiter
/// stops being whitespace.
/// </para>
/// <para>
/// A field that does not read as its conversion stops the scan where it stands unless
/// <c>'ReturnOnError'</c> is false, in which case it is an error. <c>'TreatAsEmpty'</c> names text
/// that reads as an empty numeric field; <c>'CommentStyle'</c> blanks comments before anything is
/// read; <c>'EndOfLine'</c> names the line break; <c>'MultipleDelimsAsOne'</c> makes a run of
/// delimiters one delimiter. <c>%d8</c> and its siblings answer the integer class they name, saturating
/// and rounding as MATLAB does; <c>%c</c> answers a char column.
/// </para>
/// </remarks>
internal static class JgsTextScanner
{
    /// <summary>One conversion in a compiled format.</summary>
    private readonly record struct Conversion(
        char Kind, int Width, int Precision, bool Skipped, string Set, bool Negated, JgsNumericClass Class);

    /// <summary>One piece of a compiled format: a conversion, or literal text that must be matched.</summary>
    private readonly record struct Piece(Conversion? Conversion, string Literal);

    /// <summary>What reading one field came to.</summary>
    private enum Outcome
    {
        /// <summary>A value was read.</summary>
        Value,

        /// <summary>The field was empty: a delimiter with nothing before it, or a word named by TreatAsEmpty.</summary>
        Empty,

        /// <summary>The text ran out before the field.</summary>
        End,

        /// <summary>The text there does not read as the conversion.</summary>
        Mismatch,
    }

    /// <summary>The options <c>textscan</c> takes as trailing name/value pairs.</summary>
    internal sealed class Options
    {
        public IReadOnlyList<string> Delimiters { get; init; } = [];

        public int HeaderLines { get; init; }

        public string Whitespace { get; init; } = " \b\t";

        public double EmptyValue { get; init; } = double.NaN;

        public bool CollectOutput { get; init; }

        /// <summary>The line break, or null for any of \n, \r\n and \r.</summary>
        public string? EndOfLine { get; init; }

        public string? CommentOpen { get; init; }

        /// <summary>What closes a comment, or null for the end of the line.</summary>
        public string? CommentClose { get; init; }

        public bool MultipleDelimsAsOne { get; init; }

        public IReadOnlyList<string> TreatAsEmpty { get; init; } = [];

        public bool ReturnOnError { get; init; } = true;
    }

    /// <summary>The scanner's cursor and the state that decides what an empty stretch means.</summary>
    private sealed class Cursor
    {
        public int At;

        /// <summary>Whether the last thing consumed was a delimiter, so that what follows is a field.</summary>
        public bool PendingDelimiter;
    }

    /// <summary>
    /// Reads <paramref name="text"/> under <paramref name="format"/> at most
    /// <paramref name="repetitions"/> times (any negative count is every time), and answers one
    /// column per conversion.
    /// </summary>
    internal static (List<JgsValue> Columns, int Consumed) Scan(
        string text, string format, int repetitions, Options options, int line, int col)
    {
        if (repetitions < 0)
        {
            repetitions = int.MaxValue;
        }

        text = WithoutComments(text, options);
        var cursor = new Cursor { At = SkipHeaderLines(text, options.HeaderLines, options) };

        if (format.Length == 0)
        {
            format = string.Concat(Enumerable.Repeat("%f", FieldsInFirstRecord(text, cursor.At, options)));
        }

        // A format with no conversion at all reads nothing and answers the 1-by-0 cell (measured),
        // exactly as a format of nothing but skipped conversions does.
        List<Piece> pieces = Compile(format, line, col);
        int conversions = 0;
        foreach (Piece piece in pieces)
        {
            if (piece.Conversion is { Skipped: false })
            {
                conversions++;
            }
        }

        var numbers = new List<double>[conversions];
        var words = new List<string>[conversions];
        var kinds = new Conversion[conversions];
        int slot = 0;
        foreach (Piece piece in pieces)
        {
            if (piece.Conversion is { Skipped: false } conversion)
            {
                numbers[slot] = [];
                words[slot] = [];
                kinds[slot] = conversion;
                slot++;
            }
        }

        int done = 0;
        bool finished = false;
        while (!finished && done < repetitions)
        {
            int recordStart = cursor.At;
            int column = 0;
            bool readAny = false;

            foreach (Piece piece in pieces)
            {
                if (piece.Conversion is not { } conversion)
                {
                    if (!MatchLiteral(text, cursor, piece.Literal, options))
                    {
                        finished = true;
                        break;
                    }

                    continue;
                }

                Outcome outcome = ReadField(text, cursor, conversion, options, column == 0 && !readAny,
                    out double number, out string word);
                if (outcome == Outcome.End)
                {
                    finished = true;
                    break;
                }

                if (outcome == Outcome.Mismatch)
                {
                    if (!options.ReturnOnError)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:textscan:handleErrorAndShowInfo",
                            $"Mismatch between file and format character vector. Trouble reading a '{ConversionName(conversion)}' field at position {cursor.At + 1}.");
                    }

                    finished = true;
                    break;
                }

                readAny = true;
                if (conversion.Skipped)
                {
                    continue;
                }

                if (conversion.Kind is 's' or 'q' or 'c' or '[')
                {
                    words[column].Add(outcome == Outcome.Empty ? string.Empty : word);
                }
                else
                {
                    numbers[column].Add(outcome == Outcome.Empty ? options.EmptyValue : number);
                }

                column++;
            }

            if (!readAny)
            {
                cursor.At = recordStart;
                break;
            }

            done++;
        }

        var answer = new List<JgsValue>(conversions);
        for (int i = 0; i < conversions; i++)
        {
            answer.Add(ColumnValue(kinds[i], numbers[i], words[i]));
        }

        if (options.CollectOutput)
        {
            answer = Collected(answer, kinds);
        }

        return (answer, cursor.At);
    }

    // --- the answer -----------------------------------------------------------------------------------

    /// <summary>One column as the value it stands for: a cell of text, a char column, or a numeric column.</summary>
    private static JgsValue ColumnValue(Conversion conversion, List<double> numbers, List<string> words)
    {
        if (conversion.Kind == 'c')
        {
            // A char column: one row per value, as wide as the conversion asked for.
            return words.Count == 1 ? JgsValue.Str(words[0]) : JgsValue.CharMatrix(words.ToArray());
        }

        if (conversion.Kind is 's' or 'q' or '[')
        {
            var cells = new JgsValue[words.Count];
            for (int i = 0; i < words.Count; i++)
            {
                cells[i] = JgsValue.Str(words[i]);
            }

            JgsValue cell = JgsValue.Cell(cells);
            cell.Reshape(cells.Length, 1);
            return cell;
        }

        var column = new JgsValue[numbers.Count];
        for (int i = 0; i < numbers.Count; i++)
        {
            column[i] = JgsValue.Number(InClass(numbers[i], conversion.Class));
        }

        if (column.Length == 0)
        {
            return JgsEmpty.Shaped(0, 1);
        }

        JgsValue value = JgsValue.Array(column);
        value.Reshape(column.Length, 1);
        if (conversion.Class != JgsNumericClass.Double)
        {
            value.SetNumericClass(conversion.Class);
        }

        return value;
    }

    /// <summary>A number as the class stores it: integers round and saturate, NaN becomes 0.</summary>
    private static double InClass(double value, JgsNumericClass numericClass)
    {
        (double low, double high) = numericClass switch
        {
            JgsNumericClass.Int8 => (sbyte.MinValue, sbyte.MaxValue),
            JgsNumericClass.Int16 => (short.MinValue, short.MaxValue),
            JgsNumericClass.Int32 => (int.MinValue, int.MaxValue),
            JgsNumericClass.Int64 => (long.MinValue, long.MaxValue),
            JgsNumericClass.UInt8 => (0, byte.MaxValue),
            JgsNumericClass.UInt16 => (0, ushort.MaxValue),
            JgsNumericClass.UInt32 => (0, uint.MaxValue),
            JgsNumericClass.UInt64 => (0, ulong.MaxValue),
            JgsNumericClass.Single => (double.NegativeInfinity, double.PositiveInfinity),
            _ => (double.NegativeInfinity, double.PositiveInfinity),
        };

        if (numericClass is JgsNumericClass.Double)
        {
            return value;
        }

        if (numericClass == JgsNumericClass.Single)
        {
            return (float)value;
        }

        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), low, high);
    }

    /// <summary>
    /// Neighbouring columns of one class gathered into one array — MATLAB's <c>'CollectOutput'</c>,
    /// which is what makes a table of numbers one matrix instead of many columns. Cells of text
    /// gather the same way; an int32 column and a double column do not (measured).
    /// </summary>
    private static List<JgsValue> Collected(List<JgsValue> columns, Conversion[] kinds)
    {
        static string KeyOf(Conversion conversion) => conversion.Kind switch
        {
            'c' => "char",
            's' or 'q' or '[' => "cell",
            _ => conversion.Class.ToString(),
        };

        var gathered = new List<JgsValue>();
        int i = 0;
        while (i < columns.Count)
        {
            string key = KeyOf(kinds[i]);
            int run = 1;
            while (i + run < columns.Count && KeyOf(kinds[i + run]) == key)
            {
                run++;
            }

            if (run == 1 || key == "char")
            {
                for (int k = 0; k < run; k++)
                {
                    gathered.Add(columns[i + k]);
                }
            }
            else
            {
                int rows = columns[i].Type == JgsType.Cell ? columns[i].AsCell.Length : columns[i].ArrayLength;
                var flat = new JgsValue[rows * run];
                for (int c = 0; c < run; c++)
                {
                    JgsValue source = columns[i + c];
                    JgsValue[] elements = source.Type == JgsType.Cell ? source.AsCell : source.BoxedElements();
                    for (int r = 0; r < rows; r++)
                    {
                        flat[(c * rows) + r] = r < elements.Length ? elements[r] : JgsValue.Str(string.Empty);
                    }
                }

                JgsValue merged = key == "cell" ? JgsValue.Cell(flat) : JgsValue.Array(flat);
                merged.Reshape(rows, run);
                if (key != "cell" && kinds[i].Class != JgsNumericClass.Double)
                {
                    merged.SetNumericClass(kinds[i].Class);
                }

                gathered.Add(merged);
            }

            i += run;
        }

        return gathered;
    }

    // --- the text ---------------------------------------------------------------------------------------

    /// <summary>The text with every comment blanked, character for character, so positions still hold.</summary>
    private static string WithoutComments(string text, Options options)
    {
        if (string.IsNullOrEmpty(options.CommentOpen))
        {
            return text;
        }

        var built = new StringBuilder(text);
        int at = 0;
        while ((at = text.IndexOf(options.CommentOpen, at, StringComparison.Ordinal)) >= 0)
        {
            int end;
            if (options.CommentClose is { Length: > 0 } close)
            {
                int closing = text.IndexOf(close, at + options.CommentOpen.Length, StringComparison.Ordinal);
                end = closing < 0 ? text.Length : closing + close.Length;
            }
            else
            {
                end = at;
                while (end < text.Length && !AtEndOfLine(text, end, options, out _))
                {
                    end++;
                }
            }

            for (int i = at; i < end; i++)
            {
                if (!AtEndOfLine(text, i, options, out _))
                {
                    built[i] = ' ';
                }
            }

            at = Math.Max(end, at + 1);
        }

        return built.ToString();
    }

    private static int SkipHeaderLines(string text, int count, Options options)
    {
        int at = 0;
        for (int i = 0; i < count && at < text.Length; i++)
        {
            while (at < text.Length && !AtEndOfLine(text, at, options, out _))
            {
                at++;
            }

            if (at < text.Length)
            {
                AtEndOfLine(text, at, options, out int length);
                at += length;
            }
        }

        return at;
    }

    /// <summary>How many fields the first record holds, for a format that names none.</summary>
    private static int FieldsInFirstRecord(string text, int at, Options options)
    {
        int end = at;
        while (end < text.Length && !AtEndOfLine(text, end, options, out _))
        {
            end++;
        }

        string first = text[at..end];
        if (options.Delimiters.Count > 0)
        {
            int fields = 1;
            for (int i = 0; i < first.Length; i++)
            {
                if (AtDelimiter(first, i, options, out int length))
                {
                    fields++;
                    i += length - 1;
                }
            }

            return fields;
        }

        return Math.Max(1, first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>Whether a line break starts at <paramref name="at"/>, and how long it is.</summary>
    private static bool AtEndOfLine(string text, int at, Options options, out int length)
    {
        length = 0;
        if (at >= text.Length)
        {
            return false;
        }

        if (options.EndOfLine is { Length: > 0 } eol)
        {
            if (string.CompareOrdinal(text, at, eol, 0, eol.Length) == 0)
            {
                length = eol.Length;
                return true;
            }

            return false;
        }

        if (text[at] == '\r')
        {
            length = at + 1 < text.Length && text[at + 1] == '\n' ? 2 : 1;
            return true;
        }

        if (text[at] == '\n')
        {
            length = 1;
            return true;
        }

        return false;
    }

    private static bool AtDelimiter(string text, int at, Options options, out int length)
    {
        foreach (string delimiter in options.Delimiters)
        {
            if (delimiter.Length > 0 && at + delimiter.Length <= text.Length
                && string.CompareOrdinal(text, at, delimiter, 0, delimiter.Length) == 0)
            {
                length = delimiter.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    /// <summary>Whether the character is whitespace to skip: named as such, and not also a delimiter.</summary>
    private static bool IsWhitespace(char character, Options options)
    {
        if (!options.Whitespace.Contains(character))
        {
            return false;
        }

        foreach (string delimiter in options.Delimiters)
        {
            if (delimiter.Length == 1 && delimiter[0] == character)
            {
                return false;
            }
        }

        return true;
    }

    private static void SkipWhitespace(string text, Cursor cursor, Options options)
    {
        while (cursor.At < text.Length && IsWhitespace(text[cursor.At], options))
        {
            cursor.At++;
        }
    }

    /// <summary>Consumes one delimiter, or a whole run of them with <c>'MultipleDelimsAsOne'</c>.</summary>
    private static void ConsumeDelimiter(string text, Cursor cursor, Options options, int length)
    {
        cursor.At += length;
        cursor.PendingDelimiter = true;
        if (!options.MultipleDelimsAsOne)
        {
            return;
        }

        while (true)
        {
            int save = cursor.At;
            SkipWhitespace(text, cursor, options);
            if (cursor.At < text.Length && AtDelimiter(text, cursor.At, options, out int more))
            {
                cursor.At += more;
                continue;
            }

            cursor.At = save;
            return;
        }
    }

    private static bool MatchLiteral(string text, Cursor cursor, string literal, Options options)
    {
        bool matchedText = false;
        foreach (char expected in literal)
        {
            if (char.IsWhiteSpace(expected))
            {
                // A blank in the format skips the whitespace the options name, and only that: a line
                // break is not swallowed here, so '3,' at the end of a line still leaves its second
                // field empty when the format is '%f %f' (measured).
                SkipWhitespace(text, cursor, options);
                continue;
            }

            SkipWhitespace(text, cursor, options);
            if (cursor.At >= text.Length || text[cursor.At] != expected)
            {
                return false;
            }

            cursor.At++;
            matchedText = true;
        }

        if (matchedText)
        {
            cursor.PendingDelimiter = false;
        }

        return true;
    }

    // --- one field ------------------------------------------------------------------------------------

    /// <summary>Reads one field: what lies before the next delimiter, line break or whitespace.</summary>
    private static Outcome ReadField(string text, Cursor cursor, Conversion conversion, Options options,
        bool firstInRecord, out double number, out string word)
    {
        number = 0;
        word = string.Empty;

        if (conversion.Kind == 'c')
        {
            // %c takes the next characters whatever they are, whitespace and delimiters included.
            int take = Math.Max(1, conversion.Width);
            if (cursor.At + take > text.Length)
            {
                return cursor.At >= text.Length ? Outcome.End : Outcome.Mismatch;
            }

            word = text.Substring(cursor.At, take);
            cursor.At += take;
            cursor.PendingDelimiter = false;
            return Outcome.Value;
        }

        // Where the field starts: past whitespace, and past line breaks. A delimiter met before
        // anything else is an empty field, and so is a line break met mid-record with a delimiter
        // pending — '3,' at the end of a line leaves its second field empty — while a delimiter that
        // merely ends a complete record before a line break, or before the end of the text, is only
        // a delimiter (all measured).
        bool numeric = conversion.Kind is not ('s' or 'q' or '[');
        while (true)
        {
            SkipWhitespace(text, cursor, options);
            if (numeric)
            {
                // A number skips the blanks in front of it whatever 'Whitespace' says, unless they are
                // delimiters: textscan('1 2 3', '%f', 'Whitespace', '') still reads three (measured).
                while (cursor.At < text.Length && text[cursor.At] is ' ' or '\t'
                       && !AtDelimiter(text, cursor.At, options, out _))
                {
                    cursor.At++;
                }
            }

            if (cursor.At >= text.Length)
            {
                cursor.PendingDelimiter = false;
                return Outcome.End;
            }

            if (AtEndOfLine(text, cursor.At, options, out int eol))
            {
                if (cursor.PendingDelimiter && !firstInRecord)
                {
                    cursor.PendingDelimiter = false;
                    return Outcome.Empty;
                }

                cursor.PendingDelimiter = false;
                cursor.At += eol;
                continue;
            }

            if (AtDelimiter(text, cursor.At, options, out int length))
            {
                ConsumeDelimiter(text, cursor, options, length);
                if (options.MultipleDelimsAsOne)
                {
                    continue;
                }

                return Outcome.Empty;
            }

            break;
        }

        int start = cursor.At;
        int limit = conversion.Width > 0 ? Math.Min(text.Length, cursor.At + conversion.Width) : text.Length;
        Outcome outcome;
        switch (conversion.Kind)
        {
            case 'q' when text[cursor.At] == '"':
            {
                cursor.At++;
                var quoted = new StringBuilder();
                while (cursor.At < text.Length && text[cursor.At] != '"')
                {
                    quoted.Append(text[cursor.At++]);
                }

                if (cursor.At < text.Length)
                {
                    cursor.At++; // the closing quote
                }

                word = quoted.ToString();
                outcome = Outcome.Value;
                break;
            }

            case 's' or 'q':
            {
                // With delimiters named, a text field runs to the next delimiter or line break and
                // loses the whitespace at its ends; without them, whitespace ends it.
                bool untilDelimiter = options.Delimiters.Count > 0;
                while (cursor.At < limit
                       && !AtEndOfLine(text, cursor.At, options, out _)
                       && !AtDelimiter(text, cursor.At, options, out _)
                       && (untilDelimiter || !IsWhitespace(text[cursor.At], options)))
                {
                    cursor.At++;
                }

                word = text[start..cursor.At];
                int trimStart = 0;
                int trimEnd = word.Length;
                while (trimEnd > trimStart && (IsWhitespace(word[trimEnd - 1], options) || word[trimEnd - 1] is ' ' or '\t'))
                {
                    trimEnd--;
                }

                while (trimStart < trimEnd && (IsWhitespace(word[trimStart], options) || word[trimStart] is ' ' or '\t'))
                {
                    trimStart++;
                }

                word = word[trimStart..trimEnd];
                outcome = Outcome.Value;
                break;
            }

            case '[':
            {
                while (cursor.At < limit && conversion.Set.Contains(text[cursor.At]) != conversion.Negated)
                {
                    cursor.At++;
                }

                word = text[start..cursor.At];
                outcome = word.Length > 0 ? Outcome.Value : Outcome.Mismatch;
                break;
            }

            default:
            {
                if (TryReadNumber(text, ref cursor.At, limit, conversion.Precision, out number))
                {
                    // The number ends where its digits end; whatever follows is the next field's
                    // problem, which is how '1,5 2' answers {1} and '0x1F 2' answers {0} (measured).
                    outcome = Outcome.Value;
                }
                else if (TreatedAsEmpty(text, cursor, options))
                {
                    outcome = Outcome.Empty;
                }
                else
                {
                    cursor.At = start;
                    outcome = Outcome.Mismatch;
                }

                break;
            }
        }

        if (outcome == Outcome.Mismatch)
        {
            return outcome;
        }

        // Past the field: whitespace, then one delimiter if there is one.
        cursor.PendingDelimiter = false;
        int after = cursor.At;
        SkipWhitespace(text, cursor, options);
        if (cursor.At < text.Length && AtDelimiter(text, cursor.At, options, out int trailing))
        {
            ConsumeDelimiter(text, cursor, options, trailing);
        }
        else
        {
            cursor.At = after;
        }

        return outcome;
    }

    /// <summary>Whether the text at the cursor is a word <c>'TreatAsEmpty'</c> names, and consumes it.</summary>
    private static bool TreatedAsEmpty(string text, Cursor cursor, Options options)
    {
        foreach (string empty in options.TreatAsEmpty)
        {
            if (empty.Length == 0 || cursor.At + empty.Length > text.Length
                || string.CompareOrdinal(text, cursor.At, empty, 0, empty.Length) != 0)
            {
                continue;
            }

            int end = cursor.At + empty.Length;
            if (end == text.Length || IsWhitespace(text[end], options)
                || AtEndOfLine(text, end, options, out _) || AtDelimiter(text, end, options, out _))
            {
                cursor.At = end;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a number: a sign, digits with a point and an exponent, or Inf or NaN in any case. A
    /// precision of 0 or more caps the digits after the point, so '%.2f' reads 1.234 as 1.23 then 4.
    /// </summary>
    private static bool TryReadNumber(string text, ref int at, int limit, int precision, out double number)
    {
        number = 0;
        int start = at;
        int i = at;
        bool negative = false;
        if (i < limit && (text[i] == '-' || text[i] == '+'))
        {
            negative = text[i] == '-';
            i++;
        }

        if (i + 3 <= limit && string.Compare(text, i, "inf", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
        {
            at = i + 3;
            number = negative ? double.NegativeInfinity : double.PositiveInfinity;
            return true;
        }

        if (i + 3 <= limit && string.Compare(text, i, "nan", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
        {
            at = i + 3;
            number = double.NaN;
            return true;
        }

        int digits = 0;
        while (i < limit && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < limit && text[i] == '.')
        {
            i++;
            int fraction = 0;
            while (i < limit && char.IsAsciiDigit(text[i]) && (precision < 0 || fraction < precision))
            {
                i++;
                digits++;
                fraction++;
            }
        }

        if (digits == 0)
        {
            at = start;
            return false;
        }

        if (i < limit && (text[i] == 'e' || text[i] == 'E'))
        {
            int save = i;
            i++;
            if (i < limit && (text[i] == '-' || text[i] == '+'))
            {
                i++;
            }

            if (i < limit && char.IsAsciiDigit(text[i]))
            {
                while (i < limit && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }
            }
            else
            {
                i = save;
            }
        }

        number = double.Parse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture);
        at = i;
        return true;
    }

    private static string ConversionName(Conversion conversion) => conversion.Kind switch
    {
        'd' or 'u' => "Integer",
        's' or 'q' => "String",
        'c' => "Character",
        '[' => "Character set",
        _ => "Numeric",
    };

    // --- the format -----------------------------------------------------------------------------------

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

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                literal.Append('%');
                i++;
                continue;
            }

            if (literal.Length > 0)
            {
                pieces.Add(new Piece(null, literal.ToString()));
                literal.Clear();
            }

            i++;
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

            int precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                // A precision caps the digits read after the point (measured: '%.2f' on '1.234'
                // answers 1.23 and then 4).
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
                pieces.Add(new Piece(
                    new Conversion('[', width, precision, skipped, negated ? set[1..] : set, negated, JgsNumericClass.Double),
                    string.Empty));
                i = close;
                continue;
            }

            if (kind is not ('d' or 'u' or 'f' or 'n' or 'g' or 'e' or 's' or 'q' or 'c'))
            {
                throw new JgsRuntimeException(line, col,
                    $"textscan does not support the conversion '%{kind}'. It reads " +
                    "%d, %u, %f, %n, %g, %e, %s, %q, %c and %[...].");
            }

            // The bit width after an integer or floating conversion names its class: %d8 is int8,
            // %u16 is uint16, %f32 is single. %d alone is int32 and %u alone uint32.
            JgsNumericClass numericClass = kind switch
            {
                'd' => JgsNumericClass.Int32,
                'u' => JgsNumericClass.UInt32,
                _ => JgsNumericClass.Double,
            };
            if (kind is 'd' or 'u' or 'f')
            {
                int bitsStart = i + 1;
                int bitsEnd = bitsStart;
                while (bitsEnd < format.Length && char.IsAsciiDigit(format[bitsEnd]))
                {
                    bitsEnd++;
                }

                string bits = format[bitsStart..bitsEnd];
                JgsNumericClass? named = (kind, bits) switch
                {
                    ('d', "8") => JgsNumericClass.Int8,
                    ('d', "16") => JgsNumericClass.Int16,
                    ('d', "32") => JgsNumericClass.Int32,
                    ('d', "64") => JgsNumericClass.Int64,
                    ('u', "8") => JgsNumericClass.UInt8,
                    ('u', "16") => JgsNumericClass.UInt16,
                    ('u', "32") => JgsNumericClass.UInt32,
                    ('u', "64") => JgsNumericClass.UInt64,
                    ('f', "32") => JgsNumericClass.Single,
                    ('f', "64") => JgsNumericClass.Double,
                    _ => null,
                };
                if (named is { } chosen)
                {
                    numericClass = chosen;
                    i = bitsEnd - 1;
                }
            }

            pieces.Add(new Piece(new Conversion(kind, width, precision, skipped, string.Empty, false, numericClass), string.Empty));
        }

        if (literal.Length > 0)
        {
            pieces.Add(new Piece(null, literal.ToString()));
        }

        return pieces;
    }
}
