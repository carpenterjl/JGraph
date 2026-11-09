namespace JGraph.Numerics.LinearAlgebra;

/// <summary>What a stiff ODE solver needs of a factored iteration matrix: solves against it.</summary>
public interface ILinearSolve
{
    /// <summary>Solves A·x = b into a new array.</summary>
    double[] Solve(double[] b);
}

/// <summary>
/// LU factorization with partial pivoting of a matrix whose nonzeros lie in a narrow band, in
/// LAPACK's band layout (<c>dgbtf2</c>/<c>dgbtrs</c>, unblocked): kl subdiagonals, ku
/// superdiagonals, and kl more above them for the fill the row interchanges make. The every-row
/// plan's I1 (ADR 0324): a stiff solver's iteration matrix I - h·γ·J over a banded Jacobian (a
/// <c>JPattern</c>, or a differenced Jacobian whose columns only reach their neighbours) costs
/// n·kl·(kl+ku) here where the dense factorization costs n³/3; R2025b factors the patterned case
/// sparse.
/// </summary>
public sealed class BandedLu : ILinearSolve
{
    private readonly double[] _ab;   // column-major, ldab = 2·kl + ku + 1; element (r, c) at ab[kv + r - c + c·ldab]
    private readonly int[] _ipiv;    // 0-based: at step j, row j was swapped with row _ipiv[j]
    private readonly int _n;
    private readonly int _kl;
    private readonly int _kv;
    private readonly int _ldab;

    private BandedLu(double[] ab, int[] ipiv, int n, int kl, int ku)
    {
        _ab = ab;
        _ipiv = ipiv;
        _n = n;
        _kl = kl;
        _kv = kl + ku;
        _ldab = (2 * kl) + ku + 1;
    }

    /// <summary>The matrix order.</summary>
    public int Order => _n;

    /// <summary>The subdiagonals the matrix had.</summary>
    public int Lower => _kl;

    /// <summary>The superdiagonals the matrix had.</summary>
    public int Upper => _kv - _kl;

    /// <summary>
    /// The band of <paramref name="matrix"/>'s nonzeros: the most subdiagonals and superdiagonals
    /// any column reaches. A zero stored inside the band is part of it; one outside never is.
    /// </summary>
    public static (int Lower, int Upper) Bandwidths(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        int kl = 0;
        int ku = 0;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < r - kl; c++)
            {
                if (matrix[r, c] != 0)
                {
                    kl = r - c;
                    break;
                }
            }

            for (int c = n - 1; c > r + ku; c--)
            {
                if (matrix[r, c] != 0)
                {
                    ku = c - r;
                    break;
                }
            }
        }

        return (kl, ku);
    }

    /// <summary>
    /// Factors <paramref name="matrix"/> in band form when its band is narrow enough to pay
    /// (2·kl + ku + 1 at most an eighth of n, n at least 32), or answers null: the caller factors
    /// it dense. Null too when a pivot is exactly zero, so the dense factorization reports the
    /// singular matrix in its own words.
    /// </summary>
    public static BandedLu? TryFactor(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (n < 32 || matrix.GetLength(1) != n)
        {
            return null;
        }

        (int kl, int ku) = Bandwidths(matrix);
        int ldab = (2 * kl) + ku + 1;
        if (ldab * 8 > n)
        {
            return null;
        }

        int kv = kl + ku;
        var ab = new double[(long)ldab * n];
        for (int c = 0; c < n; c++)
        {
            int top = Math.Max(0, c - ku);
            int bottom = Math.Min(n - 1, c + kl);
            int column = c * ldab;
            for (int r = top; r <= bottom; r++)
            {
                ab[column + kv + r - c] = matrix[r, c];
            }
        }

        var ipiv = new int[n];
        int ju = 0;
        for (int j = 0; j < n; j++)
        {
            int km = Math.Min(kl, n - 1 - j);
            int diagonal = (j * ldab) + kv;

            // The pivot: the largest magnitude on and below the diagonal, the first of equals.
            int jp = 0;
            double largest = Math.Abs(ab[diagonal]);
            for (int p = 1; p <= km; p++)
            {
                double magnitude = Math.Abs(ab[diagonal + p]);
                if (magnitude > largest)
                {
                    largest = magnitude;
                    jp = p;
                }
            }

            ipiv[j] = j + jp;
            if (ab[diagonal + jp] == 0)
            {
                return null;
            }

            ju = Math.Max(ju, Math.Min(j + ku + jp, n - 1));
            if (jp != 0)
            {
                for (int c = j; c <= ju; c++)
                {
                    int at = c * ldab;
                    int upper = at + kv + j - c;
                    int lower = upper + jp;
                    (ab[upper], ab[lower]) = (ab[lower], ab[upper]);
                }
            }

            if (km > 0)
            {
                double pivot = ab[diagonal];
                for (int p = 1; p <= km; p++)
                {
                    ab[diagonal + p] /= pivot;
                }

                for (int c = j + 1; c <= ju; c++)
                {
                    int at = c * ldab;
                    double u = ab[at + kv + j - c];
                    if (u != 0)
                    {
                        for (int p = 1; p <= km; p++)
                        {
                            ab[at + kv + j + p - c] -= ab[diagonal + p] * u;
                        }
                    }
                }
            }
        }

        return new BandedLu(ab, ipiv, n, kl, ku);
    }

    /// <inheritdoc />
    public double[] Solve(double[] b)
    {
        if (b.Length != _n)
        {
            throw new ArgumentException("The right-hand side's length must match the matrix order.", nameof(b));
        }

        var x = (double[])b.Clone();
        double[] ab = _ab;
        int ldab = _ldab;
        int kv = _kv;

        // L: the interchanges and the multipliers, column by column.
        for (int j = 0; j < _n - 1; j++)
        {
            int l = _ipiv[j];
            if (l != j)
            {
                (x[j], x[l]) = (x[l], x[j]);
            }

            double xj = x[j];
            if (xj != 0)
            {
                int lm = Math.Min(_kl, _n - 1 - j);
                int diagonal = (j * ldab) + kv;
                for (int p = 1; p <= lm; p++)
                {
                    x[j + p] -= ab[diagonal + p] * xj;
                }
            }
        }

        // U, whose band is kl + ku wide after the interchanges.
        for (int j = _n - 1; j >= 0; j--)
        {
            int at = j * ldab;
            double xj = x[j] / ab[at + kv];
            x[j] = xj;
            if (xj != 0)
            {
                for (int i = Math.Max(0, j - kv); i < j; i++)
                {
                    x[i] -= ab[at + kv + i - j] * xj;
                }
            }
        }

        return x;
    }

    /// <summary>
    /// A stiff solver's iteration matrix factored: in band form when <see cref="TryFactor"/> takes
    /// it, else dense through <see cref="LuDecomposition"/> (into <paramref name="reuse"/> when one
    /// is given).
    /// </summary>
    public static ILinearSolve FactorIteration(double[,] matrix, double[]? reuse = null) =>
        TryFactor(matrix) is { } banded
            ? banded
            : reuse is null ? LuDecomposition.Factor(matrix) : LuDecomposition.FactorReusing(matrix, reuse);
}
