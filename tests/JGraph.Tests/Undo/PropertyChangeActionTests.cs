using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Undo;

public class PropertyChangeActionTests
{
    [Fact]
    public void UndoRedo_SwapOldAndNewValues()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit) { DisplayName = "after" };
        var action = new PropertyChangeAction(plot, nameof(PlotObject.DisplayName), "before", "after");

        action.Undo();
        Assert.Equal("before", plot.DisplayName);

        action.Redo();
        Assert.Equal("after", plot.DisplayName);
    }

    [Fact]
    public void Undo_RaisesModelNotifications()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit) { Opacity = 0.5 };
        var action = new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 1.0, 0.5);

        InvalidationKind? seen = null;
        plot.Invalidated += (_, e) => seen = e.Kind;

        action.Undo();
        Assert.Equal(1.0, plot.Opacity);
        Assert.Equal(InvalidationKind.Render, seen);
    }

    [Fact]
    public void Constructor_RejectsUnknownOrReadOnlyProperties()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        Assert.Throws<ArgumentException>(() => new PropertyChangeAction(plot, "NoSuchProperty", 1, 2));
        Assert.Throws<ArgumentException>(() => new PropertyChangeAction(plot, nameof(PlotObject.Axes), null, null));
    }

    [Fact]
    public void TryMerge_CombinesEditsOfSameProperty()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit) { Opacity = 0.8 };
        var first = new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 1.0, 0.9);
        var second = new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 0.9, 0.8);

        Assert.True(first.TryMerge(second));

        first.Undo();
        Assert.Equal(1.0, plot.Opacity);
        first.Redo();
        Assert.Equal(0.8, plot.Opacity);
    }

    [Fact]
    public void TryMerge_RejectsDifferentPropertyOrTarget()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        var other = new TestPlot(DataRange.Unit, DataRange.Unit);

        var action = new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 1.0, 0.5);
        Assert.False(action.TryMerge(new PropertyChangeAction(plot, nameof(PlotObject.DisplayName), "a", "b")));
        Assert.False(action.TryMerge(new PropertyChangeAction(other, nameof(PlotObject.Opacity), 1.0, 0.5)));
    }

    [Fact]
    public void PushOrMerge_CoalescesIntoOneUndoStep()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        var stack = new UndoStack();

        plot.Opacity = 0.9;
        stack.PushOrMerge(new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 1.0, 0.9));
        plot.Opacity = 0.7;
        stack.PushOrMerge(new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 0.9, 0.7));

        stack.Undo();
        Assert.Equal(1.0, plot.Opacity);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Push_KeepsDiscreteEditsSeparate()
    {
        var plot = new TestPlot(DataRange.Unit, DataRange.Unit);
        var stack = new UndoStack();

        plot.Opacity = 0.9;
        stack.Push(new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 1.0, 0.9));
        plot.Opacity = 0.7;
        stack.Push(new PropertyChangeAction(plot, nameof(PlotObject.Opacity), 0.9, 0.7));

        stack.Undo();
        Assert.Equal(0.9, plot.Opacity);
        Assert.True(stack.CanUndo);
    }
}
