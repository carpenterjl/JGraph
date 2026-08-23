using System.ComponentModel;
using System.Globalization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>Which way a <see cref="ConstantLinePlot"/> runs, and therefore which value it fixes.</summary>
public enum ConstantLineDirection
{
    /// <summary>A line at a fixed X, spanning the axes vertically — MATLAB <c>xline</c>.</summary>
    Vertical,

    /// <summary>A line at a fixed Y, spanning the axes horizontally — MATLAB <c>yline</c>.</summary>
    Horizontal,
}

/// <summary>Which way a <see cref="ConstantLinePlot"/>'s label reads (MATLAB <c>LabelOrientation</c>).</summary>
public enum ConstantLineLabelOrientation
{
    /// <summary>Along the line, so a vertical line's label reads upward. MATLAB's default.</summary>
    Aligned,

    /// <summary>Left to right, whichever way the line runs.</summary>
    Horizontal,
}

/// <summary>
/// A reference line across the whole axes at one value (MATLAB <c>xline</c>/<c>yline</c>): a limit, a
/// threshold, a mean, the moment something happened.
/// <para>
/// Unlike every other plot object it reports no data extent. That is deliberate and matches MATLAB: a
/// constant line marks the data rather than being data, so drawing a threshold at 1000 beside a series
/// that reaches 3 must not flatten the series. It also means the line always spans the visible axes,
/// because it is drawn from the plot rectangle rather than from coordinates that could fall short.
/// </para>
/// </summary>
public sealed class ConstantLinePlot : PlotObject, IDrawable, ILegendItem
{
    private ConstantLineDirection _direction;
    private double _value;
    private Color? _color;
    private double _lineWidth = 1;
    private DashStyle _dash = DashStyle.Dash;
    private string _label = string.Empty;
    private TextStyle? _labelStyle;
    private HorizontalAlignment _labelHorizontalAlignment = HorizontalAlignment.Right;
    private VerticalAlignment _labelVerticalAlignment = VerticalAlignment.Top;
    private ConstantLineLabelOrientation _labelOrientation = ConstantLineLabelOrientation.Aligned;

    public ConstantLinePlot(ConstantLineDirection direction, double value)
    {
        _direction = direction;
        _value = value;
        Name = direction == ConstantLineDirection.Vertical ? "X line" : "Y line";
    }

    /// <summary>Whether the line is drawn at a fixed X (vertical) or a fixed Y (horizontal).</summary>
    [Category("Appearance")]
    public ConstantLineDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value, InvalidationKind.Render);
    }

    /// <summary>The coordinate the line is fixed at, in the direction it does not run.</summary>
    [Category("Appearance")]
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Render);
    }

    /// <summary>Explicit line color, or null to take the axes' next series color.</summary>
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

    /// <summary>The dash pattern. Dashed by default, as MATLAB's constant lines are.</summary>
    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle Dash
    {
        get => _dash;
        set => SetProperty(ref _dash, value, InvalidationKind.Render);
    }

    /// <summary>Text drawn along the line, or empty for none.</summary>
    [Category("General")]
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <summary>How the label is drawn, or null to follow the line's own color at 10 point.</summary>
    [Category("General"), DisplayName("Label style")]
    public TextStyle? LabelStyle
    {
        get => _labelStyle;
        set => SetProperty(ref _labelStyle, value, InvalidationKind.Render);
    }

    /// <summary>Which end of the line the label sits at, along the line's own direction.</summary>
    [Category("General"), DisplayName("Label horizontal alignment")]
    public HorizontalAlignment LabelHorizontalAlignment
    {
        get => _labelHorizontalAlignment;
        set => SetProperty(ref _labelHorizontalAlignment, value, InvalidationKind.Render);
    }

    /// <summary>Which side of the line the label sits on, across the line.</summary>
    [Category("General"), DisplayName("Label vertical alignment")]
    public VerticalAlignment LabelVerticalAlignment
    {
        get => _labelVerticalAlignment;
        set => SetProperty(ref _labelVerticalAlignment, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether the label reads along the line or straight across the page. It says nothing about a
    /// horizontal line, whose label reads left to right either way — which is why MATLAB's own
    /// default is the aligned one and why only a vertical line looks different for it.
    /// </summary>
    [Category("General"), DisplayName("Label orientation")]
    public ConstantLineLabelOrientation LabelOrientation
    {
        get => _labelOrientation;
        set => SetProperty(ref _labelOrientation, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>Empty: a constant line marks the data and must not stretch the axes to reach itself.</summary>
    public override DataRange GetXDataBounds() => DataRange.Empty;

    /// <inheritdoc cref="GetXDataBounds" />
    public override DataRange GetYDataBounds() => DataRange.Empty;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        Rect2D area = state.PlotArea;

        // The value is mapped through the axes; the span comes from the plot rectangle, so the line
        // reaches both edges however the view is panned.
        (Point2D from, Point2D to) = _direction == ConstantLineDirection.Vertical
            ? Ends(state.Mapper.DataToPixel(_value, 0).X, area.Top, area.Bottom, vertical: true)
            : Ends(state.Mapper.DataToPixel(0, _value).Y, area.Left, area.Right, vertical: false);

        if (!double.IsFinite(from.X) || !double.IsFinite(from.Y))
        {
            return;
        }

        context.DrawLine(from, to, new LineStyle(color, _lineWidth, _dash));

        if (_label.Length > 0)
        {
            DrawLabel(context, from, to, color);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(new LineStyle(_color ?? seriesColor, _lineWidth, _dash), marker: null, swatch: null);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        // Only the across-line distance decides: every point along the line is equally on it.
        double distance = _direction == ConstantLineDirection.Vertical
            ? System.Math.Abs(pixelPoint.X - mapper.DataToPixel(_value, 0).X)
            : System.Math.Abs(pixelPoint.Y - mapper.DataToPixel(0, _value).Y);

        if (!(distance <= tolerancePixels))
        {
            return null;
        }

        Point2D data = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        Point2D at = _direction == ConstantLineDirection.Vertical
            ? new Point2D(_value, data.Y)
            : new Point2D(data.X, _value);
        // No point index: every place along the line is the same line, not a sample of it.
        return new PlotHitResult(this, at, distance);
    }

    /// <summary>The default text for a line nobody named: the value it stands at.</summary>
    public string DefaultLabel => _value.ToString("G6", CultureInfo.InvariantCulture);

    private (Point2D From, Point2D To) Ends(double fixedCoordinate, double start, double end, bool vertical) =>
        vertical
            ? (new Point2D(fixedCoordinate, start), new Point2D(fixedCoordinate, end))
            : (new Point2D(start, fixedCoordinate), new Point2D(end, fixedCoordinate));

    /// <summary>
    /// Places the label along the line. The two alignments are read the way MATLAB reads them — one
    /// says where along the line, the other says which side of it — and both are applied by turning
    /// them into an offset from the chosen end, so the text never straddles the stroke.
    /// </summary>
    private void DrawLabel(IRenderContext context, Point2D from, Point2D to, Color color)
    {
        const double Gap = 4;
        TextStyle style = _labelStyle ?? new TextStyle(color, 10);

        if (_direction == ConstantLineDirection.Vertical
            && _labelOrientation == ConstantLineLabelOrientation.Horizontal)
        {
            // Read across the page instead of up the line. The end the label sits at is chosen the
            // same way; only the quarter turn and the alignments that went with it are dropped.
            double at = _labelHorizontalAlignment switch
            {
                HorizontalAlignment.Left => System.Math.Max(from.Y, to.Y) - Gap,
                HorizontalAlignment.Center => (from.Y + to.Y) / 2,
                _ => System.Math.Min(from.Y, to.Y) + Gap,
            };

            bool leftOfLine = _labelVerticalAlignment is VerticalAlignment.Bottom;
            context.DrawText(
                _label,
                new Point2D(from.X + (leftOfLine ? -Gap : Gap), at),
                style,
                leftOfLine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                _labelHorizontalAlignment == HorizontalAlignment.Left
                    ? VerticalAlignment.Bottom
                    : VerticalAlignment.Top);
            return;
        }

        if (_direction == ConstantLineDirection.Vertical)
        {
            // Along a vertical line, "horizontal alignment" chooses top or bottom of the span.
            double y = _labelHorizontalAlignment switch
            {
                HorizontalAlignment.Left => System.Math.Max(from.Y, to.Y) - Gap,
                HorizontalAlignment.Center => (from.Y + to.Y) / 2,
                _ => System.Math.Min(from.Y, to.Y) + Gap,
            };

            // ...and "vertical alignment" chooses which side of the stroke, rotated with the text.
            bool leftSide = _labelVerticalAlignment is VerticalAlignment.Bottom;

            // Rotated a quarter turn anticlockwise, a run of text grows upward from where it starts.
            // A label placed at the top of the line therefore has to end there instead of starting
            // there, or it climbs straight out of the axes -- which is what clipped every default
            // xline label in a tight layout, since the top is where the default puts it.
            HorizontalAlignment along = _labelHorizontalAlignment switch
            {
                HorizontalAlignment.Left => HorizontalAlignment.Left,
                HorizontalAlignment.Center => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right,
            };

            context.DrawText(
                _label,
                new Point2D(from.X + (leftSide ? -Gap : Gap), y),
                style,
                along,
                leftSide ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                rotationDegrees: -90);
            return;
        }

        double x = _labelHorizontalAlignment switch
        {
            HorizontalAlignment.Left => System.Math.Min(from.X, to.X) + Gap,
            HorizontalAlignment.Center => (from.X + to.X) / 2,
            _ => System.Math.Max(from.X, to.X) - Gap,
        };

        bool above = _labelVerticalAlignment != VerticalAlignment.Bottom;
        context.DrawText(
            _label,
            new Point2D(x, from.Y + (above ? -Gap : Gap)),
            style,
            _labelHorizontalAlignment,
            above ? VerticalAlignment.Bottom : VerticalAlignment.Top);
    }
}
