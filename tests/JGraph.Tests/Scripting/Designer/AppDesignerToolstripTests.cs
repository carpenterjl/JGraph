using JGraph.Scripting.Designer;
using JGraph.Scripting.Toolstrip;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The designer window's toolstrip, UI-free (App Designer window plan, stage W2): App Designer's
/// layout as embedded (<see cref="DesignToolstrip"/>), its contexts and execution controls as
/// R2025b's <c>ExecutionControlsToolstrip</c> swaps them, the action registry with the stage that
/// runs each action (<see cref="DesignActions"/>), the Keyboard Shortcuts table, the settings by
/// R2025b's names with the window title they make (<see cref="DesignSettings"/>), and the document
/// tab menu with R2025b's path abbreviation (<see cref="DesignTabMenu"/>). The window itself is
/// checked by tools/runtime-checks/run-w2designer.ps1.
/// </summary>
public class AppDesignerToolstripTests
{
    [Fact]
    public void EmbedsAppDesignersToolstripWithTheIntervalsLimits()
    {
        ToolstripLayout layout = DesignToolstrip.Layout;
        Assert.Equal(["DESIGNER", "SIMULINK", "CANVAS", "VIEW", "EDITOR", "VIEW"], layout.Tabs.Select(t => t.Title));
        ToolstripItem spinner = layout.AllItems().First(i => i.Tag == DesignToolstrip.IntervalSpinner);
        Assert.Equal((10.0, 50.0, 5.0, "10"), (spinner.Minimum!.Value, spinner.Maximum!.Value, spinner.Step, spinner.Value));
        Assert.Equal(["RUN", "SAVE", "CUT", "COPY", "PASTE", "UNDO", "REDO", "HELP"], layout.QuickAccess.Select(q => q.ActionId!["AD_".Length..]));
    }

    [Fact]
    public void TheEditorActionsHaveTheIconsR2025bsEditorGivesThem()
    {
        // W0 had stubbed the rich-text editor's icon provider; with rtc/RTCToolstripMetaDataProvider run
        // (W2) the RUN section, CODE and the Code View's VIEW tab carry R2025b's icon names.
        Dictionary<string, string?> icons = DesignToolstrip.Layout.AllItems().Where(i => i.Tag is not null).GroupBy(i => i.Tag!).ToDictionary(g => g.Key, g => g.First().Icon);
        Assert.Equal("stepForward", icons[DesignToolstrip.Step]);
        Assert.Equal("stepForwardIn", icons[DesignToolstrip.StepIn]);
        Assert.Equal("stop", icons[DesignToolstrip.Stop]);
        Assert.Equal("pause", icons[DesignToolstrip.Pause]);
        Assert.Equal("comment", icons["appDesignerToolstrip.editorTab.edit.commentButton"]);
        Assert.Equal("indentCodeAuto", icons["appDesignerToolstrip.editorTab.edit.smartIndentButton"]);
        Assert.Equal("splitDocHorizontally", icons["appDesignerToolstrip.codeViewTab.split.leftRight"]);
        Assert.Equal("arrowGoTo", icons["appDesignerToolstrip.editorTab.navigate.gotoButton"]);
        Assert.DoesNotContain(DesignToolstrip.Layout.AllItems(), i => i.IsButton && i.Icon is null);
    }

    [Fact]
    public void EachViewShowsItsTabGroups()
    {
        Assert.Equal(["appDesignerToolstrip.layoutTabGroup", "appDesignerToolstrip.designviewTabGroup"], DesignToolstrip.DesignViewGroups);
        Assert.Equal(["appDesignerToolstrip.editorTabGroup", "appDesignerToolstrip.viewTabGroup"], DesignToolstrip.CodeViewGroups);
    }

    [Theory]
    [InlineData(false, false, DesignToolstrip.Run, false)]
    [InlineData(true, false, DesignToolstrip.Pause, false)]
    [InlineData(false, true, DesignToolstrip.Continue, true)]
    [InlineData(true, true, DesignToolstrip.Pause, true)]
    public void TheRunColumnShowsOneOfRunPauseContinue(bool busy, bool debugging, string first, bool steps)
    {
        IReadOnlyList<(string Tag, bool Shown)> controls = DesignToolstrip.ExecutionControls(busy, debugging);
        Assert.Equal([first], controls.Where(c => c.Shown && c.Tag is DesignToolstrip.Run or DesignToolstrip.Pause or DesignToolstrip.Continue).Select(c => c.Tag));
        Assert.Equal(steps, controls.Single(c => c.Tag == DesignToolstrip.StepIn).Shown);
        Assert.Equal(steps, controls.Single(c => c.Tag == DesignToolstrip.StepOut).Shown);
    }

    [Fact]
    public void TheDocumentsKindFillsTheWords()
    {
        Assert.Equal("Save and run current App", DesignToolstrip.Format("Save and run current ${0}", component: false));
        Assert.Equal("Component\nHelp Text", DesignToolstrip.Format("${0}\nHelp Text", component: true));
    }

    // --- the action registry ------------------------------------------------------------------------

    [Fact]
    public void RegistersR2025bsActionsAndKnowsEveryToolstripId()
    {
        Assert.Equal(202, DesignActions.All.Count);
        IEnumerable<string> used = DesignToolstrip.Layout.AllItems().Select(i => i.ActionId)
            .Concat(DesignToolstrip.Layout.Popups.Values.SelectMany(p => p.Entries).Select(e => e.ActionId))
            .Concat(DesignToolstrip.Layout.QuickAccess.Select(q => q.ActionId))
            .OfType<string>().Distinct();
        Assert.All(used, id => Assert.NotNull(DesignActions.Find(id)));
    }

    [Theory]
    [InlineData("AD_SAVE", null)]
    [InlineData("AD_RUN", null)]
    [InlineData("AD_DESIGN_GROUP_GROUP", null)]
    [InlineData("AD_EDITOR_ADD_PUBLIC_FUNCTION", null)]
    [InlineData("AD_INSERT_AT_CURSOR", null)]
    [InlineData("AD_NEW_2COL_APP", null)]
    [InlineData("OPEN_STARTSCREEN_DIALOG", null)]
    [InlineData("AD_EXAMPLE_TABLEEXAMPLE", "X8")]
    [InlineData("AD_DESIGN_ALIGN_LEFT", null)]
    [InlineData("AD_DESIGN_REORDER_COMPONENT_FRONT", null)]
    [InlineData("AD_ADD_LABEL", null)]
    [InlineData("APPDES_DESIGN_REORDER_NEXT", "W5")]
    [InlineData("AD_DUPLICATE", "W5")]
    [InlineData("AD_ZOOM_IN", null)]
    [InlineData("AD_DESIGN_VIEW_DISPLAY_SIZE", null)]
    [InlineData("AD_APP_DETAILS", "W8")]
    [InlineData("AD_RESPONSIFY_2COLUMN", "W8")]
    [InlineData("AD_EDITOR_COMMENT", "W9")]
    [InlineData("AD_EDITOR_LINE_NUMBERS", "W9")]
    [InlineData("AD_EDITOR_STEP", "W11")]
    [InlineData("AD_EDITOR_TOGGLE_STOP_IF_ERROR", "W11")]
    [InlineData("AD_SHARE_DESKTOP", "W12")]
    [InlineData("AD_SHARE_MATLAB", "W13")]
    [InlineData("OPEN_FILE_COMPARISON_DIALOG", "W14")]
    [InlineData("AD_CONFIGURE_METADATA", "W15")]
    [InlineData("AD_SHARE_WEB", "X2")]
    [InlineData("AD_OPEN_GUIDE_APP", "X3")]
    [InlineData("AD_SELECT_SIMULINK_MODEL", "X1")]
    [InlineData("AD_EDITOR_SPELL_CHECKER", "X9")]
    public void EachActionWaitsForItsStage(string id, string? stage)
    {
        Assert.Equal(stage, DesignActions.Find(id)!.Stage);
    }

    [Fact]
    public void OnlyACustomComponentsActionsWaitForW15()
    {
        // The Start Page's new components too (W3 leaves them to W15).
        Assert.Equal(
            ["AD_CONFIGURE_METADATA", "AD_EDITOR_ADD_CALLBACK_EVENT_PAIR", "AD_EDITOR_ADD_MANAGED_USER_PROPERTY", "AD_EDITOR_ADD_PRIVATE_MANAGED_USER_PROPERTY", "AD_EDITOR_ADD_PUBLIC_MANAGED_USER_PROPERTY",
             "AD_NEW_COMP_APP", "AD_NEW_COMP_GRID_APP"],
            DesignActions.All.Values.Where(a => a.Stage == "W15").Select(a => a.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheShortcutsDialogIsR2025bsTable()
    {
        Assert.Equal(10, DesignActions.Shortcuts.Count);
        Assert.Equal(new DesignShortcut("Run the active file", "F5"), DesignActions.Shortcuts[0]);
        Assert.Equal(new DesignShortcut("Quit App Designer", "Ctrl + Q"), DesignActions.Shortcuts[2]);
        Assert.Equal(new DesignShortcut("Show this list of shortcuts", "Ctrl + /"), DesignActions.Shortcuts[^1]);
        Assert.DoesNotContain(DesignActions.Shortcuts, s => s.Action.Contains("universal", StringComparison.Ordinal));
    }

    // --- settings -----------------------------------------------------------------------------------

    [Fact]
    public void SettingsStartAtR2025bsFactoryValues()
    {
        var settings = new DesignSettings();
        Assert.Equal(8, DesignSettings.Definitions.Count(d => d.Listed));
        Assert.False(settings.GetBool(DesignSettings.ShowGrid));
        Assert.True(settings.GetBool(DesignSettings.SnapToGrid));
        Assert.True(settings.GetBool(DesignSettings.ShowAlignmentHints));
        Assert.False(settings.GetBool(DesignSettings.ShowResizingHints));
        Assert.Equal(10, settings.GridIntervalValue);
        Assert.Equal(8, settings.GetInt(DesignSettings.MaxFileListSize, 0));
        Assert.Equal("m", settings.Get(DesignSettings.CreateNewAppsAs));
        Assert.True(settings.GetBool(DesignSettings.ShowShortcutsDialog));
    }

    [Fact]
    public void SavedSettingsOverlayTheFactoryOnesAndChangesAreRaisedOnce()
    {
        var settings = new DesignSettings(new Dictionary<string, string> { [DesignSettings.ShowGrid] = "true", [DesignSettings.GridInterval] = "75" });
        Assert.True(settings.GetBool(DesignSettings.ShowGrid));
        Assert.Equal(50, settings.GridIntervalValue); // kept within App Designer's 10 to 50
        List<string> changed = [];
        settings.Changed += (_, name) => changed.Add(name);
        settings.Set(DesignSettings.ShowGrid, true);
        settings.Set(DesignSettings.SnapToGrid, false);
        settings.Set(DesignSettings.SnapToGrid, false);
        Assert.Equal([DesignSettings.SnapToGrid], changed);
    }

    [Fact]
    public void TheTitleIsR2025bs()
    {
        var settings = new DesignSettings();
        string file = Path.Combine("C:", "apps", "Mortgage.mlapp");
        Assert.Equal("App Designer", settings.Title(null, dirty: false));
        Assert.Equal("App Designer - " + file, settings.Title(file, dirty: false));
        Assert.Equal("App Designer - " + file + "*", settings.Title(file, dirty: true));
        settings.Set(DesignSettings.ShowTitleFilePath, false);
        Assert.Equal("App Designer - Mortgage.mlapp", settings.Title(file, dirty: false));
        settings.Set(DesignSettings.ShowTitleFileExtension, false);
        Assert.Equal("App Designer - Mortgage*", settings.Title(file, dirty: true));
    }

    // --- the document tab menu ----------------------------------------------------------------------

    [Fact]
    public void ASavedAppsTabMenuIsR2025bs()
    {
        string file = Path.Combine(Path.GetTempPath(), "apps", "Mortgage.mlapp");
        string folder = Path.GetDirectoryName(file)!;
        IReadOnlyList<DesignTabMenuItem> items = DesignTabMenu.Items(file, "Mortgage.mlapp", 0, 1, _ => false, _ => false);
        Assert.Equal(
            [DesignTabMenuCommand.ChangeCurrentFolder, DesignTabMenuCommand.AddToSearchPath, DesignTabMenuCommand.ShowInExplorer, DesignTabMenuCommand.CopyFullPath,
             DesignTabMenuCommand.NewApp, DesignTabMenuCommand.CompareTo, null, DesignTabMenuCommand.Close],
            items.Select(i => i.Command));
        Assert.Equal($"Change Current Folder to {DesignTabMenu.Abbreviate(folder)}", items[0].Text);
        Assert.Equal("Close Mortgage.mlapp", items[^1].Text);

        IReadOnlyList<DesignTabMenuItem> here = DesignTabMenu.Items(file, "Mortgage.mlapp", 0, 1, f => f == folder, f => f == folder);
        Assert.False(here[0].Enabled);
        Assert.Equal(DesignTabMenuCommand.RemoveFromSearchPath, here[1].Command);
        Assert.Equal($"Remove {DesignTabMenu.Abbreviate(folder)} from Search Path", here[1].Text);
    }

    [Fact]
    public void WithSeveralTabsTheContainerAddsItsCloseItems()
    {
        IReadOnlyList<DesignTabMenuItem> middle = DesignTabMenu.Items(null, "app2.mlapp", 1, 3, _ => false, _ => false);
        Assert.Equal(
            [DesignTabMenuCommand.NewApp, DesignTabMenuCommand.CompareTo, null, DesignTabMenuCommand.Close, DesignTabMenuCommand.CloseAllExcept,
             DesignTabMenuCommand.CloseAllToTheRight, DesignTabMenuCommand.CloseAll, null, DesignTabMenuCommand.Alphabetize],
            middle.Select(i => i.Command));
        Assert.Equal("Close All Except app2.mlapp", middle[4].Text);
        Assert.DoesNotContain(DesignTabMenu.Items(null, "app3.mlapp", 2, 3, _ => false, _ => false), i => i.Command == DesignTabMenuCommand.CloseAllToTheRight);
    }

    [Theory]
    [InlineData(@"C:\apps\demo", @"C:\apps\demo")]
    [InlineData(@"C:\Users\Carpe\Documents\MATLAB\apps\demo", @"C:\Users\Carpe\...\apps\demo")]
    [InlineData(@"D:\EE Projects\JGraph\JGraph\examples\hello-app", @"D:\EE Projects\...\hello-app")]
    [InlineData("/home/someone/projects/matlab/apps/demo", "/home/someone/.../apps/demo")]
    public void AbbreviatesAFolderAsR2025bDoes(string folder, string shown)
    {
        Assert.Equal(shown, DesignTabMenu.Abbreviate(folder));
    }
}
