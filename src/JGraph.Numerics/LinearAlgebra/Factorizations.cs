using System.Diagnostics.CodeAnalysis;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The Cholesky factorization of a symmetric positive definite matrix: A = L·Lᵀ over the lower
/// triangle, or A = Rᵀ·R over the upper. The factor is held column-major — LAPACK's layout, and the
/// script's — and the triangle a caller asks for is the triangle that is read and the one that is
/// written, which is what MATLAB's <c>chol</c> does with its <c>'upper'</c> and <c>'lower'</c> words.
/// </summary>
/// <remarks>
/// The two directions are mirror images: for a symmetric input, factoring the upper triangle asks
/// the same question of the same numbers as factoring the lower one, so <c>chol(A)</c> and
/// <c>chol(A, 'lower')'</c> are the same matrix and nothing has to be transposed to get from one to
/// the other. The managed kernel sums those products in the same order either way and so agrees to
/// the bit; a blocked native factorization reorders within its last ulps, as it does everywhere.
/// </remarks>
public sealed class Cholesky
{
    private readonly double[] _factor;   // column-major n×n: the computed triangle, the other one zeroed
    private readonly int _n;

    private Cholesky(double[] factor, int n, bool lower, bool positiveDefinite, int failedAt)
    {
        _factor = factor;
        _n = n;
        IsLower = lower;
        IsPositiveDefinite = positiveDefinite;
        FailedAt = failedAt;
    }

    /// <summary>Whether the factor computed is the lower triangle L rather than the upper R.</summary>
    public bool IsLower { get; }

    /// <summary>The lower triangular factor L, with L·Lᵀ = A.</summary>
    public double[,] Lower => Rect(transposed: !IsLower);

    /// <summary>The triangle that was asked for — L or R as <see cref="IsLower"/> says.</summary>
    public double[,] Triangle => Rect(transposed: false);

    /// <summary>
    /// The factor's own storage: column-major, with the other triangle zeroed. Handed over rather
    /// than copied, so a caller that wants to keep it must not also expect this object to stay
    /// meaningful — which is the same bargain <see cref="FactorAdopting"/> makes going in.
    /// </summary>
    public double[] ColumnMajor => _factor;

    /// <summary>
    /// Whether the factorization succeeded. A matrix that is not positive definite runs into a
    /// non-positive pivot, which is exactly the standard test for definiteness.
    /// </summary>
    public bool IsPositiveDefinite { get; }

    /// <summary>
    /// The 1-based order at which a non-positive pivot was met, or 0 when the matrix is positive
    /// definite — MATLAB's <c>flag</c> from <c>[R, flag] = chol(A)</c>.
    /// </summary>
    /// <remarks>
    /// The factorization walks the leading minors in turn, so failing at order q says the leading
    /// (q−1)-by-(q−1) block *is* positive definite and its factor is already computed. That partial
    /// factor was always returned in <see cref="Lower"/>; until M76 nothing said how much of it was
    /// meaningful, which is the whole content of the second output.
    /// </remarks>
    public int FailedAt { get; }

    /// <summary>Factors a square matrix, reading only its lower triangle.</summary>
    public static Cholesky Factor(double[,] matrix) => Factor(matrix, lower: true);

    /// <summary>Factors a square matrix, reading the triangle it is asked for and producing that factor.</summary>
    public static Cholesky Factor(double[,] matrix, bool lower)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        var work = new double[(long)n * n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                work[((long)c * n) + r] = matrix[r, c];
            }
        }

        return FactorInPlace(work, n, lower);
    }

    /// <summary>
    /// Factors an n-by-n matrix already laid out column-major — the layout packed script storage
    /// uses, so this is the entry point that costs one copy and no transpose.
    /// </summary>
    /// <exception cref="ArgumentException">The span is shorter than n².</exception>
    public static Cholesky Factor(ReadOnlySpan<double> columnMajor, int n, bool lower)
    {
        if (columnMajor.Length < (long)n * n)
        {
            throw new ArgumentException("The span must hold n² elements.", nameof(columnMajor));
        }

        double[] work = GC.AllocateUninitializedArray<double>(n * n);
        columnMajor[..(n * n)].CopyTo(work);
        return FactorInPlace(work, n, lower);
    }

    /// <summary>
    /// Factors an n-by-n column-major array <em>in place</em>, taking ownership of it: the caller
    /// must not read <paramref name="columnMajor"/> afterwards, because it now holds the factor.
    /// </summary>
    /// <exception cref="ArgumentException">The array is shorter than n².</exception>
    public static Cholesky FactorAdopting(double[] columnMajor, int n, bool lower)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        if (columnMajor.LongLength < (long)n * n)
        {
            throw new ArgumentException("The array must hold n² elements.", nameof(columnMajor));
        }

        return FactorInPlace(columnMajor, n, lower);
    }

    private static Cholesky FactorInPlace(double[] work, int n, bool lower)
    {
        int info = LinalgProvider.Current.Potrf(lower, n, work, n);

        // The factorization only ever writes its own triangle, so the other one still holds the
        // input. Clearing it is what turns the working array into the triangular factor itself.
        for (int c = 0; c < n; c++)
        {
            long origin = (long)c * n;
            if (lower)
            {
                for (int r = 0; r < c; r++)
                {
                    work[origin + r] = 0;
                }
            }
            else
            {
                for (int r = c + 1; r < n; r++)
                {
                    work[origin + r] = 0;
                }
            }
        }

        // A failure at order q leaves everything past the leading (q−1) block untouched — which is
        // to say holding the input, not a factor. Clearing it keeps the promise that what comes
        // back is a triangular factor of *something*, with FailedAt saying how much of it is one.
        if (info != 0)
        {
            int kept = info - 1;
            for (int c = 0; c < n; c++)
            {
                long origin = (long)c * n;
                for (int r = 0; r < n; r++)
                {
                    if (r >= kept || c >= kept)
                    {
                        work[origin + r] = 0;
                    }
                }
            }
        }

        return new Cholesky(work, n, lower, info == 0, info);
    }

    private double[,] Rect(bool transposed)
    {
        var rect = new double[_n, _n];
        for (int c = 0; c < _n; c++)
        {
            long origin = (long)c * _n;
            for (int r = 0; r < _n; r++)
            {
                if (transposed)
                {
                    rect[c, r] = _factor[origin + r];
                }
                else
                {
                    rect[r, c] = _factor[origin + r];
                }
            }
        }

        return rect;
    }
}

/// <summary>
/// The LDLᵀ factorization of a symmetric matrix: P·A·Pᵀ = L·D·Lᵀ with L unit lower triangular and
/// D diagonal.
/// </summary>
/// <remarks>
/// Pivoting is symmetric and 1×1 — at each step the largest remaining diagonal entry takes the
/// pivot — which keeps P·A·Pᵀ symmetric and covers every definite and most indefinite matrices.
/// It cannot handle a matrix whose remaining diagonal is entirely zero (<c>[0 1; 1 0]</c> is the
/// small example); LAPACK's 2×2 block pivoting is what those need, and
/// <see cref="IsFactored"/> reports the case rather than returning nonsense.
/// </remarks>
public sealed class Ldl
{
    private Ldl(double[,] lower, double[] diagonal, int[] order, bool factored)
    {
        Lower = lower;
        Diagonal = diagonal;
        Order = order;
        IsFactored = factored;
    }

    /// <summary>The unit lower triangular factor L.</summary>
    public double[,] Lower { get; }

    /// <summary>The diagonal of D.</summary>
    public double[] Diagonal { get; }

    /// <summary>The pivot order: row <c>Order[i]</c> of A became row i of the factored matrix.</summary>
    public int[] Order { get; }

    /// <summary>Whether the factorization completed; false when a 2×2 pivot block would be needed.</summary>
    public bool IsFactored { get; }

    /// <summary>The permutation P as a matrix, so that P·A·Pᵀ = L·D·Lᵀ.</summary>
    public double[,] Permutation
    {
        get
        {
            int n = Order.Length;
            var p = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                p[i, Order[i]] = 1;
            }

            return p;
        }
    }

    /// <summary>Factors a symmetric matrix.</summary>
    public static Ldl Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        var lower = Linear.Identity(n);
        var diagonal = new double[n];

        for (int k = 0; k < n; k++)
        {
            // Symmetric pivot: swap the largest remaining diagonal entry into position, rows and
            // columns together so the working matrix stays symmetric.
            int best = k;
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(a[i, i]) > Math.Abs(a[best, best]))
                {
                    best = i;
                }
            }

            if (best != k)
            {
                SwapSymmetric(a, n, k, best);
                (order[k], order[best]) = (order[best], order[k]);
                for (int c = 0; c < k; c++)
                {
                    (lower[k, c], lower[best, c]) = (lower[best, c], lower[k, c]);
                }
            }

            diagonal[k] = a[k, k];
            if (diagonal[k] == 0)
            {
                return new Ldl(lower, diagonal, order, factored: false);
            }

            for (int i = k + 1; i < n; i++)
            {
                double multiplier = a[i, k] / diagonal[k];
                lower[i, k] = multiplier;
                for (int j = k + 1; j <= i; j++)
                {
                    a[i, j] -= multiplier * a[j, k];
                    a[j, i] = a[i, j];
                }
            }
        }

        return new Ldl(lower, diagonal, order, factored: true);
    }

    private static void SwapSymmetric(double[,] a, int n, int i, int j)
    {
        for (int c = 0; c < n; c++)
        {
            (a[i, c], a[j, c]) = (a[j, c], a[i, c]);
        }

        for (int r = 0; r < n; r++)
        {
            (a[r, i], a[r, j]) = (a[r, j], a[r, i]);
        }
    }
}

/// <summary>
/// The upper Hessenberg reduction A = Q·H·Qᵀ by Householder reflections — the first half of every
/// eigenvalue algorithm, and MATLAB's <c>hess</c>.
/// </summary>
public sealed class Hessenberg
{
    private Hessenberg(double[,] h, double[,] q)
    {
        H = h;
        Q = q;
    }

    /// <summary>The upper Hessenberg matrix: zero below the first subdiagonal.</summary>
    public double[,] H { get; }

    /// <summary>The orthogonal similarity transform, with Q·H·Qᵀ = A.</summary>
    public double[,] Q { get; }

    /// <summary>Reduces a square matrix to upper Hessenberg form.</summary>
    public static Hessenberg Reduce(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (TryReduceNative(matrix, out Hessenberg? blocked))
        {
            return blocked;
        }

        int n = matrix.GetLength(0);
        var h = (double[,])matrix.Clone();
        var q = Linear.Identity(n);
        var scratch = new double[n];

        for (int k = 0; k < n - 2; k++)
        {
            // The reflector that zeroes column k below the subdiagonal.
            double norm = 0;
            for (int i = k + 1; i < n; i++)
            {
                norm += h[i, k] * h[i, k];
            }

            norm = Math.Sqrt(norm);
            if (norm == 0)
            {
                continue;
            }

            double alpha = h[k + 1, k] > 0 ? -norm : norm;
            var v = new double[n];
            v[k + 1] = h[k + 1, k] - alpha;
            for (int i = k + 2; i < n; i++)
            {
                v[i] = h[i, k];
            }

            double vv = 0;
            for (int i = k + 1; i < n; i++)
            {
                vv += v[i] * v[i];
            }

            if (vv == 0)
            {
                continue;
            }

            ApplyLeft(h, v, vv, n, k + 1, scratch);
            ApplyRight(h, v, vv, n, k + 1);
            ApplyRight(q, v, vv, n, k + 1);
        }

        // The reflections leave rounding dust below the subdiagonal; Hessenberg form means exactly
        // zero there, and a caller testing istriu on the result should see that.
        for (int r = 2; r < n; r++)
        {
            for (int c = 0; c < r - 1; c++)
            {
                h[r, c] = 0;
            }
        }

        return new Hessenberg(h, q);
    }

    /// <summary>
    /// The same reduction on the backend's blocked kernel, when it has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop below is the reduction written as rank-one updates, and M120 already spent what
    /// there is to spend on it by walking rows instead of columns. What is left between it and
    /// MATLAB is not a cache miss but an algorithm: LAPACK accumulates a panel of reflectors and
    /// applies them as one matrix multiply, so the work lands on BLAS-3 instead of BLAS-2. That is
    /// the difference between 0.196 s and 0.005 s at 400 square, and it is not something a managed
    /// rewrite of the same loop can close.
    /// </para>
    /// <para>
    /// Both halves are asked for, and the whole road is abandoned if either says no: half a
    /// reduction is not a reduction, and falling back is free.
    /// </para>
    /// </remarks>
    private static bool TryReduceNative(double[,] matrix, [NotNullWhen(true)] out Hessenberg? reduced)
    {
        reduced = null;
        int n = matrix.GetLength(0);
        if (n < 1 || matrix.GetLength(1) != n || !LinalgProvider.Current.IsNative)
        {
            return false;
        }

        // Column-major, which is the layout the contract is written in.
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                a[r + (c * n)] = matrix[r, c];
            }
        }

        var tau = new double[Math.Max(n - 1, 1)];
        if (!LinalgProvider.Current.TryGehrd(n, a, n, tau))
        {
            return false;
        }

        // The reduction's output carries H above the subdiagonal and the reflectors below, so Q is
        // formed from a copy and H is read out of the original with the reflectors zeroed away.
        var forQ = (double[])a.Clone();
        if (!LinalgProvider.Current.TryOrghr(n, forQ, n, tau))
        {
            return false;
        }

        var h = new double[n, n];
        var q = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                h[r, c] = r > c + 1 ? 0 : a[r + (c * n)];
                q[r, c] = forQ[r + (c * n)];
            }
        }

        reduced = new Hessenberg(h, q);
        return true;
    }

    /// <summary>M ← (I - 2vvᵀ/vᵀv)·M, over rows <paramref name="first"/> and after.</summary>
    /// <remarks>
    /// <para>
    /// The reflector is zero above <paramref name="first"/>, so those rows contribute nothing and
    /// are skipped -- half the work on average, and exactly the same answer, because what is
    /// skipped is a running sum adding zero to zero.
    /// </para>
    /// <para>
    /// The rows are the outer loop and the columns the inner one, which is the opposite of how a
    /// dot product per column reads and the whole of why this used to cost what it did. A
    /// <c>double[,]</c> is stored a row at a time, so walking a column is a stride of n doubles per
    /// step: at 400 square that is a cache line fetched and one number used out of it, four hundred
    /// times per column, four hundred columns, four hundred reflectors. Accumulating a whole row of
    /// dot products at once reads every one of those lines once and uses all of it (M120).
    /// </para>
    /// </remarks>
    private static void ApplyLeft(double[,] m, double[] v, double vv, int n, int first, double[] dots)
    {
        Array.Clear(dots, 0, n);
        for (int r = first; r < n; r++)
        {
            double weight = v[r];
            if (weight == 0)
            {
                continue;
            }

            for (int c = 0; c < n; c++)
            {
                dots[c] += weight * m[r, c];
            }
        }

        // Written as the multiply and then the divide, in that order, because that is the order the
        // one-column-at-a-time version used and (2·d)/v is not d·(2/v) in the last bit. Reassociating
        // it moved the reduction of a 17-square by three parts in a thousand -- every answer still
        // backward stable, MATLAB's own a third value again, and none of that a reason to move.
        for (int c = 0; c < n; c++)
        {
            dots[c] = 2.0 * dots[c] / vv;
        }

        for (int r = first; r < n; r++)
        {
            double weight = v[r];
            if (weight == 0)
            {
                continue;
            }

            for (int c = 0; c < n; c++)
            {
                m[r, c] -= dots[c] * weight;
            }
        }
    }

    /// <summary>M ← M·(I - 2vvᵀ/vᵀv), over columns <paramref name="first"/> and after.</summary>
    /// <remarks>
    /// This one already ran along the rows, so only the reflector's leading zeros are dropped.
    /// </remarks>
    private static void ApplyRight(double[,] m, double[] v, double vv, int n, int first)
    {
        for (int r = 0; r < n; r++)
        {
            double dot = 0;
            for (int c = first; c < n; c++)
            {
                dot += m[r, c] * v[c];
            }

            dot = 2.0 * dot / vv;
            for (int c = first; c < n; c++)
            {
                m[r, c] -= dot * v[c];
            }
        }
    }
}

/// <summary>Functions of a whole matrix, as distinct from functions applied element by element.</summary>
public static class MatrixFunctions
{
    /// <summary>
    /// The matrix exponential e^A, by scaling and squaring around a Padé approximant: the matrix is
    /// halved until its norm is small enough for the approximant to be accurate, then the result is
    /// squared back. Evaluating the Taylor series directly instead is the classic way to get a
    /// badly wrong answer for a matrix with a large norm.
    /// </summary>
    public static double[,] Exponential(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);

        double norm = 0;
        for (int r = 0; r < n; r++)
        {
            double rowSum = 0;
            for (int c = 0; c < n; c++)
            {
                rowSum += Math.Abs(matrix[r, c]);
            }

            norm = Math.Max(norm, rowSum);
        }

        int squarings = norm > 0.5 ? (int)Math.Max(0, Math.Floor(Math.Log2(norm)) + 2) : 0;
        double scale = Math.Pow(2, -squarings);
        var a = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                a[r, c] = matrix[r, c] * scale;
            }
        }

        const int Order = 6;
        double[,] numerator = Linear.Identity(n);
        double[,] denominator = Linear.Identity(n);
        double[,] power = Linear.Identity(n);
        double coefficient = 1;
        bool negative = true;
        for (int k = 1; k <= Order; k++)
        {
            coefficient = coefficient * (Order - k + 1) / (((2 * Order) - k + 1) * (double)k);
            power = Linear.Multiply(a, power);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    double term = coefficient * power[r, c];
                    numerator[r, c] += term;
                    denominator[r, c] += negative ? -term : term;
                }
            }

            negative = !negative;
        }

        double[,] result = Linear.Solve(denominator, numerator);
        for (int i = 0; i < squarings; i++)
        {
            result = Linear.Multiply(result, result);
        }

        return result;
    }
}

/// <summary>
/// Rank-one updates of a factorization: recomputing from scratch costs O(n³) where updating the
/// existing factors costs O(n²), which is the whole reason these exist.
/// </summary>
public static class RankOneUpdates
{
    /// <summary>
    /// Given R with RᵀR = A, returns the R̃ with R̃ᵀR̃ = A + xxᵀ. The update runs a chain of Givens
    /// rotations down the factor, so the result stays exactly upper triangular rather than being
    /// re-triangularized afterwards.
    /// </summary>
    public static double[,] CholeskyUpdate(double[,] r, double[] x)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(x);
        int n = r.GetLength(0);
        if (r.GetLength(1) != n || x.Length != n)
        {
            throw new ArgumentException("cholupdate needs a square factor and a vector of matching length.");
        }

        var result = (double[,])r.Clone();
        var work = (double[])x.Clone();

        for (int k = 0; k < n; k++)
        {
            double length = Hypot(result[k, k], work[k]);
            double cos = length == 0 ? 1 : result[k, k] / length;
            double sin = length == 0 ? 0 : work[k] / length;
            result[k, k] = length;

            for (int j = k + 1; j < n; j++)
            {
                double top = (cos * result[k, j]) + (sin * work[j]);
                work[j] = (cos * work[j]) - (sin * result[k, j]);
                result[k, j] = top;
            }
        }

        return result;
    }

    /// <summary>
    /// Given R with RᵀR = A, returns the R̃ with R̃ᵀR̃ = A − xxᵀ, or null when that matrix is not
    /// positive definite. A downdate can genuinely destroy definiteness, so the failure is reported
    /// rather than papered over with a NaN-filled factor.
    /// </summary>
    public static double[,]? CholeskyDowndate(double[,] r, double[] x)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(x);
        int n = r.GetLength(0);
        if (r.GetLength(1) != n || x.Length != n)
        {
            throw new ArgumentException("cholupdate needs a square factor and a vector of matching length.");
        }

        // Solve Rᵀp = x. If ‖p‖ ≥ 1 the downdated matrix is not positive definite, which is exactly
        // the condition to test before touching the factor.
        var p = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = x[i];
            for (int j = 0; j < i; j++)
            {
                sum -= r[j, i] * p[j];
            }

            if (r[i, i] == 0)
            {
                return null;
            }

            p[i] = sum / r[i, i];
        }

        double residual = 1;
        for (int i = 0; i < n; i++)
        {
            residual -= p[i] * p[i];
        }

        if (residual <= 0)
        {
            return null;
        }

        // The hyperbolic rotations run bottom up, carrying the residual with them.
        var cos = new double[n];
        var sin = new double[n];
        double carry = Math.Sqrt(residual);

        for (int i = n - 1; i >= 0; i--)
        {
            double length = Hypot(carry, p[i]);
            cos[i] = carry / length;
            sin[i] = p[i] / length;
            carry = length;
        }

        var result = (double[,])r.Clone();

        for (int i = n - 1; i >= 0; i--)
        {
            double below = 0;
            for (int j = i; j >= 0; j--)
            {
                double above = (cos[j] * below) + (sin[j] * result[j, i]);
                result[j, i] = (cos[j] * result[j, i]) - (sin[j] * below);
                below = above;
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (result[i, i] < 0)
            {
                for (int j = i; j < n; j++)
                {
                    result[i, j] = -result[i, j];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Given A = Q·R, returns the factors of A + uvᵀ. Q is brought back to a Hessenberg-plus-rank-one
    /// shape by rotating u down to a multiple of e₁, and the resulting bulge is chased out of R.
    /// </summary>
    public static (double[,] Q, double[,] R) QrUpdate(double[,] q, double[,] r, double[] u, double[] v)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(v);

        int m = q.GetLength(0);
        int n = r.GetLength(1);
        if (q.GetLength(1) != m || r.GetLength(0) != m || u.Length != m || v.Length != n)
        {
            throw new ArgumentException("qrupdate needs a square Q and vectors matching A's two dimensions.");
        }

        var qq = (double[,])q.Clone();
        var rr = (double[,])r.Clone();

        // w = Qᵀu, so that A + uvᵀ = Q(R + wvᵀ).
        var w = new double[m];
        for (int i = 0; i < m; i++)
        {
            double sum = 0;
            for (int k = 0; k < m; k++)
            {
                sum += q[k, i] * u[k];
            }

            w[i] = sum;
        }

        // Rotate w down onto e₁, applying each rotation to R as it goes. R picks up one subdiagonal
        // on the way, which is what the second pass removes.
        for (int i = m - 2; i >= 0; i--)
        {
            double length = Hypot(w[i], w[i + 1]);
            if (length == 0)
            {
                continue;
            }

            double cos = w[i] / length;
            double sin = w[i + 1] / length;
            w[i] = length;
            w[i + 1] = 0;

            RotateRows(rr, m, n, i, cos, sin);
            RotateColumns(qq, m, i, cos, sin);
        }

        for (int j = 0; j < n; j++)
        {
            rr[0, j] += w[0] * v[j];
        }

        for (int i = 0; i < Math.Min(m - 1, n); i++)
        {
            double length = Hypot(rr[i, i], rr[i + 1, i]);
            if (length == 0)
            {
                continue;
            }

            double cos = rr[i, i] / length;
            double sin = rr[i + 1, i] / length;

            RotateRows(rr, m, n, i, cos, sin);
            RotateColumns(qq, m, i, cos, sin);
            rr[i + 1, i] = 0;
        }

        return (qq, rr);
    }

    /// <summary>Applies a Givens rotation to rows i and i+1.</summary>
    private static void RotateRows(double[,] a, int rows, int columns, int i, double cos, double sin)
    {
        if (i + 1 >= rows)
        {
            return;
        }

        for (int j = 0; j < columns; j++)
        {
            double top = (cos * a[i, j]) + (sin * a[i + 1, j]);
            a[i + 1, j] = (cos * a[i + 1, j]) - (sin * a[i, j]);
            a[i, j] = top;
        }
    }

    /// <summary>Applies the transpose of the same rotation to columns i and i+1, keeping Q·R fixed.</summary>
    private static void RotateColumns(double[,] a, int rows, int i, double cos, double sin)
    {
        for (int k = 0; k < rows; k++)
        {
            double left = (cos * a[k, i]) + (sin * a[k, i + 1]);
            a[k, i + 1] = (cos * a[k, i + 1]) - (sin * a[k, i]);
            a[k, i] = left;
        }
    }

    /// <summary>√(a² + b²) without the intermediate overflow the plain formula has.</summary>
    private static double Hypot(double a, double b)
    {
        double x = Math.Abs(a);
        double y = Math.Abs(b);
        if (x < y)
        {
            (x, y) = (y, x);
        }

        return x == 0 ? 0 : x * Math.Sqrt(1 + ((y / x) * (y / x)));
    }
}
