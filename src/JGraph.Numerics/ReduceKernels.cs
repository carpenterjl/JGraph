using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace JGraph.Numerics;

/// <summary>
/// Dimension reductions over packed column-major storage: every slice along one dimension folded to
/// a value (<see cref="Sum"/>, <see cref="Extreme"/>), or swept to a running vector
/// (<see cref="CumulativeSum"/>, <see cref="Differences"/>). Each kernel is the exact fold the boxed
/// builtin runs per slice — same seed, same order, same NaN treatment — so the answers agree to the
/// bit; the win is that a slice is read where it lies instead of being copied out, boxed, and copied
/// back.
/// </summary>
/// <remarks>
/// <para>
/// A reduction along dimension <c>d</c> of column-major storage decomposes as
/// <c>(inner, n, outer)</c> — see <see cref="Split"/>. Two layouts fall out. When <c>inner</c> is 1
/// the slices are contiguous runs and each is folded on its own; when <c>inner</c> is larger the
/// slices interleave, and the kernel walks the fold dimension once while carrying one accumulator
/// per output — the same per-output fold order, but each step reads a contiguous row, which is what
/// lets the exact folds (a sum is a sum in any lane) ride <see cref="TensorPrimitives"/>.
/// </para>
/// <para>
/// Threading follows the M93 discipline: outputs are cut into blocks whose boundaries are a function
/// of the shape alone, each output is folded whole by one thread, and nothing is combined across
/// threads — so the answer is bit-identical at one thread and at sixteen. The one fold that crosses
/// the whole array, a cumulative sweep down a single slice, stays serial because its answer is one
/// long dependency chain.
/// </para>
/// </remarks>
public static class ReduceKernels
{
    /// <summary>
    /// The shape of a reduction along one dimension of column-major storage: <c>Inner</c> is the
    /// product of the dimensions below it, <c>Count</c> the length being reduced, <c>Outer</c> the
    /// product of the dimensions above. Slice <c>s = o·Inner + i</c> holds its <c>j</c>-th element at
    /// <c>o·Inner·Count + j·Inner + i</c>, and the outputs land at <c>s</c> — the order the reduced
    /// array stores them.
    /// </summary>
    public readonly record struct Split(int Inner, int Count, int Outer)
    {
        /// <summary>How many slices there are, which is how many outputs a scalar fold makes.</summary>
        public int Slices => Inner * Outer;

        /// <summary>Total elements read.</summary>
        public long Total => (long)Inner * Count * Outer;
    }

    // --- Scalar folds: one value per slice ------------------------------------------------------

    /// <summary>Left fold from 0 with <c>+</c>; under <paramref name="omitNan"/> NaN is skipped, and
    /// an all-NaN slice answers the untouched seed — 0, the sum's own identity.</summary>
    public static void Sum(NumericBuffer src, NumericBuffer dest, Split split, bool omitNan)
    {
        if (split.Inner == 1 && split.Outer == 1 && !omitNan)
        {
            // One long run: its grains go to the threads and are added in index order.
            dest.AsSpan()[0] = GrainSum(src, 0, split.Count);
        }
        else if (split.Inner == 1)
        {
            OverColumns(split, ParallelKernels.ReductionThreshold, (first, count) =>
            {
                Span<double> outputs = dest.AsSpan(first, count);
                for (int s = 0; s < count; s++)
                {
                    Span<double> x = src.AsSpan((first + s) * split.Count, split.Count);
                    outputs[s] = omitNan ? StridedSum(x, 1, split.Count, omitNan: true, out _) : GrainSum(x);
                }
            });
        }
        else if (split.Count > ParallelKernels.GrainElements)
        {
            // A fold dimension longer than a grain: the panel form below is a straight left fold,
            // which is the sum's definition only up to one grain.
            ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold,
                (x, stride, n) => StridedSum(x, stride, n, omitNan, out _));
        }
        else
        {
            OverPanels(split, ParallelKernels.ReductionThreshold, (page, first, count) =>
            {
                Span<double> acc = dest.AsSpan((page * split.Inner) + first, count);
                acc.Clear();
                int pageBase = page * split.Inner * split.Count;
                for (int j = 0; j < split.Count; j++)
                {
                    Span<double> row = src.AsSpan(pageBase + (j * split.Inner) + first, count);
                    if (omitNan)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (!double.IsNaN(row[i]))
                            {
                                acc[i] += row[i];
                            }
                        }
                    }
                    else
                    {
                        TensorPrimitives.Add<double>(acc, row, acc);
                    }
                }
            });
        }

        GC.KeepAlive(src);
        GC.KeepAlive(dest);
    }

    /// <summary>Left fold from 1 with <c>×</c>; under <paramref name="omitNan"/> NaN is skipped, and
    /// an all-NaN slice answers the untouched seed — 1, the product's own identity.</summary>
    public static void Product(NumericBuffer src, NumericBuffer dest, Split split, bool omitNan)
    {
        if (split.Inner == 1)
        {
            OverColumns(split, ParallelKernels.ReductionThreshold, (first, count) =>
            {
                Span<double> outputs = dest.AsSpan(first, count);
                for (int s = 0; s < count; s++)
                {
                    Span<double> x = src.AsSpan((first + s) * split.Count, split.Count);
                    double total = 1;
                    foreach (double v in x)
                    {
                        if (!omitNan || !double.IsNaN(v))
                        {
                            total *= v;
                        }
                    }

                    outputs[s] = total;
                }
            });
        }
        else
        {
            OverPanels(split, ParallelKernels.ReductionThreshold, (page, first, count) =>
            {
                Span<double> acc = dest.AsSpan((page * split.Inner) + first, count);
                acc.Fill(1);
                int pageBase = page * split.Inner * split.Count;
                for (int j = 0; j < split.Count; j++)
                {
                    Span<double> row = src.AsSpan(pageBase + (j * split.Inner) + first, count);
                    if (omitNan)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (!double.IsNaN(row[i]))
                            {
                                acc[i] *= row[i];
                            }
                        }
                    }
                    else
                    {
                        TensorPrimitives.Multiply<double>(acc, row, acc);
                    }
                }
            });
        }

        GC.KeepAlive(src);
        GC.KeepAlive(dest);
    }

    /// <summary>
    /// The sum fold divided by the count — all of the slice, or only its non-NaN part under
    /// <paramref name="omitNan"/>, where a slice with nothing left answers NaN outright rather than
    /// whatever <c>0/0</c> computes to, because that is the identity the boxed wrapper returns.
    /// </summary>
    public static void Mean(NumericBuffer src, NumericBuffer dest, Split split, bool omitNan)
    {
        if (!omitNan)
        {
            // Including NaN, the mean is the sum fold divided by the full count — the same two
            // operations in the same order the boxed builtin runs them, so the sum kernel's faster
            // contiguous and panel forms serve here too.
            Sum(src, dest, split, omitNan: false);
            Span<double> outputs = dest.AsSpan(0, split.Slices);
            foreach (ref double v in outputs)
            {
                v /= split.Count;
            }

            GC.KeepAlive(dest);
            return;
        }

        ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold, (x, stride, n) =>
        {
            double total = StridedSum(x, stride, n, omitNan, out int kept);
            return kept == 0 ? double.NaN : total / kept;
        });
    }

    /// <summary>Root mean square: <c>sqrt((Σ v·v) / count)</c>, the boxed builtin's own fold; an
    /// omit-NaN slice with nothing left answers NaN, the wrapper's identity for it.</summary>
    public static void RootMeanSquare(NumericBuffer src, NumericBuffer dest, Split split, bool omitNan)
    {
        ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold, (x, stride, n) =>
        {
            double total = 0;
            int kept = 0;
            for (int j = 0; j < n; j++)
            {
                double v = x[j * stride];
                if (omitNan && double.IsNaN(v))
                {
                    continue;
                }

                total += v * v;
                kept++;
            }

            return kept == 0 ? double.NaN : Math.Sqrt(total / kept);
        });
    }

    /// <summary>
    /// The two-pass variance the boxed <c>std</c>/<c>var</c> run per slice: a mean fold, then a fold
    /// of squared deviations, divided by <c>n−1</c> — or by <c>n</c> when
    /// <paramref name="population"/> asks for MATLAB's weight-1 normalization. One value is 0 under
    /// either, none is NaN; <paramref name="takeRoot"/> makes it the standard deviation.
    /// </summary>
    public static void Variance(
        NumericBuffer src, NumericBuffer dest, Split split, bool omitNan, bool population, bool takeRoot)
    {
        if (split.Inner == 1 && split.Outer == 1 && !omitNan && split.Count > ParallelKernels.GrainElements)
        {
            // One long run: both passes go to the threads grain by grain, each added in index order.
            int n = split.Count;
            double mean = GrainSum(src, 0, n) / n;
            double spread = GrainSquaredDeviations(src, 0, n, mean) / (population ? n : n - 1);
            dest.AsSpan()[0] = takeRoot ? Math.Sqrt(spread) : spread;
            GC.KeepAlive(dest);
            return;
        }

        ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold, (x, stride, n) =>
        {
            double mean = StridedSum(x, stride, n, omitNan, out int kept);
            if (kept == 0)
            {
                return double.NaN;
            }

            if (kept == 1)
            {
                return 0;
            }

            mean /= kept;
            double sumSquares = StridedSquaredDeviations(x, stride, n, omitNan, mean);
            double spread = sumSquares / (population ? kept : kept - 1);
            return takeRoot ? Math.Sqrt(spread) : spread;
        });
    }

    // --- Sums in grains ---------------------------------------------------------------------------

    /// <summary>
    /// The sum every reduction here means (the every-row plan's I5, ADR 0328): the values are cut
    /// into grains of <see cref="ParallelKernels.GrainElements"/>, each grain is a left fold from
    /// zero, and the grains' totals are a left fold from zero in index order. Up to one grain that
    /// is the plain left fold; past it the definition is still a function of the values alone, so
    /// it is the same number at one thread and at sixteen, and the grains can go to the threads.
    /// </summary>
    public static double GrainSum(ReadOnlySpan<double> x)
    {
        double total = 0;
        for (int start = 0; start < x.Length; start += ParallelKernels.GrainElements)
        {
            total += FoldSum(x.Slice(start, Math.Min(ParallelKernels.GrainElements, x.Length - start)));
        }

        return total;
    }

    /// <summary><see cref="GrainSum(ReadOnlySpan{double})"/> over a stretch of a buffer, its grains
    /// on several threads from <see cref="ParallelKernels.ReductionThreshold"/> elements.</summary>
    public static double GrainSum(NumericBuffer src, int start, int count)
    {
        if (count < ParallelKernels.ReductionThreshold || ParallelKernels.MaxDegree == 1)
        {
            double serial = GrainSum(src.AsSpan(start, count));
            GC.KeepAlive(src);
            return serial;
        }

        int grains = ((count - 1) / ParallelKernels.GrainElements) + 1;
        var partial = new double[grains];
        ParallelKernels.ForBlocks(grains, parallel: true, g =>
        {
            int at = g * ParallelKernels.GrainElements;
            partial[g] = FoldSum(src.AsSpan(start + at, Math.Min(ParallelKernels.GrainElements, count - at)));
        });

        GC.KeepAlive(src);
        double total = 0;
        foreach (double p in partial)
        {
            total += p;
        }

        return total;
    }

    /// <summary>The sum of squared distances from <paramref name="mean"/>, folded in the same
    /// grains as <see cref="GrainSum(ReadOnlySpan{double})"/>.</summary>
    public static double GrainSquaredDeviations(ReadOnlySpan<double> x, double mean)
    {
        double total = 0;
        for (int start = 0; start < x.Length; start += ParallelKernels.GrainElements)
        {
            total += FoldSquaredDeviations(
                x.Slice(start, Math.Min(ParallelKernels.GrainElements, x.Length - start)), mean);
        }

        return total;
    }

    /// <summary><see cref="GrainSquaredDeviations(ReadOnlySpan{double}, double)"/> over a stretch
    /// of a buffer, its grains on several threads from
    /// <see cref="ParallelKernels.ReductionThreshold"/> elements.</summary>
    public static double GrainSquaredDeviations(NumericBuffer src, int start, int count, double mean)
    {
        if (count < ParallelKernels.ReductionThreshold || ParallelKernels.MaxDegree == 1)
        {
            double serial = GrainSquaredDeviations(src.AsSpan(start, count), mean);
            GC.KeepAlive(src);
            return serial;
        }

        int grains = ((count - 1) / ParallelKernels.GrainElements) + 1;
        var partial = new double[grains];
        ParallelKernels.ForBlocks(grains, parallel: true, g =>
        {
            int at = g * ParallelKernels.GrainElements;
            partial[g] = FoldSquaredDeviations(
                src.AsSpan(start + at, Math.Min(ParallelKernels.GrainElements, count - at)), mean);
        });

        GC.KeepAlive(src);
        double total = 0;
        foreach (double p in partial)
        {
            total += p;
        }

        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double FoldSum(ReadOnlySpan<double> x)
    {
        double total = 0;
        for (int i = 0; i < x.Length; i++)
        {
            total += x[i];
        }

        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double FoldSquaredDeviations(ReadOnlySpan<double> x, double mean)
    {
        double total = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = x[i] - mean;
            total += d * d;
        }

        return total;
    }

    /// <summary>
    /// The grain sum of a slice read at a stride, with NaN passed over under
    /// <paramref name="omitNan"/>: the grains are counted in the values that are kept, so the
    /// answer is the grain sum of the slice with its NaN removed, which is how the boxed wrapper
    /// computes it. <paramref name="kept"/> is how many values were added.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double StridedSum(Span<double> x, int stride, int n, bool omitNan, out int kept)
    {
        double total = 0;
        double partial = 0;
        int inGrain = 0;
        kept = 0;
        for (int j = 0; j < n; j++)
        {
            double v = x[j * stride];
            if (omitNan && double.IsNaN(v))
            {
                continue;
            }

            partial += v;
            kept++;
            if (++inGrain == ParallelKernels.GrainElements)
            {
                total += partial;
                partial = 0;
                inGrain = 0;
            }
        }

        return inGrain > 0 ? total + partial : total;
    }

    /// <summary><see cref="StridedSum"/>'s walk over squared distances from <paramref name="mean"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double StridedSquaredDeviations(Span<double> x, int stride, int n, bool omitNan, double mean)
    {
        double total = 0;
        double partial = 0;
        int inGrain = 0;
        for (int j = 0; j < n; j++)
        {
            double v = x[j * stride];
            if (omitNan && double.IsNaN(v))
            {
                continue;
            }

            double d = v - mean;
            partial += d * d;
            if (++inGrain == ParallelKernels.GrainElements)
            {
                total += partial;
                partial = 0;
                inGrain = 0;
            }
        }

        return inGrain > 0 ? total + partial : total;
    }

    /// <summary>Whether any element of the slice is nonzero (1) or none is (0) — and NaN is nonzero,
    /// which is why the truth reductions have no NaN option to honor.</summary>
    public static void Any(NumericBuffer src, NumericBuffer dest, Split split)
    {
        ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold, (x, stride, n) =>
        {
            for (int j = 0; j < n; j++)
            {
                if (x[j * stride] != 0)
                {
                    return 1;
                }
            }

            return 0;
        });
    }

    /// <summary>Whether every element of the slice is nonzero (1); an empty slice cannot reach here,
    /// so the vacuous-truth case stays the boxed builtin's business.</summary>
    public static void All(NumericBuffer src, NumericBuffer dest, Split split)
    {
        ForEachSlice(src, dest, split, ParallelKernels.ReductionThreshold, (x, stride, n) =>
        {
            for (int j = 0; j < n; j++)
            {
                if (x[j * stride] == 0)
                {
                    return 0;
                }
            }

            return 1;
        });
    }

    /// <summary>
    /// The p-norm of each slice, exactly as the boxed <c>vecnorm</c> folds it: <c>max(|x|)</c> from 0
    /// for an infinite p, otherwise <c>pow(Σ pow(|x|, p), 1/p)</c> — through <see cref="Math.Pow"/>
    /// both times, because <c>Math.Pow(x, 2)</c> is not <c>x·x</c> (M93) and the boxed fold takes
    /// the former.
    /// </summary>
    public static void Norm(NumericBuffer src, NumericBuffer dest, Split split, double p)
    {
        ForEachSlice(src, dest, split, ParallelKernels.ComputeBoundThreshold, (x, stride, n) =>
        {
            if (double.IsPositiveInfinity(p))
            {
                double largest = 0;
                for (int j = 0; j < n; j++)
                {
                    largest = Math.Max(largest, Math.Abs(x[j * stride]));
                }

                return largest;
            }

            double sum = 0;
            for (int j = 0; j < n; j++)
            {
                sum += Math.Pow(Math.Abs(x[j * stride]), p);
            }

            return Math.Pow(sum, 1.0 / p);
        });
    }

    /// <summary>
    /// The extreme of every slice and the position it came from, replicating the boxed scan bit for
    /// bit: ties go to the first, under omit-NaN a NaN never wins (so an all-NaN slice answers its
    /// first element at position 0), and under include-NaN the scan stops at the first NaN it meets
    /// past the start and reports <see cref="double.NaN"/> there. Positions are 0-based fold indices;
    /// the caller owns index bases and linearization.
    /// </summary>
    public static void Extreme(
        NumericBuffer src, NumericBuffer values, NumericBuffer indices, Split split,
        bool takeMin, bool omitNan)
    {
        if (split.Inner > 1 && omitNan)
        {
            // The panel form: one running (best, at) pair per output, advanced a whole row at a
            // time. Only the omit-NaN scan can take it, because it is the one with no early stop.
            OverPanels(split, ParallelKernels.ReductionThreshold, (page, first, count) =>
            {
                Span<double> best = values.AsSpan((page * split.Inner) + first, count);
                Span<double> at = indices.AsSpan((page * split.Inner) + first, count);
                int pageBase = page * split.Inner * split.Count;
                src.AsSpan(pageBase + first, count).CopyTo(best);
                at.Clear();
                for (int j = 1; j < split.Count; j++)
                {
                    Span<double> row = src.AsSpan(pageBase + (j * split.Inner) + first, count);

                    // The strict comparison first: it is the whole story unless the running best
                    // is NaN, and putting the NaN test behind it keeps the hot path one compare —
                    // the same wins rule as ScanExtreme, split into its cheap and rare halves.
                    if (takeMin)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            double candidate = row[i];
                            if (candidate < best[i]
                                || (double.IsNaN(best[i]) && !double.IsNaN(candidate)))
                            {
                                best[i] = candidate;
                                at[i] = j;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            double candidate = row[i];
                            if (candidate > best[i]
                                || (double.IsNaN(best[i]) && !double.IsNaN(candidate)))
                            {
                                best[i] = candidate;
                                at[i] = j;
                            }
                        }
                    }
                }
            });
        }
        else
        {
            ForEachSliceIndexed(src, split, ParallelKernels.ReductionThreshold, (x, stride, n, slice) =>
            {
                (double bestValue, int bestAt) = ScanExtreme(x, stride, n, takeMin, omitNan);
                values.AsSpan()[slice] = bestValue;
                indices.AsSpan()[slice] = bestAt;
            });
        }

        GC.KeepAlive(src);
        GC.KeepAlive(values);
        GC.KeepAlive(indices);
    }

    /// <summary>The extreme scan over a whole buffer read as one slice — <c>min(A(:))</c> without
    /// the <c>(:)</c> ever being materialized.</summary>
    public static (double Value, int At) ExtremeFlat(NumericBuffer src, bool takeMin, bool omitNan)
    {
        (double value, int at) = ScanExtreme(src.AsSpan(), 1, src.Length, takeMin, omitNan);
        GC.KeepAlive(src);
        return (value, at);
    }

    // --- Running folds: a whole vector per slice ------------------------------------------------

    /// <summary>
    /// Running totals down each slice, from the near end or (<paramref name="reverse"/>) the far
    /// one. Under <paramref name="omitNan"/> a NaN reads as 0 — the running value passes over it —
    /// which is the substitution the boxed wrapper makes before its fold.
    /// </summary>
    public static void CumulativeSum(
        NumericBuffer src, NumericBuffer dest, Split split, bool omitNan, bool reverse)
    {
        ForEachRunning(src, dest, split, (x, y, stride, n) =>
        {
            if (stride == 1 && !omitNan && !reverse)
            {
                RunningSum(x, y, n);
                return;
            }

            double acc = 0;
            if (reverse)
            {
                for (int j = n - 1; j >= 0; j--)
                {
                    double v = x[j * stride];
                    acc += omitNan && double.IsNaN(v) ? 0 : v;
                    y[j * stride] = acc;
                }
            }
            else
            {
                for (int j = 0; j < n; j++)
                {
                    double v = x[j * stride];
                    acc += omitNan && double.IsNaN(v) ? 0 : v;
                    y[j * stride] = acc;
                }
            }
        });
    }

    /// <summary>The forward running total of a contiguous slice: the same additions in the same
    /// order as the general walk, compiled optimised from its first call (ADR 0158).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RunningSum(Span<double> x, Span<double> y, int n)
    {
        ref double source = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(x);
        ref double running = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(y);
        double acc = 0;
        for (nint j = 0; j < n; j++)
        {
            acc += Unsafe.Add(ref source, j);
            Unsafe.Add(ref running, j) = acc;
        }
    }

    /// <summary>Running products down each slice; a NaN reads as 1 under
    /// <paramref name="omitNan"/>, the multiplication's identity.</summary>
    public static void CumulativeProduct(
        NumericBuffer src, NumericBuffer dest, Split split, bool omitNan, bool reverse)
    {
        ForEachRunning(src, dest, split, (x, y, stride, n) =>
        {
            double acc = 1;
            if (reverse)
            {
                for (int j = n - 1; j >= 0; j--)
                {
                    double v = x[j * stride];
                    acc *= omitNan && double.IsNaN(v) ? 1 : v;
                    y[j * stride] = acc;
                }
            }
            else
            {
                for (int j = 0; j < n; j++)
                {
                    double v = x[j * stride];
                    acc *= omitNan && double.IsNaN(v) ? 1 : v;
                    y[j * stride] = acc;
                }
            }
        });
    }

    /// <summary>
    /// Running extremes down each slice through <see cref="Math.Max(double, double)"/> /
    /// <see cref="Math.Min(double, double)"/>, with the first element taken as it stands — the boxed
    /// running fold's seed. Under <paramref name="omitNan"/> (these names' default) a NaN reads as
    /// the losing infinity, so the running value passes over it; without it
    /// <see cref="Math.Max(double, double)"/> propagates the NaN, and so does this.
    /// </summary>
    public static void CumulativeExtreme(
        NumericBuffer src, NumericBuffer dest, Split split, bool takeMin, bool omitNan, bool reverse)
    {
        double identity = takeMin ? double.PositiveInfinity : double.NegativeInfinity;
        ForEachRunning(src, dest, split, (x, y, stride, n) =>
        {
            if (reverse)
            {
                double v = x[(n - 1) * stride];
                double acc = omitNan && double.IsNaN(v) ? identity : v;
                y[(n - 1) * stride] = acc;
                for (int j = n - 2; j >= 0; j--)
                {
                    v = x[j * stride];
                    double candidate = omitNan && double.IsNaN(v) ? identity : v;
                    acc = takeMin ? Math.Min(acc, candidate) : Math.Max(acc, candidate);
                    y[j * stride] = acc;
                }
            }
            else
            {
                double v = x[0];
                double acc = omitNan && double.IsNaN(v) ? identity : v;
                y[0] = acc;
                for (int j = 1; j < n; j++)
                {
                    v = x[j * stride];
                    double candidate = omitNan && double.IsNaN(v) ? identity : v;
                    acc = takeMin ? Math.Min(acc, candidate) : Math.Max(acc, candidate);
                    y[j * stride] = acc;
                }
            }
        });
    }

    /// <summary>
    /// One differencing pass: <c>dest</c>'s slice element <c>j</c> is <c>x[j+1] − x[j]</c>, so its
    /// slices are one shorter than the source's. Repeated differencing is this applied again by the
    /// caller, exactly as the boxed builtin is called again.
    /// </summary>
    /// <remarks>
    /// A subtraction is correctly rounded whoever performs it, so the contiguous branch is
    /// <see cref="TensorPrimitives.Subtract{T}(ReadOnlySpan{T}, ReadOnlySpan{T}, Span{T})"/> of the
    /// slice against itself shifted by one — the same operand order as <c>x[j+1] - x[j]</c>, so a
    /// NaN keeps the payload the scalar loop kept — and one long slice is cut by output index at
    /// <see cref="ParallelKernels.MemoryBoundThreshold"/>, each grain reading one element past its
    /// end (item 11b, ADR 0158). The interleaved branch was already this subtraction.
    /// </remarks>
    public static void Differences(NumericBuffer src, NumericBuffer dest, Split split)
    {
        int shorter = split.Count - 1;
        if (split.Inner == 1 && split.Outer == 1)
        {
            ParallelKernels.For(shorter, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
                TensorPrimitives.Subtract<double>(
                    src.AsSpan(start + 1, len), src.AsSpan(start, len), dest.AsSpan(start, len)));
        }
        else if (split.Inner == 1)
        {
            OverColumns(split, ParallelKernels.ReductionThreshold, (first, count) =>
            {
                for (int s = first; s < first + count; s++)
                {
                    Span<double> x = src.AsSpan(s * split.Count, split.Count);
                    TensorPrimitives.Subtract<double>(x[1..], x[..shorter], dest.AsSpan(s * shorter, shorter));
                }
            });
        }
        else
        {
            OverPanels(split, ParallelKernels.ReductionThreshold, (page, first, count) =>
            {
                int srcBase = page * split.Inner * split.Count;
                int destBase = page * split.Inner * shorter;
                for (int j = 0; j < shorter; j++)
                {
                    Span<double> low = src.AsSpan(srcBase + (j * split.Inner) + first, count);
                    Span<double> high = src.AsSpan(srcBase + ((j + 1) * split.Inner) + first, count);
                    Span<double> y = dest.AsSpan(destBase + (j * split.Inner) + first, count);
                    TensorPrimitives.Subtract<double>(high, low, y);
                }
            });
        }

        GC.KeepAlive(src);
        GC.KeepAlive(dest);
    }

    // --- The shared walking machinery -----------------------------------------------------------

    /// <summary>One slice read at a fixed stride: element <c>j</c> is <c>x[j · stride]</c>.</summary>
    private delegate double SliceFold(Span<double> x, int stride, int n);

    private delegate void SliceVisit(Span<double> x, int stride, int n, int slice);

    private delegate void RunningFold(Span<double> x, Span<double> y, int stride, int n);

    private delegate void PanelRows(int page, int first, int count);

    /// <summary>Runs a scalar fold over every slice, writing one output each, threaded in blocks of
    /// whole slices. The strided read serves both layouts: contiguous slices pass stride 1.</summary>
    private static void ForEachSlice(
        NumericBuffer src, NumericBuffer dest, Split split, int threshold, SliceFold fold)
    {
        ForEachSliceIndexed(src, split, threshold, (x, stride, n, slice) =>
            dest.AsSpan()[slice] = fold(x, stride, n));
        GC.KeepAlive(dest);
    }

    private static void ForEachSliceIndexed(
        NumericBuffer src, Split split, int threshold, SliceVisit visit)
    {
        if (split.Inner == 1)
        {
            OverColumns(split, threshold, (first, count) =>
            {
                for (int s = first; s < first + count; s++)
                {
                    visit(src.AsSpan(s * split.Count, split.Count), 1, split.Count, s);
                }
            });
        }
        else
        {
            OverPanels(split, threshold, (page, first, count) =>
            {
                int pageBase = page * split.Inner * split.Count;
                for (int i = first; i < first + count; i++)
                {
                    // The whole strided slice as one span: from its first element to its last.
                    int length = ((split.Count - 1) * split.Inner) + 1;
                    visit(src.AsSpan(pageBase + i, length), split.Inner, split.Count,
                        (page * split.Inner) + i);
                }
            });
        }

        GC.KeepAlive(src);
    }

    /// <summary>Runs a running fold over every slice, writing a same-shaped slice back.</summary>
    private static void ForEachRunning(
        NumericBuffer src, NumericBuffer dest, Split split, RunningFold fold)
    {
        if (split.Inner == 1)
        {
            OverColumns(split, ParallelKernels.ReductionThreshold, (first, count) =>
            {
                for (int s = first; s < first + count; s++)
                {
                    fold(src.AsSpan(s * split.Count, split.Count),
                        dest.AsSpan(s * split.Count, split.Count), 1, split.Count);
                }
            });
        }
        else
        {
            OverPanels(split, ParallelKernels.ReductionThreshold, (page, first, count) =>
            {
                int pageBase = page * split.Inner * split.Count;
                int length = ((split.Count - 1) * split.Inner) + 1;
                for (int i = first; i < first + count; i++)
                {
                    fold(src.AsSpan(pageBase + i, length), dest.AsSpan(pageBase + i, length),
                        split.Inner, split.Count);
                }
            });
        }

        GC.KeepAlive(src);
        GC.KeepAlive(dest);
    }

    /// <summary>The boxed extreme scan, verbatim — including the include-NaN early stop that skips
    /// a NaN sitting in the very first position, because the boxed loop starts at the second.</summary>
    private static (double Value, int At) ScanExtreme(
        Span<double> x, int stride, int n, bool takeMin, bool omitNan)
    {
        double best = x[0];
        int at = 0;
        for (int j = 1; j < n; j++)
        {
            double candidate = x[j * stride];
            if (!omitNan && double.IsNaN(candidate))
            {
                return (double.NaN, j);
            }

            bool wins = double.IsNaN(best)
                ? !double.IsNaN(candidate)
                : takeMin ? candidate < best : candidate > best;
            if (wins)
            {
                best = candidate;
                at = j;
            }
        }

        return (best, at);
    }

    /// <summary>
    /// Contiguous layout: whole slices grouped into fixed blocks of roughly
    /// <see cref="ParallelKernels.GrainElements"/> elements each; <c>body(firstSlice, count)</c>.
    /// </summary>
    private static void OverColumns(Split split, int threshold, Action<int, int> body)
    {
        int block = (int)Math.Clamp(
            ParallelKernels.GrainElements / (long)Math.Max(split.Count, 1), 1, split.Outer);
        int blocks = ((split.Outer - 1) / block) + 1;
        ParallelKernels.ForBlocks(blocks, split.Total >= threshold, b =>
        {
            int first = b * block;
            body(first, Math.Min(block, split.Outer - first));
        });
    }

    /// <summary>
    /// Interleaved layout: each page's rows grouped into fixed-width bands of roughly
    /// <see cref="ParallelKernels.GrainElements"/> elements of work; <c>body(page, firstRow, count)</c>.
    /// </summary>
    private static void OverPanels(Split split, int threshold, PanelRows body)
    {
        // At least 512 rows a band even when the fold dimension is long: each fold step then reads
        // a 4 KB contiguous run, which the prefetchers stream, where a band sized purely by
        // GrainElements/Count degenerates to a dozen rows and a 64 KB stride between touches —
        // measured at half the throughput on the 8000×5000 row-max.
        int width = (int)Math.Clamp(
            Math.Max(ParallelKernels.GrainElements / (long)Math.Max(split.Count, 1), 512),
            1, split.Inner);
        int perPage = ((split.Inner - 1) / width) + 1;
        ParallelKernels.ForBlocks(perPage * split.Outer, split.Total >= threshold, unit =>
        {
            int page = unit / perPage;
            int first = (unit % perPage) * width;
            body(page, first, Math.Min(width, split.Inner - first));
        });
    }
}
