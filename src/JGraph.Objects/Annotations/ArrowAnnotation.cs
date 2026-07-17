using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// A straight line from <see cref="Start"/> to <see cref="End"/> with an optional filled arrow head at
/// the end. With <see cref="ShowHead"/> off it doubles as a plain line annotation. Hit-testing uses
/// the distance to the rendered segment rather than the (potentially huge, mostly empty) bounding box.
/// </summary>
public sealed class ArrowAnnotation : AnnotationObject, IDrawable
{
    private Point2D _start;
    private Point2D _end;
    private Color? _color;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private bool _showHead = true;
    private double _headLength = 12;
    private double _headWidth = 9;

    private Point2D _renderedStart = Point2D.NaN;
    private Point2D _renderedEnd = Point2D.NaN;

    public ArrowAnnotation()
    {
        Name = "Arrow";
    }

    public ArrowAnnotation(double x1, double y1, double x2, double y2)
        : this()
    {
        _start = new Point2D(x1, y1);
        _end = new Point2D(x2, y2);
    }

    /// <summary>The tail point, in this annotation's coordinate space.</summary>
    [Browsable(false)]
    public Point2D Start
    {
        get => _start;
        set => SetProperty(ref _start, value, InvalidationKind.Render);
    }

    /// <summary>The tip point (where the head is drawn), in this annotation's coordinate space.</summary>
    [Browsable(false)]
    public Point2D End
    {
        get => _end;
        set => SetProperty(ref _end, value, InvalidationKind.Render);
    }

    /// <summary>Line and head color, or null to use the theme's default annotation ink.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
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

    /// <summary>Whether the arrow head is drawn; off makes this a plain line annotation.</summary>
    [Category("Appearance"), DisplayName("Show head")]
    public bool ShowHead
    {
        get => _showHead;
        set => SetProperty(ref _showHead, value, InvalidationKind.Render);
    }

    /// <summary>Arrow head length in device-independent units.</summary>
    [Category("Appearance"), DisplayName("Head length")]
    public double HeadLength
    {
        get => _headLength;
        set => SetProperty(ref _headLength, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Arrow head base width in device-independent units.</summary>
    [Category("Appearance"), DisplayName("Head width")]
    public double HeadWidth
    {
        get => _headWidth;
        set => SetProperty(ref _headWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <inheritdoc />
    public override IReadOnlyList<Point2D> GetAnchorPoints() => new[] { _start, _end };

    /// <inheritdoc />
    public override void SetAnchorPoints(IReadOnlyList<Point2D> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count != 2)
        {
            throw new ArgumentException("ArrowAnnotation has exactly two anchor points.", nameof(anchors));
        }

        Start = anchors[0];
        End = anchors[1];
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Point2D a = state.Mapper.DataToPixel(_start.X, _start.Y);
        Point2D b = state.Mapper.DataToPixel(_end.X, _end.Y);
        _renderedStart = a;
        _renderedEnd = b;

        Color ink = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        var line = new LineStyle(ink, _lineWidth, _dashStyle);

        Vector2D direction = b - a;
        double length = direction.Length;

        if (_showHead && length > 1e-9 && _headLength > 0)
        {
            // Shorten the shaft so it does not poke out of the head's tip.
            Vector2D unit = direction / length;
            double shaft = System.Math.Max(0, length - _headLength);
            Point2D shaftEnd = a + (unit * shaft);
            context.DrawLine(a, shaftEnd, line);

            Vector2D normal = new(-unit.Y, unit.X);
            Point2D baseCenter = b - (unit * _headLength);
            Span<Point2D> head = stackalloc Point2D[3];
            head[0] = b;
            head[1] = baseCenter + (normal * (_headWidth / 2));
            head[2] = baseCenter - (normal * (_headWidth / 2));
            context.DrawPolygon(head, stroke: null, fill: ink);
        }
        else
        {
            context.DrawLine(a, b, line);
        }

        double pad = System.Math.Max(_headWidth, _lineWidth) / 2 + 1;
        Rect2D bounds = Rect2D.FromCorners(a, b);
        SetRenderedBounds(new Rect2D(
            bounds.X - pad,
            bounds.Y - pad,
            bounds.Width + (2 * pad),
            bounds.Height + (2 * pad)));
    }

    /// <inheritdoc />
    public override bool HitTest(Point2D pixel, double tolerancePixels)
    {
        if (!_renderedStart.IsFinite || !_renderedEnd.IsFinite)
        {
            return false;
        }

        double pick = tolerancePixels + (System.Math.Max(_lineWidth, _showHead ? _headWidth : 0) / 2);
        return DistanceToSegment(pixel, _renderedStart, _renderedEnd) <= pick;
    }

    private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
    {
        Vector2D ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-18)
        {
            return p.DistanceTo(a);
        }

        Vector2D ap = p - a;
        double t = System.Math.Clamp(((ap.X * ab.X) + (ap.Y * ab.Y)) / lengthSquared, 0, 1);
        var closest = new Point2D(a.X + (ab.X * t), a.Y + (ab.Y * t));
        return p.DistanceTo(closest);
    }
}
