using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace JGraph.Deploy.Apps;

/// <summary>
/// The options a toolbox is packaged with (R2025b's <c>matlab.addons.toolbox.ToolboxOptions</c>, App Designer
/// window plan W13): the folder, the identifier, the name, version, author, summary and description, the files,
/// the MATLAB path folders, the Apps gallery files, the image, the getting started guide, the output file, the
/// releases and the platforms. The values are held as given; the session's class checks them in R2025b's words.
/// </summary>
public sealed class ToolboxPackageOptions
{
    public ToolboxPackageOptions(string toolboxFolder, string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolboxFolder);
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ToolboxFolder = Path.GetFullPath(toolboxFolder).TrimEnd(Path.DirectorySeparatorChar);
        Identifier = identifier;
        ToolboxName = Path.GetFileName(ToolboxFolder);
        OutputFile = Path.Combine(Path.GetDirectoryName(ToolboxFolder) ?? ToolboxFolder, Path.GetFileName(ToolboxFolder) + ".mltbx");
    }

    /// <summary>The folder the packaged paths are relative to.</summary>
    public string ToolboxFolder { get; }

    /// <summary>The toolbox's unique identifier (its GUID).</summary>
    public string Identifier { get; }

    public string ToolboxName { get; set; }

    public string ToolboxVersion { get; set; } = "1.0.0";

    public string AuthorName { get; set; } = string.Empty;

    public string AuthorEmail { get; set; } = string.Empty;

    public string AuthorCompany { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The files packaged, each a full path.</summary>
    public List<string> ToolboxFiles { get; } = [];

    /// <summary>The folders put on the path when installed, each a full path under <see cref="ToolboxFolder"/>.</summary>
    public List<string> ToolboxMatlabPath { get; } = [];

    /// <summary>The files the Apps gallery lists, each among <see cref="ToolboxFiles"/>.</summary>
    public List<string> AppGalleryFiles { get; } = [];

    /// <summary>The toolbox's image, or empty.</summary>
    public string ToolboxImageFile { get; set; } = string.Empty;

    /// <summary>The getting started guide, or empty.</summary>
    public string ToolboxGettingStartedGuide { get; set; } = string.Empty;

    /// <summary>Where the <c>.mltbx</c> is written.</summary>
    public string OutputFile { get; set; }

    /// <summary>The earliest release ("" for R2014b).</summary>
    public string MinimumMatlabRelease { get; set; } = string.Empty;

    /// <summary>The latest release ("" for latest).</summary>
    public string MaximumMatlabRelease { get; set; } = string.Empty;

    public bool Win64 { get; set; } = true;

    public bool Glnxa64 { get; set; } = true;

    public bool Maci64 { get; set; } = true;

    public bool MatlabOnline { get; set; } = true;

    /// <summary>The release the package says it was written by.</summary>
    public const string Release = "R2025b";

    /// <summary>Why <paramref name="identifier"/> will not do (<c>mustBeValidIdentifier</c>), or null.</summary>
    public static string? WhyNotIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return identifier.Length > 0 && identifier.Replace("-", string.Empty, StringComparison.Ordinal).All(char.IsLetterOrDigit)
            ? null
            : $"Invalid toolbox identifier '{identifier}'.  Identifier must be alphanumeric and may contain hyphens ('-').";
    }

    /// <summary>Why <paramref name="version"/> will not do (<c>set.ToolboxVersion</c>: two to four whole numbers), or null.</summary>
    public static string? WhyNotVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        string[] parts = version.Split('.');
        bool ok = parts.Length is >= 2 and <= 4 && parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 0 && !double.IsNaN(d));
        return ok ? null : $"Invalid version '{version}'. The version must be a scalar string or character vector of the form Major.Minor.Bug.Build. Bug and Build are optional.";
    }

    /// <summary>Whether <paramref name="release"/> is spelled <c>RXXXXx</c>.</summary>
    public static bool IsReleaseName(string release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Length == 6 && release[0] == 'R' && release[1..5].All(char.IsDigit) && release[5] is 'a' or 'b';
    }

    /// <summary>Whether release <paramref name="a"/> is the same as or later than <paramref name="b"/>.</summary>
    public static bool ReleaseAtLeast(string a, string b) => string.CompareOrdinal(a, b) >= 0;

    /// <summary>The MATLAB code files the Apps gallery takes (<c>SUPPORTED_APP_GALLERY_EXTENSIONS</c>).</summary>
    public static bool IsGalleryFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".m" or ".p" or ".mlx" or ".mlapp" or ".mexw64";

    /// <summary>The image formats a toolbox image may have.</summary>
    public static bool IsImageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".bmp" or ".png" or ".gif";

    /// <summary>A file's path as the package names it: under the folder, relative with slashes; elsewhere, the full path with its colon doubled under.</summary>
    public string PackagedPath(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        string full = Path.GetFullPath(file);
        string root = ToolboxFolder + Path.DirectorySeparatorChar;
        string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full.Replace(":", "__", StringComparison.Ordinal);
        return relative.Replace('\\', '/');
    }

    /// <summary>A path folder as the configuration names it: "" for the folder itself, "/sub" under it.</summary>
    public string PackagedFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(full, ToolboxFolder, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string root = ToolboxFolder + Path.DirectorySeparatorChar;
        string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full.Replace(":", "__", StringComparison.Ordinal);
        return "/" + relative.Replace('\\', '/');
    }
}

/// <summary>One of a package's files: its packaged path and size.</summary>
public sealed record ToolboxPackageFile(string PackagedPath, long Bytes);

/// <summary>One Apps gallery entry of a toolbox (<c>DesktopToolset.xml</c>'s <c>tool</c>).</summary>
public sealed record ToolboxGalleryEntry(string AppFilePath, string Id, string Label, string Callback, string Summary, string Description);

/// <summary>What a <c>.mltbx</c> holds, read from its parts (R2025b's or JGraph's).</summary>
public sealed class ToolboxPackageInfo
{
    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Guid { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string AuthorContact { get; init; } = string.Empty;

    public string AuthorOrganization { get; init; } = string.Empty;

    public string MatlabRelease { get; init; } = string.Empty;

    public IReadOnlyList<ToolboxPackageFile> Files { get; init; } = [];

    /// <summary>The path folders as the configuration names them ("" the root, "/sub").</summary>
    public IReadOnlyList<string> MatlabPaths { get; init; } = [];

    public IReadOnlyList<ToolboxGalleryEntry> GalleryEntries { get; init; } = [];

    public string ReleaseStart { get; init; } = string.Empty;

    public string ReleaseEnd { get; init; } = string.Empty;

    /// <summary>The screenshot part's name (<c>metadata/primaryScreenShot.png</c>), or null.</summary>
    public string? ScreenshotPart { get; init; }

    /// <summary>Whether the package carries MathWorks' XML signature (R2025b's do; JGraph's do not).</summary>
    public bool IsSigned { get; init; }
}

/// <summary>
/// The <c>.mltbx</c> file (App Designer window plan W13): written as R2025b's <c>packageFromOptions</c> writes one -
/// <c>fsroot/</c> with the files and <c>DesktopToolset.xml</c> for the Apps gallery, and the metadata parts
/// <c>addonProperties</c>, <c>configuration</c>, <c>coreProperties</c>, <c>filesystemManifest</c>,
/// <c>mwcoreProperties</c>, <c>mwcorePropertiesExtension</c>, <c>mwcorePropertiesReleaseInfo</c>, the screenshot and
/// <c>systemRequirements</c>, with the content types and relationships R2025b writes (probe <c>w13_apps</c>) - but
/// unsigned, since JGraph cannot sign as MathWorks does; and read back, R2025b's packages included.
/// </summary>
public static class ToolboxPackage
{
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = "text/plain;charset=utf-8",
        ["txt"] = "text/plain;charset=utf-8",
        ["mat"] = "application/vnd.mathworks.matlab.workspaceData",
        ["mlapp"] = "application/vnd.mathworks.matlab.app",
        ["mlx"] = "application/vnd.mathworks.matlab.code",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["rels"] = "application/vnd.openxmlformats-package.relationships+xml",
        ["xml"] = "application/vnd.mathworks.matlab.addon.properties+xml",
        ["json"] = "application/json",
        ["csv"] = "text/csv",
        ["html"] = "text/html",
        ["pdf"] = "application/pdf",
    };

    /// <summary>
    /// Writes <paramref name="options"/>' package to its <see cref="ToolboxPackageOptions.OutputFile"/>, with
    /// <paramref name="gallery"/> as the Apps gallery entries (one per <see cref="ToolboxPackageOptions.AppGalleryFiles"/>
    /// when null, read from the files), and answers the path written.
    /// </summary>
    /// <exception cref="InvalidOperationException">The folder does not exist, or no file does.</exception>
    public static string Write(ToolboxPackageOptions options, IReadOnlyList<ToolboxGalleryEntry>? gallery = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Directory.Exists(options.ToolboxFolder))
        {
            throw new InvalidOperationException($"Toolbox folder \"{options.ToolboxFolder}\" does not exist.");
        }

        if (options.ToolboxFiles.Count == 0)
        {
            throw new InvalidOperationException("ToolboxFiles must contain at least one file.");
        }

        var files = options.ToolboxFiles.Select(Path.GetFullPath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException("ToolboxFiles contains no files that exist.");
        }

        gallery ??= options.AppGalleryFiles.Select(GalleryEntryOf).ToList();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var parts = new List<(string Name, byte[] Bytes, DateTimeOffset Written)>();
        var manifest = new List<(string Packaged, DateTimeOffset Date)>();
        var fsroot = files.Select(f => (Packaged: options.PackagedPath(f), File: f)).ToList();
        bool toolset = gallery.Count > 0;
        if (toolset)
        {
            fsroot.Add(("DesktopToolset.xml", string.Empty));
        }

        fsroot.Sort((a, b) => string.CompareOrdinal(a.Packaged, b.Packaged));
        foreach ((string packaged, string file) in fsroot)
        {
            if (file.Length == 0)
            {
                parts.Add(("fsroot/DesktopToolset.xml", Utf8(DesktopToolset(gallery)), now));
                manifest.Add((packaged, now));
            }
            else
            {
                DateTimeOffset written = File.GetLastWriteTimeUtc(file);
                parts.Add(("fsroot/" + packaged, File.ReadAllBytes(file), written));
                manifest.Add((packaged, written));
            }
        }

        string? screenshot = options.ToolboxImageFile.Length > 0 && File.Exists(options.ToolboxImageFile) ? options.ToolboxImageFile : null;
        string screenshotPart = screenshot is null ? string.Empty : "metadata/primaryScreenShot" + Path.GetExtension(screenshot).ToLowerInvariant();
        var paths = options.ToolboxMatlabPath.Select(options.PackagedFolder).Distinct(StringComparer.Ordinal).ToList();
        string uuid = System.Guid.NewGuid().ToString();

        var metadata = new List<(string Name, byte[] Bytes)>
        {
            ("metadata/addonProperties.xml", Utf8(AddonProperties(options))),
        };
        if (paths.Count > 0)
        {
            metadata.Add(("metadata/configuration.xml", Utf8(Configuration(paths))));
        }

        metadata.Add(("metadata/coreProperties.xml", Utf8(CoreProperties(options, now))));
        metadata.Add(("metadata/filesystemManifest.xml", Utf8(FilesystemManifest(manifest))));
        metadata.Add(("metadata/mwcoreProperties.xml", Utf8(MwCoreProperties)));
        metadata.Add(("metadata/mwcorePropertiesExtension.xml", Utf8(MwCorePropertiesExtension(uuid))));
        metadata.Add(("metadata/mwcorePropertiesReleaseInfo.xml", Utf8(ReleaseInfo(now))));
        if (screenshot is not null)
        {
            metadata.Add((screenshotPart, File.ReadAllBytes(screenshot)));
        }

        metadata.Add(("metadata/systemRequirements.xml", Utf8(SystemRequirements(options))));

        var extensions = new SortedSet<string>(StringComparer.Ordinal) { "rels", "xml" };
        foreach ((string packaged, _) in fsroot)
        {
            string extension = Path.GetExtension(packaged).TrimStart('.').ToLowerInvariant();
            if (extension.Length > 0)
            {
                extensions.Add(extension);
            }
        }

        if (screenshot is not null)
        {
            extensions.Add(Path.GetExtension(screenshot).TrimStart('.').ToLowerInvariant());
        }

        string output = Path.GetFullPath(options.OutputFile);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        string temporary = output + ".packaging";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", Utf8(ContentTypesXml(extensions, toolset, paths.Count > 0)), now);
                Add(zip, "_rels/.rels", Utf8(Relationships(paths.Count > 0, screenshot is not null, screenshotPart)), now);
                foreach ((string name, byte[] bytes, DateTimeOffset written) in parts)
                {
                    Add(zip, name, bytes, written);
                }

                foreach ((string name, byte[] bytes) in metadata)
                {
                    Add(zip, name, bytes, now);
                }
            }

            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return output;
    }

    /// <summary>The Apps gallery entry R2025b's <c>writeDesktopToolset</c> makes for <paramref name="appFile"/>: its details' name as the label, the file's name as the callback.</summary>
    public static ToolboxGalleryEntry GalleryEntryOf(string appFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(appFile);
        AppPackageDetails details = AppPackageDetails.Read(appFile);
        string file = Path.GetFileName(appFile);
        return new ToolboxGalleryEntry(file, details.Uuid + ":" + file, details.Name, Path.GetFileNameWithoutExtension(appFile), details.Summary, details.Description);
    }

    /// <summary>Reads what <paramref name="path"/> holds.</summary>
    /// <exception cref="InvalidDataException">The file is not a toolbox package.</exception>
    public static ToolboxPackageInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using ZipArchive zip = OpenPackage(path);
        XElement? addon = Root(zip, "metadata/addonProperties.xml");
        XElement? core = Root(zip, "metadata/coreProperties.xml");
        XElement? mw = Root(zip, "metadata/mwcoreProperties.xml");
        if (addon is null || core is null)
        {
            throw new InvalidDataException($"'{path}' is not a MATLAB toolbox file: it has no addonProperties or coreProperties part.");
        }

        XElement? author = addon.Descendants().FirstOrDefault(e => e.Name.LocalName == "author");
        var files = new List<ToolboxPackageFile>();
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.StartsWith("fsroot/", StringComparison.Ordinal) && entry.FullName != "fsroot/DesktopToolset.xml" && entry.Length >= 0 && !entry.FullName.EndsWith('/'))
            {
                files.Add(new ToolboxPackageFile(entry.FullName["fsroot".Length..], entry.Length));
            }
        }

        XElement? configuration = Root(zip, "metadata/configuration.xml");
        var paths = configuration?.Descendants().Where(e => e.Name.LocalName == "matlabPath").Select(e => e.Value).ToList() ?? [];
        var gallery = new List<ToolboxGalleryEntry>();
        if (Root(zip, "fsroot/DesktopToolset.xml") is { } toolsetRoot)
        {
            foreach (XElement tool in toolsetRoot.Elements("tool"))
            {
                gallery.Add(new ToolboxGalleryEntry(
                    (string?)tool.Attribute("app_file_path") ?? string.Empty,
                    (string?)tool.Attribute("id") ?? string.Empty,
                    (string?)tool.Attribute("label") ?? string.Empty,
                    tool.Element("callback")?.Value ?? string.Empty,
                    tool.Element("summary")?.Value ?? string.Empty,
                    tool.Element("description")?.Value ?? string.Empty));
            }
        }

        XElement? requirements = Root(zip, "metadata/systemRequirements.xml");
        XElement? compatibility = requirements?.Descendants().FirstOrDefault(e => e.Name.LocalName == "releaseCompatibility");
        string? screenshot = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("metadata/primaryScreenShot", StringComparison.Ordinal))?.FullName;
        return new ToolboxPackageInfo
        {
            Name = Local(core, "title"),
            Version = Local(core, "version"),
            Guid = Local(addon, "identifier"),
            Summary = Local(core, "description"),
            Description = addon.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value ?? string.Empty,
            AuthorName = Local(author, "name"),
            AuthorContact = Local(author, "contact"),
            AuthorOrganization = Local(author, "organization"),
            MatlabRelease = Local(mw, "matlabRelease"),
            Files = files,
            MatlabPaths = paths,
            GalleryEntries = gallery,
            ReleaseStart = (string?)compatibility?.Attribute("start") ?? string.Empty,
            ReleaseEnd = (string?)compatibility?.Attribute("end") ?? string.Empty,
            ScreenshotPart = screenshot,
            IsSigned = zip.GetEntry("_xmlsignatures/sig1.xml") is not null,
        };
    }

    /// <summary>Copies the package's <c>fsroot</c> into <paramref name="folder"/>, <c>DesktopToolset.xml</c> included.</summary>
    public static void Extract(string path, string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        using ZipArchive zip = OpenPackage(path);
        string root = Path.GetFullPath(folder);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("fsroot/", StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(root, entry.FullName["fsroot/".Length..]));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"'{path}' names a file outside its folder: {entry.FullName}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>The screenshot's bytes and extension, or null without one.</summary>
    public static (byte[] Bytes, string Extension)? ReadScreenshot(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using ZipArchive zip = OpenPackage(path);
        if (zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("metadata/primaryScreenShot", StringComparison.Ordinal)) is not { } entry)
        {
            return null;
        }

        using Stream stream = entry.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return (bytes.ToArray(), Path.GetExtension(entry.FullName));
    }

    private static ZipArchive OpenPackage(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"'{path}' is not a MATLAB toolbox file.", ex);
        }
    }

    // --- The parts ---------------------------------------------------------------------------------------------

    private static string ContentTypesXml(IEnumerable<string> extensions, bool toolset, bool configuration)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<Types xmlns=\"").Append(ContentTypesNamespace).Append("\">\n");
        foreach (string extension in extensions)
        {
            string type = ContentTypes.TryGetValue(extension, out string? known) ? known : "application/octet-stream";
            text.Append("  <Default ContentType=\"").Append(type).Append("\" Extension=\"").Append(extension).Append("\"/>\n");
        }

        if (toolset)
        {
            text.Append("  <Override ContentType=\"application/xml\" PartName=\"/fsroot/DesktopToolset.xml\"/>\n");
        }

        if (configuration)
        {
            text.Append("  <Override ContentType=\"application/vnd.mathworks.matlab.addon.configuration+xml\" PartName=\"/metadata/configuration.xml\"/>\n");
        }

        text.Append("  <Override ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" PartName=\"/metadata/coreProperties.xml\"/>\n");
        text.Append("  <Override ContentType=\"application/vnd.mathworks.package.filesystemManifest+xml\" PartName=\"/metadata/filesystemManifest.xml\"/>\n");
        text.Append("  <Override ContentType=\"application/vnd.mathworks.package.coreProperties+xml\" PartName=\"/metadata/mwcoreProperties.xml\"/>\n");
        text.Append("  <Override ContentType=\"application/vnd.mathworks.package.corePropertiesExtension+xml\" PartName=\"/metadata/mwcorePropertiesExtension.xml\"/>\n");
        text.Append("  <Override ContentType=\"application/vnd.mathworks.package.corePropertiesReleaseInfo+xml\" PartName=\"/metadata/mwcorePropertiesReleaseInfo.xml\"/>\n");
        text.Append("  <Override ContentType=\"application/vnd.mathworks.matlab.addon.systemRequirements+xml\" PartName=\"/metadata/systemRequirements.xml\"/>\n");
        text.Append("</Types>\n");
        return text.ToString();
    }

    private static string Relationships(bool configuration, bool screenshot, string screenshotPart)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<Relationships xmlns=\"").Append(RelationshipsNamespace).Append("\">\n");
        text.Append("  <Relationship Id=\"rId1\" Target=\"metadata/addonProperties.xml\" Type=\"http://schemas.mathworks.com/matlab/addon/2014/relationships/addonProperties\"/>\n");
        if (screenshot)
        {
            text.Append("  <Relationship Id=\"rId100\" Target=\"").Append(screenshotPart).Append("\" Type=\"http://schemas.mathworks.com/matlab/addon/2014/relationships/primaryScreenShot\"/>\n");
            text.Append("  <Relationship Id=\"rId101\" Target=\"").Append(screenshotPart).Append("\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\"/>\n");
        }

        text.Append("  <Relationship Id=\"rId2\" Target=\"metadata/mwcoreProperties.xml\" Type=\"http://schemas.mathworks.com/package/2012/relationships/coreProperties\"/>\n");
        text.Append("  <Relationship Id=\"rId3\" Target=\"metadata/mwcorePropertiesExtension.xml\" Type=\"http://schemas.mathworks.com/package/2014/relationships/corePropertiesExtension\"/>\n");
        text.Append("  <Relationship Id=\"rId4\" Target=\"metadata/mwcorePropertiesReleaseInfo.xml\" Type=\"http://schemas.mathworks.com/package/2019/relationships/corePropertiesReleaseInfo\"/>\n");
        text.Append("  <Relationship Id=\"rId5\" Target=\"metadata/coreProperties.xml\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\"/>\n");
        text.Append("  <Relationship Id=\"rId6\" Target=\"metadata/filesystemManifest.xml\" Type=\"http://schemas.mathworks.com/package/2013/relationships/filesystemManifest\"/>\n");
        int next = 7;
        if (configuration)
        {
            text.Append("  <Relationship Id=\"rId7\" Target=\"metadata/configuration.xml\" Type=\"http://schemas.mathworks.com/matlab/addon/2014/relationships/configuration\"/>\n");
            next = 8;
        }

        text.Append("  <Relationship Id=\"rId").Append(next).Append("\" Target=\"metadata/systemRequirements.xml\" Type=\"http://schemas.mathworks.com/matlab/addon/2014/relationships/systemRequirements\"/>\n");
        text.Append("</Relationships>\n");
        return text.ToString();
    }

    /// <summary>R2025b's <c>writeDesktopToolset</c>: one <c>tool</c> per gallery file, with the generic icons and <c>my_apps</c> as its parent.</summary>
    public static string DesktopToolset(IReadOnlyList<ToolboxGalleryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<toolset id=\"user_apps_toolset\" template=\"false\">\n");
        foreach (ToolboxGalleryEntry entry in entries)
        {
            text.Append("    <tool app_file_path=\"").Append(X(entry.AppFilePath)).Append("\" handler_class=\"com.mathworks.deployment.desktop.toolstrip.ToolboxToolSetExtensionHandler\" id=\"")
                .Append(X(entry.Id)).Append("\" label=\"").Append(X(entry.Label)).Append("\" quick_access_eligible=\"false\" toolset_id=\"user_apps_toolset\">\n");
            text.Append("        <callback>").Append(X(entry.Callback)).Append("</callback>\n");
            text.Append("        <summary>").Append(X(entry.Summary)).Append("</summary>\n");
            text.Append("        <description>").Append(X(entry.Description)).Append("</description>\n");
            text.Append("        <icon filename=\"matlab_app_generic_icon_24\" id=\"app\"/>\n");
            text.Append("        <icon filename=\"matlab_app_generic_icon_16\"/>\n");
            text.Append("        <parent_tool id=\"my_apps\" toolset_id=\"apps_toolset\"/>\n");
            text.Append("    </tool>\n");
        }

        text.Append("</toolset>\n");
        return text.ToString();
    }

    private static string AddonProperties(ToolboxPackageOptions options) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?><addonProperties xmlns=\"http://schemas.mathworks.com/matlab/addon/2014/addonProperties\"><identifier type=\"GUID\">"
        + X(options.Identifier) + "</identifier><description>" + X(options.Description) + "</description><authors><author><name>" + X(options.AuthorName)
        + "</name><contact>" + X(options.AuthorEmail) + "</contact><organization>" + X(options.AuthorCompany) + "</organization></author></authors></addonProperties>";

    private static string Configuration(IReadOnlyList<string> paths) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?><configuration xmlns=\"http://schemas.mathworks.com/matlab/addon/2014/configuration\"><matlabPaths>"
        + string.Concat(paths.Select(p => "<matlabPath>" + X(p) + "</matlabPath>")) + "</matlabPaths></configuration>";

    private static string CoreProperties(ToolboxPackageOptions options, DateTimeOffset now)
    {
        string stamp = Stamp(now);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
            + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
            + "  <dcterms:created xsi:type=\"dcterms:W3CDTF\">" + stamp + "</dcterms:created>\n"
            + "  <dc:creator>" + X(options.AuthorName) + "</dc:creator>\n"
            + "  <dc:description>" + X(options.Summary) + "</dc:description>\n"
            + "  <dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + stamp + "</dcterms:modified>\n"
            + "  <dc:title>" + X(options.ToolboxName) + "</dc:title>\n"
            + "  <cp:version>" + X(options.ToolboxVersion) + "</cp:version>\n"
            + "</cp:coreProperties>\n";
    }

    private static string FilesystemManifest(IReadOnlyList<(string Packaged, DateTimeOffset Date)> entries)
    {
        var text = new StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<fileEntries xmlns=\"http://schemas.mathworks.com/package/2013/filesystemManifest\" createdByEncoding=\"UTF-8\" createdByPlatform=\"win64\" version=\"2.0\">\n");
        foreach ((string packaged, DateTimeOffset date) in entries)
        {
            text.Append("  <fileEntry content=\"/fsroot/").Append(X(packaged)).Append("\" date=\"").Append(Stamp(date)).Append("\" name=\"/").Append(X(packaged)).Append("\" permissions=\"010666\" type=\"File\"/>\n");
        }

        text.Append("</fileEntries>\n");
        return text.ToString();
    }

    private const string MwCoreProperties = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<mwcoreProperties xmlns=\"http://schemas.mathworks.com/package/2012/coreProperties\">\n"
        + "  <contentType>application/vnd.mathworks.matlab.toolbox</contentType>\n  <contentTypeFriendlyName>MATLAB Toolbox</contentTypeFriendlyName>\n  <matlabRelease>" + ToolboxPackageOptions.Release + "</matlabRelease>\n</mwcoreProperties>\n";

    private static string MwCorePropertiesExtension(string uuid) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<mwcoreProperties xmlns=\"http://schemas.mathworks.com/package/2014/corePropertiesExtension\">\n  <uuid>" + uuid + "</uuid>\n</mwcoreProperties>\n";

    /// <summary>
    /// R2025b's release information, verbatim: its reader validates the part ("ReleaseInfo is invalid", probe
    /// <c>w13_variants</c>) and takes the release's own build number, date and checksum, so the package says it was
    /// written by R2025b's build, as the dialect's packages must (a divergence: JGraph's version is not in it).
    /// </summary>
    private static string ReleaseInfo(DateTimeOffset now) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- Version information for MathWorks " + ToolboxPackageOptions.Release + " Release -->\n<MathWorks_version_info>\n"
        + "  <version>25.2.0.2998904</version>\n  <release>" + ToolboxPackageOptions.Release + "</release>\n  <description></description>\n"
        + "  <date>Aug 21 2025</date>\n  <checksum>3277779261</checksum>\n</MathWorks_version_info>\n";

    private static string SystemRequirements(ToolboxPackageOptions options) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?><systemRequirements xmlns=\"http://schemas.mathworks.com/matlab/addon/2014/systemRequirements\"><platformCompatibility MATLABOnline=\""
        + Flag(options.MatlabOnline) + "\" linux=\"" + Flag(options.Glnxa64) + "\" mac=\"" + Flag(options.Maci64) + "\" win=\"" + Flag(options.Win64) + "\"/><releaseCompatibility end=\""
        + X(options.MaximumMatlabRelease.Length > 0 ? options.MaximumMatlabRelease : "latest") + "\" start=\"" + X(options.MinimumMatlabRelease.Length > 0 ? options.MinimumMatlabRelease : "R2014b") + "\"/></systemRequirements>";

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Stamp(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>XML-escaped text.</summary>
    internal static string X(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static void Add(ZipArchive zip, string name, byte[] bytes, DateTimeOffset written)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = written.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : written;
        using Stream into = entry.Open();
        into.Write(bytes);
    }

    internal static XElement? Root(ZipArchive zip, string part)
    {
        if (zip.GetEntry(part) is not { } entry)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream).Root;
    }

    internal static string Local(XElement? root, string name) =>
        root?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;
}
