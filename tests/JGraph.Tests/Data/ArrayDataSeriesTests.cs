using JGraph.Core.Data;
using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Data;

public class ArrayDataSeriesTests
{
    [Fact]
    public void ComputesBounds()
    {
        var series = new ArrayDataSeries(new double[] { 0, 1, 2 }, new double[] { -3, 5, 1 });
        Assert.Equal(new DataRange(0, 2), series.XBounds);
        Assert.Equal(new DataRange(-3, 5), series.YBounds);
    }

    [Fact]
    public void DetectsAscendingX()
    {
        var ascending = new ArrayDataSeries(new double[] { 0, 1, 2 }, new double[] { 0, 0, 0 });
        var jumbled = new ArrayDataSeries(new double[] { 0, 2, 1 }, new double[] { 0, 0, 0 });
        Assert.True(ascending.IsXAscending);
        Assert.False(jumbled.IsXAscending);
    }

    [Fact]
    public void IgnoresNonFiniteInBounds()
    {
        var series = new ArrayDataSeries(
            new double[] { 0, 1, 2 },
            new double[] { 1, double.NaN, 3 });
        Assert.Equal(new DataRange(1, 3), series.YBounds);
    }

    [Fact]
    public void FromValues_UsesIndexAsX()
    {
        var series = ArrayDataSeries.FromValues(new double[] { 10, 20, 30 });
        Assert.Equal(0, series.GetX(0));
        Assert.Equal(2, series.GetX(2));
        Assert.Equal(new DataRange(0, 2), series.XBounds);
    }

    [Fact]
    public void TryGetSpans_ExposesStorage()
    {
        var series = new ArrayDataSeries(new double[] { 1, 2 }, new double[] { 3, 4 });
        Assert.True(series.TryGetSpans(out ReadOnlySpan<double> xs, out ReadOnlySpan<double> ys));
        Assert.Equal(2, xs.Length);
        Assert.Equal(4, ys[1]);
    }

    [Fact]
    public void MismatchedLengths_Throw()
    {
        Assert.Throws<ArgumentException>(() => new ArrayDataSeries(new double[] { 1 }, new double[] { 1, 2 }));
    }
}
