using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// The canvas's gestures on a figure's menus, toolbars and context menus (App Designer window plan,
/// stage W5d): their places pressed (<c>MenuHierarchySelectPlugin</c>, <c>ToolbarHierarchySelectPlugin</c>,
/// <c>ContextMenuSelectPlugin</c>), their +s (<c>MenuHierarchyPlusBtnPlugin</c>, <c>ToolbarController</c>),
/// their keys and drags, their menus (<c>MenuMenuItemProvider</c>, <c>MenuBarMenuItemProvider</c>,
/// <c>ToolMenuItemProvider</c>, <c>ToolbarMenuItemProvider</c>, <c>ContextMenuMenuItemProvider</c>), and the
/// Context Menus tray: its entries, a context menu opened to be edited, and one dragged to a component
/// (<c>CanvasTrayView</c>, <c>ContextMenuRichEditPlugin</c>, <c>DragAssignContextMenuService</c>).
/// </content>
public sealed partial class DesignGestures
{
    /// <summary>The tray's width (<c>_setWidth(600)</c>).</summary>
    public const double TrayWidth = 600;

    /// <summary>The tray's height when open (<c>_userDefinedHeight</c>).</summary>
    public const double TrayHeight = 200;

    /// <summary>The height of the tray's title (<c>ad-canvas-tray-type-title</c>).</summary>
    public const double TrayTitle = 24;

    /// <summary>The padding round the tray's entries (<c>ad-canvas-tray-content-panel</c>).</summary>
    public const double TrayPadding = 15;

    /// <summary>An entry's width (<c>ad-contextmenu-entry</c>).</summary>
    public const double TrayEntryWidth = 118;

    /// <summary>An entry's margin across (<c>margin:0 10px</c>).</summary>
    public const double TrayEntryMargin = 10;

    /// <summary>An entry's heading: its name and how many it is assigned to (<c>ad-contextmenu-entry-header</c>).</summary>
    public const double TrayEntryHeader = 19;

    /// <summary>The height of an entry's items, the rest cut off (<c>_MAX_HEIGHT</c>).</summary>
    public const double TrayEntryBody = 96;

    /// <summary>How far a context menu's press must travel to be a drag to a component (<c>ContextMenuDragAssignPlugin</c>).</summary>
    private const double AssignThreshold = 4;

    private enum BarMenu
    {
        None,
        MenuBar,
        Menu,
        Toolbar,
        Tool,
        ContextMenu,
        TrayPanel,
    }

    /// <summary>What is being dragged among the bars: its kind, and what it needs at the drop.</summary>
    private sealed record BarDrag(DesignBarPlaceKind Kind, IReadOnlyList<DesignComponent> Siblings, IReadOnlyList<Rect2D> Boxes, Rect2D Target, bool Across, Rect2D Start);

    private BarMenu _barMenu;
    private BarDrag? _barDrag;
    private int? _barHovered;
    private bool _barAtEnd;
    private (int Bar, int Tool, bool AtEnd)? _toolHover;
    private bool _trayStarted;
    private int _trayCount;
    private IReadOnlyList<DesignComponent> _traySelection = [];
    private Point2D _assignAt;
    private bool _assignOverTray;

    /// <summary>The tray's width as the view leaves it room: <see cref="TrayWidth"/>, or the view's where it is narrower.</summary>
    public double TrayAreaWidth { get; set; } = TrayWidth;

    /// <summary>The width of a text on the canvas, which lays the bars out; an estimate where nothing says.</summary>
    public Func<string, double>? TextWidth { get; set; }

    /// <summary>Raised with a menu to open where a toolbar's + arrow is: its items, and the point on the surface (<c>_setupSplitButtonDropDown</c>).</summary>
    public event EventHandler<(IReadOnlyList<DesignMenuItem> Items, Point2D At)>? MenuOpened;

    /// <summary>The bars as they stand, above the figure's content.</summary>
    public DesignBarLayout Bars => new(Document.Design.Figure, Document.SelectionIsEmpty ? [] : Document.Selection, TextWidth ?? (static t => t.Length * 6.0), BarText, BarFlag);

    // What the design holds, or the class's default: the bars are laid out at every draw, so not read from the runtime.
    private string? BarText(DesignComponent component, string name) =>
        Document.HeldOrDefault(component, name) switch
        {
            CharValue text => text.Text,
            _ => null,
        };

    private bool? BarFlag(DesignComponent component, string name) =>
        Document.HeldOrDefault(component, name) switch
        {
            OnOffValue flag => flag.IsOn,
            NumericValue { Data: [var v] } => v != 0,
            _ => null,
        };

    /// <summary>The context menu open to be edited in the tray (<c>appRichEditingStateChanged</c>); null when none.</summary>
    public DesignComponent? TrayEditing { get; private set; }

    /// <summary>Whether the tray stands open rather than down to its tab (<c>ad-canvastray-hidden</c>).</summary>
    public bool TrayOpen { get; set; }

    /// <summary>Whether the tray shows at all: while the figure has a context menu (<c>isEmpty</c>).</summary>
    public bool TrayShown => Document.ContextMenus.Count > 0;

    /// <summary>A context menu being dragged over the canvas, or the library's: where, what it says and how; null when none.</summary>
    public DesignAssignDrag? AssignDrag { get; private set; }

    /// <summary>The first box a dragged menu, tool or toolbar is drawn at (<c>ad-menu-avatar</c>, <c>ad-tool-avatar</c>); null when none is dragged.</summary>
    public Rect2D? BarDragged { get; private set; }

    /// <summary>Where a dragged menu, tool or toolbar would go, which the canvas marks: the box, whether on its far side, whether the row runs across; null when nothing is dragged or it would not drop.</summary>
    public (Rect2D Box, bool After, bool Across)? BarDrop { get; private set; }

    /// <summary>Whether what is being dragged among the bars is in the tray, not above the figure.</summary>
    public bool BarDragInTray { get; private set; }

    // Called when the selection or the design changed: what the tray shows follows it.
    private void SyncTray()
    {
        DesignDocument document = Document;
        int count = document.ContextMenus.Count;
        if (!_trayStarted)
        {
            // Opened with the app, the tray stays down (createEntryWidget's isLoading).
            _trayStarted = true;
        }
        else if (count > _trayCount)
        {
            TrayOpen = true;
        }

        _trayCount = count;
        if (TrayEditing is { } editing && !document.ContextMenus.Contains(editing))
        {
            TrayEditing = null;
        }

        IReadOnlyList<DesignComponent> now = document.SelectionIsEmpty ? [] : document.Selection;
        if (!now.SequenceEqual(_traySelection, ReferenceEqualityComparer.Instance))
        {
            IReadOnlyList<DesignComponent> before = _traySelection;
            _traySelection = now;

            // ContextMenuRichEditPlugin.handleComponentSelectionChanged, _onCanvasSelectionChanged.
            if (now.Any(static c => c.Type == DesignBars.ContextMenuType))
            {
                TrayEditing = null;
                TrayOpen = true;
            }
            else if (now.Any(DesignBars.IsContextMenuItem))
            {
                TrayEditing = DesignBars.ContextMenuFor(now[0]);
                TrayOpen = true;
            }
            else
            {
                TrayEditing = null;
                if (now.Count == 0 && before.Any(static c => c.Type == DesignBars.ContextMenuType || DesignBars.IsContextMenuItem(c)))
                {
                    TrayOpen = true;
                }
            }
        }
    }

    // --- the bars' places --------------------------------------------------------------------------

    /// <summary>A place of the bars pressed.</summary>
    /// <returns>Whether the canvas should hold the mouse.</returns>
    private bool PressBar(DesignBarPlace place, Point2D at, DesignButton button, DesignModifiers modifiers)
    {
        DesignDocument document = Document;
        bool left = button == DesignButton.Left;
        switch (place.Kind)
        {
            case DesignBarPlaceKind.AddChild or DesignBarPlaceKind.AddRight:
                if (left)
                {
                    document.AddMenuAtEnd(place.Component);
                }

                return false;
            case DesignBarPlaceKind.AddTopLevel:
                if (left)
                {
                    document.AddMenuAtEnd(document.Design.Figure);
                }

                return false;
            case DesignBarPlaceKind.ToolbarPlus:
                if (left)
                {
                    document.AddTool(toggle: false, toolbar: place.Component);
                }

                return false;
            case DesignBarPlaceKind.ToolbarArrow:
                if (left)
                {
                    DesignComponent toolbar = place.Component;
                    MenuOpened?.Invoke(this, (
                        [
                            new DesignMenuItem("Add Push Tool", true, () => document.AddTool(toggle: false, toolbar: toolbar)) { Shortcut = "Shift+P" },
                            new DesignMenuItem("Add Toggle Tool", true, () => document.AddTool(toggle: true, toolbar: toolbar)) { Shortcut = "Shift+T" },
                        ],
                        new Point2D(place.Box.X, place.Box.Y + place.Box.Height)));
                }

                return false;
            case DesignBarPlaceKind.MenuBar:
                if (!left)
                {
                    _barMenu = BarMenu.MenuBar;
                    return false;
                }

                document.Select([]);
                return false;
        }

        DesignComponent component = place.Component;
        bool held = document.Selection.Contains(component) && !document.SelectionIsEmpty;
        if (!left)
        {
            // A right press selects what is not selected; one of several selected becomes the anchor.
            if (!held)
            {
                document.Select([component]);
            }
            else if (document.Selection.Count > 1)
            {
                document.SetAnchor(DesignPick.Of(component));
            }

            _barMenu = place.Kind switch
            {
                DesignBarPlaceKind.Tool => BarMenu.Tool,
                DesignBarPlaceKind.Toolbar => BarMenu.Toolbar,
                _ => BarMenu.Menu,
            };
            return false;
        }

        if (modifiers == DesignModifiers.None)
        {
            if (!held)
            {
                document.Select([component]);
            }

            _barDrag = BarDragOf(component);
            return _barDrag is not null && Begin(Intent.BarDrag, component, ResizeEdges.None);
        }

        AddToBarSelection(component, held);
        return false;
    }

    /// <summary>Ctrl or Shift on a menu, tool or toolbar (<c>_addToSelection</c>): out of the selection, or in with its like among its siblings, else alone.</summary>
    private void AddToBarSelection(DesignComponent component, bool held)
    {
        DesignDocument document = Document;
        IReadOnlyList<DesignComponent> selection = document.SelectionIsEmpty ? [] : document.Selection;
        if (held)
        {
            document.Select(selection.Where(c => !ReferenceEquals(c, component)).ToArray());
        }
        else if (selection.Count > 0 && ReferenceEquals(selection[0].Parent, component.Parent) && (DesignBars.IsTool(component) || selection[0].Type == component.Type))
        {
            document.Select([.. selection, component]);
        }
        else
        {
            document.Select([component]);
        }
    }

    // --- a drag among the bars ---------------------------------------------------------------------

    private BarDrag? BarDragOf(DesignComponent component)
    {
        BarDragInTray = TrayEditing is not null && DesignBars.IsContextMenuItem(component);
        if (component.Type == DesignBars.MenuType)
        {
            IReadOnlyList<DesignComponent> siblings = DesignBars.IsTopLevelMenu(component) ? Document.TopLevelMenus : component.Parent!.Children;
            List<Rect2D> boxes = [];
            foreach (DesignComponent sibling in siblings)
            {
                if (PlaceOf(sibling) is not { } place)
                {
                    return null;
                }

                boxes.Add(place.Box);
            }

            bool top = DesignBars.IsTopLevelMenu(component);
            Rect2D target = top
                ? new Rect2D(0, boxes[0].Y, Bars.Width, DesignBarLayout.MenuBarHeight)
                : new Rect2D(boxes[0].X, boxes[0].Y, boxes[0].Width, boxes[^1].Y + boxes[^1].Height - boxes[0].Y);
            return new BarDrag(DesignBarPlaceKind.Menu, siblings, boxes, target, top, PlaceOf(component)!.Box);
        }

        if (Bars.PlaceOf(component) is not { } own)
        {
            return null;
        }

        return component.Type == DesignBars.ToolbarType
            ? new BarDrag(DesignBarPlaceKind.Toolbar, Document.Toolbars, Document.Toolbars.Select(t => Bars.PlaceOf(t)!.Box).ToArray(), own.Box, false, own.Box)
            : new BarDrag(DesignBarPlaceKind.Tool, Document.Toolbars, Document.Toolbars.Select(t => Bars.PlaceOf(t)!.Box).ToArray(), own.Box, true, own.Box);
    }

    private void DragBar(BarDrag drag, Point2D at, double dx, double dy)
    {
        BarDragged = new Rect2D(drag.Start.X + System.Math.Round(dx, MidpointRounding.AwayFromZero), drag.Start.Y + System.Math.Round(dy, MidpointRounding.AwayFromZero), drag.Start.Width, drag.Start.Height);
        switch (drag.Kind)
        {
            case DesignBarPlaceKind.Menu:
                if (!BarDragged.Value.IntersectsWith(drag.Target))
                {
                    _barHovered = null;
                    BarDrop = null;
                    return;
                }

                double[] centres = drag.Boxes.Select(b => drag.Across ? b.X + (b.Width / 2) : b.Y + (b.Height / 2)).ToArray();
                (int hovered, bool atEnd) = DesignBars.Hovered(centres, drag.Across ? at.X : at.Y);
                _barHovered = hovered;
                _barAtEnd = atEnd;
                BarDrop = (drag.Boxes[hovered], atEnd, drag.Across);
                return;
            case DesignBarPlaceKind.Toolbar:
                double[] middles = drag.Boxes.Select(static b => b.Y + (b.Height / 2)).ToArray();
                (int row, bool rowEnd) = DesignBars.Hovered(middles, at.Y);
                _barHovered = row;
                _barAtEnd = rowEnd;
                BarDrop = (drag.Boxes[row], rowEnd, false);
                return;
            default:
                double[] bottoms = drag.Boxes.Select(static b => b.Y + b.Height).ToArray();
                (int bar, _) = DesignBars.Hovered(bottoms, at.Y);
                DesignComponent toolbar = drag.Siblings[bar];
                double[] toolCentres = toolbar.Children.Select(t => Bars.PlaceOf(t)!.Box).Select(static b => b.X + (b.Width / 2)).ToArray();
                (int tool, bool toolEnd) = DesignBars.Hovered(toolCentres, at.X);
                _toolHover = (bar, tool, toolEnd);
                BarDrop = tool >= 0 ? (Bars.PlaceOf(toolbar.Children[tool])!.Box, toolEnd, true) : (drag.Boxes[bar], false, true);
                return;
        }
    }

    private void DropBar(BarDrag drag)
    {
        DesignDocument document = Document;
        switch (drag.Kind)
        {
            case DesignBarPlaceKind.Menu when _barHovered is { } hovered:
                DesignComponent menu = document.Selection[0];
                if (DesignBars.MenuDrop(drag.Siblings.ToList().IndexOf(menu), hovered, _barAtEnd) is { } drop)
                {
                    document.MoveTo(drop.Drop, drop.Direction);
                }

                break;
            case DesignBarPlaceKind.Toolbar when _barHovered is { } row:
                if (DesignBars.ToolbarDrop(drag.Siblings.ToList().IndexOf(document.Selection[0]), row, _barAtEnd) is { } moved)
                {
                    document.MoveTo(moved.Drop, moved.Direction);
                }

                break;
            case DesignBarPlaceKind.Tool when _toolHover is { } hover:
                DesignComponent[] tools = document.Selection.Where(DesignBars.IsTool).ToArray();
                DesignComponent from = tools[0].Parent!;
                DesignComponent last = tools.OrderByDescending(t => from.Children.ToList().IndexOf(t)).First();
                var place = DesignBars.ToolDrop(document.Toolbars.ToList().IndexOf(from), from.Children.ToList().IndexOf(last), tools.Length, hover.Bar, hover.Tool, hover.AtEnd);
                if (place.To is { } to)
                {
                    document.MoveToolsTo(drag.Siblings[hover.Bar], to);
                }
                else if (place.Drop is { } along)
                {
                    document.MoveTo(along, place.Direction ?? 0);
                }

                break;
        }
    }

    private void ClearBarDrag()
    {
        _barDrag = null;
        _barHovered = null;
        _toolHover = null;
        BarDragged = null;
        BarDrop = null;
        BarDragInTray = false;
        AssignDrag = null;
    }

    // --- the keys ----------------------------------------------------------------------------------

    /// <summary>A key with a menu, context menu, tool or toolbar selected; null when it is not theirs.</summary>
    private bool? BarKey(DesignKey key, DesignModifiers modifiers)
    {
        DesignDocument document = Document;
        if (document.SelectionIsEmpty || document.Selection is not [var first, ..])
        {
            return null;
        }

        bool menu = first.Type is DesignBars.MenuType or DesignBars.ContextMenuType;
        bool tools = first.Type == DesignBars.ToolbarType || DesignBars.IsTool(first);
        if (!menu && !tools)
        {
            return null;
        }

        switch (key)
        {
            case DesignKey.Left or DesignKey.Right or DesignKey.Up or DesignKey.Down or DesignKey.Home or DesignKey.End:
                if (modifiers == DesignModifiers.None)
                {
                    DesignComponent? next = menu ? DesignBars.NavigateMenu(document.Selection, key)
                        : DesignBars.IsTool(first) ? DesignBars.NavigateTool(document.Selection, key)
                        : DesignBars.NavigateToolbar(document.Selection, key);
                    if (next is not null)
                    {
                        document.Select([next]);
                    }
                }

                return true; // the keyboard plugins stop every one of them
            case DesignKey.Escape:
                if (first.Type == DesignBars.ContextMenuType && TrayEditing is not null)
                {
                    EndTrayEdit();
                }
                else if (first.Parent is { } parent)
                {
                    document.Select([parent]);
                }

                return true;
            case DesignKey.Enter when menu:
                if (document.Selection.Count == 1 && modifiers is DesignModifiers.None or DesignModifiers.Shift)
                {
                    switch (DesignBars.Enter(first, modifiers == DesignModifiers.Shift))
                    {
                        case DesignBars.EnterAction.AddBelow:
                            document.AddMenu(right: false);
                            break;
                        case DesignBars.EnterAction.AddRight:
                            document.AddMenu(right: true);
                            break;
                        case DesignBars.EnterAction.AddAtEnd:
                            document.AddMenuAtEnd(first);
                            break;
                    }
                }

                return true;
            case DesignKey.AddPushTool when tools:
                document.AddTool(toggle: false, atEnd: first.Type == DesignBars.ToolbarType);
                return true;
            case DesignKey.AddToggleTool when tools:
                document.AddTool(toggle: true, atEnd: first.Type == DesignBars.ToolbarType);
                return true;
            default:
                return null;
        }
    }

    /// <summary>
    /// Up or Down in a menu's in-place editor, once it is saved (<c>ArrowKeyExit</c>,
    /// <c>MenuBarInPlaceEditPlugin</c>): a menu bar's menu goes on to the next or the last, or adds one;
    /// a context menu's items stay (<c>ContextMenuInPlaceEditPlugin</c>).
    /// </summary>
    public void EditorArrow(DesignComponent menu, bool down)
    {
        ArgumentNullException.ThrowIfNull(menu);
        DesignDocument document = Document;
        if (DesignBars.IsContextMenuItem(menu))
        {
            return;
        }

        if (!down)
        {
            document.Select([DesignBars.EditorUp(menu)]);
            return;
        }

        (DesignComponent? select, DesignBars.EnterAction add, DesignComponent? to) = DesignBars.EditorDown(menu);
        if (select is not null)
        {
            document.Select([select]);
        }
        else if (add == DesignBars.EnterAction.AddBelow)
        {
            document.AddMenu(right: false);
        }
        else if (to is not null)
        {
            document.AddMenuAtEnd(to);
        }
    }

    // --- where they stand --------------------------------------------------------------------------

    /// <summary>Where <paramref name="component"/> shows in the bars, or in the tray when it is a context menu's item there.</summary>
    private DesignBarPlace? PlaceOf(DesignComponent component) =>
        DesignBars.IsContextMenuItem(component) ? TrayPlaces().FirstOrDefault(p => ReferenceEquals(p.Component, component) && p.Kind == DesignBarPlaceKind.Menu) : Bars.PlaceOf(component);

    // A menu's, tool's place to edit, on the surface; what the canvas says for any other (W5b).
    private IReadOnlyList<Rect2D>? InPlaceSpots(DesignComponent component)
    {
        if (component.Type == DesignBars.MenuType || DesignBars.IsTool(component))
        {
            if (PlaceOf(component) is not { } place)
            {
                return [new Rect2D(0, 0, 0, 0)];
            }

            Rect2D box = place.Box;
            if (place.InList)
            {
                // A list's row edits its text, beside the check mark's place (textAndDescriptionContainerNode).
                box = new Rect2D(box.X + DesignBarLayout.RowPadding + 18, box.Y, System.Math.Max(0, box.Width - DesignBarLayout.RowPadding - 18), box.Height);
            }

            return [box]; // in the tray's coordinates for a context menu's item, where the canvas stands its editor
        }

        return HotspotsOf?.Invoke(component);
    }

    // --- the tray ----------------------------------------------------------------------------------

    /// <summary>
    /// The tray's places, in its own coordinates, its title's top at 0 (<c>ContextMenuTrayWidget</c>,
    /// <c>ContextMenuTrayWidgetContainer</c>): an entry for each context menu, or the one being edited
    /// laid out open under the go-back line.
    /// </summary>
    public IReadOnlyList<DesignBarPlace> TrayPlaces()
    {
        DesignDocument document = Document;
        if (TrayEditing is { } editing)
        {
            DesignBarLayout bars = Bars;
            IReadOnlyList<DesignBarPlace> list = bars.ContextMenuPlaces(editing, 0, 0);
            double width = list.Where(static p => p.Kind == DesignBarPlaceKind.Menu).Select(static p => p.Box.X + p.Box.Width).DefaultIfEmpty(DesignBarLayout.AddSize).Max();
            return bars.ContextMenuPlaces(editing, System.Math.Max(TrayPadding, (TrayAreaWidth - width) / 2), TrayTitle + 30);
        }

        var places = new List<DesignBarPlace>();
        double x = TrayPadding + TrayEntryMargin;
        double y = TrayTitle + TrayPadding;
        foreach (DesignComponent menu in document.ContextMenus)
        {
            if (x + TrayEntryWidth > TrayAreaWidth - TrayPadding && x > TrayPadding + TrayEntryMargin)
            {
                x = TrayPadding + TrayEntryMargin;
                y += TrayEntryHeader + TrayEntryBody + TrayPadding;
            }

            places.Add(new DesignBarPlace(DesignBarPlaceKind.ContextMenu, menu, new Rect2D(x, y, TrayEntryWidth, TrayEntryHeader + TrayEntryBody))
            {
                Text = "app." + menu.CodeName,
                Items = menu.Children.Select(c => DesignBarLayout.DisplayLabel(BarText(c, "Text") ?? string.Empty)).ToArray(),
                Assignments = document.AssignmentsOf(menu).Count,
                Selected = !document.SelectionIsEmpty && document.Selection.Contains(menu),
            });
            x += TrayEntryWidth + (2 * TrayEntryMargin);
        }

        return places;
    }

    /// <summary>Where the go-back line of a context menu being edited stands (<c>ad-tray-container-go-back</c>), and what it says; null when none is.</summary>
    public (Rect2D Box, string Text)? TrayGoBack =>
        TrayEditing is { } editing ? (new Rect2D(10, TrayTitle + 8, 300, 16), "Editing app." + editing.CodeName) : null;

    private DesignBarPlace? TrayPlaceAt(Point2D at)
    {
        IReadOnlyList<DesignBarPlace> places = TrayPlaces();
        for (int k = places.Count - 1; k >= 0; k--)
        {
            if (places[k].Box.Contains(at))
            {
                return places[k];
            }
        }

        return null;
    }

    /// <summary>A press at <paramref name="at"/> in the tray, in its own coordinates.</summary>
    /// <returns>Whether the tray should hold the mouse.</returns>
    public bool PressTray(Point2D at, DesignButton button, DesignModifiers modifiers)
    {
        if (_press != PressState.None)
        {
            return true;
        }

        EndKeyMove();
        Refusal = null;
        _menuNode = null;
        _barMenu = BarMenu.None;
        _down = at;
        _lastDrag = at;
        _pressAt = at;
        _clickSelects = null;
        DesignDocument document = Document;
        if (button == DesignButton.Left && TrayGoBack is { } back && back.Box.Contains(at))
        {
            EndTrayEdit();
            return false;
        }

        DesignBarPlace? place = TrayPlaceAt(at);
        if (place is null)
        {
            _barMenu = button == DesignButton.Left ? BarMenu.None : BarMenu.TrayPanel;
            return false;
        }

        if (place.Kind != DesignBarPlaceKind.ContextMenu)
        {
            return PressBar(place, at, button, modifiers);
        }

        // A context menu is selected as a widget is (WidgetSelectionRules), and a drag assigns it (CheckConnect).
        DesignComponent menu = place.Component;
        bool held = !document.SelectionIsEmpty && document.Selection.Contains(menu);
        if (button != DesignButton.Left)
        {
            if (!held)
            {
                document.Select([menu]);
            }

            _barMenu = BarMenu.ContextMenu;
            return false;
        }

        if (modifiers == DesignModifiers.None)
        {
            if (!held)
            {
                document.Select([menu]);
            }

            return Begin(Intent.Assign, menu, ResizeEdges.None);
        }

        AddToBarSelection(menu, held);
        return false;
    }

    /// <summary>The mouse at <paramref name="at"/> in the tray's coordinates, with a press from the tray held: <paramref name="onSurface"/> is the same point on the surface, <paramref name="overTray"/> whether it is over the tray still.</summary>
    public void DragTray(Point2D at, Point2D onSurface, bool overTray, DesignModifiers modifiers)
    {
        _assignAt = onSurface;
        _assignOverTray = overTray;
        Drag(at, modifiers);
    }

    private void DragAssign(DesignComponent contextMenu)
    {
        DesignComponent? target = _assignOverTray ? null : Document.AssignTargetAt(_assignAt);
        (string text, DesignAssignHint hint) = Document.AssignMessage(contextMenu.CodeName, target, fromLibrary: false);
        AssignDrag = new DesignAssignDrag(contextMenu, target, text, hint, _assignAt, Document.AssignmentsOf(contextMenu));
    }

    /// <summary>A double click in the tray: a context menu's entry opens it to be edited (<c>ContextMenuRichEditPlugin.handleDoubleClick</c>); a menu's row edits its text.</summary>
    /// <returns>Whether it did.</returns>
    public bool DoubleClickTray(Point2D at)
    {
        if (TrayPlaceAt(at) is not { } place)
        {
            return false;
        }

        if (place.Kind == DesignBarPlaceKind.ContextMenu)
        {
            BeginTrayEdit(place.Component);
            return true;
        }

        if (place.Kind == DesignBarPlaceKind.Menu)
        {
            BeginInPlace(place.Component);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Opens <paramref name="contextMenu"/> to be edited (<c>_onEditContextMenu</c>, <c>_expand</c>): the
    /// tray open, and its first item selected when none of its items is.
    /// </summary>
    public void BeginTrayEdit(DesignComponent contextMenu)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        TrayOpen = true;
        if (contextMenu.Children.Count > 0 && !contextMenu.Children.Any(c => Document.Selection.Contains(c)))
        {
            Document.Select([contextMenu.Children[0]]);
        }
        else
        {
            TrayEditing = contextMenu;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends editing the context menu open, which is selected (the go-back line, Escape, <c>_onTrayStateChanged</c>).</summary>
    public void EndTrayEdit()
    {
        if (TrayEditing is not { } editing)
        {
            return;
        }

        TrayEditing = null;
        Document.Select([editing]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The menu for the press the tray last took.</summary>
    public IReadOnlyList<DesignMenuItem> TrayMenu() => MenuAt(new Point2D(0, 0));

    /// <summary>A library entry dragged over the tray: a context menu only, which goes in with no assignment (<c>_afterMouseMoveCanvasTray</c>).</summary>
    /// <returns>Whether it can be dropped there.</returns>
    public bool DragOverTray(DesignPaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Type != DesignBars.ContextMenuType)
        {
            return false;
        }

        AssignDrag = new DesignAssignDrag(null, null, "Create New Context Menu", DesignAssignHint.Tray, _assignAt, []);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>A context menu's library entry dropped in the tray: made, assigned to nothing.</summary>
    /// <returns>What was added.</returns>
    public IReadOnlyList<DesignComponent> DropOnTray(DesignPaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Cancel();
        _drawEntry = null;
        _libraryDrag = false;
        return entry.Type == DesignBars.ContextMenuType ? AddFrom(() => [Document.AddContextMenu()]) : [];
    }

    // --- the menus ---------------------------------------------------------------------------------

    /// <summary>What the bars' last press, or the menu key, asks a menu for; null when it is the canvas's own.</summary>
    private List<DesignMenuItem>? BarItems()
    {
        DesignDocument document = Document;
        DesignClipboard? clipboard = Clipboard;
        switch (_barMenu)
        {
            case BarMenu.MenuBar:
                // MenuBarMenuItemProvider.
                return
                [
                    new("Add Top-Level Menu", document.TopLevelMenus.Count > 0, () => document.AddMenuAtEnd(document.Design.Figure)),
                    DesignMenuItem.Separator,
                    new("Select All Menus", document.TopLevelMenus.Count > 0, document.SelectAllMenus),
                    new("Copy", clipboard is not null && document.CanCopy && document.Selection.All(static c => c.Type == DesignBars.MenuType), () => document.Copy(clipboard!)) { Shortcut = "Ctrl+C" },
                    new("Paste", clipboard is not null && document.CanPasteToMenuBar(clipboard), () => document.PasteToMenuBar(clipboard!)) { Shortcut = "Ctrl+V" },
                ];
            case BarMenu.Menu:
                // MenuMenuItemProvider, then StandardMenuItemsProvider with Paste to Right.
                (bool right, bool below, _) = document.MenuAbility;
                bool top = document.Selection is [{ } single] && DesignBars.IsTopLevelMenu(single);
                List<DesignMenuItem> menu =
                [
                    new("Add Menu Item", below, () => document.AddMenu(right: false)) { Shortcut = "Enter" },
                    new("Add Menu", right, () => document.AddMenu(right: true)) { Shortcut = "Shift+Enter" },
                    MoveMenu(across: top),
                    DesignMenuItem.Separator,
                ];
                AddStandard(menu, document.Selection.FirstOrDefault());
                return menu;
            case BarMenu.Toolbar:
                // ToolbarMenuItemProvider: its tools at the end, moved up and down.
                List<DesignMenuItem> bar =
                [
                    new("Add Push Tool", document.CanAddTool, () => document.AddTool(toggle: false, atEnd: true)) { Shortcut = "Shift+P" },
                    new("Add Toggle Tool", document.CanAddTool, () => document.AddTool(toggle: true, atEnd: true)) { Shortcut = "Shift+T" },
                    MoveMenu(across: false),
                    DesignMenuItem.Separator,
                ];
                AddStandard(bar, null);
                return bar;
            case BarMenu.Tool:
                // ToolMenuItemProvider: after the tool, moved left and right.
                List<DesignMenuItem> tool =
                [
                    new("Add Push Tool", document.CanAddTool, () => document.AddTool(toggle: false)) { Shortcut = "Shift+P" },
                    new("Add Toggle Tool", document.CanAddTool, () => document.AddTool(toggle: true)) { Shortcut = "Shift+T" },
                    MoveMenu(across: true),
                    DesignMenuItem.Separator,
                ];
                AddStandard(tool, null);
                return tool;
            case BarMenu.ContextMenu:
                // StandardMenuItemsProvider, then ContextMenuMenuItemProvider.
                List<DesignMenuItem> context = [];
                AddStandard(context, null);
                context.Add(DesignMenuItem.Separator);
                DesignComponent? one = document.Selection is [{ Type: DesignBars.ContextMenuType } alone] ? alone : null;
                IReadOnlyList<DesignComponent> assigned = one is null ? [] : document.AssignmentsOf(one);
                context.Add(new DesignMenuItem("Go to Component", assigned.Count > 0, Items: assigned.Count == 0 ? null
                    : assigned.Select(c => new DesignMenuItem("app." + c.CodeName, true, () => document.Select([c]))).ToArray()));
                context.Add(new DesignMenuItem(one is null ? "Edit" : "Edit app." + one.CodeName, one is not null, () => BeginTrayEdit(one!)));
                return context;
            case BarMenu.TrayPanel:
                // The tray's own menu: Paste of a context menu (_IsPasteDisabledForCanvasTray).
                return
                [
                    new("Paste", clipboard is not null && clipboard.HoldsOnly(DesignBars.ContextMenuType) && document.CanPaste(clipboard), () => document.Paste(clipboard!)) { Shortcut = "Ctrl+V" },
                ];
            default:
                return null;
        }
    }

    // Cut, Copy, Paste, Paste to Right for a menu, Duplicate and Delete (StandardMenuItemsProvider, the delete item).
    private void AddStandard(List<DesignMenuItem> items, DesignComponent? menu)
    {
        DesignDocument document = Document;
        DesignClipboard? clipboard = Clipboard;
        bool copy = document.CanCopy;
        items.Add(new DesignMenuItem("Cut", copy && clipboard is not null, () => document.Cut(clipboard!)) { Shortcut = "Ctrl+X" });
        items.Add(new DesignMenuItem("Copy", copy && clipboard is not null, () => document.Copy(clipboard!)) { Shortcut = "Ctrl+C" });
        items.Add(new DesignMenuItem("Paste", clipboard is not null && CanPasteOnBars(clipboard), () => document.Paste(clipboard!)) { Shortcut = "Ctrl+V" });
        if (menu is not null)
        {
            items.Add(new DesignMenuItem("Paste to Right", clipboard is not null && DesignDocument.CanPasteRight(clipboard), () => document.PasteRight(clipboard!, menu)));
        }

        items.Add(new DesignMenuItem("Duplicate", document.CanDuplicate, () => document.Duplicate()) { Shortcut = "Ctrl+D" });
        items.Add(new DesignMenuItem("Delete", document.Selection.Any(document.CanDelete), () => document.Delete(document.Selection)) { Shortcut = "Delete" });
    }

    // _isPasteDisabledForTool and _IsPasteDisabledForComponentsInsideCanvasTray on top of what a paste can do.
    private bool CanPasteOnBars(DesignClipboard clipboard)
    {
        DesignDocument document = Document;
        if (!document.CanPaste(clipboard) || clipboard.Clip is not { } clip)
        {
            return false;
        }

        if (document.Selection.Any(static c => c.Type == DesignBars.ToolbarType || DesignBars.IsTool(c))
            && !clip.Items.All(static i => i.Copy.Type == DesignBars.ToolbarType || DesignBars.IsTool(i.Copy)))
        {
            return false;
        }

        return !document.Selection.Any(static c => c.Type == DesignBars.ContextMenuType || DesignBars.IsContextMenuItem(c))
            || clip.Items[0].Copy.Type is DesignBars.ContextMenuType or DesignBars.MenuType;
    }

    /// <summary>
    /// A component's Context Menu submenu (<c>buildContextMenuAssignment</c>): for one with a context menu,
    /// Go to it, Replace With another, and Unassign Context Menu; for one without, Add New Context Menu and
    /// Assign one there is.
    /// </summary>
    private DesignMenuItem? ContextMenuItem()
    {
        DesignDocument document = Document;
        if (document.SelectionIsEmpty || document.Picks is not [var pick, ..])
        {
            return null;
        }

        DesignComponent? target = document.SelectedLabeled?.Field ?? pick.Component;
        if (target is null || !DesignDocument.TakesContextMenu(target))
        {
            return null;
        }

        var items = new List<DesignMenuItem>();
        DesignComponent? had = document.ContextMenuOf(target);
        IReadOnlyList<DesignMenuItem> Others() => document.ContextMenus.Where(c => !ReferenceEquals(c, had))
            .Select(c => new DesignMenuItem("app." + c.CodeName, true, () => document.AssignContextMenu(target, c))).ToArray();
        if (had is not null)
        {
            items.Add(new DesignMenuItem("Go to app." + had.CodeName, true, () => document.Select([had])));
            if (Others() is { Count: > 0 } others)
            {
                items.Add(new DesignMenuItem("Replace With", true, Items: others));
            }

            items.Add(new DesignMenuItem("Unassign Context Menu", true, () => document.UnassignContextMenu(target)));
        }
        else
        {
            items.Add(new DesignMenuItem("Add New Context Menu", true, () => document.AddContextMenu(target)));
            if (Others() is { Count: > 0 } others)
            {
                items.Add(new DesignMenuItem("Assign", true, Items: others));
            }
        }

        return new DesignMenuItem("Context Menu", true, Items: items);
    }

    /// <summary>Which bars' menu the menu key brings for the selection; none for the canvas's own.</summary>
    private BarMenu BarMenuOfSelection()
    {
        if (Document.SelectionIsEmpty || Document.Selection is not [var first, ..])
        {
            return BarMenu.None;
        }

        return first.Type switch
        {
            DesignBars.MenuType => BarMenu.Menu,
            DesignBars.ToolbarType => BarMenu.Toolbar,
            DesignBars.PushToolType or DesignBars.ToggleToolType => BarMenu.Tool,
            DesignBars.ContextMenuType => BarMenu.ContextMenu,
            _ => BarMenu.None,
        };
    }
}

/// <summary>A context menu dragged over the canvas: which (null from the library), the component under it, what it says and how, where, and who holds it now.</summary>
public sealed record DesignAssignDrag(DesignComponent? ContextMenu, DesignComponent? Target, string Text, DesignAssignHint Hint, Point2D At, IReadOnlyList<DesignComponent> Assigned);
