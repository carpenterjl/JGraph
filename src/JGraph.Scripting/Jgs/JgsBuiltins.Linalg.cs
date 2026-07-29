using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The linear algebra builtins (M36): <c>inv</c>, <c>det</c>, <c>rank</c>, <c>trace</c>,
/// <c>norm</c>, and the decompositions <c>eig</c>, <c>lu</c>, <c>qr</c>, <c>svd</c> with their
/// MATLAB multiple-output forms. All are thin shells over
/// <see cref="JGraph.Numerics.LinearAlgebra"/>; real input only — complex matrices report a clear
/// error rather than a silently real answer.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the linear algebra builtins into <paramref name="env"/>.</summary>
    private static void RegisterLinearAlgebraBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("inv", (args, line, col) =>
        {
            Arity("inv", args, 1, line, col);
            if (HasComplexElements(args[0]))
            {
                return ComplexInverse("inv", args[0], line, col);
            }

            double[,] a = SquareRect("inv", args[0], line, col);
            LuDecomposition lu = LuDecomposition.Factor(a);
            if (lu.IsSingular)
            {
                throw new JgsRuntimeException(line, col, "inv: the matrix is singular to working precision.");
            }

            return FromRect(lu.Inverse());
        });

        Define("det", (args, line, col) =>
        {
            Arity("det", args, 1, line, col);
            if (HasComplexElements(args[0]))
            {
                return ComplexDeterminant("det", args[0], line, col);
            }

            return JgsValue.Number(LuDecomposition.Factor(SquareRect("det", args[0], line, col)).Determinant);
        });

        Define("rank", (args, line, col) =>
        {
            ArityRange("rank", args, 1, 2, line, col);
            double[,] a = RectOf("rank", args[0], line, col);
            Svd svd = Svd.Factor(a);
            if (args.Count == 2)
            {
                double tolerance = Num("rank", args, 1, line, col);
                return JgsValue.Number(svd.Values.Count(s => s > tolerance));
            }

            return JgsValue.Number(svd.Rank(a.GetLength(0), a.GetLength(1)));
        });

        Define("trace", (args, line, col) =>
        {
            Arity("trace", args, 1, line, col);
            if (args[0].Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
            {
                return args[0]; // the trace of a 1x1 is itself
            }

            // Complex-aware on purpose: eig of a general matrix hands back complex eigenvalues, and
            // trace(D) over that diagonal is the everyday check that they sum to the trace.
            Complex[,] a = ComplexSquareOf("trace", args[0], line, col);
            Complex sum = Complex.Zero;
            for (int i = 0; i < a.GetLength(0); i++)
            {
                sum += a[i, i];
            }

            return JgsValue.ComplexNum(sum);
        });

        Define("norm", (args, line, col) => Norm(args, line, col));

        Define("eig",
            (args, line, col) =>
            {
                Arity("eig", args, 1, line, col);
                if (HasComplexElements(args[0]))
                {
                    // Complex input takes the complex QR path (values only, like MATLAB's e = eig(A)).
                    Complex[] complexValues = ComplexEigen.Values(ComplexSquareOf("eig", args[0], line, col));
                    var boxed = new JgsValue[complexValues.Length];
                    for (int i = 0; i < boxed.Length; i++)
                    {
                        boxed[i] = ComplexValue(complexValues[i]);
                    }

                    JgsValue column = JgsValue.Array(boxed);
                    if (boxed.Length > 1)
                    {
                        column.Reshape(boxed.Length, 1);
                    }

                    return column;
                }

                Eigen eigen = Eigen.Factor(SquareRect("eig", args[0], line, col));
                var values = new JgsValue[eigen.Values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = ComplexValue(eigen.Values[i]);
                }

                return JgsValue.Array(values);
            },
            (args, _, line, col) =>
            {
                Arity("eig", args, 1, line, col);
                if (HasComplexElements(args[0]))
                {
                    throw new JgsRuntimeException(line, col,
                        "[V, D] = eig(A) is not supported for a complex A; e = eig(A) computes the eigenvalues.");
                }

                Eigen eigen = Eigen.Factor(SquareRect("eig", args[0], line, col));
                int n = eigen.Values.Length;
                var d = new Complex[n, n];
                for (int i = 0; i < n; i++)
                {
                    d[i, i] = eigen.Values[i];
                }

                return [FromComplexRect(eigen.Vectors), FromComplexRect(d)];
            });

        Define("lu",
            (args, line, col) =>
            {
                Arity("lu", args, 1, line, col);
                if (args[0].Type == JgsType.Sparse)
                {
                    throw new JgsRuntimeException(line, col,
                        "lu of a sparse matrix returns its factors: use [L, U] = lu(A).");
                }

                LuDecomposition lu = LuDecomposition.Factor(SquareRect("lu", args[0], line, col));

                // MATLAB's one-output lu is the two factors in one matrix: L below the (unit)
                // diagonal and U on and above it, in pivoted row order.
                double[,] permutedLower = Linear.Multiply(Linear.Transpose(lu.Permutation), lu.Lower);
                double[,] upper = lu.Upper;
                int n = upper.GetLength(0);
                var combined = new double[n, n];
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        combined[r, c] = permutedLower[r, c] + upper[r, c] - (r == c ? 1 : 0);
                    }
                }

                return FromRect(combined);
            },
            (args, wanted, line, col) =>
            {
                Arity("lu", args, 1, line, col);
                if (args[0].Type == JgsType.Sparse)
                {
                    if (wanted >= 3)
                    {
                        throw new JgsRuntimeException(line, col,
                            "lu of a sparse matrix supports the two-output form [L, U] = lu(A).");
                    }

                    // Gilbert–Peierls, permutation folded into L (the same contract as dense [L, U]).
                    try
                    {
                        (JGraph.Numerics.Sparse.CscMatrix lower, JGraph.Numerics.Sparse.CscMatrix upper) =
                            args[0].AsSparse.LowerUpper();
                        return [JgsValue.Sparse(lower), JgsValue.Sparse(upper)];
                    }
                    catch (ArgumentException ex)
                    {
                        throw new JgsRuntimeException(line, col, ex.Message);
                    }
                }

                LuDecomposition lu = LuDecomposition.Factor(SquareRect("lu", args[0], line, col));
                if (wanted >= 3)
                {
                    return [FromRect(lu.Lower), FromRect(lu.Upper), FromRect(lu.Permutation)];
                }

                // [L, U] folds the permutation into L, so L*U still reassembles A.
                return
                [
                    FromRect(Linear.Multiply(Linear.Transpose(lu.Permutation), lu.Lower)),
                    FromRect(lu.Upper),
                ];
            });

        // MATLAB's qr(A) is the full factorization — Q is m-by-m — and qr(A, 0) is the economy one.
        // The difference matters to anything that treats Q as an orthogonal basis for the whole
        // space rather than just A's range, qrupdate above all.
        static bool Economy(IReadOnlyList<JgsValue> args, string name, int line, int col)
        {
            ArityRange(name, args, 1, 2, line, col);
            return args.Count == 2 && Num(name, args, 1, line, col) == 0;
        }

        Define("qr",
            (args, line, col) =>
            {
                QrDecomposition qr = QrDecomposition.Factor(RectOf("qr", args[0], line, col));
                return FromRect(Economy(args, "qr", line, col) ? qr.R : qr.FullR);
            },
            (args, _, line, col) =>
            {
                QrDecomposition qr = QrDecomposition.Factor(RectOf("qr", args[0], line, col));
                return Economy(args, "qr", line, col)
                    ? [FromRect(qr.Q), FromRect(qr.R)]
                    : [FromRect(qr.FullQ), FromRect(qr.FullR)];
            });

        Define("svd",
            (args, line, col) =>
            {
                Arity("svd", args, 1, line, col);
                if (HasComplexElements(args[0]))
                {
                    double[] sigma = ComplexEigen.SingularValues(ComplexRectOf("svd", args[0], line, col));
                    JgsValue column = Numbers(sigma);
                    if (sigma.Length > 1)
                    {
                        column.Reshape(sigma.Length, 1);
                    }

                    return column;
                }

                return Numbers(Svd.Factor(RectOf("svd", args[0], line, col)).Values);
            },
            (args, _, line, col) =>
            {
                Arity("svd", args, 1, line, col);
                if (HasComplexElements(args[0]))
                {
                    throw new JgsRuntimeException(line, col,
                        "[U, S, V] = svd(A) is not supported for a complex A; s = svd(A) computes the singular values.");
                }

                Svd svd = Svd.Factor(RectOf("svd", args[0], line, col));
                int k = svd.Values.Length;
                var s = new double[k, k];
                for (int i = 0; i < k; i++)
                {
                    s[i, i] = svd.Values[i];
                }

                return [FromRect(svd.U), FromRect(s), FromRect(svd.V)];
            });
    }

    /// <summary>norm(x), norm(x, p), norm(A, 1|2|inf|'fro') — vector and matrix norms.</summary>
    private static JgsValue Norm(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("norm", args, 1, 2, line, col);
        bool isMatrix = IsMatrixValue(args[0]);
        string? word = args.Count == 2 && args[1].Type == JgsType.String ? args[1].AsString.ToLowerInvariant() : null;
        double p = args.Count == 2 && args[1].Type != JgsType.String ? Num("norm", args, 1, line, col) : 2;
        if (word == "inf")
        {
            p = double.PositiveInfinity;
        }
        else if (word is not null && word != "fro")
        {
            throw new JgsRuntimeException(line, col, $"norm does not recognize the option '{args[1].AsString}'.");
        }

        if (!isMatrix)
        {
            double[] v = ToDoubles("norm", args[0], line, col);
            if (word == "fro" || p == 2)
            {
                double sumSquares = 0;
                foreach (double x in v)
                {
                    sumSquares += x * x;
                }

                return JgsValue.Number(System.Math.Sqrt(sumSquares));
            }

            if (double.IsPositiveInfinity(p))
            {
                return JgsValue.Number(v.Length == 0 ? 0 : v.Max(System.Math.Abs));
            }

            if (double.IsNegativeInfinity(p))
            {
                return JgsValue.Number(v.Length == 0 ? 0 : v.Min(System.Math.Abs));
            }

            double sum = 0;
            foreach (double x in v)
            {
                sum += System.Math.Pow(System.Math.Abs(x), p);
            }

            return JgsValue.Number(System.Math.Pow(sum, 1 / p));
        }

        double[,] a = RectOf("norm", args[0], line, col);
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        if (word == "fro")
        {
            double sumSquares = 0;
            foreach (double x in a)
            {
                sumSquares += x * x;
            }

            return JgsValue.Number(System.Math.Sqrt(sumSquares));
        }

        if (p == 1)
        {
            double best = 0;
            for (int c = 0; c < cols; c++)
            {
                double sum = 0;
                for (int r = 0; r < rows; r++)
                {
                    sum += System.Math.Abs(a[r, c]);
                }

                best = System.Math.Max(best, sum);
            }

            return JgsValue.Number(best);
        }

        if (double.IsPositiveInfinity(p))
        {
            double best = 0;
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                for (int c = 0; c < cols; c++)
                {
                    sum += System.Math.Abs(a[r, c]);
                }

                best = System.Math.Max(best, sum);
            }

            return JgsValue.Number(best);
        }

        if (p == 2)
        {
            double[] sigma = Svd.Factor(a).Values;
            return JgsValue.Number(sigma.Length == 0 ? 0 : sigma[0]);
        }

        throw new JgsRuntimeException(line, col, "Matrix norms support p = 1, 2, inf, or 'fro'.");
    }

    /// <summary>A scalar, vector, or matrix value as a rectangular double matrix (vectors are rows).</summary>
    private static double[,] RectOf(string name, JgsValue value, int line, int col)
    {
        double[][] rows = AsJaggedRows(name, value, line, col);
        var rect = new double[rows.Length, rows[0].Length];
        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r].Length != rect.GetLength(1))
            {
                throw new JgsRuntimeException(line, col, $"{name}: matrix rows must have equal lengths.");
            }

            for (int c = 0; c < rows[r].Length; c++)
            {
                rect[r, c] = rows[r][c];
            }
        }

        return rect;
    }

    private static double[,] SquareRect(string name, JgsValue value, int line, int col)
    {
        double[,] rect = RectOf(name, value, line, col);
        if (rect.GetLength(0) != rect.GetLength(1))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a square matrix, but got {rect.GetLength(0)}x{rect.GetLength(1)}.");
        }

        return rect;
    }

    /// <summary>A rectangular result as a script value; one row or column collapses to a vector.</summary>
    private static JgsValue FromRect(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        if (rows == 1 && cols == 1)
        {
            return JgsValue.Number(matrix[0, 0]);
        }

        if (rows == 1 || cols == 1)
        {
            var flat = new double[rows * cols];
            int at = 0;
            foreach (double v in matrix)
            {
                flat[at++] = v;
            }

            // A single column keeps its orientation — linsolve(A, b) must agree in shape with
            // A \ b, or subtracting the two becomes an outer difference under implicit expansion.
            JgsValue vector = Numbers(flat);
            if (cols == 1 && rows > 1)
            {
                vector.Reshape(rows, 1);
            }

            return vector;
        }

        var jagged = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            jagged[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                jagged[r][c] = matrix[r, c];
            }
        }

        return MatrixFromRows(jagged);
    }

    /// <summary>A complex matrix as a script value; all-real entries stay plain numbers.</summary>
    private static JgsValue FromComplexRect(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        if (rows == 1 && cols == 1)
        {
            return ComplexValue(matrix[0, 0]);
        }

        return JgsMatrix.BuildValues(rows, cols, (r, c) => ComplexValue(matrix[r, c]));
    }

    private static JgsValue ComplexValue(Complex value) =>
        value.Imaginary == 0 ? JgsValue.Number(value.Real) : JgsValue.ComplexNum(value);
}
