namespace JGraph.Scripting.Toolstrip;

/// <summary>
/// The kinds of control a toolstrip column holds, as R2025b's toolstrip framework names them
/// (<c>MW/toolstrip/constants/TypeConstants</c>; the App Designer window plan's stage W1).
/// </summary>
public enum ToolstripItemKind
{
    /// <summary>A button that runs its action.</summary>
    PushButton,

    /// <summary>A button that runs its action, with an arrow beside it that opens a popup.</summary>
    SplitButton,

    /// <summary>A button that only opens a popup.</summary>
    DropDownButton,

    /// <summary>A button that stays pressed; in a button group, one of a set of choices.</summary>
    ToggleButton,

    /// <summary>A drop-down list, editable or not.</summary>
    ComboBox,

    /// <summary>A number field with up and down arrows.</summary>
    Spinner,

    /// <summary>A text field.</summary>
    TextField,

    /// <summary>A line of text.</summary>
    Label,

    /// <summary>A gap the height of one small control.</summary>
    EmptyControl,

    /// <summary>A block of controls inside a column: its columns side by side, any other control on a row of its own.</summary>
    Panel,
}

/// <summary>What an entry of a popup list is.</summary>
public enum ToolstripPopupEntryKind
{
    /// <summary>An item that runs its action (<c>ListItem</c>).</summary>
    Item,

    /// <summary>An item with a check mark that runs its action (<c>ListItemWithCheckBox</c>).</summary>
    CheckItem,

    /// <summary>A heading over the items that follow (<c>PopupListHeader</c>).</summary>
    Header,

    /// <summary>A rule between items (<c>PopupListSeparator</c>).</summary>
    Separator,

    /// <summary>A place the host fills with items each time the popup opens: recent files, argument sets, functions (<c>PopupListPanel</c>).</summary>
    Panel,
}

/// <summary>
/// A control in a toolstrip column. Words are the item's own or its action's (W0 recorded both and
/// wrote the resolved form); a line break in <see cref="Text"/> splits a large button's label.
/// </summary>
public sealed record ToolstripItem
{
    /// <summary>What the control is.</summary>
    public required ToolstripItemKind Kind { get; init; }

    /// <summary>The control's tag, unique in the toolstrip when given; the automation id is built from it.</summary>
    public string? Tag { get; init; }

    /// <summary>The label, or null for a control drawn by its icon alone.</summary>
    public string? Text { get; init; }

    /// <summary>The tooltip.</summary>
    public string? Description { get; init; }

    /// <summary>The icon's name in R2025b's icon set.</summary>
    public string? Icon { get; init; }

    /// <summary>The icon the quick access bar shows for this control, when it differs.</summary>
    public string? QuickAccessIcon { get; init; }

    /// <summary>The keyboard shortcut shown with the tooltip, in R2025b's words ("Ctrl+S").</summary>
    public string? Shortcut { get; init; }

    /// <summary>The action the control runs: the host binds commands by this id.</summary>
    public string? ActionId { get; init; }

    /// <summary>The popup a split or drop-down button opens.</summary>
    public string? PopupTag { get; init; }

    /// <summary>Whether the popup holds a panel the host fills when it opens.</summary>
    public bool HasDynamicPopup { get; init; }

    /// <summary>The group of toggle buttons only one of which is pressed at a time.</summary>
    public string? ButtonGroup { get; init; }

    /// <summary>The key tip given by the layout (upper-case letters), or null.</summary>
    public string? Mnemonic { get; init; }

    /// <summary>Whether a toggle starts pressed.</summary>
    public bool Selected { get; init; }

    /// <summary>Whether the control starts enabled, when the layout says.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Whether a combo box or spinner takes typing.</summary>
    public bool Editable { get; init; }

    /// <summary>A combo box's choices.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>A combo box's chosen item, or a spinner's or text field's value.</summary>
    public string? Value { get; init; }

    /// <summary>The text a spinner shows when what was typed is not a number in its limits.</summary>
    public string? ErrorText { get; init; }

    /// <summary>A spinner's least value.</summary>
    public double? Minimum { get; init; }

    /// <summary>A spinner's greatest value.</summary>
    public double? Maximum { get; init; }

    /// <summary>A spinner's step.</summary>
    public double Step { get; init; } = 1;

    /// <summary>A panel's columns and controls, in order.</summary>
    public IReadOnlyList<ToolstripPanelChild> Children { get; init; } = [];

    /// <summary>Whether the control is one of the framework's sizeable buttons, the ones a column lays out large or small.</summary>
    public bool IsButton => Kind is ToolstripItemKind.PushButton or ToolstripItemKind.SplitButton
        or ToolstripItemKind.DropDownButton or ToolstripItemKind.ToggleButton;
}

/// <summary>A panel's child: a column of controls, or a control on a row of its own.</summary>
/// <param name="Column">The column, when the child is one.</param>
/// <param name="Item">The control, when the child is one.</param>
public sealed record ToolstripPanelChild(ToolstripColumn? Column, ToolstripItem? Item);

/// <summary>A column of a section: one control drawn large, or a stack of small ones.</summary>
/// <param name="Tag">The column's tag.</param>
/// <param name="Width">The column's fixed width in pixels, or 0 to fit its controls.</param>
/// <param name="HorizontalAlignment">"left", "center" or "right", or null for the framework's default (left).</param>
/// <param name="Items">The controls, top to bottom.</param>
public sealed record ToolstripColumn(string? Tag, double Width, string? HorizontalAlignment, IReadOnlyList<ToolstripItem> Items);

/// <summary>A titled group of columns on a tab; it is what collapses to a drop-down when the tab is too narrow.</summary>
/// <param name="Tag">The section's tag.</param>
/// <param name="Title">The title under the section ("FILE"), or null.</param>
/// <param name="Columns">The columns, left to right.</param>
/// <param name="Icon">The icon of the drop-down the section collapses to (the framework's <c>section_24</c> by default).</param>
/// <param name="CollapsePriority">Higher collapses later; -1 never collapses (the framework's default 0 for all of App Designer's).</param>
public sealed record ToolstripSection(string? Tag, string? Title, IReadOnlyList<ToolstripColumn> Columns, string Icon = "section_24", int CollapsePriority = 0);

/// <summary>A tab of the toolstrip.</summary>
/// <param name="Tag">The tab's tag.</param>
/// <param name="Title">The title on the tab ("DESIGNER").</param>
/// <param name="GroupTag">The tab group the tab came in; a host shows contextual tabs by group.</param>
/// <param name="IsContextual">Whether the tab is shown only in some contexts (a view of the document).</param>
/// <param name="Sections">The sections, left to right.</param>
/// <param name="Mnemonic">The tab's key tip, or null for one chosen from its title.</param>
public sealed record ToolstripTab(string Tag, string Title, string? GroupTag, bool IsContextual, IReadOnlyList<ToolstripSection> Sections, string? Mnemonic = null);

/// <summary>An entry of a popup list.</summary>
public sealed record ToolstripPopupEntry
{
    /// <summary>What the entry is.</summary>
    public required ToolstripPopupEntryKind Kind { get; init; }

    /// <summary>The entry's tag.</summary>
    public string? Tag { get; init; }

    /// <summary>The item's words, or a header's title.</summary>
    public string? Text { get; init; }

    /// <summary>The item's description: its tooltip, or a second line when <see cref="ShowDescription"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The icon's name.</summary>
    public string? Icon { get; init; }

    /// <summary>The keyboard shortcut shown at the item's right.</summary>
    public string? Shortcut { get; init; }

    /// <summary>The action the item runs.</summary>
    public string? ActionId { get; init; }

    /// <summary>Whether the description is drawn under the words.</summary>
    public bool ShowDescription { get; init; }

    /// <summary>Whether a check item starts checked.</summary>
    public bool Selected { get; init; }

    /// <summary>Whether choosing a check item closes the popup (the framework's default is to keep it open).</summary>
    public bool ClosePopupOnClick { get; init; }

    /// <summary>The tallest a panel grows before it scrolls, or 0 for no limit.</summary>
    public double MaxHeight { get; init; }
}

/// <summary>A popup list a split or drop-down button opens.</summary>
/// <param name="Tag">The popup's tag, named by the buttons that open it.</param>
/// <param name="Entries">The entries, top to bottom.</param>
public sealed record ToolstripPopup(string Tag, IReadOnlyList<ToolstripPopupEntry> Entries);

/// <summary>
/// A whole toolstrip as data (the plan's "layout as recorded data"): the tabs, the popups their
/// buttons open, and the quick access bar. The control renders it and knows nothing of the host.
/// </summary>
/// <param name="Tabs">The tabs, left to right; contextual ones are shown by the host.</param>
/// <param name="Popups">The popups, by tag.</param>
/// <param name="QuickAccess">The quick access bar's buttons, left to right.</param>
public sealed record ToolstripLayout(IReadOnlyList<ToolstripTab> Tabs, IReadOnlyDictionary<string, ToolstripPopup> Popups, IReadOnlyList<ToolstripItem> QuickAccess)
{
    /// <summary>A layout with nothing in it.</summary>
    public static ToolstripLayout Empty { get; } = new([], new Dictionary<string, ToolstripPopup>(), []);

    /// <summary>The popup of that tag, or null.</summary>
    public ToolstripPopup? Popup(string? tag) => tag is not null && Popups.TryGetValue(tag, out ToolstripPopup? popup) ? popup : null;

    /// <summary>
    /// The layout with each control replaced by what <paramref name="map"/> makes of it - panels'
    /// controls included, the quick access bar's too - as a host fills in limits, icons or words.
    /// </summary>
    public ToolstripLayout MapItems(Func<ToolstripItem, ToolstripItem> map)
    {
        ToolstripItem Item(ToolstripItem item)
        {
            ToolstripItem mapped = map(item);
            return mapped.Children.Count == 0 ? mapped : mapped with
            {
                Children = [.. mapped.Children.Select(c => new ToolstripPanelChild(c.Column is null ? null : Column(c.Column), c.Item is null ? null : Item(c.Item)))],
            };
        }

        ToolstripColumn Column(ToolstripColumn column) => column with { Items = [.. column.Items.Select(Item)] };

        return this with
        {
            Tabs = [.. Tabs.Select(t => t with { Sections = [.. t.Sections.Select(s => s with { Columns = [.. s.Columns.Select(Column)] })] })],
            QuickAccess = [.. QuickAccess.Select(map)],
        };
    }

    /// <summary>Every control of every tab, panels' controls included, in order.</summary>
    public IEnumerable<ToolstripItem> AllItems()
    {
        foreach (ToolstripTab tab in Tabs)
        {
            foreach (ToolstripItem item in tab.Sections.SelectMany(s => s.Columns).SelectMany(ItemsOf))
            {
                yield return item;
            }
        }
    }

    /// <summary>A column's controls, a panel's included after the panel.</summary>
    public static IEnumerable<ToolstripItem> ItemsOf(ToolstripColumn column)
    {
        foreach (ToolstripItem item in column.Items)
        {
            yield return item;
            foreach (ToolstripPanelChild child in item.Children)
            {
                if (child.Item is not null)
                {
                    yield return child.Item;
                }
                else if (child.Column is not null)
                {
                    foreach (ToolstripItem inner in ItemsOf(child.Column))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }
}
