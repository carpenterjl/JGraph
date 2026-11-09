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
