namespace JGraph.Deploy.Projects;

/// <summary>What the upgrade of a legacy project left: the MATLAB project, its task and the warnings.</summary>
public sealed class LegacyProjectUpgradeResult
{
    internal LegacyProjectUpgradeResult(MatlabProject project, CompilerTaskConfiguration? task, string backupFile, IReadOnlyList<string> fileWarnings, IReadOnlyList<string> unsupportedWarnings)
    {
        Project = project;
        Task = task;
        BackupFile = backupFile;
        FileWarnings = fileWarnings;
        UnsupportedWarnings = unsupportedWarnings;
    }

    /// <summary>The MATLAB project the legacy file's folder became (or already was).</summary>
    public MatlabProject Project { get; }

    /// <summary>The compiler task carrying the legacy settings; null for a Hadoop project, which R2025b turns into nothing.</summary>
    public CompilerTaskConfiguration? Task { get; }

    /// <summary>Where the legacy file went: <c>&lt;file&gt;.bak</c>.</summary>
    public string BackupFile { get; }

    /// <summary>Files the legacy project named that do not exist, in R2025b's words.</summary>
    public IReadOnlyList<string> FileWarnings { get; }

    /// <summary>Settings R2025b no longer supports, in its words: mcc options, the type safe API, the Excel client.</summary>
    public IReadOnlyList<string> UnsupportedWarnings { get; }
}

/// <summary>
/// The upgrade of a legacy deployment project into a MATLAB project with a compiler task (compiler
/// and SDK plan, stage K21), as R2025b's <c>matlab.internal.deployment.createProjectModelFromPRJ</c>
/// does it when <c>deploytool</c>, the legacy apps or <c>open</c> are given one (probe
/// <c>k21_legacy</c>, <c>upgrade.*</c>): the legacy file is renamed <c>.bak</c>, its folder becomes
/// a project named after the file (or the project already there takes the task), every file under
/// the folder joins the project, and a task named "&lt;project&gt; &lt;kind&gt;" carries the
/// settings the legacy parameters mean, its files as labels.
/// </summary>
public static class LegacyProjectUpgrade
{
    /// <summary>The words R2025b puts after the project's name in the task's name, per kind.</summary>
    public static string TaskSuffix(LegacyTarget target) => target switch
    {
        LegacyTarget.StandaloneApplication => "Standalone",
        LegacyTarget.CSharedLibrary => "C Shared Library",
        LegacyTarget.CppSharedLibrary => "CPP Shared Library",
        LegacyTarget.DotNetAssembly => "NET Assembly",
        LegacyTarget.JavaPackage => "Java Package",
        LegacyTarget.PythonPackage => "Python Package",
        LegacyTarget.ComComponent => "COM Component",
        LegacyTarget.ExcelAddIn => "Excel AddIn",
        LegacyTarget.ProductionServerArchive or LegacyTarget.ExcelClientForProductionServer => "Deployable Archive",
        LegacyTarget.WebAppArchive => "Web App",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>The task type (stage K20's <see cref="CompilerTaskKinds.Types"/>) a kind upgrades to.</summary>
    public static string TaskType(LegacyTarget target) => target switch
    {
        LegacyTarget.StandaloneApplication => "StandaloneDesktopApp",
        LegacyTarget.CSharedLibrary => "CSharedLib",
        LegacyTarget.CppSharedLibrary => "CppSharedLib",
        LegacyTarget.DotNetAssembly => "DotNETAssembly",
        LegacyTarget.JavaPackage => "JavaPackage",
        LegacyTarget.PythonPackage => "PythonPackage",
        LegacyTarget.ComComponent => "COMComponent",
        LegacyTarget.ExcelAddIn => "ExcelAddIn",
        LegacyTarget.ProductionServerArchive or LegacyTarget.ExcelClientForProductionServer => "ProductionServerArchive",
        LegacyTarget.WebAppArchive => "WebAppArchive",
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>R2025b's warning for an Excel client project, which it turns into an archive.</summary>
    public const string ExcelClientWarning = "Building an Excel client for Production Server is no longer supported using a project.  The project has been updated to build a deployable archive for Production Server.";

    /// <summary>R2025b's warning for a class-based project with more than one class: the task takes the first.</summary>
    public const string MultipleClassesWarning = "Multiple classes are no longer supported.  The first class name will be used.";

    /// <summary>R2025b's warning for a .NET project that asked for the type safe API.</summary>
    public const string TypeSafeWarning = "Building .NET Assemblies with the type safe API is no longer supported.  The assembly will not be built with a type safe interface.  It is recommended to use strong types with the data array interface instead.";

    /// <summary>
    /// The kind a legacy project upgrades to: by its sub-target, and for an Application Compiler
    /// project with an unknown sub-target a standalone application (R2025b upgrades it as one);
    /// null for a Hadoop project, a Library or Production Server Compiler project with an unknown
    /// sub-target (R2025b upgrades those into nothing), or one of no known kind.
    /// </summary>
    public static LegacyTarget? TargetOf(LegacyDeploymentProject legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        if (legacy.Target == LegacyDeploymentProject.HadoopTarget || legacy.SubTarget == "subtarget.hadoop")
        {
            return null;
        }

        if (LegacyTaskSettings.From(legacy) is { } settings)
        {
            return settings.Target;
        }

        return legacy.Target == LegacyDeploymentProject.StandaloneTarget ? LegacyTarget.StandaloneApplication : null;
    }

    /// <summary>
    /// Upgrades <paramref name="legacy"/>: the file becomes <c>.bak</c>, its folder a project with
    /// every file in it, and the task carries the settings. A Hadoop project is left as it is with
    /// a null task.
    /// </summary>
    public static LegacyProjectUpgradeResult? Upgrade(LegacyDeploymentProject legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        bool compilerProject = legacy.Target is LegacyDeploymentProject.StandaloneTarget or LegacyDeploymentProject.LibraryTarget
            or LegacyDeploymentProject.ProductionServerTarget or LegacyDeploymentProject.WebAppTarget;
        if (!compilerProject || legacy.SubTarget == "subtarget.hadoop")
        {
            return null;
        }

        // A Library or Production Server Compiler project with a sub-target R2025b does not know still
        // becomes a project of its files (the file renamed .bak), with no task.
        LegacyTarget? kind = TargetOf(legacy);
        string backup = legacy.File + ".bak";
        File.Copy(legacy.File, backup, overwrite: true);
        File.Delete(legacy.File);

        string folder = legacy.Folder;
        string projectName = Path.GetFileNameWithoutExtension(legacy.File);
        MatlabProject project = ProjectDefinitionFiles.IsProjectRoot(folder) ? MatlabProject.Open(folder) : MatlabProject.Create(folder, projectName);
        AddEveryFile(project);

        var fileWarnings = new List<string>();
        var unsupported = new List<string>();
        if (kind is not { } target)
        {
            project.Save();
            return new LegacyProjectUpgradeResult(project, null, backup, fileWarnings, unsupported);
        }

        string taskName = $"{project.Name} {TaskSuffix(target)}";
        CompilerTaskConfiguration task = project.GetOrCreateTask(TaskType(target), taskName);
        LegacyTaskSettings settings = LegacyTaskSettings.From(legacy, target);
        {
            if (settings.UnsupportedMccOptions.Count > 0)
            {
                unsupported.Add("The following additional MCC options are not supported: " + string.Join(" ", settings.UnsupportedMccOptions));
            }

            if (settings.Target == LegacyTarget.DotNetAssembly && settings.TypeSafeApi)
            {
                unsupported.Add(TypeSafeWarning);
            }

            if (settings.Target == LegacyTarget.ExcelClientForProductionServer)
            {
                unsupported.Add(ExcelClientWarning);
            }

            if (settings.NeedsClasses && settings.Classes.Count > 1)
            {
                unsupported.Add(MultipleClassesWarning);
            }

            Apply(task, settings, fileWarnings);
        }

        project.Save();
        return new LegacyProjectUpgradeResult(project, task, backup, fileWarnings, unsupported);
    }

    /// <summary>Adds every file under the root but the definition files and the project's own file.</summary>
    private static void AddEveryFile(MatlabProject project)
    {
        string definitions = ProjectDefinitionFiles.DefinitionFolder(project.RootFolder);
        foreach (string file in Directory.EnumerateFiles(project.RootFolder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (file.StartsWith(definitions + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file, project.ProjectFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = project.RelativePath(file) ?? "";
            if (relative.Length > 0 && project.FindFile(relative) is null)
            {
                project.AddFile(relative);
            }
        }
    }

    /// <summary>Carries the settings into the task: the texts and flags by name, the files as labels.</summary>
    private static void Apply(CompilerTaskConfiguration task, LegacyTaskSettings s, List<string> fileWarnings)
    {
        MatlabProject project = task.Project;
        void Text(string name, string value)
        {
            if (task.Kind.Property(name) is { } p && task.GetText(p) != value)
            {
                task.SetText(p, value);
            }
        }

        void Flag(string name, bool value)
        {
            if (task.Kind.Property(name) is { } p && task.GetFlag(p) != value)
            {
                task.SetFlag(p, value);
            }
        }

        void Files(string labelKey, IReadOnlyList<string> files)
        {
            if (!task.Kind.Labels.Any(l => l.Key == labelKey))
            {
                return;
            }

            var present = new List<string>();
            foreach (string file in files)
            {
                if (File.Exists(file) && project.IsUnderRoot(file))
                {
                    present.Add(file);
                }
                else
                {
                    fileWarnings.Add($"{file} could not be added to the task because it does not exist.");
                }
            }

            task.SetLabelledFiles(labelKey, present);
        }

        void OneFile(string labelKey, string file)
        {
            if (file.Length > 0)
            {
                Files(labelKey, [file]);
            }
        }

        string Relative(string folder)
        {
            if (folder.Length == 0)
            {
                return "";
            }

            string? relative = project.RelativePath(folder);
            return (relative is { Length: > 0 } ? relative : folder).Replace('\\', '/');
        }

        switch (s.Target)
        {
            case LegacyTarget.StandaloneApplication:
                Text("ApplicationName", s.AppName);
                Text("ApplicationVersion", s.Version);
                Text("ExecutableName", s.AppName);
                Flag("SuppressWindowsCmdPrompt", s.WindowsApplication);
                Flag("TreatInputsAsNumeric", s.TreatInputsAsNumeric);
                Flag("EmbedArchive", s.EmbedArchive);
                Text("ExampleText", s.ExampleText);
                Text("HelpText", s.HelpText);
                OneFile("MainFile", s.MainFile);
                OneFile("ExeIconFile", s.Icon);
                OneFile("ExeSplashImageFile", s.Splash);
                break;
            case LegacyTarget.CSharedLibrary:
            case LegacyTarget.CppSharedLibrary:
                Text("LibraryName", s.BuildName);
                Text("LibraryVersion", s.Version);
                Flag("EmbedArchive", s.EmbedArchive);
                Text("Interface", s.CppApi == "mwarray" ? "mwarray" : "matlabdata");
                break;
            case LegacyTarget.DotNetAssembly:
                Text("AssemblyName", s.BuildName);
                Text("AssemblyVersion", s.Version);
                Text("ClassName", s.ClassName);
                Flag("EmbedArchive", s.EmbedArchive);
                Flag("EnableRemoting", s.EnableRemoting);
                Text("FrameworkVersion", s.FrameworkVersion);
                Text("Interface", "mwarray"); // a legacy .NET assembly's API is the MWArray one
                OneFile("StrongKeyFile", s.StrongNameKeyFile);
                break;
            case LegacyTarget.PythonPackage:
                Text("PackageName", s.BuildName);
                break;
            case LegacyTarget.ExcelAddIn:
                Text("Name", s.AppName);
                Text("AddInName", s.AppName);
                Text("AddInVersion", s.Version);
                Text("ClassName", s.ClassName);
                Flag("EmbedArchive", s.EmbedArchive);
                Flag("GenerateVisualBasicFile", true);
                break;
            case LegacyTarget.ProductionServerArchive:
            case LegacyTarget.ExcelClientForProductionServer:
                Text("ArchiveName", s.AppName);
                break;
        }

        Text("AuthorName", s.AuthorName);
        Text("AuthorEmail", s.AuthorEmail);
        Text("AuthorOrganization", s.AuthorCompany);
        Text("Summary", s.Summary);
        Text("Description", s.Description);
        Text("BuildOutputFolder", Relative(s.OutputFolder));
        Files("AdditionalFiles", s.AdditionalFiles);
        if (!s.HasMainFile)
        {
            Files("ExportedFunctionFile", s.ExportedFunctions);
            if (s.Target is LegacyTarget.CppSharedLibrary or LegacyTarget.DotNetAssembly or LegacyTarget.PythonPackage or LegacyTarget.JavaPackage)
            {
                Files("SamplesFile", s.SampleFiles); // the kinds whose builders generate samples; a C library's or add-in's examples are left unlabelled, as R2025b leaves them
            }

            // The documentation file set is left unlabelled too (R2025b's upgrade drops it: probe k21_legacy, upgrade.k21c.project.labels).
        }

        if (task.Kind.HasInstaller)
        {
            Text("PackageOutputFolder", Relative(s.PackageFolder));
            Text("InstallationSubFolder", s.InstallSubfolder.Replace('\\', '/'));
            Text("RuntimeInstallType", s.RuntimeDelivery switch
            {
                RuntimeDeliveryChoice.Installer => "installer",
                RuntimeDeliveryChoice.None => "NONE",
                _ => "web",
            });
            Text("InstallerName", s.InstallerName);
            Text("InstallerVersion", s.Version);
            Text("InstallerNotes", s.InstallNotes);
            Files("InstallerAdditionalFiles", s.InstallerAdditionalFiles);
            OneFile("InstallerIconFile", s.Icon);
            OneFile("InstalerLogoFile", s.Logo);
            OneFile("InstallerSplashFile", s.Splash);
        }
    }

}
