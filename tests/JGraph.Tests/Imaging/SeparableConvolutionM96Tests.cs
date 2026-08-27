using System;
using JGraph.Imaging;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M96b: <c>conv2(u, v, A)</c> stops building the outer product of its two vectors and runs the two
/// passes the product stood for. Two passes of sums round differently from one pass over a
/// materialised kernel, so what the tests here pin is not the old bits but the old answer: every
/// shape, every anchor and every crop agrees with the built-kernel convolution to within the
/// precision either of them has, the delta kernel comes back as the image itself exactly, and the
/// answer does not move with the number of threads.
/// </summary>
public class SeparableConvolutionM96Tests
{
    public static TheoryData<int, int, int, int> Sizes() => new()
    {
        { 8, 8, 3, 3 },
        { 8, 8, 4, 4 },      // even taps, so the 'same' anchor is off-centre
        { 7, 11, 5, 3 },
        { 11, 7, 1, 9 },
        { 5, 5, 7, 7 },      // a kernel larger than the image
        { 1, 12, 1, 4 },     // a row of samples
        { 12, 1, 4, 1 },     // a column
        { 64, 48, 21, 21 },  // the shape the blur benchmark uses
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryShapeAgreesWithTheKernelItNoLongerBuilds(int ah, int aw, int uh, int vw)
    {
        double[,] a = Field(ah, aw, seed: ah + aw);
        double[] u = Taps(uh, seed: uh);
        double[] v = Taps(vw, seed: vw + 7);
        var outer = new double[uh, vw];
        for (int r = 0; r < uh; r++)
        {
            for (int c = 0; c < vw; c++)
            {
                outer[r, c] = u[r] * v[c];
            }
        }

        foreach (Conv2Shape shape in new[] { Conv2Shape.Full, Conv2Shape.Same, Conv2Shape.Valid })
        {
            double[,] want = Filters.Convolve2(a, outer, shape);
            double[,] got = Filters.SeparableConvolve2(a, u, v, shape);

            Assert.Equal(want.GetLength(0), got.GetLength(0));
            Assert.Equal(want.GetLength(1), got.GetLength(1));

            double scale = 0;
            double worst = 0;
            for (int r = 0; r < want.GetLength(0); r++)
            {
                for (int c = 0; c < want.GetLength(1); c++)
                {
                    scale = Math.Max(scale, Math.Abs(want[r, c]));
                    worst = Math.Max(worst, Math.Abs(want[r, c] - got[r, c]));
                }
            }

            Assert.True(
                worst <= Math.Max(scale, 1) * 1e-13,
                $"{shape} {ah}x{aw} with {uh}x{vw}: drifted {worst:E3} against a scale of {scale:E3}");
        }
    }

    [Fact]
    public void ADeltaKernelHandsTheImageBackExactly()
    {
        double[,] a = Field(9, 6, seed: 42);
        double[] u = [0, 1, 0];
        double[] v = [0, 0, 1, 0, 0];
        double[,] got = Filters.SeparableConvolve2(a, u, v, Conv2Shape.Same);
        for (int r = 0; r < 9; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(a[r, c]),
                    BitConverter.DoubleToInt64Bits(got[r, c]));
            }
        }
    }

    [Fact]
    public void TheAnswerDoesNotMoveWithTheNumberOfThreads()
    {
        int was = ParallelKernels.MaxDegree;
        try
        {
            double[,] a = Field(400, 400, seed: 3);
            double[] u = Taps(21, seed: 1);
            double[] v = Taps(21, seed: 2);

            ParallelKernels.MaxDegree = 1;
            double[,] one = Filters.SeparableConvolve2(a, u, v, Conv2Shape.Same);
            ParallelKernels.MaxDegree = 16;
            double[,] many = Filters.SeparableConvolve2(a, u, v, Conv2Shape.Same);

            for (int r = 0; r < one.GetLength(0); r++)
            {
                for (int c = 0; c < one.GetLength(1); c++)
                {
                    Assert.Equal(
                        BitConverter.DoubleToInt64Bits(one[r, c]),
                        BitConverter.DoubleToInt64Bits(many[r, c]));
                }
            }
        }
        finally
        {
            ParallelKernels.MaxDegree = was;
        }
    }

    /// <summary>
    /// An empty operand has no two passes to run, so the separable entry hands the call back to the
    /// general one — which keeps whatever it answered before, odd sizes and all.
    /// </summary>
    [Fact]
    public void AnEmptyOperandFallsBackToTheGeneralConvolution()
    {
        double[,] a = Field(4, 4, seed: 1);
        double[,] noTaps = Filters.SeparableConvolve2(a, [], [1.0], Conv2Shape.Full);
        double[,] sameWay = Filters.Convolve2(a, new double[0, 1], Conv2Shape.Full);
        Assert.Equal(sameWay.GetLength(0), noTaps.GetLength(0));
        Assert.Equal(sameWay.GetLength(1), noTaps.GetLength(1));

        double[,] noImage = Filters.SeparableConvolve2(new double[0, 0], [1.0], [1.0], Conv2Shape.Full);
        Assert.Empty(noImage);

        // A single tap either way is the image itself, whichever road it took.
        double[,] unit = Filters.SeparableConvolve2(a, [1.0], [1.0], Conv2Shape.Same);
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(a[r, c]),
                    BitConverter.DoubleToInt64Bits(unit[r, c]));
            }
        }
    }

    private static double[,] Field(int rows, int cols, int seed)
    {
        var a = new double[rows, cols];
        double phi = 0.618033988749895;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                a[r, c] = (((r * cols) + c + seed) * phi % 1.0) - 0.5 + Math.Sin((r * 0.3) + (c * 0.7));
            }
        }

        return a;
    }

    private static double[] Taps(int n, int seed)
    {
        var t = new double[n];
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            t[i] = Math.Exp(-((i - ((n - 1) / 2.0)) * (i - ((n - 1) / 2.0))) / (2.0 + seed));
            total += t[i];
        }

        for (int i = 0; i < n; i++)
        {
            t[i] /= total;
        }

        return t;
    }
}
