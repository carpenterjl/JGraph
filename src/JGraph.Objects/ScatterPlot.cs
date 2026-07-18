using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>A scatter plot drawing a marker at each data sample, with no connecting line.</summary>
public sealed class ScatterPlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _color;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 7;
    private Color? _fill;
    private double _edgeWidth = 1.0;

    private Point2D[] _pixelBuffer = new Point2D[16];

    public ScatterPlot(IDataSeries data)
        : base(data)
    {
        Name = "Scatter";
    }

    public ScatterPlot(double[] xs, double[] ys)
        : this(new ArrayDataSeries(xs, ys))
    {
    }

    /// <summary>Explicit marker edge color, or null to use the auto series color.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
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

    /// <summary>Marker interior color, or null for open markers.</summary>
    [Category("Appearance")]
    public Color? Fill
    {
        get => _fill;
        set => SetProperty(ref _fill, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Edge width")]
    public double EdgeWidth
    {
        get => _edgeWidth;
        set => SetProperty(ref _edgeWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = _color ?? state.SeriesColor;
        var marker = new MarkerStyle(_marker, _markerSize, _fill ?? color, color, _edgeWidth);
        SeriesRenderer.DrawMarkers(context, state, Data, marker, color, ref _pixelBuffer);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var marker = new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _fill ?? color, color, _edgeWidth);
        return new LegendKey(line: null, marker, swatch: null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
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
