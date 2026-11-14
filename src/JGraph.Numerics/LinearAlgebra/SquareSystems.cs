namespace JGraph.Numerics.LinearAlgebra;

/// <summary>How well a square system could be solved, in the four words MATLAB's warnings use.</summary>
public enum SquareCondition
{
    /// <summary>Nothing to say: the reciprocal condition number is at least eps.</summary>
    Fine,

    /// <summary><c>MATLAB:nearlySingularMatrix</c>: RCOND is below eps.</summary>
    NearlySingular,

    /// <summary><c>MATLAB:singularMatrix</c>: a zero pivot, or an RCOND of exactly zero.</summary>
    Singular,

    /// <summary><c>MATLAB:illConditionedMatrix</c>: RCOND is NaN (a NaN in the matrix).</summary>
    IllConditioned,
}

/// <summary>
/// MATLAB's <c>A\B</c> and <c>inv(A)</c> for a square dense real <c>A</c>, answering where the
/// matrix is singular rather than refusing, and saying how well it went (open item 307, ADR 0355).
/// </summary>
/// <remarks>
/// <para>
/// R2025b warns and answers: <c>inv([1 2; 2 4])</c> is all Inf under <c>MATLAB:singularMatrix</c>,
/// <c>[1 2; 2 4] \ [1; 2]</c> is NaN, <c>magic(4) \ b</c> is an answer under
/// <c>MATLAB:nearlySingularMatrix</c> with RCOND in the text. The answers on a singular system
/// are what its factorization's substitutions give with IEEE division, so they are computed that
/// way here, in managed code on both linear-algebra backends, so the two agree.
/// </para>
/// <para>
/// A triangular matrix is solved by substitution, lower before upper (a diagonal one is both, and
/// R2025b's answer for <c>diag([1 0 2]) \ ones(3, 1)</c> is forward substitution's
/// <c>[1; Inf; NaN]</c>). Anything else takes the partial-pivot LU. R2025b's symmetric and
/// Hessenberg roads (Cholesky, LDLᵀ) are not taken: on a nonsingular system they answer the same
/// solution to rounding, and on a singular symmetric one the Inf and NaN can fall elsewhere.
/// </para>
/// </remarks>
public static class SquareSystems
{
    /// <summary>The spacing of one, MATLAB's eps: the RCOND below which a system is nearly singular.</summary>
    private const double Eps = 2.220446049250313e-16;

    /// <summary>The spacing of one in single precision: a single system's threshold.</summary>
    private const double SingleEps = 1.1920928955078125e-07;

    /// <summary>
    /// The backend a system holding a NaN is factored on. The native LAPACKE entry points check
    /// their inputs for NaN and return without touching them, which left <c>inv([1 NaN; 2 3])</c>
    /// answering its own argument; the managed kernels let the NaN through as R2025b's do.
    /// </summary>
    private static readonly ManagedLinalg NaNThrough = new();

    /// <summary>
    /// Solves A·X = B for an n-by-n column-major <paramref name="a"/> (overwritten) and an
    /// n-by-<paramref name="nrhs"/> column-major <paramref name="b"/> (overwritten with X). A
    /// <paramref name="single"/> system is nearly singular below single's eps, as R2025b says it.
    /// </summary>
    public static SquareCondition Solve(double[] a, int n, Span<double> b, int nrhs, out double rcond, bool single = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        rcond = double.PositiveInfinity;
        if (n == 0)
        {
            return SquareCondition.Fine;
        }

        double anorm = DenseLinalg.OneNorm(n, n, a, n);
        if (IsLowerTriangular(a, n) || IsUpperTriangular(a, n))
        {
            bool lower = IsLowerTriangular(a, n);
            bool zeroDiagonal = HasZeroDiagonal(a, n);
            // R2025b's triangular solve judges by its diagonal alone: RCOND is the smallest
            // magnitude over the largest (measured: [1 1; 0 1e-17] says 1e-17 where the true
            // 1-norm figure is 5e-18, and [1 1e20; 0 1] says nothing at all). inv does not.
            rcond = zeroDiagonal ? 0 : DiagonalRatio(a, n);
            for (int c = 0; c < nrhs; c++)
            {
                Span<double> column = b.Slice(c * n, n);
                if (lower)
                {
                    ForwardSubstitute(a, n, column, unitDiagonal: false);
                }
                else
                {
                    BackSubstitute(a, n, column);
                }
            }

            return Classify(zeroDiagonal, 0, rcond, single);
        }

        var ipiv = new int[n];
        DenseLinalg provider = HasNaN(a) || HasNaN(b) ? NaNThrough : LinalgProvider.Current;
        provider.Getrf(n, n, a, n, ipiv);
        if (HasZeroDiagonal(a, n))
        {
            rcond = 0;
            SubstituteThroughLu(a, n, ipiv, b, nrhs);
            return SquareCondition.Singular;
        }

        rcond = ReciprocalCondition(provider, a, n, anorm);
        provider.Getrs(transpose: false, n, nrhs, a, n, ipiv, b, n);
        return Classify(false, anorm, rcond, single);
    }

    /// <summary>
    /// The inverse of an n-by-n column-major <paramref name="a"/> (overwritten), as R2025b's
    /// <c>inv</c> answers it: a diagonal matrix inverts element by element; a triangular one with
    /// a zero on its diagonal is Inf over its triangle; any other matrix with a zero pivot is Inf
    /// throughout; and anything else is the LU inverse.
    /// </summary>
    public static SquareCondition Invert(double[] a, int n, out double[] inverse, out double rcond, bool single = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        rcond = double.PositiveInfinity;
        inverse = new double[(long)n * n];
        if (n == 0)
        {
            return SquareCondition.Fine;
        }

        double anorm = DenseLinalg.OneNorm(n, n, a, n);
        bool lower = IsLowerTriangular(a, n);
        bool upper = IsUpperTriangular(a, n);
        if (lower && upper)
        {
            bool zero = HasZeroDiagonal(a, n);
            for (int i = 0; i < n; i++)
            {
                inverse[((long)i * n) + i] = 1.0 / a[((long)i * n) + i];
            }

            rcond = zero ? 0 : TriangularRcond(a, n, lower: false, anorm);
            return Classify(zero, anorm, rcond, single);
        }

        if ((lower || upper) && HasZeroDiagonal(a, n))
        {
            rcond = 0;
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    if (lower ? r >= c : r <= c)
                    {
                        inverse[((long)c * n) + r] = double.PositiveInfinity;
                    }
                }
            }

            return SquareCondition.Singular;
        }

        if (lower || upper)
        {
            // A triangle inverts as a triangle, never through a pivoted LU, which can find an exact
            // zero pivot in a nonsingular lower triangle (measured: inv([1 0 0; 2 1e-16 0; 3 4 7])
            // is R2025b's answer under nearlySingularMatrix, not Inf).
            DenseLinalg solver = HasNaN(a) ? NaNThrough : LinalgProvider.Current;
            for (int i = 0; i < n; i++)
            {
                inverse[((long)i * n) + i] = 1.0;
            }

            solver.Trtrs(lower, transpose: false, n, n, a, n, inverse, n);
            rcond = TriangularRcond(a, n, lower, anorm);
            return Classify(false, anorm, rcond, single);
        }

        var ipiv = new int[n];
        DenseLinalg provider = HasNaN(a) ? NaNThrough : LinalgProvider.Current;
        provider.Getrf(n, n, a, n, ipiv);
        if (HasZeroDiagonal(a, n))
        {
            rcond = 0;
            Array.Fill(inverse, double.PositiveInfinity);
            return SquareCondition.Singular;
        }

        rcond = ReciprocalCondition(provider, a, n, anorm);
        a.AsSpan(0, n * n).CopyTo(inverse);
        provider.Getri(n, inverse, n, ipiv);
        return Classify(false, anorm, rcond, single);
    }

    private static SquareCondition Classify(bool zeroPivot, double anorm, double rcond, bool single)
    {
        if (zeroPivot)
        {
            return SquareCondition.Singular;
        }

        if (double.IsNaN(anorm) || double.IsNaN(rcond))
        {
            return SquareCondition.IllConditioned;
        }

        if (rcond == 0)
        {
            return SquareCondition.Singular;
        }

        return rcond < (single ? SingleEps : Eps) ? SquareCondition.NearlySingular : SquareCondition.Fine;
    }

    /// <summary>RCOND from an LU factorization with no zero pivot: zero for an infinite norm, NaN for a NaN one.</summary>
    private static double ReciprocalCondition(DenseLinalg provider, double[] lu, int n, double anorm)
    {
        if (double.IsNaN(anorm))
        {
            return double.NaN;
        }

        if (double.IsInfinity(anorm))
        {
            return 0;
        }

        for (long i = 0; i < (long)n * n; i++)
        {
            if (double.IsNaN(lu[i]))
            {
                return double.NaN;
            }
        }

        return provider.Gecon(n, lu, n, anorm);
    }

    /// <summary>
    /// RCOND of a triangular matrix with no zero on its diagonal, through the LU estimator: an upper
    /// triangle is its own factorization (L = I), and a lower one is L·D⁻¹ times D.
    /// </summary>
    private static double TriangularRcond(double[] a, int n, bool lower, double anorm)
    {
        if (double.IsNaN(anorm) || double.IsInfinity(anorm))
        {
            return double.IsNaN(anorm) ? double.NaN : 0;
        }

        var factors = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            double pivot = a[((long)c * n) + c];
            for (int r = 0; r < n; r++)
            {
                double v = a[((long)c * n) + r];
                factors[((long)c * n) + r] = lower ? (r > c ? v / pivot : r == c ? v : 0) : (r <= c ? v : 0);
            }
        }

        return ReciprocalCondition(LinalgProvider.Current, factors, n, anorm);
    }

    /// <summary>The smallest diagonal magnitude over the largest; NaN when the diagonal holds one.</summary>
    private static double DiagonalRatio(double[] a, int n)
    {
        double smallest = double.PositiveInfinity;
        double largest = 0;
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(a[((long)i * n) + i]);
            if (double.IsNaN(d))
            {
                return double.NaN;
            }

            smallest = Math.Min(smallest, d);
            largest = Math.Max(largest, d);
        }

        return smallest / largest;
    }

    private static bool IsLowerTriangular(double[] a, int n)
    {
        for (int c = 1; c < n; c++)
        {
            for (int r = 0; r < c; r++)
            {
                if (a[((long)c * n) + r] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsUpperTriangular(double[] a, int n)
    {
        for (int c = 0; c < n - 1; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[((long)c * n) + r] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasNaN(ReadOnlySpan<double> values)
    {
        foreach (double v in values)
        {
            if (double.IsNaN(v))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasZeroDiagonal(double[] a, int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (a[((long)i * n) + i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>L·x = b in place, column by column as the reference <c>dtrsm</c> runs it, with IEEE division.</summary>
    private static void ForwardSubstitute(double[] a, int n, Span<double> x, bool unitDiagonal)
    {
        for (int k = 0; k < n; k++)
        {
            if (!unitDiagonal)
            {
                x[k] /= a[((long)k * n) + k];
            }

            double xk = x[k];
            for (int i = k + 1; i < n; i++)
            {
                x[i] -= xk * a[((long)k * n) + i];
            }
        }
    }

    /// <summary>U·x = b in place, column by column from the last, with IEEE division.</summary>
    private static void BackSubstitute(double[] a, int n, Span<double> x)
    {
        for (int k = n - 1; k >= 0; k--)
        {
            x[k] /= a[((long)k * n) + k];
            double xk = x[k];
            for (int i = 0; i < k; i++)
            {
                x[i] -= xk * a[((long)k * n) + i];
            }
        }
    }

    /// <summary>
    /// A singular LU's substitutions: the row interchanges, the unit lower triangle, then the upper
    /// one with its zero pivot, so 0/0 and x/0 land where R2025b's land.
    /// </summary>
    private static void SubstituteThroughLu(double[] lu, int n, int[] ipiv, Span<double> b, int nrhs)
    {
        for (int c = 0; c < nrhs; c++)
        {
            Span<double> column = b.Slice(c * n, n);
            for (int i = 0; i < n; i++)
            {
                int p = ipiv[i] - 1;
                if (p != i)
                {
                    (column[i], column[p]) = (column[p], column[i]);
                }
            }

            ForwardSubstitute(lu, n, column, unitDiagonal: true);
            BackSubstitute(lu, n, column);
        }
    }
}
