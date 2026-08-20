using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The common callback and interaction block (M71): ButtonDownFcn/CreateFcn/DeleteFcn plus
/// Interruptible, BusyAction, Selected, SelectionHighlight, HitTest, PickableParts and BeingDeleted,
/// on every class through one table edit — and the two behaviours with a moment of their own:
/// CreateFcn fires only in the creating call, and DeleteFcn fires on every real deletion path,
/// exactly once, parent-first, while the object still exists.
/// </summary>
[Collection("JG facade")]
public class MatlabCallbackPropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabCallbackPropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (_, _) => { }));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>The printed lines, trimmed — for tests that assert an order of callback firings.</summary>
    private string[] Printed(params string[] interesting) =>
        _output.NormalLines.Select(static l => l.Trim()).Where(interesting.Contains).ToArray();

    [Fact]
    public Task TheCommonBlockHasMatlabsDefaults() => RunAsserting("""
        p = plot(1:3);
        assert(strcmp(get(p, 'BusyAction'), 'queue'));
        assert(strcmp(get(p, 'Interruptible'), 'on'));
        assert(strcmp(get(p, 'HitTest'), 'on'));
        assert(strcmp(get(p, 'PickableParts'), 'visible'));
        assert(strcmp(get(p, 'Selected'), 'off'));
        assert(strcmp(get(p, 'SelectionHighlight'), 'on'));
        assert(strcmp(get(p, 'BeingDeleted'), 'off'));
        assert(isempty(get(p, 'ButtonDownFcn')));
        assert(isempty(get(p, 'CreateFcn')));
        assert(isempty(get(p, 'DeleteFcn')));
        """);

    [Fact]
    public Task ACallbackStoresClearsAndRefusesByName() => RunAsserting("""
        p = plot(1:3);
        set(p, 'ButtonDownFcn', @(src, event) disp('bd'));
        assert(isa(get(p, 'ButtonDownFcn'), 'function_handle'));
        p.ButtonDownFcn = [];
        assert(isempty(p.ButtonDownFcn));
        ok = 0;
        try
            set(p, 'ButtonDownFcn', 42);
        catch err
            ok = contains(err.message, 'function handle');
        end
        assert(ok);
        ok = 0;
        try
            set(p, 'BusyAction', 'sideways');
        catch err
            ok = contains(err.message, 'queue');
        end
        assert(ok);
        """);

    [Fact]
    public Task TheBlockReachesEveryKindThroughOneEdit() => RunAsserting("""
        figure; f = gcf;
        ax = gca;
        p = plot(1:3);
        lgd = legend('a');
        t = title('x');
        assert(strcmp(get(ax, 'BusyAction'), 'queue'));
        assert(strcmp(get(lgd, 'BeingDeleted'), 'off'));
        assert(strcmp(get(f, 'Interruptible'), 'on'));
        set(ax, 'DeleteFcn', @(s, e) disp('x'));
        assert(isa(get(ax, 'DeleteFcn'), 'function_handle'));
        assert(isempty(get(f, 'CloseRequestFcn')));
        assert(isempty(get(f, 'SizeChangedFcn')));
        set(f, 'CloseRequestFcn', @(s, e) disp('closing'));
        assert(isa(get(f, 'CloseRequestFcn'), 'function_handle'));
        """);

    [Fact]
    public Task SelectedAndHitTestReadAndWriteTheModelsOwnState() => RunAsserting("""
        p = plot(1:3);
        set(p, 'Selected', 'on');
        assert(strcmp(get(p, 'Selected'), 'on'));
        set(p, 'HitTest', 'off');
        assert(strcmp(get(p, 'HitTest'), 'off'));
        set(p, 'SelectionHighlight', 'off');
        assert(strcmp(get(p, 'SelectionHighlight'), 'off'));
        set(p, 'PickableParts', 'none');
        assert(strcmp(get(p, 'PickableParts'), 'none'));
        """);

    [Fact]
    public Task CreateFcnFiresInTheCreatingCallAndOnlyThere() => RunAsserting("""
        made = 0;
        q = plot(1:3, 'CreateFcn', @(s, e) assignin('base', 'made', made + 1));
        assert(made == 1);
        set(q, 'CreateFcn', @(s, e) assignin('base', 'made', made + 10));
        assert(made == 1);
        """);

    [Fact]
    public async Task CreateFcnSeesTheNewObjectAsGcbo()
    {
        await RunAsserting("""
            q = plot(1:3, 'CreateFcn', @(s, e) fprintf('created %d\n', gcbo == s));
            """);
        Assert.Contains(_output.NormalLines, static line => line.Contains("created 1"));
    }

    [Fact]
    public async Task DeleteFcnFiresWhileTheObjectStillExists()
    {
        await RunAsserting("""
            r = plot(1:3);
            set(r, 'DeleteFcn', @(s, e) fprintf('deleted %s\n', get(s, 'BeingDeleted')));
            delete(r);
            assert(~ishandle(r));
            """);
        Assert.Contains(_output.NormalLines, static line => line.Contains("deleted on"));
    }

    [Fact]
    public async Task DeletingAnAxesFiresParentFirstThenItsChildren()
    {
        await RunAsserting("""
            ax = gca;
            s1 = plot(1:3); hold on; s2 = plot(2:4);
            set(ax, 'DeleteFcn', @(s, e) disp('ax'));
            set(s1, 'DeleteFcn', @(s, e) disp('s1'));
            set(s2, 'DeleteFcn', @(s, e) disp('s2'));
            delete(ax);
            """);
        Assert.Equal(new[] { "ax", "s1", "s2" }, Printed("ax", "s1", "s2"));
    }

    [Fact]
    public async Task ClfClaAndHoldOffReplacementAllFireDeleteFcn()
    {
        await RunAsserting("""
            t1 = plot(1:3);
            set(t1, 'DeleteFcn', @(s, e) disp('clf'));
            clf;
            t2 = plot(1:3);
            set(t2, 'DeleteFcn', @(s, e) disp('cla'));
            cla;
            t3 = plot(1:3);
            set(t3, 'DeleteFcn', @(s, e) disp('replace'));
            plot(4:6);
            """);
        Assert.Equal(new[] { "clf", "cla", "replace" }, Printed("clf", "cla", "replace"));
    }

    [Fact]
    public async Task ADeleteFcnThatDeletesThingsItself_FiresEachExactlyOnce()
    {
        await RunAsserting("""
            u1 = plot(1:3); hold on; u2 = plot(2:4);
            set(u1, 'DeleteFcn', @(s, e) eval('disp(''u1''); delete(u2); disp(''u1 end'');'));
            set(u2, 'DeleteFcn', @(s, e) disp('u2'));
            clf;
            """);
        Assert.Equal(new[] { "u1", "u2", "u1 end" }, Printed("u1", "u2", "u1 end"));
    }

    [Fact]
    public async Task CloseFiresTheFiguresDeleteFcn()
    {
        await RunAsserting("""
            figure; f2 = gcf;
            set(f2, 'DeleteFcn', @(s, e) disp('bye'));
            close(f2);
            """);
        Assert.Contains("bye", Printed("bye"));
    }

    [Fact]
    public async Task AReparentMoveFiresNothing_AndAnErroringDeleteFcnDoesNotStopDeletion()
    {
        await RunAsserting("""
            a1 = subplot(1,2,1); a2 = subplot(1,2,2);
            v = plot(a1, 1:3);
            set(v, 'DeleteFcn', @(s, e) disp('moved!'));
            set(v, 'Parent', a2);
            assert(ishandle(v));
            hold(a2, 'on');   % or the next plot would replace v — a real deletion, correctly fired
            w = plot(a2, 1:3);
            set(w, 'DeleteFcn', @(s, e) error('boom'));
            delete(w);
            assert(~ishandle(w));
            """);
        Assert.DoesNotContain("moved!", Printed("moved!"));
        Assert.Contains(_output.Errors, static e => e.Contains("boom"));
    }

    [Fact]
    public async Task DeletingTheLegendIsDeletionButHidingItIsNot()
    {
        await RunAsserting("""
            p = plot(1:3);
            lgd = legend('a');
            set(lgd, 'DeleteFcn', @(s, e) disp('legend gone'));
            set(lgd, 'Visible', 'off');
            set(lgd, 'Visible', 'on');
            delete(lgd);
            """);
        Assert.Single(Printed("legend gone"));
    }

    [Fact]
    public Task EntryStateOnAHiddenLegendSurvivesTheReaper() => RunAsserting("""
        p = plot(1:3);
        lgd = legend('a');
        set(lgd, 'ItemHitFcn', @(s, e) disp('x'));
        set(lgd, 'Visible', 'off');
        hold on; r = plot(3:5);
        delete(r);
        assert(isa(get(lgd, 'ItemHitFcn'), 'function_handle'));
        """);

    [Fact]
    public async Task WorkspaceClearFiresNoDeleteFcn()
    {
        var session = NewSession();
        await session.ExecuteAsync(
            "p = plot(1:3); set(p, 'DeleteFcn', @(s, e) disp('cleared!'));", "", CancellationToken.None);
        session.Clear();
        await session.DisposeAsync();
        Assert.DoesNotContain("cleared!", Printed("cleared!"));
    }
}
