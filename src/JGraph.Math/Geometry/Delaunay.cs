namespace JGraph.Maths.Geometry;

/// <summary>
/// The Delaunay triangulation of a set of points in the plane, by Bowyer–Watson incremental
/// insertion: start from one triangle large enough to hold everything, and for each point delete
/// the triangles whose circumcircle it falls inside and re-fill the hole from its boundary.
/// </summary>
/// <remarks>
/// The circumcircle test is written as the standard 4-point determinant on triangles kept
/// counter-clockwise, rather than by computing a centre and a radius. Computing the centre divides
/// by the triangle's area, which is what makes a nearly collinear triple — exactly the case a real
/// point set produces at its convex hull — return a centre at infinity and a meaningless answer.
/// </remarks>
public static class Delaunay
{
    /// <summary>
    /// The triangulation of <paramref name="x"/>/<paramref name="y"/> as an m-by-3 array of
    /// zero-based vertex indices, each triangle counter-clockwise.
    /// </summary>
    public static int[,] Triangulate(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("The coordinate arrays must be the same length.", nameof(y));
        }

        int n = x.Length;
        if (n < 3)
        {
            throw new ArgumentException("A triangulation needs at least three points.", nameof(x));
        }

        double minX = x[0], maxX = x[0], minY = y[0], maxY = y[0];
        for (int i = 1; i < n; i++)
        {
            minX = Math.Min(minX, x[i]);
            maxX = Math.Max(maxX, x[i]);
            minY = Math.Min(minY, y[i]);
            maxY = Math.Max(maxY, y[i]);
        }

        double span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-12);
        double centreX = 0.5 * (minX + maxX);
        double centreY = 0.5 * (minY + maxY);

        // Three extra vertices forming a triangle that swallows the whole point set. Everything
        // touching them is thrown away at the end.
        var px = new double[n + 3];
        var py = new double[n + 3];
        Array.Copy(x, px, n);
        Array.Copy(y, py, n);
        px[n] = centreX - (20 * span);
        py[n] = centreY - span;
        px[n + 1] = centreX;
        py[n + 1] = centreY + (20 * span);
        px[n + 2] = centreX + (20 * span);
        py[n + 2] = centreY - span;

        var triangles = new List<(int A, int B, int C)> { Oriented(px, py, n, n + 1, n + 2) };
        var doomed = new List<int>();
        var boundary = new List<(int A, int B)>();

        for (int p = 0; p < n; p++)
        {
            doomed.Clear();
            for (int t = 0; t < triangles.Count; t++)
            {
                if (InCircumcircle(px, py, triangles[t], p))
                {
                    doomed.Add(t);
                }
            }

            // The hole's boundary is the edges belonging to exactly one doomed triangle; the rest
            // are interior walls between two of them and vanish with the pair.
            boundary.Clear();
            foreach (int t in doomed)
            {
                (int a, int b, int c) = triangles[t];
                AddOrCancel(boundary, a, b);
                AddOrCancel(boundary, b, c);
                AddOrCancel(boundary, c, a);
            }

            for (int i = doomed.Count - 1; i >= 0; i--)
            {
                triangles.RemoveAt(doomed[i]);
            }

            foreach ((int a, int b) in boundary)
            {
                triangles.Add(Oriented(px, py, a, b, p));
            }
        }

        var kept = new List<(int A, int B, int C)>();
        foreach ((int a, int b, int c) in triangles)
        {
            if (a < n && b < n && c < n && Orientation(px, py, a, b, c) != 0)
            {
                kept.Add((a, b, c));
            }
        }

        // A stable order so two runs on the same points give the same answer, which matters for
        // anything comparing triangulations or writing one to a file.
        kept.Sort(static (l, r) => l.A != r.A ? l.A.CompareTo(r.A) : l.B != r.B ? l.B.CompareTo(r.B) : l.C.CompareTo(r.C));

        var result = new int[kept.Count, 3];
        for (int i = 0; i < kept.Count; i++)
        {
            result[i, 0] = kept[i].A;
            result[i, 1] = kept[i].B;
            result[i, 2] = kept[i].C;
        }

        return result;
    }

    /// <summary>Records an edge, or removes it if it is already there — it was an interior wall.</summary>
    private static void AddOrCancel(List<(int A, int B)> edges, int a, int b)
    {
        (int low, int high) = a < b ? (a, b) : (b, a);
        for (int i = 0; i < edges.Count; i++)
        {
            (int ea, int eb) = edges[i];
            (int elow, int ehigh) = ea < eb ? (ea, eb) : (eb, ea);
            if (elow == low && ehigh == high)
            {
                edges.RemoveAt(i);
                return;
            }
        }

        edges.Add((a, b));
    }

    /// <summary>The triangle with its vertices in counter-clockwise order.</summary>
    private static (int A, int B, int C) Oriented(double[] x, double[] y, int a, int b, int c) =>
        Orientation(x, y, a, b, c) < 0 ? (a, c, b) : (a, b, c);

    /// <summary>Twice the signed area: positive counter-clockwise, zero when the three are collinear.</summary>
    private static double Orientation(double[] x, double[] y, int a, int b, int c) =>
        ((x[b] - x[a]) * (y[c] - y[a])) - ((y[b] - y[a]) * (x[c] - x[a]));

    /// <summary>
    /// Whether <paramref name="p"/> lies strictly inside the circumcircle of a counter-clockwise
    /// triangle, from the sign of the lifted-paraboloid determinant.
    /// </summary>
    private static bool InCircumcircle(double[] x, double[] y, (int A, int B, int C) triangle, int p)
    {
        double ax = x[triangle.A] - x[p];
        double ay = y[triangle.A] - y[p];
        double bx = x[triangle.B] - x[p];
        double by = y[triangle.B] - y[p];
        double cx = x[triangle.C] - x[p];
        double cy = y[triangle.C] - y[p];

        double a2 = (ax * ax) + (ay * ay);
        double b2 = (bx * bx) + (by * by);
        double c2 = (cx * cx) + (cy * cy);

        double determinant =
            (a2 * ((bx * cy) - (by * cx)))
            - (b2 * ((ax * cy) - (ay * cx)))
            + (c2 * ((ax * by) - (ay * bx)));

        return determinant > 0;
    }
}
