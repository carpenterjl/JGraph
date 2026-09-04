using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The real generalized Schur (QZ) factorization of a matrix pencil: orthogonal <c>Q</c> and
/// <c>Z</c> with <c>Q·A·Z</c> quasi-upper-triangular and <c>Q·B·Z</c> upper triangular.
/// </summary>
/// <remarks>
/// <para>
/// The eigenvalues of the pencil are the ratios of the diagonals, reported as a pair
/// (<see cref="Alpha"/>, <see cref="Beta"/>) rather than as a quotient. That is the whole reason
/// this exists. A pencil whose <c>B</c> is singular has eigenvalues at infinity, and the only
/// honest way to say so is a zero denominator — which is why the earlier construction, taking the
/// ordinary Schur form of <c>B⁻¹A</c>, could not answer for a singular <c>B</c> and refused by name.
/// </para>
/// <para>
/// Three phases, in the classical order. <c>B</c> is made upper triangular by a QR factorization;
/// the pair is then reduced to Hessenberg-triangular form by Givens rotations in pairs — one from
/// the left to put a zero into <c>A</c>, one from the right to take back out of <c>B</c> the fill
/// that the first one caused; and the iteration then walks the subdiagonal to zero with implicit
/// double shifts, which is the same rotation pair applied column by column and so is written here
/// as the same loop. A zero on <c>B</c>'s diagonal is chased to the bottom of the active block and
/// deflated there as an infinite eigenvalue.
/// </para>
/// <para>
/// The convention is JGraph's rather than LAPACK's: <c>Q·A·Z = AA</c>, with <c>Q</c> already
/// transposed, because that is the relation the <c>qz</c> builtin has documented and tested since
/// M66.
/// </para>
/// </remarks>
public sealed class GeneralizedSchur
{
    private const int IterationsPerEigenvalue = 40;

    private GeneralizedSchur(double[,] aa, double[,] bb, double[,] q, double[,] z,
        Complex[] alpha, double[] beta)
    {
        AA = aa;
        BB = bb;
        Q = q;
        Z = z;
        Alpha = alpha;
        Beta = beta;
    }

    /// <summary>The quasi-upper-triangular factor, <c>Q·A·Z</c>.</summary>
    public double[,] AA { get; }

    /// <summary>The upper-triangular factor, <c>Q·B·Z</c>.</summary>
    public double[,] BB { get; }

    /// <summary>The left orthogonal factor, in the convention <c>Q·A·Z = AA</c>.</summary>
    public double[,] Q { get; }

    /// <summary>The right orthogonal factor.</summary>
    public double[,] Z { get; }

    /// <summary>The numerators of the eigenvalues, one per column of the factorization.</summary>
    public Complex[] Alpha { get; }

    /// <summary>
    /// The denominators of the eigenvalues. A zero entry is an eigenvalue at infinity — a direction
    /// in which the pencil degenerates — and is the case that the whole iteration exists to answer.
    /// </summary>
    public double[] Beta { get; }

    /// <summary>The eigenvalues themselves, infinite where <see cref="Beta"/> is zero.</summary>
    public Complex[] Eigenvalues
    {
        get
        {
            var values = new Complex[Alpha.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Beta[i] == 0
                    ? new Complex(double.PositiveInfinity, 0)
                    : Alpha[i] / Beta[i];
            }

            return values;
        }
    }

    /// <summary>Whether every eigenvalue is finite — equivalently, whether <c>B</c> is nonsingular.</summary>
    public bool IsFinite => Array.TrueForAll(Beta, static b => b != 0);

    /// <summary>Factors the pencil (<paramref name="a"/>, <paramref name="b"/>), both n-by-n.</summary>
    /// <exception cref="ArgumentException">
    /// The matrices are not square and the same size, or the pencil is singular — every λ an
    /// eigenvalue, which is a degeneracy of the pair rather than a shape this can answer in.
    /// </exception>
    public static GeneralizedSchur Factor(double[,] a, double[,] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int n = a.GetLength(0);
        if (a.GetLength(1) != n || b.GetLength(0) != n || b.GetLength(1) != n)
        {
            throw new ArgumentException("A generalized Schur form needs two square matrices of the same size.");
        }

        if (n == 0)
        {
            return new GeneralizedSchur(new double[0, 0], new double[0, 0], new double[0, 0],
                new double[0, 0], [], []);
        }

        double[] aa = Flatten(a, n);
        double[] bb = Flatten(b, n);
        var vsl = new double[(long)n * n];
        var vsr = new double[(long)n * n];
        var alphar = new double[n];
        var alphai = new double[n];
        var beta = new double[n];
        if (LinalgProvider.Current.Gges(vectors: true, n, aa, n, bb, n,
                alphar, alphai, beta, vsl, n, vsr, n) != 0)
        {
            throw new ArgumentException(
                "This pencil is singular — every number is an eigenvalue of it — so it has no " +
                "generalized Schur form to compute.");
        }

        // The blocked iteration leaves rounding where the managed kernel promises an exact zero: a
        // singular B's eigenvalue at infinity must have beta exactly 0, not 1e-17 - arriving at
        // 1e-17 would make the answer say the pencil is finite, which is the one thing it is not.
        // The snap rule is the managed kernel's own, applied to the same numbers.
        double largest = 0;
        foreach (double entry in bb)
        {
            largest = Math.Max(largest, Math.Abs(entry));
        }

        double tolerance = 1e-12 * (1 + largest);
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(beta[i]) <= tolerance)
            {
                beta[i] = 0;
            }

            if (Math.Abs(bb[(i * n) + i]) <= tolerance)
            {
                bb[(i * n) + i] = 0;
            }
        }

        var alpha = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            alpha[i] = new Complex(alphar[i], alphai[i]);
        }

        // The kernel convention here is Q·A·Z = AA; the provider hands back A = VSL·AA·VSRᵀ, so
        // the left factor comes across transposed and the right one comes across as it stands.
        return new GeneralizedSchur(Rebuild(aa, n), Rebuild(bb, n),
            RebuildTransposed(vsl, n), Rebuild(vsr, n), alpha, beta);
    }

    /// <summary>
    /// The managed QZ behind <see cref="Factor"/>, which <see cref="ManagedLinalg"/> reaches
    /// directly. A singular B is not a separate case: a zero on its diagonal is chased to the
    /// bottom of the active block and deflated there, as LAPACK's <c>dhgeqz</c> does it, so an
    /// eigenvalue at infinity is reported with the sign the native route reports.
    /// </summary>
    internal static GeneralizedSchur FactorManaged(double[,] a, double[,] b) => Iterated(a, b);

    private static double[] Flatten(double[,] source, int n)
    {
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

    private static double[,] Rebuild(double[] flat, int n)
    {
        var rect = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                rect[r, c] = flat[(c * n) + r];
            }
        }

        return rect;
    }

    private static double[,] RebuildTransposed(double[] flat, int n)
    {
        var rect = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                rect[c, r] = flat[(c * n) + r];
            }
        }

        return rect;
    }

    /// <summary>The Frobenius norm of B on the way in, which no rotation changes: the yardstick a negligible diagonal is measured against.</summary>
    private static double FrobeniusNorm(double[,] m)
    {
        double sum = 0;
        foreach (double value in m)
        {
            sum += value * value;
        }

        return Math.Sqrt(sum);
    }

    /// <summary>The three phases in order: B triangular, A Hessenberg beside it, then the iteration.</summary>
    /// <remarks>
    /// Before any of them, LAPACK's <c>dggbal</c> permutation: a row or a column with at most one
    /// nonzero in both matrices already holds an eigenvalue, and is moved to the edge so the three
    /// phases work on the block between. That is not an economy here but a convention. The QR runs
    /// on the block alone, which decides the reflectors, which decide the signs the eigenvalues at
    /// infinity come out with — and the native route's signs are the ones being matched.
    /// </remarks>
    private static GeneralizedSchur Iterated(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        var work = (double[,])a.Clone();
        var triangular = (double[,])b.Clone();
        (int[] rowOrder, int[] columnOrder) = Isolate(work, triangular, out int low, out int high);

        // Phase one: B triangular over the block. Its QR gives the left factor that does it, and A
        // comes along; outside the block both are triangular already.
        int rows = high - low + 1;
        int columns = n - low;
        var block = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                block[r, c] = triangular[low + r, low + c];
            }
        }

        double[,] blockQ = QrDecomposition.Factor(block).FullQ;
        ApplyTransposeToRows(blockQ, work, low);
        ApplyTransposeToRows(blockQ, triangular, low);
        ZeroBelowDiagonal(triangular, 1);

        // The accumulating factors start as the permutations, with the block's Q folded into the
        // left one — which holds the transpose of Q, as every rotation below expects.
        var qAccumulated = new double[n, n];
        var zAccumulated = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            zAccumulated[columnOrder[i], i] = 1;
            if (i < low || i > high)
            {
                qAccumulated[rowOrder[i], i] = 1;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < rows; c++)
            {
                qAccumulated[rowOrder[low + r], low + c] = blockQ[r, c];
            }
        }

        // Phase two: A to Hessenberg over the block, B kept triangular.
        for (int column = low; column < high - 1; column++)
        {
            ReduceColumn(work, triangular, qAccumulated, zAccumulated, column, column + 2, high);
        }

        // Phase three: the iteration.
        Iterate(work, triangular, qAccumulated, zAccumulated, n);

        double[,] q = Linear.Transpose(qAccumulated);
        (Complex[] alpha, double[] beta) = EigenvaluePairs(work, triangular);
        return new GeneralizedSchur(work, triangular, q, zAccumulated, alpha, beta);
    }

    /// <summary>
    /// LAPACK's <c>dggbal</c> permutation. A row with at most one nonzero across both matrices, in
    /// the columns still in play, is swapped to the bottom along with the column that nonzero
    /// stands in; then a column with at most one nonzero in the rows still in play is swapped to
    /// the front along with its row. Each swap isolates one eigenvalue, and the search restarts
    /// after each. The pair is permuted in place; the answer is the block that is left and, for
    /// each position, the original row and column now standing there.
    /// </summary>
    private static (int[] Rows, int[] Columns) Isolate(double[,] a, double[,] b, out int low, out int high)
    {
        int n = a.GetLength(0);
        var rows = new int[n];
        var columns = new int[n];
        for (int i = 0; i < n; i++)
        {
            rows[i] = i;
            columns[i] = i;
        }

        int first = 0;
        int last = n - 1;
        bool swapped = true;
        while (swapped && last > 0)
        {
            swapped = false;
            for (int i = last; i >= 0; i--)
            {
                if (!LoneNonzero(a, b, i, 0, last, byRow: true, out int at))
                {
                    continue;
                }

                SwapRows(a, b, rows, i, last);
                SwapColumns(a, b, columns, at < 0 ? last : at, last);
                last--;
                swapped = true;
                break;
            }
        }

        swapped = true;
        while (swapped && first < last)
        {
            swapped = false;
            for (int j = first; j <= last; j++)
            {
                if (!LoneNonzero(a, b, j, first, last, byRow: false, out int at))
                {
                    continue;
                }

                SwapColumns(a, b, columns, j, first);
                SwapRows(a, b, rows, at < 0 ? last : at, first);
                first++;
                swapped = true;
                break;
            }
        }

        low = first;
        high = last;
        return (rows, columns);
    }

    /// <summary>
    /// Whether row (or column) <paramref name="index"/> has at most one nonzero in either matrix
    /// over positions <paramref name="from"/>..<paramref name="to"/>, and where it is: −1 for none.
    /// </summary>
    private static bool LoneNonzero(double[,] a, double[,] b, int index, int from, int to, bool byRow, out int at)
    {
        at = -1;
        for (int k = from; k <= to; k++)
        {
            bool zero = byRow
                ? a[index, k] == 0 && b[index, k] == 0
                : a[k, index] == 0 && b[k, index] == 0;
            if (zero)
            {
                continue;
            }

            if (at >= 0)
            {
                return false;
            }

            at = k;
        }

        return true;
    }

    private static void SwapRows(double[,] a, double[,] b, int[] order, int i, int m)
    {
        if (i == m)
        {
            return;
        }

        int n = a.GetLength(1);
        for (int j = 0; j < n; j++)
        {
            (a[i, j], a[m, j]) = (a[m, j], a[i, j]);
            (b[i, j], b[m, j]) = (b[m, j], b[i, j]);
        }

        (order[i], order[m]) = (order[m], order[i]);
    }

    private static void SwapColumns(double[,] a, double[,] b, int[] order, int j, int m)
    {
        if (j == m)
        {
            return;
        }

        int n = a.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            (a[i, j], a[i, m]) = (a[i, m], a[i, j]);
            (b[i, j], b[i, m]) = (b[i, m], b[i, j]);
        }

        (order[j], order[m]) = (order[m], order[j]);
    }

    /// <summary>Rows <paramref name="low"/> onwards of <paramref name="m"/>, as many as the block has, become Qᵀ times themselves.</summary>
    private static void ApplyTransposeToRows(double[,] blockQ, double[,] m, int low)
    {
        int rows = blockQ.GetLength(0);
        int columns = m.GetLength(1);
        var mixed = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double sum = 0;
                for (int k = 0; k < rows; k++)
                {
                    sum += blockQ[k, r] * m[low + k, c];
                }

                mixed[r, c] = sum;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                m[low + r, c] = mixed[r, c];
            }
        }
    }

    /// <summary>
    /// The same factorization with the eigenvalues <paramref name="select"/> marks moved to the
    /// front, in the order they already stand in — MATLAB's <c>ordqz</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A selected eigenvalue belongs to a 2-by-2 block. Splitting a conjugate pair would take the
    /// factorization out of the reals, and moving the pair as a unit is not what a per-eigenvalue
    /// selection asks for, so the case is refused rather than approximated.
    /// </exception>
    public GeneralizedSchur Reordered(bool[] select)
    {
        ArgumentNullException.ThrowIfNull(select);
        int n = AA.GetLength(0);
        if (select.Length != n)
        {
            throw new ArgumentException("The selection needs one entry per eigenvalue.", nameof(select));
        }

        var aa = (double[,])AA.Clone();
        var bb = (double[,])BB.Clone();
        var q = Linear.Transpose(Q);   // back into the accumulating convention
        var z = (double[,])Z.Clone();
        var wanted = (bool[])select.Clone();

        // A selection sort over adjacent swaps: the next wanted eigenvalue is walked up to the
        // front one position at a time, which is the only motion a 2-by-2 rotation can make.
        int target = 0;
        for (int i = 0; i < n; i++)
        {
            if (!wanted[i])
            {
                continue;
            }

            for (int at = i; at > target; at--)
            {
                if (IsBlockStart(aa, at) || IsBlockStart(aa, at - 1))
                {
                    throw new InvalidOperationException(
                        "Reordering a generalized Schur form here moves one eigenvalue at a time, and " +
                        "the selection falls inside a 2-by-2 block holding a conjugate pair.");
                }

                SwapAdjacent(aa, bb, q, z, at - 1);
                (wanted[at - 1], wanted[at]) = (wanted[at], wanted[at - 1]);
            }

            target++;
        }

        (Complex[] alpha, double[] beta) = EigenvaluePairs(aa, bb);
        return new GeneralizedSchur(aa, bb, Linear.Transpose(q), z, alpha, beta);
    }

    // --- the iteration ----------------------------------------------------------------------

    /// <summary>One unit in the last place — LAPACK's <c>dlamch('P')</c>.</summary>
    private const double Ulp = 2.220446049250313e-16;

    private static void Iterate(double[,] a, double[,] b, double[,] q, double[,] z, int n)
    {
        // What a diagonal of B has to be smaller than to count as zero: LAPACK's btol.
        double bTolerance = Math.Max(double.Epsilon, Ulp * FrobeniusNorm(b));

        int high = n - 1;
        int budget = IterationsPerEigenvalue * n;
        while (high >= 0 && budget-- > 0)
        {
            // Everything negligible below the diagonal is made exactly zero, so that what is left
            // describes one unreduced block and the search for it cannot be fooled by dust.
            for (int i = 1; i <= high; i++)
            {
                double scale = Math.Abs(a[i - 1, i - 1]) + Math.Abs(a[i, i]);
                if (Math.Abs(a[i, i - 1]) <= double.Epsilon + (1e-15 * scale))
                {
                    a[i, i - 1] = 0;
                }
            }

            if (high == 0 || a[high, high - 1] == 0)
            {
                // A 1-by-1 block has converged. Its denominator is made non-negative before it is
                // read, so that every sign in the answer is the numerator's — LAPACK's convention,
                // and the one the ratios are read under.
                MakeDenominatorNonNegative(a, b, z, high, high + 1);
                high--;
                continue;
            }

            if (Math.Abs(b[high, high]) <= bTolerance)
            {
                // An eigenvalue at infinity, already at the bottom: one rotation of the last two
                // columns clears A's subdiagonal there, and the block is a 1-by-1 over a zero.
                b[high, high] = 0;
                SplitInfiniteAtBottom(a, b, z, high);
                MakeDenominatorNonNegative(a, b, z, high, high + 1);
                high--;
                continue;
            }

            // Up to the top of the unreduced block, watching B's diagonal on the way: a zero there
            // cannot be iterated over and is chased to the bottom instead, where the pass above
            // deflates it.
            int low = high - 1;
            bool chased = false;
            while (true)
            {
                bool splitAbove = low == 0 || a[low, low - 1] == 0;
                if (Math.Abs(b[low, low]) <= bTolerance)
                {
                    b[low, low] = 0;
                    if (splitAbove)
                    {
                        ChaseWithRows(a, b, q, low, high, bTolerance);
                    }
                    else
                    {
                        ChaseWithRowsAndColumns(a, b, q, z, low, high);
                    }

                    chased = true;
                    break;
                }

                if (splitAbove)
                {
                    break;
                }

                low--;
            }

            if (chased)
            {
                continue;
            }

            if (low == high - 1)
            {
                // Two eigenvalues, either a conjugate pair that stays as a block or a real pair the
                // form is meant to separate.
                if (Split2x2(a, b, q, z, low))
                {
                    continue; // two 1-by-1 blocks now, read by the next two passes
                }

                MakeDenominatorNonNegative(a, b, z, low, low + 2);
                MakeDenominatorNonNegative(a, b, z, low + 1, low + 2);
                high = low - 1;
                continue;
            }

            DoubleShiftStep(a, b, q, z, low, high);
        }

        ZeroBelowDiagonal(b, 1);
        ZeroBelowDiagonal(a, 2);
    }

    /// <summary>
    /// One implicit double-shift sweep. The shift is the trailing 2-by-2 pencil's own pair of
    /// eigenvalues, and it never has to be computed as a root: only their sum and product enter, and
    /// both are real even when the pair is not.
    /// </summary>
    private static void DoubleShiftStep(double[,] a, double[,] b, double[,] q, double[,] z,
        int low, int high)
    {
        double a11 = a[high - 1, high - 1];
        double a12 = a[high - 1, high];
        double a21 = a[high, high - 1];
        double a22 = a[high, high];
        double b11 = b[high - 1, high - 1];
        double b12 = b[high - 1, high];
        double b22 = b[high, high];

        double quadratic = b11 * b22;
        double linear = -((a11 * b22) + (a22 * b11) - (b12 * a21));
        double constant = (a11 * a22) - (a12 * a21);

        double sum;
        double product;
        if (Math.Abs(quadratic) <= double.Epsilon)
        {
            // The trailing block is degenerate. An exceptional shift built from the block's own size
            // moves the iteration off the point without pretending to know where the eigenvalues are.
            sum = Math.Abs(a[high, high - 1]) + Math.Abs(a[high - 1, high - 2]);
            product = 0;
        }
        else
        {
            sum = -linear / quadratic;
            product = constant / quadratic;
        }

        // The first column of (M − λ₁)(M − λ₂)·e₁ for M = A·B⁻¹, which never forms M: B is upper
        // triangular, so B⁻¹e₁ is a scaled e₁ and one back-substitution gives the rest.
        double first = b[low, low];
        if (first == 0)
        {
            first = double.Epsilon;
        }

        double u0 = a[low, low] / first;
        double u1 = a[low + 1, low] / first;

        double w1 = u1 / NonZero(b[low + 1, low + 1]);
        double w0 = (u0 - (b[low, low + 1] * w1)) / NonZero(b[low, low]);

        double x0 = (a[low, low] * w0) + (a[low, low + 1] * w1) - (sum * u0) + product;
        double x1 = (a[low + 1, low] * w0) + (a[low + 1, low + 1] * w1) - (sum * u1);
        double x2 = a[low + 2, low + 1] * w1;

        // Two rotations put that vector on the first axis; applying them is what starts the bulge.
        (double c1, double s1) = RotationZeroingSecond(x1, x2);
        ApplyLeft(a, low + 1, low + 2, c1, s1);
        ApplyLeft(b, low + 1, low + 2, c1, s1);
        ApplyRight(q, low + 1, low + 2, c1, s1);

        (double c0, double s0) = RotationZeroingSecond(x0, (c1 * x1) + (s1 * x2));
        ApplyLeft(a, low, low + 1, c0, s0);
        ApplyLeft(b, low, low + 1, c0, s0);
        ApplyRight(q, low, low + 1, c0, s0);

        // Those two rotations mixed three of B's rows, so B is no longer triangular in the corner
        // they touched. Three rotations of columns take that back out, bottom-left first so that no
        // later one refills an earlier one's zero — and the fill they leave in A is the bulge the
        // chase below exists to remove.
        ClearRight(a, b, z, low + 2, low, low + 1);
        ClearRight(a, b, z, low + 2, low + 1, low + 2);
        ClearRight(a, b, z, low + 1, low, low + 1);

        // ...and restoring the form column by column is what chases it off the bottom. The same
        // rotation pair as the Hessenberg-triangular reduction, which is the content of the
        // implicit-Q theorem written out.
        for (int column = low; column <= high - 2; column++)
        {
            ReduceColumn(a, b, q, z, column, column + 2, high);
        }
    }

    /// <summary>
    /// Puts zeros into column <paramref name="column"/> of A from row <paramref name="from"/> down
    /// to <paramref name="to"/>, taking back out of B the fill each one causes.
    /// </summary>
    private static void ReduceColumn(double[,] a, double[,] b, double[,] q, double[,] z,
        int column, int from, int to)
    {
        for (int i = to; i >= from; i--)
        {
            if (a[i, column] == 0)
            {
                continue;
            }

            (double c, double s, _) = Rotation(a[i - 1, column], a[i, column]);
            ApplyLeft(a, i - 1, i, c, s);
            ApplyLeft(b, i - 1, i, c, s);
            ApplyRight(q, i - 1, i, c, s);
            a[i, column] = 0;

            // The left rotation mixed two of B's rows, so the lower one now reaches one column
            // further left than a triangle allows. A rotation of those two columns puts it back,
            // and cannot disturb the zero just made in A because it does not touch that column.
            ClearRight(a, b, z, i, i - 1, i);
        }
    }

    /// <summary>
    /// Clears B's entry at (<paramref name="row"/>, <paramref name="p"/>) with a rotation of columns
    /// <paramref name="p"/> and <paramref name="q"/>, carrying A and the accumulating Z along.
    /// </summary>
    private static void ClearRight(double[,] a, double[,] b, double[,] z, int row, int p, int q)
    {
        if (b[row, p] == 0)
        {
            return;
        }

        // dlartg's way round: the entry that stays is the rotation's f, and keeps its sign.
        (double c, double s, _) = Rotation(b[row, q], b[row, p]);
        ApplyRight(a, q, p, c, s);
        ApplyRight(b, q, p, c, s);
        ApplyRight(z, q, p, c, s);
        b[row, p] = 0;
    }

    /// <summary>
    /// B's last diagonal is zero: a rotation of the last two columns clears A's subdiagonal there,
    /// which leaves an infinite eigenvalue as a 1-by-1 block whose numerator is what the rotation
    /// put on A's diagonal — with <c>dlartg</c>'s sign, so the infinity carries the sign LAPACK's
    /// does.
    /// </summary>
    private static void SplitInfiniteAtBottom(double[,] a, double[,] b, double[,] z, int high)
    {
        (double c, double s, double r) = Rotation(a[high, high], a[high, high - 1]);
        a[high, high] = r;
        a[high, high - 1] = 0;
        RotateColumns(a, high, high - 1, c, s, high);
        RotateColumns(b, high, high - 1, c, s, high);
        RotateColumns(z, high, high - 1, c, s, z.GetLength(0));
    }

    /// <summary>
    /// A zero on B's diagonal at <paramref name="low"/>, where A is already split above it: row
    /// rotations walk the split down A one row at a time, and stop early where B's diagonal below
    /// turns out not to be zero — the block from there down is regular and is iterated on its
    /// own, and the one above ends in the zero, deflated when the bottom reaches it.
    /// </summary>
    private static void ChaseWithRows(double[,] a, double[,] b, double[,] q, int low, int high,
        double tolerance)
    {
        for (int jch = low; jch < high; jch++)
        {
            (double c, double s, double r) = Rotation(a[jch, jch], a[jch + 1, jch]);
            a[jch, jch] = r;
            a[jch + 1, jch] = 0;
            RotateRows(a, jch, jch + 1, c, s, jch + 1);
            RotateRows(b, jch, jch + 1, c, s, jch + 1);
            ApplyRight(q, jch, jch + 1, c, s);
            if (Math.Abs(b[jch + 1, jch + 1]) >= tolerance)
            {
                return;
            }

            b[jch + 1, jch + 1] = 0;
        }
    }

    /// <summary>
    /// A zero on B's diagonal at <paramref name="low"/> inside an unreduced block: a row rotation
    /// moves the zero one place down B's diagonal, which puts fill below A's subdiagonal, and a
    /// column rotation takes the fill back out — repeated until the zero stands at the bottom.
    /// </summary>
    private static void ChaseWithRowsAndColumns(double[,] a, double[,] b, double[,] q, double[,] z,
        int low, int high)
    {
        int n = a.GetLength(0);
        for (int jch = low; jch < high; jch++)
        {
            (double c, double s, double r) = Rotation(b[jch, jch + 1], b[jch + 1, jch + 1]);
            b[jch, jch + 1] = r;
            b[jch + 1, jch + 1] = 0;
            RotateRows(b, jch, jch + 1, c, s, jch + 2);
            RotateRows(a, jch, jch + 1, c, s, jch - 1);
            ApplyRight(q, jch, jch + 1, c, s);

            (c, s, r) = Rotation(a[jch + 1, jch], a[jch + 1, jch - 1]);
            a[jch + 1, jch] = r;
            a[jch + 1, jch - 1] = 0;
            RotateColumns(a, jch, jch - 1, c, s, jch + 1);
            RotateColumns(b, jch, jch - 1, c, s, jch);
            RotateColumns(z, jch, jch - 1, c, s, n);
        }
    }

    /// <summary>
    /// Negates column <paramref name="column"/> of the pair, and of Z, when B's diagonal there is
    /// negative — a right transformation, so the factorization still holds — which is what makes
    /// every denominator non-negative and every sign in an eigenvalue the numerator's.
    /// </summary>
    private static void MakeDenominatorNonNegative(double[,] a, double[,] b, double[,] z, int column, int rows)
    {
        if (b[column, column] >= 0)
        {
            return;
        }

        for (int i = 0; i < rows; i++)
        {
            a[i, column] = -a[i, column];
            b[i, column] = -b[i, column];
        }

        int n = z.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            z[i, column] = -z[i, column];
        }
    }

    /// <summary>
    /// Triangularizes a 2-by-2 block whose eigenvalues are real, and says so; one whose eigenvalues
    /// are a conjugate pair is left alone — which is what makes the result quasi-triangular rather
    /// than triangular, and is the only shape a real factorization can have.
    /// </summary>
    private static bool Split2x2(double[,] a, double[,] b, double[,] q, double[,] z, int at)
    {
        (double sum, double product, bool real) = BlockShift(a, b, at);
        if (!real)
        {
            return false;
        }

        double root = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * product)));

        // The root further from zero is the stable one to form directly; either may be used, and
        // this one keeps the subtraction out of the numerator.
        double lambda = sum >= 0 ? (sum + root) / 2 : (sum - root) / 2;
        PlaceEigenvalueFirst(a, b, q, z, at, lambda);
        return true;
    }

    /// <summary>Exchanges the two 1-by-1 blocks at <paramref name="at"/> and the one after it.</summary>
    private static void SwapAdjacent(double[,] a, double[,] b, double[,] q, double[,] z, int at)
    {
        double denominator = b[at + 1, at + 1];
        double lambda = denominator == 0
            ? double.PositiveInfinity
            : a[at + 1, at + 1] / denominator;

        if (double.IsInfinity(lambda))
        {
            // An infinite eigenvalue moves by making B's column degenerate first; the same rotation
            // pair does it with the roles of A and B exchanged.
            PlaceDegenerateFirst(a, b, q, z, at, useA: false);
            return;
        }

        PlaceEigenvalueFirst(a, b, q, z, at, lambda);
    }

    /// <summary>
    /// Rotates the 2-by-2 block at <paramref name="at"/> so that <paramref name="lambda"/> stands
    /// first on the diagonal. The block's eigenvector for λ becomes the first column, after which
    /// A's and B's first columns are parallel and one rotation clears both subdiagonals at once.
    /// </summary>
    private static void PlaceEigenvalueFirst(double[,] a, double[,] b, double[,] q, double[,] z,
        int at, double lambda)
    {
        double c00 = a[at, at] - (lambda * b[at, at]);
        double c01 = a[at, at + 1] - (lambda * b[at, at + 1]);
        double c10 = a[at + 1, at] - (lambda * b[at + 1, at]);
        double c11 = a[at + 1, at + 1] - (lambda * b[at + 1, at + 1]);

        // The eigenvector is the null direction of that singular 2-by-2; the better-conditioned of
        // its two rows names it.
        (double v0, double v1) = Math.Abs(c00) + Math.Abs(c01) >= Math.Abs(c10) + Math.Abs(c11)
            ? (-c01, c00)
            : (-c11, c10);

        double length = Math.Sqrt((v0 * v0) + (v1 * v1));
        if (length == 0)
        {
            return; // already split, or nothing a rotation can do
        }

        ApplyRight(a, at, at + 1, v0 / length, v1 / length);
        ApplyRight(b, at, at + 1, v0 / length, v1 / length);
        ApplyRight(z, at, at + 1, v0 / length, v1 / length);

        PlaceDegenerateFirst(a, b, q, z, at,
            useA: Math.Abs(a[at, at]) + Math.Abs(a[at + 1, at])
                >= Math.Abs(b[at, at]) + Math.Abs(b[at + 1, at]));
    }

    /// <summary>
    /// Clears the subdiagonal of both matrices at <paramref name="at"/> with one rotation of rows,
    /// chosen from whichever of the two has the column worth measuring.
    /// </summary>
    private static void PlaceDegenerateFirst(double[,] a, double[,] b, double[,] q, double[,] z,
        int at, bool useA)
    {
        _ = z;
        double[,] source = useA ? a : b;
        (double c, double s) = RotationZeroingSecond(source[at, at], source[at + 1, at]);
        ApplyLeft(a, at, at + 1, c, s);
        ApplyLeft(b, at, at + 1, c, s);
        ApplyRight(q, at, at + 1, c, s);
        a[at + 1, at] = 0;
        b[at + 1, at] = 0;
    }

    // --- reading the answer off -----------------------------------------------------------------

    private static bool IsBlockStart(double[,] a, int i) =>
        i + 1 < a.GetLength(0) && a[i + 1, i] != 0;

    /// <summary>The trailing 2-by-2 pencil's eigenvalue sum and product, and whether they are real.</summary>
    private static (double Sum, double Product, bool Real) BlockShift(double[,] a, double[,] b, int at)
    {
        double quadratic = b[at, at] * b[at + 1, at + 1];
        double linear = -((a[at, at] * b[at + 1, at + 1]) + (a[at + 1, at + 1] * b[at, at])
            - (b[at, at + 1] * a[at + 1, at]));
        double constant = (a[at, at] * a[at + 1, at + 1]) - (a[at, at + 1] * a[at + 1, at]);

        if (Math.Abs(quadratic) <= double.Epsilon)
        {
            return (0, 0, false);
        }

        double sum = -linear / quadratic;
        double product = constant / quadratic;
        return (sum, product, (sum * sum) - (4 * product) >= 0);
    }

    private static (Complex[] Alpha, double[] Beta) EigenvaluePairs(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        var alpha = new Complex[n];
        var beta = new double[n];

        for (int i = 0; i < n; i++)
        {
            if (!IsBlockStart(a, i))
            {
                alpha[i] = new Complex(a[i, i], 0);
                beta[i] = b[i, i];
                continue;
            }

            // A 2-by-2 block holds a conjugate pair. Its two roots are read off the same quadratic
            // the shift uses, and reported as numerator and denominator so that a degenerate block
            // still has somewhere to put an infinite eigenvalue.
            double quadratic = b[i, i] * b[i + 1, i + 1];
            double linear = -((a[i, i] * b[i + 1, i + 1]) + (a[i + 1, i + 1] * b[i, i])
                - (b[i, i + 1] * a[i + 1, i]));
            double constant = (a[i, i] * a[i + 1, i + 1]) - (a[i, i + 1] * a[i + 1, i]);
            double discriminant = (linear * linear) - (4 * quadratic * constant);

            if (discriminant >= 0)
            {
                double root = Math.Sqrt(discriminant);
                alpha[i] = new Complex(-linear + root, 0);
                alpha[i + 1] = new Complex(-linear - root, 0);
            }
            else
            {
                double imaginary = Math.Sqrt(-discriminant);
                alpha[i] = new Complex(-linear, imaginary);
                alpha[i + 1] = new Complex(-linear, -imaginary);
            }

            beta[i] = 2 * quadratic;
            beta[i + 1] = 2 * quadratic;
            i++;
        }

        return (alpha, beta);
    }

    // --- rotations ------------------------------------------------------------------------------

    /// <summary>
    /// LAPACK's <c>dlartg</c>: the rotation taking (f, g) to (r, 0), with r carrying f's sign and
    /// c never negative. The convention is the point — it decides the sign an eigenvalue at
    /// infinity is reported with — so the deflations follow it.
    /// </summary>
    private static (double C, double S, double R) Rotation(double f, double g)
    {
        if (g == 0)
        {
            return (1, 0, f);
        }

        if (f == 0)
        {
            return (0, Math.Sign(g), Math.Abs(g));
        }

        double d = Math.Sqrt((f * f) + (g * g));
        double r = Math.CopySign(d, f);
        return (Math.Abs(f) / d, g / r, r);
    }

    /// <summary>Rotates rows <paramref name="p"/> and <paramref name="q"/> from column <paramref name="fromColumn"/> on.</summary>
    private static void RotateRows(double[,] m, int p, int q, double c, double s, int fromColumn)
    {
        int columns = m.GetLength(1);
        for (int j = Math.Max(0, fromColumn); j < columns; j++)
        {
            double top = m[p, j];
            double bottom = m[q, j];
            m[p, j] = (c * top) + (s * bottom);
            m[q, j] = (c * bottom) - (s * top);
        }
    }

    /// <summary>Rotates columns <paramref name="p"/> and <paramref name="q"/> over the first <paramref name="rows"/> rows.</summary>
    private static void RotateColumns(double[,] m, int p, int q, double c, double s, int rows)
    {
        for (int i = 0; i < rows; i++)
        {
            double left = m[i, p];
            double right = m[i, q];
            m[i, p] = (c * left) + (s * right);
            m[i, q] = (c * right) - (s * left);
        }
    }


    /// <summary>The rotation taking (x, y) to (r, 0) — it clears the second of the pair.</summary>
    private static (double C, double S) RotationZeroingSecond(double x, double y)
    {
        double r = Math.Sqrt((x * x) + (y * y));
        return r == 0 ? (1, 0) : (x / r, y / r);
    }

    /// <summary>Rotates rows <paramref name="p"/> and <paramref name="q"/> of a matrix.</summary>
    private static void ApplyLeft(double[,] m, int p, int q, double c, double s)
    {
        int columns = m.GetLength(1);
        for (int j = 0; j < columns; j++)
        {
            double top = m[p, j];
            double bottom = m[q, j];
            m[p, j] = (c * top) + (s * bottom);
            m[q, j] = (c * bottom) - (s * top);
        }
    }

    /// <summary>Rotates columns <paramref name="p"/> and <paramref name="q"/> of a matrix.</summary>
    private static void ApplyRight(double[,] m, int p, int q, double c, double s)
    {
        int rows = m.GetLength(0);
        for (int i = 0; i < rows; i++)
        {
            double left = m[i, p];
            double right = m[i, q];
            m[i, p] = (c * left) + (s * right);
            m[i, q] = (c * right) - (s * left);
        }
    }

    private static double NonZero(double value) => value == 0 ? double.Epsilon : value;

    /// <summary>Writes the structural zeros a factorization guarantees, so callers read no dust.</summary>
    private static void ZeroBelowDiagonal(double[,] m, int offset)
    {
        int n = m.GetLength(0);
        for (int i = offset; i < n; i++)
        {
            for (int j = 0; j <= i - offset; j++)
            {
                m[i, j] = 0;
            }
        }
    }
}
