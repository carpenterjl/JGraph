using JGraph.Statistics.Distributions;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave I: what a distribution object adds over the family it wraps, which is truncation. The
/// checks are identities the arithmetic has to satisfy — a renormalized density integrates to one,
/// a truncated quantile inverts a truncated distribution function, a conditional mean matches the
/// closed form where one exists — rather than numbers copied from anywhere.
/// </summary>
public class DistributionObjectTests
{
    private static DistributionObject Normal(double mu = 0, double sigma = 1) =>
        new(ContinuousFamilies.Find("Normal")!, [mu, sigma]);

    [Fact]
    public void AnUntruncatedObjectIsExactlyItsFamily()
    {
        DistributionObject pd = Normal(3, 2);
        Assert.False(pd.IsTruncated);
        Assert.Equal(1, pd.Retained, 12);
        Assert.Equal(ContinuousDistributions.NormalPdf(4, 3, 2), pd.Pdf(4), 12);
        Assert.Equal(ContinuousDistributions.NormalCdf(4, 3, 2), pd.Cdf(4), 12);
        Assert.Equal(3, pd.Mean(), 12);
        Assert.Equal(2, pd.Deviation(), 12);
    }

    [Fact]
    public void TruncationRenormalizesTheDensityAndRemapsTheQuantile()
    {
        DistributionObject pd = Normal().Truncate(-1, 1);
        double retained = ContinuousDistributions.NormalCdf(1, 0, 1) - ContinuousDistributions.NormalCdf(-1, 0, 1);

        Assert.Equal(retained, pd.Retained, 12);
        Assert.Equal(ContinuousDistributions.NormalPdf(0, 0, 1) / retained, pd.Pdf(0), 12);
        Assert.Equal(0, pd.Cdf(-1), 12);
        Assert.Equal(1, pd.Cdf(1), 12);
        Assert.Equal(0.5, pd.Cdf(0), 12);
        Assert.Equal(0, pd.Pdf(-1.5), 12);

        for (double p = 0.05; p < 1; p += 0.05)
        {
            Assert.Equal(p, pd.Cdf(pd.Inv(p)), 10);
        }
    }

    [Fact]
    public void TheTruncatedMomentsMatchTheirClosedForm()
    {
        DistributionObject pd = Normal().Truncate(-1, 1);
        double retained = ContinuousDistributions.NormalCdf(1, 0, 1) - ContinuousDistributions.NormalCdf(-1, 0, 1);

        // A symmetric interval about the mean leaves the mean where it was, and the variance has a
        // closed form the quadrature knows nothing about.
        Assert.Equal(0, pd.Mean(), 8);
        Assert.Equal(1 - (2 * ContinuousDistributions.NormalPdf(1, 0, 1) / retained), pd.Variance(), 7);
    }

    [Fact]
    public void ANormalTruncatedAtItsMeanIsAHalfNormal()
    {
        // An independent statement of the same distribution: two families that must agree, and the
        // code paths to them share nothing but the normal density.
        DistributionObject truncated = Normal(0, 2).Truncate(0, double.PositiveInfinity);
        DistributionFamily half = ObjectFamilies.Find("HalfNormal")!;

        Assert.Equal(half.Stat([0, 2]).Mean, truncated.Mean(), 6);
        Assert.Equal(Math.Sqrt(half.Stat([0, 2]).Variance), truncated.Deviation(), 5);
        for (double x = 0.5; x < 6; x += 0.5)
        {
            Assert.Equal(half.Cdf(x, [0, 2]), truncated.Cdf(x), 10);
            Assert.Equal(half.Pdf(x, [0, 2]), truncated.Pdf(x), 10);
        }
    }

    [Fact]
    public void ATruncatedDiscreteDistributionKeepsTheMassOnItsEndpoints()
    {
        var poisson = new DistributionObject(DiscreteFamilies.Find("Poisson")!, [4]);
        DistributionObject kept = poisson.Truncate(2, 6);

        double total = 0;
        double mean = 0;
        for (int k = 2; k <= 6; k++)
        {
            total += kept.Pdf(k);
            mean += k * kept.Pdf(k);
        }

        Assert.Equal(1, total, 12);
        Assert.Equal(mean, kept.Mean(), 10);

        // The lower endpoint is inside the interval, so its own mass survives — the distribution
        // function at the lower limit is that mass and not zero, which is the one place a discrete
        // truncation differs from a continuous one.
        Assert.True(kept.Pdf(2) > 0);
        Assert.Equal(kept.Pdf(2), kept.Cdf(2), 12);
        Assert.Equal(0, kept.Cdf(1), 12);
    }

    [Fact]
    public void ATruncatedDrawStaysInsideTheInterval()
    {
        DistributionObject pd = Normal(0, 1).Truncate(1.5, 2);
        var random = new Random(90210);
        double total = 0;
        const int Draws = 5000;
        for (int i = 0; i < Draws; i++)
        {
            double draw = pd.Sample(random);
            Assert.InRange(draw, 1.5, 2);
            total += draw;
        }

        // Inversion rather than rejection, so a tail holding under a hundredth of the mass costs the
        // same as any other interval — and the average lands where the conditional mean says.
        Assert.True(Math.Abs((total / Draws) - pd.Mean()) < 0.02);
    }

    [Fact]
    public void TruncatingCountsInTheLikelihood()
    {
        DistributionFitting.Sample sample = DistributionFitting.MakeSample([0.2, 0.4, 0.5, 0.7], null, null);
        DistributionObject whole = Normal(0.5, 1);
        DistributionObject kept = whole.Truncate(0, 1);

        // Conditioning throws probability away, so every observation is more likely under the
        // truncated distribution than under the whole one: the negative log-likelihood must fall.
        Assert.True(kept.NegativeLogLikelihood(sample) < whole.NegativeLogLikelihood(sample));
        Assert.Equal(
            whole.NegativeLogLikelihood(sample) + (4 * Math.Log(kept.Retained)),
            kept.NegativeLogLikelihood(sample),
            10);
    }

    [Fact]
    public void TruncatingTwiceNarrowsFurther()
    {
        DistributionObject once = Normal().Truncate(-2, 2);
        DistributionObject twice = once.Truncate(-0.5, 0.5);

        Assert.Equal(-0.5, twice.Lower, 12);
        Assert.Equal(0.5, twice.Upper, 12);
        Assert.True(twice.Variance() < once.Variance());

        // A record, so the first object is untouched — which is what makes these value objects
        // rather than handles, and what a script relies on when it keeps both.
        Assert.Equal(-2, once.Lower, 12);
    }

    [Fact]
    public void AnIntervalHoldingNoProbabilityIsRefusedRatherThanAnswered()
    {
        var uniform = new DistributionObject(ContinuousFamilies.Find("Uniform")!, [0, 1]);
        DistributionObject empty = uniform.Truncate(5, 6);
        Assert.Equal(0, empty.Retained, 12);
        Assert.True(double.IsNaN(empty.Pdf(5.5)));

        Assert.Throws<ArgumentException>(() => uniform.Truncate(1, 0));
    }
}
