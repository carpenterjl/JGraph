using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The interactive session seam: state that survives from one statement to the next, which is the one
/// thing a one-shot run deliberately does not give you.
/// </summary>
[Collection("JG facade")]
public class JgsReplSessionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsReplSessionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (number, figure) => _figures.Add((number, figure)));

    private IScriptSession NewSession(IScriptEngine engine) =>
        Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());

    private static Task<ScriptRunResult> Exec(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public async Task BothInterpreterEngines_OfferSessions_AndReportTheirOwnLanguage()
    {
        await using IScriptSession jgs = NewSession(new JgsScriptEngine());
        await using IScriptSession matlab = NewSession(new MatlabScriptEngine());

        Assert.Equal("JGS", jgs.Language);
        Assert.Equal("MATLAB", matlab.Language);
    }

    [Fact]
    public async Task Variables_SurviveBetweenStatements()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        Assert.True((await Exec(session, "let x = 21")).Success);
        ScriptRunResult result = await Exec(session, "let y = x * 2");

        Assert.True(result.Success, result.Message);
        ScriptVariable y = Assert.Single(result.Variables, v => v.Name == "y");
        Assert.Equal(42d, Assert.IsType<double>(y.RawValue));
    }

    [Fact]
    public async Task FunctionsDefinedAtThePrompt_AreCallableLater()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        await Exec(session, "fn double(v) { return v * 2 }");
        ScriptRunResult result = await Exec(session, "let z = double(4)");

        Assert.True(result.Success, result.Message);
        Assert.Equal(8d, Assert.Single(result.Variables, v => v.Name == "z").RawValue);
    }

    [Fact]
    public async Task GetVariables_MatchesTheLastResult_AndHidesUntouchedBuiltins()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Exec(session, "let a = 1");

        IReadOnlyList<ScriptVariable> variables = session.GetVariables();

        Assert.Equal("a", Assert.Single(variables).Name);
    }

    [Fact]
    public async Task AFailedStatement_LeavesEarlierWorkIntact()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Exec(session, "let keep = 7");

        ScriptRunResult failure = await Exec(session, "let bad = nosuchfunction(1)");

        Assert.False(failure.Success);
        Assert.Equal(7d, Assert.Single(session.GetVariables(), v => v.Name == "keep").RawValue);
    }

    [Fact]
    public async Task AStatementThatTouchesAFigure_RefreshesTheSameWindow()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        ScriptRunResult first = await Exec(session, "plot([0, 1], [0, 1])");
        ScriptRunResult second = await Exec(session, "title(\"second statement\")");

        Assert.Equal(1, first.FiguresShown);
        Assert.Equal(1, second.FiguresShown); // the same figure again, displayed with its new title
        Assert.Equal(2, _figures.Count);
        Assert.Same(_figures[0].Figure, _figures[1].Figure);
        Assert.Equal(1, _figures[1].Number);
        Assert.Equal("second statement", _figures[1].Figure.Axes[0].Title);
    }

    [Fact]
    public async Task ClearBuiltin_DropsVariables_ButLeavesBuiltinsAndFigures()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Exec(session, "plot([0, 1], [0, 1])");
        await Exec(session, "let gone = 99");

        ScriptRunResult result = await Exec(session, "clear");

        Assert.True(result.Success, result.Message);
        Assert.Empty(session.GetVariables());
        Assert.True((await Exec(session, "let again = sqrt(9)")).Success); // builtins still bound
        Assert.Single(JG.FigureNumbers);
    }

    [Fact]
    public async Task SessionClear_ResetsVariablesAndTheFigureRegistry()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Exec(session, "plot([0, 1], [0, 1])");
        await Exec(session, "let gone = 99");

        session.Clear();

        Assert.Empty(session.GetVariables());
        Assert.Empty(JG.FigureNumbers);
        Assert.True((await Exec(session, "let fresh = 1")).Success);
    }

    [Fact]
    public async Task MatlabSession_KeepsItsDialect_AcrossStatements()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        await Exec(session, "v = [10 20 30];");
        ScriptRunResult result = await Exec(session, "first = v(1);");

        Assert.True(result.Success, result.Message);
        Assert.Equal(10d, Assert.Single(result.Variables, x => x.Name == "first").RawValue); // 1-based
    }

    [Fact]
    public async Task MatlabSession_EchoesAnUnsuppressedStatement()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        await Exec(session, "x = 3");

        Assert.Contains("x", _output.NormalText);
        Assert.Contains("3", _output.NormalText);
    }

    [Fact]
    public async Task Cancellation_StopsALoop_AndTheSessionStaysUsable()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        using var cts = new CancellationTokenSource();
        await Exec(session, "let before = 1");

        Task<ScriptRunResult> run = session.ExecuteAsync("while true { }", sourceId: "", cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        ScriptRunResult cancelled = await run;

        Assert.False(cancelled.Success);
        Assert.Equal(1d, Assert.Single(session.GetVariables(), v => v.Name == "before").RawValue);
        Assert.True((await Exec(session, "let after = 2")).Success); // a fresh token, a fresh budget
    }

    [Fact]
    public async Task FunctionOnlyCodeAtThePrompt_DefinesWithoutInvoking()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult result = await Exec(session, "function poke\nfprintf('ran\\n');");

        Assert.True(result.Success, result.Message);
        Assert.DoesNotContain("ran", _output.NormalText, StringComparison.Ordinal);
        Assert.True((await Exec(session, "poke")).Success); // defined and callable, exactly once asked
        Assert.Contains("ran", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteFile_InvokesAMatlabFunctionFile()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());

        ScriptRunResult result = await session.ExecuteFileAsync(
            "function test1\nplot([1 2], [3 4])\ntitle('Figure 1')",
            sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, result.FiguresShown);
        Assert.Equal("Figure 1", Assert.Single(_figures).Figure.Axes[0].Title);
    }

    [Fact]
    public async Task ExecuteFile_InvokesAJgsFunctionOnlyFile()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        ScriptRunResult result = await session.ExecuteFileAsync(
            "fn main() { plot([0, 1], [0, 1]) }",
            sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, result.FiguresShown);
    }

    [Fact]
    public async Task ExecuteFile_RunsOrdinaryScriptsExactlyLikeExecute()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        ScriptRunResult result = await session.ExecuteFileAsync(
            "let x = 2\nlet y = x + 1", sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3d, Assert.Single(result.Variables, v => v.Name == "y").RawValue);
    }

    [Fact]
    public async Task ClcAtThePrompt_ClearsTheDisplay_ButLeavesTheWorkspaceAlone()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        await Exec(session, "let keep = 5");

        ScriptRunResult result = await Exec(session, "clc");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, _output.ClearCount);
        Assert.Equal(5d, Assert.Single(session.GetVariables(), v => v.Name == "keep").RawValue);
    }

    [Fact]
    public async Task ExitAtThePrompt_ComesBackAsAnExitCode_RatherThanAnError()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());

        ScriptRunResult result = await Exec(session, "exit(3)");

        Assert.True(result.Success);
        Assert.Equal(3, result.ExitCode);
    }
}
