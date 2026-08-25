using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// Eigenvalues and eigenvectors of a real square matrix. A symmetric matrix takes the symmetric
/// path — real, ascending eigenvalues and orthonormal vectors, MATLAB's symmetric order — and a
/// general one the nonsymmetric path, whose eigenvalues come in conjugate pairs and whose vectors
/// have unit 2-norm.
/// </summary>
/// <remarks>
/// Symmetry is decided here rather than by the backend, because it is a policy about what the
/// caller meant and not a fact about arithmetic: a matrix that is symmetric only to rounding is
/// still meant symmetrically, and sending it down the general path would answer with a spectrum
/// carrying imaginary dust.
/// </remarks>
public sealed class Eigen
{
    private Eigen(Complex[] values, Complex[,] vectors)
    {
        Values = values;
        Vectors = vectors;
    }

    /// <summary>The eigenvalues (real ones carry a zero imaginary part).</summary>
    public Complex[] Values { get; }

    /// <summary>The eigenvectors, one column per eigenvalue, each with unit 2-norm.</summary>
    public Complex[,] Vectors { get; }

    /// <summary>Whether every eigenvalue (and so every vector) is real.</summary>
    public bool IsReal
    {
        get
        {
            foreach (Complex value in Values)
            {
                if (value.Imaginary != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Factors square <paramref name="matrix"/>; the input is not modified.</summary>
    /// <exception cref="ArgumentException">The matrix is not square.</exception>
    public static Eigen Factor(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
        {
            throw new ArgumentException("Eigen decomposition needs a square matrix.", nameof(matrix));
        }

        var flat = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                flat[(c * n) + r] = matrix[r, c];
            }
        }

        return FactorAdopting(flat, n);
    }

    /// <summary>Factors an n-by-n column-major matrix; the input is not modified.</summary>
    public static Eigen Factor(ReadOnlySpan<double> columnMajor, int n)
    {
        var flat = new double[(long)n * n];
        columnMajor[..(n * n)].CopyTo(flat);
        return FactorAdopting(flat, n);
    }

    /// <summary>
    /// Factors an n-by-n column-major matrix, overwriting <paramref name="columnMajor"/>, which the
    /// decomposition takes ownership of. The caller must not read it again.
    /// </summary>
    public static Eigen FactorAdopting(double[] columnMajor, int n)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        if (n == 0)
        {
            return new Eigen([], new Complex[0, 0]);
        }

        return IsSymmetric(columnMajor, n)
            ? FactorSymmetric(columnMajor, n)
            : FactorGeneral(columnMajor, n);
    }

    /// <summary>
    /// The eigenvalues alone, overwriting <paramref name="columnMajor"/>, which the call takes
    /// ownership of. Recovering the eigenvectors is most of what a general eigensolver does, and a
    /// caller that only wants the spectrum should not be charged for them.
    /// </summary>
    public static Complex[] Spectrum(double[] columnMajor, int n)
    {
        ArgumentNullException.ThrowIfNull(columnMajor);
        if (n == 0)
        {
            return [];
        }

        var values = new Complex[n];
        if (IsSymmetric(columnMajor, n))
        {
            var w = new double[n];
            LinalgProvider.Current.Syevd(vectors: false, lower: true, n, columnMajor, n, w);
            for (int i = 0; i < n; i++)
            {
                values[i] = w[i];
            }

            return values;
        }

        var wr = new double[n];
        var wi = new double[n];
        LinalgProvider.Current.Geev(vectors: false, n, columnMajor, n, wr, wi, Span<double>.Empty, 1);
        for (int i = 0; i < n; i++)
        {
            values[i] = new Complex(wr[i], wi[i]);
        }

        return values;
    }

    private static bool IsSymmetric(ReadOnlySpan<double> a, int n)
    {
        double scale = 0;
        for (int i = 0; i < n * n; i++)
        {
            scale = Math.Max(scale, Math.Abs(a[i]));
        }

        double tolerance = scale * 1e-12;
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(a[(c * n) + r] - a[(r * n) + c]) > tolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Eigen FactorSymmetric(double[] a, int n)
    {
        var w = new double[n];
        LinalgProvider.Current.Syevd(vectors: true, lower: true, n, a, n, w);

        var values = new Complex[n];
        var vectors = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            values[c] = w[c];
            for (int r = 0; r < n; r++)
            {
                vectors[r, c] = a[(c * n) + r];
            }
        }

        return new Eigen(values, vectors);
    }

    private static Eigen FactorGeneral(double[] a, int n)
    {
        var wr = new double[n];
        var wi = new double[n];
        var vr = new double[(long)n * n];
        LinalgProvider.Current.Geev(vectors: true, n, a, n, wr, wi, vr, n);

        var values = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = new Complex(wr[i], wi[i]);
        }

        return new Eigen(values, DenseLinalg.ComplexVectorsOf(vr, wi, n, n));
    }
}
