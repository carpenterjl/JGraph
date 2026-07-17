namespace JGraph.Core.Undo;

/// <summary>
/// A reversible operation recorded on the <see cref="UndoStack"/>. Actions are pushed after they have
/// already been applied, so <see cref="Redo"/> re-applies and <see cref="Undo"/> reverses. This
/// foundation is used for navigation (zoom/pan) now and extends to property edits and object moves in
/// later milestones.
/// </summary>
public interface IUndoableAction
{
    /// <summary>A short human-readable description (for menus/history).</summary>
    string Description { get; }

    /// <summary>Re-applies the action (used by redo).</summary>
    void Redo();

    /// <summary>Reverses the action.</summary>
    void Undo();
}
