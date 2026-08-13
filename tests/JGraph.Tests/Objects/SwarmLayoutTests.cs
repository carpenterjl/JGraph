using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Maths;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M57 wave E: where a swarm chart puts its markers, and what a chart in space does with sizes read
/// as values. Both are properties on the scatter objects, so these are tests of a scatter that has
/// been asked to spread — there is no swarm object to test.
/// </summary>
public class SwarmLayoutTests
{
    /// <summary>Two columns of readings: four at x = 1 and six at x = 2.</summary>
    private static readonly double[] Xs = [1, 1, 1, 1, 2, 2, 2, 2, 2, 2];
    private static readonly double[] Ys = [1, 2, 3, 4, 1, 1, 2, 2, 3, 4];

    private static ScatterPlot Columns() =>
        new([.. Xs], [.. Ys]) { XJitter = JitterStyle.Density };

    [Fact]
    public void NothingIsSpreadUntilAJitterIsAskedFor()
    {
        var plot = new ScatterPlot([.. Xs], [.. Ys]);

        Assert.Equal(JitterStyle.None, plot.XJitter);
        Assert.All(plot.XOffsets, offset => Assert.Equal(0, offset));
        Assert.All(plot.YOffsets, offset => Assert.Equal(0, offset));
    }

    [Fact]
    public void TheSpreadStaysInsideTheWidthItWasGiven()
    {
        ScatterPlot plot = Columns();
        plot.XJitterWidth = 0.4;

        Assert.All(plot.XOffsets, offset => Assert.True(System.Math.Abs(offset) <= 0.2 + 1e-12));
    }

    [Fact]
    public void TheWidthNobodySetIsNineTenthsOfTheClosestTwoReadings()
    {
        // The two columns are a unit apart, so a spread of 0.9 leaves a tenth of a unit between them.
        Assert.Equal(0.9, Columns().XJitterWidth, 12);

        // Readings half a unit apart get half the width, so the gap between groups stays proportional.
        var closer = new ScatterPlot([1, 1.5, 1, 1.5], [1, 1, 2, 2]) { XJitter = JitterStyle.Density };
        Assert.Equal(0.45, closer.XJitterWidth, 12);

        // One group has nothing to keep clear of, so the width is the unit one.
        Assert.Equal(0.9, Swarm.AutomaticWidth([3, 3, 3]), 12);
    }

    [Fact]
    public void EachColumnIsSpreadAgainstItsOwnCrowd()
    {
        // Four readings at x = 1 fall two to a bin, so both bins are as full as the fullest and both
        // fill the whole width — the outer points sit half a width either side of the column.
        var plot = new ScatterPlot([1, 1, 1, 1], [1, 2, 3, 4]) { XJitter = JitterStyle.Density };

        IReadOnlyList<double> offsets = plot.XOffsets;
        Assert.Equal(-0.45, offsets[0], 12);
        Assert.Equal(0.45, offsets[1], 12);
        Assert.Equal(-0.45, offsets[2], 12);
        Assert.Equal(0.45, offsets[3], 12);
    }

    [Fact]
    public void AThinPartOfTheCrowdIsDrawnNarrowerThanTheThickPart()
    {
        // Three readings share a bin and one sits alone in the other: the crowd fills the width and
        // the lone reading stays on the centre line, which is what makes the outline a histogram.
        var plot = new ScatterPlot([1, 1, 1, 1], [1, 1, 1, 4]) { XJitter = JitterStyle.Density };

        IReadOnlyList<double> offsets = plot.XOffsets;
        Assert.Equal(-0.45, offsets[0], 12);
        Assert.Equal(0, offsets[1], 12);
        Assert.Equal(0.45, offsets[2], 12);
        Assert.Equal(0, offsets[3], 12);
    }

    [Fact]
    public void TheSpreadIsSymmetricSoTheColumnStaysWhereItIs()
    {
        double total = 0;
        foreach (double offset in Columns().XOffsets)
        {
            total += offset;
        }

        Assert.Equal(0, total, 12);
    }

    [Fact]
    public void ARandomSpreadIsTheSameSpreadTwice()
    {
        // MATLAB's random jitter moves every time it is drawn; here it is a function of which point
        // it is, so a chart saved, loaded or redrawn is the chart that was on screen.
        var first = new ScatterPlot([.. Xs], [.. Ys]) { XJitter = JitterStyle.Rand };
        var second = new ScatterPlot([.. Xs], [.. Ys]) { XJitter = JitterStyle.Rand };

        Assert.Equal(first.XOffsets, second.XOffsets);
        Assert.All(first.XOffsets, offset => Assert.True(System.Math.Abs(offset) <= 0.45 + 1e-12));

        var bell = new ScatterPlot([.. Xs], [.. Ys]) { XJitter = JitterStyle.Randn };
        Assert.All(bell.XOffsets, offset => Assert.True(System.Math.Abs(offset) <= 0.45 + 1e-12));
        Assert.NotEqual(first.XOffsets, bell.XOffsets);
    }

    [Fact]
    public void TheChartIsAsWideAsItIsDrawnRatherThanAsWideAsItsData()
    {
        var plain = new ScatterPlot([.. Xs], [.. Ys]);
        ScatterPlot spread = Columns();

        Assert.Equal(1, plain.GetXDataBounds().Min);
        Assert.True(spread.GetXDataBounds().Min < 1);
        Assert.True(spread.GetXDataBounds().Max > 2);

        // Only the axis being spread moves; the readings themselves are untouched either way.
        Assert.Equal(plain.GetYDataBounds().Min, spread.GetYDataBounds().Min);
        Assert.Equal(Xs[0], spread.Data.GetX(0));
        Assert.Equal(Xs[^1], spread.Data.GetX(Xs.Length - 1));
    }

    [Fact]
    public void SettingTheWidthBackToZeroHandsItToTheData()
    {
        ScatterPlot plot = Columns();
        plot.XJitterWidth = 0.2;
        Assert.Equal(0.2, plot.XJitterWidth, 12);

        plot.XJitterWidth = 0;
        Assert.Equal(0.9, plot.XJitterWidth, 12);
    }

    [Fact]
    public void ASwarmInSpaceSpreadsTheTwoCoordinatesThatAreNotTheHeight()
    {
        var plot = new Scatter3DPlot([1, 1, 1, 1], [2, 2, 2, 2], [1, 2, 3, 4])
        {
            XJitter = JitterStyle.Density,
            YJitter = JitterStyle.Density,
        };

        Assert.All(plot.XOffsets, offset => Assert.True(System.Math.Abs(offset) <= 0.45 + 1e-12));
        Assert.Contains(plot.XOffsets, offset => offset != 0);
        Assert.Contains(plot.YOffsets, offset => offset != 0);

        // The height is left alone, and the drawn positions are the readings plus their spread.
        Assert.All(plot.ZOffsets, offset => Assert.Equal(0, offset));
        Assert.Equal([1, 2, 3, 4], plot.DrawnZ);
        Assert.Equal(plot.X[0] + plot.XOffsets[0], plot.DrawnX[0], 12);
    }

    [Fact]
    public void BubblesInSpaceAreSizedByValueRatherThanByArea()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        var plot = new Scatter3DPlot([1, 2, 3], [1, 2, 3], [1, 2, 3])
        {
            SizeData = [10, 20, 30],
            BubbleSizing = true,
        };
        axes.Plots.Add(plot);

        // The axes works the bubble limits out from every chart that says its sizes are values, which
        // is the whole reason that is a property rather than something the verb kept to itself.
        Assert.Equal(10, axes.ResolveBubbleLimits().Min);
        Assert.Equal(30, axes.ResolveBubbleLimits().Max);

        Assert.Equal(BubbleScale.DefaultSizeRange.Min, plot.DiameterAt(0), 12);
        Assert.Equal(BubbleScale.DefaultSizeRange.Max, plot.DiameterAt(2), 12);
        Assert.True(plot.DiameterAt(1) > plot.DiameterAt(0));

        // Read as areas instead, the same array gives the square roots and nothing else changes.
        plot.BubbleSizing = false;
        Assert.Equal(System.Math.Sqrt(10), plot.DiameterAt(0), 12);
    }

    [Fact]
    public void ASpreadIsWorkedOutAgainWhenTheReadingsChange()
    {
        var plot = new Scatter3DPlot([1, 1], [1, 1], [1, 2]) { XJitter = JitterStyle.Density };
        Assert.Equal(2, plot.DrawnX.Count);

        plot.SetData([5, 5, 5], [1, 1, 1], [1, 2, 3]);
        Assert.Equal(3, plot.DrawnX.Count);
        Assert.All(plot.DrawnX, drawn => Assert.True(System.Math.Abs(drawn - 5) <= 0.45 + 1e-12));
    }

    [Fact]
    public void ReadingsThatAreAllTheSameStillGetASpread()
    {
        // Nothing to measure a gap against, so the width is the unit one and the crowd fans out in it
        // rather than every point landing on the same pixel.
        var plot = new ScatterPlot([3, 3, 3, 3], [1, 2, 3, 4]) { XJitter = JitterStyle.Density };

        Assert.Equal(0.9, plot.XJitterWidth, 12);
        Assert.Contains(plot.XOffsets, offset => offset != 0);
    }
}
