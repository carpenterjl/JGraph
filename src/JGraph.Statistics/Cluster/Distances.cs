namespace JGraph.Statistics.Cluster;

/// <summary>How far apart two observations are held to be.</summary>
public enum DistanceMetric
{
    /// <summary>The straight-line distance.</summary>
    Euclidean,

    /// <summary>The straight-line distance without the square root.</summary>
    SquaredEuclidean,

    /// <summary>Euclidean after dividing each variable by its own spread.</summary>
    StandardizedEuclidean,

    /// <summary>Euclidean in the metric the data's covariance defines.</summary>
    Mahalanobis,

    /// <summary>The sum of the coordinate differences.</summary>
    CityBlock,

    /// <summary>The p-norm of the coordinate differences.</summary>
    Minkowski,

    /// <summary>The largest coordinate difference.</summary>
    Chebychev,

    /// <summary>One less the cosine of the angle between the two observations.</summary>
    Cosine,

    /// <summary>One less the correlation of the two observations, each centred on its own mean.</summary>
    Correlation,

    /// <summary>One less the correlation of the two observations' ranks.</summary>
    Spearman,

    /// <summary>The fraction of coordinates that differ.</summary>
    Hamming,

    /// <summary>The fraction of coordinates that differ among those where at least one is non-zero.</summary>
    Jaccard,
}

/// <summary>
/// A metric together with whatever the data had to supply before it could be evaluated.
/// </summary>
/// <remarks>
/// Three of the twelve metrics are not functions of the two observations alone: the standardized
/// Euclidean distance needs each variable's spread, the Mahalanobis distance needs the covariance's
/// inverse, and Spearman's needs each observation's ranks. Those are computed once, here, from the
/// data the caller is about to measure — which is also why a measure built for one data set must not
/// be reused on another, and why <see cref="Distances.Between"/> builds its own from the two sets
/// stacked.
/// </remarks>
public sealed class DistanceMeasure
{
    private readonly double[]? _scale;
    private readonly double[,]? _inverse;
    private readonly Dictionary<double[], int>? _lookup;
    private readonly Dictionary<double[], double[]> _ranks = new(ReferenceEqualityComparer.Instance);

    private DistanceMeasure(
        DistanceMetric metric, double exponent, double[]? scale, double[,]? inverse,
        Dictionary<double[], int>? lookup = null)
    {
        Metric = metric;
        Exponent = exponent;
        _scale = scale;
        _inverse = inverse;
        _lookup = lookup;
    }

    /// <summary>
    /// A measure that reads its answers out of a distance matrix the caller already had.
    /// </summary>
    /// <remarks>
    /// The rows handed in must be the rows of that matrix themselves, because the pair is found by the
    /// identity of the arrays rather than by their contents — which is exact, and is what lets a caller
    /// who computed distances some other way feed them to any of the clustering routines unchanged.
    /// </remarks>
    public static DistanceMeasure Precomputed(IReadOnlyList<double[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var index = new Dictionary<double[], int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < rows.Count; i++)
        {
            index[rows[i]] = i;
        }

        return new DistanceMeasure(DistanceMetric.Euclidean, 2, null, null, index);
    }

    /// <summary>Which metric this measures.</summary>
    public DistanceMetric Metric { get; }

    /// <summary>The Minkowski exponent; two for every other metric.</summary>
    public double Exponent { get; }

    /// <summary>
    /// Builds a measure of <paramref name="metric"/> over <paramref name="data"/>.
    /// </summary>
    /// <param name="metric">Which distance to measure.</param>
    /// <param name="data">Every observation the measure will be asked about, one per row.</param>
    /// <param name="exponent">The Minkowski exponent, or null for two.</param>
    /// <param name="scale">The per-variable divisor for the standardized Euclidean distance, or null to take it from the data.</param>
    /// <param name="covariance">The covariance for the Mahalanobis distance, or null to take it from the data.</param>
    public static DistanceMeasure Create(
        DistanceMetric metric,
        IReadOnlyList<double[]> data,
        double? exponent = null,
        double[]? scale = null,
        double[,]? covariance = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        double p = exponent ?? 2;
        if (metric == DistanceMetric.Minkowski && (!(p > 0) || double.IsNaN(p)))
        {
            throw new ArgumentException("The Minkowski exponent must be a positive number.", nameof(exponent));
        }

        int width = data.Count > 0 ? data[0].Length : 0;
        switch (metric)
        {
            case DistanceMetric.StandardizedEuclidean:
            {
                double[] spreads = scale ?? Spreads(data, width);
                if (spreads.Length != width)
                {
                    throw new ArgumentException(
                        "The standardized Euclidean scaling needs one value for each variable.", nameof(scale));
                }

                foreach (double s in spreads)
                {
                    if (!(s >= 0) || double.IsNaN(s))
                    {
                        throw new ArgumentException(
                            "The standardized Euclidean scaling must be non-negative.", nameof(scale));
                    }
                }

                return new DistanceMeasure(metric, p, spreads, null);
            }

            case DistanceMetric.Mahalanobis:
            {
                double[,] sigma = covariance ?? Covariance(data, width);
                if (sigma.GetLength(0) != width || sigma.GetLength(1) != width)
                {
                    throw new ArgumentException(
                        "The Mahalanobis covariance must be square with one row for each variable.",
                        nameof(covariance));
                }

                double[,]? inverse = Invert(sigma)
                    ?? throw new ArgumentException(
                        "The Mahalanobis covariance is singular, so no distance is defined.", nameof(covariance));
                return new DistanceMeasure(metric, p, null, inverse);
            }

            default:
                return new DistanceMeasure(metric, p, null, null);
        }
    }

    /// <summary>The distance between two observations.</summary>
    public double Distance(double[] a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (_lookup is not null)
        {
            if (!_lookup.TryGetValue(b, out int j))
            {
                throw new ArgumentException(
                    "A precomputed measure was asked about a point that is not one of its own rows.", nameof(b));
            }

            // Row i of the matrix holds observation i's distance to everything, so the pair is one
            // lookup and one subscript; the row the caller handed in is the one doing the asking.
            return a[j];
        }

        if (a.Length != b.Length)
        {
            throw new ArgumentException("Two observations must have the same number of variables.", nameof(b));
        }

        switch (Metric)
        {
            case DistanceMetric.Euclidean:
                return Math.Sqrt(SumOfSquares(a, b));

            case DistanceMetric.SquaredEuclidean:
                return SumOfSquares(a, b);

            case DistanceMetric.StandardizedEuclidean:
            {
                double total = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    double s = _scale![i];
                    double gap = a[i] - b[i];

                    // A variable that does not vary contributes nothing when the two agree there and
                    // everything when they do not, which is the limit of dividing by a vanishing scale.
                    total += s > 0 ? gap * gap / (s * s) : gap == 0 ? 0 : double.PositiveInfinity;
                }

                return Math.Sqrt(total);
            }

            case DistanceMetric.Mahalanobis:
            {
                int n = a.Length;
                var gap = new double[n];
                for (int i = 0; i < n; i++)
                {
                    gap[i] = a[i] - b[i];
                }

                double total = 0;
                for (int i = 0; i < n; i++)
                {
                    double row = 0;
                    for (int j = 0; j < n; j++)
                    {
                        row += _inverse![i, j] * gap[j];
                    }

                    total += gap[i] * row;
                }

                return Math.Sqrt(Math.Max(total, 0));
            }

            case DistanceMetric.CityBlock:
            {
                double total = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    total += Math.Abs(a[i] - b[i]);
                }

                return total;
            }

            case DistanceMetric.Minkowski:
            {
                if (double.IsPositiveInfinity(Exponent))
                {
                    goto case DistanceMetric.Chebychev;
                }

                double total = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    total += Math.Pow(Math.Abs(a[i] - b[i]), Exponent);
                }

                return Math.Pow(total, 1 / Exponent);
            }

            case DistanceMetric.Chebychev:
            {
                double largest = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    largest = Math.Max(largest, Math.Abs(a[i] - b[i]));
                }

                return largest;
            }

            case DistanceMetric.Cosine:
                return 1 - CosineOf(a, b);

            case DistanceMetric.Correlation:
                return 1 - CosineOf(Centred(a), Centred(b));

            case DistanceMetric.Spearman:
                return 1 - CosineOf(Centred(RanksOf(a)), Centred(RanksOf(b)));

            case DistanceMetric.Hamming:
            {
                int differing = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] != b[i])
                    {
                        differing++;
                    }
                }

                return a.Length == 0 ? 0 : (double)differing / a.Length;
            }

            case DistanceMetric.Jaccard:
            {
                int differing = 0;
                int either = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    bool nonZero = a[i] != 0 || b[i] != 0;
                    if (!nonZero)
                    {
                        continue;
                    }

                    either++;
                    if (a[i] != b[i])
                    {
                        differing++;
                    }
                }

                return either == 0 ? 0 : (double)differing / either;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(Metric));
        }
    }

    private static double SumOfSquares(double[] a, double[] b)
    {
        double total = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double gap = a[i] - b[i];
            total += gap * gap;
        }

        return total;
    }

    private static double CosineOf(double[] a, double[] b)
    {
        double dot = 0;
        double left = 0;
        double right = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            left += a[i] * a[i];
            right += b[i] * b[i];
        }

        double scale = Math.Sqrt(left) * Math.Sqrt(right);
        return scale > 0 ? dot / scale : double.NaN;
    }

    private static double[] Centred(double[] values)
    {
        double mean = 0;
        foreach (double value in values)
        {
            mean += value;
        }

        mean = values.Length > 0 ? mean / values.Length : 0;
        var centred = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            centred[i] = values[i] - mean;
        }

        return centred;
    }

    private double[] RanksOf(double[] observation)
    {
        // Every pair the caller asks about re-uses the same observations, so ranking each one once and
        // remembering it turns an n² problem back into an n one. The key is the array itself, by
        // reference, because that is what the caller hands back each time.
        if (_ranks.TryGetValue(observation, out double[]? cached))
        {
            return cached;
        }

        (double[] ranks, _) = DescriptiveStatistics.TiedRanks(
            observation, DescriptiveStatistics.TieAdjustment.PairCount);
        _ranks[observation] = ranks;
        return ranks;
    }

    private static double[] Spreads(IReadOnlyList<double[]> data, int width)
    {
        var spreads = new double[width];
        for (int c = 0; c < width; c++)
        {
            var column = new double[data.Count];
            for (int r = 0; r < data.Count; r++)
            {
                column[r] = data[r][c];
            }

            spreads[c] = DescriptiveStatistics.StandardDeviation(column, population: false);
        }

        return spreads;
    }

    private static double[,] Covariance(IReadOnlyList<double[]> data, int width)
    {
        int n = data.Count;
        var means = new double[width];
        foreach (double[] row in data)
        {
            for (int c = 0; c < width; c++)
            {
                means[c] += row[c];
            }
        }

        for (int c = 0; c < width; c++)
        {
            means[c] = n > 0 ? means[c] / n : 0;
        }

        var sigma = new double[width, width];
        foreach (double[] row in data)
        {
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    sigma[i, j] += (row[i] - means[i]) * (row[j] - means[j]);
                }
            }
        }

        double df = n > 1 ? n - 1 : 1;
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < width; j++)
            {
                sigma[i, j] /= df;
            }
        }

        return sigma;
    }

    private static double[,]? Invert(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var work = new double[n, 2 * n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                work[r, c] = matrix[r, c];
            }

            work[r, n + r] = 1;
        }

        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(work[r, c]) > Math.Abs(work[pivot, c]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(work[pivot, c]) < 1e-14)
            {
                return null;
            }

            if (pivot != c)
            {
                for (int k = 0; k < 2 * n; k++)
                {
                    (work[c, k], work[pivot, k]) = (work[pivot, k], work[c, k]);
                }
            }

            double lead = work[c, c];
            for (int k = 0; k < 2 * n; k++)
            {
                work[c, k] /= lead;
            }

            for (int r = 0; r < n; r++)
            {
                if (r == c || work[r, c] == 0)
                {
                    continue;
                }

                double factor = work[r, c];
                for (int k = 0; k < 2 * n; k++)
                {
                    work[r, k] -= factor * work[c, k];
                }
            }
        }

        var inverse = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                inverse[r, c] = work[r, n + c];
            }
        }

        return inverse;
    }
}

/// <summary>
/// Distances between observations: every pair within one set, every pair across two, the two ways of
/// writing the first of those down, and the two neighbourhood searches.
/// </summary>
/// <remarks>
/// A set of pairwise distances is held the way MathWorks holds it — the upper triangle read across
/// each row in turn, so the pair (i, j) with i &lt; j sits at position (i·(2n − i − 3))/2 + j − 1.
/// That ordering is not an implementation detail: it is what <c>squareform</c> and <c>linkage</c>
/// agree on, and it is the thing a caller who indexes the vector by hand relies on.
/// </remarks>
public static class Distances
{
    /// <summary>Every pair of observations, as the condensed upper triangle.</summary>
    public static double[] Pairwise(IReadOnlyList<double[]> rows, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(measure);

        int n = rows.Count;
        var condensed = new double[n * (n - 1) / 2];
        int at = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                condensed[at++] = measure.Distance(rows[i], rows[j]);
            }
        }

        return condensed;
    }

    /// <summary>Every observation of one set against every observation of another.</summary>
    public static double[,] Between(
        IReadOnlyList<double[]> left, IReadOnlyList<double[]> right, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(measure);

        var distances = new double[left.Count, right.Count];
        for (int i = 0; i < left.Count; i++)
        {
            for (int j = 0; j < right.Count; j++)
            {
                distances[i, j] = measure.Distance(left[i], right[j]);
            }
        }

        return distances;
    }

    /// <summary>The condensed distances written out as a symmetric matrix with a zero diagonal.</summary>
    public static double[,] SquareForm(IReadOnlyList<double> condensed)
    {
        ArgumentNullException.ThrowIfNull(condensed);
        int n = SideOf(condensed.Count);
        var square = new double[n, n];
        int at = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                square[i, j] = condensed[at];
                square[j, i] = condensed[at];
                at++;
            }
        }

        return square;
    }

    /// <summary>The upper triangle of a symmetric matrix, read the way <see cref="Pairwise"/> writes it.</summary>
    public static double[] CondensedForm(double[,] square)
    {
        ArgumentNullException.ThrowIfNull(square);
        int n = square.GetLength(0);
        if (square.GetLength(1) != n)
        {
            throw new ArgumentException("A distance matrix must be square.", nameof(square));
        }

        var condensed = new double[n * (n - 1) / 2];
        int at = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                condensed[at++] = square[i, j];
            }
        }

        return condensed;
    }

    /// <summary>How many observations a condensed vector of the given length describes.</summary>
    /// <exception cref="ArgumentException">No whole number of observations gives that length.</exception>
    public static int SideOf(int length)
    {
        int n = (int)Math.Round((1 + Math.Sqrt(1 + (8.0 * length))) / 2);
        if (n * (n - 1) / 2 != length)
        {
            throw new ArgumentException(
                "That many distances do not describe every pair of any number of observations.", nameof(length));
        }

        return n;
    }

    /// <summary>The <paramref name="k"/> nearest members of <paramref name="data"/> to each query.</summary>
    /// <returns>One row per query: the indices in increasing distance, and those distances.</returns>
    public static (int[][] Index, double[][] Distance) Nearest(
        IReadOnlyList<double[]> data, IReadOnlyList<double[]> queries, int k, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(measure);
        if (k < 1)
        {
            throw new ArgumentException("The number of neighbours must be at least one.", nameof(k));
        }

        int wanted = Math.Min(k, data.Count);
        var indices = new int[queries.Count][];
        var distances = new double[queries.Count][];
        for (int q = 0; q < queries.Count; q++)
        {
            (int[] order, double[] measured) = Ordered(data, queries[q], measure);
            indices[q] = order[..wanted];
            distances[q] = measured[..wanted];
        }

        return (indices, distances);
    }

    /// <summary>Every member of <paramref name="data"/> within <paramref name="radius"/> of each query.</summary>
    public static (int[][] Index, double[][] Distance) Within(
        IReadOnlyList<double[]> data, IReadOnlyList<double[]> queries, double radius, DistanceMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(measure);
        if (!(radius >= 0))
        {
            throw new ArgumentException("The search radius must be non-negative.", nameof(radius));
        }

        var indices = new int[queries.Count][];
        var distances = new double[queries.Count][];
        for (int q = 0; q < queries.Count; q++)
        {
            (int[] order, double[] measured) = Ordered(data, queries[q], measure);
            int count = 0;
            while (count < measured.Length && measured[count] <= radius)
            {
                count++;
            }

            indices[q] = order[..count];
            distances[q] = measured[..count];
        }

        return (indices, distances);
    }

    /// <summary>
    /// The squared Mahalanobis distance from each row of <paramref name="points"/> to the centre of
    /// <paramref name="reference"/>, in the metric the reference's own covariance defines.
    /// </summary>
    public static double[] Mahalanobis(IReadOnlyList<double[]> points, IReadOnlyList<double[]> reference)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Count == 0)
        {
            throw new ArgumentException("The reference sample cannot be empty.", nameof(reference));
        }

        int width = reference[0].Length;
        if (reference.Count <= width)
        {
            throw new ArgumentException(
                "The reference sample needs more observations than variables for its covariance to be invertible.",
                nameof(reference));
        }

        var centre = new double[width];
        foreach (double[] row in reference)
        {
            for (int c = 0; c < width; c++)
            {
                centre[c] += row[c];
            }
        }

        for (int c = 0; c < width; c++)
        {
            centre[c] /= reference.Count;
        }

        DistanceMeasure measure = DistanceMeasure.Create(DistanceMetric.Mahalanobis, reference);
        var squared = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double d = measure.Distance(points[i], centre);
            squared[i] = d * d;
        }

        return squared;
    }

    private static (int[] Order, double[] Distance) Ordered(
        IReadOnlyList<double[]> data, double[] query, DistanceMeasure measure)
    {
        var measured = new double[data.Count];
        var order = new int[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            measured[i] = measure.Distance(data[i], query);
            order[i] = i;
        }

        // Two points the same distance away are reported in the order the data was given. The sort
        // itself makes no such promise, so the index breaks the tie explicitly — which is what makes a
        // nearest-neighbour answer reproducible rather than merely correct.
        Array.Sort(order, (a, b) =>
        {
            int byDistance = measured[a].CompareTo(measured[b]);
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        });

        var sorted = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            sorted[i] = measured[order[i]];
        }

        return (order, sorted);
    }
}
