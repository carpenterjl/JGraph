using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A scatter plot drawing a marker at each data sample, with no connecting line.
/// <para>
/// Two optional per-point channels follow MATLAB, as they do on <see cref="Scatter3DPlot"/>:
/// <see cref="ColorData"/> is a value per point taken through <see cref="Colormap"/>, and
/// <see cref="SizeData"/> is a size per point. What a size <em>means</em> depends on the verb that
/// drew the plot, which is what <see cref="BubbleSizing"/> records: <c>scatter</c>'s sizes are marker
/// areas in points squared, while <c>bubblechart</c>'s are data values mapped through the axes'
/// <see cref="BubbleScale"/> — the same array, read two ways, so the plot has to remember which.
/// </para>
/// </summary>
public sealed class ScatterPlot : XYPlot, IDrawable, ILegendItem, IColorMapped, IBubbleData
{
    private Color? _color;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 7;
    private Color? _fill;
    private double _edgeWidth = 1.0;

    private double[]? _sizeData;
    private double[]? _colorData;
    private bool _bubbleSizing;

    private Colormap _colormap = Colormap.Parula;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;

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

    /// <summary>
    /// A size per point, or null for a uniform <see cref="MarkerSize"/>. Read as a marker area in
    /// points squared, or as a bubble value when <see cref="BubbleSizing"/> is set. Must have one
    /// entry per sample.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<double>? SizeData
    {
        get => _sizeData;
        set
        {
            _sizeData = Checked(value, nameof(SizeData));
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// Per-point values colored through <see cref="Colormap"/> (MATLAB's <c>c</c>), or null to draw
    /// every marker in the series color. Must have one entry per sample.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<double>? ColorData
    {
        get => _colorData;
        set
        {
            _colorData = Checked(value, nameof(ColorData));
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// Whether <see cref="SizeData"/> is read as bubble values against the axes' scale rather than as
    /// marker areas. Set by <c>bubblechart</c> and by nothing else.
    /// </summary>
    [Category("Appearance"), DisplayName("Bubble sizing")]
    public bool BubbleSizing
    {
        get => _bubbleSizing;
        set => SetProperty(ref _bubbleSizing, value, InvalidationKind.Render);
    }

    /// <summary>The colormap <see cref="ColorData"/> is sampled through.</summary>
    [Category("Appearance")]
    public Colormap Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render);
    }

    /// <summary>Whether the color range is taken from the data rather than from ColorMin/ColorMax.</summary>
    [Category("Appearance"), DisplayName("Auto-scale color")]
    public bool AutoScaleColor
    {
        get => _autoScaleColor;
        set => SetProperty(ref _autoScaleColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Color min")]
    public double ColorMin
    {
        get => _colorMin;
        set => SetProperty(ref _colorMin, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Color max")]
    public double ColorMax
    {
        get => _colorMax;
        set => SetProperty(ref _colorMax, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public bool HasMappedData => _colorData is not null;

    /// <inheritdoc />
    public (double Min, double Max) ColorRange => ResolveColorRange();

    /// <inheritdoc />
    Color? IBubbleData.BubbleFaceColor => _fill ?? _color;

    /// <summary>
    /// The scale this plot's bubbles are drawn against: the axes' when it is in one, and one read off
    /// its own sizes when it is not, so a chart measured before it is added still answers sensibly.
    /// </summary>
    [Browsable(false)]
    public BubbleScale BubbleScale => Axes?.BubbleScale ?? Core.Model.BubbleScale.ForValues(_sizeData);

    /// <summary>The diameter each point is drawn at, in points — what the legend has to reproduce.</summary>
    public double DiameterAt(int index)
    {
        if (_sizeData is not { } sizes || index < 0 || index >= sizes.Length)
        {
            return _markerSize;
        }

        return _bubbleSizing
            ? BubbleScale.DiameterFor(sizes[index])
            : System.Math.Sqrt(System.Math.Max(0, sizes[index]));
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        Color color = _color ?? state.SeriesColor;
        if (_sizeData is null && _colorData is null)
        {
            // One uniform style: the whole cloud goes out as a single call, as it did before there
            // were per-point channels at all.
            var marker = new MarkerStyle(_marker, _markerSize, _fill ?? color, color, _edgeWidth);
            SeriesRenderer.DrawMarkers(context, state, Data, marker, color, ref _pixelBuffer);
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        (double min, double max) = ResolveColorRange();
        Span<Point2D> one = stackalloc Point2D[1];
        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double y = Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            Color point = _colorData is { } values ? _colormap.Sample(values[i], min, max) : color;
            Color fill = _colorData is null ? _fill ?? color : point;
            one[0] = mapper.DataToPixel(x, y);
            context.DrawMarkers(one, new MarkerStyle(_marker, DiameterAt(i), fill, point, _edgeWidth), point);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var marker = new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _fill ?? color, color, _edgeWidth);
        return new LegendKey(line: null, marker, swatch: null);
    }

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor || _colorData is not { } values)
        {
            return (_colorMin, _colorMax);
        }

        DataRange bounds = DataRange.Empty;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                bounds = bounds.Include(value);
            }
        }

        return bounds.IsValid ? (bounds.Min, bounds.Max) : (_colorMin, _colorMax);
    }

    private double[]? Checked(IReadOnlyList<double>? values, string what)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count != Data.Count)
        {
            throw new ArgumentException(
                $"{what} needs one entry per point ({Data.Count}), but got {values.Count}.", nameof(values));
        }

        return values.ToArray();
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        // A bubble is picked anywhere inside it, so the tolerance has to grow with the biggest one —
        // otherwise a click in the middle of a large bubble lands on nothing.
        double widest = _markerSize;
        for (int i = 0; _sizeData is not null && i < _sizeData.Length; i++)
        {
            widest = System.Math.Max(widest, DiameterAt(i));
        }

        double pick = System.Math.Max(tolerancePixels, widest);
        if (SeriesHitTester.FindNearest(Data, mapper, pixelPoint, pick) is not var (index, distance))
        {
            return null;
        }

        return new PlotHitResult(this, new Point2D(Data.GetX(index), Data.GetY(index)), distance, index);
    }
}
