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
    private StepMode _steps = StepMode.None;
    private MarkerType _marker = MarkerType.None;
    private double _markerSize = 6;
    private Color? _markerFill;
    private Color? _markerEdge;
    private int[]? _markerIndices;
    private LineJoin _lineJoin = LineJoin.Miter;
    private bool _alignVertexCenters;

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
        set
        {
            SetProperty(ref _dashStyle, value, InvalidationKind.Render);
            LineStyleManual = true;
        }
    }

    /// <summary>
    /// Whether the samples are joined straight or as a stairstep (MATLAB <c>stairs</c>). Only the
    /// path between samples changes: markers still sit on the samples, and so do the data bounds.
    /// </summary>
    [Category("Appearance"), DisplayName("Step mode")]
    public StepMode Steps
    {
        get => _steps;
        set => SetProperty(ref _steps, value, InvalidationKind.Render);
    }

    [Category("Appearance")]
    public MarkerType Marker
    {
        get => _marker;
        set
        {
            SetProperty(ref _marker, value, InvalidationKind.Render);
            MarkerManual = true;
        }
    }

    [Category("Appearance"), DisplayName("Marker size")]
    public double MarkerSize
    {
        get => _markerSize;
        set => SetProperty(ref _markerSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Marker interior color, or null for open (unfilled) markers.</summary>
    [Category("Appearance"), DisplayName("Marker fill")]
    public Color? MarkerFaceColor
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <summary>Marker outline color, or null to draw it in the line's own colour.</summary>
    [Category("Appearance"), DisplayName("Marker edge")]
    public Color? MarkerEdgeColor
    {
        get => _markerEdge;
        set => SetProperty(ref _markerEdge, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Which samples carry a marker, counting from zero, or null for all of them. MATLAB's
    /// <c>MarkerIndices</c> is how a dense line is given a readable handful of markers rather than
    /// one per point.
    /// </summary>
    [Browsable(false)]
    public int[]? MarkerIndices
    {
        get => _markerIndices;
        set => SetProperty(ref _markerIndices, value, InvalidationKind.Render);
    }

    /// <summary>How corners between segments are joined.</summary>
    [Category("Appearance"), DisplayName("Line join")]
    public LineJoin LineJoin
    {
        get => _lineJoin;
        set => SetProperty(ref _lineJoin, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether vertices are snapped to pixel centres. A one-pixel line drawn between them is
    /// blurred across two rows of pixels; snapping is what makes it crisp, which is the whole of
    /// what MATLAB's property of this name does.
    /// </summary>
    [Category("Appearance"), DisplayName("Align vertex centers")]
    public bool AlignVertexCenters
    {
        get => _alignVertexCenters;
        set => SetProperty(ref _alignVertexCenters, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = _color ?? state.SeriesColor;
        var line = new LineStyle(
            color.WithOpacity(Opacity), _lineWidth, _dashStyle, LineCap.Butt, _lineJoin);

        // A stepped line draws an expanded path but keeps its samples: the markers below, the hit
        // test, and the bounds all still read Data, so only the ink between samples moves.
        IDataSeries path = Data;
        if (_steps != StepMode.None && Data.Count > 0)
        {
            (double[] stepX, double[] stepY) = StairSteps.Build(Data, _steps);
            path = new ArrayDataSeries(stepX, stepY);
        }

        SeriesRenderer.DrawLine(
            context, state, path, line, ref _dataBuffer, ref _pixelBuffer, _alignVertexCenters);

        if (_marker != MarkerType.None && Data.Count <= SeriesRenderer.MaxMarkerCount)
        {
            Color edge = _markerEdge ?? color;
            var marker = new MarkerStyle(_marker, _markerSize, _markerFill, edge);
            SeriesRenderer.DrawMarkers(
                context, state, Data, marker, edge, ref _pixelBuffer, _markerIndices);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var line = new LineStyle(color, _lineWidth, _dashStyle);
        MarkerStyle? marker = _marker != MarkerType.None
            ? new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _markerFill, _markerEdge ?? color)
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
