using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// A text label anchored at a point, with an optional padded background box and border. The anchor is
/// in data coordinates (or normalized figure coordinates when <see cref="AnnotationObject.Space"/> is
/// <see cref="AnnotationSpace.Figure"/>); the alignment properties say which corner/edge of the text
/// box sits on the anchor.
/// </summary>
public sealed class TextAnnotation : AnnotationObject, IDrawable
{
    private Point2D _position;
    private string _text = string.Empty;
    private double _fontSize = 12;
    private string _fontFamily = "Segoe UI";
    private bool _bold;
    private bool _italic;
    private Color? _color;
    private Color? _background;
    private Color? _borderColor;
    private double _padding = 4;
    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Left;
    private VerticalAlignment _verticalAlignment = VerticalAlignment.Bottom;

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

        Position = anchors[0];
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        if (string.IsNullOrEmpty(_text))
        {
            SetRenderedBounds(Rect2D.Empty);
            return;
        }

        Color ink = _color ?? state.SeriesColor;
        var style = new TextStyle(ink, _fontSize, _fontFamily, _bold, _italic);
        Size2D textSize = context.MeasureText(_text, style);

        Point2D anchor = state.Mapper.DataToPixel(_position.X, _position.Y);
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

        var box = new Rect2D(left, top, boxWidth, boxHeight);
        if (_background is not null || _borderColor is not null)
        {
            LineStyle? border = _borderColor is { } bc ? new LineStyle(bc.WithOpacity(Opacity), 1) : null;
            context.DrawRectangle(box, border, _background?.WithOpacity(Opacity));
        }

        context.DrawText(
            _text,
            new Point2D(left + _padding, top + _padding),
            style.WithColor(ink.WithOpacity(Opacity)),
            HorizontalAlignment.Left,
            VerticalAlignment.Top);

        SetRenderedBounds(box);
    }
}
