using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W5b on an app: in-place text editing - F2 and a typed
/// character on the one selected component, a double click on any the canvas edits, what each
/// editor opens with and saves as one edit, a component's several places one at a time with Tab
/// between them, a labelled component's label deleted when emptied, a hyperlink's URL, the keys of
/// each editor. Which place a key and Tab open, and how its text aligns, are checked against
/// R2025b's own controllers by <see cref="Designer.DesignInPlaceOracleTests"/>.
/// </summary>
[Collection("JG facade")]
public class AppDesignerInPlaceTests
{
    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static Point2D Centre(Rect2D r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        component.Set("Position", NumericValue.Row(position));
        return component;
    }

    /// <summary>A 640-by-480 figure with one component of each kind the tests edit.</summary>
    private static DesignDocument App()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Label", "Label", 20, 420, 100, 22));
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 380, 100, 23));
        figure.Add(Make("matlab.ui.control.DropDown", "DropDown", 20, 340, 100, 22));
        figure.Add(Make("matlab.ui.control.NumericEditField", "NumericEditField", 20, 300, 100, 22));
        figure.Add(Make("matlab.ui.control.Hyperlink", "Hyperlink", 20, 260, 100, 22));
        figure.Add(Make("matlab.ui.control.TextArea", "TextArea", 20, 150, 150, 60));
        figure.Add(Make("matlab.ui.control.Slider", "Slider", 200, 420, 150, 3));
        figure.Add(Make("matlab.ui.control.Switch", "Switch", 220, 340, 45, 20));
        DesignComponent latex = Make("matlab.ui.control.Label", "Formula", 200, 250, 100, 22);
        latex.Set("Interpreter", new CharValue("latex"));
        figure.Add(latex);
        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 350, 50, 250, 200);
        panel.Set("Title", new CharValue("Panel"));
        figure.Add(panel);
        panel.Add(Make("matlab.ui.control.Button", "Inner", 20, 20, 100, 23));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static Rect2D Box(DesignDocument document, string name) => document.BoxOf(C(document, name))!.Value.Box;

    private static void Select(DesignDocument document, params string[] names) =>
        document.SelectPicks(names.Select(n => DesignPick.Of(C(document, n))).ToArray());

    private static string[] Texts(DesignValue? value) => value switch
    {
        CharValue c => [c.Text],
        CellValue cell => cell.Items.Select(static i => ((CharValue)i).Text).ToArray(),
        _ => throw new InvalidOperationException(value?.GetType().Name),
    };

    /// <summary>F2 on a selected label opens its text, all of it; Enter's save is one edit, more than one line a column of them.</summary>
    [Fact]
    public void F2OpensTheSelectedLabelAndASaveIsOneEdit()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent label = C(document, "Label");
        Select(document, "Label");
        Assert.Same(label, document.InPlaceTarget);
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        DesignInPlaceEdit edit = gestures.InPlace!;
        Assert.Equal(DesignInPlaceKind.MultiLine, edit.Kind);
        Assert.Equal("Text", edit.Property);
        Assert.Equal(["Label"], edit.Lines);
        Assert.Null(edit.Seed);
        Assert.Equal("Label", edit.Text);
        Assert.Equal(Box(document, "Label"), edit.Box);

        Assert.Equal((null, null), gestures.EndInPlace(save: true, "Hello\nWorld"));
        Assert.Null(gestures.InPlace);
        CellValue lines = Assert.IsType<CellValue>(document.ValueOf(label, "Text"));
        Assert.Equal((2, 1), (lines.Rows, lines.Cols));
        Assert.Equal(["Hello", "World"], Texts(lines));

        document.Undo();
        Assert.Equal(["Label"], Texts(document.ValueOf(label, "Text")));
        Assert.False(document.CanUndo);
    }

    /// <summary>A character past a space opens the selected button's editor on what was typed; a space, two selected, or a closed edit open none.</summary>
    [Fact]
    public void TypingOpensTheEditorOnWhatWasTyped()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent button = C(document, "Button");
        Select(document, "Button", "Label");
        Assert.Null(document.InPlaceTarget);
        Assert.False(gestures.TypeText("O"));

        Select(document, "Button");
        Assert.False(gestures.TypeText(" "));
        Assert.True(gestures.TypeText("O"));
        Assert.Equal("O", gestures.InPlace!.Seed);
        Assert.Equal("O", gestures.InPlace.Text);
        Assert.False(gestures.TypeText("K")); // the editor has the keys now

        gestures.EndInPlace(save: true, "OK");
        Assert.Equal(["OK"], Texts(document.ValueOf(button, "Text")));

        // Escape: nothing saved, nothing to undo but the save before.
        Assert.True(gestures.TypeText("X"));
        gestures.EndInPlace(save: false, "X");
        Assert.Equal(["OK"], Texts(document.ValueOf(button, "Text")));
    }

    /// <summary>A drop-down's items open by a double click only, each line an item; with Ctrl held a double click edits nothing.</summary>
    [Fact]
    public void ADropDownsItemsOpenOnlyByADoubleClick()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent dropDown = C(document, "DropDown");
        Select(document, "DropDown");
        Assert.Null(document.InPlaceTarget);
        Assert.False(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.False(gestures.TypeText("a"));

        Assert.False(gestures.DoubleClick(Centre(Box(document, "DropDown")), DesignModifiers.Control));
        Assert.Null(gestures.InPlace);

        Select(document, "Label");
        Assert.True(gestures.DoubleClick(Centre(Box(document, "DropDown"))));
        Assert.Equal([dropDown], document.Selection);
        DesignInPlaceEdit edit = gestures.InPlace!;
        Assert.Equal(DesignInPlaceKind.Items, edit.Kind);
        Assert.Equal(["Option 1", "Option 2", "Option 3", "Option 4"], edit.Lines);

        gestures.EndInPlace(save: true, "Red\nGreen\nBlue");
        CellValue items = Assert.IsType<CellValue>(document.ValueOf(dropDown, "Items"));
        Assert.Equal((1, 3), (items.Rows, items.Cols));
        Assert.Equal(["Red", "Green", "Blue"], Texts(items));
    }

    /// <summary>A labelled field's label is what F2 opens with the pair selected; emptied, it is deleted.</summary>
    [Fact]
    public void ALabelledFieldsLabelEmptiedIsDeleted()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        IReadOnlyList<DesignComponent> made = gestures.Drop(Entry("Edit Field (Text)"), new Point2D(450, 60));
        DesignComponent label = made.Single(static c => c.Type == "matlab.ui.control.Label");
        DesignComponent field = made.Single(static c => c.Type == "matlab.ui.control.EditField");
        Assert.NotNull(document.SelectedLabeled);
        Assert.Same(label, document.InPlaceTarget);

        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Same(label, gestures.InPlace!.Component);
        gestures.EndInPlace(save: true, string.Empty);
        Assert.Null(document.Design.Find(label.CodeName));
        Assert.Same(field, document.Design.Find(field.CodeName));

        // The field's own value is a single line.
        Select(document, field.CodeName);
        Assert.True(gestures.TypeText("a"));
        Assert.Equal(DesignInPlaceKind.SingleLine, gestures.InPlace!.Kind);
        Assert.Equal("Value", gestures.InPlace.Property);
        gestures.EndInPlace(save: true, "abc");
        Assert.Equal(["abc"], Texts(document.ValueOf(field, "Value")));
    }

    /// <summary>A numeric field opens on its value as the box shows a number, and takes a number, Inf, or nothing.</summary>
    [Fact]
    public void ANumericFieldTakesANumberOrNothing()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent field = C(document, "NumericEditField");
        Select(document, "NumericEditField");
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal(["0"], gestures.InPlace!.Lines);
        Assert.Equal(DesignInPlace.NumberPattern, gestures.InPlace.Pattern);

        gestures.EndInPlace(save: true, "abc");
        Assert.False(document.CanUndo);

        gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None);
        gestures.EndInPlace(save: true, "12.5");
        Assert.Equal([12.5], ((NumericValue)document.ValueOf(field, "Value")!).Data);

        gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None);
        Assert.Equal(["12.5"], gestures.InPlace!.Lines);
        gestures.EndInPlace(save: true, "-Inf");
        Assert.Equal([double.NegativeInfinity], ((NumericValue)document.ValueOf(field, "Value")!).Data);
    }

    /// <summary>A slider's limits open one at a time - a key the minimum - Tab goes to the other; a click edits the one under it or none.</summary>
    [Fact]
    public void ASlidersLimitsOpenOneAtATime()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent slider = C(document, "Slider");
        Select(document, "Slider");
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        DesignInPlaceEdit edit = gestures.InPlace!;
        Assert.Equal(("Minimum", 0, 2, "center"), (edit.Property, edit.Hotspot, edit.Hotspots, edit.Alignment));
        Assert.Equal(["0"], edit.Lines);
        Assert.Equal(DesignInPlace.LimitPattern, edit.Pattern);

        // Saved with Tab: the maximum opens next.
        Assert.Equal((null, 1), gestures.EndInPlace(save: true, "10", next: false));
        Assert.Equal([10.0, 100.0], document.Runtime.ReadNumbers(slider, "Limits")!);
        Assert.True(gestures.BeginInPlace(slider, hotspot: 1));
        Assert.Equal("Maximum", gestures.InPlace!.Property);
        Assert.Equal(["100"], gestures.InPlace.Lines);

        // What passes the pattern but is no number changes nothing; a maximum under the minimum is refused.
        gestures.EndInPlace(save: true, "1x5");
        Assert.Equal([10.0, 100.0], document.Runtime.ReadNumbers(slider, "Limits")!);
        gestures.BeginInPlace(slider, hotspot: 1);
        Assert.NotNull(gestures.EndInPlace(save: true, "5").Refused);
        Assert.Equal([10.0, 100.0], document.Runtime.ReadNumbers(slider, "Limits")!);

        // Where the canvas drew the labels: a double click on the maximum's edits it, beside them none.
        Rect2D box = Box(document, "Slider");
        var low = new Rect2D(box.X - 5, box.Bottom + 10, 20, 14);
        var high = new Rect2D(box.Right - 15, box.Bottom + 10, 30, 14);
        gestures.HotspotsOf = c => ReferenceEquals(c, slider) ? [low, high] : null;
        Assert.True(gestures.DoubleClick(Centre(high)));
        Assert.Equal(("Maximum", high), (gestures.InPlace!.Property, gestures.InPlace.Box));
        gestures.EndInPlace(save: false, string.Empty);
        Assert.True(gestures.DoubleClick(Centre(box)));
        Assert.Null(gestures.InPlace);
    }

    /// <summary>A switch's item is edited in its place among the others; a key opens a horizontal switch's first.</summary>
    [Fact]
    public void ASwitchsItemIsEditedAmongTheOthers()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent toggle = C(document, "Switch");
        Select(document, "Switch");
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal(("Items", 0, "right"), (gestures.InPlace!.Property, gestures.InPlace.Hotspot, gestures.InPlace.Alignment));
        Assert.Equal(["Off"], gestures.InPlace.Lines);
        Assert.Equal((null, 1), gestures.EndInPlace(save: true, "Stop", next: false));
        Assert.Equal(["Stop", "On"], Texts(document.ValueOf(toggle, "Items")));
    }

    /// <summary>A double click inside a panel edits its title, unless on a component in it the canvas edits.</summary>
    [Fact]
    public void APanelsTitleOpensFromADoubleClickInsideIt()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent panel = C(document, "Panel");
        Rect2D box = Box(document, "Panel");
        Assert.True(gestures.DoubleClick(new Point2D(box.Right - 10, box.Bottom - 10)));
        DesignInPlaceEdit edit = gestures.InPlace!;
        Assert.Equal((DesignInPlaceKind.SingleLine, "Title", "left"), (edit.Kind, edit.Property, edit.Alignment));
        Assert.Equal(["Panel"], edit.Lines);
        Assert.Equal((box.X, box.Y, box.Width), (edit.Box.X, edit.Box.Y, edit.Box.Width));
        gestures.EndInPlace(save: true, "Settings");
        Assert.Equal(["Settings"], Texts(document.ValueOf(panel, "Title")));

        Assert.True(gestures.DoubleClick(Centre(Box(document, "Inner"))));
        Assert.Equal(("Inner", "Text"), (gestures.InPlace!.Component.CodeName, gestures.InPlace.Property));
    }

    /// <summary>A hyperlink's text and URL are two edits; a URL it does not take is not saved.</summary>
    [Fact]
    public void AHyperlinksUrlIsSavedOnlyWhenItTakesIt()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent link = C(document, "Hyperlink");
        Select(document, "Hyperlink");
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal((DesignInPlaceKind.Hyperlink, string.Empty), (gestures.InPlace!.Kind, gestures.InPlace.Url));
        gestures.EndInPlace(save: true, "Docs", "matlab:doc");
        Assert.Equal(["Docs"], Texts(document.ValueOf(link, "Text")));
        Assert.True(document.ValueOf(link, "URL") is CharValue { Text: "" });

        gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None);
        gestures.EndInPlace(save: true, "Docs", "www.mathworks.com");
        Assert.True(document.ValueOf(link, "URL") is CharValue { Text: "www.mathworks.com" });
        document.Undo();
        Assert.True(document.ValueOf(link, "URL") is CharValue { Text: "" });
        Assert.Equal(["Docs"], Texts(document.ValueOf(link, "Text")));
    }

    /// <summary>A text area's lines are its value's, a column of them.</summary>
    [Fact]
    public void ATextAreaKeepsItsLines()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent area = C(document, "TextArea");
        Select(document, "TextArea");
        Assert.True(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.Equal(DesignInPlaceKind.TextArea, gestures.InPlace!.Kind);
        gestures.EndInPlace(save: true, "first\nsecond");
        Assert.Equal(["first", "second"], Texts(document.ValueOf(area, "Value")));
    }

    /// <summary>A LaTeX label, the figure and a component with no text to edit open no editor.</summary>
    [Fact]
    public void NotEveryComponentIsEditedInPlace()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Assert.False(document.CanEditInPlace(C(document, "Formula")));
        Assert.False(document.CanEditInPlace(document.Design.Figure));
        Select(document, "Formula");
        Assert.False(gestures.KeyDown(DesignKey.EditInPlace, DesignModifiers.None));
        Assert.False(gestures.DoubleClick(Centre(Box(document, "Formula"))));
        Assert.Null(gestures.InPlace);
        Assert.False(gestures.DoubleClick(new Point2D(600, 10)));
    }

    /// <summary>Each editor's keys, as R2025b's editors take them.</summary>
    [Fact]
    public void TheKeysDoWhatR2025bsEditorsDo()
    {
        static DesignInPlaceAction Key(DesignInPlaceKind kind, DesignInPlaceKey key, DesignModifiers modifiers = DesignModifiers.None, DesignInPlaceField field = DesignInPlaceField.Text) =>
            DesignInPlace.KeyAction(kind, key, modifiers, field);

        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.SingleLine, DesignInPlaceKey.Enter));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.SingleLine, DesignInPlaceKey.Enter, DesignModifiers.Shift));
        Assert.Equal(DesignInPlaceAction.SaveNext, Key(DesignInPlaceKind.SingleLine, DesignInPlaceKey.Tab));
        Assert.Equal(DesignInPlaceAction.SavePrevious, Key(DesignInPlaceKind.SingleLine, DesignInPlaceKey.Tab, DesignModifiers.Shift));
        Assert.Equal(DesignInPlaceAction.Revert, Key(DesignInPlaceKind.SingleLine, DesignInPlaceKey.Z, DesignModifiers.Control));
        Assert.Equal(DesignInPlaceAction.Cancel, Key(DesignInPlaceKind.MultiLine, DesignInPlaceKey.Escape));

        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.MultiLine, DesignInPlaceKey.Enter));
        Assert.Equal(DesignInPlaceAction.NewLine, Key(DesignInPlaceKind.MultiLine, DesignInPlaceKey.Enter, DesignModifiers.Shift));
        Assert.Equal(DesignInPlaceAction.Swallow, Key(DesignInPlaceKind.MultiLine, DesignInPlaceKey.Enter, DesignModifiers.Control));

        Assert.Equal(DesignInPlaceAction.NewLine, Key(DesignInPlaceKind.TextArea, DesignInPlaceKey.Enter));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.TextArea, DesignInPlaceKey.Tab));

        Assert.Equal(DesignInPlaceAction.NewLine, Key(DesignInPlaceKind.Items, DesignInPlaceKey.Enter));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.Items, DesignInPlaceKey.Enter, DesignModifiers.Shift));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.Items, DesignInPlaceKey.Enter, DesignModifiers.Alt));
        Assert.Equal(DesignInPlaceAction.None, Key(DesignInPlaceKind.Items, DesignInPlaceKey.Z, DesignModifiers.Control));

        Assert.Equal(DesignInPlaceAction.ToUrl, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Tab));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Tab, DesignModifiers.Shift));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Enter, field: DesignInPlaceField.Url));
        Assert.Equal(DesignInPlaceAction.ToVisit, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Tab, field: DesignInPlaceField.Url));
        Assert.Equal(DesignInPlaceAction.ToText, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Tab, DesignModifiers.Shift, DesignInPlaceField.Url));
        Assert.Equal(DesignInPlaceAction.Visit, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Enter, field: DesignInPlaceField.Visit));
        Assert.Equal(DesignInPlaceAction.Save, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Tab, field: DesignInPlaceField.Visit));
        Assert.Equal(DesignInPlaceAction.Cancel, Key(DesignInPlaceKind.Hyperlink, DesignInPlaceKey.Escape, field: DesignInPlaceField.Visit));
    }

    /// <summary>What the editor's box shows a number as (JavaScript's text of it), and which URLs a hyperlink takes.</summary>
    [Fact]
    public void NumbersAndUrlsAsTheEditorTakesThem()
    {
        Assert.Equal("0", DesignDocument.NumberText(0));
        Assert.Equal("0.1", DesignDocument.NumberText(0.1));
        Assert.Equal("100", DesignDocument.NumberText(100));
        Assert.Equal("-2.5", DesignDocument.NumberText(-2.5));
        Assert.Equal("123456789012", DesignDocument.NumberText(123456789012));
        Assert.Equal("1e+21", DesignDocument.NumberText(1e21));
        Assert.Equal("1e-7", DesignDocument.NumberText(1e-7));
        Assert.Equal("0.000001", DesignDocument.NumberText(1e-6));
        Assert.Equal("Infinity", DesignDocument.NumberText(double.PositiveInfinity));
        Assert.Equal("-Infinity", DesignDocument.NumberText(double.NegativeInfinity));
        Assert.Equal("NaN", DesignDocument.NumberText(double.NaN));

        Assert.Equal(double.PositiveInfinity, DesignInPlace.Number("inf"));
        Assert.False(DesignInPlace.Matches(DesignInPlace.NumberPattern, "Infinity")); // what the box shows for Inf fails the pattern
        Assert.True(DesignInPlace.Matches(DesignInPlace.NumberPattern, "s"));
        Assert.True(DesignInPlace.Matches(DesignInPlace.LimitPattern, "1x5")); // its dots match any character

        Assert.True(DesignInPlace.IsValidUrl(string.Empty));
        Assert.True(DesignInPlace.IsValidUrl("www.mathworks.com"));
        Assert.False(DesignInPlace.IsValidUrl("mathworks"));
        Assert.False(DesignInPlace.IsValidUrl(".com"));
        Assert.False(DesignInPlace.IsValidUrl("mathworks."));
        Assert.False(DesignInPlace.IsValidUrl("matlab:doc"));
        Assert.False(DesignInPlace.IsValidUrl("javascript:alert(1)"));
    }
}
