using System.Numerics;
using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using JGraph.Maths.Geometry;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The last of the M38 coverage: the logical-array constructors <c>true</c>/<c>false</c>, the
/// two-dimensional transforms, and the two computational-geometry primitives that plotting already
/// has the machinery for.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the constructors, 2-D transforms, and geometry builtins (M38).</summary>
    private static void RegisterGeometryBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        RegisterLogicalConstructors(Define);
        RegisterTwoDimensionalTransforms(Define);
        RegisterHulls(Define);
        RegisterTriangulation(Define);
        RegisterContourMatrix(Define);
    }

    // --- Triangulation ----------------------------------------------------------------------------

    private static void RegisterTriangulation(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("delaunay", (args, line, col) =>
        {
            ArityRange("delaunay", args, 1, 2, line, col);

            double[] xs;
            double[] ys;

            if (args.Count == 1)
            {
                // delaunay(P) takes the points as an n-by-2 matrix.
                double[,] points = RectOf("delaunay", args[0], line, col);
                if (points.GetLength(1) != 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "delaunay: a single argument must be an n-by-2 matrix of points.");
                }

                int n = points.GetLength(0);
                xs = new double[n];
                ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    xs[i] = points[i, 0];
                    ys[i] = points[i, 1];
                }
            }
            else
            {
                xs = ToDoubles("delaunay", args[0], line, col);
                ys = ToDoubles("delaunay", args[1], line, col);
            }

            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col, "delaunay needs the same number of x and y coordinates.");
            }

            if (xs.Length < 3)
            {
                throw new JgsRuntimeException(line, col, "delaunay needs at least 3 points.");
            }

            int[,] triangles = Delaunay.Triangulate(xs, ys);

            // The kernel counts from zero; MATLAB's connectivity list counts from one, and this is a
            // list of vertex numbers rather than an index into anything JGraph subscripts.
            return JgsMatrix.Build(triangles.GetLength(0), 3, (t, v) => triangles[t, v] + 1.0);
        });
    }

    // --- Contour matrix ---------------------------------------------------------------------------

    private static void RegisterContourMatrix(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("contourc", (args, line, col) =>
        {
            ArityRange("contourc", args, 1, 4, line, col);

            // contourc(Z, ...) and contourc(x, y, Z, ...) differ by where the matrix sits.
            bool gridded = args.Count >= 3;
            double[,] z = RectOf("contourc", args[gridded ? 2 : 0], line, col);
            int rows = z.GetLength(0);
            int columns = z.GetLength(1);

            double[] x = gridded ? ToDoubles("contourc", args[0], line, col) : Sequence(columns);
            double[] y = gridded ? ToDoubles("contourc", args[1], line, col) : Sequence(rows);

            if (x.Length != columns || y.Length != rows)
            {
                throw new JgsRuntimeException(line, col,
                    $"contourc: the grid is {y.Length}x{x.Length} but the matrix is {rows}x{columns}.");
            }

            // The level argument reads two ways: a vector lists the levels outright, and a lone
            // number asks for that many chosen automatically. MATLAB gives the same argument both
            // readings, and a caller wanting a single named level writes it twice.
            int levelIndex = gridded ? 3 : 1;
            double[] levels = AutomaticLevels(z, 10);
            if (levelIndex < args.Count)
            {
                levels = args[levelIndex].Type is JgsType.Number or JgsType.Bool
                    ? AutomaticLevels(z, Count("contourc", args, levelIndex, line, col))
                    : ToDoubles("contourc", args[levelIndex], line, col);
            }

            return ContourMatrix(x, y, z, levels);
        });
    }

    /// <summary>1, 2, … n — the implied grid coordinates when contourc is given only a matrix.</summary>
    private static double[] Sequence(int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    /// <summary>
    /// Evenly spaced levels strictly inside the data's range. The extremes are left out because a
    /// level exactly at the minimum or maximum touches the surface without crossing it, so it
    /// produces no line — the same rule JGraph's own contour plot uses, so the two agree.
    /// </summary>
    private static double[] AutomaticLevels(double[,] z, int count)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double v in z)
        {
            if (double.IsFinite(v))
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        if (!double.IsFinite(min) || !double.IsFinite(max) || min == max)
        {
            return [];
        }

        var levels = new double[count];
        for (int i = 0; i < count; i++)
        {
            levels[i] = min + ((max - min) * (i + 1) / (count + 1.0));
        }

        return levels;
    }

    /// <summary>
    /// MATLAB's contour matrix: a 2-row array in which each line is introduced by a column holding
    /// its level and its point count, followed by that many columns of coordinates. It is an odd
    /// shape for a return value, but it is the one <c>contourc</c> is defined to produce and the one
    /// any code that consumes a contour matrix expects to walk.
    /// </summary>
    private static JgsValue ContourMatrix(double[] x, double[] y, double[,] z, double[] levels)
    {
        var top = new List<double>();
        var bottom = new List<double>();

        double span = Math.Max(
            Math.Max(x[^1] - x[0], y[^1] - y[0]),
            1e-12);

        foreach (double level in levels)
        {
            foreach (Point2D[] path in ContourPaths.Assemble(MarchingSquares.Lines(x, y, z, level), span * 1e-10))
            {
                if (path.Length < 2)
                {
                    continue;
                }

                top.Add(level);
                bottom.Add(path.Length);
                foreach (Point2D point in path)
                {
                    top.Add(point.X);
                    bottom.Add(point.Y);
                }
            }
        }

        return JgsValue.Array([Numbers([.. top]), Numbers([.. bottom])]);
    }

    // --- Logical array constructors ---------------------------------------------------------------

    private static void RegisterLogicalConstructors(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // true and false are lexer keywords, so a bare mention is still the literal; the parser only
        // routes here when the word is followed by '(' — see ParsePrimary.
        void Constructor(string name, bool value) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 0, 2, line, col);
                if (args.Count == 0)
                {
                    return JgsValue.Bool(value);
                }

                int rows;
                int cols;
                if (args.Count == 1 && args[0].Type == JgsType.Array)
                {
                    double[] shape = ToDoubles(name, args[0], line, col);
                    rows = (int)shape[0];
                    cols = shape.Length > 1 ? (int)shape[1] : rows;
                }
                else
                {
                    rows = Count(name, args, 0, line, col);
                    cols = args.Count == 2 ? Count(name, args, 1, line, col) : rows;
                }

                if (rows < 0 || cols < 0)
                {
                    throw new JgsRuntimeException(line, col, $"{name}: a size cannot be negative.");
                }

                JgsValue element = JgsValue.Bool(value);
                return JgsMatrix.BuildValues(rows, cols, (_, _) => element);
            });

        Constructor("true", true);
        Constructor("false", false);
    }

    // --- Two-dimensional transforms ---------------------------------------------------------------

    private static void RegisterTwoDimensionalTransforms(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // A separable transform: run the 1-D transform along every row, then along every column.
        // fftn is the same thing — JGraph's arrays have at most the two dimensions a matrix has.
        void Transform2D(string name, bool inverse) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                Complex[][] rows = ComplexRows(name, args[0], line, col);
                int height = rows.Length;
                int width = rows[0].Length;

                for (int r = 0; r < height; r++)
                {
                    rows[r] = inverse ? JGraph.Signal.Fft.Inverse(rows[r]) : JGraph.Signal.Fft.Forward(rows[r]);
                }

                var column = new Complex[height];
                for (int c = 0; c < width; c++)
                {
                    for (int r = 0; r < height; r++)
                    {
                        column[r] = rows[r][c];
                    }

                    Complex[] transformed = inverse
                        ? JGraph.Signal.Fft.Inverse(column)
                        : JGraph.Signal.Fft.Forward(column);
                    for (int r = 0; r < height; r++)
                    {
                        rows[r][c] = transformed[r];
                    }
                }

                return JgsMatrix.BuildValues(height, width, (r, c) => ComplexValue(rows[r][c]));
            });

        Transform2D("fft2", inverse: false);
        Transform2D("ifft2", inverse: true);
        Transform2D("fftn", inverse: false);
        Transform2D("ifftn", inverse: true);
    }

    /// <summary>A matrix (or a vector, as one row) as jagged complex rows the transform can work on.</summary>
    private static Complex[][] ComplexRows(string name, JgsValue value, int line, int col)
    {
        if (!IsMatrixValue(value))
        {
            double[] flat = value.Type is JgsType.Number or JgsType.Bool
                ? [value.AsNumber]
                : ToDoubles(name, value, line, col);
            var single = new Complex[flat.Length];
            for (int i = 0; i < flat.Length; i++)
            {
                single[i] = new Complex(flat[i], 0);
            }

            return [single];
        }

        double[,] rect = RectOf(name, value, line, col);
        var rows = new Complex[rect.GetLength(0)][];
        for (int r = 0; r < rows.Length; r++)
        {
            rows[r] = new Complex[rect.GetLength(1)];
            for (int c = 0; c < rows[r].Length; c++)
            {
                rows[r][c] = new Complex(rect[r, c], 0);
            }
        }

        return rows;
    }

    // --- Convex hull ------------------------------------------------------------------------------

    private static void RegisterHulls(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        Define("convhull", (args, line, col) =>
        {
            Arity("convhull", args, 2, line, col);
            double[] xs = ToDoubles("convhull", args[0], line, col);
            double[] ys = ToDoubles("convhull", args[1], line, col);
            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col, "convhull needs the same number of x and y coordinates.");
            }

            if (xs.Length < 3)
            {
                throw new JgsRuntimeException(line, col, "convhull needs at least 3 points.");
            }

            return Numbers(ConvexHull(xs, ys));
        });
    }

    /// <summary>
    /// The indices of the convex hull, counter-clockwise and closed (the first index repeats at the
    /// end) as MATLAB returns them, by Andrew's monotone chain: sort by x, sweep the lower boundary
    /// and then the upper, discarding any point that makes a clockwise turn.
    /// </summary>
    private static double[] ConvexHull(double[] xs, double[] ys)
    {
        int n = xs.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => xs[a] != xs[b] ? xs[a].CompareTo(xs[b]) : ys[a].CompareTo(ys[b]));

        double Cross(int o, int a, int b) =>
            ((xs[a] - xs[o]) * (ys[b] - ys[o])) - ((ys[a] - ys[o]) * (xs[b] - xs[o]));

        var hull = new List<int>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach (int point in pass == 0 ? order : order.Reverse())
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], point) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1); // the pass's last point starts the next one
        }

        var indices = new double[hull.Count + 1];
        for (int i = 0; i < hull.Count; i++)
        {
            indices[i] = hull[i] + 1; // MATLAB indexes the input points from 1
        }

        indices[^1] = indices[0]; // closing the polygon is part of the answer
        return indices;
    }
}
