using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A box chart (MATLAB <c>boxchart</c>): one box and pair of whiskers per group of observations,
/// with everything past the whiskers drawn as a marker.
/// <para>
/// The plot holds the observations, not the summary. <see cref="YData"/> is every value and
/// <see cref="XData"/> says which group each one falls in, so changing either redraws the boxes from
/// the data rather than from numbers a caller worked out once — which is what lets a script move a
/// point and see the median follow it. The quartile arithmetic itself is
/// <see cref="Quartiles.Summarize"/>, shared with anything else that needs a five-number summary.
/// </para>
/// <para>
/// Several box charts on one axes share the slot at each position and stand side by side in it,
/// which is how MATLAB draws <c>GroupByColor</c> and what makes <c>hold on</c> do the useful thing.
/// A box chart works that out from its siblings at draw time rather than being told its place, so
/// adding one re-spaces the ones already there.
/// </para>
/// <para>
/// The recorded divergence: MATLAB's <c>YData</c> answers with the matrix a call passed and keeps
/// the columns as columns. Here a call is flattened into observations plus the group each belongs
/// to, so <c>YData</c> is always a plain list and <c>XData</c> always names its groups. Everything a
/// script can ask about the drawn chart survives that; the original shape does not.
/// </para>
/// </summary>
public sealed class BoxChartPlot : PlotObject, IDrawable, ILegendItem
{
    private double[] _yData;
    private double[]? _xData;
    private Color? _boxFaceColor;
    private double _boxFaceAlpha = 0.5;
    private Color _boxEdgeColor = Color.FromScRgb(0.15, 0.15, 0.15);
    private Color _boxMedianLineColor = Color.FromScRgb(0.15, 0.15, 0.15);
    private double _boxWidth = 0.5;
    private double _lineWidth = 1.0;
    private Color _whiskerLineColor = Color.FromScRgb(0.15, 0.15, 0.15);
    private DashStyle _whiskerLineStyle = DashStyle.Solid;
    private MarkerType _markerStyle = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerColor;
    private bool _notch;
    private bool _jitterOutliers;
    private bool _horizontal;

    private List<(double Position, BoxSummary Summary)>? _groups;

    /// <summary>Creates a box chart over one group of observations.</summary>
    public BoxChartPlot(double[] yData)
        : this(null, yData)
    {
    }

    /// <summary>
    /// Creates a box chart whose observations are cut into groups by <paramref name="xData"/>: one
    /// box per distinct value in it, drawn at that value. Null puts every observation in one group
    /// at position 1, which is where MATLAB puts a single unlabelled box.
    /// </summary>
    public BoxChartPlot(double[]? xData, double[] yData)
    {
        _yData = yData ?? throw new ArgumentNullException(nameof(yData));
        _xData = xData;
        Name = "Box chart";
    }

    /// <summary>Which group each observation falls in, or null when they are all in one.</summary>
    [Browsable(false)]
    public double[]? XData
    {
        get => _xData;
        set
        {
            _xData = value;
            Resummarize();
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>Every observation, in the order it was given.</summary>
    [Browsable(false)]
    public double[] YData
    {
        get => _yData;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _yData = value;
            Resummarize();
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>The colour the boxes are filled with, or null to take the series colour.</summary>
    [Category("Appearance"), DisplayName("Box face color")]
    public Color? BoxFaceColor
    {
        get => _boxFaceColor;
        set => SetProperty(ref _boxFaceColor, value, InvalidationKind.Render);
    }

    /// <summary>How opaque the box fill is, in [0, 1]. The outline is unaffected.</summary>
    [Category("Appearance"), DisplayName("Box face alpha")]
    public double BoxFaceAlpha
    {
        get => _boxFaceAlpha;
        set => SetProperty(ref _boxFaceAlpha, System.Math.Clamp(value, 0, 1), InvalidationKind.Render);
    }

    /// <summary>The colour of the box outline.</summary>
    [Category("Appearance"), DisplayName("Box edge color")]
    public Color BoxEdgeColor
    {
        get => _boxEdgeColor;
        set => SetProperty(ref _boxEdgeColor, value, InvalidationKind.Render);
    }

    /// <summary>The colour of the line across the box at the median.</summary>
    [Category("Appearance"), DisplayName("Box median line color")]
    public Color BoxMedianLineColor
    {
        get => _boxMedianLineColor;
        set => SetProperty(ref _boxMedianLineColor, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How much of the slot at each position the boxes fill, in data units. Box charts sharing an
    /// axes divide this between them, so it is the width of the group rather than of one box.
    /// </summary>
    [Category("Appearance"), DisplayName("Box width")]
    public double BoxWidth
    {
        get => _boxWidth;
        set => SetProperty(ref _boxWidth, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>The width of the box outline, the median line and the whiskers.</summary>
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>The colour of the whiskers.</summary>
    [Category("Appearance"), DisplayName("Whisker line color")]
    public Color WhiskerLineColor
    {
        get => _whiskerLineColor;
        set => SetProperty(ref _whiskerLineColor, value, InvalidationKind.Render);
    }

    /// <summary>The dash pattern of the whiskers.</summary>
    [Category("Appearance"), DisplayName("Whisker line style")]
    public DashStyle WhiskerLineStyle
    {
        get => _whiskerLineStyle;
        set => SetProperty(ref _whiskerLineStyle, value, InvalidationKind.Render);
    }

    /// <summary>The marker drawn for each observation past the whiskers.</summary>
    [Category("Appearance"), DisplayName("Marker style")]
    public MarkerType MarkerStyle
    {
        get => _markerStyle;
        set => SetProperty(ref _markerStyle, value, InvalidationKind.Render);
    }

    /// <summary>The size of the outlier markers.</summary>
    [Category("Appearance"), DisplayName("Marker size")]
    public double MarkerSize
    {
        get => _markerSize;
        set => SetProperty(ref _markerSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>The colour of the outlier markers, or null to take the box fill colour.</summary>
    [Category("Appearance"), DisplayName("Marker color")]
    public Color? MarkerColor
    {
        get => _markerColor;
        set => SetProperty(ref _markerColor, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether the box is notched at the median. The notch spans the interval within which the
    /// median is not distinguishable from another, so two boxes whose notches miss each other differ
    /// in median — which is the only thing a notch is for.
    /// </summary>
    [Category("Appearance")]
    public bool Notch
    {
        get => _notch;
        set => SetProperty(ref _notch, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether outliers are spread sideways within the box's slot instead of stacked on its centre
    /// line, which is what makes a crowd of them countable. The spread is fixed per observation, so
    /// it does not move between redraws.
    /// </summary>
    [Category("Appearance"), DisplayName("Jitter outliers")]
    public bool JitterOutliers
    {
        get => _jitterOutliers;
        set => SetProperty(ref _jitterOutliers, value, InvalidationKind.Render);
    }

    /// <summary>When true, the boxes lie along X and the groups run up Y.</summary>
    [Category("Appearance")]
    public bool Horizontal
    {
        get => _horizontal;
        set => SetProperty(ref _horizontal, value, InvalidationKind.Layout);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>
    /// The boxes this chart draws: one per distinct group, in ascending order of position, each with
    /// the summary of the observations that fell in it. A group with nothing finite in it is left
    /// out rather than drawn flat.
    /// </summary>
    public IReadOnlyList<(double Position, BoxSummary Summary)> Groups() => _groups ??= BuildGroups();

    /// <summary>Recomputes the summaries after the observations were changed in place.</summary>
    public void Resummarize()
    {
        _groups = null;
        Invalidate(InvalidationKind.Data);
    }

    /// <summary>
    /// How wide one of this chart's boxes is, and how far its centre sits from the group position.
    /// Both come from how many box charts share the axes, which is what makes several of them stand
    /// side by side without being told to.
    /// </summary>
    public (double Width, double Offset) SlotGeometry()
    {
        int index = 0;
        int count = 0;
        if (Axes is { } axes)
        {
            foreach (PlotObject plot in axes.Plots)
            {
                if (plot is not BoxChartPlot box)
                {
                    continue;
                }

                if (ReferenceEquals(box, this))
                {
                    index = count;
                }

                count++;
            }
        }

        count = System.Math.Max(1, count);
        double width = _boxWidth / count;
        return (width, (index - ((count - 1) / 2.0)) * width);
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => _horizontal ? ValueBounds() : PositionBounds();

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => _horizontal ? PositionBounds() : ValueBounds();

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        IReadOnlyList<(double Position, BoxSummary Summary)> groups = Groups();
        if (groups.Count == 0)
        {
            return;
        }

        Color face = (_boxFaceColor ?? state.SeriesColor).WithOpacity(Opacity * _boxFaceAlpha);
        LineStyle? outline = _lineWidth > 0
            ? new LineStyle(_boxEdgeColor.WithOpacity(Opacity), _lineWidth)
            : null;
        var median = new LineStyle(_boxMedianLineColor.WithOpacity(Opacity), System.Math.Max(_lineWidth, 0.5));
        var whisker = new LineStyle(
            _whiskerLineColor.WithOpacity(Opacity), System.Math.Max(_lineWidth, 0.5), _whiskerLineStyle);

        (double width, double offset) = SlotGeometry();
        double half = width / 2;

        foreach ((double position, BoxSummary summary) in groups)
        {
            double centre = position + offset;
            DrawBox(context, state.Mapper, centre, half, summary, outline, face, median);
            DrawWhiskers(context, state.Mapper, centre, summary, whisker);
            DrawOutliers(context, state.Mapper, centre, half, summary, state.SeriesColor);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor) =>
        new(line: null, marker: null, swatch: _boxFaceColor ?? seriesColor);

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        IReadOnlyList<(double Position, BoxSummary Summary)> groups = Groups();
        if (!HitTestVisible || groups.Count == 0)
        {
            return null;
        }

        (double width, double offset) = SlotGeometry();
        double half = width / 2;

        for (int i = 0; i < groups.Count; i++)
        {
            (double position, BoxSummary summary) = groups[i];
            double centre = position + offset;
            Point2D low = At(mapper, centre - half, summary.LowerWhisker);
            Point2D high = At(mapper, centre + half, summary.UpperWhisker);
            var box = Rect2D.FromCorners(low, high);
            if (pixelPoint.X < box.Left - tolerancePixels || pixelPoint.X > box.Right + tolerancePixels
                || pixelPoint.Y < box.Top - tolerancePixels || pixelPoint.Y > box.Bottom + tolerancePixels)
            {
                continue;
            }

            // A box says its median, which is the one number a data tip on it should show.
            Point2D at = _horizontal
                ? new Point2D(summary.Median, position)
                : new Point2D(position, summary.Median);
            return new PlotHitResult(this, at, 0, i);
        }

        return null;
    }

    private List<(double Position, BoxSummary Summary)> BuildGroups()
    {
        var byPosition = new SortedDictionary<double, List<double>>();
        for (int i = 0; i < _yData.Length; i++)
        {
            double position = _xData is { } groups
                ? (i < groups.Length ? groups[i] : double.NaN)
                : 1;
            if (!double.IsFinite(position) || !double.IsFinite(_yData[i]))
            {
                continue;
            }

            if (!byPosition.TryGetValue(position, out List<double>? values))
            {
                byPosition[position] = values = [];
            }

            values.Add(_yData[i]);
        }

        var built = new List<(double, BoxSummary)>(byPosition.Count);
        foreach ((double position, List<double> values) in byPosition)
        {
            if (Quartiles.Summarize(values) is { } summary)
            {
                built.Add((position, summary));
            }
        }

        return built;
    }

    /// <summary>The extent along the direction the groups run in, boxes and all.</summary>
    private DataRange PositionBounds()
    {
        IReadOnlyList<(double Position, BoxSummary Summary)> groups = Groups();
        if (groups.Count == 0)
        {
            return DataRange.Empty;
        }

        (double width, double offset) = SlotGeometry();
        DataRange range = DataRange.Empty;
        foreach ((double position, _) in groups)
        {
            // Half a slot of clearance past the outermost box, so a box never touches the frame.
            range = range.Include(position + offset - width).Include(position + offset + width);
        }

        return range;
    }

    /// <summary>The extent along the direction the observations run in, outliers included.</summary>
    private DataRange ValueBounds()
    {
        DataRange range = DataRange.Empty;
        foreach (double value in _yData)
        {
            if (double.IsFinite(value))
            {
                range = range.Include(value);
            }
        }

        return range;
    }

    /// <summary>A point at <paramref name="along"/> across the groups and <paramref name="value"/> up them.</summary>
    private Point2D At(ICoordinateMapper mapper, double along, double value) =>
        _horizontal ? mapper.DataToPixel(value, along) : mapper.DataToPixel(along, value);

    private void DrawBox(
        IRenderContext context,
        ICoordinateMapper mapper,
        double centre,
        double half,
        BoxSummary summary,
        LineStyle? outline,
        Color face,
        LineStyle median)
    {
        double notch = _notch ? summary.NotchHalfWidth : 0;
        double waist = _notch ? half * 0.5 : half;

        Point2D[] corners = _notch
            ?
            [
                At(mapper, centre - half, summary.LowerQuartile),
                At(mapper, centre - half, summary.Median - notch),
                At(mapper, centre - waist, summary.Median),
                At(mapper, centre - half, summary.Median + notch),
                At(mapper, centre - half, summary.UpperQuartile),
                At(mapper, centre + half, summary.UpperQuartile),
                At(mapper, centre + half, summary.Median + notch),
                At(mapper, centre + waist, summary.Median),
                At(mapper, centre + half, summary.Median - notch),
                At(mapper, centre + half, summary.LowerQuartile),
            ]
            :
            [
                At(mapper, centre - half, summary.LowerQuartile),
                At(mapper, centre - half, summary.UpperQuartile),
                At(mapper, centre + half, summary.UpperQuartile),
                At(mapper, centre + half, summary.LowerQuartile),
            ];

        context.DrawPolygon(corners, outline, face);
        context.DrawLine(
            At(mapper, centre - waist, summary.Median),
            At(mapper, centre + waist, summary.Median),
            median);
    }

    private void DrawWhiskers(
        IRenderContext context, ICoordinateMapper mapper, double centre, BoxSummary summary, LineStyle style)
    {
        // No crossbar at the end: boxchart draws a bare whisker where the older boxplot draws a cap.
        context.DrawLine(
            At(mapper, centre, summary.UpperQuartile), At(mapper, centre, summary.UpperWhisker), style);
        context.DrawLine(
            At(mapper, centre, summary.LowerQuartile), At(mapper, centre, summary.LowerWhisker), style);
    }

    private void DrawOutliers(
        IRenderContext context,
        ICoordinateMapper mapper,
        double centre,
        double half,
        BoxSummary summary,
        Color seriesColor)
    {
        if (summary.Outliers.Count == 0 || _markerStyle == MarkerType.None || _markerSize <= 0)
        {
            return;
        }

        Color color = (_markerColor ?? _boxFaceColor ?? seriesColor).WithOpacity(Opacity);
        var points = new Point2D[summary.Outliers.Count];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = At(mapper, centre + Jitter(summary.Outliers[i], half), summary.Outliers[i]);
        }

        context.DrawMarkers(points, new MarkerStyle(_markerStyle, _markerSize, color, color), color);
    }

    /// <summary>
    /// How far sideways an outlier is nudged. It is a function of the value itself rather than of a
    /// random draw, so the same chart drawn twice puts the same point in the same place.
    /// </summary>
    private double Jitter(double value, double half)
    {
        if (!_jitterOutliers || half <= 0)
        {
            return 0;
        }

        uint bits = (uint)(value.GetHashCode() * 2654435761L);
        return ((bits / (double)uint.MaxValue) - 0.5) * half;
    }
}
