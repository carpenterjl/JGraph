namespace JGraph.Core.Undo;

/// <summary>
/// An undoable action that can absorb a subsequent action into itself, so continuous gestures
/// (dragging a slider, live color preview) collapse into a single undo step. Merging is opt-in per
/// push site via <see cref="UndoStack.PushOrMerge"/>; discrete edits use <see cref="UndoStack.Push"/>
/// and are never merged.
/// </summary>
public interface IMergeableAction : IUndoableAction
{
    /// <summary>
    /// Attempts to absorb <paramref name="next"/> (an action that has already been applied) into this
    /// one. Returns true when merged; false when the actions are unrelated and <paramref name="next"/>
    /// must be pushed separately.
    /// </summary>
    bool TryMerge(IUndoableAction next);
}
