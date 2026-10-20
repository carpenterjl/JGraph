using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS3 of the designer plan: App Designer's component library and inspector, its naming of new and
/// renamed components, and the edits that add, delete and set them - each through the runtime's own
/// component, read back, and undone.
/// </summary>
[Collection("JG facade")]
public class AppDesignerPaletteTests(ITestOutputHelper output)
{
    private static DesignDocument Blank() => DesignDocument.FromText(AppDesignerDocument.NewAppText(), null);

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    /// <summary>The generated lines of one component, unindented, its comment left out.</summary>
    private static string[] Lines(DesignDocument document, string codeName) =>
        DesignCodeWriter.ComponentCode(document.Design.Find(codeName)!, document.Style).ToArray();

    // --- the library ---------------------------------------------------------------------------------

    /// <summary>
    /// The library's four sections come in App Designer's order, and each section's entries are sorted
    /// by name as its view sorts them (JavaScript's code-unit order: HTML before Hyperlink).
    /// </summary>
    [Fact]
    public void TheLibraryListsAppDesignersEntriesInItsSectionsSortedByName()
    {
        DesignPalette palette = DesignPalette.R2025b;
        Assert.Equal(["Common", "Containers", "Figure Tools", "Instrumentation"], DesignPalette.Categories);
        Assert.Equal(39, palette.Entries.Count);
        Assert.Equal(DesignPalette.Categories, palette.Entries.Select(static e => e.Category).Distinct());
        foreach (string category in DesignPalette.Categories)
        {
            string[] names = palette.Entries.Where(e => e.Category == category).Select(static e => e.Name).ToArray();
            Assert.Equal(names.Order(StringComparer.Ordinal), names);
        }

        Assert.Equal(
            ["Context Menu", "Menu Bar", "Toolbar"],
            palette.Entries.Where(static e => e.Category == "Figure Tools").Select(static e => e.Name));
        Assert.All(palette.Entries, e => Assert.NotNull(palette.Catalog.ByType(e.Type)));
        Assert.All(palette.Entries, static e => Assert.Null(e.NotYet));
    }

    /// <summary>
    /// What each class's design-time component holds is what its adapter gives it in R2025b (probe
    /// ds3_palette): a panel's size and title, a table's four columns and no row names, an axes'
    /// labels; a button only its size.
    /// </summary>
    [Fact]
    public void ADesignTimeComponentHoldsWhatItsAdapterGivesIt()
    {
        DesignPalette palette = DesignPalette.R2025b;
        Assert.Equal(new Size2D(260, 221), palette.DesignTimeSize("matlab.ui.container.Panel"));
        Assert.Equal(new Size2D(100, 22), palette.DesignTimeSize("matlab.ui.control.Button"));
        Assert.Equal(new Size2D(150, 3), palette.DesignTimeSize("matlab.ui.control.Slider"));
        Assert.Equal(new Size2D(302, 185), palette.DesignTimeSize("matlab.ui.control.Table"));

        IReadOnlyList<KeyValuePair<string, DesignValue>> panel = palette.DesignTimeValues("matlab.ui.container.Panel");
        Assert.Equal("'Panel'", DesignValueText.Format(Assert.Single(panel, p => p.Key == "Title").Value));
        IReadOnlyList<KeyValuePair<string, DesignValue>> table = palette.DesignTimeValues("matlab.ui.control.Table");
        Assert.Equal("{'Column 1'; 'Column 2'; 'Column 3'; 'Column 4'}", DesignValueText.Format(table.Single(p => p.Key == "ColumnName").Value));
        IReadOnlyList<KeyValuePair<string, DesignValue>> axes = palette.DesignTimeValues("matlab.ui.control.UIAxes");
        Assert.Equal(["TitleString", "XLabelString", "YLabelString", "ZLabelString"], axes.Select(static p => p.Key));
        Assert.Empty(palette.DesignTimeValues("matlab.ui.control.Button"));
    }

    /// <summary>
    /// A new label is as wide as App Designer makes it: its text's width and the larger of 5 and 3% of
    /// it more, against labels App Designer made in apps R2025b ships (IbisAmiManager).
    /// </summary>
    [Theory]
    [InlineData("Edit Field", 56)]
    [InlineData("IBIS Model Name", 100)]
    [InlineData("Target directory", 88)]
    [InlineData("Tx and Rx corner percentage", 161)]
    public void ANewLabelIsSizedToItsTextAsAppDesignerSizesIt(string text, double width)
    {
        Size2D size = DesignPalette.LabelSize(text);
        output.WriteLine($"{text}: {size.Width} x {size.Height} (text {JGraph.Core.Model.UiFit.TextWidth([text], "Helvetica", 12, false, false)})");
        Assert.Equal(width, size.Width);
        Assert.Equal(22, size.Height);
    }

    // --- naming --------------------------------------------------------------------------------------

    /// <summary>
    /// A new component's text and code name count up as App Designer's do: the first is the class's,
    /// the next takes 2, a labelled component's label is named after it, and a code name already
    /// taken gets an _N.
    /// </summary>
    [Fact]
    public void NewComponentsAreNamedAsAppDesignerNamesThem()
    {
        Assert.Equal(("Button", "Button"), DesignNames.ForNew("matlab.ui.control.Button", ["UIFigure"], ["MATLAB App"]));
        Assert.Equal(("Button2", "Button2"), DesignNames.ForNew("matlab.ui.control.Button", ["UIFigure", "Button"], ["MATLAB App", "Button"]));
        Assert.Equal(("EditField", (string?)null), DesignNames.ForNew("matlab.ui.control.EditField", ["UIFigure"], []));
        Assert.Equal(("EditField3", (string?)null), DesignNames.ForNew("matlab.ui.control.EditField", ["EditField", "EditField2", "EditFieldLabel"], []));
        Assert.Equal(("EditField", "EditFieldLabel", "Edit Field"), DesignNames.ForNewLabeled("matlab.ui.control.EditField", ["UIFigure"], ["MATLAB App"]));
        Assert.Equal(
            ("EditField2", "EditField2Label", "Edit Field2"),
            DesignNames.ForNewLabeled("matlab.ui.control.NumericEditField", ["UIFigure", "EditField", "EditFieldLabel"], ["MATLAB App", "Edit Field"]));
        Assert.Equal("Menu_2", DesignNames.AppendUniqueIncrement(["UIFigure", "Menu", "Menu2"], "Menu", distinct: true));
        Assert.Equal("Menu_3", DesignNames.AppendUniqueIncrement(["Menu", "Menu_2"], "Menu", distinct: true));

        // App Designer's count: a name holding the text and ending in digits raises it, whatever is between.
        Assert.Equal("Button3", DesignNames.GenerateVariableName(["Button Group2"], "Button", keepWhole: true));
    }

    /// <summary>
    /// A component named from its text follows its text; one named by hand keeps its name; and a
    /// typed name must be a MATLAB name, not a keyword, not too long and not taken.
    /// </summary>
    [Fact]
    public void ACodeNameFollowsItsTextUnlessItWasNamedByHand()
    {
        Assert.False(DesignNames.IsNamedByHand("Button", "Button", "Button", "Button"));
        Assert.False(DesignNames.IsNamedByHand("Button2", "Button", "Button2_3", "Button"));
        Assert.True(DesignNames.IsNamedByHand("Button", "Button", "OKButton", "Button"));
        Assert.Equal("GoButton", DesignNames.ForEditedText("Go", "Button", "Button", "Button", ["UIFigure", "Button"]));
        Assert.Equal("StartStopButton", DesignNames.ForEditedText("Start / Stop", "Button", "Button", "Button", ["Button"]));
        Assert.Equal("GoButton_2", DesignNames.ForEditedText("Go", "Button", "Button2", "Button", ["GoButton", "Button2"]));
        Assert.Equal("Button2", DesignNames.ForEditedText("Button2", "Button", "Button2", "Button", ["Button2"]));

        string[] inUse = ["UIFigure", "Button", "startupFcn"];
        Assert.Null(DesignNames.WhyNotName("GoButton", "Button", inUse));
        Assert.Null(DesignNames.WhyNotName("Button", "Button", inUse));
        Assert.NotNull(DesignNames.WhyNotName("end", "Button", inUse));
        Assert.NotNull(DesignNames.WhyNotName("2Button", "Button", inUse));
        Assert.NotNull(DesignNames.WhyNotName("Go Button", "Button", inUse));
        Assert.NotNull(DesignNames.WhyNotName("startupFcn", "Button", inUse));
        Assert.NotNull(DesignNames.WhyNotName(new string('a', 64), "Button", inUse));
    }

    // --- adding --------------------------------------------------------------------------------------

    /// <summary>
    /// A button added to a blank app is App Designer's: named Button, its default text not written,
    /// its box's top-left corner where it was dropped; a second is Button2 with its text written.
    /// Undo takes it out of the design and the canvas.
    /// </summary>
    [Fact]
    public void AButtonAddedToABlankAppIsNamedAndPlacedAsAppDesignerAddsIt()
    {
        using DesignDocument document = Blank();
        IReadOnlyList<DesignComponent> added = document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 30));
        DesignComponent button = Assert.Single(added);
        Assert.Equal(
            ["app.Button = uibutton(app.UIFigure, 'push');", "app.Button.Position = [21 429 100 22];"],
            Lines(document, "Button"));
        Assert.Equal([button], document.Selection);
        Assert.Equal(new Rect2D(20, 30, 100, 22), document.BoxOf(button)!.Value.Box);

        document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 70));
        Assert.Equal(
            ["app.Button2 = uibutton(app.UIFigure, 'push');", "app.Button2.Position = [21 389 100 22];", "app.Button2.Text = 'Button2';"],
            Lines(document, "Button2"));

        document.Undo();
        Assert.Null(document.Design.Find("Button2"));
        Assert.DoesNotContain(document.Boxes, b => b.Component.CodeName == "Button2");
        document.Redo();
        Assert.NotNull(document.BoxOf(document.Design.Find("Button2")!));
    }

    /// <summary>
    /// A labelled entry makes its label first, right-aligned and sized to its text, and its component
    /// 15 pixels to the right of it, level with it; a second is numbered as App Designer numbers it.
    /// </summary>
    [Fact]
    public void AnEditFieldComesWithItsLabelBesideIt()
    {
        using DesignDocument document = Blank();
        IReadOnlyList<DesignComponent> added = document.Add(Entry("Edit Field (Text)"), document.Design.Figure, new Point2D(30, 40));
        Assert.Equal(["EditFieldLabel", "EditField"], added.Select(static c => c.CodeName));
        Assert.Equal(
            [
                "app.EditFieldLabel = uilabel(app.UIFigure);",
                "app.EditFieldLabel.HorizontalAlignment = 'right';",
                "app.EditFieldLabel.Position = [31 419 56 22];",
                "app.EditFieldLabel.Text = 'Edit Field';",
            ],
            Lines(document, "EditFieldLabel"));
        Assert.Equal(
            ["app.EditField = uieditfield(app.UIFigure, 'text');", "app.EditField.Position = [102 419 100 22];"],
            Lines(document, "EditField"));

        IReadOnlyList<DesignComponent> second = document.Add(Entry("Edit Field (Numeric)"), document.Design.Figure, new Point2D(30, 80));
        Assert.Equal(["EditField2Label", "EditField2"], second.Select(static c => c.CodeName));
        Assert.Equal("'Edit Field2'", DesignValueText.Format(second[0].Get("Text")!));
    }

    /// <summary>
    /// A list box's label is level with its top (App Designer's west-top place, 2 pixels down), and a
    /// knob's is under it and centred, 15 pixels below its ticks.
    /// </summary>
    [Fact]
    public void AListBoxsLabelIsLevelWithItsTopAndAKnobsIsUnderIt()
    {
        using DesignDocument document = Blank();
        IReadOnlyList<DesignComponent> list = document.Add(Entry("List Box"), document.Design.Figure, new Point2D(10, 10));
        double[] label = ((NumericValue)list[0].Get("Position")!).Data.ToArray();
        double[] box = ((NumericValue)list[1].Get("Position")!).Data.ToArray();
        Assert.Equal(box[1] + box[3] - 2, label[1] + label[3]); // the label's top 2 pixels below the list's
        Assert.Equal(label[0] + label[2] + DesignPalette.LabelOffset, box[0]);

        IReadOnlyList<DesignComponent> knob = document.Add(Entry("Knob"), document.Design.Figure, new Point2D(300, 10));
        double[] under = ((NumericValue)knob[0].Get("Position")!).Data.ToArray();
        double[] dial = ((NumericValue)knob[1].Get("Position")!).Data.ToArray();
        Assert.Equal("'center'", DesignValueText.Format(knob[0].Get("HorizontalAlignment")!));
        Assert.Equal(under[1] + under[3] + DesignPalette.LabelOffset + 19, dial[1]); // above the label, past the knob's lower ticks
        Assert.InRange(under[0] + (under[2] / 2) - (dial[0] - 26 + (114 / 2.0)), -0.5, 0.5); // centred under its ticks' box
    }

    /// <summary>
    /// A radio button group comes with App Designer's three buttons, Button to Button3, where its
    /// definition puts them, the first selected; a tab group with Tab and Tab2; a tree with a node
    /// holding three more.
    /// </summary>
    [Fact]
    public void CompoundEntriesComeWithTheComponentsAppDesignerPutsInThem()
    {
        using DesignDocument document = Blank();
        DesignComponent group = document.Add(Entry("Radio Button Group"), document.Design.Figure, new Point2D(10, 10)).Single();
        Assert.Equal("ButtonGroup", group.CodeName);
        Assert.Equal(["Button", "Button2", "Button3"], group.Children.Select(static c => c.CodeName));
        Assert.Equal(
            [
                "app.ButtonGroup = uibuttongroup(app.UIFigure);",
                "app.ButtonGroup.Title = 'Button Group';",
                "app.ButtonGroup.Position = [11 365 123 106];",
            ],
            Lines(document, "ButtonGroup"));
        Assert.Equal(
            [
                "app.Button = uiradiobutton(app.ButtonGroup);",
                "app.Button.Text = 'Button';",
                "app.Button.Position = [11 60 58 22];",
                "app.Button.Value = true;",
            ],
            Lines(document, "Button"));
        Assert.DoesNotContain(Lines(document, "Button2"), l => l.Contains(".Value", StringComparison.Ordinal));

        DesignComponent tabs = document.Add(Entry("Tab Group"), document.Design.Figure, new Point2D(200, 10)).Single();
        Assert.Equal(["Tab", "Tab2"], tabs.Children.Select(static c => c.CodeName));
        Assert.Equal("'Tab2'", DesignValueText.Format(tabs.Children[1].Get("Title")!));

        DesignComponent tree = document.Add(Entry("Tree"), document.Design.Figure, new Point2D(10, 200)).Single();
        DesignComponent node = Assert.Single(tree.Children);
        Assert.Equal("Node", node.CodeName);
        Assert.Equal(["Node2", "Node3", "Node4"], node.Children.Select(static c => c.CodeName));
        Assert.Empty(document.Runtime.Problems);
    }

    /// <summary>
    /// The figure tools go in the figure whatever they are dropped on: a menu bar's two menus first
    /// among its children, a context menu last with its two items, which take an _N where the menu
    /// bar's names are taken, as App Designer's do.
    /// </summary>
    [Fact]
    public void FigureToolsGoInTheFigure()
    {
        using DesignDocument document = Blank();
        DesignComponent panel = document.Add(Entry("Panel"), document.Design.Figure, new Point2D(10, 10)).Single();
        document.Add(Entry("Menu Bar"), panel, new Point2D(20, 20));
        Assert.Equal(["Menu", "Menu2", "Panel"], document.Design.Figure.Children.Select(static c => c.CodeName));
        Assert.Equal("'Menu2'", DesignValueText.Format(document.Design.Find("Menu2")!.Get("Text")!));

        DesignComponent context = document.Add(Entry("Context Menu"), panel, new Point2D(20, 20)).Single();
        Assert.Same(document.Design.Figure, context.Parent);
        Assert.Equal(["Menu_2", "Menu2_2"], context.Children.Select(static c => c.CodeName));
        Assert.Empty(document.Runtime.Problems);
    }

    /// <summary>
    /// A component dropped into a panel goes into it, kept inside it; one that cannot be held where it
    /// is dropped goes into the nearest container that can hold it.
    /// </summary>
    [Fact]
    public void ADropIntoAPanelGoesIntoItAndIsKeptInsideIt()
    {
        using DesignDocument document = Blank();
        DesignComponent panel = document.Add(Entry("Panel"), document.Design.Figure, new Point2D(10, 10)).Single();
        Rect2D inner = document.Runtime.InnerOnSurface(panel);
        DesignComponent button = document.Add(Entry("Button"), panel, new Point2D(inner.X + inner.Width - 20, inner.Y + 5)).Single();
        Assert.Same(panel, button.Parent);
        double[] position = ((NumericValue)button.Get("Position")!).Data.ToArray();
        Assert.Equal(inner.Width + 1 - 100, position[0]); // pushed back inside
        Assert.Same(panel, document.ContainerFor(Entry("Button"), panel));
        Assert.Same(panel, document.ContainerFor(Entry("Button"), button));
        Assert.Same(document.Design.Figure, document.ContainerFor(Entry("Menu Bar"), button));
    }

    /// <summary>
    /// App Designer's click and draw: with an entry chosen, a click places it at its own size with its
    /// corner there, a drag draws its size, and either ends the mode; Escape ends it with nothing
    /// added. A dropped entry lands where it is dropped.
    /// </summary>
    [Fact]
    public void ClickAndDrawPlacesOrDrawsTheChosenEntry()
    {
        using DesignDocument document = Blank();
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        gestures.DrawEntry = Entry("Button");
        Assert.Equal(DesignCursor.Cross, gestures.CursorAt(new Point2D(5, 5)));
        Assert.True(gestures.Press(new Point2D(50, 60), DesignButton.Left, DesignModifiers.None));
        gestures.Release();
        Assert.Null(gestures.DrawEntry);
        Assert.Equal(new Rect2D(50, 60, 100, 22), document.BoxOf(document.Design.Find("Button")!)!.Value.Box);

        gestures.DrawEntry = Entry("Panel");
        gestures.Press(new Point2D(400, 300), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(300, 200), DesignModifiers.None);
        Assert.Equal(new Rect2D(300, 200, 100, 100), gestures.Drawn);
        gestures.Release();
        Assert.Equal(new Rect2D(300, 200, 100, 100), document.BoxOf(document.Design.Find("Panel")!)!.Value.Box);

        gestures.DrawEntry = Entry("Label");
        Assert.True(gestures.KeyDown(DesignKey.Escape, DesignModifiers.None));
        Assert.Null(gestures.DrawEntry);

        gestures.ShowGrid = true;
        IReadOnlyList<DesignComponent> dropped = gestures.Drop(Entry("Check Box"), new Point2D(123, 87));
        Rect2D landed = document.BoxOf(Assert.Single(dropped))!.Value.Box;
        Assert.Equal((120.0, 90.0), (landed.X, landed.Y));

        document.Select([document.Design.Find("Panel")!]);
        Assert.True(gestures.KeyDown(DesignKey.Delete, DesignModifiers.None));
        Assert.Null(document.Design.Find("Panel"));
        document.Select([]);
        Assert.False(gestures.KeyDown(DesignKey.Delete, DesignModifiers.None));
    }

    // --- deleting ------------------------------------------------------------------------------------

    /// <summary>
    /// Deleting takes a component and what it holds out of the design and the canvas as one edit; a
    /// context menu deleted is taken off the components it was assigned to; undo puts all of it back.
    /// The figure cannot be deleted.
    /// </summary>
    [Fact]
    public void DeletingTakesAComponentOutAndUndoPutsItBack()
    {
        using DesignDocument document = Blank();
        DesignComponent panel = document.Add(Entry("Panel"), document.Design.Figure, new Point2D(10, 10)).Single();
        DesignComponent button = document.Add(Entry("Button"), panel, new Point2D(30, 60)).Single();
        DesignComponent context = document.Add(Entry("Context Menu"), document.Design.Figure, new Point2D(0, 0)).Single();
        Assert.Null(document.SetProperty(button, "ContextMenu", new ComponentsValue(["ContextMenu"], bracketed: false)));
        Assert.Contains("app.Button.ContextMenu = app.ContextMenu;", DesignCodeWriter.Method(document.Design, document.Style).Select(static l => l.Trim()));

        document.Delete([context, document.Design.Figure]);
        Assert.Null(document.Design.Find("ContextMenu"));
        Assert.Null(button.Get("ContextMenu"));
        Assert.Same(document.Design.Figure, document.Design.Find("UIFigure"));
        document.Undo();
        Assert.Same(context, document.Design.Find("ContextMenu"));
        Assert.Equal("app.ContextMenu", DesignValueText.Format(button.Get("ContextMenu")!));

        document.Delete([panel, button]);
        Assert.Null(document.Design.Find("Panel"));
        Assert.Null(document.Design.Find("Button"));
        Assert.Equal([document.Design.Figure], document.Selection);
        Assert.Null(document.BoxOf(panel));
        document.Undo();
        Assert.Same(panel, button.Parent);
        Assert.NotNull(document.BoxOf(button));
        Assert.Empty(document.Runtime.Problems);
    }

    // --- properties ----------------------------------------------------------------------------------

    /// <summary>
    /// A text set on a button goes through its component; the button, named from its text, is renamed
    /// as App Designer renames it, in the same edit; undo puts back both.
    /// </summary>
    [Fact]
    public void ABetterTextRenamesAButtonNamedFromItsText()
    {
        using DesignDocument document = Blank();
        DesignComponent button = document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 30)).Single();
        Assert.Null(document.SetProperty(button, "Text", new CharValue("Go")));
        Assert.Equal("GoButton", button.CodeName);
        Assert.Contains("app.GoButton.Text = 'Go';", Lines(document, "GoButton"));
        Assert.Equal([button], document.Selection);

        document.Undo();
        Assert.Equal("Button", button.CodeName);
        Assert.Equal(["app.Button = uibutton(app.UIFigure, 'push');", "app.Button.Position = [21 429 100 22];"], Lines(document, "Button"));

        // A name given by hand stays when the text changes.
        Assert.Null(document.Rename(button, "OKButton"));
        Assert.Null(document.SetProperty(button, "Text", new CharValue("Stop")));
        Assert.Equal("OKButton", button.CodeName);
    }

    /// <summary>
    /// A labelled component's label's text names the component, and the label after it, as App
    /// Designer renames the pair.
    /// </summary>
    [Fact]
    public void ALabelsTextRenamesItsComponentAndItself()
    {
        using DesignDocument document = Blank();
        IReadOnlyList<DesignComponent> added = document.Add(Entry("Edit Field (Numeric)"), document.Design.Figure, new Point2D(30, 40));
        Assert.Same(added[1], document.LabelledBy(added[0]));
        Assert.Null(document.SetProperty(added[0], "Text", new CharValue("Speed")));
        Assert.Equal("SpeedEditField", added[1].CodeName);
        Assert.Equal("SpeedEditFieldLabel", added[0].CodeName);
        document.Undo();
        Assert.Equal(["EditFieldLabel", "EditField"], added.Select(static c => c.CodeName));
    }

    /// <summary>
    /// The design keeps what the component kept: a string set as text is held as the char the
    /// component holds; a value set to its default is unset; a drop-down's value follows its items;
    /// a radio button selected takes the selection from its group's other.
    /// </summary>
    [Fact]
    public void TheDesignKeepsWhatTheComponentKept()
    {
        using DesignDocument document = Blank();
        DesignComponent label = document.Add(Entry("Label"), document.Design.Figure, new Point2D(10, 10)).Single();
        Assert.Null(document.SetProperty(label, "FontColor", NumericValue.Row(1, 0, 0)));
        Assert.Null(document.SetProperty(label, "Tooltip", new StringValue("tip")));
        Assert.IsType<CharValue>(label.Get("Tooltip"));
        Assert.Null(document.SetProperty(label, "FontSize", NumericValue.Scalar(12)));
        Assert.Null(label.Get("FontSize"));

        IReadOnlyList<DesignComponent> drop = document.Add(Entry("Drop Down"), document.Design.Figure, new Point2D(10, 60));
        Assert.Null(document.SetProperty(drop[1], "Items", CellValue.Row(new CharValue("a"), new CharValue("b"))));
        Assert.Equal("'a'", DesignValueText.Format(drop[1].Get("Value")!));
        document.Undo();
        Assert.Null(drop[1].Get("Value"));
        Assert.Null(drop[1].Get("Items"));

        DesignComponent group = document.Add(Entry("Radio Button Group"), document.Design.Figure, new Point2D(10, 200)).Single();
        Assert.Null(document.SetProperty(group.Children[1], "Value", NumericValue.Logical(true)));
        Assert.Null(group.Children[0].Get("Value"));
        Assert.Equal("true", DesignValueText.Format(group.Children[1].Get("Value")!));
        Assert.Equal([group.Children[1]], document.Selection);

        Assert.NotNull(document.SetProperty(label, "FontSize", new CharValue("big")));
        Assert.Empty(document.Runtime.Problems);
    }

    /// <summary>
    /// A rename takes a valid name not in use: the design's references and the code's <c>app.Name</c>
    /// follow; a name in use, a keyword or an invalid name is refused with nothing changed.
    /// </summary>
    [Fact]
    public void ARenameTakesAFreeNameAndTheCodeFollows()
    {
        // A startup function in the callbacks block, where App Designer keeps it (DS5): a function
        // put anywhere else is in a block App Designer writes again from the design.
        string text = AppDesignerDocument.NewAppText().Replace(
            "    % Component initialization",
            "    % Callbacks that handle component events\n    methods (Access = private)\n\n        % Code that executes after component creation\n"
            + "        function startupFcn(app)\n            app.UIFigure.Name = 'x';\n        end\n    end\n\n    % Component initialization",
            StringComparison.Ordinal);
        using DesignDocument document = DesignDocument.FromText(text, null);
        Assert.Contains("startupFcn", document.NamesInUse());
        Assert.Contains("app1", document.NamesInUse());
        Assert.NotNull(document.Rename(document.Design.Figure, "startupFcn"));
        Assert.NotNull(document.Rename(document.Design.Figure, "for"));
        Assert.NotNull(document.Rename(document.Design.Figure, "My Figure"));
        Assert.Null(document.Rename(document.Design.Figure, "MainFigure"));
        Assert.Contains("app.MainFigure.Name = 'x';", document.CurrentText, StringComparison.Ordinal);
        Assert.Contains("registerApp(app, app.MainFigure)", document.CurrentText, StringComparison.Ordinal);
        document.Undo();
        Assert.Contains("app.UIFigure.Name = 'x';", document.CurrentText, StringComparison.Ordinal);
        Assert.Equal("UIFigure", document.Design.Figure.CodeName);
    }

    /// <summary>
    /// The code is written under the figure's theme: a dark app's components compare their values with
    /// the dark theme's defaults, as App Designer's generator does under the figure's theme.
    /// </summary>
    [Fact]
    public void TheCodeIsWrittenUnderTheFiguresTheme()
    {
        using DesignDocument document = Blank();
        Assert.Equal(DesignTheme.Light, document.Theme);
        Assert.Null(document.SetProperty(document.Design.Figure, "Theme", new CharValue("dark")));
        Assert.Equal(DesignTheme.Dark, document.Theme);
        Assert.Contains("app.UIFigure.Theme = 'dark';", Lines(document, "UIFigure"));
        Assert.Equal(DesignTheme.Dark, DesignCodeReader.Read(DesignCodeWriter.Splice(document.Text, document.Design, document.Style)!).Style.Theme);
        document.Undo();
        Assert.Equal(DesignTheme.Light, document.Theme);
    }

    /// <summary>
    /// Components added, set, renamed and deleted are saved into the app - its code and App Designer's
    /// component tree - and the app opens again with them, its code written as the designer writes it.
    /// </summary>
    [Fact]
    public void WhatIsAddedSetAndRenamedIsSavedIntoTheApp()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "Built.mlapp");
            AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText());
            using (DesignDocument document = DesignDocument.Open(path))
            {
                IReadOnlyList<DesignComponent> field = document.Add(Entry("Edit Field (Numeric)"), document.Design.Figure, new Point2D(20, 20));
                Assert.Null(document.SetProperty(field[0], "Text", new CharValue("Speed")));
                DesignComponent group = document.Add(Entry("Radio Button Group"), document.Design.Figure, new Point2D(300, 20)).Single();
                DesignComponent context = document.Add(Entry("Context Menu"), document.Design.Figure, new Point2D(0, 0)).Single();
                Assert.Null(document.SetProperty(group, "ContextMenu", new ComponentsValue([context.CodeName], bracketed: false)));
                Assert.Null(document.Rename(group, "ModeGroup"));
                DesignComponent extra = document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 200)).Single();
                document.Delete([extra]);
                string status = document.Save();
                output.WriteLine(status);
                Assert.DoesNotContain("leaves out", status, StringComparison.Ordinal);
            }

            using DesignDocument reopened = DesignDocument.Open(path);
            Assert.Empty(reopened.ReadProblems);
            Assert.Equal(
                ["UIFigure", "SpeedEditFieldLabel", "SpeedEditField", "ModeGroup", "Button", "Button2", "Button3", "ContextMenu", "Menu", "Menu2"],
                reopened.Design.Components.Select(static c => c.CodeName));
            Assert.Equal("app.ContextMenu", DesignValueText.Format(reopened.Design.Find("ModeGroup")!.Get("ContextMenu")!));
            Assert.Equal(
                DesignCodeWriter.Method(reopened.Design, reopened.Style),
                DesignCodeWriter.Method(DesignCodeReader.Read(reopened.Text).Design!, reopened.Style));
            Assert.Empty(reopened.Runtime.Problems);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// An app built from every entry the library makes, laid out down columns of a large figure, saves
    /// and opens again whole, every component made. With <c>JGRAPH_DS6_OUT</c> it is written to
    /// <c>new/</c> there, with its createComponents beside it, for R2025b's App Designer to read
    /// (<c>ds6_verify</c>, <c>DS6_PART=new</c>).
    /// </summary>
    [Fact]
    public void AnAppBuiltFromEveryLibraryEntrySavesAndOpensWhole()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), "jgraph-ds3-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(outFolder is null ? temp : Path.Combine(outFolder, "new"), "90_PaletteApp");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "PaletteApp.mlapp");
            AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText());
            int made = 0;
            using (DesignDocument document = DesignDocument.Open(path))
            {
                Assert.Null(document.SetProperty(document.Design.Figure, "Position", NumericValue.Row(100, 100, 1400, 900)));
                double x = 10;
                double y = 10;
                // A Grid Layout lays out the container it is added to: it has its own app (AppDesignerGridTests).
                foreach (DesignPaletteEntry entry in document.Palette.Entries.Where(static e => e.Type != DesignDocument.GridType))
                {
                    IReadOnlyList<DesignComponent> added = document.Add(entry, document.Design.Figure, new Point2D(x, y));
                    made += added.Count;
                    double bottom = added.Select(c => document.BoxOf(c)?.Box is { } b ? b.Y + b.Height : y).DefaultIfEmpty(y).Max();
                    y = bottom + 15;
                    if (y > 700)
                    {
                        x += 330;
                        y = 10;
                    }
                }

                Assert.Empty(document.Runtime.Problems);
                Assert.Null(document.SetProperty(document.Design.Find("Button")!, "Text", new CharValue("Run")));
                document.Save();
            }

            using DesignDocument reopened = DesignDocument.Open(path);
            Assert.Empty(reopened.ReadProblems);
            Assert.Empty(reopened.Runtime.Problems);
            Assert.NotNull(reopened.Design.Find("RunButton"));
            output.WriteLine($"{made} components added; {reopened.Design.Components.Count()} in the app");
            if (outFolder is not null)
            {
                File.WriteAllLines(Path.Combine(folder, "PaletteApp__code.m"), DesignCodeWriter.Method(reopened.Design, reopened.Style));
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// The inspector shows App Designer's groups for each class in its order, a button's starting with
    /// its Button group, and edits what the generator writes; the rest - modes, read-only properties -
    /// it shows only.
    /// </summary>
    [Fact]
    public void TheInspectorShowsAppDesignersGroups()
    {
        IReadOnlyList<DesignInspectorGroup> button = DesignInspector.R2025b.GroupsOf("matlab.ui.control.Button");
        Assert.Equal(
            ["Button", "Font and Color", "Interactivity", "Position", "Callback Execution Control", "Parent / Child", "Identifiers"],
            button.Select(static g => g.Title));
        Assert.Equal(["Text", "Interpreter", "WordWrap", "HorizontalAlignment", "VerticalAlignment", "Icon", "IconAlignment"],
            button[0].Rows.Select(static r => r.Editors.Single().Name));
        Assert.Equal(DesignEditorKind.Text, button[0].Rows[0].Editors[0].Kind);
        Assert.Equal(DesignEditorKind.OnOff, button[0].Rows[2].Editors[0].Kind);
        Assert.Equal(["left", "center", "right"], button[0].Rows[3].Editors[0].Choices);
        Assert.Equal(DesignEditorKind.Color, button[1].Rows.Single(r => r.Editors[0].Name == "FontColor").Editors[0].Kind);
        Assert.Equal(DesignEditorKind.Component, button[2].Rows.Single(r => r.Editors[0].Name == "ContextMenu").Editors[0].Kind);
        Assert.DoesNotContain(button.SelectMany(static g => g.Rows), r => r.Editors.Any(e => e.Name == "ForegroundColor"));

        DesignInspectorRow items = DesignInspector.R2025b.GroupsOf("matlab.ui.control.DropDown")[0].Rows[0];
        Assert.Equal(["Value", "Items"], items.Editors.Select(static e => e.Name));
        Assert.Contains(
            DesignInspector.R2025b.GroupsOf("matlab.ui.control.UIAxes").SelectMany(static g => g.Rows).SelectMany(static r => r.Editors),
            e => e.Name == "XLimMode" && e.Kind == DesignEditorKind.ReadOnly);
        Assert.All(DesignPalette.R2025b.Catalog.Classes, c => Assert.NotEmpty(DesignInspector.R2025b.GroupsOf(c.Type)));
    }
}
