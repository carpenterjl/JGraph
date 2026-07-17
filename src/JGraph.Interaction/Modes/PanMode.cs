using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>Drags the plot: pointer motion pans the axes so the grabbed data point follows the cursor.</summary>
public sealed class PanMode : InteractionModeBase
{
    private bool _active;
    private AxesModel? _axes;
    private ICoordinateMapper? _startMapper;
    private DataRange _startX;
    private DataRange _startY;
    private Point2D _startPixel;
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
        _before = AxesViewState.Capture(axes);
        _active = true;
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e)
    {
        if (!_active || _axes is null || _startMapper is null)
        {
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

        controller.CommitViewChange(_axes, _before, "Pan");
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
        _axes = null;
        _startMapper = null;
        _before = null;
    }
}
