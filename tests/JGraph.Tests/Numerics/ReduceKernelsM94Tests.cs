using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M94: the dimension reductions leave the boxed road, and the one claim that licenses it is that
/// they change nothing — each kernel is the boxed per-slice fold to the bit, whatever the layout,
/// the NaN treatment, or the thread count. So this file is a bit-comparison factory: every kernel
/// against a reference that slices and folds the slow way, over shapes that exercise both layouts;
/// then the same kernels at one thread and at sixteen over arrays big enough to actually split; and
/// then the deliberate oddities — the include-NaN scan that spares a NaN in first position, the sum
/// whose zero seed turns a lone negative zero positive, ties going to the first — pinned one by one
/// so a cleanup can't quietly fix them.
/// </summary>
public class ReduceKernelsM94Tests
{
    /// <summary>Shapes that cover both layouts: contiguous (inner 1), interleaved, single-slice,
    /// single-element folds, and a fold dimension of one.</summary>
    public static TheoryData<int, int, int> Shapes() => new()
    {
        { 1, 7, 5 },   // contiguous columns
        { 1, 40, 1 },  // one contiguous slice
        { 4, 6, 3 },   // interleaved pages
        { 6, 1, 4 },   // fold dimension of one: every element its own slice
        { 5, 9, 1 },   // one page, wide
        { 1, 1, 1 },   // a scalar
    };

    // --- Every kernel is its reference fold, bit for bit ----------------------------------------

    [Theory]
    [MemberData(nameof(Shapes))]
    public void SumMatchesTheBoxedFoldOverEveryLayoutAndNanTreatment(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool omitNan in new[] { false, true })
            {
                AssertScalarKernel(data, split,
                    (src, dest) => ReduceKernels.Sum(src, dest, split, omitNan),
                    slice => Fold(slice, omitNan, 0, static (acc, v) => acc + v));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ProductMatchesTheBoxedFold(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool omitNan in new[] { false, true })
            {
                AssertScalarKernel(data, split,
                    (src, dest) => ReduceKernels.Product(src, dest, split, omitNan),
                    slice => Fold(slice, omitNan, 1, static (acc, v) => acc * v));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void MeanAndRmsMatchTheBoxedFolds(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool omitNan in new[] { false, true })
            {
                AssertScalarKernel(data, split,
                    (src, dest) => ReduceKernels.Mean(src, dest, split, omitNan),
                    slice =>
                    {
                        double[] kept = Kept(slice, omitNan);
                        if (kept.Length == 0)
                        {
                            return double.NaN;
                        }

                        double total = 0;
                        foreach (double v in kept)
                        {
                            total += v;
                        }

                        return total / kept.Length;
                    });

                AssertScalarKernel(data, split,
                    (src, dest) => ReduceKernels.RootMeanSquare(src, dest, split, omitNan),
                    slice =>
                    {
                        double[] kept = Kept(slice, omitNan);
                        if (kept.Length == 0)
                        {
                            return double.NaN;
                        }

                        double total = 0;
                        foreach (double v in kept)
                        {
                            total += v * v;
                        }

                        return Math.Sqrt(total / kept.Length);
                    });
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void VarianceMatchesTheBoxedTwoPassFoldUnderBothWeights(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool omitNan in new[] { false, true })
            {
                foreach (bool population in new[] { false, true })
                {
                    foreach (bool takeRoot in new[] { false, true })
                    {
                        AssertScalarKernel(data, split,
                            (src, dest) => ReduceKernels.Variance(
                                src, dest, split, omitNan, population, takeRoot),
                            slice =>
                            {
                                double[] kept = Kept(slice, omitNan);
                                if (kept.Length == 0)
                                {
                                    return double.NaN;
                                }

                                if (kept.Length == 1)
                                {
                                    return 0;
                                }

                                double mean = 0;
                                foreach (double v in kept)
                                {
                                    mean += v;
                                }

                                mean /= kept.Length;
                                double sumSquares = 0;
                                foreach (double v in kept)
                                {
                                    double d = v - mean;
                                    sumSquares += d * d;
                                }

                                double spread = sumSquares
                                    / (population ? kept.Length : kept.Length - 1);
                                return takeRoot ? Math.Sqrt(spread) : spread;
                            });
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AnyAndAllMatchTheBoxedScans(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            AssertScalarKernel(data, split,
                (src, dest) => ReduceKernels.Any(src, dest, split),
                slice => Array.Exists(slice, static v => v != 0) ? 1.0 : 0.0);
            AssertScalarKernel(data, split,
                (src, dest) => ReduceKernels.All(src, dest, split),
                slice => Array.TrueForAll(slice, static v => v != 0) ? 1.0 : 0.0);
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NormMatchesTheBoxedVecnormFoldForEveryP(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (double p in new[] { 1.0, 2.0, 3.5, double.PositiveInfinity })
            {
                AssertScalarKernel(data, split,
                    (src, dest) => ReduceKernels.Norm(src, dest, split, p),
                    slice =>
                    {
                        if (double.IsPositiveInfinity(p))
                        {
                            double largest = 0;
                            foreach (double v in slice)
                            {
                                largest = Math.Max(largest, Math.Abs(v));
                            }

                            return largest;
                        }

                        double sum = 0;
                        foreach (double v in slice)
                        {
                            // Math.Pow both times, because that is the fold vecnorm runs and
                            // Math.Pow(x, 2) is not x*x (M93).
                            sum += Math.Pow(Math.Abs(v), p);
                        }

                        return Math.Pow(sum, 1.0 / p);
                    });
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ExtremeMatchesTheBoxedScanInValueAndPosition(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool takeMin in new[] { false, true })
            {
                foreach (bool omitNan in new[] { true, false })
                {
                    using NumericBuffer src = Adopt(data);
                    using var values = new ManagedBuffer(split.Slices);
                    using var indices = new ManagedBuffer(split.Slices);
                    ReduceKernels.Extreme(src, values, indices, split, takeMin, omitNan);

                    for (int s = 0; s < split.Slices; s++)
                    {
                        (double value, int at) = ReferenceExtreme(
                            SliceOf(data, split, s), takeMin, omitNan);
                        AssertBits(value, values.AsSpan()[s]);
                        Assert.Equal(at, (int)indices.AsSpan()[s]);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void CumulativeFoldsMatchTheBoxedRunningFolds(int inner, int n, int outer)
    {
        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            foreach (bool omitNan in new[] { false, true })
            {
                foreach (bool reverse in new[] { false, true })
                {
                    AssertRunningKernel(data, split,
                        (src, dest) => ReduceKernels.CumulativeSum(src, dest, split, omitNan, reverse),
                        slice => RunningFold(slice, omitNan, reverse, 0, seedFirst: false,
                            static (acc, v) => acc + v));
                    AssertRunningKernel(data, split,
                        (src, dest) => ReduceKernels.CumulativeProduct(src, dest, split, omitNan, reverse),
                        slice => RunningFold(slice, omitNan, reverse, 1, seedFirst: false,
                            static (acc, v) => acc * v));
                    AssertRunningKernel(data, split,
                        (src, dest) => ReduceKernels.CumulativeExtreme(
                            src, dest, split, takeMin: false, omitNan, reverse),
                        slice => RunningFold(slice, omitNan, reverse, double.NegativeInfinity,
                            seedFirst: true, Math.Max));
                    AssertRunningKernel(data, split,
                        (src, dest) => ReduceKernels.CumulativeExtreme(
                            src, dest, split, takeMin: true, omitNan, reverse),
                        slice => RunningFold(slice, omitNan, reverse, double.PositiveInfinity,
                            seedFirst: true, Math.Min));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void DifferencesMatchTheBoxedAdjacentSubtraction(int inner, int n, int outer)
    {
        if (n < 2)
        {
            return; // a shorter fold has no differences; the ops layer answers empty before here
        }

        foreach ((var split, double[] data) in Cases(inner, n, outer))
        {
            using NumericBuffer src = Adopt(data);
            var shorter = new ReduceKernels.Split(split.Inner, split.Count - 1, split.Outer);
            using var dest = new ManagedBuffer((int)shorter.Total);
            ReduceKernels.Differences(src, dest, split);

            for (int s = 0; s < split.Slices; s++)
            {
                double[] slice = SliceOf(data, split, s);
                for (int j = 0; j < slice.Length - 1; j++)
                {
                    AssertBits(slice[j + 1] - slice[j], ElementOf(dest, shorter, s, j));
                }
            }
        }
    }

    // --- A thread count is not an input ---------------------------------------------------------

    /// <summary>Both layouts at a size that certainly splits: contiguous slices grouped into
    /// blocks, and an interleaved panel cut into bands.</summary>
    public static TheoryData<int, int, int> ThreadedShapes() => new()
    {
        { 1, 1_024, 4_179 },   // contiguous, uneven block tail
        { 1_030, 4_099, 1 },   // panel, uneven band tail
    };

    [Theory]
    [MemberData(nameof(ThreadedShapes))]
    public void EveryReductionAnswersTheSameBitsAtOneThreadAndAtSixteen(int inner, int n, int outer)
    {
        var split = new ReduceKernels.Split(inner, n, outer);
        double[] data = Data((int)split.Total, nanEvery: 97);
        using NumericBuffer src = Adopt(data);

        foreach (Action<NumericBuffer, NumericBuffer> kernel in new Action<NumericBuffer, NumericBuffer>[]
        {
            (s, d) => ReduceKernels.Sum(s, d, split, omitNan: false),
            (s, d) => ReduceKernels.Sum(s, d, split, omitNan: true),
            (s, d) => ReduceKernels.Mean(s, d, split, omitNan: true),
            (s, d) => ReduceKernels.Variance(s, d, split, omitNan: false, population: false, takeRoot: true),
            (s, d) => ReduceKernels.Norm(s, d, split, 2),
            (s, d) => ReduceKernels.Any(s, d, split),
        })
        {
            double[] alone = AtDegree(1, () => ScalarResult(kernel, src, split));
            double[] together = AtDegree(16, () => ScalarResult(kernel, src, split));
            AssertSameBits(alone, together);
        }

        foreach (bool takeMin in new[] { false, true })
        {
            (double[] Values, double[] At) alone = AtDegree(1, () => ExtremeResult(src, split, takeMin));
            (double[] Values, double[] At) together = AtDegree(16, () => ExtremeResult(src, split, takeMin));
            AssertSameBits(alone.Values, together.Values);
            Assert.Equal(alone.At, together.At);
        }

        double[] cumAlone = AtDegree(1, () => RunningResult(
            (s, d) => ReduceKernels.CumulativeSum(s, d, split, omitNan: false, reverse: false), src, split));
        double[] cumTogether = AtDegree(16, () => RunningResult(
            (s, d) => ReduceKernels.CumulativeSum(s, d, split, omitNan: false, reverse: false), src, split));
        AssertSameBits(cumAlone, cumTogether);
    }

    // --- The deliberate oddities, pinned --------------------------------------------------------

    [Fact]
    public void TheZeroSeedTurnsALoneNegativeZeroPositive()
    {
        // The boxed sum folds from 0.0, and 0 + (−0) is +0 — so sum([−0]) is +0, sign and all,
        // and the kernel must lose the sign the same way.
        using NumericBuffer src = Adopt([-0.0]);
        using var dest = new ManagedBuffer(1);
        ReduceKernels.Sum(src, dest, new ReduceKernels.Split(1, 1, 1), omitNan: false);
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(dest.AsSpan()[0]));
    }

    [Fact]
    public void TheRunningFoldsSeedFromTheFirstElementAndKeepItsSign()
    {
        // cummax copies its first element as it stands — no fold touches it — so a leading −0
        // stays −0 there, unlike the sum above.
        using NumericBuffer src = Adopt([-0.0, -1.0]);
        using var dest = new ManagedBuffer(2);
        ReduceKernels.CumulativeExtreme(src, dest, new ReduceKernels.Split(1, 2, 1),
            takeMin: false, omitNan: true, reverse: false);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0),
            BitConverter.DoubleToInt64Bits(dest.AsSpan()[0]));
    }

    [Fact]
    public void TheIncludeNanScanStopsAtTheFirstNanExceptOneSittingInFirstPosition()
    {
        // The boxed loop starts comparing at the second element, so a NaN opening the slice never
        // triggers the include-NaN stop: the answer is the best of the rest. Anywhere else, the
        // scan stops there and answers the canonical NaN at that position.
        using NumericBuffer leading = Adopt([double.NaN, 3, 7]);
        (double value, int at) = ReduceKernels.ExtremeFlat(leading, takeMin: false, omitNan: false);
        Assert.Equal(7, value);
        Assert.Equal(2, at);

        using NumericBuffer inside = Adopt([3, double.NaN, 7]);
        (value, at) = ReduceKernels.ExtremeFlat(inside, takeMin: false, omitNan: false);
        Assert.Equal(BitConverter.DoubleToInt64Bits(double.NaN), BitConverter.DoubleToInt64Bits(value));
        Assert.Equal(1, at);
    }

    [Fact]
    public void UnderOmitNanAnAllNanSliceAnswersItsOwnFirstElement()
    {
        using NumericBuffer src = Adopt([double.NaN, double.NaN]);
        (double value, int at) = ReduceKernels.ExtremeFlat(src, takeMin: true, omitNan: true);
        Assert.True(double.IsNaN(value));
        Assert.Equal(0, at);
    }

    [Fact]
    public void TiesGoToTheFirstInBothLayouts()
    {
        double[] data = [5, 2, 5, 5, 1, 5]; // rows of a 2×3: each row is [5 5 1] / [2 5 5]
        var split = new ReduceKernels.Split(2, 3, 1);
        using NumericBuffer src = Adopt(data);
        using var values = new ManagedBuffer(2);
        using var indices = new ManagedBuffer(2);
        ReduceKernels.Extreme(src, values, indices, split, takeMin: false, omitNan: true);
        Assert.Equal(0, (int)indices.AsSpan()[0]); // [5 5 1]: the first 5
        Assert.Equal(1, (int)indices.AsSpan()[1]); // [2 5 5]: still the first 5
    }

    [Fact]
    public void ForBlocksRunsSeriallyInOrderWhenToldNotToSplit()
    {
        var seen = new List<int>();
        ParallelKernels.ForBlocks(5, parallel: false, seen.Add);
        Assert.Equal([0, 1, 2, 3, 4], seen);
    }

    [Fact]
    public void ForBlocksHandsBackTheFirstExceptionItselfNotABundle()
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            AtDegree(16, () =>
            {
                ParallelKernels.ForBlocks(64, parallel: true,
                    _ => throw new InvalidOperationException("one failure"));
                return 0;
            }));
        Assert.Equal("one failure", thrown.Message);
    }

    // --- Machinery ------------------------------------------------------------------------------

    /// <summary>Data variants worth folding: clean, NaN-speckled, and a wholly-NaN slice.</summary>
    private static IEnumerable<(ReduceKernels.Split Split, double[] Data)> Cases(
        int inner, int n, int outer)
    {
        var split = new ReduceKernels.Split(inner, n, outer);
        int total = (int)split.Total;

        yield return (split, Data(total, nanEvery: 0));
        yield return (split, Data(total, nanEvery: 3));

        // The first slice entirely NaN — the omit-NaN identities' one chance to show.
        double[] blanked = Data(total, nanEvery: 5);
        for (int j = 0; j < n; j++)
        {
            blanked[j * inner] = double.NaN;
        }

        yield return (split, blanked);
    }

    private static double[] Data(int length, int nanEvery)
    {
        var data = new double[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = nanEvery > 0 && i % nanEvery == 1
                ? double.NaN
                : ((i % 9_973) * 0.001_37) - 3.5;
        }

        return data;
    }

    private static void AssertScalarKernel(
        double[] data, ReduceKernels.Split split,
        Action<NumericBuffer, NumericBuffer> kernel, Func<double[], double> reference)
    {
        using NumericBuffer src = Adopt(data);
        using var dest = new ManagedBuffer(split.Slices);
        kernel(src, dest);
        for (int s = 0; s < split.Slices; s++)
        {
            AssertBits(reference(SliceOf(data, split, s)), dest.AsSpan()[s]);
        }
    }

    private static void AssertRunningKernel(
        double[] data, ReduceKernels.Split split,
        Action<NumericBuffer, NumericBuffer> kernel, Func<double[], double[]> reference)
    {
        using NumericBuffer src = Adopt(data);
        using var dest = new ManagedBuffer((int)split.Total);
        kernel(src, dest);
        for (int s = 0; s < split.Slices; s++)
        {
            double[] expected = reference(SliceOf(data, split, s));
            for (int j = 0; j < expected.Length; j++)
            {
                AssertBits(expected[j], ElementOf(dest, split, s, j));
            }
        }
    }

    /// <summary>The boxed scalar fold: delete NaN under omit-NaN, then fold left from the seed; a
    /// slice emptied by the deletion answers the seed, which is each fold's own identity.</summary>
    private static double Fold(double[] slice, bool omitNan, double seed, Func<double, double, double> op)
    {
        double acc = seed;
        foreach (double v in Kept(slice, omitNan))
        {
            acc = op(acc, v);
        }

        return acc;
    }

    /// <summary>The boxed running fold: NaN replaced by the identity under omit-NaN, then either a
    /// seeded fold (cumsum) or one seeded from the first element (cummax), reversed by folding the
    /// reversed slice and reversing back.</summary>
    private static double[] RunningFold(
        double[] slice, bool omitNan, bool reverse, double identity, bool seedFirst,
        Func<double, double, double> op)
    {
        double[] prepared = (double[])slice.Clone();
        if (omitNan)
        {
            for (int i = 0; i < prepared.Length; i++)
            {
                if (double.IsNaN(prepared[i]))
                {
                    prepared[i] = identity;
                }
            }
        }

        if (reverse)
        {
            Array.Reverse(prepared);
        }

        var result = new double[prepared.Length];
        double acc = seedFirst ? prepared[0] : identity;
        if (seedFirst)
        {
            result[0] = acc;
        }

        for (int i = seedFirst ? 1 : 0; i < prepared.Length; i++)
        {
            acc = op(acc, prepared[i]);
            result[i] = acc;
        }

        if (reverse)
        {
            Array.Reverse(result);
        }

        return result;
    }

    /// <summary>The boxed extreme scan, reimplemented independently of the kernel's copy.</summary>
    private static (double Value, int At) ReferenceExtreme(double[] values, bool takeMin, bool omitNan)
    {
        double best = values[0];
        int at = 0;
        for (int i = 1; i < values.Length; i++)
        {
            double candidate = values[i];
            if (!omitNan && double.IsNaN(candidate))
            {
                return (double.NaN, i);
            }

            bool wins = double.IsNaN(best)
                ? !double.IsNaN(candidate)
                : takeMin ? candidate < best : candidate > best;
            if (wins)
            {
                best = candidate;
                at = i;
            }
        }

        return (best, at);
    }

    private static double[] Kept(double[] slice, bool omitNan) =>
        omitNan ? Array.FindAll(slice, static v => !double.IsNaN(v)) : slice;

    /// <summary>Slice <c>s = o·inner + i</c> gathered the way <c>JgsMatrix.SlicesAlong</c> cuts.</summary>
    private static double[] SliceOf(double[] src, ReduceKernels.Split split, int s)
    {
        int o = s / split.Inner;
        int i = s % split.Inner;
        var slice = new double[split.Count];
        for (int j = 0; j < split.Count; j++)
        {
            slice[j] = src[(o * split.Inner * split.Count) + (j * split.Inner) + i];
        }

        return slice;
    }

    private static double ElementOf(NumericBuffer dest, ReduceKernels.Split split, int s, int j)
    {
        int o = s / split.Inner;
        int i = s % split.Inner;
        return dest.AsSpan()[(o * split.Inner * split.Count) + (j * split.Inner) + i];
    }

    private static double[] ScalarResult(
        Action<NumericBuffer, NumericBuffer> kernel, NumericBuffer src, ReduceKernels.Split split)
    {
        using var dest = new ManagedBuffer(split.Slices);
        kernel(src, dest);
        return dest.AsSpan().ToArray();
    }

    private static double[] RunningResult(
        Action<NumericBuffer, NumericBuffer> kernel, NumericBuffer src, ReduceKernels.Split split)
    {
        using var dest = new ManagedBuffer((int)split.Total);
        kernel(src, dest);
        return dest.AsSpan().ToArray();
    }

    private static (double[] Values, double[] At) ExtremeResult(
        NumericBuffer src, ReduceKernels.Split split, bool takeMin)
    {
        using var values = new ManagedBuffer(split.Slices);
        using var indices = new ManagedBuffer(split.Slices);
        ReduceKernels.Extreme(src, values, indices, split, takeMin, omitNan: true);
        return (values.AsSpan().ToArray(), indices.AsSpan().ToArray());
    }

    private static void AssertSameBits(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertBits(expected[i], actual[i]);
        }
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static NumericBuffer Adopt(double[] data) => ManagedBuffer.Adopt((double[])data.Clone());

    private static T AtDegree<T>(int degree, Func<T> body)
    {
        int previous = ParallelKernels.MaxDegree;
        ParallelKernels.MaxDegree = degree;
        try
        {
            return body();
        }
        finally
        {
            ParallelKernels.MaxDegree = previous;
        }
    }
}
