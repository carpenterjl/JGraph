using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The M15 debugger UX, black-box through the public <see cref="JgsDebugSession"/>: set-next-statement
/// (moving the execution point within the paused block) and live code edits while paused (tail swaps,
/// loop bodies, function refresh and hoisting, and the incompatibility rules). Driven lock-step with
/// timeouts, like <see cref="JgsDebugSessionTests"/>.
/// </summary>
[Collection("JG facade")]
public class JgsDebuggerUxTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();

    public JgsDebuggerUxTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

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

    // --- Set next statement -----------------------------------------------------------------------

    [Fact]
    public void SetNext_Forward_SkipsStatements_AndPausesAtTheTarget()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 0
            b = 5
            b = b + 1
            print(b)
            """, Context(), CancellationToken.None);

        Assert.Equal(3, NextPause().Location.Line);
        Assert.True(session.TrySetNextStatement("main", 4, out string? error), error);

        JgsPausedEventArgs jumped = NextPause();
        Assert.Equal(PauseReason.EntryJump, jumped.Reason);
        Assert.Equal(4, jumped.Location.Line);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("1", _output.NormalText);          // b = 5 never ran: 0 + 1
        Assert.DoesNotContain("6", _output.NormalText);
    }

    [Fact]
    public void SetNext_Backwards_RunsTheStatementAgain()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let total = 0
            total = total + 1
            let done = 1
            print(total)
            """, Context(), CancellationToken.None);

        Assert.Equal(3, NextPause().Location.Line);
        Assert.True(session.TrySetNextStatement("main", 2, out string? error), error);
        Assert.Equal(2, NextPause().Location.Line);

        session.SetBreakpoints("main", Array.Empty<int>()); // don't re-hit line 3 on the second pass
        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("2", _output.NormalText);          // line 2 executed twice
    }

    [Fact]
    public void SetNext_IntoANestedBlock_IsRejected()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 1 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let x = 1
            if x > 0 {
                let y = 2
            }
            print(x)
            """, Context(), CancellationToken.None);

        NextPause();
        Assert.False(session.TrySetNextStatement("main", 3, out string? error));
        Assert.Contains("block", error, StringComparison.OrdinalIgnoreCase);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void SetNext_FromAnotherFile_OrWhenNotPaused_IsRejected()
    {
        JgsDebugSession session = CreateSession();
        Assert.False(session.TrySetNextStatement("main", 1, out string? notPaused));
        Assert.Contains("not paused", notPaused, StringComparison.OrdinalIgnoreCase);

        session.SetBreakpoints("main", new[] { 2 });
        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            """, Context(), CancellationToken.None);

        NextPause();
        Assert.False(session.TrySetNextStatement("other.jgs", 1, out string? wrongFile));
        Assert.Contains("file", wrongFile, StringComparison.OrdinalIgnoreCase);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    // --- Live edit ----------------------------------------------------------------------------------

    [Fact]
    public void LiveEdit_OfTheStatementsAhead_TakesEffectOnResume()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            print(a + b)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            let a = 1
            let b = 2
            print(a * b + 100)
            """);
        Assert.True(result.Applied, result.Message);
        Assert.Equal(3, result.NewLocation?.Line);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("102", _output.NormalText);
    }

    [Fact]
    public void LiveEdit_OfALoopBody_AffectsTheRemainingIterations()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let total = 0
            for i in [1, 2, 3] {
                total = total + i
            }
            print(total)
            """, Context(), CancellationToken.None);

        NextPause();                                        // first iteration, before the add
        LiveEditResult result = session.TryApplyEdit("main", """
            let total = 0
            for i in [1, 2, 3] {
                total = total + i * 10
            }
            print(total)
            """);
        Assert.True(result.Applied, result.Message);

        session.SetBreakpoints("main", Array.Empty<int>());
        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("60", _output.NormalText);          // every iteration used the new body
    }

    [Fact]
    public void LiveEdit_WhitespaceAboveThePause_IsCompatible_AndTheLocationShifts()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            print(a + b)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            # a comment pushes everything down one line
            let a = 1
            let b = 2
            print(a + b)
            """);
        Assert.True(result.Applied, result.Message);
        Assert.Equal(4, result.NewLocation?.Line);          // the paused statement moved with the edit

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("3", _output.NormalText);
    }

    [Fact]
    public void LiveEdit_OfCodeThatAlreadyRan_IsIncompatible_AndChangesNothing()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            let b = 2
            print(a + b)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            let a = 99
            let b = 2
            print(a + b)
            """);
        Assert.False(result.Applied);
        Assert.Contains("already ran", result.Message);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("3", _output.NormalText);           // the old program ran unchanged
    }

    [Fact]
    public void LiveEdit_OfTheLoopHeaderExecutionIsInside_IsIncompatible()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let i = 0
            while i < 3 {
                i = i + 1
            }
            print(i)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            let i = 0
            while i < 5 {
                i = i + 1
            }
            print(i)
            """);
        Assert.False(result.Applied);
        Assert.Contains("inside", result.Message);

        session.SetBreakpoints("main", Array.Empty<int>());
        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("3", _output.NormalText);
    }

    [Fact]
    public void LiveEdit_ThatDoesNotParse_IsIncompatible()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 2 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            print(a)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", "let a = ");
        Assert.False(result.Applied);
        Assert.Contains("parse", result.Message);

        session.Continue();
        Assert.True(Await(run).Success);
    }

    [Fact]
    public void LiveEdit_OfAFunctionBody_NotOnTheStack_AffectsFutureCalls()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 5 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            fn f(n) {
                return n + 1
            }
            let a = f(1)
            let b = f(1)
            print(a + b)
            """, Context(), CancellationToken.None);

        NextPause();                                        // after the first call, before the second
        LiveEditResult result = session.TryApplyEdit("main", """
            fn f(n) {
                return n + 100
            }
            let a = f(1)
            let b = f(1)
            print(a + b)
            """);
        Assert.True(result.Applied, result.Message);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("103", _output.NormalText);         // a kept the old result (2), b got the new (101)
    }

    [Fact]
    public void LiveEdit_AddingANewFunction_HoistsIt()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 2 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            let a = 1
            print(a)
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            let a = 1
            print(g(a))
            fn g(n) {
                return n * 30
            }
            """);
        Assert.True(result.Applied, result.Message);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("30", _output.NormalText);          // g was hoisted, callable above its declaration
    }

    [Fact]
    public void LiveEdit_InsideAFunctionOnTheStack_EditsItsRemainingBody()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            fn f(n) {
                let m = n + 1
                return m
            }
            print(f(1))
            """, Context(), CancellationToken.None);

        NextPause();                                        // paused at 'return m', inside f
        LiveEditResult result = session.TryApplyEdit("main", """
            fn f(n) {
                let m = n + 1
                return m * 10
            }
            print(f(1))
            """);
        Assert.True(result.Applied, result.Message);
        Assert.Equal(3, result.NewLocation?.Line);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("20", _output.NormalText);
    }

    [Fact]
    public void LiveEdit_ChangingTheParametersOfAStackedFunction_IsIncompatible()
    {
        JgsDebugSession session = CreateSession();
        session.SetBreakpoints("main", new[] { 3 });

        Task<ScriptRunResult> run = session.RunAsync("main", """
            fn f(n) {
                let m = n + 1
                return m
            }
            print(f(1))
            """, Context(), CancellationToken.None);

        NextPause();
        LiveEditResult result = session.TryApplyEdit("main", """
            fn f(n, o) {
                let m = n + 1
                return m
            }
            print(f(1))
            """);
        Assert.False(result.Applied);
        Assert.Contains("parameters", result.Message);

        session.Continue();
        Assert.True(Await(run).Success);
        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public void LiveEdit_WhenNotPaused_Throws()
    {
        JgsDebugSession session = CreateSession();
        Assert.Throws<InvalidOperationException>(() => session.TryApplyEdit("main", "let a = 1"));
    }
}
