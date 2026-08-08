using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave D: the discrete distribution kernels. Three things are worth testing here and each has
/// its own group below — that the probabilities are the published ones, that they sum to exactly one
/// over the support, and that the quantile really is the inverse of the distribution function at
/// every step rather than one place off.
/// </summary>
public class DiscreteDistributionTests
{
    [Fact]
    public void BinomialProbabilitiesArePublishedValues()
    {
        Assert.Equal(0.266827932, DiscreteDistributions.BinomialPdf(3, 10, 0.3), 9);
        Assert.Equal(0.6496107184, DiscreteDistributions.BinomialCdf(3, 10, 0.3), 9);
        Assert.Equal(0.3503892816, DiscreteDistributions.BinomialUpper(3, 10, 0.3), 9);

        // A count outside the support has probability zero, and so does a fractional one — a
        // binomial variable cannot be three and a half.
        Assert.Equal(0, DiscreteDistributions.BinomialPdf(11, 10, 0.3));
        Assert.Equal(0, DiscreteDistributions.BinomialPdf(3.5, 10, 0.3));
        Assert.Equal(0, DiscreteDistributions.BinomialPdf(-1, 10, 0.3));

        // The degenerate probabilities are the ones a logarithm would turn into NaN.
        Assert.Equal(1, DiscreteDistributions.BinomialPdf(0, 10, 0));
        Assert.Equal(1, DiscreteDistributions.BinomialPdf(10, 10, 1));
    }

    [Fact]
    public void PoissonProbabilitiesArePublishedValues()
    {
        Assert.Equal(0.2240418077, DiscreteDistributions.PoissonPdf(2, 3), 9);
        Assert.Equal(0.4231900811, DiscreteDistributions.PoissonCdf(2, 3), 9);
        Assert.Equal(0.5768099189, DiscreteDistributions.PoissonUpper(2, 3), 9);
        Assert.Equal(1, DiscreteDistributions.PoissonPdf(0, 0));
    }

    /// <summary>
    /// MATLAB's geometric and negative binomial count the failures before the success, not the trials
    /// taken. Reading them the other way is the single easiest mistake to make here, and shifts every
    /// answer by one place.
    /// </summary>
    [Fact]
    public void TheGeometricAndNegativeBinomialCountFailures()
    {
        // Two failures then a success at probability a quarter: (3/4)² × (1/4).
        Assert.Equal(0.140625, DiscreteDistributions.GeometricPdf(2, 0.25), 12);
        Assert.Equal(0.25, DiscreteDistributions.GeometricPdf(0, 0.25), 12);
        Assert.Equal(1 - Math.Pow(0.75, 3), DiscreteDistributions.GeometricCdf(2, 0.25), 12);
        Assert.Equal(Math.Pow(0.75, 3), DiscreteDistributions.GeometricUpper(2, 0.25), 12);

        // Three failures before the second success: C(4,3) p² q³.
        Assert.Equal(4 * 0.16 * 0.216, DiscreteDistributions.NegativeBinomialPdf(3, 2, 0.4), 12);
        Assert.Equal(0.16, DiscreteDistributions.NegativeBinomialPdf(0, 2, 0.4), 12);

        // One success waited for is the geometric exactly.
        for (int x = 0; x < 6; x++)
        {
            Assert.Equal(
                DiscreteDistributions.GeometricPdf(x, 0.3),
                DiscreteDistributions.NegativeBinomialPdf(x, 1, 0.3), 12);
        }
    }

    [Fact]
    public void TheHypergeometricCountsDrawsWithoutReplacement()
    {
        // Two of the six marked items in five draws from twenty: C(6,2)C(14,3)/C(20,5).
        Assert.Equal(5460.0 / 15504, DiscreteDistributions.HypergeometricPdf(2, 20, 6, 5), 12);
        Assert.Equal(0.8686790506, DiscreteDistributions.HypergeometricCdf(2, 20, 6, 5), 9);

        // Six draws from a population of ten of which eight are marked: at least four must be marked,
        // so the support does not start at zero and neither does the distribution function.
        Assert.Equal(0, DiscreteDistributions.HypergeometricPdf(3, 10, 8, 6));
        Assert.Equal(0, DiscreteDistributions.HypergeometricCdf(3, 10, 8, 6));
        Assert.Equal(1, DiscreteDistributions.HypergeometricCdf(6, 10, 8, 6), 12);
    }

    [Fact]
    public void TheDiscreteUniformStartsAtOne()
    {
        Assert.Equal(1.0 / 6, DiscreteDistributions.DiscreteUniformPdf(3, 6), 12);
        Assert.Equal(0, DiscreteDistributions.DiscreteUniformPdf(0, 6));
        Assert.Equal(0, DiscreteDistributions.DiscreteUniformPdf(7, 6));
        Assert.Equal(0.5, DiscreteDistributions.DiscreteUniformCdf(3, 6), 12);
        Assert.Equal(1, DiscreteDistributions.DiscreteUniformCdf(6, 6), 12);
    }

    /// <summary>
    /// The one identity every probability mass function has to satisfy. A pdf written through the
    /// wrong binomial coefficient can match a published value at one point and still fail this.
    /// </summary>
    [Theory]
    [InlineData("Binomial", 12.0, 0.37, 0.0, 12)]
    [InlineData("Poisson", 4.5, 0.0, 0.0, 60)]
    [InlineData("Geometric", 0.2, 0.0, 0.0, 400)]
    [InlineData("Hypergeometric", 30.0, 12.0, 8.0, 30)]
    [InlineData("Negative Binomial", 3.0, 0.4, 0.0, 400)]
    [InlineData("Discrete Uniform", 9.0, 0.0, 0.0, 9)]
    public void TheProbabilitiesSumToOne(string name, double a, double b, double c, int highest)
    {
        DistributionFamily family = DiscreteFamilies.Find(name)!;
        double[] parameters = new[] { a, b, c }[..family.ParameterCount];

        double total = 0;
        for (int x = 0; x <= highest; x++)
        {
            total += family.Pdf(x, parameters);
        }

        Assert.Equal(1, total, 9);
    }

    /// <summary>
    /// Every distribution function has to agree with the sum of the probabilities up to that point,
    /// and every quantile has to land back on the value it started from. The second half is where an
    /// off-by-one in the search would show, since the answer is only ever a whole number and a
    /// tolerance would hide nothing.
    /// </summary>
    [Theory]
    [InlineData("Binomial", 12.0, 0.37, 0.0, 12)]
    [InlineData("Poisson", 4.5, 0.0, 0.0, 20)]
    [InlineData("Geometric", 0.2, 0.0, 0.0, 25)]
    [InlineData("Hypergeometric", 30.0, 12.0, 8.0, 8)]
    [InlineData("Negative Binomial", 3.0, 0.4, 0.0, 25)]
    [InlineData("Discrete Uniform", 9.0, 0.0, 0.0, 9)]
    public void TheQuantileUndoesTheDistributionFunction(string name, double a, double b, double c, int highest)
    {
        DistributionFamily family = DiscreteFamilies.Find(name)!;
        double[] parameters = new[] { a, b, c }[..family.ParameterCount];
        double lowest = name == "Discrete Uniform" ? 1 : (name == "Hypergeometric" ? 0 : 0);

        double running = 0;
        for (double x = lowest; x <= highest; x++)
        {
            running += family.Pdf(x, parameters);
            double cumulative = family.Cdf(x, parameters);
            Assert.Equal(running, cumulative, 9);

            if (cumulative is > 1e-12 and < 1 - 1e-12)
            {
                Assert.Equal(x, family.Inv(cumulative, parameters));

                // Anything strictly between two steps has to round up to the higher one.
                double half = cumulative - (family.Pdf(x, parameters) / 2);
                if (half > 0)
                {
                    Assert.Equal(x, family.Inv(half, parameters));
                }
            }
        }
    }

    /// <summary>The ends of the probability scale name the ends of the support, not an error.</summary>
    [Fact]
    public void TheQuantileEndsAtTheEndsOfTheSupport()
    {
        Assert.Equal(0, DiscreteDistributions.BinomialInv(0, 10, 0.3));
        Assert.Equal(10, DiscreteDistributions.BinomialInv(1, 10, 0.3));
        Assert.Equal(1, DiscreteDistributions.DiscreteUniformInv(0, 6));
        Assert.Equal(6, DiscreteDistributions.DiscreteUniformInv(1, 6));
        Assert.Equal(double.PositiveInfinity, DiscreteDistributions.PoissonInv(1, 3));
        Assert.True(double.IsNaN(DiscreteDistributions.PoissonInv(1.5, 3)));
        Assert.True(double.IsNaN(DiscreteDistributions.BinomialPdf(3, 10.5, 0.3)));
        Assert.True(double.IsNaN(DiscreteDistributions.BinomialPdf(3, 10, 1.2)));
    }

    [Fact]
    public void TheMomentsAreTheTextbookOnes()
    {
        (double binomialMean, double binomialVariance) = DiscreteDistributions.BinomialStat(10, 0.3);
        Assert.Equal(3, binomialMean, 12);
        Assert.Equal(2.1, binomialVariance, 12);

        Assert.Equal((4.0, 4.0), DiscreteDistributions.PoissonStat(4));

        (double geometricMean, double geometricVariance) = DiscreteDistributions.GeometricStat(0.2);
        Assert.Equal(4, geometricMean, 12);
        Assert.Equal(20, geometricVariance, 12);

        Assert.Equal((3.5, 35.0 / 12), DiscreteDistributions.DiscreteUniformStat(6));

        (double mean, double variance) = DiscreteDistributions.HypergeometricStat(20, 6, 5);
        Assert.Equal(1.5, mean, 12);
        Assert.Equal(5 * 0.3 * 0.7 * (15.0 / 19), variance, 12);

        (double negativeMean, double negativeVariance) = DiscreteDistributions.NegativeBinomialStat(3, 0.4);
        Assert.Equal(4.5, negativeMean, 12);
        Assert.Equal(11.25, negativeVariance, 12);
    }

    /// <summary>
    /// The samplers have to draw from the distribution they are named after, which the probabilities
    /// alone cannot check. Each family is drawn from many times under a fixed seed and the counts
    /// compared with the mass function.
    /// </summary>
    [Theory]
    [InlineData("Binomial", 12.0, 0.37, 0.0)]
    [InlineData("Poisson", 4.5, 0.0, 0.0)]
    [InlineData("Geometric", 0.35, 0.0, 0.0)]
    [InlineData("Hypergeometric", 30.0, 12.0, 8.0)]
    [InlineData("Negative Binomial", 3.0, 0.4, 0.0)]
    [InlineData("Discrete Uniform", 9.0, 0.0, 0.0)]
    public void TheSamplersDrawFromTheirOwnDistribution(string name, double a, double b, double c)
    {
        DistributionFamily family = DiscreteFamilies.Find(name)!;
        double[] parameters = new[] { a, b, c }[..family.ParameterCount];

        const int Draws = 40000;
        var random = new Random(20260808);
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < Draws; i++)
        {
            double drawn = family.Sample(random, parameters);
            Assert.Equal(Math.Floor(drawn), drawn);
            seen[(int)drawn] = seen.GetValueOrDefault((int)drawn) + 1;
        }

        foreach ((int value, int count) in seen)
        {
            double expected = family.Pdf(value, parameters);
            Assert.True(Math.Abs((count / (double)Draws) - expected) < 0.01,
                $"{name} drew {value} {count} times in {Draws}, where its probability is {expected}.");
        }
    }

    /// <summary>
    /// The binomial sampler switches strategy above sixty-four trials — from counting coin flips to
    /// reading the count off the distribution function — so the larger case needs its own check that
    /// it is still binomial.
    /// </summary>
    [Fact]
    public void TheBinomialSamplerIsBinomialAboveItsCrossover()
    {
        var random = new Random(11);
        double total = 0;
        const int Draws = 20000;
        for (int i = 0; i < Draws; i++)
        {
            total += DiscreteDistributions.BinomialSample(random, 500, 0.2);
        }

        Assert.Equal(100, total / Draws, 0);
    }

    [Fact]
    public void TheMultinomialIsTheCountVectorsProbability()
    {
        // 6!/(1!2!3!) × 0.2 × 0.3² × 0.5³.
        Assert.Equal(0.135, DiscreteDistributions.MultinomialPdf([1, 2, 3], [0.2, 0.3, 0.5]), 12);

        // One category is the degenerate case, and a single trial is the categorical distribution.
        Assert.Equal(1, DiscreteDistributions.MultinomialPdf([4], [1]), 12);
        Assert.Equal(0.3, DiscreteDistributions.MultinomialPdf([0, 1, 0], [0.2, 0.3, 0.5]), 12);

        // Probabilities that do not describe a distribution are not quietly renormalized.
        Assert.True(double.IsNaN(DiscreteDistributions.MultinomialPdf([1, 1], [0.2, 0.3])));
        Assert.True(double.IsNaN(DiscreteDistributions.MultinomialPdf([1.5, 0.5], [0.5, 0.5])));

        // And every possible split of three trials over two categories accounts for all of it.
        double total = 0;
        for (int i = 0; i <= 3; i++)
        {
            total += DiscreteDistributions.MultinomialPdf([i, 3 - i], [0.4, 0.6]);
        }

        Assert.Equal(1, total, 12);
    }

    [Fact]
    public void MultinomialDrawsKeepTheirTrialCount()
    {
        var random = new Random(5);
        var totals = new double[3];
        const int Draws = 5000;

        for (int i = 0; i < Draws; i++)
        {
            double[] counts = DiscreteDistributions.MultinomialSample(random, 10, [0.2, 0.3, 0.5]);
            Assert.Equal(10, counts.Sum());
            for (int c = 0; c < counts.Length; c++)
            {
                totals[c] += counts[c];
            }
        }

        Assert.Equal(2, totals[0] / Draws, 1);
        Assert.Equal(3, totals[1] / Draws, 1);
        Assert.Equal(5, totals[2] / Draws, 1);
    }
}
