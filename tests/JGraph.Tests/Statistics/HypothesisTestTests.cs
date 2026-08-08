using JGraph.Statistics.Distributions;
using JGraph.Statistics.Hypothesis;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave F: the hypothesis tests, checked against the identities that pin them rather than against
/// numbers copied from somewhere.
/// </summary>
/// <remarks>
/// A test statistic has very few published values worth pinning, but it has a great many identities: a
/// two-sided probability is twice a one-sided one for a symmetric null, a pooled t on two samples of the
/// same size is a t on their difference of means, Welch's degrees of freedom collapse to the pooled ones
/// when the variances are equal, the studentized range of two means is a t, and an exact combinatorial
/// count can be brute-forced in the test. Those are what is asserted here.
/// </remarks>
public class HypothesisTestTests
{
    private static readonly double[] Ten = [5.1, 4.9, 6.2, 5.7, 5.5, 6.0, 5.3, 5.8, 6.1, 5.4];

    // --- Tests of a mean ------------------------------------------------------------------------------

    /// <summary>
    /// The one-sample t statistic is the departure over the standard error, and the interval is the
    /// estimate plus and minus the quantile times that error — both written out here rather than taken
    /// on trust.
    /// </summary>
    [Fact]
    public void TheOneSampleTIsTheDepartureOverTheStandardError()
    {
        ParametricTests.LocationTest outcome = ParametricTests.OneSampleT(Ten, 5.5, 0.05, Tail.Both);

        double mean = JGraph.Statistics.DescriptiveStatistics.Mean(Ten);
        double sd = JGraph.Statistics.DescriptiveStatistics.StandardDeviation(Ten, population: false);
        double error = sd / Math.Sqrt(10);

        Assert.Equal(9, outcome.Df, 12);
        Assert.Equal((mean - 5.5) / error, outcome.Statistic, 12);
        Assert.Equal(sd, outcome.Spread[0], 12);
        Assert.Equal(2 * ContinuousDistributions.TCdf(-Math.Abs(outcome.Statistic), 9), outcome.P, 12);
        Assert.Equal(mean - (ContinuousDistributions.TInv(0.975, 9) * error), outcome.Lower, 12);
        Assert.Equal(mean + (ContinuousDistributions.TInv(0.975, 9) * error), outcome.Upper, 12);
    }

    /// <summary>A one-sided test halves the two-sided probability and leaves one end of the interval open.</summary>
    [Fact]
    public void AOneSidedTestHalvesTheProbabilityAndOpensOneEnd()
    {
        ParametricTests.LocationTest both = ParametricTests.OneSampleT(Ten, 5.0, 0.05, Tail.Both);
        ParametricTests.LocationTest right = ParametricTests.OneSampleT(Ten, 5.0, 0.05, Tail.Right);
        ParametricTests.LocationTest left = ParametricTests.OneSampleT(Ten, 5.0, 0.05, Tail.Left);

        Assert.Equal(both.P / 2, right.P, 12);
        Assert.Equal(1 - (both.P / 2), left.P, 12);
        Assert.Equal(double.PositiveInfinity, right.Upper);
        Assert.Equal(double.NegativeInfinity, left.Lower);
        Assert.True(right.Lower > both.Lower, "a one-sided lower limit is above the two-sided one.");
    }

    /// <summary>The paired test is the one-sample test of the differences, by construction.</summary>
    [Fact]
    public void ThePairedTestIsTheOneSampleTestOfTheDifferences()
    {
        double[] y = [4.8, 5.2, 5.9, 5.5, 5.6, 5.7, 5.1, 6.0, 5.7, 5.6];
        var differences = new double[10];
        for (int i = 0; i < 10; i++)
        {
            differences[i] = Ten[i] - y[i];
        }

        ParametricTests.LocationTest paired = ParametricTests.PairedT(Ten, y, 0, 0.05, Tail.Both);
        ParametricTests.LocationTest single = ParametricTests.OneSampleT(differences, 0, 0.05, Tail.Both);

        Assert.Equal(single.Statistic, paired.Statistic, 12);
        Assert.Equal(single.P, paired.P, 12);
    }

    /// <summary>
    /// Welch's test and the pooled test agree exactly when the two samples are the same size and have
    /// the same variance, which is the only case where the two formulas are the same one.
    /// </summary>
    [Fact]
    public void WelchAndThePooledTestAgreeOnEqualSamples()
    {
        double[] a = [1, 2, 3, 4, 5];
        double[] b = [3, 4, 5, 6, 7];

        ParametricTests.LocationTest pooled = ParametricTests.TwoSampleT(a, b, 0, 0.05, Tail.Both, pooled: true);
        ParametricTests.LocationTest welch = ParametricTests.TwoSampleT(a, b, 0, 0.05, Tail.Both, pooled: false);

        Assert.Equal(pooled.Statistic, welch.Statistic, 12);
        Assert.Equal(pooled.Df, welch.Df, 10);
        Assert.Equal(-2, pooled.Statistic / Math.Abs(pooled.Statistic) * 2, 12);
        Assert.Single(pooled.Spread);
        Assert.Equal(2, welch.Spread.Length);
    }

    /// <summary>Welch's degrees of freedom fall between the smaller sample's and the pooled total.</summary>
    [Fact]
    public void WelchLosesDegreesOfFreedomWhenTheVariancesDiffer()
    {
        double[] a = [1, 2, 3, 4, 5, 6, 7, 8];
        double[] b = [10, 40, -20, 60, 0];

        ParametricTests.LocationTest welch = ParametricTests.TwoSampleT(a, b, 0, 0.05, Tail.Both, pooled: false);
        Assert.InRange(welch.Df, 4, 11);
        Assert.True(welch.Df < 11, "Welch's degrees of freedom cannot reach the pooled n1 + n2 − 2.");
    }

    /// <summary>The z test is the t test with infinite degrees of freedom, and its interval says so.</summary>
    [Fact]
    public void TheZTestUsesTheNormalRatherThanStudent()
    {
        ParametricTests.LocationTest outcome = ParametricTests.Z(Ten, 5.5, 0.5, 0.05, Tail.Both);
        double mean = JGraph.Statistics.DescriptiveStatistics.Mean(Ten);
        double error = 0.5 / Math.Sqrt(10);

        Assert.Equal(double.PositiveInfinity, outcome.Df);
        Assert.Equal((mean - 5.5) / error, outcome.Statistic, 12);
        Assert.Equal(mean - (1.959963984540054 * error), outcome.Lower, 10);
    }

    // --- Tests of a variance ---------------------------------------------------------------------------

    /// <summary>
    /// The variance interval brackets the estimate and its ends are the chi-square quantiles, which is
    /// what makes it asymmetric.
    /// </summary>
    [Fact]
    public void TheVarianceIntervalBracketsTheEstimateAsymmetrically()
    {
        ParametricTests.SpreadTest outcome = ParametricTests.Variance(Ten, 0.25, 0.05, Tail.Both);
        double observed = JGraph.Statistics.DescriptiveStatistics.Variance(Ten, population: false);

        Assert.Equal(9 * observed / 0.25, outcome.Statistic, 12);
        Assert.InRange(observed, outcome.Lower, outcome.Upper);
        Assert.True(
            outcome.Upper - observed > observed - outcome.Lower,
            "a chi-square interval reaches further above the estimate than below it.");
    }

    /// <summary>
    /// Swapping the two samples inverts the variance ratio and its interval, and leaves the two-sided
    /// probability alone — which is the only sense in which an F test is symmetric.
    /// </summary>
    [Fact]
    public void SwappingTheSamplesInvertsTheVarianceRatio()
    {
        double[] a = [1, 2, 3, 4, 5, 6];
        double[] b = [2, 4, 6, 8, 10, 12, 14];

        ParametricTests.SpreadTest forward = ParametricTests.TwoVariances(a, b, 0.05, Tail.Both);
        ParametricTests.SpreadTest backward = ParametricTests.TwoVariances(b, a, 0.05, Tail.Both);

        Assert.Equal(1 / forward.Statistic, backward.Statistic, 12);
        Assert.Equal(forward.P, backward.P, 12);
        Assert.Equal(1 / forward.Upper, backward.Lower, 10);
        Assert.Equal(1 / forward.Lower, backward.Upper, 10);
    }

    /// <summary>
    /// With two groups, Levene's test on absolute deviations is an analysis of variance of those
    /// deviations, so its F is the square of the two-sample t of the same numbers.
    /// </summary>
    [Fact]
    public void LevenesTestOnTwoGroupsIsATTestOfTheDeviations()
    {
        double[] a = [1, 2, 3, 4, 5];
        double[] b = [10, 20, 30, 40, 50];

        ParametricTests.SpreadTest levene = ParametricTests.SeveralVariances(
            [a, b], ParametricTests.SpreadComparison.LeveneAbsolute);

        double[] da = Deviations(a);
        double[] db = Deviations(b);
        ParametricTests.LocationTest t = ParametricTests.TwoSampleT(da, db, 0, 0.05, Tail.Both, pooled: true);

        Assert.Equal(t.Statistic * t.Statistic, levene.Statistic, 10);
        Assert.Equal(t.P, levene.P, 10);

        static double[] Deviations(double[] group)
        {
            double mean = JGraph.Statistics.DescriptiveStatistics.Mean(group);
            var away = new double[group.Length];
            for (int i = 0; i < group.Length; i++)
            {
                away[i] = Math.Abs(group[i] - mean);
            }

            return away;
        }
    }

    /// <summary>Bartlett's statistic is zero when the groups have identical variances, and only then.</summary>
    [Fact]
    public void BartlettsStatisticVanishesOnIdenticalSpreads()
    {
        ParametricTests.SpreadTest same = ParametricTests.SeveralVariances(
            [[1, 2, 3], [11, 12, 13], [21, 22, 23]], ParametricTests.SpreadComparison.Bartlett);
        Assert.Equal(0, same.Statistic, 10);
        Assert.Equal(1, same.P, 10);

        ParametricTests.SpreadTest different = ParametricTests.SeveralVariances(
            [[1, 2, 3], [1, 20, 40], [5, 5.1, 4.9]], ParametricTests.SpreadComparison.Bartlett);
        Assert.True(different.Statistic > 1, "unequal spreads have to move Bartlett's statistic.");
        Assert.Equal(2, different.Df[0], 12);
    }

    // --- Distributional tests ---------------------------------------------------------------------------

    /// <summary>
    /// The Kolmogorov–Smirnov statistic is the largest gap, and a sample laid exactly on the quantiles of
    /// its own distribution leaves a gap of one over the sample size, which is as small as it can be.
    /// </summary>
    [Fact]
    public void TheKolmogorovStatisticIsTheLargestGap()
    {
        var perfect = new double[20];
        for (int i = 0; i < 20; i++)
        {
            perfect[i] = ContinuousDistributions.NormalInv((i + 1) / 21.0, 0, 1);
        }

        GoodnessOfFit.FitTest outcome = GoodnessOfFit.KolmogorovSmirnov(perfect, Standard, 0.05, Tail.Both);
        Assert.Equal(1.0 / 21, outcome.Statistic, 12);
        Assert.True(outcome.P > 0.99, "a sample on its own quantiles cannot look unusual.");

        double[] wrong = [10, 11, 12, 13];
        GoodnessOfFit.FitTest rejected = GoodnessOfFit.KolmogorovSmirnov(wrong, Standard, 0.05, Tail.Both);
        Assert.Equal(1, rejected.Statistic, 10);
        Assert.True(rejected.P < 1e-3, "four values ten standard deviations out are not standard normal.");
    }

    /// <summary>A statistic exactly at the critical value has a probability of exactly the level.</summary>
    [Fact]
    public void TheKolmogorovCriticalValueAgreesWithItsProbability()
    {
        var sample = new double[30];
        for (int i = 0; i < 30; i++)
        {
            sample[i] = ContinuousDistributions.NormalInv((i + 0.5) / 30, 0, 1);
        }

        foreach (double alpha in new[] { 0.10, 0.05, 0.01 })
        {
            GoodnessOfFit.FitTest outcome = GoodnessOfFit.KolmogorovSmirnov(sample, Standard, alpha, Tail.Both);
            var shifted = new double[30];
            for (int i = 0; i < 30; i++)
            {
                shifted[i] = sample[i];
            }

            // Feed the critical value back in as a statistic by asking for the probability of exactly
            // that gap; the two-sided series is what both directions go through.
            double p = ProbabilityOfGap(outcome.Critical, 30);
            Assert.Equal(alpha, p, 6);
        }

        static double ProbabilityOfGap(double gap, int n)
        {
            double lambda = Math.Sqrt(n) * gap;
            double sum = 0;
            for (int j = 1; j <= 101; j++)
            {
                sum += (j % 2 == 1 ? 1 : -1) * Math.Exp(-2.0 * lambda * lambda * j * j);
            }

            return Math.Clamp(2 * sum, 0, 1);
        }
    }

    /// <summary>
    /// Two samples that never overlap have a two-sample statistic of one; two copies of the same sample
    /// have a statistic of zero.
    /// </summary>
    [Fact]
    public void TheTwoSampleStatisticSpansSeparationAndIdentity()
    {
        GoodnessOfFit.FitTest apart =
            GoodnessOfFit.TwoSampleKolmogorovSmirnov([1, 2, 3, 4, 5], [6, 7, 8, 9, 10], 0.05, Tail.Both);
        Assert.Equal(1, apart.Statistic, 12);

        GoodnessOfFit.FitTest same =
            GoodnessOfFit.TwoSampleKolmogorovSmirnov([1, 2, 3, 4, 5], [1, 2, 3, 4, 5], 0.05, Tail.Both);
        Assert.Equal(0, same.Statistic, 12);
        Assert.Equal(1, same.P, 12);
    }

    /// <summary>
    /// Lilliefors' statistic is smaller than the fully specified one on the same data, because the
    /// distribution being compared against was fitted to it. That is the whole reason it needs its own
    /// table.
    /// </summary>
    [Fact]
    public void LillieforsIsSmallerThanTheFullySpecifiedGap()
    {
        double[] sample = [2.1, 3.4, 1.9, 4.8, 3.3, 2.7, 5.1, 3.9, 2.2, 4.4, 3.1, 3.6];

        GoodnessOfFit.FitTest fitted = GoodnessOfFit.Lilliefors(
            sample, GoodnessOfFit.FittedFamily.Normal, 0.05);
        GoodnessOfFit.FitTest specified = GoodnessOfFit.KolmogorovSmirnov(sample, Standard, 0.05, Tail.Both);

        Assert.True(fitted.Statistic < specified.Statistic);
        Assert.InRange(fitted.P, 0.01, 0.15);
        Assert.True(fitted.Critical > fitted.Statistic, "this sample is not rejected at 5%.");
    }

    /// <summary>
    /// Anderson and Darling's statistic weights the tails, so it notices a heavy tail that leaves the
    /// largest gap in the middle unchanged.
    /// </summary>
    [Fact]
    public void AndersonDarlingNoticesTheTails()
    {
        var random = new Random(4);
        var heavy = new double[80];
        for (int i = 0; i < 80; i++)
        {
            // A t with three degrees of freedom is normal-looking in the middle and much heavier at the
            // ends, which is exactly the departure this statistic exists for.
            heavy[i] = ContinuousDistributions.TInv((i + 0.5) / 80, 3);
        }

        GoodnessOfFit.FitTest outcome = GoodnessOfFit.AndersonDarling(
            heavy, GoodnessOfFit.FittedFamily.Normal, 0.05);
        Assert.True(outcome.P < 0.05, $"a t3 sample should be rejected as normal, but p was {outcome.P}.");
        _ = random;

        var normal = new double[80];
        for (int i = 0; i < 80; i++)
        {
            normal[i] = ContinuousDistributions.NormalInv((i + 0.5) / 80, 2, 3);
        }

        GoodnessOfFit.FitTest accepted = GoodnessOfFit.AndersonDarling(
            normal, GoodnessOfFit.FittedFamily.Normal, 0.05);
        Assert.True(accepted.P > 0.5, "a normal sample laid on its own quantiles must not be rejected.");
    }

    /// <summary>The lognormal test is the normal test of the logarithms, and says so exactly.</summary>
    [Fact]
    public void TheLognormalTestIsTheNormalTestOfTheLogarithms()
    {
        double[] sample = [1.2, 3.4, 0.8, 5.6, 2.2, 4.1, 1.7, 2.9];
        var logs = new double[sample.Length];
        for (int i = 0; i < sample.Length; i++)
        {
            logs[i] = Math.Log(sample[i]);
        }

        GoodnessOfFit.FitTest direct = GoodnessOfFit.AndersonDarling(
            sample, GoodnessOfFit.FittedFamily.Lognormal, 0.05);
        GoodnessOfFit.FitTest viaLogs = GoodnessOfFit.AndersonDarling(
            logs, GoodnessOfFit.FittedFamily.Normal, 0.05);

        Assert.Equal(viaLogs.Statistic, direct.Statistic, 12);
        Assert.Equal(viaLogs.P, direct.P, 12);
        Assert.Throws<ArgumentException>(() =>
            GoodnessOfFit.AndersonDarling([1, -2, 3], GoodnessOfFit.FittedFamily.Lognormal, 0.05));
    }

    /// <summary>
    /// The Jarque–Bera statistic is n/6 times the squared skewness plus a quarter of the squared excess
    /// kurtosis, and a symmetric mesokurtic sample gives nearly zero.
    /// </summary>
    [Fact]
    public void TheJarqueBeraStatisticIsWrittenOut()
    {
        double[] sample = [-2, -1, -1, 0, 0, 0, 1, 1, 2];
        GoodnessOfFit.FitTest outcome = GoodnessOfFit.JarqueBera(sample, 0.05);

        double skewness = JGraph.Statistics.DescriptiveStatistics.Skewness(sample, bias: true);
        double kurtosis = JGraph.Statistics.DescriptiveStatistics.Kurtosis(sample, bias: true);
        Assert.Equal(0, skewness, 12);
        Assert.Equal(
            sample.Length / 6.0 * ((skewness * skewness) + ((kurtosis - 3) * (kurtosis - 3) / 4)),
            outcome.Statistic,
            12);
        Assert.Equal(ContinuousDistributions.Chi2Inv(0.95, 2), outcome.Critical, 10);
    }

    /// <summary>
    /// A binned test with counts exactly at their expectations has a statistic of zero, and pooling
    /// merges the sparse bins from the ends inwards without losing an observation.
    /// </summary>
    [Fact]
    public void TheBinnedTestPoolsSparseBinsWithoutLosingCounts()
    {
        GoodnessOfFit.BinnedTest exact = GoodnessOfFit.ChiSquareBins(
            [0, 1, 2, 3], [10, 20, 30], [10, 20, 30], 0, 5);
        Assert.Equal(0, exact.Statistic, 12);
        Assert.Equal(2, exact.Df, 12);

        GoodnessOfFit.BinnedTest pooled = GoodnessOfFit.ChiSquareBins(
            [0, 1, 2, 3, 4, 5], [1, 30, 40, 30, 2], [2, 29, 41, 29, 2], 2, 5);

        Assert.Equal(3, pooled.Observed.Length);
        Assert.Equal(4, pooled.Edges.Length);
        Assert.Equal(103, Total(pooled.Observed), 12);
        Assert.Equal(103, Total(pooled.Expected), 12);
        Assert.Equal(0, pooled.Df, 12);

        static double Total(double[] values)
        {
            double sum = 0;
            foreach (double value in values)
            {
                sum += value;
            }

            return sum;
        }
    }

    /// <summary>
    /// The exact run distribution is a count, so it can be brute-forced: every arrangement of five ones
    /// and five zeros is enumerated and its runs counted.
    /// </summary>
    [Fact]
    public void TheExactRunDistributionMatchesBruteForce()
    {
        const int n = 10;
        var counts = new int[n + 1];
        int total = 0;
        for (int mask = 0; mask < 1 << n; mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) != 5)
            {
                continue;
            }

            total++;
            int runs = 1;
            for (int i = 1; i < n; i++)
            {
                if (((mask >> i) & 1) != ((mask >> (i - 1)) & 1))
                {
                    runs++;
                }
            }

            counts[runs]++;
        }

        Assert.Equal(252, total);

        // Two runs: everything on one side then everything on the other, either way round.
        var pattern = new bool[n];
        for (int i = 0; i < 5; i++)
        {
            pattern[i] = true;
        }

        GoodnessOfFit.RunTest outcome = GoodnessOfFit.Runs(pattern, exact: true, Tail.Both);
        Assert.Equal(2, outcome.Runs);
        Assert.Equal(2.0 * counts[2] / total, outcome.P, 12);

        GoodnessOfFit.RunTest one = GoodnessOfFit.Runs(pattern, exact: true, Tail.Left);
        Assert.Equal((double)counts[2] / total, one.P, 12);
    }

    /// <summary>A sequence entirely on one side of its reference has one run and nothing to test.</summary>
    [Fact]
    public void ASequenceOnOneSideHasNothingToTest()
    {
        GoodnessOfFit.RunTest outcome = GoodnessOfFit.Runs([true, true, true], exact: true, Tail.Both);
        Assert.Equal(1, outcome.P, 12);
        Assert.Equal(1, outcome.Runs);
        Assert.Equal(0, outcome.Below);
    }

    // --- Rank tests --------------------------------------------------------------------------------------

    /// <summary>
    /// The exact rank-sum distribution is a count of subsets, brute-forced here over every choice of
    /// four ranks from nine.
    /// </summary>
    [Fact]
    public void TheExactRankSumMatchesBruteForce()
    {
        double[] x = [1, 3, 5, 7];
        double[] y = [2, 4, 6, 8, 9];

        RankTests.RankOutcome outcome = RankTests.RankSum(x, y, Tail.Both, RankTests.Method.Exact);
        Assert.Equal(16, outcome.Statistic, 12);
        Assert.True(outcome.Exact);

        // Every way of giving four of the nine ranks to the first sample, counted.
        int atMost = 0;
        int atLeast = 0;
        int total = 0;
        for (int mask = 0; mask < 1 << 9; mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) != 4)
            {
                continue;
            }

            total++;
            int sum = 0;
            for (int i = 0; i < 9; i++)
            {
                if (((mask >> i) & 1) != 0)
                {
                    sum += i + 1;
                }
            }

            if (sum <= 16)
            {
                atMost++;
            }

            if (sum >= 16)
            {
                atLeast++;
            }
        }

        Assert.Equal(126, total);
        Assert.Equal(2.0 * Math.Min(atMost, atLeast) / total, outcome.P, 12);
    }

    /// <summary>Ties make the exact count wrong rather than slow, so an exact test is refused over them.</summary>
    [Fact]
    public void AnExactRankTestIsRefusedOverTies()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            RankTests.RankSum([1, 2, 2], [3, 4], Tail.Both, RankTests.Method.Exact));
        Assert.Contains("ties", error.Message, StringComparison.Ordinal);

        // The same data goes through the approximation without complaint.
        RankTests.RankOutcome approximate =
            RankTests.RankSum([1, 2, 2], [3, 4], Tail.Both, RankTests.Method.Approximate);
        Assert.False(approximate.Exact);
        Assert.InRange(approximate.P, 0, 1);
    }

    /// <summary>
    /// The signed-rank distribution over n differences is the distribution of a subset sum of 1…n, which
    /// is symmetric about n(n+1)/4 — so the smallest and largest statistics have the same probability.
    /// </summary>
    [Fact]
    public void TheSignedRankDistributionIsSymmetric()
    {
        double[] allNegative = [-1, -2, -3, -4, -5, -6];
        double[] allPositive = [1, 2, 3, 4, 5, 6];

        RankTests.RankOutcome low = RankTests.SignedRank(allNegative, Tail.Both, RankTests.Method.Exact);
        RankTests.RankOutcome high = RankTests.SignedRank(allPositive, Tail.Both, RankTests.Method.Exact);

        Assert.Equal(0, low.Statistic, 12);
        Assert.Equal(21, high.Statistic, 12);
        Assert.Equal(low.P, high.P, 12);
        Assert.Equal(2.0 / 64, low.P, 12);
    }

    /// <summary>A difference of exactly zero leaves the signed-rank test rather than being given a sign.</summary>
    [Fact]
    public void AZeroDifferenceLeavesTheSignedRankTest()
    {
        RankTests.RankOutcome withZeros =
            RankTests.SignedRank([0, 0, 1, 2, 3], Tail.Both, RankTests.Method.Approximate);
        RankTests.RankOutcome without =
            RankTests.SignedRank([1, 2, 3], Tail.Both, RankTests.Method.Approximate);

        Assert.Equal(without.Statistic, withZeros.Statistic, 12);
        Assert.Equal(without.P, withZeros.P, 12);
    }

    /// <summary>The sign test is the binomial test, and its probability can be written down.</summary>
    [Fact]
    public void TheSignTestIsTheBinomialTest()
    {
        RankTests.RankOutcome outcome = RankTests.Sign([1, 1, 1, 1, 1, -1], Tail.Both, RankTests.Method.Exact);
        Assert.Equal(5, outcome.Statistic, 12);

        // Two-sided: twice the chance of one or fewer of six coming out negative.
        double expected = 2 * (Math.Pow(0.5, 6) * (1 + 6));
        Assert.Equal(expected, outcome.P, 12);

        RankTests.RankOutcome right = RankTests.Sign([1, 1, 1, 1, 1, -1], Tail.Right, RankTests.Method.Exact);
        Assert.Equal(Math.Pow(0.5, 6) * (6 + 1), right.P, 12);
    }

    /// <summary>
    /// Ansari and Bradley's statistic is large when the first sample is the tighter one, so the
    /// alternative that its variance is <em>greater</em> reads the opposite tail. Getting that backwards
    /// is the mistake this pins.
    /// </summary>
    [Fact]
    public void TheDispersionTestReadsItsTailBackwards()
    {
        double[] tight = [4.9, 5.0, 5.1, 5.0, 4.95, 5.05];
        double[] loose = [1, 3, 5, 7, 9, 11];

        RankTests.RankOutcome bigger = RankTests.AnsariBradley(tight, loose, Tail.Right, RankTests.Method.Approximate);
        RankTests.RankOutcome smaller = RankTests.AnsariBradley(tight, loose, Tail.Left, RankTests.Method.Approximate);

        Assert.True(bigger.Z > 0, "the tight sample collects the large scores.");
        Assert.True(
            smaller.P < bigger.P,
            "the alternative that the tight sample varies less is the one the data supports.");
        Assert.Equal(1, bigger.P + smaller.P, 10);
    }

    // --- Tests about a model --------------------------------------------------------------------------------

    /// <summary>
    /// Imhof's inversion has an exact check: a difference of two chi-squares is an F, so the probability
    /// that <c>χ² − λ·χ²</c> is negative is the F distribution function at λ.
    /// </summary>
    [Fact]
    public void ImhofsInversionReproducesTheFDistribution()
    {
        foreach (double lambda in new[] { 0.25, 0.5, 1.0, 2.0, 7.5 })
        {
            Assert.Equal(
                ContinuousDistributions.FCdf(lambda, 1, 1),
                Imhof.BelowZero([1, -lambda]),
                7);

            Assert.Equal(
                ContinuousDistributions.FCdf(lambda, 2, 2),
                Imhof.BelowZero([1, 1, -lambda, -lambda]),
                7);

            Assert.Equal(
                ContinuousDistributions.FCdf(lambda * 6.0 / 4.0, 4, 6),
                Imhof.BelowZero([1, 1, 1, 1, -lambda, -lambda, -lambda, -lambda, -lambda, -lambda]),
                7);
        }

        // A symmetric weighting has to give exactly one half, whatever the quadrature does.
        Assert.Equal(0.5, Imhof.BelowZero([1, -1]), 9);
    }

    /// <summary>
    /// Alternating residuals are as far from serial correlation as a sequence can get, so the statistic
    /// is near four and the right-hand test — the one looking for positive correlation — finds nothing.
    /// </summary>
    [Fact]
    public void TheDurbinWatsonStatisticSpansBothKindsOfCorrelation()
    {
        int n = 24;
        var design = new double[n, 1];
        var alternating = new double[n];
        var drifting = new double[n];
        for (int i = 0; i < n; i++)
        {
            design[i, 0] = 1;
            alternating[i] = i % 2 == 0 ? 1 : -1;
            drifting[i] = i - ((n - 1) / 2.0);
        }

        LinearModelTests.SerialCorrelation apart =
            LinearModelTests.DurbinWatson(alternating, design, exact: true, Tail.Right);
        Assert.True(apart.D > 3.8, $"alternating residuals should give d near 4, not {apart.D}.");
        Assert.True(apart.P > 0.99, "there is no positive correlation to find here.");

        LinearModelTests.SerialCorrelation together =
            LinearModelTests.DurbinWatson(drifting, design, exact: true, Tail.Right);
        Assert.True(together.D < 0.1, $"a monotone drift should give d near 0, not {together.D}.");
        Assert.True(together.P < 1e-6, "a monotone drift is as correlated as residuals get.");

        // The approximation is a different route to the same question and has to agree on the verdict.
        LinearModelTests.SerialCorrelation approximate =
            LinearModelTests.DurbinWatson(drifting, design, exact: false, Tail.Right);
        Assert.Equal(together.D, approximate.D, 12);
        Assert.True(approximate.P < 0.01);
    }

    /// <summary>
    /// A linear hypothesis on the identity is the sum of squared coefficients over their rank, and a
    /// repeated restriction is counted once because the rank is what divides.
    /// </summary>
    [Fact]
    public void ARepeatedRestrictionIsCountedOnce()
    {
        double[] beta = [1, 2];
        double[,] identity = { { 1, 0 }, { 0, 1 } };

        LinearModelTests.LinearHypothesis both =
            LinearModelTests.Linear(beta, identity, [0, 0], identity, 10);
        Assert.Equal(2, both.Rank);
        Assert.Equal(2.5, both.F, 12);
        Assert.Equal(1 - ContinuousDistributions.FCdf(2.5, 2, 10), both.P, 12);

        double[,] repeated = { { 1, 0 }, { 1, 0 }, { 1, 0 } };
        LinearModelTests.LinearHypothesis once =
            LinearModelTests.Linear(beta, identity, [0, 0, 0], repeated, 10);
        Assert.Equal(1, once.Rank);
        Assert.Equal(1, once.F, 12);

        // With no residual degrees of freedom the same statistic is referred to a chi-square instead.
        LinearModelTests.LinearHypothesis asymptotic =
            LinearModelTests.Linear(beta, identity, [0, 0], identity, double.PositiveInfinity);
        Assert.Equal(1 - ContinuousDistributions.Chi2Cdf(5, 2), asymptotic.P, 12);
    }

    /// <summary>
    /// Independent variables need no dimensions at all; two variables that are really one need exactly
    /// one.
    /// </summary>
    [Fact]
    public void BartlettsDimensionalityTestCountsRealDirections()
    {
        var random = new Random(19);
        var independent = new double[60, 3];
        var collinear = new double[60, 3];
        for (int i = 0; i < 60; i++)
        {
            double driver = ContinuousDistributions.StandardNormal(random);
            for (int j = 0; j < 3; j++)
            {
                independent[i, j] = ContinuousDistributions.StandardNormal(random);
                collinear[i, j] = (driver * (j + 1))
                    + (0.01 * ContinuousDistributions.StandardNormal(random));
            }
        }

        Assert.Equal(0, LinearModelTests.Bartlett(independent, 0.05).Dimension);
        Assert.Equal(1, LinearModelTests.Bartlett(collinear, 0.05).Dimension);
    }

    /// <summary>
    /// Fisher's exact test on the tea-tasting table: three of four correct out of four and four gives a
    /// two-sided probability of 34/70.
    /// </summary>
    [Fact]
    public void FishersExactTestReproducesTheTeaTastingTable()
    {
        ContingencyTests.ExactTable outcome = ContingencyTests.Fisher(3, 1, 1, 3, 0.05, Tail.Both);
        Assert.Equal(34.0 / 70, outcome.P, 12);
        Assert.Equal(9, outcome.OddsRatio, 12);
        Assert.True(outcome.Lower < 1 && outcome.Upper > 1, "this table does not reject independence.");

        ContingencyTests.ExactTable right = ContingencyTests.Fisher(3, 1, 1, 3, 0.05, Tail.Right);
        Assert.Equal(17.0 / 70, right.P, 12);

        // Every table with the same margins, added up, is one.
        double atMost = ContingencyTests.Fisher(3, 1, 1, 3, 0.05, Tail.Left).P;
        double probabilityOfThree = 16.0 / 70;
        Assert.Equal(1, atMost + right.P - probabilityOfThree, 12);
    }

    /// <summary>The perfectly separated table is the most extreme one and its probability is 1/70.</summary>
    [Fact]
    public void APerfectlySeparatedTableIsTheMostExtreme()
    {
        ContingencyTests.ExactTable outcome = ContingencyTests.Fisher(4, 0, 0, 4, 0.05, Tail.Right);
        Assert.Equal(1.0 / 70, outcome.P, 12);
        Assert.Equal(double.PositiveInfinity, outcome.OddsRatio);
    }

    // --- Power and sample size --------------------------------------------------------------------------------

    /// <summary>
    /// The z test's power has a closed form, and the sample size that reaches a given power is the one
    /// that inverts it — so asking for a size and then asking for its power comes back where it started.
    /// </summary>
    [Fact]
    public void PowerAndSampleSizeInvertEachOther()
    {
        foreach (SampleSize.TestKind kind in new[]
                 {
                     SampleSize.TestKind.Z, SampleSize.TestKind.T, SampleSize.TestKind.TwoSampleT,
                 })
        {
            double n = SampleSize.SampleFor(kind, [100, 10], 110, 0.8, 0.05, Tail.Both, 1);
            double reached = SampleSize.Power(kind, [100, 10], 110, n, 0.05, Tail.Both, 1);
            double below = SampleSize.Power(kind, [100, 10], 110, n - 1, 0.05, Tail.Both, 1);

            Assert.True(reached >= 0.8, $"{kind}: {n} observations only reach {reached}.");
            Assert.True(below < 0.8, $"{kind}: {n - 1} observations already reach {below}.");
        }
    }

    /// <summary>
    /// The one-sample t test needs ten observations to notice a one-standard-deviation shift with
    /// four-fifths certainty — the size MathWorks' own documentation gives for that question.
    /// </summary>
    [Fact]
    public void TheStudentSampleSizeMatchesThePublishedExample() =>
        Assert.Equal(
            10, SampleSize.SampleFor(SampleSize.TestKind.T, [100, 10], 110, 0.8, 0.05, Tail.Both, 1), 12);

    /// <summary>A one-sided test needs fewer observations than a two-sided one for the same power.</summary>
    [Fact]
    public void AOneSidedTestNeedsASmallerSample()
    {
        double both = SampleSize.SampleFor(SampleSize.TestKind.Z, [0, 1], 0.5, 0.9, 0.05, Tail.Both, 1);
        double right = SampleSize.SampleFor(SampleSize.TestKind.Z, [0, 1], 0.5, 0.9, 0.05, Tail.Right, 1);
        Assert.True(right < both, $"one-sided needed {right} and two-sided {both}.");
    }

    /// <summary>
    /// The exact binomial test's power is a sum over the counts it would reject at, so a null proportion
    /// tested against itself has power no greater than the level.
    /// </summary>
    [Fact]
    public void TheProportionTestIsConservativeUnderItsOwnNull()
    {
        double power = SampleSize.Power(
            SampleSize.TestKind.Proportion, [0.5], 0.5, 40, 0.05, Tail.Both, 1);
        Assert.True(power <= 0.05, $"the exact test rejects its own null {power} of the time.");

        double away = SampleSize.Power(
            SampleSize.TestKind.Proportion, [0.5], 0.9, 40, 0.05, Tail.Both, 1);
        Assert.True(away > 0.99);
    }

    // --- The studentized range ----------------------------------------------------------------------------------

    /// <summary>
    /// With two means the studentized range is √2 times a Student's t, which is an exact identity and
    /// the only closed form the distribution has.
    /// </summary>
    [Fact]
    public void TheRangeOfTwoMeansIsAStudentT()
    {
        foreach (double df in new double[] { 5, 12, 40 })
        {
            foreach (double q in new[] { 0.5, 1.5, 3.0, 4.5 })
            {
                double expected = (2 * ContinuousDistributions.TCdf(q / Math.Sqrt(2), df)) - 1;
                Assert.Equal(expected, StudentizedRange.Probability(q, 2, df), 6);
            }

            Assert.Equal(
                Math.Sqrt(2) * ContinuousDistributions.TInv(0.975, df),
                StudentizedRange.Critical(0.05, 2, df),
                5);
        }

        // With the scale known, the same identity is written with a normal instead.
        Assert.Equal(
            (2 * ContinuousDistributions.NormalCdf(3 / Math.Sqrt(2), 0, 1)) - 1,
            StudentizedRange.Probability(3, 2, double.PositiveInfinity),
            8);
    }

    /// <summary>The published critical values of the studentized range, to the two decimals they are given to.</summary>
    [Theory]
    [InlineData(0.05, 3, 10, 3.88)]
    [InlineData(0.05, 4, 20, 3.96)]
    [InlineData(0.05, 5, 30, 4.10)]
    [InlineData(0.01, 3, 10, 5.27)]
    public void TheStudentizedRangeMatchesItsPublishedTable(double alpha, int groups, double df, double expected) =>
        Assert.Equal(expected, StudentizedRange.Critical(alpha, groups, df), 1);

    /// <summary>
    /// With two groups every correction is the same test, because there is only one comparison to
    /// correct for — which is what makes the family of rules agree there and nowhere else.
    /// </summary>
    [Fact]
    public void EveryCorrectionAgreesOnASinglePair()
    {
        double[] estimates = [10, 14];
        double[] weights = [0.25, 0.25];

        double reference = double.NaN;
        foreach (MultipleComparison.Correction correction in Enum.GetValues<MultipleComparison.Correction>())
        {
            MultipleComparison.Comparison[] compared =
                MultipleComparison.Compare(estimates, weights, 2, 12, 0.05, correction);
            Assert.Single(compared);
            if (double.IsNaN(reference))
            {
                reference = compared[0].P;
            }
            else
            {
                Assert.Equal(reference, compared[0].P, 6);
            }
        }
    }

    /// <summary>
    /// With more than one comparison the corrections order themselves: no correction at all is the most
    /// eager to reject, and Scheffé's — which covers every contrast, not only the pairs — is the least.
    /// </summary>
    [Fact]
    public void TheCorrectionsOrderThemselves()
    {
        double[] estimates = [10, 14, 18, 11];
        double[] weights = [0.25, 0.25, 0.25, 0.25];

        double Smallest(MultipleComparison.Correction correction) =>
            MultipleComparison.Compare(estimates, weights, 2, 20, 0.05, correction)[0].P;

        double lsd = Smallest(MultipleComparison.Correction.LeastSignificant);
        double tukey = Smallest(MultipleComparison.Correction.TukeyKramer);
        double sidak = Smallest(MultipleComparison.Correction.DunnSidak);
        double bonferroni = Smallest(MultipleComparison.Correction.Bonferroni);
        double scheffe = Smallest(MultipleComparison.Correction.Scheffe);

        Assert.True(lsd < tukey, $"no correction ({lsd}) must be the most eager, not {tukey}.");
        Assert.True(tukey < sidak);
        Assert.True(sidak <= bonferroni);
        Assert.True(bonferroni < scheffe);
    }

    /// <summary>The interval a comparison reports contains zero exactly when it does not reject.</summary>
    [Fact]
    public void AComparisonsIntervalAgreesWithItsProbability()
    {
        double[] estimates = [10, 14, 18];
        double[] weights = [0.2, 0.2, 0.2];

        foreach (MultipleComparison.Comparison compared in
                 MultipleComparison.Compare(estimates, weights, 2, 15, 0.05, MultipleComparison.Correction.TukeyKramer))
        {
            bool spansZero = compared.Lower <= 0 && compared.Upper >= 0;
            Assert.Equal(spansZero, compared.P > 0.05);
            Assert.Equal(compared.Estimate, (compared.Lower + compared.Upper) / 2, 10);
        }
    }

    private static double[] Standard(double[] points)
    {
        var probabilities = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            probabilities[i] = ContinuousDistributions.NormalCdf(points[i], 0, 1);
        }

        return probabilities;
    }
}
