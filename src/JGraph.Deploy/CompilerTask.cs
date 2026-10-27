namespace JGraph.Deploy;

/// <summary>
/// The targets the compiler apps build for (compiler and SDK plan, stage K19): R2025b's
/// <c>deployment:model</c> configuration types that JGraph's compiler builds, one app window each.
/// Java packages, COM components and web apps are out of scope (decisions KD1, KD2) and have no app.
/// </summary>
public enum CompilerTarget
{
    /// <summary>A standalone application (<c>standaloneApplicationCompiler</c>).</summary>
    StandaloneApplication,

    /// <summary>A Production Server archive (<c>productionServerArchiveCompiler</c>).</summary>
    ProductionServerArchive,

    /// <summary>A .NET assembly (<c>dotNetAssemblyCompiler</c>).</summary>
    DotNetAssembly,

    /// <summary>A Python package (<c>pythonPackageCompiler</c>).</summary>
    PythonPackage,

    /// <summary>A C++ shared library (<c>cppSharedLibraryCompiler</c>).</summary>
    CppSharedLibrary,

    /// <summary>A C shared library (<c>cSharedLibraryCompiler</c>).</summary>
    CSharedLibrary,

    /// <summary>An Excel add-in (<c>excelAddinCompiler</c>); its build waits for stage K17.</summary>
    ExcelAddIn,
}

/// <summary>Where an application installer gets the JGraph Runtime from (<c>compiler.package.InstallerOptions</c>' RuntimeDelivery).</summary>
public enum RuntimeDeliveryChoice
{
    /// <summary>Downloaded while installing.</summary>
    Web,

    /// <summary>Carried inside the installer.</summary>
    Installer,

    /// <summary>Left to the end user.</summary>
    None,
}

/// <summary>
/// What R2025b's catalogs say about each target: the app's name in the gallery
/// (<c>deployment:desktop</c>), the configuration type's name and description
/// (<c>deployment:model</c>), the command that opens the app, and the words of its window
/// (<c>compiler_ui_common</c> and the target's own catalog; probe <c>k19_apps</c>, <c>text.*</c>).
/// </summary>
public static class CompilerTargets
{
    /// <summary>Every target, in the gallery's order.</summary>
    public static IReadOnlyList<CompilerTarget> All { get; } =
    [
        CompilerTarget.StandaloneApplication, CompilerTarget.ProductionServerArchive, CompilerTarget.DotNetAssembly,
        CompilerTarget.PythonPackage, CompilerTarget.CppSharedLibrary, CompilerTarget.CSharedLibrary, CompilerTarget.ExcelAddIn,
    ];

    /// <summary>The app's name in the gallery: <c>Tool_&lt;command&gt;_Label</c>.</summary>
    public static string AppTitle(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Standalone Application Compiler",
        CompilerTarget.ProductionServerArchive => "Production Server Archive Compiler",
        CompilerTarget.DotNetAssembly => ".NET Assembly Compiler",
        CompilerTarget.PythonPackage => "Python Package Compiler",
        CompilerTarget.CppSharedLibrary => "C++ Shared Library Compiler",
        CompilerTarget.CSharedLibrary => "C Shared Library Compiler",
        CompilerTarget.ExcelAddIn => "Excel Add-in Compiler",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The command that opens the app, which the gallery's description names in parentheses.</summary>
    public static string Command(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "standaloneApplicationCompiler",
        CompilerTarget.ProductionServerArchive => "productionServerArchiveCompiler",
        CompilerTarget.DotNetAssembly => "dotNetAssemblyCompiler",
        CompilerTarget.PythonPackage => "pythonPackageCompiler",
        CompilerTarget.CppSharedLibrary => "cppSharedLibraryCompiler",
        CompilerTarget.CSharedLibrary => "cSharedLibraryCompiler",
        CompilerTarget.ExcelAddIn => "excelAddinCompiler",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The configuration type's name: <c>CONFIGTYPE_*</c>.</summary>
    public static string TypeName(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Standalone Application",
        CompilerTarget.ProductionServerArchive => "Deployable Archive (.ctf)",
        CompilerTarget.DotNetAssembly => ".NET Assembly",
        CompilerTarget.PythonPackage => "Python Package",
        CompilerTarget.CppSharedLibrary => "C++ Shared Library",
        CompilerTarget.CSharedLibrary => "C Shared Library",
        CompilerTarget.ExcelAddIn => "Excel Add-in",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The configuration type's description: <c>CONFIGTYPE_*_DESC</c>.</summary>
    public static string Description(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Package MATLAB programs for deployment as standalone applications",
        CompilerTarget.ProductionServerArchive => "Package MATLAB programs for deployment to MATLAB Production Server",
        CompilerTarget.DotNetAssembly => "Package MATLAB programs for deployment as .NET assemblies",
        CompilerTarget.PythonPackage => "Package MATLAB programs for deployment as Python packages",
        CompilerTarget.CppSharedLibrary => "Package MATLAB programs for deployment as C++ shared libraries",
        CompilerTarget.CSharedLibrary => "Package MATLAB programs for deployment as C shared libraries",
        CompilerTarget.ExcelAddIn => "Package MATLAB programs for deployment as Excel add-ins",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>Whether the target takes one main file (else a list of exported functions).</summary>
    public static bool HasMainFile(CompilerTarget target) => target == CompilerTarget.StandaloneApplication;

    /// <summary>Whether the target's build can be packaged into an installer (an archive cannot: "Current target does not support package step").</summary>
    public static bool CanPackage(CompilerTarget target) => target != CompilerTarget.ProductionServerArchive;

    /// <summary>Whether JGraph's compiler builds the target now (the Excel add-in waits for stage K17).</summary>
    public static bool IsBuilt(CompilerTarget target) => target != CompilerTarget.ExcelAddIn;

    /// <summary>The window's info section: "Application Info" and its siblings.</summary>
    public static string InfoTitle(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Application Info",
        CompilerTarget.ProductionServerArchive => "Archive Info",
        CompilerTarget.DotNetAssembly => ".NET Assembly Info",
        CompilerTarget.PythonPackage => "Package Info",
        CompilerTarget.ExcelAddIn => "Add-In Info",
        _ => "Library Info",
    };

    /// <summary>The label of the target's name: "Application Name" and its siblings.</summary>
    public static string NameLabel(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Application Name",
        CompilerTarget.ProductionServerArchive => "Archive Name",
        CompilerTarget.DotNetAssembly => "Assembly Name",
        CompilerTarget.PythonPackage => "Package Name",
        CompilerTarget.ExcelAddIn => "Add-In Name",
        _ => "Library Name",
    };

    /// <summary>The required-files section's title: "Files Required for Standalone to Run" and its siblings.</summary>
    public static string RequiredFilesTitle(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Files Required for Standalone to Run",
        CompilerTarget.ProductionServerArchive => "Files Required for Archive to Run",
        CompilerTarget.DotNetAssembly => "Files Required for the .NET Assembly to Run",
        CompilerTarget.PythonPackage => "Files Required for Package to Run",
        CompilerTarget.ExcelAddIn => "Files Required for the Add-In to Run",
        _ => "Files Required for Library to Run",
    };

    /// <summary>The packaging dialog's first step: "Building Standalone App" and its siblings.</summary>
    public static string BuildStep(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "Building Standalone App",
        CompilerTarget.ProductionServerArchive => "Building MPS Archive",
        CompilerTarget.DotNetAssembly => "Building .NET Assembly",
        CompilerTarget.PythonPackage => "Building Python Package",
        CompilerTarget.CppSharedLibrary => "Building C++ Shared Library",
        CompilerTarget.CSharedLibrary => "Building C Shared Library",
        CompilerTarget.ExcelAddIn => "Building Excel Add-In",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The status line while the task has no main file or exported function.</summary>
    public static string MissingMainText(CompilerTarget target) => HasMainFile(target)
        ? "Add a main file to make the task deployable"
        : "Add an exported function to make the task deployable";

    /// <summary>The packaging dialog's second step.</summary>
    public const string PackageStep = "Creating Installer";

    /// <summary>The status line of a task that can be built.</summary>
    public const string ReadyText = "The compiler task is ready";

    /// <summary>The main file's section title.</summary>
    public const string MainFileTitle = "Main File";

    /// <summary>The exported functions' section title.</summary>
    public const string ExportedFunctionsTitle = "Exported Functions";

    /// <summary>The runtime delivery choices' texts, in the installer settings' order.</summary>
    public static string RuntimeDeliveryText(RuntimeDeliveryChoice choice) => choice switch
    {
        RuntimeDeliveryChoice.Web => "Fetch MATLAB Runtime from mathworks.com during application installation",
        RuntimeDeliveryChoice.Installer => "Include MATLAB Runtime in application installer for offline installation",
        RuntimeDeliveryChoice.None => "Do not include MATLAB Runtime in application installer",
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };
}

/// <summary>
/// One compiler task as an app's window holds it (compiler and SDK plan, stage K19): the main
/// file or the exported functions, the info R2025b's apps ask for, the files, the target's own
/// settings and the installer's, from which <see cref="DeploymentScript"/> writes the
/// <c>compiler.build</c> script the Build and Package buttons run. The fields are R2025b's option
/// names where the options classes have them (stages K6 to K18), so the script sets them by name.
/// </summary>
public sealed class CompilerTaskModel
{
    /// <summary>Creates an empty task for <paramref name="target"/>.</summary>
    public CompilerTaskModel(CompilerTarget target)
    {
        Target = target;
    }

    /// <summary>What the task builds.</summary>
    public CompilerTarget Target { get; }

    /// <summary>The main file (a standalone application), or null.</summary>
    public string? MainFile { get; set; }

    /// <summary>The exported functions (every other target), as full paths.</summary>
    public List<string> ExportedFunctions { get; } = [];

    /// <summary>The application's, archive's, assembly's, package's, library's or add-in's name; the first file's when empty.</summary>
    public string Name { get; set; } = "";

    /// <summary>The version (ExecutableVersion, AssemblyVersion, LibraryVersion, the installer's Version).</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>The installer's AuthorName.</summary>
    public string AuthorName { get; set; } = "";

    /// <summary>The installer's AuthorCompany.</summary>
    public string CompanyName { get; set; } = "";

    /// <summary>The installer's AuthorEmail.</summary>
    public string Email { get; set; } = "";

    /// <summary>The installer's Summary.</summary>
    public string Summary { get; set; } = "";

    /// <summary>The installer's Description.</summary>
    public string Description { get; set; } = "";

    /// <summary>The files and folders added by hand (AdditionalFiles), beyond what the analysis finds.</summary>
    public List<string> AdditionalFiles { get; } = [];

    /// <summary>The files the analysis found (shown; not written to the script, which analyses again when it builds).</summary>
    public List<string> RequiredFiles { get; } = [];

    /// <summary>AutoDetectDataFiles.</summary>
    public bool AutoDetectDataFiles { get; set; } = true;

    /// <summary>ObfuscateArchive.</summary>
    public bool Obfuscate { get; set; }

    /// <summary>The build's output folder; the task's <c>output\build</c> under the project root when empty.</summary>
    public string OutputFolder { get; set; } = "";

    /// <summary>The installer's output folder; the task's <c>output\package</c> under the project root when empty.</summary>
    public string PackageFolder { get; set; } = "";

    // --- Standalone application ---

    /// <summary>ExecutableName; the main file's name when empty.</summary>
    public string ExecutableName { get; set; } = "";

    /// <summary>Whether the program is a Standalone Windows Application (no console) rather than a Standalone Application.</summary>
    public bool WindowsApplication { get; set; }

    /// <summary>TreatInputsAsNumeric.</summary>
    public bool TreatInputsAsNumeric { get; set; }

    /// <summary>ExecutableIcon, or empty for the default.</summary>
    public string ExecutableIcon { get; set; } = "";

    /// <summary>ExecutableSplashScreen, or empty for the default.</summary>
    public string ExecutableSplashScreen { get; set; } = "";

    // --- Libraries, packages and archives ---

    /// <summary>The .NET or C++ API: "mwarray" or "matlab-data" for .NET; "matlab-data" for C++ (the only one JGraph builds, decision KD7).</summary>
    public string Interface { get; set; } = "matlab-data";

    /// <summary>The archive's FunctionSignatures file, or empty.</summary>
    public string FunctionSignatures { get; set; } = "";

    /// <summary>The add-in's ClassName, or empty for R2025b's default.</summary>
    public string ClassName { get; set; } = "";

    // --- The installer ---

    /// <summary>Where the installer gets the runtime from.</summary>
    public RuntimeDeliveryChoice RuntimeDelivery { get; set; } = RuntimeDeliveryChoice.Web;

    /// <summary>InstallerName; R2025b's MyAppInstaller when empty.</summary>
    public string InstallerName { get; set; } = "";

    /// <summary>InstallationNotes.</summary>
    public string InstallerNotes { get; set; } = "";

    /// <summary>The installation root the installer proposes (the window's "Install Root").</summary>
    public string InstallRoot { get; set; } = "%ProgramFiles%";

    /// <summary>The folder under the root (the window's "Install subfolder"); the name when empty.</summary>
    public string InstallSubfolder { get; set; } = "";

    /// <summary>InstallerIcon, or empty for the default.</summary>
    public string InstallerIcon { get; set; } = "";

    /// <summary>InstallerSplash, or empty for the default.</summary>
    public string InstallerSplash { get; set; } = "";

    /// <summary>InstallerLogo (the window's "Sidebar Image"), or empty for the default.</summary>
    public string InstallerLogo { get; set; } = "";

    /// <summary>The installer's own AdditionalFiles ("Files Packaged in the Installer").</summary>
    public List<string> InstallerAdditionalFiles { get; } = [];

    /// <summary>The files the task starts from: the main file, or the exported functions.</summary>
    public IReadOnlyList<string> SourceFiles => CompilerTargets.HasMainFile(Target)
        ? (MainFile is { Length: > 0 } main ? [main] : [])
        : ExportedFunctions;

    /// <summary>The project root the script's paths are relative to: the first source file's folder, or null with none.</summary>
    public string? ProjectRoot => SourceFiles.Count > 0 ? Path.GetDirectoryName(Path.GetFullPath(SourceFiles[0])) : null;

    /// <summary>The name the build uses: <see cref="Name"/>, else the first source file's.</summary>
    public string EffectiveName => Name.Length > 0 ? Name
        : SourceFiles.Count > 0 ? Path.GetFileNameWithoutExtension(SourceFiles[0])
        : "";

    /// <summary>The build's output folder as the script writes it.</summary>
    public string EffectiveOutputFolder => OutputFolder.Length > 0 ? OutputFolder
        : ProjectRoot is { } root ? Path.Combine(root, EffectiveName, "output", "build") : "";

    /// <summary>The installer's output folder as the script writes it.</summary>
    public string EffectivePackageFolder => PackageFolder.Length > 0 ? PackageFolder
        : ProjectRoot is { } root ? Path.Combine(root, EffectiveName, "output", "package") : "";

    /// <summary>The installer's DefaultInstallationDir: the root and the subfolder (the name when none).</summary>
    public string DefaultInstallationDir => Path.Combine(InstallRoot, InstallSubfolder.Length > 0 ? InstallSubfolder : EffectiveName);

    /// <summary>Whether the task can be built: it has its main file or an exported function.</summary>
    public bool IsDeployable => SourceFiles.Count > 0;

    /// <summary>The window's status line: ready, or what is missing.</summary>
    public string StatusText => IsDeployable ? CompilerTargets.ReadyText : CompilerTargets.MissingMainText(Target);
}
