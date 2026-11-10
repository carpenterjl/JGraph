using System.Runtime.CompilerServices;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I6 (ADR 0327): <c>unique</c>, the set operations, <c>ismember</c> and
/// <c>accumarray</c> over packed doubles read where they lie, and the table that answers them when
/// the keys are whole numbers in a narrow range.
/// </summary>
/// <remarks>
/// <para>
/// The general roads box one <see cref="JgsValue"/> per element and settle every question with a
/// comparison delegate, which is right for any mix of kinds and is what a two-million-element
/// <c>unique</c> spent nearly all its time on. Keys that are whole numbers no wider apart than the
/// input is long need no comparison at all: one table over the range says where each value first
/// (or last) appears, an ascending walk of the table is the sorted answer, and a second pass reads
/// every element's place in it.
/// </para>
/// <para>
/// A table is taken only for inputs it answers exactly as the general road does. NaN, a negative
/// zero and anything that is not a whole number send the call back: the general road has rules
/// about each (a NaN is its own group, the two zeros are told apart by <c>unique</c> and not by
/// the set operations), and one home for those rules is worth more than a fast road for them.
/// </para>
/// <para>
/// <c>JGRAPH_SET_FAST=0</c> turns every I6 road off, which is what the measurement's off condition
/// and the tests use.
/// </para>
/// </remarks>
internal static class JgsSetKernels
{
    /// <summary>Whether the I6 roads are taken (<c>JGRAPH_SET_FAST</c>, on unless it is <c>0</c>).</summary>
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_SET_FAST") != "0";

    /// <summary>The narrowest range a table is always allowed: 64K keys is a quarter-megabyte of table.</summary>
    private const int FreeSpan = 1 << 16;

    /// <summary>Elements from which the scans and the fills go across threads.</summary>
    private const int ParallelFrom = 1 << 17;

    private static long _roads;

    /// <summary>How many calls have taken an I6 road (the tests read it to see a road taken).</summary>
    internal static long Roads => Interlocked.Read(ref _roads);

    /// <summary>Counts one call that took a road.</summary>
    internal static void Took() => Interlocked.Increment(ref _roads);

    /// <summary>
    /// Whether a value is a packed array of plain doubles — no logical kind, no integer class, no
    /// char, time or categorical tag, no higher dimensions and no growth slack — which is the only
    /// storage these roads read.
    /// </summary>
    internal static bool IsPlainDoubles(JgsValue value) =>
        JgsPacking.Enabled
        && value.Type == JgsType.Array
        && value.IsPacked
        && value.PackedKind == JgsPackedKind.Number
        && value.NumericClass == JgsNumericClass.Double
        && !value.IsCharMatrix
        && !value.IsStringArray
        && !value.IsTime
        && !value.IsCategorical
        && !value.IsNd
        && !value.HasGrowthCapacity;

    /// <summary>
    /// The least and greatest of <paramref name="values"/> when every one is a whole number an
    /// <see cref="int"/> holds and none is a negative zero; false otherwise (NaN and the
    /// infinities included). An empty span is whole numbers with an empty range.
    /// </summary>
    internal static bool TryWholeRange(NumericBuffer values, out int min, out int max)
    {
        int n = values.Length;
        min = 0;
        max = -1;
        if (n == 0)
        {
            return true;
        }

        int blockSize = ParallelKernels.GrainElements;
        int blocks = ((n - 1) / blockSize) + 1;
        var lows = new int[blocks];
        var highs = new int[blocks];
        var whole = new bool[blocks];
        ParallelKernels.ForBlocks(blocks, n >= ParallelFrom, b =>
        {
            int from = b * blockSize;
            whole[b] = WholeRange(values.AsSpan().Slice(from, Math.Min(blockSize, n - from)), out lows[b], out highs[b]);
        });
        GC.KeepAlive(values);

        min = int.MaxValue;
        max = int.MinValue;
        for (int b = 0; b < blocks; b++)
        {
            if (!whole[b])
            {
                return false;
            }

            min = Math.Min(min, lows[b]);
            max = Math.Max(max, highs[b]);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool WholeRange(ReadOnlySpan<double> values, out int min, out int max)
    {
        int lo = int.MaxValue;
        int hi = int.MinValue;
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];

            // The comparison is what proves the cast: whatever an out-of-range or NaN conversion
            // answers, it is not equal to the double it came from. Int32.MinValue is left out so
            // the range arithmetic below never has to think about it.
            int k = (int)v;
            if (k != v || k == int.MinValue || (k == 0 && double.IsNegative(v)))
            {
                min = 0;
                max = -1;
                return false;
            }

            lo = Math.Min(lo, k);
            hi = Math.Max(hi, k);
        }

        min = lo;
        max = hi;
        return true;
    }

    /// <summary>Whether a table over <c>[min, max]</c> is worth having for <paramref name="count"/> keys.</summary>
    internal static bool TableFits(int min, int max, long count) =>
        max >= min && (long)max - min + 1 <= Math.Max(FreeSpan, count);

    // --- unique -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[C, ia, ic] = unique(A)</c> over whole numbers in a narrow range: the distinct values
    /// (ascending, or in order of first appearance under <paramref name="stable"/>), the 0-based
    /// position each is taken from (its first, or its last under <paramref name="last"/>), and —
    /// already carrying <paramref name="indexBase"/> — where every input landed, made only when
    /// <paramref name="wantInto"/> asks for it.
    /// </summary>
    internal static bool TryUniqueWhole(
        NumericBuffer values, bool stable, bool last, int indexBase, bool wantInto,
        out double[] distinct, out int[] from, out double[] into)
    {
        distinct = [];
        from = [];
        into = [];
        int n = values.Length;
        if (!TryWholeRange(values, out int min, out int max) || !TableFits(min, max, n))
        {
            return false;
        }

        // One slot per key in the range: 0 while the key has not been seen, else one more than the
        // position that names its group; rewritten below as the group's place in the answer.
        var table = new int[max - min + 1];
        int groups = MarkOccurrences(values.AsSpan(), table, min, last);
        distinct = new double[groups];
        from = new int[groups];
        if (stable)
        {
            // In order of first appearance: the groups sorted by the position that names them,
            // which under 'stable' is always the first ('stable' with 'last' is refused upstream).
            var keys = new int[groups];
            int g = 0;
            for (int k = 0; k < table.Length; k++)
            {
                if (table[k] != 0)
                {
                    from[g] = table[k] - 1;
                    keys[g] = k;
                    g++;
                }
            }

            Array.Sort(from, keys);
            for (int place = 0; place < groups; place++)
            {
                distinct[place] = (double)min + keys[place];
                table[keys[place]] = place;
            }
        }
        else
        {
            int g = 0;
            for (int k = 0; k < table.Length; k++)
            {
                if (table[k] != 0)
                {
                    from[g] = table[k] - 1;
                    distinct[g] = (double)min + k;
                    table[k] = g;
                    g++;
                }
            }
        }

        if (wantInto)
        {
            var places = new double[n];
            int blockSize = ParallelKernels.GrainElements;
            int blocks = ((n - 1) / blockSize) + 1;
            ParallelKernels.ForBlocks(blocks, n >= ParallelFrom, b =>
            {
                int start = b * blockSize;
                int length = Math.Min(blockSize, n - start);
                PlacesOf(values.AsSpan().Slice(start, length), places.AsSpan(start, length), table, min, indexBase);
            });
            into = places;
        }

        GC.KeepAlive(values);
        return true;
    }

    /// <summary>Marks each key's first (or last) position, one more than it; answers how many keys were seen.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int MarkOccurrences(ReadOnlySpan<double> values, int[] table, int min, bool last)
    {
        int groups = 0;
        if (last)
        {
            for (int i = 0; i < values.Length; i++)
            {
                ref int slot = ref table[(int)values[i] - min];
                if (slot == 0)
                {
                    groups++;
                }

                slot = i + 1;
            }
        }
        else
        {
            for (int i = 0; i < values.Length; i++)
            {
                ref int slot = ref table[(int)values[i] - min];
                if (slot == 0)
                {
                    slot = i + 1;
                    groups++;
                }
            }
        }

        return groups;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void PlacesOf(ReadOnlySpan<double> values, Span<double> places, int[] table, int min, int indexBase)
    {
        for (int i = 0; i < values.Length; i++)
        {
            places[i] = table[(int)values[i] - min] + indexBase;
        }
    }

    // --- union, intersect, setdiff, setxor --------------------------------------------------------

    /// <summary>
    /// One of the four set operations over two lists of whole numbers in a narrow shared range, in
    /// sorted order: the values, and the 0-based position on each side every value is taken from
    /// (its first), exactly as the general road names them — <c>intersect</c> names both sides for
    /// every value, the others whichever side a value came from.
    /// </summary>
    internal static bool TrySetWhole(
        string name, NumericBuffer left, NumericBuffer right,
        out double[] values, out int[] fromLeft, out int[] fromRight)
    {
        values = [];
        fromLeft = [];
        fromRight = [];
        if (!TryWholeRange(left, out int minA, out int maxA) || !TryWholeRange(right, out int minB, out int maxB))
        {
            return false;
        }

        int min = left.Length == 0 ? minB : right.Length == 0 ? minA : Math.Min(minA, minB);
        int max = left.Length == 0 ? maxB : right.Length == 0 ? maxA : Math.Max(maxA, maxB);
        if (left.Length + right.Length == 0 || !TableFits(min, max, (long)left.Length + right.Length))
        {
            return false;
        }

        var inLeft = new int[max - min + 1];
        var inRight = new int[max - min + 1];
        MarkOccurrences(left.AsSpan(), inLeft, min, last: false);
        MarkOccurrences(right.AsSpan(), inRight, min, last: false);
        GC.KeepAlive(left);
        GC.KeepAlive(right);

        bool both = name == "intersect";
        bool rightToo = name is "union" or "setxor";
        bool leftAlone = name is "setdiff" or "setxor";
        var picked = new List<double>();
        var lefts = new List<int>();
        var rights = new List<int>();
        for (int k = 0; k < inLeft.Length; k++)
        {
            int a = inLeft[k];
            int b = inRight[k];
            if (a != 0)
            {
                if (both ? b != 0 : !leftAlone || b == 0)
                {
                    picked.Add((double)min + k);
                    lefts.Add(a - 1);
                    if (both)
                    {
                        rights.Add(b - 1);
                    }
                }
            }
            else if (b != 0 && rightToo)
            {
                picked.Add((double)min + k);
                rights.Add(b - 1);
            }
        }

        values = [.. picked];
        fromLeft = [.. lefts];
        fromRight = [.. rights];
        return true;
    }

    // --- ismember ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[tf, loc] = ismember(A, B)</c> where B is whole numbers in a narrow range: for every
    /// element of A, whether B holds it (1 or 0, into <paramref name="found"/>) and — when
    /// <paramref name="where"/> is given — the first position holding it, carrying
    /// <paramref name="indexBase"/>, or one below the base when nothing does. A itself may hold
    /// anything: a value that is not a whole number in B's range is simply not there.
    /// </summary>
    internal static bool TryMemberWhole(
        NumericBuffer subject, NumericBuffer set, int indexBase, NumericBuffer found, double[]? where)
    {
        if (set.Length == 0 || !TryWholeRange(set, out int min, out int max) || !TableFits(min, max, (long)set.Length * 4))
        {
            return false;
        }

        var table = new int[max - min + 1];
        MarkOccurrences(set.AsSpan(), table, min, last: false);
        int n = subject.Length;
        int blockSize = ParallelKernels.GrainElements;
        int blocks = n == 0 ? 0 : ((n - 1) / blockSize) + 1;
        ParallelKernels.ForBlocks(blocks, n >= ParallelFrom, b =>
        {
            int start = b * blockSize;
            int length = Math.Min(blockSize, n - start);
            Members(
                subject.AsSpan().Slice(start, length), found.AsSpan().Slice(start, length),
                where is null ? default : where.AsSpan(start, length), table, min, indexBase);
        });
        GC.KeepAlive(subject);
        GC.KeepAlive(set);
        GC.KeepAlive(found);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Members(
        ReadOnlySpan<double> subject, Span<double> found, Span<double> where, int[] table, int min, int indexBase)
    {
        for (int i = 0; i < subject.Length; i++)
        {
            double v = subject[i];
            int k = (int)v;
            int at = 0;
            if (k == v)
            {
                long slot = (long)k - min;
                if ((ulong)slot < (ulong)table.Length)
                {
                    at = table[slot];
                }
            }

            found[i] = at != 0 ? 1 : 0;
            if (!where.IsEmpty)
            {
                where[i] = at - 1 + indexBase;
            }
        }
    }

    // --- accumarray -------------------------------------------------------------------------------

    /// <summary>
    /// <c>accumarray(subs, vals)</c> for a column of subscripts and one value a row: each bin the
    /// sum of its values in the order they arrive, which is the order the general road adds them
    /// in, so the answer is the same to the last bit. Null when a subscript is not a whole number
    /// from 1 up — the general road is where that is refused.
    /// </summary>
    internal static double[]? TrySumIntoBins(NumericBuffer subscripts, NumericBuffer values)
    {
        if (subscripts.Length == 0 || !TryWholeRange(subscripts, out int min, out int max) || min < 1)
        {
            return null;
        }

        var sums = new double[max];
        SumInto(subscripts.AsSpan(), values.AsSpan(), sums, new bool[max]);
        GC.KeepAlive(subscripts);
        GC.KeepAlive(values);
        return sums;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SumInto(ReadOnlySpan<double> subscripts, ReadOnlySpan<double> values, double[] sums, bool[] touched)
    {
        for (int i = 0; i < subscripts.Length; i++)
        {
            int bin = (int)subscripts[i] - 1;

            // The first value is stored rather than added to the zero already there, as the general
            // road does it: a bin holding only negative zeros answers a negative zero.
            if (touched[bin])
            {
                sums[bin] += values[i];
            }
            else
            {
                sums[bin] = values[i];
                touched[bin] = true;
            }
        }
    }
}
