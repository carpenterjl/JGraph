namespace JGraph.Maths.Geometry;

/// <summary>
/// The Delaunay tetrahedralization of a set of points in space, by Bowyer–Watson insertion: start
/// from one tetrahedron large enough to hold everything, and for each point delete the tetrahedra
/// whose circumsphere it falls inside, then re-fill the hole from its boundary triangles.
/// </summary>
/// <remarks>
/// <para>
/// This is the space version of <see cref="Delaunay"/>, and it is the same algorithm with one
/// dimension more: the circumcircle test becomes a circumsphere test, the hole's boundary is made
/// of triangles rather than edges, and the enclosing simplex is a tetrahedron rather than a
/// triangle. The in-sphere test is the standard 5-point determinant on tetrahedra kept
/// positively oriented, for the same reason the planar one is a determinant — computing a centre
/// divides by the volume, and a nearly flat tetrahedron is exactly what a real point set produces.
/// </para>
/// <para>
/// Points on one plane have no tetrahedralization with a volume; that case is refused, and in the
/// plane <c>delaunay(x, y)</c> is the question that has an answer.
/// </para>
/// </remarks>
public static class Delaunay3D
{
    private readonly record struct Cell(int A, int B, int C, int D);

    private readonly record struct Facet(int A, int B, int C)
    {
        /// <summary>The face's vertices in ascending order, so the two cells sharing it agree on a key.</summary>
        public (int, int, int) Key
        {
            get
            {
                int a = A;
                int b = B;
                int c = C;
                if (a > b)
                {
                    (a, b) = (b, a);
                }

                if (b > c)
                {
                    (b, c) = (c, b);
                }

                if (a > b)
                {
                    (a, b) = (b, a);
                }

                return (a, b, c);
            }
        }
    }

    /// <summary>
    /// The tetrahedralization of the given points as an m-by-4 array of zero-based vertex indices.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The arrays differ in length, there are fewer than four points, or they lie on one plane.
    /// </exception>
    public static int[,] Tetrahedra(double[] x, double[] y, double[] z)
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
            throw new ArgumentException("A tetrahedralization needs at least four points.", nameof(x));
        }

        // The points, with four more around them big enough that every real tetrahedron is finished
        // before the enclosing one is thrown away.
        double minX = x[0];
        double maxX = x[0];
        double minY = y[0];
        double maxY = y[0];
        double minZ = z[0];
        double maxZ = z[0];
        for (int i = 1; i < n; i++)
        {
            minX = System.Math.Min(minX, x[i]);
            maxX = System.Math.Max(maxX, x[i]);
            minY = System.Math.Min(minY, y[i]);
            maxY = System.Math.Max(maxY, y[i]);
            minZ = System.Math.Min(minZ, z[i]);
            maxZ = System.Math.Max(maxZ, z[i]);
        }

        double span = System.Math.Max(maxX - minX, System.Math.Max(maxY - minY, maxZ - minZ));
        if (span <= 0)
        {
            throw new ArgumentException("Every point is in the same place, so there is nothing to triangulate.");
        }

        double reach = 1000 * span;
        double cx = (minX + maxX) / 2;
        double cy = (minY + maxY) / 2;
        double cz = (minZ + maxZ) / 2;

        var px = new double[n + 4];
        var py = new double[n + 4];
        var pz = new double[n + 4];
        Array.Copy(x, px, n);
        Array.Copy(y, py, n);
        Array.Copy(z, pz, n);

        // A big tetrahedron about the centre: one apex up each of three directions and one down all
        // three, which contains the whole box for any reach large enough.
        px[n] = cx + reach; py[n] = cy; pz[n] = cz - reach;
        px[n + 1] = cx - reach; py[n + 1] = cy + reach; pz[n + 1] = cz - reach;
        px[n + 2] = cx - reach; py[n + 2] = cy - reach; pz[n + 2] = cz - reach;
        px[n + 3] = cx; py[n + 3] = cy; pz[n + 3] = cz + reach;

        var cells = new List<Cell> { Oriented(new Cell(n, n + 1, n + 2, n + 3), px, py, pz) };

        for (int point = 0; point < n; point++)
        {
            var doomed = new List<Cell>();
            var kept = new List<Cell>(cells.Count);
            foreach (Cell cell in cells)
            {
                if (InSphere(cell, point, px, py, pz))
                {
                    doomed.Add(cell);
                }
                else
                {
                    kept.Add(cell);
                }
            }

            if (doomed.Count == 0)
            {
                continue; // already accounted for by an existing cell
            }

            // The hole's boundary: a triangle shared by two doomed cells is inside it; one belonging
            // to a single doomed cell is its wall, and the new point cones onto each wall.
            var walls = new Dictionary<(int, int, int), (Facet Facet, int Count)>();
            foreach (Cell cell in doomed)
            {
                Wall(walls, new Facet(cell.B, cell.C, cell.D));
                Wall(walls, new Facet(cell.A, cell.D, cell.C));
                Wall(walls, new Facet(cell.A, cell.B, cell.D));
                Wall(walls, new Facet(cell.A, cell.C, cell.B));
            }

            foreach ((Facet facet, int count) in walls.Values)
            {
                if (count == 1)
                {
                    kept.Add(Oriented(new Cell(facet.A, facet.B, facet.C, point), px, py, pz));
                }
            }

            cells = kept;
        }

        // Everything still touching the enclosing tetrahedron belongs to it, not to the points.
        var answer = new List<Cell>(cells.Count);
        foreach (Cell cell in cells)
        {
            if (cell.A < n && cell.B < n && cell.C < n && cell.D < n)
            {
                answer.Add(cell);
            }
        }

        if (answer.Count == 0)
        {
            throw new ArgumentException(
                "The points lie on one plane, so they enclose no volume. In the plane, delaunay(x, y) " +
                "is the question with an answer.");
        }

        var result = new int[answer.Count, 4];
        for (int i = 0; i < answer.Count; i++)
        {
            result[i, 0] = answer[i].A;
            result[i, 1] = answer[i].B;
            result[i, 2] = answer[i].C;
            result[i, 3] = answer[i].D;
        }

        return result;
    }

    private static void Wall(Dictionary<(int, int, int), (Facet, int)> walls, Facet facet)
    {
        (int, int, int) key = facet.Key;
        walls[key] = walls.TryGetValue(key, out (Facet Facet, int Count) already)
            ? (already.Facet, already.Count + 1)
            : (facet, 1);
    }

    /// <summary>The same four vertices, wound so the tetrahedron has positive volume.</summary>
    private static Cell Oriented(Cell cell, double[] x, double[] y, double[] z) =>
        Volume6(cell, x, y, z) < 0 ? new Cell(cell.A, cell.C, cell.B, cell.D) : cell;

    /// <summary>Six times the signed volume — the orientation determinant.</summary>
    private static double Volume6(Cell cell, double[] x, double[] y, double[] z)
    {
        double ax = x[cell.A] - x[cell.D];
        double ay = y[cell.A] - y[cell.D];
        double az = z[cell.A] - z[cell.D];
        double bx = x[cell.B] - x[cell.D];
        double by = y[cell.B] - y[cell.D];
        double bz = z[cell.B] - z[cell.D];
        double cx = x[cell.C] - x[cell.D];
        double cy = y[cell.C] - y[cell.D];
        double cz = z[cell.C] - z[cell.D];

        return (ax * ((by * cz) - (bz * cy)))
            - (ay * ((bx * cz) - (bz * cx)))
            + (az * ((bx * cy) - (by * cx)));
    }

    /// <summary>
    /// Whether <paramref name="point"/> lies inside the cell's circumsphere, by the 5-point
    /// determinant on a positively oriented cell. Nothing is divided by, so a flat cell gives a
    /// small answer rather than a meaningless one.
    /// </summary>
    private static bool InSphere(Cell cell, int point, double[] x, double[] y, double[] z)
    {
        double px = x[point];
        double py = y[point];
        double pz = z[point];

        double ax = x[cell.A] - px;
        double ay = y[cell.A] - py;
        double az = z[cell.A] - pz;
        double bx = x[cell.B] - px;
        double by = y[cell.B] - py;
        double bz = z[cell.B] - pz;
        double cx = x[cell.C] - px;
        double cy = y[cell.C] - py;
        double cz = z[cell.C] - pz;
        double dx = x[cell.D] - px;
        double dy = y[cell.D] - py;
        double dz = z[cell.D] - pz;

        double a2 = (ax * ax) + (ay * ay) + (az * az);
        double b2 = (bx * bx) + (by * by) + (bz * bz);
        double c2 = (cx * cx) + (cy * cy) + (cz * cz);
        double d2 = (dx * dx) + (dy * dy) + (dz * dz);

        // Expanded along the column of squared norms, whose cofactor signs alternate starting from
        // minus for the first row. Getting that opening sign wrong inverts the whole test — every
        // point then reads as outside every sphere, nothing is ever inserted, and the answer comes
        // back empty rather than wrong, which is how this was caught.
        double determinant =
            -(a2 * Minor(bx, by, bz, cx, cy, cz, dx, dy, dz))
            + (b2 * Minor(ax, ay, az, cx, cy, cz, dx, dy, dz))
            - (c2 * Minor(ax, ay, az, bx, by, bz, dx, dy, dz))
            + (d2 * Minor(ax, ay, az, bx, by, bz, cx, cy, cz));

        return determinant > 0;
    }

    private static double Minor(
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz) =>
        (ax * ((by * cz) - (bz * cy)))
        - (ay * ((bx * cz) - (bz * cx)))
        + (az * ((bx * cy) - (by * cx)));
}
