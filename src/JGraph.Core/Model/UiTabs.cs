using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>MATLAB's <c>TabLocation</c>: the edge of a tab group its tabs' headings run along.</summary>
public enum UiTabLocation
{
    Top,
    Left,
    Bottom,
    Right,
}

/// <summary>
/// MATLAB's <c>uitabgroup</c> (<c>matlab.ui.container.TabGroup</c>; app-building plan, U8): a box
/// with a strip of headings along one edge, holding tabs and nothing else, one of which shows. Its
/// children are placed in what the strip and a one-pixel border leave — R2025b's settled sizes
/// (probe <c>u8_behave</c>): 24 pixels for a strip along the top or the bottom, and along a side a
/// strip as wide as its widest heading, between 66 and 101 pixels.
/// </summary>
public sealed class UiTabGroupModel : UiContainerModel
{
    /// <summary>The height of a strip along the top or the bottom, with the line under it.</summary>
    public const double StripHeight = 23;

    private UiTabLocation _location = UiTabLocation.Top;
    private UiTabModel? _selected;

    public UiTabGroupModel()
    {
        Name = "TabGroup";
        Units = UiUnits.Normalized;
        Position = new Rect2D(0, 0, 1, 1);
    }

    /// <summary>A tab group with the defaults one made in a <c>uifigure</c> has.</summary>
    public static UiTabGroupModel ForUiFigure() => new()
    {
        Units = UiUnits.Pixels,
        Position = new Rect2D(20, 20, 250, 210),
        AutoResizeChildren = true,
    };

    [Browsable(false)]
    public UiTabLocation TabLocation
    {
        get => _location;
        set => SetProperty(ref _location, value, InvalidationKind.Ui);
    }

    /// <summary>The tabs, in the order their headings stand.</summary>
    [Browsable(false)]
    public IEnumerable<UiTabModel> Tabs => Components.OfType<UiTabModel>();

    /// <summary>MATLAB's <c>SelectedTab</c>: the tab that shows, or null when the group has none.</summary>
    [Browsable(false)]
    public UiTabModel? SelectedTab =>
        _selected is { } selected && ReferenceEquals(selected.Parent, this) ? selected : Tabs.FirstOrDefault();

    /// <summary>Shows a tab of this group. The caller has checked that it is one.</summary>
    public void Select(UiTabModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!ReferenceEquals(_selected, tab))
        {
            _selected = tab;
            Invalidate(InvalidationKind.Ui);
        }
    }

    /// <summary>
    /// A tab is about to leave the group, deleted or moved. When it is the one showing, the tab after
    /// it shows instead, or the one before it when it was the last (R2025b, probe <c>u8_behave</c>).
    /// </summary>
    public void Leaving(UiTabModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!ReferenceEquals(SelectedTab, tab))
        {
            return;
        }

        List<UiTabModel> tabs = [.. Tabs];
        int at = tabs.IndexOf(tab);
        _selected = at + 1 < tabs.Count ? tabs[at + 1] : at > 0 ? tabs[at - 1] : null;
        Invalidate(InvalidationKind.Ui);
    }

    /// <summary>The width of a strip along a side: its widest heading and 24 pixels, within R2025b's limits.</summary>
    public double SideStripWidth()
    {
        double widest = 0;
        foreach (UiTabModel tab in Tabs)
        {
            widest = System.Math.Max(widest, UiFit.TextWidth([tab.Title], "Helvetica", 12, bold: false, italic: false));
        }

        return System.Math.Clamp(System.Math.Ceiling(widest) + 24, 66, 101);
    }

    /// <inheritdoc />
    public override Thickness Insets() => _location switch
    {
        UiTabLocation.Top => new Thickness(1, StripHeight + 1, 1, 1),
        UiTabLocation.Bottom => new Thickness(1, 1, 1, StripHeight + 1),
        UiTabLocation.Left => new Thickness(SideStripWidth() + 1, 1, 1, 1),
        _ => new Thickness(1, 1, SideStripWidth() + 1, 1),
    };
}

/// <summary>
/// MATLAB's <c>uitab</c> (<c>matlab.ui.container.Tab</c>): one page of a tab group. It has no
/// place of its own — it fills what its group leaves its tabs — and shows only while it is the
/// group's <c>SelectedTab</c>.
/// </summary>
public sealed class UiTabModel : UiContainerModel
{
    private string _title = string.Empty;
    private UiColor? _background = UiPanelModel.DefaultBackground;
    private UiColor? _foreground = UiComponentModel.DefaultFontColor;

    public UiTabModel()
    {
        Name = "Tab";
        Units = UiUnits.Normalized;
        Position = new Rect2D(0, 0, 1, 1);
    }

    /// <summary>A tab with the defaults one made in a <c>uifigure</c> has.</summary>
    public static UiTabModel ForUiFigure() => new()
    {
        Units = UiUnits.Pixels,
        AutoResizeChildren = true,
    };

    /// <summary>The heading.</summary>
    [Browsable(false)]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The page's colour, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }

    /// <summary>The heading's colour, null for <c>'none'</c>.</summary>
    [Browsable(false)]
    public UiColor? ForegroundColor
    {
        get => _foreground;
        set => SetProperty(ref _foreground, value, InvalidationKind.Ui);
    }

    /// <summary>The group this tab is a page of, or null while it is detached.</summary>
    [Browsable(false)]
    public UiTabGroupModel? Group => Parent as UiTabGroupModel;

    /// <summary>Whether this is the page its group shows.</summary>
    [Browsable(false)]
    public bool IsShowing => Group is { } group && ReferenceEquals(group.SelectedTab, this);

    /// <inheritdoc />
    public override Thickness Insets() => new(0);
}
