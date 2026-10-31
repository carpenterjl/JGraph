using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W5e: Replace With on the canvas's menu and a tool's - a component or a
/// labelled component made into another class of its family, as one edit that undoes: what it holds, where
/// it stands, what it and its label are called, what follows its name, what becomes of its callbacks.
/// </summary>
[Collection("JG facade")]
public class AppDesignerReplaceTests
{
    private const string Slider = "matlab.ui.control.Slider";
    private const string Knob = "matlab.ui.control.Knob";

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static DesignDocument App()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        var button = new DesignComponent("Other", DesignCatalog.R2025b.ByType("matlab.ui.control.Button")!);
        button.Set("Position", NumericValue.Row(500, 420, 100, 23));
        figure.Add(button);
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static IReadOnlyList<DesignComponent> Add(DesignDocument document, string entry, double x = 100, double y = 100, bool withoutLabel = false) =>
        document.Add(Entry(entry), document.Design.Figure, new Point2D(x, y), withoutLabel: withoutLabel);

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static string[] Children(DesignDocument document) => document.Design.Figure.Children.Select(static c => c.CodeName).ToArray();

    private static string Words(DesignComponent component, string property) => DesignPalette.Text(component.Get(property)!)!;

    private static double[] Numbers(DesignComponent component, string property) => [.. ((NumericValue)component.Get(property)!).Data];

    private static DesignMenuItem? ReplaceItem(DesignGestures gestures) =>
        gestures.MenuAt(new Point2D(0, 0)).SingleOrDefault(static i => i.Label == "Replace With");

    [Fact]
    public void TheMenuOffersTheFamilyLastAndForOneComponentOnly()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Add(document, "Slider");
        Assert.Equal(["SliderLabel", "Slider"], document.Selection.Select(static c => c.CodeName));

        // A labelled component, last in the canvas's menu with R2025b's names in its order.
        IReadOnlyList<DesignMenuItem> menu = gestures.MenuAt(new Point2D(0, 0));
        Assert.Equal("Replace With", menu[^1].Label);
        Assert.True(menu[^2].IsSeparator);
        Assert.Equal(["Knob", "Edit Field (Numeric)", "Slider (Range)", "Spinner"], menu[^1].Items!.Select(static i => i.Label));

        // Its field alone counts as it; its label does not.
        document.SelectPicks([DesignPick.Of(C(document, "Slider"))]);
        Assert.NotNull(ReplaceItem(gestures));
        document.SelectPicks([DesignPick.Of(C(document, "SliderLabel"))]);
        Assert.Null(ReplaceItem(gestures));

        // Two components, a class with no family, nothing, and a plain group do not.
        document.Select([C(document, "Slider"), C(document, "Other")]);
        Assert.Null(ReplaceItem(gestures));
        Add(document, "Panel", 300, 100);
        Assert.Null(ReplaceItem(gestures));
        document.Select([document.Design.Figure]);
        Assert.Null(ReplaceItem(gestures));

        // A button is offered a state button, and a label a hyperlink.
        document.Select([C(document, "Other")]);
        Assert.Equal(["State Button"], ReplaceItem(gestures)!.Items!.Select(static i => i.Label));
        Add(document, "Label", 300, 300);
        Assert.Equal(["Hyperlink"], ReplaceItem(gestures)!.Items!.Select(static i => i.Label));
    }

    [Fact]
    public void ALabelledSliderBecomesAKnobWithItsLabelAndUndoes()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Add(document, "Slider", 100, 100);
        DesignComponent slider = C(document, "Slider");
        DesignComponent label = C(document, "SliderLabel");
        DesignGroup group = document.Design.LabeledGroupOf(slider)!;
        string[] before = Children(document);
        double[] sliderBefore = Numbers(slider, "Position");
        double[] labelBefore = Numbers(label, "Position");
        string[] selectedBefore = document.Selection.Select(static c => c.CodeName).ToArray();

        ReplaceItem(gestures)!.Items!.Single(static i => i.Label == "Knob").Run!();

        // The default names follow the class, as does the label's default text.
        DesignComponent knob = C(document, "Knob");
        Assert.Equal(Knob, knob.Type);
        Assert.Null(document.Design.Find("Slider"));
        Assert.Same(label, C(document, "KnobLabel"));
        Assert.Equal("Knob", Words(label, "Text"));

        // Each stands where the old one stood among its siblings, and they are still a labelled component, selected.
        Assert.Equal(before.Select(static n => n == "SliderLabel" ? "KnobLabel" : n == "Slider" ? "Knob" : n), Children(document));
        Assert.Same(group, document.Design.LabeledGroupOf(knob));
        Assert.Same(knob, group.Field);
        Assert.Equal(["KnobLabel", "Knob"], document.Selection.Select(static c => c.CodeName));
        Assert.True(document.Picks is [{ Group: not null }]);

        // A knob's size, its position within the box a new one takes at the slider's outer corner.
        double[] position = Numbers(knob, "Position");
        Assert.Equal(60, position[2]);
        Assert.Equal(60, position[3]);
        Assert.Equal(sliderBefore[0] - 7 + 26, position[0]);

        // The label is under it now, as a knob's is, and App Designer's code reads back.
        Assert.True(Numbers(label, "Position")[1] < position[1]);
        DesignDocument saved = DesignDocument.FromText(document.CurrentText, null);
        Assert.Equal(Knob, saved.Design.Find("Knob")!.Type);
        Assert.Equal("Knob", Words(saved.Design.Find("KnobLabel")!, "Text"));

        // One edit: Undo puts it all back.
        document.Undo();
        Assert.Same(slider, C(document, "Slider"));
        Assert.Same(label, C(document, "SliderLabel"));
        Assert.Equal(before, Children(document));
        Assert.Equal(sliderBefore, Numbers(slider, "Position"));
        Assert.Equal(labelBefore, Numbers(label, "Position"));
        Assert.Equal("Slider", Words(label, "Text"));
        Assert.Same(slider, group.Field);
        Assert.Equal(selectedBefore, document.Selection.Select(static c => c.CodeName));
        document.Redo();
        Assert.Same(knob, C(document, "Knob"));
        Assert.Same(knob, group.Field);
    }

    [Fact]
    public void ALabelsOwnWordsStayAndTheNamesFollowThemIntoTheCode()
    {
        DesignDocument document = App();
        Add(document, "Slider", 100, 100);
        DesignComponent label = C(document, "SliderLabel");
        document.Select([label]);
        document.SetProperty(label, "Text", new CharValue("Speed"));
        Assert.NotNull(document.Design.Find("SpeedSlider"));
        Assert.NotNull(document.Design.Find("SpeedSliderLabel"));
        DesignGroup group = document.Design.LabeledGroupOf(label)!;
        document.Select([group.Field!]);

        string? added = document.AddCallback(group.Field!, "ValueChangedFcn");
        Assert.Null(added);
        DesignCallback callback = document.Callbacks.Single(static c => c.Name == "SpeedSliderValueChanged");
        Assert.Contains("value = app.SpeedSlider.Value;", callback.Body.Select(static l => l.Trim()));
        callback.Body.Add("            label = app.SpeedSliderLabel.Text;");

        DesignComponent knob = document.ReplaceWith(Knob)!;

        Assert.Equal("SpeedKnob", knob.CodeName);
        Assert.Equal("SpeedKnobLabel", label.CodeName);
        Assert.Equal("Speed", Words(label, "Text"));

        // Both names follow in the user's code - R2025b leaves the label's at a name it does not end up with -
        // and the callback, named by its old name, takes the new one.
        Assert.Equal("SpeedKnobValueChanged", callback.Name);
        Assert.Equal("SpeedKnobValueChanged", ((CallbackValue)knob.Get("ValueChangedFcn")!).Method);
        Assert.Contains("value = app.SpeedKnob.Value;", callback.Body.Select(static l => l.Trim()));
        Assert.Contains("label = app.SpeedKnobLabel.Text;", callback.Body.Select(static l => l.Trim()));

        document.Undo();
        Assert.Equal("SpeedSliderValueChanged", callback.Name);
        Assert.Contains("value = app.SpeedSlider.Value;", callback.Body.Select(static l => l.Trim()));
        Assert.Contains("label = app.SpeedSliderLabel.Text;", callback.Body.Select(static l => l.Trim()));
        Assert.Equal("SpeedSliderLabel", label.CodeName);
    }

    [Fact]
    public void ANameByHandStays()
    {
        DesignDocument document = App();
        Add(document, "Spinner", 100, 100);
        DesignComponent spinner = C(document, "Spinner");
        Assert.Null(document.Rename(spinner, "rate"));
        Assert.Null(document.AddCallback(spinner, "ValueChangedFcn"));
        DesignCallback callback = document.Callbacks.Single(static c => c.Name == "rateValueChanged");

        DesignComponent knob = document.ReplaceWith(Knob)!;
        Assert.Equal("rate", knob.CodeName);

        // R2025b counts the callback's own name as taken, and makes it rateValueChanged2: here it stays.
        Assert.Equal("rateValueChanged", callback.Name);
    }

    [Fact]
    public void ALabelNamedByHandThatSaysItsClasssWordDoesNotFail()
    {
        DesignDocument document = App();
        Add(document, "Slider", 100, 100);
        DesignComponent label = C(document, "SliderLabel");
        Assert.Null(document.Rename(label, "myLabel"));
        DesignComponent knob = document.ReplaceWith(Knob)!;
        Assert.Equal(Knob, knob.Type);
        Assert.Equal("myLabel", label.CodeName);
        Assert.Equal("Knob", Words(label, "Text"));
    }

    [Fact]
    public void AButtonBecomesAStateButtonKeepingItsTextAndNotItsPushedCallback()
    {
        DesignDocument document = App();
        DesignComponent button = Add(document, "Button", 100, 100)[0];
        document.SetProperty(button, "Text", new CharValue("Go"));
        Assert.Equal("GoButton", button.CodeName);
        document.SetProperty(button, "Position", NumericValue.Row(100, 100, 300, 80));
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Single(document.Callbacks, static c => !c.IsStartup);

        DesignComponent state = document.ReplaceWith("matlab.ui.control.StateButton")!;
        Assert.Equal("Go", Words(state, "Text"));

        // R2025b's generator: a new one's name, counted among the texts (the other button says Button), which
        // the text then does not rename, as a text the name does not follow from counts as a name by hand.
        Assert.Equal("Button2", state.CodeName);

        // A new one's size, at the old one's corner, centred on the same spot as a text no wider than it is.
        Assert.Equal([100, 100, 100, 22], Numbers(state, "Position"));

        // No ValueChangedFcn for it to hold: a callback with only its default code goes.
        Assert.DoesNotContain(document.Callbacks, static c => !c.IsStartup);
        Assert.Null(state.Get("ValueChangedFcn"));
        document.Undo();
        Assert.Equal("GoButtonPushed", ((CallbackValue)C(document, "GoButton").Get("ButtonPushedFcn")!).Method);
        Assert.Single(document.Callbacks, static c => !c.IsStartup);

        // With code of its own it stays, bound to nothing.
        document.Callbacks.Single(static c => !c.IsStartup).Body.Add("            disp(1)");
        document.Select([C(document, "GoButton")]);
        state = document.ReplaceWith("matlab.ui.control.StateButton")!;
        Assert.Equal("GoButtonPushed", document.Callbacks.Single(static c => !c.IsStartup).Name);
        Assert.Empty(DesignCallbacks.BindingsOf(document.Design, "GoButtonPushed"));
        Assert.Null(state.Get("ButtonPushedFcn"));
    }

    [Fact]
    public void WhatTheClassesShareIsKeptAndTheRestIsNew()
    {
        DesignDocument document = App();
        Add(document, "Drop Down", 100, 100);
        DesignComponent drop = C(document, "DropDown");
        document.SetProperty(drop, "FontSize", NumericValue.Scalar(20));
        document.SetProperty(drop, "Tooltip", new CharValue("Pick"));
        document.Select([drop]);
        DesignComponent list = document.ReplaceWith("matlab.ui.control.ListBox")!;
        Assert.Equal([20d], Numbers(list, "FontSize"));
        Assert.Equal("Pick", Words(list, "Tooltip"));

        // A drop down's default items and value, which are not a list box's; its height is a new list box's.
        Assert.Equal(["Option 1", "Option 2", "Option 3", "Option 4"], ((CellValue)list.Get("Items")!).Items.Select(static i => DesignPalette.Text(i)));
        Assert.Equal("Option 1", Words(list, "Value"));
        Assert.Equal(74, Numbers(list, "Position")[3]);
    }

    [Fact]
    public void NumbersCarryOverWithinFiniteLimits()
    {
        DesignDocument document = App();
        Add(document, "Spinner", 100, 100);
        DesignComponent spinner = C(document, "Spinner");
        document.SetProperty(spinner, "Value", NumericValue.Scalar(7));

        // A spinner's infinite limits around its value, for a slider's that must be finite.
        DesignComponent slider = document.ReplaceWith(Slider)!;
        Assert.Equal([-93d, 107], Numbers(slider, "Limits"));
        Assert.Equal([7d], Numbers(slider, "Value"));

        // To a range slider: the value up to the upper limit. Back: its first.
        DesignComponent range = document.ReplaceWith("matlab.ui.control.RangeSlider")!;
        Assert.Equal([7d, 107], Numbers(range, "Value"));
        DesignComponent knob = document.ReplaceWith(Knob)!;
        Assert.Equal([7d], Numbers(knob, "Value"));
    }

    [Fact]
    public void TextFieldsAndNumericFieldsConvert()
    {
        DesignDocument document = App();
        Add(document, "Edit Field (Text)", 100, 100);
        DesignComponent field = C(document, "EditField");
        document.SetProperty(field, "Value", new CharValue("abc"));
        DesignComponent area = document.ReplaceWith("matlab.ui.control.TextArea")!;
        Assert.Equal("abc", Words(area, "Value"));

        document.SetProperty(area, "Value", CellValue.Column(new CharValue("one"), new CharValue("two")));
        DesignComponent back = document.ReplaceWith("matlab.ui.control.EditField")!;
        Assert.Equal("onetwo", Words(back, "Value"));

        // A text field to a number, and on to text: 0, and nothing.
        DesignComponent number = document.ReplaceWith("matlab.ui.control.NumericEditField")!;
        Assert.True(number.Get("Value") is null or NumericValue { Data: [0] });
        DesignComponent text = document.ReplaceWith("matlab.ui.control.EditField")!;
        Assert.Null(text.Get("Value"));
    }

    [Fact]
    public void ATreesNodesAndATabInATreeGoAcross()
    {
        DesignDocument document = App();
        Add(document, "Tree", 100, 100);
        DesignComponent tree = C(document, "Tree");
        DesignComponent[] nodes = [.. tree.Children];
        Assert.NotEmpty(nodes);
        DesignComponent check = document.ReplaceWith("matlab.ui.container.CheckBoxTree")!;
        Assert.Equal(nodes, check.Children);
        Assert.All(nodes, n => Assert.Same(check, n.Parent));
        Assert.Equal(nodes.Select(static n => n.CodeName), check.Children.Select(static n => n.CodeName));
        document.Undo();
        Assert.Equal(nodes, C(document, "Tree").Children);
        Assert.All(nodes, n => Assert.Same(tree, n.Parent));
        document.Redo();
        Assert.Equal(nodes, check.Children);
    }

    [Fact]
    public void AToolIsReplacedInItsPlaceFromItsOwnMenu()
    {
        DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Add(document, "Toolbar");
        DesignComponent toolbar = document.Toolbars[0];
        DesignComponent first = toolbar.Children[0];
        document.Select([first]);
        document.AddTool(toggle: false);
        Assert.Equal(2, toolbar.Children.Count);
        document.Select([first]);

        IReadOnlyList<DesignMenuItem> menu = gestures.MenuAt(new Point2D(0, 0));
        Assert.Equal("Replace With", menu[^1].Label);
        Assert.Equal(["Toggle Tool"], menu[^1].Items!.Select(static i => i.Label));
        menu[^1].Items![0].Run!();

        Assert.Equal("matlab.ui.container.toolbar.ToggleTool", toolbar.Children[0].Type);
        Assert.Same(toolbar.Children[0], document.Selection.Single());
        Assert.Equal(2, toolbar.Children.Count);
        document.Undo();
        Assert.Same(first, toolbar.Children[0]);
    }

    [Fact]
    public void AGridChildKeepsItsCellsAndASliderOneRow()
    {
        DesignDocument document = App();
        document.Delete([C(document, "Other")]);
        DesignComponent grid = document.Gridify(document.Design.Figure); // 8 rows of 3
        DesignComponent spinner = document.AddToGrid(Entry("Spinner"), grid, [new GridSpan(1, 2, 2, 2)], withoutLabel: true)[0];
        document.Select([spinner]);

        DesignComponent knob = document.ReplaceWith(Knob)!;
        Assert.Equal([1d, 2], Numbers(knob, "Layout.Row"));
        Assert.Equal([2d], Numbers(knob, "Layout.Column"));
        Assert.Null(knob.Get("Position"));

        DesignComponent slider = document.ReplaceWith(Slider)!;
        Assert.Equal([1d], Numbers(slider, "Layout.Row")); // _convertLayoutConstraints: its height is fixed

        // A labelled one's label goes where a new one's goes beside its cells: under a knob.
        IReadOnlyList<DesignComponent> labelled = document.AddToGrid(Entry("Spinner"), grid, [new GridSpan(5, 5, 1, 1), new GridSpan(5, 5, 2, 2)]);
        document.Select([labelled[1]]);
        DesignComponent second = document.ReplaceWith(Knob)!;
        Assert.Equal(new GridSpan(5, 5, 2, 2), document.SpanOf(second));
        Assert.Equal(new GridSpan(6, 6, 2, 2), document.SpanOf(labelled[0]));
        document.Undo();
        Assert.Equal(new GridSpan(5, 5, 1, 1), document.SpanOf(labelled[0]));
    }

    [Fact]
    public void AMemberOfAGroupStaysInItAndIsSelectedAlone()
    {
        DesignDocument document = App();
        DesignComponent button = Add(document, "Button", 100, 100)[0];
        DesignComponent other = C(document, "Other");
        document.Select([button, other]);
        Assert.NotNull(document.Group());
        DesignGroup group = document.Design.GroupOf(button)!;
        document.SelectPicks([DesignPick.Of(button)]);

        DesignComponent state = document.ReplaceWith("matlab.ui.control.StateButton")!;
        Assert.Same(group, document.Design.GroupOf(state));
        Assert.Contains(group.Members, m => ReferenceEquals(m.Component, state));
        Assert.DoesNotContain(group.Members, m => ReferenceEquals(m.Component, button));
        Assert.Same(state, document.Selection.Single());
        document.Undo();
        Assert.Same(group, document.Design.GroupOf(button));
        Assert.Same(button, document.Selection.Single());
    }

    [Fact]
    public void ALabelledComponentKeepsItsPlaceInTheStackOrder()
    {
        DesignDocument document = App();
        Add(document, "Label", 10, 10);
        Add(document, "Slider", 100, 100);
        Add(document, "Spinner", 100, 300);
        string[] before = Children(document);
        int at = Array.IndexOf(before, "Slider");
        document.Select([C(document, "Slider")]);
        DesignComponent knob = document.ReplaceWith(Knob)!;

        // R2025b takes the index before its label is deleted, and so moves it past the next one and its label
        // back: here each keeps its place, and with it the tab order.
        Assert.Equal(at, Array.IndexOf(Children(document), knob.CodeName));
        Assert.Equal(before.Length, Children(document).Length);
    }

    [Fact]
    public void TheCallbacksAreR2025bs()
    {
        using var oracle = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignReplacementOracle.json")));
        int scenes = 0;
        foreach (System.Text.Json.JsonElement r in oracle.RootElement.GetProperty("callbacks").EnumerateArray())
        {
            string source = r.GetProperty("source").GetString()!;
            string target = r.GetProperty("target").GetString()!;
            string property = r.GetProperty("property").GetString()!;
            string name = r.GetProperty("name").GetString()!;
            string codeName = r.GetProperty("codeName").GetString()!;
            bool tool = source.Contains("toolbar", StringComparison.Ordinal);
            var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
            figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
            var component = new DesignComponent(codeName, DesignCatalog.R2025b.ByType(source)!);
            if (tool)
            {
                var toolbar = new DesignComponent("Toolbar", DesignCatalog.R2025b.ByType("matlab.ui.container.Toolbar")!);
                toolbar.Add(component);
                figure.Add(toolbar);
            }
            else
            {
                component.Set("Position", NumericValue.Row(50, 50, 100, 22));
                figure.Add(component);
            }

            DesignDocument document = DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), new AppDesign(figure))!, null);
            DesignComponent held = document.Design.Find(codeName)!;
            Assert.Null(document.AddCallback(held, property, name));
            DesignCallback callback = document.Callbacks.Single(c => c.Name == name);
            if (!r.GetProperty("isDefaultCode").GetBoolean())
            {
                callback.Body.Add("            disp(1)");
            }

            if (!r.GetProperty("isDefaultName").GetBoolean())
            {
                callback.GeneratedName = "somethingElse";
            }

            document.Select([held]);
            DesignComponent made = document.ReplaceWith(target)!;
            System.Text.Json.JsonElement want = r.GetProperty("out");
            Assert.Equal(want.GetProperty("codeName").GetString(), made.CodeName);
            var ours = document.Callbacks.Where(static c => !c.IsStartup)
                .Select(c => (c.Name, Bound: DesignCallbacks.BindingsOf(document.Design, c.Name).Select(static b => b.Property).ToArray())).ToArray();
            System.Text.Json.JsonElement[] theirs = want.GetProperty("callbacks").EnumerateArray().ToArray();
            Assert.Equal(theirs.Length, ours.Length);
            for (int i = 0; i < theirs.Length; i++)
            {
                // R2025b's own name counted as taken, which makes it a 2 more: not kept.
                string expected = theirs[i].GetProperty("name").GetString()!;
                Assert.Equal(expected == name + "2" ? name : expected, ours[i].Name);
                Assert.Equal(theirs[i].GetProperty("bound").EnumerateArray().Select(static b => b.GetString()!), ours[i].Bound);
            }

            scenes++;
        }

        Assert.Equal(52, scenes);
    }
}
