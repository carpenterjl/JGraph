using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W7c: the inspector's editors in an app - which editor each row has, several properties set
/// as one step, a property's mode beside it, a component sized to its text, and what each editor's change comes to in the file.
/// </summary>
[Collection("JG facade")]
public class AppDesignerEditorsTests
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

    private static DesignEditor EditorOf(string type, string name) =>
        DesignInspector.R2025b.GroupsOf(type).SelectMany(static g => g.Rows).SelectMany(static r => r.Editors).First(e => e.Name == name);

    [Theory]
    [InlineData("matlab.ui.control.Button", "Text", DesignEditorKind.Lines)]
    [InlineData("matlab.ui.control.Button", "FontName", DesignEditorKind.FontName)]
    [InlineData("matlab.ui.control.Button", "FontSize", DesignEditorKind.FontSize)]
    [InlineData("matlab.ui.control.Button", "FontWeight", DesignEditorKind.Toggle)]
    [InlineData("matlab.ui.control.Button", "FontAngle", DesignEditorKind.Toggle)]
    [InlineData("matlab.ui.control.Button", "HorizontalAlignment", DesignEditorKind.ToggleGroup)]
    [InlineData("matlab.ui.control.Button", "BackgroundColor", DesignEditorKind.Color)]
    [InlineData("matlab.ui.control.Button", "Icon", DesignEditorKind.File)]
    [InlineData("matlab.ui.control.Button", "Position", DesignEditorKind.Fields)]
    [InlineData("matlab.ui.control.DropDown", "Items", DesignEditorKind.Items)]
    [InlineData("matlab.ui.control.DropDown", "ItemsData", DesignEditorKind.Lines)]
    [InlineData("matlab.ui.control.DiscreteKnob", "Items", DesignEditorKind.Items)]
    [InlineData("matlab.ui.control.Switch", "Items", DesignEditorKind.Items)]
    [InlineData("matlab.ui.control.Gauge", "ScaleColors", DesignEditorKind.ScaleColors)]
    [InlineData("matlab.ui.control.Gauge", "ScaleColorLimits", DesignEditorKind.ScaleColors)]
    [InlineData("matlab.ui.control.Gauge", "MajorTicks", DesignEditorKind.Ticks)]
    [InlineData("matlab.ui.control.Gauge", "Limits", DesignEditorKind.Fields)]
    [InlineData("matlab.ui.control.NumericEditField", "ValueDisplayFormat", DesignEditorKind.ViewFormat)]
    [InlineData("matlab.ui.control.Table", "ColumnName", DesignEditorKind.Columns)]
    [InlineData("matlab.ui.control.Table", "ColumnWidth", DesignEditorKind.Columns)]
    [InlineData("matlab.ui.control.Table", "BackgroundColor", DesignEditorKind.ColorList)]
    [InlineData("matlab.ui.control.DatePicker", "Value", DesignEditorKind.Date)]
    [InlineData("matlab.ui.control.DatePicker", "Limits", DesignEditorKind.DateLimits)]
    [InlineData("matlab.ui.control.DatePicker", "DisabledDates", DesignEditorKind.DisabledDates)]
    [InlineData("matlab.ui.control.DatePicker", "DisabledDaysOfWeek", DesignEditorKind.DaysOfWeek)]
    [InlineData("matlab.ui.container.Panel", "BorderWidth", DesignEditorKind.Spinner)]
    [InlineData("matlab.ui.control.UIAxes", "XTick", DesignEditorKind.Ticks)]
    [InlineData("matlab.ui.control.UIAxes", "XLim", DesignEditorKind.Fields)]
    public void EachRowHasTheEditorR2025bsSheetNames(string type, string name, DesignEditorKind kind)
    {
        Assert.Equal(kind, EditorOf(type, name).Kind);
    }

    [Fact]
    public void TheSheetsSayWhatTheEditorsNeed()
    {
        DesignEditor items = EditorOf("matlab.ui.control.DiscreteKnob", "Items");
        Assert.Equal("ItemsEditor", items.Info.Rich);
        Assert.Equal(2, items.Info.MinNumber);
        Assert.Equal("defaultStateName", items.Info.DefaultNameKey);
        Assert.Equal(2, EditorOf("matlab.ui.control.Switch", "Items").Info.MaxNumber);
        Assert.Equal(["normal", "bold"], EditorOf("matlab.ui.control.Button", "FontWeight").Info.Enumerated);
        Assert.Equal(["x", "y", "width", "height"], EditorOf("matlab.ui.control.Button", "Position").Info.Fields);
        Assert.Contains("maxGreaterThanMin", EditorOf("matlab.ui.control.Gauge", "Limits").Info.Validation);
        Assert.Equal("ResetLimitsEditor", EditorOf("matlab.ui.control.UIAxes", "XLim").Info.Rich);
        Assert.Contains("12", EditorOf("matlab.ui.control.Button", "FontSize").Choices);
        Assert.Contains("parula", EditorOf("matlab.ui.Figure", "AD_ColormapString").Choices);
        Assert.False(EditorOf("matlab.ui.control.DatePicker", "DisplayFormat").Info.IsProtected);
    }

    [Fact]
    public void ADropDownsItemsAndValueGoAsOneStep()
    {
        DesignDocument document = App();
        DesignComponent drop = One(document, "Drop Down");
        document.SelectPicks([DesignPick.Of(drop)]);
        DesignInspection inspection = document.Inspection!;
        Assert.Null(document.SetInspected(inspection, [("Items", DesignEditorValues.Items(["Red", "Green"])), ("Value", new CharValue("Green"))]));
        Assert.Equal(["Red", "Green"], DesignEditorValues.Lines(document.ValueOf(drop, "Items")));
        Assert.Equal("Green", Assert.IsType<CharValue>(document.ValueOf(drop, "Value")).Text);
        string saved = DesignDocumentText(document);
        Assert.Contains("{'Red', 'Green'}", saved, StringComparison.Ordinal);
        Assert.Contains("'Green'", saved, StringComparison.Ordinal);

        document.Undo();
        Assert.DoesNotContain("Red", DesignEditorValues.Lines(document.ValueOf(drop, "Items")));
        Assert.DoesNotContain("{'Red', 'Green'}", DesignDocumentText(document), StringComparison.Ordinal);
    }

    [Fact]
    public void AKnobTakesANewState()
    {
        DesignDocument document = App();
        DesignComponent knob = One(document, "Knob (Discrete)");
        document.SelectPicks([DesignPick.Of(knob)]);
        DesignEditor items = EditorOf(knob.Type, "Items");
        DesignValue? held = document.ValueOf(knob, "Items");
        var model = new DesignItemsModel(DesignEditorValues.Lines(held), DesignEditorValues.SelectedIndex(held, document.ValueOf(knob, "ItemsData"), document.ValueOf(knob, "Value")), items.Info);
        Assert.Equal(1, model.SelectedIndex);
        IReadOnlyList<DesignEditorCommit> commits = model.AddRow(0, out _);
        Assert.Equal("State 1", model.Items[1]);
        Assert.Equal("Items", Assert.Single(commits).Property);
        Assert.Null(document.SetInspected(document.Inspection!, [("Items", DesignEditorValues.Items(model.Items))]));
        Assert.Contains("State 1", DesignDocumentText(document), StringComparison.Ordinal);
    }

    private static string DesignDocumentText(DesignDocument document) => DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), document.Design)!;

    [Fact]
    public void AGaugesColoursAndLimitsGoAsOneStep()
    {
        DesignDocument document = App();
        DesignComponent gauge = One(document, "Gauge");
        document.SelectPicks([DesignPick.Of(gauge)]);
        var model = new DesignScaleColorsModel([], [], [0, 100]);
        IReadOnlyList<DesignEditorCommit> commits = model.AddRow(-1, out int row);
        Assert.Equal(0, row);
        Assert.Equal(["ScaleColors", "ScaleColorLimits"], commits.Select(static c => c.Property));
        Assert.Null(document.SetInspected(document.Inspection!, commits.Select(c => (c.Property, (DesignValue?)DesignEditorValues.Matrix((List<double[]>)c.After!, c.Property == "ScaleColors" ? 3 : 2))).ToArray()));
        Assert.Equal([1, 0, 0], Assert.IsType<NumericValue>(gauge.Get("ScaleColors")).Data);
        Assert.Equal([90, 100], Assert.IsType<NumericValue>(gauge.Get("ScaleColorLimits")).Data);
        document.Undo();
        Assert.Null(gauge.Get("ScaleColors"));
        Assert.Null(gauge.Get("ScaleColorLimits"));
    }

    [Fact]
    public void ARefusalAmongSeveralLeavesNothingChanged()
    {
        DesignDocument document = App();
        DesignComponent drop = One(document, "Drop Down");
        document.SelectPicks([DesignPick.Of(drop)]);
        object? before = document.ValueOf(drop, "Value");
        Assert.NotNull(document.SetInspected(document.Inspection!, [("Items", DesignEditorValues.Items(["Red", "Green"])), ("Value", new CharValue("NotThere"))]));
        Assert.DoesNotContain("Red", DesignEditorValues.Lines(document.ValueOf(drop, "Items")));
        Assert.Equal(DesignValueText.Format((DesignValue)before!), DesignValueText.Format(document.ValueOf(drop, "Value")!));
        Assert.Null(drop.Get("Items"));
    }

    [Fact]
    public void ALabelIsSizedToItsNewTextAndFontKeepingItsSide()
    {
        DesignDocument document = App();
        DesignComponent label = One(document, "Label");
        document.SelectPicks([DesignPick.Of(label)]);
        double[] was = document.Runtime.ReadNumbers(label, "Position")!;
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("A far longer label than it was")));
        double[] wide = document.Runtime.ReadNumbers(label, "Position")!;
        Assert.True(wide[2] > was[2]);
        Assert.Equal(was[0], wide[0]);

        // Larger type is taller, its top staying where it was.
        Assert.Null(document.SetInspected(document.Inspection!, "FontSize", NumericValue.Scalar(36)));
        double[] tall = document.Runtime.ReadNumbers(label, "Position")!;
        Assert.True(tall[3] > wide[3]);
        Assert.True(tall[2] > wide[2]);
        Assert.Equal(wide[1] + wide[3], tall[1] + tall[3], 6);

        // Right aligned, a wider one grows to the left.
        Assert.Null(document.SetInspected(document.Inspection!, "HorizontalAlignment", new CharValue("right")));
        double right = tall[0] + tall[2];
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("A far longer label than it was, and longer")));
        double[] after = document.Runtime.ReadNumbers(label, "Position")!;
        Assert.Equal(right, after[0] + after[2], 6);

        // One step takes off the text and the size together.
        document.Undo();
        Assert.Equal(tall, document.Runtime.ReadNumbers(label, "Position"));
    }

    [Fact]
    public void AComponentSizedByHandIsNotShrunk()
    {
        DesignDocument document = App();
        DesignComponent label = One(document, "Label");
        document.SetProperty(label, "Position", NumericValue.Row(100, 100, 300, 40));
        document.SelectPicks([DesignPick.Of(label)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("x")));
        double[] held = document.Runtime.ReadNumbers(label, "Position")!;
        Assert.Equal(300, held[2]);
        Assert.Equal(40, held[3]);
    }

    [Fact]
    public void AButtonIsNoNarrowerThanItIsMadeAndAFieldKeepsItsWidth()
    {
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        document.SelectPicks([DesignPick.Of(button)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("a")));
        Assert.Equal(100, document.Runtime.ReadNumbers(button, "Position")![2]);
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("A very long button text indeed, longer than a hundred pixels")));
        Assert.True(document.Runtime.ReadNumbers(button, "Position")![2] > 100);

        DesignComponent field = One(document, "Edit Field (Text)", 300, 300);
        document.SelectPicks([DesignPick.Of(field)]);
        double width = document.Runtime.ReadNumbers(field, "Position")![2];
        Assert.Null(document.SetInspected(document.Inspection!, "FontSize", NumericValue.Scalar(30)));
        double[] held = document.Runtime.ReadNumbers(field, "Position")!;
        Assert.Equal(width, held[2]);
        Assert.True(held[3] > 22);
    }

    [Fact]
    public void AGridsChildIsNotSizedByItsText()
    {
        DesignDocument document = App();
        DesignComponent label = One(document, "Label");
        document.Gridify(document.Design.Figure);
        double[] was = document.Runtime.ReadNumbers(label, "Position")!;
        document.SelectPicks([DesignPick.Of(label)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("A far longer label than it was")));
        Assert.Equal(was, document.Runtime.ReadNumbers(label, "Position"));
    }

    [Fact]
    public void AValueSayingItsDefaultIsAutomaticAndNotWritten()
    {
        DesignDocument document = App();
        DesignComponent gauge = One(document, "Gauge");
        document.SelectPicks([DesignPick.Of(gauge)]);
        DesignValue held = document.ValueOf(gauge, "MajorTicks")!;
        Assert.Null(document.SetInspected(document.Inspection!, "MajorTicks", NumericValue.Row(0, 50, 100)));
        Assert.NotNull(gauge.Get("MajorTicks"));

        // Within two places of the class's default: its mode goes automatic, and the generator writes nothing.
        DesignValue? fallback = gauge.Class!.Property("MajorTicks")!.DefaultFor(document.Theme);
        Assert.Null(document.SetInspected(document.Inspection!, "MajorTicks", fallback));
        Assert.Null(gauge.Get("MajorTicks"));
        Assert.Equal(DesignValueText.Format(held), DesignValueText.Format(document.ValueOf(gauge, "MajorTicks")!));
    }

    [Fact]
    public void ATablesColumnsGoAsTheyGoOut()
    {
        DesignDocument document = App();
        DesignComponent table = One(document, "Table");
        document.SelectPicks([DesignPick.Of(table)]);
        var model = new DesignColumnsModel(
            DesignEditorValues.ColumnNames(document.ValueOf(table, "ColumnName")),
            DesignEditorValues.ColumnWidths(document.ValueOf(table, "ColumnWidth")),
            DesignEditorValues.Flags(document.ValueOf(table, "ColumnEditable")),
            DesignEditorValues.Flags(document.ValueOf(table, "ColumnSortable")));
        Assert.Equal(4, model.Names.Count);
        Assert.Equal(4, model.Widths.Count);
        Assert.True(model.IsAuto(0));

        IReadOnlyList<DesignEditorCommit> commits = model.SetAuto(1, false);
        Assert.Equal("ColumnWidth", Assert.Single(commits).Property);
        Assert.Null(document.SetInspected(document.Inspection!, commits.Select(c => (c.Property, DesignEditorValues.ColumnValue(c.Property, c.After))).ToArray()));
        Assert.Contains("{'auto', 75, 'auto', 'auto'}", DesignDocumentText(document), StringComparison.Ordinal);

        commits = model.AddRow(3, out int row);
        Assert.Equal(4, row);
        Assert.Equal("Column 5", model.Names[4]);
        Assert.Null(document.SetInspected(document.Inspection!, commits.Select(c => (c.Property, DesignEditorValues.ColumnValue(c.Property, c.After))).ToArray()));
        Assert.Equal(5, DesignEditorValues.ColumnNames(document.ValueOf(table, "ColumnName")).Count);
    }

    [Fact]
    public void ADatePickersDatesGoInTheFile()
    {
        DesignDocument document = App();
        DesignComponent picker = One(document, "Date Picker");
        document.SelectPicks([DesignPick.Of(picker)]);
        Assert.Null(document.SetInspected(document.Inspection!, [("DisabledDaysOfWeek", NumericValue.Row(1, 7))]));
        Assert.Contains("[1 7]", DesignDocumentText(document), StringComparison.Ordinal);
        Assert.Null(document.SetInspected(document.Inspection!, [("DisabledDates", DesignEditorValues.DatesValue([new DesignDate(2026, 12, 25)]))]));
        Assert.Equal([new DesignDate(2026, 12, 25)], DesignEditorValues.Dates(picker.Get("DisabledDates")));
        Assert.Contains("datetime([2026 12 25])", DesignDocumentText(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADatePickersValueAndLimitsAreReadAsDates()
    {
        DesignDocument document = App();
        DesignComponent picker = One(document, "Date Picker");
        document.SelectPicks([DesignPick.Of(picker)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Limits", DesignEditorValues.DatesValue([new DesignDate(2026, 1, 1), new DesignDate(2026, 12, 31)])));
        Assert.Equal([new DesignDate(2026, 1, 1), new DesignDate(2026, 12, 31)], DesignEditorValues.Dates(document.ValueOf(picker, "Limits")));
        Assert.Null(document.SetInspected(document.Inspection!, "Value", DesignEditorValues.DatesValue([new DesignDate(2026, 6, 15)])));
        Assert.Equal([new DesignDate(2026, 6, 15)], DesignEditorValues.Dates(document.ValueOf(picker, "Value")));
        Assert.Contains("datetime([2026 6 15])", DesignDocumentText(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TextLinesComeToWhatTheComponentHolds()
    {
        Assert.Equal([], DesignTextArea.Lines(string.Empty));
        Assert.Equal(["a"], DesignTextArea.Lines("a\n"));
        Assert.Equal(["a", "", "b"], DesignTextArea.Lines("a\n\nb"));
        Assert.Equal(["a", "b"], DesignTextArea.Lines("a\r\nb"));
        Assert.Equal("a,b", DesignTextArea.Shown(["a", "b"]));
        Assert.Equal("a\nb", DesignTextArea.Edited(["a", "b"]));
        Assert.IsType<CharValue>(DesignEditorValues.Text(["a"]));
        Assert.IsType<CellValue>(DesignEditorValues.Text(["a", "b"]));
        Assert.Equal(string.Empty, Assert.IsType<CharValue>(DesignEditorValues.Text([])).Text);
        Assert.Equal("{1,2}", DesignValueText.Format(DesignEditorValues.ItemsData("{1,2}")));
        Assert.Equal("{'1', '2'}", DesignValueText.Format(DesignEditorValues.ItemsData("1\n2")));
    }

    [Fact]
    public void ColoursAreReadAsTheBoxTakesThem()
    {
        Assert.Equal([1, 0, 0], DesignColors.Parse("red")!);
        Assert.Equal([0, 0, 1], DesignColors.Parse("b")!);
        Assert.Equal([0.5, 0.25, 1], DesignColors.Parse("0.5,0.25,1")!);
        Assert.Equal([0.5, 0.25, 1], DesignColors.Parse("[0.5 0.25 1]")!);
        Assert.Null(DesignColors.Parse("2,0,0"));
        Assert.Null(DesignColors.Parse("purple"));
        Assert.Equal("#ff0080", DesignColors.Hex([1, 0, 128 / 255d]));
        Assert.Equal("0.96,0.96,0.96", DesignColors.Text([0.96078431372549, 0.96078431372549, 0.96078431372549]));
        Assert.Equal(4, DesignColors.Standard.Count);
        Assert.All(DesignColors.Standard, static row => Assert.Equal(7, row.Count));
    }

    [Fact]
    public void AValuePicksItsItem()
    {
        DesignValue items = DesignEditorValues.Items(["A", "B", "C"]);
        Assert.Equal(2, DesignEditorValues.SelectedIndex(items, null, new CharValue("B")));
        Assert.Equal(-1, DesignEditorValues.SelectedIndex(items, null, new CharValue("Z")));
        DesignValue data = DesignEditorValues.Items(["x", "y", "z"]);
        Assert.Equal(3, DesignEditorValues.SelectedIndex(items, data, new CharValue("z")));
        Assert.Equal("C", Assert.IsType<CharValue>(DesignEditorValues.ValueAt(3, ["A", "B", "C"], null, false)).Text);
        Assert.Equal("y", Assert.IsType<CharValue>(DesignEditorValues.ValueAt(2, ["A", "B", "C"], data, false)).Text);
        Assert.Equal("z", Assert.IsType<CharValue>(DesignEditorValues.ValueAt(9, ["A", "B", "C"], data, false)).Text);
        Assert.Equal(string.Empty, Assert.IsType<CharValue>(DesignEditorValues.ValueAt(-1, ["A"], null, false)).Text);
        Assert.IsType<CellValue>(DesignEditorValues.ValueAt(1, ["A"], null, true));
    }

    [Fact]
    public void TickRowsGoWithTheirPartners()
    {
        Assert.Equal(("MajorTicks", "MajorTickLabels", "Limits"), DesignEditorValues.TickNames("MajorTicks"));
        Assert.Equal(("XTick", "XTickLabel", "XLim"), DesignEditorValues.TickNames("XTickLabel"));
        Assert.Equal("[0 1 2]", DesignValueText.Format(DesignEditorValues.Numbers("0,1,2")));
        Assert.Equal("[0 1 2]", DesignValueText.Format(DesignEditorValues.Numbers("[0 1 2]")));
    }
}
