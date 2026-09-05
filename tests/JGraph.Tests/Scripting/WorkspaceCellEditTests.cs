using JGraph.Api;
using JGraph.Data;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Data Viewer's write path: a cell edit becomes the statement that performs it, in the
/// workspace's own language, and running that statement changes the variable. Black-box through
/// the <see cref="IWorkspaceCellEditor"/> capability of a session and the paused debugger's twin.
/// </summary>
[Collection("JG facade")]
public class WorkspaceCellEditTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public WorkspaceCellEditTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, _) => { });

    private IScriptSession NewSession(IScriptEngine engine) =>
        Assert.IsAssignableFrom<IScriptRepl>(engine).CreateSession(Context());

    private static async Task<ScriptVariable> Define(IScriptSession session, string code, string name)
    {
        ScriptRunResult result = await session.ExecuteAsync(code, "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return Assert.Single(result.Variables, v => v.Name == name);
    }

    private static string Compose(IScriptSession session, ScriptVariable variable, int row, int column, string text)
    {
        string? statement = Assert.IsAssignableFrom<IWorkspaceCellEditor>(session)
            .ComposeCellAssignment(variable, row, column, text);
        Assert.NotNull(statement);
        return statement!;
    }

    [Fact]
    public async Task MatlabVector_IsWrittenOneBased_AndTheIndexColumnIsNot()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        ScriptVariable v = await Define(session, "v = [1 2 3];", "v");
        Assert.IsType<double[]>(v.RawValue);

        Assert.Equal("v(2) = 9;", Compose(session, v, 1, 1, " 9 "));
        Assert.Null(((IWorkspaceCellEditor)session).ComposeCellAssignment(v, 1, 0, "9"));
        Assert.Null(((IWorkspaceCellEditor)session).ComposeCellAssignment(v, 5, 1, "9"));
        Assert.Null(((IWorkspaceCellEditor)session).ComposeCellAssignment(v, 1, 1, "   "));

        ScriptRunResult written = await session.ExecuteAsync("v(2) = 9;", "", CancellationToken.None);
        Assert.Equal(new[] { 1.0, 9.0, 3.0 }, Assert.IsType<double[]>(Assert.Single(written.Variables, x => x.Name == "v").RawValue));
    }

    [Fact]
    public async Task JgsVector_IsWrittenInTheDialectsIndexBase()
    {
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        ScriptVariable v = await Define(session, "let v = [1, 2, 3]", "v");
        Assert.Equal("v(1) = 9;", Compose(session, v, 1, 1, "9"));

        ScriptRunResult written = await session.ExecuteAsync("v(1) = 9;", "", CancellationToken.None);
        Assert.True(written.Success, written.Message);
        Assert.Equal(new[] { 1.0, 9.0, 3.0 }, Assert.IsType<double[]>(Assert.Single(written.Variables, x => x.Name == "v").RawValue));
    }

    [Fact]
    public async Task Matrix_TakesTwoSubscripts_AndAnExpression()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        ScriptVariable m = await Define(session, "m = [1 2; 3 4];", "m");
        Assert.Equal("matrix", Assert.IsType<ScriptValueGrid>(m.RawValue).Kind);

        Assert.Equal("m(2, 1) = pi * 2;", Compose(session, m, 1, 0, "pi * 2"));
        ScriptRunResult written = await session.ExecuteAsync("m(2, 1) = pi * 2; disp(m(2,1))", "", CancellationToken.None);
        Assert.True(written.Success, written.Message);
        Assert.StartsWith("6.28", _output.NormalLines[^1].Trim());
    }

    [Fact]
    public async Task Cell_BracesIn_AndStructWritesTheField_InMatlabOnly()
    {
        await using IScriptSession matlab = NewSession(new MatlabScriptEngine());
        ScriptVariable c = await Define(matlab, "c = {1, 'a'; 2, 'b'};", "c");
        Assert.Equal("c{1, 2} = 'z';", Compose(matlab, c, 0, 1, "'z'"));

        ScriptVariable s = await Define(matlab, "s = struct('a', 1, 'b', 'x');", "s");
        ScriptValueGrid grid = Assert.IsType<ScriptValueGrid>(s.RawValue);
        Assert.Equal("struct", grid.Kind);
        Assert.Equal("s.b = 'q';", Compose(matlab, s, 1, 2, "'q'"));
        Assert.Null(((IWorkspaceCellEditor)matlab).ComposeCellAssignment(s, 1, 0, "renamed")); // the Field column
        Assert.Null(((IWorkspaceCellEditor)matlab).ComposeCellAssignment(s, 1, 1, "double"));  // the Type column

        ScriptRunResult written = await matlab.ExecuteAsync("c{1, 2} = 'z'; s.b = 'q'; disp(c{1,2}); disp(s.b)", "", CancellationToken.None);
        Assert.True(written.Success, written.Message);
        Assert.Equal(new[] { "z", "q" }, _output.NormalLines.TakeLast(2).Select(l => l.Trim()));

        // JGS has no brace or dot syntax, so its cells and structs stay read-only.
        await using IScriptSession jgs = NewSession(new JgsScriptEngine());
        ScriptVariable jc = await Define(jgs, "let jc = cell(2, 2)", "jc");
        Assert.Null(((IWorkspaceCellEditor)jgs).ComposeCellAssignment(jc, 0, 0, "5"));
    }

    [Fact]
    public async Task Table_WritesTheVariableOfTheColumn()
    {
        await using IScriptSession session = NewSession(new MatlabScriptEngine());
        ScriptVariable t = await Define(session, "t = table([1;2],{'x';'y'},'VariableNames',{'A','Name'});", "t");
        Assert.IsType<Table>(t.RawValue);

        Assert.Equal("t.A(2) = 20;", Compose(session, t, 1, 0, "20"));
        Assert.Equal("t.Name(1) = 'p';", Compose(session, t, 0, 1, "'p'"));
        Assert.Null(((IWorkspaceCellEditor)session).ComposeCellAssignment(t, 2, 0, "1"));

        ScriptRunResult written = await session.ExecuteAsync("t.A(2) = 20; t.Name(1) = 'p'; disp(t.A(2)); disp(t.Name{1})", "", CancellationToken.None);
        Assert.True(written.Success, written.Message);
        Assert.Equal(new[] { "20", "p" }, _output.NormalLines.TakeLast(2).Select(l => l.Trim()));
    }

    [Fact]
    public async Task ThePausedDebugger_ComposesTheSameStatements_ForItsFrame()
    {
        var engine = new MatlabScriptEngine();
        await using IScriptSession session = NewSession(engine);
        JgsDebugSession debug = engine.CreateDebugSession();
        var pauses = new System.Collections.Concurrent.BlockingCollection<JgsPausedEventArgs>();
        debug.Paused += (_, e) => pauses.Add(e);
        debug.SetBreakpoints("main.m", new[] { 2 });
        Task<ScriptRunResult> run = debug.RunAsync(session, "main.m", """
            m = [1 2; 3 4];
            disp(m(1, 2))
            """, CancellationToken.None);
        Assert.True(pauses.TryTake(out _, TimeSpan.FromSeconds(10)));

        ScriptVariable m = Assert.Single(debug.GetVariables(), v => v.Name == "m");
        string? statement = debug.ComposeCellAssignment(m, 0, 1, "42");
        Assert.Equal("m(1, 2) = 42;", statement);

        ScriptRunResult edited = await debug.EvaluateAsync(statement!, 0, CancellationToken.None);
        Assert.True(edited.Success, edited.Message);
        debug.Continue();
        ScriptRunResult finished = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(finished.Success, finished.Message);
        Assert.Equal("42", _output.NormalLines[^1].Trim());
    }
}
