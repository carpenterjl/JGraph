using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A stem plot (MATLAB <c>stem</c>): a vertical stem rises from a baseline to each sample, capped by a
/// marker. Useful for discrete/sampled sequences where a connecting line would imply continuity.
/// </summary>
public sealed class StemPlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _color;
    private double _lineWidth = 1.5;
    private readonly BaseLineModel _baseLine = new();
    private DashStyle _dashStyle = DashStyle.Solid;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerFill;
    private Color? _markerEdge;

    public StemPlot(IDataSeries data)
        : base(data)
    {
        Name = "Stem";
        Adopt(_baseLine);
    }

    public StemPlot(double[] xs, double[] ys)
        : this(new ArrayDataSeries(xs, ys))
    {
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

    /// <summary>The value the stems rise from (usually 0) — the baseline's own number.</summary>
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

    /// <summary>The line the stems stand on, with its own colour, width and dash.</summary>
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

    /// <summary>How the stems are dashed.</summary>
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
    public Color? MarkerFill
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <summary>Marker outline color, or null to draw it in the stem's own colour.</summary>
    [Category("Appearance"), DisplayName("Marker edge")]
    public Color? MarkerEdge
    {
        get => _markerEdge;
        set => SetProperty(ref _markerEdge, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>Stems always reach the baseline, so it is part of the vertical extent.</summary>
    public override DataRange GetYDataBounds() => Data.YBounds.Include(Baseline);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        Color markerEdge = (_markerEdge ?? _color ?? state.SeriesColor).WithOpacity(Opacity);
        var stemStyle = new LineStyle(color, _lineWidth, _dashStyle);
        ICoordinateMapper mapper = state.Mapper;

        Span<Point2D> tip = stackalloc Point2D[1];
        var marker = new MarkerStyle(_marker, _markerSize, _markerFill, markerEdge);

        double floor = Baseline;
        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double y = Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            Point2D baseP = mapper.DataToPixel(x, floor);
            Point2D tipP = mapper.DataToPixel(x, y);
            context.DrawLine(baseP, tipP, stemStyle);

            if (_marker != MarkerType.None)
            {
                tip[0] = tipP;
                context.DrawMarkers(tip, marker, markerEdge);
            }
        }

        DrawBaseLine(context, mapper, color);
    }

    /// <summary>
    /// The line the stems stand on, drawn across the positions they occupy. MATLAB draws it under
    /// every stem chart; before M77 this one carried the number and drew nothing.
    /// </summary>
    private void DrawBaseLine(IRenderContext context, ICoordinateMapper mapper, Color fallback)
    {
        if (!_baseLine.Visible || Data.Count == 0)
        {
            return;
        }

        DataRange along = Data.XBounds;
        if (!along.IsValid)
        {
            return;
        }

        var pen = new LineStyle(
            (_baseLine.Color ?? fallback).WithOpacity(Opacity),
            System.Math.Max(_baseLine.LineWidth, 0.5),
            _baseLine.LineStyle);
        context.DrawLine(
            mapper.DataToPixel(along.Min, Baseline),
            mapper.DataToPixel(along.Max, Baseline),
            pen);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var line = new LineStyle(color, _lineWidth);
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

        double pick = System.Math.Max(tolerancePixels, _markerSize);
        if (SeriesHitTester.FindNearest(Data, mapper, pixelPoint, pick) is not var (index, distance))
        {
            return null;
        }

        return new PlotHitResult(this, new Point2D(Data.GetX(index), Data.GetY(index)), distance, index);
    }
}
