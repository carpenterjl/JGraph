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
/// <para>
/// <see cref="XJitter"/> and <see cref="YJitter"/> spread points that share a position so that a
/// crowd of them can be counted by eye, which is what <c>swarmchart</c> switches on. That too is a
/// property rather than a plot type of its own: a swarm chart is a scatter drawn slightly to one
/// side, and everything else about it — the sizes, the colours, the legend — is unchanged.
/// </para>
/// </summary>
public sealed class ScatterPlot : XYPlot, IDrawable, ILegendItem, IColorMapped, IBubbleData
{
    private double[]? _alphaData;
    private AlphaMapping _alphaDataMapping = AlphaMapping.Scaled;
    private Color? _color;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 7;
    private Color? _fill;
    private double _edgeWidth = 1.0;

    private double[]? _sizeData;
    private double[]? _colorData;
    private bool _bubbleSizing;

    private readonly JitterChannel _xJitter = new();
    private readonly JitterChannel _yJitter = new();
    private IDataSeries? _jittered;
    private IDataSeries? _jitterSource;

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

    /// <summary>
    /// How points sharing an x are spread sideways so that all of them can be seen — MATLAB's
    /// <c>XJitter</c>, and what <c>swarmchart</c> turns on. The spread moves the markers only: the
    /// data a script reads back out of <c>XData</c> is the data it put in.
    /// </summary>
    [Category("Appearance"), DisplayName("X jitter")]
    public JitterStyle XJitter
    {
        get => _xJitter.Style;
        set
        {
            if (_xJitter.Style != value)
            {
                _xJitter.Style = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>How points sharing a y are spread up and down (MATLAB's <c>YJitter</c>).</summary>
    [Category("Appearance"), DisplayName("Y jitter")]
    public JitterStyle YJitter
    {
        get => _yJitter.Style;
        set
        {
            if (_yJitter.Style != value)
            {
                _yJitter.Style = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>
    /// How wide the sideways spread is allowed to be, in x. Reading it gives the width in force, which
    /// is the one that was set or — when none was — nine tenths of the gap between the two closest
    /// distinct x values, so neighbouring groups stay apart. Writing zero puts it back to that.
    /// </summary>
    [Category("Appearance"), DisplayName("X jitter width")]
    public double XJitterWidth
    {
        get => _xJitter.WidthFor(Column(x: true));
        set
        {
            if (_xJitter.Width != value)
            {
                _xJitter.Width = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>How tall the up-and-down spread is allowed to be, read and written as x's is.</summary>
    [Category("Appearance"), DisplayName("Y jitter width")]
    public double YJitterWidth
    {
        get => _yJitter.WidthFor(Column(x: false));
        set
        {
            if (_yJitter.Width != value)
            {
                _yJitter.Width = value;
                DiscardSpread();
            }
        }
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

    /// <summary>
    /// The x spread width that was <em>set</em>, or zero when none was — which is what a saved figure
    /// has to keep. Saving what <see cref="XJitterWidth"/> reads would turn a width that follows the
    /// data into one pinned to whatever the data happened to be on the day it was saved.
    /// </summary>
    [Browsable(false)]
    public double XJitterWidthOverride
    {
        get => _xJitter.Width;
        set => XJitterWidth = value;
    }

    /// <summary>The y spread width that was set, or zero when none was.</summary>
    [Browsable(false)]
    public double YJitterWidthOverride
    {
        get => _yJitter.Width;
        set => YJitterWidth = value;
    }

    /// <summary>How far along x each point is drawn from where its data puts it.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> XOffsets => Offsets(x: true);

    /// <summary>How far along y each point is drawn from where its data puts it.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> YOffsets => Offsets(x: false);

    /// <summary>
    /// The points as they are drawn: the data itself when nothing is being spread, and a spread copy
    /// of it when something is. Everything that asks where a marker <em>is</em> — the drawing, the hit
    /// test, and the bounds the axes fits itself to — goes through here, so a swarm cannot be drawn in
    /// one place and picked in another.
    /// </summary>
    private IDataSeries Displayed
    {
        get
        {
            if (!_xJitter.Spreads && !_yJitter.Spreads)
            {
                return Data;
            }

            // Replacing the data mints a new series object, so the cache is keyed on which one it was
            // built from rather than on a flag every setter would have to remember to clear.
            if (_jittered is null || !ReferenceEquals(_jitterSource, Data))
            {
                double[] xs = Column(x: true);
                double[] ys = Column(x: false);
                double[] alongX = _xJitter.Offsets(xs, ys);
                double[] alongY = _yJitter.Offsets(ys, xs);
                for (int i = 0; i < xs.Length; i++)
                {
                    xs[i] += alongX[i];
                    ys[i] += alongY[i];
                }

                _jitterSource = Data;
                _jittered = new ArrayDataSeries(xs, ys);
            }

            return _jittered;
        }
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override void AdoptAxesDefaults(AxesModel axes)
    {
        if (axes.ResolveColormap() is { } map)
        {
            Colormap = map;
        }

        if (axes.ColorLimits is { } limits)
        {
            AutoScaleColor = false;
            ColorMin = limits.Min;
            ColorMax = limits.Max;
        }
    }

    public override DataRange GetXDataBounds() => Displayed.XBounds;

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => Displayed.YBounds;

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
        IDataSeries drawn = Displayed;
        if (_sizeData is null && _colorData is null && _alphaData is null)
        {
            // One uniform style: the whole cloud goes out as a single call, as it did before there
            // were per-point channels at all.
            var marker = new MarkerStyle(_marker, _markerSize, _fill ?? color, color, _edgeWidth);
            SeriesRenderer.DrawMarkers(context, state, drawn, marker, color, ref _pixelBuffer);
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        (double min, double max) = ResolveColorRange();
        bool logColor = this.LogColorScale();
        AlphaLookup alpha = this.ResolveAlpha(AlphaBounds());
        Span<Point2D> one = stackalloc Point2D[1];
        for (int i = 0; i < drawn.Count; i++)
        {
            double x = drawn.GetX(i);
            double y = drawn.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            Color point = _colorData is { } values ? _colormap.Sample(values[i], min, max, logColor) : color;
            Color fill = _colorData is null ? _fill ?? color : point;

            // The per-point transparency, read the way the axes' alpha map says to read it — the
            // same AlphaResolver a surface and an image have used since M74.
            if (_alphaData is { } alphas && i < alphas.Length)
            {
                double opacity = OpacityOf(alphas[i], alpha);
                fill = fill.WithOpacity(opacity);
                point = point.WithOpacity(opacity);
            }

            one[0] = mapper.DataToPixel(x, y);
            context.DrawMarkers(one, new MarkerStyle(_marker, DiameterAt(i), fill, point, _edgeWidth), point);
        }
    }

    /// <summary>
    /// A transparency per point, or null for one opacity across the cloud. MATLAB reads these
    /// through the axes' alpha map exactly as it reads CData through the colormap, which is why
    /// <see cref="AlphaDataMapping"/> says how — and why 'none' means the numbers are opacities
    /// already.
    /// </summary>
    [Browsable(false)]
    public double[]? AlphaData
    {
        get => _alphaData;
        set => SetProperty(ref _alphaData, Checked(value, nameof(AlphaData)), InvalidationKind.Render);
    }

    /// <summary>Whether AlphaData is scaled through the axes' map, taken as it stands, or ignored.</summary>
    [Category("Appearance"), DisplayName("Alpha data mapping")]
    public AlphaMapping AlphaDataMapping
    {
        get => _alphaDataMapping;
        set => SetProperty(ref _alphaDataMapping, value, InvalidationKind.Render);
    }

    /// <summary>
    /// One alpha reading turned into an opacity, as <see cref="AlphaDataMapping"/> says to read it:
    /// as an opacity already, as an index into the alphamap, or stretched over the axes' alpha
    /// limits and looked up there.
    /// </summary>
    private double OpacityOf(double raw, AlphaLookup scaled)
    {
        if (!double.IsFinite(raw))
        {
            return 1;
        }

        switch (_alphaDataMapping)
        {
            case AlphaMapping.None:
                return System.Math.Clamp(raw, 0, 1);

            case AlphaMapping.Direct:
            {
                IReadOnlyList<double> map = Axes?.ResolveAlphamap() ?? AlphaSampler.DefaultMap;
                int index = System.Math.Clamp((int)System.Math.Round(raw) - 1, 0, map.Count - 1);
                return System.Math.Clamp(map[index], 0, 1);
            }

            default:
                return System.Math.Clamp(scaled.Sample(raw), 0, 1);
        }
    }

    /// <summary>The spread the alpha map is stretched over, which is the readings themselves.</summary>
    private DataRange AlphaBounds()
    {
        DataRange bounds = DataRange.Empty;
        foreach (double value in _alphaData ?? [])
        {
            if (double.IsFinite(value))
            {
                bounds = bounds.Include(value);
            }
        }

        return bounds;
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        var marker = new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _fill ?? color, color, _edgeWidth);
        return new LegendKey(line: null, marker, swatch: null);
    }

    /// <summary>One coordinate of every sample, as an array the spread can be worked out over.</summary>
    private double[] Column(bool x)
    {
        var values = new double[Data.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = x ? Data.GetX(i) : Data.GetY(i);
        }

        return values;
    }

    private double[] Offsets(bool x) => x
        ? _xJitter.Offsets(Column(x: true), Column(x: false))
        : _yJitter.Offsets(Column(x: false), Column(x: true));

    private void DiscardSpread()
    {
        _jittered = null;
        _jitterSource = null;
        Invalidate(InvalidationKind.Data);
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
        IDataSeries drawn = Displayed;
        if (SeriesHitTester.FindNearest(drawn, mapper, pixelPoint, pick) is not var (index, distance))
        {
            return null;
        }

        // The marker is found where it was drawn, but reported where its data is — a data tip on a
        // swarm chart should name the reading, not the place the spread happened to put it.
        return new PlotHitResult(this, new Point2D(Data.GetX(index), Data.GetY(index)), distance, index);
    }
}
