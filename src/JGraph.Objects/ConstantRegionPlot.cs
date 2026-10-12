using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>What a <see cref="ConstantRegionPlot"/>'s two values are (MATLAB's <c>Value</c> class).</summary>
public enum RegionValueKind
{
    /// <summary>Plain numbers.</summary>
    Number,

    /// <summary>Datetimes, held as days the way a datetime ruler holds them.</summary>
    Datetime,

    /// <summary>Durations, held as days the way a duration ruler holds them.</summary>
    Duration,
}

/// <summary>
/// A shaded band across the whole axes between two values (MATLAB <c>xregion</c>/<c>yregion</c>, open
/// item 108): a working range, a tolerance, the time something lasted.
/// <para>
/// Unlike a <see cref="ConstantLinePlot"/> it reports its extent, because R2025b's does: an
/// <c>xregion(20, 30)</c> beside data reaching 10 widens the limits to show the band. The band
/// still spans the plot rectangle the other way, whatever the limits there.
/// </para>
/// </summary>
public sealed class ConstantRegionPlot : PlotObject, IDrawable, ILegendItem
{
    /// <summary>R2025b's automatic face colour, a mid grey (FaceColorMode 'auto').</summary>
    public static readonly Color DefaultFaceColor = Color.FromRgb(125, 125, 125);

    private ConstantLineDirection _direction;
    private double _first;
    private double _second;
    private Color? _faceColor;
    private double _faceAlpha = 0.3;
    private Color _edgeColor = Colors.Transparent;
    private double _edgeAlpha = 1;
    private double _lineWidth = 0.5;
    private DashStyle _dash = DashStyle.Solid;
    private bool _onTop;

    public ConstantRegionPlot(ConstantLineDirection direction, double first, double second)
    {
        _direction = direction;
        _first = first;
        _second = second;
        Name = direction == ConstantLineDirection.Vertical ? "X region" : "Y region";
        ZOrder = -1; // R2025b's Layer 'bottom': under the data
    }

    /// <summary>Whether the band is between two x values (spanning vertically) or two y values.</summary>
    [Category("Appearance")]
    public ConstantLineDirection Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value, InvalidationKind.Layout);
    }

    /// <summary>The first of the two values, kept in the order written (MATLAB <c>Value(1)</c>).</summary>
    [Category("Appearance"), DisplayName("First value")]
    public double First
    {
        get => _first;
        set => SetProperty(ref _first, value, InvalidationKind.Layout);
    }

    /// <summary>The second of the two values (MATLAB <c>Value(2)</c>).</summary>
    [Category("Appearance"), DisplayName("Second value")]
    public double Second
    {
        get => _second;
        set => SetProperty(ref _second, value, InvalidationKind.Layout);
    }

    /// <summary>The fill, or null for R2025b's automatic grey; Transparent is no fill.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>How opaque the fill is, 0.3 by default as R2025b's is.</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The two boundary lines' colour; Transparent (R2025b's 'none', the default) draws none.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    /// <summary>How opaque the boundary lines are.</summary>
    [Category("Appearance"), DisplayName("Edge alpha")]
    public double EdgeAlpha
    {
        get => _edgeAlpha;
        set => SetProperty(ref _edgeAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle Dash
    {
        get => _dash;
        set => SetProperty(ref _dash, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether the band is drawn over the data (MATLAB <c>Layer</c> 'top') rather than under it, the
    /// default. It moves the band through <see cref="GraphObject.ZOrder"/>, which is the draw order.
    /// </summary>
    [Category("Appearance"), DisplayName("On top")]
    public bool OnTop
    {
        get => _onTop;
        set
        {
            if (SetProperty(ref _onTop, value, InvalidationKind.Render))
            {
                ZOrder = value ? 0 : -1;
            }
        }
    }

    /// <summary>
    /// Whether <see cref="FaceColor"/> was chosen (MATLAB <c>FaceColorMode</c> 'manual'). Back in
    /// 'auto' a band keeps the colour it had until a <see cref="PlotObject.SeriesIndex"/> names one,
    /// as R2025b's does (open item 111).
    /// </summary>
    [Browsable(false)]
    public bool FaceColorManual { get; set; }

    /// <summary>What the two values are: numbers, or times the axis holds as days (open item 111).</summary>
    [Browsable(false)]
    public RegionValueKind ValueKind { get; set; }

    /// <summary>The display format a datetime or duration band's values were written in.</summary>
    [Browsable(false)]
    public string? TimeFormat { get; set; }

    /// <summary>Whether that format was chosen rather than left to the type's default.</summary>
    [Browsable(false)]
    public bool TimeFormatSet { get; set; }

    /// <summary>The time zone a datetime band's values were written in, if any.</summary>
    [Browsable(false)]
    public string? TimeZone { get; set; }

    /// <summary>Whether <c>Value</c> reads back as a column, as a script wrote it (R2025b keeps the shape).</summary>
    [Browsable(false)]
    public bool ValueIsColumn { get; set; }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The finite part of the band, which is what the limits widen to show.</summary>
    private DataRange Extent
    {
        get
        {
            DataRange range = DataRange.Empty;
            foreach (double value in new[] { _first, _second })
            {
                if (double.IsFinite(value))
                {
                    range = range.Include(value);
                }
            }

            return range;
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() =>
        _direction == ConstantLineDirection.Vertical ? Extent : DataRange.Empty;

    /// <inheritdoc />
    public override DataRange GetYDataBounds() =>
        _direction == ConstantLineDirection.Horizontal ? Extent : DataRange.Empty;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        if (double.IsNaN(_first) || double.IsNaN(_second))
        {
            return;
        }

        Rect2D area = state.PlotArea;
        bool vertical = _direction == ConstantLineDirection.Vertical;
        double a = Across(state, _first, vertical, area);
        double b = Across(state, _second, vertical, area);
        if (!double.IsFinite(a) || !double.IsFinite(b))
        {
            return;
        }

        double low = System.Math.Min(a, b);
        double high = System.Math.Max(a, b);
        Rect2D band = vertical
            ? new Rect2D(low, area.Top, high - low, area.Height)
            : new Rect2D(area.Left, low, area.Width, high - low);

        Color face = FaceFor(state.SeriesColor);
        if (!face.IsTransparent)
        {
            context.DrawRectangle(band, stroke: null, face.WithOpacity(_faceAlpha * Opacity));
        }

        if (!_edgeColor.IsTransparent && _lineWidth > 0)
        {
            var stroke = new LineStyle(_edgeColor.WithOpacity(_edgeAlpha * Opacity), _lineWidth, _dash);
            foreach (double at in new[] { a, b })
            {
                if (vertical)
                {
                    context.DrawLine(new Point2D(at, area.Top), new Point2D(at, area.Bottom), stroke);
                }
                else
                {
                    context.DrawLine(new Point2D(area.Left, at), new Point2D(area.Right, at), stroke);
                }
            }
        }
    }

    /// <summary>A value mapped across the band's direction, an infinite one standing at the edge it points to.</summary>
    private static double Across(RenderState state, double value, bool vertical, Rect2D area)
    {
        if (double.IsInfinity(value))
        {
            // Which edge holds the larger values depends on the ruler's direction, so ask the mapper.
            bool positive = double.IsPositiveInfinity(value);
            Point2D near = state.Mapper.PixelToData(area.Left, area.Top);
            Point2D far = state.Mapper.PixelToData(area.Right, area.Bottom);
            return vertical
                ? (positive == (far.X > near.X) ? area.Right : area.Left)
                : (positive == (near.Y > far.Y) ? area.Top : area.Bottom);
        }

        double pixel = vertical ? state.Mapper.DataToPixel(value, 0).X : state.Mapper.DataToPixel(0, value).Y;
        double min = vertical ? area.Left : area.Top;
        double max = vertical ? area.Right : area.Bottom;
        return System.Math.Clamp(pixel, min - 1, max + 1);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: FaceFor(seriesColor).WithOpacity(_faceAlpha));

    /// <summary>
    /// The fill drawn: the colour written, else the colour order's entry once a SeriesIndex names one
    /// (R2025b's FaceColorMode 'auto'), else the grey.
    /// </summary>
    private Color FaceFor(Color seriesColor) => _faceColor ?? (SeriesIndex >= 0 ? seriesColor : DefaultFaceColor);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        Point2D data = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        double along = _direction == ConstantLineDirection.Vertical ? data.X : data.Y;
        double low = System.Math.Min(_first, _second);
        double high = System.Math.Max(_first, _second);
        return along >= low && along <= high ? new PlotHitResult(this, data, 0) : null;
    }
}
