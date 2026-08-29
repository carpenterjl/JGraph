namespace JGraph.Numerics;

/// <summary>
/// Where a signal stops being one thing and starts being another. <c>ischange</c> asks that question
/// three ways — did the mean move, did the spread move, did the slope move — and every one of them is
/// the same computation: cut the signal into segments, charge each segment what its own model cannot
/// explain, charge a penalty per cut, and keep the cheapest set of cuts. This class is that
/// computation, done once, so the three methods cannot drift apart in how they weigh a cut.
/// </summary>
/// <remarks>
/// <para>
/// The search is the exact dynamic programme over prefixes, not a heuristic: <c>F(j)</c> is the
/// cheapest cost of the first <c>j</c> samples, found by trying every place the last segment could
/// begin. That is O(n²) segment costs, and each cost is O(1) off prefix sums, so a ten-thousand-point
/// trace answers in the time a script takes to plot it. Ties keep the earliest split when a penalty
/// drives the search and the latest when a change budget does — both calibrated against MATLAB
/// R2024a on vectors where two segmentations cost exactly the same.
/// </para>
/// <para>
/// A change budget (<c>MaxNumChanges</c>) is not "spend the whole budget": among the change counts
/// the budget allows, the smallest count whose residual is no worse than any larger count's wins, so
/// a signal that one cut already explains perfectly does not get a second cut for free.
/// </para>
/// </remarks>
public static class ChangePoints
{
    /// <summary>What a segment is charged for: distance from its mean, its spread, or its line.</summary>
    public enum Statistic
    {
        /// <summary>Sum of squared residuals about the segment mean.</summary>
        Mean,

        /// <summary>Gaussian log-likelihood of the segment's own maximum-likelihood variance.</summary>
        Variance,

        /// <summary>Sum of squared residuals about the segment's least-squares line.</summary>
        Linear,
    }

    /// <summary>
    /// The starts of new segments (0-based indices past the first segment), given either a penalty
    /// per change or a budget of changes. Sample points are the abscissae the linear model fits in;
    /// the other two never read them.
    /// </summary>
    public static int[] Find(
        ReadOnlySpan<double> values,
        ReadOnlySpan<double> points,
        Statistic statistic,
        double penalty,
        int? maxChanges)
    {
        int n = values.Length;
        int least = statistic == Statistic.Mean ? 1 : 2;
        if (n < 2 * least)
        {
            return [];
        }

        var sums = new PrefixSums(values, points);
        return maxChanges is { } budget
            ? Budgeted(sums, statistic, least, Math.Min(budget, (n / least) - 1))
            : Penalised(sums, statistic, least, penalty);
    }

    /// <summary>The mean of each segment, written over the samples the segment covers.</summary>
    public static double[] SegmentMeans(ReadOnlySpan<double> values, int[] changes)
    {
        var result = new double[values.Length];
        foreach ((int from, int to) in Segments(values.Length, changes))
        {
            double total = 0;
            for (int i = from; i < to; i++)
            {
                total += values[i];
            }

            double mean = total / (to - from);
            for (int i = from; i < to; i++)
            {
                result[i] = mean;
            }
        }

        return result;
    }

    /// <summary>The sample variance of each segment, written over the samples it covers.</summary>
    public static double[] SegmentVariances(ReadOnlySpan<double> values, int[] changes)
    {
        var result = new double[values.Length];
        foreach ((int from, int to) in Segments(values.Length, changes))
        {
            int count = to - from;
            double total = 0;
            for (int i = from; i < to; i++)
            {
                total += values[i];
            }

            double mean = total / count;
            double spread = 0;
            for (int i = from; i < to; i++)
            {
                spread += (values[i] - mean) * (values[i] - mean);
            }

            double variance = count > 1 ? spread / (count - 1) : 0;
            for (int i = from; i < to; i++)
            {
                result[i] = variance;
            }
        }

        return result;
    }

    /// <summary>
    /// The least-squares slope and intercept of each segment in the given abscissae, written over
    /// the samples the segment covers.
    /// </summary>
    public static (double[] Slopes, double[] Intercepts) SegmentLines(
        ReadOnlySpan<double> values, ReadOnlySpan<double> points, int[] changes)
    {
        var slopes = new double[values.Length];
        var intercepts = new double[values.Length];
        foreach ((int from, int to) in Segments(values.Length, changes))
        {
            int count = to - from;
            double meanT = 0;
            double meanY = 0;
            for (int i = from; i < to; i++)
            {
                meanT += points[i];
                meanY += values[i];
            }

            meanT /= count;
            meanY /= count;
            double covariance = 0;
            double spread = 0;
            for (int i = from; i < to; i++)
            {
                covariance += (points[i] - meanT) * (values[i] - meanY);
                spread += (points[i] - meanT) * (points[i] - meanT);
            }

            double slope = spread > 0 ? covariance / spread : 0;
            double intercept = meanY - (slope * meanT);
            for (int i = from; i < to; i++)
            {
                slopes[i] = slope;
                intercepts[i] = intercept;
            }
        }

        return (slopes, intercepts);
    }

    private static IEnumerable<(int From, int To)> Segments(int length, int[] changes)
    {
        int from = 0;
        foreach (int change in changes)
        {
            yield return (from, change);
            from = change;
        }

        yield return (from, length);
    }

    /// <summary>
    /// The penalised search: every change costs <paramref name="penalty"/>, and a tie keeps the
    /// earliest place the last segment could start.
    /// </summary>
    private static int[] Penalised(in PrefixSums sums, Statistic statistic, int least, double penalty)
    {
        int n = sums.Count;
        var best = new double[n + 1];
        var from = new int[n + 1];
        best[0] = -penalty; // the first segment is not a change, so its penalty is refunded here
        for (int j = 1; j <= n; j++)
        {
            best[j] = double.PositiveInfinity;
            for (int i = 0; i <= j - least; i++)
            {
                // A start inside the first segment's minimum length has an infinite best[i]
                // already, so the length rule needs no second statement here.
                double candidate = best[i] + Cost(sums, statistic, i, j) + penalty;
                if (candidate < best[j])
                {
                    best[j] = candidate;
                    from[j] = i;
                }
            }
        }

        return Recover(from, n);
    }

    /// <summary>
    /// The budgeted search: the residual is minimised for every change count the budget allows, the
    /// smallest count that no larger count improves on wins, and a tie keeps the latest place the
    /// last segment could start.
    /// </summary>
    private static int[] Budgeted(in PrefixSums sums, Statistic statistic, int least, int budget)
    {
        int n = sums.Count;
        if (budget < 1)
        {
            return [];
        }

        // best[k][j]: cheapest residual of the first j samples cut into k+1 segments.
        var best = new double[budget + 1][];
        var from = new int[budget + 1][];
        best[0] = new double[n + 1];
        from[0] = new int[n + 1];
        for (int j = 1; j <= n; j++)
        {
            best[0][j] = j >= least ? Cost(sums, statistic, 0, j) : double.PositiveInfinity;
        }

        for (int k = 1; k <= budget; k++)
        {
            best[k] = new double[n + 1];
            from[k] = new int[n + 1];
            for (int j = 0; j <= n; j++)
            {
                best[k][j] = double.PositiveInfinity;
                for (int i = least; i <= j - least; i++)
                {
                    double candidate = best[k - 1][i] + Cost(sums, statistic, i, j);
                    if (candidate <= best[k][j])
                    {
                        best[k][j] = candidate;
                        from[k][j] = i;
                    }
                }
            }
        }

        int chosen = 0;
        for (int k = 1; k <= budget; k++)
        {
            if (best[k][n] < best[chosen][n])
            {
                chosen = k;
            }
        }

        if (chosen == 0)
        {
            return [];
        }

        var changes = new int[chosen];
        int at = n;
        for (int k = chosen; k >= 1; k--)
        {
            at = from[k][at];
            changes[k - 1] = at;
        }

        return changes;
    }

    private static int[] Recover(int[] from, int n)
    {
        var reversed = new List<int>();
        for (int at = n; at > 0; at = from[at])
        {
            if (from[at] > 0)
            {
                reversed.Add(from[at]);
            }
        }

        reversed.Reverse();
        return [.. reversed];
    }

    /// <summary>What the half-open segment <c>[from, to)</c> cannot explain about itself.</summary>
    private static double Cost(in PrefixSums sums, Statistic statistic, int from, int to)
    {
        int count = to - from;
        double sumY = sums.Y[to] - sums.Y[from];
        double sumYY = sums.YY[to] - sums.YY[from];
        double aboutMean = Math.Max(0, sumYY - (sumY * sumY / count));
        switch (statistic)
        {
            case Statistic.Mean:
                return aboutMean;
            case Statistic.Variance:
                // The Gaussian −2·log-likelihood of the segment's own maximum-likelihood variance,
                // floored so a perfectly flat segment costs a large finite amount rather than −∞ —
                // the floor cancels across segmentations because sample counts always sum to n.
                return count * Math.Log(Math.Max(aboutMean / count, double.Epsilon));
            default:
                double sumT = sums.T[to] - sums.T[from];
                double sumTT = sums.TT[to] - sums.TT[from];
                double sumTY = sums.TY[to] - sums.TY[from];
                double spread = sumTT - (sumT * sumT / count);
                double covariance = sumTY - (sumT * sumY / count);
                return spread > 0 ? Math.Max(0, aboutMean - (covariance * covariance / spread)) : aboutMean;
        }
    }

    /// <summary>Prefix sums that price any segment in constant time.</summary>
    private readonly struct PrefixSums
    {
        public PrefixSums(ReadOnlySpan<double> values, ReadOnlySpan<double> points)
        {
            Count = values.Length;
            Y = new double[Count + 1];
            YY = new double[Count + 1];
            T = new double[Count + 1];
            TT = new double[Count + 1];
            TY = new double[Count + 1];
            for (int i = 0; i < Count; i++)
            {
                double y = values[i];
                double t = points.Length > i ? points[i] : i + 1;
                Y[i + 1] = Y[i] + y;
                YY[i + 1] = YY[i] + (y * y);
                T[i + 1] = T[i] + t;
                TT[i + 1] = TT[i] + (t * t);
                TY[i + 1] = TY[i] + (t * y);
            }
        }

        public int Count { get; }

        public double[] Y { get; }

        public double[] YY { get; }

        public double[] T { get; }

        public double[] TT { get; }

        public double[] TY { get; }
    }
}
