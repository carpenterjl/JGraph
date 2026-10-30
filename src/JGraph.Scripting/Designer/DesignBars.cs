using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// App Designer's rules for a figure's menus, toolbars and context menus on the canvas (App Designer
/// window plan, stage W5d), apart from any app: where Add Menu and Add Tool put what they add
/// (<c>MenuAction</c>, <c>MenuBarAction</c>, <c>ToolAction</c>, <c>ToolbarAction</c>), what the arrow keys,
/// Home, End and Escape select (<c>MenuHierarchyNavigateReorderPlugin</c>, <c>ToolKeyboardPlugin</c>,
/// <c>ToolbarKeyboardPlugin</c>, <c>MenuBarInPlaceEditPlugin</c>) and where a drag drops a menu, a tool or
/// a toolbar (<c>MenuHierarchyDragModel</c>, <c>ToolDragModel</c>, <c>ToolbarDragModel</c>), transcribed
/// from R2025b's client and checked against it by the node oracle <c>ui-probes/w5/oracle/bars.js</c>.
/// </summary>
public static class DesignBars
{
    public const string MenuType = "matlab.ui.container.Menu";
    public const string ContextMenuType = "matlab.ui.container.ContextMenu";
    public const string ToolbarType = "matlab.ui.container.Toolbar";
    public const string PushToolType = "matlab.ui.container.toolbar.PushTool";
    public const string ToggleToolType = "matlab.ui.container.toolbar.ToggleTool";

    /// <summary>Whether <paramref name="component"/> is a push or toggle tool.</summary>
    public static bool IsTool(DesignComponent component) => component.Type is PushToolType or ToggleToolType;

    /// <summary>Whether <paramref name="component"/> is a menu at the top of its figure's menu bar (<c>isTopLevelMenu</c>).</summary>
    public static bool IsTopLevelMenu(DesignComponent component) => component.Type == MenuType && component.Parent is { Parent: null };

    /// <summary>The context menu <paramref name="component"/> is, or is an item of; null for any other (<c>getContextMenuFor</c>).</summary>
    public static DesignComponent? ContextMenuFor(DesignComponent component)
    {
        for (DesignComponent? up = component; up is { Type: MenuType or ContextMenuType }; up = up.Parent)
        {
            if (up.Type == ContextMenuType)
            {
                return up;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="component"/> is an item of a context menu (<c>isContextMenuItem</c>).</summary>
    public static bool IsContextMenuItem(DesignComponent component) => component.Type == MenuType && ContextMenuFor(component) is not null;

    /// <summary>The menu bar's menus (<c>getTopLevelMenus</c>).</summary>
    public static IReadOnlyList<DesignComponent> TopLevelMenus(DesignComponent figure) => figure.Children.Where(static c => c.Type == MenuType).ToArray();

    /// <summary>The figure's toolbars, top to bottom (<c>getToolbars</c>).</summary>
    public static IReadOnlyList<DesignComponent> Toolbars(DesignComponent figure) => figure.Children.Where(static c => c.Type == ToolbarType).ToArray();

    /// <summary>The figure's context menus (<c>getContextMenus</c>).</summary>
    public static IReadOnlyList<DesignComponent> ContextMenus(DesignComponent figure) => figure.Children.Where(static c => c.Type == ContextMenuType).ToArray();

    // --- adding ------------------------------------------------------------------------------------

    /// <summary>Which of Add Menu to Right, Add Menu Below and adding at the end apply (<c>MenuAction._updateAction</c>).</summary>
    public static (bool Right, bool Below, bool AtEnd) MenuAbility(DesignComponent figure, IReadOnlyList<DesignComponent> selection)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(selection);
        bool hasMenus = TopLevelMenus(figure).Count > 0;
        bool right = false, below = false, atEnd = false;
        if (selection is [var one])
        {
            if (one.Parent is null)
            {
                right = hasMenus;
            }
            else if (one.Type == ContextMenuType)
            {
                atEnd = true;
                below = true;
            }
            else if (one.Type == MenuType)
            {
                right = true;
                below = true;
            }
        }

        return (right, below, atEnd || hasMenus || ContextMenus(figure).Count > 0);
    }

    /// <summary>
    /// Where Add Menu to Right (<paramref name="right"/>) or Add Menu Below puts a menu with
    /// <paramref name="selected"/> selected (<c>_performAddMenu</c>): a top-level menu's on its right, or
    /// first under it; any other's first under it, or after it.
    /// </summary>
    public static (DesignComponent Parent, int Index) AddMenuPlace(DesignComponent selected, bool right)
    {
        ArgumentNullException.ThrowIfNull(selected);
        DesignComponent parent = selected.Parent!;
        if (parent.Parent is null)
        {
            return right ? (parent, TopLevelMenus(parent).ToList().IndexOf(selected) + 1) : (selected, 0);
        }

        return right ? (selected, 0) : (parent, Index(parent, selected) + 1);
    }

    /// <summary>Where a menu added at the end of <paramref name="target"/> goes (<c>_performAddMenuAtEnd</c>): the figure's after its last menu, any other's last.</summary>
    public static (DesignComponent Parent, int? Index) AddMenuAtEndPlace(DesignComponent target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Parent is not null)
        {
            return (target, null);
        }

        IReadOnlyList<DesignComponent> menus = TopLevelMenus(target);
        return (target, menus.Count == 0 ? 0 : Index(target, menus[^1]) + 1);
    }

    /// <summary>Where Add Push Tool or Add Toggle Tool to the right of what is selected puts it (<c>ToolAction._performAddTool</c>): first in a toolbar, after a tool.</summary>
    public static (DesignComponent Parent, int Index) AddToolPlace(DesignComponent selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return selected.Type == ToolbarType ? (selected, 0) : (selected.Parent!, Index(selected.Parent!, selected) + 1);
    }

    /// <summary>The toolbar a tool added at the end goes into (<c>ToolbarAction._performAddTool</c>): the one selected, or a selected tool's.</summary>
    public static DesignComponent AddToolAtEndPlace(DesignComponent selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return selected.Type == ToolbarType ? selected : selected.Parent!;
    }

    /// <summary>Where a toolbar dropped at <paramref name="y"/> goes among the figure's toolbars (<c>_getInsertionIndexForToolbar</c>): before the first whose middle is past it.</summary>
    public static int ToolbarDropIndex(IReadOnlyList<double> middles, double y)
    {
        ArgumentNullException.ThrowIfNull(middles);
        for (int k = 0; k < middles.Count; k++)
        {
            if (y < middles[k])
            {
                return k;
            }
        }

        return middles.Count;
    }

    // --- the keys ----------------------------------------------------------------------------------

    /// <summary>What Enter or Shift+Enter does with one menu or context menu selected (<c>_noCommandEnter</c>, <c>_shiftOnlyEnter</c>).</summary>
    public enum EnterAction
    {
        None,
        AddRight,
        AddBelow,
        AddAtEnd,
    }

    /// <summary>
    /// What Enter or Shift+Enter does with <paramref name="selected"/> alone selected: Enter adds below;
    /// Shift+Enter adds to the right, at the end of a menu with items under it, and nothing on a
    /// context menu, where Enter does nothing either (<c>ContextMenuNavigateReorderPlugin</c>).
    /// </summary>
    public static EnterAction Enter(DesignComponent selected, bool shift)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Type == ContextMenuType)
        {
            return EnterAction.None;
        }

        if (!shift)
        {
            return EnterAction.AddBelow;
        }

        return !IsTopLevelMenu(selected) && selected.Children.Count > 0 ? EnterAction.AddAtEnd : EnterAction.AddRight;
    }

    private static IReadOnlyList<DesignComponent> MenuSiblings(DesignComponent item) =>
        item.Type == ContextMenuType ? ContextMenus(item.Parent!)
        : IsTopLevelMenu(item) ? TopLevelMenus(item.Parent!)
        : item.Parent!.Children;

    // _isTopLevelItem: the menu bar's menus, or the context menus.
    private static bool IsTopItem(DesignComponent item) => item.Type == ContextMenuType || IsTopLevelMenu(item);

    // _isNonLeafItem: a menu with items under it that is no top-level menu; a context menu's are menus too.
    private static bool IsNonLeaf(DesignComponent item) => item.Type == MenuType && !IsTopLevelMenu(item) && item.Children.Count > 0;

    /// <summary>What an arrow, Home or End selects with <paramref name="selection"/> - menus or context menus - selected; null when it stays.</summary>
    public static DesignComponent? NavigateMenu(IReadOnlyList<DesignComponent> selection, DesignKey key)
    {
        ArgumentNullException.ThrowIfNull(selection);
        DesignComponent first = selection[0];
        IReadOnlyList<DesignComponent> siblings = MenuSiblings(first);
        switch (key)
        {
            case DesignKey.Home:
                return siblings[0];
            case DesignKey.End:
                return siblings[^1];
            case DesignKey.Left or DesignKey.Up:
                // The first of its siblings goes up to its parent.
                return selection.Count == 1 && !IsTopItem(first) && Index(first.Parent!, first) == 0 ? first.Parent : Previous(selection, siblings);
            case DesignKey.Right:
                return selection.Count == 1 && IsNonLeaf(first) ? first.Children[0] : Next(selection, siblings);
            case DesignKey.Down when IsTopItem(first):
                return selection.Count == 1 && first.Children.Count > 0 ? first.Children[0] : null;
            case DesignKey.Down:
                return Next(selection, siblings);
            default:
                return null;
        }
    }

    private static DesignComponent? Previous(IReadOnlyList<DesignComponent> selection, IReadOnlyList<DesignComponent> siblings)
    {
        if (siblings.Count <= 1)
        {
            return null;
        }

        int at = selection.Count > 1 ? selection.Min(c => IndexIn(siblings, c)) : IndexIn(siblings, selection[0]);
        return at < 0 ? null : siblings[at == 0 ? siblings.Count - 1 : at - 1];
    }

    private static DesignComponent? Next(IReadOnlyList<DesignComponent> selection, IReadOnlyList<DesignComponent> siblings)
    {
        if (siblings.Count <= 1)
        {
            return null;
        }

        int at = selection.Count > 1 ? selection.Max(c => IndexIn(siblings, c)) : IndexIn(siblings, selection[0]);
        return at < 0 ? null : siblings[at + 1 < siblings.Count ? at + 1 : 0];
    }

    /// <summary>What an arrow, Home or End selects with tools selected (<c>ToolKeyboardPlugin</c>); null when it stays.</summary>
    public static DesignComponent? NavigateTool(IReadOnlyList<DesignComponent> selection, DesignKey key)
    {
        ArgumentNullException.ThrowIfNull(selection);
        DesignComponent first = selection[0];
        DesignComponent toolbar = first.Parent!;
        IReadOnlyList<DesignComponent> tools = toolbar.Children;
        int at = IndexIn(tools, first);
        IReadOnlyList<DesignComponent> toolbars = Toolbars(toolbar.Parent!);
        int row = IndexIn(toolbars, toolbar);
        return key switch
        {
            DesignKey.Left => selection.Count == 1 && at == 0 ? tools[^1] : at - 1 >= 0 ? tools[at - 1] : null,
            DesignKey.Right => selection.Count == 1 && at == tools.Count - 1 ? tools[0] : at + 1 < tools.Count ? tools[at + 1] : null,
            DesignKey.Up => row > 0 ? toolbars[row - 1] : toolbars[0],
            DesignKey.Down => row < toolbars.Count - 1 ? toolbars[row + 1] : toolbar,
            DesignKey.Home => tools[0],
            DesignKey.End => tools[^1],
            _ => null,
        };
    }

    /// <summary>What an arrow, Home or End selects with toolbars selected (<c>ToolbarKeyboardPlugin</c>); null when it stays.</summary>
    public static DesignComponent? NavigateToolbar(IReadOnlyList<DesignComponent> selection, DesignKey key)
    {
        ArgumentNullException.ThrowIfNull(selection);
        DesignComponent first = selection[0];
        IReadOnlyList<DesignComponent> toolbars = Toolbars(first.Parent!);
        int at = IndexIn(toolbars, first);
        return key switch
        {
            DesignKey.Left => first.Children.Count > 0 ? first.Children[0] : null,
            DesignKey.Right => first.Children.Count > 0 ? first.Children[^1] : null,
            DesignKey.Up => selection.Count == 1 && at == 0 ? toolbars[^1] : at - 1 >= 0 ? toolbars[at - 1] : null,
            DesignKey.Down => selection.Count == 1 && at == toolbars.Count - 1 ? toolbars[0] : at + 1 < toolbars.Count ? toolbars[at + 1] : null,
            DesignKey.Home => toolbars[0],
            DesignKey.End => toolbars[^1],
            _ => null,
        };
    }

    /// <summary>
    /// What Down in a menu bar's menu's in-place editor does once saved (<c>MenuBarInPlaceEditPlugin._downArrowNavigation</c>):
    /// selects its first item or its next sibling, or adds below it or at the end of its parent.
    /// </summary>
    public static (DesignComponent? Select, EnterAction Add, DesignComponent? AddTo) EditorDown(DesignComponent menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (IsTopLevelMenu(menu))
        {
            return menu.Children.Count > 0 ? (menu.Children[0], EnterAction.None, null) : (null, EnterAction.AddBelow, null);
        }

        DesignComponent parent = menu.Parent!;
        int at = Index(parent, menu);
        return at < parent.Children.Count - 1 ? (parent.Children[at + 1], EnterAction.None, null) : (null, EnterAction.AddAtEnd, parent);
    }

    /// <summary>
    /// What Up in a menu bar's menu's in-place editor selects once saved (<c>_upArrowNavigation</c>):
    /// a top-level menu the figure's child before it, the first the last top-level menu; any other its sibling before it, the first its parent.
    /// </summary>
    public static DesignComponent EditorUp(DesignComponent menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        DesignComponent parent = menu.Parent!;
        int at = Index(parent, menu);
        if (IsTopLevelMenu(menu))
        {
            return parent.Children[at > 0 ? at - 1 : TopLevelMenus(parent).Count - 1];
        }

        return at > 0 ? parent.Children[at - 1] : parent;
    }

    // --- a drag ------------------------------------------------------------------------------------

    /// <summary>
    /// The sibling a drag at <paramref name="pointer"/> is over (<c>handleDoingMenuHierarchyDrag</c>): the
    /// first whose centre is past it, or the last with <c>AtEnd</c>.
    /// </summary>
    public static (int Index, bool AtEnd) Hovered(IReadOnlyList<double> centres, double pointer)
    {
        ArgumentNullException.ThrowIfNull(centres);
        for (int k = 0; k < centres.Count; k++)
        {
            if (pointer < centres[k])
            {
                return (k, false);
            }
        }

        return (centres.Count - 1, true);
    }

    /// <summary>
    /// A menu's drag dropped over the sibling at <paramref name="hovered"/> (<c>MenuHierarchyDragModel._handleCommitDragHoveredIndexChange</c>):
    /// the place it goes from and which way; null when it stays. <paramref name="start"/> is the place of the first selected.
    /// </summary>
    public static (int Drop, int Direction)? MenuDrop(int start, int hovered, bool atEnd)
    {
        int to = hovered;
        if (start < to && !atEnd)
        {
            to--;
        }

        return start == to ? null : (to, System.Math.Sign(to - start));
    }

    /// <summary>
    /// A toolbar's drag dropped over the toolbar at <paramref name="hovered"/> (<c>ToolbarDragModel</c>);
    /// null when it stays. <paramref name="start"/> is its place among the toolbars.
    /// </summary>
    public static (int Drop, int Direction)? ToolbarDrop(int start, int hovered, bool atEnd) => MenuDrop(start, hovered, atEnd);

    /// <summary>
    /// A tool's drag dropped at tool <paramref name="hoveredTool"/> of toolbar <paramref name="hoveredBar"/> (<c>ToolDragModel</c>):
    /// into another toolbar at a place (<c>To</c>), or along its own with a <c>Drop</c> and <c>Direction</c>;
    /// nothing when it stays. <paramref name="startBar"/> and <paramref name="startTool"/> are the place of the last selected, <paramref name="count"/> how many.
    /// </summary>
    public static (int? To, int? Drop, double? Direction) ToolDrop(int startBar, int startTool, int count, int hoveredBar, int hoveredTool, bool atToolEnd)
    {
        if (hoveredBar < 0)
        {
            return default;
        }

        int h = hoveredTool;
        if (startBar != hoveredBar)
        {
            return (atToolEnd ? h + 1 : h, null, null);
        }

        if (h <= startTool - count || h > startTool + 1 || atToolEnd)
        {
            if (startTool < h && !atToolEnd)
            {
                h--;
            }

            int offset = h - startTool;
            return (null, h, offset == 0 ? double.NaN : offset / (double)System.Math.Abs(offset));
        }

        return default;
    }

    private static int Index(DesignComponent parent, DesignComponent child) => IndexIn(parent.Children, child);

    private static int IndexIn(IReadOnlyList<DesignComponent> list, DesignComponent item)
    {
        for (int k = 0; k < list.Count; k++)
        {
            if (ReferenceEquals(list[k], item))
            {
                return k;
            }
        }

        return -1;
    }
}

/// <summary>What a place of the canvas's menu bar, toolbars or context menu tray is.</summary>
public enum DesignBarPlaceKind
{
    /// <summary>The menu bar away from its menus (<c>ad-menubar-overlay</c>).</summary>
    MenuBar,

    /// <summary>A menu, in the bar or in a list under one.</summary>
    Menu,

    /// <summary>A toolbar away from its tools (<c>ad-toolbar-overlay</c>).</summary>
    Toolbar,

    /// <summary>A push or toggle tool (<c>ad-tool-overlay</c>).</summary>
    Tool,

    /// <summary>The + under a list of menus, which adds one at its end (<c>ad-menu-add-child</c>).</summary>
    AddChild,

    /// <summary>The + beside a selected menu with no items, which adds one under it (<c>ad-menu-add-right</c>).</summary>
    AddRight,

    /// <summary>The + after the menu bar's last menu (<c>ad-menu-add-toplevel</c>).</summary>
    AddTopLevel,

    /// <summary>A toolbar's +, which adds a push tool at its end (<c>ad-toolbar-split-button</c>).</summary>
    ToolbarPlus,

    /// <summary>The arrow of a toolbar's +, which opens its menu.</summary>
    ToolbarArrow,

    /// <summary>A context menu's entry in the tray (<c>ad-contextmenu-inner-overlay</c>).</summary>
    ContextMenu,
}

/// <summary>A place on the canvas's bars or tray: what it is, the component it belongs to, and its box.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Component">The menu, tool, toolbar or context menu; the figure for the menu bar's own places.</param>
/// <param name="Box">Its box, on the surface (the bars stand above the figure, so at negative y) or in the tray.</param>
public sealed record DesignBarPlace(DesignBarPlaceKind Kind, DesignComponent Component, Rect2D Box)
{
    /// <summary>The text it shows: a menu's without its access key's ampersand.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>A menu's <c>Accelerator</c> as its row shows it (<c>CTRL + X</c>); empty for none.</summary>
    public string Shortcut { get; init; } = string.Empty;

    /// <summary>Whether it is shown faded: its <c>Visible</c> off (<c>gbtHideMenu</c>).</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether it is greyed: its <c>Enable</c> off.</summary>
    public bool Disabled { get; init; }

    /// <summary>A menu's <c>Checked</c>, or a toggle tool's <c>State</c>.</summary>
    public bool Checked { get; init; }

    /// <summary>Whether a line stands before it: a menu's or tool's <c>Separator</c>.</summary>
    public bool Separator { get; init; }

    /// <summary>Whether a menu has items under it, which its row shows by an arrow.</summary>
    public bool HasItems { get; init; }

    /// <summary>Whether it is in a list under a menu rather than in the bar.</summary>
    public bool InList { get; init; }

    /// <summary>Whether it is selected.</summary>
    public bool Selected { get; init; }

    /// <summary>Whether something under it is selected (<c>ad-child-menu-selected</c>).</summary>
    public bool Holds { get; init; }

    /// <summary>A tray entry's items, as far as its 96 pixels show them.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>How many components a tray entry's context menu is assigned to.</summary>
    public int Assignments { get; init; }
}

/// <summary>
/// Where the canvas lays out a figure's menu bar, toolbars and context menu lists (the sizes of R2025b's
/// style sheet: <c>gbtMenuBar</c> 22 high with its border, <c>gbtToolbar</c> 26, a tool 22 in a margin of
/// 2, a toolbar's + 38 by 22, <c>float-toolbar</c> 18 round). The bars stand above the figure's content,
/// which stays where the surface's origin is, as they stand above it in R2025b's <c>gbtFigureToolsContainer</c>.
/// </summary>
public sealed class DesignBarLayout
{
    /// <summary>The menu bar's height (<c>_HEIGHT_OF_MENU_BAR</c>).</summary>
    public const double MenuBarHeight = 22;

    /// <summary>A toolbar's height (<c>ad-toolbar-highlight</c>).</summary>
    public const double ToolbarHeight = 26;

    /// <summary>A tool's side.</summary>
    public const double ToolSize = 22;

    /// <summary>A tool's margin (<c>margin:2px</c>).</summary>
    public const double ToolMargin = 2;

    /// <summary>A + button's side (<c>float-toolbar</c>).</summary>
    public const double AddSize = 18;

    /// <summary>A toolbar's + and its arrow together (<c>ad-toolbar-split-button</c>).</summary>
    public const double ToolbarPlusWidth = 38;

    /// <summary>The gap between a toolbar's last tool and its + (<c>SPLIT_BUTTON_PIXEL_OFFSET</c>).</summary>
    public const double ToolbarPlusOffset = 4;

    /// <summary>The width of the + after the menu bar's last menu (<c>ad-menu-add-toplevel</c>).</summary>
    public const double AddTopLevelWidth = 24;

    /// <summary>A list's row's height: 5 pixels of padding each way round a line (<c>mwHideDescription</c>, and <c>_resizeOverlays</c>' 26).</summary>
    public const double RowHeight = 26;

    /// <summary>A row's padding across (<c>padding:5px 8px</c>), and the check mark's place in it.</summary>
    public const double RowPadding = 8;

    private const double RowCheck = 18;
    private const double RowArrow = 14;
    private const double TopPadding = 6;

    private readonly Func<string, double> _measure;
    private readonly IReadOnlyList<DesignComponent> _selection;
    private readonly Func<DesignComponent, string, string?> _text;
    private readonly Func<DesignComponent, string, bool?> _flag;

    /// <summary>A layout of what <paramref name="figure"/> holds.</summary>
    /// <param name="figure">The figure.</param>
    /// <param name="selection">What is selected.</param>
    /// <param name="measure">A text's width on the canvas.</param>
    /// <param name="text">A component's text property, as the design has it.</param>
    /// <param name="flag">A component's on/off property, as the design has it; null when it is unset.</param>
    public DesignBarLayout(DesignComponent figure, IReadOnlyList<DesignComponent> selection, Func<string, double> measure, Func<DesignComponent, string, string?> text, Func<DesignComponent, string, bool?> flag)
    {
        ArgumentNullException.ThrowIfNull(figure);
        _selection = selection;
        _measure = measure;
        _text = text;
        _flag = flag;
        IReadOnlyList<DesignComponent> menus = DesignBars.TopLevelMenus(figure);
        IReadOnlyList<DesignComponent> toolbars = DesignBars.Toolbars(figure);
        Height = (menus.Count > 0 ? MenuBarHeight : 0) + (toolbars.Count * ToolbarHeight);
        double y = -Height;
        var places = new List<DesignBarPlace>();
        var lists = new List<DesignBarPlace>();
        double width = figure.Get("Position") is NumericValue { Data: [_, _, var w, _] } ? w : 640;
        Width = width;
        if (menus.Count > 0)
        {
            places.Add(new DesignBarPlace(DesignBarPlaceKind.MenuBar, figure, new Rect2D(0, y, width, MenuBarHeight)));
            double x = 0;
            var tops = new List<(DesignComponent Menu, Rect2D Box)>();
            foreach (DesignComponent menu in menus)
            {
                double w2 = _measure(Label(menu)) + (2 * TopPadding);
                tops.Add((menu, new Rect2D(x, y, w2, MenuBarHeight)));
                places.Add(Row(menu, new Rect2D(x, y, w2, MenuBarHeight), inList: false));
                x += w2;
            }

            places.Add(new DesignBarPlace(DesignBarPlaceKind.AddTopLevel, figure, new Rect2D(x, y, AddTopLevelWidth, MenuBarHeight)));
            bool several = _selection.Count > 1 && DesignBars.IsTopLevelMenu(_selection[0]);
            foreach ((DesignComponent menu, Rect2D box) in tops)
            {
                // A list shows under a selected menu, or one with something selected under it; not under several at once.
                if (!several && ShowsList(menu))
                {
                    AddList(menu, box.X, box.Y + box.Height - 2, System.Math.Max(box.Width, 0), lists);
                }
            }

            y += MenuBarHeight;
        }

        foreach (DesignComponent toolbar in toolbars)
        {
            places.Add(new DesignBarPlace(DesignBarPlaceKind.Toolbar, toolbar, new Rect2D(0, y, width, ToolbarHeight))
            {
                Selected = Holds(toolbar),
                Hidden = _flag(toolbar, "Visible") == false,
            });
            double x = 0;
            foreach (DesignComponent tool in toolbar.Children)
            {
                bool separator = _flag(tool, "Separator") == true;
                if (separator)
                {
                    x += 5;
                }

                places.Add(new DesignBarPlace(DesignBarPlaceKind.Tool, tool, new Rect2D(x + ToolMargin, y + ToolMargin, ToolSize, ToolSize))
                {
                    Selected = Holds(tool),
                    Hidden = _flag(tool, "Visible") == false,
                    Disabled = _flag(tool, "Enable") == false,
                    Checked = _flag(tool, "State") == true,
                    Separator = separator,
                });
                x += ToolSize + (2 * ToolMargin);
            }

            double plus = x + ToolbarPlusOffset;
            places.Add(new DesignBarPlace(DesignBarPlaceKind.ToolbarPlus, toolbar, new Rect2D(plus, y + ToolMargin, ToolbarPlusWidth / 2, ToolSize)));
            places.Add(new DesignBarPlace(DesignBarPlaceKind.ToolbarArrow, toolbar, new Rect2D(plus + (ToolbarPlusWidth / 2), y + ToolMargin, ToolbarPlusWidth / 2, ToolSize)));
            y += ToolbarHeight;
        }

        places.AddRange(lists);
        Places = places;
    }

    /// <summary>The bars' height above the figure's content; 0 with neither a menu bar nor a toolbar.</summary>
    public double Height { get; }

    /// <summary>The bars' width, the figure's.</summary>
    public double Width { get; }

    /// <summary>The places, each over the ones before it.</summary>
    public IReadOnlyList<DesignBarPlace> Places { get; }

    /// <summary>The place at <paramref name="at"/>, the front one; null when none.</summary>
    public DesignBarPlace? PlaceAt(Point2D at)
    {
        for (int k = Places.Count - 1; k >= 0; k--)
        {
            // Half open: the figure's top edge, where the bars' lowest ends, is the figure's.
            Rect2D box = Places[k].Box;
            if (at.X >= box.X && at.X < box.X + box.Width && at.Y >= box.Y && at.Y < box.Y + box.Height)
            {
                return Places[k];
            }
        }

        return null;
    }

    /// <summary>The place of <paramref name="component"/> of the kinds it shows as; null when it does not show.</summary>
    public DesignBarPlace? PlaceOf(DesignComponent component) =>
        Places.FirstOrDefault(p => ReferenceEquals(p.Component, component) && p.Kind is DesignBarPlaceKind.Menu or DesignBarPlaceKind.Tool or DesignBarPlaceKind.Toolbar);

    /// <summary>
    /// A context menu's list laid out from (<paramref name="x"/>, <paramref name="y"/>), as it stands open
    /// in the tray: its items, the lists under what is selected, and the + under it.
    /// </summary>
    public IReadOnlyList<DesignBarPlace> ContextMenuPlaces(DesignComponent contextMenu, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        var list = new List<DesignBarPlace>();
        AddList(contextMenu, x, y, 0, list);
        return list;
    }

    /// <summary>A menu's or context menu's items in a column, with what shows beside them.</summary>
    private void AddList(DesignComponent owner, double x, double y, double atLeast, List<DesignBarPlace> list)
    {
        List<DesignComponent> items = [.. owner.Children.Where(static c => c.Type == DesignBars.MenuType)];
        double width = atLeast;
        foreach (DesignComponent item in items)
        {
            string shortcut = Accelerator(item);
            width = System.Math.Max(width, (2 * RowPadding) + RowCheck + _measure(Label(item)) + (shortcut.Length > 0 ? 16 + _measure(shortcut) : 0) + RowArrow);
        }

        double top = y;
        foreach (DesignComponent item in items)
        {
            list.Add(Row(item, new Rect2D(x, top, width, RowHeight), inList: true));
            top += RowHeight;
        }

        // The + under the list, unless something further under it is selected (ad-descendant-selected).
        bool further = items.Any(i => i.Children.Any(HoldsOrIs));
        if (!further)
        {
            double centred = x + ((System.Math.Max(width, AddSize) - AddSize) / 2);
            list.Add(new DesignBarPlace(DesignBarPlaceKind.AddChild, owner, new Rect2D(centred, top + 1, AddSize, AddSize)));
        }

        foreach ((DesignComponent item, int k) in items.Select(static (m, i) => (m, i)))
        {
            double rowTop = y + (k * RowHeight);
            if (item.Children.Count > 0 && ShowsList(item))
            {
                AddList(item, x + width, rowTop - 1, 0, list);
            }
            else if (item.Children.Count == 0 && _selection is [var only] && ReferenceEquals(only, item))
            {
                // The + beside a selected item with none under it (ad-menu-add-right: top:calc(50% - 10px)).
                list.Add(new DesignBarPlace(DesignBarPlaceKind.AddRight, item, new Rect2D(x + width, rowTop + (RowHeight / 2) - 10, AddSize, AddSize)));
            }
        }
    }

    private DesignBarPlace Row(DesignComponent menu, Rect2D box, bool inList) => new(DesignBarPlaceKind.Menu, menu, box)
    {
        Text = Label(menu),
        Shortcut = inList ? Accelerator(menu) : string.Empty,
        Selected = Holds(menu),
        Holds = !Holds(menu) && ShowsList(menu),
        Hidden = _flag(menu, "Visible") == false,
        Disabled = _flag(menu, "Enable") == false,
        Checked = _flag(menu, "Checked") == true,
        Separator = inList && _flag(menu, "Separator") == true,
        HasItems = inList && menu.Children.Count > 0,
        InList = inList,
    };

    private bool Holds(DesignComponent component) => _selection.Any(c => ReferenceEquals(c, component));

    private bool HoldsOrIs(DesignComponent component) => Holds(component) || component.Children.Any(HoldsOrIs);

    private bool ShowsList(DesignComponent menu) => HoldsOrIs(menu);

    private string Label(DesignComponent menu) => DisplayLabel(_text(menu, "Text") ?? string.Empty);

    private string Accelerator(DesignComponent menu) =>
        _text(menu, "Accelerator") is { Length: > 0 } key ? "CTRL + " + key.ToUpperInvariant() : string.Empty;

    /// <summary>A menu's text as its row shows it (<c>getDisplayLabel</c>): an access key's ampersand gone, a doubled one kept once.</summary>
    public static string DisplayLabel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&&", "\u0001", StringComparison.Ordinal).Replace("&", string.Empty, StringComparison.Ordinal).Replace('\u0001', '&');
    }
}
