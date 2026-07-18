using System.ComponentModel;
using System.Globalization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Annotations;

/// <summary>
/// A persistent data tip (MATLAB-style point label): a marker pinned to a captured data point, a
/// label box showing its coordinates (or custom <see cref="Text"/>), and a leader line between
/// them. The pin stores plain data coordinates — not a plot reference — so it serializes as two
/// doubles and survives plot deletion/reorder; if the data changes underneath, the tip stays at the
/// captured coordinates. The single anchor point is the LABEL position: dragging moves the label
/// while the pin never leaves the data point.
/// </summary>
public sealed class DataTipAnnotation : AnnotationObject, IDrawable
{
    private Point2D _pinnedPoint;
    private Point2D _labelPosition;
    private string _text = string.Empty;
    private string _sourceSeries = string.Empty;
    private int _pointIndex = -1;
    private double _fontSize = 11;
    private Color? _color;
    private Color? _background;
    private double _markerSize = 6;

    public DataTipAnnotation()
    {
        Name = "Data tip";
    }

    /// <summary>Creates a tip pinned at a data point, with the label offset set by the placing tool.</summary>
    public DataTipAnnotation(Point2D pinnedPoint, Point2D labelPosition)
        : this()
    {
        _pinnedPoint = pinnedPoint;
        _labelPosition = labelPosition;
    }

    /// <summary>The pinned data point (never moved by dragging).</summary>
    [Browsable(false)]
    public Point2D PinnedPoint
    {
        get => _pinnedPoint;
        set => SetProperty(ref _pinnedPoint, value, InvalidationKind.Render);
    }

    /// <summary>The label box's anchor position, in data coordinates (dragging moves this).</summary>
    [Browsable(false)]
    public Point2D LabelPosition
    {
        get => _labelPosition;
        set => SetProperty(ref _labelPosition, value, InvalidationKind.Render);
    }

    /// <summary>The pinned X coordinate (editable; the marker follows).</summary>
    [Category("Data"), DisplayName("Pinned X")]
    public double PinnedX
    {
        get => _pinnedPoint.X;
        set => PinnedPoint = new Point2D(value, _pinnedPoint.Y);
    }

    /// <summary>The pinned Y coordinate (editable; the marker follows).</summary>
    [Category("Data"), DisplayName("Pinned Y")]
    public double PinnedY
    {
        get => _pinnedPoint.Y;
        set => PinnedPoint = new Point2D(_pinnedPoint.X, value);
    }

    /// <summary>Custom label text; empty shows the pinned coordinates.</summary>
    [Category("General")]
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>The name of the series the tip was placed on, informational only.</summary>
    [Category("Data"), DisplayName("Source series")]
    public string SourceSeries
    {
        get => _sourceSeries;
        set => SetProperty(ref _sourceSeries, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>The index of the picked point within its series at placement time (-1 when unknown).</summary>
    [Browsable(false)]
    public int PointIndex
    {
        get => _pointIndex;
        set => SetProperty(ref _pointIndex, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Font size")]
    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, System.Math.Max(1, value), InvalidationKind.Render);
    }

    /// <summary>Ink for the text, leader, marker, and box border; null uses the theme default.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
    }

    /// <summary>Label box fill; null uses a translucent theme surface.</summary>
    [Category("Appearance")]
    public Color? Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    /// <summary>The pin marker's diameter in device-independent units.</summary>
    [Category("Appearance"), DisplayName("Marker size")]
    public double MarkerSize
    {
        get => _markerSize;
        set => SetProperty(ref _markerSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>The label shown in the box: <see cref="Text"/>, or the pinned coordinates.</summary>
    public string EffectiveLabel => _text.Length > 0
        ? _text
        : string.Create(CultureInfo.InvariantCulture, $"({_pinnedPoint.X:G6}, {_pinnedPoint.Y:G6})");

    /// <inheritdoc />
    public override IReadOnlyList<Point2D> GetAnchorPoints() => new[] { _labelPosition };

    /// <inheritdoc />
    public override void SetAnchorPoints(IReadOnlyList<Point2D> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        if (anchors.Count != 1)
        {
            throw new ArgumentException("DataTipAnnotation has exactly one anchor point (the label).", nameof(anchors));
        }

        LabelPosition = anchors[0];
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color ink = _color ?? state.SeriesColor;
        Point2D pin = state.Mapper.DataToPixel(_pinnedPoint.X, _pinnedPoint.Y);
        Point2D anchor = state.Mapper.DataToPixel(_labelPosition.X, _labelPosition.Y);

        var style = new TextStyle(ink.WithOpacity(Opacity), _fontSize, "Segoe UI", bold: false, italic: false);
        string label = EffectiveLabel;
        Size2D textSize = context.MeasureText(label, style);
        const double Padding = 4;
        double boxWidth = textSize.Width + (Padding * 2);
        double boxHeight = textSize.Height + (Padding * 2);

        // The label box centers on its anchor; the leader runs pin → nearest box edge point.
        var box = new Rect2D(anchor.X - (boxWidth / 2), anchor.Y - (boxHeight / 2), boxWidth, boxHeight);
        Point2D leaderEnd = ClosestEdgePoint(box, pin);
        context.DrawLine(pin, leaderEnd, new LineStyle(ink.WithOpacity(Opacity * 0.8), 1));

        Color fill = _background ?? JGraph.Core.Drawing.Color.FromArgb(230, 255, 255, 235);
        context.DrawRectangle(box, new LineStyle(ink.WithOpacity(Opacity), 1), fill.WithOpacity(Opacity));
        context.DrawText(label, new Point2D(box.X + Padding, box.Y + Padding), style,
            HorizontalAlignment.Left, VerticalAlignment.Top);

        if (_markerSize > 0)
        {
            var marker = new MarkerStyle(MarkerType.Circle, _markerSize, fill.WithOpacity(Opacity),
                ink.WithOpacity(Opacity), 1.5);
            context.DrawMarkers(stackalloc Point2D[] { pin }, marker, ink);
        }

        SetRenderedBounds(box); // the label box is the hit/drag target
    }

    private static Point2D ClosestEdgePoint(Rect2D box, Point2D from)
    {
        double x = System.Math.Clamp(from.X, box.X, box.X + box.Width);
        double y = System.Math.Clamp(from.Y, box.Y, box.Y + box.Height);
        return new Point2D(x, y);
    }
}
