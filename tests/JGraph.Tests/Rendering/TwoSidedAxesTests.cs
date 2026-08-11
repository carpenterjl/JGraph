using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M54 wave D: an axes with a second Y ruler — what <c>yyaxis</c> makes. What is checked is that each
/// ruler fits only the data bound to it, that the second one is drawn outside the right edge with room
/// reserved for it, and that a one-ruler figure draws exactly as it always did.
/// </summary>
public class TwoSidedAxesTests
{
    /// <summary>Small numbers on the left, large ones on the right — the case yyaxis exists for.</summary>
    private static FigureModel TwoSidedFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 1, 2, 3 }, new double[] { 0, 1, 2 });

        AxisModel right = axes.UseYAxis(1);
        right.Label = "right";
        axes.AddLine(new double[] { 1, 2, 3 }, new double[] { 0, 500, 1000 });

        return figure;
    }

    [Fact]
    public void EachRulerFitsOnlyTheDataBoundToIt()
    {
        FigureModel figure = TwoSidedFigure();
        AxesModel axes = figure.Axes[0];

        figure.RecomputeDataBounds();

        Assert.True(axes.YAxes[0].Range.Max < 10, "the left ruler must not stretch to the right series");
        Assert.True(axes.YAxes[1].Range.Max >= 1000);
    }

    [Fact]
    public void APlotDrawnWhileASideIsActiveBelongsToThatSide()
    {
        FigureModel figure = TwoSidedFigure();
        AxesModel axes = figure.Axes[0];

        Assert.Equal(0, axes.Plots[0].YAxisIndex);
        Assert.Equal(1, axes.Plots[1].YAxisIndex);

        // Going back to the left binds the next plot there again.
        axes.UseYAxis(0);
        axes.AddLine(new double[] { 1, 2 }, new double[] { 3, 4 });
        Assert.Equal(0, axes.Plots[2].YAxisIndex);
    }

    [Fact]
    public void AskingForTheSameSideTwiceDoesNotAddASecondRuler()
    {
        var axes = new AxesModel();

        axes.UseYAxis(1);
        AxisModel again = axes.UseYAxis(1);

        Assert.Equal(2, axes.YAxes.Count);
        Assert.Same(axes.YAxes[1], again);
    }

    [Fact]
    public void TheSecondRulerIsDrawnOutsideTheRightEdgeWithItsOwnNumbers()
    {
        FigureModel figure = TwoSidedFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        // Its label is turned the other way from the left one and sits past the plot area, whose right
        // edge is where the widest left-hand content stops.
        int label = context.Texts.IndexOf("right");
        Assert.True(label >= 0, "the right ruler's label must be drawn");

        double labelX = context.TextPositions[label].X;
        Assert.True(labelX > 300, $"the right label should sit outside the plot area, but was at x={labelX}");
        Assert.True(labelX < 400, "and still inside the figure");

        // A tick label only the right ruler could have produced.
        Assert.Contains("1000", context.Texts);
    }

    [Fact]
    public void RoomIsReservedSoTheRightRulerDoesNotOverhangTheFigure()
    {
        var narrow = new FigureModel();
        AxesModel axes = narrow.AddAxes();
        axes.AddLine(new double[] { 1, 2 }, new double[] { 0, 1 });

        var before = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult oneRuler = new FigureRenderer().Render(narrow, before, Theme.Light);

        axes.UseYAxis(1).Label = "second";
        axes.AddLine(new double[] { 1, 2 }, new double[] { 0, 1000 });

        var after = new RecordingRenderContext(new Size2D(400, 300));
        FigureRenderResult twoRulers = new FigureRenderer().Render(narrow, after, Theme.Light);

        Assert.True(
            twoRulers.Axes[0].PlotArea.Right < oneRuler.Axes[0].PlotArea.Right,
            "the plot area has to give up the width the second ruler needs");
    }

    [Fact]
    public void EachRulerIsTintedLikeTheSeriesItMeasures()
    {
        FigureModel figure = TwoSidedFigure();
        var context = new RecordingRenderContext(new Size2D(400, 300));

        new FigureRenderer().Render(figure, context, Theme.Light);

        int label = context.Texts.IndexOf("right");
        Color rightInk = context.TextStyles[label].Color;

        Assert.Equal(Theme.Light.SeriesPalette[1], rightInk);
        Assert.NotEqual(figure.Axes[0].YAxes[1].LabelStyle.Color, rightInk);
    }

    [Fact]
    public void AOneRulerAxesIsDrawnInTheThemesOwnInk()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryYAxis.Label = "only";
        axes.AddLine(new double[] { 1, 2 }, new double[] { 0, 1 });

        var context = new RecordingRenderContext(new Size2D(400, 300));
        new FigureRenderer().Render(figure, context, Theme.Light);

        int label = context.Texts.IndexOf("only");
        Assert.Equal(axes.PrimaryYAxis.LabelStyle.Color, context.TextStyles[label].Color);
    }
}
