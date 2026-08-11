using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Statistics;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave E: what a box chart summarizes, where its boxes stand, and what it draws.
/// The interesting parts are the whiskers stopping at observations rather than at their own reach,
/// several charts on one axes dividing the slot between them, and the quartile convention being the
/// same one the statistics toolbox uses.
/// </summary>
public class BoxChartLayoutTests
{
    [Fact]
    public void TheQuartilesAreTheOnesTheStatisticsToolboxWouldGive()
    {
        double[] sample = [2, 4, 4, 5, 7, 9, 11, 12, 20];
        BoxSummary summary = Summarize(sample);

        // Two implementations of the same convention would drift apart silently, so they are pinned
        // to each other: a script that draws a box and then asks prctile must get the same number.
        double[] expected = DescriptiveStatistics.Percentiles(sample, [25, 50, 75]);
        Assert.Equal(expected[0], summary.LowerQuartile, 12);
        Assert.Equal(expected[1], summary.Median, 12);
        Assert.Equal(expected[2], summary.UpperQuartile, 12);
    }

    [Fact]
    public void AWhiskerStopsAtAnObservationAndWhatItCannotReachIsAnOutlier()
    {
        // The quartiles here are 2.75 and 7.25, so the whiskers may reach 6.75 past them: 100 cannot
        // be reached, and the whisker stops at 8 rather than at the reach it was allowed.
        BoxSummary summary = Summarize([1, 2, 3, 4, 5, 6, 7, 8, 100]);

        Assert.Equal(1, summary.LowerWhisker, 12);
        Assert.Equal(8, summary.UpperWhisker, 12);
        Assert.Equal([100], summary.Outliers);
        Assert.Equal(9, summary.Count);
    }

    [Fact]
    public void NothingFiniteIsNothingToDrawRatherThanAFlatBox()
    {
        Assert.Null(Quartiles.Summarize([double.NaN, double.PositiveInfinity]));
        Assert.Null(Quartiles.Summarize([]));

        // A single observation is a box with no height, which is a real answer and not a degenerate one.
        BoxSummary one = Summarize([7]);
        Assert.Equal(7, one.Median, 12);
        Assert.Equal(0, one.InterquartileRange, 12);
    }

    [Fact]
    public void ObservationsAreCutIntoGroupsByTheValueBesideThem()
    {
        var chart = new BoxChartPlot([2, 1, 2, 1, double.NaN], [10, 1, 30, 3, 99]);
        IReadOnlyList<(double Position, BoxSummary Summary)> groups = chart.Groups();

        // Ascending by position, whatever order the observations arrived in, and an observation with
        // no group to fall in is left out rather than made into one of its own.
        Assert.Equal([1, 2], groups.Select(g => g.Position));
        Assert.Equal(2, groups[0].Summary.Median, 12);
        Assert.Equal(20, groups[1].Summary.Median, 12);
    }

    [Fact]
    public void WithNoGroupingEverythingIsOneBoxAtPositionOne()
    {
        var chart = new BoxChartPlot([4, 8, 6]);
        (double position, BoxSummary summary) = Assert.Single(chart.Groups());

        Assert.Equal(1, position, 12);
        Assert.Equal(6, summary.Median, 12);
    }

    [Fact]
    public void BoxChartsSharingAnAxesDivideTheSlotBetweenThem()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        BoxChartPlot first = axes.AddBoxChart(null, [1, 2, 3]);

        Assert.Equal((0.5, 0.0), first.SlotGeometry());

        // A second chart halves both boxes and moves them apart, and the first learns it from the
        // axes rather than being told — which is what makes hold on do the useful thing.
        BoxChartPlot second = axes.AddBoxChart(null, [4, 5, 6]);
        Assert.Equal((0.25, -0.125), first.SlotGeometry());
        Assert.Equal((0.25, 0.125), second.SlotGeometry());
    }

    [Fact]
    public void TheBoundsCoverTheBoxesAcrossAndEveryObservationAlong()
    {
        var chart = new BoxChartPlot([1, 1, 2, 2], [0, 10, 20, 30]);

        Assert.Equal(new DataRange(0.5, 2.5), chart.GetXDataBounds());
        Assert.Equal(new DataRange(0, 30), chart.GetYDataBounds());

        // Turned on its side the two swap, because the groups now run up the page.
        chart.Horizontal = true;
        Assert.Equal(new DataRange(0, 30), chart.GetXDataBounds());
        Assert.Equal(new DataRange(0.5, 2.5), chart.GetYDataBounds());
    }

    [Fact]
    public void EachGroupDrawsABoxAMedianAndTwoWhiskers()
    {
        var chart = new BoxChartPlot([1, 1, 2, 2], [1, 3, 10, 30]);
        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(context, State());

        Assert.Equal(2, context.PolygonCount);
        Assert.Equal([4, 4], context.PolygonSizes);

        // Per group: the median line and the two whiskers. No cap across the whisker ends — that is
        // the older boxplot's furniture, not this chart's.
        Assert.Equal(6, context.LineCount);
        Assert.Equal(0, context.TotalMarkerPoints);
    }

    [Fact]
    public void ANotchedBoxIsCutInAtTheMedian()
    {
        var chart = new BoxChartPlot([1, 2, 3, 4, 5]) { Notch = true };
        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(context, State());

        Assert.Equal([10], context.PolygonSizes);
    }

    [Fact]
    public void OutliersAreDrawnAsMarkersAndJitterMovesThemWithoutMovingTheirValue()
    {
        var chart = new BoxChartPlot([1, 2, 3, 4, 5, 6, 7, 8, 100, -100]);
        var plain = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(plain, State());

        Assert.Equal(2, plain.TotalMarkerPoints);
        Assert.All(plain.MarkerPoints, point => Assert.Equal(1, point.X, 12));

        // Jittered, the markers move sideways within the box and stay at the value they came from.
        chart.JitterOutliers = true;
        var spread = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(spread, State());

        Assert.Equal(2, spread.TotalMarkerPoints);
        for (int i = 0; i < spread.MarkerPoints.Count; i++)
        {
            Assert.Equal(plain.MarkerPoints[i].Y, spread.MarkerPoints[i].Y, 12);
            Assert.True(System.Math.Abs(spread.MarkerPoints[i].X - 1) <= 0.25);
        }

        // Drawing it again puts them in the same places, which a random nudge would not.
        var again = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(again, State());
        Assert.Equal(spread.MarkerPoints, again.MarkerPoints);
    }

    [Fact]
    public void TurnedOnItsSideTheBoxLiesAlongTheOtherDirection()
    {
        var chart = new BoxChartPlot([2, 4, 6]) { Horizontal = true };
        var context = new RecordingRenderContext(new Size2D(400, 400));
        ((IDrawable)chart).Render(context, State());

        // The median line now runs up and down at the median rather than across at it.
        (Point2D from, Point2D to, _) = context.Lines[0];
        Assert.Equal(4, from.X, 12);
        Assert.Equal(4, to.X, 12);
        Assert.NotEqual(from.Y, to.Y);
    }

    [Fact]
    public void AClickNamesTheGroupItLandedInAndTheMedianOfIt()
    {
        var chart = new BoxChartPlot([1, 1, 2, 2], [1, 3, 10, 30]);
        var mapper = new UnitMapper();

        PlotHitResult? hit = chart.HitTest(new Point2D(2, 20), mapper, tolerancePixels: 2);
        Assert.NotNull(hit);
        Assert.Equal(1, hit!.PointIndex);
        Assert.Equal(new Point2D(2, 20), hit.DataPoint);

        Assert.Null(chart.HitTest(new Point2D(8, 20), mapper, tolerancePixels: 2));
    }

    private static BoxSummary Summarize(double[] values) =>
        Quartiles.Summarize(values) ?? throw new InvalidOperationException("nothing to summarize");

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
