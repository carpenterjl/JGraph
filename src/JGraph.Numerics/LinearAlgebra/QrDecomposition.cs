namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Householder QR factorization A = Q·R of an m-by-n matrix of any shape, optionally with column
/// pivoting (A·P = Q·R), and the least-squares solver built on it — the tall/overdetermined case of
/// the <c>\</c> operator.
/// </summary>
/// <remarks>
/// <para>
/// The factorization runs over the first min(m, n) columns, which is what makes a wide matrix an
/// ordinary case rather than a refused one: there are only m reflectors to be had, and applying
/// them leaves R m-by-n upper trapezoidal. Before M76 a wide matrix was refused outright, and
/// because the refusal was an <see cref="ArgumentException"/> nothing caught, <c>qr</c> of one
/// ended the process.
/// </para>
/// <para>
/// The factors are held flat and column-major, in LAPACK's own storage — R on and above the
/// diagonal, the reflector vectors below it with their leading 1 implied, and the scalars that
/// finish them alongside. Q is never formed unless something asks for it, and a least-squares
/// solve does not: multiplying by Qᵀ is a walk over the reflectors.
/// </para>
/// </remarks>
public sealed class QrDecomposition
{
    private readonly double[] _qr;       // column-major: R on and above the diagonal, reflectors below
    private readonly double[] _tau;      // the scalar that finishes each reflector
    private readonly int[] _pivot;       // factored column j was column _pivot[j] of the input
    private readonly int _m;
    private readonly int _n;
    private readonly int _p;             // min(m, n) — the number of reflectors

    private QrDecomposition(double[] qr, double[] tau, int[] pivot, int m, int n)
    {
        _qr = qr;
        _tau = tau;
        _pivot = pivot;
        _m = m;
        _n = n;
        _p = Math.Min(m, n);
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
        var flat = new double[(long)m * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                flat[(c * m) + r] = matrix[r, c];
            }
        }

        return FactorAdopting(flat, m, n, pivot);
    }

    /// <summary>Factors an m-by-n column-major matrix; <paramref name="columnMajor"/> is copied.</summary>
    public static QrDecomposition Factor(ReadOnlySpan<double> columnMajor, int m, int n, bool pivot)
    {
        var flat = new double[(long)m * n];
        columnMajor[..(m * n)].CopyTo(flat);
        return FactorAdopting(flat, m, n, pivot);
    }

    /// <summary>
    /// Factors an m-by-n column-major matrix <em>in</em> <paramref name="columnMajor"/>, which the
    /// decomposition takes ownership of. The caller must not read it again.
    /// </summary>
    public static QrDecomposition FactorAdopting(double[] columnMajor, int m, int n, bool pivot)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        int p = Math.Min(m, n);
        var tau = new double[Math.Max(p, 1)];
        var order = new int[Math.Max(n, 1)];

        if (pivot)
        {
            LinalgProvider.Current.Geqp3(m, n, columnMajor, Math.Max(m, 1), order, tau);
            for (int j = 0; j < n; j++)
            {
                order[j]--; // LAPACK's record is 1-based; every caller here counts from zero
            }
        }
        else
        {
            LinalgProvider.Current.Geqrf(m, n, columnMajor, Math.Max(m, 1), tau);
            for (int j = 0; j < n; j++)
            {
                order[j] = j;
            }
        }

        return new QrDecomposition(columnMajor, tau, order, m, n);
    }

    /// <summary>
    /// The column order the factorization used: factored column j was column <c>PivotVector[j]</c>
    /// of the input, 0-based. The identity when the factorization did not pivot.
    /// </summary>
    public int[] PivotVector
    {
        get
        {
            var copy = new int[_n];
            Array.Copy(_pivot, copy, _n);
            return copy;
        }
    }

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

    /// <summary>The permutation matrix P as a flat n-by-n column-major array.</summary>
    public double[] PermutationColumnMajor()
    {
        var p = new double[(long)_n * _n];
        for (int j = 0; j < _n; j++)
        {
            p[(j * _n) + _pivot[j]] = 1;
        }

        return p;
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

            for (int k = 0; k < _p; k++)
            {
                if (_qr[(k * _m) + k] == 0)
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
    public double[,] Q => Rect(QColumnMajor(full: false), _m, _p);

    /// <summary>
    /// The economy-size upper-triangular factor R: min(m, n)-by-n, the partner of <see cref="Q"/>.
    /// n-by-n for a tall matrix as before, and m-by-n upper trapezoidal for a wide one.
    /// </summary>
    public double[,] R => Rect(RColumnMajor(full: false), _p, _n);

    /// <summary>
    /// The full m-by-m orthogonal factor, as against <see cref="Q"/>'s economy-size m-by-n. The
    /// extra columns span the orthogonal complement of A's range, which is what a rank-one update
    /// needs in order to stay a factorization of the whole space.
    /// </summary>
    public double[,] FullQ => Rect(QColumnMajor(full: true), _m, _m);

    /// <summary>The full m-by-n upper-triangular factor, the partner of <see cref="FullQ"/>.</summary>
    public double[,] FullR => Rect(RColumnMajor(full: true), _m, _n);

    /// <summary>
    /// The orthogonal factor as a flat column-major array: m-by-m when <paramref name="full"/>,
    /// m-by-min(m, n) otherwise. Expanding the reflectors is what costs here, so a caller that only
    /// wants to multiply by Q should not ask for it.
    /// </summary>
    public double[] QColumnMajor(bool full)
    {
        int columns = full ? _m : _p;
        var q = new double[(long)_m * columns];
        for (int c = 0; c < Math.Min(_p, columns); c++)
        {
            Array.Copy(_qr, (long)c * _m, q, (long)c * _m, _m);
        }

        LinalgProvider.Current.Orgqr(_m, columns, _p, q, Math.Max(_m, 1), _tau);
        return q;
    }

    /// <summary>
    /// The upper-triangular factor as a flat column-major array: m-by-n when <paramref name="full"/>,
    /// min(m, n)-by-n otherwise.
    /// </summary>
    public double[] RColumnMajor(bool full)
    {
        int rows = full ? _m : _p;
        var r = new double[(long)rows * _n];
        for (int c = 0; c < _n; c++)
        {
            for (int i = 0; i <= Math.Min(c, rows - 1); i++)
            {
                r[(c * rows) + i] = _qr[(c * _m) + i];
            }
        }

        return r;
    }

    /// <summary>
    /// Overwrites the m-by-nrhs column-major <paramref name="b"/> with Qᵀ·B, walking the reflectors
    /// rather than forming Q — which is the whole of <c>[C, R] = qr(A, B)</c>, and the reason that
    /// form exists at all.
    /// </summary>
    public void ApplyTransposeInPlace(double[] b, int nrhs)
    {
        ArgumentNullException.ThrowIfNull(b);
        LinalgProvider.Current.Ormqr(leftSide: true, transpose: true, _m, nrhs, _p,
            _qr, Math.Max(_m, 1), _tau, b, Math.Max(_m, 1));
    }

    /// <summary>The least-squares solution of A·X ≈ B (minimizing ‖A·X − B‖ column by column).</summary>
    /// <exception cref="InvalidOperationException">A is rank deficient.</exception>
    /// <exception cref="ArgumentException">B's row count is not A's row count.</exception>
    public double[,] SolveColumns(double[,] b)
    {
        ArgumentNullException.ThrowIfNull(b);
        if (b.GetLength(0) != _m)
        {
            throw new ArgumentException("The right-hand side's row count must match the matrix's.", nameof(b));
        }

        int columns = b.GetLength(1);
        var flat = new double[(long)_m * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < _m; r++)
            {
                flat[(c * _m) + r] = b[r, c];
            }
        }

        double[] solution = SolveColumnMajor(flat, columns);
        var x = new double[_n, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < _n; r++)
            {
                x[r, c] = solution[(c * _n) + r];
            }
        }

        return x;
    }

    /// <summary>
    /// The least-squares solution as a flat n-by-nrhs column-major array, from an m-by-nrhs
    /// column-major right-hand side. Q is applied through the reflectors rather than formed.
    /// </summary>
    /// <exception cref="InvalidOperationException">A is rank deficient.</exception>
    public double[] SolveColumnMajor(double[] b, int nrhs)
    {
        ArgumentNullException.ThrowIfNull(b);
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

        DenseLinalg backend = LinalgProvider.Current;
        backend.Ormqr(leftSide: true, transpose: true, _m, nrhs, _p, _qr, Math.Max(_m, 1), _tau, b, Math.Max(_m, 1));
        backend.Trtrs(lower: false, transpose: false, _n, nrhs, _qr, Math.Max(_m, 1), b, Math.Max(_m, 1));

        // The solution occupies the first n rows of an m-row buffer; compacting it is what makes
        // the answer n-by-nrhs rather than an m-by-nrhs array with residuals hanging off the end.
        if (_n == _m)
        {
            return b;
        }

        var solution = new double[(long)_n * nrhs];
        for (int c = 0; c < nrhs; c++)
        {
            Array.Copy(b, (long)c * _m, solution, (long)c * _n, _n);
        }

        return solution;
    }

    private static double[,] Rect(double[] columnMajor, int rows, int cols)
    {
        var rect = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                rect[r, c] = columnMajor[(c * rows) + r];
            }
        }

        return rect;
    }
}
