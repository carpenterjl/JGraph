using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;

namespace JGraph.Rendering;

/// <summary>
/// Draws an axes' colorbar: a vertical colormap gradient to the right of the plot area with a value
/// scale, legending the first visible <see cref="IColorMapped"/> plot. The layout pass reserves the
/// strip's width via <see cref="MeasureReservedWidth"/> so the plot area shrinks to make room.
/// </summary>
public static class ColorbarRenderer
{
    private const double Gap = 10;
    private const double TickLength = 4;
    private const double LabelPadding = 4;

    /// <summary>The extra right margin the colorbar needs, or 0 when hidden or without a source plot.</summary>
    public static double MeasureReservedWidth(AxesModel axes, IRenderContext context)
    {
        if (!axes.Colorbar.Visible || FindSource(axes) is null)
        {
            return 0;
        }

        (double min, double max) = FindSource(axes)!.ColorRange;
        double labelWidth = 0;
        foreach (Tick tick in GenerateTicks(min, max).MajorTicks)
        {
            labelWidth = System.Math.Max(labelWidth, context.MeasureText(tick.Label, axes.Colorbar.TickLabelStyle).Width);
        }

        double width = Gap + axes.Colorbar.Width + TickLength + LabelPadding + labelWidth;
        if (!string.IsNullOrEmpty(axes.Colorbar.Label))
        {
            width += context.MeasureText(axes.Colorbar.Label, axes.Colorbar.TickLabelStyle).Height + LabelPadding;
        }

        return width;
    }

    /// <summary>Draws the colorbar beside <paramref name="plotArea"/> (no-op without a color-mapped plot).</summary>
    public static void Draw(IRenderContext context, AxesModel axes, Rect2D plotArea, ITheme theme)
    {
        IColorMapped? source = FindSource(axes);
        if (source is null)
        {
            return;
        }

        (double min, double max) = source.ColorRange;
        if (!(max > min))
        {
            max = min + 1;
        }

        var strip = new Rect2D(plotArea.Right + Gap, plotArea.Top, axes.Colorbar.Width, plotArea.Height);

        // The gradient: 256 samples, high values at the top (row 0).
        const int Samples = 256;
        var pixels = new uint[Samples];
        for (int i = 0; i < Samples; i++)
        {
            double t = 1 - (i / (double)(Samples - 1));
            pixels[i] = source.Colormap.Sample(t).ToArgb();
        }

        context.DrawImage(pixels, 1, Samples, strip, interpolate: true);
        context.DrawRectangle(strip, new LineStyle(theme.AxisLine, 1), fill: null);

        // Value scale to the right of the strip.
        var tickStyle = new LineStyle(theme.AxisLine, 1);
        foreach (Tick tick in GenerateTicks(min, max).MajorTicks)
        {
            if (tick.Value < min || tick.Value > max)
            {
                continue;
            }

            double y = strip.Bottom - ((tick.Value - min) / (max - min) * strip.Height);
            context.DrawLine(new Point2D(strip.Right, y), new Point2D(strip.Right + TickLength, y), tickStyle);
            context.DrawText(
                tick.Label,
                new Point2D(strip.Right + TickLength + LabelPadding, y),
                axes.Colorbar.TickLabelStyle,
                HorizontalAlignment.Left,
                VerticalAlignment.Middle);
        }

        if (!string.IsNullOrEmpty(axes.Colorbar.Label))
        {
            double labelX = strip.Right + TickLength + LabelPadding + MeasureWidestTick(context, axes, min, max) + LabelPadding;
            context.DrawText(
                axes.Colorbar.Label!,
                new Point2D(labelX, strip.CenterY),
                axes.Colorbar.TickLabelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Top,
                rotationDegrees: 90);
        }
    }

    private static double MeasureWidestTick(IRenderContext context, AxesModel axes, double min, double max)
    {
        double width = 0;
        foreach (Tick tick in GenerateTicks(min, max).MajorTicks)
        {
            width = System.Math.Max(width, context.MeasureText(tick.Label, axes.Colorbar.TickLabelStyle).Width);
        }

        return width;
    }

    private static TickSet GenerateTicks(double min, double max) =>
        new LinearTickGenerator().Generate(new DataRange(min, max > min ? max : min + 1), 6);

    private static IColorMapped? FindSource(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot.Visible && plot is IColorMapped mapped)
            {
                return mapped;
            }
        }

        return null;
    }
}
