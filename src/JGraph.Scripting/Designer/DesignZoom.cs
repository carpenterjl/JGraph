using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// The canvas's zoom (App Designer window plan, stage W6), as R2025b's App Designer has it
/// (<c>appdesigner/view/zoom/ZoomConstants</c>, <c>appdesigner/util/ZoomUtil</c>, <c>ZoomAction</c>,
/// <c>DesignViewKeyboardHandler</c>, <c>DesignAreaViewController</c>): 21 steps from half size to four
/// times; Zoom In and Zoom Out a step at a time, Reset Zoom back to 1, Fit to View the step below the
/// one nearest to fitting the figure and 30 pixels round it into the view.
/// </summary>
public static class DesignZoom
{
    /// <summary>The zoom levels (<c>ZOOM_STEPS</c>).</summary>
    public static readonly IReadOnlyList<double> Steps = [0.5, 0.55, 0.6, 0.7, 0.8, 0.9, 1, 1.12, 1.25, 1.37, 1.5, 1.62, 1.75, 1.87, 2, 2.12, 2.5, 2.75, 3, 3.5, 4];

    /// <summary>The space Fit to View leaves round the figure, in the figure's pixels (<c>getComponentsBounds</c>'s 30).</summary>
    public const double FitMargin = 30;

    /// <summary>How long Space must be held before it pans rather than fits the view (<c>_spacePressed</c>'s timer).</summary>
    public const int SpaceHoldMilliseconds = 200;

    /// <summary>How many times a zoom shows <see cref="PanMessage"/> in a session, until Space has been held once (<c>PanMessageCount</c>).</summary>
    public const int PanMessageTimes = 5;

    /// <summary>How long the pan message shows, in milliseconds.</summary>
    public const int PanMessageMilliseconds = 2000;

    /// <summary>What a zoom tells the user about panning (<c>panTooltip</c>, its Space in bold).</summary>
    public const string PanMessage = "Hold Space and drag to pan";

    /// <summary>The floating zoom panel's reset button (<c>floatingButtonZoomReset</c>).</summary>
    public const string ResetText = "Reset";

    /// <summary>Whether Zoom In can go further (<c>ZoomAction</c>: not at the last step or past it).</summary>
    public static bool CanZoomIn(double level) => level < Steps[^1];

    /// <summary>Whether Zoom Out can go further (not at the first step or below it).</summary>
    public static bool CanZoomOut(double level) => level > Steps[0];

    /// <summary>Whether Reset Zoom applies: the zoom is not 1.</summary>
    public static bool CanReset(double level) => level != 1;

    /// <summary>
    /// The level after Zoom In (<c>calculateZoomLevel</c>): the next step, the level taken to its
    /// hundredth first; from below the first step the first, from past the last the last. From a
    /// level between two steps - which only Fit to View gives - the next step up.
    /// </summary>
    public static double ZoomIn(double level) => Step(level, up: true);

    /// <summary>The level after Zoom Out: the step before, as <see cref="ZoomIn"/> goes the other way.</summary>
    public static double ZoomOut(double level) => Step(level, up: false);

    private static double Step(double level, bool up)
    {
        double at = System.Math.Floor(level * 100) / 100;
        int index = IndexOf(at);
        if (index < 0)
        {
            if (at < Steps[0])
            {
                return Steps[0];
            }

            if (at > Steps[^1])
            {
                return Steps[^1];
            }

            // Between two steps: R2025b's index is -1 here and both go to the first step; JGraph goes on to the next.
            return up ? Steps.First(s => s > at) : Steps.Last(s => s < at);
        }

        return up ? Steps[System.Math.Min(index + 1, Steps.Count - 1)] : Steps[System.Math.Max(index - 1, 0)];
    }

    private static int IndexOf(double level)
    {
        for (int i = 0; i < Steps.Count; i++)
        {
            if (Steps[i] == level)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Fit to View's level (<c>calculateFitToViewZoomLevel</c>): the zoom at which the figure and
    /// <see cref="FitMargin"/> round it fill <paramref name="viewport"/> across or down, whichever is
    /// less; then the step below the nearest step to it - or itself when that is the first step or it
    /// is past the steps either way.
    /// </summary>
    /// <param name="viewport">The view's area, without its scroll bars.</param>
    /// <param name="figure">The figure's size at zoom 1.</param>
    public static double FitToView(Size2D viewport, Size2D figure)
    {
        double across = viewport.Width / (figure.Width + (2 * FitMargin));
        double down = viewport.Height / (figure.Height + (2 * FitMargin));
        double fit = System.Math.Min(across, down);
        if (fit < Steps[0] || fit > Steps[^1])
        {
            return fit;
        }

        // reduce over the steps: a later step wins only when strictly nearer.
        double nearest = Steps[0];
        foreach (double step in Steps)
        {
            if (System.Math.Abs(step - fit) < System.Math.Abs(nearest - fit))
            {
                nearest = step;
            }
        }

        int index = IndexOf(nearest);
        return index == 0 ? fit : Steps[index - 1];
    }

    /// <summary>The floating zoom panel's percentage (<c>updateZoomLevel</c>): the level in hundredths, rounded, and "%".</summary>
    public static string Percent(double level) => ((int)DesignArrange.JsRound(level * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
}
