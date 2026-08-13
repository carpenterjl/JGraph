using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// M57 wave D: round-tripping a binned scatter. The format stays at v6 — this is a new plot type
/// within it, not a new shape of file.
/// </summary>
public class BinScatterSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void BinScatterPlot_RoundTrips_ItsReadingsAndItsGrid()
    {
        var figure = new FigureModel();
        BinScatterPlot plot = figure.AddAxes().AddBinScatter([1, 2, 3, 4], [1, 2, 3, 4]);
        plot.NumBinsX = 3;
        plot.NumBinsY = 2;
        plot.XLimits = new DataRange(0, 5);
        plot.YLimits = new DataRange(-1, 6);
        plot.ShowEmptyBins = true;
        plot.Colormap = Colormap.Hot;
        plot.ColorLimits = new DataRange(0, 8);

        var loaded = (BinScatterPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([1, 2, 3, 4], loaded.X);
        Assert.Equal([1, 2, 3, 4], loaded.Y);
        Assert.Equal(3, loaded.NumBinsX);
        Assert.Equal(2, loaded.NumBinsY);
        Assert.Equal(0, loaded.XLimits!.Value.Min);
        Assert.Equal(5, loaded.XLimits!.Value.Max);
        Assert.Equal(-1, loaded.YLimits!.Value.Min);
        Assert.Equal(6, loaded.YLimits!.Value.Max);
        Assert.True(loaded.ShowEmptyBins);
        Assert.Equal("Hot", loaded.Colormap.Name);
        Assert.Equal(8, loaded.ColorLimits!.Value.Max);
    }

    [Fact]
    public void TheCountsAreWorkedOutAgainOnLoadRatherThanStored()
    {
        // The readings are what is saved, so the grid a loaded chart answers with is the grid the
        // saved one drew — and it can still be rebinned afterwards.
        var figure = new FigureModel();
        BinScatterPlot original = figure.AddAxes().AddBinScatter([1, 1, 2, 2], [1, 1, 1, 1]);
        original.NumBinsX = 2;
        original.NumBinsY = 1;

        var loaded = (BinScatterPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(2, loaded.Values[0, 0]);
        Assert.Equal(2, loaded.Values[1, 0]);

        loaded.NumBinsX = 1;
        Assert.Equal(4, loaded.Values[0, 0]);
    }

    [Fact]
    public void LimitsThatWereNeverSetStayUnsetSoTheyKeepFollowingTheReadings()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddBinScatter([1, 2], [1, 2]);

        var loaded = (BinScatterPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Null(loaded.XLimits);
        Assert.Null(loaded.YLimits);
        Assert.Null(loaded.ColorLimits);
    }
}
