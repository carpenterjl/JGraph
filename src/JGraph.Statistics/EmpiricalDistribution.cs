using JGraph.Numerics;

namespace JGraph.Statistics;

/// <summary>
/// The distribution a sample is, rather than the distribution it came from: the empirical cumulative
/// distribution (with Kaplan-Meier's answer when some observations are censored), the histogram that
/// follows from one, and the kernel-smoothed density that turns a finite sample into a curve.
/// </summary>
public static class EmpiricalDistribution
{
    /// <summary>Which curve <see cref="Empirical"/> returns.</summary>
    public enum CurveKind
    {
        /// <summary>The proportion at or below each point.</summary>
        Cdf,

        /// <summary>The proportion still above each point.</summary>
        Survivor,

        /// <summary>The accumulated hazard, −log of the survivor function.</summary>
        CumulativeHazard,
    }

    /// <summary>The empirical distribution of a sample, with its confidence bounds.</summary>
    /// <param name="Values">The curve at each point, starting from the value before any event.</param>
    /// <param name="Points">Where the curve was evaluated; the first point repeats the smallest observation.</param>
    /// <param name="Lower">The lower confidence bound at each point.</param>
    /// <param name="Upper">The upper confidence bound at each point.</param>
    public readonly record struct EmpiricalCurve(
        double[] Values, double[] Points, double[] Lower, double[] Upper);

    /// <summary>
    /// The empirical distribution function of a sample. With no censoring this is the staircase that
    /// steps up by 1/n at each observation; with censoring it is the Kaplan-Meier estimator, which
    /// spreads the missing information over the observations that outlived it.
    /// </summary>
    /// <param name="values">The observations.</param>
    /// <param name="censored">Which observations were censored (true = the event had not happened yet), or null.</param>
    /// <param name="frequency">How many times each observation stands for, or null for once each.</param>
    /// <param name="kind">Which curve to return.</param>
    /// <param name="alpha">The confidence bounds cover 100(1 − alpha) percent.</param>
    public static EmpiricalCurve Empirical(
        IReadOnlyList<double> values,
        IReadOnlyList<bool>? censored,
        IReadOnlyList<double>? frequency,
        CurveKind kind,
        double alpha)
    {
        var order = new List<int>();
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsNaN(values[i]))
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) => values[a].CompareTo(values[b]));
        if (order.Count == 0)
        {
            return new EmpiricalCurve([], [], [], []);
        }

        double atRisk = 0;
        foreach (int i in order)
        {
            atRisk += frequency?[i] ?? 1;
        }

        double smallest = values[order[0]];
        var points = new List<double> { smallest };
        var survival = new List<double> { 1 };
        var hazard = new List<double> { 0 };
        var greenwood = new List<double> { 0 };

        double running = 1;
        double accumulated = 0;
        double variance = 0;

        int position = 0;
        while (position < order.Count)
        {
            double time = values[order[position]];
            double events = 0;
            double leaving = 0;
            while (position < order.Count && values[order[position]] == time)
            {
                double weight = frequency?[order[position]] ?? 1;
                leaving += weight;
                if (censored is null || !censored[order[position]])
                {
                    events += weight;
                }

                position++;
            }

            if (events > 0)
            {
                running *= 1 - (events / atRisk);
                accumulated += events / atRisk;

                // Greenwood's formula: each event contributes to the uncertainty in proportion to how
                // few observations were left to see it.
                if (atRisk > events)
                {
                    variance += events / (atRisk * (atRisk - events));
                }

                points.Add(time);
                survival.Add(running);
                hazard.Add(accumulated);
                greenwood.Add(variance);
            }

            atRisk -= leaving;
        }

        int n = points.Count;
        var curve = new double[n];
        var lower = new double[n];
        var upper = new double[n];
        double halfWidth = Math.Sqrt(2) * SpecialFunctions.ErfInverse(1 - alpha);

        for (int i = 0; i < n; i++)
        {
            double standardError = survival[i] * Math.Sqrt(greenwood[i]);
            switch (kind)
            {
                case CurveKind.Survivor:
                    curve[i] = survival[i];
                    lower[i] = Math.Max(0, survival[i] - (halfWidth * standardError));
                    upper[i] = Math.Min(1, survival[i] + (halfWidth * standardError));
                    break;

                case CurveKind.CumulativeHazard:
                    // The hazard's own standard error is the same Greenwood sum, undivided by the
                    // survival it was scaled by.
                    double hazardError = Math.Sqrt(greenwood[i]);
                    curve[i] = hazard[i];
                    lower[i] = Math.Max(0, hazard[i] - (halfWidth * hazardError));
                    upper[i] = hazard[i] + (halfWidth * hazardError);
                    break;

                default:
                    double failed = 1 - survival[i];
                    curve[i] = failed;
                    lower[i] = Math.Max(0, failed - (halfWidth * standardError));
                    upper[i] = Math.Min(1, failed + (halfWidth * standardError));
                    break;
            }
        }

        return new EmpiricalCurve(curve, [.. points], lower, upper);
    }

    /// <summary>
    /// The histogram that agrees with an empirical distribution function: each jump in the curve is
    /// dropped into the bin its point falls in, and the bin's height is the total divided by its width,
    /// so the bars integrate to one.
    /// </summary>
    /// <param name="curve">The cumulative values, as <see cref="Empirical"/> returns them.</param>
    /// <param name="points">Where those values sit.</param>
    /// <param name="edges">The bin edges, in increasing order.</param>
    public static double[] HistogramFromEmpirical(
        IReadOnlyList<double> curve, IReadOnlyList<double> points, IReadOnlyList<double> edges)
    {
        int bins = Math.Max(edges.Count - 1, 0);
        var heights = new double[bins];
        if (bins == 0)
        {
            return heights;
        }

        for (int i = 1; i < curve.Count && i < points.Count; i++)
        {
            double jump = curve[i] - curve[i - 1];
            double where = points[i];

            // The last bin owns its right edge, which is what keeps the largest observation counted.
            int bin = -1;
            for (int b = 0; b < bins; b++)
            {
                bool inside = where >= edges[b] && (where < edges[b + 1] || (b == bins - 1 && where <= edges[b + 1]));
                if (inside)
                {
                    bin = b;
                    break;
                }
            }

            if (bin >= 0)
            {
                heights[bin] += jump;
            }
        }

        for (int b = 0; b < bins; b++)
        {
            double width = edges[b + 1] - edges[b];
            heights[b] = width == 0 ? double.NaN : heights[b] / width;
        }

        return heights;
    }

    /// <summary>The kernel shapes MATLAB's <c>ksdensity</c> offers.</summary>
    public enum Kernel
    {
        /// <summary>The standard normal density — the default, and the only one with unbounded reach.</summary>
        Normal,

        /// <summary>A flat kernel over one bandwidth either side.</summary>
        Box,

        /// <summary>A tent over one bandwidth either side.</summary>
        Triangle,

        /// <summary>The parabola that minimizes the asymptotic error.</summary>
        Epanechnikov,
    }

    /// <summary>Which curve <see cref="KernelDensity"/> returns.</summary>
    public enum SmoothedKind
    {
        /// <summary>The smoothed density.</summary>
        Pdf,

        /// <summary>The smoothed cumulative distribution.</summary>
        Cdf,

        /// <summary>The value the smoothed cumulative distribution reaches each requested probability at.</summary>
        Icdf,

        /// <summary>One minus the smoothed cumulative distribution.</summary>
        Survivor,

        /// <summary>−log of the survivor function.</summary>
        CumulativeHazard,
    }

    /// <summary>How the estimate is kept inside a bounded support.</summary>
    public enum BoundaryRule
    {
        /// <summary>Estimate on a transformed scale where the support is the whole line, then map back.</summary>
        Log,

        /// <summary>Reflect the sample in each boundary, so the mass that would leak back stays inside.</summary>
        Reflection,
    }

    /// <summary>
    /// A kernel-smoothed estimate of the distribution a sample came from, evaluated at the given
    /// points.
    /// </summary>
    /// <param name="values">The sample.</param>
    /// <param name="weights">A weight per observation, or null for equal weights.</param>
    /// <param name="points">Where to evaluate; for <see cref="SmoothedKind.Icdf"/> these are probabilities.</param>
    /// <param name="bandwidth">The kernel width; not positive asks for the default.</param>
    /// <param name="kernel">The kernel shape.</param>
    /// <param name="kind">Which curve to return.</param>
    /// <param name="lowerBound">The lowest value the variable can take, or negative infinity.</param>
    /// <param name="upperBound">The highest value the variable can take, or positive infinity.</param>
    /// <param name="rule">How to respect those bounds.</param>
    public static double[] KernelDensity(
        IReadOnlyList<double> values,
        IReadOnlyList<double>? weights,
        IReadOnlyList<double> points,
        double bandwidth,
        Kernel kernel,
        SmoothedKind kind,
        double lowerBound,
        double upperBound,
        BoundaryRule rule)
    {
        double[] sample = DescriptiveStatistics.WithoutNaN(values);
        if (sample.Length == 0)
        {
            var empty = new double[points.Count];
            Array.Fill(empty, double.NaN);
            return empty;
        }

        double[] weight = NormalizedWeights(values, weights);
        bool bounded = !double.IsNegativeInfinity(lowerBound) || !double.IsPositiveInfinity(upperBound);

        if (bounded && rule == BoundaryRule.Log)
        {
            return OnTransformedScale(
                sample, weight, points, bandwidth, kernel, kind, lowerBound, upperBound);
        }

        double width = bandwidth > 0 ? bandwidth : DefaultBandwidth(sample);
        double[] centres = sample;
        double[] centreWeights = weight;
        if (bounded && rule == BoundaryRule.Reflection)
        {
            (centres, centreWeights) = Reflected(sample, weight, lowerBound, upperBound);
        }

        if (kind != SmoothedKind.Icdf)
        {
            var curve = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                curve[i] = Evaluate(centres, centreWeights, width, kernel, kind, points[i], lowerBound, upperBound);
            }

            return curve;
        }

        return InverseOnGrid(centres, centreWeights, width, kernel, points, lowerBound, upperBound);
    }

    /// <summary>
    /// The default bandwidth: the sample's robust spread, scaled by the rule that is optimal for a
    /// normal sample. The median absolute deviation is used rather than the standard deviation so that
    /// one distant observation does not widen the whole estimate.
    /// </summary>
    public static double DefaultBandwidth(IReadOnlyList<double> sample)
    {
        int n = sample.Count;
        if (n == 0)
        {
            return 1;
        }

        double spread = DescriptiveStatistics.AbsoluteDeviation(sample, aroundMedian: true) / 0.6745;
        if (spread <= 0)
        {
            spread = DescriptiveStatistics.Range(sample);
        }

        return spread > 0 ? spread * Math.Pow(4.0 / (3.0 * n), 0.2) : 1;
    }

    private static double[] NormalizedWeights(IReadOnlyList<double> values, IReadOnlyList<double>? weights)
    {
        var kept = new List<double>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsNaN(values[i]))
            {
                kept.Add(weights is null ? 1 : weights[i]);
            }
        }

        double total = 0;
        foreach (double w in kept)
        {
            total += w;
        }

        var normalized = new double[kept.Count];
        for (int i = 0; i < kept.Count; i++)
        {
            normalized[i] = total == 0 ? 0 : kept[i] / total;
        }

        return normalized;
    }

    /// <summary>
    /// The estimate on a scale where the support is the whole line — the logarithm for a variable
    /// bounded below, the logit for one bounded at both ends — carried back through the derivative of
    /// that map, which is what keeps the density integrating to one.
    /// </summary>
    private static double[] OnTransformedScale(
        double[] sample,
        double[] weight,
        IReadOnlyList<double> points,
        double bandwidth,
        Kernel kernel,
        SmoothedKind kind,
        double lower,
        double upper)
    {
        bool twoSided = !double.IsPositiveInfinity(upper);
        double Forward(double x) => twoSided
            ? Math.Log((x - lower) / (upper - x))
            : Math.Log(x - lower);

        double Back(double t) => twoSided
            ? lower + (((upper - lower) * Math.Exp(t)) / (1 + Math.Exp(t)))
            : lower + Math.Exp(t);

        double Derivative(double x) => twoSided
            ? (upper - lower) / ((x - lower) * (upper - x))
            : 1 / (x - lower);

        var transformed = new double[sample.Length];
        for (int i = 0; i < sample.Length; i++)
        {
            transformed[i] = Forward(sample[i]);
        }

        double width = bandwidth > 0 ? bandwidth : DefaultBandwidth(transformed);

        if (kind == SmoothedKind.Icdf)
        {
            double[] onScale = InverseOnGrid(
                transformed, weight, width, kernel, points,
                double.NegativeInfinity, double.PositiveInfinity);
            for (int i = 0; i < onScale.Length; i++)
            {
                onScale[i] = Back(onScale[i]);
            }

            return onScale;
        }

        var curve = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i];
            if (x <= lower || (twoSided && x >= upper))
            {
                // Outside the support there is no density, and the cumulative curve has either not
                // started or already finished.
                curve[i] = kind switch
                {
                    SmoothedKind.Pdf => 0,
                    SmoothedKind.Cdf => x <= lower ? 0 : 1,
                    SmoothedKind.Survivor => x <= lower ? 1 : 0,
                    SmoothedKind.CumulativeHazard => x <= lower ? 0 : double.PositiveInfinity,
                    _ => double.NaN,
                };
                continue;
            }

            double t = Forward(x);
            double value = Evaluate(
                transformed, weight, width, kernel, kind, t,
                double.NegativeInfinity, double.PositiveInfinity);

            // Only the density carries the Jacobian: a cumulative probability is the same number on
            // either scale, because the map is increasing.
            curve[i] = kind == SmoothedKind.Pdf ? value * Derivative(x) : value;
        }

        return curve;
    }

    /// <summary>
    /// The sample with a mirror image of itself outside each boundary. A kernel that would have put
    /// mass beyond the boundary now puts the same mass just inside it instead.
    /// </summary>
    private static (double[] Centres, double[] Weights) Reflected(
        double[] sample, double[] weight, double lower, double upper)
    {
        var centres = new List<double>(sample.Length * 3);
        var weights = new List<double>(sample.Length * 3);
        for (int i = 0; i < sample.Length; i++)
        {
            centres.Add(sample[i]);
            weights.Add(weight[i]);
            if (!double.IsNegativeInfinity(lower))
            {
                centres.Add((2 * lower) - sample[i]);
                weights.Add(weight[i]);
            }

            if (!double.IsPositiveInfinity(upper))
            {
                centres.Add((2 * upper) - sample[i]);
                weights.Add(weight[i]);
            }
        }

        return ([.. centres], [.. weights]);
    }

    private static double Evaluate(
        double[] centres,
        double[] weights,
        double width,
        Kernel kernel,
        SmoothedKind kind,
        double at,
        double lower,
        double upper)
    {
        double total = 0;
        for (int i = 0; i < centres.Length; i++)
        {
            double u = (at - centres[i]) / width;
            double weight = weights[i % weights.Length];
            total += kind == SmoothedKind.Pdf
                ? weight * KernelValue(kernel, u) / width
                : weight * KernelCumulative(kernel, u);
        }

        if (kind == SmoothedKind.Pdf && (at < lower || at > upper))
        {
            return 0;
        }

        return kind switch
        {
            SmoothedKind.Pdf => total,
            SmoothedKind.Cdf => Math.Clamp(total, 0, 1),
            SmoothedKind.Survivor => Math.Clamp(1 - total, 0, 1),
            SmoothedKind.CumulativeHazard => -Math.Log(Math.Clamp(1 - total, 0, 1)),
            _ => total,
        };
    }

    /// <summary>
    /// The values at which the smoothed cumulative distribution reaches the requested probabilities,
    /// found by evaluating it on a fine grid and reading back along it. The grid rather than a root
    /// find, because the curve is monotone and the same grid answers every probability at once.
    /// </summary>
    private static double[] InverseOnGrid(
        double[] centres,
        double[] weights,
        double width,
        Kernel kernel,
        IReadOnlyList<double> probabilities,
        double lower,
        double upper)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double centre in centres)
        {
            low = Math.Min(low, centre);
            high = Math.Max(high, centre);
        }

        low -= 4 * width;
        high += 4 * width;

        const int steps = 2048;
        var grid = new double[steps];
        var cumulative = new double[steps];
        for (int i = 0; i < steps; i++)
        {
            grid[i] = low + ((high - low) * i / (steps - 1.0));
            cumulative[i] = Evaluate(
                centres, weights, width, kernel, SmoothedKind.Cdf, grid[i], lower, upper);
        }

        var result = new double[probabilities.Count];
        for (int i = 0; i < probabilities.Count; i++)
        {
            result[i] = ReadBack(grid, cumulative, probabilities[i]);
        }

        return result;
    }

    private static double ReadBack(double[] grid, double[] cumulative, double probability)
    {
        if (double.IsNaN(probability) || probability < 0 || probability > 1)
        {
            return double.NaN;
        }

        if (probability <= cumulative[0])
        {
            return grid[0];
        }

        for (int i = 1; i < grid.Length; i++)
        {
            if (cumulative[i] >= probability)
            {
                double span = cumulative[i] - cumulative[i - 1];
                double fraction = span == 0 ? 0 : (probability - cumulative[i - 1]) / span;
                return grid[i - 1] + (fraction * (grid[i] - grid[i - 1]));
            }
        }

        return grid[^1];
    }

    /// <summary>
    /// The kernel's own shape at <paramref name="u"/> standard widths from the centre, before any
    /// bandwidth is divided out.
    /// </summary>
    /// <remarks>
    /// Public because the multivariate estimate (M53 wave E) is a product of these one per variable,
    /// and a second copy of four one-line formulas is a second thing to keep right.
    /// </remarks>
    public static double KernelWeight(Kernel kernel, double u) => KernelValue(kernel, u);

    private static double KernelValue(Kernel kernel, double u) => kernel switch
    {
        Kernel.Box => Math.Abs(u) <= 1 ? 0.5 : 0,
        Kernel.Triangle => Math.Abs(u) <= 1 ? 1 - Math.Abs(u) : 0,
        Kernel.Epanechnikov => Math.Abs(u) <= 1 ? 0.75 * (1 - (u * u)) : 0,
        _ => Math.Exp(-0.5 * u * u) / Math.Sqrt(2 * Math.PI),
    };

    private static double KernelCumulative(Kernel kernel, double u)
    {
        switch (kernel)
        {
            case Kernel.Box:
                return Math.Clamp((u + 1) / 2, 0, 1);

            case Kernel.Triangle:
                if (u <= -1)
                {
                    return 0;
                }

                if (u >= 1)
                {
                    return 1;
                }

                return u <= 0
                    ? (1 + u) * (1 + u) / 2
                    : 1 - ((1 - u) * (1 - u) / 2);

            case Kernel.Epanechnikov:
                if (u <= -1)
                {
                    return 0;
                }

                return u >= 1 ? 1 : 0.5 + (0.75 * (u - (u * u * u / 3)));

            default:
                return 0.5 * SpecialFunctions.Erfc(-u / Math.Sqrt(2));
        }
    }
}
