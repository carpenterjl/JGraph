using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave C: maximum likelihood fitting. The test that matters is parameter recovery — a sample
/// drawn from known parameters has to be fitted back to them — because a fitter that returns its own
/// starting point passes every smoke test and fails this one.
/// </summary>
public class DistributionFittingTests
{
    private static DistributionFitting.Sample Draw(string name, double[] parameters, int n, int seed)
    {
        DistributionFamily family = ContinuousFamilies.Find(name)!;
        var random = new Random(seed);
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = family.Sample(random, parameters);
        }

        return DistributionFitting.MakeSample(values, null, null);
    }

    [Theory]
    [InlineData("Normal", 3.0, 2.0)]
    [InlineData("Exponential", 4.0, 0.0)]
    [InlineData("Gamma", 3.0, 2.0)]
    [InlineData("Beta", 2.0, 5.0)]
    [InlineData("Weibull", 2.0, 3.0)]
    [InlineData("Lognormal", 0.5, 0.75)]
    [InlineData("Extreme Value", 1.0, 2.0)]
    [InlineData("Rayleigh", 2.0, 0.0)]
    public void FittingRecoversTheParametersASampleWasDrawnFrom(string name, double a, double b)
    {
        DistributionFamily family = ContinuousFamilies.Find(name)!;
        double[] truth = new[] { a, b }[..family.ParameterCount];
        DistributionFitting.Sample sample = Draw(name, truth, 4000, 424242);

        DistributionFitting.FitOutcome fit = DistributionFitting.Fit(family, sample, 0.05);

        for (int i = 0; i < truth.Length; i++)
        {
            // Ten percent of the true value: comfortably inside the sampling error of four thousand
            // draws, and far outside anything a fitter that had not moved would produce.
            Assert.True(Math.Abs(fit.Parameters[i] - truth[i]) < 0.1 * Math.Abs(truth[i]),
                $"{name} parameter {family.ParameterNames[i]}: fitted {fit.Parameters[i]}, true {truth[i]}");

            // And the interval has to contain what was fitted, in the right order.
            Assert.True(fit.Lower[i] <= fit.Parameters[i] && fit.Parameters[i] <= fit.Upper[i],
                $"{name} interval [{fit.Lower[i]}, {fit.Upper[i]}] excludes {fit.Parameters[i]}");
        }
    }

    /// <summary>
    /// The normal's estimate and interval are the exact textbook ones, not an asymptotic
    /// approximation, so they can be written out in full and compared exactly.
    /// </summary>
    [Fact]
    public void TheNormalFitIsTheExactClosedForm()
    {
        double[] x = [2, 4, 4, 4, 5, 5, 7, 9];
        DistributionFitting.Sample sample = DistributionFitting.MakeSample(x, null, null);
        DistributionFitting.FitOutcome fit = DistributionFitting.Fit(
            ContinuousFamilies.Find("Normal")!, sample, 0.05);

        Assert.Equal(5, fit.Parameters[0], 12);
        Assert.Equal(Math.Sqrt(32.0 / 7.0), fit.Parameters[1], 12);

        double half = ContinuousDistributions.TInv(0.975, 7) * fit.Parameters[1] / Math.Sqrt(8);
        Assert.Equal(5 - half, fit.Lower[0], 10);
        Assert.Equal(5 + half, fit.Upper[0], 10);
        Assert.Equal(fit.Parameters[1] * Math.Sqrt(7 / ContinuousDistributions.Chi2Inv(0.975, 7)),
            fit.Lower[1], 10);
    }

    /// <summary>
    /// A censored observation says only that the value is at least this large, so it must pull the
    /// estimate up. Treating it as an exact observation — the mistake a fitter that ignores the
    /// censoring vector makes — pulls it down instead.
    /// </summary>
    [Fact]
    public void CensoringPullsTheEstimateUpwards()
    {
        double[] x = [1, 2, 3, 4, 5];
        DistributionFamily exponential = ContinuousFamilies.Find("Exponential")!;

        DistributionFitting.FitOutcome plain = DistributionFitting.Fit(
            exponential, DistributionFitting.MakeSample(x, null, null), 0.05);
        DistributionFitting.FitOutcome censored = DistributionFitting.Fit(
            exponential, DistributionFitting.MakeSample(x, [0, 0, 0, 1, 1], null), 0.05);

        Assert.Equal(3, plain.Parameters[0], 12);

        // With two of the five only known to exceed their value, the mean of the underlying
        // exponential is the total time divided by the three failures actually seen.
        Assert.Equal(5, censored.Parameters[0], 4);
        Assert.True(censored.Parameters[0] > plain.Parameters[0]);
    }

    /// <summary>
    /// A frequency vector is a compressed sample, so fitting the compressed form and fitting the
    /// expanded one have to give the same answer.
    /// </summary>
    [Fact]
    public void FrequenciesCountObservationsRatherThanWeightingThem()
    {
        DistributionFamily normal = ContinuousFamilies.Find("Normal")!;

        DistributionFitting.FitOutcome compressed = DistributionFitting.Fit(
            normal, DistributionFitting.MakeSample([1, 2, 3], [0, 0, 0], [1, 3, 2]), 0.05);
        DistributionFitting.FitOutcome expanded = DistributionFitting.Fit(
            normal, DistributionFitting.MakeSample([1, 2, 2, 2, 3, 3], null, null), 0.05);

        Assert.Equal(expanded.Parameters[0], compressed.Parameters[0], 12);
        Assert.Equal(expanded.Parameters[1], compressed.Parameters[1], 12);
        Assert.Equal(expanded.Lower[0], compressed.Lower[0], 10);
    }

    /// <summary>
    /// The uniform's estimates are the observed extremes, and the true endpoints can only lie further
    /// out — so its confidence interval is one-sided on each side rather than centred.
    /// </summary>
    [Fact]
    public void TheUniformIntervalOnlyReachesOutwards()
    {
        DistributionFitting.FitOutcome fit = DistributionFitting.Fit(
            ContinuousFamilies.Find("Uniform")!,
            DistributionFitting.MakeSample([2, 3, 5, 7, 9], null, null), 0.05);

        Assert.Equal(2, fit.Parameters[0], 12);
        Assert.Equal(9, fit.Parameters[1], 12);
        Assert.True(fit.Lower[0] < 2);
        Assert.Equal(9, fit.Lower[1], 12);
        Assert.Equal(2, fit.Upper[0], 12);
        Assert.True(fit.Upper[1] > 9);
    }

    [Fact]
    public void TheLikelihoodIsLowestAtTheEstimate()
    {
        DistributionFamily gamma = ContinuousFamilies.Find("Gamma")!;
        DistributionFitting.Sample sample = Draw("Gamma", [3, 2], 500, 99);
        DistributionFitting.FitOutcome fit = DistributionFitting.Fit(gamma, sample, 0.05);

        double best = DistributionFitting.NegativeLogLikelihood(gamma, fit.Parameters, sample);
        Assert.True(best < DistributionFitting.NegativeLogLikelihood(gamma, [fit.Parameters[0] * 1.2, fit.Parameters[1]], sample));
        Assert.True(best < DistributionFitting.NegativeLogLikelihood(gamma, [fit.Parameters[0], fit.Parameters[1] * 0.8], sample));

        // The asymptotic covariance is a real covariance: positive on the diagonal and symmetric.
        double[,] covariance = DistributionFitting.AsymptoticCovariance(gamma, fit.Parameters, sample);
        Assert.True(covariance[0, 0] > 0);
        Assert.True(covariance[1, 1] > 0);
        Assert.Equal(covariance[0, 1], covariance[1, 0], 10);
    }

    [Fact]
    public void TheGeneralizedFamiliesAreFittedToo()
    {
        DistributionFitting.Sample paretoSample = Draw("Generalized Pareto", [0.2, 2, 0], 4000, 7);
        DistributionFitting.FitOutcome pareto = DistributionFitting.Fit(
            ContinuousFamilies.Find("Generalized Pareto")!, paretoSample, 0.05);
        Assert.Equal(0.2, pareto.Parameters[0], 1);
        Assert.Equal(2, pareto.Parameters[1], 1);

        DistributionFitting.Sample extremeSample = Draw("Generalized Extreme Value", [0.1, 1.5, 3], 4000, 11);
        DistributionFitting.FitOutcome extreme = DistributionFitting.Fit(
            ContinuousFamilies.Find("Generalized Extreme Value")!, extremeSample, 0.05);
        Assert.Equal(0.1, extreme.Parameters[0], 1);
        Assert.Equal(1.5, extreme.Parameters[1], 1);
        Assert.Equal(3, extreme.Parameters[2], 1);
    }
}
