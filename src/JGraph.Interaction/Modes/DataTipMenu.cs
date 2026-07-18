using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Objects.Annotations;

namespace JGraph.Interaction.Modes;

/// <summary>
/// The data-tip entries of the plot surface's right-click menu, available in every mode: delete the
/// tip under the cursor, or every tip on the axes — both undoably.
/// </summary>
internal static class DataTipMenu
{
    public static void AddItems(InteractionController controller, Point2D pixel, IList<ContextMenuItem> items)
    {
        if (!controller.Surface.TryGetAxesAt(pixel, out AxesModel axes, out _, out _))
        {
            return;
        }

        if (PointerMode.HitTip(controller, pixel) is { } hitTip)
        {
            items.Add(new ContextMenuItem("Delete This Data Tip", () => Delete(controller, axes, hitTip)));
        }

        if (HasTips(axes))
        {
            items.Add(new ContextMenuItem("Delete All Data Tips", () => DeleteAll(controller, axes)));
        }
    }

    private static bool HasTips(AxesModel axes)
    {
        foreach (AnnotationObject annotation in axes.Annotations)
        {
            if (annotation is DataTipAnnotation)
            {
                return true;
            }
        }

        return false;
    }

    private static void Delete(InteractionController controller, AxesModel axes, DataTipAnnotation tip)
    {
        int index = axes.Annotations.IndexOf(tip);
        if (index < 0)
        {
            return;
        }

        if (ReferenceEquals(controller.Selection.Selected, tip))
        {
            controller.Selection.Clear();
        }

        axes.Annotations.RemoveAt(index);
        controller.Surface.UndoStack.Push(new RemoveAnnotationAction(axes.Annotations, tip, index));
        controller.Surface.RequestRender();
    }

    private static void DeleteAll(InteractionController controller, AxesModel axes)
    {
        // Delete back to front so each removal's recorded index stays valid on undo (they are
        // re-inserted in reverse order of removal).
        for (int i = axes.Annotations.Count - 1; i >= 0; i--)
        {
            if (axes.Annotations[i] is DataTipAnnotation tip)
            {
                Delete(controller, axes, tip);
            }
        }
    }
}
