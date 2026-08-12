using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// A coordinate system within a <see cref="FigureModel"/>: one or more X and Y axes, the plotted
/// content, a grid, and a legend. An axes occupies a rectangular fraction of the figure
/// (<see cref="NormalizedBounds"/>) so that multiple axes can be tiled into subplots.
/// </summary>
public sealed class AxesModel : GraphObject
{
    private string _title = string.Empty;
    private TextStyle _titleStyle = new(Colors.Black, 15, bold: true);
    private string _subtitle = string.Empty;
    private TextStyle _subtitleStyle = new(Colors.Black, 12);
    private Color _background = Colors.White;
    private Rect2D _normalizedBounds = new(0, 0, 1, 1);
    private double _autoScalePadding = 0.05;
    private bool _equalAspect;
    private bool _frameVisible = true;
    private bool _is3D;
    private bool _isPolar;
    private ThetaZeroLocation _thetaZeroLocation = ThetaZeroLocation.Right;
    private ThetaDirection _thetaDirection = ThetaDirection.CounterClockwise;
    private AngleUnits _thetaAxisUnits = AngleUnits.Degrees;
    private double _rAxisLocation = 80;
    private double _azimuth = -37.5;
    private double _elevation = 30;
    private double _roll;
    private IReadOnlyList<Color>? _colorOrder;
    private Vector3D _plotBoxAspect = new(1, 1, 1);
    private int _activeYAxisIndex;
    private DataRange _bubbleSizeRange = BubbleScale.DefaultSizeRange;
    private DataRange? _bubbleSizeLimits;

    public AxesModel()
    {
        Name = "Axes";
        XAxes = new GraphObjectCollection<AxisModel>(this);
        YAxes = new GraphObjectCollection<AxisModel>(this);
        Plots = new GraphObjectCollection<PlotObject>(this);
        Annotations = new GraphObjectCollection<AnnotationObject>(this);
        Lights = new GraphObjectCollection<LightModel>(this);

        Grid = new GridModel();
        Grid.SetParent(this);
        Legend = new LegendModel();
        Legend.SetParent(this);
        Colorbar = new ColorbarModel();
        Colorbar.SetParent(this);
        BubbleLegend = new BubbleLegendModel();
        BubbleLegend.SetParent(this);

        ZAxis = new AxisModel(AxisOrientation.Vertical, AxisPosition.Left) { Name = "ZAxis" };
        ZAxis.SetParent(this);

        // The angular rulers are built for every axes, as the Z ruler is, so that a script can set
        // their ticks or limits before anything is drawn and a saved figure keeps them. A full turn is
        // not something to be fitted to the data — the circle is the whole point — so the theta ruler
        // starts pinned, while r fits like an ordinary scale.
        RAxis = new AxisModel(AxisOrientation.Vertical, AxisPosition.Left) { Name = "RAxis" };
        RAxis.SetParent(this);
        ThetaAxis = new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom)
        {
            Name = "ThetaAxis",
            AutoScale = false,
            Range = new DataRange(0, 360),
        };
        ThetaAxis.SetParent(this);

        XAxes.Add(new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom));
        YAxes.Add(new AxisModel(AxisOrientation.Vertical, AxisPosition.Left));

        // Which side new plots land on is a property of the axes, not of each plotting verb: MATLAB's
        // yyaxis says "from here on, draw against this ruler" and every verb obeys without being told.
        // Binding here is what makes that true for all sixty of them at once.
        Plots.CollectionChanged += (_, e) =>
        {
            if (_activeYAxisIndex == 0 || e.NewItems is null)
            {
                return;
            }

            foreach (PlotObject plot in e.NewItems.OfType<PlotObject>())
            {
                // A plot that already names a ruler keeps it, so loading a file or copying an object
                // reproduces what was saved rather than what the axes happens to be pointing at.
                if (plot.YAxisIndex == 0)
                {
                    plot.YAxisIndex = _activeYAxisIndex;
                }
            }
        };
    }

    /// <summary>The X axes. The first entry is the primary (bottom) axis.</summary>
    public GraphObjectCollection<AxisModel> XAxes { get; }

    /// <summary>The Y axes. The first entry is the primary (left) axis.</summary>
    public GraphObjectCollection<AxisModel> YAxes { get; }

    /// <summary>The plotted content drawn in this coordinate system.</summary>
    public GraphObjectCollection<PlotObject> Plots { get; }

    /// <summary>
    /// Annotations drawn over the plots, clipped to the plot area. Their anchors are data coordinates
    /// (unless an annotation's <see cref="AnnotationObject.Space"/> says otherwise), so they follow
    /// zoom and pan. Annotations never influence auto-scaling.
    /// </summary>
    public GraphObjectCollection<AnnotationObject> Annotations { get; }

    /// <summary>
    /// The lights illuminating this axes' 3D content. Empty by default, which is what makes a
    /// <c>surf</c> flat colormap color until a script says otherwise, exactly as in MATLAB. Lights
    /// sum, and 2D content ignores them entirely.
    /// </summary>
    public GraphObjectCollection<LightModel> Lights { get; }

    /// <summary>
    /// The colors plots cycle through, overriding the theme's series palette for this axes only
    /// (MATLAB <c>colororder</c>). Null — the default — leaves the theme in charge, which is what
    /// keeps a figure following a theme switch.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<Color>? ColorOrder
    {
        get => _colorOrder;
        set => SetProperty(ref _colorOrder, value, InvalidationKind.Render);
    }

    /// <summary>The grid lines.</summary>
    public GridModel Grid { get; }

    /// <summary>The legend (hidden until enabled).</summary>
    public LegendModel Legend { get; }

    /// <summary>The colorbar (hidden until enabled). Legends the first color-mapped plot's colormap.</summary>
    public ColorbarModel Colorbar { get; }

    /// <summary>The bubble legend (hidden until enabled). Legends <see cref="BubbleScale"/>.</summary>
    public BubbleLegendModel BubbleLegend { get; }

    /// <summary>
    /// The smallest and largest bubble diameter in points (MATLAB <c>bubblesize</c>). It belongs to
    /// the axes rather than to a chart because two bubble charts drawn together must be read against
    /// one scale, or the reader cannot compare them.
    /// </summary>
    [Category("Appearance"), DisplayName("Bubble size range")]
    public DataRange BubbleSizeRange
    {
        get => _bubbleSizeRange;
        set => SetProperty(ref _bubbleSizeRange, Sane(value), InvalidationKind.Render);
    }

    /// <summary>
    /// The data values mapped onto the ends of <see cref="BubbleSizeRange"/> (MATLAB
    /// <c>bubblelim</c>), or null to take them from the data. Null is not a range a script can read,
    /// so <c>get</c> answers with the effective limits instead — see <see cref="BubbleScale"/>.
    /// </summary>
    [Browsable(false)]
    public DataRange? BubbleSizeLimits
    {
        get => _bubbleSizeLimits;
        set => SetProperty(ref _bubbleSizeLimits, value is { } range ? Sane(range) : null, InvalidationKind.Render);
    }

    /// <summary>
    /// How this axes turns a bubble chart's size value into a diameter: the size range as set, and the
    /// value limits either as set or as taken from every bubble chart drawn here. Reading the limits
    /// off the siblings is what makes two charts share one scale without either being told about the
    /// other.
    /// </summary>
    [Browsable(false)]
    public BubbleScale BubbleScale => new(ResolveBubbleLimits(), _bubbleSizeRange);

    /// <summary>The bubble value limits in force, worked out from the data when none were set.</summary>
    public DataRange ResolveBubbleLimits()
    {
        if (_bubbleSizeLimits is { } fixedLimits)
        {
            return fixedLimits;
        }

        DataRange limits = DataRange.Empty;
        foreach (PlotObject plot in Plots)
        {
            if (!plot.Visible || plot is not IBubbleData { BubbleSizing: true, SizeData: { } sizes })
            {
                continue;
            }

            foreach (double size in sizes)
            {
                if (double.IsFinite(size))
                {
                    limits = limits.Include(size);
                }
            }
        }

        return limits.IsEmpty ? DataRange.Unit : limits;
    }

    /// <summary>Orders a pair and refuses the non-finite, so a bad pair cannot make bubbles vanish.</summary>
    private static DataRange Sane(DataRange range)
    {
        if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max))
        {
            return DataRange.Unit;
        }

        return range.Min <= range.Max ? range : new DataRange(range.Max, range.Min);
    }

    /// <summary>
    /// The Z axis. Always constructed so its label/range/tick configuration persists, but only
    /// consulted (for autoscale, projection, and drawing) when <see cref="Is3D"/> is true.
    /// </summary>
    public AxisModel ZAxis { get; }

    /// <summary>
    /// The radial ruler of a polar axes. Always constructed, like <see cref="ZAxis"/>, but only
    /// consulted when <see cref="IsPolar"/> is true.
    /// </summary>
    public AxisModel RAxis { get; }

    /// <summary>The angular ruler of a polar axes. Its range and ticks are in degrees.</summary>
    public AxisModel ThetaAxis { get; }

    /// <summary>The primary (first) X axis.</summary>
    public AxisModel PrimaryXAxis => XAxes[0];

    /// <summary>The primary (first) Y axis.</summary>
    public AxisModel PrimaryYAxis => YAxes[0];

    /// <summary>
    /// Which Y ruler the y-facing verbs read and write, and which new plots bind to (MATLAB
    /// <c>yyaxis left</c> / <c>yyaxis right</c>). Like <see cref="Hold"/> this is an editing mode
    /// rather than part of the figure's appearance, so it is neither saved nor shown in the inspector:
    /// a reloaded two-ruler figure comes back with the left side active, which is where MATLAB starts.
    /// </summary>
    [Browsable(false)]
    public int ActiveYAxisIndex
    {
        get => _activeYAxisIndex;
        set => _activeYAxisIndex = System.Math.Clamp(value, 0, YAxes.Count - 1);
    }

    /// <summary>The Y ruler <see cref="ActiveYAxisIndex"/> names, falling back to the primary one.</summary>
    public AxisModel ActiveYAxis =>
        _activeYAxisIndex > 0 && _activeYAxisIndex < YAxes.Count ? YAxes[_activeYAxisIndex] : PrimaryYAxis;

    [Category("General")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Layout);
    }

    /// <summary>How the axes title is drawn (font, size, weight, color).</summary>
    [Category("General"), DisplayName("Title style")]
    public TextStyle TitleStyle
    {
        get => _titleStyle;
        set => SetProperty(ref _titleStyle, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// A second line under the title (MATLAB <c>subtitle</c>), for the qualification a title should
    /// not have to carry — the conditions a run was made under, the units, the date.
    /// </summary>
    [Category("General")]
    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value ?? string.Empty, InvalidationKind.Layout);
    }

    /// <summary>How the subtitle is drawn. Smaller and lighter than the title, so it reads as second.</summary>
    [Category("General"), DisplayName("Subtitle style")]
    public TextStyle SubtitleStyle
    {
        get => _subtitleStyle;
        set => SetProperty(ref _subtitleStyle, value, InvalidationKind.Layout);
    }

    [Category("Appearance")]
    public Color Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    /// <summary>
    /// This axes' placement within the figure expressed as fractions in [0, 1] of the figure size
    /// (X and Y measured from the top-left). Defaults to the whole figure.
    /// </summary>
    [Category("Appearance"), DisplayName("Bounds")]
    public Rect2D NormalizedBounds
    {
        get => _normalizedBounds;
        set => SetProperty(ref _normalizedBounds, value, InvalidationKind.Layout);
    }

    /// <summary>Fractional padding added around the data extent when an axis auto-scales.</summary>
    [Category("Behavior"), DisplayName("Auto-scale padding")]
    public double AutoScalePadding
    {
        get => _autoScalePadding;
        set => SetProperty(ref _autoScalePadding, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>
    /// When true, one data unit spans the same number of pixels on both axes (MATLAB <c>axis equal</c>):
    /// the plot area is shrunk to a centered rectangle of the correct aspect so circles render round.
    /// Used by polar, Smith, and Nyquist plots. Only meaningful with linear scales.
    /// </summary>
    [Category("Behavior"), DisplayName("Equal aspect")]
    public bool EqualAspect
    {
        get => _equalAspect;
        set => SetProperty(ref _equalAspect, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// When true, plotting into these axes adds to what is already there instead of replacing it
    /// (MATLAB <c>hold on</c>). Hold belongs to the axes, as in MATLAB, so it ends when the axes does —
    /// a new figure or <c>clf</c> starts unheld. It is a transient editing mode, not part of the
    /// figure's appearance, so it is neither saved to a <c>.graph</c> file nor shown in the inspector.
    /// </summary>
    [Browsable(false)]
    public bool Hold { get; set; }

    /// <summary>
    /// When true (default), the rectangular axis frame is drawn around the plot area. Polar and Smith
    /// charts turn it off because they draw their own circular grid instead.
    /// </summary>
    [Category("Appearance"), DisplayName("Frame visible")]
    public bool FrameVisible
    {
        get => _frameVisible;
        set => SetProperty(ref _frameVisible, value, InvalidationKind.Render);
    }

    /// <summary>
    /// When true, this axes renders as a 3D coordinate box: plots implementing the 3D drawing
    /// interface are projected through the camera angles below, and dragging rotates the view
    /// instead of panning. Set automatically by the surface-plot verbs.
    /// </summary>
    [Category("3D View"), DisplayName("3D")]
    public bool Is3D
    {
        get => _is3D;
        set => SetProperty(ref _is3D, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// When true, this axes renders as a circle: every plot's first coordinate is read as an angle and
    /// its second as a radius, the rings and spokes of <see cref="RAxis"/> and <see cref="ThetaAxis"/>
    /// stand in for the rectangular grid, and no Cartesian frame, ticks or labels are drawn. Set by
    /// <c>polaraxes</c> and by the angular plotting verbs, the same way the surface verbs set
    /// <see cref="Is3D"/>.
    /// </summary>
    [Category("Polar"), DisplayName("Polar")]
    public bool IsPolar
    {
        get => _isPolar;
        set => SetProperty(ref _isPolar, value, InvalidationKind.Layout);
    }

    /// <summary>Which compass point θ = 0 sits at (MATLAB <c>ThetaZeroLocation</c>).</summary>
    [Category("Polar"), DisplayName("Theta zero location")]
    public ThetaZeroLocation ThetaZeroLocation
    {
        get => _thetaZeroLocation;
        set => SetProperty(ref _thetaZeroLocation, value, InvalidationKind.Render);
    }

    /// <summary>Which way θ increases (MATLAB <c>ThetaDirection</c>).</summary>
    [Category("Polar"), DisplayName("Theta direction")]
    public ThetaDirection ThetaDirection
    {
        get => _thetaDirection;
        set => SetProperty(ref _thetaDirection, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The unit angles are read and written in (MATLAB <c>ThetaAxisUnits</c>). It governs the numbers
    /// crossing the boundary, not the drawing: <see cref="ThetaAxis"/> always holds degrees, because a
    /// ruler that changed units under its own ticks could not be compared with itself.
    /// </summary>
    [Category("Polar"), DisplayName("Theta axis units")]
    public AngleUnits ThetaAxisUnits
    {
        get => _thetaAxisUnits;
        set => SetProperty(ref _thetaAxisUnits, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The angle in degrees along which the r tick labels are written (MATLAB <c>RAxisLocation</c>).
    /// The default 80° keeps them off the horizontal spoke, where a data curve most often runs.
    /// </summary>
    [Category("Polar"), DisplayName("R axis location")]
    public double RAxisLocation
    {
        get => _rAxisLocation;
        set => SetProperty(ref _rAxisLocation, value, InvalidationKind.Render);
    }

    /// <summary>The camera azimuth in degrees (rotation about the vertical axis; MATLAB view() convention).</summary>
    [Category("3D View")]
    public double Azimuth
    {
        get => _azimuth;
        set => SetProperty(ref _azimuth, value, InvalidationKind.Render);
    }

    /// <summary>The camera elevation in degrees, clamped to [-90, 90].</summary>
    [Category("3D View")]
    public double Elevation
    {
        get => _elevation;
        set => SetProperty(ref _elevation, System.Math.Clamp(value, -90, 90), InvalidationKind.Render);
    }

    /// <summary>
    /// The camera roll in degrees: how far it is turned about the direction it is already looking
    /// (MATLAB <c>camroll</c>). Positive rolls the camera anticlockwise, so the scene inside the plot
    /// area appears to turn clockwise.
    /// </summary>
    [Category("3D View")]
    public double Roll
    {
        get => _roll;
        set => SetProperty(ref _roll, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The relative side lengths of the 3D plot box (MATLAB <c>pbaspect</c>). The default cube is what
    /// every 3D axes had before M45; only the ratios matter, since the box is scaled to fit the plot
    /// area either way.
    /// </summary>
    [Category("3D View"), DisplayName("Plot box aspect")]
    public Vector3D PlotBoxAspect
    {
        get => _plotBoxAspect;
        set => SetProperty(ref _plotBoxAspect, value, InvalidationKind.Render);
    }

    /// <summary>Adds a secondary X axis at the given position and returns it.</summary>
    public AxisModel AddXAxis(AxisPosition position = AxisPosition.Top)
    {
        var axis = new AxisModel(AxisOrientation.Horizontal, position);
        XAxes.Add(axis);
        return axis;
    }

    /// <summary>Adds a secondary Y axis at the given position and returns it.</summary>
    public AxisModel AddYAxis(AxisPosition position = AxisPosition.Right)
    {
        var axis = new AxisModel(AxisOrientation.Vertical, position);
        YAxes.Add(axis);
        return axis;
    }

    /// <summary>
    /// Makes the Y ruler at <paramref name="index"/> the active one, adding rulers on the right until
    /// it exists. Asking twice for the same side is not an error and does not add a second ruler, which
    /// is what lets a script say <c>yyaxis right</c> before each of several plots.
    /// </summary>
    public AxisModel UseYAxis(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        while (YAxes.Count <= index)
        {
            AddYAxis();
        }

        ActiveYAxisIndex = index;
        return YAxes[index];
    }

    /// <summary>Returns the X axis a plot object is bound to, falling back to the primary axis.</summary>
    public AxisModel GetXAxisFor(PlotObject plot) =>
        plot.XAxisIndex >= 0 && plot.XAxisIndex < XAxes.Count ? XAxes[plot.XAxisIndex] : PrimaryXAxis;

    /// <summary>Returns the Y axis a plot object is bound to, falling back to the primary axis.</summary>
    public AxisModel GetYAxisFor(PlotObject plot) =>
        plot.YAxisIndex >= 0 && plot.YAxisIndex < YAxes.Count ? YAxes[plot.YAxisIndex] : PrimaryYAxis;

    /// <summary>
    /// Recomputes each axis' <see cref="AxisModel.DataBounds"/> from the plots bound to it and, for
    /// axes with <see cref="AxisModel.AutoScale"/> enabled, updates their visible
    /// <see cref="AxisModel.Range"/> to fit (with <see cref="AutoScalePadding"/> applied).
    /// </summary>
    public void RecomputeDataBounds()
    {
        UpdateAxisBounds(XAxes, isX: true);
        UpdateAxisBounds(YAxes, isX: false);
        UpdateZAxisBounds();
        UpdatePolarBounds();
    }

    /// <summary>
    /// Fits the angular rulers to the data. A polar plot keeps θ where an ordinary plot keeps x and r
    /// where it keeps y, so the extents the pass above already found are the ones needed here.
    /// </summary>
    /// <remarks>
    /// The r ruler is fitted without <see cref="AutoScalePadding"/> and always reaches the origin when
    /// the data is positive: a ring drawn a little outside the largest sample is what a reader measures
    /// against, and a circle whose middle is not zero is a chart that lies about proportion.
    /// </remarks>
    private void UpdatePolarBounds()
    {
        if (!_isPolar)
        {
            return;
        }

        DataRange rBounds = DataRange.Empty;
        DataRange thetaBounds = DataRange.Empty;
        foreach (PlotObject plot in Plots)
        {
            if (!plot.Visible)
            {
                continue;
            }

            DataRange r = plot.GetYDataBounds();
            if (!r.IsEmpty)
            {
                rBounds = rBounds.Union(r);
            }

            DataRange theta = plot.GetXDataBounds();
            if (!theta.IsEmpty)
            {
                thetaBounds = thetaBounds.Union(theta);
            }
        }

        RAxis.DataBounds = rBounds;
        ThetaAxis.DataBounds = thetaBounds;

        if (RAxis.AutoScale)
        {
            DataRange fitted = rBounds.IsEmpty ? DataRange.Unit : rBounds.EnsureValid();
            RAxis.Range = new DataRange(System.Math.Min(0, fitted.Min), fitted.Max);
        }
    }

    /// <summary>Unions the Z extents of visible 3D plots into <see cref="ZAxis"/> (all 3D plots share it).</summary>
    private void UpdateZAxisBounds()
    {
        DataRange bounds = DataRange.Empty;
        foreach (PlotObject plot in Plots)
        {
            if (plot.Visible && plot is IHasZData zData)
            {
                DataRange plotBounds = zData.GetZDataBounds();
                if (!plotBounds.IsEmpty)
                {
                    bounds = bounds.Union(plotBounds);
                }
            }
        }

        ZAxis.DataBounds = bounds;

        if (ZAxis.AutoScale)
        {
            DataRange fitted = bounds.IsEmpty ? DataRange.Unit : bounds.EnsureValid();
            if (_autoScalePadding > 0 && fitted.IsValid)
            {
                fitted = ExpandForScale(fitted, ZAxis.Scale, _autoScalePadding);
            }

            ZAxis.Range = fitted;
        }
    }

    private void UpdateAxisBounds(GraphObjectCollection<AxisModel> axes, bool isX)
    {
        for (int i = 0; i < axes.Count; i++)
        {
            AxisModel axis = axes[i];
            DataRange bounds = DataRange.Empty;

            foreach (PlotObject plot in Plots)
            {
                if (!plot.Visible)
                {
                    continue;
                }

                int boundIndex = isX ? plot.XAxisIndex : plot.YAxisIndex;
                if (boundIndex != i)
                {
                    continue;
                }

                DataRange plotBounds = isX ? plot.GetXDataBounds() : plot.GetYDataBounds();
                if (!plotBounds.IsEmpty)
                {
                    bounds = bounds.Union(plotBounds);
                }
            }

            axis.DataBounds = bounds;

            if (axis.AutoScale)
            {
                DataRange fitted = bounds.IsEmpty ? DataRange.Unit : bounds.EnsureValid();
                if (_autoScalePadding > 0 && fitted.IsValid)
                {
                    fitted = ExpandForScale(fitted, axis.Scale, _autoScalePadding);
                }

                axis.Range = fitted;
            }
        }
    }

    /// <summary>
    /// Expands a fitted range by a fraction of its span for auto-scale padding. On a logarithmic axis
    /// the padding is applied in log space (a fraction of the decade span) so a small positive minimum
    /// is not driven to or below zero, which would collapse the visible range.
    /// </summary>
    private static DataRange ExpandForScale(DataRange range, AxisScaleType scale, double fraction)
    {
        if (scale == AxisScaleType.Logarithmic && range.Min > 0 && range.Max > 0)
        {
            double logMin = System.Math.Log10(range.Min);
            double logMax = System.Math.Log10(range.Max);
            double pad = (logMax - logMin) * fraction;
            return new DataRange(System.Math.Pow(10, logMin - pad), System.Math.Pow(10, logMax + pad));
        }

        return range.Expand(fraction);
    }
}
