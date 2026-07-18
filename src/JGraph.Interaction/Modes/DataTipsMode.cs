using JGraph.Core.Model;
using JGraph.Core.Undo;
using JGraph.Objects.Annotations;

namespace JGraph.Interaction.Modes;

/// <summary>
/// The Data Tips tool (M21, formerly the transient data cursor): each click places a
/// <see cref="DataTipAnnotation"/> on the nearest data point, replacing the tip THIS tool placed
/// last — a roving readout. Tips placed by the default pointer are never touched; to keep a tip
/// this tool placed, switch back to the pointer. Placement/replacement is one undo step.
/// </summary>
public sealed class DataTipsMode : InteractionModeBase
{
    private DataTipAnnotation? _lastPlaced;

    public override InteractionModeKind Kind => InteractionModeKind.DataTips;

    public override InteractionCursor Cursor => InteractionCursor.Cross;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        if (DataTipPlacement.FindPoint(controller, e.Position) is not { } found)
        {
            return;
        }

        DataTipAnnotation tip = DataTipPlacement.CreateTip(found.Mapper, found.Hit);

        // Replace the tip this tool placed last (when it is still in a collection).
        GraphObjectCollection<AnnotationObject>? previousHome =
            _lastPlaced?.Parent is AxesModel previousAxes && previousAxes.Annotations.Contains(_lastPlaced)
                ? previousAxes.Annotations
                : null;
        DataTipAnnotation? replaced = previousHome is null ? null : _lastPlaced;
        previousHome?.Remove(replaced!);

        found.Axes.Annotations.Add(tip);
        controller.Surface.UndoStack.Push(
            new PlaceDataTipAction(found.Axes.Annotations, tip, previousHome, replaced));
        controller.Selection.Select(tip);
        _lastPlaced = tip;
        controller.Surface.RequestRender();
    }

    /// <summary>One undo step covering "remove the roving tip's previous position, add the new one".</summary>
    private sealed class PlaceDataTipAction : IUndoableAction
    {
        private readonly GraphObjectCollection<AnnotationObject> _addedTo;
        private readonly DataTipAnnotation _added;
        private readonly GraphObjectCollection<AnnotationObject>? _removedFrom;
        private readonly DataTipAnnotation? _removed;

        public PlaceDataTipAction(
            GraphObjectCollection<AnnotationObject> addedTo,
            DataTipAnnotation added,
            GraphObjectCollection<AnnotationObject>? removedFrom,
            DataTipAnnotation? removed)
        {
            _addedTo = addedTo;
            _added = added;
            _removedFrom = removedFrom;
            _removed = removed;
        }

        public string Description => "Place data tip";

        public void Redo()
        {
            if (_removed is not null)
            {
                _removedFrom!.Remove(_removed);
            }

            _addedTo.Add(_added);
        }

        public void Undo()
        {
            _addedTo.Remove(_added);
            if (_removed is not null)
            {
                _removedFrom!.Add(_removed);
            }
        }
    }
}
