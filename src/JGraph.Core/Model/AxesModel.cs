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
    private Color? _backgroundColor;
    private Rect2D _normalizedBounds = new(0, 0, 1, 1);
    private double _autoScalePadding = 0.05;
    private bool _equalAspect;
    private bool _frameVisible = true;
    private bool _is3D;
    private bool _isPolar;
    private ThetaZeroLocation _thetaZeroLocation = ThetaZeroLocation.Right;
    private double _thetaZeroOffset;
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
    private AxesLayer _layer = AxesLayer.Bottom;
    private double _lineWidth = 1.0;
    private Box3DStyle _boxStyle = Box3DStyle.Back;
    private Color _ambientLightColor = Colors.White;
    private double _titleFontSizeMultiplier = 1.1;
    private double _labelFontSizeMultiplier = 1.1;
    private TitleHorizontalAlignment _titleHorizontalAlignment = TitleHorizontalAlignment.Center;
    private ColorScaleType _colorScale = ColorScaleType.Linear;
    private Colormap? _colormap;
    private DataRange? _colorLimits;
    private Vector3D? _dataAspectRatio;
    private IReadOnlyList<SeriesLineStyle>? _lineStyleOrder;
    private int _nextSeriesIndex;
    private Vector3D? _cameraPosition;
    private Vector3D? _cameraTarget;
    private Vector3D? _cameraUpVector;
    private double? _cameraViewAngle;
    private ProjectionType _projection = ProjectionType.Orthographic;
    private SortMethodType _sortMethod = SortMethodType.Depth;
    private bool _clipping = true;
    private DataRange? _alphaLimits;
    private IReadOnlyList<double>? _alphamap;
    private ColorScaleType _alphaScale = ColorScaleType.Linear;
    private Vector3D _currentPointFront;
    private Vector3D _currentPointBack;
    private Rect2D? _innerTarget;
    private PositionConstraintType _positionConstraint = PositionConstraintType.OuterPosition;
    private int? _layoutTile;
    private int _layoutRowSpan = 1;
    private int _layoutColumnSpan = 1;
    private TiledLayoutOptionsModel? _layoutOptions;
    private List<InteractionModel>? _interactions;
    private bool _interactionsDisabled;
    private AxesToolbarModel? _toolbar;

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
            // An emptied axes starts its color cycle over, which is what makes cla, hold off, and a
            // replacing plot verb all behave like MATLAB's newplot without any of them knowing a
            // counter exists. Removing one plot leaves the counter alone: survivors keep their colors.
            if (Plots.Count == 0)
            {
                _nextSeriesIndex = 0;
            }

            if (e.NewItems is not null)
            {
                foreach (PlotObject plot in e.NewItems.OfType<PlotObject>())
                {
                    plot.AdoptAxesDefaults(this);
                }
            }

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

    /// <summary>
    /// The dash-and-marker cycle auto-styled series step through after the color order wraps
    /// (MATLAB <c>LineStyleOrder</c>), or null for the default single solid entry.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<SeriesLineStyle>? LineStyleOrder
    {
        get => _lineStyleOrder;
        set => SetProperty(ref _lineStyleOrder, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How many series slots this axes has handed out since it was last empty — the 0-based seat of
    /// the next auto-styled plot in the color cycle (MATLAB's <c>ColorOrderIndex</c> and
    /// <c>NextSeriesIndex</c> are this number spoken 1-based). Writable so a script can rewind or
    /// skip the cycle.
    /// </summary>
    [Browsable(false)]
    public int NextSeriesIndex
    {
        get => _nextSeriesIndex;
        set => _nextSeriesIndex = System.Math.Max(0, value);
    }

    /// <summary>
    /// Hands out the next series slot: which palette seat the plot occupies and which line-style
    /// entry goes with it. Colors advance first and the line style steps once per full lap of the
    /// palette, which is MATLAB's law for the two cycles.
    /// </summary>
    public SeriesSlot TakeSeriesSlot()
    {
        int index = _nextSeriesIndex++;
        IReadOnlyList<Color> palette = _colorOrder is { Count: > 0 } chosen ? chosen : Colors.DefaultSeriesOrder;
        SeriesLineStyle style = _lineStyleOrder is { Count: > 0 } order
            ? order[index / palette.Count % order.Count]
            : SeriesLineStyle.Solid;
        return new SeriesSlot(index, palette[index % palette.Count], style);
    }

    /// <summary>
    /// The palette color a plot's slot resolves to today, without advancing anything. A plot that
    /// never took a slot (built through the API rather than a script) answers by its position, which
    /// is how those plots have always been colored.
    /// </summary>
    public Color PeekSeriesColor(PlotObject plot)
    {
        IReadOnlyList<Color> palette = _colorOrder is { Count: > 0 } chosen ? chosen : Colors.DefaultSeriesOrder;
        int index = plot.SeriesIndex;
        if (index < 0)
        {
            index = 0;
            foreach (PlotObject candidate in Plots.InDrawOrder())
            {
                if (ReferenceEquals(candidate, plot))
                {
                    break;
                }

                index++;
            }
        }

        return palette[index % palette.Count];
    }

    /// <summary>The grid lines.</summary>
    public GridModel Grid { get; }

    /// <summary>Whether the grid and ticks are drawn under or over the data (MATLAB <c>Layer</c>).</summary>
    [Category("Appearance")]
    public AxesLayer Layer
    {
        get => _layer;
        set => SetProperty(ref _layer, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The width in pixels of the axis lines, box, and tick marks (MATLAB <c>LineWidth</c> — which
    /// there defaults to half a point; the JGraph default of 1 is what every figure was drawn with,
    /// recorded as a divergence).
    /// </summary>
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0.1, value), InvalidationKind.Render);
    }

    /// <summary>How much of the 3D coordinate box is outlined (MATLAB <c>BoxStyle</c>).</summary>
    [Category("3D View"), DisplayName("Box style")]
    public Box3DStyle BoxStyle
    {
        get => _boxStyle;
        set => SetProperty(ref _boxStyle, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The color of the one ambient light every lit object here sees (MATLAB
    /// <c>AmbientLightColor</c>). White — the default — multiplies nothing away, and like MATLAB it
    /// only shows while a light object exists.
    /// </summary>
    [Category("3D View"), DisplayName("Ambient light color")]
    public Color AmbientLightColor
    {
        get => _ambientLightColor;
        set => SetProperty(ref _ambientLightColor, value, InvalidationKind.Render);
    }

    /// <summary>How much larger than the axes font the title is drawn (MATLAB <c>TitleFontSizeMultiplier</c>).</summary>
    [Category("General"), DisplayName("Title size multiplier")]
    public double TitleFontSizeMultiplier
    {
        get => _titleFontSizeMultiplier;
        set => SetProperty(ref _titleFontSizeMultiplier, System.Math.Max(0.1, value), InvalidationKind.Layout);
    }

    /// <summary>How much larger than the axes font the axis labels are drawn (MATLAB <c>LabelFontSizeMultiplier</c>).</summary>
    [Category("General"), DisplayName("Label size multiplier")]
    public double LabelFontSizeMultiplier
    {
        get => _labelFontSizeMultiplier;
        set => SetProperty(ref _labelFontSizeMultiplier, System.Math.Max(0.1, value), InvalidationKind.Layout);
    }

    /// <summary>Where the title sits over the plot area (MATLAB <c>TitleHorizontalAlignment</c>).</summary>
    [Category("General"), DisplayName("Title alignment")]
    public TitleHorizontalAlignment TitleHorizontalAlignment
    {
        get => _titleHorizontalAlignment;
        set => SetProperty(ref _titleHorizontalAlignment, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// True once a script has chosen the axes font size (MATLAB <c>FontSizeMode</c> 'manual').
    /// Setting the mode back to automatic restores the built-in sizes.
    /// </summary>
    [Browsable(false)]
    public bool FontSizeManual { get; set; }

    /// <summary>
    /// True once a script has chosen the plot box aspect (MATLAB <c>PlotBoxAspectRatioMode</c>
    /// 'manual'); automatic is the default cube.
    /// </summary>
    [Browsable(false)]
    public bool PlotBoxAspectManual { get; set; }

    /// <summary>How values are spread over the color limits (MATLAB <c>ColorScale</c>).</summary>
    [Category("Appearance"), DisplayName("Color scale")]
    public ColorScaleType ColorScale
    {
        get => _colorScale;
        set => SetProperty(ref _colorScale, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The colormap this axes hands to its color-mapped plots (MATLAB <c>Colormap</c>), or null for
    /// the automatic one. The color-mapped plots keep their own copies — this is what new plots are
    /// seeded from and what a script reads back, so <c>colormap</c> works whichever side of the
    /// plotting verb it is called on.
    /// </summary>
    [Browsable(false)]
    public Colormap? Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The color limits this axes pins its color-mapped plots to (MATLAB <c>CLim</c>), or null when
    /// each plot scales to its own data. Like <see cref="Colormap"/>, plots carry their own working
    /// values seeded from this.
    /// </summary>
    [Browsable(false)]
    public DataRange? ColorLimits
    {
        get => _colorLimits;
        set => SetProperty(ref _colorLimits, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The relative lengths one data unit takes along x, y, and z (MATLAB <c>daspect</c>), or null
    /// to fit the data freely. (1, 1, 1) is <c>axis equal</c> said with numbers.
    /// </summary>
    [Browsable(false)]
    public Vector3D? DataAspectRatio
    {
        get => _dataAspectRatio;
        set => SetProperty(ref _dataAspectRatio, value, InvalidationKind.Layout);
    }

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
    /// The fill behind this axes' whole cell, or null to let the figure show through (M84).
    /// </summary>
    /// <remarks>
    /// <see cref="Background"/> — MATLAB's <c>Color</c> — fills the plot box. This fills the cell the
    /// box sits in, ticks, labels and all, which is what MATLAB's <c>UIAxes.BackgroundColor</c> means
    /// and the one property a <c>uiaxes</c> documents that a plain <c>axes</c> does not. Null by
    /// default, so an axes nobody made through <c>uiaxes</c> draws exactly as it always has.
    /// </remarks>
    [Category("Appearance"), DisplayName("Background colour")]
    public Color? BackgroundColor
    {
        get => _backgroundColor;
        set => SetProperty(ref _backgroundColor, value, InvalidationKind.Render);
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
        set
        {
            // Entering three dimensions turns the wall grid on, which is the figure every 3D verb
            // has always produced (the old renderer drew it unconditionally) and what MATLAB's own
            // surf and plot3 show. Leaving changes nothing: 2D axes keep whatever grid they chose.
            if (value && !_is3D)
            {
                Grid.ShowMajor = true;
            }

            SetProperty(ref _is3D, value, InvalidationKind.Layout);
        }
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

    /// <summary>
    /// A further rotation of the whole chart, in degrees, on top of <see cref="ThetaZeroLocation"/>
    /// (M83).
    /// </summary>
    /// <remarks>
    /// MATLAB has no such property, and this build needs one for a reason its four-word cousin cannot
    /// meet: a drag that turns the chart moves it by whatever angle the pointer moved, and
    /// <see cref="ThetaZeroLocation"/> holds four compass points. Shifting <c>ThetaLim</c> was the
    /// other candidate and does not rotate anything — the visible turn decides which angles are drawn,
    /// not where a drawn one lands. Recorded as a divergence in ADR 0083.
    /// </remarks>
    [Category("Polar"), DisplayName("Theta zero offset")]
    public double ThetaZeroOffset
    {
        get => _thetaZeroOffset;
        set => SetProperty(ref _thetaZeroOffset, value, InvalidationKind.Render);
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
        set
        {
            RAxisLocationManual = true;
            SetProperty(ref _rAxisLocation, value, InvalidationKind.Render);
        }
    }

    /// <summary>
    /// True once a script has chosen the angle the r labels are written along (MATLAB
    /// <c>RAxisLocationMode</c> 'manual'). It carries its own flag rather than reading a nullable,
    /// because 80° is both the automatic answer and an angle a script may legitimately ask for.
    /// </summary>
    [Browsable(false)]
    public bool RAxisLocationManual { get; set; }

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
        set
        {
            // pbaspect and daspect both shape the same box, so the one written last is the one in
            // charge; a stored data aspect would silently override this write every frame.
            _dataAspectRatio = null;
            SetProperty(ref _plotBoxAspect, value, InvalidationKind.Render);
        }
    }

    /// <summary>
    /// Where the camera stands, in data coordinates (MATLAB <c>CameraPosition</c>), or null to
    /// stand where <see cref="Azimuth"/> and <see cref="Elevation"/> put it. Null is what every
    /// figure drew before M74, so an untouched axes projects exactly as it always did.
    /// </summary>
    [Browsable(false)]
    public Vector3D? CameraPosition
    {
        get => _cameraPosition;
        set => SetProperty(ref _cameraPosition, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The point the camera looks at, in data coordinates (MATLAB <c>CameraTarget</c>), or null for
    /// the center of the plot box. A manual target is what the plot area centers on.
    /// </summary>
    [Browsable(false)]
    public Vector3D? CameraTarget
    {
        get => _cameraTarget;
        set => SetProperty(ref _cameraTarget, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Which data direction points up the screen (MATLAB <c>CameraUpVector</c>), or null for +z,
    /// the up every 3D axes has used. <see cref="Roll"/> turns the camera about its own axis after
    /// this vector has chosen the frame.
    /// </summary>
    [Browsable(false)]
    public Vector3D? CameraUpVector
    {
        get => _cameraUpVector;
        set => SetProperty(ref _cameraUpVector, value, InvalidationKind.Render);
    }

    /// <summary>
    /// How wide a cone the camera sees, in degrees (MATLAB <c>CameraViewAngle</c>), or null to fit
    /// the plot box to the plot area — which is what MATLAB's own automatic view angle means.
    /// </summary>
    [Browsable(false)]
    public double? CameraViewAngle
    {
        get => _cameraViewAngle;
        set
        {
            if (value is { } angle && (double.IsNaN(angle) || angle <= 0 || angle >= 180))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), angle, "The camera view angle is an angle strictly between 0 and 180 degrees.");
            }

            SetProperty(ref _cameraViewAngle, value, InvalidationKind.Render);
        }
    }

    /// <summary>Parallel rays or a viewpoint (MATLAB <c>Projection</c>). Consulted only in 3D.</summary>
    [Browsable(false)]
    public ProjectionType Projection
    {
        get => _projection;
        set => SetProperty(ref _projection, value, InvalidationKind.Render);
    }

    /// <summary>How the faces of each 3D object are ordered (MATLAB <c>SortMethod</c>).</summary>
    [Browsable(false)]
    public SortMethodType SortMethod
    {
        get => _sortMethod;
        set => SetProperty(ref _sortMethod, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Whether plotted content is confined to the plot area (MATLAB <c>Clipping</c>). Off lets a
    /// curve run out into the margins, which is how MATLAB shows data that leaves the limits.
    /// </summary>
    [Browsable(false)]
    public bool Clipping
    {
        get => _clipping;
        set => SetProperty(ref _clipping, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The alpha-data limits this axes maps transparency over (MATLAB <c>ALim</c>), or null while
    /// each plot spreads its own alpha data over the whole map.
    /// </summary>
    [Browsable(false)]
    public DataRange? AlphaLimits
    {
        get => _alphaLimits;
        set => SetProperty(ref _alphaLimits, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The transparencies alpha data is looked up in (MATLAB <c>Alphamap</c>), or null for the
    /// even ramp from clear to opaque. Unlike a colormap this is a plain list of numbers, because
    /// that is what MATLAB's alphamap is.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<double>? Alphamap
    {
        get => _alphamap;
        set => SetProperty(ref _alphamap, value, InvalidationKind.Render);
    }

    /// <summary>How alpha data is spread over <see cref="AlphaLimits"/> (MATLAB <c>AlphaScale</c>).</summary>
    [Browsable(false)]
    public ColorScaleType AlphaScale
    {
        get => _alphaScale;
        set => SetProperty(ref _alphaScale, value, InvalidationKind.Render);
    }

    /// <summary>
    /// Where the pointer last crossed this axes, as the two ends of the line of sight through it
    /// (MATLAB <c>CurrentPoint</c>). Interaction state, not document state: it is never serialized,
    /// and a figure that has never been pointed at answers zeros.
    /// </summary>
    [Browsable(false)]
    public (Vector3D Front, Vector3D Back) CurrentPoint => (_currentPointFront, _currentPointBack);

    /// <summary>
    /// Records where the pointer is. Deliberately silent — a redraw provoked by moving the mouse
    /// would chase its own tail, and nothing is drawn from this.
    /// </summary>
    public void SetCurrentPoint(Vector3D front, Vector3D back)
    {
        _currentPointFront = front;
        _currentPointBack = back;
    }

    /// <summary>
    /// The plot box this axes was asked to occupy, in the same fractions and the same downward Y as
    /// <see cref="NormalizedBounds"/>, or null while the cell is what was asked for. Setting it makes
    /// the renderer derive the cell each frame by inflating this rectangle by the margins it measured,
    /// which is what MATLAB's <c>PositionConstraint</c> of <c>'innerposition'</c> means.
    /// </summary>
    [Browsable(false)]
    public Rect2D? InnerTarget
    {
        get => _innerTarget;
        set => SetProperty(ref _innerTarget, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Which cell of the figure's tiled layout this axes was handed, counting from one, or null when
    /// it is not in one. MATLAB reaches this through <c>ax.Layout.Tile</c>, which is what it is for:
    /// an axes in a layout can be moved to another cell by naming the cell.
    /// </summary>
    [Browsable(false)]
    public int? LayoutTile
    {
        get => _layoutTile;
        set => SetProperty(ref _layoutTile, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The gestures this axes answers to without a tool being chosen first (MATLAB
    /// <c>Interactions</c>). Made on first use with what a fresh axes has always done — pan, zoom and
    /// a click that pins a data tip, plus rotate once there is a third direction to turn.
    /// <para>
    /// Deliberately not serialized: it says how a window behaves, not what a figure is. A saved
    /// figure opened again is as interactive as a fresh one, which is what a reader expects.
    /// </para>
    /// </summary>
    [Browsable(false)]
    public IList<InteractionModel> Interactions
    {
        get
        {
            if (_interactions is null)
            {
                _interactions = [new PanInteractionModel(), new ZoomInteractionModel(),
                    new DataTipInteractionModel()];
                if (_is3D)
                {
                    _interactions.Add(new RotateInteractionModel());
                }

                foreach (InteractionModel interaction in _interactions)
                {
                    Adopt(interaction);
                }
            }

            return _interactions;
        }
    }

    /// <summary>
    /// The toolbar shown over this axes when the pointer is inside it, made on first use with
    /// MATLAB's own default buttons. It is window chrome: the renderer never draws it, so an export
    /// never carries it, and only whether it is shown is worth keeping in a saved figure.
    /// </summary>
    [Browsable(false)]
    public AxesToolbarModel Toolbar
    {
        get
        {
            if (_toolbar is null)
            {
                _toolbar = new AxesToolbarModel();
                Adopt(_toolbar);
            }

            return _toolbar;
        }
        set
        {
            _toolbar = value;
            Adopt(value);
        }
    }

    /// <summary>Whether this axes answers to a given gesture, and with what setting.</summary>
    public T? InteractionOf<T>()
        where T : InteractionModel =>
        _interactionsDisabled ? null : Interactions.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Turns every default gesture off, or back on (MATLAB <c>disableDefaultInteractivity</c>). The
    /// list itself is kept, so enabling gives back whatever a script had chosen rather than the
    /// defaults — which is what makes the pair a switch rather than a reset.
    /// </summary>
    public bool InteractionsDisabled
    {
        get => _interactionsDisabled;
        set => SetProperty(ref _interactionsDisabled, value, InvalidationKind.None);
    }

    /// <summary>
    /// The small object MATLAB reaches this axes' place in a tiled layout through, made on first
    /// use. It is a view of the three properties above rather than a second copy of them.
    /// </summary>
    [Browsable(false)]
    public TiledLayoutOptionsModel LayoutOptions
    {
        get
        {
            if (_layoutOptions is null)
            {
                _layoutOptions = new TiledLayoutOptionsModel(this);
                Adopt(_layoutOptions);
            }

            return _layoutOptions;
        }
    }

    /// <summary>How many rows of the grid the tile covers (MATLAB <c>ax.Layout.TileSpan</c>).</summary>
    [Browsable(false)]
    public int LayoutRowSpan
    {
        get => _layoutRowSpan;
        set => SetProperty(ref _layoutRowSpan, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <inheritdoc cref="LayoutRowSpan" />
    [Browsable(false)]
    public int LayoutColumnSpan
    {
        get => _layoutColumnSpan;
        set => SetProperty(ref _layoutColumnSpan, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <summary>Which rectangle a placement fixes (MATLAB <c>PositionConstraint</c>).</summary>
    [Browsable(false)]
    public PositionConstraintType PositionConstraint
    {
        get => _positionConstraint;
        set => SetProperty(ref _positionConstraint, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// What the renderer measured for this axes on the last frame, or null before the first one.
    /// Written by the renderer and read by the layout properties; deliberately silent and never
    /// serialized, since it describes a drawing rather than a document.
    /// </summary>
    [Browsable(false)]
    public AxesLayoutSnapshot? LastLayout { get; set; }

    /// <summary>
    /// The colormap this axes actually hands out: its own if it has chosen one, otherwise the
    /// figure's, otherwise none at all. MATLAB's colormap lives on the figure and an axes overrides
    /// it, so a figure-level map has to be visible from here or setting one would reach nothing.
    /// </summary>
    public Colormap? ResolveColormap() => _colormap ?? (Parent as FigureModel)?.Colormap;

    /// <summary>The transparencies this axes looks alpha data up in, read through to the figure.</summary>
    public IReadOnlyList<double>? ResolveAlphamap() => _alphamap ?? (Parent as FigureModel)?.Alphamap;

    /// <summary>The view angle an axes uses when it has not been told one (MATLAB's own default).</summary>
    public const double DefaultCameraViewAngle = 6.6086;

    /// <summary>
    /// Points the camera at the given angles, releasing every manual camera slot. MATLAB's
    /// <c>view</c> does exactly this: it is the verb that says "let the angles decide again".
    /// </summary>
    public void SetViewAngles(double azimuth, double elevation)
    {
        _cameraPosition = null;
        _cameraTarget = null;
        _cameraUpVector = null;
        _cameraViewAngle = null;
        Azimuth = azimuth;
        Elevation = elevation;
    }

    /// <summary>True while the camera is entirely the angles' to decide and the rays are parallel.</summary>
    public bool HasAutomaticCamera =>
        _cameraPosition is null && _cameraTarget is null && _cameraUpVector is null
        && _cameraViewAngle is null && _projection == ProjectionType.Orthographic;

    /// <summary>The point the camera looks at, chosen or derived.</summary>
    public Vector3D EffectiveCameraTarget()
    {
        if (_cameraTarget is { } chosen)
        {
            return chosen;
        }

        RecomputeDataBounds();
        return new Vector3D(
            (PrimaryXAxis.Range.Min + PrimaryXAxis.Range.Max) / 2,
            (ActiveYAxis.Range.Min + ActiveYAxis.Range.Max) / 2,
            (ZAxis.Range.Min + ZAxis.Range.Max) / 2);
    }

    /// <summary>
    /// Which way is up the screen, chosen or derived. The derived answer is +z for every view that
    /// looks at the box from the side, but a camera looking straight down cannot use +z as its up —
    /// it is looking along it — so a top-down view answers the azimuth's own north instead. MATLAB
    /// does the same: <c>view(2); camup</c> is [0 1 0], not [0 0 1].
    /// </summary>
    public Vector3D EffectiveCameraUpVector()
    {
        if (_cameraUpVector is { } chosen)
        {
            return chosen;
        }

        if (System.Math.Abs(System.Math.Abs(_elevation) - 90) > 1e-6)
        {
            return new Vector3D(0, 0, 1);
        }

        double azimuth = _azimuth * System.Math.PI / 180.0;
        double facing = _elevation >= 0 ? 1 : -1;
        return new Vector3D(-System.Math.Sin(azimuth) * facing, System.Math.Cos(azimuth) * facing, 0);
    }

    /// <summary>The view angle, chosen or MATLAB's default.</summary>
    public double EffectiveCameraViewAngle() => _cameraViewAngle ?? DefaultCameraViewAngle;

    /// <summary>
    /// Where the camera stands, chosen or derived from the angles. The derived stand-off is the one
    /// the view angle implies, so reading the position, the angle and the projection back together
    /// describes a camera that would draw the picture on screen.
    /// </summary>
    public Vector3D EffectiveCameraPosition()
    {
        if (_cameraPosition is { } chosen)
        {
            return chosen;
        }

        Vector3D target = EffectiveCameraTarget();
        double azimuth = _azimuth * System.Math.PI / 180.0;
        double elevation = _elevation * System.Math.PI / 180.0;

        // The same direction Projection3D looks along, so campos and the picture agree.
        double dx = System.Math.Sin(azimuth) * System.Math.Cos(elevation);
        double dy = -System.Math.Cos(azimuth) * System.Math.Cos(elevation);
        double dz = System.Math.Sin(elevation);

        double xSpan = System.Math.Abs(PrimaryXAxis.Range.Max - PrimaryXAxis.Range.Min);
        double ySpan = System.Math.Abs(ActiveYAxis.Range.Max - ActiveYAxis.Range.Min);
        double zSpan = System.Math.Abs(ZAxis.Range.Max - ZAxis.Range.Min);
        double diagonal = System.Math.Sqrt((xSpan * xSpan) + (ySpan * ySpan) + (zSpan * zSpan));
        if (diagonal <= 0 || double.IsNaN(diagonal))
        {
            diagonal = 1;
        }

        double half = EffectiveCameraViewAngle() * System.Math.PI / 360.0;
        double distance = diagonal / 2 / System.Math.Tan(half);

        return new Vector3D(
            target.X + (dx * distance * (xSpan > 0 ? xSpan / diagonal : 1)),
            target.Y + (dy * distance * (ySpan > 0 ? ySpan / diagonal : 1)),
            target.Z + (dz * distance * (zSpan > 0 ? zSpan / diagonal : 1)));
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
            FitRange(ZAxis, bounds);
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
                FitRange(axis, bounds);
            }
        }
    }

    /// <summary>
    /// Turns a ruler's data extent into its visible limits under its <see cref="LimitMethod"/>:
    /// padded by <see cref="AutoScalePadding"/> (the default every existing figure was fitted
    /// under), exactly tight, or pushed outward to tick-friendly round numbers.
    /// </summary>
    private void FitRange(AxisModel axis, DataRange bounds)
    {
        DataRange fitted = bounds.IsEmpty ? DataRange.Unit : bounds.EnsureValid();
        switch (axis.LimitMethod)
        {
            case LimitMethod.Tight:
                break;
            case LimitMethod.Tickaligned:
                if (fitted.IsValid)
                {
                    fitted = AlignToTicks(fitted, axis.Scale);
                }

                break;
            default:
                if (_autoScalePadding > 0 && fitted.IsValid)
                {
                    fitted = ExpandForScale(fitted, axis.Scale, _autoScalePadding);
                }

                break;
        }

        axis.Range = fitted;
    }

    /// <summary>
    /// Pushes a fitted range outward to the nearest multiples of a nice tick step. The 1-2-5 ladder
    /// is re-said here because Core cannot reach the tick generators in JGraph.Maths; the arithmetic
    /// in TickGenerators is this method's twin. A log ruler snaps to whole decades instead.
    /// </summary>
    private static DataRange AlignToTicks(DataRange range, AxisScaleType scale)
    {
        if (scale == AxisScaleType.Logarithmic && range.Min > 0 && range.Max > 0)
        {
            return new DataRange(
                System.Math.Pow(10, System.Math.Floor(System.Math.Log10(range.Min))),
                System.Math.Pow(10, System.Math.Ceiling(System.Math.Log10(range.Max))));
        }

        double span = range.Max - range.Min;
        if (span <= 0 || !double.IsFinite(span))
        {
            return range;
        }

        double raw = span / 5;
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
        double step = magnitude * (raw / magnitude) switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 };
        double min = System.Math.Floor(range.Min / step) * step;
        double max = System.Math.Ceiling(range.Max / step) * step;
        return min == max ? new DataRange(min - step, max + step) : new DataRange(min, max);
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

/// <summary>
/// One handed-out seat in an axes' series cycle: the 0-based index, the palette color it resolves
/// to under the default palette, and the line-style entry that goes with it.
/// </summary>
public readonly record struct SeriesSlot(int Index, Color Color, SeriesLineStyle Style);
