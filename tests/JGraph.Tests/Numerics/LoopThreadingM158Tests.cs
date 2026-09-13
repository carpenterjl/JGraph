using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// ADR 0158 (item 11): the loops the gap-closure plan threads, and the one claim each of them
/// makes — that the thread count and the grain are not inputs. An expensive map cut into 4K
/// grains answers the bits the 64K grains answered; one contiguous slice differenced by output
/// index answers the scalar loop's bits, signed zeros and NaN payloads included; a stable rank
/// sort split into buckets answers the library sort plus its tie repair; a streaming finite range
/// merged in grain order answers the serial <c>&lt;</c> fold, whichever zero came first.
/// </summary>
public class LoopThreadingM158Tests
{
    public static TheoryData<int> ExpensiveLengths() =>
    [
        ParallelKernels.ExpensiveThreshold,
        200_000,
        ParallelKernels.ComputeBoundThreshold,
        1 << 20,
    ];

    [Theory]
    [MemberData(nameof(ExpensiveLengths))]
    public void AnExpensiveMapAnswersTheSameBitsAtEveryThreadCountAndGrain(int length)
    {
        using NumericBuffer x = Arguments(length);
        double[] reference = AtDegree(1, () => MapInto(x, ParallelKernels.DefaultCostlyGrain, PackedMath.CostClass.Compute));

        foreach (int grain in new[] { 1000, ParallelKernels.DefaultCostlyGrain, 1 << 14, ParallelKernels.GrainElements })
        {
            AssertBitsEach(reference, AtDegree(1, () => MapInto(x, grain, PackedMath.CostClass.Expensive)));
            AssertBitsEach(reference, AtDegree(16, () => MapInto(x, grain, PackedMath.CostClass.Expensive)));
        }
    }

    [Fact]
    public void AnExpensiveZipAnswersTheSameBitsAtEveryThreadCount()
    {
        const int length = 200_000;
        using NumericBuffer x = Arguments(length);
        using var dest = new ManagedBuffer(length);

        double[] alone = AtDegree(1, () =>
        {
            PackedMath.ZipScalar(x, 0.5, dest, static (v, nu) => BesselFunctions.K(nu, v), cost: PackedMath.CostClass.Expensive);
            return dest.AsSpan().ToArray();
        });
        double[] together = AtDegree(16, () =>
        {
            PackedMath.ZipScalar(x, 0.5, dest, static (v, nu) => BesselFunctions.K(nu, v), cost: PackedMath.CostClass.Expensive);
            return dest.AsSpan().ToArray();
        });

        AssertBitsEach(alone, together);
        Assert.Equal(BesselFunctions.K(0.5, x.AsSpan()[7]), together[7]);
    }

    [Fact]
    public void ForCostlyCutsAtTheCostlyGrainAndThreadsFromTheExpensiveThreshold()
    {
        int previous = ParallelKernels.CostlyGrain;
        try
        {
            ParallelKernels.CostlyGrain = 1000;
            var starts = new List<(int Start, int Length)>();
            AtDegree(1, () =>
            {
                ParallelKernels.ForCostly(2500, null, (start, len) =>
                {
                    lock (starts)
                    {
                        starts.Add((start, len));
                    }
                });
                return 0;
            });

            Assert.Equal([(0, 1000), (1000, 1000), (2000, 500)], starts.OrderBy(s => s.Start));

            // Below the expensive threshold the grains still run, on the calling thread.
            int caller = Environment.CurrentManagedThreadId;
            var threads = new HashSet<int>();
            AtDegree(16, () =>
            {
                ParallelKernels.ForCostly(ParallelKernels.ExpensiveThreshold - 1, null, (_, _) =>
                {
                    lock (threads)
                    {
                        threads.Add(Environment.CurrentManagedThreadId);
                    }
                });
                return 0;
            });
            Assert.Equal([caller], threads);
        }
        finally
        {
            ParallelKernels.CostlyGrain = previous;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ParallelKernels.CostlyGrain = 0);
    }

    [Fact]
    public void OneContiguousSliceDifferencedByOutputIndexAnswersTheScalarLoopsBits()
    {
        int n = ParallelKernels.MemoryBoundThreshold + 12_345;
        var data = new double[n];
        for (int i = 0; i < n; i++)
        {
            data[i] = ((i % 9_973) * 0.001_37) - 3.5;
        }

        // Every pair whose subtraction has a sign or a payload to get wrong, placed on and off the
        // grain boundaries.
        double payload = BitConverter.Int64BitsToDouble(0x7ff8_0000_0000_0123);
        double[] specials = [0.0, -0.0, 0.0, -0.0, payload, 1.0, payload, -payload, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, 2.0];
        for (int k = 0; k < specials.Length; k++)
        {
            data[k] = specials[k];
            data[ParallelKernels.GrainElements - 6 + k] = specials[k];
            data[n - specials.Length + k] = specials[k];
        }

        var expected = new double[n - 1];
        for (int j = 0; j < n - 1; j++)
        {
            expected[j] = data[j + 1] - data[j];
        }

        using NumericBuffer src = ManagedBuffer.Adopt(data);
        var split = new ReduceKernels.Split(1, n, 1);
        foreach (int degree in new[] { 1, 16 })
        {
            using var dest = new ManagedBuffer(n - 1);
            AtDegree(degree, () =>
            {
                ReduceKernels.Differences(src, dest, split);
                return 0;
            });
            AssertBitsEach(expected, dest.AsSpan().ToArray());
        }
    }

    [Fact]
    public void ManyContiguousSlicesDifferencedAnswerTheScalarLoopsBits()
    {
        var split = new ReduceKernels.Split(1, 5, 7);
        double[] data = [.. Enumerable.Range(0, 35).Select(i => i % 4 == 0 ? -0.0 : (i * 0.37) - 6)];
        using NumericBuffer src = ManagedBuffer.Adopt(data);
        using var dest = new ManagedBuffer(4 * 7);
        ReduceKernels.Differences(src, dest, split);
        for (int s = 0; s < 7; s++)
        {
            for (int j = 0; j < 4; j++)
            {
                AssertBits(data[(s * 5) + j + 1] - data[(s * 5) + j], dest.AsSpan()[(s * 4) + j]);
            }
        }
    }

    [Theory]
    [InlineData(300_000, 1 << 18)]   // above the threshold as shipped
    [InlineData(300_000, 1000)]      // and with the threshold dropped, so the buckets are many
    [InlineData(5_000, 1000)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void SortRanksIsTheStableSortWhateverTheThresholdAndThreadCount(int n, int threshold)
    {
        int previous = SortKernels.RankThreshold;
        try
        {
            SortKernels.RankThreshold = threshold;
            foreach ((string what, ulong[] keys) in RankCases(n))
            {
                (ulong[] expectedKeys, int[] expectedPayload) = ReferenceStableSort(keys);
                foreach (int degree in new[] { 1, 16 })
                {
                    ulong[] k = (ulong[])keys.Clone();
                    int[] p = Enumerable.Range(0, n).ToArray();
                    AtDegree(degree, () =>
                    {
                        SortKernels.SortRanks(k, p, n);
                        return 0;
                    });
                    Assert.True(expectedKeys.SequenceEqual(k), $"{what}: keys at {degree} thread(s)");
                    Assert.True(expectedPayload.SequenceEqual(p), $"{what}: payload at {degree} thread(s)");
                }
            }
        }
        finally
        {
            SortKernels.RankThreshold = previous;
        }
    }

    [Fact]
    public void SortRanksRefusesACountBeyondTheArrays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SortKernels.SortRanks(new ulong[3], new int[3], 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SortKernels.SortRanks(new ulong[3], new int[2], 3));
    }

    [Fact]
    public void FiniteRangeIsTheSerialFoldWithTheFirstZeroWinning()
    {
        int n = ParallelKernels.MemoryBoundThreshold + 4_321;
        foreach (bool minusFirst in new[] { true, false })
        {
            var data = new double[n];
            for (int i = 0; i < n; i++)
            {
                data[i] = 0.25 + (((i * 7919L) % 10_007) * 0.001);
            }

            data[3] = double.NaN;
            data[10] = double.PositiveInfinity;
            data[11] = double.NegativeInfinity;
            data[ParallelKernels.GrainElements + 5] = minusFirst ? -0.0 : 0.0;
            data[(2 * ParallelKernels.GrainElements) + 9] = minusFirst ? 0.0 : -0.0;
            data[n - 2] = 12.5;

            (double low, double high, bool any) = SerialRange(data);
            using NumericBuffer buffer = ManagedBuffer.Adopt(data);
            foreach (int degree in new[] { 1, 16 })
            {
                (double l, double h, bool a) = AtDegree(degree, () => PackedMath.FiniteRange(buffer));
                Assert.True(a == any, "any");
                AssertBits(low, l);
                AssertBits(high, h);
            }

            Assert.Equal(12.5, high);
            Assert.Equal(minusFirst, double.IsNegative(low));
        }

        using var nothing = ManagedBuffer.Adopt([double.NaN, double.PositiveInfinity]);
        Assert.False(PackedMath.FiniteRange(nothing).Any);
        using var empty = new ManagedBuffer(0);
        Assert.False(PackedMath.FiniteRange(empty).Any);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static double[] MapInto(NumericBuffer x, int grain, PackedMath.CostClass cost)
    {
        int previous = ParallelKernels.CostlyGrain;
        ParallelKernels.CostlyGrain = grain;
        try
        {
            using var dest = new ManagedBuffer(x.Length);
            PackedMath.Map(x, dest, static v => BesselFunctions.I(0, v), cost: cost);
            return dest.AsSpan().ToArray();
        }
        finally
        {
            ParallelKernels.CostlyGrain = previous;
        }
    }

    private static NumericBuffer Arguments(int length)
    {
        var buffer = new ManagedBuffer(length);
        Span<double> x = buffer.AsSpan();
        for (int i = 0; i < length; i++)
        {
            x[i] = 0.025 + (4.975 * (((i * 0.618033988749895) % 1.0)));
        }

        return buffer;
    }

    private static IEnumerable<(string What, ulong[] Keys)> RankCases(int n)
    {
        var random = new Random(158);
        var few = new ulong[n];
        var many = new ulong[n];
        var same = new ulong[n];
        var extremes = new ulong[n];
        var sorted = new ulong[n];
        for (int i = 0; i < n; i++)
        {
            few[i] = (ulong)random.Next(0, 5);
            many[i] = ((ulong)(uint)random.Next() << 8) | (uint)random.Next(0, 3);
            same[i] = 0x8000_0000_0000_0000UL;
            extremes[i] = (i % 3) switch { 0 => ulong.MaxValue, 1 => 0, _ => (ulong)random.Next() };
            sorted[i] = (ulong)i / 3;
        }

        yield return ("five distinct keys", few);
        yield return ("many keys with ties", many);
        yield return ("every key the same", same);
        yield return ("extremes", extremes);
        yield return ("already sorted with ties", sorted);
    }

    private static (ulong[] Keys, int[] Payload) ReferenceStableSort(ulong[] keys)
    {
        var order = Enumerable.Range(0, keys.Length).OrderBy(i => keys[i]).ThenBy(i => i).ToArray();
        return (order.Select(i => keys[i]).ToArray(), order);
    }

    private static (double Low, double High, bool Any) SerialRange(double[] data)
    {
        double low = 0;
        double high = 0;
        bool any = false;
        foreach (double v in data)
        {
            if (!double.IsFinite(v))
            {
                continue;
            }

            if (!any)
            {
                (low, high, any) = (v, v, true);
                continue;
            }

            if (v < low)
            {
                low = v;
            }

            if (v > high)
            {
                high = v;
            }
        }

        return (low, high, any);
    }

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

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private static void AssertBitsEach(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(expected[i]) != BitConverter.DoubleToInt64Bits(actual[i]))
            {
                Assert.Fail($"element {i}: {expected[i]:R} ({BitConverter.DoubleToInt64Bits(expected[i]):x16}) vs {actual[i]:R} ({BitConverter.DoubleToInt64Bits(actual[i]):x16})");
            }
        }
    }
}
