using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The dense linear algebra kernels (M36): LU, QR, SVD, and eigen decompositions. Expected numbers
/// are MATLAB's answers for the same matrices; factor-level checks verify the defining identities
/// (P·A = L·U, A = Q·R, A = U·S·Vᵀ, A·v = λ·v) rather than pinning rounding.
/// </summary>
public class LinearAlgebraTests
{
    private const double Tolerance = 1e-9;

    private static readonly double[,] Magic3 =
    {
        { 8, 1, 6 },
        { 3, 5, 7 },
        { 4, 9, 2 },
    };

    private static void AssertMatrixEqual(double[,] expected, double[,] actual, double tolerance = Tolerance)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        for (int r = 0; r < expected.GetLength(0); r++)
        {
            for (int c = 0; c < expected.GetLength(1); c++)
            {
                Assert.True(Math.Abs(expected[r, c] - actual[r, c]) < tolerance,
                    $"({r},{c}): expected {expected[r, c]}, got {actual[r, c]}");
            }
        }
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        int m = a.GetLength(0);
        int inner = a.GetLength(1);
        int n = b.GetLength(1);
        var product = new double[m, n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++)
                {
                    sum += a[r, k] * b[k, c];
                }

                product[r, c] = sum;
            }
        }

        return product;
    }

    // --- LU -------------------------------------------------------------------------------------

    [Fact]
    public void Lu_DeterminantOfMagic3_IsMinus360()
    {
        Assert.Equal(-360, LuDecomposition.Factor(Magic3).Determinant, 6);
    }

    [Fact]
    public void Lu_FactorsReassembleThePermutedMatrix()
    {
        LuDecomposition lu = LuDecomposition.Factor(Magic3);
        AssertMatrixEqual(Multiply(lu.Permutation, Magic3), Multiply(lu.Lower, lu.Upper));
    }

    [Fact]
    public void Lu_Inverse_MatchesMatlab()
    {
        double[,] a = { { 4, 7 }, { 2, 6 } };
        AssertMatrixEqual(new double[,] { { 0.6, -0.7 }, { -0.2, 0.4 } }, LuDecomposition.Factor(a).Inverse());
    }

    [Fact]
    public void Lu_Solve_ReproducesTheRightHandSide()
    {
        double[] x = LuDecomposition.Factor(Magic3).Solve([15, 15, 15]);
        Assert.All(x, v => Assert.True(Math.Abs(v - 1) < Tolerance)); // magic rows sum to 15
    }

    [Fact]
    public void Lu_SingularMatrix_RefusesToSolve()
    {
        LuDecomposition lu = LuDecomposition.Factor(new double[,] { { 1, 2 }, { 2, 4 } });
        Assert.True(lu.IsSingular);
        Assert.Equal(0, lu.Determinant, 12);
        Assert.Throws<InvalidOperationException>(() => lu.Solve([1, 2]));
    }

    // --- QR -------------------------------------------------------------------------------------

    [Fact]
    public void Qr_FactorsReassembleTheMatrix()
    {
        double[,] a = { { 1, 1 }, { 1, 2 }, { 1, 3 } };
        QrDecomposition qr = QrDecomposition.Factor(a);
        AssertMatrixEqual(a, Multiply(qr.Q, qr.R));
    }

    [Fact]
    public void Qr_LeastSquares_MatchesMatlabBackslash()
    {
        // A\b for A = [1 1; 1 2; 1 3], b = [6; 0; 0] is [8; -3] in MATLAB.
        double[,] a = { { 1, 1 }, { 1, 2 }, { 1, 3 } };
        double[,] x = QrDecomposition.Factor(a).SolveColumns(new double[,] { { 6 }, { 0 }, { 0 } });
        Assert.Equal(8, x[0, 0], 9);
        Assert.Equal(-3, x[1, 0], 9);
    }

    [Fact]
    public void Qr_QHasOrthonormalColumns()
    {
        double[,] a = { { 2, 0 }, { 1, 1 }, { 0, 2 } };
        double[,] q = QrDecomposition.Factor(a).Q;
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                double dot = 0;
                for (int r = 0; r < 3; r++)
                {
                    dot += q[r, i] * q[r, j];
                }

                Assert.Equal(i == j ? 1 : 0, dot, 9);
            }
        }
    }

    // --- SVD ------------------------------------------------------------------------------------

    [Fact]
    public void Svd_SingularValues_MatchMatlab()
    {
        Svd svd = Svd.Factor(new double[,] { { 1, 2 }, { 3, 4 } });
        Assert.Equal(5.4649857042190426, svd.Values[0], 9);
        Assert.Equal(0.36596619062625746, svd.Values[1], 9);
    }

    [Fact]
    public void Svd_FactorsReassembleTheMatrix()
    {
        double[,] a = { { 1, 2, 3 }, { 4, 5, 6 } };
        Svd svd = Svd.Factor(a);

        int m = a.GetLength(0), n = a.GetLength(1), k = svd.Values.Length;
        var reassembled = new double[m, n];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int i = 0; i < k; i++)
                {
                    sum += svd.U[r, i] * svd.Values[i] * svd.V[c, i];
                }

                reassembled[r, c] = sum;
            }
        }

        AssertMatrixEqual(a, reassembled);
    }

    [Fact]
    public void Svd_Rank_SeesThroughDependentRows()
    {
        Assert.Equal(1, Svd.Factor(new double[,] { { 1, 2 }, { 2, 4 } }).Rank(2, 2));

        double[,] magic4 =
        {
            { 16, 2, 3, 13 },
            { 5, 11, 10, 8 },
            { 9, 7, 6, 12 },
            { 4, 14, 15, 1 },
        };
        Assert.Equal(3, Svd.Factor(magic4).Rank(4, 4)); // MATLAB: rank(magic(4)) == 3
    }

    /// <summary>
    /// Equal-norm parallel columns are the one-sided Jacobi sweep's degenerate case: the rotation that
    /// separates them is exactly 45°, and asking <c>Math.Sign</c> for the direction of a zero answers
    /// zero, which is a rotation by nothing. A matrix of ones came out with three equal singular values
    /// and full rank until that was fixed.
    /// </summary>
    [Fact]
    public void Svd_EqualNormParallelColumns_AreStillRankOne()
    {
        double[,] ones = { { 1, 1, 1 }, { 1, 1, 1 }, { 1, 1, 1 } };
        Svd svd = Svd.Factor(ones);

        Assert.Equal(1, svd.Rank(3, 3));
        Assert.Equal(3, svd.Values[0], 12);
        Assert.Equal(0, svd.Values[1], 12);
        Assert.Equal(0, svd.Values[2], 12);

        // The same shape at another scale, and with two identical columns beside a third.
        Assert.Equal(1, Svd.Factor(new double[,] { { 5, 5 }, { 5, 5 } }).Rank(2, 2));
        Assert.Equal(2, Svd.Factor(new double[,] { { 1, 1, 0 }, { 1, 1, 0 }, { 0, 0, 2 } }).Rank(3, 3));
    }

    // --- Eigen ----------------------------------------------------------------------------------

    [Fact]
    public void Eigen_Symmetric_AscendingRealValues()
    {
        Eigen eigen = Eigen.Factor(new double[,] { { 2, 1 }, { 1, 2 } });
        Assert.True(eigen.IsReal);
        Assert.Equal(1, eigen.Values[0].Real, 9);
        Assert.Equal(3, eigen.Values[1].Real, 9);
    }

    [Fact]
    public void Eigen_General_MatchesTheQuadraticRoots()
    {
        // eig([1 2; 3 4]) = (5 ± sqrt(33)) / 2.
        Eigen eigen = Eigen.Factor(new double[,] { { 1, 2 }, { 3, 4 } });
        double[] sorted = eigen.Values.Select(static v => v.Real).OrderBy(static v => v).ToArray();
        Assert.Equal((5 - Math.Sqrt(33)) / 2, sorted[0], 8);
        Assert.Equal((5 + Math.Sqrt(33)) / 2, sorted[1], 8);
    }

    [Fact]
    public void Eigen_RotationMatrix_HasImaginaryPair()
    {
        Eigen eigen = Eigen.Factor(new double[,] { { 0, -1 }, { 1, 0 } });
        Complex[] sorted = eigen.Values.OrderBy(static v => v.Imaginary).ToArray();
        Assert.Equal(-1, sorted[0].Imaginary, 8);
        Assert.Equal(1, sorted[1].Imaginary, 8);
        Assert.Equal(0, sorted[0].Real, 8);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Eigen_VectorsSatisfyTheDefinition(bool symmetric)
    {
        double[,] a = symmetric
            ? new double[,] { { 4, 1, 0 }, { 1, 3, 1 }, { 0, 1, 2 } }
            : new double[,] { { 2, -1, 3 }, { 4, 0, 1 }, { -2, 5, 6 } };

        Eigen eigen = Eigen.Factor(a);
        int n = 3;
        for (int k = 0; k < n; k++)
        {
            for (int r = 0; r < n; r++)
            {
                Complex av = Complex.Zero;
                for (int c = 0; c < n; c++)
                {
                    av += a[r, c] * eigen.Vectors[c, k];
                }

                Complex lv = eigen.Values[k] * eigen.Vectors[r, k];
                Assert.True((av - lv).Magnitude < 1e-6,
                    $"eigenpair {k}, row {r}: A*v = {av}, λ*v = {lv}");
            }
        }
    }

    [Fact]
    public void Eigen_Magic3_HasFifteenAsAnEigenvalue()
    {
        // The magic-sum eigenvalue: magic(3)'s all-ones eigenvector sums each row to 15.
        Eigen eigen = Eigen.Factor(Magic3);
        Assert.Contains(eigen.Values, v => Math.Abs(v.Real - 15) < 1e-8 && Math.Abs(v.Imaginary) < 1e-8);
    }
}
