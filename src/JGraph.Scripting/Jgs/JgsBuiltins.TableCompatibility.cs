using JGraph.Data;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    private static JgsValue RowNameCell(IReadOnlyList<string>? names)
    {
        var result = JgsValue.Cell((names ?? []).Select(JgsValue.Str).ToArray());
        if (names is not null)
            result.Reshape(names.Count, 1);
        return result;
    }
    private static void RegisterTableCompatibility(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) => env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));
        Define("array2table", (args, line, col) =>
        {
            ArityRange("array2table", args, 1, 5, line, col);
            int rows = args[0].Rows, cols = args[0].Cols;
            double[] values = FlattenColumnMajor("array2table", args[0], line, col);
            var columns = new List<JgsValue>();
            for (int c = 0; c < cols; c++)
                columns.Add(JgsMatrix.FromColumnMajorDims(values.Skip(c * rows).Take(rows).ToArray(), [rows, 1]));
            columns.AddRange(args.Skip(1));
            return BuildTable("array2table", columns, null, line, col);
        });
        Define("readtimetable", (args, line, col) =>
        {
            Table imported = ReadTable("readtimetable", args, line, col, host.readtable, host.readtable).AsTable;
            if (imported.ColumnCount < 2)
                throw new JgsRuntimeException(line, col, "readtimetable requires row times and data variables.");
            TableColumn times = imported[0];
            if (times.Type != ColumnType.DateTime)
            {
                var dates = new double[imported.RowCount];
                for (int r = 0; r < dates.Length; r++)
                    dates[r] = DateTime.Parse(times.GetText(r), System.Globalization.CultureInfo.InvariantCulture).ToOADate();
                times = new DateTimeColumn(times.Name, dates);
            }
            return JgsValue.Table(new Table(imported.Columns.Skip(1).ToArray()) { RowTimes = times });
        });
        Define("istimetable", (args, line, col) => { Arity("istimetable", args, 1, line, col); return JgsValue.Bool(args[0].Type == JgsType.Table && args[0].AsTable.RowTimes is not null); });
        foreach (string name in new[] { "movmean", "movsum", "movmedian", "movmin", "movmax", "movstd", "movvar", "movprod" })
        {
            if (!env.TryGet(name, out JgsValue inner))
                continue;
            Define(name, (args, line, col) =>
            {
                if (args.Count == 0 || args[0].Type != JgsType.Table)
                    return inner.AsCallable.Call(args, line, col);
                Table table = args[0].AsTable;
                string[] selected = table.ColumnNames.ToArray();
                bool replace = true;
                var options = new List<JgsValue>();
                for (int i = 1; i < args.Count; i++)
                {
                    if (args[i].Type == JgsType.String && args[i].AsString == "DataVariables" && i + 1 < args.Count)
                        selected = FieldNameList(name, args[++i], line, col);
                    else if (args[i].Type == JgsType.String && args[i].AsString == "ReplaceValues" && i + 1 < args.Count)
                        replace = args[++i].AsNumber != 0;
                    else
                        options.Add(args[i]);
                }
                foreach (string variable in selected)
                if (!table.TryGetColumn(variable, out _))
                    throw new JgsRuntimeException(line, col, $"{name}: unknown variable '{variable}'.");
                if (table.RowTimes is { Type: ColumnType.DateTime } rowTimes)
                {
                    if (options.Count == 0 || !options[0].IsDuration)
                        throw new JgsRuntimeException(line, col, "A datetime timetable requires a duration window.");
                    options[0] = Untagged(options[0]);
                    options.Add(JgsValue.Str("SamplePoints"));
                    options.Add(JgsMatrix.FromColumnMajorDims(Enumerable.Range(0, table.RowCount).Select(r => rowTimes.GetNumber(r) * JgsTime.MsPerDay).ToArray(), [table.RowCount, 1]));
                }
                var columns = replace ? new List<TableColumn>() : new List<TableColumn>(table.Columns);
                foreach (TableColumn variable in table.Columns)
                {
                    if (!selected.Contains(variable.Name))
                    {
                        if (replace)
                            columns.Add(variable);
                        continue;
                    }
                    var callArgs = new List<JgsValue> { TableColumnValue(table, variable.Name, line, col) };
                    callArgs.AddRange(options);
                    JgsValue result = inner.AsCallable.Call(callArgs, line, col);
                    columns.Add(TableColumnFrom(name, replace ? variable.Name : variable.Name + "_" + name, result, line, col));
                }
                return JgsValue.Table(new Table(columns) { RowNames = table.RowNames, RowTimes = table.RowTimes });
            });
        }
    }
}
