using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Interaction;

public class SelectionManagerTests
{
    [Fact]
    public void Select_SetsAndClearsIsSelectedFlags()
    {
        var manager = new SelectionManager();
        var first = new TestPlot(DataRange.Unit, DataRange.Unit);
        var second = new TestPlot(DataRange.Unit, DataRange.Unit);

        manager.Select(first);
        Assert.True(first.IsSelected);
        Assert.Same(first, manager.Selected);

        manager.Select(second);
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);

        manager.Clear();
        Assert.False(second.IsSelected);
        Assert.Null(manager.Selected);
    }

    [Fact]
    public void Select_RaisesSelectionChangedOnceAndOnlyOnChange()
    {
        var manager = new SelectionManager();
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);

        var events = new List<GraphObject?>();
        manager.SelectionChanged += (_, selected) => events.Add(selected);

        manager.Select(plot);
        manager.Select(plot); // no-op
        manager.Clear();
        manager.Clear();      // no-op

        Assert.Equal(new GraphObject?[] { plot, null }, events);
    }
}
