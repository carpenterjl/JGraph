using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>M45.D: round-tripping the drawing primitives through the .graph format.</summary>
public class Primitive3DSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    [Fact]
    public void Line3DPlot_RoundTrips_ItsPointsAndStyle()
    {
        var figure = new FigureModel();
        Line3DPlot plot = figure.AddAxes().AddLine3D([0, 1, 2], [3, 4, 5], [6, 7, 8]);
        plot.Color = Colors.Red;
        plot.LineWidth = 3;
        plot.DashStyle = DashStyle.DashDot;
        plot.Marker = MarkerType.Square;
        plot.MarkerSize = 9;

        var loaded = (Line3DPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([0, 1, 2], loaded.X);
        Assert.Equal([6, 7, 8], loaded.Z);
        Assert.Equal(Colors.Red, loaded.Color);
        Assert.Equal(3, loaded.LineWidth);
        Assert.Equal(DashStyle.DashDot, loaded.DashStyle);
        Assert.Equal(MarkerType.Square, loaded.Marker);
        Assert.Equal(9, loaded.MarkerSize);
    }

    [Fact]
    public void Scatter3DPlot_RoundTrips_ItsPerPointChannels()
    {
        var figure = new FigureModel();
        Scatter3DPlot plot = figure.AddAxes().AddScatter3D([0, 1], [2, 3], [4, 5]);
        plot.SizeData = new double[] { 16, 64 };
        plot.ColorData = new double[] { -1, 1 };
        plot.Colormap = Colormap.Hot;
        plot.Filled = true;
        plot.AutoScaleColor = false;
        plot.ColorMin = -2;
        plot.ColorMax = 2;

        var loaded = (Scatter3DPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([16, 64], loaded.SizeData);
        Assert.Equal([-1, 1], loaded.ColorData);
        Assert.Equal("Hot", loaded.Colormap.Name);
        Assert.True(loaded.Filled);
        Assert.Equal((-2, 2), ((JGraph.Rendering.IColorMapped)loaded).ColorRange);
    }

    [Fact]
    public void PatchPlot_RoundTrips_ItsFacesAndColoring()
    {
        var figure = new FigureModel();
        PatchPlot patch = figure.AddAxes().AddPatch(
            [0, 1, 1, 0],
            [0, 0, 1, 1],
            [0, 0, 1, 1],
            [[0, 1, 2], [0, 2, 3]]);
        patch.ColorData = new double[] { 1, 2, 3, 4 };
        patch.Shading = PatchShading.Interp;
        patch.EdgeColor = null;
        patch.EdgeWidth = 2.5;
        patch.Colormap = Colormap.Cool;

        var loaded = (PatchPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(2, loaded.Faces.Count);
        Assert.Equal([0, 2, 3], loaded.Faces[1]);
        Assert.Equal([1, 2, 3, 4], loaded.ColorData);
        Assert.Equal(PatchShading.Interp, loaded.Shading);
        Assert.Null(loaded.EdgeColor);
        Assert.Equal(2.5, loaded.EdgeWidth);
    }

    /// <summary>
    /// The patch's default edge is black, not "no edge" — a document that never touched EdgeColor has
    /// to come back with the outline MATLAB's patch draws.
    /// </summary>
    [Fact]
    public void PatchPlot_KeepsItsDefaultBlackEdge()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddPatch([0, 1, 1], [0, 0, 1], new double[3]);

        var loaded = (PatchPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(Colors.Black, loaded.EdgeColor);
    }

    [Fact]
    public void TextAnnotation_RoundTripsItsHeight()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Annotations.Add(new TextAnnotation(1, 2, "peak") { Z = 7.5 });

        var loaded = (TextAnnotation)RoundTrip(figure).Axes[0].Annotations[0];

        Assert.Equal(7.5, loaded.Z);
        Assert.Equal("peak", loaded.Text);
    }

    /// <summary>A label written before heights existed reads back on the floor, not somewhere new.</summary>
    [Fact]
    public void TextAnnotation_WithoutAHeight_ReadsBackAtZero()
    {
        const string LegacyJson = """
            {
              "format": "jgraph",
              "formatVersion": 5,
              "figure": {
                "name": "Figure",
                "axes": [
                  {
                    "name": "Axes",
                    "xAxes": [],
                    "yAxes": [],
                    "plots": [],
                    "annotations": [
                      { "type": "text", "name": "Text", "position": { "x": 1, "y": 2 }, "text": "old" }
                    ]
                  }
                ]
              }
            }
            """;

        FigureModel figure = GraphFormat.Deserialize(LegacyJson);

        var label = (TextAnnotation)figure.Axes[0].Annotations[0];
        Assert.Equal(0, label.Z);
        Assert.Equal("old", label.Text);
    }
}
