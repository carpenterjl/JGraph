using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering;

/// <summary>
/// Draws an axes' bubble legend: a few circles at the sizes the chart itself would draw, with the
/// values they stand for written beside them. The sizes come from the axes'
/// <see cref="AxesModel.BubbleScale"/> and the colour from the first bubble chart in the axes, so the
/// legend cannot say one thing while the chart says another — which is the only property that makes
/// a bubble legend worth drawing at all.
/// </summary>
internal static class BubbleLegendRenderer
{
    private const double Padding = 10;
    private const double LabelGap = 8;
    private const double RowGap = 6;

    /// <summary>Draws the legend and returns its box, or null when there is nothing to legend.</summary>
    public static Rect2D? Draw(
        IRenderContext context, AxesModel axes, Rect2D plotArea, Rect2D figureArea, ITheme theme)
    {
        BubbleLegendModel legend = axes.BubbleLegend;
        BubbleScale scale = axes.BubbleScale;
        IReadOnlyList<double> values = legend.ValuesFor(scale);

        var bubbles = new List<(double Value, double Diameter, string? Label)>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            bool labelled = !legend.LimitLabels || i == 0 || i == values.Count - 1;
            bubbles.Add((values[i], scale.DiameterFor(values[i]), labelled ? Format(values[i]) : null));
        }

        Color fill = BubbleColor(axes, theme);
        Color edge = fill.WithOpacity(1.0);

        double widest = bubbles.Max(b => b.Diameter);
        double labelWidth = 0;
        double labelHeight = 0;
        foreach ((_, _, string? label) in bubbles)
        {
            if (label is null)
            {
                continue;
            }

            Size2D size = context.MeasureText(label, legend.TextStyle);
            labelWidth = System.Math.Max(labelWidth, size.Width);
            labelHeight = System.Math.Max(labelHeight, size.Height);
        }

        Size2D title = string.IsNullOrEmpty(legend.Title)
            ? new Size2D(0, 0)
            : context.MeasureText(legend.Title!, legend.TextStyle);

        (double width, double height) = legend.Style switch
        {
            BubbleLegendStyle.Horizontal => (
                (bubbles.Count * widest) + ((bubbles.Count - 1) * RowGap) + (2 * Padding),
                widest + LabelGap + labelHeight + (2 * Padding)),
            BubbleLegendStyle.Telescopic => (
                widest + LabelGap + labelWidth + (2 * Padding),
                widest + (2 * Padding)),
            _ => (
                widest + LabelGap + labelWidth + (2 * Padding),
                bubbles.Sum(b => System.Math.Max(b.Diameter, labelHeight))
                    + ((bubbles.Count - 1) * RowGap) + (2 * Padding)),
        };

        if (title.Width > 0)
        {
            width = System.Math.Max(width, title.Width + (2 * Padding));
            height += title.Height + RowGap;
        }

        // Placement is the legend's own, worked out by the same rules — the two boxes are furniture of
        // the same kind and a script that says 'northwest' means the same corner for either.
        Rect2D box = LegendRenderer.PlaceBox(Placement(legend), plotArea, plotArea, width, height);
        LineStyle? border = legend.ShowBorder ? new LineStyle(legend.BorderColor, 1) : null;
        context.DrawRectangle(box, border, legend.Background);

        double top = box.Top + Padding;
        if (title.Width > 0)
        {
            context.DrawText(
                legend.Title!,
                new Point2D(box.Left + Padding, top),
                legend.TextStyle,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);
            top += title.Height + RowGap;
        }

        double left = box.Left + Padding;
        switch (legend.Style)
        {
            case BubbleLegendStyle.Horizontal:
            {
                double x = left;
                foreach ((_, double diameter, string? label) in bubbles)
                {
                    // Bottom-aligned, so a row of bubbles reads as growing rather than as floating.
                    double bottom = top + widest;
                    Circle(x + (widest / 2), bottom - (diameter / 2), diameter);
                    Write(label, new Point2D(x + (widest / 2), bottom + LabelGap),
                        HorizontalAlignment.Center, VerticalAlignment.Top);
                    x += widest + RowGap;
                }

                break;
            }

            case BubbleLegendStyle.Telescopic:
            {
                double bottom = top + widest;

                // Largest first, so a smaller circle is drawn over the one containing it.
                foreach ((_, double diameter, string? label) in bubbles.OrderByDescending(static b => b.Diameter))
                {
                    Circle(left + (widest / 2), bottom - (diameter / 2), diameter);
                    Write(label, new Point2D(left + widest + LabelGap, bottom - diameter),
                        HorizontalAlignment.Left, VerticalAlignment.Middle);
                }

                break;
            }

            default:
            {
                double y = top;
                foreach ((_, double diameter, string? label) in bubbles)
                {
                    double band = System.Math.Max(diameter, labelHeight);
                    Circle(left + (widest / 2), y + (band / 2), diameter);
                    Write(label, new Point2D(left + widest + LabelGap, y + (band / 2)),
                        HorizontalAlignment.Left, VerticalAlignment.Middle);
                    y += band + RowGap;
                }

                break;
            }
        }

        return box;

        void Circle(double centerX, double centerY, double diameter)
        {
            Span<Point2D> one = stackalloc Point2D[1];
            one[0] = new Point2D(centerX, centerY);
            context.DrawMarkers(one, new MarkerStyle(MarkerType.Circle, diameter, fill, edge, 1), edge);
        }

        void Write(string? label, Point2D at, HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            if (label is not null)
            {
                context.DrawText(label, at, legend.TextStyle, horizontal, vertical);
            }
        }
    }

    /// <summary>
    /// A legend model carrying this bubble legend's placement, so the shared placement rules can be
    /// used without a second copy of them. Only the two fields <c>PlaceBox</c> reads are set.
    /// </summary>
    private static LegendModel Placement(BubbleLegendModel legend) =>
        new() { Position = legend.Position, Location = legend.Location, FigureBox = legend.FigureBox };

    /// <summary>
    /// The colour to draw the legend's bubbles in: the first bubble chart's own fill if it has one,
    /// and otherwise the palette colour that chart is drawn in — resolved by draw order, exactly as
    /// the legend resolves a swatch.
    /// </summary>
    private static Color BubbleColor(AxesModel axes, ITheme theme)
    {
        IReadOnlyList<Color> palette = SeriesPalette.Of(axes, theme);
        int index = 0;
        foreach (PlotObject plot in axes.Plots.InDrawOrder())
        {
            if (plot is IBubbleData { BubbleSizing: true } bubbles)
            {
                Color color = bubbles.BubbleFaceColor ?? SeriesPalette.Resolve(palette, plot, index);
                return color.WithOpacity(0.6);
            }

            index++;
        }

        return Colors.Gray.WithOpacity(0.6);
    }

    /// <summary>A value as a legend writes it: enough digits to tell the bubbles apart, and no more.</summary>
    private static string Format(double value)
    {
        double magnitude = System.Math.Abs(value);
        if (magnitude != 0 && (magnitude >= 1e5 || magnitude < 1e-3))
        {
            return value.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
        }

        return System.Math.Round(value, 3).ToString(
            magnitude >= 100 ? "0" : "0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
