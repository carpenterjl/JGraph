using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.PythonConsole;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The live out-of-process Python console. These need a real CPython on the machine, so — like every
/// other Python test here — they sit behind the <c>!~Python</c> gate filter and each one skips itself
/// when no interpreter is installed.
/// </summary>
[Collection("JG facade")]
public class PythonReplSessionTests : IDisposable
{
    private static readonly PythonRuntimeInfo? Runtime = PythonLocator.Find();

    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public PythonReplSessionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static bool Available => Runtime?.Executable is { Length: > 0 }
        && PythonReplSession.FindConsoleScript() is not null;

    private IScriptSession NewSession() =>
        new PythonScriptEngine().CreateSession(new ScriptContext(_output, (_, figure) => _figures.Add(figure)));

    private static Task<ScriptRunResult> Exec(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public void TheEngine_AdvertisesTheReplCapability() =>
        Assert.IsAssignableFrom<IScriptRepl>(new PythonScriptEngine());

    [Fact]
    public void TheConsoleScript_IsDeployedBesideTheAssembly() =>
        Assert.NotNull(PythonReplSession.FindConsoleScript());

    [Fact]
    public async Task Variables_SurviveBetweenStatements()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        Assert.True((await Exec(session, "x = 21")).Success);
        ScriptRunResult result = await Exec(session, "y = x * 2");

        Assert.True(result.Success, result.Message);
        Assert.Equal("42", Assert.Single(result.Variables, v => v.Name == "y").DisplayValue);
    }

    [Fact]
    public async Task PrintOutput_ReachesTheHostConsole()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        await Exec(session, "print('hello from the child')");

        Assert.Contains("hello from the child", _output.NormalText);
    }

    [Fact]
    public async Task ABareExpression_EchoesItsValue()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        await Exec(session, "6 * 7");

        Assert.Contains("42", _output.NormalText);
    }

    [Fact]
    public async Task AFailedStatement_ReportsTheError_AndTheSessionKeepsGoing()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();
        await Exec(session, "keep = 5");

        ScriptRunResult failure = await Exec(session, "1 / 0");

        Assert.False(failure.Success);
        Assert.Contains("ZeroDivisionError", failure.Message);
        Assert.True((await Exec(session, "after = 1")).Success);
    }

    [Fact]
    public async Task PlottingFromTheChild_ReachesARealFigure()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Exec(session, "plot([0, 1, 2], [0, 1, 4]); title('from Python')");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.FiguresShown);
        Assert.Equal("from Python", Assert.Single(_figures).Axes[0].Title);
    }

    [Fact]
    public async Task ListVariables_CarryTheirDataForTheGrid()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Exec(session, "xs = [1.5, 2.5, 3.5]");

        ScriptVariable xs = Assert.Single(result.Variables, v => v.Name == "xs");
        Assert.Equal("array", xs.Type);
        Assert.Equal(new[] { 1.5, 2.5, 3.5 }, Assert.IsType<double[]>(xs.RawValue));
    }

    [Fact]
    public async Task ExitAtThePrompt_ComesBackAsAnExitCode()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Exec(session, "exit(3)");

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task Cancellation_KillsTheChild_AndTheNextStatementStartsAFreshOne()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();
        await Exec(session, "gone = 1");
        using var cts = new CancellationTokenSource();

        Task<ScriptRunResult> run = session.ExecuteAsync("while True: pass", sourceId: "", cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));
        ScriptRunResult cancelled = await run;

        Assert.False(cancelled.Success);
        Assert.Contains("restarted", cancelled.Message);
        Assert.True((await Exec(session, "fresh = 2")).Success);
        Assert.DoesNotContain(session.GetVariables(), v => v.Name == "gone"); // the namespace went with it
    }

    [Fact]
    public async Task Clear_RestartsTheChild_WithAnEmptyNamespace()
    {
        if (!Available)
        {
            return;
        }

        await using IScriptSession session = NewSession();
        await Exec(session, "gone = 1");

        session.Clear();

        Assert.Empty(session.GetVariables());
        Assert.True((await Exec(session, "fresh = 2")).Success);
        Assert.Equal("fresh", Assert.Single(session.GetVariables()).Name);
    }
}
