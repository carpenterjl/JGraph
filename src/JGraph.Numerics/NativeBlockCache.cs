using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// A short free list of the native blocks <see cref="NativeBuffer"/> has just given back, so the
/// next large array is written into memory the process has already touched (the every-row plan's
/// I5, ADR 0328; the design ADR 0157 kept for the day a consumer's numbers asked for it).
/// </summary>
/// <remarks>
/// <para>
/// A native block of tens of megabytes comes from the operating system as untouched pages, and the
/// first write to each page is a fault that zeroes it: a statement that makes an 80 MB answer pays
/// for twenty thousand of them, on one thread when the kernel that fills the answer is serial. A
/// script that clears one large array and makes the next (which is every script that works on
/// large arrays) can write the next one over the last.
/// </para>
/// <para>
/// The list is small and bounded: at most <see cref="MaxEntries"/> blocks and
/// <see cref="CapBytes"/> in all, the oldest evicted first; a block is reused only for a request
/// that fits it with no more than an eighth to spare; a block nobody has asked for within
/// <see cref="IdleSeconds"/> is returned to the operating system by a timer, so an idle session
/// holds nothing; and the allocator empties the list before it decides a request does not fit in
/// memory. A reused block holds whatever its last owner left, so <see cref="NativeBuffer"/> takes
/// one only for a destination that is written in full (the contract
/// <see cref="BufferAllocator.AllocateForOverwrite"/> already had); a zeroed request gets fresh
/// pages, which are zero for nothing.
/// <c>JGRAPH_BLOCK_CACHE=0</c> turns the list off; <c>JGRAPH_BLOCK_CACHE_MB</c> sets the cap.
/// </para>
/// </remarks>
public static unsafe class NativeBlockCache
{
    /// <summary>The most blocks the list holds.</summary>
    public const int MaxEntries = 8;

    /// <summary>Seconds a block waits unused before it goes back to the operating system.</summary>
    public const int IdleSeconds = 10;

    private static readonly object Gate = new();
    private static readonly nint[] Blocks = new nint[MaxEntries];
    private static readonly long[] Sizes = new long[MaxEntries];
    private static readonly long[] Stamps = new long[MaxEntries];
    private static int _count;
    private static long _heldBytes;
    private static long _reused;
    private static long _offered;
    private static Timer? _trimmer;

    /// <summary>Whether freed blocks are kept for reuse (<c>JGRAPH_BLOCK_CACHE</c>, on unless 0).</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_BLOCK_CACHE") != "0";

    /// <summary>
    /// The must-fail seam (<c>JGRAPH_BLOCK_POISON=1</c>, tests and diagnosis only): every native
    /// block handed out for overwriting, fresh or reused, is first filled with NaN, so a kernel
    /// that promised to write every element and did not shows it in its answer. A reused block
    /// holds its last owner's numbers, which can pass for an answer; a NaN cannot.
    /// </summary>
    public static bool Poison { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_BLOCK_POISON") == "1";

    /// <summary>The most bytes the list holds: a sixteenth of physical memory between 256 MB and
    /// 1 GB, or <c>JGRAPH_BLOCK_CACHE_MB</c>. What the list holds is added to the process's peak
    /// (d03's 50M-element arrays: 1.7 GB without the list, 2.6 GB with a 1 GB cap, the same eight
    /// reuses as a 2 GB cap gave at 3.7 GB), so the cap is where memory is traded for time.</summary>
    public static long CapBytes { get; set; } = ResolveCap();

    /// <summary>Bytes held now (diagnostics and tests).</summary>
    public static long HeldBytes => Interlocked.Read(ref _heldBytes);

    /// <summary>How many requests a held block has served (diagnostics and tests).</summary>
    public static long Reused => Interlocked.Read(ref _reused);

    /// <summary>How many freed blocks the list has been offered (diagnostics).</summary>
    public static long Offered => Interlocked.Read(ref _offered);

    /// <summary>
    /// A held block of at least <paramref name="bytes"/> and at most an eighth more, removed from
    /// the list, or null. <paramref name="capacity"/> is the block's own size, which whoever takes
    /// it must give back with it.
    /// </summary>
    internal static void* Take(long bytes, out long capacity)
    {
        capacity = 0;
        if (!Enabled || _count == 0)
        {
            return null;
        }

        lock (Gate)
        {
            int best = -1;
            for (int i = 0; i < _count; i++)
            {
                if (Sizes[i] >= bytes && Sizes[i] - bytes <= bytes >> 3 && (best < 0 || Sizes[i] < Sizes[best]))
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                return null;
            }

            void* block = (void*)Blocks[best];
            capacity = Sizes[best];
            RemoveAt(best);
            Interlocked.Increment(ref _reused);
            return block;
        }
    }

    /// <summary>
    /// Offers a block back. True when the list kept it (it is the list's to free from then on);
    /// false when the caller must free it.
    /// </summary>
    internal static bool Offer(void* block, long capacity)
    {
        if (!Enabled || capacity > CapBytes || capacity <= 0)
        {
            return false;
        }

        Interlocked.Increment(ref _offered);

        lock (Gate)
        {
            while (_count == MaxEntries || (_count > 0 && _heldBytes + capacity > CapBytes))
            {
                int oldest = 0;
                for (int i = 1; i < _count; i++)
                {
                    if (Stamps[i] < Stamps[oldest])
                    {
                        oldest = i;
                    }
                }

                NativeMemory.Free((void*)Blocks[oldest]);
                RemoveAt(oldest);
            }

            Blocks[_count] = (nint)block;
            Sizes[_count] = capacity;
            Stamps[_count] = Environment.TickCount64;
            _count++;
            Interlocked.Add(ref _heldBytes, capacity);
            _trimmer ??= new Timer(static _ => Trim(IdleSeconds * 1000L), null, 5000, 5000);
        }

        return true;
    }

    /// <summary>Frees every held block: the allocator's first move when memory is short.</summary>
    public static void Clear() => Trim(-1);

    /// <summary>Frees the blocks that have waited longer than <paramref name="olderThanMs"/>.</summary>
    internal static void Trim(long olderThanMs)
    {
        if (_count == 0)
        {
            return;
        }

        lock (Gate)
        {
            long now = Environment.TickCount64;
            for (int i = _count - 1; i >= 0; i--)
            {
                if (now - Stamps[i] > olderThanMs)
                {
                    NativeMemory.Free((void*)Blocks[i]);
                    RemoveAt(i);
                }
            }
        }
    }

    private static void RemoveAt(int index)
    {
        Interlocked.Add(ref _heldBytes, -Sizes[index]);
        _count--;
        Blocks[index] = Blocks[_count];
        Sizes[index] = Sizes[_count];
        Stamps[index] = Stamps[_count];
    }

    private static long ResolveCap()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable("JGRAPH_BLOCK_CACHE_MB"), out long megabytes)
            && megabytes >= 0)
        {
            return megabytes << 20;
        }

        long physical = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return Math.Clamp(physical / 16, 256L << 20, 1L << 30);
    }
}
