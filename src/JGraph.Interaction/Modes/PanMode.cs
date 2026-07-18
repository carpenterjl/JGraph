using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>
/// Drags the plot: on a 2D axes, pointer motion pans so the grabbed data point follows the cursor;
/// on a 3D axes the same drag rotates the camera (azimuth follows horizontal motion, elevation
/// vertical), matching MATLAB's rotate tool. The mechanics live in <see cref="PanDragGesture"/>,
/// shared with the default <see cref="PointerMode"/>.
/// </summary>
public sealed class PanMode : InteractionModeBase
{
    private readonly PanDragGesture _gesture = new();

    public override InteractionModeKind Kind => InteractionModeKind.Pan;

    public override InteractionCursor Cursor => InteractionCursor.Hand;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        _gesture.Begin(controller, e.Position);
    }

    public override void OnPointerMove(InteractionController controller, PointerEventArgs e) =>
        _gesture.Move(controller, e.Position);

    public override void OnPointerUp(InteractionController controller, PointerEventArgs e) =>
        _gesture.End(controller);

    public override void Cancel(InteractionController controller) => _gesture.Cancel(controller);
}
