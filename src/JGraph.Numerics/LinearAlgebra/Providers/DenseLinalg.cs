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
