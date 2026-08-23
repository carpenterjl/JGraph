using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// A text label anchored at a point, with an optional padded background box and border. The anchor is
/// in data coordinates (or normalized figure coordinates when <see cref="AnnotationObject.Space"/> is
/// <see cref="AnnotationSpace.Figure"/>); the alignment properties say which corner/edge of the text
/// box sits on the anchor.
///
/// In a 3D axes the anchor is <see cref="Position"/> plus <see cref="Z"/>, projected through the same
/// camera as the plots — which is what makes MATLAB's <c>text(x, y, z, str)</c> label a point on a
/// surface. The label itself stays upright and screen-sized; only its anchor moves with the camera.
/// </summary>
public sealed class TextAnnotation : AnnotationObject, IDrawable, I3DDrawable
{
    private Point2D _position;
    private double _z;
    private string _text = string.Empty;
    private double _fontSize = 12;
    private string _fontFamily = "Segoe UI";
    private bool _bold;
    private bool _italic;
    private TextInterpreter _interpreter = TextInterpreter.Tex;
    private Color? _color;
    private Color? _background;
    private Color? _borderColor;
    private double _padding = 4;
    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Left;
    private VerticalAlignment _verticalAlignment = VerticalAlignment.Bottom;
    private Rect2D? _box;
    private double _rotation;
    private double _borderWidth = 1;
    private DashStyle _borderDash = DashStyle.Solid;
    private bool _smoothing = true;
    private bool _clipping;

    public TextAnnotation()
    {
        Name = "Text";
    }

    public TextAnnotation(double x, double y, string text)
        : this()
    {
        _position = new Point2D(x, y);
        _text = text ?? string.Empty;
    }

    /// <summary>The anchor point, in this annotation's coordinate space.</summary>
    [Browsable(false)]
    public Point2D Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The anchor's height, used only in a 3D axes. Zero — the default, and what every label written
    /// before M45 carries — puts the label on the floor of the box, which is where a 2D label sits.
    /// </summary>
    [Category("General")]
    public double Z
    {
        get => _z;
        set => SetProperty(ref _z, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Which markup <see cref="Text"/> is written in (MATLAB <c>Interpreter</c>). TeX is the default,
    /// so a label saying \sigma shows one; 'none' shows the backslash and the word.
    /// </summary>
    [Category("Appearance")]
    public TextInterpreter Interpreter
    {
        get => _interpreter;
        set => SetProperty(ref _interpreter, value, InvalidationKind.Render);
    }

    /// <summary>The text to display.</summary>
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

    [Category("Appearance")]
    public bool Bold
    {
        get => _bold;
        set => SetProperty(ref _bold, value, InvalidationKind.Render);
    }

    [Category("Appearance")]
    public bool Italic
    {
        get => _italic;
        set => SetProperty(ref _italic, value, InvalidationKind.Render);
    }

    /// <summary>Text color, or null to use the theme's default annotation ink.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
    }

    /// <summary>Background box fill, or null for no box.</summary>
    [Category("Appearance")]
    public Color? Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    /// <summary>Background box border color, or null for no border.</summary>
    [Category("Appearance"), DisplayName("Border color")]
    public Color? BorderColor
    {
        get => _borderColor;
        set => SetProperty(ref _borderColor, value, InvalidationKind.Render);
    }

    /// <summary>Padding between the text and the box edge, in device-independent units.</summary>
    [Category("Appearance")]
    public double Padding
    {
        get => _padding;
        set => SetProperty(ref _padding, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Which horizontal edge of the text box sits on the anchor.</summary>
    [Category("Appearance"), DisplayName("Horizontal alignment")]
    public HorizontalAlignment HorizontalAlignment
    {
        get => _horizontalAlignment;
        set => SetProperty(ref _horizontalAlignment, value, InvalidationKind.Render);
    }

    /// <summary>Which vertical edge of the text box sits on the anchor.</summary>
    [Category("Appearance"), DisplayName("Vertical alignment")]
    public VerticalAlignment VerticalAlignment
    {
        get => _verticalAlignment;
        set => SetProperty(ref _verticalAlignment, value, InvalidationKind.Render);
    }

    /// <summary>
    /// An explicit box in this annotation's coordinate space, or null to size the box to the text.
    /// A label placed by hand fits what it says; a box a script asked for by size keeps that size,
    /// which is what <c>annotation('textbox', [x y w h])</c> means.
    /// </summary>
    [Browsable(false)]
    public Rect2D? Box
    {
        get => _box;
        set => SetProperty(ref _box, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How far the label is turned, in degrees anticlockwise about its anchor (MATLAB
    /// <c>Rotation</c>). The box turns with the text, so a rotated label's background stays behind it.
    /// </summary>
    [Category("Appearance")]
    public double Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, value, InvalidationKind.Render);
    }

    /// <summary>How thick the box's border is drawn (MATLAB <c>LineWidth</c>).</summary>
    [Category("Appearance"), DisplayName("Border width")]
    public double BorderWidth
    {
        get => _borderWidth;
        set => SetProperty(ref _borderWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>The dash pattern of the box's border (MATLAB <c>LineStyle</c>).</summary>
    [Category("Appearance"), DisplayName("Border style")]
    public DashStyle BorderDash
    {
        get => _borderDash;
        set => SetProperty(ref _borderDash, value, InvalidationKind.Render);
    }

    /// <summary>Whether the glyphs are drawn antialiased (MATLAB <c>FontSmoothing</c>).</summary>
    [Category("Appearance"), DisplayName("Font smoothing")]
    public bool Smoothing
    {
        get => _smoothing;
        set => SetProperty(ref _smoothing, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether the label is cut off at the edge of the plot box (MATLAB <c>Clipping</c>). Off is
    /// MATLAB's default for text and this build's: a label placed just outside the data should still
    /// be read, which is the whole point of putting it there.
    /// </summary>
    [Category("Behavior")]
    public bool Clipping
    {
        get => _clipping;
        set => SetProperty(ref _clipping, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public override IReadOnlyList<Point2D> GetAnchorPoints() => new[] { _position };

    /// <inheritdoc />
    public override void SetAnchorPoints(IReadOnlyList<Point2D> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count != 1)
        {
            throw new ArgumentException("TextAnnotation has exactly one anchor point.", nameof(anchors));
        }

        Point2D moved = anchors[0];
        if (_box is { } current)
        {
            Box = new Rect2D(
                current.X + (moved.X - _position.X),
                current.Y + (moved.Y - _position.Y),
                current.Width,
                current.Height);
        }

        Position = moved;
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DrawAt(
            context,
            state,
            () => state.Mapper.DataToPixel(_position.X, _position.Y),
            () => _box is { } box
                ? Rect2D.FromCorners(
                    state.Mapper.DataToPixel(box.Left, box.Top),
                    state.Mapper.DataToPixel(box.Right, box.Bottom))
                : null);
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        // A given box is a figure-space rectangle and has no meaning in a projected axes, so a 3-D
        // label always sizes itself to what it says.
        ArgumentNullException.ThrowIfNull(projection);
        DrawAt(context, state, () => projection.ProjectPoint(_position.X, _position.Y, _z), () => null);
    }

    private void DrawAt(IRenderContext context, RenderState state, Func<Point2D> anchorAt, Func<Rect2D?> boxAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        Rect2D? given = boxAt();
        if (string.IsNullOrEmpty(_text) && given is null)
        {
            SetRenderedBounds(Rect2D.Empty);
            return;
        }

        Color ink = _color ?? state.SeriesColor;
        var style = new TextStyle(ink, _fontSize, _fontFamily, _bold, _italic, _interpreter, _smoothing);
        Size2D textSize = context.MeasureText(_text, style);

        Rect2D box;
        Point2D pivot;
        if (given is { } fixedBox)
        {
            box = fixedBox;
            pivot = new Point2D(fixedBox.Left, fixedBox.Top);
        }
        else
        {
            Point2D anchor = anchorAt();
            pivot = anchor;
            double boxWidth = textSize.Width + (_padding * 2);
            double boxHeight = textSize.Height + (_padding * 2);

            double left = _horizontalAlignment switch
            {
                HorizontalAlignment.Center => anchor.X - (boxWidth / 2),
                HorizontalAlignment.Right => anchor.X - boxWidth,
                _ => anchor.X,
            };
            double top = _verticalAlignment switch
            {
                VerticalAlignment.Middle => anchor.Y - (boxHeight / 2),
                VerticalAlignment.Bottom or VerticalAlignment.Baseline => anchor.Y - boxHeight,
                _ => anchor.Y,
            };

            box = new Rect2D(left, top, boxWidth, boxHeight);
        }

        // A turned label turns about its anchor, box and all. There is no transform stack to lean on,
        // so the four corners are turned here and drawn as a polygon — which is also why the recorded
        // bounds stay the upright box: a rotated label's hit area is its unturned extent, and that is
        // what MATLAB's Extent reports too.
        bool turned = System.Math.Abs(_rotation) > 1e-9;
        bool clip = _clipping && !state.PlotArea.IsEmpty;
        if (clip)
        {
            context.PushClip(state.PlotArea);
        }

        if (_background is not null || _borderColor is not null)
        {
            LineStyle? border = _borderColor is { } bc && _borderWidth > 0 && _borderDash != DashStyle.None
                ? new LineStyle(bc.WithOpacity(Opacity), _borderWidth, _borderDash)
                : null;

            if (turned)
            {
                Span<Point2D> corners =
                [
                    Turn(new Point2D(box.Left, box.Top), pivot),
                    Turn(new Point2D(box.Right, box.Top), pivot),
                    Turn(new Point2D(box.Right, box.Bottom), pivot),
                    Turn(new Point2D(box.Left, box.Bottom), pivot),
                ];
                context.DrawPolygon(corners, border, _background?.WithOpacity(Opacity));
            }
            else
            {
                context.DrawRectangle(box, border, _background?.WithOpacity(Opacity));
            }
        }

        if (_text.Length > 0)
        {
            var origin = new Point2D(box.Left + _padding, box.Top + _padding);
            context.DrawText(
                _text,
                turned ? Turn(origin, pivot) : origin,
                style.WithColor(ink.WithOpacity(Opacity)),
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                _rotation);
        }

        if (clip)
        {
            context.PopClip();
        }

        SetRenderedBounds(box);
    }

    /// <summary>Turns a point about <paramref name="pivot"/> by <see cref="Rotation"/> degrees, anticlockwise on the page.</summary>
    private Point2D Turn(Point2D point, Point2D pivot)
    {
        double radians = _rotation * System.Math.PI / 180;
        double cos = System.Math.Cos(radians);
        double sin = System.Math.Sin(radians);
        double dx = point.X - pivot.X;

        // Device y counts downward, so a positive angle turns the other way here than it does on paper.
        double dy = point.Y - pivot.Y;
        return new Point2D(
            pivot.X + (dx * cos) + (dy * sin),
            pivot.Y - (dx * sin) + (dy * cos));
    }
}
