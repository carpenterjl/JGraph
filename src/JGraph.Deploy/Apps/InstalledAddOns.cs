using System.Text;
using System.Xml.Linq;

namespace JGraph.Deploy.Apps;

/// <summary>An installed toolbox (R2025b's <c>installedToolboxes</c> struct: Name, Version, Guid) and where it is.</summary>
public sealed record InstalledToolbox(string Name, string Version, string Guid, string Folder, IReadOnlyList<string> PathFolders, string Summary, string Description, string? Screenshot);

/// <summary>An installed app (R2025b's <c>getInstalledAppInfo</c> struct: id, name, status, location) and what starts it.</summary>
public sealed record InstalledApp(string Id, string Name, string Status, string Location, string Guid, string EntryPoint, string Version, string Summary, string Description);

/// <summary>One entry of the Apps gallery: what the shell's Apps menu shows and runs.</summary>
/// <param name="Label">The app's name.</param>
/// <param name="Callback">The statement that starts it (<c>AcceptForm</c>, or <c>matlab.apputil.run('AcceptFormAPP')</c>).</param>
/// <param name="Summary">The app's summary.</param>
/// <param name="Description">The app's description.</param>
/// <param name="Folder">Where the app or its toolbox is installed.</param>
/// <param name="PathFolders">The folders that must be on the path first.</param>
/// <param name="Icon">A picture for the entry, or null.</param>
/// <param name="IsApp">Whether the entry is an installed app (else a toolbox's gallery entry).</param>
/// <param name="Id">The app's id, or the toolbox's guid.</param>
public sealed record GalleryEntry(string Label, string Callback, string Summary, string Description, string Folder, IReadOnlyList<string> PathFolders, string? Icon, bool IsApp, string Id);

/// <summary>
/// JGraph's add-ons folder (App Designer window plan W13), laid out as R2025b lays out its
/// <c>MATLAB Add-Ons</c> folder (probe <c>w13_installed</c>): <c>Toolboxes\&lt;Name&gt;</c> holds a toolbox's files with
/// <c>resources\addons_core.xml</c>, <c>matlab_path_entries.xml</c>, <c>mltbx_app_gallery_registration.xml</c> and the
/// screenshot; <c>Apps\&lt;Name&gt;</c> holds an app's files with <c>resources\addons_core.xml</c> and
/// <c>mlappinstall_app_gallery_registration.xml</c>. The folder is <c>%APPDATA%\JGraph\Add-Ons</c>, or what the
/// <c>JGRAPH_ADDONS</c> variable names (the tests and the window checks point it elsewhere).
/// </summary>
public sealed class InstalledAddOns
{
    /// <summary>The environment variable that names another add-ons folder.</summary>
    public const string RootVariable = "JGRAPH_ADDONS";

    public InstalledAddOns(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>The add-ons folder in use: <see cref="RootVariable"/>'s, else the user's.</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } named
            ? named
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph", "Add-Ons");

    /// <summary>The add-ons folder in use.</summary>
    public static InstalledAddOns Default => new(DefaultRoot);

    public string Root { get; }

    public string ToolboxesFolder => Path.Combine(Root, "Toolboxes");

    public string AppsFolder => Path.Combine(Root, "Apps");

    // --- Toolboxes ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Installs the <c>.mltbx</c> at <paramref name="file"/>: an earlier install with the same identifier goes first, the
    /// files land under <c>Toolboxes\&lt;Name&gt;</c>, the resources beside them.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a toolbox package.</exception>
    public InstalledToolbox InstallToolbox(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ToolboxPackageInfo info = ToolboxPackage.Read(file);
        if (info.Guid.Length > 0)
        {
            UninstallToolbox(info.Guid);
        }

        string name = info.Name.Length > 0 ? info.Name : Path.GetFileNameWithoutExtension(file);
        string folder = Path.Combine(ToolboxesFolder, FolderName(name));
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        ToolboxPackage.Extract(file, folder);
        string resources = Path.Combine(folder, "resources");
        Directory.CreateDirectory(resources);
        string? screenshot = null;
        if (ToolboxPackage.ReadScreenshot(file) is { } picture)
        {
            screenshot = Path.Combine(resources, "screenshot" + picture.Extension);
            File.WriteAllBytes(screenshot, picture.Bytes);
        }

        File.WriteAllText(Path.Combine(resources, "addons_core.xml"), ToolboxCore(info, screenshot is null ? null : "resources/" + Path.GetFileName(screenshot)), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(resources, "matlab_path_entries.xml"), PathEntries(info.MatlabPaths), new UTF8Encoding(false));
        string toolset = Path.Combine(folder, "DesktopToolset.xml");
        if (File.Exists(toolset))
        {
            File.Copy(toolset, Path.Combine(resources, "mltbx_app_gallery_registration.xml"), overwrite: true);
        }

        return ToolboxAt(folder) ?? throw new InvalidDataException($"'{file}' could not be installed.");
    }

    /// <summary>Every installed toolbox, in folder order.</summary>
    public IReadOnlyList<InstalledToolbox> Toolboxes()
    {
        if (!Directory.Exists(ToolboxesFolder))
        {
            return [];
        }

        return Directory.GetDirectories(ToolboxesFolder).OrderBy(static f => f, StringComparer.OrdinalIgnoreCase).Select(ToolboxAt).Where(static t => t is not null).ToList()!;
    }

    /// <summary>The toolbox with <paramref name="guid"/>, or null.</summary>
    public InstalledToolbox? FindToolbox(string guid) => Toolboxes().FirstOrDefault(t => string.Equals(t.Guid, guid, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes the toolbox with <paramref name="guid"/>; false when none is installed.</summary>
    public bool UninstallToolbox(string guid)
    {
        if (FindToolbox(guid) is not { } toolbox)
        {
            return false;
        }

        Directory.Delete(toolbox.Folder, recursive: true);
        return true;
    }

    private InstalledToolbox? ToolboxAt(string folder)
    {
        string core = Path.Combine(folder, "resources", "addons_core.xml");
        if (!File.Exists(core))
        {
            return null;
        }

        XElement root;
        try
        {
            root = XElement.Load(core);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (!string.Equals(root.Element("type")?.Value, "Toolbox", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var paths = new List<string>();
        string entries = Path.Combine(folder, "resources", "matlab_path_entries.xml");
        if (File.Exists(entries))
        {
            foreach (XElement path in XElement.Load(entries).Elements("path"))
            {
                string relative = path.Value.Trim();
                paths.Add(relative is "." or "" ? folder : Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar))));
            }
        }

        if (paths.Count == 0)
        {
            paths.Add(folder);
        }

        string? image = root.Element("image")?.Value;
        string? screenshot = image is { Length: > 0 } && File.Exists(Path.Combine(folder, image.Replace('/', Path.DirectorySeparatorChar))) ? Path.Combine(folder, image.Replace('/', Path.DirectorySeparatorChar)) : null;
        return new InstalledToolbox(
            root.Element("label")?.Value ?? Path.GetFileName(folder),
            root.Element("version")?.Value ?? string.Empty,
            root.Element("identifier")?.Value ?? string.Empty,
            folder,
            paths,
            root.Element("summary")?.Value ?? string.Empty,
            root.Element("description")?.Value ?? string.Empty,
            screenshot);
    }

    // --- Apps ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Installs the <c>.mlappinstall</c> at <paramref name="file"/> under <c>Apps\&lt;dir&gt;</c>, the folder named from the
    /// app's name as R2025b's <c>convertNameToDir</c> names it; an install over an earlier one is "updated".
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not an app installation package.</exception>
    public InstalledApp InstallApp(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        MlappInstallInfo info = MlappInstallPackage.Read(file);
        string name = info.Name.Length > 0 ? info.Name : Path.GetFileNameWithoutExtension(file);
        string dir = AppFolderName(name);
        string folder = Path.Combine(AppsFolder, dir);
        bool updated = Directory.Exists(folder);
        if (updated)
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        MlappInstallPackage.Extract(file, folder);
        string resources = Path.Combine(folder, "resources");
        Directory.CreateDirectory(resources);
        string id = dir + "APP";
        File.WriteAllText(Path.Combine(resources, "addons_core.xml"), AppCore(info, name), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(resources, "mlappinstall_app_gallery_registration.xml"), AppRegistration(info, name, id), new UTF8Encoding(false));
        InstalledApp installed = AppAt(folder) ?? throw new InvalidDataException($"'{file}' could not be installed.");
        return installed with { Status = updated ? "updated" : "installed" };
    }

    /// <summary>Every installed app, in folder order.</summary>
    public IReadOnlyList<InstalledApp> Apps()
    {
        if (!Directory.Exists(AppsFolder))
        {
            return [];
        }

        return Directory.GetDirectories(AppsFolder).OrderBy(static f => f, StringComparer.OrdinalIgnoreCase).Select(AppAt).Where(static a => a is not null).ToList()!;
    }

    /// <summary>The apps whose id <paramref name="id"/> matches: exactly when <paramref name="strict"/>, else by its start without case (<c>AppUtil.findAppIDs</c>).</summary>
    public IReadOnlyList<InstalledApp> FindApps(string id, bool strict)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Apps().Where(a => strict ? string.Equals(a.Id, id, StringComparison.Ordinal) : a.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Removes the app whose id is exactly <paramref name="id"/>; false when none is installed.</summary>
    public bool UninstallApp(string id)
    {
        if (FindApps(id, strict: true).FirstOrDefault() is not { } app)
        {
            return false;
        }

        Directory.Delete(app.Location, recursive: true);
        return true;
    }

    /// <summary>
    /// The folders an app's run puts on the path (<c>AppUtil.genpath</c>): the app's folder and those under it, but
    /// <c>resources</c>, <c>private</c>, <c>metadata</c> and class and package folders.
    /// </summary>
    public static IReadOnlyList<string> AppPathFolders(string location)
    {
        ArgumentException.ThrowIfNullOrEmpty(location);
        var folders = new List<string>();
        Walk(location, isRoot: true);
        return folders;

        void Walk(string folder, bool isRoot)
        {
            string name = Path.GetFileName(folder);
            if (name is "private" or "resources" or "metadata" && !isRoot)
            {
                return;
            }

            folders.Add(folder);
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (string sub in Directory.GetDirectories(folder).OrderBy(static f => f, StringComparer.OrdinalIgnoreCase))
            {
                string subName = Path.GetFileName(sub);
                if (subName.StartsWith('@') || subName.StartsWith('+') || subName.StartsWith('.'))
                {
                    continue;
                }

                Walk(sub, isRoot: false);
            }
        }
    }

    /// <summary>
    /// R2025b's <c>convertNameToDir</c>: the name's letters, digits, underscores and blanks made a MATLAB identifier
    /// (<c>genvarname</c>: blanks dropped, a leading digit given an x), at most 63 characters, and "private" or
    /// "resources" given "app".
    /// </summary>
    public static string AppFolderName(string appName)
    {
        ArgumentNullException.ThrowIfNull(appName);
        var kept = new StringBuilder();
        foreach (char c in appName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                kept.Append(c);
            }
        }

        string dir = kept.ToString();
        if (dir.Length == 0)
        {
            dir = "x";
        }
        else if (char.IsDigit(dir[0]))
        {
            dir = "x" + dir;
        }

        if (dir.Length > 63)
        {
            dir = dir[..63];
        }

        if (dir.Equals("private", StringComparison.OrdinalIgnoreCase) || dir.Equals("resources", StringComparison.OrdinalIgnoreCase))
        {
            dir += "app";
        }

        return dir;
    }

    private InstalledApp? AppAt(string folder)
    {
        string core = Path.Combine(folder, "resources", "addons_core.xml");
        if (!File.Exists(core))
        {
            return null;
        }

        XElement root;
        try
        {
            root = XElement.Load(core);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (!string.Equals(root.Element("type")?.Value, "app", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new InstalledApp(
            Path.GetFileName(folder) + "APP",
            root.Element("label")?.Value ?? Path.GetFileName(folder),
            "installed",
            folder,
            root.Element("identifier")?.Value ?? string.Empty,
            root.Element("entryPoint")?.Value ?? string.Empty,
            root.Element("version")?.Value ?? string.Empty,
            root.Element("summary")?.Value ?? string.Empty,
            root.Element("description")?.Value ?? string.Empty);
    }

    // --- The gallery ---------------------------------------------------------------------------------------------

    /// <summary>Every entry the Apps gallery shows: the installed apps, then each toolbox's gallery entries.</summary>
    public IReadOnlyList<GalleryEntry> Gallery()
    {
        var entries = new List<GalleryEntry>();
        foreach (InstalledApp app in Apps())
        {
            string? icon = new[] { "icon_24.png", "icon_48.png", "icon_16.png" }.Select(n => Path.Combine(app.Location, "resources", n)).FirstOrDefault(File.Exists);
            entries.Add(new GalleryEntry(app.Name, $"matlab.apputil.run('{app.Id}')", app.Summary, app.Description, app.Location, AppPathFolders(app.Location), icon, true, app.Id));
        }

        foreach (InstalledToolbox toolbox in Toolboxes())
        {
            string registration = Path.Combine(toolbox.Folder, "resources", "mltbx_app_gallery_registration.xml");
            if (!File.Exists(registration))
            {
                continue;
            }

            XElement root;
            try
            {
                root = XElement.Load(registration);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (XElement tool in root.Elements("tool"))
            {
                string callback = tool.Element("callback")?.Value ?? string.Empty;
                if (callback.Length == 0)
                {
                    continue;
                }

                entries.Add(new GalleryEntry(
                    (string?)tool.Attribute("label") ?? callback, callback,
                    tool.Element("summary")?.Value ?? string.Empty, tool.Element("description")?.Value ?? string.Empty,
                    toolbox.Folder, toolbox.PathFolders, toolbox.Screenshot, false, toolbox.Guid));
            }
        }

        return entries;
    }

    // --- The resources' texts ------------------------------------------------------------------------------------

    private static string ToolboxCore(ToolboxPackageInfo info, string? image)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<addonCore>\n");
        text.Append("  <label>").Append(ToolboxPackage.X(info.Name)).Append("</label>\n");
        text.Append("  <version>").Append(ToolboxPackage.X(info.Version)).Append("</version>\n");
        text.Append("  <type>Toolbox</type>\n");
        text.Append("  <identifier>").Append(ToolboxPackage.X(info.Guid)).Append("</identifier>\n");
        text.Append("  <summary>").Append(ToolboxPackage.X(info.Summary)).Append("</summary>\n");
        text.Append("  <description>").Append(ToolboxPackage.X(info.Description)).Append("</description>\n");
        text.Append("  <createdBy name=\"").Append(ToolboxPackage.X(info.AuthorName)).Append("\"/>\n");
        if (image is not null)
        {
            text.Append("  <image>").Append(ToolboxPackage.X(image)).Append("</image>\n");
        }

        text.Append("</addonCore>\n");
        return text.ToString();
    }

    private static string PathEntries(IReadOnlyList<string> paths)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<paths>\n");
        IEnumerable<string> entries = paths.Count == 0 ? ["."] : paths.Select(static p => p.Length == 0 || p == "/" ? "." : p.TrimStart('/'));
        foreach (string entry in entries)
        {
            text.Append("  <path>").Append(ToolboxPackage.X(entry)).Append("</path>\n");
        }

        text.Append("</paths>\n");
        return text.ToString();
    }

    private static string AppCore(MlappInstallInfo info, string name)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<addonCore>\n");
        text.Append("  <label>").Append(ToolboxPackage.X(name)).Append("</label>\n");
        text.Append("  <version>").Append(ToolboxPackage.X(info.Version)).Append("</version>\n");
        text.Append("  <type>app</type>\n");
        text.Append("  <packagedBy>").Append(ToolboxPackage.X(info.CreatedByMatlabRelease)).Append("</packagedBy>\n");
        text.Append("  <identifier>").Append(ToolboxPackage.X(info.Guid)).Append("</identifier>\n");
        text.Append("  <summary>").Append(ToolboxPackage.X(info.Summary)).Append("</summary>\n");
        text.Append("  <description>").Append(ToolboxPackage.X(info.Description)).Append("</description>\n");
        text.Append("  <createdBy name=\"").Append(ToolboxPackage.X(info.AuthorName)).Append("\" company=\"").Append(ToolboxPackage.X(info.AuthorOrganization)).Append("\" email=\"").Append(ToolboxPackage.X(info.AuthorContact)).Append("\"/>\n");
        // JGraph's own: R2025b keeps the entry point in the wrapper class it writes beside the app; JGraph writes no wrapper.
        text.Append("  <entryPoint>").Append(ToolboxPackage.X(info.EntryPoint)).Append("</entryPoint>\n");
        text.Append("</addonCore>\n");
        return text.ToString();
    }

    private static string AppRegistration(MlappInstallInfo info, string name, string id) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<toolset id=\"user_apps_toolset\" template=\"false\">\n"
        + "   <tool app_file_path=\"./\" handler_class=\"com.mathworks.appmanagement.desktop.ToolboxToolSetExtensionHandler\" id=\"" + ToolboxPackage.X(info.Guid) + "\" label=\"" + ToolboxPackage.X(name) + "\" toolset_id=\"user_apps_toolset\">\n"
        + "      <callback>matlab.apputil.run('" + id + "')</callback>\n"
        + "      <summary>" + ToolboxPackage.X(info.Summary) + "</summary>\n"
        + "      <description>" + ToolboxPackage.X(info.Description) + "</description>\n"
        + "      <parent_tool id=\"my_apps\" toolset_id=\"apps_toolset\"/>\n"
        + "   </tool>\n</toolset>\n";

    /// <summary>A toolbox's folder name: its name with the characters a file name cannot hold replaced.</summary>
    private static string FolderName(string name)
    {
        var text = new StringBuilder();
        foreach (char c in name)
        {
            text.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }

        string folder = text.ToString().Trim();
        return folder.Length == 0 ? "Toolbox" : folder;
    }
}
