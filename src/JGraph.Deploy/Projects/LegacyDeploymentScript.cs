using System.Text;

namespace JGraph.Deploy.Projects;

/// <summary>The kinds of legacy project R2025b's <c>createDeploymentScript</c> writes a script for, by sub-target.</summary>
public enum LegacyTarget
{
    StandaloneApplication,
    CSharedLibrary,
    CppSharedLibrary,
    DotNetAssembly,
    JavaPackage,
    PythonPackage,
    ComComponent,
    ExcelAddIn,
    ProductionServerArchive,
    ExcelClientForProductionServer,
    WebAppArchive,
}

/// <summary>
/// What a legacy project's parameters and file sets mean (compiler and SDK plan, stage K21): the
/// values R2025b's <c>*PRJDataAdapter</c> classes read out of a <c>&lt;deployment-project&gt;</c>
/// for its deployment script and its upgrade into a project task, measured by probe
/// <c>k21_legacy</c> (<c>script.*</c>, <c>upgrade.*</c>). The additional files are the resources
/// file set sorted; the installer's files the package file set sorted; the icon is the first of
/// <c>param.icons</c> (R2025b passes <c>param.icon</c> by); <c>-C</c> among the mcc options turns
/// the archive's embedding off and any other option is warned about; a .NET or Java name is the
/// namespace when there is one; a Python package is <c>namespace.appname</c>.
/// </summary>
public sealed class LegacyTaskSettings
{
    private LegacyTaskSettings(LegacyDeploymentProject project, LegacyTarget target)
    {
        Project = project;
        Target = target;
    }

    /// <summary>The project the settings come from.</summary>
    public LegacyDeploymentProject Project { get; }

    /// <summary>The kind of task.</summary>
    public LegacyTarget Target { get; }

    /// <summary>JGraph's target for the kind, or null where JGraph builds none (Java, COM, web apps, the Excel client).</summary>
    public CompilerTarget? JGraphTarget => Target switch
    {
        LegacyTarget.StandaloneApplication => CompilerTarget.StandaloneApplication,
        LegacyTarget.CSharedLibrary => CompilerTarget.CSharedLibrary,
        LegacyTarget.CppSharedLibrary => CompilerTarget.CppSharedLibrary,
        LegacyTarget.DotNetAssembly => CompilerTarget.DotNetAssembly,
        LegacyTarget.PythonPackage => CompilerTarget.PythonPackage,
        LegacyTarget.ExcelAddIn => CompilerTarget.ExcelAddIn,
        LegacyTarget.ProductionServerArchive => CompilerTarget.ProductionServerArchive,
        _ => null,
    };

    /// <summary>The application, library, archive or add-in name: <c>param.appname</c>.</summary>
    public string AppName => Project.Param("param.appname");

    /// <summary>The version: <c>param.version</c>.</summary>
    public string Version => Project.Param("param.version");

    /// <summary>The namespace of a .NET, Java or Python library: <c>param.namespace</c>.</summary>
    public string Namespace => Project.Param("param.namespace");

    /// <summary>The name the build options carry: the namespace of a .NET assembly or Java package when set, <c>namespace.appname</c> for Python, else the app name.</summary>
    public string BuildName => Target switch
    {
        LegacyTarget.DotNetAssembly or LegacyTarget.JavaPackage => Namespace.Length > 0 ? Namespace : AppName,
        LegacyTarget.PythonPackage => Namespace.Length > 0 ? Namespace + "." + AppName : AppName,
        _ => AppName,
    };

    /// <summary>The author's name, email and company.</summary>
    public string AuthorName => Project.Param("param.authnamewatermark");

    /// <summary>The author's email.</summary>
    public string AuthorEmail => Project.Param("param.email");

    /// <summary>The author's company.</summary>
    public string AuthorCompany => Project.Param("param.company");

    /// <summary>The summary.</summary>
    public string Summary => Project.Param("param.summary");

    /// <summary>The description.</summary>
    public string Description => Project.Param("param.description");

    /// <summary>The main file of a standalone application or a web app.</summary>
    public string MainFile => Target == LegacyTarget.WebAppArchive
        ? Project.FileSet("fileset.web.main").FirstOrDefault() ?? ""
        : Project.FileSet("fileset.main").FirstOrDefault() ?? "";

    /// <summary>The exported functions of a library or an archive, in document order.</summary>
    public IReadOnlyList<string> ExportedFunctions => Project.FileSet("fileset.exports");

    /// <summary>The class organization, in document order.</summary>
    public IReadOnlyList<LegacyProjectClass> Classes => Project.Classes;

    /// <summary>The additional files: the resources file set, sorted.</summary>
    public IReadOnlyList<string> AdditionalFiles => Sorted(Project.FileSet("fileset.resources"));

    /// <summary>The installer's additional files: the package file set, sorted.</summary>
    public IReadOnlyList<string> InstallerAdditionalFiles => Sorted(Project.FileSet("fileset.package"));

    /// <summary>The sample files of a library: the examples file set, sorted.</summary>
    public IReadOnlyList<string> SampleFiles => Sorted(Project.FileSet("fileset.examples"));

    /// <summary>The icon: the first of <c>param.icons</c>, "" when there is none.</summary>
    public string Icon => Project.Icons.FirstOrDefault() ?? "";

    /// <summary>The splash screen: <c>param.screenshot</c>.</summary>
    public string Splash => Project.Param("param.screenshot");

    /// <summary>The installer's logo: <c>param.logo</c>.</summary>
    public string Logo => Project.Param("param.logo");

    /// <summary>The installation notes: <c>param.install.notes</c>.</summary>
    public string InstallNotes => Project.Param("param.install.notes");

    /// <summary>Whether a standalone application gets a help file: <c>param.checkbox</c>, whatever the text.</summary>
    public bool HelpFileWanted => Project.Flag("param.checkbox");

    /// <summary>The help text of a standalone application: <c>param.help.text</c>.</summary>
    public string HelpText => Project.Param("param.help.text");

    /// <summary>The example text of a standalone application.</summary>
    public string ExampleText => Project.Param("param.example");

    /// <summary>The mcc options' tokens: <c>param.user.defined.mcr.options</c> split on blanks.</summary>
    public IReadOnlyList<string> MccOptions =>
        Project.Param("param.user.defined.mcr.options").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether the archive is embedded: no <c>-C</c> among the mcc options.</summary>
    public bool EmbedArchive => !MccOptions.Contains("-C");

    /// <summary>The mcc options R2025b's builders cannot use: every token but <c>-C</c>.</summary>
    public IReadOnlyList<string> UnsupportedMccOptions => MccOptions.Where(o => o != "-C").ToList();

    /// <summary>Whether a standalone application is a Windows application: <c>param.windows.command.prompt</c> (suppress the console) is true.</summary>
    public bool WindowsApplication => Project.Flag("param.windows.command.prompt");

    /// <summary>Whether a standalone application treats its inputs as numeric: <c>param.native.matlab</c>.</summary>
    public bool TreatInputsAsNumeric => Project.Flag("param.native.matlab");

    /// <summary>The C++ API choice: <c>mwarray</c> for the legacy API alone, else <c>matlab-data</c>; both for <c>option.cpp.all</c>.</summary>
    public string CppApi => Project.Param("param.cpp.api") switch
    {
        "option.cpp.legacy" => "mwarray",
        _ => "matlab-data",
    };

    /// <summary>Whether a C++ library asks for both APIs (<c>option.cpp.all</c>, the default).</summary>
    public bool CppBothApis => Project.Param("param.cpp.api") is "option.cpp.all" or "";

    /// <summary>The .NET framework version: <c>5.0</c> for <c>option.net.version.five</c>, else <c>4.0</c>.</summary>
    public string FrameworkVersion => Project.Param("param.assembly.net.version") == "option.net.version.five" ? "5.0" : "4.0";

    /// <summary>Whether .NET remoting is enabled.</summary>
    public bool EnableRemoting => Project.Flag("param.net.enable.remoting");

    /// <summary>The strong name key file of a shared .NET assembly, "" for a private one.</summary>
    public string StrongNameKeyFile => Project.Flag("param.assembly.type") ? Project.Param("param.encryption.key.file") : "";

    /// <summary>Whether the type safe API was asked for (R2025b no longer builds it).</summary>
    public bool TypeSafeApi => Project.Flag("param.net.tsa.enable");

    /// <summary>The Excel add-in's or COM component's class: the first class's name, <c>Class1</c> when none.</summary>
    public string ClassName => Classes.FirstOrDefault()?.Name ?? "Class1";

    /// <summary>The output folder of the build: <c>param.intermediate</c>, repeated separators collapsed as R2025b writes it.</summary>
    public string OutputFolder => Folder(Project.Param("param.intermediate"));

    /// <summary>The output folder of the installer: <c>param.output</c>, repeated separators collapsed.</summary>
    public string PackageFolder => Folder(Project.Param("param.output"));

    private static string Folder(string path)
    {
        if (path.Length == 0)
        {
            return "";
        }

        string separator = Path.DirectorySeparatorChar.ToString();
        string doubled = separator + separator;
        string body = path.Length > 2 ? path[2..] : "";
        while (body.Contains(doubled, StringComparison.Ordinal))
        {
            body = body.Replace(doubled, separator, StringComparison.Ordinal);
        }

        return path[..Math.Min(2, path.Length)] + body;
    }

    /// <summary>The root the installer offers: the <c>param.installpath.combo</c> option's folder.</summary>
    public string InstallRoot => Project.Param("param.installpath.combo") switch
    {
        "option.installpath.appdata" => "%AppData%",
        "option.installpath.systemwideapp" => "/Applications",
        "option.installpath.user" => "/usr",
        "option.installpath.userlocal" => "/usr/local",
        _ => "%ProgramFiles%",
    };

    /// <summary>The installation subfolder: <c>param.installpath.string</c>.</summary>
    public string InstallSubfolder => Project.Param("param.installpath.string");

    /// <summary>The default installation folder: the root and the subfolder, a separator put between them when the subfolder brings none.</summary>
    public string DefaultInstallationDir
    {
        get
        {
            string sub = InstallSubfolder;
            while (sub.Contains("\\\\", StringComparison.Ordinal))
            {
                sub = sub.Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            if (sub.Length == 0)
            {
                return InstallRoot;
            }

            return sub[0] is '\\' or '/' ? InstallRoot + sub : InstallRoot + "\\" + sub;
        }
    }

    /// <summary>Whether the installer downloads the runtime: <c>param.web.mcr</c>.</summary>
    public bool WebRuntime => Project.Flag("param.web.mcr");

    /// <summary>Whether the installer carries the runtime: <c>param.package.mcr</c>.</summary>
    public bool PackagedRuntime => Project.Flag("param.package.mcr");

    /// <summary>Whether the installer leaves the runtime out: <c>param.no.mcr</c>.</summary>
    public bool NoRuntime => Project.Flag("param.no.mcr");

    /// <summary>
    /// The runtime delivery: the packaged runtime wins, then the web, then none when asked for
    /// alone. R2025b writes none for a project that asks for no runtime (its installer then
    /// downloads one under the packaged runtime's name); JGraph follows the option's meaning
    /// (recorded in MATLAB_Behavioral_Fixes).
    /// </summary>
    public RuntimeDeliveryChoice RuntimeDelivery => PackagedRuntime ? RuntimeDeliveryChoice.Installer
        : WebRuntime ? RuntimeDeliveryChoice.Web
        : NoRuntime ? RuntimeDeliveryChoice.None
        : RuntimeDeliveryChoice.Web;

    /// <summary>The installer's name: the web name when the runtime is downloaded, the no-runtime name when it is left out, else the packaged runtime's name.</summary>
    public string InstallerName => WebRuntime ? Project.Param("param.web.mcr.name")
        : !PackagedRuntime && NoRuntime ? Project.Param("param.no.mcr.name")
        : Project.Param("param.package.mcr.name");

    /// <summary>The Excel client's timeout and response size (<c>param.time.out</c>, <c>param.max.size</c>).</summary>
    public string ServerTimeOut => Project.Param("param.time.out");

    /// <summary>The Excel client's largest response, in MB.</summary>
    public string MaxResponseSize => Project.Param("param.max.size");

    /// <summary>The settings of <paramref name="project"/> read as a <paramref name="target"/> of the given kind, whatever its sub-target says.</summary>
    public static LegacyTaskSettings From(LegacyDeploymentProject project, LegacyTarget target)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new LegacyTaskSettings(project, target);
    }

    /// <summary>The settings of <paramref name="project"/>; null for a sub-target without a kind.</summary>
    public static LegacyTaskSettings? From(LegacyDeploymentProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        LegacyTarget? target = project.SubTarget switch
        {
            "subtarget.standalone" => LegacyTarget.StandaloneApplication,
            "subtarget.library.c" => LegacyTarget.CSharedLibrary,
            "subtarget.library.cpp" => LegacyTarget.CppSharedLibrary,
            "subtarget.net.component" => LegacyTarget.DotNetAssembly,
            "subtarget.java.package" => LegacyTarget.JavaPackage,
            "subtarget.python.module" => LegacyTarget.PythonPackage,
            "subtarget.com.component" => LegacyTarget.ComComponent,
            "subtarget.ex.addin" => LegacyTarget.ExcelAddIn,
            "subtarget.mads" => LegacyTarget.ProductionServerArchive,
            "subtarget.mps.excel" => LegacyTarget.ExcelClientForProductionServer,
            "subtarget.web.app" => LegacyTarget.WebAppArchive,
            _ => null,
        };
        return target is { } t ? new LegacyTaskSettings(project, t) : null;
    }

    /// <summary>Whether the kind takes a class organization: .NET, Java, COM, the Excel add-in and the Excel client.</summary>
    public bool NeedsClasses => Target is LegacyTarget.DotNetAssembly or LegacyTarget.JavaPackage or LegacyTarget.ComComponent
        or LegacyTarget.ExcelAddIn or LegacyTarget.ExcelClientForProductionServer;

    /// <summary>Whether the kind has a main file rather than exported functions.</summary>
    public bool HasMainFile => Target is LegacyTarget.StandaloneApplication or LegacyTarget.WebAppArchive;

    /// <summary>Whether the kind's script packages an installer (archives and web apps package none).</summary>
    public bool HasInstaller => Target is not (LegacyTarget.ProductionServerArchive or LegacyTarget.WebAppArchive or LegacyTarget.ExcelClientForProductionServer);

    /// <summary>
    /// R2025b's checks before a script or a build: a standalone application or web app without a
    /// main file, a library or archive without exported functions, a class-based kind without a
    /// class.
    /// </summary>
    public void Check()
    {
        if (HasMainFile && MainFile.Length == 0)
        {
            throw new LegacyProjectException("Compiler:build:compatibility:noMainFileSelected", "The project cannot be built because no main file is selected.");
        }

        if (!HasMainFile && ExportedFunctions.Count == 0)
        {
            throw new LegacyProjectException("Compiler:build:compatibility:noExportedFunctionsSelected", "The project cannot be built because no exported functions are selected.");
        }

        if (NeedsClasses && Classes.Count == 0)
        {
            throw new LegacyProjectException("Compiler:build:compatibility:invalidClassDefinition", "The project cannot be built because the class definitions are invalid.");
        }
    }

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> files) => files.OrderBy(f => f, StringComparer.Ordinal).ToList();
}

/// <summary>
/// The deployment script R2025b's <c>createDeploymentScript</c> writes for a legacy project
/// (compiler and SDK plan, stage K21; probe <c>k21_legacy</c>, <c>script.*</c>): the build options
/// object over the main file, the exported functions or a class map, the files, folders and flags,
/// the target's own lines, the build, then for the kinds that package one the runtime download
/// when it is carried and the installer options and <c>compiler.package.installer</c>. The paths
/// are written in full (no project root variable), the lists sorted, and the values as the
/// adapters read them.
/// </summary>
public static class LegacyDeploymentScript
{
    /// <summary>The warning R2025b raises for mcc options its builders cannot use.</summary>
    public const string MccFlagsWarningId = "Compiler:build:compatibility:mccFlagsNotSupported";

    /// <summary>The warning's words.</summary>
    public const string MccFlagsWarning = "Additional MCC options defined in the project will not be used in build.";

    /// <summary>The warning R2025b raises for a .NET project that asked for the type safe API.</summary>
    public const string TypeSafeWarningId = "Compiler:build:compatibility:typeSafeNotSupported";

    /// <summary>The warning's words.</summary>
    public const string TypeSafeWarning = "Building .NET Assemblies with the type safe API is no longer supported.  Build will continue without the type safe API.";

    /// <summary>
    /// The script for <paramref name="settings"/>, after <see cref="LegacyTaskSettings.Check"/>;
    /// <paramref name="warnings"/> gets the warnings R2025b raises on the way (the mcc options,
    /// the type safe API), identifier and words.
    /// </summary>
    public static string Generate(LegacyTaskSettings settings, out IReadOnlyList<(string Id, string Message)> warnings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Check();
        var raised = new List<(string, string)>();
        if (settings.UnsupportedMccOptions.Count > 0)
        {
            raised.Add((MccFlagsWarningId, MccFlagsWarning));
        }

        if (settings.Target == LegacyTarget.DotNetAssembly && settings.TypeSafeApi)
        {
            raised.Add((TypeSafeWarningId, TypeSafeWarning));
        }

        warnings = raised;
        var text = new StringBuilder();
        text.Append("% Create target build options object, set build properties and build.\n");
        AppendBuild(text, settings);
        if (settings.Target == LegacyTarget.ExcelClientForProductionServer)
        {
            AppendExcelClient(text, settings);
        }
        else if (settings.HasInstaller)
        {
            text.Append('\n');
            if (settings.RuntimeDelivery == RuntimeDeliveryChoice.Installer)
            {
                text.Append("% Download the MATLAB Runtime to include in the installer.\ncompiler.runtime.download;\n");
            }

            text.Append('\n');
            AppendPackage(text, settings);
        }

        return text.ToString();
    }

    /// <summary>The <c>compiler.build</c> options class and function for a kind.</summary>
    public static (string OptionsClass, string BuildFunction) BuildNames(LegacyTaskSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Target switch
        {
            LegacyTarget.StandaloneApplication => ("StandaloneApplicationOptions", settings.WindowsApplication ? "standaloneWindowsApplication" : "standaloneApplication"),
            LegacyTarget.CSharedLibrary => ("CSharedLibraryOptions", "cSharedLibrary"),
            LegacyTarget.CppSharedLibrary => ("CppSharedLibraryOptions", "cppSharedLibrary"),
            LegacyTarget.DotNetAssembly => ("DotNETAssemblyOptions", "dotNETAssembly"),
            LegacyTarget.JavaPackage => ("JavaPackageOptions", "javaPackage"),
            LegacyTarget.PythonPackage => ("PythonPackageOptions", "pythonPackage"),
            LegacyTarget.ComComponent => ("COMComponentOptions", "comComponent"),
            LegacyTarget.ExcelAddIn => ("ExcelAddInOptions", "excelAddIn"),
            LegacyTarget.ProductionServerArchive or LegacyTarget.ExcelClientForProductionServer => ("ProductionServerArchiveOptions", "productionServerArchive"),
            LegacyTarget.WebAppArchive => ("WebAppArchiveOptions", "webAppArchive"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    private static void AppendBuild(StringBuilder text, LegacyTaskSettings s)
    {
        (string optionsClass, string buildFunction) = BuildNames(s);
        bool classMap = s.Target is LegacyTarget.DotNetAssembly or LegacyTarget.JavaPackage or LegacyTarget.ComComponent;
        if (classMap)
        {
            text.Append("classmap = containers.Map;\n");
            foreach (LegacyProjectClass cls in s.Classes)
            {
                text.Append($"classmap({Quoted(cls.Name)}) = {List(cls.Files)};\n");
            }

            text.Append($"buildOpts = compiler.build.{optionsClass}(classmap);\n");
        }
        else
        {
            text.Append($"buildOpts = compiler.build.{optionsClass}({(s.HasMainFile ? Quoted(s.MainFile) : List(s.ExportedFunctions))});\n");
        }

        if (s.AdditionalFiles.Count > 0)
        {
            text.Append($"buildOpts.AdditionalFiles = {List(s.AdditionalFiles)};\n");
        }

        text.Append("buildOpts.AutoDetectDataFiles = true;\n");
        text.Append($"buildOpts.OutputDir = {Quoted(s.OutputFolder)};\n");
        text.Append("buildOpts.Verbose = true;\n");
        switch (s.Target)
        {
            case LegacyTarget.StandaloneApplication:
                if (s.HelpFileWanted)
                {
                    text.Append("helpTextFile = tempname;\n");
                    text.Append("fileID = fopen(helpTextFile,'w');\n");
                    text.Append("ensureFileCloseObject = onCleanup(@()fclose(fileID));\n");
                    text.Append($"fprintf(fileID, {Quoted(s.HelpText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal))});\n");
                    text.Append("ensureFileDeleteObject = onCleanup(@()delete(helpTextFile));\n");
                    text.Append("buildOpts.CustomHelpTextFile = helpTextFile;\n");
                }

                text.Append($"buildOpts.EmbedArchive = {Flag(s.EmbedArchive)};\n");
                AppendWhenSet(text, "buildOpts.ExecutableIcon", s.Icon);
                AppendWhenSet(text, "buildOpts.ExecutableName", s.AppName);
                AppendWhenSet(text, "buildOpts.ExecutableSplashScreen", s.Splash);
                text.Append($"buildOpts.ExecutableVersion = {Quoted(s.Version)};\n");
                text.Append($"buildOpts.TreatInputsAsNumeric = {Flag(s.TreatInputsAsNumeric)};\n");
                break;
            case LegacyTarget.CSharedLibrary:
                text.Append("buildOpts.DebugBuild = false;\n");
                text.Append($"buildOpts.EmbedArchive = {Flag(s.EmbedArchive)};\n");
                text.Append($"buildOpts.LibraryName = {Quoted(s.BuildName)};\n");
                break;
            case LegacyTarget.CppSharedLibrary:
                text.Append("buildOpts.DebugBuild = false;\n");
                if (s.CppBothApis)
                {
                    text.Append("%buildOpts.Interface = \"mwarray\";\n");
                }

                text.Append($"buildOpts.Interface = {Quoted(s.CppApi)};\n");
                text.Append($"buildOpts.LibraryName = {Quoted(s.BuildName)};\n");
                text.Append($"buildOpts.LibraryVersion = {Quoted(s.Version)};\n");
                AppendListWhenAny(text, "buildOpts.SampleGenerationFiles", s.SampleFiles);
                break;
            case LegacyTarget.DotNetAssembly:
                text.Append($"buildOpts.AssemblyName = {Quoted(s.BuildName)};\n");
                text.Append($"buildOpts.AssemblyVersion = {Quoted(s.Version)};\n");
                AppendListWhenAny(text, "buildOpts.SampleGenerationFiles", s.SampleFiles);
                text.Append($"buildOpts.EnableRemoting = {Flag(s.EnableRemoting)};\n");
                text.Append($"buildOpts.FrameworkVersion = {Quoted(s.FrameworkVersion)};\n");
                AppendWhenSet(text, "buildOpts.StrongNameKeyFile", s.StrongNameKeyFile);
                break;
            case LegacyTarget.JavaPackage:
                text.Append($"buildOpts.PackageName = {Quoted(s.BuildName)};\n");
                AppendListWhenAny(text, "buildOpts.SampleGenerationFiles", s.SampleFiles);
                break;
            case LegacyTarget.PythonPackage:
                text.Append($"buildOpts.PackageName = {Quoted(s.BuildName)};\n");
                AppendListWhenAny(text, "buildOpts.SampleGenerationFiles", s.SampleFiles);
                break;
            case LegacyTarget.ComComponent:
                text.Append($"buildOpts.ComponentName = {Quoted(s.BuildName)};\n");
                text.Append($"buildOpts.ComponentVersion = {Quoted(s.Version)};\n");
                text.Append($"buildOpts.EmbedArchive = {Flag(s.EmbedArchive)};\n");
                break;
            case LegacyTarget.ExcelAddIn:
                text.Append($"buildOpts.AddInName = {Quoted(s.BuildName)};\n");
                text.Append($"buildOpts.AddInVersion = {Quoted(s.Version)};\n");
                text.Append($"buildOpts.ClassName = {Quoted(s.ClassName)};\n");
                text.Append("buildOpts.DebugBuild = false;\n");
                text.Append($"buildOpts.EmbedArchive = {Flag(s.EmbedArchive)};\n");
                text.Append("buildOpts.GenerateVisualBasicFile = \"on\";\n");
                break;
            case LegacyTarget.ProductionServerArchive:
            case LegacyTarget.ExcelClientForProductionServer:
            case LegacyTarget.WebAppArchive:
                text.Append($"buildOpts.ArchiveName = {Quoted(s.BuildName)};\n");
                break;
        }

        text.Append($"buildResult = compiler.build.{buildFunction}(buildOpts);\n");
    }

    private static void AppendExcelClient(StringBuilder text, LegacyTaskSettings s)
    {
        text.Append("\n% Create target build options object, set build properties and build.\n");
        text.Append("buildOpts = compiler.build.ExcelClientForProductionServerOptions(buildResult);\n");
        text.Append($"buildOpts.AddInName = {Quoted(s.AppName)};\n");
        text.Append($"buildOpts.AddInVersion = {Quoted(s.Version)};\n");
        text.Append($"buildOpts.ClassName = {Quoted(s.ClassName)};\n");
        text.Append("buildOpts.DebugBuild = false;\n");
        text.Append($"buildOpts.FunctionFiles = {List(s.ExportedFunctions)};\n");
        text.Append($"buildOpts.OutputDir = {Quoted(s.OutputFolder)};\n");
        text.Append("buildOpts.Verbose = true;\n");
        text.Append("excelClientBuildResult = compiler.build.excelClientForProductionServer(buildOpts);\n");
        text.Append("\n% Create package options object, set package properties and package.\n");
        text.Append("packageOpts = compiler.package.ExcelClientForProductionServerOptions(excelClientBuildResult);\n");
        text.Append($"packageOpts.InstallerName = {Quoted(s.AppName + "ClientInstl")};\n");
        text.Append($"packageOpts.OutputDir = {Quoted(s.PackageFolder)};\n");
        text.Append($"packageOpts.MaxResponseSize = {Quoted(s.MaxResponseSize)};\n");
        text.Append($"packageOpts.ServerTimeOut = {Quoted(s.ServerTimeOut)};\n");
        text.Append("compiler.package.excelClientForProductionServer(buildResult, \"Options\", packageOpts);\n");
    }

    private static void AppendPackage(StringBuilder text, LegacyTaskSettings s)
    {
        text.Append("% Create package options object, set package properties and package.\n");
        text.Append("packageOpts = compiler.package.InstallerOptions(buildResult);\n");
        AppendListWhenAny(text, "packageOpts.AdditionalFiles", s.InstallerAdditionalFiles);
        AppendWhenSet(text, "packageOpts.AddRemoveProgramsIcon", s.Icon);
        AppendWhenSet(text, "packageOpts.ApplicationName", s.AppName);
        AppendWhenSet(text, "packageOpts.AuthorName", s.AuthorName);
        AppendWhenSet(text, "packageOpts.AuthorEmail", s.AuthorEmail);
        AppendWhenSet(text, "packageOpts.AuthorCompany", s.AuthorCompany);
        text.Append($"packageOpts.DefaultInstallationDir = {Quoted(s.DefaultInstallationDir)};\n");
        if (s.Description.Length > 0)
        {
            text.Append($"packageOpts.Description = {DeploymentScript.Lines(s.Description)};\n");
        }

        AppendWhenSet(text, "packageOpts.InstallerIcon", s.Icon);
        AppendWhenSet(text, "packageOpts.InstallerLogo", s.Logo);
        text.Append($"packageOpts.InstallerName = {Quoted(s.InstallerName)};\n");
        AppendWhenSet(text, "packageOpts.InstallerSplash", s.Splash);
        if (s.InstallNotes.Length > 0)
        {
            text.Append($"packageOpts.InstallationNotes = {DeploymentScript.Lines(s.InstallNotes)};\n");
        }

        text.Append($"packageOpts.OutputDir = {Quoted(s.PackageFolder)};\n");
        if (s.RuntimeDelivery != RuntimeDeliveryChoice.Web)
        {
            text.Append($"packageOpts.RuntimeDelivery = {Quoted(s.RuntimeDelivery.ToString().ToLowerInvariant())};\n");
        }

        if (s.Summary.Length > 0)
        {
            text.Append($"packageOpts.Summary = {DeploymentScript.Lines(s.Summary)};\n");
        }

        text.Append("packageOpts.Verbose = true;\n");
        text.Append($"packageOpts.Version = {Quoted(s.Version)};\n");
        text.Append("compiler.package.installer(buildResult, \"Options\", packageOpts);\n");
    }

    private static void AppendWhenSet(StringBuilder text, string property, string value)
    {
        if (value.Length > 0)
        {
            text.Append($"{property} = {Quoted(value)};\n");
        }
    }

    private static void AppendListWhenAny(StringBuilder text, string property, IReadOnlyList<string> files)
    {
        if (files.Count > 0)
        {
            text.Append($"{property} = {List(files)};\n");
        }
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string List(IReadOnlyList<string> files) => files.Count == 1
        ? Quoted(files[0])
        : "[" + string.Join(", ", files.Select(Quoted)) + "]";

    private static string Quoted(string text) => DeploymentScript.Quoted(text);
}
