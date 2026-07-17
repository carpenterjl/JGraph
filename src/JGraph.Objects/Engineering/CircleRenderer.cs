using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Engineering;

/// <summary>
/// Shared helpers for the polar and Smith grids: sampling a data-space circle (or an arc of one
/// clipped to a disk) into a device-space polyline through a coordinate mapper. Because the mapper
/// carries the axes' equal-aspect square, a data circle maps to a round pixel circle.
/// </summary>
internal static class CircleRenderer
{
    /// <summary>Draws a full circle of data radius <paramref name="radius"/> centered at (cx, cy).</summary>
    public static void DrawCircle(
        IRenderContext context,
        ICoordinateMapper mapper,
        double cx,
        double cy,
        double radius,
        LineStyle style,
        int segments = 96)
    {
        if (radius <= 0 || segments < 3)
        {
            return;
        }

        var points = new Point2D[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double theta = 2.0 * System.Math.PI * i / segments;
            points[i] = mapper.DataToPixel(cx + (radius * System.Math.Cos(theta)), cy + (radius * System.Math.Sin(theta)));
        }

        context.DrawPolyline(points, style);
    }

    /// <summary>
    /// Draws only the portion of a circle that lies within the disk of radius
    /// <paramref name="clipRadius"/> centered at the origin (in data space), splitting the arc into
    /// contiguous runs. Used for the Smith chart's constant-reactance arcs.
    /// </summary>
    public static void DrawCircleClippedToUnitDisk(
        IRenderContext context,
        ICoordinateMapper mapper,
        double cx,
        double cy,
        double radius,
        double clipRadius,
        LineStyle style,
        int segments = 240)
    {
        if (radius <= 0 || segments < 3)
        {
            return;
        }

        double clipSquared = clipRadius * clipRadius;
        var run = new List<Point2D>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double theta = 2.0 * System.Math.PI * i / segments;
            double dx = cx + (radius * System.Math.Cos(theta));
            double dy = cy + (radius * System.Math.Sin(theta));
            bool inside = (dx * dx) + (dy * dy) <= clipSquared + 1e-9;
            if (inside)
            {
                run.Add(mapper.DataToPixel(dx, dy));
            }
            else if (run.Count > 0)
            {
                Flush(context, run, style);
            }
        }

        Flush(context, run, style);
    }

    private static void Flush(IRenderContext context, List<Point2D> run, LineStyle style)
    {
        if (run.Count >= 2)
        {
            context.DrawPolyline(run.ToArray(), style);
        }

        run.Clear();
    }
}
