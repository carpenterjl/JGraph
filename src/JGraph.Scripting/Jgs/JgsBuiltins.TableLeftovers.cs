using System.Globalization;
using System.Text;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The table leftovers of open item 128, measured on R2025b (probe_128, probe_128b, probe_128c and
/// probe_128d in the batch 21 scratch): the missing-value verbs answering per variable over a table
/// (<c>ismissing</c> with and without indicators, <c>anymissing</c>, <c>fillmissing</c> with its
/// methods, constants, <c>DataVariables</c> and <c>ReplaceValues</c>, <c>rmmissing</c> over rows or
/// variables, <c>standardizeMissing</c>), <c>summary</c>'s whole struct and its display, the
/// <c>convertvars</c> warning for <c>'char'</c>, and what a table and a <c>vartype</c> list as their
/// properties. MATLAB dialect only: a JGS script keeps the verbs it had.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>What a table variable is, for the questions the missing-value verbs ask of it.</summary>
    private enum VariableKind
    {
        Float,
        Integer,
        Logical,
        Categorical,
        Cellstr,
        Text,
        Char,
        Time,
        Other,
    }

    /// <summary>The values an indicator names: numbers for numeric variables, text for the text kinds.</summary>
    private sealed record MissingIndicators(double[] Numbers, string[] Texts);

    private const string ConvertCharWarning =
        "Converting to data type 'char' converts variables into cell arrays of character vectors rather than a character array. "
        + "Storing text data as character arrays in a table is not recommended. When working with text data in a table or a "
        + "timetable, consider using a string array or a categorical array.";

    private const string PreallocateCharWarning =
        "Preallocation using data type 'char' creates a table variable that is a cell array of character vectors rather than a "
        + "character array. Storing text data as character arrays in a table is not recommended. When working with text data in "
        + "a table or a timetable, consider using a string array or a categorical array.";

    /// <summary>
    /// The display <c>disp</c> and an echo give a <c>vartype</c> subscript in R2025b (probe_128b): the
    /// lines under the name, each ending in a newline, the last followed by a blank line.
    /// </summary>
    internal static string VartypeDisplay(JgsValue vartype) =>
        "\ttable <a href=\"matlab:doc('vartype')\">vartype</a> subscript:\n\n"
        + $"\t\tSelect table variables matching the type '{vartype.AsStruct["Type"].AsString}'\n\n"
        + "\tSee <a href=\"matlab:helpview(fullfile(docroot,'matlab','matlab_prog','access-data-in-a-table.html'))\">Access Data in a Table</a>.\n\n";

    /// <summary>Registers the table leftovers over the verbs they teach (open item 128).</summary>
    internal static void RegisterTableLeftovers(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        // A verb that already exists learns the table case in the MATLAB dialect; everything else
        // goes to the verb as it was, outputs and all. String scalars arrive as written, because a
        // fill constant's kind is part of the answer ("q" is not 'q' to a cellstr variable).
        void Wrap(string name, Func<IJgsCallable, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> tables, bool takesOutputCount = false)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable inner = existing.AsCallable;

            // A verb that prints when its answer is discarded (methods, properties) or takes the output
            // count is asked as it was; any other is asked for one answer at least.
            bool countsOutputs = inner is BuiltinFunction { TakesOutputCount: true } or BuiltinFunction { KnowsWhenDiscarded: true };
            JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                (JgsRunningDialect.ThreadIsMatlab ? tables(inner, args, wanted, line, col) : null)
                ?? (inner is IJgsMultiCallable multi
                    ? multi.CallMultiple(args, countsOutputs ? wanted : Math.Max(wanted, 1), line, col)
                    : [inner.Call(args, line, col)]);
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                JgsValue[] answers = Both(args, 1, line, col);
                return answers.Length > 0 ? answers[0] : JgsValue.Null;
            })
            {
                MultiOutput = Both,
                BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
                AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
                KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },
                TakesOutputCount = takesOutputCount || inner is BuiltinFunction { TakesOutputCount: true },
                KeepsStringArguments = true,
            }));
        }

        Wrap("ismissing", (inner, args, wanted, line, col) =>
            args.Count is 1 or 2 && args[0].Type == JgsType.Table
                ? [TableIsMissing(args[0].AsTable, args.Count == 2 ? IndicatorsOf(args[1]) : null, line, col)]
                : null);
        Wrap("anymissing", (inner, args, wanted, line, col) =>
            args.Count == 1 && args[0].Type == JgsType.Table
                ? [JgsValue.Bool(TableMissingRows(args[0].AsTable, null, line, col).Any(static column => column.Any(static m => m)))]
                : null);
        Wrap("fillmissing", (inner, args, wanted, line, col) => FillMissingTable(inner, interpreter, args, line, col));
        Wrap("rmmissing", (inner, args, wanted, line, col) => RemoveMissingTable(interpreter, args, line, col));
        Wrap("standardizeMissing", (inner, args, wanted, line, col) => StandardizeMissingTable(interpreter, args, line, col));

        // summary over a table answers R2025b's whole struct when asked, and prints its display when
        // not (probe_128, probe_128d): no ans is made.
        Wrap("summary", (inner, args, wanted, line, col) =>
        {
            if (args.Count != 1 || args[0].Type != JgsType.Table)
            {
                return null;
            }

            if (wanted == 0)
            {
                string? named = interpreter.PendingCall?.Arguments is [VariableExpr variable] ? variable.Name : null;
                host.print(TableSummaryText(args[0].AsTable, named, line, col));
                return [];
            }

            return [TableSummaryMatlab(args[0].AsTable, line, col)];
        }, takesOutputCount: true);

        // convertvars to 'char' warns before it converts, as R2025b's does (probe_128).
        Wrap("convertvars", (inner, args, wanted, line, col) =>
        {
            if (args.Count == 3 && args[0].Type == JgsType.Table && VerbText(args[2]) == "char")
            {
                Warn(host, "MATLAB:table:convertvars:ConvertCharWarning", ConvertCharWarning);
            }

            return null;
        });

        // A table lists its variables and the three names a dot also answers to; a vartype lists none
        // (probe_128, probe_128d).
        foreach (string lister in (string[])["properties", "fieldnames"])
        {
            Wrap(lister, (inner, args, wanted, line, col) =>
            {
                if (args.Count != 1)
                {
                    return null;
                }

                if (IsVartype(args[0]))
                {
                    JgsValue none = JgsValue.Cell([]);
                    none.Reshape(0, 1);
                    return [none];
                }

                if (args[0].Type == JgsType.Table)
                {
                    Table table = args[0].AsTable;
                    IReadOnlyList<string> dimensions = table.DimensionNames ?? [table.RowTimes?.Name ?? "Row", "Variables"];
                    return [CellColumn((string[])[.. table.ColumnNames, "Properties", dimensions[0], dimensions[1]])];
                }

                return null;
            });
        }

        // A vartype is one of R2025b's table subscripters, with three methods (probe_128d).
        Wrap("isa", (inner, args, wanted, line, col) =>
            args.Count == 2 && IsVartype(args[0]) && VerbText(args[1]) is "matlab.internal.tabular.private.subscripter" ? [JgsValue.Bool(true)] : null);
        Wrap("ismethod", (inner, args, wanted, line, col) =>
            args.Count == 2 && IsVartype(args[0]) && VerbText(args[1]) is { } method ? [JgsValue.Bool(method is "keyHash" or "keyMatch" or "vartype")] : null);
        Wrap("methods", (inner, args, wanted, line, col) =>
            args.Count == 1 && wanted >= 1 && IsVartype(args[0]) ? [CellColumn((string[])["keyHash", "keyMatch", "vartype"])] : null);

        // table('Size', sz, 'VariableTypes', types, ...) preallocates (probe_128c); the char type warns.
        Wrap("table", (inner, args, wanted, line, col) =>
            PreallocationSizeAt(args) is int at ? [PreallocatedTable(args, at, host, line, col)] : null);
    }

    // --- what a variable is ---------------------------------------------------------------------

    private static VariableKind KindOfVariable(JgsValue value)
    {
        if (value.IsCategorical)
        {
            return VariableKind.Categorical;
        }

        if (value.IsDatetime || value.IsDuration)
        {
            return VariableKind.Time;
        }

        if (value.IsStringArray)
        {
            return VariableKind.Text;
        }

        if (value.IsCharMatrix || value.Type == JgsType.String)
        {
            return VariableKind.Char;
        }

        if (value.Type == JgsType.Cell)
        {
            return value.AsCell.All(static e => e.Type == JgsType.String && !e.IsStringArray) ? VariableKind.Cellstr : VariableKind.Other;
        }

        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Array or JgsType.Complex)
        {
            string cls = ClassOf(value, JgsDialect.Matlab);
            return cls switch
            {
                "double" or "single" => VariableKind.Float,
                "logical" => VariableKind.Logical,
                _ when IntegerClassNames.Contains(cls, StringComparer.Ordinal) => VariableKind.Integer,
                _ => VariableKind.Other,
            };
        }

        return VariableKind.Other;
    }

    /// <summary>A variable's rows and the width of each row (a char matrix's row is one element).</summary>
    private static (int Rows, int Width) VariableShape(JgsValue value, VariableKind kind, int tableRows)
    {
        if (kind == VariableKind.Char)
        {
            return (tableRows, 1);
        }

        int count = value.Type switch
        {
            JgsType.Array => value.ArrayLength,
            JgsType.Cell => value.AsCell.Length,
            JgsType.Struct => value.AsStructArray.Length,
            _ => 1,
        };
        return (tableRows, tableRows == 0 ? Math.Max(value.Cols, 0) : count / tableRows);
    }

    /// <summary>The text of a char row of a variable, or of a char-row variable.</summary>
    private static string CharRow(JgsValue value, int row) =>
        value.IsCharMatrix ? value.CharMatrixRows()[row] : value.Type == JgsType.String ? value.AsString : string.Empty;

    /// <summary>Text elements of a value, by kind: a cellstr's chars, a string array's texts (null for missing).</summary>
    private static string?[] TextElements(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            return [.. value.AsCell.Select(static e => (string?)e.AsString)];
        }

        var texts = new string?[value.ArrayLength];
        for (int i = 0; i < texts.Length; i++)
        {
            string text = value.ElementAt(i).AsString;
            texts[i] = text == MissingSentinel ? null : text;
        }

        return texts;
    }

    /// <summary>
    /// Whether each element of a variable is missing, column-major: NaN among floats, an undefined
    /// category, an empty char in a cellstr, the missing string, a row of blanks in a char variable,
    /// a missing time; never for integers, logicals and the rest. With indicators, whether each is one
    /// of them instead - numbers for the numeric kinds, text for the text kinds, where '' also names
    /// an undefined category (probe_128c).
    /// </summary>
    private static bool[] ElementMissing(JgsValue value, VariableKind kind, int rows, MissingIndicators? indicators)
    {
        switch (kind)
        {
            case VariableKind.Float or VariableKind.Integer or VariableKind.Logical:
            {
                if (indicators is null && kind != VariableKind.Float)
                {
                    return new bool[ElementsOfVariable(value)];
                }

                int count = ElementsOfVariable(value);
                var missing = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    JgsValue element = value.Type == JgsType.Array ? value.ElementAt(i) : value;
                    double x = element.Type == JgsType.Complex ? element.AsComplex.Real : element.AsNumber;
                    bool nan = double.IsNaN(x) || (element.Type == JgsType.Complex && double.IsNaN(element.AsComplex.Imaginary));
                    missing[i] = indicators is null ? nan : indicators.Numbers.Any(n => n.Equals(x) || (double.IsNaN(n) && nan));
                }

                return missing;
            }

            case VariableKind.Categorical:
            {
                double[] codes = JgsCategorical.CodesOf(value);
                JgsCategoricalTag tag = value.CategoricalTag!;
                return [.. codes.Select(code => indicators is null
                    ? double.IsNaN(code)
                    : double.IsNaN(code) ? indicators.Texts.Contains(string.Empty) : indicators.Texts.Contains(JgsCategorical.NameOf(tag, code), StringComparer.Ordinal))];
            }

            case VariableKind.Cellstr:
                return [.. TextElements(value).Select(text => indicators is null ? text!.Length == 0 : indicators.Texts.Contains(text!, StringComparer.Ordinal))];

            case VariableKind.Text:
                return [.. TextElements(value).Select(text => indicators is null ? text is null : text is not null && indicators.Texts.Contains(text, StringComparer.Ordinal))];

            case VariableKind.Char:
            {
                var missing = new bool[rows];
                for (int r = 0; r < rows; r++)
                {
                    string row = CharRow(value, r);
                    missing[r] = indicators is null ? row.Trim(' ').Length == 0 : indicators.Texts.Contains(row, StringComparer.Ordinal);
                }

                return missing;
            }

            case VariableKind.Time:
                return indicators is null ? [.. TimeMs(value).Select(double.IsNaN)] : new bool[value.ArrayLength];

            default:
                return new bool[Math.Max(rows, 0)];
        }
    }

    private static int ElementsOfVariable(JgsValue value) => value.Type == JgsType.Array ? value.ArrayLength : 1;

    /// <summary>Each row of a variable: whether any element in it is missing (a zero-width variable has none).</summary>
    private static bool[] RowsAnyMissing(bool[] elements, int rows)
    {
        var any = new bool[rows];
        if (rows == 0)
        {
            return any;
        }

        for (int i = 0; i < elements.Length; i++)
        {
            any[i % rows] |= elements[i];
        }

        return any;
    }

    /// <summary>The indicators a value names: numbers and text, from a cell of either, an array, or text.</summary>
    private static MissingIndicators IndicatorsOf(JgsValue indicator)
    {
        var numbers = new List<double>();
        var texts = new List<string>();
        void Take(JgsValue value)
        {
            if (value.IsCategorical)
            {
                texts.AddRange(JgsCategorical.NamesOf(value));
            }
            else if (value.IsStringArray)
            {
                texts.AddRange(TextElements(value).Where(static t => t is not null)!);
            }
            else if (value.IsCharMatrix)
            {
                texts.AddRange(value.CharMatrixRows());
            }
            else if (value.Type == JgsType.String)
            {
                texts.Add(value.AsString);
            }
            else if (value.Type is JgsType.Number or JgsType.Bool)
            {
                numbers.Add(value.AsNumber);
            }
            else if (value.Type == JgsType.Array)
            {
                for (int i = 0; i < value.ArrayLength; i++)
                {
                    numbers.Add(value.ElementAt(i).AsNumber);
                }
            }
        }

        if (indicator.Type == JgsType.Cell)
        {
            foreach (JgsValue element in indicator.AsCell)
            {
                Take(element);
            }
        }
        else
        {
            Take(indicator);
        }

        return new MissingIndicators([.. numbers], [.. texts]);
    }

    /// <summary>Each variable's rows, whether any element in the row is missing (or one of the indicators).</summary>
    private static bool[][] TableMissingRows(Table table, MissingIndicators? indicators, int line, int col)
    {
        var columns = new bool[table.ColumnCount][];
        for (int v = 0; v < columns.Length; v++)
        {
            JgsValue value = TableColumnValue(table, table[v].Name, line, col);
            VariableKind kind = KindOfVariable(value);
            columns[v] = RowsAnyMissing(ElementMissing(value, kind, table.RowCount, indicators), table.RowCount);
        }

        return columns;
    }

    /// <summary><c>ismissing(T)</c>, <c>ismissing(T, indicators)</c>: a logical of the table's size.</summary>
    private static JgsValue TableIsMissing(Table table, MissingIndicators? indicators, int line, int col)
    {
        bool[][] columns = TableMissingRows(table, indicators, line, col);
        var flags = new bool[table.RowCount * columns.Length];
        for (int v = 0; v < columns.Length; v++)
        {
            columns[v].CopyTo(flags, v * table.RowCount);
        }

        return LogicalMask(flags, table.RowCount, columns.Length);
    }

    /// <summary>The variables a <c>'DataVariables'</c> value names, refused in the verb's own words.</summary>
    private static int[] DataVariablesOf(string verb, Table table, JgsValue spec, Interpreter interpreter, int line, int col)
    {
        try
        {
            return VariableIndices(verb, table, spec, interpreter, line, col);
        }
        catch (JgsRuntimeException)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:DataVariablesTableSubscript", "Invalid 'DataVariables' value.");
        }
    }

    /// <summary>The option pairs after the positional arguments: the named ones taken out, the rest handed on.</summary>
    private static (Dictionary<string, JgsValue> Named, List<JgsValue> Others) TableOptions(
        IReadOnlyList<JgsValue> args, int from, IReadOnlyCollection<string> names)
    {
        var named = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
        var rest = new List<JgsValue>();
        for (int i = from; i < args.Count; i++)
        {
            if (i + 1 < args.Count && VerbText(args[i]) is { } option && names.FirstOrDefault(n => n.Equals(option, StringComparison.OrdinalIgnoreCase)) is { } known)
            {
                named[known] = args[i + 1];
                i++;
                continue;
            }

            rest.Add(args[i]);
        }

        return (named, rest);
    }

    // --- fillmissing over a table -----------------------------------------------------------------

    private static readonly string[] InterpolatingFills = ["linear", "spline", "pchip", "makima", "movmean", "movmedian"];

    /// <summary>
    /// <c>[F, tf] = fillmissing(T, method, ...)</c> (probe_128, probe_128c): each selected variable
    /// filled by its kind - numbers by the numeric verb with every option it takes, the other kinds
    /// from their neighbouring rows or a constant of their own kind - and the table rebuilt, or the
    /// filled variables added as <c>name_filled</c> when <c>'ReplaceValues'</c> is false.
    /// </summary>
    private static JgsValue[]? FillMissingTable(IJgsCallable inner, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Table)
        {
            return null;
        }

        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        Table table = args[0].AsTable;
        string method = VerbText(args[1])?.ToLowerInvariant()
            ?? throw new JgsRuntimeException(line, col, "fillmissing: the fill method is a name, such as 'previous' or 'constant'.");
        bool takesParameter = method is "constant" or "movmean" or "movmedian";
        if (takesParameter && args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsValue? parameter = takesParameter ? args[2] : null;
        (Dictionary<string, JgsValue> named, List<JgsValue> handedOn) = TableOptions(args, takesParameter ? 3 : 2, ["DataVariables", "ReplaceValues"]);
        int[] selected = named.TryGetValue("DataVariables", out JgsValue? spec)
            ? DataVariablesOf("fillmissing", table, spec, interpreter, line, col)
            : [.. Enumerable.Range(0, table.ColumnCount)];
        bool replace = !named.TryGetValue("ReplaceValues", out JgsValue? replaceValue) || replaceValue.AsNumber != 0;

        JgsValue[] values = [.. table.Columns.Select(c => TableColumnValue(table, c.Name, line, col))];
        VariableKind[] kinds = [.. values.Select(KindOfVariable)];

        // Every selected variable is asked before any is filled: an interpolation over a kind it does
        // not reach, or a constant of the wrong kind, names the first such variable.
        if (Array.IndexOf(InterpolatingFills, method) >= 0
            && selected.Any(v => kinds[v] is not (VariableKind.Float or VariableKind.Integer or VariableKind.Logical or VariableKind.Time)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fillmissing:InterpolationInvalidTableVariable",
                $"Table contains variables of types for which the '{method}' fill method is not supported.");
        }

        JgsValue[] constants = [];
        if (method == "constant")
        {
            constants = ConstantsOf(parameter!);
            if (constants.Length != 1 && constants.Length != selected.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fillmissing:SizeConstant",
                    $"Fill constant must be a scalar or a {selected.Length}-element vector.");
            }

            for (int k = 0; k < selected.Length; k++)
            {
                int v = selected[k];
                if (!FitsConstant(kinds[v], constants[constants.Length == 1 ? 0 : k], values[v]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:fillmissing:ConstantInvalidTypeForTableVariable",
                        $"Invalid fill constant type for table variable {table[v].Name}.");
                }
            }
        }

        var filled = new JgsValue[selected.Length];
        var filledRows = new bool[selected.Length][];
        for (int k = 0; k < selected.Length; k++)
        {
            int v = selected[k];
            JgsValue constant = method == "constant" ? constants[constants.Length == 1 ? 0 : k] : JgsValue.Null;
            (filled[k], filledRows[k]) = FillVariable(inner, values[v], kinds[v], table.RowCount, method, parameter, constant, handedOn, line, col);
        }

        var columns = new List<TableColumn>(table.Columns);
        var flags = new List<bool[]>();
        for (int v = 0; v < table.ColumnCount; v++)
        {
            int k = Array.IndexOf(selected, v);
            flags.Add(replace && k >= 0 ? filledRows[k] : new bool[table.RowCount]);
            if (replace && k >= 0)
            {
                columns[v] = TableColumnFrom("fillmissing", table[v].Name, filled[k], line, col);
            }
        }

        if (!replace)
        {
            for (int k = 0; k < selected.Length; k++)
            {
                columns.Add(TableColumnFrom("fillmissing", table[selected[k]].Name + "_filled", filled[k], line, col));
                flags.Add(filledRows[k]);
            }
        }

        var mask = new bool[table.RowCount * flags.Count];
        for (int c = 0; c < flags.Count; c++)
        {
            flags[c].CopyTo(mask, c * table.RowCount);
        }

        return [JgsValue.Table(table.WithColumns(columns)), LogicalMask(mask, table.RowCount, flags.Count)];
    }

    /// <summary>The fill constants a parameter holds: a cell's elements, an array's, or the one value.</summary>
    private static JgsValue[] ConstantsOf(JgsValue parameter)
    {
        if (parameter.Type == JgsType.Cell)
        {
            return parameter.AsCell;
        }

        if (parameter.IsStringArray && parameter.ArrayLength > 1)
        {
            return [.. Enumerable.Range(0, parameter.ArrayLength).Select(i => JgsValue.StringScalar(parameter.ElementAt(i).AsString))];
        }

        if (parameter.Type == JgsType.Array && !parameter.IsStringArray && !parameter.IsCharMatrix && !parameter.IsCategorical
            && !parameter.IsTime && parameter.ArrayLength > 1)
        {
            return [.. Enumerable.Range(0, parameter.ArrayLength).Select(parameter.ElementAt)];
        }

        return [parameter];
    }

    /// <summary>Whether a fill constant is of a kind the variable takes (probe_128c).</summary>
    private static bool FitsConstant(VariableKind kind, JgsValue constant, JgsValue variable)
    {
        bool number = constant.Type is JgsType.Number or JgsType.Bool
            || (constant.Type == JgsType.Array && constant.ArrayLength == 1 && !constant.IsStringArray && !constant.IsCharMatrix
                && !constant.IsCategorical && !constant.IsTime);
        bool chars = constant.Type == JgsType.String && !constant.IsStringArray;
        bool stringScalar = IsStringScalar(constant);
        return kind switch
        {
            VariableKind.Float or VariableKind.Integer or VariableKind.Logical => number,
            VariableKind.Categorical => constant.IsCategorical || chars || stringScalar,
            VariableKind.Cellstr => chars,
            VariableKind.Text => stringScalar,
            VariableKind.Char => chars,
            VariableKind.Time => constant.IsTime && constant.TimeTag!.Kind == variable.TimeTag!.Kind,
            _ => false,
        };
    }

    /// <summary>One variable filled, and which of its rows were.</summary>
    private static (JgsValue Filled, bool[] Rows) FillVariable(
        IJgsCallable inner, JgsValue value, VariableKind kind, int rows, string method, JgsValue? parameter, JgsValue constant,
        List<JgsValue> handedOn, int line, int col)
    {
        bool[] elements = ElementMissing(value, kind, rows, null);
        if (rows == 0 || !elements.Any(static m => m))
        {
            return (value, new bool[rows]);
        }

        // Numbers, and times between their neighbours, take the numeric verb with everything it takes.
        if (kind == VariableKind.Float || (kind == VariableKind.Time && Array.IndexOf(InterpolatingFills, method) >= 0))
        {
            var call = new List<JgsValue> { value, JgsValue.Str(method) };
            if (parameter is not null)
            {
                call.Add(method == "constant" ? constant : parameter);
            }

            call.AddRange(handedOn);
            JgsValue[] answers = inner is IJgsMultiCallable multi ? multi.CallMultiple(call, 2, line, col) : [inner.Call(call, line, col)];
            bool[] changed = answers.Length > 1 ? Flags(answers[1]) : elements;
            return (answers[0], RowsAnyMissing(changed, rows));
        }

        bool[] missing = RowsAnyMissing(elements, rows);
        if (method == "constant")
        {
            return (FilledWithConstant(value, kind, missing, constant, line, col), missing);
        }

        if (method is not ("previous" or "next" or "nearest"))
        {
            throw new JgsRuntimeException(line, col, $"fillmissing: '{method}' is not a fill method for a {ClassOf(value, JgsDialect.Matlab)} variable.");
        }

        int[] sources = FillSources(missing, method);
        var changedRows = new bool[rows];
        for (int r = 0; r < rows; r++)
        {
            changedRows[r] = sources[r] != r;
        }

        return (JgsValueColumn.RowsOfValue(value, sources), changedRows);
    }

    /// <summary>The elements of a logical answer, column-major.</summary>
    private static bool[] Flags(JgsValue mask) =>
        mask.Type == JgsType.Array ? [.. Enumerable.Range(0, mask.ArrayLength).Select(i => mask.ElementAt(i).AsNumber != 0)] : [mask.AsNumber != 0];

    /// <summary>
    /// The row each row takes its value from: itself, or for a missing row the nearest present one
    /// before, after, or either way (a tie goes to the one after, probe_128: [1 NaN 3] fills 3).
    /// </summary>
    private static int[] FillSources(bool[] missing, string method)
    {
        int n = missing.Length;
        var before = new int[n];
        var after = new int[n];
        int last = -1;
        for (int r = 0; r < n; r++)
        {
            before[r] = missing[r] ? last : r;
            if (!missing[r])
            {
                last = r;
            }
        }

        last = -1;
        for (int r = n - 1; r >= 0; r--)
        {
            after[r] = missing[r] ? last : r;
            if (!missing[r])
            {
                last = r;
            }
        }

        var sources = new int[n];
        for (int r = 0; r < n; r++)
        {
            int source = method switch
            {
                "previous" => before[r],
                "next" => after[r],
                _ => before[r] < 0 ? after[r]
                    : after[r] < 0 ? before[r]
                    : r - before[r] < after[r] - r ? before[r] : after[r],
            };
            sources[r] = source < 0 ? r : source;
        }

        return sources;
    }

    /// <summary>A non-numeric variable with its missing rows set to a constant of its own kind.</summary>
    private static JgsValue FilledWithConstant(JgsValue value, VariableKind kind, bool[] missing, JgsValue constant, int line, int col)
    {
        switch (kind)
        {
            case VariableKind.Categorical:
            {
                string?[] name = constant.IsCategorical ? [.. JgsCategorical.NamesOf(constant).Take(1)] : JgsCategorical.TextNames(constant) ?? [];
                (double[] fillCodes, JgsCategoricalTag tag) = JgsCategorical.Absorb(value.CategoricalTag!, name, line, col);
                double[] codes = JgsCategorical.Remap(JgsCategorical.CodesOf(value), value.CategoricalTag!, tag);
                for (int i = 0; i < codes.Length; i++)
                {
                    if (double.IsNaN(codes[i]))
                    {
                        codes[i] = fillCodes.Length > 0 ? fillCodes[0] : double.NaN;
                    }
                }

                return JgsCategorical.FromCodesLike(codes, value, tag);
            }

            case VariableKind.Cellstr:
            {
                JgsValue[] cells = [.. value.AsCell.Select(static e => JgsValue.Share(e))];
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].AsString.Length == 0)
                    {
                        cells[i] = JgsValue.Str(constant.AsString);
                    }
                }

                JgsValue cell = JgsValue.Cell(cells);
                cell.TakeShapeOf(value);
                return cell;
            }

            case VariableKind.Text:
            {
                string text = constant.ElementAt(0).AsString;
                JgsValue[] texts = [.. Enumerable.Range(0, value.ArrayLength).Select(i =>
                    value.ElementAt(i).AsString == MissingSentinel ? JgsValue.Str(text) : value.ElementAt(i))];
                JgsValue strings = JgsValue.Array(texts);
                strings.TakeShapeOf(value);
                return strings.MarkStringArray();
            }

            case VariableKind.Time:
            {
                double fill = TimeMs(constant)[0];
                double[] ms = [.. TimeMs(value).Select(x => double.IsNaN(x) ? fill : x)];
                return JgsMatrix.FromColumnMajorDims(ms, value.Dims).MarkTime(value.TimeTag!);
            }

            default:
                return value;
        }
    }

    // --- rmmissing and standardizeMissing over a table ---------------------------------------------

    /// <summary>
    /// <c>[R, tf] = rmmissing(T, dim, 'MinNumMissing', k, 'DataVariables', v)</c> (probe_128c): the
    /// rows with at least k missing variables among the selected ones, or with dim 2 the selected
    /// variables with at least k missing rows, taken out; row names go with their rows.
    /// </summary>
    private static JgsValue[]? RemoveMissingTable(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Table)
        {
            return null;
        }

        Table table = args[0].AsTable;
        int dim = 1;
        int from = 1;
        if (args.Count > 1 && args[1].Type is JgsType.Number or JgsType.Bool)
        {
            dim = (int)args[1].AsNumber;
            from = 2;
            if (dim is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:rmmissing:DimensionInvalid", "Dimension argument must be 1 or 2.");
            }
        }

        (Dictionary<string, JgsValue> named, List<JgsValue> rest) = TableOptions(args, from, ["MinNumMissing", "DataVariables"]);
        if (rest.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "rmmissing over a table takes a dimension, 'MinNumMissing' and 'DataVariables'.");
        }

        int least = named.TryGetValue("MinNumMissing", out JgsValue? minimum) ? Math.Max((int)minimum.AsNumber, 1) : 1;
        int[] selected = named.TryGetValue("DataVariables", out JgsValue? spec)
            ? DataVariablesOf("rmmissing", table, spec, interpreter, line, col)
            : [.. Enumerable.Range(0, table.ColumnCount)];
        bool[][] missing = TableMissingRows(table, null, line, col);

        if (dim == 2)
        {
            var gone = new bool[table.ColumnCount];
            foreach (int v in selected)
            {
                gone[v] = missing[v].Count(static m => m) >= least;
            }

            int[] kept = [.. Enumerable.Range(0, table.ColumnCount).Where(v => !gone[v])];
            return [JgsValue.Table(table.Select([.. Enumerable.Range(0, table.RowCount)], kept)), LogicalMask(gone, 1, gone.Length)];
        }

        var removed = new bool[table.RowCount];
        for (int r = 0; r < table.RowCount; r++)
        {
            removed[r] = selected.Count(v => missing[v][r]) >= least;
        }

        int[] rows = [.. Enumerable.Range(0, table.RowCount).Where(r => !removed[r])];
        return [JgsValue.Table(table.Select(rows, [.. Enumerable.Range(0, table.ColumnCount)])), LogicalMask(removed, removed.Length, 1)];
    }

    /// <summary>
    /// <c>standardizeMissing(T, indicators, 'DataVariables', v)</c> (probe_128c): in each selected
    /// variable, the elements an indicator of its kind names become that kind's missing value - NaN,
    /// an undefined category, the empty char, the missing string.
    /// </summary>
    private static JgsValue[]? StandardizeMissingTable(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Table)
        {
            return null;
        }

        Table table = args[0].AsTable;
        (Dictionary<string, JgsValue> named, List<JgsValue> rest) = TableOptions(args, 2, ["DataVariables"]);
        if (rest.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "standardizeMissing: past the indicator comes 'DataVariables' and the variables it names.");
        }

        MissingIndicators indicators = IndicatorsOf(args[1]);
        int[] selected = named.TryGetValue("DataVariables", out JgsValue? spec)
            ? DataVariablesOf("standardizeMissing", table, spec, interpreter, line, col)
            : [.. Enumerable.Range(0, table.ColumnCount)];
        var columns = new List<TableColumn>(table.Columns);
        foreach (int v in selected)
        {
            JgsValue value = TableColumnValue(table, table[v].Name, line, col);
            VariableKind kind = KindOfVariable(value);
            if (kind is not (VariableKind.Float or VariableKind.Categorical or VariableKind.Cellstr or VariableKind.Text))
            {
                continue;
            }

            bool[] named_ = ElementMissing(value, kind, table.RowCount, indicators);
            if (!named_.Any(static m => m))
            {
                continue;
            }

            JgsValue standardized = kind switch
            {
                VariableKind.Float => StandardizedFloats(value, named_),
                VariableKind.Categorical => JgsCategorical.FromCodesLike(
                    [.. JgsCategorical.CodesOf(value).Select((code, i) => named_[i] ? double.NaN : code)], value, value.CategoricalTag!),
                VariableKind.Cellstr => ReplacedCells(value, named_),
                _ => ReplacedStrings(value, named_),
            };
            columns[v] = TableColumnFrom("standardizeMissing", table[v].Name, standardized, line, col);
        }

        return [JgsValue.Table(table.WithColumns(columns))];
    }

    private static JgsValue StandardizedFloats(JgsValue value, bool[] named)
    {
        var numbers = new double[named.Length];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = named[i] ? double.NaN : (value.Type == JgsType.Array ? value.ElementAt(i) : value).AsNumber;
        }

        JgsValue array = JgsMatrix.FromColumnMajorDims(numbers, value.Type == JgsType.Array ? value.Dims : [1, 1]);
        return JgsNumericClasses.Stamp(array, value.NumericClass);
    }

    private static JgsValue ReplacedCells(JgsValue value, bool[] named)
    {
        JgsValue[] cells = [.. value.AsCell.Select((e, i) => named[i] ? JgsValue.Str(string.Empty) : JgsValue.Share(e))];
        JgsValue cell = JgsValue.Cell(cells);
        cell.TakeShapeOf(value);
        return cell;
    }

    private static JgsValue ReplacedStrings(JgsValue value, bool[] named)
    {
        JgsValue[] texts = [.. Enumerable.Range(0, value.ArrayLength).Select(i => named[i] ? JgsValue.Str(MissingSentinel) : value.ElementAt(i))];
        JgsValue strings = JgsValue.Array(texts);
        strings.TakeShapeOf(value);
        return strings.MarkStringArray();
    }

    // --- summary ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>s = summary(T)</c> in R2025b's words (probe_128, probe_128d): a field per variable (the row
    /// times first for a timetable), each a struct of the size, class, description, units and
    /// continuity, then what the kind reports - missing counts and the order statistics for numbers
    /// and times, categories and counts for a categorical, true and false counts for a logical.
    /// </summary>
    private static JgsValue TableSummaryMatlab(Table table, int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (table.RowTimes is { } times)
        {
            fields[times.Name] = VariableSummary(TableColumnValue(new Table([times]), times.Name, line, col), table.RowCount, string.Empty, string.Empty);
        }

        for (int v = 0; v < table.ColumnCount; v++)
        {
            string description = table.VariableDescriptions is { } descriptions && v < descriptions.Count ? descriptions[v] : string.Empty;
            string units = table.VariableUnits is { } allUnits && v < allUnits.Count ? allUnits[v] : string.Empty;
            fields[table[v].Name] = VariableSummary(TableColumnValue(table, table[v].Name, line, col), table.RowCount, description, units);
        }

        return JgsValue.Struct(fields);
    }

    private static JgsValue VariableSummary(JgsValue value, int rows, string description, string units)
    {
        VariableKind kind = KindOfVariable(value);
        int width = kind == VariableKind.Char ? (value.IsCharMatrix ? value.Cols : value.AsString.Length) : VariableShape(value, kind, rows).Width;
        if (value.Type == JgsType.Table)
        {
            width = value.AsTable.ColumnCount;
        }

        var info = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Size"] = JgsMatrix.FromColumnMajor([rows, width], 1, 2),
            ["Type"] = JgsValue.Str(ClassOf(value, JgsDialect.Matlab)),
            ["Description"] = JgsValue.Str(description),
            ["Units"] = JgsValue.Str(units),
            ["Continuity"] = JgsMatrix.FromColumnMajor([], 0, 0),
        };

        switch (kind)
        {
            case VariableKind.Float or VariableKind.Integer:
                AddOrderStatistics(info, value, rows, width, kind == VariableKind.Float);
                break;
            case VariableKind.Logical:
            {
                bool[] truths = Flags(value);
                var trues = new double[width];
                var falses = new double[width];
                for (int i = 0; i < truths.Length; i++)
                {
                    int c = rows == 0 ? 0 : i / rows;
                    if (truths[i])
                    {
                        trues[c]++;
                    }
                    else
                    {
                        falses[c]++;
                    }
                }

                info["True"] = PerColumn(trues);
                info["False"] = PerColumn(falses);
                break;
            }

            case VariableKind.Categorical:
            {
                JgsCategoricalTag tag = value.CategoricalTag!;
                double[] codes = JgsCategorical.CodesOf(value);
                var counts = new double[tag.Categories.Length];
                foreach (double code in codes.Where(static c => !double.IsNaN(c)))
                {
                    counts[(int)code - 1]++;
                }

                info["Categories"] = JgsCategorical.CategoriesColumn(tag);
                info["Counts"] = JgsMatrix.FromColumnMajor(counts, counts.Length, 1);
                info["NumMissing"] = JgsValue.Number(codes.Count(double.IsNaN));
                if (tag.Ordinal && codes.Any(static c => !double.IsNaN(c)))
                {
                    double[] present = [.. codes.Where(static c => !double.IsNaN(c)).OrderBy(static c => c)];
                    info["Min"] = JgsCategorical.FromCodes([present[0]], 1, 1, tag);
                    info["Median"] = JgsCategorical.FromCodes([present[present.Length / 2]], 1, 1, tag); // the upper middle of an even count
                    info["Max"] = JgsCategorical.FromCodes([present[^1]], 1, 1, tag);
                }

                break;
            }

            case VariableKind.Cellstr or VariableKind.Text:
                info["NumMissing"] = JgsValue.Number(ElementMissing(value, kind, rows, null).Count(static m => m));
                break;
            case VariableKind.Char:
            {
                var blanks = new double[width];
                for (int r = 0; r < rows; r++)
                {
                    string row = CharRow(value, r);
                    for (int c = 0; c < width && c < row.Length; c++)
                    {
                        if (row[c] == ' ')
                        {
                            blanks[c]++;
                        }
                    }
                }

                info["NumMissing"] = PerColumn(blanks);
                break;
            }

            case VariableKind.Time:
                AddTimeStatistics(info, value);
                break;
            default:
                if (value.Type == JgsType.Cell)
                {
                    info["NumMissing"] = JgsValue.Number(0);
                }

                break;
        }

        return JgsValue.Struct(info);
    }

    private static JgsValue PerColumn(double[] values) =>
        values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, 1, values.Length);

    /// <summary>NumMissing, Min, Median, Max, Mean and (for floats) Std, a column at a time, missing values left out.</summary>
    private static void AddOrderStatistics(Dictionary<string, JgsValue> info, JgsValue value, int rows, int width, bool floating)
    {
        JgsNumericClass numericClass = value.NumericClass;
        var missing = new double[width];
        var min = new double[width];
        var median = new double[width];
        var max = new double[width];
        var mean = new double[width];
        var std = new double[width];
        for (int c = 0; c < width; c++)
        {
            var present = new List<double>(rows);
            for (int r = 0; r < rows; r++)
            {
                JgsValue element = value.Type == JgsType.Array ? value.ElementAt((c * rows) + r) : value;
                double x = element.Type == JgsType.Complex ? element.AsComplex.Real : element.AsNumber;
                if (double.IsNaN(x))
                {
                    missing[c]++;
                }
                else
                {
                    present.Add(x);
                }
            }

            present.Sort();
            int n = present.Count;
            min[c] = n == 0 ? double.NaN : present[0];
            max[c] = n == 0 ? double.NaN : present[^1];
            median[c] = n == 0 ? double.NaN : n % 2 == 1 ? present[n / 2] : (present[(n / 2) - 1] + present[n / 2]) / 2;
            mean[c] = n == 0 ? double.NaN : present.Sum() / n;
            std[c] = n == 0 ? double.NaN : n == 1 ? 0 : Math.Sqrt(present.Sum(x => (x - mean[c]) * (x - mean[c])) / (n - 1));
        }

        JgsValue Classed(double[] values) => JgsNumericClasses.Stamp(PerColumn(values), numericClass);
        info["NumMissing"] = PerColumn(missing);
        if (rows == 0)
        {
            info["Min"] = JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor([], 0, width), numericClass);
            info["Median"] = Classed(median);
            info["Max"] = JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor([], 0, width), numericClass);
        }
        else
        {
            info["Min"] = Classed(min);
            info["Median"] = Classed(numericClass.IsInteger() ? [.. median.Select(m => Math.Round(m, MidpointRounding.AwayFromZero))] : median);
            info["Max"] = Classed(max);
        }

        info["Mean"] = numericClass == JgsNumericClass.Single ? Classed(mean) : PerColumn(mean);
        if (floating)
        {
            info["Std"] = Classed(std);
        }
    }

    /// <summary>A time variable's statistics: datetimes keep their zone's name and a duration spread.</summary>
    private static void AddTimeStatistics(Dictionary<string, JgsValue> info, JgsValue value)
    {
        JgsTimeTag tag = value.TimeTag!;
        if (tag.Kind == JgsTimeKind.Datetime)
        {
            info["TimeZone"] = JgsValue.Str(tag.TimeZone ?? string.Empty);
        }

        double[] ms = TimeMs(value);
        double[] present = [.. ms.Where(static x => !double.IsNaN(x)).OrderBy(static x => x)];
        int n = present.Length;
        double mean = n == 0 ? double.NaN : present.Sum() / n;
        double spread = n == 0 ? double.NaN : n == 1 ? 0 : Math.Sqrt(present.Sum(x => (x - mean) * (x - mean)) / (n - 1));
        JgsValue Moment(double x) => JgsMatrix.FromColumnMajorDims([x], [1, 1]).MarkTime(tag);
        info["NumMissing"] = JgsValue.Number(ms.Length - n);
        info["Min"] = Moment(n == 0 ? double.NaN : present[0]);
        info["Median"] = Moment(n == 0 ? double.NaN : n % 2 == 1 ? present[n / 2] : (present[(n / 2) - 1] + present[n / 2]) / 2);
        info["Max"] = Moment(n == 0 ? double.NaN : present[^1]);
        info["Mean"] = Moment(mean);
        info["Std"] = JgsMatrix.FromColumnMajorDims([spread], [1, 1]).MarkTime(tag.Kind == JgsTimeKind.Duration ? tag : JgsTime.DurationTag());
    }

    /// <summary>
    /// What <c>summary(T)</c> prints with nothing asked of it (probe_128, probe_128d): the table's
    /// name and size, a line per variable, and the statistics of the variables that have them laid
    /// out as a table. The lines under "Variables:" are R2025b's; the statistics block is laid out by
    /// this build's own rule (a recorded divergence: R2025b's is its table display).
    /// </summary>
    private static string TableSummaryText(Table table, string? name, int line, int col)
    {
        var text = new StringBuilder("\n");
        string size = $"{table.RowCount}×{table.ColumnCount} table";
        text.Append(name is null ? size : $"<strong>{name}</strong>: {size}").Append('\n');
        if (table.ColumnCount == 0)
        {
            return text.Append('\n').ToString();
        }

        text.Append("\nVariables:\n\n");
        var statistics = new List<(string Name, Dictionary<string, JgsValue> Info)>();
        for (int v = 0; v < table.ColumnCount; v++)
        {
            string variable = table[v].Name;
            JgsValue value = TableColumnValue(table, variable, line, col);
            VariableKind kind = KindOfVariable(value);
            string described = kind switch
            {
                VariableKind.Categorical => CategoricalSummaryLink(name, variable, value.CategoricalTag!.Categories.Length),
                VariableKind.Cellstr => "cell array of character vectors",
                VariableKind.Logical => $"logical ({Flags(value).Count(static t => t)} true)",
                _ when value.Type == JgsType.Table => $"{value.AsTable.RowCount}×{value.AsTable.ColumnCount} table",
                _ => ClassOf(value, JgsDialect.Matlab),
            };
            text.Append($"    <strong>{variable}</strong>: {described}\n");
            Dictionary<string, JgsValue> info = VariableSummary(value, table.RowCount, string.Empty, string.Empty).AsStruct;
            if (info.ContainsKey("NumMissing"))
            {
                statistics.Add((variable, info));
            }
        }

        if (statistics.Count == 0)
        {
            return text.ToString();
        }

        string[] headers = [.. ((string[])["NumMissing", "Min", "Median", "Max", "Mean", "Std"]).Where(h => statistics.Any(s => s.Info.ContainsKey(h)))];
        string[][] cells = [.. statistics.Select(s => headers.Select(h => s.Info.TryGetValue(h, out JgsValue? stat) ? StatisticText(stat) : string.Empty).ToArray())];
        for (int h = 0; h < headers.Length; h++)
        {
            string[] column = [.. cells.Select(c => c[h])];
            if (column.All(static c => c.Length == 0 || IsWhole(c)))
            {
                continue;
            }

            for (int r = 0; r < cells.Length; r++)
            {
                if (double.TryParse(cells[r][h], NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                {
                    cells[r][h] = x.ToString("F4", CultureInfo.InvariantCulture);
                }
            }
        }

        int nameWidth = statistics.Max(static s => s.Name.Length);
        int[] widths = [.. headers.Select((h, i) => Math.Max(h.Length, cells.Max(c => c[i].Length)))];
        string Centered(string content, int width)
        {
            int left = (width - content.Length) / 2;
            return new string(' ', left) + content + new string(' ', width - content.Length - left);
        }

        text.Append("\nStatistics for applicable variables:\n\n");
        text.Append(new string(' ', nameWidth + 8));
        text.Append(string.Join("     ", headers.Select((h, i) => Centered(h, widths[i]).Replace(h, $"<strong>{h}</strong>", StringComparison.Ordinal)))).Append("    \n\n");
        foreach ((string variable, _) in statistics)
        {
            int r = statistics.FindIndex(s => s.Name == variable);
            text.Append($"    <strong>{variable.PadRight(nameWidth)}</strong>    ");
            text.Append(string.Join("     ", cells[r].Select((c, i) => Centered(c, widths[i])))).Append("  \n");
        }

        return text.ToString();
    }

    private static bool IsWhole(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && x == Math.Floor(x) && Math.Abs(x) < 1e9;

    private static string StatisticText(JgsValue stat)
    {
        if (stat.IsCategorical || stat.IsTime || (stat.Type == JgsType.Array && stat.ArrayLength != 1))
        {
            return string.Empty;
        }

        double x = stat.Type == JgsType.Array ? stat.ElementAt(0).AsNumber : stat.AsNumber;
        return double.IsNaN(x) ? "NaN" : x.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A categorical variable's line: its category count, linked to its counts when the table has a name.</summary>
    private static string CategoricalSummaryLink(string? table, string variable, int categories) => table is null
        ? $"categorical ({categories} categories)"
        : "categorical (<a href=\"matlab:if exist('" + table + "','var') && istabular(" + table + ") && ismember('" + variable + "',"
            + table + ".Properties.VariableNames) && iscategorical(" + table + ".('" + variable + "')),displayWholeSummary(" + table + ".('"
            + variable + "')),fprintf(matlab.internal.display.lineSpacingCharacter),else,fprintf('Unable to display counts for ''"
            + variable + "'' because ''" + table + "'' has changed.\\n');end\">" + categories + " categories</a>)";

    // --- table('Size', ...) --------------------------------------------------------------------------

    /// <summary>Where a <c>'Size'</c> name stands in a call to <c>table</c>, or null when it is not a preallocation.</summary>
    internal static int? PreallocationSizeAt(IReadOnlyList<JgsValue> args)
    {
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (VerbText(args[i]) is { } word && word.Equals("Size", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>table('Size', [rows vars], 'VariableTypes', types, 'VariableNames', names, 'RowNames', rows)</c>
    /// (probe_128c): each variable the default of its type - zeros of a numeric class, false, the
    /// missing string, empty chars for cellstr and char (char warns), empty cells, undefined
    /// categories, NaT, zero durations, empty structs - with R2025b's refusals.
    /// </summary>
    private static JgsValue PreallocatedTable(IReadOnlyList<JgsValue> args, int sizeAt, JGraphScriptGlobals host, int line, int col)
    {
        if (sizeAt > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:InvalidSizeSyntax",
                "Specifying both size and input data variables is not supported. See the <a href=\"matlab:helpview(fullfile(docroot,'matlab','ref','table.html'))\">table documentation</a> for more information.");
        }

        var options = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count || VerbText(args[i]) is not { } option)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:InvalidSizeSyntax",
                    "Specifying both size and input data variables is not supported. See the <a href=\"matlab:helpview(fullfile(docroot,'matlab','ref','table.html'))\">table documentation</a> for more information.");
            }

            options[option] = args[i + 1];
        }

        JgsValue sizeValue = options["Size"];
        double[] size = sizeValue.Type is JgsType.Number or JgsType.Bool ? [sizeValue.AsNumber]
            : sizeValue.Type == JgsType.Array && !sizeValue.IsStringArray && !sizeValue.IsCharMatrix
                ? [.. Enumerable.Range(0, sizeValue.ArrayLength).Select(i => sizeValue.ElementAt(i).AsNumber)]
                : [];
        if (size.Length != 2 || size.Any(static s => s < 0 || s != Math.Floor(s) || double.IsInfinity(s)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:InvalidSize",
                "Size must be a two-element vector of nonnegative integer values specifying the number of rows and the number of variables.");
        }

        int rows = (int)size[0];
        int count = (int)size[1];
        if (!options.TryGetValue("VariableTypes", out JgsValue? typesValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:MissingVariableTypes", "Specify variable types when creating a table with a specified size.");
        }

        string[] types = NameTexts(typesValue);
        if (types.Length != count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:VariableTypesAndSizeMismatch",
                "Number of variable types must match the number of variables specified in the size.");
        }

        string[] names = options.TryGetValue("VariableNames", out JgsValue? namesValue) ? NameTexts(namesValue) : [.. Enumerable.Range(1, count).Select(i => $"Var{i}")];
        if (names.Length != count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:IncorrectNumberOfVarNames",
                "The VariableNames property must contain one name for each variable in the table.");
        }

        string[]? rowNames = options.TryGetValue("RowNames", out JgsValue? rowNamesValue) ? NameTexts(rowNamesValue) : null;
        if (rowNames is { Length: 0 })
        {
            rowNames = null;
        }

        if (rowNames is not null && rowNames.Length != rows)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:table:IncorrectNumberOfRowNames",
                "The RowNames property must contain one name for each row in the table.");
        }

        var columns = new TableColumn[count];
        for (int v = 0; v < count; v++)
        {
            if (types[v] == "char")
            {
                Warn(host, "MATLAB:table:PreallocateCharWarning", PreallocateCharWarning);
            }

            columns[v] = TableColumnFrom("table", names[v], DefaultVariable(types[v], rows, line, col), line, col);
        }

        return JgsValue.Table(new Table(columns, rows) { RowNames = rowNames });
    }

    /// <summary>Text names from a char row, a cell of them or a string array.</summary>
    private static string[] NameTexts(JgsValue value) =>
        value.Type == JgsType.String ? [value.AsString]
        : value.Type == JgsType.Cell ? [.. value.AsCell.Select(static e => e.AsString)]
        : value.IsStringArray ? [.. Enumerable.Range(0, value.ArrayLength).Select(i => value.ElementAt(i).AsString)]
        : [];

    /// <summary>A preallocated variable of <paramref name="rows"/> rows of a type's default (probe_128c).</summary>
    private static JgsValue DefaultVariable(string type, int rows, int line, int col)
    {
        JgsValue Cells(Func<JgsValue> element)
        {
            JgsValue cell = JgsValue.Cell([.. Enumerable.Range(0, rows).Select(_ => element())]);
            cell.Reshape(rows, 1);
            return cell;
        }

        if (JgsNumericClasses.Parse(type) is { } numericClass)
        {
            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajorDims(new double[rows], [rows, 1]), numericClass);
        }

        switch (type)
        {
            case "logical":
                return LogicalMask(new bool[rows], rows, 1);
            case "string":
            {
                JgsValue strings = JgsValue.Array([.. Enumerable.Range(0, rows).Select(_ => JgsValue.Str(MissingSentinel))]);
                strings.Reshape(rows, 1);
                return rows == 0 ? JgsValue.StringArray([], 0, 1) : strings.MarkStringArray();
            }

            case "cellstr" or "char":
                return Cells(() => JgsValue.Str(string.Empty));
            case "cell":
                return Cells(JgsEmpty.Zero);
            case "categorical":
                return JgsCategorical.FromCodes(Enumerable.Repeat(double.NaN, rows).ToArray(), rows, 1, new JgsCategoricalTag([], false, false));
            case "datetime":
                return JgsMatrix.FromColumnMajorDims(Enumerable.Repeat(double.NaN, rows).ToArray(), [rows, 1])
                    .MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, JgsTime.DefaultDatetimeFormat));
            case "duration":
                return JgsMatrix.FromColumnMajorDims(new double[rows], [rows, 1]).MarkTime(JgsTime.DurationTag());
            case "struct":
                return JgsValue.StructArray(new JgsStructArray([.. Enumerable.Range(0, rows).Select(_ => new Dictionary<string, JgsValue>(StringComparer.Ordinal))], []), rows, 1);
            default:
                throw new JgsRuntimeException(line, col, "MATLAB:table:PreAllocationUndefinedClass",
                    $"Unable to create a variable of type '{type}'. There is no class named '{type}' defined.");
        }
    }
}
