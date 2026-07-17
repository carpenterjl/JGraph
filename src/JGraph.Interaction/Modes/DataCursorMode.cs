using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction.Modes;

/// <summary>
/// Shows a readout of the nearest data point under the cursor. Clicking picks the closest hit-testable
/// plot within a tolerance; the host draws the marker and label. This is the foundation for a richer
/// data-tip experience in later milestones.
/// </summary>
public sealed class DataCursorMode : InteractionModeBase
{
    private const double PickTolerancePixels = 14;

    public override InteractionModeKind Kind => InteractionModeKind.DataCursor;

    public override InteractionCursor Cursor => InteractionCursor.Cross;

    public override void OnPointerDown(InteractionController controller, PointerEventArgs e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }

        if (!controller.Surface.TryGetAxesAt(e.Position, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            controller.SetDataCursor(null);
            return;
        }

        PlotHitResult? best = null;
        foreach (PlotObject plot in axes.Plots)
        {
            if (!plot.Visible)
            {
                continue;
            }

            PlotHitResult? hit = plot.HitTest(e.Position, mapper, PickTolerancePixels);
            if (hit is not null && (best is null || hit.DistancePixels < best.DistancePixels))
            {
                best = hit;
            }
        }

        if (best is null)
        {
            controller.SetDataCursor(null);
            return;
        }

        Point2D pixel = mapper.DataToPixel(best.DataPoint.X, best.DataPoint.Y);
        controller.SetDataCursor(new DataCursorInfo(axes, best.Target, best.DataPoint, pixel));
    }
}
