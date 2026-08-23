using System.ComponentModel;
using System.Globalization;
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
    private bool _showText;
    private double[]? _labelLevels;
    private TextStyle? _labelStyle;
    private double _colorMin;
    private double _colorMax = 1;
    private Color? _lineColor;
    private DashStyle _lineDash = DashStyle.Solid;
    private double? _levelStep;
    private double? _textStep;
    private double _labelSpacing = 144;
    private bool _contoursAtZero;
    private bool _xImplied;
    private bool _yImplied;

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

    /// <summary>
    /// When true, each labelled level carries its own value written into a gap in the curve
    /// (MATLAB <c>clabel</c> / the <c>ShowText</c> property).
    /// </summary>
    [Category("Appearance"), DisplayName("Show text")]
    public bool ShowText
    {
        get => _showText;
        set => SetProperty(ref _showText, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Which levels are labelled when <see cref="ShowText"/> is on; null labels every level. A level
    /// is matched to the nearest drawn one, so <c>clabel(C, h, v)</c> works with the values a script
    /// read out of the contour matrix.
    /// </summary>
    [Browsable(false)]
    public double[]? LabelLevels
    {
        get => _labelLevels;
        set
        {
            _labelLevels = value;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>How level labels are drawn, or null to follow each level's own color at 9 point.</summary>
    [Category("Appearance"), DisplayName("Label style")]
    public TextStyle? LabelStyle
    {
        get => _labelStyle;
        set => SetProperty(ref _labelStyle, value, InvalidationKind.Render);
    }

    /// <summary>When true, the bands between levels are filled (contourf); otherwise iso-lines are drawn.</summary>
    [Category("Appearance")]
    public bool Filled
    {
        get => _filled;
        set => SetProperty(ref _filled, value, InvalidationKind.Render);
    }

    /// <summary>
    /// One colour for every curve, or null to colour each by its own level through the colormap —
    /// which is what MATLAB spells <c>LineColor</c> 'flat' and makes the default.
    /// </summary>
    [Category("Appearance"), DisplayName("Line color")]
    public Color? LineColor
    {
        get => _lineColor;
        set => SetProperty(ref _lineColor, value, InvalidationKind.Render);
    }

    /// <summary>The dash pattern of the curves (MATLAB <c>LineStyle</c>).</summary>
    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle LineDash
    {
        get => _lineDash;
        set => SetProperty(ref _lineDash, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The spacing between automatic levels, or null to fit <see cref="LevelCount"/> of them across
    /// the data. Given a step, the levels are the multiples of it that fall inside the data — which
    /// is what makes <c>LevelStep</c> a rounder answer than a count.
    /// </summary>
    [Browsable(false)]
    public double? LevelStep
    {
        get => _levelStep;
        set
        {
            _levelStep = value is { } step && step > 0 && double.IsFinite(step) ? step : null;
            _lines = null;
            _bands = null;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// The spacing between labelled levels, or null to label whichever levels
    /// <see cref="LabelLevels"/> names. Given a step, every level that is a multiple of it is labelled.
    /// </summary>
    [Browsable(false)]
    public double? TextStep
    {
        get => _textStep;
        set
        {
            _textStep = value is { } step && step > 0 && double.IsFinite(step) ? step : null;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// How far apart labels are placed along the curves, in points. A curve shorter than this carries
    /// none, so a wider spacing labels fewer curves.
    /// </summary>
    [Category("Appearance"), DisplayName("Label spacing")]
    public double LabelSpacing
    {
        get => _labelSpacing;
        set => SetProperty(ref _labelSpacing, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>
    /// True when the curves lie on the floor of a 3-D axes rather than each at its own height
    /// (MATLAB <c>ZLocation</c> 'zero'). Only consulted in a 3-D axes; a flat contour is flat either
    /// way.
    /// </summary>
    [Category("Appearance"), DisplayName("Contours at zero")]
    public bool ContoursAtZero
    {
        get => _contoursAtZero;
        set => SetProperty(ref _contoursAtZero, value, InvalidationKind.Render);
    }

    /// <summary>True when the x positions were counted out from the grid rather than given.</summary>
    [Browsable(false)]
    public bool XImplied
    {
        get => _xImplied;
        set => _xImplied = value;
    }

    /// <summary>True when the y positions were counted out from the grid rather than given.</summary>
    [Browsable(false)]
    public bool YImplied
    {
        get => _yImplied;
        set => _yImplied = value;
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

    public override DataRange GetXDataBounds() => VectorBounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => VectorBounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds() => ZBounds();

    /// <summary>
    /// The levels actually drawn: the explicit list if there is one, otherwise the evenly spaced ones
    /// this plot chose for itself. What <c>clabel</c> and the contour matrix have to agree with.
    /// <para>
    /// Not browsable: it is the answer <c>LevelList</c> gives, and an inspector row that recomputed
    /// itself on every read and could not be written would be a worse one than <c>Levels</c>.
    /// </para>
    /// </summary>
    [Browsable(false)]
    public double[] ResolvedLevels => ResolveLevels(ZBounds(), exclusive: false);

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
                    levels, colorMin, colorMax, opacity, exclusive, _showText);
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
            // ZLocation 'zero' lays the whole set on the floor of the box instead, which is how a
            // contour drawn under a surface is read against it.
            bool onFloor = _contoursAtZero;
            DrawLines(
                context, (x, y, level) => projection.ProjectPoint(x, y, onFloor ? 0 : level),
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
                .Sample((boundaries[b] + boundaries[b + 1]) / 2, colorMin, colorMax, this.LogColorScale())
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
        bool exclusive,
        bool label = false)
    {
        ContourLineSet lines = Lines(levels);
        Point2D[] pixels = RenderScratch.Rent(ref _pixels, System.Math.Max(1, lines.MaxLevelVertices), exclusive);

        // One more slot than there are paths: labelling breaks the curve it writes into, which turns
        // one path into two.
        int[] starts = RenderScratch.Rent(ref _starts, System.Math.Max(1, lines.MaxLevelPaths + 1), exclusive);

        for (int level = 0; level < levels.Length; level++)
        {
            int paths = lines.PathCount(level);
            if (paths == 0)
            {
                continue;
            }

            // The longest curve at this level is the one with room for the text — and only if it is
            // long enough for the spacing asked for.
            int labelled = label && IsLabelled(levels[level]) ? LongestPath(lines, level, paths) : -1;
            if (labelled >= 0 && !LongEnoughToLabel(lines, level, labelled))
            {
                labelled = -1;
            }
            int labelFrom = 0;
            int labelTo = 0;

            int v = 0;
            for (int i = 0; i < paths; i++)
            {
                starts[i] = v;
                if (i == labelled)
                {
                    labelFrom = v;
                }

                foreach (Point2D p in lines.Path(level, i))
                {
                    pixels[v++] = place(p.X, p.Y, levels[level]);
                }

                if (i == labelled)
                {
                    labelTo = v;
                }
            }

            // 'flat' — a colour per level out of the map — unless the chart has been given one ink
            // for every curve, which is what LineColor means when it is not the word.
            Color color = (_lineColor
                    ?? _colormap.Sample(levels[level], colorMin, colorMax, this.LogColorScale()))
                .WithOpacity(opacity);
            LevelLabel? text = labelled >= 0
                ? OpenGap(context, pixels, starts, ref v, ref paths, labelled, labelFrom, labelTo, levels[level], color)
                : null;

            context.DrawPaths(
                pixels.AsSpan(0, v),
                starts.AsSpan(0, paths),
                closed: false,
                new LineStyle(color, _lineWidth, _lineDash),
                null);

            if (text is { } drawn)
            {
                context.DrawText(
                    drawn.Text,
                    drawn.At,
                    _labelStyle ?? new TextStyle(color, 9),
                    HorizontalAlignment.Center,
                    VerticalAlignment.Middle,
                    drawn.RotationDegrees);
            }
        }
    }

    /// <summary>Where a level's own value is written, and which way up.</summary>
    private readonly record struct LevelLabel(string Text, Point2D At, double RotationDegrees);

    /// <summary>Whether a level is one of the ones asked for, nearest-match as MATLAB's clabel is.</summary>
    private bool IsLabelled(double level)
    {
        // A step says which levels carry text without naming them one at a time: every level that is
        // a multiple of it. It outranks the list, the same way LevelStep outranks a level count.
        if (_textStep is { } step && step > 0)
        {
            double nearest = System.Math.Round(level / step) * step;
            return System.Math.Abs(nearest - level) <= 1e-9 * System.Math.Max(1, System.Math.Abs(level));
        }

        if (_labelLevels is not { Length: > 0 } wanted)
        {
            return true;
        }

        foreach (double v in wanted)
        {
            if (System.Math.Abs(v - level) <= 1e-9 * System.Math.Max(1, System.Math.Abs(level)))
            {
                return true;
            }
        }

        return false;
    }

    private static int LongestPath(ContourLineSet lines, int level, int paths)
    {
        int best = -1;
        int longest = 4; // Below this there is nowhere to put text without swallowing the curve.
        for (int i = 0; i < paths; i++)
        {
            int length = lines.Path(level, i).Length;
            if (length > longest)
            {
                longest = length;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether the chosen curve is long enough to be worth labelling at the spacing asked for. The
    /// curve is measured in the coordinates it was traced in, so the threshold is scaled by the data
    /// range: a spacing in points is a length on the page, and this is the same judgement made where
    /// the page size is not known.
    /// </summary>
    private bool LongEnoughToLabel(ContourLineSet lines, int level, int path)
    {
        if (_labelSpacing <= 0 || path < 0)
        {
            return path >= 0;
        }

        ReadOnlySpan<Point2D> curve = lines.Path(level, path);
        double length = 0;
        for (int i = 1; i < curve.Length; i++)
        {
            double dx = curve[i].X - curve[i - 1].X;
            double dy = curve[i].Y - curve[i - 1].Y;
            length += System.Math.Sqrt((dx * dx) + (dy * dy));
        }

        // 144 points is MATLAB's default and this build's, and at the default a curve spanning a
        // twentieth of the grid still carries its value; a wider spacing asks for longer curves.
        double span = System.Math.Max(1e-12, Span(_x) + Span(_y));
        return length >= span * (_labelSpacing / 144) / 20;
    }

    private static double Span(double[] values) =>
        values.Length < 2 ? 0 : System.Math.Abs(values[^1] - values[0]);

    /// <summary>
    /// Cuts the text's own width out of the curve so the label sits in the line rather than on top of
    /// it, which is how a contour map is read. The vertices inside the gap are removed from the shared
    /// buffer and the path they belonged to becomes two, so the drawing call below sees only the two
    /// stubs. Returns null — leaving the curve whole — when the curve is too short to give up the room.
    /// </summary>
    private LevelLabel? OpenGap(
        IRenderContext context,
        Point2D[] pixels,
        int[] starts,
        ref int count,
        ref int paths,
        int path,
        int from,
        int to,
        double level,
        Color color)
    {
        string text = level.ToString("G4", CultureInfo.InvariantCulture);
        double half = (context.MeasureText(text, _labelStyle ?? new TextStyle(color, 9)).Width / 2) + 2;

        int middle = (from + to) / 2;
        Point2D at = pixels[middle];

        int a = middle;
        while (a > from && Distance(pixels[a], at) < half)
        {
            a--;
        }

        int b = middle;
        while (b < to - 1 && Distance(pixels[b], at) < half)
        {
            b++;
        }

        if (a == middle || b == middle || b - a < 2)
        {
            return null;
        }

        double rotation = Angle(pixels[a], pixels[b]);

        // Close the buffer over the removed vertices, then split the path in two at the gap.
        int removed = b - a - 1;
        Array.Copy(pixels, b, pixels, a + 1, count - b);
        count -= removed;

        for (int i = paths; i > path + 1; i--)
        {
            starts[i] = starts[i - 1] - removed;
        }

        starts[path + 1] = a + 1;
        paths++;

        return new LevelLabel(text, at, rotation);
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The heading of a gap, turned so the text always reads left to right.</summary>
    private static double Angle(Point2D a, Point2D b)
    {
        double degrees = System.Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / System.Math.PI;
        if (degrees > 90)
        {
            degrees -= 180;
        }
        else if (degrees < -90)
        {
            degrees += 180;
        }

        return degrees;
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

        // A chosen step outranks a chosen count: it says where the levels are rather than how many,
        // so the answer is the multiples of it that fall strictly inside the data. Scratch is not
        // reused here because the count depends on the data range rather than on a fixed field.
        if (_levelStep is { } step && step > 0)
        {
            var stepped = new List<double>();
            double first = System.Math.Ceiling(zBounds.Min / step) * step;
            for (double level = first; level < zBounds.Max; level += step)
            {
                if (level > zBounds.Min)
                {
                    stepped.Add(level);
                }
            }

            if (stepped.Count > 0)
            {
                return [.. stepped];
            }
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
