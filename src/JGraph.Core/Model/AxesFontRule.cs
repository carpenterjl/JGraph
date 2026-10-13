namespace JGraph.Core.Model;

/// <summary>
/// R2025b's automatic axes font (open item 118; probe_118, probe_118b and probe_118c in the batch 19
/// scratch): ten points when the plot box is at least 276 pixels across and 222 up, and half a point
/// less per band below that down to eight, each side read on its own and the smaller answer taken.
/// It is the inner plot box that counts, before or after the labels take their room, and the
/// figure's size enters only through it. A <c>uiaxes</c> keeps its font at every size, a text object
/// its own ten, and a size a script wrote is manual and stays.
/// </summary>
public static class AxesFontRule
{
    /// <summary>The axes font R2025b draws at full size, in points.</summary>
    public const double FullPoints = 10;

    /// <summary>The smallest automatic font, in points.</summary>
    public const double LeastPoints = 8;

    private static readonly double[] WidthBands = [132, 180, 228, 276];
    private static readonly double[] HeightBands = [106, 144, 182, 222];

    /// <summary>The automatic axes font, in R2025b's points, for a plot box of the given pixel size.</summary>
    public static double AutoPoints(double widthPx, double heightPx) =>
        System.Math.Min(Points(widthPx, WidthBands), Points(heightPx, HeightBands));

    /// <summary>
    /// The factor the built-in font sizes shrink by for a plot box of the given size: 1 at full size,
    /// 0.8 at the smallest. JGraph's built-in sizes are larger than R2025b's, so the ratio rather than
    /// the points is what carries over.
    /// </summary>
    public static double AutoScale(double widthPx, double heightPx) => AutoPoints(widthPx, heightPx) / FullPoints;

    private static double Points(double pixels, double[] bands)
    {
        double points = LeastPoints;
        foreach (double band in bands)
        {
            if (pixels >= band)
            {
                points += 0.5;
            }
        }

        return points;
    }
}
