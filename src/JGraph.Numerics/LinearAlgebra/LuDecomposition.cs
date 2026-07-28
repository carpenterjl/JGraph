namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// LU factorization with partial pivoting: P·A = L·U for a square matrix A. The workhorse behind
/// <c>det</c>, <c>inv</c>, and the square case of the <c>\</c> solver.
/// </summary>
public sealed class LuDecomposition
{
    private readonly double[,] _lu;      // L below the diagonal (unit diagonal implied), U on and above
    private readonly int[] _pivot;       // row i of the factored matrix came from row _pivot[i] of A
    private readonly int _sign;          // +1/-1 with the number of row swaps, for the determinant
    private readonly int _n;

    private LuDecomposition(double[,] lu, int[] pivot, int sign)
    {
        _lu = lu;
        _pivot = pivot;
        _sign = sign;
        _n = pivot.Length;
    }

    /// <summary>Factors square <paramref name="matrix"/>; the input is not modified.</summary>
    /// <exception cref="ArgumentException">The matrix is not square.</exception>
    public static LuDecomposition Factor(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
        {
            throw new ArgumentException("LU factorization needs a square matrix.", nameof(matrix));
        }

        var lu = (double[,])matrix.Clone();
        var pivot = new int[n];
        for (int i = 0; i < n; i++)
        {
            pivot[i] = i;
        }

        int sign = 1;
        for (int k = 0; k < n; k++)
        {
            // Partial pivoting: bring the largest remaining entry of column k to the diagonal.
            int best = k;
            double bestAbs = Math.Abs(lu[k, k]);
            for (int r = k + 1; r < n; r++)
            {
                double candidate = Math.Abs(lu[r, k]);
                if (candidate > bestAbs)
                {
                    best = r;
                    bestAbs = candidate;
                }
            }

            if (best != k)
            {
                for (int c = 0; c < n; c++)
                {
                    (lu[k, c], lu[best, c]) = (lu[best, c], lu[k, c]);
                }

                (pivot[k], pivot[best]) = (pivot[best], pivot[k]);
                sign = -sign;
            }

            double diagonal = lu[k, k];
            if (diagonal == 0)
            {
                continue; // singular: the zero stays on U's diagonal and the determinant reports it
            }

            for (int r = k + 1; r < n; r++)
            {
                double factor = lu[r, k] / diagonal;
                lu[r, k] = factor;
                for (int c = k + 1; c < n; c++)
                {
                    lu[r, c] -= factor * lu[k, c];
                }
            }
        }

        return new LuDecomposition(lu, pivot, sign);
    }

    /// <summary>The matrix order n.</summary>
    public int Order => _n;

    /// <summary>The determinant of A: ±(product of U's diagonal).</summary>
    public double Determinant
    {
        get
        {
            double product = _sign;
            for (int i = 0; i < _n; i++)
            {
                product *= _lu[i, i];
            }

            return product;
        }
    }

    /// <summary>Whether a diagonal pivot vanished — A is singular to working precision.</summary>
    public bool IsSingular
    {
        get
        {
            for (int i = 0; i < _n; i++)
            {
                if (_lu[i, i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The unit-lower-triangular factor L.</summary>
    public double[,] Lower
    {
        get
        {
            var l = new double[_n, _n];
            for (int r = 0; r < _n; r++)
            {
                l[r, r] = 1;
                for (int c = 0; c < r; c++)
                {
                    l[r, c] = _lu[r, c];
                }
            }

            return l;
        }
    }

    /// <summary>The upper-triangular factor U.</summary>
    public double[,] Upper
    {
        get
        {
            var u = new double[_n, _n];
            for (int r = 0; r < _n; r++)
            {
                for (int c = r; c < _n; c++)
                {
                    u[r, c] = _lu[r, c];
                }
            }

            return u;
        }
    }

    /// <summary>The permutation matrix P with P·A = L·U.</summary>
    public double[,] Permutation
    {
        get
        {
            var p = new double[_n, _n];
            for (int r = 0; r < _n; r++)
            {
                p[r, _pivot[r]] = 1;
            }

            return p;
        }
    }

    /// <summary>Solves A·x = b.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    /// <exception cref="ArgumentException">b's length is not the matrix order.</exception>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n)
        {
            throw new ArgumentException("The right-hand side's length must match the matrix order.", nameof(b));
        }

        double[,] solved = SolveColumns(ToColumn(b));
        var x = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            x[i] = solved[i, 0];
        }

        return x;
    }

    /// <summary>Solves A·X = B column by column.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    /// <exception cref="ArgumentException">B's row count is not the matrix order.</exception>
    public double[,] SolveColumns(double[,] b)
    {
        if (b.GetLength(0) != _n)
        {
            throw new ArgumentException("The right-hand side's row count must match the matrix order.", nameof(b));
        }

        if (IsSingular)
        {
            throw new InvalidOperationException("The matrix is singular to working precision.");
        }

        int columns = b.GetLength(1);
        var x = new double[_n, columns];

        // Apply the row permutation, then forward-substitute L and back-substitute U.
        for (int r = 0; r < _n; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                x[r, c] = b[_pivot[r], c];
            }
        }

        for (int k = 0; k < _n; k++)
        {
            for (int r = k + 1; r < _n; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    x[r, c] -= _lu[r, k] * x[k, c];
                }
            }
        }

        for (int k = _n - 1; k >= 0; k--)
        {
            for (int c = 0; c < columns; c++)
            {
                x[k, c] /= _lu[k, k];
            }

            for (int r = 0; r < k; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    x[r, c] -= _lu[r, k] * x[k, c];
                }
            }
        }

        return x;
    }

    /// <summary>The inverse of A.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    public double[,] Inverse()
    {
        var identity = new double[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            identity[i, i] = 1;
        }

        return SolveColumns(identity);
    }

    private double[,] ToColumn(double[] values)
    {
        var column = new double[_n, 1];
        for (int i = 0; i < _n; i++)
        {
            column[i, 0] = values[i];
        }

        return column;
    }
}
