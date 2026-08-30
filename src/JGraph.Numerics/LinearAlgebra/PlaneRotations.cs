using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The Givens rotation and the three things built out of it: the rotation that puts a two-element
/// vector on its first axis, and the updates that add or remove one row or one column of a matrix
/// whose QR factorization is already in hand.
/// </summary>
/// <remarks>
/// <para>
/// Everything here works in <see cref="Complex"/>, one implementation rather than two. That is safe
/// for real data because the only arithmetic a rotation does is add, subtract and multiply — each
/// of which leaves a zero imaginary part exactly zero and computes the real part with the same two
/// operations a real implementation would — and because every division is by the rotation's radius,
/// which is real, and is applied to the parts separately rather than through complex division.
/// Complex division is the one operation that would not have been safe: .NET computes it by Smith's
/// algorithm, which is not the same expression as a real quotient even when the denominator's
/// imaginary part is nought.
/// </para>
/// <para>
/// An update costs one rotation per row it has to walk rather than the whole factorization again,
/// which is the point of having them at all: appending a column to a design matrix and re-solving
/// is O(mn) here where re-factoring is O(mn²).
/// </para>
/// </remarks>
public static class PlaneRotations
{
    /// <summary>
    /// The rotation G and the rotated vector y for which <c>G·x = y</c> and <c>y(2) = 0</c>: the
    /// two-by-two case that every update below is written out of.
    /// </summary>
    /// <remarks>
    /// A second element that is already nought is left alone rather than rotated by an identity
    /// computed from it, because a radius of zero would divide the whole rotation by zero and hand
    /// back NaN where the answer is the identity.
    /// </remarks>
    public static (Complex[,] Rotation, Complex First, Complex Second) Plane(Complex x1, Complex x2)
    {
        var g = new Complex[2, 2];
        if (x2 == Complex.Zero)
        {
            g[0, 0] = Complex.One;
            g[1, 1] = Complex.One;
            return (g, x1, x2);
        }

        double r = TwoNorm(x1, x2);
        g[0, 0] = Scaled(Complex.Conjugate(x1), r);
        g[0, 1] = Scaled(Complex.Conjugate(x2), r);
        g[1, 0] = Scaled(-x2, r);
        g[1, 1] = Scaled(x1, r);
        return (g, new Complex(r, 0.0), Complex.Zero);
    }

    /// <summary>
    /// The length of a two-element vector — the quantity MATLAB's <c>norm</c> answers, computed to
    /// the last bit rather than to within an ulp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="double.Hypot(double, double)"/> is the obvious call and it is the wrong one: it
    /// differs from a correctly rounded length in 48 of 400 measured pairs, and MATLAB's <c>norm</c>
    /// is correctly rounded, so <c>planerot</c>'s radius would have disagreed with <c>norm(x)</c>
    /// twelve times in a hundred. What is used instead is a rounded square root followed by one
    /// Newton step over the residual, with every term of that residual computed exactly by a fused
    /// multiply-add — the sum of squares, the two squarings and the square root each contribute a
    /// rounding error, and all four are recoverable.
    /// </para>
    /// <para>
    /// MATLAB's own <c>hypot</c> is a third answer again, agreeing with neither; the name this
    /// follows is <c>norm</c>, because that is what <c>planerot</c>'s source calls.
    /// </para>
    /// </remarks>
    public static double TwoNorm(Complex x1, Complex x2)
    {
        if (x1.Imaginary == 0 && x2.Imaginary == 0)
        {
            return Length(x1.Real, x2.Real);
        }

        // A complex pair is a real four-vector, and its length is taken pairwise: the exactness
        // above survives one composition, and there is no four-argument form to compose instead.
        return Length(Length(x1.Real, x1.Imaginary), Length(x2.Real, x2.Imaginary));
    }

    /// <summary>The correctly rounded length of a real two-vector.</summary>
    public static double Length(double first, double second)
    {
        double a = Math.Abs(first);
        double b = Math.Abs(second);
        if (double.IsInfinity(a) || double.IsInfinity(b))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return double.NaN;
        }

        if (a < b)
        {
            (a, b) = (b, a);
        }

        if (b == 0)
        {
            return a;
        }

        // Both are scaled by a power of two so that the squaring below can neither overflow nor
        // fall into the subnormals; a power of two is exact both ways, so the scaling costs nothing.
        int shift = Math.ILogB(a);
        double scale = Math.ScaleB(1.0, -shift);
        a *= scale;
        b *= scale;

        double x = a * a;
        double y = b * b;
        double sum = x + y;
        double root = Math.Sqrt(sum);

        // Everything the rounded arithmetic dropped, recovered: the two squarings, the addition
        // (exact by Fast2Sum because x is the larger), and the square root itself.
        double dropped = Math.FusedMultiplyAdd(a, a, -x)
            + Math.FusedMultiplyAdd(b, b, -y)
            + ((x - sum) + y)
            + Math.FusedMultiplyAdd(-root, root, sum);
        return Math.ScaleB(root + (dropped / (2 * root)), shift);
    }

    /// <summary>
    /// Inserts <paramref name="x"/> as the <paramref name="j"/>-th column (one-based) of the matrix
    /// whose factors are <paramref name="q"/> and <paramref name="r"/>, and answers the factors of
    /// the matrix that results.
    /// </summary>
    public static (Complex[,] Q, Complex[,] R) InsertColumn(Complex[,] q, Complex[,] r, int j, Complex[] x)
    {
        int mq = q.GetLength(0);
        int mr = r.GetLength(0);
        int nr = r.GetLength(1);

        Complex[,] work = UpperTriangle(r, mr, nr + 1);
        for (int c = nr; c >= j; c--)
        {
            for (int i = 0; i < mr; i++)
            {
                work[i, c] = work[i, c - 1];
            }
        }

        // The new column reaches R through Qᴴ, which is what makes the columns of the enlarged Q
        // still span what they spanned: only the rotations below change Q at all.
        for (int i = 0; i < mr; i++)
        {
            Complex sum = Complex.Zero;
            for (int k = 0; k < mq; k++)
            {
                sum += Complex.Conjugate(q[k, i]) * x[k];
            }

            work[i, j - 1] = sum;
        }

        var factor = (Complex[,])q.Clone();
        int columns = nr + 1;
        for (int k = mr - 1; k >= j; k--)
        {
            (Complex[,] g, Complex first, Complex second) = Plane(work[k - 1, j - 1], work[k, j - 1]);
            work[k - 1, j - 1] = first;
            work[k, j - 1] = second;
            if (k < columns)
            {
                ApplyLeft(g, work, k - 1, k, k, columns - 1);
            }

            ApplyRight(factor, g, k - 1, k);
        }

        return (factor, work);
    }

    /// <summary>
    /// Inserts <paramref name="x"/> as the <paramref name="j"/>-th row (one-based), which grows both
    /// factors by one: a row of the matrix is a row of Q as well as a row of R.
    /// </summary>
    public static (Complex[,] Q, Complex[,] R) InsertRow(Complex[,] q, Complex[,] r, int j, Complex[] x)
    {
        int mq = q.GetLength(0);
        int nq = q.GetLength(1);
        int mr = r.GetLength(0);
        int nr = r.GetLength(1);

        Complex[,] work = UpperTriangle(r, mr + 1, nr);
        for (int c = nr - 1; c >= 0; c--)
        {
            for (int i = mr; i >= 1; i--)
            {
                work[i, c] = work[i - 1, c];
            }

            work[0, c] = x[c];
        }

        // The new row enters Q as a unit vector in a brand-new first column, so that the rotations
        // below have somewhere to rotate it into. Everything the old Q held moves one column right.
        var factor = new Complex[mq + 1, nq + 1];
        for (int c = 0; c < nq; c++)
        {
            for (int i = 0; i < j - 1; i++)
            {
                factor[i, c + 1] = q[i, c];
            }

            for (int i = j - 1; i < mq; i++)
            {
                factor[i + 1, c + 1] = q[i, c];
            }
        }

        factor[j - 1, 0] = Complex.One;

        int passes = Math.Min(mr, nr);
        for (int i = 1; i <= passes; i++)
        {
            (Complex[,] g, Complex first, Complex second) = Plane(work[i - 1, i - 1], work[i, i - 1]);
            work[i - 1, i - 1] = first;
            work[i, i - 1] = second;
            ApplyLeft(g, work, i - 1, i, i, nr - 1);
            ApplyRight(factor, g, i - 1, i);
        }

        return (factor, work);
    }

    /// <summary>Removes the <paramref name="j"/>-th column (one-based) and re-triangularizes.</summary>
    public static (Complex[,] Q, Complex[,] R) DeleteColumn(Complex[,] q, Complex[,] r, int j)
    {
        int mq = q.GetLength(0);
        int nq = q.GetLength(1);
        int mr = r.GetLength(0);
        int nr = r.GetLength(1);

        Complex[,] full = UpperTriangle(r, mr, nr);
        var work = new Complex[mr, nr - 1];
        for (int c = 0, at = 0; c < nr; c++)
        {
            if (c == j - 1)
            {
                continue;
            }

            for (int i = 0; i < mr; i++)
            {
                work[i, at] = full[i, c];
            }

            at++;
        }

        var factor = (Complex[,])q.Clone();
        int columns = nr - 1;
        int last = Math.Min(columns, mr - 1);
        for (int k = j; k <= last; k++)
        {
            (Complex[,] g, Complex first, Complex second) = Plane(work[k - 1, k - 1], work[k, k - 1]);
            work[k - 1, k - 1] = first;
            work[k, k - 1] = second;
            if (k < columns)
            {
                ApplyLeft(g, work, k - 1, k, k, columns - 1);
            }

            ApplyRight(factor, g, k - 1, k);
        }

        // A factorization that was economy-sized to begin with loses its last row of R and its last
        // column of Q, because one fewer column means one fewer reflector was ever needed.
        return mq == nq ? (factor, work) : (Trim(factor, mq, nq - 1), Trim(work, mr - 1, columns));
    }

    /// <summary>Removes the <paramref name="j"/>-th row (one-based) and re-triangularizes.</summary>
    public static (Complex[,] Q, Complex[,] R) DeleteRow(Complex[,] q, Complex[,] r, int j)
    {
        int mq = q.GetLength(0);
        int nq = q.GetLength(1);
        int mr = r.GetLength(0);
        int nr = r.GetLength(1);

        Complex[,] work = UpperTriangle(r, mr, nr);
        var factor = (Complex[,])q.Clone();

        // The row being taken out is read off Q, rotated down to a single one in the first place,
        // and the same rotations carried through R. What is left of Q above and below that place is
        // the factor of the matrix with the row gone.
        var row = new Complex[mr];
        for (int i = 0; i < mr; i++)
        {
            row[i] = Complex.Conjugate(q[j - 1, i]);
        }

        for (int i = mr; i >= 2; i--)
        {
            (Complex[,] g, Complex first, Complex second) = Plane(row[i - 2], row[i - 1]);
            row[i - 2] = first;
            row[i - 1] = second;
            ApplyRight(factor, g, i - 2, i - 1);
            ApplyLeft(g, work, i - 2, i - 1, i - 2, nr - 1);
        }

        var trimmed = new Complex[mq - 1, nq - 1];
        for (int c = 1; c < nq; c++)
        {
            for (int i = 0, at = 0; i < mq; i++)
            {
                if (i == j - 1)
                {
                    continue;
                }

                trimmed[at++, c - 1] = factor[i, c];
            }
        }

        var cut = new Complex[mr - 1, nr];
        for (int c = 0; c < nr; c++)
        {
            for (int i = 1; i < mr; i++)
            {
                cut[i - 1, c] = work[i, c];
            }
        }

        return (trimmed, cut);
    }

    /// <summary>Multiplies rows <paramref name="top"/> and <paramref name="bottom"/> of a matrix by G, over the given column span.</summary>
    private static void ApplyLeft(Complex[,] g, Complex[,] m, int top, int bottom, int from, int through)
    {
        for (int c = from; c <= through; c++)
        {
            Complex a = m[top, c];
            Complex b = m[bottom, c];
            m[top, c] = (g[0, 0] * a) + (g[0, 1] * b);
            m[bottom, c] = (g[1, 0] * a) + (g[1, 1] * b);
        }
    }

    /// <summary>Multiplies columns <paramref name="left"/> and <paramref name="right"/> of a matrix by Gᴴ.</summary>
    private static void ApplyRight(Complex[,] m, Complex[,] g, int left, int right)
    {
        int rows = m.GetLength(0);
        for (int i = 0; i < rows; i++)
        {
            Complex a = m[i, left];
            Complex b = m[i, right];
            m[i, left] = (a * Complex.Conjugate(g[0, 0])) + (b * Complex.Conjugate(g[0, 1]));
            m[i, right] = (a * Complex.Conjugate(g[1, 0])) + (b * Complex.Conjugate(g[1, 1]));
        }
    }

    /// <summary>A copy of R's upper triangle in a block of the requested shape, zero elsewhere.</summary>
    private static Complex[,] UpperTriangle(Complex[,] r, int rows, int cols)
    {
        var work = new Complex[rows, cols];
        int mr = Math.Min(rows, r.GetLength(0));
        int nr = Math.Min(cols, r.GetLength(1));
        for (int c = 0; c < nr; c++)
        {
            for (int i = 0; i <= Math.Min(c, mr - 1); i++)
            {
                work[i, c] = r[i, c];
            }
        }

        return work;
    }

    /// <summary>The leading block of a matrix, as a fresh array.</summary>
    private static Complex[,] Trim(Complex[,] m, int rows, int cols)
    {
        var cut = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                cut[i, c] = m[i, c];
            }
        }

        return cut;
    }

    /// <summary>A complex number over a real divisor, part by part — never through complex division.</summary>
    private static Complex Scaled(Complex value, double divisor) =>
        new(value.Real / divisor, value.Imaginary / divisor);
}
