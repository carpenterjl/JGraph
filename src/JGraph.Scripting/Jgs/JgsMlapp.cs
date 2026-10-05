using System.IO.Compression;
using System.Text;
using System.Xml;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reads the code out of an App Designer file (U7 of the app-building plan, ADR 0204). An
/// <c>.mlapp</c> is an OPC package - a zip - whose document part holds a whole class file as text;
/// MATLAB finds the part through the package's relationships and runs the text as it runs an
/// <c>.m</c> file. Nothing else in the package is needed to run the app, and nothing else is read:
/// the design-time model beside the code (<c>appdesigner/appModel.mat</c>) is App Designer's.
/// </summary>
/// <remarks>
/// Measured in R2025b (fixture <c>u7_mlapp</c>): a package holding only its content types, its
/// relationships and the document runs, wherever the relationships say the document is; a zip
/// holding the document and no relationships is found (<c>exist</c> answers 2) and refused when
/// it is called, as <c>MATLAB:fileio:cantOpenFile</c>. The text need not be an app: a plain class
/// or a function in an <c>.mlapp</c> runs as one.
/// </remarks>
public static class JgsMlapp
{
    /// <summary>The extension of an App Designer file.</summary>
    public const string Extension = ".mlapp";

    private const string Relationships = "_rels/.rels";
    private const string DocumentRelationship = "/relationships/document";
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Whether a path names an App Designer file, by its extension.</summary>
    public static bool IsMlapp(string path) =>
        Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The text of a code file: an <c>.mlapp</c>'s document, any other file's own text. This is
    /// what everything that reads a file to run it, list it or show it asks.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is an <c>.mlapp</c> and holds no document that can be read.</exception>
    public static string ReadSource(string path) => IsMlapp(path) ? ReadCode(path) : File.ReadAllText(path);

    /// <summary>The class file an <c>.mlapp</c> holds, its lines ended by a line feed as XML reads them.</summary>
    /// <exception cref="InvalidDataException">The file is not a package, or has no document part.</exception>
    public static string ReadCode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using ZipArchive package = ZipFile.OpenRead(path);
            ZipArchiveEntry document = DocumentOf(package)
                ?? throw new InvalidDataException("the package names no code document");
            using Stream stream = document.Open();
            return TextOf(stream);
        }
        catch (Exception ex) when (ex is XmlException or NotSupportedException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>
    /// The part the package's relationships name as its code document. Null when the package has
    /// no relationships or they name none, which MATLAB cannot open either.
    /// </summary>
    private static ZipArchiveEntry? DocumentOf(ZipArchive package)
    {
        if (package.GetEntry(Relationships) is not { } relationships)
        {
            return null;
        }

        using Stream stream = relationships.Open();
        using var reader = XmlReader.Create(stream, Settings());
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                && reader.GetAttribute("Type") is { } type && type.EndsWith(DocumentRelationship, StringComparison.Ordinal)
                && reader.GetAttribute("Target") is { } target)
            {
                return package.GetEntry(target.TrimStart('/'));
            }
        }

        return null;
    }

    /// <summary>Every run of text in the document, joined: the samples hold one run, in one CDATA section.</summary>
    private static string TextOf(Stream document)
    {
        var code = new StringBuilder();
        using var reader = XmlReader.Create(document, Settings());
        bool more = reader.Read();
        while (more)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t"
                && reader.NamespaceURI == WordprocessingNamespace && !reader.IsEmptyElement)
            {
                // Reading the run leaves the reader on whatever follows it, which may be the next run.
                code.Append(reader.ReadElementContentAsString());
                more = !reader.EOF;
                continue;
            }

            more = reader.Read();
        }

        return code.ToString();
    }

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = false,
    };
}
