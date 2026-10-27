using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The <c>compiler.build</c> script a compiler task stands for (compiler and SDK plan, stages K19
/// and K20): what the app's Code View shows, what Build and Build and Package run, and what Create
/// Build Script saves. Its shape is R2025b's deployment script as
/// <c>compiler.internal.deployScriptGenerator.DeployScriptBuilder</c> writes one for a project's
/// task (probe <c>k20_projects</c>, <c>script.*</c>): the project root, the build options object
/// set property by property (the common ones always, the target's own as R2025b writes them) and
/// built, then the package options object and the installer. Two places follow the options'
/// meaning where R2025b's generator does not (ADR 0275): a task that suppresses the Windows command
/// prompt builds a <c>standaloneWindowsApplication</c>, and an installer delivery of
/// <c>installer</c> is written as <c>RuntimeDelivery</c>.
/// </summary>
public static class DeploymentScript
{
    /// <summary>The script's variable for the project root, as R2025b writes it.</summary>
    public const string ProjectRootVariable = "projectRoot";

    /// <summary>
    /// Writes the script for <paramref name="task"/>: the build, and the installer too when
    /// <paramref name="package"/> is set and the target can be packaged.
    /// </summary>
    public static string Generate(CompilerTaskModel task, bool package)
    {
        ArgumentNullException.ThrowIfNull(task);
        var text = new StringBuilder();
        string? root = task.ProjectRoot;
        text.Append($"{ProjectRootVariable} = {Quoted(root ?? "")};\n\n");
        text.Append("% Create target build options object, set build properties and build.\n");
        AppendBuild(text, task, root);
        if (package && CompilerTargets.CanPackage(task.Target))
        {
            text.Append("\n\n");
            AppendPackage(text, task, root);
        }

        return text.ToString();
    }

    /// <summary>
    /// The installer's part alone, over the <c>buildResult</c> a build left in the workspace: what
    /// the app's Package step runs after its Build step, so each step is its own run.
    /// </summary>
    public static string GeneratePackageOnly(CompilerTaskModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var text = new StringBuilder();
        text.Append($"{ProjectRootVariable} = {Quoted(task.ProjectRoot ?? "")};\n");
        AppendPackage(text, task, task.ProjectRoot);
        return text.ToString();
    }

    private static string Files(CompilerTaskModel task, string? root) => task.SourceFiles.Count == 1
        ? PathExpression(task.SourceFiles[0], root)
        : "[" + string.Join(", ", task.SourceFiles.Select(f => PathExpression(f, root))) + "]";

    private static void AppendBuild(StringBuilder text, CompilerTaskModel task, string? root)
    {
        string files = Files(task, root);
        string name = task.EffectiveName;
        if (task.Target == CompilerTarget.DotNetAssembly)
        {
            text.Append("classmap = containers.Map;\n");
            text.Append($"classmap({Quoted(task.ClassName.Length > 0 ? task.ClassName : "Class1")}) = {files};\n");
            text.Append("buildOpts = compiler.build.DotNETAssemblyOptions(classmap);\n");
        }
        else
        {
            text.Append($"buildOpts = compiler.build.{OptionsClass(task.Target)}({files});\n");
        }

        if (task.AdditionalFiles.Count > 0)
        {
            text.Append($"buildOpts.AdditionalFiles = {PathList(task.AdditionalFiles, root)};\n");
        }

        text.Append($"buildOpts.AutoDetectDataFiles = {Flag(task.AutoDetectDataFiles)};\n");
        text.Append($"buildOpts.OutputDir = {PathExpression(task.EffectiveOutputFolder, root)};\n");
        text.Append($"buildOpts.ObfuscateArchive = {Flag(task.Obfuscate)};\n");
        text.Append("buildOpts.Verbose = true;\n");
        switch (task.Target)
        {
            case CompilerTarget.StandaloneApplication:
                if (task.HelpText.Length > 0)
                {
                    text.Append("helpTextFile = tempname;\n");
                    text.Append("fileID = fopen(helpTextFile,'w');\n");
                    text.Append("ensureFileCloseObject = onCleanup(@()fclose(fileID));\n");
                    text.Append($"fprintf(fileID, {Quoted(task.HelpText)});\n");
                    text.Append("ensureFileDeleteObject = onCleanup(@()delete(helpTextFile));\n");
                    text.Append("buildOpts.CustomHelpTextFile = helpTextFile;\n");
                }
                else if (task.CustomHelpTextFile.Length > 0)
                {
                    text.Append($"buildOpts.CustomHelpTextFile = {PathExpression(task.CustomHelpTextFile, root)};\n");
                }

                text.Append($"buildOpts.EmbedArchive = {Flag(task.EmbedArchive)};\n");
                if (task.ExecutableIcon.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableIcon = {PathExpression(task.ExecutableIcon, root)};\n");
                }

                text.Append($"buildOpts.ExecutableName = {Quoted(task.EffectiveExecutableName)};\n");
                if (task.ExecutableSplashScreen.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableSplashScreen = {PathExpression(task.ExecutableSplashScreen, root)};\n");
                }

                text.Append($"buildOpts.ExecutableVersion = {Quoted(task.Version)};\n");
                text.Append($"buildOpts.TreatInputsAsNumeric = {Flag(task.TreatInputsAsNumeric)};\n");
                break;
            case CompilerTarget.ProductionServerArchive:
                text.Append($"buildOpts.ArchiveName = {Quoted(name)};\n");
                if (task.FunctionSignatures.Length > 0)
                {
                    text.Append($"buildOpts.FunctionSignatures = {PathExpression(task.FunctionSignatures, root)};\n");
                }

                break;
            case CompilerTarget.PythonPackage:
                text.Append($"buildOpts.PackageName = {Quoted(name)};\n");
                break;
            case CompilerTarget.DotNetAssembly:
                text.Append($"buildOpts.AssemblyName = {Quoted(name)};\n");
                text.Append($"buildOpts.AssemblyVersion = {Quoted(task.Version)};\n");
                text.Append($"buildOpts.DebugBuild = {Flag(task.DebugBuild)};\n");
                text.Append($"buildOpts.EmbedArchive = {Quoted(task.EmbedArchive ? "on" : "off")};\n");
                text.Append($"buildOpts.Interface = {Quoted(task.Interface)};\n");
                text.Append($"buildOpts.EnableRemoting = {Flag(task.EnableRemoting)};\n");
                text.Append($"buildOpts.FrameworkVersion = {Quoted(task.FrameworkVersion)};\n");
                if (task.StrongNameKeyFile.Length > 0)
                {
                    text.Append($"buildOpts.StrongNameKeyFile = {PathExpression(task.StrongNameKeyFile, root)};\n");
                }

                break;
            case CompilerTarget.CppSharedLibrary:
                text.Append($"buildOpts.DebugBuild = {Flag(task.DebugBuild)};\n");
                text.Append($"buildOpts.Interface = {Quoted(task.Interface)};\n");
                text.Append($"buildOpts.LibraryName = {Quoted(name)};\n");
                text.Append($"buildOpts.LibraryVersion = {Quoted(task.Version)};\n");
                break;
            case CompilerTarget.CSharedLibrary:
                text.Append($"buildOpts.DebugBuild = {Flag(task.DebugBuild)};\n");
                text.Append($"buildOpts.LibraryName = {Quoted(name)};\n");
                break;
            case CompilerTarget.ExcelAddIn:
                text.Append($"buildOpts.AddInName = {Quoted(name)};\n");
                text.Append($"buildOpts.AddInVersion = {Quoted(task.Version)};\n");
                text.Append($"buildOpts.ClassName = {Quoted(task.ClassName.Length > 0 ? task.ClassName : "Class1")};\n");
                text.Append($"buildOpts.DebugBuild = {Flag(task.DebugBuild)};\n");
                text.Append($"buildOpts.EmbedArchive = {Flag(task.EmbedArchive)};\n");
                text.Append($"buildOpts.GenerateVisualBasicFile = {Flag(task.GenerateVisualBasicFile)};\n");
                break;
        }

        text.Append($"buildResult = {BuildFunction(task)}(buildOpts);\n");
    }

    private static void AppendPackage(StringBuilder text, CompilerTaskModel task, string? root)
    {
        text.Append("% Create package options object, set package properties and package.\n");
        text.Append("packageOpts = compiler.package.InstallerOptions(buildResult);\n");
        text.Append($"packageOpts.ApplicationName = {Quoted(task.Target == CompilerTarget.ExcelAddIn && task.DisplayName.Length > 0 ? task.DisplayName : task.EffectiveName)};\n");
        AppendWhenSet(text, "AuthorName", task.AuthorName);
        AppendWhenSet(text, "AuthorEmail", task.Email);
        AppendWhenSet(text, "AuthorCompany", task.CompanyName);
        if (task.InstallerAdditionalFiles.Count > 0)
        {
            text.Append($"packageOpts.AdditionalFiles = {PathList(task.InstallerAdditionalFiles, root)};\n");
        }

        // R2025b writes the subfolder alone (the installer puts a relative folder under Program Files);
        // a root the window set to something else is written whole.
        if (task.InstallRoot != "%ProgramFiles%")
        {
            text.Append($"packageOpts.DefaultInstallationDir = {Quoted(task.DefaultInstallationDir)};\n");
        }
        else if (task.InstallSubfolder.Length > 0)
        {
            text.Append($"packageOpts.DefaultInstallationDir = {Quoted(task.InstallSubfolder)};\n");
        }

        if (task.Description.Length > 0)
        {
            text.Append($"packageOpts.Description = {Lines(task.Description)};\n");
        }

        AppendPathWhenSet(text, "InstallerIcon", task.InstallerIcon, root);
        AppendPathWhenSet(text, "InstallerLogo", task.InstallerLogo, root);
        AppendWhenSet(text, "InstallerName", task.InstallerName);
        AppendPathWhenSet(text, "InstallerSplash", task.InstallerSplash, root);
        if (task.InstallerNotes.Length > 0)
        {
            text.Append($"packageOpts.InstallationNotes = {Lines(task.InstallerNotes)};\n");
        }

        text.Append($"packageOpts.OutputDir = {PathExpression(task.EffectivePackageFolder, root)};\n");
        if (task.RuntimeDelivery != RuntimeDeliveryChoice.Web)
        {
            text.Append($"packageOpts.RuntimeDelivery = {Quoted(task.RuntimeDelivery.ToString().ToLowerInvariant())};\n");
        }

        if (task.Summary.Length > 0)
        {
            text.Append($"packageOpts.Summary = {Lines(task.Summary)};\n");
        }

        text.Append("packageOpts.Verbose = true;\n");
        if (task.InstallerVersion.Length > 0 && task.InstallerVersion != "1.0.0")
        {
            text.Append($"packageOpts.Version = {Quoted(task.InstallerVersion)};\n");
        }

        text.Append("compiler.package.installer(buildResult, \"Options\", packageOpts);\n");
    }

    private static void AppendWhenSet(StringBuilder text, string property, string value)
    {
        if (value.Length > 0)
        {
            text.Append($"packageOpts.{property} = {Quoted(value)};\n");
        }
    }

    private static void AppendPathWhenSet(StringBuilder text, string property, string value, string? root)
    {
        if (value.Length > 0)
        {
            text.Append($"packageOpts.{property} = {PathExpression(value, root)};\n");
        }
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string PathList(IReadOnlyList<string> files, string? root) => files.Count == 1
        ? PathExpression(files[0], root)
        : "[" + string.Join(", ", files.Select(f => PathExpression(f, root))) + "]";

    /// <summary>A text with line breaks as R2025b writes it: the lines joined by <c>+ newline +</c>.</summary>
    public static string Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join(" + newline + ", text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(Quoted));
    }

    /// <summary>The <c>compiler.build</c> options class for a target.</summary>
    public static string OptionsClass(CompilerTarget target) => target switch
    {
        CompilerTarget.StandaloneApplication => "StandaloneApplicationOptions",
        CompilerTarget.ProductionServerArchive => "ProductionServerArchiveOptions",
        CompilerTarget.DotNetAssembly => "DotNETAssemblyOptions",
        CompilerTarget.PythonPackage => "PythonPackageOptions",
        CompilerTarget.CppSharedLibrary => "CppSharedLibraryOptions",
        CompilerTarget.CSharedLibrary => "CSharedLibraryOptions",
        CompilerTarget.ExcelAddIn => "ExcelAddInOptions",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The <c>compiler.build</c> function that builds the task's target.</summary>
    public static string BuildFunction(CompilerTaskModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Target switch
        {
            CompilerTarget.StandaloneApplication => task.WindowsApplication ? "compiler.build.standaloneWindowsApplication" : "compiler.build.standaloneApplication",
            CompilerTarget.ProductionServerArchive => "compiler.build.productionServerArchive",
            CompilerTarget.DotNetAssembly => "compiler.build.dotNETAssembly",
            CompilerTarget.PythonPackage => "compiler.build.pythonPackage",
            CompilerTarget.CppSharedLibrary => "compiler.build.cppSharedLibrary",
            CompilerTarget.CSharedLibrary => "compiler.build.cSharedLibrary",
            CompilerTarget.ExcelAddIn => "compiler.build.excelAddIn",
            _ => throw new ArgumentOutOfRangeException(nameof(task)),
        };
    }

    /// <summary>
    /// A path as the script writes it: <c>fullfile(projectRoot, "rel")</c> under the project root,
    /// else the full path as a string.
    /// </summary>
    public static string PathExpression(string path, string? root)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0)
        {
            return Quoted("");
        }

        string full = Path.GetFullPath(path);
        if (root is not null)
        {
            string relative = Path.GetRelativePath(root, full);
            if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return $"fullfile({ProjectRootVariable}, {string.Join(", ", parts.Select(Quoted))})";
            }

            if (relative == ".")
            {
                return ProjectRootVariable;
            }
        }

        return Quoted(full);
    }

    /// <summary>A MATLAB string literal: double quotes, a quote doubled.</summary>
    public static string Quoted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
