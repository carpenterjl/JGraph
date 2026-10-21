using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 150 (the designer leftovers plan, stage G1), on the canvas: App Designer's selection
/// rules over groups, a group moved as one, a labelled component resized by its sides with the
/// label following, members deleted, the four grouping actions, a labelled component made by the
/// library, and a group member moved by whole cells in its grid (DS4's oracle of
/// <c>CellLocationValidator</c>).
/// </summary>
[Collection("JG facade")]
public class AppDesignerGroupCanvasTests
{
    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static Point2D Centre(Rect2D r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        return component;
    }

    /// <summary>A 640-by-480 figure holding five buttons in a row and a panel with two more.</summary>
    private static DesignDocument App()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        for (int i = 1; i <= 5; i++)
        {
            figure.Add(Make("matlab.ui.control.Button", i == 1 ? "Button" : $"Button{i}", 20 + ((i - 1) * 110), 400, 100, 23));
        }

        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 20, 100, 300, 200);
        figure.Add(panel);
        panel.Add(Make("matlab.ui.control.Button", "Inner", 20, 20, 100, 23));
        panel.Add(Make("matlab.ui.control.Button", "Inner2", 150, 20, 100, 23));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static string[] Picks(DesignDocument document) => document.Picks.Select(static p => p.ToString()).ToArray();

    private static void Click(DesignGestures gestures, Point2D at, DesignModifiers modifiers = DesignModifiers.None)
    {
        gestures.Press(at, DesignButton.Left, modifiers);
        gestures.Release();
    }

    // --- selection ---------------------------------------------------------------------------------

    /// <summary>
    /// A press on a member selects its group; one on a member of the selected group selects the
    /// member alone on release (so that a drag moves the group); one on another member while one
    /// is selected alone selects that member alone; a press on a group's own area selects it.
    /// </summary>
    [Fact]
    public void APressOnAMemberSelectsItsGroup_ThenTheMemberOnASecondClick()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b2 = C(document, "Button2");
        DesignGroup group = document.Design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2)], "g");
        var gestures = new DesignGestures(document);
        Point2D on1 = Centre(document.BoxOf(b1)!.Value.Box);
        Point2D on2 = Centre(document.BoxOf(b2)!.Value.Box);

        gestures.Press(on1, DesignButton.Left, DesignModifiers.None);
        Assert.Equal(["group g"], Picks(document));
        Assert.Equal([b1, b2], document.Selection);
        gestures.Release();
        Assert.Equal(["group g"], Picks(document));

        // The group is selected: a press keeps it (a drag would move the group); the release selects the member.
        gestures.Press(on1, DesignButton.Left, DesignModifiers.None);
        Assert.Equal(["group g"], Picks(document));
        gestures.Release();
        Assert.Equal(["Button"], Picks(document));

        // Another member while one is selected alone: that member alone, at once.
        gestures.Press(on2, DesignButton.Left, DesignModifiers.None);
        Assert.Equal(["Button2"], Picks(document));
        gestures.Release();

        // The group's own area, between its members, selects the group; its box is the members' union.
        Rect2D gb = document.BoxOfGroup(group)!.Value;
        Rect2D box1 = document.BoxOf(b1)!.Value.Box;
        Rect2D box2 = document.BoxOf(b2)!.Value.Box;
        Assert.Equal(new Rect2D(box1.X, box1.Y, box2.Right - box1.X, box1.Height), gb);
        var gap = new Point2D((box1.Right + box2.X) / 2, gb.Y + 10);
        Click(gestures, gap);
        Assert.Equal(["group g"], Picks(document));
        Assert.Equal(DesignCursor.Move, gestures.CursorAt(gap));

        // A press on a component in no group selects it alone, as before.
        Click(gestures, Centre(document.BoxOf(C(document, "Button3"))!.Value.Box));
        Assert.Equal(["Button3"], Picks(document));
    }

    /// <summary>Alt drills up a chain of groups; Ctrl adds by App Designer's rules.</summary>
    [Fact]
    public void AltClickDrillsUp_AndCtrlClickAddsByTheRules()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b2 = C(document, "Button2");
        DesignComponent b3 = C(document, "Button3");
        DesignComponent b4 = C(document, "Button4");
        DesignGroup inner = document.Design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2)], "inner");
        DesignGroup outer = document.Design.AddGroup([DesignGroupMember.Of(inner), DesignGroupMember.Of(b3)], "outer");
        var gestures = new DesignGestures(document);
        Point2D on1 = Centre(document.BoxOf(b1)!.Value.Box);

        // With the figure selected, a plain press on b1 selects the group it is in, the inner one.
        Click(gestures, on1);
        Assert.Equal(["group inner"], Picks(document));

        // Alt on a member of the selected inner group: past it, to the outer.
        Click(gestures, on1, DesignModifiers.Alt);
        Assert.Equal(["group outer"], Picks(document));

        // Alt again with the outermost selected: it stays.
        Click(gestures, on1, DesignModifiers.Alt);
        Assert.Equal(["group outer"], Picks(document));

        // From the member alone with nothing else: Alt selects its group.
        document.SelectPicks([DesignPick.Of(b1)]);
        Click(gestures, on1, DesignModifiers.Alt);
        Assert.Equal(["group inner"], Picks(document));

        // Ctrl on b3 (outer's member) with inner selected: the chain holds a selection, no ancestor of b3's group is selected - added.
        Click(gestures, Centre(document.BoxOf(b3)!.Value.Box), DesignModifiers.Control);
        Assert.Equal(["group inner", "Button3"], Picks(document));

        // Ctrl on b1 (a member of the selected inner): b1 takes the place of its selected ancestor.
        Click(gestures, on1, DesignModifiers.Control);
        Assert.Equal(["Button3", "Button"], Picks(document));

        // Ctrl on a component of another container: it alone.
        Click(gestures, Centre(document.BoxOf(C(document, "Inner"))!.Value.Box), DesignModifiers.Control);
        Assert.Equal(["Inner"], Picks(document));

        // Ctrl on a member with nothing selected but the figure: its group.
        document.Select([]);
        Click(gestures, Centre(document.BoxOf(b4)!.Value.Box), DesignModifiers.Control);
        Assert.Equal(["Button4"], Picks(document));
        // Ctrl on a member whose chain holds no selection: the member is added.
        Click(gestures, on1, DesignModifiers.Control);
        Assert.Equal(["Button4", "Button"], Picks(document));
    }

    // --- moving and resizing -------------------------------------------------------------------------

    /// <summary>A group selected whole moves as one; undone, both go back; the group's box is their union.</summary>
    [Fact]
    public void AGroupMovesAsOne()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b2 = C(document, "Button2");
        DesignGroup group = document.Design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2)], "g");
        var gestures = new DesignGestures(document) { ShowAlignmentHints = false };
        Point2D on1 = Centre(document.BoxOf(b1)!.Value.Box);
        Rect2D was1 = document.BoxOf(b1)!.Value.Box;
        Rect2D was2 = document.BoxOf(b2)!.Value.Box;
        gestures.Press(on1, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(on1.X + 30, on1.Y + 40), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(new Rect2D(was1.X + 30, was1.Y + 40, was1.Width, was1.Height), document.BoxOf(b1)!.Value.Box);
        Assert.Equal(new Rect2D(was2.X + 30, was2.Y + 40, was2.Width, was2.Height), document.BoxOf(b2)!.Value.Box);
        Assert.Equal(["group g"], Picks(document));
        Assert.Same(group, document.Design.GroupOf(b1));
        document.Undo();
        Assert.Equal(was1, document.BoxOf(b1)!.Value.Box);
        Assert.Equal(was2, document.BoxOf(b2)!.Value.Box);
        Assert.Equal(["group g"], Picks(document));
    }

    /// <summary>
    /// A labelled edit field from the library is a labelled component, selected whole, with the left
    /// and right handles only; its right edge dragged grows the field and leaves the label, which
    /// stands before it; its left edge dragged moves the label with it.
    /// </summary>
    [Fact]
    public void ALabelledComponentResizesByItsSides_TheLabelFollowing()
    {
        using DesignDocument document = App();
        IReadOnlyList<DesignComponent> made = document.Add(Entry("Edit Field (Text)"), document.Design.Figure, new Point2D(300, 200));
        DesignComponent label = made.Single(static c => c.Type == "matlab.ui.control.Label");
        DesignComponent field = made.Single(static c => c.Type == "matlab.ui.control.EditField");
        DesignGroup pair = document.Design.LabeledGroupOf(field)!;
        Assert.True(pair.IsLabeled);
        Assert.Same(label, pair.Label);
        Assert.Equal([$"group {pair.Id}"], Picks(document));
        Assert.Same(pair, document.SelectedLabeled);

        var gestures = new DesignGestures(document) { ShowAlignmentHints = false };
        Assert.Equal([ResizeEdges.West, ResizeEdges.East], gestures.Handles().Select(static h => h.Edges));
        Rect2D fieldWas = document.BoxOf(field)!.Value.Box;
        Rect2D labelWas = document.BoxOf(label)!.Value.Box;

        DesignHandle east = gestures.Handles().Single(static h => h.Edges == ResizeEdges.East);
        Point2D at = Centre(east.Box);
        gestures.Press(at, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(at.X + 50, at.Y), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(new Rect2D(fieldWas.X, fieldWas.Y, fieldWas.Width + 50, fieldWas.Height), document.BoxOf(field)!.Value.Box);
        Assert.Equal(labelWas, document.BoxOf(label)!.Value.Box);
        Assert.Equal([$"group {pair.Id}"], Picks(document));

        DesignHandle west = gestures.Handles().Single(static h => h.Edges == ResizeEdges.West);
        at = Centre(west.Box);
        gestures.Press(at, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(at.X - 20, at.Y), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(new Rect2D(fieldWas.X - 20, fieldWas.Y, fieldWas.Width + 70, fieldWas.Height), document.BoxOf(field)!.Value.Box);
        Assert.Equal(new Rect2D(labelWas.X - 20, labelWas.Y, labelWas.Width, labelWas.Height), document.BoxOf(label)!.Value.Box);

        // Undone twice: both back; the pair is still a group.
        document.Undo();
        document.Undo();
        Assert.Equal(fieldWas, document.BoxOf(field)!.Value.Box);
        Assert.Equal(labelWas, document.BoxOf(label)!.Value.Box);
        Assert.Same(pair, document.Design.LabeledGroupOf(label));

        // Undone once more, the pair and its group are gone; redone, back and grouped.
        document.Undo();
        Assert.Null(document.Design.Find("EditField"));
        Assert.Empty(document.Design.Groups);
        document.Redo();
        Assert.True(document.Design.LabeledGroupOf(document.Design.Find("EditField")!) is { IsLabeled: true });
    }

    /// <summary>The label's text renames its field and itself through the group, as DS3's pairing did by name.</summary>
    [Fact]
    public void ALabelsTextRenamesTheFieldThroughTheGroup()
    {
        using DesignDocument document = App();
        IReadOnlyList<DesignComponent> made = document.Add(Entry("Edit Field (Text)"), document.Design.Figure, new Point2D(300, 200));
        DesignComponent label = made[0];
        Assert.Same(made[1], document.LabelledBy(label));
        Assert.Null(document.SetProperty(label, "Text", new CharValue("Width")));
        Assert.Equal("WidthEditField", made[1].CodeName);
        Assert.Equal("WidthEditFieldLabel", label.CodeName);
    }

    // --- deleting ------------------------------------------------------------------------------------

    /// <summary>A deleted member leaves its group; a group left with one member goes, that member joining the group around it; undone, all back.</summary>
    [Fact]
    public void DeletingMembersLeavesTheirGroup_AndAGroupOfOneGoes()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b2 = C(document, "Button2");
        DesignComponent b3 = C(document, "Button3");
        DesignComponent b4 = C(document, "Button4");
        DesignGroup inner = document.Design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(b2), DesignGroupMember.Of(b3)], "inner");
        DesignGroup outer = document.Design.AddGroup([DesignGroupMember.Of(inner), DesignGroupMember.Of(b4)], "outer");

        document.Delete([b3]);
        Assert.Equal(["Button", "Button2"], inner.Members.Select(static m => m.ToString()));
        Assert.Null(document.Design.Find("Button3"));

        document.Delete([b2]);
        Assert.DoesNotContain(inner, document.Design.Groups);
        Assert.Equal(["Button4", "Button"], outer.Members.Select(static m => m.ToString()));
        Assert.Same(outer, document.Design.GroupOf(b1));

        document.Undo();
        Assert.Contains(inner, document.Design.Groups);
        Assert.Same(outer, inner.Parent);
        Assert.Equal(["inner", "Button4"], outer.Members.Select(static m => m.ToString()));
        Assert.Equal(["Button", "Button2"], inner.Members.Select(static m => m.ToString()));
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3"], inner.Members.Select(static m => m.ToString()));
        Assert.Same(inner, document.Design.GroupOf(document.Design.Find("Button3")!));
    }

    /// <summary>A selected labelled field deleted takes its label; a selected group deleted takes every member and the group.</summary>
    [Fact]
    public void DeletingALabelledFieldTakesItsLabel_AndAGroupItsMembers()
    {
        using DesignDocument document = App();
        IReadOnlyList<DesignComponent> made = document.Add(Entry("Edit Field (Text)"), document.Design.Figure, new Point2D(300, 200));
        document.SelectPicks([DesignPick.Of(made[1])]);
        document.Delete(document.Selection);
        Assert.Null(document.Design.Find("EditField"));
        Assert.Null(document.Design.Find("EditFieldLabel"));
        Assert.Empty(document.Design.Groups);
        document.Undo();
        Assert.True(document.Design.LabeledGroupOf(document.Design.Find("EditFieldLabel")!) is { IsLabeled: true });

        DesignGroup group = document.Design.AddGroup([DesignGroupMember.Of(C(document, "Button")), DesignGroupMember.Of(C(document, "Button2"))], "g");
        document.SelectPicks([DesignPick.Of(group)]);
        document.Delete(document.Selection);
        Assert.Null(document.Design.Find("Button"));
        Assert.DoesNotContain(group, document.Design.Groups);
        document.Undo();
        Assert.Contains(group, document.Design.Groups);
        Assert.Equal(["Button", "Button2"], group.Members.Select(static m => m.ToString()));
    }

    // --- the grouping actions -----------------------------------------------------------------------

    /// <summary>Group, Add to Group, Remove from Group and Ungroup apply when App Designer's do, each one edit that sets the selection as its command does.</summary>
    [Fact]
    public void TheGroupingActionsApplyAsAppDesignersDo()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b2 = C(document, "Button2");
        DesignComponent b3 = C(document, "Button3");
        DesignComponent b4 = C(document, "Button4");
        var gestures = new DesignGestures(document);

        Assert.False(document.CanGroup); // the figure alone
        document.Select([b1]);
        Assert.False(document.CanGroup);
        document.Select([b1, b2]);
        Assert.True(document.CanGroup);
        Assert.False(document.CanUngroup);
        Assert.False(document.CanAddToGroup);
        Assert.False(document.CanRemoveFromGroup);

        Assert.True(gestures.KeyDown(DesignKey.Group, DesignModifiers.Control));
        DesignGroup group = document.Design.Groups.Single();
        Assert.Equal([$"group {group.Id}"], Picks(document));
        Assert.True(document.CanUngroup);
        Assert.False(document.CanGroup); // one pick

        // The group and two more: Add to Group; Group too (a group at the top is no member).
        document.SelectPicks([DesignPick.Of(group), DesignPick.Of(b3), DesignPick.Of(b4)]);
        Assert.True(document.CanAddToGroup);
        Assert.True(document.CanGroup);
        Assert.True(document.AddToGroup());
        Assert.Equal(["Button", "Button2", "Button3", "Button4"], group.Members.Select(static m => m.ToString()));
        Assert.Equal([$"group {group.Id}"], Picks(document));

        // Two members: Remove from Group leaves two behind, so it applies; three would not.
        document.SelectPicks([DesignPick.Of(b3), DesignPick.Of(b4)]);
        Assert.True(document.CanRemoveFromGroup);
        document.SelectPicks([DesignPick.Of(b2), DesignPick.Of(b3), DesignPick.Of(b4)]);
        Assert.False(document.CanRemoveFromGroup);
        document.SelectPicks([DesignPick.Of(b3), DesignPick.Of(b4)]);
        Assert.True(document.RemoveFromGroup());
        Assert.Equal(["Button", "Button2"], group.Members.Select(static m => m.ToString()));
        Assert.Equal(["Button3", "Button4"], Picks(document));
        Assert.Null(document.Design.GroupOf(b3));

        // Undo puts them back in their places; redo takes them out again.
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3", "Button4"], group.Members.Select(static m => m.ToString()));
        Assert.Equal([$"group {group.Id}"], Picks(document));
        document.Redo();
        Assert.Equal(["Button", "Button2"], group.Members.Select(static m => m.ToString()));

        // Ungroup, by its key: the members selected; undone, the same group again.
        document.SelectPicks([DesignPick.Of(group)]);
        Assert.True(gestures.KeyDown(DesignKey.Ungroup, DesignModifiers.Control | DesignModifiers.Shift));
        Assert.Empty(document.Design.Groups);
        Assert.Equal(["Button", "Button2"], Picks(document));
        document.Undo();
        Assert.Same(group, document.Design.Groups.Single());
        Assert.Equal([$"group {group.Id}"], Picks(document));

        // A member of a group is not grouped again (a grid does not refuse: GroupingAction's grid test never runs in R2025b).
        document.SelectPicks([DesignPick.Of(b1), DesignPick.Of(b3)]);
        Assert.False(document.CanGroup);
        IReadOnlyList<DesignMenuItem> menu = gestures.MenuAt(Centre(document.BoxOf(b3)!.Value.Box));
        Assert.Equal([("Group", false), ("Add to Group", false), ("Remove from Group", false), ("Ungroup", false)],
            menu.Where(static i => i.Label.Contains("group", StringComparison.OrdinalIgnoreCase)).Select(static i => (i.Label, i.Enabled)));
    }

    // --- a group member in a grid -----------------------------------------------------------------------

    /// <summary>
    /// DS4's oracle of <c>CellLocationValidator</c> (three 100-wide columns, three 50-high rows,
    /// spacing 10, padding 10): where a selection holding a group member lands, moved as one.
    /// </summary>
    [Theory]
    [InlineData("a one member at (1,1) to the top-left of (2,2)", 125, 75, 60, 22, 1, 1, 1, 1, true, true, 2, 2)]
    [InlineData("b top 40 h 22: 20 left in the row", 125, 40, 60, 22, 1, 1, 1, 1, true, true, 1, 2)]
    [InlineData("c top 50 h 22: 10 left, under half", 125, 50, 60, 22, 1, 1, 1, 1, true, true, 2, 2)]
    [InlineData("d top 49: 11 left, half", 125, 49, 60, 22, 1, 1, 1, 1, true, true, 1, 2)]
    [InlineData("e two members, 1 by 2, at column 3", 235, 15, 180, 22, 1, 1, 1, 2, true, false, 0, 0)]
    [InlineData("f two members, 1 by 2, at column 2", 125, 15, 180, 22, 1, 1, 1, 2, true, true, 1, 2)]
    [InlineData("g a member spanning rows 1 to 2", 125, 75, 60, 100, 1, 2, 1, 1, true, true, 2, 2)]
    [InlineData("h above the grid by less than half a row", 125, -10, 60, 22, 2, 2, 2, 2, true, true, 1, 2)]
    [InlineData("i above the grid by more than half a row", 125, -20, 60, 22, 2, 2, 2, 2, true, false, 0, 0)]
    [InlineData("j in the padding only", 1, 15, 8, 22, 1, 1, 1, 1, true, false, 0, 0)]
    [InlineData("k outside the grid", 400, 15, 8, 22, 1, 1, 1, 1, false, false, 0, 0)]
    [InlineData("n left 80 w 60: 30 left, half", 80, 15, 60, 22, 1, 1, 1, 1, true, true, 1, 1)]
    [InlineData("o left 81 w 60: 29 left, under half", 81, 15, 60, 22, 1, 1, 1, 1, true, true, 1, 2)]
    [InlineData("p left 81 w 20: the right edge still in the column", 81, 15, 20, 22, 1, 1, 1, 1, true, true, 1, 1)]
    [InlineData("q in the gap between rows, within the 5-pixel window", 64, 64, 10, 10, 1, 1, 1, 1, true, true, 2, 1)]
    public void AGroupMemberMovesByWholeCellsInItsGrid(
        string name, double x, double y, double w, double h, int rowStart, int rowEnd, int columnStart, int columnEnd, bool inContainer, bool canDrop, int row, int column)
    {
        var cells = new GridCells(new Rect2D(0, 0, 340, 190), [10, 10, 10, 10], 10, 10, [(10, 50), (70, 50), (130, 50)], [(10, 100), (120, 100), (230, 100)]);
        DesignGridRules.GroupMoveResult result = DesignGridRules.GroupMove(new Rect2D(x, y, w, h), new GridSpan(rowStart, rowEnd, columnStart, columnEnd), cells);
        Assert.True((inContainer, canDrop, row, column) == (result.InContainer, result.CanDrop, result.Row, result.Column), $"{name}: {result}");
    }

    // --- the review's cases (ADR 0254) --------------------------------------------------------------

    /// <summary>Members deleted out of index order go back in their places on undo, and Remove from Group of picks out of order too.</summary>
    [Fact]
    public void UndoPutsMembersBackInTheirPlaces()
    {
        using DesignDocument document = App();
        DesignComponent[] b = new[] { "Button", "Button2", "Button3", "Button4", "Button5" }.Select(n => C(document, n)).ToArray();
        DesignGroup group = document.Design.AddGroup(b.Select(DesignGroupMember.Of), "g");
        document.Delete([b[1], b[3]]);
        Assert.Equal(["Button", "Button3", "Button5"], group.Members.Select(static m => m.ToString()));
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3", "Button4", "Button5"], group.Members.Select(static m => m.ToString()));

        document.SelectPicks([DesignPick.Of(b[2]), DesignPick.Of(b[0])]);
        Assert.True(document.RemoveFromGroup());
        Assert.Equal(["Button2", "Button4", "Button5"], group.Members.Select(static m => m.ToString()));
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3", "Button4", "Button5"], group.Members.Select(static m => m.ToString()));
    }

    /// <summary>A group selected whole and deleted goes with the group inside it, and comes back whole; a member's delete dissolves its own group only, never the one around.</summary>
    [Fact]
    public void DeletingANestedGroupWholeComesBackWhole_AndADissolveDoesNotClimb()
    {
        using DesignDocument document = App();
        DesignComponent x = C(document, "Button");
        DesignComponent a = C(document, "Button2");
        DesignComponent bb = C(document, "Button3");
        DesignGroup g1 = document.Design.AddGroup([DesignGroupMember.Of(a), DesignGroupMember.Of(bb)], "g1");
        DesignGroup g = document.Design.AddGroup([DesignGroupMember.Of(x), DesignGroupMember.Of(g1)], "g");

        document.SelectPicks([DesignPick.Of(g)]);
        document.Delete(document.Selection);
        Assert.Empty(document.Design.Groups);
        Assert.Null(document.Design.Find("Button2"));
        document.Undo();
        Assert.Equal(["g", "g1"], document.Design.Groups.Select(static gr => gr.Id).Order());
        Assert.Equal(["Button", "g1"], g.Members.Select(static m => m.ToString()));
        Assert.Equal(["Button2", "Button3"], g1.Members.Select(static m => m.ToString()));
        Assert.Same(g, g1.Parent);

        // Both members of g1 deleted on their own: g1 goes; g keeps X as its one member (App Designer does not climb).
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(bb)]);
        document.Delete(document.Selection);
        Assert.Equal(["g"], document.Design.Groups.Select(static gr => gr.Id));
        Assert.Equal(["Button"], g.Members.Select(static m => m.ToString()));
        document.Undo();
        Assert.Equal(["Button", "g1"], g.Members.Select(static m => m.ToString()));
    }

    /// <summary>Two groups taken apart at once select both groups' members; undone, both groups.</summary>
    [Fact]
    public void UngroupingTwoGroupsSelectsEveryMember()
    {
        using DesignDocument document = App();
        DesignGroup g1 = document.Design.AddGroup([DesignGroupMember.Of(C(document, "Button")), DesignGroupMember.Of(C(document, "Button2"))], "g1");
        DesignGroup g2 = document.Design.AddGroup([DesignGroupMember.Of(C(document, "Button3")), DesignGroupMember.Of(C(document, "Button4"))], "g2");
        document.SelectPicks([DesignPick.Of(g1), DesignPick.Of(g2)]);
        Assert.True(document.Ungroup());
        Assert.Equal(["Button", "Button2", "Button3", "Button4"], Picks(document));
        document.Undo();
        Assert.Equal(["group g1", "group g2"], Picks(document));
    }

    /// <summary>A right press on an unselected member selects its group; Ctrl+Shift, Ctrl+Alt and Alt on a component in no group do nothing.</summary>
    [Fact]
    public void ARightPressSelectsAMembersGroup_AndOddModifiersDoNothing()
    {
        using DesignDocument document = App();
        DesignComponent b1 = C(document, "Button");
        DesignComponent b3 = C(document, "Button3");
        document.Design.AddGroup([DesignGroupMember.Of(b1), DesignGroupMember.Of(C(document, "Button2"))], "g");
        var gestures = new DesignGestures(document);
        Assert.False(gestures.Press(Centre(document.BoxOf(b1)!.Value.Box), DesignButton.Right, DesignModifiers.None));
        Assert.Equal(["group g"], Picks(document));

        document.Select([b3]);
        Click(gestures, Centre(document.BoxOf(C(document, "Button4"))!.Value.Box), DesignModifiers.Control | DesignModifiers.Shift);
        Assert.Equal(["Button3"], Picks(document));
        Click(gestures, Centre(document.BoxOf(C(document, "Button4"))!.Value.Box), DesignModifiers.Control | DesignModifiers.Alt);
        Assert.Equal(["Button3"], Picks(document));
        Click(gestures, Centre(document.BoxOf(C(document, "Button4"))!.Value.Box), DesignModifiers.Alt);
        Assert.Equal(["Button3"], Picks(document));
        Click(gestures, Centre(document.BoxOf(b1)!.Value.Box), DesignModifiers.Control | DesignModifiers.Shift);
        Assert.Equal(["Button3"], Picks(document));
    }

    /// <summary>Two children of a grid group, as R2025b's shipped apps hold groups in grids.</summary>
    [Fact]
    public void ComponentsInAGridGroup()
    {
        using DesignDocument document = App();
        DesignComponent panel = C(document, "Panel");
        DesignComponent grid = document.Gridify(panel);
        DesignComponent inner = C(document, "Inner");
        DesignComponent inner2 = C(document, "Inner2");
        Assert.Same(grid, inner.Parent);
        document.SelectPicks([DesignPick.Of(inner), DesignPick.Of(inner2)]);
        Assert.True(document.CanGroup);
        DesignGroup group = document.Group()!;
        Assert.Equal(["Inner", "Inner2"], group.Members.Select(static m => m.ToString()));
        Assert.Same(grid, group.Container);
    }
}
