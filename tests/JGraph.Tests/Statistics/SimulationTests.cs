using JGraph.Statistics.Cluster;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Multivariate;
using JGraph.Statistics.Sampling;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave J: the distributions described rather than named, the two Markov chains, the piecewise
/// distribution with fitted tails, the covariance of an estimate, and the embedding.
/// </summary>
/// <remarks>
/// Everything stochastic here is seeded and checked against what the thing it draws from says about
/// itself: a chain aimed at a standard normal has a standard normal's mean and spread, a Johnson curve
/// passes through the quantiles it was fitted to, and a Pearson curve has the moments it was asked
/// for. None of it is compared against a recorded run, so a change that improves the arithmetic does
/// not break the suite.
/// </remarks>
public class SimulationTests
{
    // --- The Johnson system -------------------------------------------------------------------------

    [Fact]
    public void AJohnsonCurvePassesThroughTheQuantilesItWasFittedTo()
    {
        double[] z = [-1.5, -0.5, 0.5, 1.5];
        double[] x = [-1.7, -0.4, 0.6, 2.2];

        MomentMatching.JohnsonCurve curve = MomentMatching.Johnson(z, x);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(x[i], curve.At(z[i]), 8);
        }
    }

    [Fact]
    public void TheThreeMembersAreToldApartByTheirGaps()
    {
        // Outer gaps wider than the inner one squared: unbounded.
        Assert.Equal(
            MomentMatching.JohnsonKind.SU,
            MomentMatching.Johnson([-1.5, -0.5, 0.5, 1.5], [-4.0, -0.5, 0.5, 4.0]).Kind);

        // Outer gaps narrower: bounded.
        Assert.Equal(
            MomentMatching.JohnsonKind.SB,
            MomentMatching.Johnson([-1.5, -0.5, 0.5, 1.5], [-1.2, -1.0, 1.0, 1.2]).Kind);

        // Exactly one: the lognormal on the boundary between them.
        Assert.Equal(
            MomentMatching.JohnsonKind.SL,
            MomentMatching.Johnson([-1.5, -0.5, 0.5, 1.5], [1, 2, 4, 8]).Kind);
    }

    [Fact]
    public void ALognormalJohnsonCurveIsExactlyAnExponentialOfALine()
    {
        // The quantiles double each step, so the curve through them is 2^z and every point on it is.
        MomentMatching.JohnsonCurve curve =
            MomentMatching.Johnson([-1.5, -0.5, 0.5, 1.5], [1, 2, 4, 8]);

        Assert.Equal(16, curve.At(2.5), 6);
        Assert.Equal(0.5, curve.At(-2.5), 6);
    }

    [Fact]
    public void TheJohnsonConstructionRefusesQuantilesThatDoNotIncrease()
    {
        Assert.Throws<ArgumentException>(() =>
            MomentMatching.Johnson([-1.5, -0.5, 0.5, 1.5], [1, 2, 2, 8]));
        Assert.Throws<ArgumentException>(() =>
            MomentMatching.Johnson([-1, -0.5, 0.5, 1], [1, 2, 4, 8]));
    }

    // --- The Pearson system -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(0.75, 4.5, 4)]
    [InlineData(0.5, 4.0, 4)]
    [InlineData(2.0, 12.0, 6)]
    [InlineData(0, 5.0, 7)]
    [InlineData(0, 2.4, 2)]
    public void ThePearsonTypeFollowsFromTheShapeMoments(double skewness, double kurtosis, int type)
    {
        MomentMatching.PearsonCurve curve = MomentMatching.Pearson(0, 1, skewness, kurtosis);
        Assert.Equal(type, curve.Type);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(0.6, 4.0)]
    [InlineData(-0.6, 4.0)]
    [InlineData(0, 5.0)]
    public void APearsonCurveHasTheMomentsItWasAskedFor(double skewness, double kurtosis)
    {
        MomentMatching.PearsonCurve curve = MomentMatching.Pearson(10, 2, skewness, kurtosis);

        // The moments are integrated over the quantile, which is exact for a well-behaved curve and
        // needs no draws at all: the mean is the average of the quantile over probability.
        const int Steps = 20000;
        double mean = 0;
        double second = 0;
        double third = 0;
        for (int i = 0; i < Steps; i++)
        {
            double x = curve.Quantile((i + 0.5) / Steps);
            mean += x;
            second += x * x;
            third += x * x * x;
        }

        mean /= Steps;
        second /= Steps;
        third /= Steps;

        double variance = second - (mean * mean);
        double skew = (third - (3 * mean * variance) - (mean * mean * mean)) / Math.Pow(variance, 1.5);

        Assert.Equal(10, mean, 1);
        Assert.Equal(2, Math.Sqrt(variance), 1);
        Assert.Equal(skewness, skew, 1);
    }

    [Fact]
    public void TheNormalMemberIsTheNormal()
    {
        MomentMatching.PearsonCurve curve = MomentMatching.Pearson(3, 2, 0, 3);
        Assert.Equal(0, curve.Type);
        Assert.Equal(3, curve.Quantile(0.5), 8);
        Assert.Equal(3 + (2 * 1.959963984540054), curve.Quantile(0.975), 6);
    }

    [Fact]
    public void ImpossibleShapeMomentsAreRefused()
    {
        // Kurtosis must exceed squared skewness by more than one; nothing lies below that line.
        Assert.Throws<ArgumentException>(() => MomentMatching.Pearson(0, 1, 1, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MomentMatching.Pearson(0, 0, 0, 3));
    }

    // --- The chains ---------------------------------------------------------------------------------

    [Fact]
    public void AMetropolisChainAimedAtANormalHasANormalsMoments()
    {
        var random = new Random(24);
        MarkovChain.Chain chain = MarkovChain.Using(random, () => MarkovChain.Metropolis(
            [0],
            4000,
            point => -0.5 * point[0] * point[0],
            point => [point[0] + (2 * (random.NextDouble() - 0.5) * 3)],
            null,
            burnIn: 500,
            thin: 1));

        Assert.Equal(4000, chain.Samples.Length);
        Assert.InRange(chain.Accepted, 0.1, 0.95);

        (double mean, double deviation) = Moments(chain.Samples);
        Assert.Equal(0, mean, 1);
        Assert.Equal(1, deviation, 1);
    }

    [Fact]
    public void ASymmetricProposalAndAStatedOneAgree()
    {
        // The proposal density cancels when it is symmetric, so saying so and writing it down must
        // give the same chain from the same seed.
        double[] Propose(double[] point) => [point[0] + 0.5];
        double Logged(double[] from, double[] to) => 0;

        MarkovChain.Chain cancelled = MarkovChain.Using(
            new Random(7),
            () => MarkovChain.Metropolis([0], 50, p => -p[0] * p[0], Propose, null, 0, 1));
        MarkovChain.Chain written = MarkovChain.Using(
            new Random(7),
            () => MarkovChain.Metropolis([0], 50, p => -p[0] * p[0], Propose, Logged, 0, 1));

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(cancelled.Samples[i][0], written.Samples[i][0], 12);
        }
    }

    [Fact]
    public void ASliceSamplerAimedAtANormalHasANormalsMoments()
    {
        var random = new Random(99);
        MarkovChain.Chain chain = MarkovChain.Using(random, () => MarkovChain.Slice(
            [0], 3000, point => -0.5 * point[0] * point[0], [4], burnIn: 200, thin: 1));

        Assert.Equal(3000, chain.Samples.Length);
        Assert.True(chain.Evaluations > 3000);

        (double mean, double deviation) = Moments(chain.Samples);
        Assert.Equal(0, mean, 1);
        Assert.Equal(1, deviation, 1);
    }

    [Fact]
    public void ThinningKeepsOneDrawInEveryFew()
    {
        MarkovChain.Chain chain = MarkovChain.Using(
            new Random(3),
            () => MarkovChain.Slice([0], 100, p => -0.5 * p[0] * p[0], [3], 0, 5));

        Assert.Equal(100, chain.Samples.Length);
    }

    [Fact]
    public void ASeededChainIsTheSameChainTwice()
    {
        MarkovChain.Chain Run() => MarkovChain.Using(
            new Random(1234),
            () => MarkovChain.Slice([0.5], 200, p => -0.5 * p[0] * p[0], [2], 10, 1));

        MarkovChain.Chain first = Run();
        MarkovChain.Chain second = Run();
        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(first.Samples[i][0], second.Samples[i][0], 12);
        }
    }

    private static (double Mean, double Deviation) Moments(double[][] samples)
    {
        double mean = 0;
        foreach (double[] point in samples)
        {
            mean += point[0];
        }

        mean /= samples.Length;

        double spread = 0;
        foreach (double[] point in samples)
        {
            spread += (point[0] - mean) * (point[0] - mean);
        }

        return (mean, Math.Sqrt(spread / (samples.Length - 1)));
    }

    // --- Pareto tails -------------------------------------------------------------------------------

    [Fact]
    public void TheMiddleOfAPiecewiseFitIsTheSampleAndTheTailsAreNot()
    {
        var random = new Random(2024);
        var values = new List<double>();
        for (int i = 0; i < 2000; i++)
        {
            values.Add(ContinuousDistributions.TInv(random.NextDouble(), 3));
        }

        var fitted = new ParetoTails(values, 0.1, 0.9);

        Assert.Equal(2000, fitted.Count);
        Assert.Equal(0.1, fitted.Cdf(fitted.LowerBoundary), 6);
        Assert.Equal(0.9, fitted.Cdf(fitted.UpperBoundary), 6);
        Assert.Equal(0, fitted.Segment(0));
        Assert.Equal(-1, fitted.Segment(fitted.LowerBoundary - 1));
        Assert.Equal(1, fitted.Segment(fitted.UpperBoundary + 1));

        // The quantile inverts the distribution function in every piece, which is the one thing that
        // has to hold across the joins.
        foreach (double p in new[] { 0.01, 0.05, 0.2, 0.5, 0.8, 0.95, 0.99 })
        {
            Assert.Equal(p, fitted.Cdf(fitted.Inv(p)), 6);
        }
    }

    [Fact]
    public void ThePiecewiseFitReachesFurtherThanTheSampleDid()
    {
        // A heavy tail, so that the fitted tail really does reach: a light one is fitted with a shape
        // that bounds it, and a bounded tail can end below the largest thing observed.
        var values = new List<double>();
        for (int i = 0; i < 500; i++)
        {
            values.Add(ContinuousDistributions.TInv((i + 0.5) / 500, 3));
        }

        var fitted = new ParetoTails(values, 0.05, 0.95);
        double largest = values[^1];

        // The point of the construction: a probability beyond anything observed still has a value.
        Assert.True(fitted.Inv(0.9999) > largest, $"the tail reached only {fitted.Inv(0.9999)}.");
        Assert.True(fitted.Cdf(largest) < 1);
        Assert.True(fitted.Pdf(largest + 1) > 0);
    }

    [Fact]
    public void APiecewiseFitWithNoTailsIsTheEmpiricalDistribution()
    {
        double[] values = [1, 2, 3, 4, 5];
        var fitted = new ParetoTails(values, 0, 1);

        Assert.Equal(0, fitted.Segment(0));
        Assert.Equal(0, fitted.Segment(100));
        Assert.Equal(0.5, fitted.Cdf(3), 8);
        Assert.Equal(3, fitted.Inv(0.5), 8);
    }

    // --- The covariance of an estimate ----------------------------------------------------------------

    [Fact]
    public void TheCovarianceOfANormalFitMatchesItsClosedForm()
    {
        const int Count = 400;
        var values = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            values[i] = ContinuousDistributions.NormalInv((i + 0.5) / Count, 5, 2);
        }

        double Negative(double[] parameters)
        {
            double total = 0;
            foreach (double value in values)
            {
                total += Math.Log(ContinuousDistributions.NormalPdf(value, parameters[0], parameters[1]));
            }

            return -total;
        }

        double[,] covariance = LikelihoodCovariance.Of(Negative, [5, 2]);

        // The mean's variance is sigma squared over n and the deviation's is half of that, and the two
        // are uncorrelated. The differencing reproduces them to within a fraction of a percent, which
        // is what a second difference of a function known only as a function is worth.
        Assert.Equal(4.0 / Count, covariance[0, 0], 6);
        Assert.InRange(covariance[1, 1], 0.99 * 2.0 / Count, 1.01 * 2.0 / Count);
        Assert.Equal(0, covariance[0, 1], 6);
    }

    // --- The embedding ----------------------------------------------------------------------------------

    [Fact]
    public void TheEmbeddingSeparatesTwoSeparatedClusters()
    {
        var random = new Random(17);
        var data = new double[120][];
        for (int i = 0; i < 120; i++)
        {
            double shift = i < 60 ? 0 : 12;
            data[i] = [Gaussian(random) + shift, Gaussian(random) + shift, Gaussian(random)];
        }

        StochasticEmbedding.Embedding embedding = StochasticEmbedding.Embed(
            random,
            data,
            dimensions: 2,
            perplexity: 20,
            exaggeration: 4,
            rate: 6,
            iterations: 400,
            DistanceMeasure.Create(DistanceMetric.Euclidean, data),
            start: null);

        Assert.Equal(120, embedding.Coordinates.Length);
        foreach (double[] point in embedding.Coordinates)
        {
            Assert.Equal(2, point.Length);
            Assert.True(double.IsFinite(point[0]) && double.IsFinite(point[1]));
        }

        double[] first = Centre(embedding.Coordinates, 0, 60);
        double[] second = Centre(embedding.Coordinates, 60, 120);
        double between = Math.Sqrt(
            ((first[0] - second[0]) * (first[0] - second[0]))
            + ((first[1] - second[1]) * (first[1] - second[1])));

        double within = Spread(embedding.Coordinates, 0, 60, first);
        Assert.True(between > 2 * within, $"the clusters are {between} apart and {within} wide.");
    }

    [Fact]
    public void AnEmbeddingRefusesWhatItCannotDo()
    {
        double[][] tiny = [[0.0], [1.0]];
        Assert.Throws<ArgumentException>(() => StochasticEmbedding.Embed(
            new Random(1), tiny, 2, 1, 4, 100, 50,
            DistanceMeasure.Create(DistanceMetric.Euclidean, tiny), null));

        double[][] four = [[0.0], [1.0], [2.0], [3.0]];
        Assert.Throws<ArgumentOutOfRangeException>(() => StochasticEmbedding.Embed(
            new Random(1), four, 2, 4, 4, 100, 50,
            DistanceMeasure.Create(DistanceMetric.Euclidean, four), null));
    }

    private static double[] Centre(double[][] points, int from, int to)
    {
        double x = 0;
        double y = 0;
        for (int i = from; i < to; i++)
        {
            x += points[i][0];
            y += points[i][1];
        }

        return [x / (to - from), y / (to - from)];
    }

    private static double Spread(double[][] points, int from, int to, double[] centre)
    {
        double total = 0;
        for (int i = from; i < to; i++)
        {
            double dx = points[i][0] - centre[0];
            double dy = points[i][1] - centre[1];
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total / (to - from);
    }

    private static double Gaussian(Random random)
    {
        double u = 1 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
