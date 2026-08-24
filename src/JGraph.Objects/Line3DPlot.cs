using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A polyline through points in space, with optional markers — MATLAB's <c>plot3</c> and the 3D form
/// of <c>line</c>. The three coordinate arrays are parallel and the same length; a non-finite entry in
/// any of them breaks the line there, exactly as it does for a 2D series.
///
/// The whole polyline is drawn as one stroke over whatever was drawn before it. There is no depth
/// buffer in this pipeline — 3D content is painted plot by plot in draw order — so a line added after
/// a surface stays in front of it even where it passes behind. That is a limitation of the painter's
/// algorithm, not of this plot, and it matters only when a line and a surface interpenetrate.
/// </summary>
public sealed class Line3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem
{
    private double[] _x;
    private double[] _y;
    private double[] _z;

    private Color? _color;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private MarkerType _marker = MarkerType.None;
    private double _markerSize = 6;
    private Color? _markerFill;
    private Color? _markerEdge;

    private Point2D[] _pixels = new Point2D[16];

    public Line3DPlot(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D line", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        Name = "Line3D";
    }

    /// <summary>The X coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The Y coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>The Z coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Z => _z;

    /// <summary>Replaces the point list.</summary>
    public void SetData(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D line", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        Invalidate(InvalidationKind.Layout);
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
    public Color? MarkerFaceColor
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <summary>Marker outline color, or null to draw it in the line's own colour.</summary>
    /// <remarks>
    /// M86. <c>plot3</c> makes a <c>Line</c> in MATLAB, the same class <c>plot</c> makes, so this had
    /// been missing rather than deliberately absent: a marker in space could be filled but not
    /// outlined, and the name a script would reach for did not resolve at all.
    /// </remarks>
    [Category("Appearance"), DisplayName("Marker edge")]
    public Color? MarkerEdgeColor
    {
        get => _markerEdge;
        set => SetProperty(ref _markerEdge, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => Vertices3D.Bounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => Vertices3D.Bounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds() => Vertices3D.Bounds(_z);

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);

        int count = _x.Length;
        if (count == 0)
        {
            return;
        }

        SeriesRenderer.EnsureCapacity(ref _pixels, count);
        for (int i = 0; i < count; i++)
        {
            double x = _x[i], y = _y[i], z = _z[i];
            _pixels[i] = double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
                ? projection.ProjectPoint(x, y, z)
                : Point2D.NaN;
        }

        Color color = _color ?? state.SeriesColor;
        if (_lineWidth > 0 && _dashStyle != DashStyle.None)
        {
            var line = new LineStyle(color.WithOpacity(Opacity), _lineWidth, _dashStyle);
            SeriesRenderer.DrawWithGaps(context, _pixels, count, line);
        }

        if (_marker != MarkerType.None)
        {
            // The gaps are already NaN, and markers must not be drawn at them, so the finite points
            // are compacted forward in place over a buffer that is about to be rebuilt anyway.
            int m = 0;
            for (int i = 0; i < count; i++)
            {
                if (_pixels[i].IsFinite)
                {
                    _pixels[m++] = _pixels[i];
                }
            }

            Color edge = (_markerEdge ?? color).WithOpacity(Opacity);
            var marker = new MarkerStyle(
                _marker, _markerSize, _markerFill?.WithOpacity(Opacity), edge);
            context.DrawMarkers(_pixels.AsSpan(0, m), marker, edge);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// M87. Picked against the segments rather than the vertices, so the middle of a long straight
    /// run is as clickable as its ends — the same rule the flat line uses, applied to the polyline
    /// the camera actually drew.
    /// </remarks>
    public override PlotHitResult? HitTest3D(
        Point2D pixelPoint, ISpatialMapper projector, double tolerancePixels)
    {
        ArgumentNullException.ThrowIfNull(projector);
        if (SpatialPicking.NearestSegment(pixelPoint, projector, X, Y, Z, tolerancePixels)
            is not var (index, distance, depth))
        {
            return null;
        }

        return new PlotHitResult(this, new Point2D(X[index], Y[index]), distance, index, depth);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        LineStyle? line = _dashStyle != DashStyle.None
            ? new LineStyle(color, _lineWidth, _dashStyle)
            : null;
        MarkerStyle? marker = _marker != MarkerType.None
            ? new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _markerFill, _markerEdge ?? color)
            : null;
        return new LegendKey(line, marker, swatch: null);
    }
}
