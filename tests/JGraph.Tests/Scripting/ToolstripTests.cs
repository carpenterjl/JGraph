using System.Text.Json;
using JGraph.Scripting.Toolstrip;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The toolstrip's UI-free half (App Designer window plan, stage W1): R2025b's App Designer toolstrip
/// read from stage W0's recording (w0_toolstrip.json), the framework's layout rules, the resize
/// against R2025b's own <c>Tab.resize</c> run under node over seeded tabs
/// (ToolstripResizeOracle.json, written by ui-probes/w0/oracle/resize.js), and the key tips.
/// The control itself is checked in a window by tools/runtime-checks/run-w1toolstrip.ps1.
/// </summary>
public class ToolstripTests
{
    private static readonly Lazy<ToolstripLayout> Recorded = new(() =>
    {
        using JsonDocument recording = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Toolstrip", "w0_toolstrip.json")));
        return ToolstripConfigReader.ReadRecording(recording.RootElement);
    });

    private static ToolstripTab Tab(string tag) => Recorded.Value.Tabs.Single(t => t.Tag == "appDesignerToolstrip." + tag);

    private static ToolstripItem Item(string tag) => Recorded.Value.AllItems().First(i => i.Tag == "appDesignerToolstrip." + tag);

    // --- the recording ------------------------------------------------------------------------------

    [Fact]
    public void ReadsAppDesignersSixTabs()
    {
        ToolstripLayout layout = Recorded.Value;
        Assert.Equal(["DESIGNER", "SIMULINK", "CANVAS", "VIEW", "EDITOR", "VIEW"], layout.Tabs.Select(t => t.Title));
        Assert.Equal([false, false, true, true, true, true], layout.Tabs.Select(t => t.IsContextual));
        Assert.Equal("appDesignerToolstrip.layoutTabGroup", Tab("layoutTab").GroupTag);
        Assert.Equal(["FILE", "SHARE", "RUN"], Tab("designerTab").Sections.Select(s => s.Title));
        Assert.Equal(["SPLIT DOCUMENT", "DISPLAY", "CODE FOLDING", "ZOOM", "Review", "RESOURCES"], Tab("viewTab").Sections.Select(s => s.Title));
    }

    [Fact]
    public void SharesTheRunSectionWithTheTabsThatNameIt()
    {
        foreach (string tab in new[] { "designerTab", "simulinkTab", "layoutTab", "editorTab" })
        {
            Assert.Equal("RUN", Tab(tab).Sections[^1].Title);
            Assert.Single(Tab(tab).Sections, s => s.Tag == "appDesignerToolstrip.editorTab.run");
        }

        Assert.DoesNotContain(Tab("designviewTab").Sections, s => s.Title == "RUN");
        Assert.DoesNotContain(Tab("viewTab").Sections, s => s.Title == "RUN");
    }

    [Fact]
    public void ReadsTheControlsWithTheirActionsWords()
    {
        ToolstripItem run = Item("editorTab.run.run");
        Assert.Equal(ToolstripItemKind.SplitButton, run.Kind);
        Assert.Equal(("Run", "AD_RUN", "F5", "playControl", "R"), (run.Text, run.ActionId, run.Shortcut, run.Icon, run.Mnemonic));
        Assert.Equal("appDesignerToolstrip.editorTab.run.runSectionPopUpList", run.PopupTag);
        Assert.True(run.HasDynamicPopup);
        Assert.Equal("DD", Item("editorTab.run.step").Mnemonic);
        Assert.Equal("App\nDetails", Item("designerTab.share.appDetailsButton").Text);
        Assert.Equal(ToolstripItemKind.DropDownButton, Item("designerTab.share.shareAllOptions").Kind);

        ToolstripItem combo = Item("layoutTab.spacing.combo");
        Assert.Equal(ToolstripItemKind.ComboBox, combo.Kind);
        Assert.Equal(["Evenly", "20"], combo.Items);
        Assert.Equal("Evenly", combo.Value);
        Assert.True(combo.Editable);

        ToolstripItem spinner = Item("layoutTab.view.intervalSpinner");
        Assert.Equal(ToolstripItemKind.Spinner, spinner.Kind);
        Assert.Equal("Value must be between 10 and 50", spinner.ErrorText);

        Assert.Equal(["appDesignerToolstrip.codeViewTab.split.none"], Recorded.Value.AllItems()
            .Where(i => i.Kind == ToolstripItemKind.ToggleButton && i.Text == "None").Select(i => i.Tag!));
        Assert.All(Recorded.Value.AllItems().Where(i => i.Tag is { } t && t.Contains(".split.", StringComparison.Ordinal)),
            i => Assert.False(string.IsNullOrEmpty(i.ButtonGroup)));
    }

    [Fact]
    public void EveryPopupAButtonOpensIsRead()
    {
        ToolstripLayout layout = Recorded.Value;
        // Configure for Apps is a push button whose popup tag holds its description: a push button opens nothing.
        foreach (ToolstripItem item in layout.AllItems().Where(i => i.PopupTag is not null && i.Kind is ToolstripItemKind.SplitButton or ToolstripItemKind.DropDownButton))
        {
            Assert.NotNull(layout.Popup(item.PopupTag));
        }

        ToolstripPopup run = layout.Popup("appDesignerToolstrip.editorTab.run.runSectionPopUpList")!;
        Assert.Equal(
            [ToolstripPopupEntryKind.Header, ToolstripPopupEntryKind.Item, ToolstripPopupEntryKind.Item, ToolstripPopupEntryKind.Item, ToolstripPopupEntryKind.Item,
             ToolstripPopupEntryKind.Header, ToolstripPopupEntryKind.CheckItem, ToolstripPopupEntryKind.CheckItem, ToolstripPopupEntryKind.CheckItem, ToolstripPopupEntryKind.CheckItem],
            run.Entries.Select(e => e.Kind));
        Assert.Equal("Breakpoints", run.Entries[0].Text);
        Assert.Equal("Pause on Errors", run.Entries[6].Text);
        Assert.True(run.Entries[6].ClosePopupOnClick);

        ToolstripPopup save = layout.Popup("appDesignerToolstrip.designerTab.file.saveMenu")!;
        Assert.Equal(["Save", "Save As...", "Save Copy As...", null, "Export to .m File..."], save.Entries.Select(e => e.Text));
        Assert.Equal(ToolstripPopupEntryKind.Separator, save.Entries[3].Kind);

        ToolstripPopup goTo = layout.Popup("appDesignerToolstrip.editorTab.navigate.gotoPopUpList")!;
        Assert.Equal(ToolstripPopupEntryKind.Panel, goTo.Entries[^1].Kind);
    }

    [Fact]
    public void ReadsTheQuickAccessBar()
    {
        IReadOnlyList<ToolstripItem> bar = Recorded.Value.QuickAccess;
        Assert.Equal(["Run", "Save", "Cut", "Copy", "Paste", "Undo", "Redo", "Help"], bar.Select(i => i.Text));
        Assert.Equal(["AD_RUN", "AD_SAVE", "AD_CUT", "AD_COPY", "AD_PASTE", "AD_UNDO", "AD_REDO", "AD_HELP"], bar.Select(i => i.ActionId));
        Assert.All(bar, i => Assert.Equal(ToolstripItemKind.PushButton, i.Kind));
        Assert.Equal("playControl", bar[0].Icon);
        Assert.Equal("Ctrl+Z", bar[5].Shortcut);
    }

    [Fact]
    public void MapItemsReachesPanelsAndTheQuickAccessBar()
    {
        ToolstripLayout mapped = Recorded.Value.MapItems(i => i with { Description = "x" });
        Assert.All(mapped.AllItems(), i => Assert.Equal("x", i.Description));
        Assert.All(mapped.QuickAccess, i => Assert.Equal("x", i.Description));
        Assert.Equal(Recorded.Value.AllItems().Count(), mapped.AllItems().Count());
    }

    // --- the framework's layout rules ---------------------------------------------------------------

    [Fact]
    public void AColumnOfOneDrawsItLargeAndOfSeveralSmall()
    {
        ToolstripSection file = Tab("designerTab").Sections[0];
        Assert.True(ToolstripRules.IsVertical(file.Columns[0], file.Columns[0].Items[0]));
        ToolstripSection zoom = Tab("designviewTab").Sections[1];
        Assert.True(ToolstripRules.IsVertical(zoom.Columns[0], zoom.Columns[0].Items[0]));
        Assert.All(zoom.Columns[1].Items, i => Assert.False(ToolstripRules.IsVertical(zoom.Columns[1], i)));

        // The grid interval's panel: its label and spinner have no orientation; Snap to Grid, alone on its row, is small.
        ToolstripColumn intervalColumn = Tab("designviewTab").Sections[0].Columns[1];
        ToolstripItem panel = intervalColumn.Items[0];
        Assert.Equal(ToolstripItemKind.Panel, panel.Kind);
        IReadOnlyList<IReadOnlyList<ToolstripPanelChild>> rows = ToolstripRules.PanelRows(panel);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal("Snap to Grid", rows[1][0].Item!.Text);
        Assert.False(ToolstripRules.IsVerticalInPanel(intervalColumn, rows[0][0].Column!, rows[0][0].Column!.Items[0]));
    }

    [Fact]
    public void OnlyColumnsOfLabelledSmallButtonsWithoutAWidthDropLabels()
    {
        Assert.True(ToolstripRules.CanHideLabels(Tab("designviewTab").Sections[1].Columns[1]));   // Zoom In, Zoom Out, Reset Zoom
        Assert.False(ToolstripRules.CanHideLabels(Tab("designerTab").Sections[0].Columns[0]));    // New, large
        Assert.False(ToolstripRules.CanHideLabels(Tab("layoutTab").Sections[3].Columns[0]));      // the spacing combo box above two buttons
        Assert.False(ToolstripRules.CanHideLabels(Tab("designviewTab").Sections[0].Columns[1]));  // a width of 109
        // ALIGN's buttons are labelled " ": not empty to the framework, so their column qualifies (and loses nothing).
        Assert.True(ToolstripRules.CanHideLabels(Tab("layoutTab").Sections[1].Columns[0]));
        Assert.True(ToolstripRules.CanHideLabels(new ToolstripColumn(null, 0, null,
            [new ToolstripItem { Kind = ToolstripItemKind.EmptyControl }, new ToolstripItem { Kind = ToolstripItemKind.PushButton, Text = "a" }, new ToolstripItem { Kind = ToolstripItemKind.ToggleButton, Text = "b" }])));
        Assert.False(ToolstripRules.CanHideLabels(new ToolstripColumn(null, 0, null,
            [new ToolstripItem { Kind = ToolstripItemKind.PushButton, Text = "a" }, new ToolstripItem { Kind = ToolstripItemKind.PushButton }])));
    }

    [Fact]
    public void LabelsAndTooltips()
    {
        Assert.Equal("App\nDetails", ToolstripRules.Label("App\nDetails", vertical: true));
        Assert.Equal("App Details", ToolstripRules.Label("App\nDetails", vertical: false));
        Assert.Equal("Highlight Current Line", ToolstripRules.Label("Highlight \nCurrent Line", vertical: false));
        Assert.Equal("Left/ Right", ToolstripRules.Label("Left/ Right", vertical: false));
        Assert.Equal("Save (Ctrl+S)\nSave App", ToolstripRules.ToolTip("Save", "Save App", "Ctrl+S"));
        Assert.Equal("Specify spacing", ToolstripRules.ToolTip(null, "Specify spacing", null));
        Assert.Equal("Grid", ToolstripRules.ToolTip("Grid", "Grid", null));
    }

    [Fact]
    public void QuickAccessTipsNumberAsR2025bDoes()
    {
        Assert.Equal(["1", "9", "01", "09", "0A", "0Z"], new[] { 0, 8, 9, 17, 18, 43 }.Select(ToolstripRules.QuickAccessKeyTip));
        Assert.Null(ToolstripRules.QuickAccessKeyTip(44));
    }

    // --- the resize against R2025b's Tab.resize -----------------------------------------------------

    private sealed class ModelSection(int priority, double full, double bare, double collapsed) : IToolstripResizable
    {
        public int CollapsePriority { get; } = priority;

        public bool IsCollapsed { get; private set; }

        public bool LabelsHidden { get; private set; }

        public double Width => IsCollapsed ? collapsed : LabelsHidden ? bare : full;

        public string State => IsCollapsed ? "c" : LabelsHidden ? "e-" : "e+";

        public void Collapse() => IsCollapsed = CollapsePriority != -1 || IsCollapsed;

        public void Expand() => IsCollapsed = false;

        public void ShowLabels(bool show) => LabelsHidden = !show;
    }

    [Fact]
    public void ResizesAsR2025bsTabDoes()
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Toolstrip", "ToolstripResizeOracle.json")));
        Assert.Equal(ToolstripResizer.CollapseButtonWidth, oracle.RootElement.GetProperty("collapseButtonWidth").GetDouble());
        int steps = 0;
        var mismatches = new List<string>();
        int scenario = 0;
        foreach (JsonElement s in oracle.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            ModelSection[] sections = [.. s.GetProperty("sections").EnumerateArray().Select(e => new ModelSection(
                e.GetProperty("priority").GetInt32(), e.GetProperty("full").GetDouble(), e.GetProperty("bare").GetDouble(), e.GetProperty("collapsed").GetDouble()))];
            foreach (JsonElement step in s.GetProperty("steps").EnumerateArray())
            {
                double available = step.GetProperty("available").GetDouble();
                ToolstripResizer.Resize(sections, () => available, () => sections.Sum(x => x.Width) + ToolstripResizer.CollapseButtonWidth);
                string[] expected = [.. step.GetProperty("states").EnumerateArray().Select(e => e.GetString()!)];
                string[] actual = [.. sections.Select(x => x.State)];
                if (!expected.SequenceEqual(actual))
                {
                    mismatches.Add($"scenario {scenario} at {available}: R2025b {string.Join(" ", expected)}, JGraph {string.Join(" ", actual)}");
                }

                steps++;
            }

            scenario++;
        }

        Assert.Equal(4800, steps);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(10)));
    }

    [Fact]
    public void ASectionThatNeverCollapsesKeepsItsPlace()
    {
        ModelSection[] sections = [new(0, 100, 80, 40), new(-1, 100, 80, 40), new(0, 100, 80, 40)];
        ToolstripResizer.Resize(sections, () => 150, () => sections.Sum(x => x.Width) + 20);
        Assert.Equal(["c", "e+", "c"], sections.Select(x => x.State));
    }

    [Fact]
    public void AHigherPriorityCollapsesLater()
    {
        ModelSection[] sections = [new(0, 100, 100, 40), new(2, 100, 100, 40), new(1, 100, 100, 40)];
        ToolstripResizer.Resize(sections, () => 260, () => sections.Sum(x => x.Width) + 20);
        Assert.Equal(["c", "e-", "e-"], sections.Select(x => x.State));
        ToolstripResizer.Resize(sections, () => 200, () => sections.Sum(x => x.Width) + 20);
        Assert.Equal(["c", "e-", "c"], sections.Select(x => x.State));
    }

    // --- key tips -----------------------------------------------------------------------------------

    [Fact]
    public void AssignsTipsFromLabelsKeepingTheLayoutsOwn()
    {
        IReadOnlyList<string> tips = ToolstripKeyTips.Assign(
            [(null, "New"), (null, "Open"), ("R", "Run"), ("R", "Pause"), ("DD", "Step"), (null, "Step In"), (null, "Save"), (null, "Stop"), ("Q", "Quit")]);
        Assert.Equal(["N", "O", "R", "R", "DD", "S", "A", "T", "Q"], tips);
    }

    [Fact]
    public void TipsAreNeverPrefixesOfOneAnother()
    {
        var entries = Enumerable.Range(0, 40).Select(i => ((string?)null, (string?)$"Item {(char)('A' + i % 26)}{i}")).ToList();
        entries.Add(("DD", "Step"));
        IReadOnlyList<string> tips = ToolstripKeyTips.Assign(entries);
        Assert.All(tips, t => Assert.NotEqual("", t));
        Assert.Equal(tips.Count, tips.Distinct().Count());
        for (int i = 0; i < tips.Count; i++)
        {
            for (int j = 0; j < tips.Count; j++)
            {
                Assert.True(i == j || !tips[j].StartsWith(tips[i], StringComparison.Ordinal), $"{tips[i]} begins {tips[j]}");
            }
        }
    }

    [Fact]
    public void KeyTipsNarrowCompleteAndGoBack()
    {
        var machine = new ToolstripKeyTips();
        machine.ShowTabs([new("D", "designer", ToolstripKeyTipScope.Tab), new("C", "canvas", ToolstripKeyTipScope.Tab), new("1", "run", ToolstripKeyTipScope.QuickAccess)]);
        Assert.Equal(ToolstripKeyTipMode.Tabs, machine.Mode);
        Assert.Equal(ToolstripKeyTipMatch.None, machine.Type('x').Match);
        (ToolstripKeyTipMatch match, ToolstripKeyTip? tip) = machine.Type('d');
        Assert.Equal(ToolstripKeyTipMatch.Full, match);
        Assert.Equal("designer", tip!.Target);

        machine.ShowControls([new("R", "run", ToolstripKeyTipScope.Control), new("DD", "step", ToolstripKeyTipScope.Control), new("DA", "detail", ToolstripKeyTipScope.Control)]);
        Assert.Equal(ToolstripKeyTipMatch.Partial, machine.Type('D').Match);
        Assert.Equal(["DD", "DA"], machine.Shown.Select(t => t.Tip));
        Assert.Equal(ToolstripKeyTipMode.Controls, machine.Back());
        Assert.Equal("", machine.Typed);
        Assert.Equal(3, machine.Shown.Count());
        Assert.Equal(ToolstripKeyTipMatch.Partial, machine.Type('D').Match);
        Assert.Equal("step", machine.Type('D').Tip!.Target);
        Assert.Equal(ToolstripKeyTipMode.Tabs, machine.Back());
        machine.ShowTabs([new("D", "designer", ToolstripKeyTipScope.Tab)]);
        Assert.Equal(ToolstripKeyTipMode.Off, machine.Back());
        Assert.Empty(machine.Shown);
    }
}
