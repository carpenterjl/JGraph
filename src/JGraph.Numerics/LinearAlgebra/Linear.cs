namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The dense operations MATLAB's matrix operators map onto: the <c>\</c> solver in its three shapes
/// (square LU, tall least-squares QR, wide minimum-norm QR), matrix multiplication, and integer
/// matrix powers.
/// </summary>
public static class Linear
{
    /// <summary>The spacing of one at double precision, which every rank tolerance is a multiple of.</summary>
    private const double DoubleSpacing = 2.220446049250313e-16;

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
    /// <exception cref="InvalidOperationException">A square A is singular.</exception>
    public static double[] Solve(double[] a, int m, int n, double[] b, int nrhs) =>
        Solve(a, m, n, b, nrhs, out _, out _);

    /// <summary>
    /// The same, saying what the rank decision was. A <paramref name="rank"/> short of
    /// <c>min(m, n)</c> is the deficiency MATLAB warns about rather than refuses, and
    /// <paramref name="tolerance"/> is the cut it was taken at — the two numbers that warning
    /// quotes. A square system takes no rank decision and reports its order.
    /// </summary>
    /// <exception cref="InvalidOperationException">A square A is singular.</exception>
    public static double[] Solve(double[] a, int m, int n, double[] b, int nrhs,
        out int rank, out double tolerance)
    {
        if (m == n)
        {
            rank = n;
            tolerance = 0.0;
            LuDecomposition lu = LuDecomposition.FactorAdopting(a, n);
            lu.SolveInPlace(b, nrhs, n);
            return b;
        }

        return BasicSolution(a, m, n, b, nrhs, out rank, out tolerance);
    }

    /// <summary>
    /// The <em>basic</em> least-squares solution of a rectangular system, which is the one MATLAB's
    /// <c>\</c> answers: factor A·P = Q·R with column pivoting, keep the leading columns whose
    /// diagonal entry clears the rank tolerance, solve that triangle, and leave a nought wherever
    /// the pivoting did not reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rectangular system rarely has one answer to give. An under-determined one has a whole
    /// affine space of exact solutions, and a rank-deficient over-determined one a whole space of
    /// least-squares ones; the two conventional ways to choose from that space disagree, and only
    /// one of them is MATLAB's. The minimum-norm solution is the shortest vector in it and spreads
    /// the answer over every column — it makes <c>[1 2 3] \ 2</c> into <c>[1; 2; 3]/7</c>. The
    /// basic solution carries the answer on the <c>rank(A)</c> columns the pivoting ranked first,
    /// and is <c>[0; 0; 2/3]</c>, which is what MATLAB prints.
    /// </para>
    /// <para>
    /// Every rectangular shape takes this road, not only the under-determined ones, and that is
    /// what lets a rank-deficient system answer sensibly at all. The unpivoted factorization behind
    /// <see cref="DenseLinalg.Gels"/> has no rank test worth the name — it asks whether a diagonal
    /// entry is exactly nought, which rounding sees to it is almost never true — so a deficient
    /// system came back with whatever a near-singular triangle produced. That was some point of the
    /// solution space rather than nonsense, but it was neither the shortest one nor MATLAB's.
    /// </para>
    /// </remarks>
    private static double[] BasicSolution(double[] a, int m, int n, double[] b, int nrhs,
        out int rank, out double tolerance)
    {
        int p = Math.Min(m, n);
        var x = new double[(long)n * nrhs];
        if (p == 0)
        {
            // Nothing to factor and no column to keep, which is the rank nought MATLAB reports for
            // zeros(0, 3) \ zeros(0, 1) — with a 3-by-1 of noughts to go with it.
            rank = 0;
            tolerance = 0.0;
            return x;
        }

        DenseLinalg provider = LinalgProvider.Current;
        var pivot = new int[n];
        var tau = new double[p];
        if (provider.Geqp3(m, n, a, m, pivot, tau) != 0)
        {
            throw new InvalidOperationException("The pivoted factorization failed.");
        }

        // The pivoting puts the largest diagonal entry first, so the whole rank decision reads off
        // that one number. This is MATLAB's tolerance exactly, down to the last digit it prints.
        tolerance = Math.Max(m, n) * DoubleSpacing * Math.Abs(a[0]);
        rank = 0;
        while (rank < p && Math.Abs(a[(rank * m) + rank]) > tolerance)
        {
            rank++;
        }

        if (rank == 0)
        {
            return x;
        }

        // Qᵀ·B, and then the leading triangle. Q is never formed: the reflectors apply straight to
        // the right-hand side, which is what keeps this one factorization rather than a
        // factorization and an m-by-m expansion after it.
        if (provider.Ormqr(leftSide: true, transpose: true, m, nrhs, p, a, m, tau, b, m) != 0)
        {
            throw new InvalidOperationException("The factorization's reflectors could not be applied.");
        }

        if (provider.Trtrs(lower: false, transpose: false, rank, nrhs, a, m, b, m) != 0)
        {
            throw new InvalidOperationException("The matrix is rank deficient to working precision.");
        }

        // Back through the pivoting, into a solution that is noughts everywhere the pivoting left.
        for (int c = 0; c < nrhs; c++)
        {
            for (int i = 0; i < rank; i++)
            {
                x[(c * n) + pivot[i] - 1] = b[(c * m) + i];
            }
        }

        return x;
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
