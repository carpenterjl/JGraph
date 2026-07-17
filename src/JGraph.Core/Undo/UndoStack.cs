namespace JGraph.Core.Undo;

/// <summary>
/// A two-stack undo/redo history. Callers apply a change, then <see cref="Push"/> a matching action to
/// record it; pushing clears the redo history. <see cref="Undo"/> and <see cref="Redo"/> move actions
/// between the stacks and invoke them. Raises <see cref="StateChanged"/> so UI can update command
/// availability.
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();
    private readonly int _capacity;

    public UndoStack(int capacity = 200)
    {
        _capacity = System.Math.Max(1, capacity);
    }

    /// <summary>Raised whenever the availability of undo/redo may have changed.</summary>
    public event EventHandler? StateChanged;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Description of the next undo action, or null if none.</summary>
    public string? NextUndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;

    /// <summary>Description of the next redo action, or null if none.</summary>
    public string? NextRedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    /// <summary>Records an already-applied action and clears the redo history.</summary>
    public void Push(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _undo.Push(action);
        _redo.Clear();
        TrimToCapacity();
        OnStateChanged();
    }

    /// <summary>
    /// Records an already-applied action, first offering it to the most recent action for merging
    /// (see <see cref="IMergeableAction"/>) so continuous gestures coalesce into one undo step.
    /// </summary>
    public void PushOrMerge(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_undo.Count > 0 && _undo.Peek() is IMergeableAction mergeable && mergeable.TryMerge(action))
        {
            _redo.Clear();
            OnStateChanged();
            return;
        }

        Push(action);
    }

    /// <summary>Undoes the most recent action, if any.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        IUndoableAction action = _undo.Pop();
        action.Undo();
        _redo.Push(action);
        OnStateChanged();
    }

    /// <summary>Redoes the most recently undone action, if any.</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        IUndoableAction action = _redo.Pop();
        action.Redo();
        _undo.Push(action);
        OnStateChanged();
    }

    /// <summary>Clears both histories.</summary>
    public void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        OnStateChanged();
    }

    private void TrimToCapacity()
    {
        if (_undo.Count <= _capacity)
        {
            return;
        }

        // Rebuild keeping the most recent _capacity actions (oldest at the bottom is dropped).
        IUndoableAction[] items = _undo.ToArray(); // index 0 = newest
        _undo.Clear();
        for (int i = System.Math.Min(items.Length, _capacity) - 1; i >= 0; i--)
        {
            _undo.Push(items[i]);
        }
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
