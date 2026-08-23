namespace JGraph.Maths.Geometry;

/// <summary>
/// The convex hull of a set of points in space, by the incremental quickhull construction: start
/// from a tetrahedron known to be inside the hull, and repeatedly take the point furthest outside
/// some face, remove every face that point can see, and close the hole from its horizon.
/// </summary>
/// <remarks>
/// <para>
/// Faces are kept outward-facing, which makes "can this point see this face" a single signed
/// volume, and makes the enclosed volume a sum of signed tetrahedra over the faces with no
/// separate orientation pass. The tolerance is scaled by the extent of the point set, because an
/// absolute one is meaningless for coordinates in millimetres and in light years alike.
/// </para>
/// <para>
/// Degenerate input — every point on one plane, one line, or one spot — has no hull with an inside,
/// and is refused rather than answered with a sliver. In the plane that case is <c>convhull(x, y)</c>
/// and is a different question with a different answer.
/// </para>
/// </remarks>
public static class ConvexHull3D
{
    /// <summary>A face of the hull: three vertex indices, wound so the normal points outward.</summary>
    private readonly record struct Face(int A, int B, int C);

    /// <summary>
    /// The hull of the given points as an m-by-3 array of zero-based vertex indices, one triangle
    /// per row, each wound counter-clockwise seen from outside.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The arrays differ in length, there are fewer than four points, or the points are coplanar.
    /// </exception>
    public static int[,] Faces(double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (x.Length != y.Length || x.Length != z.Length)
        {
            throw new ArgumentException("The coordinate arrays must be the same length.", nameof(y));
        }

        int n = x.Length;
        if (n < 4)
        {
            throw new ArgumentException("A convex hull in space needs at least four points.", nameof(x));
        }

        double tolerance = Tolerance(x, y, z);
        List<Face> faces = StartingTetrahedron(x, y, z, tolerance);

        // Every point that is still outside something, tried furthest-first. A point inside every
        // face is inside the hull and can never become a vertex, so it never has to be looked at
        // again once the faces that could see it are gone.
        var pending = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            pending.Add(i);
        }

        bool grew = true;
        while (grew)
        {
            grew = false;
            int chosen = -1;
            double furthest = tolerance;

            for (int p = 0; p < pending.Count; p++)
            {
                int point = pending[p];
                foreach (Face face in faces)
                {
                    double above = Above(face, point, x, y, z);
                    if (above > furthest)
                    {
                        furthest = above;
                        chosen = point;
                    }
                }
            }

            if (chosen < 0)
            {
                break;
            }

            faces = Absorb(faces, chosen, x, y, z, tolerance);
            pending.Remove(chosen);
            grew = true;
        }

        var answer = new int[faces.Count, 3];
        for (int f = 0; f < faces.Count; f++)
        {
            answer[f, 0] = faces[f].A;
            answer[f, 1] = faces[f].B;
            answer[f, 2] = faces[f].C;
        }

        return answer;
    }

    /// <summary>
    /// The volume the hull encloses, as the sum of the signed tetrahedra its outward faces make with
    /// the origin — which is exact for any closed surface, wherever the origin happens to be.
    /// </summary>
    public static double Volume(int[,] faces, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(faces);
        double total = 0;
        for (int f = 0; f < faces.GetLength(0); f++)
        {
            int a = faces[f, 0];
            int b = faces[f, 1];
            int c = faces[f, 2];
            total +=
                (x[a] * ((y[b] * z[c]) - (z[b] * y[c])))
                - (y[a] * ((x[b] * z[c]) - (z[b] * x[c])))
                + (z[a] * ((x[b] * y[c]) - (y[b] * x[c])));
        }

        return System.Math.Abs(total) / 6;
    }

    /// <summary>Replaces every face the point can see with a cone of new ones from the horizon.</summary>
    private static List<Face> Absorb(List<Face> faces, int point,
        double[] x, double[] y, double[] z, double tolerance)
    {
        var kept = new List<Face>(faces.Count);
        var seen = new List<Face>();
        foreach (Face face in faces)
        {
            if (Above(face, point, x, y, z) > tolerance)
            {
                seen.Add(face);
            }
            else
            {
                kept.Add(face);
            }
        }

        // The horizon is the boundary of what was seen: an edge shared by two removed faces is
        // interior to the hole, and an edge belonging to just one of them is its rim.
        var edges = new Dictionary<(int, int), int>();
        foreach (Face face in seen)
        {
            Count(edges, face.A, face.B);
            Count(edges, face.B, face.C);
            Count(edges, face.C, face.A);
        }

        foreach (Face face in seen)
        {
            AddIfHorizon(kept, edges, face.A, face.B, point);
            AddIfHorizon(kept, edges, face.B, face.C, point);
            AddIfHorizon(kept, edges, face.C, face.A, point);
        }

        return kept;
    }

    private static void Count(Dictionary<(int, int), int> edges, int from, int to)
    {
        (int, int) key = from < to ? (from, to) : (to, from);
        edges[key] = edges.TryGetValue(key, out int already) ? already + 1 : 1;
    }

    /// <summary>
    /// Adds the triangle closing one horizon edge onto the new point. The edge keeps the winding it
    /// had on the face that is going away, so the triangle built on it faces outward for free.
    /// </summary>
    private static void AddIfHorizon(List<Face> kept, Dictionary<(int, int), int> edges,
        int from, int to, int point)
    {
        (int, int) key = from < to ? (from, to) : (to, from);
        if (edges[key] == 1)
        {
            kept.Add(new Face(from, to, point));
        }
    }

    /// <summary>How far outside <paramref name="face"/> a point lies; negative is inside.</summary>
    private static double Above(Face face, int point, double[] x, double[] y, double[] z) =>
        AboveAt(face, x[point], y[point], z[point], x, y, z);

    /// <summary>The same, for a place rather than one of the points — the centroid, above all.</summary>
    private static double AboveAt(Face face, double px, double py, double pz,
        double[] x, double[] y, double[] z)
    {
        double ax = x[face.B] - x[face.A];
        double ay = y[face.B] - y[face.A];
        double az = z[face.B] - z[face.A];
        double bx = x[face.C] - x[face.A];
        double by = y[face.C] - y[face.A];
        double bz = z[face.C] - z[face.A];

        double nx = (ay * bz) - (az * by);
        double ny = (az * bx) - (ax * bz);
        double nz = (ax * by) - (ay * bx);

        double length = System.Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length == 0)
        {
            return double.NegativeInfinity; // a degenerate face sees nothing
        }

        return (((px - x[face.A]) * nx)
            + ((py - y[face.A]) * ny)
            + ((pz - z[face.A]) * nz)) / length;
    }

    /// <summary>
    /// Four points that genuinely enclose a volume: the two ends of the widest spread, the point
    /// furthest from the line through them, and the point furthest from the plane of those three.
    /// </summary>
    private static List<Face> StartingTetrahedron(double[] x, double[] y, double[] z, double tolerance)
    {
        int n = x.Length;
        int first = 0;
        int second = 0;
        double widest = -1;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double distance = Squared(x[i] - x[j], y[i] - y[j], z[i] - z[j]);
                if (distance > widest)
                {
                    widest = distance;
                    first = i;
                    second = j;
                }
            }
        }

        if (widest <= tolerance * tolerance)
        {
            throw new ArgumentException("Every point is in the same place, so there is no hull to build.");
        }

        int third = -1;
        double furthest = 0;
        for (int i = 0; i < n; i++)
        {
            double ax = x[second] - x[first];
            double ay = y[second] - y[first];
            double az = z[second] - z[first];
            double bx = x[i] - x[first];
            double by = y[i] - y[first];
            double bz = z[i] - z[first];
            double area = Squared((ay * bz) - (az * by), (az * bx) - (ax * bz), (ax * by) - (ay * bx));
            if (area > furthest)
            {
                furthest = area;
                third = i;
            }
        }

        if (third < 0 || furthest <= tolerance * tolerance)
        {
            throw new ArgumentException("The points lie on one line, so they enclose no volume.");
        }

        var basis = new Face(first, second, third);
        int fourth = -1;
        double deepest = tolerance;
        for (int i = 0; i < n; i++)
        {
            double above = System.Math.Abs(Above(basis, i, x, y, z));
            if (above > deepest)
            {
                deepest = above;
                fourth = i;
            }
        }

        if (fourth < 0)
        {
            throw new ArgumentException(
                "The points lie on one plane, so they enclose no volume. In the plane, convhull(x, y) " +
                "is the question with an answer.");
        }

        var faces = new List<Face>(4)
        {
            basis,
            new(basis.A, basis.B, fourth),
            new(basis.B, basis.C, fourth),
            new(basis.C, basis.A, fourth),
        };

        // Every face must look away from the inside, and the tetrahedron's centroid is the one place
        // certainly inside it. Asking each face about that single point settles all four windings
        // without any reasoning about which edge came from where.
        double cx = (x[first] + x[second] + x[third] + x[fourth]) / 4;
        double cy = (y[first] + y[second] + y[third] + y[fourth]) / 4;
        double cz = (z[first] + z[second] + z[third] + z[fourth]) / 4;
        for (int f = 0; f < faces.Count; f++)
        {
            if (AboveAt(faces[f], cx, cy, cz, x, y, z) > 0)
            {
                faces[f] = new Face(faces[f].A, faces[f].C, faces[f].B);
            }
        }

        return faces;
    }

    private static double Squared(double a, double b, double c) => (a * a) + (b * b) + (c * c);

    /// <summary>A tolerance in proportion to how far apart the points are.</summary>
    private static double Tolerance(double[] x, double[] y, double[] z)
    {
        double extent = 0;
        for (int i = 0; i < x.Length; i++)
        {
            extent = System.Math.Max(extent, System.Math.Abs(x[i]));
            extent = System.Math.Max(extent, System.Math.Abs(y[i]));
            extent = System.Math.Max(extent, System.Math.Abs(z[i]));
        }

        return 1e-12 * (1 + extent);
    }
}
