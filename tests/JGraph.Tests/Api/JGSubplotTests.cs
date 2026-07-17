using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Api;

// The JG facade holds static current-figure state, so these tests run one at a time.
[Collection("JG facade")]
public class JGSubplotTests
{
    public JGSubplotTests() => JG.Reset();

    [Fact]
    public void Subplot_CreatesGridCells()
    {
        JG.Figure();
        JG.Subplot(2, 1, 1);
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        JG.Subplot(2, 1, 2);
        JG.Plot(new double[] { 0, 1 }, new double[] { 1, 0 });

        Assert.Equal(2, JG.CurrentFigure.Axes.Count);
    }

    [Fact]
    public void Subplot_ReusesExistingCell()
    {
        JG.Figure();
        AxesModel first = JG.Subplot(2, 2, 1);
        AxesModel again = JG.Subplot(2, 2, 1);

        Assert.Same(first, again);
        Assert.Single(JG.CurrentFigure.Axes);
    }

    [Fact]
    public void Subplot_DirectsPlotsToSelectedCell()
    {
        JG.Figure();
        JG.Subplot(1, 2, 1);
        JG.Plot(new double[] { 0, 1 }, new double[] { 0, 1 });
        AxesModel cell2 = JG.Subplot(1, 2, 2);
        JG.Stem(new double[] { 0, 1 }, new double[] { 2, 3 });

        Assert.Single(cell2.Plots);
        Assert.IsType<StemPlot>(cell2.Plots[0]);
    }

    [Fact]
    public void Histogram_Facade_AddsHistogram()
    {
        JG.Figure();
        HistogramPlot hist = JG.Histogram(new double[] { 1, 2, 3, 4, 5 }, binCount: 5);
        Assert.Same(hist, JG.Gca().Plots[0]);
        Assert.Equal(5, hist.BinCount);
    }

    [Fact]
    public void Bar_CategoryFacade_SetsCategoryAxis()
    {
        JG.Figure();
        JG.Bar(new[] { "A", "B", "C" }, new double[] { 3, 5, 2 });
        Assert.Equal(AxisScaleType.Category, JG.Gca().PrimaryXAxis.Scale);
        Assert.Equal(3, JG.Gca().PrimaryXAxis.Categories!.Count);
    }

    [Fact]
    public void Image_Facade_AddsImage()
    {
        JG.Figure();
        ImagePlot image = JG.Image(new double[,] { { 1, 2 }, { 3, 4 } });
        Assert.Same(image, JG.Gca().Plots[0]);
    }

    [Fact]
    public void LinkAxes_LinksSubplots()
    {
        JG.Figure();
        AxesModel a = JG.Subplot(2, 1, 1);
        a.PrimaryXAxis.AutoScale = false;
        a.PrimaryXAxis.Range = new Core.Primitives.DataRange(0, 10);
        AxesModel b = JG.Subplot(2, 1, 2);
        b.PrimaryXAxis.AutoScale = false;
        b.PrimaryXAxis.Range = new Core.Primitives.DataRange(0, 10);

        using AxisLinkGroup group = JG.LinkAxes(AxisLinkMode.X, a, b);
        a.PrimaryXAxis.Range = new Core.Primitives.DataRange(2, 4);

        Assert.Equal(new Core.Primitives.DataRange(2, 4), b.PrimaryXAxis.Range);
    }
}
