using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>compose(formatSpec, A1, …, An)</c> the way MATLAB R2025b answers it: one string per row of the
/// data, and when one row holds more values than the format has conversion operators, one string per
/// group of that many values — <c>compose('%d', [1 2 3])</c> is <c>{'1', '2', '3'}</c> and
/// <c>compose('%d %d', [1 2 3])</c> is <c>{'1 2', '3 %d'}</c>, the unfilled operator left as written.
/// </summary>
/// <remarks>
/// The old definition handed each whole row to <c>sprintf</c>'s MATLAB reading, which repeats the
/// format until the values run out and so answered <c>{'123'}</c> for the first example above — one
/// string where MATLAB answers three. Every rule here was measured; the ones the documentation does
/// not spell out are the shape of an empty answer (0-by-0), the refusal of a cell as data, and the
/// refusal of a string where a numeric operator wants a number.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The body of <c>compose</c>.</summary>
    private static JgsValue Composed(IReadOnlyList<JgsValue> args, JgsDialect? dialect, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "compose expects a format string first.");
        }

        bool asStrings = args[0].IsStringArray;
        string[] formats = asStrings
            ? Array.ConvertAll(args[0].BoxedElements(), static e => e.AsString)
            : [Str("compose", args, 0, line, col)];
        if (dialect?.IsMatlab == true)
        {
            // MATLAB's quotes keep '\n' as two characters; compose decodes them like sprintf.
            formats = Array.ConvertAll(formats, UnescapeFormat);
        }

        JgsValue Answer(string[] texts, int rows, int cols)
        {
            JgsValue[] boxed = Array.ConvertAll(texts, JgsValue.Str);
            if (asStrings)
            {
                return JgsValue.StringArray(boxed, rows, cols);
            }

            JgsValue cell = JgsValue.Cell(boxed);
            cell.Reshape(rows, cols);
            return cell;
        }

        if (args.Count == 1)
        {
            // No data: the format itself, escapes decoded and operators left alone.
            string[] plain = Array.ConvertAll(formats, static f => f.Replace("%%", "%", StringComparison.Ordinal));
            return Answer(plain, args[0].Type == JgsType.Array ? args[0].Rows : 1,
                args[0].Type == JgsType.Array ? args[0].Cols : 1);
        }

        // The data, one grid of values per argument.
        var grids = new (JgsValue[] Values, int Rows, int Cols)[args.Count - 1];
        int dataRows = -1;
        int totalCols = 0;
        for (int a = 1; a < args.Count; a++)
        {
            grids[a - 1] = ComposeData(args[a], line, col);
            (JgsValue[] _, int rows, int cols) = grids[a - 1];
            if (dataRows < 0)
            {
                dataRows = rows;
            }
            else if (rows != dataRows)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:ComposeInputsMustHaveSameNumberOfRows",
                    "All data arrays must have the same number of rows.");
            }

            totalCols += cols;
        }

        if (dataRows == 0 || totalCols == 0)
        {
            return Answer([], 0, 0);
        }

        if (formats.Length > 1 && dataRows > 1)
        {
            throw new JgsRuntimeException(line, col,
                "compose takes several formats only when the data has one row.");
        }

        try
        {
            if (formats.Length > 1)
            {
                // Several formats, one row of data: each format reads the row.
                string[] each = Array.ConvertAll(formats, f => ComposeRow(f, RowValues(grids, 0), line, col)[0]);
                return Answer(each, args[0].Rows, args[0].Cols);
            }

            string format = formats[0];
            int operators = SpecifierSpans(format).Count;
            if (grids.Length > 1 && operators > 0 && totalCols > operators)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:ComposeTooManyColumns",
                    "With several data arrays, the format must have an operator for every column.");
            }

            var perRow = new string[dataRows][];
            int width = 0;
            for (int r = 0; r < dataRows; r++)
            {
                perRow[r] = ComposeRow(format, RowValues(grids, r), line, col);
                width = Math.Max(width, perRow[r].Length);
            }

            var flat = new string[dataRows * width];
            for (int r = 0; r < dataRows; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    flat[r + (c * dataRows)] = c < perRow[r].Length ? perRow[r][c] : string.Empty;
                }
            }

            return Answer(flat, dataRows, width);
        }
        catch (FormatException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>One data argument as a grid of values, column-major. A cell is refused, as MATLAB refuses it.</summary>
    private static (JgsValue[] Values, int Rows, int Cols) ComposeData(JgsValue data, int line, int col)
    {
        if (data.Type == JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:string:ComposeCellInput",
                "compose does not accept a cell array as data; use a numeric, logical, char or string array.");
        }

        if (data.IsCharMatrix || data.Type == JgsType.String)
        {
            return ([data.IsCharMatrix ? JgsValue.Str(data.CharMatrixText()) : data], 1, 1);
        }

        if (data.Type == JgsType.Array)
        {
            return (data.BoxedElements(), data.Rows, data.Cols);
        }

        return ([data], 1, 1);
    }

    /// <summary>Row <paramref name="r"/> of every data grid, left to right.</summary>
    private static List<JgsValue> RowValues((JgsValue[] Values, int Rows, int Cols)[] grids, int r)
    {
        var row = new List<JgsValue>();
        foreach ((JgsValue[] values, int rows, int cols) in grids)
        {
            for (int c = 0; c < cols; c++)
            {
                row.Add(values[r + (c * rows)]);
            }
        }

        return row;
    }

    /// <summary>
    /// The strings one row of values composes: the format applied once per group of as many values as
    /// it has operators, with the operators a short last group leaves unfilled kept as written.
    /// </summary>
    private static string[] ComposeRow(string format, List<JgsValue> values, int line, int col)
    {
        List<(int Start, int End)> spans = SpecifierSpans(format);
        if (spans.Count == 0)
        {
            return [format.Replace("%%", "%", StringComparison.Ordinal)];
        }

        // A string where a numeric operator wants a number is refused: compose('%d', "5") errors.
        for (int i = 0; i < values.Count; i++)
        {
            char conversion = format[spans[i % spans.Count].End - 1];
            if (values[i].Type == JgsType.String && "diouxXfeEgG".Contains(conversion))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:ComposeNumericOperatorOnText",
                    $"compose: the operator '%{conversion}' cannot format text.");
            }
        }

        var chunks = new List<string>();
        for (int from = 0; from < values.Count; from += spans.Count)
        {
            int take = Math.Min(spans.Count, values.Count - from);
            List<JgsValue> group = values.GetRange(from, take);
            if (take == spans.Count)
            {
                chunks.Add(JgsSprintf.FormatMatlab(format, group));
                continue;
            }

            // The head through the last filled operator and its following text; the rest verbatim.
            string head = format[..spans[take].Start];
            string tail = format[spans[take].Start..].Replace("%%", "%", StringComparison.Ordinal);
            chunks.Add(JgsSprintf.FormatMatlab(head, group) + tail);
        }

        return chunks.ToArray();
    }

    /// <summary>Where each conversion operator sits in a format, <c>%%</c> excluded.</summary>
    private static List<(int Start, int End)> SpecifierSpans(string format)
    {
        var spans = new List<(int Start, int End)>();
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }

            if (i + 1 < format.Length && format[i + 1] == '%')
            {
                i++;
                continue;
            }

            int j = i + 1;
            while (j < format.Length && "-+ 0#".Contains(format[j]))
            {
                j++;
            }

            while (j < format.Length && (char.IsAsciiDigit(format[j]) || format[j] is '*' or '.' or '$'))
            {
                j++;
            }

            while (j < format.Length && format[j] is 'l' or 'h')
            {
                j++;
            }

            if (j < format.Length)
            {
                j++; // the conversion character
            }

            spans.Add((i, j));
            i = j - 1;
        }

        return spans;
    }
}
