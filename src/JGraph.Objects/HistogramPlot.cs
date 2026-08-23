using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a <see cref="HistogramPlot"/> scales its bin heights.</summary>
public enum HistogramNormalization
{
    /// <summary>Bar height is the number of samples in the bin.</summary>
    Count,

    /// <summary>Bar height is the fraction of samples in the bin (heights sum to 1).</summary>
    Probability,

    /// <summary>Bar height is count / (N · bin width), so the total area is 1 (a probability density).</summary>
    Density,

    /// <summary>Bar height is the running sample count up to and including the bin.</summary>
    Cumulative,

    /// <summary>Bar height is count / bin width, so a bin twice as wide does not look twice as full.</summary>
    CountDensity,

    /// <summary>Bar height is the running fraction of samples up to and including the bin (a CDF).</summary>
    CumulativeProbability,
}

/// <summary>Whether a histogram is drawn as filled bars or as one outline over the bins.</summary>
public enum HistogramDisplayStyle
{
    /// <summary>Filled bars, one per bin — MATLAB's default.</summary>
    Bar,

    /// <summary>A single stepped outline tracing the bin tops, and no fill.</summary>
    Stairs,
}

/// <summary>Which way a histogram's bars grow.</summary>
public enum HistogramOrientation
{
    /// <summary>Bins along X, counts up — MATLAB's default.</summary>
    Vertical,

    /// <summary>Bins along Y, counts to the right.</summary>
    Horizontal,
}

/// <summary>The order a categorical histogram shows its categories in.</summary>
public enum CategoryDisplayOrder
{
    /// <summary>The order the categories themselves are in.</summary>
    Data,

    /// <summary>Fewest first.</summary>
    Ascend,

    /// <summary>Most first.</summary>
    Descend,
}

/// <summary>
/// What a bin's height means once the counting is done. The six readings are MATLAB's six
/// <c>Normalization</c> words, and they live apart from any one chart because a histogram drawn as
/// bars and one drawn as wedges round a circle have to agree about what <c>'pdf'</c> is.
/// </summary>
public static class HistogramHeights
{
    /// <summary>
    /// The bin heights for <paramref name="counts"/> under <paramref name="normalization"/>.
    /// <paramref name="total"/> is the sample size the fractions are of, which is not the sum of the
    /// counts when some readings fell outside every bin.
    /// </summary>
    public static double[] Scale(
        IReadOnlyList<double> counts,
        IReadOnlyList<double> edges,
        HistogramNormalization normalization,
        double total)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(edges);

        var heights = new double[counts.Count];
        double running = 0;
        for (int i = 0; i < counts.Count; i++)
        {
            // A degenerate bin would divide a density by zero. Treating it as unit width keeps the
            // count itself readable, which is more use than an infinity nobody can plot.
            double width = i + 1 < edges.Count ? edges[i + 1] - edges[i] : 0;
            if (!(width > 0) || !double.IsFinite(width))
            {
                width = 1;
            }

            running += counts[i];
            heights[i] = normalization switch
            {
                HistogramNormalization.CountDensity => counts[i] / width,
                HistogramNormalization.Cumulative => running,
                HistogramNormalization.Probability => total > 0 ? counts[i] / total : 0,
                HistogramNormalization.Density => total > 0 ? counts[i] / (total * width) : 0,
                HistogramNormalization.CumulativeProbability => total > 0 ? running / total : 0,
                _ => counts[i],
            };
        }

        return heights;
    }
}

/// <summary>
/// A histogram (MATLAB <c>histogram</c>): readings grouped into bins and drawn as adjacent bars.
/// <para>
/// Everything about the binning is state a script may write — the edges outright, or a count, a
/// width, limits, or the rule by which they are chosen — and every one of them re-cuts the bins and
/// counts the readings again. That is what makes <c>h.BinWidth = 0.5</c> mean anything after the
/// chart is drawn, and it is why the counting lives here rather than in the verb: the verb sees the
/// numbers once, and the object goes on being asked questions.
/// </para>
/// A histogram given counts outright has no readings behind it, exactly as MATLAB's
/// <c>histogram('BinEdges', e, 'BinCounts', n)</c> has none, and re-binning one has nothing to count.
/// A categorical histogram counts names instead of numbers and stands its bars on the counting
/// numbers, which is what its ruler is then labelled with.
/// </summary>
public sealed class HistogramPlot : PlotObject, IDrawable, ILegendItem
{
    private double[] _data;
    private double[] _binEdges;
    private double[] _binCounts;
    private string[]? _categories;
    private double[] _categoryCounts = [];
    private string _binMethod = "auto";
    private double[]? _binLimits;
    private HistogramNormalization _normalization = HistogramNormalization.Count;
    private HistogramDisplayStyle _displayStyle = HistogramDisplayStyle.Bar;
    private HistogramOrientation _orientation = HistogramOrientation.Vertical;
    private CategoryDisplayOrder _displayOrder = CategoryDisplayOrder.Data;
    private int _numDisplayBins;
    private bool _showOthers;
    private Color? _faceColor;
    private Color? _edgeColor;
    private double _faceAlpha = 1.0;
    private double _edgeAlpha = 1.0;
    private double _lineWidth = 1.0;
    private DashStyle _lineStyle = DashStyle.Solid;
    private double _barWidth = 1.0;

    /// <summary>Creates a histogram over the readings, choosing bins the way MATLAB's 'auto' does.</summary>
    public HistogramPlot(double[] values)
        : this(values, Binning.EdgesFor(values ?? [], null, null, null, "auto"))
    {
    }

    /// <summary>Creates a histogram of the readings cut into a given number of equal bins.</summary>
    public HistogramPlot(double[] values, int bins)
        : this(values, Binning.EdgesFor(values ?? [], System.Math.Max(1, bins), null, null, "auto"))
    {
    }

    /// <summary>Creates a histogram over bin edges somebody chose, counting the readings into them.</summary>
    public HistogramPlot(double[] values, double[] binEdges)
    {
        ArgumentNullException.ThrowIfNull(values);
        _data = values;
        _binEdges = Checked(binEdges);
        _binCounts = Binning.Counts(_data, _binEdges);
        Name = "Histogram";
    }

    /// <summary>
    /// A histogram of counts somebody else has already taken — MATLAB's
    /// <c>histogram('BinEdges', e, 'BinCounts', n)</c>. There is nothing behind it to re-count.
    /// </summary>
    public static HistogramPlot FromCounts(double[] binEdges, double[] binCounts) =>
        new([], binEdges) { BinCounts = binCounts };

    /// <summary>
    /// A histogram over names rather than numbers. The bars stand on the counting numbers, one per
    /// category, which is what lets an ordinary numeric ruler carry them once it is labelled.
    /// </summary>
    public static HistogramPlot FromCategories(string[] categories, double[] counts)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(counts);
        var histogram = new HistogramPlot([], CategoryEdges(categories.Length))
        {
            BinCounts = counts,
        };
        histogram._categories = categories;
        histogram._categoryCounts = counts;
        histogram.ShowCategories();
        return histogram;
    }

    /// <summary>The readings the histogram was taken over, or empty when it was given counts.</summary>
    [Browsable(false)]
    public double[] Data
    {
        get => _data;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _data = value;
            _categories = null;
            Rebin();
        }
    }

    /// <summary>
    /// Where the bins begin and end, ascending. Setting them counts the readings again — which is the
    /// whole difference between moving the bins and repainting them.
    /// </summary>
    [Browsable(false)]
    public double[] BinEdges
    {
        get => _binEdges;
        set
        {
            _binEdges = Checked(value);
            _binCounts = _data.Length > 0
                ? Binning.Counts(_data, _binEdges)
                : Fitted(_binCounts, _binEdges);
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>
    /// How many readings fell in each bin, before <see cref="Normalization"/> has anything to say
    /// about it. Setting them takes the histogram off its data, as MATLAB's counts form is off it.
    /// </summary>
    [Browsable(false)]
    public double[] BinCounts
    {
        get => _binCounts;
        set
        {
            _binCounts = Fitted(value, _binEdges);
            _data = [];
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>The height each bin is drawn at: its count read as <see cref="Normalization"/> says.</summary>
    [Browsable(false)]
    public double[] BinHeights =>
        HistogramHeights.Scale(_binCounts, _binEdges, _normalization, _data.Length > 0 ? _data.Length : Total());

    /// <summary>How many bins there are. Setting it cuts the span into that many equal ones.</summary>
    [Category("Appearance"), DisplayName("Bin count")]
    public int NumBins
    {
        get => _binCounts.Length;
        set
        {
            int bins = System.Math.Max(1, value);
            _binEdges = Checked(_data.Length > 0
                ? Binning.EdgesFor(_data, bins, null, _binLimits, _binMethod)
                : Binning.Spanning(_binEdges[0], _binEdges[^1], bins));
            _binCounts = _data.Length > 0 ? Binning.Counts(_data, _binEdges) : Fitted(_binCounts, _binEdges);
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>
    /// How wide each bin is. Setting it lays bins of that width over the readings, which is a
    /// different question from how many bins there are and is answered by the same one kernel.
    /// </summary>
    [Category("Appearance"), DisplayName("Bin width")]
    public double BinWidth
    {
        get => (_binEdges[^1] - _binEdges[0]) / System.Math.Max(1, _binCounts.Length);
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A bin has a positive width.");
            }

            if (_data.Length > 0)
            {
                BinEdges = Binning.EdgesFor(_data, null, value, _binLimits, _binMethod);
                return;
            }

            NumBins = System.Math.Max(1, (int)System.Math.Round((_binEdges[^1] - _binEdges[0]) / value));
        }
    }

    /// <summary>
    /// The span the histogram covers. Setting it keeps the readings outside out of the counting, which
    /// is what makes it different from setting the axes' limits.
    /// </summary>
    [Browsable(false)]
    public double[] BinLimits
    {
        get => _binLimits ?? [_binEdges[0], _binEdges[^1]];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 2)
            {
                throw new ArgumentException("Bin limits are a low and a high value.", nameof(value));
            }

            _binLimits = [value[0], value[1]];
            Rebin();
        }
    }

    /// <summary>Whether the span was chosen or is whatever the readings happened to cover.</summary>
    [Browsable(false)]
    public bool BinLimitsChosen => _binLimits is not null;

    /// <summary>
    /// The rule the bins are chosen by when nobody names them: MATLAB's 'auto', 'scott', 'fd',
    /// 'integers', 'sturges' or 'sqrt'.
    /// </summary>
    [Browsable(false)]
    public string BinMethod
    {
        get => _binMethod;
        set
        {
            _binMethod = value ?? "auto";
            Rebin();
        }
    }

    /// <summary>
    /// The names counted, in the order they are shown — which is what MATLAB answers, and why a
    /// re-ordering is visible here and not only on the ruler.
    /// </summary>
    [Browsable(false)]
    public string[]? Categories
    {
        get => _categories is null ? null : Displayed().Names;
        set
        {
            _categories = value;
            _categoryCounts = Fitted(_categoryCounts, CategoryEdges(value?.Length ?? 0));
            ShowCategories();
        }
    }

    /// <summary>The names as they were given, whatever order they are being shown in.</summary>
    [Browsable(false)]
    public string[] CategoryNames => _categories ?? [];

    /// <summary>How many readings each named category had, in the order the names were given.</summary>
    [Browsable(false)]
    public double[] CategoryCounts => _categoryCounts;

    /// <summary>Which order a categorical histogram shows its bars in.</summary>
    [Browsable(false)]
    public CategoryDisplayOrder DisplayOrder
    {
        get => _displayOrder;
        set
        {
            if (SetProperty(ref _displayOrder, value, InvalidationKind.Data))
            {
                ShowCategories();
            }
        }
    }

    /// <summary>How many categories are shown; 0 means all of them.</summary>
    [Browsable(false)]
    public int NumDisplayBins
    {
        get => _numDisplayBins;
        set
        {
            if (SetProperty(ref _numDisplayBins, System.Math.Max(0, value), InvalidationKind.Data))
            {
                ShowCategories();
            }
        }
    }

    /// <summary>Whether the categories left out are gathered into one bar at the end.</summary>
    [Browsable(false)]
    public bool ShowOthers
    {
        get => _showOthers;
        set
        {
            if (SetProperty(ref _showOthers, value, InvalidationKind.Data))
            {
                ShowCategories();
            }
        }
    }

    /// <summary>What a bin's height means.</summary>
    [Category("Appearance")]
    public HistogramNormalization Normalization
    {
        get => _normalization;
        set => SetProperty(ref _normalization, value, InvalidationKind.Data);
    }

    /// <summary>Whether the bins are filled bars or one stepped outline over them.</summary>
    [Category("Appearance"), DisplayName("Display style")]
    public HistogramDisplayStyle DisplayStyle
    {
        get => _displayStyle;
        set => SetProperty(ref _displayStyle, value, InvalidationKind.Render);
    }

    /// <summary>Which way the bars grow.</summary>
    [Category("Appearance")]
    public HistogramOrientation Orientation
    {
        get => _orientation;
        set => SetProperty(ref _orientation, value, InvalidationKind.Data);
    }

    /// <summary>Explicit bar fill color, or null to use the auto series color.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>Explicit bar edge color, or null to derive it from the fill color.</summary>
    [Category("Appearance"), DisplayName("Edge color")]
    public Color? EdgeColor
    {
        get => _edgeColor;
        set => SetProperty(ref _edgeColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Face alpha")]
    public double FaceAlpha
    {
        get => _faceAlpha;
        set => SetProperty(ref _faceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Edge alpha")]
    public double EdgeAlpha
    {
        get => _edgeAlpha;
        set => SetProperty(ref _edgeAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>How the outline is dashed.</summary>
    [Category("Appearance"), DisplayName("Line style")]
    public DashStyle LineStyle
    {
        get => _lineStyle;
        set => SetProperty(ref _lineStyle, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How much of its bin each bar fills, from 0 to 1. MATLAB leaves this at 1 for a numeric
    /// histogram, where a gap between bins would be misleading, and lets a categorical one open up.
    /// </summary>
    [Category("Appearance"), DisplayName("Bar width")]
    public double BarWidth
    {
        get => _barWidth;
        set => SetProperty(ref _barWidth, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The number of raw samples.</summary>
    [Browsable(false)]
    public int SampleCount => _data.Length;

    /// <inheritdoc />
    public override DataRange GetXDataBounds() =>
        _orientation == HistogramOrientation.Vertical ? AlongBins() : AcrossBins();

    /// <inheritdoc />
    public override DataRange GetYDataBounds() =>
        _orientation == HistogramOrientation.Vertical ? AcrossBins() : AlongBins();

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        if (_binEdges.Length < 2)
        {
            return;
        }

        Color fill = (_faceColor ?? state.SeriesColor).WithOpacity(Opacity * _faceAlpha);
        Color edge = (_edgeColor ?? Color.Lerp(_faceColor ?? state.SeriesColor, Colors.Black, 0.25))
            .WithOpacity(Opacity * _edgeAlpha);
        LineStyle? stroke = _lineWidth > 0 ? new LineStyle(edge, _lineWidth, _lineStyle) : null;
        double[] heights = BinHeights;

        if (_displayStyle == HistogramDisplayStyle.Stairs)
        {
            DrawOutline(context, state.Mapper, heights, stroke ?? new LineStyle(edge, 1));
            return;
        }

        for (int bin = 0; bin < heights.Length; bin++)
        {
            if (heights[bin] == 0)
            {
                continue;
            }

            context.DrawRectangle(BarAt(state.Mapper, bin, heights[bin]), stroke, fill);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color face = _faceColor ?? seriesColor;
        Color edge = _edgeColor ?? Color.Lerp(face, Colors.Black, 0.25);
        return new LegendKey(
            line: null,
            marker: null,
            swatch: face.WithOpacity(_faceAlpha),
            outline: _lineWidth > 0
                ? new LineStyle(edge.WithOpacity(_edgeAlpha), _lineWidth, _lineStyle)
                : null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        double[] heights = BinHeights;
        for (int bin = 0; bin < heights.Length; bin++)
        {
            if (BarAt(mapper, bin, heights[bin]).Contains(pixelPoint))
            {
                double center = (_binEdges[bin] + _binEdges[bin + 1]) / 2.0;
                return new PlotHitResult(this, new Point2D(center, heights[bin]), 0, bin);
            }
        }

        return null;
    }

    /// <inheritdoc />
    protected override IEnumerable<DataTipRowModel> DefaultDataTipRows()
    {
        yield return new DataTipRowModel("BinEdges", "BinEdges");
        yield return new DataTipRowModel("BinCounts", "BinCounts");
    }

    /// <summary>One bar's rectangle, which is where the orientation and the bar width are read.</summary>
    private Rect2D BarAt(ICoordinateMapper mapper, int bin, double height)
    {
        double low = _binEdges[bin];
        double high = _binEdges[bin + 1];
        if (_barWidth < 1)
        {
            double keep = (high - low) * _barWidth / 2;
            double middle = (low + high) / 2;
            (low, high) = (middle - keep, middle + keep);
        }

        return _orientation == HistogramOrientation.Vertical
            ? Rect2D.FromCorners(mapper.DataToPixel(low, 0), mapper.DataToPixel(high, height))
            : Rect2D.FromCorners(mapper.DataToPixel(0, low), mapper.DataToPixel(height, high));
    }

    /// <summary>
    /// The bins as one stepped outline, closed down to the baseline at each end, which is the figure
    /// MATLAB's <c>'stairs'</c> display style draws instead of the bars.
    /// </summary>
    private void DrawOutline(
        IRenderContext context, ICoordinateMapper mapper, double[] heights, LineStyle stroke)
    {
        var path = new List<Point2D>(((heights.Length + 1) * 2) + 1);
        Point2D At(double along, double across) =>
            _orientation == HistogramOrientation.Vertical
                ? mapper.DataToPixel(along, across)
                : mapper.DataToPixel(across, along);

        path.Add(At(_binEdges[0], 0));
        for (int bin = 0; bin < heights.Length; bin++)
        {
            path.Add(At(_binEdges[bin], heights[bin]));
            path.Add(At(_binEdges[bin + 1], heights[bin]));
        }

        path.Add(At(_binEdges[^1], 0));
        for (int i = 1; i < path.Count; i++)
        {
            context.DrawLine(path[i - 1], path[i], stroke);
        }
    }

    /// <summary>The direction the bins run in — the first edge to the last.</summary>
    private DataRange AlongBins() => new(_binEdges[0], _binEdges[^1]);

    /// <summary>The direction the counts run in, which always includes zero because a bar starts there.</summary>
    private DataRange AcrossBins()
    {
        DataRange bounds = new(0, 0);
        foreach (double height in BinHeights)
        {
            if (double.IsFinite(height))
            {
                bounds = bounds.Include(height);
            }
        }

        return bounds;
    }

    /// <summary>Cuts the bins again by whatever rule is in force, and counts the readings into them.</summary>
    private void Rebin()
    {
        if (_data.Length == 0)
        {
            Invalidate(InvalidationKind.Data);
            return;
        }

        _binEdges = Checked(Binning.EdgesFor(_data, null, null, _binLimits, _binMethod));
        _binCounts = Binning.Counts(_data, _binEdges);
        Invalidate(InvalidationKind.Data);
    }

    /// <summary>
    /// Writes the category names onto the ruler the bars stand on, in whatever order and however many
    /// of them are being shown. A categorical histogram is bars on the counting numbers, and this is
    /// the one thing that makes those numbers readable — so re-ordering or trimming re-labels too.
    /// </summary>
    private void ShowCategories()
    {
        if (_categories is null)
        {
            return;
        }

        (string[] names, double[] counts) = Displayed();
        _binEdges = CategoryEdges(names.Length);
        _binCounts = counts;
        Invalidate(InvalidationKind.Data);

        if (Axes is not { } axes)
        {
            return;
        }

        AxisModel ruler = _orientation == HistogramOrientation.Vertical
            ? axes.PrimaryXAxis
            : axes.PrimaryYAxis;
        var positions = new double[names.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = i + 1;
        }

        ruler.TickPositions = positions;
        ruler.TickLabelOverrides = names;
    }

    /// <summary>
    /// The categories as they are to be shown: re-ordered, trimmed, and gathered if asked. It always
    /// works from the names and counts as they were given, so asking twice cannot sort a sorted list
    /// again and land somewhere else.
    /// </summary>
    private (string[] Names, double[] Counts) Displayed()
    {
        string[] names = _categories!;
        double[] counts = Fitted(_categoryCounts, CategoryEdges(names.Length));

        int[] order = [.. Enumerable.Range(0, names.Length)];
        if (_displayOrder != CategoryDisplayOrder.Data)
        {
            int sign = _displayOrder == CategoryDisplayOrder.Ascend ? 1 : -1;
            Array.Sort(order, (a, b) => sign * counts[a].CompareTo(counts[b]));
        }

        int shown = _numDisplayBins > 0 ? System.Math.Min(_numDisplayBins, order.Length) : order.Length;
        var keptNames = new List<string>(shown + 1);
        var keptCounts = new List<double>(shown + 1);
        for (int i = 0; i < shown; i++)
        {
            keptNames.Add(names[order[i]]);
            keptCounts.Add(counts[order[i]]);
        }

        if (_showOthers && shown < order.Length)
        {
            double rest = 0;
            for (int i = shown; i < order.Length; i++)
            {
                rest += counts[order[i]];
            }

            keptNames.Add("Others");
            keptCounts.Add(rest);
        }

        return ([.. keptNames], [.. keptCounts]);
    }

    /// <inheritdoc />
    public override void AdoptAxesDefaults(AxesModel axes) => ShowCategories();

    /// <summary>Bins half a step either side of each counting number, so bar i stands on i.</summary>
    private static double[] CategoryEdges(int count)
    {
        var edges = new double[System.Math.Max(1, count) + 1];
        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = i + 0.5;
        }

        return edges;
    }

    private double Total()
    {
        double total = 0;
        foreach (double count in _binCounts)
        {
            total += count;
        }

        return total;
    }

    /// <summary>Edges that describe at least one bin and never turn back on themselves.</summary>
    private static double[] Checked(double[] edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Length < 2)
        {
            throw new ArgumentException("A histogram needs at least two bin edges.", nameof(edges));
        }

        for (int i = 1; i < edges.Length; i++)
        {
            if (!(edges[i] > edges[i - 1]))
            {
                throw new ArgumentException("Bin edges rise from one to the next.", nameof(edges));
            }
        }

        return edges;
    }

    /// <summary>Counts padded or trimmed to the bins there are, so the two can never disagree in length.</summary>
    private static double[] Fitted(double[] counts, double[] edges)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var fitted = new double[edges.Length - 1];
        Array.Copy(counts, fitted, System.Math.Min(counts.Length, fitted.Length));
        return fitted;
    }
}
