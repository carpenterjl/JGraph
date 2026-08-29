using System;

namespace JGraph.Numerics;

/// <summary>
/// The classical named matrices — Toeplitz, Hankel, Vandermonde, companion, Pascal, Hadamard,
/// Wilkinson, inverse Hilbert and Rosser — built straight into column-major storage.
/// </summary>
/// <remarks>
/// <para>
/// Every builder here answers a <c>double[]</c> laid out column by column, which is the storage the
/// rest of the engine reads, so nothing is transposed on the way out. None of them allocates a row
/// of rows: a Toeplitz matrix is a rule about <c>i − j</c> and is written as one.
/// </para>
/// <para>
/// The two matrices with no formula behind them — Rosser's 8-by-8 and the two circulants Hadamard's
/// construction needs at orders 12 and 20 — are written out as the constants they are. Everything
/// else is computed, including the inverse Hilbert matrix, whose entries pass a long way outside
/// what a binomial coefficient can hold and so are carried by the published recurrence instead.
/// </para>
/// </remarks>
public static class TestMatrices
{
    /// <summary>Rosser's 8-by-8: symmetric, one double eigenvalue, one zero, one tiny one.</summary>
    private static readonly double[] RosserRows =
    [
        611, 196, -192, 407, -8, -52, -49, 29,
        196, 899, 113, -192, -71, -43, -8, -44,
        -192, 113, 899, 196, 61, 49, 8, 52,
        407, -192, 196, 611, 8, 44, 59, -23,
        -8, -71, 61, 8, 411, -599, 208, 208,
        -52, -43, 49, 44, -599, 411, 208, 208,
        -49, -8, 8, 59, 208, 208, 99, -911,
        29, -44, 52, -23, 208, 208, -911, 99,
    ];

    /// <summary>The 11-long generator of the order-12 Hadamard matrix's circulant block.</summary>
    private static readonly double[] Hadamard12Seed =
        [-1, 1, -1, 1, 1, 1, -1, -1, -1, 1, -1];

    /// <summary>The 19-long generator of the order-20 Hadamard matrix's circulant block.</summary>
    private static readonly double[] Hadamard20Seed =
        [-1, -1, 1, 1, -1, -1, -1, -1, 1, -1, 1, -1, 1, 1, 1, 1, -1, -1, 1];

    /// <summary>Rosser's matrix, freshly copied so a caller may keep it.</summary>
    public static double[] Rosser()
    {
        // Symmetric, so the row-major constant above is already column-major.
        return (double[])RosserRows.Clone();
    }

    /// <summary>
    /// The Toeplitz matrix with first column <paramref name="column"/> and first row
    /// <paramref name="row"/>: constant along every diagonal, so entry (i, j) is decided by
    /// <c>i − j</c> alone. Below the diagonal it reads the column, above it the row.
    /// </summary>
    public static double[] Toeplitz(ReadOnlySpan<double> column, ReadOnlySpan<double> row)
    {
        int rows = column.Length;
        int cols = row.Length;
        var result = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            int at = c * rows;
            for (int r = 0; r < rows; r++)
            {
                result[at + r] = r >= c ? column[r - c] : row[c - r];
            }
        }

        return result;
    }

    /// <summary>
    /// The Hankel matrix with first column <paramref name="column"/> and last row
    /// <paramref name="row"/>: constant along every anti-diagonal, so entry (i, j) is decided by
    /// <c>i + j</c>. Past the column's end it continues into the row, whose first entry is the
    /// column's last and is therefore skipped.
    /// </summary>
    public static double[] Hankel(ReadOnlySpan<double> column, ReadOnlySpan<double> row)
    {
        int rows = column.Length;
        int cols = row.Length;
        var result = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            int at = c * rows;
            for (int r = 0; r < rows; r++)
            {
                int along = r + c;
                result[at + r] = along < rows ? column[along] : row[along - rows + 1];
            }
        }

        return result;
    }

    /// <summary>
    /// The Vandermonde matrix of <paramref name="points"/>, MATLAB's way round: the powers descend
    /// across each row, so the last column is all ones and the second-to-last is the points
    /// themselves.
    /// </summary>
    public static double[] Vandermonde(ReadOnlySpan<double> points)
    {
        int n = points.Length;
        var result = new double[n * n];

        // Filled right to left, each column the one to its right times the point: one multiply per
        // entry rather than a Pow, and exact for the integer points these matrices are usually built
        // from.
        for (int r = 0; r < n; r++)
        {
            result[((n - 1) * n) + r] = 1;
        }

        for (int c = n - 2; c >= 0; c--)
        {
            int at = c * n;
            int right = (c + 1) * n;
            for (int r = 0; r < n; r++)
            {
                result[at + r] = result[right + r] * points[r];
            }
        }

        return result;
    }

    /// <summary>
    /// The companion matrix of the polynomial <paramref name="coefficients"/>, highest power first:
    /// an <c>(n−1)</c>-square whose first row is the trailing coefficients negated and divided by
    /// the leading one, with ones down the subdiagonal. Its eigenvalues are the polynomial's roots.
    /// </summary>
    public static double[] Companion(ReadOnlySpan<double> coefficients, out int order)
    {
        order = coefficients.Length - 1;
        if (order <= 0)
        {
            order = Math.Max(order, 0);
            return [];
        }

        var result = new double[order * order];
        double leading = coefficients[0];
        for (int c = 0; c < order; c++)
        {
            result[c * order] = -coefficients[c + 1] / leading;
            if (c + 1 < order)
            {
                result[(c * order) + c + 1] = 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Pascal's matrix of order <paramref name="n"/>. <paramref name="kind"/> 0 is the symmetric
    /// one of binomial coefficients, 1 its lower-triangular Cholesky factor with alternating signs,
    /// and 2 a rotation of that factor which is a cube root of the identity.
    /// </summary>
    public static double[] Pascal(int n, int kind)
    {
        var result = new double[n * n];
        if (n == 0)
        {
            return result;
        }

        if (kind == 0)
        {
            // Built by the addition rule rather than by a factorial, so every entry stays exact for
            // as long as a double can hold it.
            for (int i = 0; i < n; i++)
            {
                result[i] = 1;
                result[i * n] = 1;
            }

            for (int c = 1; c < n; c++)
            {
                for (int r = 1; r < n; r++)
                {
                    result[(c * n) + r] = result[((c - 1) * n) + r] + result[(c * n) + r - 1];
                }
            }

            return result;
        }

        // The signed lower-triangular factor, again by the addition rule: L(r, c) = L(r−1, c) −
        // L(r−1, c−1), which is the alternating-sign form of Pascal's triangle.
        var factor = new double[n * n];
        for (int r = 0; r < n; r++)
        {
            factor[r] = 1;
        }

        for (int c = 1; c < n; c++)
        {
            for (int r = c; r < n; r++)
            {
                factor[(c * n) + r] = factor[(c * n) + r - 1] - factor[((c - 1) * n) + r - 1];
            }
        }

        if (kind == 1)
        {
            return factor;
        }

        // A quarter turn clockwise, negated when n is even. Both halves are needed: the turn alone
        // cubes to minus the identity for even n.
        double sign = (n & 1) == 0 ? -1 : 1;
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = sign * factor[(r * n) + n - 1 - c];
            }
        }

        return result;
    }

    /// <summary>
    /// Whether <paramref name="n"/> is an order a Hadamard matrix can be built at here: a power of
    /// two, or twelve or twenty times one. If it is, <paramref name="seed"/> is the order the
    /// construction starts from and <paramref name="doublings"/> how many times it is doubled.
    /// </summary>
    public static bool IsHadamardOrder(int n, out int seed, out int doublings)
    {
        seed = 0;
        doublings = 0;
        if (n < 1)
        {
            return false;
        }

        foreach (int candidate in (ReadOnlySpan<int>)[1, 12, 20])
        {
            if (n % candidate != 0)
            {
                continue;
            }

            int rest = n / candidate;
            if ((rest & (rest - 1)) != 0)
            {
                continue; // not a power of two
            }

            seed = candidate;
            doublings = System.Numerics.BitOperations.TrailingZeroCount(rest);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The Hadamard matrix of order <paramref name="n"/>: ±1 entries with mutually orthogonal
    /// columns, so <c>H'H = nI</c>. Sylvester's doubling does the work; the two orders it cannot
    /// reach on its own, twelve and twenty, start from a bordered circulant instead.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="n"/> is not an order this construction reaches.
    /// </exception>
    public static double[] Hadamard(int n)
    {
        if (!IsHadamardOrder(n, out int seed, out int doublings))
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "not a Hadamard order");
        }

        double[] current = seed switch
        {
            12 => BorderedCirculant(Hadamard12Seed, shiftRight: true),
            20 => BorderedCirculant(Hadamard20Seed, shiftRight: false),
            _ => [1.0],
        };

        int order = seed;
        for (int step = 0; step < doublings; step++)
        {
            current = Double(current, order);
            order *= 2;
        }

        return current;

        // [H H; H -H] — Sylvester's step, written straight into the doubled column-major block.
        static double[] Double(double[] source, int size)
        {
            int grown = size * 2;
            var result = new double[grown * grown];
            for (int c = 0; c < size; c++)
            {
                for (int r = 0; r < size; r++)
                {
                    double value = source[(c * size) + r];
                    result[(c * grown) + r] = value;
                    result[(c * grown) + size + r] = value;
                    result[((size + c) * grown) + r] = value;
                    result[((size + c) * grown) + size + r] = -value;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// A row and a column of ones bordering the circulant that <paramref name="generator"/> spins
    /// out — each row the one above it rotated a step, in the direction
    /// <paramref name="shiftRight"/> names.
    /// </summary>
    private static double[] BorderedCirculant(double[] generator, bool shiftRight)
    {
        int inner = generator.Length;
        int n = inner + 1;
        var result = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            result[i] = 1;        // first column
            result[i * n] = 1;    // first row
        }

        for (int c = 0; c < inner; c++)
        {
            for (int r = 0; r < inner; r++)
            {
                int pick = shiftRight
                    ? ((c - r) % inner + inner) % inner
                    : (c + r) % inner;
                result[((c + 1) * n) + r + 1] = generator[pick];
            }
        }

        return result;
    }

    /// <summary>
    /// Wilkinson's eigenvalue test matrix of order <paramref name="n"/>: symmetric, tridiagonal,
    /// ones off the diagonal and a V of distances to the middle along it. Its largest two
    /// eigenvalues agree to about fourteen digits without being equal.
    /// </summary>
    public static double[] Wilkinson(int n)
    {
        var result = new double[n * n];
        if (n == 0)
        {
            return result;
        }

        double middle = (n - 1) / 2.0;
        for (int i = 0; i < n; i++)
        {
            result[(i * n) + i] = Math.Abs(middle - i);
            if (i + 1 < n)
            {
                result[(i * n) + i + 1] = 1;
                result[((i + 1) * n) + i] = 1;
            }
        }

        return result;
    }

    /// <summary>
    /// The exact inverse of the Hilbert matrix of order <paramref name="n"/>, whose entries are
    /// integers however large.
    /// </summary>
    /// <remarks>
    /// Carried by the recurrence rather than by the closed form. The closed form multiplies three
    /// binomial coefficients, each of which overflows a double long before their quotient does;
    /// stepping along a row instead keeps one running value that is only ever the size of the
    /// answer. Past order 13 the answers pass beyond where a double counts by ones and stop being
    /// exact, which is a property of the matrix and not of the arithmetic.
    /// </remarks>
    public static double[] InverseHilbert(int n)
    {
        var result = new double[n * n];
        double p = n;
        for (int i = 1; i <= n; i++)
        {
            double r = p * p;
            result[((i - 1) * n) + i - 1] = r / ((2 * i) - 1);
            for (int j = i + 1; j <= n; j++)
            {
                r = -((n - j + 1) * r * (n + j - 1)) / ((double)(j - 1) * (j - 1));
                double entry = r / (i + j - 1);
                result[((j - 1) * n) + i - 1] = entry;
                result[((i - 1) * n) + j - 1] = entry;
            }

            p = ((n - i) * p * (n + i)) / ((double)i * i);
        }

        return result;
    }
}
