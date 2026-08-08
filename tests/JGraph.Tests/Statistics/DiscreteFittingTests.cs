using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave D: maximum likelihood for the discrete families. Two of the three intervals here are
/// exact rather than asymptotic, so they can be checked against the definition that produced them
/// — a Clopper–Pearson end really is the success probability at which this many successes sits in
/// the tail — rather than only against a published number.
/// </summary>
public class DiscreteFittingTests
{
    private static DistributionFitting.Sample Draw(string name, double[] parameters, int n, int seed)
    {
        DistributionFamily family = DiscreteFamilies.Find(name)!;
        var random = new Random(seed);
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = family.Sample(random, parameters);
        }

        return DistributionFitting.MakeSample(values, null, null);
    }

    [Fact]
    public void TheBinomialIntervalIsClopperAndPearsons()
    {
        (double estimate, double lower, double upper) = DiscreteFitting.BinomialProportion(45, 100, 0.05);

        Assert.Equal(0.45, estimate, 12);
        Assert.Equal(0.3503, lower, 4);
        Assert.Equal(0.5527, upper, 4);

        // The definition the ends come from: at the lower end, forty-five or more successes has
        // probability alpha/2; at the upper end, forty-five or fewer does.
        Assert.Equal(0.025, DiscreteDistributions.BinomialUpper(44, 100, lower), 9);
        Assert.Equal(0.025, DiscreteDistributions.BinomialCdf(45, 100, upper), 9);
    }

    /// <summary>
    /// No success at all still bounds the probability from above, and the interval has to say so by
    /// running from exactly zero rather than by reporting nothing.
    /// </summary>
    [Fact]
    public void TheBinomialIntervalIsOneSidedAtTheExtremes()
    {
        (double none, double noneLower, double noneUpper) = DiscreteFitting.BinomialProportion(0, 20, 0.05);
        Assert.Equal(0, none);
        Assert.Equal(0, noneLower);
        Assert.Equal(1 - Math.Pow(0.025, 1.0 / 20), noneUpper, 9);

        (double all, double allLower, double allUpper) = DiscreteFitting.BinomialProportion(20, 20, 0.05);
        Assert.Equal(1, all);
        Assert.Equal(Math.Pow(0.025, 1.0 / 20), allLower, 9);
        Assert.Equal(1, allUpper);
    }

    [Fact]
    public void ThePoissonIntervalIsTheExactChiSquareOne()
    {
        (double estimate, double lower, double upper) = DiscreteFitting.PoissonRate(15, 5, 0.05);

        Assert.Equal(3, estimate, 12);
        Assert.True(lower < 3 && 3 < upper);

        // The same definition read back: at the lower rate, fifteen or more events in five
        // observations has probability alpha/2.
        Assert.Equal(0.025, DiscreteDistributions.PoissonUpper(14, 5 * lower), 9);
        Assert.Equal(0.025, DiscreteDistributions.PoissonCdf(15, 5 * upper), 9);

        // No events at all bounds the rate from above only.
        (double none, double noneLower, _) = DiscreteFitting.PoissonRate(0, 4, 0.05);
        Assert.Equal(0, none);
        Assert.Equal(0, noneLower);
    }

    [Theory]
    [InlineData("Poisson", 4.5, 0.0)]
    [InlineData("Geometric", 0.3, 0.0)]
    [InlineData("Negative Binomial", 4.0, 0.35)]
    public void FittingRecoversTheParametersASampleWasDrawnFrom(string name, double a, double b)
    {
        DistributionFamily family = DiscreteFamilies.Find(name)!;
        double[] truth = new[] { a, b }[..family.ParameterCount];
        DistributionFitting.Sample sample = Draw(name, truth, 4000, 987654);

        DistributionFitting.FitOutcome fit = name switch
        {
            "Poisson" => PoissonOutcome(sample),
            "Geometric" => DiscreteFitting.Geometric(sample, 0.05),
            _ => DiscreteFitting.NegativeBinomial(sample, 0.05),
        };

        for (int i = 0; i < truth.Length; i++)
        {
            Assert.True(Math.Abs(fit.Parameters[i] - truth[i]) < 0.1 * truth[i],
                $"{name} parameter {family.ParameterNames[i]}: fitted {fit.Parameters[i]}, true {truth[i]}");
            Assert.True(fit.Lower[i] <= fit.Parameters[i] && fit.Parameters[i] <= fit.Upper[i],
                $"{name} interval [{fit.Lower[i]}, {fit.Upper[i]}] excludes {fit.Parameters[i]}");
        }
    }

    private static DistributionFitting.FitOutcome PoissonOutcome(in DistributionFitting.Sample sample)
    {
        (double estimate, double lower, double upper) =
            DiscreteFitting.PoissonRate(DiscreteFitting.WeightedTotal(sample), sample.Count, 0.05);
        return new DistributionFitting.FitOutcome([estimate], [lower], [upper]);
    }

    /// <summary>
    /// A negative binomial fit has to beat the Poisson that its shape running away to infinity would
    /// give, on data that really is over-dispersed. This is the check a fitter that returned its own
    /// starting point would fail.
    /// </summary>
    [Fact]
    public void TheNegativeBinomialFitBeatsTheStartItWasGiven()
    {
        DistributionFitting.Sample sample = Draw("Negative Binomial", [2, 0.25], 2000, 4242);
        DistributionFitting.FitOutcome fit = DiscreteFitting.NegativeBinomial(sample, 0.05);
        DistributionFamily family = DiscreteFamilies.Find("Negative Binomial")!;

        double best = DistributionFitting.NegativeLogLikelihood(family, fit.Parameters, sample);
        double shape = fit.Parameters[0];
        double mean = shape * (1 - fit.Parameters[1]) / fit.Parameters[1];

        foreach (double factor in new[] { 0.7, 1.4 })
        {
            double other = shape * factor;
            double[] moved = [other, other / (other + mean)];
            Assert.True(best < DistributionFitting.NegativeLogLikelihood(family, moved, sample),
                $"the fit at shape {shape} is not better than the one at {other}.");
        }
    }

    /// <summary>A frequency vector is a compressed sample, not a set of weights.</summary>
    [Fact]
    public void FrequenciesCountObservations()
    {
        DistributionFitting.Sample compressed = DistributionFitting.MakeSample([1, 2, 3], null, [1, 3, 2]);
        DistributionFitting.Sample expanded = DistributionFitting.MakeSample([1, 2, 2, 2, 3, 3], null, null);

        Assert.Equal(
            DiscreteFitting.PoissonRate(DiscreteFitting.WeightedTotal(expanded), expanded.Count, 0.05),
            DiscreteFitting.PoissonRate(DiscreteFitting.WeightedTotal(compressed), compressed.Count, 0.05));

        Assert.Equal(
            DiscreteFitting.Geometric(expanded, 0.05).Parameters[0],
            DiscreteFitting.Geometric(compressed, 0.05).Parameters[0], 12);
    }

    /// <summary>
    /// The geometric estimate is one success for every trial actually run, which is a closed form and
    /// can be written out.
    /// </summary>
    [Fact]
    public void TheGeometricEstimateIsOneSuccessPerTrialRun()
    {
        DistributionFitting.Sample sample = DistributionFitting.MakeSample([0, 1, 2, 3, 4], null, null);
        DistributionFitting.FitOutcome fit = DiscreteFitting.Geometric(sample, 0.05);

        // Five successes over five successes plus ten failures.
        Assert.Equal(5.0 / 15, fit.Parameters[0], 12);
        Assert.True(fit.Lower[0] < fit.Parameters[0] && fit.Parameters[0] < fit.Upper[0]);
    }
}
