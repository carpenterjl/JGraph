using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M56 wave C: the one angular chart that needed an object of its own. What these check is the
/// counting, what a height means once it is counted, and that a wedge is drawn where its bin is —
/// the three things a reader of the chart is trusting.
/// </summary>
public class PolarHistogramTests
{
    private static double[] Edges(int bins)
    {
        var edges = new double[bins + 1];
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = System.Math.Tau * i / bins;
        }

        return edges;
    }

    [Fact]
    public void EachBinTakesItsLeftEdgeAndTheLastTakesBoth()
    {
        // Four quadrants, and a reading sitting exactly on each boundary — including the far end,
        // which is the one place the rule changes and the only way the counts add up to the sample.
        var plot = new PolarHistogramPlot(
            [0, System.Math.PI / 2, System.Math.PI, 3 * System.Math.PI / 2, System.Math.Tau],
            Edges(4));

        Assert.Equal([1.0, 1, 1, 2], plot.BinCounts);
        Assert.Equal(5, plot.BinCounts.Sum());
    }

    [Fact]
    public void AReadingOutsideEveryBinIsCountedNowhere()
    {
        var plot = new PolarHistogramPlot([-0.5, 0.5, 7.0, double.NaN], [0, 1, 2]);

        Assert.Equal([1.0, 0], plot.BinCounts);
    }

    [Fact]
    public void TheSixNormalizationsReadTheSameCountsSixWays()
    {
        var plot = new PolarHistogramPlot([0.5, 0.5, 0.5, 1.5], [0, 1, 2]);

        Assert.Equal([3.0, 1], plot.BinHeights);

        plot.Normalization = HistogramNormalization.Probability;
        Assert.Equal([0.75, 0.25], plot.BinHeights);

        plot.Normalization = HistogramNormalization.Cumulative;
        Assert.Equal([3.0, 4], plot.BinHeights);

        plot.Normalization = HistogramNormalization.CumulativeProbability;
        Assert.Equal([0.75, 1.0], plot.BinHeights);

        // Unit-wide bins, so the two densities are the count and the fraction over again — which is
        // exactly what makes them worth checking on bins of another width.
        plot.Normalization = HistogramNormalization.CountDensity;
        Assert.Equal([3.0, 1], plot.BinHeights);

        plot.Normalization = HistogramNormalization.Density;
        Assert.Equal([0.75, 0.25], plot.BinHeights);
    }

    [Fact]
    public void ADensityDividesByTheWidthOfTheBinItIsIn()
    {
        var plot = new PolarHistogramPlot([0.5, 1.5, 2.5, 3.5], [0, 1, 4])
        {
            Normalization = HistogramNormalization.CountDensity,
        };

        // One reading in a bin one wide, three in a bin three wide: the same density either side,
        // which is the whole reason the reading exists.
        Assert.Equal([1.0, 1], plot.BinHeights);
    }

    [Fact]
    public void MovingTheBinsCountsTheDataAgain()
    {
        var plot = new PolarHistogramPlot([0.5, 1.5, 2.5, 3.5], Edges(2));
        // Half a turn each: three readings fall in the first, and 3.5 is just past π.
        Assert.Equal([3.0, 1], plot.BinCounts);

        plot.NumBins = 4;
        Assert.Equal(4, plot.BinCounts.Length);
        Assert.Equal([2.0, 1, 1, 0], plot.BinCounts);

        plot.BinLimits = [0, 4];
        Assert.Equal([1.0, 1, 1, 1], plot.BinCounts);

        plot.BinWidth = 2;
        Assert.Equal([2.0, 2], plot.BinCounts);
    }

    [Fact]
    public void CountsGivenOutrightHaveNoDataBehindThemToRecount()
    {
        PolarHistogramPlot plot = PolarHistogramPlot.FromCounts([0, 1, 2], [3, 7]);

        Assert.Empty(plot.Data);
        Assert.Equal([3.0, 7], plot.BinCounts);

        // The fractions are of the counts, since there is no sample size to be had anywhere else.
        plot.Normalization = HistogramNormalization.Probability;
        Assert.Equal([0.3, 0.7], plot.BinHeights);
    }

    [Fact]
    public void TheChartReachesFromTheMiddleToTheTallestBin()
    {
        var plot = new PolarHistogramPlot([0.5, 0.5, 1.5], [0, 1, 2]);

        Assert.Equal(0, plot.GetYDataBounds().Min, 10);
        Assert.Equal(2, plot.GetYDataBounds().Max, 10);
        Assert.Equal(0, plot.GetXDataBounds().Min, 10);
        Assert.Equal(2, plot.GetXDataBounds().Max, 10);
    }

    [Fact]
    public void EveryBinWithAnythingInItIsDrawnAsOneWedge()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        axes.AddPolarHistogram([0.5, 0.5, 2.0], Edges(4));
        figure.RecomputeDataBounds();

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        // Two of the four quadrants hold something; the empty two draw nothing rather than a wedge
        // of zero length, which would be a stroke across the middle of the chart.
        Assert.Equal(2, context.PolygonCount);
    }

    [Fact]
    public void TheStairsStyleIsOneOutlineAndNoFilledWedges()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        PolarHistogramPlot plot = axes.AddPolarHistogram([0.5, 2.0, 4.0], Edges(4));
        plot.DisplayStyle = PolarHistogramDisplayStyle.Stairs;
        figure.RecomputeDataBounds();

        var context = new RecordingRenderContext(new Size2D(400, 400));
        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(0, context.PolygonCount);

        // The rim and the ring the r ruler draws are polylines too, so what is asserted is that the
        // outline joined them rather than that it is the only one.
        Assert.True(context.PolylineCount >= 2);
    }

    [Fact]
    public void AWedgeIsHitInsideItsAngleAndNotBeyondItsReach()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.MakePolar();
        PolarHistogramPlot plot = axes.AddPolarHistogram([0.1, 0.2, 0.3], Edges(4));
        figure.RecomputeDataBounds();

        var mapper = new JGraph.Maths.Transforms.PolarTransform(
            new Rect2D(0, 0, 200, 200),
            axes.RAxis.Range,
            axes.ThetaZeroLocation,
            axes.ThetaDirection);

        // Just inside the first quadrant at half the bar's height, and the same bearing past its end.
        Point2D inside = mapper.DataToPixel(0.2, plot.BinCounts[0] / 2);
        Point2D beyond = mapper.DataToPixel(System.Math.PI, plot.BinCounts[0] / 2);

        Assert.NotNull(plot.HitTest(inside, mapper, 5));
        Assert.Null(plot.HitTest(beyond, mapper, 5));
    }
}
