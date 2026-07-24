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

        // Same idea for the legends: reconcile their rows with the plots. This mutates the model from
        // a render path, which is deliberate — it is the one place that knows both the plot list and
        // which plots can be legended. SyncEntries is idempotent and only invalidates when the rows
        // really changed, so adding a plot costs one rebuild and a steady-state repaint costs nothing.
        foreach (AxesModel axes in figure.Axes)
        {
            LegendRenderer.SyncEntries(axes);
        }

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

        // 3D axes take a separate path: an axonometric box with projected plots instead of the 2D
        // grid/frame/tick machinery. The returned info still carries the 2D transform so hit-testing
        // and wheel routing (which only need the plot rectangle) keep working.
        if (axes.Is3D)
        {
            Render3DContent(axes, context, theme, plotArea);

            if (!string.IsNullOrEmpty(axes.Title))
            {
                context.DrawText(
                    axes.Title,
                    new Point2D(plotArea.CenterX, plotArea.Top - LabelPadding),
                    axes.TitleStyle,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Bottom);
            }

            Rect2D? legendBox = axes.Legend.Visible
                ? LegendRenderer.Draw(context, axes, plotArea, theme)
                : null;

            if (axes.Colorbar.Visible)
            {
                ColorbarRenderer.Draw(context, axes, plotArea, theme);
            }

            return new AxesRenderInfo(axes, plotArea, transform, legendBox);
        }

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
        Rect2D? legendBounds = axes.Legend.Visible
            ? LegendRenderer.Draw(context, axes, plotArea, theme)
            : null;

        // Colorbar (its width was reserved by MeasureDecorations).
        if (axes.Colorbar.Visible)
        {
            ColorbarRenderer.Draw(context, axes, plotArea, theme);
        }

        return new AxesRenderInfo(axes, plotArea, transform, legendBounds);
    }

    /// <summary>
    /// Renders a 3D axes' content: the far faces of the coordinate box with grid lines, every plot
    /// implementing <see cref="I3DDrawable"/> projected through the shared camera, and tick/axis
    /// labels along adaptively chosen front edges. 2D-only plots are skipped.
    /// </summary>
    private static void Render3DContent(AxesModel axes, IRenderContext context, ITheme theme, Rect2D plotArea)
    {
        AxisModel xAxis = axes.PrimaryXAxis;
        AxisModel yAxis = axes.PrimaryYAxis;
        AxisModel zAxis = axes.ZAxis;
        DataRange xr = xAxis.Range, yr = yAxis.Range, zr = zAxis.Range;

        var projection = new Projection3D(xr, yr, zr, axes.Azimuth, axes.Elevation, plotArea);

        TickSet xTicks = TickGenerators.For(xAxis).Generate(xr, xAxis.TargetMajorTickCount, xAxis.TickLabelFormat);
        TickSet yTicks = TickGenerators.For(yAxis).Generate(yr, yAxis.TargetMajorTickCount, yAxis.TickLabelFormat);
        TickSet zTicks = TickGenerators.For(zAxis).Generate(zr, zAxis.TargetMajorTickCount, zAxis.TickLabelFormat);

        // The far value of each axis is the one whose face center sits deeper (painter order: the
        // three far faces carry the grid; plots draw over them; labels go on the near edges).
        double CenterDepth(double x, double y, double z) => projection.Project(x, y, z).Depth;
        double xMid = (xr.Min + xr.Max) / 2, yMid = (yr.Min + yr.Max) / 2, zMid = (zr.Min + zr.Max) / 2;
        double xFar = CenterDepth(xr.Min, yMid, zMid) <= CenterDepth(xr.Max, yMid, zMid) ? xr.Min : xr.Max;
        double yFar = CenterDepth(xMid, yr.Min, zMid) <= CenterDepth(xMid, yr.Max, zMid) ? yr.Min : yr.Max;
        double zFar = CenterDepth(xMid, yMid, zr.Min) <= CenterDepth(xMid, yMid, zr.Max) ? zr.Min : zr.Max;
        double xNear = xFar == xr.Min ? xr.Max : xr.Min;
        double yNear = yFar == yr.Min ? yr.Max : yr.Min;

        context.PushClip(plotArea);

        var frameStyle = new LineStyle(theme.AxisLine, 1);
        var gridStyle = new LineStyle(theme.AxisLine.WithOpacity(0.25), 1);

        void Line3D(double x1, double y1, double z1, double x2, double y2, double z2, LineStyle style) =>
            context.DrawLine(projection.ProjectPoint(x1, y1, z1), projection.ProjectPoint(x2, y2, z2), style);

        // Floor (z = zFar): grid at x and y ticks.
        foreach (Tick t in xTicks.MajorTicks)
        {
            Line3D(t.Value, yr.Min, zFar, t.Value, yr.Max, zFar, gridStyle);
        }

        foreach (Tick t in yTicks.MajorTicks)
        {
            Line3D(xr.Min, t.Value, zFar, xr.Max, t.Value, zFar, gridStyle);
        }

        // Back face at x = xFar (spans y and z).
        foreach (Tick t in yTicks.MajorTicks)
        {
            Line3D(xFar, t.Value, zr.Min, xFar, t.Value, zr.Max, gridStyle);
        }

        foreach (Tick t in zTicks.MajorTicks)
        {
            Line3D(xFar, yr.Min, t.Value, xFar, yr.Max, t.Value, gridStyle);
        }

        // Back face at y = yFar (spans x and z).
        foreach (Tick t in xTicks.MajorTicks)
        {
            Line3D(t.Value, yFar, zr.Min, t.Value, yFar, zr.Max, gridStyle);
        }

        foreach (Tick t in zTicks.MajorTicks)
        {
            Line3D(xr.Min, yFar, t.Value, xr.Max, yFar, t.Value, gridStyle);
        }

        // Outlines of the three far faces.
        Line3D(xr.Min, yr.Min, zFar, xr.Max, yr.Min, zFar, frameStyle);
        Line3D(xr.Min, yr.Max, zFar, xr.Max, yr.Max, zFar, frameStyle);
        Line3D(xr.Min, yr.Min, zFar, xr.Min, yr.Max, zFar, frameStyle);
        Line3D(xr.Max, yr.Min, zFar, xr.Max, yr.Max, zFar, frameStyle);
        Line3D(xFar, yr.Min, zr.Min, xFar, yr.Min, zr.Max, frameStyle);
        Line3D(xFar, yr.Max, zr.Min, xFar, yr.Max, zr.Max, frameStyle);
        Line3D(xr.Min, yFar, zr.Min, xr.Min, yFar, zr.Max, frameStyle);
        Line3D(xr.Max, yFar, zr.Min, xr.Max, yFar, zr.Max, frameStyle);

        // Plot content.
        IReadOnlyList<Color> palette = theme.SeriesPalette;
        int colorIndex = 0;
        foreach (PlotObject plot in axes.Plots.InDrawOrder())
        {
            Color seriesColor = palette.Count > 0 ? palette[colorIndex % palette.Count] : Colors.Black;
            colorIndex++;
            if (plot.Visible && plot is I3DDrawable drawable)
            {
                var state = new RenderState(new NormalizedCoordinateMapper(plotArea), plotArea, seriesColor);
                drawable.Render3D(context, projection, state);
            }
        }

        context.PopClip();

        // Tick labels along the front-bottom edges (drawn unclipped so they may sit in the margins).
        Point2D floorCenter = projection.ProjectPoint(xMid, yMid, zFar);

        void EdgeLabel(string text, Point2D anchor, TextStyle style, double push)
        {
            double dx = anchor.X - floorCenter.X;
            double dy = anchor.Y - floorCenter.Y;
            double length = System.Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 1e-6)
            {
                dx = 0;
                dy = 1;
                length = 1;
            }

            var position = new Point2D(anchor.X + (dx / length * push), anchor.Y + (dy / length * push));
            context.DrawText(text, position, style, HorizontalAlignment.Center, VerticalAlignment.Middle);
        }

        if (xAxis.ShowTickLabels)
        {
            foreach (Tick t in xTicks.MajorTicks)
            {
                EdgeLabel(t.Label, projection.ProjectPoint(t.Value, yNear, zFar), xAxis.TickLabelStyle, 14);
            }
        }

        if (yAxis.ShowTickLabels)
        {
            foreach (Tick t in yTicks.MajorTicks)
            {
                EdgeLabel(t.Label, projection.ProjectPoint(xNear, t.Value, zFar), yAxis.TickLabelStyle, 14);
            }
        }

        // Z ticks on the leftmost vertical box edge.
        (double zx, double zy) = LeftmostVerticalEdge(projection, xr, yr, zMid);
        if (zAxis.ShowTickLabels)
        {
            foreach (Tick t in zTicks.MajorTicks)
            {
                Point2D p = projection.ProjectPoint(zx, zy, t.Value);
                context.DrawText(
                    t.Label,
                    new Point2D(p.X - 8, p.Y),
                    zAxis.TickLabelStyle,
                    HorizontalAlignment.Right,
                    VerticalAlignment.Middle);
            }
        }

        // Axis titles at the edge midpoints, pushed further out than the tick labels.
        if (!string.IsNullOrEmpty(xAxis.Label))
        {
            EdgeLabel(xAxis.Label, projection.ProjectPoint(xMid, yNear, zFar), xAxis.LabelStyle, 34);
        }

        if (!string.IsNullOrEmpty(yAxis.Label))
        {
            EdgeLabel(yAxis.Label, projection.ProjectPoint(xNear, yMid, zFar), yAxis.LabelStyle, 34);
        }

        if (!string.IsNullOrEmpty(zAxis.Label))
        {
            Point2D p = projection.ProjectPoint(zx, zy, zMid);
            context.DrawText(
                zAxis.Label,
                new Point2D(p.X - 34, p.Y),
                zAxis.LabelStyle,
                HorizontalAlignment.Center,
                VerticalAlignment.Bottom,
                rotationDegrees: -90);
        }
    }

    /// <summary>Picks the vertical box edge that projects leftmost on screen, for the Z scale.</summary>
    private static (double X, double Y) LeftmostVerticalEdge(Projection3D projection, DataRange xr, DataRange yr, double zMid)
    {
        (double X, double Y) best = (xr.Min, yr.Min);
        double bestPx = double.PositiveInfinity;
        foreach ((double x, double y) in new[] { (xr.Min, yr.Min), (xr.Min, yr.Max), (xr.Max, yr.Min), (xr.Max, yr.Max) })
        {
            double px = projection.ProjectPoint(x, y, zMid).X;
            if (px < bestPx)
            {
                bestPx = px;
                best = (x, y);
            }
        }

        return best;
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

        right += ColorbarRenderer.MeasureReservedWidth(axes, context);

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
