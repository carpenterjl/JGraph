using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Deploy;
using JGraph.Imaging;
using JGraph.Runtime;
using JGraph.Scripting;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// A deployed component's figures in windows and its events between calls (compiler and SDK plan,
/// stage K5): each component's figures go to its own windows, a window's report runs in the state of
/// the component that owns it whichever one is live, a timer ticks while the client makes no call
/// (<c>tools/matlab-checklist/compiler-probes/recordings/k5_client.out.txt</c>, R2025b's runtime
/// running the repo's <c>k5.m</c>), and the runtime's end warns of invisible figures as R2025b's does.
/// The window host is a stand-in recording what it is asked: the WPF one is checked in a window check.
/// </summary>
[Collection("JG facade")]
public sealed class DesktopRoutingTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly FakeDesktop _desktop = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k5-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");

    public DesktopRoutingTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output), "a runtime was left running");
    }

    public void Dispose()
    {
        JgRuntime.Terminate();
        JgRuntime.DesktopOverride = null;
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

    /// <summary>A component of one function, k5w, whose operations make windows and count their callbacks.</summary>
    private const string WindowSource = """
        function out = k5w(op, arg)
        global K5W
        switch op
            case 'quiet'
                out = beep;
                beep('off');
            case 'window'
                K5W = 0;
                f = figure('Name', arg);
                set(f, 'WindowButtonDownFcn', @(~, ~) bump(1));
                out = mat2str(f.Number);
            case 'closable'
                K5W = 0;
                f = figure('Name', arg, 'CloseRequestFcn', @(src, ~) closing(src));
                out = mat2str(f.Number);
            case 'count'
                out = mat2str(K5W);
            case 'waitall'
                names = waitforallfiguresclosed;
                out = mat2str(numel(names));
            case 'figures'
                out = mat2str(numel(findall(groot, 'Type', 'figure')));
            case 'export'
                f = figure('Visible', 'off');
                plot(1:3, [2 4 3]);
                exportgraphics(f, arg);
                close(f);
                out = 'exported';
        end
        end

        function bump(n)
        global K5W
        K5W = K5W + n;
        end

        function closing(src)
        global K5W
        K5W = K5W + 100;
        delete(src);
        end
        """;

    /// <summary>The probe's own k5.m, the function R2025b's component ran.</summary>
    private static string K5Source()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder, "tools", "matlab-checklist", "compiler-probes", "src", "k5", "k5.m"));
    }

    /// <summary>Builds a library archive of one file whose one public function is its name.</summary>
    private string Library(string component, string function, string text)
    {
        string source = Directory.CreateDirectory(Path.Combine(_root, "src_" + component)).FullName;
        string path = Path.Combine(source, function + ".m");
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        var build = new CtfBuild
        {
            ComponentName = component,
            TargetType = CtfTargetType.DotNetAssembly,
            Files = [(path, component + "/" + function + ".m")],
            MatlabPath = [component],
            PublicFunctions = [new CtfFunction(function, component + "/" + function + ".m", [], [])],
            RuntimeOptions = [],
        };
        string archive = Path.Combine(_root, component + ".ctf");
        CtfArchiveWriter.Write(archive, build);
        return archive;
    }

    private static string Op(Component component, string function, string op, RuntimeValue? arg = null) =>
        Assert.IsType<RuntimeChar>(Assert.Single(component.Call(function, 1, RuntimeValue.Text(op), arg ?? RuntimeValue.Empty()))).Characters;

    /// <summary>What a window does when its user presses a mouse button over it: reported from the window's own thread.</summary>
    private static Task Click(FigureModel figure) =>
        Task.Run(() => ScriptGraphicsCallbacks.NotifyWindowButton(figure, pressed: true, SelectionKind.Normal, (5, 5)));

    [Fact]
    public void K5_EachComponentsFiguresGoToItsOwnWindows_EachNumberingItsOwn()
    {
        JgRuntime.DesktopOverride = _desktop;
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        using Component b = Component.Open(Library("K5B", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        Op(b, "k5w", "quiet");

        Assert.Equal("1", Op(a, "k5w", "window", RuntimeValue.Text("of a")));
        Assert.Equal("1", Op(b, "k5w", "window", RuntimeValue.Text("of b")));
        (object Owner, int Number, FigureModel Figure)[] shown = _desktop.ShownOnce();
        Assert.Equal(2, shown.Length);
        Assert.Equal((1, 1), (shown[0].Number, shown[1].Number));
        Assert.NotSame(shown[0].Owner, shown[1].Owner);
        Assert.Equal(("of a", "of b"), (shown[0].Figure.Name, shown[1].Figure.Name));
    }

    [Fact]
    public async Task K5_AClickBetweenCalls_RunsItsCallbackInTheComponentThatOwnsTheWindow()
    {
        JgRuntime.DesktopOverride = _desktop;
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        using Component b = Component.Open(Library("K5B", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        Op(b, "k5w", "quiet");
        Op(a, "k5w", "window", RuntimeValue.Text("of a"));
        Op(b, "k5w", "window", RuntimeValue.Text("of b"));
        (object Owner, int Number, FigureModel Figure)[] shown = _desktop.ShownOnce();
        FigureModel ofA = shown[0].Figure;
        FigureModel ofB = shown[1].Figure;

        // b is the live component: a's window is clicked, and its callback runs in a.
        await Click(ofA);
        Assert.Equal(("1", "0"), (Op(a, "k5w", "count"), Op(b, "k5w", "count")));

        // Now b's is clicked while b is the live one, and a's while a is.
        await Click(ofB);
        Assert.Equal("1", Op(b, "k5w", "count"));
        await Click(ofA);
        Assert.Equal(("2", "1"), (Op(a, "k5w", "count"), Op(b, "k5w", "count")));
    }

    [Fact]
    public async Task K5_TheCloseBox_IsAskedInTheOwningComponent_AndClosesWhereNothingAnswers()
    {
        JgRuntime.DesktopOverride = _desktop;
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        using Component b = Component.Open(Library("K5B", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        Op(b, "k5w", "quiet");
        Op(a, "k5w", "closable", RuntimeValue.Text("of a"));
        Op(b, "k5w", "window", RuntimeValue.Text("of b"));
        (object Owner, int Number, FigureModel Figure)[] shown = _desktop.ShownOnce();

        // a's window, with a CloseRequestFcn, while b is live: it stays until a has been asked, and
        // a's callback deletes the figure, which closes the window through the host.
        bool closedAnyway = false;
        bool stays = await Task.Run(() => ScriptGraphicsCallbacks.RequestClose(shown[0].Figure, () => closedAnyway = true));
        Assert.True(stays);
        Assert.Equal(("100", "0"), (Op(a, "k5w", "count"), Op(a, "k5w", "figures")));
        Assert.False(closedAnyway);
        Assert.Contains((shown[0].Owner, 1), _desktop.ClosedOnce());

        // b's, with none, while a is live: it stays for now and closes once b has been asked.
        using var asked = new ManualResetEventSlim();
        stays = await Task.Run(() => ScriptGraphicsCallbacks.RequestClose(shown[1].Figure, asked.Set));
        Assert.True(stays);
        Assert.True(asked.Wait(TimeSpan.FromSeconds(10)), "b was never asked");

        // The window then closes, which retires the figure in b.
        await Task.Run(() => ScriptGraphicsCallbacks.NotifyWindowClosed(shown[1].Figure, 1));
        Assert.Equal(("0", "0"), (Op(b, "k5w", "figures"), Op(a, "k5w", "figures")));
    }

    [Fact]
    public async Task K5_WaitForFiguresToClose_WaitsWhileAWindowIsOnScreen()
    {
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        Op(a, "k5w", "quiet");

        // No window host yet: nothing can be on screen.
        a.WaitForFiguresToClose();

        JgRuntime.DesktopOverride = _desktop;
        _desktop.Visible = true;
        Task waiting = Task.Run(() => a.WaitForFiguresToClose());
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);
        _desktop.Visible = false;
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task K5_WaitforallfiguresclosedInACall_WaitsUntilTheComponentsWindowCloses()
    {
        JgRuntime.DesktopOverride = _desktop;
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        Op(a, "k5w", "window", RuntimeValue.Text("of a"));
        FigureModel shown = _desktop.ShownOnce()[0].Figure;

        // The call waits in the engine; the window closing, reported from the window's thread, ends it.
        Task<string> waiting = Task.Run(() => Op(a, "k5w", "waitall"));
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);
        await Task.Run(() => ScriptGraphicsCallbacks.NotifyWindowClosed(shown, 1));
        Assert.Equal("0", await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("0", Op(a, "k5w", "figures"));
    }

    [Fact]
    public void K5_ExportgraphicsInAComponent_WritesTheFile()
    {
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        string png = Path.Combine(_root, "k5w.png");
        Assert.Equal("exported", Op(a, "k5w", "export", RuntimeValue.Text(png)));
        Assert.True(new FileInfo(png).Length > 0);
    }

    [Fact]
    public void K5_Terminating_RetiresEachComponentsWindows()
    {
        JgRuntime.DesktopOverride = _desktop;
        using Component a = Component.Open(Library("K5A", "k5w", WindowSource));
        using Component b = Component.Open(Library("K5B", "k5w", WindowSource));
        Op(a, "k5w", "quiet");
        Op(b, "k5w", "quiet");
        Op(a, "k5w", "window", RuntimeValue.Text("of a"));
        object owner = _desktop.ShownOnce()[0].Owner;
        JgRuntime.Terminate();
        Assert.Equal(2, _desktop.RetiredOnce().Length);
        Assert.Contains(owner, _desktop.RetiredOnce());
    }

    [Fact]
    public void K5Client_ATimerTicksBetweenCalls_AndTheEndWarnsOfInvisibleFigures_AsR2025bsRuntime()
    {
        using Component k5 = Component.Open(Library("K5", "k5", K5Source()));
        string Run(string op, RuntimeValue? arg = null) => Op(k5, "k5", op, arg);
        int Ticks(string text) => text.Split('\n').Count(static line => line.StartsWith("k5 tick ", StringComparison.Ordinal));

        Assert.Equal("on", Run("quiet"));

        // R2025b: tick 1 before 'started' came back, ticks 2 to 5 and the stop while the client slept.
        _output.Mark();
        Assert.Equal("started", Run("timer", RuntimeValue.Scalar(5)));
        Assert.Equal(1, Ticks(_output.TextSinceMark));
        _output.Mark();
        Thread.Sleep(1500);
        string slept = _output.TextSinceMark;
        Assert.Equal(4, Ticks(slept));
        Assert.Contains("k5 timer stopped", slept, StringComparison.Ordinal);
        Assert.Equal("5", Run("ticks"));
        Thread.Sleep(500);

        // A call that pauses: the ticks run inside it.
        Assert.Equal("started", Run("timer", RuntimeValue.Scalar(5)));
        Assert.Equal("1", Run("ticks"));
        Assert.Equal("1.0", Run("wait", RuntimeValue.Scalar(1)));
        Assert.Equal("5", Run("ticks"));
        Thread.Sleep(500);

        // A call that computes without pausing: they run inside that too.
        Assert.Equal("started", Run("timer", RuntimeValue.Scalar(5)));
        Assert.Equal("spun", Run("spin", RuntimeValue.Scalar(1)));
        Assert.Equal("5", Run("ticks"));

        // WaitForFiguresToDie with nothing open, and with an invisible figure: at once both times.
        k5.WaitForFiguresToClose();
        Assert.Equal(("1", "1"), (Run("figure"), Run("figures")));
        k5.WaitForFiguresToClose();
        Assert.Equal("closed", Run("closeall"));
        Assert.Equal("0", Run("figures"));
        Assert.Equal("deleted", Run("deleteall"));
        Assert.Equal("0", Run("figures"));

        // One invisible figure left: R2025b's warning when the runtime ends, its runtime renamed.
        Assert.Equal("1", Run("figure"));
        JgRuntime.Terminate();
        Assert.Equal(
            [
                "Warning: 1 invisible figure(s) at JGraph Runtime termination.",
                " Figure ID(s):",
                "",
                "Name: k5 invisible",
                "",
                "If your application has terminated unexpectedly, please note that ",
                " applications generated by the MATLAB Compiler terminate when there are no ",
                " visible figure windows.",
            ],
            _output.Errors.TakeLast(8));
    }

    /// <summary>A window host that records what it is asked and shows nothing.</summary>
    private sealed class FakeDesktop : IRuntimeDesktop
    {
        private readonly object _gate = new();
        private readonly List<(object Owner, int Number, FigureModel Figure)> _shown = [];
        private readonly List<(object Owner, int Number)> _closed = [];
        private readonly List<object> _retired = [];

        public volatile bool Visible;

        public bool IsWindowThread => false;

        public IScriptFigureFiles FigureFiles { get; } = new NoFigureFiles();

        /// <summary>Each figure shown, once, in the order first shown.</summary>
        public (object Owner, int Number, FigureModel Figure)[] ShownOnce()
        {
            lock (_gate)
            {
                return [.. _shown.DistinctBy(static s => s.Figure)];
            }
        }

        public (object Owner, int Number)[] ClosedOnce()
        {
            lock (_gate)
            {
                return [.. _closed.Distinct()];
            }
        }

        public object[] RetiredOnce()
        {
            lock (_gate)
            {
                return [.. _retired.Distinct()];
            }
        }

        public void Show(object owner, int number, FigureModel figure)
        {
            lock (_gate)
            {
                _shown.Add((owner, number, figure));
            }
        }

        public void Close(object owner, int number)
        {
            lock (_gate)
            {
                _closed.Add((owner, number));
            }
        }

        public void Retire(object owner)
        {
            lock (_gate)
            {
                _retired.Add(owner);
            }
        }

        public bool AnyVisible(object owner) => Visible;
    }

    private sealed class NoFigureFiles : IScriptFigureFiles
    {
        public void Save(FigureModel figure, string path) => throw new NotSupportedException();

        public FigureModel Load(string path) => throw new NotSupportedException();

        public void Export(FigureModel figure, string path, double scale = 1.0, Size2D? size = null) => throw new NotSupportedException();

        public ImageBuffer Capture(FigureModel figure, double scale) => throw new NotSupportedException();

        public bool CopyToClipboard(FigureModel figure, double scale) => false;
    }
}
