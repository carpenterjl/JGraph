using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M145, step 4: one resolver decides what a name means, every site asks it, the file the running
/// code came from is interpreter state of its own, and <c>@name</c> is a handle that carries what it
/// captured. The order is still the one the interpreter always had — these tests pin that nothing a
/// script computes changed except what the plan lists: <c>feval</c> by name reaching a path file, a
/// handle reporting through what it captured, and the workspace a function sees as its caller when
/// it was called from an anonymous body, from <c>evalin</c>, or from a class property default.
/// </summary>
[Collection("JG facade")]
public class JgsNameResolverTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m145-resolver-" + Guid.NewGuid().ToString("N"));

    private readonly string _library;

    public JgsNameResolverTests()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(_library);

        File.WriteAllText(Path.Combine(_folder, "beside_me.m"), """
            function y = beside_me(x)
                y = x + 1;
            end
            """);

        File.WriteAllText(Path.Combine(_library, "far_away.m"), """
            function [s, d] = far_away(a, b)
                s = a + b;
                d = a - b;
            end
            """);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private static T Await<T>(Task<T> task)
    {
        Assert.True(task.Wait(Timeout), "Timed out.");
        return task.Result;
    }

    private void WriteFile(string name, string source) => File.WriteAllText(Path.Combine(_folder, name), source);

    private string Escaped(string path) => path.Replace("\\", "\\\\");

    // --- Invoke mode: a bound value keeps its meaning, a function definition is called ------------

    /// <summary>
    /// Phase one of Invoke mode runs before any argument is evaluated, which is what lets a bound
    /// array be indexed with <c>end</c> and <c>:</c> — those need the value in hand. A global array
    /// is bound the same way, through the declaration that redirects the name.
    /// </summary>
    [Fact]
    public async Task ABoundValue_IsIndexed_Applied_OrCalled_ByWhatItIs()
    {
        ScriptRunResult result = await RunMatlab("""
            A = [1 2 3; 4 5 6];
            e = A(end);
            col = A(:, 2);
            C = {1, 2, 3};
            flat = C(:);
            T = table([1; 2; 3], [4; 5; 6], 'VariableNames', {'a', 'b'});
            sub = T(2:3, :);
            n = height(sub);
            global G
            G = [7 8 9];
            ge = G(end);
            gc = numel(G(:));
            F = griddedInterpolant([1 2 3], [10 20 30]);
            fi = F(2.5);
            m = containers.Map({'k'}, {42});
            mk = m('k');
            h = @sin;
            hs = h(0);
            assert(isequal(col, [2; 5]) && isequal(size(flat), [3 1]));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(6.0, Value(result, "e"));
        Assert.Equal(2.0, Value(result, "n"));
        Assert.Equal(9.0, Value(result, "ge"));
        Assert.Equal(3.0, Value(result, "gc"));
        Assert.Equal(25.0, Value(result, "fi"));
        Assert.Equal(42.0, Value(result, "mk"));
        Assert.Equal(0.0, Value(result, "hs"));
    }

    /// <summary>
    /// A global handle under a built-in's name is the variable at every site a call can be written
    /// at: an expression, a discarded statement (which binds <c>ans</c>), and a multi-output call
    /// whose output count reaches the handle.
    /// </summary>
    [Fact]
    public async Task AGlobalHandle_UnderABuiltinName_IsCalled_AtEverySite()
    {
        ScriptRunResult result = await RunMatlab("""
            global size
            size = @(x) max([x * 10, x * 100, x]);
            v = size(1);
            size(2);
            a = ans;
            [p, q] = size(3);
            r = inner();
            function r = inner()
                global size
                r = size(4);
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(100.0, Value(result, "v"));
        Assert.Equal(200.0, Value(result, "a"));
        Assert.Equal(300.0, Value(result, "p"));
        Assert.Equal(2.0, Value(result, "q"));
        Assert.Equal(400.0, Value(result, "r"));
    }

    /// <summary>The callee is resolved first and the arguments once, on every road a call can take.</summary>
    [Fact]
    public async Task ASideEffectingArgument_IsEvaluatedOnce()
    {
        ScriptRunResult result = await RunMatlab("""
            y = abs(tick());
            z = tick();
            h = @abs;
            w = h(tick());
            u = tick();
            C = {@abs};
            q = C{1}(tick());
            [mx, ix] = max([tick() 0]);
            last = tick();
            function n = tick()
                persistent k
                if isempty(k), k = 0; end
                k = k + 1;
                n = k;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Value(result, "z"));
        Assert.Equal(4.0, Value(result, "u"));
        Assert.Equal(6.0, Value(result, "mx"));
        Assert.Equal(7.0, Value(result, "last"));
    }

    // --- Handles ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>@name</c> is a named handle: it calls what it captured and hands the wanted output count
    /// through, from a written call, through a cell, and from <c>cellfun</c>; an anonymous function
    /// captures it like any value; and two handles to the same thing are equal by structure, since
    /// each <c>@max</c> is a fresh value now.
    /// </summary>
    [Fact]
    public async Task ANamedHandle_CallsItsTarget_PassesTheOutputCount_AndComparesByStructure()
    {
        ScriptRunResult result = await RunMatlab("""
            m = @max;
            [v, i] = m([3 9 4]);
            c = {@max};
            [cv, ci] = c{1}([3 9 4]);
            [mm, ii] = cellfun(@max, {[3 9 4], [1 2]});
            k = @max;
            g = @(x) k(x) + 1;
            gg = g([1 5]);
            same = isequal(@max, @max);
            different = isequal(@max, @min);
            held = isequal(k, @max);
            local = isequal(@tick, @tick);
            viaText = isequal(str2func('max'), @max);
            assert(isequal(mm, [9 2]) && isequal(ii, [2 2]));
            function n = tick()
                n = 1;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(9.0, Value(result, "v"));
        Assert.Equal(2.0, Value(result, "i"));
        Assert.Equal(9.0, Value(result, "cv"));
        Assert.Equal(2.0, Value(result, "ci"));
        Assert.Equal(6.0, Value(result, "gg"));
        Assert.Equal(true, Value(result, "same"));
        Assert.Equal(false, Value(result, "different"));
        Assert.Equal(true, Value(result, "held"));
        Assert.Equal(true, Value(result, "local"));
        Assert.Equal(true, Value(result, "viaText"));
    }

    /// <summary>
    /// <c>nargin</c> and <c>functions</c> answer through what the handle captured: a built-in's
    /// catalog entry, a file's own header, and — new — the file the function actually came from
    /// rather than the run's main script.
    /// </summary>
    [Fact]
    public async Task Nargin_AndFunctions_ReportThroughWhatTheHandleCaptured()
    {
        ScriptRunResult result = await RunMatlab("""
            n1 = nargin(@sin);
            nb = nargin(@beside_me);
            nn = nargin('beside_me');
            s = functions(@beside_me);
            simple = strcmp(s.type, 'simple');
            file = s.file;
            b = functions(@cos);
            builtin = strcmp(b.type, 'builtin') && isempty(b.file);
            printed = strcmp(func2str(@beside_me), '@beside_me');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Value(result, "n1"));
        Assert.Equal(1.0, Value(result, "nb"));
        Assert.Equal(1.0, Value(result, "nn"));
        Assert.Equal(true, Value(result, "simple"));
        Assert.Equal(true, Value(result, "builtin"));
        Assert.Equal(true, Value(result, "printed"));
        Assert.Equal(Path.Combine(_folder, "beside_me.m"), Value(result, "file"));
    }

    /// <summary>A name handed to <c>feval</c> resolves as the same name written as a call would — a path file included.</summary>
    [Fact]
    public async Task Feval_ByName_ReachesAPathFunction()
    {
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            a = feval('beside_me', 2);
            [s, d] = feval('far_away', 5, 2);
            inner = feval('helper', 4);
            function y = helper(x)
                y = x * 2;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(3.0, Value(result, "a"));
        Assert.Equal(7.0, Value(result, "s"));
        Assert.Equal(3.0, Value(result, "d"));
        Assert.Equal(8.0, Value(result, "inner"));
    }

    // --- Who a function's caller is -------------------------------------------------------------

    [Fact]
    public async Task EvalinCaller_FromAPathFunction_ReadsTheCallersWorkspace()
    {
        WriteFile("caller_probe.m", """
            function y = caller_probe()
                secret = -1;
                y = evalin('caller', 'secret') + 1;
                assignin('caller', 'planted', 7);
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            secret = 41;
            y = caller_probe();
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(42.0, Value(result, "y"));
        Assert.Equal(7.0, Value(result, "planted"));
    }

    /// <summary>
    /// R2025b, measured in step 0: a function called from inside an anonymous function's body has
    /// the body's captured workspace as its caller. <c>evalin('caller', 'v')</c> reads the captured
    /// <c>v</c>, <c>assignin('caller', …)</c> of a new name refuses with MATLAB's static-workspace
    /// words, and nothing lands in the invoker's workspace. Each side keeps its own <c>helper</c>:
    /// the called function resolves from its own file, the body from the file its handle was made in.
    /// </summary>
    [Fact]
    public async Task AFunctionCalledFromAnAnonymousBody_SeesTheClosureAsItsCaller()
    {
        WriteFile("maker.m", """
            function h = maker()
                v = 'maker-v';
                h = @(x) [g(x) '|' helper(x) '|' v];
            end
            function s = helper(x)
                s = 'maker-helper';
            end
            """);
        WriteFile("g.m", """
            function y = g(x)
                y = evalin('caller', 'v');
                try
                    assignin('caller', 'seen', 1);
                    y = [y '|added'];
                catch e
                    y = [y '|' e.message];
                end
                y = [y '|' helper(x)];
            end
            function s = helper(x)
                s = 'g-helper';
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            v = 'script-v';
            h = maker();
            out = h(1);
            expected = ['maker-v|Attempt to add "seen" to a static workspace.|g-helper|maker-helper|maker-v'];
            ok = strcmp(out, expected);
            gone = exist('seen');
            function s = helper(x)
                s = 'script-helper';
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(true, Value(result, "ok"));
        Assert.Equal(0.0, Value(result, "gone"));
    }

    /// <summary>
    /// A class property default runs in a private, writable workspace that is thrown away: a
    /// function it calls can <c>assignin('caller', …)</c> without error and nothing sees the result —
    /// step 0's measurement. (That the default's workspace shows no variable of the constructing
    /// frame is step 5's, once a file scope stops seeing the base workspace.)
    /// </summary>
    [Fact]
    public async Task AFunctionCalledFromAPropertyDefault_WritesIntoAWorkspaceNobodySees()
    {
        WriteFile("Box.m", """
            classdef Box
                properties
                    w = g_default(1)
                end
            end
            """);
        WriteFile("g_default.m", """
            function y = g_default(x)
                assignin('caller', 'seen', x);
                y = x + 1;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            b = Box();
            w = b.w;
            gone = exist('seen');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Value(result, "w"));
        Assert.Equal(0.0, Value(result, "gone"));
    }

    // --- The current file ---------------------------------------------------------------------------

    /// <summary>
    /// The file the running code came from follows every entry — a function, a script run by name,
    /// a function called from that script, <c>eval</c>'d text (which keeps its caller's file) — and
    /// is put back when an error thrown inside an anonymous function inside a script run by name
    /// unwinds through all of them.
    /// </summary>
    [Fact]
    public async Task TheCurrentFile_FollowsEveryEntry_AndUnwindsThroughAnError()
    {
        WriteFile("helper.m", """
            function y = helper(x)
                y = x + 1;
                y = y + 1;
            end
            """);
        WriteFile("by_name.m", """
            a = helper(1);
            b = a + 1;
            """);
        WriteFile("boom.m", """
            f = @(x) error('boom:inside', 'inside the handle');
            c = f(1);
            """);
        string main = Path.Combine(_folder, "main.m");
        string code = """
            x = helper(1);
            by_name;
            eval('q = 1;');
            try
                boom;
            catch e
                msg = e.message;
            end
            z = helper(2);
            """;
        File.WriteAllText(main, code);

        var probe = new Probe();
        await using JgsReplSession session = NewSession();
        ScriptRunResult result = await session.ExecuteFileAsync(code, main, probe, CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("inside the handle", Value(result, "msg"));

        // Every statement of every file ran with its own file current, including the statement
        // after the catch. (eval'd text runs outside the block executor, so the hook never sees it;
        // that it keeps the caller's file is what the evalin test below relies on.)
        Assert.All(probe.All, seen => Assert.True(
            seen.SourceId == seen.CurrentFile,
            $"line {seen.Line} of {Path.GetFileName(seen.SourceId)} ran under {Path.GetFileName(seen.CurrentFile)}"));
        Assert.Contains(probe.All, seen => seen.SourceId.EndsWith("helper.m", StringComparison.Ordinal));
        Assert.Contains(probe.All, seen => seen.SourceId.EndsWith("by_name.m", StringComparison.Ordinal));
        Assert.Contains(probe.All, seen => seen.SourceId.EndsWith("boom.m", StringComparison.Ordinal));
        // The last top-level statement, after the catch, ran under main again; the last statement
        // of all is helper's body, under helper.m; and nothing is current once the file has finished.
        Assert.Equal(main, probe.All.Last(seen => seen.CallDepth == 0).CurrentFile);
        Assert.EndsWith("helper.m", probe.All[^1].CurrentFile, StringComparison.Ordinal);
        Assert.Equal("", probe.Interpreter!.CurrentFile);
    }

    // --- The debugger's selected frame ------------------------------------------------------------

    /// <summary>
    /// The debugger's caller frame is the workspace the interpreter handed over at the call, not the
    /// preceding function's local: paused inside <c>g</c> reached through
    /// <c>evalin('caller', 'g()')</c> from <c>helper</c>, the frame below <c>g</c> is the workspace
    /// <c>evalin</c> named — the script's — and the prompt reads and writes it there.
    /// </summary>
    [Fact]
    public async Task TheDebuggerPrompt_OnTheCallerFrame_UsesTheWorkspaceTheCallWasMadeFrom()
    {
        var engine = new MatlabScriptEngine();
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());
        JgsDebugSession debug = Assert.IsAssignableFrom<IJgsDebuggable>(engine).CreateDebugSession();
        debug.Paused += (_, e) => _pauses.Add(e);
        debug.SetBreakpoints("main", new[] { 8 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            v = 1;
            helper();
            function helper()
                v = 2;
                evalin('caller', 'g()');
            end
            function g()
                x = 0;
            end
            """, CancellationToken.None);

        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? pause, Timeout), "Timed out waiting for a pause.");
        Assert.Equal("g", pause!.CallStack[0].FunctionName);

        ScriptRunResult caller = Await(debug.EvaluateAsync("w = v;", 1, CancellationToken.None));
        Assert.True(caller.Success, caller.Message);
        Assert.Equal(1.0, Value(caller, "w"));

        ScriptRunResult write = Await(debug.EvaluateAsync("v = 10;", 1, CancellationToken.None));
        Assert.True(write.Success, write.Message);

        debug.Continue();
        ScriptRunResult result = await run;
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(10.0, Value(await session.ExecuteAsync("again = v;", "", CancellationToken.None), "again"));
    }

    /// <summary>Reads the interpreter's current file before every statement.</summary>
    private sealed class Probe : IJgsDebugHook
    {
        public sealed record Seen(string SourceId, int Line, string CurrentFile, int CallDepth);

        public Interpreter? Interpreter { get; private set; }
        public List<Seen> All { get; } = new();

        public void RunStarting(Interpreter interpreter, JgsEnvironment globals) => Interpreter = interpreter;

        public int? BeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth)
        {
            Stmt statement = block.Statements[index];
            All.Add(new Seen(statement.SourceId, statement.Line, Interpreter!.CurrentFile, callDepth));
            return null;
        }

        public void EnterBlock(BlockExecution block) { }
        public void ExitBlock() { }
        public void EnterFunction(
            FnStmt declaration, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile) { }
        public void ExitFunction() { }
    }
}
