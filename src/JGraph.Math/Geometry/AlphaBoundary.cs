using System.Globalization;

namespace JGraph.Maths.Geometry;

/// <summary>
/// The boundary MATLAB's <c>boundary</c> draws around a set of points: an alpha shape's outer
/// edge, tightened towards the points by a shrink factor.
/// </summary>
/// <remarks>
/// <para>
/// The construction is the one <c>boundary.m</c> spells out. Every simplex of the Delaunay
/// tessellation is kept or dropped according to whether its circumradius is at most alpha, so the
/// distinct circumradii — the alpha spectrum — are the only values of alpha that matter. The
/// smallest of them that still leaves one connected region is the critical alpha; the shrink factor
/// then picks one of the spectrum's entries at or above it, from the largest (the convex hull, at a
/// shrink of nought) down to the critical one (at a shrink of one).
/// </para>
/// <para>
/// A spectrum whose top is within a thousandth of the critical value has nothing to choose between,
/// and <c>boundary.m</c> takes the convex hull rather than pretending otherwise; that guard is kept.
/// Holes are suppressed — the threshold <c>boundary.m</c> sets is the whole shape's area, which no
/// hole can exceed — so the answer is the outer loop alone.
/// </para>
/// </remarks>
public static class AlphaBoundary
{
    /// <summary>
    /// The boundary of the points in the plane: zero-based vertex indices running once around the
    /// shape with the first repeated at the end, and the area they enclose.
    /// </summary>
    public static (int[] Loop, double Area) Plane(double[] x, double[] y, double shrink)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length < 3)
        {
            return ([], 0);
        }

        int[,] triangles = Delaunay.Triangulate(x, y);
        int count = triangles.GetLength(0);
        if (count == 0)
        {
            return ([], 0);
        }

        var radii = new double[count];
        for (int t = 0; t < count; t++)
        {
            radii[t] = Circumradius(
                x[triangles[t, 0]], y[triangles[t, 0]],
                x[triangles[t, 1]], y[triangles[t, 1]],
                x[triangles[t, 2]], y[triangles[t, 2]]);
        }

        double alpha = ChooseAlpha(radii, shrink, triangles, 2, x.Length);
        var keep = new bool[count];
        for (int t = 0; t < count; t++)
        {
            keep[t] = radii[t] <= alpha;
        }

        int[] loop = OuterLoop(triangles, keep, x, y);
        return (loop, PolygonArea(loop, x, y));
    }

    /// <summary>
    /// The boundary of the points in space: triangles of zero-based vertex indices, and the volume
    /// they enclose.
    /// </summary>
    public static (int[,] Facets, double Volume) Space(double[] x, double[] y, double[] z, double shrink)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        int[,] cells = Delaunay3D.Tetrahedra(x, y, z);
        int count = cells.GetLength(0);
        if (count == 0)
        {
            return (new int[0, 3], 0);
        }

        var radii = new double[count];
        for (int t = 0; t < count; t++)
        {
            radii[t] = Circumradius3(cells, t, x, y, z);
        }

        double alpha = ChooseAlpha(radii, shrink, cells, 3, x.Length);
        var keep = new bool[count];
        double volume = 0;
        for (int t = 0; t < count; t++)
        {
            keep[t] = radii[t] <= alpha;
            if (keep[t])
            {
                volume += TetrahedronVolume(cells, t, x, y, z);
            }
        }

        var facets = new List<int[]>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int t = 0; t < count; t++)
        {
            if (!keep[t])
            {
                continue;
            }

            for (int drop = 0; drop < 4; drop++)
            {
                var face = new int[3];
                int at = 0;
                for (int v = 0; v < 4; v++)
                {
                    if (v != drop)
                    {
                        face[at++] = cells[t, v];
                    }
                }

                var sorted = (int[])face.Clone();
                Array.Sort(sorted);
                string key = string.Join(',', sorted);
                seen[key] = seen.TryGetValue(key, out int already) ? already + 1 : 1;
            }
        }

        for (int t = 0; t < count; t++)
        {
            if (!keep[t])
            {
                continue;
            }

            for (int drop = 0; drop < 4; drop++)
            {
                var face = new int[3];
                int at = 0;
                for (int v = 0; v < 4; v++)
                {
                    if (v != drop)
                    {
                        face[at++] = cells[t, v];
                    }
                }

                var sorted = (int[])face.Clone();
                Array.Sort(sorted);
                if (seen[string.Join(',', sorted)] == 1)
                {
                    facets.Add(sorted);
                }
            }
        }

        var answer = new int[facets.Count, 3];
        for (int i = 0; i < facets.Count; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                answer[i, k] = facets[i][k];
            }
        }

        return (answer, volume);
    }

    /// <summary>
    /// The alpha the shrink factor asks for: the spectrum entry that many steps down from the
    /// largest, never below the smallest that leaves one region.
    /// </summary>
    private static double ChooseAlpha(double[] radii, double shrink, int[,] simplices, int dimension, int points)
    {
        var spectrum = new List<double>();
        foreach (double radius in radii)
        {
            spectrum.Add(radius);
        }

        spectrum.Sort();
        spectrum.Reverse();
        var distinct = new List<double>();
        foreach (double value in spectrum)
        {
            if (distinct.Count == 0 || distinct[^1] != value)
            {
                distinct.Add(value);
            }
        }

        // The critical alpha is the smallest of the spectrum that still leaves the shape in one
        // piece. Keeping simplices is monotone in alpha, so the scan runs from the smallest up and
        // stops at the first that answers one — a point in no kept simplex is a piece of its own,
        // which is what makes this a stricter question than "are the kept simplices connected".
        double critical = distinct[0];
        for (int i = distinct.Count - 1; i >= 0; i--)
        {
            if (Regions(simplices, radii, distinct[i], dimension, points) == 1)
            {
                critical = distinct[i];
                break;
            }
        }

        var above = new List<double>();
        foreach (double value in distinct)
        {
            if (value >= critical)
            {
                above.Add(value);
            }
        }

        if (above[0] - critical < 1e-3 * above[0])
        {
            return double.PositiveInfinity;
        }

        int numA = above.Count;
        int step = Math.Max((int)Math.Ceiling((1 - shrink) * numA), 1);
        return above[numA - step];
    }

    /// <summary>
    /// How many pieces the shape falls into at this alpha: kept simplices sharing a corner are one
    /// piece, the way a simplicial complex is connected, and a point that no kept simplex touches is
    /// a piece of its own — which is what makes "one region" mean that the whole point set hangs
    /// together rather than only that the kept simplices do.
    /// </summary>
    private static int Regions(int[,] simplices, double[] radii, double alpha, int dimension, int points)
    {
        int total = simplices.GetLength(0);
        var parent = new int[total];
        for (int t = 0; t < total; t++)
        {
            parent[t] = t;
        }

        int Find(int a)
        {
            while (parent[a] != a)
            {
                parent[a] = parent[parent[a]];
                a = parent[a];
            }

            return a;
        }

        var owner = new Dictionary<string, int>(StringComparer.Ordinal);
        var covered = new bool[points];
        var kept = new List<int>();
        for (int t = 0; t < total; t++)
        {
            if (radii[t] > alpha)
            {
                continue;
            }

            kept.Add(t);
            for (int v = 0; v <= dimension; v++)
            {
                covered[simplices[t, v]] = true;
            }

            for (int v = 0; v <= dimension; v++)
            {
                string key = simplices[t, v].ToString(CultureInfo.InvariantCulture);
                if (owner.TryGetValue(key, out int already))
                {
                    int ra = Find(already);
                    int rb = Find(t);
                    if (ra != rb)
                    {
                        parent[rb] = ra;
                    }
                }
                else
                {
                    owner[key] = t;
                }
            }
        }

        var roots = new HashSet<int>();
        foreach (int t in kept)
        {
            roots.Add(Find(t));
        }

        int loose = 0;
        for (int i = 0; i < points; i++)
        {
            if (!covered[i])
            {
                loose++;
            }
        }

        return roots.Count + loose;
    }

    private static double Circumradius(
        double ax, double ay, double bx, double by, double cx, double cy)
    {
        double a = Distance(bx, by, cx, cy);
        double b = Distance(ax, ay, cx, cy);
        double c = Distance(ax, ay, bx, by);
        double twice = Math.Abs(((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax)));
        return twice == 0 ? double.PositiveInfinity : a * b * c / (2 * twice);
    }

    private static double Distance(double ax, double ay, double bx, double by) =>
        Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));

    private static double Circumradius3(int[,] cells, int t, double[] x, double[] y, double[] z)
    {
        double ax = x[cells[t, 0]], ay = y[cells[t, 0]], az = z[cells[t, 0]];
        var m = new double[3][];
        var rhs = new double[3];
        for (int r = 0; r < 3; r++)
        {
            int v = cells[t, r + 1];
            double dx = x[v] - ax;
            double dy = y[v] - ay;
            double dz = z[v] - az;
            m[r] = [2 * dx, 2 * dy, 2 * dz];
            rhs[r] = (dx * dx) + (dy * dy) + (dz * dz);
        }

        ScatteredSurface.SolveDense(m, rhs, 3);
        return Math.Sqrt((rhs[0] * rhs[0]) + (rhs[1] * rhs[1]) + (rhs[2] * rhs[2]));
    }

    private static double TetrahedronVolume(int[,] cells, int t, double[] x, double[] y, double[] z)
    {
        double ax = x[cells[t, 0]], ay = y[cells[t, 0]], az = z[cells[t, 0]];
        double bx = x[cells[t, 1]] - ax, by = y[cells[t, 1]] - ay, bz = z[cells[t, 1]] - az;
        double cx = x[cells[t, 2]] - ax, cy = y[cells[t, 2]] - ay, cz = z[cells[t, 2]] - az;
        double dx = x[cells[t, 3]] - ax, dy = y[cells[t, 3]] - ay, dz = z[cells[t, 3]] - az;
        double determinant =
            (bx * ((cy * dz) - (cz * dy)))
            - (by * ((cx * dz) - (cz * dx)))
            + (bz * ((cx * dy) - (cy * dx)));
        return Math.Abs(determinant) / 6;
    }

    /// <summary>
    /// The outer edge of the kept triangles, as a closed walk. Each kept triangle contributes its
    /// three edges wound counter-clockwise; an edge whose reverse belongs to another kept triangle
    /// is interior and cancels, and what is left is the boundary, already pointing the way round.
    /// </summary>
    private static int[] OuterLoop(int[,] triangles, bool[] keep, double[] x, double[] y)
    {
        var directed = new HashSet<(int, int)>();
        for (int t = 0; t < triangles.GetLength(0); t++)
        {
            if (!keep[t])
            {
                continue;
            }

            int a = triangles[t, 0], b = triangles[t, 1], c = triangles[t, 2];
            double twice = ((x[b] - x[a]) * (y[c] - y[a])) - ((y[b] - y[a]) * (x[c] - x[a]));
            if (twice < 0)
            {
                (b, c) = (c, b);
            }

            directed.Add((a, b));
            directed.Add((b, c));
            directed.Add((c, a));
        }

        var boundary = new Dictionary<int, List<int>>();
        foreach ((int a, int b) in directed)
        {
            if (!directed.Contains((b, a)))
            {
                if (!boundary.TryGetValue(a, out List<int>? outs))
                {
                    outs = [];
                    boundary[a] = outs;
                }

                outs.Add(b);
            }
        }

        var loops = new List<List<int>>();
        var used = new HashSet<(int, int)>();
        foreach (int start in boundary.Keys)
        {
            foreach (int first in boundary[start])
            {
                if (used.Contains((start, first)))
                {
                    continue;
                }

                var walk = new List<int> { start };
                int from = start;
                int at = first;
                used.Add((start, first));
                while (at != start)
                {
                    walk.Add(at);
                    if (!boundary.TryGetValue(at, out List<int>? outs))
                    {
                        break;
                    }

                    // Where a pinch leaves more than one way on, take the furthest turn to the right.
                    // That is what keeps the walk on the outside of the shape and carries it through
                    // the pinch into the next lobe, rather than closing the lobe it is in — which is
                    // why MATLAB's answer names a pinch vertex twice and encloses both lobes.
                    int next = -1;
                    double best = double.PositiveInfinity;
                    foreach (int candidate in outs)
                    {
                        if (used.Contains((at, candidate)))
                        {
                            continue;
                        }

                        double turn = Turn(x[from], y[from], x[at], y[at], x[candidate], y[candidate]);
                        if (turn < best)
                        {
                            best = turn;
                            next = candidate;
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    used.Add((at, next));
                    from = at;
                    at = next;
                }

                if (at == start && walk.Count >= 3)
                {
                    loops.Add(walk);
                }
            }
        }

        if (loops.Count == 0)
        {
            return [];
        }

        // Holes are suppressed, so what is wanted is the loop that runs round the outside — the one
        // enclosing the largest area.
        List<int> outer = loops[0];
        double widest = 0;
        foreach (List<int> loop in loops)
        {
            double area = Math.Abs(SignedArea(loop, x, y));
            if (area > widest)
            {
                widest = area;
                outer = loop;
            }
        }

        // MATLAB begins the loop at the lowest-numbered point on it, and a walk round a cycle can
        // begin anywhere, so it is rotated to start there and closed by repeating that point.
        int lowest = 0;
        for (int i = 1; i < outer.Count; i++)
        {
            if (outer[i] < outer[lowest])
            {
                lowest = i;
            }
        }

        var closed = new int[outer.Count + 1];
        for (int i = 0; i < outer.Count; i++)
        {
            closed[i] = outer[(lowest + i) % outer.Count];
        }

        closed[^1] = closed[0];
        return closed;
    }

    /// <summary>How far left a step turns, as the cross product's angle against the incoming one.</summary>
    private static double Turn(double px, double py, double qx, double qy, double rx, double ry)
    {
        double inx = qx - px, iny = qy - py;
        double outx = rx - qx, outy = ry - qy;
        double cross = (inx * outy) - (iny * outx);
        double dot = (inx * outx) + (iny * outy);
        return Math.Atan2(cross, dot);
    }

    private static double SignedArea(List<int> loop, double[] x, double[] y)
    {
        double twice = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            int a = loop[i];
            int b = loop[(i + 1) % loop.Count];
            twice += (x[a] * y[b]) - (x[b] * y[a]);
        }

        return twice / 2;
    }

    private static double PolygonArea(int[] loop, double[] x, double[] y)
    {
        double twice = 0;
        for (int i = 0; i + 1 < loop.Length; i++)
        {
            twice += (x[loop[i]] * y[loop[i + 1]]) - (x[loop[i + 1]] * y[loop[i]]);
        }

        return Math.Abs(twice) / 2;
    }
}
