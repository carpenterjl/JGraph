using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

public class ImagePlotTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(.5, 128)]
    [InlineData(2, 255)]
    public void ImagePlaneUsesScalarAlphaWithoutChangingStoredValue(double alpha, int expectedAlpha)
    {
        var image = new ImagePlot(new double[,] { { 1, 2 }, { 3, 4 } })
        {
            ScalarAlpha = alpha,
            AlphaDataMapping = AlphaMapping.None,
            XData = [1, 2], YData = [1, 2],
        };
        var area = new Rect2D(0, 0, 200, 200);
        var context = new RecordingRenderContext(new Size2D(200, 200));
        var projection = new JGraph.Maths.Transforms.Projection3D(
            new DataRange(0, 3), new DataRange(0, 3), new DataRange(0, 10), -37.5, 30, area);
        image.Render3D(context, projection, new RenderState(new IdentityMapper(area), area, Colors.Blue));
        Assert.Equal(alpha, image.ScalarAlpha);
        Assert.Equal(new DataRange(0, 0), image.GetZDataBounds());
        Assert.Equal(24, context.TotalTriangleVertices);
        Assert.All(context.TriangleColors, color => Assert.Equal(expectedAlpha, (int)(color >> 24)));
    }

    [Fact]
    public void HeatmapProvidesColorbarScaleAndDrawsGradient()
    {
        var axes = new JGraph.Core.Model.AxesModel();
        var image = axes.AddImage(Field());
        axes.Colorbar.Visible = true;
        Assert.Equal((0.0, 5.0), ((IColorMapped)image).ColorRange);
        var context = new RecordingRenderContext(new Size2D(400, 300));
        Assert.True(ColorbarRenderer.MeasureReservedWidth(axes, context) > 0);
        ColorbarRenderer.Draw(context, axes, new Rect2D(30, 30, 200, 200),
            Theme.Light);
        Assert.Equal(1, context.ImageCount);
        Assert.True(context.TextCount > 0);
        image.AutoScaleColor = false;
        image.ColorMin = -2; image.ColorMax = 8;
        Assert.Equal((-2.0, 8.0), ((IColorMapped)image).ColorRange);
    }

    private static double[,] Field() => new double[,]
    {
        { 0, 1, 2 },
        { 3, 4, 5 },
    };

    [Fact]
    public void DefaultExtentsSpanCellGrid()
    {
        var image = new ImagePlot(Field());
        Assert.Equal(2, image.Rows);
        Assert.Equal(3, image.Columns);
        Assert.Equal(new DataRange(0, 3), image.GetXDataBounds());
        Assert.Equal(new DataRange(0, 2), image.GetYDataBounds());
    }

    [Fact]
    public void CustomExtentsDriveBounds()
    {
        var image = new ImagePlot(Field())
        {
            XExtent = new DataRange(-1, 1),
            YExtent = new DataRange(10, 20),
        };
        Assert.Equal(new DataRange(-1, 1), image.GetXDataBounds());
        Assert.Equal(new DataRange(10, 20), image.GetYDataBounds());
    }

    [Fact]
    public void Render_DrawsImageIntoExtentRectangle()
    {
        var image = new ImagePlot(Field());
        var ctx = new RecordingRenderContext(new Size2D(100, 100));
        var state = new RenderState(new IdentityMapper(new Rect2D(0, 0, 100, 100)), new Rect2D(0, 0, 100, 100), Colors.Blue);

        ((IDrawable)image).Render(ctx, state);

        Assert.Equal(1, ctx.ImageCount);
        // Top-left corner is data (0, 2), bottom-right is (3, 0); under identity that is rect (0,0,3,2).
        Assert.Equal(new Rect2D(0, 0, 3, 2), ctx.LastImageDestination);
    }

    [Fact]
    public void ReplacingValuesUpdatesShape()
    {
        var image = new ImagePlot(Field());
        image.Values = new double[3, 2];
        Assert.Equal(3, image.Rows);
        Assert.Equal(2, image.Columns);
    }
}
