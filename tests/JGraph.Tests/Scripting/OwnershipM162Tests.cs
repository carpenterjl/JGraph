using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V1's contract (ADR 0162): a MATLAB-dialect binding shares its payload under a holder count
/// (M2), the first write through either name copies (M3), the check lives in the setter (M7), and
/// disposal frees only what nobody else can see (M6).
/// </summary>
/// <remarks>
/// Every test here asserts the storage, not the printed answer: which payload two names hold, how
/// many hold it, and how many buffers a write allocates. The printed answers are the parity
/// fixtures' business (<c>value_isolation_*</c>), and they would go on passing if the model were
/// implemented by copying everything — which is exactly what these tests exist to tell apart. The
/// allocator is scoped per test through <see cref="JgsPacking.Use"/>, so a count is this test's
/// own, and the lifetime tests force a native or mapped backend because a managed buffer's
/// <c>Dispose</c> is a no-op and would hide a use-after-free rather than fail on it.
/// </remarks>
[Collection("JG facade")]
public class OwnershipM162Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public OwnershipM162Tests() => JG.Reset();

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

    // ---- M1/M2: a binding shares, and the count says how many hold it -------------------------

    [Fact]
    public async Task BindingANameSharesOnePayloadUnderACountOfTwo()
    {
        await using JgsReplSession session = await RunAsync("a = [1 2 3]; b = a;");
        JgsValue a = Get(session, "a");
        JgsValue b = Get(session, "b");

        Assert.True(a.SharesStorageWith(b));
        Assert.NotSame(a, b); // M2: every entry holds its own wrapper over the one payload
        Assert.Equal(2, JgsHolders.Of(a.ScopePayload));
    }

    [Theory]
    [InlineData("a = [1 2 3];")]
    [InlineData("a = [1+2i 3];")]
    [InlineData("a = {1, 2};")]
    public async Task EveryPayloadKindSharesOnABinding(string setup)
    {
        await using JgsReplSession session = await RunAsync(setup + " b = a;");
        JgsValue a = Get(session, "a");

        Assert.True(a.SharesStorageWith(Get(session, "b")));
        Assert.Equal(2, JgsHolders.Of(Payload(a)));
    }

    [Fact]
    public async Task AStructArraySharesToo()
    {
        await using JgsReplSession session = await RunAsync("a = struct('f', {1, 2}); b = a;");
        JgsValue a = Get(session, "a");

        Assert.True(a.SharesStorageWith(Get(session, "b")));

        // Two: `struct(...)` is a call, and V1 shared every call's answer (three holders, the
        // third a temporary nobody could reach); V2.2's audit showed `struct` mints its answer,
        // so the binding adopts it and only the two names hold the array (ADR 0163).
        Assert.Equal(2, JgsHolders.Of(Payload(a)));
    }

    // ---- M3: a write detaches, and only the writer moves ---------------------------------------

    [Fact]
    public async Task AWriteGivesTheWriterItsOwnPayloadAndLeavesTheOtherAlone()
    {
        await using JgsReplSession session = await RunAsync("a = [1 2 3]; b = a; a(1) = 7;");
        JgsValue a = Get(session, "a");
        JgsValue b = Get(session, "b");

        Assert.False(a.SharesStorageWith(b));
        Assert.Equal(7, a.ElementAt(0).AsNumber);
        Assert.Equal(1, b.ElementAt(0).AsNumber);
        Assert.Equal(1, JgsHolders.Of(a.ScopePayload));
    }

    [Fact]
    public async Task OneCopyIsMadeHoweverManyTimesTheWriterWrites()
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Managed };
        using (JgsPacking.Use(allocator))
        {
            long before = JgsPacking.Allocations;
            await using (await RunAsync("a = ones(1, 64); b = a; a(1) = 7;"))
            {
            }

            long one = JgsPacking.Allocations - before;

            before = JgsPacking.Allocations;
            await using (await RunAsync("a = ones(1, 64); b = a; a(1) = 7; a(2) = 8; a(3) = 9; a(4) = 1;"))
            {
            }

            long four = JgsPacking.Allocations - before;

            // Three more writes through the same name allocate nothing: after the first copy the
            // payload is the writer's own, which is what counting buys over marking.
            Assert.Equal(one, four);
        }
    }

    [Fact]
    public async Task BothSidesWrittenAllocatesExactlyOneCopy()
    {
        // Packed storage only: in the boxed lane (JGRAPH_JGS_PACKED=0) an array owns no native,
        // mapped or counted buffer, so there is nothing here to measure.
        if (!JgsPacking.Enabled)
        {
            return;
        }

        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Managed };
        using (JgsPacking.Use(allocator))
        {
            long before = JgsPacking.Allocations;
            await using (await RunAsync("a = ones(1, 64); b = a;"))
            {
            }

            long shared = JgsPacking.Allocations - before;

            before = JgsPacking.Allocations;
            await using (await RunAsync("a = ones(1, 64); b = a; a(1) = 7; b(1) = 9;"))
            {
            }

            long written = JgsPacking.Allocations - before;

            // One copy: `ones(1, 64)` mints its answer, so the binding adopts it (V2.2's audit,
            // ADR 0163) and the two names are the payload's only holders — the first write
            // detaches one of them, the second writes in place. Under V1 alone this was two, the
            // temporary the call left behind counting as a third holder.
            Assert.Equal(shared + 1, written);
        }
    }

    [Fact]
    public async Task ANestedWriteDetachesEveryLevelAndNoOther()
    {
        await using JgsReplSession session = await RunAsync("c = {[1 2 3], [4 5 6]}; d = c; c{1}(1) = 7;");
        JgsValue c = Get(session, "c");
        JgsValue d = Get(session, "d");

        Assert.False(c.SharesStorageWith(d));                      // the slot array detached
        Assert.False(c.AsCell[0].SharesStorageWith(d.AsCell[0]));  // and the written slot
        Assert.True(c.AsCell[1].SharesStorageWith(d.AsCell[1]));   // the untouched slot did not
        Assert.Equal(7, c.AsCell[0].ElementAt(0).AsNumber);
        Assert.Equal(1, d.AsCell[0].ElementAt(0).AsNumber);
    }

    [Fact]
    public async Task AShallowContainerDetachSharesItsChildren()
    {
        await using JgsReplSession session = await RunAsync("c = {[1 2 3], [4 5 6]}; d = c; c{1} = 0;");
        JgsValue c = Get(session, "c");
        JgsValue d = Get(session, "d");

        // Replacing one slot detaches the slot array shallowly: the other slot is one payload with
        // two holders, not two copies.
        Assert.True(c.AsCell[1].SharesStorageWith(d.AsCell[1]));
        Assert.Equal(2, JgsHolders.Of(c.AsCell[1].ScopePayload));
    }

    [Fact]
    public async Task DemotingOneAliasLeavesTheOtherPacked()
    {
        await using JgsReplSession session = await RunAsync("a = [1 2 3]; b = a; a(2) = 'x';");
        JgsValue a = Get(session, "a");
        JgsValue b = Get(session, "b");

        Assert.False(a.SharesStorageWith(b));
        Assert.Equal(2, b.ElementAt(1).AsNumber);
    }

    [Fact]
    public async Task GrowthGivesTheGrowerItsOwnStorage()
    {
        await using JgsReplSession session = await RunAsync("a = [1 2 3]; b = a; a(5) = 9;");
        JgsValue a = Get(session, "a");
        JgsValue b = Get(session, "b");

        Assert.Equal(5, a.ArrayLength);
        Assert.Equal(3, b.ArrayLength);
        Assert.False(a.SharesStorageWith(b));
    }

    [Fact]
    public async Task AStructElementIsAPayloadOfItsOwn()
    {
        await using JgsReplSession session = await RunAsync("s = struct('f', {1, 2, 3}); t = s; s(2).f = 9;");
        JgsValue s = Get(session, "s");
        JgsValue t = Get(session, "t");

        Assert.Equal(9, s.AsStructArray.Elements[1]["f"].AsNumber);
        Assert.Equal(2, t.AsStructArray.Elements[1]["f"].AsNumber);

        // Only the written element was copied; the others are one dictionary with two holders.
        Assert.Same(s.AsStructArray.Elements[0], t.AsStructArray.Elements[0]);
        Assert.NotSame(s.AsStructArray.Elements[1], t.AsStructArray.Elements[1]);
    }

    // ---- M6: disposal frees only what nobody else can see ---------------------------------------

    [Theory]
    [InlineData(BufferMode.Native)]
    [InlineData(BufferMode.Mapped)]
    public async Task ClearingOneNameLeavesTheOtherReadable(BufferMode mode)
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = mode };
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = await RunAsync("a = ones(1, 8); b = a; clear a;");
            Assert.Equal(8, Sum(Get(session, "b")));
        }
    }

    [Theory]
    [InlineData(BufferMode.Native)]
    [InlineData(BufferMode.Mapped)]
    public async Task ClearingTheWorkspaceLeavesACellsChildReadable(BufferMode mode)
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = mode };
        using (JgsPacking.Use(allocator))
        {
            JgsReplSession session = NewSession();
            await using (session)
            {
                Assert.True((await session.ExecuteAsync(
                    "global G; x = ones(1, 8); G = {x}; y = G{1};", "", CancellationToken.None)).Success);
                JgsValue kept = Get(session, "y");

                Assert.True((await session.ExecuteAsync(
                    "clear variables", "", CancellationToken.None)).Success);

                // M6: the cell's child is still held by `y`, and `clear` never frees what someone
                // else can see — the count says two, whatever the walk reaches.
                Assert.Equal(8, Sum(kept));
            }
        }
    }

    [Fact]
    public async Task DisposingTwiceOverOneRetainedWrapperIsSafe()
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Native };
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = await RunAsync("a = ones(1, 8); b = a;");
            JgsValue kept = Get(session, "b");
            JgsRunner.DisposeBuffers([Get(session, "a")]);
            JgsRunner.DisposeBuffers([Get(session, "a")]);
            Assert.Equal(8, Sum(kept));
        }
    }

    [Fact]
    public async Task AnExposedPayloadIsNotFreedByTheWalk()
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Native };
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = await RunAsync("a = ones(1, 8);");
            JgsValue a = Get(session, "a");
            a.MarkExposed();
            JgsRunner.DisposeBuffers([a]);

            // M6: left to the finalizer, because the count cannot see a reader that never took a
            // counted share — a JGS binding, or a store that kept the caller's own wrapper.
            Assert.Equal(8, Sum(a));
        }
    }

    [Fact]
    public async Task AppdataSurvivesClearOnANativeBuffer()
    {
        var allocator = new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Native };
        using (JgsPacking.Use(allocator))
        {
            JgsReplSession session = NewSession();
            await using (session)
            {
                Assert.True((await session.ExecuteAsync(
                    "f = figure('Visible', 'off'); a = ones(1, 8); setappdata(f, 'k', a); clear a;",
                    "", CancellationToken.None)).Success);

                ScriptRunResult read = await session.ExecuteAsync(
                    "b = getappdata(f, 'k');", "", CancellationToken.None);
                Assert.True(read.Success, read.Message);

                // Appendix A #152: the store kept the caller's own wrapper, so the count reads one
                // while the figure still holds it. The exposed mark is what stops the free.
                Assert.Equal(8, Sum(Get(session, "b")));
            }
        }
    }

    // ---- the kill switch ------------------------------------------------------------------------

    [Fact]
    public async Task CopyingEagerlyAnswersTheSame()
    {
        bool previous = JgsOwnership.Enabled;
        try
        {
            JgsOwnership.Enabled = false;
            await using JgsReplSession session = await RunAsync("a = [1 2 3]; b = a; a(1) = 7;");
            Assert.Equal(7, Get(session, "a").ElementAt(0).AsNumber);
            Assert.Equal(1, Get(session, "b").ElementAt(0).AsNumber);
            Assert.False(Get(session, "a").SharesStorageWith(Get(session, "b")));
        }
        finally
        {
            JgsOwnership.Enabled = previous;
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static object Payload(JgsValue value) => value.Type switch
    {
        JgsType.Cell => value.AsCell,
        JgsType.Struct => value.AsStructArray,
        _ => value.IsPackedComplex ? value.AsPackedComplex : value.IsPacked ? value.AsBuffer : value.AsArray,
    };

    private static double Sum(JgsValue value)
    {
        double total = 0;
        for (int i = 0; i < value.ArrayLength; i++)
        {
            total += value.ElementAt(i).AsNumber;
        }

        return total;
    }

    private static JgsValue Get(JgsReplSession session, string name)
    {
        Assert.True(session.Workspace.TryGet(name, out JgsValue value), $"'{name}' is not bound");
        return value;
    }
}
