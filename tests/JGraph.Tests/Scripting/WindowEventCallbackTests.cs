using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M75: the six things a figure window hears that a script may want told about — a key down, a key
/// up, a button down, a button up, the pointer moving, and the wheel turning. These call the same
/// seams the figure window calls, so everything holds headlessly.
/// </summary>
[Collection("JG facade")]
public class WindowEventCallbackTests : IAsyncLifetime
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

    private static FigureModel Scene() => (FigureModel)JG.Gca().Parent!;

    [Fact]
    public async Task AKeyPressCarriesTheCharacterTheKeyAndWhatWasHeldDown()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'KeyPressFcn', @(src, event) fprintf('%s [%s] %s %d\n', ...
                event.EventName, event.Character, event.Key, numel(event.Modifier)));
            """);

        ScriptGraphicsCallbacks.NotifyKey(Scene(), pressed: true, "A", "a", ["shift"]);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("KeyPress [A] a 1"));
    }

    [Fact]
    public async Task OnePressReachesBothTheFiguresCallbackAndTheWindows()
    {
        // With no uicontrols a figure has the focus whenever its window does, so MATLAB's two
        // callbacks are the same event told twice — and both must run.
        await Exec("""
            plot(1:3);
            set(gcf, 'KeyPressFcn', @(s, e) disp('figure heard it'));
            set(gcf, 'WindowKeyPressFcn', @(s, e) disp('window heard it'));
            """);

        ScriptGraphicsCallbacks.NotifyKey(Scene(), pressed: true, "q", "q", []);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("figure heard it"));
        Assert.Contains(_output.NormalLines, static line => line.Contains("window heard it"));
    }

    [Fact]
    public async Task AKeyReleaseRunsItsOwnCallbackAndNotThePressOne()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'KeyPressFcn', @(s, e) disp('pressed'));
            set(gcf, 'KeyReleaseFcn', @(s, e) fprintf('released %s\n', e.EventName));
            """);

        ScriptGraphicsCallbacks.NotifyKey(Scene(), pressed: false, "q", "q", []);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("released KeyRelease"));
        Assert.DoesNotContain(_output.NormalLines, static line => line.Contains("pressed"));
    }

    [Fact]
    public async Task ThePressedCharacterIsWhatCurrentCharacterAnswers()
    {
        await Exec("plot(1:3);");
        FigureModel figure = Scene();

        ScriptGraphicsCallbacks.NotifyKey(figure, pressed: true, "z", "z", []);
        ScriptRunResult result = await Exec("c = get(gcf, 'CurrentCharacter');");

        Assert.True(result.Success, result.Message);
        Assert.Equal("z", figure.CurrentCharacter);
    }

    [Fact]
    public async Task AWindowButtonDownRecordsWhereAndWhichGestureItWas()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'WindowButtonDownFcn', @(s, e) fprintf('down %s\n', get(gcf, 'SelectionType')));
            """);
        FigureModel figure = Scene();

        ScriptGraphicsCallbacks.NotifyWindowButton(figure, pressed: true, SelectionKind.Alt, (30, 40));
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("down alt"));
        Assert.Equal(SelectionKind.Alt, figure.SelectionType);
    }

    [Fact]
    public async Task AWindowButtonUpRunsTheReleaseCallbackAlone()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'WindowButtonDownFcn', @(s, e) disp('down'));
            set(gcf, 'WindowButtonUpFcn', @(s, e) disp('up'));
            """);

        ScriptGraphicsCallbacks.NotifyWindowButton(
            Scene(), pressed: false, SelectionKind.Normal, (10, 10));
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("up"));
        Assert.DoesNotContain(_output.NormalLines, static line => line.Equals("down"));
    }

    [Fact]
    public async Task ThePointerPositionIsRecordedWhetherOrNotAnybodyIsListening()
    {
        await Exec("figure('Position', [0 0 640 480]); plot(1:3);");
        FigureModel figure = Scene();

        ScriptGraphicsCallbacks.NotifyWindowMotion(figure, (100, 80));
        ScriptRunResult result = await Exec("here = get(gcf, 'CurrentPoint');");

        Assert.True(result.Success, result.Message);
        double[] here = Assert.IsType<double[]>(
            Assert.Single(result.Variables, v => v.Name == "here").RawValue);

        // MATLAB counts up from the bottom of the figure; the window counts down from the top.
        Assert.Equal(100, here[0]);
        Assert.Equal(400, here[1]);
    }

    [Fact]
    public async Task AMotionStormQueuesOneCallbackRatherThanAHundred()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'WindowButtonMotionFcn', @(s, e) disp('moved'));
            """);
        FigureModel figure = Scene();

        for (int i = 0; i < 50; i++)
        {
            ScriptGraphicsCallbacks.NotifyWindowMotion(figure, (i, i));
        }

        // A drag across the window is one question about where the pointer is now.
        Assert.Equal(1, ScriptEventQueue.Count);
        await Drain();
        Assert.Single(_output.NormalLines, static line => line.Contains("moved"));
    }

    [Fact]
    public async Task TheWheelCarriesHowFarItTurnedInMatlabsCounting()
    {
        await Exec("""
            plot(1:3);
            set(gcf, 'WindowScrollWheelFcn', @(s, e) fprintf('%s %d of %d\n', ...
                e.EventName, e.VerticalScrollCount, e.VerticalScrollAmount));
            """);

        ScriptGraphicsCallbacks.NotifyScrollWheel(Scene(), notches: -2);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("WindowScrollWheel -2 of 3"));
    }

    [Fact]
    public async Task AFigureWithNoCallbackQueuesNothingAtAll()
    {
        await Exec("plot(1:3);");
        FigureModel figure = Scene();

        ScriptGraphicsCallbacks.NotifyKey(figure, pressed: true, "q", "q", []);
        ScriptGraphicsCallbacks.NotifyWindowButton(figure, pressed: true, SelectionKind.Normal, (1, 1));
        ScriptGraphicsCallbacks.NotifyWindowMotion(figure, (1, 1));
        ScriptGraphicsCallbacks.NotifyScrollWheel(figure, notches: 1);

        // An unscripted window costs nothing, which is what makes it safe to report every move.
        Assert.Equal(0, ScriptEventQueue.Count);

        // The state behind them is still kept, because a script may ask for it at any time.
        Assert.Equal("q", figure.CurrentCharacter);
    }
}
