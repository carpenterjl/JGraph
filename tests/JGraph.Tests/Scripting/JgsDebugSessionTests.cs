using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The JGS debugger, black-box through the public <see cref="JgsDebugSession"/>. The tests drive the
/// session lock-step: every pause lands in a queue, the test asserts against it, then resumes — with
/// timeouts so a debugger bug fails the test instead of hanging the suite.
/// </summary>
[Collection("JG facade")]
public class JgsDebugSessionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();

    public JgsDebugSessionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context(string? workingDirectory = null) => new(_output, (_, figure) => _figures.Add(figure), workingDirectory);

    private JgsDebugSession CreateSession()
    {
        JgsDebugSession session = _engine.CreateDebugSession();
        session.Paused += (_, e) => _pauses.Add(e);
        return session;
    }

    private JgsPausedEventArgs NextPause()
    {
        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? args, Timeout), "Timed out waiting for a pause.");
        return args!;
    }

    private static ScriptRunResult Await(Task<ScriptRunResult> task)
    {
        Assert.True(task.Wait(Timeout), "Timed out waiting for the run to finish.");
        return task.Result;
    }

    [Fact]
    public async Task Breakpoint_PausesAtTheRightLineAndFile_WithVariablesVisible()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let x = 10
            let y = 20
            let sum = x + y
            print(sum)
            """, Context(), CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(PauseReason.Breakpoint, pause.Reason);
        Assert.Equal(3, pause.Location.Line);
        Assert.Equal("main", pause.Location.SourceId);
        Assert.True(session.IsPaused);

        // Paused BEFORE line 3 runs: x and y exist, sum does not.
        IReadOnlyList<ScriptVariable> variables = session.GetVariables();
        Assert.Equal(10.0, Assert.Single(variables, v => v.Name == "x").RawValue);
        Assert.Equal(20.0, Assert.Single(variables, v => v.Name == "y").RawValue);
        Assert.DoesNotContain(variables, v => v.Name == "sum");

        session.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message);
        Assert.Contains("30", _output.NormalText);
        await Task.CompletedTask;
    }

    [Fact]
    public void Continue_RunsToTheNextBreakpoint()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 2, 4 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            let c = 3
            let d = 4
            """, Context(), CancellationToken.None);

        Assert.Equal(2, NextPause().Location.Line);
        session.Continue();
        Assert.Equal(4, NextPause().Location.Line);
        session.Continue();

        Assert.True(Await(run).Success);
    }

    [Fact]
    public void StepOver_RunsTheCallToCompletion_AndStopsOnTheNextLine()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 5 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            fn twice(n) {
                return n * 2
            }

            let y = twice(21)
            let z = y + 1
            """, Context(), CancellationToken.None);

        Assert.Equal(5, NextPause().Location.Line);
        session.StepOver();

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(PauseReason.Step, pause.Reason);
        Assert.Equal(6, pause.Location.Line);          // the call ran to completion
        Assert.Single(pause.CallStack);                 // still at the top level
        Assert.Equal(42.0, Assert.Single(session.GetVariables(), v => v.Name == "y").RawValue);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void StepIn_EntersTheFunction_AndStepOutReturns()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 5 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            fn twice(n) {
                return n * 2
            }

            let y = twice(21)
            let z = y + 1
            """, Context(), CancellationToken.None);

        Assert.Equal(5, NextPause().Location.Line);
        session.StepIn();

        JgsPausedEventArgs inside = NextPause();
        Assert.Equal(2, inside.Location.Line);          // first statement of twice's body
        Assert.Equal(2, inside.CallStack.Count);
        Assert.Equal("twice", inside.CallStack[0].FunctionName);
        Assert.Equal("(script)", inside.CallStack[1].FunctionName);
        Assert.Equal(5, inside.CallStack[1].Line);      // the caller sits at the call site

        // Inside the function, the parameter is visible.
        Assert.Equal(21.0, Assert.Single(session.GetVariables(), v => v.Name == "n").RawValue);

        session.StepOut();
        JgsPausedEventArgs back = NextPause();
        Assert.Equal(6, back.Location.Line);            // returned to the statement after the call
        Assert.Single(back.CallStack);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void StepOver_InsideALoopBody_StopsOnEveryIteration()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let total = 0
            for i in [1, 2, 3] {
                total = total + i
            }
            """, Context(), CancellationToken.None);

        // The breakpoint hits on each iteration; step-over from it also stops per statement.
        Assert.Equal(3, NextPause().Location.Line);     // i = 1
        session.Continue();
        Assert.Equal(3, NextPause().Location.Line);     // i = 2
        Assert.Equal(1.0, Assert.Single(session.GetVariables(), v => v.Name == "total").RawValue);
        session.StepOver();
        Assert.Equal(3, NextPause().Location.Line);     // i = 3 — same line, new execution
        Assert.Equal(3.0, Assert.Single(session.GetVariables(), v => v.Name == "total").RawValue);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void Breakpoints_InARunIncludedFile_Hit_AndStepInDescendsIntoIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"jgraph_dbg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string libPath = Path.Combine(dir, "lib.jgs");
            File.WriteAllText(libPath, """
                let shared = 5
                let doubled = shared * 2
                """);

            JgsDebugSession session = CreateSession();
            session.SetBreakpoints(libPath, new[] { 2 });

            Task<ScriptRunResult> run = session.RunAsync("main", """
                run("lib.jgs")
                print(doubled)
                """, Context(dir), CancellationToken.None);

            // The breakpoint in the included file hits, keyed by its resolved path.
            JgsPausedEventArgs pause = NextPause();
            Assert.Equal(2, pause.Location.Line);
            Assert.Equal(libPath, pause.Location.SourceId);
            Assert.Equal(5.0, Assert.Single(session.GetVariables(), v => v.Name == "shared").RawValue);

            // Stepping continues inside the included file, then back into the main script.
            session.StepOver();
            JgsPausedEventArgs next = NextPause();
            Assert.Equal("main", next.Location.SourceId);
            Assert.Equal(2, next.Location.Line);        // print(doubled), after run() finished

            session.Continue();
            Assert.True(Await(run).Success);
            Assert.Contains("10", _output.NormalText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Pause_InterruptsATightLoop()
    {
        JgsDebugSession session = CreateSession();
        using var cts = new CancellationTokenSource();

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let i = 0
            while true {
                i = i + 1
            }
            """, Context(), cts.Token);

        // Enter the loop deterministically first (breakpoint), then let it spin freely and break in.
        session.SetBreakpoints("main", new[] { 3 });
        NextPause();
        session.SetBreakpoints("main", Array.Empty<int>());
        session.Continue();

        session.Pause();
        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(PauseReason.PauseRequest, pause.Reason);
        Assert.True((double)Assert.Single(session.GetVariables(), v => v.Name == "i").RawValue! >= 0);

        cts.Cancel();
        Assert.False(Await(run).Success);
    }

    [Fact]
    public void Stop_WhilePaused_CancelsCleanly_AndRaisesResumed()
    {
        JgsDebugSession session = CreateSession();
        using var resumed = new ManualResetEventSlim(false);
        session.Resumed += (_, _) => resumed.Set();
        session.SetBreakpoints("main", new[] { 2 });
        using var cts = new CancellationTokenSource();

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            """, Context(), cts.Token);

        NextPause();
        cts.Cancel();

        ScriptRunResult result = Await(run);
        Assert.False(result.Success);
        Assert.Contains("cancel", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(resumed.Wait(Timeout));
        Assert.False(session.IsPaused);
    }

    [Fact]
    public void Variables_ShowShadowing_AndCallerFramesSeeTheirOwnScope()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 4 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let x = 1
            fn f() {
                let x = 2
                return x
            }
            let y = f()
            """, Context(), CancellationToken.None);

        NextPause();

        // Innermost frame: the local x shadows the global.
        Assert.Equal(2.0, Assert.Single(session.GetVariables(0), v => v.Name == "x").RawValue);

        // The script frame sees the global x (and f, a user function).
        IReadOnlyList<ScriptVariable> scriptScope = session.GetVariables(1);
        Assert.Equal(1.0, Assert.Single(scriptScope, v => v.Name == "x").RawValue);
        Assert.Equal("function", Assert.Single(scriptScope, v => v.Name == "f").Type);

        // Untouched builtins stay hidden in both frames.
        Assert.DoesNotContain(scriptScope, v => v.Name is "sin" or "plot" or "run");

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void Variables_ProjectArraysAndTables_ForTheDataViewer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jgraph_dbg_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "x,y\n1,10\n2,20");
        try
        {
            JgsDebugSession session = CreateSession();
            session.SetBreakpoints("main", new[] { 3 });

            Task<ScriptRunResult> run = session.RunAsync("main", $"""
                let a = [1, 2, 3]
                let t = readcsv("{path.Replace('\\', '/')}")
                print("done")
                """, Context(), CancellationToken.None);

            NextPause();
            IReadOnlyList<ScriptVariable> variables = session.GetVariables();
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(
                Assert.Single(variables, v => v.Name == "a").RawValue));
            Assert.Equal(2, Assert.IsType<JGraph.Data.Table>(
                Assert.Single(variables, v => v.Name == "t").RawValue).RowCount);

            session.Continue();
            Assert.True(Await(run).Success);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Inspection_WhenNotPaused_Throws()
    {
        JgsDebugSession session = CreateSession();
        Assert.Throws<InvalidOperationException>(() => session.GetCallStack());
        Assert.Throws<InvalidOperationException>(() => session.GetVariables());
    }

    [Fact]
    public void RunAsync_ASecondTime_Throws()
    {
        JgsDebugSession session = CreateSession();
        Task<ScriptRunResult> first = session.RunAsync("main", "let a = 1", Context(), CancellationToken.None);
        Assert.True(Await(first).Success);

        // The second-run guard throws synchronously, before any task is created.
        Assert.Throws<InvalidOperationException>(
            () => { _ = session.RunAsync("main", "let b = 2", Context(), CancellationToken.None); });
    }

    [Fact]
    public void SetBreakpoints_WhileRunning_TakesEffect()
    {
        JgsDebugSession session = CreateSession();
        using var cts = new CancellationTokenSource();

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let i = 0
            while i < 100000000 {
                i = i + 1
            }
            """, Context(), cts.Token);

        // Arm the loop-body breakpoint while the script is already spinning.
        session.SetBreakpoints("main", new[] { 3 });
        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(3, pause.Location.Line);
        Assert.Equal(PauseReason.Breakpoint, pause.Reason);

        session.SetBreakpoints("main", Array.Empty<int>()); // clear and finish... or stop the long loop
        cts.Cancel();
        Assert.False(Await(run).Success);
    }
}
