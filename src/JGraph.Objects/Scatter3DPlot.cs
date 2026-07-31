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
public sealed class Scatter3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem, IColorMapped
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
    public override DataRange GetXDataBounds() => Vertices3D.Bounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => Vertices3D.Bounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds() => Vertices3D.Bounds(_z);

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

        int visible = 0;
        for (int i = 0; i < count; i++)
        {
            double x = _x[i], y = _y[i], z = _z[i];
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
            double size = _sizeData is { } sizes
                ? System.Math.Sqrt(System.Math.Max(0, sizes[source]))
                : _markerSize;
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
