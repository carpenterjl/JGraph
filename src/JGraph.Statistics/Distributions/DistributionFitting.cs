using JGraph.Numerics;
using JGraph.Statistics.Optimize;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// Maximum likelihood fitting for the continuous families: the parameter estimates the <c>*fit</c>
/// names return, the confidence intervals that come with them, and the negative log-likelihood and
/// asymptotic covariance the <c>*like</c> names report.
/// </summary>
/// <remarks>
/// <para>
/// Two things decide how a family is fitted. Where the estimate has a closed form and MATLAB
/// publishes an exact confidence interval — the normal, exponential, lognormal, Rayleigh and uniform
/// — that exact interval is what comes back, because an asymptotic approximation to an interval that
/// is available exactly would be wrong on small samples in a way nobody would notice. Everything else
/// is maximized numerically and its interval read off the observed information.
/// </para>
/// <para>
/// Censoring changes that: an observation that is only known to exceed its recorded value contributes
/// its survival probability rather than its density, and none of the closed forms survive it. So the
/// moment a censoring vector is present, every family goes through the same numerical path — which is
/// the reason the log-likelihood here is written once, for all families, rather than per family.
/// </para>
/// </remarks>
public static class DistributionFitting
{
    /// <summary>What a fit returns.</summary>
    /// <param name="Parameters">The estimates, in the family's own parameter order.</param>
    /// <param name="Lower">The lower confidence limit of each parameter.</param>
    /// <param name="Upper">The upper confidence limit of each parameter.</param>
    public readonly record struct FitOutcome(double[] Parameters, double[] Lower, double[] Upper);

    /// <summary>The observations, with whichever of censoring and frequency the caller supplied.</summary>
    /// <param name="Values">The observations themselves.</param>
    /// <param name="Censored">
    /// True where the observation is right-censored — known only to be at least that large.
    /// </param>
    /// <param name="Frequency">How many times each observation is counted.</param>
    public readonly record struct Sample(double[] Values, bool[] Censored, double[] Frequency)
    {
        /// <summary>Whether anything is censored, which is what forces the numerical path.</summary>
        public bool HasCensoring
        {
            get
            {
                foreach (bool censored in Censored)
                {
                    if (censored) return true;
                }

                return false;
            }
        }

        /// <summary>The total weight, which is the sample size once frequencies are counted.</summary>
        public double Count
        {
            get
            {
                double total = 0;
                foreach (double f in Frequency)
                {
                    total += f;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// Builds a sample, filling in the defaults for whatever was not supplied: nothing censored and
    /// every observation counted once.
    /// </summary>
    /// <exception cref="ArgumentException">A supplied vector is the wrong length.</exception>
    public static Sample MakeSample(
        IReadOnlyList<double> values, IReadOnlyList<double>? censoring, IReadOnlyList<double>? frequency)
    {
        ArgumentNullException.ThrowIfNull(values);

        int n = values.Count;
        if (censoring is not null && censoring.Count != n)
        {
            throw new ArgumentException("The censoring vector must be as long as the data.", nameof(censoring));
        }

        if (frequency is not null && frequency.Count != n)
        {
            throw new ArgumentException("The frequency vector must be as long as the data.", nameof(frequency));
        }

        var kept = new List<double>(n);
        var censored = new List<bool>(n);
        var counts = new List<double>(n);

        for (int i = 0; i < n; i++)
        {
            double f = frequency is null ? 1 : frequency[i];
            if (double.IsNaN(values[i]) || double.IsNaN(f) || f <= 0)
            {
                continue;
            }

            kept.Add(values[i]);
            censored.Add(censoring is not null && censoring[i] != 0);
            counts.Add(f);
        }

        return new Sample([.. kept], [.. censored], [.. counts]);
    }

    /// <summary>
    /// The negative log-likelihood of <paramref name="parameters"/> under <paramref name="family"/>.
    /// A censored observation contributes the log of its survival probability, which is the whole
    /// difference between this and a sum of log densities.
    /// </summary>
    public static double NegativeLogLikelihood(
        DistributionFamily family, double[] parameters, in Sample sample)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(parameters);

        double total = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            double contribution;
            if (sample.Censored[i])
            {
                double survival = 1 - family.Cdf(sample.Values[i], parameters);
                contribution = survival <= 0 ? double.NegativeInfinity : Math.Log(survival);
            }
            else
            {
                double density = family.Pdf(sample.Values[i], parameters);
                contribution = density <= 0 ? double.NegativeInfinity : Math.Log(density);
            }

            if (double.IsNaN(contribution) || double.IsNegativeInfinity(contribution))
            {
                return double.PositiveInfinity;
            }

            total += sample.Frequency[i] * contribution;
        }

        return -total;
    }

    /// <summary>
    /// The asymptotic covariance of the estimates: the inverse of the observed information, which is
    /// the second-derivative matrix of the negative log-likelihood at the estimate. The derivatives
    /// are central differences, since no family here has a written-down Hessian.
    /// </summary>
    public static double[,] AsymptoticCovariance(
        DistributionFamily family, double[] parameters, in Sample sample, bool[]? held = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(parameters);

        Sample local = sample;
        int[] free = FreeIndices(parameters.Length, held);
        double[] point = new double[free.Length];
        for (int i = 0; i < free.Length; i++)
        {
            point[i] = parameters[free[i]];
        }

        double[,] hessian = Hessian(
            p =>
            {
                var full = (double[])parameters.Clone();
                for (int i = 0; i < free.Length; i++)
                {
                    full[free[i]] = p[i];
                }

                return NegativeLogLikelihood(family, full, local);
            },
            point);

        double[,] small = Invert(hessian);
        if (free.Length == parameters.Length)
        {
            return small;
        }

        // A held parameter has no sampling variance to report, so its row and column say so rather
        // than reporting a zero that would read as perfect certainty.
        var full = Filled(parameters.Length, double.NaN);
        for (int i = 0; i < free.Length; i++)
        {
            for (int j = 0; j < free.Length; j++)
            {
                full[free[i], free[j]] = small[i, j];
            }
        }

        return full;
    }

    /// <summary>
    /// The asymptotic covariance of a maximum likelihood estimate whose likelihood is not one of the
    /// families here — the one <c>mle</c> needs when the caller supplies the density themselves.
    /// </summary>
    /// <param name="negativeLogLikelihood">The objective that was minimized.</param>
    /// <param name="at">The estimate it was minimized to.</param>
    public static double[,] ObservedCovariance(Func<double[], double> negativeLogLikelihood, double[] at)
    {
        ArgumentNullException.ThrowIfNull(negativeLogLikelihood);
        ArgumentNullException.ThrowIfNull(at);
        return Invert(Hessian(negativeLogLikelihood, at));
    }

    /// <summary>
    /// Fits <paramref name="family"/> to <paramref name="sample"/> and reports the confidence
    /// interval at level 1 − <paramref name="alpha"/>.
    /// </summary>
    /// <param name="family">The distribution to fit.</param>
    /// <param name="sample">The observations.</param>
    /// <param name="alpha">One minus the confidence level wanted.</param>
    /// <param name="held">
    /// Which parameters are known rather than estimated, and so stay at the value the family's
    /// starting point puts them at. This is what makes <c>gpfit</c> the two-parameter fit MathWorks
    /// documents: its threshold is held at zero, and letting it float instead would drive it to the
    /// smallest observation and report a likelihood that is unbounded there.
    /// </param>
    public static FitOutcome Fit(
        DistributionFamily family, in Sample sample, double alpha, bool[]? held = null)
    {
        ArgumentNullException.ThrowIfNull(family);

        if (!sample.HasCensoring && held is null)
        {
            FitOutcome? exact = FitExactly(family, sample, alpha);
            if (exact is not null)
            {
                return exact.Value;
            }
        }

        double[] estimate = Maximize(family, sample, held);
        return WithAsymptoticInterval(family, estimate, sample, alpha, held);
    }

    /// <summary>Which parameter slots are being estimated.</summary>
    private static int[] FreeIndices(int count, bool[]? held)
    {
        var free = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (held is null || i >= held.Length || !held[i])
            {
                free.Add(i);
            }
        }

        return [.. free];
    }

    /// <summary>
    /// The families whose estimate and interval are both available in closed form, or null when this
    /// one is not among them.
    /// </summary>
    private static FitOutcome? FitExactly(DistributionFamily family, in Sample sample, double alpha)
    {
        double n = sample.Count;
        if (n < 1)
        {
            return null;
        }

        switch (family.Prefix)
        {
            case "norm":
            {
                (double mean, double deviation) = WeightedMoments(sample);
                if (n < 2)
                {
                    return new FitOutcome([mean, deviation], [mean, 0], [mean, double.PositiveInfinity]);
                }

                double t = ContinuousDistributions.TInv(1 - (alpha / 2), n - 1);
                double half = t * deviation / Math.Sqrt(n);
                double lowChi = ContinuousDistributions.Chi2Inv(1 - (alpha / 2), n - 1);
                double highChi = ContinuousDistributions.Chi2Inv(alpha / 2, n - 1);
                return new FitOutcome(
                    [mean, deviation],
                    [mean - half, deviation * Math.Sqrt((n - 1) / lowChi)],
                    [mean + half, deviation * Math.Sqrt((n - 1) / highChi)]);
            }

            case "logn":
            {
                var logs = new double[sample.Values.Length];
                for (int i = 0; i < logs.Length; i++)
                {
                    if (sample.Values[i] <= 0)
                    {
                        return null;
                    }

                    logs[i] = Math.Log(sample.Values[i]);
                }

                var onLogs = new Sample(logs, sample.Censored, sample.Frequency);
                DistributionFamily normal = ContinuousFamilies.Find("Normal")!;
                return FitExactly(normal, onLogs, alpha);
            }

            case "exp":
            {
                double mean = WeightedMean(sample);
                double lowChi = ContinuousDistributions.Chi2Inv(1 - (alpha / 2), 2 * n);
                double highChi = ContinuousDistributions.Chi2Inv(alpha / 2, 2 * n);
                return new FitOutcome([mean], [2 * n * mean / lowChi], [2 * n * mean / highChi]);
            }

            case "rayl":
            {
                double sumSquares = 0;
                for (int i = 0; i < sample.Values.Length; i++)
                {
                    sumSquares += sample.Frequency[i] * sample.Values[i] * sample.Values[i];
                }

                double b = Math.Sqrt(sumSquares / (2 * n));
                double lowChi = ContinuousDistributions.Chi2Inv(1 - (alpha / 2), 2 * n);
                double highChi = ContinuousDistributions.Chi2Inv(alpha / 2, 2 * n);
                return new FitOutcome(
                    [b], [b * Math.Sqrt(2 * n / lowChi)], [b * Math.Sqrt(2 * n / highChi)]);
            }

            case "unif":
            {
                double low = double.PositiveInfinity;
                double high = double.NegativeInfinity;
                foreach (double value in sample.Values)
                {
                    low = Math.Min(low, value);
                    high = Math.Max(high, value);
                }

                // The estimates are the extremes themselves, so the interval is one-sided on each: the
                // true endpoints can only be further out than what was observed.
                double spread = (high - low) / Math.Pow(alpha, 1 / n);
                return new FitOutcome([low, high], [high - spread, high], [low, low + spread]);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Maximizes the likelihood numerically, from a moment-based starting point. Parameters that must
    /// stay positive are searched as their logarithms, which is what lets an unconstrained simplex be
    /// used without it ever proposing a negative scale.
    /// </summary>
    /// <summary>
    /// The maximizing parameters with one or more slots pinned at values the caller chose, which is
    /// what a profile likelihood is: the best the other parameters can do while this one is held.
    /// </summary>
    /// <param name="family">The distribution.</param>
    /// <param name="sample">The observations.</param>
    /// <param name="pinned">A value per parameter, or null in the slots left free.</param>
    public static double[] MaximizeGiven(DistributionFamily family, in Sample sample, double?[] pinned)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(pinned);

        var held = new bool[pinned.Length];
        for (int i = 0; i < pinned.Length; i++)
        {
            held[i] = pinned[i] is not null;
        }

        return Maximize(family, sample, held, pinned);
    }

    private static double[] Maximize(
        DistributionFamily family, in Sample sample, bool[]? held, double?[]? pinned = null)
    {
        double[] start = StartingPoint(family, sample);
        if (pinned is not null)
        {
            for (int i = 0; i < start.Length && i < pinned.Length; i++)
            {
                if (pinned[i] is double fixedAt)
                {
                    start[i] = fixedAt;
                }
            }
        }

        bool[] positive = family.PositiveParameters;
        int[] free = FreeIndices(start.Length, held);

        var encoded = new double[free.Length];
        for (int i = 0; i < free.Length; i++)
        {
            int slot = free[i];
            encoded[i] = positive[slot] ? Math.Log(Math.Max(start[slot], 1e-12)) : start[slot];
        }

        Sample local = sample;
        double[] Decode(double[] point)
        {
            var parameters = (double[])start.Clone();
            for (int i = 0; i < free.Length; i++)
            {
                int slot = free[i];
                parameters[slot] = positive[slot] ? Math.Exp(point[i]) : point[i];
            }

            return parameters;
        }

        NelderMead.Result result = NelderMead.Minimize(
            point => NegativeLogLikelihood(family, Decode(point), local),
            encoded,
            new NelderMead.Settings(MaxIterations: 2000, MaxEvaluations: 4000, ToleranceX: 1e-10, ToleranceFunction: 1e-10));

        return Decode(result.Solution);
    }

    /// <summary>
    /// A starting point good enough for the simplex to find the maximum from. Method of moments where
    /// the family has one, and a small refinement for the gamma and the Weibull, whose likelihood
    /// equations reduce to a single well-behaved root that Newton finds in a handful of steps.
    /// </summary>
    private static double[] StartingPoint(DistributionFamily family, in Sample sample)
    {
        (double mean, double deviation) = WeightedMoments(sample);
        double variance = deviation * deviation;
        if (!(variance > 0))
        {
            variance = Math.Max(Math.Abs(mean), 1) * 1e-6;
        }

        switch (family.Prefix)
        {
            case "norm":
                return [mean, Math.Sqrt(variance)];

            case "exp":
                return [Math.Max(mean, 1e-12)];

            case "rayl":
                return [Math.Max(mean / Math.Sqrt(Math.PI / 2), 1e-12)];

            case "gam":
                return GammaStart(sample, mean, variance);

            case "beta":
            {
                double m = Math.Clamp(mean, 1e-6, 1 - 1e-6);
                double scale = Math.Max((m * (1 - m) / variance) - 1, 1e-6);
                return [m * scale, (1 - m) * scale];
            }

            case "wbl":
                return WeibullStart(sample, mean, variance);

            case "logn":
            {
                double logMean = 0, logSecond = 0, weight = 0;
                for (int i = 0; i < sample.Values.Length; i++)
                {
                    if (sample.Values[i] <= 0) continue;
                    double l = Math.Log(sample.Values[i]);
                    logMean += sample.Frequency[i] * l;
                    logSecond += sample.Frequency[i] * l * l;
                    weight += sample.Frequency[i];
                }

                if (weight <= 0) return [0, 1];
                logMean /= weight;
                double logVariance = Math.Max((logSecond / weight) - (logMean * logMean), 1e-12);
                return [logMean, Math.Sqrt(logVariance)];
            }

            case "ev":
            {
                double sigma = Math.Sqrt(6 * variance) / Math.PI;
                return [mean + (sigma * ContinuousDistributions.EulerMascheroni), Math.Max(sigma, 1e-12)];
            }

            case "gev":
            {
                double sigma = Math.Sqrt(6 * variance) / Math.PI;
                return [0, Math.Max(sigma, 1e-12), mean - (sigma * ContinuousDistributions.EulerMascheroni)];
            }

            case "gp":
            {
                // MATLAB's gpfit holds the threshold at zero, so only the shape and scale are free —
                // but the family carries three parameters, and the third stays where it was put.
                double k = 0.5 * (1 - (mean * mean / variance));
                double sigma = 0.5 * mean * ((mean * mean / variance) + 1);
                return [k, Math.Max(sigma, 1e-12), 0];
            }

            default:
                return [mean, Math.Max(Math.Sqrt(variance), 1e-12)];
        }
    }

    /// <summary>
    /// The gamma's shape by Newton on <c>log a − ψ(a) = log x̄ − log x</c>, Thom's approximation for
    /// the first guess. The scale then follows from the mean.
    /// </summary>
    private static double[] GammaStart(in Sample sample, double mean, double variance)
    {
        double logMean = Math.Log(Math.Max(mean, 1e-300));
        double meanLog = 0, weight = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            if (sample.Values[i] <= 0) continue;
            meanLog += sample.Frequency[i] * Math.Log(sample.Values[i]);
            weight += sample.Frequency[i];
        }

        if (weight <= 0)
        {
            return [Math.Max(mean * mean / variance, 1e-6), Math.Max(variance / mean, 1e-12)];
        }

        meanLog /= weight;
        double s = logMean - meanLog;
        double a = s > 0
            ? (3 - s + Math.Sqrt(((s - 3) * (s - 3)) + (24 * s))) / (12 * s)
            : Math.Max(mean * mean / variance, 1e-6);

        for (int step = 0; step < 50 && s > 0; step++)
        {
            double f = Math.Log(a) - SpecialFunctions.Digamma(a) - s;
            double derivative = (1 / a) - SpecialFunctions.Polygamma(1, a);
            if (derivative == 0 || double.IsNaN(f)) break;

            double next = a - (f / derivative);
            if (!(next > 0)) break;
            if (Math.Abs(next - a) < 1e-12 * a)
            {
                a = next;
                break;
            }

            a = next;
        }

        return [Math.Max(a, 1e-6), Math.Max(mean / Math.Max(a, 1e-6), 1e-12)];
    }

    /// <summary>
    /// The Weibull's shape by Newton on the likelihood equation, then its scale from the shape. The
    /// first guess is the standard one from the spread of the logarithms.
    /// </summary>
    private static double[] WeibullStart(in Sample sample, double mean, double variance)
    {
        double meanLog = 0, secondLog = 0, weight = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            if (sample.Values[i] <= 0) continue;
            double l = Math.Log(sample.Values[i]);
            meanLog += sample.Frequency[i] * l;
            secondLog += sample.Frequency[i] * l * l;
            weight += sample.Frequency[i];
        }

        if (weight <= 0)
        {
            return [Math.Max(mean, 1e-12), 1];
        }

        meanLog /= weight;
        double logVariance = Math.Max((secondLog / weight) - (meanLog * meanLog), 1e-12);
        double b = Math.PI / Math.Sqrt(6 * logVariance);

        for (int step = 0; step < 100; step++)
        {
            double sum = 0, sumLog = 0, sumLogSquare = 0;
            for (int i = 0; i < sample.Values.Length; i++)
            {
                if (sample.Values[i] <= 0) continue;
                double l = Math.Log(sample.Values[i]);
                double p = sample.Frequency[i] * Math.Pow(sample.Values[i], b);
                sum += p;
                sumLog += p * l;
                sumLogSquare += p * l * l;
            }

            if (!(sum > 0)) break;

            double f = (sumLog / sum) - (1 / b) - meanLog;
            double derivative = (sumLogSquare / sum) - ((sumLog / sum) * (sumLog / sum)) + (1 / (b * b));
            if (derivative == 0) break;

            double next = b - (f / derivative);
            if (!(next > 0)) break;
            if (Math.Abs(next - b) < 1e-12 * b)
            {
                b = next;
                break;
            }

            b = next;
        }

        double scaleSum = 0, scaleWeight = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            if (sample.Values[i] <= 0) continue;
            scaleSum += sample.Frequency[i] * Math.Pow(sample.Values[i], b);
            scaleWeight += sample.Frequency[i];
        }

        double a = scaleWeight > 0 ? Math.Pow(scaleSum / scaleWeight, 1 / b) : mean;
        return [Math.Max(a, 1e-12), Math.Max(b, 1e-12)];
    }

    /// <summary>
    /// Attaches an asymptotic normal confidence interval to an estimate. A parameter that has to stay
    /// positive gets its interval on the logarithmic scale and exponentiated back, so the lower limit
    /// is positive however wide the interval is.
    /// </summary>
    private static FitOutcome WithAsymptoticInterval(
        DistributionFamily family, double[] estimate, in Sample sample, double alpha, bool[]? held)
    {
        double[,] covariance = AsymptoticCovariance(family, estimate, sample, held);
        double z = ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);

        var lower = new double[estimate.Length];
        var upper = new double[estimate.Length];
        for (int i = 0; i < estimate.Length; i++)
        {
            double variance = covariance[i, i];
            double error = variance > 0 ? Math.Sqrt(variance) : double.NaN;

            if (family.PositiveParameters[i] && estimate[i] > 0)
            {
                double half = z * error / estimate[i];
                lower[i] = estimate[i] * Math.Exp(-half);
                upper[i] = estimate[i] * Math.Exp(half);
            }
            else
            {
                lower[i] = estimate[i] - (z * error);
                upper[i] = estimate[i] + (z * error);
            }
        }

        return new FitOutcome(estimate, lower, upper);
    }

    /// <summary>The weighted mean of the observations.</summary>
    private static double WeightedMean(in Sample sample)
    {
        double total = 0, weight = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            total += sample.Frequency[i] * sample.Values[i];
            weight += sample.Frequency[i];
        }

        return weight > 0 ? total / weight : double.NaN;
    }

    /// <summary>The weighted mean and the unbiased standard deviation around it.</summary>
    private static (double Mean, double Deviation) WeightedMoments(in Sample sample)
    {
        double mean = WeightedMean(sample);
        double weight = sample.Count;
        if (!(weight > 1))
        {
            return (mean, 0);
        }

        double sum = 0;
        for (int i = 0; i < sample.Values.Length; i++)
        {
            double d = sample.Values[i] - mean;
            sum += sample.Frequency[i] * d * d;
        }

        return (mean, Math.Sqrt(sum / (weight - 1)));
    }

    /// <summary>
    /// The second-derivative matrix of <paramref name="objective"/> by central differences, with a
    /// step scaled to each coordinate so a parameter in the thousands and one near zero are both
    /// differenced sensibly.
    /// </summary>
    private static double[,] Hessian(Func<double[], double> objective, double[] at)
    {
        int n = at.Length;
        var steps = new double[n];
        for (int i = 0; i < n; i++)
        {
            steps[i] = Math.Max(Math.Abs(at[i]), 1e-4) * 1e-4;
        }

        double centre = objective(at);
        var hessian = new double[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = i; j < n; j++)
            {
                double value;
                if (i == j)
                {
                    double up = objective(Shift(at, i, steps[i]));
                    double down = objective(Shift(at, i, -steps[i]));
                    value = (up - (2 * centre) + down) / (steps[i] * steps[i]);
                }
                else
                {
                    double upUp = objective(Shift(Shift(at, i, steps[i]), j, steps[j]));
                    double upDown = objective(Shift(Shift(at, i, steps[i]), j, -steps[j]));
                    double downUp = objective(Shift(Shift(at, i, -steps[i]), j, steps[j]));
                    double downDown = objective(Shift(Shift(at, i, -steps[i]), j, -steps[j]));
                    value = (upUp - upDown - downUp + downDown) / (4 * steps[i] * steps[j]);
                }

                hessian[i, j] = value;
                hessian[j, i] = value;
            }
        }

        return hessian;
    }

    /// <summary>A copy of <paramref name="point"/> with one coordinate moved.</summary>
    private static double[] Shift(double[] point, int index, double by)
    {
        var moved = (double[])point.Clone();
        moved[index] += by;
        return moved;
    }

    /// <summary>
    /// Inverts a small symmetric matrix by Gauss–Jordan with partial pivoting, answering all NaN if it
    /// is singular — which is what a likelihood surface that is flat in some direction produces, and
    /// is a truer answer than a huge number from a nearly singular solve.
    /// </summary>
    private static double[,] Invert(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var work = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                work[i, j] = matrix[i, j];
            }

            work[i, n + i] = 1;
        }

        for (int column = 0; column < n; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < n; row++)
            {
                if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (!(Math.Abs(work[pivot, column]) > 1e-300))
            {
                return Filled(n, double.NaN);
            }

            if (pivot != column)
            {
                for (int j = 0; j < 2 * n; j++)
                {
                    (work[column, j], work[pivot, j]) = (work[pivot, j], work[column, j]);
                }
            }

            double scale = work[column, column];
            for (int j = 0; j < 2 * n; j++)
            {
                work[column, j] /= scale;
            }

            for (int row = 0; row < n; row++)
            {
                if (row == column) continue;
                double factor = work[row, column];
                if (factor == 0) continue;
                for (int j = 0; j < 2 * n; j++)
                {
                    work[row, j] -= factor * work[column, j];
                }
            }
        }

        var inverse = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                inverse[i, j] = work[i, n + j];
            }
        }

        return inverse;
    }

    /// <summary>An n-by-n matrix of one repeated value.</summary>
    private static double[,] Filled(int n, double value)
    {
        var filled = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                filled[i, j] = value;
            }
        }

        return filled;
    }
}
