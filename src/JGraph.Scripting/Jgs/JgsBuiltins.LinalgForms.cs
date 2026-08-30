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
            Complex[,] z = ComplexSquareOf("eig", value, line, col);
            if (wanted <= 1)
            {
                return [EigenvalueList(ComplexEigen.Values(z))];
            }

            (Complex[] zValues, Complex[,] zVectors) = ComplexEigen.Factor(z);
            JgsValue zDiagonal = asVector
                ? EigenvalueList(zValues)
                : FromComplexRect(Diagonal(zValues));
            if (wanted <= 2)
            {
                return [FromComplexRect(zVectors), zDiagonal];
            }

            return [FromComplexRect(zVectors), zDiagonal,
                FromComplexRect(ComplexLeftEigenvectors(z, zValues))];
        }

        double[] a = SquareColumnMajorOf("eig", value, out int n, line, col);
        if (wanted <= 1)
        {
            // No vectors asked for and none computed: recovering them is most of what a general
            // eigensolver does, and this form of the verb never looks at one.
            Complex[] spectrum = Eigen.Spectrum(a, n);
            return [asVector ? EigenvalueList(spectrum) : FromComplexRect(Diagonal(spectrum))];
        }

        if (wanted <= 2)
        {
            Eigen pair = Eigen.FactorAdopting(a, n);
            JgsValue values = asVector
                ? EigenvalueList(pair.Values)
                : FromComplexRect(Diagonal(pair.Values));
            return [FromComplexRect(pair.Vectors), values];
        }

        Eigen eigen = Eigen.Factor(a, n);
        JgsValue d = asVector ? EigenvalueList(eigen.Values) : FromComplexRect(Diagonal(eigen.Values));
        return [FromComplexRect(eigen.Vectors), d, FromComplexRect(LeftEigenvectors(a, n, eigen.Values))];
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
                return DefinitePencil(a, b, wanted, asVector);
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

        int order = a.GetLength(0);
        if (wanted <= 1)
        {
            Complex[] spectrum = Eigen.PencilSpectrum(FlattenSquare(a), FlattenSquare(b), order);
            return [asVector ? EigenvalueList(spectrum) : FromComplexRect(Diagonal(spectrum))];
        }

        Complex[] values;
        Complex[,] vectors;
        try
        {
            (values, vectors) = Eigen.PencilFactor(FlattenSquare(a), FlattenSquare(b), order);
        }
        catch (InvalidOperationException)
        {
            values = [new Complex(double.PositiveInfinity, 0)];
            vectors = new Complex[0, 0];
        }

        if (Array.Exists(values, static v => !double.IsFinite(v.Real) || !double.IsFinite(v.Imaginary)))
        {
            throw new JgsRuntimeException(line, col,
                "[V, D] = eig(A, B) needs a nonsingular B: this pencil has an eigenvalue at infinity, " +
                "which has no eigenvector to report. e = eig(A, B) gives the eigenvalues themselves.");
        }

        JgsValue d = asVector ? EigenvalueList(values) : FromComplexRect(Diagonal(values));
        if (wanted <= 2)
        {
            return [FromComplexRect(vectors), d];
        }

        // The left eigenvectors keep coming from B\\A's transpose, matched by conjugate value —
        // the route this form has always taken.
        double[,] reduced = Linear.Solve(b, a);
        var flat = new double[(long)order * order];
        for (int c = 0; c < order; c++)
        {
            for (int r = 0; r < order; r++)
            {
                flat[(c * order) + r] = reduced[r, c];
            }
        }

        return [FromComplexRect(vectors), d,
            FromComplexRect(LeftEigenvectors(flat, order, values))];
    }

    /// <summary>
    /// The symmetric-definite pencil through the provider's Cholesky-reduction eigensolver: real
    /// ascending eigenvalues, and vectors already scaled so that <c>Vᵀ·B·V</c> is the identity —
    /// which is what MATLAB's <c>eig(A, B)</c> hands back for this pencil.
    /// </summary>
    private static JgsValue[] DefinitePencil(double[,] a, double[,] b, int wanted, bool asVector)
    {
        int n = a.GetLength(0);
        (double[] real, double[] columns) =
            Eigen.SymmetricPencil(FlattenSquare(a), FlattenSquare(b), n, wanted > 1);

        var values = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = new Complex(real[i], 0);
        }

        JgsValue d = asVector ? EigenvalueList(values) : FromComplexRect(Diagonal(values));
        if (wanted <= 1)
        {
            return [d];
        }

        JgsValue vectors = FromColumnMajorRect(columns, n, n);
        return wanted <= 2 ? [vectors, d] : [vectors, d, vectors];
    }

    /// <summary>A square rectangle as the flat column-major array the provider fronts adopt.</summary>
    private static double[] FlattenSquare(double[,] source)
    {
        int n = source.GetLength(0);
        var flat = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                flat[(c * n) + r] = source[r, c];
            }
        }

        return flat;
    }

    /// <summary>
    /// The left eigenvectors of a complex matrix: the right eigenvectors of Aᴴ for the conjugate
    /// eigenvalues, matched by value exactly as the real form matches its transpose.
    /// </summary>
    private static Complex[,] ComplexLeftEigenvectors(Complex[,] a, Complex[] values)
    {
        int n = a.GetLength(0);
        var hermitian = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                hermitian[r, c] = Complex.Conjugate(a[c, r]);
            }
        }

        (Complex[] flipped, Complex[,] vectors) = ComplexEigen.Factor(hermitian);
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

                double distance = (flipped[j] - Complex.Conjugate(values[k])).Magnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = j;
                }
            }

            taken[best] = true;
            for (int r = 0; r < n; r++)
            {
                w[r, k] = vectors[r, best];
            }
        }

        return w;
    }

    /// <summary>
    /// The left eigenvectors: the columns W for which <c>Wᴴ·A = D·Wᴴ</c>. They are the right
    /// eigenvectors of the transpose, taken for the conjugate eigenvalue — which is why the two
    /// factorizations have to be matched up by value rather than trusted to arrive in step.
    /// </summary>
    private static Complex[,] LeftEigenvectors(ReadOnlySpan<double> a, int n, Complex[] values)
    {
        var flipped = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                flipped[(c * n) + r] = a[(r * n) + c];
            }
        }

        Eigen transposed = Eigen.FactorAdopting(flipped, n);
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
        double[] a = DenseColumnMajorOf("qr", args[0], out int rows, out int cols, line, col);

        bool economy = false;
        bool zeroFlag = false;
        string form = string.Empty;
        double[]? rhs = null;
        int rhsColumns = 0;

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
                rhs = DenseColumnMajorOf("qr", argument, out int rhsRows, out rhsColumns, line, col);
                if (rhsRows != rows)
                {
                    throw new JgsRuntimeException(line, col,
                        "qr: the right-hand side must have as many rows as the matrix.");
                }

                continue;
            }

            economy = true;
            zeroFlag = true;
        }

        // MATLAB's two economy spellings do not agree about the third output: the older literal
        // zero asks for the permutation as a vector, where 'econ' leaves it the matrix qr(A) gives.
        // A word said outright still wins over both.
        string permutation = form.Length > 0 ? form : zeroFlag ? "vector" : string.Empty;

        QrDecomposition qr = QrDecomposition.FactorAdopting(a, rows, cols, pivot: wanted >= 3);
        int reflectors = Math.Min(rows, cols);
        int qColumns = economy ? reflectors : rows;

        if (rhs is not null)
        {
            // [C, R] = qr(S, B): C is Qᵀ·B, which is all a least-squares solve needs, and R\C is
            // the solution. Q itself is never asked for and — since the reflectors can be walked
            // over B directly — never formed either.
            qr.ApplyTransposeInPlace(rhs, rhsColumns);
            JgsValue c = FromColumnMajorRect(Leading(rhs, rows, qColumns, rhsColumns), qColumns, rhsColumns);
            JgsValue r = FromColumnMajorRect(qr.RColumnMajor(!economy), qColumns, cols);
            return wanted >= 3 ? [c, r, Permutation(qr, cols, permutation)] : [c, r];
        }

        if (wanted <= 1)
        {
            return [FromColumnMajorRect(qr.RColumnMajor(!economy), qColumns, cols)];
        }

        JgsValue[] factors =
        [
            FromColumnMajorRect(qr.QColumnMajor(!economy), rows, qColumns),
            FromColumnMajorRect(qr.RColumnMajor(!economy), qColumns, cols),
        ];

        return wanted <= 2 ? factors : [factors[0], factors[1], Permutation(qr, cols, permutation)];
    }

    /// <summary>The first <paramref name="keep"/> rows of a column-major block, compacted.</summary>
    private static double[] Leading(double[] block, int rows, int keep, int columns)
    {
        if (keep == rows)
        {
            return block;
        }

        var trimmed = new double[(long)keep * columns];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(block, (long)c * rows, trimmed, (long)c * keep, keep);
        }

        return trimmed;
    }

    /// <summary>The pivoting as MATLAB's <c>outputForm</c> asked for it: a matrix, or the row of
    /// column numbers that stands for it.</summary>
    private static JgsValue Permutation(QrDecomposition qr, int order, string form)
    {
        if (form != "vector")
        {
            return FromColumnMajorRect(qr.PermutationColumnMajor(), order, order);
        }

        int[] columns = qr.PivotVector;
        var numbers = new double[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            numbers[i] = columns[i] + 1;
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

        double[] input = SquareColumnMajorOf("lu", args[0], out int n, line, col);
        LuDecomposition lu = LuDecomposition.FactorAdopting(input, n);

        // Row r of A became row `factored[r]` of the factorization, so folding the permutation back
        // into L is a row move rather than the multiply by a permutation matrix it used to be —
        // 2·n³ flops at n = 2000 to shuffle rows, which is the same answer the long way round.
        ReadOnlySpan<int> order = lu.RowPermutation;
        var factored = new int[n];
        for (int i = 0; i < n; i++)
        {
            factored[order[i]] = i;
        }

        if (wanted <= 1)
        {
            return [BuildColumnMajor(n, n, span => FillCombined(lu, factored, n, span))];
        }

        if (wanted == 2)
        {
            // [L, U] folds the permutation into L, so L*U still reassembles A.
            return
            [
                BuildColumnMajor(n, n, span => FillLower(lu, factored, n, span)),
                BuildColumnMajor(n, n, span => FillUpper(lu, n, span)),
            ];
        }

        var outputs = new List<JgsValue>
        {
            BuildColumnMajor(n, n, span => FillLower(lu, rows: null, n, span)),
            BuildColumnMajor(n, n, span => FillUpper(lu, n, span)),
            PermutationValue(order.ToArray(), form, n),
        };

        // MATLAB keeps the four- and five-output forms for sparse matrices, where a column ordering
        // is what makes the factors sparse. A dense matrix needs none, so the extra outputs are the
        // identity — which is a true answer to P·A·Q = L·U·D rather than a refusal.
        if (wanted >= 4)
        {
            outputs.Add(PermutationValue(Ascending(n), form, n));
        }

        if (wanted >= 5)
        {
            outputs.Add(FromColumnMajorRect(IdentityColumnMajor(n), n, n));
        }

        return [.. outputs];
    }

    /// <summary>MATLAB's one-output <c>lu</c>: the two factors in one matrix, PᵀL + U − I.</summary>
    private static void FillCombined(LuDecomposition lu, int[] rows, int n, Span<double> combined)
    {
        ReadOnlySpan<double> factors = lu.Factors;
        for (int c = 0; c < n; c++)
        {
            int origin = c * n;
            for (int r = 0; r < n; r++)
            {
                int source = rows[r];
                double lower = c < source ? factors[origin + source] : (c == source ? 1 : 0);
                double upper = c >= r ? factors[origin + r] : 0;
                combined[origin + r] = lower + upper - (r == c ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// The unit-lower-triangular L, optionally with its rows moved back to the order A had them in
    /// — which is what the two-output form's PᵀL is. The destination arrives zeroed.
    /// </summary>
    private static void FillLower(LuDecomposition lu, int[]? rows, int n, Span<double> lower)
    {
        ReadOnlySpan<double> factors = lu.Factors;
        for (int c = 0; c < n; c++)
        {
            int origin = c * n;
            if (rows is null)
            {
                // Column c of L is a one on the diagonal and the factored column below it; above it
                // the zeros the destination came with are already the answer.
                lower[origin + c] = 1;
                factors.Slice(origin + c + 1, n - c - 1).CopyTo(lower[(origin + c + 1)..]);
                continue;
            }

            for (int r = 0; r < n; r++)
            {
                int source = rows[r];
                if (c < source)
                {
                    lower[origin + r] = factors[origin + source];
                }
                else if (c == source)
                {
                    lower[origin + r] = 1;
                }
            }
        }
    }

    /// <summary>The upper-triangular U; the destination arrives zeroed.</summary>
    private static void FillUpper(LuDecomposition lu, int n, Span<double> upper)
    {
        ReadOnlySpan<double> factors = lu.Factors;
        for (int c = 0; c < n; c++)
        {
            int origin = c * n;
            factors.Slice(origin, c + 1).CopyTo(upper[origin..]);
        }
    }

    /// <summary>A row permutation as the matrix or the index vector the <c>form</c> word asks for.</summary>
    private static JgsValue PermutationValue(int[] order, string form, int n)
    {
        if (form == "vector")
        {
            var indices = new double[n];
            for (int i = 0; i < n; i++)
            {
                indices[i] = order[i] + 1;
            }

            return Numbers(indices);
        }

        // n ones in n² of storage: the one result where not having to write the zeros is the whole
        // cost of the operation.
        return BuildColumnMajor(n, n, span =>
        {
            for (int i = 0; i < n; i++)
            {
                span[(order[i] * n) + i] = 1;
            }
        });
    }

    /// <summary>The identity permutation 0, 1, … n−1.</summary>
    private static int[] Ascending(int n)
    {
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        return order;
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

        double[] a = SquareColumnMajorOf("chol", DenseValue(args[0]), out int n, line, col);

        // The triangle asked for is the triangle read and the triangle written, which is MATLAB's
        // own reading of 'upper' and 'lower' — and what lets the factor come back without a
        // transposing pass over it. For a symmetric matrix the two directions answer the same
        // question, so which one runs is a matter of which factor was asked for.
        Cholesky cholesky = Cholesky.FactorAdopting(a, n, lower);

        if (!cholesky.IsPositiveDefinite && wanted <= 1)
        {
            throw new JgsRuntimeException(line, col,
                "chol needs a symmetric positive definite matrix; this one is not. " +
                "[R, flag] = chol(A) reports that instead of raising it.");
        }

        // A failure at order q means the leading q−1 block is definite and its factor is what there
        // is to hand back — which is exactly what MATLAB returns beside a nonzero flag.
        int order = cholesky.IsPositiveDefinite ? n : cholesky.FailedAt - 1;
        JgsValue r = FromColumnMajorRect(Leading(cholesky.ColumnMajor, n, order), order, order);

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
            outputs.Add(PermutationValue(Ascending(n), form, n));
        }

        return [.. outputs];
    }

    /// <summary>
    /// The leading order-by-order block of an n-by-n column-major matrix. The whole factor is handed
    /// over as it stands when nothing is being cut away — the common case, and 32 MB of copying
    /// avoided at n = 2000.
    /// </summary>
    private static double[] Leading(double[] columnMajor, int n, int order)
    {
        if (order == n)
        {
            return columnMajor;
        }

        var leading = new double[(long)order * order];
        for (int c = 0; c < order; c++)
        {
            columnMajor.AsSpan(c * n, order).CopyTo(leading.AsSpan(c * order, order));
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
            var flat = new double[(long)m * n];
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < m; r++)
                {
                    flat[(c * m) + r] = a[r, c];
                }
            }

            double[] singular = Svd.SingularValues(flat, m, n);
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

        // The same estimate rcond reports, from the same call — linsolve's second output is
        // documented to be rcond(A), and a script that subtracts the two expects a zero.
        return LuDecomposition.Factor(a).ReciprocalCondition(OneNorm(a));
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
        var triangle = new double[(long)n * n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                triangle[(c * n) + r] = a[r, c];
            }
        }

        var rhs = new double[(long)n * columns];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                rhs[(c * n) + r] = b[r, c];
            }
        }

        if (LinalgProvider.Current.Trtrs(lower, transpose: false, n, columns, triangle, n, rhs, n) != 0)
        {
            throw new JgsRuntimeException(line, col,
                "linsolve: the triangular matrix has a zero on its diagonal and is singular.");
        }

        var x = new double[n, columns];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                x[r, c] = rhs[(c * n) + r];
            }
        }

        return x;
    }

    /// <summary>
    /// A matrix as flat column-major doubles, filling in a sparse one on the way. The boxed
    /// rectangle this replaced had no callers left once <c>qr</c> stopped asking for one.
    /// </summary>
    private static double[] DenseColumnMajorOf(
        string name, JgsValue value, out int rows, out int cols, int line, int col) =>
        ColumnMajorOf(name, DenseValue(value), out rows, out cols, line, col);

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

    /// <summary>The eigenvalues as MATLAB's column, whatever route computed them.</summary>
    /// <remarks>
    /// A real matrix used to answer a row here and a complex one a column, which made the shape of
    /// an answer depend on a property of the argument no caller looks at. The row was the worse
    /// half: implicit expansion turns a mismatched orientation from an error into an outer product,
    /// so <c>eig(A) - b</c> against a column answered a plausible matrix rather than raising.
    /// </remarks>
    private static JgsValue EigenvalueList(Complex[] values)
    {
        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = ComplexValue(values[i]);
        }

        JgsValue list = JgsValue.Array(boxed);
        if (boxed.Length > 1)
        {
            list.Reshape(boxed.Length, 1);
        }

        return list;
    }
}
