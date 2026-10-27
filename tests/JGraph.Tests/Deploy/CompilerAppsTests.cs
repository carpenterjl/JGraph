using System.Net.Http;
using JGraph.Api;
using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The compiler apps (compiler and SDK plan, stage K19): the words their windows are made of, the
/// script a task stands for, the commands that open them as R2025b's open theirs (probe
/// <c>k19_apps</c>), and the test client's server.
/// </summary>
[Collection("JG facade")]
public sealed class CompilerAppsTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly Apps _apps = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-compilerapps-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "k19_hello.m"), "function y = k19_hello(x)\ny = x + 1;\nend\n");
        File.WriteAllText(Path.Combine(_folder, "k19stub.prj"), "<deployment-project/>");
        File.WriteAllText(Path.Combine(_folder, "k19note.txt"), "hello");
        _session = Session(_apps);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
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

        public CompilerAppServices? Services { get; private set; }

        public string? Refusal { get; set; }

        public string? Open(CompilerTarget? target, string? mainFile, CompilerAppServices services)
        {
            Opened.Add((target, mainFile));
            Services = services;
            return Refusal;
        }

        public List<(string Root, CompilerTarget? Target)> Managed { get; } = [];

        public string? OpenTaskManager(JGraph.Deploy.Projects.MatlabProject project, CompilerTarget? preferred, CompilerAppServices services)
        {
            Managed.Add((project.RootFolder, preferred));
            Services = services;
            return Refusal;
        }

        public List<(string Root, string Task)> Tasks { get; } = [];

        public string? OpenTask(JGraph.Deploy.Projects.MatlabProject project, string taskName, CompilerAppServices services)
        {
            Tasks.Add((project.RootFolder, taskName));
            Services = services;
            return Refusal;
        }
    }

    /// <summary>The identifier and message a statement fails with, or "ok", and the warning it raised.</summary>
    private async Task<string> Outcome(string statement, IScriptSession? session = null)
    {
        _output.Mark();
        ScriptRunResult result = await (session ?? _session).ExecuteAsync(
            $"lastwarn(''); try, {statement}; disp('ok'), catch e, disp([e.identifier ' | ' strrep(e.message, newline, ' / ')]), end; [m, id] = lastwarn; if ~isempty(id), disp(['warn ' id]), end",
            string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    [Fact]
    public void TheTargetsCarryR2025bsWords()
    {
        Assert.Equal(7, CompilerTargets.All.Count);
        Assert.Equal("Standalone Application Compiler", CompilerTargets.AppTitle(CompilerTarget.StandaloneApplication));
        Assert.Equal("Production Server Archive Compiler", CompilerTargets.AppTitle(CompilerTarget.ProductionServerArchive));
        Assert.Equal("Deployable Archive (.ctf)", CompilerTargets.TypeName(CompilerTarget.ProductionServerArchive));
        Assert.Equal("Package MATLAB programs for deployment as .NET assemblies", CompilerTargets.Description(CompilerTarget.DotNetAssembly));
        Assert.Equal("excelAddinCompiler", CompilerTargets.Command(CompilerTarget.ExcelAddIn));
        Assert.Equal("Files Required for the Add-In to Run", CompilerTargets.RequiredFilesTitle(CompilerTarget.ExcelAddIn));
        Assert.Equal("Building MPS Archive", CompilerTargets.BuildStep(CompilerTarget.ProductionServerArchive));
        Assert.True(CompilerTargets.HasMainFile(CompilerTarget.StandaloneApplication));
        Assert.False(CompilerTargets.HasMainFile(CompilerTarget.PythonPackage));
        Assert.False(CompilerTargets.CanPackage(CompilerTarget.ProductionServerArchive));
        Assert.False(CompilerTargets.IsBuilt(CompilerTarget.ExcelAddIn));
        Assert.Equal("Fetch MATLAB Runtime from mathworks.com during application installation", CompilerTargets.RuntimeDeliveryText(RuntimeDeliveryChoice.Web));
    }

    [Fact]
    public void ATaskKnowsWhatItIsMissingAndWhereItWrites()
    {
        var task = new CompilerTaskModel(CompilerTarget.StandaloneApplication);
        Assert.False(task.IsDeployable);
        Assert.Equal("Add a main file to make the task deployable", task.StatusText);
        Assert.Equal("", task.EffectiveOutputFolder);

        task.MainFile = Path.Combine(_folder, "k19_hello.m");
        Assert.True(task.IsDeployable);
        Assert.Equal("The compiler task is ready", task.StatusText);
        Assert.Equal("k19_hello", task.EffectiveName);
        Assert.Equal(_folder, task.ProjectRoot);
        Assert.Equal(Path.Combine(_folder, "k19_hello", "output", "build"), task.EffectiveOutputFolder);
        Assert.Equal(Path.Combine(_folder, "k19_hello", "output", "package"), task.EffectivePackageFolder);
        Assert.Equal(Path.Combine("%ProgramFiles%", "k19_hello"), task.DefaultInstallationDir);

        var library = new CompilerTaskModel(CompilerTarget.CSharedLibrary);
        Assert.Equal("Add an exported function to make the task deployable", library.StatusText);
        library.ExportedFunctions.Add(Path.Combine(_folder, "k19_hello.m"));
        library.Name = "mylib";
        library.OutputFolder = @"C:\out";
        Assert.Equal("mylib", library.EffectiveName);
        Assert.Equal(@"C:\out", library.EffectiveOutputFolder);
    }

    /// <summary>
    /// The script's shape is R2025b's deployment script: the project root, the build options set
    /// property by property and built, the package options and the installer, under the
    /// <c>compiler_ui_common:deploymentscript</c> catalog's comments; paths under the root as fullfile.
    /// </summary>
    [Fact]
    public void TheScriptIsR2025bsDeploymentScript()
    {
        var task = new CompilerTaskModel(CompilerTarget.StandaloneApplication)
        {
            MainFile = Path.Combine(_folder, "k19_hello.m"),
            Name = "Hello App",
            Version = "2.1",
            AuthorName = "A. Author",
            WindowsApplication = true,
            TreatInputsAsNumeric = true,
            ExecutableIcon = Path.Combine(_folder, "icon.png"),
            RuntimeDelivery = RuntimeDeliveryChoice.Installer,
            InstallerNotes = "Say \"hi\"",
        };
        task.AdditionalFiles.Add(Path.Combine(_folder, "data", "x.txt"));
        task.AdditionalFiles.Add(@"C:\elsewhere\y.txt");
        string script = DeploymentScript.Generate(task, package: true);
        string expected = string.Join("\n",
        [
            $"projectRoot = \"{_folder}\";",
            "",
            "% Create target build options object, set build properties and build.",
            "buildOpts = compiler.build.StandaloneApplicationOptions(fullfile(projectRoot, \"k19_hello.m\"));",
            "buildOpts.AdditionalFiles = [fullfile(projectRoot, \"data\", \"x.txt\"), \"C:\\elsewhere\\y.txt\"];",
            "buildOpts.AutoDetectDataFiles = true;",
            "buildOpts.OutputDir = fullfile(projectRoot, \"Hello App\", \"output\", \"build\");",
            "buildOpts.ObfuscateArchive = false;",
            "buildOpts.Verbose = true;",
            "buildOpts.EmbedArchive = true;",
            "buildOpts.ExecutableIcon = fullfile(projectRoot, \"icon.png\");",
            "buildOpts.ExecutableName = \"HelloApp\";",
            "buildOpts.ExecutableVersion = \"2.1\";",
            "buildOpts.TreatInputsAsNumeric = true;",
            "buildResult = compiler.build.standaloneWindowsApplication(buildOpts);",
            "",
            "",
            "% Create package options object, set package properties and package.",
            "packageOpts = compiler.package.InstallerOptions(buildResult);",
            "packageOpts.ApplicationName = \"Hello App\";",
            "packageOpts.AuthorName = \"A. Author\";",
            "packageOpts.InstallationNotes = \"Say \"\"hi\"\"\";",
            "packageOpts.OutputDir = fullfile(projectRoot, \"Hello App\", \"output\", \"package\");",
            "packageOpts.RuntimeDelivery = \"installer\";",
            "packageOpts.Verbose = true;",
            "compiler.package.installer(buildResult, \"Options\", packageOpts);",
            "",
        ]);
        Assert.Equal(expected, script);

        // The package part alone, for the dialog's second step, over the build's buildResult.
        string packageOnly = DeploymentScript.GeneratePackageOnly(task);
        Assert.StartsWith($"projectRoot = \"{_folder}\";\n% Create package options object", packageOnly);
        Assert.DoesNotContain("buildOpts", packageOnly);

        // An archive packages nothing; .NET takes its functions through a class map.
        var archive = new CompilerTaskModel(CompilerTarget.ProductionServerArchive) { Name = "arch" };
        archive.ExportedFunctions.Add(Path.Combine(_folder, "k19_hello.m"));
        archive.FunctionSignatures = Path.Combine(_folder, "sig.json");
        string archiveScript = DeploymentScript.Generate(archive, package: true);
        Assert.Contains("buildOpts = compiler.build.ProductionServerArchiveOptions(fullfile(projectRoot, \"k19_hello.m\"));\nbuildOpts.AutoDetectDataFiles = true;", archiveScript);
        Assert.EndsWith("buildOpts.ArchiveName = \"arch\";\nbuildOpts.FunctionSignatures = fullfile(projectRoot, \"sig.json\");\nbuildResult = compiler.build.productionServerArchive(buildOpts);\n", archiveScript);
        var dotnet = new CompilerTaskModel(CompilerTarget.DotNetAssembly) { Name = "Net" };
        dotnet.ExportedFunctions.Add(Path.Combine(_folder, "k19_hello.m"));
        dotnet.ExportedFunctions.Add(Path.Combine(_folder, "k19_other.m"));
        string dotnetScript = DeploymentScript.Generate(dotnet, package: false);
        Assert.Contains("classmap = containers.Map;\nclassmap(\"Class1\") = [fullfile(projectRoot, \"k19_hello.m\"), fullfile(projectRoot, \"k19_other.m\")];\nbuildOpts = compiler.build.DotNETAssemblyOptions(classmap);", dotnetScript);
        Assert.Contains("buildOpts.AssemblyName = \"Net\";\nbuildOpts.AssemblyVersion = \"1.0\";\nbuildOpts.DebugBuild = false;\nbuildOpts.EmbedArchive = \"on\";\nbuildOpts.Interface = \"matlab-data\";", dotnetScript);
        Assert.Equal("compiler.build.cppSharedLibrary", DeploymentScript.BuildFunction(new CompilerTaskModel(CompilerTarget.CppSharedLibrary)));
    }

    /// <summary>The commands hand the host the target and the file, as R2025b's open their apps on a project (probe k19_apps).</summary>
    [Fact]
    public async Task TheCommandsOpenTheirApps()
    {
        string hello = Path.Combine(_folder, "k19_hello.m");
        Assert.Equal("ok", await Outcome("standaloneApplicationCompiler"));
        Assert.Equal((CompilerTarget.StandaloneApplication, (string?)null), _apps.Opened[^1]);
        Assert.Equal("ok", await Outcome("standaloneApplicationCompiler('k19_hello.m')"));
        Assert.Equal((CompilerTarget.StandaloneApplication, hello), _apps.Opened[^1]);
        Assert.Equal("ok", await Outcome("productionServerArchiveCompiler(\"K19_HELLO.M\")"));
        Assert.Equal((CompilerTarget.ProductionServerArchive, hello), _apps.Opened[^1]);
        Assert.Equal("ok", await Outcome("dotNetAssemblyCompiler"));
        Assert.Equal((CompilerTarget.DotNetAssembly, null), _apps.Opened[^1]);
        Assert.Equal("ok", await Outcome("pythonPackageCompiler"));
        Assert.Equal("ok", await Outcome("cppSharedLibraryCompiler"));
        Assert.Equal("ok", await Outcome("cSharedLibraryCompiler"));
        Assert.Equal("ok", await Outcome("excelAddinCompiler"));
        Assert.Equal((CompilerTarget.ExcelAddIn, null), _apps.Opened[^1]);
        Assert.NotNull(_apps.Services);
        Assert.Equal(_folder, _apps.Services!.CurrentFolder);
        DependencyReport report = _apps.Services.Analyze([hello]);
        Assert.Contains(report.Files, f => f.Path.EndsWith("k19_hello.m", StringComparison.OrdinalIgnoreCase));

        // The legacy apps warn, then open theirs or the gallery.
        Assert.Equal("ok\nwarn Compiler:deploytool:JavaAPIToBeRemoved", await Outcome("applicationCompiler"));
        Assert.Equal((CompilerTarget.StandaloneApplication, null), _apps.Opened[^1]);
        Assert.Equal("ok\nwarn Compiler:deploytool:JavaAPIToBeRemoved", await Outcome("productionServerCompiler"));
        Assert.Equal((CompilerTarget.ProductionServerArchive, null), _apps.Opened[^1]);
        Assert.Equal("ok\nwarn Compiler:deploytool:JavaAPIToBeRemoved", await Outcome("libraryCompiler"));
        Assert.Equal((null, null), _apps.Opened[^1]);
        Assert.Equal("ok", await Outcome("deploytool"));
        Assert.Equal((null, null), _apps.Opened[^1]);
        Assert.Contains("applicationCompiler is scheduled for removal in a future release. To package MATLAB code using the MATLAB Compiler apps, use standaloneApplicationCompiler instead.", _output.NormalText + _output.ErrorText);

        // -? prints help, and nothing opens.
        int opened = _apps.Opened.Count;
        Assert.StartsWith("deploytool", await Outcome("deploytool('-?')"));
        Assert.Equal(opened, _apps.Opened.Count);

        // What the window could not open is the call's error.
        _apps.Refusal = "The file cannot be read.";
        Assert.Equal("JGraph:compilerApps:CannotOpen | The file cannot be read.", await Outcome("standaloneApplicationCompiler('k19_hello.m')"));
    }

    [Theory]
    [InlineData("standaloneApplicationCompiler(1)", "MATLAB:validators:mustBeTextScalar | Invalid argument at position 1. Value must be a character vector or string scalar.")]
    [InlineData("standaloneApplicationCompiler('')", "MATLAB:validators:mustBeNonzeroLengthText | Invalid argument at position 1. Value must be text with one or more characters.")]
    [InlineData("standaloneApplicationCompiler('a', 'b')", "MATLAB:TooManyInputs | Too many input arguments.")]
    [InlineData("x = standaloneApplicationCompiler", "MATLAB:TooManyOutputs | Too many output arguments.")]
    [InlineData("productionServerArchiveCompiler('k19none.prj')", "MATLAB:open:fileNotFound | File 'k19none.prj' not found.")]
    [InlineData("standaloneApplicationCompiler('k19stub.prj')", "MATLAB:nonExistentField | Unrecognized field name \"configuration\".")] // a legacy file goes the way of R2025b's open, which fails on a stub without a configuration (stage K21)
    [InlineData("standaloneApplicationCompiler('k19note.txt')", "JGraph:compilerApps:NotAMatlabFile")]
    [InlineData("excelAddinCompiler('x')", "MATLAB:TooManyInputs | Too many input arguments.")]
    [InlineData("javaPackageCompiler", "JGraph:compilerApps:targetOutOfScope")]
    [InlineData("comComponentCompiler", "JGraph:compilerApps:targetOutOfScope")]
    [InlineData("webAppCompiler", "JGraph:compilerApps:targetOutOfScope")]
    [InlineData("hadoopCompiler", "Compiler:deploytool:HadoopCompilerRemoved | hadoopCompiler has been removed.  To create standalone MATLAB MapReduce applications, or deployable archives from MATLAB map and reduce functions, use the mcc command.")]
    [InlineData("applicationCompiler(1)", "MATLAB:applicationCompiler:invalidType | Expected input number 1 to be one of these types: /  / char, string /  / Instead its type was double.\nwarn Compiler:deploytool:JavaAPIToBeRemoved")]
    [InlineData("deploytool([\"a\" \"b\"])", "MATLAB:deploytool:expectedScalartext | Expected input number 1 to be a non-missing string scalar or character vector.")]
    [InlineData("deploytool({'a'})", "MATLAB:deploytool:expectedScalartext | Expected input number 1 to be a non-missing string scalar or character vector.")]
    [InlineData("deploytool('k19none.prj')", "Compiler:deploytool:invalidFirstInputOneInput | k19none.prj is an invalid input argument. When using deploytool with one input argument, the input must be a valid MATLAB Compiler or MATLAB Compiler SDK Project (.prj) file. /")]
    [InlineData("libraryCompiler('k19stub.prj')", "Compiler:deploytool:invalidFirstInputOneInput | k19stub.prj is an invalid input argument. When using libraryCompiler with one input argument, the input must be a valid MATLAB Compiler or MATLAB Compiler SDK Project (.prj) file. / \nwarn Compiler:deploytool:JavaAPIToBeRemoved")]
    [InlineData("deploytool('-build', 'k19none.prj')", "Compiler:build:compatibility:projectDoesNotExist | Input project file k19none.prj does not exist.\nwarn Compiler:deploytool:DeploytoolBuildPackageToBeRemoved")]
    [InlineData("applicationCompiler('/package', 'k19stub.prj')", "Compiler:build:compatibility:notCompatibilityPRJ | Input project file k19stub.prj is not a valid MATLAB Compiler or MATLAB Compiler SDK project.\nwarn Compiler:deploytool:DeploytoolFunctionalityToBeRemoved")]
    [InlineData("deploytool('-BUILD', 'k19stub.prj')", "Compiler:deploytool:invalidFirstInputTwoInputs | -BUILD is an invalid input argument.  When using deploytool with two input arguments, the first input must be either \"-build\" or \"-package\" and the second input must be a valid MATLAB Compiler or MATLAB Compiler SDK Project (.prj) file. /")]
    [InlineData("deploytool('-build', 2)", "MATLAB:deploytool:invalidType | Expected input number 2 to be one of these types: /  / char, string /  / Instead its type was double.\nwarn Compiler:deploytool:DeploytoolBuildPackageToBeRemoved")]
    [InlineData("deploytool('a', 'b', 'c')", "MATLAB:narginchk:tooManyInputs | Too many input arguments.")]
    public async Task ACallIsRefusedInR2025bsWords(string statement, string expected)
    {
        int opened = _apps.Opened.Count;
        string outcome = await Outcome(statement);
        if (expected.Contains(" | ", StringComparison.Ordinal))
        {
            Assert.Equal(expected, outcome);
        }
        else
        {
            Assert.StartsWith(expected + " | ", outcome);
        }

        Assert.Equal(opened, _apps.Opened.Count);
    }

    /// <summary>A session with no window - the CLI, a batch run - refuses it, where R2025b would publish to its gallery.</summary>
    [Fact]
    public async Task ASessionWithNoWindowRefusesIt()
    {
        await using IScriptSession headless = Session(null);
        Assert.StartsWith("JGraph:compilerApps:NoWindow | standaloneApplicationCompiler opens the Standalone Application Compiler window in the JGraph app, and this session has no window.",
            await Outcome("standaloneApplicationCompiler", headless));
        Assert.StartsWith("JGraph:compilerApps:NoWindow | deploytool opens the compiler apps' gallery in the JGraph app", await Outcome("deploytool", headless));
    }

    /// <summary>
    /// The test client's server: an archive built in this session served by jgraph-mps (a checkout's
    /// build, found by LocalTestServer), its health asked and its function called, then ended.
    /// </summary>
    [Fact]
    public async Task TheTestClientServesABuiltArchive()
    {
        if (LocalTestServer.FindProgram() is null)
        {
            return; // no jgraph-mps.exe built here
        }

        string output = Path.Combine(_folder, "arch");
        Assert.Equal("ok", await Outcome($"compiler.build.productionServerArchive('k19_hello.m', 'ArchiveName', 'k19arch', 'OutputDir', '{output.Replace("\\", "/", StringComparison.Ordinal)}')"));
        string archive = Path.Combine(output, "k19arch.ctf");
        Assert.True(File.Exists(archive));
        var lines = new List<string>();
        using LocalTestServer server = LocalTestServer.Start(archive, 0, line => { lock (lines) { lines.Add(line); } });
        Assert.StartsWith("http://", server.Url);
        Assert.Equal("k19arch", server.ArchiveName);
        Assert.True(server.IsRunning);
        using var http = new HttpClient();
        string health = await http.GetStringAsync(new Uri(server.Url + "/api/health"));
        Assert.Contains("ok", health);
        using var content = new StringContent("{\"nargout\": 1, \"rhs\": [41]}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage reply = await http.PostAsync(new Uri($"{server.Url}/k19arch/k19_hello"), content);
        Assert.Equal(System.Net.HttpStatusCode.OK, reply.StatusCode);
        Assert.Contains("42", await reply.Content.ReadAsStringAsync());
    }
}
