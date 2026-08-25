namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Diagonal balancing — LAPACK's <c>dgebal</c> scaling, the step that makes a badly scaled matrix's
/// eigenvalues computable. Rescaling row i by 1/dᵢ and column i by dᵢ is a similarity, so it leaves
/// the spectrum alone; choosing the dᵢ so each row and its column have comparable norms is what
/// stops the QR iteration from spending its precision on the scaling instead of on the answer.
/// </summary>
/// <remarks>
/// The powers of two are the point: a factor that is exactly a power of the floating-point radix
/// rescales without rounding, so the balanced matrix has the same eigenvalues as the original to
/// the last bit rather than merely to working precision.
/// </remarks>
public static class Balancing
{
    /// <summary>
    /// Balances the n-by-n column-major <paramref name="a"/> in place and returns the diagonal that
    /// did it: the balanced matrix is D⁻¹·A·D with D = diag(<c>result</c>), so an eigenvector of the
    /// balanced matrix becomes one of the original by multiplying entry i by <c>result[i]</c>.
    /// </summary>
    public static double[] InPlace(Span<double> a, int n)
    {
        var scale = new double[n];
        Array.Fill(scale, 1.0);

        const double Radix = 2.0;
        const double Squared = Radix * Radix;

        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 100)
        {
            changed = false;
            for (int i = 0; i < n; i++)
            {
                double row = 0;
                double column = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    row += Math.Abs(a[(j * n) + i]);
                    column += Math.Abs(a[(i * n) + j]);
                }

                if (row == 0 || column == 0)
                {
                    continue;
                }

                double factor = 1;
                double scaled = column;
                double before = column + row;

                while (scaled < row / Radix)
                {
                    factor *= Radix;
                    scaled *= Squared;
                }

                while (scaled >= row * Radix)
                {
                    factor /= Radix;
                    scaled /= Squared;
                }

                // Accept only a scaling that genuinely shrinks the pair of norms. Without the margin
                // the loop can trade one imbalance for an equal one and never settle.
                if ((scaled + (row / factor)) >= 0.95 * before)
                {
                    continue;
                }

                changed = true;
                scale[i] *= factor;
                for (int j = 0; j < n; j++)
                {
                    a[(j * n) + i] /= factor;
                    a[(i * n) + j] *= factor;
                }
            }
        }

        return scale;
    }
}
