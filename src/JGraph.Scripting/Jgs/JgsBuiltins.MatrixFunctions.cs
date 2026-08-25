using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The matrix-function builtins the stress tests asked for (M42): <c>hilb</c>, <c>polyval</c>,
/// <c>peaks</c>, <c>cond</c>, <c>sqrtm</c>, <c>logm</c>, and the complex LU behind complex
/// <c>det</c>/<c>inv</c>. The matrix square root is Denman–Beavers (a coupled Newton iteration —
/// quadratic convergence, no Schur reordering needed) and the logarithm is inverse scaling and
/// squaring on top of it: take square roots until the matrix is near identity, sum the Mercator
/// series there, and double back up.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the matrix-function builtins into <paramref name="env"/>.</summary>
    private static void RegisterMatrixFunctionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("hilb", (args, line, col) =>
        {
            Arity("hilb", args, 1, line, col);
            int n = Count("hilb", args, 0, line, col);
            return JgsMatrix.Build(n, n, static (r, c) => 1.0 / (r + c + 1));
        });

        Define("polyval", (args, line, col) =>
        {
            Arity("polyval", args, 2, line, col);
            double[] coefficients = ToDoubles("polyval", args[0], line, col);
            return MapNumeric("polyval", args[1], x =>
            {
                double y = 0;
                foreach (double p in coefficients)
                {
                    y = (y * x) + p;
                }

                return y;
            }, line, col);
        });

        JgsValue[] PeaksGrids(IReadOnlyList<JgsValue> args, int line, int col)
        {
            ArityRange("peaks", args, 0, 1, line, col);
            int n = args.Count == 1 ? Count("peaks", args, 0, line, col) : 49;
            if (n < 2)
            {
                throw new JgsRuntimeException(line, col, "peaks needs a grid of at least 2 points.");
            }

            double At(int i) => -3.0 + (6.0 * i / (n - 1));
            static double Z(double x, double y) =>
                (3 * (1 - x) * (1 - x) * System.Math.Exp(-(x * x) - ((y + 1) * (y + 1))))
                - (10 * ((x / 5) - (x * x * x) - (y * y * y * y * y)) * System.Math.Exp(-(x * x) - (y * y)))
                - (System.Math.Exp(-((x + 1) * (x + 1)) - (y * y)) / 3);

            return
            [
                JgsMatrix.Build(n, n, (r, c) => At(c)),
                JgsMatrix.Build(n, n, (r, c) => At(r)),
                JgsMatrix.Build(n, n, (r, c) => Z(At(c), At(r))),
            ];
        }

        Define("peaks",
            (args, line, col) => PeaksGrids(args, line, col)[2],
            (args, wanted, line, col) =>
            {
                JgsValue[] grids = PeaksGrids(args, line, col);
                return wanted >= 3 ? grids : wanted == 2 ? grids[..2] : [grids[2]];
            });

        Define("ode45",
            (args, line, col) => throw new JgsRuntimeException(line, col,
                "ode45 produces two outputs: use [t, y] = ode45(f, tspan, y0)."),
            (args, _, line, col) =>
            {
                Arity("ode45", args, 3, line, col);
                if (args[0].Type != JgsType.Function)
                {
                    throw new JgsRuntimeException(line, col, "ode45 expects a function handle f(t, y).");
                }

                IJgsCallable f = args[0].AsCallable;
                double[] tspan = ToDoubles("ode45", args[1], line, col);
                double[] initial = ToDoubles("ode45", args[2], line, col);
                int states = initial.Length;

                double[] Derivative(double t, double[] y)
                {
                    JgsValue yColumn = JgsMatrix.FromColumnMajorDims((double[])y.Clone(), [states, 1]);
                    JgsValue slope = f.Call([JgsValue.Number(t), yColumn], line, col);
                    double[] dy = ToDoubles("ode45", slope, line, col);
                    if (dy.Length != states)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"ode45: f returned {dy.Length} value(s) for {states} state(s).");
                    }

                    return dy;
                }

                List<JGraph.Numerics.OdeSolvers.OdePoint> points;
                try
                {
                    points = JGraph.Numerics.OdeSolvers.DormandPrince(Derivative, tspan, initial);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    throw new JgsRuntimeException(line, col, $"ode45: {ex.Message}");
                }

                JgsValue times = JgsMatrix.Build(points.Count, 1, (r, _) => points[r].Time);
                JgsValue trajectory = JgsMatrix.Build(points.Count, states, (r, c) => points[r].State[c]);
                return [times, trajectory];
            });

        Define("cond", (args, line, col) =>
        {
            ArityRange("cond", args, 1, 2, line, col);
            double[] a = SquareColumnMajorOf("cond", args[0], out int n, line, col);
            string p = args.Count == 2
                ? args[1].Type == JgsType.String ? args[1].AsString : NormName(Num("cond", args, 1, line, col))
                : "2";

            if (p == "2")
            {
                // Only the values, never a singular vector: the 2-norm condition number is the ratio
                // of the largest to the smallest, and asking for the factors would be most of the work
                // for none of the answer.
                double[] sigma = Svd.SingularValues(a, n, n);
                if (sigma.Length == 0)
                {
                    return JgsValue.Number(double.PositiveInfinity);
                }

                double largest = sigma.Max();
                double smallest = sigma.Min();
                return JgsValue.Number(smallest == 0 ? double.PositiveInfinity : largest / smallest);
            }

            // Measured before the factorization, which overwrites the matrix it is handed. All three
            // are O(n²) against its O(n³), so taking them all costs less than deciding not to.
            double one = DenseLinalg.OneNorm(n, n, a, n);
            double infinity = RowSumNorm(a, n);
            double frobenius = EuclideanNorm(a);

            LuDecomposition lu = LuDecomposition.FactorAdopting(a, n);
            if (lu.IsSingular)
            {
                return JgsValue.Number(double.PositiveInfinity);
            }

            double[] inverse = lu.InverseColumnMajor();
            return JgsValue.Number(p switch
            {
                "1" => one * DenseLinalg.OneNorm(n, n, inverse, n),
                "Inf" or "inf" => infinity * RowSumNorm(inverse, n),
                "fro" => frobenius * EuclideanNorm(inverse),
                _ => throw new JgsRuntimeException(line, col, "cond supports p = 1, 2, Inf, or 'fro'."),
            });
        });

        Define("sqrtm", (args, line, col) =>
        {
            Arity("sqrtm", args, 1, line, col);
            double[,] a = SquareRect("sqrtm", args[0], line, col);
            return FromRect(DenmanBeaversSqrt(a, line, col));
        });

        Define("logm", (args, line, col) =>
        {
            Arity("logm", args, 1, line, col);
            double[,] b = SquareRect("logm", args[0], line, col);
            int n = b.GetLength(0);

            // Inverse scaling and squaring: square-root down toward the identity, take the Mercator
            // series there, and scale the answer back up by the number of roots taken.
            int roots = 0;
            while (OneNorm(MatrixMinusIdentity(b)) > 0.25 && roots < 40)
            {
                b = DenmanBeaversSqrt(b, line, col);
                roots++;
            }

            double[,] x = MatrixMinusIdentity(b);
            var sum = new double[n, n];
            double[,] power = (double[,])x.Clone();
            for (int m = 1; m <= 32; m++)
            {
                double sign = m % 2 == 1 ? 1.0 : -1.0;
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        sum[r, c] += sign * power[r, c] / m;
                    }
                }

                power = MatMul(power, x);
            }

            double scale = System.Math.Pow(2, roots);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    sum[r, c] *= scale;
                }
            }

            return FromRect(sum);
        });
    }

    /// <summary>
    /// The Denman–Beavers iteration for the principal matrix square root: Y ← (Y + Z⁻¹)/2,
    /// Z ← (Z + Y⁻¹)/2 with Y₀ = A, Z₀ = I; Y converges quadratically to √A (and Z to its inverse)
    /// for matrices with no eigenvalues on the closed negative real axis.
    /// </summary>
    private static double[,] DenmanBeaversSqrt(double[,] a, int line, int col)
    {
        int n = a.GetLength(0);
        double[,] y = (double[,])a.Clone();
        double[,] z = MatrixIdentity(n);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            LuDecomposition luZ = LuDecomposition.Factor(z);
            LuDecomposition luY = LuDecomposition.Factor(y);
            if (luZ.IsSingular || luY.IsSingular)
            {
                throw new JgsRuntimeException(line, col,
                    "sqrtm: the iteration hit a singular intermediate — the matrix has no principal square root.");
            }

            double[,] zInverse = luZ.Inverse();
            double[,] yInverse = luY.Inverse();
            var nextY = new double[n, n];
            var nextZ = new double[n, n];
            double drift = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    nextY[r, c] = 0.5 * (y[r, c] + zInverse[r, c]);
                    nextZ[r, c] = 0.5 * (z[r, c] + yInverse[r, c]);
                    drift = System.Math.Max(drift, System.Math.Abs(nextY[r, c] - y[r, c]));
                }
            }

            y = nextY;
            z = nextZ;
            if (drift <= 1e-14 * (1 + OneNorm(y)))
            {
                break;
            }
        }

        return y;
    }

    private static double[,] MatrixIdentity(int n)
    {
        var identity = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = 1;
        }

        return identity;
    }

    private static double[,] MatrixMinusIdentity(double[,] a)
    {
        int n = a.GetLength(0);
        var result = (double[,])a.Clone();
        for (int i = 0; i < n; i++)
        {
            result[i, i] -= 1;
        }

        return result;
    }

    private static double[,] MatMul(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int inner = a.GetLength(1);
        int m = b.GetLength(1);
        var product = new double[n, m];
        for (int r = 0; r < n; r++)
        {
            for (int k = 0; k < inner; k++)
            {
                double left = a[r, k];
                if (left == 0)
                {
                    continue;
                }

                for (int c = 0; c < m; c++)
                {
                    product[r, c] += left * b[k, c];
                }
            }
        }

        return product;
    }

    // --- Complex square matrices (det/inv/trace) -----------------------------------------------

    /// <summary>
    /// Complex multiplication with C99 Annex G infinity recovery: a naive IEEE product of an
    /// infinite complex and a finite nonzero one produces NaN components (Inf·0 terms), where the
    /// standard — and MATLAB — say the result is infinite. The recovery reruns the products over
    /// direction vectors and scales by infinity.
    /// </summary>
    internal static Complex MultiplyC99(Complex x, Complex y)
    {
        Complex naive = x * y;
        if (!double.IsNaN(naive.Real) || !double.IsNaN(naive.Imaginary))
        {
            return naive;
        }

        static bool Infinite(Complex v) => double.IsInfinity(v.Real) || double.IsInfinity(v.Imaginary);
        if (!Infinite(x) && !Infinite(y))
        {
            return naive; // genuine NaNs stay NaNs
        }

        double a = x.Real, b = x.Imaginary, c = y.Real, d = y.Imaginary;
        if (Infinite(x))
        {
            a = System.Math.CopySign(double.IsInfinity(a) ? 1 : 0, a);
            b = System.Math.CopySign(double.IsInfinity(b) ? 1 : 0, b);
            if (double.IsNaN(c)) { c = System.Math.CopySign(0, c); }
            if (double.IsNaN(d)) { d = System.Math.CopySign(0, d); }
        }

        if (Infinite(y))
        {
            c = System.Math.CopySign(double.IsInfinity(c) ? 1 : 0, c);
            d = System.Math.CopySign(double.IsInfinity(d) ? 1 : 0, d);
            if (double.IsNaN(a)) { a = System.Math.CopySign(0, a); }
            if (double.IsNaN(b)) { b = System.Math.CopySign(0, b); }
        }

        return new Complex(
            double.PositiveInfinity * ((a * c) - (b * d)),
            double.PositiveInfinity * ((a * d) + (b * c)));
    }

    /// <summary>
    /// The matrix product when either operand holds complex elements — a boxed gather multiply
    /// with the same single-flip vector leniency the real path applies.
    /// </summary>
    internal static JgsValue ComplexMatrixProduct(JgsValue left, JgsValue right, int line, int col)
    {
        Complex[,] a = ComplexRectOf("'*'", left, line, col);
        Complex[,] b = ComplexRectOf("'*'", right, line, col);
        static Complex[,] Flip(Complex[,] m)
        {
            var t = new Complex[m.GetLength(1), m.GetLength(0)];
            for (int r = 0; r < m.GetLength(0); r++)
            {
                for (int c = 0; c < m.GetLength(1); c++)
                {
                    t[c, r] = m[r, c];
                }
            }

            return t;
        }

        if (a.GetLength(1) != b.GetLength(0))
        {
            bool rightIsVector = b.GetLength(0) == 1 || b.GetLength(1) == 1;
            bool leftIsVector = a.GetLength(0) == 1 || a.GetLength(1) == 1;
            if (rightIsVector)
            {
                b = Flip(b);
            }
            else if (leftIsVector)
            {
                a = Flip(a);
            }
        }

        if (a.GetLength(1) != b.GetLength(0))
        {
            throw new JgsRuntimeException(line, col,
                $"'*' needs inner dimensions to agree: the left has {a.GetLength(1)} columns and the right has {b.GetLength(0)} rows.");
        }

        int rows = a.GetLength(0);
        int cols = b.GetLength(1);
        int inner = a.GetLength(1);
        var elements = new JgsValue[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < inner; k++)
                {
                    sum += a[r, k] * b[k, c];
                }

                elements[(c * rows) + r] = JgsValue.ComplexNum(sum);
            }
        }

        return rows == 1 && cols == 1 ? elements[0] : JgsValue.Shaped(elements, rows, cols);
    }

    /// <summary>A rectangular complex matrix read through <see cref="JgsMatrix"/> (numbers read as re+0i).</summary>
    private static Complex[,] ComplexRectOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return new Complex[1, 1] { { new Complex(value.AsNumber, 0) } };
        }

        if (value.Type == JgsType.Complex)
        {
            return new Complex[1, 1] { { value.AsComplex } };
        }

        int rows = JgsMatrix.RowCount(value);
        int cols = JgsMatrix.ColCount(value);
        var a = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                JgsValue element = JgsMatrix.At(value, r, c);
                a[r, c] = element.Type switch
                {
                    JgsType.Number or JgsType.Bool => new Complex(element.AsNumber, 0),
                    JgsType.Complex => element.AsComplex,
                    _ => throw new JgsRuntimeException(line, col,
                        $"{name} needs numbers, but element ({r}, {c}) was a {element.TypeName}."),
                };
            }
        }

        return a;
    }

    /// <summary>Whether any element of an array value is complex (a packed complex array always is).</summary>
    internal static bool HasComplexElements(JgsValue value)
    {
        if (value.Type == JgsType.Complex)
        {
            return true;
        }

        if (value.Type != JgsType.Array)
        {
            return false;
        }

        if (value.IsPackedComplex)
        {
            return true;
        }

        if (value.IsPacked)
        {
            return false;
        }

        foreach (JgsValue element in value.BoxedElements())
        {
            if (element.Type == JgsType.Complex || (element.Type == JgsType.Array && HasComplexElements(element)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A square complex matrix read through <see cref="JgsMatrix"/> (numbers read as re+0i).</summary>
    private static Complex[,] ComplexSquareOf(string name, JgsValue value, int line, int col)
    {
        int rows = JgsMatrix.RowCount(value);
        int cols = JgsMatrix.ColCount(value);
        if (value.Type != JgsType.Array || rows != cols)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a square matrix, but got {rows}x{cols}.");
        }

        var a = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                JgsValue element = JgsMatrix.At(value, r, c);
                a[r, c] = element.Type switch
                {
                    JgsType.Number or JgsType.Bool => new Complex(element.AsNumber, 0),
                    JgsType.Complex => element.AsComplex,
                    _ => throw new JgsRuntimeException(line, col,
                        $"{name} needs numbers, but element ({r}, {c}) was a {element.TypeName}."),
                };
            }
        }

        return a;
    }

    /// <summary>Complex LU with partial pivoting by magnitude, in place over a copy.</summary>
    private static (Complex[,] Factors, int[] Pivots, int Sign, bool Singular) ComplexLuFactor(Complex[,] source)
    {
        int n = source.GetLength(0);
        var lu = (Complex[,])source.Clone();
        var pivots = new int[n];
        int sign = 1;
        bool singular = false;
        for (int k = 0; k < n; k++)
        {
            int best = k;
            double bestMagnitude = lu[k, k].Magnitude;
            for (int r = k + 1; r < n; r++)
            {
                if (lu[r, k].Magnitude > bestMagnitude)
                {
                    best = r;
                    bestMagnitude = lu[r, k].Magnitude;
                }
            }

            pivots[k] = best;
            if (best != k)
            {
                for (int c = 0; c < n; c++)
                {
                    (lu[k, c], lu[best, c]) = (lu[best, c], lu[k, c]);
                }

                sign = -sign;
            }

            if (lu[k, k] == Complex.Zero)
            {
                singular = true;
                continue;
            }

            for (int r = k + 1; r < n; r++)
            {
                Complex factor = lu[r, k] / lu[k, k];
                lu[r, k] = factor;
                for (int c = k + 1; c < n; c++)
                {
                    lu[r, c] -= factor * lu[k, c];
                }
            }
        }

        return (lu, pivots, sign, singular);
    }

    /// <summary>det of a complex square matrix: the pivot product, signed by the row swaps.</summary>
    private static JgsValue ComplexDeterminant(string name, JgsValue value, int line, int col)
    {
        Complex[,] a = ComplexSquareOf(name, value, line, col);
        (Complex[,] lu, _, int sign, bool singular) = ComplexLuFactor(a);
        if (singular)
        {
            return JgsValue.Number(0);
        }

        Complex determinant = sign;
        for (int i = 0; i < a.GetLength(0); i++)
        {
            determinant *= lu[i, i];
        }

        return JgsValue.ComplexNum(determinant);
    }

    /// <summary>inv of a complex square matrix: forward/back substitution against the identity.</summary>
    private static JgsValue ComplexInverse(string name, JgsValue value, int line, int col)
    {
        Complex[,] a = ComplexSquareOf(name, value, line, col);
        (Complex[,] lu, int[] pivots, _, bool singular) = ComplexLuFactor(a);
        if (singular)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the matrix is singular to working precision.");
        }

        int n = a.GetLength(0);
        var elements = new JgsValue[n * n];
        var column = new Complex[n];
        for (int rhs = 0; rhs < n; rhs++)
        {
            for (int i = 0; i < n; i++)
            {
                column[i] = i == rhs ? Complex.One : Complex.Zero;
            }

            for (int i = 0; i < n; i++)
            {
                if (pivots[i] != i)
                {
                    (column[i], column[pivots[i]]) = (column[pivots[i]], column[i]);
                }

                for (int k = 0; k < i; k++)
                {
                    column[i] -= lu[i, k] * column[k];
                }
            }

            for (int i = n - 1; i >= 0; i--)
            {
                for (int k = i + 1; k < n; k++)
                {
                    column[i] -= lu[i, k] * column[k];
                }

                column[i] /= lu[i, i];
            }

            for (int i = 0; i < n; i++)
            {
                elements[(rhs * n) + i] = JgsValue.ComplexNum(column[i]);
            }
        }

        return n == 1 ? elements[0] : JgsValue.Shaped(elements, n, n);
    }

    /// <summary>
    /// The name <c>cond</c> knows a numeric <c>p</c> by. Infinity needs spelling out: its round-trip
    /// text is "Infinity", which matches none of the words, so <c>cond(A, inf)</c> used to be
    /// refused while <c>cond(A, 'inf')</c> and <c>norm(A, inf)</c> were both accepted.
    /// </summary>
    private static string NormName(double p) =>
        double.IsPositiveInfinity(p) ? "inf" : p.ToString("R");

    /// <summary>The ∞-norm of an n-by-n column-major matrix: the largest absolute row sum.</summary>
    private static double RowSumNorm(ReadOnlySpan<double> a, int n)
    {
        double best = 0;
        for (int r = 0; r < n; r++)
        {
            double sum = 0;
            for (int c = 0; c < n; c++)
            {
                sum += Math.Abs(a[(c * n) + r]);
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    /// <summary>The Frobenius norm — the layout does not matter, only that every entry is counted.</summary>
    private static double EuclideanNorm(ReadOnlySpan<double> a)
    {
        double sum = 0;
        foreach (double x in a)
        {
            sum += x * x;
        }

        return Math.Sqrt(sum);
    }
}
