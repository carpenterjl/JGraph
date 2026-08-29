using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The grouping family (M103): <c>findgroups</c> and <c>splitapply</c>, which turn "by category"
/// into numbers and back; their conveniences <c>groupcounts</c>, <c>grouptransform</c> and
/// <c>groupfilter</c>; and the row verbs <c>head</c>, <c>tail</c> and <c>topkrows</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>findgroups</c> numbers the distinct values of its grouping variables in sorted order — sorted
/// as numbers when they are numbers, which is why it cannot ride the text-keyed grouping the
/// preprocessing family uses ("10" sorts before "2" there). A missing value (NaN, the missing
/// string) gets a NaN group, and everything downstream skips it.
/// </para>
/// <para>
/// <c>splitapply</c> mirrors its input's orientation, measured against R2024a: a row of data hands
/// each group to the function as a row and joins the answers side by side, a column hands columns
/// and stacks them, and a matrix hands each group its rows. The same measurement fixed the error
/// identifiers a script can catch — a group vector that skips an integer is
/// <c>MATLAB:splitapply:MissingGroupNums</c>, not a message invented here.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the grouping family into <paramref name="env"/>.</summary>
    internal static void RegisterGroupingBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        DefineBoth("findgroups", FindGroups);
        DefineBoth("splitapply", (args, wanted, line, col) => SplitApply(args, wanted, line, col));
        DefineBoth("groupcounts", GroupCounts);
        Define("grouptransform", GroupTransform);
        Define("groupfilter", GroupFilter);
        Define("head", (args, line, col) => HeadOrTail("head", args, line, col));
        Define("tail", (args, line, col) => HeadOrTail("tail", args, line, col));
        DefineBoth("topkrows", TopRows);
    }

    // --- the grouping key ---------------------------------------------------------------------

    /// <summary>
    /// One grouping variable taken apart: either numbers or texts, with a missing flag per element.
    /// Sorting distinct values sorts numbers as numbers and texts as text.
    /// </summary>
    private sealed class GroupingVariable
    {
        public double[]? Numbers;

        public string[]? Texts;

        public bool IsText => Texts is not null;

        public int Length => Texts?.Length ?? Numbers!.Length;

        public bool MissingAt(int i) => IsText
            ? Texts![i].Length == 0 || Texts[i] == MissingSentinel
            : double.IsNaN(Numbers![i]);

        public int Compare(int a, int b) => IsText
            ? string.CompareOrdinal(Texts![a], Texts[b])
            : Numbers![a].CompareTo(Numbers[b]);

        public bool Same(int a, int b) => IsText
            ? string.Equals(Texts![a], Texts[b], StringComparison.Ordinal)
            : Numbers![a].Equals(Numbers[b]);
    }

    private static GroupingVariable ReadGroupingVariable(string name, JgsValue value, int line, int col)
    {
        if (TextElementsOf(value) is { } texts)
        {
            return new GroupingVariable { Texts = texts };
        }

        int[] dims = SizeDims(value);
        if (dims.Count(static d => d > 1) > 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:GroupingVarNotVector",
                "A grouping variable must be a vector.");
        }

        return new GroupingVariable { Numbers = FlattenColumnMajor(name, value, line, col) };
    }

    /// <summary>
    /// Group numbers for rows described by several grouping variables at once: distinct
    /// combinations, numbered in the order sorted by the first variable, then the second, and so
    /// on. A row with any missing value gets NaN. Returns the group of each row and, per group, a
    /// representative row index.
    /// </summary>
    private static (double[] Groups, int[] Representatives) GroupNumbers(
        IReadOnlyList<GroupingVariable> variables)
    {
        int count = variables.Count == 0 ? 0 : variables[0].Length;
        var order = new List<int>();
        for (int i = 0; i < count; i++)
        {
            bool missing = false;
            foreach (GroupingVariable variable in variables)
            {
                missing |= variable.MissingAt(i);
            }

            if (!missing)
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) =>
        {
            foreach (GroupingVariable variable in variables)
            {
                int result = variable.Compare(a, b);
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        });

        var groups = new double[count];
        Array.Fill(groups, double.NaN);
        var representatives = new List<int>();
        int number = 0;
        for (int k = 0; k < order.Count; k++)
        {
            bool fresh = k == 0 || variables.Any(v => !v.Same(order[k - 1], order[k]));
            if (fresh)
            {
                number++;
                representatives.Add(order[k]);
            }

            groups[order[k]] = number;
        }

        return (groups, [.. representatives]);
    }

    /// <summary>The distinct value of one grouping variable per group, in the variable's own kind.</summary>
    private static JgsValue GroupIdentity(GroupingVariable variable, JgsValue original, int[] representatives, bool column)
    {
        int[] shape = column ? [representatives.Length, representatives.Length == 0 ? 0 : 1]
            : [representatives.Length == 0 ? 0 : 1, representatives.Length];
        if (variable.IsText)
        {
            JgsValue[] texts = [.. representatives.Select(r => JgsValue.Str(variable.Texts![r]))];
            JgsValue answer = original.Type == JgsType.Cell ? JgsValue.Cell(texts) : JgsValue.Array(texts);
            answer.Reshape(shape[0], shape[1]);
            if (original.IsStringArray)
            {
                answer.MarkStringArray();
            }

            return answer;
        }

        double[] numbers = [.. representatives.Select(r => variable.Numbers![r])];
        return JgsMatrix.FromColumnMajorDims(numbers, shape);
    }

    /// <summary>
    /// <c>[G, ID…] = findgroups(A1, …, AN)</c> over vectors, or <c>[G, TID] = findgroups(T)</c> over
    /// a table's variables. <c>G</c> follows the first input's orientation; each <c>ID</c> holds one
    /// input's value per group.
    /// </summary>
    private static JgsValue[] FindGroups(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        if (args[0].Type == JgsType.Table)
        {
            Arity("findgroups", args, 1, line, col);
            Table table = args[0].AsTable;
            var fromTable = new List<GroupingVariable>();
            foreach (TableColumn keyed in table.Columns)
            {
                fromTable.Add(keyed.Type == ColumnType.Text
                    ? new GroupingVariable { Texts = [.. Enumerable.Range(0, table.RowCount).Select(keyed.GetText)] }
                    : new GroupingVariable { Numbers = [.. Enumerable.Range(0, table.RowCount).Select(keyed.GetNumber)] });
            }

            (double[] tableGroups, int[] keyRows) = GroupNumbers(fromTable);
            var keyColumns = new List<TableColumn>();
            for (int c = 0; c < table.ColumnCount; c++)
            {
                TableColumn source = table.Columns[c];
                keyColumns.Add(source.Type == ColumnType.Text
                    ? new TextColumn(source.Name, [.. keyRows.Select(source.GetText)])
                    : new NumberColumn(source.Name, [.. keyRows.Select(source.GetNumber)]));
            }

            return Outputs(
                wanted,
                JgsMatrix.FromColumnMajorDims(tableGroups, [tableGroups.Length, tableGroups.Length == 0 ? 0 : 1]),
                JgsValue.Table(new Table(keyColumns)));
        }

        var variables = new List<GroupingVariable>();
        foreach (JgsValue arg in args)
        {
            variables.Add(ReadGroupingVariable("findgroups", arg, line, col));
        }

        for (int v = 1; v < variables.Count; v++)
        {
            if (variables[v].Length != variables[0].Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:findgroups:InputSizeMismatch",
                    "All grouping variables must have the same length.");
            }
        }

        (double[] groups, int[] representatives) = GroupNumbers(variables);
        int[] dims = SizeDims(args[0]);
        var outputs = new JgsValue[1 + variables.Count];
        outputs[0] = JgsMatrix.FromColumnMajorDims(groups, dims);
        for (int v = 0; v < variables.Count; v++)
        {
            bool identityColumn = SizeDims(args[v]) is [_, 1, ..] and not [1, ..];
            outputs[v + 1] = GroupIdentity(variables[v], args[v], representatives, identityColumn);
        }

        return Outputs(wanted, outputs);
    }

    // --- splitapply ---------------------------------------------------------------------------

    /// <summary>The group vector read the way splitapply reads it: whole numbers 1…N, none skipped.</summary>
    private static (int[] Groups, int Count) SplitGroups(double[] raw, int line, int col)
    {
        var groups = new int[raw.Length];
        int highest = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            double g = raw[i];
            if (double.IsNaN(g))
            {
                groups[i] = 0;
                continue;
            }

            if (g < 1 || g != Math.Floor(g))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:splitapply:MissingGroupNums",
                    "For N groups, every integer between 1 and N must occur at least once in the vector of group numbers.");
            }

            groups[i] = (int)g;
            highest = Math.Max(highest, (int)g);
        }

        var present = new bool[highest + 1];
        foreach (int g in groups)
        {
            present[g] = true;
        }

        for (int g = 1; g <= highest; g++)
        {
            if (!present[g])
            {
                throw new JgsRuntimeException(line, col, "MATLAB:splitapply:MissingGroupNums",
                    "For N groups, every integer between 1 and N must occur at least once in the vector of group numbers.");
            }
        }

        return (groups, highest);
    }

    /// <summary>
    /// <c>[Y1, …, YM] = splitapply(func, X1, …, XN, G)</c>: the function applied to each group of the
    /// data, and the answers joined. A column of data hands the function columns and stacks the
    /// answers; a row hands rows and joins them side by side; a matrix hands each group its rows. A
    /// table hands each variable as its own argument.
    /// </summary>
    private static JgsValue[] SplitApply(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        if (args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "splitapply: the first argument is a function handle.");
        }

        IJgsCallable func = args[0].AsCallable;
        double[] rawGroups = FlattenColumnMajor("splitapply", args[^1], line, col);
        (int[] groups, int groupCount) = SplitGroups(rawGroups, line, col);

        // Each data argument becomes a per-group value factory plus the orientation the answers
        // should be joined in. A table contributes one factory per variable.
        var sources = new List<Func<int[], JgsValue>>();
        bool sideways = false;
        for (int a = 1; a < args.Count - 1; a++)
        {
            JgsValue data = args[a];
            if (data.Type == JgsType.Table)
            {
                Table table = data.AsTable;
                if (table.RowCount != rawGroups.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:splitapply:ColumnMismatch",
                        "The data variables must have the same number of rows as the vector of group numbers.");
                }

                foreach (TableColumn column in table.Columns)
                {
                    TableColumn captured = column;
                    sources.Add(rows => captured.Type == ColumnType.Text
                        ? CellColumn([.. rows.Select(captured.GetText)])
                        : JgsMatrix.FromColumnMajorDims(
                            [.. rows.Select(captured.GetNumber)], [rows.Length, 1]));
                }

                continue;
            }

            int[] dims = SizeDims(data);
            bool row = dims.Length >= 2 && dims[0] == 1 && dims[1] != 1;
            bool vector = dims.Count(static d => d > 1) <= 1;
            if (a == 1)
            {
                sideways = row && vector;
            }

            if (TextElementsOf(data) is { } texts)
            {
                if (texts.Length != rawGroups.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:splitapply:ColumnMismatch",
                        "The data variables must have the same size as the vector of group numbers.");
                }

                bool cell = data.Type == JgsType.Cell;
                sources.Add(rows =>
                {
                    JgsValue[] picked = [.. rows.Select(r => JgsValue.Str(texts[r]))];
                    JgsValue answer = cell ? JgsValue.Cell(picked) : JgsValue.Array(picked);
                    answer.Reshape(row && vector ? 1 : picked.Length, row && vector ? picked.Length : 1);
                    return answer;
                });
                continue;
            }

            double[] flat = FlattenColumnMajor("splitapply", data, line, col);
            if (vector)
            {
                if (flat.Length != rawGroups.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:splitapply:ColumnMismatch",
                        "The data variables must have the same number of columns as the vector of group numbers. "
                        + $"The group number vector has {rawGroups.Length} column(s), and data variable {a} has "
                        + $"{flat.Length} column(s).");
                }

                sources.Add(rows => JgsMatrix.FromColumnMajorDims(
                    [.. rows.Select(r => flat[r])],
                    row ? [1, rows.Length] : [rows.Length, 1]));
            }
            else
            {
                int height = dims[0];
                int width = flat.Length / Math.Max(1, height);
                if (height != rawGroups.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:splitapply:ColumnMismatch",
                        "The data variables must have the same number of rows as the vector of group numbers.");
                }

                sources.Add(rows =>
                {
                    var picked = new double[rows.Length * width];
                    for (int c = 0; c < width; c++)
                    {
                        for (int r = 0; r < rows.Length; r++)
                        {
                            picked[(c * rows.Length) + r] = flat[(c * height) + rows[r]];
                        }
                    }

                    return JgsMatrix.FromColumnMajorDims(picked, [rows.Length, width]);
                });
            }
        }

        var perGroup = new JgsValue[groupCount][];
        for (int g = 1; g <= groupCount; g++)
        {
            int[] rows = [.. Enumerable.Range(0, groups.Length).Where(i => groups[i] == g)];
            var inputs = new List<JgsValue>();
            foreach (Func<int[], JgsValue> source in sources)
            {
                inputs.Add(source(rows));
            }

            try
            {
                perGroup[g - 1] = CallForOutputs(func, inputs, wanted, line, col);
            }
            catch (JgsRuntimeException inner)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:splitapply:FunFailed",
                    $"Unable to apply the function to the group of data. {inner.Message}");
            }
        }

        var outputs = new JgsValue[Math.Max(1, wanted)];
        for (int o = 0; o < outputs.Length; o++)
        {
            JgsValue[] pieces = [.. perGroup.Select(answers => o < answers.Length
                ? answers[o]
                : throw new JgsRuntimeException(line, col,
                    "splitapply: the function produced fewer outputs than were asked for."))];
            outputs[o] = JoinGroupAnswers("splitapply", pieces, sideways, line, col);
        }

        return outputs;
    }

    /// <summary>The per-group answers joined — stacked for column data, side by side for row data.</summary>
    private static JgsValue JoinGroupAnswers(
        string name, JgsValue[] pieces, bool sideways, int line, int col)
    {
        if (pieces.Length == 0)
        {
            return JgsEmpty.Shaped(0, 1);
        }

        if (pieces.All(static p => p.Type == JgsType.Cell))
        {
            JgsValue[] cells = [.. pieces.SelectMany(static p => p.BoxedElements())];
            JgsValue joined = JgsValue.Cell(cells);
            joined.Reshape(sideways ? 1 : cells.Length, sideways ? cells.Length : 1);
            return joined;
        }

        var blocks = new List<(double[] Flat, int Rows, int Cols)>();
        foreach (JgsValue piece in pieces)
        {
            double[] flat = FlattenColumnMajor(name, piece, line, col);
            int[] dims = SizeDims(piece);
            int rows = dims.Length > 0 ? dims[0] : 1;
            int cols = dims.Length > 1 ? dims.Skip(1).Aggregate(1, static (a, b) => a * b) : 1;
            blocks.Add((flat, rows, cols));
        }

        if (sideways)
        {
            // Measured: a group answer wider than one column is refused even when every group's
            // agrees — MATLAB asks for a scalar (or a column) per group and points at cells.
            int height = blocks[0].Rows;
            if (blocks.Any(b => b.Rows != height || b.Cols > 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:splitapply:OutputNotUniform",
                    "The function returned a non-scalar value when applied to the 1st group of "
                    + "data.\n\nTo compute nonscalar values for each group, create an anonymous "
                    + "function to return each value in a scalar cell:\n\n\t@(x){x}");
            }

            double[] flat = [.. blocks.SelectMany(static b => b.Flat)];
            int width = blocks.Sum(static b => b.Cols);
            return JgsMatrix.FromColumnMajorDims(flat, [height, width]);
        }

        int across = blocks[0].Cols;
        if (blocks.Any(b => b.Cols != across || b.Rows > 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:splitapply:OutputNotUniform",
                "The function returned a non-scalar value when applied to the 1st group of "
                + "data.\n\nTo compute nonscalar values for each group, create an anonymous "
                + "function to return each value in a scalar cell:\n\n\t@(x){x}");
        }

        int total = blocks.Sum(static b => b.Rows);
        var stacked = new double[total * across];
        int at = 0;
        foreach ((double[] flat, int rows, int cols) in blocks)
        {
            for (int c = 0; c < cols; c++)
            {
                Array.Copy(flat, c * rows, stacked, (c * total) + at, rows);
            }

            at += rows;
        }

        return JgsMatrix.FromColumnMajorDims(stacked, [total, across]);
    }

    private static JgsValue CellColumn(string[] texts)
    {
        JgsValue cell = JgsValue.Cell([.. texts.Select(JgsValue.Str)]);
        cell.Reshape(texts.Length, texts.Length == 0 ? 0 : 1);
        return cell;
    }

    // --- groupcounts, grouptransform, groupfilter ----------------------------------------------

    /// <summary>
    /// <c>[GC, GR, GP] = groupcounts(A)</c> over a column, or a summary table over a table. Missing
    /// values count as their own group, listed last — that is R2024a's answer, unlike
    /// <c>findgroups</c>, which drops them.
    /// </summary>
    private static JgsValue[] GroupCounts(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        if (args[0].Type == JgsType.Table)
        {
            Table table = args[0].AsTable;
            string[] names = args.Count > 1
                ? TableVariableNames("groupcounts", table, args[1], line, col)
                : [.. table.ColumnNames];
            var keyed = new List<GroupingVariable>();
            foreach (string variable in names)
            {
                TableColumn column = TableVariable("groupcounts", table, variable, line, col);
                keyed.Add(column.Type == ColumnType.Text
                    ? new GroupingVariable { Texts = [.. Enumerable.Range(0, table.RowCount).Select(column.GetText)] }
                    : new GroupingVariable { Numbers = [.. Enumerable.Range(0, table.RowCount).Select(column.GetNumber)] });
            }

            (double[] rowGroups, int[] keyRows, double[] keyCounts) = CountedGroups(keyed, table.RowCount);
            _ = rowGroups;
            var columns = new List<TableColumn>();
            foreach (string picked in names)
            {
                TableColumn source = TableVariable("groupcounts", table, picked, line, col);
                columns.Add(source.Type == ColumnType.Text
                    ? new TextColumn(picked, [.. keyRows.Select(source.GetText)])
                    : new NumberColumn(picked, [.. keyRows.Select(source.GetNumber)]));
            }

            columns.Add(new NumberColumn("GroupCount", keyCounts));
            columns.Add(new NumberColumn("Percent", [.. keyCounts.Select(c => 100.0 * c / table.RowCount)]));
            return [JgsValue.Table(new Table(columns))];
        }

        int[] dims = SizeDims(args[0]);
        bool columnData = dims.Length >= 2 && dims[1] == 1;
        if (!columnData)
        {
            throw new JgsRuntimeException(line, col,
                "groupcounts here takes a column of values, or a table; MATLAB's answer for other "
                + "shapes groups whole rows, which this build does not do.");
        }

        GroupingVariable grouping = ReadGroupingVariable("groupcounts", args[0], line, col);
        (_, int[] representatives, double[] counts) = CountedGroups([grouping], grouping.Length);

        JgsValue identity = GroupIdentity(grouping, args[0], representatives, true);
        int total = grouping.Length;
        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(counts, [counts.Length, counts.Length == 0 ? 0 : 1]),
            identity,
            JgsMatrix.FromColumnMajorDims(
                [.. counts.Select(c => 100.0 * c / total)], [counts.Length, counts.Length == 0 ? 0 : 1]));
    }

    /// <summary>
    /// Group numbers where missing values form their own group, numbered last. Returns each row's
    /// group, a representative row per group, and the group sizes.
    /// </summary>
    private static (double[] Groups, int[] Representatives, double[] Counts) CountedGroups(
        IReadOnlyList<GroupingVariable> variables, int rows)
    {
        (double[] groups, int[] representatives) = GroupNumbers(variables);
        var keys = representatives.ToList();
        int missingAt = -1;
        for (int i = 0; i < rows; i++)
        {
            if (double.IsNaN(groups[i]))
            {
                if (missingAt < 0)
                {
                    missingAt = keys.Count;
                    keys.Add(i);
                }

                groups[i] = missingAt + 1;
            }
        }

        var counts = new double[keys.Count];
        for (int i = 0; i < rows; i++)
        {
            counts[(int)groups[i] - 1]++;
        }

        return (groups, [.. keys], counts);
    }

    private static readonly string[] GroupTransformMethods =
        ["zscore", "norm", "meancenter", "rescale", "meanfill", "linearfill"];

    /// <summary>
    /// <c>B = grouptransform(A, G, method)</c> over a column, or over a table's variables by named
    /// grouping variables. The method is one of the six documented words or a function handle
    /// applied to each group.
    /// </summary>
    private static JgsValue GroupTransform(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        if (args[0].Type == JgsType.Table)
        {
            Table table = args[0].AsTable;
            string[] groupNames = TableVariableNames("grouptransform", table, args[1], line, col);
            var keyed = new List<GroupingVariable>();
            foreach (string variable in groupNames)
            {
                TableColumn column = TableVariable("grouptransform", table, variable, line, col);
                keyed.Add(column.Type == ColumnType.Text
                    ? new GroupingVariable { Texts = [.. Enumerable.Range(0, table.RowCount).Select(column.GetText)] }
                    : new GroupingVariable { Numbers = [.. Enumerable.Range(0, table.RowCount).Select(column.GetNumber)] });
            }

            (double[] groups, _, _) = CountedGroups(keyed, table.RowCount);
            var columns = new List<TableColumn>();
            foreach (TableColumn column in table.Columns)
            {
                if (groupNames.Contains(column.Name, StringComparer.Ordinal) || column.Type == ColumnType.Text)
                {
                    columns.Add(column);
                    continue;
                }

                double[] data = [.. Enumerable.Range(0, table.RowCount).Select(column.GetNumber)];
                columns.Add(new NumberColumn(column.Name, TransformGroups(data, groups, args[2], line, col)));
            }

            return JgsValue.Table(new Table(columns));
        }

        int[] dims = SizeDims(args[0]);
        double[] flat = FlattenColumnMajor("grouptransform", args[0], line, col);
        double[] rawGroups = FlattenColumnMajor("grouptransform", args[1], line, col);
        bool vector = dims.Count(static d => d > 1) <= 1;
        int height = vector ? flat.Length : dims[0];
        if (rawGroups.Length != height)
        {
            throw new JgsRuntimeException(line, col,
                "grouptransform: one group number per row of the data.");
        }

        (double[] numbered, _, _) = CountedGroups(
            [new GroupingVariable { Numbers = rawGroups }], rawGroups.Length);

        int width = vector ? 1 : flat.Length / Math.Max(1, height);
        var result = new double[flat.Length];
        for (int c = 0; c < width; c++)
        {
            double[] columnData = new double[height];
            Array.Copy(flat, c * height, columnData, 0, height);
            double[] transformed = TransformGroups(columnData, numbered, args[2], line, col);
            Array.Copy(transformed, 0, result, c * height, height);
        }

        return JgsMatrix.FromColumnMajorDims(result, dims);
    }

    /// <summary>One column transformed group by group, in place order.</summary>
    private static double[] TransformGroups(
        double[] data, double[] groups, JgsValue method, int line, int col)
    {
        var result = new double[data.Length];
        int highest = data.Length == 0 ? 0 : (int)groups.Max();
        for (int g = 1; g <= highest; g++)
        {
            int[] rows = [.. Enumerable.Range(0, data.Length).Where(i => (int)groups[i] == g)];
            if (rows.Length == 0)
            {
                continue;
            }

            double[] values = [.. rows.Select(i => data[i])];
            double[] answer;
            if (IsTextScalar(method))
            {
                string word = TextOf(method).ToLowerInvariant();
                if (!GroupTransformMethods.Contains(word))
                {
                    throw new JgsRuntimeException(line, col,
                        $"grouptransform: no method called '{TextOf(method)}' (expected one of "
                        + $"'{string.Join("', '", GroupTransformMethods)}', or a function handle).");
                }

                answer = TransformValues(values, word);
            }
            else
            {
                if (method.Type != JgsType.Function)
                {
                    throw new JgsRuntimeException(line, col,
                        "grouptransform: the method is a word or a function handle.");
                }

                IJgsCallable handle = method.AsCallable;
                JgsValue column = JgsMatrix.FromColumnMajorDims(values, [values.Length, 1]);
                double[] made = FlattenColumnMajor("grouptransform", handle.Call([column], line, col), line, col);
                answer = made.Length == 1 && values.Length != 1
                    ? [.. Enumerable.Repeat(made[0], values.Length)]
                    : made;
                if (answer.Length != values.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        "grouptransform: the function must answer one value per row, or one for the group.");
                }
            }

            for (int i = 0; i < rows.Length; i++)
            {
                result[rows[i]] = answer[i];
            }
        }

        return result;
    }

    private static double[] TransformValues(double[] values, string method)
    {
        double[] present = PrepPresent(values);
        switch (method)
        {
            case "zscore":
            {
                double mean = present.Length == 0 ? double.NaN : present.Average();
                double deviation = Math.Sqrt(SampleVarianceOf(present));
                return [.. values.Select(v => (v - mean) / deviation)];
            }

            case "norm":
            {
                double length = Math.Sqrt(present.Sum(static v => v * v));
                return [.. values.Select(v => v / length)];
            }

            case "meancenter":
            {
                double mean = present.Length == 0 ? double.NaN : present.Average();
                return [.. values.Select(v => v - mean)];
            }

            case "rescale":
            {
                double low = present.Length == 0 ? double.NaN : present.Min();
                double high = present.Length == 0 ? double.NaN : present.Max();
                return [.. values.Select(v => high == low ? 0 : (v - low) / (high - low))];
            }

            case "meanfill":
            {
                double mean = present.Length == 0 ? double.NaN : present.Average();
                return [.. values.Select(v => double.IsNaN(v) ? mean : v)];
            }

            default: // linearfill
            {
                var sites = new List<double>();
                var known = new List<double>();
                for (int i = 0; i < values.Length; i++)
                {
                    if (!double.IsNaN(values[i]))
                    {
                        sites.Add(i);
                        known.Add(values[i]);
                    }
                }

                var filled = (double[])values.Clone();
                for (int i = 0; i < filled.Length; i++)
                {
                    if (!double.IsNaN(filled[i]) || sites.Count == 0)
                    {
                        continue;
                    }

                    if (sites.Count == 1)
                    {
                        filled[i] = known[0];
                        continue;
                    }

                    int after = sites.BinarySearch(i);
                    after = after >= 0 ? after : ~after;
                    int piece = Math.Clamp(after - 1, 0, sites.Count - 2);
                    double slope = (known[piece + 1] - known[piece]) / (sites[piece + 1] - sites[piece]);
                    filled[i] = known[piece] + (slope * (i - sites[piece]));
                }

                return filled;
            }
        }
    }

    /// <summary>
    /// <c>B = groupfilter(A, G, fun)</c>, or the table form with named grouping variables: the rows
    /// of every group the predicate approves, in their original order.
    /// </summary>
    private static JgsValue GroupFilter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        if (args[0].Type == JgsType.Table)
        {
            Table table = args[0].AsTable;
            string[] groupNames = TableVariableNames("groupfilter", table, args[1], line, col);
            if (args[2].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "groupfilter: the method is a function handle.");
            }

            IJgsCallable predicate = args[2].AsCallable;
            string[] dataNames = args.Count > 3
                ? TableVariableNames("groupfilter", table, args[3], line, col)
                : [.. table.ColumnNames.Where(n => !groupNames.Contains(n, StringComparer.Ordinal))];

            var keyed = new List<GroupingVariable>();
            foreach (string variable in groupNames)
            {
                TableColumn column = TableVariable("groupfilter", table, variable, line, col);
                keyed.Add(column.Type == ColumnType.Text
                    ? new GroupingVariable { Texts = [.. Enumerable.Range(0, table.RowCount).Select(column.GetText)] }
                    : new GroupingVariable { Numbers = [.. Enumerable.Range(0, table.RowCount).Select(column.GetNumber)] });
            }

            (double[] groups, int[] representatives, _) = CountedGroups(keyed, table.RowCount);
            var keep = new bool[table.RowCount];
            for (int g = 1; g <= representatives.Length; g++)
            {
                int[] rows = [.. Enumerable.Range(0, table.RowCount).Where(i => (int)groups[i] == g)];
                bool kept = GroupApproved("groupfilter", predicate, dataNames.Select(
                    n => NumbersOfColumn(table, n, rows)), rows.Length, line, col);
                foreach (int row in rows)
                {
                    keep[row] = kept;
                }
            }

            int[] chosen = [.. Enumerable.Range(0, table.RowCount).Where(i => keep[i])];
            return JgsValue.Table(table.Select(chosen, [.. Enumerable.Range(0, table.ColumnCount)]));
        }

        double[] flat = FlattenColumnMajor("groupfilter", args[0], line, col);
        double[] rawGroups = FlattenColumnMajor("groupfilter", args[1], line, col);
        if (rawGroups.Length != flat.Length)
        {
            throw new JgsRuntimeException(line, col, "groupfilter: one group number per value.");
        }

        if (args[2].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "groupfilter: the method is a function handle.");
        }

        IJgsCallable fun = args[2].AsCallable;
        (double[] numbered, int[] reps, _) = CountedGroups(
            [new GroupingVariable { Numbers = rawGroups }], rawGroups.Length);

        var keepFlat = new bool[flat.Length];
        for (int g = 1; g <= reps.Length; g++)
        {
            int[] rows = [.. Enumerable.Range(0, flat.Length).Where(i => (int)numbered[i] == g)];
            double[] values = [.. rows.Select(i => flat[i])];
            bool kept = GroupApproved(
                "groupfilter", fun,
                [JgsMatrix.FromColumnMajorDims(values, [values.Length, 1])], values.Length, line, col);
            foreach (int row in rows)
            {
                keepFlat[row] = kept;
            }
        }

        double[] chosenValues = [.. Enumerable.Range(0, flat.Length).Where(i => keepFlat[i]).Select(i => flat[i])];
        int[] dims = SizeDims(args[0]);
        bool rowShaped = dims.Length >= 2 && dims[0] == 1 && dims[1] != 1;
        return JgsMatrix.FromColumnMajorDims(
            chosenValues,
            rowShaped ? [1, chosenValues.Length] : [chosenValues.Length, 1]);
    }

    private static JgsValue NumbersOfColumn(Table table, string name, int[] rows)
    {
        TableColumn column = table.Columns.First(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        return column.Type == ColumnType.Text
            ? CellColumn([.. rows.Select(column.GetText)])
            : JgsMatrix.FromColumnMajorDims([.. rows.Select(column.GetNumber)], [rows.Length, 1]);
    }

    /// <summary>Whether the predicate keeps a group: a scalar keeps or drops it whole; a vector must agree.</summary>
    private static bool GroupApproved(
        string name, IJgsCallable predicate, IEnumerable<JgsValue> inputs, int size, int line, int col)
    {
        JgsValue verdict = predicate.Call([.. inputs], line, col);
        double[] flags = FlattenColumnMajor(name, verdict, line, col);
        if (flags.Length != 1 && flags.Length != size)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the method answers one logical per group or one per row.");
        }

        return flags.All(static f => f != 0);
    }

    // --- head, tail, topkrows -----------------------------------------------------------------

    /// <summary>
    /// <c>head(A, k)</c> and <c>tail(A, k)</c>: the first or last <c>k</c> rows of an array or a
    /// table, eight when unasked, everything when there is less than <c>k</c>.
    /// </summary>
    private static JgsValue HeadOrTail(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        int k = 8;
        if (args.Count == 2)
        {
            double raw = Num(name, args, 1, line, col);
            if (raw < 0 || raw != Math.Floor(raw) || double.IsNaN(raw))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:headtail:InvalidK",
                    "Number of rows to return must be a real, nonnegative, integer scalar.");
            }

            k = (int)Math.Min(raw, int.MaxValue);
        }

        if (args[0].Type == JgsType.Table)
        {
            Table table = args[0].AsTable;
            int take = Math.Min(k, table.RowCount);
            int[] rows = name == "head"
                ? [.. Enumerable.Range(0, take)]
                : [.. Enumerable.Range(table.RowCount - take, take)];
            return JgsValue.Table(table.Select(rows, [.. Enumerable.Range(0, table.ColumnCount)]));
        }

        int[] dims = SizeDims(args[0]);
        double[] flat = FlattenColumnMajor(name, args[0], line, col);
        int height = dims.Length > 0 ? dims[0] : 0;
        int width = height == 0 ? 0 : flat.Length / height;
        int keep = Math.Min(k, height);
        int start = name == "head" ? 0 : height - keep;
        var trimmed = new double[keep * width];
        for (int c = 0; c < width; c++)
        {
            Array.Copy(flat, (c * height) + start, trimmed, c * keep, keep);
        }

        var shape = new int[Math.Max(2, dims.Length)];
        shape[1] = dims.Length > 1 ? dims[1] : 1;
        for (int d = 2; d < dims.Length; d++)
        {
            shape[d] = dims[d];
        }

        shape[0] = keep;
        return JgsMatrix.FromColumnMajorDims(trimmed, shape);
    }

    /// <summary>
    /// <c>[B, I] = topkrows(X, k, col, direction)</c>: the top rows under a lexicographic sort —
    /// descending by every column unless told otherwise, ties settled by the next sort column, and
    /// equal rows kept in their original order.
    /// </summary>
    private static JgsValue[] TopRows(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs",
                "Not enough input arguments.");
        }

        int k = Count("topkrows", args, 1, line, col);

        if (args[0].Type == JgsType.Table)
        {
            Table table = args[0].AsTable;
            string[] byNames = args.Count > 2 && TextElementsOf(args[2]) is not null
                && !AllDirectionWords(args[2])
                ? TableVariableNames("topkrows", table, args[2], line, col)
                : [.. table.ColumnNames];
            string[] directions = ReadDirections("topkrows", args, byNames.Length, line, col);
            var byColumns = new List<TableColumn>();
            foreach (string variable in byNames)
            {
                byColumns.Add(TableVariable("topkrows", table, variable, line, col));
            }

            int[] ordered = SortedRowOrder(
                table.RowCount,
                byColumns.Count,
                (r, c) => byColumns[c].Type == ColumnType.Text
                    ? double.NaN
                    : byColumns[c].GetNumber(r),
                (r, c) => byColumns[c].Type == ColumnType.Text ? byColumns[c].GetText(r) : null,
                directions);
            int take = Math.Min(k, table.RowCount);
            int[] top = [.. ordered.Take(take)];
            return Outputs(
                wanted,
                JgsValue.Table(table.Select(top, [.. Enumerable.Range(0, table.ColumnCount)])),
                JgsMatrix.FromColumnMajorDims(
                    [.. top.Select(static r => (double)(r + 1))], [take, take == 0 ? 0 : 1]));
        }

        int[] dims = SizeDims(args[0]);
        if (dims.Count(static d => d > 1) > 2 || dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "topkrows: the data is a matrix or a table.");
        }

        double[] flat = FlattenColumnMajor("topkrows", args[0], line, col);
        int height = dims.Length > 0 ? dims[0] : 0;
        int width = height == 0 ? 0 : flat.Length / height;

        int[] sortColumns;
        if (args.Count > 2 && !IsTextScalar(args[2]) && args[2].Type != JgsType.Cell)
        {
            double[] named = NumericVector("topkrows", args[2], line, col);
            sortColumns = new int[named.Length];
            for (int c = 0; c < named.Length; c++)
            {
                if (named[c] < 1 || named[c] > width || named[c] != Math.Floor(named[c]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:topkrows:ColNotIndexVec",
                        "Column sorting vector must contain positive integers between 1 and the number "
                        + "of columns in the first argument.");
                }

                sortColumns[c] = (int)named[c] - 1;
            }
        }
        else
        {
            sortColumns = [.. Enumerable.Range(0, width)];
        }

        string[] plainDirections = ReadDirections("topkrows", args, sortColumns.Length, line, col);
        int[] order = SortedRowOrder(
            height,
            sortColumns.Length,
            (r, c) => flat[(sortColumns[c] * height) + r],
            static (_, _) => null,
            plainDirections);
        int kept = Math.Min(k, height);
        var picked = new double[kept * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < kept; r++)
            {
                picked[(c * kept) + r] = flat[(c * height) + order[r]];
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajorDims(picked, [kept, width]),
            JgsMatrix.FromColumnMajorDims(
                [.. order.Take(kept).Select(static r => (double)(r + 1))], [kept, kept == 0 ? 0 : 1]));
    }

    /// <summary>Whether every text element of the value is a sort-direction word.</summary>
    private static bool AllDirectionWords(JgsValue value) =>
        TextElementsOf(value) is { Length: > 0 } words
        && words.All(static w => w.ToLowerInvariant() is "ascend" or "descend");

    /// <summary>The sort directions, one per sort column: a word, a cell of words, or all-descending.</summary>
    private static string[] ReadDirections(
        string name, IReadOnlyList<JgsValue> args, int columns, int line, int col)
    {
        JgsValue? given = null;
        for (int i = args.Count - 1; i >= 2; i--)
        {
            if (AllDirectionWords(args[i]))
            {
                given = args[i];
                break;
            }

            if (IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:sortDirection",
                    "Sorting direction must be 'descend' or 'ascend'.");
            }
        }

        if (given is null)
        {
            return [.. Enumerable.Repeat("descend", columns)];
        }

        string[] words = TextElementsOf(given)!;
        foreach (string word in words)
        {
            if (word.ToLowerInvariant() is not ("ascend" or "descend"))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:sortDirection",
                    "Sorting direction must be 'descend' or 'ascend'.");
            }
        }

        if (words.Length == 1 && columns > 1)
        {
            return [.. Enumerable.Repeat(words[0].ToLowerInvariant(), columns)];
        }

        if (words.Length != columns)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: one sort direction, or one per sort column.");
        }

        return [.. words.Select(static w => w.ToLowerInvariant())];
    }

    /// <summary>Row order under a stable lexicographic sort across the sort columns.</summary>
    private static int[] SortedRowOrder(
        int rows, int columns, Func<int, int, double> number, Func<int, int, string?> text,
        string[] directions)
    {
        var order = Enumerable.Range(0, rows).ToArray();
        Array.Sort(order, (a, b) =>
        {
            for (int c = 0; c < columns; c++)
            {
                int sign = directions[c] == "ascend" ? 1 : -1;
                string? aText = text(a, c);
                int result;
                if (aText is not null)
                {
                    result = string.CompareOrdinal(aText, text(b, c));
                }
                else
                {
                    double left = number(a, c);
                    double right = number(b, c);

                    // NaN sorts to the low end of a descending sort and the high end of an
                    // ascending one, which is where MATLAB's row sort puts it.
                    result = left.Equals(right) ? 0
                        : double.IsNaN(left) ? 1
                        : double.IsNaN(right) ? -1
                        : left.CompareTo(right);
                }

                if (result != 0)
                {
                    return sign * result;
                }
            }

            return a.CompareTo(b); // stability: equal rows keep their original order
        });
        return order;
    }
}
