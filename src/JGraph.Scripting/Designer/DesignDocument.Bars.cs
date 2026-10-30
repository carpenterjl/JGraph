using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// A figure's menus, toolbars and context menus built on the canvas (App Designer window plan, stage
/// W5d): Add Menu to Right and Below, a top-level menu, Add Push Tool and Add Toggle Tool, Paste to Right,
/// a tool moved into another toolbar, and a context menu added, assigned, replaced and unassigned - each
/// one edit, by R2025b's <c>MenuAction</c>, <c>MenuBarAction</c>, <c>ToolAction</c>, <c>ToolbarAction</c>,
/// <c>StandardMenuItemsProvider</c>, <c>AssignContextMenuCommand</c>, <c>AddAndAssignContextMenuCommand</c>,
/// <c>UnassignContextMenuCommand</c> and <c>_ComponentContextMenuInfo</c>.
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>The menu bar's menus.</summary>
    public IReadOnlyList<DesignComponent> TopLevelMenus => DesignBars.TopLevelMenus(Design.Figure);

    /// <summary>The figure's toolbars, top to bottom.</summary>
    public IReadOnlyList<DesignComponent> Toolbars => DesignBars.Toolbars(Design.Figure);

    /// <summary>The figure's context menus.</summary>
    public IReadOnlyList<DesignComponent> ContextMenus => DesignBars.ContextMenus(Design.Figure);

    // --- menus -------------------------------------------------------------------------------------

    /// <summary>Whether Add Menu to Right, Add Menu Below and adding at the end apply (<c>MenuAction._updateAction</c>).</summary>
    public (bool Right, bool Below, bool AtEnd) MenuAbility => DesignBars.MenuAbility(Design.Figure, _selection);

    /// <summary>
    /// Add Menu to Right (Shift+Enter, <paramref name="right"/>) or Add Menu Below (Enter), as one edit
    /// (<c>_performAddMenu</c>): named among its siblings' texts, selected.
    /// </summary>
    /// <returns>The new menu, or null when it does not apply.</returns>
    public DesignComponent? AddMenu(bool right)
    {
        (bool canRight, bool canBelow, _) = MenuAbility;
        if ((right ? !canRight : !canBelow) || _selection is not [{ Parent: not null } selected])
        {
            return null;
        }

        (DesignComponent parent, int index) = DesignBars.AddMenuPlace(selected, right);

        // R2025b's figure holds its menus first, so that the place among them is the place among its
        // children; an app written otherwise keeps the new menu beside the selected one all the same.
        if (right && parent.Parent is null)
        {
            index = IndexIn(parent, selected) + 1;
        }

        return AddBarItem(DesignBars.MenuType, parent, index);
    }

    /// <summary>A menu added at the end of <paramref name="target"/>, as one edit (<c>_performAddMenuAtEnd</c>): the figure's after its last menu; any other's last.</summary>
    public DesignComponent AddMenuAtEnd(DesignComponent target)
    {
        ArgumentNullException.ThrowIfNull(target);
        (DesignComponent parent, int? index) = DesignBars.AddMenuAtEndPlace(target);
        return AddBarItem(DesignBars.MenuType, parent, index);
    }

    /// <summary>Selects the menu bar's menus (<c>MenuBarAction._performSelectAllMenus</c>).</summary>
    public void SelectAllMenus()
    {
        if (TopLevelMenus.Count > 0)
        {
            Select(TopLevelMenus);
        }
    }

    /// <summary>Whether Paste to Right applies: only menus on the clipboard (<c>admenumenu_paste_right</c>).</summary>
    public static bool CanPasteRight(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        return clipboard.HoldsOnly(DesignBars.MenuType);
    }

    /// <summary>
    /// Paste to Right on <paramref name="menu"/>, as one edit (<c>_handlePasteRight</c>, <c>PasteAtIndexCommand</c>):
    /// after a top-level menu; under any other, last.
    /// </summary>
    /// <returns>Whether anything was pasted.</returns>
    public bool PasteRight(DesignClipboard clipboard, DesignComponent menu)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(menu);
        if (!CanPasteRight(clipboard) || clipboard.Clip is not { } clip)
        {
            return false;
        }

        bool top = DesignBars.IsTopLevelMenu(menu);
        return PasteMenus(clipboard, clip, top ? menu.Parent! : menu, top ? IndexIn(menu.Parent!, menu) + 1 : null);
    }

    /// <summary>Whether the menu bar's Paste applies: only menus on the clipboard (<c>MenuBarAction._updateAction</c>).</summary>
    public bool CanPasteToMenuBar(DesignClipboard clipboard) => TopLevelMenus.Count > 0 && CanPasteRight(clipboard);

    /// <summary>The menu bar's Paste: as top-level menus, after the last, as one edit (<c>_performPasteMenus</c>).</summary>
    /// <returns>Whether anything was pasted.</returns>
    public bool PasteToMenuBar(DesignClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        return CanPasteToMenuBar(clipboard) && clipboard.Clip is { } clip && PasteMenus(clipboard, clip, Design.Figure, TopLevelMenus.Count);
    }

    private bool PasteMenus(DesignClipboard clipboard, DesignClip clip, DesignComponent parent, int? index)
    {
        EndGesture(commit: false);
        bool sameApp = ReferenceEquals(clip.Source, this);
        var placed = new List<Placed>();
        for (int i = 0; i < clip.Items.Count; i++)
        {
            placed.Add(new Placed(null, null, index is { } at ? at + i : null));
        }

        bool keepCallbacks = sameApp && clipboard.WasCut && clipboard.PasteCount(this) == 1;
        Do([.. CopyEdits(clip, parent, placed, keepCallbacks, sameApp, keepGroups: true, reassign: sameApp && clipboard.WasCut)]);
        return true;
    }

    // --- tools -------------------------------------------------------------------------------------

    /// <summary>Whether Add Push Tool and Add Toggle Tool apply: one tool or toolbar selected (their menus' <c>disabled: l.length&gt;1</c>).</summary>
    public bool CanAddTool => _selection is [{ Type: DesignBars.ToolbarType or DesignBars.PushToolType or DesignBars.ToggleToolType }];

    /// <summary>
    /// Add Push Tool (Shift+P) or Add Toggle Tool (Shift+T) by a tool's menu and keys, as one edit
    /// (<c>ToolAction._performAddTool</c>): after the selected tool, first in a selected toolbar. By a
    /// toolbar's menu and keys, or <paramref name="toolbar"/>'s +, last (<c>ToolbarAction._performAddTool</c>,
    /// with <paramref name="atEnd"/>).
    /// </summary>
    /// <returns>The new tool, or null when it does not apply.</returns>
    public DesignComponent? AddTool(bool toggle, bool atEnd = false, DesignComponent? toolbar = null)
    {
        string type = toggle ? DesignBars.ToggleToolType : DesignBars.PushToolType;
        if (toolbar is not null)
        {
            return AddBarItem(type, toolbar, null);
        }

        if (!CanAddTool)
        {
            return null;
        }

        DesignComponent selected = _selection[0];
        if (atEnd)
        {
            return AddBarItem(type, DesignBars.AddToolAtEndPlace(selected), null);
        }

        (DesignComponent parent, int index) = DesignBars.AddToolPlace(selected);
        return AddBarItem(type, parent, index);
    }

    private DesignComponent AddBarItem(string type, DesignComponent parent, int? index)
    {
        EndGesture(commit: false);
        DesignComponent made = Palette.MakeAmongSiblings(type, Design, parent);
        Do([new AddEdit(made, parent, index)]);
        return made;
    }

    /// <summary>
    /// The selected tools moved into <paramref name="toolbar"/> from <paramref name="index"/> on, as one
    /// edit (<c>ToolDragModel</c>, <c>MoveCommand</c>): each put there from the last, so they keep their order.
    /// </summary>
    public void MoveToolsTo(DesignComponent toolbar, int index)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        List<DesignComponent> tools = _selection.Where(DesignBars.IsTool).ToList();
        if (tools.Count == 0 || tools[0].Parent is not { } from)
        {
            return;
        }

        EndGesture(commit: false);
        Do(tools.OrderByDescending(t => IndexIn(from, t))
            .Select(t => (Edit)new ParentEdit(t, from, IndexIn(from, t), toolbar, index)).ToArray());
    }

    /// <summary>A toolbar added by the library at <paramref name="y"/> on the surface goes among the toolbars by where it was dropped, after the menus (<c>_setDefaultInsertIndex</c>).</summary>
    private int ToolbarIndexFor(double y)
    {
        double top = -BarsHeightAbove();
        IReadOnlyList<DesignComponent> toolbars = Toolbars;
        double[] middles = toolbars.Select((_, k) => top + (k * DesignBarLayout.ToolbarHeight) + (DesignBarLayout.ToolbarHeight / 2)).ToArray();
        return TopLevelMenus.Count + DesignBars.ToolbarDropIndex(middles, y);
    }

    // The toolbars' top: the bars' height less the menu bar's.
    private double BarsHeightAbove() => Toolbars.Count * DesignBarLayout.ToolbarHeight;

    // --- context menus -----------------------------------------------------------------------------

    /// <summary>Whether <paramref name="component"/> takes a context menu (<c>buildContextMenuAssignment</c>): none of the figure's tools, menus or context menus.</summary>
    public static bool TakesContextMenu(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Type is not (DesignBars.ContextMenuType or DesignBars.MenuType or DesignBars.ToolbarType or DesignBars.PushToolType or DesignBars.ToggleToolType);
    }

    /// <summary>The context menu assigned to <paramref name="component"/>, or null (<c>getContextMenuForComponent</c>).</summary>
    public DesignComponent? ContextMenuOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Get("ContextMenu") is ComponentsValue { CodeNames: [string name] } ? Design.Find(name) : null;
    }

    /// <summary>The components <paramref name="contextMenu"/> is assigned to (<c>getContextMenuAssignments</c>): the figure first, then its components as they stand.</summary>
    public IReadOnlyList<DesignComponent> AssignmentsOf(DesignComponent contextMenu)
    {
        ArgumentNullException.ThrowIfNull(contextMenu);
        return Design.Components.Where(c => ReferenceEquals(ContextMenuOf(c), contextMenu)).ToArray();
    }

    /// <summary>Assigns <paramref name="contextMenu"/> to <paramref name="target"/>, in place of any it had, as one edit (<c>AssignContextMenuCommand</c>); the selection stays.</summary>
    /// <returns>Whether it changed.</returns>
    public bool AssignContextMenu(DesignComponent target, DesignComponent contextMenu)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(contextMenu);
        if (!TakesContextMenu(target) || ReferenceEquals(ContextMenuOf(target), contextMenu))
        {
            return false;
        }

        EndGesture(commit: false);
        Do([AssignEdit(target, contextMenu), new StaySelected(target)]);
        return true;
    }

    /// <summary>Takes <paramref name="target"/>'s context menu off, as one edit (<c>UnassignContextMenuCommand</c>).</summary>
    /// <returns>Whether it had one.</returns>
    public bool UnassignContextMenu(DesignComponent target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Get("ContextMenu") is not { } assigned)
        {
            return false;
        }

        EndGesture(commit: false);
        Do([new PropertyEdit(target, "ContextMenu", assigned, null, assigned, new NumericValue(0, 0, [])) { Selects = false }, new StaySelected(target)]);
        return true;
    }

    /// <summary>
    /// A new context menu with its two items, assigned to <paramref name="target"/> when given, as one
    /// edit (<c>AddAndAssignContextMenuCommand</c>, <c>_onCreateContextMenu</c>, a library drop); the new
    /// context menu is selected.
    /// </summary>
    public DesignComponent AddContextMenu(DesignComponent? target = null)
    {
        EndGesture(commit: false);
        DesignPaletteEntry entry = Palette.Entries.First(static e => e.Type == DesignBars.ContextMenuType);
        (DesignComponent made, _) = Palette.Make(entry, Design, Design.Figure, new Point2D(0, 0))[0];
        List<Edit> step = [new AddEdit(made, Design.Figure, null)];
        if (target is not null && TakesContextMenu(target))
        {
            step.Add(AssignEdit(target, made));
        }

        Do([.. step]);
        return made;
    }

    /// <summary>What a value the bars read at every draw holds: the design's, or the class's default; not read from the runtime.</summary>
    public DesignValue? HeldOrDefault(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        return component.Get(name) ?? component.Class?.Property(name)?.DefaultFor(Theme);
    }

    // Nothing changed, and the selection stays where it is (an assignment's, as AssignContextMenuCommand leaves it).
    private sealed record StaySelected(DesignComponent Component) : Edit(Component)
    {
        public override bool KeepsSelection => true;

        public override void Do(DesignDocument document)
        {
        }

        public override void Undo(DesignDocument document)
        {
        }
    }

    private PropertyEdit AssignEdit(DesignComponent target, DesignComponent contextMenu)
    {
        DesignValue? before = target.Get("ContextMenu");
        var value = new ComponentsValue([contextMenu.CodeName], false);
        return new PropertyEdit(target, "ContextMenu", before, value, before ?? new NumericValue(0, 0, []), value) { Selects = false };
    }

    /// <summary>
    /// The component that takes a context menu dragged to <paramref name="at"/>
    /// (<c>getCandidateAssignTargets</c>, <c>AssignmentValidation.update</c>): the frontmost and innermost
    /// shown one whose box, 2 pixels larger each way, holds the point, the figure last; null outside it.
    /// </summary>
    public DesignComponent? AssignTargetAt(Point2D at)
    {
        DesignComponent? Search(DesignComponent component)
        {
            if (!TakesContextMenu(component))
            {
                return null;
            }

            for (int k = component.Children.Count - 1; k >= 0; k--)
            {
                if (Search(component.Children[k]) is { } found)
                {
                    return found;
                }
            }

            Rect2D box;
            if (component.Parent is null)
            {
                Size2D size = Runtime.Figure.Size;
                box = new Rect2D(0, 0, size.Width, size.Height);
            }
            else if (BoxOf(component) is { Shown: true } shown)
            {
                box = shown.Box;
            }
            else
            {
                return null;
            }

            return at.X > box.X - 2 && at.X < box.X + box.Width + 2 && at.Y > box.Y - 2 && at.Y < box.Y + box.Height + 2 ? component : null;
        }

        return Search(Design.Figure);
    }

    /// <summary>
    /// What a context menu <paramref name="codeName"/> dragged says over <paramref name="target"/>
    /// (<c>ContextMenuDragTooltip.updateContent</c>), from the library with <paramref name="fromLibrary"/>.
    /// </summary>
    /// <returns>The words, and how it says them: valid, its assignment replaced or the same one (information), or nowhere to go.</returns>
    public (string Text, DesignAssignHint Hint) AssignMessage(string codeName, DesignComponent? target, bool fromLibrary)
    {
        ArgumentNullException.ThrowIfNull(codeName);
        if (target is null)
        {
            return (fromLibrary ? "Drag to component or to Context Menus area" : "Drag to component to assign", DesignAssignHint.Invalid);
        }

        string on = "app." + target.CodeName;
        if (ContextMenuOf(target) is { } had)
        {
            return had.CodeName == codeName
                ? ($"'app.{codeName}' already assigned to {on}", DesignAssignHint.Same)
                : (fromLibrary ? $"Replace 'app.{had.CodeName}' with New Context Menu" : $"Replace 'app.{had.CodeName}' with 'app.{codeName}'", DesignAssignHint.Replace);
        }

        return (fromLibrary ? $"Create and Assign Context Menu to {on}" : $"Assign 'app.{codeName}' to {on}", DesignAssignHint.Valid);
    }
}

/// <summary>How a context menu's drag shows where it is (<c>ad-contextmenu-dragAssign-*</c>).</summary>
public enum DesignAssignHint
{
    /// <summary>Nowhere to go (<c>invalid</c>).</summary>
    Invalid,

    /// <summary>Assigned there on the drop (<c>valid</c>).</summary>
    Valid,

    /// <summary>Taking the place of what is assigned there (<c>info</c>).</summary>
    Replace,

    /// <summary>Already assigned there (<c>info</c>, <c>ad-contextmenu-same-assign-hint</c>).</summary>
    Same,

    /// <summary>A new context menu assigned to nothing, over the tray (<c>createNewContextMenuWithoutAssign</c>).</summary>
    Tray,
}
