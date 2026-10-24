using System.Diagnostics;
using JGraph.Api;
using JGraph.Deploy;
using JGraph.Runtime;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The JGraph Runtime's facade (compiler and SDK plan, stage K4): components opened from archives,
/// calls on the engine thread, what one component shares with another and with a second instance of
/// itself, and errors, all against what <c>record-k4.ps1</c> measured R2025b's runtime doing with
/// the same <c>k4.m</c> (<c>tools/matlab-checklist/compiler-probes/recordings/k4_client.out.txt</c>).
/// </summary>
[Collection("JG facade")]
public sealed class ComponentTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k4-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");

    public ComponentTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output), "a runtime was left running");
    }

    public void Dispose()
    {
        JgRuntime.Terminate();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", _cacheBefore);
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    /// <summary>The probe's own k4.m, the function R2025b's components ran.</summary>
    private static string K4Source()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder, "tools", "matlab-checklist", "compiler-probes", "src", "k4", "k4.m"));
    }

    /// <summary>Builds a library archive of <paramref name="files"/> (name, text) whose public functions are <paramref name="functions"/>.</summary>
    private string Library(string component, IReadOnlyList<(string Name, string Text)> files, IReadOnlyList<string> functions, IReadOnlyList<string>? options = null)
    {
        string source = Directory.CreateDirectory(Path.Combine(_root, "src_" + component)).FullName;
        var entries = new List<(string Source, string ArchivePath)>();
        foreach ((string name, string text) in files)
        {
            string path = Path.Combine(source, name);
            File.WriteAllText(path, text.ReplaceLineEndings("\n"));
            entries.Add((path, component + "/" + name));
        }

        var build = new CtfBuild
        {
            ComponentName = component,
            TargetType = CtfTargetType.DotNetAssembly,
            Files = entries,
            MatlabPath = [component],
            PublicFunctions = [.. functions.Select(f => new CtfFunction(f, component + "/" + f + ".m", [], []))],
            RuntimeOptions = options ?? [],
        };
        string archive = Path.Combine(_root, component + ".ctf");
        CtfArchiveWriter.Write(archive, build);
        return archive;
    }

    private static string Op(Component component, string op, RuntimeValue? arg = null) =>
        Assert.IsType<RuntimeChar>(Assert.Single(component.Call("k4", 1, RuntimeValue.Text(op), arg ?? RuntimeValue.Empty()))).Characters;

    [Fact]
    public async Task K4Client_TwoInstancesShareOneSession_ASecondComponentHasItsOwn_AsR2025bsRuntime()
    {
        string a = Library("K4A", [("k4.m", K4Source())], ["k4"]);
        string b = Library("K4B", [("k4.m", K4Source())], ["k4"]);
        string start = Environment.CurrentDirectory;

        using Component a1 = Component.Open(a);
        using Component a2 = Component.Open(a);
        using Component b1 = Component.Open(b);
        Assert.Equal(["k4"], a1.Functions);

        // quiet: the beep's switch is the session's, so a2 sees what a1 set.
        Assert.Equal(("on", "off", "on"), (Op(a1, "quiet"), Op(a2, "quiet"), Op(b1, "quiet")));
        Assert.Equal("1 0", Op(a1, "deployed"));
        Assert.Equal(a1.CtfRoot, Op(a1, "ctfroot"));
        Assert.Equal(a1.CtfRoot, Op(a2, "ctfroot"));
        Assert.Equal(b1.CtfRoot, Op(b1, "ctfroot"));
        Assert.NotEqual(a1.CtfRoot, b1.CtfRoot);

        Assert.Equal("7", Op(a1, "setg", RuntimeValue.Scalar(7)));
        Assert.Equal(("7", "7", "[] double"), (Op(a1, "getg"), Op(a2, "getg"), Op(b1, "getg")));

        // A persistent variable counting every call: the instances' calls add up, the other component's do not.
        Assert.Equal(("9", "10", "4"), (Op(a1, "count"), Op(a2, "count"), Op(b1, "count")));

        // cd is the component's: its other instance follows it, the other component stays. R2025b
        // also moved the process's own current folder; JGraph leaves it alone.
        string folder = Directory.CreateDirectory(Path.Combine(_root, "k4client_cd")).FullName;
        Assert.Equal(start, Op(a1, "pwd"));
        Assert.Equal(folder, Op(a1, "cd", RuntimeValue.Text(folder)));
        Assert.Equal(folder, Op(a2, "pwd"));
        Assert.Equal(start, Op(b1, "pwd"));
        Assert.Equal(start, Environment.CurrentDirectory);

        // The random stream: a2 draws the seeded stream's first number and a1 its second, b its own.
        // R2025b's were the twister's (0.8147 for b, 0.4170 and 0.7203); JGraph's generator is its own.
        Assert.Equal("seeded", Op(a1, "rng", RuntimeValue.Scalar(1)));
        string drawnByB = Op(b1, "rand");
        string firstSeeded = Op(a2, "rand");
        string secondSeeded = Op(a1, "rand");
        Op(a1, "rng", RuntimeValue.Scalar(1));
        Assert.Equal((firstSeeded, secondSeeded), (Op(a1, "rand"), Op(a2, "rand")));
        Assert.NotEqual(firstSeeded, drawnByB);

        // format is the component's. R2025b's client set long over its short default; JGraph's
        // default is long, so the switch measured is to short.
        Assert.Equal("short", Op(a1, "format", RuntimeValue.Text("short")));
        Assert.Equal(("3.1416", "3.1416", "3.141592653589793"), (Op(a1, "show"), Op(a2, "show"), Op(b1, "show")));

        // The warning state: off in a1 is off in a2; b's still prints it. lastwarn is set either way.
        Assert.Equal("off", Op(a1, "warnoff", RuntimeValue.Text("k4:w")));
        _output.Mark();
        Assert.Equal("k4:w | k4 warning", Op(b1, "warn"));
        Assert.Contains("Warning: k4 warning", _output.TextSinceMark, StringComparison.Ordinal);
        _output.Mark();
        Assert.Equal("k4:w | k4 warning", Op(a2, "warn"));
        Assert.Equal("k4:w | k4 warning", Op(a1, "warn"));
        Assert.DoesNotContain("Warning", _output.TextSinceMark, StringComparison.Ordinal);

        Assert.Equal("set", Op(a1, "setud", RuntimeValue.Text("v1")));
        Assert.Equal(("v1", "[] double"), (Op(a2, "getud"), Op(b1, "getud")));
        Assert.Equal("assigned", Op(a1, "base", RuntimeValue.Scalar(5)));
        Assert.Equal(("5", "ERR MATLAB:UndefinedFunction"), (Op(a2, "getbase"), Op(b1, "getbase")));

        _output.Mark();
        Assert.Equal("said", Op(a1, "say"));
        Assert.Equal("k4 disp line\nk4 fprintf line\n", _output.TextSinceMark.Replace("\r", string.Empty));
        Assert.Contains("k4 stderr line", _output.ErrorText, StringComparison.Ordinal);

        Assert.Equal("1 0", await Task.Run(() => Op(a1, "deployed")));

        // Figures are the component's: its other instance counts a1's figure, the other component none.
        Assert.Equal("1", Op(a1, "figure"));
        Assert.Equal(("1", "0"), (Op(a2, "figures"), Op(b1, "figures")));

        a1.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Op(a1, "getg"));
        Assert.Equal("7", Op(a2, "getg"));
        using Component a3 = Component.Open(a);
        Assert.Equal("7", Op(a3, "getg"));
        Assert.Equal("[] double", Op(b1, "getg"));
    }

    [Fact]
    public void Call_AnUncaughtError_IsADeployedException_WithItsIdentifier_Message_AndStack()
    {
        using Component k4 = Component.Open(Library("K4E", [("k4.m", K4Source())], ["k4"]));
        Op(k4, "quiet");
        var error = Assert.Throws<DeployedException>(() => k4.Call("k4", 1, RuntimeValue.Text("fail"), RuntimeValue.Scalar(3)));
        Assert.Equal(("k4:fail", "k4 failure 3"), (error.Identifier, error.Message));

        // R2025b's stack (k4_client): deeper at line 88, then k4 at line 37.
        Assert.Equal([("k4>deeper", 88), ("k4", 37)], error.Stack.Select(static f => (f.Name, f.Line)));
        Assert.All(error.Stack, f => Assert.Equal(Path.Combine(k4.CtfRoot, "K4E", "k4.m"), f.File, ignoreCase: true));
        Assert.Equal("Error using k4>deeper (line 88)\nk4 failure 3\n\nError in k4 (line 37)\n\nk4:fail", error.Report);

        // The session goes on.
        Assert.Equal("1 0", Op(k4, "deployed"));
    }

    [Fact]
    public void Call_RefusesAFunctionTheComponentDoesNotOffer_AndTooManyOutputs()
    {
        using Component k4 = Component.Open(Library("K4F", [("k4.m", K4Source()), ("hidden.m", "function hidden\nend\n")], ["k4"]));
        Assert.Throws<ArgumentException>(() => k4.Call("hidden", 0));
        var error = Assert.Throws<DeployedException>(() => k4.Call("k4", 2, RuntimeValue.Text("pwd"), RuntimeValue.Empty()));
        Assert.Equal("MATLAB:TooManyOutputs", error.Identifier);
        Assert.Empty(k4.Call("k4", 0, RuntimeValue.Text("pwd"), RuntimeValue.Empty()));
    }

    [Fact]
    public async Task Calls_FromTwoThreads_WaitTheirTurn_AsR2025bsDo()
    {
        string waits = "function t = waits(s)\nt = tic;\npause(s);\nt = toc(t);\nend\n";
        using Component one = Component.Open(Library("K4W", [("waits.m", waits)], ["waits"]));
        using Component two = Component.Open(Library("K4X", [("waits.m", waits)], ["waits"]));
        one.Call("waits", 1, RuntimeValue.Scalar(0));
        two.Call("waits", 1, RuntimeValue.Scalar(0));

        var clock = Stopwatch.StartNew();
        Task<RuntimeValue[]> first = Task.Run(() => one.Call("waits", 1, RuntimeValue.Scalar(0.4)));
        Task<RuntimeValue[]> second = Task.Run(() => two.Call("waits", 1, RuntimeValue.Scalar(0.4)));
        await Task.WhenAll(first, second);
        Assert.True(clock.Elapsed.TotalSeconds >= 0.75, $"two 0.4 s calls overlapped: {clock.Elapsed.TotalSeconds:F2} s");
    }

    [Fact]
    public async Task CallAsync_ACancelledToken_StopsTheFunction()
    {
        using Component spin = Component.Open(Library("K4C", [("spin.m", "function spin\nwhile true\nend\nend\n")], ["spin"]));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => spin.CallAsync("spin", 0, [], cancel.Token));
    }

    [Fact]
    public void Terminate_PutsBackTheFiguresTheProcessHeldBefore()
    {
        JgRuntime.Terminate();
        JG.Reset();
        JG.Figure();
        int before = JG.CurrentFigureNumber;
        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output));
        using Component k4 = Component.Open(Library("K4H", [("k4.m", K4Source())], ["k4"]));
        Op(k4, "quiet");
        Assert.Equal("0", Op(k4, "figures"));
        Assert.Equal("1", Op(k4, "figure"));
        JgRuntime.Terminate();

        Assert.Equal([before], JG.FigureNumbers);
        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output));
    }

    [Fact]
    public void Open_UnderRuntimeOptions_PrintsTheStartLines_AndKeepsTheLog()
    {
        JgRuntime.Terminate();
        string folder = Directory.CreateDirectory(Path.Combine(_root, "run")).FullName;
        string before = Environment.CurrentDirectory;
        Environment.CurrentDirectory = folder;
        try
        {
            string library = Library("K4R", [("k4.m", K4Source())], ["k4"], ["-startmsg,K4 start,-completemsg,K4 complete", "-logfile,k4.log", "-bogus"]);
            Assert.True(JgRuntime.Initialize(RuntimeOptions.Parse(new CtfArchiveReaderManifest(library).RuntimeOptions), _output));
            Assert.Equal(
                ["Warning: Unrecognized command line option: bogus.", "Initializing JGraph Runtime version 25.2", "K4 start", "K4 complete"],
                _output.NormalLines);

            using Component k4 = Component.Open(library);
            Op(k4, "quiet");
            Op(k4, "say");
            JgRuntime.Terminate();
            Assert.Equal("k4 disp line\nk4 fprintf line\nk4 stderr line\n", File.ReadAllText(Path.Combine(folder, "k4.log")));
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }

        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output));
    }

    /// <summary>An archive's manifest, read and closed.</summary>
    private sealed class CtfArchiveReaderManifest(string path)
    {
        public IReadOnlyList<string> RuntimeOptions { get; } = Read(path);

        private static IReadOnlyList<string> Read(string path)
        {
            using CtfArchiveReader reader = CtfArchiveReader.Open(path);
            return reader.Manifest.RuntimeOptions;
        }
    }
}
