using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

public class PackedMathTests
{
    private static ManagedBuffer From(params double[] values) => ManagedBuffer.Adopt(values);

    private static double[] Values(NumericBuffer buffer) => buffer.AsSpan().ToArray();

    [Theory]
    [InlineData(PackedMath.BinaryOp.Add)]
    [InlineData(PackedMath.BinaryOp.Subtract)]
    [InlineData(PackedMath.BinaryOp.Multiply)]
    [InlineData(PackedMath.BinaryOp.Divide)]
    [InlineData(PackedMath.BinaryOp.Remainder)]
    [InlineData(PackedMath.BinaryOp.Power)]
    public void Binary_MatchesTheScalarFold_Exactly(PackedMath.BinaryOp op)
    {
        var random = new Random(7);
        double[] a = new double[10_000];
        double[] b = new double[10_000];
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = (random.NextDouble() - 0.5) * 100;
            b[i] = (random.NextDouble() - 0.5) * 10;
        }

        using var left = From((double[])a.Clone());
        using var right = From((double[])b.Clone());
        using var dest = new ManagedBuffer(a.Length);
        PackedMath.Binary(op, left, right, dest);

        var result = Values(dest);
        for (int i = 0; i < a.Length; i++)
        {
            double expected = Apply(op, a[i], b[i]);
            // Add/Sub/Mul/Div are single IEEE ops and Rem/Pow run scalar: all bit-exact.
            Assert.Equal(expected, result[i]);
        }
    }

    [Fact]
    public void Binary_PowerWithNegativeBases_MatchesMathPow()
    {
        using var bases = From(-2, -3, -8, 4);
        using var exponents = From(3, 2, 1.0 / 3.0, 0.5);
        using var dest = new ManagedBuffer(4);
        PackedMath.Binary(PackedMath.BinaryOp.Power, bases, exponents, dest);

        var result = Values(dest);
        Assert.Equal(-8, result[0]);          // (-2)^3 — a log/exp kernel would produce NaN
        Assert.Equal(9, result[1]);
        Assert.Equal(Math.Pow(-8, 1.0 / 3.0), result[2]);
        Assert.Equal(2, result[3]);
    }

    [Fact]
    public void BinaryScalar_BothOrders_MatchScalarMath()
    {
        using var values = From(10, 20, 30);
        using var dest = new ManagedBuffer(3);

        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Subtract, values, 5, dest);
        Assert.Equal([5, 15, 25], Values(dest));

        PackedMath.BinaryScalarLeft(PackedMath.BinaryOp.Subtract, 5, values, dest);
        Assert.Equal([-5, -15, -25], Values(dest));

        PackedMath.BinaryScalarLeft(PackedMath.BinaryOp.Divide, 60, values, dest);
        Assert.Equal([6, 3, 2], Values(dest));

        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Power, values, 2, dest);
        Assert.Equal([100, 400, 900], Values(dest));

        PackedMath.BinaryScalarLeft(PackedMath.BinaryOp.Power, 2, From(3, 4, 5), dest);
        Assert.Equal([8, 16, 32], Values(dest));
    }

    [Theory]
    [InlineData(PackedMath.UnaryOp.Negate)]
    [InlineData(PackedMath.UnaryOp.Abs)]
    [InlineData(PackedMath.UnaryOp.Sqrt)]
    [InlineData(PackedMath.UnaryOp.Floor)]
    [InlineData(PackedMath.UnaryOp.Ceiling)]
    [InlineData(PackedMath.UnaryOp.Round)]
    [InlineData(PackedMath.UnaryOp.Sin)]
    [InlineData(PackedMath.UnaryOp.Cos)]
    [InlineData(PackedMath.UnaryOp.Tan)]
    [InlineData(PackedMath.UnaryOp.Exp)]
    [InlineData(PackedMath.UnaryOp.Log)]
    [InlineData(PackedMath.UnaryOp.Log10)]
    public void Unary_MatchesMathFunctions_WithinOneUlp(PackedMath.UnaryOp op)
    {
        var random = new Random(11);
        double[] source = new double[10_000];
        for (int i = 0; i < source.Length; i++)
        {
            // Positive-leaning range keeps Sqrt/Log in-domain; Negate/Abs/Sin still exercise sign
            // via the shifted values below.
            source[i] = random.NextDouble() * 20 + 0.001;
        }

        source[0] = -3.75;
        source[1] = 0.5;
        source[2] = 2.5; // banker's-rounding midpoint

        if (op is PackedMath.UnaryOp.Sqrt or PackedMath.UnaryOp.Log or PackedMath.UnaryOp.Log10)
        {
            source[0] = 3.75; // stay in-domain
        }

        using var input = From((double[])source.Clone());
        using var dest = new ManagedBuffer(source.Length);
        PackedMath.Unary(op, input, dest);

        var result = Values(dest);
        for (int i = 0; i < source.Length; i++)
        {
            double expected = Apply(op, source[i]);
            double tolerance = Math.Abs(expected) * 1e-14;
            Assert.True(Math.Abs(result[i] - expected) <= tolerance,
                $"{op}({source[i]}): got {result[i]}, expected {expected}");
        }
    }

    [Fact]
    public void Map_AppliesTheDelegatePerElement()
    {
        using var input = From(1, 2, 3);
        using var dest = new ManagedBuffer(3);
        PackedMath.Map(input, dest, x => x * x + 1);
        Assert.Equal([2, 5, 10], Values(dest));
    }

    [Fact]
    public void Fill_ProducesTheColonRange()
    {
        using var dest = new ManagedBuffer(3001);
        PackedMath.Fill(dest, 0, 1.0 / 1000);
        var result = Values(dest);
        Assert.Equal(0, result[0]);
        Assert.Equal(1.5, result[1500], 12);
        Assert.Equal(3.0, result[3000], 12);
    }

    [Fact]
    public void FillConstant_AndCopy_MoveWholeBuffers()
    {
        using var a = new ManagedBuffer(100);
        PackedMath.FillConstant(a, 7.5);
        Assert.All(Values(a), v => Assert.Equal(7.5, v));

        using var b = new ManagedBuffer(100);
        PackedMath.Copy(a, b);
        Assert.All(Values(b), v => Assert.Equal(7.5, v));
    }

    [Fact]
    public void Compare_ProducesZeroOneMasks()
    {
        using var a = From(1, 5, 3, double.NaN);
        using var b = From(2, 2, 3, 1);
        using var dest = new ManagedBuffer(4);

        PackedMath.Compare(PackedMath.CompareOp.Less, a, b, dest);
        Assert.Equal([1, 0, 0, 0], Values(dest)); // NaN comparisons are false

        PackedMath.Compare(PackedMath.CompareOp.GreaterEqual, a, b, dest);
        Assert.Equal([0, 1, 1, 0], Values(dest));

        PackedMath.CompareScalar(PackedMath.CompareOp.NotEqual, a, 3, dest);
        Assert.Equal([1, 1, 0, 1], Values(dest));

        PackedMath.CompareScalar(PackedMath.CompareOp.Less, a, 3, dest, scalarOnLeft: true);
        Assert.Equal([0, 1, 0, 0], Values(dest)); // 3 < a[i]
    }

    [Fact]
    public void Reductions_MatchScalarFolds()
    {
        using var a = From(3, -1, 4, 1.5);
        Assert.Equal(7.5, PackedMath.Sum(a));
        Assert.Equal(-1, PackedMath.Min(a));
        Assert.Equal(4, PackedMath.Max(a));

        using var b = From(1, 2, 3, 4);
        Assert.Equal(3 - 2 + 12 + 6, PackedMath.Dot(a, b));

        using var withNaN = From(1, double.NaN, 3);
        Assert.True(double.IsNaN(PackedMath.Min(withNaN)));
        Assert.True(double.IsNaN(PackedMath.Max(withNaN)));
    }

    [Fact]
    public void AllNonZero_ImplementsArrayTruthiness()
    {
        Assert.True(PackedMath.AllNonZero(From(1, -2, 0.5)));
        Assert.False(PackedMath.AllNonZero(From(1, 0, 3)));
        Assert.False(PackedMath.AllNonZero(new ManagedBuffer(0)));
    }

    [Fact]
    public void GatherAndScatter_MoveElementsByPicks()
    {
        using var source = From(10, 20, 30, 40, 50);
        using var gathered = new ManagedBuffer(3);
        PackedMath.Gather(source, [4, 0, 2], gathered);
        Assert.Equal([50, 10, 30], Values(gathered));

        PackedMath.Scatter(source, [1, 3], From(-1, -2));
        Assert.Equal([10, -1, 30, -2, 50], Values(source));

        PackedMath.ScatterConstant(source, [0, 4], 0);
        Assert.Equal([0, -1, 30, -2, 0], Values(source));
    }

    [Fact]
    public void MismatchedLengths_Throw()
    {
        using var three = new ManagedBuffer(3);
        using var four = new ManagedBuffer(4);
        Assert.Throws<ArgumentException>(
            () => PackedMath.Binary(PackedMath.BinaryOp.Add, three, four, three));
        Assert.Throws<ArgumentException>(() => PackedMath.Copy(three, four));
    }

    [Fact]
    public void ChunkedOperations_InvokeTheCallbackBetweenChunks_AndHonorCancellation()
    {
        // Two full chunks plus a partial third.
        int length = PackedMath.ChunkElements * 2 + 100;
        using var a = new ManagedBuffer(length);
        using var dest = new ManagedBuffer(length);

        int calls = 0;
        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Add, a, 1, dest, () => calls++);
        Assert.Equal(3, calls);
        Assert.Equal(1, dest.AsSpan()[length - 1]);

        // A callback that throws (the interpreter's cancellation poll) stops the operation
        // after the first chunk instead of running to completion.
        int seen = 0;
        Assert.Throws<OperationCanceledException>(() =>
            PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Add, a, 1, dest,
                () => { if (++seen == 1) { throw new OperationCanceledException(); } }));
        Assert.Equal(1, seen);
    }

    private static double Apply(PackedMath.BinaryOp op, double x, double y) => op switch
    {
        PackedMath.BinaryOp.Add => x + y,
        PackedMath.BinaryOp.Subtract => x - y,
        PackedMath.BinaryOp.Multiply => x * y,
        PackedMath.BinaryOp.Divide => x / y,
        PackedMath.BinaryOp.Remainder => x % y,
        _ => Math.Pow(x, y),
    };

    private static double Apply(PackedMath.UnaryOp op, double x) => op switch
    {
        PackedMath.UnaryOp.Negate => -x,
        PackedMath.UnaryOp.Abs => Math.Abs(x),
        PackedMath.UnaryOp.Sqrt => Math.Sqrt(x),
        PackedMath.UnaryOp.Floor => Math.Floor(x),
        PackedMath.UnaryOp.Ceiling => Math.Ceiling(x),
        PackedMath.UnaryOp.Round => Math.Round(x),
        PackedMath.UnaryOp.Sin => Math.Sin(x),
        PackedMath.UnaryOp.Cos => Math.Cos(x),
        PackedMath.UnaryOp.Tan => Math.Tan(x),
        PackedMath.UnaryOp.Exp => Math.Exp(x),
        PackedMath.UnaryOp.Log => Math.Log(x),
        _ => Math.Log10(x),
    };
}
