using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 91, 93, 94 and 95 (ADR 0224), where the parity fixture cannot reach: the model under a
/// quiver's scale, the hook that gives a figure the root's defaults, where a detached menu or tool
/// goes, and a deleted handle's class across runs.
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch11Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch11Tests() => JG.Reset();

    public void Dispose()
    {
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private async Task Runs(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public void AFlatQuiverScalesAsMatlabsDoesByItsGrid()
    {
        // quiver([0 1], [0 0], [1 1], [1 2]): √2 columns and rows, the longest arrow 3.162 cells.
        var plot = new QuiverPlot([0, 1], [0, 0], [0, 0], [1, 1], [1, 2], [0, 0]);
        Assert.Equal(0.9 / System.Math.Sqrt(10), plot.EffectiveScale, 12);

        // A 2-by-2 grid divides each span by its own rows or columns: a cell of 0.5 by 0.5.
        var grid = new QuiverPlot([0, 0, 1, 1], [0, 1, 0, 1], [0, 0, 0, 0], [1, 1, 1, 1], [0, 0, 0, 0], [0, 0, 0, 0]) { GridShape = (2, 2) };
        Assert.Equal(0.9 / System.Math.Sqrt(1 / 0.5), grid.EffectiveScale, 12);

        plot.AutoScale = false;
        Assert.Equal(plot.Scale, plot.EffectiveScale);
    }

    [Fact]
    public void AFigureMadeUnderANumberIsAnnouncedOnce()
    {
        var made = new List<FigureModel>();
        Action<FigureModel>? before = JG.FigureMade;
        JG.FigureMade = made.Add;
        try
        {
            FigureModel first = JG.Figure(3);
            JG.Figure(3);
            _ = JG.CurrentFigure;
            Assert.Equal([first], made);
        }
        finally
        {
            JG.FigureMade = before;
        }
    }

    [Fact]
    public async Task ADetachedMenuOrToolLeavesItsFigureAndOpensNoWindow()
    {
        await Runs("""
            f = figure;
            m = uimenu(f, 'Text', 'File');
            tb = uitoolbar(f);
            pt = uipushtool(tb);
            m.Parent = [];
            pt.Parent = [];
            tb.Parent = [];
            t2 = uipushtool('Parent', []);
            """);

        FigureModel figure = JG.TryGetFigure(1, out FigureModel one) ? one : throw new InvalidOperationException("no figure 1");
        Assert.Empty(figure.Menus);
        Assert.Empty(figure.Toolbars);
        Assert.Equal([1], JG.FigureNumbers);
    }

    [Fact]
    public async Task ADeletedHandlesClassIsForgottenByTheNextRun()
    {
        await Runs("ln = plot(1:3); delete(ln); assert(numel(methods(ln)) == 22); fprintf('%.17g', ln);");
        JgsValue dead = JgsValue.Number(double.Parse(_output.NormalText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("matlab.graphics.chart.primitive.Line", JgsHandleRegistry.RetiredClassOf(dead));

        JgsHandleRegistry.Clear();
        JG.Reset();
        Assert.Null(JgsHandleRegistry.RetiredClassOf(dead));
    }
}
