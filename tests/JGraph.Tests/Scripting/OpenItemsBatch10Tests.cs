using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 40, 44, 56 and 88 (ADRs 0222 and 0223), where the parity fixtures cannot reach: a
/// theme pass, a document's round trip, a second run, the model under a reshaped rectangle, and a
/// change the window makes to a watched property.
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch10Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch10Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private async Task Runs(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public async Task AColourOrTitleStyleAScriptChoseSurvivesTheThemeAndTheRestFollowIt()
    {
        await Runs("""
            f = figure('Color', [0.94 0.94 0.94]);
            ax = axes(f, 'Color', [0.2 0.4 0.6]);
            title(ax, 'kept', 'FontWeight', 'normal', 'FontSize', 10);
            g = figure;
            axes(g);
            """);

        FigureModel chosen = JG.TryGetFigure(1, out FigureModel one) ? one : throw new InvalidOperationException("no figure 1");
        FigureModel plain = JG.TryGetFigure(2, out FigureModel two) ? two : throw new InvalidOperationException("no figure 2");
        AxesModel axes = chosen.Axes[0];
        Color figureColour = chosen.Background;
        Color axesColour = axes.Background;
        TextStyle title = axes.TitleStyle;
        Theme.Dark.Apply(chosen);
        Theme.Dark.Apply(plain);

        Assert.Equal(figureColour, chosen.Background);
        Assert.Equal(axesColour, axes.Background);
        Assert.Equal(title, axes.TitleStyle);
        Assert.False(axes.TitleStyle.Bold);

        Assert.False(plain.BackgroundManual);
        Assert.False(plain.Axes[0].TitleStyleManual);
        Assert.NotEqual(figureColour, plain.Background);
    }

    [Fact]
    public void TheManualFlagsAreKeptByADocument()
    {
        var figure = new FigureModel { Background = new Color(1, 2, 3), BackgroundManual = true, TitleStyleManual = true };
        AxesModel axes = figure.AddAxes();
        axes.BackgroundManual = true;

        FigureModel restored = GraphFormat.Deserialize(GraphFormat.Serialize(figure));

        Assert.True(restored.BackgroundManual);
        Assert.True(restored.TitleStyleManual);
        Assert.True(restored.Axes[0].BackgroundManual);
        Assert.False(restored.Axes[0].TitleStyleManual);
    }

    [Fact]
    public async Task ARootDefaultIsForgottenByTheNextRun()
    {
        await Runs("set(0, 'DefaultAxesFontSize', 21); ax = axes(figure); assert(ax.FontSize == 21);");
        JgsHandleRegistry.Clear();
        JG.Reset();
        await Runs("ax = axes(figure); assert(ax.FontSize ~= 21); assert(isempty(fieldnames(get(0, 'Default'))));");
    }

    [Fact]
    public async Task ARectanglesPositionAndCurvatureRedrawItsOutline()
    {
        await Runs("""
            r = rectangle('Position', [1 2 3 4]);
            r.Position = [10 20 2 2];
            r.Curvature = [1 1];
            """);

        PatchPlot patch = Assert.IsType<PatchPlot>(Assert.Single(JG.Gca().Plots));
        DataRange x = patch.GetXDataBounds();
        DataRange y = patch.GetYDataBounds();
        Assert.Equal(10, x.Min, 9);
        Assert.Equal(12, x.Max, 9);
        Assert.Equal(20, y.Min, 9);
        Assert.Equal(22, y.Max, 9);
    }

    [Fact]
    public async Task AWatchedPropertyTheWindowChangesQueuesACheckForTheScriptThread()
    {
        ScriptEventQueue.Flush();
        await Runs("ax = axes(figure); xlim(ax, [0 1]); lh = addlistener(ax, 'XLim', 'PostSet', @(s, e) disp('heard'));");

        // The window's zoom: a change made on another thread queues a check rather than running script code there.
        AxesModel axes = JG.Gca();
        var window = new Thread(() => axes.PrimaryXAxis.Range = new DataRange(0, 5));
        window.Start();
        window.Join();
        try
        {
            Assert.True(ScriptEventQueue.IsPending(GraphicsEventKind.PropertyWatch, axes));
            Assert.DoesNotContain("heard", _output.NormalText, StringComparison.Ordinal);
        }
        finally
        {
            ScriptEventQueue.Flush();
        }
    }
}
