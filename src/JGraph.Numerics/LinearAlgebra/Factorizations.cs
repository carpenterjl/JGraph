namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The Cholesky factorization A = LLᵀ of a symmetric positive definite matrix.
/// </summary>
public sealed class Cholesky
{
    private Cholesky(double[,] lower, bool positiveDefinite)
    {
        Lower = lower;
        IsPositiveDefinite = positiveDefinite;
    }

    /// <summary>The lower triangular factor L, with L·Lᵀ = A.</summary>
    public double[,] Lower { get; }

    /// <summary>
    /// Whether the factorization succeeded. A matrix that is not positive definite runs into a
    /// non-positive pivot, which is exactly the standard test for definiteness.
    /// </summary>
    public bool IsPositiveDefinite { get; }

    /// <summary>Factors a square matrix, reading only its lower triangle as MATLAB's <c>chol</c> does.</summary>
    public static Cholesky Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        var lower = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = matrix[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= lower[i, k] * lower[j, k];
                }

                if (i == j)
                {
                    if (sum <= 0)
                    {
                        return new Cholesky(lower, positiveDefinite: false);
                    }

                    lower[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    lower[i, j] = sum / lower[j, j];
                }
            }
        }

        return new Cholesky(lower, positiveDefinite: true);
    }
}

/// <summary>
/// The LDLᵀ factorization of a symmetric matrix: P·A·Pᵀ = L·D·Lᵀ with L unit lower triangular and
/// D diagonal.
/// </summary>
/// <remarks>
/// Pivoting is symmetric and 1×1 — at each step the largest remaining diagonal entry takes the
/// pivot — which keeps P·A·Pᵀ symmetric and covers every definite and most indefinite matrices.
/// It cannot handle a matrix whose remaining diagonal is entirely zero (<c>[0 1; 1 0]</c> is the
/// small example); LAPACK's 2×2 block pivoting is what those need, and
/// <see cref="IsFactored"/> reports the case rather than returning nonsense.
/// </remarks>
public sealed class Ldl
{
    private Ldl(double[,] lower, double[] diagonal, int[] order, bool factored)
    {
        Lower = lower;
        Diagonal = diagonal;
        Order = order;
        IsFactored = factored;
    }

    /// <summary>The unit lower triangular factor L.</summary>
    public double[,] Lower { get; }

    /// <summary>The diagonal of D.</summary>
    public double[] Diagonal { get; }

    /// <summary>The pivot order: row <c>Order[i]</c> of A became row i of the factored matrix.</summary>
    public int[] Order { get; }

    /// <summary>Whether the factorization completed; false when a 2×2 pivot block would be needed.</summary>
    public bool IsFactored { get; }

    /// <summary>The permutation P as a matrix, so that P·A·Pᵀ = L·D·Lᵀ.</summary>
    public double[,] Permutation
    {
        get
        {
            int n = Order.Length;
            var p = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                p[i, Order[i]] = 1;
            }

            return p;
        }
    }

    /// <summary>Factors a symmetric matrix.</summary>
    public static Ldl Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        var lower = Linear.Identity(n);
        var diagonal = new double[n];

        for (int k = 0; k < n; k++)
        {
            // Symmetric pivot: swap the largest remaining diagonal entry into position, rows and
            // columns together so the working matrix stays symmetric.
            int best = k;
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(a[i, i]) > Math.Abs(a[best, best]))
                {
                    best = i;
                }
            }

            if (best != k)
            {
                SwapSymmetric(a, n, k, best);
                (order[k], order[best]) = (order[best], order[k]);
                for (int c = 0; c < k; c++)
                {
                    (lower[k, c], lower[best, c]) = (lower[best, c], lower[k, c]);
                }
            }

            diagonal[k] = a[k, k];
            if (diagonal[k] == 0)
            {
                return new Ldl(lower, diagonal, order, factored: false);
            }

            for (int i = k + 1; i < n; i++)
            {
                double multiplier = a[i, k] / diagonal[k];
                lower[i, k] = multiplier;
                for (int j = k + 1; j <= i; j++)
                {
                    a[i, j] -= multiplier * a[j, k];
                    a[j, i] = a[i, j];
                }
            }
        }

        return new Ldl(lower, diagonal, order, factored: true);
    }

    private static void SwapSymmetric(double[,] a, int n, int i, int j)
    {
        for (int c = 0; c < n; c++)
        {
            (a[i, c], a[j, c]) = (a[j, c], a[i, c]);
        }

        for (int r = 0; r < n; r++)
        {
            (a[r, i], a[r, j]) = (a[r, j], a[r, i]);
        }
    }
}

/// <summary>
/// The upper Hessenberg reduction A = Q·H·Qᵀ by Householder reflections — the first half of every
/// eigenvalue algorithm, and MATLAB's <c>hess</c>.
/// </summary>
public sealed class Hessenberg
{
    private Hessenberg(double[,] h, double[,] q)
    {
        H = h;
        Q = q;
    }

    /// <summary>The upper Hessenberg matrix: zero below the first subdiagonal.</summary>
    public double[,] H { get; }

    /// <summary>The orthogonal similarity transform, with Q·H·Qᵀ = A.</summary>
    public double[,] Q { get; }

    /// <summary>Reduces a square matrix to upper Hessenberg form.</summary>
    public static Hessenberg Reduce(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        var h = (double[,])matrix.Clone();
        var q = Linear.Identity(n);

        for (int k = 0; k < n - 2; k++)
        {
            // The reflector that zeroes column k below the subdiagonal.
            double norm = 0;
            for (int i = k + 1; i < n; i++)
            {
                norm += h[i, k] * h[i, k];
            }

            norm = Math.Sqrt(norm);
            if (norm == 0)
            {
                continue;
            }

            double alpha = h[k + 1, k] > 0 ? -norm : norm;
            var v = new double[n];
            v[k + 1] = h[k + 1, k] - alpha;
            for (int i = k + 2; i < n; i++)
            {
                v[i] = h[i, k];
            }

            double vv = 0;
            for (int i = k + 1; i < n; i++)
            {
                vv += v[i] * v[i];
            }

            if (vv == 0)
            {
                continue;
            }

            ApplyLeft(h, v, vv, n);
            ApplyRight(h, v, vv, n);
            ApplyRight(q, v, vv, n);
        }

        // The reflections leave rounding dust below the subdiagonal; Hessenberg form means exactly
        // zero there, and a caller testing istriu on the result should see that.
        for (int r = 2; r < n; r++)
        {
            for (int c = 0; c < r - 1; c++)
            {
                h[r, c] = 0;
            }
        }

        return new Hessenberg(h, q);
    }

    /// <summary>M ← (I - 2vvᵀ/vᵀv)·M.</summary>
    private static void ApplyLeft(double[,] m, double[] v, double vv, int n)
    {
        for (int c = 0; c < n; c++)
        {
            double dot = 0;
            for (int r = 0; r < n; r++)
            {
                dot += v[r] * m[r, c];
            }

            dot = 2.0 * dot / vv;
            for (int r = 0; r < n; r++)
            {
                m[r, c] -= dot * v[r];
            }
        }
    }

    /// <summary>M ← M·(I - 2vvᵀ/vᵀv).</summary>
    private static void ApplyRight(double[,] m, double[] v, double vv, int n)
    {
        for (int r = 0; r < n; r++)
        {
            double dot = 0;
            for (int c = 0; c < n; c++)
            {
                dot += m[r, c] * v[c];
            }

            dot = 2.0 * dot / vv;
            for (int c = 0; c < n; c++)
            {
                m[r, c] -= dot * v[c];
            }
        }
    }
}

/// <summary>Functions of a whole matrix, as distinct from functions applied element by element.</summary>
public static class MatrixFunctions
{
    /// <summary>
    /// The matrix exponential e^A, by scaling and squaring around a Padé approximant: the matrix is
    /// halved until its norm is small enough for the approximant to be accurate, then the result is
    /// squared back. Evaluating the Taylor series directly instead is the classic way to get a
    /// badly wrong answer for a matrix with a large norm.
    /// </summary>
    public static double[,] Exponential(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);

        double norm = 0;
        for (int r = 0; r < n; r++)
        {
            double rowSum = 0;
            for (int c = 0; c < n; c++)
            {
                rowSum += Math.Abs(matrix[r, c]);
            }

            norm = Math.Max(norm, rowSum);
        }

        int squarings = norm > 0.5 ? (int)Math.Max(0, Math.Floor(Math.Log2(norm)) + 2) : 0;
        double scale = Math.Pow(2, -squarings);
        var a = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                a[r, c] = matrix[r, c] * scale;
            }
        }

        const int Order = 6;
        double[,] numerator = Linear.Identity(n);
        double[,] denominator = Linear.Identity(n);
        double[,] power = Linear.Identity(n);
        double coefficient = 1;
        bool negative = true;
        for (int k = 1; k <= Order; k++)
        {
            coefficient = coefficient * (Order - k + 1) / (((2 * Order) - k + 1) * (double)k);
            power = Linear.Multiply(a, power);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    double term = coefficient * power[r, c];
                    numerator[r, c] += term;
                    denominator[r, c] += negative ? -term : term;
                }
            }

            negative = !negative;
        }

        double[,] result = Linear.Solve(denominator, numerator);
        for (int i = 0; i < squarings; i++)
        {
            result = Linear.Multiply(result, result);
        }

        return result;
    }
}
