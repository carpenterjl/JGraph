using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M19: figure handles and per-number show routing through the script seam.</summary>
[Collection("JG facade")]
public class JgsFigureWindowTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<(int Number, FigureModel Figure)> _shown = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsFigureWindowTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (n, f) => _shown.Add((n, f)), null), default);

    [Fact]
    public async Task Figure_ReturnsSequentialHandles_AndShowRoutesEachNumber()
    {
        ScriptRunResult result = await Run("""
            let a = figure()
            plot([1, 2], [3, 4])
            let b = figure()
            scatter([1], [1])
            print(a, b)
            show(a)
            show(b)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("1 2", _output.NormalText);
        Assert.Equal(2, _shown.Count);
        Assert.Equal(1, _shown[0].Number);
        Assert.Equal(2, _shown[1].Number);
        Assert.NotSame(_shown[0].Figure, _shown[1].Figure);
        Assert.Equal(2, result.FiguresShown);
    }

    [Fact]
    public async Task FigureN_RetargetsAnExistingFigure_ForInterleavedPlotting()
    {
        ScriptRunResult result = await Run("""
            figure(1)
            plot([1, 2], [3, 4])
            figure(2)
            plot([1], [1])
            figure(1)
            hold(true)
            plot([5, 6], [7, 8])
            show()
            """);

        Assert.True(result.Success, result.Message);

        // show() displayed figure 1; figure 2 auto-shows at run end (M21 MATLAB behavior).
        Assert.Equal(2, _shown.Count);
        (int number, FigureModel figure) = _shown[0];
        Assert.Equal(1, number);
        Assert.Equal(2, figure.Axes[0].Plots.Count); // Both series landed on figure 1.
        Assert.Equal(2, _shown[1].Number);
    }

    [Fact]
    public async Task Show_WithNoFigureArgument_ShowsTheCurrentFigure()
    {
        ScriptRunResult result = await Run("""
            plot([1, 2], [3, 4])
            show()
            """);

        Assert.True(result.Success, result.Message);
        (int number, FigureModel figure) = Assert.Single(_shown);
        Assert.Equal(1, number); // The implicit figure is figure 1.
        Assert.Single(figure.Axes[0].Plots);
    }

    [Fact]
    public async Task Show_UnknownFigure_IsRuntimeError()
    {
        ScriptRunResult result = await Run("show(7)");

        Assert.False(result.Success);
        Assert.Contains("no figure 7", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Figure_ZeroHandle_IsRuntimeError()
    {
        ScriptRunResult result = await Run("figure(0)");

        Assert.False(result.Success);
        Assert.Contains("start at 1", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task ReRun_ResetsNumbering()
    {
        Assert.True((await Run("figure()\nplot([1],[1])\nshow()")).Success);
        Assert.True((await Run("figure()\nplot([2],[2])\nshow()")).Success);

        // Both runs used figure 1 — a host keyed by number reuses one window.
        Assert.Equal(2, _shown.Count);
        Assert.All(_shown, s => Assert.Equal(1, s.Number));
        Assert.NotSame(_shown[0].Figure, _shown[1].Figure);
    }
}
