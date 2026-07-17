using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Core.Undo;

/// <summary>
/// An undoable move of an annotation: the anchor points captured before and after a drag gesture.
/// One gesture produces one action, mirroring how navigation records one action per zoom/pan.
/// </summary>
public sealed class MoveAnnotationAction : IUndoableAction
{
    private readonly AnnotationObject _annotation;
    private readonly Point2D[] _before;
    private readonly Point2D[] _after;

    public MoveAnnotationAction(AnnotationObject annotation, IReadOnlyList<Point2D> before, IReadOnlyList<Point2D> after)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        _annotation = annotation;
        _before = before.ToArray();
        _after = after.ToArray();
    }

    /// <inheritdoc />
    public string Description => "Move annotation";

    /// <inheritdoc />
    public void Redo() => _annotation.SetAnchorPoints(_after);

    /// <inheritdoc />
    public void Undo() => _annotation.SetAnchorPoints(_before);
}
