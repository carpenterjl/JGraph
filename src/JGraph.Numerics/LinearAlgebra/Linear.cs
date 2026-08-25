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

        int nrhs = b.GetLength(1);
        var work = new double[(long)m * n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                work[(c * m) + r] = a[r, c];
            }
        }

        var rhs = new double[(long)m * nrhs];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < nrhs; c++)
            {
                rhs[(c * m) + r] = b[r, c];
            }
        }

        double[] solution = Solve(work, m, n, rhs, nrhs);
        var x = new double[n, nrhs];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < nrhs; c++)
            {
                x[r, c] = solution[(c * n) + r];
            }
        }

        return x;
    }

    /// <summary>
    /// The same solve over flat column-major arrays — the layout packed script storage already uses,
    /// so the operator path reaches the kernels without a rectangle in between. A is m-by-n and B is
    /// m-by-nrhs; <em>both are overwritten</em>, and the n-by-nrhs solution comes back as an array
    /// that may be <paramref name="b"/> itself.
    /// </summary>
    /// <exception cref="InvalidOperationException">A is singular or rank deficient.</exception>
    public static double[] Solve(double[] a, int m, int n, double[] b, int nrhs)
    {
        if (m == n)
        {
            LuDecomposition lu = LuDecomposition.FactorAdopting(a, n);
            lu.SolveInPlace(b, nrhs, n);
            return b;
        }

        // Over- and under-determined systems go to the provider's least-squares/minimum-norm solve,
        // which is one blocked QR natively and the hand-rolled Householder factorization on the
        // managed fallback. Both want the right-hand side padded to max(m, n) rows, so an
        // under-determined system's wider solution has somewhere to land.
        int height = Math.Max(m, n);
        double[] rhs = b;
        if (height != m)
        {
            rhs = new double[(long)height * nrhs];
            for (int c = 0; c < nrhs; c++)
            {
                b.AsSpan(c * m, m).CopyTo(rhs.AsSpan(c * height, m));
            }
        }

        if (LinalgProvider.Current.Gels(m, n, nrhs, a, m, rhs, height) != 0)
        {
            throw new InvalidOperationException("The matrix is rank deficient to working precision.");
        }

        if (height == n)
        {
            return rhs;
        }

        var solution = new double[(long)n * nrhs];
        for (int c = 0; c < nrhs; c++)
        {
            rhs.AsSpan(c * height, n).CopyTo(solution.AsSpan(c * n, n));
        }

        return solution;
    }

    /// <summary>
    /// The managed backend's least-squares solve, in LAPACK's <c>dgels</c> shape: the Householder QR
    /// of A for a tall system, and the QR of Aᵀ with a forward solve for a wide one — the two
    /// branches the <c>\</c> operator has always taken, now reached through the provider so the
    /// fallback answers exactly what it answered before.
    /// </summary>
    internal static int LeastSquaresManaged(int m, int n, int nrhs, Span<double> a, int lda, Span<double> b, int ldb)
    {
        var rect = new double[m, n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                rect[r, c] = a[(c * lda) + r];
            }
        }

        var rhs = new double[m, nrhs];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < nrhs; c++)
            {
                rhs[r, c] = b[(c * ldb) + r];
            }
        }

        double[,] x;
        if (m > n)
        {
            QrDecomposition tall = QrDecomposition.Factor(rect);
            if (!tall.IsFullRank)
            {
                return 1;
            }

            x = tall.SolveColumns(rhs);
        }
        else
        {
            // Wide: factor Aᵀ = Q·R, forward-solve Rᵀ·y = B, then X = Q·y — the minimum-norm solution.
            QrDecomposition qr = QrDecomposition.Factor(Transpose(rect));
            if (!qr.IsFullRank)
            {
                return 1;
            }

            double[,] r = qr.R;
            var y = new double[m, nrhs];
            for (int k = 0; k < m; k++)
            {
                for (int c = 0; c < nrhs; c++)
                {
                    double s = rhs[k, c];
                    for (int j = 0; j < k; j++)
                    {
                        s -= r[j, k] * y[j, c];
                    }

                    y[k, c] = s / r[k, k];
                }
            }

            x = Multiply(qr.Q, y);
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < nrhs; c++)
            {
                b[(c * ldb) + r] = x[r, c];
            }
        }

        return 0;
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
