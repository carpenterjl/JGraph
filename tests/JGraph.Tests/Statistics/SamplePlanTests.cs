using JGraph.Statistics.Sampling;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave E: weighted draws, Latin hypercube designs, combinations, and the bootstrap intervals.
/// </summary>
/// <remarks>
/// A sampler cannot be checked against a published number, but it can be checked against what it
/// promises: without replacement nothing repeats, with weights the frequencies converge on the weights,
/// a Latin hypercube has one point in every stratum of every variable, and a seed repeats.
/// </remarks>
public class SamplePlanTests
{
    [Fact]
    public void SamplingWithoutReplacementNeverRepeats()
    {
        var random = new Random(11);
        for (int trial = 0; trial < 200; trial++)
        {
            int[] picks = SamplePlans.WeightedSample(random, 12, null, 12, replacement: false);
            Assert.Equal(12, picks.Distinct().Count());
            Assert.Equal(Enumerable.Range(0, 12), picks.OrderBy(i => i));
        }
    }

    [Fact]
    public void AskingForMoreThanThePopulationIsRefused() =>
        Assert.Throws<ArgumentException>(() =>
            SamplePlans.WeightedSample(new Random(1), 4, null, 5, replacement: false));

    /// <summary>
    /// With replacement, the long-run frequency of each index is its share of the total weight.
    /// </summary>
    [Fact]
    public void WeightsBecomeFrequencies()
    {
        double[] weights = [1, 3, 4, 2];
        var random = new Random(2026);
        const int draws = 200000;
        int[] picks = SamplePlans.WeightedSample(random, 4, weights, draws, replacement: true);

        for (int i = 0; i < weights.Length; i++)
        {
            double share = (double)picks.Count(p => p == i) / draws;
            Assert.Equal(weights[i] / 10, share, 2);
        }
    }

    /// <summary>
    /// Without replacement the weights still bias the draw, and a value whose weight is zero is never
    /// chosen at all — the case a cumulative sweep gets wrong at its last index.
    /// </summary>
    [Fact]
    public void AZeroWeightIsNeverChosen()
    {
        var random = new Random(77);
        for (int trial = 0; trial < 500; trial++)
        {
            int[] picks = SamplePlans.WeightedSample(random, 4, [2, 0, 1, 3], 3, replacement: false);
            Assert.DoesNotContain(1, picks);
            Assert.Equal(3, picks.Distinct().Count());
        }
    }

    [Fact]
    public void ThereMustBeEnoughPositiveWeightsToGoRound() =>
        Assert.Throws<ArgumentException>(() =>
            SamplePlans.WeightedSample(new Random(1), 4, [1, 1, 0, 0], 3, replacement: false));

    /// <summary>
    /// A Latin hypercube puts exactly one point in each of the n strata of every variable. Unsmoothed,
    /// the points sit at the stratum midpoints, so the sorted column is (i − ½)/n exactly.
    /// </summary>
    [Fact]
    public void EveryStratumHoldsExactlyOnePoint()
    {
        double[,] design = SamplePlans.LatinHypercube(
            new Random(5), 8, 3, smooth: false, SamplePlans.LatinCriterion.None, 1);

        for (int v = 0; v < 3; v++)
        {
            var column = new List<double>();
            for (int i = 0; i < 8; i++)
            {
                column.Add(design[i, v]);
            }

            column.Sort();
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal((i + 0.5) / 8, column[i], 12);
            }
        }
    }

    /// <summary>Smoothed, each point is somewhere inside its own stratum rather than at the middle.</summary>
    [Fact]
    public void ASmoothedDesignStillFillsEveryStratum()
    {
        double[,] design = SamplePlans.LatinHypercube(
            new Random(6), 10, 2, smooth: true, SamplePlans.LatinCriterion.None, 1);

        for (int v = 0; v < 2; v++)
        {
            var strata = new HashSet<int>();
            for (int i = 0; i < 10; i++)
            {
                Assert.InRange(design[i, v], 0, 1);
                strata.Add((int)(design[i, v] * 10));
            }

            Assert.Equal(10, strata.Count);
        }
    }

    /// <summary>
    /// The maximin criterion has to produce a design at least as spread out as the first attempt, or it
    /// is not choosing anything.
    /// </summary>
    [Fact]
    public void TheMaximinCriterionSpreadsThePointsOut()
    {
        double plain = ClosestPair(SamplePlans.LatinHypercube(
            new Random(3), 8, 2, smooth: false, SamplePlans.LatinCriterion.None, 1));
        double optimized = ClosestPair(SamplePlans.LatinHypercube(
            new Random(3), 8, 2, smooth: false, SamplePlans.LatinCriterion.Maximin, 40));

        Assert.True(optimized >= plain,
            $"the maximin design's closest pair is {optimized}, no better than the plain {plain}.");
    }

    /// <summary>A seeded design repeats exactly; a differently seeded one does not.</summary>
    [Fact]
    public void ADesignRepeatsUnderItsSeed()
    {
        double[,] first = SamplePlans.LatinHypercube(
            new Random(99), 6, 2, smooth: true, SamplePlans.LatinCriterion.None, 1);
        double[,] again = SamplePlans.LatinHypercube(
            new Random(99), 6, 2, smooth: true, SamplePlans.LatinCriterion.None, 1);
        double[,] other = SamplePlans.LatinHypercube(
            new Random(100), 6, 2, smooth: true, SamplePlans.LatinCriterion.None, 1);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void CombinationsAreListedInAscendingOrder()
    {
        List<int[]> pairs = SamplePlans.Combinations(4, 2);

        Assert.Equal(6, pairs.Count);
        Assert.Equal([0, 1], pairs[0]);
        Assert.Equal([0, 2], pairs[1]);
        Assert.Equal([2, 3], pairs[5]);
        Assert.Equal(pairs.Count, pairs.Select(p => string.Join(",", p)).Distinct().Count());

        Assert.Single(SamplePlans.Combinations(5, 0));
        Assert.Empty(SamplePlans.Combinations(3, 4));
        Assert.Equal(252, SamplePlans.Combinations(10, 5).Count);
    }

    /// <summary>
    /// A percentile interval really is the percentiles of the replicates, so it can be written out.
    /// </summary>
    [Fact]
    public void ThePercentileIntervalIsThePercentiles()
    {
        double[] replicates = Enumerable.Range(1, 1000).Select(i => (double)i).ToArray();
        (double lower, double upper) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.Percentile, replicates, 500.5, null, 0.05);

        double[] ends = JGraph.Statistics.DescriptiveStatistics.Percentiles(replicates, [2.5, 97.5]);
        Assert.Equal(ends[0], lower, 12);
        Assert.Equal(ends[1], upper, 12);
    }

    /// <summary>
    /// The normal interval is the statistic corrected for bootstrap bias, plus and minus 1.96 standard
    /// errors — every piece of which is arithmetic on the replicates.
    /// </summary>
    [Fact]
    public void TheNormalIntervalIsBiasCorrectedAndSymmetric()
    {
        var random = new Random(31);
        double[] replicates = Enumerable.Range(0, 5000)
            .Select(_ => 4 + JGraph.Statistics.Distributions.ContinuousDistributions.StandardNormal(random))
            .ToArray();

        (double lower, double upper) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.Normal, replicates, 4, null, 0.05);

        double mean = replicates.Average();
        double centre = 4 - (mean - 4);
        double error = Math.Sqrt(replicates.Sum(v => (v - mean) * (v - mean)) / (replicates.Length - 1));
        Assert.Equal(centre, (lower + upper) / 2, 12);
        Assert.Equal(2 * 1.959963984540054 * error, upper - lower, 12);
    }

    /// <summary>
    /// A symmetric bootstrap centred on the statistic has no bias and no skewness to correct, so all
    /// three percentile-based intervals agree — and a lopsided one makes them differ.
    /// </summary>
    [Fact]
    public void TheCorrectionsVanishOnASymmetricBootstrapAndBiteOnALopsidedOne()
    {
        // An even count with none exactly at the statistic, so exactly half the replicates lie below
        // it and the bias correction is exactly zero rather than nearly.
        double[] symmetric = Enumerable.Range(1, 500)
            .SelectMany(i => new[] { 10 - (i / 100.0), 10 + (i / 100.0) })
            .ToArray();
        double[] balanced = Enumerable.Repeat(10.0, symmetric.Length).ToArray();

        (double plainLow, double plainHigh) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.Percentile, symmetric, 10, balanced, 0.05);
        (double shiftedLow, double shiftedHigh) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.BiasCorrected, symmetric, 10, balanced, 0.05);

        Assert.Equal(plainLow, shiftedLow, 10);
        Assert.Equal(plainHigh, shiftedHigh, 10);

        // Three quarters of the replicates below the statistic: the correction has to pull the
        // interval upwards. Compressing one side would not do it — what the correction reads is how
        // many replicates fall below, not how far.
        double[] lopsided = Enumerable.Range(1, 750).Select(i => 10 - (i / 100.0))
            .Concat(Enumerable.Range(1, 250).Select(i => 10 + (i / 100.0)))
            .ToArray();
        (double biasedLow, double biasedHigh) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.BiasCorrected, lopsided, 10, balanced, 0.05);
        (double rawLow, double rawHigh) = Bootstrap.Interval(
            Bootstrap.IntervalMethod.Percentile, lopsided, 10, balanced, 0.05);

        Assert.True(biasedLow > rawLow && biasedHigh > rawHigh,
            "the bias correction did not move an interval whose replicates are lopsided.");
    }

    /// <summary>
    /// The acceleration is zero for a symmetric jackknife and takes the sign of its skewness otherwise,
    /// which is what separates the accelerated interval from the merely bias-corrected one.
    /// </summary>
    [Fact]
    public void TheAccelerationFollowsTheJackknifesSkewness()
    {
        Assert.Equal(0, Bootstrap.Acceleration([1, 2, 3, 4, 5]), 12);
        Assert.Equal(0, Bootstrap.Acceleration(null), 12);
        Assert.True(Bootstrap.Acceleration([1, 1, 1, 1, 10]) < 0);
        Assert.True(Bootstrap.Acceleration([1, 10, 10, 10, 10]) > 0);
    }

    private static double ClosestPair(double[,] design)
    {
        int n = design.GetLength(0);
        int p = design.GetLength(1);
        double smallest = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double sum = 0;
                for (int v = 0; v < p; v++)
                {
                    double gap = design[i, v] - design[j, v];
                    sum += gap * gap;
                }

                smallest = Math.Min(smallest, sum);
            }
        }

        return smallest;
    }
}
