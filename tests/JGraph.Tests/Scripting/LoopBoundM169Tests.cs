using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V8 (ADR 0169): a <c>for</c> loop's bounds run first, once, in the order written, and the compiled
/// road loads its registers against the environment as the bounds left it. Every script runs with
/// the loop compiler forced on and forced off and must print the same bytes; each case also names
/// the road it expects, through the compiler's counters, because a fast path that silently reads a
/// stale binding is invisible in the output of the cases that happen to agree.
/// </summary>
/// <remarks>
/// The parity fixture <c>loop_bound_effects</c> holds R2025b's answers for the whole matrix; these
/// pin the roads, one assertion a road, so a regression names itself. A bound's effect reaches the
/// loop through a nested function, which shares the caller's workspace (ADR 0168), or through
/// <c>assignin</c> from a local one.
/// </remarks>
[Collection("JG facade")]
public class LoopBoundM169Tests : IDisposable
{
    public LoopBoundM169Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Compiled, long Refused, long Bails);

    /// <summary>The road a script must take under the compiler: what the counters must say.</summary>
    private enum Road
    {
        /// <summary>The entry check passed after the bounds and the loop ran on registers.</summary>
        Compiled,

        /// <summary>The bounds ran, the entry check refused, and the walk ran the evaluated steps.</summary>
        RefusedAfterBounds,

        /// <summary>A bound threw before any road was chosen: neither counter moved.</summary>
        NoEntry,

        /// <summary>No claim: the loop may compile or walk.</summary>
        Any,
    }

    private static Run RunWith(bool jit, string code)
    {
        bool previous = JgsLoopJit.Enabled;
        JgsLoopJit.Enabled = jit;
        long compiledBefore = JgsLoopJit.CompiledRuns;
        long refusedBefore = JgsLoopJit.EntryRefusals;
        long bailsBefore = JgsLoopJit.Bails;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText.Trim(), result.Success, result.Message,
                JgsLoopJit.CompiledRuns - compiledBefore,
                JgsLoopJit.EntryRefusals - refusedBefore,
                JgsLoopJit.Bails - bailsBefore);
        }
        finally
        {
            JgsLoopJit.Enabled = previous;
        }
    }

    /// <summary>
    /// Both roads, same bytes, then the compiled run's counters against <paramref name="road"/>.
    /// A vector slot compiles only where packing is on (ADR 0160, 12d): in the unpacked lanes the
    /// entry check refuses it, so a case whose body writes a vector passes
    /// <paramref name="needsPacked"/> and its expected road becomes a refusal there.
    /// </summary>
    private static string AssertParity(string code, Road road, bool needsPacked = false, bool expectSuccess = true)
    {
        Run fast = RunWith(jit: true, code);
        Run walk = RunWith(jit: false, code);

        Assert.Equal(walk.Success, fast.Success);
        if (expectSuccess)
        {
            Assert.True(walk.Success, walk.Message);
        }
        else
        {
            Assert.False(walk.Success);
            Assert.Equal(walk.Message, fast.Message);
        }

        Assert.Equal(walk.Output, fast.Output);
        Assert.Equal(0, walk.Compiled);
        Assert.Equal(0, walk.Refused);

        if (road == Road.Compiled && needsPacked && !JgsPacking.Enabled)
        {
            road = Road.RefusedAfterBounds;
        }

        switch (road)
        {
            case Road.Compiled:
                Assert.True(fast.Compiled > 0, "the loop was expected to compile");
                Assert.Equal(0, fast.Refused);
                break;
            case Road.RefusedAfterBounds:
                Assert.Equal(0, fast.Compiled);
                Assert.True(fast.Refused > 0, "the entry check was expected to refuse");
                break;
            case Road.NoEntry:
                Assert.Equal(0, fast.Compiled);
                Assert.Equal(0, fast.Refused);
                Assert.Equal(0, fast.Bails);
                break;
        }

        return fast.Output;
    }

    // --- a bound's effect on a vector the body writes (#37) --------------------------------------------

    [Fact]
    public void ABoundThatRebindsTheVector_IsReadByTheCompiledLoop()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            x = zeros(1, 5);
            for k = 1:resetx()
                x(k) = k;
            end
            s = mat2str(x);
                function n = resetx()
                    x = 7 * ones(1, 5);
                    n = 1;
                end
            end
            """, Road.Compiled, needsPacked: true);

        Assert.Equal("[1 7 7 7 7]", printed);
    }

    [Fact]
    public void ABoundThatGrowsTheVector_IsReadByTheCompiledLoop()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            x = zeros(1, 3);
            for k = 1:growx()
                x(k) = k;
            end
            s = mat2str(x);
                function n = growx()
                    x(end + 1) = 9;
                    n = 4;
                end
            end
            """, Road.Compiled, needsPacked: true);

        Assert.Equal("[1 2 3 4]", printed);
    }

    [Theory]
    [InlineData("x = 5;", "[10 20 30]")]
    [InlineData("clear x", "[10 20 30]")]
    public void ABoundThatDemotesOrClearsTheVector_IsRefusedAfterTheBounds(string effect, string expected)
    {
        string printed = AssertTwoCalls($$"""
            fprintf('%s', run_it());
            function s = run_it()
            first = pass(false);
            second = pass(true);
            s = [first ' ' second];
            end
            function s = pass(effect)
            x = zeros(1, 3);
            for k = 1:bound()
                x(k) = k * 10;
            end
            s = mat2str(x);
                function n = bound()
                    if effect
                        {{effect}}
                    end
                    n = 3;
                end
            end
            """, needsPacked: true);

        Assert.Equal($"[10 20 30] {expected}", printed);
    }

    /// <summary>
    /// A loop that runs twice in one script: the first call compiles it over valid bindings, the
    /// second call's bound spoils one, and the cached program's entry check — run after the bounds —
    /// refuses, so the walk runs the evaluated steps. Both roads print the same bytes, and the
    /// counters say one compiled entry and one refusal (two refusals in a lane where the vector slot
    /// never qualifies).
    /// </summary>
    private static string AssertTwoCalls(string code, bool needsPacked = false)
    {
        Run fast = RunWith(jit: true, code);
        Run walk = RunWith(jit: false, code);
        Assert.True(walk.Success, walk.Message);
        Assert.Equal(walk.Success, fast.Success);
        Assert.Equal(walk.Output, fast.Output);
        Assert.Equal(0, walk.Compiled);
        if (!needsPacked || JgsPacking.Enabled)
        {
            Assert.Equal(1, fast.Compiled);
            Assert.Equal(1, fast.Refused);
        }
        else
        {
            Assert.Equal(0, fast.Compiled);
            Assert.Equal(2, fast.Refused);
        }

        return fast.Output;
    }

    // --- a bound's effect on a scalar the body reads ------------------------------------------------------

    [Fact]
    public void ABoundThatRebindsAScalar_IsReadByTheCompiledLoop()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            a = 1;
            t = 0;
            for k = 1:seta()
                t = t + a * k;
            end
            s = sprintf('%d %d', t, a);
                function n = seta()
                    a = 5;
                    n = 3;
                end
            end
            """, Road.Compiled);

        Assert.Equal("30 5", printed);
    }

    [Fact]
    public void ABoundThatPromotesAScalarToAVector_IsRefusedAfterTheBoundsOnTheCachedProgram()
    {
        string printed = AssertTwoCalls("""
            fprintf('%s', run_it());
            function s = run_it()
            s = [pass(false) ' ' pass(true)];
            end
            function s = pass(effect)
            a = 2;
            t = 0;
            for k = 1:bound()
                t = t + a * k;
            end
            s = mat2str(t);
                function n = bound()
                    if effect
                        a = [10 20 30];
                    end
                    n = 3;
                end
            end
            """);

        Assert.Equal("12 [60 120 180]", printed);
    }

    // --- a bound that shadows a builtin the body calls ------------------------------------------------------

    [Fact]
    public void ABoundThatShadowsABuiltinTheBodyCalls_IsRefusedAfterTheBoundsOnTheCachedProgram()
    {
        // MATLAB binds abs(k) at parse time inside a function and never sees the variable (a
        // recorded divergence, ADR 0169); here a name is resolved when it is read, so the walk
        // indexes the variable and the compiled road must not call the builtin it bound before.
        string printed = AssertTwoCalls("""
            fprintf('%s', run_it());
            function s = run_it()
            s = [pass(false) ' ' pass(true)];
            end
            function s = pass(effect)
            t = 0;
            for k = 1:bound(effect)
                t = t + abs(k);
            end
            s = num2str(t);
            end
            function n = bound(effect)
            if effect
                assignin('caller', 'abs', [4 5 6]);
            end
            n = 3;
            end
            """);

        Assert.Equal("6 15", printed);
    }

    // --- start, step and stop: each once, in order, whichever road ------------------------------------------

    [Fact]
    public void StartStepAndStop_RunOnceEach_InOrder_BeforeTheEntry()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            log = '';
            t = 0;
            for k = tick('s'):tick('d'):tick('e')
                t = t + k;
            end
            s = sprintf('%s %d', log, t);
                function n = tick(c)
                    log = [log c];
                    if c == 'e'
                        n = 3;
                    else
                        n = 1;
                    end
                end
            end
            """, Road.Compiled);

        Assert.Equal("sde 6", printed);
    }

    [Fact]
    public void ABoundThatThrows_ThrowsOnce_BeforeAnyRoadIsChosen()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            log = '';
            x = zeros(1, 3);
            try
                for k = tick('s'):boom()
                    x(k) = k;
                end
                s = 'no error';
            catch err
                s = sprintf('%s/%s/%s', log, err.message, mat2str(x));
            end
                function n = tick(c)
                    log = [log c];
                    n = 1;
                end
            end
            function n = boom()
            error('lb:boom', 'the stop threw');
            end
            """, Road.NoEntry);

        Assert.Equal("s/the stop threw/[0 0 0]", printed);
    }

    [Fact]
    public void AnUncaughtBoundError_SurfacesOnce_WithTheSameMessageOnBothRoads()
    {
        Run fast = RunWith(jit: true, "for k = 1:boom(), end\nfunction n = boom()\nerror('lb:boom', 'the stop threw');\nend\n");
        Run walk = RunWith(jit: false, "for k = 1:boom(), end\nfunction n = boom()\nerror('lb:boom', 'the stop threw');\nend\n");
        Assert.False(walk.Success);
        Assert.Equal(walk.Message, fast.Message);
        Assert.Contains("the stop threw", fast.Message);
        Assert.Equal(0, fast.Compiled);
        Assert.Equal(0, fast.Refused);
    }

    // --- the loop variable ---------------------------------------------------------------------------------

    [Fact]
    public void AStopThatReadsTheLoopVariablesPriorBinding_ReadsItBeforeTheLoopBindsIt()
    {
        string printed = AssertParity("""
            k = 3;
            t = 0;
            for k = 1:k + 1
                t = t + k;
            end
            fprintf('%d %d', t, k);
            """, Road.Compiled);

        Assert.Equal("10 4", printed);
    }

    [Fact]
    public void ABoundThatRebindsTheLoopVariable_IsOverriddenByTheLoop()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            k = 0;
            t = 0;
            for k = 1:setk()
                t = t + k;
            end
            s = sprintf('%d %d', t, k);
                function n = setk()
                    k = 99;
                    n = 3;
                end
            end
            """, Road.Compiled);

        Assert.Equal("6 3", printed);
    }

    [Theory]
    [InlineData("1:0")]
    [InlineData("[]")]
    [InlineData("zeros(1, 0)")]
    [InlineData("{}")]
    [InlineData("''")]
    [InlineData("strings(1, 0)")]
    [InlineData("int8(1):int8(0)")]
    [InlineData("5:-1:6")]
    public void AZeroTripLoop_LeavesItsVariableA0By0Double(string head)
    {
        string printed = AssertParity($"""
            k = 'was';
            for k = {head}
            end
            fprintf('%d %s %s', exist('k', 'var'), class(k), mat2str(size(k)));
            """, Road.Any);

        Assert.Equal("1 double [0 0]", printed);
    }

    [Fact]
    public void AZeroTripLoopAfterABoundWithAnEffect_RunsTheBoundOnce_AndBindsTheEmpty()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            log = '';
            k = 'was';
            for k = tick('s'):tick('z')
            end
            s = sprintf('%s %s', log, mat2str(size(k)));
                function n = tick(c)
                    log = [log c];
                    if c == 'z'
                        n = 0;
                    else
                        n = 1;
                    end
                end
            end
            """, Road.NoEntry);

        Assert.Equal("sz [0 0]", printed);
    }

    [Fact]
    public void ANestedLoopWithNoPass_LeavesItsVariableEmpty_OnTheCompiledRoadToo()
    {
        // The last outer pass runs the inner loop over 1:0; the compiled road marks the slot and
        // spills [] where the walk binds it.
        string printed = AssertParity("""
            t = 0;
            for i = 1:3
                for j = 1:(3 - i)
                    t = t + j;
                end
            end
            fprintf('%d %s %s', t, class(j), mat2str(size(j)));
            """, Road.Compiled);

        Assert.Equal("4 double [0 0]", printed);
    }

    [Fact]
    public void AStatementThatReadsANestedLoopsVariableOutsideItsLoop_Walks()
    {
        // t + j with j = [] is [], which no register holds: the compiler refuses the statement that
        // reads j outside its loop, so it is a walked statement (ADR 0160, 12d) — the spill declares
        // the [] the marked slot stands for, the walk adds it, and the loop stays compiled. A
        // program that read the register would have added the stale j of the second pass and
        // printed a 4.
        Run fast = RunWith(jit: true, NestedVariableReadOutside);
        Run walk = RunWith(jit: false, NestedVariableReadOutside);
        Assert.True(walk.Success, walk.Message);
        Assert.Equal(walk.Output, fast.Output);
        Assert.Equal("double [0 0] 3", fast.Output);
        Assert.Equal(1, fast.Compiled);
    }

    private const string NestedVariableReadOutside = """
        t = 0;
        u = 0;
        for i = 1:3
            for j = 1:(3 - i)
                u = u + 1;
            end
            t = t + j;
        end
        fprintf('%s %s %d', class(t), mat2str(size(t)), u);
        """;

    [Fact]
    public void AZeroRowSource_RunsAPassPerColumn_OverEmptyColumns()
    {
        string printed = AssertParity("""
            j = 'was';
            n = 0;
            for j = zeros(0, 3)
                n = n + 1;
            end
            fprintf('%d %s %s', n, class(j), mat2str(size(j)));
            """, Road.Any);

        Assert.Equal("3 double [0 1]", printed);
    }

    // --- an inner loop's bound runs on every pass of the outer ---------------------------------------------

    [Fact]
    public void AnInnerLoopsBound_RunsOnEveryPassOfTheOuter()
    {
        string printed = AssertParity("""
            fprintf('%s', run_it());
            function s = run_it()
            c = 0;
            t = 0;
            for i = 1:2
                for j = 1:bump()
                    t = t + j;
                end
            end
            s = sprintf('%d %d', t, c);
                function n = bump()
                    c = c + 1;
                    n = c;
                end
            end
            """, Road.Any);

        Assert.Equal("4 2", printed);
    }
}
