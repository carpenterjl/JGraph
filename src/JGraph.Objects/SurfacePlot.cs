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

/// <summary>
/// A 3D surface over a rectilinear grid (MATLAB <c>surf</c>/<c>mesh</c>/<c>meshc</c>): heights
/// <c>Z[row, col]</c> sampled at <c>X[col]</c>/<c>Y[row]</c>, colored through a <see cref="Colormap"/>
/// by height. Rendered as per-cell quads projected through the axes' camera and depth-sorted
/// (painter's algorithm), so it needs no z-buffer and works on every render backend.
/// </summary>
public sealed class SurfacePlot : PlotObject, I3DDrawable, IHasZData, ILegendItem, IColorMapped
{
    private double[,] _z;
    private double[] _x;
    private double[] _y;
    private Colormap _colormap = Colormap.Viridis;
    private SurfaceStyle _style = SurfaceStyle.FilledWithWireframe;
    private bool _showContourBelow;
    private Color? _edgeColor;
    private double _edgeWidth = 0.75;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;

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

    /// <summary>The grid X positions (one per column of <see cref="Z"/>).</summary>
    [Browsable(false)]
    public double[] X
    {
        get => _x;
        set => SetData(value ?? throw new ArgumentNullException(nameof(value)), _y, _z);
    }

    /// <summary>The grid Y positions (one per row of <see cref="Z"/>).</summary>
    [Browsable(false)]
    public double[] Y
    {
        get => _y;
        set => SetData(_x, value ?? throw new ArgumentNullException(nameof(value)), _z);
    }

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
        Invalidate(InvalidationKind.Data);
    }

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

    /// <summary>Whether cells draw as wireframe (mesh), filled faces, or both (surf).</summary>
    [Category("Appearance")]
    public SurfaceStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Render);
    }

    /// <summary>When true, contour lines of the surface are drawn on the floor of the axes box (MATLAB <c>meshc</c>).</summary>
    [Category("Appearance"), DisplayName("Contour below")]
    public bool ShowContourBelow
    {
        get => _showContourBelow;
        set => SetProperty(ref _showContourBelow, value, InvalidationKind.Render);
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

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(null, null, _colormap.Sample(0.7));

    /// <inheritdoc />
    public (double Min, double Max) ColorRange => ResolveColorRange();

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => VectorBounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => VectorBounds(_y);

    /// <inheritdoc />
    public DataRange GetZDataBounds()
    {
        DataRange bounds = DataRange.Empty;
        foreach (double v in _z)
        {
            if (double.IsFinite(v))
            {
                bounds = bounds.Include(v);
            }
        }

        return bounds;
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        int rows = _y.Length;
        int cols = _x.Length;
        if (rows < 2 || cols < 2)
        {
            return;
        }

        (double colorMin, double colorMax) = ResolveColorRange();
        double opacity = Opacity;

        // Project every grid vertex once.
        var points = new Point2D[rows * cols];
        var depths = new double[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                (Point2D p, double d) = projection.Project(_x[c], _y[r], _z[r, c]);
                points[(r * cols) + c] = p;
                depths[(r * cols) + c] = d;
            }
        }

        // Build the quads and depth-sort them, farthest first (painter's algorithm).
        int cellCount = (rows - 1) * (cols - 1);
        var order = new int[cellCount];
        var cellDepth = new double[cellCount];
        int quadCount = 0;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                if (!double.IsFinite(_z[r, c]) || !double.IsFinite(_z[r, c + 1])
                    || !double.IsFinite(_z[r + 1, c]) || !double.IsFinite(_z[r + 1, c + 1]))
                {
                    continue;
                }

                int cell = (r * (cols - 1)) + c;
                order[quadCount] = cell;
                cellDepth[quadCount] = (depths[(r * cols) + c]
                    + depths[(r * cols) + c + 1]
                    + depths[((r + 1) * cols) + c]
                    + depths[((r + 1) * cols) + c + 1]) / 4;
                quadCount++;
            }
        }

        Array.Sort(cellDepth, order, 0, quadCount);

        if (_showContourBelow)
        {
            DrawFloorContours(context, projection, colorMin, colorMax, opacity);
        }

        bool filled = _style != SurfaceStyle.Wireframe;
        bool edged = _style != SurfaceStyle.Filled;
        Span<Point2D> quad = stackalloc Point2D[4];
        for (int i = 0; i < quadCount; i++)
        {
            int cell = order[i];
            int r = cell / (cols - 1);
            int c = cell % (cols - 1);

            quad[0] = points[(r * cols) + c];
            quad[1] = points[(r * cols) + c + 1];
            quad[2] = points[((r + 1) * cols) + c + 1];
            quad[3] = points[((r + 1) * cols) + c];

            double meanZ = (_z[r, c] + _z[r, c + 1] + _z[r + 1, c] + _z[r + 1, c + 1]) / 4;
            Color faceColor = _colormap.Sample(meanZ, colorMin, colorMax).WithOpacity(opacity);

            Color? fill = filled ? faceColor : null;
            LineStyle? stroke = null;
            if (edged)
            {
                Color edge = _edgeColor
                    ?? (_style == SurfaceStyle.Wireframe ? faceColor : Color.FromRgb(0x30, 0x30, 0x30).WithOpacity(opacity * 0.8));
                stroke = new LineStyle(edge, _edgeWidth);
            }

            context.DrawPolygon(quad, stroke, fill);
        }
    }

    /// <summary>Draws colormap-colored contour lines of the surface on the floor of the axes box (meshc).</summary>
    private void DrawFloorContours(
        IRenderContext context,
        Projection3D projection,
        double colorMin,
        double colorMax,
        double opacity)
    {
        DataRange zBounds = GetZDataBounds();
        if (!zBounds.IsValid || zBounds.Max <= zBounds.Min)
        {
            return;
        }

        double floor = zBounds.Min;
        for (int i = 1; i <= 8; i++)
        {
            double level = zBounds.Min + ((zBounds.Max - zBounds.Min) * i / 9.0);
            Color color = _colormap.Sample(level, colorMin, colorMax).WithOpacity(opacity);
            var style = new LineStyle(color, 1);
            foreach (Point2D[] segment in MarchingSquares.Lines(_x, _y, _z, level))
            {
                context.DrawLine(
                    projection.ProjectPoint(segment[0].X, segment[0].Y, floor),
                    projection.ProjectPoint(segment[1].X, segment[1].Y, floor),
                    style);
            }
        }
    }

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor)
        {
            return _colorMin < _colorMax ? (_colorMin, _colorMax) : (_colorMin, _colorMin + 1);
        }

        DataRange bounds = GetZDataBounds().EnsureValid();
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
