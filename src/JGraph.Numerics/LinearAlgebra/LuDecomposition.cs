namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// LU factorization with partial pivoting: P·A = L·U for a square matrix A. The workhorse behind
/// <c>det</c>, <c>inv</c>, and the square case of the <c>\</c> solver.
/// </summary>
/// <remarks>
/// The factors live in one flat column-major array — LAPACK's own layout, and the script's — so the
/// factorization, every solve against it, and the inverse are single calls into
/// <see cref="LinalgProvider.Current"/> with nothing to transpose in between. The row-major
/// <c>double[,]</c> shapes the older callers hand in and read back are converted at the edges.
/// </remarks>
public sealed class LuDecomposition
{
    private readonly double[] _lu;       // column-major: L below the diagonal (unit diagonal implied), U on and above
    private readonly int[] _ipiv;        // LAPACK's interchange record: at step i, row i was swapped with row _ipiv[i] (1-based)
    private readonly int _n;
    private int[]? _order;               // the interchanges as a permutation, built when something asks for one

    private LuDecomposition(double[] lu, int[] ipiv, int n)
    {
        _lu = lu;
        _ipiv = ipiv;
        _n = n;
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

        var lu = new double[(long)n * n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                lu[((long)c * n) + r] = matrix[r, c];
            }
        }

        return FactorInPlace(lu, n);
    }

    /// <summary>
    /// Factors an n-by-n matrix already laid out column-major — the layout packed script storage
    /// uses, so this is the entry point that costs one copy and no transpose.
    /// </summary>
    /// <exception cref="ArgumentException">The span is shorter than n².</exception>
    public static LuDecomposition Factor(ReadOnlySpan<double> columnMajor, int n)
    {
        if (columnMajor.Length < (long)n * n)
        {
            throw new ArgumentException("The span must hold n² elements.", nameof(columnMajor));
        }

        double[] lu = GC.AllocateUninitializedArray<double>(n * n);
        columnMajor[..(n * n)].CopyTo(lu);
        return FactorInPlace(lu, n);
    }

    /// <summary>
    /// Factors an n-by-n column-major array <em>in place</em>, taking ownership of it: the caller
    /// must not read <paramref name="columnMajor"/> afterwards, because it now holds the factors.
    /// This is the form a caller that just built a private copy wants — at n = 2000 the copy this
    /// saves is 32 MB.
    /// </summary>
    /// <exception cref="ArgumentException">The array is shorter than n².</exception>
    public static LuDecomposition FactorAdopting(double[] columnMajor, int n)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        if (columnMajor.LongLength < (long)n * n)
        {
            throw new ArgumentException("The array must hold n² elements.", nameof(columnMajor));
        }

        return FactorInPlace(columnMajor, n);
    }

    private static LuDecomposition FactorInPlace(double[] lu, int n)
    {
        var ipiv = new int[n];
        LinalgProvider.Current.Getrf(n, n, lu, n, ipiv);
        return new LuDecomposition(lu, ipiv, n);
    }

    /// <summary>The matrix order n.</summary>
    public int Order => _n;

    /// <summary>The determinant of A: ±(product of U's diagonal).</summary>
    public double Determinant
    {
        get
        {
            double product = 1;
            for (int i = 0; i < _n; i++)
            {
                if (_ipiv[i] != i + 1)
                {
                    product = -product;
                }

                product *= _lu[((long)i * _n) + i];
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
                if (_lu[((long)i * _n) + i] == 0)
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
                    l[r, c] = _lu[((long)c * _n) + r];
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
                    u[r, c] = _lu[((long)c * _n) + r];
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
            int[] order = RowOrder;
            var p = new double[_n, _n];
            for (int r = 0; r < _n; r++)
            {
                p[r, order[r]] = 1;
            }

            return p;
        }
    }

    /// <summary>Row i of the factored matrix came from row <c>RowPermutation[i]</c> of A.</summary>
    public ReadOnlySpan<int> RowPermutation => RowOrder;

    /// <summary>
    /// The factors as they are stored: one column-major n-by-n array holding L strictly below the
    /// diagonal with its unit diagonal implied, and U on and above it.
    /// </summary>
    public ReadOnlySpan<double> Factors => _lu;

    private int[] RowOrder => _order ??= DenseLinalg.PermutationOf(_ipiv, _n);

    /// <summary>Solves A·x = b.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    /// <exception cref="ArgumentException">b's length is not the matrix order.</exception>
    public double[] Solve(double[] b)
    {
        if (b.Length != _n)
        {
            throw new ArgumentException("The right-hand side's length must match the matrix order.", nameof(b));
        }

        var x = new double[_n];
        b.CopyTo(x, 0);
        SolveInPlace(x, nrhs: 1, ldb: _n);
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

        int columns = b.GetLength(1);
        var flat = new double[(long)_n * columns];
        for (int r = 0; r < _n; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                flat[((long)c * _n) + r] = b[r, c];
            }
        }

        SolveInPlace(flat, columns, _n);

        var x = new double[_n, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < _n; r++)
            {
                x[r, c] = flat[((long)c * _n) + r];
            }
        }

        return x;
    }

    /// <summary>
    /// Solves A·X = B in place over a column-major right-hand side — the zero-copy form the packed
    /// script path uses, with the same singularity refusal the boxed one gets.
    /// </summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    public void SolveInPlace(Span<double> b, int nrhs, int ldb)
    {
        if (IsSingular)
        {
            throw new InvalidOperationException("The matrix is singular to working precision.");
        }

        LinalgProvider.Current.Getrs(transpose: false, _n, nrhs, _lu, _n, _ipiv, b, ldb);
    }

    /// <summary>The inverse of A.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    public double[,] Inverse()
    {
        double[] flat = InverseColumnMajor();
        var inverse = new double[_n, _n];
        for (int c = 0; c < _n; c++)
        {
            for (int r = 0; r < _n; r++)
            {
                inverse[r, c] = flat[((long)c * _n) + r];
            }
        }

        return inverse;
    }

    /// <summary>The inverse of A as a fresh column-major array.</summary>
    /// <exception cref="InvalidOperationException">A is singular.</exception>
    public double[] InverseColumnMajor()
    {
        if (IsSingular)
        {
            throw new InvalidOperationException("The matrix is singular to working precision.");
        }

        double[] inverse = GC.AllocateUninitializedArray<double>(_n * _n);
        _lu.CopyTo(inverse, 0);
        LinalgProvider.Current.Getri(_n, inverse, _n, _ipiv);
        return inverse;
    }

    /// <summary>
    /// The reciprocal condition number in the 1-norm, given <paramref name="anorm"/> = ‖A‖₁. Zero
    /// for a singular factorization. The native backend estimates this the way LAPACK — and so
    /// MATLAB — does; the managed one computes the exact reciprocal (ADR 0089).
    /// </summary>
    public double ReciprocalCondition(double anorm) =>
        IsSingular ? 0 : LinalgProvider.Current.Gecon(_n, _lu, _n, anorm);
}
