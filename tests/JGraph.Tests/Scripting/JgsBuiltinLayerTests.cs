using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M145, step 2: every built-in lives in the sealed layer under the base workspace, and nothing
/// the user can do writes there. What this pins is the boundary — a rebound name is a workspace
/// variable hiding the built-in, <c>clear</c> drops the variable and the built-in shows through,
/// and the debugger's script frame is the base workspace by identity rather than by walking to the
/// outermost scope, which is now the layer.
/// </summary>
[Collection("JG facade")]
public class JgsBuiltinLayerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m145-" + Guid.NewGuid().ToString("N"));

    public JgsBuiltinLayerTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private static Task<ScriptRunResult> Exec(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private static JgsValue Function(string name) =>
        JgsValue.Function(new BuiltinFunction(name, static (_, _, _) => JgsValue.Number(1)));

    // Names from every registrar: CreateGlobals (max, pi), the interpreter (plus), run, the eval
    // family (which), the session builtins, the path (addpath), save/load, and the workspace
    // builtins with their table wrapper.
    private static readonly string[] Registered =
        { "max", "pi", "plus", "run", "which", "addpath", "save", "load", "clear", "clearvars", "whos", "table", "array2table" };

    private static void AssertLayerHoldsEveryBuiltin(JgsEnvironment workspace)
    {
        Assert.True(workspace.Builtins.IsSealed);
        Assert.Same(workspace, workspace.Base);
        foreach (string name in Registered)
        {
            Assert.True(workspace.TryGet(name, out JgsValue resolved), name);
            Assert.True(workspace.Builtins.TryGet(name, out JgsValue entry), name);
            Assert.Same(entry, resolved);
            Assert.False(workspace.Locals.ContainsKey(name), $"{name} was declared in the workspace");
        }

        Assert.DoesNotContain(workspace.Locals.Values, v => v.Type == JgsType.Function && v.AsCallable is BuiltinFunction);
    }

    [Fact]
    public async Task InBatch_EveryRegistrarWritesToTheLayer_AndTheWorkspaceResolvesItsEntry()
    {
        ScriptRunResult result = await RunMatlab("m = 1;");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        JgsEnvironment workspace = Assert.IsType<JgsEnvironment>(JgsRunner.LastCompletedRun);
        AssertLayerHoldsEveryBuiltin(workspace);
        Assert.True(workspace.Locals.ContainsKey("m"));
    }

    [Fact]
    public async Task InASession_EveryRegistrarWritesToTheLayer_AndTheWorkspaceResolvesItsEntry()
    {
        await using JgsReplSession session = NewSession();

        Assert.True((await Exec(session, "m = 1;")).Success);

        AssertLayerHoldsEveryBuiltin(session.Workspace);
        Assert.True(session.Workspace.Locals.ContainsKey("m"));
    }

    [Fact]
    public async Task Plus_SurvivesClear_AndClearAll_AcrossStatements()
    {
        await using JgsReplSession session = NewSession();

        Assert.Equal(3.0, Value(await Exec(session, "a = plus(1, 2);"), "a"));
        Assert.True((await Exec(session, "clear")).Success, _output.ErrorText);
        Assert.Equal(4.0, Value(await Exec(session, "b = plus(2, 2);"), "b"));
        Assert.True((await Exec(session, "clear all")).Success, _output.ErrorText);
        Assert.Equal(6.0, Value(await Exec(session, "c = plus(3, 3);"), "c"));
        Assert.Equal(9.0, Value(await Exec(session, "d = max([1 9 2]);"), "d"));
    }

    [Fact]
    public async Task AssigningABuiltinName_HidesIt_AndClearingTheNameShowsItAgain()
    {
        ScriptRunResult result = await RunMatlab("""
            max = 7;
            a = max;
            clear max
            b = max([1 5 3]);
            pi = 3;
            c = pi;
            clear pi
            d = pi;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(7.0, Value(result, "a"));
        Assert.Equal(5.0, Value(result, "b"));
        Assert.Equal(3.0, Value(result, "c"));
        Assert.Equal(Math.PI, Value(result, "d"));
        Assert.DoesNotContain(result.Variables, v => v.Name is "max" or "pi");
    }

    [Fact]
    public async Task TheWorkspaceListing_NeverShowsABuiltin_AndDropsARebindingWhenCleared()
    {
        await using JgsReplSession session = NewSession();

        Assert.True((await Exec(session, "max = 7;")).Success);
        Assert.Contains(session.GetVariables(), v => v.Name == "max");
        Assert.DoesNotContain(session.GetVariables(), v => v.Name is "plus" or "pi" or "sin");

        Assert.True((await Exec(session, "clear max")).Success);
        Assert.DoesNotContain(session.GetVariables(), v => v.Name == "max");
        Assert.Equal(5.0, Value(await Exec(session, "m = max([1 5 3]);"), "m"));
    }

    [Fact]
    public async Task AGlobalNamedLikeABuiltin_IsTheVariable_InEveryScope()
    {
        ScriptRunResult result = await RunMatlab("""
            global size
            size = [4 5 6];
            s = size(2);
            n = numel(size);
            r = readit();
            function r = readit()
                global size
                r = size(3);
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(5.0, Value(result, "s"));
        Assert.Equal(3.0, Value(result, "n"));
        Assert.Equal(6.0, Value(result, "r"));
    }

    [Fact]
    public async Task PausedInAFunction_TheScriptFrameIsTheBaseWorkspace_AndABuiltinStillResolvesThere()
    {
        var engine = new MatlabScriptEngine();
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());
        Assert.True((await Exec(session, "base = 5;")).Success);

        JgsDebugSession debug = Assert.IsAssignableFrom<IJgsDebuggable>(engine).CreateDebugSession();
        debug.Paused += (_, e) => _pauses.Add(e);
        debug.SetBreakpoints("main", new[] { 5 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main", """
            r = helper(base);
            disp(r)
            disp(base)
            function r = helper(n)
                r = n + 1;
            end
            """, CancellationToken.None);

        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? pause, Timeout), "Timed out waiting for a pause.");
        Assert.Equal("helper", pause!.CallStack[0].FunctionName);

        // Frame 1 is the script: the base workspace, found by identity and not by walking outward.
        IReadOnlyList<ScriptVariable> script = debug.GetVariables(1);
        Assert.Equal(5.0, Assert.Single(script, v => v.Name == "base").RawValue);
        Assert.DoesNotContain(script, v => v.Name is "n" or "pi" or "max");

        ScriptRunResult read = Await(debug.EvaluateAsync("m = max([1 9 2]);", 1, CancellationToken.None));
        Assert.True(read.Success, read.Message);
        Assert.Equal(9.0, Value(read, "m"));

        ScriptRunResult write = Await(debug.EvaluateAsync("base = 100;", 1, CancellationToken.None));
        Assert.True(write.Success, write.Message);

        debug.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("100", _output.NormalText);

        // The prompt assignment landed in the session's workspace, not in built-in storage.
        Assert.Equal(100.0, Value(await Exec(session, "again = base;"), "again"));
        Assert.Equal(9.0, Value(await Exec(session, "m2 = max([1 9 2]);"), "m2"));
    }

    [Fact]
    public void TheLayer_IsABoundary_AndRefusesDeclarationsOnceSealed()
    {
        var layer = new JgsBuiltinLayer();
        layer.Register("f", Function("f"));
        layer.RegisterConstant("k", JgsValue.Number(2));
        layer.Seal();

        Assert.Throws<InvalidOperationException>(() => layer.Register("g", Function("g")));
        Assert.Throws<InvalidOperationException>(() => layer.Root.Declare("x", JgsValue.Number(1)));
        Assert.Throws<InvalidOperationException>(() => layer.Root.DeclareFunction("x", Function("x")));
        Assert.Throws<InvalidOperationException>(() => layer.Root.DeclareGlobal("f"));
        Assert.Throws<InvalidOperationException>(() => layer.Root.Forget("f", new Dictionary<string, JgsValue>()));

        // An assignment walking outward stops short of the layer, so the caller declares in its own scope.
        JgsEnvironment workspace = layer.Base;
        Assert.False(workspace.TryAssign("f", JgsValue.Number(7)));
        Assert.False(workspace.TryAssign("k", JgsValue.Number(7)));
        workspace.Declare("f", JgsValue.Number(7));
        Assert.True(workspace.TryGet("f", out JgsValue hidden));
        Assert.Equal(7.0, hidden.AsNumber);

        // Dropping the variable shows the built-in again, with no snapshot to restore it from.
        workspace.Forget("f", new Dictionary<string, JgsValue>());
        Assert.True(workspace.TryGet("f", out JgsValue shown));
        Assert.True(layer.TryGet("f", out JgsValue entry));
        Assert.Same(entry, shown);
        Assert.True(workspace.IsFunctionBinding("f"));

        // Every scope under the layer knows the base by identity.
        var frame = new JgsEnvironment(new JgsEnvironment(workspace)) { IsCallBoundary = true };
        Assert.Same(workspace, frame.Base);
        Assert.Same(layer, frame.Builtins);
        Assert.False(frame.IsGlobal("f"));
        Assert.Throws<InvalidOperationException>(() => new JgsEnvironment().Builtins);
    }

    [Fact]
    public void TheEditorsNameList_StillComesFromTheLayer()
    {
        IReadOnlyCollection<string> names = JgsScriptEngine.BuiltinNames();

        Assert.Contains("sin", names);
        Assert.Contains("pi", names);
        Assert.Contains("run", names);
        Assert.Contains("plus", names);
    }

    private static ScriptRunResult Await(Task<ScriptRunResult> task)
    {
        Assert.True(task.Wait(Timeout), "Timed out waiting for the run to finish.");
        return task.Result;
    }
}
