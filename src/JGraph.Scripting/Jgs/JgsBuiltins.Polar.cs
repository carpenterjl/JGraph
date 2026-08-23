using System.Numerics;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Maths;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M56: the angular family. Polar is a mode an axes is in, not a kind of chart, so what lives here is
/// the verb that puts an axes into that mode and the verbs that draw expecting it.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterPolarBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // polaraxes answers with a handle when a script asks for one, so its bare name has to be that
        // answer rather than the function itself — the rule gca already follows.
        env.Declare("polaraxes", JgsValue.Function(new BuiltinFunction("polaraxes",
            (args, line, col) => PolarAxesVerb(args, line, col))
        {
            BindsAnsAsStatement = false,
            AutoCallsBare = true,
        }));

        DefineSilent("polarplot", (args, line, col) => PolarPlot("polarplot", args, dialect, line, col));
        DefineSilent("polarscatter", (args, line, col) => PolarScatter(args, line, col));
        DefineSilent("polarhistogram", (args, line, col) => PolarHistogram(args, line, col));
        DefineSilent("polarbubblechart", (args, line, col) => PolarBubbleChart(args, line, col));
        DefineSilent("compass", (args, line, col) => Compass(args, line, col));
        DefineSilent("feather", (args, line, col) => Feather(args, line, col));

        // polar drew round a circle before polarplot was the name for it. It is the same chart, and
        // the only reason it is a separate registration is that a script that said polar should be
        // told about polar when something is wrong with the call.
        DefineSilent("polar", (args, line, col) => PolarPlot("polar", args, dialect, line, col));

        // rose is the one angular verb that can answer with data instead of drawing: asked for two
        // outputs it hands back the petal outline, which is what its callers from the days before
        // polarhistogram used it for — the pair feeds straight back into a polar line plot.
        env.Declare("rose", JgsValue.Function(new BuiltinFunction("rose",
            (args, line, col) => Rose(args, line, col))
        {
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => wanted >= 2
                ? RosePath(args, line, col)
                : [Rose(args, line, col)],
        }));
    }

    /// <summary>
    /// <c>polarplot(theta, rho)</c> and every form <c>plot</c> takes — a line spec, repeated groups, a
    /// matrix of columns, a name/value tail — drawn round a circle instead of across square paper.
    /// <para>
    /// The angles are radians, as MATLAB's are, and they are stored as the series' x data: a polar axes
    /// hands the plot a mapper that reads x as θ and y as r, so this is an ordinary line plot that
    /// happens to be somewhere round. <c>polarplot(rho)</c> alone spreads its values over a full turn,
    /// and <c>polarplot(z)</c> reads a complex array as MATLAB does, as angle and magnitude.
    /// </para>
    /// </summary>
    private static JgsValue PolarPlot(
        string verb, IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () => OnPolarAxes(() => PlotCore(
            SplitComplexAngles(verb, rest, line, col),
            dialect,
            line,
            col,
            verb,
            FullTurn)));
    }

    /// <summary>
    /// <c>polarscatter(theta, rho)</c> with the same tail <c>scatter</c> takes: sizes, colours,
    /// <c>'filled'</c>, a marker spec and name/value pairs.
    /// </summary>
    private static JgsValue PolarScatter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () => OnPolarAxes(() =>
        {
            // The table form names theta and rho where the array form passes them, and a circle
            // reads the same two channels as square paper does — so the peel is the shared one.
            (IReadOnlyList<JgsValue> data, ScatterSource? source) =
                PeelScatterTable("polarscatter", rest, spatial: false, sized: false, line, col);
            return Sourced(
                ScatterSeries("polarscatter", SplitComplexAngles("polarscatter", data, line, col), line, col),
                source);
        }));
    }

    /// <summary>
    /// <c>polarbubblechart(theta, rho, sz)</c> and <c>(theta, rho, sz, c)</c>: <c>bubblechart</c>'s
    /// sizing read round a circle. The sizes are data values against the axes' bubble scale rather
    /// than marker areas, exactly as on square paper — being round changes where a bubble sits and
    /// nothing about how big it is.
    /// </summary>
    private static JgsValue PolarBubbleChart(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () => OnPolarAxes(() =>
        {
            (IReadOnlyList<JgsValue> data, ScatterSource? source) =
                PeelScatterTable("polarbubblechart", rest, spatial: false, sized: true, line, col);
            return Sourced(BubbleSeries("polarbubblechart", data, line, col), source);
        }));
    }

    /// <summary>
    /// <c>compass(u, v)</c> and <c>compass(z)</c>: an arrow from the middle of a polar chart out to
    /// each point. The components are Cartesian and the chart is not, so they are turned into a
    /// bearing and a length here — which is the whole of the verb, because an arrow from
    /// <c>(θ, 0)</c> to <c>(θ, r)</c> is an ordinary quiver arrow on an axes that reads x as an angle,
    /// and its head is measured on the drawn shaft rather than in data units, so a radial arrow ends
    /// in a point rather than a fan.
    /// </summary>
    private static JgsValue Compass(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (double[] u, double[] v, IReadOnlyList<JgsValue> tail) =
            ArrowComponents("compass", rest, line, col);

        var bearings = new double[u.Length];
        var lengths = new double[u.Length];
        for (int i = 0; i < u.Length; i++)
        {
            bearings[i] = System.Math.Atan2(v[i], u[i]);
            lengths[i] = System.Math.Sqrt((u[i] * u[i]) + (v[i] * v[i]));
        }

        return OnAxes(named, () => OnPolarAxes(() => Handle(Arrows(
            "compass", bearings, new double[u.Length], new double[u.Length], lengths, tail, line, col))));
    }

    /// <summary>
    /// <c>feather(u, v)</c> and <c>feather(z)</c>: the same arrows laid out along a line instead of
    /// gathered at a point, one per sample, on ordinary square paper. It is in the angular family
    /// because it answers the same question as <c>compass</c> — which way, and how far — and only
    /// differs in showing the order the readings came in.
    /// </summary>
    private static JgsValue Feather(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (double[] u, double[] v, IReadOnlyList<JgsValue> tail) =
            ArrowComponents("feather", rest, line, col);

        return OnAxes(named, () => Handle(Arrows(
            "feather", Ramp1(u.Length), new double[u.Length], u, v, tail, line, col)));
    }

    /// <summary>
    /// The components <c>compass</c> and <c>feather</c> draw, in either of the two ways they are
    /// spelled, plus whatever followed them. A complex array is read as real and imaginary here —
    /// <em>not</em> as angle and magnitude, which is how <c>polarplot</c> reads one: these two verbs
    /// are given components, and that one is given a position.
    /// </summary>
    private static (double[] U, double[] V, IReadOnlyList<JgsValue> Tail) ArrowComponents(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} expects the arrow components: {verb}(u, v) or {verb}(z).");
        }

        double[] u;
        double[] v;
        int used;
        if (IsComplexData(args[0]))
        {
            Complex[] values = ComplexArray(verb, args, 0, line, col);
            u = new double[values.Length];
            v = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                u[i] = values[i].Real;
                v[i] = values[i].Imaginary;
            }

            used = 1;
        }
        else
        {
            if (args.Count < 2 || args[1].Type == JgsType.String)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} takes both components: {verb}(u, v), or one complex array: {verb}(z).");
            }

            u = ToDoubles(verb, args[0], line, col);
            v = ToDoubles(verb, args[1], line, col);
            if (u.Length != v.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: u has {u.Length} values but v has {v.Length}.");
            }

            used = 2;
        }

        return (u, v, [.. args.Skip(used)]);
    }

    /// <summary>
    /// One arrow per reading, with the tail a line spec and the option names <c>quiver</c> takes.
    /// <para>
    /// The scaling is turned off, and that is the point of drawing these through the arrow field
    /// rather than reusing <c>quiver</c> outright: a field of arrows is a sample of something and is
    /// scaled to stay readable, but these arrows <em>are</em> the readings, and one drawn at nine
    /// tenths of its own length would be a chart that lies about its numbers.
    /// </para>
    /// </summary>
    private static QuiverPlot Arrows(
        string verb,
        double[] x,
        double[] y,
        double[] u,
        double[] v,
        IReadOnlyList<JgsValue> tail,
        int line,
        int col)
    {
        (IReadOnlyList<JgsValue> spec, List<(string Name, JgsValue Value)> options) =
            SplitTrailingOptions(tail, QuiverOptionNames);

        QuiverPlot plot = JG.Quiver(x, y, u, v);
        plot.AutoScale = false;
        plot.Scale = 1;

        if (spec.Count > 1)
        {
            // More than one thing left over means a pair went unrecognised, and the first of them is
            // the name that was misspelled — saying so beats reciting the call's shape back.
            string offender = spec[0].Type == JgsType.String ? spec[0].AsString : "that argument";
            throw new JgsRuntimeException(line, col,
                $"{verb}: unknown option '{offender}'. It takes "
                + $"{string.Join(", ", QuiverOptionNames.OrderBy(name => name, StringComparer.Ordinal))}.");
        }

        if (spec.Count == 1 && spec[0].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} takes its components, then a line spec and name/value options.");
        }

        if (spec.Count == 1)
        {
            LineSpec parsed = LineSpec.Parse(spec[0].AsString);
            if (parsed.Color is { } color)
            {
                plot.Color = color;
            }

            if (parsed.Dash == DashStyle.None)
            {
                plot.LineWidth = 0;
            }
        }

        ApplyQuiverOptions(verb, plot, options, line, col);
        return plot;
    }

    private static readonly OptionSpec PolarHistogramOptions = new(
        "polarhistogram",
        Flags: [],
        Names:
        [
            "BinEdges", "BinCounts", "BinWidth", "BinLimits", "BinMethod", "Normalization",
            "DisplayStyle", "FaceColor", "EdgeColor", "FaceAlpha", "EdgeAlpha", "LineWidth",
            "LineStyle", "DisplayName", "HandleVisibility",
        ]);

    /// <summary>
    /// <c>polarhistogram(theta)</c> and the forms round it: a bin count, a set of edges, the counts
    /// themselves, and the option tail <c>histogram</c> takes.
    /// <para>
    /// The bins are chosen by <c>histcounts</c>' own rule, called here rather than reimplemented, so
    /// the wedges a script draws and the numbers it checks them against are the same arithmetic. Two
    /// things are angular rather than general. The angles are wrapped into one turn first, because a
    /// bearing of −π/2 and one of 3π/2 point the same way and a histogram that put them in different
    /// bins would be counting the arithmetic rather than the directions; and the wrapping is skipped
    /// the moment a call names its own edges or limits, since a call that says where its bins go has
    /// said which turn it means.
    /// </para>
    /// </summary>
    private static JgsValue PolarHistogram(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ParsedArgs parsed = PolarHistogramOptions.Parse(rest, 2, line, col);

        double[]? givenEdges = parsed.Vector("BinEdges");
        double[]? givenCounts = parsed.Vector("BinCounts");
        double[]? limits = parsed.Vector("BinLimits");
        double? width = parsed.Named("BinWidth") is null ? null : parsed.Scalar("BinWidth", 0);
        string rule = parsed.Word(
            "BinMethod", "auto", "auto", "scott", "sturges", "sqrt", "fd", "integers");

        if (limits is not null && limits.Length != 2)
        {
            throw new JgsRuntimeException(line, col,
                "polarhistogram: 'BinLimits' takes a [low high] pair of angles.");
        }

        if (width is { } step && (!(step > 0) || !double.IsFinite(step)))
        {
            throw new JgsRuntimeException(line, col,
                "polarhistogram: 'BinWidth' must be a positive angle.");
        }

        double[]? data = null;
        int? requested = null;
        if (givenCounts is not null)
        {
            if (parsed.Positional.Count > 0)
            {
                throw new JgsRuntimeException(line, col,
                    "polarhistogram: 'BinCounts' is the counting already done, so there is no data to count.");
            }

            if (givenEdges is null)
            {
                throw new JgsRuntimeException(line, col,
                    "polarhistogram: 'BinCounts' needs 'BinEdges' to say where the bins are.");
            }
        }
        else
        {
            if (parsed.Positional.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "polarhistogram expects the angles to count, in radians: polarhistogram(theta).");
            }

            data = FlattenColumnMajor("polarhistogram", parsed.Positional[0], line, col);
            if (parsed.Positional.Count == 2)
            {
                JgsValue second = parsed.Positional[1];
                if (second.Type is JgsType.Number or JgsType.Bool)
                {
                    requested = Count("polarhistogram", parsed.Positional, 1, line, col);
                    if (requested < 1)
                    {
                        throw new JgsRuntimeException(line, col, "polarhistogram needs at least one bin.");
                    }
                }
                else
                {
                    givenEdges = ToDoubles("polarhistogram", second, line, col);
                }
            }

            if (givenEdges is null && limits is null)
            {
                data = [.. data.Select(WrapAngle)];
            }
        }

        if (givenEdges is { Length: < 2 })
        {
            throw new JgsRuntimeException(line, col, "polarhistogram: bin edges come in twos or more.");
        }

        if (givenEdges is not null && (requested is not null || width is not null || limits is not null))
        {
            throw new JgsRuntimeException(line, col,
                "polarhistogram: bin edges already say where every bin is, so a count, width or limits cannot be given too.");
        }

        double[] edges = givenEdges ?? Binning.EdgesFor(data!, requested, width, limits, rule);

        return OnAxes(named, () => OnPolarAxes(() =>
        {
            PolarHistogramPlot plot = data is null
                ? JG.PolarHistogramOfCounts(edges, givenCounts!)
                : JG.PolarHistogram(data, edges);
            PolarHistogramOptionTail(plot, parsed, line, col);
            return Handle(plot);
        }));
    }

    /// <summary>The appearance options, applied after the bins are settled.</summary>
    private static void PolarHistogramOptionTail(
        PolarHistogramPlot plot, ParsedArgs parsed, int line, int col)
    {
        plot.Normalization = parsed.Word(
            "Normalization", "count",
            "count", "countdensity", "cumcount", "probability", "pdf", "cdf") switch
        {
            "countdensity" => HistogramNormalization.CountDensity,
            "cumcount" => HistogramNormalization.Cumulative,
            "probability" => HistogramNormalization.Probability,
            "pdf" => HistogramNormalization.Density,
            "cdf" => HistogramNormalization.CumulativeProbability,
            _ => HistogramNormalization.Count,
        };

        plot.DisplayStyle = parsed.Word("DisplayStyle", "bar", "bar", "stairs") == "stairs"
            ? PolarHistogramDisplayStyle.Stairs
            : PolarHistogramDisplayStyle.Bar;

        if (parsed.Named("FaceColor") is { } face)
        {
            plot.FaceColor = OptionColor(face, line, col, "polarhistogram");
        }

        if (parsed.Named("EdgeColor") is { } edge)
        {
            plot.EdgeColor = OptionColor(edge, line, col, "polarhistogram");
        }

        plot.FaceAlpha = parsed.Scalar("FaceAlpha", plot.FaceAlpha);
        plot.EdgeAlpha = parsed.Scalar("EdgeAlpha", plot.EdgeAlpha);
        plot.LineWidth = parsed.Scalar("LineWidth", plot.LineWidth);

        if (parsed.Text("LineStyle") is { } dash)
        {
            plot.LineStyle = ParseDashWord(dash, plot.LineStyle);
        }

        if (parsed.Text("DisplayName") is { } label)
        {
            SetDisplayName(plot, label);
        }

        if (parsed.Text("HandleVisibility") is { } visibility)
        {
            JgsHandleRegistry.Require(JgsHandleRegistry.For(plot), line, col).HandleVisible =
                !visibility.Equals("off", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// <c>rose(theta)</c>: the angular histogram as it was drawn before <c>polarhistogram</c> existed —
    /// petal outlines rather than filled wedges, twenty of them by default, tiling the whole circle
    /// whatever the data covers. That last part is the difference worth knowing: <c>polarhistogram</c>
    /// fits its bins to the angles it was given, and <c>rose</c> always divides the turn.
    /// </summary>
    private static JgsValue Rose(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (double[] angles, double[] radii) = RoseOutline(rest, line, col);
        // An ordinary line, drawn on an axes that reads its x as an angle — which is what MATLAB's rose
        // hands back too, and why its two-output form and polarplot fit together at all.
        return OnAxes(named, () => OnPolarAxes(() => Handle(JG.Plot(angles, radii))));
    }

    /// <summary>
    /// The two vectors <c>[tout, rout] = rose(…)</c> answers with, and no chart. They are the petal
    /// outline itself, so <c>polarplot(tout, rout)</c> draws exactly what <c>rose</c> would have.
    /// </summary>
    private static JgsValue[] RosePath(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (_, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        (double[] angles, double[] radii) = RoseOutline(rest, line, col);
        return [Numbers(angles), Numbers(radii)];
    }

    /// <summary>
    /// The petal outline: four points per bin — out along its first edge, round its top, and back to
    /// the middle — which is one unbroken path because consecutive petals meet at the centre.
    /// </summary>
    private static (double[] Angles, double[] Radii) RoseOutline(
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "rose expects the angles to count, in radians: rose(theta).");
        }

        ArityRange("rose", args, 1, 2, line, col);
        double[] data = [.. FlattenColumnMajor("rose", args[0], line, col).Select(WrapAngle)];

        double[] edges;
        if (args.Count == 1)
        {
            edges = Binning.Spanning(0, System.Math.Tau, 20);
        }
        else if (args[1].Type is JgsType.Number or JgsType.Bool)
        {
            int bins = Count("rose", args, 1, line, col);
            if (bins < 1)
            {
                throw new JgsRuntimeException(line, col, "rose needs at least one bin.");
            }

            edges = Binning.Spanning(0, System.Math.Tau, bins);
        }
        else
        {
            edges = EdgesAround(ToDoubles("rose", args[1], line, col), line, col);
        }

        double[] counts = Binning.Counts(data, edges);
        var angles = new double[counts.Length * 4];
        var radii = new double[counts.Length * 4];
        for (int bin = 0; bin < counts.Length; bin++)
        {
            int at = bin * 4;
            angles[at] = edges[bin];
            angles[at + 1] = edges[bin];
            angles[at + 2] = edges[bin + 1];
            angles[at + 3] = edges[bin + 1];
            radii[at + 1] = counts[bin];
            radii[at + 2] = counts[bin];
        }

        return (angles, radii);
    }

    /// <summary>
    /// The edges around a set of bin centres, which is how <c>rose</c> is told where its petals go:
    /// each boundary sits halfway between two centres, and the two ends reach out as far again.
    /// </summary>
    private static double[] EdgesAround(double[] centers, int line, int col)
    {
        if (centers.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "rose: bin centres cannot be empty.");
        }

        if (centers.Length == 1)
        {
            return [centers[0] - System.Math.PI, centers[0] + System.Math.PI];
        }

        var edges = new double[centers.Length + 1];
        for (int i = 1; i < centers.Length; i++)
        {
            edges[i] = (centers[i - 1] + centers[i]) / 2;
        }

        edges[0] = centers[0] - (edges[1] - centers[0]);
        edges[^1] = centers[^1] + (centers[^1] - edges[^2]);
        return edges;
    }

    /// <summary>A bearing folded into one turn, so that −π/2 and 3π/2 are counted as the same direction.</summary>
    private static double WrapAngle(double theta)
    {
        if (!double.IsFinite(theta))
        {
            return theta;
        }

        double turn = System.Math.Tau;
        return theta - (turn * System.Math.Floor(theta / turn));
    }

    /// <summary>
    /// Draws into a circle. The mode has to be on <em>before</em> the plots land, because the drawing
    /// verb inside replaces the axes on its way in and would wipe a mode set beforehand — so the
    /// replacement happens once here, and the body draws into an axes that is already holding.
    /// </summary>
    private static T OnPolarAxes<T>(Func<T> body)
    {
        bool wasHolding = JG.IsHolding;
        JG.PolarAxes();
        JG.Hold(true);
        try
        {
            return body();
        }
        finally
        {
            JG.Hold(wasHolding);
        }
    }

    /// <summary>
    /// The angles <c>polarplot(rho)</c> draws against: <paramref name="count"/> of them spread over a
    /// full turn. Sample numbers — what an ordinary plot would use — are angles in radians here, so a
    /// forty-point series would wind round the chart six times and mean nothing.
    /// </summary>
    private static double[] FullTurn(int count)
    {
        var angles = new double[count];
        for (int i = 0; i < count; i++)
        {
            angles[i] = count > 1 ? 2 * System.Math.PI * i / (count - 1) : 0;
        }

        return angles;
    }

    /// <summary>
    /// Reads a leading complex array as MATLAB's angular verbs do — <c>polarplot(z)</c> is
    /// <c>polarplot(angle(z), abs(z))</c> — and leaves every other call untouched. Only the first
    /// argument is read this way, and only when nothing numeric follows it, because in every other
    /// form the angles have already been said out loud.
    /// </summary>
    private static IReadOnlyList<JgsValue> SplitComplexAngles(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || !IsComplexData(args[0])
            || (args.Count > 1 && args[1].Type is not JgsType.String))
        {
            return args;
        }

        Complex[] values = ComplexArray(verb, args, 0, line, col);
        var angles = new double[values.Length];
        var radii = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            angles[i] = values[i].Phase;
            radii[i] = Complex.Abs(values[i]);
        }

        var split = new List<JgsValue> { Numbers(angles), Numbers(radii) };
        for (int i = 1; i < args.Count; i++)
        {
            split.Add(args[i]);
        }

        return split;
    }

    /// <summary>Whether a value carries an imaginary part anywhere in it.</summary>
    private static bool IsComplexData(JgsValue value)
    {
        if (value.Type == JgsType.Complex)
        {
            return true;
        }

        if (value.Type != JgsType.Array || value.IsPacked)
        {
            return false;
        }

        if (value.IsPackedComplex)
        {
            return true;
        }

        foreach (JgsValue element in value.AsArray)
        {
            if (element.Type == JgsType.Complex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>polaraxes</c>: makes the current axes a circle and hands back its handle. Given a handle it
    /// makes that axes current instead, which is how MATLAB moves between two polar charts, and any
    /// <c>'Name', value</c> tail is applied through the same property table <c>set</c> uses — so every
    /// property the axes has is settable here the day it is added, with no list to keep in step.
    /// </summary>
    private static JgsValue PolarAxesVerb(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);

        AxesModel axes;
        if (named is not null)
        {
            // Naming an existing axes selects it rather than clearing it: polaraxes(pax) is how a
            // script comes back to a chart it drew earlier, and wiping it would be the opposite.
            JG.MakeCurrent(named);
            named.MakePolar();
            axes = named;
        }
        else
        {
            axes = JG.PolarAxes();
        }

        if (rest.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col,
                "polaraxes takes properties in name/value pairs: polaraxes('ThetaDirection', 'clockwise').");
        }

        JgsValue handle = JgsHandleRegistry.For(axes);
        if (rest.Count > 0 && JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry))
        {
            for (int i = 0; i + 1 < rest.Count; i += 2)
            {
                JgsGraphicsProperties.Set(
                    entry, StrOf("polaraxes", rest[i], line, col), rest[i + 1], line, col);
            }
        }

        return handle;
    }
}
