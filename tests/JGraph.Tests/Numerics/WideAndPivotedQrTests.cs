using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// What M76 asked of the QR: a matrix of any shape rather than tall ones only, and the column
/// pivoting that <c>[Q, R, P] = qr(A)</c> needs. Also the order at which Cholesky gave up, which is
/// the whole content of <c>[R, flag] = chol(A)</c>.
/// </summary>
public class WideAndPivotedQrTests
{
    private const double Tolerance = 1e-10;

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

    private static void AssertClose(double[,] expected, double[,] actual, string what)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        for (int i = 0; i < expected.GetLength(0); i++)
        {
            for (int j = 0; j < expected.GetLength(1); j++)
            {
                Assert.True(Math.Abs(expected[i, j] - actual[i, j]) < Tolerance,
                    $"{what} at ({i},{j}): {expected[i, j]} vs {actual[i, j]}");
            }
        }
    }

    /// <summary>The matrix whose refusal used to end the process.</summary>
    [Fact]
    public void AWideMatrix_Factors()
    {
        double[,] a = { { 1, 2, 3 }, { 4, 5, 6 } };

        QrDecomposition qr = QrDecomposition.Factor(a);

        Assert.Equal(2, qr.Q.GetLength(0));
        Assert.Equal(2, qr.Q.GetLength(1));   // a wide matrix economizes nothing away
        Assert.Equal(2, qr.R.GetLength(0));
        Assert.Equal(3, qr.R.GetLength(1));
        AssertClose(a, Multiply(qr.Q, qr.R), "Q·R");
        AssertClose(a, Multiply(qr.FullQ, qr.FullR), "FullQ·FullR");
        Assert.True(Math.Abs(qr.R[1, 0]) < Tolerance, "R is not triangular");
        Assert.False(qr.IsFullRank, "three columns in two dimensions cannot be independent");
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    [InlineData(1, 4)]
    public void EveryShape_ReproducesItsMatrix(int m, int n)
    {
        var random = new Random(m * 31 + n);
        var a = new double[m, n];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = Math.Round((random.NextDouble() * 4) - 2, 3);
            }
        }

        QrDecomposition qr = QrDecomposition.Factor(a);
        AssertClose(a, Multiply(qr.Q, qr.R), "Q·R");
        AssertClose(a, Multiply(qr.FullQ, qr.FullR), "FullQ·FullR");

        double[,] identity = Multiply(Transpose(qr.FullQ), qr.FullQ);
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                Assert.True(Math.Abs(identity[i, j] - (i == j ? 1 : 0)) < Tolerance, "Q is not orthogonal");
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

    [Fact]
    public void Pivoting_OrdersTheDiagonalAndStillReproducesTheMatrix()
    {
        // The second column is much the largest, so pivoting must bring it to the front.
        double[,] a = { { 1, 90, 2 }, { 0, 40, 3 }, { 1, 10, 4 } };

        QrDecomposition qr = QrDecomposition.Factor(a, pivot: true);

        Assert.Equal(1, qr.PivotVector[0]);
        AssertClose(Multiply(a, qr.Permutation), Multiply(qr.Q, qr.R), "A·P = Q·R");

        for (int i = 1; i < 3; i++)
        {
            Assert.True(Math.Abs(qr.R[i, i]) <= Math.Abs(qr.R[i - 1, i - 1]) + Tolerance,
                "pivoting must leave R's diagonal non-increasing in magnitude");
        }
    }

    [Fact]
    public void PivotingARankDeficientMatrix_PutsTheDependenceLast()
    {
        // The third column is the first one doubled, so the last diagonal of R must vanish.
        double[,] a = { { 1, 5, 2 }, { 2, 1, 4 }, { 3, 9, 6 } };

        QrDecomposition qr = QrDecomposition.Factor(a, pivot: true);

        AssertClose(Multiply(a, qr.Permutation), Multiply(qr.Q, qr.R), "A·P = Q·R");
        Assert.True(Math.Abs(qr.R[2, 2]) < 1e-12, $"expected a vanishing last pivot, got {qr.R[2, 2]}");
    }

    [Fact]
    public void NotPivoting_LeavesTheColumnsWhereTheyWere()
    {
        double[,] a = { { 1, 90 }, { 0, 40 } };
        QrDecomposition qr = QrDecomposition.Factor(a);
        Assert.Equal([0, 1], qr.PivotVector);
    }

    [Fact]
    public void Cholesky_ReportsTheOrderAtWhichItFailed()
    {
        // Positive definite in its leading 2-by-2 and not beyond, so MATLAB's flag is 3.
        double[,] a = { { 4, 2, 1 }, { 2, 3, 1 }, { 1, 1, -5 } };

        Cholesky chol = Cholesky.Factor(a);

        Assert.False(chol.IsPositiveDefinite);
        Assert.Equal(3, chol.FailedAt);
    }

    [Fact]
    public void Cholesky_OfADefiniteMatrix_ReportsNoFailure()
    {
        double[,] a = { { 4, 2 }, { 2, 3 } };

        Cholesky chol = Cholesky.Factor(a);

        Assert.True(chol.IsPositiveDefinite);
        Assert.Equal(0, chol.FailedAt);
        AssertClose(a, Multiply(chol.Lower, Transpose(chol.Lower)), "L·Lᵀ");
    }

    [Fact]
    public void Cholesky_FailingAtOnce_SaysSo()
    {
        double[,] a = { { -1, 0 }, { 0, 1 } };
        Assert.Equal(1, Cholesky.Factor(a).FailedAt);
    }
}
