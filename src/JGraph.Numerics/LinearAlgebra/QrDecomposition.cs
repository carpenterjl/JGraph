namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Householder QR factorization A = Q·R of an m-by-n matrix of any shape, optionally with column
/// pivoting (A·P = Q·R), and the least-squares solver built on it — the tall/overdetermined case of
/// the <c>\</c> operator.
/// </summary>
/// <remarks>
/// The factorization runs over the first min(m, n) columns, which is what makes a wide matrix an
/// ordinary case rather than a refused one: there are only m reflectors to be had, and applying
/// them leaves R m-by-n upper trapezoidal. Before M76 a wide matrix was refused outright, and
/// because the refusal was an <see cref="ArgumentException"/> nothing caught, <c>qr</c> of one
/// ended the process.
/// </remarks>
public sealed class QrDecomposition
{
    private readonly double[,] _qr;      // Householder vectors below the diagonal, R on and above
    private readonly double[] _rDiag;    // R's diagonal, kept separately
    private readonly int[] _pivot;       // factored column j was column _pivot[j] of the input
    private readonly int _m;
    private readonly int _n;
    private readonly int _p;             // min(m, n) — the number of reflectors

    private QrDecomposition(double[,] qr, double[] rDiag, int[] pivot)
    {
        _qr = qr;
        _rDiag = rDiag;
        _pivot = pivot;
        _m = qr.GetLength(0);
        _n = qr.GetLength(1);
        _p = Math.Min(_m, _n);
    }

    /// <summary>Factors <paramref name="matrix"/> without pivoting; the input is not modified.</summary>
    public static QrDecomposition Factor(double[,] matrix) => Factor(matrix, pivot: false);

    /// <summary>
    /// Factors <paramref name="matrix"/>, optionally choosing at each step the remaining column of
    /// largest norm — which orders R's diagonal by decreasing magnitude and is what makes the
    /// factorization tell the truth about a rank-deficient matrix.
    /// </summary>
    public static QrDecomposition Factor(double[,] matrix, bool pivot)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);
        int p = Math.Min(m, n);

        var qr = (double[,])matrix.Clone();
        var rDiag = new double[p];
        var order = new int[n];
        for (int j = 0; j < n; j++)
        {
            order[j] = j;
        }

        for (int k = 0; k < p; k++)
        {
            if (pivot)
            {
                SwapInLargestColumn(qr, order, k, m, n);
            }

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

        return new QrDecomposition(qr, rDiag, order);
    }

    /// <summary>
    /// Moves the largest remaining column into position <paramref name="k"/>, measuring each by the
    /// part of it the reflections still have to reach — rows k downward. Recomputed rather than
    /// updated downdate-style: n is small here, and a downdated norm loses the accuracy that is the
    /// entire reason for pivoting.
    /// </summary>
    private static void SwapInLargestColumn(double[,] qr, int[] order, int k, int m, int n)
    {
        int best = k;
        double largest = -1;
        for (int c = k; c < n; c++)
        {
            double sum = 0;
            for (int r = k; r < m; r++)
            {
                sum += qr[r, c] * qr[r, c];
            }

            if (sum > largest)
            {
                largest = sum;
                best = c;
            }
        }

        if (best == k)
        {
            return;
        }

        for (int r = 0; r < m; r++)
        {
            (qr[r, k], qr[r, best]) = (qr[r, best], qr[r, k]);
        }

        (order[k], order[best]) = (order[best], order[k]);
    }

    /// <summary>
    /// The column order the factorization used: factored column j was column <c>PivotVector[j]</c>
    /// of the input, 0-based. The identity when the factorization did not pivot.
    /// </summary>
    public int[] PivotVector => (int[])_pivot.Clone();

    /// <summary>The pivoting as a permutation matrix P, so that A·P = Q·R.</summary>
    public double[,] Permutation
    {
        get
        {
            var p = new double[_n, _n];
            for (int j = 0; j < _n; j++)
            {
                p[_pivot[j], j] = 1;
            }

            return p;
        }
    }

    /// <summary>Whether every diagonal of R is nonzero — A has full column rank.</summary>
    public bool IsFullRank
    {
        get
        {
            if (_n > _m)
            {
                return false; // more columns than rows: they cannot all be independent
            }

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

    /// <summary>
    /// The economy-size orthogonal factor Q: m-by-min(m, n), with orthonormal columns. For a tall
    /// matrix that is m-by-n as before; for a wide one it is the full m-by-m, because a wide matrix
    /// has no columns to economize away — which is also what MATLAB's <c>qr(A, 0)</c> answers.
    /// </summary>
    public double[,] Q
    {
        get
        {
            var q = new double[_m, _p];
            for (int k = _p - 1; k >= 0; k--)
            {
                for (int r = 0; r < _m; r++)
                {
                    q[r, k] = 0;
                }

                q[k, k] = 1;
                for (int c = k; c < _p; c++)
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

    /// <summary>
    /// The economy-size upper-triangular factor R: min(m, n)-by-n, the partner of <see cref="Q"/>.
    /// n-by-n for a tall matrix as before, and m-by-n upper trapezoidal for a wide one.
    /// </summary>
    public double[,] R
    {
        get
        {
            var r = new double[_p, _n];
            for (int i = 0; i < _p; i++)
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

    /// <summary>
    /// The full m-by-m orthogonal factor, as against <see cref="Q"/>'s economy-size m-by-n. The
    /// extra columns span the orthogonal complement of A's range, which is what a rank-one update
    /// needs in order to stay a factorization of the whole space.
    /// </summary>
    public double[,] FullQ
    {
        get
        {
            var q = new double[_m, _m];
            for (int i = 0; i < _m; i++)
            {
                q[i, i] = 1;
            }

            // The reflectors are applied in reverse, which is what turns the stored vectors back
            // into the product they represent.
            for (int k = Math.Min(_n, _m) - 1; k >= 0; k--)
            {
                if (_qr[k, k] == 0)
                {
                    continue;
                }

                for (int c = 0; c < _m; c++)
                {
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

    /// <summary>The full m-by-n upper-triangular factor, the partner of <see cref="FullQ"/>.</summary>
    public double[,] FullR
    {
        get
        {
            var r = new double[_m, _n];
            for (int i = 0; i < Math.Min(_m, _n); i++)
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

        if (_n > _m)
        {
            // An underdetermined system has no least-squares solution of its own; the minimum-norm
            // one comes from the QR of the transpose, which is what Linear.Solve does with it.
            throw new InvalidOperationException(
                "A least-squares solve needs at least as many rows as columns.");
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
