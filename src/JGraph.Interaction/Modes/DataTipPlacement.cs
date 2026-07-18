using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Annotations;

namespace JGraph.Interaction.Modes;

/// <summary>
/// Shared data-tip placement: finding the nearest pickable data point under a pixel and building a
/// <see cref="DataTipAnnotation"/> pinned to it, with the label offset up-right of the point. Used
/// by <see cref="PointerMode"/> (persistent tips) and <see cref="DataTipsMode"/> (replace-last tips).
/// </summary>
internal static class DataTipPlacement
{
    /// <summary>How far a click may land from a data point and still pick it.</summary>
    public const double PickTolerancePixels = 14;

    private const double LabelOffsetPixels = 18;

    /// <summary>The nearest visible data point under <paramref name="pixel"/>, or null (3D axes excluded).</summary>
    public static (AxesModel Axes, ICoordinateMapper Mapper, PlotHitResult Hit)? FindPoint(
        InteractionController controller, Point2D pixel)
    {
        if (!controller.Surface.TryGetAxesAt(pixel, out AxesModel axes, out ICoordinateMapper mapper, out _)
            || axes.Is3D)
        {
            return null;
        }

        PlotHitResult? best = null;
        foreach (PlotObject plot in axes.Plots)
        {
            if (!plot.Visible)
            {
                continue;
            }

            PlotHitResult? hit = plot.HitTest(pixel, mapper, PickTolerancePixels);
            if (hit is not null && (best is null || hit.DistancePixels < best.DistancePixels))
            {
                best = hit;
            }
        }

        return best is null ? null : (axes, mapper, best);
    }

    /// <summary>Builds a tip pinned at the hit point, its label offset up-right in pixel space.</summary>
    public static DataTipAnnotation CreateTip(ICoordinateMapper mapper, PlotHitResult hit)
    {
        Point2D label = AnnotationObject.ShiftByPixels(
            hit.DataPoint, new Vector2D(LabelOffsetPixels, -LabelOffsetPixels), mapper);
        return new DataTipAnnotation(hit.DataPoint, label)
        {
            SourceSeries = hit.Target.Name,
            PointIndex = hit.PointIndex,
        };
    }
}
