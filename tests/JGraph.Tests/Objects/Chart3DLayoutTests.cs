using JGraph.Core.Primitives;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M57 wave C: the geometry the three-dimensional charts are made of. The boxes of a bar chart and
/// the extent of a raised pie are arithmetic, so they are checked here rather than through a picture
/// — a rendered chart can only be looked at, and these can be measured.
/// </summary>
public class Chart3DLayoutTests
{
    [Fact]
    public void ABarStandsInItsOwnSlotAndReachesItsHeight()
    {
        var bars = new Bar3DPlot(new double[,] { { 5, 7 }, { 1, 2 } });

        // Detached is the default: a box per entry, at the column number along X and the row number
        // along Y, filling four fifths of its slot in both directions.
        IReadOnlyList<Bar3DBox> boxes = bars.Boxes();
        Assert.Equal(4, boxes.Count);

        Bar3DBox first = boxes[0];
        Assert.Equal((0, 0), (first.Row, first.Column));
        Assert.Equal(0.6, first.XMin, 12);
        Assert.Equal(1.4, first.XMax, 12);
        Assert.Equal(0.6, first.YMin, 12);
        Assert.Equal(1.4, first.YMax, 12);
        Assert.Equal(0, first.ZMin, 12);
        Assert.Equal(5, first.ZMax, 12);

        // The second column stands one step further along X, at the same depth.
        Assert.Equal(1.6, boxes[1].XMin, 12);
        Assert.Equal(0.6, boxes[1].YMin, 12);
        Assert.Equal(7, boxes[1].ZMax, 12);
    }

    [Fact]
    public void AHorizontalBarLiesAlongXWithTheColumnsStackedUpInstead()
    {
        var bars = new Bar3DPlot(new double[,] { { 3 } }) { Horizontal = true };

        Bar3DBox box = Assert.Single(bars.Boxes());
        Assert.Equal(0, box.XMin, 12);
        Assert.Equal(3, box.XMax, 12);
        Assert.Equal(0.6, box.YMin, 12);
        Assert.Equal(1.4, box.YMax, 12);
        Assert.Equal(0.6, box.ZMin, 12);
        Assert.Equal(1.4, box.ZMax, 12);
    }

    [Fact]
    public void GroupedWidensTheBarsUntilTheyTouchAndStackedPutsThemOnTopOfEachOther()
    {
        var grouped = new Bar3DPlot(new double[,] { { 1, 2 } }) { Style = Bar3DStyle.Grouped };
        IReadOnlyList<Bar3DBox> side = grouped.Boxes();
        Assert.Equal(1.5, side[0].XMax, 12);
        Assert.Equal(1.5, side[1].XMin, 12);

        // Stacked is one bar per row: the second column starts where the first stopped, and both
        // stand in the single slot at x = 1.
        var stacked = new Bar3DPlot(new double[,] { { 1, 2 } }) { Style = Bar3DStyle.Stacked };
        IReadOnlyList<Bar3DBox> tower = stacked.Boxes();
        Assert.Equal(2, tower.Count);
        Assert.Equal((0.0, 1.0), (tower[0].ZMin, tower[0].ZMax));
        Assert.Equal((1.0, 3.0), (tower[1].ZMin, tower[1].ZMax));
        Assert.All(tower, box => Assert.Equal(0.6, box.XMin, 12));
    }

    [Fact]
    public void ARowPositionMovesTheWholeRowAndTheBoundsFollow()
    {
        var bars = new Bar3DPlot(new double[,] { { 4 }, { 6 } }) { RowPositions = [10, 20] };

        Assert.Equal(9.6, bars.GetYDataBounds().Min, 12);
        Assert.Equal(20.4, bars.GetYDataBounds().Max, 12);
        Assert.Equal(0, bars.GetZDataBounds().Min, 12);
        Assert.Equal(6, bars.GetZDataBounds().Max, 12);
    }

    [Fact]
    public void ARowPositionPerRowIsRequired()
    {
        var bars = new Bar3DPlot(new double[,] { { 4 }, { 6 } });
        ArgumentException failure = Assert.Throws<ArgumentException>(() => bars.RowPositions = [1]);
        Assert.Contains("one row position per row", failure.Message);
    }

    [Fact]
    public void ABarWithNoFiniteHeightIsLeftOutRatherThanDrawnFlat()
    {
        var bars = new Bar3DPlot(new double[,] { { 1, double.NaN } });

        Bar3DBox box = Assert.Single(bars.Boxes());
        Assert.Equal(0, box.Column);
    }

    [Fact]
    public void ARaisedPieDividesTheCircleTheSameWayAFlatOneDoes()
    {
        double[] values = [1, 2, 5];
        var flat = new PiePlot(values);
        var raised = new Pie3DPlot(values);

        Assert.Equal(
            flat.Slices().Select(s => (s.Start, s.Sweep, s.Fraction)),
            raised.Slices().Select(s => (s.Start, s.Sweep, s.Fraction)));
        Assert.Equal(flat.LabelOf(0, 0.125), raised.LabelOf(0, 0.125));
    }

    [Fact]
    public void ARaisedPieIsAsThickAsItsHeightAndAsWideAsItsLabels()
    {
        var pie = new Pie3DPlot([1, 1]) { Height = 0.4 };

        Assert.Equal(new DataRange(0, 0.4), pie.GetZDataBounds());

        // The labels sit at 1.2 radii and are given room past that, exactly as the flat pie does.
        Assert.Equal(-1.4, pie.GetXDataBounds().Min, 12);
        Assert.Equal(1.4, pie.GetYDataBounds().Max, 12);

        pie.ShowLabels = false;
        Assert.Equal(-1.0, pie.GetXDataBounds().Min, 12);
    }

    [Fact]
    public void AStemReachesItsBaselineWhicheverSideOfItTheSampleIs()
    {
        var stems = new Stem3DPlot([1, 2], [3, 4], [-5, 7]) { Baseline = 2 };

        Assert.Equal(-5, stems.GetZDataBounds().Min, 12);
        Assert.Equal(7, stems.GetZDataBounds().Max, 12);

        // The baseline is part of the reach even when every sample is on one side of it.
        var above = new Stem3DPlot([1], [1], [9]) { Baseline = 4 };
        Assert.Equal(4, above.GetZDataBounds().Min, 12);
    }

    [Fact]
    public void AStemNeedsTheSameNumberOfEachCoordinate()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new Stem3DPlot([1, 2], [3], [4, 5]));
        Assert.Contains("same length", failure.Message);
    }
}
