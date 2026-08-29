namespace JGraph.Numerics;

/// <summary>
/// Questions about polygons and rectangles in the plane: how much area one encloses, how much two
/// share, and whether a point is inside one.
/// </summary>
public static class PlanarGeometry
{
    /// <summary>
    /// The area a closed polygon encloses, by the shoelace formula, taken as a magnitude so that
    /// the winding direction does not change the answer.
    /// </summary>
    /// <param name="x">The vertices' x coordinates, in order around the boundary.</param>
    /// <param name="y">Their y coordinates.</param>
    /// <returns>
    /// The enclosed area. A polygon that crosses itself contributes its lobes with opposing signs,
    /// so the answer is the net area and not the area swept.
    /// </returns>
    public static double PolygonArea(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        double twice = 0;
        for (int i = 0; i < x.Length; i++)
        {
            int next = i + 1 == x.Length ? 0 : i + 1;
            twice += (x[next] - x[i]) * (y[next] + y[i]);
        }

        return Math.Abs(twice / 2);
    }

    /// <summary>
    /// The area each rectangle in one set shares with each rectangle in another, as a table with a
    /// row per rectangle in <paramref name="a"/> and a column per rectangle in <paramref name="b"/>.
    /// </summary>
    /// <param name="a">The first set, four numbers per rectangle: left, bottom, width, height.</param>
    /// <param name="b">The second set, in the same layout.</param>
    /// <returns>The overlap areas, column-major, with <c>a.Length / 4</c> rows.</returns>
    public static double[] RectangleOverlaps(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        int rows = a.Length / 4;
        int cols = b.Length / 4;
        var result = new double[rows * cols];

        for (int j = 0; j < cols; j++)
        {
            double leftB = b[j];
            double bottomB = b[cols + j];
            double rightB = leftB + b[(2 * cols) + j];
            double topB = bottomB + b[(3 * cols) + j];

            for (int i = 0; i < rows; i++)
            {
                double leftA = a[i];
                double bottomA = a[rows + i];
                double rightA = leftA + a[(2 * rows) + i];
                double topA = bottomA + a[(3 * rows) + i];

                double wide = Math.Max(0, Math.Min(rightA, rightB) - Math.Max(leftA, leftB));
                double tall = Math.Max(0, Math.Min(topA, topB) - Math.Max(bottomA, bottomB));
                result[(j * rows) + i] = wide * tall;
            }
        }

        return result;
    }

    /// <summary>
    /// Which of the query points lie inside a polygon, and which lie on its boundary.
    /// </summary>
    /// <param name="qx">The query points' x coordinates.</param>
    /// <param name="qy">Their y coordinates.</param>
    /// <param name="vx">The polygon vertices' x coordinates, already closed.</param>
    /// <param name="vy">Their y coordinates.</param>
    /// <returns>
    /// Two flags per query point: inside — which includes the boundary — and on the boundary.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The test is a winding count taken by quadrant rather than by angle. With the query point at
    /// the origin, each vertex falls in one of four quadrants, and the quadrant number changes by
    /// ±1 as an edge crosses an axis. A change of ±3 is really the opposite ∓1 wrapping round, and
    /// a change of ±2 means the edge crossed the far side, whose direction the cross product's sign
    /// settles. Summing those changes over the closed boundary gives four times the winding number,
    /// so a nonzero sum means the point is enclosed.
    /// </para>
    /// <para>
    /// Counting by quadrant instead of by angle is what keeps the test exact for the cases that
    /// matter. No arctangent is taken, so a point on a horizontal edge is not decided by whether a
    /// division rounded up or down; the only tolerance in the whole routine is the one below, on
    /// the cross product, and it is scaled to the size of the coordinates being compared rather
    /// than being an absolute floor that means nothing at the wrong scale.
    /// </para>
    /// </remarks>
    public static (bool[] Inside, bool[] OnBoundary) InPolygon(
        ReadOnlySpan<double> qx, ReadOnlySpan<double> qy,
        ReadOnlySpan<double> vx, ReadOnlySpan<double> vy)
    {
        var inside = new bool[qx.Length];
        var on = new bool[qx.Length];
        int edges = vx.Length - 1;
        if (edges < 1)
        {
            return (inside, on);
        }

        // The scale each edge's cross product is judged against: how big the coordinates involved
        // in that edge actually are, so the tolerance means the same thing near the origin and far
        // from it.
        var scale = new double[edges];
        for (int m = 0; m < edges; m++)
        {
            double avx = Math.Abs(0.5 * (vx[m] + vx[m + 1]));
            double avy = Math.Abs(0.5 * (vy[m] + vy[m + 1]));
            scale[m] = Math.Max(Math.Max(avx, avy), avx * avy) * (3 * 2.220446049250313e-16);
        }

        for (int p = 0; p < qx.Length; p++)
        {
            double px = qx[p];
            double py = qy[p];

            int winding = 0;
            bool boundary = false;
            int previousQuadrant = Quadrant(vx[0] - px, vy[0] - py);

            for (int m = 0; m < edges; m++)
            {
                double x0 = vx[m] - px;
                double y0 = vy[m] - py;
                double x1 = vx[m + 1] - px;
                double y1 = vy[m + 1] - py;

                int quadrant = Quadrant(x1, y1);
                double cross = (x0 * y1) - (x1 * y0);

                // A NaN coordinate leaves the cross product NaN, which is neither zero nor a
                // direction: the edge takes no part in either answer rather than defaulting to one.
                int sign = double.IsNaN(cross) ? int.MinValue
                    : Math.Abs(cross) < scale[m] ? 0
                    : Math.Sign(cross);

                if (sign == 0 && (x0 * x1) + (y0 * y1) <= 0)
                {
                    // Collinear with the edge and not beyond either end: on the boundary.
                    boundary = true;
                }

                if (previousQuadrant >= 0 && quadrant >= 0)
                {
                    int step = quadrant - previousQuadrant;
                    winding += Math.Abs(step) switch
                    {
                        3 => -step / 3,
                        2 => sign == int.MinValue ? 0 : 2 * sign,
                        _ => step,
                    };
                }

                previousQuadrant = quadrant;
            }

            on[p] = boundary;
            inside[p] = winding != 0 || boundary;
        }

        return (inside, on);
    }

    /// <summary>
    /// Which quadrant a point sits in, counted anticlockwise from the first, or −1 for a NaN. The
    /// axes belong to the quadrant anticlockwise of them, which is what makes the count consistent.
    /// </summary>
    private static int Quadrant(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return -1;
        }

        bool positiveX = x > 0;
        bool positiveY = y > 0;
        if (positiveX)
        {
            return positiveY ? 0 : 3;
        }

        return positiveY ? 1 : 2;
    }
}
