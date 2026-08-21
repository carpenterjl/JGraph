using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How the bars of a <see cref="Bar3DPlot"/> are laid out over the floor.</summary>
public enum Bar3DStyle
{
    /// <summary>
    /// A bar per matrix entry, standing apart from its neighbours in both directions (MATLAB's
    /// <c>'detached'</c>, and the default).
    /// </summary>
    Detached,

    /// <summary>
    /// A bar per matrix entry, widened until the bars of a row touch along the column direction
    /// (MATLAB's <c>'grouped'</c>).
    /// </summary>
    Grouped,

    /// <summary>
    /// One bar per row, its columns stacked on top of one another (MATLAB's <c>'stacked'</c>).
    /// </summary>
    Stacked,
}

/// <summary>
/// One box of a <see cref="Bar3DPlot"/>, in world coordinates — already through the horizontal
/// swap, so a script and a renderer both read it the same way.
/// </summary>
/// <param name="Row">Which row of the matrix the bar came from.</param>
/// <param name="Column">Which column of the matrix the bar came from.</param>
/// <param name="XMin">The near wall along X.</param>
/// <param name="XMax">The far wall along X.</param>
/// <param name="YMin">The near wall along Y.</param>
/// <param name="YMax">The far wall along Y.</param>
/// <param name="ZMin">The foot of the bar.</param>
/// <param name="ZMax">The top of the bar.</param>
public readonly record struct Bar3DBox(
    int Row,
    int Column,
    double XMin,
    double XMax,
    double YMin,
    double YMax,
    double ZMin,
    double ZMax);

/// <summary>
/// A three-dimensional bar chart (MATLAB <c>bar3</c> and <c>bar3h</c>): a matrix drawn as a field of
/// boxes standing on the floor, one per entry, coloured by the column they came from.
///
/// <para>
/// The whole matrix is one plot rather than one plot per column. MATLAB hands back a surface object
/// per column and this hands back a single handle — a deliberate divergence, and the same one
/// <c>tetramesh</c> makes: the boxes are drawn back to front by a painter's algorithm, and a depth
/// sort is only correct when it can see every face at once.
/// </para>
///
/// <para>
/// The faces are shaded by which way they point — the top of a box brightest, the sides stepped down
/// from it — because a box painted one flat colour reads as a hexagon rather than as a solid. That
/// shading is fixed rather than lit: this pipeline has no light model, and a fixed step is honest
/// about being a legibility device rather than a rendering of anything.
/// </para>
/// </summary>
public sealed class Bar3DPlot : PlotObject, I3DDrawable, IHasZData, ILegendItem
{
    /// <summary>How much of a face's colour survives, per face direction. Top first.</summary>
    private const double TopShade = 1.0;
    private const double BottomShade = 0.5;
    private const double XFaceShade = 0.82;
    private const double YFaceShade = 0.66;

    private double[,] _z;
    private double[]? _rowPositions;

    private Bar3DStyle _style = Bar3DStyle.Detached;
    private bool _horizontal;
    private double _barWidth = 0.8;
    private double _baseline;

    private Color? _faceColor;
    private Color? _edgeColor = Colors.Black;
    private double _lineWidth = 0.5;
    private double _faceAlpha = 1.0;
    private Colormap _colormap = Colormap.Parula;

    private Point2D[] _face = new Point2D[4];
    private double[] _faceDepths = [];
    private int[] _faceOrder = [];

    public Bar3DPlot(double[,] z)
    {
        _z = z ?? throw new ArgumentNullException(nameof(z));
        Name = "Bar3D";
    }

    /// <summary>The matrix of bar heights: one row per floor position, one column per series.</summary>
    [Browsable(false)]
    public double[,] ZData
    {
        get => _z;
        set => SetProperty(ref _z, value ?? throw new ArgumentNullException(nameof(value)),
            InvalidationKind.Layout);
    }

    /// <summary>
    /// Where each row sits along the floor, or null for the counting numbers. MATLAB's
    /// <c>bar3(y, Z)</c> names them; <c>bar3(Z)</c> does not.
    /// </summary>
    [Browsable(false)]
    public double[]? RowPositions
    {
        get => _rowPositions;
        set
        {
            if (value is not null && value.Length != _z.GetLength(0))
            {
                throw new ArgumentException(
                    $"A 3D bar chart needs one row position per row ({_z.GetLength(0)}), "
                        + $"but got {value.Length}.",
                    nameof(value));
            }

            SetProperty(ref _rowPositions, value, InvalidationKind.Layout);
        }
    }

    [Category("Appearance")]
    public Bar3DStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Layout);
    }

    /// <summary>Whether the bars lie along X instead of standing along Z (MATLAB <c>bar3h</c>).</summary>
    [Category("Appearance")]
    public bool Horizontal
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Layout);
    }

    /// <summary>How much of its slot a bar fills, in [0, 1].</summary>
    [Category("Appearance"), DisplayName("Bar width")]
    public double BarWidth
    {
        get => _barWidth;
        set => SetProperty(ref _barWidth, System.Math.Clamp(value, 0, 1), InvalidationKind.Layout);
    }

    /// <summary>The height the bars stand from (usually 0).</summary>
    [Category("Appearance")]
    public double Baseline
    {
        get => _baseline;
        set => SetProperty(ref _baseline, value, InvalidationKind.Layout);
    }

    /// <summary>One colour for every bar, or null to colour them by column from the colormap.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>The colour the box edges are drawn in, or null for no edges.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The colormap the columns are coloured from, spread evenly across them.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>
    /// The boxes the chart is made of, in world coordinates and in row-then-column order. A bar
    /// whose height is not a finite number is left out entirely, which is how a gap in the data
    /// becomes a gap in the chart rather than a box of no height.
    /// </summary>
    public IReadOnlyList<Bar3DBox> Boxes()
    {
        int rows = _z.GetLength(0);
        int columns = _z.GetLength(1);
        var boxes = new List<Bar3DBox>(rows * columns);

        // A grouped chart widens the bars until they meet along the column direction; a stacked one
        // has a single column of boxes, so the slot it fills is the whole of one unit.
        double width = _style == Bar3DStyle.Grouped ? 1.0 : _barWidth;

        for (int r = 0; r < rows; r++)
        {
            double v = _rowPositions is { } positions ? positions[r] : r + 1;
            if (!double.IsFinite(v))
            {
                continue;
            }

            double running = _baseline;
            for (int c = 0; c < columns; c++)
            {
                double height = _z[r, c];
                if (!double.IsFinite(height))
                {
                    continue;
                }

                double u = _style == Bar3DStyle.Stacked ? 1 : c + 1;
                double low, high;
                if (_style == Bar3DStyle.Stacked)
                {
                    low = running;
                    high = running + height;
                    running = high;
                }
                else
                {
                    low = System.Math.Min(_baseline, height);
                    high = System.Math.Max(_baseline, height);
                }

                boxes.Add(Box(r, c, u, width, v, low, high));
            }
        }

        return boxes;
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => Extent(box => (box.XMin, box.XMax));

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => Extent(box => (box.YMin, box.YMax));

    /// <inheritdoc />
    public DataRange GetZDataBounds() => Extent(box => (box.ZMin, box.ZMax));

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(state);

        IReadOnlyList<Bar3DBox> boxes = Boxes();
        if (boxes.Count == 0)
        {
            return;
        }

        int columns = System.Math.Max(_z.GetLength(1), 1);
        Color[] palette = _colormap.Resample(columns);
        LineStyle? stroke = _edgeColor is { } edge && _lineWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity), _lineWidth)
            : null;

        // Every face of every box is sorted together: the boxes interleave in depth as soon as the
        // camera is off an axis, so sorting box by box would put a near face behind a far one.
        int faces = boxes.Count * BoxFaces.Length;
        if (_faceDepths.Length < faces)
        {
            _faceDepths = new double[faces];
            _faceOrder = new int[faces];
        }

        var corners = new Point2D[boxes.Count * 8];
        var depths = new double[boxes.Count * 8];
        for (int b = 0; b < boxes.Count; b++)
        {
            Bar3DBox box = boxes[b];
            for (int corner = 0; corner < 8; corner++)
            {
                (double x, double y, double z) = CornerOf(box, corner);
                (corners[(b * 8) + corner], depths[(b * 8) + corner]) = projection.Project(x, y, z);
            }
        }

        for (int f = 0; f < faces; f++)
        {
            int[] indices = BoxFaces[f % BoxFaces.Length];
            int at = (f / BoxFaces.Length) * 8;
            double sum = 0;
            foreach (int corner in indices)
            {
                sum += depths[at + corner];
            }

            _faceDepths[f] = sum / indices.Length;
            _faceOrder[f] = f;
        }

        // SortMethod 'childorder' paints the faces in the order they are held, so the sort is what
        // is skipped: the arrays already carry that order.
        if (state.DepthSort)
        {
            Array.Sort(_faceDepths, _faceOrder, 0, faces);
        }


        for (int i = 0; i < faces; i++)
        {
            int f = _faceOrder[i];
            int which = f % BoxFaces.Length;
            int b = f / BoxFaces.Length;
            int[] indices = BoxFaces[which];

            bool drawable = true;
            for (int v = 0; v < indices.Length; v++)
            {
                _face[v] = corners[(b * 8) + indices[v]];
                drawable &= _face[v].IsFinite;
            }

            if (!drawable)
            {
                continue;
            }

            Color color = _faceColor ?? palette[boxes[b].Column % palette.Length];
            context.DrawPolygon(
                _face.AsSpan(0, indices.Length),
                stroke,
                Shaded(color, FaceShades[which]).WithOpacity(Opacity * _faceAlpha));
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) => new(
        line: null,
        marker: null,
        swatch: (_faceColor ?? _colormap.Resample(System.Math.Max(_z.GetLength(1), 1))[0])
            .WithOpacity(Opacity * _faceAlpha));

    /// <summary>The corners of each face, as indices into the eight corners of a box.</summary>
    private static readonly int[][] BoxFaces =
    [
        [4, 5, 7, 6],   // top    (z max)
        [0, 1, 3, 2],   // bottom (z min)
        [0, 1, 5, 4],   // front  (y min)
        [2, 3, 7, 6],   // back   (y max)
        [0, 2, 6, 4],   // left   (x min)
        [1, 3, 7, 5],   // right  (x max)
    ];

    /// <summary>How much of the bar's colour each of those faces keeps, in the same order.</summary>
    private static readonly double[] FaceShades =
        [TopShade, BottomShade, YFaceShade, YFaceShade, XFaceShade, XFaceShade];

    private static Color Shaded(Color color, double keep) =>
        keep >= 1 ? color : Color.Lerp(color, Colors.Black, 1 - keep);

    /// <summary>
    /// One box, placed from the slot it fills. <paramref name="u"/> is the column direction,
    /// <paramref name="v"/> the row direction and the low/high pair the value — and which world axis
    /// each of those is depends on <see cref="Horizontal"/>, which is the whole of the difference
    /// between <c>bar3</c> and <c>bar3h</c>.
    /// </summary>
    private Bar3DBox Box(int row, int column, double u, double width, double v, double low, double high)
    {
        double half = width / 2;
        double depth = _barWidth / 2;
        return _horizontal
            ? new Bar3DBox(row, column, low, high, v - depth, v + depth, u - half, u + half)
            : new Bar3DBox(row, column, u - half, u + half, v - depth, v + depth, low, high);
    }

    private static (double X, double Y, double Z) CornerOf(Bar3DBox box, int corner) => (
        (corner & 1) == 0 ? box.XMin : box.XMax,
        (corner & 2) == 0 ? box.YMin : box.YMax,
        (corner & 4) == 0 ? box.ZMin : box.ZMax);

    private DataRange Extent(Func<Bar3DBox, (double Low, double High)> reach)
    {
        DataRange bounds = DataRange.Empty;
        foreach (Bar3DBox box in Boxes())
        {
            (double low, double high) = reach(box);
            bounds = bounds.Include(low).Include(high);
        }

        return bounds;
    }
}
