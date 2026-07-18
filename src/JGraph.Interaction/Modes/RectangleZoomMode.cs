using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>How a rectangle-zoom drag is constrained (MATLAB's zoom xon/yon options).</summary>
public enum RectangleZoomConstraint
{
    /// <summary>The drag rectangle zooms both axes (the default).</summary>
    Unconstrained,

    /// <summary>Only X zooms: the drag spans the plot's full height, Y stays untouched.</summary>
    Horizontal,

    /// <summary>Only Y zooms: the drag spans the plot's full width, X stays untouched.</summary>
    Vertical,
}

/// <summary>
/// Rubber-band zoom: drag a rectangle and the view zooms to fit it. The <see cref="Constraint"/>
/// (set from the plot's right-click menu) can limit the zoom to one axis — the rubber band then
/// spans the other dimension entirely, so what you see is exactly what you get.
/// </summary>
public sealed class RectangleZoomMode : InteractionModeBase, IContextMenuSource
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

    /// <summary>The active drag constraint.</summary>
    public RectangleZoomConstraint Constraint { get; set; } = RectangleZoomConstraint.Unconstrained;

    /// <inheritdoc />
    public void AddContextMenuItems(InteractionController controller, Point2D pixel, IList<ContextMenuItem> items)
    {
        void Choice(string header, RectangleZoomConstraint constraint) =>
            items.Add(new ContextMenuItem(header, () => Constraint = constraint, Constraint == constraint));

        Choice("Unconstrained Zoom", RectangleZoomConstraint.Unconstrained);
        Choice("Horizontal Zoom", RectangleZoomConstraint.Horizontal);
        Choice("Vertical Zoom", RectangleZoomConstraint.Vertical);
    }

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
        controller.SetRubberBand(BuildRect(e.Position));
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (!_active)
        {
            return;
        }

        controller.SetRubberBand(BuildRect(e.Position));
    }

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
        if (!_active || _axes is null || _mapper is null || _before is null)
        {
            return;
        }

        Rect2D rect = BuildRect(e.Position);
        controller.SetRubberBand(null);

        // The minimum-drag check applies only to the dimension(s) the user actually drags.
        bool bigEnough = Constraint switch
        {
            RectangleZoomConstraint.Horizontal => rect.Width >= MinDragPixels,
            RectangleZoomConstraint.Vertical => rect.Height >= MinDragPixels,
            _ => rect.Width >= MinDragPixels && rect.Height >= MinDragPixels,
        };

        if (bigEnough)
        {
            DataRange keepX = _axes.PrimaryXAxis.Range;
            DataRange keepY = _axes.PrimaryYAxis.Range;
            bool keepXAuto = _axes.PrimaryXAxis.AutoScale;
            bool keepYAuto = _axes.PrimaryYAxis.AutoScale;

            Navigation.ZoomToRect(_axes, _mapper, rect);

            // A full-height/width band leaves the free axis' range identical up to pixel rounding;
            // restore it exactly so a constrained zoom never drifts the other axis.
            if (Constraint == RectangleZoomConstraint.Horizontal)
            {
                _axes.PrimaryYAxis.AutoScale = keepYAuto;
                _axes.PrimaryYAxis.Range = keepY;
            }
            else if (Constraint == RectangleZoomConstraint.Vertical)
            {
                _axes.PrimaryXAxis.AutoScale = keepXAuto;
                _axes.PrimaryXAxis.Range = keepX;
            }

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

    /// <summary>The drag rectangle under the constraint: full-height for Horizontal, full-width for Vertical.</summary>
    private Rect2D BuildRect(Point2D current)
    {
        Point2D clamped = ClampToPlot(current);
        return Constraint switch
        {
            RectangleZoomConstraint.Horizontal => Rect2D.FromCorners(
                new Point2D(_start.X, _plotArea.Top), new Point2D(clamped.X, _plotArea.Bottom)),
            RectangleZoomConstraint.Vertical => Rect2D.FromCorners(
                new Point2D(_plotArea.Left, _start.Y), new Point2D(_plotArea.Right, clamped.Y)),
            _ => Rect2D.FromCorners(_start, clamped),
        };
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
