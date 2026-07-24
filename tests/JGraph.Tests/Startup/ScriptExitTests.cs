using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Covers <c>exit</c>/<c>quit</c>: a script ending itself is a success that carries a process exit
/// code, not a failure, and the request must unwind loops and functions rather than be catchable.
/// </summary>
[Collection("JG facade")]
public class ScriptExitTests : IDisposable
{
    private readonly JgsScriptEngine _jgs = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public ScriptExitTests() => JG.Reset();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        JG.Reset();
    }

    [Fact]
    public async Task Exit_WithoutACode_SucceedsWithZero()
    {
        ScriptRunResult result = await RunJgsAsync("disp('before'); exit(); disp('after')");

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("before", _output.NormalText);
        Assert.DoesNotContain("after", _output.NormalText);
        Assert.Empty(_output.Errors);
    }

    [Theory]
    [InlineData("exit(3)", 3)]
    [InlineData("quit(4)", 4)]
    [InlineData("quit()", 0)]
    public async Task Exit_CarriesTheCodeTheScriptAsksFor(string statement, int expected)
    {
        ScriptRunResult result = await RunJgsAsync(statement);

        Assert.True(result.Success);
        Assert.Equal(expected, result.ExitCode);
    }

    [Fact]
    public async Task Exit_UnwindsOutOfLoopsAndFunctions()
    {
        ScriptRunResult result = await RunJgsAsync("""
            fn stop() {
                exit(7)
            }

            for k = 1:100 {
                if k == 3 {
                    stop()
                }
                disp(k)
            }
            disp('never')
            """);

        Assert.Equal(7, result.ExitCode);
        Assert.DoesNotContain("never", _output.NormalText);
    }

    [Fact]
    public async Task ExitedRun_KeepsTheFiguresTheScriptAlreadyBuilt()
    {
        ScriptRunResult result = await RunJgsAsync("plot([1, 2, 3]); show(); exit(2)");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(1, result.FiguresShown);
        Assert.Single(_figures);
    }

    [Fact]
    public async Task OrdinaryRun_HasNoExitCode()
    {
        ScriptRunResult result = await RunJgsAsync("disp('done')");

        Assert.True(result.Success);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Exit_WorksFromTheCSharpEngineToo()
    {
        var engine = new CSharpScriptEngine();

        ScriptRunResult result = await engine.RunAsync("exit(5);", Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public async Task BatchRunner_ReturnsTheScriptsExitCodeVerbatim()
    {
        var options = new StartupOptions(StartupMode.Batch, "disp('bye'); exit(3)");

        int code = await BatchRunner.RunAsync(
            options, new IScriptEngine[] { _jgs }, _output, (_, _) => { });

        Assert.Equal(3, code);
        Assert.Contains("bye", _output.NormalText);
    }

    private Task<ScriptRunResult> RunJgsAsync(string code) =>
        _jgs.RunAsync(code, Context(), CancellationToken.None);

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure));
}
