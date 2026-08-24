using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A stem plot in space (MATLAB <c>stem3</c>): a vertical stem rises from a baseline height to each
/// sample and is capped by a marker, exactly as <see cref="StemPlot"/> does in the plane, with the
/// foot of every stem free to sit anywhere on the floor rather than on one axis.
///
/// <para>
/// The three coordinate arrays are parallel and the same length. A non-finite entry in any of them
/// drops that sample, because a stem with no position has nowhere to stand — the same rule the other
/// spatial plots follow for a break in their data.
/// </para>
/// </summary>
public sealed class Stem3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem
{
    private double[] _x;
    private double[] _y;
    private double[] _z;

    private Color? _color;
    private double _lineWidth = 1.5;
    private DashStyle _dashStyle = DashStyle.Solid;
    private double _baseline;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerFill;
    private Color? _markerEdge;

    private Point2D[] _tips = new Point2D[16];

    public Stem3DPlot(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D stem", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        Name = "Stem3D";
    }

    /// <summary>The X coordinate of each stem's foot.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The Y coordinate of each stem's foot.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>The height each stem reaches.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Z => _z;

    /// <summary>Replaces the sample list.</summary>
    public void SetData(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D stem", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        Invalidate(InvalidationKind.Layout);
    }

    /// <summary>Explicit stem/marker color, or null to use the auto series color.</summary>
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

    /// <summary>The height the stems rise from (usually 0).</summary>
    [Category("Appearance")]
    public double Baseline
    {
        get => _baseline;
        set => SetProperty(ref _baseline, value, InvalidationKind.Layout);
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

    /// <summary>Marker outline color, or null to draw it in the stem's own colour.</summary>
    /// <remarks>M86, for the reason recorded on <see cref="Line3DPlot.MarkerEdgeColor"/>.</remarks>
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

    /// <summary>Stems always reach the baseline, so it is part of the vertical extent.</summary>
    public DataRange GetZDataBounds() => Vertices3D.Bounds(_z).Include(_baseline);

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);

        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        bool drawStems = _lineWidth > 0 && _dashStyle != DashStyle.None;
        var stem = new LineStyle(color, _lineWidth, _dashStyle);

        SeriesRenderer.EnsureCapacity(ref _tips, System.Math.Max(_x.Length, 1));
        int tips = 0;
        for (int i = 0; i < _x.Length; i++)
        {
            double x = _x[i], y = _y[i], z = _z[i];
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                continue;
            }

            Point2D tip = projection.ProjectPoint(x, y, z);
            if (drawStems)
            {
                context.DrawLine(projection.ProjectPoint(x, y, _baseline), tip, stem);
            }

            _tips[tips++] = tip;
        }

        if (_marker != MarkerType.None && tips > 0)
        {
            Color edge = _markerEdge ?? color;
            var marker = new MarkerStyle(
                _marker, _markerSize, _markerFill?.WithOpacity(Opacity), edge);
            context.DrawMarkers(_tips.AsSpan(0, tips), marker, edge);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// M87. A stem is its head and its stalk, and a click on either is a click on the stem — so each
    /// sample is tested twice, once at the marker and once along the line down to the baseline.
    /// </remarks>
    public override PlotHitResult? HitTest3D(
        Point2D pixelPoint, ISpatialMapper projector, double tolerancePixels)
    {
        ArgumentNullException.ThrowIfNull(projector);

        int best = -1;
        double bestDistance = double.PositiveInfinity;
        double bestDepth = double.NegativeInfinity;

        if (SpatialPicking.NearestPoint(pixelPoint, projector, X, Y, Z, tolerancePixels)
            is var (head, headDistance, headDepth))
        {
            best = head;
            bestDistance = headDistance;
            bestDepth = headDepth;
        }

        for (int i = 0; i < System.Math.Min(X.Count, System.Math.Min(Y.Count, Z.Count)); i++)
        {
            if (!SpatialPicking.IsDrawable(X[i], Y[i], Z[i]))
            {
                continue;
            }

            double[] xs = [X[i], X[i]];
            double[] ys = [Y[i], Y[i]];
            double[] zs = [_baseline, Z[i]];
            if (SpatialPicking.NearestSegment(pixelPoint, projector, xs, ys, zs, tolerancePixels)
                is var (_, distance, depth) && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
                bestDepth = depth;
            }
        }

        return best >= 0
            ? new PlotHitResult(this, new Point2D(X[best], Y[best]), bestDistance, best, bestDepth)
            : null;
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
