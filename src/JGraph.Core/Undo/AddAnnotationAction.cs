using JGraph.Core.Model;

namespace JGraph.Core.Undo;

/// <summary>
/// An undoable addition of an annotation to its owning collection — the inverse twin of
/// <see cref="RemoveAnnotationAction"/>. Constructed after the annotation has already been added;
/// undo removes it, redo re-appends it.
/// </summary>
public sealed class AddAnnotationAction : IUndoableAction
{
    private readonly GraphObjectCollection<AnnotationObject> _collection;
    private readonly AnnotationObject _annotation;

    /// <param name="collection">The collection the annotation was added to.</param>
    /// <param name="annotation">The added annotation.</param>
    /// <param name="description">The undo-stack label, e.g. "Add data tip".</param>
    public AddAnnotationAction(GraphObjectCollection<AnnotationObject> collection, AnnotationObject annotation, string description = "Add annotation")
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(annotation);

        _collection = collection;
        _annotation = annotation;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public void Redo() => _collection.Add(_annotation);

    /// <inheritdoc />
    public void Undo() => _collection.Remove(_annotation);
}
