using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// The tests of a mean and of a variance: Student's one- and two-sample t, the z test with a known
/// standard deviation, the chi-square and F tests of a variance, and the four ways of asking whether
/// several groups share one.
/// </summary>
/// <remarks>
/// Each returns the statistic, its degrees of freedom, the tail probability and a confidence interval
/// for the quantity being tested. The interval is one-sided when the alternative is, because an
/// alternative in one direction says nothing at all about the other: the unbounded end is reported as
/// an infinity rather than left out.
/// </remarks>
public static class ParametricTests
{
    /// <summary>The outcome of a test about a location: a statistic, its distribution, and an interval.</summary>
    /// <param name="Statistic">The test statistic.</param>
    /// <param name="Df">Its degrees of freedom, infinite where the null distribution is normal.</param>
    /// <param name="P">The tail probability.</param>
    /// <param name="Lower">The lower confidence limit for the quantity tested.</param>
    /// <param name="Upper">The upper confidence limit.</param>
    /// <param name="Spread">The standard deviations the statistic was scaled by.</param>
    public readonly record struct LocationTest(
        double Statistic, double Df, double P, double Lower, double Upper, double[] Spread);

    /// <summary>The outcome of a test about a variance.</summary>
    /// <param name="Statistic">The chi-square or F statistic.</param>
    /// <param name="Df">Its degrees of freedom — two of them for a ratio.</param>
    /// <param name="P">The tail probability.</param>
    /// <param name="Lower">The lower confidence limit for the variance, or for the ratio.</param>
    /// <param name="Upper">The upper confidence limit.</param>
    public readonly record struct SpreadTest(double Statistic, double[] Df, double P, double Lower, double Upper);

    /// <summary>
    /// <c>ttest(x, m)</c>: whether a normal sample's mean is <paramref name="mean"/>, with the standard
    /// deviation estimated from the same sample.
    /// </summary>
    public static LocationTest OneSampleT(IReadOnlyList<double> x, double mean, double alpha, Tail tail)
    {
        double[] sample = TestSupport.Clean(x);
        int n = sample.Length;
        if (n < 2)
        {
            throw new ArgumentException("a t test needs at least two observations.");
        }

        double average = DescriptiveStatistics.Mean(sample);
        double sd = DescriptiveStatistics.StandardDeviation(sample, population: false);
        double df = n - 1;
        double error = sd / Math.Sqrt(n);

        // A sample with no spread at all is a real possibility and its statistic is not a number, so
        // the ratio is formed rather than guarded: 0/0 is NaN and anything else is an infinity, both
        // of which the tail probabilities below carry through honestly.
        double statistic = (average - mean) / error;
        double p = TestSupport.StudentTail(statistic, df, tail);
        (double lower, double upper) = Interval(average, error, df, alpha, tail, StudentQuantile);
        return new LocationTest(statistic, df, p, lower, upper, [sd]);
    }

    /// <summary>
    /// <c>ttest(x, y)</c>: the one-sample test applied to the differences of paired observations.
    /// </summary>
    public static LocationTest PairedT(
        IReadOnlyList<double> x, IReadOnlyList<double> y, double mean, double alpha, Tail tail) =>
        OneSampleT(TestSupport.PairedDifferences(x, y), mean, alpha, tail);

    /// <summary>
    /// <c>ttest2(x, y)</c>: whether two independent normal samples have means differing by
    /// <paramref name="mean"/>, pooling their variances or — Welch's test — not.
    /// </summary>
    public static LocationTest TwoSampleT(
        IReadOnlyList<double> x, IReadOnlyList<double> y, double mean, double alpha, Tail tail, bool pooled)
    {
        double[] first = TestSupport.Clean(x);
        double[] second = TestSupport.Clean(y);
        if (first.Length < 2 || second.Length < 2)
        {
            throw new ArgumentException("a two-sample t test needs at least two observations in each sample.");
        }

        int n1 = first.Length;
        int n2 = second.Length;
        double s1 = DescriptiveStatistics.Variance(first, population: false);
        double s2 = DescriptiveStatistics.Variance(second, population: false);
        double difference = DescriptiveStatistics.Mean(first) - DescriptiveStatistics.Mean(second);

        double df;
        double error;
        double[] spread;
        if (pooled)
        {
            double pooledVariance = (((n1 - 1) * s1) + ((n2 - 1) * s2)) / (n1 + n2 - 2);
            df = n1 + n2 - 2;
            error = Math.Sqrt(pooledVariance * ((1.0 / n1) + (1.0 / n2)));
            spread = [Math.Sqrt(pooledVariance)];
        }
        else
        {
            double a = s1 / n1;
            double b = s2 / n2;
            error = Math.Sqrt(a + b);

            // Welch's degrees of freedom: the chi-square whose variance matches the weighted sum of
            // two. It is not a whole number and is not meant to be.
            df = (a + b) * (a + b) / ((a * a / (n1 - 1)) + (b * b / (n2 - 1)));
            spread = [Math.Sqrt(s1), Math.Sqrt(s2)];
        }

        double statistic = (difference - mean) / error;
        double p = TestSupport.StudentTail(statistic, df, tail);
        (double lower, double upper) = Interval(difference, error, df, alpha, tail, StudentQuantile);
        return new LocationTest(statistic, df, p, lower, upper, spread);
    }

    /// <summary>
    /// <c>ztest(x, m, sigma)</c>: the same question as the one-sample t test, asked where the standard
    /// deviation is known rather than estimated.
    /// </summary>
    public static LocationTest Z(IReadOnlyList<double> x, double mean, double sigma, double alpha, Tail tail)
    {
        double[] sample = TestSupport.Clean(x);
        if (sample.Length < 1)
        {
            throw new ArgumentException("a z test needs at least one observation.");
        }

        if (!(sigma > 0))
        {
            throw new ArgumentException("the known standard deviation must be above zero.");
        }

        double average = DescriptiveStatistics.Mean(sample);
        double error = sigma / Math.Sqrt(sample.Length);
        double statistic = (average - mean) / error;
        double p = TestSupport.NormalTail(statistic, tail);
        (double lower, double upper) = Interval(
            average, error, double.PositiveInfinity, alpha, tail, (probability, _) =>
                ContinuousDistributions.NormalInv(probability, 0, 1));
        return new LocationTest(statistic, double.PositiveInfinity, p, lower, upper, [sigma]);
    }

    /// <summary>
    /// <c>vartest(x, v)</c>: whether a normal sample's variance is <paramref name="variance"/>. The
    /// interval is for the variance itself and is never symmetric, because the chi-square is not.
    /// </summary>
    public static SpreadTest Variance(IReadOnlyList<double> x, double variance, double alpha, Tail tail)
    {
        double[] sample = TestSupport.Clean(x);
        int n = sample.Length;
        if (n < 2)
        {
            throw new ArgumentException("a variance test needs at least two observations.");
        }

        if (!(variance > 0))
        {
            throw new ArgumentException("the hypothesized variance must be above zero.");
        }

        double df = n - 1;
        double observed = DescriptiveStatistics.Variance(sample, population: false);
        double statistic = df * observed / variance;
        double cumulative = ContinuousDistributions.Chi2Cdf(statistic, df);
        double p = TestSupport.AsymmetricTail(cumulative, tail);

        double scaled = df * observed;
        (double lower, double upper) = tail switch
        {
            Tail.Right => (scaled / ContinuousDistributions.Chi2Inv(1 - alpha, df), double.PositiveInfinity),
            Tail.Left => (0, scaled / ContinuousDistributions.Chi2Inv(alpha, df)),
            _ => (scaled / ContinuousDistributions.Chi2Inv(1 - (alpha / 2), df),
                  scaled / ContinuousDistributions.Chi2Inv(alpha / 2, df)),
        };

        return new SpreadTest(statistic, [df], p, lower, upper);
    }

    /// <summary>
    /// <c>vartest2(x, y)</c>: whether two normal samples share a variance, through the ratio of the two
    /// estimates. The interval is for that ratio.
    /// </summary>
    public static SpreadTest TwoVariances(
        IReadOnlyList<double> x, IReadOnlyList<double> y, double alpha, Tail tail)
    {
        double[] first = TestSupport.Clean(x);
        double[] second = TestSupport.Clean(y);
        if (first.Length < 2 || second.Length < 2)
        {
            throw new ArgumentException("a two-sample variance test needs at least two observations in each sample.");
        }

        double df1 = first.Length - 1;
        double df2 = second.Length - 1;
        double ratio = DescriptiveStatistics.Variance(first, population: false)
            / DescriptiveStatistics.Variance(second, population: false);
        double cumulative = ContinuousDistributions.FCdf(ratio, df1, df2);
        double p = TestSupport.AsymmetricTail(cumulative, tail);

        (double lower, double upper) = tail switch
        {
            Tail.Right => (ratio / ContinuousDistributions.FInv(1 - alpha, df1, df2), double.PositiveInfinity),
            Tail.Left => (0, ratio / ContinuousDistributions.FInv(alpha, df1, df2)),
            _ => (ratio / ContinuousDistributions.FInv(1 - (alpha / 2), df1, df2),
                  ratio / ContinuousDistributions.FInv(alpha / 2, df1, df2)),
        };

        return new SpreadTest(ratio, [df1, df2], p, lower, upper);
    }

    /// <summary>How several groups are asked whether they share one variance.</summary>
    public enum SpreadComparison
    {
        /// <summary>Bartlett's likelihood-ratio test, which assumes normality and is sensitive to it.</summary>
        Bartlett,

        /// <summary>Levene's test on squared deviations from each group's mean.</summary>
        LeveneQuadratic,

        /// <summary>Levene's test on absolute deviations from each group's mean.</summary>
        LeveneAbsolute,

        /// <summary>Brown and Forsythe's test — absolute deviations from each group's median.</summary>
        BrownForsythe,

        /// <summary>O'Brien's test, which interpolates between Levene's and Bartlett's.</summary>
        OBrien,
    }

    /// <summary>
    /// <c>vartestn</c>: whether several groups share a variance. Bartlett's statistic is a chi-square;
    /// the other four are an analysis of variance of a transformed observation, so their statistic is
    /// an F with two degrees of freedom.
    /// </summary>
    public static SpreadTest SeveralVariances(IReadOnlyList<double[]> groups, SpreadComparison method)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var kept = new List<double[]>();
        foreach (double[] group in groups)
        {
            double[] clean = TestSupport.Clean(group);
            if (clean.Length > 1)
            {
                kept.Add(clean);
            }
        }

        if (kept.Count < 2)
        {
            throw new ArgumentException("comparing variances needs at least two groups of two or more observations.");
        }

        if (method == SpreadComparison.Bartlett)
        {
            int total = 0;
            double weighted = 0;
            double logs = 0;
            double reciprocals = 0;
            foreach (double[] group in kept)
            {
                double df = group.Length - 1;
                double variance = DescriptiveStatistics.Variance(group, population: false);
                total += group.Length;
                weighted += df * variance;
                logs += df * Math.Log(variance);
                reciprocals += 1 / df;
            }

            int k = kept.Count;
            double errorDf = total - k;
            double pooled = weighted / errorDf;
            double correction = 1 + ((reciprocals - (1 / errorDf)) / (3 * (k - 1)));
            double statistic = ((errorDf * Math.Log(pooled)) - logs) / correction;
            double df1 = k - 1;
            return new SpreadTest(
                statistic, [df1], 1 - ContinuousDistributions.Chi2Cdf(statistic, df1), double.NaN, double.NaN);
        }

        // The other four all replace each observation by a measure of how far it sits from its group's
        // centre and then ask whether those measures have the same mean, which is exactly a one-way
        // analysis of variance. Only the transformation differs.
        var transformed = new List<double[]>(kept.Count);
        foreach (double[] group in kept)
        {
            transformed.Add(Spread(group, method));
        }

        AnalysisOfVariance.OneWay oneWay = AnalysisOfVariance.OneWayFrom(transformed);
        return new SpreadTest(
            oneWay.F, [oneWay.BetweenDf, oneWay.WithinDf], oneWay.P, double.NaN, double.NaN);
    }

    private static double[] Spread(double[] group, SpreadComparison method)
    {
        double centre = method == SpreadComparison.BrownForsythe
            ? DescriptiveStatistics.Median(group)
            : DescriptiveStatistics.Mean(group);

        var measures = new double[group.Length];
        if (method == SpreadComparison.OBrien)
        {
            // O'Brien's transformation, with his recommended weight of one half: a score whose group
            // mean is the group's own variance, so an analysis of variance of the scores is a
            // comparison of variances that keeps Levene's robustness and Bartlett's power.
            const double weight = 0.5;
            int n = group.Length;
            double variance = DescriptiveStatistics.Variance(group, population: false);
            if (n < 3)
            {
                throw new ArgumentException("O'Brien's test needs at least three observations in each group.");
            }

            for (int i = 0; i < n; i++)
            {
                double gap = group[i] - centre;
                measures[i] = (((weight + n - 2) * n * gap * gap) - (weight * variance * (n - 1)))
                    / ((n - 1.0) * (n - 2.0));
            }

            return measures;
        }

        for (int i = 0; i < group.Length; i++)
        {
            double gap = group[i] - centre;
            measures[i] = method == SpreadComparison.LeveneQuadratic ? gap * gap : Math.Abs(gap);
        }

        return measures;
    }

    private static double StudentQuantile(double probability, double df) =>
        ContinuousDistributions.TInv(probability, df);

    /// <summary>
    /// The confidence interval around an estimate, one-sided when the alternative is. The unbounded
    /// end is an infinity rather than a missing value, because a one-sided test really does place no
    /// limit on the other side.
    /// </summary>
    private static (double Lower, double Upper) Interval(
        double estimate, double error, double df, double alpha, Tail tail, Func<double, double, double> quantile)
    {
        if (!(alpha > 0) || !(alpha < 1))
        {
            throw new ArgumentException("the significance level must lie strictly between 0 and 1.");
        }

        return tail switch
        {
            Tail.Right => (estimate - (quantile(1 - alpha, df) * error), double.PositiveInfinity),
            Tail.Left => (double.NegativeInfinity, estimate + (quantile(1 - alpha, df) * error)),
            _ => (estimate - (quantile(1 - (alpha / 2), df) * error),
                  estimate + (quantile(1 - (alpha / 2), df) * error)),
        };
    }
}
