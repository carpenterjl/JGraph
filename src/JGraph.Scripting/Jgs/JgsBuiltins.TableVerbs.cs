using System.Globalization;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The table verbs a categorical needs (open item 122): <c>vartype</c>, <c>convertvars</c>,
/// <c>table2cell</c>, <c>join</c>, <c>innerjoin</c> and <c>stack</c>, with <c>array2table</c> and
/// <c>table2struct</c> keeping a categorical variable one. Every verb reads a variable as the value
/// it is (<see cref="TableColumnValue"/>) and builds the answer's variables from values
/// (<see cref="TableColumnFrom"/>), so a categorical, a cell of text, a string array or an integer
/// variable goes through as itself. The behaviour was measured on R2025b (probe_122).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The class a <c>vartype</c> subscript answers to.</summary>
    internal const string VartypeClassName = "vartype";

    /// <summary>Whether a value is a <c>vartype</c> subscript.</summary>
    internal static bool IsVartype(JgsValue value) => value.Type == JgsType.Struct && value.ClassName == VartypeClassName;

    /// <summary>
    /// A <c>vartype</c> subscript as the names of the variables it selects (a row of a cell, empty
    /// when none match); any other subscript as it was.
    /// </summary>
    internal static JgsValue? VartypeAsNames(Table table, JgsValue? index)
    {
        if (index is null || !IsVartype(index))
        {
            return index;
        }

        string type = index.AsStruct["Type"].AsString;
        var names = new List<JgsValue>();
        foreach (TableColumn column in table.Columns)
        {
            if (VariableIsOfType(TableColumnValue(table, column.Name, 0, 0), type))
            {
                names.Add(JgsValue.Str(column.Name));
            }
        }

        JgsValue cell = JgsValue.Cell([.. names]);
        cell.Reshape(1, names.Count);
        return cell;
    }

    /// <summary>Whether a variable's value is of a <c>vartype</c> type: a class name or one of the families.</summary>
    private static bool VariableIsOfType(JgsValue value, string type)
    {
        string cls = ClassOf(value, JgsDialect.Matlab);
        return type switch
        {
            "numeric" => cls is "double" or "single" || IntegerClassNames.Contains(cls, StringComparer.Ordinal),
            "float" => cls is "double" or "single",
            "integer" => IntegerClassNames.Contains(cls, StringComparer.Ordinal),
            "cellstr" => value.Type == JgsType.Cell && value.AsCell.All(static e => e.Type == JgsType.String && !e.IsStringArray),
            _ => cls == type,
        };
    }

    /// <summary>Registers the verbs; <paramref name="interpreter"/> converts and concatenates for them.</summary>
    internal static void RegisterTableVerbs(JgsEnvironment env, Interpreter interpreter)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));

        void DefineMany(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => body(args, 1, line, col)[0])
            {
                MultiOutput = body,
                KeepsStringArguments = true,
            }));

        Define("vartype", (args, line, col) =>
        {
            ArityRange("vartype", args, 0, 1, line, col);
            string type = "numeric";
            if (args.Count == 1)
            {
                if (VerbText(args[0]) is not { } text)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:vartype:InvalidType",
                        "Invalid data type. Input must be a string or a character vector specifying a valid class name.");
                }

                type = text;
            }

            JgsValue made = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Type"] = JgsValue.Str(type) });
            made.SetClassName(VartypeClassName);
            return made;
        });

        Define("table2cell", (args, line, col) =>
        {
            Arity("table2cell", args, 1, line, col);
            if (args[0].Type != JgsType.Table)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table2cell:NonTable", "Input argument must be a table.");
            }

            Table table = args[0].AsTable;
            int rows = table.RowCount;
            int cols = table.ColumnCount;
            var cells = new JgsValue[rows * cols];
            for (int c = 0; c < cols; c++)
            {
                JgsValue value = TableColumnValue(table, table[c].Name, line, col);
                for (int r = 0; r < rows; r++)
                {
                    cells[r + (c * rows)] = RowElement(value, r);
                }
            }

            JgsValue cell = JgsValue.Cell(cells);
            cell.Reshape(rows, cols);
            return cell;
        });

        Define("convertvars", (args, line, col) =>
        {
            Arity("convertvars", args, 3, line, col);
            Table table = Tbl("convertvars", args, 0, line, col);
            int[] picked = VariableIndices("convertvars", table, args[1], interpreter, line, col);
            var columns = new List<TableColumn>(table.Columns);
            foreach (int c in picked)
            {
                string name = table[c].Name;
                JgsValue converted = ConvertVariable(TableColumnValue(table, name, line, col), name, args[2], interpreter, line, col);
                columns[c] = TableColumnFrom("convertvars", name, converted, line, col);
            }

            return JgsValue.Table(table.WithColumns(columns));
        });

        DefineMany("innerjoin", (args, wanted, line, col) => Join("innerjoin", args, inner: true, line, col));

        // join over two tables is the table join; anything else is the string verb it always was.
        if (env.Builtins.TryGet("join", out JgsValue textJoin) && textJoin.Type == JgsType.Function)
        {
            IJgsCallable strings = textJoin.AsCallable;
            JgsValue[] Either(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                args.Count >= 2 && args[0].Type == JgsType.Table && args[1].Type == JgsType.Table
                    ? Join("join", args, inner: false, line, col)
                    : strings is IJgsMultiCallable multi ? multi.CallMultiple(args, Math.Max(wanted, 1), line, col) : [strings.Call(args, line, col)];
            env.Builtins.Register("join", JgsValue.Function(new BuiltinFunction("join", (args, line, col) => Either(args, 1, line, col)[0])
            {
                MultiOutput = Either,
                KeepsStringArguments = strings is BuiltinFunction { KeepsStringArguments: true },
            }));
        }

        DefineMany("stack", (args, wanted, line, col) => Stack(args, interpreter, line, col));

        // array2table of a categorical makes categorical variables, one a column (probe_122).
        if (env.Builtins.TryGet("array2table", out JgsValue arrayToTable) && arrayToTable.Type == JgsType.Function)
        {
            IJgsCallable plain = arrayToTable.AsCallable;
            env.Builtins.Register("array2table", JgsValue.Function(new BuiltinFunction("array2table", (args, line, col) =>
            {
                if (args.Count == 0 || !args[0].IsCategorical)
                {
                    return plain.Call(args, line, col);
                }

                int rows = args[0].Rows;
                double[] codes = JgsCategorical.CodesOf(args[0]);
                var columns = new List<JgsValue>();
                for (int c = 0; c < args[0].Cols; c++)
                {
                    columns.Add(JgsCategorical.FromCodes([.. codes.Skip(c * rows).Take(rows)], rows, 1, args[0].CategoricalTag!));
                }

                columns.AddRange(args.Skip(1));
                return BuildTable("array2table", columns, null, line, col);
            })));
        }

        // table2struct keeps each variable's own kind: a categorical element, an integer, a struct;
        // ToScalar makes one struct whose fields are the variables (probe_122).
        if (env.Builtins.TryGet("table2struct", out JgsValue toStruct) && toStruct.Type == JgsType.Function)
        {
            IJgsCallable before = toStruct.AsCallable;
            env.Builtins.Register("table2struct", JgsValue.Function(new BuiltinFunction("table2struct", (args, line, col) =>
            {
                if (!JgsRunningDialect.ThreadIsMatlab || args.Count == 0 || args[0].Type != JgsType.Table)
                {
                    return before.Call(args, line, col);
                }

                Table table = args[0].AsTable;
                bool scalar = args.Count == 3 && VerbText(args[1]) is { } option && option.Equals("ToScalar", StringComparison.OrdinalIgnoreCase)
                    && args[2].AsNumber != 0;
                if (args.Count != 1 && !(args.Count == 3 && VerbText(args[1]) is { } named && named.Equals("ToScalar", StringComparison.OrdinalIgnoreCase)))
                {
                    return before.Call(args, line, col);
                }

                if (scalar)
                {
                    var whole = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                    foreach (TableColumn column in table.Columns)
                    {
                        whole[column.Name] = TableColumnValue(table, column.Name, line, col);
                    }

                    return JgsValue.Struct(whole);
                }

                var values = table.Columns.Select(c => (c.Name, Value: TableColumnValue(table, c.Name, line, col), Held: c is JgsValueColumn or JgsTimeColumn)).ToArray();
                var elements = new Dictionary<string, JgsValue>[table.RowCount];
                for (int r = 0; r < table.RowCount; r++)
                {
                    var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                    foreach ((string name, JgsValue value, bool held) in values)
                    {
                        TableColumn column = table[name];
                        fields[name] = held || column is NumberMatrixColumn ? RowElement(value, r)
                            : column.Type == ColumnType.Text ? JgsValue.Str(column.GetText(r))
                            : JgsValue.Number(column.GetNumber(r));
                    }

                    elements[r] = fields;
                }

                return JgsValue.StructArray(new JgsStructArray(elements, [.. table.ColumnNames]), table.RowCount, table.RowCount == 0 ? 0 : 1);
            })
            { KeepsStringArguments = true }));
        }
    }

    /// <summary>Text from a char row or a string scalar, or null.</summary>
    private static string? VerbText(JgsValue value) =>
        value.Type == JgsType.String && !value.IsStringArray ? value.AsString
        : value.IsStringArray && value.ArrayLength == 1 ? value.ElementAt(0).AsString
        : null;

    /// <summary>Row <paramref name="row"/> of a variable's value as one element: a cell's content, else the row itself.</summary>
    private static JgsValue RowElement(JgsValue value, int row)
    {
        if (value.Type == JgsType.Cell && value.Cols <= 1)
        {
            return JgsValue.Share(value.AsCell[row]);
        }

        return JgsValueColumn.RowsOfValue(value, [row]);
    }

    /// <summary>
    /// The variables a subscript names, as positions: a name, names, positions, a mask, a
    /// <c>vartype</c>, or a function each variable is asked about.
    /// </summary>
    private static int[] VariableIndices(string verb, Table table, JgsValue spec, Interpreter interpreter, int line, int col)
    {
        if (IsVartype(spec))
        {
            spec = VartypeAsNames(table, spec)!;
        }

        if (spec.Type == JgsType.Function)
        {
            var chosen = new List<int>();
            for (int c = 0; c < table.ColumnCount; c++)
            {
                JgsValue asked = spec.AsCallable.Call([TableColumnValue(table, table[c].Name, line, col)], line, col);
                if (asked.Type is JgsType.Bool or JgsType.Number && asked.AsNumber != 0)
                {
                    chosen.Add(c);
                }
            }

            return [.. chosen];
        }

        if (VerbText(spec) is { } one)
        {
            return [NamedVariable(table, one, line, col)];
        }

        if (spec.Type == JgsType.Cell || spec.IsStringArray)
        {
            JgsValue[] names = spec.Type == JgsType.Cell ? spec.AsCell : spec.BoxedElements();
            return [.. names.Select(n => NamedVariable(table, n.AsString, line, col))];
        }

        if (IsLogicalValue(spec))
        {
            double[] mask = ToDoubles(verb, spec, line, col);
            return [.. Enumerable.Range(0, mask.Length).Where(i => mask[i] != 0)];
        }

        double[] positions = ToDoubles(verb, spec, line, col);
        return [.. positions.Select(p =>
        {
            int at = (int)p - 1;
            return at >= 0 && at < table.ColumnCount ? at
                : throw new JgsRuntimeException(line, col, "MATLAB:table:VarIndexOutOfRange",
                    $"Variable index exceeds table dimensions. Index must not exceed {table.ColumnCount}.");
        })];
    }

    private static int NamedVariable(Table table, string name, int line, int col)
    {
        for (int c = 0; c < table.ColumnCount; c++)
        {
            if (string.Equals(table[c].Name, name, StringComparison.Ordinal))
            {
                return c;
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:table:UnrecognizedVarName", $"Unrecognized table variable name '{name}'.");
    }

    /// <summary>One variable converted as <c>convertvars</c> converts it: by a class's name or by a function.</summary>
    private static JgsValue ConvertVariable(JgsValue value, string name, JgsValue type, Interpreter interpreter, int line, int col)
    {
        if (type.Type == JgsType.Function)
        {
            return type.AsCallable.Call([value], line, col);
        }

        string target = VerbText(type) ?? throw new JgsRuntimeException(line, col, "MATLAB:table:convertvars:InvalidTextType",
            $"Could not convert the type of the variable '{name}'. Specify the data type as the name of a class, or a function handle.");

        // A char variable is a cell of char rows in a table (probe_122).
        string verb = target == "char" ? "cellstr" : target;
        if (!interpreter.Globals.Builtins.TryGet(verb, out JgsValue function) || function.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:convertvars:InvalidTextType",
                $"Could not convert the type of the variable '{name}' to '{target}'. Specify the data type as the name of a class, or a function handle.");
        }

        try
        {
            return function.AsCallable.Call([value], line, col);
        }
        catch (JgsRuntimeException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:convertvars:VariableTypesConversionFailed",
                $"Could not convert the type of the variable '{name}' to '{target}'. The following error occurred when calling the {target} function.");
        }
    }

    // ---- join and innerjoin ----------------------------------------------------------------------

    private sealed record JoinKey(string[] Texts, double[]? Numbers);

    /// <summary>
    /// <c>join(L, R)</c> (every left row matched by exactly one right row, the left's order kept) or
    /// <c>innerjoin(L, R)</c> (every pair with equal keys, sorted by key), with their options.
    /// </summary>
    private static JgsValue[] Join(string verb, IReadOnlyList<JgsValue> args, bool inner, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Table || args[1].Type != JgsType.Table)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:table:{verb}:InvalidInput", "Inputs must be tables or timetables.");
        }

        Table left = args[0].AsTable;
        Table right = args[1].AsTable;
        var options = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i + 1 < args.Count; i += 2)
        {
            options[VerbText(args[i]) ?? throw new JgsRuntimeException(line, col, $"{verb}: option names are text.")] = args[i + 1];
        }

        int[] leftKeys;
        int[] rightKeys;
        if (options.TryGetValue("Keys", out JgsValue? keys))
        {
            leftKeys = KeyPositions(left, keys, line, col);
            rightKeys = KeyPositions(right, keys, line, col);
        }
        else if (options.TryGetValue("LeftKeys", out JgsValue? lk) && options.TryGetValue("RightKeys", out JgsValue? rk))
        {
            leftKeys = KeyPositions(left, lk, line, col);
            rightKeys = KeyPositions(right, rk, line, col);
        }
        else
        {
            string[] common = [.. left.ColumnNames.Where(n => right.ColumnNames.Contains(n, StringComparer.Ordinal))];
            if (common.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:join:CantInferKey", "Unable to find a common table variable to use as a key variable.");
            }

            leftKeys = [.. common.Select(n => NamedVariable(left, n, line, col))];
            rightKeys = [.. common.Select(n => NamedVariable(right, n, line, col))];
        }

        JgsValue[] leftValues = [.. left.Columns.Select(c => TableColumnValue(left, c.Name, line, col))];
        JgsValue[] rightValues = [.. right.Columns.Select(c => TableColumnValue(right, c.Name, line, col))];
        JoinKey[] leftKeyRows = KeyRows(leftValues, leftKeys, left.RowCount, leftValues);
        JoinKey[] rightKeyRows = KeyRows(rightValues, rightKeys, right.RowCount, leftKeys.Select(k => leftValues[k]).ToArray(), leftValuesForOrder: true);
        static string Joined(JoinKey key) => string.Join("\u0001", key.Texts);

        var pairs = new List<(int Left, int Right)>();
        if (inner)
        {
            var byKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int r = 0; r < right.RowCount; r++)
            {
                string k = Joined(rightKeyRows[r]);
                if (!byKey.TryGetValue(k, out List<int>? list))
                {
                    byKey[k] = list = [];
                }

                list.Add(r);
            }

            for (int l = 0; l < left.RowCount; l++)
            {
                if (byKey.TryGetValue(Joined(leftKeyRows[l]), out List<int>? matches))
                {
                    pairs.AddRange(matches.Select(r => (l, r)));
                }
            }

            pairs.Sort((a, b) =>
            {
                int byKeyOrder = CompareKeys(leftKeyRows[a.Left], leftKeyRows[b.Left]);
                return byKeyOrder != 0 ? byKeyOrder : a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right);
            });
        }
        else
        {
            var unique = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int r = 0; r < right.RowCount; r++)
            {
                if (!unique.TryAdd(Joined(rightKeyRows[r]), r))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:table:join:DuplicateRightKeyVarValues", "The key variable for the right table must have unique values.");
                }
            }

            for (int l = 0; l < left.RowCount; l++)
            {
                if (!unique.TryGetValue(Joined(leftKeyRows[l]), out int r))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:table:join:LeftKeyValueNotFound",
                        "The key variable for the right table must contain all values in the key variable for the left table.");
                }

                pairs.Add((l, r));
            }
        }

        // The variables: the left's (keys where they stand), then the right's other than its keys.
        int[] leftChosen = options.TryGetValue("LeftVariables", out JgsValue? lv) ? KeyPositions(left, lv, line, col) : [.. Enumerable.Range(0, left.ColumnCount)];
        int[] rightChosen = options.TryGetValue("RightVariables", out JgsValue? rv)
            ? KeyPositions(right, rv, line, col)
            : [.. Enumerable.Range(0, right.ColumnCount).Where(c => !rightKeys.Contains(c))];
        string[] keepOne = options.TryGetValue("KeepOneCopy", out JgsValue? keep) ? NameListOf(keep) : [];
        var leftNames = leftChosen.Select(c => left[c].Name).ToList();
        var rightNames = rightChosen.Select(c => right[c].Name).ToList();
        int[] leftRows = [.. pairs.Select(static p => p.Left)];
        int[] rightRows = [.. pairs.Select(static p => p.Right)];
        var columns = new List<TableColumn>();
        foreach (int c in leftChosen)
        {
            string name = left[c].Name;
            bool clash = rightNames.Contains(name, StringComparer.Ordinal) && !leftKeys.Contains(c) && !keepOne.Contains(name, StringComparer.Ordinal);
            columns.Add(TableColumnFrom(verb, clash ? name + "_left" : name, JgsValueColumn.RowsOfValue(leftValues[c], leftRows), line, col));
        }

        foreach (int c in rightChosen)
        {
            string name = right[c].Name;
            if (leftNames.Contains(name, StringComparer.Ordinal) && (keepOne.Contains(name, StringComparer.Ordinal) || leftKeys.Any(k => left[k].Name == name)))
            {
                continue;
            }

            bool clash = leftNames.Contains(name, StringComparer.Ordinal);
            columns.Add(TableColumnFrom(verb, clash ? name + "_right" : name, JgsValueColumn.RowsOfValue(rightValues[c], rightRows), line, col));
        }

        JgsValue joined = JgsValue.Table(new Table(columns));
        JgsValue Positions(int[] rows) => JgsMatrix.FromColumnMajorDims([.. rows.Select(static r => (double)(r + 1))], [rows.Length, 1]);
        return inner ? [joined, Positions(leftRows), Positions(rightRows)] : [joined, Positions(rightRows)];
    }

    private static int[] KeyPositions(Table table, JgsValue spec, int line, int col)
    {
        if (VerbText(spec) is { } one)
        {
            return [NamedVariable(table, one, line, col)];
        }

        if (spec.Type == JgsType.Cell || spec.IsStringArray)
        {
            return [.. NameListOf(spec).Select(n => NamedVariable(table, n, line, col))];
        }

        return [.. ToDoubles("join", spec, line, col).Select(static p => (int)p - 1)];
    }

    private static string[] NameListOf(JgsValue spec) =>
        VerbText(spec) is { } one ? [one]
        : spec.Type == JgsType.Cell ? [.. spec.AsCell.Select(static e => e.AsString)]
        : spec.IsStringArray ? [.. spec.BoxedElements().Select(static e => e.AsString)]
        : [];

    /// <summary>
    /// Each row's key: its text per key variable (what matches), and a number per key variable to
    /// sort by — a categorical's position among the left table's categories, a number itself — or
    /// null where the keys are text, which sorts as text.
    /// </summary>
    private static JoinKey[] KeyRows(JgsValue[] values, int[] keys, int rows, JgsValue[] orderValues, bool leftValuesForOrder = false)
    {
        var texts = new string[keys.Length][];
        var numbers = new double[keys.Length][];
        bool numeric = true;
        for (int k = 0; k < keys.Length; k++)
        {
            JgsValue value = values[keys[k]];
            texts[k] = new string[rows];
            numbers[k] = new double[rows];
            JgsCategoricalTag? order = (leftValuesForOrder ? orderValues[k] : value).CategoricalTag;
            if (value.IsCategorical)
            {
                string[] names = JgsCategorical.NamesOf(value);
                for (int r = 0; r < rows; r++)
                {
                    texts[k][r] = names[r] == JgsCategorical.Undefined ? "\u0002undefined" : names[r];
                    int at = order is null ? -1 : Array.IndexOf(order.Categories, names[r]);
                    numbers[k][r] = at < 0 ? double.PositiveInfinity : at;
                }
            }
            else if (value.Type == JgsType.Cell || value.IsStringArray)
            {
                JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
                for (int r = 0; r < rows; r++)
                {
                    texts[k][r] = elements[r].AsString;
                }

                numeric = false;
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    double number = value.ArrayLength == 1 && value.Type != JgsType.Array ? value.AsNumber : value.ElementAt(r).AsNumber;
                    texts[k][r] = number.ToString("R", CultureInfo.InvariantCulture);
                    numbers[k][r] = number;
                }
            }
        }

        var rowKeys = new JoinKey[rows];
        for (int r = 0; r < rows; r++)
        {
            rowKeys[r] = new JoinKey([.. texts.Select(t => t[r])], numeric ? [.. numbers.Select(n => n[r])] : null);
        }

        return rowKeys;
    }

    private static int CompareKeys(JoinKey a, JoinKey b)
    {
        for (int k = 0; k < a.Texts.Length; k++)
        {
            int order = a.Numbers is not null && b.Numbers is not null
                ? a.Numbers[k].CompareTo(b.Numbers[k])
                : string.CompareOrdinal(a.Texts[k], b.Texts[k]);
            if (order != 0)
            {
                return order;
            }
        }

        return 0;
    }

    // ---- stack -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>stack(T, vars)</c>: each row once per stacked variable, the other variables repeated, an
    /// indicator naming the variable the value came from, and the values in one variable
    /// (probe_122). The second answer is the row of <c>T</c> each row came from.
    /// </summary>
    private static JgsValue[] Stack(IReadOnlyList<JgsValue> args, Interpreter interpreter, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Table)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:stack:InvalidInput", "The first input must be a table.");
        }

        Table table = args[0].AsTable;
        int[] stacked = VariableIndices("stack", table, args[1], interpreter, line, col);
        var options = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i + 1 < args.Count; i += 2)
        {
            options[VerbText(args[i]) ?? throw new JgsRuntimeException(line, col, "stack: option names are text.")] = args[i + 1];
        }

        string[] stackedNames = [.. stacked.Select(c => table[c].Name)];
        string dataName = options.TryGetValue("NewDataVariableName", out JgsValue? newName) && VerbText(newName) is { } given ? given : string.Join("_", stackedNames);
        string indicatorName = options.TryGetValue("IndexVariableName", out JgsValue? indexName) && VerbText(indexName) is { } named ? named : dataName + "_Indicator";
        int[] constants = options.TryGetValue("ConstantVariables", out JgsValue? constant)
            ? (constant.Type == JgsType.Cell && constant.AsCell.Length == 0 ? [] : VariableIndices("stack", table, constant, interpreter, line, col))
            : [.. Enumerable.Range(0, table.ColumnCount).Where(c => !stacked.Contains(c))];

        JgsValue[] stackedValues = [.. stacked.Select(c => TableColumnValue(table, table[c].Name, line, col))];
        string firstClass = ClassOf(stackedValues[0], JgsDialect.Matlab);
        for (int j = 1; j < stackedValues.Length; j++)
        {
            string cls = ClassOf(stackedValues[j], JgsDialect.Matlab);
            bool bothNumeric = VariableIsOfType(stackedValues[0], "numeric") && VariableIsOfType(stackedValues[j], "numeric");
            if (cls != firstClass && !bothNumeric)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:stack:InterleavingDataVarsFailed",
                    $"Error when stacking data variable '{stackedNames[j]}'.");
            }
        }

        int rows = table.RowCount;
        int k = stacked.Length;
        int[] source = new int[rows * k];
        var pieces = new List<JgsValue>(rows * k);
        var indicator = new double[rows * k];
        for (int r = 0; r < rows; r++)
        {
            for (int j = 0; j < k; j++)
            {
                source[(r * k) + j] = r;
                indicator[(r * k) + j] = j + 1;
                pieces.Add(JgsValueColumn.RowsOfValue(stackedValues[j], [r]));
            }
        }

        var columns = new List<TableColumn>();
        foreach (int c in constants)
        {
            columns.Add(TableColumnFrom("stack", table[c].Name, JgsValueColumn.RowsOfValue(TableColumnValue(table, table[c].Name, line, col), source), line, col));
        }

        var tag = new JgsCategoricalTag(stackedNames, false, false);
        columns.Add(TableColumnFrom("stack", indicatorName, JgsCategorical.FromCodes(indicator, indicator.Length, 1, tag), line, col));
        JgsValue data = pieces.Count == 0
            ? JgsValueColumn.RowsOfValue(stackedValues[0], [])
            : interpreter.JoinAsBracket(pieces, across: false, line, col);
        columns.Add(TableColumnFrom("stack", dataName, data, line, col));

        JgsValue result = JgsValue.Table(new Table(columns));
        return [result, JgsMatrix.FromColumnMajorDims([.. source.Select(static r => (double)(r + 1))], [source.Length, 1])];
    }
}
