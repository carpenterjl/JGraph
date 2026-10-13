using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>ScrollableViewportLocation</c> and the arithmetic behind <c>scroll</c> on a container (open
/// item 47), measured in R2025b (probe <c>probe_47</c>). The location is <c>[x y]</c> in pixels of
/// the container's own coordinates: <c>x</c> counts from 1 at the left of the content; a grid's
/// <c>y</c> is 1 at the top and falls below it as the grid scrolls down, a panel's and a figure's
/// <c>y</c> is 1 at the bottom and rises as they scroll up. A pair a script writes reads back as
/// written until the container scrolls again; <c>scroll</c> keeps its location within range; a
/// container that is not scrollable answers <c>[1 1]</c>.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private const string ViewportLocation = "ScrollableViewportLocation";

    /// <summary>Gives a grid's, a panel's, a tab's, a button group's or a figure's table the hidden property.</summary>
    private static void AddScrollableViewportLocation(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, ViewportLocation,
            entry => ViewportLocationValue(entry.Target),
            (entry, value, line, col) => SetViewportLocation(entry, value, line, col));
        Unlist(table, ViewportLocation);
    }

    /// <summary>Whether a container's <c>y</c> counts down from its top: a grid's does, a panel's and a figure's count up from the bottom.</summary>
    private static bool ScrollsFromTop(GraphObject target) => target is UiGridLayoutModel;

    /// <summary>What a container has to show and what it shows at once, from its model, on the script's side.</summary>
    private static UiScrollMeasure MeasureOf(GraphObject target) => target switch
    {
        UiGridLayoutModel grid => UiScrolling.MeasureGrid(grid),
        UiContainerModel container => UiScrolling.MeasureContainer(container),
        FigureModel figure => UiScrolling.MeasureFigure(figure),
        _ => new UiScrollMeasure(default, default, false, false),
    };

    private static (bool Scrollable, double ScrollX, double ScrollY, Point2D? Written) ScrollStateOf(GraphObject target) => target switch
    {
        UiContainerModel container => (container.Scrollable, container.ScrollX, container.ScrollY, container.ViewportLocationWritten),
        FigureModel figure => (figure.Scrollable, figure.ScrollX, figure.ScrollY, figure.ViewportLocationWritten),
        _ => (false, 0, 0, null),
    };

    private static void SetScrollOffsets(GraphObject target, double x, double y, Point2D? written)
    {
        switch (target)
        {
            case UiContainerModel container:
                container.ScrollX = x;
                container.ScrollY = y;
                container.ViewportLocationWritten = written;
                break;
            case FigureModel figure:
                figure.ScrollX = x;
                figure.ScrollY = y;
                figure.ViewportLocationWritten = written;
                break;
        }
    }

    private static JgsValue ViewportLocationValue(GraphObject target)
    {
        (bool scrollable, double scrollX, double scrollY, Point2D? written) = ScrollStateOf(target);
        if (!scrollable)
        {
            return Row(1, 1);
        }

        if (written is { } pair)
        {
            return Row(pair.X, pair.Y);
        }

        UiScrollMeasure measure = MeasureOf(target);
        double x = 1 + measure.ClampX(scrollX);
        double down = measure.ClampY(scrollY);
        double y = ScrollsFromTop(target) ? 1 - down : 1 + (measure.MaxY - down);
        return Row(x, y);
    }

    private static void SetViewportLocation(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.Type is not (JgsType.Number or JgsType.Array) || value.IsStringArray || value.IsCharMatrix || kind == "logical"
            || JgsNumericClasses.Parse(kind) is null)
        {
            throw ComponentError(entry, ViewportLocation, "MATLAB:datatypes:Point2dDataType:ArrayClass", "Input is unsupported class", line, col);
        }

        int count = value.Type == JgsType.Number ? 1 : value.ArrayLength;
        if (count != 2)
        {
            throw ComponentError(entry, ViewportLocation, "MATLAB:datatypes:Point2dDataType:ArrayShape", "Input must be 1x2", line, col);
        }

        double[] pair = JgsBuiltins.ToDoubles(ViewportLocation, value, line, col);
        if (!double.IsFinite(pair[0]) || !double.IsFinite(pair[1]))
        {
            throw ComponentError(entry, ViewportLocation, "MATLAB:datatypes:Point2dDataType:ArrayData", "Input must be finite numbers", line, col);
        }

        // The pair is kept as written; the layout keeps the offsets it means within range.
        UiScrollMeasure measure = MeasureOf(entry.Target);
        double down = ScrollsFromTop(entry.Target) ? 1 - pair[1] : measure.MaxY - (pair[1] - 1);
        SetScrollOffsets(entry.Target, pair[0] - 1, down, new Point2D(pair[0], pair[1]));
    }

    /// <summary>
    /// <c>scroll(c, 'top' | 'bottom' | 'left' | 'right')</c> and <c>scroll(c, x, y)</c> on a
    /// container that scrolls: the location to show, kept within what there is to scroll (probe_47:
    /// <c>scroll(g, 1, 100)</c> on a grid is its top, <c>scroll(g, -5, -5)</c> five pixels down).
    /// </summary>
    internal static void ScrollTo(GraphObject target, string? word, double[]? pair)
    {
        (bool _, double scrollX, double scrollY, Point2D? _) = ScrollStateOf(target);
        UiScrollMeasure measure = MeasureOf(target);
        double x = measure.ClampX(scrollX);
        double y = measure.ClampY(scrollY);
        bool fromTop = ScrollsFromTop(target);
        if (pair is not null)
        {
            x = measure.ClampX(pair[0] - 1);
            y = measure.ClampY(fromTop ? 1 - pair[1] : measure.MaxY - (pair[1] - 1));
        }
        else
        {
            switch (word)
            {
                case "top":
                    y = 0;
                    break;
                case "bottom":
                    y = measure.MaxY;
                    break;
                case "left":
                    x = 0;
                    break;
                case "right":
                    x = measure.MaxX;
                    break;
            }
        }

        SetScrollOffsets(target, x, y, null);
    }
}
