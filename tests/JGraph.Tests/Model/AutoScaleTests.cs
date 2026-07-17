using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Model;

public class AutoScaleTests
{
    [Fact]
    public void RecomputeDataBounds_UnionsPlotExtents()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AutoScalePadding = 0; // exact bounds for assertions
        axes.Plots.Add(new TestPlot(new DataRange(0, 5), new DataRange(-1, 2)));
        axes.Plots.Add(new TestPlot(new DataRange(3, 10), new DataRange(0, 8)));

        axes.RecomputeDataBounds();

        Assert.Equal(new DataRange(0, 10), axes.PrimaryXAxis.Range);
        Assert.Equal(new DataRange(-1, 8), axes.PrimaryYAxis.Range);
    }

    [Fact]
    public void RecomputeDataBounds_AppliesPadding()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AutoScalePadding = 0.1;
        axes.Plots.Add(new TestPlot(new DataRange(0, 10), new DataRange(0, 10)));

        axes.RecomputeDataBounds();

        Assert.Equal(-1, axes.PrimaryXAxis.Range.Min, 6);
        Assert.Equal(11, axes.PrimaryXAxis.Range.Max, 6);
    }

    [Fact]
    public void RecomputeDataBounds_IgnoresManualAxis()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(-100, 100);
        axes.Plots.Add(new TestPlot(new DataRange(0, 5), new DataRange(0, 5)));

        axes.RecomputeDataBounds();

        // Manual axis keeps its range; its data bounds still update for reference.
        Assert.Equal(new DataRange(-100, 100), axes.PrimaryYAxis.Range);
        Assert.Equal(new DataRange(0, 5), axes.PrimaryYAxis.DataBounds);
    }

    [Fact]
    public void RecomputeDataBounds_HiddenPlotExcluded()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AutoScalePadding = 0;
        var hidden = new TestPlot(new DataRange(100, 200), new DataRange(100, 200)) { Visible = false };
        axes.Plots.Add(hidden);
        axes.Plots.Add(new TestPlot(new DataRange(0, 5), new DataRange(0, 5)));

        axes.RecomputeDataBounds();

        Assert.Equal(new DataRange(0, 5), axes.PrimaryXAxis.Range);
    }
}
