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

    /// <summary>
    /// The bin edges when nobody named them: a count, a width, named limits, or one of MATLAB's
    /// binning rules ('auto', 'scott', 'sturges', 'sqrt', 'fd', 'integers'). This is the rule
    /// <c>histcounts</c> has always used, moved here in M77 so that a histogram drawn as bars, one
    /// drawn round a circle, and the counts a script checks them against cannot choose differently.
    /// </summary>
    public static double[] EdgesFor(
        IReadOnlyList<double> data, int? requested, double? width, double[]? limits, string rule)
    {
        IEnumerable<double> inside = limits is null
            ? data
            : data.Where(v => v >= limits[0] && v <= limits[1]);
        double[] finite = [.. inside.Where(double.IsFinite)];

        double low;
        double high;
        if (limits is not null)
        {
            (low, high) = (limits[0], limits[1]);
        }
        else if (finite.Length == 0)
        {
            (low, high) = (0, 1);
        }
        else
        {
            (low, high) = (finite.Min(), finite.Max());
        }

        if (requested is { } count)
        {
            if (high == low)
            {
                (low, high) = (low - 0.5, low + 0.5);
            }

            return Spanning(low, high, count);
        }

        if (width is { } size)
        {
            double left = limits is not null ? low : size * System.Math.Floor(low / size);
            int bins = System.Math.Max(1, (int)System.Math.Ceiling((high - left) / size));
            return Uniform(left, size, bins, high);
        }

        if (finite.Length == 0)
        {
            return [low, high];
        }

        if (low == high)
        {
            return [low - 0.5, low + 0.5]; // one distinct reading still deserves a bin around it
        }

        bool whole = finite.All(static v => v == System.Math.Floor(v));
        if (limits is null && (rule == "integers" || (rule == "auto" && whole && high - low <= 50)))
        {
            double step = System.Math.Max(1, System.Math.Ceiling((high - low + 1) / 100));
            int bins = (int)System.Math.Ceiling((high - low + 1) / step);
            return Uniform(low - 0.5, step, bins, high);
        }

        double raw = rule switch
        {
            "sturges" => (high - low) / System.Math.Ceiling(System.Math.Log2(finite.Length) + 1),
            "sqrt" => (high - low) / System.Math.Ceiling(System.Math.Sqrt(finite.Length)),
            "fd" => 2 * (Quartiles.Percentile(finite, 75) - Quartiles.Percentile(finite, 25))
                / System.Math.Cbrt(finite.Length),
            _ => 3.5 * System.Math.Sqrt(finite.Length > 1 ? Spread(finite) : 0) / System.Math.Cbrt(finite.Length),
        };

        // Limits given by name are exact — they say where the histogram starts and stops, so the
        // rule only gets to choose how many bins fit between them, never where the ends go.
        if (limits is not null)
        {
            double niceWidth = NiceEdges(raw, low, high) is { Length: > 1 } nice ? nice[1] - nice[0] : high - low;
            return Spanning(low, high, System.Math.Max(1, (int)System.Math.Round((high - low) / niceWidth)));
        }

        return NiceEdges(raw, low, high);
    }

    /// <summary>
    /// Bins on round numbers: the width is rounded to 1, 2, 3, 5 or 10 times a power of ten, and the
    /// left edge to a multiple of that width. A histogram whose bins land on readable numbers is the
    /// point of letting the function choose them.
    /// </summary>
    private static double[] NiceEdges(double raw, double low, double high)
    {
        if (!(raw > 0) || !double.IsFinite(raw))
        {
            raw = (high - low) / 10;
        }

        double powerOfTen = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
        double relative = raw / powerOfTen;
        double width = powerOfTen * (relative < 1.5 ? 1 : relative < 2.5 ? 2 : relative < 4 ? 3 : relative < 7.5 ? 5 : 10);

        double left = width * System.Math.Floor(low / width);
        int bins = System.Math.Max(1, (int)System.Math.Ceiling((high - left) / width));
        return Uniform(left, width, bins, high);
    }

    /// <summary>
    /// Bins of a fixed width. The last edge is nudged out to <paramref name="reach"/> when the
    /// arithmetic left it a hair short, because a value falling past the final edge would otherwise
    /// be counted as outside the histogram it defined.
    /// </summary>
    private static double[] Uniform(double left, double width, int bins, double reach)
    {
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = left + (i * width);
        }

        edges[^1] = System.Math.Max(edges[^1], reach);
        return edges;
    }

    /// <summary>
    /// The sample variance the Scott rule is scaled by. It is here rather than borrowed from the
    /// statistics library because this project's lowest layer may not reach that one, and one
    /// expression is a smaller price than an assembly reference.
    /// </summary>
    private static double Spread(IReadOnlyList<double> values)
    {
        double mean = 0;
        foreach (double value in values)
        {
            mean += value;
        }

        mean /= values.Count;

        double sum = 0;
        foreach (double value in values)
        {
            sum += (value - mean) * (value - mean);
        }

        return sum / (values.Count - 1);
    }
}
