using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using JGraph.Maths.Transforms;
using JGraph.Rendering.Layout;

namespace JGraph.Rendering;

/// <summary>
/// Draws a <see cref="FigureModel"/> onto any <see cref="IRenderContext"/>. The renderer is fully
/// backend-independent: the same code produces the on-screen figure, a raster export, or a vector
/// export depending only on which context is supplied. It owns figure "chrome" (backgrounds, grid,
/// axis frame, ticks, labels, titles, legend) and delegates plot content to each plot's
/// <see cref="IDrawable"/> implementation.
/// </summary>
public sealed class FigureRenderer
{
    private const double TickLength = 5;
    private const double LabelPadding = 4;
    private const double EdgePadding = 6;

    /// <summary>Renders a figure, laying it out for the context's current size, and returns its geometry.</summary>
    public FigureRenderResult Render(FigureModel figure, IRenderContext context, ITheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(context);
        theme ??= Theme.Light;

        // Ensure auto-scaled axes reflect the current data before we lay anything out.
        figure.RecomputeDataBounds();

        context.Clear(figure.Background);

        Size2D size = context.Size;
        Rect2D content = new(0, 0, size.Width, size.Height);

        if (!string.IsNullOrEmpty(figure.Title))
        {
            Size2D titleSize = context.MeasureText(figure.Title, figure.TitleStyle);
            double stripHeight = titleSize.Height + (EdgePadding * 2);
            context.DrawText(
                figure.Title,
                new Point2D(size.Width / 2, EdgePadding),
                figure.TitleStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Top);
            content = new Rect2D(0, stripHeight, size.Width, size.Height - stripHeight);
        }

        var infos = new List<AxesRenderInfo>();
        foreach (AxesModel axes in figure.Axes.InDrawOrder())
        {
            if (axes.Visible)
            {
                AxesRenderInfo? info = RenderAxes(axes, context, theme, content);
                if (info is not null)
                {
                    infos.Add(info);
                }
            }
        }

        // Figure-space annotations are drawn over everything, in normalized coordinates of the whole
        // surface, so they stay put regardless of axis navigation.
        var figureRect = new Rect2D(0, 0, size.Width, size.Height);
        var figureMapper = new NormalizedCoordinateMapper(figureRect);
        DrawAnnotations(figure.Annotations, context, figureMapper, figureRect, theme);

        return new FigureRenderResult(infos, figureMapper);
    }

    private AxesRenderInfo? RenderAxes(AxesModel axes, IRenderContext context, ITheme theme, Rect2D content)
    {
        Rect2D outer = new(
            content.X + (axes.NormalizedBounds.X * content.Width),
            content.Y + (axes.NormalizedBounds.Y * content.Height),
            axes.NormalizedBounds.Width * content.Width,
            axes.NormalizedBounds.Height * content.Height);

        AxisModel xAxis = axes.PrimaryXAxis;
        AxisModel yAxis = axes.PrimaryYAxis;

        // Ticks depend only on range and target count, so they can be generated before the plot area
        // is known and then used to measure the margins that determine the plot area.
        TickSet xTicks = TickGenerators.For(xAxis).Generate(xAxis.Range, xAxis.TargetMajorTickCount, xAxis.TickLabelFormat);
        TickSet yTicks = TickGenerators.For(yAxis).Generate(yAxis.Range, yAxis.TargetMajorTickCount, yAxis.TickLabelFormat);

        DecorationMetrics metrics = MeasureDecorations(axes, context, xAxis, yAxis, xTicks, yTicks);
        AxesLayout layout = LayoutEngine.Compute(axes, outer, metrics.Margins);
        Rect2D plotArea = layout.PlotArea;
        if (plotArea.IsEmpty)
        {
            return null;
        }

        // Equal aspect (polar/Smith/Nyquist): shrink to a centered square-per-unit rectangle so that
        // circles render round. Everything below then uses the adjusted plot area consistently.
        if (axes.EqualAspect)
        {
            plotArea = SquareForEqualAspect(plotArea, xAxis, yAxis);
        }

        var transform = AxisTransform.Create(plotArea, xAxis, yAxis);

        // Axes background.
        context.DrawRectangle(plotArea, stroke: null, fill: axes.Background);

        // Grid (below data).
        DrawGrid(context, axes.Grid, plotArea, transform, xTicks, yTicks);

        // Plot content, clipped to the plot area.
        DrawPlots(axes, context, plotArea, theme);

        // Data-space annotations sit above the plots, clipped like them.
        context.PushClip(plotArea);
        DrawAnnotations(axes.Annotations, context, transform, plotArea, theme);
        context.PopClip();

        // Axis frame (suppressed by polar/Smith charts, which draw their own circular grid).
        if (axes.FrameVisible)
        {
            var frameStyle = new LineStyle(theme.AxisLine, 1);
            context.DrawRectangle(plotArea, frameStyle, fill: null);
        }

        // Ticks and tick labels.
        DrawXTicks(context, xAxis, plotArea, transform, xTicks, theme);
        DrawYTicks(context, yAxis, plotArea, transform, yTicks, theme);

        // Axis labels and title.
        DrawAxisTitles(context, axes, xAxis, yAxis, plotArea, metrics);

        // Legend.
        if (axes.Legend.Visible)
        {
            LegendRenderer.Draw(context, axes, plotArea, theme);
        }

        return new AxesRenderInfo(axes, plotArea, transform);
    }

    /// <summary>
    /// Shrinks a plot rectangle to a centered sub-rectangle whose pixels-per-unit are equal on both
    /// axes, so that a data circle maps to a pixel circle. Spans are taken in scale-forward space so
    /// the result is correct for linear axes (the intended use); a degenerate span leaves it unchanged.
    /// </summary>
    private static Rect2D SquareForEqualAspect(Rect2D plotArea, AxisModel xAxis, AxisModel yAxis)
    {
        IScaleTransform xScale = ScaleTransforms.For(xAxis.Scale);
        IScaleTransform yScale = ScaleTransforms.For(yAxis.Scale);
        double xLen = System.Math.Abs(xScale.Forward(xAxis.Range.Max) - xScale.Forward(xAxis.Range.Min));
        double yLen = System.Math.Abs(yScale.Forward(yAxis.Range.Max) - yScale.Forward(yAxis.Range.Min));
        if (!(xLen > 0) || !(yLen > 0) || plotArea.Width <= 0 || plotArea.Height <= 0)
        {
            return plotArea;
        }

        double scale = System.Math.Min(plotArea.Width / xLen, plotArea.Height / yLen);
        double w = scale * xLen;
        double h = scale * yLen;
        double x = plotArea.X + ((plotArea.Width - w) / 2.0);
        double y = plotArea.Y + ((plotArea.Height - h) / 2.0);
        return new Rect2D(x, y, w, h);
    }

    /// <summary>Text measurements that drive both the plot-area margins and the label placement.</summary>
    private readonly record struct DecorationMetrics(
        Thickness Margins,
        double YTickWidth,
        double XTickHeight,
        double YLabelHeight,
        double XLabelHeight);

    private static DecorationMetrics MeasureDecorations(
        AxesModel axes,
        IRenderContext context,
        AxisModel xAxis,
        AxisModel yAxis,
        TickSet xTicks,
        TickSet yTicks)
    {
        double left = EdgePadding;
        double bottom = EdgePadding;
        double top = EdgePadding;
        double right = EdgePadding + 4;

        double yTickWidth = 0;
        if (yAxis.ShowTickLabels)
        {
            foreach (Tick tick in yTicks.MajorTicks)
            {
                yTickWidth = System.Math.Max(yTickWidth, context.MeasureText(tick.Label, yAxis.TickLabelStyle).Width);
            }

            left += yTickWidth + TickLength + LabelPadding;
        }

        double yLabelHeight = 0;
        if (!string.IsNullOrEmpty(yAxis.Label))
        {
            yLabelHeight = context.MeasureText(yAxis.Label, yAxis.LabelStyle).Height;
            left += yLabelHeight + LabelPadding;
        }

        double xTickHeight = 0;
        if (xAxis.ShowTickLabels)
        {
            xTickHeight = context.MeasureText("0", xAxis.TickLabelStyle).Height;
            bottom += xTickHeight + TickLength + LabelPadding;
        }

        double xLabelHeight = 0;
        if (!string.IsNullOrEmpty(xAxis.Label))
        {
            xLabelHeight = context.MeasureText(xAxis.Label, xAxis.LabelStyle).Height;
            bottom += xLabelHeight + LabelPadding;
        }

        if (!string.IsNullOrEmpty(axes.Title))
        {
            top += context.MeasureText(axes.Title, axes.TitleStyle).Height + LabelPadding;
        }

        return new DecorationMetrics(
            new Thickness(left, top, right, bottom),
            yTickWidth,
            xTickHeight,
            yLabelHeight,
            xLabelHeight);
    }

    private static void DrawGrid(
        IRenderContext context,
        GridModel grid,
        Rect2D plotArea,
        AxisTransform transform,
        TickSet xTicks,
        TickSet yTicks)
    {
        if (!grid.Visible)
        {
            return;
        }

        context.PushClip(plotArea);

        if (grid.ShowMinor)
        {
            foreach (double v in xTicks.MinorTicks)
            {
                double px = transform.DataToPixelX(v);
                context.DrawLine(new Point2D(px, plotArea.Top), new Point2D(px, plotArea.Bottom), grid.MinorLineStyle);
            }

            foreach (double v in yTicks.MinorTicks)
            {
                double py = transform.DataToPixelY(v);
                context.DrawLine(new Point2D(plotArea.Left, py), new Point2D(plotArea.Right, py), grid.MinorLineStyle);
            }
        }

        if (grid.ShowMajor)
        {
            foreach (Tick tick in xTicks.MajorTicks)
            {
                double px = transform.DataToPixelX(tick.Value);
                context.DrawLine(new Point2D(px, plotArea.Top), new Point2D(px, plotArea.Bottom), grid.MajorLineStyle);
            }

            foreach (Tick tick in yTicks.MajorTicks)
            {
                double py = transform.DataToPixelY(tick.Value);
                context.DrawLine(new Point2D(plotArea.Left, py), new Point2D(plotArea.Right, py), grid.MajorLineStyle);
            }
        }

        context.PopClip();
    }

    private static void DrawPlots(AxesModel axes, IRenderContext context, Rect2D plotArea, ITheme theme)
    {
        IReadOnlyList<Color> palette = theme.SeriesPalette;
        int colorIndex = 0;

        context.PushClip(plotArea);
        foreach (PlotObject plot in axes.Plots.InDrawOrder())
        {
            Color seriesColor = palette.Count > 0 ? palette[colorIndex % palette.Count] : Colors.Black;
            colorIndex++;

            if (!plot.Visible || plot is not IDrawable drawable)
            {
                continue;
            }

            AxisModel xAxis = axes.GetXAxisFor(plot);
            AxisModel yAxis = axes.GetYAxisFor(plot);
            var transform = AxisTransform.Create(plotArea, xAxis, yAxis);
            var state = new RenderState(transform, plotArea, seriesColor);
            drawable.Render(context, state);
        }

        context.PopClip();
    }

    private static void DrawAnnotations(
        GraphObjectCollection<AnnotationObject> annotations,
        IRenderContext context,
        ICoordinateMapper mapper,
        Rect2D area,
        ITheme theme)
    {
        if (annotations.Count == 0)
        {
            return;
        }

        // Annotations without an explicit color use the theme's label ink so they read in any theme.
        var state = new RenderState(mapper, area, theme.AxisLabel);
        foreach (AnnotationObject annotation in annotations.InDrawOrder())
        {
            if (annotation.Visible && annotation is IDrawable drawable)
            {
                drawable.Render(context, state);
            }
        }
    }

    private static void DrawXTicks(
        IRenderContext context,
        AxisModel xAxis,
        Rect2D plotArea,
        AxisTransform transform,
        TickSet xTicks,
        ITheme theme)
    {
        if (!xAxis.ShowMajorTicks && !xAxis.ShowTickLabels)
        {
            return;
        }

        var tickStyle = new LineStyle(theme.AxisLine, 1);
        foreach (Tick tick in xTicks.MajorTicks)
        {
            double px = transform.DataToPixelX(tick.Value);
            if (xAxis.ShowMajorTicks)
            {
                context.DrawLine(new Point2D(px, plotArea.Bottom), new Point2D(px, plotArea.Bottom + TickLength), tickStyle);
            }

            if (xAxis.ShowTickLabels)
            {
                context.DrawText(
                    tick.Label,
                    new Point2D(px, plotArea.Bottom + TickLength + LabelPadding),
                    xAxis.TickLabelStyle,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Top);
            }
        }
    }

    private static void DrawYTicks(
        IRenderContext context,
        AxisModel yAxis,
        Rect2D plotArea,
        AxisTransform transform,
        TickSet yTicks,
        ITheme theme)
    {
        if (!yAxis.ShowMajorTicks && !yAxis.ShowTickLabels)
        {
            return;
        }

        var tickStyle = new LineStyle(theme.AxisLine, 1);
        foreach (Tick tick in yTicks.MajorTicks)
        {
            double py = transform.DataToPixelY(tick.Value);
            if (yAxis.ShowMajorTicks)
            {
                context.DrawLine(new Point2D(plotArea.Left - TickLength, py), new Point2D(plotArea.Left, py), tickStyle);
            }

            if (yAxis.ShowTickLabels)
            {
                context.DrawText(
                    tick.Label,
                    new Point2D(plotArea.Left - TickLength - LabelPadding, py),
                    yAxis.TickLabelStyle,
                    HorizontalAlignment.Right,
                    VerticalAlignment.Middle);
            }
        }
    }

    private static void DrawAxisTitles(
        IRenderContext context,
        AxesModel axes,
        AxisModel xAxis,
        AxisModel yAxis,
        Rect2D plotArea,
        DecorationMetrics metrics)
    {
        if (!string.IsNullOrEmpty(xAxis.Label))
        {
            double y = plotArea.Bottom + TickLength + LabelPadding + metrics.XTickHeight + LabelPadding;
            context.DrawText(
                xAxis.Label,
                new Point2D(plotArea.CenterX, y),
                xAxis.LabelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Top);
        }

        if (!string.IsNullOrEmpty(yAxis.Label))
        {
            // For the -90 degree rotated label, VerticalAlignment.Bottom places the glyph cell to the
            // left of this x, clear of the tick labels to its right.
            double x = plotArea.Left - (TickLength + LabelPadding + metrics.YTickWidth + LabelPadding);
            context.DrawText(
                yAxis.Label,
                new Point2D(x, plotArea.CenterY),
                yAxis.LabelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                rotationDegrees: -90);
        }

        if (!string.IsNullOrEmpty(axes.Title))
        {
            context.DrawText(
                axes.Title,
                new Point2D(plotArea.CenterX, plotArea.Top - LabelPadding),
                axes.TitleStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom);
        }
    }
}
