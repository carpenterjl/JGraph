using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The two shapes every plot in space is picked by: a cloud of points, and a run of segments joining
/// them. Written once here because five plot types need them and the arithmetic is the part it would
/// be easy to get subtly different in each.
/// </summary>
/// <remarks>
/// Both work in <em>pixels</em>, on points put where the renderer put them. That is the whole idea of
/// picking through a camera: what a click lands on is decided by the picture, so the measuring has to
/// happen there. Measuring in data space instead would make a click near the viewer and a click far
/// from it mean different distances, and a line seen end-on impossible to hit.
/// </remarks>
public static class SpatialPicking
{
    /// <summary>
    /// The nearest of a set of points to <paramref name="pixel"/>, within the tolerance. Ties go to
    /// whichever is nearer the camera, because of two things under one pixel the nearer was drawn
    /// last and is the one a person meant.
    /// </summary>
    public static (int Index, double Distance, double Depth)? NearestPoint(
        Point2D pixel,
        ISpatialMapper projector,
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        IReadOnlyList<double> z,
        double tolerancePixels)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        double bestDepth = double.NegativeInfinity;

        int count = Shortest(x, y, z);
        for (int i = 0; i < count; i++)
        {
            if (!IsDrawable(x[i], y[i], z[i]))
            {
                continue;
            }

            (Point2D at, double depth) = projector.Project(x[i], y[i], z[i]);
            double distance = Distance(pixel, at);
            if (distance > tolerancePixels)
            {
                continue;
            }

            if (distance < bestDistance || (distance == bestDistance && depth > bestDepth))
            {
                best = i;
                bestDistance = distance;
                bestDepth = depth;
            }
        }

        return best >= 0 ? (best, bestDistance, bestDepth) : null;
    }

    /// <summary>
    /// The nearest point of the polyline through the given points, measured to the segments rather
    /// than to the vertices — so the middle of a long straight run is as pickable as its ends.
    /// </summary>
    /// <returns>The index of the segment's first vertex and the pixel distance, or null.</returns>
    public static (int Index, double Distance, double Depth)? NearestSegment(
        Point2D pixel,
        ISpatialMapper projector,
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        IReadOnlyList<double> z,
        double tolerancePixels)
    {
        int count = Shortest(x, y, z);
        if (count == 0)
        {
            return null;
        }

        int best = -1;
        double bestDistance = double.PositiveInfinity;
        double bestDepth = double.NegativeInfinity;
        bool havePrevious = false;
        Point2D previous = default;
        double previousDepth = 0;
        int previousIndex = 0;

        for (int i = 0; i < count; i++)
        {
            if (!IsDrawable(x[i], y[i], z[i]))
            {
                // A break in the data is a break in the line: the gap either side of a NaN is not a
                // segment, and picking one would let a click land on a stretch nothing was drawn on.
                havePrevious = false;
                continue;
            }

            (Point2D at, double depth) = projector.Project(x[i], y[i], z[i]);
            double distance = havePrevious
                ? DistanceToSegment(pixel, previous, at)
                : Distance(pixel, at);
            int index = havePrevious ? previousIndex : i;

            // A segment's depth is its nearer end. A line running away from the viewer is picked at
            // the end a person is looking at rather than the one behind it.
            double reach = havePrevious ? System.Math.Max(previousDepth, depth) : depth;

            if (distance < bestDistance || (distance == bestDistance && reach > bestDepth))
            {
                best = index;
                bestDistance = distance;
                bestDepth = reach;
            }

            previous = at;
            previousDepth = depth;
            previousIndex = i;
            havePrevious = true;
        }

        return best >= 0 && bestDistance <= tolerancePixels
            ? (best, bestDistance, bestDepth)
            : null;
    }

    /// <summary>Whether a sample can be drawn at all — a NaN or an infinity is a hole, not a place.</summary>
    public static bool IsDrawable(double x, double y, double z) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);

    /// <summary>
    /// Whether a pixel falls inside a face, given that face's corners as they were drawn. A filled
    /// shape is picked by its inside, not only by its outline — clicking the middle of a patch is
    /// clicking the patch.
    /// </summary>
    /// <remarks>
    /// The crossing-number rule, on screen. Its answer for a face the camera sees edge-on is "no",
    /// which is right: a face with no area on screen has nothing to click on, and the edge test
    /// beside it still catches a click along the line it collapsed to.
    /// </remarks>
    public static bool Inside(Point2D pixel, ReadOnlySpan<Point2D> corners)
    {
        bool inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            if (corners[i].Y > pixel.Y != corners[j].Y > pixel.Y
                && pixel.X < ((corners[j].X - corners[i].X) * (pixel.Y - corners[i].Y)
                    / (corners[j].Y - corners[i].Y)) + corners[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>The pixel distance from a point to the closed outline of a face.</summary>
    public static double DistanceToOutline(Point2D pixel, ReadOnlySpan<Point2D> corners)
    {
        if (corners.Length == 0)
        {
            return double.PositiveInfinity;
        }

        if (corners.Length == 1)
        {
            return Distance(pixel, corners[0]);
        }

        double best = double.PositiveInfinity;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            best = System.Math.Min(best, DistanceToSegment(pixel, corners[j], corners[i]));
        }

        return best;
    }

    /// <summary>The shortest of the three channels, which is as far as a point exists in all three.</summary>
    private static int Shortest(
        IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> z) =>
        System.Math.Min(x.Count, System.Math.Min(y.Count, z.Count));

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double DistanceToSegment(Point2D point, Point2D from, Point2D to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0)
        {
            return Distance(point, from);
        }

        double along = (((point.X - from.X) * dx) + ((point.Y - from.Y) * dy)) / lengthSquared;
        along = System.Math.Clamp(along, 0, 1);
        return Distance(point, new Point2D(from.X + (along * dx), from.Y + (along * dy)));
    }
}
