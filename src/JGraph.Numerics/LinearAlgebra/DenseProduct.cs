namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense matrix product over flat column-major storage (M42). One saxpy-style kernel:
/// R[:,c] = Σₖ B[k,c]·A[:,k], streaming contiguous columns of A into a contiguous column of R,
/// parallelized over result columns. This is what makes a script's hundredth <c>A*A'</c> at
/// n = 1000 a second-scale operation instead of a minute-scale one.
/// </summary>
public static class DenseProduct
{
    /// <summary>
    /// Multiplies A (m×inner) by B (inner×cols), both flat column-major, into a flat column-major
    /// result. Parallel over result columns when the work is worth the threads.
    /// </summary>
    public static double[] ColumnMajor(double[] a, int m, int inner, double[] b, int cols)
    {
        var result = new double[(long)m * cols];
        long flops = 2L * m * inner * cols;
        if (flops < 1_000_000)
        {
            for (int c = 0; c < cols; c++)
            {
                MultiplyColumn(a, m, inner, b, result, c);
            }
        }
        else
        {
            Parallel.For(0, cols, c => MultiplyColumn(a, m, inner, b, result, c));
        }

        return result;
    }

    private static void MultiplyColumn(double[] a, int m, int inner, double[] b, double[] result, int c)
    {
        int resultBase = c * m;
        int bBase = c * inner;
        for (int k = 0; k < inner; k++)
        {
            double factor = b[bBase + k];
            if (factor == 0)
            {
                continue;
            }

            int aBase = k * m;
            for (int r = 0; r < m; r++)
            {
                result[resultBase + r] += factor * a[aBase + r];
            }
        }
    }
}
