using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Gauss-Jordan elimination with partial pivoting, carried all the way to reduced row echelon form.
/// </summary>
/// <remarks>
/// This is the textbook elimination and not a factorization, which is deliberate: reduced row
/// echelon form is a teaching answer, and what a reader wants from it is the shape they would have
/// got by hand — the same pivots, the same order, the same columns declared negligible — rather
/// than the numerically better answer a factorization would give.
/// </remarks>
public static class RowEchelon
{
    /// <summary>
    /// Reduces <paramref name="a"/> in place and answers the one-based indices of the columns that
    /// held a pivot.
    /// </summary>
    /// <param name="a">The matrix, overwritten with its reduced row echelon form.</param>
    /// <param name="tolerance">
    /// A column whose largest remaining entry does not exceed this is taken to be nought and is
    /// zeroed outright rather than pivoted on.
    /// </param>
    public static int[] Reduce(Complex[,] a, double tolerance)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        var pivots = new List<int>();

        int row = 0;
        int col = 0;
        while (row < m && col < n)
        {
            // The pivot is the largest remaining entry of the column, which is what keeps the
            // multipliers below one and is the only numerical care this routine takes.
            double best = -1.0;
            int at = row;
            for (int i = row; i < m; i++)
            {
                double size = a[i, col].Magnitude;
                if (size > best)
                {
                    best = size;
                    at = i;
                }
            }

            if (best <= tolerance)
            {
                for (int i = row; i < m; i++)
                {
                    a[i, col] = Complex.Zero;
                }

                col++;
                continue;
            }

            pivots.Add(col + 1);
            if (at != row)
            {
                for (int c = col; c < n; c++)
                {
                    (a[row, c], a[at, c]) = (a[at, c], a[row, c]);
                }
            }

            Complex pivot = a[row, col];
            for (int c = col; c < n; c++)
            {
                a[row, c] /= pivot;
            }

            for (int i = 0; i < m; i++)
            {
                if (i == row)
                {
                    continue;
                }

                Complex factor = a[i, col];
                if (factor == Complex.Zero)
                {
                    continue;
                }

                for (int c = col; c < n; c++)
                {
                    a[i, c] -= factor * a[row, c];
                }
            }

            row++;
            col++;
        }

        return [.. pivots];
    }

    /// <summary>
    /// The default tolerance: the larger dimension times the spacing of one times the matrix's
    /// infinity norm — the largest absolute row sum.
    /// </summary>
    public static double DefaultTolerance(Complex[,] a)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        double worst = 0.0;
        for (int i = 0; i < m; i++)
        {
            double sum = 0.0;
            for (int c = 0; c < n; c++)
            {
                sum += a[i, c].Magnitude;
            }

            worst = Math.Max(worst, sum);
        }

        return Math.Max(m, n) * 2.220446049250313e-16 * worst;
    }
}
