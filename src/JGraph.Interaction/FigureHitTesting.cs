using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction;

/// <summary>What a click landed on: the object (or null for bare canvas), the axes under the pixel
/// when there was one, and where that is in the axes' data space when that means anything.</summary>
/// <param name="Target">The topmost hit-testable object under the pixel, or null.</param>
/// <param name="Axes">The axes whose plot area held the pixel, if any — the fallback target and the
/// space <paramref name="DataPoint"/> is measured in.</param>
/// <param name="DataPoint">The click in data coordinates: the hit's own data point for a plot, the
/// pixel mapped through the axes for anything else, null when no axes was under the click.</param>
public sealed record FigureHit(GraphObject? Target, AxesModel? Axes, Point2D? DataPoint);

/// <summary>
/// The one pixel-to-object resolution, shared by the edit mode's selection and the script layer's
/// <c>ButtonDownFcn</c>. Keeping it in one place is the point: what the user can select and what a
/// script hears about must be the same thing, or a click would mean two different objects.
/// </summary>
public static class FigureHitTesting
{
    /// <summary>How close a click must come to a plot's geometry, in pixels.</summary>
    public const double PlotPickTolerancePixels = 8;

    /// <summary>Annotations are compact and drawn on top, so they are picked more tightly.</summary>
    public const double AnnotationPickTolerancePixels = 4;

    /// <summary>
    /// Finds the topmost selectable object under a pixel: figure annotations (drawn last, so checked
    /// first), then the legend by its own box, then the annotations and plots of the axes under the
    /// pixel, then the axes itself.
    /// </summary>
    public static FigureHit Resolve(IInteractionSurface surface, Point2D pixel)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (surface.DefaultAxes?.Parent is FigureModel figure)
        {
            AnnotationObject? figureHit = HitTestAnnotations(figure.Annotations, pixel);
            if (figureHit is not null)
            {
                return new FigureHit(figureHit, null, null);
            }
        }

        // The legend is drawn over everything and can hang outside the plot area, so it is picked
        // first, by its own box.
        if (surface.TryGetLegendAt(pixel, out AxesModel legendAxes, out _)
            && legendAxes.Legend.Selectable)
        {
            return new FigureHit(legendAxes.Legend, legendAxes, null);
        }

        if (!surface.TryGetAxesAt(pixel, out AxesModel axes, out ICoordinateMapper mapper, out _))
        {
            return new FigureHit(null, null, null);
        }

        Point2D place = mapper.PixelToData(pixel.X, pixel.Y);
        AnnotationObject? annotationHit = HitTestAnnotations(axes.Annotations, pixel);
        if (annotationHit is not null)
        {
            return new FigureHit(annotationHit, axes, place);
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
            return new FigureHit(best.Target, axes, best.DataPoint);
        }

        return new FigureHit(axes.Selectable ? axes : null, axes, place);
    }

    private static AnnotationObject? HitTestAnnotations(
        GraphObjectCollection<AnnotationObject> annotations, Point2D pixel)
    {
        // Topmost first: reverse draw order.
        foreach (AnnotationObject annotation in annotations.InDrawOrder().Reverse())
        {
            if (annotation.Visible && annotation.Selectable
                && annotation.HitTest(pixel, AnnotationPickTolerancePixels))
            {
                return annotation;
            }
        }

        return null;
    }
}
