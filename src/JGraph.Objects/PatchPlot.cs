using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a patch face takes its color from <see cref="PatchPlot.ColorData"/>.</summary>
public enum PatchShading
{
    /// <summary>One color for the whole face (MATLAB <c>'FaceColor', 'flat'</c>).</summary>
    Flat,

    /// <summary>The vertex colors are interpolated across the face (MATLAB <c>'interp'</c>).</summary>
    Interp,
}

/// <summary>
/// Filled polygons over a shared vertex list — MATLAB's <c>patch</c>, and the <c>fill</c>/<c>fill3</c>
/// verbs built on it. Vertices carry X, Y and Z; <see cref="Faces"/> indexes into them, one entry per
/// face, so two faces can share an edge without duplicating its endpoints.
///
/// The same object draws in a 2D axes and a 3D one. In 2D the faces are drawn in declaration order and
/// Z is ignored, which is what <c>fill</c> means. In 3D they are sorted back to front by mean projected
/// depth — a painter's algorithm, so two faces that genuinely intersect cannot be resolved, but any
/// convex or well-separated set is drawn correctly.
///
/// Color comes from one of two places. <see cref="FaceColor"/> paints every face the same; otherwise
/// <see cref="ColorData"/> is sampled through <see cref="Colormap"/> — one value per face when its
/// length matches the face count, one per vertex when it matches the vertex count. Only the per-vertex
/// form can be interpolated, and that path is the one that goes through <c>DrawTriangles</c>.
/// </summary>
public sealed class PatchPlot : PlotObject, IDrawable, I3DDrawable, IHasZData, ILegendItem, IColorMapped
{
    private double[] _x;
    private double[] _y;
    private double[] _z;
    private int[][] _faces;
    private double[]? _colorData;

    private Color? _faceColor;
    private bool _faceVisible = true;
    private Color? _edgeColor = Colors.Black;
    private double _edgeWidth = 0.75;
    private PatchShading _shading = PatchShading.Flat;

    private Colormap _colormap = Colormap.Parula;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;

    private Point2D[] _pixels = new Point2D[16];
    private double[] _faceDepths = Array.Empty<double>();
    private int[] _faceOrder = Array.Empty<int>();
    private Point2D[] _face = new Point2D[8];
    private Point2D[] _fan = new Point2D[24];
    private uint[] _fanColors = new uint[24];

    public PatchPlot(double[] x, double[] y, double[] z, int[][] faces)
    {
        int vertices = Vertices3D.Validate("A patch", x, y, z);
        ArgumentNullException.ThrowIfNull(faces);
        foreach (int[] face in faces)
        {
            ArgumentNullException.ThrowIfNull(face);
            foreach (int index in face)
            {
                if (index < 0 || index >= vertices)
                {
                    throw new ArgumentException(
                        $"A patch face refers to vertex {index}, but there are only {vertices}.", nameof(faces));
                }
            }
        }

        _x = x;
        _y = y;
        _z = z;
        _faces = faces;
        Name = "Patch";
    }

    /// <summary>Builds a single-face patch through the given points, closing it automatically.</summary>
    public PatchPlot(double[] x, double[] y, double[] z)
        : this(x, y, z, [Ramp(x?.Length ?? 0)])
    {
    }

    /// <summary>The X coordinate of each vertex.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The Y coordinate of each vertex.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>The Z coordinate of each vertex.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Z => _z;

    /// <summary>The vertex indices of each face.</summary>
    [Browsable(false)]
    public IReadOnlyList<int[]> Faces => _faces;

    /// <summary>Replaces the vertex list and the faces over it.</summary>
    public void SetData(double[] x, double[] y, double[] z, int[][] faces)
    {
        var replacement = new PatchPlot(x, y, z, faces);
        _x = replacement._x;
        _y = replacement._y;
        _z = replacement._z;
        _faces = replacement._faces;
        _colorData = null;
        Invalidate(InvalidationKind.Layout);
    }

    /// <summary>
    /// Values colored through <see cref="Colormap"/>: one per face, or one per vertex. Null leaves the
    /// patch on <see cref="FaceColor"/> (or the series color).
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<double>? ColorData
    {
        get => _colorData;
        set
        {
            if (value is not null && value.Count != _faces.Length && value.Count != _x.Length)
            {
                throw new ArgumentException(
                    $"ColorData needs one value per face ({_faces.Length}) or per vertex ({_x.Length}), "
                        + $"but got {value.Count}.",
                    nameof(value));
            }

            _colorData = value?.ToArray();
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>One color for every face, or null to take it from <see cref="ColorData"/>.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether the faces are filled at all (MATLAB <c>'FaceColor', 'none'</c>). With the fill off and
    /// <see cref="ColorData"/> present, each face's outline takes the color the fill would have had —
    /// which is what turns a <c>trisurf</c> into a <c>trimesh</c>.
    /// </summary>
    [Category("Appearance"), DisplayName("Face visible")]
    public bool FaceVisible
    {
        get => _faceVisible;
        set => SetProperty(ref _faceVisible, value, InvalidationKind.Render);
    }

    /// <summary>The face outline color, or null for no outline (MATLAB <c>'EdgeColor', 'none'</c>).</summary>
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

    /// <summary>Whether per-vertex colors are interpolated across a face or flattened to one color.</summary>
    [Category("Appearance")]
    public PatchShading Shading
    {
        get => _shading;
        set => SetProperty(ref _shading, value, InvalidationKind.Render);
    }

    /// <summary>The colormap <see cref="ColorData"/> is sampled through.</summary>
    [Category("Appearance")]
    public Colormap Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render);
    }

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
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        if (_x.Length == 0 || _faces.Length == 0)
        {
            return;
        }

        SeriesRenderer.EnsureCapacity(ref _pixels, _x.Length);
        for (int i = 0; i < _x.Length; i++)
        {
            _pixels[i] = double.IsFinite(_x[i]) && double.IsFinite(_y[i])
                ? state.Mapper.DataToPixel(_x[i], _y[i])
                : Point2D.NaN;
        }

        DrawFaces(context, state, order: null);
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);
        if (_x.Length == 0 || _faces.Length == 0)
        {
            return;
        }

        SeriesRenderer.EnsureCapacity(ref _pixels, _x.Length);
        var depths = new double[_x.Length];
        for (int i = 0; i < _x.Length; i++)
        {
            if (double.IsFinite(_x[i]) && double.IsFinite(_y[i]) && double.IsFinite(_z[i]))
            {
                (_pixels[i], depths[i]) = projection.Project(_x[i], _y[i], _z[i]);
            }
            else
            {
                _pixels[i] = Point2D.NaN;
                depths[i] = double.NaN;
            }
        }

        if (_faceDepths.Length < _faces.Length)
        {
            _faceDepths = new double[_faces.Length];
            _faceOrder = new int[_faces.Length];
        }

        for (int f = 0; f < _faces.Length; f++)
        {
            double sum = 0;
            int n = 0;
            foreach (int v in _faces[f])
            {
                if (double.IsFinite(depths[v]))
                {
                    sum += depths[v];
                    n++;
                }
            }

            // A face with no finite vertex sorts to the very back, where its skipped draw costs nothing.
            _faceDepths[f] = n > 0 ? sum / n : double.NegativeInfinity;
            _faceOrder[f] = f;
        }

        Array.Sort(_faceDepths, _faceOrder, 0, _faces.Length);
        DrawFaces(context, state, _faceOrder);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: (_faceColor ?? seriesColor).WithOpacity(Opacity));

    /// <summary>
    /// Draws every face from the already-projected <see cref="_pixels"/>, in <paramref name="order"/>
    /// when one is given (3D painter order) or in declaration order when it is not (2D).
    /// </summary>
    private void DrawFaces(IRenderContext context, RenderState state, int[]? order)
    {
        (double min, double max) = ResolveColorRange();
        Color fallback = (_faceColor ?? state.SeriesColor).WithOpacity(Opacity);
        LineStyle? stroke = _edgeColor is { } edge && _edgeWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity), _edgeWidth)
            : null;
        bool perVertex = _colorData is not null && _colorData.Length == _x.Length;
        bool interpolate = _faceVisible && perVertex && _shading == PatchShading.Interp;

        // With the fill off there is nothing left to carry the color, so the outline takes it. That
        // is the whole difference between a triangulated surface and a triangulated mesh, and it is
        // why an unfilled patch is still stroked even when no edge color was ever set.
        bool colorTheEdge = !_faceVisible && _colorData is not null && _edgeWidth > 0;

        for (int i = 0; i < _faces.Length; i++)
        {
            int[] face = _faces[order is null ? i : order[i]];
            if (face.Length < 2)
            {
                continue;
            }

            if (_face.Length < face.Length)
            {
                _face = new Point2D[face.Length];
            }

            bool drawable = true;
            for (int v = 0; v < face.Length; v++)
            {
                _face[v] = _pixels[face[v]];
                drawable &= _face[v].IsFinite;
            }

            if (!drawable)
            {
                continue;
            }

            if (interpolate)
            {
                FillInterpolated(context, face, min, max);
                if (stroke is not null)
                {
                    context.DrawPolygon(_face.AsSpan(0, face.Length), stroke, fill: null);
                }

                continue;
            }

            Color color = _colorData is { } values
                ? _colormap.Sample(
                    perVertex ? MeanOf(values, face) : values[order is null ? i : order[i]],
                    min,
                    max).WithOpacity(Opacity)
                : fallback;
            context.DrawPolygon(
                _face.AsSpan(0, face.Length),
                colorTheEdge ? new LineStyle(color, _edgeWidth) : stroke,
                _faceVisible ? color : null);
        }
    }

    /// <summary>
    /// Fills one face as a triangle fan with per-vertex colors. The fan pivots on the face's first
    /// vertex, which is exact for a convex face and is what MATLAB's own interp shading assumes.
    /// </summary>
    private void FillInterpolated(IRenderContext context, int[] face, double min, double max)
    {
        int triangles = face.Length - 2;
        if (triangles < 1)
        {
            return;
        }

        int needed = triangles * 3;
        if (_fan.Length < needed)
        {
            _fan = new Point2D[needed];
            _fanColors = new uint[needed];
        }

        double[] values = _colorData!;
        uint pivot = _colormap.Sample(values[face[0]], min, max).WithOpacity(Opacity).ToArgb();
        for (int t = 0; t < triangles; t++)
        {
            int at = t * 3;
            _fan[at] = _face[0];
            _fan[at + 1] = _face[t + 1];
            _fan[at + 2] = _face[t + 2];
            _fanColors[at] = pivot;
            _fanColors[at + 1] = _colormap.Sample(values[face[t + 1]], min, max).WithOpacity(Opacity).ToArgb();
            _fanColors[at + 2] = _colormap.Sample(values[face[t + 2]], min, max).WithOpacity(Opacity).ToArgb();
        }

        context.DrawTriangles(_fan.AsSpan(0, needed), _fanColors.AsSpan(0, needed));
    }

    private static double MeanOf(double[] values, int[] face)
    {
        double sum = 0;
        foreach (int v in face)
        {
            sum += values[v];
        }

        return sum / face.Length;
    }

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor || _colorData is null)
        {
            return (_colorMin, _colorMax);
        }

        DataRange bounds = Vertices3D.Bounds(_colorData);
        return bounds.IsValid ? (bounds.Min, bounds.Max) : (_colorMin, _colorMax);
    }

    private static int[] Ramp(int count)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = i;
        }

        return indices;
    }
}
