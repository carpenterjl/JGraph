using System.Runtime.CompilerServices;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M6's resource bound (ADR 0162): a payload the model leaves to the finalizer — one carrying the
/// exposed mark, or one a second holder let go of — is reclaimed when the allocator needs the
/// room, not when the managed heap happens to fill.
/// </summary>
/// <remarks>
/// The exposed mark defers a free rather than losing it: a JGS binding and <c>setappdata</c> keep
/// the caller's own wrapper, so the disposal walk must leave the payload alone, and only the
/// collector can tell when nobody reads it any more. A native buffer registers memory pressure and
/// so is collected in time; a mapped file holds disk, handles and address space that no memory
/// figure counts. The allocator therefore keeps two budgets and, when a request would cross the
/// one for the backend it is headed for, collects once and asks again. Every loop here runs
/// <b>without</b> a forced collection per iteration, because that is the case the budget exists
/// for; the fakes make the budgets small enough to be crossed within a test. The scripts allocate
/// through roads that reach the allocator — a JGS <c>ones(n)</c>, a MATLAB arithmetic result —
/// because the MATLAB constructors build a managed array and adopt it, which no budget governs.
/// </remarks>
[Collection("JG facade")]
public class ExposedReclamationM162Tests : IDisposable
{
    private const int Elements = 1 << 16;                 // 512 KB a buffer
    private const long Bytes = Elements * sizeof(double);
    private const int Iterations = 200;

    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "JGraphTests", "exposed-" + Guid.NewGuid().ToString("N"));

    public ExposedReclamationM162Tests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        GC.Collect();
        GC.WaitForPendingFinalizers(); // release any mapped file a finalizer still owns
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeMemory : IMemoryInfo
    {
        public long TotalPhysicalBytes { get; set; }
        public long MemoryLoadBytes { get; set; }
    }

    private sealed class FakeDisk : IDiskInfo
    {
        public long Free { get; set; }
        public long AvailableFreeBytes(string directory) => Free;
    }

    /// <summary>An automatic allocator whose native budget holds exactly <paramref name="buffers"/> of <see cref="Elements"/>.</summary>
    private BufferAllocator NativeBudgetOf(int buffers) =>
        new(new FakeMemory { TotalPhysicalBytes = buffers * Bytes }, new FakeDisk { Free = long.MaxValue })
        {
            ManagedMaxElements = 0,
            NativeHeadroomFraction = 1,
            MinFreeReserveBytes = 0,
            MappedDirectory = _folder,
            MappedReserveBytes = 0,
        };

    /// <summary>A mapped allocator whose disk budget holds exactly <paramref name="buffers"/> of <see cref="Elements"/>.</summary>
    private BufferAllocator MappedBudgetOf(int buffers, int maxFiles = int.MaxValue) =>
        new(new FakeMemory { TotalPhysicalBytes = 0 }, new FakeDisk { Free = buffers * Bytes })
        {
            Mode = BufferMode.Mapped,
            MappedDirectory = _folder,
            MappedReserveBytes = 0,
            MappedMaxFiles = maxFiles,
        };

    // ---- churn without a forced collection -----------------------------------------------------

    [Fact]
    public async Task AMatlabClearLoopOverNativeBuffersStaysUnderTheNativeBudget()
    {
        BufferAllocator allocator = NativeBudgetOf(4);
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = MatlabSession();
            await Run(session, "f = figure('Visible', 'off');");
            for (int i = 0; i < Iterations; i++)
            {
                await Run(session, $"a = zeros(1, {Elements}) + 1; setappdata(f, 'k', a); clear a;");
                Assert.True(allocator.OutstandingNativeBytes <= 4 * Bytes,
                    $"iteration {i}: {allocator.OutstandingNativeBytes} native bytes outstanding");
                Assert.Equal(0, allocator.LiveMappedFiles); // reclaimed in place, never spilled to disk
            }

            await Run(session, "b = getappdata(f, 'k');");
            JgsValue kept = Get(session, "b");
            Assert.Equal(BufferKind.Native, kept.AsBuffer.Kind);
            Assert.Equal(Elements, Sum(kept)); // the retained reader still reads
            Assert.True(allocator.Collections > 0, "the budget was never crossed, so nothing was tested");
        }
    }

    [Fact]
    public async Task AJgsBindingClearedByAScriptItRunsStaysUnderTheNativeBudget()
    {
        File.WriteAllText(Path.Combine(_folder, "clear_vars.m"), "clear a\n");
        BufferAllocator allocator = NativeBudgetOf(4);
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = JgsSession();
            await Run(session, "let f = figure(\"Visible\", \"off\")");
            for (int i = 0; i < Iterations; i++)
            {
                // M17: the JGS binding keeps the wrapper uncounted, so `a` is exposed twice over —
                // by the binding and by the store — and the walk that `clear a` runs frees nothing.
                await Run(session, $"let a = ones({Elements})\nsetappdata(f, \"k\", a)\nrun(\"clear_vars.m\")");
                Assert.True(allocator.OutstandingNativeBytes <= 4 * Bytes,
                    $"iteration {i}: {allocator.OutstandingNativeBytes} native bytes outstanding");
                Assert.False(session.Workspace.TryGet("a", out _), "the script it ran did not clear the binding");
            }

            await Run(session, "let b = getappdata(f, \"k\")");
            JgsValue kept = Get(session, "b");
            Assert.Equal(BufferKind.Native, kept.AsBuffer.Kind);
            Assert.Equal(Elements, Sum(kept));
            Assert.True(allocator.Collections > 0, "the budget was never crossed, so nothing was tested");
        }
    }

    [Fact]
    public async Task AMatlabClearLoopOverMappedBuffersStaysUnderTheDiskAndFileBudgets()
    {
        BufferAllocator allocator = MappedBudgetOf(4, maxFiles: 3);
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = MatlabSession();
            await Run(session, "f = figure('Visible', 'off');");
            for (int i = 0; i < Iterations; i++)
            {
                await Run(session, $"a = zeros(1, {Elements}) + 1; setappdata(f, 'k', a); clear a;");
                Assert.True(allocator.OutstandingMappedBytes <= 4 * Bytes,
                    $"iteration {i}: {allocator.OutstandingMappedBytes} mapped bytes outstanding");
                Assert.True(allocator.LiveMappedFiles <= 3,
                    $"iteration {i}: {allocator.LiveMappedFiles} backing files open");
                Assert.True(Directory.EnumerateFiles(_folder, "*" + MappedBuffer.FileExtension).Count() <= 3,
                    $"iteration {i}: more backing files on disk than the ceiling");
            }

            await Run(session, "b = getappdata(f, 'k');");
            JgsValue kept = Get(session, "b");
            Assert.Equal(BufferKind.Mapped, kept.AsBuffer.Kind);
            Assert.Equal(Elements, Sum(kept));
            Assert.True(allocator.Collections > 0, "the budget was never crossed, so nothing was tested");
        }
    }

    // ---- the two budgets, each with live readers the collection may not touch -----------------

    [Fact]
    public void ANativeRequestOverExhaustedHeadroomIsServedMappedNotRefused()
    {
        BufferAllocator allocator = NativeBudgetOf(4);
        var held = new List<NumericBuffer>();
        for (int i = 0; i < 4; i++)
        {
            NumericBuffer buffer = allocator.Allocate(Elements);
            buffer.AsSpan()[0] = i + 1;
            held.Add(buffer);
        }

        Assert.All(held, b => Assert.Equal(BufferKind.Native, b.Kind));
        Assert.Equal(4 * Bytes, allocator.OutstandingNativeBytes);

        // The readers are live, so the one collection frees nothing, and the request goes to disk.
        using NumericBuffer fifth = allocator.Allocate(Elements);
        Assert.Equal(BufferKind.Mapped, fifth.Kind);
        Assert.Equal(1, allocator.Collections);
        Assert.Equal(4 * Bytes, allocator.OutstandingNativeBytes);
        for (int i = 0; i < held.Count; i++)
        {
            Assert.Equal(i + 1, held[i].AsSpan()[0]);
        }

        held.ForEach(b => b.Dispose());
    }

    [Fact]
    public void AMappedRequestOverTheDiskBudgetCollectsOnceThenRefusesInMatlabsWords()
    {
        BufferAllocator allocator = MappedBudgetOf(2);
        using NumericBuffer first = allocator.Allocate(Elements);
        first.AsSpan()[0] = 1;
        AllocateAndDrop(allocator);
        Assert.Equal(2, allocator.LiveMappedFiles);

        // The dropped buffer is unreachable but not yet finalized: the request's one collection
        // is what frees it, and the request then fits.
        using NumericBuffer third = allocator.Allocate(Elements);
        Assert.Equal(BufferKind.Mapped, third.Kind);
        Assert.Equal(1, allocator.Collections);
        Assert.Equal(2, allocator.LiveMappedFiles);
        Assert.Equal(1, first.AsSpan()[0]);

        // Both readers live: the collection frees nothing, and the refusal is MATLAB's.
        var refused = Assert.Throws<OutOfMemoryException>(() => allocator.Allocate(Elements));
        Assert.Equal(BufferAllocator.OutOfMemoryMessage, refused.Message);
        Assert.Equal(2, allocator.Collections);
        Assert.Equal(1, first.AsSpan()[0]);  // every live reader still reads
        Assert.Equal(0, third.AsSpan()[0]);
        Assert.Equal(2, allocator.LiveMappedFiles);
    }

    [Fact]
    public void AMappedRequestOverTheFileCeilingCollectsOnceThenRefuses()
    {
        BufferAllocator allocator = MappedBudgetOf(100, maxFiles: 2);
        NumericBuffer first = allocator.Allocate(Elements);
        using NumericBuffer second = allocator.Allocate(Elements);

        var refused = Assert.Throws<OutOfMemoryException>(() => allocator.Allocate(Elements));
        Assert.Equal(BufferAllocator.OutOfMemoryMessage, refused.Message);
        Assert.Equal(1, allocator.Collections);
        Assert.Equal(2, allocator.LiveMappedFiles);

        first.Dispose();
        Assert.Equal(1, allocator.LiveMappedFiles);
        using NumericBuffer third = allocator.Allocate(Elements); // within the ceiling: no collection
        Assert.Equal(1, allocator.Collections);
    }

    [Fact]
    public async Task ARefusedAllocationReachesTheScriptAsMatlabsErrorAndLeavesTheWorkspaceWhole()
    {
        BufferAllocator allocator = MappedBudgetOf(1);
        using (JgsPacking.Use(allocator))
        {
            await using JgsReplSession session = MatlabSession();
            await Run(session, $"a = zeros(1, {Elements}) + 1;");
            ScriptRunResult result = await session.ExecuteAsync(
                $"b = zeros(1, {Elements}) + 1;", "", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains(BufferAllocator.OutOfMemoryMessage, result.Message);
            Assert.DoesNotContain("Internal error", result.Message); // MATLAB:nomem is an ordinary error
            Assert.Equal(Elements, Sum(Get(session, "a")));
            Assert.False(session.Workspace.TryGet("b", out _));

            // ...which a script may catch.
            await Run(session, $"try, c = zeros(1, {Elements}) + 1; catch e, id = e.identifier; msg = e.message; end");
            Assert.Equal("MATLAB:nomem", Get(session, "id").AsString);
            Assert.Equal(BufferAllocator.OutOfMemoryMessage, Get(session, "msg").AsString);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Allocates a buffer and lets it go, in a frame of its own so no local keeps it alive.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAndDrop(BufferAllocator allocator)
    {
        NumericBuffer dropped = allocator.Allocate(Elements);
        dropped.AsSpan()[0] = 2;
    }

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), _folder);

    private JgsReplSession MatlabSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private JgsReplSession JgsSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new JgsScriptEngine()).CreateSession(Context()));

    private static async Task Run(JgsReplSession session, string code)
    {
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
    }

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
