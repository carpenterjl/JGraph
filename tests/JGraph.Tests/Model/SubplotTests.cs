using JGraph.Core.Model;
using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Model;

public class SubplotTests
{
    [Fact]
    public void SubplotBounds_TopLeftCellStartsNearOrigin()
    {
        Rect2D cell = FigureModel.SubplotBounds(2, 2, 1, 1);
        // Cell 1 is the top-left quadrant; its gutter-inset origin is small but positive.
        Assert.InRange(cell.X, 0.0, 0.1);
        Assert.InRange(cell.Y, 0.0, 0.1);
        Assert.InRange(cell.Width, 0.35, 0.5);
        Assert.InRange(cell.Height, 0.35, 0.5);
    }

    [Fact]
    public void SubplotBounds_IndexingIsRowMajor()
    {
        // 2x2 grid: cell 2 is top-right, cell 3 is bottom-left.
        Rect2D topRight = FigureModel.SubplotBounds(2, 2, 2, 2);
        Rect2D bottomLeft = FigureModel.SubplotBounds(2, 2, 3, 3);

        Assert.True(topRight.X > 0.4);       // right column
        Assert.InRange(topRight.Y, 0.0, 0.1); // top row
        Assert.InRange(bottomLeft.X, 0.0, 0.1); // left column
        Assert.True(bottomLeft.Y > 0.4);        // bottom row
    }

    [Fact]
    public void SubplotBounds_SpanCoversMultipleCells()
    {
        // Top row of a 2x2 grid: cells 1..2 span the full width.
        Rect2D span = FigureModel.SubplotBounds(2, 2, 1, 2);
        Rect2D single = FigureModel.SubplotBounds(2, 2, 1, 1);
        Assert.True(span.Width > single.Width * 1.8);
        Assert.InRange(span.Height, single.Height - 0.01, single.Height + 0.01);
    }

    [Fact]
    public void AddSubplot_AddsAxesWithCellBounds()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddSubplot(3, 1, 2);
        Assert.Contains(axes, figure.Axes);
        Assert.Equal(FigureModel.SubplotBounds(3, 1, 2, 2), axes.NormalizedBounds);
    }

    [Fact]
    public void SubplotBounds_RejectsOutOfRangeIndex()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => FigureModel.SubplotBounds(2, 2, 5, 5));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => FigureModel.SubplotBounds(0, 2, 1, 1));
    }
}
