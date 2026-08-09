using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Statistics;
using JGraph.Statistics.Cluster;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Regression;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave J, the drawing half: the toolbox's own plot verbs.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these draws out of the primitives the figure model already has — lines, patches,
/// scatters, bars and text — and none of them adds a plot object. A box plot is five lines and a
/// filled rectangle per group; a dendrogram is a set of right-angled lines; a probability plot is a
/// scatter and a reference line. What makes them worth having is not the drawing but the arithmetic
/// in front of it, which is the part a script would otherwise write again every time.
/// </para>
/// <para>
/// The verbs that answer numbers as well as draw — <c>perfcurve</c>, <c>hist3</c>, <c>ecdf</c> — draw
/// only when the answer was thrown away, which the interpreter tells them by asking for no outputs at
/// all. Everything else draws always, because that is what its name means.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec BoxOptions = new(
        "boxplot",
        [],
        [
            "notch", "symbol", "orientation", "whisker", "labels", "positions", "widths", "colors",
            "plotstyle", "boxstyle", "medianstyle", "datalim", "extrememode", "jitter",
            "labelorientation", "factorgap", "fullfactors", "grouporder",
        ]);

    private static readonly OptionSpec DendrogramOptions = new(
        "dendrogram",
        [],
        ["colorthreshold", "orientation", "labels", "reorder", "checkcrossing"]);

    private static readonly OptionSpec CurveOptions = new(
        "parallelcoords", [], ["group", "standardize", "labels", "quantile", "propedgecolor"]);

    private static readonly OptionSpec BiplotOptions = new(
        "biplot", [], ["scores", "varlabels", "obslabels", "positive", "markersize", "color", "linewidth"]);

    private static readonly OptionSpec GlyphOptions = new(
        "glyphplot", [], ["glyph", "grid", "obslabels", "standardize", "centers", "radius", "features"]);

    private static readonly OptionSpec Hist3Options = new(
        "hist3", [], ["nbins", "ctrs", "edges", "cdatamode", "facealpha", "edgecolor"]);

    private static readonly OptionSpec PerformanceOptions = new(
        "perfcurve",
        [],
        ["xcrit", "ycrit", "weights", "nboot", "alpha", "tvals", "usenearest", "processnan", "prior", "cost"],
        StringPositionals: 3);

    private static readonly OptionSpec EffectOptions = new(
        "maineffectsplot", [], ["varnames", "statistic", "parent"]);

    private static readonly OptionSpec LassoPlotOptions = new(
        "lassoPlot", [], ["plottype", "xscale", "predictornames", "parent"]);

    private static readonly OptionSpec ProbabilityOptions = new(
        "probplot", [], ["noref"], StringPositionals: 1);

    /// <summary>Registers the toolbox's plot verbs.</summary>
    private static void RegisterStatisticsPlotBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        // The two that answer numbers when asked and draw when not.
        void DefineDrawing(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                {
                    MultiOutput = both,
                    KnowsWhenDiscarded = true,
                }));

        DefineBoth("cdfplot", DistributionStaircase);
        Define("histfit", (args, line, col) => HistogramWithFit(args, line, col));
        Define("normplot", (args, line, col) => ProbabilityPlot("normplot", "normal", args, 0, line, col));
        Define("wblplot", (args, line, col) => ProbabilityPlot("wblplot", "weibull", args, 0, line, col));
        Define("probplot", GeneralProbabilityPlot);
        Define("qqplot", QuantileQuantilePlot);

        Define("boxplot", BoxPlot);
        Define("gscatter", GroupedScatter);
        // Both of these are written as bare words — lsline on its own is the whole call — so the name
        // has to call rather than evaluate to the function.
        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("lsline", LeastSquaresLines);
        DefineBare("refline", ReferenceLine);
        Define("refcurve", ReferenceCurve);
        DefineBoth("gplotmatrix", ScatterMatrix);
        DefineBoth("scatterhist", ScatterWithHistograms);

        DefineBoth("dendrogram", Dendrogram);
        Define("manovacluster", GroupMeanCluster);

        Define("andrewsplot", (args, line, col) => CurvePlot("andrewsplot", args, line, col));
        Define("parallelcoords", (args, line, col) => CurvePlot("parallelcoords", args, line, col));
        Define("glyphplot", GlyphPlot);
        DefineBoth("biplot", Biplot);

        DefineDrawing("hist3", (args, wanted, line, col) => BivariateHistogram(args, wanted, line, col));
        Define("addedvarplot", AddedVariablePlot);
        Define("rcoplot", ResidualCaseOrderPlot);
        DefineBoth("capaplot", (args, wanted, line, col) => CapabilityPlot(args, wanted, line, col));
        DefineBoth("normspec", (args, wanted, line, col) => SpecificationPlot(args, wanted, line, col));

        Define("interactionplot", InteractionPlot);
        Define("maineffectsplot", (args, line, col) => EffectsPlot("maineffectsplot", args, line, col));
        Define("multivarichart", (args, line, col) => EffectsPlot("multivarichart", args, line, col));
        Define("lassoPlot", LassoTracePlot);

        DefineDrawing("perfcurve", (args, wanted, line, col) => PerformanceCurve(args, wanted, line, col));
        _ = random;
    }

    // --- Distribution and probability plots ---------------------------------------------------------

    /// <summary><c>[h, stats] = cdfplot(x)</c>: the empirical distribution function, drawn as a staircase.</summary>
    private static JgsValue[] DistributionStaircase(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("cdfplot", args, 1, 1, line, col);
        double[] values = Clean("cdfplot", args[0], line, col);
        Array.Sort(values);

        // Two points per observation, which is what makes the curve rise at a step rather than slope
        // between the observations it knows nothing about.
        var x = new List<double>();
        var y = new List<double>();
        int n = values.Length;
        for (int i = 0; i < n; i++)
        {
            x.Add(values[i]);
            y.Add((double)i / n);
            x.Add(values[i]);
            y.Add((i + 1.0) / n);
        }

        LinePlot curve = JG.Plot([.. x], [.. y]);
        curve.DisplayName = "Empirical CDF";
        JG.XLabel("x");
        JG.YLabel("F(x)");
        JG.Title("Empirical CDF");

        JgsValue stats = Structure(
            ("min", JgsValue.Number(values[0])),
            ("max", JgsValue.Number(values[^1])),
            ("mean", JgsValue.Number(DescriptiveStatistics.Mean(values))),
            ("median", JgsValue.Number(DescriptiveStatistics.Median(values))),
            ("std", JgsValue.Number(DescriptiveStatistics.StandardDeviation(values, population: false))));

        return Outputs(wanted, LineHandle(curve), stats);
    }

    /// <summary><c>h = histfit(data, nbins, dist)</c>: a histogram with a fitted density over it.</summary>
    private static JgsValue HistogramWithFit(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("histfit", args, 1, 3, line, col);
        double[] values = Clean("histfit", args[0], line, col);
        if (values.Length < 2)
        {
            throw new JgsRuntimeException(line, col, "histfit needs at least two observations.");
        }

        int bins = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Count("histfit", args, 1, line, col)
            : (int)Math.Ceiling(Math.Sqrt(values.Length));

        string family = args.Count > 2 ? Str("histfit", args, 2, line, col) : "normal";

        HistogramPlot bars = JG.Histogram(values, bins);
        bool wasHolding = JG.IsHolding;
        JG.Hold(true);

        double low = values[0];
        double high = values[0];
        foreach (double value in values)
        {
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        double width = (high - low) / bins;
        if (!(width > 0))
        {
            width = 1;
        }

        // The density integrates to one and the bars count observations, so the curve is scaled by how
        // many observations one unit of density is worth: the count times the width of a bin.
        Func<double, double> density = FittedDensity("histfit", family, values, line, col);
        const int Points = 200;
        var x = new double[Points];
        var y = new double[Points];
        for (int i = 0; i < Points; i++)
        {
            x[i] = low - width + ((high - low + (2 * width)) * i / (Points - 1.0));
            y[i] = density(x[i]) * values.Length * width;
        }

        LinePlot curve = JG.Plot(x, y);
        curve.DisplayName = family;
        JG.Hold(wasHolding);

        return HandleRow([bars, curve]);
    }

    /// <summary>The density of a named family fitted to a sample, for the curve a histogram wears.</summary>
    private static Func<double, double> FittedDensity(
        string verb, string family, double[] values, int line, int col)
    {
        string name = family.ToLowerInvariant();
        if (name is "kernel")
        {
            double bandwidth = EmpiricalDistribution.DefaultBandwidth(values);
            return x => EmpiricalDistribution.KernelDensity(
                values,
                null,
                [x],
                bandwidth,
                EmpiricalDistribution.Kernel.Normal,
                EmpiricalDistribution.SmoothedKind.Pdf,
                double.NegativeInfinity,
                double.PositiveInfinity,
                EmpiricalDistribution.BoundaryRule.Log)[0];
        }

        DistributionFamily? found = ContinuousFamilies.Find(name) ?? DiscreteFamilies.Find(name);
        if (found is null)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: '{family}' is not a distribution this build can fit. Try 'normal', 'lognormal', "
                + "'gamma', 'exponential', 'weibull', 'rayleigh', 'poisson' or 'kernel'.");
        }

        DistributionFitting.Sample sample = DistributionFitting.MakeSample(values, null, null);
        double[] parameters = Guarded(verb, () => DistributionFitting.Fit(found, sample, 0.05).Parameters, line, col);
        return x => found.Pdf(x, parameters);
    }

    /// <summary>
    /// A probability plot: the sorted sample against what the family says the same plotting positions
    /// should have been, plus a reference line through the quartiles.
    /// </summary>
    /// <remarks>
    /// MATLAB's version draws on an axis whose tick positions are probabilities and whose spacing is the
    /// family's own quantile function. JGraph has no such scale, so the vertical axis here carries the
    /// quantile itself and is labelled with what it is — the points and the reference line are in the
    /// same places either way, which is what the plot is read for.
    /// </remarks>
    private static JgsValue ProbabilityPlot(
        string verb, string family, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (args.Count <= index)
        {
            throw new JgsRuntimeException(line, col, $"{verb} needs some data.");
        }

        double[] values = Clean(verb, args[index], line, col);
        if (values.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{verb} needs at least two observations.");
        }

        Array.Sort(values);
        DistributionFamily found = ContinuousFamilies.Find(family)
            ?? throw new JgsRuntimeException(line, col,
                $"{verb}: '{family}' is not a distribution this build knows.");

        bool logScale = family is "weibull" or "lognormal";
        var x = new double[values.Length];
        var y = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double p = (i + 0.5) / values.Length;
            x[i] = logScale ? Math.Log(Math.Max(values[i], 1e-300)) : values[i];
            y[i] = StandardQuantile(found, family, p);
        }

        LinePlot points = JG.Plot(x, y, "+");
        points.DisplayName = "Data";

        bool wasHolding = JG.IsHolding;
        JG.Hold(true);

        // The reference line goes through the first and third quartiles, which is where MATLAB puts it
        // and is what makes a curve away from it mean the tails rather than the middle.
        double[] quartiles = DescriptiveStatistics.Percentiles(values, [25, 75]);
        double x1 = logScale ? Math.Log(Math.Max(quartiles[0], 1e-300)) : quartiles[0];
        double x2 = logScale ? Math.Log(Math.Max(quartiles[1], 1e-300)) : quartiles[1];
        double y1 = StandardQuantile(found, family, 0.25);
        double y2 = StandardQuantile(found, family, 0.75);

        LinePlot reference = points;
        if (x2 > x1)
        {
            double slope = (y2 - y1) / (x2 - x1);
            double from = x[0] - (0.05 * (x[^1] - x[0]));
            double to = x[^1] + (0.05 * (x[^1] - x[0]));
            reference = JG.Plot([from, to], [y1 + (slope * (from - x1)), y1 + (slope * (to - x1))], "r--");
            reference.DisplayName = "Reference";
        }

        JG.Hold(wasHolding);
        JG.XLabel(logScale ? "log(Data)" : "Data");
        JG.YLabel($"Quantile of {found.Name}");
        JG.Title($"{found.Name} probability plot");

        return HandleRow([points, reference]);
    }

    /// <summary>The quantile a probability plot puts on its vertical axis, in the family's own scale.</summary>
    private static double StandardQuantile(DistributionFamily family, string name, double p) => name switch
    {
        "weibull" => Math.Log(-Math.Log(1 - p)),
        "lognormal" => ContinuousDistributions.NormalInv(p, 0, 1),
        "exponential" => -Math.Log(1 - p),
        _ => family.Inv(p, StandardParameters(family)),
    };

    /// <summary>The family's parameters in their standard form, which is what a plot's axis is in.</summary>
    private static double[] StandardParameters(DistributionFamily family)
    {
        var parameters = new double[family.ParameterNames.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = family.PositiveParameters[i] ? 1 : 0;
        }

        return parameters;
    }

    /// <summary><c>probplot(dist, y)</c>: the same plot against any family the build knows.</summary>
    private static JgsValue GeneralProbabilityPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "probplot needs some data.");
        }

        ParsedArgs parsed = ProbabilityOptions.Parse(args, 3, line, col);
        int index = 0;
        string family = "normal";
        if (parsed.Positional[0].Type == JgsType.String)
        {
            family = parsed.Positional[0].AsString;
            index = 1;
        }

        if (parsed.Positional.Count <= index)
        {
            throw new JgsRuntimeException(line, col, "probplot needs some data as well as a family.");
        }

        if (parsed.Positional.Count > index + 1)
        {
            throw new JgsRuntimeException(line, col,
                "probplot: censoring and frequency are not read here — the plotting positions this draws "
                + "count every observation once.");
        }

        return ProbabilityPlot("probplot", family.ToLowerInvariant(), parsed.Positional, index, line, col);
    }

    /// <summary>
    /// <c>qqplot(x)</c> and <c>qqplot(x, y)</c>: one sample's quantiles against a normal's, or against
    /// another sample's.
    /// </summary>
    private static JgsValue QuantileQuantilePlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("qqplot", args, 1, 3, line, col);
        double[] first = Clean("qqplot", args[0], line, col);
        if (first.Length < 2)
        {
            throw new JgsRuntimeException(line, col, "qqplot needs at least two observations.");
        }

        Array.Sort(first);
        double[] probabilities = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? NumericVector("qqplot", args[2], line, col)
            : PlottingPositions(first.Length);

        double[] x;
        double[] y = DescriptiveStatistics.Percentiles(first, Percentages(probabilities));
        string label;

        if (args.Count > 1 && !IsPlaceholderValue(args[1]))
        {
            if (TryReadDistribution(args[1], out DistributionObject? distribution, out _) && distribution is not null)
            {
                x = Array.ConvertAll(probabilities, distribution.Inv);
                label = "Quantiles of the distribution";
            }
            else
            {
                double[] second = Clean("qqplot", args[1], line, col);
                Array.Sort(second);
                x = DescriptiveStatistics.Percentiles(second, Percentages(probabilities));
                label = "Quantiles of the second sample";
            }
        }
        else
        {
            x = Array.ConvertAll(probabilities, p => ContinuousDistributions.NormalInv(p, 0, 1));
            label = "Standard normal quantiles";
        }

        LinePlot points = JG.Plot(x, y, "+");
        points.DisplayName = "Quantiles";

        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        LinePlot reference = QuartileLine(x, y);
        JG.Hold(wasHolding);

        JG.XLabel(label);
        JG.YLabel("Quantiles of the sample");
        JG.Title("Quantile-quantile plot");
        return HandleRow([points, reference]);
    }

    /// <summary>The line through the two quartile points of a pair of quantile sets.</summary>
    private static LinePlot QuartileLine(double[] x, double[] y)
    {
        double[] xq = DescriptiveStatistics.Percentiles(x, [25, 75]);
        double[] yq = DescriptiveStatistics.Percentiles(y, [25, 75]);
        double slope = xq[1] > xq[0] ? (yq[1] - yq[0]) / (xq[1] - xq[0]) : 1;

        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double one in x)
        {
            low = Math.Min(low, one);
            high = Math.Max(high, one);
        }

        LinePlot reference = JG.Plot(
            [low, high], [yq[0] + (slope * (low - xq[0])), yq[0] + (slope * (high - xq[0]))], "r--");
        reference.DisplayName = "Reference";
        return reference;
    }

    private static double[] PlottingPositions(int n)
    {
        var p = new double[n];
        for (int i = 0; i < n; i++)
        {
            p[i] = (i + 0.5) / n;
        }

        return p;
    }

    private static double[] Percentages(double[] probabilities) =>
        Array.ConvertAll(probabilities, p => p * 100);

    // --- Box plots ------------------------------------------------------------------------------------

    /// <summary><c>boxplot(x, g)</c>: a box and whisker per group.</summary>
    private static JgsValue BoxPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "boxplot needs some data.");
        }

        ParsedArgs parsed = BoxOptions.Parse(args, 2, line, col);
        RefuseBoxStyles(parsed, line, col);

        (double[][] groups, string[] names) = BoxGroups(parsed, line, col);
        bool notched = parsed.Named("notch") is { } notch && Switched(notch);
        bool horizontal = parsed.Word("orientation", "vertical", "vertical", "horizontal") == "horizontal";
        double whisker = parsed.Scalar("whisker", 1.5);
        if (whisker < 0)
        {
            throw new JgsRuntimeException(line, col, "boxplot: 'Whisker' cannot be negative.");
        }

        double[] positions = parsed.Named("positions") is { } given
            ? NumericVector("boxplot", given, line, col)
            : LevelPositions(groups.Length);

        if (positions.Length != groups.Length)
        {
            throw new JgsRuntimeException(line, col, "boxplot: 'Positions' needs one position per group.");
        }

        double[] widths = parsed.Named("widths") is { } sizes
            ? NumericVector("boxplot", sizes, line, col)
            : [0.5];

        if (parsed.Named("labels") is { } labels)
        {
            names = TextElements("boxplot", labels, line, col);
            if (names.Length != groups.Length)
            {
                throw new JgsRuntimeException(line, col, "boxplot: 'Labels' needs one label per group.");
            }
        }

        string symbol = parsed.Text("symbol") ?? "r+";
        Color? fill = parsed.Named("colors") is { } colours
            ? OptionColor(colours, line, col, "boxplot")
            : null;

        bool wasHolding = JG.IsHolding;
        var drawn = new List<PlotObject>();
        try
        {
            for (int g = 0; g < groups.Length; g++)
            {
                drawn.AddRange(DrawBox(
                    groups[g],
                    positions[g],
                    widths[g % widths.Length],
                    notched,
                    horizontal,
                    whisker,
                    symbol,
                    fill));
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        LabelBoxAxis(names, positions, horizontal);
        return HandleRow(drawn);
    }

    /// <summary>One box, its whiskers, its median and its outliers.</summary>
    private static List<PlotObject> DrawBox(
        double[] values,
        double centre,
        double width,
        bool notched,
        bool horizontal,
        double whisker,
        string symbol,
        Color? fill)
    {
        var drawn = new List<PlotObject>();
        double[] clean = DescriptiveStatistics.WithoutNaN(values);
        if (clean.Length == 0)
        {
            return drawn;
        }

        Array.Sort(clean);
        double[] quartiles = DescriptiveStatistics.Percentiles(clean, [25, 50, 75]);
        double q1 = quartiles[0];
        double median = quartiles[1];
        double q3 = quartiles[2];
        double reach = whisker * (q3 - q1);

        double low = q1;
        double high = q3;
        var outliers = new List<double>();
        foreach (double value in clean)
        {
            if (value < q1 - reach || value > q3 + reach)
            {
                outliers.Add(value);
            }
            else
            {
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        }

        double half = width / 2;

        // The notch is a wedge cut into the sides at the median, and its depth is the interval within
        // which two medians are not distinguishable — which is what makes two non-overlapping notches
        // mean something.
        double notch = notched
            ? 1.57 * (q3 - q1) / Math.Sqrt(clean.Length)
            : 0;

        double[] boxX = notched
            ?
            [
                centre - half, centre - half, centre - (half * 0.5), centre - half, centre - half,
                centre + half, centre + half, centre + (half * 0.5), centre + half, centre + half,
            ]
            : [centre - half, centre - half, centre + half, centre + half];

        double[] boxY = notched
            ?
            [
                q1, median - notch, median, median + notch, q3,
                q3, median + notch, median, median - notch, q1,
            ]
            : [q1, q3, q3, q1];

        PatchPlot box = Draw(horizontal, boxX, boxY, fill ?? Colors.White);
        box.EdgeColor = Colors.Black;
        box.DisplayName = "Box";
        drawn.Add(box);

        drawn.Add(Segment(horizontal, [centre - half, centre + half], [median, median], "r-"));
        drawn.Add(Segment(horizontal, [centre, centre], [q3, high], "k--"));
        drawn.Add(Segment(horizontal, [centre, centre], [low, q1], "k--"));
        drawn.Add(Segment(horizontal, [centre - (half / 2), centre + (half / 2)], [high, high], "k-"));
        drawn.Add(Segment(horizontal, [centre - (half / 2), centre + (half / 2)], [low, low], "k-"));

        if (outliers.Count > 0)
        {
            var at = new double[outliers.Count];
            Array.Fill(at, centre);
            drawn.Add(Segment(horizontal, at, [.. outliers], symbol));
        }

        return drawn;
    }

    private static PatchPlot Draw(bool horizontal, double[] x, double[] y, Color fill) =>
        horizontal ? JG.Fill(y, x, fill) : JG.Fill(x, y, fill);

    private static LinePlot Segment(bool horizontal, double[] x, double[] y, string spec)
    {
        LinePlot drawn = horizontal ? JG.Plot(y, x, spec) : JG.Plot(x, y, spec);
        JG.Hold(true);
        return drawn;
    }

    private static void LabelBoxAxis(string[] names, double[] positions, bool horizontal)
    {
        _ = positions;
        string joined = string.Join(", ", names);
        if (horizontal)
        {
            JG.YLabel(joined);
            JG.XLabel("Values");
        }
        else
        {
            JG.XLabel(joined);
            JG.YLabel("Values");
        }
    }

    /// <summary>The groups a box plot draws: the columns of a matrix, or the data cut by a grouping.</summary>
    private static (double[][] Groups, string[] Names) BoxGroups(ParsedArgs parsed, int line, int col)
    {
        if (parsed.Positional.Count > 1)
        {
            return Grouped("boxplot", [parsed.Positional[0], parsed.Positional[1]], line, col);
        }

        (double[] flat, int rows, int columns) = DenseMatrix("boxplot", parsed.Positional[0], line, col);
        if (rows == 1 || columns == 1)
        {
            return ([flat], ["1"]);
        }

        var groups = new double[columns][];
        var names = new string[columns];
        for (int c = 0; c < columns; c++)
        {
            groups[c] = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                groups[c][r] = flat[r + (c * rows)];
            }

            names[c] = (c + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return (groups, names);
    }

    private static void RefuseBoxStyles(ParsedArgs parsed, int line, int col)
    {
        foreach (string word in new[]
                 {
                     "plotstyle", "boxstyle", "medianstyle", "datalim", "extrememode", "jitter",
                     "labelorientation", "factorgap", "fullfactors", "grouporder",
                 })
        {
            if (parsed.Named(word) is not null)
            {
                throw new JgsRuntimeException(line, col,
                    $"boxplot: '{word}' changes how the box is drawn rather than what it says. The box "
                    + "here is the traditional one — outline, median, whiskers and outliers.");
            }
        }
    }

    /// <summary>The positions 1..n, which is where a group or a level sits along an axis.</summary>
    private static double[] LevelPositions(int count)
    {
        var positions = new double[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = i + 1;
        }

        return positions;
    }

    private static bool Switched(JgsValue value) =>
        value.Type == JgsType.String
            ? value.AsString.Equals("on", StringComparison.OrdinalIgnoreCase)
            : value.IsTruthy;

    // --- Scatter families -------------------------------------------------------------------------------

    /// <summary><c>gscatter(x, y, g, clr, sym, siz)</c>: a scatter with one colour per group.</summary>
    private static JgsValue GroupedScatter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("gscatter", args, 3, 9, line, col);
        double[] x = FlattenColumnMajor("gscatter", args[0], line, col);
        double[] y = FlattenColumnMajor("gscatter", args[1], line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, "gscatter: x and y must be the same length.");
        }

        (int[] index, string[] names) = GroupIndex("gscatter", args[2], line, col);
        if (index.Length != x.Length)
        {
            throw new JgsRuntimeException(line, col, "gscatter: the grouping needs one label per point.");
        }

        string colours = args.Count > 3 && !IsPlaceholderValue(args[3])
            ? Str("gscatter", args, 3, line, col)
            : "bgrcmyk";
        string symbols = args.Count > 4 && !IsPlaceholderValue(args[4])
            ? Str("gscatter", args, 4, line, col)
            : ".";
        double size = args.Count > 5 && !IsPlaceholderValue(args[5])
            ? Num("gscatter", args, 5, line, col)
            : 0;

        bool legend = args.Count <= 6 || IsPlaceholderValue(args[6]) || Switched(args[6]);

        bool wasHolding = JG.IsHolding;
        var drawn = new List<PlotObject>();
        try
        {
            for (int g = 0; g < names.Length; g++)
            {
                var gx = new List<double>();
                var gy = new List<double>();
                for (int i = 0; i < index.Length; i++)
                {
                    if (index[i] == g)
                    {
                        gx.Add(x[i]);
                        gy.Add(y[i]);
                    }
                }

                if (gx.Count == 0)
                {
                    continue;
                }

                string spec = string.Concat(colours[g % colours.Length], symbols[g % symbols.Length]);
                LinePlot points = JG.Plot([.. gx], [.. gy], spec);
                points.DisplayName = names[g];
                if (size > 0)
                {
                    points.MarkerSize = size;
                }

                drawn.Add(points);
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        if (args.Count > 7 && !IsPlaceholderValue(args[7]))
        {
            JG.XLabel(Str("gscatter", args, 7, line, col));
        }

        if (args.Count > 8 && !IsPlaceholderValue(args[8]))
        {
            JG.YLabel(Str("gscatter", args, 8, line, col));
        }

        if (legend && drawn.Count > 0)
        {
            JG.Legend(JG.Gca(), [.. drawn]);
        }

        return HandleRow(drawn);
    }

    /// <summary><c>lsline</c>: a least-squares line through every series already drawn.</summary>
    private static JgsValue LeastSquaresLines(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("lsline", rest, 0, 0, line, col);

        return OnAxes(named, () =>
        {
            AxesModel axes = JG.Gca();
            var drawn = new List<PlotObject>();
            bool wasHolding = JG.IsHolding;
            JG.Hold(true);
            try
            {
                foreach (PlotObject plot in axes.Plots.InDrawOrder().ToArray())
                {
                    if (plot is not LinePlot series || series.Data.Count < 2)
                    {
                        continue;
                    }

                    var x = new double[series.Data.Count];
                    var y = new double[series.Data.Count];
                    for (int i = 0; i < x.Length; i++)
                    {
                        x[i] = series.Data.GetX(i);
                        y[i] = series.Data.GetY(i);
                    }

                    double[] fitted = Guarded("lsline", () => StraightFit(x, y), line, col);
                    double low = x[0];
                    double high = x[0];
                    foreach (double one in x)
                    {
                        low = Math.Min(low, one);
                        high = Math.Max(high, one);
                    }

                    LinePlot fit = JG.Plot(
                        [low, high],
                        [Polynomial(fitted, low), Polynomial(fitted, high)]);
                    fit.DashStyle = DashStyle.Dash;
                    fit.DisplayName = "Least-squares fit";
                    drawn.Add(fit);
                }
            }
            finally
            {
                JG.Hold(wasHolding);
            }

            if (drawn.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "lsline: there is nothing plotted to fit a line to.");
            }

            return HandleRow(drawn);
        });
    }

    /// <summary><c>refline(m, b)</c>, <c>refline(coeffs)</c>, <c>refline</c>: a straight reference line.</summary>
    private static JgsValue ReferenceLine(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("refline", rest, 0, 2, line, col);

        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                return LeastSquaresLines([], line, col);
            }

            double slope;
            double intercept;
            if (rest.Count == 1)
            {
                double[] pair = NumericVector("refline", rest[0], line, col);
                if (pair.Length != 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "refline: one argument is the [slope intercept] pair.");
                }

                slope = pair[0];
                intercept = pair[1];
            }
            else
            {
                slope = Num("refline", rest, 0, line, col);
                intercept = Num("refline", rest, 1, line, col);
            }

            (double low, double high) = CurrentXRange();
            bool wasHolding = JG.IsHolding;
            JG.Hold(true);
            LinePlot drawn = JG.Plot([low, high], [intercept + (slope * low), intercept + (slope * high)]);
            drawn.DashStyle = DashStyle.Dash;
            drawn.DisplayName = "Reference line";
            JG.Hold(wasHolding);
            return LineHandle(drawn);
        });
    }

    /// <summary><c>refcurve(p)</c>: the polynomial with those coefficients, over the current axes.</summary>
    private static JgsValue ReferenceCurve(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("refcurve", rest, 1, 1, line, col);

        return OnAxes(named, () =>
        {
            double[] coefficients = NumericVector("refcurve", rest[0], line, col);
            (double low, double high) = CurrentXRange();

            const int Points = 200;
            var x = new double[Points];
            var y = new double[Points];
            for (int i = 0; i < Points; i++)
            {
                x[i] = low + ((high - low) * i / (Points - 1.0));
                y[i] = Polynomial(coefficients, x[i]);
            }

            bool wasHolding = JG.IsHolding;
            JG.Hold(true);
            LinePlot drawn = JG.Plot(x, y);
            drawn.DashStyle = DashStyle.Dash;
            drawn.DisplayName = "Reference curve";
            JG.Hold(wasHolding);
            return LineHandle(drawn);
        });
    }

    /// <summary>The polynomial with the given coefficients, highest power first.</summary>
    private static double Polynomial(double[] coefficients, double x)
    {
        double total = 0;
        foreach (double coefficient in coefficients)
        {
            total = (total * x) + coefficient;
        }

        return total;
    }

    /// <summary>The horizontal span of what is already drawn, which is what a reference line covers.</summary>
    private static (double Low, double High) CurrentXRange()
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (PlotObject plot in JG.Gca().Plots.InDrawOrder())
        {
            JGraph.Core.Primitives.DataRange bounds = plot.GetXDataBounds();
            if (!double.IsNaN(bounds.Min) && !double.IsNaN(bounds.Max))
            {
                low = Math.Min(low, bounds.Min);
                high = Math.Max(high, bounds.Max);
            }
        }

        return double.IsInfinity(low) || high <= low ? (0, 1) : (low, high);
    }

    /// <summary><c>[h, ax, bigax] = gplotmatrix(x, y, g)</c>: a grid of scatters, one per pair.</summary>
    private static JgsValue[] ScatterMatrix(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("gplotmatrix", args, 1, 6, line, col);
        (double[] left, int rows, int columns) = DenseMatrix("gplotmatrix", args[0], line, col);

        bool paired = args.Count > 1 && !IsPlaceholderValue(args[1]);
        (double[] right, int otherRows, int otherColumns) = paired
            ? DenseMatrix("gplotmatrix", args[1], line, col)
            : (left, rows, columns);

        if (otherRows != rows)
        {
            throw new JgsRuntimeException(line, col,
                "gplotmatrix: both sets of variables must be measured on the same observations.");
        }

        int[] index = [];
        string[] names = [];
        if (args.Count > 2 && !IsPlaceholderValue(args[2]))
        {
            (index, names) = GroupIndex("gplotmatrix", args[2], line, col);
        }

        var drawn = new List<PlotObject>();
        var axes = new List<double>();
        for (int r = 0; r < otherColumns; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                AxesModel cell = JG.Subplot(otherColumns, columns, (r * columns) + c + 1);
                axes.Add(JgsHandleRegistry.For(JgsHandleKind.Axes, cell).AsNumber);

                double[] x = Column(left, rows, c);
                double[] y = Column(right, rows, r);

                if (index.Length == rows && names.Length > 0)
                {
                    bool wasHolding = JG.IsHolding;
                    for (int g = 0; g < names.Length; g++)
                    {
                        var gx = new List<double>();
                        var gy = new List<double>();
                        for (int i = 0; i < rows; i++)
                        {
                            if (index[i] == g)
                            {
                                gx.Add(x[i]);
                                gy.Add(y[i]);
                            }
                        }

                        if (gx.Count > 0)
                        {
                            LinePlot points = JG.Plot([.. gx], [.. gy], ".");
                            points.DisplayName = names[g];
                            drawn.Add(points);
                            JG.Hold(true);
                        }
                    }

                    JG.Hold(wasHolding);
                }
                else if (!paired && r == c)
                {
                    drawn.Add(JG.Histogram(x, Math.Max((int)Math.Sqrt(rows), 1)));
                }
                else
                {
                    drawn.Add(JG.Plot(x, y, "."));
                }
            }
        }

        return Outputs(
            wanted,
            HandleRow(drawn),
            axes.Count == 1
                ? JgsValue.Number(axes[0])
                : JgsMatrix.FromColumnMajor([.. axes], otherColumns, columns),
            JgsHandleRegistry.For(JgsHandleKind.Axes, JG.Gca()));
    }

    /// <summary><c>scatterhist(x, y)</c>: a scatter with the two marginal histograms beside it.</summary>
    private static JgsValue[] ScatterWithHistograms(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("scatterhist", args, 2, 2, line, col);
        double[] x = Clean("scatterhist", args[0], line, col);
        double[] y = Clean("scatterhist", args[1], line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, "scatterhist: x and y must be the same length.");
        }

        int bins = Math.Max((int)Math.Sqrt(x.Length), 1);

        AxesModel main = JG.Subplot(2, 2, 3);
        JG.Plot(x, y, ".");
        JG.XLabel("x");
        JG.YLabel("y");

        AxesModel top = JG.Subplot(2, 2, 1);
        JG.Histogram(x, bins);

        AxesModel side = JG.Subplot(2, 2, 4);
        JG.Histogram(y, bins);

        JG.MakeCurrent(main);
        var handles = new double[3];
        handles[0] = JgsHandleRegistry.For(JgsHandleKind.Axes, main).AsNumber;
        handles[1] = JgsHandleRegistry.For(JgsHandleKind.Axes, top).AsNumber;
        handles[2] = JgsHandleRegistry.For(JgsHandleKind.Axes, side).AsNumber;
        return Outputs(wanted, JgsMatrix.FromColumnMajor(handles, 1, 3));
    }

    private static double[] Column(double[] flat, int rows, int column)
    {
        var values = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            values[r] = flat[r + (column * rows)];
        }

        return values;
    }

    // --- Trees ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[H, T, perm] = dendrogram(Z)</c>: the agglomerative tree drawn as the right-angled links that
    /// made it.
    /// </summary>
    /// <remarks>
    /// Asking for fewer nodes than there are observations does not hide links — it draws a different,
    /// smaller tree. The original is cut so that the wanted number of clusters remain, each of those
    /// becomes one leaf, and the merges above the cut are renumbered onto them. That is why the second
    /// output exists: it says which cluster each original observation ended up in, and without it the
    /// drawn leaves would name nothing.
    /// </remarks>
    private static JgsValue[] Dendrogram(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "dendrogram needs a linkage tree.");
        }

        ParsedArgs parsed = DendrogramOptions.Parse(args, 2, line, col);
        double[,] tree = AsRectangle("dendrogram", parsed.Positional[0], line, col);
        int merges = tree.GetLength(0);
        if (merges < 1 || tree.GetLength(1) < 3)
        {
            throw new JgsRuntimeException(line, col,
                "dendrogram: a linkage tree has three columns — the two things joined and how far apart they were.");
        }

        int leaves = merges + 1;
        int shown = parsed.Positional.Count > 1
            ? WholeOf("dendrogram", parsed.Positional[1], line, col)
            : 30;

        if (shown < 0)
        {
            throw new JgsRuntimeException(line, col, "dendrogram: the number of nodes cannot be negative.");
        }

        if (shown == 0 || shown >= leaves)
        {
            shown = leaves;
        }

        (double[,] drawnTree, int[] membership) = Collapse(tree, leaves, shown);
        int drawnLeaves = shown;
        int drawnMerges = drawnTree.GetLength(0);

        (double[] centre, int[] order) = LeafPositions(drawnTree, drawnLeaves);

        bool horizontal = parsed.Word("orientation", "top", "top", "bottom", "left", "right")
            is "left" or "right";
        double threshold = parsed.Named("colorthreshold") is { } given
            ? (given.Type == JgsType.String ? DefaultThreshold(tree) : NumOf("dendrogram", given, line, col))
            : double.PositiveInfinity;

        string[] labels = parsed.Named("labels") is { } named
            ? TextElements("dendrogram", named, line, col)
            : [];

        if (labels.Length != 0 && labels.Length != leaves)
        {
            throw new JgsRuntimeException(line, col, "dendrogram: 'Labels' needs one label per leaf.");
        }

        var drawn = new List<PlotObject>();
        bool wasHolding = JG.IsHolding;
        try
        {
            var height = new double[drawnMerges];
            for (int m = 0; m < drawnMerges; m++)
            {
                int leftNode = (int)drawnTree[m, 0] - 1;
                int rightNode = (int)drawnTree[m, 1] - 1;
                double distance = drawnTree[m, 2];

                double xLeft = centre[leftNode];
                double xRight = centre[rightNode];
                double yLeft = leftNode < drawnLeaves ? 0 : height[leftNode - drawnLeaves];
                double yRight = rightNode < drawnLeaves ? 0 : height[rightNode - drawnLeaves];

                LinePlot link = Segment(
                    horizontal,
                    [xLeft, xLeft, xRight, xRight],
                    [yLeft, distance, distance, yRight],
                    distance < threshold ? "b-" : "k-");
                link.DisplayName = "Link";
                drawn.Add(link);
                height[m] = distance;
                JG.Hold(true);
            }

            if (labels.Length == leaves && shown == leaves)
            {
                for (int i = 0; i < order.Length; i++)
                {
                    JG.Text(horizontal ? 0 : centre[order[i]], horizontal ? centre[order[i]] : 0, labels[order[i]]);
                }
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        var permutation = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            permutation[i] = order[i] + 1;
        }

        var assignment = new double[leaves];
        for (int i = 0; i < leaves; i++)
        {
            assignment[i] = membership[i] + 1;
        }

        return Outputs(
            wanted,
            HandleRow(drawn),
            JgsMatrix.FromColumnMajor(assignment, leaves, 1),
            JgsMatrix.FromColumnMajor(permutation, 1, permutation.Length));
    }

    /// <summary>
    /// The tree that is actually drawn, and which cluster of it each original leaf fell into. Asking
    /// for every leaf gives the tree back unchanged; asking for fewer keeps the merges above the cut and
    /// renumbers their children onto the clusters below it.
    /// </summary>
    private static (double[,] Tree, int[] Membership) Collapse(double[,] tree, int leaves, int shown)
    {
        var membership = new int[leaves];
        if (shown >= leaves)
        {
            for (int i = 0; i < leaves; i++)
            {
                membership[i] = i;
            }

            return (tree, membership);
        }

        int merges = tree.GetLength(0);
        int kept = shown - 1;
        int firstKept = merges - kept;

        // Which original leaves sit under each node, built once from the bottom up: the merges below the
        // cut are exactly the ones whose members become a single drawn leaf.
        var under = new List<int>[leaves + merges];
        for (int i = 0; i < leaves; i++)
        {
            under[i] = [i];
        }

        for (int m = 0; m < merges; m++)
        {
            under[leaves + m] = [.. under[(int)tree[m, 0] - 1], .. under[(int)tree[m, 1] - 1]];
        }

        // The collapsed clusters are the children of the kept merges that are not themselves kept.
        var roots = new List<int>();
        for (int m = firstKept; m < merges; m++)
        {
            foreach (int child in new[] { (int)tree[m, 0] - 1, (int)tree[m, 1] - 1 })
            {
                if (child < leaves + firstKept)
                {
                    roots.Add(child);
                }
            }
        }

        roots.Sort();
        for (int g = 0; g < roots.Count; g++)
        {
            foreach (int leaf in under[roots[g]])
            {
                membership[leaf] = g;
            }
        }

        var reduced = new double[kept, 3];
        for (int m = 0; m < kept; m++)
        {
            for (int side = 0; side < 2; side++)
            {
                int child = (int)tree[firstKept + m, side] - 1;
                reduced[m, side] = child >= leaves + firstKept
                    ? roots.Count + (child - leaves - firstKept) + 1
                    : roots.IndexOf(child) + 1;
            }

            reduced[m, 2] = tree[firstKept + m, 2];
        }

        return (reduced, membership);
    }

    /// <summary>Where each node sits along the leaf axis, and the order the leaves were laid out in.</summary>
    private static (double[] Centre, int[] Order) LeafPositions(double[,] tree, int leaves)
    {
        int merges = tree.GetLength(0);
        var centre = new double[leaves + merges];
        var order = new List<int>();

        void Walk(int node)
        {
            if (node < leaves)
            {
                order.Add(node);
                return;
            }

            Walk((int)tree[node - leaves, 0] - 1);
            Walk((int)tree[node - leaves, 1] - 1);
        }

        Walk(leaves + merges - 1);
        for (int i = 0; i < order.Count; i++)
        {
            centre[order[i]] = i + 1;
        }

        for (int m = 0; m < merges; m++)
        {
            centre[leaves + m] = (centre[(int)tree[m, 0] - 1] + centre[(int)tree[m, 1] - 1]) / 2;
        }

        return (centre, [.. order]);
    }

    /// <summary>MathWorks' default colour threshold: seven tenths of the way up the tree.</summary>
    private static double DefaultThreshold(double[,] tree)
    {
        double highest = 0;
        for (int m = 0; m < tree.GetLength(0); m++)
        {
            highest = Math.Max(highest, tree[m, 2]);
        }

        return 0.7 * highest;
    }

    /// <summary>
    /// <c>manovacluster(stats)</c>: the dendrogram of the group means, using the distances the
    /// multivariate analysis of variance already measured between them.
    /// </summary>
    private static JgsValue GroupMeanCluster(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("manovacluster", args, 1, 2, line, col);
        if (args[0].Type != JgsType.Struct
            || !args[0].AsStruct.TryGetValue("gmdist", out JgsValue? distances))
        {
            throw new JgsRuntimeException(line, col,
                "manovacluster takes the stats structure manova1 answers with, which carries the "
                + "distances between the group means.");
        }

        double[,] square = AsRectangle("manovacluster", distances, line, col);
        int groups = square.GetLength(0);
        if (groups < 2 || square.GetLength(1) != groups)
        {
            throw new JgsRuntimeException(line, col,
                "manovacluster: the group distances must be a square matrix of at least two groups.");
        }

        var condensed = new List<double>();
        for (int i = 0; i < groups; i++)
        {
            for (int j = i + 1; j < groups; j++)
            {
                condensed.Add(square[i, j]);
            }
        }

        string method = args.Count > 1 ? Str("manovacluster", args, 1, line, col) : "single";
        Hierarchical.Tree tree = Guarded(
            "manovacluster",
            () => Hierarchical.Link([.. condensed], LinkageMethodOf(method)),
            line,
            col);

        JgsValue[] answer = Dendrogram([TreeMatrix(tree)], 1, line, col);
        JG.Title("Group means");
        return answer[0];
    }

    /// <summary>The linkage method a word names, refusing anything the tree builder does not know.</summary>
    private static LinkageMethod LinkageMethodOf(string word) => word.ToLowerInvariant() switch
    {
        "complete" => LinkageMethod.Complete,
        "average" => LinkageMethod.Average,
        "centroid" => LinkageMethod.Centroid,
        "median" => LinkageMethod.Median,
        "ward" => LinkageMethod.Ward,
        "weighted" => LinkageMethod.Weighted,
        _ => LinkageMethod.Single,
    };

    // --- Curves through many variables at once ------------------------------------------------------------

    /// <summary>
    /// <c>andrewsplot(X)</c> and <c>parallelcoords(X)</c>: one curve per observation, either as a
    /// Fourier series in the variables or as a line across them.
    /// </summary>
    private static JgsValue CurvePlot(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} needs some observations.");
        }

        ParsedArgs parsed = CurveOptions.Parse(args, 1, line, col);
        (double[][] rows, int width) = Observations(verb, parsed.Positional[0], line, col);

        double[][] data = rows;
        string standardize = parsed.Text("standardize") ?? "off";
        if (standardize.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            data = Standardized(rows, width);
        }
        else if (!standardize.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: 'Standardize' is 'on' or 'off' here. The principal-component forms reduce the "
                + "data first — call pca and hand the scores over.");
        }

        int[] index = [];
        string[] names = [];
        if (parsed.Named("group") is { } grouping)
        {
            (index, names) = GroupIndex(verb, grouping, line, col);
            if (index.Length != rows.Length)
            {
                throw new JgsRuntimeException(line, col, $"{verb}: 'Group' needs one label per observation.");
            }
        }

        double[] quantiles = parsed.Named("quantile") is { } asked
            ? [Num(verb, [asked], 0, line, col)]
            : [];

        bool andrews = verb == "andrewsplot";
        const int Points = 101;
        var t = new double[Points];
        for (int i = 0; i < Points; i++)
        {
            t[i] = andrews ? i / (Points - 1.0) : 0;
        }

        var drawn = new List<PlotObject>();
        bool wasHolding = JG.IsHolding;
        try
        {
            var byGroup = new Dictionary<int, List<double[]>>();
            for (int r = 0; r < data.Length; r++)
            {
                int group = index.Length == data.Length ? index[r] : 0;
                double[] curve = andrews ? AndrewsCurve(data[r], t) : data[r];
                if (!byGroup.TryGetValue(group, out List<double[]>? held))
                {
                    held = [];
                    byGroup[group] = held;
                }

                held.Add(curve);
            }

            foreach ((int group, List<double[]> curves) in byGroup)
            {
                double[] x = andrews ? t : LevelPositions(width);
                IEnumerable<double[]> shown = quantiles.Length > 0
                    ? QuantileBand(curves, quantiles[0])
                    : curves;

                foreach (double[] curve in shown)
                {
                    LinePlot drawnCurve = JG.Plot(x, curve);
                    if (names.Length > group)
                    {
                        drawnCurve.DisplayName = names[group];
                    }

                    drawn.Add(drawnCurve);
                    JG.Hold(true);
                }
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        JG.XLabel(andrews ? "t" : "Coordinate");
        JG.YLabel(andrews ? "f(t)" : "Value");
        return HandleRow(drawn);
    }

    /// <summary>The Fourier series an observation becomes: the variables are its coefficients.</summary>
    private static double[] AndrewsCurve(double[] observation, double[] t)
    {
        var curve = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double angle = Math.PI * ((2 * t[i]) - 1);
            double total = observation.Length > 0 ? observation[0] / Math.Sqrt(2) : 0;
            for (int j = 1; j < observation.Length; j++)
            {
                int harmonic = (j + 1) / 2;
                total += observation[j] * (j % 2 == 1
                    ? Math.Sin(harmonic * angle)
                    : Math.Cos(harmonic * angle));
            }

            curve[i] = total;
        }

        return curve;
    }

    /// <summary>The three curves that bound a quantile band: the lower, the median, and the upper.</summary>
    private static IEnumerable<double[]> QuantileBand(List<double[]> curves, double quantile)
    {
        if (curves.Count == 0)
        {
            yield break;
        }

        int width = curves[0].Length;
        double[] percentages = [100 * quantile, 50, 100 * (1 - quantile)];
        var bands = new double[3][];
        for (int b = 0; b < 3; b++)
        {
            bands[b] = new double[width];
        }

        for (int i = 0; i < width; i++)
        {
            var column = new double[curves.Count];
            for (int c = 0; c < curves.Count; c++)
            {
                column[c] = curves[c][i];
            }

            double[] at = DescriptiveStatistics.Percentiles(column, percentages);
            for (int b = 0; b < 3; b++)
            {
                bands[b][i] = at[b];
            }
        }

        foreach (double[] band in bands)
        {
            yield return band;
        }
    }

    /// <summary><c>glyphplot(X)</c>: one star per observation, its rays as long as its variables.</summary>
    private static JgsValue GlyphPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "glyphplot needs some observations.");
        }

        ParsedArgs parsed = GlyphOptions.Parse(args, 1, line, col);
        (double[][] rows, int width) = Observations("glyphplot", parsed.Positional[0], line, col);

        if (parsed.Word("glyph", "star", "star", "face") == "face")
        {
            throw new JgsRuntimeException(line, col,
                "glyphplot: the face glyph maps variables onto features of a drawn face, which needs a "
                + "drawing primitive this build does not have. The star glyph says the same thing.");
        }

        // Every variable is scaled onto the same ray length, because a star is read by comparing its
        // own rays and one variable measured in thousands would otherwise be the whole picture.
        double[][] scaled = ScaledToUnit(rows, width);

        int columns = (int)Math.Ceiling(Math.Sqrt(rows.Length));
        int gridRows = (int)Math.Ceiling((double)rows.Length / columns);

        string[] labels = parsed.Named("obslabels") is { } given
            ? TextElements("glyphplot", given, line, col)
            : [];

        var drawn = new List<PlotObject>();
        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        try
        {
            for (int r = 0; r < rows.Length; r++)
            {
                double centreX = (r % columns) + 1.0;
                double centreY = gridRows - (r / columns);

                var x = new double[width + 1];
                var y = new double[width + 1];
                for (int v = 0; v < width; v++)
                {
                    double angle = 2 * Math.PI * v / width;
                    double radius = 0.4 * scaled[r][v];
                    x[v] = centreX + (radius * Math.Cos(angle));
                    y[v] = centreY + (radius * Math.Sin(angle));
                }

                x[width] = x[0];
                y[width] = y[0];
                PatchPlot star = JG.Fill(x, y, Colors.LightGray);
                star.EdgeColor = Colors.Black;
                star.DisplayName = labels.Length > r ? labels[r] : $"Observation {r + 1}";
                drawn.Add(star);

                if (labels.Length > r)
                {
                    JG.Text(centreX, centreY - 0.45, labels[r]);
                }
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        JG.Gca().EqualAspect = true;
        return HandleRow(drawn);
    }

    /// <summary>Each column mapped onto zero to one, which is what makes glyph rays comparable.</summary>
    private static double[][] ScaledToUnit(double[][] rows, int width)
    {
        var scaled = new double[rows.Length][];
        for (int r = 0; r < rows.Length; r++)
        {
            scaled[r] = new double[width];
        }

        for (int c = 0; c < width; c++)
        {
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (double[] row in rows)
            {
                low = Math.Min(low, row[c]);
                high = Math.Max(high, row[c]);
            }

            for (int r = 0; r < rows.Length; r++)
            {
                scaled[r][c] = high > low ? (rows[r][c] - low) / (high - low) : 0.5;
            }
        }

        return scaled;
    }

    /// <summary>
    /// <c>biplot(coefs, 'Scores', score)</c>: the variables as arrows from the origin and, when they
    /// were given, the observations as points in the same picture.
    /// </summary>
    private static JgsValue[] Biplot(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "biplot needs the coefficients to draw.");
        }

        ParsedArgs parsed = BiplotOptions.Parse(args, 1, line, col);
        (double[][] coefficients, int width) = Observations("biplot", parsed.Positional[0], line, col);
        if (width is not (2 or 3))
        {
            throw new JgsRuntimeException(line, col,
                "biplot draws two or three components, so the coefficients need two or three columns.");
        }

        if (width == 3)
        {
            throw new JgsRuntimeException(line, col,
                "biplot: the three-component form draws in three dimensions, which this build does not "
                + "do for this verb. Pass the first two columns.");
        }

        string[] labels = parsed.Named("varlabels") is { } given
            ? TextElements("biplot", given, line, col)
            : [];

        var drawn = new List<PlotObject>();
        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        try
        {
            for (int v = 0; v < coefficients.Length; v++)
            {
                LinePlot arrow = JG.Plot(
                    [0, coefficients[v][0]], [0, coefficients[v][1]], "b-");
                arrow.DisplayName = labels.Length > v ? labels[v] : $"Variable {v + 1}";
                drawn.Add(arrow);
                if (labels.Length > v)
                {
                    JG.Text(coefficients[v][0], coefficients[v][1], labels[v]);
                }
            }

            if (parsed.Named("scores") is { } scores)
            {
                (double[][] points, int scoreWidth) = Observations("biplot", scores, line, col);
                if (scoreWidth < 2)
                {
                    throw new JgsRuntimeException(line, col, "biplot: 'Scores' needs at least two columns.");
                }

                // The scores are scaled onto the coefficients' own range so that both fit in one
                // picture, which is the whole point of drawing them together.
                double reach = 0;
                foreach (double[] point in points)
                {
                    reach = Math.Max(reach, Math.Max(Math.Abs(point[0]), Math.Abs(point[1])));
                }

                double scale = reach > 0 ? 0.8 / reach : 1;
                var x = new double[points.Length];
                var y = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    x[i] = points[i][0] * scale;
                    y[i] = points[i][1] * scale;
                }

                LinePlot marks = JG.Plot(x, y, "r.");
                marks.DisplayName = "Scores";
                drawn.Add(marks);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        JG.XLabel("Component 1");
        JG.YLabel("Component 2");
        return Outputs(wanted, HandleRow(drawn));
    }

    // --- Two-dimensional histogram ------------------------------------------------------------------------

    /// <summary><c>[N, C] = hist3(X)</c>: how many observations fall in each cell of a grid.</summary>
    private static JgsValue[] BivariateHistogram(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "hist3 needs a two-column matrix of observations.");
        }

        ParsedArgs parsed = Hist3Options.Parse(args, 2, line, col);
        (double[][] rows, int width) = Observations("hist3", parsed.Positional[0], line, col);
        if (width != 2)
        {
            throw new JgsRuntimeException(line, col, "hist3 takes exactly two variables.");
        }

        int[] bins = [10, 10];
        if (parsed.Positional.Count > 1)
        {
            double[] asked = NumericVector("hist3", parsed.Positional[1], line, col);
            bins = [(int)asked[0], (int)asked[^1]];
        }
        else if (parsed.Named("nbins") is { } named)
        {
            double[] asked = NumericVector("hist3", named, line, col);
            bins = [(int)asked[0], (int)asked[^1]];
        }

        if (parsed.Named("ctrs") is not null || parsed.Named("edges") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "hist3: 'Ctrs' and 'Edges' place the bins by hand, which is not read here — 'Nbins' says "
                + "how many to spread evenly over the data.");
        }

        if (bins[0] < 1 || bins[1] < 1)
        {
            throw new JgsRuntimeException(line, col, "hist3: the number of bins must be positive.");
        }

        (double lowX, double highX) = Span(rows, 0);
        (double lowY, double highY) = Span(rows, 1);
        double stepX = (highX - lowX) / bins[0];
        double stepY = (highY - lowY) / bins[1];
        if (!(stepX > 0))
        {
            stepX = 1;
        }

        if (!(stepY > 0))
        {
            stepY = 1;
        }

        var counts = new double[bins[0] * bins[1]];
        foreach (double[] row in rows)
        {
            int i = Math.Clamp((int)((row[0] - lowX) / stepX), 0, bins[0] - 1);
            int j = Math.Clamp((int)((row[1] - lowY) / stepY), 0, bins[1] - 1);
            counts[i + (j * bins[0])]++;
        }

        var centresX = new double[bins[0]];
        for (int i = 0; i < bins[0]; i++)
        {
            centresX[i] = lowX + (stepX * (i + 0.5));
        }

        var centresY = new double[bins[1]];
        for (int j = 0; j < bins[1]; j++)
        {
            centresY[j] = lowY + (stepY * (j + 0.5));
        }

        if (wanted == 0)
        {
            // MATLAB draws the counts as three-dimensional bars; a surface over the same grid is the
            // same numbers said with the primitive this build has, which is a recorded divergence.
            var grid = new double[bins[1], bins[0]];
            for (int i = 0; i < bins[0]; i++)
            {
                for (int j = 0; j < bins[1]; j++)
                {
                    grid[j, i] = counts[i + (j * bins[0])];
                }
            }

            JG.Surf(centresX, centresY, grid);
            JG.XLabel("x");
            JG.YLabel("y");
            JG.ZLabel("Count");
            return [];
        }

        JgsValue centres = JgsValue.Cell(
        [
            JgsMatrix.FromColumnMajor(centresX, 1, bins[0]),
            JgsMatrix.FromColumnMajor(centresY, 1, bins[1]),
        ]);

        return Outputs(wanted, JgsMatrix.FromColumnMajor(counts, bins[0], bins[1]), centres);
    }

    private static (double Low, double High) Span(double[][] rows, int column)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double[] row in rows)
        {
            low = Math.Min(low, row[column]);
            high = Math.Max(high, row[column]);
        }

        return double.IsInfinity(low) ? (0, 1) : (low, high);
    }

    // --- Regression diagnostics ---------------------------------------------------------------------------

    /// <summary>
    /// <c>addedvarplot(X, y, num, inmodel)</c>: what one predictor adds to a model that already holds
    /// the others, drawn as the two sets of residuals against each other.
    /// </summary>
    private static JgsValue AddedVariablePlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("addedvarplot", args, 3, 5, line, col);
        double[,] predictors = AsRectangle("addedvarplot", args[0], line, col);
        double[] response = FlattenColumnMajor("addedvarplot", args[1], line, col);
        int which = Count("addedvarplot", args, 2, line, col) - 1;

        int n = predictors.GetLength(0);
        int p = predictors.GetLength(1);
        if (response.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                "addedvarplot: one response per row of the predictors.");
        }

        if (which < 0 || which >= p)
        {
            throw new JgsRuntimeException(line, col,
                $"addedvarplot: there is no predictor {which + 1}; there are {p}.");
        }

        bool[] inModel = new bool[p];
        if (args.Count > 3 && !IsPlaceholderValue(args[3]))
        {
            double[] flags = FlattenColumnMajor("addedvarplot", args[3], line, col);
            if (flags.Length != p)
            {
                throw new JgsRuntimeException(line, col,
                    "addedvarplot: the model flags need one entry per predictor.");
            }

            for (int i = 0; i < p; i++)
            {
                inModel[i] = flags[i] != 0;
            }
        }

        inModel[which] = false;

        // Both the response and the predictor of interest are stripped of what the model already
        // explains; what is left of each is what the plot is about.
        double[] responseResidual = ResidualsAfter(predictors, response, inModel, n);
        double[] column = new double[n];
        for (int i = 0; i < n; i++)
        {
            column[i] = predictors[i, which];
        }

        double[] predictorResidual = ResidualsAfter(predictors, column, inModel, n);

        LinePlot points = JG.Plot(predictorResidual, responseResidual, "+");
        points.DisplayName = $"Predictor {which + 1}";
        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        LinePlot fit = QuartileLine(predictorResidual, responseResidual);
        JG.Hold(wasHolding);

        JG.XLabel($"Predictor {which + 1} residuals");
        JG.YLabel("Response residuals");
        return HandleRow([points, fit]);
    }

    /// <summary>What is left of a response after the named predictors, and a constant, have had their say.</summary>
    /// <summary>The slope and intercept of the least-squares line, highest power first.</summary>
    private static double[] StraightFit(double[] x, double[] y)
    {
        var design = new double[x.Length, 2];
        for (int i = 0; i < x.Length; i++)
        {
            design[i, 0] = 1;
            design[i, 1] = x[i];
        }

        double[] coefficients = LeastSquares.Solve(design, y).Coefficients;
        return [coefficients[1], coefficients[0]];
    }

    private static double[] ResidualsAfter(double[,] predictors, double[] y, bool[] inModel, int n)
    {
        var columns = new List<double[]> { Ones(n) };
        for (int i = 0; i < inModel.Length; i++)
        {
            if (!inModel[i])
            {
                continue;
            }

            var column = new double[n];
            for (int r = 0; r < n; r++)
            {
                column[r] = predictors[r, i];
            }

            columns.Add(column);
        }

        var design = new double[n, columns.Count];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                design[r, c] = columns[c][r];
            }
        }

        double[] coefficients = LeastSquares.Solve(design, y).Coefficients;
        var residuals = new double[n];
        for (int r = 0; r < n; r++)
        {
            double fitted = 0;
            for (int c = 0; c < columns.Count; c++)
            {
                fitted += design[r, c] * coefficients[c];
            }

            residuals[r] = y[r] - fitted;
        }

        return residuals;
    }

    /// <summary>
    /// <c>rcoplot(r, rint)</c>: the residuals in the order the cases were measured, each with the
    /// interval the fit gives it, so that an interval clear of zero stands out as an outlier.
    /// </summary>
    private static JgsValue ResidualCaseOrderPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("rcoplot", args, 2, 2, line, col);
        double[] residuals = FlattenColumnMajor("rcoplot", args[0], line, col);
        double[,] intervals = AsRectangle("rcoplot", args[1], line, col);

        if (intervals.GetLength(0) != residuals.Length || intervals.GetLength(1) != 2)
        {
            throw new JgsRuntimeException(line, col,
                "rcoplot: the intervals are one [low high] pair per residual.");
        }

        var cases = new double[residuals.Length];
        var error = new double[residuals.Length];
        for (int i = 0; i < residuals.Length; i++)
        {
            cases[i] = i + 1;
            error[i] = (intervals[i, 1] - intervals[i, 0]) / 2;
        }

        ErrorBarPlot bars = JG.ErrorBar(cases, residuals, error);
        bars.DisplayName = "Residuals";

        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        LinePlot zero = JG.Plot([0.5, residuals.Length + 0.5], [0, 0], "r--");
        zero.DisplayName = "Zero";
        JG.Hold(wasHolding);

        JG.XLabel("Case number");
        JG.YLabel("Residual");
        return HandleRow([bars, zero]);
    }

    // --- Capability pictures --------------------------------------------------------------------------------

    /// <summary><c>[p, h] = capaplot(data, specs)</c>: the fitted normal, with the specification on it.</summary>
    private static JgsValue[] CapabilityPlot(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("capaplot", args, 2, 2, line, col);
        double[] values = Clean("capaplot", args[0], line, col);
        (double lower, double upper) = SpecificationLimits("capaplot", args, 1, line, col);
        if (values.Length < 2)
        {
            throw new JgsRuntimeException(line, col, "capaplot needs at least two observations.");
        }

        double mean = DescriptiveStatistics.Mean(values);
        double deviation = DescriptiveStatistics.StandardDeviation(values, population: false);
        return NormalWithSpecification("capaplot", mean, deviation, lower, upper, wanted);
    }

    /// <summary><c>[p, h] = normspec(specs, mu, sigma)</c>: the same picture from named parameters.</summary>
    private static JgsValue[] SpecificationPlot(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("normspec", args, 1, 4, line, col);
        (double lower, double upper) = SpecificationLimits("normspec", args, 0, line, col);
        double mean = args.Count > 1 && !IsPlaceholderValue(args[1]) ? Num("normspec", args, 1, line, col) : 0;
        double deviation = args.Count > 2 && !IsPlaceholderValue(args[2]) ? Num("normspec", args, 2, line, col) : 1;
        if (!(deviation > 0))
        {
            throw new JgsRuntimeException(line, col, "normspec: the standard deviation must be above zero.");
        }

        return NormalWithSpecification("normspec", mean, deviation, lower, upper, wanted);
    }

    private static JgsValue[] NormalWithSpecification(
        string verb, double mean, double deviation, double lower, double upper, int wanted)
    {
        const int Points = 200;
        double from = Math.Max(mean - (4 * deviation), double.IsNegativeInfinity(lower) ? mean - (4 * deviation) : Math.Min(lower - deviation, mean - (4 * deviation)));
        double to = Math.Min(mean + (4 * deviation), double.IsPositiveInfinity(upper) ? mean + (4 * deviation) : Math.Max(upper + deviation, mean + (4 * deviation)));
        if (!(to > from))
        {
            (from, to) = (mean - (4 * deviation), mean + (4 * deviation));
        }

        var x = new double[Points];
        var y = new double[Points];
        for (int i = 0; i < Points; i++)
        {
            x[i] = from + ((to - from) * i / (Points - 1.0));
            y[i] = ContinuousDistributions.NormalPdf(x[i], mean, deviation);
        }

        LinePlot curve = JG.Plot(x, y);
        curve.DisplayName = "Fitted normal";

        bool wasHolding = JG.IsHolding;
        JG.Hold(true);
        var drawn = new List<PlotObject> { curve };
        double top = 0;
        foreach (double one in y)
        {
            top = Math.Max(top, one);
        }

        if (!double.IsNegativeInfinity(lower))
        {
            drawn.Add(JG.Plot([lower, lower], [0, top], "r--"));
        }

        if (!double.IsPositiveInfinity(upper))
        {
            drawn.Add(JG.Plot([upper, upper], [0, top], "r--"));
        }

        JG.Hold(wasHolding);

        double inside =
            (double.IsPositiveInfinity(upper) ? 1 : ContinuousDistributions.NormalCdf(upper, mean, deviation))
            - (double.IsNegativeInfinity(lower) ? 0 : ContinuousDistributions.NormalCdf(lower, mean, deviation));

        JG.XLabel("x");
        JG.YLabel("Density");
        JG.Title($"{verb}: {inside * 100:0.##}% within the specification");
        return Outputs(wanted, JgsValue.Number(inside), HandleRow(drawn));
    }

    // --- Effects plots ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>maineffectsplot(Y, GROUP)</c> and <c>multivarichart(y, group)</c>: the mean of the response at
    /// each level of each factor.
    /// </summary>
    private static JgsValue EffectsPlot(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{verb} needs a response and the factors to split it by.");
        }

        ParsedArgs parsed = EffectOptions.Parse(args, 2, line, col);
        double[] response = FlattenColumnMajor(verb, parsed.Positional[0], line, col);
        List<(int[] Index, string[] Names)> factors =
            GroupingFactors(verb, parsed.Positional[1], response.Length, line, col);

        string[] labels = parsed.Named("varnames") is { } given
            ? TextElements(verb, given, line, col)
            : [];

        var drawn = new List<PlotObject>();
        for (int f = 0; f < factors.Count; f++)
        {
            JG.Subplot(1, factors.Count, f + 1);
            (int[] index, string[] names) = factors[f];

            var means = new double[names.Length];
            var at = new double[names.Length];
            for (int level = 0; level < names.Length; level++)
            {
                var held = new List<double>();
                for (int i = 0; i < index.Length; i++)
                {
                    if (index[i] == level)
                    {
                        held.Add(response[i]);
                    }
                }

                means[level] = held.Count > 0 ? DescriptiveStatistics.Mean(held) : double.NaN;
                at[level] = level + 1;
            }

            LinePlot curve = JG.Plot(at, means, "o-");
            curve.DisplayName = labels.Length > f ? labels[f] : $"Factor {f + 1}";
            drawn.Add(curve);
            JG.XLabel(string.Join(", ", names));
            if (f == 0)
            {
                JG.YLabel("Mean of the response");
            }
        }

        return HandleRow(drawn);
    }

    /// <summary>
    /// <c>interactionplot(Y, GROUP)</c>: for every pair of factors, the response's mean at each level of
    /// one, drawn as a separate line for each level of the other.
    /// </summary>
    private static JgsValue InteractionPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "interactionplot needs a response and at least two factors to cross.");
        }

        ParsedArgs parsed = EffectOptions.Parse(args, 2, line, col);
        double[] response = FlattenColumnMajor("interactionplot", parsed.Positional[0], line, col);
        List<(int[] Index, string[] Names)> factors =
            GroupingFactors("interactionplot", parsed.Positional[1], response.Length, line, col);

        if (factors.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "interactionplot needs at least two factors — an interaction is between two of them.");
        }

        var drawn = new List<PlotObject>();
        int side = factors.Count;
        for (int a = 0; a < side; a++)
        {
            for (int b = 0; b < side; b++)
            {
                if (a == b)
                {
                    continue;
                }

                JG.Subplot(side, side, (a * side) + b + 1);
                bool wasHolding = JG.IsHolding;
                try
                {
                    for (int levelB = 0; levelB < factors[b].Names.Length; levelB++)
                    {
                        var at = new List<double>();
                        var means = new List<double>();
                        for (int levelA = 0; levelA < factors[a].Names.Length; levelA++)
                        {
                            var held = new List<double>();
                            for (int i = 0; i < response.Length; i++)
                            {
                                if (factors[a].Index[i] == levelA && factors[b].Index[i] == levelB)
                                {
                                    held.Add(response[i]);
                                }
                            }

                            if (held.Count > 0)
                            {
                                at.Add(levelA + 1);
                                means.Add(DescriptiveStatistics.Mean(held));
                            }
                        }

                        if (at.Count > 0)
                        {
                            LinePlot curve = JG.Plot([.. at], [.. means], "o-");
                            curve.DisplayName = factors[b].Names[levelB];
                            drawn.Add(curve);
                            JG.Hold(true);
                        }
                    }
                }
                finally
                {
                    JG.Hold(wasHolding);
                }
            }
        }

        return HandleRow(drawn);
    }

    // --- The lasso trace ---------------------------------------------------------------------------------------

    /// <summary><c>lassoPlot(B, fitinfo)</c>: how the coefficients shrink as the penalty grows.</summary>
    private static JgsValue LassoTracePlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "lassoPlot needs the coefficients lasso answered with.");
        }

        ParsedArgs parsed = LassoPlotOptions.Parse(args, 2, line, col);
        double[,] coefficients = AsRectangle("lassoPlot", parsed.Positional[0], line, col);
        int predictors = coefficients.GetLength(0);
        int steps = coefficients.GetLength(1);

        string kind = parsed.Word("plottype", "l1", "l1", "lambda", "cv");
        if (kind == "cv")
        {
            throw new JgsRuntimeException(line, col,
                "lassoPlot: 'PlotType','CV' draws the cross-validated error, which lasso here does not "
                + "compute. The 'L1' and 'Lambda' traces are drawn from the path itself.");
        }

        double[] axis;
        if (kind == "lambda")
        {
            if (parsed.Positional.Count < 2 || parsed.Positional[1].Type != JgsType.Struct
                || !parsed.Positional[1].AsStruct.TryGetValue("Lambda", out JgsValue? lambda))
            {
                throw new JgsRuntimeException(line, col,
                    "lassoPlot: the lambda trace needs the fit information lasso answered with beside the coefficients.");
            }

            axis = Flatten(lambda);
            if (axis.Length != steps)
            {
                throw new JgsRuntimeException(line, col,
                    "lassoPlot: there must be one penalty per column of coefficients.");
            }
        }
        else
        {
            // The L1 norm of each fit, which is the axis the trace is traditionally read against
            // because it grows as the penalty falls.
            axis = new double[steps];
            for (int s = 0; s < steps; s++)
            {
                double total = 0;
                for (int p = 0; p < predictors; p++)
                {
                    total += Math.Abs(coefficients[p, s]);
                }

                axis[s] = total;
            }
        }

        var drawn = new List<PlotObject>();
        bool wasHolding = JG.IsHolding;
        try
        {
            for (int p = 0; p < predictors; p++)
            {
                var trace = new double[steps];
                for (int s = 0; s < steps; s++)
                {
                    trace[s] = coefficients[p, s];
                }

                LinePlot curve = JG.Plot(axis, trace);
                curve.DisplayName = $"Predictor {p + 1}";
                drawn.Add(curve);
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        JG.XLabel(kind == "lambda" ? "Lambda" : "L1 norm");
        JG.YLabel("Coefficient");
        return HandleRow(drawn);
    }

    // --- The performance curve --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[X, Y, T, AUC] = perfcurve(labels, scores, positive)</c>: how a classifier's two error rates
    /// trade off as its threshold moves.
    /// </summary>
    private static JgsValue[] PerformanceCurve(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                "perfcurve needs the labels, the scores, and which label counts as positive.");
        }

        ParsedArgs parsed = PerformanceOptions.Parse(args, 3, line, col);
        if (parsed.Named("nboot") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "perfcurve: 'NBoot' puts confidence bands on the curve by resampling, which is not "
                + "computed here. The curve itself is.");
        }

        double[] scores = FlattenColumnMajor("perfcurve", parsed.Positional[1], line, col);
        bool[] positive = PositiveLabels(parsed.Positional[0], parsed.Positional[2], scores.Length, line, col);

        string xCriterion = parsed.Word("xcrit", "fpr", "fpr", "tpr", "fnr", "tnr", "ppv", "accu");
        string yCriterion = parsed.Word("ycrit", "tpr", "fpr", "tpr", "fnr", "tnr", "ppv", "accu");

        // Sorting by score turns the sweep over every threshold into one pass: each step moves one
        // observation from the predicted-negative side to the predicted-positive side.
        int[] order = new int[scores.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => scores[b].CompareTo(scores[a]));

        int positives = 0;
        foreach (bool one in positive)
        {
            if (one)
            {
                positives++;
            }
        }

        int negatives = scores.Length - positives;
        if (positives == 0 || negatives == 0)
        {
            throw new JgsRuntimeException(line, col,
                "perfcurve: the labels must hold both the positive class and something else.");
        }

        var x = new List<double>();
        var y = new List<double>();
        var thresholds = new List<double>();
        int truePositives = 0;
        int falsePositives = 0;
        double area = 0;
        double lastFalseRate = 0;
        double lastTrueRate = 0;

        void Record(double threshold)
        {
            double tpr = (double)truePositives / positives;
            double fpr = (double)falsePositives / negatives;
            x.Add(Criterion(xCriterion, truePositives, falsePositives, positives, negatives));
            y.Add(Criterion(yCriterion, truePositives, falsePositives, positives, negatives));
            thresholds.Add(threshold);
            area += (fpr - lastFalseRate) * (tpr + lastTrueRate) / 2;
            lastFalseRate = fpr;
            lastTrueRate = tpr;
        }

        Record(double.PositiveInfinity);
        for (int i = 0; i < order.Length; i++)
        {
            if (positive[order[i]])
            {
                truePositives++;
            }
            else
            {
                falsePositives++;
            }

            if (i + 1 == order.Length || scores[order[i + 1]] != scores[order[i]])
            {
                Record(scores[order[i]]);
            }
        }

        if (wanted == 0)
        {
            LinePlot curve = JG.Plot([.. x], [.. y]);
            curve.DisplayName = "Performance";
            JG.XLabel(xCriterion.ToUpperInvariant());
            JG.YLabel(yCriterion.ToUpperInvariant());
            JG.Title($"Area under the curve: {area:0.####}");
            return [];
        }

        // The best operating point is the corner nearest the top left, which is the threshold that
        // trades the two error rates most evenly.
        double best = double.PositiveInfinity;
        double bestX = 0;
        double bestY = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double gap = ((1 - y[i]) * (1 - y[i])) + (x[i] * x[i]);
            if (gap < best)
            {
                best = gap;
                bestX = x[i];
                bestY = y[i];
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor([.. x], x.Count, 1),
            JgsMatrix.FromColumnMajor([.. y], y.Count, 1),
            JgsMatrix.FromColumnMajor([.. thresholds], thresholds.Count, 1),
            JgsValue.Number(area),
            RowVector([bestX, bestY]));
    }

    private static double Criterion(
        string which, int truePositives, int falsePositives, int positives, int negatives)
    {
        double trueNegatives = negatives - falsePositives;
        double falseNegatives = positives - truePositives;
        return which switch
        {
            "tpr" => (double)truePositives / positives,
            "fnr" => falseNegatives / positives,
            "tnr" => trueNegatives / negatives,
            "ppv" => truePositives + falsePositives > 0
                ? (double)truePositives / (truePositives + falsePositives)
                : 1,
            "accu" => (truePositives + trueNegatives) / (double)(positives + negatives),
            _ => (double)falsePositives / negatives,
        };
    }

    /// <summary>Which observations carry the positive label, whatever shape the labels arrived in.</summary>
    private static bool[] PositiveLabels(
        JgsValue labels, JgsValue positive, int count, int line, int col)
    {
        var flags = new bool[count];
        if (labels.Type is JgsType.Cell || (labels.Type == JgsType.String && positive.Type == JgsType.String))
        {
            string[] written = TextElements("perfcurve", labels, line, col);
            if (written.Length != count)
            {
                throw new JgsRuntimeException(line, col, "perfcurve: one label per score.");
            }

            string wanted = StrOf("perfcurve", positive, line, col);
            for (int i = 0; i < count; i++)
            {
                flags[i] = string.Equals(written[i], wanted, StringComparison.Ordinal);
            }

            return flags;
        }

        double[] numeric = FlattenColumnMajor("perfcurve", labels, line, col);
        if (numeric.Length != count)
        {
            throw new JgsRuntimeException(line, col, "perfcurve: one label per score.");
        }

        double target = NumOf("perfcurve", positive, line, col);
        for (int i = 0; i < count; i++)
        {
            flags[i] = numeric[i] == target;
        }

        return flags;
    }

    // --- Shared small pieces -------------------------------------------------------------------------------------

    /// <summary>
    /// The factors an effects plot splits a response by, as an index and a set of level names each. The
    /// analysis of variance reads the same shapes, so the reading is shared with it and only the shape
    /// of the answer differs.
    /// </summary>
    private static List<(int[] Index, string[] Names)> GroupingFactors(
        string verb, JgsValue value, int count, int line, int col)
    {
        (List<int[]> index, _, List<string[]> names) = Factors(verb, value, count, line, col);
        var factors = new List<(int[] Index, string[] Names)>(index.Count);
        for (int i = 0; i < index.Count; i++)
        {
            factors.Add((index[i], names[i]));
        }

        return factors;
    }

    /// <summary>The data of a plot verb, with the missing readings dropped.</summary>
    private static double[] Clean(string verb, JgsValue value, int line, int col) =>
        DescriptiveStatistics.WithoutNaN(FlattenColumnMajor(verb, value, line, col));

    /// <summary>A handle to one line.</summary>
    private static JgsValue LineHandle(LinePlot plot) =>
        JgsHandleRegistry.For(JgsHandleKind.Line, plot);

    /// <summary>
    /// The handles a plot verb answers with: a column, the shape MATLAB gives back. A line gets a line
    /// handle and everything else a plain plot handle, which is what decides the properties it answers.
    /// </summary>
    private static JgsValue HandleRow(IReadOnlyList<PlotObject> plots)
    {
        if (plots.Count == 0)
        {
            return JgsValue.Array([]);
        }

        if (plots.Count == 1)
        {
            return HandleFor(plots[0]);
        }

        var handles = new double[plots.Count];
        for (int i = 0; i < plots.Count; i++)
        {
            handles[i] = HandleFor(plots[i]).AsNumber;
        }

        return JgsMatrix.FromColumnMajor(handles, plots.Count, 1);
    }

    private static JgsValue HandleFor(PlotObject plot) =>
        plot is LinePlot series
            ? JgsHandleRegistry.For(JgsHandleKind.Line, series)
            : JgsHandleRegistry.For(JgsHandleKind.Plot, plot);
}
