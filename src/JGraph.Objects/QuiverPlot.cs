using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A field of arrows — MATLAB's <c>quiver</c> and <c>quiver3</c>. Each arrow starts at
/// <c>(X, Y, Z)</c> and points along <c>(U, V, W)</c>; the same object draws in a 2D axes, where Z
/// and W are ignored, and in a 3D one, where the whole thing is projected through the camera.
///
/// Arrowheads are built in <em>screen</em> space, from the projected shaft: a head sized in data
/// units would be stretched by the axis scales and squashed by the projection, so an arrow pointing
/// away from the viewer would end in a smear rather than a point.
/// </summary>
public sealed class QuiverPlot : PlotObject, IDrawable, I3DDrawable, IHasZData, ILegendItem
{
    private double[] _x;
    private double[] _y;
    private double[] _z;
    private double[] _u;
    private double[] _v;
    private double[] _w;

    private Color? _color;
    private double _lineWidth = 1;
    private bool _autoScale = true;
    private double _autoScaleFactor = 0.9;
    private double _scale = 1;
    private bool _showArrowHead = true;
    private double _maxHeadSize = 0.2;

    private double? _resolvedScale;
    private Point2D[] _verts = new Point2D[64];
    private int[] _starts = new int[16];

    /// <summary>How far back from the tip the barbs sit, as a fraction of the shaft — MATLAB's own ratio.</summary>
    private const double HeadAngleRadians = 0.3;

    /// <summary>Creates a 2D arrow field: <c>(x, y)</c> tails and <c>(u, v)</c> components.</summary>
    public QuiverPlot(double[] x, double[] y, double[] u, double[] v)
        : this(x, y, new double[x?.Length ?? 0], u, v, new double[x?.Length ?? 0])
    {
    }

    /// <summary>Creates a 3D arrow field: <c>(x, y, z)</c> tails and <c>(u, v, w)</c> components.</summary>
    public QuiverPlot(double[] x, double[] y, double[] z, double[] u, double[] v, double[] w)
    {
        int count = Vertices3D.Validate("A quiver plot", x, y, z);
        if (Vertices3D.Validate("A quiver plot's components", u, v, w) != count)
        {
            throw new ArgumentException(
                $"A quiver plot needs one component per point, but got {u.Length} for {count} points.",
                nameof(u));
        }

        _x = x;
        _y = y;
        _z = z;
        _u = u;
        _v = v;
        _w = w;
        Name = "Quiver";
    }

    /// <summary>The X coordinate of each arrow's tail.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The Y coordinate of each arrow's tail.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>The Z coordinate of each arrow's tail.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Z => _z;

    /// <summary>The X component of each arrow.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> U => _u;

    /// <summary>The Y component of each arrow.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> V => _v;

    /// <summary>The Z component of each arrow.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> W => _w;

    /// <summary>Replaces the whole field as one consistent set.</summary>
    public void SetData(double[] x, double[] y, double[] z, double[] u, double[] v, double[] w)
    {
        var replacement = new QuiverPlot(x, y, z, u, v, w);
        _x = replacement._x;
        _y = replacement._y;
        _z = replacement._z;
        _u = replacement._u;
        _v = replacement._v;
        _w = replacement._w;
        _resolvedScale = null;
        Invalidate(InvalidationKind.Layout);
    }

    /// <summary>Explicit arrow color, or null to use the auto series color.</summary>
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

    /// <summary>
    /// When true the components are scaled so the longest arrow spans about one grid step, which is
    /// what keeps a field readable; when false <see cref="Scale"/> multiplies them directly.
    /// </summary>
    [Category("Appearance"), DisplayName("Auto scale")]
    public bool AutoScale
    {
        get => _autoScale;
        set
        {
            if (SetProperty(ref _autoScale, value, InvalidationKind.Render))
            {
                _resolvedScale = null;
            }
        }
    }

    /// <summary>How much of one grid step the longest auto-scaled arrow fills (MATLAB's default is 0.9).</summary>
    [Category("Appearance"), DisplayName("Auto scale factor")]
    public double AutoScaleFactor
    {
        get => _autoScaleFactor;
        set
        {
            if (SetProperty(ref _autoScaleFactor, System.Math.Max(0, value), InvalidationKind.Render))
            {
                _resolvedScale = null;
            }
        }
    }

    /// <summary>The multiplier applied to the components when <see cref="AutoScale"/> is off.</summary>
    [Category("Appearance")]
    public double Scale
    {
        get => _scale;
        set
        {
            if (SetProperty(ref _scale, value, InvalidationKind.Render))
            {
                _resolvedScale = null;
            }
        }
    }

    /// <summary>Whether each arrow ends in a head (MATLAB <c>'ShowArrowHead', 'off'</c> turns it off).</summary>
    [Category("Appearance"), DisplayName("Show arrow head")]
    public bool ShowArrowHead
    {
        get => _showArrowHead;
        set => SetProperty(ref _showArrowHead, value, InvalidationKind.Render);
    }

    /// <summary>The head length as a fraction of the arrow's own length.</summary>
    [Category("Appearance"), DisplayName("Max head size")]
    public double MaxHeadSize
    {
        get => _maxHeadSize;
        set => SetProperty(ref _maxHeadSize, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>
    /// The multiplier actually applied to the components, whether it came from <see cref="Scale"/> or
    /// from the auto-scale rule.
    /// </summary>
    [Browsable(false)]
    public double EffectiveScale => _resolvedScale ??= ResolveScale();

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => ComponentBounds(_x, _u);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => ComponentBounds(_y, _v);

    /// <inheritdoc />
    public DataRange GetZDataBounds() => ComponentBounds(_z, _w);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        Draw(context, state, (x, y, z) => state.Mapper.DataToPixel(x, y));
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);
        Draw(context, state, projection.ProjectPoint);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(new LineStyle((_color ?? seriesColor).WithOpacity(Opacity), _lineWidth), marker: null, swatch: null);

    /// <summary>
    /// Emits every arrow as sub-paths of one stroked path: a two-point shaft, and — when heads are
    /// on — a three-point barb through the tip. Batching them is what keeps a dense field to a single
    /// draw call, and it is safe because every arrow in a plot shares one style.
    /// </summary>
    private void Draw(IRenderContext context, RenderState state, Func<double, double, double, Point2D> project)
    {
        int count = _x.Length;
        if (count == 0 || _lineWidth <= 0)
        {
            return;
        }

        double scale = EffectiveScale;
        int vertices = count * (_showArrowHead ? 5 : 2);
        int paths = count * (_showArrowHead ? 2 : 1);
        if (_verts.Length < vertices)
        {
            _verts = new Point2D[vertices];
        }

        if (_starts.Length < paths)
        {
            _starts = new int[paths];
        }

        int v = 0;
        int s = 0;
        for (int i = 0; i < count; i++)
        {
            double x = _x[i], y = _y[i], z = _z[i];
            double u = _u[i], w2 = _v[i], w3 = _w[i];
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)
                || !double.IsFinite(u) || !double.IsFinite(w2) || !double.IsFinite(w3))
            {
                continue;
            }

            Point2D tail = project(x, y, z);
            Point2D tip = project(x + (u * scale), y + (w2 * scale), z + (w3 * scale));
            if (!tail.IsFinite || !tip.IsFinite)
            {
                continue;
            }

            _starts[s++] = v;
            _verts[v++] = tail;
            _verts[v++] = tip;

            if (!_showArrowHead)
            {
                continue;
            }

            // The barbs are measured on the projected shaft, so a foreshortened arrow gets a
            // foreshortened head and an arrow of zero screen length gets none at all.
            double dx = tip.X - tail.X;
            double dy = tip.Y - tail.Y;
            double length = System.Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0)
            {
                continue;
            }

            double head = _maxHeadSize * length;
            double cos = System.Math.Cos(HeadAngleRadians) * head / length;
            double sin = System.Math.Sin(HeadAngleRadians) * head / length;

            _starts[s++] = v;
            _verts[v++] = new Point2D(tip.X - (dx * cos) - (dy * sin), tip.Y - (dy * cos) + (dx * sin));
            _verts[v++] = tip;
            _verts[v++] = new Point2D(tip.X - (dx * cos) + (dy * sin), tip.Y - (dy * cos) - (dx * sin));
        }

        if (s == 0)
        {
            return;
        }

        var style = new LineStyle((_color ?? state.SeriesColor).WithOpacity(Opacity), _lineWidth);
        context.DrawPaths(_verts.AsSpan(0, v), _starts.AsSpan(0, s), closed: false, style, fill: null);
    }

    /// <summary>
    /// The auto-scale rule: fit the longest arrow into about one grid step, where a step is taken as
    /// the side of the cell each point would own if they were spread evenly over their own bounding
    /// box. Reading a spacing off the data rather than assuming a <c>meshgrid</c> is what lets a
    /// scattered field scale sensibly too.
    /// </summary>
    private double ResolveScale()
    {
        if (!_autoScale)
        {
            return _scale;
        }

        int count = _x.Length;
        if (count == 0)
        {
            return _scale;
        }

        double longest = 0;
        for (int i = 0; i < count; i++)
        {
            double u = _u[i], v = _v[i], w = _w[i];
            if (double.IsFinite(u) && double.IsFinite(v) && double.IsFinite(w))
            {
                longest = System.Math.Max(longest, System.Math.Sqrt((u * u) + (v * v) + (w * w)));
            }
        }

        if (longest <= 0)
        {
            return _scale;
        }

        double spanX = Span(_x);
        double spanY = Span(_y);
        double spanZ = Span(_z);
        double step = spanZ > 0
            ? System.Math.Cbrt(System.Math.Max(spanX, spanZ) * System.Math.Max(spanY, spanZ) * spanZ / count)
            : System.Math.Sqrt(System.Math.Max(spanX * spanY, 0) / count);

        // A field along a single line has no area to divide up, so fall back to its one real extent.
        if (!(step > 0))
        {
            step = System.Math.Max(System.Math.Max(spanX, spanY), spanZ) / System.Math.Max(1, count - 1);
        }

        return step > 0 ? _autoScaleFactor * step / longest : _scale;
    }

    private static double Span(double[] values)
    {
        DataRange bounds = Vertices3D.Bounds(values);
        return bounds.IsValid ? bounds.Max - bounds.Min : 0;
    }

    /// <summary>
    /// The extent of the tails together with the tips they scale to, so the axes admit whole arrows
    /// rather than clipping every head at the edge of the field.
    /// </summary>
    private DataRange ComponentBounds(double[] positions, double[] components)
    {
        double scale = EffectiveScale;
        DataRange bounds = DataRange.Empty;
        for (int i = 0; i < positions.Length; i++)
        {
            double p = positions[i];
            if (!double.IsFinite(p))
            {
                continue;
            }

            bounds = bounds.Include(p);
            double tip = p + (components[i] * scale);
            if (double.IsFinite(tip))
            {
                bounds = bounds.Include(tip);
            }
        }

        return bounds;
    }
}
