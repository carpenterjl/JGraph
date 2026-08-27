namespace JGraph.Numerics;

/// <summary>
/// MATLAB's <c>sort</c> over packed column-major storage: every slice along one dimension put in
/// order, and the position each value came from when the caller asks for it. The order is the one
/// the boxed builtin produces — values compared with <c>&lt;</c>, so <c>-0</c> and <c>+0</c> tie;
/// equal values left in the order they arrived, so the sort is stable ascending and descending
/// alike; NaN taken out first and put back at whichever end <c>MissingPlacement</c> names, in the
/// order it arrived.
/// </summary>
/// <remarks>
/// <para>
/// A sort is not a fold, and that changes what determinism means here. A fold's answer depends on
/// the order its threads combine things in, which is why <see cref="ReduceKernels"/> pins its grain
/// boundaries to the shape and never to the thread count; a sort's answer is settled by its input
/// and its tie rule alone, so no schedule can move it. What has to be defended instead is
/// stability, and every step below either keeps arrival order among equal values or repairs it on
/// the spot.
/// </para>
/// <para>
/// One long slice is split on values rather than on positions. A strided sample picks the
/// splitters, one pass counts what falls in each bucket, one pass scatters the values into them —
/// block by block, so equal values keep arriving in order — and then every bucket is sorted on its
/// own thread with nothing to merge afterwards, because the buckets already sit in ascending order
/// end to end. The alternative, sorting fixed chunks and merging them pairwise, measured the same
/// on this machine while reading the whole array five more times; a partition reads it twice.
/// </para>
/// </remarks>
public static class SortKernels
{
    /// <summary>
    /// Elements in one slice at or above which the threads go inside it (512K ≈ 4 MB): below this a
    /// slice sorts in a few milliseconds on one core, and the partition's two extra passes would
    /// cost more than they save.
    /// </summary>
    public const int SliceThreshold = 1 << 19;

    /// <summary>How many elements a bucket is aimed at — a piece that sorts close to its core.</summary>
    private const int TargetBucketElements = 1 << 16;

    /// <summary>
    /// The most buckets one slice is cut into, which is written as the range of the byte that
    /// holds each value's bucket rather than as a number, because that is what fixes it: raise it
    /// past a byte and the cast in the counting pass wraps, and a wrapped bucket does not crash —
    /// it quietly answers a sorted-looking array that is not sorted. The size is right on its own
    /// terms too. A thread writes to every bucket at once, so the count is also how many pages
    /// each thread keeps open, and past a few hundred the scatter pays for address translation
    /// faster than the smaller buckets pay it back — measured slower at 128 and at 512 alike.
    /// </summary>
    private const int MaxBuckets = byte.MaxValue + 1;

    /// <summary>Sample values per bucket. Oversampling is what keeps the buckets even.</summary>
    private const int SampleFactor = 32;

    /// <summary>
    /// Sorts every slice along one dimension. <paramref name="positions"/> may be null when only the
    /// values are wanted; when it is not, each position is where that value sat in its own slice,
    /// already carrying <paramref name="indexBase"/>. <paramref name="missingFirst"/> is where NaN
    /// goes — the caller works it out from <c>MissingPlacement</c> and the direction, exactly as the
    /// boxed builtin does. <paramref name="values"/> and <paramref name="positions"/> must not be
    /// <paramref name="src"/>: the positions form re-reads each value from the source after the
    /// order is settled, so it needs the source still to say what it said.
    /// </summary>
    public static void SortAlong(
        NumericBuffer src, NumericBuffer values, NumericBuffer? positions,
        ReduceKernels.Split split, bool descending, bool missingFirst, double indexBase)
    {
        if (split.Count <= 0 || split.Slices <= 0)
        {
            return;
        }

        int slices = split.Slices;

        // Threads go across slices when there are enough slices to keep the machine busy, and
        // inside one slice when there are not. Which way round is chosen here is a question about
        // speed only: both roads end at the same array, because a sort's answer does not depend on
        // who did which part of it.
        if (slices >= ParallelKernels.MaxDegree)
        {
            int block = (int)Math.Clamp(
                ParallelKernels.GrainElements / (long)Math.Max(split.Count, 1), 1, slices);
            int blocks = ((slices - 1) / block) + 1;
            ParallelKernels.ForBlocks(blocks, split.Total >= ParallelKernels.MemoryBoundThreshold, b =>
            {
                // One scratch for the whole block rather than one per slice. A matrix sorted
                // along its rows has a slice per row, and a fresh pair of arrays for each of
                // eight thousand rows is hundreds of megabytes of garbage to hold a few
                // hundred kilobytes of live data.
                using var scratch = Scratch.For(split, positions is not null);
                int first = b * block;
                int last = Math.Min(first + block, slices);
                for (int s = first; s < last; s++)
                {
                    SortOneSlice(src, values, positions, split, s, descending, missingFirst,
                        indexBase, inside: false, scratch);
                }
            });
        }
        else
        {
            using var scratch = Scratch.For(split, positions is not null);
            for (int s = 0; s < slices; s++)
            {
                SortOneSlice(src, values, positions, split, s, descending, missingFirst,
                    indexBase, inside: true, scratch);
            }
        }

        GC.KeepAlive(src);
        GC.KeepAlive(values);
        GC.KeepAlive(positions);
    }

    private static void SortOneSlice(
        NumericBuffer src, NumericBuffer values, NumericBuffer? positions, ReduceKernels.Split split,
        int slice, bool descending, bool missingFirst, double indexBase, bool inside, Scratch scratch)
    {
        int inner = split.Inner;
        int n = split.Count;
        int at = ((slice / inner) * inner * n) + (slice % inner);
        if (inner == 1)
        {
            Contiguous(src, at, values, at, positions, at, n, descending, missingFirst, indexBase, inside);
            return;
        }

        // A strided slice is copied out, sorted where it lies contiguously, and written back with
        // the same stride. Sorting through the stride would put a multiply in every comparison and
        // every swap of an n log n loop, to save two passes.
        ManagedBuffer gathered = scratch.Gathered!;
        ManagedBuffer ordered = scratch.Ordered!;
        ManagedBuffer? placed = scratch.Placed;
        Span<double> source = src.AsSpan();
        Span<double> into = gathered.AsSpan();
        for (int j = 0; j < n; j++)
        {
            into[j] = source[at + (j * inner)];
        }

        Contiguous(gathered, 0, ordered, 0, placed, 0, n, descending, missingFirst, indexBase, inside);

        Span<double> sorted = ordered.AsSpan();
        Span<double> target = values.AsSpan();
        for (int j = 0; j < n; j++)
        {
            target[at + (j * inner)] = sorted[j];
        }

        if (placed is not null)
        {
            Span<double> where = placed.AsSpan();
            Span<double> report = positions!.AsSpan();
            for (int j = 0; j < n; j++)
            {
                report[at + (j * inner)] = where[j];
            }
        }
    }

    /// <summary>
    /// Room to gather one strided slice into and sort it, held for as long as a thread keeps
    /// taking slices. A contiguous layout needs none of it and allocates none of it.
    /// </summary>
    private sealed class Scratch : IDisposable
    {
        private Scratch(int n, bool positions)
        {
            if (n == 0)
            {
                return; // a contiguous layout sorts where it lies and gathers nothing
            }

            Gathered = ManagedBuffer.Adopt(new double[n]);
            Ordered = ManagedBuffer.Adopt(new double[n]);
            Placed = positions ? ManagedBuffer.Adopt(new double[n]) : null;
        }

        public ManagedBuffer? Gathered { get; }

        public ManagedBuffer? Ordered { get; }

        public ManagedBuffer? Placed { get; }

        public static Scratch For(ReduceKernels.Split split, bool positions) =>
            new(split.Inner == 1 ? 0 : split.Count, positions && split.Inner > 1);

        public void Dispose()
        {
            Gathered?.Dispose();
            Ordered?.Dispose();
            Placed?.Dispose();
        }
    }

    /// <summary>One contiguous slice, serially or split across threads by value.</summary>
    private static void Contiguous(
        NumericBuffer src, int srcAt, NumericBuffer dest, int destAt, NumericBuffer? pos, int posAt,
        int n, bool descending, bool missingFirst, double indexBase, bool inside)
    {
        if (inside && n >= SliceThreshold)
        {
            SplitByValue(src, srcAt, dest, destAt, pos, posAt, n, descending, missingFirst, indexBase);
            return;
        }

        Serial(
            src.AsSpan(srcAt, n), dest.AsSpan(destAt, n),
            pos is null ? default : pos.AsSpan(posAt, n), descending, missingFirst, indexBase);
        GC.KeepAlive(src);
        GC.KeepAlive(dest);
        GC.KeepAlive(pos);
    }

    /// <summary>
    /// One slice on one thread: the NaN taken to its end, everything else left in arrival order and
    /// then put in order where it lies.
    /// </summary>
    private static void Serial(
        ReadOnlySpan<double> input, Span<double> values, Span<double> places,
        bool descending, bool missingFirst, double indexBase)
    {
        int n = input.Length;
        int missing = 0;
        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(input[i]))
            {
                missing++;
            }
        }

        int finite = n - missing;
        int head = missingFirst ? missing : 0;
        int a = head;
        int b = missingFirst ? 0 : finite;
        for (int i = 0; i < n; i++)
        {
            double v = input[i];
            int slot = double.IsNaN(v) ? b++ : a++;
            values[slot] = v;
            if (!places.IsEmpty)
            {
                places[slot] = i;
            }
        }

        OrderRun(
            values.Slice(head, finite),
            places.IsEmpty ? default : places.Slice(head, finite),
            descending, repairZeros: true);
        Report(input, values, places, indexBase);
    }

    /// <summary>
    /// One slice across threads: splitters from a sample, a counting pass, a scattering pass, then
    /// every bucket sorted on its own. Nothing is merged afterwards — bucket <c>k</c> holds only
    /// values that belong before bucket <c>k + 1</c>, so the buckets laid end to end are the answer.
    /// </summary>
    private static void SplitByValue(
        NumericBuffer src, int srcAt, NumericBuffer dest, int destAt, NumericBuffer? pos, int posAt,
        int n, bool descending, bool missingFirst, double indexBase)
    {
        double[] cuts = Splitters(src, srcAt, n);
        int buckets = cuts.Length + 1;

        // Both signs of zero answer false to every <, so they always share a bucket whatever the
        // splitters are — and that is the one bucket whose order a comparison cannot settle.
        int zeros = descending ? buckets - 1 - BucketOf(cuts, 0.0) : BucketOf(cuts, 0.0);

        int blockSize = ParallelKernels.GrainElements;
        int blocks = ((n - 1) / blockSize) + 1;
        var ids = new byte[n];
        var counts = new int[blocks * buckets];
        var missing = new int[blocks];

        ParallelKernels.ForBlocks(blocks, true, b =>
        {
            int from = b * blockSize;
            Span<double> x = src.AsSpan(srcAt + from, Math.Min(blockSize, n - from));
            Span<int> local = counts.AsSpan(b * buckets, buckets);
            int nan = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i];
                if (double.IsNaN(v))
                {
                    nan++;
                    continue;
                }

                int k = BucketOf(cuts, v);
                if (descending)
                {
                    k = buckets - 1 - k;
                }

                ids[from + i] = (byte)k;
                local[k]++;
            }

            missing[b] = nan;
        });

        int absent = 0;
        foreach (int count in missing)
        {
            absent += count;
        }

        int finite = n - absent;
        int head = missingFirst ? absent : 0;

        // Bucket by bucket, block by block: the running total becomes each block's own cursor into
        // each bucket, which is what makes the scatter keep arrival order inside a bucket.
        var start = new int[buckets + 1];
        int running = head;
        for (int k = 0; k < buckets; k++)
        {
            start[k] = running;
            for (int b = 0; b < blocks; b++)
            {
                int slot = (b * buckets) + k;
                int held = counts[slot];
                counts[slot] = running;
                running += held;
            }
        }

        start[buckets] = running;

        var missingAt = new int[blocks];
        int nextMissing = missingFirst ? 0 : finite;
        for (int b = 0; b < blocks; b++)
        {
            missingAt[b] = nextMissing;
            nextMissing += missing[b];
        }

        ParallelKernels.ForBlocks(blocks, true, b =>
        {
            int from = b * blockSize;
            Span<double> x = src.AsSpan(srcAt + from, Math.Min(blockSize, n - from));
            Span<double> into = dest.AsSpan(destAt, n);
            Span<double> where = pos is null ? default : pos.AsSpan(posAt, n);
            Span<int> at = counts.AsSpan(b * buckets, buckets);
            int nan = missingAt[b];
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i];
                int slot = double.IsNaN(v) ? nan++ : at[ids[from + i]]++;
                into[slot] = v;
                if (!where.IsEmpty)
                {
                    where[slot] = from + i;
                }
            }
        });

        ParallelKernels.ForBlocks(buckets, true, k =>
        {
            int from = start[k];
            int count = start[k + 1] - from;
            if (count < 2)
            {
                return;
            }

            OrderRun(
                dest.AsSpan(destAt + from, count),
                pos is null ? default : pos.AsSpan(posAt + from, count),
                descending, repairZeros: k == zeros);
        });

        if (pos is not null)
        {
            ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (from, count) =>
                Report(src.AsSpan(srcAt, n), dest.AsSpan(destAt + from, count),
                    pos.AsSpan(posAt + from, count), indexBase));
        }

        GC.KeepAlive(src);
        GC.KeepAlive(dest);
        GC.KeepAlive(pos);
    }

    /// <summary>
    /// Puts one run of values in order: sorted ascending, turned round for <c>'descend'</c>, and
    /// then whichever repair the tie rule needs — arrival order for the positions, arrival order
    /// for the signs of zero.
    /// </summary>
    private static void OrderRun(
        Span<double> run, Span<double> places, bool descending, bool repairZeros)
    {
        if (run.Length < 2)
        {
            return;
        }

        // The signs have to be read while the run is still in arrival order, which is now.
        ulong[]? signs = null;
        int zeros = 0;
        if (repairZeros && places.IsEmpty)
        {
            signs = ZeroSigns(run, out zeros);
        }

        if (!Ascending(run))
        {
            if (places.IsEmpty)
            {
                run.Sort();
            }
            else
            {
                run.Sort(places);
            }
        }

        if (descending)
        {
            run.Reverse();
            if (!places.IsEmpty)
            {
                places.Reverse();
            }
        }

        if (!places.IsEmpty)
        {
            SettleTies(run, places);
        }
        else if (signs is not null)
        {
            RestoreZeros(signs, zeros, run);
        }
    }

    /// <summary>Whether a run of non-NaN values is already in ascending order.</summary>
    private static bool Ascending(ReadOnlySpan<double> run)
    {
        for (int i = 1; i < run.Length; i++)
        {
            if (run[i] < run[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Equal values back into the order they arrived in. A comparison sort is free to shuffle them
    /// and this one does; MATLAB's is not, and a script that reads the second output of <c>sort</c>
    /// as a permutation it can apply to another array depends on it.
    /// </summary>
    private static void SettleTies(ReadOnlySpan<double> run, Span<double> places)
    {
        int i = 0;
        while (i < run.Length)
        {
            int j = i + 1;
            while (j < run.Length && run[j] == run[i])
            {
                j++;
            }

            if (j - i > 1)
            {
                Span<double> tied = places.Slice(i, j - i);
                if (!Ascending(tied))
                {
                    tied.Sort();
                }
            }

            i = j;
        }
    }

    /// <summary>
    /// The signs of the zeros in a run, in the order they arrive, or null when they are all one
    /// sign and there is therefore nothing a sort could get wrong.
    /// </summary>
    private static ulong[]? ZeroSigns(ReadOnlySpan<double> run, out int zeros)
    {
        zeros = 0;
        bool minus = false;
        bool plus = false;
        for (int i = 0; i < run.Length; i++)
        {
            if (run[i] != 0)
            {
                continue;
            }

            zeros++;
            if (double.IsNegative(run[i]))
            {
                minus = true;
            }
            else
            {
                plus = true;
            }
        }

        if (!minus || !plus)
        {
            zeros = 0;
            return null;
        }

        var signs = new ulong[((zeros - 1) >> 6) + 1];
        int at = 0;
        for (int i = 0; i < run.Length; i++)
        {
            if (run[i] != 0)
            {
                continue;
            }

            if (double.IsNegative(run[i]))
            {
                signs[at >> 6] |= 1UL << (at & 63);
            }

            at++;
        }

        return signs;
    }

    /// <summary>
    /// Writes the remembered signs back over the zeros. <c>-0</c> and <c>+0</c> are the one pair of
    /// distinct doubles that compare equal, so they are the one pair a comparison sort may swap
    /// without noticing; a stable sort leaves them alone, and this is how that is kept. They are
    /// one contiguous run wherever the sort put them, being equal to each other and to nothing else.
    /// </summary>
    private static void RestoreZeros(ulong[] signs, int zeros, Span<double> run)
    {
        int from = 0;
        while (from < run.Length && run[from] != 0)
        {
            from++;
        }

        for (int k = 0; k < zeros; k++)
        {
            run[from + k] = (signs[k >> 6] & (1UL << (k & 63))) != 0 ? -0.0 : 0.0;
        }
    }

    /// <summary>
    /// Turns the positions a sort worked with into the answer: each value re-read from the slice it
    /// came from, so it keeps the bits it had — a NaN its payload, a zero its sign — and each
    /// position shifted into the dialect's own base.
    /// </summary>
    private static void Report(
        ReadOnlySpan<double> input, Span<double> values, Span<double> places, double indexBase)
    {
        if (places.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < places.Length; i++)
        {
            int at = (int)places[i];
            values[i] = input[at];
            places[i] = at + indexBase;
        }
    }

    /// <summary>
    /// The values that cut one slice into buckets, read off a strided sample. The stride is a
    /// function of the slice's length, so the same slice always picks the same splitters; the
    /// sample is oversampled well past the bucket count so that a lopsided cut stays unlikely, and
    /// repeated splitters are dropped rather than left to make empty buckets.
    /// </summary>
    private static double[] Splitters(NumericBuffer src, int at, int n)
    {
        int buckets = (int)Math.Clamp(
            ((long)n + TargetBucketElements - 1) / TargetBucketElements, 2, MaxBuckets);
        int wanted = Math.Min(n, buckets * SampleFactor);
        var sample = new double[wanted];
        Span<double> x = src.AsSpan(at, n);
        int stride = n / wanted;
        int taken = 0;
        for (int i = 0; i < wanted; i++)
        {
            double v = x[i * stride];
            if (!double.IsNaN(v))
            {
                sample[taken++] = v;
            }
        }

        if (taken == 0)
        {
            return [];
        }

        Array.Sort(sample, 0, taken);
        var cuts = new double[buckets - 1];
        int kept = 0;
        for (int i = 1; i < buckets; i++)
        {
            double cut = sample[(int)((long)i * taken / buckets)];
            if (kept == 0 || cut != cuts[kept - 1])
            {
                cuts[kept++] = cut;
            }
        }

        return cuts[..kept];
    }

    /// <summary>
    /// Which bucket a value belongs to: how many splitters it is at or past. Monotone in the value,
    /// so equal values always land together and a bucket's whole contents belong before the next
    /// bucket's — which is what makes concatenation the only merge this needs.
    /// </summary>
    private static int BucketOf(double[] cuts, double v)
    {
        int lo = 0;
        int hi = cuts.Length;
        while (lo < hi)
        {
            int mid = (int)(((uint)lo + (uint)hi) >> 1);
            if (v < cuts[mid])
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lo;
    }
}
