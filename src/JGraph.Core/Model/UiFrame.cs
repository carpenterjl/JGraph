using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// One <c>uicontrol</c> as a frame snapshot holds it: every value the window needs to draw and place
/// it, copied out of the model on the script thread, so the interface thread never reads a model the
/// script is writing. <c>Source</c> is there to route a user's action back and is never read;
/// <c>Position</c> is MATLAB's (1-based left and bottom edges, then the size); <c>FontSize</c> is in
/// pixels of 1/96 inch; <c>UserWriteSeq</c> is the last user edit the model had taken.
/// </summary>
public sealed record UiControlFrame(
    UiControlModel Source,
    UiControlStyle Style,
    Rect2D Position,
    bool Visible,
    UiEnable Enable,
    UiText Text,
    UiHorizontalAlignment Alignment,
    UiColor? Background,
    UiColor? Foreground,
    string FontName,
    double FontSize,
    bool Bold,
    bool Italic,
    string Tooltip,
    long UserWriteSeq);

/// <summary>
/// An immutable picture of a figure's components at one flush (app-building plan, section A). The
/// script thread takes it at a flush point and hands it to the window, which lays it out and applies
/// it; a window resize lays the last one out again without asking the script.
/// </summary>
public sealed class UiFrame
{
    private UiFrame(FigureModel figure, long sequence, IReadOnlyList<UiControlFrame> controls)
    {
        Figure = figure;
        Sequence = sequence;
        Controls = controls;
    }

    /// <summary>A frame with nothing in it.</summary>
    public static UiFrame Empty(FigureModel figure) => new(figure, 0, []);

    public FigureModel Figure { get; }

    /// <summary>Increases with every frame taken, process-wide; a later frame supersedes an earlier.</summary>
    public long Sequence { get; }

    /// <summary>The components in creation order, which is also back-to-front.</summary>
    public IReadOnlyList<UiControlFrame> Controls { get; }

    private static long _sequence;

    /// <summary>Copies the figure's components. Call on the thread that writes the model.</summary>
    public static UiFrame Take(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var controls = new List<UiControlFrame>(figure.Components.Count);
        foreach (UiObject component in figure.Components)
        {
            if (component is UiControlModel control)
            {
                controls.Add(new UiControlFrame(
                    control,
                    control.Style,
                    control.Position,
                    control.Visible,
                    control.Enable,
                    control.Text,
                    control.HorizontalAlignment,
                    control.BackgroundColor,
                    control.ForegroundColor,
                    control.FontName,
                    control.FontSizeInPixels(control.Position.Height),
                    control.FontWeight is "bold" or "demi",
                    control.FontAngle is "italic" or "oblique",
                    control.Tooltip.Joined,
                    control.UserWriteSeq));
            }
        }

        return new UiFrame(figure, Interlocked.Increment(ref _sequence), controls);
    }
}
