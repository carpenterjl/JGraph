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
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

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
        // Each question reads any double, single or logical matrix - real, complex or sparse - the
        // way R2025b does (open item 276); an N-D array is answered false by every predicate.
        void Predicate(string name, Func<ShapeQuestionSubject, bool> test) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                ShapeQuestionSubject subject = ShapeSubjectOf(name, args[0], line, col);
                return JgsValue.Bool(!subject.NotTwoD && test(subject));
            }, null);

        Predicate("istril", static a => ShapeBandwidth(a).Upper == 0);
        Predicate("istriu", static a => ShapeBandwidth(a).Lower == 0);
        Predicate("isdiag", static a => ShapeBandwidth(a) is (0, 0));

        // The two differ once an entry is complex: ishermitian compares with the conjugate, so a
        // complex diagonal entry is never Hermitian. Either takes R2025b's 'skew' or 'nonskew'.
        void Symmetry(string name, bool conjugate, string flagId) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 1, 2, line, col);
                bool skew = false;
                if (args.Count == 2)
                {
                    string flag = IsTextScalar(args[1]) ? TextOf(args[1]) : "";
                    skew = flag.Equals("skew", StringComparison.OrdinalIgnoreCase)
                        || (!flag.Equals("nonskew", StringComparison.OrdinalIgnoreCase)
                            ? throw new JgsRuntimeException(line, col, flagId, "Option must be 'skew' or 'nonskew'.")
                            : false);
                }

                ShapeQuestionSubject subject = ShapeSubjectOf(name, args[0], line, col);
                return JgsValue.Bool(!subject.NotTwoD && ShapeIsSymmetric(subject, conjugate, skew));
            }, null);

        Symmetry("issymmetric", conjugate: false, "MATLAB:issymmetric:inputFlag");
        Symmetry("ishermitian", conjugate: true, "MATLAB:isHermitian:inputFlag");

        Define("isbanded", (args, line, col) =>
        {
            Arity("isbanded", args, 3, line, col);
            ShapeQuestionSubject subject = ShapeSubjectOf("isbanded", args[0], line, col);
            int lowest = Count("isbanded", args, 1, line, col);
            int highest = Count("isbanded", args, 2, line, col);
            if (lowest < 0 || highest < 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:isbanded:nonNegIntScalar",
                    "Second and third inputs must be nonnegative integer scalars.");
            }

            if (subject.NotTwoD)
            {
                return JgsValue.False;
            }

            (int lower, int upper) = ShapeBandwidth(subject);
            return JgsValue.Bool(lower <= lowest && upper <= highest);
        }, null);

        Define("bandwidth", (args, line, col) =>
        {
            ArityRange("bandwidth", args, 1, 2, line, col);
            (int lower, int upper) = ShapeBandwidth(TwoDSubjectOf("bandwidth", args[0], line, col));

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
            (int lower, int upper) = ShapeBandwidth(TwoDSubjectOf("bandwidth", args[0], line, col));
            return [JgsValue.Number(lower), JgsValue.Number(upper)];
        });
    }

    /// <summary>
    /// The matrix a shape question reads: real dense entries, complex dense entries, or a sparse
    /// matrix's stored ones - or none at all for an N-D array, which every predicate answers false.
    /// </summary>
    private readonly record struct ShapeQuestionSubject(
        double[,]? Real, System.Numerics.Complex[,]? Complex, JGraph.Numerics.Sparse.CscMatrix? Sparse, bool NotTwoD);

    /// <summary>
    /// A shape question's subject read once. A complex matrix used to reach the real reader and be
    /// refused at its first complex entry (open item 276), and an empty one was read past its end.
    /// </summary>
    private static ShapeQuestionSubject ShapeSubjectOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Sparse)
        {
            return new(null, null, value.AsSparse, false);
        }

        if (value.Type == JgsType.Array && value.IsNd && !value.IsStringArray && !value.IsCategorical)
        {
            return new(null, null, null, true);
        }

        if (HasComplexElements(value) || (value.Type == JgsType.Array && value.ArrayLength == 0))
        {
            return new(null, ComplexRectOf(name, value, line, col), null, false);
        }

        return new(RectOf(name, value, line, col), null, null, false);
    }

    /// <summary>The subject of <c>bandwidth</c>, which refuses an N-D array where the predicates answer false.</summary>
    private static ShapeQuestionSubject TwoDSubjectOf(string name, JgsValue value, int line, int col)
    {
        ShapeQuestionSubject subject = ShapeSubjectOf(name, value, line, col);
        return subject.NotTwoD
            ? throw (JgsRunningDialect.ThreadIsMatlab
                ? new JgsRuntimeException(line, col, "MATLAB:bandwidth:inputMustBe2D", "Input must be 2-D.")
                : new JgsRuntimeException(line, col, $"{name} needs a 2-D matrix."))
            : subject;
    }

    /// <summary>How far below and above the diagonal a subject's non-zero entries reach.</summary>
    private static (int Lower, int Upper) ShapeBandwidth(in ShapeQuestionSubject subject)
    {
        if (subject.Real is { } real)
        {
            return Bandwidth(real);
        }

        int lower = 0;
        int upper = 0;
        void Reach(int r, int c)
        {
            if (r > c)
            {
                lower = Math.Max(lower, r - c);
            }
            else if (c > r)
            {
                upper = Math.Max(upper, c - r);
            }
        }

        if (subject.Complex is { } complex)
        {
            for (int r = 0; r < complex.GetLength(0); r++)
            {
                for (int c = 0; c < complex.GetLength(1); c++)
                {
                    if (complex[r, c] != System.Numerics.Complex.Zero)
                    {
                        Reach(r, c);
                    }
                }
            }
        }
        else if (subject.Sparse is { } sparse)
        {
            for (int c = 0; c < sparse.Cols; c++)
            {
                for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
                {
                    if (sparse.Values[k] != 0)
                    {
                        Reach(sparse.RowIndices[k], c);
                    }
                }
            }
        }

        return (lower, upper);
    }

    /// <summary>
    /// Whether a subject equals its transpose - its conjugate transpose when
    /// <paramref name="conjugate"/>, the negative of either when <paramref name="skew"/>. Exact
    /// equality, the diagonal included, as R2025b compares: a NaN anywhere it looks is unequal to
    /// itself, and a complex diagonal entry is never Hermitian.
    /// </summary>
    private static bool ShapeIsSymmetric(in ShapeQuestionSubject subject, bool conjugate, bool skew)
    {
        if (subject.Sparse is { } sparse)
        {
            if (sparse.Rows != sparse.Cols)
            {
                return false;
            }

            // Real entries only, so the conjugate changes nothing: each stored entry against its mirror.
            var stored = new Dictionary<(int, int), double>();
            for (int c = 0; c < sparse.Cols; c++)
            {
                for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
                {
                    stored[(sparse.RowIndices[k], c)] = sparse.Values[k];
                }
            }

            foreach (((int r, int c), double value) in stored)
            {
                double mirror = stored.TryGetValue((c, r), out double found) ? found : 0;
                if (value != (skew ? -mirror : mirror))
                {
                    return false;
                }
            }

            return true;
        }

        if (subject.Real is { } real)
        {
            int n = real.GetLength(0);
            if (n != real.GetLength(1))
            {
                return false;
            }

            for (int r = 0; r < n; r++)
            {
                for (int c = r; c < n; c++)
                {
                    if (real[r, c] != (skew ? -real[c, r] : real[c, r]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        System.Numerics.Complex[,] complex = subject.Complex!;
        int size = complex.GetLength(0);
        if (size != complex.GetLength(1))
        {
            return false;
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = r; c < size; c++)
            {
                System.Numerics.Complex mirror = conjugate
                    ? System.Numerics.Complex.Conjugate(complex[c, r])
                    : complex[c, r];
                if (complex[r, c] != (skew ? -mirror : mirror))
                {
                    return false;
                }
            }
        }

        return true;
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
                if (TryPackedHessenberg(args[0], wantQ: false, out JgsValue h, out _))
                {
                    return h;
                }

                return FromRect(Hessenberg.Reduce(SquareRect("hess", args[0], line, col)).H);
            },
            (args, wanted, line, col) =>
            {
                Arity("hess", args, 1, line, col);
                if (TryPackedHessenberg(args[0], wantQ: wanted >= 2, out JgsValue h, out JgsValue? q))
                {
                    return q is null ? [h] : [q, h];
                }

                Hessenberg reduced = Hessenberg.Reduce(SquareRect("hess", args[0], line, col));
                return wanted >= 2 ? [FromRect(reduced.Q), FromRect(reduced.H)] : [FromRect(reduced.H)];
            });

        Define("expm", (args, line, col) =>
        {
            Arity("expm", args, 1, line, col);

            // A packed square matrix goes in and comes out column-major, with no rectangle either
            // side of the same arithmetic (the every-row plan's I3, ADR 0332).
            if (TryPackedMatrix(args[0], out JGraph.Numerics.NumericBuffer stored, out int rows, out int cols)
                && rows == cols)
            {
                double[] exponential = MatrixFunctions.Exponential(stored.AsSpan(0, rows * cols), rows);
                GC.KeepAlive(stored);
                return FromColumnMajorRect(exponential, rows, cols);
            }

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

    /// <summary>
    /// <c>hess</c> of a packed square matrix on the blocked kernel, column-major in and out, with Q
    /// formed only when it is one of the outputs (the every-row plan's I3, ADR 0332). False for
    /// anything else, or a backend without the kernel, and the rectangle road answers.
    /// </summary>
    private static bool TryPackedHessenberg(JgsValue value, bool wantQ, out JgsValue h, out JgsValue? q)
    {
        h = JgsValue.Null;
        q = null;
        if (!LinalgProvider.Current.IsNative
            || !TryPackedMatrix(value, out JGraph.Numerics.NumericBuffer stored, out int rows, out int cols)
            || rows != cols || rows == 0)
        {
            return false;
        }

        int n = rows;
        double[] work = GC.AllocateUninitializedArray<double>(n * n);
        FactorFills.Copy(stored.AsSpan(0, n * n), work);
        GC.KeepAlive(stored);
        if (!Hessenberg.TryReduceColumnMajor(work, n, wantQ, out double[]? factor))
        {
            return false;
        }

        h = FromColumnMajorRect(work, n, n);
        q = factor is null ? null : FromColumnMajorRect(factor, n, n);
        return true;
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
            return SelectColumnsColumnMajor(svd.VColumnMajor, cols, svd.VColumnCount, svd.Values,
                Tolerance(rows, cols, svd.Values), keepAbove: false);
        }, null);

        Define("orth", (args, line, col) =>
        {
            Arity("orth", args, 1, line, col);
            double[] a = ColumnMajorOf("orth", args[0], out int rows, out int cols, line, col);

            // The range needs no completing: it can be no wider than the rank, and the economy U
            // already carries every column a nonzero singular value could claim.
            Svd svd = Svd.Factor(a, rows, cols);
            return SelectColumnsColumnMajor(svd.UColumnMajor, rows, svd.UColumnCount, svd.Values,
                Tolerance(rows, cols, svd.Values), keepAbove: true);
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

            // The singular values descend, so the ones that count are a leading run and the
            // pseudoinverse is one product over that run: V·diag(1/s)·Uᵀ. It used to be a triple
            // loop over rank, rows and columns, which at 600 by 300 was four times the SVD it
            // followed (the every-row plan's I4, ADR 0329).
            int rank = 0;
            while (rank < values.Length && values[rank] > tolerance)
            {
                rank++;
            }

            if (rank == 0 || rows == 0 || cols == 0)
            {
                return BuildColumnMajor(cols, rows, static _ => { });
            }

            var scaled = new double[(long)cols * rank];
            for (int k = 0; k < rank; k++)
            {
                double reciprocal = 1.0 / values[k];
                int at = k * cols;
                for (int r = 0; r < cols; r++)
                {
                    scaled[at + r] = reciprocal * v[at + r];
                }
            }

            return BuildColumnMajor(cols, rows, destination =>
                LinalgProvider.Current.Gemm(false, true, cols, rows, rank, scaled, cols, u, rows, destination, cols));
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

    /// <summary>
    /// <see cref="SelectColumns"/> over a column-major factor, answering the script value: the kept
    /// columns are whole runs of storage, copied as they lie, where the rectangle road built the
    /// factor's rectangle, a rectangle of the kept columns and the value from that — three
    /// transposing copies for the same numbers (the every-row plan's I3, ADR 0332).
    /// </summary>
    private static JgsValue SelectColumnsColumnMajor(double[] factor, int rows, int columns, double[] singular,
        double tolerance, bool keepAbove)
    {
        var kept = new List<int>();
        for (int k = 0; k < columns; k++)
        {
            double value = k < singular.Length ? singular[k] : 0;
            if (keepAbove ? value > tolerance : value <= tolerance)
            {
                kept.Add(k);
            }
        }

        // A factor with no rows still answers its columns' count: FromRect of a rows-by-k rectangle.
        if (rows == 0 || kept.Count == 0)
        {
            return FromRect(new double[rows, kept.Count]);
        }

        var selected = new double[(long)rows * kept.Count];
        for (int c = 0; c < kept.Count; c++)
        {
            Array.Copy(factor, (long)kept[c] * rows, selected, (long)c * rows, rows);
        }

        return FromColumnMajorRect(selected, rows, kept.Count);
    }

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
