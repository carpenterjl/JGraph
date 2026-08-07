using JGraph.Statistics;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave B: the kernels behind the descriptive statistics, pinned against values MATLAB publishes.
/// The percentile convention is the one that matters most — it is the difference between agreeing with
/// MATLAB and agreeing with NumPy, and nothing above this layer can tell which it got.
/// </summary>
public class DescriptiveStatisticsTests
{
    /// <summary>
    /// MATLAB places the sorted observations at the cumulative probabilities (i − ½)/n, so the lower
    /// quartile of 1, 2, 3, 4 is 1.5. The convention JGS's own <c>percentile</c> uses — observations at
    /// i/(n − 1) — answers 1.75 for the same data, which is why this is a separate kernel rather than a
    /// second caller of the existing one.
    /// </summary>
    [Fact]
    public void PercentilesUseMatlabsMidpointConvention()
    {
        double[] x = [1, 2, 3, 4];
        Assert.Equal(1.5, DescriptiveStatistics.Percentiles(x, [25])[0], 12);
        Assert.Equal(2.5, DescriptiveStatistics.Percentiles(x, [50])[0], 12);
        Assert.Equal(3.5, DescriptiveStatistics.Percentiles(x, [75])[0], 12);

        // The ends clamp to the extreme observation rather than extrapolating past it.
        Assert.Equal(1, DescriptiveStatistics.Percentiles(x, [0])[0], 12);
        Assert.Equal(4, DescriptiveStatistics.Percentiles(x, [100])[0], 12);

        // An odd-length sample's median is an observation, which is the one place the two conventions
        // agree — and the reason a test that only checked the median would have proved nothing.
        Assert.Equal(3, DescriptiveStatistics.Percentiles([1, 2, 3, 4, 5], [50])[0], 12);
    }

    [Fact]
    public void PercentilesDropMissingValuesAndShrinkTheDenominator()
    {
        double[] withGap = [1, double.NaN, 2, 3, double.NaN, 4];
        Assert.Equal(1.5, DescriptiveStatistics.Percentiles(withGap, [25])[0], 12);
        Assert.Equal(double.NaN, DescriptiveStatistics.Percentiles([double.NaN], [50])[0]);
    }

    [Fact]
    public void SkewnessAndKurtosisCorrectForBiasOnRequest()
    {
        double[] x = [1, 1, 2, 6];

        // m3 / m2^1.5 on the plain moments, then the correction sqrt(n(n-1))/(n-2).
        Assert.Equal(9 / Math.Pow(4.25, 1.5), DescriptiveStatistics.Skewness(x, bias: true), 12);
        Assert.Equal(1.779179, DescriptiveStatistics.Skewness(x, bias: false), 6);

        // MATLAB's kurtosis is on the scale where a normal sample answers 3, not 0.
        Assert.Equal(1.7, DescriptiveStatistics.Kurtosis([1, 2, 3, 4, 5], bias: true), 12);
        Assert.Equal(0, DescriptiveStatistics.Skewness([1, 2, 3, 4, 5], bias: true), 12);
    }

    [Fact]
    public void AbsoluteDeviationSwitchesFromTheMeanToTheMedian()
    {
        Assert.Equal(1, DescriptiveStatistics.AbsoluteDeviation([1, 2, 3, 4], aroundMedian: false), 12);

        // One distant observation moves the mean deviation a long way and the median deviation not at
        // all, which is the whole reason the second form exists.
        Assert.Equal(1, DescriptiveStatistics.AbsoluteDeviation([1, 2, 3, 100], aroundMedian: true), 12);
        Assert.True(DescriptiveStatistics.AbsoluteDeviation([1, 2, 3, 100], aroundMedian: false) > 30);
    }

    [Fact]
    public void TrimmingDropsWholeObservationsOrWeightsThem()
    {
        double[] x = [1, 2, 3, 4, 100];

        // 40 percent of five observations is one from each end, so the outlier goes.
        Assert.Equal(3, DescriptiveStatistics.TrimmedMean(x, 40, DescriptiveStatistics.TrimRule.Round), 12);

        // Ten percent of five is a quarter of an observation: rounding keeps everything, flooring keeps
        // everything, and weighting counts the ends at three quarters.
        Assert.Equal(22, DescriptiveStatistics.TrimmedMean(x, 10, DescriptiveStatistics.TrimRule.Round), 12);
        Assert.Equal(22, DescriptiveStatistics.TrimmedMean(x, 10, DescriptiveStatistics.TrimRule.Floor), 12);

        double weighted = DescriptiveStatistics.TrimmedMean(x, 10, DescriptiveStatistics.TrimRule.Weighted);
        Assert.Equal(((0.75 * (1 + 100)) + 2 + 3 + 4) / 4.5, weighted, 12);
    }

    [Fact]
    public void RanksShareTheAverageAcrossATie()
    {
        (double[] ranks, double adjustment) = DescriptiveStatistics.TiedRanks(
            [10, 20, 20, 30], DescriptiveStatistics.TieAdjustment.RankSumOfCubes);

        Assert.Equal([1, 2.5, 2.5, 4], ranks);
        Assert.Equal(3, adjustment, 12); // (2^3 - 2) / 2

        // The Wilcoxon tests want the pair count instead, which is the same tie read differently.
        (_, double pairs) = DescriptiveStatistics.TiedRanks(
            [10, 20, 20, 30], DescriptiveStatistics.TieAdjustment.PairCount);
        Assert.Equal(1, pairs, 12);

        // Ranking from the outside in pairs the smallest with the largest, which is what the
        // Ansari-Bradley dispersion test counts.
        (double[] outside, _) = DescriptiveStatistics.TiedRanks(
            [1, 2, 3, 4], DescriptiveStatistics.TieAdjustment.RankSumOfCubes, fromOutside: true);
        Assert.Equal([1, 2, 2, 1], outside);
    }

    [Fact]
    public void MissingValuesTakeNoRankButKeepTheirPlace()
    {
        (double[] ranks, _) = DescriptiveStatistics.TiedRanks(
            [3, double.NaN, 1], DescriptiveStatistics.TieAdjustment.RankSumOfCubes);

        Assert.Equal(2, ranks[0]);
        Assert.True(double.IsNaN(ranks[1]));
        Assert.Equal(1, ranks[2]);
    }

    /// <summary>
    /// A sample of positive whole numbers gets a row for every integer up to the largest, including the
    /// ones nobody took — which is what lets the table be indexed by the value.
    /// </summary>
    [Fact]
    public void FrequencyTablesCountEveryIntegerUpToTheLargest()
    {
        DescriptiveStatistics.FrequencyRow[] table = DescriptiveStatistics.Tabulate([1, 2, 2, 4]);

        Assert.Equal(4, table.Length);
        Assert.Equal(new DescriptiveStatistics.FrequencyRow(3, 0, 0), table[2]);
        Assert.Equal(2, table[1].Count);
        Assert.Equal(50, table[1].Percent, 12);

        // Anything that is not a positive whole number gets a row per distinct value instead.
        DescriptiveStatistics.FrequencyRow[] fractional = DescriptiveStatistics.Tabulate([2.5, 2.5, -1]);
        Assert.Equal(2, fractional.Length);
        Assert.Equal(-1, fractional[0].Value);
        Assert.Equal(2, fractional[1].Count);
    }

    [Fact]
    public void MeansAndSpreadsAnswerTheOrdinaryDefinitions()
    {
        Assert.Equal(2, DescriptiveStatistics.GeometricMean([1, 4]), 12);
        Assert.Equal(1.6, DescriptiveStatistics.HarmonicMean([1, 4]), 12);
        Assert.Equal(6, DescriptiveStatistics.Range([3, 1, 7]), 12);

        // range ignores NaN because it is max minus min, and both of those do.
        Assert.Equal(6, DescriptiveStatistics.Range([3, double.NaN, 1, 7]), 12);

        (double[] scores, double centre, double spread) =
            DescriptiveStatistics.StandardScores([1, 2, 3], population: false);
        Assert.Equal([-1, 0, 1], scores);
        Assert.Equal(2, centre, 12);
        Assert.Equal(1, spread, 12);

        // A sample with no spread scores zero throughout rather than dividing by nothing.
        (double[] flat, _, _) = DescriptiveStatistics.StandardScores([5, 5, 5], population: false);
        Assert.Equal([0, 0, 0], flat);
    }
}
