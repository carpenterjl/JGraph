namespace JGraph.Maths.Geometry;

/// <summary>
/// The Delaunay triangulation of points in any number of directions, by Bowyer–Watson insertion:
/// start from one simplex large enough to hold everything, and for each point delete the simplices
/// whose circumsphere it falls inside, then re-fill the hole from its boundary facets.
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="Delaunay"/> and <see cref="Delaunay3D"/> written once for any dimension. The
/// in-sphere test is the lifted-paraboloid determinant on a simplex kept positively oriented, and
/// both determinants are taken by plain Gaussian elimination with partial pivoting on a small dense
/// matrix rather than by an expanded formula, because the expansion has a different shape in every
/// dimension and there is nothing to gain from writing each of them out.
/// </para>
/// <para>
/// MATLAB's <c>delaunayn</c> strips the simplices whose volume is zero after the tessellation, which
/// is what its Qhull options <c>Qt Qbb Qc</c> leave behind on co-spherical input; the same strip is
/// applied here, with MATLAB's own tolerance — the spacing of the largest coordinate in the simplex.
/// </para>
/// </remarks>
public static class DelaunayN
{
    /// <summary>
    /// The tessellation of <paramref name="points"/> — m points down the rows, d coordinates across
    /// — as an array of zero-based vertex indices, one simplex of d+1 vertices per row.
    /// </summary>
    public static int[,] Simplices(double[,] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        int n = points.GetLength(0);
        int d = points.GetLength(1);
        if (d < 1)
        {
            throw new ArgumentException("A tessellation needs at least one coordinate per point.", nameof(points));
        }

        if (n < d + 1)
        {
            throw new ArgumentException(
                $"A tessellation in {d} directions needs at least {d + 1} points.", nameof(points));
        }

        // The point set plus d+1 more around it, far enough out that every real simplex is finished
        // before the enclosing one is thrown away.
        var lo = new double[d];
        var hi = new double[d];
        for (int k = 0; k < d; k++)
        {
            lo[k] = double.PositiveInfinity;
            hi[k] = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                lo[k] = Math.Min(lo[k], points[i, k]);
                hi[k] = Math.Max(hi[k], points[i, k]);
            }
        }

        double span = 0;
        for (int k = 0; k < d; k++)
        {
            span = Math.Max(span, hi[k] - lo[k]);
        }

        if (span <= 0)
        {
            throw new ArgumentException("Every point is in the same place, so there is nothing to tessellate.");
        }

        double reach = 1000 * span;
        var p = new double[n + d + 1][];
        for (int i = 0; i < n; i++)
        {
            var row = new double[d];
            for (int k = 0; k < d; k++)
            {
                row[k] = points[i, k];
            }

            p[i] = row;
        }

        // A simplex about the centre: one apex out along each direction and one back along all of
        // them at once, which contains the whole box for any reach large enough.
        for (int v = 0; v <= d; v++)
        {
            var row = new double[d];
            for (int k = 0; k < d; k++)
            {
                double centre = 0.5 * (lo[k] + hi[k]);
                row[k] = v == d ? centre - reach : centre + (v == k ? d * reach : -reach);
            }

            p[n + v] = row;
        }

        var big = new int[d + 1];
        for (int v = 0; v <= d; v++)
        {
            big[v] = n + v;
        }

        var cells = new List<int[]> { Oriented(p, big, d) };
        var kept = new List<int[]>();
        var walls = new Dictionary<string, (int[] Facet, int Count)>(StringComparer.Ordinal);

        for (int point = 0; point < n; point++)
        {
            kept.Clear();
            walls.Clear();
            bool any = false;
            foreach (int[] cell in cells)
            {
                if (InSphere(p, cell, point, d))
                {
                    any = true;
                    for (int drop = 0; drop <= d; drop++)
                    {
                        Wall(walls, Facet(cell, drop));
                    }
                }
                else
                {
                    kept.Add(cell);
                }
            }

            if (!any)
            {
                continue;
            }

            foreach ((int[] facet, int count) in walls.Values)
            {
                if (count == 1)
                {
                    var grown = new int[d + 1];
                    Array.Copy(facet, grown, d);
                    grown[d] = point;
                    kept.Add(Oriented(p, grown, d));
                }
            }

            cells = [.. kept];
        }

        var answer = new List<int[]>(cells.Count);
        foreach (int[] cell in cells)
        {
            bool real = true;
            foreach (int v in cell)
            {
                real &= v < n;
            }

            // MATLAB strips the flat simplices a co-spherical set leaves behind, at the spacing of
            // the largest coordinate the simplex touches.
            if (real && !IsFlat(p, cell, d))
            {
                answer.Add(cell);
            }
        }

        // A stable order, so two runs on the same points answer the same way.
        answer.Sort((l, r) =>
        {
            for (int k = 0; k <= d; k++)
            {
                if (l[k] != r[k])
                {
                    return l[k].CompareTo(r[k]);
                }
            }

            return 0;
        });

        var result = new int[answer.Count, d + 1];
        for (int i = 0; i < answer.Count; i++)
        {
            for (int k = 0; k <= d; k++)
            {
                result[i, k] = answer[i][k];
            }
        }

        return result;
    }

    /// <summary>The simplex's vertices with the one at <paramref name="drop"/> left out, sorted.</summary>
    private static int[] Facet(int[] cell, int drop)
    {
        var facet = new int[cell.Length - 1];
        int at = 0;
        for (int v = 0; v < cell.Length; v++)
        {
            if (v != drop)
            {
                facet[at++] = cell[v];
            }
        }

        Array.Sort(facet);
        return facet;
    }

    private static void Wall(Dictionary<string, (int[], int)> walls, int[] facet)
    {
        string key = string.Join(',', facet);
        walls[key] = walls.TryGetValue(key, out (int[] Facet, int Count) already)
            ? (already.Facet, already.Count + 1)
            : (facet, 1);
    }

    /// <summary>The same vertices, wound so the simplex has positive volume.</summary>
    private static int[] Oriented(double[][] p, int[] cell, int d)
    {
        if (Volume(p, cell, d) < 0)
        {
            (cell[0], cell[1]) = (cell[1], cell[0]);
        }

        return cell;
    }

    /// <summary>The signed volume determinant of a simplex, up to the factorial the volume divides by.</summary>
    private static double Volume(double[][] p, int[] cell, int d)
    {
        var m = new double[d][];
        for (int r = 0; r < d; r++)
        {
            var row = new double[d];
            for (int k = 0; k < d; k++)
            {
                row[k] = p[cell[r]][k] - p[cell[d]][k];
            }

            m[r] = row;
        }

        return Determinant(m, d);
    }

    private static bool IsFlat(double[][] p, int[] cell, int d)
    {
        double largest = 0;
        foreach (int v in cell)
        {
            for (int k = 0; k < d; k++)
            {
                largest = Math.Max(largest, Math.Abs(p[v][k]));
            }
        }

        double tolerance = largest == 0 ? double.Epsilon : Ulp(largest);
        return Math.Abs(Volume(p, cell, d)) <= tolerance;
    }

    /// <summary>The spacing of the floating-point numbers around <paramref name="x"/>, MATLAB's <c>eps(x)</c>.</summary>
    private static double Ulp(double x)
    {
        double magnitude = Math.Abs(x);
        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        return BitConverter.Int64BitsToDouble(bits + 1) - magnitude;
    }

    /// <summary>
    /// Whether <paramref name="point"/> lies inside the circumsphere of a positively oriented
    /// simplex, from the sign of the lifted-paraboloid determinant taken relative to the point.
    /// </summary>
    private static bool InSphere(double[][] p, int[] cell, int point, int d)
    {
        var m = new double[d + 1][];
        for (int r = 0; r <= d; r++)
        {
            var row = new double[d + 1];
            double squared = 0;
            for (int k = 0; k < d; k++)
            {
                double delta = p[cell[r]][k] - p[point][k];
                row[k] = delta;
                squared += delta * delta;
            }

            row[d] = squared;
            m[r] = row;
        }

        // For a simplex already wound to positive volume the lifted determinant is positive exactly
        // when the point is inside the circumsphere, in every dimension — the sign does not alternate
        // with the parity, because the squared-norm column is appended after the coordinates rather
        // than before them.
        return Determinant(m, d + 1) > 0;
    }

    /// <summary>The determinant of a small dense matrix, by Gaussian elimination with partial pivoting.</summary>
    private static double Determinant(double[][] m, int size)
    {
        double product = 1;
        for (int c = 0; c < size; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < size; r++)
            {
                if (Math.Abs(m[r][c]) > Math.Abs(m[pivot][c]))
                {
                    pivot = r;
                }
            }

            if (m[pivot][c] == 0)
            {
                return 0;
            }

            if (pivot != c)
            {
                (m[pivot], m[c]) = (m[c], m[pivot]);
                product = -product;
            }

            product *= m[c][c];
            for (int r = c + 1; r < size; r++)
            {
                double factor = m[r][c] / m[c][c];
                for (int k = c; k < size; k++)
                {
                    m[r][k] -= factor * m[c][k];
                }
            }
        }

        return product;
    }
}
