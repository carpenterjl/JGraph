using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// The shared base of two-corner shape annotations (rectangle, ellipse): two anchor corners in the
/// annotation's coordinate space plus stroke and fill styling.
/// </summary>
public abstract class ShapeAnnotation : AnnotationObject, IDrawable
{
    private Point2D _corner1;
    private Point2D _corner2;
    private Color? _stroke;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private Color? _fill;

    protected ShapeAnnotation(Point2D corner1, Point2D corner2)
    {
        _corner1 = corner1;
        _corner2 = corner2;
    }

    /// <summary>One corner of the shape's bounding box, in this annotation's coordinate space.</summary>
    [Browsable(false)]
    public Point2D Corner1
    {
        get => _corner1;
        set => SetProperty(ref _corner1, value, InvalidationKind.Render);
    }

    /// <summary>The opposite corner of the shape's bounding box.</summary>
    [Browsable(false)]
    public Point2D Corner2
    {
        get => _corner2;
        set => SetProperty(ref _corner2, value, InvalidationKind.Render);
    }

    /// <summary>Outline color, or null to use the theme's default annotation ink.</summary>
    [Category("Appearance")]
    public Color? Stroke
    {
        get => _stroke;
        set => SetProperty(ref _stroke, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Dash style")]
    public DashStyle DashStyle
    {
        get => _dashStyle;
        set => SetProperty(ref _dashStyle, value, InvalidationKind.Render);
    }

    /// <summary>Interior fill color, or null for an unfilled shape.</summary>
    [Category("Appearance")]
    public Color? Fill
    {
        get => _fill;
        set => SetProperty(ref _fill, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public override IReadOnlyList<Point2D> GetAnchorPoints() => new[] { _corner1, _corner2 };

    /// <inheritdoc />
    public override void SetAnchorPoints(IReadOnlyList<Point2D> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count != 2)
        {
            throw new ArgumentException($"{GetType().Name} has exactly two anchor points.", nameof(anchors));
        }

        Corner1 = anchors[0];
        Corner2 = anchors[1];
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Point2D a = state.Mapper.DataToPixel(_corner1.X, _corner1.Y);
        Point2D b = state.Mapper.DataToPixel(_corner2.X, _corner2.Y);
        Rect2D rect = Rect2D.FromCorners(a, b);

        Color ink = (_stroke ?? state.SeriesColor).WithOpacity(Opacity);
        LineStyle? stroke = _lineWidth > 0 ? new LineStyle(ink, _lineWidth, _dashStyle) : null;
        Color? fill = _fill?.WithOpacity(Opacity);

        RenderShape(context, rect, stroke, fill);

        double pad = (_lineWidth / 2) + 1;
        SetRenderedBounds(new Rect2D(
            rect.X - pad,
            rect.Y - pad,
            rect.Width + (2 * pad),
            rect.Height + (2 * pad)));
    }

    /// <summary>Draws the concrete shape into the device-space rectangle.</summary>
    protected abstract void RenderShape(IRenderContext context, Rect2D rect, LineStyle? stroke, Color? fill);
}

/// <summary>A rectangle annotation defined by two opposite corners.</summary>
public sealed class RectangleAnnotation : ShapeAnnotation
{
    public RectangleAnnotation()
        : base(Point2D.Zero, Point2D.Zero)
    {
        Name = "Rectangle";
    }

    public RectangleAnnotation(double x1, double y1, double x2, double y2)
        : base(new Point2D(x1, y1), new Point2D(x2, y2))
    {
        Name = "Rectangle";
    }

    /// <inheritdoc />
    protected override void RenderShape(IRenderContext context, Rect2D rect, LineStyle? stroke, Color? fill) =>
        context.DrawRectangle(rect, stroke, fill);
}

/// <summary>An ellipse annotation inscribed in the box defined by two opposite corners.</summary>
public sealed class EllipseAnnotation : ShapeAnnotation
{
    private const int Segments = 64;

    public EllipseAnnotation()
        : base(Point2D.Zero, Point2D.Zero)
    {
        Name = "Ellipse";
    }

    public EllipseAnnotation(double x1, double y1, double x2, double y2)
        : base(new Point2D(x1, y1), new Point2D(x2, y2))
    {
        Name = "Ellipse";
    }

    /// <inheritdoc />
    protected override void RenderShape(IRenderContext context, Rect2D rect, LineStyle? stroke, Color? fill)
    {
        double rx = rect.Width / 2;
        double ry = rect.Height / 2;
        Span<Point2D> points = stackalloc Point2D[Segments];
        for (int i = 0; i < Segments; i++)
        {
            double angle = 2 * System.Math.PI * i / Segments;
            points[i] = new Point2D(
                rect.CenterX + (rx * System.Math.Cos(angle)),
                rect.CenterY + (ry * System.Math.Sin(angle)));
        }

        context.DrawPolygon(points, stroke, fill);
    }
}
