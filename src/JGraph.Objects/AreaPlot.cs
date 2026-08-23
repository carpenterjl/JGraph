using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A filled band between a floor and a series (MATLAB <c>area</c>) — a line plot with the region
/// under it shaded, and the shape a stacked composition is built from.
/// <para>
/// The floor is <see cref="BaseValue"/> for a band standing on its own, and <see cref="LowerEdge"/>
/// for one stacked on the bands below it. The two cases read the value differently, which is
/// MATLAB's own rule and worth stating plainly: an unstacked band runs from the base value up to its
/// value, and a stacked one runs from the band beneath it up by its value. That is what keeps
/// <c>YData</c> answering the column a script actually passed rather than a running total it never
/// wrote.
/// </para>
/// </summary>
public sealed class AreaPlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _faceColor;
    private Color? _edgeColor;
    private double _faceAlpha = 1.0;
    private double _lineWidth = 1.0;
    private DashStyle _dash = DashStyle.Solid;
    private readonly BaseLineModel _baseLine = new();
    private double _edgeAlpha = 1.0;
    private bool _alignVertexCenters;
    private double[]? _lowerEdge;

    public AreaPlot(IDataSeries data)
        : base(data)
    {
        Name = "Area";
        Adopt(_baseLine);
    }

    public AreaPlot(double[] xs, double[] ys)
        : this(new ArrayDataSeries(xs, ys))
    {
    }

    /// <summary>Explicit fill color, or null to use the auto series color.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>Explicit outline color, or null to darken the fill.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    /// <summary>How opaque the fill is, in [0, 1]. The outline is unaffected.</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
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

    /// <summary>The floor an unstacked band stands on (usually 0).</summary>
    [Category("Appearance"), DisplayName("Base value")]
    public double BaseValue
    {
        get => _baseLine.BaseValue;
        set
        {
            _baseLine.BaseValue = value;
            Invalidate(InvalidationKind.Layout);
        }
    }

    /// <summary>Whether the line along the floor is drawn.</summary>
    [Category("Appearance"), DisplayName("Show base line")]
    public bool ShowBaseLine
    {
        get => _baseLine.Visible;
        set
        {
            _baseLine.Visible = value;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// The per-point floor this band is stacked on, or null to stand on <see cref="BaseValue"/>.
    /// One entry per sample; a shorter array leaves the remaining samples on the base value.
    /// </summary>
    [Browsable(false)]
    public double[]? LowerEdge
    {
        get => _lowerEdge;
        set => SetProperty(ref _lowerEdge, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Whether the band's vertices are snapped to pixel centres, which is what keeps a thin outline
    /// crisp rather than spread across two rows of pixels.
    /// </summary>
    [Category("Appearance"), DisplayName("Align vertex centers")]
    public bool AlignVertexCenters
    {
        get => _alignVertexCenters;
        set => SetProperty(ref _alignVertexCenters, value, InvalidationKind.Render);
    }

    /// <summary>How opaque the outline is, in [0, 1]. The fill has its own.</summary>
    [Category("Appearance"), DisplayName("Edge alpha")]
    public double EdgeAlpha
    {
        get => _edgeAlpha;
        set => SetProperty(ref _edgeAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>
    /// The line the band stands on. An area has drawn one since M6; what it did not have was a
    /// handle on it, so the line could not be coloured, widened or dashed on its own.
    /// </summary>
    [Browsable(false)]
    public BaseLineModel BaseLine => _baseLine;

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The floor under sample <paramref name="i"/>.</summary>
    public double FloorAt(int i) =>
        _lowerEdge is { } edge && i < edge.Length && double.IsFinite(edge[i]) ? edge[i] : BaseValue;

    /// <summary>The top of the band at sample <paramref name="i"/>: absolute alone, relative when stacked.</summary>
    public double TopAt(int i) => _lowerEdge is null ? Data.GetY(i) : FloorAt(i) + Data.GetY(i);

    public override DataRange GetYDataBounds()
    {
        DataRange range = DataRange.Empty;
        for (int i = 0; i < Data.Count; i++)
        {
            double top = TopAt(i);
            if (!double.IsFinite(top))
            {
                continue;
            }

            range = range.Include(FloorAt(i)).Include(top);
        }

        // A band always shows its own floor even where it has no samples above it.
        return range.IsEmpty ? DataRange.Empty : range.Include(BaseValue);
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color face = (_faceColor ?? state.SeriesColor).WithOpacity(Opacity * _faceAlpha);
        Color edge = _edgeColor ?? Color.Lerp(_faceColor ?? state.SeriesColor, Colors.Black, 0.25);
        LineStyle? stroke = _lineWidth > 0 ? new LineStyle(edge.WithOpacity(Opacity * _edgeAlpha), _lineWidth, _dash) : null;
        ICoordinateMapper mapper = state.Mapper;

        // A gap in the data breaks the band rather than bridging it, the way a line plot's gaps do,
        // so each unbroken run of samples is its own polygon.
        var polygon = new List<Point2D>();
        var floor = new List<Point2D>();

        void Flush()
        {
            if (polygon.Count >= 2)
            {
                for (int i = floor.Count - 1; i >= 0; i--)
                {
                    polygon.Add(floor[i]);
                }

                context.DrawPolygon(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(polygon), stroke, face);
            }

            polygon.Clear();
            floor.Clear();
        }

        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double top = TopAt(i);
            double bottom = FloorAt(i);
            if (!double.IsFinite(x) || !double.IsFinite(top) || !double.IsFinite(bottom))
            {
                Flush();
                continue;
            }

            polygon.Add(Snapped(mapper.DataToPixel(x, top)));
            floor.Add(Snapped(mapper.DataToPixel(x, bottom)));
        }

        Flush();

        if (ShowBaseLine && Data.Count > 0)
        {
            DrawBaseLine(context, mapper, edge);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color face = _faceColor ?? seriesColor;
        Color edge = _edgeColor ?? Color.Lerp(face, Colors.Black, 0.25);
        return new LegendKey(
            line: null,
            marker: null,
            swatch: face.WithOpacity(_faceAlpha),
            outline: _lineWidth > 0
                ? new LineStyle(edge.WithOpacity(_edgeAlpha), _lineWidth, _dash)
                : null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        // Inside the band counts as a hit, not just near its outline: a filled shape is clicked on
        // its face. The nearest sample names the point, which is what a data tip wants to show.
        for (int i = 1; i < Data.Count; i++)
        {
            double left = Data.GetX(i - 1);
            double right = Data.GetX(i);
            if (!double.IsFinite(left) || !double.IsFinite(right))
            {
                continue;
            }

            Point2D a = mapper.DataToPixel(left, TopAt(i - 1));
            Point2D b = mapper.DataToPixel(right, TopAt(i));
            Point2D floorA = mapper.DataToPixel(left, FloorAt(i - 1));
            Point2D floorB = mapper.DataToPixel(right, FloorAt(i));
            double minX = System.Math.Min(a.X, b.X) - tolerancePixels;
            double maxX = System.Math.Max(a.X, b.X) + tolerancePixels;
            if (pixelPoint.X < minX || pixelPoint.X > maxX)
            {
                continue;
            }

            double t = System.Math.Abs(b.X - a.X) < 1e-9 ? 0 : (pixelPoint.X - a.X) / (b.X - a.X);
            double topY = a.Y + (t * (b.Y - a.Y));
            double bottomY = floorA.Y + (t * (floorB.Y - floorA.Y));
            if (pixelPoint.Y < System.Math.Min(topY, bottomY) - tolerancePixels
                || pixelPoint.Y > System.Math.Max(topY, bottomY) + tolerancePixels)
            {
                continue;
            }

            int nearest = t < 0.5 ? i - 1 : i;
            return new PlotHitResult(this, new Point2D(Data.GetX(nearest), Data.GetY(nearest)), 0, nearest);
        }

        return null;
    }

    /// <summary>A vertex on a pixel centre when the band was told to align them, and as it is otherwise.</summary>
    private Point2D Snapped(Point2D point) => _alignVertexCenters && point.IsFinite
        ? new Point2D(System.Math.Floor(point.X) + 0.5, System.Math.Floor(point.Y) + 0.5)
        : point;

    private void DrawBaseLine(IRenderContext context, ICoordinateMapper mapper, Color edge)
    {
        double left = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            if (!double.IsFinite(x))
            {
                continue;
            }

            left = System.Math.Min(left, x);
            right = System.Math.Max(right, x);
        }

        if (!double.IsFinite(left) || !double.IsFinite(right))
        {
            return;
        }

        // The line has its own colour, width and dash since M77; where it has none of its own it
        // is drawn in the band's edge ink at the width it always was.
        context.DrawLine(
            mapper.DataToPixel(left, BaseValue),
            mapper.DataToPixel(right, BaseValue),
            new LineStyle(
                (_baseLine.Color ?? edge).WithOpacity(Opacity),
                System.Math.Max(_baseLine.Color is null ? _lineWidth : _baseLine.LineWidth, 0.5),
                _baseLine.LineStyle));
    }
}
