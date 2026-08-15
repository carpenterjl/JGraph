using System.Globalization;
using System.Text;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Writing data back out (M65): <c>writematrix</c>, <c>writecell</c>, <c>writetable</c> and
/// <c>writelines</c>, with <c>readmatrix</c> and <c>readcell</c> to read the same files back.
/// </summary>
/// <remarks>
/// Reading has been possible since M10 and writing was not possible at all, which made the whole
/// environment a one-way door: a script could load a measurement file, work on it, plot it, and then
/// had nowhere to put the answer but the console.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec WriteTextOptions = new(
        "writematrix",
        Flags: [],
        Names: ["Delimiter", "WriteMode", "WriteVariableNames", "QuoteStrings"]);

    private static readonly OptionSpec ReadTextOptions = new(
        "readmatrix",
        Flags: [],
        Names: ["Delimiter", "NumHeaderLines", "Range"]);

    private static void RegisterDataOutBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, JGraphScriptGlobals host)
    {
        Define("writematrix", (args, line, col) =>
        {
            (string path, ParsedArgs parsed) = WriteTarget("writematrix", args, host, line, col);
            WriteRows("writematrix", path, MatrixText("writematrix", args[0], line, col), parsed, line, col);
            return JgsValue.Null;
        });

        Define("writecell", (args, line, col) =>
        {
            (string path, ParsedArgs parsed) = WriteTarget("writecell", args, host, line, col);
            JgsValue data = args[0];
            if (data.Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col,
                    $"writecell expects a cell array, but got a {data.TypeName}.");
            }

            WriteRows("writecell", path, CellText(data), parsed, line, col);
            return JgsValue.Null;
        });

        Define("writetable", (args, line, col) =>
        {
            (string path, ParsedArgs parsed) = WriteTarget("writetable", args, host, line, col);
            JgsValue data = args[0];
            if (data.Type != JgsType.Table)
            {
                throw new JgsRuntimeException(line, col,
                    $"writetable expects a table, but got a {data.TypeName}.");
            }

            Table table = data.AsTable;
            bool header = parsed.Flag("WriteVariableNames", true);
            var rows = new List<string[]>(table.RowCount + 1);
            if (header)
            {
                rows.Add([.. table.ColumnNames]);
            }

            for (int r = 0; r < table.RowCount; r++)
            {
                var cells = new string[table.ColumnCount];
                for (int c = 0; c < table.ColumnCount; c++)
                {
                    cells[c] = table.Columns[c].GetText(r);
                }

                rows.Add(cells);
            }

            WriteRows("writetable", path, rows, parsed, line, col);
            return JgsValue.Null;
        });

        Define("writelines", (args, line, col) =>
        {
            ArityRange("writelines", args, 2, 2, line, col);
            string path = host.ResolveForWrite(Str("writelines", args, 1, line, col));
            var lines = new List<string>();
            foreach (JgsValue piece in TextPieces(args[0]))
            {
                lines.Add(piece.Type == JgsType.String ? piece.AsString : piece.Display());
            }

            Attempt("writelines", line, col, () => File.WriteAllLines(path, lines));
            return JgsValue.Null;
        });

        Define("readlines", (args, line, col) =>
        {
            ArityRange("readlines", args, 1, 1, line, col);
            string path = host.Resolve(Str("readlines", args, 0, line, col));
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new JgsRuntimeException(line, col, $"readlines: {ex.Message}");
            }


            // A column of string, the shape MATLAB gives it, and the shape that makes
            // writelines(readlines(f), g) a copy rather than a transpose.
            var elements = new JgsValue[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                elements[i] = JgsValue.Str(lines[i]);
            }

            return JgsValue.StringArray(elements, lines.Length, lines.Length == 0 ? 0 : 1);
        });

        Define("readmatrix", (args, line, col) =>
        {
            List<string[]> rows = ReadRows("readmatrix", args, host, line, col);
            int width = 0;
            foreach (string[] row in rows)
            {
                width = System.Math.Max(width, row.Length);
            }

            if (rows.Count == 0 || width == 0)
            {
                return JgsValue.Array([]);
            }

            // Anything that is not a number is NaN — which is what MATLAB does with a stray word in
            // a numeric file rather than refusing the whole read.
            return JgsMatrix.Build(rows.Count, width, (r, c) =>
            {
                string cell = c < rows[r].Length ? rows[r][c] : string.Empty;
                return double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : double.NaN;
            });
        });

        Define("readcell", (args, line, col) =>
        {
            List<string[]> rows = ReadRows("readcell", args, host, line, col);
            int width = 0;
            foreach (string[] row in rows)
            {
                width = System.Math.Max(width, row.Length);
            }

            if (rows.Count == 0 || width == 0)
            {
                return JgsValue.Cell([]);
            }

            var cells = new JgsValue[rows.Count * width];
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    string cell = c < rows[r].Length ? rows[r][c] : string.Empty;

                    // A cell that reads as a number comes back as one and everything else as text,
                    // which is the whole reason readcell exists beside readmatrix.
                    cells[r + (c * rows.Count)] =
                        double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                            ? JgsValue.Number(parsed)
                            : JgsValue.Str(cell);
                }
            }

            JgsValue result = JgsValue.Cell(cells);
            result.Reshape(rows.Count, width);
            return result;
        });

        // The legacy spellings, both documented by MATLAB as superseded by writematrix.
        Define("csvwrite", (args, line, col) =>
        {
            ArityRange("csvwrite", args, 2, 2, line, col);
            string path = host.ResolveForWrite(Str("csvwrite", args, 0, line, col));
            WriteDelimited("csvwrite", path, MatrixText("csvwrite", args[1], line, col), ",", append: false, line, col);
            return JgsValue.Null;
        });

        Define("dlmwrite", (args, line, col) =>
        {
            ArityRange("dlmwrite", args, 2, 3, line, col);
            string path = host.ResolveForWrite(Str("dlmwrite", args, 0, line, col));
            string delimiter = args.Count > 2 ? Str("dlmwrite", args, 2, line, col) : ",";
            WriteDelimited("dlmwrite", path, MatrixText("dlmwrite", args[1], line, col), delimiter, append: false, line, col);
            return JgsValue.Null;
        });

        Define("struct2table", (args, line, col) =>
        {
            ArityRange("struct2table", args, 1, 1, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col,
                    $"struct2table expects a struct array, but got a {args[0].TypeName}.");
            }

            JgsStructArray payload = args[0].AsStructArray;
            var columns = new List<TableColumn>();
            foreach (string field in payload.FieldNames)
            {
                bool numeric = true;
                for (int i = 0; i < payload.Length; i++)
                {
                    numeric &= payload.Elements[i][field].Type is JgsType.Number or JgsType.Bool;
                }

                if (numeric)
                {
                    var numbers = new double[payload.Length];
                    for (int i = 0; i < numbers.Length; i++)
                    {
                        numbers[i] = payload.Elements[i][field].AsNumber;
                    }

                    columns.Add(new NumberColumn(field, numbers));
                }
                else
                {
                    var text = new string[payload.Length];
                    for (int i = 0; i < text.Length; i++)
                    {
                        JgsValue held = payload.Elements[i][field];
                        text[i] = held.Type == JgsType.String ? held.AsString : held.Display();
                    }

                    columns.Add(new TextColumn(field, text));
                }
            }

            return JgsValue.Table(new Table(columns));
        });

        Define("table2struct", (args, line, col) =>
        {
            ArityRange("table2struct", args, 1, 1, line, col);
            Table table = Tbl("table2struct", args, 0, line, col);
            var elements = new Dictionary<string, JgsValue>[table.RowCount];
            for (int r = 0; r < table.RowCount; r++)
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (TableColumn column in table.Columns)
                {
                    fields[column.Name] = column.Type == ColumnType.Text
                        ? JgsValue.Str(column.GetText(r))
                        : JgsValue.Number(column.GetNumber(r));
                }

                elements[r] = fields;
            }

            // A column, one element per row — the shape MATLAB's answer has.
            return JgsValue.StructArray(
                new JgsStructArray(elements, [.. table.ColumnNames]),
                table.RowCount, table.RowCount == 0 ? 0 : 1);
        });
    }

    /// <summary>The resolved output path and the parsed option tail the write verbs share.</summary>
    private static (string Path, ParsedArgs Parsed) WriteTarget(
        string name, IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs the data and a file name.");
        }

        // The file name is a string and so would read as an option name; the two leading arguments
        // are taken by position and only what follows them is parsed as options.
        ParsedArgs parsed = WriteTextOptions.Parse(OptionTail(args,2), positionalMax: 0, line, col);
        string given = Str(name, args, 1, line, col);

        // Spreadsheet output would need a workbook writer, and the one piece of xlsx machinery here
        // reads. Refusing by name beats writing a .xlsx that is really a comma-separated file.
        if (given.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || given.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} writes delimited text, not spreadsheets — use a .csv or .txt name.");
        }

        return (host.ResolveForWrite(given), parsed);
    }

    /// <summary>The arguments after the leading positional ones, for the option parser.</summary>
    private static List<JgsValue> OptionTail(IReadOnlyList<JgsValue> args, int from)
    {
        var tail = new List<JgsValue>(System.Math.Max(0, args.Count - from));
        for (int i = from; i < args.Count; i++)
        {
            tail.Add(args[i]);
        }

        return tail;
    }

    /// <summary>Writes the rows, honouring 'Delimiter' and 'WriteMode'.</summary>
    private static void WriteRows(
        string name, string path, List<string[]> rows, ParsedArgs parsed, int line, int col)
    {
        string delimiter = parsed.Named("Delimiter") is { Type: JgsType.String } given
            ? NamedDelimiter(given.AsString)
            : path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "\t" : ",";

        bool append = parsed.Named("WriteMode") is { Type: JgsType.String } mode
            && mode.AsString.Equals("append", StringComparison.OrdinalIgnoreCase);

        WriteDelimited(name, path, rows, delimiter, append, line, col);
    }

    /// <summary>MATLAB's delimiter words, so 'tab' and 'comma' mean what they say.</summary>
    private static string NamedDelimiter(string given) => given switch
    {
        "tab" => "\t",
        "comma" => ",",
        "space" => " ",
        "semi" => ";",
        "bar" => "|",
        _ => given,
    };

    private static void WriteDelimited(
        string name, string path, List<string[]> rows, string delimiter, bool append, int line, int col)
    {
        var text = new StringBuilder();
        foreach (string[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(delimiter);
                }

                text.Append(Quoted(row[i], delimiter));
            }

            text.Append('\n');
        }

        Attempt(name, line, col, () =>
        {
            if (append)
            {
                File.AppendAllText(path, text.ToString());
            }
            else
            {
                File.WriteAllText(path, text.ToString());
            }
        });
    }

    /// <summary>Quotes a cell that would otherwise break the row apart.</summary>
    private static string Quoted(string cell, string delimiter) =>
        cell.Contains(delimiter, StringComparison.Ordinal)
            || cell.Contains('"', StringComparison.Ordinal)
            || cell.Contains('\n', StringComparison.Ordinal)
            ? '"' + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : cell;

    /// <summary>A numeric (or logical) matrix as rows of text, one string per element.</summary>
    private static List<string[]> MatrixText(string name, JgsValue data, int line, int col)
    {
        if (data.Type is JgsType.Number or JgsType.Bool or JgsType.String)
        {
            return [[data.Type == JgsType.String ? data.AsString : JgsNumberFormat.Format(data.AsNumber)]];
        }

        if (data.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects a matrix, but got a {data.TypeName}.");
        }

        int rows = JgsMatrix.RowCount(data);
        int cols = JgsMatrix.ColCount(data);
        var text = new List<string[]>(rows);
        for (int r = 0; r < rows; r++)
        {
            var row = new string[cols];
            for (int c = 0; c < cols; c++)
            {
                JgsValue element = JgsMatrix.At(data, r, c);
                row[c] = element.Type == JgsType.String ? element.AsString : JgsNumberFormat.Format(element.AsNumber);
            }

            text.Add(row);
        }

        return text;
    }

    /// <summary>A cell array as rows of text, each element written the way it displays.</summary>
    private static List<string[]> CellText(JgsValue data)
    {
        JgsValue[] elements = data.AsCell;
        int rows = data.Rows;
        int cols = data.Cols;
        var text = new List<string[]>(rows);
        for (int r = 0; r < rows; r++)
        {
            var row = new string[cols];
            for (int c = 0; c < cols; c++)
            {
                JgsValue element = elements[r + (c * rows)];
                row[c] = element.Type == JgsType.String ? element.AsString
                    : element.Type is JgsType.Number or JgsType.Bool ? JgsNumberFormat.Format(element.AsNumber)
                    : element.Display();
            }

            text.Add(row);
        }

        return text;
    }

    /// <summary>Reads a delimited file into rows of raw cells, honouring the read options.</summary>
    private static List<string[]> ReadRows(
        string name, IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a file name.");
        }

        ParsedArgs parsed = ReadTextOptions.Parse(OptionTail(args,1), positionalMax: 0, line, col);
        string path = host.Resolve(Str(name, args, 0, line, col));
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        int skip = parsed.Named("NumHeaderLines") is { } header
            ? (int)header.AsNumber
            : 0;

        string? delimiter = parsed.Named("Delimiter") is { Type: JgsType.String } given
            ? NamedDelimiter(given.AsString)
            : null;

        var rows = new List<string[]>(lines.Length);
        for (int i = skip; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            // With no delimiter named, the one that splits this line into the most pieces wins —
            // the same guess readtable makes, and the reason readmatrix('x.tsv') needs no options.
            string separator = delimiter ?? GuessDelimiter(lines[i]);
            rows.Add(SplitRow(lines[i], separator));
        }

        return rows;
    }

    private static string GuessDelimiter(string line)
    {
        string best = ",";
        int most = 0;
        foreach (string candidate in new[] { ",", "\t", ";", "|" })
        {
            int count = SplitRow(line, candidate).Length;
            if (count > most)
            {
                most = count;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Splits one row, honouring double quotes so a quoted cell may hold the delimiter.</summary>
    private static string[] SplitRow(string line, string delimiter)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }

                continue;
            }

            if (ch == '"' && cell.Length == 0)
            {
                quoted = true;
            }
            else if (string.CompareOrdinal(line, i, delimiter, 0, delimiter.Length) == 0)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                i += delimiter.Length - 1;
            }
            else
            {
                cell.Append(ch);
            }
        }

        cells.Add(cell.ToString().Trim());
        return [.. cells];
    }

    /// <summary>The lines a text value stands for: a char row is one, a cell or string array is many.</summary>
    private static IReadOnlyList<JgsValue> TextPieces(JgsValue value) => value.Type switch
    {
        JgsType.Cell => value.AsCell,
        JgsType.Array => value.BoxedElements(),
        _ => [value],
    };
}
