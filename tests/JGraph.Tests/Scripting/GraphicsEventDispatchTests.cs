using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The graphics event loop (M71): interface events queue on <see cref="ScriptEventQueue"/> and are
/// delivered to script callbacks on the script thread — at a drain point when a statement is
/// running, or through an idle pump run when nothing is. These tests drive the queue with synthetic
/// events, which is exactly what the windows do, so everything here holds headlessly.
/// </summary>
[Collection("JG facade")]
public class GraphicsEventDispatchTests : IAsyncLifetime
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
        ScriptEventQueue.InstallPump(null);
        await _session.DisposeAsync();
        JG.Reset();
    }

    private Task<ScriptRunResult> Exec(string code) =>
        _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    /// <summary>Everything printed so far, one trimmed line each — what the assertions read.</summary>
    private List<string> Lines() => _output.NormalLines.Select(static line => line.Trim()).ToList();

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    /// <summary>The legend and first plot of the session's current figure — the pair every
    /// synthetic legend click needs.</summary>
    private static (LegendModel Legend, PlotObject Plot) LegendAndPlot()
    {
        AxesModel axes = JG.Gca();
        return (axes.Legend, axes.Plots[0]);
    }

    [Fact]
    public async Task AQueuedLegendClick_RunsItsCallback_OnAnIdlePumpRun()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('hit');");
        (LegendModel legend, PlotObject plot) = LegendAndPlot();

        Assert.True(ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot));
        Assert.Equal(1, ScriptEventQueue.Count);
        await Drain();

        Assert.Contains("hit", Lines());
        Assert.Equal(0, ScriptEventQueue.Count);
        Assert.NotNull(legend);
    }

    [Fact]
    public async Task ALegendWithNoCallback_DeclinesTheClick_AndQueuesNothing()
    {
        await Exec("p = plot(1:3); legend('a');");
        (_, PlotObject plot) = LegendAndPlot();

        Assert.False(ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot));
        Assert.Equal(0, ScriptEventQueue.Count);
    }

    [Fact]
    public async Task DrawnowDeliversAQueuedEvent_MidStatement_BeforeTheStatementContinues()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('cb');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        await Exec("drawnow; disp('after');");

        int callback = Lines().IndexOf("cb");
        int after = Lines().IndexOf("after");
        Assert.True(callback >= 0, "the callback never ran");
        Assert.True(callback < after, "the callback ran after the statement finished");
    }

    [Fact]
    public async Task DrawnowNocallbacks_LeavesTheQueueAlone()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('cb');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        await Exec("drawnow nocallbacks; disp('after');");

        Assert.DoesNotContain("cb", Lines());
        Assert.Equal(1, ScriptEventQueue.Count);
    }

    [Fact]
    public async Task PauseIsADrainPoint()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('cb');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        await Exec("pause(0.06); disp('after');");

        Assert.True(Lines().IndexOf("cb") < Lines().IndexOf("after"));
    }

    [Fact]
    public async Task ACallbackError_IsReported_AndTheStatementCarriesOn()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) error('boom');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        ScriptRunResult result = await Exec("drawnow; disp('after');");

        Assert.True(result.Success, result.Message);
        Assert.Contains("after", Lines());
        Assert.Contains(_output.Errors, e => e.Contains("boom"));
    }

    [Fact]
    public async Task GcboNamesTheCallbackOwner_AndIsEmptyAgainAfterwards()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp(gcbo == lgd);");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        await Drain();

        Assert.Contains(Lines(), line => line == "true");

        await Exec("disp(isempty(gcbo));");
        Assert.Equal("true", Lines()[^1]);
    }

    [Fact]
    public async Task TheCallbackReceivesTheLegend_AndThePeerThatWasClicked()
    {
        await Exec("""
            p = plot(1:3);
            lgd = legend('a');
            lgd.ItemHitFcn = @(src, event) fprintf('src %d peer %d\n', src == lgd, event.Peer == p);
            """);
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        await Drain();

        Assert.Contains(Lines(), line => line.Contains("src 1 peer 1"));
    }

    [Fact]
    public async Task ANonInterruptibleCallback_HoldsQueuedEventsUntilItFinishes()
    {
        // A runs first and is not interruptible; its drawnow must not deliver B. B's object keeps
        // the default BusyAction 'queue', so B runs after A completes — in the same pump run.
        await Exec("""
            p = plot(1:3);
            lgd = legend('a');
            lgd.ItemHitFcn = @(src, event) eval('disp(''A in''); drawnow; disp(''A out'');');
            """);
        (LegendModel legend, PlotObject plot) = LegendAndPlot();
        Assert.True(JgsHandleRegistry.TryGetEntry(legend, out JgsHandleEntry? entry));
        entry.Interruptible = false;

        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        await Drain();

        string[] order = Lines().Where(l => l is "A in" or "A out").ToArray();
        Assert.Equal(new[] { "A in", "A out", "A in", "A out" }, order);
    }

    [Fact]
    public async Task AnInterruptibleCallback_IsInterruptedAtItsDrawnow()
    {
        await Exec("""
            p = plot(1:3);
            lgd = legend('a');
            lgd.ItemHitFcn = @(src, event) eval('disp(''in''); drawnow; disp(''out'');');
            """);
        (_, PlotObject plot) = LegendAndPlot();

        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        await Drain();

        // The second click is delivered inside the first callback's drawnow: in, in, out, out.
        string[] order = Lines().Where(l => l is "in" or "out").ToArray();
        Assert.Equal(new[] { "in", "in", "out", "out" }, order);
    }

    [Fact]
    public async Task BusyActionCancel_DiscardsAnEventThatCannotRunAtOnce()
    {
        // The eval string must not end at the drawnow: eval called for its value evaluates its last
        // statement as an expression, and a bare drawnow in expression position is the function, not
        // a call. The trailing disp keeps the drawnow a statement — and asserts A ran to its end.
        await Exec("""
            p = plot(1:3);
            lgd = legend('a');
            lgd.ItemHitFcn = @(src, event) eval('disp(''A''); drawnow; disp(''A end'');');
            """);
        (LegendModel legend, PlotObject plot) = LegendAndPlot();
        Assert.True(JgsHandleRegistry.TryGetEntry(legend, out JgsHandleEntry? entry));
        entry.Interruptible = false;
        entry.BusyActionQueues = false;

        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        await Drain();

        // The second event reached a drain point inside non-interruptible A and was discarded there.
        Assert.Single(Lines(), "A");
        Assert.Single(Lines(), "A end");
        Assert.Equal(0, ScriptEventQueue.Count);
    }

    [Fact]
    public async Task WaitforReturns_WhenAQueuedCallbackChangesTheWatchedProperty()
    {
        // waitfor really waits only where a pump is installed; a no-op pump stands in for the shell.
        ScriptEventQueue.InstallPump(() => { });
        await Exec("""
            p = plot(1:3);
            lgd = legend('a');
            lgd.ItemHitFcn = @(src, event) set(lgd, 'Visible', 'off');
            """);
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        ScriptRunResult result = await Exec("waitfor(lgd, 'Visible', 'off'); disp('done');");

        Assert.True(result.Success, result.Message);
        Assert.Contains("done", Lines());
    }

    [Fact]
    public async Task WaitforWithNoPump_KeepsTheHeadlessContract_AndReturnsAtOnce()
    {
        await Exec("p = plot(1:3); lgd = legend('a');");

        ScriptRunResult result = await Exec("waitfor(lgd, 'Visible', 'off'); disp('done');");

        Assert.True(result.Success, result.Message);
        Assert.Contains("done", Lines());
    }

    [Fact]
    public async Task AnEventForADeletedObject_IsQuietlyDropped()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('hit');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        await Exec("close all;");
        await Drain();

        Assert.DoesNotContain("hit", Lines());
    }

    [Fact]
    public async Task ThePumpYieldsBetweenEvents_WhenAskedTo()
    {
        await Exec("p = plot(1:3); lgd = legend('a'); lgd.ItemHitFcn = @(src, event) disp('hit');");
        (_, PlotObject plot) = LegendAndPlot();
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);
        ScriptGraphicsCallbacks.NotifyLegendItemHit(JG.Gca(), plot);

        await ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(
            () => Lines().Contains("hit"), CancellationToken.None);

        Assert.Single(Lines(), "hit");
        Assert.Equal(1, ScriptEventQueue.Count);
    }
}

