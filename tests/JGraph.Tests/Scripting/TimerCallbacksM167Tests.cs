using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), eleventh sub-stage: timers (appendix A #105). A <c>timer</c> is a handle whose
/// callbacks run on the script thread at a drain point — <c>start</c>, <c>stop</c>, <c>wait</c>,
/// <c>pause</c>, <c>drawnow</c>, or the boundary between two statements — and never inside one, so
/// a callback's captured values are snapshots and a held operand is read before a callback can
/// write it (M5).
/// </summary>
/// <remarks>
/// The parity fixture <c>timer_callbacks</c> holds R2025b's answers for the forms; these pin the
/// roads the sub-stage touched, one assertion a road, and the one place this build keeps to the
/// documented contract where R2025b does not: an <c>ErrorFcn</c> receives the timer and an event
/// carrying the error.
/// </remarks>
[Collection("JG facade")]
public class TimerCallbacksM167Tests : IDisposable
{
    /// <summary>The parity fixtures' helpers folder, for <c>vlog</c>.</summary>
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();

    public TimerCallbacksM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    /// <summary>The fixtures' log: a global the helper <c>vlog</c> appends to.</summary>
    private const string Log = "global vlog_text; vlog_text = ''; ";

    // --- when a callback runs ---------------------------------------------------------------------

    [Fact]
    public void StartRunsTheStartFcnAndWaitReturnsAfterTheStopFcn()
    {
        Assert.Equal("start;after_start;tick;stop;after_wait;", RunAndRead(Log + """
            t = timer('StartFcn', @(~, ~) vlog('start'), 'TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.05);
            start(t); vlog('after_start'); wait(t); vlog('after_wait'); delete(t);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void ATimerDueAtOnceFiresInsideStart()
    {
        Assert.Equal("start;tick;stop;after_start;", RunAndRead(Log + """
            t = timer('StartFcn', @(~, ~) vlog('start'), 'TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'));
            start(t); vlog('after_start'); wait(t); delete(t);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void ADueTimerFiresBetweenTwoStatementsNotInsideOne()
    {
        // The TimerFcn is due long before the loop ends and runs at the first statement boundary
        // after it is due — and the loop's own sum is never touched mid-statement.
        Assert.Equal("tick;loop_done; 50000005000000", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartDelay', 0.001);
            start(t);
            x = 0;
            for i = 1:1e7
                x = x + i;
            end
            vlog('loop_done'); wait(t); delete(t);
            fprintf('%s %.0f\n', vlog_text, x);
            """));
    }

    [Fact]
    public void PauseIsADrainPoint()
    {
        Assert.Equal("tick;after_pause;", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartDelay', 0.01);
            start(t); pause(0.2); vlog('after_pause'); wait(t); delete(t);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void DrawnowIsADrainPoint()
    {
        Assert.Equal("tick;after_drawnow;", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartDelay', 0.01);
            start(t); pause(0.05); drawnow; vlog('after_drawnow'); wait(t); delete(t);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void TwoTimersFireInDueOrder()
    {
        Assert.Equal("b;a;", RunAndRead(Log + """
            a = timer('TimerFcn', @(~, ~) vlog('a'), 'StartDelay', 0.08);
            b = timer('TimerFcn', @(~, ~) vlog('b'), 'StartDelay', 0.01);
            start(a); start(b); wait(a); wait(b); delete(a); delete(b);
            disp(vlog_text);
            """));
    }

    // --- ownership (#105) -------------------------------------------------------------------------

    [Fact]
    public void ACallbackCapturesASnapshot()
    {
        Assert.Equal("[1 2 3]", RunAndRead("""
            global timer_out
            v = [1 2 3];
            t = timer('TimerFcn', @(~, ~) store_timer(v), 'StartDelay', 0.02);
            v(1) = 7;
            start(t); wait(t); delete(t);
            disp(mat2str(timer_out));
            function store_timer(x)
            global timer_out
            timer_out = x;
            end
            """));
    }

    [Fact]
    public void AHeldOperandIsReadBeforeACallbackWritesIt()
    {
        // r = g + pause_then_zero(): the callback writes the global during the pause; the operand
        // already read is held for the whole call (M5), so r sees the old value and g the new.
        Assert.Equal("[1 2 3] [7 2 3]", RunAndRead("""
            global g_timer
            g_timer = [1 2 3];
            t = timer('TimerFcn', @(~, ~) bump_timer(), 'StartDelay', 0.01);
            start(t);
            r = g_timer + pause_then_zero();
            wait(t); delete(t);
            fprintf('%s %s\n', mat2str(r), mat2str(g_timer));
            function bump_timer()
            global g_timer
            g_timer(1) = 7;
            end
            function z = pause_then_zero()
            pause(0.3);
            z = 0;
            end
            """));
    }

    [Theory]
    [InlineData("t = timer; u = [1 2 3]; t.UserData = u; u(1) = 7; w = t.UserData; w(2) = 8; disp(mat2str(t.UserData)); delete(t);", "[1 2 3]")]
    [InlineData("t = timer; t2 = t; t2.UserData = 5; disp(t.UserData); delete(t);", "5")]
    [InlineData("t = timer('UserData', [1 2 3]); v = t.UserData; v(1) = 9; disp(mat2str(t.UserData)); delete(t);", "[1 2 3]")]
    public void UserDataIsStoredAsAShareAndReadBackAsOne(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(code));
    }

    [Fact]
    public void ACallbackWritingUserDataThroughItsTimerLeavesAnEarlierReadAlone()
    {
        Assert.Equal("[7 2 3] [1 2 3]", RunAndRead("""
            t = timer('TimerFcn', @(src, ~) bump_userdata(src), 'StartDelay', 0.01);
            t.UserData = [1 2 3];
            u = t.UserData;
            start(t); wait(t);
            fprintf('%s %s\n', mat2str(t.UserData), mat2str(u));
            delete(t);
            function bump_userdata(t)
            t.UserData(1) = 7;
            end
            """));
    }

    // --- the event and the callback forms ---------------------------------------------------------

    [Theory]
    [InlineData("@(~, e) vlog(strjoin(fieldnames(e)', ','))", "Type,Data;")]
    [InlineData("@(~, e) vlog([e.Type ':' class(e.Data.time) ':' mat2str(size(e.Data.time))])", "TimerFcn:double:[1 6];")]
    [InlineData("@(src, ~) vlog([class(src) ':' src.Running])", "timer:on;")]
    [InlineData("'vlog(''str'')'", "str;")]
    [InlineData("{@cell_target, 'cellarg'}", "cellarg:TimerFcn;")]
    [InlineData("{'cell_target', 'named'}", "named:TimerFcn;")]
    public void ACallbackIsAHandleAStringOrACell(string callback, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + $"""
            t = timer('TimerFcn', {callback}, 'StartDelay', 0.01);
            start(t); wait(t); delete(t);
            disp(vlog_text);
            function cell_target(~, e, arg)
            vlog([arg ':' e.Type]);
            end
            """));
    }

    // --- modes, counts and the verbs --------------------------------------------------------------

    [Theory]
    [InlineData("'TasksToExecute', 3", "tick;stop; 1 off")]
    [InlineData("'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 3", "tick;tick;tick;stop; 3 off")]
    [InlineData("'ExecutionMode', 'fixedDelay', 'Period', 0.02, 'TasksToExecute', 2", "tick;tick;stop; 2 off")]
    [InlineData("'ExecutionMode', 'fixedSpacing', 'Period', 0.02, 'TasksToExecute', 2", "tick;tick;stop; 2 off")]
    public void ExecutionModeAndTasksToExecuteDecideHowOftenATimerFires(string settings, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + $"""
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01, {settings});
            start(t); wait(t);
            fprintf('%s %d %s\n', vlog_text, t.TasksExecuted, t.Running);
            delete(t);
            """));
    }

    [Fact]
    public void StopRunsTheStopFcnBeforeReturningAndEvenWhenNothingWasRunning()
    {
        Assert.Equal("stop;after_stop;stop;", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
            start(t); stop(t); vlog('after_stop'); stop(t); delete(t);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void ARestartCountsFromZero()
    {
        Assert.Equal("start;tick;stop;start;tick;stop; 1 1", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StartFcn', @(~, ~) vlog('start'), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01);
            start(t); wait(t); n1 = t.TasksExecuted; start(t); wait(t);
            fprintf('%s %d %d\n', vlog_text, n1, t.TasksExecuted);
            delete(t);
            """));
    }

    [Fact]
    public void ACallbackMayStopOrDeleteItsOwnTimer()
    {
        Assert.Equal("tick;stop; 1 off\nstop; 0", RunAndRead(Log + """
            t = timer('TimerFcn', @(t, ~) stop_and_log(t), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 5, 'StartDelay', 0.01);
            start(t); wait(t);
            fprintf('%s %d %s\n', vlog_text, t.TasksExecuted, t.Running);
            delete(t);
            vlog_text = '';
            d = timer('TimerFcn', @(src, ~) delete(src), 'StopFcn', @(~, ~) vlog('stop'), 'StartDelay', 0.01);
            start(d); pause(0.1);
            fprintf('%s %d\n', vlog_text, isvalid(d));
            function stop_and_log(t)
            vlog('tick');
            stop(t);
            end
            """));
    }

    [Fact]
    public void AFailingTimerFcnIsReportedRunsTheErrorFcnAndStopsTheTimer()
    {
        // The documented ErrorFcn contract, which R2025b does not honour (the fixture records its
        // silence as a divergence): the timer and an event whose Data carries the error.
        string answer = RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) error('probe:bad', 'bad thing'), 'ErrorFcn', @(src, e) vlog([class(src) '/' e.Type '/' e.Data.messageID '/' e.Data.message]), ...
                'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 0.02, 'TasksToExecute', 3, 'StartDelay', 0.01);
            start(t); wait(t);
            fprintf('%s %d %s\n', vlog_text, t.TasksExecuted, t.Running);
            delete(t);
            """);
        Assert.Equal("timer/ErrorFcn/probe:bad/bad thing;stop; 1 off", answer);
        Assert.Contains("Error while evaluating TimerFcn for timer 'timer-1'", _output.ErrorText);
        Assert.Contains("bad thing", _output.ErrorText);
    }

    [Theory]
    [InlineData("t = timer('StartDelay', 5); wait(t); disp('returned'); delete(t);", "returned")]
    [InlineData("t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1); a = t.Running; start(t); b = t.Running; stop(t); fprintf('%s %s %s\\n', a, b, t.Running); delete(t);", "off on off")]
    [InlineData("t = timer; fprintf('%s %s %s %s %s %s %d %s %s\\n', mat2str(t.Period), mat2str(t.StartDelay), t.ExecutionMode, mat2str(t.TasksToExecute), t.BusyMode, t.Running, t.TasksExecuted, mat2str(t.UserData), class(t.UserData)); delete(t);", "1 0 singleShot Inf drop off 0 [] double")]
    [InlineData("t = timer('timerfcn', @(~, ~) [], 'tag', 'lower'); fprintf('%s %s\\n', t.Tag, class(t)); delete(t);", "lower timer")]
    [InlineData("t = timer; fprintf('%d %s %d %d\\n', isvalid(t), class(isvalid(t)), isa(t, 'handle'), isa(t, 'timer')); delete(t);", "1 logical 1 1")]
    [InlineData("t = timer; delete(t); delete(t); fprintf('%d\\n', isvalid(t));", "0")]
    public void PropertiesAndVerbsAnswerAsRecorded(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(code));
    }

    [Fact]
    public void DeletingARunningTimerStopsItFirstWithAWarning()
    {
        Assert.Equal("stop;after_delete; 0", RunAndRead(Log + """
            t = timer('TimerFcn', @(~, ~) vlog('tick'), 'StopFcn', @(~, ~) vlog('stop'), 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1);
            start(t); delete(t); vlog('after_delete');
            fprintf('%s %d\n', vlog_text, isvalid(t));
            """));
        Assert.Contains("You are deleting one or more running timer objects", _output.ErrorText);
    }

    // --- refusals, in R2025b's words --------------------------------------------------------------

    [Theory]
    [InlineData("t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1); start(t); wait(t);", "Can't wait with a timer that has an infinite TasksToExecute.")]
    [InlineData("t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1); start(t); start(t);", "Cannot start timer because it is already running.")]
    [InlineData("t = timer; start(t);", "Cannot start timer without specifying a 'TimerFcn' callback.")]
    [InlineData("t = timer('TimerFcn', @(~, ~) [], 'ExecutionMode', 'fixedRate', 'Period', 1, 'StartDelay', 1); start(t); t.Period = 0.5;", "Period cannot be set while Timer is running.")]
    [InlineData("t = timer('Foo', 1);", "The name 'Foo' is not an accessible property for an instance of class 'timer'.")]
    [InlineData("t = timer; t.Foo = 1;", "Unrecognized property 'Foo' for class 'timer'.")]
    [InlineData("t = timer; x = t.Foo;", "Unrecognized method, property, or field 'Foo' for class 'timer'.")]
    [InlineData("t = timer('ExecutionMode', 'sometimes');", "Error setting property 'ExecutionMode' of class 'timer'. 'sometimes' is invalid. Value must be 'singleShot', 'fixedSpacing', 'fixedDelay', or 'fixedRate'.")]
    [InlineData("t = timer('Period', -1);", "Error setting property 'Period' of class 'timer'. Value must be positive.")]
    [InlineData("t = timer('StartDelay', -1);", "Error setting property 'StartDelay' of class 'timer'. Value must be nonnegative.")]
    [InlineData("t = timer('TasksToExecute', 0);", "Error setting property 'TasksToExecute' of class 'timer'. Value must be positive.")]
    [InlineData("t = timer('TimerFcn', 5);", "TimerFcn callback must be set to a string scalar, a function handle, or a 1-by-N cell array.")]
    [InlineData("t = timer; t.TasksExecuted = 5;", "Unable to set the 'TasksExecuted' property of class ''timer'' because it is read-only.")]
    [InlineData("t = timer; t.Running = 'on';", "Unable to set the 'Running' property of class ''timer'' because it is read-only.")]
    [InlineData("t = timer; delete(t); x = t.TasksExecuted;", "Invalid or deleted object.")]
    [InlineData("t = timer; delete(t); t.UserData = 5;", "Invalid or deleted object.")]
    [InlineData("t = timer('TimerFcn', @(~, ~) []); delete(t); start(t);", "Invalid timer object. This object has been deleted and should be removed from your workspace using CLEAR.")]
    [InlineData("t = timer; delete(t); wait(t);", "Invalid timer object. This object has been deleted and should be removed from your workspace using CLEAR.")]
    [InlineData("start(5);", "Undefined function 'start' for input arguments of type 'double'.")]
    public void TimersRefuseInMatlabsWords(string code, string expected)
    {
        Assert.Contains(expected, RunExpectingError(code));
    }
}
