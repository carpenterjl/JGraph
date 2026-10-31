using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W5d: a figure's menus, toolbars and context menus built on the
/// canvas - Add Menu Below and to Right, the menu bar's and lists' +s, a tool added by its keys, menu and
/// toolbar's +, the keys that go from one to the next, Move and a drag, Paste to Right, the menus of
/// each, a context menu made, assigned, replaced and taken off from a component's menu, dragged to a
/// component or dropped from the library, opened in the tray to be edited - each one edit that undoes.
/// </summary>
[Collection("JG facade")]
public class AppDesignerBarsTests
{
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

    private static DesignComponent AddEntry(DesignDocument document, string entry) => document.Add(Entry(entry), document.Design.Figure, new Point2D(10, 10))[0];

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static string[] Children(DesignComponent container) => container.Children.Select(static c => c.CodeName).ToArray();

    private static string[] Menus(DesignDocument document) => document.TopLevelMenus.Select(static c => c.CodeName).ToArray();

    private static string[] Selected(DesignDocument document) => document.Selection.Select(static c => c.CodeName).ToArray();

    private static Point2D Centre(Rect2D box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    [Fact]
    public void AMenuBarStandsAboveTheFigureAndDropsOnce()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Assert.Equal(0, gestures.Bars.Height);

        // Dropped from the library: two menus, the first selected, the bar above the figure's content.
        Assert.Equal(2, gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50)).Count);
        Assert.Equal(["Menu", "Menu2"], Menus(document));
        Assert.Equal(DesignBarLayout.MenuBarHeight, gestures.Bars.Height);
        Assert.Equal(-DesignBarLayout.MenuBarHeight, gestures.Bars.PlaceOf(C(document, "Menu"))!.Box.Y);
        Assert.Equal("Menu", gestures.Bars.PlaceOf(C(document, "Menu"))!.Text);

        // The entry is the bar's alone (SingleDropEntryModel).
        Assert.Empty(gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50)));
        Assert.NotNull(gestures.Refusal);
        Assert.Equal(2, document.TopLevelMenus.Count);

        // A press on the bar's menu selects it, and its + below shows; the top-level + stands after the last.
        DesignBarPlace plus = gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.AddTopLevel);
        gestures.Press(Centre(gestures.Bars.PlaceOf(C(document, "Menu2"))!.Box), DesignButton.Left, DesignModifiers.None);
        gestures.Release();
        Assert.Equal(["Menu2"], Selected(document));
        Assert.Contains(gestures.Bars.Places, static p => p.Kind == DesignBarPlaceKind.AddChild && p.Component.CodeName == "Menu2");
        Assert.False(gestures.Press(Centre(plus.Box), DesignButton.Left, DesignModifiers.None));
        Assert.Equal(3, document.TopLevelMenus.Count);
        Assert.Equal(document.TopLevelMenus[2], document.Selection[0]);
        Assert.Equal("Menu3", DesignPalette.Text(document.TopLevelMenus[2].Get("Text")!));
        document.Undo();
        Assert.Equal(["Menu", "Menu2"], Menus(document));
    }

    [Fact]
    public void EnterAndShiftEnterAddMenusWhereR2025bPutsThem()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);

        // Shift+Enter on a top-level menu: to its right.
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.Shift));
        Assert.Equal(menu, document.TopLevelMenus[0]);
        Assert.Equal("Menu3", DesignPalette.Text(document.TopLevelMenus[1].Get("Text")!));
        Assert.Equal("Menu2", DesignPalette.Text(document.TopLevelMenus[2].Get("Text")!));
        document.Select([menu]);

        // Enter on a top-level menu: first under it, named among what is under it.
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.None));
        DesignComponent item = Assert.Single(menu.Children);
        Assert.Equal("Menu", DesignPalette.Text(item.Get("Text")!));
        Assert.Equal(item, document.Selection[0]);

        // Enter on an item: after it.
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.None));
        Assert.Equal(item, menu.Children[0]);
        DesignComponent after = menu.Children[1];
        Assert.Equal("Menu2", DesignPalette.Text(after.Get("Text")!));

        // Shift+Enter on an item with none under it: under it; on one with items: last under it.
        document.Select([item]);
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.Shift));
        DesignComponent under = Assert.Single(item.Children);
        document.Select([item]);
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.Shift));
        Assert.Equal(2, item.Children.Count);
        Assert.Equal(under, item.Children[0]);

        // Each is one edit.
        document.Undo();
        Assert.Single(item.Children);

        // The code holds them in their menus, in order.
        string code = document.CurrentText;
        Assert.True(code.IndexOf($"app.{item.CodeName} = uimenu(app.Menu)", StringComparison.Ordinal) < code.IndexOf($"app.{after.CodeName} = uimenu(app.Menu)", StringComparison.Ordinal));
        Assert.Contains($"app.{under.CodeName} = uimenu(app.{item.CodeName})", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AListsPlusesAddUnderTheMenu()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);

        // The + under an empty list.
        Rect2D add = gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.AddChild).Box;
        Assert.False(gestures.Press(Centre(add), DesignButton.Left, DesignModifiers.None));
        Assert.Single(menu.Children);

        // The + beside the selected item adds under it (ad-menu-add-right).
        DesignComponent item = menu.Children[0];
        Rect2D right = gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.AddRight).Box;
        Assert.Equal(gestures.Bars.PlaceOf(item)!.Box.Right, right.X);
        Assert.False(gestures.Press(Centre(right), DesignButton.Left, DesignModifiers.None));
        Assert.Single(item.Children);

        // An item further down selected: its list's + shows, and the upper one's does not (ad-descendant-selected).
        Assert.Single(gestures.Bars.Places, static p => p.Kind == DesignBarPlaceKind.AddChild);
        Assert.Equal(item, gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.AddChild).Component);
        Assert.True(gestures.Bars.PlaceOf(item)!.Holds);
        Assert.True(gestures.Bars.PlaceOf(menu)!.Holds);
    }

    [Fact]
    public void TheArrowsHomeEndAndEscapeGoFromMenuToMenu()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);
        gestures.KeyDown(DesignKey.Enter, DesignModifiers.None);
        gestures.KeyDown(DesignKey.Enter, DesignModifiers.None);
        document.Select([menu]);
        DesignComponent one = menu.Children[0], two = menu.Children[1];

        Assert.True(gestures.KeyDown(DesignKey.Down, DesignModifiers.None));
        Assert.Equal(one, document.Selection[0]);
        gestures.KeyDown(DesignKey.Down, DesignModifiers.None);
        Assert.Equal(two, document.Selection[0]);
        gestures.KeyDown(DesignKey.Up, DesignModifiers.None);
        gestures.KeyDown(DesignKey.Up, DesignModifiers.None);
        Assert.Equal(["Menu"], Selected(document));
        gestures.KeyDown(DesignKey.Right, DesignModifiers.None);
        Assert.Equal(["Menu2"], Selected(document));
        gestures.KeyDown(DesignKey.Home, DesignModifiers.None);
        Assert.Equal(["Menu"], Selected(document));
        gestures.KeyDown(DesignKey.End, DesignModifiers.None);
        Assert.Equal(["Menu2"], Selected(document));

        // A held Shift takes the key and does nothing, as the plugin stops it.
        Assert.True(gestures.KeyDown(DesignKey.Left, DesignModifiers.Shift));
        Assert.Equal(["Menu2"], Selected(document));

        // Escape goes up to the parent; the figure from the bar.
        document.Select([one]);
        Assert.True(gestures.KeyDown(DesignKey.Escape, DesignModifiers.None));
        Assert.Equal(["Menu"], Selected(document));
        gestures.KeyDown(DesignKey.Escape, DesignModifiers.None);
        Assert.Equal(document.Design.Figure, document.Selection[0]);
    }

    [Fact]
    public void MoveAndADragOrderMenusAmongMenus()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        document.AddMenuAtEnd(document.Design.Figure);
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);
        Assert.True(document.MovesInside);
        Assert.False(document.CanMove(DesignMove.First));
        Assert.True(document.Move(DesignMove.Last));
        Assert.Equal(["Menu2", "Menu3", "Menu"], Menus(document));
        document.Undo();
        Assert.Equal(["Menu", "Menu2", "Menu3"], Menus(document));

        // A drag along the bar: before the first whose centre is past the pointer.
        Rect2D first = gestures.Bars.PlaceOf(menu)!.Box;
        Rect2D third = gestures.Bars.PlaceOf(C(document, "Menu3"))!.Box;
        Assert.True(gestures.Press(Centre(first), DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(third.X + 1, third.Y + 5), DesignModifiers.None);
        Assert.NotNull(gestures.BarDrop);
        Assert.NotNull(gestures.BarDragged);
        gestures.Release();
        Assert.Null(gestures.BarDrop);
        Assert.Equal(["Menu2", "Menu", "Menu3"], Menus(document));

        // Dragged off its row it stays.
        first = gestures.Bars.PlaceOf(menu)!.Box;
        gestures.Press(Centre(first), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(first.X, 200), DesignModifiers.None);
        Assert.Null(gestures.BarDrop);
        gestures.Release();
        Assert.Equal(["Menu2", "Menu", "Menu3"], Menus(document));
    }

    [Fact]
    public void ToolsAreAddedByTheirKeysMenusAndTheToolbarsPlus()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Assert.Single(gestures.Drop(Entry("Toolbar"), new Point2D(50, 50)));
        DesignComponent bar = C(document, "Toolbar");
        Assert.Equal(["PushTool"], Children(bar));
        Assert.Equal(DesignBarLayout.ToolbarHeight, gestures.Bars.Height);

        // The + adds a push tool last; its arrow's menu either.
        DesignBarLayout bars = gestures.Bars;
        Assert.False(gestures.Press(Centre(bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.ToolbarPlus).Box), DesignButton.Left, DesignModifiers.None));
        Assert.Equal(["PushTool", "PushTool2"], Children(bar));
        IReadOnlyList<DesignMenuItem>? arrow = null;
        gestures.MenuOpened += (_, m) => arrow = m.Items;
        gestures.Press(Centre(gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.ToolbarArrow).Box), DesignButton.Left, DesignModifiers.None);
        Assert.Equal(["Add Push Tool", "Add Toggle Tool"], arrow!.Select(static i => i.Label));
        arrow![1].Run!();
        Assert.Equal(["PushTool", "PushTool2", "ToggleTool"], Children(bar));

        // P and T: after a selected tool; last in a selected toolbar.
        document.Select([C(document, "PushTool")]);
        Assert.True(gestures.KeyDown(DesignKey.AddPushTool, DesignModifiers.None));
        Assert.Equal(["PushTool", "PushTool3", "PushTool2", "ToggleTool"], Children(bar));
        document.Select([bar]);
        Assert.True(gestures.KeyDown(DesignKey.AddToggleTool, DesignModifiers.Shift));
        Assert.Equal("ToggleTool2", Children(bar)[^1]);

        // A button takes neither.
        document.Select([C(document, "Button")]);
        Assert.False(gestures.KeyDown(DesignKey.AddPushTool, DesignModifiers.None));
        Assert.Equal(5, bar.Children.Count);

        // A tool's menu and a toolbar's.
        Rect2D tool = gestures.Bars.PlaceOf(C(document, "PushTool"))!.Box;
        Assert.False(gestures.Press(Centre(tool), DesignButton.Right, DesignModifiers.None));
        string[] labels = gestures.MenuAt(new Point2D(0, 0)).Select(static i => i.Label).ToArray();
        Assert.Equal(["Add Push Tool", "Add Toggle Tool", "Move", "-", "Cut", "Copy", "Paste", "Duplicate", "Delete", "-", "Replace With"], labels);
        Assert.Equal(["First", "Left", "Right", "Last"], gestures.MenuAt(new Point2D(0, 0))[2].Items!.Select(static i => i.Label));
        gestures.Press(new Point2D(600, gestures.Bars.PlaceOf(bar)!.Box.Y + 3), DesignButton.Right, DesignModifiers.None);
        Assert.Equal(["bar"], new[] { document.Selection[0].CodeName.ToLowerInvariant().Replace("toolbar", "bar", StringComparison.Ordinal) });
        Assert.Equal(["First", "Up", "Down", "Last"], gestures.MenuAt(new Point2D(0, 0))[2].Items!.Select(static i => i.Label));
    }

    [Fact]
    public void AToolDraggedGoesAlongItsToolbarOrIntoAnother()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Toolbar"), new Point2D(50, -100));
        DesignComponent one = C(document, "Toolbar");
        document.Select([one]);
        document.AddTool(toggle: false, atEnd: true);
        document.AddTool(toggle: false, atEnd: true);
        Assert.Equal(["PushTool", "PushTool2", "PushTool3"], Children(one));

        // A second toolbar, dropped under the first, goes after it, before the components.
        gestures.Drop(Entry("Toolbar"), new Point2D(50, -1));
        DesignComponent two = C(document, "Toolbar2");
        Assert.Equal(["Toolbar", "Toolbar2"], document.Toolbars.Select(static t => t.CodeName));
        Assert.Equal(["PushTool4"], Children(two));

        // Along its toolbar: over the last tool's left half, it goes before it.
        DesignComponent first = C(document, "PushTool");
        Rect2D start = gestures.Bars.PlaceOf(first)!.Box;
        Rect2D last = gestures.Bars.PlaceOf(C(document, "PushTool3"))!.Box;
        Assert.True(gestures.Press(Centre(start), DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(last.X + 1, last.Y + 5), DesignModifiers.None);
        Assert.NotNull(gestures.BarDrop);
        gestures.Release();
        Assert.Equal(["PushTool2", "PushTool", "PushTool3"], Children(one));
        document.Undo();
        Assert.Equal(["PushTool", "PushTool2", "PushTool3"], Children(one));

        // Into the other toolbar, as one edit.
        start = gestures.Bars.PlaceOf(first)!.Box;
        Rect2D other = gestures.Bars.PlaceOf(C(document, "PushTool4"))!.Box;
        gestures.Press(Centre(start), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(other.X + 1, other.Y + 5), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(["PushTool", "PushTool4"], Children(two));
        Assert.Equal(["PushTool2", "PushTool3"], Children(one));
        document.Undo();
        Assert.Equal(["PushTool4"], Children(two));
        Assert.Equal(first, one.Children[0]);

        // A toolbar goes up and down among the toolbars, by a drag and by Move.
        document.Select([two]);
        Assert.True(document.Move(DesignMove.First));
        Assert.Equal(["Toolbar2", "Toolbar"], document.Toolbars.Select(static t => t.CodeName));
        Rect2D twoRow = gestures.Bars.PlaceOf(two)!.Box;
        gestures.Press(new Point2D(600, twoRow.Y + 5), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(600, twoRow.Y + DesignBarLayout.ToolbarHeight + 20), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(["Toolbar", "Toolbar2"], document.Toolbars.Select(static t => t.CodeName));
    }

    [Fact]
    public void AMenusTextIsEditedInPlaceAndAToolsIconChosen()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);

        // F2 opens a line on its place in the bar, saved as its text.
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal(DesignInPlaceKind.SingleLine, gestures.InPlace!.Kind);
        Assert.Equal("Text", gestures.InPlace!.Property);
        Assert.Equal(gestures.Bars.PlaceOf(menu)!.Box, gestures.InPlace!.Box);
        gestures.EndInPlace(save: true, "&File");
        Assert.Equal("&File", DesignPalette.Text(menu.Get("Text")!));
        Assert.Equal("File", gestures.Bars.PlaceOf(menu)!.Text); // its access key's ampersand not shown

        // A typed character opens it too; a double click on it.
        Assert.True(gestures.TypeText("Z"));
        gestures.EndInPlace(save: false, string.Empty);
        Assert.True(gestures.DoubleClick(Centre(gestures.Bars.PlaceOf(menu)!.Box)));
        gestures.EndInPlace(save: false, string.Empty);

        // Down from a menu with nothing under it adds below it; Up goes back.
        gestures.EditorArrow(menu, down: true);
        Assert.Single(menu.Children);
        DesignComponent item = menu.Children[0];
        document.Select([item]);
        gestures.EditorArrow(item, down: true);
        Assert.Equal(2, menu.Children.Count);
        gestures.EditorArrow(menu.Children[1], down: false);
        Assert.Equal(item, document.Selection[0]);

        // A list's row edits its text beside the check mark's place.
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Rect2D row = gestures.Bars.PlaceOf(item)!.Box;
        Assert.True(gestures.InPlace!.Box.X > row.X && gestures.InPlace!.Box.Right == row.Right);
        gestures.EndInPlace(save: false, string.Empty);

        // A tool's opens its icon's file; P and T typed do not.
        DesignComponent tool = AddEntry(document, "Toolbar").Children[0];
        document.Select([tool]);
        Assert.False(gestures.TypeText("p"));
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal(DesignInPlaceKind.FileBrowser, gestures.InPlace!.Kind);
        Assert.Equal("Icon", gestures.InPlace!.Property);
        gestures.ChooseImage(null);
    }

    [Fact]
    public void PasteToRightAndTheMenuBarsOwnMenu()
    {
        DesignDocument document = App();
        var clipboard = new DesignClipboard();
        var gestures = new DesignGestures(document) { Clipboard = clipboard };
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");

        // A menu's own menu.
        gestures.Press(Centre(gestures.Bars.PlaceOf(menu)!.Box), DesignButton.Right, DesignModifiers.None);
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(new Point2D(0, 0));
        Assert.Equal(["Add Menu Item", "Add Menu", "Move", "-", "Cut", "Copy", "Paste", "Paste to Right", "Duplicate", "Delete"], items.Select(static i => i.Label));
        Assert.Equal(["First", "Left", "Right", "Last"], items[2].Items!.Select(static i => i.Label));
        Assert.False(items.Single(static i => i.Label == "Paste to Right").Enabled);
        items.Single(static i => i.Label == "Copy").Run!();

        // Paste to Right: after a top-level menu.
        gestures.Press(Centre(gestures.Bars.PlaceOf(menu)!.Box), DesignButton.Right, DesignModifiers.None);
        items = gestures.MenuAt(new Point2D(0, 0));
        Assert.True(items.Single(static i => i.Label == "Paste to Right").Enabled);
        items.Single(static i => i.Label == "Paste to Right").Run!();
        Assert.Equal(3, document.TopLevelMenus.Count);
        Assert.Equal(menu, document.TopLevelMenus[0]);
        Assert.Equal("Menu2", document.TopLevelMenus[2].CodeName);
        document.Undo();
        Assert.Equal(["Menu", "Menu2"], Menus(document));

        // Paste to Right on an item: last under it, down a level.
        document.Select([menu]);
        DesignComponent item = document.AddMenu(right: false)!;
        Assert.True(document.PasteRight(clipboard, item));
        Assert.Single(item.Children);

        // The bar away from its menus has its own: Add Top-Level Menu, Select All Menus, Copy, Paste.
        Rect2D bar = gestures.Bars.Places.Single(static p => p.Kind == DesignBarPlaceKind.MenuBar).Box;
        gestures.Press(new Point2D(bar.Right - 5, bar.Y + 5), DesignButton.Right, DesignModifiers.None);
        items = gestures.MenuAt(new Point2D(0, 0));
        Assert.Equal(["Add Top-Level Menu", "-", "Select All Menus", "Copy", "Paste"], items.Select(static i => i.Label));
        items.Single(static i => i.Label == "Select All Menus").Run!();
        Assert.Equal(["Menu", "Menu2"], Selected(document));
        items.Single(static i => i.Label == "Paste").Run!();
        Assert.Equal(3, document.TopLevelMenus.Count);

        // With several selected no list shows (ad-multiselected-menu).
        document.SelectAllMenus();
        Assert.DoesNotContain(gestures.Bars.Places, static p => p.Kind == DesignBarPlaceKind.AddChild);

        // Ctrl adds a sibling of its kind, and takes one out.
        document.Select([menu]);
        gestures.Press(Centre(gestures.Bars.PlaceOf(C(document, "Menu2"))!.Box), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal(["Menu", "Menu2"], Selected(document));
        gestures.Press(Centre(gestures.Bars.PlaceOf(C(document, "Menu2"))!.Box), DesignButton.Left, DesignModifiers.Control);
        Assert.Equal(["Menu"], Selected(document));
    }

    [Fact]
    public void AComponentsContextMenuSubmenuAddsAssignsReplacesAndTakesOff()
    {
        DesignDocument document = App();
        var clipboard = new DesignClipboard();
        var gestures = new DesignGestures(document) { Clipboard = clipboard };
        DesignComponent button = C(document, "Button");
        document.Select([button]);
        DesignMenuItem Submenu() => gestures.MenuAt(new Point2D(0, 0)).Single(static i => i.Label == "Context Menu");
        DesignMenuItem Item(string label) => Submenu().Items!.Single(i => i.Label == label);

        // Nothing there yet: Add New only.
        Assert.Equal(["Add New Context Menu"], Submenu().Items!.Select(static i => i.Label));
        Item("Add New Context Menu").Run!();
        DesignComponent first = C(document, "ContextMenu");
        Assert.Equal(first, document.ContextMenuOf(button));
        Assert.Equal(first, document.Selection[0]);
        Assert.Equal(["Menu", "Menu2"], Children(first));
        Assert.True(gestures.TrayOpen);
        Assert.True(gestures.TrayShown);
        Assert.Contains("app.Button.ContextMenu = app.ContextMenu", document.CurrentText, StringComparison.Ordinal);

        // One edit, the tray going with it.
        document.Undo();
        Assert.Null(document.ContextMenuOf(button));
        Assert.Empty(document.ContextMenus);
        Assert.False(gestures.TrayShown);
        document.Redo();
        first = C(document, "ContextMenu");
        Assert.Equal(first, document.ContextMenuOf(button));

        // With one assigned: Go to it, Unassign; a second gives Replace With.
        document.Select([button]);
        Assert.Equal(["Go to app.ContextMenu", "Unassign Context Menu"], Submenu().Items!.Select(static i => i.Label));
        DesignComponent second = document.AddContextMenu();
        Assert.Equal("ContextMenu2", second.CodeName);
        document.Select([button]);
        Assert.Equal(["Go to app.ContextMenu", "Replace With", "Unassign Context Menu"], Submenu().Items!.Select(static i => i.Label));
        Item("Replace With").Items!.Single().Run!();
        Assert.Equal(second, document.ContextMenuOf(button));
        Assert.Equal(button, document.Selection[0]); // an assignment leaves the selection
        document.Undo();
        Assert.Equal(first, document.ContextMenuOf(button));

        Item("Go to app.ContextMenu").Run!();
        Assert.Equal(first, document.Selection[0]);
        document.Select([button]);
        Item("Unassign Context Menu").Run!();
        Assert.Null(document.ContextMenuOf(button));
        Assert.Equal(button, document.Selection[0]);
        Assert.DoesNotContain("app.Button.ContextMenu", document.CurrentText, StringComparison.Ordinal);

        // With none assigned and some there: Assign lists them.
        Assert.Equal(["Add New Context Menu", "Assign"], Submenu().Items!.Select(static i => i.Label));
        Assert.Equal(["app.ContextMenu", "app.ContextMenu2"], Item("Assign").Items!.Select(static i => i.Label));

        // The figure takes one too; a menu has no such item.
        document.Select([document.Design.Figure]);
        Assert.Equal("Context Menu", Submenu().Label);
        AddEntry(document, "Menu Bar");
        document.Select([document.TopLevelMenus[0]]);
        Assert.DoesNotContain(gestures.MenuAt(new Point2D(0, 0)), static i => i.Label == "Context Menu");
    }

    [Fact]
    public void AContextMenuDraggedFromTheTrayOrDroppedFromTheLibraryAssigns()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent button = C(document, "Button");
        Rect2D at = document.BoxOf(button)!.Value.Box;

        // From the library: on a component, made and assigned; on the figure's own area the figure's; outside refused.
        Assert.Equal("Figure's", document.AssignTargetAt(new Point2D(5, 5))!.Parent is null ? "Figure's" : "other");
        gestures.DragOver(Entry("Context Menu"), Centre(at));
        Assert.Equal(button, gestures.AssignDrag!.Target);
        Assert.Equal("Create and Assign Context Menu to app.Button", gestures.AssignDrag!.Text);
        Assert.Equal(DesignAssignHint.Valid, gestures.AssignDrag!.Hint);
        gestures.DragLeave();
        Assert.Null(gestures.AssignDrag);

        Assert.Single(gestures.Drop(Entry("Context Menu"), Centre(at)));
        DesignComponent first = C(document, "ContextMenu");
        Assert.Equal(first, document.ContextMenuOf(button));
        Assert.Empty(gestures.Drop(Entry("Context Menu"), new Point2D(5000, 5000)));
        Assert.Equal("Drag to component or to Context Menus area", gestures.Refusal);
        Assert.Single(document.ContextMenus);

        // Over the tray, made and assigned to nothing.
        Assert.True(gestures.DragOverTray(Entry("Context Menu")));
        Assert.Equal("Create New Context Menu", gestures.AssignDrag!.Text);
        Assert.False(gestures.DragOverTray(Entry("Button")));
        Assert.Single(gestures.DropOnTray(Entry("Context Menu")));
        DesignComponent second = C(document, "ContextMenu2");
        Assert.Empty(document.AssignmentsOf(second));

        // Its entry in the tray: its name, how many it is assigned to, its items.
        IReadOnlyList<DesignBarPlace> entries = gestures.TrayPlaces();
        Assert.Equal(2, entries.Count);
        Assert.Equal("app.ContextMenu", entries[0].Text);
        Assert.Equal(1, entries[0].Assignments);
        Assert.Equal(["Menu", "Menu2"], entries[0].Items);
        Assert.Equal(0, entries[1].Assignments);
        Assert.True(entries[1].Box.X > entries[0].Box.Right);

        // Dragged from the tray: says what it would do, and a drop assigns it there.
        Assert.True(gestures.PressTray(Centre(entries[1].Box), DesignButton.Left, DesignModifiers.None));
        Assert.Equal(second, document.Selection[0]);
        gestures.DragTray(new Point2D(Centre(entries[1].Box).X, 0), new Point2D(5, 5), overTray: false, DesignModifiers.None);
        Assert.Equal("Assign 'app.ContextMenu2' to app.UIFigure", gestures.AssignDrag!.Text);
        gestures.DragTray(new Point2D(Centre(entries[1].Box).X, 0), Centre(at), overTray: false, DesignModifiers.None);
        Assert.Equal("Replace 'app.ContextMenu' with 'app.ContextMenu2'", gestures.AssignDrag!.Text);
        Assert.Equal(DesignAssignHint.Replace, gestures.AssignDrag!.Hint);
        gestures.DragTray(new Point2D(Centre(entries[1].Box).X, 0), Centre(at), overTray: true, DesignModifiers.None);
        Assert.Equal("Drag to component to assign", gestures.AssignDrag!.Text);
        Assert.Equal(DesignAssignHint.Invalid, gestures.AssignDrag!.Hint);
        gestures.DragTray(new Point2D(Centre(entries[1].Box).X, 0), Centre(at), overTray: false, DesignModifiers.None);
        gestures.Release();
        Assert.Null(gestures.AssignDrag);
        Assert.Equal(second, document.ContextMenuOf(button));
        document.Undo();
        Assert.Equal(first, document.ContextMenuOf(button));

        // The one it has says so.
        (string same, DesignAssignHint hint) = document.AssignMessage("ContextMenu", button, fromLibrary: false);
        Assert.Equal("'app.ContextMenu' already assigned to app.Button", same);
        Assert.Equal(DesignAssignHint.Same, hint);
    }

    [Fact]
    public void AContextMenuOpensInTheTrayToBeEdited()
    {
        DesignDocument document = App();
        var clipboard = new DesignClipboard();
        var gestures = new DesignGestures(document) { Clipboard = clipboard };
        DesignComponent button = C(document, "Button");
        DesignComponent context = document.AddContextMenu(button);
        Rect2D entry = gestures.TrayPlaces().Single().Box;

        // Its menu: the clipboard's and Delete, Go to Component, Edit.
        gestures.PressTray(Centre(entry), DesignButton.Right, DesignModifiers.None);
        IReadOnlyList<DesignMenuItem> items = gestures.TrayMenu();
        Assert.Equal(["Cut", "Copy", "Paste", "Duplicate", "Delete", "-", "Go to Component", "Edit app.ContextMenu"], items.Select(static i => i.Label));
        Assert.Equal(["app.Button"], items.Single(static i => i.Label == "Go to Component").Items!.Select(static i => i.Label));

        // The tray's own menu: Paste, for a context menu.
        gestures.PressTray(new Point2D(590, 190), DesignButton.Right, DesignModifiers.None);
        Assert.Equal(["Paste"], gestures.TrayMenu().Select(static i => i.Label));
        Assert.False(gestures.TrayMenu()[0].Enabled);

        // A double click opens it, its first item selected.
        document.Select([context]);
        Assert.True(gestures.DoubleClickTray(Centre(entry)));
        Assert.Equal(context, gestures.TrayEditing);
        Assert.Equal("Editing app.ContextMenu", gestures.TrayGoBack!.Value.Text);
        DesignComponent item = context.Children[0];
        Assert.Equal(item, document.Selection[0]);
        IReadOnlyList<DesignBarPlace> open = gestures.TrayPlaces();
        Assert.Equal(2, open.Count(static p => p.Kind == DesignBarPlaceKind.Menu));

        // Its items take the menus' keys, +s and drags, in the tray.
        Assert.True(gestures.KeyDown(DesignKey.Enter, DesignModifiers.None));
        Assert.Equal(3, context.Children.Count);
        Rect2D plus = gestures.TrayPlaces().Single(static p => p.Kind == DesignBarPlaceKind.AddRight).Box;
        Assert.False(gestures.PressTray(Centre(plus), DesignButton.Left, DesignModifiers.None));
        document.Undo();
        document.Select([item]);
        Rect2D row = gestures.TrayPlaces().First(p => p.Component == item && p.Kind == DesignBarPlaceKind.Menu).Box;
        Rect2D lastRow = gestures.TrayPlaces().Where(static p => p.Kind == DesignBarPlaceKind.Menu).Last().Box;
        Assert.True(gestures.PressTray(Centre(row), DesignButton.Left, DesignModifiers.None));
        Assert.Equal(item, document.Selection[0]);
        gestures.DragTray(new Point2D(row.X + 5, lastRow.Bottom - 1), default, overTray: true, DesignModifiers.None);
        Assert.True(gestures.BarDragInTray);
        Assert.NotNull(gestures.BarDrop);
        gestures.Release();
        Assert.Equal(item, context.Children[^1]);

        // A key on a row edits it; a double click too.
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal("Text", gestures.InPlace!.Property);
        gestures.EndInPlace(save: false, string.Empty);
        Rect2D rowAgain = gestures.TrayPlaces().First(p => p.Component == item && p.Kind == DesignBarPlaceKind.Menu).Box;
        Assert.True(gestures.DoubleClickTray(Centre(rowAgain)));
        gestures.EndInPlace(save: false, string.Empty);

        // The go-back line ends it, the context menu selected; Escape on it goes to the figure.
        Rect2D back = gestures.TrayGoBack!.Value.Box;
        Assert.False(gestures.PressTray(Centre(back), DesignButton.Left, DesignModifiers.None));
        Assert.Null(gestures.TrayEditing);
        Assert.Equal(context, document.Selection[0]);
        Assert.True(gestures.KeyDown(DesignKey.Escape, DesignModifiers.None));
        Assert.Equal(document.Design.Figure, document.Selection[0]);

        // A context menu's item selected from elsewhere opens it; its context menu selected shuts it.
        document.Select([item]);
        Assert.Equal(context, gestures.TrayEditing);
        document.Select([context]);
        Assert.Null(gestures.TrayEditing);

        // Deleted, it comes off the button and the tray goes.
        document.Delete([context]);
        Assert.Null(document.ContextMenuOf(button));
        Assert.False(gestures.TrayShown);
        document.Undo();
        Assert.Equal(context.CodeName, document.ContextMenuOf(button)!.CodeName);
    }

    [Fact]
    public void WhatADeletedMenuOrToolLeavesSelected()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        document.Delete([C(document, "Menu2")]);
        Assert.Equal(["Menu"], Selected(document));

        DesignComponent bar = AddEntry(document, "Toolbar");
        document.Select([bar]);
        document.AddTool(toggle: false, atEnd: true);
        document.Delete([bar.Children[1]]);
        Assert.Equal(bar.Children[0], document.Selection[0]);
    }

    [Fact]
    public void TheMenuKeyBringsWhatIsSelectedsMenu()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document) { Clipboard = new DesignClipboard() };
        gestures.Drop(Entry("Menu Bar"), new Point2D(50, 50));
        DesignComponent menu = C(document, "Menu");
        document.Select([menu]);
        IReadOnlyList<DesignMenuItem> items = gestures.KeyboardMenu(out Point2D at);
        Assert.Equal("Add Menu Item", items[0].Label);
        Assert.True(at.Y < 0);

        DesignComponent tool = AddEntry(document, "Toolbar").Children[0];
        document.Select([tool]);
        Assert.Equal("Add Push Tool", gestures.KeyboardMenu(out _)[0].Label);

        DesignComponent context = document.AddContextMenu();
        document.Select([context]);
        Assert.Equal("Cut", gestures.KeyboardMenu(out _)[0].Label);

        document.Select([C(document, "Button")]);
        Assert.Contains(gestures.KeyboardMenu(out _), static i => i.Label == "Context Menu");
    }

    [Fact]
    public void TheActionsAreLive()
    {
        foreach (string name in new[] { "DESIGN_MENU_HIERARCHY_ADD_RIGHT", "DESIGN_MENUBAR_SELECT_ALL", "DESIGN_TOOL_ADD_PUSHTOOL_TO_RIGHT", "DESIGN_TOOLBAR_ADD_TOGGLETOOL_TO_END" })
        {
            Assert.True(DesignActions.All.Values.Single(a => a.Name == name).IsLive, name);
        }
    }
}
