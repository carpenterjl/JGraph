using JGraph.Api;
using JGraph.Deploy;
using JGraph.Deploy.Projects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// Legacy deployment projects (compiler and SDK plan, stage K21): the <c>&lt;deployment-project&gt;</c>
/// files read as R2025b's <c>readPRJStruct</c> reads them, the script <c>createDeploymentScript</c>
/// writes for them against R2025b's recorded text (probe <c>k21_legacy</c>, the <c>script.*.text</c>
/// blocks, over samples written as the probe writes them), the upgrade into a MATLAB project with
/// a task, and the session's commands over them.
/// </summary>
[Collection("JG facade")]
public sealed class LegacyProjectTests : IAsyncLifetime
{
    private static readonly string Recording = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes", "recordings", "k21_legacy.out.txt");

    private readonly RecordingScriptOutput _output = new();
    private readonly Apps _apps = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-legacy-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _folder, null) { CompilerApps = _apps });
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

    private sealed class Apps : IScriptCompilerApps
    {
        public List<(string Root, string Task)> Tasks { get; } = [];

        public string? Open(CompilerTarget? target, string? mainFile, CompilerAppServices services) => null;

        public string? OpenTaskManager(MatlabProject project, CompilerTarget? preferred, CompilerAppServices services) => null;

        public string? OpenTask(MatlabProject project, string taskName, CompilerAppServices services)
        {
            Tasks.Add((project.RootFolder, taskName));
            return null;
        }
    }

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    /// <summary>The lines under <c>&lt;key&gt;.text</c> in the recording, joined with newlines, a newline at the end.</summary>
    private static string RecordedScript(string key)
    {
        string[] lines = File.ReadAllLines(Recording);
        int at = Array.IndexOf(lines, key + ".text");
        Assert.True(at >= 0, $"{key}.text is not in the recording");
        var text = new List<string>();
        for (int i = at + 1; i < lines.Length && lines[i].StartsWith("  |", StringComparison.Ordinal); i++)
        {
            text.Add(lines[i][3..]);
        }

        return string.Join("\n", text) + "\n";
    }

    // --- the samples, written as probe_k21_legacy writes them ------------------------------------------

    private static readonly (string Key, string Value)[] StandaloneDefaults =
    [
        ("param.appname", "{name}"), ("param.icon", ""), ("param.icons", ""), ("param.version", "1.0"), ("param.authnamewatermark", ""),
        ("param.email", ""), ("param.company", ""), ("param.summary", ""), ("param.description", ""), ("param.screenshot", ""), ("param.guid", ""),
        ("param.installpath.string", "\\{name}\\"), ("param.installpath.combo", "option.installpath.programfiles"), ("param.logo", ""),
        ("param.install.notes", ""), ("param.target.install.notes", ""), ("param.intermediate", "${PROJECT_ROOT}\\{name}\\for_testing"),
        ("param.files.only", "${PROJECT_ROOT}\\{name}\\for_redistribution_files_only"), ("param.output", "${PROJECT_ROOT}\\{name}\\for_redistribution"),
        ("param.logdir", "${PROJECT_ROOT}\\{name}"), ("param.enable.clean.build", "false"), ("param.user.defined.mcr.options", ""),
        ("param.target.type", "subtarget.standalone"), ("param.support.packages", ""), ("param.web.mcr", "true"), ("param.package.mcr", "false"),
        ("param.no.mcr", "false"), ("param.web.mcr.name", "MyAppInstaller_web"), ("param.package.mcr.name", "MyAppInstaller_mcr"),
        ("param.no.mcr.name", "MyAppInstaller_app"), ("param.windows.command.prompt", "true"), ("param.create.log", "false"), ("param.log.file", ""),
        ("param.native.matlab", "false"), ("param.checkbox", "false"), ("param.example", ""), ("param.help.text", ""),
    ];

    private static readonly (string Key, string Value)[] LibraryDefaults =
    [
        ("param.appname", "{name}"), ("param.icon", ""), ("param.icons", ""), ("param.version", "1.0"), ("param.authnamewatermark", ""),
        ("param.email", ""), ("param.company", ""), ("param.summary", ""), ("param.description", ""), ("param.screenshot", ""), ("param.guid", ""),
        ("param.installpath.string", "\\{name}\\"), ("param.installpath.combo", "option.installpath.programfiles"), ("param.logo", ""),
        ("param.install.notes", ""), ("param.target.install.notes", ""), ("param.intermediate", "${PROJECT_ROOT}\\{name}\\for_testing"),
        ("param.files.only", "${PROJECT_ROOT}\\{name}\\for_redistribution_files_only"), ("param.output", "${PROJECT_ROOT}\\{name}\\for_redistribution"),
        ("param.logdir", "${PROJECT_ROOT}\\{name}"), ("param.enable.clean.build", "false"), ("param.user.defined.mcr.options", ""),
        ("param.target.type", "{sub}"), ("param.support.packages", ""), ("param.namespace", ""), ("param.classorg", ""),
        ("param.web.mcr", "true"), ("param.package.mcr", "false"), ("param.no.mcr", "false"), ("param.web.mcr.name", "MyAppInstaller_web"),
        ("param.package.mcr.name", "MyAppInstaller_mcr"), ("param.no.mcr.name", "MyAppInstaller_app"), ("param.windows.command.prompt", "true"),
        ("param.create.log", "false"), ("param.log.file", ""), ("param.user.only.registration", "false"), ("param.assembly.net.version", "option.net.version.four"),
        ("param.assembly.type", "false"), ("param.encryption.key.file", ""), ("param.net.enable.remoting", "false"), ("param.net.tsa.enable", "false"),
        ("param.net.tsa.assembly", ""), ("param.net.tsa.interface", ""), ("param.net.tsa.namespace", ""), ("param.net.tsa.superclass", "Class1"),
        ("param.net.tsa.metadata", ""), ("param.net.tsa.metadata.assembly", ""), ("param.net.saved.interface", ""), ("param.cpp.api", "option.cpp.all"),
    ];

    private static readonly (string Key, string Value)[] ArchiveDefaults =
    [
        ("param.appname", "{name}"), ("param.icon", ""), ("param.icons", ""), ("param.version", "1.0"),
        ("param.intermediate", "${PROJECT_ROOT}\\{name}\\for_testing"), ("param.files.only", "${PROJECT_ROOT}\\{name}\\for_redistribution_files_only"),
        ("param.output", "${PROJECT_ROOT}\\{name}\\for_redistribution"), ("param.logdir", "${PROJECT_ROOT}\\{name}"), ("param.enable.clean.build", "false"),
        ("param.user.defined.mcr.options", ""), ("param.server.ctf", "${PROJECT_ROOT}\\{name}\\for_testing\\server\\{name}.ctf"),
        ("param.server.readme", "${PROJECT_ROOT}\\{name}\\for_testing\\server\\readme.txt"), ("param.target.type", "{sub}"), ("param.support.packages", ""),
        ("param.required.mcr.products", ""), ("param.discovery.file", ""), ("param.classorg", ""), ("param.function.data", ""),
        ("param.user.only.registration", "false"), ("param.mps.server.dir", "server"), ("param.mps.client.dir", "client"),
        ("param.time.out", "60"), ("param.max.size", "750"), ("param.certificate.file", ""), ("param.mads.server.configuration", ""),
    ];

    private static readonly string[] StandaloneSets = ["fileset.main", "fileset.resources", "fileset.package", "fileset.depfun"];
    private static readonly string[] LibrarySets = ["fileset.exports", "fileset.classes", "fileset.resources", "fileset.depfun", "fileset.package", "fileset.examples", "fileset.documentation"];
    private static readonly string[] ArchiveSets = ["fileset.exports", "fileset.classes", "fileset.resources", "fileset.depfun", "fileset.package", "fileset.package.server", "fileset.package.client"];

    /// <summary>The sources every sample gets a copy of.</summary>
    private void WriteSources(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "k21_hello.m"), "function k21_hello(name)\n%K21_HELLO Greets.\n%   k21_hello(name) prints a greeting.\nif nargin < 1, name = 'world'; end\nfprintf('hello %s\\n', name);\nend\n");
        File.WriteAllText(Path.Combine(folder, "k21_add.m"), "function c = k21_add(a, b)\n%K21_ADD Adds.\nc = a + b;\nend\n");
        File.WriteAllText(Path.Combine(folder, "k21_mul.m"), "function c = k21_mul(a, b)\n%K21_MUL Multiplies.\nc = a * b;\nend\n");
        File.WriteAllText(Path.Combine(folder, "k21_sample.m"), "% A sample script.\nc = k21_add(1, 2);\ndisp(c)\n");
        foreach (string name in (string[])["k21_note.txt", "k21_notes.txt", "k21_a.txt", "k21_b.txt", "k21_data.mat", "k21_icon.png", "k21_splash.png", "k21_logo.png", "k21_icon.ico", "k21_app.mlapp"])
        {
            File.WriteAllText(Path.Combine(folder, name), name + "\n");
        }
    }

    /// <summary>A legacy project in a folder of its own, as the probe's writePrj writes one.</summary>
    private string Sample(string name, string target, string targetName, (string Key, string Value)[] defaults, string[] setNames,
        Dictionary<string, string>? overrides = null, Dictionary<string, string[]>? sets = null, (string Name, string[] Files)[]? classes = null,
        string[]? icons = null, string? location = null, string plugin = "plugin.ezdeploy", string sub = "")
    {
        string folder = Path.Combine(_folder, "p", name);
        WriteSources(folder);
        string file = Path.Combine(folder, name + ".prj");
        var text = new System.Text.StringBuilder();
        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        text.Append($"<deployment-project plugin=\"{plugin}\" plugin-version=\"1.0\">\n");
        text.Append($"  <configuration build-checksum=\"1\" file=\"{file}\" location=\"{location ?? folder}\" name=\"{name}\" target=\"{target}\" target-name=\"{targetName}\">\n");
        var unset = new List<string>();
        foreach ((string key, string raw) in defaults)
        {
            string value = raw.Replace("{name}", name, StringComparison.Ordinal).Replace("{sub}", sub, StringComparison.Ordinal);
            if (overrides is not null && overrides.TryGetValue(key, out string? given))
            {
                value = given;
            }

            if (key == "param.icons" && icons is { Length: > 0 })
            {
                text.Append("    <param.icons>\n");
                foreach (string icon in icons)
                {
                    text.Append($"      <file>{icon}</file>\n");
                }

                text.Append("    </param.icons>\n");
            }
            else if (value.Length == 0)
            {
                text.Append($"    <{key} />\n");
                unset.Add(key);
            }
            else
            {
                text.Append($"    <{key}>{value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")}</{key}>\n");
            }
        }

        text.Append("    <unset>\n");
        foreach (string key in unset)
        {
            text.Append($"      <{key} />\n");
        }

        text.Append("    </unset>\n");
        foreach (string setName in setNames)
        {
            if (setName == "fileset.classes" && classes is { Length: > 0 })
            {
                text.Append("    <fileset.classes>\n      <entity.package name=\"\">\n");
                foreach ((string className, string[] files) in classes)
                {
                    text.Append($"        <entity.class name=\"{className}\">\n");
                    foreach (string f in files)
                    {
                        text.Append($"          <file>{f}</file>\n");
                    }

                    text.Append("        </entity.class>\n");
                }

                text.Append("      </entity.package>\n    </fileset.classes>\n");
                continue;
            }

            string[] members = sets is not null && sets.TryGetValue(setName, out string[]? given) ? given : [];
            if (members.Length == 0)
            {
                text.Append($"    <{setName} />\n");
            }
            else
            {
                text.Append($"    <{setName}>\n");
                foreach (string f in members)
                {
                    text.Append($"      <file>{f}</file>\n");
                }

                text.Append($"    </{setName}>\n");
            }
        }

        text.Append("    <build-deliverables />\n    <workflow />\n    <matlab>\n      <root>C:\\Program Files\\MATLAB\\R2025b</root>\n      <toolboxes />\n    </matlab>\n");
        text.Append("    <platform>\n      <unix>false</unix>\n      <mac>false</mac>\n      <windows>true</windows>\n      <win2k>false</win2k>\n      <winxp>false</winxp>\n      <vista>false</vista>\n      <linux>false</linux>\n      <solaris>false</solaris>\n      <osver>10.0</osver>\n      <os32>false</os32>\n      <os64>true</os64>\n      <arch>win64</arch>\n      <matlab>true</matlab>\n    </platform>\n");
        text.Append("  </configuration>\n</deployment-project>");
        File.WriteAllText(file, text.ToString());
        return file;
    }

    private string Standalone(string name, Dictionary<string, string>? overrides = null, Dictionary<string, string[]>? sets = null, string[]? icons = null) =>
        Sample(name, LegacyDeploymentProject.StandaloneTarget, "Application Compiler", StandaloneDefaults, StandaloneSets,
            overrides, sets ?? new Dictionary<string, string[]> { ["fileset.main"] = ["${PROJECT_ROOT}\\k21_hello.m"] }, icons: icons);

    private static readonly Dictionary<string, string> LibraryInfo = new()
    {
        ["param.authnamewatermark"] = "Ada Lovelace", ["param.email"] = "ada@example.com", ["param.company"] = "Analytical Engines",
        ["param.summary"] = "A library", ["param.description"] = "Adds and multiplies.", ["param.version"] = "1.2",
        ["param.install.notes"] = "Library notes", ["param.target.install.notes"] = "Target notes", ["param.logo"] = "${PROJECT_ROOT}\\k21_logo.png",
        ["param.screenshot"] = "${PROJECT_ROOT}\\k21_splash.png", ["param.icon"] = "${PROJECT_ROOT}\\k21_icon.ico",
    };

    private static readonly Dictionary<string, string[]> LibrarySetFiles = new()
    {
        ["fileset.exports"] = ["${PROJECT_ROOT}\\k21_add.m", "${PROJECT_ROOT}\\k21_mul.m"], ["fileset.resources"] = ["${PROJECT_ROOT}\\k21_note.txt"],
        ["fileset.package"] = ["${PROJECT_ROOT}\\k21_notes.txt"], ["fileset.examples"] = ["${PROJECT_ROOT}\\k21_sample.m"], ["fileset.documentation"] = ["${PROJECT_ROOT}\\k21_b.txt"],
    };

    private string Library(string name, string sub, Dictionary<string, string>? more = null)
    {
        var overrides = new Dictionary<string, string>(LibraryInfo);
        foreach ((string k, string v) in more ?? [])
        {
            overrides[k] = v;
        }

        return Sample(name, LegacyDeploymentProject.LibraryTarget, "Library Compiler", LibraryDefaults, LibrarySets, overrides, LibrarySetFiles,
            [("Class1", ["${PROJECT_ROOT}\\k21_add.m", "${PROJECT_ROOT}\\k21_mul.m"])], sub: sub);
    }

    private string Std() => Standalone("k21std", new Dictionary<string, string>
    {
        ["param.authnamewatermark"] = "Ada Lovelace", ["param.email"] = "ada@example.com", ["param.company"] = "Analytical Engines",
        ["param.summary"] = "A greeting", ["param.description"] = "Prints a greeting.\nTwo lines.", ["param.version"] = "2.3.1",
        ["param.icon"] = "${PROJECT_ROOT}\\k21_icon.ico", ["param.screenshot"] = "${PROJECT_ROOT}\\k21_splash.png", ["param.logo"] = "${PROJECT_ROOT}\\k21_logo.png",
        ["param.install.notes"] = "Notes line 1\nNotes line 2", ["param.installpath.string"] = "\\Analytical Engines\\k21std\\",
        ["param.windows.command.prompt"] = "false", ["param.create.log"] = "true", ["param.log.file"] = "k21std.log", ["param.native.matlab"] = "true",
        ["param.checkbox"] = "true", ["param.example"] = "k21std world", ["param.help.text"] = "Usage: k21std name\nPrints hello.",
        ["param.user.defined.mcr.options"] = "-C -v",
    }, new Dictionary<string, string[]>
    {
        ["fileset.main"] = ["${PROJECT_ROOT}\\k21_hello.m"],
        ["fileset.resources"] = ["${PROJECT_ROOT}\\k21_note.txt", "${PROJECT_ROOT}\\k21_b.txt", "${PROJECT_ROOT}\\k21_a.txt", "${PROJECT_ROOT}\\k21_data.mat"],
        ["fileset.package"] = ["${PROJECT_ROOT}\\k21_notes.txt", "${PROJECT_ROOT}\\k21_a.txt"], ["fileset.depfun"] = ["${PROJECT_ROOT}\\k21_add.m"],
    }, ["${PROJECT_ROOT}\\k21_icon.png", "${PROJECT_ROOT}\\k21_splash.png"]);

    /// <summary>The recorded script for a sample with the recorder's work token replaced by this test's folder.</summary>
    private string Expected(string key, string name) =>
        RecordedScript(key).Replace("<W>\\p\\p\\" + name, Path.Combine(_folder, "p", name), StringComparison.Ordinal);

    private static string Generated(string prj, out IReadOnlyList<(string Id, string Message)> warnings) =>
        LegacyDeploymentScript.Generate(LegacyTaskSettings.From(LegacyDeploymentProject.Read(prj))!, out warnings);

    // --- the tests ---------------------------------------------------------------------------------------

    /// <summary>The reader: the configuration as readstruct reads it with the macros resolved, the parameters and file sets, and the refusals in R2025b's words.</summary>
    [Fact]
    public void TheReaderIsR2025bs()
    {
        string prj = Std();
        string folder = Path.GetDirectoryName(prj)!;
        Assert.True(LegacyDeploymentProject.IsLegacyProjectFile(prj));
        LegacyDeploymentProject legacy = LegacyDeploymentProject.Read(prj);
        Assert.Equal(LegacyDeploymentProject.StandaloneTarget, legacy.Target);
        Assert.Equal("subtarget.standalone", legacy.SubTarget);
        Assert.Equal("Application Compiler", legacy.TargetName);
        Assert.Equal(Path.Combine(folder, "k21_hello.m"), legacy.FileSet("fileset.main")[0]);
        Assert.Equal(4, legacy.FileSet("fileset.resources").Count);
        Assert.Equal([Path.Combine(folder, "k21_icon.png"), Path.Combine(folder, "k21_splash.png")], legacy.Icons);
        Assert.Equal("Prints a greeting.\nTwo lines.", legacy.Param("param.description"));
        Assert.True(legacy.HasFileSet("fileset.depfun"));

        // readstruct's shape: attributes as <name>Attribute, dots as underscores, a repeated file as a string array, an empty element "".
        LegacyXmlStruct resolved = legacy.Resolved;
        Assert.Equal("k21std", resolved.Text("nameAttribute"));
        Assert.Equal("target.ezdeploy.standalone", resolved.Text("targetAttribute"));
        Assert.Equal("Application Compiler", resolved.Text("target_nameAttribute"));
        Assert.Equal(Path.Combine(folder, "k21_hello.m"), resolved.Struct("fileset_main")!.Text("file"));
        Assert.Equal(4, resolved.Struct("fileset_resources")!.Texts("file").Count);
        Assert.Equal("", resolved.Text("build_deliverables"));
        Assert.Equal("", resolved.Struct("unset")!.Text("param_guid"));
        Assert.Equal("true", resolved.Struct("platform")!.Text("windows"));
        Assert.Equal(Path.Combine(folder, "k21std", "for_testing"), resolved.Text("param_intermediate"));

        // The refusals: a missing file, a file that is not a project, an unknown sub-target, a Hadoop project.
        Assert.Equal("Compiler:build:compatibility:projectDoesNotExist", Assert.Throws<LegacyProjectException>(() => LegacyDeploymentProject.Read(Path.Combine(folder, "none.prj"), "none.prj")).Identifier);
        File.WriteAllText(Path.Combine(folder, "text.prj"), "not a project\n");
        Assert.Equal("Compiler:build:compatibility:notCompatibilityPRJ", Assert.Throws<LegacyProjectException>(() => LegacyDeploymentProject.Read(Path.Combine(folder, "text.prj"))).Identifier);
        Assert.Null(LegacyDeploymentProject.ReadStruct(Path.Combine(folder, "text.prj")));
        string badSub = Standalone("k21badsub", new Dictionary<string, string> { ["param.target.type"] = "subtarget.nothing" });
        Assert.Equal("Compiler:build:compatibility:notCompatibilityPRJ", Assert.Throws<LegacyProjectException>(() => LegacyDeploymentProject.Read(badSub)).Identifier);
        string hadoop = Sample("k21hadoop", LegacyDeploymentProject.HadoopTarget, "Hadoop Compiler", StandaloneDefaults, ["fileset.mapper", "fileset.reducer", "fileset.datastore", "fileset.resources", "fileset.depfun"],
            new Dictionary<string, string> { ["param.target.type"] = "subtarget.hadoop" }, new Dictionary<string, string[]> { ["fileset.mapper"] = ["${PROJECT_ROOT}\\k21_add.m"] });
        Assert.Equal("Compiler:build:compatibility:hadoopNotSupported", Assert.Throws<LegacyProjectException>(() => LegacyDeploymentProject.Read(hadoop)).Identifier);

        // The checks before a script: no main file, no exported functions, no class for a class-based kind.
        string noMain = Standalone("k21nomain", sets: new Dictionary<string, string[]>());
        Assert.Equal("Compiler:build:compatibility:noMainFileSelected", Assert.Throws<LegacyProjectException>(() => LegacyTaskSettings.From(LegacyDeploymentProject.Read(noMain))!.Check()).Identifier);
        string noExports = Sample("k21noexports", LegacyDeploymentProject.LibraryTarget, "Library Compiler", LibraryDefaults, LibrarySets, sub: "subtarget.library.c");
        Assert.Equal("Compiler:build:compatibility:noExportedFunctionsSelected", Assert.Throws<LegacyProjectException>(() => LegacyTaskSettings.From(LegacyDeploymentProject.Read(noExports))!.Check()).Identifier);
        string noClass = Sample("k21noclass", LegacyDeploymentProject.LibraryTarget, "Library Compiler", LibraryDefaults, LibrarySets,
            sets: new Dictionary<string, string[]> { ["fileset.exports"] = ["${PROJECT_ROOT}\\k21_add.m"] }, sub: "subtarget.net.component");
        Assert.Equal("Compiler:build:compatibility:invalidClassDefinition", Assert.Throws<LegacyProjectException>(() => LegacyTaskSettings.From(LegacyDeploymentProject.Read(noClass))!.Check()).Identifier);
    }

    /// <summary>
    /// The script is R2025b's recorded text for the probe's samples: a standalone application with
    /// everything set (the sorted additional files, the help text file, the splash, the installer's
    /// files, the author, the install folder and notes, the version; -C turning the embedding off
    /// with the other option warned about), a C shared library, a Python package with a namespace,
    /// a .NET assembly for .NET 5 with remoting and a strong name key, a Production Server archive
    /// (no installer), and the icons' first file as the icon.
    /// </summary>
    [Fact]
    public void TheScriptIsR2025bsRecordedText()
    {
        Assert.Equal(Expected("script.k21std", "k21std"), Generated(Std(), out IReadOnlyList<(string Id, string Message)> warnings));
        Assert.Equal([(LegacyDeploymentScript.MccFlagsWarningId, LegacyDeploymentScript.MccFlagsWarning)], warnings);

        Assert.Equal(Expected("script.k21c", "k21c"), Generated(Library("k21c", "subtarget.library.c"), out warnings));
        Assert.Empty(warnings);
        Assert.Equal(Expected("script.k21python", "k21python"), Generated(Library("k21python", "subtarget.python.module", new Dictionary<string, string> { ["param.namespace"] = "k21py" }), out _));
        Assert.Equal(Expected("script.k21cpp", "k21cpp"), Generated(Library("k21cpp", "subtarget.library.cpp"), out _));
        Assert.Equal(Expected("script.k21excel", "k21excel"), Generated(Library("k21excel", "subtarget.ex.addin"), out _));

        string net5 = Sample("k21net5", LegacyDeploymentProject.LibraryTarget, "Library Compiler", LibraryDefaults, LibrarySets,
            new Dictionary<string, string> { ["param.assembly.net.version"] = "option.net.version.five", ["param.net.enable.remoting"] = "true", ["param.assembly.type"] = "true", ["param.encryption.key.file"] = "${PROJECT_ROOT}\\key.snk" },
            new Dictionary<string, string[]> { ["fileset.exports"] = ["${PROJECT_ROOT}\\k21_add.m"] }, [("Class1", ["${PROJECT_ROOT}\\k21_add.m"])], sub: "subtarget.net.component");
        Assert.Equal(Expected("script.k21net5", "k21net5"), Generated(net5, out _));

        string mps = Sample("k21mps", LegacyDeploymentProject.ProductionServerTarget, "Production Server Compiler", ArchiveDefaults, ArchiveSets, sets: new Dictionary<string, string[]>
        {
            ["fileset.exports"] = ["${PROJECT_ROOT}\\k21_add.m", "${PROJECT_ROOT}\\k21_mul.m"], ["fileset.resources"] = ["${PROJECT_ROOT}\\k21_note.txt"],
            ["fileset.package.server"] = ["${PROJECT_ROOT}\\k21_a.txt"], ["fileset.package.client"] = ["${PROJECT_ROOT}\\k21_b.txt"],
        }, sub: "subtarget.mads");
        Assert.Equal(Expected("script.k21mps", "k21mps"), Generated(mps, out _));

        string icons = Standalone("k21icons", new Dictionary<string, string> { ["param.icon"] = "${PROJECT_ROOT}\\k21_icon.ico" }, icons: ["${PROJECT_ROOT}\\k21_icon.png", "${PROJECT_ROOT}\\k21_splash.png"]);
        Assert.Equal(Expected("script.k21icons", "k21icons"), Generated(icons, out _));

        // The runtime deliveries: packaged (the download line and "installer"), none (JGraph writes the project's meaning: R2025b's _mcr name with a web delivery is recorded as a slip).
        string mcr = Standalone("k21mcr", new Dictionary<string, string> { ["param.web.mcr"] = "false", ["param.package.mcr"] = "true" });
        Assert.Equal(Expected("script.k21mcr", "k21mcr"), Generated(mcr, out _));
        string noMcr = Standalone("k21nomcr", new Dictionary<string, string> { ["param.web.mcr"] = "false", ["param.no.mcr"] = "true" });
        string noMcrScript = Generated(noMcr, out _);
        Assert.Contains("packageOpts.InstallerName = \"MyAppInstaller_app\";\n", noMcrScript);
        Assert.Contains("packageOpts.RuntimeDelivery = \"none\";\n", noMcrScript);
    }

    /// <summary>
    /// The upgrade: the legacy file becomes .bak, its folder a project named after it holding every
    /// file, and a task named "&lt;project&gt; &lt;kind&gt;" carries the settings as labels and
    /// properties; the warnings name the mcc options and the files that do not exist; a .bak upgrades
    /// again into the same project's task.
    /// </summary>
    [Fact]
    public void TheUpgradeMakesAProjectTask()
    {
        string prj = Std();
        string folder = Path.GetDirectoryName(prj)!;
        File.WriteAllText(Path.Combine(folder, "k21_a.txt"), "a\n");
        LegacyDeploymentProject legacy = LegacyDeploymentProject.Read(prj);
        LegacyProjectUpgradeResult upgraded = LegacyProjectUpgrade.Upgrade(legacy)!;
        Assert.True(File.Exists(prj + ".bak"));
        Assert.True(ProjectDefinitionFiles.IsProjectFile(prj)); // the project's own stub takes the legacy file's name
        Assert.Equal("k21std", upgraded.Project.Name);
        Assert.Equal(["The following additional MCC options are not supported: -v"], upgraded.UnsupportedWarnings);
        Assert.Empty(upgraded.FileWarnings);
        Assert.Contains(new ProjectFileEntry("k21std.prj.bak", false), upgraded.Project.Files);
        Assert.Contains(new ProjectFileEntry("k21_hello.m", false), upgraded.Project.Files);

        CompilerTaskConfiguration task = upgraded.Task!;
        Assert.Equal("k21std Standalone", task.Name);
        Assert.Equal("Standalone Application - k21std Standalone", task.CategoryName);
        Assert.Equal(Path.Combine(folder, "k21_hello.m"), task.LabelledFile("MainFile"));
        Assert.Equal(["k21_a.txt", "k21_b.txt", "k21_data.mat", "k21_note.txt"], task.LabelledFiles("AdditionalFiles").Select(Path.GetFileName));
        Assert.Equal(["k21_a.txt", "k21_notes.txt"], task.LabelledFiles("InstallerAdditionalFiles").Select(Path.GetFileName));
        Assert.Equal("k21_icon.png", Path.GetFileName(task.LabelledFile("ExeIconFile")));
        Assert.Equal("k21_splash.png", Path.GetFileName(task.LabelledFile("InstallerSplashFile")));
        CompilerTaskModel model = task.ToTaskModel()!;
        Assert.Equal("k21std", model.Name);
        Assert.Equal("2.3.1", model.Version);
        Assert.Equal("Ada Lovelace", model.AuthorName);
        Assert.False(model.WindowsApplication); // the legacy project keeps the console (R2025b stores the flag's inverse, ADR 0276)
        Assert.True(model.TreatInputsAsNumeric);
        Assert.False(model.EmbedArchive);
        Assert.Equal("Usage: k21std name\nPrints hello.", model.HelpText);
        Assert.Equal("k21std world", model.ExampleText);
        Assert.Equal("/Analytical Engines/k21std/", model.InstallSubfolder);
        Assert.Equal("2.3.1", model.InstallerVersion);
        Assert.Equal("Notes line 1\nNotes line 2", model.InstallerNotes);
        Assert.Equal(Path.Combine(folder, "k21std", "for_testing"), model.EffectiveOutputFolder);
        Assert.Equal(["Classification:Design", "Standalone Application - k21std Standalone:Additional File"], upgraded.Project.LabelsOf("k21_data.mat").Select(l => l.CategoryName + ":" + l.LabelName));

        // Upgrading the .bak again lands in the same project and task.
        LegacyProjectUpgradeResult again = LegacyProjectUpgrade.Upgrade(LegacyDeploymentProject.Read(prj + ".bak"))!;
        Assert.Equal(upgraded.Project.RootFolder, again.Project.RootFolder);
        Assert.Equal("k21std Standalone", again.Task!.Name);
        Assert.Single(again.Project.Tasks);

        // A library: the exported functions, samples and documentation as labels, the namespace as the package name; a missing file warned about.
        string python = Library("k21python", "subtarget.python.module", new Dictionary<string, string> { ["param.namespace"] = "k21py" });
        File.Delete(Path.Combine(Path.GetDirectoryName(python)!, "k21_note.txt"));
        LegacyProjectUpgradeResult lib = LegacyProjectUpgrade.Upgrade(LegacyDeploymentProject.Read(python))!;
        Assert.Equal("k21python Python Package", lib.Task!.Name);
        Assert.Equal(["k21_add.m", "k21_mul.m"], lib.Task.LabelledFiles("ExportedFunctionFile").Select(Path.GetFileName));
        Assert.Equal(["k21_sample.m"], lib.Task.LabelledFiles("SamplesFile").Select(Path.GetFileName));
        Assert.Empty(lib.Task.LabelledFiles("DocumentationFile")); // R2025b's upgrade drops the documentation set
        Assert.Equal("k21py.k21python", lib.Task.ToTaskModel()!.Name);
        Assert.Single(lib.FileWarnings);
        Assert.EndsWith("k21_note.txt could not be added to the task because it does not exist.", lib.FileWarnings[0]);

        // Nothing upgrades a Hadoop project.
        string hadoop = Sample("k21hadoop", LegacyDeploymentProject.HadoopTarget, "Hadoop Compiler", StandaloneDefaults, ["fileset.mapper", "fileset.reducer", "fileset.datastore", "fileset.resources", "fileset.depfun"],
            new Dictionary<string, string> { ["param.target.type"] = "subtarget.hadoop" });
        Assert.Null(LegacyProjectUpgrade.Upgrade(LegacyDeploymentProject.ReadStruct(hadoop)!));
        Assert.True(File.Exists(hadoop));
    }

    /// <summary>
    /// The session: createDeploymentScript writes the script and refuses in R2025b's order and words;
    /// deploytool -build builds the program with "Build Complete"; deploytool prj upgrades the project,
    /// leaves it open and brings up the task's app; readPRJStruct answers the struct.
    /// </summary>
    [Fact]
    public async Task TheSessionTakesALegacyProject()
    {
        string prj = Standalone("k21plain");
        string folder = Path.GetDirectoryName(prj)!;
        string prjForward = prj.Replace('\\', '/');
        string scripts = Path.Combine(_folder, "scripts");
        Directory.CreateDirectory(scripts);
        string scriptsForward = scripts.Replace('\\', '/');
        Assert.Equal("ok", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/deploy_plain.m')"));
        Assert.Equal(Expected("script.k21plain", "k21plain"), File.ReadAllText(Path.Combine(scripts, "deploy_plain.m")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.StartsWith("Compiler:build:compatibility:scriptFileCannotExist | Output file ", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/deploy_plain.m')"));
        Assert.Equal($"Compiler:build:compatibility:incompatibleScriptExtension | The output file {scriptsForward}/deploy.txt must use the \".m\" extension.", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/deploy.txt')"));
        Assert.Equal("Compiler:build:compatibility:invalidOutputFileName | The output file 1bad must be a valid MATLAB variable name.", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/1bad.m')"));
        Assert.Equal("Compiler:build:compatibility:projectDoesNotExist | Input project file none.prj does not exist.", await Outcome("createDeploymentScript('none.prj', 'x.m')"));
        Assert.Equal("MATLAB:minrhs | Invalid argument list. Function requires 1 more input(s).", await Outcome($"createDeploymentScript('{prjForward}')"));
        Assert.Equal("ok", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/deploy_noext')"));
        Assert.True(File.Exists(Path.Combine(scripts, "deploy_noext.m")));
        Assert.Equal("ok\nwarn Compiler:build:compatibility:mccFlagsNotSupported", await Outcome($"createDeploymentScript('{Std().Replace('\\', '/')}', '{scriptsForward}/deploy_std.m')"));

        // readPRJStruct: the struct, a name without its extension, a missing file.
        Assert.Equal("k21plain", await Printed($"s = compiler.internal.readPRJStruct('{prjForward}'); disp(s.nameAttribute)"));
        Assert.Equal(Path.Combine(folder, "k21_hello.m"), await Printed($"s = compiler.internal.readPRJStruct('{prjForward[..^4]}'); disp(s.fileset_main.file)"));
        Assert.StartsWith("MATLAB:textio:textio:FileNotFound | Unable to find or open '", await Outcome("compiler.internal.readPRJStruct('nothere.prj')"));

        // deploytool -build: the program built in the project's for_testing folder, Build Complete printed.
        string built = await Printed($"deploytool('-build', '{prjForward}')");
        Assert.EndsWith("Build Complete", built);
        Assert.True(File.Exists(Path.Combine(folder, "k21plain", "for_testing", "k21plain.exe")), built);
        Assert.Equal("Compiler:build:compatibility:noMainFileSelected | The project cannot be built because no main file is selected.",
            (await Outcome($"deploytool('-build', '{Standalone("k21nomain", sets: new Dictionary<string, string[]>()).Replace('\\', '/')}')")).Split('\n')[0]);
        Assert.Equal("Compiler:build:compatibility:projectDoesNotExist | Input project file x.m does not exist.", (await Outcome("deploytool('-build', 'x.m')")).Split('\n')[0]);

        // deploytool prj: the upgrade, the project open, the task's app brought up; the project's .prj is now MATLAB's.
        Assert.Equal("ok", await Outcome($"deploytool('{prjForward}')"));
        Assert.Equal("k21plain", await Printed("disp(currentProject().Name)"));
        Assert.Contains((folder, "k21plain Standalone"), _apps.Tasks);
        Assert.True(File.Exists(prj + ".bak"));
        Assert.True(ProjectDefinitionFiles.IsProjectFile(prj));
        Assert.StartsWith("Compiler:build:compatibility:notCompatibilityPRJ", await Outcome($"createDeploymentScript('{prjForward}', '{scriptsForward}/deploy_upgraded.m')"));
        Assert.Equal("ok", await Outcome("close(currentProject)"));

        // open and the legacy apps take the same road; createProjectModelFromPRJ answers the handle and the warnings.
        string c = Library("k21c", "subtarget.library.c");
        Assert.Equal("ok\nwarn Compiler:deploytool:JavaAPIToBeRemoved", await Outcome($"libraryCompiler('{c.Replace('\\', '/')}')"));
        Assert.Contains((Path.GetDirectoryName(c)!, "k21c C Shared Library"), _apps.Tasks);
        Assert.Equal("ok", await Outcome("close(currentProject)"));
        string tsa = Sample("k21tsa", LegacyDeploymentProject.LibraryTarget, "Library Compiler", LibraryDefaults, LibrarySets,
            new Dictionary<string, string> { ["param.net.tsa.enable"] = "true", ["param.net.tsa.superclass"] = "Class1" },
            new Dictionary<string, string[]> { ["fileset.exports"] = ["${PROJECT_ROOT}\\k21_add.m"] }, [("Class1", ["${PROJECT_ROOT}\\k21_add.m"])], sub: "subtarget.net.component");
        Assert.Equal("compiler.project.DotNETLibraryConfigurationOptions|k21tsa NET Assembly|" + LegacyProjectUpgrade.TypeSafeWarning,
            await Printed($"[cfg, fw, u] = matlab.internal.deployment.createProjectModelFromPRJ('{tsa.Replace('\\', '/')}'); fprintf('%s|%s|%s\\n', class(cfg), cfg.ProfileDisplayName, u)"));
        Assert.Equal("double 0 0", await Printed("[cfg, fw, u] = matlab.internal.deployment.createProjectModelFromPRJ('nothere.prj'); fprintf('%s %d %d\\n', class(cfg), size(fw, 1), size(u, 2))"));
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
}
