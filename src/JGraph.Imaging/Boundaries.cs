namespace JGraph.Imaging;

/// <summary>
/// Region outlines: boundary tracing (<c>bwboundaries</c>, <c>bwtraceboundary</c>), the boundary mask,
/// convex hulls (<c>bwconvhull</c>) and polygon simplification (<c>reducepoly</c>).
/// </summary>
/// <remarks>
/// The tracer is Moore-neighbour following with Jacob's stopping criterion: stand on a boundary
/// pixel, remember which neighbour you arrived from, and sweep the eight neighbours from there until
/// the next foreground pixel appears — that is the next step, and the last background cell you passed
/// is where you arrived from next time. Stopping when the <em>pair</em> (pixel, arrival direction)
/// repeats rather than when the pixel alone repeats is what makes a figure-of-eight or a one-pixel
/// isthmus trace correctly; returning to the start from a different side is not the same as being
/// finished.
/// </remarks>
public static class Boundaries
{
    /// <summary>The eight neighbour offsets in clockwise order, starting east.</summary>
    private static readonly (int R, int C)[] Ring8 =
        [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];

    /// <summary>The four neighbour offsets in clockwise order, starting east.</summary>
    private static readonly (int R, int C)[] Ring4 = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    /// <summary>
    /// Traces the outline of the connected component containing <paramref name="startRow"/>,
    /// <paramref name="startCol"/> (MATLAB <c>bwtraceboundary</c>), returning the pixels in order and
    /// closing the loop by repeating the first.
    /// </summary>
    /// <param name="mask">The foreground.</param>
    /// <param name="startRow">A boundary pixel's row.</param>
    /// <param name="startCol">Its column.</param>
    /// <param name="connectivity">4 or 8.</param>
    /// <param name="fromRow">The row of the background cell to set out from.</param>
    /// <param name="fromCol">Its column.</param>
    /// <param name="maxPoints">Stop after this many points; null for the whole loop.</param>
    /// <param name="clockwise">Which way round to go.</param>
    public static (int Row, int Col)[] Trace(
        bool[,] mask, int startRow, int startCol, int connectivity,
        int fromRow, int fromCol, int? maxPoints = null, bool clockwise = true)
    {
        ArgumentNullException.ThrowIfNull(mask);
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);
        if ((uint)startRow >= (uint)h || (uint)startCol >= (uint)w || !mask[startRow, startCol])
        {
            throw new ArgumentException("the starting pixel must be inside the image and part of the region.");
        }

        (int R, int C)[] ring = connectivity == 4 ? Ring4 : Ring8;
        var trace = new List<(int, int)> { (startRow, startCol) };

        bool Foreground(int r, int c) => (uint)r < (uint)h && (uint)c < (uint)w && mask[r, c];

        // A lone pixel has no neighbour to step to; MATLAB still reports it, as a boundary of one.
        int br = startRow;
        int bc = startCol;
        int cr = fromRow;
        int cc = fromCol;
        int firstStepR = int.MinValue;
        int firstStepC = int.MinValue;

        // A boundary cannot be longer than four steps per pixel, so anything past that means the
        // walk is not closing — a start the caller chose badly, most likely. Stopping is the only
        // safe answer: without the bound the list grows until the process runs out of memory.
        int limit = maxPoints ?? ((4 * h * w) + 8);
        while (trace.Count < limit)
        {
            int entry = IndexOf(ring, cr - br, cc - bc);
            if (entry < 0)
            {
                break;
            }

            int found = -1;
            int before = entry;
            for (int k = 1; k <= ring.Length; k++)
            {
                int step = clockwise ? entry + k : entry - k + (2 * ring.Length);
                int index = ((step % ring.Length) + ring.Length) % ring.Length;
                if (Foreground(br + ring[index].R, bc + ring[index].C))
                {
                    found = index;
                    break;
                }

                // The neighbour just examined is where the next step will have arrived from.
                before = index;
            }

            if (found < 0)
            {
                // Nothing around: an isolated pixel, and the loop is already complete.
                break;
            }

            int nextR = br + ring[found].R;
            int nextC = bc + ring[found].C;
            if (firstStepR == int.MinValue)
            {
                firstStepR = nextR;
                firstStepC = nextC;
            }
            else if (br == startRow && bc == startCol && nextR == firstStepR && nextC == firstStepC)
            {
                // Jacob's criterion: back at the start, about to take the same first step again.
                // Testing the pair rather than the pixel alone is what lets a trace pass through its
                // own starting pixel from another side without stopping short.
                break;
            }

            cr = br + ring[before].R;
            cc = bc + ring[before].C;
            br = nextR;
            bc = nextC;
            trace.Add((br, bc));
        }

        if (maxPoints is null && trace.Count > 1 &&
            (trace[^1].Item1 != startRow || trace[^1].Item2 != startCol))
        {
            trace.Add((startRow, startCol));
        }

        return [.. trace];
    }

    /// <summary>
    /// Traces every object outline, and optionally every hole (MATLAB <c>bwboundaries</c>). Returns
    /// the traces, the label map that says which object each came from, and, for each trace, the
    /// index of the trace that encloses it (−1 for an outer boundary).
    /// </summary>
    /// <param name="image">The binary image.</param>
    /// <param name="connectivity">Foreground connectivity, 4 or 8.</param>
    /// <param name="includeHoles">Whether to trace hole boundaries as well as object ones.</param>
    public static (List<(int Row, int Col)[]> Traces, int[,] Labels, int[] Parent, int ObjectCount) Find(
        ImageBuffer image, int connectivity = 8, bool includeHoles = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Channels != 1)
        {
            throw new ArgumentException("bwboundaries needs a binary (single-channel) image.", nameof(image));
        }

        int h = image.Height;
        int w = image.Width;
        var mask = new bool[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                mask[r, c] = image[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(image);
        (int[,] labels, int count) = Regions.Label(image, connectivity);

        var traces = new List<(int Row, int Col)[]>();
        var parent = new List<int>();

        // Outer boundaries first, in label order, so index k of the result belongs to object k + 1 —
        // which is what makes the returned label map usable as an index into the trace list.
        var outerIndex = new int[count + 1];
        for (int label = 1; label <= count; label++)
        {
            (int sr, int sc) = FirstPixel(labels, label);
            traces.Add(Trace(mask, sr, sc, connectivity, sr, sc - 1));
            parent.Add(-1);
            outerIndex[label] = traces.Count - 1;
        }

        int objectCount = traces.Count;
        if (!includeHoles)
        {
            return (traces, labels, [.. parent], objectCount);
        }

        // A hole is a background component that the border cannot reach. Its boundary is traced on
        // the complement, which is why the connectivity flips: 8-connected objects have 4-connected
        // holes, and vice versa.
        var background = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                background[r, c, 0] = mask[r, c] ? 0.0 : 1.0;
            }
        }

        int holeConnectivity = connectivity == 8 ? 4 : 8;
        (int[,] holeLabels, int holeCount) = Regions.Label(background, holeConnectivity);
        background.Dispose();

        var touchesBorder = new bool[holeCount + 1];
        for (int c = 0; c < w; c++)
        {
            touchesBorder[holeLabels[0, c]] = true;
            touchesBorder[holeLabels[h - 1, c]] = true;
        }

        for (int r = 0; r < h; r++)
        {
            touchesBorder[holeLabels[r, 0]] = true;
            touchesBorder[holeLabels[r, w - 1]] = true;
        }

        var holeMask = new bool[h, w];
        for (int label = 1; label <= holeCount; label++)
        {
            if (touchesBorder[label])
            {
                continue;
            }

            Array.Clear(holeMask);
            int enclosing = 0;
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (holeLabels[r, c] != label)
                    {
                        continue;
                    }

                    holeMask[r, c] = true;
                    if (enclosing == 0)
                    {
                        enclosing = EnclosingLabel(labels, r, c);
                    }
                }
            }

            (int sr, int sc) = FirstPixel(holeLabels, label);
            traces.Add(Trace(holeMask, sr, sc, holeConnectivity, sr, sc - 1));
            parent.Add(enclosing > 0 ? outerIndex[enclosing] : -1);
        }

        return (traces, labels, [.. parent], objectCount);
    }

    /// <summary>
    /// The boundary mask (MATLAB <c>boundarymask</c>): pixels sitting on a border between two labels,
    /// which for a binary image is the object outline plus the background pixels beside it.
    /// </summary>
    public static ImageBuffer BoundaryMask(ImageBuffer image, int connectivity = 8)
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
                double here = image[r, c, 0];
                bool onEdge = false;
                foreach ((int nr, int nc) in MorphologicalReconstruction.Neighbours(r, c, connectivity))
                {
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w && image[nr, nc, 0] != here)
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
    /// The convex hull of a point set, counter-clockwise in a y-up reading, by Andrew's monotone
    /// chain: sort by x then y, sweep once for the lower chain and once for the upper.
    /// </summary>
    public static (double X, double Y)[] ConvexHull(IReadOnlyList<(double X, double Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count <= 2)
        {
            return [.. points];
        }

        var sorted = new List<(double X, double Y)>(points);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

        var hull = new List<(double X, double Y)>();
        foreach ((double X, double Y) point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            (double X, double Y) point = sorted[i];
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        hull.RemoveAt(hull.Count - 1);
        return [.. hull];
    }

    /// <summary>
    /// The convex hull of every object, or of all objects together (MATLAB <c>bwconvhull</c>).
    /// </summary>
    /// <param name="image">The binary image.</param>
    /// <param name="method">"union" (one hull over everything), "objects" (one per component).</param>
    /// <param name="connectivity">Component connectivity for the "objects" method.</param>
    public static ImageBuffer ConvexHullImage(ImageBuffer image, string method = "union", int connectivity = 8)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(method);
        int h = image.Height;
        int w = image.Width;
        var result = new ImageBuffer(h, w, 1);

        if (string.Equals(method, "union", StringComparison.OrdinalIgnoreCase))
        {
            var points = new List<(double X, double Y)>();
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (image[r, c, 0] != 0)
                    {
                        AddCorners(points, r, c);
                    }
                }
            }

            FillHull(result, ConvexHull(points));
            GC.KeepAlive(image);
            return result;
        }

        if (!string.Equals(method, "objects", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"unknown method '{method}' (use 'union' or 'objects').", nameof(method));
        }

        (int[,] labels, int count) = Regions.Label(image, connectivity);
        for (int label = 1; label <= count; label++)
        {
            var points = new List<(double X, double Y)>();
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (labels[r, c] == label)
                    {
                        AddCorners(points, r, c);
                    }
                }
            }

            FillHull(result, ConvexHull(points));
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Drops vertices a polyline can do without (MATLAB <c>reducepoly</c>), by Ramer–Douglas–Peucker:
    /// keep the point furthest from the chord between the ends, recurse on both halves, and stop when
    /// nothing is further than the tolerance.
    /// </summary>
    /// <param name="points">The polyline, as [x y] rows.</param>
    /// <param name="tolerance">
    /// A fraction of the point set's largest side, matching MATLAB's scale-free reading — 0 keeps
    /// everything, 1 reduces to the two endpoints.
    /// </param>
    public static (double X, double Y)[] Reduce(IReadOnlyList<(double X, double Y)> points, double tolerance = 0.001)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count <= 2 || tolerance <= 0)
        {
            return [.. points];
        }

        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        foreach ((double x, double y) in points)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        double scale = Math.Max(maxX - minX, maxY - minY);
        double epsilon = tolerance * scale;
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        Simplify(points, 0, points.Count - 1, epsilon, keep);

        var reduced = new List<(double X, double Y)>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                reduced.Add(points[i]);
            }
        }

        return [.. reduced];
    }

    private static void Simplify(
        IReadOnlyList<(double X, double Y)> points, int first, int last, double epsilon, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        (double ax, double ay) = points[first];
        (double bx, double by) = points[last];
        double dx = bx - ax;
        double dy = by - ay;
        double length = Math.Sqrt((dx * dx) + (dy * dy));

        double worst = -1;
        int worstAt = -1;
        for (int i = first + 1; i < last; i++)
        {
            (double px, double py) = points[i];
            double distance = length > 0
                ? Math.Abs((dy * (px - ax)) - (dx * (py - ay))) / length
                : Math.Sqrt(((px - ax) * (px - ax)) + ((py - ay) * (py - ay)));
            if (distance > worst)
            {
                worst = distance;
                worstAt = i;
            }
        }

        if (worst <= epsilon || worstAt < 0)
        {
            return;
        }

        keep[worstAt] = true;
        Simplify(points, first, worstAt, epsilon, keep);
        Simplify(points, worstAt, last, epsilon, keep);
    }

    /// <summary>Whether a point is inside a polygon, by the even–odd crossing rule.</summary>
    internal static bool InsidePolygon(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            (double xi, double yi) = polygon[i];
            (double xj, double yj) = polygon[j];
            if (yi > y != yj > y && x < (((xj - xi) * (y - yi)) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    internal static void AddCorners(List<(double X, double Y)> points, int r, int c)
    {
        points.Add((c - 0.5, r - 0.5));
        points.Add((c + 0.5, r - 0.5));
        points.Add((c - 0.5, r + 0.5));
        points.Add((c + 0.5, r + 0.5));
    }

    private static void FillHull(ImageBuffer target, (double X, double Y)[] hull)
    {
        if (hull.Length == 0)
        {
            return;
        }

        for (int r = 0; r < target.Height; r++)
        {
            for (int c = 0; c < target.Width; c++)
            {
                if (InsidePolygon(hull, c, r))
                {
                    target[r, c, 0] = 1.0;
                }
            }
        }
    }

    private static (int Row, int Col) FirstPixel(int[,] labels, int label)
    {
        int h = labels.GetLength(0);
        int w = labels.GetLength(1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (labels[r, c] == label)
                {
                    return (r, c);
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(label), label, "no pixel carries that label.");
    }

    /// <summary>The object label immediately outside a hole pixel, found by walking left.</summary>
    private static int EnclosingLabel(int[,] labels, int row, int col)
    {
        for (int c = col - 1; c >= 0; c--)
        {
            if (labels[row, c] != 0)
            {
                return labels[row, c];
            }
        }

        return 0;
    }

    private static int IndexOf((int R, int C)[] ring, int dr, int dc)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            if (ring[i].R == dr && ring[i].C == dc)
            {
                return i;
            }
        }

        return -1;
    }
}
