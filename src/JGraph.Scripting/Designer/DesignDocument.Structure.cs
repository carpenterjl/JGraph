namespace JGraph.Scripting.Designer;

/// <content>
/// The structure of tab groups, button groups and trees on the canvas (App Designer window plan,
/// stage W5c): Add Tab and a tab moved among its siblings, Add Radio Button and Add Toggle Button, a
/// tree's Add Sibling, Add Child and indents, and a node moved among its siblings - each one edit,
/// by R2025b's <c>AddTabMenuItemProvider</c>, <c>TabStripMouseController</c>, <c>ReorderAction</c>,
/// <c>ButtonGroupMenuItemProvider</c>, <c>MutualExclusiveButtonUtil</c>, <c>TreeAction</c> and
/// <c>TreeUtil</c>; and what a deleted tab or node leaves selected (<c>DeleteCommand</c>).
/// </content>
public sealed partial class DesignDocument
{
    // The models whose DesignGestures allow CanReorder: a tab and a tree node (the menus and tools come with W5d).
    private static bool CanReorderInside(DesignComponent component) => component.Type is TabType or TreeNodeType;

    // --- tabs --------------------------------------------------------------------------------------

    /// <summary>Whether Add Tab applies: one tab group or one tab selected (<c>AddTabMenuItemProvider</c>).</summary>
    public bool CanAddTab => _picks is [{ Component.Type: TabGroupType or TabType }];

    /// <summary>
    /// Add Tab (the canvas menu's): a tab after the selected one, or last in the selected tab group
    /// (<c>_handleAddTab</c>); see <see cref="AddTab(DesignComponent, int?)"/>.
    /// </summary>
    /// <returns>The new tab, or null when Add Tab does not apply.</returns>
    public DesignComponent? AddTab()
    {
        if (_picks is not [{ Component: { Type: TabGroupType or TabType } picked }])
        {
            return null;
        }

        return picked.Type == TabType
            ? AddTab(picked.Parent!, IndexIn(picked.Parent!, picked) + 1)
            : AddTab(picked);
    }

    /// <summary>
    /// A tab added to <paramref name="group"/> at <paramref name="index"/>, or last - Add Tab, or the
    /// tab strip's + (<c>TabStripMouseController._addTab</c>) - as one edit: titled among its siblings'
    /// titles (<c>TabsUtil.createTabComponentData</c>), selected and shown.
    /// </summary>
    public DesignComponent AddTab(DesignComponent group, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Type != TabGroupType)
        {
            throw new ArgumentException($"{group.CodeName} is no tab group.", nameof(group));
        }

        EndGesture(commit: false);
        DesignComponent tab = Palette.MakeAmongSiblings(TabType, Design, group);
        Do([new AddEdit(tab, group, index)]);
        return tab;
    }

    // --- the order of tabs and nodes ---------------------------------------------------------------

    /// <summary>
    /// The selected tabs or nodes Move moves, in their siblings' order, and every sibling they move
    /// among (<c>ReorderAction._filterAndOrderByIndex</c> and <c>_getReorderableComponents</c> for the
    /// internal order).
    /// </summary>
    private (List<DesignComponent> Moving, List<DesignComponent> Among)? MoveSet()
    {
        if (_picks.Count == 0 || _picks.Any(static p => p.Component is null))
        {
            return null;
        }

        List<DesignComponent> chosen = _picks.Select(static p => p.Component!).Where(CanReorderInside).ToList();
        if (chosen.Count == 0 || chosen[0].Parent is not { } parent)
        {
            return null;
        }

        List<DesignComponent> among = [.. parent.Children];
        List<DesignComponent> moving = among.Where(c => chosen.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
        return moving.Count == 0 ? null : (moving, among);
    }

    /// <summary>
    /// Whether the selection moves among its siblings rather than restacks: a tab or a tree node among
    /// it, which the Ctrl+Shift keys move (<c>ArrowKeyMoveReorderPlugin</c>, the tree's own keys).
    /// </summary>
    public bool MovesInside => MoveSet() is not null;

    /// <summary>Whether Move can move the selection <paramref name="how"/> (<c>_checkAbilityToReorder</c> for the internal order).</summary>
    public bool CanMove(DesignMove how)
    {
        if (MoveSet() is not { } set)
        {
            return false;
        }

        var can = DesignStructure.Ability(set.Among.Count, [.. set.Moving.Select(c => set.Among.IndexOf(c))], stack: false);
        return how switch
        {
            DesignMove.First => can.First,
            DesignMove.Previous => can.Previous,
            DesignMove.Next => can.Next,
            _ => can.Last,
        };
    }

    /// <summary>
    /// Moves the selected tabs or nodes <paramref name="how"/> among their siblings, as one edit
    /// (<c>ReorderAction._performReorder</c>, <c>ReorderCommand</c>); the selection stays.
    /// </summary>
    /// <returns>Whether the order changed.</returns>
    public bool Move(DesignMove how) => CanMove(how) && MoveSet() is { } set && Reorder(set, how, null);

    /// <summary>
    /// A tab heading's drag dropped at <paramref name="index"/> (<c>DESIGN_REORDER_OFFSET</c>, which
    /// <c>TabDragController</c> runs): the selected tabs one after another from there, as one edit.
    /// </summary>
    /// <returns>Whether the order changed.</returns>
    public bool MoveTo(int index) => MoveSet() is { } set && Reorder(set, null, index);

    private bool Reorder((List<DesignComponent> Moving, List<DesignComponent> Among) set, DesignMove? how, int? drop)
    {
        EndGesture(commit: false);
        int[] order = DesignStructure.Order(set.Among.Count, [.. set.Moving.Select(c => set.Among.IndexOf(c))], how, drop);
        List<DesignComponent> after = order.Select(k => set.Among[k]).ToList();
        return ReorderEdits(set.Moving[0].Parent!, set.Among, after) is { } edit && DoOrder([edit]);
    }

    // --- button groups -----------------------------------------------------------------------------

    /// <summary>
    /// The kind of button <paramref name="group"/> holds (<c>MutualExclusiveChildrenType</c>): its first
    /// radio or toggle button's, a radio button for a group with neither.
    /// </summary>
    public static string ButtonTypeOf(DesignComponent group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Children.FirstOrDefault(static c => c.Type is RadioButtonType or ToggleButtonType)?.Type ?? RadioButtonType;
    }

    /// <summary>
    /// The kind of button Add Radio Button or Add Toggle Button adds to the one button group selected
    /// (<c>ButtonGroupMenuItemProvider</c>); null when neither applies.
    /// </summary>
    public string? AddButtonType => _picks is [{ Component: { Type: ButtonGroupType } group }] ? ButtonTypeOf(group) : null;

    /// <summary>
    /// Add Radio Button or Add Toggle Button, as one edit (<c>executeAddComponentPropertyEditorCommand</c>):
    /// a button of the group's kind, named among its buttons, under its lowest one or 10 pixels below
    /// its top, then sized to its text keeping its top (<c>setComponentToPreferredSize</c>), selected
    /// when it is the first; the group stays selected.
    /// </summary>
    /// <returns>The new button, or null when neither applies.</returns>
    public DesignComponent? AddButton()
    {
        if (_picks is not [{ Component: { Type: ButtonGroupType } group }])
        {
            return null;
        }

        EndGesture(commit: false);
        string type = ButtonTypeOf(group);
        bool toggle = type == ToggleButtonType;
        double[] bottoms = group.Children.Where(static c => c.Type is RadioButtonType or ToggleButtonType)
            .Select(c => Runtime.PositionOf(c)?.Y).OfType<double>().ToArray();
        (double x, double y, bool value) = DesignStructure.ButtonPlace(toggle, Runtime.InnerOnSurface(group).Height, bottoms);

        DesignComponent button = Palette.MakeAmongSiblings(type, Design, group);
        double startHeight = toggle ? DesignStructure.ToggleButtonSize.Height : DesignStructure.RadioButtonSize.Height;
        string text = DesignPalette.Text(button.Get("Text")!) ?? string.Empty;
        (double width, double height) = DesignStructure.ButtonPreferredSize(toggle, text);
        button.Set("Position", NumericValue.Row(x, y + startHeight - height, width, height));
        if (value)
        {
            button.Set("Value", NumericValue.Logical(true));
        }

        Do([new AddEdit(button, group, null) { Selects = false }, new SelectEdit(group)]);
        return button;
    }

    // --- trees -------------------------------------------------------------------------------------

    /// <summary>Whether Add Sibling and Add Child apply: one tree node or one tree selected (<c>TreeAction._updateAction</c>).</summary>
    public bool CanAddTreeNode => _picks is [{ Component.Type: TreeNodeType or TreeType or CheckBoxTreeType }];

    /// <summary>
    /// Add Sibling (Enter) or, with <paramref name="child"/>, Add Child (Shift+Enter), as one edit
    /// (<c>TreeAction._performAddNode</c>): a node after the selected node, or first inside it; last in
    /// a selected tree either way. Named among its siblings, shown and selected.
    /// </summary>
    /// <returns>The new node, or null when neither applies.</returns>
    public DesignComponent? AddTreeNode(bool child)
    {
        if (_picks is not [{ Component: { Type: TreeNodeType or TreeType or CheckBoxTreeType } picked }])
        {
            return null;
        }

        EndGesture(commit: false);
        (DesignComponent parent, int? index) = picked.Type != TreeNodeType ? (picked, (int?)null)
            : child ? (picked, 0)
            : (picked.Parent!, IndexIn(picked.Parent!, picked) + 1);
        DesignComponent node = Palette.MakeAmongSiblings(TreeNodeType, Design, parent);
        Do([new AddEdit(node, parent, index)]);
        return node;
    }

    /// <summary>
    /// Where Increase Indent or Decrease Indent moves the one selected node
    /// (<c>_getParentNodeAndIndexForIndentAction</c>): into the node before it, last; or out of its
    /// node, after it. Null when it has no node before it, or its parent is the tree.
    /// </summary>
    private (DesignComponent Node, DesignComponent To, int? At)? IndentTarget(bool increase)
    {
        if (_picks is not [{ Component: { Type: TreeNodeType, Parent: { } parent } node }])
        {
            return null;
        }

        if (increase)
        {
            int at = IndexIn(parent, node);
            return at == 0 ? null : (node, parent.Children[at - 1], null);
        }

        return parent.Parent is { Type: TreeNodeType or TreeType or CheckBoxTreeType } grand ? (node, grand, IndexIn(grand, parent) + 1) : null;
    }

    /// <summary>Whether Increase Indent (<paramref name="increase"/>) or Decrease Indent applies to the selection.</summary>
    public bool CanIndent(bool increase) => IndentTarget(increase) is not null;

    /// <summary>
    /// Increase Indent (Ctrl+]) or Decrease Indent (Ctrl+[), as one edit (<c>TreeAction._performIndentNode</c>,
    /// <c>MoveCommand</c>): the selected node into the node before it, last, or out of its node, after
    /// it; shown and selected.
    /// </summary>
    /// <returns>Whether it moved.</returns>
    public bool Indent(bool increase)
    {
        if (IndentTarget(increase) is not { } target)
        {
            return false;
        }

        EndGesture(commit: false);
        DesignComponent from = target.Node.Parent!;
        Do([new ParentEdit(target.Node, from, IndexIn(from, target.Node), target.To, target.At)]);
        return true;
    }

    /// <summary>Whether <paramref name="node"/> is in a check box tree, where one node at a time is selected (<c>TreeNodeModel.isSingleSelection</c>).</summary>
    public static bool IsInCheckBoxTree(DesignComponent node)
    {
        ArgumentNullException.ThrowIfNull(node);
        DesignComponent? up = node.Parent;
        while (up is { Type: TreeNodeType })
        {
            up = up.Parent;
        }

        return up?.Type == CheckBoxTreeType;
    }

    /// <summary>The tree <paramref name="node"/> is in, or null.</summary>
    public static DesignComponent? TreeOf(DesignComponent node)
    {
        ArgumentNullException.ThrowIfNull(node);
        DesignComponent? up = node.Parent;
        while (up is { Type: TreeNodeType })
        {
            up = up.Parent;
        }

        return up is { Type: TreeType or CheckBoxTreeType } ? up : null;
    }

    /// <summary>Opens or closes <paramref name="node"/> on the canvas, as its twisty does; what is open is the canvas's, not the design's.</summary>
    /// <returns>Whether it has nodes to show or hide.</returns>
    public bool ToggleExpanded(DesignComponent node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Runtime.ToggleExpanded(node))
        {
            return false;
        }

        _boxes = null;
        OnChanged();
        return true;
    }

    // --- what is shown and selected ----------------------------------------------------------------

    /// <summary>
    /// A tab made, put back or moved in is shown, as its controller's <c>createView</c> makes it the
    /// group's <c>SelectedTab</c>; a node, the nodes above it opened (the tree's <c>expandTo</c>).
    /// </summary>
    private void ShowMade(DesignComponent component)
    {
        if (component.Type == TabType)
        {
            Runtime.ShowTab(component);
        }
        else if (component.Type == TreeNodeType)
        {
            Runtime.ExpandTo(component);
        }
    }

    /// <summary>
    /// What a delete takes (<c>DeleteCommand._filterDeletingParentIfLastChild</c>): a tab group's only
    /// tab deleted takes its group with it.
    /// </summary>
    private static IReadOnlyList<DesignComponent> WithLastChildParents(IReadOnlyList<DesignComponent> components) =>
        components.Select(static c => c is { Type: TabType, Parent: { Children.Count: 1, Parent: not null } group } ? group : c).ToArray();

    /// <summary>
    /// What is selected once <paramref name="components"/> are deleted (<c>_checkForSelectAfterDelete</c>):
    /// for a tab or a node first among them, the sibling before the first of them, else the first
    /// sibling left, else their container (<c>_getClosestComponent</c>); null for any other, which
    /// leaves nothing selected.
    /// </summary>
    private static DesignComponent? SelectedAfterDelete(IReadOnlyList<DesignComponent> components)
    {
        if (components is not [{ Type: TabType or TreeNodeType, Parent: { } parent }, ..])
        {
            return null;
        }

        List<DesignComponent> siblings = [.. parent.Children];
        int first = components.Min(c => siblings.IndexOf(c));
        if (first > 0)
        {
            return siblings[first - 1];
        }

        return siblings.FirstOrDefault(s => !components.Contains(s, ReferenceEqualityComparer.Instance)) ?? parent;
    }
}
