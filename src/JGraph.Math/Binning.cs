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
    /// How many of the <c>(x, y)</c> pairs fall in each cell of the grid the two sets of edges
    /// describe, indexed <c>[x bin, y bin]</c>.
    /// <para>
    /// That way round is MATLAB's: <c>binscatter</c>'s <c>Values</c> is as many rows as there are
    /// bins across, so a column of the answer is a column of the picture. A pair with either
    /// coordinate outside its own edges is outside the grid, which is the one-dimensional rule
    /// applied twice rather than a new one.
    /// </para>
    /// </summary>
    public static double[,] Counts2D(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        IReadOnlyList<double> xEdges,
        IReadOnlyList<double> yEdges)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        ArgumentNullException.ThrowIfNull(xEdges);
        ArgumentNullException.ThrowIfNull(yEdges);

        var counts = new double[
            System.Math.Max(0, xEdges.Count - 1),
            System.Math.Max(0, yEdges.Count - 1)];

        int pairs = System.Math.Min(xs.Count, ys.Count);
        for (int i = 0; i < pairs; i++)
        {
            int column = BinOf(xs[i], xEdges);
            int row = BinOf(ys[i], yEdges);
            if (column >= 0 && row >= 0)
            {
                counts[column, row]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// How many bins to use per side for <paramref name="count"/> readings when nobody said: the
    /// square-root choice, held to at least one and at most <paramref name="most"/>.
    /// <para>
    /// MATLAB does not document how <c>binscatter</c> picks its own default, so this is a recorded
    /// divergence rather than an imitation. The square root is the same rule a histogram uses, and
    /// the cap is what keeps a million readings from asking for a thousand bins a side.
    /// </para>
    /// </summary>
    public static int SquareRootChoice(int count, int most = 100) =>
        System.Math.Clamp((int)System.Math.Ceiling(System.Math.Sqrt(System.Math.Max(0, count))), 1, most);

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
