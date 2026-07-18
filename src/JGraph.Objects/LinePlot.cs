using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A line plot connecting its data samples, optionally with markers. Large ascending series are
/// automatically decimated to a per-pixel min/max envelope so millions of points render smoothly.
/// </summary>
public sealed class LinePlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _color;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private MarkerType _marker = MarkerType.None;
    private double _markerSize = 6;
    private Color? _markerFill;

    private Point2D[] _dataBuffer = new Point2D[16];
    private Point2D[] _pixelBuffer = new Point2D[16];

    public LinePlot(IDataSeries data)
        : base(data)
    {
        Name = "Line";
    }

    public LinePlot(double[] xs, double[] ys)
        : this(new ArrayDataSeries(xs, ys))
    {
    }

    /// <summary>Explicit line color, or null to use the auto series color.</summary>
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

    [Category("Appearance")]
    public MarkerType Marker
    {
        get => _marker;
        set => SetProperty(ref _marker, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Marker size")]
    public double MarkerSize
    {
        get => _markerSize;
        set => SetProperty(ref _markerSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Marker interior color, or null for open (unfilled) markers.</summary>
    [Category("Appearance"), DisplayName("Marker fill")]
    public Color? MarkerFill
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = _color ?? state.SeriesColor;
        var line = new LineStyle(color.WithOpacity(Opacity), _lineWidth, _dashStyle);
        SeriesRenderer.DrawLine(context, state, Data, line, ref _dataBuffer, ref _pixelBuffer);

        if (_marker != MarkerType.None && Data.Count <= SeriesRenderer.MaxMarkerCount)
        {
            var marker = new MarkerStyle(_marker, _markerSize, _markerFill, color);
            SeriesRenderer.DrawMarkers(context, state, Data, marker, color, ref _pixelBuffer);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var line = new LineStyle(color, _lineWidth, _dashStyle);
        MarkerStyle? marker = _marker != MarkerType.None
            ? new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _markerFill, color)
            : null;
        return new LegendKey(line, marker, swatch: null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible || Data.Count == 0)
        {
            return null;
        }

        if (SeriesHitTester.FindNearest(Data, mapper, pixelPoint, tolerancePixels) is not var (index, distance))
        {
            return null;
        }

        return new PlotHitResult(this, new Point2D(Data.GetX(index), Data.GetY(index)), distance, index);
    }
}
