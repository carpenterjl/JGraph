using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// Item 10c (ADR 0157): a stiff run keeps one iteration matrix and one set of LU factors and
/// writes both in place at every refactor. The contract is bits — the in-place forms answer what
/// the allocating forms answered, element for element — and that a buffer reused for a second
/// matrix answers that matrix, not a mixture.
/// </summary>
public class StiffScratchM157Tests
{
    public static TheoryData<int> Orders => new() { 1, 2, 3, 7, 30, 64 };

    private static double[,] Matrix(int n, double seed)
    {
        var a = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                a[r, c] = Math.Sin((seed * ((r * n) + c + 1)) + (0.3 * seed)) + (r == c ? n : 0);
            }
        }

        return a;
    }

    private static long[] Bits(ReadOnlySpan<double> values)
    {
        var bits = new long[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            bits[i] = BitConverter.DoubleToInt64Bits(values[i]);
        }

        return bits;
    }

    private static void Poison(double[,] values)
    {
        for (int r = 0; r < values.GetLength(0); r++)
        {
            for (int c = 0; c < values.GetLength(1); c++)
            {
                values[r, c] = double.NaN;
            }
        }
    }

    private static long[] Bits(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var bits = new long[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bits[(r * cols) + c] = BitConverter.DoubleToInt64Bits(values[r, c]);
            }
        }

        return bits;
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void FactorReusingAnswersFactorsBitsAndOwnsTheBuffer(int n)
    {
        double[,] first = Matrix(n, 0.7);
        double[,] second = Matrix(n, 1.9);
        var buffer = new double[n * n];

        LuDecomposition fresh = LuDecomposition.Factor(first);
        LuDecomposition reused = LuDecomposition.FactorReusing(first, buffer);
        Assert.Equal(Bits(fresh.Factors), Bits(reused.Factors));
        Assert.Equal(fresh.RowPermutation.ToArray(), reused.RowPermutation.ToArray());
        Assert.Equal(Bits(fresh.Factors), Bits(buffer));   // the factors were written into the caller's buffer

        // The same buffer factors a second matrix: the answer is that matrix's, to the bit, and a
        // solve against it is the fresh decomposition's solve.
        LuDecomposition again = LuDecomposition.FactorReusing(second, buffer);
        LuDecomposition freshAgain = LuDecomposition.Factor(second);
        Assert.Equal(Bits(freshAgain.Factors), Bits(again.Factors));
        var rhs = new double[n];
        for (int i = 0; i < n; i++)
        {
            rhs[i] = Math.Cos(i + 0.5);
        }

        Assert.Equal(Bits(freshAgain.Solve(rhs)), Bits(again.Solve(rhs)));
    }

    [Fact]
    public void FactorReusingRefusesAShortBufferAndANonSquareMatrix()
    {
        Assert.Throws<ArgumentException>(() => LuDecomposition.FactorReusing(Matrix(4, 0.7), new double[15]));
        Assert.Throws<ArgumentException>(() => LuDecomposition.FactorReusing(new double[3, 4], new double[12]));
        Assert.Throws<ArgumentNullException>(() => LuDecomposition.FactorReusing(Matrix(2, 0.7), null!));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void IterationMatrixIntoAnswersIterationMatrixBits(int n)
    {
        double[,] mass = OdeStiffSupport.Identity(n);
        double[,] jacobian = Matrix(n, 2.3);

        // Zeros in both operands and a negative scale: 0 − (−s·0) and 0 − (s·0) settle a signed
        // zero, and the in-place form must settle it the same way.
        jacobian[0, n - 1] = 0;
        jacobian[n - 1, 0] = -0.0;
        var into = new double[n, n];
        foreach (double scale in new[] { 0.0625, -3.5, 0.0, -0.0, 1e-300, 7.25e3 })
        {
            Poison(into);
            double[,] fresh = OdeStiffSupport.IterationMatrix(mass, scale, jacobian);
            OdeStiffSupport.IterationMatrixInto(into, mass, scale, jacobian);
            Assert.Equal(Bits(fresh), Bits(into));
        }
    }

    [Fact]
    public void IterationMatrixIntoRefusesABufferOfAnotherShape()
    {
        double[,] mass = OdeStiffSupport.Identity(3);
        Assert.Throws<ArgumentException>(() => OdeStiffSupport.IterationMatrixInto(new double[3, 4], mass, 1, mass));
        Assert.Throws<ArgumentException>(() => OdeStiffSupport.IterationMatrixInto(new double[2, 2], mass, 1, mass));
    }

    [Fact]
    public void ADenseNumericalJacobianAnswersWhatItAnsweredBeforeItsDeadStoreWent()
    {
        // Robertson's kinetics, the problem OdeNumericalJacobianTests pins the increments on; the
        // dense branch's Jacobian is a difference quotient per element and nothing else.
        static double[] Robertson(double[] y) =>
        [
            (-0.04 * y[0]) + (1e4 * y[1] * y[2]),
            (0.04 * y[0]) - (1e4 * y[1] * y[2]) - (3e7 * (y[1] * y[1])),
            3e7 * (y[1] * y[1]),
        ];

        var options = new OdeJacobianOptions { Threshold = [1e-8, 1e-14, 1e-6] };
        double[] y = [1, 0, 0];
        double[] f0 = Robertson(y);
        double[,] jacobian = OdeNumericalJacobian.Compute(Robertson, null, y, f0, options, out int evaluations);

        // Three columns, and one taken again: at y = [1 0 0] the second column's difference sits
        // at the level of rounding, so Salane's rule reads it once more with a larger increment
        // (OdeNumericalJacobianTests pins the increments). That fourth evaluation is the point:
        // the retry reads the Jacobian's column, never the difference matrix the dense branch lost.
        Assert.Equal(4, evaluations);
        Assert.Equal(-0.04, jacobian[0, 0], 1e-8);
        Assert.Equal(0.04, jacobian[1, 0], 1e-8);
        Assert.Equal(0, jacobian[2, 0]);
        Assert.Equal(0, jacobian[0, 2]);
    }
}
