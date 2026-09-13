using System;
using JGraph.Imaging;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// Item 08 of the head2head_v3 gap-closure plan (ADR 0155): the two marshalling copies around
/// <c>conv2</c> become a tiled transpose (08a), and the packed column-major kernels answer the bits
/// the boxed road answers (08b). Everything here is a bit-for-bit clause — a layout change and a
/// reordering of the same additions have no rounding of their own to hide behind — so the
/// comparisons are on the 64-bit patterns, which is what tells a NaN from a NaN and a −0 from a +0.
/// </summary>
[Collection("JG facade")]
public class FilterKernelsM96Tests
{
    // ----- 08a: the tiled transpose ------------------------------------------------------------

    public static TheoryData<int, int> Shapes() => new()
    {
        { 1, 1 },
        { 1, 7 },
        { 7, 1 },
        { 3, 5 },
        { 63, 65 },       // one short of a tile and one over
        { 64, 64 },       // exactly a tile
        { 130, 70 },      // ragged tiles both ways
        { 2048, 2048 },   // the benchmark's image, above the threading threshold
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheTiledTransposeMovesEveryBitToWhereTheStridedLoopPutIt(int rows, int cols)
    {
        double[] source = Lcg(rows * cols, seed: (rows * 31) + cols);
        // A few payloads a plain equality would not tell apart.
        if (source.Length > 3)
        {
            source[1] = double.NaN;
            source[2] = -0.0;
            source[3] = double.NegativeInfinity;
        }

        var want = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                want[(r * cols) + c] = source[(c * rows) + r];
            }
        }

        var got = new double[rows * cols];
        MatrixLayout.Transpose(source, rows, cols, got);
        AssertSameBits(want, got);

        // Back again is the identity.
        var back = new double[rows * cols];
        MatrixLayout.Transpose(got, cols, rows, back);
        AssertSameBits(source, back);
    }

    [Fact]
    public void TheTransposeDoesNotMoveWithTheNumberOfThreads()
    {
        int was = ParallelKernels.MaxDegree;
        try
        {
            const int rows = 1500;
            const int cols = 1700;   // 2.55M elements: over the memory-bound threshold
            double[] source = Lcg(rows * cols, seed: 9);
            var one = new double[rows * cols];
            var many = new double[rows * cols];
            ParallelKernels.MaxDegree = 1;
            MatrixLayout.Transpose(source, rows, cols, one);
            ParallelKernels.MaxDegree = 16;
            MatrixLayout.Transpose(source, rows, cols, many);
            AssertSameBits(one, many);
        }
        finally
        {
            ParallelKernels.MaxDegree = was;
        }
    }

    [Fact]
    public void TheTransposeRefusesAShapeThatDoesNotFitItsSpans()
    {
        var six = new double[6];
        Assert.Throws<ArgumentException>(() => MatrixLayout.Transpose(six, 2, 4, new double[8]));
        Assert.Throws<ArgumentException>(() => MatrixLayout.Transpose(six, 2, 3, new double[5]));
        MatrixLayout.Transpose(Array.Empty<double>(), 0, 5, Array.Empty<double>());
    }

    // ----- helpers ------------------------------------------------------------------------------

    internal static void AssertSameBits(ReadOnlySpan<double> want, ReadOnlySpan<double> got, string? what = null)
    {
        Assert.Equal(want.Length, got.Length);
        for (int i = 0; i < want.Length; i++)
        {
            long w = BitConverter.DoubleToInt64Bits(want[i]);
            long g = BitConverter.DoubleToInt64Bits(got[i]);
            if (w != g)
            {
                Assert.Fail($"{what ?? "bits"}: element {i} is {got[i]:R} (0x{g:X16}) where the boxed road answered {want[i]:R} (0x{w:X16})");
            }
        }
    }

    /// <summary>A deterministic field in (−1, 1) with a few exact zeros in it.</summary>
    internal static double[] Lcg(int n, int seed)
    {
        var x = new double[n];
        uint state = ((uint)seed * 2654435761u) + 1u;
        for (int i = 0; i < n; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            x[i] = ((state >> 8) / 16777216.0 * 2) - 1;
            if ((state & 0xF000) == 0)
            {
                x[i] = 0;
            }
        }

        return x;
    }

    /// <summary>A 0/1 mask with about <paramref name="density"/> of its pixels set.</summary>
    private static double[] Mask(int n, int seed, double density)
    {
        double[] x = Lcg(n, seed);
        for (int i = 0; i < n; i++)
        {
            x[i] = (x[i] + 1) / 2 < density ? 1 : 0;
        }

        return x;
    }

    private static double[] Taps(int n, int seed)
    {
        double[] t = Lcg(n, seed + 100);
        for (int i = 0; i < n; i++)
        {
            t[i] = (t[i] * 0.9) + 0.05;   // no exact zeros among the taps
        }

        return t;
    }

    private static double[] Box(int n)
    {
        var b = new double[n];
        Array.Fill(b, 1.0 / n);
        return b;
    }

    /// <summary>Taps with zeros, an Inf and a NaN among them.</summary>
    private static double[] Special(int n)
    {
        double[] s = Taps(n, seed: 3);
        s[0] = double.PositiveInfinity;
        s[n / 2] = double.NaN;
        s[n - 1] = 0;
        if (n > 4)
        {
            s[1] = 0;
            s[n - 2] = double.NegativeInfinity;
        }

        return s;
    }

    /// <summary>A column-major flat array as the boxed road's rectangle.</summary>
    private static double[,] Boxed(double[] columnMajor, int rows, int cols)
    {
        var a = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                a[r, c] = columnMajor[(c * rows) + r];
            }
        }

        return a;
    }

    /// <summary>The boxed road's rectangle as a column-major flat array.</summary>
    private static double[] Packed(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var flat = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = a[r, c];
            }
        }

        return flat;
    }
}
