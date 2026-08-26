using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M93: the milestone that hands the elementwise kernels to every core the machine has, and the two
/// claims that come with it. The first is that a thread count is not an input — the same kernel over
/// the same buffer answers the same bits at one thread and at sixteen, because the grains it is cut
/// into are a function of length and nothing else. The second is that the approximate tier, switched
/// on here for the first time, stays where the ADR says it stays: below the threshold it is the
/// scalar kernel exactly, and above it, it is within a few ulps rather than within a hope.
/// </summary>
public class ParallelKernelsM93Tests
{
    /// <summary>Long enough to be cut into three grains and to be over the threading threshold.</summary>
    private const int Threaded = ParallelKernels.MemoryBoundThreshold + 12_345;

    /// <summary>Every kernel that M93 sweeps in grains, named so a failure says which one moved.</summary>
    public static TheoryData<string> Kernels() =>
    [
        "binary-add", "binary-divide", "binary-remainder", "binary-power",
        "scalar-right-multiply", "scalar-right-power", "scalar-left-subtract", "scalar-left-divide",
        "unary-sqrt", "unary-negate", "unary-sin", "unary-scalar-log", "unary-tiered-exp",
        "compare-less", "compare-notequal", "compare-scalar-greaterequal",
        "map", "zip", "zip-scalar", "fill", "fill-constant", "copy",
    ];

    [Theory]
    [MemberData(nameof(Kernels))]
    public void EveryGrainedKernelAnswersTheSameAtOneThreadAndAtSixteen(string kernel)
    {
        using NumericBuffer a = Ramp(Threaded, 0);
        using NumericBuffer b = Ramp(Threaded, 7);

        double[] alone = AtDegree(1, () => RunInto(kernel, a, b));
        double[] together = AtDegree(16, () => RunInto(kernel, a, b));

        Assert.Equal(alone.Length, together.Length);
        for (int i = 0; i < alone.Length; i++)
        {
            // Bits, not values: a thread count that could flip a sign of zero or lose a NaN would
            // pass a value comparison and still be the bug this test exists to catch.
            Assert.Equal(BitConverter.DoubleToInt64Bits(alone[i]),
                         BitConverter.DoubleToInt64Bits(together[i]));
        }
    }

    [Fact]
    public void CountingAndCompactingAreTheSameAtEveryThreadCount()
    {
        using NumericBuffer source = Ramp(Threaded, 3);
        using var mask = new ManagedBuffer(Threaded);
        Span<double> m = mask.AsSpan();
        for (int i = 0; i < Threaded; i++)
        {
            // Uneven on purpose: each grain contributes a different number, so an offset computed
            // any way but "add the counts up in index order" lands the elements somewhere else.
            m[i] = (i % 7 == 0 || i > (Threaded / 2)) ? 1 : 0;
        }

        long aloneCount = AtDegree(1, () => PackedMath.CountNonZero(mask));
        long togetherCount = AtDegree(16, () => PackedMath.CountNonZero(mask));
        Assert.Equal(aloneCount, togetherCount);

        double[] alone = AtDegree(1, () => Compacted(source, mask, aloneCount));
        double[] together = AtDegree(16, () => Compacted(source, mask, aloneCount));
        Assert.Equal(alone, together);

        // And the order is the order a single loop would have written, which is the only order the
        // answer is allowed to be in.
        var expected = new List<double>();
        Span<double> s = source.AsSpan();
        for (int i = 0; i < Threaded; i++)
        {
            if (m[i] != 0)
            {
                expected.Add(s[i]);
            }
        }

        Assert.Equal(expected.ToArray(), together);
    }

    [Fact]
    public void ADomainCheckStillFindsANegativeSittingInTheLastGrain()
    {
        using NumericBuffer source = Ramp(Threaded, 1); // every element positive
        using var dest = new ManagedBuffer(Threaded);
        Assert.True(AtDegree(16, () => PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, source, dest)));

        source.AsSpan()[Threaded - 1] = -4;
        Assert.False(AtDegree(16, () => PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, source, dest)));
        Assert.False(AtDegree(1, () => PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, source, dest)));

        // And one in the first grain, where the parallel form has other grains already running.
        source.AsSpan()[Threaded - 1] = 1;
        source.AsSpan()[0] = -1;
        Assert.False(AtDegree(16, () => PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, source, dest)));
    }

    [Theory]
    [InlineData(PackedMath.UnaryOp.Sin)]
    [InlineData(PackedMath.UnaryOp.Cos)]
    [InlineData(PackedMath.UnaryOp.Tan)]
    [InlineData(PackedMath.UnaryOp.Exp)]
    [InlineData(PackedMath.UnaryOp.Log)]
    [InlineData(PackedMath.UnaryOp.Log10)]
    public void AnApproximateKernelStaysWithinFourUlpsOfTheOneItReplaces(PackedMath.UnaryOp op)
    {
        double[] domain = DomainFor(op);
        using NumericBuffer source = ManagedBuffer.Adopt(domain);
        using var vector = new ManagedBuffer(domain.Length);
        using var scalar = new ManagedBuffer(domain.Length);

        PackedMath.Unary(op, source, vector);
        PackedMath.UnaryScalar(op, source, scalar);

        Span<double> v = vector.AsSpan();
        Span<double> s = scalar.AsSpan();
        long worst = 0;
        double worstAt = 0;
        for (int i = 0; i < domain.Length; i++)
        {
            long apart = UlpsApart(v[i], s[i]);
            if (apart > worst)
            {
                worst = apart;
                worstAt = domain[i];
            }
        }

        Assert.True(worst <= 4, $"{op} was {worst} ulps from Math at x = {worstAt:G17}");
    }

    [Fact]
    public void TheApproximateTierIsOnAtThirtyTwoThousandAndNotBelowIt()
    {
        Assert.Equal(1 << 15, PackedMath.DefaultApproximateThreshold);
        Assert.Equal(PackedMath.DefaultApproximateThreshold, PackedMath.ApproximateThreshold);

        // Exact operations never consult the threshold; approximate ones consult nothing else.
        Assert.True(PackedMath.Vectorizes(PackedMath.UnaryOp.Sqrt, 1));
        Assert.False(PackedMath.Vectorizes(PackedMath.UnaryOp.Sin, PackedMath.ApproximateThreshold - 1));
        Assert.True(PackedMath.Vectorizes(PackedMath.UnaryOp.Sin, PackedMath.ApproximateThreshold));
    }

    [Fact]
    public void AThreadedSweepPollsOncePerGrain()
    {
        using NumericBuffer a = Ramp(Threaded, 2);
        using var dest = new ManagedBuffer(Threaded);

        int calls = 0;
        AtDegree(16, () =>
        {
            PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Add, a, 1, dest,
                () => Interlocked.Increment(ref calls));
            return 0;
        });

        int grains = ((Threaded - 1) / ParallelKernels.GrainElements) + 1;
        Assert.Equal(grains, calls);
        Assert.True(grains > 1);
    }

    [Fact]
    public void ACancelledGrainComesBackAsACancellationAndNotAsABundle()
    {
        using NumericBuffer a = Ramp(Threaded, 2);
        using var dest = new ManagedBuffer(Threaded);

        // Thrown from every grain at once, which is exactly what a cancelled statement does — and
        // what would reach the interpreter as an AggregateException if nothing unwrapped it.
        Assert.Throws<OperationCanceledException>(() => AtDegree(16, () =>
        {
            PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Add, a, 1, dest,
                () => throw new OperationCanceledException());
            return 0;
        }));
    }

    [Fact]
    public void AGrainThatFailsForSomeOtherReasonComesBackAsItself()
    {
        using NumericBuffer a = Ramp(Threaded, 2);
        using var dest = new ManagedBuffer(Threaded);

        var raised = Assert.Throws<InvalidTimeZoneException>(() => AtDegree(16, () =>
        {
            PackedMath.Map(a, dest, _ => throw new InvalidTimeZoneException("from inside a grain"));
            return 0;
        }));

        Assert.Equal("from inside a grain", raised.Message);
    }

    [Fact]
    public void TheThreadCountIsAtLeastOneAndAtMostSixtyFour()
    {
        Assert.True(ParallelKernels.MaxDegree >= 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ParallelKernels.MaxDegree = 0);
        Assert.Equal(64, AtDegree(1_000, () => ParallelKernels.MaxDegree));
    }

    /// <summary>Runs <paramref name="body"/> with the thread count pinned, and puts it back after.</summary>
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

    private static NumericBuffer Ramp(int length, int seed)
    {
        var buffer = new ManagedBuffer(length);
        Span<double> d = buffer.AsSpan();
        for (int i = 0; i < length; i++)
        {
            // Positive, spread over several decades, and never an exact power of two, so a kernel
            // that quietly rounded would show.
            d[i] = ((i + seed) % 9_973) * 0.001_37 + 0.25;
        }

        return buffer;
    }

    private static double[] Compacted(NumericBuffer source, NumericBuffer mask, long count)
    {
        using var dest = new ManagedBuffer((int)count);
        PackedMath.Compact(source, mask, dest);
        return dest.AsSpan().ToArray();
    }

    private static double[] RunInto(string kernel, NumericBuffer a, NumericBuffer b)
    {
        using var dest = new ManagedBuffer(a.Length);
        switch (kernel)
        {
            case "binary-add": PackedMath.Binary(PackedMath.BinaryOp.Add, a, b, dest); break;
            case "binary-divide": PackedMath.Binary(PackedMath.BinaryOp.Divide, a, b, dest); break;
            case "binary-remainder": PackedMath.Binary(PackedMath.BinaryOp.Remainder, a, b, dest); break;
            case "binary-power": PackedMath.Binary(PackedMath.BinaryOp.Power, a, b, dest); break;
            case "scalar-right-multiply": PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Multiply, a, 3.25, dest); break;
            case "scalar-right-power": PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Power, a, 2, dest); break;
            case "scalar-left-subtract": PackedMath.BinaryScalarLeft(PackedMath.BinaryOp.Subtract, 1.5, a, dest); break;
            case "scalar-left-divide": PackedMath.BinaryScalarLeft(PackedMath.BinaryOp.Divide, 1.5, a, dest); break;
            case "unary-sqrt": PackedMath.Unary(PackedMath.UnaryOp.Sqrt, a, dest); break;
            case "unary-negate": PackedMath.Unary(PackedMath.UnaryOp.Negate, a, dest); break;
            case "unary-sin": PackedMath.Unary(PackedMath.UnaryOp.Sin, a, dest); break;
            case "unary-scalar-log": PackedMath.UnaryScalar(PackedMath.UnaryOp.Log, a, dest); break;
            case "unary-tiered-exp": PackedMath.UnaryTiered(PackedMath.UnaryOp.Exp, a, dest); break;
            case "compare-less": PackedMath.Compare(PackedMath.CompareOp.Less, a, b, dest); break;
            case "compare-notequal": PackedMath.Compare(PackedMath.CompareOp.NotEqual, a, b, dest); break;
            case "compare-scalar-greaterequal": PackedMath.CompareScalar(PackedMath.CompareOp.GreaterEqual, a, 5, dest); break;
            case "map": PackedMath.Map(a, dest, Math.Cbrt); break;
            case "zip": PackedMath.Zip(a, b, dest, Math.Atan2); break;
            case "zip-scalar": PackedMath.ZipScalar(a, 0.75, dest, Math.Atan2); break;
            case "fill": PackedMath.Fill(dest, -3.5, 0.000_25); break;
            case "fill-constant": PackedMath.FillConstant(dest, Math.Tau); break;
            case "copy": PackedMath.Copy(a, dest); break;
            default: throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "no such kernel");
        }

        return dest.AsSpan().ToArray();
    }

    /// <summary>Where each transcendental is actually asked to work, sampled densely.</summary>
    private static double[] DomainFor(PackedMath.UnaryOp op)
    {
        const int Count = 60_000;
        var values = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            double t = i / (double)(Count - 1);
            values[i] = op switch
            {
                PackedMath.UnaryOp.Sin or PackedMath.UnaryOp.Cos => (t * 40) - 20,
                PackedMath.UnaryOp.Tan => (t * 3) - 1.5,
                PackedMath.UnaryOp.Exp => (t * 40) - 20,
                _ => Math.Pow(10, (t * 12) - 6),
            };
        }

        return values;
    }

    /// <summary>How many representable doubles lie between two of them; NaN counts as agreeing with NaN.</summary>
    private static long UlpsApart(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return double.IsNaN(a) && double.IsNaN(b) ? 0 : long.MaxValue;
        }

        if (a == b)
        {
            return 0;
        }

        if (double.IsInfinity(a) || double.IsInfinity(b))
        {
            return long.MaxValue;
        }

        long left = Ordered(BitConverter.DoubleToInt64Bits(a));
        long right = Ordered(BitConverter.DoubleToInt64Bits(b));
        return Math.Abs(left - right);

        // Sign-magnitude bits put the negatives in descending order; this makes the whole range
        // one ascending line, so subtracting two of them counts the doubles in between.
        static long Ordered(long bits) => bits < 0 ? long.MinValue - bits : bits;
    }
}
