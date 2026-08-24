namespace JGraph.Core.Primitives;

/// <summary>
/// Says where a point of three-dimensional data space was drawn, and how near the camera it is. The
/// spatial counterpart of <see cref="ICoordinateMapper"/>, and here for the same reason: hit-testing
/// has to ask where something was drawn without knowing what drew it.
/// </summary>
/// <remarks>
/// <para>
/// A flat mapper answers both questions — a pixel names one data point and a data point names one
/// pixel — so <see cref="ICoordinateMapper"/> can go both ways. A camera cannot: a pixel names a
/// whole line of sight through the box, which is why the axes' <c>CurrentPoint</c> is two points
/// rather than one. So this goes one way only, and picking works forward, by drawing each candidate
/// where the renderer drew it and measuring on screen.
/// </para>
/// <para>
/// The depth increases toward the viewer, which is the order a painter's algorithm draws in and the
/// tie-break a click wants: of two things under one pixel, the nearer was drawn last and is the one
/// a person meant.
/// </para>
/// </remarks>
public interface ISpatialMapper
{
    /// <summary>
    /// The pixel a data point was drawn at, and how near the camera it is.
    /// </summary>
    (Point2D Position, double Depth) Project(double x, double y, double z);
}
