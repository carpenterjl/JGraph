using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Objects.Annotations;

namespace JGraph.Interaction.Modes;

/// <summary>
/// The default pointer (M21): drag pans a 2D axes (or rotates a 3D one), hovering near a data point
/// shows a crosshair, and a click on a point places a persistent <see cref="DataTipAnnotation"/> —
/// undoably. Clicking an existing tip's label selects it; dragging the label moves it while the pin
/// stays on the data point. All without switching tools, like MATLAB's default mouse behaviors.
/// </summary>
public sealed class PointerMode : InteractionModeBase
{
    /// <summary>Movement below this is a click; at or above it the gesture becomes a pan.</summary>
    private const double DragThresholdPixels = 4;

    private readonly PanDragGesture _pan = new();
    private bool _armed;
    private Point2D _downPixel;
    private bool _hoverNearPoint;

    // Label-drag state (mirrors EditMode's annotation drag).
    private DataTipAnnotation? _dragTip;
    private ICoordinateMapper? _dragMapper;
    private Point2D[] _dragStartAnchors = Array.Empty<Point2D>();
    private bool _draggingLabel;
    private bool _labelMoved;

    public override InteractionModeKind Kind => InteractionModeKind.Pointer;

    /// <summary>Dynamic: a hand while dragging, a crosshair near a pickable point, otherwise an arrow.</summary>
    public override InteractionCursor Cursor =>
        _pan.Active ? InteractionCursor.Hand
        : _hoverNearPoint ? InteractionCursor.Cross
        : InteractionCursor.Arrow;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        _downPixel = e.Position;

        // An existing tip's label takes priority: click selects, drag moves it.
        if (HitTip(controller, e.Position) is { } tip
            && MapperFor(controller, tip) is { } mapper)
        {
            controller.Selection.Select(tip);
            _dragTip = tip;
            _dragMapper = mapper;
            _dragStartAnchors = tip.GetAnchorPoints().ToArray();
            _draggingLabel = true;
            _labelMoved = false;
            return;
        }

        _armed = true; // pan or place — decided by whether movement crosses the threshold
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (_draggingLabel && _dragTip is not null && _dragMapper is not null)
        {
            Vector2D delta = e.Position - _downPixel;
            var moved = new Point2D[_dragStartAnchors.Length];
            for (int i = 0; i < _dragStartAnchors.Length; i++)
            {
                moved[i] = AnnotationObject.ShiftByPixels(_dragStartAnchors[i], delta, _dragMapper);
            }

            _dragTip.SetAnchorPoints(moved);
            _labelMoved = true;
            controller.Surface.RequestRender();
            return;
        }

        if (_pan.Active)
        {
            _pan.Move(controller, e.Position);
            return;
        }

        if (_armed)
        {
            if (Distance(_downPixel, e.Position) >= DragThresholdPixels && _pan.Begin(controller, _downPixel))
            {
                _armed = false;
                _pan.Move(controller, e.Position);
                controller.NotifyCursorChanged();
            }

            return;
        }

        // Idle hover: crosshair near a pickable point (2D only), arrow otherwise.
        bool near = HitTip(controller, e.Position) is not null
            || DataTipPlacement.FindPoint(controller, e.Position) is not null;
        if (near != _hoverNearPoint)
        {
            _hoverNearPoint = near;
            controller.NotifyCursorChanged();
        }
    }

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
        if (_draggingLabel)
        {
            if (_labelMoved && _dragTip is not null)
            {
                IReadOnlyList<Point2D> after = _dragTip.GetAnchorPoints();
                controller.Surface.UndoStack.Push(new MoveAnnotationAction(_dragTip, _dragStartAnchors, after));
            }

            ResetLabelDrag();
            return;
        }

        if (_pan.Active)
        {
            _pan.End(controller);
            controller.NotifyCursorChanged();
            return;
        }

        if (!_armed)
        {
            return;
        }

        _armed = false;

        // A genuine click: place a persistent data tip on the nearest point, undoably.
        if (DataTipPlacement.FindPoint(controller, e.Position) is not { } found)
        {
            controller.Selection.Clear();
            return;
        }

        DataTipAnnotation tip = DataTipPlacement.CreateTip(found.Mapper, found.Hit);
        found.Axes.Annotations.Add(tip);
        controller.Surface.UndoStack.Push(new AddAnnotationAction(found.Axes.Annotations, tip, "Add data tip"));
        controller.Selection.Select(tip);
        controller.Surface.RequestRender();
    }

    public override void Cancel(InteractionController controller)
    {
        if (_draggingLabel && _dragTip is not null)
        {
            _dragTip.SetAnchorPoints(_dragStartAnchors);
            controller.Surface.RequestRender();
        }

        _pan.Cancel(controller);
        ResetLabelDrag();
        _armed = false;
    }

    /// <summary>The topmost data-tip label under a pixel, if any.</summary>
    internal static DataTipAnnotation? HitTip(InteractionController controller, Point2D pixel)
    {
        if (!controller.Surface.TryGetAxesAt(pixel, out AxesModel axes, out _, out _))
        {
            return null;
        }

        foreach (AnnotationObject annotation in axes.Annotations.InDrawOrder().Reverse())
        {
            if (annotation is DataTipAnnotation tip && tip.Visible && tip.HitTest(pixel, 2))
            {
                return tip;
            }
        }

        return null;
    }

    private static ICoordinateMapper? MapperFor(InteractionController controller, AnnotationObject annotation) =>
        annotation.Parent is AxesModel axes ? controller.Surface.GetMapper(axes) : null;

    private static double Distance(Point2D a, Point2D b) =>
        System.Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private void ResetLabelDrag()
    {
        _draggingLabel = false;
        _labelMoved = false;
        _dragTip = null;
        _dragMapper = null;
        _dragStartAnchors = Array.Empty<Point2D>();
    }
}
