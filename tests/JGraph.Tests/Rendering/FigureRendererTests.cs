using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

public class FigureRendererTests
{
    private static FigureModel LineFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Title = "T";
        axes.AddLine(new double[] { 0, 1, 2, 3 }, new double[] { 0, 1, 4, 9 });
        return figure;
    }

    [Fact]
    public void Render_ClearsAndDrawsChromeAndLine()
    {
        FigureModel figure = LineFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(1, context.ClearCount);
        Assert.True(context.PolylineCount >= 1, "expected the line series to be drawn");
        Assert.True(context.RectangleCount >= 1, "expected the axes/frame rectangles");
        Assert.True(context.TextCount >= 1, "expected tick labels and title");
    }

    [Fact]
    public void Render_AutoScalesAxesFromData()
    {
        FigureModel figure = LineFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        AxisModel yAxis = figure.Axes[0].PrimaryYAxis;
        Assert.True(yAxis.Range.Min <= 0);
        Assert.True(yAxis.Range.Max >= 9);
    }

    [Fact]
    public void Render_PlotsAreClippedToPlotArea()
    {
        FigureModel figure = LineFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.True(context.MaxClipDepth >= 1, "plot content must be drawn inside a clip");
        Assert.Equal(0, context.ClipDepth); // balanced push/pop
    }

    [Fact]
    public void Render_EmptyFigureDoesNotThrow()
    {
        var figure = new FigureModel();
        var context = new RecordingRenderContext(new Size2D(200, 200));

        new FigureRenderer().Render(figure, context);

        Assert.Equal(1, context.ClearCount);
    }

    [Fact]
    public void Render_HeatmapDrawsImage()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddImage(new double[,] { { 0, 1, 2 }, { 3, 4, 5 } });
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(1, context.ImageCount);
    }

    [Fact]
    public void Render_CategoryAxisLabelsFromCategories()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddBar(new[] { "Alpha", "Beta", "Gamma" }, new double[] { 3, 5, 2 });
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        // Three category tick labels plus other chrome text must have been drawn.
        Assert.True(context.TextCount >= 3);
    }

    [Fact]
    public void Render_SubplotsProduceMultiplePlotAreas()
    {
        var figure = new FigureModel();
        AxesModel top = figure.AddSubplot(2, 1, 1);
        top.AddLine(new double[] { 0, 1, 2 }, new double[] { 0, 1, 2 });
        AxesModel bottom = figure.AddSubplot(2, 1, 2);
        bottom.AddLine(new double[] { 0, 1, 2 }, new double[] { 2, 1, 0 });
        var context = new RecordingRenderContext(new Size2D(400, 400));

        FigureRenderResult result = new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(2, result.Axes.Count);
        // The two subplot plot areas must not overlap vertically.
        Rect2D a = result.Axes[0].PlotArea;
        Rect2D b = result.Axes[1].PlotArea;
        Assert.True(a.Bottom <= b.Top + 1);
    }
}
