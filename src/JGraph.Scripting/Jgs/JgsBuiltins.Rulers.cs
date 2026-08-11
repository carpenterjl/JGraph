using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M54 wave C: the verbs that aim at one ruler of an axes — where its ticks go, what they read, which
/// way up they are, and how their numbers are written.
/// <para>
/// Each verb is the same shape: name an axes or leave it to <c>gca</c>, then either ask a question or
/// answer one. What the answer means lives in <see cref="JgsRulerTicks"/>, which the MATLAB property
/// spellings share, so <c>xticks(0:5)</c> and <c>set(gca, 'XTick', 0:5)</c> cannot come to mean
/// different things.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Which of an axes' three rulers a verb is aimed at.</summary>
    private enum RulerSide
    {
        X,
        Y,
        Z,
    }

    private static void RegisterRulerBuiltins(JgsEnvironment env)
    {
        // Every one of these answers a question when handed nothing, so a bare name has to be that
        // answer rather than the function itself — otherwise numel(xticks) counts a function.
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        foreach (RulerSide side in new[] { RulerSide.X, RulerSide.Y, RulerSide.Z })
        {
            RulerSide which = side;
            string letter = side.ToString().ToLowerInvariant();

            Define(letter + "ticks", (args, line, col) => OneRuler(
                letter + "ticks", which, args, line, col, JgsRulerTicks.ReadValues, JgsRulerTicks.WriteValues));

            Define(letter + "ticklabels", (args, line, col) => OneRuler(
                letter + "ticklabels", which, args, line, col, JgsRulerTicks.ReadLabels, JgsRulerTicks.WriteLabels));

            Define(letter + "tickangle", (args, line, col) => OneRuler(
                letter + "tickangle", which, args, line, col, JgsRulerTicks.ReadAngle, JgsRulerTicks.WriteAngle));

            Define(letter + "tickformat", (args, line, col) => OneRuler(
                letter + "tickformat", which, args, line, col, JgsRulerTicks.ReadFormat, JgsRulerTicks.WriteFormat));

            Define(letter + "lim", (args, line, col) => Limits(letter + "lim", which, args, line, col));
        }

        // A ruler value and a number are the same thing here: every scale this renderer draws — linear,
        // logarithmic, category, date — stores its data as doubles, so the conversion MATLAB needs for
        // a datetime ruler has nothing to do. The pair still checks that it was handed a ruler, because
        // a script that calls these is asking about one.
        env.Declare("num2ruler", JgsValue.Function(new BuiltinFunction("num2ruler",
            (args, line, col) => SameValue("num2ruler", args, line, col))));
        env.Declare("ruler2num", JgsValue.Function(new BuiltinFunction("ruler2num",
            (args, line, col) => SameValue("ruler2num", args, line, col))));

        // The angular rulers are the same machinery pointed at an axes that does not exist yet. Saying
        // so is more use than "undefined function", and the name is taken by the thing that will fill it.
        foreach (string name in new[]
        {
            "rticks", "thetaticks", "rticklabels", "thetaticklabels",
            "rtickformat", "thetatickformat", "rtickangle", "rlim", "thetalim",
        })
        {
            string angular = name;
            env.Declare(angular, JgsValue.Function(new BuiltinFunction(angular, (_, line, col) =>
                throw new JgsRuntimeException(line, col,
                    $"{angular} requires polar axes, which this build does not draw yet. "
                    + "Use xticks/yticks on a Cartesian axes."))));
        }
    }

    /// <summary>
    /// The shape every tick verb has: peel a named axes off the front, then read with no argument left
    /// or write with one.
    /// </summary>
    private static JgsValue OneRuler(
        string verb,
        RulerSide side,
        IReadOnlyList<JgsValue> args,
        int line,
        int col,
        Func<AxisModel, JgsValue> read,
        Action<string, AxisModel, JgsValue, int, int> write)
    {
        (AxesModel? named, AxisModel? aimed, IReadOnlyList<JgsValue> rest) = PeelRuler(args);
        ArityRange(verb, rest, 0, 1, line, col);

        AxesModel axes = named ?? JG.Gca();
        AxisModel ruler = aimed ?? RulerOf(axes, side);

        if (rest.Count == 0)
        {
            return read(ruler);
        }

        write(verb, ruler, rest[0], line, col);
        return JgsValue.Null;
    }

    /// <summary>
    /// One ruler's visible range: <c>ylim</c> reads it, <c>ylim([0 5])</c> and <c>ylim(0, 5)</c> set it,
    /// and <c>'auto'</c>/<c>'manual'</c> hand the choice back to the data or take it away. On a two-sided
    /// axes this is the side <c>yyaxis</c> made active, which is what makes each side's limits its own.
    /// </summary>
    private static JgsValue Limits(string verb, RulerSide side, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, AxisModel? aimed, IReadOnlyList<JgsValue> rest) = PeelRuler(args);
        ArityRange(verb, rest, 0, 2, line, col);

        AxesModel axes = named ?? JG.Gca();
        AxisModel ruler = aimed ?? RulerOf(axes, side);

        if (rest.Count == 0)
        {
            // The M51 rule: an auto-scaling ruler holds a placeholder until the layout pass fits it, so
            // a script that asks before anything is drawn would otherwise be told 0 to 1.
            if (ruler.AutoScale)
            {
                axes.RecomputeDataBounds();
            }

            return JgsGraphicsProperties.Row(ruler.Range.Min, ruler.Range.Max);
        }

        if (rest.Count == 1 && rest[0].Type == JgsType.String)
        {
            string word = rest[0].AsString;
            if (word.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                ruler.AutoScale = true;
                axes.RecomputeDataBounds();
                return JgsValue.Null;
            }

            if (!word.Equals("manual", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"{verb} expects 'auto' or 'manual', but got '{word}'.");
            }

            // 'manual' freezes what is showing, so the range has to be fitted before it is frozen.
            if (ruler.AutoScale)
            {
                axes.RecomputeDataBounds();
            }

            ruler.AutoScale = false;
            return JgsValue.Null;
        }

        (double low, double high) = LimitPair(verb, rest, line, col);
        if (!(high > low))
        {
            throw new JgsRuntimeException(line, col, $"{verb}: the second limit must exceed the first, but got [{low:G6} {high:G6}].");
        }

        ruler.AutoScale = false;
        ruler.Range = new Core.Primitives.DataRange(low, high);
        return JgsValue.Null;
    }

    /// <summary>The ruler a verb's letter names, with y meaning whichever side <c>yyaxis</c> left active.</summary>
    private static AxisModel RulerOf(AxesModel axes, RulerSide side) => side switch
    {
        RulerSide.X => axes.PrimaryXAxis,
        RulerSide.Y => axes.ActiveYAxis,
        _ => axes.ZAxis,
    };

    /// <summary>The <c>num2ruler</c>/<c>ruler2num</c> body: the value back, once the ruler checks out.</summary>
    private static JgsValue SameValue(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(verb, args, 2, line, col);
        if (!JgsHandleRegistry.TryGet(args[1], out JgsHandleEntry? entry) || entry.Target is not AxisModel)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} wants a ruler as its second argument, such as ax.XAxis.");
        }

        return args[0];
    }
}
