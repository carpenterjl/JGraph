using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W7a: the component browser over an app - its rows in each view,
/// the search, the app's own row, the breadcrumbs, rows picked and dragged as one edit that undoes, and its
/// menu in R2025b's order.
/// </summary>
[Collection("JG facade")]
public class AppDesignerBrowserTests
{
    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static DesignDocument App()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static IReadOnlyList<DesignComponent> Add(DesignDocument document, string entry, double x = 100, double y = 100, DesignComponent? into = null, bool withoutLabel = false) =>
        document.Add(Entry(entry), into ?? document.Design.Figure, new Point2D(x, y), withoutLabel: withoutLabel);

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static string[] Labels(DesignDocument document, DesignBrowserList view) => document.BrowserRows(view).Select(static r => r.Label).ToArray();

    private static string[] Words(IEnumerable<DesignMenuItem> items) => items.Select(static i => i.Label).ToArray();

    [Fact]
    public void TheRowsAreTheAppThenItsComponentsAndLabelsAreLeftOutByDefault()
    {
        DesignDocument document = App();
        Add(document, "Slider");
        Add(document, "Lamp", 200, 200, withoutLabel: true);
        Add(document, "Button", 300, 300);

        string app = document.AppName;
        Assert.False(new DesignBrowserState().View == DesignBrowserList.Complete);
        Assert.Equal([app, "app.UIFigure", "app.Button", "app.Lamp", "app.Slider"], Labels(document, DesignBrowserList.ExcludeLabels));
        Assert.Equal([app, "app.UIFigure", "app.Button", "app.Lamp", "app.Slider", "app.SliderLabel"], Labels(document, DesignBrowserList.Complete));

        // The tab order: what Tab goes to, in the order it is made; no label, no lamp.
        Assert.Equal([app, "app.UIFigure", "app.Slider", "app.Button"], Labels(document, DesignBrowserList.TabOrder));
    }

    [Fact]
    public void TheStateGoesBackFromTheTabOrderToWhatItListed()
    {
        var state = new DesignBrowserState();
        Assert.Equal(DesignBrowserList.ExcludeLabels, state.View);
        state.SetView(DesignBrowserList.Complete);
        state.SetView(DesignBrowserList.TabOrder);
        state.ShowStackOrder();
        Assert.Equal(DesignBrowserList.Complete, state.View);

        // The setting while the tab order shows is what the stack order goes back to.
        state.SetView(DesignBrowserList.TabOrder);
        state.SettingChanged(includeLabels: false);
        Assert.Equal(DesignBrowserList.TabOrder, state.View);
        state.ShowStackOrder();
        Assert.Equal(DesignBrowserList.ExcludeLabels, state.View);

        Assert.Equal(DesignBrowserList.Complete, new DesignBrowserState(includeLabels: true).View);
    }

    [Fact]
    public void ASearchKeepsWhatMatchesAndTheRowsAboveInAnyCase()
    {
        DesignDocument document = App();
        Add(document, "Panel", 10, 10, withoutLabel: true);
        DesignComponent panel = C(document, "Panel");
        Add(document, "Button", 20, 20, into: panel);
        Add(document, "Lamp", 300, 300, withoutLabel: true);
        IReadOnlyList<DesignBrowserRow> rows = document.BrowserRows(DesignBrowserList.Complete);

        string app = document.AppName;
        Assert.Equal([app, "app.UIFigure", "app.Panel", "app.Button"], DesignBrowser.Filtered(rows, "BUTTON").Select(static r => r.Label));

        // The label is searched as a whole, so "app." finds every component.
        Assert.Equal(rows.Count, DesignBrowser.Filtered(rows, "app.").Count);
        Assert.Empty(DesignBrowser.Filtered(rows, "nothing like it"));
        Assert.Equal(rows.Count, DesignBrowser.Filtered(rows, string.Empty).Count);

        Assert.Equal([(4, 3), (7, 3)], DesignBrowser.Matches("app.Tabtab", "tab"));
        Assert.Empty(DesignBrowser.Matches("app.Tab", string.Empty));
    }

    [Fact]
    public void TheAppsRowIsSelectedApartAndLeftByAnyOtherSelection()
    {
        DesignDocument document = App();
        Add(document, "Button", 20, 20);
        DesignComponent button = C(document, "Button");

        document.BrowserSelect([null]);
        Assert.True(document.AppSelected);
        Assert.True(document.SelectionIsEmpty);
        Assert.Equal([null], document.BrowserSelection);

        document.BrowserSelect([button]);
        Assert.False(document.AppSelected);
        Assert.Equal([button], document.BrowserSelection);

        // The figure picked from the app's row, when the figure was selected already, selects it again.
        document.BrowserSelect([null]);
        document.Select([document.Design.Figure]);
        Assert.False(document.AppSelected);
    }

    [Fact]
    public void PickingAFieldInTheTreeSelectsTheFieldNotTheLabelledComponent()
    {
        DesignDocument document = App();
        Add(document, "Slider");
        DesignComponent field = C(document, "Slider");

        // Added, the labelled component is selected whole, which the tree shows as its members.
        Assert.NotNull(document.SelectedLabeled);
        Assert.Equal(2, document.BrowserSelection.Count);

        document.Select([document.Design.Figure]);
        document.BrowserSelect([field]);
        Assert.Null(document.SelectedLabeled);
        Assert.Equal([field], document.Selection);
    }

    [Fact]
    public void ClickingAskedForRowsAsTheTreeDoes()
    {
        DesignDocument document = App();
        Add(document, "Button", 20, 20);
        Add(document, "Button", 20, 60);
        Add(document, "Button", 20, 100);
        IReadOnlyList<DesignBrowserRow> rows = document.BrowserRows(DesignBrowserList.Complete);
        DesignBrowserRow r2 = rows[2], r3 = rows[3], r4 = rows[4];

        DesignBrowserRow? boundary = null;
        Assert.Equal([r2], DesignBrowser.Clicked(rows, [], ref boundary, r2, ctrl: false, shift: false));
        Assert.Same(r2, boundary);

        // Ctrl adds and takes off; Shift runs from the row last clicked; Alt, and both, nothing.
        Assert.Equal([r2, r4], DesignBrowser.Clicked(rows, [r2], ref boundary, r4, ctrl: true, shift: false));
        Assert.Equal([r4], DesignBrowser.Clicked(rows, [r2, r4], ref boundary, r2, ctrl: true, shift: false));
        boundary = r2;
        Assert.Equal([r2, r3, r4], DesignBrowser.Clicked(rows, [r2], ref boundary, r4, ctrl: false, shift: true));
        Assert.Same(r2, boundary);
        Assert.Null(DesignBrowser.Clicked(rows, [r2], ref boundary, r4, ctrl: true, shift: true));
        Assert.Null(DesignBrowser.Clicked(rows, [r2], ref boundary, r4, ctrl: false, shift: false, alt: true));
    }

    [Fact]
    public void ADropAmongSiblingsIsOneEditThatUndoes()
    {
        DesignDocument document = App();
        Add(document, "Button", 20, 20);
        Add(document, "Lamp", 20, 60, withoutLabel: true);
        Add(document, "Image", 20, 100, withoutLabel: true);
        DesignComponent figure = document.Design.Figure;
        DesignComponent button = C(document, "Button"), lamp = C(document, "Lamp"), image = C(document, "Image");
        Assert.Equal([button, lamp, image], figure.Children);

        // The stack order lists them last made first: Image, Lamp, Button. The button dropped first in the
        // list goes last made, as if it were made after the image.
        document.BrowserSelect([button]);
        Assert.True(document.BrowserDrop(DesignBrowserList.ExcludeLabels, [button], figure, DesignBrowserPlace.Before, image));
        Assert.Equal([lamp, image, button], figure.Children);
        Assert.Equal([button], document.Selection);
        document.Undo();
        Assert.Equal([button, lamp, image], figure.Children);
        document.Redo();
        Assert.Equal([lamp, image, button], figure.Children);
        document.Undo();

        // The tab order lists them made first first, and a drop reads the other way.
        document.BrowserSelect([image]);
        Add(document, "Button", 20, 200);
        DesignComponent second = C(document, "Button2");
        Assert.True(document.BrowserDrop(DesignBrowserList.TabOrder, [second], figure, DesignBrowserPlace.Before, button));
        Assert.Equal(second, figure.Children[0]);
    }

    [Fact]
    public void ToolsAndNodesDropUnderOtherParentsAndUndo()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        gestures.Drop(Entry("Toolbar"), new Point2D(50, 50));
        gestures.Drop(Entry("Toolbar"), new Point2D(50, 50));
        DesignComponent one = C(document, "Toolbar"), two = C(document, "Toolbar2");
        document.Select([one]);
        DesignComponent tool = document.AddTool(toggle: false)!;
        document.Select([two]);
        DesignComponent other = document.AddTool(toggle: false)!;

        // A tool takes a toolbar only, and stays selected where it lands.
        Assert.False(document.BrowserCanDrop([tool], document.Design.Figure));
        Assert.True(document.BrowserCanDrop([tool], two));
        int inOne = one.Children.Count, inTwo = two.Children.Count;
        Assert.True(document.BrowserDrop(DesignBrowserList.Complete, [tool], two, DesignBrowserPlace.First));
        Assert.Same(tool, two.Children[0]);
        Assert.Equal(inTwo + 1, two.Children.Count);
        Assert.Equal(inOne - 1, one.Children.Count);
        Assert.Contains(other, two.Children);
        Assert.Equal([tool], document.Selection);
        document.Undo();
        Assert.Contains(tool, one.Children);
        Assert.Equal(inTwo, two.Children.Count);

        // A tree's node goes under a node, not into itself or its own.
        Add(document, "Tree", 20, 20, withoutLabel: true);
        DesignComponent tree = C(document, "Tree");
        DesignComponent a = tree.Children[0];
        document.Select([a]);
        DesignComponent b = document.AddTreeNode(child: false)!;
        Assert.True(document.BrowserCanDrop([b], a));
        Assert.False(document.BrowserCanDrop([a], a));
        Assert.True(document.BrowserDrop(DesignBrowserList.Complete, [b], a, DesignBrowserPlace.First));
        Assert.Equal([b], a.Children.Where(c => ReferenceEquals(c, b)));
        Assert.False(document.BrowserCanDrop([a], b));
    }

    [Fact]
    public void ALabelledSwitchStaysWithItsLabel()
    {
        DesignDocument document = App();
        Add(document, "Panel", 200, 200, withoutLabel: true);
        Add(document, "Switch", 20, 20);
        DesignComponent panel = C(document, "Panel"), slider = C(document, "Switch");

        // R2025b lets a slider switch go to any parent, and leaves its label behind; it stays here.
        Assert.False(document.BrowserCanDrop([slider], panel));
        document.Delete(document.Selection);
        Add(document, "Switch", 20, 20, withoutLabel: true);
        Assert.True(document.BrowserCanDrop([C(document, "Switch")], panel));
    }

    [Fact]
    public void TheBreadcrumbsRunFromTheAppToTheSelection()
    {
        DesignDocument document = App();
        Add(document, "Panel", 10, 10, withoutLabel: true);
        DesignComponent panel = C(document, "Panel");
        Add(document, "Button", 20, 20, into: panel);
        Add(document, "Button", 20, 80, into: panel);
        Add(document, "Slider", 300, 300);
        DesignComponent button = C(document, "Button"), button2 = C(document, "Button2");
        string app = document.AppName;

        string[] Crumbs() => document.Breadcrumbs().Select(static c => c.Text).ToArray();

        Assert.Equal([app, "UIFigure", "Slider"], Crumbs());
        document.Select([button]);
        Assert.Equal([app, "UIFigure", "Panel", "Button"], Crumbs());
        document.Select([button, button2]);
        Assert.Equal([app, "UIFigure", "Panel", "Multiple Components(2)"], Crumbs());
        document.Select([button, button2]);
        document.Group();
        Assert.Equal([app, "UIFigure", "Panel", "Group(2)"], Crumbs());
        document.BrowserSelect([null]);
        Assert.Equal([app], Crumbs());

        Assert.Equal("A name of nineteen c", DesignBrowser.CrumbText("A name of nineteen characters")[..20]);
        Assert.Equal("Short", DesignBrowser.CrumbText("Short"));
        Assert.EndsWith("...", DesignBrowser.CrumbText("Exactly twenty chars"));
    }

    [Fact]
    public void TheMenuIsInR2025bsOrderForEachKindOfSelection()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document) { Clipboard = new DesignClipboard() };
        var state = new DesignBrowserState();
        IReadOnlyList<DesignMenuItem> Menu(bool code = false) => gestures.BrowserMenu(state, code, () => { }, () => { }, _ => { }, canInsert: true);

        Add(document, "Button", 20, 20);
        Assert.Equal(
            [DesignBrowser.IncludeLabels, "-", DesignBrowser.SortByStackOrder, DesignBrowser.SortByTabOrder, "-", "Rename", "Cut", "Copy", "Paste", "Duplicate", "Delete", "Reorder", "-", "Callbacks", "Context Menu", "-", "Help on Selection"],
            Words(Menu()));
        Assert.Equal(["Bring to Front", "Bring Forward", "Send Backward", "Send to Back"], Words(Menu().Single(static i => i.Label == "Reorder").Items!));

        // A class that can have a label has Add Label, on its own line after the sorts.
        Add(document, "Slider", 200, 200, withoutLabel: true);
        Assert.Equal(["-", "Add Label", "-", "Rename"], Words(Menu())[4..8]);
        document.Select([C(document, "Button")]);

        // The Code View takes the edits off and has Insert at Cursor, which gives app.Name.
        string? inserted = null;
        IReadOnlyList<DesignMenuItem> code = gestures.BrowserMenu(state, true, () => { }, () => { }, t => inserted = t, canInsert: true);
        Assert.DoesNotContain("Cut", Words(code));
        Assert.DoesNotContain("Rename", Words(code));
        code.Single(static i => i.Label == "Insert at Cursor").Run!();
        Assert.Equal("app.Button", inserted);
        Assert.False(gestures.BrowserMenu(state, true, () => { }, () => { }, _ => { }, canInsert: false).Single(static i => i.Label == "Insert at Cursor").Enabled);

        // The figure: no Delete, no Reorder, no Duplicate; the tab order instead.
        document.Select([document.Design.Figure]);
        string[] figure = Words(Menu());
        Assert.DoesNotContain("Delete", figure);
        Assert.DoesNotContain("Reorder", figure);
        Assert.DoesNotContain("Duplicate", figure);
        Assert.Equal(["Apply Auto Tab Order", "Visualize Tab Order"], figure[^2..]);

        // The app's row: its name not edited, nothing to cut, the whole app's tab order.
        document.BrowserSelect([null]);
        IReadOnlyList<DesignMenuItem> app = Menu();
        Assert.False(app.Single(static i => i.Label == "Rename").Enabled);
        Assert.False(app.Single(static i => i.Label == "Cut").Enabled);
        Assert.Equal(["Apply Auto Tab Order", "Visualize Tab Order"], Words(app)[^2..]);

        // The tab order's view has no include labels, and its own sort is checked.
        state.SetView(DesignBrowserList.TabOrder);
        document.Select([C(document, "Button")]);
        IReadOnlyList<DesignMenuItem> tabs = Menu();
        Assert.Equal(DesignBrowser.SortByStackOrder, tabs[0].Label);
        Assert.False(tabs[0].Checked);
        Assert.True(tabs[1].Checked);

        // A click on the other sort changes the view, on its own nothing.
        bool shown = false;
        IReadOnlyList<DesignMenuItem> again = gestures.BrowserMenu(state, false, () => shown = true, () => { }, _ => { }, canInsert: true);
        again[1].Run!();
        Assert.False(shown);
        again[0].Run!();
        Assert.True(shown);
        Assert.Equal(DesignBrowserList.ExcludeLabels, state.View);
    }
}
