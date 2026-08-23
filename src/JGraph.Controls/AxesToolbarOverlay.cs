using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Rendering;

namespace JGraph.Controls;

/// <summary>
/// The strip of buttons that appears at the top-right of an axes while the pointer is inside it
/// (MATLAB's <c>axtoolbar</c>).
/// <para>
/// It is drawn here rather than by <see cref="FigureRenderer"/>, and that is the whole design: a
/// toolbar is window chrome, so an export, a saved document and the <c>-batch</c> CLI must never
/// carry it. The figure renderer draws the figure; this draws over the top of it.
/// </para>
/// </summary>
internal static class AxesToolbarOverlay
{
    private const double ButtonSize = 20;
    private const double Gap = 2;
    private const double Inset = 4;

    /// <summary>Where each of an axes' toolbar buttons sits, left to right, or nothing when hidden.</summary>
    public static IReadOnlyList<(AxesToolbarButtonModel Button, Rect2D Box)> Layout(
        AxesModel axes, Rect2D plotArea)
    {
        if (axes.Toolbar is not { Visible: true } toolbar || toolbar.Buttons.Count == 0)
        {
            return [];
        }

        var placed = new List<(AxesToolbarButtonModel, Rect2D)>(toolbar.Buttons.Count);
        double width = (toolbar.Buttons.Count * ButtonSize) + ((toolbar.Buttons.Count - 1) * Gap);
        double left = plotArea.Right - width - Inset;
        double top = plotArea.Top + Inset;

        foreach (AxesToolbarButtonModel button in toolbar.Buttons)
        {
            placed.Add((button, new Rect2D(left, top, ButtonSize, ButtonSize)));
            left += ButtonSize + Gap;
        }

        return placed;
    }

    /// <summary>Draws the strip over the axes under the pointer, if there is one.</summary>
    public static void Draw(
        IRenderContext context, AxesModel? hovered, Rect2D plotArea, ITheme theme, InteractionModeKind mode)
    {
        if (hovered is null)
        {
            return;
        }

        Color ink = theme.AxisLine;
        Color face = theme.AxesBackground.WithOpacity(0.85);
        foreach ((AxesToolbarButtonModel button, Rect2D box) in Layout(hovered, plotArea))
        {
            bool down = button.Style == ToolbarButtonStyle.State
                ? button.Value
                : IsCurrent(button.Icon, mode);

            context.DrawPolygon(
                [new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Top),
                 new Point2D(box.Right, box.Bottom), new Point2D(box.Left, box.Bottom)],
                new LineStyle(ink.WithOpacity(0.4), 1),
                down ? ink.WithOpacity(0.25) : face);

            DrawGlyph(context, button.Icon, box, ink);
        }
    }

    /// <summary>The button under a pixel, or null. This is what turns a press into an action.</summary>
    public static AxesToolbarButtonModel? Hit(AxesModel axes, Rect2D plotArea, Point2D pixel)
    {
        foreach ((AxesToolbarButtonModel button, Rect2D box) in Layout(axes, plotArea))
        {
            if (pixel.X >= box.Left && pixel.X <= box.Right
                && pixel.Y >= box.Top && pixel.Y <= box.Bottom)
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>Whether a mode button stands for the tool that is currently chosen.</summary>
    private static bool IsCurrent(string icon, InteractionModeKind mode) => icon switch
    {
        "pan" or "rotate" => mode == InteractionModeKind.Pan,
        "datacursor" => mode == InteractionModeKind.DataTips,
        _ => false,
    };

    /// <summary>
    /// A small drawn mark for each button. Deliberately made of lines rather than of a font or an
    /// image: the render context draws lines on every backend, and a glyph that was missing on one
    /// of them would be a button nobody could tell from another.
    /// </summary>
    private static void DrawGlyph(IRenderContext context, string icon, Rect2D box, Color ink)
    {
        var pen = new LineStyle(ink, 1.4);
        double cx = box.Left + (box.Width / 2);
        double cy = box.Top + (box.Height / 2);
        double r = box.Width * 0.22;

        switch (icon)
        {
            case "zoomin":
            case "zoomout":
                Circle(context, cx - 1, cy - 1, r, pen);
                context.DrawLine(new Point2D(cx + r - 1, cy + r - 1), new Point2D(cx + r + 3, cy + r + 3), pen);
                context.DrawLine(new Point2D(cx - 1 - (r / 2), cy - 1), new Point2D(cx - 1 + (r / 2), cy - 1), pen);
                if (icon == "zoomin")
                {
                    context.DrawLine(new Point2D(cx - 1, cy - 1 - (r / 2)), new Point2D(cx - 1, cy - 1 + (r / 2)), pen);
                }

                break;

            case "pan":
                context.DrawLine(new Point2D(cx - r, cy), new Point2D(cx + r, cy), pen);
                context.DrawLine(new Point2D(cx, cy - r), new Point2D(cx, cy + r), pen);
                break;

            case "rotate":
                Circle(context, cx, cy, r, pen);
                context.DrawLine(new Point2D(cx + r, cy), new Point2D(cx + r - 3, cy - 3), pen);
                break;

            case "datacursor":
                context.DrawLine(new Point2D(cx - r, cy + r), new Point2D(cx + r, cy - r), pen);
                Circle(context, cx + r, cy - r, 2, pen);
                break;

            case "restoreview":
                context.DrawLine(new Point2D(cx - r, cy + r), new Point2D(cx - r, cy - r), pen);
                context.DrawLine(new Point2D(cx - r, cy + r), new Point2D(cx + r, cy + r), pen);
                context.DrawLine(new Point2D(cx - r, cy - r), new Point2D(cx + r, cy + r), pen);
                break;

            default:
                // export, and anything a script named that has no mark of its own: a page.
                context.DrawPolygon(
                    [new Point2D(cx - r, cy - r), new Point2D(cx + r, cy - r),
                     new Point2D(cx + r, cy + r), new Point2D(cx - r, cy + r)],
                    pen,
                    fill: null);
                break;
        }
    }

    private static void Circle(IRenderContext context, double cx, double cy, double r, LineStyle pen)
    {
        const int Steps = 12;
        var points = new Point2D[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            double angle = i * 2 * System.Math.PI / Steps;
            points[i] = new Point2D(cx + (r * System.Math.Cos(angle)), cy + (r * System.Math.Sin(angle)));
        }

        for (int i = 0; i < Steps; i++)
        {
            context.DrawLine(points[i], points[i + 1], pen);
        }
    }
}
