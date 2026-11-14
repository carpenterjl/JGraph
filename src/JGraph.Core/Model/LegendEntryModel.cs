using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>
/// One row of a <see cref="LegendModel"/>. A row always refers to a plot, so its swatch is whatever
/// that series is actually drawn with and can never drift; what the row owns is the label override,
/// whether it is included, and its position among the other rows.
/// <para>
/// <see cref="GraphObject.Visible"/> means "included in the legend" — unchecking a row hides the row,
/// not the series.
/// </para>
/// </summary>
public sealed class LegendEntryModel : GraphObject
{
    private PlotObject? _plot;
    private string? _label;

    public LegendEntryModel()
    {
        Name = "Legend entry";
    }

    /// <summary>
    /// The series this row legends. An in-memory reference: documents store the plot's index within
    /// its axes instead, and the sync pass re-establishes the link after a load.
    /// </summary>
    [Browsable(false)]
    public PlotObject? Plot
    {
        get => _plot;
        set => SetProperty(ref _plot, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// Overrides the label taken from the series. Null or empty means "use the series' own legend
    /// label", so renaming the series keeps flowing through until the row is deliberately renamed.
    /// </summary>
    [Category("General")]
    public string? Label
    {
        get => _label;
        set => SetProperty(ref _label, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The group this row stands for, when a script legended a group (open item 313): the row's
    /// <see cref="Plot"/> is then the group's first member, which gives the swatch, and the words are
    /// the group's <see cref="GraphicsGroupModel.DisplayName"/>. Null for an ordinary row.
    /// </summary>
    [Browsable(false)]
    public GraphicsGroupModel? Group
    {
        get => _group;
        set => SetProperty(ref _group, value, InvalidationKind.Layout);
    }

    private GraphicsGroupModel? _group;

    /// <summary>The words this row shows: its override, else its group's name, else <paramref name="seriesLabel"/>.</summary>
    public string ResolveRowLabel(string seriesLabel) =>
        ResolveLabel(_group is { } group ? group.DisplayName : seriesLabel);

    /// <summary>The override if there is one, else <paramref name="seriesLabel"/>.</summary>
    public string ResolveLabel(string seriesLabel) =>
        string.IsNullOrEmpty(_label) ? seriesLabel : _label;
}
