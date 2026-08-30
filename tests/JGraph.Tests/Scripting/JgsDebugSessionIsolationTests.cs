using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M108: setting a breakpoint must not change what a script means.
/// </summary>
/// <remarks>
/// <para>
/// A run with a breakpoint anywhere goes under <see cref="JgsDebugSession"/>, which drives the
/// one-shot runner — and that runner opened by resetting the <c>JG</c> facade and clearing the handle
/// registry, both of which are process-wide and both of which the live console session was still
/// using. Worse than losing them: <c>JgsHandleRegistry.Clear</c> rewinds the handle counter, so the
/// first object the debug run drew was handed the number the session's first object already had.
/// Every handle in the console workspace silently came to mean a different object.
/// </para>
/// <para>
/// These tests are written as the user meets the bug: a session that runs a script, is asked a
/// question about a handle, has a debug run happen in the middle of its life, and is asked the same
/// question again. The answer has to be the same one.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class JgsDebugSessionIsolationTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public JgsDebugSessionIsolationTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context => new(_output, static (_, _) => { });

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(Context);

    private async Task RunAsserting(IScriptSession session, string code)
    {
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>Runs one script under the debugger, pausing at <paramref name="breakpoint"/> and continuing.</summary>
    private async Task<ScriptRunResult> RunDebugged(string code, int breakpoint)
    {
        JgsDebugSession session = new MatlabScriptEngine().CreateDebugSession();
        session.SetBreakpoints("debug.m", new[] { breakpoint });

        int pauses = 0;
        session.Paused += (_, _) =>
        {
            pauses++;
            session.Continue();
        };

        ScriptRunResult result = await session.RunAsync("debug.m", code, Context, CancellationToken.None);
        Assert.True(pauses > 0, "the breakpoint was never reached, so this proves nothing.");
        return result;
    }

    [Fact]
    public async Task ADebugRunLeavesTheConsolesHandlesMeaningWhatTheyMeant()
    {
        await using IScriptSession session = NewSession();
        await RunAsserting(session, """
            [X, Y] = meshgrid(1:20, 1:20);
            hSurf = surf(X, Y, X .* Y);
            set(hSurf, 'FaceAlpha', 0.5);
            """);

        // A debug run of an unrelated script, drawing an object of an entirely different type. Before
        // the fix its line took handle 1000000.5 — the number hSurf already held — and the console's
        // surface became this line.
        Assert.True((await RunDebugged("""
            f = figure;
            h = plot(1:10, (1:10) .^ 2);
            """, breakpoint: 1)).Success);

        await RunAsserting(session, """
            disp(size(get(hSurf, 'ZData')));
            disp(get(hSurf, 'FaceAlpha'));
            """);

        // 20-by-20 and the alpha this session set: the surface, not the debug run's line. Before the
        // fix the first line answered [1 0] and the second threw "a line has no property FaceAlpha".
        Assert.Equal(new[] { "[20, 20]", "0.5" }, _output.NormalLines);
    }

    [Fact]
    public async Task ADebugRunDoesNotCloseTheConsolesFigures()
    {
        await using IScriptSession session = NewSession();
        await RunAsserting(session, "figure(7); plot(1:3);");

        Assert.True((await RunDebugged("x = 1 + 1;\ny = x * 2;", breakpoint: 2)).Success);

        await RunAsserting(session, "disp(ishandle(7)); disp(numel(findobj(7, 'Type', 'line')));");
        Assert.Equal(new[] { "true", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task AFailingDebugRunStillShowsWhatItDrew()
    {
        // The other half of the same complaint: a script that drew and then failed showed its figure
        // on an ordinary run and nothing at all under the debugger, because the one-shot runner's
        // error path never asked the figures to be shown.
        var shown = new List<int>();
        JgsDebugSession session = new MatlabScriptEngine().CreateDebugSession();
        session.SetBreakpoints("debug.m", new[] { 1 });
        session.Paused += (_, _) => session.Continue();

        ScriptRunResult result = await session.RunAsync(
            "debug.m",
            """
            surf(peaks(10));
            title('drawn');
            error('and then it failed');
            """,
            new ScriptContext(_output, (number, _) => shown.Add(number)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(new[] { 1 }, shown);
    }

    [Fact]
    public async Task ADebuggedRunAndAnOrdinaryRunOfTheSameScriptAgree()
    {
        // The general statement of the property, rather than one instance of it: whatever the script
        // says, it says the same thing with a breakpoint set as without one.
        const string Code = """
            [X, Y] = meshgrid(1:6, 1:6);
            h = surf(X, Y, X .* Y);
            set(h, 'ZData', X + Y);
            disp(mat2str(size(get(h, 'ZData'))));
            disp(max(max(get(h, 'ZData'))));
            disp(class(h));
            """;

        await using (IScriptSession plain = NewSession())
        {
            await RunAsserting(plain, Code);
        }

        string[] ordinary = [.. _output.NormalLines];
        _output.Normal.Clear(); // Clear() is the clc counter, not the buffer

        Assert.True((await RunDebugged(Code, breakpoint: 2)).Success);
        Assert.Equal(ordinary, _output.NormalLines);
    }
}
