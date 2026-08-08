using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// The tests of whether a sample came from a distribution: the two Kolmogorov–Smirnov tests,
/// Lilliefors' and Anderson–Darling's composite versions, the skewness-and-kurtosis test, the
/// binned chi-square test, and the test of randomness by runs.
/// </summary>
/// <remarks>
/// Two of these have exact null distributions and use them. The rest do not, and their published
/// p-values come from simulation: Lilliefors' and Anderson–Darling's statistics have a different
/// distribution once the parameters are estimated from the same sample, with no closed form at all.
/// Where that is so, the probability here is read off a table of published critical values rather than
/// re-simulated, so it is clamped to the range the table covers and says so instead of extrapolating a
/// tail off five points.
/// </remarks>
public static class GoodnessOfFit
{
    /// <summary>The outcome of a distributional test: the statistic, its probability, and a critical value.</summary>
    public readonly record struct FitTest(double Statistic, double P, double Critical);

    /// <summary>The outcome of a binned chi-square test, which reports what it binned.</summary>
    public readonly record struct BinnedTest(
        double Statistic, double Df, double P, double[] Edges, double[] Observed, double[] Expected);

    /// <summary>The outcome of a test of randomness by runs.</summary>
    public readonly record struct RunTest(double P, int Runs, int Above, int Below, double Z);

    // Stephens' modified statistics have a null distribution that no longer depends on the sample
    // size, which is what lets one five-row table serve every n. The rows are the published upper-tail
    // critical values, from the most ordinary significance level to the least.
    private static readonly CriticalValueTable LillieforsNormal =
        new([0.15, 0.10, 0.05, 0.025, 0.01], [0.775, 0.819, 0.895, 0.955, 1.035]);

    private static readonly CriticalValueTable LillieforsExponential =
        new([0.15, 0.10, 0.05, 0.025, 0.01], [0.926, 0.995, 1.094, 1.184, 1.298]);

    private static readonly CriticalValueTable LillieforsExtremeValue =
        new([0.25, 0.10, 0.05, 0.025, 0.01], [0.660, 0.760, 0.819, 0.880, 0.944]);

    private static readonly CriticalValueTable AndersonExponential =
        new([0.15, 0.10, 0.05, 0.025, 0.01], [1.062, 1.321, 1.591, 1.959, 2.422]);

    private static readonly CriticalValueTable AndersonExtremeValue =
        new([0.25, 0.10, 0.05, 0.025, 0.01], [0.474, 0.637, 0.757, 0.877, 1.038]);

    /// <summary>Which distribution a composite goodness-of-fit test fits before testing.</summary>
    public enum FittedFamily
    {
        /// <summary>The normal, with both parameters estimated.</summary>
        Normal,

        /// <summary>The exponential, with its mean estimated.</summary>
        Exponential,

        /// <summary>The type 1 extreme value distribution, with both parameters estimated.</summary>
        ExtremeValue,

        /// <summary>The lognormal — the normal test applied to the logarithms.</summary>
        Lognormal,

        /// <summary>The Weibull — the extreme value test applied to the logarithms.</summary>
        Weibull,
    }

    // --- Kolmogorov–Smirnov ---------------------------------------------------------------------------

    /// <summary>
    /// <c>kstest</c>: the largest gap between a sample's empirical distribution function and a fully
    /// specified one. The distribution's parameters must not have come from this sample — if they did,
    /// the null distribution is Lilliefors' and not this one.
    /// </summary>
    /// <param name="x">The sample.</param>
    /// <param name="cdf">The hypothesized distribution function, evaluated at the sorted sample.</param>
    /// <param name="alpha">The level the critical value is reported at.</param>
    /// <param name="tail">
    /// Which one-sided departure to look for: <see cref="Tail.Right"/> where the empirical function may
    /// lie above the hypothesized one, and <see cref="Tail.Left"/> where it may lie below.
    /// </param>
    public static FitTest KolmogorovSmirnov(
        IReadOnlyList<double> x, Func<double[], double[]> cdf, double alpha, Tail tail)
    {
        ArgumentNullException.ThrowIfNull(cdf);
        double[] sample = TestSupport.Clean(x);
        int n = sample.Length;
        if (n < 1)
        {
            throw new ArgumentException("a Kolmogorov–Smirnov test needs at least one observation.");
        }

        Array.Sort(sample);
        double[] hypothesized = cdf(sample);
        if (hypothesized.Length != n)
        {
            throw new ArgumentException("the hypothesized distribution function must answer one value per observation.");
        }

        double above = 0;
        double below = 0;
        for (int i = 0; i < n; i++)
        {
            double value = hypothesized[i];
            if (double.IsNaN(value))
            {
                throw new ArgumentException("the hypothesized distribution function answered a value that is not a number.");
            }

            above = Math.Max(above, ((i + 1.0) / n) - value);
            below = Math.Max(below, value - ((double)i / n));
        }

        double statistic = tail switch
        {
            Tail.Right => above,
            Tail.Left => below,
            _ => Math.Max(above, below),
        };

        double p = tail == Tail.Both ? TwoSidedKs(statistic, n) : Math.Exp(-2.0 * n * statistic * statistic);
        double critical = CriticalKs(alpha, n, tail == Tail.Both);
        return new FitTest(statistic, Math.Clamp(p, 0, 1), critical);
    }

    /// <summary>
    /// <c>kstest2</c>: the largest gap between two samples' empirical distribution functions, which
    /// asks whether they came from the same distribution without naming one.
    /// </summary>
    public static FitTest TwoSampleKolmogorovSmirnov(
        IReadOnlyList<double> x, IReadOnlyList<double> y, double alpha, Tail tail)
    {
        double[] first = TestSupport.Clean(x);
        double[] second = TestSupport.Clean(y);
        if (first.Length < 1 || second.Length < 1)
        {
            throw new ArgumentException("a two-sample Kolmogorov–Smirnov test needs observations in both samples.");
        }

        Array.Sort(first);
        Array.Sort(second);

        double above = 0;
        double below = 0;
        int i = 0;
        int j = 0;
        while (i < first.Length && j < second.Length)
        {
            double point = Math.Min(first[i], second[j]);
            while (i < first.Length && first[i] <= point)
            {
                i++;
            }

            while (j < second.Length && second[j] <= point)
            {
                j++;
            }

            double gap = ((double)i / first.Length) - ((double)j / second.Length);
            above = Math.Max(above, gap);
            below = Math.Max(below, -gap);
        }

        double statistic = tail switch
        {
            Tail.Right => above,
            Tail.Left => below,
            _ => Math.Max(above, below),
        };

        // The effective sample size of a two-sample comparison is the harmonic-style combination that
        // makes its statistic behave like a one-sample statistic of that many observations.
        double effective = (double)first.Length * second.Length / (first.Length + second.Length);
        double p;
        if (tail == Tail.Both)
        {
            double lambda = Math.Max(
                0, ((Math.Sqrt(effective) + 0.12 + (0.11 / Math.Sqrt(effective))) * statistic));
            p = TwoSidedKsFromLambda(lambda);
        }
        else
        {
            p = Math.Exp(-2 * effective * statistic * statistic);
        }

        double critical = CriticalKs(alpha, (int)Math.Round(effective), tail == Tail.Both);
        return new FitTest(statistic, Math.Clamp(p, 0, 1), critical);
    }

    private static double TwoSidedKs(double statistic, int n) =>
        TwoSidedKsFromLambda(Math.Sqrt(n) * statistic);

    private static double TwoSidedKsFromLambda(double lambda)
    {
        if (lambda <= 0)
        {
            return 1;
        }

        // The alternating series for the limiting Kolmogorov distribution. It converges geometrically
        // in λ², so a hundred terms is far past machine precision for every λ worth evaluating.
        double sum = 0;
        for (int j = 1; j <= 101; j++)
        {
            double term = Math.Exp(-2.0 * lambda * lambda * j * j);
            sum += (j % 2 == 1 ? 1 : -1) * term;
            if (term < 1e-18)
            {
                break;
            }
        }

        return Math.Clamp(2 * sum, 0, 1);
    }

    private static double CriticalKs(double alpha, int n, bool twoSided)
    {
        if (n < 1)
        {
            return double.NaN;
        }

        // Invert the same series the probability came from, so the critical value and the p-value
        // agree with each other at the level: a statistic exactly at the critical value has p = α.
        double low = 0;
        double high = 2;
        for (int i = 0; i < 200; i++)
        {
            double middle = (low + high) / 2;
            double p = twoSided
                ? TwoSidedKs(middle, n)
                : Math.Exp(-2.0 * n * middle * middle);
            if (p > alpha)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    // --- Composite fits ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>lillietest</c>: the Kolmogorov–Smirnov statistic where the distribution's parameters were
    /// estimated from the same sample, which makes the gap smaller than it would otherwise be and so
    /// needs its own null distribution.
    /// </summary>
    public static FitTest Lilliefors(IReadOnlyList<double> x, FittedFamily family, double alpha)
    {
        double[] sample = Transformed(TestSupport.Clean(x), family, out FittedFamily reduced);
        int n = sample.Length;
        if (n < 4)
        {
            throw new ArgumentException("Lilliefors' test needs at least four observations.");
        }

        Array.Sort(sample);
        double[] fitted = FittedCdf(sample, reduced);

        double gap = 0;
        for (int i = 0; i < n; i++)
        {
            gap = Math.Max(gap, Math.Max(((i + 1.0) / n) - fitted[i], fitted[i] - ((double)i / n)));
        }

        // Stephens' modification multiplies the gap by a factor that depends only on the sample size,
        // which is what makes one table serve every n. The critical value is reported on the raw
        // statistic's scale, so it is the tabulated one divided back by the same factor.
        double root = Math.Sqrt(n);
        (double factor, double offset, CriticalValueTable table) = reduced switch
        {
            FittedFamily.Exponential => (root + 0.26 + (0.5 / root), 0.2 / n, LillieforsExponential),
            FittedFamily.ExtremeValue => (root + (0.5 / root), 0.0, LillieforsExtremeValue),
            _ => (root - 0.01 + (0.85 / root), 0.0, LillieforsNormal),
        };

        double modified = (gap - offset) * factor;
        return new FitTest(gap, table.Probability(modified), (table.Critical(alpha) / factor) + offset);
    }

    /// <summary>
    /// <c>adtest</c>: Anderson and Darling's statistic, which weights the tails of the distribution
    /// where Kolmogorov and Smirnov's weights the middle, so it notices a departure the other misses.
    /// </summary>
    public static FitTest AndersonDarling(IReadOnlyList<double> x, FittedFamily family, double alpha)
    {
        double[] sample = Transformed(TestSupport.Clean(x), family, out FittedFamily reduced);
        int n = sample.Length;
        if (n < 4)
        {
            throw new ArgumentException("Anderson and Darling's test needs at least four observations.");
        }

        Array.Sort(sample);
        double[] fitted = FittedCdf(sample, reduced);

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double low = Math.Clamp(fitted[i], 1e-300, 1 - 1e-16);
            double high = Math.Clamp(fitted[n - 1 - i], 1e-300, 1 - 1e-16);
            sum += ((2.0 * i) + 1) * (Math.Log(low) + Math.Log(1 - high));
        }

        double statistic = -n - (sum / n);
        double root = Math.Sqrt(n);

        switch (reduced)
        {
            case FittedFamily.Exponential:
            {
                double modified = statistic * (1 + (0.6 / n));
                return new FitTest(
                    statistic,
                    AndersonExponential.Probability(modified),
                    AndersonExponential.Critical(alpha) / (1 + (0.6 / n)));
            }

            case FittedFamily.ExtremeValue:
            {
                double modified = statistic * (1 + (0.2 / root));
                return new FitTest(
                    statistic,
                    AndersonExtremeValue.Probability(modified),
                    AndersonExtremeValue.Critical(alpha) / (1 + (0.2 / root)));
            }

            default:
            {
                // The normal case has a published closed-form p-value rather than a table — four
                // fitted pieces covering the whole range of the modified statistic, which is why it is
                // the one composite test here that does not clamp.
                double modified = statistic * (1 + (0.75 / n) + (2.25 / (double)n / n));
                double p = modified switch
                {
                    >= 0.6 => Math.Exp(1.2937 - (5.709 * modified) + (0.0186 * modified * modified)),
                    >= 0.34 => Math.Exp(0.9177 - (4.279 * modified) - (1.38 * modified * modified)),
                    > 0.2 => 1 - Math.Exp(-8.318 + (42.796 * modified) - (59.938 * modified * modified)),
                    _ => 1 - Math.Exp(-13.436 + (101.14 * modified) - (223.73 * modified * modified)),
                };

                return new FitTest(statistic, Math.Clamp(p, 0, 1), NormalAndersonCritical(alpha, n));
            }
        }
    }

    private static double NormalAndersonCritical(double alpha, int n)
    {
        double low = 0;
        double high = 20;
        for (int i = 0; i < 200; i++)
        {
            double middle = (low + high) / 2;
            double modified = middle * (1 + (0.75 / n) + (2.25 / (double)n / n));
            double p = modified switch
            {
                >= 0.6 => Math.Exp(1.2937 - (5.709 * modified) + (0.0186 * modified * modified)),
                >= 0.34 => Math.Exp(0.9177 - (4.279 * modified) - (1.38 * modified * modified)),
                > 0.2 => 1 - Math.Exp(-8.318 + (42.796 * modified) - (59.938 * modified * modified)),
                _ => 1 - Math.Exp(-13.436 + (101.14 * modified) - (223.73 * modified * modified)),
            };

            if (p > alpha)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// The sample a composite test really works on: the lognormal and the Weibull are the normal and
    /// the extreme value applied to logarithms, so they are handled by taking the logarithms rather
    /// than by two more sets of tables.
    /// </summary>
    private static double[] Transformed(double[] sample, FittedFamily family, out FittedFamily reduced)
    {
        switch (family)
        {
            case FittedFamily.Lognormal:
            case FittedFamily.Weibull:
                var logs = new double[sample.Length];
                for (int i = 0; i < sample.Length; i++)
                {
                    if (!(sample[i] > 0))
                    {
                        throw new ArgumentException(
                            "the lognormal and Weibull families are only defined for positive observations.");
                    }

                    logs[i] = Math.Log(sample[i]);
                }

                reduced = family == FittedFamily.Lognormal ? FittedFamily.Normal : FittedFamily.ExtremeValue;
                return logs;

            case FittedFamily.Exponential:
                foreach (double value in sample)
                {
                    if (value < 0)
                    {
                        throw new ArgumentException("the exponential family is only defined for non-negative observations.");
                    }
                }

                reduced = family;
                return sample;

            default:
                reduced = family;
                return sample;
        }
    }

    private static double[] FittedCdf(double[] sorted, FittedFamily family)
    {
        var fitted = new double[sorted.Length];
        switch (family)
        {
            case FittedFamily.Exponential:
            {
                double mean = DescriptiveStatistics.Mean(sorted);
                for (int i = 0; i < sorted.Length; i++)
                {
                    fitted[i] = ContinuousDistributions.ExponentialCdf(sorted[i], mean);
                }

                return fitted;
            }

            case FittedFamily.ExtremeValue:
            {
                // The extreme value family has no closed-form estimate, so it goes through the same
                // maximum-likelihood fitter evfit uses rather than a second implementation of it.
                DistributionFamily gumbel = ContinuousFamilies.Find("ev")
                    ?? throw new InvalidOperationException("the extreme value family is not registered.");
                double[] parameters = DistributionFitting
                    .Fit(gumbel, DistributionFitting.MakeSample(sorted, null, null), 0.05).Parameters;
                for (int i = 0; i < sorted.Length; i++)
                {
                    fitted[i] = ContinuousDistributions.ExtremeValueCdf(sorted[i], parameters[0], parameters[1]);
                }

                return fitted;
            }

            default:
            {
                double mean = DescriptiveStatistics.Mean(sorted);
                double sd = DescriptiveStatistics.StandardDeviation(sorted, population: false);
                for (int i = 0; i < sorted.Length; i++)
                {
                    fitted[i] = ContinuousDistributions.NormalCdf(sorted[i], mean, sd);
                }

                return fitted;
            }
        }
    }

    /// <summary>
    /// <c>jbtest</c>: whether a sample's skewness and kurtosis are the normal distribution's. The two
    /// departures are squared and added, so the statistic is a chi-square with two degrees of freedom
    /// however large the sample.
    /// </summary>
    public static FitTest JarqueBera(IReadOnlyList<double> x, double alpha)
    {
        double[] sample = TestSupport.Clean(x);
        int n = sample.Length;
        if (n < 4)
        {
            throw new ArgumentException("the Jarque–Bera test needs at least four observations.");
        }

        double skewness = DescriptiveStatistics.Skewness(sample, bias: true);
        double kurtosis = DescriptiveStatistics.Kurtosis(sample, bias: true);
        double statistic = n / 6.0 * ((skewness * skewness) + ((kurtosis - 3) * (kurtosis - 3) / 4));
        return new FitTest(
            statistic,
            1 - ContinuousDistributions.Chi2Cdf(statistic, 2),
            ContinuousDistributions.Chi2Inv(1 - alpha, 2));
    }

    // --- Binned ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>chi2gof</c>: the sample put into bins and each bin's count compared with what the
    /// distribution says to expect. Bins whose expected count is below <paramref name="minimum"/> are
    /// merged into their neighbours from the ends inwards, because the chi-square approximation is what
    /// small expected counts break.
    /// </summary>
    /// <param name="edges">The bin edges, ascending; the first and last are the outer limits.</param>
    /// <param name="observed">How many observations fell in each bin.</param>
    /// <param name="expected">How many the hypothesized distribution says to expect in each.</param>
    /// <param name="estimated">How many parameters were estimated from this same sample.</param>
    /// <param name="minimum">The smallest expected count a bin may keep.</param>
    public static BinnedTest ChiSquareBins(
        double[] edges, double[] observed, double[] expected, int estimated, double minimum)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(expected);
        if (observed.Length != expected.Length || edges.Length != observed.Length + 1)
        {
            throw new ArgumentException("the bins, their counts and their expected counts must line up.");
        }

        var keptEdges = new List<double>(edges);
        var keptObserved = new List<double>(observed);
        var keptExpected = new List<double>(expected);

        // Pool from the low end upwards, then from the high end downwards. Merging a sparse bin into
        // its neighbour is the only pooling that keeps the bins contiguous, which is what lets the
        // edges still be reported.
        while (keptExpected.Count > 1 && keptExpected[0] < minimum)
        {
            keptExpected[1] += keptExpected[0];
            keptObserved[1] += keptObserved[0];
            keptExpected.RemoveAt(0);
            keptObserved.RemoveAt(0);
            keptEdges.RemoveAt(1);
        }

        while (keptExpected.Count > 1 && keptExpected[^1] < minimum)
        {
            keptExpected[^2] += keptExpected[^1];
            keptObserved[^2] += keptObserved[^1];
            keptExpected.RemoveAt(keptExpected.Count - 1);
            keptObserved.RemoveAt(keptObserved.Count - 1);
            keptEdges.RemoveAt(keptEdges.Count - 2);
        }

        double statistic = 0;
        for (int i = 0; i < keptExpected.Count; i++)
        {
            if (keptExpected[i] <= 0)
            {
                continue;
            }

            double gap = keptObserved[i] - keptExpected[i];
            statistic += gap * gap / keptExpected[i];
        }

        double df = keptExpected.Count - 1 - estimated;
        double p = df > 0 ? 1 - ContinuousDistributions.Chi2Cdf(statistic, df) : double.NaN;
        return new BinnedTest(statistic, df, p, [.. keptEdges], [.. keptObserved], [.. keptExpected]);
    }

    // --- Runs ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>runstest</c>: whether a sequence of values above and below a reference alternates the way an
    /// independent sequence would. Too few runs means the values cluster; too many means they alternate.
    /// </summary>
    /// <param name="above">Which observations lie above the reference; those exactly at it are dropped first.</param>
    /// <param name="exact">Whether to count the null distribution exactly rather than approximate it.</param>
    /// <param name="tail">Which departure to look for.</param>
    public static RunTest Runs(IReadOnlyList<bool> above, bool exact, Tail tail)
    {
        ArgumentNullException.ThrowIfNull(above);
        int n1 = 0;
        foreach (bool value in above)
        {
            if (value)
            {
                n1++;
            }
        }

        int n = above.Count;
        int n0 = n - n1;
        if (n1 == 0 || n0 == 0)
        {
            // Every observation on one side of the reference: there is one run and nothing to test.
            return new RunTest(1, n == 0 ? 0 : 1, n1, n0, double.NaN);
        }

        int runs = 1;
        for (int i = 1; i < n; i++)
        {
            if (above[i] != above[i - 1])
            {
                runs++;
            }
        }

        double mean = (2.0 * n1 * n0 / n) + 1;
        double variance = (mean - 1) * (mean - 2) / (n - 1);
        double z = variance > 0 ? (runs - mean) / Math.Sqrt(variance) : double.NaN;

        double p;
        if (exact)
        {
            double atMost = RunProbability(runs, n1, n0, atMost: true);
            double atLeast = RunProbability(runs, n1, n0, atMost: false);
            p = tail switch
            {
                Tail.Right => atLeast,
                Tail.Left => atMost,
                _ => Math.Min(1, 2 * Math.Min(atMost, atLeast)),
            };
        }
        else
        {
            // A continuity correction of one half, because the run count is a whole number and the
            // normal it is being compared with is not.
            double correction = tail switch
            {
                Tail.Right => -0.5,
                Tail.Left => 0.5,
                _ => runs > mean ? -0.5 : 0.5,
            };

            double corrected = variance > 0 ? (runs + correction - mean) / Math.Sqrt(variance) : double.NaN;
            p = TestSupport.NormalTail(corrected, tail);
        }

        return new RunTest(Math.Clamp(p, 0, 1), runs, n1, n0, z);
    }

    /// <summary>
    /// The probability of seeing at most (or at least) <paramref name="runs"/> runs, summed term by
    /// term over the exact distribution.
    /// </summary>
    private static double RunProbability(int runs, int n1, int n0, bool atMost)
    {
        double total = TestSupport.LogChoose(n1 + n0, n1);
        double sum = 0;
        int highest = (2 * Math.Min(n1, n0)) + (n1 == n0 ? 0 : 1);

        for (int r = atMost ? 2 : runs; atMost ? r <= runs : r <= highest; r++)
        {
            sum += Math.Exp(LogRunProbability(r, n1, n0) - total);
        }

        return Math.Clamp(sum, 0, 1);
    }

    private static double LogRunProbability(int r, int n1, int n0)
    {
        if (r % 2 == 0)
        {
            int k = r / 2;
            double log = Math.Log(2) + TestSupport.LogChoose(n1 - 1, k - 1) + TestSupport.LogChoose(n0 - 1, k - 1);
            return log;
        }

        int m = (r - 1) / 2;
        double left = Math.Exp(TestSupport.LogChoose(n1 - 1, m) + TestSupport.LogChoose(n0 - 1, m - 1));
        double right = Math.Exp(TestSupport.LogChoose(n1 - 1, m - 1) + TestSupport.LogChoose(n0 - 1, m));
        double combined = left + right;
        return combined > 0 ? Math.Log(combined) : double.NegativeInfinity;
    }
}
