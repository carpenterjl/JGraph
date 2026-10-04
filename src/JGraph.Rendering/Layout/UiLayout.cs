using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering.Layout;

/// <summary>Where one component goes on the figure's surface, top-left origin, in DIPs.</summary>
public readonly record struct UiPlacement(UiControlFrame Control, Rect2D Box, bool Visible);

/// <summary>
/// The pure layout of a frame (app-building plan, section A): from a <see cref="UiFrame"/> and the
/// size of the figure's drawable area to the rectangle each component occupies. The window's component
/// layer and, later, the headless export read the same answer, and a window resize runs it again on
/// the last frame without asking the script. U1 places in absolute pixels only; containers, units and
/// the grid arrive in U2 and U5.
/// </summary>
public static class UiLayout
{
    /// <summary>
    /// MATLAB places a component by its bottom-left corner, 1-based, in pixels of 1/96 inch — a DIP
    /// (measured at 125 % in U0) — so pixel 1 is the figure's edge.
    /// </summary>
    public static IReadOnlyList<UiPlacement> Place(UiFrame frame, Size2D figure)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var placed = new List<UiPlacement>(frame.Controls.Count);
        foreach (UiControlFrame control in frame.Controls)
        {
            Rect2D position = control.Position;
            var box = new Rect2D(
                position.X - 1,
                figure.Height - (position.Y - 1) - position.Height,
                position.Width,
                position.Height);
            placed.Add(new UiPlacement(control, box, control.Visible));
        }

        return placed;
    }

    /// <summary>
    /// The font a component names, as the machine has it: MATLAB's classic default MS Sans Serif is a
    /// bitmap font Windows maps to Microsoft Sans Serif, and Helvetica, the uifigure default, is Arial
    /// here. One table, so text is measured and drawn in the same face.
    /// </summary>
    public static string FontFamily(string name) => name.Trim().ToLowerInvariant() switch
    {
        "ms sans serif" => "Microsoft Sans Serif",
        "helvetica" => "Arial",
        "fixedwidth" => "Courier New",
        "" => "Microsoft Sans Serif",
        _ => name,
    };
}
