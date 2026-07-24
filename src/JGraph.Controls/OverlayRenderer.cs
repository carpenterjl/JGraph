using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Interaction;
using JGraph.Rendering;

namespace JGraph.Controls;

/// <summary>
/// Draws transient interaction overlays (the rubber-band zoom rectangle and the selection highlight)
/// on top of the rendered figure, using the same backend-independent <see cref="IRenderContext"/>.
/// Data tips are no longer an overlay: since M21 they are persistent
/// <see cref="JGraph.Objects.Annotations.DataTipAnnotation"/> model objects drawn by the figure
/// renderer itself.
/// </summary>
internal static class OverlayRenderer
{
    private static readonly Color SelectionFill = Color.FromArgb(48, 0x1E, 0x88, 0xE5);
    private static readonly Color SelectionEdge = Color.FromArgb(220, 0x1E, 0x88, 0xE5);
    private const double HandleSize = 6;

    public static void Draw(IRenderContext context, InteractionController controller, ITheme theme)
    {
        if (controller.Selection.Selected is { } selected)
        {
            DrawSelectionHighlight(context, controller.Surface, selected);
        }

        if (controller.RubberBand is { } rect && rect.Width > 0 && rect.Height > 0)
        {
            context.DrawRectangle(rect, new LineStyle(SelectionEdge, 1, DashStyle.Dash), SelectionFill);
        }
    }

    private static void DrawSelectionHighlight(IRenderContext context, IInteractionSurface surface, GraphObject selected)
    {
        Rect2D? bounds = SelectionBoundsOf(surface, selected);
        if (bounds is not { } b || b.IsEmpty)
        {
            return;
        }

        context.DrawRectangle(b, new LineStyle(SelectionEdge, 1, DashStyle.Dash), fill: null);

        // Corner handles, VS/MATLAB style.
        Span<Point2D> corners = stackalloc Point2D[4];
        corners[0] = new Point2D(b.Left, b.Top);
        corners[1] = new Point2D(b.Right, b.Top);
        corners[2] = new Point2D(b.Right, b.Bottom);
        corners[3] = new Point2D(b.Left, b.Bottom);
        foreach (Point2D corner in corners)
        {
            var handle = new Rect2D(
                corner.X - (HandleSize / 2),
                corner.Y - (HandleSize / 2),
                HandleSize,
                HandleSize);
            context.DrawRectangle(handle, new LineStyle(SelectionEdge, 1), Colors.White);
        }
    }

    /// <summary>Computes the device-space highlight rectangle for the selected object, if it has one.</summary>
    private static Rect2D? SelectionBoundsOf(IInteractionSurface surface, GraphObject selected)
    {
        switch (selected)
        {
            case AnnotationObject annotation when annotation.Visible:
                return annotation.RenderedBounds.IsEmpty
                    ? null
                    : Inflate(annotation.RenderedBounds, 3);

            case PlotObject plot when plot.Axes is { } plotAxes:
            {
                if (surface.GetMapper(plotAxes) is not { } mapper)
                {
                    return null;
                }

                DataRange x = plot.GetXDataBounds();
                DataRange y = plot.GetYDataBounds();
                if (x.IsEmpty || y.IsEmpty)
                {
                    return null;
                }

                Rect2D dataRect = Rect2D.FromCorners(
                    mapper.DataToPixel(x.Min, y.Min),
                    mapper.DataToPixel(x.Max, y.Max));
                Rect2D? clipped = Intersect(dataRect, mapper.PlotArea);
                return clipped is { } c ? Inflate(c, 2) : null;
            }

            case LegendModel legend when legend.Parent is AxesModel legendAxes:
                return surface.GetLegendBounds(legendAxes) is { } legendBounds
                    ? Inflate(legendBounds, 2)
                    : null;

            case AxesModel axes:
                return surface.GetMapper(axes) is { } axesMapper ? Inflate(axesMapper.PlotArea, 1) : null;

            case AxisModel axis when axis.Parent is AxesModel owner:
                return surface.GetMapper(owner) is { } ownerMapper ? Inflate(ownerMapper.PlotArea, 1) : null;

            default:
                return null;
        }
    }

    private static Rect2D Inflate(Rect2D rect, double amount) => new(
        rect.X - amount,
        rect.Y - amount,
        rect.Width + (2 * amount),
        rect.Height + (2 * amount));

    private static Rect2D? Intersect(Rect2D a, Rect2D b)
    {
        double left = System.Math.Max(a.Left, b.Left);
        double top = System.Math.Max(a.Top, b.Top);
        double right = System.Math.Min(a.Right, b.Right);
        double bottom = System.Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new Rect2D(left, top, right - left, bottom - top) : null;
    }

}
