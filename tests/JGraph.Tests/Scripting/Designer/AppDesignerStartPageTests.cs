using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Toolstrip;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The Start Page and new apps, UI-free (App Designer window plan, stage W3): the page as R2025b's
/// <c>StartScreenConfig</c> and <c>StartScreenActionDefinitions</c> make it (<see cref="DesignStartPage"/>),
/// the recent files as its <c>MRUArray</c> keeps them (<see cref="DesignRecentFiles"/>), new
/// documents' names as <c>_AppFactory</c> gives them, the Open drop-down's recent files, and
/// JGraph's example apps: each as the designer writes it, copied out as <c>.mlapp</c> files, and
/// run in a MATLAB session with its callbacks driven. The window is checked by
/// tools/runtime-checks/run-w3designer.ps1.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerStartPageTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-w3", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _directory)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    // --- the page ----------------------------------------------------------------------------------

    [Fact]
    public void ThePagesSectionsAreR2025bsLessSimulink()
    {
        Assert.Equal(["Apps", "Custom UI Components"], DesignStartPage.Sections.Select(s => s.Title));
        DesignStartSection apps = DesignStartPage.Sections[0];
        Assert.Equal(["Blank App", "2-Panel App with Auto-Reflow", "3-Panel App with Auto-Reflow"], apps.Tiles.Select(t => t.Text));
        Assert.Equal("Create new blank app", apps.Tiles[0].Description);
        Assert.All(apps.Tiles, t => Assert.True(t.IsLive));
        Assert.Equal([DesignStartPage.GeneralExamples, DesignStartPage.ProgrammingExamples], apps.Examples.Select(g => g.Title));
        Assert.Equal(["Respond to Numerical Input", "Lay Out Controls in a Grid"], apps.Examples[0].Examples.Select(e => e.Title));
        Assert.Equal(["Link Data to a Tree", "Analyze an Image", "Configure a Timer", "Display Specialized Axes", "Create a Table", "Pass Data Between Apps"],
            apps.Examples[1].Examples.Select(e => e.Title));

        // A new custom component waits for W15; its section has no examples, so R2025b drops that part.
        DesignStartSection components = DesignStartPage.Sections[1];
        Assert.Equal(["Blank Component", "Blank Component with Grid Layout"], components.Tiles.Select(t => t.Text));
        Assert.All(components.Tiles, t => Assert.Equal("W15", t.Stage));
        Assert.Empty(components.Examples);
        Assert.Throws<NotSupportedException>(() => DesignStartPage.NewText(DesignNewKind.BlankComponent, "comp1"));
    }

    [Fact]
    public void TheTilesWordsAreTheRegistrysAndTheExamplesAreJGraphsOwn()
    {
        foreach (DesignStartTile tile in DesignStartPage.Sections.SelectMany(s => s.Tiles))
        {
            DesignAction action = DesignActions.Find(tile.ActionId)!;
            Assert.Equal((action.Text, action.Description), (tile.Text, tile.Description));
        }

        // R2025b's own example and tutorial actions are MathWorks' (X8); New is live.
        Assert.Equal("X8", DesignActions.Find("AD_EXAMPLE_MORTGAGE")!.Stage);
        Assert.Equal("X8", DesignActions.Find("AD_TUTORIAL_BASICCODING")!.Stage);
        Assert.True(DesignActions.IsLive("OPEN_STARTSCREEN_DIALOG"));
        Assert.Equal("W15", DesignActions.Find("AD_NEW_COMP_GRID_APP")!.Stage);
        Assert.Equal(["Getting Started", "GUIDE Migration Strategies", "Display Graphics in App Designer", "Find and Create UI Components"],
            DesignStartPage.Links.Select(l => l.Text));
        Assert.Equal(("App", "Custom UI Component"), (DesignStartPage.EntryType(DesignAppType.Responsive), DesignStartPage.EntryType(DesignAppType.CustomUIComponent)));
    }

    [Fact]
    public void NewDocumentsAreNamedAsR2025bNamesThem()
    {
        // One count for every app kind, its own for components, from 1 each session.
        var names = new DesignNewNames();
        Assert.Equal(["app1", "app2", "comp1", "app3", "comp2"],
            new[] { DesignNewKind.BlankApp, DesignNewKind.ThreePanelApp, DesignNewKind.BlankComponent, DesignNewKind.TwoPanelApp, DesignNewKind.BlankComponentWithGrid }
                .Select(names.Next));

        using DesignDocument blank = DesignDocument.FromText(DesignStartPage.NewText(DesignNewKind.BlankApp, "app4"), null);
        Assert.Equal("app4", blank.Code!.ClassName);
        Assert.Null(blank.Path);
        Assert.False(blank.Design.IsResponsive);
        using DesignDocument three = DesignDocument.FromText(DesignStartPage.NewText(DesignNewKind.ThreePanelApp, "app5"), null);
        Assert.Equal("app5", three.Code!.ClassName);
        Assert.True(three.Design.IsResponsive);
        Assert.NotNull(three.Design.Find("CenterPanel"));
        Assert.Contains("classdef app5 < matlab.apps.AppBase", three.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewAppsTitleIsItsNameAndExtension()
    {
        var settings = new DesignSettings();
        Assert.Equal("App Designer - app1.mlapp*", settings.Title("app1.mlapp", dirty: true, isNew: true));
        settings.Set(DesignSettings.ShowTitleFilePath, false);
        settings.Set(DesignSettings.ShowTitleFileExtension, false);
        Assert.Equal("App Designer - app1", settings.Title("app1.mlapp", dirty: false, isNew: true));
    }

    // --- recent files ----------------------------------------------------------------------------

    [Fact]
    public void RecentFilesKeepMRUArraysOrder()
    {
        var files = new DesignRecentFiles([], 3);
        files.Push(new(@"C:\a\One.mlapp", DesignAppType.Standard));
        files.Push(new(@"C:\a\Two.mlapp", DesignAppType.Responsive));
        files.Push(new(@"C:\a\Three.mlapp", DesignAppType.Standard));
        files.Push(new(@"C:\A\ONE.mlapp", DesignAppType.Standard)); // the same file on Windows: moved first
        Assert.Equal([@"C:\A\ONE.mlapp", @"C:\a\Three.mlapp", @"C:\a\Two.mlapp"], files.Files.Select(f => f.FilePath));
        files.Push(new(@"C:\a\Four.mlapp", DesignAppType.Standard)); // full: the oldest goes
        Assert.Equal([@"C:\a\Four.mlapp", @"C:\A\ONE.mlapp", @"C:\a\Three.mlapp"], files.Files.Select(f => f.FilePath));
        Assert.True(files.Remove(@"c:\a\three.mlapp"));
        Assert.False(files.Remove(@"C:\a\Nine.mlapp"));
        files.SetSize(1);
        Assert.Equal([@"C:\a\Four.mlapp"], files.Files.Select(f => f.FilePath));
        files.SetSize(0);
        files.Push(new(@"C:\a\Five.mlapp", DesignAppType.Standard));
        Assert.Empty(files.Files);
    }

    [Fact]
    public void RecentFilesAreKeptAsR2025bWritesThem()
    {
        var files = new DesignRecentFiles([], 8);
        files.Push(new(@"C:\apps\Two Panel.mlapp", DesignAppType.Responsive));
        Assert.Equal([@"{""apptype"":""Responsive"",""filepath"":""C:\\apps\\Two Panel.mlapp""}"], files.Entries());

        // Read back, with an older bare path and an empty entry, which MRUArray drops.
        var read = new DesignRecentFiles([.. files.Entries(), @"D:\old\Bare.mlapp", ""], 8);
        Assert.Equal([new DesignRecentFile(@"C:\apps\Two Panel.mlapp", DesignAppType.Responsive), new DesignRecentFile(@"D:\old\Bare.mlapp", DesignAppType.Standard)],
            read.Files);

        var settings = new DesignSettings();
        settings.Set(DesignSettings.MaxFileListSize, 1);
        settings.SetRecentFiles(read);
        Assert.Equal([@"C:\apps\Two Panel.mlapp"], settings.RecentFiles().Files.Select(f => f.FilePath));
    }

    [Fact]
    public void OpensDropDownListsTheRecentFilesAfterOpenAndNoGuide()
    {
        ToolstripPopup open = DesignToolstrip.Layout.Popups[DesignToolstrip.OpenMenu];
        Assert.Equal([ToolstripPopupEntryKind.Item, ToolstripPopupEntryKind.Panel], open.Entries.Select(e => e.Kind));
        Assert.Equal(("AD_OPEN", DesignToolstrip.RecentFilesPanel), (open.Entries[0].ActionId, open.Entries[1].Tag));

        Assert.Empty(DesignToolstrip.RecentFilesEntries([]));
        IReadOnlyList<ToolstripPopupEntry> entries = DesignToolstrip.RecentFilesEntries(
            [new(@"C:\apps\One.mlapp", DesignAppType.Standard), new(@"C:\apps\Two.m", DesignAppType.Standard)]);
        Assert.Equal(["Recent Files", "One.mlapp", "Two.m"], entries.Select(e => e.Text));
        Assert.Equal(@"C:\apps\Two.m", entries[2].Description);
        Assert.True(entries[2].ShowDescription);
        Assert.Equal(1, DesignToolstrip.RecentFileIndex(entries[2].Tag));
        Assert.Equal(-1, DesignToolstrip.RecentFileIndex(DesignToolstrip.RecentFilesHeader));
    }

    // --- the example apps ------------------------------------------------------------------------

    /// <summary>
    /// Each example app is carried whole and is what the designer writes: read with nothing left
    /// over, its canvas made, and its class text the designer's own. With <c>JGRAPH_W3_NORMALIZE</c>
    /// pointing at the repository's <c>examples/apps</c>, the files are rewritten in the designer's
    /// form instead.
    /// </summary>
    [Fact]
    public void EachExampleAppIsAsTheDesignerWritesIt()
    {
        string? normalize = Environment.GetEnvironmentVariable("JGRAPH_W3_NORMALIZE");
        foreach (DesignExample example in DesignStartPage.Examples)
        {
            IReadOnlyList<(string File, string Text)> files = DesignStartPage.ExampleFiles(example);
            Assert.Equal(example.Apps.Select(a => a + ".m").Order(), files.Select(f => f.File).Order());
            foreach ((string file, string text) in files)
            {
                using DesignDocument document = DesignDocument.FromText(text, null);
                Assert.Empty(document.ReadProblems);
                Assert.NotNull(document.Code);
                Assert.Empty(document.Runtime.Problems);
                string written = document.CurrentText;
                if (normalize is not null && written != text)
                {
                    File.WriteAllText(Path.Combine(normalize, example.Folder, file), written);
                    output.WriteLine($"rewrote {example.Folder}/{file}");
                    continue;
                }

                if (written != text)
                {
                    output.WriteLine(written);
                }

                Assert.Equal(text, written);
            }
        }
    }

    [Fact]
    public void AnExampleIsCopiedOutAsAppsAndAnEditedCopyIsKept()
    {
        DesignExample example = DesignStartPage.Examples.Single(e => e.Folder == "MultiWindowExample");
        string main = DesignStartPage.CopyExample(example, _directory);
        Assert.Equal(Path.Combine(_directory, "MultiWindowExample", "MainApp.mlapp"), main);
        Assert.True(File.Exists(Path.Combine(_directory, "MultiWindowExample", "OptionsDialog.mlapp")));
        using (DesignDocument opened = DesignDocument.Open(main))
        {
            Assert.Empty(opened.ReadProblems);
            Assert.Equal("MainApp", opened.Code!.ClassName);
        }

        DateTime written = File.GetLastWriteTimeUtc(main);
        File.SetLastWriteTimeUtc(main, written.AddHours(-1));
        Assert.Equal(main, DesignStartPage.CopyExample(example, _directory));
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(main));
    }

    /// <summary>
    /// The examples run: each copied out and constructed in a MATLAB session, its callbacks driven as
    /// a user's clicks would drive them, and what they do read back.
    /// </summary>
    [Fact]
    public async Task TheExampleAppsRunAndRespond()
    {
        foreach (DesignExample example in DesignStartPage.Examples)
        {
            string main = DesignStartPage.CopyExample(example, _directory);
            await Exec($"addpath('{Path.GetDirectoryName(main)}');");
        }

        await Exec("a = SavingsApp; a.YearsField.Value = 2; f = a.YearsField.ValueChangedFcn; f(a.YearsField, []);");
        Assert.Equal("Balance after 2 years: 2529.09", await Eval("a.BalanceLabel.Text")); // R2025b's, from ui-probes/w3/w3_examples
        Assert.Equal("24", await Eval("numel(a.UIAxes.Children(1).XData)"));

        await Exec("a = PulseApp; a.ShapeDropDown.Value = 'Triangle'; f = a.ShapeDropDown.ValueChangedFcn; f(a.ShapeDropDown, []);");
        Assert.Equal("1", await Eval("max(a.UIAxes.Children(1).YData)"));

        await Exec("a = PartsTreeApp; n = a.Tree.Children(3).Children(1); a.Tree.SelectedNodes = n; f = a.Tree.SelectionChangedFcn; f(a.Tree, struct('SelectedNodes', n));");
        Assert.Equal("Bearing 608 Bearings 16", await Eval("[a.NameValue.Text ' ' a.CategoryValue.Text ' ' num2str(a.QuantityField.Value)]"));
        await Exec("n.Text = 'Bearing 608-2RS'; g = a.Tree.NodeTextChangedFcn; g(a.Tree, struct('Node', n, 'PreviousText', 'Bearing 608'));");
        Assert.Equal("Bearing 608-2RS", await Eval("n.NodeData.Name"));

        await Exec("a = ImageApp; a.ChannelDropDown.Value = 'Gray'; f = a.ChannelDropDown.ValueChangedFcn; f(a.ChannelDropDown, []);");
        Assert.Equal("40000", await Eval("sum(a.HistogramAxes.Children(1).Values)"));

        await Exec("a = TimerApp; f = a.StartButton.ButtonPushedFcn; f(a.StartButton, []);");
        Assert.Equal("on off", await Eval("[char(a.StopButton.Enable) ' ' char(a.StartButton.Enable)]"));
        await Exec("g = a.StopButton.ButtonPushedFcn; g(a.StopButton, []);");
        Assert.Equal("off on", await Eval("[char(a.StopButton.Enable) ' ' char(a.StartButton.Enable)]"));
        await Exec("close(a.UIFigure);"); // the close request stops and deletes the timer, then the app
        Assert.Equal("false", await Eval("isvalid(a)"));

        await Exec("a = PolarApp; a.LobesSpinner.Value = 6; f = a.LobesSpinner.ValueChangedFcn; f(a.LobesSpinner, []);");
        Assert.Equal("polaraxes", await Eval("a.PlotPanel.Children(1).Type"));

        await Exec("a = TableApp; f = a.UITable.CellEditCallback; a.UITable.Data{1, 2} = -1; f(a.UITable, struct('Indices', [1 2], 'NewData', -1, 'PreviousData', 0.5));");
        Assert.Equal("0.5", await Eval("a.UITable.Data.Price(1)"));
        await Exec("g = a.AddButton.ButtonPushedFcn; g(a.AddButton, []);");
        Assert.Equal("Total: 15.85", await Eval("a.TotalLabel.Text"));

        await Exec("a = MainApp; f = a.OptionsButton.ButtonPushedFcn; f(a.OptionsButton, []);");
        Assert.Equal("off", await Eval("char(a.OptionsButton.Enable)"));
        await Exec("d = get(findall(groot, 'Type', 'figure', 'Name', 'Options'), 'RunningAppInstance'); d.ColorDropDown.Value = 'Black'; d.WidthSpinner.Value = 3; g = d.OKButton.ButtonPushedFcn; g(d.OKButton, []);");
        Assert.Equal("0 0 0 3 on", await Eval("sprintf('%g %g %g %g %s', a.UIAxes.Children(1).Color, a.UIAxes.Children(1).LineWidth, char(a.OptionsButton.Enable))"));
        Assert.Equal("false", await Eval("isvalid(d)"));
    }

    private async Task<string> Eval(string expression)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync($"disp({expression})", sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
