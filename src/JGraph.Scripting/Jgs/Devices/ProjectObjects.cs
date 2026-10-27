using System.Text;
using JGraph.Deploy;
using JGraph.Deploy.Projects;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// The handle objects a MATLAB project hands a script (compiler and SDK plan, stage K20), as R2025b
/// shows them (probe <c>k20_projects</c>): <c>matlab.project.Project</c> and its files, labels,
/// categories, path folders and shortcuts, the compiler task configurations
/// (<c>compiler.project.StandaloneAppConfigurationOptions</c> and its kin) and the enumerations
/// they carry. Each is a view over <see cref="MatlabProject"/>, which writes the definition files
/// on every change; a project's objects go stale when it closes, as R2025b's do
/// (<c>MATLAB:project:api:CurrentProjectMismatch</c>).
/// </summary>
internal static class ProjectDisplay
{
    /// <summary>R2025b's object display: the class name, then each property right-aligned to the longest, then a blank line.</summary>
    public static string Block(string shortName, IReadOnlyList<(string Name, string Value)> rows)
    {
        int width = rows.Count == 0 ? 0 : rows.Max(static r => r.Name.Length);
        var sb = new StringBuilder();
        sb.Append("  ").Append(shortName).Append(" with properties:\n\n");
        foreach ((string name, string value) in rows)
        {
            sb.Append("    ").Append(name.PadLeft(width)).Append(": ").Append(value).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>A value as R2025b's display shows it in a property line.</summary>
    public static string Shown(JgsValue value)
    {
        if (value.IsStringArray)
        {
            return value.ArrayLength == 1 ? "\"" + value.ElementAt(0).AsString + "\"" : $"[{value.Rows}×{value.Cols} string]";
        }

        if (value.Type == JgsType.String)
        {
            return "'" + value.AsString + "'";
        }

        if (value.IsCharMatrix && value.ArrayLength == 0)
        {
            return "''";
        }

        if (value.Type == JgsType.Bool)
        {
            return value.AsBool ? "1" : "0";
        }

        if (value.Type == JgsType.Number)
        {
            return value.Display();
        }

        if (value.Type == JgsType.Cell)
        {
            return $"{{{value.Rows}×{value.Cols} cell}}";
        }

        if (value.Type == JgsType.Struct)
        {
            return $"[{value.Rows}×{value.Cols} struct]";
        }

        if (value.AsExternalOrNull() is ProjectEnumValue member)
        {
            return member.Choice;
        }

        if (value.AsExternalOrNull() is DeviceArray array)
        {
            return $"[{array.Rows}×{array.Columns} {array.ClassName}]";
        }

        if (value.AsExternalOrNull() is DeviceObject single)
        {
            return $"[1×1 {single.ClassName}]";
        }

        return value.Display();
    }

    /// <summary>A cell column of char rows, 0-by-1 when empty, as the configuration classes hold file lists.</summary>
    public static JgsValue CellColumn(IReadOnlyList<string> items)
    {
        JgsValue cell = JgsValue.Cell(items.Select(JgsValue.Str).ToArray());
        cell.Reshape(items.Count, 1);
        return cell;
    }

    /// <summary>A string column, 0-by-1 when empty, as <c>findFiles</c> answers.</summary>
    public static JgsValue StringColumn(IReadOnlyList<string> items)
    {
        JgsValue column = JgsValue.StringArray(items.Select(JgsValue.Str).ToArray(), items.Count, 1);
        return column;
    }

    /// <summary>A string row, 1-by-0 when empty, as <c>StartupFiles</c> is held.</summary>
    public static JgsValue StringRow(IReadOnlyList<string> items) =>
        JgsValue.StringArray(items.Select(JgsValue.Str).ToArray(), 1, items.Count);

    /// <summary>An array of objects: the object itself for one, a 1-by-N device array otherwise (1-by-0 for none).</summary>
    public static JgsValue ArrayOf(DeviceClass @class, Interpreter interpreter, IReadOnlyList<DeviceObject> items) =>
        items.Count == 1 ? JgsValue.External(items[0]) : JgsValue.External(new DeviceArray(@class, interpreter, items, 1, items.Count));

    /// <summary>A refusal from the project model in R2025b's identifier and words.</summary>
    public static JgsRuntimeException Refusal(ProjectException ex, int line, int col) => new(line, col, ex.Identifier, ex.Message);
}

/// <summary>A member of one of the projects' enumerations: <c>deployment.compiler.model.MCRInstallType.web</c>, <c>matlab.project.DefinitionFiles.FixedPathMultiFile</c>.</summary>
internal sealed class ProjectEnumValue : DeviceObject
{
    public const string McrInstallType = "deployment.compiler.model.MCRInstallType";
    public const string ApiOptionType = "deployment.compiler.model.MatlabAPIOptionType";
    public const string DefinitionFiles = "matlab.project.DefinitionFiles";
    public const string SourceControlStatus = "matlab.project.SourceControlStatus";

    /// <summary>Every enumeration's members, in <c>enumeration</c>'s order.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Members = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [McrInstallType] = ["NONE", "web", "installer"],
        [ApiOptionType] = ["matlabdata", "mwarray"],
        [DefinitionFiles] = ["SingleFile", "FixedPathMultiFile", "MultiFile"],
        [SourceControlStatus] = ["NotUnderSourceControl", "Unmodified", "Modified", "Added", "Deleted", "Conflicted", "Ignored", "Missing", "Unknown"],
    };

    private static readonly Dictionary<string, DeviceClass> Classes = new(StringComparer.Ordinal);

    private ProjectEnumValue(DeviceSession session, Interpreter interpreter, string className, string member)
        : base(session, interpreter)
    {
        Choice = member;
        lock (Classes)
        {
            if (!Classes.TryGetValue(className, out DeviceClass? declared))
            {
                Classes[className] = declared = Declare(className);
            }

            Class = declared;
        }
    }

    public string Choice { get; }

    public override DeviceClass Class { get; }

    public override bool DeletesWhenUnheld => false;

    public static JgsValue Of(DeviceSession session, Interpreter interpreter, string className, string member) =>
        JgsValue.External(new ProjectEnumValue(session, interpreter, className, member));

    public bool Matches(JgsValue value) =>
        value.AsExternalOrNull() is ProjectEnumValue other ? other.ClassName == ClassName && other.Choice == Choice
        : DeviceChecks.IsText(value) && DeviceChecks.Text(value) == Choice;

    public override string? Summary() => Choice;

    protected override string ShortDisplay() => $"  {Class.ShortName} enumeration\n\n    {Choice}\n";

    protected override void OnDelete()
    {
    }

    private static DeviceClass Declare(string className)
    {
        static ProjectEnumValue Me(DeviceObject o) => (ProjectEnumValue)o;
        static JgsValue[] One(DeviceCall call, JgsValue value) =>
            call.Args.Count != 0 ? throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.") : [value];
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["char"] = static c => One(c, JgsValue.Str(Me(c.Target).Choice)),
            ["string"] = static c => One(c, JgsValue.StringScalar(Me(c.Target).Choice)),
            ["isequal"] = static c => [JgsValue.Bool(c.Args.All(a => Me(c.Target).Matches(a)))],
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && Me(c.Target).Matches(c.Args[0]))],
            ["ne"] = static c => [JgsValue.Bool(!(c.Args.Count == 1 && Me(c.Target).Matches(c.Args[0])))],
        };
        string shortName = className[(className.LastIndexOf('.') + 1)..];
        return new DeviceClass(className, shortName, [], [], methods, [], []);
    }
}

/// <summary>The session's one open project and what opening it changed: the folder before, the path folders added.</summary>
internal sealed class ProjectSession
{
    /// <summary>The open project, or null.</summary>
    public MatlabProject? Current { get; set; }

    /// <summary>The handle object the project opened with, or null.</summary>
    public ProjectObject? CurrentObject { get; set; }

    /// <summary>The session's half of the operations, set when the project opened.</summary>
    public ProjectOperations? Operations { get; set; }

    /// <summary>
    /// A fresh handle over the open project, as every <c>currentProject</c> call answers one in
    /// R2025b (two of them compare unequal), or null when none is open.
    /// </summary>
    public ProjectObject? NewHandle() =>
        Current is { } project && CurrentObject is { } first && Operations is { } operations
            ? new ProjectObject(first.Session, first.Interpreter, project, this, operations)
            : null;

    /// <summary>The folder the session was in before the project opened, to go back to on close.</summary>
    public string? FolderBefore { get; set; }

    /// <summary>The folders opening the project put on the search path, to take off on close.</summary>
    public List<string> AddedFolders { get; } = [];

    /// <summary>Whether <paramref name="project"/> is the open one: the project at that root (a reload re-reads it into a new instance, and the handles stay good).</summary>
    public bool IsLoaded(MatlabProject project) =>
        Current is not null && string.Equals(Current.RootFolder, project.RootFolder, StringComparison.OrdinalIgnoreCase);
}

/// <summary><c>matlab.project.Project</c>: the open project as a handle.</summary>
internal sealed class ProjectObject : DeviceObject
{
    /// <summary>The methods <c>methods(proj)</c> lists, as R2025b lists them.</summary>
    public static readonly string[] MethodListing =
    [
        "addFile", "addFolderIncludingChildFiles", "addLabel", "addPath", "addReference", "addShortcut", "addShutdownFile", "addStartupFile",
        "addlistener", "close", "createCategory", "delete", "eq", "export", "findCategory", "findFiles", "findobj", "findprop", "ge", "gt",
        "isLoaded", "isvalid", "le", "listAllProjectReferences", "listImpactedFiles", "listModifiedFiles", "listRequiredFiles", "listShutdownIssues",
        "listStartupIssues", "listener", "lt", "ne", "notify", "refreshSourceControl", "reload", "removeCategory", "removeFile", "removeLabel",
        "removePath", "removeReference", "removeShortcut", "removeShutdownFile", "removeStartupFile", "runChecks", "updateDependencies",
    ];

    private static readonly string[] DisplayOrder =
    [
        "Name", "Description", "RootFolder", "TopLevel", "ReadOnly", "DefinitionFilesType", "SourceControlIntegration", "RepositoryLocation",
        "Files", "Shortcuts", "Categories", "Dependencies", "StartupFiles", "ShutdownFiles", "ProjectPath", "ProjectReferences",
        "SimulinkCodeGenFolder", "ProjectStartupFolder", "DependencyCacheFile", "SimulinkCacheFolder",
    ];

    private static readonly DeviceClass Declaration = Declare();

    /// <summary>The class, for an empty array of projects.</summary>
    public static DeviceClass DeclaredClass => Declaration;

    public ProjectObject(DeviceSession session, Interpreter interpreter, MatlabProject project, ProjectSession projects, ProjectOperations operations)
        : base(session, interpreter)
    {
        Project = project;
        Projects = projects;
        Operations = operations;
    }

    public MatlabProject Project { get; }

    public ProjectSession Projects { get; }

    /// <summary>The session's half of the operations: the search path, the current folder, running a file, analysing files.</summary>
    public ProjectOperations Operations { get; }

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static ProjectObject Me(DeviceCall call) => (ProjectObject)call.Target;

    private static ProjectObject Me(DeviceObject o) => (ProjectObject)o;

    /// <summary>The open project, when this handle's is still it; R2025b's stale refusal otherwise.</summary>
    public MatlabProject Live(DeviceCall call)
    {
        if (!Projects.IsLoaded(Project))
        {
            throw call.Error("MATLAB:project:api:CurrentProjectMismatch", $"The project at \"{Project.RootFolder}\" is not currently loaded.");
        }

        return Projects.Current!;
    }

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, c) => JgsValue.StringScalar(Me(o).Live(c).Name), static (o, v, c) => Me(o).Live(c).Name = TextArgument(v, c, "Name")),
            new("Description", static (o, c) => JgsValue.StringScalar(Me(o).Live(c).Description), static (o, v, c) => Me(o).Live(c).Description = TextArgument(v, c, "Description")),
            new("RootFolder", static (o, c) => JgsValue.StringScalar(Me(o).Live(c).RootFolder)),
            new("TopLevel", static (o, c) => { Me(o).Live(c); return JgsValue.Bool(true); }),
            new("ReadOnly", static (o, c) => { Me(o).Live(c); return JgsValue.Bool(false); }),
            new("DefinitionFilesType", static (o, c) => { Me(o).Live(c); return ProjectEnumValue.Of(o.Session, o.Interpreter, ProjectEnumValue.DefinitionFiles, "FixedPathMultiFile"); }),
            new("SourceControlIntegration", static (o, c) => { Me(o).Live(c); return JgsValue.StringScalar(""); }),
            new("RepositoryLocation", static (o, c) => { Me(o).Live(c); return JgsValue.StringScalar(""); }),
            new("Files", static (o, c) => Me(o).FilesValue(c, Me(o).Live(c).Files.Select(static f => f.RelativePath).ToList())),
            new("Shortcuts", static (o, c) => Me(o).ShortcutsValue(c)),
            new("Categories", static (o, c) => Me(o).CategoriesValue(c)),
            new("Dependencies", static (o, c) => Me(o).DependenciesValue(c)),
            new("StartupFiles", static (o, c) => ProjectDisplay.StringRow(Me(o).Live(c).StartupFiles.Select(Me(o).Project.FullPath).ToList())),
            new("ShutdownFiles", static (o, c) => ProjectDisplay.StringRow(Me(o).Live(c).ShutdownFiles.Select(Me(o).Project.FullPath).ToList())),
            new("ProjectPath", static (o, c) => Me(o).PathValue(c)),
            new("ProjectReferences", static (o, c) => { Me(o).Live(c); return JgsValue.External(new DeviceArray(ProjectReferenceClass, o.Interpreter, [], 1, 0)); }),
            new("SimulinkCodeGenFolder", static (o, c) => { Me(o).Live(c); return JgsValue.StringScalar(""); }),
            new("ProjectStartupFolder", static (o, c) => JgsValue.StringScalar(Me(o).Live(c).RootFolder)),
            new("DependencyCacheFile", static (o, c) => { Me(o).Live(c); return JgsValue.StringScalar(""); }),
            new("SimulinkCacheFolder", static (o, c) => { Me(o).Live(c); return JgsValue.StringScalar(""); }),
        };
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["addFile"] = static c => [Me(c).AddFile(c)],
            ["addFolderIncludingChildFiles"] = static c => [Me(c).AddFolderIncludingChildFiles(c)],
            ["addLabel"] = static c => [Me(c).AddLabel(c)],
            ["addPath"] = static c => [Me(c).AddPath(c)],
            ["addShortcut"] = static c => [Me(c).AddShortcut(c)],
            ["addShutdownFile"] = static c => [Me(c).AddRunFile(c, shutdown: true)],
            ["addStartupFile"] = static c => [Me(c).AddRunFile(c, shutdown: false)],
            ["close"] = static c => { Me(c).Close(c); return []; },
            ["createCategory"] = static c => [Me(c).CreateCategory(c)],
            ["findCategory"] = static c => [Me(c).FindCategory(c)],
            ["findFiles"] = static c => [Me(c).FindFiles(c)],
            ["isLoaded"] = static c => [JgsValue.Bool(Me(c).Projects.IsLoaded(Me(c).Project))],
            ["listImpactedFiles"] = static c => [Me(c).ListImpactedFiles(c)],
            ["listModifiedFiles"] = static c => { Me(c).Live(c); return [JgsValue.External(new DeviceArray(ProjectFileObject.Declaration, c.Interpreter, [], 1, 0))]; },
            ["listRequiredFiles"] = static c => [Me(c).ListRequiredFiles(c)],
            ["listShutdownIssues"] = static c => { Me(c).Live(c); return [JgsValue.External(new DeviceArray(IssueClass, c.Interpreter, [], 1, 0))]; },
            ["listStartupIssues"] = static c => { Me(c).Live(c); return [JgsValue.External(new DeviceArray(IssueClass, c.Interpreter, [], 1, 0))]; },
            ["reload"] = static c => { Me(c).Reload(c); return []; },
            ["removeCategory"] = static c => { Me(c).RemoveCategory(c); return []; },
            ["removeFile"] = static c => { Me(c).RemoveFile(c); return []; },
            ["removeLabel"] = static c => { Me(c).RemoveLabel(c); return []; },
            ["removePath"] = static c => { Me(c).RemovePath(c); return []; },
            ["removeShortcut"] = static c => { Me(c).RemoveShortcut(c); return []; },
            ["removeShutdownFile"] = static c => { Me(c).RemoveRunFile(c, shutdown: true); return []; },
            ["removeStartupFile"] = static c => { Me(c).RemoveRunFile(c, shutdown: false); return []; },
            ["updateDependencies"] = static c => { Me(c).Live(c); return []; },
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && ReferenceEquals(c.Args[0].AsExternalOrNull(), c.Target))],
            ["ne"] = static c => [JgsValue.Bool(!(c.Args.Count == 1 && ReferenceEquals(c.Args[0].AsExternalOrNull(), c.Target)))],
            ["isvalid"] = static c => [JgsValue.Bool(!c.Target.Deleted)],
        };
        foreach (string later in (string[])["addReference", "export", "listAllProjectReferences", "refreshSourceControl", "removeReference", "runChecks"])
        {
            methods[later] = c => throw c.Error("JGraph:projects:notInJGraph",
                $"{later} is not in JGraph: project references, export, source control and the project checks are outside the compiler and SDK plan's stage K20 (open item 217).");
        }

        return new DeviceClass("matlab.project.Project", "Project", ["matlab.mixin.CustomDisplay", "dynamicprops", "handle"], properties, methods, MethodListing, DisplayOrder)
        {
            NoGetSet = true,
        };
    }

    /// <summary><c>matlab.project.ProjectReference</c>, which JGraph never fills (open item 217).</summary>
    public static readonly DeviceClass ProjectReferenceClass = new("matlab.project.ProjectReference", "ProjectReference", ["handle"], [], new Dictionary<string, DeviceMethodBody>(), [], []);

    /// <summary><c>matlab.project.Issue</c>, which a startup without trouble never fills.</summary>
    public static readonly DeviceClass IssueClass = new("matlab.project.Issue", "Issue", ["handle"], [], new Dictionary<string, DeviceMethodBody>(), [], []);

    protected override string ShortDisplay()
    {
        if (!Projects.IsLoaded(Project))
        {
            return $"  Project with properties:\n\n    (the project at \"{Project.RootFolder}\" is not currently loaded)\n";
        }

        var rows = new List<(string, string)>();
        var call = new DeviceCall { Target = this, Args = [] };
        foreach (string name in DisplayOrder)
        {
            rows.Add((name, ProjectDisplay.Shown(Class.Find(name)!.Get(this, call))));
        }

        return ProjectDisplay.Block("Project", rows);
    }

    protected override void OnDelete()
    {
    }

    // --- arguments ----------------------------------------------------------------------------------

    internal static string TextArgument(JgsValue value, DeviceCall call, string what)
    {
        if (!DeviceChecks.IsText(value))
        {
            throw call.Error("MATLAB:invalidType", $"Expected {what} to be one of these types:\n\nchar, string\n\nInstead its type was {DeviceChecks.ClassOf(value)}.");
        }

        return DeviceChecks.Text(value);
    }

    /// <summary>A file argument: a char row, a string scalar, a cell of them, a string array or ProjectFile objects, as paths.</summary>
    internal static List<string> FileArguments(JgsValue value, DeviceCall call)
    {
        var files = new List<string>();
        if (value.AsExternalOrNull() is ProjectFileObject one)
        {
            files.Add(one.FullPath);
        }
        else if (value.AsExternalOrNull() is DeviceArray array && array.Class == ProjectFileObject.Declaration)
        {
            files.AddRange(array.Items.Cast<ProjectFileObject>().Select(static f => f.FullPath));
        }
        else if (value.Type == JgsType.String || (value.IsCharMatrix && value.Rows <= 1))
        {
            files.Add(DeviceChecks.Text(value));
        }
        else if (value.IsStringArray)
        {
            for (int i = 0; i < value.ArrayLength; i++)
            {
                files.Add(value.ElementAt(i).AsString);
            }
        }
        else if (value.Type == JgsType.Cell)
        {
            foreach (JgsValue item in value.AsCell)
            {
                if (!DeviceChecks.IsText(item))
                {
                    throw call.Error("MATLAB:invalidType", $"Expected file to be one of these types:\n\nchar, string, cell\n\nInstead its type was {DeviceChecks.ClassOf(value)}.");
                }

                files.Add(DeviceChecks.Text(item));
            }
        }
        else
        {
            throw call.Error("MATLAB:invalidType", $"Expected file to be one of these types:\n\nchar, string, cell\n\nInstead its type was {DeviceChecks.ClassOf(value)}.");
        }

        return files;
    }

    private static JgsValue Arg(DeviceCall call, int index, string method)
    {
        if (call.Args.Count <= index)
        {
            throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        }

        _ = method;
        return call.Args[index];
    }

    private static T Guard<T>(DeviceCall call, Func<T> body)
    {
        try
        {
            return body();
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, call.Line, call.Column);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw call.Error("MATLAB:project:api:FileNotWritable", $"Unable to write file {ProjectOf(call).RootFolder}. Check write permission. {ex.Message}");
        }
    }

    private static MatlabProject ProjectOf(DeviceCall call) => Me(call).Project;

    /// <summary>A path under the root, from an argument that may be relative to the current folder or the root.</summary>
    private string? Relative(string location, out string full)
    {
        full = Path.IsPathRooted(location) ? Path.GetFullPath(location) : Path.GetFullPath(Path.Combine(Operations.CurrentFolder, location));
        string? relative = Project.RelativePath(full);
        if (relative is null && !Path.IsPathRooted(location))
        {
            // R2025b takes a relative path under the root too (files.addFile.relative).
            string underRoot = Path.GetFullPath(Path.Combine(Project.RootFolder, location));
            if (File.Exists(underRoot) || Directory.Exists(underRoot))
            {
                full = underRoot;
                relative = Project.RelativePath(full);
            }
        }

        return relative;
    }

    // --- files --------------------------------------------------------------------------------------

    private JgsValue FilesValue(DeviceCall call, IReadOnlyList<string> relativePaths)
    {
        var items = relativePaths.Select(r => (DeviceObject)new ProjectFileObject(Session, Interpreter, this, r)).ToList();
        return ProjectDisplay.ArrayOf(ProjectFileObject.Declaration, call.Interpreter, items);
    }

    private JgsValue AddFile(DeviceCall call)
    {
        MatlabProject project = Live(call);
        List<string> files = FileArguments(Arg(call, 0, "addFile"), call);
        var added = new List<string>();
        foreach (string file in files)
        {
            string? relative = Relative(file, out string full);
            if (relative is null)
            {
                throw call.Error("MATLAB:project:management:FileNotInProjectRoot", $"The file {full} is not a child of the project root folder:\n\t{project.RootFolder}.");
            }

            if (relative.Length == 0)
            {
                throw call.Error("MATLAB:project:management:RootFolderNotAddable", $"Unable to add the project root folder to the project:\n{project.RootFolder}");
            }

            if (MatlabProject.IsDefinitionPath(relative))
            {
                throw call.Error("MATLAB:project:management:FileIsMetadataError", $"Unable to perform action on \"{Path.Combine(project.RootFolder, "resources", "project")}\" because it is a project definition file.");
            }

            added.Add(Guard(call, () => project.AddFile(relative)).RelativePath);
        }

        return FilesValue(call, added);
    }

    private JgsValue AddFolderIncludingChildFiles(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string folder = TextArgument(Arg(call, 0, "addFolderIncludingChildFiles"), call, "folder");
        string? relative = Relative(folder, out string full);
        if (relative is null)
        {
            throw call.Error("MATLAB:project:management:FileNotInProjectRoot", $"The file {full} is not a child of the project root folder:\n\t{project.RootFolder}.");
        }

        if (!Directory.Exists(full))
        {
            throw call.Error("MATLAB:project:api:FolderDoesNotExist", $"The folder \"{full}\" does not exist.");
        }

        IReadOnlyList<ProjectFileEntry> added = Guard(call, () => project.AddFolderIncludingChildFiles(relative));
        return FilesValue(call, [added[0].RelativePath]);
    }

    private void RemoveFile(DeviceCall call)
    {
        MatlabProject project = Live(call);
        foreach (string file in FileArguments(Arg(call, 0, "removeFile"), call))
        {
            string? relative = Relative(file, out _);
            if (relative is null)
            {
                continue; // outside the root: nothing to remove, and R2025b says nothing
            }

            Guard(call, () => { project.RemoveFile(relative); return 0; });
        }
    }

    private JgsValue FindFiles(DeviceCall call)
    {
        MatlabProject project = Live(call);
        List<string>? wanted = null;
        string? category = null;
        string? label = null;
        bool asObjects = false;
        int i = 0;
        if (call.Args.Count > 0 && !(DeviceChecks.IsText(call.Args[0]) && IsFindFilesOption(DeviceChecks.Text(call.Args[0]))))
        {
            wanted = FileArguments(call.Args[0], call);
            i = 1;
        }

        for (; i + 1 < call.Args.Count; i += 2)
        {
            string option = DeviceChecks.Text(call.Args[i]);
            JgsValue value = call.Args[i + 1];
            switch (option)
            {
                case "Category":
                    category = value.AsExternalOrNull() is CategoryObject c ? c.Name : DeviceChecks.Text(value);
                    break;
                case "Label":
                    label = value.AsExternalOrNull() is LabelDefinitionObject d ? d.Name : DeviceChecks.Text(value);
                    break;
                case "IncludeReferences":
                    break;
                case "OutputFormat":
                    asObjects = DeviceChecks.Text(value).Equals("ProjectFile", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    throw call.Error("MATLAB:InputParser:UnmatchedParameter", $"'{option}' is not a recognized parameter.");
            }
        }

        IEnumerable<ProjectFileEntry> entries = project.Files;
        if (wanted is not null)
        {
            var set = new HashSet<string>(wanted.Select(f => Relative(f, out _) ?? "\0"), StringComparer.OrdinalIgnoreCase);
            entries = entries.Where(e => set.Contains(e.RelativePath));
        }

        if (label is not null || category is not null)
        {
            ProjectCategory? categoryFound = category is null ? null : project.FindCategory(category);
            if (category is not null && categoryFound is null)
            {
                throw call.Error("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{category}\" does not exist in the current project.");
            }

            if (label is not null)
            {
                List<ProjectCategory> holding = (categoryFound is not null ? [categoryFound] : project.Categories.ToList()).Where(c => c.Labels.Any(l => l.Name == label)).ToList();
                if (holding.Count == 0)
                {
                    throw call.Error("MATLAB:project:api:LabelDoesNotExistInProject", $"The label \"{label}\" does not exist in the project. Use an existing label.");
                }

                if (holding.Count > 1)
                {
                    throw call.Error("MATLAB:project:api:LabelExistsInMultipleCategories", $"The label \"{label}\" exists in more than 1 category in project {project.Name}. Specify a category.");
                }

                entries = entries.Where(e => project.LabelsOf(e.RelativePath).Any(l => l.CategoryName == holding[0].Name && l.LabelName == label));
            }
            else
            {
                entries = entries.Where(e => project.LabelsOf(e.RelativePath).Any(l => l.CategoryName == category));
            }
        }

        List<string> paths = entries.Select(static e => e.RelativePath).ToList();
        return asObjects ? FilesValue(call, paths) : ProjectDisplay.StringColumn(paths.Select(project.FullPath).ToList());
    }

    private static bool IsFindFilesOption(string text) => text is "Category" or "Label" or "IncludeReferences" or "OutputFormat";

    private JgsValue ListRequiredFiles(DeviceCall call)
    {
        MatlabProject project = Live(call);
        List<string> roots = FileArguments(Arg(call, 0, "listRequiredFiles"), call)
            .Select(f => { Relative(f, out string full); return full; })
            .Where(f => project.RelativePath(f) is { Length: > 0 } && File.Exists(f))
            .ToList();
        if (roots.Count == 0)
        {
            return ProjectDisplay.StringColumn([]);
        }

        DependencyReport report = Operations.Analyze(roots);
        var required = report.Files.Select(static f => f.Path).Where(f => project.RelativePath(f) is { Length: > 0 }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return ProjectDisplay.StringColumn(required);
    }

    private JgsValue ListImpactedFiles(DeviceCall call)
    {
        MatlabProject project = Live(call);
        var wanted = new HashSet<string>(FileArguments(Arg(call, 0, "listImpactedFiles"), call).Select(f => { Relative(f, out string full); return full; }), StringComparer.OrdinalIgnoreCase);
        var impacted = new List<string>();
        foreach (ProjectFileEntry entry in project.Files.Where(static e => !e.IsFolder && JgsFunctionPath.CodeExtensions.Contains(Path.GetExtension(e.RelativePath), StringComparer.OrdinalIgnoreCase)))
        {
            string full = project.FullPath(entry.RelativePath);
            if (wanted.Contains(full) || Operations.Analyze([full]).Files.Any(f => wanted.Contains(f.Path)))
            {
                impacted.Add(full);
            }
        }

        return ProjectDisplay.StringColumn(impacted);
    }

    private JgsValue DependenciesValue(DeviceCall call)
    {
        MatlabProject project = Live(call);
        var nodes = new List<string>();
        var edges = new List<(string, string)>();
        foreach (ProjectFileEntry entry in project.Files.Where(static e => !e.IsFolder && JgsFunctionPath.CodeExtensions.Contains(Path.GetExtension(e.RelativePath), StringComparer.OrdinalIgnoreCase)))
        {
            string full = project.FullPath(entry.RelativePath);
            nodes.Add(full);
            foreach (DependencyFile required in Operations.Analyze([full]).Files)
            {
                if (!string.Equals(required.Path, full, StringComparison.OrdinalIgnoreCase) && project.RelativePath(required.Path) is { Length: > 0 })
                {
                    edges.Add((full, required.Path));
                }
            }
        }

        JgsValue edgeCell = JgsValue.Cell(edges.SelectMany(e => new[] { JgsValue.Str(e.Item1), JgsValue.Str(e.Item2) }).ToArray());
        edgeCell.Reshape(edges.Count, 2);
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Nodes"] = ProjectDisplay.CellColumn(nodes),
            ["Edges"] = edgeCell,
        });
    }

    // --- labels and categories ---------------------------------------------------------------------

    private JgsValue CategoriesValue(DeviceCall call)
    {
        MatlabProject project = Live(call);
        var items = project.Categories.Select(c => (DeviceObject)new CategoryObject(Session, Interpreter, this, c.Name)).ToList();
        return ProjectDisplay.ArrayOf(CategoryObject.Declaration, call.Interpreter, items);
    }

    private JgsValue CreateCategory(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string name = TextArgument(Arg(call, 0, "createCategory"), call, "categoryName");
        string dataType = "none";
        bool single = false;
        for (int i = 1; i < call.Args.Count; i++)
        {
            string word = DeviceChecks.Text(call.Args[i]);
            if (word.Equals("single-valued", StringComparison.OrdinalIgnoreCase))
            {
                single = true;
                continue;
            }

            string[] types = ["none", "char", "double", "integer", "logical"];
            string? matched = types.FirstOrDefault(t => t.Equals(word, StringComparison.OrdinalIgnoreCase));
            dataType = matched ?? throw call.Error("MATLAB:unrecognizedStringChoice",
                $"Expected dataType to match one of these values:\n\n'none', 'char', 'double', 'integer', 'logical'\n\nThe input, '{word}', did not match any of the valid values.");
        }

        ProjectCategory made = Guard(call, () => project.CreateCategory(name, dataType, single));
        return JgsValue.External(new CategoryObject(Session, Interpreter, this, made.Name));
    }

    private JgsValue FindCategory(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string name = TextArgument(Arg(call, 0, "findCategory"), call, "categoryName");
        return project.FindCategory(name) is { } found
            ? JgsValue.External(new CategoryObject(Session, Interpreter, this, found.Name))
            : JgsValue.External(new DeviceArray(CategoryObject.Declaration, call.Interpreter, [], 1, 0));
    }

    private void RemoveCategory(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string name = call.Args.Count > 0 && call.Args[0].AsExternalOrNull() is CategoryObject c ? c.Name : TextArgument(Arg(call, 0, "removeCategory"), call, "categoryName");
        Guard(call, () => { project.RemoveCategory(name); return 0; });
    }

    /// <summary>
    /// <c>addLabel(proj, files, categoryName, labelName[, data])</c>, <c>addLabel(proj, files, labelDefinition[, data])</c>,
    /// <c>addLabel(proj, files, labelName)</c> when the name is in one category.
    /// </summary>
    private JgsValue AddLabel(DeviceCall call)
    {
        MatlabProject project = Live(call);
        List<string> files = FileArguments(Arg(call, 0, "addLabel"), call);
        JgsValue second = Arg(call, 1, "addLabel");
        string category;
        string label;
        JgsValue? data = null;
        if (second.AsExternalOrNull() is LabelDefinitionObject definition)
        {
            category = definition.CategoryName;
            label = definition.Name;
            data = call.Args.Count > 2 ? call.Args[2] : null;
        }
        else if (call.Args.Count >= 3 && (second.AsExternalOrNull() is CategoryObject || DeviceChecks.IsText(call.Args[2])))
        {
            category = second.AsExternalOrNull() is CategoryObject co ? co.Name : DeviceChecks.Text(second);
            label = DeviceChecks.Text(call.Args[2]);
            data = call.Args.Count > 3 ? call.Args[3] : null;
        }
        else
        {
            label = DeviceChecks.Text(second);
            List<ProjectCategory> holding = project.Categories.Where(c => c.Labels.Any(l => l.Name == label)).ToList();
            if (holding.Count == 0)
            {
                throw call.Error("MATLAB:project:api:LabelDoesNotExistInProject", $"The label \"{label}\" does not exist in the project. Use an existing label.");
            }

            if (holding.Count > 1)
            {
                throw call.Error("MATLAB:project:api:LabelExistsInMultipleCategories", $"The label \"{label}\" exists in more than 1 category in project {project.Name}. Specify a category.");
            }

            category = holding[0].Name;
            data = call.Args.Count > 2 ? call.Args[2] : null;
        }

        ProjectCategory found = project.FindCategory(category)
            ?? throw call.Error("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{category}\" does not exist in the current project.");
        var results = new List<DeviceObject>();
        foreach (string file in files)
        {
            string? relative = Relative(file, out string full);
            if (relative is null || relative.Length == 0)
            {
                throw call.Error("MATLAB:project:management:FileNotInProjectRoot", $"The file {full} is not a child of the project root folder:\n\t{project.RootFolder}.");
            }

            if (project.FindFile(relative) is null)
            {
                throw call.Error("MATLAB:project:api:FileIsNotInProject", $"The file or folder \"{full}\" is not in the current project.");
            }

            string? text = data is null ? null : LabelData(found, data, label, full, call);
            Guard(call, () => project.AddLabel(relative, category, label, text));
            results.Add(new ProjectLabelObject(Session, Interpreter, this, relative, category, label));
        }

        return ProjectDisplay.ArrayOf(ProjectLabelObject.Declaration, call.Interpreter, results);
    }

    /// <summary>A label's data as text for the file, checked against the category's type.</summary>
    private static string? LabelData(ProjectCategory category, JgsValue data, string label, string file, DeviceCall call)
    {
        if (category.DataType == "none")
        {
            return null;
        }

        bool ok = category.DataType switch
        {
            "char" => DeviceChecks.IsText(data),
            "logical" => data.Type == JgsType.Bool,
            "double" => data.Type == JgsType.Number,
            "integer" => data.Type == JgsType.Number && Math.Floor(data.AsNumber) == data.AsNumber,
            _ => false,
        };
        if (!ok)
        {
            throw call.Error("MATLAB:project:api:AddFileLabelInvalidData", $"Unable to set data for label \"{label}\" for files:\n{file}");
        }

        return category.DataType switch
        {
            "char" => DeviceChecks.Text(data),
            "logical" => data.AsBool ? "true" : "false",
            _ => data.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private void RemoveLabel(DeviceCall call)
    {
        MatlabProject project = Live(call);
        List<string> files = FileArguments(Arg(call, 0, "removeLabel"), call);
        JgsValue second = Arg(call, 1, "removeLabel");
        string? category;
        string label;
        if (second.AsExternalOrNull() is LabelDefinitionObject definition)
        {
            category = definition.CategoryName;
            label = definition.Name;
        }
        else if (call.Args.Count >= 3)
        {
            category = second.AsExternalOrNull() is CategoryObject co ? co.Name : DeviceChecks.Text(second);
            label = DeviceChecks.Text(call.Args[2]);
        }
        else
        {
            label = DeviceChecks.Text(second);
            category = project.Categories.FirstOrDefault(c => c.Labels.Any(l => l.Name == label))?.Name;
        }

        if (category is null)
        {
            return;
        }

        foreach (string file in files)
        {
            if (Relative(file, out _) is { Length: > 0 } relative)
            {
                Guard(call, () => { project.RemoveLabel(relative, category, label); return 0; });
            }
        }
    }

    // --- the path -----------------------------------------------------------------------------------

    private JgsValue PathValue(DeviceCall call)
    {
        MatlabProject project = Live(call);
        var items = project.PathFolders.Select(f => (DeviceObject)new PathFolderObject(Session, Interpreter, this, f)).ToList();
        return ProjectDisplay.ArrayOf(PathFolderObject.Declaration, call.Interpreter, items);
    }

    private JgsValue AddPath(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string folder = TextArgument(Arg(call, 0, "addPath"), call, "folder");
        string? relative = Relative(folder, out string full);
        if (relative is null)
        {
            throw call.Error("MATLAB:project:api:PathNotInProject", $"The location \"{full}\" is not a folder in the project root folder.");
        }

        string added = Guard(call, () => project.AddPath(full));
        Operations.AddToSearchPath(full);
        return JgsValue.External(new PathFolderObject(Session, Interpreter, this, added));
    }

    private void RemovePath(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string folder = TextArgument(Arg(call, 0, "removePath"), call, "folder");
        string? relative = Relative(folder, out string full);
        if (relative is null)
        {
            return;
        }

        Guard(call, () => { project.RemovePath(full); return 0; });
        Operations.RemoveFromSearchPath(full);
    }

    // --- entry points --------------------------------------------------------------------------------

    private JgsValue ShortcutsValue(DeviceCall call)
    {
        MatlabProject project = Live(call);
        var items = project.Shortcuts.Select(s => (DeviceObject)new ShortcutObject(Session, Interpreter, this, s.RelativePath)).ToList();
        return ProjectDisplay.ArrayOf(ShortcutObject.Declaration, call.Interpreter, items);
    }

    private JgsValue AddRunFile(DeviceCall call, bool shutdown)
    {
        MatlabProject project = Live(call);
        string file = TextArgument(Arg(call, 0, shutdown ? "addShutdownFile" : "addStartupFile"), call, "file");
        Relative(file, out string full);
        string added = Guard(call, () => shutdown ? project.AddShutdownFile(full) : project.AddStartupFile(full));
        return JgsValue.StringScalar(added);
    }

    private void RemoveRunFile(DeviceCall call, bool shutdown)
    {
        MatlabProject project = Live(call);
        string file = TextArgument(Arg(call, 0, shutdown ? "removeShutdownFile" : "removeStartupFile"), call, "file");
        Relative(file, out string full);
        Guard(call, () => { if (shutdown) project.RemoveShutdownFile(full); else project.RemoveStartupFile(full); return 0; });
    }

    private JgsValue AddShortcut(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string file = TextArgument(Arg(call, 0, "addShortcut"), call, "file");
        Relative(file, out string full);
        ProjectEntryPoint added = Guard(call, () => project.AddShortcut(full));
        return JgsValue.External(new ShortcutObject(Session, Interpreter, this, added.RelativePath));
    }

    private void RemoveShortcut(DeviceCall call)
    {
        MatlabProject project = Live(call);
        string file = TextArgument(Arg(call, 0, "removeShortcut"), call, "file");
        Relative(file, out string full);
        Guard(call, () => { project.RemoveShortcut(full); return 0; });
    }

    // --- open and close ---------------------------------------------------------------------------------

    private void Close(DeviceCall call)
    {
        Live(call);
        Operations.CloseCurrent(call.Line, call.Column);
    }

    private void Reload(DeviceCall call)
    {
        Live(call);
        Operations.Reopen(Project.RootFolder, call.Line, call.Column);
    }
}

/// <summary><c>matlab.project.ProjectFile</c>: one file or folder of the open project.</summary>
internal sealed class ProjectFileObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.ProjectFile", "ProjectFile", ["handle"],
        [
            new("Path", static (o, c) => JgsValue.StringScalar(Me(o).FullPath)),
            new("Revision", static (o, c) => { Me(o).Owner.Live(c); return JgsValue.StringScalar(""); }),
            new("SourceControlStatus", static (o, c) => { Me(o).Owner.Live(c); return ProjectEnumValue.Of(o.Session, o.Interpreter, ProjectEnumValue.SourceControlStatus, "NotUnderSourceControl"); }),
            new("Labels", static (o, c) => Me(o).LabelsValue(c)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["addLabel"] = static c => [Me(c.Target).AddLabel(c)],
            ["removeLabel"] = static c => { Me(c.Target).RemoveLabel(c); return []; },
            ["findLabel"] = static c => [Me(c.Target).FindLabel(c)],
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && c.Args[0].AsExternalOrNull() is ProjectFileObject other && string.Equals(other.FullPath, Me(c.Target).FullPath, StringComparison.OrdinalIgnoreCase))],
        },
        ["addLabel", "addlistener", "delete", "eq", "findLabel", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "removeLabel"],
        ["Path", "Revision", "SourceControlStatus", "Labels"])
    {
        NoGetSet = true,
    };

    public ProjectFileObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, string relativePath)
        : base(session, interpreter)
    {
        Owner = owner;
        RelativePath = relativePath;
    }

    public ProjectObject Owner { get; }

    public string RelativePath { get; }

    public string FullPath => Owner.Project.FullPath(RelativePath);

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static ProjectFileObject Me(DeviceObject o) => (ProjectFileObject)o;

    private JgsValue LabelsValue(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        var items = project.LabelsOf(RelativePath).Select(l => (DeviceObject)new ProjectLabelObject(Session, Interpreter, Owner, RelativePath, l.CategoryName, l.LabelName)).ToList();
        return ProjectDisplay.ArrayOf(ProjectLabelObject.Declaration, call.Interpreter, items);
    }

    private JgsValue AddLabel(DeviceCall call)
    {
        var forwarded = new DeviceCall { Target = Owner, Args = [JgsValue.External(this), .. call.Args], Line = call.Line, Column = call.Column, Wanted = call.Wanted };
        return Owner.Class.Methods["addLabel"](forwarded)[0];
    }

    private void RemoveLabel(DeviceCall call)
    {
        var forwarded = new DeviceCall { Target = Owner, Args = [JgsValue.External(this), .. call.Args], Line = call.Line, Column = call.Column };
        Owner.Class.Methods["removeLabel"](forwarded);
    }

    private JgsValue FindLabel(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        string? category = call.Args.Count > 1 ? DeviceChecks.Text(call.Args[0]) : null;
        string name = DeviceChecks.Text(call.Args[call.Args.Count > 1 ? 1 : 0]);
        ProjectFileLabel? found = project.LabelsOf(RelativePath).Cast<ProjectFileLabel?>().FirstOrDefault(l => l!.Value.LabelName == name && (category is null || l.Value.CategoryName == category));
        return found is { } label
            ? JgsValue.External(new ProjectLabelObject(Session, Interpreter, Owner, RelativePath, label.CategoryName, label.LabelName))
            : JgsValue.External(new DeviceArray(ProjectLabelObject.Declaration, call.Interpreter, [], 1, 0));
    }

    protected override string ShortDisplay()
    {
        var call = new DeviceCall { Target = this, Args = [] };
        return ProjectDisplay.Block("ProjectFile",
        [
            ("Path", ProjectDisplay.Shown(JgsValue.StringScalar(FullPath))),
            ("Revision", "\"\""),
            ("SourceControlStatus", "NotUnderSourceControl"),
            ("Labels", ProjectDisplay.Shown(LabelsValue(call))),
        ]);
    }

    protected override void OnDelete()
    {
    }
}

/// <summary><c>matlab.project.Label</c>: a label on a file.</summary>
internal sealed class ProjectLabelObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.Label", "Label", ["matlab.project.LabelDefinition", "handle"],
        [
            new("Name", static (o, c) => JgsValue.StringScalar(Me(o).Name)),
            new("CategoryName", static (o, c) => JgsValue.StringScalar(Me(o).CategoryName)),
            new("DataType", static (o, c) => JgsValue.StringScalar(Me(o).DataType(c))),
            new("Data", static (o, c) => Me(o).Data(c), static (o, v, c) => Me(o).SetData(v, c)),
            new("File", static (o, c) => JgsValue.StringScalar(Me(o).Owner.Project.FullPath(Me(o).RelativePath))),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"],
        ["Name", "CategoryName", "DataType", "Data", "File"])
    {
        NoGetSet = true,
    };

    public ProjectLabelObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, string relativePath, string categoryName, string name)
        : base(session, interpreter)
    {
        Owner = owner;
        RelativePath = relativePath;
        CategoryName = categoryName;
        Name = name;
    }

    public ProjectObject Owner { get; }

    public string RelativePath { get; }

    public string CategoryName { get; }

    public string Name { get; }

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static ProjectLabelObject Me(DeviceObject o) => (ProjectLabelObject)o;

    private string DataType(DeviceCall call) => Owner.Live(call).FindCategory(CategoryName)?.DataType ?? "none";

    private JgsValue Data(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        string type = DataType(call);
        string? text = project.LabelsOf(RelativePath).FirstOrDefault(l => l.CategoryName == CategoryName && l.LabelName == Name).Data;
        return type switch
        {
            "char" => JgsValue.Str(text ?? ""),
            "logical" => text is null ? Empty00() : JgsValue.Bool(text == "true"),
            "double" or "integer" => text is null ? Empty00() : JgsValue.Number(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
            _ => Empty00(),
        };
    }

    /// <summary>The 0-by-0 double a label without data answers.</summary>
    private static JgsValue Empty00()
    {
        JgsValue empty = JgsValue.Array([]);
        empty.Reshape(0, 0);
        return empty;
    }

    private void SetData(JgsValue value, DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        ProjectCategory category = project.FindCategory(CategoryName)
            ?? throw call.Error("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{CategoryName}\" does not exist in the current project.");
        if (category.DataType == "none")
        {
            throw call.Error("MATLAB:project:api:LabelDataTypeIsNone", $"Unable to set data because the label \"{Name}\" in category \"{CategoryName}\" is of type \"None\".");
        }

        string text = category.DataType switch
        {
            "char" when DeviceChecks.IsText(value) => DeviceChecks.Text(value),
            "logical" when value.Type == JgsType.Bool => value.AsBool ? "true" : "false",
            "double" or "integer" when value.Type == JgsType.Number => value.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw call.Error("MATLAB:project:api:LabelDataTypeMismatch",
                $"The label \"{Name}\" in category \"{CategoryName}\" expects data of type \"{category.DataType}\" but was given data of type \"{DeviceChecks.ClassOf(value)}\"."),
        };
        try
        {
            project.AddLabel(RelativePath, CategoryName, Name, text);
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, call.Line, call.Column);
        }
    }

    protected override string ShortDisplay()
    {
        var call = new DeviceCall { Target = this, Args = [] };
        return ProjectDisplay.Block("Label",
        [
            ("File", ProjectDisplay.Shown(JgsValue.StringScalar(Owner.Project.FullPath(RelativePath)))),
            ("DataType", ProjectDisplay.Shown(JgsValue.StringScalar(DataType(call)))),
            ("Data", ProjectDisplay.Shown(Data(call))),
            ("Name", ProjectDisplay.Shown(JgsValue.StringScalar(Name))),
            ("CategoryName", ProjectDisplay.Shown(JgsValue.StringScalar(CategoryName))),
        ]);
    }

    protected override void OnDelete()
    {
    }
}

/// <summary><c>matlab.project.LabelDefinition</c>: a label's name in a category.</summary>
internal sealed class LabelDefinitionObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.LabelDefinition", "LabelDefinition", [],
        [
            new("Name", static (o, c) => JgsValue.StringScalar(Me(o).Name)),
            new("CategoryName", static (o, c) => JgsValue.StringScalar(Me(o).CategoryName)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && c.Args[0].AsExternalOrNull() is LabelDefinitionObject other && other.Name == Me(c.Target).Name && other.CategoryName == Me(c.Target).CategoryName)],
        },
        ["LabelDefinition", "eq"],
        ["Name", "CategoryName"])
    {
        NoGetSet = true,
    };

    public LabelDefinitionObject(DeviceSession session, Interpreter interpreter, string categoryName, string name)
        : base(session, interpreter)
    {
        CategoryName = categoryName;
        Name = name;
    }

    public string CategoryName { get; }

    public string Name { get; }

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static LabelDefinitionObject Me(DeviceObject o) => (LabelDefinitionObject)o;

    protected override string ShortDisplay() => ProjectDisplay.Block("LabelDefinition",
    [
        ("Name", ProjectDisplay.Shown(JgsValue.StringScalar(Name))),
        ("CategoryName", ProjectDisplay.Shown(JgsValue.StringScalar(CategoryName))),
    ]);

    protected override void OnDelete()
    {
    }
}

/// <summary><c>matlab.project.Category</c>: a category of labels.</summary>
internal sealed class CategoryObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.Category", "Category", ["handle"],
        [
            new("SingleValued", static (o, c) => JgsValue.Bool(Me(o).Found(c).SingleValued)),
            new("DataType", static (o, c) => JgsValue.StringScalar(Me(o).Found(c).DataType)),
            new("Name", static (o, c) => JgsValue.StringScalar(Me(o).Name)),
            new("LabelDefinitions", static (o, c) => Me(o).DefinitionsValue(c)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["createLabel"] = static c => [Me(c.Target).CreateLabel(c)],
            ["findLabel"] = static c => [Me(c.Target).FindLabel(c)],
            ["removeLabel"] = static c => { Me(c.Target).RemoveLabel(c); return []; },
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && c.Args[0].AsExternalOrNull() is CategoryObject other && other.Name == Me(c.Target).Name)],
        },
        ["addlistener", "createLabel", "delete", "eq", "findLabel", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "removeLabel"],
        ["SingleValued", "DataType", "Name", "LabelDefinitions"])
    {
        NoGetSet = true,
    };

    public CategoryObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, string name)
        : base(session, interpreter)
    {
        Owner = owner;
        Name = name;
    }

    public ProjectObject Owner { get; }

    public string Name { get; }

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static CategoryObject Me(DeviceObject o) => (CategoryObject)o;

    private ProjectCategory Found(DeviceCall call) => Owner.Live(call).FindCategory(Name)
        ?? throw call.Error("MATLAB:project:api:CategoryDoesNotExist", $"The category \"{Name}\" does not exist in the current project.");

    private JgsValue DefinitionsValue(DeviceCall call)
    {
        var items = Found(call).Labels.Select(l => (DeviceObject)new LabelDefinitionObject(Session, Interpreter, Name, l.Name)).ToList();
        return ProjectDisplay.ArrayOf(LabelDefinitionObject.Declaration, call.Interpreter, items);
    }

    private JgsValue CreateLabel(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        string label = call.Args.Count > 0 ? DeviceChecks.Text(call.Args[0]) : throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        try
        {
            ProjectLabelDefinition made = project.CreateLabel(Name, label);
            return JgsValue.External(new LabelDefinitionObject(Session, Interpreter, Name, made.Name));
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, call.Line, call.Column);
        }
    }

    private JgsValue FindLabel(DeviceCall call)
    {
        string label = call.Args.Count > 0 ? DeviceChecks.Text(call.Args[0]) : throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        return Found(call).Labels.Any(l => l.Name == label)
            ? JgsValue.External(new LabelDefinitionObject(Session, Interpreter, Name, label))
            : JgsValue.External(new DeviceArray(LabelDefinitionObject.Declaration, call.Interpreter, [], 1, 0));
    }

    private void RemoveLabel(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        string label = call.Args.Count > 0 ? DeviceChecks.Text(call.Args[0]) : throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        try
        {
            project.RemoveLabelDefinition(Name, label);
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, call.Line, call.Column);
        }
    }

    protected override string ShortDisplay()
    {
        var call = new DeviceCall { Target = this, Args = [] };
        ProjectCategory found = Found(call);
        return ProjectDisplay.Block("Category",
        [
            ("SingleValued", found.SingleValued ? "1" : "0"),
            ("DataType", "\"" + found.DataType + "\""),
            ("Name", "\"" + Name + "\""),
            ("LabelDefinitions", ProjectDisplay.Shown(DefinitionsValue(call))),
        ]);
    }

    protected override void OnDelete()
    {
    }
}

/// <summary><c>matlab.project.PathFolder</c>: a folder on the project path.</summary>
internal sealed class PathFolderObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.PathFolder", "PathFolder", ["matlab.project.FolderReference"],
        [
            new("File", static (o, c) => JgsValue.StringScalar(Me(o).FullPath)),
            new("StoredLocation", static (o, c) => JgsValue.StringScalar(Me(o).Relative)),
            new("Type", static (o, c) => JgsValue.StringScalar("Relative")),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        ["PathFolder"],
        ["File", "StoredLocation", "Type"])
    {
        NoGetSet = true,
    };

    public PathFolderObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, string relative)
        : base(session, interpreter)
    {
        Owner = owner;
        Relative = relative;
    }

    public ProjectObject Owner { get; }

    public string Relative { get; }

    public string FullPath => Relative.Length == 0 ? Owner.Project.RootFolder : Owner.Project.FullPath(Relative);

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static PathFolderObject Me(DeviceObject o) => (PathFolderObject)o;

    protected override string ShortDisplay() => ProjectDisplay.Block("PathFolder",
    [
        ("File", "\"" + FullPath + "\""),
        ("StoredLocation", "\"" + Relative + "\""),
        ("Type", "\"Relative\""),
    ]);

    protected override void OnDelete()
    {
    }
}

/// <summary><c>matlab.project.Shortcut</c>: a shortcut to a file.</summary>
internal sealed class ShortcutObject : DeviceObject
{
    public static readonly DeviceClass Declaration = new(
        "matlab.project.Shortcut", "Shortcut", ["handle"],
        [
            new("Name", static (o, c) => JgsValue.StringScalar(Me(o).Entry(c).Name)),
            new("Group", static (o, c) => { Me(o).Entry(c); return JgsValue.StringScalar("General"); }),
            new("File", static (o, c) => JgsValue.StringScalar(Me(o).Owner.Project.FullPath(Me(o).RelativePath))),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"],
        ["Name", "Group", "File"])
    {
        NoGetSet = true,
    };

    public ShortcutObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, string relativePath)
        : base(session, interpreter)
    {
        Owner = owner;
        RelativePath = relativePath;
    }

    public ProjectObject Owner { get; }

    public string RelativePath { get; }

    public override DeviceClass Class => Declaration;

    public override bool DeletesWhenUnheld => false;

    private static ShortcutObject Me(DeviceObject o) => (ShortcutObject)o;

    private ProjectEntryPoint Entry(DeviceCall call)
    {
        MatlabProject project = Owner.Live(call);
        return project.EntryPoints.Cast<ProjectEntryPoint?>().FirstOrDefault(e => string.Equals(e!.Value.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase))
            ?? new ProjectEntryPoint(RelativePath, Path.GetFileNameWithoutExtension(RelativePath), "Basic", true);
    }

    protected override string ShortDisplay()
    {
        var call = new DeviceCall { Target = this, Args = [] };
        return ProjectDisplay.Block("Shortcut",
        [
            ("Name", "\"" + Entry(call).Name + "\""),
            ("Group", "\"General\""),
            ("File", "\"" + Owner.Project.FullPath(RelativePath) + "\""),
        ]);
    }

    protected override void OnDelete()
    {
    }
}

/// <summary>
/// A compiler task configuration (<c>compiler.project.StandaloneAppConfigurationOptions</c> and its
/// kin): a handle over the task in its project, each property read from and written to the
/// project's files at once, checked as R2025b checks it.
/// </summary>
internal sealed class TaskConfigurationObject : DeviceObject
{
    private static readonly Dictionary<string, DeviceClass> Classes = new(StringComparer.Ordinal);

    public TaskConfigurationObject(DeviceSession session, Interpreter interpreter, ProjectObject owner, CompilerTaskConfiguration task)
        : base(session, interpreter)
    {
        Owner = owner;
        Task = task;
        lock (Classes)
        {
            if (!Classes.TryGetValue(task.Kind.ClassName, out DeviceClass? declared))
            {
                Classes[task.Kind.ClassName] = declared = Declare(task.Kind);
            }

            Class = declared;
        }
    }

    public ProjectObject Owner { get; }

    public CompilerTaskConfiguration Task { get; }

    public override DeviceClass Class { get; }

    public override bool DeletesWhenUnheld => false;

    private static TaskConfigurationObject Me(DeviceObject o) => (TaskConfigurationObject)o;

    private static DeviceClass Declare(CompilerTaskKind kind)
    {
        var properties = new List<DeviceProperty>();
        foreach (TaskProperty property in kind.Properties)
        {
            TaskProperty p = property;
            Action<DeviceObject, JgsValue, DeviceCall>? setter = p.Kind is TaskPropertyKind.ProfileName or TaskPropertyKind.LabelInfo or TaskPropertyKind.Issues
                ? null
                : (o, v, c) => Me(o).Set(p, v, c);
            properties.Add(new DeviceProperty(p.Name, (o, c) => Me(o).Get(p, c), setter, p.Hidden));
        }

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && ReferenceEquals(c.Args[0].AsExternalOrNull(), c.Target))],
            ["ne"] = static c => [JgsValue.Bool(!(c.Args.Count == 1 && ReferenceEquals(c.Args[0].AsExternalOrNull(), c.Target)))],
            ["isvalid"] = static c => [JgsValue.Bool(!c.Target.Deleted)],
        };
        string[] listing = ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"];
        return new DeviceClass(kind.ClassName, kind.ShortClassName, ["matlab.mixin.Heterogeneous", "handle"], properties, methods, listing,
            kind.Properties.Where(static p => !p.Hidden).Select(static p => p.Name).ToList())
        {
            NoGetSet = true,
        };
    }

    /// <summary>The task's project, when it is still loaded (a configuration of a closed project keeps writing, as R2025b's does).</summary>
    private MatlabProject Project => Task.Project;

    private JgsValue Get(TaskProperty property, DeviceCall call)
    {
        _ = call;
        switch (property.Kind)
        {
            case TaskPropertyKind.ProfileName:
                return JgsValue.Str(Task.Name);
            case TaskPropertyKind.Text:
            case TaskPropertyKind.Folder:
                return JgsValue.Str(Task.GetText(property));
            case TaskPropertyKind.Flag:
                return JgsValue.Bool(Task.GetFlag(property));
            case TaskPropertyKind.LabelFile:
                return JgsValue.Str(Task.LabelledFile(property.Key));
            case TaskPropertyKind.LabelFiles:
                return ProjectDisplay.CellColumn(Task.LabelledFiles(property.Key));
            case TaskPropertyKind.LabelInfo:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Category"] = JgsValue.Str(Task.CategoryName),
                    ["Name"] = JgsValue.Str(Task.LabelName(property.Key)),
                });
            case TaskPropertyKind.RuntimeInstall:
                return ProjectEnumValue.Of(Session, Interpreter, ProjectEnumValue.McrInstallType, Task.GetText(property));
            case TaskPropertyKind.ApiOption:
                return ProjectEnumValue.Of(Session, Interpreter, ProjectEnumValue.ApiOptionType, Task.GetText(property));
            case TaskPropertyKind.SupportPackages:
                return JgsValue.StructArray(new JgsStructArray([], ["Name", "Exclude"]), 0, 1);
            case TaskPropertyKind.Issues:
                return JgsValue.StructArray(new JgsStructArray([], ["AffectedFields", "Type", "Message"]), 0, 1);
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    private JgsRuntimeException Setting(DeviceCall call, string identifier, TaskProperty property, string reason) =>
        call.Error(identifier, $"Error setting property '{property.Name}' of class '{Task.Kind.ShortClassName}':\n{reason}");

    private static readonly string[] NameProperties = ["ApplicationName", "ArchiveName", "LibraryName", "AssemblyName", "PackageName", "AddInName", "Name", "ExecutableName"];

    private void Set(TaskProperty property, JgsValue value, DeviceCall call)
    {
        try
        {
            switch (property.Kind)
            {
                case TaskPropertyKind.Text:
                {
                    if (!DeviceChecks.IsText(value))
                    {
                        throw Setting(call, "MATLAB:class:RequireString", property, "Value must be a character vector or a string scalar.");
                    }

                    string text = DeviceChecks.Text(value);
                    if (text.Length == 0 && NameProperties.Contains(property.Name, StringComparer.Ordinal))
                    {
                        throw call.Error("deployment:exception:VALIDATION_FAILURE_REASON", "Validation failed: The value for \"Name\" cannot be empty.");
                    }

                    Task.SetText(property, text);
                    return;
                }

                case TaskPropertyKind.Folder:
                {
                    if (!DeviceChecks.IsText(value))
                    {
                        throw Setting(call, "MATLAB:class:RequireString", property, "Value must be a character vector or a string scalar.");
                    }

                    string folder = DeviceChecks.Text(value);
                    CompilerTaskConfiguration.CheckOutputFolder(folder);
                    Task.SetText(property, folder);
                    return;
                }

                case TaskPropertyKind.Flag:
                {
                    if (value.Type is not (JgsType.Bool or JgsType.Number) || DeviceChecks.Count(value) != 1)
                    {
                        throw Setting(call, "MATLAB:class:RequireScalar", property, "Value must be a scalar.");
                    }

                    Task.SetFlag(property, value.Type == JgsType.Bool ? value.AsBool : value.AsNumber != 0);
                    return;
                }

                case TaskPropertyKind.LabelFile:
                {
                    if (!DeviceChecks.IsText(value))
                    {
                        throw Setting(call, "MATLAB:class:RequireString", property, "Value must be a character vector or a string scalar.");
                    }

                    string file = DeviceChecks.Text(value);
                    Task.SetLabelledFile(property.Key, file.Length == 0 ? "" : Project.FullPath(file));
                    return;
                }

                case TaskPropertyKind.LabelFiles:
                {
                    var files = new List<string>();
                    if (value.Type == JgsType.Cell)
                    {
                        foreach (JgsValue item in value.AsCell)
                        {
                            if (!DeviceChecks.IsText(item))
                            {
                                throw Setting(call, "mcos_factory:mcos_factory:must_be_cell_array", property, "Parameter must be a cell array");
                            }

                            files.Add(Project.FullPath(DeviceChecks.Text(item)));
                        }
                    }
                    else if (value.IsStringArray)
                    {
                        for (int i = 0; i < value.ArrayLength; i++)
                        {
                            files.Add(Project.FullPath(value.ElementAt(i).AsString));
                        }
                    }
                    else
                    {
                        throw Setting(call, "mcos_factory:mcos_factory:must_be_cell_array", property, "Parameter must be a cell array");
                    }

                    Task.SetLabelledFiles(property.Key, files);
                    return;
                }

                case TaskPropertyKind.RuntimeInstall:
                case TaskPropertyKind.ApiOption:
                {
                    string className = property.Kind == TaskPropertyKind.RuntimeInstall ? ProjectEnumValue.McrInstallType : ProjectEnumValue.ApiOptionType;
                    string? member = value.AsExternalOrNull() is ProjectEnumValue e && e.ClassName == className ? e.Choice
                        : DeviceChecks.IsText(value) ? DeviceChecks.Text(value)
                        : null;
                    if (member is null || !ProjectEnumValue.Members[className].Contains(member, StringComparer.Ordinal))
                    {
                        throw Setting(call, "mf0:messages:InvalidValue", property, "The property value is invalid.");
                    }

                    Task.SetText(property, member);
                    return;
                }

                case TaskPropertyKind.SupportPackages:
                {
                    if (value.Type != JgsType.Struct || (value.ArrayLength == 1 && !value.AsStruct.Keys.OrderBy(static n => n, StringComparer.Ordinal).SequenceEqual(["Exclude", "Name"])))
                    {
                        throw call.Error("deployment:exception:MCOS_SUPPORT_PACKAGES_INCORRECT_FIELDS", "Configuration supported packages struct fields are limited to: \"Name\" and \"Exclude\".");
                    }

                    if (value.ArrayLength > 0)
                    {
                        throw call.Error("JGraph:compiler:noSupportPackages", "JGraph has no support packages, so a task's SupportPackages stays empty.");
                    }

                    return;
                }

                default:
                    throw call.Error("MATLAB:class:SetProhibited", $"Unable to set the '{property.Name}' property of class ''{Task.Kind.ShortClassName}'' because it is read-only.");
            }
        }
        catch (ProjectException ex)
        {
            throw ProjectDisplay.Refusal(ex, call.Line, call.Column);
        }
    }

    protected override string ShortDisplay()
    {
        var call = new DeviceCall { Target = this, Args = [] };
        var rows = new List<(string, string)>();
        foreach (TaskProperty property in Task.Kind.Properties.Where(static p => !p.Hidden))
        {
            rows.Add((property.Name, ProjectDisplay.Shown(Get(property, call))));
        }

        return ProjectDisplay.Block(Task.Kind.ShortClassName, rows);
    }

    protected override void OnDelete()
    {
    }
}

/// <summary>
/// What the project objects ask of the session: the current folder, the search path, running a
/// file, the dependency analysis, and opening and closing the current project.
/// </summary>
internal sealed class ProjectOperations
{
    public required Func<string> CurrentFolderGetter { get; init; }

    public required Action<string> AddToSearchPath { get; init; }

    public required Action<string> RemoveFromSearchPath { get; init; }

    public required Func<IReadOnlyList<string>, DependencyReport> Analyze { get; init; }

    public required Action<int, int> CloseCurrent { get; init; }

    public required Action<string, int, int> Reopen { get; init; }

    public string CurrentFolder => CurrentFolderGetter();
}
