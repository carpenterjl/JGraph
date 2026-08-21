using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A binned scatter (MATLAB <c>binscatter</c>): the readings are counted into a grid of rectangular
/// bins and the grid is drawn as a coloured tile, which is what a scatter of a million points cannot
/// be — every marker after the first few thousand lands on one already drawn, and the picture stops
/// saying how many are underneath.
/// <para>
/// Two recorded divergences. MATLAB rebins as the axes are zoomed, so the picture sharpens as you go
/// in; here the bins are worked out once from the data and stay put, which is what makes
/// <c>XBinEdges</c> answerable at any time and a saved figure identical to the one that was saved.
/// And MATLAB does not document how it chooses the bin count, so an unasked-for one is the
/// square-root choice <see cref="Binning.SquareRootChoice"/> makes.
/// </para>
/// </summary>
public sealed class BinScatterPlot : PlotObject, IDrawable, IColorMapped
{
    /// <summary>The most bins MATLAB allows in one direction, and the most this takes.</summary>
    public const int MaxBinsPerSide = 250;

    private readonly double[] _x;
    private readonly double[] _y;
    private int _numBinsX;
    private int _numBinsY;
    private DataRange? _xLimits;
    private DataRange? _yLimits;
    private bool _showEmptyBins;
    private Colormap _colormap = Colormap.Parula;
    private DataRange? _colorLimits;

    private double[]? _xEdges;
    private double[]? _yEdges;
    private double[,]? _values;
    private uint[]? _pixels;
    private double _builtOpacity = 1;

    /// <summary>Creates a binned scatter over the given readings (the arrays are used directly).</summary>
    public BinScatterPlot(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException(
                $"A binned scatter needs an x and a y for every reading, but got {x.Length} and {y.Length}.",
                nameof(y));
        }

        _x = x;
        _y = y;
        _numBinsX = Binning.SquareRootChoice(x.Length);
        _numBinsY = _numBinsX;
        Name = "BinScatter";
    }

    /// <summary>The x of every reading, as supplied.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> X => _x;

    /// <summary>The y of every reading, as supplied.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Y => _y;

    /// <summary>How many bins the grid is across.</summary>
    [Category("Appearance"), DisplayName("Bins across")]
    public int NumBinsX
    {
        get => _numBinsX;
        set
        {
            if (SetProperty(ref _numBinsX, Held(value), InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
    }

    /// <summary>How many bins the grid is up.</summary>
    [Category("Appearance"), DisplayName("Bins up")]
    public int NumBinsY
    {
        get => _numBinsY;
        set
        {
            if (SetProperty(ref _numBinsY, Held(value), InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
    }

    /// <summary>The span the bins cover across, or null to take it from the readings.</summary>
    [Browsable(false)]
    public DataRange? XLimits
    {
        get => _xLimits;
        set
        {
            if (SetProperty(ref _xLimits, value, InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
    }

    /// <summary>The span the bins cover up, or null to take it from the readings.</summary>
    [Browsable(false)]
    public DataRange? YLimits
    {
        get => _yLimits;
        set
        {
            if (SetProperty(ref _yLimits, value, InvalidationKind.Data))
            {
                DiscardBins();
            }
        }
    }

    /// <summary>
    /// Whether a bin nothing fell in is painted at the bottom of the colormap rather than left
    /// showing the axes through it.
    /// </summary>
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

    /// <summary>The colormap the counts are coloured through.</summary>
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

    /// <summary>The counts at the two ends of the colormap, or null to take them from the grid.</summary>
    [Browsable(false)]
    public DataRange? ColorLimits
    {
        get => _colorLimits;
        set
        {
            if (SetProperty(ref _colorLimits, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>The edges of the bins across (one more than <see cref="NumBinsX"/>).</summary>
    [Browsable(false)]
    public IReadOnlyList<double> XBinEdges
    {
        get
        {
            EnsureBins();
            return _xEdges!;
        }
    }

    /// <summary>The edges of the bins up (one more than <see cref="NumBinsY"/>).</summary>
    [Browsable(false)]
    public IReadOnlyList<double> YBinEdges
    {
        get
        {
            EnsureBins();
            return _yEdges!;
        }
    }

    /// <summary>How many readings fell in each bin, indexed <c>[across, up]</c> as MATLAB answers.</summary>
    [Browsable(false)]
    public double[,] Values
    {
        get
        {
            EnsureBins();
            return _values!;
        }
    }

    /// <inheritdoc />
    (double Min, double Max) IColorMapped.ColorRange
    {
        get
        {
            DataRange limits = EffectiveLimits();
            return (limits.Min, limits.Max);
        }
    }

    /// <inheritdoc />
    bool IColorMapped.HasMappedData => _x.Length > 0;

    /// <summary>
    /// The counts at the two ends of the colormap: the ones that were set, or one reading up to the
    /// fullest bin. The low end is one rather than zero because an empty bin is not drawn at all by
    /// default, and leaving it in would spend a slice of the colormap on nothing.
    /// </summary>
    public DataRange EffectiveLimits()
    {
        if (_colorLimits is { } given && given.Max > given.Min)
        {
            return given;
        }

        EnsureBins();
        double high = 0;
        foreach (double count in _values!)
        {
            if (count > high)
            {
                high = count;
            }
        }

        double low = _showEmptyBins ? 0 : 1;
        return high > low ? new DataRange(low, high) : new DataRange(low, low + 1);
    }

    /// <summary>The colour bin <c>(across, up)</c> is filled with, transparent when it is not drawn.</summary>
    public Color ColorOf(int across, int up)
    {
        EnsureBins();
        return ColorOf(_values![across, up], EffectiveLimits());
    }

    /// <summary>
    /// The colour a count takes against a range that has already been worked out. The range is passed
    /// in rather than looked up because painting the tile asks this once per bin, and finding the
    /// fullest bin is a walk over all of them.
    /// </summary>
    private Color ColorOf(double count, DataRange limits)
    {
        if (count <= 0 && !_showEmptyBins)
        {
            return Color.FromArgb(0, 0, 0, 0);
        }

        return _colormap.Sample(System.Math.Clamp(
            (count - limits.Min) / (limits.Max - limits.Min), 0, 1));
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public override void AdoptAxesDefaults(AxesModel axes)
    {
        if (axes.ResolveColormap() is { } map)
        {
            Colormap = map;
        }
    }

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
    public void Render(IRenderContext context, RenderState state)
    {
        EnsureBins();
        if (_x.Length == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        Point2D first = mapper.DataToPixel(_xEdges![0], _yEdges![0]);
        Point2D last = mapper.DataToPixel(_xEdges[^1], _yEdges[^1]);

        // The tile's first row is drawn along the top edge, so it holds the lowest y only when the
        // ruler puts the lowest y there — which is the usual way up, and not the only one.
        if (_pixels is null || _builtOpacity != Opacity)
        {
            BuildTile(first.Y <= last.Y, first.X <= last.X);
        }

        context.DrawImage(
            _pixels!, _numBinsX, _numBinsY, Rect2D.FromCorners(first, last), interpolate: false);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible || _x.Length == 0)
        {
            return null;
        }

        EnsureBins();
        Point2D point = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        int across = Binning.BinOf(point.X, _xEdges!);
        int up = Binning.BinOf(point.Y, _yEdges!);
        if (across < 0 || up < 0 || (_values![across, up] <= 0 && !_showEmptyBins))
        {
            return null;
        }

        // The centre of the bin is what the reading is reported at, since no one reading is what was
        // hit — and the point index is the bin in column-major order, matching Values.
        return new PlotHitResult(
            this,
            new Point2D((_xEdges![across] + _xEdges[across + 1]) / 2, (_yEdges![up] + _yEdges[up + 1]) / 2),
            0,
            (up * _numBinsX) + across);
    }

    private static int Held(int bins) => System.Math.Clamp(bins, 1, MaxBinsPerSide);

    private void DiscardBins()
    {
        _xEdges = null;
        _yEdges = null;
        _values = null;
        _pixels = null;
    }

    private void EnsureBins()
    {
        if (_values is not null)
        {
            return;
        }

        DataRange across = SpanOf(_xLimits, _x);
        DataRange up = SpanOf(_yLimits, _y);
        _xEdges = Binning.Spanning(across.Min, across.Max, _numBinsX);
        _yEdges = Binning.Spanning(up.Min, up.Max, _numBinsY);
        _values = Binning.Counts2D(_x, _y, _xEdges, _yEdges);
        _pixels = null;
    }

    /// <summary>
    /// The span the bins fill: the one that was set, or the extent of the finite readings. A span
    /// with no width would divide by zero, so a single reading is given half a unit either side —
    /// which is a bin it sits in the middle of rather than on the edge of.
    /// </summary>
    private static DataRange SpanOf(DataRange? given, IReadOnlyList<double> values)
    {
        DataRange span = given is { } limits && limits.Max > limits.Min ? limits : Extent(values);
        return span.Max > span.Min ? span : new DataRange(span.Min - 0.5, span.Min + 0.5);
    }

    /// <summary>
    /// The extent of the finite readings, kept as the two ends rather than as a <see cref="DataRange"/>
    /// built up by inclusion: readings that are all the same value collapse to a range a bounds object
    /// calls invalid, and that case is one the caller widens rather than one it throws away.
    /// </summary>
    private static DataRange Extent(IReadOnlyList<double> values)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                low = System.Math.Min(low, value);
                high = System.Math.Max(high, value);
            }
        }

        return low <= high ? new DataRange(low, high) : new DataRange(0, 1);
    }

    private void BuildTile(bool lowestYFirst, bool lowestXFirst)
    {
        var pixels = new uint[_numBinsX * _numBinsY];
        double opacity = Opacity;
        DataRange limits = EffectiveLimits();

        for (int up = 0; up < _numBinsY; up++)
        {
            int row = (lowestYFirst ? up : _numBinsY - 1 - up) * _numBinsX;
            for (int across = 0; across < _numBinsX; across++)
            {
                int column = lowestXFirst ? across : _numBinsX - 1 - across;
                pixels[row + column] = ColorOf(_values![across, up], limits).WithOpacity(opacity).ToArgb();
            }
        }

        _pixels = pixels;
        _builtOpacity = opacity;
    }
}
