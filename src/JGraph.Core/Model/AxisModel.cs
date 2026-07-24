using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// A single axis of an <see cref="AxesModel"/>. An axes may own several X and several Y axes to
/// support multiple/secondary scales. The <see cref="Range"/> is the source of truth for coordinate
/// transforms and tick generation; when <see cref="AutoScale"/> is true the axes layout pass keeps
/// it synchronized with the data extent, and interaction (zoom/pan) sets it explicitly.
/// </summary>
public sealed class AxisModel : GraphObject
{
    private AxisScaleType _scale = AxisScaleType.Linear;
    private DataRange _range = DataRange.Unit;
    private DataRange _dataBounds = DataRange.Empty;
    private bool _autoScale = true;
    private bool _inverted;
    private string _label = string.Empty;
    private bool _showMajorTicks = true;
    private bool _showMinorTicks;
    private bool _showTickLabels = true;
    private int _targetMajorTickCount = 5;
    private string? _tickLabelFormat;
    private TextStyle _labelStyle = new(Colors.Black, 13);
    private TextStyle _tickLabelStyle = new(Colors.DarkGray, 11);
    private IReadOnlyList<string>? _categories;

    public AxisModel(AxisOrientation orientation, AxisPosition position)
    {
        Orientation = orientation;
        Position = position;
        Name = orientation == AxisOrientation.Horizontal ? "XAxis" : "YAxis";
    }

    /// <summary>Whether this axis maps to the horizontal or vertical device direction.</summary>
    public AxisOrientation Orientation { get; }

    /// <summary>Which plot edge this axis is anchored to.</summary>
    public AxisPosition Position { get; }

    /// <summary>The data-to-linear scale applied to values on this axis.</summary>
    [Category("General")]
    public AxisScaleType Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value, InvalidationKind.Layout);
    }

    /// <summary>The currently visible data range. Drives transforms and tick generation.</summary>
    [Category("General")]
    public DataRange Range
    {
        get => _range;
        set => SetProperty(ref _range, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The union of the extents of all data attached to this axis, updated by the axes layout pass.
    /// Used to seed <see cref="Range"/> when <see cref="AutoScale"/> is true.
    /// </summary>
    [Browsable(false)]
    public DataRange DataBounds
    {
        get => _dataBounds;
        set => SetProperty(ref _dataBounds, value, InvalidationKind.None);
    }

    /// <summary>When true, the axes layout pass keeps <see cref="Range"/> fitted to <see cref="DataBounds"/>.</summary>
    [Category("General"), DisplayName("Auto scale")]
    public bool AutoScale
    {
        get => _autoScale;
        set => SetProperty(ref _autoScale, value, InvalidationKind.Layout);
    }

    /// <summary>When true, the axis direction is reversed (larger values toward the origin).</summary>
    [Category("General")]
    public bool Inverted
    {
        get => _inverted;
        set => SetProperty(ref _inverted, value, InvalidationKind.Layout);
    }

    /// <summary>The axis title (for example "Time (s)").</summary>
    [Category("General")]
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value ?? string.Empty, InvalidationKind.Layout);
    }

    [Category("Ticks"), DisplayName("Show major ticks")]
    public bool ShowMajorTicks
    {
        get => _showMajorTicks;
        set => SetProperty(ref _showMajorTicks, value, InvalidationKind.Layout);
    }

    [Category("Ticks"), DisplayName("Show minor ticks")]
    public bool ShowMinorTicks
    {
        get => _showMinorTicks;
        set => SetProperty(ref _showMinorTicks, value, InvalidationKind.Layout);
    }

    [Category("Ticks"), DisplayName("Show tick labels")]
    public bool ShowTickLabels
    {
        get => _showTickLabels;
        set => SetProperty(ref _showTickLabels, value, InvalidationKind.Layout);
    }

    /// <summary>The desired number of major ticks; the tick generator treats this as a target, not a mandate.</summary>
    [Category("Ticks"), DisplayName("Target tick count")]
    public int TargetMajorTickCount
    {
        get => _targetMajorTickCount;
        set => SetProperty(ref _targetMajorTickCount, System.Math.Max(2, value), InvalidationKind.Layout);
    }

    /// <summary>A .NET numeric format string for tick labels, or null to format automatically.</summary>
    [Category("Ticks"), DisplayName("Tick label format")]
    public string? TickLabelFormat
    {
        get => _tickLabelFormat;
        set => SetProperty(ref _tickLabelFormat, value, InvalidationKind.Layout);
    }

    /// <summary>How the axis label is drawn.</summary>
    [Category("General"), DisplayName("Label style")]
    public TextStyle LabelStyle
    {
        get => _labelStyle;
        set => SetProperty(ref _labelStyle, value, InvalidationKind.Layout);
    }

    /// <summary>How the tick labels are drawn.</summary>
    [Category("Ticks"), DisplayName("Tick label style")]
    public TextStyle TickLabelStyle
    {
        get => _tickLabelStyle;
        set => SetProperty(ref _tickLabelStyle, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The category labels for a <see cref="AxisScaleType.Category"/> axis, placed at integer positions
    /// 0, 1, 2, …. Null for non-category axes.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<string>? Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value, InvalidationKind.Layout);
    }

    /// <summary>True when this axis maps data along the horizontal device direction.</summary>
    public bool IsHorizontal => Orientation == AxisOrientation.Horizontal;

    /// <summary>Switches this axis to a category scale showing the given labels at positions 0, 1, 2, ….</summary>
    public void UseCategories(IReadOnlyList<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        Categories = categories;
        Scale = AxisScaleType.Category;
    }

    /// <summary>
    /// Switches this axis to a date/time scale. Data values are OLE automation dates (see
    /// <see cref="DateTimeAxis"/>); ticks are placed on natural time boundaries and labeled as dates.
    /// </summary>
    public void UseDateTime() => Scale = AxisScaleType.DateTime;
}
