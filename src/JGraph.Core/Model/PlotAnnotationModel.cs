using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>How a series shows in a legend: with its own row, not at all, or through its children.</summary>
public enum LegendIconDisplay
{
    /// <summary>The series gets a legend row — MATLAB's default.</summary>
    On,

    /// <summary>The series is left out of the legend, though it is still drawn.</summary>
    Off,

    /// <summary>The children carry the rows. Nothing here has children that draw, so this is on.</summary>
    Children,
}

/// <summary>
/// The little object MATLAB hangs off every plot as <c>h.Annotation</c>, whose one useful part is
/// <see cref="LegendInformation"/>. It carries no drawing of its own: it exists so a script can say
/// <c>h.Annotation.LegendInformation.IconDisplayStyle = 'off'</c>, which is the documented way to keep
/// one series out of a legend while leaving every other one alone.
/// </summary>
public sealed class PlotAnnotationModel : GraphObject
{
    public PlotAnnotationModel()
    {
        Name = "Annotation";
        LegendInformation.SetParent(this);
    }

    /// <summary>The legend row this series would take, and whether it takes one.</summary>
    [Browsable(false)]
    public LegendEntryInfoModel LegendInformation { get; } = new();
}

/// <summary>
/// Whether a series appears in its axes' legend. MATLAB reaches this through
/// <c>Annotation.LegendInformation</c> rather than through a property on the series, and this is the
/// same two-step so that the spelling a script already knows is the spelling that works.
/// </summary>
public sealed class LegendEntryInfoModel : GraphObject
{
    private LegendIconDisplay _iconDisplayStyle = LegendIconDisplay.On;

    public LegendEntryInfoModel() => Name = "LegendEntry";

    /// <summary>Whether the owning series takes a legend row.</summary>
    [Category("Appearance"), DisplayName("Icon display style")]
    public LegendIconDisplay IconDisplayStyle
    {
        get => _iconDisplayStyle;
        set => SetProperty(ref _iconDisplayStyle, value, InvalidationKind.Layout);
    }
}
