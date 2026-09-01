using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Maths.Transforms;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a <see cref="Histogram2Plot"/> shows its grid of counts.</summary>
public enum Histogram2DisplayStyle
{
    /// <summary>A box standing on the floor per bin, its height the count (MATLAB's default).</summary>
    Bar3,

    /// <summary>The grid seen from above, each bin a coloured rectangle (MATLAB's <c>'tile'</c>).</summary>
    Tile,
}

/// <summary>
/// A bivariate histogram (MATLAB <c>histogram2</c>): pairs of readings counted onto a rectangular
/// grid, drawn either as a field of boxes standing on the floor or as the grid seen from above.
/// </summary>
/// <remarks>
/// <para>
/// The counting is <see cref="Binning"/>'s, which is the same code <c>histcounts2</c> answers from,
/// so the picture and the numbers a script checks it against cannot disagree about which side of an
/// edge a reading falls on. The automatic bin choice divides by the fourth root of the sample count
/// rather than the cube root a one-dimensional histogram uses, because the same readings are spread
/// over bins in two directions at once.
/// </para>
/// <para>
/// One object, two pictures, and the axes changes dimension underneath them: the box field is a 3-D
/// chart and the tile is a flat one, so setting <see cref="DisplayStyle"/> moves the axes into or out
/// of three dimensions. That is the one place this differs in kind from every other chart here, and
/// it is MATLAB's arrangement — <c>histogram2(x, y, 'DisplayStyle', 'tile')</c> gives a flat axes you
/// can put a colorbar beside.
/// </para>
/// <para>
/// A bin nothing fell in is not drawn at all unless <see cref="ShowEmptyBins"/> asks for it, which
/// matters more here than for a one-dimensional histogram: an empty grid cell drawn as a
/// zero-height box still paints its bottom face, and a grid that is mostly empty then reads as a
/// solid floor with a few bumps on it rather than as a scatter.
/// </para>
/// </remarks>
public sealed class Histogram2Plot : PlotObject, IDrawable, I3DDrawable, IColorMapped, IHasZData, ILegendItem
{
    /// <summary>The most bins allowed in one direction, matching <see cref="BinScatterPlot"/>.</summary>
    public const int MaxBinsPerSide = 1024;

    private readonly BoxFieldRenderer _painter = new();
    private readonly double[] _x;
    private readonly double[] _y;

    private double[]? _xEdges;
    private double[]? _yEdges;
    private double[,]? _counts;
    private double[,]? _given;

    private int? _xBins;
    private int? _yBins;
    private double? _xWidth;
    private double? _yWidth;
    private DataRange? _xLimits;
    private DataRange? _yLimits;
    private bool _edgesWereGiven;
    private string _binMethod = "auto";
    private string _normalization = "count";

    private Histogram2DisplayStyle _style = Histogram2DisplayStyle.Bar3;
    private Color? _faceColor;
    private string? _faceColorWord = "auto";
    private Color? _edgeColor = Color.FromRgb(38, 38, 38);
    private double _lineWidth = 0.5;
    private double _faceAlpha = 1;
    private bool _showEmptyBins;
    private Colormap _colormap = Colormap.Parula;

    private uint[]? _pixels;
    private double _builtOpacity = 1;

    /// <summary>Creates a bivariate histogram over the given readings (the arrays are used directly).</summary>
    /// <param name="x">The first coordinate of every reading.</param>
    /// <param name="y">The second coordinate of every reading.</param>
    public Histogram2Plot(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException(
                $"A bivariate histogram needs an x and a y for every reading, but got {x.Length} and {y.Length}.",
                nameof(y));
        }

        _x = x;
        _y = y;
        Name = "Histogram2";
    }

    /// <summary>
    /// Creates a bivariate histogram from counts that were worked out elsewhere, which is MATLAB's
    /// <c>histogram2('XBinEdges', …, 'YBinEdges', …, 'BinCounts', …)</c> form.
    /// </summary>
    /// <param name="xEdges">The bin edges across; one more than the counts are wide.</param>
    /// <param name="yEdges">The bin edges up; one more than the counts are tall.</param>
    /// <param name="counts">The count in each bin, indexed <c>[across, up]</c>.</param>
    public Histogram2Plot(double[] xEdges, double[] yEdges, double[,] counts)
    {
        ArgumentNullException.ThrowIfNull(xEdges);
        ArgumentNullException.ThrowIfNull(yEdges);
        ArgumentNullException.ThrowIfNull(counts);
        if (xEdges.Length != counts.GetLength(0) + 1 || yEdges.Length != counts.GetLength(1) + 1)
        {
            throw new ArgumentException(
                $"A {counts.GetLength(0)}-by-{counts.GetLength(1)} grid of counts needs "
                + $"{counts.GetLength(0) + 1} and {counts.GetLength(1) + 1} edges, but got "
                + $"{xEdges.Length} and {yEdges.Length}.",
                nameof(counts));
        }

        _x = [];
        _y = [];
        _xEdges = xEdges;
        _yEdges = yEdges;
        _given = counts;
        Name = "Histogram2";
    }

    /// <summary>The first coordinate of every reading, as supplied.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> XData => _x;

    /// <summary>The second coordinate of every reading, as supplied.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> YData => _y;

    /// <summary>Whether the grid was handed over already counted rather than worked out from readings.</summary>
    [Browsable(false)]
    public bool CountsWereGiven => _given is not null;

    /// <summary>Whether the boxes stand up or the grid is seen from above.</summary>
    [Category("Appearance"), DisplayName("Display style")]
    public Histogram2DisplayStyle DisplayStyle
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Layout);
    }

    /// <summary>How many bins the grid is across and up.</summary>
    [Category("Appearance"), DisplayName("Bins")]
    public (int Across, int Up) NumBins
    {
        get
        {
            EnsureBins();
            return (_xEdges!.Length - 1, _yEdges!.Length - 1);
        }

        set
        {
            _xBins = Held(value.Across);
            _yBins = Held(value.Up);
            _xWidth = null;
            _yWidth = null;
            _edgesWereGiven = false;
            Rebin();
        }
    }

    /// <summary>The width of a bin across and up, or null to take it from the bin count.</summary>
    [Category("Appearance"), DisplayName("Bin width")]
    public (double Across, double Up)? BinWidth
    {
        get
        {
            EnsureBins();
            return (_xEdges![1] - _xEdges[0], _yEdges![1] - _yEdges[0]);
        }

        set
        {
            _xWidth = value?.Across;
            _yWidth = value?.Up;
            _xBins = null;
            _yBins = null;
            _edgesWereGiven = false;
            Rebin();
        }
    }

    /// <summary>Which automatic rule chooses the bins: <c>auto</c>, <c>scott</c>, <c>fd</c> or <c>integers</c>.</summary>
    [Category("Appearance"), DisplayName("Bin method")]
    public string BinMethod
    {
        get => _binMethod;
        set
        {
            _binMethod = string.IsNullOrEmpty(value) ? "auto" : value;
            _xBins = null;
            _yBins = null;
            _xWidth = null;
            _yWidth = null;
            _edgesWereGiven = false;
            Rebin();
        }
    }

    /// <summary>The span the bins cover across, or null to take it from the readings.</summary>
    [Browsable(false)]
    public DataRange? XBinLimits
    {
        get => _xLimits;
        set
        {
            _xLimits = value;
            Rebin();
        }
    }

    /// <summary>The span the bins cover up, or null to take it from the readings.</summary>
    [Browsable(false)]
    public DataRange? YBinLimits
    {
        get => _yLimits;
        set
        {
            _yLimits = value;
            Rebin();
        }
    }

    /// <summary>
    /// What the heights mean: <c>count</c>, <c>probability</c>, <c>countdensity</c>, <c>pdf</c>,
    /// <c>cumcount</c> or <c>cdf</c>.
    /// </summary>
    [Category("Appearance")]
    public string Normalization
    {
        get => _normalization;
        set
        {
            _normalization = string.IsNullOrEmpty(value) ? "count" : value;
            Rebin();
        }
    }

    /// <summary>The colour every box takes, or null when a word decides it instead.</summary>
    /// <remarks>
    /// Setting a colour clears <see cref="FaceColorWord"/>, because a chart cannot be both painted a
    /// colour and told to work one out. The default is the word rather than a colour: a field of
    /// boxes all one colour hides the very thing the chart is drawn to show, since the height of a
    /// box at the back is exactly what the box in front of it obscures.
    /// </remarks>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set
        {
            _faceColorWord = value is null ? "auto" : null;
            if (SetProperty(ref _faceColor, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>
    /// MATLAB's word for how the faces are coloured — <c>auto</c>, <c>flat</c> or <c>none</c> — or
    /// null when a colour was named instead.
    /// </summary>
    /// <remarks>
    /// <c>auto</c> and <c>flat</c> draw the same picture here: each box takes its own height's place
    /// in the colormap. They are kept apart only so a script reads back the word it wrote, which is
    /// what MATLAB does. <c>none</c> leaves the faces unpainted and the edges standing.
    /// </remarks>
    [Browsable(false)]
    public string? FaceColorWord
    {
        get => _faceColorWord;
        set
        {
            _faceColorWord = value;
            if (value is not null)
            {
                _faceColor = null;
            }

            _pixels = null;
            Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>Whether a word names a way of colouring the faces rather than a colour.</summary>
    /// <param name="word">The word to test.</param>
    /// <returns>True for <c>auto</c>, <c>flat</c> and <c>none</c>.</returns>
    public static bool IsFaceColorWord(string word) =>
        word.Equals("auto", StringComparison.OrdinalIgnoreCase)
        || word.Equals("flat", StringComparison.OrdinalIgnoreCase)
        || word.Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the faces are painted at all.</summary>
    private bool FacesShown => !string.Equals(_faceColorWord, "none", StringComparison.Ordinal);

    /// <summary>The colour the box edges are drawn in, or null for no edges.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    /// <summary>How wide the edges are drawn.</summary>
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>How much of each face's colour survives, in [0, 1].</summary>
    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set
        {
            if (SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>Whether a bin nothing fell in is drawn at all.</summary>
    [Category("Appearance"), DisplayName("Show empty bins")]
    public bool ShowEmptyBins
    {
        get => _showEmptyBins;
        set
        {
            if (SetProperty(ref _showEmptyBins, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>The colormap the heights are coloured through.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set
        {
            if (SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>
    /// Puts the bins exactly where the caller says, rather than at a width or a count the chart works
    /// out — MATLAB's <c>'XBinEdges'</c> and <c>'YBinEdges'</c>, which must be given together because
    /// edges in one direction alone leave the other still to be chosen.
    /// </summary>
    /// <param name="xEdges">The edges across, ascending.</param>
    /// <param name="yEdges">The edges up, ascending.</param>
    public void SetBinEdges(double[] xEdges, double[] yEdges)
    {
        ArgumentNullException.ThrowIfNull(xEdges);
        ArgumentNullException.ThrowIfNull(yEdges);
        if (xEdges.Length < 2 || yEdges.Length < 2)
        {
            throw new ArgumentException(
                "A grid needs at least two edges in each direction, which is one bin.", nameof(xEdges));
        }

        _xEdges = xEdges;
        _yEdges = yEdges;
        _edgesWereGiven = true;
        _xBins = null;
        _yBins = null;
        _xWidth = null;
        _yWidth = null;
        _counts = null;
        _pixels = null;
        Invalidate(InvalidationKind.Data);
    }

    /// <summary>The edges of the bins across (one more than the grid is wide).</summary>
    [Browsable(false)]
    public IReadOnlyList<double> XBinEdges
    {
        get
        {
            EnsureBins();
            return _xEdges!;
        }
    }

    /// <summary>The edges of the bins up (one more than the grid is tall).</summary>
    [Browsable(false)]
    public IReadOnlyList<double> YBinEdges
    {
        get
        {
            EnsureBins();
            return _yEdges!;
        }
    }

    /// <summary>How many readings fell in each bin, indexed <c>[across, up]</c>, before normalization.</summary>
    [Browsable(false)]
    public double[,] BinCounts
    {
        get
        {
            EnsureBins();
            return _given ?? _counts!;
        }
    }

    /// <summary>What each bin is drawn at: the counts, put through <see cref="Normalization"/>.</summary>
    [Browsable(false)]
    public double[,] Values
    {
        get
        {
            EnsureBins();
            return _counts!;
        }
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    (double Min, double Max) IColorMapped.ColorRange
    {
        get
        {
            DataRange limits = HeightRange();
            return (limits.Min, limits.Max);
        }
    }

    /// <inheritdoc />
    bool IColorMapped.HasMappedData => _faceColor is null && FacesShown;

    /// <inheritdoc />
    public override void AdoptAxesDefaults(AxesModel axes)
    {
        if (axes.ResolveColormap() is { } map)
        {
            Colormap = map;
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds()
    {
        EnsureBins();
        return new DataRange(_xEdges![0], _xEdges[^1]);
    }

    /// <inheritdoc />
    public override DataRange GetYDataBounds()
    {
        EnsureBins();
        return new DataRange(_yEdges![0], _yEdges[^1]);
    }

    /// <inheritdoc />
    public DataRange GetZDataBounds()
    {
        DataRange heights = HeightRange();
        return new DataRange(System.Math.Min(0, heights.Min), heights.Max);
    }

    /// <summary>
    /// The boxes the chart is made of, in world coordinates. A bin with nothing in it contributes no
    /// box unless <see cref="ShowEmptyBins"/> asks for one, so an empty grid draws nothing rather
    /// than a floor.
    /// </summary>
    public IReadOnlyList<Bar3DBox> Boxes()
    {
        EnsureBins();
        int across = _counts!.GetLength(0);
        int up = _counts.GetLength(1);
        var boxes = new List<Bar3DBox>(across * up);
        for (int j = 0; j < up; j++)
        {
            for (int i = 0; i < across; i++)
            {
                double height = _counts[i, j];
                if (!double.IsFinite(height) || (height <= 0 && !_showEmptyBins))
                {
                    continue;
                }

                boxes.Add(new Bar3DBox(
                    j, i, _xEdges![i], _xEdges[i + 1], _yEdges![j], _yEdges[j + 1], 0, height));
            }
        }

        return boxes;
    }

    /// <inheritdoc />
    public void Render3D(IRenderContext context, Projection3D projection, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_style != Histogram2DisplayStyle.Bar3)
        {
            return;
        }

        IReadOnlyList<Bar3DBox> boxes = Boxes();
        DataRange limits = HeightRange();
        LineStyle? stroke = _edgeColor is { } edge && _lineWidth > 0
            ? new LineStyle(edge.WithOpacity(Opacity), _lineWidth)
            : null;

        _painter.Render(
            context, projection, state, boxes,
            b => FacesShown
                ? _faceColor ?? ColorOf(boxes[b].ZMax, limits)
                : Color.FromArgb(0, 0, 0, 0),
            stroke,
            Opacity * _faceAlpha);
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        if (_style != Histogram2DisplayStyle.Tile)
        {
            return;
        }

        EnsureBins();
        int across = _counts!.GetLength(0);
        int up = _counts.GetLength(1);
        if (across == 0 || up == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        Point2D first = mapper.DataToPixel(_xEdges![0], _yEdges![0]);
        Point2D last = mapper.DataToPixel(_xEdges[^1], _yEdges[^1]);
        if (_pixels is null || _builtOpacity != Opacity)
        {
            BuildTile(first.Y <= last.Y, first.X <= last.X);
        }

        context.DrawImage(_pixels!, across, up, Rect2D.FromCorners(first, last), interpolate: false);
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) => new(
        line: null,
        marker: null,
        swatch: (_faceColor ?? ColorOf(HeightRange().Max, HeightRange())).WithOpacity(Opacity * _faceAlpha));

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible || _style != Histogram2DisplayStyle.Tile)
        {
            return null;
        }

        EnsureBins();
        Point2D point = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        int across = Binning.BinOf(point.X, _xEdges!);
        int up = Binning.BinOf(point.Y, _yEdges!);
        if (across < 0 || up < 0 || (_counts![across, up] <= 0 && !_showEmptyBins))
        {
            return null;
        }

        return new PlotHitResult(
            this,
            new Point2D((_xEdges![across] + _xEdges[across + 1]) / 2, (_yEdges![up] + _yEdges[up + 1]) / 2),
            0,
            (up * _counts.GetLength(0)) + across);
    }

    /// <summary>The heights at the two ends of the colormap.</summary>
    public DataRange HeightRange()
    {
        EnsureBins();
        double high = 0;
        foreach (double height in _counts!)
        {
            if (double.IsFinite(height) && height > high)
            {
                high = height;
            }
        }

        // Zero, always: a height is measured from the floor, so the bottom of the colormap belongs to
        // an empty bin whether or not one is drawn. A binned scatter starts at one instead, because
        // there the colour is the only thing saying how full a bin is.
        return high > 0 ? new DataRange(0, high) : new DataRange(0, 1);
    }

    private static int Held(int bins) => System.Math.Clamp(bins, 1, MaxBinsPerSide);

    private Color ColorOf(double height, DataRange limits) => _colormap.Sample(
        System.Math.Clamp((height - limits.Min) / (limits.Max - limits.Min), 0, 1));

    private void Rebin()
    {
        // Counts handed over directly are not re-counted: there are no readings to count. Nor are
        // edges the caller named — changing the normalization must not quietly move bins that were
        // asked for by their exact positions.
        if (_given is null && !_edgesWereGiven)
        {
            _xEdges = null;
            _yEdges = null;
        }

        _counts = null;
        _pixels = null;
        Invalidate(InvalidationKind.Data);
    }

    private void EnsureBins()
    {
        if (_counts is not null)
        {
            return;
        }

        _xEdges ??= Binning.EdgesFor(_x, _xBins, _xWidth, LimitsOf(_xLimits), _binMethod, 4);
        _yEdges ??= Binning.EdgesFor(_y, _yBins, _yWidth, LimitsOf(_yLimits), _binMethod, 4);

        double[,] raw = _given ?? Binning.Counts2D(_x, _y, _xEdges, _yEdges);
        _counts = Normalized(raw);
        _pixels = null;
    }

    private static double[]? LimitsOf(DataRange? range) =>
        range is { } limits ? [limits.Min, limits.Max] : null;

    /// <summary>
    /// The counts put through <see cref="Normalization"/>. The cumulative forms run across then up,
    /// which is the order MATLAB accumulates a two-dimensional running total in.
    /// </summary>
    private double[,] Normalized(double[,] counts)
    {
        int across = counts.GetLength(0);
        int up = counts.GetLength(1);
        double total = 0;
        foreach (double count in counts)
        {
            total += count;
        }

        if (_normalization is "count" || total <= 0)
        {
            return counts;
        }

        var scaled = new double[across, up];
        if (_normalization is "cumcount" or "cdf")
        {
            double divisor = _normalization == "cdf" ? total : 1;
            for (int i = 0; i < across; i++)
            {
                for (int j = 0; j < up; j++)
                {
                    double left = i > 0 ? scaled[i - 1, j] : 0;
                    double below = j > 0 ? scaled[i, j - 1] : 0;
                    double corner = i > 0 && j > 0 ? scaled[i - 1, j - 1] : 0;
                    scaled[i, j] = left + below - corner + (counts[i, j] / divisor);
                }
            }

            return scaled;
        }

        for (int i = 0; i < across; i++)
        {
            for (int j = 0; j < up; j++)
            {
                double area = (_xEdges![i + 1] - _xEdges[i]) * (_yEdges![j + 1] - _yEdges[j]);
                scaled[i, j] = _normalization switch
                {
                    "probability" => counts[i, j] / total,
                    "countdensity" => area > 0 ? counts[i, j] / area : 0,
                    "pdf" => area > 0 ? counts[i, j] / (total * area) : 0,
                    _ => counts[i, j],
                };
            }
        }

        return scaled;
    }

    private void BuildTile(bool lowestYFirst, bool lowestXFirst)
    {
        int across = _counts!.GetLength(0);
        int up = _counts.GetLength(1);
        var pixels = new uint[across * up];
        double opacity = Opacity * _faceAlpha;
        DataRange limits = HeightRange();

        for (int j = 0; j < up; j++)
        {
            int row = (lowestYFirst ? j : up - 1 - j) * across;
            for (int i = 0; i < across; i++)
            {
                int column = lowestXFirst ? i : across - 1 - i;
                double height = _counts[i, j];
                Color color = (height <= 0 && !_showEmptyBins) || !FacesShown
                    ? Color.FromArgb(0, 0, 0, 0)
                    : _faceColor ?? ColorOf(height, limits);
                pixels[row + column] = color.WithOpacity(opacity).ToArgb();
            }
        }

        _pixels = pixels;
        _builtOpacity = Opacity;
    }
}
