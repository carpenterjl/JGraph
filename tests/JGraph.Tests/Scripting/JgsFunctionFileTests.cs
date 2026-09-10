using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M145, step 5: a file's functions live with the file, not in any workspace, and a file's scope
/// sits under the built-in layer rather than the base workspace. Every expectation here was
/// recorded from R2025b (<c>prec5/probe5.out</c>): a path function and a script's local function
/// read no variable of the script; a path function does not see the calling script's local
/// function; two scripts' same-named local functions coexist; the local functions of a script run
/// from inside a function are not that function's nested functions; and what must survive all of
/// that — globals, persistents, a called script's shared workspace, retained closures — does.
/// </summary>
[Collection("JG facade")]
public class JgsFunctionFileTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m145-storage-" + Guid.NewGuid().ToString("N"));

    public JgsFunctionFileTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private static T Await<T>(Task<T> task)
    {
        Assert.True(task.Wait(Timeout), "Timed out.");
        return task.Result;
    }

    private string WriteFile(string name, string source)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, source);
        return path;
    }

    private static string Undefined(string name) => $"'{name}' is not recognized as a variable or a function.";

    // --- The four leaks, closed -------------------------------------------------------------------

    /// <summary>R2025b: <c>Unrecognized function or variable 'gain'.</c> from both.</summary>
    [Fact]
    public async Task APathFunction_AndAScriptLocalFunction_ReadNoVariableOfTheScript()
    {
        WriteFile("reads_gain.m", """
            function y = reads_gain(x)
                y = gain * x;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            gain = 3;
            try
                a = reads_gain(2);
            catch e
                a = e.message;
            end
            try
                b = local_reads_gain(2);
            catch e
                b = e.message;
            end
            c = gain * 2;
            function y = local_reads_gain(x)
                y = gain * x;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(Undefined("gain"), Value(result, "a"));
        Assert.Equal(Undefined("gain"), Value(result, "b"));
        Assert.Equal(6.0, Value(result, "c"));
    }

    /// <summary>R2025b: <c>Unrecognized function or variable 'helper'.</c></summary>
    [Fact]
    public async Task APathFunction_DoesNotSeeTheCallingScriptsLocalFunction()
    {
        WriteFile("calls_helper.m", """
            function s = calls_helper()
                s = helper();
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            try
                a = calls_helper();
            catch e
                a = e.message;
            end
            b = helper();
            function s = helper()
                s = 'script-helper';
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(Undefined("helper"), Value(result, "a"));
        Assert.Equal("script-helper", Value(result, "b"));
    }

    [Fact]
    public async Task TwoScripts_SameNamedLocalFunctions_Coexist_AndNeitherReachesTheCaller()
    {
        WriteFile("scriptA.m", """
            a_answer = helper();
            function s = helper()
                s = 'A-helper';
            end
            """);
        WriteFile("scriptB.m", """
            b_answer = helper();
            function s = helper()
                s = 'B-helper';
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            scriptA
            first = a_answer;
            scriptB
            second = b_answer;
            scriptA
            third = a_answer;
            run('scriptB.m');
            fourth = b_answer;
            try
                leaked = helper();
            catch e
                leaked = e.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("A-helper", Value(result, "first"));
        Assert.Equal("B-helper", Value(result, "second"));
        Assert.Equal("A-helper", Value(result, "third"));
        Assert.Equal("B-helper", Value(result, "fourth"));
        Assert.Equal(Undefined("helper"), Value(result, "leaked"));
    }

    /// <summary>
    /// R2025b: <c>body-k=11|body-w=20|local-k|…'k'.|local-G5|…'G5'.</c> — the script's body shares
    /// the function's workspace; the script's local function shares nothing, not even the
    /// function's <c>global</c> declaration.
    /// </summary>
    [Fact]
    public async Task AScriptRunInsideAFunction_SharesItsBodyButNotItsLocalFunctions()
    {
        WriteFile("outer_runs_script.m", """
            function s = outer_runs_script()
                k = 10;
                global G5
                G5 = 3;
                persistent p
                p = 4;
                inner_script
                s = ['body-k=' num2str(k) '|body-w=' num2str(w) '|' local_report];
            end
            """);
        WriteFile("inner_script.m", """
            k = k + 1;
            w = 20;
            local_report = try_local();
            function s = try_local()
                try
                    s = ['local-sees-k=' num2str(k)];
                catch e
                    s = ['local-k|' e.message];
                end
                try
                    s = [s '|local-sees-G5=' num2str(G5)];
                catch e
                    s = [s '|local-G5|' e.message];
                end
                try
                    s = [s '|local-sees-p=' num2str(p)];
                catch e
                    s = [s '|local-p|' e.message];
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("s = outer_runs_script();");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(
            $"body-k=11|body-w=20|local-k|{Undefined("k")}|local-G5|{Undefined("G5")}|local-p|{Undefined("p")}",
            Value(result, "s"));
    }

    // --- What survives ----------------------------------------------------------------------------

    /// <summary>R2025b: <c>6|14|8</c>, <c>7|3</c>.</summary>
    [Fact]
    public async Task ADeclaredGlobal_AndAPersistent_StillWork_FromAPathFunction_AndAScriptLocalFunction()
    {
        WriteFile("reads_global.m", """
            function y = reads_global()
                global G
                y = G * 2;
            end
            """);
        WriteFile("counter.m", """
            function n = counter()
                persistent c
                if isempty(c)
                    c = 0;
                end
                c = c + 1;
                n = c;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            global G
            G = 7;
            a = reads_global();
            b = local_reads_global();
            counter(); counter();
            c = counter();
            local_counter(); local_counter();
            d = local_counter();
            function y = local_reads_global()
                global G
                y = G + 1;
            end
            function n = local_counter()
                persistent c
                if isempty(c)
                    c = 0;
                end
                c = c + 10;
                n = c;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(14.0, Value(result, "a"));
        Assert.Equal(8.0, Value(result, "b"));
        Assert.Equal(3.0, Value(result, "c"));
        Assert.Equal(30.0, Value(result, "d"));
    }

    /// <summary>R2025b: <c>9|script-q|1</c> — the script runs in its caller's workspace, so the path function's caller is the base workspace.</summary>
    [Fact]
    public async Task ACalledScript_SharesTheCallersWorkspace_AndEvalinCaller_FromAPathFunctionItCalls_ReadsIt()
    {
        WriteFile("runs_evalin_script.m", "seen_q = peeks_q();\n");
        WriteFile("peeks_q.m", """
            function s = peeks_q()
                s = evalin('caller', 'q');
                assignin('caller', 'q_seen_by_peek', 1);
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            q = 'script-q';
            runs_evalin_script
            a = seen_q;
            b = q_seen_by_peek;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("script-q", Value(result, "a"));
        Assert.Equal(1.0, Value(result, "b"));
    }

    /// <summary>R2025b: <c>11|101|202</c>.</summary>
    [Fact]
    public async Task TwoOuterFunctions_WithSameNamedNestedHelpers_KeepTheirOwnClosures()
    {
        WriteFile("two_outers.m", """
            function [f1, f2] = two_outers()
                f1 = first();
                f2 = second();
            end
            function f = first()
                base = 100;
                f = @() nestedHelper();
                function y = nestedHelper()
                    y = base + 1;
                end
            end
            function f = second()
                base = 200;
                f = @() nestedHelper();
                function y = nestedHelper()
                    y = base + 2;
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            [f1, f2] = two_outers();
            a = f1();
            b = f2();
            [g1, g2] = two_outers();
            c = g1() + g2();
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(101.0, Value(result, "a"));
        Assert.Equal(202.0, Value(result, "b"));
        Assert.Equal(303.0, Value(result, "c"));
    }

    /// <summary>R2025b: <c>8|maker5-helper-1</c>.</summary>
    [Fact]
    public async Task AHandleReturnedFromAPathFile_KeepsThatFilesHelper_AfterCd()
    {
        WriteFile("maker5.m", """
            function h = maker5()
                h = @(x) helper(x);
            end
            function s = helper(x)
                s = ['maker5-helper-' num2str(x)];
            end
            """);
        string elsewhere = Path.Combine(_folder, "elsewhere");
        Directory.CreateDirectory(elsewhere);

        ScriptRunResult result = await RunMatlab($"""
            h = maker5();
            cd('{elsewhere.Replace("\\", "\\\\")}');
            a = h(1);
            function s = helper(x)
                s = 'script-helper';
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("maker5-helper-1", Value(result, "a"));
    }

    /// <summary>R2025b: <c>10|10|1</c> — the script's own function is still callable and a persistent starts over.</summary>
    [Fact]
    public async Task ClearAll_KeepsAScriptsOwnFunctionsCallable_AndStartsPersistentsOver()
    {
        WriteFile("counter.m", """
            function n = counter()
                persistent c
                if isempty(c)
                    c = 0;
                end
                c = c + 1;
                n = c;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            counter(); counter(); counter();
            clear all
            a = local_after_clear(5);
            b = counter();
            clear functions
            c = counter();
            function y = local_after_clear(x)
                y = x * 2;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(10.0, Value(result, "a"));
        Assert.Equal(1.0, Value(result, "b"));
        Assert.Equal(1.0, Value(result, "c"));
    }

    [Fact]
    public async Task AJgsScript_RunningAnMFile_GetsItsResults()
    {
        WriteFile("lib.m", """
            t = twice(4);
            function y = twice(x)
                y = 2 * x;
            end
            """);

        ScriptRunResult result = await new JgsScriptEngine().RunAsync("""
            run("lib.m")
            let u = t + 1
            """, Context(), default);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(8.0, Value(result, "t"));
        Assert.Equal(9.0, Value(result, "u"));
    }

    // --- The debugger -----------------------------------------------------------------------------

    private (IScriptSession Session, JgsDebugSession Debug) NewDebugger()
    {
        var engine = new MatlabScriptEngine();
        IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());
        JgsDebugSession debug = Assert.IsAssignableFrom<IJgsDebuggable>(engine).CreateDebugSession();
        debug.Paused += (_, e) => _pauses.Add(e);
        return (session, debug);
    }

    private JgsPausedEventArgs NextPause()
    {
        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? pause, Timeout), "Timed out waiting for a pause.");
        return pause!;
    }

    /// <summary>The step-2 gate again, paused inside a path function whose scope no longer reaches the base workspace by walking.</summary>
    [Fact]
    public async Task PausedInsideAPathFunction_TheScriptFrameIsStillTheBaseWorkspace()
    {
        string helper = WriteFile("helper.m", """
            function r = helper(n)
                r = n + 1;
            end
            """);
        (IScriptSession session, JgsDebugSession debug) = NewDebugger();
        await using IScriptSession owned = session;
        Assert.True((await session.ExecuteAsync("base = 5;", "", CancellationToken.None)).Success);
        debug.SetBreakpoints(helper, new[] { 2 });

        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            r = helper(base);
            disp(r)
            disp(base)
            """, CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal("helper", pause.CallStack[0].FunctionName);
        Assert.Equal("(script)", pause.CallStack[1].FunctionName);

        IReadOnlyList<ScriptVariable> script = debug.GetVariables(1);
        Assert.Equal(5.0, Assert.Single(script, v => v.Name == "base").RawValue);
        Assert.DoesNotContain(script, v => v.Name is "n" or "pi" or "max");

        // Frame 0 is the path function: its parameter, the built-ins, and no base variable.
        ScriptRunResult inside = Await(debug.EvaluateAsync("try; seen = base; catch; seen = 'absent'; end", 0, CancellationToken.None));
        Assert.True(inside.Success, inside.Message);
        Assert.Equal("absent", Value(inside, "seen"));

        ScriptRunResult read = Await(debug.EvaluateAsync("m = max([1 9 2]);", 1, CancellationToken.None));
        Assert.True(read.Success, read.Message);
        Assert.Equal(9.0, Value(read, "m"));

        ScriptRunResult write = Await(debug.EvaluateAsync("base = 100;", 1, CancellationToken.None));
        Assert.True(write.Success, write.Message);

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("100", _output.NormalText);
        Assert.Equal(100.0, Value(await session.ExecuteAsync("again = base;", "", CancellationToken.None), "again"));
    }

    /// <summary>The record for <c>b</c> names the script's line, not the last line of <c>a.m</c>, and its own helper is its file's.</summary>
    [Fact]
    public async Task TwoCallsInOneExpression_TheSecondsCallerIsTheScriptsLine_AndItsHelperIsItsOwn()
    {
        WriteFile("a.m", """
            function y = a()
                y = helper();
            end
            function y = helper()
                y = 1;
            end
            """);
        string b = WriteFile("b.m", """
            function y = b()
                y = helper() * 10;
            end
            function y = helper()
                y = 2;
            end
            """);
        (IScriptSession session, JgsDebugSession debug) = NewDebugger();
        await using IScriptSession owned = session;
        debug.SetBreakpoints(b, new[] { 2 });

        Task<ScriptRunResult> run = debug.RunAsync(session, "main", "x = a() + b();\n", CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal("b", pause.CallStack[0].FunctionName);
        Assert.Equal("main", pause.CallStack[1].SourceId);
        Assert.Equal(1, pause.CallStack[1].Line);

        ScriptRunResult own = Await(debug.EvaluateAsync("k = helper();", 0, CancellationToken.None));
        Assert.True(own.Success, own.Message);
        Assert.Equal(2.0, Value(own, "k"));

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(21.0, Value(await session.ExecuteAsync("y = x;", "", CancellationToken.None), "y"));
    }

    /// <summary>
    /// An anonymous body is a frame of its own: paused in <c>g</c> reached through an escaped handle,
    /// the stack shows the body as <c>g</c>'s caller, selecting it reads and writes the closure's
    /// workspace and resolves <c>helper</c> from <c>maker.m</c>, and the frame above it is the script.
    /// </summary>
    [Fact]
    public async Task PausedThroughAnEscapedHandle_TheAnonymousBodyIsAFrame_WithItsWorkspaceAndFile()
    {
        WriteFile("maker.m", """
            function h = maker()
                v = 'maker-v';
                h = @(x) [g(x) '|' v];
            end
            function s = helper(x)
                s = 'maker-helper';
            end
            """);
        string g = WriteFile("g.m", """
            function y = g(x)
                y = ['g' num2str(x)];
            end
            """);
        (IScriptSession session, JgsDebugSession debug) = NewDebugger();
        await using IScriptSession owned = session;
        debug.SetBreakpoints(g, new[] { 2 });

        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            v = 'script-v';
            h = maker();
            out = h(1);
            function s = helper(x)
                s = 'script-helper';
            end
            """, CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal("g", pause.CallStack[0].FunctionName);
        Assert.Equal("@(x) [g(x), '|', v]", pause.CallStack[1].FunctionName);
        Assert.Equal("(script)", pause.CallStack[2].FunctionName);
        Assert.Equal("main", pause.CallStack[2].SourceId);
        Assert.Equal(3, pause.CallStack[2].Line);

        ScriptRunResult closure = Await(debug.EvaluateAsync("a = v; b = helper(1); c = x;", 1, CancellationToken.None));
        Assert.True(closure.Success, closure.Message);
        Assert.Equal("maker-v", Value(closure, "a"));
        Assert.Equal("maker-helper", Value(closure, "b"));
        Assert.Equal(1.0, Value(closure, "c"));

        ScriptRunResult script = Await(debug.EvaluateAsync("d = v; e = helper(1);", 2, CancellationToken.None));
        Assert.True(script.Success, script.Message);
        Assert.Equal("script-v", Value(script, "d"));
        Assert.Equal("script-helper", Value(script, "e"));

        ScriptRunResult write = Await(debug.EvaluateAsync("v = 'changed';", 1, CancellationToken.None));
        Assert.True(write.Success, write.Message);

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("g1|changed", Value(await session.ExecuteAsync("o = out;", "", CancellationToken.None), "o"));
        Assert.Equal("script-v", Value(await session.ExecuteAsync("w = v;", "", CancellationToken.None), "w"));
    }

    /// <summary>
    /// A class property default is a frame of its own too: it sees no variable of the script, and
    /// the frame above it is the script that constructed the object.
    /// </summary>
    [Fact]
    public async Task PausedThroughAPropertyDefault_TheDefaultIsAFrame_AndTheFrameAboveIsTheScript()
    {
        WriteFile("Box.m", """
            classdef Box
                properties
                    w = g_default(1)
                end
            end
            """);
        string g = WriteFile("g_default.m", """
            function y = g_default(x)
                y = x + 1;
            end
            """);
        (IScriptSession session, JgsDebugSession debug) = NewDebugger();
        await using IScriptSession owned = session;
        debug.SetBreakpoints(g, new[] { 2 });

        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            v = 'script-v';
            b = Box();
            w = b.w;
            """, CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal("g_default", pause.CallStack[0].FunctionName);
        Assert.Equal("Box.w default", pause.CallStack[1].FunctionName);
        Assert.Equal("(script)", pause.CallStack[2].FunctionName);
        Assert.Equal(2, pause.CallStack[2].Line);

        ScriptRunResult inside = Await(debug.EvaluateAsync("try; a = v; catch; a = 'absent'; end", 1, CancellationToken.None));
        Assert.True(inside.Success, inside.Message);
        Assert.Equal("absent", Value(inside, "a"));

        ScriptRunResult script = Await(debug.EvaluateAsync("c = v;", 2, CancellationToken.None));
        Assert.True(script.Success, script.Message);
        Assert.Equal("script-v", Value(script, "c"));

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Value(await session.ExecuteAsync("x = w;", "", CancellationToken.None), "x"));
    }
}
