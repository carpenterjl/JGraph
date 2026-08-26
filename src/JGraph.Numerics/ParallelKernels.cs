using System.Runtime.ExceptionServices;
using JGraph.Numerics.LinearAlgebra.Native;

namespace JGraph.Numerics;

/// <summary>
/// The one place a managed kernel becomes several threads. Work is cut into fixed grains — the same
/// grains whatever the thread count, whatever the machine — so a threaded kernel answers what the
/// serial one answers, to the bit, and answers it identically on every run.
/// </summary>
/// <remarks>
/// <para>
/// Fixed grains are the whole discipline. A partitioner that hands out work by how fast the workers
/// happen to be running would make the split depend on the machine's mood; where an operation is
/// per-element that changes nothing, but the moment a fold is involved the answer would move with
/// the weather. Grain boundaries here are a function of length alone, so a reduction that combines
/// its grains in index order is bit-reproducible even though it ran on eight cores.
/// </para>
/// <para>
/// Cancellation keeps the contract <see cref="PackedMath"/> already had: the caller's poll runs
/// between grains, from whichever thread finished one, and the <see cref="OperationCanceledException"/>
/// it throws comes back out of <see cref="For"/> unwrapped — a cancelled statement must not reach
/// the interpreter dressed as an <see cref="AggregateException"/>.
/// </para>
/// </remarks>
public static class ParallelKernels
{
    /// <summary>
    /// Elements per grain (64K = 512 KB): a stretch that fits a core's own cache, and small enough
    /// that a two-million-element array has thirty-two pieces to hand out rather than two.
    /// </summary>
    /// <remarks>
    /// The grain is the ceiling on how many threads a given length can use, and the first size tried
    /// here — a million elements, a sub-multiple of <see cref="PackedMath.ChunkElements"/> — turned
    /// out to be that ceiling and nothing else: <c>x .^ 2</c> over 262,144 elements is three and a
    /// half milliseconds of <c>Math.Pow</c> and got not one thread, because it was one grain. At 64K
    /// the same array is four grains and four cores, and a 50M one is seven hundred and sixty-three
    /// pieces whose scheduling costs about a microsecond each.
    /// </remarks>
    public const int GrainElements = 1 << 16;

    /// <summary>
    /// Length at or above which an operation that spends its time moving memory is worth splitting
    /// (2M ≈ 16 MB): below it the array is close enough to the cores that the whole operation is
    /// tens of microseconds and there is nothing to win.
    /// </summary>
    public const int MemoryBoundThreshold = 1 << 21;

    /// <summary>
    /// Length at or above which an operation that spends its time computing is worth splitting
    /// (256K): a transcendental or a <c>Math.Pow</c> is tens of cycles per element, so there is real
    /// work to divide long before there is real memory traffic.
    /// </summary>
    public const int ComputeBoundThreshold = 1 << 18;

    /// <summary>
    /// Total elements at or above which a dimension reduction is worth splitting (4M): a reduction
    /// reads everything but writes almost nothing, so it is even more bandwidth-shaped than a copy,
    /// and below this the whole answer is under a millisecond on one core.
    /// </summary>
    public const int ReductionThreshold = 1 << 22;

    private static int _maxDegree = ResolveDegree();

    /// <summary>
    /// How many threads a kernel may use: <c>JGRAPH_THREADS</c> when set, otherwise one per logical
    /// processor capped at 16.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same knob, and the same environment variable, as the native side reads — one answer to
    /// "how much of this machine may JGraph's arithmetic take", whether the arithmetic is OpenBLAS's
    /// or ours. The <em>default</em> differs, and on purpose. <see cref="ProcessorTopology"/> records
    /// why a blocked factorization wants one thread per physical core: hyperthread siblings share
    /// the multiply-add units it saturates. These kernels do not saturate them. <c>Math.Pow</c> over
    /// fifty million elements is a chain of dependent latencies, and the sibling thread fills the
    /// stalls: 90.9 ms at eight threads against 83.2 ms at sixteen, and <c>sin</c>'s scalar loop
    /// 42.6 ms against 34.7 ms. The memory-bound kernels are within a few percent either way, so the
    /// wider count wins outright here as surely as the narrower one wins there.
    /// </para>
    /// <para>
    /// Settable because the count is something the tests have to be able to move: an answer that
    /// changes with it is a bug, and the only way to say so is to run the same kernel at one thread
    /// and at all of them.
    /// </para>
    /// </remarks>
    public static int MaxDegree
    {
        get => _maxDegree;
        set => _maxDegree = value >= 1
            ? Math.Min(value, 64)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "at least one thread");
    }

    /// <summary>
    /// Runs <paramref name="body"/> over <c>[0, length)</c> in grains of
    /// <see cref="GrainElements"/>, on several threads when there are at least
    /// <paramref name="threshold"/> elements and this machine has more than one core to give.
    /// <paramref name="betweenGrains"/> runs after each grain, on the thread that finished it.
    /// </summary>
    /// <param name="length">Total elements.</param>
    /// <param name="threshold">Length below which the work runs on the calling thread.</param>
    /// <param name="betweenGrains">The cancellation poll, or null.</param>
    /// <param name="body">Called with the start index and length of one grain.</param>
    public static void For(int length, int threshold, Action? betweenGrains, Action<int, int> body)
    {
        if (length <= 0)
        {
            return;
        }

        int grains = ((length - 1) / GrainElements) + 1;
        if (grains == 1 || length < threshold || MaxDegree == 1)
        {
            for (int g = 0; g < grains; g++)
            {
                int start = g * GrainElements;
                body(start, Math.Min(GrainElements, length - start));
                betweenGrains?.Invoke();
            }

            return;
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDegree };
        try
        {
            Parallel.For(0, grains, options, g =>
            {
                int start = g * GrainElements;
                body(start, Math.Min(GrainElements, length - start));
                betweenGrains?.Invoke();
            });
        }
        catch (AggregateException bundled)
        {
            ExceptionDispatchInfo.Capture(Unwrap(bundled)).Throw();
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> once per block index in <c>[0, blocks)</c> — the shape a kernel
    /// takes when its work does not cut into equal element grains: a reduction hands out whole
    /// slices, and how many slices make one block is the kernel's own arithmetic. The caller also
    /// decides <paramref name="parallel"/>, because only it knows how many elements a block touches;
    /// a false there, one block, or one thread all run serially in block order.
    /// </summary>
    /// <remarks>
    /// The determinism rule is the caller's to keep: every block must own its outputs outright, and
    /// the block boundaries must be a function of the problem's shape alone — never of the thread
    /// count, which this method deliberately has no way to leak.
    /// </remarks>
    public static void ForBlocks(int blocks, bool parallel, Action<int> body)
    {
        if (blocks <= 0)
        {
            return;
        }

        if (!parallel || blocks == 1 || MaxDegree == 1)
        {
            for (int b = 0; b < blocks; b++)
            {
                body(b);
            }

            return;
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDegree };
        try
        {
            Parallel.For(0, blocks, options, body);
        }
        catch (AggregateException bundled)
        {
            ExceptionDispatchInfo.Capture(Unwrap(bundled)).Throw();
        }
    }

    /// <summary>
    /// The exception a caller should see: one exception, the way the serial loop this replaced threw
    /// one.
    /// </summary>
    /// <remarks>
    /// Every grain is running the same body over a different stretch of the same buffer, so several
    /// grains failing is one failure observed several times, not several failures — a caller that
    /// used to get the one exception its loop raised must not start getting a bundle because the
    /// array happened to be big enough to split. Cancellation is picked out first
    /// because it is the one failure every grain really does raise at once.
    /// </remarks>
    private static Exception Unwrap(AggregateException bundled)
    {
        AggregateException flat = bundled.Flatten();
        foreach (Exception inner in flat.InnerExceptions)
        {
            if (inner is OperationCanceledException cancelled)
            {
                return cancelled;
            }
        }

        return flat.InnerExceptions.Count > 0 ? flat.InnerExceptions[0] : flat;
    }

    private static int ResolveDegree()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("JGRAPH_THREADS"), out int asked) && asked > 0)
        {
            return Math.Min(asked, 64);
        }

        return Math.Clamp(Environment.ProcessorCount, 1, 16);
    }
}
