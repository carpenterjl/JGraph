using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// A straight line from <see cref="Start"/> to <see cref="End"/> with an optional filled arrow head at
/// the end. With <see cref="ShowHead"/> off it doubles as a plain line annotation, and with
/// <see cref="ShowTailHead"/> on it is double-headed. A non-empty <see cref="Text"/> is drawn beside
/// the tail, which is the labelled arrow MATLAB spells <c>annotation('textarrow', …)</c>. Hit-testing
/// uses the distance to the rendered segment rather than the (potentially huge, mostly empty) bounding
/// box.
/// </summary>
public sealed class ArrowAnnotation : AnnotationObject, IDrawable
{
    private Point2D _start;
    private Point2D _end;
    private Color? _color;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private bool _showHead = true;
    private bool _showTailHead;
    private double _headLength = 12;
    private double _headWidth = 9;
    private string _text = string.Empty;
    private double _fontSize = 10;
    private string _fontFamily = "Segoe UI";

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

    /// <summary>Whether a second head is drawn at the tail, making this a double-headed arrow.</summary>
    [Category("Appearance"), DisplayName("Show tail head")]
    public bool ShowTailHead
    {
        get => _showTailHead;
        set => SetProperty(ref _showTailHead, value, InvalidationKind.Render);
    }

    /// <summary>A label drawn beside the tail, or empty for a plain arrow.</summary>
    [Category("General")]
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Font size")]
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, System.Math.Max(1, value), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Font family")]
    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value, InvalidationKind.Render);
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
        bool headed = length > 1e-9 && _headLength > 0;

        if (headed && (_showHead || _showTailHead))
        {
            // Pull each end of the shaft back by a head's length so it does not poke out of a tip.
            Vector2D unit = direction / length;
            Point2D shaftStart = _showTailHead ? a + (unit * System.Math.Min(_headLength, length)) : a;
            Point2D shaftEnd = _showHead ? b - (unit * System.Math.Min(_headLength, length)) : b;
            context.DrawLine(shaftStart, shaftEnd, line);

            if (_showHead)
            {
                DrawHead(context, b, unit, ink);
            }

            if (_showTailHead)
            {
                DrawHead(context, a, new Vector2D(-unit.X, -unit.Y), ink);
            }
        }
        else
        {
            context.DrawLine(a, b, line);
        }

        double pad = System.Math.Max(_headWidth, _lineWidth) / 2 + 1;
        Rect2D bounds = Rect2D.FromCorners(a, b);

        if (_text.Length > 0)
        {
            // The label sits on the far side of the tail from the tip, which is where MATLAB puts a
            // textarrow's string: the arrow points away from its own caption.
            var style = new TextStyle(ink, _fontSize, _fontFamily);
            Size2D size = context.MeasureText(_text, style);
            bool rightOfTail = b.X >= a.X;
            double textLeft = rightOfTail ? a.X - size.Width - pad : a.X + pad;
            double textTop = a.Y - (size.Height / 2);
            context.DrawText(
                _text,
                new Point2D(textLeft, textTop),
                style,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);
            bounds = Rect2D.FromCorners(
                new Point2D(System.Math.Min(bounds.Left, textLeft), System.Math.Min(bounds.Top, textTop)),
                new Point2D(
                    System.Math.Max(bounds.Right, textLeft + size.Width),
                    System.Math.Max(bounds.Bottom, textTop + size.Height)));
        }

        SetRenderedBounds(new Rect2D(
            bounds.X - pad,
            bounds.Y - pad,
            bounds.Width + (2 * pad),
            bounds.Height + (2 * pad)));
    }

    /// <summary>Fills one triangular head whose tip is at <paramref name="tip"/> and which points along <paramref name="unit"/>.</summary>
    private void DrawHead(IRenderContext context, Point2D tip, Vector2D unit, Color ink)
    {
        Vector2D normal = new(-unit.Y, unit.X);
        Point2D baseCenter = tip - (unit * _headLength);
        Span<Point2D> head = stackalloc Point2D[3];
        head[0] = tip;
        head[1] = baseCenter + (normal * (_headWidth / 2));
        head[2] = baseCenter - (normal * (_headWidth / 2));
        context.DrawPolygon(head, stroke: null, fill: ink);
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
