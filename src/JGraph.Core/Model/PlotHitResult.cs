using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The result of a successful hit test against a <see cref="PlotObject"/>: which object was hit,
/// the nearest data point (when the plot is point-based), and the device-space distance to it.
/// </summary>
public sealed class PlotHitResult
{
    public PlotHitResult(PlotObject target, Point2D dataPoint, double distancePixels, int pointIndex = -1)
    {
        Target = target;
        DataPoint = dataPoint;
        DistancePixels = distancePixels;
        PointIndex = pointIndex;
    }

    /// <summary>The plot object that was hit.</summary>
    public PlotObject Target { get; }

    /// <summary>The nearest data-space point on the object.</summary>
    public Point2D DataPoint { get; }

    /// <summary>Device-space distance from the query point to <see cref="DataPoint"/>.</summary>
    public double DistancePixels { get; }

    /// <summary>Index of the nearest data sample, or -1 if not applicable.</summary>
    public int PointIndex { get; }
}
