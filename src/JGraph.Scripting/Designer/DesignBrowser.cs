namespace JGraph.Scripting.Designer;

/// <summary>What App Designer's component browser lists, and in what order (<c>ComponentTreeDisplayStrategyConstants</c>).</summary>
public enum DesignBrowserList
{
    /// <summary>Stack order without the labels of labelled components (<c>ExcludeLabelsComponentTreeDisplayStrategy</c>), the factory view.</summary>
    ExcludeLabels,

    /// <summary>Stack order with every component (<c>CompleteComponentTreeDisplayStrategy</c>).</summary>
    Complete,

    /// <summary>What Tab goes to, in the order it goes (<c>TabOrderComponentTreeDisplayStrategy</c>).</summary>
    TabOrder,
}

/// <summary>Where a dragged row is dropped among the rows of its target (the tree store's <c>_calcPlacementIndex</c>).</summary>
public enum DesignBrowserPlace
{
    /// <summary>First among them: a drop on a row.</summary>
    First,

    /// <summary>Before a row.</summary>
    Before,

    /// <summary>After a row.</summary>
    After,
}

/// <summary>One row of the browser: the app (<paramref name="Component"/> null), or a component under the row of <paramref name="Parent"/>, the app's row for the figure.</summary>
/// <param name="Component">The row's component; null for the app.</param>
/// <param name="Parent">The component of the row above it in the tree; null for the app and the figure.</param>
/// <param name="Label">What it says: the app's name, or <c>app.CodeName</c>.</param>
public sealed record DesignBrowserRow(DesignComponent? Component, DesignComponent? Parent, string Label)
{
    /// <summary>Whether this is the app's row, which is not renamed, moved or deleted (<c>_InspectableRootNodeModel</c>).</summary>
    public bool IsApp => Component is null;
}

/// <summary>One place of the browser's collapsed form (<c>_generateBreadcrumbDataFromSelection</c>): what it says, and what a click selects.</summary>
/// <param name="Text">What it says, cut at 20 characters (<c>Breadcrumbs._MAX_LENGTH_OF_LABEL</c>).</param>
/// <param name="Component">The component it selects; null for the app or nothing.</param>
/// <param name="IsApp">Whether it is the app's place.</param>
public sealed record DesignBreadcrumb(string Text, DesignComponent? Component, bool IsApp);

/// <summary>
/// One app's browser as the user left it (<c>ComponentTreeController</c>, one per app): what it lists and
/// what it listed before, the search, and whether it is collapsed to its breadcrumbs.
/// </summary>
public sealed class DesignBrowserState
{
    /// <summary>A browser opened with the setting <c>IncludeComponentLabels</c> as <paramref name="includeLabels"/> (<c>_setupStrategy</c>).</summary>
    public DesignBrowserState(bool includeLabels = false)
    {
        View = includeLabels ? DesignBrowserList.Complete : DesignBrowserList.ExcludeLabels;
        Previous = View;
    }

    /// <summary>What it lists now.</summary>
    public DesignBrowserList View { get; private set; }

    /// <summary>What it listed before, which leaving the tab order goes back to (<c>_previousStrategy</c>).</summary>
    public DesignBrowserList Previous { get; private set; }

    /// <summary>What is searched for; empty for nothing.</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>Whether it shows breadcrumbs for the tree (<c>ObjectBrowser.showBreadcrumbs</c>).</summary>
    public bool Collapsed { get; set; }

    /// <summary>The rows closed by the user, which stay closed as the tree is made again.</summary>
    public HashSet<DesignComponent> Closed { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Lists <paramref name="view"/> (<c>_setComponentTreeDisplayStrategy</c>).</summary>
    public void SetView(DesignBrowserList view)
    {
        if (view != View)
        {
            Previous = View;
            View = view;
        }
    }

    /// <summary>The toolbar's and the menu's Sort by Stack Order: back to what it listed before the tab order.</summary>
    public void ShowStackOrder()
    {
        if (View == DesignBrowserList.TabOrder)
        {
            SetView(Previous);
        }
    }

    /// <summary>The setting changed (<c>_modifyStrategyBasedOnSetting</c>): during the tab order, it is what the stack order goes back to.</summary>
    public void SettingChanged(bool includeLabels)
    {
        DesignBrowserList view = includeLabels ? DesignBrowserList.Complete : DesignBrowserList.ExcludeLabels;
        if (View == DesignBrowserList.TabOrder)
        {
            Previous = view;
        }
        else
        {
            SetView(view);
        }
    }
}

/// <summary>
/// App Designer's component browser (App Window plan, stage W7a), free of the screen: the rows of each view
/// (<c>ComponentTreeViewModel</c> and its strategies), the search (<c>mw-data-model</c>'s filter), which
/// rows can be dragged and where to (<c>ComponentTreeView._validateDrag</c>, <c>_validateDrop</c>, each
/// model's <c>ComponentTreeBehavior</c>), the order a drop leaves (<c>TreeDragReorderCommand</c>), what a
/// picked set of rows selects (<c>ComponentTreeView._setSelection</c>), the breadcrumbs and the browser's
/// height (<c>ComponentBrowserLayoutManager._layoutChangeHandler</c>). The oracle
/// <c>ui-probes/w7/oracle/browser.js</c> runs R2025b's own code for each of these.
/// </summary>
public static class DesignBrowser
{
    private const string LabelType = "matlab.ui.control.Label";
    private const string FigureType = "matlab.ui.Figure";
    private const string MenuType = "matlab.ui.container.Menu";
    private const string ContextMenuType = "matlab.ui.container.ContextMenu";
    private const string ToolbarType = "matlab.ui.container.Toolbar";
    private const string PushToolType = "matlab.ui.container.toolbar.PushTool";
    private const string ToggleToolType = "matlab.ui.container.toolbar.ToggleTool";
    private const string TabType = "matlab.ui.container.Tab";
    private const string TabGroupType = "matlab.ui.container.TabGroup";
    private const string TreeType = "matlab.ui.container.Tree";
    private const string CheckBoxTreeType = "matlab.ui.container.CheckBoxTree";
    private const string TreeNodeType = "matlab.ui.container.TreeNode";
    private const string UIAxesType = "matlab.ui.control.UIAxes";
    private const string SwitchType = "matlab.ui.control.Switch";

    /// <summary>A row's height, from which the tree's height is worked out (<c>ObjectBrowser._getTreeHeightInPixles</c>).</summary>
    public const double RowHeight = 21;

    /// <summary>What the browser asks for collapsed (<c>_emitLayout(25)</c>).</summary>
    public const double BreadcrumbHeight = 25;

    /// <summary>The name a row under no selection's tree says when nothing matches the search.</summary>
    public const string NoMatches = "No matches found";

    /// <summary>The Sort by Stack Order item's words.</summary>
    public const string SortByStackOrder = "Sort by Stack Order";

    /// <summary>The Sort &amp; Filter by Tab Order item's words.</summary>
    public const string SortByTabOrder = "Sort & Filter by Tab Order";

    /// <summary>The include labels item's words.</summary>
    public const string IncludeLabels = "Include component labels in Component Browser";

    /// <summary>The classes the tab order's view leaves out (<c>isShownInTabOrderTree</c>).</summary>
    private static readonly HashSet<string> NotInTabOrder = new(StringComparer.Ordinal)
    {
        LabelType, "matlab.ui.control.Knob", "matlab.ui.control.DiscreteKnob", "matlab.ui.control.Lamp", "matlab.ui.control.Gauge",
        "matlab.ui.control.LinearGauge", "matlab.ui.control.NinetyDegreeGauge", "matlab.ui.control.SemicircularGauge", SwitchType,
        "matlab.ui.control.ToggleSwitch", "matlab.ui.control.RockerSwitch", UIAxesType, ContextMenuType,
    };

    /// <summary>The classes that stay next to their own kind in a drag (<c>shouldComponentsOfSameTypeBeAdjacent</c>).</summary>
    private static bool Adjacent(string type) => type is UIAxesType or MenuType or ContextMenuType or ToolbarType;

    /// <summary>Whether a row of <paramref name="type"/> can go to another parent (<c>canDragReparentInTree</c>).</summary>
    private static bool CanReparent(string type) => type is SwitchType or TabType or TreeNodeType or MenuType or ContextMenuType or PushToolType or ToggleToolType;

    /// <summary>Whether children of <paramref name="type"/> come first added, last listed (<c>ReversedComponentViewOrder</c>).</summary>
    private static bool Reversed(string type) => type is not (TabType or TreeNodeType or MenuType or ContextMenuType or ToolbarType or PushToolType or ToggleToolType);

    /// <summary>Whether a class is selected only by itself among the browser's rows (<c>isSingleSelection</c>): a tab, a check box tree's node.</summary>
    private static bool IsSingleSelection(DesignComponent component) =>
        component.Type == TabType || (component.Type == TreeNodeType && DesignDocument.IsInCheckBoxTree(component));

    /// <summary>Whether a class is selected only with its own class (<c>IsSameTypeSelection</c>).</summary>
    private static bool IsSameTypeSelection(string type) => type is MenuType or ContextMenuType or ToolbarType;

    // --- the rows ----------------------------------------------------------------------------------

    /// <summary>The label of <paramref name="component"/> (<c>_createTreeData</c>).</summary>
    public static string LabelOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return "app." + component.CodeName;
    }

    /// <summary>
    /// The rows of <paramref name="view"/>, top to bottom, each under the row above it: the app, the figure,
    /// and the rest. A responsive app's grid is not listed and its regions stand under the figure
    /// (<c>IsInvisibleToUser</c>); a row whose parent <paramref name="view"/> leaves out is left out with it
    /// (a context menu's items in the tab order).
    /// </summary>
    public static IReadOnlyList<DesignBrowserRow> Rows(AppDesign design, string appName, DesignBrowserList view)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(appName);
        DesignComponent? hidden = DesignResponsive.Grid(design);
        var rows = new List<DesignBrowserRow> { new(null, null, appName), new(design.Figure, null, LabelOf(design.Figure)) };
        var shown = new HashSet<DesignComponent>(ReferenceEqualityComparer.Instance) { design.Figure };
        IEnumerable<DesignComponent> order = view == DesignBrowserList.TabOrder ? Depth(design.Figure) : DesignCodeWriter.ViewOrder(design).Skip(1);
        foreach (DesignComponent c in order)
        {
            if (ReferenceEquals(c, hidden) || !Listed(design, c, view))
            {
                continue;
            }

            DesignComponent up = c.Parent!;
            if (ReferenceEquals(up, hidden))
            {
                up = up.Parent!;
            }

            if (shown.Contains(up))
            {
                rows.Add(new DesignBrowserRow(c, up, LabelOf(c)));
                shown.Add(c);
            }
        }

        return rows;

        // _populateComponentList: the design's own order, each before its children.
        static IEnumerable<DesignComponent> Depth(DesignComponent container)
        {
            foreach (DesignComponent child in container.Children)
            {
                yield return child;
                foreach (DesignComponent c in Depth(child))
                {
                    yield return c;
                }
            }
        }
    }

    // shouldComponentBeDisplayed.
    private static bool Listed(AppDesign design, DesignComponent component, DesignBrowserList view) => view switch
    {
        DesignBrowserList.TabOrder => !NotInTabOrder.Contains(component.Type),
        DesignBrowserList.ExcludeLabels => !ReferenceEquals(design.LabeledGroupOf(component)?.Label, component),
        _ => true,
    };

    /// <summary>
    /// The rows a search keeps (<c>_filter.isFilterMatch</c>, <c>addAncestorsToFilter</c>): those whose label
    /// holds <paramref name="search"/> in any case, and the rows above them; all for no search.
    /// </summary>
    public static IReadOnlyList<DesignBrowserRow> Filtered(IReadOnlyList<DesignBrowserRow> rows, string search)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(search);
        if (search.Length == 0)
        {
            return rows;
        }

        string text = search.ToLowerInvariant();
        var keep = new HashSet<DesignBrowserRow>(ReferenceEqualityComparer.Instance);
        var byComponent = new Dictionary<DesignComponent, DesignBrowserRow>(ReferenceEqualityComparer.Instance);
        foreach (DesignBrowserRow row in rows)
        {
            if (row.Component is { } component)
            {
                byComponent[component] = row;
            }
        }

        DesignBrowserRow app = rows[0];
        foreach (DesignBrowserRow row in rows)
        {
            if (!row.Label.ToLowerInvariant().Contains(text, StringComparison.Ordinal))
            {
                continue;
            }

            keep.Add(row);
            for (DesignBrowserRow? up = row; up is { IsApp: false };)
            {
                up = up.Parent is { } parent ? byComponent[parent] : app;
                keep.Add(up);
            }
        }

        return rows.Where(keep.Contains).ToArray();
    }

    /// <summary>The spans of <paramref name="label"/> a search marks (<c>_search._searchNode</c>).</summary>
    public static IReadOnlyList<(int Start, int Count)> Matches(string label, string search)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(search);
        var spans = new List<(int, int)>();
        if (search.Length == 0)
        {
            return spans;
        }

        for (int at = label.IndexOf(search, StringComparison.OrdinalIgnoreCase); at >= 0; at = at + search.Length <= label.Length ? label.IndexOf(search, at + search.Length, StringComparison.OrdinalIgnoreCase) : -1)
        {
            spans.Add((at, search.Length));
        }

        return spans;
    }

    // --- drags -------------------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="sources"/> can be dragged (<c>_validateDrag</c>): each one movable, and one
    /// kind only among those that stay next to their own.
    /// </summary>
    public static bool CanDrag(IReadOnlyList<DesignComponent> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Any(static s => s.Parent is null))
        {
            return false;
        }

        int kinds = sources.Where(static s => Adjacent(s.Type)).Select(static s => s.Type).Distinct(StringComparer.Ordinal).Count();
        if (sources.Any(static s => !Adjacent(s.Type)))
        {
            kinds++;
        }

        return kinds <= 1;
    }

    /// <summary>
    /// Whether <paramref name="sources"/> can drop into <paramref name="target"/> (<c>_validateDrop</c>): where
    /// each may go to another parent, into what takes it (<c>canMoveToParent</c>); where not, only where
    /// they are. The app takes none; nor does a parent inside one of them (<c>isDraggedItemInTheNodePath</c>).
    /// </summary>
    /// <remarks>
    /// R2025b's <c>canMoveToParent</c> says yes to anything for a slider switch - a button, the app - and lets a
    /// context menu go into a menu, each of which then fails in MATLAB: those are refused here.
    /// </remarks>
    public static bool CanDrop(IReadOnlyList<DesignComponent> sources, DesignComponent? target)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (target is null || sources.Count == 0)
        {
            return false;
        }

        for (DesignComponent? up = target; up is not null; up = up.Parent)
        {
            if (sources.Contains(up, ReferenceEqualityComparer.Instance))
            {
                return false;
            }
        }

        return sources.All(static s => CanReparent(s.Type))
            ? sources.All(s => CanMoveToParent(s, target))
            : ReferenceEquals(sources[0].Parent, target);
    }

    // canMoveToParent, with the two refusals above.
    private static bool CanMoveToParent(DesignComponent source, DesignComponent target) => source.Type switch
    {
        MenuType => target.Type is MenuType or ContextMenuType or FigureType,
        ContextMenuType => target.Type == FigureType,
        PushToolType or ToggleToolType => target.Type == ToolbarType,
        TabType => target.Type == TabGroupType,
        TreeNodeType => target.Type is TreeType or CheckBoxTreeType or TreeNodeType,
        SwitchType => target.Type is FigureType or "matlab.ui.container.Panel" or TabType or "matlab.ui.container.ButtonGroup",
        _ => false,
    };

    /// <summary>
    /// The rows of a target after a drop (<c>_calcPlacementIndex</c>): each source out and in again at the
    /// place, one after another - so that several dropped after a row stand in the reverse of their order.
    /// </summary>
    public static IReadOnlyList<DesignComponent> Moved(IReadOnlyList<DesignComponent> rows, IReadOnlyList<DesignComponent> sources, DesignBrowserPlace place, DesignComponent? at = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sources);
        var list = new List<DesignComponent>(rows);
        foreach (DesignComponent source in sources)
        {
            list.RemoveAll(c => ReferenceEquals(c, source));
            int index = place == DesignBrowserPlace.First ? 0
                : list.FindIndex(c => ReferenceEquals(c, at)) + (place == DesignBrowserPlace.After ? 1 : 0);
            list.Insert(index, source);
        }

        return list;
    }

    /// <summary>
    /// A container's children after <paramref name="sources"/>, among them, are dropped so that the rows
    /// under it read <paramref name="rowsAfter"/> (<c>TreeDragReorderCommand._getReorderData</c>, <c>ReorderCommand</c>):
    /// the sources go beside their neighbours in their own section of the rows, by the view's rule.
    /// </summary>
    public static IReadOnlyList<DesignComponent> DropOrder(DesignBrowserList view, IReadOnlyList<DesignComponent> children, IReadOnlyList<DesignComponent> rowsAfter, IReadOnlyList<DesignComponent> sources)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(rowsAfter);
        ArgumentNullException.ThrowIfNull(sources);
        var order = new List<DesignComponent>(children);
        if (sources.Count == 0)
        {
            return order;
        }

        // _getTreeSection: menus among menus, and so on; the rest among the rest.
        DesignComponent lead = sources[0];
        DesignComponent[] section = Adjacent(lead.Type)
            ? rowsAfter.Where(c => c.Type == lead.Type).ToArray()
            : rowsAfter.Where(static c => !Adjacent(c.Type)).ToArray();
        int? up = Neighbour(section, sources, children, up: true);
        int? down = Neighbour(section, sources, children, up: false);

        var moves = new List<(DesignComponent Item, int Old, int New)>();
        foreach (DesignComponent source in sources)
        {
            int now = IndexIn(children, source);
            int? to = view == DesignBrowserList.TabOrder
                ? Insertion(now, up, down)
                : Reversed(source.Type) ? Insertion(now, down, up) : Insertion(now, up, down);
            if (to is { } place && place > -1)
            {
                moves.Add((source, now, place));
            }
        }

        moves.Sort(static (a, b) => a.Old.CompareTo(b.Old));
        return DesignStructure.Apply(order, moves);
    }

    // getNeighborInformation: the first row past the sources from the first of them, its place among the children.
    private static int? Neighbour(DesignComponent[] section, IReadOnlyList<DesignComponent> sources, IReadOnlyList<DesignComponent> children, bool up)
    {
        DesignComponent? first = section.FirstOrDefault(c => sources.Contains(c, ReferenceEqualityComparer.Instance));
        if (first is null)
        {
            return null;
        }

        int at = Array.FindIndex(section, c => ReferenceEquals(c, first));
        while (at >= 0 && at < section.Length && sources.Contains(section[at], ReferenceEqualityComparer.Instance))
        {
            at += up ? 1 : -1;
        }

        return at >= 0 && at < section.Length ? IndexIn(children, section[at]) : null;
    }

    // getPeerNodeInsertionInsertionIndex: after the next one, else at the other.
    private static int? Insertion(int now, int? first, int? second)
    {
        if (second is { } s)
        {
            return s < now ? s + 1 : s;
        }

        return first is { } f ? (f < now ? f : f - 1) : null;
    }

    private static int IndexIn(IReadOnlyList<DesignComponent> list, DesignComponent component)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], component))
            {
                return i;
            }
        }

        return -1;
    }

    // --- selection ---------------------------------------------------------------------------------

    /// <summary>
    /// What rows picked in the tree select (<c>_setSelection</c>), from what is selected:
    /// <paramref name="asked"/> holds the rows' components in the order the tree holds them, null for the
    /// app. One row, unless it is selected already: that row. Several: added one by one while each has
    /// the first selected one's parent and, where its class is selected only with its class, its class;
    /// on the first that does not, the last asked alone, or the first asked if the last is what was
    /// selected. A figure, a tab or a check box tree's node among several, or the figure selected, gives
    /// the figure, or the last asked if the figure was selected.
    /// </summary>
    /// <returns>The picks, or null when nothing changes; a pick for null, <paramref name="asked"/>'s app, is the app.</returns>
    public static IReadOnlyList<DesignComponent?>? SelectionAfter(IReadOnlyList<DesignComponent?> current, IReadOnlyList<DesignComponent?> asked, DesignComponent figure)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(figure);
        if (asked.Count == 0)
        {
            return [];
        }

        if (asked.Count == 1)
        {
            return current.Contains(asked[0], ReferenceEqualityComparer.Instance) ? null : [asked[0]];
        }

        DesignComponent? last = asked[^1];
        bool figureSelected = current is [{ Parent: null } only] && only.Type == FigureType;
        if (asked.Any(p => p is null || IsSingleSelection(p) || figureSelected || p.Type == FigureType))
        {
            return current.Count > 0 && current[0] is { Parent: null } first && first.Type == FigureType ? [last] : [figure];
        }

        var selection = new List<DesignComponent?>(current);
        var adding = new List<DesignComponent?>();
        IReadOnlyList<DesignComponent?>? set = null;
        foreach (DesignComponent? p in asked)
        {
            if (selection.Count == 0)
            {
                selection.Add(p);
                set = [p];
            }

            if (CanAdd(selection[0], p!))
            {
                adding.Add(p);
            }
            else
            {
                adding.Clear();
                set = ReferenceEquals(last, selection[0]) ? [asked[0]] : [last];
                break;
            }
        }

        if (adding.Count == 0)
        {
            return set;
        }

        foreach (DesignComponent? p in adding)
        {
            if (!selection.Contains(p, ReferenceEqualityComparer.Instance))
            {
                selection.Add(p);
            }
        }

        return selection;

        // _verifyCanAddToMultiSelect.
        static bool CanAdd(DesignComponent? first, DesignComponent p)
        {
            if (first is null || !ReferenceEquals(first.Parent, p.Parent))
            {
                return false;
            }

            return !(IsSameTypeSelection(first.Type) || IsSameTypeSelection(p.Type)) || first.Type == p.Type;
        }
    }

    /// <summary>
    /// The rows the tree asks to select for a click (<c>MultipleNodeSelection._handleSelection</c>,
    /// <c>SelectionAdaptor</c>): Ctrl adds or takes off the row, Shift the rows from the row last clicked,
    /// neither the row alone; Alt, and Ctrl with Shift, nothing.
    /// </summary>
    /// <param name="rows">The rows shown, top to bottom.</param>
    /// <param name="selected">The rows selected, in the order they were.</param>
    /// <param name="boundary">The row last clicked without Shift; updated.</param>
    /// <param name="clicked">The row clicked.</param>
    /// <param name="ctrl">Whether Ctrl is held.</param>
    /// <param name="shift">Whether Shift is held.</param>
    /// <param name="alt">Whether Alt is held.</param>
    /// <returns>The rows asked for; null when the click does nothing.</returns>
    public static IReadOnlyList<DesignBrowserRow>? Clicked(IReadOnlyList<DesignBrowserRow> rows, IReadOnlyList<DesignBrowserRow> selected, ref DesignBrowserRow? boundary, DesignBrowserRow clicked, bool ctrl, bool shift, bool alt = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(clicked);
        if (alt || (ctrl && shift))
        {
            return null;
        }

        if (shift)
        {
            // getContiguousSelectionRange: from the boundary's row, the first if it has none.
            int to = IndexOf(rows, clicked);
            int from = boundary is { } b && IndexOf(rows, b) is >= 0 and var k ? k : 0;
            return rows.Skip(System.Math.Min(from, to)).Take(System.Math.Abs(to - from) + 1).ToArray();
        }

        boundary = clicked;
        if (!ctrl)
        {
            return [clicked];
        }

        // _toggleSelection.
        int at = IndexOf(selected, clicked);
        var list = new List<DesignBrowserRow>(selected);
        if (at >= 0)
        {
            list.RemoveAt(at);
        }
        else
        {
            list.Add(clicked);
        }

        return list;

        static int IndexOf(IReadOnlyList<DesignBrowserRow> list, DesignBrowserRow row)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i].Component, row.Component))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    // --- the collapsed form and the height ---------------------------------------------------------

    /// <summary>What a crumb says (<c>Breadcrumbs._setMetaData</c>).</summary>
    public static string CrumbText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length >= 20 ? text[..20] + "..." : text;
    }

    /// <summary>
    /// The height the browser takes (<c>_layoutChangeHandler</c>) in a side <paramref name="height"/> high, asked for
    /// <paramref name="asked"/> by its tree: its toolbar's 33 more, no more than three quarters of the side, a hand's
    /// height when one was set; null when it stays.
    /// </summary>
    /// <param name="height">The side's height.</param>
    /// <param name="asked">The height asked for: <see cref="RowHeight"/> a row, or <see cref="BreadcrumbHeight"/>.</param>
    /// <param name="manual">Whether the splitter was dragged last.</param>
    /// <param name="manualTop">The height it was dragged to.</param>
    /// <param name="previous">The height set last; updated.</param>
    public static double? TopHeight(double height, double asked, bool manual, double manualTop, ref double previous)
    {
        if (height == 0 || asked == 0)
        {
            return null;
        }

        double wanted = asked + 33;
        double most = height * 0.75;
        double top = 58;
        if (wanted > top)
        {
            top = System.Math.Max(wanted, 65);
        }

        if (top - 33 < 65)
        {
            top = 98;
        }

        if (manual && top != 58)
        {
            top = manualTop;
        }

        top = System.Math.Min(most, top);
        if (top == previous && top >= most)
        {
            return null;
        }

        previous = top;
        return top;
    }
}
