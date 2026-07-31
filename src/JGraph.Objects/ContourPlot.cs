using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using JGraph.Maths.Transforms;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A contour plot (MATLAB <c>contour</c>/<c>contourf</c>/<c>contour3</c>): iso-lines — or filled
/// bands — of a scalar field <c>Z[row, col]</c> sampled at <c>X[col]</c>/<c>Y[row]</c>, colored
/// through a <see cref="Colormap"/> by level. The contour geometry comes from
/// <see cref="MarchingSquares"/> and lives in data space, which is what lets the same traced curves
/// serve both axes modes.
///
/// In a 2D axes the curves are drawn flat through the coordinate mapper. In a 3D one each curve is
/// lifted to the height of its own level and projected through the camera, which is <c>contour3</c>.
/// A filled contour has no single height per band, so in 3D it draws its iso-lines instead of its
/// bands.
/// </summary>
public sealed class ContourPlot : PlotObject, IDrawable, I3DDrawable, IHasZData, IColorMapped
{
    private double[,] _z;
    private double[] _x;
    private double[] _y;
    private double[]? _levels;
    private bool _filled;
    private Colormap _colormap = Colormap.Parula;
    private double _lineWidth = 1.5;
    private int _levelCount = 8;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;

    // Data-derived caches. The geometry lives in data space, so panning or zooming the axes only
    // re-maps it -- it is re-extracted only when the data or the levels change.
    private DataRange? _zBounds;
    private ContourBands? _bands;
    private double[]? _bandBoundaries;
    private ContourLineSet? _lines;

    // Per-frame scratch, refilled every render (see RenderScratch).
    private Point2D[]? _pixels;
    private int[]? _starts;
    private double[]? _levelScratch;
    private double[]? _boundaryScratch;
    private int _rendering;

    /// <summary>Creates a contour plot of <c>z[row, col]</c> sampled at <c>x[col]</c>/<c>y[row]</c>.</summary>
    public ContourPlot(double[] x, double[] y, double[,] z)
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
        Name = "Contour";
    }

    /// <summary>The grid X positions (one per column of <see cref="Z"/>).</summary>
    [Browsable(false)]
    public double[] X => _x;

    /// <summary>The grid Y positions (one per row of <see cref="Z"/>).</summary>
    [Browsable(false)]
    public double[] Y => _y;

    /// <summary>The scalar field, <c>[row, col]</c> with rows indexing Y.</summary>
    [Browsable(false)]
    public double[,] Z => _z;

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
        _zBounds = null;
        _bands = null;
        _lines = null;
        Invalidate(InvalidationKind.Data);
    }

    /// <summary>Explicit contour levels (ascending); null derives <see cref="LevelCount"/> evenly spaced levels.</summary>
    [Browsable(false)]
    public double[]? Levels
    {
        get => _levels;
        set
        {
            _levels = value;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>The number of automatic levels when <see cref="Levels"/> is null.</summary>
    [Category("Appearance"), DisplayName("Level count")]
    public int LevelCount
    {
        get => _levelCount;
        set => SetProperty(ref _levelCount, System.Math.Clamp(value, 1, 64), InvalidationKind.Render);
    }

    /// <summary>When true, the bands between levels are filled (contourf); otherwise iso-lines are drawn.</summary>
    [Category("Appearance")]
    public bool Filled
    {
        get => _filled;
        set => SetProperty(ref _filled, value, InvalidationKind.Render);
    }

    /// <summary>The colormap levels are colored through.</summary>
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

    /// <summary>The iso-line width.</summary>
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0.1, value), InvalidationKind.Render);
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

    /// <inheritdoc />
    public (double Min, double Max) ColorRange
    {
        get
        {
            if (!_autoScaleColor)
            {
                return _colorMin < _colorMax ? (_colorMin, _colorMax) : (_colorMin, _colorMin + 1);
            }

            DataRange bounds = ZBounds().EnsureValid();
            return (bounds.Min, bounds.Max);
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => VectorBounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => VectorBounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds() => ZBounds();

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        DataRange zBounds = ZBounds();
        if (!zBounds.IsValid || zBounds.Max <= zBounds.Min)
        {
            return;
        }

        (double colorMin, double colorMax) = ColorRange;
        double opacity = Opacity;

        // Scratch buffers live on the instance so a pan allocates nothing; a second concurrent
        // render of the same plot takes its own arrays rather than sharing them.
        bool exclusive = Interlocked.Exchange(ref _rendering, 1) == 0;
        try
        {
            double[] levels = ResolveLevels(zBounds, exclusive);
            if (_filled)
            {
                DrawBands(context, state.Mapper, zBounds, levels, colorMin, colorMax, opacity, exclusive);
            }
            else
            {
                DrawLines(
                    context, (x, y, _) => state.Mapper.DataToPixel(x, y),
                    levels, colorMin, colorMax, opacity, exclusive);
            }
        }
        finally
        {
            if (exclusive)
            {
                Volatile.Write(ref _rendering, 0);
            }
        }
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(projection);
        DataRange zBounds = ZBounds();
        if (!zBounds.IsValid || zBounds.Max <= zBounds.Min)
        {
            return;
        }

        (double colorMin, double colorMax) = ColorRange;
        bool exclusive = Interlocked.Exchange(ref _rendering, 1) == 0;
        try
        {
            // Each curve rides at the height of the level it traces, which is the whole of what
            // makes this contour3 rather than a contour drawn on the floor. Levels go out in
            // ascending order; the curves are hairlines with nothing to occlude, so no depth sort
            // buys anything.
            DrawLines(
                context, (x, y, level) => projection.ProjectPoint(x, y, level),
                ResolveLevels(zBounds, exclusive), colorMin, colorMax, Opacity, exclusive);
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
    /// Draws the filled bands, one <see cref="IRenderContext.DrawPaths"/> call each.
    /// </summary>
    /// <remarks>
    /// A band is thousands of little per-cell polygons that happen to tile, and drawing them one at
    /// a time meant every shared interior edge was antialiased against its neighbour and left a
    /// visible seam. The old fix was to stroke each cell in its own fill color, which hid the seam
    /// on an opaque band and made it <em>worse</em> on a translucent one, since the stroke blended
    /// over the fill twice. Handing Skia all of a band's polygons as sub-paths of one path removes
    /// the problem instead of covering it: the whole band is scan-converted as a unit, so a shared
    /// edge gets full coverage from both sides and there is nothing to stroke over.
    /// </remarks>
    private void DrawBands(
        IRenderContext context,
        ICoordinateMapper mapper,
        DataRange zBounds,
        double[] levels,
        double colorMin,
        double colorMax,
        double opacity,
        bool exclusive)
    {
        // Boundaries are [zMin, L1, ..., Ln, zMax]: one band per gap, filled with the colormap
        // sample at its midpoint.
        double[] boundaries = RenderScratch.Rent(ref _boundaryScratch, levels.Length + 2, exclusive);
        int count = 0;
        boundaries[count++] = zBounds.Min;
        foreach (double level in levels)
        {
            if (level > zBounds.Min && level < zBounds.Max)
            {
                boundaries[count++] = level;
            }
        }

        boundaries[count++] = zBounds.Max;

        ContourBands bands = Bands(boundaries.AsSpan(0, count));
        Point2D[] pixels = RenderScratch.Rent(ref _pixels, System.Math.Max(1, bands.MaxBandVertices), exclusive);
        int[] starts = RenderScratch.Rent(ref _starts, System.Math.Max(1, bands.MaxBandPolygons), exclusive);

        for (int b = 0; b < bands.BandCount; b++)
        {
            int polygons = bands.BandPolygonCount(b);
            if (polygons == 0)
            {
                continue;
            }

            int v = 0;
            for (int i = 0; i < polygons; i++)
            {
                starts[i] = v;
                foreach (Point2D p in bands.BandPolygon(b, i))
                {
                    pixels[v++] = mapper.DataToPixel(p.X, p.Y);
                }
            }

            Color fill = _colormap
                .Sample((boundaries[b] + boundaries[b + 1]) / 2, colorMin, colorMax)
                .WithOpacity(opacity);

            // Bands tile internally now, but each one is still its own path, so the edge one band
            // shares with the next is antialiased from both sides and lets a hairline of background
            // through. Tracing each band's own outline in its own color covers its half; between
            // them the boundary comes out solid. On a translucent contour the outline would blend
            // over the fill and darken the rim, which is the artifact this replaced, so there it is
            // left off and the hairline is accepted instead.
            LineStyle? outline = opacity >= 1 ? new LineStyle(fill, 1) : null;
            context.DrawPaths(pixels.AsSpan(0, v), starts.AsSpan(0, polygons), closed: true, outline, fill);
        }
    }

    /// <summary>
    /// Draws the iso-lines, one <see cref="IRenderContext.DrawPaths"/> call per level over curves
    /// assembled from the loose marching-squares segments. Correctness first and speed second: a
    /// dash pattern restarts at the beginning of every sub-path, so drawing a contour as a few
    /// thousand two-point lines rendered a dashed contour as a uniform row of ticks that ignored the
    /// pattern entirely.
    /// </summary>
    private void DrawLines(
        IRenderContext context,
        Func<double, double, double, Point2D> place,
        double[] levels,
        double colorMin,
        double colorMax,
        double opacity,
        bool exclusive)
    {
        ContourLineSet lines = Lines(levels);
        Point2D[] pixels = RenderScratch.Rent(ref _pixels, System.Math.Max(1, lines.MaxLevelVertices), exclusive);
        int[] starts = RenderScratch.Rent(ref _starts, System.Math.Max(1, lines.MaxLevelPaths), exclusive);

        for (int level = 0; level < levels.Length; level++)
        {
            int paths = lines.PathCount(level);
            if (paths == 0)
            {
                continue;
            }

            int v = 0;
            for (int i = 0; i < paths; i++)
            {
                starts[i] = v;
                foreach (Point2D p in lines.Path(level, i))
                {
                    pixels[v++] = place(p.X, p.Y, levels[level]);
                }
            }

            Color color = _colormap.Sample(levels[level], colorMin, colorMax).WithOpacity(opacity);
            context.DrawPaths(
                pixels.AsSpan(0, v), starts.AsSpan(0, paths), closed: false, new LineStyle(color, _lineWidth), null);
        }
    }

    /// <summary>The band geometry, re-clipped only when the data or the boundaries change.</summary>
    private ContourBands Bands(ReadOnlySpan<double> boundaries)
    {
        ContourBands? cached = _bands;
        if (cached is not null && _bandBoundaries is { } previous && boundaries.SequenceEqual(previous))
        {
            return cached;
        }

        var built = new ContourBands();
        built.Build(_x, _y, _z, boundaries);
        _bandBoundaries = boundaries.ToArray();

        // Published only once complete, so a concurrent render sees a whole set or the old one.
        Volatile.Write(ref _bands, built);
        return built;
    }

    /// <summary>The assembled iso-lines, re-traced only when the data or the levels change.</summary>
    private ContourLineSet Lines(double[] levels)
    {
        ContourLineSet? cached = _lines;
        if (cached is not null && cached.Matches(levels))
        {
            return cached;
        }

        ContourLineSet built = ContourLineSet.Build(_x, _y, _z, levels);
        Volatile.Write(ref _lines, built);
        return built;
    }

    private double[] ResolveLevels(DataRange zBounds, bool exclusive)
    {
        if (_levels is { Length: > 0 })
        {
            return _levels;
        }

        // Evenly spaced interior levels, excluding the exact extremes (which produce no geometry).
        // The caches compare against the levels they were built for by value, so reusing the array
        // costs nothing and keeps a frame from allocating.
        double[] levels = exclusive && _levelScratch is { } cached && cached.Length == _levelCount
            ? cached
            : new double[_levelCount];
        for (int i = 0; i < _levelCount; i++)
        {
            levels[i] = zBounds.Min + ((zBounds.Max - zBounds.Min) * (i + 1) / (_levelCount + 1.0));
        }

        if (exclusive)
        {
            _levelScratch = levels;
        }

        return levels;
    }

    private DataRange ZBounds()
    {
        if (_zBounds is { } cached)
        {
            return cached;
        }

        // An indexed loop, not foreach: the multidimensional-array enumerator is markedly slower,
        // and this walks every sample in the grid.
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
}
