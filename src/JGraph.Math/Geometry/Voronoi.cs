using JGraph.Core.Primitives;

namespace JGraph.Maths.Geometry;

/// <summary>
/// The Voronoi diagram of a set of points, computed as the dual of their Delaunay triangulation:
/// every triangle contributes its circumcentre as a vertex, every pair of triangles sharing an edge
/// contributes the segment between their circumcentres, and every hull edge contributes a ray
/// running outward forever.
/// </summary>
/// <remarks>
/// Circumcentres of triangles whose vertices are cocircular coincide — four points on a square
/// produce two triangles with the same centre — and the diagram merges them, because the duplicate
/// is an artefact of which diagonal the triangulation happened to pick, not a feature of the
/// diagram. The merge tolerance scales with the point set's span. MATLAB computes this through
/// Qhull, whose joggling and merging make slightly different calls on degenerate input; the shape
/// of the answer agrees, vertex order and exact ties may not.
/// </remarks>
public sealed class VoronoiDiagram
{
    private readonly double[] _siteX;
    private readonly double[] _siteY;

    internal VoronoiDiagram(
        IReadOnlyList<Point2D> vertices,
        IReadOnlyList<(int From, int To)> edges,
        IReadOnlyList<(int Start, Point2D Direction)> rays,
        IReadOnlyList<int[]> cells,
        double[] siteX,
        double[] siteY)
    {
        Vertices = vertices;
        Edges = edges;
        Rays = rays;
        Cells = cells;
        _siteX = siteX;
        _siteY = siteY;
    }

    /// <summary>The finite vertices — deduplicated circumcentres of the Delaunay triangles.</summary>
    public IReadOnlyList<Point2D> Vertices { get; }

    /// <summary>The finite edges, as index pairs into <see cref="Vertices"/>.</summary>
    public IReadOnlyList<(int From, int To)> Edges { get; }

    /// <summary>
    /// The unbounded edges: each starts at a finite vertex and runs in a unit direction forever.
    /// One per hull edge of the triangulation.
    /// </summary>
    public IReadOnlyList<(int Start, Point2D Direction)> Rays { get; }

    /// <summary>
    /// One cell per input point: the indices of its vertices in counter-clockwise order, with −1
    /// standing for the point at infinity in an unbounded cell. Points on the convex hull have
    /// unbounded cells; interior points have closed ones.
    /// </summary>
    public IReadOnlyList<int[]> Cells { get; }

    /// <summary>
    /// The diagram as drawable segments: every finite edge as it is, and every ray cut off where it
    /// leaves a box round the whole picture, padded by <paramref name="margin"/> of the box's larger
    /// side. A ray has no end, so drawing one is always a choice of where to stop; MATLAB stops at
    /// the axes limits it is about to set, which is the same rule read from the other side.
    /// </summary>
    public (Point2D From, Point2D To)[] Segments(double margin = 0.1)
    {
        double minX = _siteX[0], maxX = _siteX[0], minY = _siteY[0], maxY = _siteY[0];
        void Cover(double x, double y)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        for (int i = 1; i < _siteX.Length; i++)
        {
            Cover(_siteX[i], _siteY[i]);
        }

        // The vertices are covered too, so a ray always starts inside the box and the exit distance
        // below is positive — an obtuse triangle's centre can sit well outside the points it belongs
        // to, and a box drawn round the sites alone would have left it behind.
        foreach (Point2D vertex in Vertices)
        {
            Cover(vertex.X, vertex.Y);
        }

        double pad = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-12) * margin;
        minX -= pad;
        maxX += pad;
        minY -= pad;
        maxY += pad;

        var segments = new List<(Point2D From, Point2D To)>(Edges.Count + Rays.Count);
        foreach ((int from, int to) in Edges)
        {
            segments.Add((Vertices[from], Vertices[to]));
        }

        foreach ((int start, Point2D direction) in Rays)
        {
            Point2D at = Vertices[start];
            double reach = double.PositiveInfinity;
            reach = Math.Min(reach, Exit(at.X, direction.X, minX, maxX));
            reach = Math.Min(reach, Exit(at.Y, direction.Y, minY, maxY));
            if (double.IsFinite(reach))
            {
                segments.Add((at, new Point2D(at.X + (direction.X * reach), at.Y + (direction.Y * reach))));
            }
        }

        return [.. segments];
    }

    /// <summary>How far along a direction a point may travel before it leaves one pair of walls.</summary>
    private static double Exit(double at, double step, double low, double high) =>
        step > 0 ? (high - at) / step
        : step < 0 ? (low - at) / step
        : double.PositiveInfinity;
}

/// <summary>Builds <see cref="VoronoiDiagram"/>s from point sets.</summary>
public static class Voronoi
{
    /// <summary>The Voronoi diagram of <paramref name="x"/>/<paramref name="y"/>.</summary>
    public static VoronoiDiagram FromPoints(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("The coordinate arrays must be the same length.", nameof(y));
        }

        if (x.Length < 3)
        {
            throw new ArgumentException("A Voronoi diagram needs at least three points.", nameof(x));
        }

        return FromTriangulation(x, y, Delaunay.Triangulate(x, y));
    }

    /// <summary>
    /// The Voronoi diagram dual to a triangulation that is already in hand — <c>voronoi(x, y, TRI)</c>
    /// asking for the diagram of a particular triangulation rather than of the points alone.
    /// </summary>
    /// <remarks>
    /// The dual only means what it says for a Delaunay triangulation: a circumcentre is equidistant
    /// from three sites whatever triangulation named them, but only the Delaunay one guarantees no
    /// fourth site is nearer. A different table produces a picture, not a Voronoi diagram.
    /// </remarks>
    public static VoronoiDiagram FromTriangulation(double[] x, double[] y, int[,] triangles)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(triangles);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("The coordinate arrays must be the same length.", nameof(y));
        }

        if (triangles.GetLength(1) != 3)
        {
            throw new ArgumentException("A triangulation has three vertices per row.", nameof(triangles));
        }

        int triangleCount = triangles.GetLength(0);
        if (triangleCount == 0)
        {
            // Every triangle degenerated, which is what a collinear point set produces.
            throw new ArgumentException("The points are collinear; their Voronoi diagram has no vertices.", nameof(x));
        }

        for (int t = 0; t < triangleCount; t++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int v = triangles[t, corner];
                if (v < 0 || v >= x.Length)
                {
                    throw new ArgumentException(
                        $"A triangle names point {v}, but there are only {x.Length}.", nameof(triangles));
                }
            }
        }

        double span = Span(x, y);
        double tolerance = span * 1e-9;

        // Circumcentres, merged when they coincide. vertexOf maps a triangle to its merged vertex.
        var vertices = new List<Point2D>();
        var vertexOf = new int[triangleCount];
        var byCell = new Dictionary<(long, long), List<int>>();
        for (int t = 0; t < triangleCount; t++)
        {
            Point2D centre = Circumcentre(x, y, triangles, t);
            vertexOf[t] = AddMerged(vertices, byCell, centre, tolerance);
        }

        // Every undirected triangle edge, with the triangles that own it. Two owners make a finite
        // Voronoi edge between their centres; one owner is a hull edge and makes a ray.
        var owners = new Dictionary<(int Low, int High), (int First, int Second)>();
        for (int t = 0; t < triangleCount; t++)
        {
            for (int side = 0; side < 3; side++)
            {
                int a = triangles[t, side];
                int b = triangles[t, (side + 1) % 3];
                (int low, int high) = a < b ? (a, b) : (b, a);
                owners[(low, high)] = owners.TryGetValue((low, high), out (int First, int Second) seen)
                    ? (seen.First, t)
                    : (t, -1);
            }
        }

        var edges = new List<(int From, int To)>();
        var rays = new List<(int Start, Point2D Direction)>();
        var hull = new HashSet<int>();
        foreach (KeyValuePair<(int Low, int High), (int First, int Second)> pair in owners)
        {
            if (pair.Value.Second >= 0)
            {
                int from = vertexOf[pair.Value.First];
                int to = vertexOf[pair.Value.Second];
                if (from != to)
                {
                    edges.Add(from < to ? (from, to) : (to, from));
                }

                continue;
            }

            // A hull edge: the ray leaves the owning triangle's centre along the edge's outward
            // perpendicular — the side away from the triangle's third vertex, not away from the
            // centre, because an obtuse triangle's circumcentre already sits outside it.
            hull.Add(pair.Key.Low);
            hull.Add(pair.Key.High);
            int owner = pair.Value.First;
            int third = Third(triangles, owner, pair.Key.Low, pair.Key.High);
            double ex = x[pair.Key.High] - x[pair.Key.Low];
            double ey = y[pair.Key.High] - y[pair.Key.Low];
            double px = -ey;
            double py = ex;
            double midX = 0.5 * (x[pair.Key.Low] + x[pair.Key.High]);
            double midY = 0.5 * (y[pair.Key.Low] + y[pair.Key.High]);
            if ((px * (midX - x[third])) + (py * (midY - y[third])) < 0)
            {
                px = -px;
                py = -py;
            }

            double length = Math.Sqrt((px * px) + (py * py));
            rays.Add((vertexOf[owner], new Point2D(px / length, py / length)));
        }

        edges.Sort();
        for (int i = edges.Count - 1; i > 0; i--)
        {
            if (edges[i] == edges[i - 1])
            {
                edges.RemoveAt(i); // two triangle pairs merged onto the same vertex pair
            }
        }

        rays.Sort(static (l, r) => l.Start.CompareTo(r.Start));

        // Cells: the merged centres of each point's triangles, ordered by angle around the point,
        // with the point at infinity spliced into a hull point's largest angular gap — which is the
        // direction its two rays leave in.
        var incident = new List<int>[x.Length];
        for (int t = 0; t < triangleCount; t++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int point = triangles[t, corner];
                (incident[point] ??= []).Add(vertexOf[t]);
            }
        }

        var cells = new int[x.Length][];
        for (int p = 0; p < x.Length; p++)
        {
            cells[p] = incident[p] is { } around
                ? OrderCell(vertices, around, x[p], y[p], hull.Contains(p))
                : hull.Contains(p) ? [-1] : [];
        }

        return new VoronoiDiagram(vertices, edges, rays, cells, x, y);
    }

    /// <summary>The larger of the point set's two extents, floored away from zero.</summary>
    private static double Span(double[] x, double[] y)
    {
        double minX = x[0], maxX = x[0], minY = y[0], maxY = y[0];
        for (int i = 1; i < x.Length; i++)
        {
            minX = Math.Min(minX, x[i]);
            maxX = Math.Max(maxX, x[i]);
            minY = Math.Min(minY, y[i]);
            maxY = Math.Max(maxY, y[i]);
        }

        return Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-12);
    }

    /// <summary>
    /// The circumcentre of triangle <paramref name="t"/>, from the intersection of two
    /// perpendicular bisectors. The division is by twice the triangle's signed area, which the
    /// triangulation guarantees is not zero — it drops degenerate triangles before returning.
    /// </summary>
    private static Point2D Circumcentre(double[] x, double[] y, int[,] triangles, int t)
    {
        double ax = x[triangles[t, 0]];
        double ay = y[triangles[t, 0]];
        double bx = x[triangles[t, 1]];
        double by = y[triangles[t, 1]];
        double cx = x[triangles[t, 2]];
        double cy = y[triangles[t, 2]];

        double d = 2 * ((ax * (by - cy)) + (bx * (cy - ay)) + (cx * (ay - by)));
        double a2 = (ax * ax) + (ay * ay);
        double b2 = (bx * bx) + (by * by);
        double c2 = (cx * cx) + (cy * cy);
        double ux = ((a2 * (by - cy)) + (b2 * (cy - ay)) + (c2 * (ay - by))) / d;
        double uy = ((a2 * (cx - bx)) + (b2 * (ax - cx)) + (c2 * (bx - ax))) / d;
        return new Point2D(ux, uy);
    }

    /// <summary>
    /// Adds <paramref name="centre"/> to <paramref name="vertices"/> unless one already sits within
    /// the tolerance, and answers the index either way. Candidates are found through a grid of
    /// tolerance-sized cells, checking the cell and its eight neighbours, so coincident centres are
    /// caught however they round.
    /// </summary>
    private static int AddMerged(
        List<Point2D> vertices,
        Dictionary<(long, long), List<int>> byCell,
        Point2D centre,
        double tolerance)
    {
        long cellX = (long)Math.Floor(centre.X / tolerance);
        long cellY = (long)Math.Floor(centre.Y / tolerance);
        for (long dx = -1; dx <= 1; dx++)
        {
            for (long dy = -1; dy <= 1; dy++)
            {
                if (!byCell.TryGetValue((cellX + dx, cellY + dy), out List<int>? candidates))
                {
                    continue;
                }

                foreach (int index in candidates)
                {
                    double gapX = vertices[index].X - centre.X;
                    double gapY = vertices[index].Y - centre.Y;
                    if ((gapX * gapX) + (gapY * gapY) <= tolerance * tolerance)
                    {
                        return index;
                    }
                }
            }
        }

        vertices.Add(centre);
        int added = vertices.Count - 1;
        if (!byCell.TryGetValue((cellX, cellY), out List<int>? mine))
        {
            byCell[(cellX, cellY)] = mine = [];
        }

        mine.Add(added);
        return added;
    }

    /// <summary>The vertex of triangle <paramref name="t"/> that is neither <paramref name="a"/> nor <paramref name="b"/>.</summary>
    private static int Third(int[,] triangles, int t, int a, int b)
    {
        for (int corner = 0; corner < 3; corner++)
        {
            int v = triangles[t, corner];
            if (v != a && v != b)
            {
                return v;
            }
        }

        throw new InvalidOperationException("A triangle repeated a vertex.");
    }

    /// <summary>
    /// The cell's distinct vertices in counter-clockwise order around its point, with −1 spliced
    /// into the widest angular gap when the cell is unbounded.
    /// </summary>
    private static int[] OrderCell(List<Point2D> vertices, List<int> around, double px, double py, bool unbounded)
    {
        var distinct = new List<int>();
        foreach (int v in around)
        {
            if (!distinct.Contains(v))
            {
                distinct.Add(v);
            }
        }

        distinct.Sort((l, r) =>
            Math.Atan2(vertices[l].Y - py, vertices[l].X - px)
                .CompareTo(Math.Atan2(vertices[r].Y - py, vertices[r].X - px)));

        if (!unbounded)
        {
            return [.. distinct];
        }

        // The widest gap between consecutive vertices is where the cell opens out; the point at
        // infinity goes there so the listing still walks the boundary in one direction.
        int gapAfter = distinct.Count - 1;
        double widest = double.NegativeInfinity;
        for (int i = 0; i < distinct.Count; i++)
        {
            double here = Math.Atan2(vertices[distinct[i]].Y - py, vertices[distinct[i]].X - px);
            double next = Math.Atan2(
                vertices[distinct[(i + 1) % distinct.Count]].Y - py,
                vertices[distinct[(i + 1) % distinct.Count]].X - px);
            double gap = next - here;
            if (i == distinct.Count - 1)
            {
                gap += 2 * Math.PI;
            }

            if (gap > widest)
            {
                widest = gap;
                gapAfter = i;
            }
        }

        var cell = new List<int>(distinct.Count + 1);
        for (int i = 0; i < distinct.Count; i++)
        {
            cell.Add(distinct[i]);
            if (i == gapAfter)
            {
                cell.Add(-1);
            }
        }

        return [.. cell];
    }
}
