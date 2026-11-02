namespace JGraph.Scripting.Editing;

/// <summary>
/// The editor's zoom (App Designer window plan W9c, R2025b's <c>rtc/plugins/zoom/ZoomConfiguration</c> and <c>ZoomController</c>): seventeen
/// factors from 25% to 500%, Zoom In to the next and Zoom Out to the one before, Reset Zoom to 100%, Ctrl+wheel stepping once the wheel
/// has turned past a threshold, and one factor for every editor (<c>EnableSingleFileZoom</c> off).
/// </summary>
public static class ZoomRules
{
    /// <summary>The factors (<c>ZOOM_FACTORS</c>).</summary>
    public static IReadOnlyList<double> Factors { get; } = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    /// <summary>The factor Reset Zoom goes to.</summary>
    public const double Default = 1;

    /// <summary>How far the wheel turns, with Ctrl held, for one step (<c>SCROLL_THRESHOLD</c>; a WPF wheel click is 120).</summary>
    public const int ScrollThreshold = 53;

    /// <summary>The factor after <paramref name="factor"/> (<c>getNextZoomFactor</c>), the last at the top.</summary>
    public static double Next(double factor) => Factors[Math.Min(IndexOf(factor) + 1, Factors.Count - 1)];

    /// <summary>The factor before <paramref name="factor"/> (<c>getPreviousZoomFactor</c>), the first at the bottom.</summary>
    public static double Previous(double factor) => Factors[Math.Max(IndexOf(factor) - 1, 0)];

    /// <summary>Whether Zoom In can go on (<c>_canZoomIn</c>).</summary>
    public static bool CanZoomIn(double factor) => Factors[^1] > factor;

    /// <summary>Whether Zoom Out can go on (<c>_canZoomOut</c>).</summary>
    public static bool CanZoomOut(double factor) => Factors[0] < factor;

    /// <summary>Whether Reset Zoom does anything (<c>_canReset</c>).</summary>
    public static bool CanReset(double factor) => factor != Default;

    /// <summary>
    /// What the wheel asks for (<c>_handleGestureDelta</c>): <paramref name="accumulated"/> wheel delta (positive turning down) past the threshold
    /// is one Zoom Out, past minus the threshold one Zoom In; the accumulation starts again after a step.
    /// </summary>
    /// <returns>+1 for in, -1 for out, 0 for nothing yet.</returns>
    public static int WheelStep(ref double accumulated)
    {
        if (accumulated >= ScrollThreshold)
        {
            accumulated = 0;
            return -1;
        }

        if (accumulated <= -ScrollThreshold)
        {
            accumulated = 0;
            return 1;
        }

        return 0;
    }

    private static int IndexOf(double factor)
    {
        for (int i = 0; i < Factors.Count; i++)
        {
            if (Factors[i] == factor)
            {
                return i;
            }
        }

        // A factor not in the list counts as the one just below it (R2025b logs an error and answers -1, the next step from which is the first).
        int below = -1;
        for (int i = 0; i < Factors.Count && Factors[i] < factor; i++)
        {
            below = i;
        }

        return below;
    }
}
