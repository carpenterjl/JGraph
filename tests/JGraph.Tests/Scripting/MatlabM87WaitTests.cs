using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M87: the three verbs that stop and wait for a person — bare <c>pause</c>,
/// <c>waitforbuttonpress</c> and <c>ginput</c>.
/// <para>
/// ADR 0071 recorded that bare <c>pause</c> was unsupported because <em>this build has no key routing
/// to the interpreter</em>. M75 built that routing and nobody came back to the sentence; the other two
/// verbs did not exist at all. What is tested here is both halves — that they wait when there is a
/// window, driven headlessly by the same seams the window calls, and that they refuse by name when
/// there is not, which is what keeps a batch run and the stress gate free of a verb that would wait
/// forever for somebody who is not there.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM87WaitTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM87WaitTests()
    {
        JG.Reset();
        ScriptInputWatch.Reset();
        ScriptEventQueue.Flush();
    }

    public void Dispose()
    {
        ScriptEventQueue.InstallPump(null);
        ScriptEventQueue.Flush();
        ScriptInputWatch.Reset();
        JG.Reset();
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    private static object? Raw(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private static double Number(ScriptRunResult result, string name) =>
        Raw(result, name) switch
        {
            double[] { Length: 1 } packed => packed[0],
            double one => one,
            { } other => throw new InvalidOperationException($"{name} is a {other.GetType()}."),
            null => throw new InvalidOperationException($"{name} carries no value."),
        };

    /// <summary>
    /// Says a window is present, the way the application does. The pump itself does nothing: what the
    /// waiting verbs read is whether one was installed at all, which is the same question
    /// <c>waitfor</c> has always asked.
    /// </summary>
    private static void PretendThereIsAWindow() => ScriptEventQueue.InstallPump(static () => { });

    /// <summary>Reports a press shortly after now, from another thread, as a window would.</summary>
    private static void PressShortly(Action press) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            press();
        });

    // --- With no window ------------------------------------------------------------------------------

    /// <summary>
    /// Each refuses by name and says which verb does the job without waiting. A batch run that
    /// blocked here would hang the stress gate with nothing able to say why.
    /// </summary>
    [Theory]
    [InlineData("pause;", "pause(seconds)")]
    [InlineData("w = waitforbuttonpress;", "no window")]
    [InlineData("[x, y] = ginput(1);", "nowhere to click")]
    public async Task EachRefusesByNameWhereThereIsNoWindow(string code, string fragment)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.False(result.Success, $"expected a refusal from: {code}");
        Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The switch is not a wait, so it works anywhere. Each of the three words answers the state as it
    /// was <em>before</em> the call, which is what makes <c>old = pause('off'); … pause(old)</c> put
    /// back whatever was there rather than guessing.
    /// </summary>
    [Fact]
    public async Task ThePauseSwitchWorksWithoutAWindowAndAnswersThePriorState()
    {
        ScriptRunResult result = await RunMatlab("""
            a = pause('query');
            b = pause('off');
            c = pause('query');
            d = pause('on');
            e = pause('query');
            """);
        Succeeded(result);
        Assert.Equal("on", Raw(result, "a"));
        Assert.Equal("on", Raw(result, "b"));
        Assert.Equal("off", Raw(result, "c"));
        Assert.Equal("off", Raw(result, "d"));
        Assert.Equal("on", Raw(result, "e"));
    }

    /// <summary>
    /// With pauses off, every pause returns at once — including the bare one, which therefore does not
    /// refuse either. That is the point of the switch: a script written with pauses in it is run
    /// without them, and a script that turned them off should not then be stopped by one.
    /// </summary>
    [Fact]
    public async Task PausesTurnedOffMakeEveryPauseReturnAtOnce()
    {
        ScriptRunResult result = await RunMatlab("""
            pause('off');
            t = tic;
            pause(3);
            pause;
            took = toc(t);
            pause('on');
            """);
        Succeeded(result);
        Assert.True(Number(result, "took") < 1, "a pause with pauses off should not wait.");
    }

    [Fact]
    public async Task AnUnknownWordIsRefusedByName()
    {
        ScriptRunResult result = await RunMatlab("pause('sideways');");
        Assert.False(result.Success);
        Assert.Contains("'sideways'", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking for no points needs no window, because it never waits. A refusal here would be a verb
    /// complaining about a thing it was not about to do.
    /// </summary>
    [Fact]
    public async Task GinputOfNoPointsAnswersEmptyWithoutAWindow()
    {
        ScriptRunResult result = await RunMatlab("[x, y] = ginput(0); n = numel(x);");
        Succeeded(result);
        Assert.Equal(0, Number(result, "n"));
    }

    // --- With a window -------------------------------------------------------------------------------

    /// <summary>A key releases a bare pause.</summary>
    [Fact]
    public async Task AKeyReleasesABarePause()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        PressShortly(() => ScriptGraphicsCallbacks.NotifyKey(figure, true, "q", "q", []));
        ScriptRunResult result = await RunMatlab("t = tic; pause; took = toc(t);");

        Succeeded(result);
        Assert.True(Number(result, "took") < 30, "the pause should have been released by the key.");
    }

    /// <summary>
    /// <c>waitforbuttonpress</c> answers MATLAB's 1 for a key and 0 for a mouse button — the one
    /// question it exists to answer, and the one it would be easy to get backwards.
    /// </summary>
    [Fact]
    public async Task WaitforbuttonpressAnswersOneForAKey()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        PressShortly(() => ScriptGraphicsCallbacks.NotifyKey(figure, true, "a", "a", []));
        ScriptRunResult result = await RunMatlab("w = waitforbuttonpress;");

        Succeeded(result);
        Assert.Equal(1, Number(result, "w"));
    }

    [Fact]
    public async Task WaitforbuttonpressAnswersZeroForAButton()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        PressShortly(() => ScriptGraphicsCallbacks.NotifyWindowButton(
            figure, true, SelectionKind.Normal, (10, 10)));
        ScriptRunResult result = await RunMatlab("w = waitforbuttonpress;");

        Succeeded(result);
        Assert.Equal(0, Number(result, "w"));
    }

    /// <summary>
    /// A press is heard even when nothing has a callback for it. This is the reason the waiting verbs
    /// read their own record rather than the callback queue: that queue only ever holds an event some
    /// object has a callback for, which is what makes an unscripted window cost nothing — and a verb
    /// waiting for a key must hear the key regardless.
    /// </summary>
    [Fact]
    public async Task APressIsHeardWithNoCallbackAnywhere()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        Assert.Equal(0, ScriptEventQueue.Count);
        ScriptGraphicsCallbacks.NotifyKey(figure, true, "z", "z", []);

        // Nothing was queued, because nothing is listening — and it was still recorded.
        Assert.Equal(0, ScriptEventQueue.Count);
        Assert.Equal(1, ScriptInputWatch.Count);
        Assert.Equal(ScriptInputKind.Key, ScriptInputWatch.Latest.Input.Kind);
    }

    /// <summary>
    /// <c>ginput</c> collects as many clicks as it was asked for, and reports which button each was.
    /// </summary>
    [Fact]
    public async Task GinputCollectsTheClicksItWasAskedFor()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            ScriptGraphicsCallbacks.NotifyWindowButton(figure, true, SelectionKind.Normal, (20, 20));
            await Task.Delay(120);
            ScriptGraphicsCallbacks.NotifyWindowButton(figure, true, SelectionKind.Alt, (40, 40));
        });

        ScriptRunResult result = await RunMatlab(
            "[x, y, b] = ginput(2); n = numel(x); m = numel(b); first = b(1); second = b(2);");

        Succeeded(result);
        Assert.Equal(2, Number(result, "n"));
        Assert.Equal(2, Number(result, "m"));

        // Left then right, in MATLAB's numbering.
        Assert.Equal(1, Number(result, "first"));
        Assert.Equal(3, Number(result, "second"));
    }

    /// <summary>A key ends the collection early, which is MATLAB's Enter.</summary>
    [Fact]
    public async Task AKeyEndsGinputEarly()
    {
        PretendThereIsAWindow();
        await RunMatlab("plot(1:3);");
        var figure = (FigureModel)JG.Gca().Parent!;

        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            ScriptGraphicsCallbacks.NotifyWindowButton(figure, true, SelectionKind.Normal, (20, 20));
            await Task.Delay(120);
            ScriptGraphicsCallbacks.NotifyKey(figure, true, "\r", "return", []);
        });

        ScriptRunResult result = await RunMatlab("[x, y] = ginput(5); n = numel(x);");

        Succeeded(result);
        Assert.Equal(1, Number(result, "n"));
    }
}
