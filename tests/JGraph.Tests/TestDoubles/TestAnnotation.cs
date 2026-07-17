using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Tests.TestDoubles;

/// <summary>
/// A minimal concrete <see cref="AnnotationObject"/> with one anchor and directly settable rendered
/// bounds, so selection/edit-mode behavior can be exercised without running a renderer.
/// </summary>
internal sealed class TestAnnotation : AnnotationObject
{
    private Point2D _position;

    public TestAnnotation(double x = 0, double y = 0)
    {
        Name = "TestAnnotation";
        _position = new Point2D(x, y);
    }

    public Point2D Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Render);
    }

    public override IReadOnlyList<Point2D> GetAnchorPoints() => new[] { _position };

    public override void SetAnchorPoints(IReadOnlyList<Point2D> anchors) => Position = anchors[0];

    /// <summary>Simulates a paint by recording device-space bounds for hit-testing.</summary>
    public void SetBounds(Rect2D bounds) => SetRenderedBounds(bounds);
}
