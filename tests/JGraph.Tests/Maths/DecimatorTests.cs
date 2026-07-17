using JGraph.Core.Primitives;
using JGraph.Maths.Decimation;
using Xunit;

namespace JGraph.Tests.Maths;

public class DecimatorTests
{
    private static (double[] Xs, double[] Ys) MakeRamp(int n)
    {
        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = i;
            ys[i] = System.Math.Sin(i * 0.001);
        }

        return (xs, ys);
    }

    [Fact]
    public void Decimate_ReducesPointCount()
    {
        (double[] xs, double[] ys) = MakeRamp(1_000_000);
        int columns = 800;
        var output = new Point2D[MinMaxDecimator.RequiredBufferSize(columns)];

        int written = MinMaxDecimator.Decimate(xs, ys, new DataRange(0, 999_999), columns, output);

        Assert.True(written <= columns * 2);
        Assert.True(written > 0);
    }

    [Fact]
    public void Decimate_PreservesVerticalEnvelope()
    {
        // A spike in one bucket must survive decimation.
        int n = 100_000;
        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = i;
            ys[i] = 0;
        }

        ys[54_321] = 999; // single tall spike

        int columns = 500;
        var output = new Point2D[MinMaxDecimator.RequiredBufferSize(columns)];
        int written = MinMaxDecimator.Decimate(xs, ys, new DataRange(0, n - 1), columns, output);

        bool spikePreserved = false;
        for (int i = 0; i < written; i++)
        {
            if (output[i].Y == 999)
            {
                spikePreserved = true;
                break;
            }
        }

        Assert.True(spikePreserved);
    }

    [Fact]
    public void Decimate_SmallDataCopiedVerbatim()
    {
        double[] xs = { 0, 1, 2, 3 };
        double[] ys = { 5, 6, 7, 8 };
        var output = new Point2D[MinMaxDecimator.RequiredBufferSize(100)];

        int written = MinMaxDecimator.Decimate(xs, ys, new DataRange(0, 3), 100, output);

        Assert.Equal(4, written);
        Assert.Equal(new Point2D(0, 5), output[0]);
        Assert.Equal(new Point2D(3, 8), output[3]);
    }

    [Fact]
    public void Decimate_WindowsToVisibleRange()
    {
        (double[] xs, double[] ys) = MakeRamp(10_000);
        var output = new Point2D[MinMaxDecimator.RequiredBufferSize(200)];

        int written = MinMaxDecimator.Decimate(xs, ys, new DataRange(4000, 4100), 200, output);

        // All emitted points should be near the visible window (plus one neighbor of padding).
        for (int i = 0; i < written; i++)
        {
            Assert.InRange(output[i].X, 3999, 4101);
        }
    }

    [Fact]
    public void LowerBound_And_UpperBound_FindWindow()
    {
        double[] xs = { 0, 1, 2, 2, 2, 3, 4 };
        Assert.Equal(2, MinMaxDecimator.LowerBound(xs, 2));
        Assert.Equal(5, MinMaxDecimator.UpperBound(xs, 2));
    }
}
