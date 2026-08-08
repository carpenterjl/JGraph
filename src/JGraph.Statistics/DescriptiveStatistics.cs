namespace JGraph.Statistics;

/// <summary>
/// The descriptive and robust statistics of one sample: percentiles, the shape moments, the spread
/// measures that survive an outlier, and the rank and frequency summaries the nonparametric tests are
/// built on. Pure functions over plain doubles — argument reading, dimensions and option words live in
/// the scripting layer.
/// </summary>
/// <remarks>
/// Every function here states which values it drops. MATLAB is not uniform about it: <c>prctile</c>,
/// <c>skewness</c>, <c>kurtosis</c>, <c>mad</c> and <c>trimmean</c> discard NaN and shrink their
/// denominator, while <c>moment</c> and <c>zscore</c> let it propagate, so a NaN in the data changes
/// the answer of one and destroys the answer of the other. Copying that faithfully means the caller
/// decides, so nothing here filters unless its own documentation says it does.
/// </remarks>
public static class DescriptiveStatistics
{
    /// <summary>
    /// The percentiles of a sample, MATLAB's way: the sorted observations are placed at the cumulative
    /// probabilities (i − ½)/n and the answer is read off the straight line between them, with anything
    /// past the ends clamped to the extreme observation.
    /// </summary>
    /// <remarks>
    /// This is the whole reason percentiles are computed here rather than by the JGS
    /// <c>percentile</c> builtin, which places the observations at i/(n − 1) instead — the convention
    /// NumPy and R's type 7 use. The two agree only at the median of an odd-length sample, so a script
    /// moved across would silently get different quartiles.
    /// </remarks>
    /// <param name="values">The sample; NaN is discarded before anything else happens.</param>
    /// <param name="percents">The percentiles wanted, each between 0 and 100.</param>
    public static double[] Percentiles(IReadOnlyList<double> values, IReadOnlyList<double> percents)
    {
        double[] sorted = SortedWithoutNaN(values);
        var result = new double[percents.Count];
        for (int i = 0; i < percents.Count; i++)
        {
            result[i] = PercentileOf(sorted, percents[i]);
        }

        return result;
    }

    /// <summary>One percentile of an already sorted, NaN-free sample.</summary>
    private static double PercentileOf(double[] sorted, double percent)
    {
        int n = sorted.Length;
        if (n == 0 || double.IsNaN(percent))
        {
            return double.NaN;
        }

        if (n == 1)
        {
            return sorted[0];
        }

        // Where this percentile sits among the midpoints: position (i - 0.5)/n * 100 holds sorted[i-1].
        double position = ((percent / 100.0) * n) - 0.5;
        if (position <= 0)
        {
            return sorted[0];
        }

        if (position >= n - 1)
        {
            return sorted[n - 1];
        }

        int below = (int)Math.Floor(position);
        double fraction = position - below;
        return sorted[below] + (fraction * (sorted[below + 1] - sorted[below]));
    }

    /// <summary>The k-th central moment: the mean of (x − x̄)^k. NaN propagates, as MATLAB's does.</summary>
    public static double CentralMoment(IReadOnlyList<double> values, int order)
    {
        int n = values.Count;
        if (n == 0)
        {
            return double.NaN;
        }

        // The first central moment is zero by construction, and MATLAB reports the zero rather than
        // the rounding error that summing the deviations would otherwise leave behind.
        if (order == 1)
        {
            return 0;
        }

        double mean = Mean(values);
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            total += Math.Pow(values[i] - mean, order);
        }

        return total / n;
    }

    /// <summary>
    /// Skewness. <paramref name="bias"/> true is MATLAB's default and the plain ratio m₃/m₂^1.5;
    /// false applies the correction that makes it unbiased for a normal sample. NaN is dropped.
    /// </summary>
    public static double Skewness(IReadOnlyList<double> values, bool bias)
    {
        double[] x = WithoutNaN(values);
        int n = x.Length;
        double m2 = RawCentralMoment(x, 2);
        double m3 = RawCentralMoment(x, 3);
        if (n == 0 || m2 == 0)
        {
            return double.NaN;
        }

        double s = m3 / Math.Pow(m2, 1.5);
        if (bias || n < 3)
        {
            return s;
        }

        return s * Math.Sqrt((double)n * (n - 1)) / (n - 2);
    }

    /// <summary>
    /// Kurtosis, on MATLAB's scale where a normal sample answers 3 rather than 0.
    /// <paramref name="bias"/> true is the plain ratio m₄/m₂²; false applies the correction.
    /// NaN is dropped.
    /// </summary>
    public static double Kurtosis(IReadOnlyList<double> values, bool bias)
    {
        double[] x = WithoutNaN(values);
        int n = x.Length;
        double m2 = RawCentralMoment(x, 2);
        double m4 = RawCentralMoment(x, 4);
        if (n == 0 || m2 == 0)
        {
            return double.NaN;
        }

        double k = m4 / (m2 * m2);
        if (bias || n < 4)
        {
            return k;
        }

        return ((((n + 1) * k) - (3 * (n - 1))) * (n - 1) / ((double)(n - 2) * (n - 3))) + 3;
    }

    /// <summary>
    /// Absolute deviation. <paramref name="aroundMedian"/> false is MATLAB's default — the mean
    /// deviation from the mean; true is the median deviation from the median, which an outlier cannot
    /// move. NaN is dropped.
    /// </summary>
    public static double AbsoluteDeviation(IReadOnlyList<double> values, bool aroundMedian)
    {
        double[] x = WithoutNaN(values);
        if (x.Length == 0)
        {
            return double.NaN;
        }

        if (!aroundMedian)
        {
            double mean = Mean(x);
            double total = 0;
            foreach (double value in x)
            {
                total += Math.Abs(value - mean);
            }

            return total / x.Length;
        }

        double centre = Median(x);
        var spread = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            spread[i] = Math.Abs(x[i] - centre);
        }

        return Median(spread);
    }

    /// <summary>How the ends of a sample are dropped before its mean is taken.</summary>
    public enum TrimRule
    {
        /// <summary>MATLAB's default: round the count of observations to remove from each tail.</summary>
        Round,

        /// <summary>Round the count down, so slightly fewer observations are dropped.</summary>
        Floor,

        /// <summary>Drop whole observations, then weight the two innermost survivors by the remainder.</summary>
        Weighted,
    }

    /// <summary>
    /// The mean of a sample with <paramref name="percent"/> of the observations removed, half from
    /// each tail. NaN is dropped first, so the percentage is of what was actually recorded.
    /// </summary>
    public static double TrimmedMean(IReadOnlyList<double> values, double percent, TrimRule rule)
    {
        double[] x = SortedWithoutNaN(values);
        int n = x.Length;
        if (n == 0)
        {
            return double.NaN;
        }

        double exact = n * percent / 200.0;
        int whole = rule switch
        {
            TrimRule.Round => (int)Math.Round(exact, MidpointRounding.AwayFromZero),
            _ => (int)Math.Floor(exact),
        };

        // Trimming past the middle leaves nothing to average. MATLAB keeps the middle observation
        // rather than answering NaN, which is what the clamp preserves.
        whole = Math.Min(whole, (n - 1) / 2);

        if (rule != TrimRule.Weighted)
        {
            double kept = 0;
            for (int i = whole; i < n - whole; i++)
            {
                kept += x[i];
            }

            return kept / (n - (2 * whole));
        }

        // The weighted rule drops `whole` observations outright and then counts the two now-outermost
        // survivors only partly, so the divisor is the fractional count rather than a whole one.
        double fraction = Math.Min(exact - whole, 0.5);
        double total = (1 - fraction) * (x[whole] + x[n - 1 - whole]);
        for (int i = whole + 1; i < n - whole - 1; i++)
        {
            total += x[i];
        }

        double divisor = n - (2 * (whole + fraction));
        return divisor <= 0 ? double.NaN : total / divisor;
    }

    /// <summary>
    /// Standardized scores: how many standard deviations each observation sits from the mean.
    /// A sample with no spread scores zero throughout rather than dividing by nothing.
    /// </summary>
    /// <param name="values">The sample. NaN propagates into every score, as MATLAB's does.</param>
    /// <param name="population">
    /// Whether the standard deviation divides by n rather than n − 1.
    /// </param>
    public static (double[] Scores, double Centre, double Spread) StandardScores(
        IReadOnlyList<double> values, bool population)
    {
        double mean = Mean(values);
        double spread = StandardDeviation(values, population);
        double divisor = spread == 0 || double.IsNaN(spread) ? 1 : spread;

        var scores = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            scores[i] = (values[i] - mean) / divisor;
        }

        return (scores, mean, spread);
    }

    /// <summary>The geometric mean. A negative value has no real answer, which is the caller's error.</summary>
    public static double GeometricMean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        // Summing logarithms rather than multiplying keeps a long sample from overflowing to infinity
        // or underflowing to zero before the root is taken.
        double total = 0;
        foreach (double value in values)
        {
            if (value == 0)
            {
                return 0;
            }

            total += Math.Log(value);
        }

        return Math.Exp(total / values.Count);
    }

    /// <summary>The harmonic mean: the reciprocal of the mean of the reciprocals.</summary>
    public static double HarmonicMean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        double total = 0;
        foreach (double value in values)
        {
            total += 1.0 / value;
        }

        return values.Count / total;
    }

    /// <summary>
    /// The distance between the quartiles: a spread that a handful of wild observations cannot move,
    /// where the range is defined by exactly the two most extreme of them.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="Percentiles"/>, so it uses the midpoint convention MathWorks documents
    /// rather than a second interpolation rule that would disagree with <c>prctile(x, [25 75])</c>.
    /// </remarks>
    public static double InterquartileRange(IReadOnlyList<double> values)
    {
        double[] quartiles = Percentiles(values, [25, 75]);
        return quartiles[1] - quartiles[0];
    }

    /// <summary>
    /// The distance from the smallest observation to the largest. NaN is ignored, because MATLAB's
    /// <c>range</c> is <c>max</c> minus <c>min</c> and both of those ignore it.
    /// </summary>
    public static double Range(IReadOnlyList<double> values)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        bool any = false;
        foreach (double value in values)
        {
            if (double.IsNaN(value))
            {
                continue;
            }

            any = true;
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        return any ? high - low : double.NaN;
    }

    /// <summary>What <c>tiedrank</c>'s tie adjustment is being computed for.</summary>
    public enum TieAdjustment
    {
        /// <summary>Σ(t³ − t)/2 over the tie runs — the correction the rank ANOVA tests want.</summary>
        RankSumOfCubes,

        /// <summary>Σt(t − 1)/2 — the correction the Wilcoxon rank tests want.</summary>
        PairCount,
    }

    /// <summary>
    /// Ranks from smallest to largest, with tied observations sharing the average of the ranks they
    /// span. NaN keeps its place in the output but takes no rank and is not counted.
    /// </summary>
    /// <param name="values">The sample.</param>
    /// <param name="adjustment">Which tie correction to accumulate alongside the ranks.</param>
    /// <param name="fromOutside">
    /// Whether to rank from the outside in — the smallest and the largest share rank 1, the next pair
    /// share rank 2, and so on. This is the ranking the Ansari-Bradley dispersion test is built on.
    /// </param>
    public static (double[] Ranks, double TieAdjustment) TiedRanks(
        IReadOnlyList<double> values, TieAdjustment adjustment, bool fromOutside = false)
    {
        int total = values.Count;
        var order = new List<int>(total);
        for (int i = 0; i < total; i++)
        {
            if (!double.IsNaN(values[i]))
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) => values[a].CompareTo(values[b]));

        int n = order.Count;
        var ranks = new double[total];
        for (int i = 0; i < total; i++)
        {
            ranks[i] = double.NaN;
        }

        double tieAdjustment = 0;
        int position = 0;
        while (position < n)
        {
            int runEnd = position;
            while (runEnd + 1 < n && values[order[runEnd + 1]] == values[order[position]])
            {
                runEnd++;
            }

            int runLength = runEnd - position + 1;

            // Every member of a tie run gets the average of the ranks the run covers, so the ranks
            // still sum to what they would have without the tie.
            double shared = 0;
            for (int i = position; i <= runEnd; i++)
            {
                shared += fromOutside ? Math.Min(i + 1, n - i) : i + 1;
            }

            shared /= runLength;
            for (int i = position; i <= runEnd; i++)
            {
                ranks[order[i]] = shared;
            }

            if (runLength > 1)
            {
                tieAdjustment += adjustment == TieAdjustment.PairCount
                    ? runLength * (runLength - 1) / 2.0
                    : runLength * ((double)runLength - 1) * (runLength + 1) / 2.0;
            }

            position = runEnd + 1;
        }

        return (ranks, tieAdjustment);
    }

    /// <summary>One row of a frequency table: a value, how often it occurred, and its share.</summary>
    /// <param name="Value">The value the row counts.</param>
    /// <param name="Count">How many observations took it.</param>
    /// <param name="Percent">That count as a percentage of the sample.</param>
    public readonly record struct FrequencyRow(double Value, int Count, double Percent);

    /// <summary>
    /// The frequency table of a sample. A sample of positive whole numbers gets a row for every
    /// integer from 1 to the largest — including the ones nobody took, which is what makes the table
    /// line up with an index. Anything else gets a row per distinct value, in increasing order.
    /// </summary>
    public static FrequencyRow[] Tabulate(IReadOnlyList<double> values)
    {
        var present = new List<double>(values.Count);
        foreach (double value in values)
        {
            if (!double.IsNaN(value))
            {
                present.Add(value);
            }
        }

        int n = present.Count;
        bool countable = n > 0;
        double largest = 0;
        foreach (double value in present)
        {
            countable &= value > 0 && value == Math.Floor(value) && value < int.MaxValue;
            largest = Math.Max(largest, value);
        }

        var counts = new Dictionary<double, int>();
        foreach (double value in present)
        {
            counts[value] = counts.TryGetValue(value, out int seen) ? seen + 1 : 1;
        }

        List<double> rows;
        if (countable)
        {
            rows = [];
            for (int value = 1; value <= (int)largest; value++)
            {
                rows.Add(value);
            }
        }
        else
        {
            rows = [.. counts.Keys];
            rows.Sort();
        }

        var table = new FrequencyRow[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            int count = counts.TryGetValue(rows[i], out int seen) ? seen : 0;
            table[i] = new FrequencyRow(rows[i], count, n == 0 ? 0 : 100.0 * count / n);
        }

        return table;
    }

    // --- Shared arithmetic -------------------------------------------------------------------------

    /// <summary>The arithmetic mean, NaN and all.</summary>
    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        double total = 0;
        foreach (double value in values)
        {
            total += value;
        }

        return total / values.Count;
    }

    /// <summary>The standard deviation, dividing by n when <paramref name="population"/>, else n − 1.</summary>
    public static double StandardDeviation(IReadOnlyList<double> values, bool population) =>
        Math.Sqrt(Variance(values, population));

    /// <summary>The variance, dividing by n when <paramref name="population"/>, else n − 1.</summary>
    public static double Variance(IReadOnlyList<double> values, bool population)
    {
        int n = values.Count;
        if (n == 0)
        {
            return double.NaN;
        }

        if (n == 1)
        {
            return 0;
        }

        double mean = Mean(values);
        double total = 0;
        foreach (double value in values)
        {
            double deviation = value - mean;
            total += deviation * deviation;
        }

        return total / (population ? n : n - 1);
    }

    /// <summary>The median of a sample, NaN dropped.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = SortedWithoutNaN(values);
        int n = sorted.Length;
        if (n == 0)
        {
            return double.NaN;
        }

        return n % 2 == 1 ? sorted[n / 2] : (sorted[(n / 2) - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>The sample with every NaN removed, in the order it was given.</summary>
    public static double[] WithoutNaN(IReadOnlyList<double> values)
    {
        var kept = new List<double>(values.Count);
        foreach (double value in values)
        {
            if (!double.IsNaN(value))
            {
                kept.Add(value);
            }
        }

        return [.. kept];
    }

    /// <summary>The sample with every NaN removed, sorted.</summary>
    public static double[] SortedWithoutNaN(IReadOnlyList<double> values)
    {
        double[] kept = WithoutNaN(values);
        Array.Sort(kept);
        return kept;
    }

    /// <summary>The k-th central moment of an already-filtered sample.</summary>
    private static double RawCentralMoment(double[] values, int order)
    {
        if (values.Length == 0)
        {
            return double.NaN;
        }

        double mean = Mean(values);
        double total = 0;
        foreach (double value in values)
        {
            total += Math.Pow(value - mean, order);
        }

        return total / values.Length;
    }
}
