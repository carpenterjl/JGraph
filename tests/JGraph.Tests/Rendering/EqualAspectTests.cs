using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

public class EqualAspectTests
{
    [Fact]
    public void EqualAspect_MakesPlotAreaSquareForEqualRanges()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 10 }, new double[] { 0, 10 }); // equal-length X and Y ranges
        axes.EqualAspect = true;

        // A deliberately wide surface: without equal aspect the plot area would be far from square.
        var context = new RecordingRenderContext(new Size2D(800, 400));
        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        Rect2D plotArea = result.Axes[0].PlotArea;
        Assert.True(System.Math.Abs(plotArea.Width - plotArea.Height) < 1.0,
            $"expected a square plot area, got {plotArea.Width} x {plotArea.Height}");
    }

    [Fact]
    public void FrameVisibleFalse_DrawsOneFewerRectangle()
    {
        var withFrame = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(BuildFigure(frameVisible: true), withFrame, Theme.Light);

        var withoutFrame = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(BuildFigure(frameVisible: false), withoutFrame, Theme.Light);

        Assert.Equal(withFrame.RectangleCount - 1, withoutFrame.RectangleCount);
    }

    private static FigureModel BuildFigure(bool frameVisible)
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1, 2 }, new double[] { 0, 1, 2 });
        axes.FrameVisible = frameVisible;
        return figure;
    }
}
