using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Serialization;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

/// <summary>M24: the true-colour <see cref="RgbImagePlot"/> — rendering through the DrawImage seam and .graph round-trip.</summary>
public class RgbImagePlotTests
{
    private static uint[] Pixels() => new uint[]
    {
        0xFFFF0000, 0xFF00FF00, // red, green
        0xFF0000FF, 0xFFFFFFFF, // blue, white
    };

    [Fact]
    public void Render_EmitsOneImageDraw()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddRgbImage(Pixels(), 2, 2);
        var context = new RecordingRenderContext(new Size2D(200, 200));

        new FigureRenderer().Render(figure, context, Theme.Light);

        Assert.Equal(1, context.ImageCount);
        Assert.True(context.LastImageDestination.Width > 0);
    }

    [Fact]
    public void Constructor_RejectsUndersizedBuffer()
    {
        Assert.Throws<ArgumentException>(() => new RgbImagePlot(new uint[] { 0xFF000000 }, 2, 2));
    }

    [Fact]
    public void GraphRoundTrip_PreservesPixelsAndExtents()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        RgbImagePlot plot = axes.AddRgbImage(Pixels(), 2, 2);
        plot.XExtent = new DataRange(0, 20);
        plot.YExtent = new DataRange(0, 10);
        plot.Interpolate = true;

        FigureModel restored = GraphFormat.Deserialize(GraphFormat.Serialize(figure));

        var back = Assert.IsType<RgbImagePlot>(restored.Axes[0].Plots[0]);
        Assert.Equal(2, back.Width);
        Assert.Equal(2, back.Height);
        Assert.Equal(Pixels(), back.Pixels);
        Assert.Equal(20, back.XExtent.Max);
        Assert.Equal(10, back.YExtent.Max);
        Assert.True(back.Interpolate);
    }

    [Fact]
    public void Serialize_UsesFormatVersionFive()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddRgbImage(Pixels(), 2, 2);
        Assert.Contains($"\"formatVersion\": {GraphFormat.CurrentVersion}", GraphFormat.Serialize(figure));
        Assert.True(GraphFormat.CurrentVersion >= 5);
    }
}
