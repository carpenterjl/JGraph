using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The real QZ iteration (M76). Every check is a defining identity rather than a pinned matrix: a
/// factorization is right when <c>Q·A·Z</c> and <c>Q·B·Z</c> reproduce the pencil in the two shapes
/// promised and the factors are orthogonal, and there are many correct answers that differ in sign
/// and in the order of the diagonal.
/// </summary>
public class GeneralizedSchurTests
{
    private const double Tolerance = 1e-8;

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        var product = new double[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    sum += a[i, k] * b[k, j];
                }

                product[i, j] = sum;
            }
        }

        return product;
    }

    private static double MaxDifference(double[,] a, double[,] b)
    {
        double worst = 0;
        for (int i = 0; i < a.GetLength(0); i++)
        {
            for (int j = 0; j < a.GetLength(1); j++)
            {
                worst = Math.Max(worst, Math.Abs(a[i, j] - b[i, j]));
            }
        }

        return worst;
    }

    private static void AssertOrthogonal(double[,] m, string name)
    {
        int n = m.GetLength(0);
        double[,] product = Multiply(Transpose(m), m);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.True(Math.Abs(product[i, j] - (i == j ? 1 : 0)) < Tolerance,
                    $"{name} is not orthogonal at ({i},{j}): {product[i, j]}");
            }
        }
    }

    private static double[,] Transpose(double[,] m)
    {
        var t = new double[m.GetLength(1), m.GetLength(0)];
        for (int i = 0; i < m.GetLength(0); i++)
        {
            for (int j = 0; j < m.GetLength(1); j++)
            {
                t[j, i] = m[i, j];
            }
        }

        return t;
    }

    /// <summary>The whole contract, asserted for one pencil.</summary>
    private static GeneralizedSchur AssertFactors(double[,] a, double[,] b)
    {
        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        int n = a.GetLength(0);

        AssertOrthogonal(qz.Q, "Q");
        AssertOrthogonal(qz.Z, "Z");

        Assert.True(MaxDifference(Multiply(qz.Q, Multiply(a, qz.Z)), qz.AA) < Tolerance,
            "Q·A·Z did not reproduce AA");
        Assert.True(MaxDifference(Multiply(qz.Q, Multiply(b, qz.Z)), qz.BB) < Tolerance,
            "Q·B·Z did not reproduce BB");

        for (int i = 1; i < n; i++)
        {
            for (int j = 0; j < i; j++)
            {
                Assert.True(Math.Abs(qz.BB[i, j]) < Tolerance, $"BB is not triangular at ({i},{j})");
            }
        }

        // Quasi-triangular: never two subdiagonal entries in a row, which is what says the 2-by-2
        // blocks really are blocks and not an unconverged sweep.
        for (int i = 2; i < n; i++)
        {
            Assert.False(qz.AA[i, i - 1] != 0 && qz.AA[i - 1, i - 2] != 0,
                $"AA has consecutive subdiagonal entries at {i}");
            for (int j = 0; j < i - 1; j++)
            {
                Assert.True(Math.Abs(qz.AA[i, j]) < Tolerance, $"AA is not Hessenberg at ({i},{j})");
            }
        }

        return qz;
    }

    private static double[,] Random(int n, Random random)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                m[i, j] = Math.Round((random.NextDouble() * 4) - 2, 3);
            }
        }

        return m;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void RandomPencils_FactorIntoTheTwoPromisedShapes(int n)
    {
        var random = new Random(20 + n);
        for (int trial = 0; trial < 25; trial++)
        {
            AssertFactors(Random(n, random), Random(n, random));
        }
    }

    /// <summary>
    /// The deflation of an eigenvalue at infinity, exercised in bulk: a B built with a dependent
    /// column is singular, so every one of these pencils has a zero on B's diagonal to chase.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RandomSingularPencils_AreFactoredToo(int n)
    {
        var random = new Random(100 + n);
        for (int trial = 0; trial < 25; trial++)
        {
            double[,] b = Random(n, random);
            for (int i = 0; i < n; i++)
            {
                b[i, n - 1] = b[i, 0];      // the last column repeats the first: B is singular
            }

            GeneralizedSchur qz = AssertFactors(Random(n, random), b);
            Assert.False(qz.IsFinite, "a singular B must give an eigenvalue at infinity");
        }
    }

    [Fact]
    public void TheEigenvalues_AreThoseOfBInverseA_WhenBIsInvertible()
    {
        double[,] a = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 10 } };
        double[,] b = { { 2, 0, 1 }, { 0, 3, 0 }, { 1, 0, 4 } };

        GeneralizedSchur qz = AssertFactors(a, b);
        Assert.True(qz.IsFinite);

        double[] mine = Array.ConvertAll(qz.Eigenvalues, static v => v.Real);
        double[] theirs = Array.ConvertAll(Eigen.Factor(Linear.Solve(b, a)).Values, static v => v.Real);
        Array.Sort(mine);
        Array.Sort(theirs);

        for (int i = 0; i < mine.Length; i++)
        {
            Assert.True(Math.Abs(mine[i] - theirs[i]) < 1e-7, $"{mine[i]} vs {theirs[i]}");
        }
    }

    /// <summary>The case the earlier construction refused, and the reason this class exists.</summary>
    [Fact]
    public void ASingularB_GivesAnInfiniteEigenvalue_RatherThanARefusal()
    {
        double[,] a = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 10 } };
        double[,] b = { { 1, 0, 0 }, { 0, 0, 0 }, { 0, 0, 1 } };

        GeneralizedSchur qz = AssertFactors(a, b);

        Assert.False(qz.IsFinite);
        Assert.Contains(qz.Beta, static beta => beta == 0);
        Assert.Contains(qz.Eigenvalues, static value => double.IsInfinity(value.Real));
    }

    [Fact]
    public void AWhollySingularB_IsStillFactored()
    {
        double[,] a = { { 1, 2 }, { 3, 4 } };
        var b = new double[2, 2];

        GeneralizedSchur qz = AssertFactors(a, b);
        Assert.All(qz.Beta, static beta => Assert.Equal(0, beta));
    }

    [Fact]
    public void AConjugatePair_StaysAsATwoByTwoBlock()
    {
        // A rotation has no real eigenvalues, so a real factorization cannot triangularize it.
        double[,] a = { { 0, -1 }, { 1, 0 } };
        double[,] b = { { 1, 0 }, { 0, 1 } };

        GeneralizedSchur qz = AssertFactors(a, b);

        Assert.NotEqual(0, qz.AA[1, 0]);
        Assert.All(qz.Eigenvalues, static value => Assert.True(Math.Abs(value.Imaginary) > 0.5));
    }

    [Fact]
    public void RealEigenvalues_AreSplitOntoTheDiagonal()
    {
        double[,] a = { { 1, 2 }, { 3, 4 } };
        double[,] b = { { 1, 0 }, { 0, 1 } };

        GeneralizedSchur qz = AssertFactors(a, b);

        Assert.Equal(0, qz.AA[1, 0]);
        double[] ratios = { qz.AA[0, 0] / qz.BB[0, 0], qz.AA[1, 1] / qz.BB[1, 1] };
        Array.Sort(ratios);
        Assert.True(Math.Abs(ratios[0] - -0.3722813232690143) < 1e-7, $"{ratios[0]}");
        Assert.True(Math.Abs(ratios[1] - 5.372281323269014) < 1e-7, $"{ratios[1]}");
    }

    [Fact]
    public void ADiagonalPencil_IsAlreadyItsOwnFactorization()
    {
        double[,] a = { { 3, 0 }, { 0, 5 } };
        double[,] b = { { 1, 0 }, { 0, 1 } };

        GeneralizedSchur qz = AssertFactors(a, b);
        Assert.Equal(3, qz.AA[0, 0] / qz.BB[0, 0], 9);
        Assert.Equal(5, qz.AA[1, 1] / qz.BB[1, 1], 9);
    }

    [Fact]
    public void Reordering_BringsTheChosenEigenvalueToTheFront()
    {
        double[,] a = { { 1, 2 }, { 3, 4 } };
        double[,] b = { { 1, 0 }, { 0, 1 } };

        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        double second = qz.AA[1, 1] / qz.BB[1, 1];

        GeneralizedSchur moved = qz.Reordered([false, true]);

        Assert.True(Math.Abs((moved.AA[0, 0] / moved.BB[0, 0]) - second) < 1e-7);
        AssertOrthogonal(moved.Q, "reordered Q");
        AssertOrthogonal(moved.Z, "reordered Z");
        Assert.True(MaxDifference(Multiply(moved.Q, Multiply(a, moved.Z)), moved.AA) < Tolerance);
        Assert.True(MaxDifference(Multiply(moved.Q, Multiply(b, moved.Z)), moved.BB) < Tolerance);
    }

    [Fact]
    public void Reordering_MovesSeveralAndKeepsTheirOrder()
    {
        var random = new Random(7);
        double[,] a = Random(4, random);
        double[,] b = Multiply(Transpose(Random(4, random)), Random(4, random));

        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        if (Array.Exists(qz.Eigenvalues, static v => Math.Abs(v.Imaginary) > 1e-12))
        {
            return; // a conjugate pair is refused by contract; this fixture is only for real ones
        }

        Complex[] before = qz.Eigenvalues;
        GeneralizedSchur moved = qz.Reordered([false, true, false, true]);

        Assert.True(Math.Abs(moved.Eigenvalues[0].Real - before[1].Real) < 1e-6);
        Assert.True(Math.Abs(moved.Eigenvalues[1].Real - before[3].Real) < 1e-6);
        Assert.True(MaxDifference(Multiply(moved.Q, Multiply(a, moved.Z)), moved.AA) < Tolerance);
    }

    [Fact]
    public void AnEmptyPencil_IsAnEmptyFactorization()
    {
        GeneralizedSchur qz = GeneralizedSchur.Factor(new double[0, 0], new double[0, 0]);
        Assert.Empty(qz.Alpha);
        Assert.True(qz.IsFinite);
    }

    [Fact]
    public void MismatchedSizes_AreRefused() =>
        Assert.Throws<ArgumentException>(() =>
            GeneralizedSchur.Factor(new double[2, 2], new double[3, 3]));
}
