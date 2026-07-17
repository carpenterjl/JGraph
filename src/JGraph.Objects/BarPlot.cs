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
/// </summary>
public sealed class BarPlot : XYPlot, IDrawable, ILegendItem
{
    private Color? _fillColor;
    private Color? _edgeColor;
    private double _edgeWidth = 1.0;
    private double _barWidthFraction = 0.8;
    private double _baseline;
    private bool _horizontal;

    public BarPlot(IDataSeries data)
        : base(data)
    {
        Name = "Bar";
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

    /// <summary>Bar width as a fraction of the spacing between adjacent bars, in (0, 1].</summary>
    [Category("Appearance"), DisplayName("Bar width fraction")]
    public double BarWidthFraction
    {
        get => _barWidthFraction;
        set => SetProperty(ref _barWidthFraction, System.Math.Clamp(value, 0.01, 1.0), InvalidationKind.Layout);
    }

    /// <summary>The value bars rise from (usually 0).</summary>
    [Category("Appearance")]
    public double Baseline
    {
        get => _baseline;
        set => SetProperty(ref _baseline, value, InvalidationKind.Layout);
    }

    /// <summary>When true, draws a horizontal bar chart (values extend along X).</summary>
    [Category("Appearance")]
    public bool Horizontal
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Layout);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    public override DataRange GetXDataBounds() =>
        _horizontal ? Data.YBounds.Include(_baseline) : ExpandByHalfBar(Data.XBounds);

    public override DataRange GetYDataBounds() =>
        _horizontal ? ExpandByHalfBar(Data.XBounds) : Data.YBounds.Include(_baseline);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color fill = (_fillColor ?? state.SeriesColor).WithOpacity(Opacity);
        Color edge = _edgeColor ?? Color.Lerp(fill, Colors.Black, 0.25);
        LineStyle? stroke = _edgeWidth > 0 ? new LineStyle(edge, _edgeWidth) : null;
        double halfWidth = HalfBarWidth();
        ICoordinateMapper mapper = state.Mapper;

        for (int i = 0; i < Data.Count; i++)
        {
            double position = Data.GetX(i);
            double value = Data.GetY(i);
            if (!double.IsFinite(position) || !double.IsFinite(value))
            {
                continue;
            }

            Rect2D rect = _horizontal
                ? Rect2D.FromCorners(
                    mapper.DataToPixel(_baseline, position - halfWidth),
                    mapper.DataToPixel(value, position + halfWidth))
                : Rect2D.FromCorners(
                    mapper.DataToPixel(position - halfWidth, value),
                    mapper.DataToPixel(position + halfWidth, _baseline));

            context.DrawRectangle(rect, stroke, fill);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: _fillColor ?? seriesColor);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        double halfWidth = HalfBarWidth();
        for (int i = 0; i < Data.Count; i++)
        {
            double position = Data.GetX(i);
            double value = Data.GetY(i);
            if (!double.IsFinite(position) || !double.IsFinite(value))
            {
                continue;
            }

            Rect2D rect = _horizontal
                ? Rect2D.FromCorners(
                    mapper.DataToPixel(_baseline, position - halfWidth),
                    mapper.DataToPixel(value, position + halfWidth))
                : Rect2D.FromCorners(
                    mapper.DataToPixel(position - halfWidth, value),
                    mapper.DataToPixel(position + halfWidth, _baseline));

            if (rect.Contains(pixelPoint))
            {
                return new PlotHitResult(this, new Point2D(position, value), 0, i);
            }
        }

        return null;
    }

    private double HalfBarWidth() => 0.5 * _barWidthFraction * Spacing();

    private DataRange ExpandByHalfBar(DataRange positions)
    {
        double half = HalfBarWidth();
        return positions.IsEmpty ? positions : new DataRange(positions.Min - half, positions.Max + half);
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
