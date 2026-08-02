namespace JGraph.Imaging;

/// <summary>
/// The neighbourhood-table side of binary morphology: <c>makelut</c>/<c>bwlookup</c>, the whole
/// <c>bwmorph</c> operation set, perimeters, ultimate erosion, and skeletons.
/// </summary>
/// <remarks>
/// <para>
/// Most of what <c>bwmorph</c> does is a rule about a 3×3 window, and the fastest way to apply the
/// same rule to a million pixels is to answer it once for each of the 512 possible windows and then
/// index. That is what a lookup table is, and it is why <c>makelut</c> exists as a public function
/// rather than an implementation detail: a table built here is the same object a script can build for
/// itself.
/// </para>
/// <para>
/// The index follows MATLAB's own weighting, <c>2^(r + 3c)</c> over the window — column-major, so the
/// weights read down the first column before crossing to the second. A table built by MATLAB and one
/// built here therefore mean the same thing.
/// </para>
/// </remarks>
public static class BinaryMorphology
{
    /// <summary>The operations <see cref="Morph"/> understands, in MATLAB's own spelling.</summary>
    public static readonly string[] Operations =
    [
        "bothat", "branchpoints", "bridge", "clean", "close", "diag", "dilate", "endpoints",
        "erode", "fill", "hbreak", "majority", "open", "remove", "shrink", "skel", "spur",
        "thicken", "thin", "tophat",
    ];

    /// <summary>
    /// Builds a lookup table by asking <paramref name="rule"/> about every possible neighbourhood
    /// (MATLAB <c>makelut</c>). <paramref name="order"/> is 2 or 3, giving a table of 16 or 512 entries.
    /// </summary>
    public static double[] MakeLut(Func<bool[,], double> rule, int order)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (order is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "a lookup table is built over a 2x2 or 3x3 neighbourhood.");
        }

        int cells = order * order;
        int count = 1 << cells;
        var table = new double[count];
        for (int index = 0; index < count; index++)
        {
            var window = new bool[order, order];
            for (int bit = 0; bit < cells; bit++)
            {
                // Column-major: bit b is row b % order of column b / order.
                window[bit % order, bit / order] = (index & (1 << bit)) != 0;
            }

            table[index] = rule(window);
        }

        return table;
    }

    /// <summary>
    /// Applies a lookup table to a binary image (MATLAB <c>bwlookup</c>, and its older name
    /// <c>applylut</c>). Pixels off the edge read as background.
    /// </summary>
    /// <remarks>
    /// A 512-entry table is read against the 3×3 window centred on each pixel; a 16-entry table
    /// against the 2×2 window whose lower-right corner is the pixel, which is MATLAB's convention.
    /// </remarks>
    public static ImageBuffer ApplyLut(ImageBuffer image, IReadOnlyList<double> table)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(table);
        int order = table.Count switch
        {
            16 => 2,
            512 => 3,
            _ => throw new ArgumentException(
                $"a lookup table has 16 entries (2x2) or 512 (3x3), not {table.Count}.", nameof(table)),
        };

        if (image.Channels != 1)
        {
            throw new ArgumentException("a lookup table applies to a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        var result = new ImageBuffer(h, w, 1);

        // Both window sizes hang off the same origin: the 3×3 is centred on the pixel, and the 2×2 —
        // whose offsets are only 0 and −1 — therefore has the pixel at its lower right, MATLAB's rule.
        const int origin = 1;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int index = 0;
                for (int bit = 0; bit < order * order; bit++)
                {
                    int dr = (bit % order) - origin;
                    int dc = (bit / order) - origin;
                    if (Sample(image, r + dr, c + dc))
                    {
                        index |= 1 << bit;
                    }
                }

                result[r, c, 0] = table[index];
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The perimeter pixels (MATLAB <c>bwperim</c>): foreground pixels with at least one background
    /// neighbour under the given connectivity. Outside the picture counts as background.
    /// </summary>
    public static ImageBuffer Perimeter(ImageBuffer image, int connectivity = 4)
    {
        ArgumentNullException.ThrowIfNull(image);
        MorphologicalReconstruction.CheckConnectivity(connectivity);
        int h = image.Height;
        int w = image.Width;
        var result = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (!Sample(image, r, c))
                {
                    continue;
                }

                bool onEdge = false;
                foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, connectivity))
                {
                    if (!Sample(image, nr, nc))
                    {
                        onEdge = true;
                        break;
                    }
                }

                result[r, c, 0] = onEdge ? 1.0 : 0.0;
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Applies one of the <see cref="Operations"/> the given number of times (MATLAB <c>bwmorph</c>).
    /// Pass <see cref="int.MaxValue"/> for MATLAB's <c>Inf</c> — repeat until nothing changes.
    /// </summary>
    public static ImageBuffer Morph(ImageBuffer image, string operation, int iterations = 1)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(operation);
        if (image.Channels != 1)
        {
            throw new ArgumentException("bwmorph needs a binary (single-channel) image.", nameof(image));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        string op = operation.ToLowerInvariant();
        if (Array.IndexOf(Operations, op) < 0)
        {
            throw new ArgumentException(
                $"unknown operation '{operation}' (one of: {string.Join(", ", Operations)}).", nameof(operation));
        }

        ImageBuffer current = image.Clone();
        int quiet = 0;
        for (int step = 0; step < iterations; step++)
        {
            ImageBuffer next = Once(current, op, step);
            bool changed = !Same(current, next);
            current.Dispose();
            current = next;

            // Two quiet passes, not one: the thinning operations alternate between two rules that peel
            // from opposite corners, and a pass of one rule can find nothing to do while the other
            // still would. Stopping at the first quiet pass would leave the stroke half-thinned.
            quiet = changed ? 0 : quiet + 1;
            if (quiet >= 2)
            {
                break;
            }
        }

        return current;
    }

    /// <summary>
    /// The ultimate erosion (MATLAB <c>bwulterode</c>): the regional maxima of the distance transform
    /// of the complement, which are the last points of each object to survive continued erosion.
    /// </summary>
    public static ImageBuffer UltimateErode(
        ImageBuffer image, DistanceTransforms.Metric metric = DistanceTransforms.Metric.Euclidean,
        int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ImageBuffer complement = PointOps.Complement(image);
        (double[] distance, _) = DistanceTransforms.Transform(complement, metric);

        using var field = new ImageBuffer(image.Height, image.Width, 1);
        MorphologicalReconstruction.WritePlane(field, distance, 0);
        using ImageBuffer peaks = MorphologicalReconstruction.RegionalMax(field, connectivity);

        var result = new ImageBuffer(image.Height, image.Width, 1);
        Span<double> output = result.Pixels;
        ReadOnlySpan<double> flags = peaks.Pixels;
        ReadOnlySpan<double> source = image.Pixels;
        int channels = image.Channels;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = flags[i] != 0 && source[i * channels] != 0 ? 1.0 : 0.0;
        }

        GC.KeepAlive(peaks);
        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The skeleton (MATLAB <c>bwskel</c>): a topology-preserving thinning to single-pixel strokes,
    /// with branches shorter than <paramref name="minBranchLength"/> pruned away afterwards.
    /// </summary>
    public static ImageBuffer Skeleton(ImageBuffer image, int minBranchLength = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(minBranchLength);
        ImageBuffer skeleton = Morph(image, "skel", int.MaxValue);
        if (minBranchLength <= 0)
        {
            return skeleton;
        }

        // Pruning is iterative: taking a short spur off can shorten the branch that carried it, and
        // that branch may in turn now be short enough to go.
        for (int pass = 0; pass < minBranchLength + 1; pass++)
        {
            ImageBuffer pruned = PruneOnce(skeleton, minBranchLength);
            bool changed = !Same(skeleton, pruned);
            skeleton.Dispose();
            skeleton = pruned;
            if (!changed)
            {
                break;
            }
        }

        return skeleton;
    }

    /// <summary>The default 2-D connectivity neighbourhood (MATLAB <c>conndef(2, …)</c>).</summary>
    /// <param name="minimal">True for the smallest connectivity (4), false for the largest (8).</param>
    public static double[,] ConnectivityDefinition(bool minimal)
    {
        var values = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                values[r, c] = minimal
                    ? (Math.Abs(r - 1) + Math.Abs(c - 1) <= 1 ? 1.0 : 0.0)
                    : 1.0;
            }
        }

        return values;
    }

    /// <summary>
    /// The default 3-D connectivity neighbourhood (MATLAB <c>conndef(3, …)</c>): 6-connectivity for
    /// the minimal form, 26 for the maximal one, returned as a 3×3×3 array.
    /// </summary>
    public static double[,,] ConnectivityDefinition3(bool minimal)
    {
        var values = new double[3, 3, 3];
        for (int p = 0; p < 3; p++)
        {
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    values[r, c, p] = minimal
                        ? (Math.Abs(r - 1) + Math.Abs(c - 1) + Math.Abs(p - 1) <= 1 ? 1.0 : 0.0)
                        : 1.0;
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Whether a value is a valid connectivity specifier (MATLAB <c>iptcheckconn</c>): 1, 4, 8, 6, 18
    /// or 26, or a symmetric odd-sided array of zeros and ones with a one at its centre.
    /// </summary>
    public static bool IsConnectivity(double[,] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int rows = value.GetLength(0);
        int cols = value.GetLength(1);
        if (rows == 1 && cols == 1)
        {
            return value[0, 0] is 1 or 4 or 8 or 6 or 18 or 26;
        }

        if (rows % 2 == 0 || cols % 2 == 0)
        {
            return false;
        }

        if (value[rows / 2, cols / 2] != 1)
        {
            return false;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (value[r, c] is not (0 or 1) || value[r, c] != value[rows - 1 - r, cols - 1 - c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ImageBuffer Once(ImageBuffer image, string operation, int step) => operation switch
    {
        "dilate" => Morphology.Dilate(image, StructuringElement.Square(3)),
        "erode" => Morphology.Erode(image, StructuringElement.Square(3)),
        "open" => Morphology.Open(image, StructuringElement.Square(3)),
        "close" => Morphology.Close(image, StructuringElement.Square(3)),
        "tophat" => Morphology.TopHat(image, StructuringElement.Square(3)),
        "bothat" => Morphology.BottomHat(image, StructuringElement.Square(3)),
        "thin" => ThinPass(image, step, guoHall: false, keepEndpoints: true),
        "skel" => ThinPass(image, step, guoHall: true, keepEndpoints: true),
        "shrink" => ThinPass(image, step, guoHall: false, keepEndpoints: false),
        "thicken" => Thicken(image, step),
        _ => Rule(image, operation),
    };

    private static ImageBuffer Thicken(ImageBuffer image, int step)
    {
        using ImageBuffer complement = PointOps.Complement(image);
        using ImageBuffer thinned = ThinPass(complement, step, guoHall: false, keepEndpoints: true);
        return PointOps.Complement(thinned);
    }

    /// <summary>The single-pixel rules, each a question about the 3×3 window and nothing else.</summary>
    private static ImageBuffer Rule(ImageBuffer image, string operation)
    {
        int h = image.Height;
        int w = image.Width;
        var result = new ImageBuffer(h, w, 1);
        Span<bool> window = stackalloc bool[9];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        window[((dr + 1) * 3) + dc + 1] = Sample(image, r + dr, c + dc);
                    }
                }

                result[r, c, 0] = Decide(window, operation) ? 1.0 : 0.0;
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static bool Decide(ReadOnlySpan<bool> w, string operation)
    {
        bool centre = w[4];
        bool north = w[1];
        bool south = w[7];
        bool west = w[3];
        bool east = w[5];

        switch (operation)
        {
            case "clean":
                // An isolated foreground pixel has nothing around it and goes.
                return centre && Count(w) > 1;

            case "fill":
                // A background pixel walled in on all four sides is an interior hole of one pixel.
                return centre || (north && south && west && east);

            case "remove":
                // Drop interior pixels, which is what leaves the outline behind.
                return centre && !(north && south && west && east);

            case "spur":
                return centre && Count(w) - 1 > 1;

            case "endpoints":
                return centre && Count(w) - 1 <= 1;

            case "branchpoints":
                return centre && Crossings(w) >= 3;

            case "majority":
                return Count(w) >= 5;

            case "hbreak":
                // The two H shapes, whose waist joins two rows that are already joined at both ends.
                if (!centre)
                {
                    return false;
                }

                bool horizontalH = w[0] && w[1] && w[2] && !w[3] && !w[5] && w[6] && w[7] && w[8];
                bool verticalH = w[0] && !w[1] && w[2] && w[3] && w[5] && w[6] && !w[7] && w[8];
                return !(horizontalH || verticalH);

            case "diag":
                // Fill the elbow between two pixels that are only diagonally joined, which is what
                // stops the background slipping through the same diagonal.
                return centre || (north && west) || (north && east) || (south && west) || (south && east);

            case "bridge":
                return centre || SplitsNeighbourhood(w);

            default:
                throw new ArgumentException($"unhandled operation '{operation}'.", nameof(operation));
        }
    }

    /// <summary>
    /// Whether setting a background pixel would join foreground that is otherwise apart: the eight
    /// neighbours form two or more separate runs.
    /// </summary>
    private static bool SplitsNeighbourhood(ReadOnlySpan<bool> w)
    {
        // The eight neighbours read round the ring; a run of foreground is one piece, and two or more
        // pieces means the centre is the only thing that could connect them.
        ReadOnlySpan<int> ring = [0, 1, 2, 5, 8, 7, 6, 3];
        int runs = 0;
        for (int i = 0; i < 8; i++)
        {
            bool here = w[ring[i]];
            bool before = w[ring[(i + 7) % 8]];
            if (here && !before)
            {
                runs++;
            }
        }

        return runs >= 2;
    }

    private static int Count(ReadOnlySpan<bool> w)
    {
        int count = 0;
        for (int i = 0; i < w.Length; i++)
        {
            if (w[i])
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of background-to-foreground transitions round the eight neighbours.</summary>
    private static int Crossings(ReadOnlySpan<bool> w)
    {
        ReadOnlySpan<int> ring = [0, 1, 2, 5, 8, 7, 6, 3];
        int crossings = 0;
        for (int i = 0; i < 8; i++)
        {
            if (w[ring[i]] && !w[ring[(i + 7) % 8]])
            {
                crossings++;
            }
        }

        return crossings;
    }

    /// <summary>
    /// One sub-iteration of a thinning. Both algorithms here alternate between two rules that peel
    /// from opposite corners, because peeling from one side alone walks a stroke sideways instead of
    /// narrowing it.
    /// </summary>
    private static ImageBuffer ThinPass(ImageBuffer image, int step, bool guoHall, bool keepEndpoints)
    {
        int h = image.Height;
        int w = image.Width;
        var result = image.Clone();
        bool even = step % 2 == 0;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (!Sample(image, r, c))
                {
                    continue;
                }

                bool p2 = Sample(image, r - 1, c);
                bool p3 = Sample(image, r - 1, c + 1);
                bool p4 = Sample(image, r, c + 1);
                bool p5 = Sample(image, r + 1, c + 1);
                bool p6 = Sample(image, r + 1, c);
                bool p7 = Sample(image, r + 1, c - 1);
                bool p8 = Sample(image, r, c - 1);
                bool p9 = Sample(image, r - 1, c - 1);

                bool remove = guoHall
                    ? GuoHall(even, p2, p3, p4, p5, p6, p7, p8, p9)
                    : ZhangSuen(even, keepEndpoints, p2, p3, p4, p5, p6, p7, p8, p9);
                if (remove)
                {
                    result[r, c, 0] = 0.0;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static bool ZhangSuen(
        bool even, bool keepEndpoints,
        bool p2, bool p3, bool p4, bool p5, bool p6, bool p7, bool p8, bool p9)
    {
        int neighbours = B(p2) + B(p3) + B(p4) + B(p5) + B(p6) + B(p7) + B(p8) + B(p9);
        int floor = keepEndpoints ? 2 : 1;
        if (neighbours < floor || neighbours > 6)
        {
            return false;
        }

        // Exactly one background-to-foreground transition round the ring means the pixel is not a
        // bridge: taking it away cannot break the stroke into two.
        int transitions =
            T(p2, p3) + T(p3, p4) + T(p4, p5) + T(p5, p6) +
            T(p6, p7) + T(p7, p8) + T(p8, p9) + T(p9, p2);
        if (transitions != 1)
        {
            return false;
        }

        return even
            ? !(p2 && p4 && p6) && !(p4 && p6 && p8)
            : !(p2 && p4 && p8) && !(p2 && p6 && p8);
    }

    private static bool GuoHall(
        bool even, bool p2, bool p3, bool p4, bool p5, bool p6, bool p7, bool p8, bool p9)
    {
        int c = B(!p2 && (p3 || p4)) + B(!p4 && (p5 || p6)) + B(!p6 && (p7 || p8)) + B(!p8 && (p9 || p2));
        if (c != 1)
        {
            return false;
        }

        int n1 = B(p9 || p2) + B(p3 || p4) + B(p5 || p6) + B(p7 || p8);
        int n2 = B(p2 || p3) + B(p4 || p5) + B(p6 || p7) + B(p8 || p9);
        int n = Math.Min(n1, n2);
        if (n is < 2 or > 3)
        {
            return false;
        }

        return even ? !((p6 || p7 || !p9) && p8) : !((p2 || p3 || !p5) && p4);
    }

    private static ImageBuffer PruneOnce(ImageBuffer skeleton, int minBranchLength)
    {
        int h = skeleton.Height;
        int w = skeleton.Width;

        // A branch runs from a free end to the first junction. Walking outward from each free end and
        // stopping at the first pixel with three or more neighbours measures exactly that.
        var remove = new bool[h * w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (!Sample(skeleton, r, c) || NeighbourCount(skeleton, r, c) != 1)
                {
                    continue;
                }

                var branch = new List<int>();
                int cr = r;
                int cc = c;
                int previous = -1;
                while (true)
                {
                    int here = (cr * w) + cc;
                    branch.Add(here);
                    if (branch.Count > minBranchLength)
                    {
                        branch.Clear();
                        break;
                    }

                    int next = -1;
                    foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(cr, cc, 8))
                    {
                        if (!Sample(skeleton, nr, nc))
                        {
                            continue;
                        }

                        int candidate = (nr * w) + nc;
                        if (candidate != previous)
                        {
                            next = candidate;
                            break;
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    int nextRow = next / w;
                    int nextCol = next % w;
                    if (NeighbourCount(skeleton, nextRow, nextCol) >= 3)
                    {
                        break;
                    }

                    previous = here;
                    cr = nextRow;
                    cc = nextCol;
                }

                foreach (int p in branch)
                {
                    remove[p] = true;
                }
            }
        }

        ImageBuffer result = skeleton.Clone();
        Span<double> output = result.Pixels;
        for (int i = 0; i < remove.Length; i++)
        {
            if (remove[i])
            {
                output[i] = 0.0;
            }
        }

        return result;
    }

    private static int NeighbourCount(ImageBuffer image, int r, int c)
    {
        int count = 0;
        foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, 8))
        {
            if (Sample(image, nr, nc))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Same(ImageBuffer a, ImageBuffer b)
    {
        ReadOnlySpan<double> left = a.Pixels;
        ReadOnlySpan<double> right = b.Pixels;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                GC.KeepAlive(a);
                GC.KeepAlive(b);
                return false;
            }
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return true;
    }

    private static bool Sample(ImageBuffer image, int r, int c) =>
        (uint)r < (uint)image.Height && (uint)c < (uint)image.Width && image[r, c, 0] != 0;

    private static int B(bool value) => value ? 1 : 0;

    private static int T(bool first, bool second) => !first && second ? 1 : 0;
}
