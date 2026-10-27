using System.Xml;
using System.Xml.Linq;

namespace JGraph.Deploy.Projects;

/// <summary>
/// What a legacy deployment project refuses with: R2025b's <c>Compiler:build:compatibility:*</c>
/// identifiers and words (compiler and SDK plan, stage K21).
/// </summary>
public sealed class LegacyProjectException(string identifier, string message) : Exception(message)
{
    /// <summary>The MATLAB identifier.</summary>
    public string Identifier { get; } = identifier;
}

/// <summary>
/// One element of a legacy project read the way R2025b's <c>readstruct</c> reads XML with
/// <c>DetectTypes</c> off: each attribute a field named <c>&lt;name&gt;Attribute</c>, each child
/// element a field named after it with dots and dashes made underscores, a leaf child its text, a
/// repeated leaf a string array, a child with children or attributes a struct and a repeated one a
/// struct array, the text of an element that also has attributes under <c>Text</c>, and an empty
/// element "" (probe <c>k21_legacy</c>, <c>prj.*</c>).
/// </summary>
public sealed class LegacyXmlStruct
{
    /// <summary>The fields in document order. A value is a string, a string[], a <see cref="LegacyXmlStruct"/> or a <see cref="LegacyXmlStruct"/>[].</summary>
    public List<(string Name, object Value)> Fields { get; } = [];

    /// <summary>The field's value, or null when there is none.</summary>
    public object? this[string name] => Fields.FirstOrDefault(f => f.Name == name).Value;

    /// <summary>A field's text, "" for a missing field or one that is not text.</summary>
    public string Text(string name) => this[name] as string ?? "";

    /// <summary>A field's texts: one for a string, each for a string array, none otherwise.</summary>
    public IReadOnlyList<string> Texts(string name) => this[name] switch
    {
        string s => s.Length == 0 ? [] : [s],
        string[] a => a,
        _ => [],
    };

    /// <summary>The field's struct, or null.</summary>
    public LegacyXmlStruct? Struct(string name) => this[name] as LegacyXmlStruct;

    /// <summary>The field's structs: one for a struct, each for an array, none otherwise.</summary>
    public IReadOnlyList<LegacyXmlStruct> Structs(string name) => this[name] switch
    {
        LegacyXmlStruct s => [s],
        LegacyXmlStruct[] a => a,
        _ => [],
    };

    /// <summary>readstruct's field name for an XML name: dots and dashes become underscores.</summary>
    public static string FieldName(string xmlName) => xmlName.Replace('.', '_').Replace('-', '_');

    /// <summary>Reads <paramref name="element"/> as readstruct reads it.</summary>
    public static LegacyXmlStruct From(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var result = new LegacyXmlStruct();
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            result.Fields.Add((FieldName(attribute.Name.LocalName) + "Attribute", attribute.Value));
        }

        if (element.HasAttributes && !element.HasElements && element.Value.Length > 0)
        {
            result.Fields.Add(("Text", element.Value));
        }

        var groups = new List<(string Name, List<XElement> Elements)>();
        foreach (XElement child in element.Elements())
        {
            string name = child.Name.LocalName;
            int at = groups.FindIndex(g => g.Name == name);
            if (at < 0)
            {
                groups.Add((name, [child]));
            }
            else
            {
                groups[at].Elements.Add(child);
            }
        }

        foreach ((string name, List<XElement> elements) in groups)
        {
            bool leaves = elements.All(static e => !e.HasElements && !e.HasAttributes);
            if (leaves)
            {
                result.Fields.Add((FieldName(name), elements.Count == 1 ? elements[0].Value : elements.Select(static e => e.Value).ToArray()));
            }
            else
            {
                result.Fields.Add((FieldName(name), elements.Count == 1 ? From(elements[0]) : elements.Select(From).ToArray()));
            }
        }

        return result;
    }

    /// <summary>A copy with every text made over by <paramref name="map"/>.</summary>
    public LegacyXmlStruct Map(Func<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var result = new LegacyXmlStruct();
        foreach ((string name, object value) in Fields)
        {
            result.Fields.Add((name, value switch
            {
                string s => map(s),
                string[] a => a.Select(map).ToArray(),
                LegacyXmlStruct s => s.Map(map),
                LegacyXmlStruct[] a => a.Select(s => s.Map(map)).ToArray(),
                _ => value,
            }));
        }

        return result;
    }
}

/// <summary>A class of a legacy library project's class organization: its package, name and files.</summary>
public sealed record LegacyProjectClass(string Package, string Name, IReadOnlyList<string> Files);

/// <summary>
/// A legacy MATLAB Compiler deployment project (compiler and SDK plan, stage K21): the
/// <c>&lt;deployment-project plugin="plugin.ezdeploy"&gt;</c> file the Application Compiler, Library
/// Compiler, Production Server Compiler and Web App Compiler apps of R2024b and before wrote, read as
/// R2025b's <c>compiler.internal.readPRJStruct</c> reads one and refused as its
/// <c>createDeploymentScript</c> and <c>deploytool -build</c> refuse one. The file's own folder is
/// <c>${PROJECT_ROOT}</c>, whatever the configuration's <c>location</c> says; <c>${MATLAB_ROOT}</c>
/// is the runtime's root; every slash becomes the file separator. Nothing writes the legacy form.
/// </summary>
public sealed class LegacyDeploymentProject
{
    /// <summary>The Application Compiler's target.</summary>
    public const string StandaloneTarget = "target.ezdeploy.standalone";

    /// <summary>The Library Compiler's target.</summary>
    public const string LibraryTarget = "target.ezdeploy.library";

    /// <summary>The Production Server Compiler's target.</summary>
    public const string ProductionServerTarget = "target.ezdeploy.mps.archive";

    /// <summary>The Web App Compiler's target (plugin.webdeploy).</summary>
    public const string WebAppTarget = "target.webdeploy";

    /// <summary>The Hadoop Compiler's target, which R2025b no longer builds.</summary>
    public const string HadoopTarget = "target.ezdeploy.map.reduce";

    /// <summary>The sub-targets R2025b's legacy builders know, by <c>param.target.type</c>.</summary>
    public static readonly string[] SubTargets =
    [
        "subtarget.standalone", "subtarget.library.c", "subtarget.library.cpp", "subtarget.net.component", "subtarget.java.package",
        "subtarget.python.module", "subtarget.com.component", "subtarget.ex.addin", "subtarget.mads", "subtarget.mps.excel",
        "subtarget.web.app", "subtarget.hadoop",
    ];

    private readonly Dictionary<string, string> _params = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _fileSets = new(StringComparer.Ordinal);

    private LegacyDeploymentProject(string file, XElement configuration, LegacyXmlStruct raw, LegacyXmlStruct resolved)
    {
        File = file;
        Folder = Path.GetDirectoryName(file) ?? "";
        Configuration = configuration;
        Raw = raw;
        Resolved = resolved;
        Plugin = configuration.Parent?.Attribute("plugin")?.Value ?? "";
        Target = configuration.Attribute("target")?.Value ?? "";
        TargetName = configuration.Attribute("target-name")?.Value ?? "";
        Name = configuration.Attribute("name")?.Value ?? "";
        Location = configuration.Attribute("location")?.Value ?? "";
        foreach (XElement child in configuration.Elements())
        {
            string name = child.Name.LocalName;
            if (name.StartsWith("param.", StringComparison.Ordinal))
            {
                if (name == "param.icons" && child.HasElements)
                {
                    Icons = child.Elements("file").Select(e => Resolve(e.Value)).ToList();
                    _params[name] = "";
                }
                else
                {
                    _params[name] = Resolve(child.Value);
                }
            }
            else if (name.StartsWith("fileset.", StringComparison.Ordinal))
            {
                if (name == "fileset.classes")
                {
                    var classes = new List<LegacyProjectClass>();
                    foreach (XElement package in child.Elements("entity.package"))
                    {
                        string packageName = package.Attribute("name")?.Value ?? "";
                        foreach (XElement cls in package.Elements("entity.class"))
                        {
                            classes.Add(new LegacyProjectClass(packageName, cls.Attribute("name")?.Value ?? "",
                                cls.Elements("file").Select(e => Resolve(e.Value)).ToList()));
                        }
                    }

                    Classes = classes;
                }

                _fileSets[name] = child.Elements("file").Select(e => Resolve(e.Value)).ToList();
            }
        }
    }

    /// <summary>The project file's full path.</summary>
    public string File { get; }

    /// <summary>The file's folder: what <c>${PROJECT_ROOT}</c> stands for.</summary>
    public string Folder { get; }

    /// <summary>The <c>&lt;configuration&gt;</c> element.</summary>
    public XElement Configuration { get; }

    /// <summary>The configuration as readstruct reads it, macros unresolved.</summary>
    public LegacyXmlStruct Raw { get; }

    /// <summary>The configuration as <c>compiler.internal.readPRJStruct</c> answers it: macros resolved and slashes made the file separator in every text.</summary>
    public LegacyXmlStruct Resolved { get; }

    /// <summary>The plugin: <c>plugin.ezdeploy</c>, or <c>plugin.webdeploy</c> for a web app.</summary>
    public string Plugin { get; }

    /// <summary>The target attribute: one of the <c>*Target</c> constants.</summary>
    public string Target { get; }

    /// <summary>The target's display name: Application Compiler, Library Compiler, ...</summary>
    public string TargetName { get; }

    /// <summary>The configuration's name attribute.</summary>
    public string Name { get; }

    /// <summary>The configuration's location attribute: the folder the project was saved in, which may be elsewhere now.</summary>
    public string Location { get; }

    /// <summary>The sub-target: <c>param.target.type</c>.</summary>
    public string SubTarget => Param("param.target.type");

    /// <summary>The <c>param.icons</c> files, when the element held files.</summary>
    public IReadOnlyList<string> Icons { get; } = [];

    /// <summary>The class organization of a library project, in document order.</summary>
    public IReadOnlyList<LegacyProjectClass> Classes { get; } = [];

    /// <summary>The parameters by their element names (<c>param.appname</c>, ...), macros resolved.</summary>
    public IReadOnlyDictionary<string, string> Params => _params;

    /// <summary>The file sets by their element names (<c>fileset.main</c>, ...), macros resolved.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FileSets => _fileSets;

    /// <summary>A parameter's value, "" when absent.</summary>
    public string Param(string name) => _params.TryGetValue(name, out string? value) ? value : "";

    /// <summary>A parameter as a flag: the text <c>true</c>.</summary>
    public bool Flag(string name) => Param(name).Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A file set's files, none when absent.</summary>
    public IReadOnlyList<string> FileSet(string name) => _fileSets.TryGetValue(name, out IReadOnlyList<string>? files) ? files : [];

    /// <summary>Whether the file set element is present at all (an absent one is what R2025b's adapters trip over).</summary>
    public bool HasFileSet(string name) => _fileSets.ContainsKey(name);

    /// <summary>The root MATLAB folder <c>${MATLAB_ROOT}</c> stands for: the runtime's, or the process's folder.</summary>
    public static string MatlabRoot { get; set; } = AppContext.BaseDirectory.TrimEnd('\\', '/');

    /// <summary>
    /// Whether <paramref name="file"/> is a legacy deployment project by its text: a
    /// <c>&lt;deployment-project</c> element.
    /// </summary>
    public static bool IsLegacyProjectFile(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!System.IO.File.Exists(file))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(file);
            char[] head = new char[2048];
            int read = reader.Read(head, 0, head.Length);
            return new string(head, 0, read).Contains("<deployment-project", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <paramref name="file"/> as R2025b's legacy builders read one: a missing file is
    /// <c>projectDoesNotExist</c>; a file that is not XML, has no configuration, or names a
    /// sub-target the builders do not know is <c>notCompatibilityPRJ</c>; a Hadoop project is
    /// <c>hadoopNotSupported</c>. The name in the words is the file as given.
    /// </summary>
    public static LegacyDeploymentProject Read(string file, string? asGiven = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        string given = asGiven ?? file;
        if (!System.IO.File.Exists(file))
        {
            throw new LegacyProjectException("Compiler:build:compatibility:projectDoesNotExist", $"Input project file {given} does not exist.");
        }

        LegacyDeploymentProject project = TryRead(file) ?? throw new LegacyProjectException("Compiler:build:compatibility:notCompatibilityPRJ",
            $"Input project file {given} is not a valid MATLAB Compiler or MATLAB Compiler SDK project.");
        if (project.Target == HadoopTarget || project.SubTarget == "subtarget.hadoop")
        {
            throw new LegacyProjectException("Compiler:build:compatibility:hadoopNotSupported",
                "Building Hadoop Compiler projects is no longer supported.  Use compiler.build.standaloneApplication instead.");
        }

        return project;
    }

    /// <summary>
    /// Reads <paramref name="file"/> as <c>compiler.internal.readPRJStruct</c> does, without the
    /// builders' checks: null when the file is not XML with a <c>configuration</c> element. A
    /// sub-target the builders do not know still reads.
    /// </summary>
    public static LegacyDeploymentProject? ReadStruct(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        XDocument document;
        try
        {
            document = XDocument.Load(file, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }

        XElement? configuration = document.Root?.Element("configuration");
        if (configuration is null)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
        LegacyXmlStruct raw = LegacyXmlStruct.From(configuration);
        LegacyXmlStruct resolved = raw.Map(text => ResolveText(text, folder));
        return new LegacyDeploymentProject(Path.GetFullPath(file), configuration, raw, resolved);
    }

    private static LegacyDeploymentProject? TryRead(string file)
    {
        LegacyDeploymentProject? project = ReadStruct(file);
        return project is not null && SubTargets.Contains(project.SubTarget) ? project : null;
    }

    /// <summary>A text with the macros resolved against this project's folder.</summary>
    public string Resolve(string text) => ResolveText(text, Folder);

    /// <summary>
    /// readPRJStruct's resolution: <c>${PROJECT_ROOT}</c> is <paramref name="folder"/>,
    /// <c>${MATLAB_ROOT}</c> the MATLAB root, every slash the file separator, and <c>\&gt;</c>
    /// put back to <c>/&gt;</c> so XML held in a value keeps its closing tags.
    /// </summary>
    public static string ResolveText(string text, string folder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(folder);
        string separator = Path.DirectorySeparatorChar.ToString();
        return text.Replace("${PROJECT_ROOT}", folder, StringComparison.Ordinal)
            .Replace("${MATLAB_ROOT}", MatlabRoot, StringComparison.Ordinal)
            .Replace("\\", separator, StringComparison.Ordinal)
            .Replace("/", separator, StringComparison.Ordinal)
            .Replace("\\>", "/>", StringComparison.Ordinal);
    }
}
