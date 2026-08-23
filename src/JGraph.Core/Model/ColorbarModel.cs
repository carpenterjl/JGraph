using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>Where a colorbar sits relative to its axes (MATLAB <c>Location</c>).</summary>
public enum ColorbarLocation
{
    /// <summary>Outside the right edge — the default, and where every colorbar sat before M78.</summary>
    EastOutside,

    /// <summary>Outside the left edge.</summary>
    WestOutside,

    /// <summary>Above the plot box, lying on its side.</summary>
    NorthOutside,

    /// <summary>Below the plot box, lying on its side.</summary>
    SouthOutside,

    /// <summary>Inside the plot box against its right edge; the plot area does not shrink.</summary>
    East,

    /// <summary>Inside the plot box against its left edge.</summary>
    West,

    /// <summary>Inside the plot box against its top edge, lying on its side.</summary>
    North,

    /// <summary>Inside the plot box against its bottom edge, lying on its side.</summary>
    South,

    /// <summary>Wherever <see cref="ColorbarModel.FigureBox"/> puts it.</summary>
    Manual,
}

/// <summary>
/// The colorbar of an <see cref="AxesModel"/> — a colormap gradient beside (or over) the plot area
/// that legends the axes' first color-mapped plot. Hidden by default and shown via the API (for
/// example <c>JG.Colorbar()</c>). This model stores placement, the ruler that runs alongside the
/// strip, and styling; the renderer reads the colormap and the value range from the plots.
/// </summary>
public sealed class ColorbarModel : GraphObject
{
    private double _width = 18;
    private string? _label;
    private TextStyle _tickLabelStyle = new(Colors.DarkGray, 11);
    private ColorbarLocation _location = ColorbarLocation.EastOutside;
    private Rect2D? _figureBox;
    private DataRange? _limits;
    private double[]? _tickValues;
    private string[]? _tickLabels;
    private TickDirection _tickDirection = TickDirection.Out;
    private double _tickLength = 0.01;
    private bool _inverted;
    private bool _labelsInside;
    private bool _boxVisible = true;
    private Color? _ink;
    private double _lineWidth = 0.5;

    public ColorbarModel()
    {
        Name = "Colorbar";
        Visible = false;
    }

    /// <summary>The width of the gradient strip in pixels — its thickness, whichever way it lies.</summary>
    [Category("Appearance")]
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, System.Math.Max(4, value), InvalidationKind.Layout);
    }

    /// <summary>An optional label drawn alongside the colorbar.</summary>
    [Category("General")]
    public string? Label
    {
        get => _label;
        set => SetProperty(ref _label, value, InvalidationKind.Layout);
    }

    /// <summary>The style of the value labels beside the strip.</summary>
    [Category("Ticks"), DisplayName("Tick label style")]
    public TextStyle TickLabelStyle
    {
        get => _tickLabelStyle;
        set => SetProperty(ref _tickLabelStyle, value, InvalidationKind.Layout);
    }

    /// <summary>Which side of the axes the strip stands on, and whether it takes room from it.</summary>
    [Category("Appearance")]
    public ColorbarLocation Location
    {
        get => _location;
        set => SetProperty(ref _location, value, InvalidationKind.Layout);
    }

    /// <summary>True when the strip lies on its side, with values running left to right.</summary>
    public bool IsHorizontal => _location is ColorbarLocation.North or ColorbarLocation.South
        or ColorbarLocation.NorthOutside or ColorbarLocation.SouthOutside
        || (_location == ColorbarLocation.Manual && _figureBox is { } box && box.Width > box.Height);

    /// <summary>True when the strip takes its room from the plot box rather than lying over it.</summary>
    public bool IsOutside => _location is ColorbarLocation.EastOutside or ColorbarLocation.WestOutside
        or ColorbarLocation.NorthOutside or ColorbarLocation.SouthOutside;

    /// <summary>
    /// An explicit box in figure fractions (Y downward, as the rest of this model measures), or null
    /// to place the strip by <see cref="Location"/>. Setting it is what moves the location to
    /// <see cref="ColorbarLocation.Manual"/>.
    /// </summary>
    [Browsable(false)]
    public Rect2D? FigureBox
    {
        get => _figureBox;
        set => SetProperty(ref _figureBox, value, InvalidationKind.Layout);
    }

    /// <summary>Where the renderer last drew the strip, in device pixels, or null before the first frame.</summary>
    [Browsable(false)]
    public Rect2D? LastBox { get; set; }

    /// <summary>
    /// The span of values the strip legends, or null to take the color range of the plot it legends.
    /// A colorbar with limits of its own shows that slice of the map, which is how MATLAB narrows one.
    /// </summary>
    [Browsable(false)]
    public DataRange? Limits
    {
        get => _limits;
        set => SetProperty(ref _limits, value, InvalidationKind.Layout);
    }

    /// <summary>Chosen tick values, or null to generate them from the limits.</summary>
    [Browsable(false)]
    public double[]? TickValues
    {
        get => _tickValues;
        set => SetProperty(ref _tickValues, value, InvalidationKind.Layout);
    }

    /// <summary>Chosen tick labels, cycled if shorter than the ticks, or null for the generated ones.</summary>
    [Browsable(false)]
    public string[]? TickLabelOverrides
    {
        get => _tickLabels;
        set => SetProperty(ref _tickLabels, value, InvalidationKind.Layout);
    }

    /// <summary>Which way the tick marks point off the strip.</summary>
    [Category("Ticks"), DisplayName("Tick direction")]
    public TickDirection TickDirection
    {
        get => _tickDirection;
        set => SetProperty(ref _tickDirection, value, InvalidationKind.Render);
    }

    /// <summary>How long a tick mark is, as a fraction of the strip's long side.</summary>
    [Category("Ticks"), DisplayName("Tick length")]
    public double TickLength
    {
        get => _tickLength;
        set => SetProperty(ref _tickLength, System.Math.Max(0, value), InvalidationKind.Layout);
    }

    /// <summary>True when high values sit at the low end of the strip (MATLAB <c>Direction</c> 'reverse').</summary>
    [Category("Appearance")]
    public bool Inverted
    {
        get => _inverted;
        set => SetProperty(ref _inverted, value, InvalidationKind.Render);
    }

    /// <summary>
    /// True when the tick labels are written on the plot-side face of the strip (MATLAB
    /// <c>AxisLocation</c> 'in'). Out — away from the axes — is the default.
    /// </summary>
    [Category("Ticks"), DisplayName("Labels inside")]
    public bool LabelsInside
    {
        get => _labelsInside;
        set => SetProperty(ref _labelsInside, value, InvalidationKind.Layout);
    }

    /// <summary>Whether the outline round the strip is drawn (MATLAB <c>Box</c>).</summary>
    [Category("Appearance"), DisplayName("Show box")]
    public bool BoxVisible
    {
        get => _boxVisible;
        set => SetProperty(ref _boxVisible, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The ink of the outline and the tick marks, or null to take the theme's axis line. The tick
    /// labels carry their own colour in <see cref="TickLabelStyle"/>, and MATLAB's one <c>Color</c>
    /// writes both.
    /// </summary>
    [Category("Appearance")]
    public Color? Ink
    {
        get => _ink;
        set => SetProperty(ref _ink, value, InvalidationKind.Render);
    }

    /// <summary>How thick the outline and tick marks are drawn.</summary>
    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }
}
