using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// Open items 272 and 307 (ADR 0355): a power of 0.5 is the square root and a power of -1 the
/// reciprocal on every road, and a square system that is singular or nearly so answers under
/// R2025b's classification rather than refusing. Every expectation here was measured in R2025b.
/// </summary>
public class SquareSystemsAndRootPowersTests
{
    private static readonly double[] Corners =
        [0, -0.0, double.PositiveInfinity, double.NaN, 1, 2, 4, 0.1, 2.2250738585072014e-308,
         5.5626846462680035e-309, double.Epsilon, double.MaxValue, 1e300, 123.456, 7e7];

    [Fact]
    public void APowerOfOneHalfIsTheRootOfPlusZeroPlusTheBase()
    {
        foreach (double x in Corners)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Sqrt(x + 0.0)), BitConverter.DoubleToInt64Bits(PackedMath.Power(x, 0.5)));
        }

        // (-0).^0.5 is +0 in R2025b, where the root alone keeps the sign.
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(PackedMath.Power(-0.0, 0.5)));

        // realmax.^0.5 is where the C runtime's pow and the root part company by an ulp.
        Assert.Equal(0x5FEFFFFFFFFFFFFFL, BitConverter.DoubleToInt64Bits(PackedMath.Power(double.MaxValue, 0.5)));
    }

    [Fact]
    public void APowerOfMinusOneIsTheReciprocal()
    {
        foreach (double x in Corners)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(1.0 / x), BitConverter.DoubleToInt64Bits(PackedMath.Power(x, -1)));
        }

        Assert.Equal(double.NegativeInfinity, PackedMath.Power(-0.0, -1));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void ThePackedScalarKernelAgreesWithTheScalarPowerToTheBit(double exponent)
    {
        var random = new Random(272);
        var values = new double[5000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = random.NextDouble() * Math.Pow(10, random.Next(-300, 300));
        }

        Corners.CopyTo(values, 0);
        using var source = ManagedBuffer.Adopt((double[])values.Clone());
        using var dest = new ManagedBuffer(values.Length);
        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Power, source, exponent, dest);
        double[] packed = dest.AsSpan().ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(PackedMath.Power(values[i], exponent)), BitConverter.DoubleToInt64Bits(packed[i]));
        }
    }

    [Fact]
    public void TheKernelRootRunsInPlace()
    {
        double[] values = [-0.0, 4, 9, 2];
        using var buffer = ManagedBuffer.Adopt(values);
        PackedMath.BinaryScalarRight(PackedMath.BinaryOp.Power, buffer, 0.5, buffer);
        Assert.Equal([0, 2, 3, Math.Sqrt(2)], buffer.AsSpan().ToArray());
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(buffer.AsSpan()[0]));
    }

    private static double[] ColumnMajor(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var flat = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = a[r, c];
            }
        }

        return flat;
    }

    [Fact]
    public void ASingularSystemAnswersWhatItsSubstitutionsGive()
    {
        // [1 2; 2 4] \ [1; 2] is NaN; [1 1; 1 1] \ [1; 2] is [-Inf; Inf] (R2025b).
        double[] b = [1, 2];
        Assert.Equal(SquareCondition.Singular, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 2 }, { 2, 4 } }), 2, b, 1, out double rcond));
        Assert.Equal(0, rcond);
        Assert.True(double.IsNaN(b[0]) && double.IsNaN(b[1]));

        b = [1, 2];
        Assert.Equal(SquareCondition.Singular, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 1 }, { 1, 1 } }), 2, b, 1, out _));
        Assert.Equal([double.NegativeInfinity, double.PositiveInfinity], b);

        // A singular symmetric one whose zero pivot leaves a column alone: [NaN; NaN; 1].
        b = [1, 1, 1];
        SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 } }), 3, b, 1, out _);
        Assert.True(double.IsNaN(b[0]) && double.IsNaN(b[1]));
        Assert.Equal(1, b[2]);
    }

    [Fact]
    public void ATriangleIsSubstitutedLowerFirstAndJudgedByItsDiagonal()
    {
        // diag([1 0 2]) \ ones(3, 1) is [1; Inf; NaN]: forward substitution, no skipped zeros.
        double[] b = [1, 1, 1];
        Assert.Equal(SquareCondition.Singular, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 0, 0 }, { 0, 0, 0 }, { 0, 0, 2 } }), 3, b, 1, out _));
        Assert.Equal(1, b[0]);
        Assert.Equal(double.PositiveInfinity, b[1]);
        Assert.True(double.IsNaN(b[2]));

        // [1 1; 0 1e-17] warns with RCOND 1e-17, the diagonal's ratio, not the 5e-18 of the 1-norm.
        b = [1, 1];
        Assert.Equal(SquareCondition.NearlySingular, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 1 }, { 0, 1e-17 } }), 2, b, 1, out double rcond));
        Assert.Equal(1e-17, rcond);
        Assert.Equal([-1e17, 1e17], b);

        // [1 1e20; 0 1] is badly conditioned and says nothing: its diagonal is even.
        b = [1, 1];
        Assert.Equal(SquareCondition.Fine, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 1e20 }, { 0, 1 } }), 2, b, 1, out _));
    }

    [Fact]
    public void ANaNPassesThroughOnEveryBackendAndIsIllConditioned()
    {
        double[] b = [1, 1];
        Assert.Equal(SquareCondition.IllConditioned, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, double.NaN }, { 2, 3 } }), 2, b, 1, out double rcond));
        Assert.True(double.IsNaN(rcond));
        Assert.True(double.IsNaN(b[0]) && double.IsNaN(b[1]));

        Assert.Equal(SquareCondition.IllConditioned, SquareSystems.Invert(ColumnMajor(new double[,] { { 1, double.NaN }, { 2, 3 } }), 2, out double[] inverse, out _));
        Assert.All(inverse, v => Assert.True(double.IsNaN(v)));

        // A NaN only on the right still solves the rest: [2 1; 1 3] \ [NaN; 1] is NaN throughout.
        b = [double.NaN, 1];
        Assert.Equal(SquareCondition.Fine, SquareSystems.Solve(ColumnMajor(new double[,] { { 2, 1 }, { 1, 3 } }), 2, b, 1, out _));
        Assert.True(double.IsNaN(b[0]) && double.IsNaN(b[1]));
    }

    [Fact]
    public void AnInverseIsInfWhereR2025bsIs()
    {
        Assert.Equal(SquareCondition.Singular, SquareSystems.Invert(ColumnMajor(new double[,] { { 1, 2 }, { 2, 4 } }), 2, out double[] inverse, out _));
        Assert.All(inverse, v => Assert.Equal(double.PositiveInfinity, v));

        // Diagonal: element by element. Upper triangle with a zero pivot: Inf over the triangle.
        SquareSystems.Invert(ColumnMajor(new double[,] { { 1, 0, 0 }, { 0, 0, 0 }, { 0, 0, 2 } }), 3, out inverse, out _);
        Assert.Equal([1, 0, 0, 0, double.PositiveInfinity, 0, 0, 0, 0.5], inverse);
        SquareSystems.Invert(ColumnMajor(new double[,] { { 1, 2 }, { 0, 0 } }), 2, out inverse, out _);
        Assert.Equal([double.PositiveInfinity, 0, double.PositiveInfinity, double.PositiveInfinity], inverse);

        // [1 Inf; 2 3]: no zero pivot, an infinite norm, so RCOND is 0 and it is singular.
        Assert.Equal(SquareCondition.Singular, SquareSystems.Invert(ColumnMajor(new double[,] { { 1, double.PositiveInfinity }, { 2, 3 } }), 2, out inverse, out _));
        Assert.Equal([0, 0, 0.5, 0], inverse);

        // A nonsingular lower triangle inverts as a triangle, never through a pivoted LU.
        Assert.Equal(SquareCondition.NearlySingular, SquareSystems.Invert(ColumnMajor(new double[,] { { 1, 0, 0 }, { 2, 1e-16, 0 }, { 3, 4, 7 } }), 3, out inverse, out _));
        Assert.All(inverse, v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void ASingleSystemIsNearlySingularBelowSinglesEps()
    {
        double[] b = [1, 1];
        Assert.Equal(SquareCondition.NearlySingular, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 0 }, { 0, 1e-8 } }), 2, b, 1, out _, single: true));
        b = [1, 1];
        Assert.Equal(SquareCondition.Fine, SquareSystems.Solve(ColumnMajor(new double[,] { { 1, 0 }, { 0, 1e-8 } }), 2, b, 1, out _));
    }
}
