using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Completion;
using JGraph.Scripting.Startup;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The packages and class folders plan's acceptance library (stage P7, ADR 0313), run in process
/// against what R2025b printed for it: <c>tools/matlab-checklist/ui-probes/p7/lib</c> - the
/// package <c>+signal</c> with its sub-package, handle class, script and private folder, and the
/// class folder <c>@Series</c> with its method files and private folder - used by the top-level
/// script <c>p7_main.m</c> through its imports (<c>p7_accept.out</c>), built with <c>mcc</c> and
/// run as a program (<c>p7_build.out</c>, <c>p7_tool.out</c>), opened as a MATLAB project
/// (<c>p7_project.out</c>), and completed in the editor through the session's completion source,
/// which R2025b has no headless form of. <c>accept-p7.ps1</c> runs the same library through the
/// published runtime in both engines.
/// </summary>
[Collection("JG facade")]
public sealed class PackageAcceptanceTests : IAsyncLifetime
{
    private static readonly IScriptEngine[] Engines = [new JgsScriptEngine(), new MatlabScriptEngine()];
    private static readonly string Probes = Path.Combine(FindRepo(), "tools", "matlab-checklist", "ui-probes", "p7");

    private readonly RecordingScriptOutput _output = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jgraph-p7-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    private string Copy => Path.Combine(_root, "p7");

    private string Work => Path.Combine(_root, "work");

    public async Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(Work);
        CopyTree(Path.Combine(Probes, "lib"), Path.Combine(Copy, "lib"));
        foreach (string name in (string[])["p7_main.m", "p7_tool.m", "p7_accept.m", "p7_build.m", "p7_project.m"])
        {
            File.Copy(Path.Combine(Probes, name), Path.Combine(Copy, name));
        }

        // The trace file p7_accept writes, for the facts that do not run p7_accept first.
        Directory.CreateDirectory(Path.Combine(Copy, "rt"));
        File.WriteAllText(Path.Combine(Copy, "rt", "trace.txt"), "bench A\n3\n1\n4\nnot a number\n1\n5\n");
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, static (_, _) => { }, Copy));
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // swept later
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

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(from))
        {
            CopyTree(folder, Path.Combine(to, Path.GetFileName(folder)));
        }
    }

    /// <summary>R2025b's recording, its key-value lines without the runner's EXIT line.</summary>
    private static List<string> Recorded(string stem) =>
        [.. File.ReadAllLines(Path.Combine(Probes, stem + ".out")).Where(static l => l.Length > 0 && !l.StartsWith("EXIT\t", StringComparison.Ordinal) && !l.StartsWith("STDERR\t", StringComparison.Ordinal))];

    private static Dictionary<string, string> Keyed(IEnumerable<string> lines)
    {
        var keyed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            int tab = line.IndexOf('\t');
            keyed[tab < 0 ? line : line[..tab]] = tab < 0 ? "" : line[(tab + 1)..];
        }

        return keyed;
    }

    private async Task<List<string>> Printed(string statement)
    {
        _output.Normal.Clear();
        ScriptRunResult result = await _session.ExecuteAsync(statement, "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return [.. _output.NormalLines.Where(static l => l.Length > 0)];
    }

    private string Slashed(string path) => path.Replace('\\', '/');

    /// <summary>The library through the top-level script, then the tools over it: every line R2025b printed.</summary>
    [Fact]
    public async Task TheLibraryPrintsWhatR2025bPrinted()
    {
        Assert.Equal(Recorded("p7_accept"), await Printed("p7_accept"));
    }

    /// <summary>
    /// The library as a MATLAB project: created, the folders added with their files, listed, on the
    /// project path, closed and opened again - every line R2025b printed but the class folder
    /// R2025b still resolves after the project's path folder is gone (its path cache; a divergence
    /// recorded in ADR 0313), which JGraph forgets at once.
    /// </summary>
    [Fact]
    public async Task TheLibraryOpensAsAProject()
    {
        var theirs = Keyed(Recorded("p7_project"));
        var ours = Keyed(await Printed($"p7_project('{Slashed(Work)}')"));
        Assert.Equal(theirs.Keys.Order(StringComparer.Ordinal), ours.Keys.Order(StringComparer.Ordinal));
        foreach (string key in theirs.Keys.Where(static k => !k.StartsWith("resolves.", StringComparison.Ordinal)))
        {
            Assert.True(theirs[key] == ours[key], $"{key}: R2025b '{theirs[key]}', JGraph '{ours[key]}'");
        }

        Assert.Equal("8 2", theirs["resolves.open"]);
        Assert.Equal("8 2", ours["resolves.open"]);
        Assert.Equal("0 2", theirs["resolves.closed"]); // the class folder stays in R2025b's cache, rehash or not
        Assert.Equal("0 2", theirs["resolves.rehash"]);
        Assert.Equal("0 0", ours["resolves.closed"]);
        Assert.Equal("0 0", ours["resolves.rehash"]);
    }

    /// <summary>
    /// <c>mcc -m</c> of the program over the library: the build, the library's files in the archive
    /// and the manifest's functions and main as R2025b's (the archive's own entries are each
    /// runtime's, ADR 0258), and the program run under the deployed session printing what R2025b's
    /// program printed.
    /// </summary>
    [Fact]
    public async Task MccBuildsTheProgramAndItRunsAsR2025bsDid()
    {
        var theirs = Keyed(Recorded("p7_build"));
        var ours = Keyed(await Printed($"p7_build('{Slashed(Work)}')"));
        foreach (string key in (string[])["build", "deliverables", "manifest.functions", "manifest.main"])
        {
            Assert.True(theirs[key] == ours[key], $"{key}: R2025b '{theirs[key]}', JGraph '{ours[key]}'");
        }

        static IEnumerable<string> Library(Dictionary<string, string> keyed) =>
            keyed.Where(static p => p.Key.StartsWith("archive[", StringComparison.Ordinal) && p.Value.StartsWith("fsroot/p7_tool/", StringComparison.Ordinal))
                .Select(static p => p.Value).Order(StringComparer.Ordinal);
        Assert.Equal(Library(theirs), Library(ours));

        string program = Path.Combine(Work, "build", "p7_tool.exe");
        Assert.True(File.Exists(program), program);
        string trace = Path.Combine(Copy, "rt", "trace.txt");
        string run = Directory.CreateDirectory(Path.Combine(_root, "run")).FullName;
        string? before = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        try
        {
            JG.Reset();
            _output.Normal.Clear();
            StartupOptions options = StartupCommandLine.Parse(["--deployed", program, "-sd", run, "--", trace, "2"]);
            Assert.False(options.HasUsageError, options.UsageError);
            Assert.Equal(0, await BatchRunner.RunAsync(options, Engines, _output, static (_, _) => { }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", before);
        }

        Assert.Equal(Recorded("p7_tool"), _output.NormalLines.Where(static l => l.Length > 0));
    }

    /// <summary>
    /// The editor's completion over the library, through the session: the package's members after
    /// <c>signal.</c>, the sub-package's after <c>signal.io.</c>, the class's static after
    /// <c>Series.</c>, and an object's members in the class file and in a method file of the folder.
    /// </summary>
    [Fact]
    public async Task CompletionOffersThePackageAndTheClassFolder()
    {
        await Printed("addpath('lib');");
        var live = Assert.IsAssignableFrom<IScriptCompletionSource>(_session);
        static string[] Names(JgsCompletionResult r) => [.. r.Items.Select(static i => i.Text).Order(StringComparer.OrdinalIgnoreCase)];

        JgsCompletionResult package = JgsCompletionEngine.GetCompletions("x = signal.", 11, matlab: true, live: live);
        Assert.Equal(["io", "summarize", "Trace"], Names(package));
        var kinds = package.Items.ToDictionary(static i => i.Text, static i => i.Kind);
        Assert.Equal(CompletionItemKind.Namespace, kinds["io"]);
        Assert.Equal(CompletionItemKind.Function, kinds["summarize"]);
        Assert.Equal(CompletionItemKind.Type, kinds["Trace"]);
        Assert.Equal(["readTrace"], Names(JgsCompletionEngine.GetCompletions("signal.io.", 10, matlab: true, live: live)));
        Assert.Equal(["fromFile"], Names(JgsCompletionEngine.GetCompletions("signal.Trace.", 13, matlab: true, live: live)));
        Assert.Equal(["ofTrace"], Names(JgsCompletionEngine.GetCompletions("Series.", 7, matlab: true, live: live)));

        const string methodFile = "function r = total(s)\nr = s.";
        string[] inMethodFile = Names(JgsCompletionEngine.GetCompletions(methodFile, methodFile.Length, matlab: true, live: live, filePath: Path.Combine(Copy, "lib", "@Series", "total.m")));
        Assert.Equal(["describe", "ofTrace", "smooth", "total", "Values"], inMethodFile); // private/weight.m is offered nowhere

        string classFile = File.ReadAllText(Path.Combine(Copy, "lib", "+signal", "Trace.m"));
        string code = classFile.Replace("            p = max(t.Data);", "            p = max(t.");
        int offset = code.IndexOf("p = max(t.", StringComparison.Ordinal) + 10;
        string[] inClassFile = Names(JgsCompletionEngine.GetCompletions(code, offset, matlab: true, live: live, filePath: Path.Combine(Copy, "lib", "+signal", "Trace.m")));
        Assert.Contains("Data", inClassFile);
        Assert.Contains("Name", inClassFile);
        Assert.Contains("scale", inClassFile);
        Assert.Contains("describe", inClassFile);
    }
}
