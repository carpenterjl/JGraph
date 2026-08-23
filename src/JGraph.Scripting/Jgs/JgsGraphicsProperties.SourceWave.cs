using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M79's first block: the table a marker chart was drawn from, and the variable behind each of its
/// channels — MATLAB's <c>SourceTable</c> and the <c>*Variable</c> family.
/// <para>
/// M77 declined these names on a scatter because no table-backed scatter existed, exactly as it
/// declined the geographic ones. M78 lifted the same ceiling on a heatmap the moment the chart could
/// remember its table, and this is that decision applied where the table form now exists: naming a
/// different variable re-reads the source and redraws the chart, which is the only thing that makes
/// the name worth answering. The geographic names stay unanswered, and are the whole of what is left.
/// </para>
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>
    /// The source block for a marker chart, flat or spatial. <paramref name="spatial"/> decides which
    /// of the two channels this build cannot serve on the other kind — a height on square paper, a
    /// per-point transparency in space — each of which reads empty and refuses a name.
    /// </summary>
    private static void AddScatterSourceBlock(IDictionary<string, GraphicsProperty> table, bool spatial)
    {
        Put(table, "SourceTable",
            entry => entry.ScatterSource?.Table ?? JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                if (value.Type != JgsType.Table)
                {
                    throw new JgsRuntimeException(line, col,
                        "SourceTable is a table — the one whose variables this chart is drawn from.");
                }

                RequireScatterSource(entry, "SourceTable", line, col).Table = value;
                JgsBuiltins.ReplotFromSource(entry, line, col);
            });

        AddScatterVariable(table, "XVariable",
            source => source.XVariable, (source, name) => source.XVariable = name);
        AddScatterVariable(table, "YVariable",
            source => source.YVariable, (source, name) => source.YVariable = name);
        AddScatterVariable(table, "SizeVariable",
            source => source.SizeVariable, (source, name) => source.SizeVariable = name);
        AddScatterVariable(table, "ColorVariable",
            source => source.ColorVariable, (source, name) => source.ColorVariable = name);

        // The two channels the other kind of marker chart does not have. Each answers empty rather
        // than refusing to be read — a script may ask any chart what feeds its height — and refuses
        // the write by name, which is the shape ZData already takes on a flat chart.
        if (spatial)
        {
            AddScatterVariable(table, "ZVariable",
                source => source.ZVariable, (source, name) => source.ZVariable = name);
            AddAbsentVariable(table, "AlphaVariable",
                "a marker chart in space has no per-point transparency here — draw it with scatter "
                + "and set AlphaVariable there");
        }
        else
        {
            AddScatterVariable(table, "AlphaVariable",
                source => source.AlphaVariable, (source, name) => source.AlphaVariable = name);
            AddAbsentVariable(table, "ZVariable",
                "ZVariable gives a flat chart a height, which this build does not do — draw it with "
                + "scatter3 instead");
        }

        // A circle reads the same two channels as square paper, under the names it uses for them.
        AddPolarVariable(table, "ThetaVariable",
            source => source.XVariable, (source, name) => source.XVariable = name);
        AddPolarVariable(table, "RVariable",
            source => source.YVariable, (source, name) => source.YVariable = name);
    }

    private static void AddScatterVariable(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<ScatterSource, string> read,
        Action<ScatterSource, string> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => JgsValue.Str(entry.ScatterSource is { } source ? read(source) : string.Empty),
            (entry, value, line, col) =>
            {
                write(RequireScatterSource(entry, spelling, line, col),
                    JgsBuiltins.StrOf(spelling, value, line, col));
                JgsBuiltins.ReplotFromSource(entry, line, col);
            });
    }

    /// <summary>
    /// A channel name this kind of chart does not carry: it answers empty, and naming a variable is
    /// refused with the verb that would draw a chart which does carry it.
    /// </summary>
    private static void AddAbsentVariable(
        IDictionary<string, GraphicsProperty> table, string name, string because)
    {
        string spelling = name;
        string reason = because;
        Put(table, spelling,
            static _ => JgsValue.Str(string.Empty),
            (_, value, line, col) =>
            {
                if (JgsBuiltins.StrOf(spelling, value, line, col).Length > 0)
                {
                    throw new JgsRuntimeException(line, col, $"{reason}.");
                }
            });
    }

    /// <summary>
    /// The polar spelling of one of the position channels, answered only by a chart drawn round a
    /// circle. On square paper the name is refused rather than aliased, the way <c>ThetaData</c> is.
    /// </summary>
    private static void AddPolarVariable(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<ScatterSource, string> read,
        Action<ScatterSource, string> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => IsOnPolarAxes(entry) && entry.ScatterSource is { } source
                ? JgsValue.Str(read(source))
                : JgsValue.Str(string.Empty),
            (entry, value, line, col) =>
            {
                if (!IsOnPolarAxes(entry))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling} names a variable of a chart drawn round a circle. This one is on "
                        + $"square paper, where the same channel is "
                        + $"{(spelling == "ThetaVariable" ? "XVariable" : "YVariable")}.");
                }

                write(RequireScatterSource(entry, spelling, line, col),
                    JgsBuiltins.StrOf(spelling, value, line, col));
                JgsBuiltins.ReplotFromSource(entry, line, col);
            });
    }

    private static ScatterSource RequireScatterSource(
        JgsHandleEntry entry, string what, int line, int col) =>
        entry.ScatterSource ?? throw new JgsRuntimeException(line, col,
            $"{what} names a variable of the table a chart was drawn from, and this one was given its "
            + "numbers directly — draw it with scatter(tbl, xvar, yvar) to give it a table.");
}
