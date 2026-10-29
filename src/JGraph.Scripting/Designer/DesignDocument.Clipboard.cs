using System.Text.RegularExpressions;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// The clipboard on the canvas (App Designer window plan, stage W5): Cut, Copy, Paste, Paste at a
/// point, Duplicate, a Ctrl-drag's copy and Select All, each one edit, by R2025b's commands
/// (<c>CopyCommand</c>, <c>CutCommand</c>, <c>PasteCommand</c>, <c>PasteAtCommand</c>,
/// <c>DuplicateCommand</c>, <c>DuplicateAtCommand</c>, <c>DuplicateInGridCommand</c>,
/// <c>CopyComponentCommand</c>, <c>CopyCallbackCommand</c>, <c>SelectAllComponentsCommand</c>) and
/// their helpers (<c>CopyPasteUtil</c>, <c>CopyCodeNameGenerator</c>, <c>GridUtil.findLocation</c>).
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>How far each paste after the first into a container moves along and down (<c>PASTE_OFFSET</c>).</summary>
    public const double PasteOffset = 20;

    /// <summary>How far in from its container's top-left corner a paste into another container lands (<c>PASTE_UPPER_LEFT_INSET</c>).</summary>
    public const double PasteInset = 8;

    /// <summary>How far a duplicate lands along and down from what it copies (<c>DUPLICATE_OFFSET</c>).</summary>
    public const double DuplicateOffset = 20;

    private const string TabGroupType = "matlab.ui.container.TabGroup";
    private const string TreeType = "matlab.ui.container.Tree";
    private const string CheckBoxTreeType = "matlab.ui.container.CheckBoxTree";
    private const string TreeNodeType = "matlab.ui.container.TreeNode";
    private const string PanelType = "matlab.ui.container.Panel";
    private const string ButtonGroupType = "matlab.ui.container.ButtonGroup";
    private const string PushToolType = "matlab.ui.container.toolbar.PushTool";
    private const string ToggleToolType = "matlab.ui.container.toolbar.ToggleTool";
    private const string RadioButtonType = "matlab.ui.control.RadioButton";
    private const string ToggleButtonType = "matlab.ui.control.ToggleButton";

    // The models placed by their order among their siblings (PositionIsOrderBased).
    private static readonly HashSet<string> OrderBased = new(StringComparer.Ordinal)
    {
        TabType, MenuType, ContextMenuType, ToolbarType, PushToolType, ToggleToolType, TreeNodeType,
    };

    private bool _nothingSelected;

    /// <summary>
    /// Whether App Designer's selection is empty: after a delete, whose components are gone (the
    /// canvas shows the figure's properties). A paste then goes back where its components came from.
    /// </summary>
    public bool SelectionIsEmpty => _nothingSelected || _picks.Count == 0;

    // --- what applies ------------------------------------------------------------------------------

    /// <summary>
    /// Whether Cut, Copy and Duplicate apply to the selection (<c>areSelectionsValidForCutCopyOrDuplicateAction</c>):
    /// something other than the figure selected, all in one container, and a group among it or a
    /// component that can be copied - anything but a responsive app's region.
    /// </summary>
    public bool CanCopy
    {
        get
        {
            if (_picks.Count == 0 || _nothingSelected || _picks.Any(static p => p.Component is { Parent: null }))
            {
                return false;
            }

            DesignComponent[] leaves = _picks.SelectMany(static p => p.Component is { } c ? [c] : p.Group!.AllComponents()).ToArray();
            DesignComponent? parent = leaves.FirstOrDefault()?.Parent;
            return parent is not null && leaves.All(c => ReferenceEquals(c.Parent, parent))
                && _picks.Any(p => p.Group is not null || CanBeCopied(p.Component!));
        }
    }

    /// <summary>Whether Duplicate applies to the selection: as Cut and Copy do (<c>CopyAction</c> enables both).</summary>
    public bool CanDuplicate => CanCopy;

    // RegionPanelModel's DesignGestures: CanCut, CanCopy, CanDuplicate and CanDragDuplicate are false.
    private bool CanBeCopied(DesignComponent component) =>
        component.Parent is not null && !IsRegion(component) && !IsResponsiveGrid(component);

    /// <summary>Whether <paramref name="container"/> can take a component of <paramref name="type"/> pasted into it (each model's <c>canMoveToParent</c>).</summary>
    private bool CanReceive(DesignComponent container, string type) => type switch
    {
        TabType => container.Type == TabGroupType,
        TreeNodeType => container.Type is TreeType or CheckBoxTreeType or TreeNodeType,
        MenuType => container.Type is FigureType or MenuType or ContextMenuType,
        PushToolType or ToggleToolType => container.Type == ToolbarType,
        ContextMenuType or ToolbarType => container.Parent is null,
        RadioButtonType or ToggleButtonType => container.Type == ButtonGroupType,
        _ => IsGrid(container) || container.Type is FigureType or PanelType or TabType or ButtonGroupType,
    };

    // --- copy and cut ------------------------------------------------------------------------------

    /// <summary>Copies the selection to <paramref name="clipboard"/> (<c>CopyCommand</c>); false when Copy does not apply.</summary>
    public bool Copy(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (!CanCopy || ClipOf(_picks) is not { } clip)
        {
            return false;
        }

        EndGesture(commit: false);
        clipboard.Set(clip, cut: false);
        return true;
    }

    /// <summary>Copies the selection to <paramref name="clipboard"/> and deletes it, the delete one edit (<c>CutCommand</c>); false when Cut does not apply.</summary>
    public bool Cut(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (!CanCopy || ClipOf(_picks) is not { } clip)
        {
            return false;
        }

        EndGesture(commit: false);
        clipboard.Set(clip, cut: true);
        Delete(Selection);
        return true;
    }

    /// <summary>
    /// What <paramref name="picks"/> give the clipboard (<c>CopyCommand</c>): each component that can be
    /// copied, or each component of a group selected whole, copied with everything in it, in its
    /// container's order; the groups selected whole and those inside them; and each context menu's
    /// components. Null when nothing can be copied.
    /// </summary>
    private DesignClip? ClipOf(IReadOnlyList<DesignPick> picks)
    {
        var tops = new List<DesignComponent>();
        foreach (DesignPick pick in picks)
        {
            foreach (DesignComponent c in pick.Component is { } one ? [one] : pick.Group!.AllComponents())
            {
                if (CanBeCopied(c) && !tops.Contains(c, ReferenceEqualityComparer.Instance))
                {
                    tops.Add(c);
                }
            }
        }

        if (tops.Count == 0)
        {
            return null;
        }

        DesignComponent parent = tops[0].Parent!;
        tops = [.. tops.OrderBy(c => IndexIn(c.Parent!, c))];
        var items = new List<DesignClipItem>();
        foreach (DesignComponent c in tops)
        {
            DesignComponent copy = CloneTree(c);
            Rect2D? outer = null;
            bool orderBased = OrderBased.Contains(c.Type) || c.Type == GridType;
            if (!orderBased && Runtime.PositionOf(c) is { } position)
            {
                // The place it holds now, written out: the runtime knows a place the design may leave unset.
                outer = OuterPositionOf(c) ?? position;
                copy.Set("Position", NumericValue.Row(position.X, position.Y, position.Width, position.Height));
            }

            GridSpan? span = IsGridChild(c) ? SpanOf(c) : null;
            if (span is { } s)
            {
                copy.Set("Layout.Row", s.RowValue);
                copy.Set("Layout.Column", s.ColumnValue);
            }

            string[] assigned = c.Type == ContextMenuType
                ? Design.Components.Where(o => o.Get("ContextMenu") is ComponentsValue { CodeNames: [string menu] } && menu == c.CodeName).Select(static o => o.CodeName).ToArray()
                : [];
            items.Add(new DesignClipItem(copy, c, outer, span, OrderBased.Contains(c.Type), assigned));
        }

        // The groups selected whole, and those inside them (findGroupsInSelection).
        var groups = new List<DesignClipGroup>();
        foreach (DesignGroup group in picks.Where(static p => p.Group is not null).Select(static p => p.Group!))
        {
            if (ClipGroupOf(group, tops, items) is { } made)
            {
                groups.Add(made);
            }
        }

        return new DesignClip(items, groups, this, parent);
    }

    private static DesignClipGroup? ClipGroupOf(DesignGroup group, List<DesignComponent> tops, List<DesignClipItem> items)
    {
        var made = new DesignClipGroup();
        foreach (DesignGroupMember member in group.Members)
        {
            if (member.Component is { } c && tops.FindIndex(t => ReferenceEquals(t, c)) is var i and >= 0)
            {
                made.Members.Add((i, null));
                items[i].Group = made;
            }
            else if (member.Group is { } inner && ClipGroupOf(inner, tops, items) is { } innerMade)
            {
                innerMade.Parent = made;
                made.Members.Add((null, innerMade));
            }
        }

        return made.Members.Count > 0 ? made : null;
    }

    /// <summary>A copy of <paramref name="component"/> and everything in it, belonging to no design: its code names and properties as they are.</summary>
    private static DesignComponent CloneTree(DesignComponent component)
    {
        var copy = new DesignComponent(component.CodeName, component.Type, component.Function, component.ExtraArguments) { Class = component.Class };
        foreach ((string name, DesignValue value) in component.Properties)
        {
            copy.Set(name, value);
        }

        foreach (DesignComponent child in component.Children)
        {
            copy.Add(CloneTree(child));
        }

        return copy;
    }

    // --- paste ---------------------------------------------------------------------------------------

    /// <summary>
    /// The container a paste of <paramref name="clip"/> goes into (<c>CopyPasteUtil.getValidParent</c>):
    /// with nothing selected, the container its components came from in the same app (the figure when
    /// that is gone, or in another app); with several selected, the tree nodes' parent, the group's
    /// container or the first one's container; with one, as <see cref="PasteParentFor"/> says.
    /// </summary>
    private DesignComponent PasteParent(DesignClip clip)
    {
        if (SelectionIsEmpty)
        {
            return ReferenceEquals(clip.Source, this) && IsInDesign(clip.SourceParent) ? clip.SourceParent : Design.Figure;
        }

        if (_picks.Count > 1)
        {
            DesignPick first = _picks[0];
            return first.Component is { Type: TreeNodeType } && Anchor?.Component is { Parent: { } anchorParent } ? anchorParent
                : first.Group is { Container: { } container } ? container
                : first.Component?.Parent ?? Design.Figure;
        }

        return PasteParentFor(clip, _picks[0]);
    }

    /// <summary>
    /// The container a paste goes into with one pick selected (<c>getValidParentWithSingleSelection</c>):
    /// the figure for the figure and for a context menu; a container itself, but a tab group's shown
    /// tab, a tab's group for a tab, a menu's parent for a menu, a tree node's or tree's parent for a
    /// node or tree, a toolbar for a tool and the figure for a toolbar, a panel's only grid; a group's
    /// container; a tool's toolbar for a tool; any other component's container.
    /// </summary>
    private DesignComponent PasteParentFor(DesignClip clip, DesignPick pick)
    {
        string type = clip.Items[0].Copy.Type;
        if (pick.Group is { } group)
        {
            return group.Container ?? Design.Figure;
        }

        DesignComponent k = pick.Component!;
        if (k.Parent is null)
        {
            return k;
        }

        if (type == ContextMenuType)
        {
            return Design.Figure;
        }

        return k.Type switch
        {
            ToolbarType => type == ToolbarType ? Design.Figure : k,
            TabGroupType => type == TabType ? k : Runtime.ShownTabOf(k) ?? k.Children.FirstOrDefault() ?? k,
            TabType => type == TabType ? k.Parent : k,
            MenuType => type == MenuType ? k.Parent : Design.Figure,
            TreeNodeType => type == TreeNodeType ? k.Parent : Design.Figure,
            TreeType or CheckBoxTreeType => type is TreeType or CheckBoxTreeType ? k.Parent : type == TreeNodeType ? k : Design.Figure,
            PanelType => k.Children is [var only] && IsGrid(only) ? only : k,
            ContextMenuType => type != MenuType ? Design.Figure : k,
            FigureType or ButtonGroupType or GridType => k,
            PushToolType or ToggleToolType => type is PushToolType or ToggleToolType ? k.Parent : Design.Figure,
            _ => k.Parent,
        };
    }

    /// <summary>
    /// Whether everything on the clipboard can go into <paramref name="parent"/>: each component that
    /// keeps to a kind of container one of that kind (<c>isValidParentForContents</c>), the rest a
    /// container that holds components; and no grid into a container holding a grid that is not
    /// one itself (<c>checkIfCanPaste</c>).
    /// </summary>
    private bool CanPasteInto(DesignClip clip, DesignComponent parent) =>
        clip.Items.All(i => CanReceive(parent, i.Copy.Type))
        && !(clip.Items.Any(static i => i.Copy.Type == GridType) && parent.Children.Any(IsGrid) && !IsGrid(parent));

    /// <summary>Whether Paste applies: something on <paramref name="clipboard"/> that can go where a paste puts it (<c>PasteAction._clipboardCanBePasted</c>).</summary>
    public bool CanPaste(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        return clipboard.Clip is { } clip && CanPasteInto(clip, PasteParent(clip));
    }

    /// <summary>
    /// Pastes <paramref name="clipboard"/> as one edit (<c>PasteCommand</c>), into the container
    /// <see cref="PasteParent"/> gives; what is pasted is selected. Into the container its components
    /// came from they keep their places, moved 20 pixels along and down for each paste there after the
    /// first (after a cut, the first lands where they were); into another, their box's top-left
    /// corner 8 pixels in from its top-left, and moved as often - a tab's components into a sibling
    /// tab, or another app's into a figure, keep their places. Into a grid, the first free cells.
    /// </summary>
    /// <returns>Whether anything was pasted.</returns>
    public bool Paste(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (clipboard.Clip is not { } clip)
        {
            return false;
        }

        DesignComponent parent = PasteParent(clip);
        if (!CanPasteInto(clip, parent))
        {
            return false;
        }

        EndGesture(commit: false);
        bool sameApp = ReferenceEquals(clip.Source, this);
        DesignComponent pasteParent = sameApp && IsInDesign(clip.SourceParent) ? clip.SourceParent : Design.Figure;
        bool sameContainer = sameApp && ReferenceEquals(parent, pasteParent);
        int count = clipboard.CountPaste(this, parent);
        int step = sameContainer && !clipboard.WasCut ? count : count - 1;
        bool exact = sameApp
            ? !sameContainer && parent.Type == TabType && pasteParent.Type == TabType && ReferenceEquals(pasteParent.Parent, parent.Parent)
            : parent.Parent is null;

        List<Placed>? placed = IsGrid(parent)
            ? GridPlaces(clip, parent, null, null)
            : ShiftedPlaces(clip, parent, sameContainer || exact, step);
        if (placed is null)
        {
            clipboard.UncountPaste(this, parent);
            return false;
        }

        IReadOnlyList<int?> indexes = OrderIndexes(clip, menusAtEnd: false);
        for (int i = 0; i < placed.Count; i++)
        {
            placed[i] = placed[i] with { Index = indexes[i] };
        }

        bool keepCallbacks = sameApp && clipboard.WasCut && clipboard.PasteCount(this) == 1;
        Do([.. CopyEdits(clip, parent, placed, keepCallbacks, sameApp, keepGroups: !IsGrid(parent), reassign: sameApp && clipboard.WasCut),
            new CountEdit(parent, clipboard, this, parent)]);
        return true;
    }

    /// <summary>
    /// Pastes <paramref name="clipboard"/> where the canvas's menu was opened, as one edit
    /// (<c>StandardMenuItemsProvider._handlePasteSelectedComponents</c>, <c>PasteAtCommand</c>): into
    /// the container a paste with the component there selected goes into, its box's top-left corner
    /// at <paramref name="at"/>; into a grid, from the cell under it.
    /// </summary>
    /// <returns>Whether anything was pasted.</returns>
    public bool PasteAt(DesignClipboard clipboard, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (clipboard.Clip is not { } clip || PasteAtParent(clip, at) is not { } parent || !CanPasteInto(clip, parent))
        {
            return false;
        }

        EndGesture(commit: false);
        bool sameApp = ReferenceEquals(clip.Source, this);
        List<Placed>? placed;
        if (IsGrid(parent))
        {
            (int? row, int? column) = CellAt(parent, at);
            placed = GridPlaces(clip, parent, row, column);
        }
        else
        {
            Rect2D inner = Runtime.InnerOnSurface(parent);
            double left = System.Math.Round(at.X - inner.X, MidpointRounding.AwayFromZero) + 1;
            double top = inner.Height - System.Math.Round(at.Y - inner.Y, MidpointRounding.AwayFromZero) + 1;
            placed = PlacesAt(clip, left, top);
        }

        if (placed is null)
        {
            return false;
        }

        IReadOnlyList<int?> indexes = OrderIndexes(clip, menusAtEnd: true);
        for (int i = 0; i < placed.Count; i++)
        {
            placed[i] = placed[i] with { Index = indexes[i] };
        }

        bool keepCallbacks = sameApp && clipboard.WasCut && clipboard.PasteCount(this) == 1;
        Do([.. CopyEdits(clip, parent, placed, keepCallbacks, sameApp, keepGroups: !IsGrid(parent), reassign: sameApp && clipboard.WasCut)]);
        return true;
    }

    /// <summary>
    /// Whether the canvas menu's Paste applies at <paramref name="at"/> (<c>_getIsPasteDisabled</c>):
    /// something on the clipboard that can go into the container there, and room for it from the
    /// cell under the point in a grid (<c>canPasteComponentsIn</c>).
    /// </summary>
    public bool CanPasteAt(DesignClipboard clipboard, Point2D at)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (clipboard.Clip is not { } clip || PasteAtParent(clip, at) is not { } parent || !CanPasteInto(clip, parent))
        {
            return false;
        }

        if (!IsGrid(parent))
        {
            return true;
        }

        (int? row, int? column) = CellAt(parent, at);
        return GridPlaces(clip, parent, row, column) is not null;
    }

    // The container under the menu's point (_getContainerDataForClickPosition): a paste's for the component there.
    private DesignComponent? PasteAtParent(DesignClip clip, Point2D at) =>
        ComponentAt(at) is { } hit ? PasteParentFor(clip, DesignPick.Of(hit)) : Design.Figure;

    private (int? Row, int? Column) CellAt(DesignComponent grid, Point2D at)
    {
        if (CellsOf(grid) is not { } cells)
        {
            return (null, null);
        }

        GridPointInfo info = DesignGridRules.PointInfo(at, cells);
        return (info.Row, info.Column);
    }

    // --- duplicate -----------------------------------------------------------------------------------

    /// <summary>
    /// Duplicates the selection in its container, as one edit (<c>DuplicateCommand</c>): each copy 20
    /// pixels along and down from what it copies, or in a grid in the first free cells; the copies are
    /// selected, and take no callbacks.
    /// </summary>
    /// <returns>Whether anything was duplicated.</returns>
    public bool Duplicate()
    {
        if (!CanDuplicate || ClipOf(_picks) is not { } clip)
        {
            return false;
        }

        clip = ByPositionType(clip);
        DesignComponent parent = clip.SourceParent;
        if (!CanPasteInto(clip, parent))
        {
            return false;
        }

        EndGesture(commit: false);
        List<Placed>? placed = IsGrid(parent) ? GridPlaces(clip, parent, null, null) : DuplicatePlaces(clip);
        if (placed is null)
        {
            return false;
        }

        int maxIndex = clip.Items.Where(static i => i.OrderBased).Select(i => IndexIn(parent, i.Source)).DefaultIfEmpty(-1).Max();
        for (int i = 0; i < placed.Count; i++)
        {
            placed[i] = placed[i] with { Index = clip.Items[i].OrderBased ? maxIndex + i + 1 : null };
        }

        Do([.. CopyEdits(clip, parent, placed, keepCallbacks: false, sameApp: true, keepGroups: true, reassign: false)]);
        return true;
    }

    /// <summary>The clip with the components placed by order first, in their order, then the rest (<c>orderByPositionType</c>).</summary>
    private DesignClip ByPositionType(DesignClip clip)
    {
        if (!clip.Items.Any(static i => i.OrderBased))
        {
            return clip;
        }

        DesignClipItem[] ordered = [.. clip.Items.Where(static i => i.OrderBased).OrderBy(i => IndexIn(clip.SourceParent, i.Source)), .. clip.Items.Where(static i => !i.OrderBased)];
        var map = ordered.Select((item, at) => (Old: clip.Items.ToList().IndexOf(item), New: at)).ToDictionary(static p => p.Old, static p => p.New);
        foreach (DesignClipGroup group in clip.Groups)
        {
            Renumber(group);
        }

        return clip with { Items = ordered };

        void Renumber(DesignClipGroup group)
        {
            for (int i = 0; i < group.Members.Count; i++)
            {
                (int? item, DesignClipGroup? inner) = group.Members[i];
                if (item is { } index)
                {
                    group.Members[i] = (map[index], null);
                }
                else
                {
                    Renumber(inner!);
                }
            }
        }
    }

    private static List<Placed> DuplicatePlaces(DesignClip clip) =>
        clip.Items.Select(static i => i.OrderBased || i.Copy.Type == GridType || i.Outer is null
            ? new Placed(null, null, null)
            : new Placed(Shift(i, DuplicateOffset, -DuplicateOffset), null, null)).ToList();

    /// <summary>
    /// Ends a Ctrl-drag (<c>DragDuplicateController</c>): copies of the gesture's components where
    /// they were dropped, as one edit (<c>DuplicateAtCommand</c>, <c>DuplicateInGridCommand</c>); the
    /// components themselves stay where they were. Into a grid each copy takes the cells the
    /// placement gives it, and none is made when any would take its own component's cells.
    /// </summary>
    private void EndDuplicate(Gesture gesture, DesignPlacement? placement)
    {
        if (ClipOf(_picks) is not { } clip)
        {
            return;
        }

        DesignComponent[] moving = [.. gesture.Start.Keys];
        DesignComponent from = clip.SourceParent;
        var placed = new List<Placed>();
        DesignComponent into;
        if (placement is { Container: { } grid, Spans: { } spans } && IsGrid(grid) && spans.Count == moving.Length)
        {
            into = grid;
            foreach (DesignClipItem item in clip.Items)
            {
                int at = Array.FindIndex(moving, m => ReferenceEquals(m, item.Source));
                if (at < 0 || (ReferenceEquals(item.Source.Parent, grid) && SpanOf(item.Source) == spans[at]))
                {
                    return;
                }

                placed.Add(new Placed(null, spans[at], null));
            }
        }
        else
        {
            DesignComponent? target = placement?.Container;
            into = target is not null && !IsGrid(target) && clip.Items.All(i => CanReceive(target, i.Copy.Type)) ? target : from;
            if (IsGrid(into))
            {
                return;
            }

            foreach (DesignClipItem item in clip.Items)
            {
                if (!gesture.Last.TryGetValue(item.Source, out Rect2D box) || !gesture.Start.TryGetValue(item.Source, out Rect2D start))
                {
                    placed.Add(new Placed(null, null, null));
                    continue;
                }

                // Where its drawn self was dragged, at the size it has - a grid's child by its own box, not its cells'.
                Rect2D drawn = BoxOf(item.Source)?.Box ?? start;
                Rect2D position = Runtime.ToPosition(into, new Rect2D(drawn.X + box.X - start.X, drawn.Y + box.Y - start.Y, drawn.Width, drawn.Height));
                placed.Add(new Placed(new Rect2D(
                    System.Math.Round(position.X, MidpointRounding.AwayFromZero),
                    System.Math.Round(position.Y, MidpointRounding.AwayFromZero),
                    System.Math.Round(position.Width, MidpointRounding.AwayFromZero),
                    System.Math.Round(position.Height, MidpointRounding.AwayFromZero)), null, null));
            }
        }

        if (!CanPasteInto(clip, into))
        {
            return;
        }

        Do([.. CopyEdits(clip, into, placed, keepCallbacks: false, sameApp: true, keepGroups: true, reassign: false)]);
    }

    // --- select all ----------------------------------------------------------------------------------

    /// <summary>
    /// Selects everything at the top of the figure (<c>SelectAllComponentsCommand</c>): with a menu of
    /// the menu bar selected, the menu bar's menus; with tools, their toolbar's tools; with toolbars,
    /// the toolbars; else the figure's other components - a group whole - or the figure when it has none.
    /// </summary>
    public void SelectAll()
    {
        EndGesture(commit: false);
        DesignComponent figure = Design.Figure;
        IEnumerable<DesignComponent> children = figure.Children;
        if (_selection.Any(c => c.Type == MenuType && ReferenceEquals(c.Parent, figure)))
        {
            SetPicks(children.Where(static c => c.Type == MenuType).Select(DesignPick.Of).ToArray());
            return;
        }

        if (_selection.Any(static c => c.Type is ToolbarType or PushToolType or ToggleToolType))
        {
            if (_selection.All(static c => c.Type is PushToolType or ToggleToolType))
            {
                SetPicks(_selection[0].Parent!.Children.Select(DesignPick.Of).ToArray());
            }
            else if (_selection.All(static c => c.Type == ToolbarType))
            {
                SetPicks(_selection[0].Parent!.Children.Where(static c => c.Type == ToolbarType).Select(DesignPick.Of).ToArray());
            }

            return;
        }

        DesignComponent[] all = children.Where(c => c.Type is not (MenuType or ToolbarType or ContextMenuType) && !IsResponsiveGrid(c)).ToArray();
        if (all.Length == 0)
        {
            SetPicks([DesignPick.Of(figure)]);
            return;
        }

        var picks = new List<DesignPick>();
        foreach (DesignComponent c in all)
        {
            DesignPick pick = Design.RootGroupOf(c) is { } group ? DesignPick.Of(group) : DesignPick.Of(c);
            if (!DesignGroupRules.IsSelected(picks, pick))
            {
                picks.Add(pick);
            }
        }

        SetPicks(picks);
    }

    // --- where the copies go -------------------------------------------------------------------------

    /// <summary>Where a copy goes: its new <c>Position</c> (null to keep none it has), its cells in a grid, its index among its siblings.</summary>
    private sealed record Placed(Rect2D? Position, GridSpan? Span, int? Index);

    private static Rect2D? PositionOfCopy(DesignClipItem item) =>
        item.Copy.Get("Position") is NumericValue { Data: [var x, var y, var w, var h] } ? new Rect2D(x, y, w, h) : null;

    /// <summary>The item's <c>Position</c> moved by (<paramref name="dx"/>, <paramref name="dy"/>) in MATLAB's pixels, y up.</summary>
    private static Rect2D? Shift(DesignClipItem item, double dx, double dy) =>
        PositionOfCopy(item) is { } p ? new Rect2D(p.X + dx, p.Y + dy, p.Width, p.Height) : null;

    /// <summary>
    /// <c>PasteCommand._positionInTargetContainer</c>: the box round the items' outer boxes has its
    /// top-left corner 8 pixels in from the container's (none moved when <paramref name="exact"/>),
    /// then moved 20 pixels along and down <paramref name="step"/> times.
    /// </summary>
    private List<Placed> ShiftedPlaces(DesignClip clip, DesignComponent parent, bool exact, int step)
    {
        Rect2D[] outers = clip.Items.Where(static i => !i.OrderBased && i.Copy.Type != GridType && i.Outer is not null).Select(static i => i.Outer!.Value).ToArray();
        double minX = outers.Length > 0 ? outers.Min(static o => o.X) : 0;
        double maxY = outers.Length > 0 ? outers.Max(static o => o.Y + o.Height) : 0;
        double height = Runtime.InnerOnSurface(parent).Height;
        double p = exact ? 0 : minX - PasteInset;
        double x = exact ? 0 : maxY - height + PasteInset;
        return clip.Items.Select(i => i.OrderBased || i.Copy.Type == GridType || i.Outer is null
            ? new Placed(null, null, null)
            : new Placed(Shift(i, -p + (step * PasteOffset), -x - (step * PasteOffset)), null, null)).ToList();
    }

    /// <summary><c>PasteAtCommand</c>: the box round the items' outer boxes with its top-left corner at (<paramref name="left"/>, <paramref name="top"/>), each rounded.</summary>
    private static List<Placed> PlacesAt(DesignClip clip, double left, double top)
    {
        Rect2D[] outers = clip.Items.Where(static i => !i.OrderBased && i.Copy.Type is not (GridType or ContextMenuType) && i.Outer is not null).Select(static i => i.Outer!.Value).ToArray();
        double minX = outers.Length > 0 ? outers.Min(static o => o.X) : 0;
        double maxY = outers.Length > 0 ? outers.Max(static o => o.Y + o.Height) : 0;
        return clip.Items.Select(i =>
        {
            if (i.OrderBased || i.Copy.Type is GridType or ContextMenuType || i.Outer is not { } outer || PositionOfCopy(i) is not { } position)
            {
                return new Placed(null, null, null);
            }

            double newLeft = System.Math.Round(left + (outer.X - minX), MidpointRounding.AwayFromZero);
            double newBottom = System.Math.Round(top + (outer.Y + outer.Height - maxY) - outer.Height, MidpointRounding.AwayFromZero);
            return new Placed(new Rect2D(newLeft + (position.X - outer.X), newBottom + (position.Y - outer.Y), position.Width, position.Height), null, null);
        }).ToList();
    }

    /// <summary>
    /// The cells each item takes in <paramref name="grid"/> (<c>GridUtil.findLocation</c>), from the
    /// cell at <paramref name="row"/> and <paramref name="column"/> or the first: an item from a grid
    /// its span's size, any other one cell, a group's items their group's box. Null when they do not all fit.
    /// </summary>
    private List<Placed>? GridPlaces(DesignClip clip, DesignComponent grid, int? row, int? column)
    {
        int rows = TracksOf(grid, GridAxis.Row).Count;
        int columns = TracksOf(grid, GridAxis.Column).Count;
        var occupied = grid.Children.Select(SpanOf).Where(static s => s is not null).Select(static s => s!.Value).ToArray();
        var entries = clip.Items.Select(i => (i.Span, i.Group)).ToArray();
        IReadOnlyList<(int Row, int Column)>? found = FindLocation(entries, rows, columns, occupied, row, column);
        if (found is null)
        {
            return null;
        }

        // applyLocationForComponentsInfo: a span keeps its size from the cell found.
        return clip.Items.Select((item, i) =>
        {
            (int r, int c) = found[i];
            GridSpan span = item.Span is { } s
                ? new GridSpan(r, r + s.RowEnd - s.RowStart, c, c + s.ColumnEnd - s.ColumnStart)
                : new GridSpan(r, r, c, c);
            return new Placed(null, span, null);
        }).ToList();
    }

    /// <summary>
    /// <c>GridUtil.findLocation</c>, transcribed: the cells free in a <paramref name="rows"/> by
    /// <paramref name="columns"/> grid are read row by row from the start cell; each entry takes the
    /// first place where its span's size fits - the corner cells and the second column of the rows
    /// between them free, as App Designer checks - and a group's entries take their group's box, kept
    /// as it was, at its first. Null when not every entry finds a place.
    /// </summary>
    internal static IReadOnlyList<(int Row, int Column)>? FindLocation(
        IReadOnlyList<(GridSpan? Span, DesignClipGroup? Group)> entries, int rows, int columns, IReadOnlyList<GridSpan> occupied, int? startRow, int? startColumn)
    {
        var groups = new Dictionary<DesignClipGroup, GroupBox>(ReferenceEqualityComparer.Instance);
        foreach ((GridSpan? span, DesignClipGroup? group) in entries)
        {
            if (span is not { } s || group is null)
            {
                continue;
            }

            if (!groups.TryGetValue(group, out GroupBox? box))
            {
                box = new GroupBox();
                groups[group] = box;
            }

            box.RowStart = System.Math.Min(s.RowStart, box.RowStart);
            box.RowEnd = System.Math.Max(s.RowEnd, box.RowEnd);
            box.ColumnStart = System.Math.Min(s.ColumnStart, box.ColumnStart);
            box.ColumnEnd = System.Math.Max(s.ColumnEnd, box.ColumnEnd);
        }

        var taken = new bool[rows, columns];
        foreach (GridSpan s in occupied)
        {
            for (int r = s.RowStart; r <= s.RowEnd; r++)
            {
                for (int c = s.ColumnStart; c <= s.ColumnEnd; c++)
                {
                    if (r >= 1 && r <= rows && c >= 1 && c <= columns)
                    {
                        taken[r - 1, c - 1] = true;
                    }
                }
            }
        }

        int cellCount = rows * columns;
        var found = new List<(int, int)>();
        int at = 0;
        int cell = ((startRow ?? 1) - 1) * columns + ((startColumn ?? 1) - 1);
        while (cell < cellCount && at < entries.Count)
        {
            (GridSpan? span, DesignClipGroup? group) = entries[at];
            int width = 1, height = 1;
            GroupBox? box = null;
            if (span is { } s)
            {
                box = group is not null ? groups.GetValueOrDefault(group) : null;
                if (box is { Processed: true })
                {
                    found.Add((s.RowStart + box.RowDelta, s.ColumnStart + box.ColumnDelta));
                    at++;
                    continue;
                }

                if (box is not null)
                {
                    height = box.RowEnd - box.RowStart + 1;
                    width = box.ColumnEnd - box.ColumnStart + 1;
                }
                else
                {
                    width = s.ColumnEnd - s.ColumnStart + 1;
                    height = s.RowEnd - s.RowStart + 1;
                }
            }

            int r0 = cell / columns, c0 = cell % columns;
            if (c0 < columns - width + 1 && r0 < rows - height + 1 && !taken[r0, c0] && !taken[r0 + height - 1, c0 + width - 1])
            {
                bool free = true;
                for (int r = r0 + 1; r < r0 + height - 1 && free; r++)
                {
                    // App Designer's inner loop breaks after its first column, so only that one is looked at.
                    if (c0 + 1 < c0 + width - 1 && taken[r, c0 + 1])
                    {
                        free = false;
                    }
                }

                if (free)
                {
                    for (int r = r0; r <= r0 + height - 1; r++)
                    {
                        for (int c = c0; c <= c0 + width - 1; c++)
                        {
                            taken[r, c] = true;
                        }
                    }

                    if (box is not null)
                    {
                        box.RowDelta = r0 + 1 - box.RowStart;
                        box.ColumnDelta = c0 + 1 - box.ColumnStart;
                        box.Processed = true;
                        continue;
                    }

                    found.Add((r0 + 1, c0 + 1));
                    at++;
                }
            }

            cell++;
        }

        return found.Count == entries.Count ? found : null;
    }

    private sealed class GroupBox
    {
        public int RowStart { get; set; } = int.MaxValue;

        public int RowEnd { get; set; } = int.MinValue;

        public int ColumnStart { get; set; } = int.MaxValue;

        public int ColumnEnd { get; set; } = int.MinValue;

        public int RowDelta { get; set; }

        public int ColumnDelta { get; set; }

        public bool Processed { get; set; }
    }

    /// <summary>
    /// Where each item placed by its order goes among its new siblings (<c>PasteCommand._getOrderedPositionData</c>):
    /// after the last of the selection placed by order, in turn; with none, a menu after the menu bar's
    /// menus and anything else last.
    /// </summary>
    private IReadOnlyList<int?> OrderIndexes(DesignClip clip, bool menusAtEnd)
    {
        DesignComponent[] selected = _picks.SelectMany(static p => p.Component is { } c ? [c] : p.Group!.AllComponents()).ToArray();
        int maxIndex = -1;
        if (selected is [{ Type: ToolbarType } toolbar, ..] && clip.Items[0].Copy.Type is PushToolType or ToggleToolType)
        {
            maxIndex = toolbar.Children.Count;
        }
        else if (!menusAtEnd && selected.Length > 0 && selected[0].Parent is { } parent)
        {
            foreach (DesignComponent c in selected.Where(static c => OrderBased.Contains(c.Type)))
            {
                maxIndex = System.Math.Max(maxIndex, IndexIn(parent, c));
            }
        }

        var indexes = new int?[clip.Items.Count];
        for (int i = 0; i < clip.Items.Count; i++)
        {
            DesignClipItem item = clip.Items[i];
            if (item.Copy.Type == MenuType && (maxIndex < 0 || menusAtEnd))
            {
                // getOrderIndexForMenus: after the menu bar's menus, unless a menu is selected.
                int? index = selected is [] || selected[0].Type != MenuType ? Design.Figure.Children.Count(static c => c.Type == MenuType) : null;
                if (i >= 1 && index is { } m && indexes[i - 1] is { } previous && m <= previous)
                {
                    index = previous + 1;
                }

                indexes[i] = index;
            }
            else if (item.OrderBased && !menusAtEnd)
            {
                indexes[i] = maxIndex < 0 ? null : maxIndex + i + 1;
            }
        }

        return indexes;
    }

    // --- the copies ----------------------------------------------------------------------------------

    /// <summary>
    /// The edits that put copies of <paramref name="clip"/>'s items into <paramref name="parent"/> as
    /// <paramref name="placed"/> says (<c>CopyComponentCommand</c>): each copy and everything in it
    /// renamed as <c>CopyCodeNameGenerator</c> names copies; references among them following, others
    /// kept in the same app when what they name is there; callbacks taken off, unless
    /// <paramref name="keepCallbacks"/> (a cut's first paste), when each is bound again and made again
    /// if it went; context menu assignments dropped in another app; a radio or toggle button selected
    /// only in a group with none; a check box tree's checked nodes cleared; the groups made again
    /// (only the labelled ones without <paramref name="keepGroups"/>); a cut context menu assigned
    /// again (<paramref name="reassign"/>); the copies selected, the selection put back on an undo.
    /// </summary>
    private List<Edit> CopyEdits(DesignClip clip, DesignComponent parent, IReadOnlyList<Placed> placed, bool keepCallbacks, bool sameApp, bool keepGroups, bool reassign)
    {
        DesignComponent[] copies = clip.Items.Select(static i => CloneTree(i.Copy)).ToArray();
        Dictionary<string, string> renamed = CopyNames(clip, copies, NamesInUse().ToList());
        var edits = new List<Edit>();
        var remade = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < copies.Length; i++)
        {
            DesignComponent copy = copies[i];
            foreach (DesignComponent c in Walk(copy))
            {
                foreach ((string name, DesignValue value) in c.Properties.ToArray())
                {
                    switch (value)
                    {
                        case ComponentsValue when (c.Type == CheckBoxTreeType && name == "CheckedNodes") || (!sameApp && name == "ContextMenu"):
                            c.Unset(name);
                            break;
                        case ComponentsValue refs:
                            string[] kept = refs.CodeNames.Select(n => renamed.TryGetValue(n, out string? to) ? to : sameApp && Design.Find(n) is not null ? n : null)
                                .Where(static n => n is not null).Select(static n => n!).ToArray();
                            if (kept.Length == 0)
                            {
                                c.Unset(name);
                            }
                            else
                            {
                                c.Set(name, new ComponentsValue(kept, refs.Bracketed));
                            }

                            break;
                        case CallbackValue when !keepCallbacks:
                            c.Unset(name);
                            break;
                        case CallbackValue callback when Code is not null && Code.Find(callback.Method) is null && remade.Add(callback.Method):
                            // CopyCallbackCommand._copyCallback: a callback the cut took is made again, with App Designer's code for it.
                            IReadOnlyList<string> defaults = DesignCallbacks.R2025b.DefaultBody(c, DesignCallbacks.PropertyName(name));
                            edits.Add(new CallbackListEdit(Design.Figure, new DesignCallback(callback.Method, defaults.Select(l => Style.Indent(3) + l)) { DefaultBody = defaults },
                                NewCallbackIndex() + remade.Count - 1, Add: true) { Selects = false });
                            break;
                    }
                }
            }

            // handleValueProperties: a radio or toggle button pasted alone is selected only in a group with none.
            if (copy.Type is RadioButtonType or ToggleButtonType)
            {
                if (!parent.Children.Any(static c => c.Type is RadioButtonType or ToggleButtonType))
                {
                    copy.Set("Value", NumericValue.Logical(true));
                }
                else
                {
                    copy.Unset("Value");
                }
            }

            Placed place = placed[i];
            if (IsGrid(parent))
            {
                copy.Unset("Position");
                if (place.Span is { } span)
                {
                    copy.Set("Layout.Row", span.RowValue);
                    copy.Set("Layout.Column", span.ColumnValue);
                }
            }
            else
            {
                copy.Unset("Layout.Row");
                copy.Unset("Layout.Column");
                if (copy.Type == GridType)
                {
                    copy.Unset("Position");
                }
                else if (place.Position is { } p)
                {
                    copy.Set("Position", NumericValue.Row(p.X, p.Y, p.Width, p.Height));
                }
            }
        }

        // Added at their indexes in turn.
        for (int i = 0; i < copies.Length; i++)
        {
            edits.Add(new AddEdit(copies[i], parent, placed[i].Index));
        }

        // The groups, inner ones first, none selected by itself.
        var made = new Dictionary<DesignClipGroup, DesignGroup>(ReferenceEqualityComparer.Instance);
        foreach (DesignClipGroup group in clip.Groups.Where(g => keepGroups || g.IsLabeled(clip.Items)))
        {
            MakeGroup(group);
        }

        // A cut context menu goes back on the components it was assigned to that have none now.
        if (reassign)
        {
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i].Type != ContextMenuType)
                {
                    continue;
                }

                foreach (string name in clip.Items[i].AssignedTo)
                {
                    if (Design.Find(name) is { } owner && owner.Get("ContextMenu") is null)
                    {
                        var value = new ComponentsValue([copies[i].CodeName], bracketed: false);
                        edits.Add(new PropertyEdit(owner, "ContextMenu", null, value, RuntimeBefore: null, RuntimeAfter: value) { Selects = false });
                    }
                }
            }
        }

        var picks = new List<DesignPick>();
        for (int i = 0; i < copies.Length; i++)
        {
            DesignClipGroup? top = clip.Items[i].Group;
            while (top?.Parent is { } up)
            {
                top = up;
            }

            DesignPick pick = top is not null && made.TryGetValue(top, out DesignGroup? group) ? DesignPick.Of(group) : DesignPick.Of(copies[i]);
            if (!DesignGroupRules.IsSelected(picks, pick))
            {
                picks.Add(pick);
            }
        }

        edits.Add(new SelectionEdit(copies[0], _picks.ToArray(), picks));
        return edits;

        DesignGroup MakeGroup(DesignClipGroup group)
        {
            var members = new List<DesignGroupMember>();
            foreach ((int? item, DesignClipGroup? inner) in group.Members)
            {
                members.Add(item is { } index ? DesignGroupMember.Of(copies[index]) : DesignGroupMember.Of(MakeGroup(inner!)));
            }

            var designGroup = new DesignGroup();
            made[group] = designGroup;
            edits.Add(new GroupEdit(copies[group.AllItems().First()], designGroup, members, null, null) { Selects = false, SelectsGroup = false });
            return designGroup;
        }
    }

    /// <summary>
    /// The code names of copies (<c>CopyCodeNameGenerator</c>): each component's name with any
    /// <c>_N</c> taken off, made unique among the app's names and those given before it - its
    /// class's name when that is too long; a labelled component's field so, and its label the
    /// field's new name with <c>Label</c> after it, or its own name when the field's was named by hand.
    /// The items first, then what is inside each, level by level. Sets them on
    /// <paramref name="copies"/> - the items' copies - adding them to <paramref name="inUse"/>, and
    /// returns each old name's new one.
    /// </summary>
    internal static Dictionary<string, string> CopyNames(DesignClip clip, IReadOnlyList<DesignComponent> copies, List<string> inUse)
    {
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);

        // The top level as App Designer's hierarchy data has it: a group where its first item is.
        var top = new List<(DesignComponent? Component, DesignClipGroup? Group)>();
        var done = new HashSet<DesignClipGroup>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < copies.Count; i++)
        {
            DesignClipGroup? group = clip.Items[i].Group;
            while (group?.Parent is { } up)
            {
                group = up;
            }

            if (group is null)
            {
                top.Add((copies[i], null));
            }
            else if (done.Add(group))
            {
                top.Add((null, group));
            }
        }

        Name(top);
        return renamed;

        void Name(IReadOnlyList<(DesignComponent? Component, DesignClipGroup? Group)> nodes)
        {
            var later = new List<IReadOnlyList<(DesignComponent?, DesignClipGroup?)>>();
            foreach ((DesignComponent? component, DesignClipGroup? group) in nodes)
            {
                if (group is not null)
                {
                    if (group.IsLabeled(clip.Items))
                    {
                        NameLabeled(copies[group.Members[0].Item!.Value], copies[group.Members[1].Item!.Value]);
                    }
                    else
                    {
                        Name(group.Members.Select(m => m.Item is { } i ? ((DesignComponent?)copies[i], (DesignClipGroup?)null) : (null, m.Group)).ToArray());
                    }

                    continue;
                }

                string name = CopyName(component!.CodeName);
                if (name.Length > DesignNames.NameLengthMax)
                {
                    name = CopyName(DefaultCodeName(component.Type));
                }

                SetName(component, name);
                inUse.Add(name);
                if (component.Children.Count > 0)
                {
                    later.Add(component.Children.Select(static c => ((DesignComponent?)c, (DesignClipGroup?)null)).ToArray());
                }
            }

            foreach (IReadOnlyList<(DesignComponent?, DesignClipGroup?)> children in later)
            {
                Name(children);
            }
        }

        void NameLabeled(DesignComponent first, DesignComponent second)
        {
            (DesignComponent label, DesignComponent field) = first.Type == DesignGroup.LabelType ? (first, second) : (second, first);
            string fieldName = field.CodeName;
            string defaultCodeName = DefaultCodeName(field.Type);
            string defaultText = DesignNames.Of(field.Type)?.Text ?? string.Empty;
            string labelText = (label.Get("Text") is { } text ? DesignPalette.Text(text) : null) ?? string.Empty;
            string g = CopyName(fieldName);
            string labelBase = DesignNames.IsNamedByHand(labelText, defaultText, fieldName, defaultCodeName) ? label.CodeName : g + "Label";
            if (g.Length > DesignNames.NameLengthMax)
            {
                g = CopyName(defaultCodeName);
            }

            string a = CopyName(labelBase);
            if (a.Length > DesignNames.NameLengthMax)
            {
                a = CopyName(defaultCodeName + "Label");
            }

            SetName(field, g);
            SetName(label, a);
            inUse.Add(g);
            inUse.Add(a);
        }

        string CopyName(string name) => DesignNames.AppendUniqueIncrement(inUse, NumberedName().Replace(name, "$1"), distinct: true);

        void SetName(DesignComponent component, string name)
        {
            renamed[component.CodeName] = name;
            component.CodeName = name;
        }
    }

    private static string DefaultCodeName(string type) => DesignNames.Of(type)?.CodeName ?? type[(type.LastIndexOf('.') + 1)..];

    [GeneratedRegex(@"^(.*)_(\d)+$")]
    private static partial Regex NumberedName();

    // --- the edits -------------------------------------------------------------------------------

    /// <summary>Nothing changed, but <paramref name="After"/> selected once the step is done and <paramref name="Before"/> once it is undone (<c>CopyComponentCommand</c>).</summary>
    private sealed record SelectionEdit(DesignComponent Component, IReadOnlyList<DesignPick> Before, IReadOnlyList<DesignPick> After) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
        }

        public override void Undo(DesignDocument document)
        {
        }

        public override IReadOnlyList<DesignPick>? PicksAfter(bool done) => done ? After : Before;
    }

    /// <summary>A paste counted into its container (<c>PasteCommand</c>'s undo and redo); counted when made, so its first do counts nothing.</summary>
    private sealed record CountEdit(DesignComponent Component, DesignClipboard Clipboard, DesignDocument Document, DesignComponent Container) : Edit(Component)
    {
        private bool _counted = true;

        public override bool KeepsSelection => true;

        public override void Do(DesignDocument document)
        {
            if (!_counted)
            {
                Clipboard.CountPaste(Document, Container);
            }

            _counted = false;
        }

        public override void Undo(DesignDocument document) => Clipboard.UncountPaste(Document, Container);
    }
}
