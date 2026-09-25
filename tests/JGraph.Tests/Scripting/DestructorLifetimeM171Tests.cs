using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V10 (ADR 0171): exact lifetime for handles with destructors. An <c>onCleanup</c> task and a
/// handle class's <c>delete</c> run when the last holder of the object goes - at a frame's exit,
/// at <c>clear</c>, at a rebinding, at an error's unwinding, when a container, a snapshot or an
/// escaped nested workspace that held it is released - after the statement that dropped it, in
/// R2025b's measured order; a temporary dies at its statement's end; <c>delete(h)</c> releases
/// the properties at once; a listener made by <c>listener</c> ends with its last handle.
/// </summary>
/// <remarks>
/// The parity fixture <c>destructor_lifetime</c> holds R2025b's answers for the whole matrix (53
/// lines); these pin the roads the stage touched, one assertion a road, so a regression names
/// itself. The helpers (<c>DeleteLogger</c>, <c>DeleteHolder</c>, <c>DeleteThrower</c>,
/// <c>PropOrder</c>, <c>HandleHolder</c>, <c>ValueBox</c>, <c>EventBox</c>, <c>vlog</c>) are the
/// fixtures'.
/// </remarks>
[Collection("JG facade")]
public class DestructorLifetimeM171Tests : IDisposable
{
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();

    public DestructorLifetimeM171Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    /// <summary>The fixtures' log: a global the helper <c>vlog</c> appends to.</summary>
    private const string Log = "global vlog_text; vlog_text = ''; ";

    /// <summary>
    /// A script that runs <paramref name="body"/> inside a function (so the variables it binds
    /// are a frame's) and prints the log; <paramref name="helpers"/> are further local functions.
    /// </summary>
    private static string Case(string body, string helpers = "") =>
        Log + "disp(probe());\nfunction s = probe()\n" + body + "\ns = logged();\nend\n"
        + "function s = logged()\nglobal vlog_text\ns = vlog_text;\nend\n" + helpers;

    // --- a frame's exit ---------------------------------------------------------------------------

    [Fact]
    public void AFramesVariablesAreReleasedInDeclarationOrderAtItsExit()
    {
        Assert.Equal("Z;A;M;after;", RunAndRead(Log + """
            make_zam(); vlog('after'); disp(vlog_text);
            function make_zam()
            z = DeleteLogger('Z'); a = DeleteLogger('A'); m = DeleteLogger('M');
            end
            """));
    }

    [Fact]
    public void ARebindingDestroysTheOldValueAndKeepsTheNamesSlot()
    {
        Assert.Equal("M1;M2;A;after;", RunAndRead(Log + """
            make_rebound(); vlog('after'); disp(vlog_text);
            function make_rebound()
            m = DeleteLogger('M1'); a = DeleteLogger('A'); m = DeleteLogger('M2');
            end
            """));
    }

    [Fact]
    public void AnOutputTheCallerKeepsSurvivesTheFramesExit()
    {
        Assert.Equal("Z;A;kept;H;after;", RunAndRead(Case(
            "h = make_h_with_locals(); vlog('kept'); clear h; vlog('after');",
            """
            function h = make_h_with_locals()
            z = DeleteLogger('Z'); h = DeleteLogger('H'); a = DeleteLogger('A');
            end
            """)));
    }

    [Fact]
    public void ADiscardedOutputDiesWithTheStatement()
    {
        Assert.Equal("H;after;", RunAndRead(Case(
            "[~] = make_h(); vlog('after');",
            """
            function h = make_h()
            h = DeleteLogger('H');
            end
            """)));
    }

    [Fact]
    public void AnErrorUnwindsTheFrameBeforeTheCatchRuns()
    {
        Assert.Equal("done;caught;after;", RunAndRead(Case(
            """
            try
                thrower();
            catch
                vlog('caught');
            end
            vlog('after');
            """,
            """
            function thrower()
            c = onCleanup(@() vlog('done'));
            error('lt:boom', 'boom');
            end
            """)));
    }

    // --- clear and rebinding ----------------------------------------------------------------------

    [Theory]
    [InlineData("clear", "Z;A;M;after;")]
    [InlineData("clear variables", "Z;A;M;after;")]
    [InlineData("clear m z", "M;Z;after;")]
    public void ClearReleasesInDeclarationOrderAndNamedInNamedOrder(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(Case($"""
            z = DeleteLogger('Z'); a = DeleteLogger('A'); m = DeleteLogger('M');
            {statement}
            vlog('after');
            """)));
    }

    [Fact]
    public void AnAliasOutlivesClearOfTheOriginal()
    {
        Assert.Equal("mid;A;after;", RunAndRead(Case("""
            a = DeleteLogger('A'); b = a; clear a; vlog('mid'); clear b; vlog('after');
            """)));
    }

    [Fact]
    public void ClearOfACleanupInsideAFunctionRunsItThere()
    {
        Assert.Equal("done;after;", RunAndRead(Case("""
            c = onCleanup(@() vlog('done')); clear c; vlog('after');
            """)));
    }

    [Fact]
    public void ACleanupReadsTheValueItCapturedNotALaterOne()
    {
        Assert.Equal("7;1;after;", RunAndRead(Case("""
            v = [1 2 3]; c = onCleanup(@() vlog(sprintf('%d', v(1)))); v(1) = 7; vlog(sprintf('%d', v(1))); clear c; vlog('after');
            """)));
    }

    // --- temporaries and ans ----------------------------------------------------------------------

    [Fact]
    public void ATemporaryArgumentDiesAtTheEndOfItsStatement()
    {
        Assert.Equal("usedT;T;after;", RunAndRead(Case(
            "use_tag(DeleteLogger('T')); vlog('after');",
            """
            function use_tag(x)
            vlog(['used' x.tag]);
            end
            """)));
    }

    [Fact]
    public void AStatementBindsTheHandleToAnsUntilAnsIsRebound()
    {
        Assert.Equal("mid;T;after;", RunAndRead(Case("""
            DeleteLogger('T'); vlog('mid'); max(1, 2); vlog('after');
            """)));
    }

    [Fact]
    public void ACalleeClearingItsParameterLeavesTheCallersHandleAlive()
    {
        Assert.Equal("in;mid;A;after;", RunAndRead(Case(
            "a = DeleteLogger('A'); takes_and_clears(a); vlog('mid'); clear a; vlog('after');",
            """
            function takes_and_clears(x)
            clear x
            vlog('in');
            end
            """)));
    }

    // --- captures and nested workspaces -----------------------------------------------------------

    [Fact]
    public void AnAnonymousSnapshotHoldsTheHandleUntilTheHandleIsCleared()
    {
        Assert.Equal("kept;C;after;", RunAndRead(Case("""
            c = DeleteLogger('C'); g = @() c; clear c; vlog('kept'); clear g; vlog('after');
            """)));
    }

    [Fact]
    public void AnEscapedNestedWorkspaceLivesUntilItsLastHandleGoes()
    {
        Assert.Equal("1;one;1;done;after;", RunAndRead(Case(
            "[f1, f2] = make_two(); vlog(sprintf('%d', f1())); clear f1; vlog('one'); vlog(sprintf('%d', f2())); clear f2; vlog('after');",
            """
            function [f1, f2] = make_two()
            c = onCleanup(@() vlog('done'));
            f1 = @read1;
            f2 = @read2;
                function y = read1()
                    y = isa(c, 'onCleanup');
                end
                function y = read2()
                    y = isa(c, 'onCleanup');
                end
            end
            """)));
    }

    [Fact]
    public void ANestedHandleHeldOnlyByItsOwnWorkspaceIsNoEscape()
    {
        Assert.Equal("done;after;", RunAndRead(Case(
            "make_selfref(); vlog('after');",
            """
            function make_selfref()
            c = onCleanup(@() vlog('done'));
            f = @read;
                function y = read()
                    y = c;
                end
            end
            """)));
    }

    [Fact]
    public void ClearInANestedFunctionReleasesTheParentsVariableAtOnce()
    {
        Assert.Equal("done;cleared;back;after;", RunAndRead(Case(
            "outer(); vlog('after');",
            """
            function outer()
            c = onCleanup(@() vlog('done'));
            inner();
            vlog('back');
                function inner()
                    clear c
                    vlog('cleared');
                end
            end
            """)));
    }

    // --- containers -------------------------------------------------------------------------------

    [Fact]
    public void AContainerReleasesDirectHandlesFirstThenNestedContainers()
    {
        Assert.Equal("A;D;B;C;after;", RunAndRead(Case("""
            c = {DeleteLogger('A'), {DeleteLogger('B'), DeleteLogger('C')}, DeleteLogger('D')}; clear c; vlog('after');
            """)));
    }

    [Fact]
    public void AStructReleasesDirectHandlesFirstInFieldOrder()
    {
        Assert.Equal("D;A;B;after;", RunAndRead(Case("""
            st.c = {DeleteLogger('A'), DeleteLogger('B')}; st.d = DeleteLogger('D'); clear st; vlog('after');
            """)));
    }

    [Fact]
    public void ASharedContainerHoldsItsHandleUntilItsLastAliasGoes()
    {
        Assert.Equal("kept;done;after;", RunAndRead(Case("""
            c = {onCleanup(@() vlog('done'))}; d = c; clear c; vlog('kept'); clear d; vlog('after');
            """)));
    }

    [Fact]
    public void DeletingTheElementFromOneAliasReleasesThatAliasHoldOnly()
    {
        Assert.Equal("deleted;done;after;", RunAndRead(Case("""
            c = {onCleanup(@() vlog('done')), 1}; d = c; d(1) = []; vlog('deleted'); clear c; vlog('after');
            """)));
    }

    [Fact]
    public void ASlotRebindingReleasesWhatTheSlotHeld()
    {
        Assert.Equal("A;after;", RunAndRead(Case("""
            c = {DeleteLogger('A'), 1}; c{1} = 5; vlog('after');
            """)));
    }

    [Fact]
    public void ANestedSlotWriteMakesTheOuterCellTrackedToo()
    {
        Assert.Equal("kept;A;after;", RunAndRead(Case("""
            c = {{1}}; c{1}{1} = DeleteLogger('A'); d = c; clear c; vlog('kept'); clear d; vlog('after');
            """)));
    }

    [Fact]
    public void AStructArraysElementsReleaseInIndexOrder()
    {
        Assert.Equal("A;B;after;", RunAndRead(Case("""
            st(2).h = DeleteLogger('B'); st(1).h = DeleteLogger('A'); clear st; vlog('after');
            """)));
    }

    [Fact]
    public void AnObjectArrayReleasesInIndexOrder()
    {
        Assert.Equal("A;B;after;", RunAndRead(Case("""
            arr = [DeleteLogger('A'), DeleteLogger('B')]; clear arr; vlog('after');
            """)));
    }

    [Fact]
    public void ALoopOverACellLiteralOfHandlesKeepsThemForTheLoop()
    {
        Assert.Equal("itA;itB;A;after;", RunAndRead(Case("""
            for x = {DeleteLogger('A'), DeleteLogger('B')}
                vlog(['it' x{1}.tag]);
            end
            vlog('after');
            """)));
    }

    [Fact]
    public void AMapReleasesItsEntriesWhenItIsCleared()
    {
        Assert.Equal("A;after;", RunAndRead(Case("""
            m = containers.Map(); m('k') = DeleteLogger('A'); clear m; vlog('after');
            """)));
    }

    // --- objects holding objects ------------------------------------------------------------------

    [Fact]
    public void AHandlesDeleteRunsBeforeItsPropertiesAreReleased()
    {
        Assert.Equal("H;I;after;", RunAndRead(Case("""
            h = DeleteHolder('H'); h.inner = DeleteLogger('I'); clear h; vlog('after');
            """)));
    }

    [Fact]
    public void AnExplicitDeleteReleasesThePropertiesAtOnce()
    {
        Assert.Equal("H;I;J;mid;after;", RunAndRead(Case("""
            h = DeleteHolder('H'); h.inner = {DeleteLogger('I'), DeleteLogger('J')}; delete(h); vlog('mid'); clear h; vlog('after');
            """)));
    }

    [Fact]
    public void AHandleWithoutADestructorReleasesWhatItHoldsWithItsLastAlias()
    {
        Assert.Equal("kept;A;after;", RunAndRead(Case("""
            h = HandleHolder(); h.data = DeleteLogger('A'); g = h; clear h; vlog('kept'); clear g; vlog('after');
            """)));
    }

    [Fact]
    public void AValueObjectReleasesWhatItHoldsWhenCleared()
    {
        Assert.Equal("P;after;", RunAndRead(Case("""
            o = ValueBox(); o.p = DeleteLogger('P'); clear o; vlog('after');
            """)));
    }

    [Fact]
    public void ObjectBeingDestroyedIsRaisedWhenTheLastHolderGoes()
    {
        Assert.Equal("gone;after;", RunAndRead(Case("""
            b = EventBox(); lh = addlistener(b, 'ObjectBeingDestroyed', @(~, ~) vlog('gone')); clear b; vlog('after');
            """)));
    }

    // --- onCleanup itself -------------------------------------------------------------------------

    [Fact]
    public void OnCleanupIsAHandleClassWithATaskProperty()
    {
        Assert.Equal("onCleanup 1 1 1 function_handle", RunAndRead("""
            c = onCleanup(@() 1);
            fprintf('%s %d %d %d %s\n', class(c), isa(c, 'handle'), isa(c, 'onCleanup'), isvalid(c), class(c.task));
            """));
    }

    [Fact]
    public void AnExplicitDeleteRunsTheTaskOnceAndInvalidatesEveryAlias()
    {
        Assert.Equal("done;0;mid;after;", RunAndRead(Case("""
            c = onCleanup(@() vlog('done')); d = c; delete(c); vlog(sprintf('%d', isvalid(d))); vlog('mid'); clear c d; vlog('after');
            """)));
    }

    [Fact]
    public void ATaskThatFailsIsAWarningInMatlabsWords()
    {
        string output = RunAndRead(Case("""
            lastwarn('');
            c = onCleanup(@() error('lt:bad', 'bad task')); clear c; vlog('after');
            [msg, id] = lastwarn;
            vlog(id);
            """));
        Assert.Equal("after;MATLAB:class:DestructorError;", output);
        Assert.Contains("Warning: The following error was caught while executing 'onCleanup' class destructor:\nbad task", _output.ErrorText.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void ADeleteMethodThatFailsIsTheSameWarning()
    {
        string output = RunAndRead(Case("""
            lastwarn('');
            d = DeleteThrower(); clear d; vlog('after');
            [msg, id] = lastwarn;
            vlog(id);
            """));
        Assert.Equal("after;MATLAB:class:DestructorError;", output);
        Assert.Contains("executing 'DeleteThrower' class destructor:", _output.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("onCleanup();", "Not enough input arguments.")]
    [InlineData("onCleanup(5);", "Input must be a function handle.")]
    public void OnCleanupChecksItsArgument(string statement, string expected)
    {
        ScriptRunResult result = Run(statement);
        Assert.False(result.Success);
        Assert.Contains(expected, result.Message ?? "", StringComparison.Ordinal);
    }

    // --- other holders ----------------------------------------------------------------------------

    [Fact]
    public void AGlobalIsReleasedByClearGlobalNotByClearOfTheLink()
    {
        Assert.Equal("unlinked;G;after;", RunAndRead(Case("""
            global lt_test_g
            lt_test_g = DeleteLogger('G'); clear lt_test_g; vlog('unlinked'); clear global lt_test_g; vlog('after');
            """)));
    }

    [Fact]
    public void APersistentReleasesItsHandleWhenReset()
    {
        Assert.Equal("set;P;reset;", RunAndRead(Case(
            "keep(false); vlog('set'); keep(true); vlog('reset');",
            """
            function keep(reset)
            persistent p
            if reset
                p = [];
                return
            end
            if isempty(p)
                p = DeleteLogger('P');
            end
            end
            """)));
    }

    [Fact]
    public void AListenerMadeByListenerEndsWithItsLastHandle()
    {
        Assert.Equal("L;", RunAndRead(Log + """
            b = EventBox();
            lh = listener(b, 'Changed', @(~, ~) vlog('L'));
            b.fire();
            clear lh
            b.fire();
            disp(vlog_text);
            """));
    }

    [Fact]
    public void AListenerMadeByAddlistenerLivesWithItsSource()
    {
        Assert.Equal("L;L;", RunAndRead(Log + """
            b = EventBox();
            lh = addlistener(b, 'Changed', @(~, ~) vlog('L'));
            b.fire();
            clear lh
            b.fire();
            disp(vlog_text);
            """));
    }

    [Fact]
    public void TheBaseWorkspaceIsDestroyedByNameWhenTheRunEnds()
    {
        // R2025b's -batch exit destroys x, y, c as c;x;y (measured).
        Assert.Equal("end;C;X;Y;", RunAndRead("""
            x = onCleanup(@() fprintf('X;'));
            y = onCleanup(@() fprintf('Y;'));
            c = onCleanup(@() fprintf('C;'));
            fprintf('end;');
            """));
    }

    [Fact]
    public void AHandleHeldOnlyByAnUnscannedCellIsNotDestroyedUnderIt()
    {
        // A cell built before anything with a destructor existed still counts the handles it holds
        // (a handle's holders are exact from birth), so a destructor-bearing value put into the
        // handle later runs with the cell, not with the name.
        Assert.Equal("kept;A;after;", RunAndRead(Case("""
            h = HandleHolder(); c = {h}; h.data = DeleteLogger('A'); clear h; vlog('kept'); clear c; vlog('after');
            """)));
    }
}
