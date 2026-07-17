using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Objects;

public class AnnotationRenderTests
{
    private static RenderState StateOver(Rect2D area) =>
        new(new NormalizedCoordinateMapper(area), area, Colors.Black);

    [Fact]
    public void TextAnnotation_Render_RecordsBoundsAtAlignedPosition()
    {
        var context = new RecordingRenderContext(new Size2D(200, 100));
        var area = new Rect2D(0, 0, 200, 100);
        var text = new TextAnnotation(0.5, 0.5, "hi") { Padding = 4 };

        // Default alignment: box's left edge at the anchor, bottom edge on it.
        text.Render(context, StateOver(area));

        Assert.Equal(1, context.TextCount);
        Rect2D bounds = text.RenderedBounds;
        Assert.Equal(100, bounds.Left, precision: 6);
        Assert.Equal(50, bounds.Bottom, precision: 6);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
    }

    [Fact]
    public void TextAnnotation_CenterAlignment_CentersBoxOnAnchor()
    {
        var context = new RecordingRenderContext(new Size2D(200, 100));
        var area = new Rect2D(0, 0, 200, 100);
        var text = new TextAnnotation(0.5, 0.5, "hi")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
        };

        text.Render(context, StateOver(area));

        Assert.Equal(100, text.RenderedBounds.CenterX, precision: 6);
        Assert.Equal(50, text.RenderedBounds.CenterY, precision: 6);
    }

    [Fact]
    public void TextAnnotation_WithBox_DrawsBackgroundRectangle()
    {
        var context = new RecordingRenderContext(new Size2D(200, 100));
        var area = new Rect2D(0, 0, 200, 100);
        var text = new TextAnnotation(0.5, 0.5, "hi") { Background = Colors.White };

        text.Render(context, StateOver(area));
        Assert.Equal(1, context.RectangleCount);
    }

    [Fact]
    public void ArrowAnnotation_Render_DrawsShaftAndHead_AndHitTestsTheSegment()
    {
        var context = new RecordingRenderContext(new Size2D(100, 100));
        var area = new Rect2D(0, 0, 100, 100);
        var arrow = new ArrowAnnotation(0.1, 0.5, 0.9, 0.5);

        arrow.Render(context, StateOver(area));

        Assert.Equal(1, context.LineCount);
        Assert.Equal(1, context.PolygonCount);

        // On the segment (y=50) within tolerance; far from it fails.
        Assert.True(arrow.HitTest(new Point2D(50, 52), 3));
        Assert.False(arrow.HitTest(new Point2D(50, 70), 3));
    }

    [Fact]
    public void ArrowAnnotation_WithoutHead_DrawsPlainLine()
    {
        var context = new RecordingRenderContext(new Size2D(100, 100));
        var area = new Rect2D(0, 0, 100, 100);
        var arrow = new ArrowAnnotation(0, 0, 1, 1) { ShowHead = false };

        arrow.Render(context, StateOver(area));

        Assert.Equal(1, context.LineCount);
        Assert.Equal(0, context.PolygonCount);
    }

    [Fact]
    public void ShapeAnnotations_RenderAndRecordBounds()
    {
        var context = new RecordingRenderContext(new Size2D(100, 100));
        var area = new Rect2D(0, 0, 100, 100);

        var rectangle = new RectangleAnnotation(0.2, 0.2, 0.6, 0.4);
        rectangle.Render(context, StateOver(area));
        Assert.Equal(1, context.RectangleCount);
        Assert.True(rectangle.RenderedBounds.Contains(new Point2D(40, 30)));

        var ellipse = new EllipseAnnotation(0.2, 0.2, 0.6, 0.4);
        ellipse.Render(context, StateOver(area));
        Assert.Equal(1, context.PolygonCount);
    }

    [Fact]
    public void FigureRenderer_DrawsAxesAndFigureAnnotations_AndExposesFigureMapper()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1, 2 }, new double[] { 0, 1, 0 });
        axes.AddText(1, 0.5, "data note");
        figure.AddText(0.5, 0.05, "figure note");

        var context = new RecordingRenderContext(new Size2D(640, 480));
        FigureRenderResult result = new FigureRenderer().Render(figure, context);

        Assert.NotNull(result.FigureMapper);
        Assert.True(context.TextCount >= 2); // both annotations drew (plus tick labels)

        // The figure mapper spans the whole surface.
        Point2D bottomRight = result.FigureMapper!.DataToPixel(1, 1);
        Assert.Equal(640, bottomRight.X, precision: 6);
        Assert.Equal(480, bottomRight.Y, precision: 6);
    }

    [Fact]
    public void FigureRenderer_SkipsHiddenAnnotations()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        TextAnnotation note = axes.AddText(0.5, 0.5, "hidden");
        note.Visible = false;

        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);

        Assert.Equal(Rect2D.Empty, note.RenderedBounds);
    }

    [Fact]
    public void Annotations_DoNotAffectAutoScale()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 });
        axes.AddText(500, 500, "far away");

        figure.RecomputeDataBounds();

        Assert.True(axes.PrimaryXAxis.Range.Max < 10);
        Assert.True(axes.PrimaryYAxis.Range.Max < 10);
    }
}
