using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M55 wave A: the area band as an object — what it reports for auto-scaling, and how a stack of
/// them is built. The bounds are where the interesting behaviour is: a band always shows its floor
/// even when no sample reaches it, and a stacked band is measured from the one beneath it.
/// </summary>
public class AreaPlotTests
{
    [Fact]
    public void ABandAlwaysShowsItsFloorEvenWhenNoSampleReachesIt()
    {
        var band = new AreaPlot([1.0, 2, 3], [4.0, 5, 6]);

        DataRange y = band.GetYDataBounds();

        // Nothing in the data is anywhere near 0, but a filled band that floats would read as a
        // ribbon rather than as an area.
        Assert.Equal(0, y.Min);
        Assert.Equal(6, y.Max);
    }

    [Fact]
    public void ANamedBaseValueMovesBothTheFloorAndTheBoundsToIt()
    {
        var band = new AreaPlot([1.0, 2, 3], [4.0, 5, 6]) { BaseValue = 10 };

        DataRange y = band.GetYDataBounds();

        Assert.Equal(4, y.Min);
        Assert.Equal(10, y.Max);
        Assert.Equal(10, band.FloorAt(1));
    }

    [Fact]
    public void AGapInTheDataIsSkippedRatherThanCountedAsAValue()
    {
        var band = new AreaPlot([1.0, 2, 3], [4.0, double.NaN, 6]);

        DataRange y = band.GetYDataBounds();

        Assert.Equal(0, y.Min);
        Assert.Equal(6, y.Max);
    }

    [Fact]
    public void AStackedBandIsMeasuredFromTheOneBeneathItButKeepsItsOwnValues()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        IReadOnlyList<AreaPlot> bands = axes.AddStackedArea(
            [1.0, 2, 3],
            [[1.0, 2, 3], [10.0, 20, 30]]);

        Assert.Equal(2, bands.Count);

        // The first stands on the base value; the second stands on the first.
        Assert.Null(bands[0].LowerEdge);
        Assert.Equal([1.0, 2, 3], Assert.IsType<double[]>(bands[1].LowerEdge));

        // Its own data is still the column that was passed, which is what YData answers…
        Assert.Equal(20, bands[1].Data.GetY(1));

        // …while what gets drawn is that column added to the floor.
        Assert.Equal(22, bands[1].TopAt(1));
        Assert.Equal(33, bands[1].GetYDataBounds().Max);
    }

    [Fact]
    public void TheStackedBandsTogetherSpanFromTheFloorToTheTotal()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddStackedArea([1.0, 2], [[3.0, 4], [5.0, 6]]);

        DataRange y = axes.Plots
            .Select(plot => plot.GetYDataBounds())
            .Aggregate(DataRange.Empty, (all, one) => all.Union(one));

        Assert.Equal(0, y.Min);
        Assert.Equal(10, y.Max);
    }
}
