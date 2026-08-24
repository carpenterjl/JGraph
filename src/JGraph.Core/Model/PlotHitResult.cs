using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The result of a successful hit test against a <see cref="PlotObject"/>: which object was hit,
/// the nearest data point (when the plot is point-based), and the device-space distance to it.
/// </summary>
public sealed class PlotHitResult
{
    public PlotHitResult(
        PlotObject target,
        Point2D dataPoint,
        double distancePixels,
        int pointIndex = -1,
        double cameraDepth = double.NaN)
    {
        Target = target;
        DataPoint = dataPoint;
        DistancePixels = distancePixels;
        PointIndex = pointIndex;
        CameraDepth = cameraDepth;
    }

    /// <summary>The plot object that was hit.</summary>
    public PlotObject Target { get; }

    /// <summary>The nearest data-space point on the object.</summary>
    public Point2D DataPoint { get; }

    /// <summary>Device-space distance from the query point to <see cref="DataPoint"/>.</summary>
    public double DistancePixels { get; }

    /// <summary>Index of the nearest data sample, or -1 if not applicable.</summary>
    public int PointIndex { get; }

    /// <summary>
    /// How near the camera the hit was, increasing toward the viewer, or NaN for a flat hit where
    /// the question has no meaning (M87).
    /// </summary>
    /// <remarks>
    /// Two objects can sit under one pixel, and in space the one in front is the one a person meant —
    /// which pixel distance alone cannot say, since both may be dead centre. Carried on the result
    /// rather than worked out again by the caller, because only the object that was hit knows which
    /// of its own parts the click landed on and therefore how far away that part was.
    /// </remarks>
    public double CameraDepth { get; }
}
