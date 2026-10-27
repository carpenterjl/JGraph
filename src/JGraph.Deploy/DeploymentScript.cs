using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The <c>compiler.build</c> script a compiler task stands for (compiler and SDK plan, stage K19):
/// what the app's Code View shows, what Build and Build and Package run, and what Create Build
/// Script saves. Its shape is R2025b's deployment script (<c>createDeploymentScript</c>, the share
/// window's <c>AppDesignerDeployScriptBuilder</c>): the project root, the build options object set
/// property by property and built, then the package options object and the installer, under the
/// <c>compiler_ui_common:deploymentscript</c> catalog's comments. Only what differs from the options'
/// defaults is set, so the script reads as the task does.
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
        text.Append("% Set the path to the project root directory.\n");
        text.Append($"{ProjectRootVariable} = {Quoted(root ?? "")};\n\n");
        text.Append("% Create target build options object, set build properties and build.\n");
        AppendBuild(text, task, root);
        if (package && CompilerTargets.CanPackage(task.Target))
        {
            text.Append('\n');
            AppendPackage(text, task, root);
        }

        return text.ToString();
    }

    /// <summary>
    /// The installer's part alone, over the <c>buildResults</c> a build left in the workspace: what
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

    private static void AppendBuild(StringBuilder text, CompilerTaskModel task, string? root)
    {
        string files = task.SourceFiles.Count == 1
            ? PathExpression(task.SourceFiles[0], root)
            : "[" + string.Join(", ", task.SourceFiles.Select(f => PathExpression(f, root))) + "]";
        string name = task.EffectiveName;
        switch (task.Target)
        {
            case CompilerTarget.StandaloneApplication:
                text.Append($"buildOpts = compiler.build.StandaloneApplicationOptions({files});\n");
                if (task.ExecutableName.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableName = {Quoted(task.ExecutableName)};\n");
                }

                if (task.Version.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableVersion = {Quoted(task.Version)};\n");
                }

                if (task.ExecutableIcon.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableIcon = {PathExpression(task.ExecutableIcon, root)};\n");
                }

                if (task.ExecutableSplashScreen.Length > 0)
                {
                    text.Append($"buildOpts.ExecutableSplashScreen = {PathExpression(task.ExecutableSplashScreen, root)};\n");
                }

                if (task.TreatInputsAsNumeric)
                {
                    text.Append("buildOpts.TreatInputsAsNumeric = true;\n");
                }

                break;
            case CompilerTarget.ProductionServerArchive:
                text.Append($"buildOpts = compiler.build.ProductionServerArchiveOptions({files});\n");
                text.Append($"buildOpts.ArchiveName = {Quoted(name)};\n");
                if (task.FunctionSignatures.Length > 0)
                {
                    text.Append($"buildOpts.FunctionSignatures = {PathExpression(task.FunctionSignatures, root)};\n");
                }

                break;
            case CompilerTarget.DotNetAssembly:
                text.Append($"buildOpts = compiler.build.DotNETAssemblyOptions({files});\n");
                text.Append($"buildOpts.AssemblyName = {Quoted(name)};\n");
                text.Append($"buildOpts.Interface = {Quoted(task.Interface)};\n");
                if (task.Version.Length > 0)
                {
                    if (task.Interface == "matlab-data")
                    {
                        text.Append("% \"AssemblyVersion\" will be ignored when using the 'matlab-data' api.\n");
                    }

                    text.Append($"buildOpts.AssemblyVersion = {Quoted(task.Version)};\n");
                }

                break;
            case CompilerTarget.PythonPackage:
                text.Append($"buildOpts = compiler.build.PythonPackageOptions({files});\n");
                text.Append($"buildOpts.PackageName = {Quoted(name)};\n");
                break;
            case CompilerTarget.CppSharedLibrary:
                text.Append($"buildOpts = compiler.build.CppSharedLibraryOptions({files});\n");
                text.Append($"buildOpts.LibraryName = {Quoted(name)};\n");
                text.Append($"buildOpts.Interface = {Quoted(task.Interface)};\n");
                if (task.Version.Length > 0)
                {
                    if (task.Interface == "matlab-data")
                    {
                        text.Append("% \"LibraryVersion\" will be ignored when using the 'matlab-data' api.\n");
                    }

                    text.Append($"buildOpts.LibraryVersion = {Quoted(task.Version)};\n");
                }

                break;
            case CompilerTarget.CSharedLibrary:
                text.Append($"buildOpts = compiler.build.CSharedLibraryOptions({files});\n");
                text.Append($"buildOpts.LibraryName = {Quoted(name)};\n");
                if (task.Version.Length > 0)
                {
                    text.Append($"buildOpts.LibraryVersion = {Quoted(task.Version)};\n");
                }

                break;
            case CompilerTarget.ExcelAddIn:
                text.Append($"buildOpts = compiler.build.ExcelAddInOptions({files});\n");
                text.Append($"buildOpts.AddInName = {Quoted(name)};\n");
                if (task.ClassName.Length > 0)
                {
                    text.Append($"buildOpts.ClassName = {Quoted(task.ClassName)};\n");
                }

                if (task.Version.Length > 0)
                {
                    text.Append($"buildOpts.AddInVersion = {Quoted(task.Version)};\n");
                }

                break;
        }

        if (task.AdditionalFiles.Count > 0)
        {
            text.Append($"buildOpts.AdditionalFiles = [{string.Join(", ", task.AdditionalFiles.Select(f => PathExpression(f, root)))}];\n");
        }

        if (!task.AutoDetectDataFiles)
        {
            text.Append("buildOpts.AutoDetectDataFiles = false;\n");
        }

        if (task.Obfuscate)
        {
            text.Append("buildOpts.ObfuscateArchive = true;\n");
        }

        text.Append($"buildOpts.OutputDir = {PathExpression(task.EffectiveOutputFolder, root)};\n");
        text.Append("buildOpts.Verbose = true;\n");
        text.Append($"buildResults = {BuildFunction(task)}(buildOpts);\n");
    }

    private static void AppendPackage(StringBuilder text, CompilerTaskModel task, string? root)
    {
        if (task.RuntimeDelivery == RuntimeDeliveryChoice.Installer)
        {
            text.Append("% Download the MATLAB Runtime to include in the installer.\n");
            text.Append("compiler.runtime.download;\n\n");
        }

        text.Append("% Create package options object, set package properties and package.\n");
        text.Append("packageOpts = compiler.package.InstallerOptions(buildResults);\n");
        text.Append($"packageOpts.ApplicationName = {Quoted(task.EffectiveName)};\n");
        if (task.Version.Length > 0)
        {
            text.Append($"packageOpts.Version = {Quoted(task.Version)};\n");
        }

        foreach ((string property, string value) in new[]
        {
            ("AuthorName", task.AuthorName), ("AuthorCompany", task.CompanyName), ("AuthorEmail", task.Email),
            ("Summary", task.Summary), ("Description", task.Description), ("InstallationNotes", task.InstallerNotes),
            ("InstallerName", task.InstallerName),
        })
        {
            if (value.Length > 0)
            {
                text.Append($"packageOpts.{property} = {Quoted(value)};\n");
            }
        }

        if (task.RuntimeDelivery != RuntimeDeliveryChoice.Web)
        {
            text.Append($"packageOpts.RuntimeDelivery = {Quoted(task.RuntimeDelivery.ToString().ToLowerInvariant())};\n");
        }

        foreach ((string property, string value) in new[]
        {
            ("InstallerIcon", task.InstallerIcon), ("InstallerSplash", task.InstallerSplash), ("InstallerLogo", task.InstallerLogo),
        })
        {
            if (value.Length > 0)
            {
                text.Append($"packageOpts.{property} = {PathExpression(value, root)};\n");
            }
        }

        if (task.InstallerAdditionalFiles.Count > 0)
        {
            text.Append($"packageOpts.AdditionalFiles = [{string.Join(", ", task.InstallerAdditionalFiles.Select(f => PathExpression(f, root)))}];\n");
        }

        text.Append($"packageOpts.DefaultInstallationDir = {Quoted(task.DefaultInstallationDir)};\n");
        text.Append($"packageOpts.OutputDir = {PathExpression(task.EffectivePackageFolder, root)};\n");
        text.Append("packageOpts.Verbose = true;\n");
        text.Append("compiler.package.installer(buildResults, \"Options\", packageOpts);\n");
    }

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
