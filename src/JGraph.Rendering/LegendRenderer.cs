using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// Draws an axes' legend from its plots' <see cref="ILegendItem"/> keys. Entry colors are resolved
/// from the theme palette using the same per-plot indexing as the plot renderer, so a series' legend
/// swatch always matches how it is drawn.
/// </summary>
internal static class LegendRenderer
{
    private const double SwatchWidth = 26;
    private const double SwatchGap = 6;
    private const double Padding = 8;
    private const double RowGap = 4;
    private const double Inset = 10;

    public static void Draw(IRenderContext context, AxesModel axes, Rect2D plotArea, ITheme theme)
    {
        IReadOnlyList<Color> palette = theme.SeriesPalette;
        var entries = new List<(string Label, LegendKey Key)>();

        int colorIndex = 0;
        foreach (PlotObject plot in axes.Plots.InDrawOrder())
        {
            Color color = palette.Count > 0 ? palette[colorIndex % palette.Count] : Colors.Black;
            colorIndex++;

            if (!plot.Visible || plot is not ILegendItem item || string.IsNullOrEmpty(item.LegendLabel))
            {
                continue;
            }

            entries.Add((item.LegendLabel, item.GetLegendKey(color)));
        }

        if (entries.Count == 0)
        {
            return;
        }

        LegendModel legend = axes.Legend;
        double rowHeight = 0;
        double maxLabelWidth = 0;
        foreach ((string label, _) in entries)
        {
            Size2D size = context.MeasureText(label, legend.TextStyle);
            rowHeight = System.Math.Max(rowHeight, size.Height);
            maxLabelWidth = System.Math.Max(maxLabelWidth, size.Width);
        }

        double boxWidth = Padding + SwatchWidth + SwatchGap + maxLabelWidth + Padding;
        double boxHeight = Padding + (entries.Count * rowHeight) + ((entries.Count - 1) * RowGap) + Padding;

        Rect2D box = PlaceBox(legend.Position, plotArea, boxWidth, boxHeight);

        LineStyle? border = legend.ShowBorder ? new LineStyle(legend.BorderColor, 1) : null;
        context.DrawRectangle(box, border, legend.Background);

        double y = box.Top + Padding;
        foreach ((string label, LegendKey key) in entries)
        {
            double rowCenterY = y + (rowHeight / 2);
            double swatchLeft = box.Left + Padding;
            DrawSwatch(context, key, swatchLeft, rowCenterY);

            context.DrawText(
                label,
                new Point2D(swatchLeft + SwatchWidth + SwatchGap, rowCenterY),
                legend.TextStyle,
                HorizontalAlignment.Left,
                VerticalAlignment.Middle);

            y += rowHeight + RowGap;
        }
    }

    private static void DrawSwatch(IRenderContext context, LegendKey key, double left, double centerY)
    {
        if (key.Swatch is { } swatch)
        {
            var rect = new Rect2D(left, centerY - 5, SwatchWidth, 10);
            context.DrawRectangle(rect, stroke: null, fill: swatch);
        }

        if (key.Line is { } line)
        {
            context.DrawLine(
                new Point2D(left, centerY),
                new Point2D(left + SwatchWidth, centerY),
                line);
        }

        if (key.Marker is { } marker && marker.IsVisible)
        {
            Span<Point2D> center = stackalloc Point2D[1];
            center[0] = new Point2D(left + (SwatchWidth / 2), centerY);
            context.DrawMarkers(center, marker, marker.Edge ?? Colors.Black);
        }
    }

    private static Rect2D PlaceBox(LegendPosition position, Rect2D plotArea, double width, double height)
    {
        double left = position switch
        {
            LegendPosition.TopLeft or LegendPosition.BottomLeft or LegendPosition.Left => plotArea.Left + Inset,
            LegendPosition.Top or LegendPosition.Bottom => plotArea.CenterX - (width / 2),
            _ => plotArea.Right - width - Inset,
        };

        double top = position switch
        {
            LegendPosition.BottomLeft or LegendPosition.BottomRight or LegendPosition.Bottom => plotArea.Bottom - height - Inset,
            LegendPosition.Left or LegendPosition.Right => plotArea.CenterY - (height / 2),
            _ => plotArea.Top + Inset,
        };

        return new Rect2D(left, top, width, height);
    }
}
