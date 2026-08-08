using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Sampling;

/// <summary>
/// Confidence intervals read off a set of bootstrap replicates.
/// </summary>
/// <remarks>
/// The resampling loop itself is not here: a bootstrap statistic is whatever function the caller
/// passed, which in this build is a script-level function handle, so the loop belongs where handles
/// can be called. What is here is the part that is arithmetic — turning a cloud of replicates into two
/// numbers — and the four ways of doing it differ enough to be worth naming.
/// </remarks>
public static class Bootstrap
{
    /// <summary>How the two ends of the interval are found.</summary>
    public enum IntervalMethod
    {
        /// <summary>The plain percentiles of the replicates.</summary>
        Percentile,

        /// <summary>
        /// Percentiles shifted so that the median replicate sits at the observed statistic, which
        /// corrects a bootstrap distribution that is off-centre.
        /// </summary>
        BiasCorrected,

        /// <summary>The observed statistic, less its bootstrap bias, plus and minus a normal margin.</summary>
        Normal,

        /// <summary>
        /// Bias-corrected and accelerated: the shift above, plus a skewness correction read off the
        /// jackknife, which is the default because it is right to a higher order than the others.
        /// </summary>
        Accelerated,
    }

    /// <summary>
    /// The interval at confidence <c>1 − alpha</c>.
    /// </summary>
    /// <param name="method">Which interval.</param>
    /// <param name="replicates">The statistic recomputed on each resample.</param>
    /// <param name="observed">The statistic on the data itself.</param>
    /// <param name="jackknife">
    /// The statistic with each observation left out, needed only by
    /// <see cref="IntervalMethod.Accelerated"/>.
    /// </param>
    /// <param name="alpha">One minus the confidence level.</param>
    public static (double Lower, double Upper) Interval(
        IntervalMethod method, double[] replicates, double observed, double[]? jackknife, double alpha)
    {
        ArgumentNullException.ThrowIfNull(replicates);
        if (replicates.Length == 0)
        {
            return (double.NaN, double.NaN);
        }

        if (method == IntervalMethod.Normal)
        {
            double mean = 0;
            foreach (double value in replicates)
            {
                mean += value;
            }

            mean /= replicates.Length;

            double square = 0;
            foreach (double value in replicates)
            {
                square += (value - mean) * (value - mean);
            }

            double error = replicates.Length > 1 ? Math.Sqrt(square / (replicates.Length - 1)) : 0;
            double centre = observed - (mean - observed);
            double margin = ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1) * error;
            return (centre - margin, centre + margin);
        }

        double lowerProbability = alpha / 2;
        double upperProbability = 1 - (alpha / 2);

        if (method != IntervalMethod.Percentile)
        {
            double below = 0;
            foreach (double value in replicates)
            {
                if (value < observed)
                {
                    below++;
                }
            }

            // Every replicate on one side of the statistic leaves the correction undefined; the plain
            // percentiles are then the honest answer rather than an infinite shift.
            double fraction = below / replicates.Length;
            if (fraction <= 0 || fraction >= 1)
            {
                return Percentiles(replicates, lowerProbability, upperProbability);
            }

            double bias = ContinuousDistributions.NormalInv(fraction, 0, 1);
            double acceleration = method == IntervalMethod.Accelerated ? Acceleration(jackknife) : 0;

            lowerProbability = Adjusted(bias, acceleration, alpha / 2);
            upperProbability = Adjusted(bias, acceleration, 1 - (alpha / 2));
        }

        return Percentiles(replicates, lowerProbability, upperProbability);
    }

    /// <summary>
    /// The skewness correction, from how much the statistic moves as each observation is left out.
    /// </summary>
    public static double Acceleration(double[]? jackknife)
    {
        if (jackknife is null || jackknife.Length < 2)
        {
            return 0;
        }

        double mean = 0;
        foreach (double value in jackknife)
        {
            mean += value;
        }

        mean /= jackknife.Length;

        double squares = 0;
        double cubes = 0;
        foreach (double value in jackknife)
        {
            double gap = mean - value;
            squares += gap * gap;
            cubes += gap * gap * gap;
        }

        double denominator = 6 * Math.Pow(squares, 1.5);
        return denominator > 0 ? cubes / denominator : 0;
    }

    private static double Adjusted(double bias, double acceleration, double probability)
    {
        double z = ContinuousDistributions.NormalInv(probability, 0, 1);
        double shifted = bias + z;
        double scaled = bias + (shifted / (1 - (acceleration * shifted)));
        return Math.Clamp(ContinuousDistributions.NormalCdf(scaled, 0, 1), 0, 1);
    }

    private static (double Lower, double Upper) Percentiles(double[] replicates, double lower, double upper)
    {
        double[] ends = DescriptiveStatistics.Percentiles(replicates, [100 * lower, 100 * upper]);
        return (ends[0], ends[1]);
    }
}
