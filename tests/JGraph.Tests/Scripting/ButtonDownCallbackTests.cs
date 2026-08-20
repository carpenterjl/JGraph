using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ButtonDownFcn (M71 Wave C): a press is reported with what it landed on, the scripting layer
/// decides whose callback the click is — the hit object, the axes when the object opted out with
/// PickableParts, the figure for bare canvas — and the callback receives MATLAB's Hit event. These
/// tests call <see cref="ScriptGraphicsCallbacks.NotifyButtonDown"/> exactly as the figure window
/// does, so everything holds headlessly.
/// </summary>
[Collection("JG facade")]
public class ButtonDownCallbackTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        JG.Reset();
    }

    private Task<ScriptRunResult> Exec(string code) =>
        _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private static (FigureModel Figure, AxesModel Axes, PlotObject Plot) Scene()
    {
        AxesModel axes = JG.Gca();
        return ((FigureModel)axes.Parent!, axes, axes.Plots[0]);
    }

    [Fact]
    public async Task AClickOnAPlot_RunsItsButtonDownFcn_WithMatlabsHitEvent()
    {
        await Exec("""
            p = plot(1:3);
            set(p, 'ButtonDownFcn', @(src, event) fprintf('%s b%d at %g %g %g src%d\n', ...
                event.EventName, event.Button, event.IntersectionPoint, src == p));
            """);
        (FigureModel figure, AxesModel axes, PlotObject plot) = Scene();

        ScriptGraphicsCallbacks.NotifyButtonDown(figure, plot, axes, (2.0, 2.0), button: 1);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("Hit b1 at 2 2 0 src1"));
    }

    [Fact]
    public async Task TheClickBecomesGco_EvenWithNoCallbackAnywhere()
    {
        await Exec("p = plot(1:3);");
        (FigureModel figure, AxesModel axes, PlotObject plot) = Scene();

        ScriptGraphicsCallbacks.NotifyButtonDown(figure, plot, axes, (2.0, 2.0), button: 1);
        await Exec("disp(gco == p);");

        Assert.Contains(_output.NormalLines, static line => line.Trim() == "true");
    }

    [Fact]
    public async Task PickablePartsNone_PassesTheClickToTheAxes()
    {
        await Exec("""
            p = plot(1:3);
            set(p, 'PickableParts', 'none');
            set(p, 'ButtonDownFcn', @(src, event) disp('plot!'));
            set(gca, 'ButtonDownFcn', @(src, event) disp('axes'));
            """);
        (FigureModel figure, AxesModel axes, PlotObject plot) = Scene();

        ScriptGraphicsCallbacks.NotifyButtonDown(figure, plot, axes, (2.0, 2.0), button: 1);
        await Drain();

        string[] lines = _output.NormalLines.Select(static l => l.Trim()).ToArray();
        Assert.Contains("axes", lines);
        Assert.DoesNotContain("plot!", lines);
    }

    [Fact]
    public async Task BareCanvas_IsTheFiguresClick_WithNaNIntersection_AndEmptyGco()
    {
        await Exec("""
            p = plot(1:3);
            set(gcf, 'ButtonDownFcn', @(src, event) fprintf('fig %d nan %d\n', ...
                src == gcf, all(isnan(event.IntersectionPoint))));
            """);
        (FigureModel figure, _, _) = Scene();

        ScriptGraphicsCallbacks.NotifyButtonDown(figure, hit: null, axes: null, dataPoint: null, button: 1);
        await Drain();
        await Exec("disp(isempty(gco));");

        Assert.Contains(_output.NormalLines, static line => line.Contains("fig 1 nan 1"));
        Assert.Contains(_output.NormalLines, static line => line.Trim() == "true");
    }

    [Fact]
    public async Task TheRightButtonReportsAsThree()
    {
        await Exec("""
            p = plot(1:3);
            set(p, 'ButtonDownFcn', @(src, event) fprintf('button %d\n', event.Button));
            """);
        (FigureModel figure, AxesModel axes, PlotObject plot) = Scene();

        ScriptGraphicsCallbacks.NotifyButtonDown(figure, plot, axes, (2.0, 2.0), button: 3);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("button 3"));
    }
}
