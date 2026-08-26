using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M92: the kernels that the interpreter and the builtins were finally wired to, and the claim that
/// wiring them changes nothing. Every assertion here is <see cref="Assert.Equal(double, double)"/>
/// on raw doubles rather than a tolerance, because the point of the determinism tiers is that an
/// operation only reaches a vector kernel where the vector kernel's answer <em>is</em> the scalar
/// one — the interesting inputs are therefore the ones where two plausible implementations differ:
/// signed zero, NaN, infinity, and lengths that do not fill a vector register.
/// </summary>
public class PackedMathM92Tests
{
    private static ManagedBuffer From(params double[] values) => ManagedBuffer.Adopt(values);

    private static double[] Values(NumericBuffer buffer) => buffer.AsSpan().ToArray();

    /// <summary>The awkward doubles, plus enough filler to run past several vector registers.</summary>
    private static double[] Awkward(int length)
    {
        double[] edges =
        [
            0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            1.0, -1.0, 0.5, -0.5, 2.5, -2.5, 1.5, -1.5, double.Epsilon, -double.Epsilon,
            double.MaxValue, double.MinValue, 4.0, 9.0, 1e-320,
        ];

        var random = new Random(92);
        var values = new double[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = i < edges.Length ? edges[i] : (random.NextDouble() - 0.4) * 40;
        }

        return values;
    }

    // --- The determinism tiers ------------------------------------------------------------------

    [Theory]
    [InlineData(PackedMath.UnaryOp.Negate)]
    [InlineData(PackedMath.UnaryOp.Abs)]
    [InlineData(PackedMath.UnaryOp.Sqrt)]
    [InlineData(PackedMath.UnaryOp.Floor)]
    [InlineData(PackedMath.UnaryOp.Ceiling)]
    [InlineData(PackedMath.UnaryOp.Round)]
    public void AnExactOperationsVectorKernelIsItsScalarKernel_BitForBit(PackedMath.UnaryOp op)
    {
        Assert.Equal(PackedMath.Determinism.Exact, PackedMath.DeterminismOf(op));

        double[] source = Awkward(1_000);
        using var input = From((double[])source.Clone());
        using var vector = new ManagedBuffer(source.Length);
        using var scalar = new ManagedBuffer(source.Length);
        PackedMath.Unary(op, input, vector);
        PackedMath.UnaryScalar(op, input, scalar);

        double[] fromVector = Values(vector);
        double[] fromScalar = Values(scalar);
        for (int i = 0; i < source.Length; i++)
        {
            // BitConverter rather than Equal so that NaN counts as agreement and the two zeros do
            // not: those are exactly the places a vector form is allowed to drift and must not.
            Assert.Equal(BitConverter.DoubleToInt64Bits(fromScalar[i]),
                         BitConverter.DoubleToInt64Bits(fromVector[i]));
        }
    }

    [Theory]
    [InlineData(PackedMath.UnaryOp.Sin)]
    [InlineData(PackedMath.UnaryOp.Cos)]
    [InlineData(PackedMath.UnaryOp.Tan)]
    [InlineData(PackedMath.UnaryOp.Exp)]
    [InlineData(PackedMath.UnaryOp.Log)]
    [InlineData(PackedMath.UnaryOp.Log10)]
    public void AnApproximateOperationIsTheScalarOneUntilTheThresholdSaysOtherwise(PackedMath.UnaryOp op)
    {
        Assert.Equal(PackedMath.Determinism.Approximate, PackedMath.DeterminismOf(op));

        // M92 lands the tier plumbing switched off, so every length answers the scalar kernel and a
        // packed array cannot disagree with a boxed one. M93 lowers the threshold, with the ADR and
        // the ulp bounds that decision needs.
        Assert.False(PackedMath.Vectorizes(op, 1));
        Assert.False(PackedMath.Vectorizes(op, 100_000_000));

        double[] source = Awkward(1_000);
        using var input = From((double[])source.Clone());
        using var tiered = new ManagedBuffer(source.Length);
        using var scalar = new ManagedBuffer(source.Length);
        PackedMath.UnaryTiered(op, input, tiered);
        PackedMath.UnaryScalar(op, input, scalar);
        Assert.Equal(Values(scalar), Values(tiered));
    }

    [Theory]
    [InlineData(PackedMath.UnaryOp.Sqrt)]
    [InlineData(PackedMath.UnaryOp.Abs)]
    public void AnExactOperationTakesTheVectorKernelAtEveryLength(PackedMath.UnaryOp op)
    {
        Assert.True(PackedMath.Vectorizes(op, 1));
        Assert.True(PackedMath.Vectorizes(op, 100_000_000));
    }

    [Fact]
    public void TheScalarKernelIsTheFunctionTheBoxedInterpreterCalls()
    {
        double[] source = Awkward(300);
        using var input = From((double[])source.Clone());
        using var dest = new ManagedBuffer(source.Length);

        (PackedMath.UnaryOp Op, Func<double, double> Same)[] pairs =
        [
            (PackedMath.UnaryOp.Negate, static x => -x),
            (PackedMath.UnaryOp.Abs, Math.Abs),
            (PackedMath.UnaryOp.Sqrt, Math.Sqrt),
            (PackedMath.UnaryOp.Floor, Math.Floor),
            (PackedMath.UnaryOp.Ceiling, Math.Ceiling),
            (PackedMath.UnaryOp.Sin, Math.Sin),
            (PackedMath.UnaryOp.Cos, Math.Cos),
            (PackedMath.UnaryOp.Tan, Math.Tan),
            (PackedMath.UnaryOp.Exp, Math.Exp),
            (PackedMath.UnaryOp.Log, Math.Log),
            (PackedMath.UnaryOp.Log10, Math.Log10),
        ];

        foreach ((PackedMath.UnaryOp op, Func<double, double> same) in pairs)
        {
            PackedMath.UnaryScalar(op, input, dest);
            double[] result = Values(dest);
            for (int i = 0; i < source.Length; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(same(source[i])),
                             BitConverter.DoubleToInt64Bits(result[i]));
            }
        }
    }

    // --- The fused domain check -----------------------------------------------------------------

    [Fact]
    public void AtLeastRunsTheWholeArrayWhenNothingIsBelowTheBound()
    {
        double[] source = new double[5_000];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = i * 0.25;
        }

        source[3] = double.NaN;                  // NaN belongs to every real domain here
        source[4] = double.PositiveInfinity;

        using var input = From((double[])source.Clone());
        using var dest = new ManagedBuffer(source.Length);
        Assert.True(PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, input, dest));

        double[] result = Values(dest);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Sqrt(source[i])),
                         BitConverter.DoubleToInt64Bits(result[i]));
        }
    }

    [Theory]
    [InlineData(0)]      // the first tile
    [InlineData(9_000)]  // a later one, past the tile boundary
    [InlineData(19_999)] // the last element there is
    public void AtLeastDeclinesWhereverTheBoundIsBroken(int where)
    {
        double[] source = new double[20_000];
        Array.Fill(source, 4.0);
        source[where] = -1e-300;

        using var input = From(source);
        using var dest = new ManagedBuffer(source.Length);
        Assert.False(PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, input, dest));
    }

    [Fact]
    public void AtLeastIsNotFooledByANaNSittingBesideANegative()
    {
        // A minimum would be NaN for this tile and would let the -4 through, answering NaN where
        // MATLAB answers 2i. The domain test asks whether anything is below the bound, not what the
        // smallest thing is.
        double[] source = new double[5_000];
        Array.Fill(source, 1.0);
        source[10] = double.NaN;
        source[11] = -4.0;

        using var input = From(source);
        using var dest = new ManagedBuffer(source.Length);
        Assert.False(PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, input, dest));
    }

    [Fact]
    public void AtLeastAcceptsNegativeZeroAsBeingAtTheBound()
    {
        // -0.0 is not less than 0, and sqrt(-0.0) is -0.0 rather than a complex number, which is why
        // the domain predicate this replaces admitted it too.
        using var input = From(-0.0, 1.0, 4.0);
        using var dest = new ManagedBuffer(3);
        Assert.True(PackedMath.TryUnaryAtLeast(PackedMath.UnaryOp.Sqrt, 0, input, dest));
        Assert.True(double.IsNegative(Values(dest)[0]));
    }

    // --- Comparison ------------------------------------------------------------------------------

    [Theory]
    [InlineData(PackedMath.CompareOp.Less)]
    [InlineData(PackedMath.CompareOp.LessEqual)]
    [InlineData(PackedMath.CompareOp.Greater)]
    [InlineData(PackedMath.CompareOp.GreaterEqual)]
    [InlineData(PackedMath.CompareOp.Equal)]
    [InlineData(PackedMath.CompareOp.NotEqual)]
    public void ComparisonAgreesWithTheOperatorItNames(PackedMath.CompareOp op)
    {
        // 1,003 is deliberately not a multiple of any vector width, so the scalar tail runs too.
        double[] a = Awkward(1_003);
        double[] b = Awkward(1_003);
        Array.Reverse(b);
        b[0] = a[0];
        b[1] = a[1];
        b[2] = a[2];

        using var left = From((double[])a.Clone());
        using var right = From((double[])b.Clone());
        using var dest = new ManagedBuffer(a.Length);
        PackedMath.Compare(op, left, right, dest);

        double[] mask = Values(dest);
        for (int i = 0; i < a.Length; i++)
        {
            bool expected = op switch
            {
                PackedMath.CompareOp.Less => a[i] < b[i],
                PackedMath.CompareOp.LessEqual => a[i] <= b[i],
                PackedMath.CompareOp.Greater => a[i] > b[i],
                PackedMath.CompareOp.GreaterEqual => a[i] >= b[i],
                PackedMath.CompareOp.Equal => a[i] == b[i],
                _ => a[i] != b[i],
            };

            Assert.Equal(expected ? 1.0 : 0.0, mask[i]);
        }
    }

    [Theory]
    [InlineData(PackedMath.CompareOp.Less, false)]
    [InlineData(PackedMath.CompareOp.Less, true)]
    [InlineData(PackedMath.CompareOp.GreaterEqual, false)]
    [InlineData(PackedMath.CompareOp.GreaterEqual, true)]
    [InlineData(PackedMath.CompareOp.NotEqual, true)]
    public void ScalarComparisonReadsFromTheSideItWasGiven(PackedMath.CompareOp op, bool scalarOnLeft)
    {
        double[] a = Awkward(1_003);
        const double Scalar = 1.5;

        using var input = From((double[])a.Clone());
        using var dest = new ManagedBuffer(a.Length);
        PackedMath.CompareScalar(op, input, Scalar, dest, scalarOnLeft);

        double[] mask = Values(dest);
        for (int i = 0; i < a.Length; i++)
        {
            (double left, double right) = scalarOnLeft ? (Scalar, a[i]) : (a[i], Scalar);
            bool expected = op switch
            {
                PackedMath.CompareOp.Less => left < right,
                PackedMath.CompareOp.GreaterEqual => left >= right,
                _ => left != right,
            };

            Assert.Equal(expected ? 1.0 : 0.0, mask[i]);
        }
    }

    // --- Counting and compacting -----------------------------------------------------------------

    [Fact]
    public void CountNonZeroCountsWhatIsNotZero()
    {
        using var buffer = From(1, 0, -0.0, double.NaN, -3, 0, double.PositiveInfinity);

        // NaN is not zero and -0.0 is: both are what `!= 0` says of them, and what nnz has to answer.
        Assert.Equal(4, PackedMath.CountNonZero(buffer));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(1_003)]
    [InlineData(70_000)]
    public void CountNonZeroAgreesWithTheLoopAtEveryLength(int length)
    {
        var random = new Random(length);
        double[] values = new double[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = random.Next(3) == 0 ? 0 : random.NextDouble() - 0.5;
        }

        long expected = 0;
        foreach (double v in values)
        {
            if (v != 0)
            {
                expected++;
            }
        }

        using var buffer = From(values);
        Assert.Equal(expected, PackedMath.CountNonZero(buffer));
    }

    [Fact]
    public void MinAndMaxAreTheScalarFoldsAnswer_IncludingTheTwoZeros()
    {
        // The substitution of Min for a hand-written fold rests on the reduction being order-free.
        // It is, but only if the vector kernel keeps Math.Min's two tie-breaks: NaN beats everything,
        // and negative zero is below positive zero even though the two compare equal.
        double[] zeros = new double[64];
        zeros[40] = -0.0;
        using var buffer = From(zeros);

        double fold = zeros[0];
        foreach (double v in zeros)
        {
            fold = Math.Min(fold, v);
        }

        Assert.True(double.IsNegative(fold));
        Assert.Equal(BitConverter.DoubleToInt64Bits(fold),
                     BitConverter.DoubleToInt64Bits(PackedMath.Min(buffer)));
    }

    [Fact]
    public void CompactCopiesTheElementsTheMaskPicked()
    {
        using var source = From(10, 20, 30, 40, 50);
        using var mask = From(0, 1, 1, 0, 1);
        using var dest = new ManagedBuffer(3);
        Assert.Equal(3, PackedMath.Compact(source, mask, dest));
        Assert.Equal([20, 30, 50], Values(dest));
    }

    [Fact]
    public void CompactMatchesGatheringThroughPositions()
    {
        var random = new Random(4);
        double[] values = new double[70_003];
        double[] flags = new double[values.Length];
        var expected = new List<double>();
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = random.NextDouble();
            flags[i] = random.Next(2);
            if (flags[i] != 0)
            {
                expected.Add(values[i]);
            }
        }

        using var source = From(values);
        using var mask = From(flags);
        using var dest = new ManagedBuffer(expected.Count);
        Assert.Equal(expected.Count, PackedMath.Compact(source, mask, dest));
        Assert.Equal(expected.ToArray(), Values(dest));
    }

    // --- Zip --------------------------------------------------------------------------------------

    [Fact]
    public void ZipAppliesTheDelegateToBothOperands()
    {
        using var a = From(1, 2, 3);
        using var b = From(10, 20, 30);
        using var dest = new ManagedBuffer(3);
        PackedMath.Zip(a, b, dest, static (x, y) => x + (y * 2));
        Assert.Equal([21, 42, 63], Values(dest));
    }

    [Fact]
    public void ZipScalarKnowsWhichSideTheScalarIsOn()
    {
        using var a = From(1, 2, 3);
        using var dest = new ManagedBuffer(3);

        PackedMath.ZipScalar(a, 10, dest, static (x, y) => x - y);
        Assert.Equal([-9, -8, -7], Values(dest));

        PackedMath.ZipScalar(a, 10, dest, static (x, y) => x - y, scalarOnLeft: true);
        Assert.Equal([9, 8, 7], Values(dest));
    }
}
