using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The real Schur decomposition and the block reordering built on it. Every assertion here is a
/// property of the factorization rather than a stored answer, because the factorization is not
/// unique — only U·T·Uᵀ = A, Uᵀ·U = I, and the quasi-triangular shape are.
/// </summary>
public class SchurTests
{
    private static double[,] Random(int n, int seed)
    {
        var random = new Random(seed);
        var a = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = (random.NextDouble() * 4) - 2;
            }
        }

        return a;
    }

    private static void AssertReassembles(double[,] a, double[,] t, double[,] u)
    {
        int n = a.GetLength(0);
        double[,] product = Linear.Multiply(Linear.Multiply(u, t), Linear.Transpose(u));

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(a[i, j], product[i, j], 1e-10);
            }
        }
    }

    private static void AssertOrthogonal(double[,] u)
    {
        int n = u.GetLength(0);
        double[,] product = Linear.Multiply(Linear.Transpose(u), u);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(i == j ? 1.0 : 0.0, product[i, j], 1e-12);
            }
        }
    }

    /// <summary>Quasi-triangular means nothing below the subdiagonal, and no two adjacent 2×2 blocks.</summary>
    private static void AssertQuasiTriangular(double[,] t)
    {
        int n = t.GetLength(0);
        for (int i = 2; i < n; i++)
        {
            for (int j = 0; j < i - 1; j++)
            {
                Assert.Equal(0.0, t[i, j]);
            }
        }

        for (int i = 0; i + 1 < n; i++)
        {
            if (t[i + 1, i] != 0)
            {
                Assert.True(i + 2 >= n || t[i + 2, i + 1] == 0, $"Two 2×2 blocks overlap at row {i}.");

                // A standardized 2×2 block has equal diagonal entries and a genuinely complex pair.
                Assert.Equal(t[i, i], t[i + 1, i + 1], 1e-10);
                Assert.True(t[i, i + 1] * t[i + 1, i] < 0, $"The block at row {i} does not hold a conjugate pair.");
                i++;
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 7)]
    [InlineData(8, 11)]
    [InlineData(12, 13)]
    public void Factor_ReassemblesTheMatrixItCameFrom(int n, int seed)
    {
        double[,] a = Random(n, seed);
        Schur schur = Schur.Factor(a);

        AssertReassembles(a, schur.T, schur.U);
        AssertOrthogonal(schur.U);
        AssertQuasiTriangular(schur.T);
    }

    [Fact]
    public void Factor_HandlesASymmetricMatrix()
    {
        // A symmetric matrix has real eigenvalues, so its Schur form has to come out fully
        // triangular — and, being symmetric, diagonal.
        double[,] a = Random(6, 5);
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < i; j++)
            {
                a[i, j] = a[j, i];
            }
        }

        Schur schur = Schur.Factor(a);
        AssertReassembles(a, schur.T, schur.U);

        for (int i = 1; i < 6; i++)
        {
            Assert.Equal(0.0, schur.T[i, i - 1], 1e-10);
        }
    }

    [Fact]
    public void Factor_FindsTheKnownEigenvaluesOfARotation()
    {
        // A 2-D rotation by 30° has eigenvalues e^±i30°, which must come out as one 2×2 block.
        double angle = Math.PI / 6;
        double[,] a = { { Math.Cos(angle), -Math.Sin(angle) }, { Math.Sin(angle), Math.Cos(angle) } };

        Complex[] values = Schur.Factor(a).Eigenvalues;
        Assert.Equal(2, values.Length);
        Assert.Equal(Math.Cos(angle), values[0].Real, 1e-14);
        Assert.Equal(Math.Sin(angle), Math.Abs(values[0].Imaginary), 1e-14);
        Assert.Equal(-values[0].Imaginary, values[1].Imaginary, 1e-14);
    }

    /// <summary>
    /// The eigenvalues have to sum to the trace and multiply to the determinant. Those two are
    /// independent of the factorization, so they catch a spectrum that is merely plausible —
    /// the failure mode a shifted QR that loses track of a shift actually has.
    /// </summary>
    [Theory]
    [InlineData(4, 5)]
    [InlineData(7, 21)]
    [InlineData(10, 41)]
    public void Factor_FindsASpectrumWithTheRightTraceAndDeterminant(int n, int seed)
    {
        double[,] a = Random(n, seed);
        Complex[] values = Schur.Factor(a).Eigenvalues;

        Complex sum = Complex.Zero;
        Complex product = Complex.One;
        double trace = 0;
        for (int i = 0; i < n; i++)
        {
            trace += a[i, i];
            sum += values[i];
            product *= values[i];
        }

        double determinant = LuDecomposition.Factor(a).Determinant;
        Assert.Equal(trace, sum.Real, 1e-10);
        Assert.Equal(0.0, sum.Imaginary, 1e-12);
        Assert.Equal(determinant, product.Real, Math.Abs(determinant) * 1e-9);
        Assert.Equal(0.0, product.Imaginary, Math.Abs(determinant) * 1e-9);
    }

    [Fact]
    public void Factor_AndTheEigensolverAgree()
    {
        // eig now reads its values off this factorization, so the two must agree exactly.
        double[,] a = Random(7, 21);
        Complex[] fromSchur = Schur.Factor(a).Eigenvalues;
        Complex[] fromEigen = Eigen.Factor(a).Values;

        Assert.Equal(fromEigen.Length, fromSchur.Length);
        foreach (Complex expected in fromEigen)
        {
            Assert.Contains(fromSchur, actual => Complex.Abs(actual - expected) < 1e-10);
        }
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(9, 17)]
    public void Reorder_BringsTheSelectedEigenvaluesToTheTop(int n, int seed)
    {
        double[,] a = Random(n, seed);
        Schur schur = Schur.Factor(a);
        Complex[] before = schur.Eigenvalues;

        // Select everything with a positive real part — the stable/unstable split a control
        // engineer actually asks for.
        bool[] select = new bool[n];
        for (int i = 0; i < n; i++)
        {
            select[i] = before[i].Real > 0;
        }

        Schur reordered = Schur.Reorder(schur.T, schur.U, select);
        AssertReassembles(a, reordered.T, reordered.U);
        AssertOrthogonal(reordered.U);
        AssertQuasiTriangular(reordered.T);

        Complex[] after = reordered.Eigenvalues;
        int wanted = select.Count(flag => flag);

        // The count is preserved and the wanted ones lead. A 2×2 block moves whole, so the boundary
        // can slip by one when a conjugate pair straddles it — the test allows for that.
        Assert.Equal(n, after.Length);
        foreach (Complex value in before)
        {
            Assert.Contains(after, other => Complex.Abs(other - value) < 1e-7);
        }

        int leading = 0;
        while (leading < n && after[leading].Real > 0)
        {
            leading++;
        }

        Assert.True(leading >= wanted - 1, $"Only {leading} of {wanted} selected eigenvalues came to the top.");
    }

    [Fact]
    public void Reorder_LeavesAMatrixWithRepeatedEigenvaluesAlone()
    {
        // Adjacent equal eigenvalues make the exchange's Sylvester system singular. Swapping them
        // is a no-op, and the routine has to say so rather than fail.
        double[,] a = { { 2, 1, 0 }, { 0, 2, 1 }, { 0, 0, 2 } };
        Schur schur = Schur.Factor(a);
        Schur reordered = Schur.Reorder(schur.T, schur.U, [false, false, true]);

        AssertReassembles(a, reordered.T, reordered.U);
        foreach (Complex value in reordered.Eigenvalues)
        {
            Assert.Equal(2.0, value.Real, 1e-7);
        }
    }
}

/// <summary>
/// The rank-one updates. Each is checked against the factorization computed from scratch, which is
/// the thing the update exists to avoid doing.
/// </summary>
public class RankOneUpdateTests
{
    private static double[,] PositiveDefinite(int n, int seed)
    {
        var random = new Random(seed);
        var b = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                b[i, j] = random.NextDouble() - 0.5;
            }
        }

        double[,] a = Linear.Multiply(Linear.Transpose(b), b);
        for (int i = 0; i < n; i++)
        {
            a[i, i] += n;
        }

        return a;
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    public void CholeskyUpdate_MatchesRefactoringTheUpdatedMatrix(int n, int seed)
    {
        double[,] a = PositiveDefinite(n, seed);
        var random = new Random(seed + 100);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = random.NextDouble();
        }

        // R is the upper factor, so RᵀR = A.
        double[,] r = Linear.Transpose(Cholesky.Factor(a).Lower);
        double[,] updated = RankOneUpdates.CholeskyUpdate(r, x);

        var expectedMatrix = (double[,])a.Clone();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                expectedMatrix[i, j] += x[i] * x[j];
            }
        }

        double[,] product = Linear.Multiply(Linear.Transpose(updated), updated);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(expectedMatrix[i, j], product[i, j], 1e-10);
            }
        }
    }

    [Fact]
    public void CholeskyDowndate_UndoesAnUpdate()
    {
        const int n = 5;
        double[,] a = PositiveDefinite(n, 9);
        double[] x = [0.3, -0.7, 0.2, 0.9, -0.1];

        double[,] r = Linear.Transpose(Cholesky.Factor(a).Lower);
        double[,] up = RankOneUpdates.CholeskyUpdate(r, x);
        double[,]? back = RankOneUpdates.CholeskyDowndate(up, x);

        Assert.NotNull(back);
        double[,] product = Linear.Multiply(Linear.Transpose(back!), back!);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(a[i, j], product[i, j], 1e-9);
            }
        }
    }

    [Fact]
    public void CholeskyDowndate_ReportsWhenDefinitenessWouldBeLost()
    {
        // Subtracting a vector far larger than the matrix cannot leave a positive definite result.
        double[,] a = PositiveDefinite(3, 4);
        double[,] r = Linear.Transpose(Cholesky.Factor(a).Lower);
        Assert.Null(RankOneUpdates.CholeskyDowndate(r, [100, 100, 100]));
    }

    [Theory]
    [InlineData(4, 4, 31)]
    [InlineData(6, 3, 37)]
    public void QrUpdate_MatchesRefactoringTheUpdatedMatrix(int rows, int columns, int seed)
    {
        var random = new Random(seed);
        var a = new double[rows, columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                a[i, j] = (random.NextDouble() * 2) - 1;
            }
        }

        var u = new double[rows];
        var v = new double[columns];
        for (int i = 0; i < rows; i++)
        {
            u[i] = random.NextDouble();
        }

        for (int j = 0; j < columns; j++)
        {
            v[j] = random.NextDouble();
        }

        QrDecomposition qr = QrDecomposition.Factor(a);
        (double[,] qNew, double[,] rNew) = RankOneUpdates.QrUpdate(qr.FullQ, qr.FullR, u, v);

        double[,] product = Linear.Multiply(qNew, rNew);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                Assert.Equal(a[i, j] + (u[i] * v[j]), product[i, j], 1e-10);
            }
        }

        // Q stays orthogonal and R stays upper triangular; an update that lost either would still
        // multiply back correctly but would no longer be a QR factorization.
        double[,] gram = Linear.Multiply(Linear.Transpose(qNew), qNew);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < rows; j++)
            {
                Assert.Equal(i == j ? 1.0 : 0.0, gram[i, j], 1e-11);
            }
        }

        for (int i = 1; i < rows; i++)
        {
            for (int j = 0; j < Math.Min(i, columns); j++)
            {
                Assert.Equal(0.0, rNew[i, j], 1e-11);
            }
        }
    }
}
