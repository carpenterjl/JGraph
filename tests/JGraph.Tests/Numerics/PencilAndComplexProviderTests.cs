using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The M91 provider surface: the complex z-routines (product, LU trio, eigensolver, SVD), the two
/// generalized eigensolvers, the real Schur form, the QZ factorization, and the Schur reorder.
/// Every assertion is a property that holds whichever backend computed it — residuals, unitarity,
/// structure — because the backends agree on the answer and not on the phase of any particular
/// column of it. Where a convention is fixed (descending singular values, ascending
/// symmetric-definite eigenvalues, Zᵀ·B·Z = I) it is asserted directly.
/// </summary>
public class PencilAndComplexProviderTests
{
    private const double Tolerance = 1e-9;

    private static readonly ManagedLinalg Managed = new();

    public static TheoryData<string> BackendNames()
    {
        var data = new TheoryData<string> { "managed" };
        if (LinalgProvider.NativeAvailable)
        {
            data.Add("native");
        }

        return data;
    }

    private static DenseLinalg Backend(string name) =>
        name == "managed" ? Managed : new OpenBlasLinalg();

    /// <summary>A deterministic complex m-by-n matrix, column-major interleaved.</summary>
    private static Complex[] ComplexRectangular(int m, int n)
    {
        var a = new Complex[m * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                a[(c * m) + r] = new Complex(
                    Math.Sin(0.7 * (r + 1)) + (r == c ? m : 0),
                    Math.Cos(1.3 * ((r * n) + c + 1)));
            }
        }

        return a;
    }

    private static double[] General(int n, int seed)
    {
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                a[(c * n) + r] = Math.Sin((0.7 * (r + 1)) + seed) + Math.Cos(1.3 * (c + 1))
                    + (r == c ? 2.0 : 0);
            }
        }

        return a;
    }

    private static double[] SymmetricPositive(int n, double shift)
    {
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = c; r < n; r++)
            {
                double value = Math.Cos(0.4 * ((r * n) + c + 1)) + (r == c ? n + shift : 0);
                a[(c * n) + r] = value;
                a[(r * n) + c] = value;
            }
        }

        return a;
    }

    private static Complex[] ZProduct(ReadOnlySpan<Complex> a, int m, int k, ReadOnlySpan<Complex> b, int n)
    {
        var c = new Complex[m * n];
        for (int j = 0; j < n; j++)
        {
            for (int p = 0; p < k; p++)
            {
                Complex scale = b[(j * k) + p];
                for (int i = 0; i < m; i++)
                {
                    c[(j * m) + i] += a[(p * m) + i] * scale;
                }
            }
        }

        return c;
    }

    // --- Zgemm --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheComplexProductMatchesTheHandRolledOne(string name)
    {
        Complex[] a = ComplexRectangular(4, 3);
        Complex[] b = ComplexRectangular(3, 5);
        var c = new Complex[4 * 5];
        Backend(name).Zgemm(4, 5, 3, a, 4, b, 3, c, 4);

        Complex[] expected = ZProduct(a, 4, 3, b, 5);
        for (int i = 0; i < c.Length; i++)
        {
            Assert.True((c[i] - expected[i]).Magnitude < Tolerance, $"element {i} off by {(c[i] - expected[i]).Magnitude}");
        }
    }

    // --- The complex LU trio ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheComplexSolveLeavesNoResidual(string name)
    {
        const int N = 6;
        Complex[] a = ComplexRectangular(N, N);
        var pristine = (Complex[])a.Clone();
        Complex[] b = ComplexRectangular(N, 2);
        var rhs = (Complex[])b.Clone();

        DenseLinalg backend = Backend(name);
        var pivots = new int[N];
        Assert.Equal(0, backend.Zgetrf(N, N, a, N, pivots));
        backend.Zgetrs(N, 2, a, N, pivots, rhs, N);

        Complex[] reproduced = ZProduct(pristine, N, N, rhs, 2);
        for (int i = 0; i < b.Length; i++)
        {
            Assert.True((reproduced[i] - b[i]).Magnitude < Tolerance, $"residual {(reproduced[i] - b[i]).Magnitude}");
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheComplexInverseInverts(string name)
    {
        const int N = 5;
        Complex[] a = ComplexRectangular(N, N);
        var pristine = (Complex[])a.Clone();

        DenseLinalg backend = Backend(name);
        var pivots = new int[N];
        Assert.Equal(0, backend.Zgetrf(N, N, a, N, pivots));
        Assert.Equal(0, backend.Zgetri(N, a, N, pivots));

        Complex[] product = ZProduct(pristine, N, N, a, N);
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                Complex expected = r == c ? Complex.One : Complex.Zero;
                Assert.True((product[(c * N) + r] - expected).Magnitude < Tolerance,
                    $"({r},{c}) off by {(product[(c * N) + r] - expected).Magnitude}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ASingularComplexMatrixIsReportedNotSolved(string name)
    {
        // Two proportional rows: singular however you pivot.
        Complex[] a = [new(1, 1), new(2, 2), new(2, 0), new(4, 0)];
        var pivots = new int[2];
        Assert.NotEqual(0, Backend(name).Zgetrf(2, 2, a, 2, pivots));
    }

    // --- Zgeev --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ComplexEigenpairsSatisfyTheirDefinition(string name)
    {
        const int N = 6;
        Complex[] a = ComplexRectangular(N, N);
        var pristine = (Complex[])a.Clone();
        var values = new Complex[N];
        var vectors = new Complex[N * N];

        Assert.Equal(0, Backend(name).Zgeev(vectors: true, N, a, N, values, vectors, N));

        for (int j = 0; j < N; j++)
        {
            double residual = 0;
            double length = 0;
            for (int r = 0; r < N; r++)
            {
                Complex av = Complex.Zero;
                for (int k = 0; k < N; k++)
                {
                    av += pristine[(k * N) + r] * vectors[(j * N) + k];
                }

                residual = Math.Max(residual, (av - (values[j] * vectors[(j * N) + r])).Magnitude);
                length += vectors[(j * N) + r].Magnitude * vectors[(j * N) + r].Magnitude;
            }

            Assert.True(residual < 1e-7, $"eigenpair {j} residual {residual}");
            Assert.Equal(1.0, Math.Sqrt(length), 6);
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ComplexEigenvaluesReproduceTheTrace(string name)
    {
        const int N = 7;
        Complex[] a = ComplexRectangular(N, N);
        Complex trace = Complex.Zero;
        for (int i = 0; i < N; i++)
        {
            trace += a[(i * N) + i];
        }

        var values = new Complex[N];
        Assert.Equal(0, Backend(name).Zgeev(vectors: false, N, a, N, values, [], 1));

        Complex sum = Complex.Zero;
        foreach (Complex value in values)
        {
            sum += value;
        }

        Assert.True((sum - trace).Magnitude < 1e-8, $"trace off by {(sum - trace).Magnitude}");
    }

    // --- Zgesdd -------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheComplexSvdReassemblesTallWideAndSquare(string name)
    {
        foreach ((int m, int n) in new[] { (6, 4), (4, 6), (5, 5) })
        {
            Complex[] a = ComplexRectangular(m, n);
            var pristine = (Complex[])a.Clone();
            int k = Math.Min(m, n);
            var s = new double[k];
            var u = new Complex[m * m];
            var vt = new Complex[n * n];

            Assert.Equal(0, Backend(name).Zgesdd(SvdVectors.All, m, n, a, m, s, u, m, vt, n));

            // Descending, nonnegative.
            for (int i = 1; i < k; i++)
            {
                Assert.True(s[i] <= s[i - 1] + 1e-12, $"σ ascends at {i} for {m}x{n}");
            }

            // U unitary: UᴴU = I over all m columns.
            for (int c = 0; c < m; c++)
            {
                for (int c2 = 0; c2 < m; c2++)
                {
                    Complex dot = Complex.Zero;
                    for (int r = 0; r < m; r++)
                    {
                        dot += Complex.Conjugate(u[(c * m) + r]) * u[(c2 * m) + r];
                    }

                    Complex expected = c == c2 ? Complex.One : Complex.Zero;
                    Assert.True((dot - expected).Magnitude < 1e-8, $"UᴴU ({c},{c2}) for {m}x{n}: {(dot - expected).Magnitude}");
                }
            }

            // A = U·Σ·Vᴴ.
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < m; r++)
                {
                    Complex sum = Complex.Zero;
                    for (int i = 0; i < k; i++)
                    {
                        sum += u[(i * m) + r] * s[i] * vt[(c * n) + i];
                    }

                    Assert.True((sum - pristine[(c * m) + r]).Magnitude < 1e-8,
                        $"A − UΣVᴴ at ({r},{c}) for {m}x{n}: {(sum - pristine[(c * m) + r]).Magnitude}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ComplexSingularValuesDoNotLoseTheSmallOnes(string name)
    {
        // A nearly rank-deficient matrix: σ₂ ≈ 1e-8. The old Gram-matrix route squared it to 1e-16
        // and lost half its digits; a genuine SVD keeps them.
        Complex[] a =
        [
            new(1, 0), new(0, 1e-8),
            new(0, 1), new(1e-8, 0),
        ];
        var s = new double[2];
        Assert.Equal(0, Backend(name).Zgesdd(SvdVectors.None, 2, 2, a, 2, s, [], 1, [], 1));
        Assert.Equal(Math.Sqrt(2), s[0], 10);
        Assert.Equal(1e-8 * Math.Sqrt(2), s[1], 12);
    }

    // --- Ggev / Sygvd -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void GeneralizedEigenvaluesSolveTheirDeterminantEquation(string name)
    {
        const int N = 5;
        double[] a = General(N, 1);
        double[] b = SymmetricPositive(N, 1);
        var alphar = new double[N];
        var alphai = new double[N];
        var beta = new double[N];

        Assert.Equal(0, Backend(name).Ggev(vectors: false, N,
            (double[])a.Clone(), N, (double[])b.Clone(), N, alphar, alphai, beta, [], 1));

        // Each eigenvalue λ makes A − λ·B singular: its smallest singular value is ~0.
        for (int i = 0; i < N; i++)
        {
            Assert.True(beta[i] != 0, $"β[{i}] vanished for a nonsingular B");
            var lambda = new Complex(alphar[i], alphai[i]) / beta[i];

            // ‖(A − λB)·x‖ minimized ≈ 0 is awkward without a complex solver here; the determinant
            // of the real 2n embedding answers the same question with real arithmetic.
            int n2 = 2 * N;
            var embedded = new double[n2, n2];
            for (int c = 0; c < N; c++)
            {
                for (int r = 0; r < N; r++)
                {
                    double real = a[(c * N) + r] - (lambda.Real * b[(c * N) + r]);
                    double imag = -lambda.Imaginary * b[(c * N) + r];
                    embedded[r, c] = real;
                    embedded[r, c + N] = -imag;
                    embedded[r + N, c] = imag;
                    embedded[r + N, c + N] = real;
                }
            }

            LuDecomposition lu = LuDecomposition.Factor(embedded);
            double scale = 0;
            foreach (double value in embedded)
            {
                scale = Math.Max(scale, Math.Abs(value));
            }

            Assert.True(Math.Abs(lu.Determinant) < 1e-6 * Math.Pow(scale, n2),
                $"det(A − λ{i}B) = {lu.Determinant}");
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void GeneralizedEigenvectorsSatisfyThePencil(string name)
    {
        const int N = 4;
        double[] a = General(N, 3);
        double[] b = SymmetricPositive(N, 2);
        var alphar = new double[N];
        var alphai = new double[N];
        var beta = new double[N];
        var vr = new double[N * N];

        Assert.Equal(0, Backend(name).Ggev(vectors: true, N,
            (double[])a.Clone(), N, (double[])b.Clone(), N, alphar, alphai, beta, vr, N));

        Complex[,] vectors = DenseLinalg.ComplexVectorsOf(vr, alphai, N, N);
        for (int j = 0; j < N; j++)
        {
            var lambda = new Complex(alphar[j], alphai[j]) / beta[j];
            double residual = 0;
            for (int r = 0; r < N; r++)
            {
                Complex av = Complex.Zero;
                Complex bv = Complex.Zero;
                for (int k = 0; k < N; k++)
                {
                    av += a[(k * N) + r] * vectors[k, j];
                    bv += b[(k * N) + r] * vectors[k, j];
                }

                residual = Math.Max(residual, (av - (lambda * bv)).Magnitude);
            }

            Assert.True(residual < 1e-8, $"pencil eigenpair {j} residual {residual}");
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheSymmetricDefinitePencilAscendsAndBNormalizes(string name)
    {
        const int N = 5;
        double[] a = SymmetricPositive(N, 0);
        a[1] += 0.5;
        a[N] += 0.5; // keep it symmetric but not equal to b
        double[] b = SymmetricPositive(N, 3);
        var aWork = (double[])a.Clone();
        var bWork = (double[])b.Clone();
        var w = new double[N];

        Assert.Equal(0, Backend(name).Sygvd(vectors: true, lower: true, N, aWork, N, bWork, N, w));

        for (int i = 1; i < N; i++)
        {
            Assert.True(w[i] >= w[i - 1], $"eigenvalues descend at {i}");
        }

        // Zᵀ·B·Z = I, and A·z = λ·B·z.
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                double bij = 0;
                for (int r = 0; r < N; r++)
                {
                    double bz = 0;
                    for (int k = 0; k < N; k++)
                    {
                        bz += b[(k * N) + r] * aWork[(j * N) + k];
                    }

                    bij += aWork[(i * N) + r] * bz;
                }

                Assert.Equal(i == j ? 1.0 : 0.0, bij, 8);
            }

            for (int r = 0; r < N; r++)
            {
                double az = 0;
                double bz = 0;
                for (int k = 0; k < N; k++)
                {
                    az += a[(k * N) + r] * aWork[(i * N) + k];
                    bz += b[(k * N) + r] * aWork[(i * N) + k];
                }

                Assert.Equal(az, w[i] * bz, 7);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void AnIndefiniteBIsReportedThroughTheInfoCode(string name)
    {
        double[] a = SymmetricPositive(3, 0);
        double[] b = [1, 0, 0, 0, -1, 0, 0, 0, 1]; // symmetric, not positive definite
        var w = new double[3];
        Assert.True(Backend(name).Sygvd(vectors: false, lower: true, 3, a, 3, b, 3, w) > 3);
    }

    // --- Gees / Gges / Trsen ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheSchurFormReassemblesWithAnOrthogonalFactor(string name)
    {
        const int N = 6;
        double[] a = General(N, 5);
        var t = (double[])a.Clone();
        var wr = new double[N];
        var wi = new double[N];
        var vs = new double[N * N];

        Assert.Equal(0, Backend(name).Gees(vectors: true, N, t, N, wr, wi, vs, N));

        // Z·T·Zᵀ = A, Z orthogonal, T quasi-triangular.
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                double sum = 0;
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        sum += vs[(i * N) + r] * t[(j * N) + i] * vs[(j * N) + c];
                    }
                }

                Assert.Equal(a[(c * N) + r], sum, 8);
                if (r > c + 1)
                {
                    Assert.Equal(0.0, t[(c * N) + r], 12);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheQzFactorizationReassemblesBothMatrices(string name)
    {
        const int N = 5;
        double[] a = General(N, 7);
        double[] b = General(N, 11);
        var aa = (double[])a.Clone();
        var bb = (double[])b.Clone();
        var alphar = new double[N];
        var alphai = new double[N];
        var beta = new double[N];
        var vsl = new double[N * N];
        var vsr = new double[N * N];

        Assert.Equal(0, Backend(name).Gges(vectors: true, N, aa, N, bb, N,
            alphar, alphai, beta, vsl, N, vsr, N));

        // A = VSL·AA·VSRᵀ and B = VSL·BB·VSRᵀ; BB is upper triangular.
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                double sumA = 0;
                double sumB = 0;
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        double left = vsl[(i * N) + r];
                        double right = vsr[(j * N) + c];
                        sumA += left * aa[(j * N) + i] * right;
                        sumB += left * bb[(j * N) + i] * right;
                    }
                }

                Assert.Equal(a[(c * N) + r], sumA, 8);
                Assert.Equal(b[(c * N) + r], sumB, 8);
                if (r > c)
                {
                    Assert.Equal(0.0, bb[(c * N) + r], 10);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ReorderingBringsTheChosenEigenvalueToTheTop(string name)
    {
        const int N = 5;
        double[] a = General(N, 13);
        var t = (double[])a.Clone();
        var wr = new double[N];
        var wi = new double[N];
        var q = new double[N * N];

        DenseLinalg backend = Backend(name);
        Assert.Equal(0, backend.Gees(vectors: true, N, t, N, wr, wi, q, N));

        // Choose the eigenvalue with the smallest real part, wherever its block sits.
        int chosen = 0;
        for (int i = 1; i < N; i++)
        {
            if (wr[i] < wr[chosen])
            {
                chosen = i;
            }
        }

        double target = wr[chosen];
        var select = new bool[N];
        select[chosen] = true;
        if (wi[chosen] > 0 && chosen + 1 < N)
        {
            select[chosen + 1] = true;
        }
        else if (wi[chosen] < 0 && chosen > 0)
        {
            select[chosen - 1] = true;
        }

        Assert.Equal(0, backend.Trsen(select, N, t, N, q, N, wr, wi));
        Assert.Equal(target, wr[0], 8);

        // The similarity is preserved: Q·T·Qᵀ is still A.
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                double sum = 0;
                for (int i = 0; i < N; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        sum += q[(i * N) + r] * t[(j * N) + i] * q[(j * N) + c];
                    }
                }

                Assert.Equal(a[(c * N) + r], sum, 8);
            }
        }
    }
}
