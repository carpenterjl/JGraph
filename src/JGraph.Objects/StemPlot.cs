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
    private double _baseline;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerFill;

    public StemPlot(IDataSeries data)
        : base(data)
    {
        Name = "Stem";
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

    /// <summary>The value the stems rise from (usually 0).</summary>
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
    public Color? MarkerFill
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>Stems always reach the baseline, so it is part of the vertical extent.</summary>
    public override DataRange GetYDataBounds() => Data.YBounds.Include(_baseline);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        var stemStyle = new LineStyle(color, _lineWidth);
        ICoordinateMapper mapper = state.Mapper;

        Span<Point2D> tip = stackalloc Point2D[1];
        var marker = new MarkerStyle(_marker, _markerSize, _markerFill, color);

        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double y = Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            Point2D baseP = mapper.DataToPixel(x, _baseline);
            Point2D tipP = mapper.DataToPixel(x, y);
            context.DrawLine(baseP, tipP, stemStyle);

            if (_marker != MarkerType.None)
            {
                tip[0] = tipP;
                context.DrawMarkers(tip, marker, color);
            }
        }
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
