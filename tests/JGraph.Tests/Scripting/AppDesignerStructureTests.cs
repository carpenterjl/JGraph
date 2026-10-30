using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W5c on an app: Add Tab, the tab strip's +, a tab moved by
/// Move and by its heading's drag, Add Radio Button and Add Toggle Button, a tree's nodes added,
/// indented, moved, selected on the canvas and edited in place, the menus of a tab and a node, what a
/// deleted tab or node leaves, and the stack order kept to one component. The arithmetic is checked
/// against R2025b's own modules by <see cref="Designer.DesignStructureOracleTests"/>.
/// </summary>
[Collection("JG facade")]
public class AppDesignerStructureTests
{
    private const string TreeNode = "matlab.ui.container.TreeNode";

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    /// <summary>A 640-by-480 figure with a button; the tests add what they need from the library.</summary>
    private static DesignDocument App()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        var button = new DesignComponent("Button", DesignCatalog.R2025b.ByType("matlab.ui.control.Button")!);
        button.Set("Position", NumericValue.Row(500, 420, 100, 23));
        figure.Add(button);
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static DesignComponent Add(DesignDocument document, string entry, double x, double y) =>
        document.Add(Entry(entry), document.Design.Figure, new Point2D(x, y))[0];

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static string[] Children(DesignComponent container) => container.Children.Select(static c => c.CodeName).ToArray();

    /// <summary>The order the runtime holds a tab group's tabs or a tree's or node's nodes in.</summary>
    private static string[] RuntimeOrder(DesignDocument document, DesignComponent container)
    {
        IEnumerable<GraphObject> held = document.Runtime.ObjectOf(container) switch
        {
            UiTabGroupModel group => group.Components,
            UiTreeModel tree => tree.Nodes,
            UiTreeNodeModel node => node.Nodes,
            _ => [],
        };
        return held.Select(o => document.Runtime.ComponentOf(o)!.CodeName).ToArray();
    }

    private static string Text(DesignDocument document, DesignComponent component, string property) =>
        DesignPalette.Text(document.ValueOf(component, property)!)!;

    // --- tabs --------------------------------------------------------------------------------------

    [Fact]
    public void AddTabPutsATabAfterTheSelectedOne_OrLast_TitledAmongItsSiblings()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        Assert.Equal(["Tab", "Tab2"], Children(group));

        document.Select([C(document, "Tab")]);
        Assert.True(document.CanAddTab);
        DesignComponent tab = document.AddTab()!;
        Assert.Equal("Tab3", tab.CodeName);
        Assert.Equal("Tab3", Text(document, tab, "Title"));
        Assert.Equal(["Tab", "Tab3", "Tab2"], Children(group));
        Assert.Equal(["Tab", "Tab3", "Tab2"], RuntimeOrder(document, group));

        // It is selected and shown (TabController.createView makes it the group's SelectedTab).
        Assert.Equal([tab], document.Selection);
        Assert.Same(tab, document.Runtime.ShownTabOf(group));

        // The code makes it between the two.
        string code = document.CurrentText;
        Assert.True(code.IndexOf("app.Tab3 = uitab", StringComparison.Ordinal) < code.IndexOf("app.Tab2 = uitab", StringComparison.Ordinal));

        // One edit: undone it goes, redone it is back in its place and shown.
        document.Undo();
        Assert.Equal(["Tab", "Tab2"], Children(group));
        document.Redo();
        Assert.Equal(["Tab", "Tab3", "Tab2"], RuntimeOrder(document, group));
        Assert.Same(tab, document.Runtime.ShownTabOf(group));

        // The group selected: last.
        document.Select([group]);
        Assert.Equal("Tab4", document.AddTab()!.CodeName);
        Assert.Equal(["Tab", "Tab3", "Tab2", "Tab4"], Children(group));

        // Not for two, nor for a button.
        document.Select([C(document, "Tab"), C(document, "Tab2")]);
        Assert.False(document.CanAddTab);
        Assert.Null(document.AddTab());
        document.Select([C(document, "Button")]);
        Assert.False(document.CanAddTab);
    }

    [Fact]
    public void TheTabStripsPlusAddsATabAtItsEnd()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        Rect2D box = document.BoxOf(group)!.Value.Box;
        var gestures = new DesignGestures(document)
        {
            TabHeadingBoxOf = c => c.Parent == group ? new Rect2D(box.X + (group.Children.ToList().IndexOf(c) * 40), box.Y, 40, 23) : null,
        };

        // After the last of two 40-pixel headings.
        Rect2D plus = gestures.TabPlusBox(group)!.Value;
        Assert.Equal(new Rect2D(box.X + 80, box.Y, DesignGestures.TabPlusSize, 23), plus);
        Assert.Contains(gestures.TabPluses(), p => p.Group == group);

        Assert.False(gestures.Press(new Point2D(plus.X + 5, plus.Y + 5), DesignButton.Left, DesignModifiers.None));
        Assert.Equal(["Tab", "Tab2", "Tab3"], Children(group));
        Assert.Equal([C(document, "Tab3")], document.Selection);

        // A right press selects the group, adding nothing.
        plus = gestures.TabPlusBox(group)!.Value;
        gestures.Press(new Point2D(plus.X + 5, plus.Y + 5), DesignButton.Right, DesignModifiers.None);
        Assert.Equal(3, group.Children.Count);
        Assert.Equal([group], document.Selection);
    }

    [Fact]
    public void MoveTakesATabAlongItsStrip_AsOneEdit()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        document.Select([group]);
        document.AddTab();
        document.Select([C(document, "Tab2")]);
        Assert.True(document.MovesInside);
        Assert.True(document.CanMove(DesignMove.First));
        Assert.True(document.CanMove(DesignMove.Next));

        Assert.True(document.Move(DesignMove.First));
        Assert.Equal(["Tab2", "Tab", "Tab3"], Children(group));
        Assert.Equal(["Tab2", "Tab", "Tab3"], RuntimeOrder(document, group));
        Assert.Equal([C(document, "Tab2")], document.Selection);
        Assert.False(document.CanMove(DesignMove.Previous));
        Assert.False(document.Move(DesignMove.First));

        Assert.True(document.Move(DesignMove.Last));
        Assert.Equal(["Tab", "Tab3", "Tab2"], Children(group));
        document.Undo();
        Assert.Equal(["Tab2", "Tab", "Tab3"], RuntimeOrder(document, group));

        // A tab does not restack (TabModel's CanStack), and a button does not move inside.
        Assert.False(document.CanStack(DesignStack.Front));
        document.Select([C(document, "Button")]);
        Assert.False(document.MovesInside);
        Assert.False(document.CanMove(DesignMove.First));
    }

    [Fact]
    public void ATabHeadingsDragMovesItWhereTheMarkerIs()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        document.Select([group]);
        document.AddTab();
        Rect2D box = document.BoxOf(group)!.Value.Box;
        Rect2D Heading(DesignComponent tab) => new(box.X + (group.Children.ToList().IndexOf(tab) * 40), box.Y, 40, 23);
        var gestures = new DesignGestures(document)
        {
            DragThreshold = 4,
            TabHeadingBoxOf = c => c.Parent == group ? Heading(c) : null,
            TabHeadingAt = at => group.Children.FirstOrDefault(t => Heading(t).Contains(at)),
        };

        // Tab dragged from its heading past the third's centre: last.
        Point2D from = new(box.X + 20, box.Y + 10);
        Assert.True(gestures.Press(from, DesignButton.Left, DesignModifiers.None));
        Assert.Equal([C(document, "Tab")], document.Selection);
        gestures.Drag(new Point2D(box.X + 115, box.Y + 12), DesignModifiers.None);
        Assert.Equal((Heading(C(document, "Tab3")), true, true, (int?)2), gestures.TabDrop);
        Assert.NotNull(gestures.TabDragged);
        gestures.Release();
        Assert.Null(gestures.TabDrop);
        Assert.Equal(["Tab2", "Tab3", "Tab"], Children(group));
        Assert.Equal(["Tab2", "Tab3", "Tab"], RuntimeOrder(document, group));

        // Dragged off the strip, it stays.
        gestures.Press(new Point2D(box.X + 100, box.Y + 10), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(box.X + 10, box.Y + 200), DesignModifiers.None);
        Assert.Null(gestures.TabDrop);
        gestures.Release();
        Assert.Equal(["Tab2", "Tab3", "Tab"], Children(group));

        // A click on a heading with Ctrl takes a selected one out.
        gestures.Press(new Point2D(box.X + 100, box.Y + 10), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal([group.Parent!], document.Selection);
    }

    [Fact]
    public void ATabsMenuHasAddTabAndMove_UpAndDownForTabsDownASide()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        var gestures = new DesignGestures(document);
        document.Select([C(document, "Tab")]);
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(new Point2D(0, 0));
        Assert.Contains(items, static i => i.Label == "Add Tab" && i.Enabled);
        DesignMenuItem move = items.Single(static i => i.Label == "Move");
        Assert.Equal(["First", "Left", "Right", "Last"], move.Items!.Select(static i => i.Label));
        Assert.Equal([false, false, true, true], move.Items!.Select(static i => i.Enabled));
        Assert.Equal(["Ctrl+Shift+Home", "Ctrl+Shift+←", "Ctrl+Shift+→", "Ctrl+Shift+End"], move.Items!.Select(static i => i.Shortcut));
        Assert.True(items.ToList().IndexOf(items.Single(static i => i.Label == "Add Tab")) < items.ToList().IndexOf(items.Single(static i => i.Label == "Cut")));

        Assert.Null(document.SetProperty(group, "TabLocation", new CharValue("left")));
        document.Select([C(document, "Tab")]);
        move = gestures.MenuAt(new Point2D(0, 0)).Single(static i => i.Label == "Move");
        Assert.Equal(["First", "Up", "Down", "Last"], move.Items!.Select(static i => i.Label));

        // The group: Add Tab, no Move.
        document.Select([group]);
        items = gestures.MenuAt(new Point2D(0, 0));
        Assert.Contains(items, static i => i.Label == "Add Tab");
        Assert.DoesNotContain(items, static i => i.Label == "Move");
    }

    [Fact]
    public void ADeletedTabLeavesTheOneBeforeSelected_AndTheOnlyTabTakesItsGroup()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Tab Group", 20, 20);
        document.Select([group]);
        document.AddTab();

        document.Select([C(document, "Tab2")]);
        document.Delete(document.Selection);
        Assert.Equal(["Tab", "Tab3"], Children(group));
        Assert.Equal([C(document, "Tab")], document.Selection);
        Assert.Same(C(document, "Tab"), document.Runtime.ShownTabOf(group));

        // The first deleted: the first one left.
        document.Delete(document.Selection);
        Assert.Equal([C(document, "Tab3")], document.Selection);

        // The only one: its group goes with it, and comes back with the undo.
        document.Delete(document.Selection);
        Assert.Null(document.Design.Find("TabGroup"));
        document.Undo();
        Assert.Equal(["Tab3"], Children(C(document, "TabGroup")));
    }

    // --- button groups -----------------------------------------------------------------------------

    [Fact]
    public void AddRadioButtonPutsOneUnderTheLowest_TheGroupStaysSelected()
    {
        using DesignDocument document = App();
        DesignComponent group = Add(document, "Radio Button Group", 20, 20);
        document.Select([group]);
        Assert.Equal("matlab.ui.control.RadioButton", document.AddButtonType);
        Assert.Contains(new DesignGestures(document).MenuAt(new Point2D(0, 0)), static i => i.Label == "Add Radio Button");

        // Under Button3 (11 16 65 22): 1 pixel between, 15 high, then sized to "Button4" keeping its top.
        DesignComponent button = document.AddButton()!;
        Assert.Equal("Button4", button.CodeName);
        Assert.Equal("Button4", Text(document, button, "Text"));
        Assert.Equal([11, -7, 65, 22], ((NumericValue)button.Get("Position")!).Data);
        Assert.Equal(new Rect2D(11, -7, 65, 22), document.Runtime.PositionOf(button));
        Assert.Null(button.Get("Value"));
        Assert.Equal([group], document.Selection);
        Assert.Equal(4, group.Children.Count);
        Assert.Same(button, group.Children[3]);

        // One edit, the group selected after the undo too.
        document.Undo();
        Assert.Equal(3, group.Children.Count);
        Assert.Equal([group], document.Selection);

        // An empty group: 10 below its top, selected.
        document.Delete([.. group.Children]);
        document.Select([group]);
        button = document.AddButton()!;
        double inner = document.Runtime.InnerOnSurface(group).Height;
        Assert.Equal([11, inner - 25 + 15 - 22, 58, 22], ((NumericValue)button.Get("Position")!).Data);
        Assert.Equal("Button", button.CodeName.Replace("_2", string.Empty, StringComparison.Ordinal));
        Assert.True(button.Get("Value") is NumericValue { Data: [1] });
        Assert.Contains("app.Button_2.Value = true;", document.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public void AddToggleButtonOverlapsTheLowestByAPixel_AndAGroupHoldsItsOwnKind()
    {
        using DesignDocument document = App();
        DesignComponent toggles = Add(document, "Toggle Button Group", 20, 20);
        DesignComponent radios = Add(document, "Radio Button Group", 300, 20);
        document.Select([toggles]);
        Assert.Equal("matlab.ui.control.ToggleButton", document.AddButtonType);
        Assert.Contains(new DesignGestures(document).MenuAt(new Point2D(0, 0)), static i => i.Label == "Add Toggle Button");

        // Under the third (11 11 100 22): 22 high, 1 over.
        DesignComponent button = document.AddButton()!;
        Assert.Equal("matlab.ui.control.ToggleButton", button.Type);
        Assert.Equal([11, -10, 100, 22], ((NumericValue)button.Get("Position")!).Data);

        // A radio button belongs in a radio button group, a toggle button in a toggle button group (canMoveToParent).
        DesignComponent radio = radios.Children[0];
        Assert.False(document.CanHold(toggles, radio));
        Assert.False(DesignDocument.CanHoldNew(toggles, "matlab.ui.control.RadioButton"));
        Assert.True(DesignDocument.CanHoldNew(radios, "matlab.ui.control.RadioButton"));
        Assert.False(DesignDocument.CanHoldNew(radios, "matlab.ui.control.ToggleButton"));

        // Neither for two groups, nor for a panel.
        document.Select([toggles, radios]);
        Assert.Null(document.AddButtonType);
        Assert.Null(document.AddButton());
    }

    // --- trees -------------------------------------------------------------------------------------

    /// <summary>A tree from the library: Node holding Node2, Node3 and Node4, shown open.</summary>
    private static (DesignDocument Document, DesignComponent Tree, DesignComponent Node) TreeApp()
    {
        DesignDocument document = App();
        DesignComponent tree = Add(document, "Tree", 20, 20);
        return (document, tree, C(document, "Node"));
    }

    [Fact]
    public void ATreeShowsItsFirstNodesOpen_AndANodeSelectedShowsItself()
    {
        (DesignDocument document, DesignComponent tree, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        Assert.Equal(["Node"], Children(tree));
        Assert.True(document.Runtime.IsExpanded(node));

        // Shut, its nodes come into view again when one is selected.
        Assert.True(document.ToggleExpanded(node));
        Assert.False(document.Runtime.IsExpanded(node));
        document.Select([C(document, "Node3")]);
        Assert.True(document.Runtime.IsExpanded(node));
        Assert.False(document.ToggleExpanded(C(document, "Node3"))); // no nodes in it
    }

    [Fact]
    public void AddSiblingAndAddChild_AsTheTreesActionsAddThem()
    {
        (DesignDocument document, DesignComponent tree, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        document.Select([C(document, "Node2")]);
        Assert.True(document.CanAddTreeNode);

        // A sibling after it, named among its siblings.
        DesignComponent sibling = document.AddTreeNode(child: false)!;
        Assert.Equal("Node5", sibling.CodeName);
        Assert.Equal("Node5", Text(document, sibling, "Text"));
        Assert.Equal(["Node2", "Node5", "Node3", "Node4"], Children(node));
        Assert.Equal(["Node2", "Node5", "Node3", "Node4"], RuntimeOrder(document, node));
        Assert.Equal([sibling], document.Selection);

        // A child first inside it, which opens: named "Node", its code name made unique.
        DesignComponent child = document.AddTreeNode(child: true)!;
        Assert.Equal("Node", Text(document, child, "Text"));
        Assert.Equal("Node_2", child.CodeName);
        Assert.Equal(["Node_2"], Children(sibling));
        Assert.True(document.Runtime.IsExpanded(sibling));

        // The tree selected: last among its nodes.
        document.Select([tree]);
        DesignComponent last = document.AddTreeNode(child: true)!;
        Assert.Equal(["Node", last.CodeName], Children(tree));

        // One edit each.
        document.Undo();
        document.Undo();
        Assert.Equal(["Node2", "Node5", "Node3", "Node4"], Children(node));
        Assert.Empty(sibling.Children);

        // Not for two nodes, nor for a button.
        document.Select([C(document, "Node2"), C(document, "Node3")]);
        Assert.False(document.CanAddTreeNode);
        document.Select([C(document, "Button")]);
        Assert.Null(document.AddTreeNode(child: false));
    }

    [Fact]
    public void EnterAddsASibling_ShiftEnterAChild()
    {
        (DesignDocument document, _, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        var gestures = new DesignGestures(document);
        document.Select([C(document, "Node4")]);
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.None));
        Assert.Equal(["Node2", "Node3", "Node4", "Node5"], Children(node));
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.Shift));
        Assert.Single(C(document, "Node5").Children);

        // Enter on a button does nothing.
        document.Select([C(document, "Button")]);
        Assert.False(gestures.KeyDown(DesignKey.Enter, DesignModifiers.None));
    }

    [Fact]
    public void IndentsMoveANodeIntoTheOneBeforeIt_OrOutAfterItsParent()
    {
        (DesignDocument document, DesignComponent tree, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        DesignComponent node3 = C(document, "Node3");
        document.Select([node3]);
        Assert.True(document.CanIndent(increase: true));
        Assert.True(document.CanIndent(increase: false));

        Assert.True(document.Indent(increase: true));
        Assert.Equal(["Node2", "Node4"], Children(node));
        Assert.Equal(["Node3"], Children(C(document, "Node2")));
        Assert.Equal(["Node3"], RuntimeOrder(document, C(document, "Node2")));
        Assert.True(document.Runtime.IsExpanded(C(document, "Node2")));
        Assert.Equal([node3], document.Selection);

        // Out again: after Node2 in Node.
        Assert.True(document.Indent(increase: false));
        Assert.Equal(["Node2", "Node3", "Node4"], Children(node));
        Assert.Equal(["Node2", "Node3", "Node4"], RuntimeOrder(document, node));

        // Out of Node: after it in the tree.
        Assert.True(document.Indent(increase: false));
        Assert.Equal(["Node", "Node3"], Children(tree));
        Assert.Equal(["Node", "Node3"], RuntimeOrder(document, tree));

        // A first node has none before it; a node in the tree has no node round it.
        Assert.False(document.CanIndent(increase: false));
        document.Select([C(document, "Node2")]);
        Assert.False(document.CanIndent(increase: true));

        document.Undo();
        document.Undo();
        document.Undo();
        Assert.Equal(["Node2", "Node3", "Node4"], Children(node));
        Assert.Equal(["Node2", "Node3", "Node4"], RuntimeOrder(document, node));
    }

    [Fact]
    public void MoveTakesNodesAmongTheirSiblings_KeepingTheGapsBetweenThem()
    {
        (DesignDocument document, _, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        document.Select([C(document, "Node4"), C(document, "Node2")]);
        Assert.True(document.MovesInside);
        Assert.False(document.CanMove(DesignMove.First));
        Assert.False(document.CanMove(DesignMove.Last));

        document.Select([C(document, "Node3")]);
        Assert.True(document.Move(DesignMove.Next));
        Assert.Equal(["Node2", "Node4", "Node3"], Children(node));
        Assert.Equal(["Node2", "Node4", "Node3"], RuntimeOrder(document, node));
        Assert.True(document.Move(DesignMove.First));
        Assert.Equal(["Node3", "Node2", "Node4"], Children(node));

        // The code makes them in that order.
        string code = document.CurrentText;
        Assert.True(code.IndexOf("app.Node3 = uitreenode", StringComparison.Ordinal) < code.IndexOf("app.Node2 = uitreenode", StringComparison.Ordinal));
    }

    [Fact]
    public void ANodesRowSelectsIt_CtrlAddsShiftSpansAndTheTwistyOpensIt()
    {
        (DesignDocument document, DesignComponent tree, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        DesignComponent[] rows = [node, C(document, "Node2"), C(document, "Node3"), C(document, "Node4")];
        var gestures = new DesignGestures(document)
        {
            TreeNodeAt = at => at.Y is >= 100 and < 180 ? (rows[(int)((at.Y - 100) / 20)], at.X < 20) : null,
            TreeNodeBoxOf = c => Array.IndexOf(rows, c) is >= 0 and var i ? new Rect2D(10, 100 + (i * 20), 100, 20) : null,
        };
        Point2D Row(int i, double x = 50) => new(x, 105 + (i * 20));

        Assert.False(gestures.Press(Row(1), DesignButton.Left, DesignModifiers.None));
        Assert.Equal([rows[1]], document.Selection);
        gestures.Press(Row(3), DesignButton.Left, DesignModifiers.Shift);
        Assert.Equal([rows[1], rows[2], rows[3]], document.Selection);
        gestures.Press(Row(2), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal([rows[1], rows[3]], document.Selection);
        gestures.Press(Row(1), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal([rows[3]], document.Selection);

        // The + beside one selected node, level with its row, adds a sibling.
        gestures.Press(Row(2), DesignButton.Left, DesignModifiers.None);
        Rect2D add = gestures.TreeAddButton!.Value;
        Rect2D treeBox = document.BoxOf(tree)!.Value.Box;
        Assert.Equal(treeBox.X + treeBox.Width - DesignGestures.TreeAddSize - 4, add.X);
        Assert.Equal(150 - (DesignGestures.TreeAddSize / 2), add.Y);
        gestures.Press(new Point2D(add.X + 2, add.Y + 2), DesignButton.Left, DesignModifiers.None);
        Assert.Equal(["Node2", "Node3", "Node5", "Node4"], Children(node));

        // The twisty shuts and opens a node.
        gestures.Press(Row(0, x: 10), DesignButton.Left, DesignModifiers.None);
        Assert.False(document.Runtime.IsExpanded(node));
        gestures.Press(Row(0, x: 10), DesignButton.Left, DesignModifiers.None);
        Assert.True(document.Runtime.IsExpanded(node));
    }

    [Fact]
    public void ACheckBoxTreesNodesAreSelectedOneAtATime()
    {
        using DesignDocument document = App();
        DesignComponent tree = Add(document, "Tree (Check Box)", 20, 20);
        DesignComponent[] rows = [tree.Children[0], .. tree.Children[0].Children];
        var gestures = new DesignGestures(document)
        {
            TreeNodeAt = at => at.Y is >= 100 and < 180 ? (rows[(int)((at.Y - 100) / 20)], false) : null,
        };
        gestures.Press(new Point2D(50, 125), DesignButton.Left, DesignModifiers.None);
        gestures.Press(new Point2D(50, 165), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal([rows[3]], document.Selection);
        Assert.True(DesignDocument.IsInCheckBoxTree(rows[3]));
    }

    [Fact]
    public void ANodesMenu_ItsPasteOnlyForNodes()
    {
        (DesignDocument document, _, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        var clipboard = new DesignClipboard();
        DesignComponent node3 = C(document, "Node3");
        var gestures = new DesignGestures(document)
        {
            Clipboard = clipboard,
            TreeNodeAt = at => at.Y < 50 ? (node3, false) : null,
        };

        // A right press on its row selects it and brings the node's menu.
        gestures.Press(new Point2D(30, 30), DesignButton.Right, DesignModifiers.None);
        Assert.Equal([node3], document.Selection);
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(new Point2D(30, 30));
        Assert.Equal(["Add Sibling", "Add Child", "Increase Indent", "Decrease Indent", "Move", "-", "Cut", "Copy", "Paste", "Duplicate", "Delete"], items.Select(static i => i.Label));
        Assert.Equal(["Enter", "Shift+Enter", "Ctrl+]", "Ctrl+["], items.Take(4).Select(static i => i.Shortcut));
        Assert.Equal(["Up", "Down"], items[4].Items!.Skip(1).Take(2).Select(static i => i.Label));
        Assert.False(items.Single(static i => i.Label == "Paste").Enabled);

        // A node copied: Paste.
        Assert.True(document.Copy(clipboard));
        Assert.True(clipboard.HoldsOnly(TreeNode));
        Assert.True(gestures.MenuAt(new Point2D(30, 30)).Single(static i => i.Label == "Paste").Enabled);
        items.Single(static i => i.Label == "Increase Indent").Run!();
        Assert.Equal(["Node3"], Children(C(document, "Node2")));

        // A press elsewhere brings the canvas's menu.
        gestures.Press(new Point2D(600, 400), DesignButton.Right, DesignModifiers.None);
        Assert.DoesNotContain(gestures.MenuAt(new Point2D(600, 400)), static i => i.Label == "Add Sibling");

        // The menu key: a selected node's menu at its row, the canvas's for anything else at its centre.
        document.Select([node3]);
        gestures.TreeNodeBoxOf = c => ReferenceEquals(c, node3) ? new Rect2D(10, 20, 100, 20) : null;
        Assert.Equal("Add Sibling", gestures.KeyboardMenu(out Point2D at)[0].Label);
        Assert.Equal(new Point2D(60, 30), at);
        document.Select([C(document, "Button")]);
        Assert.DoesNotContain(gestures.KeyboardMenu(out at), static i => i.Label == "Add Sibling");
        Assert.Equal(new Point2D(499 + 50, 480 - 420 - 23 + 12.5), at); // Position counts from 1, the surface from 0
    }

    [Fact]
    public void F2AndADoubleClickEditANodesText_TypingDoesNot()
    {
        (DesignDocument document, _, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        DesignComponent node2 = C(document, "Node2");
        var row = new Rect2D(30, 60, 100, 20);
        var gestures = new DesignGestures(document)
        {
            HotspotsOf = c => ReferenceEquals(c, node2) ? [row] : null,
            TreeNodeAt = at => row.Contains(at) ? (node2, false) : null,
        };
        document.Select([node2]);
        Assert.False(gestures.TypeText("x"));
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal((DesignInPlaceKind.SingleLine, "Text", "Node2"), (gestures.InPlace!.Kind, gestures.InPlace.Property, gestures.InPlace.Text));
        Assert.Equal(row, gestures.InPlace.Box);
        Assert.Null(gestures.EndInPlace(save: true, "Apples").Refused);
        Assert.Equal("Apples", Text(document, node2, "Text"));

        document.Select([node]);
        Assert.True(gestures.DoubleClick(new Point2D(40, 65)));
        Assert.Same(node2, gestures.InPlace!.Component);
        Assert.Equal([node2], document.Selection);
        gestures.EndInPlace(save: false, string.Empty);
    }

    [Fact]
    public void ADeletedNodeLeavesTheOneBeforeSelected()
    {
        (DesignDocument document, _, DesignComponent node) = TreeApp();
        using DesignDocument held = document;
        document.Select([C(document, "Node4"), C(document, "Node3")]);
        document.Delete(document.Selection);
        Assert.Equal([C(document, "Node2")], document.Selection);
        document.Delete(document.Selection);
        Assert.Equal([node], document.Selection);
        document.Undo();
        Assert.Equal(["Node2"], Children(node));
        Assert.Equal(["Node2"], RuntimeOrder(document, node));
    }

    // --- the stack order ---------------------------------------------------------------------------

    [Fact]
    public void TheStackOrderMovesOneComponentAlone()
    {
        using DesignDocument document = App();
        Add(document, "Panel", 20, 20);
        document.Select([C(document, "Button")]);
        Assert.True(document.CanStack(DesignStack.Front));

        // R2025b's _checkAbilityToReorder enables the stack order for one selected only.
        document.Select([C(document, "Button"), C(document, "Panel")]);
        Assert.False(document.CanStack(DesignStack.Front));
        Assert.False(document.CanStack(DesignStack.Back));
        Assert.False(document.Stack(DesignStack.Back));
    }

    [Fact]
    public void TheStructureActionsAreLive()
    {
        foreach (string id in new[]
        {
            "APPDES_DESIGN_REORDER_PREVIOUS", "APPDES_DESIGN_REORDER_NEXT", "APPDES_DESIGN_REORDER_FIRST", "APPDES_DESIGN_REORDER_LAST",
            "APPDES_DESIGN_REORDER_OFFSET", "DESIGN_TREE_ADD_SIBLING", "DESIGN_TREE_ADD_CHILD", "DESIGN_TREE_INCREASE_INDENT", "DESIGN_TREE_DECREASE_INDENT",
        })
        {
            Assert.True(DesignActions.IsLive(id), id);
        }

        Assert.False(DesignActions.IsLive("DESIGN_TREE_DRAG_REORDER"));
    }
}
