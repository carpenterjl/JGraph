using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Model;

public class AnnotationObjectTests
{
    [Fact]
    public void SetAnchorPoints_MovesAndInvalidates()
    {
        var annotation = new TestAnnotation(1, 2);
        InvalidationKind? seen = null;
        annotation.Invalidated += (_, e) => seen = e.Kind;

        annotation.SetAnchorPoints(new[] { new Point2D(5, 6) });

        Assert.Equal(new Point2D(5, 6), annotation.GetAnchorPoints()[0]);
        Assert.Equal(InvalidationKind.Render, seen);
    }

    [Fact]
    public void TranslateByPixels_IsExactThroughTheMapper()
    {
        // 100x100 pixel rect over normalized [0,1]²: 10 px = 0.1 units.
        var mapper = new NormalizedCoordinateMapper(new Rect2D(0, 0, 100, 100));
        var annotation = new TestAnnotation(0.5, 0.5);

        annotation.TranslateByPixels(new Vector2D(10, -20), mapper);

        Point2D anchor = annotation.GetAnchorPoints()[0];
        Assert.Equal(0.6, anchor.X, precision: 12);
        Assert.Equal(0.3, anchor.Y, precision: 12);
    }

    [Fact]
    public void HitTest_UsesRenderedBoundsWithTolerance()
    {
        var annotation = new TestAnnotation();
        Assert.False(annotation.HitTest(new Point2D(10, 10), 3)); // never painted

        annotation.SetBounds(new Rect2D(10, 10, 30, 20));
        Assert.True(annotation.HitTest(new Point2D(25, 20), 0));
        Assert.True(annotation.HitTest(new Point2D(8, 10), 3));   // within tolerance
        Assert.False(annotation.HitTest(new Point2D(5, 10), 3));  // beyond tolerance
    }

    [Fact]
    public void Annotations_AttachToAxesAndFigure_WithParentLinks()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        var dataAnnotation = new TestAnnotation();
        axes.Annotations.Add(dataAnnotation);
        Assert.Same(axes, dataAnnotation.Parent);

        var figureAnnotation = new TestAnnotation { Space = AnnotationSpace.Figure };
        figure.Annotations.Add(figureAnnotation);
        Assert.Same(figure, figureAnnotation.Parent);
    }

    [Fact]
    public void AddingAnnotation_RaisesStructureInvalidation()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        InvalidationKind? seen = null;
        figure.Invalidated += (_, e) => seen = e.Kind;

        axes.Annotations.Add(new TestAnnotation());
        Assert.Equal(InvalidationKind.Structure, seen);
    }

    [Fact]
    public void MoveAnnotationAction_RestoresAnchors()
    {
        var annotation = new TestAnnotation(1, 1);
        Point2D[] before = { new(1, 1) };
        Point2D[] after = { new(4, 5) };
        annotation.SetAnchorPoints(after);

        var action = new MoveAnnotationAction(annotation, before, after);
        action.Undo();
        Assert.Equal(new Point2D(1, 1), annotation.Position);
        action.Redo();
        Assert.Equal(new Point2D(4, 5), annotation.Position);
    }

    [Fact]
    public void RemoveAnnotationAction_UndoReinsertsAtOriginalIndex()
    {
        var axes = new AxesModel();
        var first = new TestAnnotation();
        var second = new TestAnnotation();
        var third = new TestAnnotation();
        axes.Annotations.AddRange(new[] { first, second, third });

        axes.Annotations.RemoveAt(1);
        var action = new RemoveAnnotationAction(axes.Annotations, second, 1);

        action.Undo();
        Assert.Equal(new AnnotationObject[] { first, second, third }, axes.Annotations);
        Assert.Same(axes, second.Parent);

        action.Redo();
        Assert.Equal(new AnnotationObject[] { first, third }, axes.Annotations);
    }
}
