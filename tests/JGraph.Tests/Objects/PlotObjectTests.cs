using JGraph.Core.Primitives;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

public class PlotObjectTests
{
    [Fact]
    public void LinePlot_BoundsFromData()
    {
        var line = new LinePlot(new double[] { 0, 1, 2 }, new double[] { -2, 3, 1 });
        Assert.Equal(new DataRange(0, 2), line.GetXDataBounds());
        Assert.Equal(new DataRange(-2, 3), line.GetYDataBounds());
    }

    [Fact]
    public void BarPlot_YBoundsIncludeBaseline()
    {
        var bar = new BarPlot(new double[] { 1, 2, 3 }, new double[] { 5, 8, 6 });
        // Baseline 0 must be included so bars are anchored to it.
        Assert.Equal(0, bar.GetYDataBounds().Min);
        Assert.Equal(8, bar.GetYDataBounds().Max);
    }

    [Fact]
    public void BarPlot_XBoundsExpandByHalfBar()
    {
        var bar = new BarPlot(new double[] { 1, 2, 3 }, new double[] { 5, 8, 6 })
        {
            BarWidthFraction = 1.0,
        };

        // Spacing is 1, half-bar is 0.5, so X extent widens to [0.5, 3.5].
        Assert.Equal(0.5, bar.GetXDataBounds().Min, 6);
        Assert.Equal(3.5, bar.GetXDataBounds().Max, 6);
    }

    [Fact]
    public void HorizontalBar_SwapsBoundsRoles()
    {
        var bar = new BarPlot(new double[] { 1, 2, 3 }, new double[] { 5, 8, 6 })
        {
            Horizontal = true,
            BarWidthFraction = 1.0,
        };

        // Values (plus baseline) drive X; positions (plus half-bar) drive Y.
        Assert.Equal(0, bar.GetXDataBounds().Min);
        Assert.Equal(8, bar.GetXDataBounds().Max);
        Assert.Equal(0.5, bar.GetYDataBounds().Min, 6);
        Assert.Equal(3.5, bar.GetYDataBounds().Max, 6);
    }

    [Fact]
    public void LinePlot_HitTest_FindsNearestPoint()
    {
        var line = new LinePlot(new double[] { 0, 1, 2 }, new double[] { 0, 1, 2 });
        var mapper = new IdentityMapper(new Rect2D(0, 0, 100, 100));

        // Query near data point (1, 1) which maps to pixel (1, 1) under identity.
        var hit = line.HitTest(new Point2D(1.4, 1.2), mapper, tolerancePixels: 5);

        Assert.NotNull(hit);
        Assert.Equal(1, hit!.PointIndex);
    }

    /// <summary>A trivial mapper that treats data coordinates as pixels (for hit-test math).</summary>
    private sealed class IdentityMapper : Core.Primitives.ICoordinateMapper
    {
        public IdentityMapper(Rect2D area) => PlotArea = area;

        public Rect2D PlotArea { get; }

        public Point2D DataToPixel(double x, double y) => new(x, y);

        public Point2D PixelToData(double px, double py) => new(px, py);
    }
}
