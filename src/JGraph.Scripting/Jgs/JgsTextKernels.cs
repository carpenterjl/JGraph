using System.Runtime.CompilerServices;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I7 (ADR 0326): the per-element loops of the text verbs over a string array,
/// cut into fixed blocks and run on several threads, and the direct roads that let those verbs read
/// a string array's elements and write their answers without a call through the general builtin per
/// element.
/// </summary>
/// <remarks>
/// <para>
/// Only a map may take <see cref="For"/>: every element's answer is a function of that element (and
/// of arguments read once before the loop), nothing is folded across elements, and nothing in the
/// body can throw. A block owns its outputs outright and the blocks are a function of the length
/// alone, so an answer is the same at one thread and at sixteen. A verb whose element can raise an
/// error — a position past the end, a cell holding a number — keeps its serial loop, so the error is
/// still the first element's.
/// </para>
/// <para>
/// <c>JGRAPH_TEXT_FAST=0</c> turns every I7 road off (the serial loops and the per-element calls
/// they replaced come back), which is what the measurement's off condition and the tests use.
/// </para>
/// </remarks>
internal static class JgsTextKernels
{
    /// <summary>Elements per block: 16K strings, a millisecond or so of the cheapest text map.</summary>
    internal const int Block = 1 << 14;

    /// <summary>Whether the I7 roads are taken (<c>JGRAPH_TEXT_FAST</c>, on unless it is <c>0</c>).</summary>
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_TEXT_FAST") != "0";

    private static long _maps;

    /// <summary>How many maps have run with the roads on (the tests read it to see a road taken).</summary>
    internal static long Maps => Interlocked.Read(ref _maps);

    /// <summary>
    /// Runs <paramref name="body"/> over <c>[0, count)</c> in blocks of <see cref="Block"/>, on
    /// several threads when there is more than one block and the roads are on.
    /// </summary>
    internal static void For(int count, Action<int, int> body)
    {
        if (count <= 0)
        {
            return;
        }

        if (Enabled)
        {
            Interlocked.Increment(ref _maps);
        }

        int blocks = ((count - 1) / Block) + 1;
        ParallelKernels.ForBlocks(blocks, Enabled && blocks > 1, b =>
        {
            int start = b * Block;
            body(start, Math.Min(Block, count - start));
        });
    }

    // --- Ordering (I6, ADR 0327) ------------------------------------------------------------------

    /// <summary>Texts from which <see cref="OrderTexts"/> is taken rather than a comparison sort.</summary>
    internal const int OrderFrom = 64;

    /// <summary>
    /// Keys from which the prefix keys are sorted by the threaded rank sort rather than the radix
    /// sort on one thread. Either is stable, so the answer is the same on both sides of it.
    /// </summary>
    internal static int RanksFrom { get; set; } = 1 << 15;

    /// <summary>
    /// Puts <paramref name="positions"/> — positions into <paramref name="texts"/>, ascending as
    /// they arrive — in the order of the texts they name: by character code, ascending or
    /// descending, equal texts left in the order they arrived. That is the order a stable
    /// comparison sort with <see cref="string.CompareOrdinal(string, string)"/> gives, reached
    /// without a comparison for most of the work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every text is first read past the prefix they all share, and the next eight characters
    /// (four, when any of them needs more than a byte) are packed into one <see cref="ulong"/>
    /// whose unsigned order is the characters' own; a text that ends inside the window is padded
    /// with zeros. A stable sort of those keys — a byte-wise radix sort, or the threaded rank sort
    /// from <see cref="RanksFrom"/> keys up — settles every pair whose windows
    /// differ. A run of equal keys is then settled by comparing its texts whole: nothing at all
    /// when they are one text repeated, a comparison sort of the run otherwise.
    /// </para>
    /// <para>
    /// The answer is a function of the texts alone. The key passes are maps over fixed blocks, the
    /// runs are disjoint, and each run is settled by whichever block it starts in.
    /// </para>
    /// </remarks>
    internal static void OrderTexts(string[] texts, int[] positions, bool descending)
    {
        int n = positions.Length;
        if (n < 2)
        {
            return;
        }

        int blocks = ((n - 1) / Block) + 1;
        string first = texts[positions[0]];
        var shared = new int[blocks];
        For(n, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (start, length) =>
        {
            int common = first.Length;
            for (int i = start; i < start + length && common > 0; i++)
            {
                common = Math.Min(common, first.AsSpan(0, common).CommonPrefixLength(texts[positions[i]]));
            }

            shared[start / Block] = common;
        });

        int skip = first.Length;
        foreach (int common in shared)
        {
            skip = Math.Min(skip, common);
        }

        var keys = new ulong[n];
        var wideIn = new bool[blocks];
        For(n, (start, length) => wideIn[start / Block] = NarrowKeys(texts, positions, keys, start, length, skip, descending));
        if (Array.IndexOf(wideIn, true) >= 0)
        {
            For(n, (start, length) => WideKeys(texts, positions, keys, start, length, skip, descending));
        }

        var order = (int[])positions.Clone();
        if (n >= RanksFrom)
        {
            SortKernels.SortRanks(keys, order, n, RanksFrom);
        }
        else
        {
            RadixSort(keys, order, n);
        }

        Comparer<int> whole = Comparer<int>.Create((x, y) =>
        {
            int compared = string.CompareOrdinal(texts[x], texts[y]);
            if (descending)
            {
                compared = -compared;
            }

            return compared != 0 ? compared : x.CompareTo(y);
        });
        For(n, (start, length) => SettleRuns(texts, keys, order, start, start + length, whole));
        Array.Copy(order, positions, n);
    }

    /// <summary>
    /// Eight characters past <paramref name="skip"/>, a byte each, as one key per text; true when
    /// a character in the window does not fit a byte, which makes the keys of this block useless.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool NarrowKeys(
        string[] texts, int[] positions, ulong[] keys, int start, int length, int skip, bool descending)
    {
        uint seen = 0;
        for (int i = start; i < start + length; i++)
        {
            ReadOnlySpan<char> window = texts[positions[i]].AsSpan(skip);
            if (window.Length > 8)
            {
                window = window[..8];
            }

            ulong key = 0;
            foreach (char c in window)
            {
                seen |= c;
                key = (key << 8) | (byte)c;
            }

            // A text that ends inside the window is padded with zeros on the right; one that ends
            // at the skip has no characters at all and its key is already zero.
            if (window.Length is > 0 and < 8)
            {
                key <<= 8 * (8 - window.Length);
            }

            keys[i] = descending ? ~key : key;
        }

        return seen > byte.MaxValue;
    }

    /// <summary>Four characters past <paramref name="skip"/>, whole, as one key per text.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WideKeys(
        string[] texts, int[] positions, ulong[] keys, int start, int length, int skip, bool descending)
    {
        for (int i = start; i < start + length; i++)
        {
            ReadOnlySpan<char> window = texts[positions[i]].AsSpan(skip);
            if (window.Length > 4)
            {
                window = window[..4];
            }

            ulong key = 0;
            foreach (char c in window)
            {
                key = (key << 16) | c;
            }

            if (window.Length is > 0 and < 4)
            {
                key <<= 16 * (4 - window.Length);
            }

            keys[i] = descending ? ~key : key;
        }
    }

    /// <summary>
    /// A stable ascending sort of <paramref name="keys"/> with a payload beside each: one counting
    /// pass per byte, least significant first, a byte every key agrees on passed over.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RadixSort(ulong[] keys, int[] payload, int n)
    {
        var counts = new int[8 * 256];
        for (int i = 0; i < n; i++)
        {
            ulong key = keys[i];
            for (int b = 0; b < 8; b++)
            {
                counts[(b << 8) + (int)((key >> (b << 3)) & 0xFF)]++;
            }
        }

        ulong[] fromKeys = keys;
        int[] fromPayload = payload;
        var toKeys = new ulong[n];
        var toPayload = new int[n];
        for (int b = 0; b < 8; b++)
        {
            Span<int> at = counts.AsSpan(b << 8, 256);
            int shift = b << 3;
            if (at[(int)((fromKeys[0] >> shift) & 0xFF)] == n)
            {
                continue;
            }

            int running = 0;
            for (int d = 0; d < 256; d++)
            {
                int held = at[d];
                at[d] = running;
                running += held;
            }

            for (int i = 0; i < n; i++)
            {
                ulong key = fromKeys[i];
                int slot = at[(int)((key >> shift) & 0xFF)]++;
                toKeys[slot] = key;
                toPayload[slot] = fromPayload[i];
            }

            (fromKeys, toKeys) = (toKeys, fromKeys);
            (fromPayload, toPayload) = (toPayload, fromPayload);
        }

        if (!ReferenceEquals(fromKeys, keys))
        {
            Array.Copy(fromKeys, keys, n);
            Array.Copy(fromPayload, payload, n);
        }
    }

    /// <summary>
    /// Settles every run of equal keys that starts in <c>[from, to)</c>, wherever it ends: the
    /// texts of a run agree on the window the key was made from and nothing else is known about
    /// them. The sort was stable, so a run arrives in arrival order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SettleRuns(string[] texts, ulong[] keys, int[] order, int from, int to, Comparer<int> whole)
    {
        int i = from;

        // A run that began in the block before this one is that block's to settle.
        while (i < to && i > 0 && keys[i] == keys[i - 1])
        {
            i++;
        }

        while (i < to)
        {
            int end = i + 1;
            while (end < keys.Length && keys[end] == keys[i])
            {
                end++;
            }

            if (end - i == 2)
            {
                if (whole.Compare(order[i], order[i + 1]) > 0)
                {
                    (order[i], order[i + 1]) = (order[i + 1], order[i]);
                }
            }
            else if (end - i > 2)
            {
                string one = texts[order[i]];
                bool repeated = true;
                for (int k = i + 1; k < end && repeated; k++)
                {
                    repeated = string.Equals(one, texts[order[k]]);
                }

                if (!repeated)
                {
                    Array.Sort(order, i, end - i, whole);
                }
            }

            i = end;
        }
    }

    /// <summary>The text of every element of a boxed string array, in storage order.</summary>
    internal static string[] Texts(JgsValue[] elements)
    {
        var texts = new string[elements.Length];
        For(elements.Length, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (start, length) =>
        {
            for (int i = start; i < start + length; i++)
            {
                texts[i] = elements[i].AsString;
            }
        });

        return texts;
    }

    /// <summary>One string element per text, in storage order.</summary>
    internal static JgsValue[] Boxed(string[] texts)
    {
        var boxed = new JgsValue[texts.Length];
        For(texts.Length, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (start, length) =>
        {
            for (int i = start; i < start + length; i++)
            {
                boxed[i] = JgsValue.Str(texts[i]);
            }
        });

        return boxed;
    }
}
