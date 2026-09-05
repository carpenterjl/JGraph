using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A debug run inside a live console session, and the <c>K&gt;&gt;</c> prompt over it: the script
/// shares the prompt's workspace, and a statement typed while paused reads and writes the paused
/// frame. Driven lock-step with timeouts like <see cref="JgsDebugSessionTests"/>.
/// </summary>
[Collection("JG facade")]
public class JgsDebugSharedWorkspaceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();

    public JgsDebugSharedWorkspaceTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

    private IScriptSession NewSession(IScriptEngine engine) =>
        Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());

    private JgsDebugSession CreateDebugSession(IScriptEngine engine)
    {
        JgsDebugSession session = Assert.IsAssignableFrom<IJgsDebuggable>(engine).CreateDebugSession();
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

    private static Task<ScriptRunResult> Exec(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private static ScriptRunResult Eval(JgsDebugSession debug, string code, int frame = 0) =>
        Await(debug.EvaluateAsync(code, frame, CancellationToken.None));

    [Fact]
    public async Task DebugRun_SharesTheConsoleWorkspace_BothWays()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        Assert.True((await Exec(session, "let base = 5")).Success);

        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            let y = base * 2
            print(y)
            """, CancellationToken.None);

        Assert.Equal(2, NextPause().Location.Line);
        IReadOnlyList<ScriptVariable> paused = debug.GetVariables();
        Assert.Equal(5.0, Assert.Single(paused, v => v.Name == "base").RawValue);
        Assert.Equal(10.0, Assert.Single(paused, v => v.Name == "y").RawValue);

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message);

        // What the debugged script left behind is there at the prompt afterwards.
        ScriptRunResult after = await Exec(session, "print(y + 1)");
        Assert.True(after.Success, after.Message);
        Assert.Contains("11", _output.NormalText);
        Assert.Contains(after.Variables, v => v.Name == "y");
    }

    [Fact]
    public async Task Evaluate_ReadsAndWritesThePausedFrame()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            let x = 1
            print(x)
            """, CancellationToken.None);

        NextPause();
        ScriptRunResult read = Eval(debug, "print(x * 100)");
        Assert.True(read.Success, read.Message);
        Assert.Contains("100", _output.NormalText);

        ScriptRunResult write = Eval(debug, "x = 42");
        Assert.True(write.Success, write.Message);
        Assert.Equal(42.0, Assert.Single(write.Variables, v => v.Name == "x").RawValue);
        Assert.True(debug.IsPaused);

        debug.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("42", _output.NormalLines[^1]); // the script printed the value the prompt set
    }

    [Fact]
    public async Task Evaluate_InACallerFrame_SeesAndChangesTheCallersVariables()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            fn inner(n) {
                let m = n + 1
                return m
            }
            let outer = 7
            let r = inner(outer)
            print(outer)
            """, CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal("inner", pause.CallStack[0].FunctionName);
        Assert.Equal("(script)", pause.CallStack[1].FunctionName);

        // Frame 0 is inside inner (n is there); frame 1 is the script, where n does not exist.
        Assert.Contains(debug.GetVariables(0), v => v.Name == "n");
        Assert.DoesNotContain(debug.GetVariables(1), v => v.Name == "n");

        ScriptRunResult caller = Eval(debug, "outer = 100", frame: 1);
        Assert.True(caller.Success, caller.Message);
        Assert.Equal(100.0, Assert.Single(caller.Variables, v => v.Name == "outer").RawValue);

        debug.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("100", _output.NormalLines[^1]);
    }

    [Fact]
    public async Task Evaluate_Error_IsReported_AndTheScriptStaysPaused()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            let x = 1
            print(x)
            """, CancellationToken.None);

        NextPause();
        ScriptRunResult failed = Eval(debug, "noSuchFunction(1)");
        Assert.False(failed.Success);
        Assert.NotEmpty(_output.ErrorText);
        Assert.True(debug.IsPaused);
        Assert.Equal(2, debug.GetCallStack()[0].Line);

        debug.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public async Task Evaluate_DoesNotStopAtBreakpoints_InWhatItCalls()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2, 5 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            fn helper(v) {
                let q = v * 3
                return q
            }
            let x = 1
            print(x)
            """, CancellationToken.None);

        Assert.Equal(5, NextPause().Location.Line);
        ScriptRunResult result = Eval(debug, "print(helper(2))");
        Assert.True(result.Success, result.Message);
        Assert.Contains("6", _output.NormalText);
        Assert.Empty(_pauses); // the breakpoint on helper's line 2 did not fire for the typed call
        Assert.Equal(5, debug.GetCallStack()[0].Line); // and the paused frame is untouched

        // The debugger's own view of the stack survived the call: stepping still works.
        debug.StepOver();
        Assert.Equal(6, NextPause().Location.Line);
        debug.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public async Task Continue_WhileAStatementIsEvaluating_IsRefused()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            let x = 1
            print(x)
            """, CancellationToken.None);

        NextPause();
        using var cancel = new CancellationTokenSource();
        Task<ScriptRunResult> spinning = debug.EvaluateAsync("while (true) { x = x + 1 }", 0, cancel.Token);
        SpinWait.SpinUntil(() => debug.IsEvaluating, Timeout);
        Assert.True(debug.IsEvaluating);

        Assert.Throws<InvalidOperationException>(() => debug.Continue());
        await Assert.ThrowsAsync<InvalidOperationException>(() => debug.EvaluateAsync("x", 0, CancellationToken.None));

        cancel.Cancel();
        ScriptRunResult interrupted = Await(spinning);
        Assert.False(interrupted.Success);
        Assert.False(debug.IsEvaluating);
        Assert.True(debug.IsPaused);

        debug.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public async Task Stop_WhileAStatementIsEvaluating_EndsBoth()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main", new[] { 2 });
        using var stop = new CancellationTokenSource();
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            let x = 1
            print(x)
            """, stop.Token);

        NextPause();
        Task<ScriptRunResult> spinning = debug.EvaluateAsync("while (true) { x = x + 1 }", 0, CancellationToken.None);
        SpinWait.SpinUntil(() => debug.IsEvaluating, Timeout);

        stop.Cancel();
        Assert.False(Await(spinning).Success);
        Assert.False(Await(run).Success);
        Assert.False(debug.IsPaused);

        // The session itself is fine afterwards.
        Assert.True((await Exec(session, "print(x)")).Success);
    }

    [Fact]
    public async Task Matlab_Evaluate_AutoDeclares_AndEchoes()
    {
        var engine = new MatlabScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = CreateDebugSession(engine);
        debug.SetBreakpoints("main.m", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main.m", """
            y = 1;
            disp(y + z)
            """, CancellationToken.None);

        NextPause();
        ScriptRunResult declared = Eval(debug, "z = 9");
        Assert.True(declared.Success, declared.Message);
        Assert.Contains("z =", _output.NormalText); // unsuppressed assignment echoes, as at the prompt
        Assert.Equal(9.0, Assert.Single(declared.Variables, v => v.Name == "z").RawValue);

        debug.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("10", _output.NormalLines[^1]);
    }

    [Fact]
    public async Task AForeignSession_IsRefused()
    {
        var engine = new JgsScriptEngine();
        JgsDebugSession debug = CreateDebugSession(engine);
        await using IScriptSession csharp = NewSession(new CSharpScriptEngine());
        await Assert.ThrowsAsync<ArgumentException>(() => debug.RunAsync(csharp, "main", "let x = 1", CancellationToken.None));
    }
}
