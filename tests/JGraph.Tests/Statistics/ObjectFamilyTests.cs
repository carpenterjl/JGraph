using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave I: the families that arrive with the distribution objects. Four kinds of check — a value
/// MathWorks publishes, an identity a plausible wrong implementation fails, a special case with a
/// closed form the general code does not share, and a seeded sample, which is an implementation of
/// the distribution that has no code in common with the density being checked.
/// </summary>
public class ObjectFamilyTests
{
    /// <summary>Every family, its parameters and a point comfortably inside its support.</summary>
    public static TheoryData<string, double[], double> Points => new()
    {
        { "Birnbaum-Saunders", [1.5, 0.4], 1.7 },
        { "Burr", [2, 3, 4], 1.3 },
        { "Half Normal", [0, 2], 1.4 },
        { "Inverse Gaussian", [1.5, 3], 1.2 },
        { "Logistic", [1, 2], 0.7 },
        { "Log-Logistic", [0.5, 0.3], 1.9 },
        { "Loguniform", [1, 10], 4 },
        { "Nakagami", [1.5, 2], 1.1 },
        { "Rician", [2, 1], 2.4 },
        { "t Location-Scale", [1, 2, 5], 2.5 },
        { "Triangular", [0, 1, 3], 1.4 },
        { "Stable", [1.6, 0.3, 1, 0], 0.8 },
    };

    [Theory]
    [MemberData(nameof(Points))]
    public void TheQuantileUndoesTheDistributionFunction(string name, double[] parameters, double x)
    {
        DistributionFamily family = ObjectFamilies.Find(name)!;
        Assert.NotNull(family);

        double p = family.Cdf(x, parameters);
        Assert.InRange(p, 0.001, 0.999);
        Assert.Equal(x, family.Inv(p, parameters), 5);
    }

    [Theory]
    [MemberData(nameof(Points))]
    public void TheDensityIsTheSlopeOfTheDistributionFunction(string name, double[] parameters, double x)
    {
        DistributionFamily family = ObjectFamilies.Find(name)!;
        const double Step = 1e-5;
        double slope = (family.Cdf(x + Step, parameters) - family.Cdf(x - Step, parameters)) / (2 * Step);
        double density = family.Pdf(x, parameters);
        Assert.True(Math.Abs(slope - density) < 1e-5 * Math.Max(1, density),
            $"{name}: the density said {density} where the distribution function rises at {slope}.");
    }

    [Theory]
    [MemberData(nameof(Points))]
    public void TheMomentsAgreeWithASeededSample(string name, double[] parameters, double x)
    {
        _ = x;
        DistributionFamily family = ObjectFamilies.Find(name)!;
        (double mean, double variance) = family.Stat(parameters);
        if (double.IsNaN(mean) || double.IsInfinity(variance))
        {
            return;
        }

        var random = new Random(20260808);
        const int Draws = 40000;
        double total = 0;
        double square = 0;
        for (int i = 0; i < Draws; i++)
        {
            double draw = family.Sample(random, parameters);
            total += draw;
            square += draw * draw;
        }

        double sampleMean = total / Draws;
        double sampleVariance = (square / Draws) - (sampleMean * sampleMean);
        double tolerance = 5 * Math.Sqrt(variance / Draws);
        Assert.True(Math.Abs(sampleMean - mean) < tolerance,
            $"{name}: the sample averaged {sampleMean} where the family says {mean}.");
        Assert.True(Math.Abs(sampleVariance - variance) < 0.15 * variance,
            $"{name}: the sample spread {sampleVariance} where the family says {variance}.");
    }

    [Fact]
    public void BirnbaumSaundersMatchesItsNormalChangeOfVariable()
    {
        DistributionFamily family = ObjectFamilies.Find("BirnbaumSaunders")!;
        double[] parameters = [2, 0.5];

        // The median is the scale, exactly, because the deviate is zero there. Nothing in the code
        // says so; it falls out only if the change of variable is the documented one.
        Assert.Equal(0.5, family.Cdf(2, parameters), 12);
        Assert.Equal(2, family.Inv(0.5, parameters), 10);
    }

    [Fact]
    public void TheInverseGaussianTailSurvivesAnExponentialThatWouldOverflow()
    {
        DistributionFamily family = ObjectFamilies.Find("InverseGaussian")!;

        // exp(2*lambda/mu) here is e^2000, which is not a number. Written as published the answer is
        // a NaN; folded into the scaled complementary error function it is an ordinary probability.
        double p = family.Cdf(1.2, [1, 1000]);
        Assert.True(double.IsFinite(p), "the tail came back as " + p);
        Assert.InRange(p, 0.9, 1.0);
    }

    [Fact]
    public void TheRicianReducesToARayleighWithNoSignal()
    {
        DistributionFamily rician = ObjectFamilies.Find("Rician")!;
        DistributionFamily rayleigh = ContinuousFamilies.Find("Rayleigh")!;

        for (double x = 0.25; x < 4; x += 0.25)
        {
            Assert.Equal(rayleigh.Cdf(x, [1.5]), rician.Cdf(x, [0, 1.5]), 9);
            Assert.Equal(rayleigh.Pdf(x, [1.5]), rician.Pdf(x, [0, 1.5]), 9);
        }
    }

    [Fact]
    public void TheLocationScaleTReducesToStudentsT()
    {
        DistributionFamily scaled = ObjectFamilies.Find("tLocationScale")!;
        Assert.Equal(ContinuousDistributions.TCdf(1.3, 7), scaled.Cdf(1.3, [0, 1, 7]), 12);
        Assert.Equal(ContinuousDistributions.TPdf(1.3, 7), scaled.Pdf(1.3, [0, 1, 7]), 12);
    }

    [Fact]
    public void TheHalfNormalIsTheNormalFoldedAtItsLocation()
    {
        DistributionFamily half = ObjectFamilies.Find("HalfNormal")!;
        Assert.Equal(
            (2 * ContinuousDistributions.NormalCdf(1.4, 0, 2)) - 1, half.Cdf(1.4, [0, 2]), 12);
        Assert.Equal(0, half.Cdf(-0.5, [0, 2]), 12);
    }

    // --- The stable family ------------------------------------------------------------------------

    [Fact]
    public void TheStableFamilyAnswersItsThreeClosedFormMembers()
    {
        // A stability index of two is a normal with twice the variance of its scale; one with no
        // skewness is a Cauchy. Both are answered in closed form, so these pin the parameterization
        // rather than the quadrature.
        Assert.Equal(
            ContinuousDistributions.NormalCdf(1.3, 2, 3 * Math.Sqrt(2)),
            StableDistribution.Cdf(1.3, 2, 0, 3, 2), 12);
        Assert.Equal(0.75, StableDistribution.Cdf(1, 1, 0, 1, 0), 12);

        // The Lévy — a stability index of one half, skewed all the way — has a closed form that the
        // quadrature knows nothing about, so this one checks the integral itself.
        for (double x = 0.2; x < 6; x += 0.4)
        {
            double levy = SpecialLevyCdf(x + 1);
            Assert.Equal(levy, StableDistribution.Cdf(x, 0.5, 1, 1, 0), 6);
        }
    }

    /// <summary>
    /// The Lévy distribution function, written directly. In the zero-parameterization a scale of one
    /// puts the support edge at minus one, which is why the argument is shifted before it gets here.
    /// </summary>
    private static double SpecialLevyCdf(double x) =>
        x <= 0 ? 0 : JGraph.Numerics.SpecialFunctions.Erfc(Math.Sqrt(1 / (2 * x)));

    [Fact]
    public void TheStableDensityIntegratesToOneAndMatchesItsOwnDistributionFunction()
    {
        foreach ((double alpha, double beta) in new[] { (1.7, 0.5), (0.8, -0.4), (1.0, 0.6), (1.3, 0.0) })
        {
            // Integrating over a window rather than the line, and comparing against the two
            // probabilities at its ends: a tail this heavy leaves real mass outside any window, so
            // "does it come to one" would only be measuring where the window was put.
            double lower = StableDistribution.Cdf(-1.5, alpha, beta, 1, 0);
            double upper = StableDistribution.Cdf(2.5, alpha, beta, 1, 0);
            double between = 0;
            for (double x = -1.5; x < 2.5; x += 1e-4)
            {
                between += StableDistribution.Pdf(x + 5e-5, alpha, beta, 1, 0) * 1e-4;
            }

            Assert.True(Math.Abs(between - (upper - lower)) < 2e-4,
                $"alpha {alpha}, beta {beta}: the density held {between} where the two probabilities differ by {upper - lower}.");

            // And the distribution function does reach both ends, which is what says no mass went
            // missing where the quadrature could not see it.
            Assert.True(StableDistribution.Cdf(-1e12, alpha, beta, 1, 0) < 1e-6);
            Assert.True(StableDistribution.Cdf(1e12, alpha, beta, 1, 0) > 1 - 1e-6);
        }
    }

    [Fact]
    public void TheStableFamilyIsContinuousAsTheStabilityIndexPassesThroughOne()
    {
        // Continuity there is the whole reason this parameterization was chosen over the older one,
        // and the extra term it carries at exactly one is easy to leave out unnoticed.
        // A scale away from one is the whole point: at a scale of one the two candidate
        // parameterizations agree, so the mistake this catches would be invisible.
        foreach (double scale in new[] { 2.0, 0.4 })
        {
            double below = StableDistribution.Cdf(0.4, 0.9999, 0.7, scale, 1);
            double at = StableDistribution.Cdf(0.4, 1, 0.7, scale, 1);
            double above = StableDistribution.Cdf(0.4, 1.0001, 0.7, scale, 1);
            Assert.True(Math.Abs(below - at) < 1e-4, $"scale {scale}: {below} against {at}");
            Assert.True(Math.Abs(above - at) < 1e-4, $"scale {scale}: {above} against {at}");

            double slopeBelow = StableDistribution.Pdf(0.4, 0.9999, 0.7, scale, 1);
            double slopeAt = StableDistribution.Pdf(0.4, 1, 0.7, scale, 1);
            Assert.True(Math.Abs(slopeBelow - slopeAt) < 1e-4, $"scale {scale}: {slopeBelow} against {slopeAt}");
        }
    }

    [Theory]
    [InlineData(1.4, 0.5, 2.0)]
    [InlineData(0.7, -0.6, 1.0)]

    // The stability index of exactly one is its own transformation, and the printings of it disagree
    // over one factor inside a logarithm — which displaces the whole sample by a constant and can only
    // be seen by comparing it against the distribution function.
    [InlineData(1.0, 0.7, 2.0)]
    [InlineData(1.0, 0.0, 0.4)]
    public void AStableSampleReproducesItsOwnDistributionFunction(double alpha, double beta, double scale)
    {
        var random = new Random(4711);
        const int Draws = 40000;
        int below = 0;
        for (int i = 0; i < Draws; i++)
        {
            if (StableDistribution.Sample(random, alpha, beta, scale, 1) <= 3)
            {
                below++;
            }
        }

        double expected = StableDistribution.Cdf(3, alpha, beta, scale, 1);
        Assert.True(Math.Abs(((double)below / Draws) - expected) < 0.012,
            $"the sample put {(double)below / Draws} below three where the distribution function says {expected}.");
    }

    // --- The three built per instance ---------------------------------------------------------------

    [Fact]
    public void AMultinomialIsItsOwnProbabilities()
    {
        DistributionFamily family = ObjectFamilies.Multinomial(3);
        double[] p = [0.2, 0.5, 0.3];

        Assert.Equal(0.5, family.Pdf(2, p), 12);
        Assert.Equal(0, family.Pdf(2.5, p), 12);
        Assert.Equal(0.7, family.Cdf(2, p), 12);
        Assert.Equal(2, family.Inv(0.5, p), 12);
        Assert.Equal(2.1, family.Stat(p).Mean, 12);
    }

    [Fact]
    public void AKernelFitIsWiderThanTheSampleItSmoothed()
    {
        double[] data = [1, 2, 3, 4, 5];
        DistributionFamily family = ObjectFamilies.Kernel(data.Length, "normal");
        double[] parameters = [0.5, .. data];

        Assert.Equal(3, family.Stat(parameters).Mean, 12);

        // The sample's own spread is 2; smoothing adds the kernel's, which is the width squared.
        Assert.Equal(2 + 0.25, family.Stat(parameters).Variance, 12);
        Assert.Equal(0.5, family.Cdf(3, parameters), 10);
        Assert.Equal(3, family.Inv(0.5, parameters), 6);
    }

    [Fact]
    public void APiecewiseLinearDistributionRisesBetweenItsBreakpoints()
    {
        DistributionFamily family = ObjectFamilies.PiecewiseLinear(3);
        double[] parameters = [0, 1, 3, 0, 0.5, 1];

        Assert.Equal(0.25, family.Cdf(0.5, parameters), 12);
        Assert.Equal(0.75, family.Cdf(2, parameters), 12);
        Assert.Equal(0.5, family.Pdf(0.5, parameters), 12);
        Assert.Equal(0.25, family.Pdf(2, parameters), 12);
        Assert.Equal(1, family.Inv(0.5, parameters), 12);

        // Uniform on nothing: the mean of the two straight pieces, worked out by hand.
        Assert.Equal((0.5 * 0.5) + (0.5 * 2), family.Stat(parameters).Mean, 12);
    }
}
