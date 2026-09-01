using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The Hessenberg reduction after it was moved onto LAPACK's blocked kernel (M123).
/// </summary>
/// <remarks>
/// A faster factorization is only a faster factorization if it is the same one, and the checks that
/// say so are the factorization's own definition rather than a table of numbers: Q·H·Qᵀ is the matrix
/// that went in, Q is orthogonal, and H is zero below its first subdiagonal. A wrong sign or a
/// transposed Q passes none of the three and every eyeball test there is.
/// </remarks>
public class HessenbergM123Tests
{
    private static double[,] Deterministic(int n)
    {
        const double Phi = 0.618033988749895;
        var a = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                a[r, c] = ((((r * n) + c + 1) * Phi) % 1) - 0.5;
            }
        }

        return a;
    }

    private static double Frobenius(double[,] a)
    {
        double sum = 0;
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                sum += a[r, c] * a[r, c];
            }
        }

        return System.Math.Sqrt(sum);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(40)]
    [InlineData(120)]
    public void TheReductionIsTheMatrixItCameFrom(int n)
    {
        double[,] a = Deterministic(n);
        Hessenberg reduced = Hessenberg.Reduce(a);

        var back = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        sum += reduced.Q[r, i] * reduced.H[i, j] * reduced.Q[c, j];
                    }
                }

                back[r, c] = sum - a[r, c];
            }
        }

        Assert.True(Frobenius(back) <= 1e-12 * System.Math.Max(Frobenius(a), 1),
            $"n={n}: Q·H·Qᵀ is not the matrix it was made from");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(120)]
    public void QIsOrthogonal(int n)
    {
        Hessenberg reduced = Hessenberg.Reduce(Deterministic(n));

        var drift = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    sum += reduced.Q[i, r] * reduced.Q[i, c];
                }

                drift[r, c] = sum - (r == c ? 1 : 0);
            }
        }

        Assert.True(Frobenius(drift) < 1e-12, $"n={n}: QᵀQ is not the identity");
    }

    /// <summary>
    /// Hessenberg form means exactly zero below the subdiagonal, not nearly zero. A script asking
    /// <c>istriu(H, -1)</c> has to be told the truth.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(120)]
    public void NothingSurvivesBelowTheSubdiagonal(int n)
    {
        Hessenberg reduced = Hessenberg.Reduce(Deterministic(n));

        for (int r = 2; r < n; r++)
        {
            for (int c = 0; c < r - 1; c++)
            {
                Assert.Equal(0, reduced.H[r, c]);
            }
        }
    }

    /// <summary>
    /// A matrix already in Hessenberg form comes back unchanged with an identity beside it, which is
    /// the case both roads have to agree on and the one an order-two shortcut could get wrong.
    /// </summary>
    [Fact]
    public void AMatrixAlreadyInFormIsLeftAlone()
    {
        double[,] a = { { 1, 2 }, { 3, 4 } };
        Hessenberg reduced = Hessenberg.Reduce(a);

        Assert.Equal(1, reduced.Q[0, 0]);
        Assert.Equal(0, reduced.Q[0, 1]);
        Assert.Equal(0, reduced.Q[1, 0]);
        Assert.Equal(1, reduced.Q[1, 1]);
        Assert.Equal(2, reduced.H[0, 1]);
        Assert.Equal(3, reduced.H[1, 0]);
    }

    /// <summary>
    /// A symmetric matrix reduces to a tridiagonal one, because the reduction is a similarity and a
    /// similarity keeps symmetry. Nothing in the code says so, which is what makes it worth asking.
    /// </summary>
    [Fact]
    public void ASymmetricMatrixComesBackTridiagonal()
    {
        const int N = 30;
        double[,] a = Deterministic(N);
        var s = new double[N, N];
        for (int r = 0; r < N; r++)
        {
            for (int c = 0; c < N; c++)
            {
                s[r, c] = a[r, c] + a[c, r];
            }
        }

        Hessenberg reduced = Hessenberg.Reduce(s);

        double above = 0;
        for (int r = 0; r < N; r++)
        {
            for (int c = r + 2; c < N; c++)
            {
                above = System.Math.Max(above, System.Math.Abs(reduced.H[r, c]));
            }
        }

        Assert.True(above < 1e-11, $"the upper triangle kept {above}, so the form is not tridiagonal");
    }
}
