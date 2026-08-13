using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>
/// M57 wave D: what a binned scatter counts, where its bins sit, and what it does with a reading on
/// an edge or off the end.
/// </summary>
public class BinScatterLayoutTests
{
    /// <summary>Ten readings on a four-by-three lattice, used by most of the cases below.</summary>
    private static readonly double[] Xs = [1, 2, 3, 4, 1, 2, 3, 4, 1, 2];
    private static readonly double[] Ys = [1, 1, 1, 1, 2, 2, 2, 2, 3, 3];

    [Fact]
    public void EveryReadingLandsInExactlyOneBin()
    {
        var plot = new BinScatterPlot([.. Xs], [.. Ys]) { NumBinsX = 2, NumBinsY = 2 };

        double total = 0;
        foreach (double count in plot.Values)
        {
            total += count;
        }

        Assert.Equal(Xs.Length, total);
    }

    [Fact]
    public void TheCountsAreIndexedAcrossThenUp()
    {
        // Which way round the grid is stored is the one thing a script reading Values depends on,
        // and it is MATLAB's: as many rows as there are bins across.
        var plot = new BinScatterPlot([.. Xs], [.. Ys]) { NumBinsX = 2, NumBinsY = 2 };

        Assert.Equal(2, plot.Values.GetLength(0));
        Assert.Equal(2, plot.Values.GetLength(1));

        // Bins across split at 2.5, bins up at 2: the low-x, high-y corner holds four readings.
        Assert.Equal(2, plot.Values[0, 0]);
        Assert.Equal(4, plot.Values[0, 1]);
        Assert.Equal(2, plot.Values[1, 0]);
        Assert.Equal(2, plot.Values[1, 1]);
    }

    [Fact]
    public void TheBinsSpanTheReadingsAndTheChartSpansTheBins()
    {
        var plot = new BinScatterPlot([.. Xs], [.. Ys]) { NumBinsX = 3, NumBinsY = 2 };

        Assert.Equal(4, plot.XBinEdges.Count);
        Assert.Equal(3, plot.YBinEdges.Count);
        Assert.Equal(1, plot.XBinEdges[0]);
        Assert.Equal(4, plot.XBinEdges[^1]);
        Assert.Equal(1, plot.GetXDataBounds().Min);
        Assert.Equal(4, plot.GetXDataBounds().Max);
        Assert.Equal(1, plot.GetYDataBounds().Min);
        Assert.Equal(3, plot.GetYDataBounds().Max);
    }

    [Fact]
    public void GivenLimitsMoveTheBinsAndDropWhatFallsOutsideThem()
    {
        var plot = new BinScatterPlot([.. Xs], [.. Ys])
        {
            NumBinsX = 2,
            NumBinsY = 1,
            XLimits = new DataRange(0, 2),
        };

        Assert.Equal(0, plot.XBinEdges[0]);
        Assert.Equal(2, plot.XBinEdges[^1]);

        // Only the readings at x = 1 and x = 2 are left, which is six of the ten.
        Assert.Equal(6, plot.Values[0, 0] + plot.Values[1, 0]);
    }

    [Fact]
    public void ARepeatedReadingGivesTheBinsNoWidthUntilOneIsMadeForThem()
    {
        // Every reading the same would divide by a zero-width span, so the one bin is half a unit
        // either side of the value and the reading sits in the middle of it.
        var plot = new BinScatterPlot([5, 5, 5], [2, 2, 2]) { NumBinsX = 1, NumBinsY = 1 };

        Assert.Equal(4.5, plot.XBinEdges[0]);
        Assert.Equal(5.5, plot.XBinEdges[^1]);
        Assert.Equal(3, plot.Values[0, 0]);
    }

    [Fact]
    public void ANonFiniteReadingIsCountedNowhere()
    {
        var plot = new BinScatterPlot([1, 2, double.NaN], [1, 2, 1]) { NumBinsX = 2, NumBinsY = 2 };

        double total = 0;
        foreach (double count in plot.Values)
        {
            total += count;
        }

        Assert.Equal(2, total);
    }

    [Fact]
    public void ChangingTheBinCountCountsAgain()
    {
        var plot = new BinScatterPlot([.. Xs], [.. Ys]) { NumBinsX = 1, NumBinsY = 1 };
        Assert.Equal(10, plot.Values[0, 0]);

        plot.NumBinsX = 2;
        Assert.Equal(2, plot.Values.GetLength(0));
        Assert.Equal(6, plot.Values[0, 0]);
    }

    [Fact]
    public void AnEmptyBinIsNotDrawnUnlessItIsAskedFor()
    {
        // Three bins across [1, 10] and nothing in the middle one.
        var plot = new BinScatterPlot([1, 2, 3, 10], [1, 1, 1, 1]) { NumBinsX = 3, NumBinsY = 1 };
        Assert.Equal(0, plot.Values[1, 0]);
        Assert.True(plot.ColorOf(1, 0).IsTransparent);

        plot.ShowEmptyBins = true;
        Assert.False(plot.ColorOf(1, 0).IsTransparent);
    }

    [Fact]
    public void TheColourRangeRunsFromOneReadingToTheFullestBin()
    {
        var plot = new BinScatterPlot([.. Xs], [.. Ys]) { NumBinsX = 2, NumBinsY = 2 };

        Assert.Equal(1, plot.EffectiveLimits().Min);
        Assert.Equal(4, plot.EffectiveLimits().Max);

        // Showing empty bins puts zero at the bottom of the colormap, since it is now a value.
        plot.ShowEmptyBins = true;
        Assert.Equal(0, plot.EffectiveLimits().Min);
    }

    [Fact]
    public void TheBinCountIsHeldToWhatCanBeDrawn()
    {
        var plot = new BinScatterPlot([1, 2], [1, 2]) { NumBinsX = 0, NumBinsY = 100_000 };

        Assert.Equal(1, plot.NumBinsX);
        Assert.Equal(BinScatterPlot.MaxBinsPerSide, plot.NumBinsY);
    }

    [Fact]
    public void EveryReadingNeedsBothOfItsCoordinates()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new BinScatterPlot([1, 2, 3], [1, 2]));
        Assert.Contains("3 and 2", error.Message);
    }

    [Fact]
    public void TheChartShowsTheColorbarBecauseNothingElseSaysHowManyAReadingIs()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        Assert.False(axes.Colorbar.Visible);

        axes.AddBinScatter([1, 2], [1, 2]);
        Assert.True(axes.Colorbar.Visible);
    }

    [Fact]
    public void TheDefaultBinCountIsTheSquareRootChoiceCappedAtAHundred()
    {
        Assert.Equal(1, Binning.SquareRootChoice(0));
        Assert.Equal(4, Binning.SquareRootChoice(10));
        Assert.Equal(10, Binning.SquareRootChoice(100));
        Assert.Equal(100, Binning.SquareRootChoice(1_000_000));
        Assert.Equal(4, new BinScatterPlot([.. Xs], [.. Ys]).NumBinsX);
    }

    [Fact]
    public void TheLastBinTakesBothOfItsEdgesSoTheTopReadingIsCounted()
    {
        // The one-dimensional rule, applied in two directions rather than replaced by a new one.
        double[,] counts = Binning.Counts2D([0, 1, 2], [0, 1, 2], [0, 1, 2], [0, 1, 2]);

        Assert.Equal(1, counts[0, 0]);
        Assert.Equal(2, counts[1, 1]);
    }
}
