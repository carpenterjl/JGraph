using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>Rubber-band zoom: drag a rectangle and the view zooms to fit it.</summary>
public sealed class RectangleZoomMode : InteractionModeBase
{
    private const double MinDragPixels = 4;

    private bool _active;
    private AxesModel? _axes;
    private ICoordinateMapper? _mapper;
    private Rect2D _plotArea;
    private Point2D _start;
    private AxesViewState? _before;

    public override InteractionModeKind Kind => InteractionModeKind.RectangleZoom;

    public override InteractionCursor Cursor => InteractionCursor.Cross;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        if (!controller.Surface.TryGetAxesAt(e.Position, out AxesModel axes, out ICoordinateMapper mapper, out Rect2D plotArea))
        {
            return;
        }

        // A pixel rectangle has no meaning in a projected 3D view; use the wheel dolly instead.
        if (axes.Is3D)
        {
            return;
        }

        _axes = axes;
        _mapper = mapper;
        _plotArea = plotArea;
        _start = e.Position;
        _before = AxesViewState.Capture(axes);
        _active = true;
        controller.SetRubberBand(new Rect2D(_start.X, _start.Y, 0, 0));
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (!_active)
        {
            return;
        }

        Point2D clamped = ClampToPlot(e.Position);
        controller.SetRubberBand(Rect2D.FromCorners(_start, clamped));
    }

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
        if (!_active || _axes is null || _mapper is null || _before is null)
        {
            return;
        }

        Rect2D rect = Rect2D.FromCorners(_start, ClampToPlot(e.Position));
        controller.SetRubberBand(null);

        if (rect.Width >= MinDragPixels && rect.Height >= MinDragPixels)
        {
            Navigation.ZoomToRect(_axes, _mapper, rect);
            controller.CommitViewChange(_axes, _before, "Zoom to rectangle");
        }

        Reset();
    }

    public override void Cancel(InteractionController controller)
    {
        if (_active)
        {
            controller.SetRubberBand(null);
        }

        Reset();
    }

    private Point2D ClampToPlot(Point2D p) => new(
        System.Math.Clamp(p.X, _plotArea.Left, _plotArea.Right),
        System.Math.Clamp(p.Y, _plotArea.Top, _plotArea.Bottom));

    private void Reset()
    {
        _active = false;
        _axes = null;
        _mapper = null;
        _before = null;
    }
}
