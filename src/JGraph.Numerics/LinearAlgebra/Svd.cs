namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Singular value decomposition A = U·S·Vᵀ by one-sided Jacobi rotations — a compact algorithm
/// whose singular values converge to full working precision. Backs <c>svd</c>, <c>rank</c>, and the
/// matrix 2-norm.
/// </summary>
public sealed class Svd
{
    private Svd(double[] values, double[,] u, double[,] v)
    {
        Values = values;
        U = u;
        V = v;
    }

    /// <summary>The singular values, in descending order.</summary>
    public double[] Values { get; }

    /// <summary>The left singular vectors (economy size: m-by-min(m,n), orthonormal columns).</summary>
    public double[,] U { get; }

    /// <summary>The right singular vectors (n-by-min(m,n), orthonormal columns).</summary>
    public double[,] V { get; }

    /// <summary>The numeric rank: singular values above max(m,n)·eps·σ₁, MATLAB's default tolerance.</summary>
    public int Rank(int rows, int cols)
    {
        double tolerance = Math.Max(rows, cols) * 2.220446049250313e-16 * (Values.Length > 0 ? Values[0] : 0);
        int rank = 0;
        foreach (double s in Values)
        {
            if (s > tolerance)
            {
                rank++;
            }
        }

        return rank;
    }

    /// <summary>Factors <paramref name="matrix"/>; the input is not modified.</summary>
    public static Svd Factor(double[,] matrix)
    {
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);

        // One-sided Jacobi wants at least as many rows as columns; a wide matrix factors as Aᵀ
        // with U and V swapped.
        if (m < n)
        {
            Svd wide = Factor(Transpose(matrix));
            return new Svd(wide.Values, wide.V, wide.U);
        }

        var b = (double[,])matrix.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            v[i, i] = 1;
        }

        // Sweep column pairs, rotating each pair orthogonal, until every pair already is.
        const double Eps = 2.220446049250313e-16;
        for (int sweep = 0; sweep < 60; sweep++)
        {
            bool rotated = false;
            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double alpha = 0, beta = 0, gamma = 0;
                    for (int r = 0; r < m; r++)
                    {
                        alpha += b[r, p] * b[r, p];
                        beta += b[r, q] * b[r, q];
                        gamma += b[r, p] * b[r, q];
                    }

                    if (Math.Abs(gamma) <= Eps * Math.Sqrt(alpha * beta) || gamma == 0)
                    {
                        continue;
                    }

                    rotated = true;
                    double zeta = (beta - alpha) / (2 * gamma);
                    double t = Math.Sign(zeta) / (Math.Abs(zeta) + Math.Sqrt(1 + (zeta * zeta)));
                    double c = 1 / Math.Sqrt(1 + (t * t));
                    double s = c * t;

                    for (int r = 0; r < m; r++)
                    {
                        double bp = b[r, p];
                        b[r, p] = (c * bp) - (s * b[r, q]);
                        b[r, q] = (s * bp) + (c * b[r, q]);
                    }

                    for (int r = 0; r < n; r++)
                    {
                        double vp = v[r, p];
                        v[r, p] = (c * vp) - (s * v[r, q]);
                        v[r, q] = (s * vp) + (c * v[r, q]);
                    }
                }
            }

            if (!rotated)
            {
                break;
            }
        }

        // Singular values are the rotated columns' norms; U their normalized directions.
        var sigma = new double[n];
        var u = new double[m, n];
        for (int c = 0; c < n; c++)
        {
            double norm = 0;
            for (int r = 0; r < m; r++)
            {
                norm += b[r, c] * b[r, c];
            }

            sigma[c] = Math.Sqrt(norm);
            if (sigma[c] > 0)
            {
                for (int r = 0; r < m; r++)
                {
                    u[r, c] = b[r, c] / sigma[c];
                }
            }
        }

        SortDescending(sigma, u, v);
        CompleteZeroColumns(sigma, u);
        return new Svd(sigma, u, v);
    }

    private static void SortDescending(double[] sigma, double[,] u, double[,] v)
    {
        int n = sigma.Length;
        for (int i = 0; i < n - 1; i++)
        {
            int biggest = i;
            for (int j = i + 1; j < n; j++)
            {
                if (sigma[j] > sigma[biggest])
                {
                    biggest = j;
                }
            }

            if (biggest != i)
            {
                (sigma[i], sigma[biggest]) = (sigma[biggest], sigma[i]);
                SwapColumns(u, i, biggest);
                SwapColumns(v, i, biggest);
            }
        }
    }

    /// <summary>
    /// A zero singular value leaves its U column zero; replace it with a unit vector orthogonal to
    /// the others (Gram–Schmidt over basis vectors) so U keeps orthonormal columns.
    /// </summary>
    private static void CompleteZeroColumns(double[] sigma, double[,] u)
    {
        int m = u.GetLength(0);
        int n = u.GetLength(1);
        for (int c = 0; c < n; c++)
        {
            if (sigma[c] > 0)
            {
                continue;
            }

            for (int basis = 0; basis < m; basis++)
            {
                var candidate = new double[m];
                candidate[basis] = 1;
                for (int other = 0; other < n; other++)
                {
                    if (other == c)
                    {
                        continue;
                    }

                    double projection = 0;
                    for (int r = 0; r < m; r++)
                    {
                        projection += candidate[r] * u[r, other];
                    }

                    for (int r = 0; r < m; r++)
                    {
                        candidate[r] -= projection * u[r, other];
                    }
                }

                double norm = 0;
                for (int r = 0; r < m; r++)
                {
                    norm += candidate[r] * candidate[r];
                }

                if (norm > 0.5)
                {
                    norm = Math.Sqrt(norm);
                    for (int r = 0; r < m; r++)
                    {
                        u[r, c] = candidate[r] / norm;
                    }

                    break;
                }
            }
        }
    }

    private static void SwapColumns(double[,] matrix, int a, int b)
    {
        int rows = matrix.GetLength(0);
        for (int r = 0; r < rows; r++)
        {
            (matrix[r, a], matrix[r, b]) = (matrix[r, b], matrix[r, a]);
        }
    }

    private static double[,] Transpose(double[,] matrix)
    {
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);
        var t = new double[n, m];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                t[c, r] = matrix[r, c];
            }
        }

        return t;
    }
}
