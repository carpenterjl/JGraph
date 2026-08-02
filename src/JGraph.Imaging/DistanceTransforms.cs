namespace JGraph.Imaging;

/// <summary>
/// Distance transforms: the plain one (<c>bwdist</c>), the geodesic one that has to go round obstacles
/// (<c>bwdistgeodesic</c>), and the gray-weighted one where travelling through a bright pixel costs
/// more than travelling through a dark one (<c>graydist</c>).
/// </summary>
public static class DistanceTransforms
{
    /// <summary>How distance is measured.</summary>
    public enum Metric
    {
        /// <summary>Straight-line distance — MATLAB's default.</summary>
        Euclidean,

        /// <summary>Steps along the axes only; a diagonal costs two.</summary>
        CityBlock,

        /// <summary>A diagonal costs the same as a step, so distance is the larger of the two offsets.</summary>
        Chessboard,

        /// <summary>Axis steps cost 1 and diagonals √2, which approximates the straight line.</summary>
        QuasiEuclidean,
    }

    /// <summary>
    /// The distance from every pixel to the nearest nonzero pixel, and the flat row-major index of
    /// that nearest pixel (MATLAB <c>[D, idx] = bwdist(BW)</c>).
    /// </summary>
    /// <remarks>
    /// The Euclidean case is exact, by Felzenszwalb and Huttenlocher's method: the squared distance
    /// transform separates into one pass down the columns and one across the rows, and each of those
    /// is the lower envelope of a family of parabolas, which a single sweep maintaining the envelope
    /// computes in time linear in the row. Chamfer approximations need no such thing — two passes over
    /// the picture propagate the best-so-far in each direction — but they are approximations, and
    /// MATLAB's default is the exact answer.
    /// </remarks>
    public static (double[] Distance, int[] NearestIndex) Transform(
        ImageBuffer image, Metric metric = Metric.Euclidean)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("a distance transform needs a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        var seed = new bool[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                seed[(r * w) + c] = image[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(image);
        return metric == Metric.Euclidean ? Exact(seed, h, w) : Chamfer(seed, h, w, metric);
    }

    /// <summary>
    /// The geodesic distance from the seed pixels, travelling only through the mask (MATLAB
    /// <c>bwdistgeodesic</c>). Pixels outside the mask, and mask pixels the seeds cannot reach, come
    /// back as infinity.
    /// </summary>
    public static double[] Geodesic(
        ImageBuffer mask, IReadOnlyList<(int Row, int Col)> seeds, Metric metric = Metric.Euclidean)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(seeds);
        int h = mask.Height;
        int w = mask.Width;
        var passable = new bool[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                passable[(r * w) + c] = mask[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(mask);

        var distance = new double[h * w];
        Array.Fill(distance, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)h || (uint)col >= (uint)w)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {h}x{w} image.");
            }

            int p = (row * w) + col;
            if (!passable[p])
            {
                continue;
            }

            distance[p] = 0.0;
            queue.Enqueue(p, 0.0);
        }

        Dijkstra(queue, distance, passable, h, w, metric, (_, _) => 1.0);
        return distance;
    }

    /// <summary>
    /// The gray-weighted distance (MATLAB <c>graydist</c>): the cost of a step is the average of the
    /// two samples it joins, times the step's own length, so a path through a dark valley is cheap and
    /// one over a bright ridge is not.
    /// </summary>
    public static double[] GrayWeighted(
        ImageBuffer image, IReadOnlyList<(int Row, int Col)> seeds, Metric metric = Metric.Euclidean)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(seeds);
        if (image.Channels != 1)
        {
            throw new ArgumentException("graydist needs a grayscale image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        var weights = new double[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                weights[(r * w) + c] = image[r, c, 0];
            }
        }

        GC.KeepAlive(image);

        var passable = new bool[h * w];
        Array.Fill(passable, true);
        var distance = new double[h * w];
        Array.Fill(distance, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        foreach ((int row, int col) in seeds)
        {
            if ((uint)row >= (uint)h || (uint)col >= (uint)w)
            {
                throw new ArgumentOutOfRangeException(nameof(seeds),
                    $"seed ({row}, {col}) is outside the {h}x{w} image.");
            }

            int p = (row * w) + col;
            distance[p] = 0.0;
            queue.Enqueue(p, 0.0);
        }

        Dijkstra(queue, distance, passable, h, w, metric, (from, to) => (weights[from] + weights[to]) / 2.0);
        return distance;
    }

    /// <summary>The step length between two neighbours under a metric.</summary>
    private static double StepLength(int dr, int dc, Metric metric)
    {
        bool diagonal = dr != 0 && dc != 0;
        return metric switch
        {
            Metric.CityBlock => diagonal ? 2.0 : 1.0,
            Metric.Chessboard => 1.0,
            _ => diagonal ? Math.Sqrt(2.0) : 1.0,
        };
    }

    private static void Dijkstra(
        PriorityQueue<int, double> queue, double[] distance, bool[] passable,
        int height, int width, Metric metric, Func<int, int, double> weight)
    {
        var settled = new bool[distance.Length];
        while (queue.TryDequeue(out int p, out double cost))
        {
            if (settled[p])
            {
                continue;
            }

            settled[p] = true;
            int r = p / width;
            int c = p % width;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0)
                    {
                        continue;
                    }

                    // City-block distance has no diagonal step at all; giving it one of cost 2 would
                    // let a path cut a corner for the same price as going round it, which is the same
                    // number but the wrong route for the nearest-pixel answer.
                    if (metric == Metric.CityBlock && dr != 0 && dc != 0)
                    {
                        continue;
                    }

                    int nr = r + dr;
                    int nc = c + dc;
                    if ((uint)nr >= (uint)height || (uint)nc >= (uint)width)
                    {
                        continue;
                    }

                    int q = (nr * width) + nc;
                    if (!passable[q] || settled[q])
                    {
                        continue;
                    }

                    double candidate = cost + (weight(p, q) * StepLength(dr, dc, metric));
                    if (candidate < distance[q])
                    {
                        distance[q] = candidate;
                        queue.Enqueue(q, candidate);
                    }
                }
            }
        }
    }

    /// <summary>The exact Euclidean transform, one dimension at a time.</summary>
    private static (double[] Distance, int[] NearestIndex) Exact(bool[] seed, int height, int width)
    {
        const double far = 1e18;
        var squared = new double[seed.Length];
        var nearest = new int[seed.Length];

        // Pass one, down each column: the distance to the nearest seed in that column alone.
        var columnValues = new double[height];
        var columnSource = new int[height];
        var envelope = new int[Math.Max(height, width) + 1];
        var boundary = new double[Math.Max(height, width) + 2];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                columnValues[r] = seed[(r * width) + c] ? 0.0 : far;
                columnSource[r] = seed[(r * width) + c] ? r : -1;
            }

            LowerEnvelope(columnValues, columnSource, height, envelope, boundary,
                out double[] outValues, out int[] outSource);
            for (int r = 0; r < height; r++)
            {
                squared[(r * width) + c] = outValues[r];
                nearest[(r * width) + c] = outSource[r] < 0 ? -1 : (outSource[r] * width) + c;
            }
        }

        // Pass two, across each row, over the column results: the true two-dimensional answer, because
        // the squared distance is the sum of the two one-dimensional ones.
        var rowValues = new double[width];
        var rowSource = new int[width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                rowValues[c] = squared[(r * width) + c];
                rowSource[c] = nearest[(r * width) + c];
            }

            LowerEnvelope(rowValues, rowSource, width, envelope, boundary,
                out double[] outValues, out int[] outSource);
            for (int c = 0; c < width; c++)
            {
                squared[(r * width) + c] = outValues[c];
                nearest[(r * width) + c] = outSource[c];
            }
        }

        var distance = new double[seed.Length];
        for (int i = 0; i < distance.Length; i++)
        {
            distance[i] = squared[i] >= far ? double.PositiveInfinity : Math.Sqrt(squared[i]);
        }

        return (distance, nearest);
    }

    /// <summary>
    /// The lower envelope of the parabolas <c>(x − i)² + f(i)</c>. <paramref name="carried"/> travels
    /// with each parabola so the winning one can say which seed it came from.
    /// </summary>
    private static void LowerEnvelope(
        double[] f, int[] carried, int n, int[] envelope, double[] boundary,
        out double[] values, out int[] source)
    {
        int k = 0;
        envelope[0] = 0;
        boundary[0] = double.NegativeInfinity;
        boundary[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double intersection;
            while (true)
            {
                int p = envelope[k];
                intersection = ((f[q] + ((double)q * q)) - (f[p] + ((double)p * p))) / (2.0 * (q - p));
                if (intersection <= boundary[k] && k > 0)
                {
                    k--;
                    continue;
                }

                break;
            }

            k++;
            envelope[k] = q;
            boundary[k] = intersection;
            boundary[k + 1] = double.PositiveInfinity;
        }

        values = new double[n];
        source = new int[n];
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (boundary[k + 1] < q)
            {
                k++;
            }

            int p = envelope[k];
            values[q] = ((double)(q - p) * (q - p)) + f[p];
            source[q] = carried[p];
        }
    }

    /// <summary>A two-pass chamfer transform for the approximate metrics.</summary>
    private static (double[] Distance, int[] NearestIndex) Chamfer(
        bool[] seed, int height, int width, Metric metric)
    {
        var distance = new double[seed.Length];
        var nearest = new int[seed.Length];
        for (int i = 0; i < seed.Length; i++)
        {
            distance[i] = seed[i] ? 0.0 : double.PositiveInfinity;
            nearest[i] = seed[i] ? i : -1;
        }

        void Relax(int p, int r, int c, int dr, int dc)
        {
            int nr = r + dr;
            int nc = c + dc;
            if ((uint)nr >= (uint)height || (uint)nc >= (uint)width)
            {
                return;
            }

            int q = (nr * width) + nc;
            double candidate = distance[q] + StepLength(dr, dc, metric);
            if (candidate < distance[p])
            {
                distance[p] = candidate;
                nearest[p] = nearest[q];
            }
        }

        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int p = (r * width) + c;
                Relax(p, r, c, -1, -1);
                Relax(p, r, c, -1, 0);
                Relax(p, r, c, -1, 1);
                Relax(p, r, c, 0, -1);
            }
        }

        for (int r = height - 1; r >= 0; r--)
        {
            for (int c = width - 1; c >= 0; c--)
            {
                int p = (r * width) + c;
                Relax(p, r, c, 1, 1);
                Relax(p, r, c, 1, 0);
                Relax(p, r, c, 1, -1);
                Relax(p, r, c, 0, 1);
            }
        }

        return (distance, nearest);
    }
}
