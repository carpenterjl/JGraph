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
    public void FrameVisibleFalse_KeepsTheRulerEdgesAndDropsTheFarOnes()
    {
        // MATLAB's box off keeps the two edges the rulers sit on; Box adds the far pair. The frame
        // stopped being one rectangle in M73 so that each ruler's color can reach its own line.
        FigureModel framedFigure = BuildFigure(frameVisible: true);
        framedFigure.Axes[0].Grid.ShowMajor = false;
        var withFrame = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult framed = new FigureRenderer().Render(framedFigure, withFrame, Theme.Light);

        FigureModel openFigure = BuildFigure(frameVisible: false);
        openFigure.Axes[0].Grid.ShowMajor = false;
        var withoutFrame = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult open = new FigureRenderer().Render(openFigure, withoutFrame, Theme.Light);

        static bool AtEdge(double value, double a, double b) =>
            System.Math.Abs(value - a) < 0.001 || System.Math.Abs(value - b) < 0.001;

        static int EdgeLines(RecordingRenderContext context, Rect2D area) =>
            context.Lines.Count(l =>
                (System.Math.Abs(l.From.Y - l.To.Y) < 0.001
                    && AtEdge(l.From.Y, area.Top, area.Bottom)
                    && System.Math.Abs(l.From.X - area.Left) < 0.001
                    && System.Math.Abs(l.To.X - area.Right) < 0.001)
                || (System.Math.Abs(l.From.X - l.To.X) < 0.001
                    && AtEdge(l.From.X, area.Left, area.Right)
                    && System.Math.Abs(l.From.Y - area.Top) < 0.001
                    && System.Math.Abs(l.To.Y - area.Bottom) < 0.001));

        Assert.Equal(4, EdgeLines(withFrame, framed.Axes[0].PlotArea));
        Assert.Equal(2, EdgeLines(withoutFrame, open.Axes[0].PlotArea));
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
