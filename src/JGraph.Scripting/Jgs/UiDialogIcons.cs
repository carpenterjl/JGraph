namespace JGraph.Scripting.Jgs;

/// <summary>
/// The four pictures MATLAB's classic dialogs carry — error, warning, help and question — as 32 by
/// 32 truecolour images with a transparency of their own (app-building plan, U4). They are drawn
/// here, from circles, a triangle and strokes, rather than read from files: R2025b's own icons are
/// MathWorks' artwork, and what a script can see of them is only their size and class.
/// </summary>
internal static class UiDialogIcons
{
    private const int Side = 32;

    private static readonly (double R, double G, double B) Red = (0.84, 0.18, 0.16);
    private static readonly (double R, double G, double B) Amber = (0.98, 0.76, 0.12);
    private static readonly (double R, double G, double B) Blue = (0.15, 0.47, 0.84);
    private static readonly (double R, double G, double B) White = (1, 1, 1);
    private static readonly (double R, double G, double B) Ink = (0.18, 0.18, 0.18);

    /// <summary>One icon: its colours as an m-by-n-by-3 array of doubles, and its opacity as m-by-n.</summary>
    /// <param name="name"><c>error</c>, <c>warn</c>, <c>help</c> or <c>quest</c>.</param>
    public static (JgsValue Picture, JgsValue Alpha) Standard(string name)
    {
        var picture = new double[Side * Side * 3];
        var alpha = new double[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int column = 0; column < Side; column++)
            {
                double x = column + 0.5;
                double y = row + 0.5;
                ((double R, double G, double B) ground, double cover, double glyph, (double R, double G, double B) ink) = name switch
                {
                    "error" => (Red, Disc(x, y, 16, 16, 14.5),
                        System.Math.Max(Stroke(x, y, 10.5, 10.5, 21.5, 21.5, 3.4), Stroke(x, y, 21.5, 10.5, 10.5, 21.5, 3.4)), White),
                    "warn" => (Amber, Triangle(x, y),
                        System.Math.Max(Stroke(x, y, 16, 11.5, 16, 19.5, 3.2), Disc(x, y, 16, 24, 1.9)), Ink),
                    "help" => (Blue, Disc(x, y, 16, 16, 14.5),
                        System.Math.Max(Stroke(x, y, 16, 14.5, 16, 23.5, 3.2), Disc(x, y, 16, 9.3, 2.1)), White),
                    _ => (Blue, Disc(x, y, 16, 16, 14.5),
                        System.Math.Max(System.Math.Max(Hook(x, y), Stroke(x, y, 16, 17.2, 16, 19.6, 3)), Disc(x, y, 16, 24.2, 1.9)), White),
                };

                int at = row + (column * Side);
                picture[at] = (ink.R * glyph) + (ground.R * (1 - glyph));
                picture[at + (Side * Side)] = (ink.G * glyph) + (ground.G * (1 - glyph));
                picture[at + (2 * Side * Side)] = (ink.B * glyph) + (ground.B * (1 - glyph));
                alpha[at] = cover;
            }
        }

        JgsValue colours = JgsMatrix.FromColumnMajor(picture, Side * Side * 3, 1);
        colours.ReshapeDims([Side, Side, 3]);
        return (colours, JgsMatrix.FromColumnMajor(alpha, Side, Side));
    }

    /// <summary>How much of a pixel a shape covers, from its signed distance to the shape's edge.</summary>
    private static double Cover(double signedDistance) => System.Math.Clamp(0.5 - signedDistance, 0, 1);

    private static double Disc(double x, double y, double cx, double cy, double radius) =>
        Cover(System.Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) - radius);

    /// <summary>A round-ended line of a given width between two points.</summary>
    private static double Stroke(double x, double y, double x1, double y1, double x2, double y2, double width)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double along = System.Math.Clamp((((x - x1) * dx) + ((y - y1) * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
        double px = x1 + (along * dx);
        double py = y1 + (along * dy);
        return Cover(System.Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py))) - (width / 2));
    }

    /// <summary>The warning sign's triangle, its corners a little rounded.</summary>
    private static double Triangle(double x, double y)
    {
        const double round = 1.5;
        (double X, double Y)[] corners = [(16, 3.5 + round), (29.5 - round, 28 - round), (2.5 + round, 28 - round)];
        double inside = double.MinValue;
        for (int i = 0; i < 3; i++)
        {
            (double ax, double ay) = corners[i];
            (double bx, double by) = corners[(i + 1) % 3];
            double ex = bx - ax;
            double ey = by - ay;
            double length = System.Math.Sqrt((ex * ex) + (ey * ey));

            // Corners run clockwise on a y-down page, so the outside of each edge is on its left.
            inside = System.Math.Max(inside, (((y - ay) * ex) - ((x - ax) * ey)) / -length);
        }

        return Cover(inside - round);
    }

    /// <summary>The curve of a question mark: three quarters and a little of a circle, open at the lower left.</summary>
    private static double Hook(double x, double y)
    {
        const double cx = 16;
        const double cy = 12.2;
        const double radius = 4.4;
        const double width = 3;
        const double from = -180;
        const double to = 55;
        double dx = x - cx;
        double dy = y - cy;
        double angle = System.Math.Atan2(dy, dx) * 180 / System.Math.PI;
        double distance;
        if (angle >= from && angle <= to)
        {
            distance = System.Math.Abs(System.Math.Sqrt((dx * dx) + (dy * dy)) - radius);
        }
        else
        {
            static double To(double x, double y, double degrees)
            {
                double px = cx + (radius * System.Math.Cos(degrees * System.Math.PI / 180));
                double py = cy + (radius * System.Math.Sin(degrees * System.Math.PI / 180));
                return System.Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
            }

            distance = System.Math.Min(To(x, y, from), To(x, y, to));
        }

        return Cover(distance - (width / 2));
    }
}
