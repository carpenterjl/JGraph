using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The documented forms of the decompositions that M76 added: the pencil <c>eig</c> takes, the left
/// eigenvectors, the words that choose an algorithm or the shape of an answer, and the second
/// outputs <c>chol</c> and <c>linsolve</c> report their difficulty in.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>eig(A)</c>, <c>eig(A, B)</c>, and the trailing words: <c>'balance'</c>/<c>'nobalance'</c>,
    /// <c>'chol'</c>/<c>'qz'</c>, and <c>'vector'</c>/<c>'matrix'</c>.
    /// </summary>
    private static JgsValue[] EigenAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("eig", args, 1, 3, line, col);

        // The second argument is the pencil's other matrix unless it is a word, which is the only
        // thing that tells the two families of form apart.
        JgsValue? pair = args.Count > 1 && !IsTextScalar(args[1]) ? args[1] : null;
        int at = pair is null ? 1 : 2;

        string algorithm = string.Empty;
        string form = string.Empty;
        for (; at < args.Count; at++)
        {
            string word = Str("eig", args, at, line, col).ToLowerInvariant();
            switch (word)
            {
                case "balance":
                case "nobalance":
                    if (pair is not null)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"eig: '{word}' balances a single matrix; a pair of matrices takes 'chol' or 'qz'.");
                    }

                    break;
                case "chol":
                case "qz":
                    if (pair is null)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"eig: '{word}' chooses an algorithm for a pair of matrices, and only one was given.");
                    }

                    algorithm = word;
                    break;
                case "vector":
                case "matrix":
                    form = word;
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"eig: '{word}' is not one of 'balance', 'nobalance', 'chol', 'qz', 'vector' or 'matrix'.");
            }
        }

        // One output answers a column of eigenvalues, more than one a diagonal matrix of them; the
        // word overrides either way, which is what it is for.
        bool asVector = form.Length > 0 ? form == "vector" : wanted <= 1;

        return pair is null
            ? SingleEigen(args[0], wanted, asVector, line, col)
            : PencilEigen(args[0], pair, algorithm, wanted, asVector, line, col);
    }

    /// <summary>The eigenvalues of one matrix, with its right and left eigenvectors when asked.</summary>
    private static JgsValue[] SingleEigen(JgsValue value, int wanted, bool asVector, int line, int col)
    {
        if (HasComplexElements(value))
        {
            if (wanted > 1)
            {
                throw new JgsRuntimeException(line, col,
                    "[V, D] = eig(A) is not supported for a complex A; e = eig(A) computes the eigenvalues.");
            }

            return [EigenvalueList(ComplexEigen.Values(ComplexSquareOf("eig", value, line, col)), asColumn: true)];
        }

        double[,] a = SquareRect("eig", value, line, col);
        Eigen eigen = Eigen.Factor(a);
        if (wanted <= 1)
        {
            return [asVector ? EigenvalueList(eigen.Values, asColumn: false) : FromComplexRect(Diagonal(eigen.Values))];
        }

        JgsValue d = asVector ? EigenvalueList(eigen.Values, asColumn: false) : FromComplexRect(Diagonal(eigen.Values));
        return wanted <= 2
            ? [FromComplexRect(eigen.Vectors), d]
            : [FromComplexRect(eigen.Vectors), d, FromComplexRect(LeftEigenvectors(a, eigen.Values))];
    }

    /// <summary>The eigenvalues of a pencil — <c>A·v = λ·B·v</c>.</summary>
    private static JgsValue[] PencilEigen(JgsValue first, JgsValue second, string algorithm,
        int wanted, bool asVector, int line, int col)
    {
        if (HasComplexElements(first) || HasComplexElements(second))
        {
            throw new JgsRuntimeException(line, col,
                "eig of a pair of matrices is real here; a complex pencil is not supported.");
        }

        double[,] a = SquareRect("eig", first, line, col);
        double[,] b = SquareRect("eig", second, line, col);
        if (a.GetLength(0) != b.GetLength(0))
        {
            throw new JgsRuntimeException(line, col,
                $"eig: the two matrices must be the same size, but got {a.GetLength(0)}x{a.GetLength(1)} " +
                $"and {b.GetLength(0)}x{b.GetLength(1)}.");
        }

        // MATLAB's own default: a symmetric pair with a definite B goes through Cholesky, which is
        // both faster and the reason those eigenvalues come back real and in ascending order.
        if (algorithm != "qz" && IsSymmetric(a) && IsSymmetric(b))
        {
            Cholesky cholesky = Cholesky.Factor(b);
            if (cholesky.IsPositiveDefinite)
            {
                return DefinitePencil(a, cholesky.Lower, wanted, asVector);
            }

            if (algorithm == "chol")
            {
                throw new JgsRuntimeException(line, col,
                    "eig(A, B, 'chol') needs a symmetric positive definite B; this one is not, so use 'qz'.");
            }
        }
        else if (algorithm == "chol")
        {
            throw new JgsRuntimeException(line, col,
                "eig(A, B, 'chol') needs both matrices symmetric; these are not, so use 'qz'.");
        }

        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        if (wanted <= 1)
        {
            return [asVector ? EigenvalueList(qz.Eigenvalues, asColumn: false) : FromComplexRect(Diagonal(qz.Eigenvalues))];
        }

        if (!qz.IsFinite)
        {
            throw new JgsRuntimeException(line, col,
                "[V, D] = eig(A, B) needs a nonsingular B: this pencil has an eigenvalue at infinity, " +
                "which has no eigenvector to report. e = eig(A, B) gives the eigenvalues themselves.");
        }

        // With B nonsingular the pencil's eigenvectors are those of B\\A exactly, and that path is
        // the one already measured against MATLAB.
        Eigen eigen = Eigen.Factor(Linear.Solve(b, a));
        JgsValue d = asVector ? EigenvalueList(eigen.Values, asColumn: false) : FromComplexRect(Diagonal(eigen.Values));
        return wanted <= 2
            ? [FromComplexRect(eigen.Vectors), d]
            : [FromComplexRect(eigen.Vectors), d,
                FromComplexRect(LeftEigenvectors(Linear.Solve(b, a), eigen.Values))];
    }

    /// <summary>
    /// The symmetric-definite pencil through its Cholesky factor: <c>L⁻¹·A·L⁻ᵀ</c> is symmetric and
    /// has the pencil's eigenvalues, and its eigenvectors carried back through <c>L⁻ᵀ</c> are the
    /// pencil's — already scaled so that <c>Vᵀ·B·V</c> is the identity.
    /// </summary>
    private static JgsValue[] DefinitePencil(double[,] a, double[,] lower, int wanted, bool asVector)
    {
        double[,] reduced = Linear.Transpose(
            Linear.Solve(lower, Linear.Transpose(Linear.Solve(lower, a))));

        // Symmetry is exact in theory and to rounding in practice; averaging with the transpose
        // keeps the symmetric eigenvalue path from being sent down the general one by dust.
        int n = reduced.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double mean = (reduced[i, j] + reduced[j, i]) / 2;
                reduced[i, j] = mean;
                reduced[j, i] = mean;
            }
        }

        Eigen eigen = Eigen.Factor(reduced);
        JgsValue d = asVector ? EigenvalueList(eigen.Values, asColumn: false) : FromComplexRect(Diagonal(eigen.Values));
        if (wanted <= 1)
        {
            return [d];
        }

        var real = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                real[i, j] = eigen.Vectors[i, j].Real;
            }
        }

        double[,] vectors = Linear.Solve(Linear.Transpose(lower), real);
        return wanted <= 2
            ? [FromRect(vectors), d]
            : [FromRect(vectors), d, FromRect(vectors)];
    }

    /// <summary>
    /// The left eigenvectors: the columns W for which <c>Wᴴ·A = D·Wᴴ</c>. They are the right
    /// eigenvectors of the transpose, taken for the conjugate eigenvalue — which is why the two
    /// factorizations have to be matched up by value rather than trusted to arrive in step.
    /// </summary>
    private static Complex[,] LeftEigenvectors(double[,] a, Complex[] values)
    {
        int n = values.Length;
        Eigen transposed = Eigen.Factor(Linear.Transpose(a));
        var taken = new bool[n];
        var w = new Complex[n, n];

        for (int k = 0; k < n; k++)
        {
            int best = -1;
            double nearest = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (taken[j])
                {
                    continue;
                }

                double distance = (transposed.Values[j] - Complex.Conjugate(values[k])).Magnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = j;
                }
            }

            taken[best] = true;

            double length = 0;
            for (int i = 0; i < n; i++)
            {
                length += transposed.Vectors[i, best].Magnitude * transposed.Vectors[i, best].Magnitude;
            }

            length = Math.Sqrt(length);
            for (int i = 0; i < n; i++)
            {
                w[i, k] = length == 0 ? transposed.Vectors[i, best] : transposed.Vectors[i, best] / length;
            }
        }

        return w;
    }

    // --- qr ---------------------------------------------------------------------------------

    /// <summary>
    /// Every documented <c>qr</c> form: any shape of matrix, the economy flag, column pivoting with
    /// its permutation as a matrix or a vector, and the pair form <c>[C, R] = qr(S, B)</c> that
    /// applies <c>Qᵀ</c> to a right-hand side instead of forming <c>Q</c> at all.
    /// </summary>
    private static JgsValue[] QrAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("qr", args, 1, 3, line, col);
        double[,] a = DenseOf("qr", args[0], line, col);

        bool economy = false;
        string form = string.Empty;
        double[,]? rhs = null;

        for (int at = 1; at < args.Count; at++)
        {
            JgsValue argument = args[at];
            if (IsTextScalar(argument))
            {
                string word = Str("qr", args, at, line, col).ToLowerInvariant();
                switch (word)
                {
                    case "vector":
                    case "matrix":
                        form = word;
                        break;
                    case "econ":
                        economy = true;
                        break;
                    default:
                        throw new JgsRuntimeException(line, col,
                            $"qr: '{word}' is not one of 'econ', 'vector' or 'matrix'.");
                }

                continue;
            }

            // A literal zero is MATLAB's economy flag; anything else in that position is the
            // right-hand side of the pair form.
            if (at == 1 && !(argument.Type == JgsType.Number && argument.AsNumber == 0))
            {
                rhs = DenseOf("qr", argument, line, col);
                if (rhs.GetLength(0) != a.GetLength(0))
                {
                    throw new JgsRuntimeException(line, col,
                        "qr: the right-hand side must have as many rows as the matrix.");
                }

                continue;
            }

            economy = true;
        }

        bool pivoting = rhs is null ? wanted >= 3 : wanted >= 3;
        QrDecomposition qr = QrDecomposition.Factor(a, pivoting);

        if (rhs is not null)
        {
            // [C, R] = qr(S, B): C is Qᵀ·B, which is all a least-squares solve needs, and R\C is
            // the solution. Q itself is never asked for and never formed.
            double[,] c = Linear.Multiply(Linear.Transpose(economy ? qr.Q : qr.FullQ), rhs);
            double[,] r = economy ? qr.R : qr.FullR;
            return wanted >= 3
                ? [FromRect(c), FromRect(r), Permutation(qr, form)]
                : [FromRect(c), FromRect(r)];
        }

        if (wanted <= 1)
        {
            return [FromRect(economy ? qr.R : qr.FullR)];
        }

        return wanted <= 2
            ? [FromRect(economy ? qr.Q : qr.FullQ), FromRect(economy ? qr.R : qr.FullR)]
            : [FromRect(economy ? qr.Q : qr.FullQ), FromRect(economy ? qr.R : qr.FullR),
                Permutation(qr, form)];
    }

    /// <summary>The pivoting as MATLAB's <c>outputForm</c> asked for it: a matrix, or the row of
    /// column numbers that stands for it.</summary>
    private static JgsValue Permutation(QrDecomposition qr, string form)
    {
        if (form != "vector")
        {
            return FromRect(qr.Permutation);
        }

        int[] order = qr.PivotVector;
        var numbers = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            numbers[i] = order[i] + 1;
        }

        return Numbers(numbers);
    }

    // --- lu ---------------------------------------------------------------------------------

    /// <summary>
    /// The permutation outputs <c>lu</c> can be asked for, and the word that says whether they come
    /// back as matrices or as the vectors of indices that stand for them.
    /// </summary>
    private static JgsValue[] LowerUpperAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("lu", args, 1, 3, line, col);

        string form = string.Empty;
        for (int at = 1; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = Str("lu", args, at, line, col).ToLowerInvariant();
                form = word is "vector" or "matrix"
                    ? word
                    : throw new JgsRuntimeException(line, col,
                        $"lu: '{word}' is not 'vector' or 'matrix'.");
                continue;
            }

            // The pivoting threshold. Sparse LU here pivots for stability with no dial to turn, so
            // the value is read and checked rather than acted on.
            double threshold = Num("lu", args, at, line, col);
            if (threshold < 0 || threshold > 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"lu: a pivoting threshold is between 0 and 1, but got {threshold}.");
            }
        }

        if (args[0].Type == JgsType.Sparse)
        {
            return SparseLowerUpper(args[0], form, wanted, line, col);
        }

        LuDecomposition lu = LuDecomposition.Factor(SquareRect("lu", args[0], line, col));
        int n = lu.Upper.GetLength(0);

        if (wanted <= 1)
        {
            return [FromRect(Combined(lu))];
        }

        if (wanted == 2)
        {
            // [L, U] folds the permutation into L, so L*U still reassembles A.
            return
            [
                FromRect(Linear.Multiply(Linear.Transpose(lu.Permutation), lu.Lower)),
                FromRect(lu.Upper),
            ];
        }

        var outputs = new List<JgsValue>
        {
            FromRect(lu.Lower),
            FromRect(lu.Upper),
            PermutationOf(lu.Permutation, form, byRow: true),
        };

        // MATLAB keeps the four- and five-output forms for sparse matrices, where a column ordering
        // is what makes the factors sparse. A dense matrix needs none, so the extra outputs are the
        // identity — which is a true answer to P·A·Q = L·U·D rather than a refusal.
        if (wanted >= 4)
        {
            outputs.Add(PermutationOf(Linear.Identity(n), form, byRow: false));
        }

        if (wanted >= 5)
        {
            outputs.Add(FromRect(Linear.Identity(n)));
        }

        return [.. outputs];
    }

    /// <summary>MATLAB's one-output <c>lu</c>: the two factors in one matrix.</summary>
    private static double[,] Combined(LuDecomposition lu)
    {
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

        return combined;
    }

    private static JgsValue[] SparseLowerUpper(JgsValue value, string form, int wanted, int line, int col)
    {
        if (wanted <= 1)
        {
            throw new JgsRuntimeException(line, col,
                "lu of a sparse matrix returns its factors: use [L, U] = lu(A).");
        }

        (JGraph.Numerics.Sparse.CscMatrix lower, JGraph.Numerics.Sparse.CscMatrix upper) =
            value.AsSparse.LowerUpper();

        if (wanted == 2)
        {
            return [JgsValue.Sparse(lower), JgsValue.Sparse(upper)];
        }

        // Gilbert–Peierls here folds the row permutation into L, so the factors that come back are
        // already the permuted ones and the permutation to report alongside them is the identity.
        int n = value.AsSparse.Rows;
        var outputs = new List<JgsValue>
        {
            JgsValue.Sparse(lower),
            JgsValue.Sparse(upper),
            PermutationOf(Linear.Identity(n), form, byRow: true),
        };

        if (wanted >= 4)
        {
            outputs.Add(PermutationOf(Linear.Identity(n), form, byRow: false));
        }

        if (wanted >= 5)
        {
            outputs.Add(FromRect(Linear.Identity(n)));
        }

        return [.. outputs];
    }

    /// <summary>A permutation matrix, or the vector of indices MATLAB's <c>'vector'</c> asks for.</summary>
    private static JgsValue PermutationOf(double[,] permutation, string form, bool byRow)
    {
        if (form != "vector")
        {
            return FromRect(permutation);
        }

        int n = permutation.GetLength(0);
        var indices = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (permutation[i, j] != 0)
                {
                    if (byRow)
                    {
                        indices[i] = j + 1;
                    }
                    else
                    {
                        indices[j] = i + 1;
                    }
                }
            }
        }

        return Numbers(indices);
    }

    // --- chol -------------------------------------------------------------------------------

    /// <summary>
    /// <c>chol</c> with the second output that turns a failure into an answer: zero when the matrix
    /// is positive definite, and otherwise the order at which it stopped being so — with the factor
    /// of the leading block that is.
    /// </summary>
    private static JgsValue[] CholeskyAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("chol", args, 1, 3, line, col);

        bool lower = false;
        string form = string.Empty;
        for (int at = 1; at < args.Count; at++)
        {
            string word = Str("chol", args, at, line, col).ToLowerInvariant();
            switch (word)
            {
                case "lower":
                    lower = true;
                    break;
                case "upper":
                    lower = false;
                    break;
                case "vector":
                case "matrix":
                    form = word;
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"chol: '{word}' is not one of 'upper', 'lower', 'vector' or 'matrix'.");
            }
        }

        double[,] a = SquareRect("chol", DenseValue(args[0]), line, col);
        Cholesky cholesky = Cholesky.Factor(a);

        if (!cholesky.IsPositiveDefinite && wanted <= 1)
        {
            throw new JgsRuntimeException(line, col,
                "chol needs a symmetric positive definite matrix; this one is not. " +
                "[R, flag] = chol(A) reports that instead of raising it.");
        }

        // A failure at order q means the leading q−1 block is definite and its factor is what there
        // is to hand back — which is exactly what MATLAB returns beside a nonzero flag.
        int order = cholesky.IsPositiveDefinite ? a.GetLength(0) : cholesky.FailedAt - 1;
        double[,] factor = Leading(cholesky.Lower, order);
        JgsValue r = FromRect(lower ? factor : Linear.Transpose(factor));

        if (wanted <= 1)
        {
            return [r];
        }

        var outputs = new List<JgsValue>
        {
            r,
            JgsValue.Number(cholesky.IsPositiveDefinite ? 0 : cholesky.FailedAt),
        };

        if (wanted >= 3)
        {
            // The reordering that makes a sparse factor sparse. Nothing here reorders, so the
            // permutation is the identity and the relation Pᵀ·A·P = Rᵀ·R holds as it stands.
            outputs.Add(PermutationOf(Linear.Identity(a.GetLength(0)), form, byRow: true));
        }

        return [.. outputs];
    }

    private static double[,] Leading(double[,] m, int order)
    {
        if (order == m.GetLength(0))
        {
            return m;
        }

        var leading = new double[order, order];
        for (int i = 0; i < order; i++)
        {
            for (int j = 0; j < order; j++)
            {
                leading[i, j] = m[i, j];
            }
        }

        return leading;
    }

    // --- linsolve ---------------------------------------------------------------------------

    /// <summary>
    /// <c>linsolve(A, B)</c>, the <c>opts</c> structure that says what is known about A, and the
    /// second output that says how far the answer can be trusted.
    /// </summary>
    private static JgsValue[] LinearSolveAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("linsolve", args, 2, 3, line, col);
        double[,] a = RectOf("linsolve", args[0], line, col);
        double[,] b = ColumnsOf("linsolve", args[1], line, col);

        bool transpose = false;
        bool lower = false;
        bool upper = false;
        bool definite = false;

        if (args.Count == 3)
        {
            if (args[2].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col,
                    "linsolve: the third argument is a structure of options, such as " +
                    "struct('LT', true) for a lower triangular matrix.");
            }

            foreach ((string field, JgsValue setting) in args[2].AsStruct)
            {
                bool on = setting.IsTruthy;
                switch (field.ToUpperInvariant())
                {
                    case "TRANSA": transpose = on; break;
                    case "LT": lower = on; break;
                    case "UT": upper = on; break;
                    case "POSDEF": definite = on; break;
                    case "SYM":
                    case "RECT":
                    case "UHESS":
                        break; // known, and nothing here does differently for them
                    default:
                        throw new JgsRuntimeException(line, col,
                            $"linsolve: '{field}' is not one of LT, UT, UHESS, SYM, POSDEF, RECT or TRANSA.");
                }
            }
        }

        if (lower && upper)
        {
            throw new JgsRuntimeException(line, col,
                "linsolve: a matrix cannot be both lower and upper triangular.");
        }

        if (transpose)
        {
            a = Linear.Transpose(a);
            (lower, upper) = (upper, lower);
        }

        if (a.GetLength(0) != b.GetLength(0))
        {
            throw new JgsRuntimeException(line, col,
                $"linsolve: the right-hand side has {b.GetLength(0)} row(s) and the matrix has " +
                $"{a.GetLength(0)}.");
        }

        // A triangular flag is a promise about the matrix, and MATLAB keeps it by reading only that
        // triangle. Honouring it is the difference between the options meaning something and being
        // accepted and ignored.
        double[,] x = lower || upper
            ? SubstituteTriangular(a, b, lower, line, col)
            : Linear.Solve(definite && IsSymmetric(a) ? a : a, b);

        JgsValue solution = FromRect(x);
        if (wanted <= 1)
        {
            return [solution];
        }

        return [solution, JgsValue.Number(SolveQuality(a))];
    }

    /// <summary>
    /// The reciprocal condition number for a square matrix, and the rank for one that is not —
    /// which is what MATLAB's second output from <c>linsolve</c> reports in each case.
    /// </summary>
    private static double SolveQuality(double[,] a)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        if (m != n)
        {
            double[] singular = Svd.Factor(a).Values;
            double largest = singular.Length == 0 ? 0 : singular[0];
            foreach (double value in singular)
            {
                largest = Math.Max(largest, value);
            }

            double cutoff = Math.Max(m, n) * largest * 2.220446049250313e-16;
            int rank = 0;
            foreach (double value in singular)
            {
                if (value > cutoff)
                {
                    rank++;
                }
            }

            return rank;
        }

        LuDecomposition lu = LuDecomposition.Factor(a);
        if (lu.IsSingular)
        {
            return 0;
        }

        double product = OneNorm(a) * OneNorm(lu.Inverse());
        return product == 0 ? 0 : 1 / product;
    }

    /// <summary>Forward or back substitution, reading only the triangle the caller promised.</summary>
    private static double[,] SubstituteTriangular(double[,] a, double[,] b, bool lower, int line, int col)
    {
        int n = a.GetLength(0);
        if (n != a.GetLength(1))
        {
            throw new JgsRuntimeException(line, col,
                "linsolve: a triangular matrix must be square.");
        }

        int columns = b.GetLength(1);
        var x = new double[n, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int step = 0; step < n; step++)
            {
                int i = lower ? step : n - 1 - step;
                double sum = b[i, c];
                if (lower)
                {
                    for (int j = 0; j < i; j++)
                    {
                        sum -= a[i, j] * x[j, c];
                    }
                }
                else
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        sum -= a[i, j] * x[j, c];
                    }
                }

                if (a[i, i] == 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "linsolve: the triangular matrix has a zero on its diagonal and is singular.");
                }

                x[i, c] = sum / a[i, i];
            }
        }

        return x;
    }

    /// <summary>A matrix as a dense rectangle, filling in a sparse one on the way.</summary>
    private static double[,] DenseOf(string name, JgsValue value, int line, int col) =>
        RectOf(name, DenseValue(value), line, col);

    /// <summary>
    /// A sparse value as the dense one it stands for. The decompositions here are dense, so a sparse
    /// argument is filled in rather than refused — the factors come back dense, which is a
    /// difference in storage and not in the answer.
    /// </summary>
    private static JgsValue DenseValue(JgsValue value)
    {
        if (value.Type != JgsType.Sparse)
        {
            return value;
        }

        JGraph.Numerics.Sparse.CscMatrix sparse = value.AsSparse;
        var dense = new double[sparse.Rows, sparse.Cols];
        for (int c = 0; c < sparse.Cols; c++)
        {
            for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
            {
                dense[sparse.RowIndices[k], c] = sparse.Values[k];
            }
        }

        return FromRect(dense);
    }

    private static Complex[,] Diagonal(Complex[] values)
    {
        int n = values.Length;
        var d = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            d[i, i] = values[i];
        }

        return d;
    }

    /// <summary>
    /// The eigenvalues as a list, in the shape this build has always answered them in: a row for a
    /// real matrix and a column for a complex one.
    /// </summary>
    /// <remarks>
    /// MATLAB answers a column in both cases, and the difference is long-standing rather than new
    /// here — frozen stress scripts compare <c>sort(real(eig(B)))</c> against a row literal, and
    /// turning the row into a column would silently broadcast those comparisons into matrices. The
    /// shape is therefore left as it stands and recorded as a divergence, and the pencil form
    /// answers in the same shape as the single-matrix one so that the two agree with each other.
    /// </remarks>
    private static JgsValue EigenvalueList(Complex[] values, bool asColumn)
    {
        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = ComplexValue(values[i]);
        }

        JgsValue list = JgsValue.Array(boxed);
        if (asColumn && boxed.Length > 1)
        {
            list.Reshape(boxed.Length, 1);
        }

        return list;
    }
}
