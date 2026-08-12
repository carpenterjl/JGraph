using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Maths.Transforms;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>How a <see cref="PolarHistogramPlot"/> draws its bins.</summary>
public enum PolarHistogramDisplayStyle
{
    /// <summary>Each bin is a filled wedge from the middle out to its height.</summary>
    Bar,

    /// <summary>The bins are one unfilled outline round the chart, stepping at each edge.</summary>
    Stairs,
}

/// <summary>
/// A histogram of angles (MATLAB <c>polarhistogram</c>): the circle is cut into angular bins and each
/// one is drawn as a wedge reaching out as far as its height.
/// <para>
/// This is the one angular verb that could not be an ordinary plot handed the polar mapper. A line or
/// a marker is a point wherever it is drawn, so <c>polarplot</c> and <c>polarscatter</c> are
/// <c>plot</c> and <c>scatter</c> on an axes in a different mode; a histogram bar is a <em>rectangle</em>
/// on square paper and a wedge on a circle, and no mapping turns one into the other. So the wedges are
/// this object's own work — but everything else about it is shared: the bins are assigned by
/// <see cref="Binning"/> and the heights read by <see cref="HistogramHeights"/>, which is what makes
/// the counts here and the counts <c>histcounts</c> reports the same numbers rather than two numbers
/// that usually agree.
/// </para>
/// <para>
/// The edges are always concrete — angles in radians, ascending. <see cref="NumBins"/>,
/// <see cref="BinWidth"/> and <see cref="BinLimits"/> are ways of writing a new set of them, and each
/// re-counts <see cref="Data"/> as it goes, so a script that widens the bins on a handle sees the
/// chart follow. Choosing the edges in the first place is the verb's job, because the rule that picks
/// them is <c>histcounts</c>' rule and it lives with <c>histcounts</c>.
/// </para>
/// </summary>
public sealed class PolarHistogramPlot : PlotObject, IDrawable, ILegendItem
{
    private double[] _data;
    private double[] _binEdges;
    private double[] _binCounts;
    private HistogramNormalization _normalization = HistogramNormalization.Count;
    private PolarHistogramDisplayStyle _displayStyle = PolarHistogramDisplayStyle.Bar;
    private Color? _faceColor;
    private Color? _edgeColor;
    private double _faceAlpha = 1.0;
    private double _edgeAlpha = 1.0;
    private double _lineWidth = 0.5;
    private DashStyle _lineStyle = DashStyle.Solid;

    /// <summary>
    /// Creates a histogram of <paramref name="data"/> — angles in radians — over the given bin edges,
    /// counting as it goes.
    /// </summary>
    public PolarHistogramPlot(double[] data, double[] binEdges)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _binEdges = Checked(binEdges);
        _binCounts = Binning.Counts(_data, _binEdges);
        Name = "Polar histogram";
    }

    /// <summary>
    /// A histogram of counts somebody else has already taken — MATLAB's
    /// <c>polarhistogram('BinEdges', e, 'BinCounts', n)</c>. There is no data behind it, so re-binning
    /// it has nothing to count.
    /// </summary>
    public static PolarHistogramPlot FromCounts(double[] binEdges, double[] binCounts) =>
        new([], binEdges) { BinCounts = binCounts };

    /// <summary>The angles the histogram was taken over, in radians, or empty when it was given counts.</summary>
    [Browsable(false)]
    public double[] Data
    {
        get => _data;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _data = value;
            _binCounts = Binning.Counts(_data, _binEdges);
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>
    /// Where the bins begin and end, in radians, ascending. Setting them counts the data again — which
    /// is the whole difference between moving the bins and repainting them.
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

    /// <summary>
    /// The height each bin is drawn at: its count read as <see cref="Normalization"/> says to read it.
    /// </summary>
    [Browsable(false)]
    public double[] BinHeights =>
        HistogramHeights.Scale(_binCounts, _binEdges, _normalization, _data.Length > 0 ? _data.Length : Total());

    /// <summary>How many bins there are. Setting it cuts the current span into that many equal ones.</summary>
    [Category("Appearance"), DisplayName("Bin count")]
    public int NumBins
    {
        get => _binCounts.Length;
        set => BinEdges = Binning.Spanning(_binEdges[0], _binEdges[^1], System.Math.Max(1, value));
    }

    /// <summary>
    /// How wide each bin is, in radians. Setting it keeps the left edge and lays bins of that width
    /// across the span, taking as many whole ones as fit — the span is what the histogram covers, and
    /// widening the bins must not quietly widen that too.
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

            double span = _binEdges[^1] - _binEdges[0];
            NumBins = System.Math.Max(1, (int)System.Math.Round(span / value));
        }
    }

    /// <summary>
    /// The angles the histogram covers — its first and last edge. Setting them re-cuts the same number
    /// of bins across the new span.
    /// </summary>
    [Browsable(false)]
    public double[] BinLimits
    {
        get => [_binEdges[0], _binEdges[^1]];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 2)
            {
                throw new ArgumentException("Bin limits are a low and a high angle.", nameof(value));
            }

            BinEdges = Binning.Spanning(value[0], value[1], _binCounts.Length);
        }
    }

    /// <summary>What a bin's height means.</summary>
    [Category("Appearance")]
    public HistogramNormalization Normalization
    {
        get => _normalization;
        set => SetProperty(ref _normalization, value, InvalidationKind.Data);
    }

    /// <summary>Whether the bins are filled wedges or one outline round the chart.</summary>
    [Category("Appearance"), DisplayName("Display style")]
    public PolarHistogramDisplayStyle DisplayStyle
    {
        get => _displayStyle;
        set => SetProperty(ref _displayStyle, value, InvalidationKind.Render);
    }

    /// <summary>The colour the wedges are filled with, or null to take the series colour.</summary>
    [Category("Appearance"), DisplayName("Face color")]
    public Color? FaceColor
    {
        get => _faceColor;
        set => SetProperty(ref _faceColor, value, InvalidationKind.Render);
    }

    /// <summary>The colour the wedges are outlined in, or null to darken the fill for it.</summary>
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

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The angles the chart covers, which on a polar axes is what the θ ruler is asked to hold.</summary>
    public override DataRange GetXDataBounds() => new(_binEdges[0], _binEdges[^1]);

    /// <summary>
    /// How far out the chart reaches. Zero is included because a wedge is drawn from the middle, so a
    /// ring fitted to the heights alone would start the chart at the shortest bar.
    /// </summary>
    public override DataRange GetYDataBounds()
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

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        double[] heights = BinHeights;
        if (heights.Length == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;

        // The middle of a polar chart is the smallest visible radius, which rlim can put above zero.
        // A wedge starts there and stops at the rim, because a bar drawn past either would be a bar
        // the reader cannot see the length of.
        (double floor, double ceiling) = mapper is PolarTransform polar
            ? (polar.RMin, polar.RMax)
            : (0, double.PositiveInfinity);

        Color fill = (_faceColor ?? state.SeriesColor).WithOpacity(Opacity * _faceAlpha);
        Color edge = (_edgeColor ?? Color.Lerp(_faceColor ?? state.SeriesColor, Colors.Black, 0.25))
            .WithOpacity(Opacity * _edgeAlpha);
        LineStyle? stroke = _lineWidth > 0
            ? new LineStyle(edge, _lineWidth, _lineStyle)
            : null;

        if (_displayStyle == PolarHistogramDisplayStyle.Stairs)
        {
            DrawOutline(context, mapper, heights, floor, ceiling, stroke ?? new LineStyle(edge, 1));
            return;
        }

        var vertices = new List<Point2D>();
        for (int bin = 0; bin < heights.Length; bin++)
        {
            double reach = System.Math.Clamp(heights[bin], floor, ceiling);
            if (!(reach > floor))
            {
                continue;
            }

            vertices.Clear();
            vertices.Add(mapper.DataToPixel(_binEdges[bin], floor));
            Arc(vertices, mapper, _binEdges[bin], _binEdges[bin + 1], reach);
            vertices.Add(mapper.DataToPixel(_binEdges[bin + 1], floor));
            context.DrawPolygon(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices), stroke, fill);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: _faceColor ?? seriesColor);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible)
        {
            return null;
        }

        // Worked out in data space, where a wedge is an angle range and a reach — the same test
        // whatever the chart has been turned to or zoomed to.
        Point2D point = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        double[] heights = BinHeights;
        for (int bin = 0; bin < heights.Length; bin++)
        {
            if (point.Y > heights[bin] || !Covers(bin, point.X))
            {
                continue;
            }

            double middle = (_binEdges[bin] + _binEdges[bin + 1]) / 2;
            return new PlotHitResult(this, new Point2D(middle, heights[bin]), 0, bin);
        }

        return null;
    }

    /// <summary>
    /// Whether an angle falls in a bin. The reading comes back as a bearing in [0, 2π), so a bin
    /// written as −π/4 to π/4 has to be met where the reader is rather than where it was written.
    /// </summary>
    private bool Covers(int bin, double theta)
    {
        double turn = System.Math.Tau;
        double into = theta - _binEdges[bin];
        into -= turn * System.Math.Floor(into / turn);
        return into <= _binEdges[bin + 1] - _binEdges[bin];
    }

    /// <summary>
    /// The stairs style: one polyline round the chart, along the top of each bin and radially between
    /// them, dropping to the floor at either end of the span so the outline is closed even when the
    /// bins cover less than a full turn.
    /// </summary>
    private void DrawOutline(
        IRenderContext context,
        ICoordinateMapper mapper,
        double[] heights,
        double floor,
        double ceiling,
        LineStyle stroke)
    {
        var points = new List<Point2D> { mapper.DataToPixel(_binEdges[0], floor) };
        for (int bin = 0; bin < heights.Length; bin++)
        {
            double reach = System.Math.Clamp(heights[bin], floor, ceiling);
            Arc(points, mapper, _binEdges[bin], _binEdges[bin + 1], reach);
        }

        points.Add(mapper.DataToPixel(_binEdges[^1], floor));
        context.DrawPolyline(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points), stroke);
    }

    /// <summary>
    /// Adds the arc from one angle to another at a fixed radius. It is walked in steps of about two
    /// degrees rather than drawn as a straight line, because the top of a wide bin is a curve and a
    /// chord across it would cut the corner off the reading.
    /// </summary>
    private static void Arc(List<Point2D> into, ICoordinateMapper mapper, double from, double to, double radius)
    {
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Abs(to - from) / (System.Math.PI / 90)));
        for (int step = 0; step <= steps; step++)
        {
            into.Add(mapper.DataToPixel(from + ((to - from) * step / steps), radius));
        }
    }

    /// <summary>The sample size behind a histogram that was handed its counts rather than its data.</summary>
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
