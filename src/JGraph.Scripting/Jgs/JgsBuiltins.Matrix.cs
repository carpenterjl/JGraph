using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The questions a script asks about a matrix's shape (<c>issymmetric</c>, <c>istriu</c>,
/// <c>bandwidth</c>), the triangular extractions, and the factorizations M36 left out — Cholesky,
/// LDLᵀ, Hessenberg — with the solvers and subspaces built on the decompositions it did add.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the matrix predicates and linear-algebra extras (M38).</summary>
    private static void RegisterMatrixBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        RegisterMatrixPredicates(Define);
        RegisterTriangularParts(Define);
        RegisterFactorizations(Define);
        RegisterSubspaces(Define);
    }

    // --- Shape questions --------------------------------------------------------------------------

    private static void RegisterMatrixPredicates(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        void Predicate(string name, Func<double[,], bool> test) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                return JgsValue.Bool(test(RectOf(name, args[0], line, col)));
            }, null);

        Predicate("istril", static a => Bandwidth(a).Upper == 0);
        Predicate("istriu", static a => Bandwidth(a).Lower == 0);
        Predicate("isdiag", static a => Bandwidth(a) is (0, 0));

        // For a real matrix these two ask the same question; the distinction only appears once a
        // matrix can hold complex entries, and JGraph's matrices are real.
        Predicate("issymmetric", IsSymmetric);
        Predicate("ishermitian", IsSymmetric);

        Define("isbanded", (args, line, col) =>
        {
            Arity("isbanded", args, 3, line, col);
            (int lower, int upper) = Bandwidth(RectOf("isbanded", args[0], line, col));
            return JgsValue.Bool(lower <= Count("isbanded", args, 1, line, col)
                && upper <= Count("isbanded", args, 2, line, col));
        }, null);

        Define("bandwidth", (args, line, col) =>
        {
            ArityRange("bandwidth", args, 1, 2, line, col);
            (int lower, int upper) = Bandwidth(RectOf("bandwidth", args[0], line, col));

            // bandwidth(A) alone is the lower bandwidth (MATLAB's first output); the word picks one.
            if (args.Count == 1)
            {
                return JgsValue.Number(lower);
            }

            return Str("bandwidth", args, 1, line, col) switch
            {
                "lower" => JgsValue.Number(lower),
                "upper" => JgsValue.Number(upper),
                var other => throw new JgsRuntimeException(line, col, $"bandwidth: '{other}' is not 'lower' or 'upper'."),
            };
        },
        (args, _, line, col) =>
        {
            Arity("bandwidth", args, 1, line, col);
            (int lower, int upper) = Bandwidth(RectOf("bandwidth", args[0], line, col));
            return [JgsValue.Number(lower), JgsValue.Number(upper)];
        });
    }

    /// <summary>How far below and above the diagonal a matrix's non-zero entries reach.</summary>
    private static (int Lower, int Upper) Bandwidth(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        int lower = 0;
        int upper = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (a[r, c] == 0)
                {
                    continue;
                }

                if (r > c)
                {
                    lower = Math.Max(lower, r - c);
                }
                else if (c > r)
                {
                    upper = Math.Max(upper, c - r);
                }
            }
        }

        return (lower, upper);
    }

    private static bool IsSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        if (n != a.GetLength(1))
        {
            return false;
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = r + 1; c < n; c++)
            {
                // Exact equality, as MATLAB's issymmetric uses: a matrix that is only nearly
                // symmetric is a different question, and answering it needs a tolerance the caller
                // chooses.
                if (a[r, c] != a[c, r])
                {
                    return false;
                }
            }
        }

        return true;
    }

    // --- Triangular parts -------------------------------------------------------------------------

    private static void RegisterTriangularParts(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        void Triangle(string name, bool keepLower) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 1, 2, line, col);
                int k = args.Count == 2 ? Count(name, args, 1, line, col) : 0;

                // Both verbs are pure selection: an entry is kept where it lies or written over
                // with a zero, and neither one ever looks at what the entry holds. A complex
                // matrix was refused here only because the reader on the way in was the real one.
                if (HasComplexElements(args[0]))
                {
                    return FromComplexRect(
                        KeptTriangle(ComplexRectOf(name, args[0], line, col), k, keepLower));
                }

                double[,] a = RectOf(name, args[0], line, col);
                int rows = a.GetLength(0);
                int cols = a.GetLength(1);
                var kept = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        bool inside = keepLower ? c - r <= k : c - r >= k;
                        kept[r, c] = inside ? a[r, c] : 0;
                    }
                }

                return FromRect(kept);
            }, null);

        Triangle("tril", keepLower: true);
        Triangle("triu", keepLower: false);
    }

    /// <summary>The k-th triangle of a complex matrix, with a zero everywhere outside it.</summary>
    private static System.Numerics.Complex[,] KeptTriangle(
        System.Numerics.Complex[,] matrix, int k, bool keepLower)
    {
        int rows = matrix.GetLength(0);
        int cols = matrix.GetLength(1);
        var kept = new System.Numerics.Complex[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bool inside = keepLower ? c - r <= k : c - r >= k;
                kept[r, c] = inside ? matrix[r, c] : System.Numerics.Complex.Zero;
            }
        }

        return kept;
    }

    // --- Factorizations ---------------------------------------------------------------------------

    private static void RegisterFactorizations(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("chol",
            (args, line, col) => CholeskyAnswer(args, 1, line, col)[0],
            CholeskyAnswer);

        Define("ldl",
            (args, line, col) => FromRect(FactorLdl(args, line, col).Lower),
            (args, wanted, line, col) =>
            {
                Ldl factored = FactorLdl(args, line, col);
                int n = factored.Diagonal.Length;
                var d = new double[n, n];
                for (int i = 0; i < n; i++)
                {
                    d[i, i] = factored.Diagonal[i];
                }

                if (wanted >= 3)
                {
                    return [FromRect(factored.Lower), FromRect(d), FromRect(factored.Permutation)];
                }

                // [L, D] folds the permutation into L, so L·D·Lᵀ reassembles A itself.
                return [FromRect(Linear.Multiply(Linear.Transpose(factored.Permutation), factored.Lower)), FromRect(d)];
            });

        Define("hess",
            (args, line, col) =>
            {
                Arity("hess", args, 1, line, col);
                return FromRect(Hessenberg.Reduce(SquareRect("hess", args[0], line, col)).H);
            },
            (args, _, line, col) =>
            {
                Arity("hess", args, 1, line, col);
                Hessenberg reduced = Hessenberg.Reduce(SquareRect("hess", args[0], line, col));
                return [FromRect(reduced.Q), FromRect(reduced.H)];
            });

        Define("expm", (args, line, col) =>
        {
            Arity("expm", args, 1, line, col);
            return FromRect(MatrixFunctions.Exponential(SquareRect("expm", args[0], line, col)));
        }, null);

        Define("linsolve",
            (args, line, col) => LinearSolveAnswer(args, 1, line, col)[0],
            LinearSolveAnswer);

        Define("rcond", (args, line, col) =>
        {
            Arity("rcond", args, 1, line, col);
            double[] a = SquareColumnMajorOf("rcond", args[0], out int n, line, col);

            // ‖A‖₁ first: the factorization overwrites the matrix it is handed.
            double anorm = DenseLinalg.OneNorm(n, n, a, n);
            return JgsValue.Number(LuDecomposition.FactorAdopting(a, n).ReciprocalCondition(anorm));
        }, null);
    }

    /// <summary>Runs the LDLᵀ factorization, reporting the pivot case it cannot handle.</summary>
    private static Ldl FactorLdl(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("ldl", args, 1, line, col);
        double[,] a = SquareRect("ldl", args[0], line, col);
        if (!IsSymmetric(a))
        {
            throw new JgsRuntimeException(line, col, "ldl needs a symmetric matrix.");
        }

        Ldl factored = Ldl.Factor(a);
        return factored.IsFactored
            ? factored
            : throw new JgsRuntimeException(line, col,
                "ldl: this matrix needs 2x2 block pivoting, which JGraph's factorization does not do.");
    }

    /// <summary>The maximum absolute column sum — the matrix 1-norm.</summary>
    private static double OneNorm(double[,] a)
    {
        double best = 0;
        for (int c = 0; c < a.GetLength(1); c++)
        {
            double sum = 0;
            for (int r = 0; r < a.GetLength(0); r++)
            {
                sum += Math.Abs(a[r, c]);
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    /// <summary>
    /// A right-hand side as a column matrix. A plain vector is a *column* here, not a row: the
    /// orientation-free vector has to pick a side to solve A·x = b, and b is a column in every
    /// textbook statement of the problem.
    /// </summary>
    private static double[,] ColumnsOf(string name, JgsValue value, int line, int col)
    {
        if (IsMatrixValue(value))
        {
            return RectOf(name, value, line, col);
        }

        double[] flat = value.Type is JgsType.Number or JgsType.Bool
            ? [value.AsNumber]
            : ToDoubles(name, value, line, col);
        var column = new double[flat.Length, 1];
        for (int i = 0; i < flat.Length; i++)
        {
            column[i, 0] = flat[i];
        }

        return column;
    }

    // --- Subspaces and norms ----------------------------------------------------------------------

    private static void RegisterSubspaces(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("null", (args, line, col) =>
        {
            Arity("null", args, 1, line, col);
            double[] a = ColumnMajorOf("null", args[0], out int rows, out int cols, line, col);

            // The null space is spanned by the right singular vectors of the negligible singular
            // values, which is why this needs the V factor rather than a row reduction. A wide
            // matrix needs the full one — its null space is wider than the economy V has columns —
            // and a tall matrix must not be given it: the economy V is already n-by-n there, and
            // asking for the full decomposition would build an m-by-m U to go unread, which for a
            // long thin matrix is the whole of the memory.
            Svd svd = rows < cols ? Svd.FactorFull(a, rows, cols) : Svd.Factor(a, rows, cols);
            return FromRect(SelectColumns(svd.V, svd.Values, Tolerance(rows, cols, svd.Values), keepAbove: false));
        }, null);

        Define("orth", (args, line, col) =>
        {
            Arity("orth", args, 1, line, col);
            double[] a = ColumnMajorOf("orth", args[0], out int rows, out int cols, line, col);

            // The range needs no completing: it can be no wider than the rank, and the economy U
            // already carries every column a nonzero singular value could claim.
            Svd svd = Svd.Factor(a, rows, cols);
            return FromRect(SelectColumns(svd.U, svd.Values, Tolerance(rows, cols, svd.Values), keepAbove: true));
        }, null);

        Define("pinv", (args, line, col) =>
        {
            Arity("pinv", args, 1, line, col);
            double[] a = ColumnMajorOf("pinv", args[0], out int rows, out int cols, line, col);
            Svd svd = Svd.Factor(a, rows, cols);
            double tolerance = Tolerance(rows, cols, svd.Values);
            double[] values = svd.Values;
            double[] u = svd.UColumnMajor;
            double[] v = svd.VColumnMajor;
            return BuildColumnMajor(cols, rows, destination =>
            {
                for (int k = 0; k < values.Length; k++)
                {
                    if (values[k] <= tolerance)
                    {
                        continue;
                    }

                    double reciprocal = 1.0 / values[k];
                    for (int r = 0; r < cols; r++)
                    {
                        for (int c = 0; c < rows; c++)
                        {
                            destination[(c * cols) + r] +=
                                reciprocal * v[(k * cols) + r] * u[(k * rows) + c];
                        }
                    }
                }
            });
        }, null);

        Define("cross", (args, line, col) =>
        {
            Arity("cross", args, 2, line, col);
            double[] a = ToDoubles("cross", args[0], line, col);
            double[] b = ToDoubles("cross", args[1], line, col);
            if (a.Length != 3 || b.Length != 3)
            {
                throw new JgsRuntimeException(line, col, "cross needs two 3-element vectors.");
            }

            return Numbers([
                (a[1] * b[2]) - (a[2] * b[1]),
                (a[2] * b[0]) - (a[0] * b[2]),
                (a[0] * b[1]) - (a[1] * b[0]),
            ]);
        }, null);

        Define("vecnorm", (args, line, col) =>
        {
            ArityRange("vecnorm", args, 1, 2, line, col);
            double p = args.Count == 2 ? Num("vecnorm", args, 1, line, col) : 2;

            // vecnorm works down the columns of a matrix and along a vector — the same convention
            // M36 settled on for sum/mean in the MATLAB dialect.
            if (!IsMatrixValue(args[0]))
            {
                return JgsValue.Number(VectorNorm(ToDoubles("vecnorm", args[0], line, col), p));
            }

            double[,] a = RectOf("vecnorm", args[0], line, col);
            int rows = a.GetLength(0);
            int cols = a.GetLength(1);
            var norms = new double[cols];
            var column = new double[rows];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    column[r] = a[r, c];
                }

                norms[c] = VectorNorm(column, p);
            }

            return Numbers(norms);
        }, null);
    }

    /// <summary>The singular value below which a value counts as zero — MATLAB's rank tolerance.</summary>
    private static double Tolerance(int rows, int cols, double[] singular) =>
        Math.Max(rows, cols) * (singular.Length == 0 ? 0 : singular[0]) * 2.220446049250313e-16;

    /// <summary>The columns of <paramref name="matrix"/> whose singular value is above or below a threshold.</summary>
    private static double[,] SelectColumns(double[,] matrix, double[] singular, double tolerance, bool keepAbove)
    {
        int rows = matrix.GetLength(0);
        var kept = new List<int>();
        for (int k = 0; k < matrix.GetLength(1); k++)
        {
            // Columns past the end of the singular-value list belong to the null space: they have no
            // singular value precisely because the matrix has no rank left to give them.
            double value = k < singular.Length ? singular[k] : 0;
            if (keepAbove ? value > tolerance : value <= tolerance)
            {
                kept.Add(k);
            }
        }

        var selected = new double[rows, kept.Count];
        for (int c = 0; c < kept.Count; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                selected[r, c] = matrix[r, kept[c]];
            }
        }

        return selected;
    }

    private static double VectorNorm(double[] values, double p)
    {
        if (double.IsPositiveInfinity(p))
        {
            double largest = 0;
            foreach (double x in values)
            {
                largest = Math.Max(largest, Math.Abs(x));
            }

            return largest;
        }

        double sum = 0;
        foreach (double x in values)
        {
            sum += Math.Pow(Math.Abs(x), p);
        }

        return Math.Pow(sum, 1.0 / p);
    }
}
