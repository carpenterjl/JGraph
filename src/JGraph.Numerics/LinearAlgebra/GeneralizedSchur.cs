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
    /// The managed QZ behind <see cref="Factor"/> — the iteration when B cooperates, the
    /// reciprocal pencil when it is singular. <see cref="ManagedLinalg"/> reaches it directly.
    /// </summary>
    internal static GeneralizedSchur FactorManaged(double[,] a, double[,] b)
    {
        try
        {
            return Iterated(a, b);
        }
        catch (DegeneratePencil)
        {
            return ThroughTheReciprocal(a, b);
        }
    }

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

    /// <summary>
    /// The pencil taken the other way round, for a <c>B</c> that is singular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The iteration wants a denominator it can divide by, and a singular <c>B</c> has none: some
    /// eigenvalue is at infinity, and no amount of iterating makes a zero pivot workable. Turning
    /// the pencil over turns that infinity into a zero, which is an ordinary number.
    /// </para>
    /// <para>
    /// Factoring (<c>B</c>, <c>A + μB</c>) for a μ that makes the second matrix nonsingular gives
    /// orthogonal Q and Z with <c>Q·B·Z</c> quasi-triangular and <c>Q·(A + μB)·Z</c> triangular.
    /// The same pair then answers for the original: <c>Q·A·Z</c> is that triangular matrix less μ
    /// times the other, and a difference of a triangular and a quasi-triangular matrix is
    /// quasi-triangular. One thing has to be put right — this pencil's 2-by-2 blocks land in the
    /// matrix that is required to be triangular — and a single rotation of rows per block moves
    /// each one across into <c>AA</c>, where a real factorization is allowed to keep it.
    /// </para>
    /// </remarks>
    private static GeneralizedSchur ThroughTheReciprocal(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        double scale = 1 + Norm(a) + Norm(b);

        foreach (double shift in new[] { 0.0, 1.0, -1.0, 0.5, -0.25, 2.0, 0.125 })
        {
            var shifted = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    shifted[i, j] = a[i, j] + (shift * scale * b[i, j]);
                }
            }

            GeneralizedSchur turned;
            try
            {
                turned = Iterated(b, shifted);
            }
            catch (DegeneratePencil)
            {
                continue; // this shift left the second matrix singular too; try another
            }

            double[,] bb = (double[,])turned.AA.Clone();     // Q·B·Z, quasi-triangular for now
            double[,] upper = (double[,])turned.BB.Clone();  // Q·(A + μB)·Z, triangular
            double[,] q = Linear.Transpose(turned.Q);
            double[,] z = turned.Z;

            // Any 2-by-2 block belongs on the other side of the pair. One rotation of its two rows
            // triangularizes it there, and leaves the matrix it moves into free to be full.
            for (int i = 0; i + 1 < n; i++)
            {
                if (bb[i + 1, i] == 0)
                {
                    continue;
                }

                (double c, double s) = RotationZeroingSecond(bb[i, i], bb[i + 1, i]);
                ApplyLeft(bb, i, i + 1, c, s);
                ApplyLeft(upper, i, i + 1, c, s);
                ApplyRight(q, i, i + 1, c, s);
                bb[i + 1, i] = 0;
            }

            var aa = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    aa[i, j] = upper[i, j] - (shift * scale * bb[i, j]);
                }
            }

            ZeroBelowDiagonal(bb, 1);
            ZeroBelowDiagonal(aa, 2);

            // The whole point of coming this way was an eigenvalue at infinity, and an infinity is
            // an exact zero denominator. Arriving at 1e-17 instead would make the answer say the
            // pencil is finite, which is the one thing it is not.
            double tolerance = 1e-12 * (1 + Norm(bb));
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(bb[i, i]) <= tolerance)
                {
                    bb[i, i] = 0;
                }
            }

            (Complex[] alpha, double[] beta) = EigenvaluePairs(aa, bb);
            return new GeneralizedSchur(aa, bb, Linear.Transpose(q), z, alpha, beta);
        }

        throw new ArgumentException(
            "This pencil is singular — every number is an eigenvalue of it — so it has no " +
            "generalized Schur form to compute.");
    }

    private static double Norm(double[,] m)
    {
        double largest = 0;
        foreach (double value in m)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        return largest;
    }

    /// <summary>The iteration proper, for a pencil whose second matrix stays nonsingular.</summary>
    private static GeneralizedSchur Iterated(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);

        // Phase one: B triangular. Its QR gives the left factor that does it, and A comes along.
        QrDecomposition qr = QrDecomposition.Factor(b);
        double[,] qAccumulated = qr.FullQ;
        double[,] work = Linear.Multiply(Linear.Transpose(qAccumulated), a);
        double[,] triangular = Linear.Multiply(Linear.Transpose(qAccumulated), b);
        ZeroBelowDiagonal(triangular, 1);

        // A zero on the triangle's diagonal is a singular B, which this iteration has no answer for
        // and the reciprocal route does. Asked here, before any work, rather than discovered in the
        // middle of a sweep.
        if (SingularDiagonal(triangular, 0, n - 1) >= 0)
        {
            throw new DegeneratePencil();
        }

        double[,] zAccumulated = Linear.Identity(n);

        // Phase two: A to Hessenberg, B kept triangular.
        for (int column = 0; column < n - 2; column++)
        {
            ReduceColumn(work, triangular, qAccumulated, zAccumulated, column, column + 2, n - 1);
        }

        // Phase three: the iteration.
        Iterate(work, triangular, qAccumulated, zAccumulated, n);

        double[,] q = Linear.Transpose(qAccumulated);
        (Complex[] alpha, double[] beta) = EigenvaluePairs(work, triangular);
        return new GeneralizedSchur(work, triangular, q, zAccumulated, alpha, beta);
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

    private static void Iterate(double[,] a, double[,] b, double[,] q, double[,] z, int n)
    {
        int high = n - 1;
        int budget = IterationsPerEigenvalue * n;
        while (high > 0 && budget-- > 0)
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

            if (a[high, high - 1] == 0)
            {
                high--;                                    // a 1-by-1 block has converged
                continue;
            }

            int low = high - 1;
            while (low > 0 && a[low, low - 1] != 0)
            {
                low--;
            }

            if (low == high - 1)
            {
                // Two eigenvalues, either a conjugate pair that stays as a block or a real pair the
                // form is meant to separate.
                Split2x2(a, b, q, z, low);
                high = low - 1 < 0 ? 0 : low - 1;
                continue;
            }

            if (SingularDiagonal(b, low, high) >= 0)
            {
                throw new DegeneratePencil();
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

            (double c, double s) = RotationZeroingSecond(a[i - 1, column], a[i, column]);
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

        (double c, double s) = RotationZeroingFirst(b[row, p], b[row, q]);
        ApplyRight(a, p, q, c, s);
        ApplyRight(b, p, q, c, s);
        ApplyRight(z, p, q, c, s);
        b[row, p] = 0;
    }

    /// <summary>The first index in the block whose diagonal of B has gone to zero, or −1.</summary>
    private static int SingularDiagonal(double[,] b, int low, int high)
    {
        double scale = 0;
        for (int i = low; i <= high; i++)
        {
            scale = Math.Max(scale, Math.Abs(b[i, i]));
        }

        double tolerance = double.Epsilon + (1e-15 * scale);
        for (int i = low; i <= high; i++)
        {
            if (Math.Abs(b[i, i]) <= tolerance)
            {
                b[i, i] = 0;
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Triangularizes a 2-by-2 block whose eigenvalues are real, and leaves one whose eigenvalues
    /// are a conjugate pair alone — which is what makes the result quasi-triangular rather than
    /// triangular, and is the only shape a real factorization can have.
    /// </summary>
    private static void Split2x2(double[,] a, double[,] b, double[,] q, double[,] z, int at)
    {
        (double sum, double product, bool real) = BlockShift(a, b, at);
        if (!real)
        {
            return;
        }

        double root = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * product)));

        // The root further from zero is the stable one to form directly; either may be used, and
        // this one keeps the subtraction out of the numerator.
        double lambda = sum >= 0 ? (sum + root) / 2 : (sum - root) / 2;
        PlaceEigenvalueFirst(a, b, q, z, at, lambda);
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

    /// <summary>
    /// Raised inside the iteration when B turns out to be singular, so that the caller can take the
    /// reciprocal route instead. Private and always caught here: it is control flow between two
    /// halves of one algorithm, never something a caller sees.
    /// </summary>
    private sealed class DegeneratePencil : Exception
    {
    }

    // --- rotations ------------------------------------------------------------------------------

    /// <summary>The rotation taking (x, y) to (r, 0) — it clears the second of the pair.</summary>
    private static (double C, double S) RotationZeroingSecond(double x, double y)
    {
        double r = Math.Sqrt((x * x) + (y * y));
        return r == 0 ? (1, 0) : (x / r, y / r);
    }

    /// <summary>The rotation clearing the first of the pair rather than the second.</summary>
    private static (double C, double S) RotationZeroingFirst(double x, double y)
    {
        double r = Math.Sqrt((x * x) + (y * y));
        return r == 0 ? (1, 0) : (y / r, -x / r);
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
