using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>M20b: round-tripping 3D axes (camera + Z axis), surface/contour plots, and the colorbar.</summary>
public class Surface3DSerializationTests
{
    private static FigureModel RoundTrip(FigureModel figure) =>
        GraphFormat.Deserialize(GraphFormat.Serialize(figure));

    private static (double[] X, double[] Y, double[,] Z) SmallGrid()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 10];
        var z = new double[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
        return (x, y, z);
    }

    [Fact]
    public void SurfacePlot_RoundTrips_DataStyleAndColors()
    {
        (double[] x, double[] y, double[,] z) = SmallGrid();
        var figure = new FigureModel();
        SurfacePlot surface = figure.AddAxes().AddSurface(x, y, z, SurfaceStyle.Wireframe);
        surface.Colormap = Colormap.Jet;
        surface.ShowContourBelow = true;
        surface.EdgeColor = Colors.Red;
        surface.EdgeWidth = 2;
        surface.AutoScaleColor = false;
        surface.ColorMin = -1;
        surface.ColorMax = 7;

        var loaded = (SurfacePlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal(x, loaded.X);
        Assert.Equal(y, loaded.Y);
        Assert.Equal(6, loaded.Z[1, 2]);
        Assert.Equal(SurfaceStyle.Wireframe, loaded.Style);
        Assert.True(loaded.ShowContourBelow);
        Assert.Equal(Colors.Red, loaded.EdgeColor);
        Assert.Equal(2, loaded.EdgeWidth);
        Assert.False(loaded.AutoScaleColor);
        Assert.Equal((-1, 7), loaded.ColorRange);
        Assert.Equal("Jet", loaded.Colormap.Name);
    }

    [Fact]
    public void Axes3D_CameraAndZAxis_RoundTrip()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] x, double[] y, double[,] z) = SmallGrid();
        axes.AddSurface(x, y, z);
        axes.Azimuth = 120;
        axes.Elevation = -45;
        axes.ZAxis.AutoScale = false;
        axes.ZAxis.Range = new DataRange(-3, 12);
        axes.ZAxis.Label = "Height (mm)";

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.True(loaded.Is3D);
        Assert.Equal(120, loaded.Azimuth);
        Assert.Equal(-45, loaded.Elevation);
        Assert.False(loaded.ZAxis.AutoScale);
        Assert.Equal(-3, loaded.ZAxis.Range.Min);
        Assert.Equal(12, loaded.ZAxis.Range.Max);
        Assert.Equal("Height (mm)", loaded.ZAxis.Label);
    }

    [Fact]
    public void Colorbar_RoundTrips()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Colorbar.Visible = true;
        axes.Colorbar.Width = 24;
        axes.Colorbar.Label = "Temperature";

        AxesModel loaded = RoundTrip(figure).Axes[0];

        Assert.True(loaded.Colorbar.Visible);
        Assert.Equal(24, loaded.Colorbar.Width);
        Assert.Equal("Temperature", loaded.Colorbar.Label);
    }

    [Fact]
    public void ContourPlot_RoundTrips()
    {
        (double[] x, double[] y, double[,] z) = SmallGrid();
        var figure = new FigureModel();
        ContourPlot contour = figure.AddAxes().AddContour(x, y, z, levels: [2.0, 4.0], filled: true);
        contour.LineWidth = 3;
        contour.Colormap = Colormap.Hot;

        var loaded = (ContourPlot)RoundTrip(figure).Axes[0].Plots[0];

        Assert.Equal([2.0, 4.0], loaded.Levels!);
        Assert.True(loaded.Filled);
        Assert.Equal(3, loaded.LineWidth);
        Assert.Equal("Hot", loaded.Colormap.Name);
        Assert.Equal(5, loaded.Z[1, 1]);
    }

    [Fact]
    public void LegacyDocument_WithoutNewFields_LoadsWithDefaults()
    {
        // A minimal format-version-1 document, as written before M20.
        const string LegacyJson = """
            {
              "format": "jgraph",
              "formatVersion": 1,
              "figure": {
                "name": "Figure",
                "title": "",
                "axes": [
                  {
                    "name": "Axes",
                    "title": "",
                    "autoScalePadding": 0.05,
                    "visible": true,
                    "xAxes": [],
                    "yAxes": [],
                    "plots": []
                  }
                ]
              }
            }
            """;

        FigureModel figure = GraphFormat.Deserialize(LegacyJson);
        AxesModel axes = figure.Axes[0];

        Assert.False(axes.Is3D);
        Assert.Equal(-37.5, axes.Azimuth);
        Assert.Equal(30, axes.Elevation);
        Assert.NotNull(axes.ZAxis);
        Assert.False(axes.Colorbar.Visible);
    }
}
