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

    /// <summary>
    /// The eigenvalues of the pencil A − λ·B, infinite where B is singular in that direction.
    /// Both arrays are n×n column-major and are overwritten — the call takes ownership.
    /// </summary>
    /// <exception cref="ArgumentException">The pencil is singular — every number an eigenvalue.</exception>
    public static Complex[] PencilSpectrum(double[] a, double[] b, int n)
    {
        var alphar = new double[n];
        var alphai = new double[n];
        var beta = new double[n];
        double scale = LargestOf(b, n);
        if (LinalgProvider.Current.Ggev(vectors: false, n, a, n, b, n,
                alphar, alphai, beta, Span<double>.Empty, 1) != 0)
        {
            throw new ArgumentException(
                "This pencil is singular — every number is an eigenvalue of it — so it has no " +
                "spectrum to compute.");
        }

        return Ratios(alphar, alphai, beta, n, scale);
    }

    /// <summary>
    /// The eigenvalues of a finite pencil with its right eigenvectors — <c>[V, D] = eig(A, B)</c>
    /// with B nonsingular. Each vector carries LAPACK <c>dggev</c>'s scaling: the largest
    /// component's |re| + |im| is 1, which is the convention MATLAB hands back for this form.
    /// Overwrites both arrays.
    /// </summary>
    /// <exception cref="InvalidOperationException">B is singular, so the vector route has no answer.</exception>
    public static (Complex[] Values, Complex[,] Vectors) PencilFactor(double[] a, double[] b, int n)
    {
        var alphar = new double[n];
        var alphai = new double[n];
        var beta = new double[n];
        var vr = new double[(long)n * n];
        double scale = LargestOf(b, n);
        if (LinalgProvider.Current.Ggev(vectors: true, n, a, n, b, n,
                alphar, alphai, beta, vr, n) != 0)
        {
            throw new InvalidOperationException(
                "The pencil's eigenvectors need a nonsingular B, and this B is singular.");
        }

        return (Ratios(alphar, alphai, beta, n, scale), DenseLinalg.ComplexVectorsOf(vr, alphai, n, n));
    }

    /// <summary>
    /// The symmetric-definite pencil, A·z = λ·B·z with B positive definite: real ascending values
    /// and, when asked, vectors scaled so Zᵀ·B·Z is the identity. Overwrites both arrays; the
    /// vectors come back in <paramref name="a"/>'s storage, one column each.
    /// </summary>
    /// <exception cref="ArgumentException">B stopped being positive definite.</exception>
    /// <exception cref="InvalidOperationException">The symmetric eigensolver failed to converge.</exception>
    public static (double[] Values, double[] VectorsColumnMajor) SymmetricPencil(
        double[] a, double[] b, int n, bool vectors)
    {
        var w = new double[n];
        int info = LinalgProvider.Current.Sygvd(vectors, lower: true, n, a, n, b, n, w);
        if (info > n)
        {
            throw new ArgumentException("eig(A, B) took the Cholesky route, but B is not positive definite.");
        }

        if (info != 0)
        {
            throw new InvalidOperationException("The symmetric-definite eigensolver did not converge.");
        }

        return (w, a);
    }

    /// <summary>
    /// α/β as eigenvalues. A β at rounding scale is snapped to the infinity it stands for — the
    /// managed QZ's own rule, applied here so a blocked native iteration keeps the same promise:
    /// a singular B answers Inf, not 1e16.
    /// </summary>
    private static Complex[] Ratios(double[] alphar, double[] alphai, double[] beta, int n, double scale)
    {
        double tolerance = 1e-12 * (1 + scale);
        var values = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = Math.Abs(beta[i]) <= tolerance
                ? new Complex(double.PositiveInfinity, 0)
                : new Complex(alphar[i], alphai[i]) / beta[i];
        }

        return values;
    }

    /// <summary>The largest magnitude in an n×n column-major matrix — the snap rule's yardstick.</summary>
    private static double LargestOf(ReadOnlySpan<double> a, int n)
    {
        double largest = 0;
        for (int i = 0; i < n * n; i++)
        {
            largest = Math.Max(largest, Math.Abs(a[i]));
        }

        return largest;
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
