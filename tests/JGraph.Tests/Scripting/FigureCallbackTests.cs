using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The figure callbacks (M71 Wave D). <c>close</c> runs a figure's <c>CloseRequestFcn</c> instead
/// of closing; the callback closes with <c>closereq</c> or <c>delete</c>, vetoes by doing neither,
/// and vetoes by erroring; <c>'force'</c> and <c>delete</c> never ask. <c>SizeChangedFcn</c> rides
/// the queue, coalesced, and reads the settled size off the figure.
/// </summary>
[Collection("JG facade")]
public class FigureCallbackTests : IAsyncLifetime
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

    [Fact]
    public Task CloseAsksTheCallback_WhichVetoesByDoingNothing() => RunAsserting("""
        figure; f = gcf;
        set(f, 'CloseRequestFcn', @(s, e) disp('asked'));
        close(f);
        assert(ishandle(f));
        close(f, 'force');
        assert(~ishandle(f));
        """);

    [Fact]
    public Task ACallbackClosesWithClosereq_SpelledBare() => RunAsserting("""
        figure; g = gcf;
        set(g, 'CloseRequestFcn', @(s, e) closereq);
        close(g);
        assert(~ishandle(g));
        """);

    [Fact]
    public Task DeleteNeverAsks() => RunAsserting("""
        figure; h = gcf;
        set(h, 'CloseRequestFcn', @(s, e) disp('asked!'));
        delete(h);
        assert(~ishandle(h));
        """);

    [Fact]
    public async Task AnErroringCloseRequestFcnVetoesTheClose()
    {
        await RunAsserting("""
            figure; k = gcf;
            set(k, 'CloseRequestFcn', @(s, e) error('boom'));
            close(k);
            assert(ishandle(k));
            close(k, 'force');
            """);
        Assert.Contains(_output.Errors, static e => e.Contains("boom"));
    }

    [Fact]
    public Task CloseAllForceShutsEverything() => RunAsserting("""
        figure; a = gcf;
        set(a, 'CloseRequestFcn', @(s, e) disp('no'));
        figure; b = gcf;
        close all force;
        assert(~ishandle(a) && ~ishandle(b));
        """);

    [Fact]
    public async Task AQueuedCloseRequest_WhoseCallbackWasCleared_ClosesByDefault()
    {
        // The X-button path: the window cancelled its close and queued the request; by the time the
        // script thread gets to it the callback is gone. The close still happens — the cancelled
        // close was standing in for exactly this moment.
        await Exec("figure; f = gcf; set(f, 'CloseRequestFcn', @(s, e) disp('x'));");
        FigureModel figure = (FigureModel)JG.Gca().Parent!;
        ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, figure));
        await Exec("set(f, 'CloseRequestFcn', []);");

        await Drain();

        Assert.Empty(JG.FigureNumbers);
    }

    [Fact]
    public async Task AQueuedCloseRequest_RunsTheCallback_WhichMayVeto()
    {
        await Exec("figure; f = gcf; set(f, 'CloseRequestFcn', @(s, e) disp('not yet'));");
        FigureModel figure = (FigureModel)JG.Gca().Parent!;
        ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, figure));

        await Drain();

        Assert.Single(JG.FigureNumbers);
        Assert.Contains(_output.NormalLines, static line => line.Contains("not yet"));
    }

    [Fact]
    public async Task SizeChangedRidesTheQueue_Coalesced_AndReadsTheSettledSize()
    {
        await Exec("""
            figure; f = gcf;
            set(f, 'SizeChangedFcn', @(s, e) fprintf('now %g by %g\n', s.Position(3), s.Position(4)));
            """);
        FigureModel figure = (FigureModel)JG.Gca().Parent!;

        // A drag: three resizes before the script thread gets a turn — one callback, latest size.
        figure.Size = new JGraph.Core.Primitives.Size2D(700, 500);
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        figure.Size = new JGraph.Core.Primitives.Size2D(800, 600);
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        figure.Size = new JGraph.Core.Primitives.Size2D(900, 650);
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        await Drain();

        Assert.Single(_output.NormalLines, static line => line.Contains("now "));
        Assert.Contains(_output.NormalLines, static line => line.Contains("now 900 by 650"));
    }

    [Fact]
    public void SizeChangedWithNoCallback_QueuesNothing()
    {
        JG.Figure();
        FigureModel figure = JG.CurrentFigure;
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        Assert.Equal(0, ScriptEventQueue.Count);
    }

    private async Task RunAsserting(string code)
    {
        ScriptRunResult result = await Exec(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
