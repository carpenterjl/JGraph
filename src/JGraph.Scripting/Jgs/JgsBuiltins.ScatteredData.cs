using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using JGraph.Maths.Geometry;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Scattered data (M129): <c>griddata</c> and <c>griddatan</c>, the two interpolant values
/// <c>scatteredInterpolant</c> and <c>griddedInterpolant</c>, the N-D tessellation verbs
/// <c>delaunayn</c>, <c>tsearchn</c>, <c>dsearchn</c> and <c>convhulln</c>, the shrinking hull
/// <c>boundary</c>, and the STL pair.
/// </summary>
/// <remarks>
/// <para>
/// An interpolant here is a value that is a field, the way ADR 0102's <c>pp</c> is a value that is a
/// curve — a struct carrying the properties MATLAB documents, tagged with the class name so that
/// <c>class(F)</c> answers what MATLAB's does, and called with parentheses like a function. Building
/// it that way is what makes <c>F.Values = …</c> and <c>F.Method = 'natural'</c> work with nothing
/// written for them: they are field writes on a struct, and the next call reads what they left.
/// </para>
/// <para>
/// The tessellation behind a scattered interpolant is expensive and the values in front of it are
/// not, which is exactly the split MATLAB's own object makes when it documents that replacing
/// <c>Values</c> re-uses the triangulation. That is kept here by a cache hung off the struct's own
/// field table: a call whose points are unchanged re-uses the surface and only re-hangs the values.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string ScatteredClass = "scatteredInterpolant";
    private const string GriddedClass = "griddedInterpolant";

    private sealed class ScatteredCache
    {
        public double[][] Points = [];
        public double[] Values = [];
        public ScatteredSurface? Surface;
    }

    private static readonly ConditionalWeakTable<Dictionary<string, JgsValue>, ScatteredCache> Surfaces = [];

    /// <summary>Registers the scattered-data family.</summary>
    internal static void RegisterScatteredDataBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineMany(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => body(args, 1, line, col)[0])
            {
                MultiOutput = (args, wanted, line, col) => body(args, wanted, line, col),
            }));

        DefineMany("griddata", (args, wanted, line, col) => GridData(args, wanted, host, line, col));
        Define("griddatan", (args, line, col) => GridDataN(args, line, col));
        Define("delaunayn", (args, line, col) => DelaunayNAnswer(args, line, col));
        DefineMany("tsearchn", TriangleSearch);
        DefineMany("dsearchn", NearestSearch);
        DefineMany("convhulln", ConvexHullN);
        DefineMany("boundary", BoundaryAnswer);
        DefineMany("stlread", StlRead);
        Define("stlwrite", (args, line, col) => StlWrite(args, line, col));
        Define("scatteredInterpolant", (args, line, col) => NewScattered(args, line, col));
        Define("griddedInterpolant", (args, line, col) => NewGridded(args, host, line, col));
    }

    /// <summary>Whether a value is one of the two interpolant objects, which are called rather than indexed.</summary>
    internal static bool IsInterpolant(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is ScatteredClass or GriddedClass;

    /// <summary>Reads an interpolant at the points a call named.</summary>
    internal static JgsValue CallInterpolant(
        JgsValue interpolant, IReadOnlyList<JgsValue> args, int line, int col) =>
        interpolant.ClassName == ScatteredClass
            ? SampleScattered(interpolant, args, line, col)
            : SampleGriddedInterpolant(interpolant, args, line, col);

    // --- griddata ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>griddata</c> in every documented form: five arguments in the plane, seven in space, an
    /// optional method word, and the three-output form that hands back the grid it built.
    /// </summary>
    private static JgsValue[] GridData(
        IReadOnlyList<JgsValue> args, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        ArityRange("griddata", args, 5, 9, line, col);
        int count = args.Count;
        string method = "linear";
        if (IsTextScalar(args[count - 1]))
        {
            method = Str("griddata", args, count - 1, line, col).ToLowerInvariant();
            count--;
        }

        if (method is not ("nearest" or "linear" or "natural" or "cubic" or "v4"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddata:UnknownMethod",
                "griddata: the methods are 'nearest', 'linear', 'natural', 'cubic' and 'v4'.");
        }

        int directions = count switch
        {
            5 => 2,
            7 => 3,
            _ => throw new JgsRuntimeException(line, col, "MATLAB:griddata:InvalidNumInputArgs",
                "griddata takes five arguments in the plane or seven in space, and an optional method."),
        };

        if (directions == 3 && method == "cubic")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddata:CubicMethod3D",
                "griddata: the 'cubic' method is only available in the plane.");
        }

        if (directions == 3 && method == "v4")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddata:V4Method3D",
                "griddata: the 'v4' method is only available in the plane.");
        }

        var sites = new double[directions][];
        for (int k = 0; k < directions; k++)
        {
            sites[k] = FlattenColumnMajor("griddata", args[k], line, col);
        }

        double[] values = FlattenColumnMajor("griddata", args[directions], line, col);
        var queryArgs = new JgsValue[directions];
        for (int k = 0; k < directions; k++)
        {
            queryArgs[k] = args[directions + 1 + k];
        }

        // A row of x against a column of y describes a grid rather than a list of points, which is
        // what makes the three-output form worth having: it hands the grid back.
        (double[][] queries, int[] shape, JgsValue[] expanded) =
            ExpandQueries("griddata", queryArgs, directions, line, col);

        double[] answer = SampleScattered(
            sites, values, queries, ScatteredMethodOf(method), ScatteredExtrapolation.None, host, line, col);

        JgsValue vq = ShapedNumbers(answer, shape);
        if (wanted <= 1)
        {
            return [vq];
        }

        var outputs = new JgsValue[Math.Min(wanted, directions + 1)];
        for (int k = 0; k < outputs.Length - 1; k++)
        {
            outputs[k] = expanded[k];
        }

        outputs[^1] = vq;
        return outputs;
    }

    private static ScatteredMethod ScatteredMethodOf(string method) => method switch
    {
        "nearest" => ScatteredMethod.Nearest,
        "natural" => ScatteredMethod.Natural,
        "cubic" => ScatteredMethod.Cubic,
        "v4" => ScatteredMethod.Biharmonic,
        _ => ScatteredMethod.Linear,
    };

    /// <summary>
    /// The query points a call named, and the shape the answer takes. Vectors of different shapes
    /// name the grid of their combinations — <c>meshgrid</c>'s ordering, x across the columns — and
    /// anything else is read point by point.
    /// </summary>
    private static (double[][] Points, int[] Shape, JgsValue[] Expanded) ExpandQueries(
        string name, JgsValue[] given, int directions, int line, int col)
    {
        bool allVectors = true;
        bool sameShape = true;
        int[] first = SizeDims(given[0]);
        for (int k = 0; k < directions; k++)
        {
            int[] dims = SizeDims(given[k]);
            allVectors &= dims.Length == 2 && (dims[0] == 1 || dims[1] == 1);
            sameShape &= SameShape(first, dims);
        }

        if (allVectors && !sameShape)
        {
            var axes = new double[directions][];
            for (int k = 0; k < directions; k++)
            {
                axes[k] = FlattenColumnMajor(name, given[k], line, col);
            }

            // meshgrid runs y down the rows and x across the columns, so the first two directions
            // are swapped against the array's own order.
            int rows = axes[1].Length;
            int cols = axes[0].Length;
            int pages = directions == 3 ? axes[2].Length : 1;
            int total = rows * cols * pages;
            var points = new double[directions][];
            for (int k = 0; k < directions; k++)
            {
                points[k] = new double[total];
            }

            for (int p = 0; p < pages; p++)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        int at = r + (c * rows) + (p * rows * cols);
                        points[0][at] = axes[0][c];
                        points[1][at] = axes[1][r];
                        if (directions == 3)
                        {
                            points[2][at] = axes[2][p];
                        }
                    }
                }
            }

            int[] shape = directions == 3 ? [rows, cols, pages] : [rows, cols];
            var expanded = new JgsValue[directions];
            for (int k = 0; k < directions; k++)
            {
                expanded[k] = ShapedNumbers(points[k], shape);
            }

            return (points, shape, expanded);
        }

        var flat = new double[directions][];
        for (int k = 0; k < directions; k++)
        {
            flat[k] = FlattenColumnMajor(name, given[k], line, col);
            if (flat[k].Length != flat[0].Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:scatteredInterpolant:InputMixSizeErrId",
                    "The query coordinate arrays must be the same size.");
            }
        }

        return (flat, first, given);
    }

    /// <summary>Builds a surface over the sites and reads it at every query point.</summary>
    private static double[] SampleScattered(
        double[][] sites, double[] values, double[][] queries,
        ScatteredMethod method, ScatteredExtrapolation outside,
        JGraphScriptGlobals host, int line, int col)
    {
        int directions = sites.Length;
        foreach (double[] axis in sites)
        {
            if (axis.Length != values.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "The coordinates and the values must have the same number of entries.");
            }
        }

        (double[][] points, double[] merged, bool duplicates) = MergePoints(sites, values);
        if (duplicates)
        {
            host.WriteErr("Warning: Duplicate data points have been detected and removed.\n");
        }

        if (points.Length < directions + 1)
        {
            throw new JgsRuntimeException(line, col,
                $"A surface through scattered data needs at least {directions + 1} distinct points.");
        }

        var surface = new ScatteredSurface(points, merged);
        int count = queries.Length == 0 ? 0 : queries[0].Length;
        var answer = new double[count];
        var point = new double[directions];
        for (int q = 0; q < count; q++)
        {
            for (int k = 0; k < directions; k++)
            {
                point[k] = queries[k][q];
            }

            try
            {
                answer[q] = surface.Sample(point, method, outside);
            }
            catch (InvalidOperationException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        return answer;
    }

    /// <summary>
    /// Points that share a place are one point, carrying the mean of their values — the same
    /// averaging <c>griddata</c> does before it triangulates, and for the same reason: a
    /// tessellation cannot hold two points in one place.
    /// </summary>
    private static (double[][] Points, double[] Values, bool Duplicates) MergePoints(
        double[][] sites, double[] values)
    {
        int directions = sites.Length;
        int n = values.Length;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var points = new List<double[]>();
        var sums = new List<double>();
        var counts = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var point = new double[directions];
            for (int k = 0; k < directions; k++)
            {
                point[k] = sites[k][i];
            }

            string key = string.Join(',', Array.ConvertAll(point, static v => v.ToString("R", CultureInfo.InvariantCulture)));
            if (index.TryGetValue(key, out int already))
            {
                sums[already] += values[i];
                counts[already]++;
                continue;
            }

            index[key] = points.Count;
            points.Add(point);
            sums.Add(values[i]);
            counts.Add(1);
        }

        var merged = new double[points.Count];
        for (int i = 0; i < merged.Length; i++)
        {
            merged[i] = sums[i] / counts[i];
        }

        return ([.. points], merged, points.Count < n);
    }

    // --- griddatan --------------------------------------------------------------------------------

    /// <summary><c>griddatan(X, Y, XI)</c>, with the optional method word and Qhull option list.</summary>
    private static JgsValue GridDataN(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("griddatan", args, 3, 5, line, col);
        double[,] x = RectOf("griddatan", args[0], line, col);
        double[] y = FlattenColumnMajor("griddatan", args[1], line, col);
        double[,] xi = RectOf("griddatan", args[2], line, col);
        string method = "linear";
        if (args.Count >= 4 && IsTextScalar(args[3]))
        {
            method = Str("griddatan", args, 3, line, col).ToLowerInvariant();
        }

        if (method is not ("linear" or "nearest"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddatan:InvalidMethod",
                "griddatan takes 'linear' or 'nearest'.");
        }

        int n = x.GetLength(0);
        int d = x.GetLength(1);
        if (d <= 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddatan:XLowColNum",
                "griddatan needs at least two columns of coordinates.");
        }

        if (y.Length != n)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddatan:InputSizeMismatch",
                "griddatan needs one value for every point.");
        }

        var sites = new double[d][];
        for (int k = 0; k < d; k++)
        {
            sites[k] = new double[n];
            for (int i = 0; i < n; i++)
            {
                sites[k][i] = x[i, k];
            }
        }

        int m = xi.GetLength(0);
        var queries = new double[d][];
        for (int k = 0; k < d; k++)
        {
            queries[k] = new double[m];
            for (int i = 0; i < m; i++)
            {
                queries[k][i] = xi[i, k];
            }
        }

        (double[][] points, double[] merged, _) = MergePoints(sites, y);
        var surface = new ScatteredSurface(points, merged);
        var answer = new double[m];
        var point = new double[d];
        for (int q = 0; q < m; q++)
        {
            for (int k = 0; k < d; k++)
            {
                point[k] = queries[k][q];
            }

            // griddatan's nearest is dsearchn with no outval, so it answers everywhere; its linear
            // is tsearchn, which has nothing to say outside the hull and returns NaN there.
            answer[q] = method == "nearest"
                ? surface.Sample(point, ScatteredMethod.Nearest, ScatteredExtrapolation.Nearest)
                : surface.Sample(point, ScatteredMethod.Linear, ScatteredExtrapolation.None);
        }

        return JgsMatrix.FromColumnMajor(answer, m, 1);
    }

    // --- the tessellation verbs -------------------------------------------------------------------

    private static JgsValue DelaunayNAnswer(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("delaunayn", args, 1, 2, line, col);
        double[,] x = RectOf("delaunayn", args[0], line, col);
        if (x.GetLength(1) < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:delaunayn:XLowColNum",
                "delaunayn needs at least one column of coordinates.");
        }

        if (x.GetLength(0) < x.GetLength(1) + 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:delaunayn:NotEnoughPtsForTessel",
                "delaunayn needs at least one more point than there are directions.");
        }

        int[,] cells = DelaunayN.Simplices(x);
        return JgsMatrix.Build(cells.GetLength(0), cells.GetLength(1), (t, v) => cells[t, v] + 1.0);
    }

    /// <summary><c>[t, p] = tsearchn(X, T, XI)</c>.</summary>
    private static JgsValue[] TriangleSearch(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("tsearchn", args, 3, line, col);
        (SimplexMesh mesh, int d) = MeshOf("tsearchn", args[0], args[1], line, col);
        double[,] xi = RectOf("tsearchn", args[2], line, col);
        if (xi.GetLength(1) != d)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:tsearchn:InvalidDimensions",
                "tsearchn needs the query points to have as many columns as the data points.");
        }

        int m = xi.GetLength(0);
        var found = new double[m];
        var weights = new double[m * (d + 1)];
        Array.Fill(weights, double.NaN);
        var query = new double[d];
        var bary = new double[d + 1];
        for (int q = 0; q < m; q++)
        {
            for (int k = 0; k < d; k++)
            {
                query[k] = xi[q, k];
            }

            int at = mesh.Locate(query, bary);
            found[q] = at < 0 ? double.NaN : at + 1;
            if (at >= 0)
            {
                for (int v = 0; v <= d; v++)
                {
                    weights[q + (v * m)] = bary[v];
                }
            }
        }

        JgsValue simplices = JgsMatrix.FromColumnMajor(found, m, 1);
        return wanted <= 1
            ? [simplices]
            : [simplices, JgsMatrix.FromColumnMajor(weights, m, d + 1)];
    }

    /// <summary><c>[k, d] = dsearchn(X, T, XI, outval)</c> and the two-argument form.</summary>
    private static JgsValue[] NearestSearch(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("dsearchn", args, 2, 6, line, col);
        double[,] x = RectOf("dsearchn", args[0], line, col);
        bool tessellated = args.Count >= 3;
        double[,] xi = RectOf("dsearchn", args[tessellated ? 2 : 1], line, col);
        int d = x.GetLength(1);
        if (xi.GetLength(1) != d)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dsearchn:XandXIColSizeMismatch",
                "dsearchn needs the query points to have as many columns as the data points.");
        }

        double? outval = null;
        if (args.Count >= 4 && !IsEmptyValue(args[3]))
        {
            outval = Num("dsearchn", args, 3, line, col);
        }

        SimplexMesh? mesh = null;
        if (tessellated && outval is not null)
        {
            (mesh, _) = MeshOf("dsearchn", args[0], args[1], line, col);
        }

        int n = x.GetLength(0);
        int m = xi.GetLength(0);
        var indices = new double[m];
        var distances = new double[m];
        var query = new double[d];
        var bary = new double[d + 1];
        for (int q = 0; q < m; q++)
        {
            for (int k = 0; k < d; k++)
            {
                query[k] = xi[q, k];
            }

            int best = -1;
            double least = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                double squared = 0;
                for (int k = 0; k < d; k++)
                {
                    double delta = x[i, k] - query[k];
                    squared += delta * delta;
                }

                if (squared < least)
                {
                    least = squared;
                    best = i;
                }
            }

            indices[q] = best + 1;
            distances[q] = Math.Sqrt(least);
            if (mesh is not null && mesh.Locate(query, bary) < 0)
            {
                indices[q] = outval!.Value;
            }
        }

        JgsValue found = JgsMatrix.FromColumnMajor(indices, m, 1);
        return wanted <= 1
            ? [found]
            : [found, JgsMatrix.FromColumnMajor(distances, m, 1)];
    }

    private static (SimplexMesh Mesh, int Dimension) MeshOf(
        string name, JgsValue pointsValue, JgsValue cellsValue, int line, int col)
    {
        double[,] x = RectOf(name, pointsValue, line, col);
        double[,] t = RectOf(name, cellsValue, line, col);
        int d = x.GetLength(1);
        if (t.GetLength(1) != d + 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:InvalidTessellation",
                $"{name}: the tessellation must have one more column than the points do.");
        }

        var points = new double[x.GetLength(0)][];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new double[d];
            for (int k = 0; k < d; k++)
            {
                points[i][k] = x[i, k];
            }
        }

        var cells = new int[t.GetLength(0)][];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = new int[d + 1];
            for (int k = 0; k <= d; k++)
            {
                cells[i][k] = (int)t[i, k] - 1;
            }
        }

        return (new SimplexMesh(points, cells), d);
    }

    /// <summary>
    /// <c>[K, V] = convhulln(X)</c>. The hull's facets are the facets of the Delaunay tessellation
    /// that belong to one simplex rather than two, which is the same answer Qhull gives without
    /// needing a second algorithm; in the plane they are then wound once round, as MATLAB does.
    /// </summary>
    private static JgsValue[] ConvexHullN(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("convhulln", args, 1, 2, line, col);
        double[,] x = RectOf("convhulln", args[0], line, col);
        int d = x.GetLength(1);
        if (d <= 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:convhulln:XLowColNum",
                "convhulln needs at least two columns of coordinates.");
        }

        int[,] cells = DelaunayN.Simplices(x);
        var simplices = new int[cells.GetLength(0)][];
        for (int t = 0; t < simplices.Length; t++)
        {
            simplices[t] = new int[d + 1];
            for (int v = 0; v <= d; v++)
            {
                simplices[t][v] = cells[t, v];
            }
        }

        List<int[]> facets = SimplexMesh.BoundaryFacets(simplices, d);
        double volume = HullVolume(facets, x, d);

        if (d == 2)
        {
            // MATLAB hands the planar hull back as edges in counter-clockwise order, which is what
            // its own convhulln does after Qhull, and is why the plane is the one case with a rule.
            var on = new SortedSet<int>();
            foreach (int[] facet in facets)
            {
                foreach (int v in facet)
                {
                    on.Add(v);
                }
            }

            var vertices = new List<int>(on);
            double cx = 0;
            double cy = 0;
            foreach (int v in vertices)
            {
                cx += x[v, 0];
                cy += x[v, 1];
            }

            cx /= vertices.Count;
            cy /= vertices.Count;
            vertices.Sort((l, r) =>
                Math.Atan2(x[l, 1] - cy, x[l, 0] - cx).CompareTo(Math.Atan2(x[r, 1] - cy, x[r, 0] - cx)));

            int count = vertices.Count;
            JgsValue edges = JgsMatrix.Build(count, 2, (i, k) =>
                vertices[k == 0 ? i : (i + 1) % count] + 1.0);
            return wanted <= 1 ? [edges] : [edges, JgsValue.Number(volume)];
        }

        JgsValue answer = JgsMatrix.Build(facets.Count, d, (i, k) => facets[i][k] + 1.0);
        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(volume)];
    }

    /// <summary>The content of a hull, as the cones from an interior point onto each of its facets.</summary>
    private static double HullVolume(List<int[]> facets, double[,] x, int d)
    {
        int n = x.GetLength(0);
        var centre = new double[d];
        for (int k = 0; k < d; k++)
        {
            for (int i = 0; i < n; i++)
            {
                centre[k] += x[i, k];
            }

            centre[k] /= n;
        }

        double factorial = 1;
        for (int i = 2; i <= d; i++)
        {
            factorial *= i;
        }

        double total = 0;
        foreach (int[] facet in facets)
        {
            var m = new double[d][];
            for (int r = 0; r < d; r++)
            {
                m[r] = new double[d];
                for (int k = 0; k < d; k++)
                {
                    m[r][k] = x[facet[r], k] - centre[k];
                }
            }

            total += Math.Abs(SmallDeterminant(m, d)) / factorial;
        }

        return total;
    }

    private static double SmallDeterminant(double[][] m, int size)
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

    // --- boundary ---------------------------------------------------------------------------------

    /// <summary><c>[K, V] = boundary(x, y[, z][, s])</c> and the matrix-of-points form.</summary>
    private static JgsValue[] BoundaryAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("boundary", args, 1, 4, line, col);
        int count = args.Count;
        double shrink = 0.5;
        if (count > 1 && ElementCount(args[count - 1]) == 1 && !IsTextScalar(args[count - 1])
            && (count == 4 || ElementCount(args[0]) != 1))
        {
            shrink = Num("boundary", args, count - 1, line, col);
            count--;
        }

        if (shrink < 0 || shrink > 1 || double.IsNaN(shrink))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:boundary:InvalidShrinkFactor",
                "boundary: the shrink factor must be between 0 and 1.");
        }

        (double[] x, double[] y, double[]? z) = CoordinatesOf("boundary", args, count, line, col);
        if (z is null)
        {
            (int[] loop, double area) = AlphaBoundary.Plane(x, y, shrink);
            JgsValue indices = JgsMatrix.Build(loop.Length, 1, (i, _) => loop[i] + 1.0);
            return wanted <= 1 ? [indices] : [indices, JgsValue.Number(area)];
        }

        (int[,] facets, double volume) = AlphaBoundary.Space(x, y, z, shrink);
        JgsValue triangles = JgsMatrix.Build(facets.GetLength(0), 3, (t, v) => facets[t, v] + 1.0);
        return wanted <= 1 ? [triangles] : [triangles, JgsValue.Number(volume)];
    }

    // --- STL --------------------------------------------------------------------------------------

    /// <summary><c>[TR, fileformat, attributes, solidID] = stlread(filename)</c>.</summary>
    private static JgsValue[] StlRead(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("stlread", args, 1, line, col);
        string path = Str("stlread", args, 0, line, col);
        StlMesh mesh;
        try
        {
            mesh = StlIo.Read(path);
        }
        catch (FileNotFoundException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyfun:stlFileCannotOpen",
                $"stlread: cannot open '{path}'.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyfun:stlFailedToRead",
                $"stlread: '{path}' could not be read as an STL file.");
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Points"] = JgsMatrix.Build(mesh.Points.GetLength(0), 3, (i, k) => mesh.Points[i, k]),
            ["ConnectivityList"] = JgsMatrix.Build(
                mesh.Faces.GetLength(0), 3, (t, v) => mesh.Faces[t, v] + 1.0),
        };

        JgsValue triangulation = JgsValue.Struct(fields);
        if (wanted <= 1)
        {
            return [triangulation];
        }

        var outputs = new List<JgsValue> { triangulation, JgsValue.Str(mesh.Format) };
        if (wanted >= 3)
        {
            outputs.Add(JgsMatrix.Build(mesh.Attributes.Length, 1, (i, _) => mesh.Attributes[i]));
        }

        if (wanted >= 4)
        {
            outputs.Add(JgsMatrix.Build(mesh.SolidIndex.Length, 1, (i, _) => mesh.SolidIndex[i]));
        }

        return [.. outputs];
    }

    /// <summary><c>stlwrite(TR, filename, format, 'Attribute', a, 'SolidIndex', s)</c>.</summary>
    private static JgsValue StlWrite(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("stlwrite", args, 2, 7, line, col);
        if (args[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyfun:NotTriangulation",
                "stlwrite: the first argument is a triangulation — a value with Points and a "
                + "ConnectivityList.");
        }

        Dictionary<string, JgsValue> fields = args[0].AsStruct;
        if (!fields.TryGetValue("Points", out JgsValue? pointsValue)
            || !fields.TryGetValue("ConnectivityList", out JgsValue? listValue))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyfun:NotTriangulation",
                "stlwrite: the triangulation must carry Points and ConnectivityList.");
        }

        double[,] points = RectOf("stlwrite", pointsValue, line, col);
        double[,] list = RectOf("stlwrite", listValue, line, col);
        if (list.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyfun:stlTrisOnly",
                "stlwrite: only triangles can be written to an STL file.");
        }

        string path = Str("stlwrite", args, 1, line, col);
        bool binary = true;
        ushort[]? attributes = null;
        double[]? solids = null;
        int at = 2;
        if (at < args.Count && IsTextScalar(args[at]))
        {
            string word = Str("stlwrite", args, at, line, col);
            if (word.StartsWith("text", StringComparison.OrdinalIgnoreCase))
            {
                binary = false;
                at++;
            }
            else if (word.StartsWith("binary", StringComparison.OrdinalIgnoreCase))
            {
                at++;
            }
        }

        for (; at + 1 < args.Count; at += 2)
        {
            string word = Str("stlwrite", args, at, line, col);
            double[] given = FlattenColumnMajor("stlwrite", args[at + 1], line, col);
            if (word.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            {
                attributes = Array.ConvertAll(given, static v => (ushort)v);
            }
            else if (word.StartsWith("SolidIndex", StringComparison.OrdinalIgnoreCase))
            {
                solids = given;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:polyfun:stlWriteParameter",
                    $"stlwrite: '{word}' is not a parameter it takes.");
            }
        }

        int triangles = list.GetLength(0);
        var faces = new int[triangles, 3];
        for (int t = 0; t < triangles; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                faces[t, v] = (int)list[t, v] - 1;
            }
        }

        // MATLAB gathers the triangles of one solid into one block before writing a text file, in
        // ascending order of the identifier, which is what makes a re-read give back the grouping.
        if (solids is not null && !binary)
        {
            var order = new int[triangles];
            for (int t = 0; t < triangles; t++)
            {
                order[t] = t;
            }

            double[] keys = solids;
            Array.Sort(order, (l, r) => keys[l].CompareTo(keys[r]));
            var sortedFaces = new int[triangles, 3];
            var sortedSolids = new double[triangles];
            for (int t = 0; t < triangles; t++)
            {
                for (int v = 0; v < 3; v++)
                {
                    sortedFaces[t, v] = faces[order[t], v];
                }

                sortedSolids[t] = keys[order[t]];
            }

            faces = sortedFaces;
            solids = sortedSolids;
        }

        StlIo.Write(path, points, faces, binary, attributes, solids);
        return JgsValue.Null;
    }

    // --- scatteredInterpolant ---------------------------------------------------------------------

    private static JgsValue NewScattered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("scatteredInterpolant", args, 0, 6, line, col);
        int count = args.Count;
        string method = "linear";
        string? extrapolation = null;
        if (count > 0 && IsTextScalar(args[count - 1]) && count >= 2 && IsTextScalar(args[count - 2]))
        {
            method = Str("scatteredInterpolant", args, count - 2, line, col).ToLowerInvariant();
            extrapolation = Str("scatteredInterpolant", args, count - 1, line, col).ToLowerInvariant();
            count -= 2;
        }
        else if (count > 0 && IsTextScalar(args[count - 1]))
        {
            method = Str("scatteredInterpolant", args, count - 1, line, col).ToLowerInvariant();
            count--;
        }

        if (method is not ("linear" or "nearest" or "natural"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:scatteredInterpolant:UnknownMethod",
                "scatteredInterpolant takes 'linear', 'nearest' or 'natural'.");
        }

        // MATLAB's default outside the hull follows the method: 'nearest' carries its own answer on,
        // and the other two continue linearly.
        extrapolation ??= method == "nearest" ? "nearest" : "linear";
        if (extrapolation is not ("none" or "nearest" or "linear"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:scatteredInterpolant:UnknownExtrapMethod",
                "scatteredInterpolant takes 'none', 'nearest' or 'linear' outside the hull.");
        }

        JgsValue points;
        JgsValue values;
        if (count == 0)
        {
            points = JgsMatrix.FromColumnMajor([], 0, 0);
            values = JgsMatrix.FromColumnMajor([], 0, 0);
        }
        else if (count == 2)
        {
            points = args[0];
            values = AsColumnValue("scatteredInterpolant", args[1], line, col);
        }
        else if (count is 3 or 4)
        {
            int directions = count - 1;
            var columns = new double[directions][];
            for (int k = 0; k < directions; k++)
            {
                columns[k] = FlattenColumnMajor("scatteredInterpolant", args[k], line, col);
            }

            points = JgsMatrix.Build(columns[0].Length, directions, (i, k) => columns[k][i]);
            values = AsColumnValue("scatteredInterpolant", args[directions], line, col);
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "scatteredInterpolant takes the points and their values, either as one matrix of "
                + "points or as one array per direction.");
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Points"] = points,
            ["Values"] = values,
            ["Method"] = JgsValue.Str(method),
            ["ExtrapolationMethod"] = JgsValue.Str(extrapolation),
        };

        JgsValue interpolant = JgsValue.Struct(fields);
        interpolant.SetClassName(ScatteredClass);
        return interpolant;
    }

    private static JgsValue AsColumnValue(string name, JgsValue value, int line, int col)
    {
        double[] flat = FlattenColumnMajor(name, value, line, col);
        return JgsMatrix.FromColumnMajor(flat, flat.Length, 1);
    }

    private static JgsValue SampleScattered(
        JgsValue interpolant, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Dictionary<string, JgsValue> fields = interpolant.AsStruct;
        double[,] pointsMatrix = RectOf(ScatteredClass, fields["Points"], line, col);
        double[] values = FlattenColumnMajor(ScatteredClass, fields["Values"], line, col);
        int n = pointsMatrix.GetLength(0);
        int d = pointsMatrix.GetLength(1);
        if (n == 0 || values.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                "The interpolant has no points, or one value per point is missing.");
        }

        var points = new double[n][];
        for (int i = 0; i < n; i++)
        {
            points[i] = new double[d];
            for (int k = 0; k < d; k++)
            {
                points[i][k] = pointsMatrix[i, k];
            }
        }

        ScatteredSurface surface = CachedSurface(fields, points, values);
        ScatteredMethod method = ScatteredMethodOf(Str(ScatteredClass, [fields["Method"]], 0, line, col).ToLowerInvariant());
        ScatteredExtrapolation outside =
            Str(ScatteredClass, [fields["ExtrapolationMethod"]], 0, line, col).ToLowerInvariant() switch
            {
                "none" => ScatteredExtrapolation.None,
                "nearest" => ScatteredExtrapolation.Nearest,
                _ => ScatteredExtrapolation.Linear,
            };

        double[][] queries;
        int[] shape;
        if (args.Count == 1)
        {
            double[,] asked = RectOf(ScatteredClass, args[0], line, col);
            if (asked.GetLength(1) != d)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:scatteredInterpolant:InputMixSizeErrId",
                    $"The query points must have {d} columns.");
            }

            int m = asked.GetLength(0);
            queries = new double[d][];
            for (int k = 0; k < d; k++)
            {
                queries[k] = new double[m];
                for (int i = 0; i < m; i++)
                {
                    queries[k][i] = asked[i, k];
                }
            }

            shape = [m, 1];
        }
        else if (args.Count == d)
        {
            var given = new JgsValue[d];
            for (int k = 0; k < d; k++)
            {
                given[k] = args[k];
            }

            int[] first = SizeDims(given[0]);
            for (int k = 1; k < d; k++)
            {
                if (!SameShape(first, SizeDims(given[k])))
                {
                    throw new JgsRuntimeException(line, col,
                        "MATLAB:scatteredInterpolant:InputMixSizeErrId",
                        "The query coordinate arrays must be the same size.");
                }
            }

            queries = new double[d][];
            for (int k = 0; k < d; k++)
            {
                queries[k] = FlattenColumnMajor(ScatteredClass, given[k], line, col);
            }

            shape = first;
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                $"A scattered interpolant over {d} directions is read at one matrix of points or at "
                + $"{d} coordinate arrays.");
        }

        int count = queries.Length == 0 ? 0 : queries[0].Length;
        var answer = new double[count];
        var point = new double[d];
        for (int q = 0; q < count; q++)
        {
            for (int k = 0; k < d; k++)
            {
                point[k] = queries[k][q];
            }

            answer[q] = surface.Sample(point, method, outside);
        }

        return ShapedNumbers(answer, shape);
    }

    /// <summary>
    /// The surface behind an interpolant, built once. A call whose points have not moved re-uses the
    /// tessellation and only re-hangs the values, which is what MATLAB documents about writing to
    /// <c>Values</c> and is the difference between a redraw costing a millisecond and a second.
    /// </summary>
    private static ScatteredSurface CachedSurface(
        Dictionary<string, JgsValue> fields, double[][] points, double[] values)
    {
        ScatteredCache cache = Surfaces.GetOrCreateValue(fields);
        bool samePoints = cache.Surface is not null && cache.Points.Length == points.Length;
        for (int i = 0; samePoints && i < points.Length; i++)
        {
            for (int k = 0; k < points[i].Length && samePoints; k++)
            {
                samePoints = cache.Points[i][k].Equals(points[i][k]);
            }
        }

        if (!samePoints)
        {
            cache.Surface = new ScatteredSurface(points, values);
            cache.Points = points;
            cache.Values = values;
            return cache.Surface;
        }

        bool sameValues = cache.Values.Length == values.Length;
        for (int i = 0; sameValues && i < values.Length; i++)
        {
            sameValues = cache.Values[i].Equals(values[i]);
        }

        if (!sameValues)
        {
            cache.Surface!.Values = values;
            cache.Values = values;
        }

        return cache.Surface!;
    }

    // --- griddedInterpolant -----------------------------------------------------------------------

    private static JgsValue NewGridded(
        IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, int line, int col)
    {
        ArityRange("griddedInterpolant", args, 1, 12, line, col);
        int count = args.Count;
        string? method = null;
        string? extrapolation = null;
        if (count >= 2 && IsTextScalar(args[count - 1]) && IsTextScalar(args[count - 2]))
        {
            method = Str("griddedInterpolant", args, count - 2, line, col).ToLowerInvariant();
            extrapolation = Str("griddedInterpolant", args, count - 1, line, col).ToLowerInvariant();
            count -= 2;
        }
        else if (count >= 1 && IsTextScalar(args[count - 1]))
        {
            method = Str("griddedInterpolant", args, count - 1, line, col).ToLowerInvariant();
            count--;
        }

        method ??= "linear";
        if (method is not ("linear" or "nearest" or "next" or "previous" or "pchip" or "cubic"
            or "makima" or "spline"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:BadInterpTypeErrId",
                "griddedInterpolant takes 'linear', 'nearest', 'next', 'previous', 'pchip', 'cubic', "
                + "'makima' or 'spline'.");
        }

        JgsValue values = args[count - 1];
        JgsValue vectors;
        if (count == 1)
        {
            int[] dims = SizeDims(values);
            int rank = RankOf(values);
            var implied = new JgsValue[rank];
            for (int k = 0; k < rank; k++)
            {
                int length = rank == 1 ? ElementCount(values) : dims[k];
                var axis = new double[length];
                for (int i = 0; i < length; i++)
                {
                    axis[i] = i + 1;
                }

                implied[k] = JgsMatrix.FromColumnMajor(axis, 1, length);
            }

            vectors = JgsValue.Cell(implied);
        }
        else if (count == 2 && args[0].Type == JgsType.Cell)
        {
            vectors = args[0];
        }
        else
        {
            int rank = count - 1;
            var axes = new JgsValue[rank];
            for (int k = 0; k < rank; k++)
            {
                axes[k] = AxisOf("griddedInterpolant", args[k], k, line, col);
            }

            vectors = JgsValue.Cell(axes);
        }

        // MATLAB's default outside the grid is the method itself, which is why a nearest interpolant
        // holds its edge value and a spline one carries the polynomial on.
        extrapolation ??= method;

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["GridVectors"] = vectors,
            ["Values"] = values,
            ["Method"] = JgsValue.Str(method),
            ["ExtrapolationMethod"] = JgsValue.Str(extrapolation),
        };

        JgsValue interpolant = JgsValue.Struct(fields);
        interpolant.SetClassName(GriddedClass);
        _ = host;
        return interpolant;
    }

    /// <summary>The coordinates along one direction, from either the vector of them or the whole grid.</summary>
    private static JgsValue AxisOf(string name, JgsValue given, int direction, int line, int col)
    {
        if (RankOf(given) == 1)
        {
            return given;
        }

        int[] shape = SizeDims(given);
        double[] flat = FlattenColumnMajor(name, given, line, col);
        int stride = 1;
        for (int i = 0; i < direction && i < shape.Length; i++)
        {
            stride *= shape[i];
        }

        int length = direction < shape.Length ? shape[direction] : 1;
        var picked = new double[length];
        for (int i = 0; i < length; i++)
        {
            picked[i] = flat[i * stride];
        }

        return JgsMatrix.FromColumnMajor(picked, 1, length);
    }

    private static JgsValue SampleGriddedInterpolant(
        JgsValue interpolant, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Dictionary<string, JgsValue> fields = interpolant.AsStruct;
        JgsValue[] vectorCell = fields["GridVectors"].AsCell;
        int rank = vectorCell.Length;
        var axes = new double[rank][];
        var dims = new int[rank];
        for (int k = 0; k < rank; k++)
        {
            axes[k] = FlattenColumnMajor(GriddedClass, vectorCell[k], line, col);
            dims[k] = axes[k].Length;
        }

        double[] values = FlattenColumnMajor(GriddedClass, fields["Values"], line, col);
        string method = Str(GriddedClass, [fields["Method"]], 0, line, col).ToLowerInvariant();
        string outside = Str(GriddedClass, [fields["ExtrapolationMethod"]], 0, line, col).ToLowerInvariant();

        double[][] queries;
        int[] shape;
        if (args.Count == 1 && args[0].Type == JgsType.Cell)
        {
            JgsValue[] cell = args[0].AsCell;
            if (cell.Length != rank)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:InputMixSizeErrId",
                    $"A gridded interpolant over {rank} directions is read at {rank} grid vectors.");
            }

            var asked = new double[rank][];
            for (int k = 0; k < rank; k++)
            {
                asked[k] = FlattenColumnMajor(GriddedClass, cell[k], line, col);
            }

            (queries, shape) = FullGrid(asked);
            if (rank == 1)
            {
                shape = [asked[0].Length, 1];
            }
        }
        else if (args.Count == rank)
        {
            queries = new double[rank][];
            for (int k = 0; k < rank; k++)
            {
                queries[k] = FlattenColumnMajor(GriddedClass, args[k], line, col);
            }

            shape = SizeDims(args[0]);
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:InputMixSizeErrId",
                $"A gridded interpolant over {rank} directions is read at {rank} coordinate arrays "
                + "or at one cell of grid vectors.");
        }

        int count = queries.Length == 0 ? 0 : queries[0].Length;
        var answer = new double[count];
        var point = new double[rank];

        if (rank == 1 && method is "next" or "previous" or "pchip" or "makima")
        {
            for (int q = 0; q < count; q++)
            {
                answer[q] = AlongOneAxis(axes[0], values, queries[0][q], method, outside);
            }

            return ShapedNumbers(answer, shape);
        }

        if (rank > 1 && method is "next" or "previous" or "pchip")
        {
            // MATLAB reverts these three to 'linear' beyond one direction and says so.
            method = "linear";
        }

        if (rank > 1 && method == "makima")
        {
            throw new JgsRuntimeException(line, col,
                "griddedInterpolant: 'makima' over more than one direction is not available here; its "
                + "cross terms are not the ones MATLAB uses, and answering with a different surface "
                + "would be wrong quietly (ADR 0102).");
        }

        var sampler = new GridSampler(axes, values, dims, GridMethodOf(method));
        GridSampler? outsideSampler = outside is "none" or "nearest"
            ? null
            : new GridSampler(axes, values, dims, GridMethodOf(outside == method ? method : outside));

        for (int q = 0; q < count; q++)
        {
            bool inside = true;
            for (int k = 0; k < rank; k++)
            {
                double value = queries[k][q];
                point[k] = value;
                double low = Math.Min(axes[k][0], axes[k][^1]);
                double high = Math.Max(axes[k][0], axes[k][^1]);
                inside &= value >= low && value <= high;
            }

            if (inside)
            {
                answer[q] = sampler.Sample(point, false, double.NaN);
                continue;
            }

            switch (outside)
            {
                case "none":
                    answer[q] = double.NaN;
                    break;

                case "nearest":
                    for (int k = 0; k < rank; k++)
                    {
                        double low = Math.Min(axes[k][0], axes[k][^1]);
                        double high = Math.Max(axes[k][0], axes[k][^1]);
                        point[k] = Math.Clamp(point[k], low, high);
                    }

                    answer[q] = sampler.Sample(point, false, double.NaN);
                    break;

                default:
                    answer[q] = outsideSampler!.Sample(point, true, double.NaN);
                    break;
            }
        }

        return ShapedNumbers(answer, shape);
    }

    private static GridMethod GridMethodOf(string method) => method switch
    {
        "nearest" => GridMethod.Nearest,
        "cubic" => GridMethod.Cubic,
        "spline" or "makima" or "pchip" => GridMethod.Spline,
        _ => GridMethod.Linear,
    };

    /// <summary>Every combination of one coordinate from each direction, in the array's own order.</summary>
    private static (double[][] Points, int[] Dims) FullGrid(double[][] axes)
    {
        var dims = new int[axes.Length];
        int total = 1;
        for (int k = 0; k < axes.Length; k++)
        {
            dims[k] = axes[k].Length;
            total *= dims[k];
        }

        var points = new double[axes.Length][];
        int stride = 1;
        for (int k = 0; k < axes.Length; k++)
        {
            var lane = new double[total];
            for (int i = 0; i < total; i++)
            {
                lane[i] = axes[k][(i / stride) % dims[k]];
            }

            points[k] = lane;
            stride *= dims[k];
        }

        return (points, dims);
    }

    /// <summary>
    /// The four methods that only exist along one direction: the step to the next or previous
    /// sample, and the two shape-preserving cubics whose slopes are not separable and so cannot be
    /// carried into a grid.
    /// </summary>
    private static double AlongOneAxis(
        double[] axis, double[] values, double query, string method, string outside)
    {
        double low = Math.Min(axis[0], axis[^1]);
        double high = Math.Max(axis[0], axis[^1]);
        if (query < low || query > high)
        {
            switch (outside)
            {
                case "none":
                    return double.NaN;

                case "nearest":
                    query = Math.Clamp(query, low, high);
                    break;
            }
        }

        switch (method)
        {
            case "next":
            {
                for (int i = 0; i < axis.Length; i++)
                {
                    if (axis[i] >= query)
                    {
                        return values[i];
                    }
                }

                return double.NaN;
            }

            case "previous":
            {
                for (int i = axis.Length - 1; i >= 0; i--)
                {
                    if (axis[i] <= query)
                    {
                        return values[i];
                    }
                }

                return double.NaN;
            }

            default:
            {
                double[] slopes = method == "pchip"
                    ? Interpolation.PchipSlopes(axis, values)
                    : Interpolation.MakimaSlopes(axis, values);
                int cell = 0;
                while (cell < axis.Length - 2 && query > axis[cell + 1])
                {
                    cell++;
                }

                return Interpolation.Hermite(
                    axis[cell], axis[cell + 1], values[cell], values[cell + 1],
                    slopes[cell], slopes[cell + 1], query);
            }
        }
    }
}
