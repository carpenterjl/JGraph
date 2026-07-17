using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a <see cref="HistogramPlot"/> scales its bin heights.</summary>
public enum HistogramNormalization
{
    /// <summary>Bar height is the number of samples in the bin.</summary>
    Count,

    /// <summary>Bar height is the fraction of samples in the bin (heights sum to 1).</summary>
    Probability,

    /// <summary>Bar height is count / (N · bin width), so the total area is 1 (a probability density).</summary>
    Density,

    /// <summary>Bar height is the running sample count up to and including the bin.</summary>
    Cumulative,
}

/// <summary>
/// A histogram (MATLAB <c>histogram</c>): raw sample values are grouped into equal-width bins and drawn
/// as adjacent bars. The bin count and <see cref="Normalization"/> are editable; bins are recomputed
/// lazily when the inputs change.
/// </summary>
public sealed class HistogramPlot : PlotObject, IDrawable, ILegendItem
{
    private readonly double[] _values;
    private int _binCount = 10;
    private HistogramNormalization _normalization = HistogramNormalization.Count;
    private Color? _fillColor;
    private Color? _edgeColor;
    private double _edgeWidth = 1.0;

    private double[]? _binEdges;
    private double[]? _binHeights;

    /// <summary>Creates a histogram over the given raw sample values (the array is used directly).</summary>
    public HistogramPlot(double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
        Name = "Histogram";
    }

    /// <summary>The number of equal-width bins (at least 1).</summary>
    [Category("Appearance"), DisplayName("Bin count")]
    public int BinCount
    {
        get => _binCount;
        set
        {
            if (SetProperty(ref _binCount, System.Math.Max(1, value), InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
    }

    /// <summary>How bin heights are scaled.</summary>
    [Category("Appearance")]
    public HistogramNormalization Normalization
    {
        get => _normalization;
        set
        {
            if (SetProperty(ref _normalization, value, InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
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

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The number of raw samples.</summary>
    [Browsable(false)]
    public int SampleCount => _values.Length;

    /// <summary>The raw sample values (as supplied), exposed for serialization.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Values => _values;

    /// <summary>The computed bin edges (length <see cref="BinCount"/> + 1).</summary>
    [Browsable(false)]
    public IReadOnlyList<double> BinEdges
    {
        get
        {
            EnsureBins();
            return _binEdges!;
        }
    }

    /// <summary>The computed bin heights under the current <see cref="Normalization"/>.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> BinHeights
    {
        get
        {
            EnsureBins();
            return _binHeights!;
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds()
    {
        EnsureBins();
        return _binEdges!.Length >= 2
            ? new DataRange(_binEdges[0], _binEdges[^1])
            : DataRange.Empty;
    }

    /// <inheritdoc />
    public override DataRange GetYDataBounds()
    {
        EnsureBins();
        DataRange bounds = new DataRange(0, 0);
        foreach (double h in _binHeights!)
        {
            bounds = bounds.Include(h);
        }

        return bounds;
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        EnsureBins();
        if (_binEdges!.Length < 2)
        {
            return;
        }

        Color fill = (_fillColor ?? state.SeriesColor).WithOpacity(Opacity);
        Color edge = _edgeColor ?? Color.Lerp(fill, Colors.Black, 0.25);
        LineStyle? stroke = _edgeWidth > 0 ? new LineStyle(edge, _edgeWidth) : null;
        ICoordinateMapper mapper = state.Mapper;

        for (int b = 0; b < _binHeights!.Length; b++)
        {
            double height = _binHeights[b];
            if (height == 0)
            {
                continue;
            }

            Rect2D rect = Rect2D.FromCorners(
                mapper.DataToPixel(_binEdges[b], 0),
                mapper.DataToPixel(_binEdges[b + 1], height));
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

        EnsureBins();
        for (int b = 0; b < _binHeights!.Length; b++)
        {
            double height = _binHeights[b];
            Rect2D rect = Rect2D.FromCorners(
                mapper.DataToPixel(_binEdges![b], 0),
                mapper.DataToPixel(_binEdges[b + 1], height));
            if (rect.Contains(pixelPoint))
            {
                double center = (_binEdges[b] + _binEdges[b + 1]) / 2.0;
                return new PlotHitResult(this, new Point2D(center, height), 0, b);
            }
        }

        return null;
    }

    private void EnsureBins()
    {
        if (_binEdges is not null && _binHeights is not null)
        {
            return;
        }

        DataRange bounds = DataRange.Empty;
        foreach (double v in _values)
        {
            if (double.IsFinite(v))
            {
                bounds = bounds.Include(v);
            }
        }

        int bins = System.Math.Max(1, _binCount);
        var edges = new double[bins + 1];
        var counts = new double[bins];

        DataRange valid = bounds.IsValid ? bounds : bounds.EnsureValid();
        double min = valid.Min;
        double max = valid.Max;
        double width = (max - min) / bins;
        for (int i = 0; i <= bins; i++)
        {
            edges[i] = min + (i * width);
        }

        int total = 0;
        foreach (double v in _values)
        {
            if (!double.IsFinite(v))
            {
                continue;
            }

            int index = (int)((v - min) / width);
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= bins)
            {
                index = bins - 1; // include the right edge in the last bin
            }

            counts[index]++;
            total++;
        }

        _binEdges = edges;
        _binHeights = Normalize(counts, total, width);
    }

    private double[] Normalize(double[] counts, int total, double binWidth)
    {
        var heights = new double[counts.Length];
        double safeTotal = total > 0 ? total : 1;
        switch (_normalization)
        {
            case HistogramNormalization.Probability:
                for (int i = 0; i < counts.Length; i++)
                {
                    heights[i] = counts[i] / safeTotal;
                }

                break;

            case HistogramNormalization.Density:
                double denom = safeTotal * (binWidth == 0 ? 1 : binWidth);
                for (int i = 0; i < counts.Length; i++)
                {
                    heights[i] = counts[i] / denom;
                }

                break;

            case HistogramNormalization.Cumulative:
                double running = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    running += counts[i];
                    heights[i] = running;
                }

                break;

            default: // Count
                Array.Copy(counts, heights, counts.Length);
                break;
        }

        return heights;
    }

    /// <summary>Discards the cached bins so they are recomputed on next use.</summary>
    private void DiscardBins()
    {
        _binEdges = null;
        _binHeights = null;
    }
}
