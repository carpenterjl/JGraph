using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A bar chart. Each sample is a bar centered at its X position (the category axis) rising from a
/// baseline to its Y value. Set <see cref="Horizontal"/> for a horizontal bar chart, in which the
/// roles of the two axes are swapped.
/// <para>
/// One series per column is how MATLAB draws a matrix, and the two ways of arranging those series
/// are both carried here rather than by a container: a grouped chart gives each series a
/// <see cref="GroupIndex"/> so the bars stand side by side inside one slot, and a stacked chart
/// gives each series a <see cref="LowerEdge"/> so it starts where the one beneath it ended. As with
/// <see cref="AreaPlot"/>, the stacked series keeps its own values — the floor is separate, so
/// <c>YData</c> still answers the column the script passed.
/// </para>
/// </summary>
public sealed class BarPlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _fillColor;
    private Color? _edgeColor;
    private double _edgeWidth = 1.0;
    private double _faceAlpha = 1.0;
    private DashStyle _dash = DashStyle.Solid;
    private double _barWidthFraction = 0.8;
    private readonly BaseLineModel _baseLine = new();
    private bool _horizontal;
    private double _edgeAlpha = 1.0;
    private Color[]? _colorData;
    private int _groupIndex;
    private int _groupCount = 1;
    private double _positionOffset;
    private double[]? _lowerEdge;

    public BarPlot(IDataSeries data)
        : base(data)
    {
        Name = "Bar";
        Adopt(_baseLine);
    }

    public BarPlot(double[] positions, double[] values)
        : this(new ArrayDataSeries(positions, values))
    {
    }

    /// <summary>Explicit bar fill color, or null to use the auto series color.</summary>
    [Category("Appearance"), DisplayName("Fill color")]
    public Color? FillColor
    {
        get => _fillColor;
        set => SetProperty(ref _fillColor, value, InvalidationKind.Render);
    }

    /// <summary>Explicit bar edge color, or null to derive it from the fill color.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Edge width")]
    public double EdgeWidth
    {
        get => _edgeWidth;
        set => SetProperty(ref _edgeWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>How opaque the bar faces are, in [0, 1]. The edges are unaffected.</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The dash pattern of the bar edges.</summary>
    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle Dash
    {
        get => _dash;
        set => SetProperty(ref _dash, value, InvalidationKind.Render);
    }

    /// <summary>Bar width as a fraction of the spacing between adjacent bars, in (0, 1].</summary>
    [Category("Appearance"), DisplayName("Bar width fraction")]
    public double BarWidthFraction
    {
        get => _barWidthFraction;
        set => SetProperty(ref _barWidthFraction, System.Math.Clamp(value, 0.01, 1.0), InvalidationKind.Layout);
    }

    /// <summary>The value bars rise from (usually 0), which is the baseline's own number.</summary>
    [Category("Appearance")]
    public double Baseline
    {
        get => _baseLine.BaseValue;
        set
        {
            _baseLine.BaseValue = value;
            Invalidate(InvalidationKind.Layout);
        }
    }

    /// <summary>The line the bars stand on, with its own colour, width and dash.</summary>
    [Browsable(false)]
    public BaseLineModel BaseLine => _baseLine;

    /// <summary>Whether that line is drawn. MATLAB draws it by default and so does this.</summary>
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

    /// <summary>How opaque the bar edges are, in [0, 1]. The faces are unaffected.</summary>
    [Category("Appearance"), DisplayName("Edge alpha")]
    public double EdgeAlpha
    {
        get => _edgeAlpha;
        set => SetProperty(ref _edgeAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>
    /// A colour for each bar, or null for one colour across the series. MATLAB calls it CData and
    /// takes either an n-by-3 table of colours or one index per bar into the colormap; both arrive
    /// here already resolved to colours, because deciding that is the caller's business and drawing
    /// them is this one's.
    /// </summary>
    [Browsable(false)]
    public Color[]? ColorData
    {
        get => _colorData;
        set => SetProperty(ref _colorData, value, InvalidationKind.Render);
    }

    /// <summary>When true, draws a horizontal bar chart (values extend along X).</summary>
    [Category("Appearance")]
    public bool Horizontal
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Which series of a grouped chart this is, counting from zero. Grouped bars share the slot a
    /// single series would fill on its own, so the position depends on the whole group.
    /// </summary>
    [Browsable(false)]
    public int GroupIndex
    {
        get => _groupIndex;
        set => SetProperty(ref _groupIndex, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>How many series share the slot; 1 for an ungrouped chart.</summary>
    [Browsable(false)]
    public int GroupCount
    {
        get => _groupCount;
        set => SetProperty(ref _groupCount, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <summary>
    /// How far the whole slot is shifted along the category axis, as a fraction of its width. Zero
    /// centers the bars on their position, which is what every modern bar chart does; a half shift
    /// makes them start there instead, which is what the legacy <c>histc</c> style means.
    /// </summary>
    [Browsable(false)]
    public double PositionOffset
    {
        get => _positionOffset;
        set => SetProperty(ref _positionOffset, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The per-bar floor this series is stacked on, or null to stand on <see cref="Baseline"/>.
    /// One entry per sample; a shorter array leaves the remaining bars on the baseline.
    /// </summary>
    [Browsable(false)]
    public double[]? LowerEdge
    {
        get => _lowerEdge;
        set => SetProperty(ref _lowerEdge, value, InvalidationKind.Layout);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The floor under bar <paramref name="i"/>.</summary>
    public double FloorAt(int i) =>
        _lowerEdge is { } edge && i < edge.Length && double.IsFinite(edge[i]) ? edge[i] : Baseline;

    /// <summary>The top of bar <paramref name="i"/>: absolute alone, relative when stacked.</summary>
    public double TopAt(int i) => _lowerEdge is null ? Data.GetY(i) : FloorAt(i) + Data.GetY(i);

    /// <summary>Where the center of bar <paramref name="i"/> sits along the category axis.</summary>
    public double CenterAt(int i) =>
        Data.GetX(i) + SlotShift() + ((_groupIndex - ((_groupCount - 1) / 2.0)) * 2 * HalfBarWidth());

    public override DataRange GetXDataBounds() =>
        _horizontal ? ValueBounds() : ExpandByHalfBar(Data.XBounds);

    public override DataRange GetYDataBounds() =>
        _horizontal ? ExpandByHalfBar(Data.XBounds) : ValueBounds();

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color series = _fillColor ?? state.SeriesColor;
        Color edge = _edgeColor ?? Color.Lerp(series, Colors.Black, 0.25);
        LineStyle? stroke = _edgeWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity * _edgeAlpha), _edgeWidth, _dash)
            : null;
        ICoordinateMapper mapper = state.Mapper;

        for (int i = 0; i < Data.Count; i++)
        {
            if (BarRect(i, mapper) is { } rect)
            {
                Color own = _colorData is { } colors && colors.Length > 0
                    ? colors[i % colors.Length]
                    : series;
                context.DrawRectangle(rect, stroke, own.WithOpacity(Opacity * _faceAlpha));
            }
        }

        DrawBaseLine(context, mapper, edge);
    }

    /// <summary>
    /// The line the bars stand on, drawn across whatever of the chart the bars cover. MATLAB draws
    /// it under every bar chart; before M77 this one carried the number and drew nothing.
    /// </summary>
    private void DrawBaseLine(IRenderContext context, ICoordinateMapper mapper, Color fallback)
    {
        if (!_baseLine.Visible || Data.Count == 0)
        {
            return;
        }

        DataRange along = ExpandByHalfBar(Data.XBounds);
        if (!along.IsValid)
        {
            return;
        }

        var pen = new LineStyle(
            (_baseLine.Color ?? fallback).WithOpacity(Opacity),
            System.Math.Max(_baseLine.LineWidth, 0.5),
            _baseLine.LineStyle);
        double value = Baseline;
        (Point2D from, Point2D to) = _horizontal
            ? (mapper.DataToPixel(value, along.Min), mapper.DataToPixel(value, along.Max))
            : (mapper.DataToPixel(along.Min, value), mapper.DataToPixel(along.Max, value));
        context.DrawLine(from, to, pen);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color face = _fillColor ?? seriesColor;
        Color edge = _edgeColor ?? Color.Lerp(face, Colors.Black, 0.25);
        return new LegendKey(
            line: null,
            marker: null,
            swatch: face.WithOpacity(_faceAlpha),
            outline: _edgeWidth > 0
                ? new LineStyle(edge.WithOpacity(_edgeAlpha), _edgeWidth, _dash)
                : null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        for (int i = 0; i < Data.Count; i++)
        {
            if (BarRect(i, mapper) is { } rect && rect.Contains(pixelPoint))
            {
                return new PlotHitResult(this, new Point2D(Data.GetX(i), Data.GetY(i)), 0, i);
            }
        }

        return null;
    }

    /// <summary>The pixel rectangle of bar <paramref name="i"/>, or null where it has no value.</summary>
    private Rect2D? BarRect(int i, ICoordinateMapper mapper)
    {
        double center = CenterAt(i);
        double top = TopAt(i);
        double floor = FloorAt(i);
        if (!double.IsFinite(center) || !double.IsFinite(top) || !double.IsFinite(floor))
        {
            return null;
        }

        double halfWidth = HalfBarWidth();
        return _horizontal
            ? Rect2D.FromCorners(
                mapper.DataToPixel(floor, center - halfWidth),
                mapper.DataToPixel(top, center + halfWidth))
            : Rect2D.FromCorners(
                mapper.DataToPixel(center - halfWidth, top),
                mapper.DataToPixel(center + halfWidth, floor));
    }

    /// <summary>Half the width of one bar — a share of the slot when the chart is grouped.</summary>
    private double HalfBarWidth() => SlotHalfWidth() / _groupCount;

    /// <summary>Half the width of the slot at each position, which the whole group fills.</summary>
    private double SlotHalfWidth() => 0.5 * _barWidthFraction * Spacing();

    /// <summary>What the bars span along the value axis, floors and baseline included.</summary>
    private DataRange ValueBounds()
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

        return range.IsEmpty ? DataRange.Empty : range.Include(Baseline);
    }

    /// <summary>How far the slot is shifted along the category axis, in data units.</summary>
    private double SlotShift() => _positionOffset * 2 * SlotHalfWidth();

    private DataRange ExpandByHalfBar(DataRange positions)
    {
        double half = SlotHalfWidth();
        double shift = SlotShift();
        return positions.IsEmpty
            ? positions
            : new DataRange(positions.Min - half + shift, positions.Max + half + shift);
    }

    /// <summary>The smallest positive gap between adjacent positions, or 1 when undefined.</summary>
    private double Spacing()
    {
        double spacing = double.MaxValue;
        for (int i = 1; i < Data.Count; i++)
        {
            double gap = System.Math.Abs(Data.GetX(i) - Data.GetX(i - 1));
            if (gap > 0 && gap < spacing)
            {
                spacing = gap;
            }
        }

        return spacing == double.MaxValue ? 1.0 : spacing;
    }
}
