using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Api;

// The JG facade holds static current-figure state, so these tests run one at a time.
[Collection("JG facade")]
public class JGFacadeTests
{
    public JGFacadeTests() => JG.Reset();

    [Fact]
    public void Plot_AddsLineToCurrentAxes()
    {
        JG.Figure();
        LinePlot line = JG.Plot(new double[] { 0, 1, 2 }, new double[] { 0, 1, 4 });

        Assert.Single(JG.Gca().Plots);
        Assert.Same(line, JG.Gca().Plots[0]);
    }

    [Fact]
    public void Plot_WithoutHold_ReplacesPrevious()
    {
        JG.Figure();
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.Plot(new double[] { 0, 1 }, new double[] { 1, 0 });

        Assert.Single(JG.Gca().Plots);
    }

    [Fact]
    public void Hold_AccumulatesSeries()
    {
        JG.Figure();
        JG.Hold(true);
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.Plot(new double[] { 0, 1 }, new double[] { 1, 0 });

        Assert.Equal(2, JG.Gca().Plots.Count);
    }

    [Fact]
    public void LineSpec_AppliesColorAndMarkerOnly()
    {
        JG.Figure();
        LinePlot line = JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 }, "go");

        Assert.Equal(Colors.Green, line.Color);
        Assert.Equal(MarkerType.Circle, line.Marker);
        Assert.Equal(DashStyle.None, line.DashStyle); // marker only => no line
    }

    [Fact]
    public void TitleAndLabels_SetOnAxes()
    {
        JG.Figure();
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.Title("T");
        JG.XLabel("X");
        JG.YLabel("Y");

        AxesModel axes = JG.Gca();
        Assert.Equal("T", axes.Title);
        Assert.Equal("X", axes.PrimaryXAxis.Label);
        Assert.Equal("Y", axes.PrimaryYAxis.Label);
    }

    [Fact]
    public void Legend_EnablesAndNames()
    {
        JG.Figure();
        JG.Hold(true);
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.Plot(new double[] { 0, 1 }, new double[] { 1, 0 });
        JG.Legend("a", "b");

        AxesModel axes = JG.Gca();
        Assert.True(axes.Legend.Visible);
        Assert.Equal("a", axes.Plots[0].DisplayName);
        Assert.Equal("b", axes.Plots[1].DisplayName);
    }

    [Fact]
    public void XLim_DisablesAutoScale()
    {
        JG.Figure();
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.XLim(-5, 5);

        AxisModel axis = JG.Gca().PrimaryXAxis;
        Assert.False(axis.AutoScale);
        Assert.Equal(-5, axis.Range.Min);
        Assert.Equal(5, axis.Range.Max);
    }
}
