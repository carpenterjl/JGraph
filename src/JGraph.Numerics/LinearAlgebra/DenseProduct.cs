namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense matrix product over flat column-major storage (M42, provider-backed since M88).
/// The computation itself lives behind <see cref="LinalgProvider.Current"/> — OpenBLAS when the
/// bundled library loaded, the managed saxpy kernel otherwise — so a script's hundredth
/// <c>A*A'</c> at n = 2000 is a centisecond-scale operation instead of a second-scale one.
/// </summary>
public static class DenseProduct
{
    /// <summary>
    /// Multiplies A (m×inner) by B (inner×cols), both flat column-major, into a fresh flat
    /// column-major result, through the active <see cref="DenseLinalg"/> backend.
    /// </summary>
    public static double[] ColumnMajor(double[] a, int m, int inner, double[] b, int cols)
    {
        var result = new double[(long)m * cols];
        LinalgProvider.Current.Gemm(transA: false, transB: false, m, cols, inner, a, m, b, inner, result, m);
        return result;
    }
}
