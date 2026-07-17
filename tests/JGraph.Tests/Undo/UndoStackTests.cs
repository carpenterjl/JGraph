using JGraph.Core.Undo;
using Xunit;

namespace JGraph.Tests.Undo;

public class UndoStackTests
{
    private sealed class CounterAction : IUndoableAction
    {
        private readonly Action _redo;
        private readonly Action _undo;

        public CounterAction(Action redo, Action undo)
        {
            _redo = redo;
            _undo = undo;
        }

        public string Description => "counter";

        public void Redo() => _redo();

        public void Undo() => _undo();
    }

    [Fact]
    public void Push_EnablesUndo_ClearsRedo()
    {
        var stack = new UndoStack();
        Assert.False(stack.CanUndo);

        stack.Push(new CounterAction(() => { }, () => { }));
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoRedo_InvokeActionsAndMoveBetweenStacks()
    {
        int value = 0;
        var stack = new UndoStack();
        value = 5;
        stack.Push(new CounterAction(() => value = 5, () => value = 0));

        stack.Undo();
        Assert.Equal(0, value);
        Assert.True(stack.CanRedo);

        stack.Redo();
        Assert.Equal(5, value);
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedoHistory()
    {
        var stack = new UndoStack();
        stack.Push(new CounterAction(() => { }, () => { }));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Push(new CounterAction(() => { }, () => { }));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void StateChanged_RaisedOnMutations()
    {
        var stack = new UndoStack();
        int events = 0;
        stack.StateChanged += (_, _) => events++;

        stack.Push(new CounterAction(() => { }, () => { }));
        stack.Undo();
        stack.Redo();

        Assert.Equal(3, events);
    }

    [Fact]
    public void Capacity_DropsOldestActions()
    {
        var stack = new UndoStack(capacity: 3);
        for (int i = 0; i < 5; i++)
        {
            stack.Push(new CounterAction(() => { }, () => { }));
        }

        int undone = 0;
        while (stack.CanUndo)
        {
            stack.Undo();
            undone++;
        }

        Assert.Equal(3, undone);
    }
}
