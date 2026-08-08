using JGraph.Statistics;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Hypothesis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave F, part one: the hypothesis tests. Every one answers the same four things — whether the
/// null hypothesis is rejected at the level asked for, how improbable the data would be if it were
/// true, an interval for whatever was being tested, and a structure holding the statistic itself.
/// </summary>
/// <remarks>
/// <para>
/// The tests of a mean or a variance work column by column when given a matrix, exactly as the
/// reductions do, and every output takes the shape that implies: one value per column for the decision
/// and the probability, two rows for the interval, and a structure whose fields are arrays rather than
/// an array of structures. That is one description — <see cref="ShapedTest"/> — rather than five, and
/// it is what makes <c>ttest(X)</c> on a matrix answer a row of decisions instead of refusing.
/// </para>
/// <para>
/// The output order is MathWorks' and is not the same for every name: the parametric tests lead with
/// the decision, the rank tests lead with the probability, and <c>vartestn</c>, <c>dwtest</c> and
/// <c>linhyptest</c> lead with the probability and never report a decision at all. Those are the
/// documented signatures and a script written against them would break under a tidier convention.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec MeanTestOptions = new(
        "ttest", [], ["Alpha", "Dim", "Tail"]);

    private static readonly OptionSpec TwoMeanTestOptions = new(
        "ttest2", [], ["Alpha", "Dim", "Tail", "Vartype"]);

    private static readonly OptionSpec VarianceTestOptions = new(
        "vartest", [], ["Alpha", "Dim", "Tail"]);

    private static readonly OptionSpec SeveralVarianceOptions = new(
        "vartestn", [], ["Alpha", "Display", "TestType"]);

    private static readonly OptionSpec DistributionTestOptions = new(
        "kstest", [], ["Alpha", "CDF", "Tail"]);

    private static readonly OptionSpec TwoSampleDistributionOptions = new(
        "kstest2", [], ["Alpha", "Tail"]);

    private static readonly OptionSpec ComposedFitOptions = new(
        "lillietest", [], ["Alpha", "Distr", "Distribution", "MCTol", "Asymptotic"]);

    private static readonly OptionSpec BinnedFitOptions = new(
        "chi2gof",
        [],
        ["Alpha", "Ctrs", "Edges", "NBins", "CDF", "Expected", "NParams", "EMin", "Frequency"]);

    private static readonly OptionSpec RunTestOptions = new(
        "runstest", [], ["Alpha", "Method", "Tail"], StringPositionals: 2);

    private static readonly OptionSpec RankTestOptions = new(
        "ranksum", [], ["alpha", "method", "tail"]);

    private static readonly OptionSpec ContingencyOptions = new(
        "fishertest", [], ["Alpha", "Tail"]);

    private static readonly OptionSpec SerialCorrelationOptions = new(
        "dwtest", [], ["Method", "Tail"]);

    private static readonly OptionSpec SampleSizeOptions = new(
        "sampsizepwr", [], ["Alpha", "Tail", "Ratio"], StringPositionals: 1);

    /// <summary>Registers the hypothesis tests.</summary>
    private static void RegisterHypothesisTestBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                { MultiOutput = both }));

        DefineBoth("ttest", StudentTest);
        DefineBoth("ttest2", TwoSampleStudentTest);
        DefineBoth("ztest", KnownDeviationTest);
        DefineBoth("vartest", VarianceTest);
        DefineBoth("vartest2", TwoVarianceTest);
        DefineBoth("vartestn", SeveralVarianceTest);

        DefineBoth("kstest", DistributionTest);
        DefineBoth("kstest2", TwoSampleDistributionTest);
        DefineBoth("lillietest", (args, wanted, line, col) => ComposedFitTest("lillietest", args, wanted, line, col));
        DefineBoth("adtest", (args, wanted, line, col) => ComposedFitTest("adtest", args, wanted, line, col));
        DefineBoth("jbtest", SkewnessKurtosisTest);
        DefineBoth("chi2gof", BinnedFitTest);
        DefineBoth("runstest", RandomnessTest);

        DefineBoth("ranksum", (args, wanted, line, col) => RankTest("ranksum", args, wanted, line, col));
        DefineBoth("signrank", (args, wanted, line, col) => RankTest("signrank", args, wanted, line, col));
        DefineBoth("signtest", (args, wanted, line, col) => RankTest("signtest", args, wanted, line, col));
        DefineBoth("ansaribradley", DispersionRankTest);

        DefineBoth("barttest", DimensionalityTest);
        DefineBoth("fishertest", ExactTableTest);
        DefineBoth("dwtest", SerialCorrelationTest);
        DefineBoth("linhyptest", LinearRestrictionTest);
        DefineBoth("sampsizepwr", SampleSizeOrPower);
    }

    // --- Tests of a mean --------------------------------------------------------------------------

    /// <summary>
    /// <c>[h, p, ci, stats] = ttest(x)</c>, <c>ttest(x, m)</c> and <c>ttest(x, y)</c>: whether a normal
    /// sample's mean is what was claimed, or whether two paired samples differ.
    /// </summary>
    private static JgsValue[] StudentTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MeanTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "ttest(x), ttest(x, m) or ttest(x, y) tests a mean or a pair of matched samples.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        int? dim = parsed.Named("Dim") is null ? null : parsed.Whole("Dim", 1);

        // ttest(x, y) with matching sizes is the paired test; ttest(x, m) with a single number is the
        // one-sample test. MathWorks tells them apart the same way, by whether the second argument is
        // a scalar.
        bool paired = parsed.Positional.Count == 2 && parsed.Positional[1].Type != JgsType.Number;
        double mean = paired || parsed.Positional.Count == 1
            ? 0
            : Num("ttest", parsed.Positional, 1, line, col);

        JgsValue subject = paired
            ? Difference("ttest", parsed.Positional[0], parsed.Positional[1], line, col)
            : parsed.Positional[0];

        return ShapedTest(
            "ttest", subject, dim, wanted, alpha, line, col,
            slice =>
            {
                ParametricTests.LocationTest outcome =
                    ParametricTests.OneSampleT(slice, mean, alpha, tail);
                return (outcome.P, [outcome.Lower, outcome.Upper],
                    [("tstat", [outcome.Statistic]), ("df", [outcome.Df]), ("sd", outcome.Spread)]);
            });
    }

    /// <summary>
    /// <c>[h, p, ci, stats] = ttest2(x, y)</c>: whether two independent samples have the same mean,
    /// pooling their variances unless <c>'Vartype', 'unequal'</c> asks for Welch's test.
    /// </summary>
    private static JgsValue[] TwoSampleStudentTest(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = TwoMeanTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "ttest2(x, y) compares the means of two samples.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        int? dim = parsed.Named("Dim") is null ? null : parsed.Whole("Dim", 1);
        bool pooled = parsed.Word("Vartype", "equal", "equal", "unequal") == "equal";

        return ShapedPairTest(
            "ttest2", parsed.Positional[0], parsed.Positional[1], dim, wanted, alpha, line, col,
            (first, second) =>
            {
                ParametricTests.LocationTest outcome =
                    ParametricTests.TwoSampleT(first, second, 0, alpha, tail, pooled);
                return (outcome.P, [outcome.Lower, outcome.Upper],
                    [("tstat", [outcome.Statistic]), ("df", [outcome.Df]), ("sd", outcome.Spread)]);
            });
    }

    /// <summary>
    /// <c>[h, p, ci, stats] = ztest(x, m, sigma)</c>: the same question as <c>ttest</c>, asked where the
    /// standard deviation is known rather than estimated from the sample.
    /// </summary>
    private static JgsValue[] KnownDeviationTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MeanTestOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "ztest(x, m, sigma) needs the hypothesized mean and the known standard deviation.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        int? dim = parsed.Named("Dim") is null ? null : parsed.Whole("Dim", 1);
        double mean = Num("ztest", parsed.Positional, 1, line, col);
        double sigma = Num("ztest", parsed.Positional, 2, line, col);

        return ShapedTest(
            "ztest", parsed.Positional[0], dim, wanted, alpha, line, col,
            slice =>
            {
                ParametricTests.LocationTest outcome = ParametricTests.Z(slice, mean, sigma, alpha, tail);
                return (outcome.P, [outcome.Lower, outcome.Upper], [("zval", [outcome.Statistic])]);
            });
    }

    // --- Tests of a variance ----------------------------------------------------------------------

    /// <summary>
    /// <c>[h, p, ci, stats] = vartest(x, v)</c>: whether a normal sample's variance is <c>v</c>. The
    /// interval is for the variance and is not symmetric about the estimate.
    /// </summary>
    private static JgsValue[] VarianceTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = VarianceTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "vartest(x, v) needs the hypothesized variance.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        int? dim = parsed.Named("Dim") is null ? null : parsed.Whole("Dim", 1);
        double variance = Num("vartest", parsed.Positional, 1, line, col);

        return ShapedTest(
            "vartest", parsed.Positional[0], dim, wanted, alpha, line, col,
            slice =>
            {
                ParametricTests.SpreadTest outcome =
                    ParametricTests.Variance(slice, variance, alpha, tail);
                return (outcome.P, [outcome.Lower, outcome.Upper],
                    [("chisqstat", [outcome.Statistic]), ("df", outcome.Df)]);
            });
    }

    /// <summary>
    /// <c>[h, p, ci, stats] = vartest2(x, y)</c>: whether two samples share a variance. The interval is
    /// for the ratio of the two, so it contains 1 exactly when the test does not reject.
    /// </summary>
    private static JgsValue[] TwoVarianceTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = VarianceTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "vartest2(x, y) compares the variances of two samples.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        int? dim = parsed.Named("Dim") is null ? null : parsed.Whole("Dim", 1);

        return ShapedPairTest(
            "vartest2", parsed.Positional[0], parsed.Positional[1], dim, wanted, alpha, line, col,
            (first, second) =>
            {
                ParametricTests.SpreadTest outcome =
                    ParametricTests.TwoVariances(first, second, alpha, tail);
                return (outcome.P, [outcome.Lower, outcome.Upper],
                    [("fstat", [outcome.Statistic]), ("df1", [outcome.Df[0]]), ("df2", [outcome.Df[1]])]);
            });
    }

    /// <summary>
    /// <c>[p, stats] = vartestn(X)</c> or <c>vartestn(x, group)</c>: whether several groups share a
    /// variance. Unlike its two-sample siblings this one leads with the probability and never reports
    /// a decision, because MathWorks documents no <c>h</c> for it.
    /// </summary>
    private static JgsValue[] SeveralVarianceTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = SeveralVarianceOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "vartestn(X) compares the variances of a matrix's columns; vartestn(x, group) compares groups.");
        }

        _ = Level(parsed);
        _ = parsed.Word("Display", "off", "on", "off");
        string method = parsed.Word(
            "TestType", "Bartlett",
            "Bartlett", "LeveneQuadratic", "LeveneAbsolute", "BrownForsythe", "OBrien");

        (double[][] groups, string[] _) = Grouped("vartestn", parsed.Positional, line, col);
        ParametricTests.SpreadComparison comparison = method switch
        {
            "LeveneQuadratic" => ParametricTests.SpreadComparison.LeveneQuadratic,
            "LeveneAbsolute" => ParametricTests.SpreadComparison.LeveneAbsolute,
            "BrownForsythe" => ParametricTests.SpreadComparison.BrownForsythe,
            "OBrien" => ParametricTests.SpreadComparison.OBrien,
            _ => ParametricTests.SpreadComparison.Bartlett,
        };

        ParametricTests.SpreadTest outcome = Guarded(
            "vartestn", () => ParametricTests.SeveralVariances(groups, comparison), line, col);

        JgsValue stats = comparison == ParametricTests.SpreadComparison.Bartlett
            ? Structure(
                ("chisqstat", JgsValue.Number(outcome.Statistic)),
                ("df", JgsValue.Number(outcome.Df[0])))
            : Structure(
                ("fstat", JgsValue.Number(outcome.Statistic)),
                ("df", JgsMatrix.FromColumnMajor([outcome.Df[0], outcome.Df[1]], 1, 2)));

        return Outputs(wanted, JgsValue.Number(outcome.P), stats);
    }

    // --- Tests of a distribution -------------------------------------------------------------------

    /// <summary>
    /// <c>[h, p, ksstat, cv] = kstest(x)</c>: whether a sample came from a fully specified distribution,
    /// the standard normal unless <c>'CDF'</c> names another.
    /// </summary>
    private static JgsValue[] DistributionTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = DistributionTestOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "kstest(x) tests one sample against one distribution.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed, "unequal", "larger", "smaller");
        Func<double[], double[]> cdf = HypothesizedCdf(parsed.Named("CDF"), line, col);

        GoodnessOfFit.FitTest outcome = Guarded(
            "kstest",
            () => GoodnessOfFit.KolmogorovSmirnov(
                ToDoubles("kstest", parsed.Positional[0], line, col), cdf, alpha, tail),
            line,
            col);

        return Outputs(
            wanted,
            JgsValue.Bool(outcome.P <= alpha),
            JgsValue.Number(outcome.P),
            JgsValue.Number(outcome.Statistic),
            JgsValue.Number(outcome.Critical));
    }

    /// <summary>
    /// <c>[h, p, ks2stat] = kstest2(x1, x2)</c>: whether two samples came from the same distribution,
    /// without naming one.
    /// </summary>
    private static JgsValue[] TwoSampleDistributionTest(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = TwoSampleDistributionOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "kstest2(x1, x2) compares two samples.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed, "unequal", "larger", "smaller");

        GoodnessOfFit.FitTest outcome = Guarded(
            "kstest2",
            () => GoodnessOfFit.TwoSampleKolmogorovSmirnov(
                ToDoubles("kstest2", parsed.Positional[0], line, col),
                ToDoubles("kstest2", parsed.Positional[1], line, col),
                alpha,
                tail),
            line,
            col);

        return Outputs(
            wanted,
            JgsValue.Bool(outcome.P <= alpha),
            JgsValue.Number(outcome.P),
            JgsValue.Number(outcome.Statistic));
    }

    /// <summary>
    /// <c>lillietest</c> and <c>adtest</c>: whether a sample came from a family whose parameters were
    /// estimated from that same sample, which needs its own null distribution and gets one from a table
    /// of published critical values.
    /// </summary>
    private static JgsValue[] ComposedFitTest(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ComposedFitOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}(x) tests one sample.");
        }

        double alpha = Level(parsed);
        if (parsed.Named("MCTol") is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'MCTol' asks for a simulated null distribution; this one is read from a published "
                + "table of critical values, so there is no simulation tolerance to set.");
        }

        // MathWorks spells the family 'Distr' for one of these and 'Distribution' for the other; both
        // are accepted for both, because a script has no way to remember which is which.
        string word = parsed.Text("Distr") ?? parsed.Text("Distribution") ?? "norm";
        GoodnessOfFit.FittedFamily family = word.ToLowerInvariant() switch
        {
            "norm" or "normal" => GoodnessOfFit.FittedFamily.Normal,
            "exp" or "exponential" => GoodnessOfFit.FittedFamily.Exponential,
            "ev" or "extreme value" => GoodnessOfFit.FittedFamily.ExtremeValue,
            "logn" or "lognormal" when name == "adtest" => GoodnessOfFit.FittedFamily.Lognormal,
            "weibull" or "wbl" when name == "adtest" => GoodnessOfFit.FittedFamily.Weibull,
            _ => throw new JgsRuntimeException(line, col,
                $"{name} does not fit '{word}' (expected one of 'norm', 'exp', 'ev'"
                + (name == "adtest" ? ", 'logn', 'weibull'" : string.Empty) + ")."),
        };

        double[] sample = ToDoubles(name, parsed.Positional[0], line, col);
        GoodnessOfFit.FitTest outcome = Guarded(
            name,
            () => name == "adtest"
                ? GoodnessOfFit.AndersonDarling(sample, family, alpha)
                : GoodnessOfFit.Lilliefors(sample, family, alpha),
            line,
            col);

        return Outputs(
            wanted,
            JgsValue.Bool(outcome.P <= alpha),
            JgsValue.Number(outcome.P),
            JgsValue.Number(outcome.Statistic),
            JgsValue.Number(outcome.Critical));
    }

    /// <summary>
    /// <c>[h, p, jbstat, critval] = jbtest(x)</c>: whether a sample's skewness and kurtosis are the
    /// normal distribution's.
    /// </summary>
    private static JgsValue[] SkewnessKurtosisTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("jbtest", args, 1, 3, line, col);
        double alpha = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Num("jbtest", args, 1, line, col)
            : 0.05;

        if (args.Count > 2 && !IsPlaceholderValue(args[2]))
        {
            throw new JgsRuntimeException(line, col,
                "jbtest: the third argument asks for a simulated null distribution; the statistic here is "
                + "referred to its limiting chi-square with two degrees of freedom, so there is no tolerance to set.");
        }

        GoodnessOfFit.FitTest outcome = Guarded(
            "jbtest",
            () => GoodnessOfFit.JarqueBera(ToDoubles("jbtest", args[0], line, col), alpha),
            line,
            col);

        return Outputs(
            wanted,
            JgsValue.Bool(outcome.P <= alpha),
            JgsValue.Number(outcome.P),
            JgsValue.Number(outcome.Statistic),
            JgsValue.Number(outcome.Critical));
    }

    /// <summary>
    /// <c>[h, p, stats] = chi2gof(x)</c>: the sample put into bins and each bin's count compared with
    /// what the distribution says to expect.
    /// </summary>
    private static JgsValue[] BinnedFitTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = BinnedFitOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "chi2gof(x) tests one sample.");
        }

        double alpha = Level(parsed);
        double[] data = ToDoubles("chi2gof", parsed.Positional[0], line, col);
        double[]? frequency = parsed.Vector("Frequency");
        double minimum = parsed.Scalar("EMin", 5);

        (double[] values, double[] weights) = Weighted("chi2gof", data, frequency, line, col);
        double[] edges = BinEdges(parsed, values, line, col);
        double[] observed = CountsIn(edges, values, weights);

        double total = 0;
        foreach (double count in observed)
        {
            total += count;
        }

        int estimated;
        double[] expected;
        if (parsed.Named("Expected") is { } given)
        {
            expected = NumericVector("chi2gof", given, line, col);
            if (expected.Length != observed.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"chi2gof: {expected.Length} expected counts for {observed.Length} bins.");
            }

            estimated = parsed.Whole("NParams", 0);
        }
        else
        {
            (Func<double[], double[]> cdf, int fitted) = BinnedCdf(parsed, values, line, col);
            double[] atEdges = cdf(edges);
            expected = new double[observed.Length];
            for (int i = 0; i < observed.Length; i++)
            {
                expected[i] = total * (atEdges[i + 1] - atEdges[i]);
            }

            estimated = parsed.Named("NParams") is null ? fitted : parsed.Whole("NParams", fitted);
        }

        GoodnessOfFit.BinnedTest outcome = Guarded(
            "chi2gof",
            () => GoodnessOfFit.ChiSquareBins(edges, observed, expected, estimated, minimum),
            line,
            col);

        JgsValue stats = Structure(
            ("chi2stat", JgsValue.Number(outcome.Statistic)),
            ("df", JgsValue.Number(outcome.Df)),
            ("edges", RowVector(outcome.Edges)),
            ("O", RowVector(outcome.Observed)),
            ("E", RowVector(outcome.Expected)));

        return Outputs(
            wanted,
            JgsValue.Bool(outcome.P <= alpha),
            JgsValue.Number(outcome.P),
            stats);
    }

    /// <summary>
    /// <c>[h, p, stats] = runstest(x)</c>: whether a sequence alternates about its median the way an
    /// independent sequence would.
    /// </summary>
    private static JgsValue[] RandomnessTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RunTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "runstest(x), runstest(x, v) or runstest(x, 'ud') tests a sequence for randomness.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        double[] data = ToDoubles("runstest", parsed.Positional[0], line, col);

        bool upDown = false;
        double reference = double.NaN;
        if (parsed.Positional.Count == 2)
        {
            JgsValue second = parsed.Positional[1];
            if (second.Type == JgsType.String)
            {
                string word = second.AsString;
                if (!string.Equals(word, "ud", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(word, "up-down", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"runstest: '{word}' is not a reference (expected a number, 'ud', or 'mean').");
                }

                upDown = true;
            }
            else if (!IsPlaceholderValue(second))
            {
                reference = Num("runstest", parsed.Positional, 1, line, col);
            }
        }

        var above = new List<bool>(data.Length);
        if (upDown)
        {
            // Runs up and down: each step is compared with the one before it, and a step of exactly
            // zero is no step at all.
            for (int i = 1; i < data.Length; i++)
            {
                if (data[i] > data[i - 1])
                {
                    above.Add(true);
                }
                else if (data[i] < data[i - 1])
                {
                    above.Add(false);
                }
            }
        }
        else
        {
            double centre = double.IsNaN(reference) ? DescriptiveStatistics.Median(data) : reference;
            foreach (double value in data)
            {
                if (value > centre)
                {
                    above.Add(true);
                }
                else if (value < centre)
                {
                    above.Add(false);
                }
            }
        }

        string chosen = parsed.Word("Method", "auto", "auto", "exact", "approximate");
        bool exact = chosen switch
        {
            "exact" => true,
            "approximate" => false,
            _ => !upDown && above.Count <= 50,
        };

        if (upDown && exact)
        {
            throw new JgsRuntimeException(line, col,
                "runstest: the runs up and down have no exact distribution here; ask for 'approximate'.");
        }

        GoodnessOfFit.RunTest outcome = Guarded(
            "runstest", () => GoodnessOfFit.Runs(above, exact, tail), line, col);

        JgsValue stats = Structure(
            ("nruns", JgsValue.Number(outcome.Runs)),
            ("n1", JgsValue.Number(outcome.Above)),
            ("n0", JgsValue.Number(outcome.Below)),
            ("z", JgsValue.Number(outcome.Z)));

        return Outputs(wanted, JgsValue.Bool(outcome.P <= alpha), JgsValue.Number(outcome.P), stats);
    }

    // --- Rank tests -------------------------------------------------------------------------------

    /// <summary>
    /// <c>[p, h, stats] = ranksum(x, y)</c>, <c>signrank(x, y)</c> and <c>signtest(x, y)</c>: the three
    /// tests of location that use only the order of the observations. All three lead with the
    /// probability rather than the decision, which is MathWorks' order for them.
    /// </summary>
    private static JgsValue[] RankTest(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RankTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col, $"{name}(x, y) or {name}(x, m) compares two samples.");
        }

        double alpha = parsed.Scalar("alpha", 0.05);
        Tail tail = parsed.Word("tail", "both", "both", "right", "left") switch
        {
            "right" => Tail.Right,
            "left" => Tail.Left,
            _ => Tail.Both,
        };

        RankTests.Method method = parsed.Word("method", "auto", "auto", "exact", "approximate") switch
        {
            "exact" => RankTests.Method.Exact,
            "approximate" => RankTests.Method.Approximate,
            _ => RankTests.Method.Automatic,
        };

        double[] first = ToDoubles(name, parsed.Positional[0], line, col);
        double[] second = parsed.Positional.Count == 2
            ? ToDoubles(name, parsed.Positional[1], line, col)
            : [];

        RankTests.RankOutcome outcome;
        JgsValue stats;
        if (name == "ranksum")
        {
            if (second.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "ranksum(x, y) needs two samples.");
            }

            outcome = Guarded(name, () => RankTests.RankSum(first, second, tail, method), line, col);
            stats = outcome.Exact
                ? Structure(("ranksum", JgsValue.Number(outcome.Statistic)))
                : Structure(
                    ("ranksum", JgsValue.Number(outcome.Statistic)),
                    ("zval", JgsValue.Number(outcome.Z)));
        }
        else
        {
            // The paired tests take either a second sample or a single number to compare against,
            // which is the same thing once the difference has been formed.
            double[] differences = Differences(name, first, second, parsed.Positional, line, col);
            outcome = name == "signrank"
                ? Guarded(name, () => RankTests.SignedRank(differences, tail, method), line, col)
                : Guarded(name, () => RankTests.Sign(differences, tail, method), line, col);

            string field = name == "signrank" ? "signedrank" : "sign";
            stats = outcome.Exact
                ? Structure((field, JgsValue.Number(outcome.Statistic)))
                : Structure(
                    (field, JgsValue.Number(outcome.Statistic)),
                    ("zval", JgsValue.Number(outcome.Z)));
        }

        return Outputs(
            wanted, JgsValue.Number(outcome.P), JgsValue.Bool(outcome.P <= alpha), stats);
    }

    /// <summary>
    /// <c>[h, p, stats] = ansaribradley(x, y)</c>: whether two samples are equally dispersed. This one
    /// leads with the decision, unlike the three rank tests of location.
    /// </summary>
    private static JgsValue[] DispersionRankTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = RankTestOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "ansaribradley(x, y) compares the spread of two samples.");
        }

        double alpha = parsed.Scalar("alpha", 0.05);
        Tail tail = parsed.Word("tail", "both", "both", "right", "left") switch
        {
            "right" => Tail.Right,
            "left" => Tail.Left,
            _ => Tail.Both,
        };

        RankTests.Method method = parsed.Word("method", "auto", "auto", "exact", "approximate") switch
        {
            "exact" => RankTests.Method.Exact,
            "approximate" => RankTests.Method.Approximate,
            _ => RankTests.Method.Automatic,
        };

        RankTests.RankOutcome outcome = Guarded(
            "ansaribradley",
            () => RankTests.AnsariBradley(
                ToDoubles("ansaribradley", parsed.Positional[0], line, col),
                ToDoubles("ansaribradley", parsed.Positional[1], line, col),
                tail,
                method),
            line,
            col);

        JgsValue stats = Structure(
            ("W", JgsValue.Number(outcome.Statistic)),
            ("Wstar", JgsValue.Number(outcome.Z)));

        return Outputs(wanted, JgsValue.Bool(outcome.P <= alpha), JgsValue.Number(outcome.P), stats);
    }

    // --- Tests about a model ------------------------------------------------------------------------

    /// <summary>
    /// <c>[ndim, prob, chisquare] = barttest(x, alpha)</c>: how many principal components a set of
    /// variables really needs.
    /// </summary>
    private static JgsValue[] DimensionalityTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("barttest", args, 1, 2, line, col);
        double alpha = args.Count > 1 && !IsPlaceholderValue(args[1])
            ? Num("barttest", args, 1, line, col)
            : 0.05;

        double[,] observations = AsRectangle("barttest", args[0], line, col);
        LinearModelTests.Dimensionality outcome = Guarded(
            "barttest", () => LinearModelTests.Bartlett(observations, alpha), line, col);

        return Outputs(
            wanted,
            JgsValue.Number(outcome.Dimension),
            Column(outcome.P),
            Column(outcome.ChiSquare));
    }

    /// <summary>
    /// <c>[h, p, stats] = fishertest(x)</c>: Fisher's exact test of a two-by-two table of counts.
    /// </summary>
    private static JgsValue[] ExactTableTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = ContingencyOptions.Parse(args, 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "fishertest(x) takes a two-by-two table of counts.");
        }

        double alpha = Level(parsed);
        Tail tail = Direction(parsed);

        (double[] flat, int rows, int columns) = DenseMatrix("fishertest", parsed.Positional[0], line, col);
        if (rows != 2 || columns != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"fishertest: the table is {rows}-by-{columns}; only a two-by-two table has an exact test here.");
        }

        foreach (double count in flat)
        {
            if (count < 0 || count != Math.Floor(count) || !double.IsFinite(count))
            {
                throw new JgsRuntimeException(line, col, "fishertest: a contingency table holds whole counts.");
            }
        }

        ContingencyTests.ExactTable outcome = Guarded(
            "fishertest",
            () => ContingencyTests.Fisher(
                (int)flat[0], (int)flat[2], (int)flat[1], (int)flat[3], alpha, tail),
            line,
            col);

        JgsValue stats = Structure(
            ("OddsRatio", JgsValue.Number(outcome.OddsRatio)),
            ("ConfidenceInterval", JgsMatrix.FromColumnMajor([outcome.Lower, outcome.Upper], 1, 2)));

        return Outputs(wanted, JgsValue.Bool(outcome.P <= alpha), JgsValue.Number(outcome.P), stats);
    }

    /// <summary>
    /// <c>[p, d] = dwtest(r, x)</c>: whether the residuals of a fitted model are correlated with the
    /// ones beside them.
    /// </summary>
    private static JgsValue[] SerialCorrelationTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = SerialCorrelationOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "dwtest(r, x) needs the residuals and the design matrix they came from.");
        }

        double[] residuals = ToDoubles("dwtest", parsed.Positional[0], line, col);
        double[,] design = AsRectangle("dwtest", parsed.Positional[1], line, col);
        if (design.GetLength(0) != residuals.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"dwtest: {residuals.Length} residuals against a design with {design.GetLength(0)} rows.");
        }

        Tail tail = Direction(parsed);
        bool exact = parsed.Word("Method", "auto", "auto", "exact", "approximate") switch
        {
            "exact" => true,
            "approximate" => false,
            _ => residuals.Length < 400,
        };

        LinearModelTests.SerialCorrelation outcome = Guarded(
            "dwtest", () => LinearModelTests.DurbinWatson(residuals, design, exact, tail), line, col);

        return Outputs(wanted, JgsValue.Number(outcome.P), JgsValue.Number(outcome.D));
    }

    /// <summary>
    /// <c>[p, F, r] = linhyptest(beta, COVB, c, H, dfe)</c>: whether a stated linear combination of a
    /// model's coefficients takes a stated value.
    /// </summary>
    private static JgsValue[] LinearRestrictionTest(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("linhyptest", args, 2, 5, line, col);
        double[] beta = ToDoubles("linhyptest", args[0], line, col);
        int terms = beta.Length;

        (double[] flat, int rows, int columns) = DenseMatrix("linhyptest", args[1], line, col);
        if (rows != terms || columns != terms)
        {
            throw new JgsRuntimeException(line, col,
                $"linhyptest: the covariance is {rows}-by-{columns} for {terms} coefficients.");
        }

        var covariance = Square(flat, terms);

        double[,] h;
        if (args.Count > 3 && !IsPlaceholderValue(args[3]))
        {
            h = AsRectangle("linhyptest", args[3], line, col);
            if (h.GetLength(1) != terms)
            {
                throw new JgsRuntimeException(line, col,
                    $"linhyptest: each restriction must name all {terms} coefficients.");
            }
        }
        else
        {
            // No restriction matrix means every coefficient at once, which is the identity.
            h = new double[terms, terms];
            for (int i = 0; i < terms; i++)
            {
                h[i, i] = 1;
            }
        }

        int restrictions = h.GetLength(0);
        double[] c = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? ToDoubles("linhyptest", args[2], line, col)
            : new double[restrictions];

        double errorDf = args.Count > 4 && !IsPlaceholderValue(args[4])
            ? Num("linhyptest", args, 4, line, col)
            : double.PositiveInfinity;

        LinearModelTests.LinearHypothesis outcome = Guarded(
            "linhyptest", () => LinearModelTests.Linear(beta, covariance, c, h, errorDf), line, col);

        return Outputs(
            wanted,
            JgsValue.Number(outcome.P),
            JgsValue.Number(outcome.F),
            JgsValue.Number(outcome.Rank));
    }

    /// <summary>
    /// <c>sampsizepwr(testtype, p0, p1, power, n)</c>: whichever of the effect, the sample size and the
    /// power was left out, computed from the other two.
    /// </summary>
    private static JgsValue[] SampleSizeOrPower(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = SampleSizeOptions.Parse(args, 5, line, col);
        if (parsed.Positional.Count is < 3 or > 5)
        {
            throw new JgsRuntimeException(line, col,
                "sampsizepwr(testtype, p0, p1, power) or sampsizepwr(testtype, p0, p1, [], n) "
                + "leaves out exactly one of the effect, the power and the sample size.");
        }

        if (parsed.Positional[0].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, "sampsizepwr: the first argument names the test.");
        }

        string word = parsed.Positional[0].AsString;
        SampleSize.TestKind kind = word.ToLowerInvariant() switch
        {
            "z" => SampleSize.TestKind.Z,
            "t" => SampleSize.TestKind.T,
            "t2" => SampleSize.TestKind.TwoSampleT,
            "var" => SampleSize.TestKind.Variance,
            "p" => SampleSize.TestKind.Proportion,
            _ => throw new JgsRuntimeException(line, col,
                $"sampsizepwr does not know the test '{word}' (expected 'z', 't', 't2', 'var' or 'p')."),
        };

        double[] parameters = ToDoubles("sampsizepwr", parsed.Positional[1], line, col);
        double alpha = Level(parsed);
        Tail tail = Direction(parsed);
        double ratio = parsed.Scalar("Ratio", 1);

        bool hasAlternative = parsed.Positional.Count > 2 && !IsPlaceholderValue(parsed.Positional[2]);
        bool hasPower = parsed.Positional.Count > 3 && !IsPlaceholderValue(parsed.Positional[3]);
        bool hasSize = parsed.Positional.Count > 4 && !IsPlaceholderValue(parsed.Positional[4]);

        double alternative = hasAlternative ? Num("sampsizepwr", parsed.Positional, 2, line, col) : double.NaN;
        double power = hasPower ? Num("sampsizepwr", parsed.Positional, 3, line, col) : double.NaN;
        double n = hasSize ? Num("sampsizepwr", parsed.Positional, 4, line, col) : double.NaN;

        double answer;
        bool sizeAnswered = false;
        if (!hasSize && hasPower && hasAlternative)
        {
            answer = Guarded(
                "sampsizepwr",
                () => SampleSize.SampleFor(kind, parameters, alternative, power, alpha, tail, ratio),
                line,
                col);
            sizeAnswered = true;
        }
        else if (!hasPower && hasSize && hasAlternative)
        {
            answer = Guarded(
                "sampsizepwr",
                () => SampleSize.Power(kind, parameters, alternative, n, alpha, tail, ratio),
                line,
                col);
        }
        else if (!hasAlternative && hasPower && hasSize)
        {
            answer = Guarded(
                "sampsizepwr",
                () => SampleSize.AlternativeFor(kind, parameters, power, n, alpha, tail, ratio),
                line,
                col);
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "sampsizepwr: exactly one of the alternative, the power and the sample size must be left out as [].");
        }

        if (!sizeAnswered || kind != SampleSize.TestKind.TwoSampleT || wanted < 2)
        {
            return Outputs(wanted, JgsValue.Number(answer));
        }

        // The two-sample form answers both sample sizes, because the second one is the first scaled by
        // the ratio and rounded, which the caller cannot recover exactly from the first.
        return [JgsValue.Number(answer), JgsValue.Number(Math.Round(answer * ratio))];
    }

    // --- Shared argument reading ---------------------------------------------------------------------

    /// <summary>The significance level, which every one of these names spells <c>'Alpha'</c>.</summary>
    private static double Level(ParsedArgs parsed) => parsed.Scalar("Alpha", 0.05);

    /// <summary>
    /// Which alternative is being tested. The words differ between names — the Kolmogorov–Smirnov
    /// tests say <c>'unequal'</c>, <c>'larger'</c> and <c>'smaller'</c> where the rest say
    /// <c>'both'</c>, <c>'right'</c> and <c>'left'</c> — so the accepted spellings are a parameter.
    /// </summary>
    private static Tail Direction(
        ParsedArgs parsed, string both = "both", string right = "right", string left = "left")
    {
        string word = parsed.Word("Tail", both, both, right, left);
        return word == right ? Tail.Right : word == left ? Tail.Left : Tail.Both;
    }

    /// <summary>The difference of two paired samples, kept in the shape they arrived in.</summary>
    private static JgsValue Difference(string name, JgsValue x, JgsValue y, int line, int col)
    {
        double[] first = FlattenColumnMajor(name, x, line, col);
        double[] second = FlattenColumnMajor(name, y, line, col);
        if (first.Length != second.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a paired test needs the two samples to be the same size ({first.Length} and {second.Length}).");
        }

        var differences = new double[first.Length];
        for (int i = 0; i < first.Length; i++)
        {
            differences[i] = first[i] - second[i];
        }

        return KeepingShape(x, differences);
    }

    /// <summary>The differences a paired rank test works on, from a second sample or a single number.</summary>
    private static double[] Differences(
        string name, double[] first, double[] second, IReadOnlyList<JgsValue> positional, int line, int col)
    {
        if (positional.Count == 1)
        {
            return first;
        }

        if (second.Length == 1)
        {
            var shifted = new double[first.Length];
            for (int i = 0; i < first.Length; i++)
            {
                shifted[i] = first[i] - second[0];
            }

            return shifted;
        }

        if (first.Length != second.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a paired test needs the two samples to be the same length "
                + $"({first.Length} and {second.Length}).");
        }

        var differences = new double[first.Length];
        for (int i = 0; i < first.Length; i++)
        {
            differences[i] = first[i] - second[i];
        }

        return differences;
    }

    /// <summary>
    /// The groups a several-sample test compares: the columns of a matrix, or a vector cut up by a
    /// grouping variable.
    /// </summary>
    private static (double[][] Groups, string[] Names) Grouped(
        string name, IReadOnlyList<JgsValue> positional, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, positional[0], line, col);

        if (positional.Count == 1 || IsPlaceholderValue(positional[1]))
        {
            if (rows == 1 && columns > 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a single row is one group; give a matrix whose columns are the groups, "
                    + "or a grouping variable.");
            }

            var columnwise = new double[columns][];
            var labels = new string[columns];
            for (int c = 0; c < columns; c++)
            {
                columnwise[c] = new double[rows];
                Array.Copy(flat, c * rows, columnwise[c], 0, rows);
                labels[c] = (c + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return (columnwise, labels);
        }

        (int[] index, string[] names) = GroupIndex(name, positional[1], line, col);
        if (index.Length != flat.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the grouping variable has {index.Length} entries but the data has {flat.Length}.");
        }

        var buckets = new List<double>[names.Length];
        for (int g = 0; g < names.Length; g++)
        {
            buckets[g] = [];
        }

        for (int i = 0; i < flat.Length; i++)
        {
            if (index[i] >= 0)
            {
                buckets[index[i]].Add(flat[i]);
            }
        }

        var grouped = new double[names.Length][];
        for (int g = 0; g < names.Length; g++)
        {
            grouped[g] = [.. buckets[g]];
        }

        return (grouped, names);
    }

    /// <summary>
    /// The hypothesized distribution function of a Kolmogorov–Smirnov test: the standard normal by
    /// default, a function of the caller's own, or a two-column table read by interpolation.
    /// </summary>
    private static Func<double[], double[]> HypothesizedCdf(JgsValue? given, int line, int col)
    {
        if (given is not { } value)
        {
            return points =>
            {
                var probabilities = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    probabilities[i] = ContinuousDistributions.NormalCdf(points[i], 0, 1);
                }

                return probabilities;
            };
        }

        if (value.Type == JgsType.Function)
        {
            IJgsCallable callable = value.AsCallable;
            return points =>
            {
                var probabilities = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    JgsValue answered = callable.Call([JgsValue.Number(points[i])], line, col);
                    probabilities[i] = answered.Type == JgsType.Number
                        ? answered.AsNumber
                        : throw new JgsRuntimeException(line, col,
                            "kstest: the distribution function must answer one number per point.");
                }

                return probabilities;
            };
        }

        (double[] flat, int rows, int columns) = DenseMatrix("kstest", value, line, col);
        if (columns != 2 || rows < 2)
        {
            throw new JgsRuntimeException(line, col,
                "kstest: 'CDF' takes a two-column matrix of points and their probabilities, or a function handle.");
        }

        var at = new double[rows];
        var probability = new double[rows];
        Array.Copy(flat, 0, at, 0, rows);
        Array.Copy(flat, rows, probability, 0, rows);
        Array.Sort(at, probability);

        return points =>
        {
            var answered = new double[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                answered[i] = Interpolated(at, probability, points[i]);
            }

            return answered;
        };
    }

    private static double Interpolated(double[] at, double[] value, double point)
    {
        if (point <= at[0])
        {
            return value[0];
        }

        if (point >= at[^1])
        {
            return value[^1];
        }

        int low = 0;
        int high = at.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (at[middle] <= point)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        double span = at[high] - at[low];
        return span > 0
            ? value[low] + ((point - at[low]) / span * (value[high] - value[low]))
            : value[low];
    }

    /// <summary>
    /// The distribution a binned test compares against, and how many of its parameters were estimated
    /// from the data — which is what the degrees of freedom lose.
    /// </summary>
    private static (Func<double[], double[]> Cdf, int Estimated) BinnedCdf(
        ParsedArgs parsed, double[] values, int line, int col)
    {
        JgsValue? given = parsed.Named("CDF");
        if (given is null)
        {
            // The default is a normal fitted to the data, so two parameters come out of the degrees of
            // freedom unless the caller says otherwise.
            double mean = DescriptiveStatistics.Mean(values);
            double sd = DescriptiveStatistics.StandardDeviation(values, population: false);
            return (points =>
            {
                var probabilities = new double[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    probabilities[i] = ContinuousDistributions.NormalCdf(points[i], mean, sd);
                }

                return probabilities;
            }, 2);
        }

        IJgsCallable callable;
        var extra = new List<JgsValue>();
        int estimated;
        if (given.Type == JgsType.Cell)
        {
            // A cell means the function's parameters were estimated too, and MathWorks counts them.
            JgsValue[] cell = given.AsCell;
            if (cell.Length == 0 || cell[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col,
                    "chi2gof: a cell 'CDF' holds the function handle followed by its parameters.");
            }

            callable = cell[0].AsCallable;
            for (int i = 1; i < cell.Length; i++)
            {
                extra.Add(cell[i]);
            }

            estimated = extra.Count;
        }
        else if (given.Type == JgsType.Function)
        {
            callable = given.AsCallable;
            estimated = 0;
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "chi2gof: 'CDF' takes a function handle, or a cell holding one and its estimated parameters.");
        }

        return (points =>
        {
            var probabilities = new double[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var call = new List<JgsValue>(extra.Count + 1) { JgsValue.Number(points[i]) };
                call.AddRange(extra);
                JgsValue answered = callable.Call(call, line, col);
                probabilities[i] = answered.Type == JgsType.Number
                    ? answered.AsNumber
                    : throw new JgsRuntimeException(line, col,
                        "chi2gof: the distribution function must answer one number per point.");
            }

            return probabilities;
        }, estimated);
    }

    /// <summary>The bin edges a binned test uses, from whichever of the three ways of saying it was given.</summary>
    private static double[] BinEdges(ParsedArgs parsed, double[] values, int line, int col)
    {
        if (parsed.Named("Edges") is not null)
        {
            double[] edges = parsed.Vector("Edges")!;
            if (edges.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "chi2gof: 'Edges' needs at least two edges.");
            }

            return edges;
        }

        if (parsed.Named("Ctrs") is not null)
        {
            double[] centres = parsed.Vector("Ctrs")!;
            if (centres.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "chi2gof: 'Ctrs' needs at least two bin centres.");
            }

            // Centres name the middles; the edges are the midpoints between them, with the outer two
            // pushed out to infinity so that everything falls somewhere.
            var edges = new double[centres.Length + 1];
            edges[0] = double.NegativeInfinity;
            edges[^1] = double.PositiveInfinity;
            for (int i = 1; i < centres.Length; i++)
            {
                edges[i] = (centres[i - 1] + centres[i]) / 2;
            }

            return edges;
        }

        int bins = parsed.Whole("NBins", 10);
        if (bins < 2)
        {
            throw new JgsRuntimeException(line, col, "chi2gof: there must be at least two bins.");
        }

        double lowest = double.PositiveInfinity;
        double highest = double.NegativeInfinity;
        foreach (double value in values)
        {
            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
        }

        if (!double.IsFinite(lowest) || !double.IsFinite(highest) || lowest == highest)
        {
            throw new JgsRuntimeException(line, col,
                "chi2gof: the data has no spread to divide into bins.");
        }

        var made = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            made[i] = lowest + ((highest - lowest) * i / bins);
        }

        made[0] = double.NegativeInfinity;
        made[^1] = double.PositiveInfinity;
        return made;
    }

    private static double[] CountsIn(double[] edges, double[] values, double[] weights)
    {
        var counts = new double[edges.Length - 1];
        for (int i = 0; i < values.Length; i++)
        {
            for (int b = 0; b < counts.Length; b++)
            {
                bool inside = b == counts.Length - 1
                    ? values[i] >= edges[b] && values[i] <= edges[b + 1]
                    : values[i] >= edges[b] && values[i] < edges[b + 1];
                if (inside)
                {
                    counts[b] += weights[i];
                    break;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// The observations and how many times each is counted. A frequency vector turns a list of distinct
    /// values into a sample of any size without writing it out.
    /// </summary>
    private static (double[] Values, double[] Weights) Weighted(
        string name, double[] data, double[]? frequency, int line, int col)
    {
        if (frequency is null)
        {
            var ones = new double[data.Length];
            Array.Fill(ones, 1);
            return (data, ones);
        }

        if (frequency.Length != data.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {frequency.Length} frequencies for {data.Length} values.");
        }

        return (data, frequency);
    }

    // --- Shaping ------------------------------------------------------------------------------------

    /// <summary>What one slice's test answered: its probability, its interval, and its statistics.</summary>
    private delegate (double P, double[] Interval, (string Name, double[] Values)[] Stats) SliceTest(
        double[] slice);

    /// <summary>What one slice of a two-sample test answered.</summary>
    private delegate (double P, double[] Interval, (string Name, double[] Values)[] Stats) PairSliceTest(
        double[] first, double[] second);

    /// <summary>
    /// Runs a test over every one-dimensional slice of the data and gives each output the shape that
    /// implies: a scalar per slice for the decision and the probability, two values per slice for the
    /// interval, and a structure whose fields are shaped the same way.
    /// </summary>
    private static JgsValue[] ShapedTest(
        string name, JgsValue data, int? dim, int wanted, double alpha, int line, int col, SliceTest test)
    {
        (double[][] slices, int[] dims, int along) = CutSamples(name, data, dim, line, col);
        var answers = new (double P, double[] Interval, (string Name, double[] Values)[] Stats)[slices.Length];
        for (int i = 0; i < slices.Length; i++)
        {
            double[] slice = slices[i];
            answers[i] = Guarded(name, () => test(slice), line, col);
        }

        return Assembled(wanted, alpha, answers, dims, along);
    }

    private static JgsValue[] ShapedPairTest(
        string name,
        JgsValue first,
        JgsValue second,
        int? dim,
        int wanted,
        double alpha,
        int line,
        int col,
        PairSliceTest test)
    {
        (double[][] left, int[] dims, int along) = CutSamples(name, first, dim, line, col);
        (double[][] right, _, _) = CutSamples(name, second, dim, line, col);
        if (left.Length != right.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the two samples give {left.Length} and {right.Length} comparisons; "
                + "they must line up in every dimension but the one being tested.");
        }

        var answers = new (double P, double[] Interval, (string Name, double[] Values)[] Stats)[left.Length];
        for (int i = 0; i < left.Length; i++)
        {
            double[] a = left[i];
            double[] b = right[i];
            answers[i] = Guarded(name, () => test(a, b), line, col);
        }

        return Assembled(wanted, alpha, answers, dims, along);
    }

    private static JgsValue[] Assembled(
        int wanted,
        double alpha,
        (double P, double[] Interval, (string Name, double[] Values)[] Stats)[] answers,
        int[] dims,
        int along)
    {
        var decisions = new double[answers.Length][];
        var probabilities = new double[answers.Length][];
        var intervals = new double[answers.Length][];
        for (int i = 0; i < answers.Length; i++)
        {
            decisions[i] = [answers[i].P <= alpha ? 1 : 0];
            probabilities[i] = [answers[i].P];
            intervals[i] = answers[i].Interval;
        }

        var fields = new List<(string Name, JgsValue Value)>();
        foreach ((string field, double[] _) in answers[0].Stats)
        {
            var perSlice = new double[answers.Length][];
            for (int i = 0; i < answers.Length; i++)
            {
                foreach ((string other, double[] values) in answers[i].Stats)
                {
                    if (other == field)
                    {
                        perSlice[i] = values;
                        break;
                    }
                }
            }

            fields.Add((field, Scattered(perSlice, dims, along)));
        }

        // The decision is a logical, so it is built out of logical elements rather than scattered as
        // numbers — a matrix of ones and zeros would answer 'double' to class().
        JgsValue h;
        if (answers.Length == 1)
        {
            h = JgsValue.Bool(decisions[0][0] != 0);
        }
        else
        {
            (double[] joined, int[] shape) = JgsMatrix.JoinAlong(decisions, dims, along);
            var flags = new JgsValue[joined.Length];
            for (int i = 0; i < joined.Length; i++)
            {
                flags[i] = JgsValue.Bool(joined[i] != 0);
            }

            h = JgsValue.Array(flags);
            h.ReshapeDims(shape);
        }

        return Outputs(
            wanted,
            h,
            Scattered(probabilities, dims, along),
            Scattered(intervals, dims, along),
            Structure([.. fields]));
    }

    /// <summary>
    /// The slices a test runs over: one for a vector, and one per column (or per row, or along whatever
    /// dimension was named) for anything larger.
    /// </summary>
    private static (double[][] Slices, int[] Dims, int Along) CutSamples(
        string name, JgsValue data, int? dim, int line, int col)
    {
        double[] flat = FlattenColumnMajor(name, data, line, col);
        int[] dims = JgsMatrix.DimsOf(data);
        long counted = 1;
        foreach (int size in dims)
        {
            counted *= size;
        }

        if (counted != flat.Length)
        {
            dims = [1, flat.Length];
        }

        if (dim is null && !IsMatrix(data))
        {
            // A vector is one sample however it is oriented, which is what every other statistic here
            // reads it as; only an explicit dimension makes a row into many samples of one.
            return ([flat], [1, 1], 1);
        }

        int along = dim ?? JgsMatrix.DefaultDim(dims);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, along);
        return (slices, dims, along);
    }

    /// <summary>The per-slice answers put back where their slices came from.</summary>
    private static JgsValue Scattered(double[][] perSlice, int[] dims, int along)
    {
        if (perSlice.Length == 1)
        {
            return perSlice[0].Length == 1
                ? JgsValue.Number(perSlice[0][0])
                : JgsMatrix.FromColumnMajor(perSlice[0], perSlice[0].Length, 1);
        }

        int width = perSlice[0].Length;
        foreach (double[] values in perSlice)
        {
            if (values.Length != width)
            {
                // Welch's test reports one standard deviation per sample and the pooled test one
                // altogether; a column that answered a different count than its neighbours has no
                // shape to be scattered into, so it is laid out as a plain row instead.
                return RowVector(Flattened(perSlice));
            }
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(perSlice, dims, along);
        return joined.Length == 1
            ? JgsValue.Number(joined[0])
            : JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    private static double[] Flattened(double[][] perSlice)
    {
        var all = new List<double>();
        foreach (double[] values in perSlice)
        {
            all.AddRange(values);
        }

        return [.. all];
    }

    /// <summary>
    /// A numeric value read as a rectangle of rows and columns, through the same dimension reading the
    /// rest of the statistics surface uses. The imaging surface has a same-shaped helper of its own, but
    /// it reads an unshaped list as one row, and a design matrix handed in as a plain list of numbers is
    /// the one case where that is the wrong reading.
    /// </summary>
    private static double[,] AsRectangle(string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);
        var rectangle = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                rectangle[r, c] = flat[r + (c * rows)];
            }
        }

        return rectangle;
    }

    /// <summary>A row of numbers, collapsed to a single one when that is all there is.</summary>
    private static JgsValue RowVector(double[] values) => values.Length switch
    {
        0 => JgsValue.Array([]),
        1 => JgsValue.Number(values[0]),
        _ => JgsMatrix.FromColumnMajor(values, 1, values.Length),
    };

    /// <summary>A struct built from named fields, in the order they were given.</summary>
    private static JgsValue Structure(params (string Name, JgsValue Value)[] fields)
    {
        var map = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string name, JgsValue value) in fields)
        {
            map[name] = value;
        }

        return JgsValue.Struct(map);
    }
}
