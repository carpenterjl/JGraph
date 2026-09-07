namespace JGraph.Maths.Geometry;

/// <summary>
/// A set of points and a tessellation of them into simplices, with the two searches every scattered
/// verb is built out of: which simplex encloses a query point, and which data point is nearest it.
/// </summary>
/// <remarks>
/// <para>
/// The enclosing simplex is found by a bucketed walk rather than by trying every simplex in turn.
/// The bounding box is cut into a uniform grid of cells, each cell remembering one simplex that
/// overlaps it; a query starts from its own cell's simplex and walks across the face whose
/// barycentric coordinate is most negative, which is the direction the query lies in. That makes
/// <c>griddata</c> over a fine grid cost one short walk per query instead of a scan of the whole
/// triangulation, which is the difference between linear and quadratic on the sizes a grid asks for.
/// </para>
/// <para>
/// The walk can stall on a non-convex or numerically awkward step, so it gives up after a bounded
/// number of moves and falls back to a scan. The fallback is what makes the answer right; the walk
/// is only what makes it quick.
/// </para>
/// </remarks>
public sealed class SimplexMesh
{
    private readonly double[][] _points;
    private readonly int[][] _simplices;
    private readonly int _dimension;
    private readonly double[] _lo;
    private readonly double[] _hi;
    private readonly int[] _cells;
    private readonly int[] _bucket;
    private readonly int[][] _neighbours;

    /// <summary>Builds a mesh over <paramref name="points"/> (m by d) and <paramref name="simplices"/> (t by d+1).</summary>
    public SimplexMesh(double[][] points, int[][] simplices)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(simplices);
        _points = points;
        _simplices = simplices;
        _dimension = points.Length > 0 ? points[0].Length : 0;

        _lo = new double[_dimension];
        _hi = new double[_dimension];
        for (int k = 0; k < _dimension; k++)
        {
            _lo[k] = double.PositiveInfinity;
            _hi[k] = double.NegativeInfinity;
            foreach (double[] point in points)
            {
                _lo[k] = Math.Min(_lo[k], point[k]);
                _hi[k] = Math.Max(_hi[k], point[k]);
            }

            if (!(_hi[k] > _lo[k]))
            {
                _hi[k] = _lo[k] + 1;
            }
        }

        // One bucket per simplex on average, so the grid grows with the data rather than with the
        // number of queries, and a cell's simplex is a start point rather than an answer.
        int perSide = _dimension == 0 || simplices.Length == 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling(Math.Pow(simplices.Length, 1.0 / _dimension)));
        perSide = Math.Min(perSide, 512);
        _cells = new int[_dimension];
        int total = 1;
        for (int k = 0; k < _dimension; k++)
        {
            _cells[k] = perSide;
            total *= perSide;
        }

        _bucket = new int[Math.Max(total, 1)];
        Array.Fill(_bucket, -1);
        for (int t = 0; t < simplices.Length; t++)
        {
            // The simplex is registered in every cell its own bounding box touches, which is enough
            // for the walk to start close by even where the simplices are very uneven in size.
            RegisterInBox(t);
        }

        _neighbours = Adjacency(simplices, _dimension);
    }

    /// <summary>How many coordinates each point has.</summary>
    public int Dimension => _dimension;

    /// <summary>How many simplices the tessellation has.</summary>
    public int Count => _simplices.Length;

    /// <summary>
    /// The simplex enclosing <paramref name="query"/> and the barycentric coordinates of the query
    /// inside it, or −1 when the query lies outside the tessellation.
    /// </summary>
    public int Locate(ReadOnlySpan<double> query, Span<double> weights)
    {
        if (_simplices.Length == 0)
        {
            return -1;
        }

        int at = Start(query);
        int steps = 0;
        int limit = 8 + (4 * (int)Math.Sqrt(_simplices.Length));
        int previous = -1;
        while (at >= 0 && steps++ < limit)
        {
            Barycentric(at, query, weights);
            int worst = -1;
            double least = -Tolerance;
            for (int v = 0; v <= _dimension; v++)
            {
                if (weights[v] < least)
                {
                    least = weights[v];
                    worst = v;
                }
            }

            if (worst < 0)
            {
                return at;
            }

            // Cross the face opposite the most negative coordinate: that is the one the query is on
            // the far side of.
            int next = _neighbours[at][worst];
            if (next < 0 || next == previous)
            {
                break;
            }

            previous = at;
            at = next;
        }

        for (int t = 0; t < _simplices.Length; t++)
        {
            Barycentric(t, query, weights);
            bool inside = true;
            for (int v = 0; v <= _dimension && inside; v++)
            {
                inside = weights[v] > -Tolerance;
            }

            if (inside)
            {
                return t;
            }
        }

        return -1;
    }

    /// <summary>
    /// The simplex across face <paramref name="face"/> of simplex <paramref name="t"/>, or −1 where
    /// that face is on the outside.
    /// </summary>
    public int Neighbour(int t, int face) => _neighbours[t][face];

    /// <summary>The barycentric coordinates of <paramref name="query"/> in simplex <paramref name="t"/>.</summary>
    public void Barycentric(int t, ReadOnlySpan<double> query, Span<double> weights)
    {
        int d = _dimension;
        int[] cell = _simplices[t];
        double[] last = _points[cell[d]];
        var m = new double[d][];
        var rhs = new double[d];
        for (int r = 0; r < d; r++)
        {
            var row = new double[d];
            for (int c = 0; c < d; c++)
            {
                row[c] = _points[cell[c]][r] - last[r];
            }

            m[r] = row;
            rhs[r] = query[r] - last[r];
        }

        Solve(m, rhs, d);
        double sum = 0;
        for (int v = 0; v < d; v++)
        {
            weights[v] = rhs[v];
            sum += rhs[v];
        }

        weights[d] = 1 - sum;
    }

    /// <summary>The index of the data point nearest <paramref name="query"/>, and its distance.</summary>
    public (int Index, double Distance) Nearest(ReadOnlySpan<double> query)
    {
        int best = -1;
        double least = double.PositiveInfinity;
        for (int i = 0; i < _points.Length; i++)
        {
            double squared = 0;
            double[] point = _points[i];
            for (int k = 0; k < _dimension; k++)
            {
                double delta = point[k] - query[k];
                squared += delta * delta;
            }

            if (squared < least)
            {
                least = squared;
                best = i;
            }
        }

        return (best, Math.Sqrt(least));
    }

    /// <summary>
    /// The facets on the outside of the tessellation: those belonging to one simplex rather than
    /// two. For a Delaunay tessellation they are the facets of the convex hull.
    /// </summary>
    public static List<int[]> BoundaryFacets(int[][] simplices, int dimension)
    {
        var counts = new Dictionary<string, (int[] Facet, int Count)>(StringComparer.Ordinal);
        foreach (int[] cell in simplices)
        {
            for (int drop = 0; drop <= dimension; drop++)
            {
                var facet = new int[dimension];
                int at = 0;
                for (int v = 0; v <= dimension; v++)
                {
                    if (v != drop)
                    {
                        facet[at++] = cell[v];
                    }
                }

                Array.Sort(facet);
                string key = string.Join(',', facet);
                counts[key] = counts.TryGetValue(key, out (int[] Facet, int Count) already)
                    ? (already.Facet, already.Count + 1)
                    : (facet, 1);
            }
        }

        var outside = new List<int[]>();
        foreach ((int[] facet, int count) in counts.Values)
        {
            if (count == 1)
            {
                outside.Add(facet);
            }
        }

        outside.Sort(static (l, r) =>
        {
            for (int k = 0; k < l.Length; k++)
            {
                if (l[k] != r[k])
                {
                    return l[k].CompareTo(r[k]);
                }
            }

            return 0;
        });

        return outside;
    }

    /// <summary>Which simplex sits across each face of each simplex, or −1 where there is none.</summary>
    private static int[][] Adjacency(int[][] simplices, int dimension)
    {
        var across = new Dictionary<string, (int Cell, int Face)>(StringComparer.Ordinal);
        var neighbours = new int[simplices.Length][];
        for (int t = 0; t < simplices.Length; t++)
        {
            neighbours[t] = new int[dimension + 1];
            Array.Fill(neighbours[t], -1);
        }

        for (int t = 0; t < simplices.Length; t++)
        {
            for (int drop = 0; drop <= dimension; drop++)
            {
                var facet = new int[dimension];
                int at = 0;
                for (int v = 0; v <= dimension; v++)
                {
                    if (v != drop)
                    {
                        facet[at++] = simplices[t][v];
                    }
                }

                Array.Sort(facet);
                string key = string.Join(',', facet);
                if (across.TryGetValue(key, out (int Cell, int Face) other))
                {
                    neighbours[t][drop] = other.Cell;
                    neighbours[other.Cell][other.Face] = t;
                }
                else
                {
                    across[key] = (t, drop);
                }
            }
        }

        return neighbours;
    }

    private const double Tolerance = 1e-12;

    private void RegisterInBox(int t)
    {
        int d = _dimension;
        var loCell = new int[d];
        var hiCell = new int[d];
        for (int k = 0; k < d; k++)
        {
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (int v in _simplices[t])
            {
                low = Math.Min(low, _points[v][k]);
                high = Math.Max(high, _points[v][k]);
            }

            loCell[k] = CellOf(low, k);
            hiCell[k] = CellOf(high, k);
        }

        var at = (int[])loCell.Clone();
        while (true)
        {
            int index = 0;
            for (int k = d - 1; k >= 0; k--)
            {
                index = (index * _cells[k]) + at[k];
            }

            if (_bucket[index] < 0)
            {
                _bucket[index] = t;
            }

            int carry = 0;
            while (carry < d)
            {
                at[carry]++;
                if (at[carry] <= hiCell[carry])
                {
                    break;
                }

                at[carry] = loCell[carry];
                carry++;
            }

            if (carry == d)
            {
                return;
            }
        }
    }

    private int CellOf(double value, int k)
    {
        double t = (value - _lo[k]) / (_hi[k] - _lo[k]);
        int cell = (int)(t * _cells[k]);
        return Math.Clamp(cell, 0, _cells[k] - 1);
    }

    private int Start(ReadOnlySpan<double> query)
    {
        int index = 0;
        for (int k = _dimension - 1; k >= 0; k--)
        {
            index = (index * _cells[k]) + CellOf(query[k], k);
        }

        int found = _bucket[index];
        return found >= 0 ? found : 0;
    }

    /// <summary>Solves a small dense system in place by Gaussian elimination with partial pivoting.</summary>
    private static void Solve(double[][] m, double[] rhs, int size)
    {
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

            if (pivot != c)
            {
                (m[pivot], m[c]) = (m[c], m[pivot]);
                (rhs[pivot], rhs[c]) = (rhs[c], rhs[pivot]);
            }

            double head = m[c][c];
            if (head == 0)
            {
                continue;
            }

            for (int r = c + 1; r < size; r++)
            {
                double factor = m[r][c] / head;
                if (factor == 0)
                {
                    continue;
                }

                for (int k = c; k < size; k++)
                {
                    m[r][k] -= factor * m[c][k];
                }

                rhs[r] -= factor * rhs[c];
            }
        }

        for (int r = size - 1; r >= 0; r--)
        {
            double sum = rhs[r];
            for (int c = r + 1; c < size; c++)
            {
                sum -= m[r][c] * rhs[c];
            }

            rhs[r] = m[r][r] == 0 ? double.NaN : sum / m[r][r];
        }
    }
}
