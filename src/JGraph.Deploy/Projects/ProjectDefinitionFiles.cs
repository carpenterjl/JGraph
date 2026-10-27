using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JGraph.Deploy.Projects;

/// <summary>
/// One node of a MATLAB project's definition files (compiler and SDK plan, stage K20): what one
/// <c>&lt;id&gt;p.xml</c> and <c>&lt;id&gt;d.xml</c> pair under <c>resources\project</c> holds. The
/// <c>p</c> file names the node's place (<c>location</c> and <c>type</c>), the <c>d</c> file carries
/// its data as an <c>&lt;Info&gt;</c> element, and the node's children live in a folder named by its
/// id (probe <c>k20_projects</c>, <c>tree.*</c>).
/// </summary>
public sealed class ProjectNode
{
    /// <summary>Creates a node with an empty <c>&lt;Info/&gt;</c>.</summary>
    public ProjectNode(string location, string type)
    {
        Location = location;
        Type = type;
        Info = new XElement("Info");
    }

    /// <summary>The node's location: a file's name, a category's uuid, <c>Root</c>, <c>ProjectData</c>.</summary>
    public string Location { get; set; }

    /// <summary>The node's type: <c>File</c>, <c>Category</c>, <c>Label</c>, <c>EntryPoint</c>, <c>Reference</c>, <c>Info</c>, ...</summary>
    public string Type { get; }

    /// <summary>The node's data: the <c>d.xml</c> root element, attributes and children included.</summary>
    public XElement Info { get; set; }

    /// <summary>The nodes under this one.</summary>
    public List<ProjectNode> Children { get; } = [];

    /// <summary>The child with <paramref name="location"/> and <paramref name="type"/>, or null.</summary>
    public ProjectNode? Child(string location, string type) =>
        Children.FirstOrDefault(c => c.Type == type && c.Location == location);

    /// <summary>The child with <paramref name="location"/> and <paramref name="type"/>, added when missing.</summary>
    public ProjectNode EnsureChild(string location, string type)
    {
        if (Child(location, type) is { } found)
        {
            return found;
        }

        var made = new ProjectNode(location, type);
        Children.Add(made);
        return made;
    }

    /// <summary>An attribute of the data element, or <paramref name="fallback"/>.</summary>
    public string Attribute(string name, string fallback = "") => Info.Attribute(name)?.Value ?? fallback;

    /// <summary>Sets an attribute of the data element.</summary>
    public void SetAttribute(string name, string value) => Info.SetAttributeValue(name, value);
}

/// <summary>
/// R2025b's <c>fixedPathV2</c> project definition files, read and written as MATLAB writes them
/// (<c>DefinitionFilesType</c> <c>FixedPathMultiFile</c>; probe <c>k20_projects</c>): the
/// <c>&lt;name&gt;.prj</c> stub, <c>resources\project\Project.xml</c>, <c>rootp.xml</c>, and one
/// <c>p</c>/<c>d</c> pair per node, the root's under <c>resources\project\root</c> and every other
/// node's under a folder named by its parent's id. A node's id is the base64url SHA-1 of its
/// location and type concatenated after its ancestors' (<c>RootFileshello.mFile</c> for
/// <c>hello.m</c> under the root's Files node), so JGraph's files land where MATLAB's would.
/// </summary>
public static class ProjectDefinitionFiles
{
    /// <summary>The definition files' folder under the root.</summary>
    public const string ResourcesFolder = "resources";

    /// <summary>The definition files' folder under <see cref="ResourcesFolder"/>.</summary>
    public const string ProjectFolder = "project";

    /// <summary>The XML namespace of the <c>.prj</c> stub.</summary>
    public const string PrjNamespace = "http://www.mathworks.com/MATLABProjectFile";

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    /// <summary>The folder every definition file lives under: <c>&lt;root&gt;\resources\project</c>.</summary>
    public static string DefinitionFolder(string root) => Path.Combine(root, ResourcesFolder, ProjectFolder);

    /// <summary>Whether <paramref name="root"/> holds a project: a <c>.prj</c> stub beside a <c>resources\project\Project.xml</c>.</summary>
    public static bool IsProjectRoot(string root) =>
        Directory.Exists(root) && File.Exists(Path.Combine(DefinitionFolder(root), "Project.xml")) && ProjectFileIn(root) is not null;

    /// <summary>The <c>.prj</c> stub in <paramref name="root"/> (a MATLABProject document), or null.</summary>
    public static string? ProjectFileIn(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        foreach (string candidate in Directory.EnumerateFiles(root, "*.prj").OrderBy(static f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (IsProjectFile(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="file"/> is a MATLAB project's <c>.prj</c> stub rather than a legacy deployment project.</summary>
    public static bool IsProjectFile(string file)
    {
        try
        {
            using var reader = new StreamReader(file, Encoding.UTF8);
            char[] head = new char[512];
            int read = reader.Read(head, 0, head.Length);
            return new string(head, 0, read).Contains("<MATLABProject", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The id of a node whose path from the root is <paramref name="path"/>, each step a location
    /// and a type: base64url(SHA-1(location1 + type1 + location2 + type2 + ...)), no padding.
    /// </summary>
    public static string NodeId(IEnumerable<(string Location, string Type)> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var text = new StringBuilder();
        foreach ((string location, string type) in path)
        {
            text.Append(location).Append(type);
        }

        byte[] digest = SHA1.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads the definition files under <paramref name="root"/>: the root's children, with their subtrees.</summary>
    /// <exception cref="InvalidDataException">The folder has no definition files.</exception>
    public static List<ProjectNode> Read(string root)
    {
        string folder = DefinitionFolder(root);
        if (!File.Exists(Path.Combine(folder, "Project.xml")))
        {
            throw new InvalidDataException($"The folder '{root}' has no project definition files.");
        }

        return ReadChildren(folder, "root", []);
    }

    private static List<ProjectNode> ReadChildren(string definitionFolder, string childrenFolderName, List<(string, string)> path)
    {
        var children = new List<ProjectNode>();
        string childrenFolder = Path.Combine(definitionFolder, childrenFolderName);
        if (!Directory.Exists(childrenFolder))
        {
            return children;
        }

        foreach (string pFile in Directory.EnumerateFiles(childrenFolder, "*p.xml").OrderBy(static f => f, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(pFile)[..^"p.xml".Length];
            XElement place;
            try
            {
                place = XElement.Load(pFile);
            }
            catch (XmlException)
            {
                continue;
            }

            string? location = place.Attribute("location")?.Value;
            string? type = place.Attribute("type")?.Value;
            if (location is null || type is null)
            {
                continue;
            }

            var node = new ProjectNode(location, type);
            string dFile = Path.Combine(childrenFolder, id + "d.xml");
            if (File.Exists(dFile))
            {
                try
                {
                    node.Info = XElement.Load(dFile, LoadOptions.PreserveWhitespace);
                    node.Info = Stripped(node.Info);
                }
                catch (XmlException)
                {
                    node.Info = new XElement("Info");
                }
            }

            var childPath = new List<(string, string)>(path) { (location, type) };
            node.Children.AddRange(ReadChildren(definitionFolder, id, childPath));
            children.Add(node);
        }

        return children;
    }

    /// <summary>The element without the whitespace text nodes a pretty-printed file carries.</summary>
    private static XElement Stripped(XElement element)
    {
        foreach (XText text in element.DescendantNodes().OfType<XText>().Where(static t => string.IsNullOrWhiteSpace(t.Value)).ToList())
        {
            text.Remove();
        }

        return element;
    }

    /// <summary>
    /// Writes the definition files for <paramref name="rootChildren"/> under <paramref name="root"/>:
    /// <c>Project.xml</c> and <c>rootp.xml</c>, every node's pair, and nothing else (a pair no node
    /// has any more is deleted, as are the folders left empty).
    /// </summary>
    public static void Write(string root, IReadOnlyList<ProjectNode> rootChildren)
    {
        ArgumentNullException.ThrowIfNull(rootChildren);
        string folder = DefinitionFolder(root);
        Directory.CreateDirectory(folder);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WriteFile(Path.Combine(folder, "Project.xml"), new XElement("Info", new XAttribute("MetadataType", "fixedPathV2")), wanted);
        WriteFile(Path.Combine(folder, "rootp.xml"), new XElement("Info"), wanted);
        WriteChildren(folder, "root", [], rootChildren, wanted);
        foreach (string stale in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).ToList())
        {
            if (!wanted.Contains(stale))
            {
                File.Delete(stale);
            }
        }

        foreach (string sub in Directory.EnumerateDirectories(folder).ToList())
        {
            if (!Directory.EnumerateFileSystemEntries(sub).Any())
            {
                Directory.Delete(sub);
            }
        }
    }

    private static void WriteChildren(string definitionFolder, string childrenFolderName, List<(string, string)> path, IReadOnlyList<ProjectNode> children, HashSet<string> wanted)
    {
        if (children.Count == 0)
        {
            return;
        }

        string childrenFolder = Path.Combine(definitionFolder, childrenFolderName);
        Directory.CreateDirectory(childrenFolder);
        foreach (ProjectNode node in children)
        {
            var childPath = new List<(string, string)>(path) { (node.Location, node.Type) };
            string id = NodeId(childPath);
            var place = new XElement("Info", new XAttribute("location", node.Location), new XAttribute("type", node.Type));
            WriteFile(Path.Combine(childrenFolder, id + "p.xml"), place, wanted);
            if (!IsContainer(node, path.Count == 0))
            {
                WriteFile(Path.Combine(childrenFolder, id + "d.xml"), node.Info, wanted);
            }
            WriteChildren(definitionFolder, id, childPath, node.Children, wanted);
        }
    }

    /// <summary>
    /// Whether a node is one of the root's containers, which MATLAB writes a <c>p</c> file for and no
    /// <c>d</c> file: Root/Categories, Root/Files, Root/EntryPoints, Root/ProjectPath, Extensions/Root.
    /// </summary>
    private static bool IsContainer(ProjectNode node, bool atRoot) =>
        atRoot && (node.Location == "Root" || (node.Location == "Extensions" && node.Type == "Root")) && !node.Info.HasAttributes && !node.Info.HasElements;

    private static void WriteFile(string file, XElement element, HashSet<string> wanted)
    {
        wanted.Add(file);
        string text = Declaration + Serialize(element);
        if (File.Exists(file) && File.ReadAllText(file, Encoding.UTF8) == text)
        {
            return; // untouched, so a file's time says when it last changed
        }

        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    /// <summary>
    /// The element as MATLAB writes it: attributes in ordinal order, four-space indentation,
    /// <c>&lt;Info/&gt;</c> without a space, no trailing newline.
    /// </summary>
    public static string Serialize(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        XElement ordered = Ordered(element);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, settings))
        {
            ordered.Save(writer);
        }

        return text.ToString().Replace(" />", "/>", StringComparison.Ordinal);
    }

    private static XElement Ordered(XElement element)
    {
        var copy = new XElement(element.Name);
        foreach (XAttribute attribute in element.Attributes().OrderBy(static a => a.Name.LocalName, StringComparer.Ordinal))
        {
            copy.Add(new XAttribute(attribute.Name, attribute.Value));
        }

        foreach (XNode node in element.Nodes())
        {
            copy.Add(node is XElement child ? Ordered(child) : node);
        }

        return copy;
    }

    /// <summary>Writes the <c>.prj</c> stub: an empty MATLABProject document.</summary>
    public static void WriteProjectFile(string file)
    {
        File.WriteAllText(file, Declaration + $"<MATLABProject xmlns=\"{PrjNamespace}\"/>\n", new UTF8Encoding(false));
    }
}
