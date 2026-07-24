using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;

namespace JGraph.Interaction.Modes;

/// <summary>
/// The editing mode: click selects the object under the pointer (annotations first, then plots, then
/// the axes itself), dragging a selected annotation moves it, Delete removes a selected annotation
/// (undoably), and Escape cancels a drag or clears the selection. All geometry comes from the most
/// recent paint via <see cref="IInteractionSurface"/>; the mode never touches UI or rendering types.
/// </summary>
public sealed class EditMode : InteractionModeBase
{
    private const double PlotPickTolerancePixels = 8;
    private const double AnnotationPickTolerancePixels = 4;

    private AnnotationObject? _dragTarget;
    private ICoordinateMapper? _dragMapper;
    private Point2D _dragStartPixel;
    private Point2D[] _dragStartAnchors = Array.Empty<Point2D>();
    private bool _dragging;
    private bool _moved;
    private bool _suppressEscapeDeselect;

    private LegendModel? _legendTarget;
    private Rect2D _legendDragPlotArea;
    private Point2D _legendStartLocation;
    private LegendPosition _legendStartPosition;

    public override InteractionModeKind Kind => InteractionModeKind.Edit;

    public override InteractionCursor Cursor => InteractionCursor.Arrow;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        _suppressEscapeDeselect = false;
        GraphObject? hit = HitTest(controller, e.Position);
        controller.Selection.Select(hit);

        if (hit is AnnotationObject annotation && MapperFor(controller.Surface, annotation) is { } mapper)
        {
            _dragTarget = annotation;
            _dragMapper = mapper;
            _dragStartPixel = e.Position;
            _dragStartAnchors = annotation.GetAnchorPoints().ToArray();
            _dragging = true;
            _moved = false;
        }
        else if (hit is LegendModel legend
            && legend.Parent is AxesModel legendAxes
            && controller.Surface.TryGetAxesAt(e.Position, out _, out _, out Rect2D plotArea)
            && plotArea.Width > 0
            && plotArea.Height > 0)
        {
            _legendTarget = legend;
            _legendDragPlotArea = plotArea;
            _legendStartPosition = legend.Position;

            // Start from where the legend is actually drawn, so switching from a preset to a custom
            // placement does not make the box jump on the first pixel of the drag.
            _legendStartLocation = controller.Surface.GetLegendBounds(legendAxes) is { } box
                ? new Point2D((box.Left - plotArea.Left) / plotArea.Width, (box.Top - plotArea.Top) / plotArea.Height)
                : legend.Location;

            _dragStartPixel = e.Position;
            _dragging = true;
            _moved = false;
        }
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (_dragging && _legendTarget is not null)
        {
            // Re-derive from the gesture start (never accumulate), in plot-area fractions.
            Vector2D delta = e.Position - _dragStartPixel;
            _legendTarget.Position = LegendPosition.Custom;
            _legendTarget.Location = new Point2D(
                _legendStartLocation.X + (delta.X / _legendDragPlotArea.Width),
                _legendStartLocation.Y + (delta.Y / _legendDragPlotArea.Height));
            _moved = true;
            return;
        }

        if (!_dragging || _dragTarget is null || _dragMapper is null)
        {
            return;
        }

        // Re-derive from the gesture-start anchors each move so the drag never accumulates error.
        Vector2D totalDelta = e.Position - _dragStartPixel;
        var moved = new Point2D[_dragStartAnchors.Length];
        for (int i = 0; i < _dragStartAnchors.Length; i++)
        {
            moved[i] = AnnotationObject.ShiftByPixels(_dragStartAnchors[i], totalDelta, _dragMapper);
        }

        _dragTarget.SetAnchorPoints(moved);
        _moved = true;
    }

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
        if (_dragging && _moved && _legendTarget is not null)
        {
            // Placement and the position mode changed together, so they undo together.
            controller.Surface.UndoStack.Push(new CompositeAction(
                "Move legend",
                new PropertyChangeAction(
                    _legendTarget,
                    nameof(LegendModel.Position),
                    _legendStartPosition,
                    _legendTarget.Position),
                new PropertyChangeAction(
                    _legendTarget,
                    nameof(LegendModel.Location),
                    _legendStartLocation,
                    _legendTarget.Location)));
        }

        if (_dragging && _moved && _dragTarget is not null)
        {
            IReadOnlyList<Point2D> after = _dragTarget.GetAnchorPoints();
            if (!AnchorsEqual(_dragStartAnchors, after))
            {
                controller.Surface.UndoStack.Push(new MoveAnnotationAction(_dragTarget, _dragStartAnchors, after));
            }
        }

        ResetDrag();
    }

    public override void OnKey(InteractionController controller, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case InteractionKey.Escape:
                // Cancel() already ran (the controller cancels before dispatching Escape); if it
                // aborted a drag, this Escape is consumed and must not also clear the selection.
                if (_suppressEscapeDeselect)
                {
                    _suppressEscapeDeselect = false;
                }
                else
                {
                    controller.Selection.Clear();
                }

                break;

            case InteractionKey.Delete:
                DeleteSelectedAnnotation(controller);
                break;
        }
    }

    public override void Cancel(InteractionController controller)
    {
        if (_dragging && _dragTarget is not null)
        {
            _dragTarget.SetAnchorPoints(_dragStartAnchors);
            _suppressEscapeDeselect = _moved;
        }
        else if (_dragging && _legendTarget is not null)
        {
            _legendTarget.Location = _legendStartLocation;
            _legendTarget.Position = _legendStartPosition;
            _suppressEscapeDeselect = _moved;
        }

        ResetDrag();
    }

    private static void DeleteSelectedAnnotation(InteractionController controller)
    {
        if (controller.Selection.Selected is not AnnotationObject annotation)
        {
            return;
        }

        GraphObjectCollection<AnnotationObject>? collection = annotation.Parent switch
        {
            AxesModel axes => axes.Annotations,
            FigureModel figure => figure.Annotations,
            _ => null,
        };

        if (collection is null)
        {
            return;
        }

        int index = collection.IndexOf(annotation);
        if (index < 0)
        {
            return;
        }

        controller.Selection.Clear();
        collection.RemoveAt(index);
        controller.Surface.UndoStack.Push(new RemoveAnnotationAction(collection, annotation, index));
    }

    /// <summary>
    /// Finds the topmost selectable object under a pixel: figure annotations (drawn last, so checked
    /// first), then the annotations and plots of the axes under the pixel, then the axes itself.
    /// </summary>
    private static GraphObject? HitTest(InteractionController controller, Point2D pixel)
    {
        IInteractionSurface surface = controller.Surface;

        if (surface.DefaultAxes?.Parent is FigureModel figure)
        {
            AnnotationObject? figureHit = HitTestAnnotations(figure.Annotations, pixel);
            if (figureHit is not null)
            {
                return figureHit;
            }
        }

        if (!surface.TryGetAxesAt(pixel, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            return null;
        }

        // The legend is drawn over everything in the plot area, so it is picked before them.
        if (axes.Legend.Visible
            && axes.Legend.Selectable
            && surface.GetLegendBounds(axes) is { } legendBox
            && legendBox.Contains(pixel))
        {
            return axes.Legend;
        }

        AnnotationObject? annotationHit = HitTestAnnotations(axes.Annotations, pixel);
        if (annotationHit is not null)
        {
            return annotationHit;
        }

        PlotHitResult? best = null;
        foreach (PlotObject plot in axes.Plots)
        {
            if (!plot.Visible || !plot.Selectable)
            {
                continue;
            }

            PlotHitResult? hit = plot.HitTest(pixel, mapper, PlotPickTolerancePixels);
            if (hit is not null && (best is null || hit.DistancePixels < best.DistancePixels))
            {
                best = hit;
            }
        }

        if (best is not null)
        {
            return best.Target;
        }

        return axes.Selectable ? axes : null;
    }

    private static AnnotationObject? HitTestAnnotations(GraphObjectCollection<AnnotationObject> annotations, Point2D pixel)
    {
        // Topmost first: reverse draw order.
        foreach (AnnotationObject annotation in annotations.InDrawOrder().Reverse())
        {
            if (annotation.Visible && annotation.Selectable && annotation.HitTest(pixel, AnnotationPickTolerancePixels))
            {
                return annotation;
            }
        }

        return null;
    }

    /// <summary>Returns the mapper matching an annotation's coordinate space, from the last paint.</summary>
    private static ICoordinateMapper? MapperFor(IInteractionSurface surface, AnnotationObject annotation)
    {
        if (annotation.Space == AnnotationSpace.Figure)
        {
            return surface.FigureMapper;
        }

        return annotation.Parent is AxesModel axes ? surface.GetMapper(axes) : null;
    }

    private static bool AnchorsEqual(IReadOnlyList<Point2D> a, IReadOnlyList<Point2D> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ResetDrag()
    {
        _dragging = false;
        _moved = false;
        _dragTarget = null;
        _dragMapper = null;
        _dragStartAnchors = Array.Empty<Point2D>();
        _legendTarget = null;
    }
}
