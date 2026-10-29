using System.Reflection;
using System.Text.Json;
using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Designer;

/// <summary>What kind of app a file holds, as R2025b's <c>AppTypes</c> names it in a recent-file entry.</summary>
public enum DesignAppType
{
    /// <summary>An app laid out by positions or grids (<c>Standard</c>).</summary>
    Standard,

    /// <summary>A 2- or 3-panel app with auto-reflow (<c>Responsive</c>).</summary>
    Responsive,

    /// <summary>A custom UI component (<c>CustomUIComponent</c>).</summary>
    CustomUIComponent,
}

/// <summary>The new documents the Start Page's tiles make (R2025b's <c>NewActionTileList</c>s).</summary>
public enum DesignNewKind
{
    /// <summary>Blank App (<c>AD_NEW</c>).</summary>
    BlankApp,

    /// <summary>2-Panel App with Auto-Reflow (<c>AD_NEW_2COL_APP</c>).</summary>
    TwoPanelApp,

    /// <summary>3-Panel App with Auto-Reflow (<c>AD_NEW_3COL_APP</c>).</summary>
    ThreePanelApp,

    /// <summary>Blank Component (<c>AD_NEW_COMP_APP</c>).</summary>
    BlankComponent,

    /// <summary>Blank Component with Grid Layout (<c>AD_NEW_COMP_GRID_APP</c>).</summary>
    BlankComponentWithGrid,
}

/// <summary>A tile of the Start Page that makes a new document.</summary>
/// <param name="Kind">What it makes.</param>
/// <param name="ActionId">R2025b's action id, which the toolstrip and the registry know it by.</param>
/// <param name="Text">Its title, R2025b's words.</param>
/// <param name="Description">Its description, R2025b's words.</param>
public sealed record DesignStartTile(DesignNewKind Kind, string ActionId, string Text, string Description)
{
    /// <summary>Whether JGraph makes it: the app kinds since stage W3; a custom component waits for W15.</summary>
    public bool IsLive => DesignActions.IsLive(ActionId);

    /// <summary>The stage that will make it, or null when it is live.</summary>
    public string? Stage => DesignActions.Find(ActionId)?.Stage;
}

/// <summary>
/// One of JGraph's example apps (decision WO2): written for JGraph on the topics of R2025b's
/// examples, opened as a copy as <c>openExample</c> opens R2025b's.
/// </summary>
/// <param name="Folder">Its folder under <c>examples/apps</c>, and the copy's folder's name.</param>
/// <param name="Title">Its tile's title: R2025b's words for the topic.</param>
/// <param name="Description">Its tile's description: R2025b's words for the topic.</param>
/// <param name="MainApp">The app the tile opens (a class name; its file is <c>MainApp.m</c> in the folder).</param>
/// <param name="Apps">Every app of the example, written as <c>.mlapp</c> files in the copy.</param>
public sealed record DesignExample(string Folder, string Title, string Description, string MainApp, IReadOnlyList<string> Apps);

/// <summary>A group of example tiles under its title ("Examples: General").</summary>
public sealed record DesignExampleGroup(string Title, IReadOnlyList<DesignExample> Examples);

/// <summary>A section of the Start Page: its title, its new-document tiles and its example groups.</summary>
public sealed record DesignStartSection(string Title, IReadOnlyList<DesignStartTile> Tiles, IReadOnlyList<DesignExampleGroup> Examples);

/// <summary>A link of the Start Page's links bar: R2025b's words, and the scripting guide's section it opens.</summary>
public sealed record DesignStartLink(string Text, string GuideAnchor);

/// <summary>
/// App Designer's Start Page (App Designer window plan, stage W3), as R2025b's client builds it
/// (<c>appdesigner/view/dialogs/startscreen</c>: <c>StartScreenConfig</c>, <c>StartScreenCenter</c>,
/// <c>AppTypeSectionWidget</c>, <c>OpenMenu</c>; the words from <c>StartScreenActionDefinitions</c>
/// and the <c>Dialogs</c> catalog). The sections are R2025b's, less Simulink Apps (X1); their
/// examples are JGraph's own (WO2) under R2025b's group titles, and the links open the scripting
/// guide (the links to MathWorks' course, videos and release notes are left out).
/// </summary>
public static class DesignStartPage
{
    /// <summary>The dialog's title.</summary>
    public const string Title = "App Designer Start Page";

    /// <summary>The examples toggle while they are hidden.</summary>
    public const string ShowExamples = "Show examples";

    /// <summary>The examples toggle while they are shown.</summary>
    public const string HideExamples = "Hide examples";

    /// <summary>The recent files' heading.</summary>
    public const string RecentFiles = "Recent Files";

    /// <summary>What the recent files say when there are none.</summary>
    public const string NoRecentFiles = "No Recent Files";

    /// <summary>The open menu's button: the Open action's words and "...".</summary>
    public const string OpenButton = "Open...";

    /// <summary>The group of the examples of general tasks.</summary>
    public const string GeneralExamples = "Examples: General";

    /// <summary>The group of the examples of programming tasks.</summary>
    public const string ProgrammingExamples = "Examples: Programming Tasks";

    /// <summary>JGraph's example apps, in R2025b's order for their topics.</summary>
    public static IReadOnlyList<DesignExample> Examples { get; } =
    [
        new("NumericInputExample", "Respond to Numerical Input", "Calculate and plot data based on user input", "SavingsApp", ["SavingsApp"]),
        new("GridLayoutExample", "Lay Out Controls in a Grid", "Use Grid Layout to organize and lay out controls", "PulseApp", ["PulseApp"]),
        new("TreeExample", "Link Data to a Tree", "Display, select, and edit data from a hierarchical tree", "PartsTreeApp", ["PartsTreeApp"]),
        new("ImageExample", "Analyze an Image", "Add and analyze an image programmatically", "ImageApp", ["ImageApp"]),
        new("TimerExample", "Configure a Timer", "Create and configure a timer object programmatically", "TimerApp", ["TimerApp"]),
        new("PolarAxesExample", "Display Specialized Axes", "Create axes programmatically", "PolarApp", ["PolarApp"]),
        new("TableExample", "Create a Table", "Create an interactive table", "TableApp", ["TableApp"]),
        new("MultiWindowExample", "Pass Data Between Apps", "Pass data between two apps using public functions", "MainApp", ["MainApp", "OptionsDialog"]),
    ];

    /// <summary>
    /// The sections, as <c>StartScreenConfig</c> lists them: Apps (Blank App and the two auto-reflow
    /// apps, then the examples of general and of programming tasks) and Custom UI Components (the two
    /// blank components; JGraph has no component examples, and R2025b drops an empty examples part).
    /// </summary>
    public static IReadOnlyList<DesignStartSection> Sections { get; } =
    [
        new("Apps",
            [Tile(DesignNewKind.BlankApp), Tile(DesignNewKind.TwoPanelApp), Tile(DesignNewKind.ThreePanelApp)],
            [
                new(GeneralExamples, Examples.Take(2).ToArray()),
                new(ProgrammingExamples, Examples.Skip(2).ToArray()),
            ]),
        new("Custom UI Components", [Tile(DesignNewKind.BlankComponent), Tile(DesignNewKind.BlankComponentWithGrid)], []),
    ];

    /// <summary>
    /// The links bar, R2025b's words where JGraph's guide has the topic: Getting Started, GUIDE
    /// Migration Strategies, Display Graphics in App Designer, Find and Create UI Components.
    /// </summary>
    public static IReadOnlyList<DesignStartLink> Links { get; } =
    [
        new("Getting Started", "apps-designer"),
        new("GUIDE Migration Strategies", "apps-guide"),
        new("Display Graphics in App Designer", "apps-uifigure"),
        new("Find and Create UI Components", "apps-design"),
    ];

    /// <summary>The tile that makes <paramref name="kind"/>, with R2025b's words.</summary>
    public static DesignStartTile Tile(DesignNewKind kind) => kind switch
    {
        DesignNewKind.BlankApp => new(kind, "AD_NEW", "Blank App", "Create new blank app"),
        DesignNewKind.TwoPanelApp => new(kind, "AD_NEW_2COL_APP", "2-Panel App with Auto-Reflow",
            "Create a new 2-panel app that automatically resizes and reflows its layout to accommodate different screen sizes"),
        DesignNewKind.ThreePanelApp => new(kind, "AD_NEW_3COL_APP", "3-Panel App with Auto-Reflow",
            "Create a new 3-panel app that automatically resizes and reflows its layout to accommodate different screen sizes"),
        DesignNewKind.BlankComponent => new(kind, "AD_NEW_COMP_APP", "Blank Component", "Create new blank component"),
        DesignNewKind.BlankComponentWithGrid => new(kind, "AD_NEW_COMP_GRID_APP", "Blank Component with Grid Layout", "Create new blank component with Grid Layout"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The word under a recent file on the Start Page (<c>OpenMenu._getMruEntryDescription</c>).</summary>
    public static string EntryType(DesignAppType type) => type == DesignAppType.CustomUIComponent ? "Custom UI Component" : "App";

    /// <summary>
    /// The class text of a new document of <paramref name="kind"/> named <paramref name="name"/>: a
    /// blank app as <c>StandardAppFactory</c> makes it, a 2- or 3-panel app as
    /// <c>ResponsiveAppFactory</c> does (designer plan DS4b). A custom component waits for W15.
    /// </summary>
    /// <exception cref="NotSupportedException">The kind is a custom component.</exception>
    public static string NewText(DesignNewKind kind, string name) => kind switch
    {
        DesignNewKind.BlankApp => AppDesignerDocument.NewAppText(name),
        DesignNewKind.TwoPanelApp => DesignResponsive.NewAppText(2, name),
        DesignNewKind.ThreePanelApp => DesignResponsive.NewAppText(3, name),
        _ => throw new NotSupportedException("A new custom UI component waits for stage W15 of the App Designer window plan."),
    };

    /// <summary>
    /// Writes <paramref name="example"/> into <paramref name="root"/> as <c>openExample</c> sets R2025b's
    /// up: a folder of the example's name holding its apps as <c>.mlapp</c> files (and any other file
    /// of its folder as it is). A copy already there is kept as the user left it.
    /// </summary>
    /// <returns>The main app's file in the copy.</returns>
    /// <exception cref="IOException">The copy could not be written.</exception>
    public static string CopyExample(DesignExample example, string root)
    {
        ArgumentNullException.ThrowIfNull(example);
        ArgumentNullException.ThrowIfNull(root);
        string folder = Path.Combine(root, example.Folder);
        string main = Path.Combine(folder, example.MainApp + ".mlapp");
        if (File.Exists(main))
        {
            return main;
        }

        Directory.CreateDirectory(folder);
        foreach ((string file, string text) in ExampleFiles(example))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (example.Apps.Contains(name, StringComparer.Ordinal))
            {
                AppDesignerDocument.SaveNew(Path.Combine(folder, name + ".mlapp"), text);
            }
            else
            {
                File.WriteAllText(Path.Combine(folder, file), text);
            }
        }

        return main;
    }

    /// <summary>The files of <paramref name="example"/>'s folder as JGraph carries them: each file's name and text.</summary>
    public static IReadOnlyList<(string File, string Text)> ExampleFiles(DesignExample example)
    {
        ArgumentNullException.ThrowIfNull(example);
        string prefix = ExamplePrefix + example.Folder + "/";
        Assembly assembly = Assembly.GetExecutingAssembly();
        var files = new List<(string, string)>();
        foreach (string resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            string normal = resource.Replace('\\', '/');
            if (normal.StartsWith(prefix, StringComparison.Ordinal))
            {
                using Stream stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                files.Add((normal[prefix.Length..], reader.ReadToEnd()));
            }
        }

        return files;
    }

    private const string ExamplePrefix = "JGraph.Scripting.example-apps/";
}

/// <summary>An entry of App Designer's recent files: the file and its kind of app.</summary>
public sealed record DesignRecentFile(string FilePath, DesignAppType AppType);

/// <summary>
/// App Designer's recent files (R2025b's <c>MRUArray</c>, kept by <c>RecentlyOpenedFilesManager</c>
/// in the setting <c>matlab.appdesigner.history.FileList</c>, each entry a JSON object of its
/// <c>filepath</c> and <c>apptype</c>): newest first, a file once (by name, ignoring case on
/// Windows), at most <see cref="Size"/> entries. An app goes in when it is opened, when a new one
/// is first saved and on every save.
/// </summary>
public sealed class DesignRecentFiles
{
    private readonly List<DesignRecentFile> _list;

    /// <summary>The list from the setting's entries, at most <paramref name="size"/> of them.</summary>
    public DesignRecentFiles(IEnumerable<string> entries, int size)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Size = size;
        _list = Translate(entries);
        if (_list.Count > size)
        {
            _list.RemoveRange(size, _list.Count - size);
        }
    }

    /// <summary>How many files the list keeps (<c>MaxFileListSize</c>, 0 to 12).</summary>
    public int Size { get; private set; }

    /// <summary>The files, newest first.</summary>
    public IReadOnlyList<DesignRecentFile> Files => _list;

    /// <summary>
    /// Puts <paramref name="file"/> first: moved there when it is in the list, else added with the
    /// oldest dropped when the list is full; a size of 0 keeps nothing.
    /// </summary>
    public void Push(DesignRecentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        int at = Find(file.FilePath);
        if (at != -1)
        {
            _list.RemoveAt(at);
            _list.Insert(0, file);
        }
        else if (Size == 0)
        {
            _list.Clear();
        }
        else if (_list.Count == Size)
        {
            _list.RemoveAt(Size - 1);
            _list.Insert(0, file);
        }
        else
        {
            _list.Insert(0, file);
        }
    }

    /// <summary>Takes <paramref name="filePath"/> out of the list; false when it was not in it.</summary>
    public bool Remove(string filePath)
    {
        int at = Find(filePath);
        if (at == -1)
        {
            return false;
        }

        _list.RemoveAt(at);
        return true;
    }

    /// <summary>Keeps at most <paramref name="size"/> files from now on, dropping the oldest beyond it.</summary>
    public void SetSize(int size)
    {
        Size = size;
        if (_list.Count > size)
        {
            _list.RemoveRange(size, _list.Count - size);
        }
    }

    /// <summary>The setting's entries, as R2025b writes them (<c>AppModel.getMRUEntry</c> through <c>JSON.stringify</c>): <c>{"apptype":"Standard","filepath":"..."}</c>.</summary>
    public IReadOnlyList<string> Entries() =>
        _list.Select(static f => JsonSerializer.Serialize(new Dictionary<string, string> { ["apptype"] = f.AppType.ToString(), ["filepath"] = f.FilePath }, Json)).ToArray();

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // MRUArray._translateItem: an entry that is not JSON is a path of a standard app; empty paths are dropped.
    private static List<DesignRecentFile> Translate(IEnumerable<string> entries)
    {
        var list = new List<DesignRecentFile>();
        foreach (string entry in entries)
        {
            DesignRecentFile file = new(entry, DesignAppType.Standard);
            try
            {
                if (entry.Length > 0 && JsonDocument.Parse(entry).RootElement is { ValueKind: JsonValueKind.Object } o)
                {
                    string path = o.TryGetProperty("filepath", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : string.Empty;
                    DesignAppType type = o.TryGetProperty("apptype", out JsonElement t) && Enum.TryParse(t.GetString(), out DesignAppType parsed)
                        ? parsed : DesignAppType.Standard;
                    file = new DesignRecentFile(path, type);
                }
            }
            catch (JsonException)
            {
                // a bare path, as an older release kept it
            }

            if (file.FilePath.Length > 0)
            {
                list.Add(file);
            }
        }

        return list;
    }

    private int Find(string filePath)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return _list.FindIndex(f => string.Equals(f.FilePath, filePath, comparison));
    }
}

/// <summary>
/// The names App Designer gives new documents (<c>_AppFactory._createDefaultAppName</c>): "app" and
/// a count every app factory shares, "comp" and a count of their own for custom components; the
/// counts start at 1 with each App Designer session and never go back.
/// </summary>
public sealed class DesignNewNames
{
    private int _apps;
    private int _components;

    /// <summary>The next name for a new document of <paramref name="kind"/>.</summary>
    public string Next(DesignNewKind kind) => kind is DesignNewKind.BlankComponent or DesignNewKind.BlankComponentWithGrid
        ? "comp" + ++_components
        : "app" + ++_apps;
}
