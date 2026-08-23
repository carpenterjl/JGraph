using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>
/// The shared pan/rotate drag mechanics: on a 2D axes the grabbed data point follows the cursor; on
/// a 3D axes the drag rotates the camera. Used by <see cref="PanMode"/> and <see cref="PointerMode"/>
/// so the default pointer pans exactly like the dedicated tool.
/// </summary>
internal sealed class PanDragGesture
{
    /// <summary>Camera degrees per pixel of drag.</summary>
    private const double RotateSpeed = 0.4;

    private bool _rotating;
    private AxesModel? _axes;
    private ICoordinateMapper? _startMapper;
    private DataRange _startX;
    private DataRange _startY;
    private Point2D _startPixel;
    private double _startAzimuth;
    private double _startElevation;
    private AxesViewState? _before;
    private InteractionDimensions _dimensions = InteractionDimensions.XY;

    /// <summary>Whether a drag is in progress.</summary>
    public bool Active { get; private set; }

    /// <summary>
    /// Starts a drag at <paramref name="position"/>; false when no axes sits under it, and false as
    /// well when the axes there does not answer to the gesture this drag would be (M80) — a pan on
    /// flat paper, a rotate in space. Refusing here rather than part-way through is what lets the
    /// pointer fall back to its other reading of a press.
    /// </summary>
    public bool Begin(InteractionController controller, Point2D position)
    {
        if (!controller.Surface.TryGetAxesAt(position, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            return false;
        }

        if (axes.Is3D)
        {
            if (axes.InteractionOf<RotateInteractionModel>() is null)
            {
                return false;
            }
        }
        else if (axes.InteractionOf<PanInteractionModel>() is { } pan)
        {
            _dimensions = pan.Dimensions;
        }
        else
        {
            return false;
        }

        _axes = axes;
        _startMapper = mapper;
        _startX = axes.PrimaryXAxis.Range;
        _startY = axes.PrimaryYAxis.Range;
        _startPixel = position;
        _rotating = axes.Is3D;
        _startAzimuth = axes.Azimuth;
        _startElevation = axes.Elevation;
        _before = AxesViewState.Capture(axes);
        Active = true;
        return true;
    }

    /// <summary>Updates the view for the pointer having moved to <paramref name="position"/>.</summary>
    public void Move(InteractionController controller, Point2D position)
    {
        if (!Active || _axes is null || _startMapper is null)
        {
            return;
        }

        if (_rotating)
        {
            double dx = position.X - _startPixel.X;
            double dy = position.Y - _startPixel.Y;
            // Dragging is an angle move, so it releases a camera that was placed by hand: the drag
            // would otherwise turn angles nothing is drawn from.
            _axes.SetViewAngles(
                _startAzimuth - (dx * RotateSpeed),
                System.Math.Clamp(_startElevation + (dy * RotateSpeed), -90, 90));
            controller.Surface.RequestRender();
            return;
        }

        Navigation.Pan(_axes, _startMapper, _startX, _startY, _startPixel, position, _dimensions);
        controller.Surface.RequestRender();
    }

    /// <summary>Commits the drag as one undoable view change.</summary>
    public void End(InteractionController controller)
    {
        if (Active && _axes is not null && _before is not null)
        {
            controller.CommitViewChange(_axes, _before, _rotating ? "Rotate" : "Pan");
        }

        Reset();
    }

    /// <summary>Restores the pre-drag view (Escape / mode switch).</summary>
    public void Cancel(InteractionController controller)
    {
        if (Active && _axes is not null && _before is not null)
        {
            _before.ApplyTo(_axes);
            controller.Surface.RequestRender();
        }

        Reset();
    }

    private void Reset()
    {
        Active = false;
        _rotating = false;
        _axes = null;
        _startMapper = null;
        _before = null;
    }
}
