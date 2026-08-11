using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave B: where a bar stands and how tall it is, once a chart can hold several series. The
/// arrangement is the interesting part — grouped series share one slot, stacked ones share a floor,
/// and both keep the values the caller passed.
/// </summary>
public class BarLayoutTests
{
    [Fact]
    public void AGroupedSeriesTakesItsShareOfTheSlotAndStandsBesideTheOthers()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        IReadOnlyList<BarPlot> bars = axes.AddBar(
            [1.0, 2, 3], [[1.0, 2, 3], [4.0, 5, 6]], stacked: false);

        Assert.Equal(2, bars.Count);

        // The slot is 0.8 of the unit spacing, so each of the two bars is 0.4 wide and their
        // centers sit a bar-width apart, straddling the position.
        Assert.Equal(2 - 0.2, bars[0].CenterAt(1), 12);
        Assert.Equal(2 + 0.2, bars[1].CenterAt(1), 12);

        // Grouping never changes what the bars are worth.
        Assert.Equal(5, bars[1].TopAt(1));
        Assert.Equal(0, bars[1].FloorAt(1));
    }

    [Fact]
    public void AnUngroupedSeriesIsCenteredOnItsPosition()
    {
        var bar = new BarPlot([1.0, 2, 3], [4.0, 5, 6]);

        Assert.Equal(2, bar.CenterAt(1));
        Assert.Equal(new DataRange(1 - 0.4, 3 + 0.4), bar.GetXDataBounds());
    }

    [Fact]
    public void AStackedSeriesStandsOnTheRunningTotalButKeepsItsOwnValues()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        IReadOnlyList<BarPlot> bars = axes.AddBar(
            [1.0, 2], [[1.0, 2], [10.0, 20]], stacked: true);

        Assert.Null(bars[0].LowerEdge);
        Assert.Equal([1.0, 2], Assert.IsType<double[]>(bars[1].LowerEdge));

        // YData still answers the column that was passed…
        Assert.Equal(20, bars[1].Data.GetY(1));

        // …while the bar is drawn from the top of the one beneath it.
        Assert.Equal(2, bars[1].FloorAt(1));
        Assert.Equal(22, bars[1].TopAt(1));
        Assert.Equal(22, bars[1].GetYDataBounds().Max);

        // Stacked series are not offset from one another — they share the whole slot.
        Assert.Equal(bars[0].CenterAt(0), bars[1].CenterAt(0));
    }

    [Fact]
    public void TheBarsAlwaysReachTheirBaselineEvenWhenNoValueDoes()
    {
        var bar = new BarPlot([1.0, 2], [5.0, 6]) { Baseline = 0 };

        Assert.Equal(0, bar.GetYDataBounds().Min);
        Assert.Equal(6, bar.GetYDataBounds().Max);
    }

    [Fact]
    public void AHalfSlotOffsetStartsTheBarsAtTheirPositionInsteadOfCenteringThem()
    {
        // This is what the legacy histc style means: touching bars, left-aligned.
        var bar = new BarPlot([1.0, 2, 3], [4.0, 5, 6])
        {
            BarWidthFraction = 1.0,
            PositionOffset = 0.5,
        };

        Assert.Equal(2.5, bar.CenterAt(1));
        Assert.Equal(new DataRange(1, 4), bar.GetXDataBounds());
    }

    [Fact]
    public void AHorizontalChartSwapsWhichAxisCarriesTheValues()
    {
        var bar = new BarPlot([1.0, 2, 3], [4.0, 5, 6]) { Horizontal = true };

        Assert.Equal(new DataRange(0, 6), bar.GetXDataBounds());
        Assert.Equal(new DataRange(1 - 0.4, 3 + 0.4), bar.GetYDataBounds());
    }
}

/// <summary>
/// M55 wave B: the stairstep path. One builder serves both the renderer and
/// <c>[xb, yb] = stairs(...)</c>, so what a script measures is exactly what gets drawn.
/// </summary>
public class StairStepsTests
{
    [Fact]
    public void APostStepHoldsEachValueForwardToTheNextSample()
    {
        (double[] x, double[] y) = StairSteps.Build([1.0, 2, 3], [4.0, 5, 6], StepMode.Post);

        // Two points per sample, and the last sample has no tread to stand on — which is why its
        // pair repeats the final x rather than inventing a step beyond the data.
        Assert.Equal([1.0, 2, 2, 3, 3, 3], x);
        Assert.Equal([4.0, 4, 5, 5, 6, 6], y);
    }

    [Fact]
    public void APreStepHoldsEachValueBackToTheSampleBeforeIt()
    {
        (double[] x, double[] y) = StairSteps.Build([1.0, 2, 3], [4.0, 5, 6], StepMode.Pre);

        Assert.Equal([1.0, 1, 2, 2, 3, 3], x);
        Assert.Equal([4.0, 4, 4, 5, 5, 6], y);
    }

    [Fact]
    public void AMidStepChangesHalfwayBetweenNeighbours()
    {
        (double[] x, double[] y) = StairSteps.Build([1.0, 2, 4], [4.0, 5, 6], StepMode.Mid);

        Assert.Equal([1.0, 1.5, 1.5, 3, 3, 4], x);
        Assert.Equal([4.0, 4, 5, 5, 6, 6], y);
    }

    [Fact]
    public void SteppingChangesThePathAndNothingElseAboutTheSeries()
    {
        var line = new LinePlot([1.0, 2, 3], [4.0, 5, 6]) { Steps = StepMode.Post };

        Assert.Equal(new DataRange(1, 3), line.GetXDataBounds());
        Assert.Equal(new DataRange(4, 6), line.GetYDataBounds());
        Assert.Equal(3, line.Data.Count);
    }
}
