using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V11 of the value-ownership plan (ADR 0172): the debugger follows the code's dialect. A pause
/// inside a <c>.m</c> function reached from JGS evaluates its prompt, composes its cell edits and
/// parses its live edits as MATLAB, and an outer JGS frame selected from there is JGS again; the
/// converse holds for a JGS function called from a <c>.m</c> script. Not headless-probeable, so
/// black-box through <see cref="JgsDebugSession"/>, driven lock-step like <see cref="JgsDebugSessionTests"/>.
/// </summary>
[Collection("JG facade")]
public class JgsDebugDialectM172Tests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-v11-debug-" + Guid.NewGuid().ToString("N"));

    public JgsDebugDialectM172Tests()
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

    private string WriteFile(string name, string source)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, source);
        return path;
    }

    private JgsDebugSession CreateSession()
    {
        JgsDebugSession session = new JgsScriptEngine().CreateDebugSession();
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

    private static ScriptRunResult Eval(JgsDebugSession session, string code, int frame) =>
        Await(session.EvaluateAsync(code, frame, CancellationToken.None));

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void PausedInAMatlabFunctionReachedFromJgs_ThePromptTheCellEditAndTheLiveEditAreMatlab_AndTheOuterFrameIsJgs()
    {
        string mfirst = WriteFile("mfirst.m", "function y = mfirst(x)\ny = x(1);\nend\n");
        string main = Path.Combine(_folder, "main.jgs");
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints(mfirst, new[] { 2 });

        Task<ScriptRunResult> run = session.RunAsync(main, """
            let a = [1, 2, 3]
            let r = mfirst(a)
            print(r)
            """, Context(), CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.True(SamePath(mfirst, pause.Location.SourceId), pause.Location.SourceId);
        Assert.Equal(2, pause.CallStack.Count);
        Assert.Equal("mfirst", pause.CallStack[0].FunctionName);

        // The prompt in the .m frame is MATLAB: 1-based, and JGS's `let` is not a statement there.
        Assert.Equal(1.0, Number(Eval(session, "probe = x(1);", 0), "probe"));
        Assert.False(Eval(session, "let z = 1", 0).Success);

        // The outer frame is the JGS script: 0-based, `let` declares.
        Assert.Equal(1.0, Number(Eval(session, "let probe1 = a[0]", 1), "probe1"));
        Assert.Equal(3.0, Number(Eval(session, "let probe2 = length(a)", 1), "probe2"));

        // A cell edit composes in the frame's dialect: x(2) in the .m frame, a(1) in the JGS one.
        ScriptVariable x = Assert.Single(session.GetVariables(0), v => v.Name == "x");
        Assert.Equal("x(2) = 9;", session.ComposeCellAssignment(x, 1, 1, "9", 0));
        ScriptVariable a = Assert.Single(session.GetVariables(1), v => v.Name == "a");
        Assert.Equal("a(1) = 9;", session.ComposeCellAssignment(a, 1, 1, "9", 1));

        // The live edit of the .m file parses as MATLAB, whatever the run started as.
        LiveEditResult edit = session.TryApplyEdit(mfirst, "function y = mfirst(x)\ny = x(1) + 100;\nend\n");
        Assert.True(edit.Applied, edit.Message);

        session.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("101", _output.NormalLines[^1].Trim());
    }

    [Fact]
    public void PausedInAJgsFunctionCalledFromAMatlabScript_ThePromptIsJgs_AndTheScriptFrameIsMatlab()
    {
        string caller = WriteFile("caller.m", "r = f([10 20 30]);\nfprintf('%g\\n', r);\n");
        string main = Path.Combine(_folder, "main.jgs");
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints(main, new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync(main, """
            fn f(x) {
                let y = x[0]
                return y
            }
            run("caller.m")
            """, Context(), CancellationToken.None);

        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(3, pause.Location.Line);
        Assert.Equal("f", pause.CallStack[0].FunctionName);

        // The JGS frame: 0-based, `let` declares. The caller's frame is the .m script's, which runs
        // in the base workspace as MATLAB: a space-separated row and `numel` parse and run there.
        Assert.Equal(10.0, Number(Eval(session, "let probe = x[0]", 0), "probe"));
        Assert.Equal(3.0, Number(Eval(session, "probe2 = numel([1 2 3]);", 1), "probe2"));
        Assert.False(Eval(session, "let z = 1", 1).Success);

        // A live edit of the .m script parses as MATLAB and takes effect on resume.
        LiveEditResult edit = session.TryApplyEdit(caller, "r = f([10 20 30]);\nfprintf('%g\\n', r + 1);\n");
        Assert.True(edit.Applied, edit.Message);

        session.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("11", _output.NormalLines[^1].Trim());
    }

    [Fact]
    public void ADebuggedJgsRunLeavesItsWorkspaceJgsAfterAMatlabCallReturns()
    {
        WriteFile("mfirst.m", "function y = mfirst(x)\ny = x(1);\nend\n");
        string main = Path.Combine(_folder, "main.jgs");
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints(main, new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync(main, """
            let a = [1, 2, 3]
            let r = mfirst(a)
            print(r)
            """, Context(), CancellationToken.None);

        NextPause();
        Assert.Equal(1.0, Number(Eval(session, "let probe = find([0, 1, 1])[0]", 0), "probe")); // JGS: 0-based find
        session.Continue();
        Assert.True(Await(run).Success);
    }
}
