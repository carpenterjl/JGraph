using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public class PythonScriptEngineTests : IDisposable
{
    public PythonScriptEngineTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    [Fact]
    public void Engine_ReportsPythonLanguage()
    {
        Assert.Equal("Python", new PythonScriptEngine().Language);
    }

    [Fact]
    public async Task RunAsync_RunsWhenAvailable_OrDegradesGracefullyWhenNot()
    {
        var figures = new List<FigureModel>();
        var output = new RecordingScriptOutput();
        var engine = new PythonScriptEngine();
        var context = new ScriptContext(output, (_, figure) => figures.Add(figure));

        const string code = """
            JG.Plot([0.0, 1.0, 2.0], [0.0, 1.0, 4.0], "r-")
            JG.Title("From Python")
            print("hello from python")
            show()
            """;

        ScriptRunResult result = await engine.RunAsync(code, context, CancellationToken.None);

        if (engine.IsAvailable)
        {
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.FiguresShown);
            Assert.Single(figures);
            Assert.Contains("hello from python", output.NormalText);
        }
        else
        {
            // Graceful degradation: a clear message, no exception, no partial figure.
            Assert.False(result.Success);
            Assert.Contains("Python runtime not found", result.Message);
            Assert.Empty(figures);
        }
    }

    [Fact]
    public async Task RunAsync_WhenAvailable_ReportsRuntimeErrorGracefully()
    {
        var engine = new PythonScriptEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { });

        ScriptRunResult result = await engine.RunAsync("raise ValueError('boom')", context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Message);
        Assert.NotEmpty(result.Diagnostics);
    }
}
