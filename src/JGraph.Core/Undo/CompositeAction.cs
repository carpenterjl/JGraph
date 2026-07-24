namespace JGraph.Core.Undo;

/// <summary>
/// Several actions treated as one history entry: <see cref="Redo"/> re-applies them in order and
/// <see cref="Undo"/> reverses them back to front, so a gesture that had to change more than one
/// property costs the user exactly one Undo. Dragging the legend is the first such gesture — it sets
/// both the location and the "custom placement" flag.
/// </summary>
public sealed class CompositeAction : IUndoableAction
{
    private readonly IUndoableAction[] _actions;

    public CompositeAction(string description, params IUndoableAction[] actions)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Length == 0)
        {
            throw new ArgumentException("A composite action needs at least one action.", nameof(actions));
        }

        Description = description;
        _actions = actions;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public void Redo()
    {
        foreach (IUndoableAction action in _actions)
        {
            action.Redo();
        }
    }

    /// <inheritdoc />
    public void Undo()
    {
        for (int i = _actions.Length - 1; i >= 0; i--)
        {
            _actions[i].Undo();
        }
    }
}
