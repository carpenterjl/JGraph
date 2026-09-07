namespace JGraph.Maths.Geometry;

/// <summary>How a scattered surface is read between the data points.</summary>
public enum ScatteredMethod
{
    /// <summary>The value at the nearest data point.</summary>
    Nearest,

    /// <summary>Linear over each simplex of the Delaunay tessellation.</summary>
    Linear,

    /// <summary>Sibson's natural neighbour weights, in the plane.</summary>
    Natural,

    /// <summary>A C1 cubic over each triangle of the planar Delaunay triangulation.</summary>
    Cubic,

    /// <summary>The biharmonic spline of MATLAB 4, a dense solve over every data point.</summary>
    Biharmonic,
}

/// <summary>What a scattered surface answers outside the convex hull of its data points.</summary>
public enum ScatteredExtrapolation
{
    /// <summary>NaN.</summary>
    None,

    /// <summary>The value at the nearest data point.</summary>
    Nearest,

    /// <summary>The affine continuation of the boundary simplex nearest the query.</summary>
    Linear,
}

/// <summary>
/// A surface through scattered data: the kernel behind <c>griddata</c>, <c>griddatan</c> and
/// <c>scatteredInterpolant</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tessellation is built once and every query walks it, which is what lets one interpolant be
/// read at a million grid points without rebuilding anything — and what lets <c>F.Values = …</c>
/// re-use the triangulation the way MATLAB's own object does.
/// </para>
/// <para>
/// <c>'nearest'</c> and <c>'linear'</c> work in any number of directions; <c>'natural'</c>,
/// <c>'cubic'</c> and <c>'v4'</c> are planar, as they are in MATLAB.
/// </para>
/// </remarks>
public sealed class ScatteredSurface
{
    private readonly double[][] _points;
    private readonly int[][] _simplices;
    private readonly SimplexMesh _mesh;
    private readonly int _dimension;
    private double[] _values;
    private double[]? _biharmonicWeights;
    private double[][]? _gradients;
    private readonly Dictionary<int, double[][]> _patches = [];
    private List<int[]>? _hullFacets;
    private int[]? _hullSimplices;

    /// <summary>Builds a surface over the given points, tessellating them once.</summary>
    public ScatteredSurface(double[][] points, double[] values)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(values);
        if (points.Length != values.Length)
        {
            throw new ArgumentException("There must be one value for every point.", nameof(values));
        }

        _points = points;
        _values = values;
        _dimension = points.Length > 0 ? points[0].Length : 0;
        _simplices = Tessellate(points, _dimension);
        _mesh = new SimplexMesh(points, _simplices);
    }

    /// <summary>The tessellation, as rows of zero-based vertex indices.</summary>
    public int[][] Simplices => _simplices;

    /// <summary>The values at the data points; replacing them re-uses the tessellation.</summary>
    public double[] Values
    {
        get => _values;
        set
        {
            if (value.Length != _points.Length)
            {
                throw new ArgumentException("There must be one value for every point.", nameof(value));
            }

            _values = value;
            _biharmonicWeights = null;
            _gradients = null;
            _patches.Clear();
        }
    }

    /// <summary>Reads the surface at one point.</summary>
    public double Sample(ReadOnlySpan<double> query, ScatteredMethod method, ScatteredExtrapolation outside)
    {
        if (_points.Length == 0)
        {
            return double.NaN;
        }

        foreach (double coordinate in query)
        {
            if (double.IsNaN(coordinate))
            {
                return double.NaN;
            }
        }

        if (method == ScatteredMethod.Biharmonic)
        {
            return Biharmonic(query);
        }

        if (method == ScatteredMethod.Nearest)
        {
            // MATLAB's nearest interpolant answers everywhere, so the hull only matters when the
            // caller asked for 'none' explicitly.
            if (outside == ScatteredExtrapolation.None && !Inside(query))
            {
                return double.NaN;
            }

            return _values[_mesh.Nearest(query).Index];
        }

        Span<double> weights = stackalloc double[_dimension + 1];
        int simplex = _mesh.Locate(query, weights);
        if (simplex < 0)
        {
            return Outside(query, outside);
        }

        return method switch
        {
            ScatteredMethod.Natural => Natural(query, simplex, weights),
            ScatteredMethod.Cubic => Cubic(query, simplex),
            _ => Affine(simplex, weights),
        };
    }

    /// <summary>The barycentric coordinates of a query in its enclosing simplex, for <c>tsearchn</c>.</summary>
    public int Locate(ReadOnlySpan<double> query, Span<double> weights) => _mesh.Locate(query, weights);

    /// <summary>The nearest data point to a query, for <c>dsearchn</c>.</summary>
    public (int Index, double Distance) Nearest(ReadOnlySpan<double> query) => _mesh.Nearest(query);

    /// <summary>Whether the query lies inside the tessellation.</summary>
    public bool Inside(ReadOnlySpan<double> query)
    {
        Span<double> weights = stackalloc double[_dimension + 1];
        return _mesh.Locate(query, weights) >= 0;
    }

    private static int[][] Tessellate(double[][] points, int dimension)
    {
        if (points.Length < dimension + 1)
        {
            return [];
        }

        var packed = new double[points.Length, dimension];
        for (int i = 0; i < points.Length; i++)
        {
            for (int k = 0; k < dimension; k++)
            {
                packed[i, k] = points[i][k];
            }
        }

        int[,] cells = DelaunayN.Simplices(packed);
        var simplices = new int[cells.GetLength(0)][];
        for (int t = 0; t < simplices.Length; t++)
        {
            var cell = new int[dimension + 1];
            for (int v = 0; v <= dimension; v++)
            {
                cell[v] = cells[t, v];
            }

            simplices[t] = cell;
        }

        return simplices;
    }

    private double Affine(int simplex, ReadOnlySpan<double> weights)
    {
        double sum = 0;
        for (int v = 0; v <= _dimension; v++)
        {
            sum += weights[v] * _values[_simplices[simplex][v]];
        }

        return sum;
    }

    private double Outside(ReadOnlySpan<double> query, ScatteredExtrapolation outside)
    {
        switch (outside)
        {
            case ScatteredExtrapolation.Nearest:
                return _values[_mesh.Nearest(query).Index];

            case ScatteredExtrapolation.Linear:
                return LinearOutside(query);

            default:
                return double.NaN;
        }
    }

    /// <summary>
    /// The surface continued beyond the hull: the affine function of the simplex behind the hull
    /// facet nearest the query, read at the query itself. That is what makes the continuation join
    /// the surface without a step and carry its slope on.
    /// </summary>
    private double LinearOutside(ReadOnlySpan<double> query)
    {
        if (_hullFacets is null)
        {
            _hullFacets = SimplexMesh.BoundaryFacets(_simplices, _dimension);

            // Which simplex sits behind each hull facet is a property of the tessellation, so it is
            // worked out once here rather than searched for on every query outside the hull.
            _hullSimplices = new int[_hullFacets.Count];
            for (int i = 0; i < _hullFacets.Count; i++)
            {
                _hullSimplices[i] = SimplexBehind(_hullFacets[i]);
            }
        }

        if (_hullFacets.Count == 0 || _simplices.Length == 0)
        {
            return double.NaN;
        }

        int bestSimplex = -1;
        double least = double.PositiveInfinity;
        for (int i = 0; i < _hullFacets.Count; i++)
        {
            double squared = DistanceToFacet(_hullFacets[i], query);
            if (squared < least)
            {
                least = squared;
                bestSimplex = _hullSimplices![i];
            }
        }

        if (bestSimplex < 0)
        {
            return double.NaN;
        }

        Span<double> weights = stackalloc double[_dimension + 1];
        _mesh.Barycentric(bestSimplex, query, weights);
        return Affine(bestSimplex, weights);
    }

    /// <summary>
    /// How far a query is from one facet of the hull, measured to the facet's middle rather than to
    /// its nearest point. Measuring to the nearest point picks whichever facet the query happens to
    /// face most squarely, and a query far outside then rides one facet's own slope out to a value
    /// that has nothing to do with the data; the middle keeps the choice steadier, and it is the
    /// choice that lands nearer MATLAB's own continuation.
    /// </summary>
    private double DistanceToFacet(int[] facet, ReadOnlySpan<double> query)
    {
        double squared = 0;
        for (int k = 0; k < _dimension; k++)
        {
            double centre = 0;
            foreach (int v in facet)
            {
                centre += _points[v][k];
            }

            centre /= facet.Length;
            double delta = centre - query[k];
            squared += delta * delta;
        }

        return squared;
    }

    private int SimplexBehind(int[] facet)
    {
        for (int t = 0; t < _simplices.Length; t++)
        {
            int found = 0;
            foreach (int v in facet)
            {
                foreach (int w in _simplices[t])
                {
                    if (v == w)
                    {
                        found++;
                        break;
                    }
                }
            }

            if (found == facet.Length)
            {
                return t;
            }
        }

        return -1;
    }

    // --- natural neighbour ------------------------------------------------------------------------

    /// <summary>
    /// Sibson's natural neighbour value: insert the query into the triangulation, and weight each
    /// neighbour by the area its own Voronoi cell loses to the query's new one.
    /// </summary>
    /// <remarks>
    /// The region stolen from a neighbour is the intersection of two convex cells, so it is convex,
    /// and its corners are the circumcentres of the triangles the query destroys together with the
    /// circumcentres of the two virtual triangles the query makes with that neighbour. Collecting
    /// those corners and taking the area of their convex hull is what avoids having to walk the two
    /// cells in step, which is the fiddly half of every other way of writing this.
    /// </remarks>
    private double Natural(ReadOnlySpan<double> query, int simplex, ReadOnlySpan<double> weights)
    {
        if (_dimension != 2)
        {
            throw new InvalidOperationException("Natural neighbour interpolation is a planar method.");
        }

        // Landing on a data point makes every stolen area zero, and the answer is that point's value.
        for (int v = 0; v <= _dimension; v++)
        {
            if (weights[v] > 1 - 1e-12)
            {
                return _values[_simplices[simplex][v]];
            }
        }

        double qx = query[0];
        double qy = query[1];

        // The cavity is star-shaped about the query, so it can be grown outwards from the triangle
        // the query landed in rather than found by asking every triangle in the tessellation —
        // which is what keeps a natural-neighbour surface read over a fine grid from costing the
        // product of the two sizes.
        var cavity = new List<int>();
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        if (InCircumcircle(simplex, qx, qy))
        {
            pending.Push(simplex);
            seen.Add(simplex);
        }

        while (pending.Count > 0)
        {
            int at = pending.Pop();
            cavity.Add(at);
            for (int face = 0; face <= _dimension; face++)
            {
                int next = _mesh.Neighbour(at, face);
                if (next >= 0 && seen.Add(next))
                {
                    if (InCircumcircle(next, qx, qy))
                    {
                        pending.Push(next);
                    }
                }
            }
        }

        if (cavity.Count == 0)
        {
            return Affine(simplex, weights);
        }

        // The cavity's boundary: an edge belonging to one destroyed triangle rather than two.
        var edges = new List<(int A, int B)>();
        foreach (int t in cavity)
        {
            int[] cell = _simplices[t];
            AddOrCancel(edges, cell[0], cell[1]);
            AddOrCancel(edges, cell[1], cell[2]);
            AddOrCancel(edges, cell[2], cell[0]);
        }

        var ring = Ring(edges);
        if (ring.Count < 3)
        {
            return Affine(simplex, weights);
        }

        // The corners of the query's own new cell, one per boundary edge.
        int m = ring.Count;
        var newCorners = new (double X, double Y)[m];
        for (int i = 0; i < m; i++)
        {
            newCorners[i] = Circumcentre(
                qx, qy,
                _points[ring[i]][0], _points[ring[i]][1],
                _points[ring[(i + 1) % m]][0], _points[ring[(i + 1) % m]][1]);
        }

        var corners = new List<(double X, double Y)>();
        double total = 0;
        double sum = 0;
        for (int i = 0; i < m; i++)
        {
            corners.Clear();
            corners.Add(newCorners[(i + m - 1) % m]);
            corners.Add(newCorners[i]);
            foreach (int t in cavity)
            {
                int[] cell = _simplices[t];
                if (cell[0] == ring[i] || cell[1] == ring[i] || cell[2] == ring[i])
                {
                    corners.Add(Circumcentre(
                        _points[cell[0]][0], _points[cell[0]][1],
                        _points[cell[1]][0], _points[cell[1]][1],
                        _points[cell[2]][0], _points[cell[2]][1]));
                }
            }

            double stolen = ConvexArea(corners);
            total += stolen;
            sum += stolen * _values[ring[i]];
        }

        return total > 0 ? sum / total : Affine(simplex, weights);
    }

    private static void AddOrCancel(List<(int A, int B)> edges, int a, int b)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            if ((edges[i].A == a && edges[i].B == b) || (edges[i].A == b && edges[i].B == a))
            {
                edges.RemoveAt(i);
                return;
            }
        }

        edges.Add((a, b));
    }

    /// <summary>The cavity's boundary vertices in the order they run around it.</summary>
    private static List<int> Ring(List<(int A, int B)> edges)
    {
        var next = new Dictionary<int, int>();
        foreach ((int a, int b) in edges)
        {
            next[a] = b;
        }

        var ring = new List<int>();
        if (edges.Count == 0)
        {
            return ring;
        }

        int start = edges[0].A;
        int at = start;
        while (ring.Count <= edges.Count && next.TryGetValue(at, out int step))
        {
            ring.Add(at);
            at = step;
            if (at == start)
            {
                return ring;
            }
        }

        return [];
    }

    private bool InCircumcircle(int t, double px, double py)
    {
        int[] cell = _simplices[t];
        double ax = _points[cell[0]][0] - px;
        double ay = _points[cell[0]][1] - py;
        double bx = _points[cell[1]][0] - px;
        double by = _points[cell[1]][1] - py;
        double cx = _points[cell[2]][0] - px;
        double cy = _points[cell[2]][1] - py;
        double a2 = (ax * ax) + (ay * ay);
        double b2 = (bx * bx) + (by * by);
        double c2 = (cx * cx) + (cy * cy);
        double determinant =
            (a2 * ((bx * cy) - (by * cx)))
            - (b2 * ((ax * cy) - (ay * cx)))
            + (c2 * ((ax * by) - (ay * bx)));

        // The tessellation keeps its triangles counter-clockwise, so a positive determinant is
        // "inside" whichever way the triangle was handed over.
        double orientation = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
        return orientation >= 0 ? determinant > 0 : determinant < 0;
    }

    private static (double X, double Y) Circumcentre(
        double ax, double ay, double bx, double by, double cx, double cy)
    {
        double d = 2 * ((ax * (by - cy)) + (bx * (cy - ay)) + (cx * (ay - by)));
        if (d == 0)
        {
            return (double.NaN, double.NaN);
        }

        double a2 = (ax * ax) + (ay * ay);
        double b2 = (bx * bx) + (by * by);
        double c2 = (cx * cx) + (cy * cy);
        return (
            ((a2 * (by - cy)) + (b2 * (cy - ay)) + (c2 * (ay - by))) / d,
            ((a2 * (cx - bx)) + (b2 * (ax - cx)) + (c2 * (bx - ax))) / d);
    }

    /// <summary>The area of the convex hull of a handful of corners, by angle about their mean.</summary>
    private static double ConvexArea(List<(double X, double Y)> corners)
    {
        if (corners.Count < 3)
        {
            return 0;
        }

        double cx = 0;
        double cy = 0;
        foreach ((double x, double y) in corners)
        {
            if (double.IsNaN(x) || double.IsNaN(y))
            {
                return 0;
            }

            cx += x;
            cy += y;
        }

        cx /= corners.Count;
        cy /= corners.Count;
        var ordered = new List<(double X, double Y)>(corners);
        ordered.Sort((l, r) => Math.Atan2(l.Y - cy, l.X - cx).CompareTo(Math.Atan2(r.Y - cy, r.X - cx)));

        double twice = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            (double x1, double y1) = ordered[i];
            (double x2, double y2) = ordered[(i + 1) % ordered.Count];
            twice += (x1 * y2) - (x2 * y1);
        }

        return Math.Abs(twice) / 2;
    }

    // --- the C1 cubic -----------------------------------------------------------------------------

    /// <summary>
    /// The gradient estimated at each data point: the average of the plane gradients of the
    /// triangles sharing it, weighted by their areas. That rule is MATLAB's own — it was measured
    /// from the surface <c>griddata(…, 'cubic')</c> draws, which is a cubic Hermite along every
    /// triangulation edge and so states its own end gradients exactly.
    /// </summary>
    private double[][] Gradients()
    {
        if (_gradients is not null)
        {
            return _gradients;
        }

        var sum = new double[_points.Length][];
        var weight = new double[_points.Length];
        for (int i = 0; i < _points.Length; i++)
        {
            sum[i] = new double[2];
        }

        foreach (int[] cell in _simplices)
        {
            double x0 = _points[cell[0]][0], y0 = _points[cell[0]][1];
            double x1 = _points[cell[1]][0], y1 = _points[cell[1]][1];
            double x2 = _points[cell[2]][0], y2 = _points[cell[2]][1];
            double twice = ((x1 - x0) * (y2 - y0)) - ((y1 - y0) * (x2 - x0));
            if (twice == 0)
            {
                continue;
            }

            double v0 = _values[cell[0]], v1 = _values[cell[1]], v2 = _values[cell[2]];
            double gx = (((v1 - v0) * (y2 - y0)) - ((v2 - v0) * (y1 - y0))) / twice;
            double gy = (((v2 - v0) * (x1 - x0)) - ((v1 - v0) * (x2 - x0))) / twice;
            double area = Math.Abs(twice) / 2;
            foreach (int v in cell)
            {
                sum[v][0] += area * gx;
                sum[v][1] += area * gy;
                weight[v] += area;
            }
        }

        for (int i = 0; i < _points.Length; i++)
        {
            if (weight[i] > 0)
            {
                sum[i][0] /= weight[i];
                sum[i][1] /= weight[i];
            }
        }

        _gradients = sum;
        return sum;
    }

    private double Cubic(ReadOnlySpan<double> query, int simplex)
    {
        if (_dimension != 2)
        {
            throw new InvalidOperationException("Cubic scattered interpolation is a planar method.");
        }

        if (!_patches.TryGetValue(simplex, out double[][]? patch))
        {
            patch = CloughTocher.Build(
                Corner(simplex, 0), Corner(simplex, 1), Corner(simplex, 2),
                _values[_simplices[simplex][0]], _values[_simplices[simplex][1]], _values[_simplices[simplex][2]],
                Gradients()[_simplices[simplex][0]], Gradients()[_simplices[simplex][1]],
                Gradients()[_simplices[simplex][2]]);
            _patches[simplex] = patch;
        }

        return CloughTocher.Sample(
            patch, Corner(simplex, 0), Corner(simplex, 1), Corner(simplex, 2), query[0], query[1]);
    }

    private double[] Corner(int simplex, int v) => _points[_simplices[simplex][v]];

    // --- the biharmonic spline --------------------------------------------------------------------

    /// <summary>
    /// MATLAB 4's <c>'v4'</c>: a biharmonic spline through every data point, whose Green's function
    /// is <c>d² (log d − 1)</c> and whose diagonal is zero. Sandwell's construction, and a dense
    /// solve over the whole data set — which is why it is the one method here that costs the square
    /// of the number of points.
    /// </summary>
    private double Biharmonic(ReadOnlySpan<double> query)
    {
        if (_dimension != 2)
        {
            throw new InvalidOperationException("The 'v4' method is a planar method.");
        }

        _biharmonicWeights ??= BiharmonicWeights();
        double sum = 0;
        for (int i = 0; i < _points.Length; i++)
        {
            double dx = query[0] - _points[i][0];
            double dy = query[1] - _points[i][1];
            double d = Math.Sqrt((dx * dx) + (dy * dy));
            if (d != 0)
            {
                sum += d * d * (Math.Log(d) - 1) * _biharmonicWeights[i];
            }
        }

        return sum;
    }

    private double[] BiharmonicWeights()
    {
        int n = _points.Length;
        var g = new double[n][];
        for (int i = 0; i < n; i++)
        {
            g[i] = new double[n];
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                double dx = _points[i][0] - _points[j][0];
                double dy = _points[i][1] - _points[j][1];
                double d = Math.Sqrt((dx * dx) + (dy * dy));
                g[i][j] = d * d * (Math.Log(d) - 1);
            }
        }

        var rhs = (double[])_values.Clone();
        SolveDense(g, rhs, n);
        return rhs;
    }

    /// <summary>Solves a dense system in place by Gaussian elimination with partial pivoting.</summary>
    internal static void SolveDense(double[][] m, double[] rhs, int size)
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

            rhs[r] = m[r][r] == 0 ? 0 : sum / m[r][r];
        }
    }
}
