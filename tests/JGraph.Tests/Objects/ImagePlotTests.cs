using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

public class ImagePlotTests
{
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
