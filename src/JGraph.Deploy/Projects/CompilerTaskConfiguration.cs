using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JGraph.Deploy.Projects;

/// <summary>How a task property is held: its value's shape and where it lives.</summary>
public enum TaskPropertyKind
{
    /// <summary>The task's name: read-only.</summary>
    ProfileName,

    /// <summary>A char row in the model's JSON.</summary>
    Text,

    /// <summary>A logical in the model's JSON.</summary>
    Flag,

    /// <summary>One file by a label on a project file.</summary>
    LabelFile,

    /// <summary>A list of files by a label on project files.</summary>
    LabelFiles,

    /// <summary>A label's description: a struct with Category and Name, read-only.</summary>
    LabelInfo,

    /// <summary>A folder under the root, in the model's JSON, checked as R2025b checks an output folder.</summary>
    Folder,

    /// <summary>The runtime delivery enumeration: <c>web</c>, <c>installer</c>, <c>NONE</c>.</summary>
    RuntimeInstall,

    /// <summary>The API enumeration: <c>matlabdata</c>, <c>mwarray</c>.</summary>
    ApiOption,

    /// <summary>The support packages: a 0-by-1 struct with Name and Exclude.</summary>
    SupportPackages,

    /// <summary>The analysis issues: a hidden struct.</summary>
    Issues,
}

/// <summary>Where a property's JSON key lives: the build configuration entry or the installer entry.</summary>
public enum TaskSection
{
    Build,
    Installer,
}

/// <summary>One property of a task configuration class, as R2025b declares it.</summary>
/// <param name="Name">The property's name.</param>
/// <param name="Kind">Its shape.</param>
/// <param name="Key">The JSON key, or the label key (<c>MainFile</c>, <c>AdditionalFiles</c>) for a label-backed property.</param>
/// <param name="Section">Which entry holds the key.</param>
/// <param name="Default">The value before anything sets it, as text; a flag's "true"/"false".</param>
/// <param name="Hidden">Whether <c>properties</c> and the display leave it out.</param>
public sealed record TaskProperty(string Name, TaskPropertyKind Kind, string Key, TaskSection Section = TaskSection.Build, string Default = "", bool Hidden = false);

/// <summary>
/// One kind of compiler task (R2025b's configuration types): its type name on disk, its class, the
/// JSON model's names, its category's title (<c>CONFIGTYPE_*</c>), its labels and its properties
/// in R2025b's order (probe <c>k20_projects</c>, <c>task.*</c>).
/// </summary>
public sealed class CompilerTaskKind
{
    internal CompilerTaskKind(
        CompilerTarget? target, string type, string className, string modelAttribute, string buildEntryType, string categoryTitle,
        IReadOnlyList<(string Key, string Name)> labels, IReadOnlyList<TaskProperty> properties, bool hasInstaller)
    {
        Target = target;
        Type = type;
        ClassName = className;
        ModelAttribute = modelAttribute;
        BuildEntryType = buildEntryType;
        CategoryTitle = categoryTitle;
        Labels = labels;
        Properties = properties;
        HasInstaller = hasInstaller;
    }

    /// <summary>JGraph's target, or null for a kind JGraph does not build (Java, COM, web apps, toolboxes).</summary>
    public CompilerTarget? Target { get; }

    /// <summary>The node's <c>type</c> attribute: <c>StandaloneDesktopApp</c> and its kin.</summary>
    public string Type { get; }

    /// <summary>The configuration class: <c>compiler.project.StandaloneAppConfigurationOptions</c>.</summary>
    public string ClassName { get; }

    /// <summary>The attribute carrying the model's JSON: <c>StandaloneAppConfigModel</c>.</summary>
    public string ModelAttribute { get; }

    /// <summary>The build entry's type: <c>deployment.compiler.model.StandaloneAppBuildConfiguration</c>.</summary>
    public string BuildEntryType { get; }

    /// <summary>The category's title before the task name: <c>Standalone Application</c>.</summary>
    public string CategoryTitle { get; }

    /// <summary>The labels the task's category gets: the label id's middle part and its name.</summary>
    public IReadOnlyList<(string Key, string Name)> Labels { get; }

    /// <summary>The properties, in R2025b's order.</summary>
    public IReadOnlyList<TaskProperty> Properties { get; }

    /// <summary>Whether the model carries an installer entry.</summary>
    public bool HasInstaller { get; }

    /// <summary>The class's short name: <c>StandaloneAppConfigurationOptions</c>.</summary>
    public string ShortClassName => ClassName[(ClassName.LastIndexOf('.') + 1)..];

    /// <summary>A property by name, or null.</summary>
    public TaskProperty? Property(string name) => Properties.FirstOrDefault(p => p.Name == name);
}

/// <summary>The compiler task kinds R2025b has, with JGraph's targets where it builds them.</summary>
public static class CompilerTaskKinds
{
    private const string InstallerEntryType = "deployment.compiler.model.InstallerPackageOptions";

    private static readonly (string, string)[] InstallerLabels =
    [
        ("InstallerAdditionalFiles", "Installer Additional Files"), ("AddRemoveProgramsIconFile", "Windows Apps & Features Icon File"),
        ("InstallerIconFile", "Installer Icon File"), ("InstalerLogoFile", "Installer Logo Image File"), ("InstallerSplashFile", "Installer Splash Image File"),
    ];

    private static readonly TaskProperty[] AuthorProperties =
    [
        new("AuthorName", TaskPropertyKind.Text, "authorName"), new("AuthorEmail", TaskPropertyKind.Text, "authorEmail"),
        new("AuthorOrganization", TaskPropertyKind.Text, "authorOrganization"), new("Summary", TaskPropertyKind.Text, "summary"),
        new("Description", TaskPropertyKind.Text, "description"),
    ];

    private static TaskProperty[] CommonBuildProperties(string taskFolder) =>
    [
        new("AdditionalFileLabel", TaskPropertyKind.LabelInfo, "AdditionalFiles"), new("AdditionalFileList", TaskPropertyKind.LabelFiles, "AdditionalFiles"),
        new("BuildOutputFolder", TaskPropertyKind.Folder, "outputFolder", TaskSection.Build, Path.Combine(taskFolder, "output", "build")),
    ];

    private static readonly TaskProperty[] FlagsProperties =
    [
        new("AutoDetectDataFiles", TaskPropertyKind.Flag, "autoDetectDataFiles", TaskSection.Build, "true"),
        new("ObfuscateArchive", TaskPropertyKind.Flag, "obfuscateArchive", TaskSection.Build, "false"),
        new("Verbose", TaskPropertyKind.Flag, "verbose", TaskSection.Build, "true"),
        new("SupportPackages", TaskPropertyKind.SupportPackages, "supportPackageData"),
    ];

    private static TaskProperty[] InstallerProperties(string taskFolder) =>
    [
        new("PackageOutputFolder", TaskPropertyKind.Folder, "outputFolder", TaskSection.Installer, Path.Combine(taskFolder, "output", "package")),
        new("InstallationSubFolder", TaskPropertyKind.Text, "installSubFolder", TaskSection.Installer),
        new("RuntimeInstallType", TaskPropertyKind.RuntimeInstall, "mcrInstallation", TaskSection.Installer, "web"),
        new("InstallerName", TaskPropertyKind.Text, "installerName", TaskSection.Installer),
        new("InstallerVersion", TaskPropertyKind.Text, "installerVersion", TaskSection.Installer, "1.0.0"),
        new("InstallerAdditionalFileLabel", TaskPropertyKind.LabelInfo, "InstallerAdditionalFiles"),
        new("InstallerAddRemoveProgramsIconLabel", TaskPropertyKind.LabelInfo, "AddRemoveProgramsIconFile"),
        new("InstallerIconLabel", TaskPropertyKind.LabelInfo, "InstallerIconFile"),
        new("InstallerSplashLabel", TaskPropertyKind.LabelInfo, "InstallerSplashFile"),
        new("InstallerLogoLabel", TaskPropertyKind.LabelInfo, "InstalerLogoFile"),
        new("InstallerAdditionalFileList", TaskPropertyKind.LabelFiles, "InstallerAdditionalFiles"),
        new("InstallerAddRemoveProgramsIconFile", TaskPropertyKind.LabelFile, "AddRemoveProgramsIconFile"),
        new("InstallerIconFile", TaskPropertyKind.LabelFile, "InstallerIconFile"),
        new("InstallerSplashFile", TaskPropertyKind.LabelFile, "InstallerSplashFile"),
        new("InstallerLogoFile", TaskPropertyKind.LabelFile, "InstalerLogoFile"),
        new("InstallerNotes", TaskPropertyKind.Text, "installerNotes", TaskSection.Installer),
    ];

    private static readonly TaskProperty Issues = new("AnalysisIssues", TaskPropertyKind.Issues, "", TaskSection.Build, "", Hidden: true);

    private static readonly TaskProperty[] LibraryFilesProperties =
    [
        new("DebugBuild", TaskPropertyKind.Flag, "debugBuild", TaskSection.Build, "false"),
        new("FunctionFileLabel", TaskPropertyKind.LabelInfo, "ExportedFunctionFile"), new("FunctionFileList", TaskPropertyKind.LabelFiles, "ExportedFunctionFile"),
    ];

    private static readonly TaskProperty[] SampleProperties =
    [
        new("SampleFileLabel", TaskPropertyKind.LabelInfo, "SamplesFile"), new("SampleFileList", TaskPropertyKind.LabelFiles, "SamplesFile"),
    ];

    private static readonly (string, string)[] LibraryLabels =
    [
        ("AdditionalFiles", "Additional File"), ("DocumentationFile", "Documentation File"), ("ExportedFunctionFile", "Exported Function File"), ("SamplesFile", "Sample File"),
    ];

    /// <summary>The kind's properties for a task named <paramref name="name"/> (the output folders' defaults carry the name).</summary>
    private static CompilerTaskKind Make(string name, string type) => type switch
    {
        "StandaloneDesktopApp" => new CompilerTaskKind(CompilerTarget.StandaloneApplication, type, "compiler.project.StandaloneAppConfigurationOptions",
            "StandaloneAppConfigModel", "deployment.compiler.model.StandaloneAppBuildConfiguration", "Standalone Application",
            [("AdditionalFiles", "Additional File"), ("ExeIconFile", "Executable Icon File"), ("ExeSplashImageFile", "Executable Splash Image File"),
             ("HelpTextFile", "Help Text File"), ("MainFile", "Main Function"), .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("ApplicationName", TaskPropertyKind.Text, "name", TaskSection.Build, "My Desktop Application"),
                new("ApplicationVersion", TaskPropertyKind.Text, "version", TaskSection.Build, "1.0.0"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties,
                new("ExecutableName", TaskPropertyKind.Text, "executableName", TaskSection.Build, "MyDesktopApplication"),
                new("SuppressWindowsCmdPrompt", TaskPropertyKind.Flag, "suppressWindowsCmdPrompt", TaskSection.Build, "false"),
                new("TreatInputsAsNumeric", TaskPropertyKind.Flag, "treatInputsAsNumeric", TaskSection.Build, "false"),
                new("EmbedArchive", TaskPropertyKind.Flag, "embedArchive", TaskSection.Build, "true"),
                new("ExampleText", TaskPropertyKind.Text, "exampleText"), new("HelpText", TaskPropertyKind.Text, "helpText"),
                new("InstallNotesText", TaskPropertyKind.Text, "installNotes"),
                new("ExecutableIconLabel", TaskPropertyKind.LabelInfo, "ExeIconFile"), new("ExecutableIconFile", TaskPropertyKind.LabelFile, "ExeIconFile"),
                new("ExecutableSplashLabel", TaskPropertyKind.LabelInfo, "ExeSplashImageFile"), new("ExecutableSplashFile", TaskPropertyKind.LabelFile, "ExeSplashImageFile"),
                new("MainFileLabel", TaskPropertyKind.LabelInfo, "MainFile"), new("MainFile", TaskPropertyKind.LabelFile, "MainFile"),
                new("CustomHelpTextLabel", TaskPropertyKind.LabelInfo, "HelpTextFile"), new("CustomHelpTextFile", TaskPropertyKind.LabelFile, "HelpTextFile"),
                .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "ProductionServerArchive" => new CompilerTaskKind(CompilerTarget.ProductionServerArchive, type, "compiler.project.PSAConfigurationOptions",
            "ProductionServerArchiveConfigModel", "deployment.compiler.model.ProductionServerArchiveBuildConfiguration", "Deployable Archive (.ctf)",
            [("AdditionalFiles", "Additional File"), ("ExportedFunctionFile", "Exported Function File"), ("FunctionSignaturesFile", "Function Signatures File")],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""), .. CommonBuildProperties(name),
                new("ArchiveName", TaskPropertyKind.Text, "ctfArchiveName", TaskSection.Build, "MyProductionServerArchive"),
                .. FlagsProperties,
                new("FunctionFileLabel", TaskPropertyKind.LabelInfo, "ExportedFunctionFile"), new("FunctionFileList", TaskPropertyKind.LabelFiles, "ExportedFunctionFile"),
                new("FunctionSignatureFileLabel", TaskPropertyKind.LabelInfo, "FunctionSignaturesFile"), new("FunctionSignatureFile", TaskPropertyKind.LabelFile, "FunctionSignaturesFile"),
                Issues,
            ], hasInstaller: false),
        "PythonPackage" => new CompilerTaskKind(CompilerTarget.PythonPackage, type, "compiler.project.PythonLibraryConfigurationOptions",
            "PythonLibraryConfigModel", "deployment.compiler.model.PythonPackageBuildConfiguration", "Python Package",
            [.. LibraryLabels, .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("PackageName", TaskPropertyKind.Text, "name", TaskSection.Build, "My Python Package"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties, .. LibraryFilesProperties, .. SampleProperties,
                .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "DotNETAssembly" => new CompilerTaskKind(CompilerTarget.DotNetAssembly, type, "compiler.project.DotNETLibraryConfigurationOptions",
            "DotNETLibraryConfigModel", "deployment.compiler.model.DotNETAssemblyBuildConfiguration", ".NET Assembly",
            [.. LibraryLabels, ("StrongKeyFile", "Strong Key File (.NET Shared Assembly)"), .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("AssemblyName", TaskPropertyKind.Text, "assemblyName", TaskSection.Build, "MyDotNetAssembly"),
                new("AssemblyVersion", TaskPropertyKind.Text, "version", TaskSection.Build, "1.0.0"),
                new("ClassName", TaskPropertyKind.Text, "className", TaskSection.Build, "Class1"),
                new("EmbedArchive", TaskPropertyKind.Flag, "embedArchive", TaskSection.Build, "true"),
                new("EnableRemoting", TaskPropertyKind.Flag, "enableRemoting", TaskSection.Build, "false"),
                new("FrameworkVersion", TaskPropertyKind.Text, "frameworkVersion", TaskSection.Build, "4.0"),
                new("Interface", TaskPropertyKind.ApiOption, "apiOption", TaskSection.Build, "matlabdata"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties, .. LibraryFilesProperties, .. SampleProperties,
                new("StrongNameKeyFileLabel", TaskPropertyKind.LabelInfo, "StrongKeyFile"), new("StrongNameKeyFile", TaskPropertyKind.LabelFile, "StrongKeyFile"),
                .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "CppSharedLib" => new CompilerTaskKind(CompilerTarget.CppSharedLibrary, type, "compiler.project.CppLibraryConfigurationOptions",
            "CppLibraryConfigModel", "deployment.compiler.model.CPPLibraryBuildConfiguration", "C++ Shared Library",
            [.. LibraryLabels, .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("LibraryName", TaskPropertyKind.Text, "name", TaskSection.Build, "My C++ Library"),
                new("LibraryVersion", TaskPropertyKind.Text, "version", TaskSection.Build, "1.0.0"),
                new("EmbedArchive", TaskPropertyKind.Flag, "embedArchive", TaskSection.Build, "true"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties, .. LibraryFilesProperties,
                new("Interface", TaskPropertyKind.ApiOption, "apiOption", TaskSection.Build, "matlabdata"),
                .. SampleProperties, .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "CSharedLib" => new CompilerTaskKind(CompilerTarget.CSharedLibrary, type, "compiler.project.CLibraryConfigurationOptions",
            "CLibraryConfigModel", "deployment.compiler.model.CLibraryBuildConfiguration", "C Shared Library",
            [.. LibraryLabels, .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("LibraryName", TaskPropertyKind.Text, "name", TaskSection.Build, "My C Library"),
                new("LibraryVersion", TaskPropertyKind.Text, "version", TaskSection.Build, "1.0.0"),
                new("EmbedArchive", TaskPropertyKind.Flag, "embedArchive", TaskSection.Build, "true"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties, .. LibraryFilesProperties,
                .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "ExcelAddIn" => new CompilerTaskKind(CompilerTarget.ExcelAddIn, type, "compiler.project.ExcelAddInConfigurationOptions",
            "ExcelAddInConfigModel", "deployment.compiler.model.ExcelAddInBuildConfiguration", "Excel Add-in",
            [.. LibraryLabels, .. InstallerLabels],
            [
                new("ProfileDisplayName", TaskPropertyKind.ProfileName, ""),
                new("Name", TaskPropertyKind.Text, "name", TaskSection.Build, "My Excel Add-In"),
                new("AddInName", TaskPropertyKind.Text, "addInName", TaskSection.Build, "MyExcelAddIn"),
                new("AddInVersion", TaskPropertyKind.Text, "version", TaskSection.Build, "1.0.0"),
                new("ClassName", TaskPropertyKind.Text, "className", TaskSection.Build, "Class1"),
                new("EmbedArchive", TaskPropertyKind.Flag, "embedArchive", TaskSection.Build, "true"),
                new("GenerateVisualBasicFile", TaskPropertyKind.Flag, "generateVisualBasicFile", TaskSection.Build, "true"),
                .. AuthorProperties, .. CommonBuildProperties(name), .. FlagsProperties, .. LibraryFilesProperties,
                .. InstallerProperties(name), Issues,
            ], hasInstaller: true),
        "JavaPackage" => OutOfScope(type, "compiler.project.JavaLibraryConfigurationOptions", "JavaLibraryConfigModel", "Java Package"),
        "COMComponent" => OutOfScope(type, "compiler.project.COMLibraryConfigurationOptions", "COMLibraryConfigModel", "Generic COM Component"),
        "WebAppArchive" => OutOfScope(type, "compiler.project.WebAppConfigurationOptions", "WebAppConfigModel", "Web App"),
        "Toolbox" => OutOfScope(type, "compiler.project.ToolboxConfigurationOptions", "ToolboxConfigModel", "Toolbox"),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static CompilerTaskKind OutOfScope(string type, string className, string modelAttribute, string title) =>
        new(null, type, className, modelAttribute, "", title, [], [new("ProfileDisplayName", TaskPropertyKind.ProfileName, "")], hasInstaller: false);

    /// <summary>Every type name R2025b stores, in the gallery's order.</summary>
    public static readonly string[] Types =
    [
        "StandaloneDesktopApp", "ProductionServerArchive", "DotNETAssembly", "PythonPackage", "CppSharedLib", "CSharedLib", "ExcelAddIn",
        "JavaPackage", "COMComponent", "WebAppArchive", "Toolbox",
    ];

    /// <summary>The kind for a type name, for a task named <paramref name="name"/>; null for a name R2025b has not.</summary>
    public static CompilerTaskKind? ByType(string type, string name = "task") => Types.Contains(type) ? Make(name, type) : null;

    /// <summary>The type name for one of JGraph's targets.</summary>
    public static string TypeOf(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "StandaloneDesktopApp",
        CompilerTarget.ProductionServerArchive => "ProductionServerArchive",
        CompilerTarget.DotNetAssembly => "DotNETAssembly",
        CompilerTarget.PythonPackage => "PythonPackage",
        CompilerTarget.CppSharedLibrary => "CppSharedLib",
        CompilerTarget.CSharedLibrary => "CSharedLib",
        CompilerTarget.ExcelAddIn => "ExcelAddIn",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The getter that makes a kind: <c>compiler.project.getStandaloneDesktopAppConfiguration</c>.</summary>
    public static string GetterOf(string type) => type switch
    {
        "StandaloneDesktopApp" => "compiler.project.getStandaloneDesktopAppConfiguration",
        "ProductionServerArchive" => "compiler.project.getProductionServerArchiveConfiguration",
        "DotNETAssembly" => "compiler.project.getDotNETLibraryConfiguration",
        "PythonPackage" => "compiler.project.getPythonLibraryConfiguration",
        "CppSharedLib" => "compiler.project.getCppSharedLibraryConfiguration",
        "CSharedLib" => "compiler.project.getCSharedLibraryConfiguration",
        "ExcelAddIn" => "compiler.project.getExcelAddInConfiguration",
        "JavaPackage" => "compiler.project.getJavaLibraryConfiguration",
        "COMComponent" => "compiler.project.getCOMLibraryConfiguration",
        "WebAppArchive" => "compiler.project.getWebAppConfiguration",
        "Toolbox" => "compiler.project.getToolboxConfiguration",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    internal static string InstallerType => InstallerEntryType;
}

/// <summary>
/// One compiler task in a project (compiler and SDK plan, stage K20), as
/// <c>compiler.project.get*Configuration</c> hands one back: a handle over the task's node whose
/// properties read and write the model's JSON and the task's labels on the project's files, every
/// write saved at once. The model is R2025b's mf0 document (<c>entries</c> of a build
/// configuration and, for the targets that package, the installer options), the files are the
/// project files carrying the task's labels (<c>Main Function</c>, <c>Exported Function File</c>,
/// <c>Additional File</c>, the images, the installer's files).
/// </summary>
public sealed class CompilerTaskConfiguration
{
    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9_][A-Za-z0-9_ ]*$", RegexOptions.CultureInvariant);

    private readonly ProjectNode _node;

    internal CompilerTaskConfiguration(MatlabProject project, ProjectNode node, string name)
    {
        Project = project;
        _node = node;
        Name = name;
        Kind = CompilerTaskKinds.ByType(node.Attribute("type"), name)
            ?? throw new ProjectException("deployment:configmgr:ERROR_INVALID_CONFIG_TYPE", $"Unknown task type \"{node.Attribute("type")}\" was specified.");
    }

    /// <summary>The project the task lives in.</summary>
    public MatlabProject Project { get; }

    /// <summary>The task's name (its ProfileDisplayName).</summary>
    public string Name { get; private set; }

    /// <summary>The task's type: <c>StandaloneDesktopApp</c> and its kin.</summary>
    public string Type => _node.Attribute("type");

    /// <summary>The task's uuid, which its category and labels carry.</summary>
    public string UniqueId => _node.Attribute("uniqueId");

    /// <summary>The kind: class, properties, labels.</summary>
    public CompilerTaskKind Kind { get; }

    /// <summary>JGraph's target, or null for a kind JGraph does not build.</summary>
    public CompilerTarget? Target => Kind.Target;

    /// <summary>The task's category id: <c>Deployment_Category_&lt;uuid&gt;</c>.</summary>
    public string CategoryId => $"Deployment_Category_{UniqueId}";

    /// <summary>The task's category name: <c>Standalone Application - &lt;name&gt;</c>.</summary>
    public string CategoryName => $"{Kind.CategoryTitle} - {Name}";

    /// <summary>A label's id for one of the kind's label keys.</summary>
    public string LabelId(string key) => $"Deployment_{key}_{UniqueId}";

    /// <summary>A label's name for one of the kind's label keys, or the key itself.</summary>
    public string LabelName(string key) => Kind.Labels.FirstOrDefault(l => l.Key == key).Name ?? key;

    /// <summary>Checks a task name as R2025b's <c>Deployment Task Name</c> is checked.</summary>
    /// <exception cref="ProjectException">Empty, or with a character outside letters, numbers, underscores and spaces, or ending in a space.</exception>
    public static void CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            throw new ProjectException("deployment:exception:STRING_CANNOT_BE_EMPTY", "The value for \"Deployment Task Name\" cannot be empty.");
        }

        if (!NamePattern.IsMatch(name) || name.EndsWith(' '))
        {
            throw new ProjectException("deployment:exception:STRING_MUST_MATCH_PATTERN",
                "The value for \"Deployment Task Name\" must match the format \"May only contain letters, numbers, underscores and spaces.  Cannot start or end with spaces\".");
        }
    }

    // --- the model ---------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The model's JSON, parsed; an empty document when the node has none.</summary>
    public JsonObject ReadModel()
    {
        string json = _node.Attribute(Kind.ModelAttribute);
        if (json.Length > 0)
        {
            try
            {
                if (JsonNode.Parse(json) is JsonObject parsed)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return NewModel(Kind, Name, UniqueId);
    }

    /// <summary>Stores the model's JSON on the node (the caller saves the project).</summary>
    internal void WriteModel(JsonObject model) => _node.SetAttribute(Kind.ModelAttribute, model.ToJsonString(JsonOptions));

    /// <summary>A fresh model for a task of <paramref name="kind"/>: R2025b's entries with their defaults.</summary>
    internal static JsonObject NewModel(CompilerTaskKind kind, string name, string uniqueId)
    {
        var entries = new JsonArray();
        var build = new JsonObject();
        foreach ((string key, _) in kind.Labels.Where(l => !l.Key.StartsWith("Installer", StringComparison.Ordinal) && l.Key is not "AddRemoveProgramsIconFile" and not "InstalerLogoFile"))
        {
            build[BuildLabelKey(kind, key)] = LabelReference(uniqueId, key);
        }

        foreach (TaskProperty property in kind.Properties.Where(p => p.Section == TaskSection.Build && p.Kind is TaskPropertyKind.Text or TaskPropertyKind.Folder or TaskPropertyKind.ApiOption))
        {
            if (property.Default.Length > 0)
            {
                build[property.Key] = property.Default;
            }
        }

        build["supportPackageData"] = new JsonObject
        {
            ["content"] = new JsonObject(),
            ["type"] = "deployment.compiler.model.SupportPackageInfo",
            ["uuid"] = Guid.NewGuid().ToString(),
        };
        if (kind.Type == "StandaloneDesktopApp")
        {
            build["suppressWindowsCmdPrompt"] = false;
        }

        entries.Add(new JsonObject { ["content"] = build, ["type"] = kind.BuildEntryType, ["uuid"] = Guid.NewGuid().ToString() });
        if (kind.HasInstaller)
        {
            var installer = new JsonObject
            {
                ["addRemoveProgramsIconLabel"] = LabelReference(uniqueId, "AddRemoveProgramsIconFile"),
                ["additionalFileLabel"] = LabelReference(uniqueId, "InstallerAdditionalFiles"),
                ["installerIconLabel"] = LabelReference(uniqueId, "InstallerIconFile"),
                ["installerLogoLabel"] = LabelReference(uniqueId, "InstalerLogoFile"),
                ["installerSplashLabel"] = LabelReference(uniqueId, "InstallerSplashFile"),
                ["installerVersion"] = "1.0.0",
                ["outputFolder"] = Path.Combine(name, "output", "package"),
            };
            entries.Add(new JsonObject { ["content"] = installer, ["type"] = CompilerTaskKinds.InstallerType, ["uuid"] = Guid.NewGuid().ToString() });
        }

        return new JsonObject
        {
            ["entries"] = entries,
            ["packageUris"] = new JsonArray("http://schema.mathworks.com/mf0/deployment_compiler_model/1.0.0"),
            ["version"] = "1.0",
        };
    }

    /// <summary>The build entry's JSON key for a label: <c>mainFileLabel</c> for MainFile.</summary>
    private static string BuildLabelKey(CompilerTaskKind kind, string labelKey) => labelKey switch
    {
        "AdditionalFiles" => "additionalFileLabel",
        "ExeIconFile" => "exeIconLabel",
        "ExeSplashImageFile" => "exeSplashScreenLabel",
        "HelpTextFile" => "helpTextFileLabel",
        "MainFile" => "mainFileLabel",
        "ExportedFunctionFile" => kind.Type == "ProductionServerArchive" ? "exportedFunctionsLabel" : "exportedFunctionLabel",
        "FunctionSignaturesFile" => "signaturesFileLabel",
        "DocumentationFile" => "documentationFileLabel",
        "SamplesFile" => "sampleFileLabel",
        "StrongKeyFile" => "strongNameKeyFileLabel",
        _ => char.ToLowerInvariant(labelKey[0]) + labelKey[1..] + "Label",
    };

    private static JsonObject LabelReference(string uniqueId, string key) => new()
    {
        ["content"] = new JsonObject { ["categoryID"] = $"Deployment_Category_{uniqueId}", ["labelID"] = $"Deployment_{key}_{uniqueId}" },
        ["type"] = "deployment.compiler.model.ProjectLabel",
        ["uuid"] = Guid.NewGuid().ToString(),
    };

    private JsonObject? Entry(JsonObject model, TaskSection section)
    {
        string type = section == TaskSection.Build ? Kind.BuildEntryType : CompilerTaskKinds.InstallerType;
        return model["entries"] is JsonArray entries
            ? entries.OfType<JsonObject>().FirstOrDefault(e => (string?)e["type"] == type)?["content"] as JsonObject
            : null;
    }

    private JsonObject EnsureEntry(JsonObject model, TaskSection section)
    {
        if (Entry(model, section) is { } found)
        {
            return found;
        }

        var content = new JsonObject();
        var entries = model["entries"] as JsonArray ?? new JsonArray();
        model["entries"] = entries;
        entries.Add(new JsonObject
        {
            ["content"] = content,
            ["type"] = section == TaskSection.Build ? Kind.BuildEntryType : CompilerTaskKinds.InstallerType,
            ["uuid"] = Guid.NewGuid().ToString(),
        });
        return content;
    }

    // --- properties -------------------------------------------------------------------------------------

    /// <summary>A text property's value: the JSON's, else the default.</summary>
    public string GetText(TaskProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        JsonObject? entry = Entry(ReadModel(), property.Section);
        JsonNode? value = entry?[property.Key];
        return value is JsonValue v && v.TryGetValue(out string? text) ? text : property.Default;
    }

    /// <summary>A flag property's value: the JSON's, else the default.</summary>
    public bool GetFlag(TaskProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        JsonObject? entry = Entry(ReadModel(), property.Section);
        JsonNode? value = entry?[property.Key];
        return value is JsonValue v && v.TryGetValue(out bool flag) ? flag : property.Default == "true";
    }

    /// <summary>Stores a text property.</summary>
    public void SetText(TaskProperty property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        JsonObject model = ReadModel();
        EnsureEntry(model, property.Section)[property.Key] = value;
        WriteModel(model);
        Project.Save();
    }

    /// <summary>Stores a flag property.</summary>
    public void SetFlag(TaskProperty property, bool value)
    {
        ArgumentNullException.ThrowIfNull(property);
        JsonObject model = ReadModel();
        EnsureEntry(model, property.Section)[property.Key] = value;
        WriteModel(model);
        Project.Save();
    }

    /// <summary>The files carrying one of the task's labels, as full paths in the project's order.</summary>
    public IReadOnlyList<string> LabelledFiles(string labelKey) =>
        Project.FilesWithLabel(CategoryId, LabelId(labelKey)).Select(Project.FullPath).ToList();

    /// <summary>
    /// Makes <paramref name="files"/> (full paths or paths under the root) the files carrying a
    /// label, every file checked to be in the project first (R2025b's <c>FILE_NOT_IN_PROJECT</c>).
    /// </summary>
    public void SetLabelledFiles(string labelKey, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var nodes = new List<ProjectNode>();
        foreach (string file in files)
        {
            string? relative = Project.RelativePath(file);
            ProjectNode? node = relative is { Length: > 0 } ? Project.FileNode(relative) : null;
            if (node is null)
            {
                throw new ProjectException("deployment:exception:FILE_NOT_IN_PROJECT", $"File \"{Project.FullPath(file)}\" is not part of the Project.");
            }

            nodes.Add(node);
        }

        string labelId = LabelId(labelKey);
        foreach (string carrying in Project.FilesWithLabel(CategoryId, labelId))
        {
            if (Project.FileNode(carrying) is { } node)
            {
                MatlabProject.DetachLabelFrom(node, CategoryId, labelId);
            }
        }

        foreach (ProjectNode node in nodes)
        {
            MatlabProject.AttachLabel(node, CategoryId, labelId, singleValued: false);
        }

        Project.Save();
    }

    /// <summary>The one file carrying a label, as a full path, or empty.</summary>
    public string LabelledFile(string labelKey) => LabelledFiles(labelKey).FirstOrDefault() ?? "";

    /// <summary>Makes <paramref name="file"/> the one file carrying a label; empty clears it.</summary>
    public void SetLabelledFile(string labelKey, string file) => SetLabelledFiles(labelKey, file.Length == 0 ? [] : [file]);

    /// <summary>Checks a folder as R2025b checks an output folder: not empty, relative, and without the characters it refuses.</summary>
    public static void CheckOutputFolder(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ProjectException("deployment:exception:VALIDATION_FAILURE_REASON", "Validation failed: The value for \"Output Folder\" cannot be empty.");
        }

        if (value.IndexOfAny(['<', '>', ':', '"', '*', '|', '?', '&']) >= 0)
        {
            throw new ProjectException("deployment:exception:VALIDATION_FAILURE_REASON",
                "Validation failed: The value for \"Output Folder\" must not contain any of the characters: \"<, >, :, \", *, |, ?, &\".");
        }
    }

    /// <summary>Renames the task: its display name, its category's name and the default output folders that carried the old name.</summary>
    internal void Rename(string newName)
    {
        string old = Name;
        JsonObject model = ReadModel();
        foreach (TaskSection section in (TaskSection[])[TaskSection.Build, TaskSection.Installer])
        {
            if (Entry(model, section) is { } entry && entry["outputFolder"] is JsonValue v && v.TryGetValue(out string? folder)
                && folder.StartsWith(old + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                entry["outputFolder"] = newName + folder[old.Length..];
            }
        }

        WriteModel(model);
        _node.SetAttribute("displayName", newName);
        Name = newName;
        if (Project.Categories.FirstOrDefault(c => c.Id == CategoryId) is not null)
        {
            ProjectNode category = Project.CreateCategoryNode(CategoryId, CategoryName);
            category.SetAttribute("Name", CategoryName);
        }
    }

    // --- the task as the apps and the script generator see it -------------------------------------------

    /// <summary>
    /// The task as a <see cref="CompilerTaskModel"/>: what the app windows show and
    /// <see cref="DeploymentScript"/> writes. Null for a kind JGraph does not build.
    /// </summary>
    public CompilerTaskModel? ToTaskModel()
    {
        if (Target is not { } target)
        {
            return null;
        }

        var task = new CompilerTaskModel(target) { ProjectRootOverride = Project.RootFolder, TaskName = Name };
        string Text(string name) => Kind.Property(name) is { } p ? GetText(p) : "";
        bool Flag(string name) => Kind.Property(name) is { } p && GetFlag(p);
        task.Name = target switch
        {
            CompilerTarget.StandaloneApplication => Text("ApplicationName"),
            CompilerTarget.ProductionServerArchive => Text("ArchiveName"),
            CompilerTarget.DotNetAssembly => Text("AssemblyName"),
            CompilerTarget.PythonPackage => Text("PackageName"),
            CompilerTarget.ExcelAddIn => Text("AddInName"),
            _ => Text("LibraryName"),
        };
        task.Version = target switch
        {
            CompilerTarget.StandaloneApplication => Text("ApplicationVersion"),
            CompilerTarget.DotNetAssembly => Text("AssemblyVersion"),
            CompilerTarget.ExcelAddIn => Text("AddInVersion"),
            CompilerTarget.ProductionServerArchive => "",
            _ => Text("LibraryVersion"),
        };
        task.AuthorName = Text("AuthorName");
        task.Email = Text("AuthorEmail");
        task.CompanyName = Text("AuthorOrganization");
        task.Summary = Text("Summary");
        task.Description = Text("Description");
        task.AutoDetectDataFiles = Flag("AutoDetectDataFiles");
        task.Obfuscate = Flag("ObfuscateArchive");
        task.Verbose = Flag("Verbose");
        task.OutputFolder = Project.FullPath(Text("BuildOutputFolder"));
        task.AdditionalFiles.AddRange(LabelledFiles("AdditionalFiles"));
        if (target == CompilerTarget.StandaloneApplication)
        {
            task.MainFile = LabelledFile("MainFile") is { Length: > 0 } main ? main : null;
            task.ExecutableName = Text("ExecutableName");
            task.WindowsApplication = Flag("SuppressWindowsCmdPrompt");
            task.TreatInputsAsNumeric = Flag("TreatInputsAsNumeric");
            task.EmbedArchive = Flag("EmbedArchive");
            task.ExecutableIcon = LabelledFile("ExeIconFile");
            task.ExecutableSplashScreen = LabelledFile("ExeSplashImageFile");
            task.HelpText = Text("HelpText");
            task.ExampleText = Text("ExampleText");
            task.CustomHelpTextFile = LabelledFile("HelpTextFile");
        }
        else
        {
            task.ExportedFunctions.AddRange(LabelledFiles("ExportedFunctionFile"));
            task.EmbedArchive = Kind.Property("EmbedArchive") is null || Flag("EmbedArchive");
            task.DebugBuild = Flag("DebugBuild");
            if (Kind.Labels.Any(static l => l.Key == "SamplesFile"))
            {
                task.SampleFiles.AddRange(LabelledFiles("SamplesFile"));
            }
        }

        switch (target)
        {
            case CompilerTarget.ProductionServerArchive:
                task.FunctionSignatures = LabelledFile("FunctionSignaturesFile");
                break;
            case CompilerTarget.DotNetAssembly:
                task.Interface = Text("Interface") == "mwarray" ? "mwarray" : "matlab-data";
                task.ClassName = Text("ClassName");
                task.EnableRemoting = Flag("EnableRemoting");
                task.FrameworkVersion = Text("FrameworkVersion");
                task.StrongNameKeyFile = LabelledFile("StrongKeyFile");
                break;
            case CompilerTarget.CppSharedLibrary:
                task.Interface = Text("Interface") == "mwarray" ? "mwarray" : "matlab-data";
                break;
            case CompilerTarget.ExcelAddIn:
                task.ClassName = Text("ClassName");
                task.GenerateVisualBasicFile = Flag("GenerateVisualBasicFile");
                task.DisplayName = Text("Name");
                break;
        }

        if (Kind.HasInstaller)
        {
            task.PackageFolder = Project.FullPath(Text("PackageOutputFolder"));
            task.InstallSubfolder = Text("InstallationSubFolder");
            task.RuntimeDelivery = Text("RuntimeInstallType") switch
            {
                "installer" => RuntimeDeliveryChoice.Installer,
                "NONE" => RuntimeDeliveryChoice.None,
                _ => RuntimeDeliveryChoice.Web,
            };
            task.InstallerName = Text("InstallerName");
            task.InstallerVersion = Text("InstallerVersion");
            task.InstallerNotes = Text("InstallerNotes");
            task.InstallerAdditionalFiles.AddRange(LabelledFiles("InstallerAdditionalFiles"));
            task.InstallerIcon = LabelledFile("InstallerIconFile");
            task.InstallerSplash = LabelledFile("InstallerSplashFile");
            task.InstallerLogo = LabelledFile("InstalerLogoFile");
            task.InstallerAddRemoveProgramsIcon = LabelledFile("AddRemoveProgramsIconFile");
        }

        return task;
    }

    /// <summary>
    /// Takes a <see cref="CompilerTaskModel"/>'s fields into the task: what the app window edited,
    /// written back to the model and the labels. A file the project does not hold yet is added.
    /// </summary>
    public void ApplyTaskModel(CompilerTaskModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (Target is not { } target)
        {
            return;
        }

        void Text(string name, string value)
        {
            if (Kind.Property(name) is { } p && GetText(p) != value)
            {
                SetText(p, value);
            }
        }

        void Flag(string name, bool value)
        {
            if (Kind.Property(name) is { } p && GetFlag(p) != value)
            {
                SetFlag(p, value);
            }
        }

        void Files(string labelKey, IReadOnlyList<string> files)
        {
            foreach (string file in files)
            {
                if (Project.RelativePath(file) is { Length: > 0 } relative && Project.FindFile(relative) is null && (File.Exists(file) || Directory.Exists(file)))
                {
                    Project.AddFile(relative);
                }
            }

            if (!LabelledFiles(labelKey).SequenceEqual(files.Select(Project.FullPath), StringComparer.OrdinalIgnoreCase))
            {
                SetLabelledFiles(labelKey, files);
            }
        }

        void OneFile(string labelKey, string file) => Files(labelKey, file.Length == 0 ? [] : [file]);

        string Relative(string folder) => Project.RelativePath(folder) is { } r && r.Length > 0 ? r : folder;

        switch (target)
        {
            case CompilerTarget.StandaloneApplication:
                Text("ApplicationName", task.Name);
                Text("ApplicationVersion", task.Version);
                break;
            case CompilerTarget.ProductionServerArchive:
                Text("ArchiveName", task.Name);
                break;
            case CompilerTarget.DotNetAssembly:
                Text("AssemblyName", task.Name);
                Text("AssemblyVersion", task.Version);
                break;
            case CompilerTarget.PythonPackage:
                Text("PackageName", task.Name);
                break;
            case CompilerTarget.ExcelAddIn:
                Text("AddInName", task.Name);
                Text("AddInVersion", task.Version);
                Text("Name", task.DisplayName);
                break;
            default:
                Text("LibraryName", task.Name);
                Text("LibraryVersion", task.Version);
                break;
        }

        Text("AuthorName", task.AuthorName);
        Text("AuthorEmail", task.Email);
        Text("AuthorOrganization", task.CompanyName);
        Text("Summary", task.Summary);
        Text("Description", task.Description);
        Flag("AutoDetectDataFiles", task.AutoDetectDataFiles);
        Flag("ObfuscateArchive", task.Obfuscate);
        Flag("Verbose", task.Verbose);
        if (task.OutputFolder.Length > 0)
        {
            Text("BuildOutputFolder", Relative(task.OutputFolder));
        }

        Files("AdditionalFiles", task.AdditionalFiles);
        if (target == CompilerTarget.StandaloneApplication)
        {
            OneFile("MainFile", task.MainFile ?? "");
            Text("ExecutableName", task.ExecutableName);
            Flag("SuppressWindowsCmdPrompt", task.WindowsApplication);
            Flag("TreatInputsAsNumeric", task.TreatInputsAsNumeric);
            Flag("EmbedArchive", task.EmbedArchive);
            OneFile("ExeIconFile", task.ExecutableIcon);
            OneFile("ExeSplashImageFile", task.ExecutableSplashScreen);
            Text("HelpText", task.HelpText);
            Text("ExampleText", task.ExampleText);
            OneFile("HelpTextFile", task.CustomHelpTextFile);
        }
        else
        {
            Files("ExportedFunctionFile", task.ExportedFunctions);
            Flag("EmbedArchive", task.EmbedArchive);
            Flag("DebugBuild", task.DebugBuild);
        }

        switch (target)
        {
            case CompilerTarget.ProductionServerArchive:
                OneFile("FunctionSignaturesFile", task.FunctionSignatures);
                break;
            case CompilerTarget.DotNetAssembly:
                Text("Interface", task.Interface == "mwarray" ? "mwarray" : "matlabdata");
                Text("ClassName", task.ClassName);
                Flag("EnableRemoting", task.EnableRemoting);
                Text("FrameworkVersion", task.FrameworkVersion);
                OneFile("StrongKeyFile", task.StrongNameKeyFile);
                break;
            case CompilerTarget.CppSharedLibrary:
                Text("Interface", task.Interface == "mwarray" ? "mwarray" : "matlabdata");
                break;
            case CompilerTarget.ExcelAddIn:
                Text("ClassName", task.ClassName);
                Flag("GenerateVisualBasicFile", task.GenerateVisualBasicFile);
                break;
        }

        if (Kind.HasInstaller)
        {
            if (task.PackageFolder.Length > 0)
            {
                Text("PackageOutputFolder", Relative(task.PackageFolder));
            }

            Text("InstallationSubFolder", task.InstallSubfolder);
            Text("RuntimeInstallType", task.RuntimeDelivery switch
            {
                RuntimeDeliveryChoice.Installer => "installer",
                RuntimeDeliveryChoice.None => "NONE",
                _ => "web",
            });
            Text("InstallerName", task.InstallerName);
            Text("InstallerVersion", task.InstallerVersion);
            Text("InstallerNotes", task.InstallerNotes);
            Files("InstallerAdditionalFiles", task.InstallerAdditionalFiles);
            OneFile("InstallerIconFile", task.InstallerIcon);
            OneFile("InstallerSplashFile", task.InstallerSplash);
            OneFile("InstalerLogoFile", task.InstallerLogo);
            OneFile("AddRemoveProgramsIconFile", task.InstallerAddRemoveProgramsIcon);
        }
    }
}
