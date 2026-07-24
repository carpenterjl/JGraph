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
