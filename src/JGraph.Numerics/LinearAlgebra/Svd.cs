namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Singular value decomposition A = U·S·Vᵀ. Backs <c>svd</c>, <c>rank</c>, <c>null</c>, <c>orth</c>,
/// <c>pinv</c>, <c>cond</c> and the matrix 2-norm.
/// </summary>
/// <remarks>
/// The factors are held flat and column-major. Note that the backend hands back the second factor
/// <em>transposed</em> — LAPACK's Vᵀ — and this class turns it the right way round once, at
/// construction, so that <see cref="V"/> means what its name says everywhere it is read.
/// </remarks>
public sealed class Svd
{
    private readonly double[] _u;
    private readonly double[] _v;
    private readonly int _rows;
    private readonly int _cols;
    private readonly int _uColumns;
    private readonly int _vColumns;
    private double[,]? _uRect;
    private double[,]? _vRect;

    private Svd(double[] values, double[] u, double[] v, int rows, int cols, int uColumns, int vColumns)
    {
        Values = values;
        _u = u;
        _v = v;
        _rows = rows;
        _cols = cols;
        _uColumns = uColumns;
        _vColumns = vColumns;
    }

    /// <summary>The singular values, in descending order.</summary>
    public double[] Values { get; }

    /// <summary>The left singular vectors (economy size: m-by-min(m,n), orthonormal columns).</summary>
    /// <remarks>
    /// Materialized once and kept. The factors live flat and column-major, so this shape has to be
    /// built rather than merely handed over — and callers index it inside loops, where rebuilding
    /// it per read would turn an O(n³) pinv into an O(n⁵) one.
    /// </remarks>
    public double[,] U => _uRect ??= Rect(_u, _rows, _uColumns);

    /// <summary>The right singular vectors (n-by-min(m,n), orthonormal columns).</summary>
    public double[,] V => _vRect ??= Rect(_v, _cols, _vColumns);

    /// <summary>The left factor as a flat column-major array, m-by-<see cref="UColumnCount"/>.</summary>
    public double[] UColumnMajor => _u;

    /// <summary>The right factor as a flat column-major array, n-by-<see cref="VColumnCount"/>.</summary>
    public double[] VColumnMajor => _v;

    /// <summary>How many columns <see cref="U"/> has: min(m,n) economy size, or m for the full factor.</summary>
    public int UColumnCount => _uColumns;

    /// <summary>How many columns <see cref="V"/> has: min(m,n) economy size, or n for the full factor.</summary>
    public int VColumnCount => _vColumns;

    /// <summary>The numeric rank: singular values above max(m,n)·eps·σ₁, MATLAB's default tolerance.</summary>
    public int Rank(int rows, int cols) => RankOf(Values, rows, cols);

    /// <summary>
    /// The same rank from singular values alone — which is all <c>rank</c> and <c>linsolve</c>'s
    /// second output ever needed, and neither has to pay for a singular vector to get it.
    /// </summary>
    public static int RankOf(ReadOnlySpan<double> values, int rows, int cols)
    {
        double tolerance = Math.Max(rows, cols) * 2.220446049250313e-16 * (values.Length > 0 ? values[0] : 0);
        int rank = 0;
        foreach (double s in values)
        {
            if (s > tolerance)
            {
                rank++;
            }
        }

        return rank;
    }

    /// <summary>Factors <paramref name="matrix"/> to economy size; the input is not modified.</summary>
    public static Svd Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);
        return Factor(ColumnMajorOf(matrix, m, n), m, n);
    }

    /// <summary>Factors an m-by-n column-major matrix to economy size; the input is not modified.</summary>
    public static Svd Factor(ReadOnlySpan<double> columnMajor, int m, int n) =>
        Decompose(columnMajor, m, n, SvdVectors.Economy);

    /// <summary>
    /// Factors to MATLAB's full shapes: U is m-by-m and V is n-by-n, so that <c>U·S·Vᵀ</c>
    /// reassembles A with an m-by-n S rather than a square one.
    /// </summary>
    public static Svd FactorFull(ReadOnlySpan<double> columnMajor, int m, int n) =>
        Decompose(columnMajor, m, n, SvdVectors.All);

    /// <summary>
    /// The singular values alone, without either factor — which is most of the work saved for
    /// <c>rank</c>, <c>cond</c> and the matrix 2-norm, none of which look at a singular vector.
    /// </summary>
    public static double[] SingularValues(ReadOnlySpan<double> columnMajor, int m, int n) =>
        Decompose(columnMajor, m, n, SvdVectors.None).Values;

    private static Svd Decompose(ReadOnlySpan<double> a, int m, int n, SvdVectors job)
    {
        int k = Math.Min(m, n);
        int uColumns = job switch
        {
            SvdVectors.None => 0,
            SvdVectors.All => m,
            _ => k,
        };
        int vColumns = job switch
        {
            SvdVectors.None => 0,
            SvdVectors.All => n,
            _ => k,
        };

        var values = new double[k];
        var u = new double[(long)m * uColumns];
        var vt = new double[(long)vColumns * n];
        var work = new double[(long)m * n];
        int lda = Math.Max(m, 1);
        int ldvt = Math.Max(vColumns, 1);

        DenseLinalg backend = LinalgProvider.Current;
        a[..(m * n)].CopyTo(work);
        int info = backend.Gesdd(job, m, n, work, lda, values, u, lda, vt, ldvt);

        if (info != 0)
        {
            // The divide-and-conquer driver failed to converge. It destroyed its copy on the way,
            // which is exactly why this class holds the caller's matrix at arm's length: the QR
            // iteration gets a pristine one and the caller never learns any of it happened.
            a[..(m * n)].CopyTo(work);
            Array.Clear(values);
            Array.Clear(u);
            Array.Clear(vt);
            info = backend.Gesvd(job, m, n, work, lda, values, u, lda, vt, ldvt);
            if (info != 0)
            {
                throw new InvalidOperationException(
                    "The singular value decomposition did not converge.");
            }
        }

        // Vᵀ arrives with V's columns as its rows; turning it here is what lets every reader
        // downstream take the name at face value.
        var v = new double[(long)n * vColumns];
        for (int c = 0; c < vColumns; c++)
        {
            for (int r = 0; r < n; r++)
            {
                v[(c * n) + r] = vt[(r * ldvt) + c];
            }
        }

        return new Svd(values, u, v, m, n, uColumns, vColumns);
    }

    private static double[] ColumnMajorOf(double[,] matrix, int m, int n)
    {
        var flat = new double[(long)m * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                flat[(c * m) + r] = matrix[r, c];
            }
        }

        return flat;
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
