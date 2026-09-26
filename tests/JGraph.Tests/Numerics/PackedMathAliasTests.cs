using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The kernel alias contract (Z2 of the value-ownership plan, ADR 0173): every elementwise binary
/// kernel may be handed one of its own operands as the destination and answers, bit for bit, what
/// it answers into a fresh one. The reuse roads in the interpreter (a fresh temporary reused as the
/// destination, <c>v = v op E</c> written into <c>v</c>) rest on nothing else.
/// </summary>
/// <remarks>
/// Every operation × every arrangement (array-array, scalar on the right, scalar on the left) ×
/// every aliasing the arrangement admits (the left operand, the right one, both — <c>x op x</c>
/// into <c>x</c> — and neither, the reference) × one grain and several, on random operands with a
/// few of the values that make a kernel's shortcuts visible (zeros, negative zero, infinities,
/// NaN, a subnormal). The comparison is on the bits, so <c>-0</c> against <c>0</c> and a NaN's
/// payload count.
/// </remarks>
public class PackedMathAliasTests
{
    private const int OneGrain = 1_000;
    private const int ManyGrains = (3 * ParallelKernels.GrainElements) + 17;

    public static IEnumerable<object[]> Cases()
    {
        foreach (PackedMath.BinaryOp op in Enum.GetValues<PackedMath.BinaryOp>())
        {
            foreach (int length in new[] { OneGrain, ManyGrains })
            {
                yield return [op, length];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArrayArray_IntoLeft_MatchesAFreshDestination(PackedMath.BinaryOp op, int length)
    {
        double[] a = Operands(length, 11);
        double[] b = Operands(length, 23);
        long[] expected = Bits(Reference(op, a, b));

        using var left = ManagedBuffer.Adopt((double[])a.Clone());
        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.Binary(op, left, right, left);

        Assert.Equal(expected, Bits(left.AsSpan().ToArray()));
        Assert.Equal(b, right.AsSpan().ToArray()); // the other operand is untouched
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArrayArray_IntoRight_MatchesAFreshDestination(PackedMath.BinaryOp op, int length)
    {
        double[] a = Operands(length, 31);
        double[] b = Operands(length, 47);
        long[] expected = Bits(Reference(op, a, b));

        using var left = ManagedBuffer.Adopt((double[])a.Clone());
        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.Binary(op, left, right, right);

        Assert.Equal(expected, Bits(right.AsSpan().ToArray()));
        Assert.Equal(a, left.AsSpan().ToArray());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArrayArray_IntoBoth_MatchesAFreshDestination(PackedMath.BinaryOp op, int length)
    {
        double[] a = Operands(length, 53);
        long[] expected = Bits(Reference(op, a, a));

        using var x = ManagedBuffer.Adopt((double[])a.Clone());
        PackedMath.Binary(op, x, x, x);

        Assert.Equal(expected, Bits(x.AsSpan().ToArray()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScalarRight_IntoTheArray_MatchesAFreshDestination(PackedMath.BinaryOp op, int length)
    {
        double[] a = Operands(length, 61);
        const double scalar = 0.37;
        long[] expected = Bits(ReferenceScalarRight(op, a, scalar));

        using var left = ManagedBuffer.Adopt((double[])a.Clone());
        PackedMath.BinaryScalarRight(op, left, scalar, left);

        Assert.Equal(expected, Bits(left.AsSpan().ToArray()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScalarLeft_IntoTheArray_MatchesAFreshDestination(PackedMath.BinaryOp op, int length)
    {
        double[] b = Operands(length, 67);
        const double scalar = -2.5;
        long[] expected = Bits(ReferenceScalarLeft(op, scalar, b));

        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.BinaryScalarLeft(op, scalar, right, right);

        Assert.Equal(expected, Bits(right.AsSpan().ToArray()));
    }

    /// <summary>
    /// The scalar-left subtract and divide are the two that used to fill the destination with the
    /// scalar first: their one-pass forms answer the scalar loop's bits with a fresh destination
    /// too, so the rewrite changed nothing an unaliased caller can see.
    /// </summary>
    [Theory]
    [InlineData(PackedMath.BinaryOp.Subtract)]
    [InlineData(PackedMath.BinaryOp.Divide)]
    public void ScalarLeft_FreshDestination_MatchesTheScalarLoop(PackedMath.BinaryOp op)
    {
        double[] b = Operands(ManyGrains, 71);
        const double scalar = 3.75;
        long[] expected = Bits(ReferenceScalarLeft(op, scalar, b));

        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        using var dest = new ManagedBuffer(b.Length);
        PackedMath.BinaryScalarLeft(op, scalar, right, dest);

        Assert.Equal(expected, Bits(dest.AsSpan().ToArray()));
        Assert.Equal(b, right.AsSpan().ToArray());
    }

    /// <summary>The Rounding forms keep the contract too, aliased on either side.</summary>
    [Theory]
    [InlineData(PackedMath.BinaryOp.Add)]
    [InlineData(PackedMath.BinaryOp.Multiply)]
    public void RoundingForms_Aliased_MatchAFreshDestination(PackedMath.BinaryOp op)
    {
        double[] a = Operands(ManyGrains, 73);
        double[] b = Operands(ManyGrains, 79);
        PackedMath.Rounding into = PackedMath.Rounding.Between(-128, 127);

        using var freshA = ManagedBuffer.Adopt((double[])a.Clone());
        using var freshB = ManagedBuffer.Adopt((double[])b.Clone());
        using var fresh = new ManagedBuffer(a.Length);
        PackedMath.Binary(op, freshA, freshB, fresh, into);
        long[] expected = Bits(fresh.AsSpan().ToArray());

        using var left = ManagedBuffer.Adopt((double[])a.Clone());
        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.Binary(op, left, right, left, into);
        Assert.Equal(expected, Bits(left.AsSpan().ToArray()));

        using var left2 = ManagedBuffer.Adopt((double[])a.Clone());
        using var right2 = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.Binary(op, left2, right2, right2, into);
        Assert.Equal(expected, Bits(right2.AsSpan().ToArray()));

        using var scalarSide = ManagedBuffer.Adopt((double[])b.Clone());
        using var scalarFresh = new ManagedBuffer(b.Length);
        PackedMath.BinaryScalarLeft(op, 2.5, scalarSide, scalarFresh, into);
        long[] scalarExpected = Bits(scalarFresh.AsSpan().ToArray());
        PackedMath.BinaryScalarLeft(op, 2.5, scalarSide, scalarSide, into);
        Assert.Equal(scalarExpected, Bits(scalarSide.AsSpan().ToArray()));
    }

    /// <summary>
    /// The fused <c>a ∓ b·s</c> (Z2d) answers the two operators' bits — the product rounded, then
    /// the sum — into a fresh destination, into <c>a</c> and into <c>b</c>.
    /// </summary>
    [Theory]
    [InlineData(true, OneGrain)]
    [InlineData(false, OneGrain)]
    [InlineData(true, ManyGrains)]
    [InlineData(false, ManyGrains)]
    public void ScaleAccumulate_MatchesTheTwoOperatorsIntoEveryDestination(bool subtract, int length)
    {
        double[] a = Operands(length, 83);
        double[] b = Operands(length, 89);
        const double scalar = 0.37;
        var expected = new long[length];
        for (int i = 0; i < length; i++)
        {
            double product = b[i] * scalar;
            expected[i] = BitConverter.DoubleToInt64Bits(subtract ? a[i] - product : a[i] + product);
        }

        using var freshA = ManagedBuffer.Adopt((double[])a.Clone());
        using var freshB = ManagedBuffer.Adopt((double[])b.Clone());
        using var fresh = new ManagedBuffer(length);
        PackedMath.ScaleAccumulate(freshA, freshB, scalar, subtract, fresh);
        Assert.Equal(expected, Bits(fresh.AsSpan().ToArray()));
        Assert.Equal(a, freshA.AsSpan().ToArray());
        Assert.Equal(b, freshB.AsSpan().ToArray());

        using var intoA = ManagedBuffer.Adopt((double[])a.Clone());
        using var besideA = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.ScaleAccumulate(intoA, besideA, scalar, subtract, intoA);
        Assert.Equal(expected, Bits(intoA.AsSpan().ToArray()));

        using var besideB = ManagedBuffer.Adopt((double[])a.Clone());
        using var intoB = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.ScaleAccumulate(besideB, intoB, scalar, subtract, intoB);
        Assert.Equal(expected, Bits(intoB.AsSpan().ToArray()));

        // Threaded from the first grain (Z2c's reuse-road threshold at its lowest): the same bits,
        // aliased, because the grains are fixed and every element is its own.
        using var threadedA = ManagedBuffer.Adopt((double[])a.Clone());
        using var threadedB = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.ScaleAccumulate(threadedA, threadedB, scalar, subtract, threadedA, null, threshold: 1);
        Assert.Equal(expected, Bits(threadedA.AsSpan().ToArray()));
    }

    /// <summary>The in-place binary sweeps threaded from the first grain (Z2c) answer the serial bits, aliased.</summary>
    [Theory]
    [InlineData(PackedMath.BinaryOp.Subtract)]
    [InlineData(PackedMath.BinaryOp.Divide)]
    public void ThreadedAliasedBinary_MatchesTheSerialBits(PackedMath.BinaryOp op)
    {
        double[] a = Operands(ManyGrains, 97);
        double[] b = Operands(ManyGrains, 101);
        long[] expected = Bits(Reference(op, a, b));

        using var left = ManagedBuffer.Adopt((double[])a.Clone());
        using var right = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.Binary(op, left, right, left, PackedMath.Rounding.None, null, threshold: 1);
        Assert.Equal(expected, Bits(left.AsSpan().ToArray()));

        long[] scalarLeft = Bits(ReferenceScalarLeft(op, 2.5, b));
        using var only = ManagedBuffer.Adopt((double[])b.Clone());
        PackedMath.BinaryScalarLeft(op, 2.5, only, only, PackedMath.Rounding.None, null, threshold: 1);
        Assert.Equal(scalarLeft, Bits(only.AsSpan().ToArray()));
    }

    // ---- the operands and the references -------------------------------------------------------

    /// <summary>
    /// Random operands over a few decades, with the values a kernel could special-case placed at
    /// the front and again past the first grain boundary.
    /// </summary>
    private static double[] Operands(int length, int seed)
    {
        var random = new Random(seed);
        var values = new double[length];
        for (int i = 0; i < length; i++)
        {
            double magnitude = Math.Pow(10, (random.NextDouble() * 6) - 3);
            values[i] = (random.NextDouble() < 0.5 ? -1 : 1) * magnitude;
        }

        double[] edges = [0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1e-310, 1.0, -1.0, 2.0, 0.5];
        for (int i = 0; i < edges.Length; i++)
        {
            values[i] = edges[i];
            if (length > ParallelKernels.GrainElements + edges.Length)
            {
                values[ParallelKernels.GrainElements + i] = edges[edges.Length - 1 - i];
            }
        }

        return values;
    }

    private static double[] Reference(PackedMath.BinaryOp op, double[] a, double[] b)
    {
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = Apply(op, a[i], b[i]);
        }

        return result;
    }

    private static double[] ReferenceScalarRight(PackedMath.BinaryOp op, double[] a, double scalar)
    {
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = Apply(op, a[i], scalar);
        }

        return result;
    }

    private static double[] ReferenceScalarLeft(PackedMath.BinaryOp op, double scalar, double[] b)
    {
        var result = new double[b.Length];
        for (int i = 0; i < b.Length; i++)
        {
            result[i] = Apply(op, scalar, b[i]);
        }

        return result;
    }

    private static double Apply(PackedMath.BinaryOp op, double x, double y) => op switch
    {
        PackedMath.BinaryOp.Add => x + y,
        PackedMath.BinaryOp.Subtract => x - y,
        PackedMath.BinaryOp.Multiply => x * y,
        PackedMath.BinaryOp.Divide => x / y,
        PackedMath.BinaryOp.Remainder => x % y,
        PackedMath.BinaryOp.Power => Math.Pow(x, y),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static long[] Bits(double[] values)
    {
        var bits = new long[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bits[i] = BitConverter.DoubleToInt64Bits(values[i]);
        }

        return bits;
    }
}
