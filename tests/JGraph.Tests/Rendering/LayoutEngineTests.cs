using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using Xunit;

namespace JGraph.Tests.Rendering;

public class LayoutEngineTests
{
    [Fact]
    public void ComputeOuterBounds_MapsNormalizedToDevice()
    {
        Rect2D outer = LayoutEngine.ComputeOuterBounds(new Size2D(800, 600), new Rect2D(0.5, 0, 0.5, 1));
        Assert.Equal(400, outer.X);
        Assert.Equal(0, outer.Y);
        Assert.Equal(400, outer.Width);
        Assert.Equal(600, outer.Height);
    }

    [Fact]
    public void Compute_DeflatesPlotAreaByDecorations()
    {
        var axes = new AxesModel();
        AxesLayout layout = LayoutEngine.Compute(axes, new Size2D(400, 300), new Thickness(50, 20, 10, 40));

        Assert.Equal(50, layout.PlotArea.X);
        Assert.Equal(20, layout.PlotArea.Y);
        Assert.Equal(340, layout.PlotArea.Width);
        Assert.Equal(240, layout.PlotArea.Height);
    }

    [Fact]
    public void EstimateDecorations_ReservesMoreWhenLabelsPresent()
    {
        var bare = new AxesModel();
        bare.PrimaryXAxis.ShowTickLabels = false;
        bare.PrimaryYAxis.ShowTickLabels = false;

        var labeled = new AxesModel { Title = "Title" };
        labeled.PrimaryXAxis.Label = "Time";
        labeled.PrimaryYAxis.Label = "Voltage";

        Thickness bareMargins = LayoutEngine.EstimateDecorations(bare);
        Thickness labeledMargins = LayoutEngine.EstimateDecorations(labeled);

        Assert.True(labeledMargins.Left > bareMargins.Left);
        Assert.True(labeledMargins.Bottom > bareMargins.Bottom);
        Assert.True(labeledMargins.Top > bareMargins.Top);
    }
}
