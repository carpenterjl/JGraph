namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense operations MATLAB's matrix operators map onto: the <c>\</c> solver in its three shapes
/// (square LU, tall least-squares QR, wide minimum-norm QR), matrix multiplication, and integer
/// matrix powers.
/// </summary>
public static class Linear
{
    /// <summary>
    /// Solves A·X = B, MATLAB's <c>A\B</c>: LU for square A, least squares for tall A, and the
    /// minimum-norm solution for wide A.
    /// </summary>
    /// <exception cref="InvalidOperationException">A is singular or rank deficient.</exception>
    /// <exception cref="ArgumentException">B's row count is not A's row count.</exception>
    public static double[,] Solve(double[,] a, double[,] b)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        if (b.GetLength(0) != m)
        {
            throw new ArgumentException("The right-hand side's row count must match the matrix's.", nameof(b));
        }

        if (m == n)
        {
            return LuDecomposition.Factor(a).SolveColumns(b);
        }

        if (m > n)
        {
            return QrDecomposition.Factor(a).SolveColumns(b);
        }

        // Wide: factor Aᵀ = Q·R, forward-solve Rᵀ·y = B, then X = Q·y — the minimum-norm solution.
        QrDecomposition qr = QrDecomposition.Factor(Transpose(a));
        if (!qr.IsFullRank)
        {
            throw new InvalidOperationException("The matrix is rank deficient to working precision.");
        }

        double[,] r = qr.R;
        int columns = b.GetLength(1);
        var y = new double[m, columns];
        for (int k = 0; k < m; k++)
        {
            for (int c = 0; c < columns; c++)
            {
                double s = b[k, c];
                for (int j = 0; j < k; j++)
                {
                    s -= r[j, k] * y[j, c];
                }

                y[k, c] = s / r[k, k];
            }
        }

        return Multiply(qr.Q, y);
    }

    /// <summary>The matrix product A·B.</summary>
    /// <exception cref="ArgumentException">The inner dimensions disagree.</exception>
    public static double[,] Multiply(double[,] a, double[,] b)
    {
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        if (b.GetLength(0) != inner)
        {
            throw new ArgumentException("Inner matrix dimensions must agree.", nameof(b));
        }

        var product = new double[m, n];

        // Worthwhile products go through the provider (native gemm when loaded). The row-major
        // rectangles this layer works in cost two O(n²) transposing copies against the O(n³)
        // multiply; below the threshold the naive loop wins on overhead.
        if (LinalgProvider.Current.IsNative && 2L * m * inner * n >= 1_000_000)
        {
            var flatA = new double[(long)m * inner];
            for (int r = 0; r < m; r++)
            {
                for (int k = 0; k < inner; k++)
                {
                    flatA[(k * m) + r] = a[r, k];
                }
            }

            var flatB = new double[(long)inner * n];
            for (int k = 0; k < inner; k++)
            {
                for (int c = 0; c < n; c++)
                {
                    flatB[(c * inner) + k] = b[k, c];
                }
            }

            var flat = new double[(long)m * n];
            LinalgProvider.Current.Gemm(transA: false, transB: false, m, n, inner, flatA, m, flatB, inner, flat, m);
            for (int c = 0; c < n; c++)
            {
                int origin = c * m;
                for (int r = 0; r < m; r++)
                {
                    product[r, c] = flat[origin + r];
                }
            }

            return product;
        }

        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    sum += a[r, k] * b[k, c];
                }

                product[r, c] = sum;
            }
        }

        return product;
    }

    /// <summary>The n-by-n identity matrix.</summary>
    public static double[,] Identity(int n)
    {
        var identity = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = 1;
        }

        return identity;
    }

    /// <summary>A^p for a square A and integer p (negative p inverts first), by repeated squaring.</summary>
    /// <exception cref="ArgumentException">A is not square.</exception>
    /// <exception cref="InvalidOperationException">p is negative and A is singular.</exception>
    public static double[,] Power(double[,] a, int exponent)
    {
        int n = a.GetLength(0);
        if (a.GetLength(1) != n)
        {
            throw new ArgumentException("A matrix power needs a square matrix.", nameof(a));
        }

        double[,] baseMatrix = exponent < 0 ? LuDecomposition.Factor(a).Inverse() : (double[,])a.Clone();
        int remaining = Math.Abs(exponent);
        double[,] result = Identity(n);
        while (remaining > 0)
        {
            if ((remaining & 1) == 1)
            {
                result = Multiply(result, baseMatrix);
            }

            remaining >>= 1;
            if (remaining > 0)
            {
                baseMatrix = Multiply(baseMatrix, baseMatrix);
            }
        }

        return result;
    }

    /// <summary>Aᵀ.</summary>
    public static double[,] Transpose(double[,] a)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        var t = new double[n, m];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                t[c, r] = a[r, c];
            }
        }

        return t;
    }
}
