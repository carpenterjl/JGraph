using System.Numerics;

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
                if (rows == 1 || cols == 1)
                {
                    var flat = new JgsValue[rows * cols];
                    Array.Fill(flat, element);
                    return JgsValue.Array(flat);
                }

                var matrix = new JgsValue[rows];
                for (int r = 0; r < rows; r++)
                {
                    var row = new JgsValue[cols];
                    Array.Fill(row, element);
                    matrix[r] = JgsValue.Array(row);
                }

                return JgsValue.Array(matrix);
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

                var result = new JgsValue[height];
                for (int r = 0; r < height; r++)
                {
                    var wrapped = new JgsValue[width];
                    for (int c = 0; c < width; c++)
                    {
                        wrapped[c] = ComplexValue(rows[r][c]);
                    }

                    result[r] = JgsValue.Array(wrapped);
                }

                return height == 1 ? result[0] : JgsValue.Array(result);
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
