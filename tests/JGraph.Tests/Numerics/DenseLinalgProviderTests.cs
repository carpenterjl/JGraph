using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The M88 provider seam: the managed and OpenBLAS backends answer the same gemm contract. Exact
/// agreement is asserted where IEEE guarantees it (small integer-valued cases); everything else is
/// held to a relative tolerance, because a blocked native kernel reorders accumulation within its
/// last ulps — the same divergence MATLAB's own MKL shows against a naive loop.
/// </summary>
public class DenseLinalgProviderTests
{
    private const double RelativeTolerance = 1e-12;

    private static readonly ManagedLinalg Managed = new();

    private static double[] Deterministic(int length, double seed)
    {
        var values = new double[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = System.Math.Sin(seed * (i + 1)) + System.Math.Cos((seed + 0.5) * (i + 1)) * 0.5;
        }

        return values;
    }

    [Fact]
    public void ManagedGemmMatchesKnownProduct()
    {
        // [1 3; 2 4] stored column-major times [5 7; 6 8] stored column-major.
        double[] a = [1, 2, 3, 4];
        double[] b = [5, 6, 7, 8];
        var c = new double[4];
        Managed.Gemm(false, false, 2, 2, 2, a, 2, b, 2, c, 2);
        Assert.Equal(new double[] { 23, 34, 31, 46 }, c);
    }

    [Fact]
    public void ManagedGemmHonorsLeadingDimensions()
    {
        // A is the top-left 2×2 of a 3-row column-major block; C writes into a 3-row block too.
        double[] a = [1, 2, -9, 3, 4, -9];
        double[] b = [5, 6, 7, 8];
        double[] c = [-1, -1, -1, -1, -1, -1];
        Managed.Gemm(false, false, 2, 2, 2, a, 3, b, 2, c, 3);
        Assert.Equal(23, c[0]);
        Assert.Equal(34, c[1]);
        Assert.Equal(-1, c[2]); // the slack row is untouched
        Assert.Equal(31, c[3]);
        Assert.Equal(46, c[4]);
        Assert.Equal(-1, c[5]);
    }

    [Fact]
    public void ManagedGemmWithZeroInnerDimensionClearsTheResult()
    {
        double[] c = [3, 3, 3, 3];
        Managed.Gemm(false, false, 2, 2, 0, System.Array.Empty<double>(), 2, System.Array.Empty<double>(), 1, c, 2);
        Assert.Equal(new double[] { 0, 0, 0, 0 }, c);
    }

    [Theory]
    [InlineData(3, 4, 5)]
    [InlineData(1, 9, 1)]
    [InlineData(7, 1, 6)]
    [InlineData(64, 32, 48)]
    [InlineData(130, 130, 130)] // above the managed kernel's parallel threshold
    public void NativeGemmMatchesManaged(int m, int n, int k)
    {
        if (!LinalgProvider.NativeAvailable)
        {
            return;
        }

        var native = new OpenBlasLinalg();
        double[] a = Deterministic(m * k, 0.7);
        double[] b = Deterministic(k * n, 1.3);
        var expected = new double[m * n];
        var actual = new double[m * n];
        Managed.Gemm(false, false, m, n, k, a, m, b, k, expected, m);
        native.Gemm(false, false, m, n, k, a, m, b, k, actual, m);
        AssertClose(expected, actual);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeGemmMatchesManagedTransposed(bool transA, bool transB)
    {
        if (!LinalgProvider.NativeAvailable)
        {
            return;
        }

        const int m = 11, n = 7, k = 13;
        var native = new OpenBlasLinalg();
        double[] a = Deterministic((transA ? k * m : m * k), 0.9);
        double[] b = Deterministic((transB ? n * k : k * n), 2.1);
        int lda = transA ? k : m;
        int ldb = transB ? n : k;
        var expected = new double[m * n];
        var actual = new double[m * n];
        Managed.Gemm(transA, transB, m, n, k, a, lda, b, ldb, expected, m);
        native.Gemm(transA, transB, m, n, k, a, lda, b, ldb, actual, m);
        AssertClose(expected, actual);
    }

    [Fact]
    public void NativeGemmWithZeroInnerDimensionClearsTheResult()
    {
        if (!LinalgProvider.NativeAvailable)
        {
            return;
        }

        var native = new OpenBlasLinalg();
        double[] c = [3, 3, 3, 3];
        native.Gemm(false, false, 2, 2, 0, System.Array.Empty<double>(), 2, System.Array.Empty<double>(), 1, c, 2);
        Assert.Equal(new double[] { 0, 0, 0, 0 }, c);
    }

    [Theory]
    [InlineData(true, 7, 5)]
    [InlineData(false, 7, 5)]
    [InlineData(true, 40, 64)]
    [InlineData(false, 40, 64)]
    public void SyrkIsExactlySymmetricAndMatchesGemm(bool transposeFirst, int n, int k)
    {
        // A stored k×n for AᵀA, n×k for AAᵀ.
        int rows = transposeFirst ? k : n;
        int cols = transposeFirst ? n : k;
        double[] a = Deterministic(rows * cols, 1.7);
        var viaGemm = new double[n * n];
        Managed.Gemm(transA: transposeFirst, transB: !transposeFirst, n, n, k, a, rows, a, rows, viaGemm, n);

        foreach (DenseLinalg backend in Backends())
        {
            var c = new double[n * n];
            backend.Syrk(transposeFirst, n, k, a, rows, c, n);
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    Assert.Equal(c[(i * n) + j], c[(j * n) + i]); // bitwise symmetric, not just close
                }
            }

            AssertClose(viaGemm, c);
        }
    }

    private static IEnumerable<DenseLinalg> Backends()
    {
        yield return Managed;
        if (LinalgProvider.NativeAvailable)
        {
            yield return new OpenBlasLinalg();
        }
    }

    [Fact]
    public void StatusReportAlwaysSaysSomething()
    {
        Assert.False(string.IsNullOrWhiteSpace(LinalgProvider.StatusReport));
        Assert.False(string.IsNullOrWhiteSpace(LinalgProvider.Current.Description));
    }

    [Fact]
    public void DenseProductStillAnswersThroughTheProvider()
    {
        double[] a = [1, 2, 3, 4];
        double[] b = [5, 6, 7, 8];
        double[] c = DenseProduct.ColumnMajor(a, 2, 2, b, 2);
        Assert.Equal(new double[] { 23, 34, 31, 46 }, c);
    }

    private static void AssertClose(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        double scale = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            scale = System.Math.Max(scale, System.Math.Abs(expected[i]));
        }

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(System.Math.Abs(expected[i] - actual[i]) <= RelativeTolerance * System.Math.Max(scale, 1),
                $"element {i}: managed {expected[i]:R} vs native {actual[i]:R}");
        }
    }
}
