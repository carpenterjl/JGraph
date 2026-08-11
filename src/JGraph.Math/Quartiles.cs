namespace JGraph.Maths;

/// <summary>
/// The five-number summary a box chart draws: the quartiles, the whiskers, the notch, and the
/// observations that fall outside the whiskers.
/// </summary>
/// <param name="Count">How many finite observations the summary was taken over.</param>
/// <param name="LowerQuartile">The 25th percentile — the bottom of the box.</param>
/// <param name="Median">The 50th percentile — the line across the box.</param>
/// <param name="UpperQuartile">The 75th percentile — the top of the box.</param>
/// <param name="LowerWhisker">The smallest observation still within reach of the box.</param>
/// <param name="UpperWhisker">The largest observation still within reach of the box.</param>
/// <param name="NotchHalfWidth">
/// Half the height of the notch: the interval within which this median is not distinguishable from
/// another, which is the whole reason a notch is worth drawing.
/// </param>
/// <param name="Outliers">The observations beyond the whiskers, in ascending order.</param>
public readonly record struct BoxSummary(
    int Count,
    double LowerQuartile,
    double Median,
    double UpperQuartile,
    double LowerWhisker,
    double UpperWhisker,
    double NotchHalfWidth,
    IReadOnlyList<double> Outliers)
{
    /// <summary>The height of the box, which is what the whisker reach and the notch are measured in.</summary>
    public double InterquartileRange => UpperQuartile - LowerQuartile;
}

/// <summary>
/// The quartile arithmetic behind a box chart. It lives here rather than in JGraph.Statistics
/// because a plot object may not depend on the statistics toolbox, and because what a box needs is
/// one summary of one sample rather than the toolbox's dimension-and-option surface.
/// </summary>
/// <remarks>
/// The percentile convention is MATLAB's: the sorted observations are placed at the cumulative
/// probabilities (i − ½)/n and the answer is read off the straight line between them, with anything
/// past the ends clamped to the extreme observation. That is the same convention
/// <c>DescriptiveStatistics.Percentiles</c> uses, and it has to stay the same one — a script that
/// draws a box chart and then asks <c>prctile</c> for the same quartile must get the same number.
/// A test pins the two against each other.
/// </remarks>
public static class Quartiles
{
    /// <summary>
    /// How far past the box a whisker may reach, in box heights. MATLAB's <c>boxchart</c> fixes this
    /// at 1.5 and gives no property to change it, so neither does the plot object.
    /// </summary>
    public const double WhiskerReach = 1.5;

    /// <summary>
    /// Summarizes one sample, or null when nothing in it is finite — which is what an empty group
    /// looks like, and a group with nothing to draw draws nothing rather than a degenerate box.
    /// </summary>
    /// <param name="values">The observations. Non-finite values are dropped.</param>
    /// <param name="whiskerReach">How far past the box a whisker may reach, in box heights.</param>
    public static BoxSummary? Summarize(IReadOnlyList<double> values, double whiskerReach = WhiskerReach)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = SortedFinite(values);
        if (sorted.Length == 0)
        {
            return null;
        }

        double q1 = PercentileOfSorted(sorted, 25);
        double median = PercentileOfSorted(sorted, 50);
        double q3 = PercentileOfSorted(sorted, 75);
        double reach = System.Math.Max(0, whiskerReach) * (q3 - q1);

        // The whiskers stop at observations, not at the reach itself, so a whisker never extends past
        // data that is actually there — which is what makes its end readable as a value.
        double low = q1;
        double high = q3;
        var outliers = new List<double>();
        foreach (double value in sorted)
        {
            if (value < q1 - reach || value > q3 + reach)
            {
                outliers.Add(value);
            }
            else
            {
                low = System.Math.Min(low, value);
                high = System.Math.Max(high, value);
            }
        }

        return new BoxSummary(
            sorted.Length,
            q1,
            median,
            q3,
            low,
            high,
            1.57 * (q3 - q1) / System.Math.Sqrt(sorted.Length),
            outliers);
    }

    /// <summary>One percentile of a sample, dropping anything non-finite first.</summary>
    /// <param name="values">The observations, in any order.</param>
    /// <param name="percent">The percentile wanted, between 0 and 100.</param>
    public static double Percentile(IReadOnlyList<double> values, double percent)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = SortedFinite(values);
        return sorted.Length == 0 ? double.NaN : PercentileOfSorted(sorted, percent);
    }

    private static double[] SortedFinite(IReadOnlyList<double> values)
    {
        var finite = new List<double>(values.Count);
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                finite.Add(value);
            }
        }

        double[] sorted = finite.ToArray();
        Array.Sort(sorted);
        return sorted;
    }

    private static double PercentileOfSorted(double[] sorted, double percent)
    {
        int n = sorted.Length;
        if (n == 1 || double.IsNaN(percent))
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

        int below = (int)System.Math.Floor(position);
        return sorted[below] + ((position - below) * (sorted[below + 1] - sorted[below]));
    }
}
