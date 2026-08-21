using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using JGraph.Maths.Transforms;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a <see cref="SurfacePlot"/> draws its grid cells.</summary>
public enum SurfaceStyle
{
    /// <summary>Colormap-colored edges only (MATLAB <c>mesh</c>).</summary>
    Wireframe,

    /// <summary>Colormap-filled faces only.</summary>
    Filled,

    /// <summary>Colormap-filled faces with edge lines (MATLAB <c>surf</c>).</summary>
    FilledWithWireframe,
}

/// <summary>How a <see cref="SurfacePlot"/> colors the inside of a cell (MATLAB <c>shading</c>).</summary>
public enum SurfaceShading
{
    /// <summary>One flat color per cell, taken from the mean of its four corner heights.</summary>
    Flat,

    /// <summary>Corner colors interpolated across the cell (MATLAB <c>shading interp</c>).</summary>
    Interp,
}

/// <summary>
/// How a <see cref="SurfacePlot"/> responds to the lights on its axes (MATLAB <c>lighting</c> and the
/// surface's <c>FaceLighting</c> property). Only matters once a light exists; an axes has none until
/// a script adds one, so a plain <c>surf</c> is unlit whatever this says.
/// </summary>
public enum SurfaceLighting
{
    /// <summary>Lights are ignored: faces keep their colormap color (MATLAB <c>lighting none</c>).</summary>
    None,

    /// <summary>One normal per facet, so each cell is uniformly lit (MATLAB <c>lighting flat</c>).</summary>
    Flat,

    /// <summary>Per-vertex normals interpolated across each facet (MATLAB <c>lighting gouraud</c>).</summary>
    Gouraud,
}

/// <summary>
/// A 3D surface (MATLAB <c>surf</c>/<c>mesh</c>/<c>meshc</c>): heights <c>Z[row, col]</c> colored
/// through a <see cref="Colormap"/>, rendered as per-cell quads projected through the axes' camera
/// and painted back to front, so it needs no z-buffer and works on every render backend.
/// </summary>
/// <remarks>
/// The grid comes in two forms. A <em>rectilinear</em> surface samples at <c>X[col]</c>/<c>Y[row]</c>
/// — one position per column and per row — which is what <c>surf(x, y, z)</c> and everything built on
/// a <c>meshgrid</c> produce, and it is the fast path: the cells can be painted in an analytic sweep
/// and normals come from the height field directly. A <em>parametric</em> surface carries a full
/// <c>X[row, col]</c>/<c>Y[row, col]</c> pair instead, which is the only way to express a sphere, a
/// cylinder, or anything else that folds back over itself in X or Y. Parametric grids are painted by
/// the depth-sorted fallback, since the sweep is only valid for a height field.
/// </remarks>
public sealed class SurfacePlot : PlotObject, I3DDrawable, IHasZData, ILegendItem, IColorMapped, ILitObject
{
    private double[,] _z;
    private double[] _x;
    private double[] _y;
    private double[,]? _xGrid;
    private double[,]? _yGrid;
    private Colormap _colormap = Colormap.Parula;
    private uint[]? _texture;
    private double[,]? _cData;
    private SurfaceStyle _style = SurfaceStyle.FilledWithWireframe;
    private SurfaceShading _shading = SurfaceShading.Flat;
    private bool _showContourBelow;
    private Color? _edgeColor;
    private Color? _faceColor;
    private double _faceAlpha = 1;
    private double _edgeAlpha = 1;
    private double _edgeWidth = 0.75;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;
    private int _contourLevels = 8;
    private SurfaceLighting _faceLighting = SurfaceLighting.Flat;
    private double _ambientStrength = 0.3;
    private double _diffuseStrength = 0.6;
    private double _specularStrength = 0.9;
    private double _specularExponent = 10;
    private double _specularColorReflectance = 1;

    // Data-derived caches, cleared by SetData. GetZDataBounds is called three times per frame
    // without drawing anything (axis autoscale, the color range, and the colorbar), and each call
    // used to walk the whole matrix.
    private DataRange? _zBounds;
    private bool? _gridIsMonotone;
    private bool[]? _drawableCells;
    private PaletteCache? _palette;
    private double[,]? _alphaData;
    private bool _faceAlphaFlat;
    private ContourLineSet? _floorContours;
    private double[]? _floorLevels;

    // Per-frame scratch geometry, kept on the instance so a rotate drag allocates nothing. It is
    // view-dependent, so it is never treated as valid across frames -- it is refilled every render.
    private Point2D[]? _points;
    private double[]? _depths;
    private int[]? _order;
    private int[]? _groups;
    private double[]? _cellDepth;
    private Point2D[]? _faceVerts;
    private uint[]? _faceColors;
    private Point2D[]? _edgeVerts;
    private int[]? _edgeStarts;
    private Point2D[]? _floorVerts;
    private int[]? _floorStarts;

    // Lighting scratch. Normals and the shaded colors both depend on the camera and on the axes
    // ranges, neither of which raises this plot's Invalidated, so they are recomputed every frame --
    // but only when a light exists, which by default is never.
    private uint[]? _litColors;
    private double[]? _nx;
    private double[]? _ny;
    private double[]? _nz;
    private double[]? _pxGrid;
    private double[]? _pyGrid;
    private LightSource[]? _lightScratch;
    private int _rendering;

    /// <summary>
    /// How many cells go into one draw call. Painter order is preserved inside a batch, so a group
    /// can always be split at any point; the cap only bounds the scratch buffers, which would
    /// otherwise reach tens of megabytes on a 500x500 grid.
    /// </summary>
    private const int MaxCellsPerBatch = 4096;

    /// <summary>Creates a surface over <c>z[row, col]</c> with unit-spaced X (columns) and Y (rows).</summary>
    public SurfacePlot(double[,] z)
        : this(Ramp(z is null ? 0 : z.GetLength(1)), Ramp(z is null ? 0 : z.GetLength(0)), z!)
    {
    }

    /// <summary>Creates a surface over <c>z[row, col]</c> sampled at <c>x[col]</c>/<c>y[row]</c>.</summary>
    public SurfacePlot(double[] x, double[] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (z.GetLength(0) != y.Length || z.GetLength(1) != x.Length)
        {
            throw new ArgumentException(
                $"z must be [{y.Length} rows x {x.Length} cols] to match y and x, but was [{z.GetLength(0)} x {z.GetLength(1)}].");
        }

        _x = x;
        _y = y;
        _z = z;
        Name = "Surface";
    }

    /// <summary>
    /// Creates a parametric surface: a position per vertex rather than per row and column, which is
    /// what a sphere or a cylinder needs. All three matrices must be the same size.
    /// </summary>
    public SurfacePlot(double[,] x, double[,] y, double[,] z)
    {
        ValidateGrids(x, y, z);
        _x = Ramp(z.GetLength(1));
        _y = Ramp(z.GetLength(0));
        _xGrid = x;
        _yGrid = y;
        _z = z;
        Name = "Surface";
    }

    /// <summary>
    /// The grid X positions, one per column of <see cref="Z"/>. On a parametric surface these are the
    /// column indices and <see cref="XGrid"/> holds the real positions.
    /// </summary>
    [Browsable(false)]
    public double[] X
    {
        get => _x;
        set => SetData(value ?? throw new ArgumentNullException(nameof(value)), _y, _z);
    }

    /// <summary>
    /// The grid Y positions, one per row of <see cref="Z"/>. On a parametric surface these are the
    /// row indices and <see cref="YGrid"/> holds the real positions.
    /// </summary>
    [Browsable(false)]
    public double[] Y
    {
        get => _y;
        set => SetData(_x, value ?? throw new ArgumentNullException(nameof(value)), _z);
    }

    /// <summary>The per-vertex X positions of a parametric surface, or null when the grid is rectilinear.</summary>
    [Browsable(false)]
    public double[,]? XGrid => _xGrid;

    /// <summary>The per-vertex Y positions of a parametric surface, or null when the grid is rectilinear.</summary>
    [Browsable(false)]
    public double[,]? YGrid => _yGrid;

    /// <summary>
    /// Whether this surface carries a position per vertex rather than per row and column. A
    /// parametric surface cannot use the sweep ordering or draw floor contours, both of which assume
    /// a height field over a rectilinear grid.
    /// </summary>
    [Browsable(false)]
    public bool IsParametric => _xGrid is not null;

    /// <summary>The surface heights, <c>[row, col]</c> with rows indexing Y.</summary>
    [Browsable(false)]
    public double[,] Z
    {
        get => _z;
        set => SetData(_x, _y, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Replaces the grid data as one consistent set.</summary>
    public void SetData(double[] x, double[] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (z.GetLength(0) != y.Length || z.GetLength(1) != x.Length)
        {
            throw new ArgumentException(
                $"z must be [{y.Length} rows x {x.Length} cols] to match y and x, but was [{z.GetLength(0)} x {z.GetLength(1)}].");
        }

        _x = x;
        _y = y;
        _z = z;
        _xGrid = null;
        _yGrid = null;
        DropDataCaches();
    }

    /// <summary>Replaces the grid data with a parametric one — a position per vertex.</summary>
    public void SetData(double[,] x, double[,] y, double[,] z)
    {
        ValidateGrids(x, y, z);
        _x = Ramp(z.GetLength(1));
        _y = Ramp(z.GetLength(0));
        _xGrid = x;
        _yGrid = y;
        _z = z;
        DropDataCaches();
    }

    private void DropDataCaches()
    {
        _zBounds = null;
        _gridIsMonotone = null;

        // These are sized from the grid or traced through it, so a resize has to drop them outright
        // rather than merely mark them stale.
        _drawableCells = null;
        _palette = null;
        _floorContours = null;
        Invalidate(InvalidationKind.Data);
    }

    private static void ValidateGrids(double[,] x, double[,] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (x.GetLength(0) != z.GetLength(0) || x.GetLength(1) != z.GetLength(1)
            || y.GetLength(0) != z.GetLength(0) || y.GetLength(1) != z.GetLength(1))
        {
            throw new ArgumentException(
                $"x, y and z must be the same size, but were [{x.GetLength(0)} x {x.GetLength(1)}], "
                + $"[{y.GetLength(0)} x {y.GetLength(1)}] and [{z.GetLength(0)} x {z.GetLength(1)}].");
        }
    }

    /// <summary>The X position of one grid vertex, whichever form the grid is in.</summary>
    private double Xat(int r, int c) => _xGrid is { } grid ? grid[r, c] : _x[c];

    /// <summary>The Y position of one grid vertex, whichever form the grid is in.</summary>
    private double Yat(int r, int c) => _yGrid is { } grid ? grid[r, c] : _y[r];

    /// <summary>The colormap heights are colored through.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(_colormap, value))
            {
                _colormap = value;
                Invalidate(InvalidationKind.Render);
            }
        }
    }

    /// <summary>
    /// An explicit value per vertex to take the colour from, in place of the height — MATLAB's
    /// <c>C</c> in <c>surf(X, Y, Z, C)</c>. Null is the default and means colour by <c>Z</c>.
    /// <para>
    /// It must be the same shape as <c>Z</c>, because it is one reading per vertex of the same grid.
    /// A texture (<see cref="TextureData"/>) still wins over it: a picture laid on a surface is a
    /// colour already, with nothing left for a colormap to decide.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The array is not the same size as Z.</exception>
    [Browsable(false)]
    public double[,]? CData
    {
        get => _cData;
        set
        {
            if (value is not null
                && (value.GetLength(0) != _z.GetLength(0) || value.GetLength(1) != _z.GetLength(1)))
            {
                throw new ArgumentException(
                    $"colour data needs one value per grid vertex: {value.GetLength(0)}-by-{value.GetLength(1)} given, "
                    + $"{_z.GetLength(0)}-by-{_z.GetLength(1)} wanted.");
            }

            _cData = value;
            _palette = null; // The cached colours belong to the mapping that has just been replaced.
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// A colour for every grid vertex, row-major and 0xAARRGGBB, or null to take colours from the
    /// height through the colormap. This is what makes a surface carry a picture: the renderer already
    /// asks for one colour per vertex or per cell, so a texture is a different answer to a question it
    /// was asking anyway, rather than a second way of drawing.
    /// </summary>
    /// <exception cref="ArgumentException">The array is not one colour per grid vertex.</exception>
    [Browsable(false)]
    public uint[]? TextureData
    {
        get => _texture;
        set
        {
            int vertices = _z.GetLength(0) * _z.GetLength(1);
            if (value is not null && value.Length != vertices)
            {
                throw new ArgumentException(
                    $"a surface texture needs one colour per grid vertex: {value.Length} given, {vertices} wanted.",
                    nameof(value));
            }

            _texture = value;
            _palette = null; // The cached colours belong to the mapping that has just been replaced.
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>Whether cells draw as wireframe (mesh), filled faces, or both (surf).</summary>
    [Category("Appearance")]
    public SurfaceStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Render);
    }

    /// <summary>Whether a cell is one flat color or interpolates between its corner colors.</summary>
    [Category("Appearance")]
    public SurfaceShading Shading
    {
        get => _shading;
        set => SetProperty(ref _shading, value, InvalidationKind.Render);
    }

    /// <summary>When true, contour lines of the surface are drawn on the floor of the axes box (MATLAB <c>meshc</c>).</summary>
    [Category("Appearance"), DisplayName("Contour below")]
    public bool ShowContourBelow
    {
        get => _showContourBelow;
        set => SetProperty(ref _showContourBelow, value, InvalidationKind.Render);
    }

    /// <summary>How many contour lines <see cref="ShowContourBelow"/> draws on the floor.</summary>
    [Category("Appearance"), DisplayName("Contour levels")]
    public int ContourLevels
    {
        get => _contourLevels;
        set => SetProperty(ref _contourLevels, System.Math.Clamp(value, 1, 64), InvalidationKind.Render);
    }

    /// <summary>
    /// One colour for every face, instead of the colormap. Null is the normal surface, coloured by
    /// height; setting it is what makes a mesh opaque — <c>hidden on</c> paints the faces the axes'
    /// own background so the lines behind them are covered.
    /// </summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>
    /// A transparency for every point of the grid, looked up through the axes' alphamap (MATLAB
    /// <c>AlphaData</c>), or null while the surface is uniformly transparent. It is drawn only while
    /// <see cref="FaceAlphaFlat"/> is set, which is MATLAB's flat face alpha.
    /// </summary>
    public double[,]? AlphaData
    {
        get => _alphaData;
        set
        {
            if (value is not null
                && (value.GetLength(0) != _z.GetLength(0) || value.GetLength(1) != _z.GetLength(1)))
            {
                throw new ArgumentException(
                    $"AlphaData must match the surface: expected {_z.GetLength(0)} by {_z.GetLength(1)}, "
                    + $"got {value.GetLength(0)} by {value.GetLength(1)}.",
                    nameof(value));
            }

            _alphaData = value;
            _palette = null;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// Whether the faces take their transparency from <see cref="AlphaData"/> rather than from the
    /// single <see cref="FaceAlpha"/> number (MATLAB's flat face alpha).
    /// </summary>
    public bool FaceAlphaFlat
    {
        get => _faceAlphaFlat;
        set
        {
            _faceAlphaFlat = value;
            _palette = null;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// MATLAB <c>FaceAlpha</c>: how opaque the surface is, 0 through 1. It multiplies the object's own
    /// <see cref="PlotObject.Opacity"/> rather than replacing it, so <c>alpha(0.5)</c> — which works
    /// the whole object — and a per-surface setting compose instead of fighting.
    /// </summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>MATLAB <c>EdgeAlpha</c>: how opaque the wireframe is, on the same terms as <see cref="FaceAlpha"/>.</summary>
    [Category("Appearance"), DisplayName("Edge alpha")]
    public double EdgeAlpha
    {
        get => _edgeAlpha;
        set => SetProperty(ref _edgeAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The wireframe/edge color; null colors edges through the colormap (wireframe) or dark gray (filled).</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    /// <summary>The wireframe/edge line width.</summary>
    [Category("Appearance"), DisplayName("Edge width")]
    public double EdgeWidth
    {
        get => _edgeWidth;
        set => SetProperty(ref _edgeWidth, System.Math.Max(0.1, value), InvalidationKind.Render);
    }

    /// <summary>When true, the color range is taken from the Z extent; otherwise from <see cref="ColorMin"/>/<see cref="ColorMax"/>.</summary>
    [Category("Appearance"), DisplayName("Auto color scale")]
    public bool AutoScaleColor
    {
        get => _autoScaleColor;
        set => SetProperty(ref _autoScaleColor, value, InvalidationKind.Render);
    }

    /// <summary>The value mapped to the low end of the colormap (used when <see cref="AutoScaleColor"/> is false).</summary>
    [Category("Appearance"), DisplayName("Color min")]
    public double ColorMin
    {
        get => _colorMin;
        set => SetProperty(ref _colorMin, value, InvalidationKind.Render);
    }

    /// <summary>The value mapped to the high end of the colormap (used when <see cref="AutoScaleColor"/> is false).</summary>
    [Category("Appearance"), DisplayName("Color max")]
    public double ColorMax
    {
        get => _colorMax;
        set => SetProperty(ref _colorMax, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How this surface responds to the lights on its axes (MATLAB <c>lighting</c>). Has no effect
    /// until a light exists, so the default costs nothing.
    /// </summary>
    [Category("Lighting"), DisplayName("Face lighting")]
    public SurfaceLighting FaceLighting
    {
        get => _faceLighting;
        set => SetProperty(ref _faceLighting, value, InvalidationKind.Render);
    }

    /// <summary>MATLAB <c>AmbientStrength</c>: the fraction of the surface color present with no light on it.</summary>
    [Category("Lighting"), DisplayName("Ambient strength")]
    public double AmbientStrength
    {
        get => _ambientStrength;
        set => SetProperty(ref _ambientStrength, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>MATLAB <c>DiffuseStrength</c>: how strongly the surface reflects light head-on.</summary>
    [Category("Lighting"), DisplayName("Diffuse strength")]
    public double DiffuseStrength
    {
        get => _diffuseStrength;
        set => SetProperty(ref _diffuseStrength, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>MATLAB <c>SpecularStrength</c>: how bright the highlight is.</summary>
    [Category("Lighting"), DisplayName("Specular strength")]
    public double SpecularStrength
    {
        get => _specularStrength;
        set => SetProperty(ref _specularStrength, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>MATLAB <c>SpecularExponent</c>: how tight the highlight is (higher is tighter).</summary>
    [Category("Lighting"), DisplayName("Specular exponent")]
    public double SpecularExponent
    {
        get => _specularExponent;
        set => SetProperty(ref _specularExponent, System.Math.Max(1e-3, value), InvalidationKind.Render);
    }

    /// <summary>
    /// MATLAB <c>SpecularColorReflectance</c>: 0 tints the highlight with the surface color, 1 leaves
    /// it the light's own color.
    /// </summary>
    [Category("Lighting"), DisplayName("Specular color reflectance")]
    public double SpecularColorReflectance
    {
        get => _specularColorReflectance;
        set => SetProperty(ref _specularColorReflectance, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The five reflectance coefficients as one value (MATLAB's <c>material</c> vector).</summary>
    [Browsable(false)]
    public LightingModel Material
    {
        get => new(
            _ambientStrength, _diffuseStrength, _specularStrength, _specularExponent, _specularColorReflectance);
        set
        {
            AmbientStrength = value.Ambient;
            DiffuseStrength = value.Diffuse;
            SpecularStrength = value.Specular;
            SpecularExponent = value.SpecularExponent;
            SpecularColorReflectance = value.SpecularColorReflectance;
        }
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(null, null, _colormap.Sample(0.7));

    /// <inheritdoc />
    public (double Min, double Max) ColorRange => ResolveColorRange();

    /// <inheritdoc />
    /// <inheritdoc />
    public override void AdoptAxesDefaults(AxesModel axes)
    {
        if (axes.Colormap is { } map)
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

    public override DataRange GetXDataBounds() => _xGrid is { } grid ? MatrixBounds(grid) : VectorBounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => _yGrid is { } grid ? MatrixBounds(grid) : VectorBounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds()
    {
        if (_zBounds is { } cached)
        {
            return cached;
        }

        // An indexed loop, not foreach: the multidimensional-array enumerator is markedly slower,
        // and this walks every height in the grid.
        DataRange bounds = DataRange.Empty;
        int rows = _z.GetLength(0);
        int cols = _z.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double v = _z[r, c];
                if (double.IsFinite(v))
                {
                    bounds = bounds.Include(v);
                }
            }
        }

        _zBounds = bounds;
        return bounds;
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        int rows = _z.GetLength(0);
        int cols = _z.GetLength(1);
        if (rows < 2 || cols < 2)
        {
            return;
        }

        (double colorMin, double colorMax) = ResolveColorRange();

        // FaceAlpha and EdgeAlpha multiply the object's own opacity rather than replacing it, so
        // alpha(0.5) over a surface already set to FaceAlpha 0.5 is a quarter — which is what MATLAB
        // does, and what keeps the whole-object knob and the two per-part ones from fighting.
        double opacity = Opacity * _faceAlpha;
        double edgeOpacity = Opacity * _edgeAlpha;
        bool sweep = GridIsMonotone();

        // SortMethod 'childorder' says paint the cells in the order they are held rather than back to
        // front. The wavefront walk already visits them in a fixed order, so asking it not to reverse
        // either direction is exactly that, and it costs no sort at all.
        bool depthSort = state.DepthSort;

        // Scratch geometry normally lives on the instance so a rotate drag allocates nothing. A
        // second concurrent render of the same plot (a screen paint racing an export) takes local
        // arrays instead of sharing the fields.
        bool exclusive = Interlocked.Exchange(ref _rendering, 1) == 0;
        try
        {
            LightSource[]? lights = ResolveLights(projection, exclusive, out int lightCount);

            // Gouraud lighting needs a color per vertex to interpolate between, so it promotes the
            // palette exactly as `shading interp` does -- which is why `lighting gouraud` visibly
            // smooths a faceted surface instead of doing nothing.
            bool perVertex = _shading == SurfaceShading.Interp
                || (lights is not null && _faceLighting == SurfaceLighting.Gouraud);

            // Both of these are view-independent, so they survive a rotate drag and are rebuilt only
            // when the data or the color mapping changes. Between them they take every read of the
            // height matrix out of the per-frame loops, which matters because those loops walk the
            // grid diagonally: at 500x500 a Z lookup per cell corner is four cache misses a cell.
            bool[] drawable = DrawableCells(rows, cols);
            uint[] palette = Palette(rows, cols, colorMin, colorMax, opacity, perVertex);
            if (lights is not null)
            {
                palette = LitPalette(
                    rows, cols, projection, palette, drawable,
                    lights.AsSpan(0, lightCount), perVertex, exclusive);
            }

            Point2D[] points = RenderScratch.Rent(ref _points, rows * cols, exclusive);
            double[]? depths = sweep ? null : RenderScratch.Rent(ref _depths, rows * cols, exclusive);

            // Project every grid vertex once. Depths are only needed by the fallback ordering. The
            // two grid forms get their own loop rather than a test per vertex, because this is one of
            // the two passes that is O(rows * cols) on every single frame of a rotate drag.
            if (_xGrid is { } xg && _yGrid is { } yg)
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        (Point2D p, double d) = projection.Project(xg[r, c], yg[r, c], _z[r, c]);
                        points[(r * cols) + c] = p;
                        if (depths is not null)
                        {
                            depths[(r * cols) + c] = d;
                        }
                    }
                }
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        (Point2D p, double d) = projection.Project(_x[c], _y[r], _z[r, c]);
                        points[(r * cols) + c] = p;
                        if (depths is not null)
                        {
                            depths[(r * cols) + c] = d;
                        }
                    }
                }
            }

            // Floor contours trace a height field over a rectilinear grid, which a parametric
            // surface is not; there is no z = f(x, y) to march squares over.
            if (_showContourBelow && _xGrid is null)
            {
                DrawFloorContours(context, projection, colorMin, colorMax, opacity, exclusive);
            }

            // Probe the projection rather than reading its rotation coefficients: this way a
            // descending x vector or a reversed axis range is absorbed for free. Depth grows toward
            // the viewer, so the axis end with the smaller depth is the one to start from. Z
            // cancels out of the occlusion test, so any height works for the probes.
            double origin = projection.Project(Xat(0, 0), Yat(0, 0), 0).Depth;
            bool colForward = !depthSort
                || projection.Project(Xat(0, cols - 1), Yat(0, cols - 1), 0).Depth >= origin;
            bool rowForward = !depthSort
                || projection.Project(Xat(rows - 1, 0), Yat(rows - 1, 0), 0).Depth >= origin;

            bool drawFaces = _style != SurfaceStyle.Wireframe && _faceAlpha > 0;

            // EdgeAlpha 0 is how MATLAB hides a wireframe without losing the colour it would take if
            // it came back, which is why it turns the edges off here rather than being multiplied in.
            bool drawEdges = _style != SurfaceStyle.Filled && _edgeAlpha > 0;

            // Faces and edges have to alternate group by group: an edge must land on top of its own
            // cell but underneath every cell nearer than it. With only one of the two to draw there
            // is nothing to interleave, so the whole surface collapses into a single group.
            bool interleave = drawFaces;

            int cellCount = (rows - 1) * (cols - 1);
            int[] order = RenderScratch.Rent(ref _order, cellCount, exclusive);
            int[] groups = RenderScratch.Rent(ref _groups, cellCount + 1, exclusive);
            int groupCount = sweep || !depthSort
                ? BuildWavefrontOrder(rows, cols, colForward, rowForward, interleave, drawable, order, groups)
                : BuildDepthOrder(depths!, rows, cols, interleave, exclusive, drawable, order, groups);

            EmitCells(
                context, points, palette, drawable, order, groups, groupCount, cols,
                opacity, edgeOpacity, colForward, rowForward, drawFaces, drawEdges, perVertex, exclusive);
        }
        finally
        {
            if (exclusive)
            {
                Volatile.Write(ref _rendering, 0);
            }
        }
    }

    /// <summary>
    /// Orders the cells back to front without sorting anything, in anti-diagonal wavefronts. Under
    /// the orthographic projection one point hides another only when their difference is a positive
    /// multiple of the view direction — an expression in which Z does not appear at all. So a cell
    /// can only occlude cells that are behind it along <em>both</em> grid axes, which makes
    /// <c>sweepRow + sweepCol</c> an exact stratification: cells sharing a wavefront can never
    /// occlude each other, and every occluder sits in a later one. That is also strictly more
    /// faithful than sorting on mean vertex depth, which puts a tall spike behind the flat
    /// neighbour it actually covers.
    /// </summary>
    /// <remarks>
    /// A cell is identified throughout by the vertex index of its top-left corner, which is all the
    /// emit loops need: the other three corners are one step right, one row down, and both. Nothing
    /// downstream has to divide an index back into a row and a column.
    /// </remarks>
    /// <returns>The number of groups written to <paramref name="groups"/>, whose last entry is the cell count.</returns>
    private static int BuildWavefrontOrder(
        int rows,
        int cols,
        bool colForward,
        bool rowForward,
        bool interleave,
        bool[] drawable,
        int[] order,
        int[] groups)
    {
        int lastRow = rows - 2;
        int lastCol = cols - 2;
        int n = 0;
        int groupCount = 0;

        for (int k = 0; k <= lastRow + lastCol; k++)
        {
            int begin = n;
            for (int i = System.Math.Max(0, k - lastCol); i <= System.Math.Min(k, lastRow); i++)
            {
                int r = rowForward ? i : lastRow - i;
                int c = colForward ? k - i : lastCol - (k - i);
                int vi = (r * cols) + c;
                if (drawable[vi])
                {
                    order[n++] = vi;
                }
            }

            if (interleave && n > begin)
            {
                groups[groupCount++] = begin;
            }
        }

        if (!interleave)
        {
            groups[groupCount++] = 0;
        }

        groups[groupCount] = n;
        return groupCount;
    }

    /// <summary>
    /// The general ordering for grids the sweep cannot claim (a non-monotonic X or Y vector): sort
    /// every cell by its mean vertex depth and paint farthest first. The order is total rather than
    /// stratified, so each cell is its own group when faces and edges have to interleave.
    /// </summary>
    private int BuildDepthOrder(
        double[] depths,
        int rows,
        int cols,
        bool interleave,
        bool exclusive,
        bool[] drawable,
        int[] order,
        int[] groups)
    {
        double[] cellDepth = RenderScratch.Rent(ref _cellDepth, (cols - 1) * (rows - 1), exclusive);

        int n = 0;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int vi = (r * cols) + c;
                if (!drawable[vi])
                {
                    continue;
                }

                order[n] = vi;
                cellDepth[n] = (depths[vi] + depths[vi + 1] + depths[vi + cols] + depths[vi + cols + 1]) / 4;
                n++;
            }
        }

        Array.Sort(cellDepth, order, 0, n);

        int groupCount = 0;
        if (interleave)
        {
            for (int i = 0; i < n; i++)
            {
                groups[groupCount++] = i;
            }
        }
        else
        {
            groups[groupCount++] = 0;
        }

        groups[groupCount] = n;
        return groupCount;
    }

    /// <summary>Walks the ordered cells one group at a time, emitting batched faces then edges.</summary>
    private void EmitCells(
        IRenderContext context,
        Point2D[] points,
        uint[] palette,
        bool[] drawable,
        int[] order,
        int[] groups,
        int groupCount,
        int cols,
        double opacity,
        double edgeOpacity,
        bool colForward,
        bool rowForward,
        bool drawFaces,
        bool drawEdges,
        bool perVertex,
        bool exclusive)
    {
        // A filled surface with no wireframe still traces its own outline. DrawTriangles does not
        // antialias — which is exactly what lets neighbouring cells tile without seams — so the
        // silhouette and the rim of any NaN hole would otherwise be a hard staircase. The rim is
        // skipped for a translucent surface, where stroking over the faces would darken it instead.
        bool outline = drawFaces && !drawEdges && opacity >= 1;

        for (int g = 0; g < groupCount; g++)
        {
            int end = groups[g + 1];
            for (int begin = groups[g]; begin < end; begin += MaxCellsPerBatch)
            {
                int stop = System.Math.Min(end, begin + MaxCellsPerBatch);
                if (drawFaces)
                {
                    EmitFaces(context, points, palette, order, begin, stop, cols, perVertex, exclusive);
                }

                if (drawEdges || outline)
                {
                    EmitEdges(
                        context, points, palette, drawable, order, begin, stop, cols, edgeOpacity,
                        colForward, rowForward, outline, perVertex, exclusive);
                }
            }
        }
    }

    /// <summary>Emits one cell batch as a triangle soup, two triangles per cell.</summary>
    private void EmitFaces(
        IRenderContext context,
        Point2D[] points,
        uint[] palette,
        int[] order,
        int begin,
        int end,
        int cols,
        bool interp,
        bool exclusive)
    {
        int capacity = (end - begin) * 6;
        Point2D[] verts = RenderScratch.Rent(ref _faceVerts, capacity, exclusive);
        uint[] colors = RenderScratch.Rent(ref _faceColors, capacity, exclusive);

        // A single face colour overrides the palette outright, so interpolation has nothing left to
        // interpolate between — which is exactly the opaque sheet a hidden-line mesh wants.
        uint? solid = _faceColor?.WithOpacity(Opacity * _faceAlpha).ToArgb();

        int v = 0;
        for (int i = begin; i < end; i++)
        {
            int i00 = order[i];
            int i10 = i00 + cols;

            uint a = solid ?? palette[i00];
            uint b = a, d = a, e = a;
            if (interp && solid is null)
            {
                b = palette[i00 + 1];
                d = palette[i10 + 1];
                e = palette[i10];
            }

            verts[v] = points[i00];
            colors[v++] = a;
            verts[v] = points[i00 + 1];
            colors[v++] = b;
            verts[v] = points[i10 + 1];
            colors[v++] = d;

            verts[v] = points[i00];
            colors[v++] = a;
            verts[v] = points[i10 + 1];
            colors[v++] = d;
            verts[v] = points[i10];
            colors[v++] = e;
        }

        context.DrawTriangles(verts.AsSpan(0, v), colors.AsSpan(0, v));
    }

    /// <summary>
    /// Emits the grid lines owned by one cell batch. When the edge color is uniform the whole batch
    /// is a single path; a colormap-colored wireframe has to go one cell at a time.
    /// </summary>
    private void EmitEdges(
        IRenderContext context,
        Point2D[] points,
        uint[] palette,
        bool[] drawable,
        int[] order,
        int begin,
        int end,
        int cols,
        double opacity,
        bool colForward,
        bool rowForward,
        bool outline,
        bool interp,
        bool exclusive)
    {
        Point2D[] verts = RenderScratch.Rent(ref _edgeVerts, (end - begin) * 8, exclusive);
        int[] starts = RenderScratch.Rent(ref _edgeStarts, (end - begin) * 4, exclusive);
        bool perCell = outline || (_edgeColor is null && _style == SurfaceStyle.Wireframe);

        if (!perCell)
        {
            Color edge = _edgeColor is { } chosen
                ? chosen.WithOpacity(opacity)
                : Color.FromRgb(0x30, 0x30, 0x30).WithOpacity(opacity * 0.8);
            var style = new LineStyle(edge, _edgeWidth);
            int v = 0;
            int s = 0;
            for (int i = begin; i < end; i++)
            {
                AppendEdges(order[i], cols, colForward, rowForward, outline, drawable, points, verts, starts, ref v, ref s);
            }

            if (s > 0)
            {
                context.DrawPaths(verts.AsSpan(0, v), starts.AsSpan(0, s), closed: false, style, null);
            }

            return;
        }

        for (int i = begin; i < end; i++)
        {
            int vi = order[i];
            int v = 0;
            int s = 0;
            AppendEdges(vi, cols, colForward, rowForward, outline, drawable, points, verts, starts, ref v, ref s);
            if (s == 0)
            {
                continue;
            }

            // The outline traces the surface in its own color, so it softens the silhouette without
            // drawing a line anyone can see; a colormap wireframe is the visible line itself.
            var style = new LineStyle(CellColor(palette, vi, cols, interp), outline ? 1 : _edgeWidth);
            context.DrawPaths(verts.AsSpan(0, v), starts.AsSpan(0, s), closed: false, style, null);
        }
    }

    /// <summary>
    /// Appends the edges this cell is responsible for. Every interior edge is shared by two cells,
    /// and the nearer of the two draws it: letting both draw stroked every interior line twice,
    /// which double-darkened the whole wireframe whenever the surface was translucent. A neighbour
    /// skipped for a NaN corner cannot draw its half, so that edge falls back to this cell — which
    /// is also exactly the set <paramref name="outline"/> selects on its own.
    /// </summary>
    /// <remarks>
    /// Cells along the grid's last row and column do not exist and are false in
    /// <paramref name="drawable"/>, so a neighbour that runs off the right or bottom edge lands on a
    /// false entry and needs no separate bounds test. Only the two directions that would index below
    /// zero are guarded.
    /// </remarks>
    private static void AppendEdges(
        int vi,
        int cols,
        bool colForward,
        bool rowForward,
        bool outline,
        bool[] drawable,
        Point2D[] points,
        Point2D[] verts,
        int[] starts,
        ref int v,
        ref int s)
    {
        int i10 = vi + cols;

        bool left = vi < 1 || !drawable[vi - 1];
        bool right = !drawable[vi + 1];
        bool top = vi < cols || !drawable[vi - cols];
        bool bottom = !drawable[i10];

        if (left || (!outline && colForward))
        {
            AddEdge(points[vi], points[i10], verts, starts, ref v, ref s);
        }

        if (right || (!outline && !colForward))
        {
            AddEdge(points[vi + 1], points[i10 + 1], verts, starts, ref v, ref s);
        }

        if (top || (!outline && rowForward))
        {
            AddEdge(points[vi], points[vi + 1], verts, starts, ref v, ref s);
        }

        if (bottom || (!outline && !rowForward))
        {
            AddEdge(points[i10], points[i10 + 1], verts, starts, ref v, ref s);
        }
    }

    private static void AddEdge(Point2D a, Point2D b, Point2D[] verts, int[] starts, ref int v, ref int s)
    {
        starts[s++] = v;
        verts[v++] = a;
        verts[v++] = b;
    }

    /// <summary>
    /// One color for a whole cell. Under flat shading the palette already holds it; under interp it
    /// holds the four corners, so this averages them back into the color the cell would have had.
    /// </summary>
    private static Color CellColor(uint[] palette, int vi, int cols, bool interp)
    {
        uint packed = palette[vi];
        if (interp)
        {
            uint b = palette[vi + 1];
            uint d = palette[vi + cols + 1];
            uint e = palette[vi + cols];
            packed = ((((packed >> 24) + (b >> 24) + (d >> 24) + (e >> 24)) / 4) << 24)
                | (((((packed >> 16) & 0xFF) + ((b >> 16) & 0xFF) + ((d >> 16) & 0xFF) + ((e >> 16) & 0xFF)) / 4) << 16)
                | (((((packed >> 8) & 0xFF) + ((b >> 8) & 0xFF) + ((d >> 8) & 0xFF) + ((e >> 8) & 0xFF)) / 4) << 8)
                | ((((packed & 0xFF) + (b & 0xFF) + (d & 0xFF) + (e & 0xFF)) / 4) & 0xFF);
        }

        return Unpack(packed);
    }

    /// <summary>
    /// Which cells have four finite corners, keyed by the cell's top-left vertex so that the emit
    /// loops never convert an index back into a row and a column. Entries for the last row and
    /// column stay false because no cell starts there, which doubles as the bounds test the edge
    /// ownership rules need. Cleared only by <see cref="SetData(double[], double[], double[,])"/>.
    /// </summary>
    private bool[] DrawableCells(int rows, int cols)
    {
        bool[]? cached = _drawableCells;
        if (cached is not null)
        {
            return cached;
        }

        var built = new bool[rows * cols];
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                built[(r * cols) + c] = IsCellFinite(r, c);
            }
        }

        // Published only once complete, so a concurrent render sees either nothing or all of it.
        Volatile.Write(ref _drawableCells, built);
        return built;
    }

    /// <summary>
    /// Every color the surface needs, keyed by vertex index: one entry per cell (held at the cell's
    /// top-left vertex) when the surface is flat-colored, one entry per vertex when the colors are
    /// interpolated. Rebuilt when anything it was derived from changes, which is what keeps colormap
    /// sampling out of the frame loop entirely.
    /// </summary>
    private uint[] Palette(int rows, int cols, double colorMin, double colorMax, double opacity, bool perVertex)
    {
        bool logColor = this.LogColorScale();

        // Flat alpha is looked up in the axes' map, so everything that map depends on joins the
        // cache key: a palette kept across a change of ALim would draw the old transparencies.
        double[,]? alphaData = _faceAlphaFlat ? _alphaData : null;
        AlphaLookup alphaLookup = alphaData is null
            ? default
            : this.ResolveAlpha(AlphaResolver.BoundsOf(alphaData));
        var stamp = new AlphaStamp(alphaData, Axes?.AlphaLimits, Axes?.Alphamap, Axes?.AlphaScale);

        PaletteCache? cached = _palette;
        if (cached is not null
            && cached.Matches(_colormap, colorMin, colorMax, opacity, perVertex, logColor, stamp))
        {
            return cached.Colors;
        }

        var built = new uint[rows * cols];

        // The transparency one grid point contributes: its own when alpha data is being drawn,
        // otherwise the surface's single number, which is already folded into opacity.
        double Alpha(int r, int c) =>
            alphaData is null ? opacity : opacity * alphaLookup.Sample(alphaData[r, c]);

        // What the colormap is sampled at. Height is only the default: MATLAB's surf(X, Y, Z, C)
        // colours by C instead, which is how a surface shows a quantity that is not its own shape.
        double[,] source = _cData ?? _z;
        if (_texture is { } texture && texture.Length == rows * cols)
        {
            // A textured surface takes its colours from the picture rather than from the height. The
            // per-cell case reads the cell's own corner rather than averaging four texels, because a
            // texture is meant to be seen as it is and averaging would blur every edge in it.
            for (int i = 0; i < built.Length; i++)
            {
                uint argb = texture[i];
                uint alpha = (uint)Math.Round(((argb >> 24) & 0xFF) * opacity);
                built[i] = (alpha << 24) | (argb & 0x00FFFFFF);
            }

            Volatile.Write(
                ref _palette,
                new PaletteCache(built, _colormap, colorMin, colorMax, opacity, perVertex, logColor, stamp));
            return built;
        }

        if (perVertex)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    built[(r * cols) + c] = _colormap
                        .Sample(source[r, c], colorMin, colorMax, logColor)
                        .WithOpacity(Alpha(r, c))
                        .ToArgb();
                }
            }
        }
        else
        {
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    double mean = (source[r, c] + source[r, c + 1] + source[r + 1, c] + source[r + 1, c + 1]) / 4;
                    built[(r * cols) + c] = _colormap
                        .Sample(mean, colorMin, colorMax, logColor)
                        .WithOpacity(Alpha(r, c))
                        .ToArgb();
                }
            }
        }

        Volatile.Write(
            ref _palette,
            new PaletteCache(built, _colormap, colorMin, colorMax, opacity, perVertex, logColor, stamp));
        return built;
    }

    /// <summary>
    /// The colors and everything they were derived from, held together so that one reference read
    /// gives a consistent answer to "is this still valid" without a lock.
    /// </summary>
    private sealed class PaletteCache
    {
        private readonly Colormap _map;
        private readonly double _min;
        private readonly double _max;
        private readonly double _opacity;
        private readonly bool _perVertex;
        private readonly bool _log;
        private readonly AlphaStamp _alpha;

        public PaletteCache(
            uint[] colors, Colormap map, double min, double max, double opacity, bool perVertex, bool log,
            AlphaStamp alpha)
        {
            Colors = colors;
            _map = map;
            _min = min;
            _max = max;
            _opacity = opacity;
            _perVertex = perVertex;
            _log = log;
            _alpha = alpha;
        }

        public uint[] Colors { get; }

        public bool Matches(
            Colormap map, double min, double max, double opacity, bool perVertex, bool log, AlphaStamp alpha) =>
            ReferenceEquals(_map, map) && _min.Equals(min) && _max.Equals(max)
            && _opacity.Equals(opacity) && _perVertex == perVertex && _log == log && _alpha.Equals(alpha);
    }

    /// <summary>
    /// Everything outside this plot that decides what its alpha data is drawn as. The two tables are
    /// compared by reference, because replacing one is how a script changes it.
    /// </summary>
    private readonly record struct AlphaStamp(
        double[,]? Data, DataRange? Limits, IReadOnlyList<double>? Map, ColorScaleType? Scale)
    {
        public bool Equals(AlphaStamp other) =>
            ReferenceEquals(Data, other.Data) && Limits == other.Limits
            && ReferenceEquals(Map, other.Map) && Scale == other.Scale;

        public override int GetHashCode() => HashCode.Combine(Data, Limits, Map, Scale);
    }

    /// <summary>
    /// Resolves this axes' lights against the camera, or null when nothing is lit — which is the
    /// default and the whole reason lighting costs an unlit figure nothing. A light that follows the
    /// camera has its position read in camera axes (right, up, toward the viewer) and converted here,
    /// which is the one place per frame that has to happen.
    /// </summary>
    private LightSource[]? ResolveLights(Projection3D projection, bool exclusive, out int count) =>
        SceneLights.Resolve(this, _faceLighting, projection, ref _lightScratch, exclusive, out count);

    /// <summary>
    /// Shades every palette entry against the lights, at whatever granularity the palette already
    /// has: one normal per facet when the surface is flat-colored, one per vertex when the colors
    /// interpolate. Never cached — normals live in the projection's normalized cube space, which
    /// moves with the camera and with the axes ranges, and neither raises this plot's invalidation.
    /// </summary>
    /// <remarks>
    /// Working in normalized space rather than data units is what makes the result mean anything: a
    /// surface with X in ones and Z in millions would otherwise have a normal pointing almost
    /// straight along X everywhere, and light like a vertical wall.
    /// </remarks>
    private uint[] LitPalette(
        int rows,
        int cols,
        Projection3D projection,
        uint[] baseColors,
        bool[] drawable,
        ReadOnlySpan<LightSource> lights,
        bool perVertex,
        bool exclusive)
    {
        if (_xGrid is not null)
        {
            return LitPaletteParametric(rows, cols, projection, baseColors, drawable, lights, perVertex, exclusive);
        }

        LightingModel material = Material;
        Color ambient = Axes?.AmbientLightColor ?? Colors.White;
        Vector3D view = projection.ViewDirection;

        uint[] lit = RenderScratch.Rent(ref _litColors, rows * cols, exclusive);
        double[] nx = RenderScratch.Rent(ref _nx, cols, exclusive);
        double[] ny = RenderScratch.Rent(ref _ny, rows, exclusive);
        double[] nz = RenderScratch.Rent(ref _nz, rows * cols, exclusive);

        // The normalization is affine and per axis, so the grid needs one call per row and column,
        // and two calls pin the Z line for the whole matrix -- which matters, because the heights
        // are the one part of this that is O(rows * cols).
        for (int c = 0; c < cols; c++)
        {
            nx[c] = projection.Normalize(_x[c], 0, 0).X;
        }

        for (int r = 0; r < rows; r++)
        {
            ny[r] = projection.Normalize(0, _y[r], 0).Y;
        }

        double zBase = projection.Normalize(0, 0, 0).Z;
        double zSlope = projection.Normalize(0, 0, 1).Z - zBase;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                nz[(r * cols) + c] = zBase + (_z[r, c] * zSlope);
            }
        }

        if (perVertex)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int vi = (r * cols) + c;
                    double z = nz[vi];
                    if (!double.IsFinite(z))
                    {
                        lit[vi] = baseColors[vi];
                        continue;
                    }

                    lit[vi] = material.Shade(
                        Unpack(baseColors[vi]),
                        new Vector3D(nx[c], ny[r], z),
                        VertexNormal(nx, ny, nz, rows, cols, r, c),
                        view,
                        lights, ambient).ToArgb();
                }
            }

            return lit;
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int vi = (r * cols) + c;
                if (!drawable[vi])
                {
                    lit[vi] = baseColors[vi];
                    continue;
                }

                int i10 = vi + cols;
                var a = new Vector3D(nx[c], ny[r], nz[vi]);
                var b = new Vector3D(nx[c + 1], ny[r], nz[vi + 1]);
                var d = new Vector3D(nx[c], ny[r + 1], nz[i10]);
                var e = new Vector3D(nx[c + 1], ny[r + 1], nz[i10 + 1]);

                // The cross of the two diagonals is the facet's normal, and taking them in this
                // order points it along +Z for a level cell rather than into the floor.
                lit[vi] = material.Shade(
                    Unpack(baseColors[vi]),
                    (a + b + d + e) / 4,
                    Vector3D.Cross(b - d, e - a),
                    view,
                    lights, ambient).ToArgb();
            }
        }

        return lit;
    }

    /// <summary>
    /// The same shading for a parametric grid, where a position per row and column no longer exists.
    /// Normals come from the cross product of the two tangents rather than from a height gradient,
    /// which is the only thing available once the surface can fold back over itself.
    /// </summary>
    /// <remarks>
    /// The height-field path is kept rather than generalized away: on a rectilinear grid the slope of
    /// <c>z</c> is a real derivative with a real correction for uneven spacing, while a tangent is a
    /// secant between neighbouring vertices. They agree on an evenly spaced grid and the height-field
    /// one is more faithful where they do not.
    /// </remarks>
    private uint[] LitPaletteParametric(
        int rows,
        int cols,
        Projection3D projection,
        uint[] baseColors,
        bool[] drawable,
        ReadOnlySpan<LightSource> lights,
        bool perVertex,
        bool exclusive)
    {
        LightingModel material = Material;
        Color ambient = Axes?.AmbientLightColor ?? Colors.White;
        Vector3D view = projection.ViewDirection;
        double[,] xg = _xGrid!;
        double[,] yg = _yGrid!;

        uint[] lit = RenderScratch.Rent(ref _litColors, rows * cols, exclusive);
        double[] px = RenderScratch.Rent(ref _pxGrid, rows * cols, exclusive);
        double[] py = RenderScratch.Rent(ref _pyGrid, rows * cols, exclusive);
        double[] pz = RenderScratch.Rent(ref _nz, rows * cols, exclusive);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int vi = (r * cols) + c;
                Vector3D p = projection.Normalize(xg[r, c], yg[r, c], _z[r, c]);
                px[vi] = p.X;
                py[vi] = p.Y;
                pz[vi] = p.Z;
            }
        }

        if (perVertex)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int vi = (r * cols) + c;
                    if (!IsFiniteVertex(px, py, pz, vi))
                    {
                        lit[vi] = baseColors[vi];
                        continue;
                    }

                    lit[vi] = material.Shade(
                        Unpack(baseColors[vi]),
                        new Vector3D(px[vi], py[vi], pz[vi]),
                        ParametricNormal(px, py, pz, rows, cols, r, c),
                        view,
                        lights, ambient).ToArgb();
                }
            }

            return lit;
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int vi = (r * cols) + c;
                if (!drawable[vi])
                {
                    lit[vi] = baseColors[vi];
                    continue;
                }

                int i10 = vi + cols;
                var a = new Vector3D(px[vi], py[vi], pz[vi]);
                var b = new Vector3D(px[vi + 1], py[vi + 1], pz[vi + 1]);
                var d = new Vector3D(px[i10], py[i10], pz[i10]);
                var e = new Vector3D(px[i10 + 1], py[i10 + 1], pz[i10 + 1]);

                lit[vi] = material.Shade(
                    Unpack(baseColors[vi]),
                    (a + b + d + e) / 4,
                    Vector3D.Cross(b - d, e - a),
                    view,
                    lights, ambient).ToArgb();
            }
        }

        return lit;
    }

    /// <summary>
    /// The normal at one vertex of a parametric grid: the cross product of the tangent along the
    /// columns with the tangent along the rows, which reduces to the outward normal of a level cell
    /// and points the same way the height-field normal does on a grid that happens to be rectilinear.
    /// </summary>
    /// <remarks>
    /// Which side "outward" lands on depends on how the caller parameterized the surface, and a
    /// closed shape like a sphere has no answer that is right everywhere. It does not matter: the
    /// shader flips a normal that faces away from the camera, which is MATLAB's <c>reverselit</c>.
    /// </remarks>
    private static Vector3D ParametricNormal(double[] px, double[] py, double[] pz, int rows, int cols, int r, int c)
    {
        int vi = (r * cols) + c;
        Vector3D alongCols = Tangent(px, py, pz, vi, c > 0 ? -1 : 0, c < cols - 1 ? 1 : 0);
        Vector3D alongRows = Tangent(px, py, pz, vi, r > 0 ? -cols : 0, r < rows - 1 ? cols : 0);
        return Vector3D.Cross(alongCols, alongRows);
    }

    /// <summary>
    /// A secant through the neighbours on either side of a vertex, falling back to a one-sided
    /// difference at a border or beside a hole. Offsets of zero mean there is no neighbour that way.
    /// </summary>
    private static Vector3D Tangent(double[] px, double[] py, double[] pz, int vi, int back, int forward)
    {
        int from = back != 0 && IsFiniteVertex(px, py, pz, vi + back) ? vi + back : vi;
        int to = forward != 0 && IsFiniteVertex(px, py, pz, vi + forward) ? vi + forward : vi;
        return from == to
            ? Vector3D.Zero
            : new Vector3D(px[to] - px[from], py[to] - py[from], pz[to] - pz[from]);
    }

    private static bool IsFiniteVertex(double[] px, double[] py, double[] pz, int vi) =>
        double.IsFinite(px[vi]) && double.IsFinite(py[vi]) && double.IsFinite(pz[vi]);

    /// <summary>
    /// The surface normal at one grid vertex, from the slope of the height field in each direction.
    /// For <c>z = f(x, y)</c> the tangents are <c>(1, 0, fx)</c> and <c>(0, 1, fy)</c>, so the normal
    /// is <c>(-fx, -fy, 1)</c> — already pointing up, and never zero.
    /// </summary>
    private static Vector3D VertexNormal(double[] nx, double[] ny, double[] nz, int rows, int cols, int r, int c)
    {
        int vi = (r * cols) + c;
        double z = nz[vi];

        double gx = Slope(
            z,
            c > 0 ? nz[vi - 1] : double.NaN,
            c < cols - 1 ? nz[vi + 1] : double.NaN,
            c > 0 ? nx[c] - nx[c - 1] : 0,
            c < cols - 1 ? nx[c + 1] - nx[c] : 0);

        double gy = Slope(
            z,
            r > 0 ? nz[vi - cols] : double.NaN,
            r < rows - 1 ? nz[vi + cols] : double.NaN,
            r > 0 ? ny[r] - ny[r - 1] : 0,
            r < rows - 1 ? ny[r + 1] - ny[r] : 0);

        return new Vector3D(-gx, -gy, 1);
    }

    /// <summary>
    /// A three-point derivative on an arbitrarily spaced grid — the X and Y vectors are whatever the
    /// caller handed in, so the even-spacing formula would be wrong on a logarithmic sweep. Degrades
    /// to a one-sided difference at a border or next to a NaN, and to flat when neither side is
    /// usable, which is what keeps a hole's rim lit rather than black.
    /// </summary>
    private static double Slope(double v0, double back, double forward, double hBack, double hForward)
    {
        bool hasBack = double.IsFinite(back) && hBack != 0;
        bool hasForward = double.IsFinite(forward) && hForward != 0;

        if (hasBack && hasForward && hBack + hForward != 0)
        {
            return (-hForward / (hBack * (hBack + hForward)) * back)
                + ((hForward - hBack) / (hBack * hForward) * v0)
                + (hBack / (hForward * (hBack + hForward)) * forward);
        }

        if (hasForward)
        {
            return (forward - v0) / hForward;
        }

        return hasBack ? (v0 - back) / hBack : 0;
    }

    private static Color Unpack(uint packed) =>
        Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

    private bool IsCellFinite(int r, int c)
    {
        if (!double.IsFinite(_z[r, c]) || !double.IsFinite(_z[r, c + 1])
            || !double.IsFinite(_z[r + 1, c]) || !double.IsFinite(_z[r + 1, c + 1]))
        {
            return false;
        }

        // A rectilinear grid's positions are checked once by the monotonicity test; a parametric one
        // can carry a NaN anywhere, and a hole punched in X or Y hides the cell just as surely.
        return _xGrid is null
            || (double.IsFinite(_xGrid[r, c]) && double.IsFinite(_xGrid[r, c + 1])
                && double.IsFinite(_xGrid[r + 1, c]) && double.IsFinite(_xGrid[r + 1, c + 1])
                && double.IsFinite(_yGrid![r, c]) && double.IsFinite(_yGrid[r, c + 1])
                && double.IsFinite(_yGrid[r + 1, c]) && double.IsFinite(_yGrid[r + 1, c + 1]));
    }

    /// <summary>
    /// Whether both grid vectors are strictly monotonic, which is what lets the cells be painted in
    /// a plain sweep instead of being depth-sorted. Cached because it only changes with the data.
    /// </summary>
    /// <remarks>
    /// A parametric grid never qualifies. The sweep's correctness rests on a cell only being able to
    /// occlude cells behind it along both grid axes, which is a statement about a height field over a
    /// monotone grid; a sphere's far hemisphere sits behind its near one at the same X and Y.
    /// </remarks>
    private bool GridIsMonotone() =>
        _gridIsMonotone ??= _xGrid is null && IsStrictlyMonotone(_x) && IsStrictlyMonotone(_y);

    private static bool IsStrictlyMonotone(double[] values)
    {
        if (values.Length == 0 || !double.IsFinite(values[0]))
        {
            return false;
        }

        if (values.Length == 1)
        {
            return true;
        }

        bool ascending = values[1] > values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i]) || (ascending ? values[i] <= values[i - 1] : values[i] >= values[i - 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Draws colormap-colored contour lines of the surface on the floor of the axes box (meshc).
    /// The lines themselves are traced once and kept: they live in data space, so a rotate drag only
    /// re-projects them. They used to be re-extracted with a full grid sweep per level on every
    /// frame, which put eight passes over the whole matrix inside the drag loop.
    /// </summary>
    private void DrawFloorContours(
        IRenderContext context,
        Projection3D projection,
        double colorMin,
        double colorMax,
        double opacity,
        bool exclusive)
    {
        DataRange zBounds = GetZDataBounds();
        if (!zBounds.IsValid || zBounds.Max <= zBounds.Min)
        {
            return;
        }

        ContourLineSet lines = FloorContours(zBounds);
        double floor = zBounds.Min;
        Point2D[] verts = RenderScratch.Rent(ref _floorVerts, System.Math.Max(1, lines.MaxLevelVertices), exclusive);
        int[] starts = RenderScratch.Rent(ref _floorStarts, System.Math.Max(1, lines.MaxLevelPaths), exclusive);

        for (int level = 0; level < lines.Levels.Length; level++)
        {
            int paths = lines.PathCount(level);
            if (paths == 0)
            {
                continue;
            }

            int v = 0;
            for (int i = 0; i < paths; i++)
            {
                ReadOnlySpan<Point2D> path = lines.Path(level, i);
                starts[i] = v;
                foreach (Point2D p in path)
                {
                    verts[v++] = projection.ProjectPoint(p.X, p.Y, floor);
                }
            }

            Color color = _colormap.Sample(lines.Levels[level], colorMin, colorMax, this.LogColorScale()).WithOpacity(opacity);
            context.DrawPaths(
                verts.AsSpan(0, v), starts.AsSpan(0, paths), closed: false, new LineStyle(color, 1), null);
        }
    }

    /// <summary>The floor contour geometry, traced only when the data or the level count changes.</summary>
    private ContourLineSet FloorContours(DataRange zBounds)
    {
        double[] levels = _floorLevels is { } previous && previous.Length == _contourLevels
            ? previous
            : new double[_contourLevels];
        for (int i = 0; i < levels.Length; i++)
        {
            levels[i] = zBounds.Min + ((zBounds.Max - zBounds.Min) * (i + 1) / (_contourLevels + 1.0));
        }

        ContourLineSet? cached = _floorContours;
        if (cached is not null && cached.Matches(levels))
        {
            return cached;
        }

        ContourLineSet built = ContourLineSet.Build(_x, _y, _z, levels);
        _floorLevels = levels;
        Volatile.Write(ref _floorContours, built);
        return built;
    }

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor)
        {
            return _colorMin < _colorMax ? (_colorMin, _colorMax) : (_colorMin, _colorMin + 1);
        }

        // Autoscaling spans what the colour is read from, which is C when there is one. Spanning Z
        // instead would map every value of C onto whatever part of the map Z happened to occupy.
        DataRange bounds = (_cData is null ? GetZDataBounds() : MatrixBounds(_cData)).EnsureValid();
        return (bounds.Min, bounds.Max);
    }

    private static DataRange VectorBounds(double[] values)
    {
        DataRange bounds = DataRange.Empty;
        foreach (double v in values)
        {
            if (double.IsFinite(v))
            {
                bounds = bounds.Include(v);
            }
        }

        return bounds;
    }

    private static DataRange MatrixBounds(double[,] values)
    {
        DataRange bounds = DataRange.Empty;
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double v = values[r, c];
                if (double.IsFinite(v))
                {
                    bounds = bounds.Include(v);
                }
            }
        }

        return bounds;
    }

    private static double[] Ramp(int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = i;
        }

        return values;
    }
}
