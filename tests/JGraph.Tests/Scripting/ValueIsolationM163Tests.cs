using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V2's contract (ADR 0163): every entry that keeps a wrapper it did not compute — an anonymous
/// snapshot, a keyed collection's value, appdata, <c>ans</c>, a named workspace, a cell a verb
/// built out of another's slots — holds a counted share of its own (M2), and a binding adopts a
/// call's answer only when the callee is known to have minted it.
/// </summary>
/// <remarks>
/// As in <see cref="OwnershipM162Tests"/>, the tests assert storage where they can — which payload
/// two entries hold, how many hold it, how many buffers a write allocates — and the printed answer
/// only where the road's whole point is the answer (<c>str2func</c> capturing nothing). The parity
/// fixtures hold the appendix lines themselves.
/// </remarks>
[Collection("JG facade")]
public class ValueIsolationM163Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public ValueIsolationM163Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private async Task<JgsReplSession> RunAsync(string code)
    {
        JgsReplSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return session;
    }

    // ---- retained holders: the snapshot, the collection, appdata, the named workspace --------

    [Fact]
    public async Task AnAnonymousSnapshotHoldsItsOwnShare()
    {
        await using JgsReplSession session = await RunAsync("v = ones(1, 5); f = @() v; v(1) = 7; r = f();");
        JgsValue v = Get(session, "v");
        JgsValue r = Get(session, "r");

        Assert.Equal(7, v.ElementAt(0).AsNumber);
        Assert.Equal(1, r.ElementAt(0).AsNumber);     // the capture kept what it saw (#1)
        Assert.False(v.SharesStorageWith(r));         // the write detached the workspace's side
    }

    [Fact]
    public async Task ANestedFunctionsWriteThroughTheSharedWorkspaceDoesNotReachTheSnapshot()
    {
        // #24: the nested function writes the parent's `v`, which the snapshot shares under a count.
        await using JgsReplSession session = await RunAsync(
            "r = nested_capture_probe();\n"
            + "function r = nested_capture_probe()\n"
            + "v = ones(1, 5); f = @() v; setv(); r = f();\n"
            + "    function setv()\n"
            + "        v(1) = 7;\n"
            + "    end\n"
            + "end\n");

        Assert.Equal(1, Get(session, "r").ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task AKeyedCollectionsValueHoldsItsOwnShare()
    {
        await using JgsReplSession session = await RunAsync(
            "v = ones(1, 5); m = containers.Map(); m('k') = v; v(1) = 7; q = m('k');");

        Assert.Equal(1, Get(session, "q").ElementAt(0).AsNumber);   // #2
        Assert.Equal(7, Get(session, "v").ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task ValuesOfACollectionAreEntriesOfTheirOwn()
    {
        await using JgsReplSession session = await RunAsync(
            "m = containers.Map(); m('k') = ones(1, 3); c = values(m); c{1}(1) = 9; back = m('k');");

        Assert.Equal(1, Get(session, "back").ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task AppDataHoldsItsOwnShare()
    {
        await using JgsReplSession session = await RunAsync(
            "f = figure('Visible', 'off'); v = [1 2 3]; setappdata(f, 'k', v); v(1) = 7; "
            + "w = getappdata(f, 'k'); w(2) = 8; back = getappdata(f, 'k'); close(f);");

        JgsValue back = Get(session, "back");
        Assert.Equal(1, back.ElementAt(0).AsNumber);   // #100: neither write reached the figure
        Assert.Equal(2, back.ElementAt(1).AsNumber);
    }

    [Fact]
    public async Task ACallbackStoredOnAGraphicsObjectKeepsItsCapture()
    {
        await using JgsReplSession session = await RunAsync(
            "f = figure('Visible', 'off'); p = plot([1 2 3]); v = [1 2 3]; p.ButtonDownFcn = @(~, ~) v; "
            + "v(1) = 7; fn = p.ButtonDownFcn; r = fn([], []); close(f);");

        Assert.Equal(1, Get(session, "r").ElementAt(0).AsNumber);   // #101
    }

    [Fact]
    public async Task AssigninHandsTheNamedWorkspaceItsOwnShare()
    {
        await using JgsReplSession session = await RunAsync(
            "v = ones(1, 3); assignin('base', 'w', v); v(1) = 7; w(2) = 8;");

        JgsValue v = Get(session, "v");
        JgsValue w = Get(session, "w");
        Assert.False(v.SharesStorageWith(w));
        Assert.Equal(1, w.ElementAt(0).AsNumber);
        Assert.Equal(1, v.ElementAt(1).AsNumber);
    }

    [Fact]
    public async Task AHandleFromStr2funcCapturesNothing()
    {
        // #80: R2025b's handle sees no caller variable; calling it is an error, and the handle
        // whose text names only functions still works.
        await using JgsReplSession session = await RunAsync(
            "x = 7; f = str2func('@() x'); ok = 0; try, y = f(); ok = 1; catch, end\n"
            + "g = str2func('@(a) a + 1'); z = g(4);");

        Assert.Equal(0, Get(session, "ok").AsNumber);
        Assert.Equal(5, Get(session, "z").AsNumber);
    }

    // ---- implicit bindings: ans ---------------------------------------------------------------

    [Fact]
    public async Task AnsIsAnEntryOfItsOwn()
    {
        await using JgsReplSession session = await RunAsync("C = {ones(1, 5)}; C{1}; ans(1) = 7;");

        Assert.Equal(1, Get(session, "C").AsCell[0].ElementAt(0).AsNumber);   // #3
        Assert.Equal(7, Get(session, "ans").ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task AStructFieldReadIntoAnsStaysWhereItWas()
    {
        await using JgsReplSession session = await RunAsync("st.f = ones(1, 5); st.f; ans(1) = 7;");

        Assert.Equal(1, Get(session, "st").AsStruct["f"].ElementAt(0).AsNumber);   // #4
    }

    // ---- a verb that places another's children in a container it builds ----------------------

    [Theory]
    [InlineData("D = [C, C];")]
    [InlineData("D = [C; C];")]
    [InlineData("D = repmat(C, 1, 2);")]
    [InlineData("D = C([1 1]);")]
    [InlineData("D = [C(1), C(:)'];")]
    [InlineData("D = num2cell(C); D = [D, D];")]
    public async Task ACellBuiltFromAnothersSlotsHoldsAShareInEverySlot(string build)
    {
        await using JgsReplSession session = await RunAsync("C = {ones(1, 3)}; " + build + " D{1}(1) = 7;");
        JgsValue c = Get(session, "C");
        JgsValue d = Get(session, "D");

        Assert.Equal(7, d.AsCell[0].ElementAt(0).AsNumber);
        Assert.Equal(1, d.AsCell[1].ElementAt(0).AsNumber);   // two slots, two wrappers
        Assert.Equal(1, c.AsCell[0].ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task ATransposedCellHoldsItsOwnShares()
    {
        await using JgsReplSession session = await RunAsync("C = {ones(1, 3), 2}; F = C'; F{1}(1) = 7;");

        Assert.Equal(1, Get(session, "C").AsCell[0].ElementAt(0).AsNumber);
    }

    [Theory]
    [InlineData("t = setfield(s, 'g', 1);")]
    [InlineData("t = rmfield(s, 'g');")]
    [InlineData("t = orderfields(s);")]
    public async Task AStructRebuiltFromAnothersFieldsHoldsShares(string rebuild)
    {
        await using JgsReplSession session = await RunAsync(
            "s = struct('f', ones(1, 3), 'g', 0); " + rebuild + " t.f(1) = 7;");

        Assert.Equal(1, Get(session, "s").AsStruct["f"].ElementAt(0).AsNumber);
        Assert.Equal(7, Get(session, "t").AsStruct["f"].ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task ACollectedAnswerThatIsSomeonesWrapperIsShared()
    {
        // The handle answers the wrapper its snapshot holds; the collecting cell's slot is an
        // entry, so writing through it must not move what the handle will answer next time.
        await using JgsReplSession session = await RunAsync(
            "G = ones(1, 3); h = @(x) G; D = cellfun(h, {1}, 'UniformOutput', false); D{1}(1) = 7; r = h(1);");

        Assert.Equal(1, Get(session, "r").ElementAt(0).AsNumber);
        Assert.Equal(7, Get(session, "D").AsCell[0].ElementAt(0).AsNumber);
    }

    // ---- what a binding adopts, and what it still shares -------------------------------------

    [Fact]
    public async Task AMintingBuiltinsAnswerIsAdoptedSoTheFirstWriteAllocatesNothing()
    {
        // Packed storage only: in the boxed lane (JGRAPH_JGS_PACKED=0) an array owns no native,
        // mapped or counted buffer, so there is nothing here to measure.
        if (!JgsPacking.Enabled)
        {
            return;
        }

        // `zeros` builds its buffer outside the counted allocator, so every allocation the counter
        // sees here is a copy a write made: none when the answer was adopted, one when a second
        // name shares it.
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Managed };
        using (JgsPacking.Use(allocator))
        {
            long before = JgsPacking.Allocations;
            await using (await RunAsync("x = zeros(1, 64); x(1) = 1; x(2) = 2;"))
            {
            }

            Assert.Equal(0, JgsPacking.Allocations - before);

            before = JgsPacking.Allocations;
            await using (await RunAsync("y = zeros(1, 64); x = y; x(1) = 1; x(2) = 2;"))
            {
            }

            Assert.Equal(1, JgsPacking.Allocations - before);
        }
    }

    [Fact]
    public async Task ABuiltinThatHandsItsArgumentBackIsSharedAtTheBinding()
    {
        // squeeze hands a vector back as it is; the binding takes a share, so the write detaches.
        await using JgsReplSession session = await RunAsync("v = ones(1, 3); w = squeeze(v); w(1) = 7;");
        JgsValue v = Get(session, "v");

        Assert.Equal(1, v.ElementAt(0).AsNumber);
        Assert.False(v.SharesStorageWith(Get(session, "w")));
    }

    [Fact]
    public async Task AUserFunctionsAnswerIsAdopted()
    {
        await using JgsReplSession session = await RunAsync(
            "x = mint_probe(); x(1) = 7;\n"
            + "function y = mint_probe()\n"
            + "y = ones(1, 4);\n"
            + "end\n");
        JgsValue x = Get(session, "x");

        Assert.Equal(7, x.ElementAt(0).AsNumber);
        Assert.Equal(1, JgsHolders.Of(x.ScopePayload));   // nobody else ever held it
    }

    [Fact]
    public async Task EveryMintingBuiltinIsRegisteredWithTheFlag()
    {
        foreach (string name in JgsBuiltins.MintingBuiltins)
        {
            await using JgsReplSession session = await RunAsync($"h = @{name};");
            JgsValue handle = Get(session, "h");
            Assert.True(
                handle.AsCallable is NamedHandle { Captured: BuiltinFunction { MintsAnswer: true } },
                $"'{name}' is on JgsBuiltins.MintingBuiltins but its builtin does not carry MintsAnswer");
        }
    }

    private static JgsValue Get(JgsReplSession session, string name)
    {
        Assert.True(session.Workspace.TryGet(name, out JgsValue value), $"'{name}' is not bound");
        return value;
    }
}
