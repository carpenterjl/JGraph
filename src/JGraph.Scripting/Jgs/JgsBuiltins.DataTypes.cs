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
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // missing is a value, not a function: ["apple", missing, "banana"] must just evaluate.
        env.Builtins.RegisterConstant("missing", JgsValue.Str(MissingSentinel));

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

            // A char matrix is one string per row, stacked the way the rows were.
            if (input.IsCharMatrix)
            {
                string[] rows = input.CharMatrixRows();
                return JgsValue.StringArray(Array.ConvertAll(rows, JgsValue.Str), rows.Length, 1);
            }

            // string([]) is the 0-by-0 string array, which ShapedLike cannot build from no elements,
            // and string({}) the same (measured).
            if (JgsEmpty.IsEmptyArray(input) || (input.Type == JgsType.Cell && input.AsCell.Length == 0))
            {
                return JgsValue.StringArray([], input.Rows, input.Cols);
            }

            return input.Type switch
            {
                JgsType.String => JgsValue.StringScalar(input.AsString),
                JgsType.Cell => ShapedLike(input, Array.ConvertAll(input.AsCell, StringElementOf)).MarkStringArray(),

                // A packed real array is read where it lies (ADR 0156): the same element for each
                // double that the boxed arm below builds, without a boxed number per element first.
                JgsType.Array when input.IsPacked => ShapedLike(input, PackedStringElements(input)).MarkStringArray(),
                JgsType.Array when HasComplexElements(input) =>
                    ShapedLike(input, Array.ConvertAll(input.BoxedElements(), static e => JgsValue.Str(ComplexText(e)))).MarkStringArray(),
                JgsType.Array => ShapedLike(input, Array.ConvertAll(input.BoxedElements(), StringElementOf)).MarkStringArray(),
                _ => JgsValue.StringScalar(StringElementOf(input).AsString),
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

        // compose(format, A1, ..., An) is declared in JgsBuiltins.Compose.cs.
        env.Builtins.Register("compose", JgsValue.Function(new BuiltinFunction("compose",
            (args, line, col) => Composed(args, dialect, line, col))
        { KeepsStringArguments = true }));

        // A categorical is its cell of category names; class() will say cell, and summary counts.
        Define("categorical", (args, line, col) =>
        {
            ArityRange("categorical", args, 1, 3, line, col);
            JgsValue input = args[0];
            JgsValue named = input.Type switch
            {
                JgsType.Cell => CellShapedLike(input, Array.ConvertAll(input.AsCell, StringOf)),
                JgsType.Array => CellShapedLike(input, Array.ConvertAll(input.BoxedElements(), StringOf)),
                JgsType.String => JgsValue.Cell([input]),
                _ => throw new JgsRuntimeException(line, col,
                    $"categorical expects a cell or array, but got a {input.TypeName}."),
            };

            return args.Count == 1 ? named : Relabelled(named, args, line, col);
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
            // V6: row times that are a duration or a datetime stay one (JgsTimeColumn), so the
            // class, the format and the exact values come back out of TT.Time and out of
            // TT.Properties.RowTimes after any rebuild; the drawing side reads the same seconds
            // and days through the column's numeric view.
            TableColumn rowTimes = args[0].IsTime
                ? new JgsTimeColumn("Time", TimeMs(args[0]), args[0].TimeTag!)
                : new NumberColumn("Time", ToDoubles("timetable", args[0], line, col));

            return BuildTable("timetable", args.Skip(1).ToArray(), rowTimes, line, col);
        });
    }

    /// <summary>
    /// Builds a <see cref="Table"/> from column arguments plus an optional trailing
    /// <c>'VariableNames', {…}</c> pair; unnamed columns become Var1…VarN.
    /// </summary>
    private static JgsValue BuildTable(string name, IReadOnlyList<JgsValue> args, TableColumn? timeColumn, int line, int col)
    {
        string[]? names = null;
        string[]? rowNames = null;
        int columnCount = args.Count;
        while (columnCount >= 2 && args[columnCount - 2].Type == JgsType.String
            && args[columnCount - 2].AsString is "VariableNames" or "RowNames")
        {
            JgsValue nameList = args[columnCount - 1];
            JgsValue[] nameValues = nameList.Type == JgsType.Cell ? nameList.AsCell
                : nameList.Type == JgsType.Array ? nameList.BoxedElements()
                : throw new JgsRuntimeException(line, col, $"{name}: 'VariableNames' takes a cell of names.");
            if (args[columnCount - 2].AsString == "RowNames") rowNames = Array.ConvertAll(nameValues, v => StringOf(v).AsString);
            else names = Array.ConvertAll(nameValues, v => StringOf(v).AsString);
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

        for (int i = 0; i < columnCount; i++)
        {
            string columnName = names is not null ? names[i] : $"Var{i + 1}";
            columns.Add(TableColumnFrom(name, columnName, args[i], line, col));
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

        if (rowNames is not null && (rowNames.Length != rows || rowNames.Distinct().Count() != rows || rowNames.Any(string.IsNullOrEmpty)))
            throw new JgsRuntimeException(line, col, "table: RowNames must contain one unique nonempty name per row.");
        return JgsValue.Table(new Table(columns) { RowNames = rowNames, RowTimes = timeColumn });
    }

    /// <summary>
    /// A script value as the table column <paramref name="columnName"/> — the rule <c>table(…)</c>
    /// applies to each argument, and the converse of <see cref="TableColumnValue"/>: a cell or an
    /// array of strings is a text column, anything else is numbers.
    /// </summary>
    internal static TableColumn TableColumnFrom(string verb, string columnName, JgsValue value, int line, int col)
    {
        if (value.IsDatetime) return new DateTimeColumn(columnName, TimeMs(value).Select(ms => ms / JgsTime.MsPerDay).ToArray());
        if (value.IsDuration) return new JgsTimeColumn(columnName, TimeMs(value), value.TimeTag!);
        if (value.Type == JgsType.Cell || (value.Type == JgsType.Array && HasStringElements(value)))
        {
            JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
            return new TextColumn(columnName, Array.ConvertAll(elements, v => (string?)StringOf(v).AsString));
        }

        if (value.Type == JgsType.Array && value.Cols > 1)
            return new NumberMatrixColumn(columnName, FlattenColumnMajor(verb, value, line, col), value.Rows, value.Cols);
        return new NumberColumn(columnName, ToDoubles(verb, value, line, col));
    }

    /// <summary>
    /// <paramref name="table"/> with <paramref name="column"/> in place of the variable of the same
    /// name, or appended when there is none — what <c>T.Var = v</c> means. A <see cref="Table"/>
    /// holds its columns by value, so a write is a rebuild; the row count is the one thing that must
    /// already agree.
    /// </summary>
    /// <remarks>
    /// V6 (ADR 0167): the rebuild keeps what the table is — row names, row times, dimension names,
    /// units, descriptions, description and user data (<see cref="Table.WithColumns"/>). A write
    /// that made an existing variable longer grows the table to match, as R2025b does: the other
    /// variables fill with their kind's default, the row names go on as <c>Row3</c>, <c>Row4</c>,
    /// and the row times as missing (measured). A new variable must still have the table's height.
    /// </remarks>
    internal static Table WithColumn(Table table, TableColumn column, bool grows, int line, int col)
    {
        // Only a write into part of a variable grows the table; T.Var = column must match.
        bool replaces = table.Columns.Any(c => string.Equals(c.Name, column.Name, StringComparison.Ordinal));
        if (grows && column.RowCount > table.RowCount && replaces)
        {
            table = GrownTo(table, column.RowCount);
        }

        if (column.RowCount != table.RowCount)
        {
            throw new JgsRuntimeException(line, col,
                $"To assign to or create a variable in a table, the number of rows must match the table's ({column.RowCount} given, {table.RowCount} expected).");
        }

        var columns = new List<TableColumn>(table.Columns);
        int existing = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].Name, column.Name, StringComparison.Ordinal))
            {
                existing = i;
                break;
            }
        }

        if (existing >= 0)
        {
            columns[existing] = column;
        }
        else
        {
            columns.Add(column);
        }

        return table.WithColumns(columns);
    }

    /// <summary><paramref name="table"/> without the variable <paramref name="name"/> — what <c>T.Var = []</c> means.</summary>
    internal static Table WithoutColumn(Table table, string name) =>
        table.WithColumns(table.Columns.Where(c => !string.Equals(c.Name, name, StringComparison.Ordinal)).ToArray());

    /// <summary>
    /// <paramref name="table"/> grown to <paramref name="rows"/> rows: numbers fill with 0, text
    /// with the empty char, times with the missing one; row names continue as <c>RowN</c>.
    /// </summary>
    internal static Table GrownTo(Table table, int rows)
    {
        var grown = new TableColumn[table.ColumnCount];
        for (int i = 0; i < grown.Length; i++)
        {
            grown[i] = GrownColumn(table[i], rows);
        }

        string[]? names = null;
        if (table.RowNames is { } old)
        {
            names = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                names[r] = r < old.Count ? old[r] : $"Row{r + 1}";
            }
        }

        return table.WithColumns(grown).WithRowLabels(names, table.RowTimes is null ? null : GrownColumn(table.RowTimes, rows, missing: true));
    }

    private static TableColumn GrownColumn(TableColumn column, int rows, bool missing = false)
    {
        int old = column.RowCount;
        switch (column)
        {
            case JgsTimeColumn time:
                return time.Grown(rows);
            case NumberMatrixColumn matrix:
            {
                var values = new double[rows * matrix.Width];
                for (int c = 0; c < matrix.Width; c++)
                {
                    for (int r = 0; r < old; r++)
                    {
                        values[(c * rows) + r] = matrix.Values[(c * old) + r];
                    }
                }

                return new NumberMatrixColumn(column.Name, values, rows, matrix.Width);
            }

            case TextColumn text:
            {
                var values = new string?[rows];
                for (int r = 0; r < rows; r++)
                {
                    values[r] = r < old ? text.GetString(r) : string.Empty;
                }

                return new TextColumn(column.Name, values);
            }

            default:
            {
                var values = new double[rows];
                if (missing || column is DateTimeColumn)
                {
                    Array.Fill(values, double.NaN);
                }

                for (int r = 0; r < old; r++)
                {
                    values[r] = column.GetNumber(r);
                }

                return column is DateTimeColumn ? new DateTimeColumn(column.Name, values) : new NumberColumn(column.Name, values);
            }
        }
    }

    /// <summary>The names <c>T.Properties</c> answers to and takes, in R2025b's order for the ones kept here.</summary>
    private static readonly string[] TablePropertyNames =
        ["Description", "UserData", "DimensionNames", "VariableNames", "VariableDescriptions", "VariableUnits", "RowNames", "RowTimes"];

    /// <summary><c>T.Properties</c>, a struct of what the table is beyond its variables.</summary>
    internal static JgsValue TablePropertiesValue(Table table, int line, int col)
    {
        JgsValue RowCell(IReadOnlyList<string>? perColumn) => perColumn is null || perColumn.All(string.IsNullOrEmpty)
            ? EmptyCell()
            : JgsValue.Cell(perColumn.Select(JgsValue.Str).ToArray());

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Description"] = JgsValue.Str(table.Description ?? string.Empty),
            ["UserData"] = table.UserData is JgsValue data ? JgsValue.Share(data) : JgsMatrix.FromElements([], 0, 0),
            ["DimensionNames"] = JgsValue.Cell((table.DimensionNames
                ?? [table.RowTimes?.Name ?? "Row", "Variables"]).Select(JgsValue.Str).ToArray()),
            ["VariableNames"] = JgsValue.Cell(table.ColumnNames.Select(JgsValue.Str).ToArray()),
            ["VariableDescriptions"] = RowCell(table.VariableDescriptions),
            ["VariableUnits"] = RowCell(table.VariableUnits),
            ["RowNames"] = RowNameCell(table.RowNames),
            ["RowTimes"] = table.RowTimes is null ? JgsValue.Array([]) : TableColumnValue(new Table([table.RowTimes]), table.RowTimes.Name, line, col),
        };
        return JgsValue.Struct(fields);
    }

    /// <summary>
    /// <paramref name="table"/> with its properties as <paramref name="properties"/> has them — the
    /// set half of a write through <c>T.Properties</c> (V6): the struct is read out, written like
    /// any struct, and put back here, where each property is checked in R2025b's words.
    /// </summary>
    internal static Table WithTableProperties(Table table, JgsValue properties, int line, int col)
    {
        if (properties.Type != JgsType.Struct || properties.IsStructArray)
        {
            throw new JgsRuntimeException(line, col, "A table's Properties must be set to a scalar struct of them.");
        }

        Dictionary<string, JgsValue> given = properties.AsStruct;
        foreach (string name in given.Keys)
        {
            if (Array.IndexOf(TablePropertyNames, name) < 0)
            {
                throw new JgsRuntimeException(line, col, $"Unrecognized table property name '{name}'.");
            }
        }

        string[]? PerVariable(string property)
        {
            string[] list = NameList(given[property], property, line, col);
            if (list.Length == 0)
            {
                return null;
            }

            if (list.Length != table.ColumnCount)
            {
                throw new JgsRuntimeException(line, col,
                    $"The {property} property must contain one element for each variable in the table.");
            }

            return list;
        }

        string[] variableNames = NameList(given["VariableNames"], "VariableNames", line, col);
        if (variableNames.Length != table.ColumnCount)
        {
            throw new JgsRuntimeException(line, col,
                "The VariableNames property must contain one name for each variable in the table.");
        }

        if (variableNames.Distinct(StringComparer.Ordinal).Count() != variableNames.Length)
        {
            throw new JgsRuntimeException(line, col, "Duplicate table variable name.");
        }

        var columns = new TableColumn[table.ColumnCount];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = RenamedColumn(table[i], variableNames[i]);
        }

        string[] rowNames = NameList(given["RowNames"], "RowNames", line, col);
        if (rowNames.Length != 0 && (rowNames.Length != table.RowCount || rowNames.Distinct().Count() != rowNames.Length || rowNames.Any(string.IsNullOrEmpty)))
        {
            throw new JgsRuntimeException(line, col, "The RowNames property must be a cell array or string array, with each name containing one or more characters and one name for each row.");
        }

        string[] dimensionNames = NameList(given["DimensionNames"], "DimensionNames", line, col);
        if (dimensionNames.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "The DimensionNames property must be a two-element cell array of character vectors or string array.");
        }

        TableColumn? rowTimes = table.RowTimes;
        if (rowTimes is not null && dimensionNames[0] != rowTimes.Name)
        {
            rowTimes = RenamedColumn(rowTimes, dimensionNames[0]);
        }

        JgsValue description = given["Description"];
        bool defaultDimensions = dimensionNames[0] == (table.RowTimes is null ? "Row" : "Time") && dimensionNames[1] == "Variables"
            && table.DimensionNames is null;
        return new Table(columns)
        {
            RowNames = rowNames.Length == 0 ? null : rowNames,
            RowTimes = rowTimes,
            DimensionNames = defaultDimensions ? null : dimensionNames,
            VariableUnits = PerVariable("VariableUnits"),
            VariableDescriptions = PerVariable("VariableDescriptions"),
            Description = description.Type == JgsType.String || IsStringScalar(description) ? TextOf(description)
                : description.Type == JgsType.Array && description.ArrayLength == 0 ? null
                : throw new JgsRuntimeException(line, col, "The Description property must be a character vector or string scalar."),
            UserData = given["UserData"],
        };
    }

    private static string[] NameList(JgsValue value, string property, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return [value.AsString];
        }

        if (value.Type == JgsType.Cell || (value.Type == JgsType.Array && (value.IsStringArray || value.ArrayLength == 0)))
        {
            JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
            return Array.ConvertAll(elements, element => element.Type == JgsType.String
                ? element.AsString
                : throw new JgsRuntimeException(line, col,
                    $"The {property} property must be a cell array of character vectors or a string array."));
        }

        throw new JgsRuntimeException(line, col,
            $"The {property} property must be a cell array of character vectors or a string array.");
    }

    /// <summary><paramref name="column"/> under another name; the same column when the name is its own.</summary>
    internal static TableColumn RenamedColumn(TableColumn column, string name)
    {
        if (string.Equals(column.Name, name, StringComparison.Ordinal))
        {
            return column;
        }

        return column switch
        {
            JgsTimeColumn time => time.Renamed(name),
            NumberMatrixColumn matrix => new NumberMatrixColumn(name, matrix.Values.ToArray(), matrix.RowCount, matrix.Width),
            NumberColumn numbers => new NumberColumn(name, numbers.Values.ToArray()),
            DateTimeColumn dates => new DateTimeColumn(name, dates.Values.ToArray()),
            TextColumn text => new TextColumn(name, Enumerable.Range(0, text.RowCount).Select(r => text.GetString(r)).ToArray()),
            _ => throw new InvalidOperationException($"A {column.GetType().Name} cannot be renamed."),
        };
    }

    /// <summary>
    /// A table column as a script value — what <c>T.Code</c> reads: numeric columns come back as
    /// column vectors, text columns as cells of char (so <c>T.Code{2}</c> braces in).
    /// </summary>
    internal static JgsValue TableColumnValue(Table table, string columnName, int line, int col)
    {
        if (columnName == "Properties") return TablePropertiesValue(table, line, col);
        if (table.RowTimes is { } time && columnName == time.Name) return TableColumnValue(new Table([time]), columnName, line, col);
        if (!table.TryGetColumn(columnName, out TableColumn column))
        {
            throw new JgsRuntimeException(line, col,
                $"The table has no variable '{columnName}'. Its variables are: {string.Join(", ", table.ColumnNames)}.");
        }

        if (column is JgsTimeColumn times) return times.ToValue();
        if (column is NumberMatrixColumn matrix) return JgsMatrix.FromColumnMajorDims((double[])matrix.Values.Clone(), [matrix.RowCount, matrix.Width]);
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

        return column.Type == ColumnType.DateTime
            ? JgsMatrix.FromColumnMajorDims(values.Select(d => d * JgsTime.MsPerDay).ToArray(), [values.Length, 1]).MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, JgsTime.DefaultDatetimeFormat))
            : JgsMatrix.FromColumnMajorDims(values, [values.Length, 1]);
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

    /// <summary>
    /// One element of <c>string(x)</c>. A number is written the way <c>num2str</c> writes one number,
    /// which is MATLAB's rule for <c>string(pi)</c> — <c>"3.1416"</c>, not every digit the double
    /// holds — through <see cref="JgsSprintf.FormatScalarGeneral"/>, and NaN is the missing string
    /// (measured). A complex number spells both parts, a string scalar is its text, and anything
    /// else goes through <see cref="StringOf"/>.
    /// </summary>
    internal static JgsValue StringElementOf(JgsValue value) =>
        value.Type == JgsType.Number
            ? JgsValue.Str(double.IsNaN(value.AsNumber) ? MissingSentinel : JgsSprintf.FormatScalarGeneral(value.AsNumber))
            : value.Type == JgsType.Complex ? JgsValue.Str(ComplexText(value))
            : IsStringScalar(value) ? JgsValue.Str(TextOf(value)) : StringOf(value);

    /// <summary>
    /// The elements of <c>string(x)</c> for a packed real array, in storage order: each double
    /// through the scalar formatter, NaN as the missing string, a logical as its word — what
    /// <see cref="StringElementOf"/> answers for each element <see cref="JgsValue.BoxedElements"/>
    /// would have boxed, without boxing one (ADR 0156).
    /// </summary>
    private static JgsValue[] PackedStringElements(JgsValue input)
    {
        JGraph.Numerics.NumericBuffer buffer = input.AsBuffer;
        int count = input.ArrayLength;
        Span<double> values = buffer.AsSpan(0, count);
        var texts = new JgsValue[count];
        if (input.PackedKind == JgsPackedKind.Bool)
        {
            for (int i = 0; i < count; i++)
            {
                texts[i] = JgsValue.Str(values[i] != 0 ? "true" : "false");
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                double value = values[i];
                texts[i] = JgsValue.Str(double.IsNaN(value) ? MissingSentinel : JgsSprintf.FormatScalarGeneral(value));
            }
        }

        GC.KeepAlive(buffer);
        return texts;
    }

    /// <summary>
    /// A number as <c>string</c> spells a complex one: both parts, always, so string(2.5i) is
    /// "0+2.5i" and 3 inside a complex array is "3+0i" (measured).
    /// </summary>
    internal static string ComplexText(JgsValue value)
    {
        System.Numerics.Complex z = value.Type == JgsType.Complex ? value.AsComplex : new(value.AsNumber, 0);
        string re = NumberText([JgsValue.Number(z.Real)], 0, 0).AsString;
        string im = NumberText([JgsValue.Number(Math.Abs(z.Imaginary))], 0, 0).AsString;
        string sign = z.Imaginary < 0 || (z.Imaginary == 0 && double.IsNegative(z.Imaginary)) ? "-" : "+";
        return re + sign + im + "i";
    }

    private static JgsValue StringOf(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            return value;
        }

        if (value.Type is JgsType.Number or JgsType.Complex)
        {
            return NumberText([value], 0, 0);
        }

        return JgsValue.Str(value.Display());
    }

    /// <summary>What MATLAB calls an element of a categorical whose value is in no category.</summary>
    internal const string UndefinedCategory = "<undefined>";

    /// <summary>
    /// <c>categorical(values, valueset)</c> and <c>categorical(values, valueset, names)</c>: a value
    /// that stands in the value set takes the name at its place there, and one that does not becomes
    /// <c>&lt;undefined&gt;</c>, which is what R2025b's display and its <c>cellstr</c> call it.
    /// Without the third argument the value set names itself, written the way the values were.
    /// </summary>
    /// <remarks>
    /// Matching is on the text each value is written as, because that is already what a categorical
    /// is here — a cell of names — so the value set and the values are compared after exactly one
    /// conversion rather than two kinds of comparison, one per kind of value set.
    /// </remarks>
    private static JgsValue Relabelled(JgsValue named, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string[] valueset = TextElementsOf("categorical: the value set", args[1], line, col);
        string[] names = args.Count >= 3
            ? TextElementsOf("categorical: the category names", args[2], line, col)
            : valueset;
        if (names.Length != valueset.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"categorical: the value set has {valueset.Length} values but there are {names.Length} category names.");
        }

        var byValue = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < valueset.Length; i++)
        {
            if (!byValue.TryAdd(valueset[i], names[i]))
            {
                throw new JgsRuntimeException(line, col,
                    $"categorical: '{valueset[i]}' appears more than once in the value set.");
            }
        }

        JgsValue[] elements = named.AsCell;
        var relabelled = new JgsValue[elements.Length];
        for (int i = 0; i < relabelled.Length; i++)
        {
            relabelled[i] = JgsValue.Str(byValue.TryGetValue(elements[i].AsString, out string? name)
                ? name
                : UndefinedCategory);
        }

        return CellShapedLike(named, relabelled);
    }

    /// <summary>Every element of a cell, string array or numeric array, written as text.</summary>
    private static string[] TextElementsOf(string what, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return [value.AsString];
        }

        JgsValue[] elements = value.Type switch
        {
            JgsType.Cell => value.AsCell,
            JgsType.Array => value.BoxedElements(),
            _ => throw new JgsRuntimeException(line, col,
                $"{what} must be a cell, a string array, or a numeric array, not a {value.TypeName}."),
        };

        return Array.ConvertAll(elements, static e => StringOf(e).AsString);
    }

    /// <summary>Wraps freshly built elements in a cell of the input's shape.</summary>
    private static JgsValue CellShapedLike(JgsValue input, JgsValue[] elements)
    {
        JgsValue result = JgsValue.Cell(elements);
        if (input.Rows > 1 && input.Cols > 1)
        {
            result.Reshape(input.Rows, input.Cols);
        }
        else if (elements.Length > 1 && input.Cols == 1 && input.Rows > 1)
        {
            result.Reshape(elements.Length, 1);
        }

        return result;
    }

    /// <summary>Wraps freshly built elements in the input's shape (or a plain row when unshaped).</summary>
    private static JgsValue ShapedLike(JgsValue input, JgsValue[] elements)
    {
        JgsValue result = JgsValue.Array(elements);
        if (input.Rows > 1 && input.Cols > 1)
        {
            result.Reshape(input.Rows, input.Cols);
        }
        else if (elements.Length > 1 && input.Type is (JgsType.Array or JgsType.Cell) && input.Cols == 1 && input.Rows > 1)
        {
            result.Reshape(elements.Length, 1);
        }

        return result;
    }
}
