using System.Reflection;
using System.Text.Json;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Scripting.Designer;

/// <summary>
/// App Designer's toolstrip (App Designer window plan, stage W2): R2025b's tabs, quick access bar and
/// RUN section as stage W0 recorded them from the client's own tab modules (<c>w0_toolstrip.json</c>,
/// embedded), read by <see cref="ToolstripConfigReader.ReadRecording"/>. The Design View shows the
/// DESIGNER, CANVAS and VIEW tabs, the Code View DESIGNER, EDITOR and its own VIEW tab, as the
/// recording's two contexts name them; SIMULINK is never shown (X1).
/// </summary>
public static class DesignToolstrip
{
    /// <summary>The prefix of every tag in App Designer's toolstrip.</summary>
    public const string Prefix = "appDesignerToolstrip.";

    /// <summary>The DESIGNER tab.</summary>
    public const string DesignerTab = Prefix + "designerTab";

    /// <summary>The SIMULINK tab, shown only for an app bound to a Simulink model.</summary>
    public const string SimulinkTab = Prefix + "simulinkTab";

    /// <summary>The CANVAS tab (Design View).</summary>
    public const string CanvasTab = Prefix + "layoutTab";

    /// <summary>The Design View's VIEW tab.</summary>
    public const string DesignViewTab = Prefix + "designviewTab";

    /// <summary>The EDITOR tab (Code View).</summary>
    public const string EditorTab = Prefix + "editorTab";

    /// <summary>The Code View's VIEW tab.</summary>
    public const string CodeViewTab = Prefix + "viewTab";

    /// <summary>Run, in the RUN section (<c>AppDesignerExecutionControlsToolstripTags</c>).</summary>
    public const string Run = Prefix + "editorTab.run.run";

    /// <summary>Pause, which takes Run's place while the app's code runs.</summary>
    public const string Pause = Prefix + "editorTab.run.pause";

    /// <summary>Continue, which takes Run's place while debugging.</summary>
    public const string Continue = Prefix + "editorTab.run.continue";

    /// <summary>Step.</summary>
    public const string Step = Prefix + "editorTab.run.step";

    /// <summary>Step In, shown only while debugging.</summary>
    public const string StepIn = Prefix + "editorTab.run.stepin";

    /// <summary>Step Out, shown only while debugging.</summary>
    public const string StepOut = Prefix + "editorTab.run.stepout";

    /// <summary>Stop.</summary>
    public const string Stop = Prefix + "editorTab.run.stop";

    /// <summary>The grid interval spinner (VIEW > DISPLAY).</summary>
    public const string IntervalSpinner = Prefix + "layoutTab.view.intervalSpinner";

    /// <summary>The EDITOR tab's INSERT > Callback push button, which has no action of its own.</summary>
    public const string AddCallbackButton = Prefix + "editorTab.edit.addCallbackButton";

    /// <summary>The INSERT > Callback split button a custom component's code shows in its place.</summary>
    public const string AddCallbackSplitButton = Prefix + "editorTab.edit.addCallbackSplitButton";

    /// <summary>The INSERT > Event button, a custom component's.</summary>
    public const string AddEventButton = Prefix + "editorTab.edit.addEvent";

    /// <summary>The DESIGNER tab's Configure for Apps column, a custom component's.</summary>
    public const string ConfigureMetadataColumn = Prefix + "designerTab.share.configureMetaData";

    /// <summary>The Go To popup's FUNCTIONS panel, filled when it opens.</summary>
    public const string GoToFunctionsPanel = Prefix + "editorTab.navigate.gotoPopUpList.panel";

    /// <summary>DESIGNER > New, which opens the Start Page.</summary>
    public const string NewButton = Prefix + "designerTab.file.newButton";

    /// <summary>DESIGNER > Open's popup.</summary>
    public const string OpenMenu = Prefix + "designerTab.file.openButton.menu";

    /// <summary>The Open popup's recent files, filled when it opens (<c>DesignerTab._buildRecentFilesList</c>).</summary>
    public const string RecentFilesPanel = OpenMenu + ".recentFiles.panel";

    /// <summary>The recent files' heading.</summary>
    public const string RecentFilesHeader = OpenMenu + ".recentFiles.header";

    /// <summary>A recent file's item; JGraph adds "." and the file's place in the list, so the window knows which was chosen.</summary>
    public const string RecentFilesItem = OpenMenu + ".recentFiles.item";

    /// <summary>CANVAS > ARRANGE > Reorder, the drop-down.</summary>
    public const string ReorderButton = Prefix + "layoutTab.arrange.reorderButton";

    /// <summary>CANVAS > ARRANGE > Tab Order, the drop-down.</summary>
    public const string TabOrderButton = Prefix + "layoutTab.arrange.tabOrderButton";

    /// <summary>CANVAS > SPACE's box: "Evenly" or the pixels between.</summary>
    public const string SpacingCombo = Prefix + "layoutTab.spacing.combo";

    private static readonly Lazy<Recording> Recorded = new(Read);

    /// <summary>The layout: R2025b's as recorded, with the grid interval spinner's limits and step (<see cref="DesignGeometry"/>).</summary>
    public static ToolstripLayout Layout => Recorded.Value.Layout;

    /// <summary>The tab groups the Design View shows (the recording's <c>AD_APP_DESIGN_VIEW_CONTEXT</c>).</summary>
    public static IReadOnlyList<string> DesignViewGroups => Recorded.Value.DesignGroups;

    /// <summary>The tab groups the Code View shows (<c>AD_APP_CODE_VIEW_CONTEXT</c>).</summary>
    public static IReadOnlyList<string> CodeViewGroups => Recorded.Value.CodeGroups;

    /// <summary>
    /// Which of the RUN section's execution controls show, as R2025b's <c>ExecutionControlsToolstrip</c>
    /// decides: one of Run, Pause and Continue in the first column - Pause while the app's code runs,
    /// else Continue while debugging, else Run - and Step In and Step Out only while debugging. The
    /// framework takes the others out of their columns, so the one left draws large.
    /// </summary>
    public static IReadOnlyList<(string Tag, bool Shown)> ExecutionControls(bool busy, bool debugging)
    {
        string first = busy ? Pause : debugging ? Continue : Run;
        return
        [
            (Run, first == Run),
            (Pause, first == Pause),
            (Continue, first == Continue),
            (StepIn, debugging),
            (StepOut, debugging),
        ];
    }

    /// <summary>
    /// A label or tooltip with the document's kind put in: R2025b's toolstrip words say <c>${0}</c>
    /// for "App" or "Component" ("Save and run current ${0}", "${0}\nHelp Text").
    /// </summary>
    public static string Format(string text, bool component) =>
        text.Replace("${0}", component ? "Component" : "App", StringComparison.Ordinal);

    /// <summary>
    /// The Open popup's recent files as R2025b's <c>DesignerTab._buildRecentFilesList</c> makes them:
    /// nothing with no recent file; else the "Recent Files" heading, then each file by its name with
    /// its full name under it.
    /// </summary>
    public static IReadOnlyList<ToolstripPopupEntry> RecentFilesEntries(IReadOnlyList<DesignRecentFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            return [];
        }

        var entries = new List<ToolstripPopupEntry> { new() { Kind = ToolstripPopupEntryKind.Header, Tag = RecentFilesHeader, Text = DesignStartPage.RecentFiles } };
        for (int i = 0; i < files.Count; i++)
        {
            entries.Add(new()
            {
                Kind = ToolstripPopupEntryKind.Item,
                Tag = RecentFilesItem + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Text = Path.GetFileName(files[i].FilePath),
                Description = files[i].FilePath,
                ShowDescription = true,
                Icon = "packageApp",
            });
        }

        return entries;
    }

    /// <summary>The place in the recent files of the item tagged <paramref name="tag"/>, or -1 when it is not a recent file's item.</summary>
    public static int RecentFileIndex(string? tag) =>
        tag is not null && tag.StartsWith(RecentFilesItem + ".", StringComparison.Ordinal)
        && int.TryParse(tag.AsSpan(RecentFilesItem.Length + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
            ? index : -1;

    private static Recording Read()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-toolstrip.json")
            ?? throw new InvalidOperationException("The App Designer toolstrip recording is missing from the assembly.");
        using JsonDocument recording = JsonDocument.Parse(stream);
        ToolstripLayout layout = ToolstripConfigReader.ReadRecording(recording.RootElement).MapItems(static item => item.Tag == IntervalSpinner
            ? item with { Minimum = DesignGeometry.GridMinimum, Maximum = DesignGeometry.GridMaximum, Step = DesignGeometry.GridStep, Value = Whole(DesignGeometry.GridInterval) }
            : item);

        // The Open popup (W3): the recent files go after Open..., where R2025b adds them each time it
        // opens; the GUIDE App heading and its migration tool are left out (X3).
        var popups = new Dictionary<string, ToolstripPopup>(layout.Popups, StringComparer.Ordinal);
        if (popups.TryGetValue(OpenMenu, out ToolstripPopup? open))
        {
            List<ToolstripPopupEntry> entries = [.. open.Entries.Where(static e => e.ActionId != "AD_OPEN_GUIDE_APP" && e.Tag != OpenMenu + ".guide.header")];
            entries.Insert(Math.Min(1, entries.Count), new() { Kind = ToolstripPopupEntryKind.Panel, Tag = RecentFilesPanel, MaxHeight = 2000 });
            popups[OpenMenu] = open with { Entries = entries };
            layout = layout with { Popups = popups };
        }

        JsonElement contexts = recording.RootElement.GetProperty("documentTypes")[0].GetProperty("properties").GetProperty("contexts");
        return new Recording(layout, Groups(contexts, "AD_APP_DESIGN_VIEW_CONTEXT"), Groups(contexts, "AD_APP_CODE_VIEW_CONTEXT"));
    }

    private static string[] Groups(JsonElement contexts, string id) =>
        [.. contexts.EnumerateArray().First(c => c.GetProperty("contextId").GetString() == id).GetProperty("tabGroupTags").EnumerateArray().Select(static e => e.GetString()!)];

    private static string Whole(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Recording(ToolstripLayout Layout, IReadOnlyList<string> DesignGroups, IReadOnlyList<string> CodeGroups);
}
