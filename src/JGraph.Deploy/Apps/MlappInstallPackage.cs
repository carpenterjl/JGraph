using System.IO.Compression;
using System.Xml.Linq;

namespace JGraph.Deploy.Apps;

/// <summary>What a legacy <c>.mlappinstall</c> holds (R2025b's <c>mlappinfo</c>, probe <c>w13_apps</c>).</summary>
public sealed class MlappInstallInfo
{
    /// <summary>The function or class the app starts with (<c>entryPoint</c>).</summary>
    public string EntryPoint { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string CreatedByMatlabRelease { get; init; } = string.Empty;

    public string Guid { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string AuthorContact { get; init; } = string.Empty;

    public string AuthorOrganization { get; init; } = string.Empty;

    /// <summary>The packaged files, named as the manifest names them ("/AcceptForm.mlapp").</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The app files among them (<c>appEntries</c>).</summary>
    public IReadOnlyList<string> AppEntries { get; init; } = [];
}

/// <summary>
/// The legacy <c>.mlappinstall</c> package (App Designer window plan W13): read as R2025b's <c>mlappinfo</c> reads
/// its <c>metadata/appProperties.xml</c>, and its <c>fsroot</c> extracted, so that an app packaged by R2025b's
/// App Packager installs in JGraph. JGraph writes none: R2025b's own App Designer writes a toolbox (W0).
/// </summary>
public static class MlappInstallPackage
{
    /// <summary>Reads <paramref name="path"/>'s properties.</summary>
    /// <exception cref="InvalidDataException">The file is not an app installation package.</exception>
    public static MlappInstallInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using ZipArchive zip = Open(path);
        XElement? properties = ToolboxPackage.Root(zip, "metadata/appProperties.xml")
            ?? throw new InvalidDataException($"'{path}' is not a MATLAB app installation file: it has no appProperties part.");
        XElement? author = properties.Elements().FirstOrDefault(e => e.Name.LocalName == "author");
        XElement? manifest = ToolboxPackage.Root(zip, "metadata/filesystemManifest.xml");
        return new MlappInstallInfo
        {
            EntryPoint = ToolboxPackage.Local(properties, "entryPoint"),
            Name = ToolboxPackage.Local(properties, "name"),
            Version = ToolboxPackage.Local(properties, "version"),
            CreatedByMatlabRelease = ToolboxPackage.Local(properties, "createdByMATLABRelease"),
            Guid = ToolboxPackage.Local(properties, "GUID"),
            Summary = ToolboxPackage.Local(properties, "summary"),
            Description = ToolboxPackage.Local(properties, "description"),
            AuthorName = ToolboxPackage.Local(author, "name"),
            AuthorContact = ToolboxPackage.Local(author, "contact"),
            AuthorOrganization = ToolboxPackage.Local(author, "organization"),
            Files = manifest?.Elements().Where(e => e.Name.LocalName == "fileEntry").Select(e => (string?)e.Attribute("name") ?? string.Empty).ToList() ?? [],
            AppEntries = properties.Descendants().Where(e => e.Name.LocalName == "appFile").Select(e => e.Value).ToList(),
        };
    }

    /// <summary>Copies the package's <c>fsroot</c> into <paramref name="folder"/>.</summary>
    public static void Extract(string path, string folder) => ToolboxPackage.Extract(path, folder);

    private static ZipArchive Open(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"'{path}' is not a MATLAB app installation file.", ex);
        }
    }
}
