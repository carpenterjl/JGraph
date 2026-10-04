using System.Diagnostics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U1 of the app-building plan (ADR 0198): a classic <c>uicontrol</c> driven headless through the
/// seam the window calls. A user's typing reaches the model only through the queue, written before
/// its callback is considered; callbacks take MATLAB's three forms; the keyboard goes to the
/// component that has it; and the window is fed frames, a handful per loop rather than one per write.
/// </summary>
[Collection("JG facade")]
public class UiControlU1Tests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { })));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        ScriptEventQueue.Flush();
        ScriptComponentFrames.SetSink(null);
        await _session.DisposeAsync();
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private static FigureModel Figure(int number = 1) =>
        JG.TryGetFigure(number, out FigureModel figure) ? figure : throw new InvalidOperationException("no figure");

    private static UiControlModel Control(string style, int index = 0) =>
        Figure().Components.OfType<UiControlModel>()
            .Where(c => c.Style.ToString().Equals(style, StringComparison.OrdinalIgnoreCase))
            .ElementAt(index);

    /// <summary>The user's own script, D:\Temp_Broken_Scripts\test1\test_data.m, as it stands.</summary>
    private const string TestData = """
        function parallel_calculator()
            fig = figure('Name', 'Parallel Resistor Calculator', ...
                         'NumberTitle', 'off', ...
                         'MenuBar', 'none', ...
                         'ToolBar', 'none', ...
                         'Position', [400, 400, 350, 250], ...
                         'Resize', 'off');
            uicontrol('Style', 'text', 'String', 'Resistor 1 (Ω):', ...
                      'Position', [30, 180, 100, 20], ...
                      'HorizontalAlignment', 'left', 'FontSize', 10);
            hR1 = uicontrol('Style', 'edit', ...
                            'Position', [140, 180, 150, 25], ...
                            'FontSize', 10, 'HorizontalAlignment', 'left');
            uicontrol('Style', 'text', 'String', 'Resistor 2 (Ω):', ...
                      'Position', [30, 130, 100, 20], ...
                      'HorizontalAlignment', 'left', 'FontSize', 10);
            hR2 = uicontrol('Style', 'edit', ...
                            'Position', [140, 130, 150, 25], ...
                            'FontSize', 10, 'HorizontalAlignment', 'left');
            uicontrol('Style', 'pushbutton', 'String', 'Calculate Total Resistance', ...
                      'Position', [50, 80, 240, 35], ...
                      'FontSize', 10, 'FontWeight', 'bold', ...
                      'Callback', @calculateCallback);
            hResult = uicontrol('Style', 'text', 'String', 'Total Resistance: --', ...
                                'Position', [30, 30, 290, 30], ...
                                'FontSize', 12, 'FontWeight', 'bold', ...
                                'ForegroundColor', [0, 0.45, 0.74], ...
                                'HorizontalAlignment', 'center');
            function calculateCallback(~, ~)
                str1 = get(hR1, 'String');
                str2 = get(hR2, 'String');
                R1 = str2double(str1);
                R2 = str2double(str2);
                if isnan(R1) || isnan(R2) || R1 <= 0 || R2 <= 0
                    set(hResult, 'String', 'Error: Enter positive numeric values!');
                    set(hResult, 'ForegroundColor', [0.85, 0.33, 0.1]);
                    return;
                end
                R_eq = (R1 * R2) / (R1 + R2);
                if R_eq >= 1000
                    set(hResult, 'String', sprintf('Total Resistance: %.3f kΩ', R_eq/1000));
                else
                    set(hResult, 'String', sprintf('Total Resistance: %.2f Ω', R_eq));
                end
                set(hResult, 'ForegroundColor', [0.12, 0.53, 0.22]);
            end
        end
        """;

    [Fact]
    public async Task TestDataRunsEndToEnd_AndItsNestedCallbackOutlivesTheFunction()
    {
        ScriptRunResult run = await _session.ExecuteFileAsync(TestData, sourceId: "", CancellationToken.None);
        Assert.True(run.Success, run.Message + _output.ErrorText);
        Assert.Equal(6, Figure().Components.Count);
        Assert.Equal("Parallel Resistor Calculator", Figure().Name);

        // The function has returned; its workspace lives on only in the button's callback.
        await Exec("x = 1; clear x;");
        ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit", 0), "100");
        ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit", 1), "300");
        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();

        UiControlModel result = Control("Text", 2);
        Assert.Equal("Total Resistance: 75.00 Ω", result.Text.Joined);
        Assert.Equal(new UiColor(0.12, 0.53, 0.22), result.ForegroundColor);
        Assert.Empty(_output.ErrorText);

        ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit", 1), "abc");
        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();
        Assert.Equal("Error: Enter positive numeric values!", result.Text.Joined);
    }

    [Fact]
    public async Task AButtonsCallbackGetsActionData()
    {
        await Exec("""
            f = figure('Visible', 'off');
            b = uicontrol(f, 'Style', 'pushbutton', 'Callback', ...
                @(s, e) fprintf('%s %s %d %d %d\n', class(e), e.EventName, isa(e, 'event.EventData'), isa(e, 'handle'), e.Source == s));
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();

        Assert.Contains(_output.NormalLines, static l => l == "matlab.ui.eventdata.ActionData Action 1 1 1");
    }

    [Fact]
    public async Task ATextCallbackRunsInTheBaseWorkspace_AndACellOneGetsItsArguments()
    {
        await Exec("""
            f = figure('Visible', 'off');
            b = uicontrol(f, 'Style', 'pushbutton', 'Callback', 'clicks = clicks + 1;');
            clicks = 0;
            """);
        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();
        await Exec("fprintf('clicks=%d\\n', clicks);");
        Assert.Contains(_output.NormalLines, static l => l == "clicks=2");

        await Exec("set(b, 'Callback', {@(s, e, a, t) fprintf('%d %s %s\\n', a, t, class(e)), 7, 'seven'});");
        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();
        Assert.Contains(_output.NormalLines, static l => l == "7 seven matlab.ui.eventdata.ActionData");
    }

    [Fact]
    public async Task TheUsersTextIsWrittenBeforeTheCallback_EvenWhenTheCallbackFails()
    {
        await Exec("""
            f = figure('Visible', 'off');
            e1 = uicontrol(f, 'Style', 'edit', 'Callback', @(s, e) error('boom'));
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit"), "typed");
        await Drain();

        Assert.Equal("typed", Control("Edit").Text.Joined);
        Assert.Contains("boom", _output.ErrorText);
    }

    [Fact]
    public async Task StopDropsTheCallbackButKeepsWhatTheUserTyped()
    {
        await Exec("""
            f = figure('Visible', 'off');
            e1 = uicontrol(f, 'Style', 'edit', 'Callback', @(s, e) disp('callback ran'));
            """);

        long seq = ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit"), "kept");
        ScriptEventQueue.Flush(); // what Stop does
        await Drain();

        Assert.Equal("kept", Control("Edit").Text.Joined);
        Assert.Equal(seq, Control("Edit").UserWriteSeq);
        Assert.DoesNotContain(_output.NormalLines, static l => l.Contains("callback ran"));
    }

    [Fact]
    public async Task AScriptThatNeverYieldsKeepsSeeingTheOldText()
    {
        await Exec("""
            f = figure('Visible', 'off');
            e1 = uicontrol(f, 'Style', 'edit', 'String', 'old');
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Control("Edit"), "new");
        await Exec("fprintf('[%s]\\n', get(e1, 'String'));");
        Assert.Contains(_output.NormalLines, static l => l == "[old]");

        await Drain();
        await Exec("fprintf('[%s]\\n', get(e1, 'String'));");
        Assert.Contains(_output.NormalLines, static l => l == "[new]");
    }

    [Fact]
    public async Task AFocusedComponentTakesTheKeyFromTheFigure_ButTheWindowStillHearsIt()
    {
        await Exec("""
            f = figure('Visible', 'off');
            e1 = uicontrol(f, 'Style', 'edit', 'KeyPressFcn', @(s, e) fprintf('component %s %s\n', e.Key, class(s)));
            set(f, 'KeyPressFcn', @(s, e) disp('figure heard it'));
            set(f, 'WindowKeyPressFcn', @(s, e) disp('window heard it'));
            """);

        ScriptGraphicsCallbacks.NotifyKey(Figure(), pressed: true, "a", "a", [], focused: Control("Edit"));
        await Drain();

        Assert.Contains(_output.NormalLines, static l => l == "component a double");
        Assert.Contains(_output.NormalLines, static l => l == "window heard it");
        Assert.DoesNotContain(_output.NormalLines, static l => l == "figure heard it");
    }

    [Fact]
    public async Task APressReachesTheWindowFirst_AndAReleaseReachesItLast()
    {
        // R2025b, u1w_keys: WindowKeyPressFcn, then the KeyPressFcn of whoever holds the keyboard;
        // on release the holder's KeyReleaseFcn, then WindowKeyReleaseFcn. Each with its own class.
        await Exec("""
            f = figure('Visible', 'off');
            e1 = uicontrol(f, 'Style', 'edit', ...
                'KeyPressFcn', @(s, e) fprintf('component %s %s\n', class(e), e.EventName), ...
                'KeyReleaseFcn', @(s, e) fprintf('component %s %s\n', class(e), e.EventName));
            set(f, 'KeyPressFcn', @(s, e) fprintf('figure %s %s\n', class(e), e.EventName), ...
                'KeyReleaseFcn', @(s, e) fprintf('figure %s %s\n', class(e), e.EventName), ...
                'WindowKeyPressFcn', @(s, e) fprintf('window %s %s [%s]\n', class(e), e.EventName, e.Character), ...
                'WindowKeyReleaseFcn', @(s, e) fprintf('window %s %s\n', class(e), e.EventName));
            """);

        ScriptGraphicsCallbacks.NotifyKey(Figure(), pressed: true, "a", "a", [], focused: Control("Edit"));
        ScriptGraphicsCallbacks.NotifyKey(Figure(), pressed: false, "a", "a", [], focused: Control("Edit"));
        ScriptGraphicsCallbacks.NotifyKey(Figure(), pressed: true, "X", "x", ["shift"]);
        ScriptGraphicsCallbacks.NotifyKey(Figure(), pressed: false, "X", "x", ["shift"]);
        await Drain();

        Assert.Equal(
            [
                "window matlab.ui.eventdata.KeyData WindowKeyPress [a]",
                "component matlab.ui.eventdata.UIClientComponentKeyEvent KeyPress",
                "component matlab.ui.eventdata.UIClientComponentKeyEvent KeyRelease",
                "window matlab.ui.eventdata.KeyData WindowKeyRelease",
                "window matlab.ui.eventdata.KeyData WindowKeyPress [X]",
                "figure matlab.ui.eventdata.KeyData KeyPress",
                "figure matlab.ui.eventdata.KeyData KeyRelease",
                "window matlab.ui.eventdata.KeyData WindowKeyRelease",
            ],
            _output.NormalLines);
    }

    [Fact]
    public async Task TheWindowsButtonEventsAndTheFiguresOwnClickCarryR2025bsEventData()
    {
        await Exec("""
            f = figure('Visible', 'off');
            set(f, 'WindowButtonDownFcn', @(s, e) fprintf('%s %s\n', class(e), e.EventName), ...
                'WindowButtonUpFcn', @(s, e) fprintf('%s %s\n', class(e), e.EventName), ...
                'ButtonDownFcn', @(s, e) fprintf('%s %s\n', class(e), e.EventName), ...
                'WindowScrollWheelFcn', @(s, e) fprintf('%s %s %d\n', class(e), e.EventName, e.VerticalScrollCount));
            """);

        ScriptGraphicsCallbacks.NotifyWindowButton(Figure(), pressed: true, SelectionKind.Normal, (10, 10));
        ScriptGraphicsCallbacks.NotifyButtonDown(Figure(), hit: null, axes: null, dataPoint: null, button: 1);
        ScriptGraphicsCallbacks.NotifyWindowButton(Figure(), pressed: false, SelectionKind.Normal, (10, 10));
        ScriptGraphicsCallbacks.NotifyScrollWheel(Figure(), 1);
        await Drain();

        Assert.Equal(
            [
                "matlab.ui.eventdata.WindowMouseData WindowMousePress",
                "matlab.ui.eventdata.MouseData ButtonDown",
                "matlab.ui.eventdata.WindowMouseData WindowMouseRelease",
                "matlab.ui.eventdata.ScrollWheelData WindowScrollWheel 1",
            ],
            _output.NormalLines);
    }

    [Fact]
    public async Task ATextCloseRequestFcnRunsWhenTheWindowAsks_AndVetoesTheClose()
    {
        // The bug U1 fixes: a text CloseRequestFcn other than closereq failed to resolve, and the
        // "callback vanished" path closed the figure the callback was there to keep.
        await Exec("""
            f = figure('Visible', 'off');
            f.CloseRequestFcn = 'disp(''kept open'')';
            """);

        ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, Figure()));
        await Drain();

        Assert.Contains(_output.NormalLines, static l => l == "kept open");
        Assert.True(JG.TryGetFigure(1, out _));
    }

    [Fact]
    public async Task CallbackSlotsPinWhatTheyHold()
    {
        // A nested function's workspace is reachable only through the slot once its function returns
        // and the caller's variables are cleared; the slot must keep it alive.
        await Exec("""
            function make()
                secret = 41;
                f = figure('Visible', 'off');
                uicontrol(f, 'Style', 'pushbutton', 'Callback', @tell);
                function tell(~, ~)
                    fprintf('secret %d\n', secret + 1);
                end
            end
            """);
        await Exec("make(); clear all;");

        ScriptGraphicsCallbacks.NotifyUserValue(Control("PushButton"), null);
        await Drain();

        Assert.Contains(_output.NormalLines, static l => l == "secret 42");
    }

    [Fact]
    public async Task ALoopOfTenThousandWritesSendsAHandfulOfFrames()
    {
        var frames = new List<UiFrame>();
        ScriptComponentFrames.SetSink(frame => frames.Add(frame)); // never applied: one stays in flight
        await Exec("f = figure('Visible', 'off'); h = uicontrol(f, 'Style', 'text');");
        int before = frames.Count;

        var clock = Stopwatch.StartNew();
        await Exec("for k = 1:10000, set(h, 'String', sprintf('%d', k)); end");
        clock.Stop();

        int during = frames.Count - before;
        Assert.InRange(during, 1, 3); // the first boundary sends one; the rest wait; the run's end forces one
        Assert.Equal("10000", frames[^1].Controls.Single().Text.Joined);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"10,000 writes took {clock.Elapsed}");
    }
}
