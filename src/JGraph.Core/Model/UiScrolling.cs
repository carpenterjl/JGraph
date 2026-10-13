using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// What a scrollable container has to show and what it can show at once (open item 47): the
/// content, the viewport the bars leave, and how far either can scroll. Measured in R2025b
/// (probe <c>probe_47</c>): a panel's and a figure's content reaches from their bottom-left corner
/// up and right as far as the farthest child, never below or left of it; the inner area itself
/// is always content; a bar is 16 pixels; a grid's content is its tracks (<see cref="UiGridMath"/>).
/// </summary>
/// <param name="Content">The size of everything there is to show.</param>
/// <param name="Viewport">The size of the part shown at once: the inner area less the bars.</param>
/// <param name="Vertical">Whether a bar runs down the right edge.</param>
/// <param name="Horizontal">Whether a bar runs along the bottom edge.</param>
public readonly record struct UiScrollMeasure(Size2D Content, Size2D Viewport, bool Vertical, bool Horizontal)
{
    /// <summary>How far the content can scroll across, in pixels; zero when it fits.</summary>
    public double MaxX => System.Math.Max(0, Content.Width - Viewport.Width);

    /// <summary>How far the content can scroll down, in pixels; zero when it fits.</summary>
    public double MaxY => System.Math.Max(0, Content.Height - Viewport.Height);

    /// <summary>An offset kept within what there is to scroll.</summary>
    public double ClampX(double x) => double.IsNaN(x) ? 0 : System.Math.Clamp(x, 0, MaxX);

    /// <inheritdoc cref="ClampX" />
    public double ClampY(double y) => double.IsNaN(y) ? 0 : System.Math.Clamp(y, 0, MaxY);
}

/// <summary>The arithmetic of scrolling a panel, a tab, a button group or a <c>uifigure</c> (open item 47).</summary>
public static class UiScrolling
{
    /// <summary>The thickness of a panel's or a figure's scroll bar, as R2025b draws it (probe_47: 16 pixels).</summary>
    public const double BarSize = 16;

    /// <summary>
    /// Measures a container whose content reaches <paramref name="right"/> pixels across and
    /// <paramref name="top"/> pixels up from its inner area's bottom-left corner. A bar takes room
    /// from the viewport, which can make the other direction overflow in its turn: two passes settle it.
    /// </summary>
    public static UiScrollMeasure Measure(Size2D inner, double right, double top)
    {
        var content = new Size2D(System.Math.Max(inner.Width, right), System.Math.Max(inner.Height, top));
        bool vertical = false;
        bool horizontal = false;
        for (int pass = 0; ; pass++)
        {
            var view = new Size2D(
                System.Math.Max(0, inner.Width - (vertical ? BarSize : 0)),
                System.Math.Max(0, inner.Height - (horizontal ? BarSize : 0)));
            bool down = vertical || content.Height > view.Height + 0.5;
            bool across = horizontal || content.Width > view.Width + 0.5;
            if ((down == vertical && across == horizontal) || pass == 2)
            {
                return new UiScrollMeasure(content, view, vertical, horizontal);
            }

            vertical = down;
            horizontal = across;
        }
    }

    /// <summary>How far right and how far up the children of a container reach, from its inner area's bottom-left corner.</summary>
    public static (double Right, double Top) Extent(IEnumerable<UiObject> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        double right = 0;
        double top = 0;
        foreach (UiObject child in children)
        {
            if (child is UiGridLayoutModel)
            {
                continue; // a grid fills its parent and has no place of its own
            }

            // R2025b counts a child's far edge as its position plus its size (probe_47: a label at
            // y = 300, 20 high, in a 200-pixel figure scrolls to y = 137, which is 320 less 184).
            Rect2D box = child.PixelPosition();
            right = System.Math.Max(right, box.X + box.Width);
            top = System.Math.Max(top, box.Y + box.Height);
        }

        return (right, top);
    }

    /// <summary>A panel's, a tab's or a button group's measure, from its model.</summary>
    public static UiScrollMeasure MeasureContainer(UiContainerModel container)
    {
        ArgumentNullException.ThrowIfNull(container);
        (double right, double top) = Extent(container.Components);
        return Measure(container.InnerPixelSize, right, top);
    }

    /// <summary>A <c>uifigure</c>'s measure, from its model.</summary>
    public static UiScrollMeasure MeasureFigure(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        (double right, double top) = Extent(figure.Components);
        return Measure(figure.Size, right, top);
    }

    /// <summary>A grid's measure: its tracks against its own rectangle, with its own 17-pixel bars.</summary>
    public static UiScrollMeasure MeasureGrid(UiGridLayoutModel grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        UiGridScrolled scrolled = grid.Scrolled();
        return new UiScrollMeasure(scrolled.Arrangement.Content, scrolled.Viewport, scrolled.Vertical, scrolled.Horizontal);
    }
}
