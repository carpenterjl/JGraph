using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// What the console prints, and what lands in <c>ans</c>. JGraph shows values on one compact line
/// rather than MATLAB's indented block, but the rules about <em>when</em> something is echoed and
/// what <c>ans</c> holds afterwards have to match MATLAB exactly.
/// </summary>
[Collection("JG facade")]
public class MatlabConsoleEchoTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabConsoleEchoTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Prompt(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    [Fact]
    public async Task AnAssignmentEchoesTheVariable_AndASemicolonSuppressesIt()
    {
        await using IScriptSession session = NewSession();

        await Prompt(session, "x = 3");
        Assert.Equal(new[] { "x = 3" }, _output.NormalLines);

        _output.Mark();
        await Prompt(session, "y = 4;");
        Assert.Equal(string.Empty, _output.TextSinceMark);
    }

    [Fact]
    public async Task ABareExpressionBindsAndEchoesAns_AndAnsChains()
    {
        await using IScriptSession session = NewSession();

        await Prompt(session, "3 + 4");
        Assert.Equal(new[] { "ans = 7" }, _output.NormalLines);

        ScriptRunResult result = await Prompt(session, "ans * 2");

        Assert.Equal(14d, Assert.Single(result.Variables, v => v.Name == "ans").RawValue);
    }

    [Fact]
    public async Task FigureAsAStatement_PrintsNothingAndSetsNoAns()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "figure(1)");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(string.Empty, _output.NormalText);
        Assert.DoesNotContain(result.Variables, static v => v.Name == "ans");
    }

    [Fact]
    public async Task BareFigure_PrintsNothingEither()
    {
        await using IScriptSession session = NewSession();

        await Prompt(session, "figure");

        Assert.Equal(string.Empty, _output.NormalText);
    }

    [Fact]
    public async Task AssigningFigure_StillHandsBackTheHandle()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Prompt(session, "h = figure(2)");

        Assert.Equal(2d, Assert.Single(result.Variables, v => v.Name == "h").RawValue);
        Assert.Equal(new[] { "h = 2" }, _output.NormalLines);
    }

    [Fact]
    public async Task RunningAFunctionFile_LeavesNoAnsInTheBaseWorkspace()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await session.ExecuteFileAsync(
            "function work\n1 + 1\n", sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("ans = 2", _output.NormalText, StringComparison.Ordinal); // echoed, as MATLAB does
        Assert.DoesNotContain(session.GetVariables(), static v => v.Name == "ans");
    }

    [Fact]
    public async Task AVerbThatReturnsNothing_EchoesNothing()
    {
        await using IScriptSession session = NewSession();
        await Prompt(session, "plot([0 1], [0 1]);");

        _output.Mark();
        await Prompt(session, "title('hello')");

        Assert.Equal(string.Empty, _output.TextSinceMark);
    }
}
