using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>
/// Drags the plot: on a 2D axes, pointer motion pans so the grabbed data point follows the cursor;
/// on a 3D axes the same drag rotates the camera (azimuth follows horizontal motion, elevation
/// vertical), matching MATLAB's rotate tool.
/// </summary>
public sealed class PanMode : InteractionModeBase
{
    /// <summary>Camera degrees per pixel of drag.</summary>
    private const double RotateSpeed = 0.4;

    private bool _active;
    private bool _rotating;
    private AxesModel? _axes;
    private ICoordinateMapper? _startMapper;
    private DataRange _startX;
    private DataRange _startY;
    private Point2D _startPixel;
    private double _startAzimuth;
    private double _startElevation;
    private AxesViewState? _before;

    public override InteractionModeKind Kind => InteractionModeKind.Pan;

    public override InteractionCursor Cursor => InteractionCursor.Hand;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        if (!controller.Surface.TryGetAxesAt(e.Position, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            return;
        }

        _axes = axes;
        _startMapper = mapper;
        _startX = axes.PrimaryXAxis.Range;
        _startY = axes.PrimaryYAxis.Range;
        _startPixel = e.Position;
        _rotating = axes.Is3D;
        _startAzimuth = axes.Azimuth;
        _startElevation = axes.Elevation;
        _before = AxesViewState.Capture(axes);
        _active = true;
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (!_active || _axes is null || _startMapper is null)
        {
            return;
        }

        if (_rotating)
        {
            double dx = e.Position.X - _startPixel.X;
            double dy = e.Position.Y - _startPixel.Y;
            _axes.Azimuth = _startAzimuth - (dx * RotateSpeed);
            _axes.Elevation = System.Math.Clamp(_startElevation + (dy * RotateSpeed), -90, 90);
            controller.Surface.RequestRender();
            return;
        }

        Navigation.Pan(_axes, _startMapper, _startX, _startY, _startPixel, e.Position);
        controller.Surface.RequestRender();
    }

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e)
    {
        if (!_active || _axes is null || _before is null)
        {
            return;
        }

        controller.CommitViewChange(_axes, _before, _rotating ? "Rotate" : "Pan");
        Reset();
    }

    public override void Cancel(InteractionController controller)
    {
        if (_active && _axes is not null && _before is not null)
        {
            _before.ApplyTo(_axes);
            controller.Surface.RequestRender();
        }

        Reset();
    }

    private void Reset()
    {
        _active = false;
        _rotating = false;
        _axes = null;
        _startMapper = null;
        _before = null;
    }
}
