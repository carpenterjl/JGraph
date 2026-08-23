using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;

namespace JGraph.Rendering;

/// <summary>
/// Draws an axes' colorbar: a colormap gradient standing on one of the four sides of the plot area,
/// with a value scale beside it, legending the first visible <see cref="IColorMapped"/> plot. A strip
/// on an <em>outside</em> location reserves its band through <see cref="MeasureReserved"/> so the plot
/// area shrinks to make room; one on an inside location lies over the plot area and reserves nothing,
/// which is what MATLAB's four inside words mean.
/// </summary>
public static class ColorbarRenderer
{
    private const double Gap = 10;
    private const double LabelPadding = 4;

    /// <summary>
    /// The band the colorbar needs on each side, or nothing when it is hidden, has no source plot, or
    /// lies over the plot area rather than beside it.
    /// </summary>
    public static Thickness MeasureReserved(AxesModel axes, IRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ColorbarModel bar = axes.Colorbar;
        if (!bar.Visible || FindSource(axes) is null || !bar.IsOutside)
        {
            return new Thickness(0);
        }

        double band = Gap + bar.Width + TickPixels(bar, 100) + LabelPadding + LabelBand(axes, context);
        if (!string.IsNullOrEmpty(bar.Label))
        {
            band += context.MeasureText(bar.Label, bar.TickLabelStyle).Height + LabelPadding;
        }

        return bar.Location switch
        {
            ColorbarLocation.WestOutside => new Thickness(band, 0, 0, 0),
            ColorbarLocation.NorthOutside => new Thickness(0, band, 0, 0),
            ColorbarLocation.SouthOutside => new Thickness(0, 0, 0, band),
            _ => new Thickness(0, 0, band, 0),
        };
    }

    /// <summary>The extra right margin a colorbar on the default side needs; 0 for every other side.</summary>
    public static double MeasureReservedWidth(AxesModel axes, IRenderContext context) =>
        MeasureReserved(axes, context).Right;

    /// <summary>Draws the colorbar for <paramref name="plotArea"/> (no-op without a color-mapped plot).</summary>
    public static void Draw(IRenderContext context, AxesModel axes, Rect2D plotArea, ITheme theme) =>
        Draw(context, axes, plotArea, plotArea, theme);

    /// <summary>Draws the colorbar, placing a pinned one against <paramref name="figureArea"/>.</summary>
    public static void Draw(
        IRenderContext context, AxesModel axes, Rect2D plotArea, Rect2D figureArea, ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(theme);

        IColorMapped? source = FindSource(axes);
        if (source is null)
        {
            return;
        }

        ColorbarModel bar = axes.Colorbar;
        (double min, double max) = Span(bar, source);

        Rect2D strip = Place(bar, plotArea, figureArea, context, axes);
        bar.LastBox = strip;
        if (strip.Width <= 0 || strip.Height <= 0)
        {
            return;
        }

        bool horizontal = bar.IsHorizontal;
        bool logScale = UsesLogScale(axes, min, max);

        // The gradient: 256 samples along the long side. On a vertical strip row 0 is the top, which
        // is the high end unless the direction is reversed; on a horizontal one column 0 is the left,
        // which is the low end. One flag decides both, so a reversed bar reverses either way up.
        const int Samples = 256;
        var pixels = new uint[Samples];
        for (int i = 0; i < Samples; i++)
        {
            double t = i / (double)(Samples - 1);
            double along = horizontal ? t : 1 - t;
            pixels[i] = source.Colormap.Sample(bar.Inverted ? 1 - along : along).ToArgb();
        }

        if (horizontal)
        {
            context.DrawImage(pixels, Samples, 1, strip, interpolate: true);
        }
        else
        {
            context.DrawImage(pixels, 1, Samples, strip, interpolate: true);
        }

        Color ink = bar.Ink ?? theme.AxisLine;
        var lineStyle = new LineStyle(ink, System.Math.Max(0.1, bar.LineWidth));
        if (bar.BoxVisible && bar.LineWidth > 0)
        {
            context.DrawRectangle(strip, lineStyle, fill: null);
        }

        DrawScale(context, bar, strip, min, max, logScale, lineStyle, horizontal);
        DrawLabel(context, bar, strip, horizontal);
    }

    /// <summary>The strip's rectangle, from the location or from an explicitly pinned box.</summary>
    private static Rect2D Place(
        ColorbarModel bar, Rect2D plotArea, Rect2D figureArea, IRenderContext context, AxesModel axes)
    {
        if (bar.FigureBox is { } pinned)
        {
            return new Rect2D(
                figureArea.Left + (pinned.X * figureArea.Width),
                figureArea.Top + (pinned.Y * figureArea.Height),
                pinned.Width * figureArea.Width,
                pinned.Height * figureArea.Height);
        }

        double thickness = bar.Width;
        double inset = Gap;
        double labels = LabelBand(axes, context) + LabelPadding + TickPixels(bar, LongSide(bar, plotArea));

        return bar.Location switch
        {
            // An outside strip stands in the band MeasureReserved already took out of the plot box,
            // so it is placed a gap beyond the edge; the labels go in the rest of that band.
            ColorbarLocation.WestOutside => new Rect2D(
                plotArea.Left - inset - thickness - labels, plotArea.Top, thickness, plotArea.Height),
            ColorbarLocation.NorthOutside => new Rect2D(
                plotArea.Left, plotArea.Top - inset - thickness - labels, plotArea.Width, thickness),
            ColorbarLocation.SouthOutside => new Rect2D(
                plotArea.Left, plotArea.Bottom + inset + labels, plotArea.Width, thickness),
            ColorbarLocation.EastOutside => new Rect2D(
                plotArea.Right + inset, plotArea.Top, thickness, plotArea.Height),

            // An inside strip lies over the plot area, inset from the edge it stands against and cut
            // to three quarters of the run so it does not reach corner to corner.
            ColorbarLocation.West => Inside(plotArea, thickness, horizontal: false, atFar: false),
            ColorbarLocation.East => Inside(plotArea, thickness, horizontal: false, atFar: true),
            ColorbarLocation.North => Inside(plotArea, thickness, horizontal: true, atFar: false),
            ColorbarLocation.South => Inside(plotArea, thickness, horizontal: true, atFar: true),

            // Manual with nothing pinned yet: the default side, so the bar is somewhere rather than
            // nowhere until a script gives it a box.
            _ => new Rect2D(plotArea.Right + inset, plotArea.Top, thickness, plotArea.Height),
        };
    }

    private static Rect2D Inside(Rect2D plotArea, double thickness, bool horizontal, bool atFar)
    {
        const double Margin = 16;
        if (horizontal)
        {
            double width = plotArea.Width * 0.75;
            double top = atFar ? plotArea.Bottom - Margin - thickness : plotArea.Top + Margin;
            return new Rect2D(plotArea.CenterX - (width / 2), top, width, thickness);
        }

        double height = plotArea.Height * 0.75;
        double left = atFar ? plotArea.Right - Margin - thickness : plotArea.Left + Margin;
        return new Rect2D(left, plotArea.CenterY - (height / 2), thickness, height);
    }

    private static void DrawScale(
        IRenderContext context,
        ColorbarModel bar,
        Rect2D strip,
        double min,
        double max,
        bool logScale,
        LineStyle lineStyle,
        bool horizontal)
    {
        double tickPixels = TickPixels(bar, LongSideOf(strip, horizontal));
        bool labelsFar = !bar.LabelsInside;

        IReadOnlyList<(double Value, string Label)> ticks = Ticks(bar, min, max, logScale);
        for (int i = 0; i < ticks.Count; i++)
        {
            (double value, string label) = ticks[i];
            if (value < System.Math.Min(min, max) || value > System.Math.Max(min, max))
            {
                continue;
            }

            double fraction = Fraction(value, min, max, logScale);
            if (bar.Inverted)
            {
                fraction = 1 - fraction;
            }

            if (horizontal)
            {
                double x = strip.Left + (fraction * strip.Width);
                double from = labelsFar ? strip.Bottom : strip.Top;
                double outward = labelsFar ? 1 : -1;
                DrawTick(context, bar, lineStyle, new Point2D(x, from), new Vector2D(0, outward * tickPixels));
                context.DrawText(
                    label,
                    new Point2D(x, from + (outward * (tickPixels + LabelPadding))),
                    bar.TickLabelStyle,
                    HorizontalAlignment.Center,
                    labelsFar ? VerticalAlignment.Top : VerticalAlignment.Bottom);
            }
            else
            {
                double y = strip.Bottom - (fraction * strip.Height);
                double from = labelsFar ? strip.Right : strip.Left;
                double outward = labelsFar ? 1 : -1;
                DrawTick(context, bar, lineStyle, new Point2D(from, y), new Vector2D(outward * tickPixels, 0));
                context.DrawText(
                    label,
                    new Point2D(from + (outward * (tickPixels + LabelPadding)), y),
                    bar.TickLabelStyle,
                    labelsFar ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment.Middle);
            }
        }
    }

    /// <summary>Draws one tick mark, inward, outward or both, from a point on the strip's edge.</summary>
    private static void DrawTick(
        IRenderContext context, ColorbarModel bar, LineStyle style, Point2D at, Vector2D outward)
    {
        if (bar.TickDirection is TickDirection.Out or TickDirection.Both)
        {
            context.DrawLine(at, new Point2D(at.X + outward.X, at.Y + outward.Y), style);
        }

        if (bar.TickDirection is TickDirection.In or TickDirection.Both)
        {
            context.DrawLine(at, new Point2D(at.X - outward.X, at.Y - outward.Y), style);
        }
    }

    private static void DrawLabel(IRenderContext context, ColorbarModel bar, Rect2D strip, bool horizontal)
    {
        if (string.IsNullOrEmpty(bar.Label))
        {
            return;
        }

        if (horizontal)
        {
            context.DrawText(
                bar.Label!,
                new Point2D(strip.CenterX, bar.LabelsInside ? strip.Top - LabelPadding : strip.Bottom + LabelPadding),
                bar.TickLabelStyle,
                HorizontalAlignment.Center,
                bar.LabelsInside ? VerticalAlignment.Bottom : VerticalAlignment.Top);
            return;
        }

        context.DrawText(
            bar.Label!,
            new Point2D(bar.LabelsInside ? strip.Left - LabelPadding : strip.Right + LabelPadding, strip.CenterY),
            bar.TickLabelStyle,
            HorizontalAlignment.Center,
            VerticalAlignment.Top,
            rotationDegrees: 90);
    }

    /// <summary>The values and labels the scale shows: the chosen ones, or the generated ones.</summary>
    private static IReadOnlyList<(double Value, string Label)> Ticks(
        ColorbarModel bar, double min, double max, bool logScale)
    {
        var rows = new List<(double, string)>();
        if (bar.TickValues is { } chosen)
        {
            for (int i = 0; i < chosen.Length; i++)
            {
                rows.Add((chosen[i], LabelFor(bar, i, chosen[i])));
            }

            return rows;
        }

        TickSet generated = logScale ? GenerateLogTicks(min, max) : GenerateTicks(min, max);
        int index = 0;
        foreach (Tick tick in generated.MajorTicks)
        {
            rows.Add((tick.Value, LabelFor(bar, index, tick.Value, tick.Label)));
            index++;
        }

        return rows;
    }

    /// <summary>A chosen label if there is one — cycled, as a ruler's overrides are — else the generated one.</summary>
    private static string LabelFor(ColorbarModel bar, int index, double value, string? generated = null)
    {
        if (bar.TickLabelOverrides is { Length: > 0 } labels)
        {
            return labels[index % labels.Length];
        }

        return generated ?? value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The values the strip spans: its own limits when it has them, else the plot's range.</summary>
    private static (double Min, double Max) Span(ColorbarModel bar, IColorMapped source)
    {
        (double min, double max) = bar.Limits is { } chosen
            ? (chosen.Min, chosen.Max)
            : source.ColorRange;

        return max > min ? (min, max) : (min, min + 1);
    }

    private static double LongSide(ColorbarModel bar, Rect2D plotArea) =>
        bar.IsHorizontal ? plotArea.Width : plotArea.Height;

    private static double LongSideOf(Rect2D strip, bool horizontal) =>
        horizontal ? strip.Width : strip.Height;

    /// <summary>A tick's length in pixels, from the fraction of the long side the model stores.</summary>
    private static double TickPixels(ColorbarModel bar, double longSide) =>
        System.Math.Max(0, bar.TickLength * System.Math.Max(1, longSide));

    /// <summary>How wide the tick labels are, which is what the reserved band mostly consists of.</summary>
    private static double LabelBand(AxesModel axes, IRenderContext context)
    {
        ColorbarModel bar = axes.Colorbar;
        IColorMapped? source = FindSource(axes);
        if (source is null)
        {
            return 0;
        }

        (double min, double max) = Span(bar, source);
        bool logScale = UsesLogScale(axes, min, max);
        double band = 0;
        foreach ((double _, string label) in Ticks(bar, min, max, logScale))
        {
            Size2D size = context.MeasureText(label, bar.TickLabelStyle);
            band = System.Math.Max(band, bar.IsHorizontal ? size.Height : size.Width);
        }

        return band;
    }

    private static TickSet GenerateTicks(double min, double max) =>
        new LinearTickGenerator().Generate(new DataRange(min, max > min ? max : min + 1), 6);

    /// <summary>Decade ticks for a log-scaled colorbar, from the same generator the log rulers use.</summary>
    private static TickSet GenerateLogTicks(double min, double max) =>
        new LogarithmicTickGenerator().Generate(new DataRange(min, max > min ? max : min * 10), 6);

    /// <summary>True when the axes spreads its colors logarithmically and the limits can be logged.</summary>
    private static bool UsesLogScale(AxesModel axes, double min, double max) =>
        axes.ColorScale == ColorScaleType.Log && min > 0 && max > 0;

    /// <summary>Where a value sits along the strip, under the scale in force.</summary>
    private static double Fraction(double value, double min, double max, bool logScale)
    {
        if (!logScale)
        {
            return (value - min) / (max - min);
        }

        double low = System.Math.Log10(min);
        double span = System.Math.Log10(max) - low;
        return span <= 0 ? 0.5 : (System.Math.Log10(value) - low) / span;
    }

    private static IColorMapped? FindSource(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot.Visible && plot is IColorMapped { HasMappedData: true } mapped)
            {
                return mapped;
            }
        }

        return null;
    }
}
