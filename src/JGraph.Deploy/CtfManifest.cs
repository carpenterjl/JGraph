using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace JGraph.Deploy;

/// <summary>
/// What a component is built as: the <c>target_type</c> numbers R2025b's manifests carry (read from
/// the research session's builds, <c>manifest_summary.txt</c>). The Excel add-in's number is learned
/// in its own stage.
/// </summary>
public enum CtfTargetType
{
    /// <summary><c>mcc -m</c>: a console program.</summary>
    StandaloneApplication = 1,

    /// <summary><c>mcc -e</c>: a program with no console window.</summary>
    WindowsStandaloneApplication = 2,

    /// <summary><c>-W lib</c>: a C shared library.</summary>
    CSharedLibrary = 3,

    /// <summary><c>-W dotnet</c>: a .NET assembly, either API.</summary>
    DotNetAssembly = 7,

    /// <summary><c>-W cpplib</c>: a C++ shared library.</summary>
    CppSharedLibrary = 9,

    /// <summary><c>-W CTF</c>: a Production Server archive.</summary>
    ProductionServerArchive = 10,

    /// <summary><c>-W python</c>: a Python package.</summary>
    PythonPackage = 14,
}

/// <summary>
/// How an archive's code is stored (decision KD5), each a value of the manifest's
/// <c>protection</c> attribute. Neither is encrypted (decision of 2026-10-24): R2025b's AES
/// default, <c>-s</c>, <c>-k</c> and <c>-J</c> are not built (ADR 0259).
/// </summary>
public enum CtfProtection
{
    /// <summary>The code is stored as written (decision KO1, 2026-10-23; R2025b always encrypts).</summary>
    Open,

    /// <summary>
    /// <c>mcc -j</c>: each code file is stored as JGraph's P-code, its tokens without comments or
    /// line numbers (stage K3b). A precompiled form, not a protection: it is not encrypted.
    /// </summary>
    PCode,
}

/// <summary>A public function of a component: its name, its file in the archive, and its parameters.</summary>
/// <param name="Name">The function's name.</param>
/// <param name="File">Its file, relative to the archive's file root, with forward slashes.</param>
/// <param name="Inputs">Its input parameters, <c>varargin</c> included, in order.</param>
/// <param name="Outputs">Its outputs, <c>varargout</c> included, in order.</param>
public sealed record CtfFunction(string Name, string File, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

/// <summary>A file in the archive: its place under the file root, its length and its SHA-256.</summary>
/// <param name="Path">Relative to the file root (<c>fsroot/</c> in the package), with forward slashes.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Sha256">Its SHA-256, lowercase hex.</param>
public sealed record CtfFileEntry(string Path, long Length, string Sha256);

/// <summary>
/// The archive's <c>.META/manifest.xml</c>: R2025b's element structure (a <c>manifest</c> root with
/// its <c>component-name</c>, <c>uuid</c>, <c>target_type</c> and runtime version attributes, a
/// <c>directory</c> per folder, a <c>function</c> per public function, and a <c>configuration</c>
/// holding the path, the runtime options, the warning state, the main function and the public
/// functions), without R2025b's keys, signature and licence attributes, and with what JGraph adds:
/// its own version, the protection level, each function's file, and each file's length and hash, which
/// the runtime cache checks a cached copy against.
/// </summary>
public sealed class CtfManifest
{
    /// <summary>The release the MATLAB dialect follows, which the manifest names as its runtime (KO2).</summary>
    public static readonly IReadOnlyList<int> RuntimeVersion = [25, 2, 0];

    /// <summary>The warning state every R2025b manifest carries, kept so a deployed session starts the same way.</summary>
    public const string DefaultWarningState = "off:MATLAB:dispatcher:nameConflict";

    /// <summary>The folder a deployed session's <c>prefdir</c> is, under the file root.</summary>
    public const string PreferencesFolder = ".matlab";

    /// <summary>Creates a manifest.</summary>
    public CtfManifest(
        string componentName,
        string uuid,
        CtfTargetType targetType,
        IReadOnlyList<string> matlabPath,
        IReadOnlyList<CtfFunction> publicFunctions,
        IReadOnlyList<CtfFileEntry> files)
    {
        ArgumentException.ThrowIfNullOrEmpty(componentName);
        ArgumentException.ThrowIfNullOrEmpty(uuid);
        ComponentName = componentName;
        Uuid = uuid;
        TargetType = targetType;
        MatlabPath = [.. matlabPath];
        PublicFunctions = [.. publicFunctions];
        Files = [.. files];
    }

    /// <summary>The component's name.</summary>
    public string ComponentName { get; }

    /// <summary>
    /// The archive's identity, <c>&lt;component&gt;_&lt;guid&gt;</c> as R2025b writes it; the runtime
    /// cache keys on it and <c>compiler.build.Results.BuildID</c> answers it.
    /// </summary>
    public string Uuid { get; }

    /// <summary>What the component was built as.</summary>
    public CtfTargetType TargetType { get; }

    /// <summary>The folders on the deployed session's frozen path, relative to the file root, in order.</summary>
    public IReadOnlyList<string> MatlabPath { get; }

    /// <summary>The functions a client may call; the main function is one of them.</summary>
    public IReadOnlyList<CtfFunction> PublicFunctions { get; }

    /// <summary>Every file under the file root.</summary>
    public IReadOnlyList<CtfFileEntry> Files { get; }

    /// <summary>The function a standalone program starts in, or <see langword="null"/> for a library.</summary>
    public string? MainFunction { get; init; }

    /// <summary>
    /// Whether the main's arguments go through <c>mcc -n</c>'s rule. R2025b records it as
    /// <c>use_native_matlab_types="1"</c> (the research session's <c>helnum</c> build).
    /// </summary>
    public bool NumericArguments { get; init; }

    /// <summary>The runtime options (<c>-R</c>); R2025b's standalone default is <c>-nojvm</c>.</summary>
    public IReadOnlyList<string> RuntimeOptions { get; init; } = [];

    /// <summary>The warning states the session starts with.</summary>
    public IReadOnlyList<string> WarningState { get; init; } = [DefaultWarningState];

    /// <summary>The JGraph version that built the archive.</summary>
    public string JGraphVersion { get; init; } = typeof(CtfManifest).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>How the code is protected.</summary>
    public CtfProtection Protection { get; init; } = CtfProtection.Open;

    /// <summary>A fresh uuid for <paramref name="componentName"/>: the name, an underscore and a GUID, as R2025b makes them.</summary>
    public static string NewUuid(string componentName) => componentName + "_" + Guid.NewGuid().ToString("D");

    /// <summary>The manifest as the XML the archive holds, UTF-8 without a byte order mark.</summary>
    public byte[] ToXml()
    {
        var folders = new List<string>();
        foreach (string folder in MatlabPath.Append(PreferencesFolder))
        {
            if (!folders.Contains(folder, StringComparer.Ordinal))
            {
                folders.Add(folder);
            }
        }

        var root = new XElement("manifest",
            new XAttribute("component-name", ComponentName),
            new XAttribute("jgraph-version", JGraphVersion),
            new XAttribute("mcr-major-version", RuntimeVersion[0].ToString(CultureInfo.InvariantCulture)),
            new XAttribute("mcr-minor-version", RuntimeVersion[1].ToString(CultureInfo.InvariantCulture)),
            new XAttribute("mcr-release-version", RuntimeVersion[2].ToString(CultureInfo.InvariantCulture)),
            new XAttribute("protection", ProtectionName(Protection)),
            new XAttribute("target_type", ((int)TargetType).ToString(CultureInfo.InvariantCulture)),
            new XAttribute("use_native_matlab_types", NumericArguments ? "1" : "0"),
            new XAttribute("uuid", Uuid));
        // Two folders whose names differ only in punctuation would share an id; the later one is numbered.
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string folder in folders)
        {
            string id = DirectoryId(folder);
            for (int n = 2; ids.ContainsValue(id); n++)
            {
                id = DirectoryId(folder) + "_" + n.ToString(CultureInfo.InvariantCulture);
            }

            ids[folder] = id;
            root.Add(new XElement("directory", new XAttribute("id", id), new XAttribute("path", folder)));
        }

        foreach (CtfFunction function in PublicFunctions)
        {
            root.Add(new XElement("function",
                new XAttribute("id", FunctionId(function.Name)),
                new XAttribute("file", function.File),
                new XElement("name", function.Name),
                new XElement("inputs", function.Inputs.Select(p => new XElement("param", new XAttribute("name", p)))),
                new XElement("outputs", function.Outputs.Select(p => new XElement("param", new XAttribute("name", p))))));
        }

        foreach (CtfFileEntry file in Files)
        {
            root.Add(new XElement("file",
                new XAttribute("path", file.Path),
                new XAttribute("length", file.Length.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("sha256", file.Sha256)));
        }

        var configuration = new XElement("configuration",
            new XAttribute("id", "default"),
            new XElement("matlab-path", MatlabPath.Select(f => new XElement("path-item", new XAttribute("ref", ids[f])))),
            new XElement("runtime-options", RuntimeOptions.Select(o => new XElement("option", o))),
            new XElement("warning-state", WarningState.Select(o => new XElement("option", o))),
            new XElement("prefdir", new XAttribute("ref", ids[PreferencesFolder])));
        if (MainFunction is { } main)
        {
            configuration.Add(new XElement("main-function", new XAttribute("ref", FunctionId(main))));
        }

        configuration.Add(new XElement("public-functions",
            PublicFunctions.Select(f => new XElement("function", new XAttribute("ref", FunctionId(f.Name))))));
        root.Add(configuration);
        return CtfArchiveWriter.Xml(root, standalone: "no");
    }

    /// <summary>Reads a manifest written by <see cref="ToXml"/>.</summary>
    /// <exception cref="InvalidDataException">The XML is not a JGraph archive manifest.</exception>
    public static CtfManifest Parse(Stream xml)
    {
        XElement root;
        try
        {
            root = XDocument.Load(xml).Root ?? throw new InvalidDataException("The manifest is empty.");
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The manifest is not well-formed XML: " + ex.Message, ex);
        }

        if (root.Name.LocalName != "manifest" || root.Attribute("jgraph-version") is null)
        {
            throw new InvalidDataException("The manifest was not written by JGraph.");
        }

        string Required(XElement element, string name) =>
            element.Attribute(name)?.Value ?? throw new InvalidDataException($"The manifest's {element.Name.LocalName} has no {name}.");

        var directories = root.Elements("directory").ToDictionary(d => Required(d, "id"), d => Required(d, "path"), StringComparer.Ordinal);
        var functions = root.Elements("function").Select(f => (Id: Required(f, "id"), Function: new CtfFunction(
            f.Element("name")?.Value ?? throw new InvalidDataException("A manifest function has no name."),
            Required(f, "file"),
            [.. (f.Element("inputs")?.Elements("param") ?? []).Select(p => Required(p, "name"))],
            [.. (f.Element("outputs")?.Elements("param") ?? []).Select(p => Required(p, "name"))]))).ToList();
        var files = root.Elements("file").Select(f => new CtfFileEntry(
            Required(f, "path"),
            long.Parse(Required(f, "length"), NumberStyles.None, CultureInfo.InvariantCulture),
            Required(f, "sha256"))).ToList();

        XElement configuration = root.Element("configuration") ?? throw new InvalidDataException("The manifest has no configuration.");
        string Folder(string reference) => directories.TryGetValue(reference, out string? path)
            ? path
            : throw new InvalidDataException($"The manifest's path names a directory it does not declare: {reference}.");
        var matlabPath = (configuration.Element("matlab-path")?.Elements("path-item") ?? [])
            .Select(p => Folder(Required(p, "ref"))).ToList();
        string? mainRef = configuration.Element("main-function")?.Attribute("ref")?.Value;
        string? main = mainRef is null
            ? null
            : functions.FirstOrDefault(f => f.Id == mainRef).Function?.Name
                ?? throw new InvalidDataException($"The manifest's main function {mainRef} is not one of its functions.");

        if (!int.TryParse(Required(root, "target_type"), NumberStyles.None, CultureInfo.InvariantCulture, out int target))
        {
            throw new InvalidDataException("The manifest's target_type is not a number.");
        }

        return new CtfManifest(Required(root, "component-name"), Required(root, "uuid"), (CtfTargetType)target, matlabPath,
            [.. functions.Select(f => f.Function)], files)
        {
            MainFunction = main,
            NumericArguments = root.Attribute("use_native_matlab_types")?.Value == "1",
            RuntimeOptions = [.. (configuration.Element("runtime-options")?.Elements("option") ?? []).Select(o => o.Value)],
            WarningState = [.. (configuration.Element("warning-state")?.Elements("option") ?? []).Select(o => o.Value)],
            JGraphVersion = Required(root, "jgraph-version"),
            Protection = ParseProtection(root.Attribute("protection")?.Value ?? "open"),
        };
    }

    /// <summary>The main function's entry, or <see langword="null"/> when the component has none.</summary>
    public CtfFunction? Main => MainFunction is { } name ? PublicFunctions.FirstOrDefault(f => f.Name == name) : null;

    private static string ProtectionName(CtfProtection protection) => protection switch
    {
        CtfProtection.Open => "open",
        CtfProtection.PCode => "pcode",
        _ => throw new ArgumentOutOfRangeException(nameof(protection)),
    };

    private static CtfProtection ParseProtection(string name) => name switch
    {
        "open" => CtfProtection.Open,
        "pcode" => CtfProtection.PCode,
        _ => throw new InvalidDataException(
            $"The archive's code is protected as '{name}', which this JGraph Runtime cannot read; it is newer than the runtime."),
    };

    /// <summary>R2025b's directory ids: <c>DIR_</c> and the path with every character outside [A-Za-z0-9] made an underscore.</summary>
    internal static string DirectoryId(string folder) => "DIR_" + Sanitize(folder);

    private static string FunctionId(string name) => "FUNCTION_" + Sanitize(name);

    private static string Sanitize(string text)
    {
        var id = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            id.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }

        return id.ToString();
    }
}
