using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The Sylvester equation <c>A·X + X·B = C</c>, solved by back-substitution once A and B have been
/// brought to triangular form — the Bartels-Stewart method.
/// </summary>
/// <remarks>
/// <para>
/// The equation is linear in X, so it could be written out as one system of m·n unknowns and handed
/// to a solver. That is the wrong answer by a wide margin: the system is m·n by m·n, which for a
/// pair of forty-by-forty matrices is sixteen hundred unknowns and a dense factorization of two and
/// a half million entries. Triangularizing first turns it into m·n small solves, each of them one
/// or four unknowns.
/// </para>
/// <para>
/// A real triangular form is only quasi-triangular: a conjugate pair of eigenvalues sits in a
/// two-by-two block on the diagonal, and a block cannot be divided through. So the substitution
/// walks blocks rather than entries, and each step solves a small Sylvester equation of its own —
/// at most two by two on each side, so at most four unknowns, written out as a plain linear system
/// because at that size nothing cleverer is worth the reading.
/// </para>
/// </remarks>
public static class SylvesterEquation
{
    /// <summary>
    /// Solves <c>A·X + X·B = C</c> where A and B are upper quasi-triangular — a real Schur form,
    /// or a complex one, in which case every block is one by one.
    /// </summary>
    public static Complex[,] SolveTriangular(Complex[,] a, Complex[,] b, Complex[,] c)
    {
        int m = a.GetLength(0);
        int n = b.GetLength(0);
        var x = new Complex[m, n];

        int[] rowBlocks = BlockStarts(a);
        int[] colBlocks = BlockStarts(b);

        // Columns run forward because B's strictly lower part is nought, so a column's right-hand
        // side needs only the columns already solved; rows run backward for the same reason about A.
        for (int lb = 0; lb < colBlocks.Length; lb++)
        {
            int l = colBlocks[lb];
            int q = (lb + 1 < colBlocks.Length ? colBlocks[lb + 1] : n) - l;
            for (int kb = rowBlocks.Length - 1; kb >= 0; kb--)
            {
                int k = rowBlocks[kb];
                int p = (kb + 1 < rowBlocks.Length ? rowBlocks[kb + 1] : m) - k;

                var rhs = new Complex[p, q];
                for (int i = 0; i < p; i++)
                {
                    for (int j = 0; j < q; j++)
                    {
                        Complex sum = c[k + i, l + j];
                        for (int t = k + p; t < m; t++)
                        {
                            sum -= a[k + i, t] * x[t, l + j];
                        }

                        for (int t = 0; t < l; t++)
                        {
                            sum -= x[k + i, t] * b[t, l + j];
                        }

                        rhs[i, j] = sum;
                    }
                }

                Complex[,] block = SolveBlock(a, k, p, b, l, q, rhs);
                for (int i = 0; i < p; i++)
                {
                    for (int j = 0; j < q; j++)
                    {
                        x[k + i, l + j] = block[i, j];
                    }
                }
            }
        }

        return x;
    }

    /// <summary>
    /// One block of the substitution: the small equation <c>Akk·Y + Y·Bll = R</c>, laid out as a
    /// linear system in the block's entries taken column by column.
    /// </summary>
    private static Complex[,] SolveBlock(
        Complex[,] a, int k, int p, Complex[,] b, int l, int q, Complex[,] rhs)
    {
        int size = p * q;
        var system = new Complex[size, size];
        var right = new Complex[size];

        for (int j = 0; j < q; j++)
        {
            for (int i = 0; i < p; i++)
            {
                int row = (j * p) + i;
                right[row] = rhs[i, j];
                for (int ii = 0; ii < p; ii++)
                {
                    system[row, (j * p) + ii] += a[k + i, k + ii];
                }

                for (int jj = 0; jj < q; jj++)
                {
                    system[row, (jj * p) + i] += b[l + jj, l + j];
                }
            }
        }

        Complex[] solution = SolveSmall(system, right, size);
        var y = new Complex[p, q];
        for (int j = 0; j < q; j++)
        {
            for (int i = 0; i < p; i++)
            {
                y[i, j] = solution[(j * p) + i];
            }
        }

        return y;
    }

    /// <summary>Gaussian elimination with partial pivoting over a system of at most four unknowns.</summary>
    private static Complex[] SolveSmall(Complex[,] system, Complex[] right, int n)
    {
        for (int col = 0; col < n; col++)
        {
            int best = col;
            for (int i = col + 1; i < n; i++)
            {
                if (system[i, col].Magnitude > system[best, col].Magnitude)
                {
                    best = i;
                }
            }

            if (best != col)
            {
                for (int c = 0; c < n; c++)
                {
                    (system[col, c], system[best, c]) = (system[best, c], system[col, c]);
                }

                (right[col], right[best]) = (right[best], right[col]);
            }

            Complex pivot = system[col, col];
            for (int i = col + 1; i < n; i++)
            {
                Complex factor = system[i, col] / pivot;
                if (factor == Complex.Zero)
                {
                    continue;
                }

                for (int c = col; c < n; c++)
                {
                    system[i, c] -= factor * system[col, c];
                }

                right[i] -= factor * right[col];
            }
        }

        var answer = new Complex[n];
        for (int i = n - 1; i >= 0; i--)
        {
            Complex sum = right[i];
            for (int c = i + 1; c < n; c++)
            {
                sum -= system[i, c] * answer[c];
            }

            answer[i] = sum / system[i, i];
        }

        return answer;
    }

    /// <summary>
    /// Where each diagonal block starts, reading a nonzero subdiagonal entry as the sign that two
    /// rows belong together.
    /// </summary>
    public static int[] BlockStarts(Complex[,] t)
    {
        int n = t.GetLength(0);
        var starts = new List<int>();
        for (int i = 0; i < n;)
        {
            starts.Add(i);
            i += i + 1 < n && t[i + 1, i] != Complex.Zero ? 2 : 1;
        }

        return [.. starts];
    }

    /// <summary>Whether every entry below the first subdiagonal is nought, and no two subdiagonal entries adjoin.</summary>
    public static bool IsQuasiTriangular(Complex[,] t, bool strictly)
    {
        int n = t.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            for (int i = c + 2; i < n; i++)
            {
                if (t[i, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        for (int i = 1; i < n; i++)
        {
            if (t[i, i - 1] == Complex.Zero)
            {
                continue;
            }

            if (strictly || (i + 1 < n && t[i + 1, i] != Complex.Zero))
            {
                return false;
            }
        }

        return true;
    }
}
