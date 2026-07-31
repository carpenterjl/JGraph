using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>M45.E: round-tripping the arrow field and the patch's new face switch.</summary>
public class SurfaceVariantSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void QuiverPlot_RoundTrips_ItsFieldAndScaling()
    {
        var figure = new FigureModel();
        QuiverPlot plot = figure.AddAxes().AddQuiver3([0, 1], [2, 3], [4, 5], [1, 0], [0, 1], [1, 1]);
        plot.Color = Colors.Red;
        plot.LineWidth = 2.5;
        plot.AutoScale = false;
        plot.Scale = 3;
        plot.AutoScaleFactor = 0.4;
        plot.ShowArrowHead = false;
        plot.MaxHeadSize = 0.35;

        var loaded = (QuiverPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([0, 1], loaded.X);
        Assert.Equal([4, 5], loaded.Z);
        Assert.Equal([1, 1], loaded.W);
        Assert.Equal(Colors.Red, loaded.Color);
        Assert.Equal(2.5, loaded.LineWidth);
        Assert.False(loaded.AutoScale);
        Assert.Equal(3, loaded.Scale);
        Assert.Equal(0.4, loaded.AutoScaleFactor);
        Assert.False(loaded.ShowArrowHead);
        Assert.Equal(0.35, loaded.MaxHeadSize);
        Assert.Equal(3, loaded.EffectiveScale);
    }

    /// <summary>
    /// A trimesh is a patch whose faces are off, and that has to survive a save — otherwise it comes
    /// back as the trisurf it was distinguished from.
    /// </summary>
    [Fact]
    public void PatchPlot_RoundTrips_ItsFaceVisibility()
    {
        var figure = new FigureModel();
        PatchPlot patch = figure.AddAxes().AddPatch([0, 1, 0], [0, 0, 1], [0, 1, 2]);
        patch.FaceVisible = false;
        patch.ColorData = new double[] { 0, 1, 2 };

        var loaded = (PatchPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.False(loaded.FaceVisible);
        Assert.Equal([0, 1, 2], loaded.ColorData!);
    }

    /// <summary>An ordinary patch keeps its faces, which is the default a saved file must not lose.</summary>
    [Fact]
    public void PatchPlot_KeepsItsFaces_WhenNothingTurnedThemOff()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddPatch([0, 1, 0], [0, 0, 1], [0, 1, 2]);

        Assert.True(((PatchPlot)RoundTrip(figure).Axes[0].Plots[0]).FaceVisible);
    }
}
