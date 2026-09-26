using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Z2 of the value-ownership plan (ADR 0173): an operator writes its answer into a fresh temporary
/// operand (Z2a), and a plain <c>v = v op E</c> writes into <c>v</c>'s own storage (Z2b), each
/// answering the bits the allocating roads answer. Every test here asserts the storage — which
/// buffer an answer landed in, how many were allocated, how many hold it — because the printed
/// answers are the <c>temporary_reuse</c> fixture's business and would pass if nothing were reused.
/// </summary>
/// <remarks>
/// The arrays are 70,000 elements, above <see cref="JgsReuse.MinElements"/>, so the roads apply
/// without moving the floor. The allocator is scoped per test through <see cref="JgsPacking.Use"/>,
/// so a count is this test's own. The switch is toggled only inside a test that compares the two
/// roads, and put back in a <c>finally</c>.
/// </remarks>
[Collection("JG facade")]
public class ArithmeticReuseM173Tests : IDisposable
{
    private const string Setup =
        "n = 70000; A = mod((1:n) * 7919, 1009) / 13 - 40; B = mod((1:n) * 104729, 997) / 7 + 2; s = 0.37; v0 = (1:n) / 7;";

    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-z2-reuse-" + Guid.NewGuid().ToString("N"));

    public ArithmeticReuseM173Tests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private static async Task Execute(JgsReplSession session, string code)
    {
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
    }

    private async Task<JgsReplSession> RunAsync(string code)
    {
        JgsReplSession session = NewSession();
        await Execute(session, code);
        return session;
    }

    private static JgsValue Get(JgsReplSession session, string name)
    {
        Assert.True(session.Workspace.TryGet(name, out JgsValue value), $"'{name}' is not bound");
        return value;
    }

    private static IDisposable CountingAllocator() =>
        JgsPacking.Use(new BufferAllocator(new GcMemoryInfo()) { Mode = BufferMode.Managed });

    /// <summary>Buffers allocated by <paramref name="code"/> on an already-set-up session.</summary>
    private static async Task<long> Allocations(JgsReplSession session, string code)
    {
        long before = JgsPacking.Allocations;
        await Execute(session, code);
        return JgsPacking.Allocations - before;
    }

    /// <summary>Every element's bits, in either storage (the boxed lane has no buffers, and no reuse roads, but the comparison still holds).</summary>
    private static long[] Bits(JgsValue value)
    {
        int count = value.ArrayLength;
        var bits = new long[count];
        for (int i = 0; i < count; i++)
        {
            bits[i] = BitConverter.DoubleToInt64Bits(value.ElementAt(i).AsNumber);
        }

        return bits;
    }

    private static bool Packed => JgsPacking.Enabled; // the boxed lane has no buffers to count

    // ---- Z2a: an operator writes into its fresh operand ---------------------------------------

    [Fact]
    public async Task AnOperatorWritesItsAnswerIntoAFreshTemporaryOperand()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup);

            // `v0 - A` mints one buffer; `* s` writes into it instead of minting a second.
            Assert.Equal(1, await Allocations(session, "w = (v0 - A) * s;"));
            Assert.Equal(1, await Allocations(session, "w = (s - A) .* B;"));
            Assert.Equal(1, await Allocations(session, "w = A - (v0 - A);"));

            // Three deep: one buffer per leaf operator that has no fresh operand, none for the rest.
            // (v0 - A) mints, * s reuses, + B reuses; (A + 1) mints; ./ reuses one of the two.
            Assert.Equal(2, await Allocations(session, "w = ((v0 - A) * s + B) ./ (A + 1);"));

            // A chain of + is folded pair by pair; every pair after the first writes into the sum.
            Assert.Equal(1, await Allocations(session, "w = v0 + A + B + v0;"));
        }
    }

    [Fact]
    public async Task AReusedAnswerIsAdoptedByTheBindingAsItsOwn()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " w = (v0 - A) * s;");
            JgsValue w = Get(session, "w");
            Assert.Equal(1, JgsHolders.Of(w.ScopePayload));
            Assert.False(w.SharesStorageWith(Get(session, "v0")));
            Assert.False(w.SharesStorageWith(Get(session, "A")));

            // Ownership transfer: the binding took the buffer, so the first write copies nothing.
            Assert.Equal(0, await Allocations(session, "w(1) = 0;"));
        }
    }

    [Fact]
    public async Task ATemporaryHeldByAScopeIsNotWrittenInto()
    {
        if (!Packed)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_folder, "z2_one.m"), "function y = z2_one()\ny = 1;\nend\n");
        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + $" addpath('{_folder.Replace('\\', '/')}');");

            // `(v0 - A) + z2_one()`: the left temporary is held as a share while the call runs
            // (M5), so it is a second holder's and the sum allocates; the answer is still owned.
            Assert.Equal(2, await Allocations(session, "w = (v0 - A) + z2_one();"));
            Assert.Equal(1, JgsHolders.Of(Get(session, "w").ScopePayload));
        }
    }

    // ---- Z2b: v = v op E in place ---------------------------------------------------------------

    [Theory]
    [InlineData("v = v - A;")]
    [InlineData("v = v + s;")]
    [InlineData("v = v .* B;")]
    [InlineData("v = v ./ B;")]
    [InlineData("v = v * s;")]
    [InlineData("v = v / s;")]
    [InlineData("v = A - v;")]
    [InlineData("v = s - v;")]
    [InlineData("v = s ./ v;")]
    [InlineData("v = s * v;")]
    [InlineData("v = A .* v;")]
    public async Task AnUpdateWritesIntoTheVariablesOwnStorage(string update)
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0;");
            JgsValue before = Get(session, "v");
            object? payload = before.ScopePayload;

            Assert.Equal(0, await Allocations(session, update));

            JgsValue after = Get(session, "v");
            Assert.Same(before, after);
            Assert.Same(payload, after.ScopePayload);
            Assert.Equal(1, JgsHolders.Of(payload));
        }
    }

    [Fact]
    public async Task TheUpdateLoopAllocatesOneBufferPerStep()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " zz = v0 + 0;");

            // `A * s` mints the one temporary; `zz - …` writes into zz. Two per step before Z2.
            Assert.Equal(100, await Allocations(session, "for i = 1:100, zz = zz - A * s; end"));
            Assert.Equal(1, JgsHolders.Of(Get(session, "zz").ScopePayload));
        }
    }

    [Fact]
    public async Task AnUpdateWhoseRightSideRunsACallStillWritesInPlaceOnceTheHoldIsReleased()
    {
        if (!Packed)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_folder, "z2_scaled.m"), "function y = z2_scaled(A, i)\ny = A * (i * 1e-3);\nend\n");
        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + $" addpath('{_folder.Replace('\\', '/')}'); zz = v0 + 0;");
            object? payload = Get(session, "zz").ScopePayload;

            // zz is held while the call runs (M5); the hold is this road's own and is released
            // before the count is read, so the update lands in zz's buffer: one buffer per step,
            // the callee's answer.
            Assert.Equal(10, await Allocations(session, "for i = 1:10, zz = zz - z2_scaled(A, i); end"));
            Assert.Same(payload, Get(session, "zz").ScopePayload);
        }
    }

    [Fact]
    public async Task AScaledUpdateIsOneFusedSweepWithNoTemporary()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            // Matrices, so the loop walks (the compiled vector road takes rows and columns only):
            // `zz - N * s` and `zz + s * N` never materialize the product (Z2d). `+ 0` gives zz a
            // payload of its own; reshape's answer shares v0's.
            await using JgsReplSession session = await RunAsync(
                Setup + " zz = reshape(v0, 700, 100) + 0; N = reshape(A, 700, 100);");
            object? payload = Get(session, "zz").ScopePayload;
            Assert.Equal(0, await Allocations(session, "for i = 1:100, zz = zz - N * (s * i); end"));
            Assert.Equal(0, await Allocations(session, "for i = 1:100, zz = zz + (s * i) * N; end"));
            Assert.Same(payload, Get(session, "zz").ScopePayload);

            // The product of two arrays is a matrix product, never fused: computed, then the update.
            Assert.True(await Allocations(session, "q = reshape(v0, 700, 100); q = q - N * (N' * N);") >= 1);
        }
    }

    // ---- every refusal takes the allocating road ---------------------------------------------

    [Fact]
    public async Task AnAliasedVariableIsNotUpdatedInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0; w = v;");
            long[] kept = Bits(Get(session, "w"));

            Assert.Equal(1, await Allocations(session, "v = v - A;"));
            Assert.Equal(kept, Bits(Get(session, "w")));
            Assert.False(Get(session, "v").SharesStorageWith(Get(session, "w")));
        }
    }

    [Fact]
    public async Task ACapturedVariableIsNotUpdatedInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0; f = @() v;");
            Assert.Equal(1, await Allocations(session, "v = v - A;"));
            await Execute(session, "kept = f();");
            Assert.Equal(Bits(Get(session, "v0")), Bits(Get(session, "kept")));
        }
    }

    [Fact]
    public async Task AVariableStillHeldByACellOrAFieldIsNotUpdatedInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0; c = {v}; st.f = v;");
            Assert.Equal(1, await Allocations(session, "v = v + 1;"));
            await Execute(session, "c1 = c{1}; sf = st.f;");
            Assert.Equal(Bits(Get(session, "v0")), Bits(Get(session, "c1")));
            Assert.Equal(Bits(Get(session, "v0")), Bits(Get(session, "sf")));
        }
    }

    [Fact]
    public async Task ARightSideThatRebindsTheVariableSendsTheUpdateToTheAllocatingRoad()
    {
        if (!Packed)
        {
            return;
        }

        File.WriteAllText(Path.Combine(_folder, "z2_bump.m"), "function y = z2_bump()\nglobal gv;\ngv = gv + 2;\ny = 1;\nend\n");
        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(
                Setup + $" addpath('{_folder.Replace('\\', '/')}'); global gv; gv = v0 + 0;");
            await Execute(session, "gv = gv + z2_bump();");

            // The sum read gv as it was (M5's hold), the callee rebound gv, and the answer is
            // v0 + 1 bound afresh — never a write into the payload the callee's answer holds.
            await Execute(session, "expected = v0 + 1; got = gv;");
            Assert.Equal(Bits(Get(session, "expected")), Bits(Get(session, "got")));
        }
    }

    [Fact]
    public async Task AnOperandThatIsTheVariableItselfIsNotWrittenInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0;");
            Assert.Equal(1, await Allocations(session, "v = v - v;"));
            Assert.Equal(0.0, Get(session, "v").ElementAt(5).AsNumber);
        }
    }

    [Fact]
    public async Task AVariableWithGrowthSlackIsNotWrittenInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            // A matrix grown by a row keeps its buffer with a column stride (M41's slack).
            await using JgsReplSession session = await RunAsync(Setup + " v = reshape(v0 + 0, 700, 100); v(701, 1) = 1;");
            Assert.True(Get(session, "v").HasGrowthCapacity);

            // Two: the read compacts the slack into a new buffer (as every read of a grown array
            // does), and the answer is allocated rather than written over either.
            Assert.Equal(2, await Allocations(session, "v = v - 1;"));
            Assert.False(Get(session, "v").HasGrowthCapacity);
            Assert.Equal(0.0, Get(session, "v").ElementAt(700).AsNumber); // (701, 1) - 1
            Assert.Equal(-1.0, Get(session, "v").ElementAt(701 * 100 - 1).AsNumber); // a zero-filled cell, less one
        }
    }

    [Fact]
    public async Task AnArrayBelowTheFloorOrAboveTheNoPollCeilingAllocates()
    {
        if (!Packed)
        {
            return;
        }

        int ceiling = JgsReuse.InPlaceNoPollElements;
        try
        {
            using (CountingAllocator())
            {
                await using JgsReplSession session = await RunAsync("small = (1:1000) / 7; big = (1:200000) / 7;");
                Assert.Equal(1, await Allocations(session, "small = small - 1;"));

                JgsReuse.InPlaceNoPollElements = 100_000;
                Assert.Equal(1, await Allocations(session, "big = big - 1;"));
            }
        }
        finally
        {
            JgsReuse.InPlaceNoPollElements = ceiling;
        }
    }

    [Fact]
    public async Task AClassedOrLogicalVariableIsNotWrittenInPlace()
    {
        if (!Packed)
        {
            return;
        }

        using (CountingAllocator())
        {
            await using JgsReplSession session = await RunAsync(Setup + " k = int32(v0); m = v0 > 100;");
            Assert.True(await Allocations(session, "k = k - 1;") >= 1);
            Assert.Equal(JgsNumericClass.Int32, Get(session, "k").NumericClass);
            Assert.True(await Allocations(session, "m = m + 1;") >= 1);
            Assert.Equal(JgsPackedKind.Number, Get(session, "m").PackedKind);
        }
    }

    [Fact]
    public async Task AJgsAliasKeepsItsValueWhenAMatlabScriptUpdatesTheVariable()
    {
        // c15: a JGS binding takes no counted share (M17) and marks the payload exposed (M6), so a
        // MATLAB `a = a - 1` on a payload sitting at one holder must still allocate.
        File.WriteAllText(Path.Combine(_folder, "z2_dec.m"), "a = a - 1;\n");
        string main = Path.Combine(_folder, "main.jgs");
        var context = new ScriptContext(_output, (_, _) => { }, _folder, resolvePath: null, figureFiles: new TestFigureFiles())
        {
            ScriptPath = main,
        };
        ScriptRunResult result = JgsRunner.Run(
            "let a = zeros(70000) + 1;\nlet b = a;\nrun(\"z2_dec.m\");\nprint(sum(b));\nprint(sum(a));\n",
            context, default, sourceId: main, hook: null, JgsDialect.Jgs);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("70000\n0", _output.NormalText.Trim().Replace("\r\n", "\n"));
    }

    // ---- the two roads answer the same bits ----------------------------------------------------

    [Fact]
    public async Task TheReusedAndAllocatingRoadsAnswerTheSameBits()
    {
        const string shapes = Setup + @"
v = v0; v = v - s * A; r1 = v;
v = v0; v = s - v; r2 = v;
v = v0; v = s ./ v; r3 = v;
v = v0; v = A .* v; v = v ./ B; v = v + s; r4 = v;
r5 = ((v0 - A) * s + B) ./ (A + 1);
r6 = ((s - A) .* B - (v0 - A))';
r7 = v0 + A + B + v0;
zz = v0; for i = 1:50, zz = zz - A * (s * i); end; r8 = zz;
q = v0; q(1:6) = [NaN Inf -Inf -0 1e-310 0]; q = q - 0; q = q * -1; r9 = q;";

        bool enabled = JgsReuse.Enabled;
        try
        {
            JgsReuse.Enabled = true;
            await using JgsReplSession reused = await RunAsync(shapes);
            JgsReuse.Enabled = false;
            await using JgsReplSession allocated = await RunAsync(shapes);

            for (int i = 1; i <= 9; i++)
            {
                Assert.Equal(Bits(Get(allocated, "r" + i)), Bits(Get(reused, "r" + i)));
            }
        }
        finally
        {
            JgsReuse.Enabled = enabled;
        }
    }

    [Fact]
    public async Task TheKillSwitchRestoresTheAllocatingRoads()
    {
        if (!Packed)
        {
            return;
        }

        bool enabled = JgsReuse.Enabled;
        try
        {
            JgsReuse.Enabled = false;
            using (CountingAllocator())
            {
                await using JgsReplSession session = await RunAsync(Setup + " v = v0 + 0;");
                Assert.Equal(2, await Allocations(session, "w = (v0 - A) * s;"));
                Assert.Equal(1, await Allocations(session, "v = v - A;"));
            }
        }
        finally
        {
            JgsReuse.Enabled = enabled;
        }
    }
}
