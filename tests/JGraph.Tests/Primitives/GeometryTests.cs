using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Primitives;

public class GeometryTests
{
    [Fact]
    public void Rect_Deflate_ShrinksBySides()
    {
        var rect = new Rect2D(0, 0, 100, 80);
        Rect2D inner = rect.Deflate(new Thickness(10, 5, 15, 20));
        Assert.Equal(10, inner.X);
        Assert.Equal(5, inner.Y);
        Assert.Equal(75, inner.Width);
        Assert.Equal(55, inner.Height);
    }

    [Fact]
    public void Rect_Deflate_ClampsToZero()
    {
        var rect = new Rect2D(0, 0, 10, 10);
        Rect2D inner = rect.Deflate(new Thickness(20));
        Assert.Equal(0, inner.Width);
        Assert.Equal(0, inner.Height);
    }

    [Fact]
    public void Rect_FromCorners_NormalizesOrder()
    {
        Rect2D rect = Rect2D.FromCorners(new Point2D(10, 8), new Point2D(2, 3));
        Assert.Equal(2, rect.X);
        Assert.Equal(3, rect.Y);
        Assert.Equal(8, rect.Width);
        Assert.Equal(5, rect.Height);
    }

    [Fact]
    public void Rect_Contains_Point()
    {
        var rect = new Rect2D(0, 0, 10, 10);
        Assert.True(rect.Contains(new Point2D(5, 5)));
        Assert.False(rect.Contains(new Point2D(11, 5)));
    }
}
