using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave C: the continuous distribution kernels. Three kinds of check, because no one of them
/// catches everything — values MATLAB publishes, identities a plausible wrong implementation fails,
/// and for the noncentral families a seeded sample, which is an implementation of the distribution
/// that shares no code with the series being tested.
/// </summary>
public class ContinuousDistributionTests
{
    [Fact]
    public void NormalMatchesThePublishedTable()
    {
        Assert.Equal(0.975002104852, ContinuousDistributions.NormalCdf(1.96, 0, 1), 10);
        Assert.Equal(1.959963984540, ContinuousDistributions.NormalInv(0.975, 0, 1), 10);
        Assert.Equal(0.398942280401, ContinuousDistributions.NormalPdf(0, 0, 1), 10);

        // The far tail is the reason the distribution function is written on erfc rather than on
        // 1 + erf: at eight deviations the second form has lost every significant figure. Checked
        // relatively, because an absolute tolerance at this size is meaningless.
        double tail = ContinuousDistributions.NormalCdf(-8, 0, 1);
        Assert.True(Math.Abs((tail / 6.22096057427e-16) - 1) < 1e-9, $"far tail was {tail}");
    }

    [Fact]
    public void StudentsTMatchesThePublishedTable()
    {
        Assert.Equal(2.228138851986, ContinuousDistributions.TInv(0.975, 10), 10);
        Assert.Equal(0.975, ContinuousDistributions.TCdf(2.228138851986, 10), 10);
        Assert.Equal(0.5, ContinuousDistributions.TCdf(0, 7), 12);

        // Symmetry is not built into the formula, so it is worth asserting.
        Assert.Equal(1 - ContinuousDistributions.TCdf(1.3, 5), ContinuousDistributions.TCdf(-1.3, 5), 12);
    }

    [Fact]
    public void ChiSquareAndFMatchThePublishedTables()
    {
        Assert.Equal(7.814727903251, ContinuousDistributions.Chi2Inv(0.95, 3), 9);
        Assert.Equal(3.708265, ContinuousDistributions.FInv(0.95, 3, 10), 6);
        Assert.Equal(0.95, ContinuousDistributions.FCdf(3.708265, 3, 10), 6);
    }

    [Fact]
    public void BetaAgreesWithTheBinomialSumItEquals()
    {
        // For whole-numbered parameters the regularized incomplete beta is a binomial tail, so this
        // value is exact: I(0.5; 2, 3) is the chance of at least two heads in four tosses.
        Assert.Equal(11.0 / 16.0, ContinuousDistributions.BetaCdf(0.5, 2, 3), 12);
        Assert.Equal(0.5, ContinuousDistributions.BetaCdf(0.5, 3, 3), 12);
        Assert.Equal(1.5, ContinuousDistributions.BetaPdf(0.5, 2, 2), 12);
    }

    /// <summary>
    /// MATLAB's exponential, gamma and Weibull parameterizations are the ones most easily got
    /// backwards, so each is pinned against the closed form written out in full.
    /// </summary>
    [Fact]
    public void TheScaleParameterizationsAreMatlabs()
    {
        // The exponential's parameter is the mean, so a mean of 2 puts less than half the mass below 1.
        Assert.Equal(1 - Math.Exp(-0.5), ContinuousDistributions.ExponentialCdf(1, 2), 12);
        Assert.Equal(2, ContinuousDistributions.ExponentialStat(2).Mean, 12);

        // The gamma's second argument is the scale, so shape 2 scale 3 has mean 6 and not 2/3.
        Assert.Equal(6, ContinuousDistributions.GammaStat(2, 3).Mean, 12);
        Assert.Equal(18, ContinuousDistributions.GammaStat(2, 3).Variance, 12);

        // The Weibull takes the scale first and the shape second.
        Assert.Equal(1 - Math.Exp(-4), ContinuousDistributions.WeibullCdf(2, 1, 2), 12);
        Assert.Equal(1 - Math.Exp(-0.25), ContinuousDistributions.WeibullCdf(1, 2, 2), 12);

        // A chi-square is a gamma of shape v/2 and scale 2, which is the definition the code leans on.
        Assert.Equal(ContinuousDistributions.GammaCdf(3, 2, 2), ContinuousDistributions.Chi2Cdf(3, 4), 12);
    }

    /// <summary>
    /// MATLAB's <c>ev</c> is the distribution of the <em>smallest</em> extreme, whose long tail runs
    /// to the left. The largest-extreme reading has the same shape mirrored, so a test on the density
    /// alone at one point would not tell them apart — the skew is what does.
    /// </summary>
    [Fact]
    public void ExtremeValueIsTheSmallestExtremeReading()
    {
        // The tail that runs a long way is the left one: four below the location the density is still
        // ordinary, while four above it has all but vanished.
        Assert.True(ContinuousDistributions.ExtremeValuePdf(-4, 0, 1)
            > 1e10 * ContinuousDistributions.ExtremeValuePdf(4, 0, 1));

        // The mean sits below the location parameter, which is only true of the smallest-extreme form.
        Assert.Equal(-ContinuousDistributions.EulerMascheroni,
            ContinuousDistributions.ExtremeValueStat(0, 1).Mean, 12);
        Assert.Equal(Math.PI * Math.PI / 6, ContinuousDistributions.ExtremeValueStat(0, 1).Variance, 12);

        // Its relation to the Weibull: log of a Weibull variable is an extreme value variable.
        Assert.Equal(ContinuousDistributions.WeibullCdf(Math.Exp(0.3), Math.Exp(1), 2),
            ContinuousDistributions.ExtremeValueCdf(0.3, 1, 0.5), 12);
    }

    [Fact]
    public void TheGeneralizedFamiliesReduceToTheirLimitsAtZeroShape()
    {
        // A generalized Pareto of zero shape is an exponential displaced to the threshold.
        Assert.Equal(ContinuousDistributions.ExponentialCdf(1.5, 2),
            ContinuousDistributions.GeneralizedParetoCdf(1.5, 0, 2, 0), 12);

        // A generalized extreme value of zero shape is the Gumbel, whose mean is above its location.
        Assert.Equal(ContinuousDistributions.EulerMascheroni,
            ContinuousDistributions.GeneralizedExtremeValueStat(0, 1, 0).Mean, 12);

        // Approaching zero shape has to approach the same answer, which is what says the two branches
        // are the same distribution rather than two that happen to share a name.
        Assert.Equal(
            ContinuousDistributions.GeneralizedExtremeValueCdf(1.5, 0, 2, 1),
            ContinuousDistributions.GeneralizedExtremeValueCdf(1.5, 1e-9, 2, 1), 6);
        Assert.Equal(
            ContinuousDistributions.GeneralizedParetoPdf(1.5, 0, 2, 0),
            ContinuousDistributions.GeneralizedParetoPdf(1.5, 1e-9, 2, 0), 6);
    }

    /// <summary>
    /// A negative generalized Pareto shape gives a support with a finite upper end, and a positive
    /// generalized extreme value shape gives one with a finite lower end. Both are easy to get wrong
    /// in the direction that makes the distribution function leave its range.
    /// </summary>
    [Fact]
    public void TheGeneralizedFamiliesRespectTheirFiniteEnds()
    {
        // k = -0.5, sigma = 1: the support ends at theta - sigma/k = 2.
        Assert.Equal(1, ContinuousDistributions.GeneralizedParetoCdf(2, -0.5, 1, 0), 12);
        Assert.Equal(1, ContinuousDistributions.GeneralizedParetoCdf(50, -0.5, 1, 0), 12);
        Assert.Equal(0, ContinuousDistributions.GeneralizedParetoPdf(50, -0.5, 1, 0), 12);
        Assert.Equal(2, ContinuousDistributions.GeneralizedParetoInv(1, -0.5, 1, 0), 12);

        // k = 0.5, sigma = 1, mu = 0: nothing below mu - sigma/k = -2.
        Assert.Equal(0, ContinuousDistributions.GeneralizedExtremeValueCdf(-3, 0.5, 1, 0), 12);
        Assert.Equal(-2, ContinuousDistributions.GeneralizedExtremeValueInv(0, 0.5, 1, 0), 12);
    }

    [Theory]
    [InlineData("Normal", 0.0, 1.0, 0.0)]
    [InlineData("Gamma", 2.5, 1.5, 0.0)]
    [InlineData("Beta", 2.0, 5.0, 0.0)]
    [InlineData("Weibull", 1.5, 2.5, 0.0)]
    [InlineData("Lognormal", 0.5, 0.75, 0.0)]
    [InlineData("Extreme Value", 1.0, 2.0, 0.0)]
    [InlineData("Rayleigh", 2.0, 0.0, 0.0)]
    [InlineData("t", 7.0, 0.0, 0.0)]
    [InlineData("F", 4.0, 9.0, 0.0)]
    [InlineData("Chi-square", 5.0, 0.0, 0.0)]
    [InlineData("Generalized Extreme Value", 0.2, 1.5, 3.0)]
    [InlineData("Generalized Pareto", -0.25, 2.0, 1.0)]
    [InlineData("Noncentral Chi-square", 4.0, 3.0, 0.0)]
    [InlineData("Noncentral T", 8.0, 1.5, 0.0)]
    [InlineData("Noncentral F", 4.0, 9.0, 2.0)]
    public void TheQuantileUndoesTheDistributionFunction(string name, double a, double b, double c)
    {
        DistributionFamily family = ContinuousFamilies.Find(name)!;
        double[] parameters = new[] { a, b, c }[..family.ParameterCount];

        foreach (double p in new[] { 0.01, 0.1, 0.25, 0.5, 0.75, 0.9, 0.99 })
        {
            double x = family.Inv(p, parameters);
            Assert.Equal(p, family.Cdf(x, parameters), 6);
        }
    }

    /// <summary>
    /// Every noncentral family has to become its central one when the noncentrality is zero. This is
    /// the cheapest test that the Poisson mixture is weighted correctly rather than merely converging
    /// to something.
    /// </summary>
    [Fact]
    public void TheNoncentralFamiliesReduceToTheCentralOnesAtZero()
    {
        foreach (double x in new[] { 0.5, 2.0, 6.0 })
        {
            Assert.Equal(ContinuousDistributions.Chi2Cdf(x, 4),
                ContinuousDistributions.NoncentralChi2Cdf(x, 4, 0), 12);
            Assert.Equal(ContinuousDistributions.Chi2Pdf(x, 4),
                ContinuousDistributions.NoncentralChi2Pdf(x, 4, 0), 12);
            Assert.Equal(ContinuousDistributions.FCdf(x, 3, 8),
                ContinuousDistributions.NoncentralFCdf(x, 3, 8, 0), 12);
            Assert.Equal(ContinuousDistributions.FPdf(x, 3, 8),
                ContinuousDistributions.NoncentralFPdf(x, 3, 8, 0), 12);
            Assert.Equal(ContinuousDistributions.TCdf(x, 6),
                ContinuousDistributions.NoncentralTCdf(x, 6, 0), 10);
            Assert.Equal(ContinuousDistributions.TCdf(-x, 6),
                ContinuousDistributions.NoncentralTCdf(-x, 6, 0), 10);
        }
    }

    /// <summary>
    /// The noncentral series and the sampler are two independent constructions of the same
    /// distribution, so a seeded sample checks the series against something that shares no code with
    /// it. The tolerance is the sampling error of a hundred thousand draws, not a numerical one.
    /// </summary>
    [Theory]
    [InlineData("Noncentral Chi-square", 4.0, 3.0, 0.0, 5.0)]
    [InlineData("Noncentral T", 8.0, 1.5, 0.0, 2.0)]
    [InlineData("Noncentral F", 4.0, 9.0, 2.0, 1.5)]
    public void TheNoncentralSeriesAgreesWithASeededSample(
        string name, double a, double b, double c, double at)
    {
        DistributionFamily family = ContinuousFamilies.Find(name)!;
        double[] parameters = new[] { a, b, c }[..family.ParameterCount];

        var random = new Random(20260807);
        const int draws = 100_000;
        int below = 0;
        for (int i = 0; i < draws; i++)
        {
            if (family.Sample(random, parameters) <= at)
            {
                below++;
            }
        }

        double sampled = (double)below / draws;
        double series = family.Cdf(at, parameters);
        Assert.True(Math.Abs(sampled - series) < 0.005, $"series {series}, sampled {sampled}");
    }

    [Fact]
    public void ImpossibleParametersAnswerNaNRatherThanThrowing()
    {
        Assert.True(double.IsNaN(ContinuousDistributions.NormalPdf(0, 0, -1)));
        Assert.True(double.IsNaN(ContinuousDistributions.GammaCdf(1, 0, 2)));
        Assert.True(double.IsNaN(ContinuousDistributions.BetaInv(0.5, 2, -3)));
        Assert.True(double.IsNaN(ContinuousDistributions.NormalInv(1.5, 0, 1)));
        Assert.True(double.IsNaN(ContinuousDistributions.UniformPdf(0.5, 3, 1)));
    }

    [Fact]
    public void NamesAreFoundHoweverTheyAreSpelt()
    {
        Assert.Equal("Generalized Extreme Value", ContinuousFamilies.Find("gev")!.Name);
        Assert.Equal("Generalized Extreme Value", ContinuousFamilies.Find("generalized extreme value")!.Name);
        Assert.Equal("Chi-square", ContinuousFamilies.Find("chisquare")!.Name);
        Assert.Equal("Normal", ContinuousFamilies.Find("NORMAL")!.Name);
        Assert.Null(ContinuousFamilies.Find("not a distribution"));
    }
}
