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

            double[] a = SquareColumnMajorOf("inv", args[0], out int n, line, col);
            LuDecomposition lu = LuDecomposition.FactorAdopting(a, n);
            if (lu.IsSingular)
            {
                throw new JgsRuntimeException(line, col, "inv: the matrix is singular to working precision.");
            }

            return FromColumnMajorRect(lu.InverseColumnMajor(), n, n);
        });

        Define("det", (args, line, col) =>
        {
            Arity("det", args, 1, line, col);
            if (HasComplexElements(args[0]))
            {
                return ComplexDeterminant("det", args[0], line, col);
            }

            double[] a = SquareColumnMajorOf("det", args[0], out int n, line, col);
            return JgsValue.Number(LuDecomposition.FactorAdopting(a, n).Determinant);
        });

        Define("rank", (args, line, col) =>
        {
            ArityRange("rank", args, 1, 2, line, col);
            double[] a = ColumnMajorOf("rank", args[0], out int rows, out int cols, line, col);
            double[] sigma = Svd.SingularValues(a, rows, cols);
            if (args.Count == 2)
            {
                double tolerance = Num("rank", args, 1, line, col);
                return JgsValue.Number(sigma.Count(s => s > tolerance));
            }

            return JgsValue.Number(Svd.RankOf(sigma, rows, cols));
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
            (args, line, col) => EigenAnswer(args, 1, line, col)[0],
            EigenAnswer);

        Define("lu",
            (args, line, col) => LowerUpperAnswer(args, 1, line, col)[0],
            LowerUpperAnswer);

        Define("qr",
            (args, line, col) => QrAnswer(args, 1, line, col)[0],
            QrAnswer);

        Define("svd", SingularValueList, SingularValueFactors);
    }

    /// <summary>s = svd(A) — the singular values, as MATLAB's column.</summary>
    private static JgsValue SingularValueList(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("svd", args, 1, 2, line, col);
        if (HasComplexElements(args[0]))
        {
            // The option is read for its refusals even here, where it cannot change the answer:
            // economizing trims the factors and this form has none. Skipping the check would let
            // svd(z, 'thin') through on a complex matrix and refuse it on a real one.
            _ = EconomySizedSvd(args, 0, 0, line, col);
            double[] sigma = ComplexEigen.SingularValues(ComplexRectOf("svd", args[0], line, col));
            JgsValue column = Numbers(sigma);
            if (sigma.Length > 1)
            {
                column.Reshape(sigma.Length, 1);
            }

            return column;
        }

        double[] a = ColumnMajorOf("svd", args[0], out int rows, out int cols, line, col);
        _ = EconomySizedSvd(args, rows, cols, line, col);
        double[] values = Svd.SingularValues(a, rows, cols);
        JgsValue result = Numbers(values);
        if (values.Length > 1)
        {
            result.Reshape(values.Length, 1);
        }

        return result;
    }

    /// <summary>
    /// [U, S, V] = svd(A) — MATLAB's shapes, so that <c>U·S·V'</c> is A: U is m-by-m, S is m-by-n
    /// and V is n-by-n, and the economy forms cut all three back to min(m, n).
    /// </summary>
    private static JgsValue[] SingularValueFactors(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted <= 1)
        {
            // MATLAB reads this off nargout, not off the brackets: [s] = svd(A) is s = svd(A) and
            // answers the values, never the first factor.
            return [SingularValueList(args, line, col)];
        }

        ArityRange("svd", args, 1, 2, line, col);
        if (HasComplexElements(args[0]))
        {
            Complex[,] z = ComplexRectOf("svd", args[0], line, col);
            int zRows = z.GetLength(0);
            int zCols = z.GetLength(1);
            bool zEconomy = EconomySizedSvd(args, zRows, zCols, line, col);
            (Complex[,] left, double[] sigma, Complex[,] right) = ComplexEigen.Svd(z, zEconomy);

            int zOrder = System.Math.Min(zRows, zCols);
            int sigmaRows = zEconomy ? zOrder : zRows;
            int sigmaCols = zEconomy ? zOrder : zCols;
            JgsValue middle = BuildColumnMajor(sigmaRows, sigmaCols, destination =>
            {
                for (int i = 0; i < zOrder; i++)
                {
                    destination[(i * sigmaRows) + i] = sigma[i];
                }
            });

            return wanted <= 2
                ? [FromComplexRect(left), middle]
                : [FromComplexRect(left), middle, FromComplexRect(right)];
        }

        double[] a = ColumnMajorOf("svd", args[0], out int rows, out int cols, line, col);
        bool economy = EconomySizedSvd(args, rows, cols, line, col);
        Svd svd = economy ? Svd.Factor(a, rows, cols) : Svd.FactorFull(a, rows, cols);

        int k = System.Math.Min(rows, cols);
        int diagonalRows = economy ? k : rows;
        int diagonalCols = economy ? k : cols;
        double[] values = svd.Values;
        JgsValue s = BuildColumnMajor(diagonalRows, diagonalCols, destination =>
        {
            for (int i = 0; i < k; i++)
            {
                destination[(i * diagonalRows) + i] = values[i];
            }
        });

        return wanted <= 2
            ? [FromColumnMajorRect(svd.UColumnMajor, rows, svd.UColumnCount), s]
            : [FromColumnMajorRect(svd.UColumnMajor, rows, svd.UColumnCount), s,
                FromColumnMajorRect(svd.VColumnMajor, cols, svd.VColumnCount)];
    }

    /// <summary>
    /// Whether <c>svd</c>'s second argument asked for the economy factors. The word <c>'econ'</c>
    /// always does; a literal <c>0</c> — MATLAB's older spelling — economizes a tall matrix and
    /// means the full decomposition for any other shape.
    /// </summary>
    private static bool EconomySizedSvd(IReadOnlyList<JgsValue> args, int rows, int cols, int line, int col)
    {
        if (args.Count < 2)
        {
            return false;
        }

        if (IsTextScalar(args[1]))
        {
            string word = Str("svd", args, 1, line, col).ToLowerInvariant();
            return word == "econ"
                ? true
                : throw new JgsRuntimeException(line, col, $"svd: '{word}' is not 'econ'.");
        }

        if (Num("svd", args, 1, line, col) != 0)
        {
            throw new JgsRuntimeException(line, col, "svd's second argument is 'econ' or 0.");
        }

        return rows > cols;
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

        // Every norm but one reads magnitudes and nothing else, so a complex argument is answered
        // by handing the same arithmetic |a| in place of a. The exception is the matrix 2-norm,
        // which is the largest singular value and has to ask the complex solver for it.
        JgsValue subject = args[0];
        if (HasComplexElements(args[0]))
        {
            Complex[,] z = ComplexRectOf("norm", args[0], line, col);
            if (isMatrix && word != "fro" && p == 2)
            {
                double[] singular = ComplexEigen.SingularValues(z);
                return JgsValue.Number(singular.Length == 0 ? 0 : singular[0]);
            }

            subject = MagnitudesOf(z);
        }

        if (!isMatrix)
        {
            double[] v = ToDoubles("norm", subject, line, col);
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

        double[] a = ColumnMajorOf("norm", subject, out int rows, out int cols, line, col);
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
            return JgsValue.Number(DenseLinalg.OneNorm(rows, cols, a, rows));
        }

        if (double.IsPositiveInfinity(p))
        {
            double best = 0;
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                for (int c = 0; c < cols; c++)
                {
                    sum += System.Math.Abs(a[(c * rows) + r]);
                }

                best = System.Math.Max(best, sum);
            }

            return JgsValue.Number(best);
        }

        if (p == 2)
        {
            // The largest singular value, and nothing else asked for: the vectors would cost as much
            // again as the values and this answer never looks at one.
            double[] sigma = Svd.SingularValues(a, rows, cols);
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

    /// <summary>A complex matrix's magnitudes, in the same shape — what every norm but one reads.</summary>
    private static JgsValue MagnitudesOf(Complex[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var sizes = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                sizes[r, c] = matrix[r, c].Magnitude;
            }
        }

        return FromRect(sizes);
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
