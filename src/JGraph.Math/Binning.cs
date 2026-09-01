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
        BinFinder finder = BinFinder.For(edges);
        if (values is double[] flat)
        {
            foreach (double value in flat)
            {
                int bin = finder.Of(value);
                if (bin >= 0)
                {
                    counts[bin]++;
                }
            }

            return counts;
        }

        for (int i = 0; i < values.Count; i++)
        {
            int bin = finder.Of(values[i]);
            if (bin >= 0)
            {
                counts[bin]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Which bin a value falls in, over a set of edges read once instead of once per value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is <see cref="BinOf(double, IReadOnlyList{double})"/>'s, unchanged, and so is every
    /// answer. What changes is how the bin is reached. A histogram's edges are nearly always evenly
    /// spread — every one this project chooses for itself is, since <see cref="Spanning"/> and
    /// <see cref="Uniform"/> are the only two that build them — and for evenly spread edges the bin
    /// is one subtraction and one multiply rather than a walk down a binary search that reads a
    /// different cache line at every step.
    /// </para>
    /// <para>
    /// Arithmetic on doubles does not land exactly, so the guess is checked against the edges it
    /// claims to sit between and stepped until it does. Because the edges were measured to be evenly
    /// spread to within a quarter of a bin, that step happens at most twice; a set that somehow needs
    /// more is handed to the search, which is also what an unevenly spread set gets from the start.
    /// The answer is therefore the search's answer whatever the edges look like, and the arithmetic
    /// is only ever a shortcut to it.
    /// </para>
    /// </remarks>
    public readonly struct BinFinder
    {
        private readonly double[] _edges;
        private readonly double _first;
        private readonly double _perWidth;
        private readonly int _bins;

        private BinFinder(double[] edges, double perWidth)
        {
            _edges = edges;
            _first = edges.Length > 0 ? edges[0] : 0;
            _perWidth = perWidth;
            _bins = edges.Length - 1;
        }

        /// <summary>A finder over <paramref name="edges"/>, measuring once whether they are evenly spread.</summary>
        public static BinFinder For(IReadOnlyList<double> edges)
        {
            ArgumentNullException.ThrowIfNull(edges);
            double[] own = edges as double[] ?? [.. edges];
            return new BinFinder(own, EvenWidthOf(own));
        }

        /// <summary>Which bin <paramref name="value"/> falls in, or −1 for one outside every bin.</summary>
        public int Of(double value)
        {
            if (_bins < 1)
            {
                return -1;
            }

            double[] edges = _edges;
            if (double.IsNaN(value) || value < edges[0] || value > edges[^1])
            {
                return -1;
            }

            if (value == edges[^1])
            {
                return _bins - 1; // the last bin is closed at both ends
            }

            if (_perWidth <= 0)
            {
                return Searched(edges, value);
            }

            int bin = Guess(value);
            for (int step = 0; step < RepairSteps; step++)
            {
                if (bin > 0 && value < edges[bin])
                {
                    bin--;
                }
                else if (bin < _bins - 1 && value >= edges[bin + 1])
                {
                    bin++;
                }
                else
                {
                    return bin;
                }
            }

            return Searched(edges, value);
        }

        /// <summary>
        /// The same, for bins that own their right edge rather than their left — <c>discretize</c>'s
        /// <c>'IncludedEdge', 'right'</c>, where the first bin is the one closed at both ends.
        /// </summary>
        public int OfRightClosed(double value)
        {
            if (_bins < 1)
            {
                return -1;
            }

            double[] edges = _edges;
            if (double.IsNaN(value) || value < edges[0] || value > edges[^1])
            {
                return -1;
            }

            if (value == edges[0])
            {
                return 0;
            }

            if (_perWidth <= 0)
            {
                return SearchedRight(edges, value);
            }

            int bin = Guess(value);
            for (int step = 0; step < RepairSteps; step++)
            {
                if (bin > 0 && value <= edges[bin])
                {
                    bin--;
                }
                else if (bin < _bins - 1 && value > edges[bin + 1])
                {
                    bin++;
                }
                else
                {
                    return bin;
                }
            }

            return SearchedRight(edges, value);
        }

        private const int RepairSteps = 4;

        private int Guess(double value)
        {
            int bin = (int)((value - _first) * _perWidth);
            return bin < 0 ? 0 : bin > _bins - 1 ? _bins - 1 : bin;
        }

        /// <summary>
        /// One over the width when the edges are evenly spread to within a quarter of a bin, and zero
        /// when they are not — which is also what a set holding an infinity or a NaN reports.
        /// </summary>
        private static double EvenWidthOf(double[] edges)
        {
            int bins = edges.Length - 1;
            if (bins < 1)
            {
                return 0;
            }

            double width = (edges[^1] - edges[0]) / bins;
            if (!double.IsFinite(width) || !(width > 0))
            {
                return 0;
            }

            double slack = width * 0.25;
            for (int i = 1; i <= bins; i++)
            {
                if (!(edges[i] >= edges[i - 1])
                    || System.Math.Abs(edges[i] - (edges[0] + (i * width))) > slack)
                {
                    return 0;
                }
            }

            return 1 / width;
        }

        private static int Searched(double[] edges, double value)
        {
            int low = 0;
            int high = edges.Length - 1;
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

        private static int SearchedRight(double[] edges, double value)
        {
            int low = 0;
            int high = edges.Length - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (value <= edges[mid])
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

        BinFinder across = BinFinder.For(xEdges);
        BinFinder down = BinFinder.For(yEdges);
        int pairs = System.Math.Min(xs.Count, ys.Count);
        for (int i = 0; i < pairs; i++)
        {
            int column = across.Of(xs[i]);
            int row = down.Of(ys[i]);
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
        IReadOnlyList<double> data, int? requested, double? width, double[]? limits, string rule) =>
        EdgesFor(data, requested, width, limits, rule, 3);

    /// <summary>
    /// The same chooser with the sample-count root named. One dimension of a histogram divides by
    /// the cube root of the sample count; each dimension of a two-dimensional one divides by the
    /// fourth root, because the same readings are being spread over bins in two directions at once.
    /// <c>histcounts2</c> passes 4 here and everything else takes the default.
    /// </summary>
    public static double[] EdgesFor(
        IReadOnlyList<double> data, int? requested, double? width, double[]? limits, string rule,
        int sampleRoot)
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
            // Named limits are exact — they say where the histogram starts and stops — so the count
            // only decides how many bins fit between them. Without them the count is a request and
            // the edges are still chosen to be readable, which is the rule below.
            if (limits is not null)
            {
                if (high == low)
                {
                    (low, high) = (low - 0.5, low + 0.5);
                }

                return Spanning(low, high, count);
            }

            return CountedEdges(low, high, count);
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
                / System.Math.Pow(finite.Length, 1.0 / sampleRoot),
            _ => 3.5 * System.Math.Sqrt(finite.Length > 1 ? Spread(finite) : 0)
                / System.Math.Pow(finite.Length, 1.0 / sampleRoot),
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
    /// The edges for a bin count the caller named, chosen to land on readable numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Splitting the exact range into <c>count</c> equal pieces is the obvious answer and it is not
    /// MATLAB's. Asking for 256 bins over readings that happen to run from 5.96e-6 to 0.99999 gives
    /// edges at those two numbers and every multiple of 0.0039062 between them, which is a histogram
    /// nobody can read the axis of. MATLAB spends the freedom a bin count leaves — it is a count, not
    /// a set of edges — on making the width and the left edge round, exactly as the automatic rule
    /// does, and covers the data by reaching past it rather than by stopping on it.
    /// </para>
    /// <para>
    /// The width is chosen in two passes. The first rounds the raw width <em>down</em> to a whole
    /// multiple of its own power of ten, purely to have something round to place the left edge on.
    /// The second then asks what width, starting from that edge, puts the largest reading inside the
    /// last bin: anything from <c>(xmax - left) / count</c> up to <c>(xmax - left) / (count - 1)</c>
    /// does, and the roundest number in that interval is the one it takes. That is why 256 bins over
    /// the same readings come out 0.00391 wide rather than 0.0039062 — three digits instead of
    /// seventeen, for a right edge 0.001 past the data.
    /// </para>
    /// <para>
    /// Transcribed from R2024a's own <c>binpicker</c> rather than reconstructed from its answers, and
    /// checked against them: the width, the left edge and the right edge all agree for every count
    /// the suite asks for.
    /// </para>
    /// </remarks>
    private static double[] CountedEdges(double low, double high, int count)
    {
        int bins = System.Math.Max(1, count);
        double scale = System.Math.Max(System.Math.Abs(low), System.Math.Abs(high));
        double raw = System.Math.Max((high - low) / bins, Ulp(scale));

        // Nearly constant data has no width to be clever about: put the readings in the middle of a
        // span whose ends are whole or half numbers, which is what MATLAB does with the same case.
        const double SqrtEpsilon = 1.4901161193847656e-08;
        if (!(high - low > System.Math.Max(SqrtEpsilon * scale, double.Epsilon)))
        {
            double range = System.Math.Max(1, System.Math.Ceiling(bins * Ulp(scale)));
            double flatLeft = System.Math.Floor(2 * (low - (range / 4))) / 2;
            double flatRight = System.Math.Ceiling(2 * (high + (range / 4))) / 2;
            return Spanning(flatLeft, flatRight, bins);
        }

        double powerOfTen = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
        double width = powerOfTen * System.Math.Floor(raw / powerOfTen);
        double left = System.Math.Max(
            System.Math.Min(width * System.Math.Floor(low / width), low), -double.MaxValue);

        if (bins > 1)
        {
            double lowest = (high - left) / bins;
            double highest = (high - left) / (bins - 1);
            double step = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(highest - lowest)));
            width = step * System.Math.Ceiling(lowest / step);
        }

        double right = System.Math.Min(System.Math.Max(left + (bins * width), high), double.MaxValue);
        var edges = new double[bins + 1];
        for (int i = 0; i < bins; i++)
        {
            edges[i] = left + (i * width);
        }

        edges[bins] = right;
        return edges;
    }

    /// <summary>The distance from <paramref name="value"/> to the next double, which is MATLAB's <c>eps(x)</c>.</summary>
    private static double Ulp(double value)
    {
        double magnitude = System.Math.Abs(value);
        if (!double.IsFinite(magnitude))
        {
            return double.NaN;
        }

        return magnitude == 0 ? double.Epsilon : System.Math.BitIncrement(magnitude) - magnitude;
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
