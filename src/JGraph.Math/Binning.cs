using System.Runtime.CompilerServices;

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
        /// <remarks>Compiled optimised from the first call: this is the leaf of every histogram and
        /// discretize loop, and ten million calls into tier-0 code cost the row (ADR 0158).</remarks>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
        edges[0] = low; // the left end itself, sign of zero and all, as linspace answers it
        for (int i = 1; i <= bins; i++)
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
        if (limits is not null)
        {
            return EdgesWithinLimits(data, requested, width, limits[0], limits[1], rule, sampleRoot);
        }

        double[] finite = [.. data.Where(double.IsFinite)];

        double low;
        double high;
        if (finite.Length == 0)
        {
            (low, high) = (0, 1);
        }
        else
        {
            (low, high) = (finite.Min(), finite.Max());
        }

        if (requested is { } count)
        {
            // Without limits the count is a request and the edges are still chosen to be readable.
            return CountedEdges(low, high, count);
        }

        if (width is { } size)
        {
            double left = size * System.Math.Floor(low / size);
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
        if (rule == "integers" || (rule == "auto" && whole && high - low <= 50))
        {
            // integerrule without limits (ADR 0361): unit bins centred on the integers, or bins a
            // power of ten wide when the readings would need more than the most allowed, and the
            // ends rounded (half away from zero) onto that width before the half-steps are added.
            double maximumBins = sampleRoot == 4 ? 1024 : 65536;
            double scale = System.Math.Max(System.Math.Abs(low), System.Math.Abs(high));
            double step = high - low > maximumBins
                ? MatlabPow10(System.Math.Ceiling(System.Math.Log10((high - low) / maximumBins)))
                : Ulp(scale) > 1 ? MatlabPow10(System.Math.Ceiling(System.Math.Log10(Ulp(scale)))) : 1;
            double first = step * System.Math.Round(low / step, MidpointRounding.AwayFromZero);
            double last = step * System.Math.Round(high / step, MidpointRounding.AwayFromZero);
            double[] centred = Colon(System.Math.Floor(first) - (0.5 * step), step, System.Math.Ceiling(last) + (0.5 * step));
            if (centred.Length >= 2)
            {
                return centred;
            }
        }

        double raw = rule switch
        {
            "sturges" => (high - low) / System.Math.Ceiling(System.Math.Log2(finite.Length) + 1),
            "sqrt" => (high - low) / System.Math.Ceiling(System.Math.Sqrt(finite.Length)),
            // fdrule's guard against too small an IQR (extreme outliers): a tenth of the range at
            // least, and a width of one for a single reading (ADR 0361).
            "fd" => finite.Length > 1
                ? 2 * System.Math.Max(
                    Quartiles.Percentile(finite, 75) - Quartiles.Percentile(finite, 25), (high - low) / 10)
                    * System.Math.Pow(finite.Length, -1.0 / sampleRoot)
                : 1,
            _ => 3.5 * System.Math.Sqrt(finite.Length > 1 ? Spread(finite) : 0)
                / System.Math.Pow(finite.Length, 1.0 / sampleRoot),
        };

        return NiceEdges(raw, low, high);
    }

    /// <summary>
    /// The edges under named limits, transcribed from R2025b's <c>histcounts.m</c> (its
    /// <c>BinLimits</c> branch) and the private rules beside it, <c>binpickerbl</c> and
    /// <c>integerrule</c> (open item 271, ADR 0361). Limits are exact: they are the first and last
    /// edge whatever the rule. A count spaces that many bins between them; a width steps from the
    /// low limit by the colon and closes the last bin on the high one; a rule is applied to the
    /// readings inside the limits only, and then fits whole bins to the span: the scale rules
    /// (Scott, Freedman–Diaconis) by <c>ceil(span / width)</c>, Sturges and the square root by
    /// their own count, and the integer rule by half-integer edges inside the limits.
    /// <c>histcounts2</c> differs in three places, all kept: the fourth root, a count spaced by
    /// <c>linspace</c>, and at most 1024 bins (<paramref name="sampleRoot"/> 4 says which).
    /// </summary>
    private static double[] EdgesWithinLimits(
        IReadOnlyList<double> data, int? requested, double? width, double low, double high, string rule,
        int sampleRoot)
    {
        bool twoDimensional = sampleRoot == 4;
        int maximumBins = twoDimensional ? 1024 : 65536;

        if (requested is { } count)
        {
            int bins = System.Math.Max(1, count);
            if (twoDimensional)
            {
                return Spanning(low, high, bins); // linspace(minx, maxx, numbins + 1)
            }

            // [minx + (0:numbins-1).*((maxx-minx)/numbins), maxx]
            var spaced = new double[bins + 1];
            double step = (high - low) / bins;
            for (int i = 0; i < bins; i++)
            {
                spaced[i] = low + (i * step);
            }

            spaced[bins] = high;
            return spaced;
        }

        if (width is { } size)
        {
            return LimitedByWidth(low, high, MaxIgnoringNaN(size, (high - low) / maximumBins));
        }

        double[] inside = [.. data.Where(v => v >= low && v <= high)];
        double fewest = inside.Length == 0 ? double.NaN : inside.Min();
        double most = inside.Length == 0 ? double.NaN : inside.Max();
        const double Flintmax = 9007199254740992.0;

        string chosen = rule;
        if (chosen == "auto")
        {
            bool whole = inside.Length > 0 && inside.All(static v => v == System.Math.Round(v));
            chosen = whole && high - low <= 50 && high <= Flintmax / 2 && low >= -Flintmax / 2
                ? "integers"
                : "scott";
        }

        switch (chosen)
        {
            case "integers":
                return IntegerEdgesWithinLimits(inside, fewest, most, low, high, maximumBins);

            case "sturges":
            {
                double bins = System.Math.Max(System.Math.Ceiling(System.Math.Log2(inside.Length) + 1), 1);
                return Spanning(low, high, (int)bins);
            }

            case "sqrt":
            {
                double bins = System.Math.Max(System.Math.Ceiling(System.Math.Sqrt(inside.Length)), 1);
                return Spanning(low, high, (int)bins);
            }

            case "fd":
            {
                int n = inside.Length;
                double binWidth = 1;
                if (n > 1)
                {
                    // Guard against too small an IQR, as fdrule does: some extreme outliers.
                    double iq = System.Math.Max(
                        Quartiles.Percentile(inside, 75) - Quartiles.Percentile(inside, 25), (most - fewest) / 10);
                    binWidth = 2 * iq * System.Math.Pow(n, -1.0 / sampleRoot);
                }

                return ScaledWithinLimits(fewest, most, low, high, binWidth, inside.Length);
            }

            default:
            {
                double spread = inside.Length switch
                {
                    0 => double.NaN,
                    1 => 0,
                    _ => System.Math.Sqrt(Spread(inside)),
                };
                double binWidth = 3.5 * spread / System.Math.Pow(inside.Length, 1.0 / sampleRoot);
                return ScaledWithinLimits(fewest, most, low, high, binWidth, inside.Length);
            }
        }
    }

    /// <summary>
    /// R2025b's <c>binpickerbl</c>: as many whole bins of the rule's width as cover the span, spaced
    /// by <c>linspace</c> between the limits; one bin when the readings inside are none or constant.
    /// </summary>
    private static double[] ScaledWithinLimits(
        double fewest, double most, double low, double high, double binWidth, int readings)
    {
        double scale = System.Math.Max(System.Math.Abs(fewest), System.Math.Abs(most));
        double range = most - fewest;
        binWidth = MaxIgnoringNaN(binWidth, Ulp(scale));
        const double SqrtEpsilon = 1.4901161193847656e-08;
        const double RealMin = 2.2250738585072014e-308;
        if (readings > 0 && range > System.Math.Max(SqrtEpsilon * scale, RealMin))
        {
            double bins = System.Math.Max(System.Math.Ceiling((high - low) / binWidth), 1);
            return Spanning(low, high, (int)System.Math.Min(bins, int.MaxValue - 1));
        }

        return [low, high];
    }

    /// <summary>
    /// R2025b's <c>integerrule</c> under limits: the limits, and between them the half-integer
    /// edges (or half-multiples of a power of ten, when the readings span more bins than the
    /// most allowed, or when one is too large for a unit step to register).
    /// </summary>
    private static double[] IntegerEdgesWithinLimits(
        double[] inside, double fewest, double most, double low, double high, int maximumBins)
    {
        double binWidth = 1;
        double first;
        double last;
        if (inside.Length > 0)
        {
            double scale = System.Math.Max(System.Math.Abs(fewest), System.Math.Abs(most));
            double range = most - fewest;
            if (range > maximumBins)
            {
                binWidth = MatlabPow10(System.Math.Ceiling(System.Math.Log10(range / maximumBins)));
            }
            else if (Ulp(scale) > 1)
            {
                binWidth = MatlabPow10(System.Math.Ceiling(System.Math.Log10(Ulp(scale))));
            }

            first = (binWidth * System.Math.Ceiling(low / binWidth)) + 0.5;
            last = (binWidth * System.Math.Floor(high / binWidth)) - 0.5;
        }
        else
        {
            first = System.Math.Ceiling(low) + 0.5;
            last = System.Math.Floor(high) - 0.5;
        }

        double[] middle = Colon(first, binWidth, last);
        var edges = new double[middle.Length + 2];
        edges[0] = low;
        Array.Copy(middle, 0, edges, 1, middle.Length);
        edges[^1] = high;
        return edges;
    }

    /// <summary>
    /// Bins of one width stepped from <paramref name="low"/> by the colon, the last one closed on
    /// <paramref name="high"/> — <c>histcounts</c>' <c>minx:binWidth:maxx</c>, with <c>maxx</c>
    /// appended when the colon stops short of it or takes only one step. A histogram whose limits
    /// are set afterwards keeps its width this way (open item 271).
    /// </summary>
    public static double[] LimitedByWidth(double low, double high, double width)
    {
        double[] stepped = Colon(low, width, high);
        if (stepped.Length == 0)
        {
            return [low, high];
        }

        if (stepped[^1] < high || stepped.Length == 1)
        {
            var closed = new double[stepped.Length + 1];
            Array.Copy(stepped, closed, stepped.Length);
            closed[^1] = high;
            return closed;
        }

        return stepped;
    }

    /// <summary>
    /// MATLAB's <c>a:d:b</c> for a positive step, as this project's colon evaluates it
    /// (<c>Interpreter.RangeCountOf</c> and <c>PackedMath.RangeElement</c>): the count with four
    /// epsilons of grace, the first half counted up from <c>a</c> and the rest down from the last
    /// element reached.
    /// </summary>
    private static double[] Colon(double start, double step, double stop)
    {
        double ratio = (stop - start) / step;
        if (double.IsNaN(ratio) || ratio < 0 || !double.IsFinite(ratio) || !(step > 0))
        {
            return [];
        }

        const double MachineEpsilon = 2.220446049250313e-16;
        long count = (long)System.Math.Floor(ratio * (1 + (4 * MachineEpsilon))) + 1;
        if (count > int.MaxValue / 2)
        {
            throw new ArgumentException("That bin width makes too many bins.", nameof(step));
        }

        var values = new double[count];
        long steps = count - 1;
        double reached = start + (steps * step);
        for (long i = 0; i < count; i++)
        {
            values[i] = i * 2 <= steps ? start + (i * step) : reached - ((steps - i) * step);
        }

        return values;
    }

    /// <summary>MATLAB's <c>max(a, b)</c> of two scalars: a NaN loses to a number.</summary>
    private static double MaxIgnoringNaN(double a, double b) =>
        double.IsNaN(a) ? b : double.IsNaN(b) ? a : System.Math.Max(a, b);

    /// <summary>
    /// R2025b's <c>10^e</c> for a whole exponent. It is the correctly rounded decimal except at the
    /// exponents listed, where R2025b's power lands one ulp away (probe <c>pw2</c>, exponents −40
    /// to 40; probe <c>bp1</c> read the same 10^-5 inside binpicker itself). binpicker's widths are
    /// built on it: a width a whole number of tenths is <c>0.1 * 14</c> there, 1.4000000000000001,
    /// not the decimal 1.4, and an ulp-low 10^-5 can take <c>ceil(ll / p10)</c> one unit up (ADR 0361).
    /// </summary>
    internal static double MatlabPow10(double exponent)
    {
        if (!double.IsFinite(exponent) || exponent != System.Math.Floor(exponent) || System.Math.Abs(exponent) > 300)
        {
            return System.Math.Pow(10, exponent);
        }

        int e = (int)exponent;
        return e switch
        {
            -39 => 1.0000000000000001e-39,
            -32 => 9.9999999999999992e-33,
            -29 => 1.0000000000000001e-29,
            -24 => 1.0000000000000001e-24,
            -21 => 1.0000000000000001e-21,
            -20 => 1.0000000000000001e-20,
            -17 => 9.9999999999999992e-18,
            -11 => 1.0000000000000001e-11,
            -5 => 9.9999999999999991e-06,
            29 => 1.0000000000000001e+29,
            34 => 1.0000000000000001e+34,
            39 => 1.0000000000000001e+39,
            _ => double.Parse("1e" + e.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture),
        };
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
    /// <summary>
    /// R2025b's bins for a count over no readings (none, or none finite): <c>0:n</c>, unit bins from
    /// zero, binpicker.m's empty-input branch (open item 327, ADR 0372). The MATLAB dialect's
    /// <c>histogram(x, n)</c> and <c>histcounts(x, n)</c> take these; JGS keeps (0, 1) cut in n.
    /// </summary>
    public static double[] UnitEdges(int count)
    {
        int bins = System.Math.Max(1, count);
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = i;
        }

        return edges;
    }

    public static double[] CountedEdges(double low, double high, int count)
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

        double powerOfTen = MatlabPow10(System.Math.Floor(System.Math.Log10(raw)));
        double width = powerOfTen * System.Math.Floor(raw / powerOfTen);
        double left = System.Math.Max(
            System.Math.Min(width * System.Math.Floor(low / width), low), -double.MaxValue);

        if (bins > 1)
        {
            double lowest = (high - left) / bins;
            double highest = (high - left) / (bins - 1);
            double exponent = System.Math.Floor(System.Math.Log10(highest - lowest));

            // binpicker's p10*ceil(ll./p10), transcribed with R2025b's own 10^e (probe bp1). ADR 0158
            // wrote the width as the decimal (units / 10^-e): at 10^-1 that misses (seven bins 1.4
            // wide are 0.1 * 14 = 1.4000000000000001 in R2025b, so histogram(x, 7)'s fourth edge is
            // -0.7999999999999998 there, not -0.8000000000000007), and at 10^-5 R2025b's ulp-low power
            // makes 256 bins over [-1.25, 1.31512] 1003 hundred-thousandths wide, not 1002 (ADR 0361).
            double step = MatlabPow10(exponent);
            width = step * System.Math.Ceiling(lowest / step);
        }

        double right = System.Math.Min(System.Math.Max(left + (bins * width), high), double.MaxValue);
        // The first edge is the left edge itself, as binpicker writes it: -0 stays -0 where
        // left + 0 * width would answer +0 (ADR 0158).
        var edges = new double[bins + 1];
        edges[0] = left;
        for (int i = 1; i < bins; i++)
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

        double powerOfTen = MatlabPow10(System.Math.Floor(System.Math.Log10(raw)));
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
        edges[0] = left; // the left edge itself: -0 stays -0 (ADR 0158)
        for (int i = 1; i <= bins; i++)
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
