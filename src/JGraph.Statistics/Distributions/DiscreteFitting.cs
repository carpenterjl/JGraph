using JGraph.Statistics.Optimize;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// M53 wave D: maximum likelihood for the discrete families.
/// </summary>
/// <remarks>
/// This is deliberately not the continuous fitter with a different density substituted in. Two of the
/// three estimates here have exact intervals — Clopper and Pearson's for a proportion, and the
/// chi-square one for a rate — and an exact interval on a count of three successes is a different
/// answer from the normal approximation the asymptotic machinery would give, not a rounder version of
/// the same one. Only the negative binomial, whose shape has no closed form at all, falls back on
/// searching and on the curvature of the likelihood at the answer.
/// </remarks>
public static class DiscreteFitting
{
    /// <summary>
    /// The proportion of successes and Clopper and Pearson's exact interval for it: the interval whose
    /// ends are the success probabilities that would make this many successes the alpha/2 tail.
    /// </summary>
    public static (double Estimate, double Lower, double Upper) BinomialProportion(
        double successes, double trials, double alpha)
    {
        if (double.IsNaN(successes) || double.IsNaN(trials) || trials <= 0
            || successes < 0 || successes > trials)
        {
            return (double.NaN, double.NaN, double.NaN);
        }

        double estimate = successes / trials;
        double lower = successes == 0
            ? 0
            : ContinuousDistributions.BetaInv(alpha / 2, successes, trials - successes + 1);
        double upper = successes == trials
            ? 1
            : ContinuousDistributions.BetaInv(1 - (alpha / 2), successes + 1, trials - successes);

        return (estimate, lower, upper);
    }

    /// <summary>
    /// The mean count and the exact chi-square interval for it. The sum of Poisson counts is itself
    /// Poisson, and a Poisson tail is a chi-square tail, which is what makes an exact interval
    /// available here where most families have only an asymptotic one.
    /// </summary>
    public static (double Estimate, double Lower, double Upper) PoissonRate(
        double total, double observations, double alpha)
    {
        if (double.IsNaN(total) || total < 0 || observations <= 0)
        {
            return (double.NaN, double.NaN, double.NaN);
        }

        double estimate = total / observations;
        double lower = total == 0
            ? 0
            : ContinuousDistributions.Chi2Inv(alpha / 2, 2 * total) / (2 * observations);
        double upper = ContinuousDistributions.Chi2Inv(1 - (alpha / 2), 2 * (total + 1)) / (2 * observations);
        return (estimate, lower, upper);
    }

    /// <summary>
    /// The geometric success probability. The likelihood has a closed maximum — one success per trial
    /// actually run — and the interval comes from the curvature of the likelihood there.
    /// </summary>
    public static DistributionFitting.FitOutcome Geometric(in DistributionFitting.Sample sample, double alpha)
    {
        double count = sample.Count;
        double total = WeightedTotal(sample);
        if (count <= 0)
        {
            throw new ArgumentException("A geometric fit needs at least one observation.", nameof(sample));
        }

        double estimate = 1 / (1 + (total / count));
        DistributionFamily family = DiscreteFamilies.Find("Geometric")!;
        return Interval(family, [estimate], sample, alpha);
    }

    /// <summary>
    /// The negative binomial's shape and success probability. Given the shape, the probability that
    /// maximizes the likelihood is fixed by the sample mean, so only the shape is actually searched
    /// for — and it is searched on a logarithmic scale, where a simplex cannot step it negative.
    /// </summary>
    public static DistributionFitting.FitOutcome NegativeBinomial(
        in DistributionFitting.Sample sample, double alpha)
    {
        double count = sample.Count;
        if (count <= 0)
        {
            throw new ArgumentException(
                "A negative binomial fit needs at least one observation.", nameof(sample));
        }

        double mean = WeightedTotal(sample) / count;
        if (mean <= 0)
        {
            throw new ArgumentException(
                "A negative binomial fit needs at least one non-zero observation.", nameof(sample));
        }

        DistributionFamily family = DiscreteFamilies.Find("Negative Binomial")!;
        DistributionFitting.Sample local = sample;

        double Profile(double[] logShape)
        {
            double shape = Math.Exp(logShape[0]);
            if (!double.IsFinite(shape) || shape <= 0)
            {
                return double.PositiveInfinity;
            }

            return DistributionFitting.NegativeLogLikelihood(family, [shape, shape / (shape + mean)], local);
        }

        // The method of moments gives the starting shape wherever the data is over-dispersed enough for
        // it to be positive; where it is not, the likelihood climbs towards a Poisson and any modest
        // start walks the same way.
        double variance = WeightedVariance(sample, mean);
        double start = variance > mean ? (mean * mean) / (variance - mean) : 10;

        NelderMead.Result found = NelderMead.Minimize(
            Profile, [Math.Log(Math.Max(1e-3, start))],
            new NelderMead.Settings(MaxIterations: 400, MaxEvaluations: 800));

        double shape = Math.Exp(found.Solution[0]);
        return Interval(family, [shape, shape / (shape + mean)], sample, alpha);
    }

    /// <summary>The estimate plus a normal interval read off the curvature of its likelihood.</summary>
    private static DistributionFitting.FitOutcome Interval(
        DistributionFamily family, double[] estimate, in DistributionFitting.Sample sample, double alpha)
    {
        double[,] covariance = DistributionFitting.AsymptoticCovariance(family, estimate, sample);
        double z = ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);

        var lower = new double[estimate.Length];
        var upper = new double[estimate.Length];
        for (int i = 0; i < estimate.Length; i++)
        {
            double variance = covariance[i, i];
            double half = variance > 0 ? z * Math.Sqrt(variance) : double.NaN;
            lower[i] = estimate[i] - half;
            upper[i] = estimate[i] + half;
        }

        return new DistributionFitting.FitOutcome(estimate, lower, upper);
    }

    /// <summary>The sum of the observations, counting each as many times as its frequency says.</summary>
    public static double WeightedTotal(in DistributionFitting.Sample sample)
    {
        double total = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            total += sample.Frequency[i] * sample.Values[i];
        }

        return total;
    }

    private static double WeightedVariance(in DistributionFitting.Sample sample, double mean)
    {
        double count = sample.Count;
        if (count <= 1)
        {
            return 0;
        }

        double total = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            double gap = sample.Values[i] - mean;
            total += sample.Frequency[i] * gap * gap;
        }

        return total / (count - 1);
    }
}
