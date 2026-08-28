using System;
using System.Collections.Generic;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M97: what an integer class does to an element, done a register at a time. The vector kernel does
/// not approximate <see cref="Math.Round(double, MidpointRounding)"/> followed by
/// <see cref="Math.Clamp(double, double, double)"/> — it claims to be them, for every double there
/// is — so every test here compares bits, never distances, and the grid it compares over is built
/// out of the values that break the obvious implementations: the tie, the value one ulp below the
/// tie, the magnitude past which a double has no fraction to round, the infinities, the NaN, both
/// zeros, and each class boundary with a step either side of it.
/// </summary>
public class RoundClampM97Tests
{
    /// <summary>The eight integer classes, by the range they saturate into.</summary>
    public static TheoryData<string, double, double> Classes() => new()
    {
        { "int8", sbyte.MinValue, sbyte.MaxValue },
        { "int16", short.MinValue, short.MaxValue },
        { "int32", int.MinValue, int.MaxValue },
        { "int64", long.MinValue, long.MaxValue },
        { "uint8", 0, byte.MaxValue },
        { "uint16", 0, ushort.MaxValue },
        { "uint32", 0, uint.MaxValue },
        { "uint64", 0, ulong.MaxValue },
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void EveryEdgeRoundsAndSaturatesToTheSameBitsTheScalarSpellingGives(
        string name, double min, double max)
    {
        double[] grid = EdgeGrid(min, max);
        PackedMath.Rounding rule = PackedMath.Rounding.Between(min, max);

        // Offsets so the value under test lands in the vector body and in the scalar tail alike.
        for (int lead = 0; lead < 9; lead++)
        {
            var padded = new double[grid.Length + lead];
            Array.Fill(padded, 1.0);
            Array.Copy(grid, 0, padded, lead, grid.Length);

            using var buffer = new ManagedBuffer(padded.Length);
            padded.AsSpan().CopyTo(buffer.AsSpan());
            using var into = new ManagedBuffer(padded.Length);
            PackedMath.Round(buffer, into, rule);

            Span<double> got = into.AsSpan();
            for (int i = 0; i < padded.Length; i++)
            {
                double want = Reference(padded[i], min, max);
                Assert.True(
                    Same(want, got[i]),
                    $"{name} lead {lead} element {i}: {padded[i]:R} gave {got[i]:R}, wanted {want:R}");
            }
        }
    }

    /// <summary>
    /// The scalar arm of the struct is the specification the span kernels answer to, so it has to
    /// agree with the spelling it is a copy of before anything else here means very much.
    /// </summary>
    [Theory]
    [MemberData(nameof(Classes))]
    public void TheStructsOwnScalarArmIsTheSpellingItCopies(string name, double min, double max)
    {
        PackedMath.Rounding rule = PackedMath.Rounding.Between(min, max);
        foreach (double x in EdgeGrid(min, max))
        {
            Assert.True(Same(Reference(x, min, max), rule.Apply(x)), $"{name}: {x:R}");
        }

        Assert.True(rule.Moves);
        Assert.False(PackedMath.Rounding.None.Moves);
        Assert.True(PackedMath.Rounding.ToSingle.Moves);
    }

    /// <summary>
    /// A long randomized sweep across the magnitudes a conversion actually meets — fractions,
    /// halves, values astride the class ends, and the region where doubles stop having fractions.
    /// </summary>
    [Theory]
    [MemberData(nameof(Classes))]
    public void ALongSweepOfOrdinaryValuesRoundsWhereTheScalarLoopRounds(
        string name, double min, double max)
    {
        var random = new Random(20970001);
        var values = new double[100_003];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (i % 7) switch
            {
                0 => (random.NextDouble() - 0.5) * 4,
                1 => Math.Round((random.NextDouble() - 0.5) * 40) + 0.5,   // exact ties
                2 => (random.NextDouble() - 0.5) * (max - min) * 1.5,
                3 => min + (random.NextDouble() - 0.5) * 4,                // astride the low end
                4 => max + (random.NextDouble() - 0.5) * 4,                // astride the high end
                5 => (random.NextDouble() - 0.5) * 1e18,
                _ => Math.ScaleB(random.NextDouble(), random.Next(-40, 70)),
            };
        }

        using var buffer = new ManagedBuffer(values.Length);
        values.AsSpan().CopyTo(buffer.AsSpan());
        PackedMath.Round(buffer, buffer, PackedMath.Rounding.Between(min, max));

        Span<double> got = buffer.AsSpan();
        for (int i = 0; i < values.Length; i++)
        {
            double want = Reference(values[i], min, max);
            Assert.True(Same(want, got[i]), $"{name} at {i}: {values[i]:R} gave {got[i]:R}, wanted {want:R}");
        }
    }

    [Fact]
    public void RoundingToSingleIsTheCastItStandsFor()
    {
        var values = new List<double>
        {
            0.0, -0.0, 0.1, -0.1, 1.0 / 3.0, float.MaxValue, -float.MaxValue,
            (double)float.MaxValue * 1.0000001, 1e300, -1e300, 1e-300, -1e-300,
            float.Epsilon, float.Epsilon / 2, double.Epsilon, -double.Epsilon,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            16777217.0, 16777216.0, -16777217.0, 1.4012984643e-45,
        };

        var random = new Random(20970002);
        for (int i = 0; i < 5000; i++)
        {
            values.Add(Math.ScaleB(random.NextDouble() - 0.5, random.Next(-150, 150)));
        }

        for (int lead = 0; lead < 9; lead++)
        {
            var padded = new double[values.Count + lead];
            Array.Fill(padded, 2.0);
            values.CopyTo(padded, lead);

            using var buffer = new ManagedBuffer(padded.Length);
            padded.AsSpan().CopyTo(buffer.AsSpan());
            PackedMath.Round(buffer, buffer, PackedMath.Rounding.ToSingle);

            Span<double> got = buffer.AsSpan();
            for (int i = 0; i < padded.Length; i++)
            {
                Assert.True(
                    Same((float)padded[i], got[i]),
                    $"lead {lead} element {i}: {padded[i]:R} gave {got[i]:R}, wanted {(float)padded[i]:R}");
            }
        }
    }

    /// <summary>Every length from nothing to past two registers, so body and tail both run.</summary>
    [Fact]
    public void EveryLengthUpPastTwoRegistersAnswersTheSameWay()
    {
        PackedMath.Rounding rule = PackedMath.Rounding.Between(0, byte.MaxValue);
        for (int n = 0; n <= 40; n++)
        {
            var values = new double[n];
            for (int i = 0; i < n; i++)
            {
                values[i] = (i * 37.5) - 200.25;
            }

            using var buffer = new ManagedBuffer(Math.Max(n, 1));
            using var into = new ManagedBuffer(Math.Max(n, 1));
            values.AsSpan().CopyTo(buffer.AsSpan(0, n));
            PackedMath.Round(buffer, into, rule);

            for (int i = 0; i < n; i++)
            {
                Assert.True(Same(Reference(values[i], 0, byte.MaxValue), into.AsSpan()[i]), $"n={n} i={i}");
            }
        }
    }

    /// <summary>
    /// A rounding is per-element, so no answer may depend on how the work was cut up. Long enough
    /// to cross the grain boundary threads are handed out on.
    /// </summary>
    [Fact]
    public void TheAnswerDoesNotMoveWithTheNumberOfThreads()
    {
        int was = ParallelKernels.MaxDegree;
        try
        {
            const int N = 3_000_000;
            var values = new double[N];
            for (int i = 0; i < N; i++)
            {
                values[i] = ((i * 0.618033988749895) % 1.0 - 0.5) * 600;
            }

            using var one = new ManagedBuffer(N);
            using var many = new ManagedBuffer(N);
            values.AsSpan().CopyTo(one.AsSpan());
            values.AsSpan().CopyTo(many.AsSpan());

            PackedMath.Rounding rule = PackedMath.Rounding.Between(sbyte.MinValue, sbyte.MaxValue);
            ParallelKernels.MaxDegree = 1;
            PackedMath.Round(one, one, rule);
            ParallelKernels.MaxDegree = 16;
            PackedMath.Round(many, many, rule);

            for (int i = 0; i < N; i++)
            {
                Assert.Equal(
                    BitConverter.DoubleToInt64Bits(one.AsSpan()[i]),
                    BitConverter.DoubleToInt64Bits(many.AsSpan()[i]));
            }
        }
        finally
        {
            ParallelKernels.MaxDegree = was;
        }
    }

    /// <summary>
    /// The whole point of the fused kernels: arithmetic and class in one sweep must land on the
    /// bits the two sweeps landed on. Every operator, every arity, both kinds of rounding.
    /// </summary>
    [Theory]
    [InlineData(PackedMath.BinaryOp.Add)]
    [InlineData(PackedMath.BinaryOp.Subtract)]
    [InlineData(PackedMath.BinaryOp.Multiply)]
    [InlineData(PackedMath.BinaryOp.Divide)]
    [InlineData(PackedMath.BinaryOp.Remainder)]
    [InlineData(PackedMath.BinaryOp.Power)]
    public void FusingTheClassIntoTheArithmeticChangesNoBit(PackedMath.BinaryOp op)
    {
        const int N = 40_037;                 // not a multiple of the tile, nor of a register
        var random = new Random(20970003 + (int)op);
        var left = new double[N];
        var right = new double[N];
        for (int i = 0; i < N; i++)
        {
            left[i] = (random.NextDouble() - 0.5) * 500;
            right[i] = (random.NextDouble() - 0.5) * 8;
        }

        foreach (PackedMath.Rounding rule in new[]
        {
            PackedMath.Rounding.Between(sbyte.MinValue, sbyte.MaxValue),
            PackedMath.Rounding.Between(0, byte.MaxValue),
            PackedMath.Rounding.Between(int.MinValue, int.MaxValue),
            PackedMath.Rounding.ToSingle,
            PackedMath.Rounding.None,
        })
        {
            AssertFusedMatches(op, left, right, rule);
        }
    }

    private static void AssertFusedMatches(
        PackedMath.BinaryOp op, double[] left, double[] right, PackedMath.Rounding rule)
    {
        int n = left.Length;
        using var a = new ManagedBuffer(n);
        using var b = new ManagedBuffer(n);
        left.AsSpan().CopyTo(a.AsSpan());
        right.AsSpan().CopyTo(b.AsSpan());

        using var apart = new ManagedBuffer(n);
        using var fused = new ManagedBuffer(n);

        PackedMath.Binary(op, a, b, apart);
        PackedMath.Round(apart, apart, rule);
        PackedMath.Binary(op, a, b, fused, rule);
        AssertSameBits(apart, fused, $"{op} array/array");

        PackedMath.BinaryScalarRight(op, a, 3.25, apart);
        PackedMath.Round(apart, apart, rule);
        PackedMath.BinaryScalarRight(op, a, 3.25, fused, rule);
        AssertSameBits(apart, fused, $"{op} array/scalar");

        PackedMath.BinaryScalarLeft(op, 3.25, b, apart);
        PackedMath.Round(apart, apart, rule);
        PackedMath.BinaryScalarLeft(op, 3.25, b, fused, rule);
        AssertSameBits(apart, fused, $"{op} scalar/array");
    }

    private static void AssertSameBits(NumericBuffer want, NumericBuffer got, string what)
    {
        Span<double> w = want.AsSpan();
        Span<double> g = got.AsSpan();
        for (int i = 0; i < w.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(w[i]) != BitConverter.DoubleToInt64Bits(g[i]))
            {
                Assert.Fail($"{what} at {i}: fused {g[i]:R} against {w[i]:R}");
            }
        }
    }

    /// <summary>The conversion as the interpreter has always spelled it, one element at a time.</summary>
    private static double Reference(double x, double min, double max) =>
        double.IsNaN(x) ? 0 : Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), min, max);

    /// <summary>Bit equality, so that a -0 answered where a +0 was wanted is a failure.</summary>
    private static bool Same(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    /// <summary>
    /// The values a rounding can go wrong on. Ties either side of zero; the value one ulp under a
    /// tie, which adding a half would carry and comparing against one does not; 2^52, past which a
    /// double is already whole; both class ends with a step either side; and the two infinities, the
    /// NaN and the two zeros.
    /// </summary>
    private static double[] EdgeGrid(double min, double max)
    {
        var grid = new List<double>
        {
            0.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 3.5, -3.5,
            0.49999999999999994, -0.49999999999999994,
            1.4999999999999998, -1.4999999999999998,
            0.4, -0.4, 0.6, -0.6, 1.0, -1.0,
            double.Epsilon, -double.Epsilon, 2.2250738585072014e-308,
            4503599627370496.0, -4503599627370496.0,     // 2^52
            4503599627370495.5, -4503599627370495.5,
            4503599627370495.0, -4503599627370495.0,
            9007199254740992.0, -9007199254740992.0,     // 2^53
            1e300, -1e300, double.MaxValue, -double.MaxValue,
        };

        foreach (double end in new[] { min, max })
        {
            grid.Add(end);
            grid.Add(end + 1);
            grid.Add(end - 1);
            grid.Add(end + 0.5);
            grid.Add(end - 0.5);
            grid.Add(Math.BitIncrement(end));
            grid.Add(Math.BitDecrement(end));
        }

        return grid.ToArray();
    }
}
