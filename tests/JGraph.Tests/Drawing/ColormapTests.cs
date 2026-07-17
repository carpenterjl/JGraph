using JGraph.Core.Drawing;
using Xunit;

namespace JGraph.Tests.Drawing;

public class ColormapTests
{
    [Fact]
    public void Sample_EndpointsMatchStops()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(0.0));
        Assert.Equal(Colors.White, map.Sample(1.0));
    }

    [Fact]
    public void Sample_MidpointInterpolatesLinearly()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Color mid = map.Sample(0.5);
        Assert.InRange(mid.R, 126, 129);
        Assert.Equal(mid.R, mid.G);
        Assert.Equal(mid.R, mid.B);
    }

    [Fact]
    public void Sample_ClampsOutOfRange()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(-3.0));
        Assert.Equal(Colors.White, map.Sample(4.0));
    }

    [Fact]
    public void Sample_WithRangeMapsValue()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        // Value 5 in [0, 10] is the midpoint.
        Color mid = map.Sample(5.0, 0.0, 10.0);
        Assert.InRange(mid.R, 126, 129);
    }

    [Fact]
    public void Sample_NaNMapsToLowEnd()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(double.NaN));
    }

    [Fact]
    public void Sample_ThreeStopsPicksCorrectSegment()
    {
        var map = new Colormap("rgb", Colors.Red, Colors.Green, Colors.Blue);
        // t = 0.25 sits halfway through the first (red→green) segment.
        Color quarter = map.Sample(0.25);
        Assert.True(quarter.R > 100 && quarter.G > 40);
        // t = 0.75 sits halfway through the second (green→blue) segment.
        Color threeQuarter = map.Sample(0.75);
        Assert.True(threeQuarter.B > 100);
    }

    [Fact]
    public void Presets_HaveExpectedEndpoints()
    {
        Assert.Equal(Colors.Black, Colormap.Grayscale.Sample(0));
        Assert.Equal(Colors.White, Colormap.Grayscale.Sample(1));
        // Viridis runs dark-purple to yellow.
        Assert.True(Colormap.Viridis.Sample(1).R > 200 && Colormap.Viridis.Sample(1).G > 200);
    }

    [Fact]
    public void Constructor_RejectsTooFewStops()
    {
        Assert.Throws<System.ArgumentException>(() => new Colormap("x", Colors.Red));
    }
}
