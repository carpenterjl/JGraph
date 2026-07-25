using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The C# console session. Roslyn's <c>ContinueWithAsync</c> does the work; these pin the behaviour a
/// prompt needs on top of it — state that persists, a typo that does not destroy the workspace, and
/// expression echo.
/// </summary>
[Collection("JG facade")]
public class CSharpReplSessionTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public CSharpReplSessionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() =>
        new CSharpScriptEngine().CreateSession(new ScriptContext(_output, (_, figure) => _figures.Add(figure)));

    private static Task<ScriptRunResult> Exec(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public void Engine_OffersASession_ReportingItsOwnLanguage()
    {
        Assert.IsAssignableFrom<IScriptRepl>(new CSharpScriptEngine());
        Assert.Equal("C#", NewSession().Language);
    }

    [Fact]
    public async Task Variables_SurviveBetweenStatements()
    {
        await using IScriptSession session = NewSession();

        Assert.True((await Exec(session, "var x = 21;")).Success);
        ScriptRunResult result = await Exec(session, "var y = x * 2;");

        Assert.True(result.Success, result.Message);
        Assert.Equal("42", Assert.Single(result.Variables, v => v.Name == "y").DisplayValue);
    }

    [Fact]
    public async Task AnExpressionStatement_EchoesItsValue()
    {
        await using IScriptSession session = NewSession();

        await Exec(session, "1 + 1");

        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public async Task ACompilationError_ReportsDiagnostics_AndLeavesTheWorkspaceIntact()
    {
        await using IScriptSession session = NewSession();
        await Exec(session, "var keep = 5;");

        ScriptRunResult failure = await Exec(session, "var oops = ;");

        Assert.False(failure.Success);
        Assert.NotEmpty(failure.Diagnostics);
        Assert.Equal("5", Assert.Single(session.GetVariables(), v => v.Name == "keep").DisplayValue);
    }

    [Fact]
    public async Task PlottingAtThePrompt_ShowsTheFigureWithoutAnExplicitShow()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Exec(session, "Plot(new[] { 0.0, 1.0 }, new[] { 0.0, 1.0 });");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.FiguresShown);
        Assert.Single(_figures);
    }

    [Fact]
    public async Task Clear_DropsVariables_AndTheNextStatementStartsFresh()
    {
        await using IScriptSession session = NewSession();
        await Exec(session, "var gone = 1;");

        session.Clear();

        Assert.Empty(session.GetVariables());
        Assert.True((await Exec(session, "var fresh = 2;")).Success);
        Assert.Equal("fresh", Assert.Single(session.GetVariables()).Name);
    }
}
