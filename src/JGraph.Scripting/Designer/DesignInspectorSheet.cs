namespace JGraph.Scripting.Designer;

/// <summary>Which of R2025b's views a selection gets (<c>_PropertyViewFactory.getView</c>).</summary>
public enum DesignInspectionKind
{
    /// <summary>One component.</summary>
    Single,

    /// <summary>A labelled component selected whole: its field's rows, with the label's text over them.</summary>
    Labeled,

    /// <summary>Several components of one class.</summary>
    Homogeneous,

    /// <summary>Several components of different classes.</summary>
    Heterogeneous,
}

/// <summary>What a selection comes to in the inspector.</summary>
/// <param name="Kind">The view it gets.</param>
/// <param name="Components">The selection's components, a group's every one (<c>getComponents</c>), a labelled component's label first.</param>
/// <param name="Shown">What the rows are worked out from (<c>_determineComponentsToShow</c>): the anchor alone for a labelled component or with a custom component among them.</param>
/// <param name="Anchor">The component whose rows and values are shown (<c>getAnchorComponent</c>): the last.</param>
/// <param name="Labeled">The labelled component selected whole; null otherwise.</param>
public sealed record DesignInspection(
    DesignInspectionKind Kind,
    IReadOnlyList<DesignComponent> Components,
    IReadOnlyList<DesignComponent> Shown,
    DesignComponent Anchor,
    DesignGroup? Labeled);

/// <summary>A search's answer (<c>InspectorPropertySheet._handleSearch</c>).</summary>
/// <param name="Direct">The rows whose name holds the text, by name.</param>
/// <param name="Help">The rows whose help holds it, by name, under <see cref="DesignInspectorSheet.HelpResults"/>.</param>
public sealed record DesignInspectorMatches(IReadOnlyList<DesignEditor> Direct, IReadOnlyList<DesignEditor> Help)
{
    /// <summary>Whether nothing matched.</summary>
    public bool None => Direct.Count == 0 && Help.Count == 0;
}

/// <summary>
/// App Designer's inspector for a selection (the App Designer window plan's stage W7b), free of the screen: which
/// components it is over, the rows they share, the alphabetical view, the search, who an edit goes to and what the tab says,
/// transcribed from R2025b's <c>PropertyViewController</c>, <c>PropertyInspectorViewFactory</c>, the strategies,
/// <c>PropertyInspectorView._createInspectorObject</c> and <c>InspectorPropertySheet</c>.
/// </summary>
public static class DesignInspectorSheet
{
    /// <summary>What the tab says with several selected (<c>multipleComponentsInspectorTitle</c>).</summary>
    public const string MultipleComponents = "Multiple Components";

    /// <summary>What the tab says with nothing selected (<c>noComponentInspectorTitle</c>).</summary>
    public const string NoComponent = "Component";

    /// <summary>A search that found nothing (<c>noMatchesFound</c>).</summary>
    public const string NoMatches = "No matches found";

    /// <summary>The heading over the rows a search found in their help (<c>helpDocResults</c>).</summary>
    public const string HelpResults = "Help documentation results";

    /// <summary>The names as long as this have their name in their help (<c>PROP_NAME_LENGTH_FOR_TOOLTIP</c>).</summary>
    public const int LongName = 24;

    /// <summary>The properties a labelled component's edit gives its label too (<c>LabelMassEditableProperties</c>).</summary>
    private static readonly HashSet<string> LabelMassEditable = new(StringComparer.Ordinal)
    {
        "FontName", "FontAngle", "FontSize", "FontWeight", "FontColor", "BackgroundColor", "Enable", "Visible",
    };

    private const string MenuType = "matlab.ui.container.Menu";
    private const string ContextMenuType = "matlab.ui.container.ContextMenu";
    private const string ButtonGroupType = "matlab.ui.container.ButtonGroup";
    private const string ToggleButtonType = "matlab.ui.control.ToggleButton";

    /// <summary>
    /// What <paramref name="picks"/> come to (<c>_decorateSelection</c>, <c>getView</c>): null for no pick; a labelled
    /// component's field alone when one is selected whole; else every component, a group's every one, the last as the anchor -
    /// and with a custom component among them, which takes no part in several being edited, the anchor alone.
    /// </summary>
    public static DesignInspection? Inspect(IReadOnlyList<DesignPick> picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        if (picks.Count == 0)
        {
            return null;
        }

        if (picks is [{ Group: { IsLabeled: true } labeled }])
        {
            return new DesignInspection(DesignInspectionKind.Labeled, [labeled.Label!, labeled.Field!], [labeled.Field!], labeled.Field!, labeled);
        }

        DesignComponent[] components = picks.SelectMany(static p => p.Leaves()).ToArray();
        if (components.Length == 0)
        {
            return null;
        }

        DesignComponent anchor = components[^1];
        DesignInspectionKind kind = picks is [{ Component: not null }] ? DesignInspectionKind.Single
            : components.All(c => c.Type == components[0].Type) ? DesignInspectionKind.Homogeneous
            : DesignInspectionKind.Heterogeneous;
        IReadOnlyList<DesignComponent> shown = components.Any(DesignUserComponents.IsUserComponent) ? [anchor] : components;
        return new DesignInspection(kind, components, shown, anchor, null);
    }

    /// <summary>
    /// The groups the inspector shows (<c>_createInspectorObject</c>, <c>_intersectObjectProperties</c>, <c>_renderPropertyItem</c>):
    /// the anchor's (<paramref name="anchorGroups"/>), a menu's as <see cref="ForMenu"/> leaves them, with the rows every
    /// component shown has, each typed as the first of them types it; a group or sub-group left with no rows goes.
    /// </summary>
    public static IReadOnlyList<DesignInspectorGroup> Sheet(DesignInspection inspection, IReadOnlyList<DesignInspectorGroup> anchorGroups)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(anchorGroups);
        IReadOnlyList<DesignInspectorGroup> groups = ForMenu(inspection.Anchor, anchorGroups);
        if (inspection.Shown.Count < 2)
        {
            return groups;
        }

        string first = inspection.Shown[0].Type;
        string[] others = inspection.Shown.Skip(1).Select(static c => c.Type).Distinct(StringComparer.Ordinal).ToArray();
        bool Shared(DesignEditor editor)
        {
            string? firstClass = DesignInspector.R2025b.ClassNameOf(first, editor.SheetName);
            return firstClass is not null && others.All(t => DesignInspector.R2025b.ClassNameOf(t, editor.SheetName) == firstClass);
        }

        var kept = new List<DesignInspectorGroup>();
        foreach (DesignInspectorGroup group in groups)
        {
            var rows = new List<DesignInspectorRow>();
            foreach (DesignInspectorRow row in group.Rows)
            {
                DesignEditor[] editors = row.Editors.Where(Shared).ToArray();
                if (editors.Length > 0)
                {
                    rows.Add(row with { Editors = editors });
                }
            }

            if (rows.Count > 0)
            {
                kept.Add(group with { Rows = rows });
            }
        }

        return kept;
    }

    /// <summary>
    /// A menu's rows (<c>_adjustInspectorInfo</c>): a menu of the menu bar has no Accelerator, Separator or Checked; one holding
    /// menus under a menu or context menu no Accelerator or Checked. Other classes' are as they are.
    /// </summary>
    public static IReadOnlyList<DesignInspectorGroup> ForMenu(DesignComponent anchor, IReadOnlyList<DesignInspectorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(groups);
        if (anchor.Type != MenuType || groups.Count == 0)
        {
            return groups;
        }

        string?[] removed = anchor.Parent?.Type is MenuType or ContextMenuType
            ? anchor.Children.Count > 0 ? ["Accelerator", "Checked"] : []
            : ["Accelerator", "Separator", "Checked"];
        if (removed.Length == 0)
        {
            return groups;
        }

        DesignInspectorRow[] rows = groups[0].Rows.Where(r => !r.Editors.Any(e => removed.Contains(e.SheetName))).ToArray();
        return [groups[0] with { Rows = rows }, .. groups.Skip(1)];
    }

    /// <summary>Every row of <paramref name="groups"/>, alphabetically by what it says as JavaScript's <c>&lt;</c> orders it (<c>_comparisonFcnForSort</c>).</summary>
    public static IReadOnlyList<DesignEditor> Alphabetical(IReadOnlyList<DesignInspectorGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.SelectMany(static g => g.Rows).SelectMany(static r => r.Editors)
            .OrderBy(static e => e.DisplayName, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The rows a search keeps (<c>_handleSearch</c>), in any case: those saying the text first; then those whose help holds it. The
    /// text is taken as it is typed, where R2025b's makes a pattern of it, which throws on a bracket.
    /// </summary>
    public static DesignInspectorMatches Search(IReadOnlyList<DesignEditor> rows, string text)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(text);
        var direct = new List<DesignEditor>();
        var help = new List<DesignEditor>();
        foreach (DesignEditor row in rows)
        {
            if (text.Length == 0 || row.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                direct.Add(row);
            }
            else if (row.Tooltip.Replace("||", string.Empty, StringComparison.Ordinal).Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                help.Add(row);
            }
        }

        return new DesignInspectorMatches(
            direct.OrderBy(static e => e.DisplayName, StringComparer.Ordinal).ToArray(),
            help.OrderBy(static e => e.DisplayName, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Who an edit of <paramref name="name"/> goes to (<c>_getComponentsAndPropertiesForEdit</c>): a labelled component's field,
    /// and its label for its font, colours, Enable and Visible; else all those shown; and for <c>AutoResizeChildren</c> every one
    /// of them and of what is inside them that has it.
    /// </summary>
    public static IReadOnlyList<DesignComponent> EditTargets(DesignInspection inspection, string name)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(name);
        if (inspection.Labeled is not null)
        {
            return LabelMassEditable.Contains(name) ? inspection.Components : [inspection.Anchor];
        }

        bool editsAll = inspection.Shown.Count == inspection.Components.Count;
        IReadOnlyList<DesignComponent> targets = editsAll ? inspection.Components : [inspection.Anchor];
        if (name != "AutoResizeChildren" || !editsAll)
        {
            return targets;
        }

        var inside = new List<DesignComponent>();
        void Walk(DesignComponent c)
        {
            if (DesignInspector.R2025b.ClassNameOf(c.Type, name) is not null)
            {
                inside.Add(c);
            }

            foreach (DesignComponent child in c.Children)
            {
                Walk(child);
            }
        }

        foreach (DesignComponent target in targets)
        {
            Walk(target);
        }

        return inside;
    }

    /// <summary>
    /// What the inspector's first tab says (<c>_updateContentPaneTitle</c>), its other word going to a pane title R2025b's
    /// <c>ContentPane</c> never shows: <see cref="NoComponent"/> for nothing, <see cref="MultipleComponents"/> for several, a
    /// labelled component's field's class, a button group's kind, and any other's class.
    /// </summary>
    public static string TabOf(IReadOnlyList<DesignPick> picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        if (picks is [{ Group: { IsLabeled: true } labeled }])
        {
            return TabOfClass(labeled.Field!.Type);
        }

        DesignComponent[] components = picks.SelectMany(static p => p.Leaves()).ToArray();
        return components.Length switch
        {
            0 => NoComponent,
            1 => components[0].Type == ButtonGroupType
                ? components[0].Children.Any(static c => c.Type == ToggleButtonType) ? "Toggle Button Group" : "Radio Button Group"
                : TabOfClass(components[0].Type),
            _ => MultipleComponents,
        };
    }

    // A menu's says its default text, capitalized, where its model's name is the menu bar's.
    private static string TabOfClass(string type) =>
        type == MenuType ? "Menu" : DesignInspector.R2025b.TabOf(type) ?? type[(type.LastIndexOf('.') + 1)..];

    /// <summary>
    /// The key the inspector keeps its open groups, sub-groups and view under (<c>_getIDForCache</c>, <c>adjustPropertyInspector</c>):
    /// one component's class, a grid's child apart; several's classes in order, each once. A different number of
    /// properties under the same key starts afresh, as R2025b's cache makes a new sheet.
    /// </summary>
    public static (string Key, int Properties) KeyOf(DesignInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        int properties = DesignInspector.R2025b.PropertyCount(inspection.Anchor.Type);
        if (inspection.Shown.Count == 1)
        {
            return (inspection.Anchor.Type + (inspection.Anchor.IsInGrid ? "_grid" : string.Empty), properties);
        }

        return (string.Join(',', inspection.Shown.Select(static c => c.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), properties);
    }

    /// <summary>A row's help (<c>_createTooltip</c>): its heading and body from the sheet's <c>||</c>, under its name when that is long; all null for none.</summary>
    public static (string? Name, string? Heading, string? Body) Help(DesignEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        string? name = editor.DisplayName.Length >= LongName ? editor.DisplayName : null;
        if (editor.Tooltip.Length == 0)
        {
            return (name, null, null);
        }

        string[] parts = editor.Tooltip.Split("||");
        return parts.Length == 1 ? (name, null, parts[0]) : (name, parts[0], parts[1]);
    }
}

/// <summary>What the inspector keeps for an app between selections (<c>InspectorClientManager.objectCache</c>): for each key, what is open and which view shows.</summary>
public sealed class DesignInspectorState
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>What is kept under <paramref name="key"/>, a different number of properties starting afresh.</summary>
    public Entry For(string key, int properties)
    {
        if (!_entries.TryGetValue(key, out Entry? entry) || entry.Properties != properties)
        {
            entry = new Entry(properties);
            _entries[key] = entry;
        }

        return entry;
    }

    /// <summary>What is kept for one key.</summary>
    public sealed class Entry(int properties)
    {
        /// <summary>How many properties the sheet had.</summary>
        public int Properties { get; } = properties;

        /// <summary>Whether the alphabetical view is the one chosen.</summary>
        public bool Alphabetical { get; set; }

        /// <summary>Each group's and sub-group's state set by hand, by its title and sub-group's number.</summary>
        public Dictionary<string, bool> Open { get; } = new(StringComparer.Ordinal);
    }
}
