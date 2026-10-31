using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W7d: callbacks over several components in an app - one function for them all as one edit, what the Callbacks tab is over, and the
/// Callbacks submenu in the canvas's, the bars', a tree node's and the browser's menus.
/// </summary>
[Collection("JG facade")]
public class AppDesignerCallbacksTests
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

    private static DesignComponent One(DesignDocument document, string entry, double x = 100, double y = 100) =>
        document.Add(Entry(entry), document.Design.Figure, new Point2D(x, y), withoutLabel: true)[^1];

    private static string[] Words(IEnumerable<DesignMenuItem> items) => items.Where(static i => !i.IsSeparator).Select(static i => i.Label).ToArray();

    private static DesignMenuItem Sub(IEnumerable<DesignMenuItem> items, string label) => items.Single(i => i.Label == label);

    private static DesignMenuItem CallbacksOf(DesignGestures gestures) => Sub(gestures.MenuAt(new Point2D(0, 0)), DesignCallbackRules.MenuText);

    [Fact]
    public void AddFromTheMenuMakesOneFunctionForThemAllNamedForTheFirstAsOneEdit()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        DesignComponent b = One(document, "Slider", 100, 200);
        document.Select([a, b]);
        var gestures = new DesignGestures(document);
        string? went = null;
        gestures.CallbackGoTo += (_, name) => went = name;

        Sub(CallbacksOf(gestures).Items!, "Add ValueChangedFcn callback").Run!();

        Assert.Equal("SliderValueChanged", went);
        Assert.Equal("SliderValueChanged", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("SliderValueChanged", document.CallbackOf(b, "ValueChangedFcn"));
        Assert.Single(document.Callbacks);
        Assert.Contains("% Value changed function: Slider, Slider2", document.CurrentText);
        Assert.Equal([a, b], document.Selection);

        // The last selected was bound first, whose code it starts with.
        Assert.Contains("value = app.Slider2.Value;", document.CurrentText);

        document.Undo();
        Assert.Empty(document.Callbacks);
        Assert.Null(document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Null(document.CallbackOf(b, "ValueChangedFcn"));

        document.Redo();
        Assert.Equal("SliderValueChanged", document.CallbackOf(b, "ValueChangedFcn"));
        Assert.Single(document.Callbacks);
    }

    [Fact]
    public void AddFromTheMenuRenamesTheFunctionALaterOneHoldsAsR2025bDoes()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        DesignComponent b = One(document, "Slider", 100, 200);
        DesignComponent c = One(document, "Slider", 100, 300);
        Assert.Null(document.AddCallback(b, "ValueChangedFcn", "held"));
        Assert.Null(document.BindCallback(c, "ValueChangedFcn", "held"));
        document.Select([a, b]);
        var gestures = new DesignGestures(document);
        string? went = null;
        gestures.CallbackGoTo += (_, name) => went = name;

        Sub(CallbacksOf(gestures).Items!, "Add ValueChangedFcn callback").Run!();

        // The last of them is first: its function takes the new name, whoever else holds it, and the first binds to it. Nothing was made.
        Assert.Null(went);
        Assert.Single(document.Callbacks);
        Assert.Equal("SliderValueChanged", document.Callbacks[0].Name);
        Assert.Equal("SliderValueChanged", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("SliderValueChanged", document.CallbackOf(b, "ValueChangedFcn"));
        Assert.Equal("SliderValueChanged", document.CallbackOf(c, "ValueChangedFcn"));

        document.Undo();
        Assert.Equal("held", document.Callbacks[0].Name);
        Assert.Null(document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("held", document.CallbackOf(c, "ValueChangedFcn"));
    }

    [Fact]
    public void TheTabIsOverWhatTheSelectionComesTo()
    {
        DesignDocument document = App();
        DesignComponent slider = One(document, "Slider");
        DesignComponent drop = One(document, "Drop Down", 100, 200);
        DesignComponent lamp = One(document, "Lamp", 100, 300);

        // One: its own groups.
        document.Select([drop]);
        DesignCallbackSheet one = document.CallbackSheet!;
        Assert.Same(drop, one.Anchor);
        Assert.Equal(["ValueChangedFcn", "DropDownOpeningFcn", "ClickedFcn"], one.Groups.Single().Properties);

        // Several: what they have in common, the last one's shown.
        document.Select([slider, drop]);
        DesignCallbackSheet two = document.CallbackSheet!;
        Assert.Same(drop, two.Anchor);
        Assert.Equal(["ValueChangedFcn"], two.Groups.Single().Properties);
        Assert.Null(two.Groups.Single().Title);
        Assert.Equal([slider, drop], two.Peers);

        // Nothing in common.
        document.Select([slider, lamp]);
        Assert.Equal(DesignCallbackRules.NothingInCommon, document.CallbackSheet!.Message);
        Assert.Empty(document.CallbackSheet!.Groups);

        // None at all.
        document.Select([lamp]);
        Assert.Equal("Lamp objects do not have callback properties.", document.CallbackSheet!.Message);

        // The figure's groups have no StartupFcn: that is the app's row.
        document.Select([document.Design.Figure]);
        Assert.Equal(["COMMON", "KEYBOARD", "WINDOW"], document.CallbackSheet!.Groups.Select(static g => g.Title));
        Assert.DoesNotContain(document.CallbackSheet!.Groups.SelectMany(static g => g.Properties), static p => p == "StartupFcn");
        document.SelectApp();
        DesignCallbackSheet app = document.CallbackSheet!;
        Assert.Null(app.Anchor);
        Assert.Equal(["StartupFcn"], app.Groups.Single().Properties);
    }

    [Fact]
    public void ALabelledComponentIsItsFieldAndALabelBesideAnotherGoes()
    {
        DesignDocument document = App();
        IReadOnlyList<DesignComponent> labelled = document.Add(Entry("Slider"), document.Design.Figure, new Point2D(100, 100));
        DesignGroup group = document.Design.LabeledGroupOf(labelled[^1])!;
        DesignComponent field = group.Field!, label = group.Label!;
        DesignComponent button = One(document, "Button", 100, 300);

        document.Select([field]);
        Assert.Equal([field], document.MenuComponents);
        Assert.Same(field, document.CallbackSheet!.Anchor);
        Assert.Equal(["ValueChangedFcn", "ValueChangingFcn"], document.CallbackSheet!.Groups.Single().Properties);

        // A label alone, as the browser picks it: a class with no callbacks.
        document.Select([button]);
        document.BrowserSelect([label]);
        Assert.Equal("Label objects do not have callback properties.", document.CallbackSheet!.Message);
        var gestures = new DesignGestures(document);
        Assert.DoesNotContain(DesignCallbackRules.MenuText, Words(gestures.MenuAt(new Point2D(0, 0))));
        Assert.Contains(DesignCallbackRules.HelpText, Words(gestures.MenuAt(new Point2D(0, 0))));

        // Beside another, it goes.
        document.BrowserSelect([label, button]);
        Assert.Equal([button], document.CallbackSheet!.Peers);
    }

    [Fact]
    public void ARowEditsThemAllAndAnEditOfTheAnchorsOwnValueDoesNothing()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        DesignComponent b = One(document, "Slider", 100, 200);
        Assert.Null(document.AddCallback(a, "ValueChangedFcn", "first"));
        document.Select([a, b]);
        DesignCallbackSheet sheet = document.CallbackSheet!;
        Assert.Same(b, sheet.Anchor);

        // What the anchor holds already: nothing, whatever the others hold.
        Assert.Null(document.EditCallbackRow(sheet.Peers, b, "ValueChangedFcn", string.Empty, out string? made));
        Assert.Null(made);
        Assert.Equal("first", document.CallbackOf(a, "ValueChangedFcn"));

        // A new name: made, the old function kept.
        Assert.Null(document.EditCallbackRow(sheet.Peers, b, "ValueChangedFcn", "shared", out made));
        Assert.Equal("shared", made);
        Assert.Equal(["first", "shared"], document.Callbacks.Select(static c => c.Name));
        Assert.Equal("shared", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("shared", document.CallbackOf(b, "ValueChangedFcn"));

        // One the app has: moved there, made nothing, one edit for them both.
        Assert.Null(document.EditCallbackRow(sheet.Peers, b, "ValueChangedFcn", "first", out made));
        Assert.Null(made);
        Assert.Equal("first", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("first", document.CallbackOf(b, "ValueChangedFcn"));
        document.Undo();
        Assert.Equal("shared", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("shared", document.CallbackOf(b, "ValueChangedFcn"));

        // None: both unbound, both functions kept.
        Assert.Null(document.EditCallbackRow(sheet.Peers, b, "ValueChangedFcn", string.Empty, out made));
        Assert.Null(document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Null(document.CallbackOf(b, "ValueChangedFcn"));
        Assert.Equal(2, document.Callbacks.Count);

        // The default's name is the anchor's.
        Assert.Null(document.EditCallbackRow(sheet.Peers, b, "ValueChangedFcn", DesignCallbackRules.UseDefault, out made));
        Assert.Equal("Slider2ValueChanged", made);
        Assert.Equal("Slider2ValueChanged", document.CallbackOf(a, "ValueChangedFcn"));
    }

    [Theory]
    [InlineData("for", "Name cannot be a MATLAB keyword.")]
    [InlineData("a b", "Name must start with a letter and contain only letters, numbers or underscores.")]
    [InlineData("Slider", "Name is already defined as component.")]
    [InlineData("startupFcn", DesignCallbackRules.CannotShareStartup)]
    public void ANameRefusedSaysSoAndChangesNothing(string name, string why)
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        Assert.Null(document.AddStartup());
        int callbacks = document.Callbacks.Count;
        Assert.Equal(why, document.EditCallbackRow([a], a, "ValueChangedFcn", name, out string? made));
        Assert.Null(made);
        Assert.Null(document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal(callbacks, document.Callbacks.Count);
    }

    [Fact]
    public void AnEditGoesEachWayInTurnFromTheListTheTabKeeps()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        DesignComponent b = One(document, "Slider", 100, 200);
        List<DesignComponent> peers = [a, b];
        Assert.Null(document.MassEditCallback(peers, "one", "ValueChangedFcn", detachPrevious: true, out _));
        Assert.Equal([b, a], peers);
        Assert.Contains("value = app.Slider2.Value;", document.CurrentText);
        Assert.Null(document.MassEditCallback(peers, "two", "ValueChangedFcn", detachPrevious: true, out _));
        Assert.Equal([a, b], peers);
    }

    [Fact]
    public void WhatAComponentDoesNotHaveIsRefusedWhole()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Drop Down");
        DesignComponent b = One(document, "Slider", 100, 200);
        Assert.Equal("Slider has no callback property DropDownOpeningFcn.", document.MassEditCallback([a, b], "made", "DropDownOpeningFcn", detachPrevious: false, out string? made));
        Assert.Null(made);
        Assert.Empty(document.Callbacks);
        Assert.Null(document.CallbackOf(a, "DropDownOpeningFcn"));
    }

    [Fact]
    public void TheFunctionsAreWrittenAndComeBack()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Button", 100, 200);
        Assert.Null(document.MassEditCallback([a, b], "pressed", "ButtonPushedFcn", detachPrevious: false, out _));
        DesignDocument again = DesignDocument.FromText(document.CurrentText, null);
        Assert.Equal("pressed", again.CallbackOf(again.Design.Find("Button")!, "ButtonPushedFcn"));
        Assert.Equal("pressed", again.CallbackOf(again.Design.Find("Button2")!, "ButtonPushedFcn"));
        Assert.Contains("% Button pushed function: Button, Button2", again.CurrentText);
    }

    [Fact]
    public void TheFigureHasItsGroupsAsSubmenusBeforeSelectExisting()
    {
        DesignDocument document = App();
        document.Select([document.Design.Figure]);
        var gestures = new DesignGestures(document);
        DesignMenuItem callbacks = CallbacksOf(gestures);
        Assert.Equal(
            ["Add CloseRequestFcn callback", "Add ButtonDownFcn callback", "Add ThemeChangedFcn callback", "Keyboard Callbacks", "Window Callbacks", DesignCallbackRules.SelectExistingText],
            Words(callbacks.Items!));
        Assert.Equal(["Add KeyPressFcn callback", "Add KeyReleaseFcn callback"], Words(Sub(callbacks.Items!, "Keyboard Callbacks").Items!));
        Assert.Contains("Add SizeChangedFcn callback", Words(Sub(callbacks.Items!, "Window Callbacks").Items!));

        // Select existing wants a function to bind.
        Assert.False(Sub(callbacks.Items!, DesignCallbackRules.SelectExistingText).Enabled);
        DesignComponent button = One(document, "Button");
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn", "pressed"));
        document.Select([document.Design.Figure]);
        Assert.True(Sub(CallbacksOf(gestures).Items!, DesignCallbackRules.SelectExistingText).Enabled);
    }

    [Fact]
    public void AGroupsSubmenuSaysItsTitleOnceWhereR2025bSaysItTwice()
    {
        DesignDocument document = App();
        DesignComponent tree = One(document, "Tree");
        document.Select([tree]);
        var gestures = new DesignGestures(document);
        Assert.Contains("Mouse Callbacks", Words(CallbacksOf(gestures).Items!));
    }

    [Fact]
    public void WithSeveralAGroupsItemsAreThoseTheyAllHave()
    {
        DesignDocument document = App();
        DesignComponent tree = One(document, "Tree");
        DesignComponent table = One(document, "Table", 300, 100);
        document.Select([tree, table]);
        var gestures = new DesignGestures(document);
        DesignMenuItem callbacks = CallbacksOf(gestures);
        Assert.Equal(["Add SelectionChangedFcn callback", "Mouse Callbacks", DesignCallbackRules.SelectExistingText], Words(callbacks.Items!));
        Assert.Equal(["Add ClickedFcn callback", "Add DoubleClickedFcn callback"], Words(Sub(callbacks.Items!, "Mouse Callbacks").Items!));
    }

    [Fact]
    public void GoToSaysTheFunctionAndGoesThere()
    {
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn", "pressed"));
        document.Select([button]);
        var gestures = new DesignGestures(document);
        string? went = null;
        gestures.CallbackGoTo += (_, name) => went = name;
        Sub(CallbacksOf(gestures).Items!, "Go to pressed callback").Run!();
        Assert.Equal("pressed", went);
        Assert.False(Sub(CallbacksOf(gestures).Items!, DesignCallbackRules.SelectExistingText).Enabled);
    }

    [Fact]
    public void SelectExistingAsksForWhoItIsFor()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Slider");
        DesignComponent b = One(document, "Slider", 100, 200);
        DesignComponent c = One(document, "Button", 100, 300);
        Assert.Null(document.AddCallback(c, "ButtonPushedFcn", "pressed"));
        document.Select([a, b]);
        var gestures = new DesignGestures(document);
        IReadOnlyList<DesignComponent>? asked = null;
        gestures.CallbackSelectAsked += (_, peers) => asked = peers;
        DesignMenuItem item = Sub(CallbacksOf(gestures).Items!, DesignCallbackRules.SelectExistingText);
        Assert.True(item.Enabled);
        item.Run!();
        Assert.Equal([a, b], asked);

        // Attached, no function was made, so nothing goes to the Code View.
        Assert.Null(document.MassEditCallback([.. asked!], "pressed", "ValueChangedFcn", detachPrevious: false, out string? made));
        Assert.Null(made);
        Assert.Equal("pressed", document.CallbackOf(a, "ValueChangedFcn"));
        Assert.Equal("pressed", document.CallbackOf(b, "ValueChangedFcn"));
    }

    [Fact]
    public void TheAppsRowAddsAndGoesToItsStartupFunctionAndBindsNothing()
    {
        DesignDocument document = App();
        One(document, "Button");
        document.SelectApp();
        var gestures = new DesignGestures(document);
        string? went = null;
        gestures.CallbackGoTo += (_, name) => went = name;
        IReadOnlyList<DesignMenuItem> menu = gestures.BrowserMenu(new DesignBrowserState(), false, static () => { }, static () => { }, static _ => { }, false);
        DesignMenuItem callbacks = Sub(menu, DesignCallbackRules.MenuText);
        Assert.Equal(["Add StartupFcn callback", DesignCallbackRules.SelectExistingText], Words(callbacks.Items!));
        Assert.False(Sub(callbacks.Items!, DesignCallbackRules.SelectExistingText).Enabled);
        Assert.Contains(DesignCallbackRules.HelpText, Words(menu));
        Assert.DoesNotContain("Context Menu", Words(menu));

        Sub(callbacks.Items!, "Add StartupFcn callback").Run!();
        Assert.Equal("startupFcn", went);
        Assert.True(document.Callbacks.Single().IsStartup);

        menu = gestures.BrowserMenu(new DesignBrowserState(), false, static () => { }, static () => { }, static _ => { }, false);
        Assert.Contains("Go to startupFcn callback", Words(Sub(menu, DesignCallbackRules.MenuText).Items!));
    }

    [Fact]
    public void TheCanvasMenuHasThemAfterTheDisplayOptionsAndBeforeReplaceWith()
    {
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        document.Select([button]);
        var gestures = new DesignGestures(document);
        string[] words = Words(gestures.MenuAt(new Point2D(0, 0)));
        int callbacks = Array.IndexOf(words, DesignCallbackRules.MenuText);
        Assert.True(callbacks > Array.IndexOf(words, "Reorder"));
        Assert.Equal(callbacks + 1, Array.IndexOf(words, "Context Menu"));
        Assert.Equal(callbacks + 2, Array.IndexOf(words, DesignCallbackRules.HelpText));
        Assert.True(Array.IndexOf(words, "Replace With") > callbacks + 2);

        var seen = new List<string>();
        gestures.HelpAsked += (_, _) => seen.Add("help");
        Sub(gestures.MenuAt(new Point2D(0, 0)), DesignCallbackRules.HelpText).Run!();
        Assert.Equal(["help"], seen);
    }

    [Fact]
    public void TheBrowsersMenuHasThemAfterInsertAtCursorAndNoContextMenuInTheCodeView()
    {
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        document.Select([button]);
        var gestures = new DesignGestures(document);
        string[] design = Words(gestures.BrowserMenu(new DesignBrowserState(), false, static () => { }, static () => { }, static _ => { }, false));
        Assert.Equal(Array.IndexOf(design, DesignCallbackRules.MenuText) + 1, Array.IndexOf(design, "Context Menu"));
        Assert.True(Array.IndexOf(design, DesignCallbackRules.MenuText) > Array.IndexOf(design, "Reorder"));

        string[] code = Words(gestures.BrowserMenu(new DesignBrowserState(), true, static () => { }, static () => { }, static _ => { }, true));
        Assert.Equal(Array.IndexOf(code, "Insert at Cursor") + 1, Array.IndexOf(code, DesignCallbackRules.MenuText));
        Assert.DoesNotContain("Context Menu", code);
        Assert.Contains(DesignCallbackRules.HelpText, code);

        // A container's tab order stands after them.
        DesignComponent panel = One(document, "Panel", 300, 100);
        document.Select([panel]);
        string[] holder = Words(gestures.BrowserMenu(new DesignBrowserState(), false, static () => { }, static () => { }, static _ => { }, false));
        Assert.True(Array.IndexOf(holder, "Apply Auto Tab Order") > Array.IndexOf(holder, DesignCallbackRules.HelpText));
    }

    [Fact]
    public void TheBarsMenusHaveThem()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        document.AddMenuAtEnd(document.Design.Figure);
        DesignComponent menu = document.TopLevelMenus[0];
        document.Select([menu]);
        DesignMenuItem callbacks = Sub(gestures.MenuAt(new Point2D(0, 0)), DesignCallbackRules.MenuText);
        Assert.Contains("Add MenuSelectedFcn callback", Words(callbacks.Items!));
        Assert.Contains(DesignCallbackRules.HelpText, Words(gestures.MenuAt(new Point2D(0, 0))));
        Assert.DoesNotContain("Context Menu", Words(gestures.MenuAt(new Point2D(0, 0))));
    }
}
