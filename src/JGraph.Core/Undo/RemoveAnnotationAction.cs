using JGraph.Core.Model;

namespace JGraph.Core.Undo;

/// <summary>
/// An undoable removal of an annotation from its owning collection. Constructed after the annotation
/// has already been removed; undo re-inserts it at its original index. (Plot creation/removal is
/// deliberately not undoable — annotations are lightweight decorations, so deleting one accidentally
/// should be reversible.)
/// </summary>
public sealed class RemoveAnnotationAction : IUndoableAction
{
    private readonly GraphObjectCollection<AnnotationObject> _collection;
    private readonly AnnotationObject _annotation;
    private readonly int _index;

    /// <param name="collection">The collection the annotation was removed from.</param>
    /// <param name="annotation">The removed annotation.</param>
    /// <param name="index">The index the annotation occupied before removal.</param>
    public RemoveAnnotationAction(GraphObjectCollection<AnnotationObject> collection, AnnotationObject annotation, int index)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        _collection = collection;
        _annotation = annotation;
        _index = index;
    }

    /// <inheritdoc />
    public string Description => "Delete annotation";

    /// <inheritdoc />
    public void Redo() => _collection.Remove(_annotation);

    /// <inheritdoc />
    public void Undo() => _collection.Insert(System.Math.Min(_index, _collection.Count), _annotation);
}
