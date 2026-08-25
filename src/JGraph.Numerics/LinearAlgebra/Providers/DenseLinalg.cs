namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense linear-algebra backend: flat column-major spans, explicit leading dimensions, LAPACK
/// argument conventions. <see cref="ManagedLinalg"/> is the always-works implementation over the
/// hand-rolled kernels; <see cref="OpenBlasLinalg"/> calls the bundled OpenBLAS. The active
/// implementation is <see cref="LinalgProvider.Current"/>, and the packed and boxed script
/// representations must always funnel into the same one — the provider axis is orthogonal to the
/// representation axis, never entangled with it.
/// </summary>
/// <remarks>
/// The contract deliberately says nothing about where the work happens, so a future GPU
/// implementation is another subclass, not a new seam. Methods that overwrite an input say so in
/// their own docs; callers own the value-semantics copy.
/// </remarks>
public abstract class DenseLinalg
{
    /// <summary>Whether this backend runs native code (false for the managed kernels).</summary>
    public abstract bool IsNative { get; }

    /// <summary>A one-line human-readable description, e.g. for <c>version('-blas')</c>.</summary>
    public abstract string Description { get; }

    /// <summary>
    /// C := op(A)·op(B), overwriting C. A is m×k after <paramref name="transA"/>, B is k×n after
    /// <paramref name="transB"/>, C is m×n; all column-major with the given leading dimensions.
    /// Inputs are not modified.
    /// </summary>
    public abstract void Gemm(bool transA, bool transB, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc);

    /// <summary>
    /// The symmetric rank-k product: C := Aᵀ·A when <paramref name="transposeFirst"/> (A stored
    /// k×n) and C := A·Aᵀ otherwise (A stored n×k); C is n×n and the full matrix is written,
    /// one triangle computed and mirrored so the result is <em>exactly</em> symmetric — which is
    /// what lets <c>ldl(A'*A)</c> and <c>issymmetric(A*A')</c> hold under a blocked kernel, the
    /// same way MATLAB's own syrk recognition keeps them true. A is not modified.
    /// </summary>
    public abstract void Syrk(bool transposeFirst, int n, int k,
        ReadOnlySpan<double> a, int lda, Span<double> c, int ldc);

    /// <summary>
    /// The LU factorization with partial pivoting, P·A = L·U, computed in place over the m×n
    /// column-major <paramref name="a"/>: L below the diagonal with its unit diagonal implied, U on
    /// and above. Returns 0, or the 1-based index of the first exactly-zero pivot U(i,i) — the
    /// matrix is then singular and no solve may be attempted with it.
    /// </summary>
    /// <remarks>
    /// <paramref name="ipiv"/> is LAPACK's interchange record, not a permutation: at step i, row i
    /// was swapped with row <c>ipiv[i]</c> (1-based). <see cref="PermutationOf"/> turns the record
    /// into the permutation the <c>P</c> output wants.
    /// </remarks>
    public abstract int Getrf(int m, int n, Span<double> a, int lda, Span<int> ipiv);

    /// <summary>
    /// Solves A·X = B — or Aᵀ·X = B when <paramref name="transpose"/> — from a <see cref="Getrf"/>
    /// factorization, overwriting the n×nrhs column-major <paramref name="b"/> with X.
    /// </summary>
    public abstract void Getrs(bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<int> ipiv, Span<double> b, int ldb);

    /// <summary>
    /// Overwrites a <see cref="Getrf"/> factorization with A⁻¹. Returns 0, or the 1-based index of
    /// a zero pivot. The native path costs 4/3·n³ against the 2·n³ of solving against an identity,
    /// which is the whole reason the inverse is its own contract member rather than a loop of solves.
    /// </summary>
    public abstract int Getri(int n, Span<double> a, int lda, ReadOnlySpan<int> ipiv);

    /// <summary>
    /// The reciprocal 1-norm condition number of A, from its <see cref="Getrf"/> factorization and
    /// the 1-norm <paramref name="anorm"/> of A itself. Zero says the factorization is singular.
    /// </summary>
    public abstract double Gecon(int n, ReadOnlySpan<double> a, int lda, double anorm);

    /// <summary>
    /// The Cholesky factorization in place: A = Uᵀ·U over the upper triangle, or A = L·Lᵀ over the
    /// lower. Only the named triangle is read and only it is written, so the other one keeps
    /// whatever the caller left there. Returns 0, or the 1-based order at which a leading minor
    /// stopped being positive definite — with the factor of the block before it already computed.
    /// </summary>
    public abstract int Potrf(bool lower, int n, Span<double> a, int lda);

    /// <summary>
    /// Solves a triangular system, overwriting the n×nrhs <paramref name="b"/>. Returns 0, or the
    /// 1-based index of a zero on the diagonal, which makes the system singular.
    /// </summary>
    public abstract int Trtrs(bool lower, bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, Span<double> b, int ldb);

    /// <summary>
    /// The full-rank least-squares solve of an over-determined m×n system, or the minimum-norm
    /// solve of an under-determined one. Both <paramref name="a"/> and <paramref name="b"/> are
    /// overwritten; <paramref name="b"/> is max(m,n)×nrhs on the way in — the right-hand side in
    /// its first m rows — and holds the n-row solution on the way out. Returns 0, or the 1-based
    /// index of the diagonal entry that made the factor rank deficient.
    /// </summary>
    public abstract int Gels(int m, int n, int nrhs, Span<double> a, int lda, Span<double> b, int ldb);

    /// <summary>
    /// The matrix 1-norm: the largest absolute column sum. Deliberately not a provider member —
    /// it is exact, O(n²), and identical however the rest of the arithmetic is done, so binding
    /// <c>dlange</c> would buy nothing and cost a divergence between the two backends.
    /// </summary>
    public static double OneNorm(int m, int n, ReadOnlySpan<double> a, int lda)
    {
        double best = 0;
        for (int c = 0; c < n; c++)
        {
            double sum = 0;
            ReadOnlySpan<double> column = a.Slice(c * lda, m);
            for (int r = 0; r < m; r++)
            {
                sum += Math.Abs(column[r]);
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    /// <summary>
    /// LAPACK's interchange record as a permutation: <c>result[i]</c> is the row of A that ends up
    /// as row i of the factored matrix, which is the row index the <c>P</c> output and a permuted
    /// right-hand side both want.
    /// </summary>
    public static int[] PermutationOf(ReadOnlySpan<int> ipiv, int n)
    {
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        for (int i = 0; i < ipiv.Length && i < n; i++)
        {
            int other = ipiv[i] - 1;
            if (other != i && other >= 0 && other < n)
            {
                (order[i], order[other]) = (order[other], order[i]);
            }
        }

        return order;
    }

    /// <summary>Mirrors the computed lower triangle of an n×n column-major C onto its upper.</summary>
    private protected static void MirrorLowerTriangle(Span<double> c, int n, int ldc)
    {
        for (int j = 1; j < n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                c[(j * ldc) + i] = c[(i * ldc) + j];
            }
        }
    }
}

/// <summary>The selectable <see cref="DenseLinalg"/> backends.</summary>
public enum LinalgBackend
{
    /// <summary>The hand-rolled managed kernels.</summary>
    Managed,

    /// <summary>The bundled native OpenBLAS (throws from <see cref="LinalgProvider.Use"/> if it did not load).</summary>
    OpenBlas,
}
