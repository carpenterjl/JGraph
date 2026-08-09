using JGraph.Statistics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave B, the empirical half: the distribution a sample is (<c>ecdf</c>), the histogram that
/// follows from one (<c>ecdfhist</c>), the kernel-smoothed curve that turns it back into a density
/// (<c>ksdensity</c>), and the eight legacy <c>nan*</c> names, which are the base reductions with the
/// missing values dropped.
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec EcdfOptions = new(
        "ecdf",
        Flags: [],
        Names: ["censoring", "frequency", "alpha", "function", "bounds"]);

    private static readonly OptionSpec KsdensityOptions = new(
        "ksdensity",
        Flags: [],
        Names: ["Bandwidth", "Kernel", "NumPoints", "Support", "Weights", "Function", "BoundaryCorrection"]);

    private static void RegisterEmpiricalBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name, (args, line, col) => both(args, 1, line, col)[0])
            { MultiOutput = both }));

        // ecdf and ksdensity draw the curve when nobody asked for the numbers, which is what makes
        // ecdf(x) on its own a plot; wanted is zero exactly then (M53 wave J).
        void DefineDrawing(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(
                name, (args, line, col) => both(args, 1, line, col)[0])
            {
                MultiOutput = both,
                KnowsWhenDiscarded = true,
            }));

        DefineDrawing("ecdf", Empirical);
        DefineBoth("ecdfhist", EmpiricalHistogram);
        DefineDrawing("ksdensity", SmoothedDensity);
    }

    /// <summary>
    /// <c>[F, X, FLO, FUP] = ecdf(y)</c>: the proportion of the sample at or below each observation,
    /// or — when some observations are censored — the Kaplan-Meier estimate, which spreads the
    /// information a censored observation withheld over the ones that outlived it.
    /// </summary>
    private static JgsValue[] Empirical(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "ecdf needs some data.");
        }

        ParsedArgs parsed = EcdfOptions.Parse(args, 1, line, col);
        double[] values = FlattenColumnMajor("ecdf", parsed.Positional[0], line, col);

        bool[]? censored = null;
        if (parsed.Named("censoring") is { } censoring)
        {
            double[] flags = NumericVector("ecdf", censoring, line, col);
            if (flags.Length != values.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "ecdf: 'censoring' needs one flag per observation.");
            }

            censored = Array.ConvertAll(flags, static flag => flag != 0);
        }

        double[]? frequency = null;
        if (parsed.Named("frequency") is { } counts)
        {
            frequency = NumericVector("ecdf", counts, line, col);
            if (frequency.Length != values.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "ecdf: 'frequency' needs one count per observation.");
            }
        }

        double alpha = parsed.Scalar("alpha", 0.05);
        if (!(alpha > 0 && alpha < 1))
        {
            throw new JgsRuntimeException(line, col, "ecdf: 'alpha' sits strictly between 0 and 1.");
        }

        EmpiricalDistribution.CurveKind kind =
            parsed.Word("function", "cdf", "cdf", "survivor", "cumulative hazard") switch
            {
                "survivor" => EmpiricalDistribution.CurveKind.Survivor,
                "cumulative hazard" => EmpiricalDistribution.CurveKind.CumulativeHazard,
                _ => EmpiricalDistribution.CurveKind.Cdf,
            };

        // 'bounds' merely decides whether MATLAB draws them; the numbers are computed either way and
        // the extra outputs are how a script asks for them.
        parsed.Word("bounds", "off", "on", "off");

        EmpiricalDistribution.EmpiricalCurve curve =
            EmpiricalDistribution.Empirical(values, censored, frequency, kind, alpha);

        if (wanted == 0)
        {
            JGraph.Api.JG.Plot(curve.Points, curve.Values).DisplayName = "Empirical CDF";
            JGraph.Api.JG.XLabel("x");
            JGraph.Api.JG.YLabel("F(x)");
            return [];
        }

        return Outputs(
            wanted,
            Column(curve.Values),
            Column(curve.Points),
            Column(curve.Lower),
            Column(curve.Upper));
    }

    /// <summary>
    /// <c>[N, C] = ecdfhist(F, X)</c>: the histogram heights that agree with an empirical distribution
    /// function, and the bin centres they belong to. The heights are densities, so the bars integrate
    /// to one however wide the bins are.
    /// </summary>
    private static JgsValue[] EmpiricalHistogram(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ecdfhist", args, 2, 3, line, col);
        double[] curve = FlattenColumnMajor("ecdfhist", args[0], line, col);
        double[] points = FlattenColumnMajor("ecdfhist", args[1], line, col);
        if (curve.Length != points.Length)
        {
            throw new JgsRuntimeException(line, col,
                "ecdfhist: the curve and the points it was evaluated at must be the same length.");
        }

        if (points.Length < 2)
        {
            throw new JgsRuntimeException(line, col, "ecdfhist needs at least two points.");
        }

        double[] centres;
        double[] edges;
        if (args.Count == 3 && !IsPlaceholderValue(args[2]))
        {
            double[] given = NumericVector("ecdfhist", args[2], line, col);

            // A single number is how many bins to make; a vector is the centres themselves, which is
            // the same pair of readings hist takes.
            if (given.Length == 1)
            {
                centres = EvenCentres(points, (int)given[0], line, col);
            }
            else
            {
                centres = given;
            }
        }
        else
        {
            centres = EvenCentres(points, 10, line, col);
        }

        edges = EdgesFromCentres(centres);
        double[] heights = EmpiricalDistribution.HistogramFromEmpirical(curve, points, edges);
        return Outputs(wanted, Numbers(heights), Numbers(centres));
    }

    /// <summary>Evenly spaced bin centres spanning the points a curve was evaluated at.</summary>
    private static double[] EvenCentres(double[] points, int bins, int line, int col)
    {
        if (bins < 1)
        {
            throw new JgsRuntimeException(line, col, "ecdfhist: the number of bins must be positive.");
        }

        double low = points[0];
        double high = points[^1];
        foreach (double point in points)
        {
            low = Math.Min(low, point);
            high = Math.Max(high, point);
        }

        if (high == low)
        {
            high = low + 1;
        }

        double width = (high - low) / bins;
        var centres = new double[bins];
        for (int i = 0; i < bins; i++)
        {
            centres[i] = low + (width * (i + 0.5));
        }

        return centres;
    }

    /// <summary>The edges that put each centre in the middle of its own bin.</summary>
    private static double[] EdgesFromCentres(double[] centres)
    {
        int bins = centres.Length;
        var edges = new double[bins + 1];
        if (bins == 1)
        {
            edges[0] = centres[0] - 0.5;
            edges[1] = centres[0] + 0.5;
            return edges;
        }

        for (int i = 1; i < bins; i++)
        {
            edges[i] = (centres[i - 1] + centres[i]) / 2;
        }

        edges[0] = centres[0] - ((centres[1] - centres[0]) / 2);
        edges[bins] = centres[^1] + ((centres[^1] - centres[^2]) / 2);
        return edges;
    }

    /// <summary>
    /// <c>[F, XI, BW] = ksdensity(x)</c>: a kernel-smoothed estimate of the distribution the sample
    /// came from, evaluated either at points the caller names or on a grid covering the sample, and
    /// the bandwidth it was smoothed with.
    /// </summary>
    private static JgsValue[] SmoothedDensity(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "ksdensity needs some data.");
        }

        ParsedArgs parsed = KsdensityOptions.Parse(args, 2, line, col);
        double[] sample = FlattenColumnMajor("ksdensity", parsed.Positional[0], line, col);

        EmpiricalDistribution.Kernel kernel =
            parsed.Word("Kernel", "normal", "normal", "box", "triangle", "epanechnikov") switch
            {
                "box" => EmpiricalDistribution.Kernel.Box,
                "triangle" => EmpiricalDistribution.Kernel.Triangle,
                "epanechnikov" => EmpiricalDistribution.Kernel.Epanechnikov,
                _ => EmpiricalDistribution.Kernel.Normal,
            };

        EmpiricalDistribution.SmoothedKind kind =
            parsed.Word("Function", "pdf", "pdf", "cdf", "icdf", "survivor", "cumhazard") switch
            {
                "cdf" => EmpiricalDistribution.SmoothedKind.Cdf,
                "icdf" => EmpiricalDistribution.SmoothedKind.Icdf,
                "survivor" => EmpiricalDistribution.SmoothedKind.Survivor,
                "cumhazard" => EmpiricalDistribution.SmoothedKind.CumulativeHazard,
                _ => EmpiricalDistribution.SmoothedKind.Pdf,
            };

        EmpiricalDistribution.BoundaryRule rule =
            parsed.Word("BoundaryCorrection", "log", "log", "reflection") == "reflection"
                ? EmpiricalDistribution.BoundaryRule.Reflection
                : EmpiricalDistribution.BoundaryRule.Log;

        (double lower, double upper) = SupportBounds(parsed, line, col);

        double[]? weights = null;
        if (parsed.Named("Weights") is { } given)
        {
            weights = NumericVector("ksdensity", given, line, col);
            if (weights.Length != sample.Length)
            {
                throw new JgsRuntimeException(line, col, "ksdensity: 'Weights' needs one weight per observation.");
            }
        }

        double bandwidth = parsed.Scalar("Bandwidth", 0);
        if (bandwidth < 0)
        {
            throw new JgsRuntimeException(line, col, "ksdensity: 'Bandwidth' must be positive.");
        }

        int points = parsed.Whole("NumPoints", 100);
        if (points < 1)
        {
            throw new JgsRuntimeException(line, col, "ksdensity: 'NumPoints' must be positive.");
        }

        double[] at = parsed.Positional.Count > 1
            ? FlattenColumnMajor("ksdensity", parsed.Positional[1], line, col)
            : DefaultGrid(sample, bandwidth, kind, points, lower, upper);

        double[] curve = EmpiricalDistribution.KernelDensity(
            sample, weights, at, bandwidth, kernel, kind, lower, upper, rule);

        // The third output is the width the estimate actually used, which is the interesting one
        // exactly when the caller did not name it: it is how a script reports what the automatic
        // rule chose, and what it passes back to make a second estimate comparable to the first.
        double used = bandwidth > 0
            ? bandwidth
            : EmpiricalDistribution.DefaultBandwidth(DescriptiveStatistics.WithoutNaN(sample));

        if (wanted == 0)
        {
            JGraph.Api.JG.Plot(at, curve).DisplayName = "Kernel estimate";
            JGraph.Api.JG.XLabel("x");
            JGraph.Api.JG.YLabel(kind.ToString());
            return [];
        }

        return Outputs(wanted, Column(curve), Column(at), JgsValue.Number(used));
    }

    /// <summary>
    /// Where a smoothed estimate is evaluated when the caller names no points: a grid reaching three
    /// bandwidths past the sample for a curve over the data, and the whole probability scale for the
    /// inverse, which is asked in probabilities rather than in values.
    /// </summary>
    private static double[] DefaultGrid(
        double[] sample,
        double bandwidth,
        EmpiricalDistribution.SmoothedKind kind,
        int points,
        double lower,
        double upper)
    {
        var grid = new double[points];
        if (kind == EmpiricalDistribution.SmoothedKind.Icdf)
        {
            for (int i = 0; i < points; i++)
            {
                grid[i] = points == 1 ? 0.5 : (double)i / (points - 1);
            }

            return grid;
        }

        double[] clean = DescriptiveStatistics.WithoutNaN(sample);
        double width = bandwidth > 0 ? bandwidth : EmpiricalDistribution.DefaultBandwidth(clean);
        double low = clean.Length == 0 ? 0 : clean[0];
        double high = low;
        foreach (double value in clean)
        {
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        low = Math.Max(low - (3 * width), lower);
        high = Math.Min(high + (3 * width), upper);
        for (int i = 0; i < points; i++)
        {
            grid[i] = points == 1 ? (low + high) / 2 : low + ((high - low) * i / (points - 1.0));
        }

        return grid;
    }

    /// <summary>The support a smoothed estimate is confined to: unbounded, positive, or a named pair.</summary>
    private static (double Lower, double Upper) SupportBounds(ParsedArgs parsed, int line, int col)
    {
        if (parsed.Named("Support") is not { } support)
        {
            return (double.NegativeInfinity, double.PositiveInfinity);
        }

        if (support.Type == JgsType.String)
        {
            return parsed.Word("Support", "unbounded", "unbounded", "positive") == "positive"
                ? (0, double.PositiveInfinity)
                : (double.NegativeInfinity, double.PositiveInfinity);
        }

        double[] bounds = NumericVector("ksdensity", support, line, col);
        if (bounds.Length != 2 || !(bounds[0] < bounds[1]))
        {
            throw new JgsRuntimeException(line, col,
                "ksdensity: 'Support' takes 'unbounded', 'positive', or an increasing [low high] pair.");
        }

        return (bounds[0], bounds[1]);
    }

    /// <summary>A column vector, which is the shape every one of these names answers in.</summary>
    private static JgsValue Column(double[] values)
    {
        if (values.Length == 0)
        {
            return JgsValue.Array([]);
        }

        return JgsMatrix.FromColumnMajor(values, values.Length, 1);
    }

    // --- The legacy nan* names ---------------------------------------------------------------------

    /// <summary>
    /// The eight <c>nan*</c> names, which predate every base reduction taking <c>'omitnan'</c> and now
    /// mean exactly that. Each one looks its base name up in the environment when it is called rather
    /// than when it is registered, so it inherits whatever that name has grown by then — the MATLAB
    /// dimension handling included.
    /// </summary>
    private static void RegisterLegacyNanBuiltins(JgsEnvironment env)
    {
        foreach ((string legacy, string modern) in new[]
        {
            ("nanmax", "max"), ("nanmin", "min"), ("nanmean", "mean"), ("nanmedian", "median"),
            ("nanstd", "std"), ("nansum", "sum"), ("nanvar", "var"),
        })
        {
            string capture = modern;
            string name = legacy;
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
                OmittingNaN(env, name, capture, args, line, col))));
        }

        // nancov is the odd one out: cov spells the same request as a word rather than a flag, and
        // that word goes last rather than in the option tail.
        env.Declare("nancov", JgsValue.Function(new BuiltinFunction("nancov", (args, line, col) =>
        {
            if (!env.TryGet("cov", out JgsValue covariance) || covariance.Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "nancov: cov is not available.");
            }

            var forwarded = new List<JgsValue>(args) { JgsValue.Str("omitrows") };
            return covariance.AsCallable.Call([.. forwarded], line, col);
        })));
    }

    /// <summary>
    /// Calls a base reduction with <c>'omitnan'</c> appended. <c>max</c> and <c>min</c> take their
    /// option after a placeholder second argument, which is the same shape MATLAB's own
    /// <c>max(x, [], 'omitnan')</c> has.
    /// </summary>
    private static JgsValue OmittingNaN(
        JgsEnvironment env,
        string legacy,
        string modern,
        IReadOnlyList<JgsValue> args,
        int line,
        int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{legacy} needs some data.");
        }

        if (!env.TryGet(modern, out JgsValue target) || target.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"{legacy}: {modern} is not available.");
        }

        var forwarded = new List<JgsValue>(args);
        if (modern is "max" or "min" && forwarded.Count == 1)
        {
            forwarded.Add(JgsValue.Array([]));
        }

        forwarded.Add(JgsValue.Str("omitnan"));
        return target.AsCallable.Call([.. forwarded], line, col);
    }
}
