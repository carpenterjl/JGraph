using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// M57 wave E: round-tripping the spread and the bubble reading. Both are defaulted fields on plots
/// the format already carried, so v6 stays v6 — nothing new is stored, only more about a scatter.
/// </summary>
public class SwarmSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void ASwarmChartComesBackSpreadTheSameWay()
    {
        var figure = new FigureModel();
        var plot = new ScatterPlot([1, 1, 2, 2], [1, 2, 1, 2])
        {
            XJitter = JitterStyle.Density,
            YJitter = JitterStyle.Rand,
            YJitterWidth = 0.3,
        };
        figure.AddAxes().Plots.Add(plot);

        var loaded = (ScatterPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(JitterStyle.Density, loaded.XJitter);
        Assert.Equal(JitterStyle.Rand, loaded.YJitter);
        Assert.Equal(0.3, loaded.YJitterWidth, 12);
        Assert.Equal(plot.XOffsets, loaded.XOffsets);
    }

    [Fact]
    public void AWidthThatFollowedTheDataGoesOnFollowingIt()
    {
        // Saving the width in force would pin it to whatever the data was on the day it was saved;
        // what is stored is that nobody set one, so a loaded chart still works its own out.
        var figure = new FigureModel();
        figure.AddAxes().Plots.Add(new ScatterPlot([1, 3, 1, 3], [1, 1, 2, 2])
        {
            XJitter = JitterStyle.Density,
        });

        var loaded = (ScatterPlot)RoundTrip(figure).Axes[0].Plots[0];
        Assert.Equal(1.8, loaded.XJitterWidth, 12);

        loaded.SetData([1, 2, 1, 2], [1, 1, 2, 2]);
        Assert.Equal(0.9, loaded.XJitterWidth, 12);
    }

    [Fact]
    public void ASwarmAndABubbleChartInSpaceBothComeBackWhole()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Plots.Add(new Scatter3DPlot([1, 1, 2], [1, 1, 2], [1, 2, 3])
        {
            XJitter = JitterStyle.Density,
            YJitter = JitterStyle.Randn,
            ZJitter = JitterStyle.Rand,
            ZJitterWidth = 0.25,
        });
        axes.Plots.Add(new Scatter3DPlot([1, 2, 3], [1, 2, 3], [1, 2, 3])
        {
            SizeData = [5, 10, 15],
            BubbleSizing = true,
        });

        AxesModel loaded = RoundTrip(figure).Axes[0];
        var swarm = (Scatter3DPlot)loaded.Plots[0];
        var bubbles = (Scatter3DPlot)loaded.Plots[1];

        Assert.Equal(JitterStyle.Density, swarm.XJitter);
        Assert.Equal(JitterStyle.Randn, swarm.YJitter);
        Assert.Equal(JitterStyle.Rand, swarm.ZJitter);
        Assert.Equal(0.25, swarm.ZJitterWidth, 12);

        Assert.True(bubbles.BubbleSizing);
        Assert.Equal(JitterStyle.None, bubbles.XJitter);
        Assert.Equal([5, 10, 15], bubbles.SizeData!);
        Assert.Equal(5, loaded.ResolveBubbleLimits().Min);
    }

    [Fact]
    public void AnOrdinaryScatterIsUnchangedByAnyOfThis()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddScatter([1, 2], [1, 2]);

        var loaded = (ScatterPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(JitterStyle.None, loaded.XJitter);
        Assert.Equal(JitterStyle.None, loaded.YJitter);
        Assert.False(loaded.BubbleSizing);
    }
}
