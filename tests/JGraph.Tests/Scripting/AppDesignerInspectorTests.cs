using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W7b: the inspector over a selection in an app - several components' shared rows,
/// an edit reaching all of them as one step that undoes, all or nothing, the names it brings, a labelled component's
/// label, what is inside a container, and what the inspector keeps between selections.
/// </summary>
[Collection("JG facade")]
public class AppDesignerInspectorTests
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

    private static DesignComponent One(DesignDocument document, string entry, double x = 100, double y = 100, DesignComponent? into = null) =>
        Add(document, entry, x, y, into, withoutLabel: true)[^1];

    private static string[] Rows(DesignDocument document) =>
        document.SheetOf(document.Inspection!).SelectMany(static g => g.Rows).SelectMany(static r => r.Editors).Select(static e => e.Name).ToArray();

    private static double Number(DesignDocument document, DesignComponent c, string name) =>
        Assert.IsType<NumericValue>(document.ValueOf(c, name)).Data[0];

    [Fact]
    public void SeveralComponentsShowWhatTheyShareWithTheLastsValues()
    {
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        DesignComponent lamp = One(document, "Lamp", 300, 300);
        document.SetProperty(button, "Tag", new CharValue("first"));
        document.SetProperty(lamp, "Tag", new CharValue("last"));
        document.SelectPicks([DesignPick.Of(button), DesignPick.Of(lamp)]);

        DesignInspection inspection = document.Inspection!;
        Assert.Equal(DesignInspectionKind.Heterogeneous, inspection.Kind);
        Assert.Same(lamp, inspection.Anchor);
        Assert.Equal(DesignInspectorSheet.MultipleComponents, document.InspectorTab);

        // The lamp's groups, without what the button has not: Color, and with the button's Text gone too.
        string[] rows = Rows(document);
        Assert.Contains("Visible", rows);
        Assert.Contains("Position", rows);
        Assert.Contains("Tag", rows);
        Assert.DoesNotContain("Text", rows);
        Assert.DoesNotContain("Color", rows);
        Assert.Equal("last", Assert.IsType<CharValue>(document.ValueOf(inspection.Anchor, "Tag")).Text);
    }

    [Fact]
    public void APropertyTypedDifferentlyIsNotShared()
    {
        // A button's BackgroundColor is an RGBColor, a label's an RGBAColor: R2025b's sheets say they do not share it.
        DesignDocument document = App();
        DesignComponent button = One(document, "Button");
        DesignComponent label = One(document, "Label", 300, 300);
        document.SelectPicks([DesignPick.Of(button), DesignPick.Of(label)]);
        Assert.DoesNotContain("BackgroundColor", Rows(document));
        Assert.Contains("FontColor", Rows(document));
    }

    [Fact]
    public void AnEditReachesThemAllAsOneStepThatUndoes()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Button", 300, 300);
        DesignPick[] picks = [DesignPick.Of(a), DesignPick.Of(b)];
        document.SelectPicks(picks);
        Assert.Null(document.SetInspected(document.Inspection!, "FontSize", NumericValue.Scalar(20)));

        Assert.Equal(20, Number(document, a, "FontSize"));
        Assert.Equal(20, Number(document, b, "FontSize"));
        Assert.Equal(2, document.Selection.Count);
        Assert.Contains("FontSize = 20", document.CurrentText, StringComparison.Ordinal);

        document.Undo();
        Assert.Equal(12, Number(document, a, "FontSize"));
        Assert.Equal(12, Number(document, b, "FontSize"));
        Assert.Equal(2, document.Selection.Count);

        document.Redo();
        Assert.Equal(20, Number(document, a, "FontSize"));
        Assert.Equal(20, Number(document, b, "FontSize"));
    }

    [Fact]
    public void OneAlreadyHoldingTheValueIsLeftAloneAndNoneNeedingItIsNoStep()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Button", 300, 300);
        document.SetProperty(a, "FontSize", NumericValue.Scalar(20));
        document.SetProperty(b, "FontSize", NumericValue.Scalar(20));
        int stamp = UndoSteps(document);
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(b)]);
        Assert.Null(document.SetInspected(document.Inspection!, "FontSize", NumericValue.Scalar(20)));
        Assert.Equal(stamp, UndoSteps(document));

        // One alone too (_isUserEnteredValueTheSame).
        document.SelectPicks([DesignPick.Of(a)]);
        Assert.Null(document.SetInspected(document.Inspection!, "FontSize", NumericValue.Scalar(20)));
        Assert.Equal(stamp, UndoSteps(document));
    }

    private static int UndoSteps(DesignDocument document)
    {
        int steps = 0;
        while (document.CanUndo)
        {
            document.Undo();
            steps++;
        }

        for (int i = 0; i < steps; i++)
        {
            document.Redo();
        }

        return steps;
    }

    [Fact]
    public void WhenOneRefusesNothingChanges()
    {
        DesignDocument document = App();
        DesignComponent knob = One(document, "Knob");
        DesignComponent slider = One(document, "Slider", 300, 300);
        Assert.Null(document.SetProperty(knob, "Limits", NumericValue.Row(0, 1000)));
        document.SelectPicks([DesignPick.Of(knob), DesignPick.Of(slider)]);
        double before = Number(document, knob, "Value");

        // The knob takes 500; the slider's limits refuse it, and the knob goes back.
        string? refused = document.SetProperties([knob, slider], "Value", NumericValue.Scalar(500));
        Assert.NotNull(refused);
        Assert.Equal(before, Number(document, knob, "Value"));
        Assert.DoesNotContain("Value = 500", document.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public void TextEditedOnSeveralNamesEachApart()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Button", 300, 300);
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(b)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Text", new CharValue("Run")));
        Assert.Equal("RunButton", a.CodeName);
        Assert.Equal("RunButton_2", b.CodeName);

        document.Undo();
        Assert.Equal("Button", a.CodeName);
        Assert.Equal("Button2", b.CodeName);
    }

    [Fact]
    public void ALabelledComponentsFontGoesToItsLabelInOneStep()
    {
        DesignDocument document = App();
        IReadOnlyList<DesignComponent> pair = Add(document, "Slider");
        DesignGroup group = document.Design.LabeledGroupOf(pair[0])!;
        document.SelectPicks([DesignPick.Of(group)]);
        DesignInspection inspection = document.Inspection!;
        Assert.Equal(DesignInspectionKind.Labeled, inspection.Kind);
        Assert.Equal("Slider", document.InspectorTab);

        Assert.Null(document.SetInspected(inspection, "FontSize", NumericValue.Scalar(18)));
        Assert.Equal(18, Number(document, group.Label!, "FontSize"));
        Assert.Equal(18, Number(document, group.Field!, "FontSize"));
        Assert.Same(group, document.SelectedLabeled);

        document.Undo();
        Assert.Equal(12, Number(document, group.Label!, "FontSize"));
        Assert.Equal(12, Number(document, group.Field!, "FontSize"));

        // What only the field has goes to it alone.
        Assert.Null(document.SetInspected(inspection, "Tooltip", new CharValue("tip")));
        Assert.Equal("tip", Assert.IsType<CharValue>(group.Field!.Get("Tooltip")).Text);
        Assert.Null(group.Label!.Get("Tooltip"));
    }

    [Fact]
    public void AutoResizeChildrenGoesToWhatIsInside()
    {
        DesignDocument document = App();
        DesignComponent outer = One(document, "Panel");
        DesignComponent inner = One(document, "Panel", 20, 20, outer);
        One(document, "Button", 10, 10, inner);
        document.SelectPicks([DesignPick.Of(outer)]);
        Assert.Null(document.SetInspected(document.Inspection!, "AutoResizeChildren", OnOffValue.Off));
        Assert.Contains("AutoResizeChildren = 'off'", document.CurrentText[document.CurrentText.IndexOf("Panel2", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.False(Assert.IsType<OnOffValue>(document.ValueOf(inner, "AutoResizeChildren")).IsOn);
        document.Undo();
        Assert.True(Assert.IsType<OnOffValue>(document.ValueOf(inner, "AutoResizeChildren")).IsOn);
        Assert.True(Assert.IsType<OnOffValue>(document.ValueOf(outer, "AutoResizeChildren")).IsOn);
    }

    [Fact]
    public void APlainGroupIsItsMembersAndItsLastIsTheAnchor()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Lamp", 300, 300);
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(b)]);
        DesignGroup group = document.Group()!;
        document.SelectPicks([DesignPick.Of(group)]);
        Assert.Equal(DesignInspectorSheet.MultipleComponents, document.InspectorTab);
        Assert.Same(b, document.Inspection!.Anchor);
        Assert.Equal(DesignInspectionKind.Heterogeneous, document.Inspection.Kind);

        Assert.Null(document.SetInspected(document.Inspection, "Visible", OnOffValue.Off));
        Assert.False(Assert.IsType<OnOffValue>(document.ValueOf(a, "Visible")).IsOn);
        Assert.False(Assert.IsType<OnOffValue>(document.ValueOf(b, "Visible")).IsOn);
        Assert.Same(group, document.Picks[0].Group);
    }

    [Fact]
    public void WhereAMenuStandsChangesItsRows()
    {
        DesignDocument document = App();
        document.SelectPicks([DesignPick.Of(document.Design.Figure)]);
        DesignComponent top = document.AddMenuAtEnd(document.Design.Figure);
        document.SelectPicks([DesignPick.Of(top)]);
        Assert.Equal("Menu", document.InspectorTab);
        Assert.DoesNotContain("Accelerator", Rows(document));
        Assert.DoesNotContain("Separator", Rows(document));
        Assert.DoesNotContain("Checked", Rows(document));
        Assert.Contains("Text", Rows(document));
    }

    [Fact]
    public void WithNothingSelectedTheTabSaysComponent()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        document.SelectPicks([DesignPick.Of(a)]);
        Assert.Equal("Button", document.InspectorTab);
        document.Delete(document.Selection);
        Assert.True(document.SelectionIsEmpty);
        Assert.Null(document.Inspection);
        Assert.Equal(DesignInspectorSheet.NoComponent, document.InspectorTab);

        document.SelectApp();
        Assert.Null(document.Inspection);
        Assert.Equal("App", document.InspectorTab);
    }

    [Fact]
    public void AGridsChildKeepsItsPositionAmongSeveral()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Button", 300, 300);
        document.Gridify(document.Design.Figure);
        Assert.True(a.IsInGrid);
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(b)]);
        string before = document.CurrentText;
        Assert.Null(document.SetInspected(document.Inspection!, "Position", NumericValue.Row(1, 1, 50, 50)));
        Assert.Equal(before, document.CurrentText);
    }

    [Fact]
    public void ASearchTakesWhatIsTypedAsItIs()
    {
        DesignEditor[] rows = DesignInspectorSheet.Alphabetical(DesignInspector.R2025b.GroupsOf("matlab.ui.control.Button")).ToArray();
        Assert.True(DesignInspectorSheet.Search(rows, "(").Help.Count >= 0);
        Assert.True(DesignInspectorSheet.Search(rows, "zzq").None);
        Assert.Equal(rows.Length, DesignInspectorSheet.Search(rows, string.Empty).Direct.Count);

        // A case of its own, and the help found under the heading.
        DesignInspectorMatches matches = DesignInspectorSheet.Search(rows, "COLOR");
        Assert.Contains(matches.Direct, static e => e.Name == "BackgroundColor");
        Assert.Equal(DesignInspectorSheet.Search(rows, "operational").Help.Select(static e => e.Name), ["Enable"]);
    }

    [Fact]
    public void WhatARowsHelpSays()
    {
        DesignEditor enable = DesignInspector.R2025b.GroupsOf("matlab.ui.control.Button").SelectMany(static g => g.Rows).SelectMany(static r => r.Editors).First(static e => e.Name == "Enable");
        (string? name, string? heading, string? body) = DesignInspectorSheet.Help(enable);
        Assert.Null(name);
        Assert.Equal("Operational state", heading);
        Assert.Contains("on/off logical value", body, StringComparison.Ordinal);

        // A long name says itself even with no help of its own.
        (string? longName, string? noHeading, string? noBody) = DesignInspectorSheet.Help(new DesignEditor(new string('X', DesignInspectorSheet.LongName), DesignEditorKind.Text, []));
        Assert.NotNull(longName);
        Assert.Null(noHeading);
        Assert.Null(noBody);
    }

    [Fact]
    public void ARowOfASubGroupKnowsIt()
    {
        DesignInspectorRow[] rows = DesignInspector.R2025b.GroupsOf("matlab.ui.control.Spinner").SelectMany(static g => g.Rows).ToArray();
        Assert.Contains(rows, static r => r.SubGroup == 0);
        Assert.Contains(rows, static r => r.SubGroup is null);
    }

    [Fact]
    public void WhatIsKeptIsKeptForEachClassOrSetOfClasses()
    {
        var state = new DesignInspectorState();
        DesignInspectorState.Entry button = state.For("matlab.ui.control.Button", 22);
        button.Alphabetical = true;
        button.Open["Position"] = true;
        Assert.Same(button, state.For("matlab.ui.control.Button", 22));
        Assert.NotSame(button, state.For("matlab.ui.control.Lamp", 16));

        // A sheet with another number of properties is another sheet.
        Assert.False(state.For("matlab.ui.control.Button", 23).Alphabetical);
    }

    [Fact]
    public void AnEditSurvivesASaveAndARead()
    {
        DesignDocument document = App();
        DesignComponent a = One(document, "Button");
        DesignComponent b = One(document, "Lamp", 300, 300);
        document.SelectPicks([DesignPick.Of(a), DesignPick.Of(b)]);
        Assert.Null(document.SetInspected(document.Inspection!, "Tag", new CharValue("both")));
        DesignDocument read = DesignDocument.FromText(document.CurrentText, null);
        Assert.Equal("both", Assert.IsType<CharValue>(read.Design.Find(a.CodeName)!.Get("Tag")).Text);
        Assert.Equal("both", Assert.IsType<CharValue>(read.Design.Find(b.CodeName)!.Get("Tag")).Text);
    }
}
