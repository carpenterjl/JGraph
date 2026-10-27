using JGraph.Api;
using JGraph.Deploy;
using JGraph.Deploy.Projects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// MATLAB Projects and compiler tasks (compiler and SDK plan, stage K20): the definition files as
/// R2025b writes them (probe <c>k20_projects</c>, the <c>id.*</c> and <c>tree.*</c> lines), a project's
/// operations round-tripping through its files, a task's properties, labels and script, and the
/// session's commands over them.
/// </summary>
[Collection("JG facade")]
public sealed class ProjectTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly Apps _apps = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-projects-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _session = Session(_apps);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private IScriptSession Session(IScriptCompilerApps? apps) =>
        ((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _folder, null) { CompilerApps = apps });

    private sealed class Apps : IScriptCompilerApps
    {
        public List<(CompilerTarget? Target, string? File)> Opened { get; } = [];

        public List<(string Root, CompilerTarget? Target)> Managed { get; } = [];

        public string? Open(CompilerTarget? target, string? mainFile, CompilerAppServices services)
        {
            Opened.Add((target, mainFile));
            return null;
        }

        public string? OpenTaskManager(MatlabProject project, CompilerTarget? preferred, CompilerAppServices services)
        {
            Managed.Add((project.RootFolder, preferred));
            return null;
        }
    }

    /// <summary>The identifier and message a statement fails with, or "ok", and the warning it raised.</summary>
    private async Task<string> Outcome(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(
            $"lastwarn(''); try, {statement}; disp('ok'), catch e, disp([e.identifier ' | ' strrep(e.message, newline, ' / ')]), end; [m, id] = lastwarn; if ~isempty(id), disp(['warn ' id]), end",
            string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private async Task<string> Printed(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(statement, string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private string Root(string name) => Path.Combine(_folder, name);

    private static string Definition(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(root, "resources", "project", Path.Combine(parts)));

    /// <summary>
    /// The definition files land where MATLAB's would: each node's name is the base64url SHA-1 of its
    /// location and type after its ancestors' (the recording's id.* lines), the root containers
    /// carry a p file alone, and every file is written as MATLAB writes it.
    /// </summary>
    [Fact]
    public void TheDefinitionFilesCarryR2025bsNames()
    {
        Assert.Equal("qaw0eS1zuuY1ar9TdPn1GMfrjbQ", ProjectDefinitionFiles.NodeId([("Root", "Files")]));
        Assert.Equal("NlS6DTIym__tSzNJvG8uNdsbVgE", ProjectDefinitionFiles.NodeId([("Root", "Files"), ("hello.m", "File")]));
        Assert.Equal("nYaYewgdlCVn4mYl0oyX2aTGc8k", ProjectDefinitionFiles.NodeId([("Root", "Files"), ("lib", "File"), ("helper.m", "File")]));
        Assert.Equal("_HcY0TsBOX4FgeAtNkCFGUGbvlE", ProjectDefinitionFiles.NodeId([("Root", "Files"), ("lib", "File"), ("1", "DIR_SIGNIFIER")]));
        Assert.Equal("2kj09UetkV_lru3gvSPXnY6-nM4", ProjectDefinitionFiles.NodeId([("Root", "Categories"), ("FileClassCategory", "Category"), ("test", "Label")]));
        Assert.Equal("LgmFbt-_rsQuN5yOnVKpHxrQ7vY", ProjectDefinitionFiles.NodeId([("Root", "Files"), ("hello.m", "File"), ("Data", "LabelData")]));

        string root = Root("named");
        MatlabProject project = MatlabProject.Create(root, "k20named");
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<MATLABProject xmlns=\"http://www.mathworks.com/MATLABProjectFile\"/>\n", File.ReadAllText(Path.Combine(root, "k20named.prj")));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info MetadataType=\"fixedPathV2\"/>", Definition(root, "Project.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info/>", Definition(root, "rootp.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info location=\"ProjectData\" type=\"Info\"/>", Definition(root, "root", "GiiBklLgTxteCEmomM8RCvWT0nQp.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info Name=\"k20named\"/>", Definition(root, "root", "GiiBklLgTxteCEmomM8RCvWT0nQd.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info location=\"Root\" type=\"Categories\"/>", Definition(root, "root", "fjRQtWiSIy7hIlj-Kmk87M7s21kp.xml"));
        Assert.False(File.Exists(Path.Combine(root, "resources", "project", "root", "fjRQtWiSIy7hIlj-Kmk87M7s21kd.xml")), "a root container has no d file");
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info DataType=\"None\" Name=\"Classification\" ReadOnly=\"1\" SingleValued=\"1\"/>",
            Definition(root, "fjRQtWiSIy7hIlj-Kmk87M7s21k", "NjSPEMsIuLUyIpr2u1Js5bVPsOsd.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info Name=\"Test\" ReadOnly=\"READ_ONLY\"/>",
            Definition(root, "NjSPEMsIuLUyIpr2u1Js5bVPsOs", "2kj09UetkV_lru3gvSPXnY6-nM4d.xml"));
        Assert.Equal(7, Directory.GetFiles(Path.Combine(root, "resources", "project", "NjSPEMsIuLUyIpr2u1Js5bVPsOs"), "*d.xml").Length);

        // A file gets the Design label as MATLAB labels one; a folder carries its signifier.
        File.WriteAllText(Path.Combine(root, "hello.m"), "function hello\nend\n");
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "lib", "helper.m"), "function helper\nend\n");
        project.AddFile("hello.m");
        project.AddFile(Path.Combine("lib", "helper.m"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info>\n    <Category UUID=\"FileClassCategory\">\n        <Label UUID=\"design\"/>\n    </Category>\n</Info>",
            Definition(root, "qaw0eS1zuuY1ar9TdPn1GMfrjbQ", "NlS6DTIym__tSzNJvG8uNdsbVgEd.xml"));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Info location=\"1\" type=\"DIR_SIGNIFIER\"/>",
            Definition(root, "rLJPpozaK1cVMvxoan_hMvTadXY", "_HcY0TsBOX4FgeAtNkCFGUGbvlEp.xml"));
        Assert.True(File.Exists(Path.Combine(root, "resources", "project", "rLJPpozaK1cVMvxoan_hMvTadXY", "nYaYewgdlCVn4mYl0oyX2aTGc8kd.xml")));
        Assert.True(ProjectDefinitionFiles.IsProjectRoot(root));
        Assert.True(MatlabProject.Exists(Path.Combine(root, "k20named.prj")));
    }

    /// <summary>Everything a project holds comes back from its files: names, files, labels, path, entry points, tasks.</summary>
    [Fact]
    public void AProjectRoundTripsThroughItsFiles()
    {
        string root = Root("round");
        MatlabProject project = MatlabProject.Create(root);
        Assert.Equal("blank_project", project.Name);
        project.Name = "k20proj";
        project.Description = "A K20 project";
        File.WriteAllText(Path.Combine(root, "hello.m"), "function y = hello(x)\ny = x + 1;\nend\n");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "vals.txt"), "1 2 3\n");
        File.WriteAllText(Path.Combine(root, "start.m"), "disp('started')\n");
        project.AddFile("hello.m");
        project.AddFile(Path.Combine("data", "vals.txt"));
        project.AddFile("start.m");
        ProjectCategory owners = project.CreateCategory("Owners", "char");
        project.AddLabel("hello.m", "Owners", "Jacob", "me");
        project.AddLabel(Path.Combine("data", "vals.txt"), "Classification", "Requirement");
        project.AddPath(Path.Combine(root, "data"));
        project.AddStartupFile(Path.Combine(root, "start.m"));
        project.AddShortcut(Path.Combine(root, "hello.m"));
        CompilerTaskConfiguration task = project.GetOrCreateTask("StandaloneDesktopApp", "myApp");
        task.SetLabelledFile("MainFile", Path.Combine(root, "hello.m"));
        task.SetText(task.Kind.Property("AuthorName")!, "Jacob");
        task.SetFlag(task.Kind.Property("TreatInputsAsNumeric")!, true);
        task.SetLabelledFiles("AdditionalFiles", [Path.Combine(root, "data", "vals.txt")]);

        MatlabProject again = MatlabProject.Open(Path.Combine(root, "blank_project.prj"));
        Assert.Equal("k20proj", again.Name);
        Assert.Equal("A K20 project", again.Description);
        Assert.Equal(["data", Path.Combine("data", "vals.txt"), "hello.m", "start.m"], again.Files.Select(f => f.RelativePath));
        Assert.True(again.Files[0].IsFolder);
        Assert.Equal(["Classification:Design", "Owners:Jacob", "Standalone Application - myApp:Main Function"], again.LabelsOf("hello.m").Select(l => l.CategoryName + ":" + l.LabelName));
        Assert.Equal("me", again.LabelsOf("hello.m").Single(l => l.LabelName == "Jacob").Data);
        Assert.Equal(["Classification", "Owners", "Standalone Application - myApp"], again.Categories.Select(c => c.Name));
        Assert.Equal("char", again.FindCategory("Owners")!.DataType);
        Assert.Equal(["Artifact", "Convenience", "Derived", "Design", "None", "Other", "Requirement", "Test"], again.FindCategory("Classification")!.Labels.Select(l => l.Name));
        Assert.Equal(["data"], again.PathFolders);
        Assert.Equal(["start.m"], again.StartupFiles);
        Assert.Equal(["hello"], again.Shortcuts.Select(s => s.Name));
        CompilerTaskConfiguration found = again.FindTask("myApp")!;
        Assert.Equal("StandaloneDesktopApp", found.Type);
        Assert.Equal(Path.Combine(root, "hello.m"), found.LabelledFile("MainFile"));
        Assert.Equal("Jacob", found.GetText(found.Kind.Property("AuthorName")!));
        Assert.True(found.GetFlag(found.Kind.Property("TreatInputsAsNumeric")!));
        Assert.Equal([Path.Combine(root, "data", "vals.txt")], found.LabelledFiles("AdditionalFiles"));
        Assert.Equal(Path.Combine("myApp", "output", "build"), found.GetText(found.Kind.Property("BuildOutputFolder")!));

        // A set is checked as R2025b checks it, a file not in the project refused.
        ProjectException refused = Assert.Throws<ProjectException>(() => found.SetLabelledFile("MainFile", Path.Combine(root, "nope.m")));
        Assert.Equal("deployment:exception:FILE_NOT_IN_PROJECT", refused.Identifier);
        Assert.Throws<ProjectException>(() => CompilerTaskConfiguration.CheckName("my/app"));
        Assert.Throws<ProjectException>(() => again.GetOrCreateTask("ProductionServerArchive", "myApp"));

        // Rename and remove, through the map, the display name, the category and the default folders.
        again.RenameTask("myApp", "myRenamed");
        Assert.Null(again.FindTask("myApp"));
        CompilerTaskConfiguration renamed = again.FindTask("myRenamed")!;
        Assert.Equal(Path.Combine("myRenamed", "output", "build"), renamed.GetText(renamed.Kind.Property("BuildOutputFolder")!));
        Assert.Equal(Path.Combine(root, "hello.m"), renamed.LabelledFile("MainFile"));
        Assert.Contains("Standalone Application - myRenamed", again.Categories.Select(c => c.Name));
        again.RemoveTask("myRenamed");
        Assert.Empty(again.Tasks);
        Assert.DoesNotContain(again.Categories, c => c.Name.StartsWith("Standalone", StringComparison.Ordinal));
        Assert.Equal(["Classification:Design", "Owners:Jacob"], again.LabelsOf("hello.m").Select(l => l.CategoryName + ":" + l.LabelName));

        // Removing the project keeps the user's files.
        MatlabProject.Delete(root);
        Assert.False(ProjectDefinitionFiles.IsProjectRoot(root));
        Assert.True(File.Exists(Path.Combine(root, "hello.m")));
        Assert.False(Directory.Exists(Path.Combine(root, "resources")));
        Assert.Equal("MATLAB:project:management:DeleteFailNotAProject", Assert.Throws<ProjectException>(() => MatlabProject.Delete(root)).Identifier);
        _ = owners;
    }

    /// <summary>A task's script is R2025b's deployment script for it (probe k20_projects, script.fresh.main.MCRInstaller), bar the measured slip (ADR 0275).</summary>
    [Fact]
    public void ATasksScriptIsR2025bs()
    {
        string root = Root("script");
        MatlabProject project = MatlabProject.Create(root);
        File.WriteAllText(Path.Combine(root, "hello.m"), "function y = hello(x)\ny = x + 1;\nend\n");
        project.AddFile("hello.m");
        CompilerTaskConfiguration fresh = project.GetOrCreateTask("StandaloneDesktopApp", "myFresh");
        fresh.SetLabelledFile("MainFile", Path.Combine(root, "hello.m"));
        CompilerTaskModel task = fresh.ToTaskModel()!;
        Assert.Equal(Path.Combine(root, "myFresh", "output", "build"), task.EffectiveOutputFolder);
        string expected = string.Join("\n",
        [
            $"projectRoot = \"{root}\";",
            "",
            "% Create target build options object, set build properties and build.",
            "buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(projectRoot, \"hello.m\"));",
            "buildOpts.AutoDetectDataFiles = true;",
            "buildOpts.OutputDir = fullfile(projectRoot, \"myFresh\", \"output\", \"build\");",
            "buildOpts.ObfuscateArchive = false;",
            "buildOpts.Verbose = true;",
            "buildOpts.EmbedArchive = true;",
            "buildOpts.ExecutableName = \"MyDesktopApplication\";",
            "buildOpts.ExecutableVersion = \"1.0.0\";",
            "buildOpts.TreatInputsAsNumeric = false;",
            "buildResult = compiler.build.standaloneApplication(buildOpts);", // R2025b writes standaloneWindowsApplication for a task that keeps the prompt (ADR 0275)
            "",
            "",
            "% Create package options object, set package properties and package.",
            "packageOpts = compiler.package.InstallerOptions(buildResult);",
            "packageOpts.ApplicationName = \"My Desktop Application\";",
            "packageOpts.OutputDir = fullfile(projectRoot, \"myFresh\", \"output\", \"package\");",
            "packageOpts.Verbose = true;",
            "compiler.package.installer(buildResult, \"Options\", packageOpts);",
            "",
        ]);
        Assert.Equal(expected, DeploymentScript.Generate(task, package: true));

        // The window's model writes back through the task: a change lands in the project's files.
        task.AuthorName = "Jacob";
        task.WindowsApplication = true;
        task.RuntimeDelivery = RuntimeDeliveryChoice.Installer;
        task.InstallSubfolder = "K20Sub";
        fresh.ApplyTaskModel(task);
        CompilerTaskConfiguration reread = MatlabProject.Open(root).FindTask("myFresh")!;
        Assert.Equal("Jacob", reread.GetText(reread.Kind.Property("AuthorName")!));
        Assert.True(reread.GetFlag(reread.Kind.Property("SuppressWindowsCmdPrompt")!));
        Assert.Equal("installer", reread.GetText(reread.Kind.Property("RuntimeInstallType")!));
        string script = DeploymentScript.Generate(reread.ToTaskModel()!, package: true);
        Assert.Contains("buildResult = compiler.build.standaloneWindowsApplication(buildOpts);", script);
        Assert.Contains("packageOpts.DefaultInstallationDir = \"K20Sub\";\npackageOpts.OutputDir = fullfile(projectRoot, \"myFresh\", \"output\", \"package\");\npackageOpts.RuntimeDelivery = \"installer\";\npackageOpts.Verbose = true;", script);

        // A library task: the exported functions, the .NET class map, the libraries' own lines.
        CompilerTaskConfiguration net = project.GetOrCreateTask("DotNETAssembly", "myNet");
        net.SetLabelledFiles("ExportedFunctionFile", [Path.Combine(root, "hello.m")]);
        net.SetText(net.Kind.Property("Interface")!, "mwarray");
        string netScript = DeploymentScript.Generate(net.ToTaskModel()!, package: false);
        Assert.Contains("classmap = containers.Map;\nclassmap(\"Class1\") = fullfile(projectRoot, \"hello.m\");\nbuildOpts = compiler.build.DotNETAssemblyOptions(classmap);", netScript);
        Assert.Contains("buildOpts.AssemblyName = \"MyDotNetAssembly\";\nbuildOpts.AssemblyVersion = \"1.0.0\";\nbuildOpts.DebugBuild = false;\nbuildOpts.EmbedArchive = \"on\";\nbuildOpts.Interface = \"mwarray\";\nbuildOpts.EnableRemoting = false;\nbuildOpts.FrameworkVersion = \"4.0\";\nbuildResult = compiler.build.dotNETAssembly(buildOpts);", netScript);
        CompilerTaskConfiguration archive = project.GetOrCreateTask("ProductionServerArchive", "myPSA");
        archive.SetLabelledFiles("ExportedFunctionFile", [Path.Combine(root, "hello.m")]);
        Assert.EndsWith("buildOpts.ArchiveName = \"MyProductionServerArchive\";\nbuildResult = compiler.build.productionServerArchive(buildOpts);\n", DeploymentScript.Generate(archive.ToTaskModel()!, package: true));
    }

    /// <summary>
    /// The session's commands: a project created and opened (the root the current folder, the path
    /// on the search path, the startup file run), its files and tasks from the prompt, closing (the
    /// shutdown file run, the folder before back), the refusals, and a .prj given to an app opening
    /// the Compiler Task Manager.
    /// </summary>
    [Fact]
    public async Task TheSessionOpensAProject()
    {
        string root = Root("session");
        string rootForward = root.Replace('\\', '/');
        Assert.Equal("MATLAB:project:api:NoProjectCurrentlyLoaded | No project is currently loaded.", await Outcome("currentProject()"));
        Assert.Equal("ok", await Outcome($"proj = matlab.project.createProject('{rootForward}')"));
        Assert.Equal("blank_project", await Printed("disp(proj.Name)"));
        Assert.Equal(root, await Printed("disp(pwd)"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "lib", "k20helper.m"), "function z = k20helper(x)\nz = x * 2;\nend\n");
        File.WriteAllText(Path.Combine(root, "k20start.m"), "disp('k20 startup ran')\n");
        File.WriteAllText(Path.Combine(root, "k20stop.m"), "disp('k20 shutdown ran')\n");
        File.WriteAllText(Path.Combine(root, "main.m"), "function y = main(x)\ny = k20helper(x) + 1;\nend\n");
        Assert.Equal("ok", await Outcome("addFile(proj, 'main.m')"));
        Assert.Equal("ok", await Outcome("addPath(proj, fullfile(proj.RootFolder, 'lib'))"));
        Assert.Equal("ok", await Outcome("addStartupFile(proj, fullfile(proj.RootFolder, 'k20start.m'))"));
        Assert.Equal("ok", await Outcome("addShutdownFile(proj, fullfile(proj.RootFolder, 'k20stop.m'))"));
        Assert.Equal("6", await Printed("disp(k20helper(3))"));
        Assert.Equal("MATLAB:project:management:FileNotInProjectRoot", (await Outcome($"addFile(proj, '{_folder.Replace('\\', '/')}/nope.m')")).Split(" | ")[0]);
        Assert.Equal("3 main.m", await Printed("f = findFiles(proj, Label=\"Design\"); [~, n, e] = fileparts(f(end)); disp(string(numel(f)) + \" \" + n + e)"));
        Assert.Equal("Classification:Design", await Printed("l = proj.Files(2).Labels; disp(l.CategoryName + \":\" + l.Name)"));

        // A task from the prompt, its property written to the project at once.
        Assert.Equal("ok", await Outcome("t = compiler.project.getStandaloneDesktopAppConfiguration(proj, 'myApp'); t.MainFile = 'main.m'; t.ApplicationName = 'K20 App'"));
        Assert.Equal("main.m", await Printed("[~, n, e] = fileparts(t.MainFile); disp([n e])"));
        Assert.Equal("K20 App", MatlabProject.Open(root).FindTask("myApp")!.GetText(CompilerTaskKinds.ByType("StandaloneDesktopApp", "myApp")!.Property("ApplicationName")!));
        Assert.Equal("deployment:exception:STRING_MUST_MATCH_PATTERN", (await Outcome("compiler.project.getStandaloneDesktopAppConfiguration(proj, 'a/b')")).Split(" | ")[0]);
        Assert.Equal("MATLAB:class:SetProhibited", (await Outcome("t.ProfileDisplayName = 'x'")).Split(" | ")[0]);
        Assert.Equal("MATLAB:class:RequireString", (await Outcome("t.ApplicationName = 1")).Split(" | ")[0]);
        Assert.Equal("JGraph:compilerApps:targetOutOfScope", (await Outcome("compiler.project.getJavaLibraryConfiguration(proj, 'j')")).Split(" | ")[0]);

        // Closing runs the shutdown file, takes the path off and goes back to the folder before.
        Assert.Equal("k20 shutdown ran", await Printed("close(proj)"));
        Assert.Equal(_folder, await Printed("disp(pwd)"));
        Assert.Equal("MATLAB:UndefinedFunction", (await Outcome("k20helper(3)")).Split(" | ")[0]);
        Assert.Equal("MATLAB:project:api:CurrentProjectMismatch", (await Outcome("proj.Name")).Split(" | ")[0]);
        Assert.Equal("false", await Printed("disp(isLoaded(proj))"));

        // Reopening runs the startup file; a project given to an app opens the Compiler Task Manager.
        Assert.Equal("k20 startup ran", await Printed($"p2 = openProject('{rootForward}');"));
        Assert.Equal("6", await Printed("disp(k20helper(3))"));
        Assert.Equal("ok", await Outcome("standaloneApplicationCompiler"));
        Assert.Equal((root, CompilerTarget.StandaloneApplication), _apps.Managed[^1]);
        Assert.Equal("k20 shutdown ran", await Printed("close(p2)"));
        Assert.Equal("k20 startup ran\nok", await Outcome($"productionServerArchiveCompiler('{rootForward}/blank_project.prj')")); // the app opens the project first
        Assert.Equal((root, CompilerTarget.ProductionServerArchive), _apps.Managed[^1]);
        Assert.Equal("blank_project", await Printed("disp(currentProject().Name)"));
        Assert.Equal("k20 shutdown ran\nok", await Outcome("close(currentProject())"));
        Assert.Equal("MATLAB:project:management:DeleteFailNotAProject", (await Outcome($"matlab.project.deleteProject('{_folder.Replace('\\', '/')}')")).Split(" | ")[0]);
    }
}
