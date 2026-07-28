namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Householder QR factorization A = Q·R for an m-by-n matrix with m ≥ n, and the least-squares
/// solver built on it — the tall/overdetermined case of the <c>\</c> operator.
/// </summary>
public sealed class QrDecomposition
{
    private readonly double[,] _qr;      // Householder vectors below the diagonal, R on and above
    private readonly double[] _rDiag;    // R's diagonal, kept separately
    private readonly int _m;
    private readonly int _n;

    private QrDecomposition(double[,] qr, double[] rDiag)
    {
        _qr = qr;
        _rDiag = rDiag;
        _m = qr.GetLength(0);
        _n = qr.GetLength(1);
    }

    /// <summary>Factors <paramref name="matrix"/> (m ≥ n); the input is not modified.</summary>
    /// <exception cref="ArgumentException">The matrix has more columns than rows.</exception>
    public static QrDecomposition Factor(double[,] matrix)
    {
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);
        if (m < n)
        {
            throw new ArgumentException("QR factorization here needs at least as many rows as columns.", nameof(matrix));
        }

        var qr = (double[,])matrix.Clone();
        var rDiag = new double[n];

        for (int k = 0; k < n; k++)
        {
            // Householder reflection that zeroes column k below the diagonal.
            double norm = 0;
            for (int r = k; r < m; r++)
            {
                norm = Math.Sqrt((norm * norm) + (qr[r, k] * qr[r, k]));
            }

            if (norm == 0)
            {
                rDiag[k] = 0;
                continue; // the column is already zero: rank deficiency shows up on R's diagonal
            }

            if (qr[k, k] < 0)
            {
                norm = -norm;
            }

            for (int r = k; r < m; r++)
            {
                qr[r, k] /= norm;
            }

            qr[k, k] += 1;

            for (int c = k + 1; c < n; c++)
            {
                double s = 0;
                for (int r = k; r < m; r++)
                {
                    s += qr[r, k] * qr[r, c];
                }

                s = -s / qr[k, k];
                for (int r = k; r < m; r++)
                {
                    qr[r, c] += s * qr[r, k];
                }
            }

            rDiag[k] = -norm;
        }

        return new QrDecomposition(qr, rDiag);
    }

    /// <summary>Whether every diagonal of R is nonzero — A has full column rank.</summary>
    public bool IsFullRank
    {
        get
        {
            foreach (double d in _rDiag)
            {
                if (d == 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The economy-size orthogonal factor Q (m-by-n, orthonormal columns).</summary>
    public double[,] Q
    {
        get
        {
            var q = new double[_m, _n];
            for (int k = _n - 1; k >= 0; k--)
            {
                for (int r = 0; r < _m; r++)
                {
                    q[r, k] = 0;
                }

                q[k, k] = 1;
                for (int c = k; c < _n; c++)
                {
                    if (_qr[k, k] == 0)
                    {
                        continue;
                    }

                    double s = 0;
                    for (int r = k; r < _m; r++)
                    {
                        s += _qr[r, k] * q[r, c];
                    }

                    s = -s / _qr[k, k];
                    for (int r = k; r < _m; r++)
                    {
                        q[r, c] += s * _qr[r, k];
                    }
                }
            }

            return q;
        }
    }

    /// <summary>The upper-triangular factor R (n-by-n).</summary>
    public double[,] R
    {
        get
        {
            var r = new double[_n, _n];
            for (int i = 0; i < _n; i++)
            {
                r[i, i] = _rDiag[i];
                for (int j = i + 1; j < _n; j++)
                {
                    r[i, j] = _qr[i, j];
                }
            }

            return r;
        }
    }

    /// <summary>The least-squares solution of A·X ≈ B (minimizing ‖A·X − B‖ column by column).</summary>
    /// <exception cref="InvalidOperationException">A is rank deficient.</exception>
    /// <exception cref="ArgumentException">B's row count is not A's row count.</exception>
    public double[,] SolveColumns(double[,] b)
    {
        if (b.GetLength(0) != _m)
        {
            throw new ArgumentException("The right-hand side's row count must match the matrix's.", nameof(b));
        }

        if (!IsFullRank)
        {
            throw new InvalidOperationException("The matrix is rank deficient to working precision.");
        }

        int columns = b.GetLength(1);
        var y = (double[,])b.Clone();

        // Apply Qᵀ to B, then back-substitute R.
        for (int k = 0; k < _n; k++)
        {
            if (_qr[k, k] == 0)
            {
                continue;
            }

            for (int c = 0; c < columns; c++)
            {
                double s = 0;
                for (int r = k; r < _m; r++)
                {
                    s += _qr[r, k] * y[r, c];
                }

                s = -s / _qr[k, k];
                for (int r = k; r < _m; r++)
                {
                    y[r, c] += s * _qr[r, k];
                }
            }
        }

        var x = new double[_n, columns];
        for (int k = _n - 1; k >= 0; k--)
        {
            for (int c = 0; c < columns; c++)
            {
                double s = y[k, c];
                for (int j = k + 1; j < _n; j++)
                {
                    s -= _qr[k, j] * x[j, c];
                }

                x[k, c] = s / _rDiag[k];
            }
        }

        return x;
    }
}
