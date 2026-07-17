using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Model;

public class InvalidationTests
{
    [Fact]
    public void PropertyChange_BubblesToFigureRootWithSourceAndKind()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        var plot = new TestPlot(new DataRange(0, 1), new DataRange(0, 1));
        axes.Plots.Add(plot);

        InvalidatedEventArgs? captured = null;
        figure.Invalidated += (_, e) => captured = e;

        plot.Visible = false;

        Assert.NotNull(captured);
        Assert.Same(plot, captured!.Source);
        Assert.Equal(InvalidationKind.Render, captured.Kind);
    }

    [Fact]
    public void AddingChild_RaisesStructureInvalidation()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        var kinds = new List<InvalidationKind>();
        figure.Invalidated += (_, e) => kinds.Add(e.Kind);

        axes.Plots.Add(new TestPlot(DataRange.Unit, DataRange.Unit));

        Assert.Contains(InvalidationKind.Structure, kinds);
    }

    [Fact]
    public void Collection_SetsAndClearsParent()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);

        axes.Plots.Add(plot);
        Assert.Same(axes, plot.Parent);
        Assert.Same(axes, plot.Axes);

        axes.Plots.Remove(plot);
        Assert.Null(plot.Parent);
    }

    [Fact]
    public void NoneKindProperty_DoesNotInvalidate()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        axes.Plots.Add(plot);

        bool raised = false;
        figure.Invalidated += (_, _) => raised = true;

        plot.Tag = "abc"; // Tag uses InvalidationKind.None

        Assert.False(raised);
    }

    [Fact]
    public void PropertyChanged_FiresForEditableProperty()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        var changed = new List<string?>();
        plot.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        plot.DisplayName = "Signal";

        Assert.Contains(nameof(PlotObject.DisplayName), changed);
    }
}
