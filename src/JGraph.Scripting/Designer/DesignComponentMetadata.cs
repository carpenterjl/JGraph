using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// What the "App Designer Custom UI Component Metadata" dialog and <c>appdesigner.customcomponent.removeMetadata</c>
/// learned of a component when it ran: where its root figure stood, or why the component cannot be
/// configured for apps, in R2025b's words (<c>MetadataUIViewModel.getAvatar</c> and <c>validateComponent</c>).
/// </summary>
/// <param name="Position">The component's <c>Position</c> in an invisible figure, which becomes the registry's <c>defaultPosition</c>; null when <paramref name="Error"/> says why not.</param>
/// <param name="Error">Why the component cannot be registered: an abstract class, other <c>Units</c> than pixels, children added to the parent, a public callback assigned, or what its construction failed with.</param>
public sealed record DesignComponentCheck(IReadOnlyList<double>? Position, string? Error);

/// <summary>
/// A custom UI component's registration for App Designer's Component Library (App Designer window plan W15,
/// chip 162): the <c>resources/appDesigner.json</c> beside the component's class - under the folder above its
/// <c>+package</c> or <c>@class</c> folders - read, checked and written as R2025b's
/// <c>appdesigner.internal.usercomponent.metadata.Model</c> and <c>SchemaUtils</c> do (read in place; the file's
/// text measured by probe <c>w15_component</c>: <c>jsonencode</c>'s PrettyPrint, keys in merge order,
/// <c>schema</c> 3 and <c>MATLABRelease</c> last, no trailing line break). The dialog's defaults, checks and
/// words are <c>MetadataUIViewModel</c>'s and <c>usercomponentmetadata.xml</c>'s.
/// </summary>
public sealed partial class DesignComponentMetadata
{
    /// <summary>The folder the registry file is in, under the component's root folder.</summary>
    public const string ResourcesFolder = "resources";

    /// <summary>The registry file's name.</summary>
    public const string FileName = "appDesigner.json";

    /// <summary>The class every custom UI component derives from.</summary>
    public const string Superclass = "matlab.ui.componentcontainer.ComponentContainer";

    /// <summary>The category a component not yet registered is offered (<c>MyComponents</c>).</summary>
    public const string DefaultCategory = "My Components";

    /// <summary>The version a component not yet registered is offered.</summary>
    public const string DefaultVersion = "1.0";

    /// <summary>The release the file names as the one that wrote it (R2025b's <c>ReleaseUtil.getCurrentRelease</c>): JGraph writes the dialect it speaks.</summary>
    public const string Release = "R2025b";

    /// <summary>The schema version the file carries.</summary>
    public const int Schema = 3;

    /// <summary>How long a path the dialog shows whole; a longer one is cut to <c>...\name.m</c>.</summary>
    public const int MaxFilePathLength = 76;

    /// <summary>
    /// JGraph's own 24-by-24 library icon for a component registered without one of the author's, as a data
    /// URI (R2025b writes its <c>default_component_icon.png</c>, which is MathWorks' picture): a framed square.
    /// </summary>
    public const string DefaultIcon = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAOklEQVR42mNgGFbAOm/pf2pgnIZT06EjwIK5ex78JwWPWkB7C9DT+qgF9LdgNJnS34LR4pr+VeaQBQDVW2aXsKM6LQAAAABJRU5ErkJggg==";

    // --- the dialog's words (usercomponentmetadata.xml) ---------------------------------------------

    /// <summary>The dialog's title (<c>MetadataUITitle</c>).</summary>
    public const string Title = "App Designer Custom UI Component Metadata";

    /// <summary>The header of the dialog's alerts and confirmations (<c>ComponentRegistrationHeader</c>).</summary>
    public const string Header = "App Designer";

    /// <summary>The line over the dialog's fields (<c>HeadingLabel</c>).</summary>
    public const string Heading = "To configure the component for use in App Designer, specify its information and add its folder to the MATLAB path.";

    /// <summary>Said once a component is registered (<c>RegistrationSuccessMsg</c>).</summary>
    public const string RegistrationSuccess = "A resources folder with the App Designer metadata has been created.\n\nTo use the component in App Designer, add the folder containing the component and resources to the MATLAB path.";

    /// <summary>Said once a registered component's metadata is written again (<c>UpdateSuccessMsg</c>).</summary>
    public const string UpdateSuccess = "The resources folder with the App Designer metadata has been updated.\n\nTo use the component in App Designer, add the folder containing the component and resources to the MATLAB path.";

    /// <summary>Said when the registry cannot be written (<c>NoWriteAccessErrorMsg</c>).</summary>
    public const string NoWriteAccess = "MATLAB doesn't have write access to the folder containing the component.\n\nwrite access is required to create the resources folder with the App Designer metadata.";

    /// <summary>Said when the folder's registry file cannot be read (<c>InvalidModelWarningMsg</c>).</summary>
    public const string InvalidModelWarning = "Metadata file under the resources folder in the folder containing component is invalid.\n\nSaving metadata for this component will overwrite existing metadata file.";

    /// <summary>Why a component that gave its parent other children cannot be registered (<c>RogueComponentErrorMsg</c>).</summary>
    public const string RogueComponent = "To use the component in App Designer, the component cannot dynamically add additional components to its parent.";

    /// <summary>Why a component whose <c>Units</c> are not pixels cannot be registered (<c>InvalidUnitsErrorMsg</c>).</summary>
    public const string InvalidUnits = "To use the component in App Designer, the component must have its 'Units' property set to 'pixels'.";

    /// <summary>What the dialog says while the component is checked and the file written (<c>RegistrationInProgressMsg</c>).</summary>
    public const string InProgress = "Configuring the component’s App Designer metadata...";

    /// <summary>The Browse button's refusal of a file that is not a picture (<c>IconFileErrorMsg</c>).</summary>
    public const string IconFileError = "File must be an image of type .png, .jpeg, .jpg, or .gif";

    /// <summary>The category field's refusal (<c>CategoryEmptyErrorMsg</c>).</summary>
    public const string CategoryEmpty = "Category cannot be empty";

    /// <summary>The name field's refusal (<c>ComponentNameEmptyErrorMsg</c>).</summary>
    public const string ComponentNameEmpty = "Component name cannot be empty";

    /// <summary>The version field's refusal (<c>InvalidVersionErrorMsg</c>).</summary>
    public const string InvalidVersion = "Component version cannot be empty and must be of the form Major.Minor";

    /// <summary>The email field's refusal (<c>InvalidEmailErrorMsg</c>).</summary>
    public const string InvalidEmail = "Please enter a valid email";

    /// <summary>The help icon's tip beside Component Details (<c>ComponentDetailsHelp</c>).</summary>
    public const string ComponentDetailsHelp = "Specify optional information about the component, to be displayed in a tooltip";

    /// <summary>The help icon's tip beside Category (<c>CategoryHelp</c>).</summary>
    public const string CategoryHelp = "Specify the section of the Component Library in which the component appears";

    /// <summary>The success confirmation's first button, which puts the component's folder on the path (<c>AddPathLabel</c>).</summary>
    public const string AddToPath = "Add to Path";

    /// <summary>The identifier <c>removeMetadata</c> refuses an unreadable registry under (R2025b names it and has no text for it; see <see cref="InvalidModelError"/>).</summary>
    public const string InvalidModelIdentifier = "MATLAB:appdesigner:usercomponentmetadata:InvalidModelErrorMsg";

    /// <summary>What <c>removeMetadata</c> says of an unreadable registry: R2025b's catalog has no <c>InvalidModelErrorMsg</c> entry, so JGraph says the warning's first sentence.</summary>
    public const string InvalidModelError = "Metadata file under the resources folder in the folder containing component is invalid.";

    private const string DefaultDescription = "This is a custom component created by MATLAB user";
    private const string SchemaDefaultCategory = "My Category";

    /// <summary>What the schema fills a registered component in with when its record lacks a required key (<c>appDesignerSchema.json</c>'s defaults, their types as written).</summary>
    private static readonly IReadOnlyList<(string Key, JsonNode? Default)> RequiredKeys =
    [
        ("componentName", JsonValue.Create("Custom Component")),
        ("version", JsonValue.Create(1.0)),
        ("description", JsonValue.Create(DefaultDescription)),
        ("category", JsonValue.Create(SchemaDefaultCategory)),
        ("icon", JsonValue.Create(DesignUserComponents.SchemaDefaultIcon)),
        ("authorName", JsonValue.Create(string.Empty)),
        ("authorEmail", JsonValue.Create(string.Empty)),
        ("className", JsonValue.Create(string.Empty)),
        ("avatar", JsonValue.Create(string.Empty)),
        ("avatarDark", JsonValue.Create(string.Empty)),
        ("defaultPosition", JsonValue.Create("[100 100 100 100]")),
    ];

    private readonly JsonObject _file;

    private DesignComponentMetadata(string fullFileName, string directory, string className, string componentName, JsonObject file, bool isValid)
    {
        FullFileName = fullFileName;
        Directory = directory;
        ClassName = className;
        ComponentName = componentName;
        _file = file;
        IsValid = isValid;
        Categories = Components.Select(static c => Text(c, "category")).Where(static c => c.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(static c => c, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The component's class file, as validated.</summary>
    public string FullFileName { get; }

    /// <summary>The root folder the registry belongs to: the class file's folder, or the folder above its <c>+package</c> and <c>@class</c> folders.</summary>
    public string Directory { get; }

    /// <summary>The class's name, package-qualified when it is in one.</summary>
    public string ClassName { get; }

    /// <summary>The class's name without its packages: the name the dialog offers.</summary>
    public string ComponentName { get; }

    /// <summary>The registry file's path, whether or not it exists.</summary>
    public string MetadataFilePath => Path.Combine(Directory, ResourcesFolder, FileName);

    /// <summary>Whether the registry file is missing or reads as a registry; false when it exists and cannot be read as one, which the dialog warns of and a write overwrites.</summary>
    public bool IsValid { get; }

    /// <summary>The categories the file's components use, sorted; the dialog's drop-down offers them with <see cref="DefaultCategory"/>.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Whether the file registers this component already.</summary>
    public bool IsRegistered => IndexOf(ClassName) >= 0;

    /// <summary>The file's component records, in order.</summary>
    private IEnumerable<JsonObject> Components => _file["components"] is JsonArray components ? components.OfType<JsonObject>() : [];

    /// <summary>
    /// The registry beside the component <paramref name="fullFileName"/>, read as the Model reads it: a file that
    /// cannot be parsed, or parses as something other than an object with a <c>components</c> array, leaves the
    /// model empty and <see cref="IsValid"/> false; a component record missing a required key is given the schema's default.
    /// </summary>
    public static DesignComponentMetadata Load(string fullFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullFileName);
        (string directory, string className, string componentName) = PackageParts(fullFileName);
        string path = Path.Combine(directory, ResourcesFolder, FileName);
        JsonObject file = new();
        bool valid = true;
        if (File.Exists(path))
        {
            try
            {
                JsonNode? read = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                if (read is JsonObject o && o.ContainsKey("components") && Normalized(o) is { } normalized)
                {
                    file = normalized;
                }
                else
                {
                    valid = false;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                valid = false;
            }
        }

        return new DesignComponentMetadata(fullFileName, directory, className, componentName, valid ? file : new JsonObject(), valid);
    }

    /// <summary>
    /// The component's root folder, its qualified class name and its short name from its class file's path
    /// (<c>Model.parseFilePath</c>): the folders named <c>+pkg</c> become the package prefix and the root is the
    /// folder above the first of them; an <c>@class</c> folder's root is the folder above it. R2025b takes any
    /// <c>+</c> or <c>@</c> anywhere in the path as such a folder, which misreads a folder named <c>C++</c>; JGraph
    /// reads only whole path segments that start with the mark.
    /// </summary>
    public static (string Directory, string ClassName, string ComponentName) PackageParts(string fullFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fullFileName);
        string full = Path.GetFullPath(fullFileName);
        string name = Path.GetFileNameWithoutExtension(full);
        string folder = Path.GetDirectoryName(full) ?? full;
        string[] parts = folder.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        int first = Array.FindIndex(parts, static p => p.Length > 1 && (p[0] == '+' || p[0] == '@'));
        if (first < 0)
        {
            return (folder, name, name);
        }

        string root = full[..IndexOfSegment(folder, parts, first)].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var packages = parts.Skip(first).Where(static p => p[0] == '+').Select(static p => p[1..]).ToList();
        string className = packages.Count == 0 ? name : string.Join('.', packages) + "." + name;
        return (root, className, name);
    }

    private static int IndexOfSegment(string folder, string[] parts, int index)
    {
        int at = 0;
        for (int i = 0; i < index; i++)
        {
            at = folder.IndexOf(parts[i], at, StringComparison.Ordinal) + parts[i].Length;
        }

        int segment = folder.IndexOf(parts[index], at, StringComparison.Ordinal);
        return segment < 0 ? folder.Length : segment;
    }

    /// <summary>
    /// The record the dialog starts from (<c>MetadataUIViewModel</c>): the registered record when there is one,
    /// else the defaults - the description from <paramref name="helpLine"/>, the class's first help line; JGraph's
    /// icon; <see cref="DefaultCategory"/>; no author; version <see cref="DefaultVersion"/>.
    /// </summary>
    public DesignComponentMetadataRecord InitialRecord(string? helpLine)
    {
        var record = new JsonObject { ["className"] = ClassName, ["componentName"] = ComponentName };
        int at = IndexOf(ClassName);
        if (at >= 0)
        {
            foreach (KeyValuePair<string, JsonNode?> member in Components.ElementAt(at))
            {
                record[member.Key] = member.Value?.DeepClone();
            }
        }
        else
        {
            record["description"] = (helpLine ?? string.Empty).Trim();
            record["icon"] = DefaultIcon;
            record["category"] = DefaultCategory;
            record["authorName"] = string.Empty;
            record["authorEmail"] = string.Empty;
            record["version"] = DefaultVersion;
        }

        return new DesignComponentMetadataRecord(record);
    }

    /// <summary>
    /// Writes <paramref name="record"/> into the file: appended when the component is not registered
    /// (<c>registerComponent</c>, a missing or unreadable file begun afresh), else merged over the registered record
    /// key by key, new keys last (<c>updateComponent</c>). The file's <c>schema</c> and <c>MATLABRelease</c> are set.
    /// </summary>
    /// <exception cref="IOException">The folder or file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file could not be written.</exception>
    public void Save(DesignComponentMetadataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        JsonArray components = ComponentsArray();
        int at = IndexOf(record.ClassName.Length > 0 ? record.ClassName : ClassName);
        if (at >= 0)
        {
            var merged = (JsonObject)components[at]!;
            foreach (KeyValuePair<string, JsonNode?> member in record.Node)
            {
                merged[member.Key] = member.Value?.DeepClone();
            }
        }
        else
        {
            components.Add(record.Node.DeepClone());
        }

        Write();
    }

    /// <summary>
    /// Takes the component's record out of the file (<c>deRegisterComponent</c>) and writes it again; nothing
    /// happens when the file does not register it. With no registry file at all R2025b fails on its own
    /// <c>isKey</c> (<c>MATLAB:UndefinedFunction</c>, measured); JGraph does nothing, there being nothing to remove.
    /// </summary>
    /// <returns>Whether a record was removed.</returns>
    public bool Deregister()
    {
        int at = IndexOf(ClassName);
        if (at < 0 || _file["components"] is not JsonArray components)
        {
            return false;
        }

        components.RemoveAt(at);
        Write();
        return true;
    }

    /// <summary>The file's text as it stands: R2025b's <c>jsonencode(..., 'PrettyPrint', true)</c> form.</summary>
    public string Text()
    {
        ComponentsArray();
        _file["schema"] = Schema;
        _file["MATLABRelease"] = Release;
        var text = new StringBuilder();
        Emit(text, _file, 0);
        return text.ToString();
    }

    /// <summary>Why an abstract class cannot be registered (<c>AbstractComponentErrorMsg</c>).</summary>
    public static string AbstractComponent(string className) =>
        $"The class {className} cannot be instantiated because it is abstract. Ensure you have implemented all abstract methods, including the setup and update methods inherited from {Superclass}.";

    /// <summary>Why a component whose public callbacks hold values cannot be registered (<c>CallbackHasValueErrorMsg</c>): the callbacks and their events, each list sorted and comma-joined.</summary>
    public static string CallbackHasValue(IEnumerable<string> callbacks, IEnumerable<string> events) =>
        $"A value has been assigned to the public callbacks: {string.Join(", ", callbacks)}.\n\nTo use the component in App Designer, the component must not assign a value to the public callbacks defined by the component. Instead, use the addlistener function to create event listeners for the events: {string.Join(", ", events)}.";

    /// <summary>Why <paramref name="name"/> cannot be the component's library name (blank), or null.</summary>
    public static string? WhyNotComponentName(string name) => string.IsNullOrWhiteSpace(name) ? ComponentNameEmpty : null;

    /// <summary>Why <paramref name="category"/> cannot be the component's category (blank), or null.</summary>
    public static string? WhyNotCategory(string category) => string.IsNullOrWhiteSpace(category) ? CategoryEmpty : null;

    /// <summary>Why <paramref name="version"/> cannot be the component's version (not <c>Major.Minor</c>), or null.</summary>
    public static string? WhyNotVersion(string version) => version is not null && VersionPattern().IsMatch(version) ? null : InvalidVersion;

    /// <summary>Why <paramref name="email"/> cannot be the author's email (not empty and not an address), or null.</summary>
    public static string? WhyNotEmail(string email) => string.IsNullOrEmpty(email) || EmailPattern().IsMatch(email) ? null : InvalidEmail;

    /// <summary>The path as the dialog's Component File row shows it: whole up to <see cref="MaxFilePathLength"/> characters, else its start and <c>...\name.ext</c> (<c>getFilePath</c>).</summary>
    public static string ShortenedPath(string fullFileName)
    {
        ArgumentNullException.ThrowIfNull(fullFileName);
        if (fullFileName.Length <= MaxFilePathLength)
        {
            return fullFileName;
        }

        string directory = Path.GetDirectoryName(fullFileName) ?? string.Empty;
        string tail = "..." + Path.DirectorySeparatorChar + Path.GetFileName(fullFileName);
        int keep = MaxFilePathLength - tail.Length;
        if (directory.Length >= keep)
        {
            directory = directory[..Math.Max(0, keep)];
        }

        return directory + tail;
    }

    /// <summary>
    /// The first line of a class file's help (<c>help(className)</c>'s first line, <c>getDefaultMetadata</c>): the first
    /// comment line after the <c>classdef</c> line, blank lines passed over, or the one above it; empty with none.
    /// </summary>
    public static string HelpLine(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int header = Array.FindIndex(lines, static l => l.TrimStart().StartsWith("classdef", StringComparison.Ordinal));
        if (header < 0)
        {
            return string.Empty;
        }

        for (int i = header + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            return trimmed.StartsWith('%') ? trimmed.TrimStart('%').Trim() : HelpAbove(lines, header);
        }

        return HelpAbove(lines, header);
    }

    /// <summary><see cref="HelpLine"/> of the class file <paramref name="fullFileName"/>, an <c>.m</c> or an <c>.mlapp</c>; empty when it cannot be read.</summary>
    public static string HelpLineOf(string fullFileName)
    {
        ArgumentNullException.ThrowIfNull(fullFileName);
        try
        {
            return HelpLine(JgsMlapp.ReadSource(fullFileName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string HelpAbove(string[] lines, int header)
    {
        for (int i = 0; i < header; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith('%'))
            {
                return trimmed.TrimStart('%').Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The direct superclasses a class file names (<c>classdef Name &lt; A &amp; B</c>, continuation lines joined); empty when
    /// the text is not a class, which <paramref name="isClass"/> says.
    /// </summary>
    public static IReadOnlyList<string> Superclasses(string source, out bool isClass)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int header = Array.FindIndex(lines, static l => ClassdefLine().IsMatch(l));
        isClass = header >= 0;
        if (!isClass)
        {
            return [];
        }

        var joined = new StringBuilder();
        for (int i = header; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf('%', StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            int dots = line.IndexOf("...", StringComparison.Ordinal);
            if (dots >= 0)
            {
                joined.Append(line[..dots]).Append(' ');
                continue;
            }

            joined.Append(line);
            break;
        }

        Match m = ClassdefHeader().Match(joined.ToString());
        if (!m.Success || !m.Groups["supers"].Success)
        {
            return [];
        }

        return m.Groups["supers"].Value.Split('&').Select(static s => s.Trim()).Where(static s => s.Length > 0).ToArray();
    }

    private int IndexOf(string className)
    {
        int i = 0;
        foreach (JsonObject component in Components)
        {
            if (Text(component, "className") == className)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private JsonArray ComponentsArray()
    {
        if (_file["components"] is JsonArray components)
        {
            return components;
        }

        var made = new JsonArray();
        _file["components"] = made;
        return made;
    }

    private void Write()
    {
        string folder = Path.Combine(Directory, ResourcesFolder);
        System.IO.Directory.CreateDirectory(folder);
        File.WriteAllText(MetadataFilePath, Text(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>The file with its components as an array of objects, each with the required keys (<c>fixMetadataStructure</c>, <c>validateComponents</c>); null when the components are not objects.</summary>
    private static JsonObject? Normalized(JsonObject file)
    {
        JsonNode? components = file["components"];
        JsonArray array;
        switch (components)
        {
            case JsonArray a:
                array = a;
                break;
            case JsonObject one:
                array = [one.DeepClone()];
                file["components"] = array;
                break;
            default:
                return null;
        }

        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject record)
            {
                return null;
            }

            foreach ((string key, JsonNode? value) in RequiredKeys)
            {
                if (!record.ContainsKey(key))
                {
                    record[key] = value?.DeepClone();
                }
            }
        }

        return file;
    }

    private static string Text(JsonObject record, string key) => record[key] is JsonValue v && v.TryGetValue(out string? s) ? s : string.Empty;

    /// <summary>R2025b's <c>jsonencode</c> with PrettyPrint over a node: two spaces an indent, <c>"key": value</c>, <c>[]</c> and <c>{}</c> for empties, no line break at the end.</summary>
    private static void Emit(StringBuilder text, JsonNode? node, int indent)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                return;
            case JsonArray { Count: 0 }:
                text.Append("[]");
                return;
            case JsonObject { Count: 0 }:
                text.Append("{}");
                return;
            case JsonArray a:
                text.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    text.Append(i > 0 ? "," : string.Empty).Append('\n').Append(' ', indent + 2);
                    Emit(text, a[i], indent + 2);
                }

                text.Append('\n').Append(' ', indent).Append(']');
                return;
            case JsonObject o:
                text.Append('{');
                int n = 0;
                foreach (KeyValuePair<string, JsonNode?> member in o)
                {
                    text.Append(n++ > 0 ? "," : string.Empty).Append('\n').Append(' ', indent + 2);
                    JgsJson.Quote(text, member.Key);
                    text.Append(": ");
                    Emit(text, member.Value, indent + 2);
                }

                text.Append('\n').Append(' ', indent).Append('}');
                return;
            case JsonValue v:
                if (v.TryGetValue(out string? s))
                {
                    JgsJson.Quote(text, s);
                }
                else if (v.TryGetValue(out bool b))
                {
                    text.Append(b ? "true" : "false");
                }
                else if (v.TryGetValue(out double d))
                {
                    text.Append(d == Math.Floor(d) && Math.Abs(d) < 1e15 ? d.ToString("F0", CultureInfo.InvariantCulture) : JgsJson.FormatDouble(d));
                }
                else if (v.TryGetValue(out int k))
                {
                    text.Append(k.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    text.Append(v.ToJsonString());
                }

                return;
            default:
                text.Append(node.ToJsonString());
                return;
        }
    }

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^.+@[a-zA-Z0-9-.]{1,64}\.[a-zA-Z]{2,64}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\s*classdef\b")]
    private static partial Regex ClassdefLine();

    [GeneratedRegex(@"^\s*classdef\s*(?:\([^)]*\)\s*)?(?<name>[A-Za-z]\w*(?:\.[A-Za-z]\w*)*)\s*(?:<\s*(?<supers>[^\n]*?))?\s*$")]
    private static partial Regex ClassdefHeader();
}

/// <summary>
/// One component's record in the registry: its keys in the order the file holds them, or the order the dialog
/// builds them in (<c>className</c>, <c>componentName</c>, <c>description</c>, <c>icon</c>, <c>category</c>,
/// <c>authorName</c>, <c>authorEmail</c>, <c>version</c>, then <c>avatar</c>, <c>avatarDark</c> and
/// <c>defaultPosition</c> as the check adds them). A set of an existing key keeps its place; a new key goes last.
/// </summary>
public sealed class DesignComponentMetadataRecord
{
    internal DesignComponentMetadataRecord(JsonObject node)
    {
        Node = node;
    }

    internal JsonObject Node { get; }

    /// <summary>The class, package-qualified.</summary>
    public string ClassName { get => Get("className"); set => Set("className", value); }

    /// <summary>The name the library shows.</summary>
    public string ComponentName { get => Get("componentName"); set => Set("componentName", value); }

    /// <summary>The tooltip's description; lines joined with a line feed.</summary>
    public string Description { get => Get("description"); set => Set("description", value); }

    /// <summary>The library icon as a data URI.</summary>
    public string Icon { get => Get("icon"); set => Set("icon", value); }

    /// <summary>The library section.</summary>
    public string Category { get => Get("category"); set => Set("category", value); }

    /// <summary>The author's name, or empty.</summary>
    public string AuthorName { get => Get("authorName"); set => Set("authorName", value); }

    /// <summary>The author's email, or empty.</summary>
    public string AuthorEmail { get => Get("authorEmail"); set => Set("authorEmail", value); }

    /// <summary>The version, <c>Major.Minor</c>.</summary>
    public string Version { get => Get("version"); set => Set("version", value); }

    /// <summary>The drag preview in the light theme as a data URI; JGraph writes none (an empty text).</summary>
    public string Avatar { get => Get("avatar"); set => Set("avatar", value); }

    /// <summary>The drag preview in the dark theme; JGraph writes none.</summary>
    public string AvatarDark { get => Get("avatarDark"); set => Set("avatarDark", value); }

    /// <summary>The position a new one is made with: the component's <c>Position</c> as the check read it.</summary>
    public IReadOnlyList<double> DefaultPosition
    {
        get => Node["defaultPosition"] is JsonArray a ? a.Select(static n => n is JsonValue v && v.TryGetValue(out double d) ? d : 0).ToArray() : [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Node["defaultPosition"] = new JsonArray(value.Select(static d => (JsonNode)JsonValue.Create(d)).ToArray());
        }
    }

    private string Get(string key) => Node[key] switch
    {
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out double d) => d.ToString("R", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Node[key] = value;
    }
}
