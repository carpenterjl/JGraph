using JGraph.Numerics;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// The distribution of the studentized range — the gap between the largest and smallest of several
/// independent normal means, divided by an independent estimate of their standard deviation.
/// </summary>
/// <remarks>
/// This is the one distribution the toolbox needs that has no name in the base library, and it is
/// what makes Tukey's comparison honest: comparing every pair of <c>k</c> means with a t interval
/// finds a difference at the 5% level far more often than 5% of the time, because the largest of
/// several gaps is not a typical gap. Its probability is a double integral — over the position of the
/// lowest mean, and over the scale the estimate of the standard deviation happens to take — and both
/// are done with the same Gauss–Legendre rule the multivariate probabilities use, over finite ranges
/// chosen from the quantiles of the distributions involved rather than guessed.
/// </remarks>
public static class StudentizedRange
{
    private const int RangeNodes = 96;
    private const int ScaleNodes = 64;

    /// <summary>The probability that the studentized range of <paramref name="groups"/> means is below
    /// <paramref name="q"/>, with <paramref name="df"/> degrees of freedom behind the scale estimate.
    /// An infinite <paramref name="df"/> means the standard deviation is known.</summary>
    public static double Probability(double q, int groups, double df)
    {
        if (groups < 2)
        {
            throw new ArgumentException("a range needs at least two means.");
        }

        if (!(q > 0))
        {
            return 0;
        }

        if (!double.IsFinite(df))
        {
            return RangeProbability(q, groups);
        }

        if (!(df > 0))
        {
            throw new ArgumentException("the degrees of freedom must be above zero.");
        }

        // The scale is the square root of a chi-square over its own degrees of freedom, and it is
        // concentrated: outside its own extreme quantiles the density contributes nothing, so those
        // quantiles are the integration limits rather than a guessed multiple of the mean.
        double low = Math.Sqrt(ContinuousDistributions.Chi2Inv(1e-12, df) / df);
        double high = Math.Sqrt(ContinuousDistributions.Chi2Inv(1 - 1e-12, df) / df);
        double constant = (df / 2 * Math.Log(df)) - SpecialFunctions.LogGamma(df / 2)
            - (((df / 2) - 1) * Math.Log(2));

        return GaussLegendre.Integrate(
            s =>
            {
                double density = Math.Exp(constant + ((df - 1) * Math.Log(s)) - (df * s * s / 2));
                return density * RangeProbability(q * s, groups);
            },
            low,
            high,
            ScaleNodes,
            panels: 8);
    }

    /// <summary>The critical value of the studentized range at the given upper-tail probability.</summary>
    public static double Critical(double alpha, int groups, double df)
    {
        double low = 0;
        double high = 100;
        for (int i = 0; i < 100; i++)
        {
            double middle = (low + high) / 2;
            if (1 - Probability(middle, groups, df) > alpha)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// The probability that the range of <paramref name="groups"/> standard normals is below
    /// <paramref name="w"/>: condition on the lowest of them and ask that the rest lie within
    /// <paramref name="w"/> above it.
    /// </summary>
    private static double RangeProbability(double w, int groups)
    {
        if (!(w > 0))
        {
            return 0;
        }

        double value = groups * GaussLegendre.Integrate(
            z =>
            {
                double band = ContinuousDistributions.NormalCdf(z + w, 0, 1)
                    - ContinuousDistributions.NormalCdf(z, 0, 1);
                return band <= 0 ? 0 : ContinuousDistributions.NormalPdf(z, 0, 1) * Math.Pow(band, groups - 1);
            },
            -9,
            9,
            RangeNodes,
            panels: 6);

        return Math.Clamp(value, 0, 1);
    }
}

/// <summary>
/// <c>multcompare</c>: every pair of estimates compared, with the interval widened by whichever rule
/// keeps the whole family of comparisons at the stated level.
/// </summary>
public static class MultipleComparison
{
    /// <summary>Which rule holds the family of comparisons at the stated level.</summary>
    public enum Correction
    {
        /// <summary>Tukey and Kramer's, from the studentized range. The default, and the least wide
        /// interval that is still honest about comparing every pair.</summary>
        TukeyKramer,

        /// <summary>Fisher's least significant difference — no correction at all.</summary>
        LeastSignificant,

        /// <summary>Bonferroni's: the level divided by the number of comparisons.</summary>
        Bonferroni,

        /// <summary>Dunn and Šidák's: the same idea, done exactly for independent comparisons.</summary>
        DunnSidak,

        /// <summary>Scheffé's, which covers every contrast and not only the pairwise ones.</summary>
        Scheffe,
    }

    /// <summary>One pair's comparison.</summary>
    /// <param name="First">The first estimate's index.</param>
    /// <param name="Second">The second's.</param>
    /// <param name="Lower">The lower confidence limit of the difference.</param>
    /// <param name="Estimate">The difference itself.</param>
    /// <param name="Upper">The upper confidence limit.</param>
    /// <param name="P">The probability of a difference this large, corrected for the whole family.</param>
    public readonly record struct Comparison(
        int First, int Second, double Lower, double Estimate, double Upper, double P);

    /// <summary>
    /// Compares every pair. The standard error of a pair is
    /// <paramref name="scale"/>·√(<c>wᵢ + wⱼ</c>), which is what lets one description cover an analysis
    /// of variance (scale = √MSE, w = 1/n) and the rank-based tests, whose scale is a different number
    /// but whose arithmetic is the same.
    /// </summary>
    /// <param name="estimates">The quantities being compared.</param>
    /// <param name="weights">Each estimate's variance weight, usually the reciprocal of its count.</param>
    /// <param name="scale">The common standard deviation the differences are measured in.</param>
    /// <param name="df">Its degrees of freedom, infinite where the scale was not estimated.</param>
    /// <param name="alpha">The level the family of comparisons is held at.</param>
    /// <param name="correction">Which rule holds it there.</param>
    public static Comparison[] Compare(
        double[] estimates, double[] weights, double scale, double df, double alpha, Correction correction)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        ArgumentNullException.ThrowIfNull(weights);
        if (estimates.Length != weights.Length || estimates.Length < 2)
        {
            throw new ArgumentException("comparing means needs at least two of them, each with a weight.");
        }

        if (!(alpha > 0) || !(alpha < 1))
        {
            throw new ArgumentException("the significance level must lie strictly between 0 and 1.");
        }

        int k = estimates.Length;
        int pairs = k * (k - 1) / 2;
        var comparisons = new List<Comparison>(pairs);

        double multiplier = correction switch
        {
            Correction.TukeyKramer => StudentizedRange.Critical(alpha, k, df) / Math.Sqrt(2),
            Correction.LeastSignificant => Quantile(1 - (alpha / 2), df),
            Correction.Bonferroni => Quantile(1 - (alpha / (2 * pairs)), df),
            Correction.DunnSidak => Quantile((1 + Math.Pow(1 - alpha, 1.0 / pairs)) / 2, df),
            _ => Math.Sqrt((k - 1) * FQuantile(1 - alpha, k - 1, df)),
        };

        for (int i = 0; i < k; i++)
        {
            for (int j = i + 1; j < k; j++)
            {
                double error = scale * Math.Sqrt(weights[i] + weights[j]);
                double difference = estimates[i] - estimates[j];
                double statistic = error > 0 ? Math.Abs(difference) / error : double.PositiveInfinity;

                double p = correction switch
                {
                    Correction.TukeyKramer => 1 - StudentizedRange.Probability(statistic * Math.Sqrt(2), k, df),
                    Correction.LeastSignificant => 2 * Tail(statistic, df),
                    Correction.Bonferroni => Math.Min(1, pairs * 2 * Tail(statistic, df)),
                    Correction.DunnSidak => 1 - Math.Pow(1 - (2 * Tail(statistic, df)), pairs),
                    _ => 1 - FCumulative(statistic * statistic / (k - 1), k - 1, df),
                };

                double half = multiplier * error;
                comparisons.Add(new Comparison(
                    i, j, difference - half, difference, difference + half, Math.Clamp(p, 0, 1)));
            }
        }

        return [.. comparisons];
    }

    // An infinite degrees of freedom means the scale was not estimated, so every t here is a z. The
    // three helpers below are the only places that has to be said.
    private static double Quantile(double p, double df) => double.IsFinite(df)
        ? ContinuousDistributions.TInv(p, df)
        : ContinuousDistributions.NormalInv(p, 0, 1);

    private static double Tail(double statistic, double df) => double.IsFinite(df)
        ? ContinuousDistributions.TCdf(-statistic, df)
        : ContinuousDistributions.NormalCdf(-statistic, 0, 1);

    private static double FQuantile(double p, double df1, double df2) => double.IsFinite(df2)
        ? ContinuousDistributions.FInv(p, df1, df2)
        : ContinuousDistributions.Chi2Inv(p, df1) / df1;

    private static double FCumulative(double x, double df1, double df2) => double.IsFinite(df2)
        ? ContinuousDistributions.FCdf(x, df1, df2)
        : ContinuousDistributions.Chi2Cdf(x * df1, df1);
}
