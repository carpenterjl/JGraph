using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>How a bubble legend arranges the bubbles it shows.</summary>
public enum BubbleLegendStyle
{
    /// <summary>Stacked one above another, smallest at the top.</summary>
    Vertical,

    /// <summary>Side by side, smallest at the left, with the values written underneath.</summary>
    Horizontal,

    /// <summary>Nested, sharing a bottom edge, so each bubble is drawn inside the next one up.</summary>
    Telescopic,
}

/// <summary>
/// The bubble legend of an <see cref="AxesModel"/> — a few bubbles at representative sizes with the
/// values they stand for, which is the only way to read a bubble chart's third dimension. Hidden by
/// default and shown by <c>bubblelegend</c>.
/// <para>
/// Like the colorbar this model stores only placement and styling: the sizes come from the axes'
/// <see cref="AxesModel.BubbleScale"/> and the colour from the first bubble chart drawn, so a legend
/// cannot drift out of step with the chart it explains.
/// </para>
/// </summary>
public sealed class BubbleLegendModel : GraphObject
{
    private LegendPosition _position = LegendPosition.TopRight;
    private Point2D _location = new(0.75, 0.05);
    private BubbleLegendStyle _style = BubbleLegendStyle.Vertical;
    private int _numBubbles = 3;
    private bool _limitLabels;
    private Color _background = Colors.White.WithOpacity(0.85);
    private Color _borderColor = Colors.Gray;
    private bool _showBorder = true;
    private TextStyle _textStyle = new(Colors.Black, 11);
    private double _borderWidth = 1;
    private bool _descending;
    private Rect2D? _figureBox;
    private string? _title;

    public BubbleLegendModel()
    {
        Name = "BubbleLegend";
        Visible = false;
    }

    [Category("Appearance")]
    public LegendPosition Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Layout);
    }

    /// <summary>Where the box's top-left sits as a fraction of the plot area, honored when
    /// <see cref="Position"/> is <see cref="LegendPosition.Custom"/>.</summary>
    [Category("Appearance")]
    public Point2D Location
    {
        get => _location;
        set => SetProperty(ref _location, value, InvalidationKind.Layout);
    }

    [Category("Appearance")]
    public BubbleLegendStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// How many bubbles are shown, from 2 to 6. Two is the smallest legend that says anything — the
    /// ends of the scale — and past six the bubbles crowd each other rather than informing.
    /// </summary>
    [Category("Appearance"), DisplayName("Number of bubbles")]
    public int NumBubbles
    {
        get => _numBubbles;
        set => SetProperty(ref _numBubbles, System.Math.Clamp(value, 2, 6), InvalidationKind.Layout);
    }

    /// <summary>When true only the smallest and largest bubbles are labelled, as MATLAB's option does.</summary>
    [Category("Appearance"), DisplayName("Limit labels only")]
    public bool LimitLabels
    {
        get => _limitLabels;
        set => SetProperty(ref _limitLabels, value, InvalidationKind.Layout);
    }

    [Category("Appearance")]
    public Color Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Border color")]
    public Color BorderColor
    {
        get => _borderColor;
        set => SetProperty(ref _borderColor, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Show border")]
    public bool ShowBorder
    {
        get => _showBorder;
        set => SetProperty(ref _showBorder, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Text style")]
    public TextStyle TextStyle
    {
        get => _textStyle;
        set => SetProperty(ref _textStyle, value, InvalidationKind.Layout);
    }

    /// <summary>What the sizes mean — the legend's own heading, usually the name of the size variable.</summary>
    [Category("General")]
    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The values this legend shows, spread evenly across the scale it is legending. Worked out here
    /// rather than in the renderer so a test can ask what a legend says without drawing it.
    /// </summary>
    public IReadOnlyList<double> ValuesFor(BubbleScale scale)
    {
        DataRange limits = scale.Limits;
        var values = new double[_numBubbles];
        for (int i = 0; i < _numBubbles; i++)
        {
            double t = i / (double)(_numBubbles - 1);
            values[i] = limits.Min + (t * (limits.Max - limits.Min));
        }

        if (_descending)
        {
            Array.Reverse(values);
        }

        return values;
    }

    /// <summary>How thick the box's border is drawn (MATLAB <c>LineWidth</c>).</summary>
    [Category("Appearance"), DisplayName("Border width")]
    public double BorderWidth
    {
        get => _borderWidth;
        set => SetProperty(ref _borderWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>
    /// True when the biggest bubble is listed first (MATLAB <c>BubbleSizeOrder</c> 'descend').
    /// Ascending is the default, which puts the smallest at the top of a vertical legend.
    /// </summary>
    [Category("Appearance"), DisplayName("Largest first")]
    public bool Descending
    {
        get => _descending;
        set => SetProperty(ref _descending, value, InvalidationKind.Layout);
    }

    /// <summary>An explicit box in figure fractions (Y downward), or null to place it by <see cref="Position"/>.</summary>
    [Browsable(false)]
    public Rect2D? FigureBox
    {
        get => _figureBox;
        set => SetProperty(ref _figureBox, value, InvalidationKind.Layout);
    }

    /// <summary>Where the renderer last drew the box, in device pixels, or null before the first frame.</summary>
    [Browsable(false)]
    public Rect2D? LastBox { get; set; }
}
