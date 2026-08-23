using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>Where a legend is anchored within (or beside) its axes.</summary>
public enum LegendPosition
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
    Top,
    Bottom,
    Right,
    Left,

    /// <summary>Placed at <see cref="LegendModel.Location"/>, typically because it was dragged there.</summary>
    Custom,
}

/// <summary>Which way a legend's rows run (MATLAB <c>Orientation</c>).</summary>
public enum LegendOrientation
{
    /// <summary>One entry under the next, the default.</summary>
    Vertical,

    /// <summary>Entries side by side along one row.</summary>
    Horizontal,
}

/// <summary>
/// The legend of an <see cref="AxesModel"/>: placement, styling, and an ordered list of
/// <see cref="Entries"/>, one per legended series. The entries are kept in step with the plots by
/// <see cref="SyncEntries"/>, which the renderer runs before each layout; between syncs they are the
/// user's to rename, hide and reorder. Legends are hidden by default and shown via the API (for
/// example <c>JG.Legend()</c>) or the plot browser.
/// </summary>
public sealed class LegendModel : GraphObject
{
    private LegendPosition _position = LegendPosition.TopRight;
    private Point2D _location = new(0.6, 0.05);
    private Color _background = Colors.White.WithOpacity(0.85);
    private Color _borderColor = Colors.Gray;
    private bool _showBorder = true;
    private TextStyle _textStyle = new(Colors.Black, 11);
    private string? _title;
    private double _borderWidth = 1;
    private LegendOrientation _orientation = LegendOrientation.Vertical;
    private int? _columns;
    private bool _autoUpdate = true;
    private Rect2D? _figureBox;

    public LegendModel()
    {
        Name = "Legend";
        Visible = false;
        Entries = new GraphObjectCollection<LegendEntryModel>(this);
    }

    /// <summary>The legend rows, drawn top to bottom in this order.</summary>
    public GraphObjectCollection<LegendEntryModel> Entries { get; }

    [Category("Appearance")]
    public LegendPosition Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Where the legend box's top-left sits, as a fraction of the plot area. Honored only when
    /// <see cref="Position"/> is <see cref="LegendPosition.Custom"/>; choosing a preset leaves this
    /// alone, so returning to <c>Custom</c> puts the legend back where it was dragged.
    /// </summary>
    [Category("Appearance")]
    public Point2D Location
    {
        get => _location;
        set => SetProperty(ref _location, value, InvalidationKind.Layout);
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

    [Category("General")]
    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value, InvalidationKind.Layout);
    }

    /// <summary>How thick the box's border is drawn (MATLAB <c>LineWidth</c>).</summary>
    [Category("Appearance"), DisplayName("Border width")]
    public double BorderWidth
    {
        get => _borderWidth;
        set => SetProperty(ref _borderWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Which way the rows run.</summary>
    [Category("Appearance")]
    public LegendOrientation Orientation
    {
        get => _orientation;
        set => SetProperty(ref _orientation, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// How many columns the entries are dealt into, or null to let the orientation decide — one for a
    /// vertical legend, one per entry for a horizontal one. That is MATLAB's <c>NumColumnsMode</c>
    /// read as a nullable rather than stored as a second property beside the number.
    /// </summary>
    [Browsable(false)]
    public int? Columns
    {
        get => _columns;
        set => SetProperty(ref _columns, value is { } n && n > 0 ? n : null, InvalidationKind.Layout);
    }

    /// <summary>The columns actually used, given the orientation and the number of entries.</summary>
    public int ResolveColumns(int entries)
    {
        if (_columns is { } chosen)
        {
            return System.Math.Max(1, System.Math.Min(chosen, System.Math.Max(1, entries)));
        }

        return _orientation == LegendOrientation.Horizontal ? System.Math.Max(1, entries) : 1;
    }

    /// <summary>
    /// Whether the rows follow the plots (MATLAB <c>AutoUpdate</c>). Turned off, the legend keeps the
    /// rows it has: a series added afterwards is not legended, and one removed leaves its row behind
    /// naming nothing, which is what a script asking for a fixed legend wants.
    /// </summary>
    [Category("Behavior"), DisplayName("Auto update")]
    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetProperty(ref _autoUpdate, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// An explicit box in figure fractions (Y downward, as the rest of this model measures), or null
    /// to place the legend by <see cref="Position"/>. This is what MATLAB's four-element
    /// <c>Position</c> pins, and it outranks both the preset and the dragged location.
    /// </summary>
    [Browsable(false)]
    public Rect2D? FigureBox
    {
        get => _figureBox;
        set => SetProperty(ref _figureBox, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Where the renderer last drew the box, in device pixels, or null before the first frame. A
    /// script asking a legend where it is has to be told where it went, not where it was asked to go.
    /// </summary>
    [Browsable(false)]
    public Rect2D? LastBox { get; set; }

    /// <summary>
    /// Reconciles <see cref="Entries"/> with the legendable plots: appends a row for each plot that
    /// has none, drops rows whose plot is gone, and otherwise leaves the order, labels and inclusion
    /// flags alone.
    /// <para>
    /// Returns false — having touched nothing — when the rows already match. That idempotence is what
    /// lets a render pass call this on every frame: a plot added or removed costs one structural
    /// invalidation and the steady state costs none.
    /// </para>
    /// Callers pass only the plots that can appear in a legend; deciding that needs the rendering
    /// layer's <c>ILegendItem</c>, which this layer cannot see.
    /// </summary>
    public bool SyncEntries(IEnumerable<PlotObject> plots)
    {
        ArgumentNullException.ThrowIfNull(plots);

        if (!_autoUpdate)
        {
            return false;
        }

        var legendable = plots as IReadOnlyList<PlotObject> ?? plots.ToList();
        bool changed = false;

        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            PlotObject? plot = Entries[i].Plot;
            if (plot is null || !legendable.Any(p => ReferenceEquals(p, plot)))
            {
                Entries.RemoveAt(i);
                changed = true;
            }
        }

        foreach (PlotObject plot in legendable)
        {
            if (!Entries.Any(e => ReferenceEquals(e.Plot, plot)))
            {
                Entries.Add(new LegendEntryModel { Plot = plot });
                changed = true;
            }
        }

        return changed;
    }
}
