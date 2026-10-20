using System.Globalization;
using System.IO.Compression;
using System.Text;
using JGraph.Scripting.Designer;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs;

/// <summary>What writing a new App Designer file did (designer plan DS6, ADR 0245).</summary>
/// <param name="HasDesign">
/// Whether the file holds App Designer's model - its component tree and its copy of the code - so
/// that App Designer opens it with its canvas. False when the text is not an app App Designer lays
/// out (its <c>createComponents</c> cannot be read): the file then holds the code alone.
/// </param>
/// <param name="Problems">What the tree could not hold, one line each; empty when it holds the whole design.</param>
public sealed record MlappWriteResult(bool HasDesign, IReadOnlyList<string> Problems);

/// <summary>
/// An app's details as its <c>.mlapp</c> package keeps them (designer plan DS7a): App Designer's
/// App Details in <c>metadata/coreProperties.xml</c> (the name as <c>dc:title</c>, the author as
/// <c>dc:creator</c>, the summary as <c>dc:description</c>, the version as <c>cp:version</c>), the
/// description and the app's id in <c>metadata/appMetadata.xml</c> (<c>description</c>, <c>uuid</c>).
/// Each empty when the package has none.
/// </summary>
public sealed record MlappMetadata(string Name, string Author, string Summary, string Description, string Version, string Uuid)
{
    /// <summary>No details at all.</summary>
    public static MlappMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <content>Writing a whole App Designer file, and its component tree on a save (designer plan DS6, ADR 0245).</content>
public static partial class JgsMlapp
{
    /// <summary>The class App Designer keeps beside the tree; a new app's holds nothing but the oldest release it opens in.</summary>
    private const string AppDataClass = "appdesigner.internal.serialization.app.AppData";

    private const string ModelPart = "appdesigner/appModel.mat";

    /// <summary>The release whose App Designer format this writes.</summary>
    private const string Release = "R2025b";

    /// <summary>
    /// Writes a new <c>.mlapp</c> holding <paramref name="text"/> as App Designer writes one: the
    /// code, and App Designer's model of it - the component tree built from its
    /// <c>createComponents</c> (<see cref="DesignTreeWriter"/>), App Designer's copy of the code
    /// and the app data beside them, in one MAT-file - with the package's metadata. App Designer
    /// opens such a file with its canvas, and MATLAB and this build run it. A text that is not an
    /// app App Designer lays out is written as its code alone (<see cref="WritePackage"/>). The
    /// file is replaced only once the new one is complete.
    /// </summary>
    /// <exception cref="IOException">The file could not be written or replaced.</exception>
    public static MlappWriteResult WriteApp(string path, string text) => WriteApp(path, text, null);

    /// <summary>
    /// <see cref="WriteApp(string, string)"/> with the app's details written into the package's
    /// metadata (<paramref name="metadata"/>: an app converted from its plain-text file keeps
    /// them, designer plan DS7a); a new id and empty details when null.
    /// </summary>
    /// <exception cref="IOException">The file could not be written or replaced.</exception>
    public static MlappWriteResult WriteApp(string path, string text, MlappMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        metadata ??= MlappMetadata.Empty;
        string uuid = metadata.Uuid.Length > 0 ? metadata.Uuid : Guid.NewGuid().ToString();
        string version = metadata.Version.Length > 0 ? metadata.Version : "1.0";
        static string X(string value) => System.Security.SecurityElement.Escape(value);
        string code = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        DesignReadResult read = DesignCodeReader.Read(code);
        byte[]? copy = JgsMlappModel.NewCode(JgsMlappLayout.Of(code));
        if (read.Design is not { } design || copy is null)
        {
            WritePackage(path, text);
            return new MlappWriteResult(false, read.Problems);
        }

        var problems = new List<string>(read.Problems);
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        McosObject figure = DesignTreeWriter.Tree(design, read.Style, folder, problems);
        var appData = new McosObject(AppDataClass);
        foreach (string empty in (string[])["UIFigure", "CodeData", "Metadata"])
        {
            appData.Set(empty, new McosValue.Value(new MatData.Numbers("double", [0, 0], [])));
        }

        string minimum = design.IsResponsive ? "R2019a" : "R2018a";
        appData.Set("MinimumSupportedVersion", new McosValue.Value(MatData.Text(minimum)));
        byte[] model = MatMcosWriter.WriteFile(
        [
            (DesignTreeWriter.Variable, MatData.Struct((DesignTreeWriter.FigureField, new MatData.Object(figure)))),
            ("code", new MatData.Kept(copy, [])),
            ("appData", new MatData.Object(appData)),
        ], null, compress: true);

        DateTimeOffset now = DateTimeOffset.Now;
        string stamp = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string document = DocumentHead + "<![CDATA[" + code.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal) + "]]>" + DocumentTail;
        var utf8 = new UTF8Encoding(false);
        Part Xml(string name, string content) => new() { Name = name, Bytes = utf8.GetBytes(content), Written = now, Stored = false, Attributes = 0 };
        List<Part> parts =
        [
            Xml("[Content_Types].xml", AppContentTypes),
            Xml("_rels/.rels", AppRelationships),
            Xml(DocumentPart, document),
            new() { Name = ModelPart, Bytes = model, Written = now, Stored = false, Attributes = 0 },
            Xml("metadata/appMetadata.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>"
                + "<metadata xmlns=\"http://schemas.mathworks.com/appDesigner/app/2017/appMetadata\"><description>" + X(metadata.Description) + "</description>"
                + "<MLAPPVersion>2</MLAPPVersion><minimumSupportedMATLABRelease>" + minimum + "</minimumSupportedMATLABRelease>"
                + "<screenshotMode>auto</screenshotMode><uuid>" + uuid + "</uuid>"
                + "<AppType>" + (design.IsResponsive ? "Responsive" : "Standard") + "</AppType>"
                + "<componentProducts/><imageRelativePaths/>" + UserComponentsElement(DesignUserComponents.ClassesOf(design)) + "</metadata>"),
            Xml("metadata/coreProperties.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
                + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" "
                + "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" "
                + "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n"
                + "  <dcterms:created xsi:type=\"dcterms:W3CDTF\">" + stamp + "</dcterms:created>\n"
                + "  <dc:creator>" + X(metadata.Author) + "</dc:creator>\n  <dc:description>" + X(metadata.Summary) + "</dc:description>\n"
                + "  <dcterms:modified xsi:type=\"dcterms:W3CDTF\">" + stamp + "</dcterms:modified>\n"
                + "  <dc:title>" + X(metadata.Name) + "</dc:title>\n  <cp:version>" + X(version) + "</cp:version>\n</cp:coreProperties>\n"),
            Xml("metadata/mwcoreProperties.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
                + "<mwcoreProperties xmlns=\"http://schemas.mathworks.com/package/2012/coreProperties\">\n"
                + "  <contentType>application/vnd.mathworks.matlab.app</contentType>\n"
                + "  <contentTypeFriendlyName>MATLAB App</contentTypeFriendlyName>\n"
                + "  <matlabRelease>" + Release + "</matlabRelease>\n</mwcoreProperties>\n"),
            Xml("metadata/mwcorePropertiesExtension.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
                + "<mwcoreProperties xmlns=\"http://schemas.mathworks.com/package/2014/corePropertiesExtension\">\n"
                + "  <uuid>" + Guid.NewGuid().ToString() + "</uuid>\n</mwcoreProperties>\n"),
        ];
        Replace(path, parts, null);
        return new MlappWriteResult(true, problems);
    }

    /// <summary>
    /// The <c>userComponents</c> element of <c>appMetadata.xml</c> for the custom component
    /// <paramref name="classes"/> an app uses (designer plan DS7b; probe <c>ds7_uac</c>): one
    /// <c>userComponent</c> per class, or the empty element.
    /// </summary>
    internal static string UserComponentsElement(IReadOnlyList<string> classes) => classes.Count == 0
        ? "<userComponents/>"
        : "<userComponents>" + string.Concat(classes.Select(static c => "<userComponent class=\"" + System.Security.SecurityElement.Escape(c) + "\"/>")) + "</userComponents>";

    /// <summary>
    /// The details the package <paramref name="path"/> keeps (<see cref="MlappMetadata"/>); each
    /// empty when the package has no such part, or the part no such element.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a package.</exception>
    public static MlappMetadata ReadMetadata(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using ZipArchive package = ZipFile.OpenRead(path);
            System.Xml.Linq.XElement? core = Root(package, "metadata/coreProperties.xml");
            System.Xml.Linq.XElement? app = Root(package, "metadata/appMetadata.xml");
            return new MlappMetadata(
                Local(core, "title"), Local(core, "creator"), Local(core, "description"),
                Local(app, "description"), Local(core, "version"), Local(app, "uuid"));
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or NotSupportedException or InvalidDataException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        static System.Xml.Linq.XElement? Root(ZipArchive package, string name)
        {
            if (package.GetEntry(name) is not { } entry)
            {
                return null;
            }

            using Stream stream = entry.Open();
            return System.Xml.Linq.XDocument.Load(stream).Root;
        }

        static string Local(System.Xml.Linq.XElement? root, string name) =>
            root?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Brings the component tree of the model <paramref name="model"/> to the <c>createComponents</c>
    /// of <paramref name="text"/>, keeping every saved component the text did not change as App
    /// Designer saved it (<see cref="DesignTreeWriter.Update"/>). <paramref name="appFolder"/> is
    /// where the app is saved, where its images are.
    /// </summary>
    internal static DesignTreeUpdate UpdateTree(byte[] model, string text, string? appFolder)
    {
        DesignReadResult read = DesignCodeReader.Read(text);
        if (read.Design is not { } design)
        {
            return new DesignTreeUpdate(null, [], [], [], [], [], ["Its createComponents could not be read.", .. read.Problems]);
        }

        if (read.Problems.Count > 0)
        {
            // A line the reader did not take would read as a property set back: the tree is left.
            return new DesignTreeUpdate(null, [], [], [], [], [], ["Lines of its createComponents could not be read:", .. read.Problems]);
        }

        return DesignTreeWriter.Update(model, design, read.Style, appFolder);
    }

    private const string AppContentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n"
        + "  <Default ContentType=\"application/vnd.mathworks.matlab.appDesigner.appModel+mat\" Extension=\"mat\"/>\n"
        + "  <Default ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" Extension=\"rels\"/>\n"
        + "  <Default ContentType=\"application/vnd.mathworks.package.coreProperties+xml\" Extension=\"xml\"/>\n"
        + "  <Override ContentType=\"application/vnd.mathworks.matlab.code.document+xml;plaincode=true\" PartName=\"/" + DocumentPart + "\"/>\n"
        + "  <Override ContentType=\"application/vnd.mathworks.matlab.appDesigner.appMetadata+xml\" PartName=\"/metadata/appMetadata.xml\"/>\n"
        + "  <Override ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\" PartName=\"/metadata/coreProperties.xml\"/>\n"
        + "  <Override ContentType=\"application/vnd.mathworks.package.corePropertiesExtension+xml\" PartName=\"/metadata/mwcorePropertiesExtension.xml\"/>\n"
        + "</Types>\n";

    private const string AppRelationships = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
        + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n"
        + "  <Relationship Id=\"rId1\" Target=\"metadata/mwcoreProperties.xml\" Type=\"http://schemas.mathworks.com/package/2012/relationships/coreProperties\"/>\n"
        + "  <Relationship Id=\"rId2\" Target=\"metadata/mwcorePropertiesExtension.xml\" Type=\"http://schemas.mathworks.com/package/2014/relationships/corePropertiesExtension\"/>\n"
        + "  <Relationship Id=\"rId3\" Target=\"metadata/coreProperties.xml\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\"/>\n"
        + "  <Relationship Id=\"rId4\" Target=\"" + DocumentPart + "\" Type=\"http://schemas.mathworks.com/matlab/code/2013/relationships/document\"/>\n"
        + "  <Relationship Id=\"rId5\" Target=\"" + ModelPart + "\" Type=\"http://schemas.mathworks.com/appDesigner/app/2014/relationships/appModel\"/>\n"
        + "  <Relationship Id=\"rId6\" Target=\"metadata/appMetadata.xml\" Type=\"http://schemas.mathworks.com/appDesigner/app/2017/relationships/appMetadata\"/>\n"
        + "</Relationships>\n";
}
