using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave E: <c>swarmchart</c>, <c>swarmchart3</c> and <c>bubblechart3</c> — three verbs that draw
/// no new kind of object between them.
/// <para>
/// A swarm chart is a scatter whose points are nudged aside where they would overlap, and a bubble
/// chart in space is one whose sizes are read as values rather than as areas. Both are properties the
/// scatter objects now carry, so every one of these verbs is <c>scatter</c> or <c>scatter3</c> with a
/// property already set — which is also why <c>scatter(x, y, 'XJitter', 'density')</c> works, exactly
/// as it does in MATLAB.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterSwarmBuiltins(JgsEnvironment env)
    {
        void Silent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Silent("swarmchart", (args, line, col) => SwarmChart(args, line, col));
        Silent("swarmchart3", (args, line, col) => Swarm3(args, line, col, bubbles: false));
        Silent("bubblechart3", (args, line, col) => Swarm3(args, line, col, bubbles: true));
    }

    /// <summary>
    /// <c>swarmchart(x, y)</c>, <c>(x, y, sz)</c> and <c>(x, y, sz, c)</c> with the marker word and the
    /// name/value tail <c>scatter</c> takes, on a named axes or the current one.
    /// <para>
    /// The whole of the difference from <c>scatter</c> is the first line of the body: the sideways
    /// spread starts switched on. A call that says <c>'XJitter', 'none'</c> turns it off again, and
    /// gets a scatter — which is what MATLAB gives too, and is the clearest evidence that this is one
    /// chart with a property rather than two charts.
    /// </para>
    /// </summary>
    private static JgsValue SwarmChart(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            (IReadOnlyList<JgsValue> data, ScatterSource? source) =
                PeelScatterTable("swarmchart", rest, spatial: false, sized: false, line, col);
            return Sourced(ScatterSeries("swarmchart", data, line, col, JitterStyle.Density), source);
        });
    }

    /// <summary>
    /// <c>swarmchart3(x, y, z, …)</c> and <c>bubblechart3(x, y, z, sz, …)</c>: <c>scatter3</c>'s
    /// argument list with one thing already decided — the spread for the first, the reading of the
    /// sizes for the second.
    /// </summary>
    private static JgsValue Swarm3(IReadOnlyList<JgsValue> args, int line, int col, bool bubbles)
    {
        string verb = bubbles ? "bubblechart3" : "swarmchart3";
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            // A table form names its channels rather than passing them, so counting arrays here would
            // read every one of them as missing. The peel inside the series builder does the counting
            // for that shape, and says the same thing when a name is short.
            bool named = rest.Count > 0 && rest[0].Type == JgsType.Table;
            if (bubbles && !named && rest.Count(static value => value.Type != JgsType.String) < 4)
            {
                throw new JgsRuntimeException(line, col,
                    "bubblechart3 needs the sizes as well as the positions: bubblechart3(x, y, z, sz).");
            }

            return Scatter3Series(verb, rest, line, col, bubbles);
        });
    }

    /// <summary>
    /// The word a jitter option is, as the spread it names. The four spellings are MATLAB's, and a
    /// fifth is a misspelling worth stopping for rather than a chart drawn without the spread.
    /// </summary>
    internal static JitterStyle ParseJitter(string what, JgsValue value, int line, int col)
    {
        string word = StrOf(what, value, line, col);
        return word.ToLowerInvariant() switch
        {
            "none" => JitterStyle.None,
            "density" => JitterStyle.Density,
            "rand" => JitterStyle.Rand,
            "randn" => JitterStyle.Randn,
            _ => throw new JgsRuntimeException(line, col,
                $"{what} is one of none, density, rand, randn, but got '{word}'."),
        };
    }

    /// <summary>
    /// The number a jitter width is. Zero hands the width back to the data — which is the only way to
    /// undo one that was set, since there is no <c>'auto'</c> to say it with.
    /// </summary>
    internal static double JitterWidth(string what, JgsValue value, int line, int col)
    {
        double width = NumOf(what, value, line, col);
        if (!double.IsFinite(width) || width < 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is a width of zero or more, zero meaning one worked out from the data.");
        }

        return width;
    }
}
