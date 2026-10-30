using System.Windows;
using System.Windows.Media;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// What the canvas draws for tab groups and trees (App Designer window plan, stage W5c): a tree's
/// selected nodes' rows, the + a tab group's strip ends in, the + beside a tree's selected node, and a
/// dragged tab heading with the marker where it would go.
/// </content>
public sealed partial class DesignCanvas
{
    private static readonly Brush PlusFace = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush PlusEdge = Frozen(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF)));
    private static readonly Brush PlusGlyph = Frozen(new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void DrawStructure(DrawingContext dc, DesignDocument document, DesignGestures gestures, Brush accent, Pen outline, Pen dashed)
    {
        // A tree's selected nodes, their rows filled and outlined (the tree's selection, updateNodeSelectionInView).
        var fill = new SolidColorBrush(accent is SolidColorBrush solid ? Color.FromArgb(0x40, solid.Color.R, solid.Color.G, solid.Color.B) : Color.FromArgb(0x40, 0x1E, 0x90, 0xFF));
        fill.Freeze();
        foreach (DesignComponent node in document.Selection)
        {
            if (node.Type == "matlab.ui.container.TreeNode" && gestures.TreeNodeBoxOf?.Invoke(node) is { } row)
            {
                dc.DrawRectangle(fill, outline, Inset(Wpf(row)));
            }
        }

        // The + each tab group's strip ends in, and the + beside a tree's selected node.
        foreach ((_, Rect2D plus) in gestures.TabPluses())
        {
            DrawPlus(dc, Wpf(plus), PlusGlyph);
        }

        if (gestures.TreeAddButton is { } add)
        {
            DrawPlus(dc, Wpf(add), accent);
        }

        // A dragged heading where the pointer has it, and the marker at the side of the heading it would go by.
        if (gestures.TabDragged is { } dragged)
        {
            dc.DrawRectangle(null, dashed, Inset(Wpf(dragged)));
        }

        if (gestures.TabDrop is { } drop)
        {
            var marker = new Pen(accent, 2);
            marker.Freeze();
            Rect h = Wpf(drop.Heading);
            if (drop.Across)
            {
                double x = drop.After ? h.Right - 1 : h.Left + 1;
                dc.DrawLine(marker, new Point(x, h.Top), new Point(x, h.Bottom));
            }
            else
            {
                double y = drop.After ? h.Bottom - 1 : h.Top + 1;
                dc.DrawLine(marker, new Point(h.Left, y), new Point(h.Right, y));
            }
        }
    }

    private static void DrawPlus(DrawingContext dc, Rect box, Brush glyph)
    {
        var edge = new Pen(PlusEdge, 1);
        edge.Freeze();
        dc.DrawRectangle(PlusFace, edge, Inset(box));
        var stroke = new Pen(glyph, 1.5);
        stroke.Freeze();
        double cx = box.X + (box.Width / 2);
        double cy = box.Y + (box.Height / 2);
        double arm = System.Math.Min(box.Width, box.Height) / 4;
        dc.DrawLine(stroke, new Point(cx - arm, cy), new Point(cx + arm, cy));
        dc.DrawLine(stroke, new Point(cx, cy - arm), new Point(cx, cy + arm));
    }
}
