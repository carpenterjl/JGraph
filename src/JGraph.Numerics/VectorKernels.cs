using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace JGraph.Numerics;

/// <summary>
/// One-pass kernels over a packed vector of doubles for the data-preparation names (the every-row
/// plan's I5, ADR 0328): the transform of <c>normalize</c> and <c>rescale</c>, <c>gradient</c>'s
/// central differences, <c>cumtrapz</c>'s running area, the fences of <c>isoutlier</c> and
/// <c>rmmissing</c>'s compaction. Each performs the arithmetic the boxed builtin performs on each
/// element, in the same order, so the answers agree to the bit; the per-element ones run in the
/// fixed grains of <see cref="ParallelKernels"/> and so answer the same at any thread count.
/// </summary>
public static class VectorKernels
{
    /// <summary><c>dest[i] = (x[i] - centre) / scale</c>.</summary>
    public static void CentreAndScale(NumericBuffer x, NumericBuffer dest, double centre, double scale)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            Span<double> d = dest.AsSpan(start, len);
            TensorPrimitives.Subtract<double>(x.AsSpan(start, len), centre, d);
            TensorPrimitives.Divide<double>(d, scale, d);
        });

        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    /// <summary><c>dest[i] = 0 + ((x[i] - from) * 1 / span)</c>: <c>rescale</c> onto [0, 1], the
    /// multiplication by one left out because it changes nothing.</summary>
    public static void Stretch(NumericBuffer x, NumericBuffer dest, double from, double span)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            Span<double> d = dest.AsSpan(start, len);
            TensorPrimitives.Subtract<double>(x.AsSpan(start, len), from, d);
            TensorPrimitives.Divide<double>(d, span, d);
            TensorPrimitives.Add<double>(d, 0.0, d);
        });

        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// The least and greatest element, or false when the buffer is empty or holds a NaN (the
    /// caller's general road owns what a NaN means). Which of two equal zeros is reported is not
    /// specified.
    /// </summary>
    public static bool TryRange(NumericBuffer x, out double least, out double greatest)
    {
        least = 0;
        greatest = 0;
        int n = x.Length;
        if (n == 0)
        {
            return false;
        }

        int grains = ((n - 1) / ParallelKernels.GrainElements) + 1;
        var lows = new double[grains];
        var highs = new double[grains];
        ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            int g = start / ParallelKernels.GrainElements;
            ReadOnlySpan<double> span = x.AsSpan(start, len);
            lows[g] = TensorPrimitives.Min(span);
            highs[g] = TensorPrimitives.Max(span);
        });

        GC.KeepAlive(x);
        double low = lows[0];
        double high = highs[0];
        for (int g = 0; g < grains; g++)
        {
            if (double.IsNaN(lows[g]) || double.IsNaN(highs[g]))
            {
                return false;
            }

            low = Math.Min(low, lows[g]);
            high = Math.Max(high, highs[g]);
        }

        least = low;
        greatest = high;
        return true;
    }

    /// <summary>Whether every element is finite.</summary>
    public static bool AllFinite(NumericBuffer x)
    {
        int n = x.Length;
        if (n == 0)
        {
            return true;
        }

        int grains = ((n - 1) / ParallelKernels.GrainElements) + 1;
        var wild = new bool[grains];
        ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
            wild[start / ParallelKernels.GrainElements] = AnyNotFinite(x.AsSpan(start, len)));
        GC.KeepAlive(x);
        return Array.IndexOf(wild, true) < 0;
    }

    /// <summary>
    /// <c>gradient</c> of a vector at unit spacing: the difference of the two neighbours over 2
    /// inside, the one-sided difference (over 1) at each end. At least two elements.
    /// </summary>
    public static void CentralDifferences(NumericBuffer x, NumericBuffer dest)
    {
        int n = x.Length;
        ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            int from = Math.Max(start, 1);
            int to = Math.Min(start + len, n - 1);
            if (to > from)
            {
                Span<double> d = dest.AsSpan(from, to - from);
                TensorPrimitives.Subtract<double>(x.AsSpan(from + 1, to - from), x.AsSpan(from - 1, to - from), d);
                TensorPrimitives.Divide<double>(d, 2.0, d);
            }
        });

        Span<double> v = x.AsSpan();
        Span<double> slope = dest.AsSpan();
        slope[0] = v[1] - v[0];
        slope[n - 1] = v[n - 1] - v[n - 2];
        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    /// <summary>Most strips a matrix gradient is cut into; a strip is a run of whole columns.</summary>
    private const int FieldStrips = 64;

    /// <summary>
    /// <c>gradient</c> of a column-major <paramref name="rows"/> by <paramref name="cols"/> matrix
    /// along its rows (the first output, the change from column to column) at unit spacing: the
    /// difference of the two neighbouring columns over 2 inside, the one-sided difference at the
    /// first and last column. At least two columns. Whole columns are subtracted at a time, strips
    /// of columns in parallel from <see cref="ParallelKernels.MemoryBoundThreshold"/> elements;
    /// every element is one subtraction and one division, so the threads cannot move a bit.
    /// </summary>
    public static void CentralDifferencesAcross(NumericBuffer f, int rows, int cols, NumericBuffer dest)
    {
        int strips = Math.Min(cols, FieldStrips);
        bool wide = (long)rows * cols >= ParallelKernels.MemoryBoundThreshold;
        ParallelKernels.ForBlocks(strips, wide, strip =>
        {
            int c0 = (int)((long)cols * strip / strips);
            int c1 = (int)((long)cols * (strip + 1) / strips);
            for (int c = c0; c < c1; c++)
            {
                Span<double> d = dest.AsSpan(c * rows, rows);
                if (c == 0)
                {
                    TensorPrimitives.Subtract<double>(f.AsSpan(rows, rows), f.AsSpan(0, rows), d);
                }
                else if (c == cols - 1)
                {
                    TensorPrimitives.Subtract<double>(f.AsSpan(c * rows, rows), f.AsSpan((c - 1) * rows, rows), d);
                }
                else
                {
                    TensorPrimitives.Subtract<double>(f.AsSpan((c + 1) * rows, rows), f.AsSpan((c - 1) * rows, rows), d);
                    TensorPrimitives.Divide<double>(d, 2.0, d);
                }
            }
        });
        GC.KeepAlive(f);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// <c>gradient</c> of a column-major matrix down its columns (the second output) at unit
    /// spacing: each column is <see cref="CentralDifferences"/> of itself. At least two rows.
    /// </summary>
    public static void CentralDifferencesDown(NumericBuffer f, int rows, int cols, NumericBuffer dest)
    {
        int strips = Math.Min(cols, FieldStrips);
        bool wide = (long)rows * cols >= ParallelKernels.MemoryBoundThreshold;
        ParallelKernels.ForBlocks(strips, wide, strip =>
        {
            int c0 = (int)((long)cols * strip / strips);
            int c1 = (int)((long)cols * (strip + 1) / strips);
            for (int c = c0; c < c1; c++)
            {
                Span<double> v = f.AsSpan(c * rows, rows);
                Span<double> d = dest.AsSpan(c * rows, rows);
                if (rows > 2)
                {
                    Span<double> inner = d.Slice(1, rows - 2);
                    TensorPrimitives.Subtract<double>(v.Slice(2, rows - 2), v.Slice(0, rows - 2), inner);
                    TensorPrimitives.Divide<double>(inner, 2.0, inner);
                }

                d[0] = v[1] - v[0];
                d[rows - 1] = v[rows - 1] - v[rows - 2];
            }
        });
        GC.KeepAlive(f);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// <c>cumtrapz</c> of a vector at unit spacing: <c>dest[0] = 0</c>,
    /// <c>dest[i] = dest[i-1] + (x[i-1] + x[i]) / 2</c>. One chain from end to end, so one thread.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void RunningTrapezoid(NumericBuffer x, NumericBuffer dest)
    {
        Span<double> v = x.AsSpan();
        Span<double> running = dest.AsSpan();
        if (running.Length == 0)
        {
            return;
        }

        double acc = 0;
        running[0] = 0;
        for (int i = 1; i < running.Length; i++)
        {
            acc += (v[i - 1] + v[i]) / 2;
            running[i] = acc;
        }

        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    /// <summary><c>dest[i] = |x[i] - centre|</c>, into a managed scratch array.</summary>
    public static void AbsoluteDeviations(NumericBuffer x, double[] dest, double centre)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            Span<double> d = dest.AsSpan(start, len);
            TensorPrimitives.Subtract<double>(x.AsSpan(start, len), centre, d);
            TensorPrimitives.Abs<double>(d, d);
        });

        GC.KeepAlive(x);
    }

    /// <summary><c>dest[i] = x[i] &lt; low || x[i] &gt; high ? 1 : 0</c>.</summary>
    public static void OutsideFences(NumericBuffer x, NumericBuffer dest, double low, double high)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
            OutsideChunk(x.AsSpan(start, len), dest.AsSpan(start, len), low, high));
        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    /// <summary>How many elements of each grain are NaN, in grain order.</summary>
    public static int[] NaNCounts(NumericBuffer x)
    {
        int n = x.Length;
        var counts = new int[n == 0 ? 0 : ((n - 1) / ParallelKernels.GrainElements) + 1];
        ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
            counts[start / ParallelKernels.GrainElements] = CountNaN(x.AsSpan(start, len)));
        GC.KeepAlive(x);
        return counts;
    }

    /// <summary>
    /// Copies the elements that are not NaN into <paramref name="dest"/> in order, given
    /// <see cref="NaNCounts"/> of the source: each grain knows where its survivors start, so the
    /// grains can be copied on any thread.
    /// </summary>
    public static void CopyWithoutNaN(NumericBuffer x, int[] nanCounts, NumericBuffer dest)
    {
        int n = x.Length;
        var offsets = new int[nanCounts.Length];
        int at = 0;
        for (int g = 0; g < nanCounts.Length; g++)
        {
            offsets[g] = at;
            at += Math.Min(ParallelKernels.GrainElements, n - (g * ParallelKernels.GrainElements)) - nanCounts[g];
        }

        ParallelKernels.For(n, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
        {
            int g = start / ParallelKernels.GrainElements;
            Span<double> source = x.AsSpan(start, len);
            if (nanCounts[g] == 0)
            {
                source.CopyTo(dest.AsSpan(offsets[g], len));
            }
            else
            {
                KeepPresent(source, dest.AsSpan(offsets[g], len - nanCounts[g]));
            }
        });

        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool AnyNotFinite(ReadOnlySpan<double> x)
    {
        // A finite value less itself is zero; an infinity or a NaN less itself is NaN, and a NaN
        // in a running sum stays one.
        double trace = 0;
        for (int i = 0; i < x.Length; i++)
        {
            trace += x[i] - x[i];
        }

        return trace != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int CountNaN(ReadOnlySpan<double> x)
    {
        int count = 0;
        for (int i = 0; i < x.Length; i++)
        {
            if (double.IsNaN(x[i]))
            {
                count++;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void KeepPresent(ReadOnlySpan<double> source, Span<double> dest)
    {
        int at = 0;
        for (int i = 0; i < source.Length; i++)
        {
            double v = source[i];
            if (!double.IsNaN(v))
            {
                dest[at++] = v;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void OutsideChunk(ReadOnlySpan<double> x, Span<double> d, double low, double high)
    {
        for (int i = 0; i < d.Length; i++)
        {
            double v = x[i];
            d[i] = v < low || v > high ? 1.0 : 0.0;
        }
    }
}
