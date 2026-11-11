using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// The feed-forward half of <c>filter(b, a, x)</c>: the case where the denominator has nothing in
/// it past <c>a(1)</c>, so no output feeds back into the next one and every sample of the answer can
/// be worked out on its own. That is what makes it a kernel at all — a filter with a real
/// denominator is one long dependency chain and stays where it is.
/// </summary>
/// <remarks>
/// <para>
/// The direct form the transposed recurrence unrolls to is exact, not approximate. Writing out
/// <c>z_j</c> for a filter whose feedback terms are zero gives
/// <c>y[i] = b0·x[i] + (b1·x[i−1] + (b2·x[i−2] + (… + (b_{L−1}·x[i−L+1] + s))))</c> — one
/// right-nested chain that starts at the oldest tap and works forwards, with <c>s</c> the delay the
/// caller carried in when the window still reaches back past the first sample and zero once it does
/// not. Summing the taps in that order reproduces the recurrence's own rounding, so the answers are
/// the same bits; summing them in any other order would not be.
/// </para>
/// <para>
/// Vectorising therefore goes across outputs, never across taps: a lane per output, each lane
/// running the same chain in the same order. Threads go the same way, in fixed grains — an output
/// belongs to exactly one of them, and none of them can see another's work.
/// </para>
/// <para>
/// One thing does change, and only where the data is not finite. The recurrence multiplied the
/// output by <c>a(j+1)</c> and subtracted it even when that coefficient was zero, and zero times an
/// infinity is a NaN — so a single NaN in the input used to poison the delay line and every later
/// sample with it. A filter with no feedback cannot carry a value further than its own length, and
/// this one does not (ADR 0096).
/// </para>
/// </remarks>
public static class FilterKernels
{
    /// <summary>
    /// Whether a denominator names no feedback at all — everything past <c>a(1)</c> exactly zero.
    /// </summary>
    public static bool IsFeedForward(ReadOnlySpan<double> a)
    {
        for (int i = 1; i < a.Length; i++)
        {
            if (a[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The outputs in <c>[from, to)</c>, written into <paramref name="y"/> at the same positions.
    /// <paramref name="taps"/> is the numerator already divided by <c>a(1)</c> and padded to the
    /// filter's order; <paramref name="initial"/> is the delay line the caller carried in.
    /// </summary>
    public static void FeedForward(
        ReadOnlySpan<double> taps, ReadOnlySpan<double> x, ReadOnlySpan<double> initial,
        Span<double> y, int from, int to)
    {
        int order = taps.Length;
        int delays = order - 1;
        int i = from;

        // The warm-up: while the window still reaches back past the first sample, the chain ends on
        // whatever the caller carried in rather than on zero.
        int settled = Math.Min(to, delays);
        for (; i < settled; i++)
        {
            double acc = i < initial.Length ? initial[i] : 0;
            for (int j = i; j >= 0; j--)
            {
                acc = (taps[j] * x[i - j]) + acc;
            }

            y[i] = acc;
        }

        int width = Vector<double>.Count;
        if (i + width <= to)
        {
            ref double xs = ref MemoryMarshal.GetReference(x);
            ref double ys = ref MemoryMarshal.GetReference(y);
            for (; i + width <= to; i += width)
            {
                var acc = Vector<double>.Zero;
                for (int j = order - 1; j >= 0; j--)
                {
                    acc = (new Vector<double>(taps[j]) * Vector.LoadUnsafe(ref xs, (nuint)(i - j))) + acc;
                }

                Vector.StoreUnsafe(acc, ref ys, (nuint)i);
            }
        }

        for (; i < to; i++)
        {
            y[i] = One(taps, x, i, order);
        }
    }

    /// <summary>
    /// The delay line the filter would resume from — MATLAB's <c>zf</c>. It reads only the tail of
    /// the signal, and only the initial delays that no sample has pushed out yet.
    /// </summary>
    public static void FinalState(
        ReadOnlySpan<double> taps, ReadOnlySpan<double> x, ReadOnlySpan<double> initial,
        Span<double> state)
    {
        int delays = taps.Length - 1;
        int n = x.Length;
        int wanted = Math.Min(delays, state.Length);
        for (int j = 0; j < wanted; j++)
        {
            int last = delays - 1 - j;
            int reach = Math.Min(last, n - 1);
            double acc = 0;
            if (reach < last)
            {
                // Fewer samples than the delay line is long: what is still in it came in with it.
                int k = j + n;
                acc = k < initial.Length ? initial[k] : 0;
            }

            for (int m = reach; m >= 0; m--)
            {
                acc = (taps[j + 1 + m] * x[n - 1 - m]) + acc;
            }

            state[j] = acc;
        }
    }

    /// <summary>
    /// One filtered slice per slice along a dimension of packed column-major storage, threaded
    /// inside a long slice and across short ones. <paramref name="finals"/>, when asked for, takes
    /// the same geometry with the delay line's length in place of the signal's.
    /// </summary>
    public static void FeedForwardAlong(
        NumericBuffer src, NumericBuffer dst, NumericBuffer? finals,
        ReduceKernels.Split split, double[] taps, double[] initial)
    {
        int slices = split.Slices;
        int inner = split.Inner;
        int n = split.Count;
        int delays = taps.Length - 1;
        if (slices <= 0 || n <= 0)
        {
            return;
        }

        bool overSlices = slices >= ParallelKernels.MaxDegree;
        ParallelKernels.ForBlocks(slices, overSlices, s =>
        {
            int at = ((s / inner) * inner * n) + (s % inner);
            if (inner == 1)
            {
                // A contiguous slice is filtered where it lies, in grains that own their outputs.
                if (overSlices)
                {
                    FeedForward(taps, src.AsSpan(at, n), initial, dst.AsSpan(at, n), 0, n);
                }
                else
                {
                    ParallelKernels.For(n, ParallelKernels.ComputeBoundThreshold, null, (start, len) =>
                        FeedForward(taps, src.AsSpan(at, n), initial, dst.AsSpan(at, n), start, start + len));
                }

                if (finals is not null)
                {
                    FinalState(taps, src.AsSpan(at, n), initial, finals.AsSpan(s * delays, delays));
                }

                return;
            }

            double[] gathered = System.Buffers.ArrayPool<double>.Shared.Rent(n);
            double[] filtered = System.Buffers.ArrayPool<double>.Shared.Rent(n);
            try
            {
                Span<double> from = src.AsSpan();
                Span<double> into = gathered.AsSpan(0, n);
                for (int j = 0; j < n; j++)
                {
                    into[j] = from[at + (j * inner)];
                }

                Span<double> answer = filtered.AsSpan(0, n);
                FeedForward(taps, into, initial, answer, 0, n);
                Span<double> to = dst.AsSpan();
                for (int j = 0; j < n; j++)
                {
                    to[at + (j * inner)] = answer[j];
                }

                if (finals is not null && delays > 0)
                {
                    int state = ((s / inner) * inner * delays) + (s % inner);
                    Span<double> tail = finals.AsSpan();
                    double[] rented = System.Buffers.ArrayPool<double>.Shared.Rent(delays);
                    try
                    {
                        Span<double> last = rented.AsSpan(0, delays);
                        FinalState(taps, into, initial, last);
                        for (int j = 0; j < delays; j++)
                        {
                            tail[state + (j * inner)] = last[j];
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<double>.Shared.Return(rented);
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<double>.Shared.Return(gathered);
                System.Buffers.ArrayPool<double>.Shared.Return(filtered);
            }
        });

        GC.KeepAlive(src);
        GC.KeepAlive(dst);
        GC.KeepAlive(finals);
    }

    /// <summary>
    /// The feedback half: the transposed direct-form II recurrence over one line, with the delay
    /// line carried in <paramref name="delays"/> and left there. <paramref name="b"/> and
    /// <paramref name="a"/> are already divided by <c>a(1)</c> and padded with zeros to the filter's
    /// order (<c>delays.Length + 1</c>); zero coefficients are multiplied like any other, so a
    /// non-finite sample travels exactly as it did through <c>DigitalFilter.Filter</c>'s loop.
    /// </summary>
    /// <remarks>
    /// A recurrence is one dependency chain and cannot be threaded or vectorised along the line;
    /// what it can lose is everything around the chain. Delay lines up to four long live in locals
    /// (registers), and every line keeps the general loop's expression to the operation:
    /// <c>output = b0·x + d0</c>, then <c>d_j = ((b_{j+1}·x) + d_{j+1}) − (a_{j+1}·output)</c> in
    /// ascending <c>j</c>, the last delay adding a literal zero where the loop added its missing
    /// neighbour (which turns a −0 into +0, as the loop did). Same operations, same order, same bits
    /// (ADR 0334).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Recurrence(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> x, Span<double> y,
        Span<double> delays)
    {
        int stateLength = delays.Length;
        int n = x.Length;
        if (b.Length != stateLength + 1 || a.Length != stateLength + 1 || y.Length < n)
        {
            throw new ArgumentException("the coefficients must be padded to the delay line's length plus one.");
        }

        if (n == 0)
        {
            return;
        }

        ref double xs = ref MemoryMarshal.GetReference(x);
        ref double ys = ref MemoryMarshal.GetReference(y);
        double b0 = b[0];
        switch (stateLength)
        {
            case 1:
            {
                double b1 = b[1], a1 = a[1];
                double d0 = delays[0];
                for (int i = 0; i < n; i++)
                {
                    double input = Unsafe.Add(ref xs, i);
                    double output = (b0 * input) + d0;
                    d0 = (b1 * input) + 0.0 - (a1 * output);
                    Unsafe.Add(ref ys, i) = output;
                }

                delays[0] = d0;
                return;
            }

            case 2:
            {
                double b1 = b[1], b2 = b[2], a1 = a[1], a2 = a[2];
                double d0 = delays[0], d1 = delays[1];
                for (int i = 0; i < n; i++)
                {
                    double input = Unsafe.Add(ref xs, i);
                    double output = (b0 * input) + d0;
                    d0 = (b1 * input) + d1 - (a1 * output);
                    d1 = (b2 * input) + 0.0 - (a2 * output);
                    Unsafe.Add(ref ys, i) = output;
                }

                delays[0] = d0;
                delays[1] = d1;
                return;
            }

            case 3:
            {
                double b1 = b[1], b2 = b[2], b3 = b[3], a1 = a[1], a2 = a[2], a3 = a[3];
                double d0 = delays[0], d1 = delays[1], d2 = delays[2];
                for (int i = 0; i < n; i++)
                {
                    double input = Unsafe.Add(ref xs, i);
                    double output = (b0 * input) + d0;
                    d0 = (b1 * input) + d1 - (a1 * output);
                    d1 = (b2 * input) + d2 - (a2 * output);
                    d2 = (b3 * input) + 0.0 - (a3 * output);
                    Unsafe.Add(ref ys, i) = output;
                }

                delays[0] = d0;
                delays[1] = d1;
                delays[2] = d2;
                return;
            }

            case 4:
            {
                double b1 = b[1], b2 = b[2], b3 = b[3], b4 = b[4];
                double a1 = a[1], a2 = a[2], a3 = a[3], a4 = a[4];
                double d0 = delays[0], d1 = delays[1], d2 = delays[2], d3 = delays[3];
                for (int i = 0; i < n; i++)
                {
                    double input = Unsafe.Add(ref xs, i);
                    double output = (b0 * input) + d0;
                    d0 = (b1 * input) + d1 - (a1 * output);
                    d1 = (b2 * input) + d2 - (a2 * output);
                    d2 = (b3 * input) + d3 - (a3 * output);
                    d3 = (b4 * input) + 0.0 - (a4 * output);
                    Unsafe.Add(ref ys, i) = output;
                }

                delays[0] = d0;
                delays[1] = d1;
                delays[2] = d2;
                delays[3] = d3;
                return;
            }

            default:
            {
                for (int i = 0; i < n; i++)
                {
                    double input = Unsafe.Add(ref xs, i);
                    double output = (b0 * input) + delays[0];
                    for (int j = 0; j < stateLength; j++)
                    {
                        double next = j + 1 < stateLength ? delays[j + 1] : 0;
                        delays[j] = (b[j + 1] * input) + next - (a[j + 1] * output);
                    }

                    Unsafe.Add(ref ys, i) = output;
                }

                return;
            }
        }
    }

    /// <summary>
    /// <see cref="Recurrence"/> once per slice along a dimension of packed column-major storage:
    /// a contiguous slice where it lies, a strided one gathered and scattered, and the slices spread
    /// over threads (each slice's chain belongs to one thread, so the answer does not depend on how
    /// many there are). <paramref name="finals"/>, when asked for, takes the delay line each slice
    /// finishes on, laid out like <see cref="FeedForwardAlong"/>'s.
    /// </summary>
    public static void RecurrenceAlong(
        NumericBuffer src, NumericBuffer dst, NumericBuffer? finals,
        ReduceKernels.Split split, double[] b, double[] a, double[] initial)
    {
        int slices = split.Slices;
        int inner = split.Inner;
        int n = split.Count;
        int delays = b.Length - 1;
        if (slices <= 0 || n <= 0 || delays < 1)
        {
            return;
        }

        ParallelKernels.ForBlocks(slices, slices > 1, s =>
        {
            int at = ((s / inner) * inner * n) + (s % inner);
            double[] line = System.Buffers.ArrayPool<double>.Shared.Rent(delays);
            double[]? gathered = inner == 1 ? null : System.Buffers.ArrayPool<double>.Shared.Rent(n);
            double[]? filtered = inner == 1 ? null : System.Buffers.ArrayPool<double>.Shared.Rent(n);
            try
            {
                Span<double> state = line.AsSpan(0, delays);
                state.Clear();
                initial.AsSpan(0, Math.Min(initial.Length, delays)).CopyTo(state);
                if (inner == 1)
                {
                    Recurrence(b, a, src.AsSpan(at, n), dst.AsSpan(at, n), state);
                }
                else
                {
                    Span<double> from = src.AsSpan();
                    Span<double> into = gathered.AsSpan(0, n);
                    for (int j = 0; j < n; j++)
                    {
                        into[j] = from[at + (j * inner)];
                    }

                    Span<double> answer = filtered.AsSpan(0, n);
                    Recurrence(b, a, into, answer, state);
                    Span<double> to = dst.AsSpan();
                    for (int j = 0; j < n; j++)
                    {
                        to[at + (j * inner)] = answer[j];
                    }
                }

                if (finals is not null)
                {
                    int place = ((s / inner) * inner * delays) + (s % inner);
                    Span<double> tail = finals.AsSpan();
                    for (int j = 0; j < delays; j++)
                    {
                        tail[place + (j * inner)] = state[j];
                    }
                }
            }
            finally
            {
                System.Buffers.ArrayPool<double>.Shared.Return(line);
                if (gathered is not null)
                {
                    System.Buffers.ArrayPool<double>.Shared.Return(gathered);
                }

                if (filtered is not null)
                {
                    System.Buffers.ArrayPool<double>.Shared.Return(filtered);
                }
            }
        });

        GC.KeepAlive(src);
        GC.KeepAlive(dst);
        GC.KeepAlive(finals);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double One(ReadOnlySpan<double> taps, ReadOnlySpan<double> x, int i, int order)
    {
        double acc = 0;
        for (int j = order - 1; j >= 0; j--)
        {
            acc = (taps[j] * x[i - j]) + acc;
        }

        return acc;
    }
}
