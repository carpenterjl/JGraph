using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave D: where a heatmap's cells land, what colour they take, and what is written in them.
/// The colour rules are the interesting part — one range for the chart, or one per column or row —
/// and so is the decision not to write a label that will not fit in the cell it belongs to.
/// </summary>
public class HeatmapLayoutTests
{
    private static double[,] Grid() => new double[,]
    {
        { 1, 2 },
        { 3, 4 },
    };

    [Fact]
    public void EachCellIsAUnitSquareAroundItsOwnIntegerPoint()
    {
        var heatmap = new HeatmapPlot(new double[3, 2]);

        Assert.Equal(3, heatmap.Rows);
        Assert.Equal(2, heatmap.Columns);
        Assert.Equal(new DataRange(-0.5, 1.5), heatmap.GetXDataBounds());
        Assert.Equal(new DataRange(-0.5, 2.5), heatmap.GetYDataBounds());
    }

    [Fact]
    public void TheColourRangeIsTheDataUntilItIsSetAndTheDataIgnoresWhatIsMissing()
    {
        var heatmap = new HeatmapPlot(new double[,] { { 1, double.NaN }, { 3, 4 } });
        Assert.Equal(new DataRange(1, 4), heatmap.EffectiveLimits());

        heatmap.ColorLimits = new DataRange(0, 8);
        Assert.Equal(new DataRange(0, 8), heatmap.EffectiveLimits());
        Assert.Equal(0.5, heatmap.Fraction(1, 1), 12);
    }

    [Fact]
    public void ColumnAndRowScalingMeasureEachSliceAgainstItself()
    {
        var heatmap = new HeatmapPlot(Grid());

        // Against the whole chart, the top-left cell is at the bottom of the range.
        Assert.Equal(0, heatmap.Fraction(0, 0), 12);

        // Against its own column — 1 and 3 — it is still lowest, but 2 in the other column is too.
        heatmap.ColorScaling = HeatmapScaling.ScaledColumns;
        Assert.Equal(0, heatmap.Fraction(0, 0), 12);
        Assert.Equal(0, heatmap.Fraction(0, 1), 12);

        // Against its own row — 1 and 2 — the cell beside it is now at the top.
        heatmap.ColorScaling = HeatmapScaling.ScaledRows;
        Assert.Equal(1, heatmap.Fraction(0, 1), 12);
    }

    [Fact]
    public void ALogarithmicScaleHasNoPlaceForZeroOrLess()
    {
        var heatmap = new HeatmapPlot(new double[,] { { 1, 0 }, { 10, 100 } })
        {
            ColorScaling = HeatmapScaling.Log,
            ColorLimits = new DataRange(1, 100),
        };

        Assert.Equal(0, heatmap.Fraction(0, 0), 12);
        Assert.Equal(0.5, heatmap.Fraction(1, 0), 12);
        Assert.Equal(1, heatmap.Fraction(1, 1), 12);

        // Zero is missing rather than clamped, which is why it takes the missing colour.
        Assert.True(double.IsNaN(heatmap.Fraction(0, 1)));
        Assert.Equal(heatmap.MissingDataColor, heatmap.ColorOf(0, 1));
    }

    [Fact]
    public void ACellSaysItsValueOrThatThereIsNoneToSay()
    {
        var heatmap = new HeatmapPlot(new double[,] { { 1.23456, double.NaN } });

        Assert.Equal("1.235", heatmap.CellLabel(0, 0));
        Assert.Equal("NaN", heatmap.CellLabel(0, 1));

        heatmap.CellLabelFormat = "0.0";
        heatmap.MissingDataLabel = "-";
        Assert.Equal("1.2", heatmap.CellLabel(0, 0));
        Assert.Equal("-", heatmap.CellLabel(0, 1));
    }

    [Fact]
    public void TheColumnsAndRowsAreNumberedFromOneUntilTheyAreNamed()
    {
        var heatmap = new HeatmapPlot(new double[2, 3]);
        Assert.Equal(new[] { "1", "2", "3" }, heatmap.ColumnLabels());
        Assert.Equal(new[] { "1", "2" }, heatmap.RowLabels());

        heatmap.XData = ["a", "b", "c"];
        Assert.Equal(new[] { "a", "b", "c" }, heatmap.ColumnLabels());
    }

    [Fact]
    public void ALabelIsWrittenOnlyWhenItFitsInTheCellItBelongsTo()
    {
        var heatmap = new HeatmapPlot(Grid());
        var context = new RecordingRenderContext(new Size2D(400, 400));

        // A cell one pixel across has no room for anything, so the grid draws but the text does not.
        ((IDrawable)heatmap).Render(context, StateAt(scale: 1));
        Assert.Equal(1, context.ImageCount);
        Assert.Empty(context.Texts);

        // Given a hundred pixels a cell, every value is written.
        var roomy = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)heatmap).Render(roomy, StateAt(scale: 100));
        Assert.Equal(new[] { "1", "2", "3", "4" }, roomy.Texts);
    }

    [Fact]
    public void TheCellTextStandsOutAgainstWhateverItIsWrittenOn()
    {
        var heatmap = new HeatmapPlot(Grid());
        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)heatmap).Render(context, StateAt(scale: 100));

        // Parula runs dark to light, so the first cell is written in white and the last in black.
        Assert.Equal(Colors.White, context.TextStyles[0].Color);
        Assert.Equal(Colors.Black, context.TextStyles[3].Color);

        // Naming a colour uses it everywhere, whatever the cell beneath it looks like.
        var fixedColor = new RecordingRenderContext(new Size2D(400, 400));
        heatmap.CellLabelColor = Colors.Red;
        ((IDrawable)heatmap).Render(fixedColor, StateAt(scale: 100));
        Assert.All(fixedColor.TextStyles, style => Assert.Equal(Colors.Red, style.Color));
    }

    [Fact]
    public void AClickNamesTheCellItLandedIn()
    {
        var heatmap = new HeatmapPlot(Grid());
        var mapper = new ScaledMapper(1);

        PlotHitResult? corner = heatmap.HitTest(new Point2D(0.2, 0.9), mapper, tolerancePixels: 5);
        Assert.NotNull(corner);
        Assert.Equal(new Point2D(0, 1), corner!.DataPoint);

        // Column-major, which is where the value sits in the matrix a script passed in.
        Assert.Equal(1, corner.PointIndex);
        Assert.Null(heatmap.HitTest(new Point2D(4, 0), mapper, tolerancePixels: 5));
    }

    [Fact]
    public void AddingAHeatmapPointsTheRulersAtItsCells()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        HeatmapPlot heatmap = axes.AddHeatmap(Grid(), ["left", "right"], ["top", "bottom"]);

        Assert.Same(heatmap, Assert.Single(axes.Plots));
        Assert.False(axes.FrameVisible);
        Assert.Equal(AxisScaleType.Category, axes.PrimaryXAxis.Scale);
        Assert.Equal(new[] { "left", "right" }, axes.PrimaryXAxis.Categories);

        // Row zero reads first, which on a ruler that counts upward means the ruler is turned over.
        Assert.Equal(new[] { "top", "bottom" }, axes.PrimaryYAxis.Categories);
        Assert.True(axes.PrimaryYAxis.Inverted);
    }

    private static RenderState StateAt(double scale) =>
        new(new ScaledMapper(scale), new Rect2D(0, 0, 400, 400), Colors.Blue);

    /// <summary>A mapper that only scales, so a cell can be given a size a label fits in or does not.</summary>
    private sealed class ScaledMapper(double scale) : ICoordinateMapper
    {
        public Rect2D PlotArea => new(0, 0, 400, 400);

        public Point2D DataToPixel(double x, double y) => new(x * scale, y * scale);

        public Point2D PixelToData(double px, double py) => new(px / scale, py / scale);
    }
}
