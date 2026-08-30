using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The generalized singular value decomposition of a pair of matrices sharing their columns, by way
/// of Van Loan's CS decomposition.
/// </summary>
/// <remarks>
/// <para>
/// The two matrices are stacked and factored once. Their shared column space is then a single
/// orthonormal basis, and what remains is to describe how each of the two matrices sits inside it —
/// which is the CS decomposition: two blocks of one orthonormal set, diagonalized against each
/// other so that the squares of the two diagonals sum to one everywhere. That is where the name
/// comes from, and it is why the answer is a pair of "cosines" and "sines" rather than one list of
/// singular values.
/// </para>
/// <para>
/// The generalized singular values are the ratios of those two diagonals, and they come out in
/// ascending order, which is the opposite of what an ordinary SVD does. That is not an oversight to
/// be corrected: the ratio is <c>c/s</c>, and a large one means A dominates where a large ordinary
/// singular value means the matrix does. Reversing it here would make <c>gsvd</c> of a pair
/// disagree with every published account of it.
/// </para>
/// </remarks>
public static class GeneralizedSvd
{
    /// <summary>The five factors, with <c>U·C·Xᴴ = A</c> and <c>V·S·Xᴴ = B</c>.</summary>
    public readonly record struct Factors(
        Complex[,] U, Complex[,] V, Complex[,] X, Complex[,] C, Complex[,] S);

    /// <summary>
    /// Factors the pair. When <paramref name="economy"/> is set, a matrix with more rows than
    /// columns is pre-factored so that its part of the answer has only as many columns as it needs.
    /// </summary>
    public static Factors Factor(Complex[,] a, Complex[,] b, bool economy)
    {
        int m = a.GetLength(0);
        int p = a.GetLength(1);
        int n = b.GetLength(0);

        Complex[,]? qa = null;
        Complex[,]? qb = null;
        if (economy && m > p)
        {
            HouseholderQr first = HouseholderQr.Factor(a, pivot: false);
            qa = first.Q(full: false);
            a = first.R(full: false);
            m = p;
        }

        if (economy && n > p)
        {
            HouseholderQr second = HouseholderQr.Factor(b, pivot: false);
            qb = second.Q(full: false);
            b = second.R(full: false);
            n = p;
        }

        var stacked = new Complex[m + n, p];
        for (int c = 0; c < p; c++)
        {
            for (int i = 0; i < m; i++)
            {
                stacked[i, c] = a[i, c];
            }

            for (int i = 0; i < n; i++)
            {
                stacked[m + i, c] = b[i, c];
            }
        }

        HouseholderQr stack = HouseholderQr.Factor(stacked, pivot: true);
        Complex[,] q = stack.Q(full: false);
        Complex[,] r = stack.R(full: false);
        int[] perm = stack.Pivot;

        int rank = 0;
        double[] diagonal = stack.DiagonalMagnitudes;
        if (diagonal.Length > 0)
        {
            double tolerance = Math.Max(m + n, p) * Spacing(diagonal[0]);
            foreach (double value in diagonal)
            {
                if (value > tolerance)
                {
                    rank++;
                }
            }
        }

        if (rank < q.GetLength(1))
        {
            q = Columns(q, rank);
            r = Rows(r, rank);
        }

        // The factorization pivoted the columns; the answer is about the original ones, so the
        // permutation is undone before anything else is built on top of R.
        var unpermuted = new Complex[r.GetLength(0), p];
        for (int c = 0; c < p; c++)
        {
            for (int i = 0; i < r.GetLength(0); i++)
            {
                unpermuted[i, perm[c]] = r[i, c];
            }
        }

        (Complex[,] u, Complex[,] v, Complex[,] z, Complex[,] cc, Complex[,] ss) =
            Cs(Rows(q, m), Skip(q, m, n));

        Complex[,] x = Multiply(ConjugateTranspose(unpermuted), z);
        if (qa is not null)
        {
            u = Multiply(qa, u);
        }

        if (qb is not null)
        {
            v = Multiply(qb, v);
        }

        return new Factors(u, v, x, cc, ss);
    }

    /// <summary>
    /// The generalized singular values alone: the ratio of the two diagonals, padded at whichever
    /// end has fewer rows than the shared rank.
    /// </summary>
    public static double[] Values(Complex[,] a, Complex[,] b)
    {
        int m = a.GetLength(0);
        int n = b.GetLength(0);
        Factors factors = Factor(a, b, economy: false);
        int q = factors.C.GetLength(1);

        var numerator = new double[q];
        var denominator = new double[q];
        int lead = Math.Max(0, q - m);
        for (int i = 0; i < q; i++)
        {
            numerator[i] = i < lead ? 0.0 : DiagonalAt(factors.C, i - lead, lead);
            denominator[i] = i < Math.Min(q, n) ? DiagonalAt(factors.S, i, 0) : 0.0;
        }

        var values = new double[q];
        for (int i = 0; i < q; i++)
        {
            values[i] = numerator[i] / denominator[i];
        }

        return values;
    }

    /// <summary>
    /// The CS decomposition of a matrix with orthonormal columns cut into an upper block Q1 and a
    /// lower block Q2.
    /// </summary>
    private static (Complex[,] U, Complex[,] V, Complex[,] Z, Complex[,] C, Complex[,] S) Cs(
        Complex[,] q1, Complex[,] q2)
    {
        int m = q1.GetLength(0);
        int p = q1.GetLength(1);
        int n = q2.GetLength(0);

        if (m < n)
        {
            // The decomposition is written for the taller block on top; when it is not, the two are
            // swapped and everything the recursion answers is reversed back into place.
            (Complex[,] v2, Complex[,] u2, Complex[,] z2, Complex[,] s2, Complex[,] c2) = Cs(q2, q1);
            ReverseColumns(c2);
            ReverseColumns(s2);
            ReverseColumns(z2);
            int top = Math.Min(m, p);
            ReverseLeadingRows(c2, top);
            ReverseLeadingColumns(u2, top);
            int bottom = Math.Min(n, p);
            ReverseLeadingRows(s2, bottom);
            ReverseLeadingColumns(v2, bottom);
            return (u2, v2, z2, c2, s2);
        }

        if (m == 1 && p > 1)
        {
            var z1 = new Complex[p, 2];
            for (int i = 0; i < p; i++)
            {
                z1[i, 0] = Complex.Conjugate(q2[0, i]);
                z1[i, 1] = Complex.Conjugate(q1[0, i]);
            }

            var one = new Complex[1, 1] { { Complex.One } };
            var flat = new Complex[1, 2] { { Complex.Zero, Complex.One } };
            var upright = new Complex[1, 2] { { Complex.One, Complex.Zero } };
            return (one, one, z1, flat, upright);
        }

        (Complex[,] u, double[] sigma, Complex[,] z) = ComplexEigen.Svd(q1, economy: false);
        var c = new Complex[m, p];
        for (int i = 0; i < sigma.Length; i++)
        {
            c[i, i] = new Complex(sigma[i], 0.0);
        }

        int q = Math.Min(m, p);
        ReverseLeadingBlock(c, q);
        ReverseLeadingColumns(u, q);
        ReverseLeadingColumns(z, q);

        Complex[,] s = Multiply(q2, z);
        int k;
        if (q == 1)
        {
            k = 0;
        }
        else if (m < p)
        {
            k = n;
        }
        else
        {
            k = 0;
            for (int i = 0; i < q; i++)
            {
                if (c[i, i].Magnitude <= 1.0 / Math.Sqrt(2.0))
                {
                    k = i + 1;
                }
            }
        }

        Complex[,] v = FullQ(Columns(s, k), n);
        s = Multiply(ConjugateTranspose(v), s);
        KeepDiagonal(s, Math.Min(k, m));

        if (k < Math.Min(n, p))
        {
            int right = Math.Min(n, p);
            Complex[,] tail = Sub(s, k, n - k, k, right - k);
            (Complex[,] ut, double[] st, Complex[,] vt) = ComplexEigen.Svd(tail, economy: false);
            for (int j = k; j < right; j++)
            {
                for (int i = 0; i < k; i++)
                {
                    s[i, j] = Complex.Zero;
                }

                for (int i = k; i < n; i++)
                {
                    s[i, j] = i - k < st.Length && i - k == j - k
                        ? new Complex(st[i - k], 0.0)
                        : Complex.Zero;
                }
            }

            ApplyRight(c, vt, k, right);
            ApplyRightColumns(v, ut, k, n);
            ApplyRight(z, vt, k, right);

            Complex[,] block = Sub(c, k, q - k, k, right - k);
            HouseholderQr corner = HouseholderQr.Factor(block, pivot: false);
            Complex[,] cq = corner.Q(full: true);
            Complex[,] cr = corner.R(full: true);
            KeepDiagonal(cr, Math.Min(cr.GetLength(0), cr.GetLength(1)));
            for (int j = k; j < right; j++)
            {
                for (int i = k; i < q; i++)
                {
                    c[i, j] = cr[i - k, j - k];
                }
            }

            ApplyRightColumns(u, cq, k, q);
        }

        if (m < p)
        {
            Wide(c, s, z, v, m, n, p, q);
        }

        if (n < p)
        {
            for (int j = n; j < p; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    s[i, j] = Complex.Zero;
                }
            }
        }

        Phase(u, c, Math.Max(0, p - m));
        RealPart(c);
        Phase(v, s, 0);
        RealPart(s);
        Normalize(u, v, z, c, s, m, n, p);
        return (u, v, z, c, s);
    }

    /// <summary>
    /// The extra rearrangement a block with fewer rows than shared columns needs: what the pair
    /// cannot resolve is pushed to the front and the columns rotated so the answer still reads left
    /// to right.
    /// </summary>
    private static void Wide(
        Complex[,] c, Complex[,] s, Complex[,] z, Complex[,] v, int m, int n, int p, int q)
    {
        double small = Math.Sqrt(2.220446049250313e-16);
        int byC = 0;
        for (int i = 0; i < Math.Min(c.GetLength(0), c.GetLength(1)); i++)
        {
            if (c[i, i].Magnitude > 10 * m * 2.220446049250313e-16)
            {
                byC++;
            }
        }

        int byS = 0;
        for (int i = 0; i < Math.Min(s.GetLength(0), s.GetLength(1)); i++)
        {
            if (s[i, i].Magnitude > 10 * n * 2.220446049250313e-16)
            {
                byS++;
            }
        }

        int byTail = 0;
        for (int i = 0; i < n; i++)
        {
            double worst = 0.0;
            for (int j = m; j < p; j++)
            {
                worst = Math.Max(worst, s[i, j].Magnitude);
            }

            if (worst < small)
            {
                byTail++;
            }
        }

        int rank = Math.Min(byC, Math.Min(byS, byTail));
        int most = m + n - p;
        for (int j = rank; j < most; j++)
        {
            double worst = 0.0;
            for (int i = 0; i < n; i++)
            {
                worst = Math.Max(worst, s[i, j].Magnitude);
            }

            if (worst > small)
            {
                rank++;
            }
        }

        Complex[,] block = Sub(s, rank, n - rank, m, p - m);
        HouseholderQr corner = HouseholderQr.Factor(block, pivot: false);
        Complex[,] cq = corner.Q(full: true);
        Complex[,] cr = corner.R(full: true);
        KeepDiagonal(cr, Math.Min(cr.GetLength(0), cr.GetLength(1)));

        for (int j = rank; j < p; j++)
        {
            for (int i = 0; i < n; i++)
            {
                s[i, j] = Complex.Zero;
            }
        }

        for (int j = m; j < p; j++)
        {
            for (int i = rank; i < n; i++)
            {
                s[i, j] = cr[i - rank, j - m];
            }
        }

        ApplyRightColumns(v, cq, rank, n);

        var rowOrder = new int[n];
        if (n > 1)
        {
            int at = 0;
            for (int i = rank; i < rank + p - m; i++)
            {
                rowOrder[at++] = i;
            }

            for (int i = 0; i < rank; i++)
            {
                rowOrder[at++] = i;
            }

            for (int i = rank + p - m; i < n; i++)
            {
                rowOrder[at++] = i;
            }
        }
        else
        {
            rowOrder[0] = 0;
        }

        var colOrder = new int[p];
        int put = 0;
        for (int j = m; j < p; j++)
        {
            colOrder[put++] = j;
        }

        for (int j = 0; j < m; j++)
        {
            colOrder[put++] = j;
        }

        PermuteColumns(c, colOrder);
        PermuteBoth(s, rowOrder, colOrder);
        PermuteColumns(z, colOrder);
        PermuteColumns(v, rowOrder);
        _ = q;
    }

    /// <summary>
    /// The last step: the two diagonals are rescaled so that each pair is a genuine cosine and sine,
    /// and the whole answer is reordered so the ratios ascend.
    /// </summary>
    private static void Normalize(
        Complex[,] u, Complex[,] v, Complex[,] z, Complex[,] c, Complex[,] s, int m, int n, int p)
    {
        int[] rowsC;
        int[] rowsS;
        int[] cols;
        if (m >= p && n >= p)
        {
            rowsC = Range(0, p);
            rowsS = Range(0, p);
            cols = Range(0, p);
        }
        else if (m >= p)
        {
            rowsC = Range(0, n);
            rowsS = Range(0, n);
            cols = Range(0, n);
            for (int i = n; i < p; i++)
            {
                c[i, i] = Complex.One;
            }
        }
        else
        {
            rowsC = Range(0, m + n - p);
            rowsS = Range(p - m, n - (p - m));
            cols = Range(p - m, n - (p - m));
            for (int t = 0; t < m - (m + n - p); t++)
            {
                c[m + n - p + t, n + t] = Complex.One;
            }

            for (int t = 0; t < p - m; t++)
            {
                s[t, t] = Complex.One;
            }
        }

        int count = cols.Length;
        var cosines = new double[count];
        var sines = new double[count];
        for (int i = 0; i < count; i++)
        {
            double cc = c[rowsC[i], cols[i]].Real;
            double ss = s[rowsS[i], cols[i]].Real;
            double length = double.Hypot(cc, ss);
            cosines[i] = cc / length;
            sines[i] = ss / length;
        }

        var order = new int[count];
        bool sorted = true;
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        for (int i = 1; i < count; i++)
        {
            if (cosines[i] / sines[i] < cosines[i - 1] / sines[i - 1])
            {
                sorted = false;
                break;
            }
        }

        if (!sorted)
        {
            Array.Sort(order, (x, y) =>
            {
                double left = cosines[x] / sines[x];
                double right = cosines[y] / sines[y];
                return left != right ? left.CompareTo(right) : x.CompareTo(y);
            });

            var reordered = new double[count];
            var reorderedSines = new double[count];
            for (int i = 0; i < count; i++)
            {
                reordered[i] = cosines[order[i]];
                reorderedSines[i] = sines[order[i]];
            }

            Array.Copy(reordered, cosines, count);
            Array.Copy(reorderedSines, sines, count);
            PermuteColumnRange(u, rowsC[0], order);
            PermuteColumnRange(v, rowsS[0], order);
            PermuteColumnRange(z, cols[0], order);
        }

        for (int i = 0; i < count; i++)
        {
            c[rowsC[i], cols[i]] = new Complex(cosines[i], 0.0);
            s[rowsS[i], cols[i]] = new Complex(sines[i], 0.0);
        }
    }

    /// <summary>
    /// Rotates each column so the entry on the chosen diagonal is real and non-negative, moving the
    /// phase into the unitary beside it.
    /// </summary>
    private static void Phase(Complex[,] y, Complex[,] x, int k)
    {
        int length = Math.Min(x.GetLength(0), x.GetLength(1) - k);
        for (int t = 0; t < length; t++)
        {
            Complex value = x[t, t + k];
            if (value.Real >= 0 && value.Imaginary == 0)
            {
                continue;
            }

            double size = value.Magnitude;
            if (size == 0)
            {
                continue;
            }

            Complex turn = Complex.Conjugate(value) / size;
            for (int i = 0; i < y.GetLength(0); i++)
            {
                y[i, t] *= Complex.Conjugate(turn);
            }

            for (int j = 0; j < x.GetLength(1); j++)
            {
                x[t, j] *= turn;
            }
        }
    }

    private static void RealPart(Complex[,] a)
    {
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                a[r, c] = new Complex(a[r, c].Real + 0.0, 0.0);
            }
        }
    }

    /// <summary>The spacing of the doubles at a magnitude — MATLAB's <c>eps(x)</c>.</summary>
    private static double Spacing(double x)
    {
        double size = Math.Abs(x);
        return double.IsFinite(size) ? Math.BitIncrement(size) - size : double.NaN;
    }

    private static double DiagonalAt(Complex[,] a, int i, int k) =>
        i < a.GetLength(0) && i + k < a.GetLength(1) ? a[i, i + k].Real : 0.0;

    private static int[] Range(int from, int count)
    {
        var r = new int[Math.Max(0, count)];
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = from + i;
        }

        return r;
    }

    private static Complex[,] Multiply(Complex[,] a, Complex[,] b) =>
        NormEstimators.Product(a, b, conjugateTranspose: false);

    private static Complex[,] ConjugateTranspose(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var t = new Complex[cols, rows];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                t[c, r] = Complex.Conjugate(a[r, c]);
            }
        }

        return t;
    }

    private static Complex[,] FullQ(Complex[,] a, int n)
    {
        if (a.GetLength(1) == 0)
        {
            var identity = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                identity[i, i] = Complex.One;
            }

            return identity;
        }

        return HouseholderQr.Factor(a, pivot: false).Q(full: true);
    }

    private static Complex[,] Columns(Complex[,] a, int count)
    {
        int rows = a.GetLength(0);
        var cut = new Complex[rows, Math.Max(0, count)];
        for (int c = 0; c < count && c < a.GetLength(1); c++)
        {
            for (int r = 0; r < rows; r++)
            {
                cut[r, c] = a[r, c];
            }
        }

        return cut;
    }

    private static Complex[,] Rows(Complex[,] a, int count)
    {
        int cols = a.GetLength(1);
        var cut = new Complex[Math.Max(0, count), cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < count && r < a.GetLength(0); r++)
            {
                cut[r, c] = a[r, c];
            }
        }

        return cut;
    }

    private static Complex[,] Skip(Complex[,] a, int from, int count)
    {
        int cols = a.GetLength(1);
        var cut = new Complex[count, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < count; r++)
            {
                cut[r, c] = a[from + r, c];
            }
        }

        return cut;
    }

    private static Complex[,] Sub(Complex[,] a, int row, int rows, int col, int cols)
    {
        var cut = new Complex[Math.Max(0, rows), Math.Max(0, cols)];
        for (int c = 0; c < cut.GetLength(1); c++)
        {
            for (int r = 0; r < cut.GetLength(0); r++)
            {
                cut[r, c] = a[row + r, col + c];
            }
        }

        return cut;
    }

    /// <summary>Zeroes everything off the main diagonal of the leading square block.</summary>
    private static void KeepDiagonal(Complex[,] a, int count)
    {
        for (int c = 0; c < count && c < a.GetLength(1); c++)
        {
            for (int r = 0; r < a.GetLength(0); r++)
            {
                if (r != c)
                {
                    a[r, c] = Complex.Zero;
                }
            }
        }
    }

    private static void ReverseColumns(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        for (int c = 0; c < cols / 2; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                (a[r, c], a[r, cols - 1 - c]) = (a[r, cols - 1 - c], a[r, c]);
            }
        }
    }

    private static void ReverseLeadingRows(Complex[,] a, int count)
    {
        int cols = a.GetLength(1);
        for (int r = 0; r < count / 2; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                (a[r, c], a[count - 1 - r, c]) = (a[count - 1 - r, c], a[r, c]);
            }
        }
    }

    private static void ReverseLeadingColumns(Complex[,] a, int count)
    {
        int rows = a.GetLength(0);
        for (int c = 0; c < count / 2; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                (a[r, c], a[r, count - 1 - c]) = (a[r, count - 1 - c], a[r, c]);
            }
        }
    }

    /// <summary>Reverses both the rows and the columns of the leading square block.</summary>
    private static void ReverseLeadingBlock(Complex[,] a, int count)
    {
        var block = new Complex[count, count];
        for (int c = 0; c < count; c++)
        {
            for (int r = 0; r < count; r++)
            {
                block[r, c] = a[count - 1 - r, count - 1 - c];
            }
        }

        for (int c = 0; c < count; c++)
        {
            for (int r = 0; r < count; r++)
            {
                a[r, c] = block[r, c];
            }
        }
    }

    /// <summary>Multiplies the given column span of a matrix by a small unitary on the right.</summary>
    private static void ApplyRight(Complex[,] a, Complex[,] turn, int from, int through)
    {
        int rows = a.GetLength(0);
        int width = through - from;
        var block = new Complex[rows, width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                block[r, c] = a[r, from + c];
            }
        }

        Complex[,] turned = Multiply(block, turn);
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                a[r, from + c] = turned[r, c];
            }
        }
    }

    private static void ApplyRightColumns(Complex[,] a, Complex[,] turn, int from, int through) =>
        ApplyRight(a, turn, from, through);

    private static void PermuteColumns(Complex[,] a, int[] order)
    {
        int rows = a.GetLength(0);
        var copy = (Complex[,])a.Clone();
        for (int c = 0; c < order.Length && c < a.GetLength(1); c++)
        {
            for (int r = 0; r < rows; r++)
            {
                a[r, c] = copy[r, order[c]];
            }
        }
    }

    private static void PermuteBoth(Complex[,] a, int[] rows, int[] cols)
    {
        var copy = (Complex[,])a.Clone();
        for (int c = 0; c < cols.Length && c < a.GetLength(1); c++)
        {
            for (int r = 0; r < rows.Length && r < a.GetLength(0); r++)
            {
                a[r, c] = copy[rows[r], cols[c]];
            }
        }
    }

    /// <summary>Permutes a run of columns starting at <paramref name="from"/> by a local order.</summary>
    private static void PermuteColumnRange(Complex[,] a, int from, int[] order)
    {
        int rows = a.GetLength(0);
        var copy = (Complex[,])a.Clone();
        for (int c = 0; c < order.Length; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                a[r, from + c] = copy[r, from + order[c]];
            }
        }
    }
}
