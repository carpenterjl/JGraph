using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Contours;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A 2D contour plot (MATLAB <c>contour</c>/<c>contourf</c>): iso-lines — or filled bands — of a
/// scalar field <c>Z[row, col]</c> sampled at <c>X[col]</c>/<c>Y[row]</c>, colored through a
/// <see cref="Colormap"/> by level. Lives in a normal 2D axes; the contour geometry comes from
/// <see cref="MarchingSquares"/> and is drawn in data space through the standard coordinate mapper.
/// </summary>
public sealed class ContourPlot : PlotObject, IDrawable, IColorMapped
{
    private double[,] _z;
    private double[] _x;
    private double[] _y;
    private double[]? _levels;
    private bool _filled;
    private Colormap _colormap = Colormap.Viridis;
    private double _lineWidth = 1.5;
    private int _levelCount = 8;

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

    /// <inheritdoc />
    public (double Min, double Max) ColorRange
    {
        get
        {
            DataRange bounds = ZBounds().EnsureValid();
            return (bounds.Min, bounds.Max);
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => VectorBounds(_x);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => VectorBounds(_y);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        DataRange zBounds = ZBounds();
        if (!zBounds.IsValid || zBounds.Max <= zBounds.Min)
        {
            return;
        }

        double[] levels = ResolveLevels(zBounds);
        (double colorMin, double colorMax) = (zBounds.Min, zBounds.Max);
        ICoordinateMapper mapper = state.Mapper;
        double opacity = Opacity;

        if (_filled)
        {
            // Bands between consecutive boundaries [zMin, L1, ..., Ln, zMax], each filled with the
            // colormap sample at the band midpoint.
            var boundaries = new List<double>(levels.Length + 2) { zBounds.Min };
            boundaries.AddRange(levels.Where(l => l > zBounds.Min && l < zBounds.Max));
            boundaries.Add(zBounds.Max);

            for (int b = 0; b < boundaries.Count - 1; b++)
            {
                double lower = boundaries[b];
                double upper = boundaries[b + 1];
                Color fill = _colormap.Sample((lower + upper) / 2, colorMin, colorMax).WithOpacity(opacity);
                var seamStroke = new LineStyle(fill, 1); // self-colored stroke hides anti-aliasing seams between cells
                foreach (Point2D[] polygon in MarchingSquares.FilledCells(_x, _y, _z, lower, upper))
                {
                    var pixels = new Point2D[polygon.Length];
                    for (int i = 0; i < polygon.Length; i++)
                    {
                        pixels[i] = mapper.DataToPixel(polygon[i].X, polygon[i].Y);
                    }

                    context.DrawPolygon(pixels, seamStroke, fill);
                }
            }

            return;
        }

        foreach (double level in levels)
        {
            Color color = _colormap.Sample(level, colorMin, colorMax).WithOpacity(opacity);
            var style = new LineStyle(color, _lineWidth);
            foreach (Point2D[] segment in MarchingSquares.Lines(_x, _y, _z, level))
            {
                context.DrawLine(
                    mapper.DataToPixel(segment[0].X, segment[0].Y),
                    mapper.DataToPixel(segment[1].X, segment[1].Y),
                    style);
            }
        }
    }

    private double[] ResolveLevels(DataRange zBounds)
    {
        if (_levels is { Length: > 0 })
        {
            return _levels;
        }

        // Evenly spaced interior levels, excluding the exact extremes (which produce no geometry).
        var levels = new double[_levelCount];
        for (int i = 0; i < _levelCount; i++)
        {
            levels[i] = zBounds.Min + ((zBounds.Max - zBounds.Min) * (i + 1) / (_levelCount + 1.0));
        }

        return levels;
    }

    private DataRange ZBounds()
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
