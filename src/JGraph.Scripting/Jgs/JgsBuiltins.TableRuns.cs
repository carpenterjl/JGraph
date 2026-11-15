using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>scatter(tbl, 'Var1', {'Var2', 'Var3'})</c> and <c>scatter3</c>'s like (open item 313, ADR 0360):
/// R2025b makes one chart per variable when a channel names several - a cell of names, a string array
/// or a vector of variable numbers - pairing them off where several channels name several and
/// repeating a channel that names one. Each chart is held beside the ones before it, its legend name is
/// the varying variable, and the axes' NextPlot is what it was (probe p313).
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] TableChannelLetters = ["X", "Y", "Z"];

    /// <summary>
    /// The argument lists of the charts a table call names, one per object, with the varying variable
    /// of each; null when the call names one chart (the common case, read as it always was).
    /// </summary>
    private static List<(IReadOnlyList<JgsValue> Args, string Name)>? TableRuns(
        IReadOnlyList<JgsValue> rest, int channels, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || rest.Count < channels + 1 || rest[0].Type != JgsType.Table)
        {
            return null;
        }

        Table table = rest[0].AsTable;
        var names = new string[channels][];
        bool several = false;
        for (int c = 0; c < channels; c++)
        {
            if (VariablesNamed(table, rest[c + 1]) is not { Length: > 0 } named)
            {
                return null; // not a selector this reads; the plain path says what is wrong
            }

            names[c] = named;
            several |= named.Length > 1;
        }

        if (!several)
        {
            return null;
        }

        int count = 1;
        for (int c = 0; c < channels; c++)
        {
            int here = names[c].Length;
            if (here != 1 && count != 1 && here != count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:NumVariablesMismatch",
                    $"The number of variables specified for '{TableChannelLetters[c]}' did not match the number of "
                    + "variables specified in one of the previous arguments.");
            }

            count = System.Math.Max(count, here);
        }

        int varying = Enumerable.Range(0, channels).Reverse().FirstOrDefault(c => names[c].Length > 1, 0);
        var runs = new List<(IReadOnlyList<JgsValue> Args, string Name)>(count);
        for (int k = 0; k < count; k++)
        {
            var args = new List<JgsValue>(rest.Count) { rest[0] };
            for (int c = 0; c < channels; c++)
            {
                // A channel named by variable numbers passes each number on, which the chart reads
                // back (open item 326, ADR 0372).
                string name = names[c][names[c].Length == 1 ? 0 : k];
                bool numbered = rest[c + 1].Type is JgsType.Number or JgsType.Array && !rest[c + 1].IsCharMatrix
                    && !rest[c + 1].IsStringArray && !IsLogicalValue(rest[c + 1]) && rest[c + 1].Type != JgsType.Cell;
                args.Add(numbered ? JgsValue.Number(IndexOfVariable(table, name) + 1) : JgsValue.Str(name));
            }

            args.AddRange(rest.Skip(channels + 1));
            runs.Add((args, names[varying][names[varying].Length == 1 ? 0 : k]));
        }

        return runs;
    }

    /// <summary>
    /// The variables one channel names: a name, a cell of names, a string array, or variable numbers
    /// counted from one; null for anything else.
    /// </summary>
    private static string[]? VariablesNamed(Table table, JgsValue value)
    {
        if (IsTextScalar(value))
        {
            return [TextOf(value)];
        }

        if (TextElementsOf(value) is { Length: > 0 } many)
        {
            return many;
        }

        if (value.Type is JgsType.Number or JgsType.Array && !value.IsCharMatrix)
        {
            IReadOnlyList<string> columns = table.ColumnNames;
            var picked = new List<string>();
            foreach (double index in ToDoubles("variable", value, 0, 0))
            {
                int at = (int)index;
                if (at != index || at < 1 || at > columns.Count)
                {
                    return null;
                }

                picked.Add(columns[at - 1]);
            }

            return [.. picked];
        }

        return null;
    }

    private static int IndexOfVariable(Table table, string name)
    {
        IReadOnlyList<string> columns = table.ColumnNames;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i] == name)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Makes one chart per run, each held beside the last, named by its varying variable.</summary>
    private static JgsValue ChartPerRun(
        List<(IReadOnlyList<JgsValue> Args, string Name)> runs, Func<IReadOnlyList<JgsValue>, JgsValue> make, int line, int col)
    {
        var made = new List<PlotObject>(runs.Count);
        bool holding = JG.IsHolding;
        try
        {
            foreach ((IReadOnlyList<JgsValue> args, string name) in runs)
            {
                JgsValue handle = make(args);
                if (JgsHandleRegistry.Require(handle, line, col).Target is PlotObject plot)
                {
                    plot.DisplayName = name;
                    made.Add(plot);
                }

                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(holding);
        }

        return HandlesFor(made);
    }
}
