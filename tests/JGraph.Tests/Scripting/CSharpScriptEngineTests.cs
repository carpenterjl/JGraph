using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public class CSharpScriptEngineTests : IDisposable
{
    private readonly CSharpScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public CSharpScriptEngineTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure));

    [Fact]
    public void Engine_IsAlwaysAvailable()
    {
        Assert.True(_engine.IsAvailable);
        Assert.Equal("C#", _engine.Language);
    }

    [Fact]
    public async Task RunAsync_BuildsFigureThroughStaticJgApi_AndDisplaysIt()
    {
        const string code = """
            Plot(new double[] { 0, 1, 2 }, new double[] { 0, 1, 4 }, "r-");
            Title("From C#");
            Legend("series");
            show();
            """;

        ScriptRunResult result = await _engine.RunAsync(code, Context(), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.FiguresShown);
        FigureModel figure = Assert.Single(_figures);
        Assert.Single(figure.Axes[0].Plots);
        Assert.Equal("From C#", figure.Axes[0].Title);
    }

    [Fact]
    public async Task RunAsync_CapturesPrintOutput()
    {
        ScriptRunResult result = await _engine.RunAsync("print(\"hello world\");", Context(), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("hello world", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_OnSyntaxError_ReportsDiagnosticsWithLocation()
    {
        ScriptRunResult result = await _engine.RunAsync("Plot(this is not valid", Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.IsError);
        Assert.All(result.Diagnostics, d => Assert.True(d.Line >= 1));
    }

    [Fact]
    public async Task RunAsync_OnRuntimeException_FailsWithMessage()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "throw new System.InvalidOperationException(\"boom\");", Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Message);
    }

    [Fact]
    public async Task RunAsync_WithAlreadyCancelledToken_DoesNotRun()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.RunAsync("var a = 1;", Context(), cts.Token));
    }
}
