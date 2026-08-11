using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave F: how a bubble's data value becomes a diameter, who decides the scale, and what a
/// bubble legend says. The interesting parts are the mapping being linear in area rather than in
/// diameter, the limits being read off every bubble chart in the axes, and the same scale serving
/// both the chart and the legend that explains it.
/// </summary>
public class BubbleChartLayoutTests
{
    [Fact]
    public void ABubbleTwiceTheValueCoversTwiceThePage()
    {
        // Linear in area, which is the only reading that does not mislead: the diameter at the middle
        // of the range is the root-mean-square of the ends, not their average.
        var scale = new BubbleScale(new DataRange(0, 100), new DataRange(10, 30));

        Assert.Equal(10, scale.DiameterFor(0), 12);
        Assert.Equal(30, scale.DiameterFor(100), 12);
        Assert.Equal(System.Math.Sqrt((100 + 900) / 2.0), scale.DiameterFor(50), 12);

        // Areas, not diameters, are what a reader compares — so they are what has to be linear.
        double AreaAt(double value) => System.Math.PI * System.Math.Pow(scale.DiameterFor(value) / 2, 2);
        Assert.Equal(AreaAt(25) - AreaAt(0), AreaAt(50) - AreaAt(25), 9);
    }

    [Fact]
    public void ValuesPastTheLimitsFlattenRatherThanVanish()
    {
        var scale = new BubbleScale(new DataRange(10, 20), new DataRange(4, 24));

        Assert.Equal(4, scale.DiameterFor(-5), 12);
        Assert.Equal(24, scale.DiameterFor(1000), 12);

        // One distinct size is neither the biggest nor the smallest thing in the data, so it is drawn
        // halfway up the range rather than made loud or timid by an accident of scaling.
        var flat = new BubbleScale(new DataRange(7, 7), new DataRange(10, 30));
        Assert.Equal(System.Math.Sqrt((100 + 900) / 2.0), flat.DiameterFor(7), 12);
    }

    [Fact]
    public void TheAxesTakesItsLimitsFromEveryBubbleChartDrawnInIt()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddBubbleChart([1, 2], [1, 2], [5, 10]);

        Assert.Equal(new DataRange(5, 10), axes.ResolveBubbleLimits());

        // A second chart widens the scale for both, which is what makes two charts comparable — and
        // neither of them was told about the other.
        ScatterPlot second = axes.AddBubbleChart([3, 4], [3, 4], [2, 40]);
        Assert.Equal(new DataRange(2, 40), axes.ResolveBubbleLimits());
        Assert.Equal(axes.BubbleScale.DiameterFor(40), second.DiameterAt(1), 12);

        // Fixed limits stop the data moving the scale at all.
        axes.BubbleSizeLimits = new DataRange(0, 100);
        Assert.Equal(new DataRange(0, 100), axes.ResolveBubbleLimits());
    }

    [Fact]
    public void APlainScatterReadsTheSameArrayAsMarkerAreas()
    {
        // The one difference between scatter(x, y, sz) and bubblechart(x, y, sz): the same numbers,
        // read as areas in points squared rather than as values against a scale.
        var scatter = new ScatterPlot([1, 2], [1, 2]) { SizeData = [36, 100] };
        Assert.Equal(6, scatter.DiameterAt(0), 12);
        Assert.Equal(10, scatter.DiameterAt(1), 12);

        var bubbles = new ScatterPlot([1, 2], [1, 2]) { BubbleSizing = true, SizeData = [36, 100] };
        Assert.Equal(BubbleScale.DefaultSizeRange.Min, bubbles.DiameterAt(0), 12);
        Assert.Equal(BubbleScale.DefaultSizeRange.Max, bubbles.DiameterAt(1), 12);
    }

    [Fact]
    public void EachBubbleIsDrawnAtItsOwnSizeAndAPlainScatterStillGoesOutAsOneCall()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        ScatterPlot chart = axes.AddBubbleChart([1, 2, 3], [1, 1, 1], [0, 50, 100]);

        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(context, State());

        Assert.Equal(3, context.MarkerBatchCount);
        Assert.Equal(
            [chart.DiameterAt(0), chart.DiameterAt(1), chart.DiameterAt(2)],
            context.MarkerStyles.Select(m => m.Size));

        // A scatter with no per-point channel is still one call, which is what keeps a big cloud cheap.
        var plain = new ScatterPlot([1, 2, 3], [1, 1, 1]);
        var single = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)plain).Render(single, State());
        Assert.Equal(1, single.MarkerBatchCount);
        Assert.Equal(3, single.TotalMarkerPoints);
    }

    [Fact]
    public void AValuePerBubbleColorsThemThroughTheColormap()
    {
        var chart = new ScatterPlot([1, 2, 3], [1, 1, 1]) { ColorData = [0, 5, 10] };

        Assert.True(((IColorMapped)chart).HasMappedData);
        Assert.Equal((0, 10), ((IColorMapped)chart).ColorRange);

        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(context, State());

        // Three markers in three colours, the ends being the ends of the colormap.
        Assert.Equal(3, context.MarkerStyles.Count);
        Assert.Equal(chart.Colormap.Sample(0, 0, 10), context.MarkerStyles[0].Fill);
        Assert.Equal(chart.Colormap.Sample(10, 0, 10), context.MarkerStyles[2].Fill);
        Assert.NotEqual(context.MarkerStyles[0].Fill, context.MarkerStyles[1].Fill);
    }

    [Fact]
    public void SizeDataHasToCountTheBubblesItSizes()
    {
        var chart = new ScatterPlot([1, 2, 3], [1, 1, 1]);
        ArgumentException error = Assert.Throws<ArgumentException>(() => chart.SizeData = [1, 2]);
        Assert.Contains("one entry per point (3)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClickInsideALargeBubbleFindsIt()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        ScatterPlot chart = axes.AddBubbleChart([10, 100], [10, 10], [1, 100]);

        // Well inside the big bubble but nowhere near its centre: the pick radius has to grow with the
        // bubble, or a click in the middle of one lands on nothing.
        PlotHitResult? hit = chart.HitTest(new Point2D(115, 10), new UnitMapper(), tolerancePixels: 2);
        Assert.NotNull(hit);
        Assert.Equal(1, hit!.PointIndex);
    }

    [Fact]
    public void TheLegendShowsValuesSpreadAcrossTheScaleItLegends()
    {
        var legend = new BubbleLegendModel();
        var scale = new BubbleScale(new DataRange(0, 90), BubbleScale.DefaultSizeRange);

        Assert.Equal([0, 45, 90], legend.ValuesFor(scale));

        legend.NumBubbles = 4;
        Assert.Equal([0, 30, 60, 90], legend.ValuesFor(scale));

        // Two is the smallest legend that says anything and six is where bubbles start crowding, so
        // anything outside that is pulled back rather than drawn badly.
        legend.NumBubbles = 99;
        Assert.Equal(6, legend.NumBubbles);
        legend.NumBubbles = 0;
        Assert.Equal(2, legend.NumBubbles);
    }

    [Fact]
    public void TheLegendDrawsTheSameSizesTheChartDid()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddBubbleChart([1, 2, 3], [1, 2, 3], [0, 50, 100]);
        axes.BubbleLegend.Visible = true;

        var context = new RecordingRenderContext(new Size2D(500, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        // Three chart bubbles and three legend bubbles, at the same three diameters.
        double[] drawn = [.. context.MarkerStyles.Select(m => m.Size)];
        Assert.Equal(6, drawn.Length);
        Assert.Equal(drawn[..3], drawn[3..]);

        // And the values under them are written out.
        Assert.Contains("0", context.Texts);
        Assert.Contains("50", context.Texts);
        Assert.Contains("100", context.Texts);
    }

    [Fact]
    public void ALegendCanBeTitledAndCanLabelOnlyItsEnds()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddBubbleChart([1, 2, 3], [1, 2, 3], [0, 50, 100]);
        axes.BubbleLegend.Visible = true;
        axes.BubbleLegend.Title = "Population";
        axes.BubbleLegend.LimitLabels = true;

        var context = new RecordingRenderContext(new Size2D(500, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Contains("Population", context.Texts);
        Assert.Contains("0", context.Texts);
        Assert.Contains("100", context.Texts);
        Assert.DoesNotContain("50", context.Texts);
    }

    [Fact]
    public void TheThreeArrangementsPutTheirBubblesInDifferentPlaces()
    {
        Point2D[] Centers(BubbleLegendStyle style)
        {
            var figure = new FigureModel();
            AxesModel axes = figure.AddAxes();
            axes.AddBubbleChart([1, 2, 3], [1, 2, 3], [0, 50, 100]);
            axes.BubbleLegend.Visible = true;
            axes.BubbleLegend.Style = style;

            var context = new RecordingRenderContext(new Size2D(500, 400));
            new FigureRenderer().Render(figure, context, Theme.Light);

            // The chart drew the first three; the legend drew the rest.
            return [.. context.MarkerPoints.Skip(3)];
        }

        Point2D[] stacked = Centers(BubbleLegendStyle.Vertical);
        Assert.Equal(stacked[0].X, stacked[2].X, 9);
        Assert.True(stacked[2].Y > stacked[0].Y);

        Point2D[] across = Centers(BubbleLegendStyle.Horizontal);
        Assert.True(across[2].X > across[0].X);

        // Nested circles share a bottom edge and a centre line, and the largest is drawn first.
        Point2D[] nested = Centers(BubbleLegendStyle.Telescopic);
        Assert.Equal(nested[0].X, nested[2].X, 9);
        Assert.True(nested[2].Y > nested[0].Y);
    }

    private static RenderState State() =>
        new(new UnitMapper(), new Rect2D(0, 0, 400, 400), Colors.Blue);

    /// <summary>A mapper that leaves data coordinates alone, so pixels read as the values they came from.</summary>
    private sealed class UnitMapper : ICoordinateMapper
    {
        public Rect2D PlotArea => new(0, 0, 400, 400);

        public Point2D DataToPixel(double x, double y) => new(x, y);

        public Point2D PixelToData(double px, double py) => new(px, py);
    }
}
