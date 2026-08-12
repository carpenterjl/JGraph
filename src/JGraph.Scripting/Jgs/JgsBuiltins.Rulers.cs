using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Maths.Ticks;

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
    /// <summary>Which of an axes' rulers a verb is aimed at.</summary>
    private enum RulerSide
    {
        X,
        Y,
        Z,
        R,
        Theta,
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

        // The angular rulers. r is an ordinary scale, so its verbs are the Cartesian machinery pointed
        // at RAxis; θ is stored in degrees whatever unit the axes speaks, so its verbs convert at the
        // boundary and answer through the same spoke arithmetic the renderer draws with.
        Define("rticks", (args, line, col) => OneRuler(
            "rticks", RulerSide.R, args, line, col, JgsRulerTicks.ReadValues, JgsRulerTicks.WriteValues));
        Define("rticklabels", (args, line, col) => OneRuler(
            "rticklabels", RulerSide.R, args, line, col, JgsRulerTicks.ReadLabels, JgsRulerTicks.WriteLabels));
        Define("rtickformat", (args, line, col) => OneRuler(
            "rtickformat", RulerSide.R, args, line, col, JgsRulerTicks.ReadFormat, JgsRulerTicks.WriteFormat));
        Define("rtickangle", (args, line, col) => OneRuler(
            "rtickangle", RulerSide.R, args, line, col, JgsRulerTicks.ReadAngle, JgsRulerTicks.WriteAngle));
        Define("rlim", (args, line, col) => Limits("rlim", RulerSide.R, args, line, col));

        Define("thetaticks", (args, line, col) => OneRuler(
            "thetaticks", RulerSide.Theta, args, line, col, ReadThetaTicks, WriteThetaTicks));
        Define("thetaticklabels", (args, line, col) => OneRuler(
            "thetaticklabels", RulerSide.Theta, args, line, col, ReadThetaLabels, WriteThetaLabels));
        Define("thetatickformat", (args, line, col) => OneRuler(
            "thetatickformat", RulerSide.Theta, args, line, col, JgsRulerTicks.ReadFormat, JgsRulerTicks.WriteFormat));
        Define("thetalim", ThetaLimits);
    }

    /// <summary>
    /// The angular verbs speak about rings and spokes, so an axes that has neither is refused by name
    /// rather than silently configuring rulers no Cartesian chart will draw.
    /// </summary>
    private static void DemandPolar(string verb, AxesModel axes, int line, int col)
    {
        if (!axes.IsPolar)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} aims at a polar axes, but this axes is Cartesian. "
                + "Make one with polaraxes, or draw with a verb such as polarplot first.");
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
        if (side is RulerSide.R or RulerSide.Theta)
        {
            DemandPolar(verb, axes, line, col);
        }

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
        if (side is RulerSide.R or RulerSide.Theta)
        {
            DemandPolar(verb, axes, line, col);
        }

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
        RulerSide.R => axes.RAxis,
        RulerSide.Theta => axes.ThetaAxis,
        _ => axes.ZAxis,
    };

    // --- The θ ruler ------------------------------------------------------------------------------
    //
    // The ruler always holds degrees — a ruler that changed units under its own ticks could not be
    // compared with itself — so the unit the axes speaks is applied here, at the boundary, and the
    // spokes come from the same arithmetic the renderer draws with.

    /// <summary>The unit the θ ruler speaks in: its axes' choice, degrees when it has no axes yet.</summary>
    private static AngleUnits UnitsOf(AxisModel ruler) =>
        ruler.Parent is AxesModel axes ? axes.ThetaAxisUnits : AngleUnits.Degrees;

    private static double FromDegrees(AngleUnits units, double degrees) =>
        units == AngleUnits.Radians ? degrees * System.Math.PI / 180 : degrees;

    private static double ToDegrees(AngleUnits units, double value) =>
        units == AngleUnits.Radians ? value * 180 / System.Math.PI : value;

    private static JgsValue ReadThetaTicks(AxisModel ruler)
    {
        AngleUnits units = UnitsOf(ruler);
        IReadOnlyList<double> degrees = ruler.TickPositions ?? PolarSpokes.Degrees(ruler);
        return JgsGraphicsProperties.Row([.. degrees.Select(d => FromDegrees(units, d))]);
    }

    private static void WriteThetaTicks(string what, AxisModel ruler, JgsValue value, int line, int col)
    {
        if (JgsRulerTicks.ModeWord(what, value, line, col) is { } word)
        {
            ruler.TickPositions = word == "auto"
                ? null
                : [.. PolarSpokes.Degrees(ruler)];
            return;
        }

        JgsRulerTicks.RefuseHandle(what, value, line, col);
        AngleUnits units = UnitsOf(ruler);
        double[] values = ToDoubles(what, value, line, col);
        var degrees = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            degrees[i] = ToDegrees(units, values[i]);
            if (i > 0 && degrees[i] <= degrees[i - 1])
            {
                throw new JgsRuntimeException(line, col,
                    $"{what}: tick values increase, but {values[i - 1]:G6} is followed by {values[i]:G6}.");
            }
        }

        ruler.TickPositions = degrees;
    }

    private static JgsValue ReadThetaLabels(AxisModel ruler)
    {
        AngleUnits units = UnitsOf(ruler);
        IReadOnlyList<double> degrees = PolarSpokes.Degrees(ruler);
        var labels = new JgsValue[degrees.Count];
        for (int i = 0; i < degrees.Count; i++)
        {
            labels[i] = JgsValue.Str(PolarSpokes.Label(ruler, units, degrees[i], i));
        }

        return JgsValue.Cell(labels);
    }

    private static void WriteThetaLabels(string what, AxisModel ruler, JgsValue value, int line, int col)
    {
        if (JgsRulerTicks.ModeWord(what, value, line, col) is { } word)
        {
            if (word == "auto")
            {
                ruler.TickLabelOverrides = null;
                return;
            }

            AngleUnits units = UnitsOf(ruler);
            ruler.TickLabelOverrides = [.. PolarSpokes.Degrees(ruler)
                .Select((d, i) => PolarSpokes.Label(ruler, units, d, i))];
            return;
        }

        ruler.TickLabelOverrides = JgsRulerTicks.LabelWords(what, value, line, col);
    }

    /// <summary>
    /// The visible turn: <c>thetalim</c> reads it, <c>thetalim([0 180])</c> cuts the circle to a wedge,
    /// and <c>'auto'</c> is the whole circle back — θ is never fitted to the data, because the circle
    /// is the chart. A turn asked for beyond 360° is trimmed to one: the chart cannot show an angle
    /// twice, and drawing the circle and a half literally would wind the frame over itself.
    /// </summary>
    private static JgsValue ThetaLimits(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string verb = "thetalim";
        (AxesModel? named, AxisModel? aimed, IReadOnlyList<JgsValue> rest) = PeelRuler(args);
        ArityRange(verb, rest, 0, 2, line, col);

        AxesModel axes = named ?? JG.Gca();
        DemandPolar(verb, axes, line, col);
        AxisModel ruler = aimed ?? axes.ThetaAxis;
        AngleUnits units = UnitsOf(ruler);

        if (rest.Count == 0)
        {
            return JgsGraphicsProperties.Row(
                FromDegrees(units, ruler.Range.Min), FromDegrees(units, ruler.Range.Max));
        }

        if (rest.Count == 1 && rest[0].Type == JgsType.String)
        {
            string word = rest[0].AsString;
            if (word.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                ruler.Range = new Core.Primitives.DataRange(0, 360);
                return JgsValue.Null;
            }

            if (!word.Equals("manual", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"{verb} expects 'auto' or 'manual', but got '{word}'.");
            }

            // The θ ruler never autoscales, so what is showing is already frozen.
            return JgsValue.Null;
        }

        (double low, double high) = LimitPair(verb, rest, line, col);
        double lowDegrees = ToDegrees(units, low);
        double highDegrees = ToDegrees(units, high);
        if (!(highDegrees > lowDegrees))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: the second limit must exceed the first, but got [{low:G6} {high:G6}].");
        }

        if (highDegrees - lowDegrees > 360)
        {
            highDegrees = lowDegrees + 360;
        }

        ruler.Range = new Core.Primitives.DataRange(lowDegrees, highDegrees);
        return JgsValue.Null;
    }

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
