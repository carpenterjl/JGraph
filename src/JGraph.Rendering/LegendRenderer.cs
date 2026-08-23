using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// Draws an axes' legend: one row per <see cref="LegendEntryModel"/>, in the order the entries are
/// listed. Entry colors are resolved from the theme palette using the same per-plot indexing as the
/// plot renderer, so a series' legend swatch always matches how it is drawn — and because that index
/// follows draw order rather than row order, hiding or reordering rows never re-colors a series.
/// </summary>
internal static class LegendRenderer
{
    private const double SwatchWidth = 26;
    private const double SwatchGap = 6;
    private const double Padding = 8;
    private const double RowGap = 4;
    private const double Inset = 10;

    /// <summary>
    /// Reconciles the legend's rows with the plots that can appear in one. Kept here because deciding
    /// which plots those are needs <see cref="ILegendItem"/>, which the model layer cannot see.
    /// </summary>
    public static bool SyncEntries(AxesModel axes) =>
        axes.Legend.SyncEntries(
            axes.Plots.Where(static p => p is ILegendItem && p.ShowsInLegend));

    /// <summary>
    /// Draws the legend and returns where it landed, or null when nothing was drawn. Each row's own
    /// rectangle comes back beside the box, so a click can be traced to the series it names without
    /// the interaction layer having to lay the legend out again.
    /// </summary>
    public static LegendLayout? Draw(IRenderContext context, AxesModel axes, Rect2D plotArea, ITheme theme)
    {
        IReadOnlyList<Color> palette = SeriesPalette.Of(axes, theme);

        // Palette index follows draw order, never row order (see the type comment).
        var colors = new Dictionary<PlotObject, Color>();
        int colorIndex = 0;
        foreach (PlotObject plot in axes.Plots.InDrawOrder())
        {
            colors[plot] = SeriesPalette.Resolve(palette, plot, colorIndex);
            colorIndex++;
        }

        LegendModel legend = axes.Legend;
        var entries = new List<(string Label, LegendKey Key, PlotObject Plot, bool Dimmed)>();
        foreach (LegendEntryModel entry in legend.Entries)
        {
            if (!entry.Visible
                || entry.Plot is not { } plot
                || plot is not ILegendItem item)
            {
                continue;
            }

            string label = entry.ResolveLabel(item.LegendLabel);
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            // A hidden series keeps its row, drawn faded — clicking that row is how the series
            // comes back, so removing it would strand the series off-screen.
            LegendKey key = item.GetLegendKey(colors.TryGetValue(plot, out Color c) ? c : Colors.Black);
            entries.Add((label, key, plot, !plot.Visible));
        }

        if (entries.Count == 0)
        {
            return null;
        }

        double rowHeight = 0;
        double maxLabelWidth = 0;
        foreach ((string label, _, _, _) in entries)
        {
            Size2D size = context.MeasureText(label, legend.TextStyle);
            rowHeight = System.Math.Max(rowHeight, size.Height);
            maxLabelWidth = System.Math.Max(maxLabelWidth, size.Width);
        }

        double boxWidth = Padding + SwatchWidth + SwatchGap + maxLabelWidth + Padding;
        double boxHeight = Padding + (entries.Count * rowHeight) + ((entries.Count - 1) * RowGap) + Padding;

        Rect2D box = PlaceBox(legend, plotArea, boxWidth, boxHeight);

        LineStyle? border = legend.ShowBorder ? new LineStyle(legend.BorderColor, 1) : null;
        context.DrawRectangle(box, border, legend.Background);

        double y = box.Top + Padding;
        var rows = new List<LegendRowBounds>(entries.Count);
        foreach ((string label, LegendKey key, PlotObject plot, bool dimmed) in entries)
        {
            double rowCenterY = y + (rowHeight / 2);
            double swatchLeft = box.Left + Padding;
            DrawSwatch(context, dimmed ? Dim(key) : key, swatchLeft, rowCenterY);

            TextStyle text = legend.TextStyle;
            if (dimmed)
            {
                text = new TextStyle(
                    text.Color.WithOpacity(DimOpacity), text.FontSize, text.FontFamily, text.Bold, text.Italic);
            }

            context.DrawText(
                label,
                new Point2D(swatchLeft + SwatchWidth + SwatchGap, rowCenterY),
                text,
                HorizontalAlignment.Left,
                VerticalAlignment.Middle);

            // The clickable row spans the whole box width, not just the drawn glyphs — a click just
            // right of a short label is still a click on that row.
            rows.Add(new LegendRowBounds(plot, new Rect2D(box.Left, y, box.Width, rowHeight)));
            y += rowHeight + RowGap;
        }

        return new LegendLayout(box, rows);
    }

    /// <summary>How much of a hidden series' ink survives in its dimmed legend row.</summary>
    private const double DimOpacity = 0.35;

    /// <summary>The same key with every color faded, for the row of a hidden series.</summary>
    private static LegendKey Dim(LegendKey key) => new(
        key.Line is { } line ? line.WithColor(line.Color.WithOpacity(DimOpacity)) : null,
        key.Marker is { } marker
            ? new MarkerStyle(
                marker.Type,
                marker.Size,
                marker.Fill?.WithOpacity(DimOpacity),
                (marker.Edge ?? Colors.Black).WithOpacity(DimOpacity),
                marker.EdgeWidth)
            : null,
        key.Swatch?.WithOpacity(DimOpacity),
        key.Outline is { } outline ? outline.WithColor(outline.Color.WithOpacity(DimOpacity)) : null);

    private static void DrawSwatch(IRenderContext context, LegendKey key, double left, double centerY)
    {
        if (key.Swatch is { } swatch)
        {
            var rect = new Rect2D(left, centerY - 5, SwatchWidth, 10);
            context.DrawRectangle(rect, key.Outline, fill: swatch);
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

    /// <summary>
    /// Resolves the legend box's device-space rectangle: one of the eight presets, or the explicit
    /// <see cref="LegendModel.Location"/> (a fraction of the plot area) when the position is
    /// <see cref="LegendPosition.Custom"/>. A custom box is clamped to stay inside the plot area.
    /// </summary>
    internal static Rect2D PlaceBox(LegendModel legend, Rect2D plotArea, double width, double height)
    {
        LegendPosition position = legend.Position;
        if (position == LegendPosition.Custom)
        {
            return new Rect2D(
                Clamp(plotArea.Left + (legend.Location.X * plotArea.Width), plotArea.Left, plotArea.Right - width),
                Clamp(plotArea.Top + (legend.Location.Y * plotArea.Height), plotArea.Top, plotArea.Bottom - height),
                width,
                height);
        }

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

    /// <summary>Clamps to [min, max], preferring <paramref name="min"/> when the box does not fit.</summary>
    private static double Clamp(double value, double min, double max) =>
        max <= min ? min : System.Math.Clamp(value, min, max);
}
