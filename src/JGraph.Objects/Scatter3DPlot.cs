using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// Markers at points in space — MATLAB's <c>scatter3</c>. Points are drawn back to front by their
/// projected depth, so a near marker covers a far one, which is the whole reason this is not just a
/// <see cref="Line3DPlot"/> with its line switched off.
///
/// Two optional per-point channels follow MATLAB: <see cref="SizeData"/> is an <em>area</em> in
/// points squared (so a marker is drawn at diameter sqrt(s), matching how <c>scatter3(x, y, z, s)</c>
/// scales), and <see cref="ColorData"/> is a value per point mapped through <see cref="Colormap"/>.
/// </summary>
public sealed class Scatter3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem, IColorMapped, IBubbleData
{
    private double[] _x;
    private double[] _y;
    private double[] _z;
    private double[]? _sizeData;
    private double[]? _colorData;

    private Color? _color;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 7;
    private bool _filled;
    private double _edgeWidth = 1.0;

    private Colormap _colormap = Colormap.Parula;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;

    private bool _bubbleSizing;
    private readonly JitterChannel _xJitter = new();
    private readonly JitterChannel _yJitter = new();
    private readonly JitterChannel _zJitter = new();
    private double[]? _drawnX;
    private double[]? _drawnY;
    private double[]? _drawnZ;

    private Point2D[] _pixels = new Point2D[16];
    private Point2D[] _sorted = new Point2D[16];
    private double[] _depths = new double[16];
    private int[] _order = new int[16];

    public Scatter3DPlot(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D scatter", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        Name = "Scatter3D";
    }

    /// <summary>The X coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The Y coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>The Z coordinate of each point.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Z => _z;

    /// <summary>Replaces the point list.</summary>
    public void SetData(double[] x, double[] y, double[] z)
    {
        Vertices3D.Validate("A 3D scatter", x, y, z);
        _x = x;
        _y = y;
        _z = z;
        DiscardSpread();
        Invalidate(InvalidationKind.Layout);
    }

    /// <summary>
    /// Per-point marker area in points squared (MATLAB's <c>s</c>), or null for a uniform
    /// <see cref="MarkerSize"/>. Must have one entry per point.
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
    /// every marker in the series color. Must have one entry per point.
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

    /// <summary>Explicit marker color, or null to use the auto series color.</summary>
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

    /// <summary>Whether markers are filled with their color (MATLAB's <c>'filled'</c>) or left open.</summary>
    [Category("Appearance")]
    public bool Filled
    {
        get => _filled;
        set => SetProperty(ref _filled, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Edge width")]
    public double EdgeWidth
    {
        get => _edgeWidth;
        set => SetProperty(ref _edgeWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>
    /// Whether <see cref="SizeData"/> means bubble values read against the axes' scale rather than
    /// MATLAB <c>scatter3</c>'s marker areas in points squared. Set by <c>bubblechart3</c> and by
    /// nothing else — the same array, read the other way.
    /// </summary>
    [Category("Appearance"), DisplayName("Bubble sizing")]
    public bool BubbleSizing
    {
        get => _bubbleSizing;
        set => SetProperty(ref _bubbleSizing, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The scale this plot's bubbles are drawn against: the axes' when it is in one, and one read off
    /// its own sizes when it is not, so a chart measured before it is added still answers sensibly.
    /// </summary>
    [Browsable(false)]
    public BubbleScale BubbleScale => Axes?.BubbleScale ?? Core.Model.BubbleScale.ForValues(_sizeData);

    /// <summary>
    /// How points sharing an x are spread along it so that all of them can be seen — MATLAB's
    /// <c>XJitter</c>, and what <c>swarmchart3</c> turns on. As in the flat chart, the spread moves
    /// the markers and leaves the data alone.
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

    /// <summary>How points sharing a y are spread along it (MATLAB's <c>YJitter</c>).</summary>
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

    /// <summary>How points sharing a z are spread along it (MATLAB's <c>ZJitter</c>).</summary>
    [Category("Appearance"), DisplayName("Z jitter")]
    public JitterStyle ZJitter
    {
        get => _zJitter.Style;
        set
        {
            if (_zJitter.Style != value)
            {
                _zJitter.Style = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>
    /// How wide the spread along x is allowed to be. Reading it gives the width in force — the one
    /// that was set, or nine tenths of the gap between the two closest distinct x values; writing zero
    /// puts it back to being worked out that way.
    /// </summary>
    [Category("Appearance"), DisplayName("X jitter width")]
    public double XJitterWidth
    {
        get => _xJitter.WidthFor(_x);
        set
        {
            if (_xJitter.Width != value)
            {
                _xJitter.Width = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>How wide the spread along y is allowed to be, read and written as x's is.</summary>
    [Category("Appearance"), DisplayName("Y jitter width")]
    public double YJitterWidth
    {
        get => _yJitter.WidthFor(_y);
        set
        {
            if (_yJitter.Width != value)
            {
                _yJitter.Width = value;
                DiscardSpread();
            }
        }
    }

    /// <summary>How wide the spread along z is allowed to be, read and written as x's is.</summary>
    [Category("Appearance"), DisplayName("Z jitter width")]
    public double ZJitterWidth
    {
        get => _zJitter.WidthFor(_z);
        set
        {
            if (_zJitter.Width != value)
            {
                _zJitter.Width = value;
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
    Color? IBubbleData.BubbleFaceColor => _filled ? _color : null;

    /// <inheritdoc />
    public override DataRange GetXDataBounds()
    {
        EnsureSpread();
        return Vertices3D.Bounds(_drawnX!);
    }

    /// <inheritdoc />
    public override DataRange GetYDataBounds()
    {
        EnsureSpread();
        return Vertices3D.Bounds(_drawnY!);
    }

    /// <inheritdoc />
    public DataRange GetZDataBounds()
    {
        EnsureSpread();
        return Vertices3D.Bounds(_drawnZ!);
    }

    /// <summary>The diameter a point is drawn at, in points — a bubble value or a marker area.</summary>
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
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);

        int count = _x.Length;
        if (count == 0)
        {
            return;
        }

        SeriesRenderer.EnsureCapacity(ref _pixels, count);
        SeriesRenderer.EnsureCapacity(ref _sorted, count);
        if (_depths.Length < count)
        {
            _depths = new double[count];
            _order = new int[count];
        }

        EnsureSpread();
        int visible = 0;
        for (int i = 0; i < count; i++)
        {
            double x = _drawnX![i], y = _drawnY![i], z = _drawnZ![i];
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                continue;
            }

            (Point2D position, double depth) = projection.Project(x, y, z);
            _pixels[i] = position;
            _depths[visible] = depth;
            _order[visible] = i;
            visible++;
        }

        if (visible == 0)
        {
            return;
        }

        // Painter's order: depth grows toward the viewer, so ascending draws the far markers first.
        // The depths are the sort keys and the source indices ride along, which is what lets the
        // per-point size and color channels stay indexed by the point they belong to.
        Array.Sort(_depths, _order, 0, visible);
        for (int i = 0; i < visible; i++)
        {
            _sorted[i] = _pixels[_order[i]];
        }

        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        if (_colorData is null && _sizeData is null)
        {
            // One uniform style: the whole cloud goes out as a single call.
            context.DrawMarkers(_sorted.AsSpan(0, visible), StyleFor(color), color);
            return;
        }

        (double min, double max) = ResolveColorRange();
        for (int i = 0; i < visible; i++)
        {
            int source = _order[i];
            Color point = _colorData is { } values
                ? _colormap.Sample(values[source], min, max).WithOpacity(Opacity)
                : color;
            double size = DiameterAt(source);
            context.DrawMarkers(_sorted.AsSpan(i, 1), StyleFor(point, size), point);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        return new LegendKey(
            line: null,
            StyleFor(color, System.Math.Min(_markerSize, 8)),
            swatch: null);
    }

    /// <summary>
    /// The x spread width that was <em>set</em>, or zero when none was — which is what a saved figure
    /// has to keep, so that a width following the data goes on following it after a load.
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

    /// <summary>The z spread width that was set, or zero when none was.</summary>
    [Browsable(false)]
    public double ZJitterWidthOverride
    {
        get => _zJitter.Width;
        set => ZJitterWidth = value;
    }

    /// <summary>How far along x each point is drawn from where its data puts it.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> XOffsets => _xJitter.Offsets(_x, _z);

    /// <summary>How far along y each point is drawn from where its data puts it.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> YOffsets => _yJitter.Offsets(_y, _z);

    /// <summary>How far along z each point is drawn from where its data puts it.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> ZOffsets => _zJitter.Offsets(_z, _y);

    /// <summary>Where each point is drawn along x, which is where its data puts it plus its spread.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> DrawnX
    {
        get
        {
            EnsureSpread();
            return _drawnX!;
        }
    }

    /// <summary>Where each point is drawn along y.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> DrawnY
    {
        get
        {
            EnsureSpread();
            return _drawnY!;
        }
    }

    /// <summary>Where each point is drawn along z.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> DrawnZ
    {
        get
        {
            EnsureSpread();
            return _drawnZ!;
        }
    }

    /// <summary>
    /// Works out where the markers go. The spread along x and along y both read their crowding off z,
    /// which is the height a swarm in space is a swarm <em>of</em>; the spread along z, the odd one
    /// out, reads y. Nothing is worked out at all when no axis is spreading, and then the drawn
    /// positions are the given ones.
    /// </summary>
    private void EnsureSpread()
    {
        if (_drawnX is not null)
        {
            return;
        }

        _drawnX = Spread(_x, _xJitter, _z);
        _drawnY = Spread(_y, _yJitter, _z);
        _drawnZ = Spread(_z, _zJitter, _y);
    }

    private static double[] Spread(double[] values, JitterChannel channel, double[] crowded)
    {
        if (!channel.Spreads)
        {
            return values;
        }

        double[] offsets = channel.Offsets(values, crowded);
        var drawn = new double[values.Length];
        for (int i = 0; i < drawn.Length; i++)
        {
            drawn[i] = values[i] + offsets[i];
        }

        return drawn;
    }

    private void DiscardSpread()
    {
        _drawnX = null;
        _drawnY = null;
        _drawnZ = null;
        Invalidate(InvalidationKind.Data);
    }

    private MarkerStyle StyleFor(Color color) => StyleFor(color, _markerSize);

    private MarkerStyle StyleFor(Color color, double size) =>
        new(_marker, size, _filled ? color : null, color, _edgeWidth);

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor || _colorData is null)
        {
            return (_colorMin, _colorMax);
        }

        DataRange bounds = Vertices3D.Bounds(_colorData);
        return bounds.IsValid ? (bounds.Min, bounds.Max) : (_colorMin, _colorMax);
    }

    private double[]? Checked(IReadOnlyList<double>? values, string what)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count != _x.Length)
        {
            throw new ArgumentException(
                $"{what} needs one entry per point ({_x.Length}), but got {values.Count}.", nameof(values));
        }

        return values.ToArray();
    }
}
