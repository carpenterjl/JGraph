using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Model;

/// <summary>M20b: the 3D members of <see cref="AxesModel"/> and colormap name lookup.</summary>
public class Axes3DModelTests
{
    private sealed class FakeZPlot : PlotObject, IHasZData
    {
        public DataRange X { get; init; } = new(0, 1);

        public DataRange Y { get; init; } = new(0, 1);

        public DataRange Z { get; init; } = new(0, 1);

        public override DataRange GetXDataBounds() => X;

        public override DataRange GetYDataBounds() => Y;

        public DataRange GetZDataBounds() => Z;
    }

    [Fact]
    public void Defaults_MatchMatlab()
    {
        var axes = new AxesModel();

        Assert.False(axes.Is3D);
        Assert.Equal(-37.5, axes.Azimuth);
        Assert.Equal(30, axes.Elevation);
        Assert.NotNull(axes.ZAxis);
        Assert.False(axes.Colorbar.Visible);
    }

    [Fact]
    public void Elevation_IsClampedToPlusMinus90()
    {
        var axes = new AxesModel { Elevation = 250 };
        Assert.Equal(90, axes.Elevation);

        axes.Elevation = -250;
        Assert.Equal(-90, axes.Elevation);
    }

    [Fact]
    public void RecomputeDataBounds_UnionsZExtents_OfVisible3DPlots()
    {
        var axes = new AxesModel { AutoScalePadding = 0 };
        axes.Plots.Add(new FakeZPlot { Z = new DataRange(0, 5) });
        axes.Plots.Add(new FakeZPlot { Z = new DataRange(-2, 3) });
        axes.Plots.Add(new FakeZPlot { Z = new DataRange(50, 90), Visible = false });

        axes.RecomputeDataBounds();

        Assert.Equal(-2, axes.ZAxis.DataBounds.Min);
        Assert.Equal(5, axes.ZAxis.DataBounds.Max);
        Assert.Equal(-2, axes.ZAxis.Range.Min);
        Assert.Equal(5, axes.ZAxis.Range.Max);
    }

    [Fact]
    public void ZAxis_ManualRange_SurvivesRecompute()
    {
        var axes = new AxesModel();
        axes.ZAxis.AutoScale = false;
        axes.ZAxis.Range = new DataRange(0, 42);
        axes.Plots.Add(new FakeZPlot { Z = new DataRange(0, 5) });

        axes.RecomputeDataBounds();

        Assert.Equal(42, axes.ZAxis.Range.Max);
    }

    [Theory]
    [InlineData("viridis")]
    [InlineData("Jet")]
    [InlineData("HOT")]
    [InlineData("cool")]
    [InlineData("gray")]
    [InlineData("greyscale")]
    public void ColormapLookup_IsCaseInsensitive(string name)
    {
        Assert.True(Colormap.TryGetByName(name, out Colormap map));
        Assert.NotNull(map);
    }

    [Fact]
    public void ColormapLookup_UnknownName_Fails()
    {
        Assert.False(Colormap.TryGetByName("plasma", out _));
        Assert.False(Colormap.TryGetByName(null, out _));
    }
}
