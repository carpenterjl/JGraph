using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The every-row plan's I1 (ADR 0324): the banded LU a stiff solver's iteration matrix takes when
/// its nonzeros sit in a narrow band. Its solves agree with the dense factorization's to rounding,
/// with row interchanges forced, and it declines what it should leave to the dense one.
/// </summary>
public class BandedLuI1Tests
{
    private static double[,] Banded(int n, int kl, int ku, int seed, bool weakDiagonal)
    {
        var random = new Random(seed);
        var a = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = Math.Max(0, r - kl); c <= Math.Min(n - 1, r + ku); c++)
            {
                a[r, c] = random.NextDouble() - 0.5;
            }

            // A weak diagonal over a strong first subdiagonal makes partial pivoting swap rows on
            // every step and keeps the matrix well conditioned (a row permutation of a dominant one).
            if (weakDiagonal && kl > 0 && r > 0)
            {
                a[r, r] *= 1e-3;
                a[r, r - 1] += a[r, r - 1] < 0 ? -4 : 4;
            }
            else
            {
                a[r, r] += 4;
            }
        }

        return a;
    }

    [Theory]
    [InlineData(400, 2, 2, false)]
    [InlineData(400, 2, 2, true)]
    [InlineData(257, 0, 3, false)]
    [InlineData(300, 5, 1, true)]
    [InlineData(64, 1, 1, true)]
    [InlineData(1000, 7, 4, true)]
    public void SolvesAgreeWithTheDenseFactorization(int n, int kl, int ku, bool weakDiagonal)
    {
        double[,] a = Banded(n, kl, ku, seed: n + kl + ku, weakDiagonal);
        Assert.Equal((kl, ku), BandedLu.Bandwidths(a));
        BandedLu? banded = BandedLu.TryFactor(a);
        Assert.NotNull(banded);
        var b = new double[n];
        for (int i = 0; i < n; i++)
        {
            b[i] = Math.Sin(i + 1);
        }

        double[] expected = LuDecomposition.Factor(a).Solve(b);
        double[] actual = banded!.Solve(b);
        double scale = expected.Max(Math.Abs);
        for (int i = 0; i < n; i++)
        {
            Assert.True(Math.Abs(actual[i] - expected[i]) <= 1e-10 * scale, $"x[{i}] {actual[i]} vs {expected[i]}");
        }

        // And the residual is the dense solution's order.
        double Residual(double[] x)
        {
            double worst = 0;
            for (int r = 0; r < n; r++)
            {
                double sum = 0;
                for (int c = 0; c < n; c++)
                {
                    sum += a[r, c] * x[c];
                }

                worst = Math.Max(worst, Math.Abs(sum - b[r]));
            }

            return worst;
        }

        Assert.True(Residual(actual) <= (10 * Residual(expected)) + 1e-13, $"residual {Residual(actual)} vs {Residual(expected)}");
    }

    [Fact]
    public void ItDeclinesSmallMatricesWideBandsAndZeroPivots()
    {
        Assert.Null(BandedLu.TryFactor(Banded(31, 1, 1, 1, weakDiagonal: false)));     // under 32
        Assert.Null(BandedLu.TryFactor(Banded(100, 6, 1, 2, weakDiagonal: false)));    // 2·6+1+1 = 14 > 100/8
        double[,] singular = Banded(200, 1, 1, 3, weakDiagonal: false);
        for (int c = 0; c < 200; c++)
        {
            singular[50, c] = 0;
            singular[c, 50] = 0;
        }

        Assert.Null(BandedLu.TryFactor(singular));
        Assert.IsType<LuDecomposition>(BandedLu.FactorIteration(Banded(10, 1, 1, 4, weakDiagonal: false)));
        Assert.IsType<BandedLu>(BandedLu.FactorIteration(Banded(400, 2, 2, 5, weakDiagonal: false)));
    }

    [Fact]
    public void ADenseMatrixWhoseOnlyNonzerosAreBandedIsBanded()
    {
        var a = new double[100, 100];
        for (int i = 0; i < 100; i++)
        {
            a[i, i] = 2;
        }

        a[60, 57] = 1;  // three below
        a[10, 12] = -1; // two above
        Assert.Equal((3, 2), BandedLu.Bandwidths(a));
    }
}

/// <summary>
/// I1 (ADR 0324): <c>colgroup</c>'s overlap table counted row by row gives the groups the
/// pair-by-pair count gave, on banded, random and degenerate patterns.
/// </summary>
public class ColumnGroupsI1Tests
{
    private static int[] Reference(bool[,] pattern)
    {
        int rows = pattern.GetLength(0);
        int n = pattern.GetLength(1);
        var groups = new int[n];
        for (int i = 0; i < rows; i++)
        {
            bool full = true;
            for (int j = 0; j < n && full; j++)
            {
                full = pattern[i, j];
            }

            if (full)
            {
                for (int j = 0; j < n; j++)
                {
                    groups[j] = j + 1;
                }

                return groups;
            }
        }

        var overlap = new int[n, n];
        for (int j = 0; j < n; j++)
        {
            for (int i = j; i < n; i++)
            {
                int shared = 0;
                for (int r = 0; r < rows; r++)
                {
                    if (pattern[r, i] && pattern[r, j])
                    {
                        shared++;
                    }
                }

                overlap[i, j] = shared;
            }
        }

        var pending = Enumerable.Range(0, n).ToList();
        var reach = new int[n];
        int groupNumber = 0;
        while (pending.Count > 0)
        {
            groupNumber++;
            int first = pending[0];
            groups[first] = groupNumber;
            for (int i = 0; i < n; i++)
            {
                reach[i] = overlap[i, first];
            }

            foreach (int k in pending)
            {
                if (reach[k] != 0)
                {
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    reach[i] += overlap[i, k];
                }

                groups[k] = groupNumber;
            }

            pending.RemoveAll(j => groups[j] != 0);
        }

        for (int j = 0; j < n; j++)
        {
            bool any = false;
            for (int r = 0; r < rows && !any; r++)
            {
                any = pattern[r, j];
            }

            if (!any)
            {
                groups[j] = 0;
            }
        }

        return groups;
    }

    [Theory]
    [InlineData(400, 400, 0.0, 2)]
    [InlineData(60, 60, 0.05, 0)]
    [InlineData(80, 50, 0.2, 0)]
    [InlineData(50, 80, 0.5, 1)]
    [InlineData(30, 30, 0.0, 0)]
    public void TheGroupsAreThePairwiseCountsGroups(int rows, int columns, double density, int band)
    {
        var random = new Random(rows * 31 + columns);
        var pattern = new bool[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                pattern[r, c] = Math.Abs(r - c) <= band || random.NextDouble() < density;
            }
        }

        // A column of zeros, where there is room for one.
        if (columns > 10 && band == 0)
        {
            for (int r = 0; r < rows; r++)
            {
                pattern[r, 7] = false;
            }
        }

        Assert.Equal(Reference(pattern), JGraph.Numerics.OdeNumericalJacobian.ColumnGroups(pattern));
    }
}
