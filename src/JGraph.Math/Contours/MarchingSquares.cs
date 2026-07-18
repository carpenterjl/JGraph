using JGraph.Core.Primitives;

namespace JGraph.Maths.Contours;

/// <summary>
/// Contour extraction from a rectilinear scalar field <c>z[row, col]</c> sampled at grid positions
/// <c>x[col]</c>/<c>y[row]</c> (MATLAB convention: rows index Y). <see cref="Lines"/> yields iso-line
/// segments for a single level via the standard 16-case marching-squares table with linear edge
/// interpolation; <see cref="FilledCells"/> yields per-cell band polygons (the cell quad clipped
/// between two levels) for filled contours — adjacent cells share interpolated edge crossings, so the
/// bands tile seamlessly without any global polygon assembly. Cells touching a non-finite sample are
/// skipped.
/// </summary>
public static class MarchingSquares
{
    /// <summary>
    /// Extracts iso-line segments for <paramref name="level"/>. Each returned polyline is a short
    /// segment in data space (two points; saddle cells contribute two segments).
    /// </summary>
    public static IReadOnlyList<Point2D[]> Lines(double[] x, double[] y, double[,] z, double level)
    {
        Validate(x, y, z);
        var segments = new List<Point2D[]>();

        for (int r = 0; r < y.Length - 1; r++)
        {
            for (int c = 0; c < x.Length - 1; c++)
            {
                // Corner values, counter-clockwise from bottom-left of the cell in data space.
                double v00 = z[r, c];         // (x[c],     y[r])
                double v10 = z[r, c + 1];     // (x[c + 1], y[r])
                double v11 = z[r + 1, c + 1]; // (x[c + 1], y[r + 1])
                double v01 = z[r + 1, c];     // (x[c],     y[r + 1])
                if (!double.IsFinite(v00) || !double.IsFinite(v10) || !double.IsFinite(v11) || !double.IsFinite(v01))
                {
                    continue;
                }

                int mask = (v00 >= level ? 1 : 0)
                         | (v10 >= level ? 2 : 0)
                         | (v11 >= level ? 4 : 0)
                         | (v01 >= level ? 8 : 0);
                if (mask is 0 or 15)
                {
                    continue;
                }

                double x0 = x[c], x1 = x[c + 1];
                double y0 = y[r], y1 = y[r + 1];

                // Interpolated crossing points on the four cell edges.
                Point2D Bottom() => new(Interp(x0, x1, v00, v10, level), y0);
                Point2D Top() => new(Interp(x0, x1, v01, v11, level), y1);
                Point2D Left() => new(x0, Interp(y0, y1, v00, v01, level));
                Point2D Right() => new(x1, Interp(y0, y1, v10, v11, level));

                switch (mask)
                {
                    case 1 or 14:
                        segments.Add([Left(), Bottom()]);
                        break;
                    case 2 or 13:
                        segments.Add([Bottom(), Right()]);
                        break;
                    case 3 or 12:
                        segments.Add([Left(), Right()]);
                        break;
                    case 4 or 11:
                        segments.Add([Top(), Right()]);
                        break;
                    case 6 or 9:
                        segments.Add([Bottom(), Top()]);
                        break;
                    case 7 or 8:
                        segments.Add([Left(), Top()]);
                        break;
                    case 5:
                    case 10:
                        // Saddle: resolve by the cell-center average.
                        double center = (v00 + v10 + v11 + v01) / 4;
                        bool centerHigh = center >= level;
                        if ((mask == 5) == centerHigh)
                        {
                            segments.Add([Left(), Top()]);
                            segments.Add([Bottom(), Right()]);
                        }
                        else
                        {
                            segments.Add([Left(), Bottom()]);
                            segments.Add([Top(), Right()]);
                        }

                        break;
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Clips each grid cell to the band <paramref name="lower"/> ≤ z ≤ <paramref name="upper"/> and
    /// returns the resulting polygons (3–8 vertices each) in data space.
    /// </summary>
    public static IReadOnlyList<Point2D[]> FilledCells(double[] x, double[] y, double[,] z, double lower, double upper)
    {
        Validate(x, y, z);
        var polygons = new List<Point2D[]>();
        Span<(Point2D P, double V)> buffer = stackalloc (Point2D, double)[16];
        Span<(Point2D P, double V)> clipped = stackalloc (Point2D, double)[16];

        for (int r = 0; r < y.Length - 1; r++)
        {
            for (int c = 0; c < x.Length - 1; c++)
            {
                double v00 = z[r, c];
                double v10 = z[r, c + 1];
                double v11 = z[r + 1, c + 1];
                double v01 = z[r + 1, c];
                if (!double.IsFinite(v00) || !double.IsFinite(v10) || !double.IsFinite(v11) || !double.IsFinite(v01))
                {
                    continue;
                }

                buffer[0] = (new Point2D(x[c], y[r]), v00);
                buffer[1] = (new Point2D(x[c + 1], y[r]), v10);
                buffer[2] = (new Point2D(x[c + 1], y[r + 1]), v11);
                buffer[3] = (new Point2D(x[c], y[r + 1]), v01);

                int count = Clip(buffer, 4, clipped, lower, keepAbove: true);
                if (count < 3)
                {
                    continue;
                }

                count = Clip(clipped, count, buffer, upper, keepAbove: false);
                if (count < 3)
                {
                    continue;
                }

                var polygon = new Point2D[count];
                for (int i = 0; i < count; i++)
                {
                    polygon[i] = buffer[i].P;
                }

                polygons.Add(polygon);
            }
        }

        return polygons;
    }

    /// <summary>
    /// Sutherland–Hodgman clip of a value-annotated polygon against an iso-value: keeps vertices with
    /// value ≥ (or ≤) <paramref name="level"/> and inserts linearly interpolated crossing points.
    /// </summary>
    private static int Clip(
        ReadOnlySpan<(Point2D P, double V)> input,
        int count,
        Span<(Point2D P, double V)> output,
        double level,
        bool keepAbove)
    {
        int outCount = 0;
        for (int i = 0; i < count; i++)
        {
            (Point2D P, double V) current = input[i];
            (Point2D P, double V) next = input[(i + 1) % count];
            bool currentIn = keepAbove ? current.V >= level : current.V <= level;
            bool nextIn = keepAbove ? next.V >= level : next.V <= level;

            if (currentIn)
            {
                output[outCount++] = current;
            }

            if (currentIn != nextIn)
            {
                double t = (level - current.V) / (next.V - current.V);
                var crossing = new Point2D(
                    current.P.X + ((next.P.X - current.P.X) * t),
                    current.P.Y + ((next.P.Y - current.P.Y) * t));
                output[outCount++] = (crossing, level);
            }
        }

        return outCount;
    }

    private static double Interp(double a, double b, double va, double vb, double level)
    {
        double denom = vb - va;
        double t = System.Math.Abs(denom) < double.Epsilon ? 0.5 : (level - va) / denom;
        return a + ((b - a) * System.Math.Clamp(t, 0, 1));
    }

    private static void Validate(double[] x, double[] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (z.GetLength(0) != y.Length || z.GetLength(1) != x.Length)
        {
            throw new ArgumentException(
                $"z must be [{y.Length} rows x {x.Length} cols] to match y and x, but was [{z.GetLength(0)} x {z.GetLength(1)}].");
        }
    }
}
