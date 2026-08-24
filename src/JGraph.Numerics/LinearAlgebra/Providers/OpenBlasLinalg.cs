using JGraph.Numerics.LinearAlgebra.Native;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The OpenBLAS <see cref="DenseLinalg"/>. Construction is gated by <see cref="LinalgProvider"/>,
/// which only instantiates this after <see cref="OpenBlasLoader"/> reports a successful load.
/// Numeric note: BLAS multiplies every element — it does not skip zero factors the way the managed
/// saxpy kernel does — so <c>0·Inf</c> and <c>0·NaN</c> contribute NaN here exactly as they do in
/// MATLAB, where the managed kernel's skip silently drops them (recorded divergence, ADR 0088).
/// </summary>
public sealed class OpenBlasLinalg : DenseLinalg
{
    /// <inheritdoc />
    public override bool IsNative => true;

    /// <inheritdoc />
    public override string Description => OpenBlasLoader.Status.Description;

    /// <inheritdoc />
    public override unsafe void Gemm(bool transA, bool transB, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc)
    {
        if (m == 0 || n == 0)
        {
            return;
        }

        if (k == 0)
        {
            // beta = 0 would clear C, but with nothing to multiply the empty spans cannot be
            // handed to the native call; the contract's "C is overwritten" is honored by hand.
            for (int col = 0; col < n; col++)
            {
                c.Slice(col * ldc, m).Clear();
            }

            return;
        }

        fixed (double* pa = a)
        fixed (double* pb = b)
        fixed (double* pc = c)
        {
            OpenBlasNative.Dgemm(OpenBlasNative.CblasColMajor,
                transA ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                transB ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                m, n, k, 1.0, pa, lda, pb, ldb, 0.0, pc, ldc);
        }
    }

    /// <inheritdoc />
    public override unsafe void Syrk(bool transposeFirst, int n, int k,
        ReadOnlySpan<double> a, int lda, Span<double> c, int ldc)
    {
        if (n == 0)
        {
            return;
        }

        if (k == 0)
        {
            for (int col = 0; col < n; col++)
            {
                c.Slice(col * ldc, n).Clear();
            }

            return;
        }

        fixed (double* pa = a)
        fixed (double* pc = c)
        {
            OpenBlasNative.Dsyrk(OpenBlasNative.CblasColMajor, OpenBlasNative.CblasLower,
                transposeFirst ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                n, k, 1.0, pa, lda, 0.0, pc, ldc);
        }

        MirrorLowerTriangle(c, n, ldc);
    }
}
