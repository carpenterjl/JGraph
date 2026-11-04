using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JGraph.Deploy.Apps;

/// <summary>
/// What a toolbox's Apps gallery entry and the MATLAB App share window take from an app file (App
/// Designer window plan W13): an <c>.mlapp</c>'s <c>coreProperties.xml</c> (the name, author, summary and
/// version) and <c>appMetadata.xml</c> (the description and the app's uuid), as
/// <c>appdesigner.internal.serialization.MLAPPDeserializer.getAppMetadata</c> reads them for
/// <c>writeDesktopToolset</c>; a plain-text app's <c>%[app:appDetails]</c> and <c>%[app:internalData]</c>
/// appendix sections; and for any other code file its name alone.
/// </summary>
/// <param name="Name">App Details' Name, or the file's name when it holds none.</param>
/// <param name="Version">The version ("1.0" when none).</param>
/// <param name="Author">The author.</param>
/// <param name="Summary">The summary.</param>
/// <param name="Description">The description.</param>
/// <param name="Uuid">The app's identifier; one is made for a file that holds none.</param>
public sealed record AppPackageDetails(string Name, string Version, string Author, string Summary, string Description, string Uuid)
{
    private static readonly Regex DetailsBlock = new(@"%\[app:appDetails\]\s*\r?\n%\{\s*\r?\n(?<xml>.*?)\r?\n%\}", RegexOptions.Singleline);
    private static readonly Regex InternalBlock = new(@"%\[app:internalData\]\s*\r?\n%\{\s*\r?\n(?<xml>.*?)\r?\n%\}", RegexOptions.Singleline);

    /// <summary>Reads <paramref name="path"/>'s details; a file that cannot be read gives its name and a fresh uuid.</summary>
    public static AppPackageDetails Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string name = Path.GetFileNameWithoutExtension(path);
        try
        {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".mlapp", StringComparison.OrdinalIgnoreCase))
            {
                return ReadPackage(path, name);
            }

            if (extension.Equals(".m", StringComparison.OrdinalIgnoreCase))
            {
                return ReadPlainText(File.ReadAllText(path), name);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
        }

        return new AppPackageDetails(name, "1.0", string.Empty, string.Empty, string.Empty, Guid.NewGuid().ToString());
    }

    private static AppPackageDetails ReadPackage(string path, string fileName)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        XElement? core = Root(package, "metadata/coreProperties.xml");
        XElement? app = Root(package, "metadata/appMetadata.xml");
        string name = Local(core, "title");
        string version = Local(core, "version");
        string uuid = Local(app, "uuid");
        return new AppPackageDetails(
            name.Length > 0 ? name : fileName,
            version.Length > 0 ? version : "1.0",
            Local(core, "creator"),
            Local(core, "description"),
            Local(app, "description"),
            uuid.Length > 0 ? uuid : Guid.NewGuid().ToString());
    }

    /// <summary>A plain-text app's details from its appendix (<c>DesignPlainText</c> writes them as App Designer's <c>AppDetails</c> element).</summary>
    public static AppPackageDetails ReadPlainText(string text, string fileName)
    {
        ArgumentNullException.ThrowIfNull(text);
        string name = fileName, version = "1.0", author = "", summary = "", description = "", uuid = "";
        if (DetailsBlock.Match(text) is { Success: true } details)
        {
            XElement root = XElement.Parse(details.Groups["xml"].Value.Trim());
            name = Element(root, "Name") is { Length: > 0 } n ? n : fileName;
            version = Element(root, "Version") is { Length: > 0 } v ? v : "1.0";
            author = Element(root, "Author");
            summary = Element(root, "Summary");
            description = Element(root, "Description");
        }

        if (InternalBlock.Match(text) is { Success: true } internals)
        {
            uuid = Element(XElement.Parse(internals.Groups["xml"].Value.Trim()), "AppId");
        }

        return new AppPackageDetails(name, version, author, summary, description, uuid.Length > 0 ? uuid : Guid.NewGuid().ToString());
    }

    private static string Element(XElement root, string name) => root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

    private static XElement? Root(ZipArchive package, string part)
    {
        if (package.GetEntry(part) is not { } entry)
        {
            return null;
        }

        using Stream stream = entry.Open();
        return XDocument.Load(stream).Root;
    }

    private static string Local(XElement? root, string name) =>
        root?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;
}
