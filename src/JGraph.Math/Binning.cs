namespace JGraph.Maths;

/// <summary>
/// Where a value falls among a set of bin edges, and how many values fall in each. Every bin takes
/// its left edge and not its right, except the last, which takes both — that one rule is what makes
/// the counts add up to the sample size, and it lives here so that a histogram drawn on square paper,
/// a histogram drawn round a circle, and the <c>histcounts</c> a script checks them against cannot
/// disagree about which side of an edge a reading sits on.
/// </summary>
public static class Binning
{
    /// <summary>
    /// Which bin <paramref name="value"/> falls in, or −1 for one outside every bin — which is what a
    /// value past either end, or a NaN, is.
    /// </summary>
    public static int BinOf(double value, IReadOnlyList<double> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Count < 2 || double.IsNaN(value) || value < edges[0] || value > edges[^1])
        {
            return -1;
        }

        if (value == edges[^1])
        {
            return edges.Count - 2; // the last bin is closed at both ends
        }

        int low = 0;
        int high = edges.Count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (value < edges[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    /// <summary>How many of <paramref name="values"/> fall in each bin the edges describe.</summary>
    public static double[] Counts(IReadOnlyList<double> values, IReadOnlyList<double> edges)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(edges);

        var counts = new double[System.Math.Max(0, edges.Count - 1)];
        foreach (double value in values)
        {
            int bin = BinOf(value, edges);
            if (bin >= 0)
            {
                counts[bin]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// <paramref name="count"/> bins of equal width filling the span from <paramref name="low"/> to
    /// <paramref name="high"/>. The last edge is set rather than accumulated, so a chart asked for
    /// bins covering a full turn ends exactly on it and not a rounding error short.
    /// </summary>
    public static double[] Spanning(double low, double high, int count)
    {
        int bins = System.Math.Max(1, count);
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = low + ((high - low) * i / bins);
        }

        edges[^1] = high;
        return edges;
    }
}
