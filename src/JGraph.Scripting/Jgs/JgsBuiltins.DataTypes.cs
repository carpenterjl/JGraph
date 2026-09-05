using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The data-type builtins the stress tests asked for (M43): the <c>table</c>/<c>timetable</c>
/// constructors over the existing <see cref="Table"/>, <c>categorical</c> and <c>summary</c>,
/// string/cell conversions (<c>string</c>, <c>cellstr</c>, <c>compose</c>), <c>missing</c> with
/// <c>ismissing</c>, and <c>seconds</c>. Documented divergences, all recorded in the coverage doc:
/// a categorical is a cell of category names, a duration is its number of seconds, and a missing
/// string is the sentinel <c>&lt;missing&gt;</c> — the shapes scripts actually consume, without a
/// tagged-type system the object model does not have.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The stand-in for MATLAB's missing value inside string arrays.</summary>
    internal const string MissingSentinel = "<missing>";

    /// <summary>Registers the data-type builtins into <paramref name="env"/>.</summary>
    private static void RegisterDataTypeBuiltins(JgsEnvironment env, JgsDialect? dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // missing is a value, not a function: ["apple", missing, "banana"] must just evaluate.
        env.Declare("missing", JgsValue.Str(MissingSentinel));

        Define("ismissing", (args, line, col) =>
        {
            Arity("ismissing", args, 1, line, col);
            if (args[0].Type == JgsType.String)
            {
                return JgsValue.Bool(args[0].AsString == MissingSentinel);
            }

            // A string array answers elementwise (M63), which is what makes ismissing useful on one:
            // the whole point of a missing string is that it sits among strings that are not.
            if (args[0].IsStringArray)
            {
                JgsValue[] texts = args[0].BoxedElements();
                var flags = new JgsValue[texts.Length];
                for (int i = 0; i < texts.Length; i++)
                {
                    flags[i] = JgsValue.Bool(texts[i].AsString == MissingSentinel);
                }

                JgsValue answer = flags.Length == 1 ? flags[0] : JgsValue.Array(flags);
                if (flags.Length > 1)
                {
                    answer.TakeShapeOf(args[0]);
                }

                return answer;
            }

            return MapToBool("ismissing", args[0], double.IsNaN, line, col);
        });

        // string(x) is the string-array constructor (M63), and the only way to get one out of a value
        // that was not written with double quotes. A char row becomes one string, not one per
        // character: that a piece of text is a single element is the whole point of the type.
        Define("string", (args, line, col) =>
        {
            Arity("string", args, 1, line, col);
            JgsValue input = args[0];
            if (input.IsStringArray)
            {
                return input;
            }

            // A time answers with how it displays, not with its milliseconds (M64). Asked first,
            // because a datetime is an array underneath and the Array arm below would otherwise turn
            // each moment into the number it is stored as.
            if (input.IsTime)
            {
                var texts = new JgsValue[input.ArrayLength];
                for (int i = 0; i < texts.Length; i++)
                {
                    texts[i] = JgsValue.Str(TimeText(input, i));
                }

                return ShapedLike(input, texts).MarkStringArray();
            }

            return input.Type switch
            {
                JgsType.String => JgsValue.StringScalar(input.AsString),
                JgsType.Cell => ShapedLike(input, Array.ConvertAll(input.AsCell, StringOf)).MarkStringArray(),
                JgsType.Array => ShapedLike(input, Array.ConvertAll(input.BoxedElements(), StringOf)).MarkStringArray(),
                _ => JgsValue.StringScalar(input.Display()),
            };
        });

        Define("cellstr", (args, line, col) =>
        {
            Arity("cellstr", args, 1, line, col);
            JgsValue input = args[0];

            // A char matrix is its rows, one cell each, with the padding taken back off (M105) —
            // MATLAB deblanks here, which is exactly what makes cellstr the usual way back out of a
            // char matrix. The Array arm below would otherwise have read its code points as numbers.
            if (input.IsCharMatrix)
            {
                string[] rows = input.CharMatrixRows();
                JgsValue answer = JgsValue.Cell(Array.ConvertAll(rows, static r => JgsValue.Str(r.TrimEnd(' '))));
                answer.Reshape(rows.Length, 1);
                return answer;
            }

            return input.Type switch
            {
                JgsType.Cell => input,
                JgsType.String => JgsValue.Cell([input]),
                JgsType.Array => JgsValue.Cell(Array.ConvertAll(input.BoxedElements(), StringOf)),
                _ => throw new JgsRuntimeException(line, col,
                    $"cellstr expects a string array or cell, but got a {input.TypeName}."),
            };
        });

        // compose(format, A1, ..., An): one answer per row, each row's values taken from every data
        // argument in turn and formatted under sprintf's MATLAB rules — so the format cycles across a
        // row with more values than specifiers, and compose('%d-%d', [1 2; 3 4]) is {'1-2'; '3-4'}.
        // The answer takes the format's kind, as MATLAB's does: a string format makes a string
        // array, a char one a cell of char rows. A call with no data is the format itself, escapes
        // decoded and specifiers left alone.
        env.Declare("compose", JgsValue.Function(new BuiltinFunction("compose", (args, line, col) =>
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
                string[] plain = Array.ConvertAll(formats, static f => f.Replace("%%", "%", StringComparison.Ordinal));
                return Answer(plain, args[0].Type == JgsType.Array ? args[0].Rows : 1,
                    args[0].Type == JgsType.Array ? args[0].Cols : 1);
            }

            static bool IsRowData(JgsValue data) =>
                data.Type is JgsType.Array or JgsType.Cell && !data.IsCharMatrix;

            int dataRows = 1;
            for (int a = 1; a < args.Count; a++)
            {
                int given = IsRowData(args[a]) ? args[a].Rows : 1;
                if (given != 1 && dataRows != 1 && given != dataRows)
                {
                    throw new JgsRuntimeException(line, col,
                        $"compose expects every data argument to have the same number of rows, but got {dataRows} and {given}.");
                }

                dataRows = given == 1 ? dataRows : given;
            }

            if (formats.Length > 1 && dataRows > 1)
            {
                throw new JgsRuntimeException(line, col,
                    "compose takes several formats only when the data has one row.");
            }

            var values = new List<JgsValue>();
            string RowText(string format, int r)
            {
                values.Clear();
                for (int a = 1; a < args.Count; a++)
                {
                    JgsValue data = args[a];
                    if (!IsRowData(data))
                    {
                        values.Add(data);
                        continue;
                    }

                    int row = data.Rows == 1 ? 0 : r; // a one-row argument is repeated for every row
                    JgsValue[] cells = data.Type == JgsType.Cell ? data.AsCell : [];
                    for (int c = 0; c < data.Cols; c++)
                    {
                        int at = data.LinearIndex(row, c);
                        values.Add(data.Type == JgsType.Cell ? cells[at] : data.ElementAt(at));
                    }
                }

                return JgsSprintf.FormatMatlab(format, values);
            }

            try
            {
                if (formats.Length > 1)
                {
                    string[] each = Array.ConvertAll(formats, f => RowText(f, 0));
                    return Answer(each, args[0].Rows, args[0].Cols);
                }

                var texts = new string[dataRows];
                for (int r = 0; r < dataRows; r++)
                {
                    texts[r] = RowText(formats[0], r);
                }

                return Answer(texts, dataRows, 1);
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        })
        { KeepsStringArguments = true }));

        // A categorical is its cell of category names; class() will say cell, and summary counts.
        Define("categorical", (args, line, col) =>
        {
            Arity("categorical", args, 1, line, col);
            JgsValue input = args[0];
            return input.Type switch
            {
                JgsType.Cell => JgsValue.Cell(Array.ConvertAll(input.AsCell, StringOf)),
                JgsType.Array => JgsValue.Cell(Array.ConvertAll(input.BoxedElements(), StringOf)),
                JgsType.String => JgsValue.Cell([input]),
                _ => throw new JgsRuntimeException(line, col,
                    $"categorical expects a cell or array, but got a {input.TypeName}."),
            };
        });

        Define("summary", (args, line, col) =>
        {
            Arity("summary", args, 1, line, col);
            if (args[0].Type == JgsType.Table)
            {
                return TableSummary(args[0].AsTable);
            }

            if (args[0].Type is JgsType.Cell or JgsType.Array)
            {
                // Category counts, in first-appearance order — what summary(categorical) reports.
                JgsValue[] elements = args[0].Type == JgsType.Cell ? args[0].AsCell : args[0].BoxedElements();
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (JgsValue element in elements)
                {
                    string category = StringOf(element).AsString;
                    if (counts.TryGetValue(category, out int soFar))
                    {
                        counts[category] = soFar + 1;
                    }
                    else
                    {
                        counts[category] = 1;
                        order.Add(category);
                    }
                }

                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (string category in order)
                {
                    fields[category] = JgsValue.Number(counts[category]);
                }

                return JgsValue.Struct(fields);
            }

            throw new JgsRuntimeException(line, col,
                $"summary expects a table or categorical, but got a {args[0].TypeName}.");
        });

        Define("table", (args, line, col) => BuildTable("table", args, timeColumn: null, line, col));

        // seconds used to live here, answering with its own argument because a duration was its count
        // of seconds and nothing more. M64 gives it a real type, and RegisterTimeBuiltins declares it.
        Define("timetable", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "timetable expects row times and at least one variable.");
            }

            // A duration row-time column is stored as its count of seconds, and a datetime one as its
            // serial date number (M64). A Table column holds doubles, so the row times have to be
            // some number; these are the two a script goes on to plot or compare against, and they
            // are the readings timetable's row times had before the types existed.
            double[] times = args[0].IsDuration
                ? System.Array.ConvertAll(TimeMs(args[0]), static ms => ms / JgsTime.MsPerSecond)
                : args[0].IsDatetime
                    ? System.Array.ConvertAll(TimeMs(args[0]), JgsTime.ToDatenum)
                    : ToDoubles("timetable", args[0], line, col);

            return BuildTable("timetable", args.Skip(1).ToArray(), new NumberColumn("Time", times), line, col);
        });
    }

    /// <summary>
    /// Builds a <see cref="Table"/> from column arguments plus an optional trailing
    /// <c>'VariableNames', {…}</c> pair; unnamed columns become Var1…VarN.
    /// </summary>
    private static JgsValue BuildTable(string name, IReadOnlyList<JgsValue> args, TableColumn? timeColumn, int line, int col)
    {
        string[]? names = null;
        int columnCount = args.Count;
        if (columnCount >= 2 && args[columnCount - 2].Type == JgsType.String
            && args[columnCount - 2].AsString == "VariableNames")
        {
            JgsValue nameList = args[columnCount - 1];
            JgsValue[] nameValues = nameList.Type == JgsType.Cell ? nameList.AsCell
                : nameList.Type == JgsType.Array ? nameList.BoxedElements()
                : throw new JgsRuntimeException(line, col, $"{name}: 'VariableNames' takes a cell of names.");
            names = Array.ConvertAll(nameValues, v => StringOf(v).AsString);
            columnCount -= 2;
        }

        if (columnCount == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one variable.");
        }

        if (names is not null && names.Length != columnCount)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {columnCount} variables but {names.Length} names.");
        }

        var columns = new List<TableColumn>();
        if (timeColumn is not null)
        {
            columns.Add(timeColumn);
        }

        for (int i = 0; i < columnCount; i++)
        {
            string columnName = names is not null ? names[i] : $"Var{i + 1}";
            JgsValue value = args[i];
            if (value.Type == JgsType.Cell || (value.Type == JgsType.Array && HasStringElements(value)))
            {
                JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
                columns.Add(new TextColumn(columnName, Array.ConvertAll(elements, v => (string?)StringOf(v).AsString)));
            }
            else
            {
                columns.Add(new NumberColumn(columnName, ToDoubles(name, value, line, col)));
            }
        }

        int rows = columns[0].RowCount;
        foreach (TableColumn column in columns)
        {
            if (column.RowCount != rows)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: every variable needs the same number of rows ({column.Name} has {column.RowCount}, expected {rows}).");
            }
        }

        return JgsValue.Table(new Table(columns));
    }

    /// <summary>
    /// A table column as a script value — what <c>T.Code</c> reads: numeric columns come back as
    /// column vectors, text columns as cells of char (so <c>T.Code{2}</c> braces in).
    /// </summary>
    internal static JgsValue TableColumnValue(Table table, string columnName, int line, int col)
    {
        if (!table.TryGetColumn(columnName, out TableColumn column))
        {
            throw new JgsRuntimeException(line, col,
                $"The table has no variable '{columnName}'. Its variables are: {string.Join(", ", table.ColumnNames)}.");
        }

        if (column.Type == ColumnType.Text)
        {
            var cells = new JgsValue[column.RowCount];
            for (int r = 0; r < column.RowCount; r++)
            {
                cells[r] = JgsValue.Str(column.GetText(r));
            }

            JgsValue cell = JgsValue.Cell(cells);
            cell.Reshape(cells.Length, 1);
            return cell;
        }

        var values = new double[column.RowCount];
        for (int r = 0; r < column.RowCount; r++)
        {
            values[r] = column.GetNumber(r);
        }

        return JgsMatrix.FromColumnMajorDims(values, [values.Length, 1]);
    }

    /// <summary>Per-variable min/max/mean (numeric) or size/type (text) — <c>summary(T)</c>.</summary>
    private static JgsValue TableSummary(Table table)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (TableColumn column in table.Columns)
        {
            var info = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Size"] = JgsValue.Array([JgsValue.Number(column.RowCount), JgsValue.Number(1)]),
            };
            if (column.Type == ColumnType.Text)
            {
                info["Type"] = JgsValue.Str("cell");
            }
            else
            {
                info["Type"] = JgsValue.Str("double");
                double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0;
                int counted = 0;
                for (int r = 0; r < column.RowCount; r++)
                {
                    double x = column.GetNumber(r);
                    if (double.IsNaN(x))
                    {
                        continue;
                    }

                    min = System.Math.Min(min, x);
                    max = System.Math.Max(max, x);
                    sum += x;
                    counted++;
                }

                if (counted > 0)
                {
                    info["Min"] = JgsValue.Number(min);
                    info["Max"] = JgsValue.Number(max);
                    info["Mean"] = JgsValue.Number(sum / counted);
                }
            }

            fields[column.Name] = JgsValue.Struct(info);
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>Whether a boxed array holds any string element (a string array, for table purposes).</summary>
    private static bool HasStringElements(JgsValue value)
    {
        if (value.IsPacked)
        {
            return false; // packed storage is numbers by construction
        }

        foreach (JgsValue element in value.BoxedElements())
        {
            if (element.Type == JgsType.String)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One element as a string value, through Display for non-strings.</summary>
    private static JgsValue StringOf(JgsValue value) =>
        value.Type == JgsType.String ? value : JgsValue.Str(value.Display());

    /// <summary>Wraps freshly built elements in the input's shape (or a plain row when unshaped).</summary>
    private static JgsValue ShapedLike(JgsValue input, JgsValue[] elements)
    {
        JgsValue result = JgsValue.Array(elements);
        if (input.Rows > 1 && input.Cols > 1)
        {
            result.Reshape(input.Rows, input.Cols);
        }
        else if (elements.Length > 1 && input.Type == JgsType.Array && input.Cols == 1 && input.Rows > 1)
        {
            result.Reshape(elements.Length, 1);
        }

        return result;
    }
}
