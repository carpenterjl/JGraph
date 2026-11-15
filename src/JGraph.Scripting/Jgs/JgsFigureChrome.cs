using System.Runtime.CompilerServices;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The menus and the toolbar R2025b gives every figure <c>figure</c> makes (open item 326, ADR 0372;
/// probes p6a and p6b): six menus and the 83 entries under them, and the figure toolbar with its nine
/// tools, each handle hidden. <c>findall(f)</c> and <c>allchild(f)</c> list them; <c>Children</c>,
/// <c>findobj</c> and the drawing do not.
/// <para>
/// They exist for the MATLAB dialect only and only as the handles a script reaches: they are never
/// put in <see cref="FigureModel.Menus"/> or <see cref="FigureModel.Toolbars"/>, so no window draws
/// them, no document saves them and no <c>.fig</c> writes them. A figure has them from the first time a
/// MATLAB-dialect search or listing reaches it; the menus while its <c>MenuBar</c> is <c>'figure'</c>, the
/// toolbar while its <c>ToolBar</c> is <c>'figure'</c>, or <c>'auto'</c> with the menus. A script may
/// read, write and delete them; <c>clf</c> and <c>clf reset</c> leave them, as R2025b's do. A uifigure
/// has none.
/// </para>
/// </summary>
internal static class JgsFigureChrome
{
    private sealed class Chrome
    {
        public required UiToolbarModel Toolbar { get; init; }

        /// <summary>The six menus, back first: File ... Help (allchild lists Help first).</summary>
        public required List<MenuItemModel> Menus { get; init; }

        public bool ToolbarGone { get; set; }
    }

    private static readonly ConditionalWeakTable<FigureModel, Chrome> Figures = new();
    private static readonly ConditionalWeakTable<GraphObject, FigureModel> Owners = new();

    /// <summary>The six menus, front first: tag, then text.</summary>
    private static readonly (string Tag, string Text)[] TopMenus =
    [
        ("figMenuHelp", "&Help"), ("figMenuTools", "&Tools"), ("figMenuInsert", "&Insert"),
        ("figMenuView", "&View"), ("figMenuEdit", "&Edit"), ("figMenuFile", "&File"),
    ];

    /// <summary>The toolbar's tools, front first: toggle, tag, tooltip, separator.</summary>
    private static readonly (bool Toggle, string Tag, string Tip, bool Separator)[] Tools =
    [
        (false, "Standard.OpenInspector", "Open Property Inspector", false),
        (true, "Standard.EditPlot", "Edit Plot", true),
        (true, "Annotation.InsertLegend", "Insert Legend", false),
        (true, "Annotation.InsertColorbar", "Insert Colorbar", true),
        (true, "DataManager.Linking", "Link/Unlink Plot", true),
        (false, "Standard.PrintFigure", "Print Figure", false),
        (false, "Standard.SaveFigure", "Save Figure", false),
        (false, "Standard.FileOpen", "Open File", false),
        (false, "Standard.NewFigure", "New Figure", false),
    ];

    /// <summary>
    /// The entries under the menus, each parent's front first, as R2025b's findall lists them:
    /// parent tag | tag | text | accelerator | separator | enable.
    /// </summary>
    private static readonly string[] Entries =
    [
        "figMenuHelp|figMenuHelpPatents|Pate&nts||off|on",
        "figMenuHelp|figMenuHelpTerms|&Terms of Use||on|on",
        "figMenuHelp|figMenuHelpPrintingExport|Printing and &Exporting||off|on",
        "figMenuHelp|figMenuHelpAnnotatingGraphs|&Annotating Graphs||off|on",
        "figMenuHelp|figMenuHelpPlottingTools|&Plotting Tools||on|on",
        "figMenuHelp|figMenuHelpGraphics|&Graphics Help||off|on",
        "figMenuTools|figMenuToolsDS|&Data Statistics||off|on",
        "figMenuTools|figMenuToolsBF|Basic &Fitting||on|on",
        "figMenuTools|figMenuToolsAlignDistrib|Distri&bute||off|on",
        "figMenuTools|figMenuToolsAlign|Ali&gn||off|on",
        "figMenuTools|figMenuToolsAlignDistributeSmart|&Smart Align and Distribute||on|on",
        "figMenuTools|figMenuViewGrid|View Layout Gr&id||off|on",
        "figMenuTools|figMenuSnapToGrid|Snap To &Layout Grid||off|on",
        "figMenuTools|figMenuEditPinning|Pi&n to Axes||on|on",
        "figMenuTools|figDataManagerBrushTools|B&rushing||off|on",
        "figMenuTools|figLinked|&Link||off|on",
        "figMenuTools|figMenuToolsPlotedit|&Edit Plot||off|on",
        "figMenuInsert|figMenuInsertLight|L&ight||off|off",
        "figMenuInsert|figMenuInsertAxes|&Axes||on|on",
        "figMenuInsert|figMenuInsertEllipse|Elli&pse||off|on",
        "figMenuInsert|figMenuInsertRectangle|Rectan&gle||off|on",
        "figMenuInsert|figMenuInsertTextbox|TextB&ox||off|on",
        "figMenuInsert|figMenuInsertArrow2|Dou&ble Arrow||off|on",
        "figMenuInsert|figMenuInsertTextArrow|T&ext Arrow||off|on",
        "figMenuInsert|figMenuInsertArrow|A&rrow||off|on",
        "figMenuInsert|figMenuInsertLine|Li&ne||on|on",
        "figMenuInsert|figMenuInsertColorbar|&Colorbar||off|off",
        "figMenuInsert|figMenuInsertLegend|&Legend||on|off",
        "figMenuInsert|figMenuInsertTitle|&Title||off|off",
        "figMenuInsert|figMenuInsertZLabel|&Z Label||off|off",
        "figMenuInsert|figMenuInsertYLabel|&Y Label||off|off",
        "figMenuInsert|figMenuInsertXLabel|&X Label||off|off",
        "figMenuView|figMenuObjectBrowser|Object Browser||off|on",
        "figMenuView|figMenuPropertyInspector|Property Inspector||on|on",
        "figMenuView|figMenuPloteditToolbar|&Plot Edit Toolbar||off|on",
        "figMenuView|figMenuCameraToolbar|&Camera Toolbar||off|on",
        "figMenuView|figMenuFigureToolbar|&Figure Toolbar||off|on",
        "figMenuEdit|figMenuEditColormap|Color&map...||off|on",
        "figMenuEdit|figMenuEditGCO|Current Ob&ject Properties...||off|on",
        "figMenuEdit|figMenuEditGCA|&Axes Properties...||off|on",
        "figMenuEdit|figMenuEditGCF|F&igure Properties...||on|on",
        "figMenuEdit|figMenuEditCopyOptions|Copy &Options...||off|on",
        "figMenuEdit|figMenuEditCopyFigure|Copy &Figure||on|on",
        "figMenuEdit|figMenuEditSelectAll|&Select All|A|on|off",
        "figMenuEdit|figMenuEditDelete|Delete||off|on",
        "figMenuEdit|figMenuEditClear|Clea&r Clipboard||off|off",
        "figMenuEdit|figMenuEditPaste|&Paste|V|off|off",
        "figMenuEdit|figMenuEditCopy|&Copy|C|off|off",
        "figMenuEdit|figMenuEditCut|Cu&t|X|on|off",
        "figMenuEdit|figMenuEditRedo|&Redo|Y|off|off",
        "figMenuEdit|figMenuEditUndo|&Undo|Z|off|off",
        "figMenuFile|printMenu|&Print...|P|off|on",
        "figMenuFile|figMenuFilePrintPreview|Print Pre&view...||off|on",
        "figMenuFile|figMenuFileExportSetup|Expo&rt Setup...||on|on",
        "figMenuFile|figMenuGenerateCode|Generate Code...||off|on",
        "figMenuFile|figMenuFileSaveAs|Save &As...||off|on",
        "figMenuFile|figMenuFileSave|&Save|S|on|on",
        "figMenuFile|figMenuFileClose|&Close|W|off|on",
        "figMenuFile|figMenuOpen|&Open...|O|off|on",
        "figMenuFile|figMenuNewFigure|New Figure||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeHRight|Horizontal Right Edges||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeHCent|Horizontal Centers||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeHLeft|Horizontal Left Edges||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeHAdj|Horizontal Adjacent Edges||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeVBot|Vertical Bottom Edges||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeVMid|Vertical Middles||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeVTop|Vertical Top Edges||off|on",
        "figMenuToolsAlignDistrib|figMenuToolsDistributeVAdj|Vertical Adjacent Edges||off|on",
        "figMenuToolsAlign|figMenuToolsAlignBottom|Bottom Edges||off|on",
        "figMenuToolsAlign|figMenuToolsAlignMiddle|Middles (Y)||off|on",
        "figMenuToolsAlign|figMenuToolsAlignTop|Top Edges||off|on",
        "figMenuToolsAlign|figMenuToolsAlignRight|Right Edges||off|on",
        "figMenuToolsAlign|figMenuToolsAlignCenter|Centers (X)||off|on",
        "figMenuToolsAlign|figMenuToolsAlignLeft|Left Edges||off|on",
        "figDataManagerBrushTools|figDataManagerCopy|Copy to clipboard||on|on",
        "figDataManagerBrushTools|figDataManagerPaste|Paste Data to Command Line||on|on",
        "figDataManagerBrushTools|figDataManagerNewVar|Export brushed...||off|on",
        "figDataManagerBrushTools|figDataManagerRemoveUnbr|Remove unbrushed||off|on",
        "figDataManagerBrushTools|figDataManagerRemove|Remove brushed||off|on",
        "figDataManagerBrushTools|figDataManagerColor|Color...||off|on",
        "figDataManagerBrushTools|figDataManagerBrush|Replace with||off|on",
        "figDataManagerBrush|figDataManagerReplaceConst|Constant...||off|on",
        "figDataManagerBrush|figDataManagerReplaceNaN|NaNs||off|on",
    ];

    /// <summary>Whether <paramref name="target"/> is one of a figure's default menus, entries, toolbar or tools.</summary>
    public static bool IsChrome(GraphObject target) => Owners.TryGetValue(target, out _);

    /// <summary>The figure a top-level default menu or the default toolbar belongs to, else null.</summary>
    public static FigureModel? OwnerOf(GraphObject target) =>
        target is MenuItemModel { Parent: null } or UiToolbarModel { Parent: null } && Owners.TryGetValue(target, out FigureModel? figure)
            ? figure
            : null;

    /// <summary>
    /// The default menus and toolbar a figure lists among its children, back first (File ... Help, then
    /// the toolbar), made the first time; empty for a uifigure, outside the MATLAB dialect, and for a
    /// figure whose bars are turned off.
    /// </summary>
    public static IReadOnlyList<GraphObject> ListedFor(FigureModel figure) =>
        JgsRunningDialect.ThreadIsMatlab && !figure.IsUiFigure ? Listing(figure, Figures.GetValue(figure, Make)) : [];

    /// <summary>The default menus and toolbar a figure already has, whatever the dialect (to keep their handles alive).</summary>
    public static IReadOnlyList<GraphObject> ExistingFor(FigureModel figure) =>
        Figures.TryGetValue(figure, out Chrome? had) ? Listing(figure, had) : [];

    private static IReadOnlyList<GraphObject> Listing(FigureModel figure, Chrome chrome)
    {
        bool menus = figure.MenuBar;
        bool toolbar = !chrome.ToolbarGone && figure.ShowsToolBar;
        var listed = new List<GraphObject>(7);
        if (menus)
        {
            listed.AddRange(chrome.Menus);
        }

        if (toolbar)
        {
            listed.Add(chrome.Toolbar);
        }

        return listed;
    }

    /// <summary>Takes a deleted top-level menu or the toolbar off its figure; false for anything else.</summary>
    public static bool Remove(GraphObject target)
    {
        if (OwnerOf(target) is not { } figure || !Figures.TryGetValue(figure, out Chrome? chrome))
        {
            return false;
        }

        if (target is UiToolbarModel)
        {
            chrome.ToolbarGone = true;
        }
        else
        {
            chrome.Menus.Remove((MenuItemModel)target);
        }

        return true;
    }

    private static Chrome Make(FigureModel figure)
    {
        var toolbar = new UiToolbarModel { Tag = "FigureToolBar" };
        Owners.AddOrUpdate(toolbar, figure);
        for (int i = Tools.Length - 1; i >= 0; i--)
        {
            (bool toggle, string tag, string tip, bool separator) = Tools[i];
            UiToolModel tool = toggle ? new UiToggleToolModel() : new UiToolModel();
            tool.Tag = tag;
            tool.Tooltip = UiText.Of(tip);
            tool.Separator = separator;
            toolbar.Tools.Add(tool);
            Owners.AddOrUpdate(tool, figure);
        }

        var byTag = new Dictionary<string, MenuItemModel>(StringComparer.Ordinal);
        var menus = new List<MenuItemModel>(TopMenus.Length);
        foreach ((string tag, string text) in TopMenus)
        {
            var menu = new MenuItemModel { Tag = tag, Text = text };
            byTag[tag] = menu;
            menus.Insert(0, menu); // back first
            Owners.AddOrUpdate(menu, figure);
        }

        // Each parent's entries are written front first; a menu holds its items back first.
        var under = new Dictionary<string, List<MenuItemModel>>(StringComparer.Ordinal);
        foreach (string row in Entries)
        {
            string[] part = row.Split('|');
            var item = new MenuItemModel
            {
                Tag = part[1],
                Text = part[2],
                Accelerator = part[3],
                Separator = part[4] == "on",
                Enable = part[5] == "on",
            };
            byTag[part[1]] = item;
            Owners.AddOrUpdate(item, figure);
            if (!under.TryGetValue(part[0], out List<MenuItemModel>? list))
            {
                under[part[0]] = list = [];
            }

            list.Add(item);
        }

        foreach ((string parent, List<MenuItemModel> items) in under)
        {
            MenuItemModel holder = byTag[parent];
            for (int i = items.Count - 1; i >= 0; i--)
            {
                holder.Items.Add(items[i]);
            }
        }

        // Their handles are minted hidden when first reached (JgsHandleRegistry), and a menu's
        // Children leave out the entries no search has reached yet (VisibleChildrenOf).
        return new Chrome { Toolbar = toolbar, Menus = menus };
    }
}
