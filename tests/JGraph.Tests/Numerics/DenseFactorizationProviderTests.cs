using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The M89 provider surface: LU, the solves against it, the inverse, the condition estimate,
/// Cholesky, the triangular solve, and least squares. Every one is asserted through a property that
/// holds whichever backend computed it — P·A = L·U, A·A⁻¹ = I, Rᵀ·R = A — because a blocked native
/// factorization and a hand-rolled one agree on the answer, not on its last ulps.
/// </summary>
public class DenseFactorizationProviderTests
{
    private const double Tolerance = 1e-9;

    private static readonly ManagedLinalg Managed = new();

    private static IEnumerable<DenseLinalg> Backends()
    {
        yield return Managed;
        if (LinalgProvider.NativeAvailable)
        {
            yield return new OpenBlasLinalg();
        }
    }

    /// <summary>A deterministic well-conditioned n-by-n matrix, column-major.</summary>
    private static double[] General(int n)
    {
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                a[(c * n) + r] = Math.Sin(0.7 * (r + 1)) + Math.Cos(1.3 * (c + 1)) + (r == c ? 2.0 * n : 0);
            }
        }

        return a;
    }

    /// <summary>A deterministic symmetric positive definite n-by-n matrix, column-major.</summary>
    private static double[] Definite(int n)
    {
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = c; r < n; r++)
            {
                double value = Math.Cos(0.4 * ((r * n) + c + 1)) + (r == c ? 2.0 * n : 0);
                a[(c * n) + r] = value;
                a[(r * n) + c] = value;
            }
        }

        return a;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    public void FactorsReassembleThePermutedMatrix(int n)
    {
        double[] original = General(n);
        foreach (DenseLinalg backend in Backends())
        {
            var lu = (double[])original.Clone();
            var ipiv = new int[n];
            Assert.Equal(0, backend.Getrf(n, n, lu, n, ipiv));

            int[] order = DenseLinalg.PermutationOf(ipiv, n);
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    // (L·U)(i, j) = Σ over the shared range, with L's implied unit diagonal.
                    double sum = 0;
                    for (int k = 0; k <= Math.Min(i, j); k++)
                    {
                        double l = k == i ? 1 : lu[(k * n) + i];
                        sum += l * lu[(j * n) + k];
                    }

                    Assert.Equal(original[(j * n) + order[i]], sum, Tolerance);
                }
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    public void SolveReproducesTheRightHandSide(int n)
    {
        double[] original = General(n);
        var b = new double[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = Math.Cos(0.5 * (i + 1)) + 1;
        }

        foreach (DenseLinalg backend in Backends())
        {
            var lu = (double[])original.Clone();
            var ipiv = new int[n];
            backend.Getrf(n, n, lu, n, ipiv);
            var x = (double[])b.Clone();
            backend.Getrs(transpose: false, n, 1, lu, n, ipiv, x, n);

            for (int r = 0; r < n; r++)
            {
                double sum = 0;
                for (int c = 0; c < n; c++)
                {
                    sum += original[(c * n) + r] * x[c];
                }

                Assert.Equal(b[r], sum, Tolerance);
            }
        }
    }

    [Fact]
    public void TransposedSolveUsesTheTransposedMatrix()
    {
        const int n = 5;
        double[] original = General(n);
        var b = new double[] { 1, -2, 3, -4, 5 };

        foreach (DenseLinalg backend in Backends())
        {
            var lu = (double[])original.Clone();
            var ipiv = new int[n];
            backend.Getrf(n, n, lu, n, ipiv);
            var x = (double[])b.Clone();
            backend.Getrs(transpose: true, n, 1, lu, n, ipiv, x, n);

            for (int r = 0; r < n; r++)
            {
                double sum = 0;
                for (int c = 0; c < n; c++)
                {
                    sum += original[(r * n) + c] * x[c]; // Aᵀ(r, c) = A(c, r)
                }

                Assert.Equal(b[r], sum, Tolerance);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(33)]
    public void InverseMultipliesBackToTheIdentity(int n)
    {
        double[] original = General(n);
        foreach (DenseLinalg backend in Backends())
        {
            var inverse = (double[])original.Clone();
            var ipiv = new int[n];
            backend.Getrf(n, n, inverse, n, ipiv);
            Assert.Equal(0, backend.Getri(n, inverse, n, ipiv));

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < n; k++)
                    {
                        sum += original[(k * n) + i] * inverse[(j * n) + k];
                    }

                    Assert.Equal(i == j ? 1 : 0, sum, Tolerance);
                }
            }
        }
    }

    [Fact]
    public void ConditionEstimateIsOneForTheIdentityAndZeroForASingularFactor()
    {
        foreach (DenseLinalg backend in Backends())
        {
            var identity = new double[9];
            identity[0] = identity[4] = identity[8] = 1;
            var ipiv = new int[3];
            backend.Getrf(3, 3, identity, 3, ipiv);
            Assert.Equal(1, backend.Gecon(3, identity, 3, anorm: 1), 12);

            // [1 2; 2 4] column-major: the second pivot vanishes exactly.
            double[] singular = [1, 2, 2, 4];
            var pivots = new int[2];
            Assert.NotEqual(0, backend.Getrf(2, 2, singular, 2, pivots));
            Assert.Equal(0, backend.Gecon(2, singular, 2, anorm: 6));
        }
    }

    [Fact]
    public void ConditionEstimateBracketsTheTrueReciprocal()
    {
        // [1 2; 2 4.0001]: κ₁ is about 1e5, so the reciprocal is small but not zero. LAPACK
        // estimates rather than computes, so the assertion is on the order of magnitude — which is
        // all rcond ever promises, and all MATLAB's own answer promises either.
        double[] nearly = [1, 2, 2, 4.0001];
        double anorm = DenseLinalg.OneNorm(2, 2, nearly, 2);
        foreach (DenseLinalg backend in Backends())
        {
            var lu = (double[])nearly.Clone();
            var ipiv = new int[2];
            backend.Getrf(2, 2, lu, 2, ipiv);
            double rcond = backend.Gecon(2, lu, 2, anorm);
            Assert.InRange(rcond, 1e-7, 1e-4);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(9, true)]
    [InlineData(9, false)]
    [InlineData(48, true)]
    [InlineData(48, false)]
    public void CholeskyFactorReassemblesTheMatrix(int n, bool lower)
    {
        double[] original = Definite(n);
        foreach (DenseLinalg backend in Backends())
        {
            var factor = (double[])original.Clone();
            Assert.Equal(0, backend.Potrf(lower, n, factor, n));

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    // L·Lᵀ or Rᵀ·R, reading only the triangle that was written.
                    double sum = 0;
                    for (int k = 0; k <= Math.Min(i, j); k++)
                    {
                        sum += lower
                            ? factor[(k * n) + i] * factor[(k * n) + j]
                            : factor[(i * n) + k] * factor[(j * n) + k];
                    }

                    Assert.Equal(original[(j * n) + i], sum, Tolerance * Math.Max(1, Math.Abs(original[(j * n) + i])));
                }
            }
        }
    }

    [Fact]
    public void CholeskyReportsTheOrderItStoppedAt()
    {
        // [4 2 1; 2 3 1; 1 1 -5] column-major: the leading 2-by-2 is definite, the third order is not.
        double[] indefinite = [4, 2, 1, 2, 3, 1, 1, 1, -5];
        foreach (DenseLinalg backend in Backends())
        {
            var upper = (double[])indefinite.Clone();
            Assert.Equal(3, backend.Potrf(lower: false, 3, upper, 3));

            var lower = (double[])indefinite.Clone();
            Assert.Equal(3, backend.Potrf(lower: true, 3, lower, 3));
        }
    }

    [Fact]
    public void CholeskyOfASymmetricMatrixMirrorsBetweenTheTwoTriangles()
    {
        // Factoring the upper triangle and factoring the lower one answer the same question, so
        // chol(A) and chol(A, 'lower')' are the same matrix — which is why neither has to be
        // transposed into the other. The managed kernel sums the same products in the same order
        // and so agrees to the bit; a blocked native factorization reorders within its last ulps,
        // and that difference is real and expected rather than a fault.
        const int n = 24;
        double[] original = Definite(n);
        foreach (DenseLinalg backend in Backends())
        {
            var upper = (double[])original.Clone();
            var lower = (double[])original.Clone();
            backend.Potrf(lower: false, n, upper, n);
            backend.Potrf(lower: true, n, lower, n);

            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r <= c; r++)
                {
                    double mirrored = lower[(r * n) + c];
                    if (backend.IsNative)
                    {
                        Assert.Equal(upper[(c * n) + r], mirrored, Tolerance * Math.Max(1, Math.Abs(mirrored)));
                    }
                    else
                    {
                        Assert.Equal(upper[(c * n) + r], mirrored);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void TriangularSolveReproducesTheRightHandSide(bool lower, bool transpose)
    {
        const int n = 6;
        var triangle = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                bool inside = lower ? r >= c : r <= c;
                triangle[(c * n) + r] = inside ? Math.Cos(0.9 * ((c * n) + r + 1)) + (r == c ? 3 : 0) : 0;
            }
        }

        var b = new double[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = i + 1;
        }

        foreach (DenseLinalg backend in Backends())
        {
            var x = (double[])b.Clone();
            Assert.Equal(0, backend.Trtrs(lower, transpose, n, 1, triangle, n, x, n));

            for (int r = 0; r < n; r++)
            {
                double sum = 0;
                for (int c = 0; c < n; c++)
                {
                    sum += (transpose ? triangle[(r * n) + c] : triangle[(c * n) + r]) * x[c];
                }

                Assert.Equal(b[r], sum, Tolerance);
            }
        }
    }

    [Fact]
    public void TriangularSolveRefusesAZeroDiagonal()
    {
        // [1 5; 0 0] column-major, upper triangular with a vanished second pivot.
        double[] singular = [1, 0, 5, 0];
        foreach (DenseLinalg backend in Backends())
        {
            var x = new double[] { 1, 1 };
            Assert.Equal(2, backend.Trtrs(lower: false, transpose: false, 2, 1, singular, 2, x, 2));
        }
    }

    [Fact]
    public void LeastSquaresMinimizesTheResidualOfATallSystem()
    {
        // Four points on y = 2x + 1 with one nudged: the fit is the normal-equation answer.
        const int m = 4, n = 2;
        double[] design = [1, 1, 1, 1, 0, 1, 2, 3];   // column-major: ones, then x
        double[] observed = [1, 3, 5, 7.4];

        foreach (DenseLinalg backend in Backends())
        {
            var a = (double[])design.Clone();
            var b = new double[Math.Max(m, n)];
            observed.CopyTo(b, 0);
            Assert.Equal(0, backend.Gels(m, n, 1, a, m, b, b.Length));

            // The residual must be orthogonal to both design columns — the defining property.
            for (int c = 0; c < n; c++)
            {
                double dot = 0;
                for (int r = 0; r < m; r++)
                {
                    double fitted = (design[r] * b[0]) + (design[m + r] * b[1]);
                    dot += design[(c * m) + r] * (observed[r] - fitted);
                }

                Assert.Equal(0, dot, Tolerance);
            }
        }
    }

    [Fact]
    public void LeastSquaresGivesTheMinimumNormAnswerForAWideSystem()
    {
        // x + y + z = 3 has a plane of solutions; the minimum-norm one is (1, 1, 1).
        const int m = 1, n = 3;
        foreach (DenseLinalg backend in Backends())
        {
            double[] a = [1, 1, 1];
            var b = new double[n];
            b[0] = 3;
            Assert.Equal(0, backend.Gels(m, n, 1, a, m, b, n));
            Assert.Equal(1, b[0], Tolerance);
            Assert.Equal(1, b[1], Tolerance);
            Assert.Equal(1, b[2], Tolerance);
        }
    }

    [Fact]
    public void EmptyMatricesAreAnsweredRatherThanRefused()
    {
        foreach (DenseLinalg backend in Backends())
        {
            Assert.Equal(0, backend.Getrf(0, 0, Array.Empty<double>(), 1, Array.Empty<int>()));
            Assert.Equal(0, backend.Potrf(lower: true, 0, Array.Empty<double>(), 1));
            backend.Getrs(transpose: false, 0, 0, Array.Empty<double>(), 1, Array.Empty<int>(), Array.Empty<double>(), 1);
        }
    }

    [Fact]
    public void OneNormIsTheLargestAbsoluteColumnSum()
    {
        // [1 -4; -2 3] column-major: columns sum to 3 and 7.
        double[] a = [1, -2, -4, 3];
        Assert.Equal(7, DenseLinalg.OneNorm(2, 2, a, 2));
    }

    [Fact]
    public void InterchangeRecordBecomesTheRowOrderItStandsFor()
    {
        // Swap row 0 with row 2, then row 1 with row 2: 0,1,2 → 2,1,0 → 2,0,1.
        int[] ipiv = [3, 3, 3];
        Assert.Equal(new[] { 2, 0, 1 }, DenseLinalg.PermutationOf(ipiv, 3));
    }

    [Fact]
    public void FactorizationsAgreeBetweenTheBackends()
    {
        if (!LinalgProvider.NativeAvailable)
        {
            return;
        }

        const int n = 50;
        var native = new OpenBlasLinalg();
        double[] original = General(n);

        var managedLu = (double[])original.Clone();
        var nativeLu = (double[])original.Clone();
        var managedPivots = new int[n];
        var nativePivots = new int[n];
        Managed.Getrf(n, n, managedLu, n, managedPivots);
        native.Getrf(n, n, nativeLu, n, nativePivots);

        // Partial pivoting picks the same rows in both — the factors differ only in their last ulps.
        Assert.Equal(managedPivots, nativePivots);
        for (int i = 0; i < managedLu.Length; i++)
        {
            Assert.Equal(managedLu[i], nativeLu[i], Tolerance * Math.Max(1, Math.Abs(managedLu[i])));
        }

        double[] definite = Definite(n);
        var managedChol = (double[])definite.Clone();
        var nativeChol = (double[])definite.Clone();
        Managed.Potrf(lower: false, n, managedChol, n);
        native.Potrf(lower: false, n, nativeChol, n);
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r <= c; r++)
            {
                int at = (c * n) + r;
                Assert.Equal(managedChol[at], nativeChol[at], Tolerance * Math.Max(1, Math.Abs(managedChol[at])));
            }
        }
    }
}
