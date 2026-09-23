using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The table verbs V6 adds (ADR 0167, #122): <c>table2array</c>, <c>addvars</c>, <c>varfun</c>
/// and <c>rowfun</c>, and <c>vertcat</c>/<c>horzcat</c> when a table is among the arguments.
/// They are declared beside the interpreter because they join values by the bracket's rules
/// (<see cref="Interpreter.Concatenate(List{JgsValue[]}, int, int)"/>), <c>addvars</c> names a new variable after the
/// expression it was written as, and the two <c>fun</c> verbs run script code — each row, or
/// each variable, is a call — so both are on the <see cref="ScriptRunningBuiltins"/> list.
/// Every rule and message is R2025b's, recorded in <c>table_forms</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the table verbs into <paramref name="env"/>.</summary>
    internal static void RegisterTableFormBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // vertcat(T, U) and horzcat(T, U) are the brackets' table join; anything else is what they were.
        foreach ((string name, bool vertical) in new[] { ("vertcat", true), ("horzcat", false) })
        {
            if (!env.TryGet(name, out JgsValue plain) || plain.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable inner = plain.AsCallable;
            Define(name, (args, line, col) =>
            {
                if (!args.Any(static a => a.Type == JgsType.Table))
                {
                    return inner.Call(args, line, col);
                }

                List<JgsValue[]> rows = vertical
                    ? args.Select(static a => new[] { a }).ToList()
                    : [[.. args]];
                return interpreter.ConcatenateTables(rows, line, col);
            });
        }

        Define("table2array", (args, line, col) =>
        {
            Arity("table2array", args, 1, line, col);
            Table table = Tbl("table2array", args, 0, line, col);
            var values = new JgsValue[table.ColumnCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = TableColumnValue(table, table[i].Name, line, col);
                if (i > 0 && (values[i].Type == JgsType.Cell) != (values[0].Type == JgsType.Cell))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Unable to concatenate the table variables '{table[0].Name}' and '{table[i].Name}', because their types are {ClassOf(values[0], JgsDialect.Matlab)} and {ClassOf(values[i], JgsDialect.Matlab)}.");
                }
            }

            return values.Length == 0
                ? JgsEmpty.Shaped(table.RowCount, 0)
                : interpreter.Concatenate([values], line, col);
        });

        Define("addvars", (args, line, col) => AddVariables(args, interpreter.PendingCall, interpreter, line, col));
        Define("varfun", (args, line, col) => ApplyOverVariables(args, interpreter, line, col));
        Define("rowfun", (args, line, col) => ApplyOverRows(args, interpreter, line, col));
    }

    /// <summary>
    /// The name-value tail of a table verb, from <paramref name="from"/>: each name must be one
    /// of <paramref name="allowed"/> (case-insensitively), and the ones MATLAB has that this
    /// build does not are refused by name rather than ignored.
    /// </summary>
    private static Dictionary<string, JgsValue> TableVerbOptions(
        string verb, IReadOnlyList<JgsValue> args, int from, string[] allowed, string[] unsupported, int line, int col)
    {
        var options = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
        for (int i = from; i < args.Count; i += 2)
        {
            if (args[i].Type != JgsType.String && !IsStringScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, $"{verb}: expected an option name, but got a {args[i].TypeName}.");
            }

            string word = TextOf(args[i]);
            if (unsupported.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"{verb}: '{word}' is not supported here.");
            }

            string? canonical = allowed.FirstOrDefault(a => string.Equals(a, word, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: unknown option '{word}' (options: {string.Join(", ", allowed)}).");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{verb}: '{canonical}' needs a value after it.");
            }

            options[canonical] = args[i + 1];
        }

        return options;
    }

    /// <summary>Where the option tail of a table verb starts: the first option name after <paramref name="from"/>.</summary>
    private static int OptionsStart(IReadOnlyList<JgsValue> args, int from, string[] allowed, string[] unsupported)
    {
        for (int i = from; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String || IsStringScalar(args[i]))
            {
                string word = TextOf(args[i]);
                if (allowed.Contains(word, StringComparer.OrdinalIgnoreCase) || unsupported.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return args.Count;
    }

    // ---- addvars --------------------------------------------------------------------------------

    private static readonly string[] AddVarsOptions = ["NewVariableNames", "Before", "After"];

    /// <summary>
    /// <c>addvars(T, v1, v2, …, 'NewVariableNames', names, 'Before' | 'After', where)</c>: the values
    /// as new variables, at the end or where said. A variable is named as given, else after the
    /// plain variable it was written as, else <c>VarN</c> by its position in the result, made
    /// unique against the names there with <c>_1</c>, <c>_2</c>… (measured: <c>'Before', 'Var2'</c>
    /// on <c>Var1</c>, <c>Var2</c> adds <c>Var2_1</c>). The rebuild keeps what the table is.
    /// </summary>
    private static JgsValue AddVariables(
        IReadOnlyList<JgsValue> args, CallExpr? call, Interpreter interpreter, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "addvars(T, var1, ...) needs the table and at least one variable to add.");
        }

        Table table = Tbl("addvars", args, 0, line, col);
        int tail = OptionsStart(args, 1, AddVarsOptions, []);
        Dictionary<string, JgsValue> options = TableVerbOptions("addvars", args, tail, AddVarsOptions, [], line, col);
        int count = tail - 1;
        if (count == 0)
        {
            throw new JgsRuntimeException(line, col, "addvars(T, var1, ...) needs at least one variable to add.");
        }

        int position = table.ColumnCount;
        if (options.TryGetValue("Before", out JgsValue? before))
        {
            position = VariablePosition(table, before, line, col);
        }
        else if (options.TryGetValue("After", out JgsValue? after))
        {
            position = VariablePosition(table, after, line, col) + 1;
        }

        string[]? given = options.TryGetValue("NewVariableNames", out JgsValue? named)
            ? FieldNameList("addvars", named, line, col)
            : null;
        if (given is not null && given.Length != count)
        {
            throw new JgsRuntimeException(line, col,
                "The NewVariableNames property must contain one name for each variable being added.");
        }

        var names = new List<string>(table.ColumnNames);
        var added = new TableColumn[count];
        for (int i = 0; i < count; i++)
        {
            string name;
            if (given is not null)
            {
                name = given[i];
                if (names.Contains(name, StringComparer.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, $"Duplicate table variable name: '{name}'.");
                }
            }
            else
            {
                name = call is not null && 1 + i < call.Arguments.Count && call.Arguments[1 + i] is VariableExpr variable
                    ? variable.Name
                    : $"Var{position + i + 1}";
                name = UniqueName(name, names);
            }

            TableColumn column = TableColumnFrom("addvars", name, args[1 + i], line, col);
            if (column.RowCount != table.RowCount)
            {
                throw new JgsRuntimeException(line, col,
                    "To assign to or create a variable in a table, the number of rows must match the height of the table.");
            }

            names.Insert(position + i, name);
            added[i] = column;
        }

        var columns = new List<TableColumn>(table.Columns);
        columns.InsertRange(position, added);
        return JgsValue.Table(table.WithColumns(columns));
    }

    /// <summary>A variable's position from a name or a 1-based number, for <c>'Before'</c> and <c>'After'</c>.</summary>
    private static int VariablePosition(Table table, JgsValue where, int line, int col)
    {
        if (where.Type == JgsType.String || IsStringScalar(where))
        {
            string name = TextOf(where);
            for (int i = 0; i < table.ColumnCount; i++)
            {
                if (string.Equals(table[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new JgsRuntimeException(line, col, $"Unrecognized table variable name '{name}'.");
        }

        if (where.Type != JgsType.Number || where.AsNumber != Math.Floor(where.AsNumber)
            || where.AsNumber < 1 || where.AsNumber > table.ColumnCount)
        {
            throw new JgsRuntimeException(line, col,
                "The location must be a table variable name or a position between 1 and the number of variables.");
        }

        return (int)where.AsNumber - 1;
    }

    /// <summary><paramref name="name"/> made unique against <paramref name="taken"/> the way MATLAB does: <c>name_1</c>, <c>name_2</c>…</summary>
    private static string UniqueName(string name, List<string> taken)
    {
        if (!taken.Contains(name, StringComparer.Ordinal))
        {
            return name;
        }

        for (int suffix = 1; ; suffix++)
        {
            string candidate = $"{name}_{suffix}";
            if (!taken.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }
    }

    // ---- varfun ---------------------------------------------------------------------------------

    private static readonly string[] VarFunOptions = ["InputVariables", "OutputFormat"];
    private static readonly string[] FunUnsupported = ["GroupingVariables", "ErrorHandler"];

    /// <summary>
    /// <c>varfun(func, T, 'InputVariables', v, 'OutputFormat', 'table' | 'uniform' | 'cell')</c>:
    /// the function applied to each variable whole. A table answer's variables are
    /// <c>fun_Var</c> (<c>Fun_Var</c> for an anonymous function), the row names are dropped and
    /// the row times cut to the answer's height; <c>'uniform'</c> needs a scalar from each call
    /// and answers a row; <c>'cell'</c> a row of cells.
    /// </summary>
    private static JgsValue ApplyOverVariables(IReadOnlyList<JgsValue> args, Interpreter interpreter, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "varfun(func, T) needs a function handle and a table.");
        }

        IJgsCallable func = FunctionArgument("varfun", args[0], line, col);
        Table table = Tbl("varfun", args, 1, line, col);
        Dictionary<string, JgsValue> options = TableVerbOptions("varfun", args, 2, VarFunOptions, FunUnsupported, line, col);
        int[] selected = SelectedVariables("varfun", table, options, line, col);
        string format = OutputFormatOf("varfun", options, line, col);

        var answers = new JgsValue[selected.Length];
        for (int i = 0; i < selected.Length; i++)
        {
            string name = table[selected[i]].Name;
            answers[i] = func.Call([TableColumnValue(table, name, line, col)], line, col);
            if (format == "uniform" && !IsScalarAnswer(answers[i]))
            {
                throw new JgsRuntimeException(line, col,
                    $"The function '{SourceTextOf("varfun", args[0], line, col)}' returned a non-scalar value when applied to the variable '{name}'.");
            }
        }

        switch (format)
        {
            case "uniform":
                return answers.Length == 0 ? JgsEmpty.Shaped(1, 0) : interpreter.Concatenate([answers], line, col);
            case "cell":
            {
                JgsValue cell = JgsValue.Cell(Array.ConvertAll(answers, JgsValue.Share));
                cell.Reshape(1, answers.Length);
                return cell;
            }
        }

        string prefix = func is AnonymousFunction ? "Fun" : func.Name;
        var columns = new TableColumn[selected.Length];
        int height = -1;
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = TableColumnFrom("varfun", $"{prefix}_{table[selected[i]].Name}", answers[i], line, col);
            if (height >= 0 && columns[i].RowCount != height)
            {
                throw new JgsRuntimeException(line, col,
                    $"varfun: the function returned {columns[i].RowCount} row(s) for '{table[selected[i]].Name}' where the variables before it had {height}; a table answer needs one height.");
            }

            height = columns[i].RowCount;
        }

        TableColumn? times = null;
        if (table.RowTimes is { } rowTimes && columns.Length > 0)
        {
            times = height <= table.RowCount
                ? new Table([rowTimes]).Select(Enumerable.Range(0, height).ToArray(), [0])[0]
                : GrownTo(new Table([rowTimes]), height)[0];
        }

        return JgsValue.Table(new Table(columns)
        {
            RowTimes = times,
            DimensionNames = table.DimensionNames,
            Description = table.Description,
            UserData = table.UserData,
        });
    }

    // ---- rowfun ---------------------------------------------------------------------------------

    private static readonly string[] RowFunOptions =
        ["InputVariables", "OutputFormat", "OutputVariableNames", "NumOutputs", "SeparateInputs", "ExtractCellContents"];

    /// <summary>
    /// <c>rowfun(func, T, …)</c>: the function called once per row, each selected variable's row
    /// an argument (a text variable's row is a one-element cell unless
    /// <c>'ExtractCellContents'</c>; <c>'SeparateInputs', false</c> hands the row as one array).
    /// The answers stack into a table whose variables are <c>Var1</c>… or the
    /// <c>'OutputVariableNames'</c>, with the row names and row times kept; <c>'uniform'</c> needs
    /// a scalar from each call and answers a column per output; <c>'cell'</c> a cell of them.
    /// </summary>
    private static JgsValue ApplyOverRows(IReadOnlyList<JgsValue> args, Interpreter interpreter, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "rowfun(func, T) needs a function handle and a table.");
        }

        IJgsCallable func = FunctionArgument("rowfun", args[0], line, col);
        Table table = Tbl("rowfun", args, 1, line, col);
        Dictionary<string, JgsValue> options = TableVerbOptions("rowfun", args, 2, RowFunOptions, FunUnsupported, line, col);
        int[] selected = SelectedVariables("rowfun", table, options, line, col);
        string format = OutputFormatOf("rowfun", options, line, col);
        int outputs = options.TryGetValue("NumOutputs", out JgsValue? wanted) ? PositiveCount("rowfun", "NumOutputs", wanted, line, col) : 1;
        bool separate = !options.TryGetValue("SeparateInputs", out JgsValue? separateFlag) || TruthOf("rowfun", "SeparateInputs", separateFlag, line, col);
        bool extract = options.TryGetValue("ExtractCellContents", out JgsValue? extractFlag) && TruthOf("rowfun", "ExtractCellContents", extractFlag, line, col);
        string[]? names = options.TryGetValue("OutputVariableNames", out JgsValue? outputNames)
            ? FieldNameList("rowfun", outputNames, line, col)
            : null;
        if (names is not null && names.Length != outputs)
        {
            throw new JgsRuntimeException(line, col,
                "The OutputVariableNames property must contain one name for each output of the function.");
        }

        var values = new JgsValue[selected.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = TableColumnValue(table, table[selected[i]].Name, line, col);
        }

        var collected = new JgsValue[outputs][];
        for (int o = 0; o < outputs; o++)
        {
            collected[o] = new JgsValue[table.RowCount];
        }

        for (int r = 0; r < table.RowCount; r++)
        {
            var inputs = new JgsValue[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                inputs[i] = RowArgumentOf(values[i], r, extract);
            }

            JgsValue[] handed = separate ? inputs : [interpreter.Concatenate([inputs], line, col)];
            JgsValue[] answers = CallForOutputs(func, handed, outputs, line, col);
            if (answers.Length < outputs)
            {
                throw new JgsRuntimeException(line, col,
                    $"rowfun: the function produced {answers.Length} output(s) for the {Ordinal(r + 1)} row, but {outputs} were asked for.");
            }

            for (int o = 0; o < outputs; o++)
            {
                if (format == "uniform" && !IsScalarAnswer(answers[o]))
                {
                    throw new JgsRuntimeException(line, col,
                        $"The function '{SourceTextOf("rowfun", args[0], line, col)}' returned a non-scalar value when applied to the {Ordinal(r + 1)} row.");
                }

                collected[o][r] = JgsValue.Share(answers[o]);
            }
        }

        if (format == "cell")
        {
            var cells = new JgsValue[table.RowCount * outputs];
            for (int o = 0; o < outputs; o++)
            {
                collected[o].CopyTo(cells, o * table.RowCount);
            }

            JgsValue cell = JgsValue.Cell(cells);
            cell.Reshape(table.RowCount, outputs);
            return cell;
        }

        var stacked = new JgsValue[outputs];
        for (int o = 0; o < outputs; o++)
        {
            stacked[o] = table.RowCount == 0
                ? JgsEmpty.Shaped(0, 1)
                : interpreter.Concatenate(collected[o].Select(static a => new[] { a }).ToList(), line, col);
        }

        if (format == "uniform")
        {
            return outputs == 1 ? stacked[0] : interpreter.Concatenate([stacked], line, col);
        }

        var columns = new TableColumn[outputs];
        for (int o = 0; o < outputs; o++)
        {
            columns[o] = TableColumnFrom("rowfun", names is not null ? names[o] : $"Var{o + 1}", stacked[o], line, col);
            if (columns[o].RowCount != table.RowCount)
            {
                throw new JgsRuntimeException(line, col,
                    $"rowfun: output {o + 1} stacks to {columns[o].RowCount} row(s) for a table of {table.RowCount}; each call must answer one row.");
            }
        }

        return JgsValue.Table(new Table(columns)
        {
            RowNames = table.RowNames,
            RowTimes = table.RowTimes,
            DimensionNames = table.DimensionNames,
            Description = table.Description,
            UserData = table.UserData,
        });
    }

    /// <summary>Row <paramref name="row"/> of a variable's value, as the call receives it. (Named apart from every other helper: the audit joins calls by name.)</summary>
    private static JgsValue RowArgumentOf(JgsValue value, int row, bool extract)
    {
        if (value.Type == JgsType.Cell && extract)
        {
            return JgsValue.Share(value.AsCell[row]);
        }

        JgsValue picked = JgsValueColumn.RowsOfValue(value, [row]);
        if (picked.Type == JgsType.Array && picked.ArrayLength == 1 && !picked.IsStringArray && !picked.IsTime
            && picked.NumericClass == JgsNumericClass.Double)
        {
            return picked.ElementAt(0);
        }

        return picked;
    }

    // ---- shared ---------------------------------------------------------------------------------

    private static IJgsCallable FunctionArgument(string verb, JgsValue value, int line, int col) =>
        value.Type == JgsType.Function
            ? value.AsCallable
            : throw new JgsRuntimeException(line, col, $"{verb}: the first argument must be a function handle, but got a {value.TypeName}.");

    /// <summary>The variables a verb works over: the <c>'InputVariables'</c>, or every data variable.</summary>
    private static int[] SelectedVariables(string verb, Table table, Dictionary<string, JgsValue> options, int line, int col)
    {
        if (!options.TryGetValue("InputVariables", out JgsValue? chosen))
        {
            return Enumerable.Range(0, table.ColumnCount).ToArray();
        }

        if (chosen.Type is JgsType.Number or JgsType.Array && !chosen.IsStringArray)
        {
            JgsValue[] picks = chosen.Type == JgsType.Number ? [chosen] : chosen.BoxedElements();
            return Array.ConvertAll(picks, p => (int)p.AsNumber - 1 is int at && at >= 0 && at < table.ColumnCount
                ? at
                : throw new JgsRuntimeException(line, col, $"{verb}: 'InputVariables' names a position the table does not have."));
        }

        string[] names = FieldNameList(verb, chosen, line, col);
        var positions = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            positions[i] = -1;
            for (int c = 0; c < table.ColumnCount; c++)
            {
                if (string.Equals(table[c].Name, names[i], StringComparison.Ordinal))
                {
                    positions[i] = c;
                    break;
                }
            }

            if (positions[i] < 0)
            {
                throw new JgsRuntimeException(line, col, $"Unrecognized table variable name '{names[i]}'.");
            }
        }

        return positions;
    }

    private static string OutputFormatOf(string verb, Dictionary<string, JgsValue> options, int line, int col)
    {
        if (!options.TryGetValue("OutputFormat", out JgsValue? given))
        {
            return "table";
        }

        string format = TextOf(given).ToLowerInvariant();
        return format is "table" or "uniform" or "cell"
            ? format
            : throw new JgsRuntimeException(line, col, $"{verb}: 'OutputFormat' must be 'table', 'uniform' or 'cell', not '{TextOf(given)}'.");
    }

    private static int PositiveCount(string verb, string option, JgsValue value, int line, int col) =>
        value.Type == JgsType.Number && value.AsNumber >= 1 && value.AsNumber == Math.Floor(value.AsNumber)
            ? (int)value.AsNumber
            : throw new JgsRuntimeException(line, col, $"{verb}: '{option}' must be a positive whole number.");

    private static bool TruthOf(string verb, string option, JgsValue value, int line, int col) => value.Type switch
    {
        JgsType.Bool => value.AsBool,
        JgsType.Number => value.AsNumber != 0,
        _ => throw new JgsRuntimeException(line, col, $"{verb}: '{option}' takes true or false."),
    };

    /// <summary>Whether an answer is the scalar a <c>'uniform'</c> output takes.</summary>
    private static bool IsScalarAnswer(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
        || (value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray);

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th, 21st — how MATLAB numbers a row in a message.</summary>
    private static string Ordinal(int n)
    {
        int last = n % 10;
        int lastTwo = n % 100;
        string suffix = lastTwo is 11 or 12 or 13 ? "th" : last switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }
}
