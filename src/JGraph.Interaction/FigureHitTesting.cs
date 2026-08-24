using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;

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

        if (!surface.TryGetAxesAt(pixel, out AxesModel axes, out ICoordinateMapper mapper, out Rect2D plotArea))
        {
            return new FigureHit(null, null, null);
        }

        // A 3-D axes is picked through the camera it was drawn with — the same camera CurrentPoint
        // was already asking, built once and used for both. Before M87 there was no such branch and
        // no plot type in space implemented a hit test, so every click there fell through to the
        // axes below.
        Projection3D? camera = axes.Is3D ? ProjectionFor(axes, plotArea) : null;

        Point2D place = mapper.PixelToData(pixel.X, pixel.Y);
        RecordCurrentPoint(axes, pixel, place, camera);

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

            PlotHitResult? hit = camera is not null
                ? plot.HitTest3D(pixel, camera, PlotPickTolerancePixels)
                : plot.HitTest(pixel, mapper, PlotPickTolerancePixels);
            if (hit is not null && (best is null || Beats(hit, best)))
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

    /// <summary>
    /// Whether one hit should be preferred to another. Nearer the pointer wins; among hits the
    /// pointer is equally near — which in space is the common case, since a click inside two
    /// overlapping faces is dead centre of both — the one nearer the camera wins, because that is
    /// the one drawn on top and the one a person was looking at.
    /// </summary>
    /// <remarks>
    /// The camera depth is NaN on a flat hit, and every comparison against NaN is false, so a flat
    /// axes falls through to the first-found rule it has always had. That is deliberate: two flat
    /// objects under one pixel have no "in front", and inventing an order for them would change
    /// behaviour this milestone has no business changing.
    /// </remarks>
    private static bool Beats(PlotHitResult candidate, PlotHitResult best)
    {
        if (candidate.DistancePixels < best.DistancePixels - DepthTieTolerance)
        {
            return true;
        }

        if (candidate.DistancePixels > best.DistancePixels + DepthTieTolerance)
        {
            return false;
        }

        return candidate.CameraDepth > best.CameraDepth;
    }

    /// <summary>
    /// How close two hits' pixel distances must be before the camera decides between them. A click
    /// inside two faces reads zero for both; a click near the shared edge of two reads two distances
    /// that differ by a rounding, and calling those a tie is what stops the answer flickering
    /// between the front face and the one behind it as the pointer moves a fraction.
    /// </summary>
    private const double DepthTieTolerance = 1.0;

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

    /// <summary>
    /// Records where the pointer crossed an axes, whatever it went on to hit: MATLAB's
    /// <c>CurrentPoint</c> is where the pointer is, not what it managed to pick. In two dimensions a
    /// pixel names one point; in three it names a line of sight, so the box is asked where that line
    /// enters and leaves it.
    /// </summary>
    private static void RecordCurrentPoint(
        AxesModel axes, Point2D pixel, Point2D place, Projection3D? camera)
    {
        if (camera is null)
        {
            var flat = new Vector3D(place.X, place.Y, 0);
            axes.SetCurrentPoint(flat, flat);
            return;
        }

        (Vector3D front, Vector3D back) = camera.Unproject(pixel.X, pixel.Y);
        axes.SetCurrentPoint(front, back);
    }

    /// <summary>
    /// The camera the axes was last drawn through, rebuilt from the same state the renderer reads.
    /// Reading a pixel back is exactly the inverse of drawing one, so it has to be the same camera.
    /// </summary>
    private static Projection3D ProjectionFor(AxesModel axes, Rect2D plotArea)
    {
        DataRange xr = axes.PrimaryXAxis.Range, yr = axes.ActiveYAxis.Range, zr = axes.ZAxis.Range;
        Vector3D boxAspect = axes.DataAspectRatio is { X: > 0, Y: > 0, Z: > 0 } dar
            ? new Vector3D(
                (xr.Max - xr.Min) / dar.X,
                (yr.Max - yr.Min) / dar.Y,
                (zr.Max - zr.Min) / dar.Z)
            : axes.PlotBoxAspect;

        return axes.HasAutomaticCamera
            ? new Projection3D(xr, yr, zr, axes.Azimuth, axes.Elevation, plotArea, boxAspect, axes.Roll)
            : new Projection3D(
                xr, yr, zr, plotArea, boxAspect, axes.Roll,
                axes.EffectiveCameraPosition(), axes.EffectiveCameraTarget(), axes.EffectiveCameraUpVector(),
                axes.CameraViewAngle, axes.Projection == ProjectionType.Perspective);
    }
}
