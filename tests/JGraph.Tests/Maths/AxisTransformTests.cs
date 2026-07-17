using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using Xunit;

namespace JGraph.Tests.Maths;

public class AxisTransformTests
{
    private static AxisTransform LinearTransform(Rect2D area) => new(
        area,
        LinearScaleTransform.Instance,
        new DataRange(0, 10),
        xInverted: false,
        LinearScaleTransform.Instance,
        new DataRange(0, 100),
        yInverted: false);

    [Fact]
    public void DataToPixel_MapsCornersToRectEdges()
    {
        var area = new Rect2D(50, 20, 200, 160); // left=50 right=250 top=20 bottom=180
        AxisTransform t = LinearTransform(area);

        Point2D bottomLeft = t.DataToPixel(0, 0);
        Assert.Equal(50, bottomLeft.X, 6);
        Assert.Equal(180, bottomLeft.Y, 6); // data y-min at bottom

        Point2D topRight = t.DataToPixel(10, 100);
        Assert.Equal(250, topRight.X, 6);
        Assert.Equal(20, topRight.Y, 6); // data y-max at top
    }

    [Fact]
    public void DataToPixel_YAxisIsFlipped()
    {
        var area = new Rect2D(0, 0, 100, 100);
        AxisTransform t = LinearTransform(area);
        Point2D mid = t.DataToPixel(5, 50);
        Assert.Equal(50, mid.X, 6);
        Assert.Equal(50, mid.Y, 6);
    }

    [Fact]
    public void PixelToData_RoundTrips()
    {
        var area = new Rect2D(30, 15, 300, 200);
        AxisTransform t = LinearTransform(area);

        Point2D data = t.PixelToData(123, 87);
        Point2D pixel = t.DataToPixel(data.X, data.Y);
        Assert.Equal(123, pixel.X, 6);
        Assert.Equal(87, pixel.Y, 6);
    }

    [Fact]
    public void InvertedXAxis_ReversesDirection()
    {
        var area = new Rect2D(0, 0, 100, 100);
        var t = new AxisTransform(
            area,
            LinearScaleTransform.Instance,
            new DataRange(0, 10),
            xInverted: true,
            LinearScaleTransform.Instance,
            new DataRange(0, 10),
            yInverted: false);

        Assert.Equal(100, t.DataToPixelX(0), 6);
        Assert.Equal(0, t.DataToPixelX(10), 6);
    }

    [Fact]
    public void LogAxis_MapsDecadesEvenly()
    {
        var area = new Rect2D(0, 0, 300, 100);
        var t = new AxisTransform(
            area,
            LogarithmicScaleTransform.Instance,
            new DataRange(1, 1000),
            xInverted: false,
            LinearScaleTransform.Instance,
            new DataRange(0, 1),
            yInverted: false);

        // Three decades across 300px -> 100px per decade.
        Assert.Equal(0, t.DataToPixelX(1), 4);
        Assert.Equal(100, t.DataToPixelX(10), 4);
        Assert.Equal(200, t.DataToPixelX(100), 4);
        Assert.Equal(300, t.DataToPixelX(1000), 4);
    }
}
