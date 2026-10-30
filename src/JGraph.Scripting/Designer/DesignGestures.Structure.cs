using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// The canvas's gestures on tab groups and trees (App Designer window plan, stage W5c): a tab heading
/// dragged along its strip (<c>TabDragController</c>), the strip's + (<c>TabStripMouseController</c>),
/// a tree's nodes selected (<c>TreeSelectPlugin</c>) and opened by their twisties, the + beside the
/// selected node (<c>TreeToolbar</c>), and the menus of a tab and a node.
/// </content>
public sealed partial class DesignGestures
{
    private const string TabType = "matlab.ui.container.Tab";
    private const string TabGroupType = "matlab.ui.container.TabGroup";
    private const string TreeNodeType = "matlab.ui.container.TreeNode";

    /// <summary>A tab heading being dragged along its strip: its tab, place, heading and strip, which way the strip runs, and its headings' boxes.</summary>
    private sealed record TabDrag(DesignComponent Tab, int Start, Rect2D Heading, Rect2D Strip, bool Across, IReadOnlyList<Rect2D> Headings);

    /// <summary>
    /// Where a dragged tab heading would go, which the canvas marks (<c>ad-insert-left</c>,
    /// <c>ad-insert-right</c>, <c>ad-insert-above</c>, <c>ad-insert-below</c>): the heading the marker is
    /// at, whether on its far side, whether the strip runs across, and the tab's new place - null when it
    /// stays; null when no heading is dragged, or the drag left the strip.
    /// </summary>
    public (Rect2D Heading, bool After, bool Across, int? To)? TabDrop { get; private set; }

    /// <summary>The dragged heading where the pointer has it (<c>ad-tab-avatar</c>); null when none is dragged.</summary>
    public Rect2D? TabDragged { get; private set; }

    private TabDrag? TabDragOf(DesignComponent tab)
    {
        if (TabHeadingBoxOf is not { } headingOf || tab.Parent is not { } group || Document.BoxOf(group) is not { Shown: true } groupBox)
        {
            return null;
        }

        List<Rect2D> headings = [];
        foreach (DesignComponent sibling in group.Children)
        {
            if (headingOf(sibling) is not { } box)
            {
                return null;
            }

            headings.Add(box);
        }

        int start = group.Children.ToList().IndexOf(tab);
        bool across = TabLocationOf(group) is not ("left" or "right");
        Rect2D first = headings[0];
        Rect2D g = groupBox.Box;

        // The strip the headings stand in (the tab list), across the group's width or down its height.
        Rect2D strip = across ? new Rect2D(g.X, first.Y, g.Width, first.Height) : new Rect2D(first.X, g.Y, first.Width, g.Height);
        return new TabDrag(tab, start, headings[start], strip, across, headings);
    }

    private void DragTab(TabDrag drag, Point2D at, double dx, double dy)
    {
        Rect2D avatar = new(drag.Heading.X + System.Math.Round(dx, MidpointRounding.AwayFromZero), drag.Heading.Y + System.Math.Round(dy, MidpointRounding.AwayFromZero), drag.Heading.Width, drag.Heading.Height);
        TabDragged = avatar;
        if (!avatar.IntersectsWith(drag.Strip))
        {
            TabDrop = null;
            return;
        }

        double[] centres = drag.Headings.Select(h => drag.Across ? h.X + (h.Width / 2) : h.Y + (h.Height / 2)).ToArray();
        (int marker, bool after, int? to) = DesignStructure.TabDrop(centres, drag.Across ? at.X : at.Y, drag.Start);
        TabDrop = (drag.Headings[marker], after, drag.Across, to);
    }

    private void ClearTabDrag()
    {
        _tabDrag = null;
        TabDrop = null;
        TabDragged = null;
    }

    private string TabLocationOf(DesignComponent group) =>
        Document.ValueOf(group, "TabLocation") is CharValue { Text: var location } ? location : "top";

    // --- the tab strip's + -------------------------------------------------------------------------

    /// <summary>
    /// The + the strip of <paramref name="group"/> ends in, which adds a tab at its end (the tab bar's
    /// plus button): after its last heading, or under it for tabs down a side; null when the canvas
    /// does not show the group or cannot say where its headings are.
    /// </summary>
    public Rect2D? TabPlusBox(DesignComponent group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (TabHeadingBoxOf is not { } headingOf || Document.BoxOf(group) is not { Shown: true } shown
            || group.Children.LastOrDefault(static c => c.Type == TabType) is not { } last || headingOf(last) is not { } h)
        {
            return null;
        }

        Rect2D plus = TabLocationOf(group) is "left" or "right"
            ? new Rect2D(h.X, h.Y + h.Height, h.Width, TabPlusSize)
            : new Rect2D(h.X + h.Width, h.Y, TabPlusSize, h.Height);
        return plus.IntersectsWith(shown.Box) ? plus : null;
    }

    /// <summary>Every shown tab group's + (<see cref="TabPlusBox"/>), which the canvas draws.</summary>
    public IEnumerable<(DesignComponent Group, Rect2D Box)> TabPluses()
    {
        foreach (DesignBox box in Document.Boxes)
        {
            if (box.Component.Type == TabGroupType && box.Shown && TabPlusBox(box.Component) is { } plus)
            {
                yield return (box.Component, plus);
            }
        }
    }

    private DesignComponent? TabPlusAt(Point2D at)
    {
        foreach ((DesignComponent group, Rect2D box) in TabPluses())
        {
            if (box.Contains(at))
            {
                return group;
            }
        }

        return null;
    }

    // --- trees -------------------------------------------------------------------------------------

    /// <summary>
    /// The + beside a tree's one selected node, which adds a sibling (<c>TreeToolbar</c>,
    /// <c>_updateTreeToolbarPosition</c>): at the tree's right edge, level with the node's row; at the
    /// top of a selected tree with no nodes; null otherwise, and while a press is held.
    /// </summary>
    public Rect2D? TreeAddButton
    {
        get
        {
            if (_press != PressState.None)
            {
                return null;
            }

            const double inset = 4;
            if (Document.SelectedTreeNode is { } node && DesignDocument.TreeOf(node) is { } tree
                && Document.BoxOf(tree) is { Shown: true } shown && TreeNodeBoxOf?.Invoke(node) is { } row
                && row.Y >= shown.Box.Y && row.Y + row.Height <= shown.Box.Y + shown.Box.Height)
            {
                return new Rect2D(shown.Box.X + shown.Box.Width - TreeAddSize - inset, row.Y + ((row.Height - TreeAddSize) / 2), TreeAddSize, TreeAddSize);
            }

            if (Document.Selection is [{ Type: "matlab.ui.container.Tree" or "matlab.ui.container.CheckBoxTree", Children.Count: 0 } empty]
                && Document.BoxOf(empty) is { Shown: true } box)
            {
                return new Rect2D(box.Box.X + box.Box.Width - TreeAddSize - inset, box.Box.Y + inset, TreeAddSize, TreeAddSize);
            }

            return null;
        }
    }

    /// <summary>
    /// A press on a tree node's row (<c>TreeSelectPlugin._handleMouseDown</c>): its twisty opens or
    /// closes it; a right press selects it, if it is not, for its menu; a plain press selects it alone,
    /// Ctrl adds it to or takes it from the tree's selected nodes and Shift selects the rows from the
    /// last one pressed - one at a time in a check box tree (<c>isSingleSelection</c>). With Ctrl or
    /// Shift while something other than nodes is selected, the tree is pressed instead.
    /// </summary>
    private void PressNode(DesignComponent node, bool twisty, DesignButton button, DesignModifiers modifiers)
    {
        DesignDocument document = Document;
        if (twisty && button == DesignButton.Left)
        {
            document.ToggleExpanded(node);
            return;
        }

        if (button != DesignButton.Left)
        {
            if (!document.Selection.Contains(node))
            {
                document.Select([node]);
            }

            _menuNode = node;
            return;
        }

        DesignComponent? tree = DesignDocument.TreeOf(node);
        bool nodesOnly = document.Selection.All(static c => c.Type == TreeNodeType);
        if (modifiers != DesignModifiers.None && !nodesOnly && tree is not null)
        {
            DesignGroupRules.PressResult result = DesignGroupRules.Press(document.Design, document.Picks, DesignPick.Of(tree), modifiers);
            if ((result.Now ?? result.OnRelease) is { } picks)
            {
                document.SelectPicks(picks);
            }

            return;
        }

        List<DesignComponent> held = document.Selection.Where(c => c.Type == TreeNodeType && ReferenceEquals(DesignDocument.TreeOf(c), tree)).ToList();
        List<DesignComponent> picked;
        if (modifiers == DesignModifiers.None || DesignDocument.IsInCheckBoxTree(node))
        {
            picked = [node];
            _nodeAnchor = node;
        }
        else if (modifiers.HasFlag(DesignModifiers.Control))
        {
            picked = held.Contains(node) ? held.Where(n => !ReferenceEquals(n, node)).ToList() : [.. held, node];
            _nodeAnchor = node;
        }
        else
        {
            List<DesignComponent> rows = tree is null ? [node] : VisibleNodes(tree);
            int from = _nodeAnchor is { } anchor && rows.Contains(anchor) ? rows.IndexOf(anchor) : held.Count > 0 ? rows.IndexOf(held[0]) : rows.IndexOf(node);
            int to = rows.IndexOf(node);
            picked = rows.GetRange(System.Math.Min(from, to), System.Math.Abs(to - from) + 1);
        }

        document.Select(picked);
    }

    /// <summary>A tree's nodes as its rows show them, top to bottom: each node, then its nodes when it stands open.</summary>
    private List<DesignComponent> VisibleNodes(DesignComponent tree)
    {
        var rows = new List<DesignComponent>();
        void Walk(DesignComponent parent)
        {
            foreach (DesignComponent node in parent.Children.Where(static c => c.Type == TreeNodeType))
            {
                rows.Add(node);
                if (Document.Runtime.IsExpanded(node))
                {
                    Walk(node);
                }
            }
        }

        Walk(tree);
        return rows;
    }

    // --- menus -------------------------------------------------------------------------------------

    /// <summary>
    /// The menu the keyboard's menu key brings (the browser's <c>contextmenu</c> event at the focused
    /// canvas, or at a tree's focused node): a selected node's own, else the canvas's for the selection,
    /// at its first component's centre - which <paramref name="at"/> gives, for the menu to open at.
    /// </summary>
    public IReadOnlyList<DesignMenuItem> KeyboardMenu(out Point2D at)
    {
        DesignDocument document = Document;
        DesignComponent? first = document.Selection.FirstOrDefault();
        Rect2D? box = first is null ? null : first.Type == TreeNodeType ? TreeNodeBoxOf?.Invoke(first) : document.BoxOf(first)?.Box;
        at = box is { } b ? new Point2D(b.X + (b.Width / 2), b.Y + (b.Height / 2)) : new Point2D(0, 0);
        _menuNode = document.SelectedTreeNode;
        return MenuAt(at);
    }

    /// <summary>
    /// A tree node's menu (<c>TreeNodeMenuItemsProvider</c> and the delete item): Add Sibling, Add
    /// Child, Increase Indent, Decrease Indent, Move, then Cut, Copy, Paste - when the clipboard holds
    /// nodes only - Duplicate and Delete.
    /// </summary>
    private List<DesignMenuItem> NodeMenu()
    {
        DesignDocument document = Document;
        DesignClipboard? clipboard = Clipboard;
        bool copy = document.CanCopy;
        return
        [
            new("Add Sibling", document.CanAddTreeNode, () => document.AddTreeNode(child: false)) { Shortcut = "Enter" },
            new("Add Child", document.CanAddTreeNode, () => document.AddTreeNode(child: true)) { Shortcut = "Shift+Enter" },
            new("Increase Indent", document.CanIndent(increase: true), () => document.Indent(increase: true)) { Shortcut = "Ctrl+]" },
            new("Decrease Indent", document.CanIndent(increase: false), () => document.Indent(increase: false)) { Shortcut = "Ctrl+[" },
            MoveMenu(across: false),
            DesignMenuItem.Separator,
            new("Cut", copy && clipboard is not null, () => document.Cut(clipboard!)) { Shortcut = "Ctrl+X" },
            new("Copy", copy && clipboard is not null, () => document.Copy(clipboard!)) { Shortcut = "Ctrl+C" },
            new("Paste", clipboard is not null && clipboard.HoldsOnly(TreeNodeType) && document.CanPaste(clipboard), () => document.Paste(clipboard!)) { Shortcut = "Ctrl+V" },
            new("Duplicate", document.CanDuplicate, () => document.Duplicate()) { Shortcut = "Ctrl+D" },
            new("Delete", document.Selection.Any(document.CanDelete), () => document.Delete(document.Selection)) { Shortcut = "Delete" },
        ];
    }

    /// <summary>
    /// The Move submenu of a tab or a node (<c>AddTabMenuItemProvider</c>, <c>TreeNodeMenuItemsProvider</c>):
    /// First, one back, one on and Last, back and on Left and Right along tabs across the top or bottom,
    /// Up and Down otherwise; enabled when any of them is.
    /// </summary>
    private DesignMenuItem MoveMenu(bool across)
    {
        DesignDocument document = Document;
        bool first = document.CanMove(DesignMove.First), previous = document.CanMove(DesignMove.Previous);
        bool next = document.CanMove(DesignMove.Next), last = document.CanMove(DesignMove.Last);
        return new DesignMenuItem("Move", first || previous || next || last, Items:
        [
            new("First", first, () => document.Move(DesignMove.First)) { Shortcut = "Ctrl+Shift+Home" },
            new(across ? "Left" : "Up", previous, () => document.Move(DesignMove.Previous)) { Shortcut = across ? "Ctrl+Shift+←" : "Ctrl+Shift+↑" },
            new(across ? "Right" : "Down", next, () => document.Move(DesignMove.Next)) { Shortcut = across ? "Ctrl+Shift+→" : "Ctrl+Shift+↓" },
            new("Last", last, () => document.Move(DesignMove.Last)) { Shortcut = "Ctrl+Shift+End" },
        ]);
    }

    /// <summary>
    /// Add Tab, and a tab's Move (<c>AddTabMenuItemProvider</c>): with one tab group or one tab selected;
    /// Move only for a tab, along its group's strip.
    /// </summary>
    private void AddTabItems(List<DesignMenuItem> items)
    {
        DesignDocument document = Document;
        if (!document.CanAddTab)
        {
            return;
        }

        if (items.Count > 0)
        {
            items.Add(DesignMenuItem.Separator);
        }

        items.Add(new DesignMenuItem("Add Tab", true, () => document.AddTab()));
        if (document.Selection is [{ Type: TabType, Parent: { } group }])
        {
            items.Add(MoveMenu(across: TabLocationOf(group) is not ("left" or "right")));
        }
    }
}
