namespace JGraph.Numerics;

/// <summary>
/// Whether a spectrum is exactly conjugate-symmetric, which is the test R2025b's <c>ifft</c>,
/// <c>ifft2</c> and <c>ifftn</c> make before transforming: when it holds, the inverse is real and the
/// answer is handed back as a real array, as though <c>'symmetric'</c> had been written (the
/// every-row plan's I11, ADR 0323).
/// </summary>
/// <remarks>
/// <para>
/// The test is exact, element against element: <c>X(k) == conj(X(n - k))</c> with indices taken
/// modulo the transform length, and a zero imaginary part where an element is its own mirror (the
/// first element, and the middle one of an even length). A NaN is never equal to itself, so a
/// spectrum holding one is not symmetric. The slice tested is the one the transform reads: padded
/// with zeros or cut to the transform length first.
/// </para>
/// <para>
/// A spectrum that fails usually fails on its first pair, so the cost of the test on an input that
/// is not symmetric is a few reads; on one that is, it is one pass over the input, against the
/// complex arithmetic every later operation would otherwise pay on a complex answer.
/// </para>
/// </remarks>
public static class ConjugateSymmetry
{
    /// <summary>
    /// Every slice cut by <paramref name="split"/>, padded or cut to <paramref name="n"/>, is its own
    /// conjugate reflection. An empty <paramref name="im"/> says the input is real.
    /// </summary>
    public static bool Along(
        ReadOnlySpan<double> re, ReadOnlySpan<double> im, ReduceKernels.Split split, int n)
    {
        if (n <= 1)
        {
            // A slice of one element is its own mirror; only its imaginary part can spoil it.
            return n < 1 || im.IsEmpty || split.Count < 1 || AllZeroFirsts(im, split);
        }

        bool complex = !im.IsEmpty;
        int inner = split.Inner;
        int count = split.Count;
        int copy = Math.Min(n, count);
        if (copy == 0)
        {
            return true; // every slice is all padding, and zeros are symmetric
        }

        int slices = split.Slices;
        if (slices == 1 && inner == 1 && copy == n && n >= ParallelKernels.MemoryBoundThreshold)
        {
            return Contiguous(re.Slice(0, n), complex ? im.Slice(0, n) : default);
        }

        for (int s = 0; s < slices; s++)
        {
            int at = ((s / inner) * inner * count) + (s % inner);
            if (complex && im[at] != 0)
            {
                return false;
            }

            if (inner == 1 && copy == n)
            {
                // The common case — a vector or the columns of a matrix, neither padded nor cut —
                // reads both ends of one contiguous run.
                ReadOnlySpan<double> r = re.Slice(at, n);
                for (int j = 1, k = n - 1; j <= k; j++, k--)
                {
                    if (r[j] != r[k])
                    {
                        return false;
                    }
                }

                if (complex)
                {
                    ReadOnlySpan<double> i = im.Slice(at, n);
                    for (int j = 1, k = n - 1; j <= k; j++, k--)
                    {
                        if (i[j] != -i[k])
                        {
                            return false;
                        }
                    }
                }

                continue;
            }

            for (int j = 1, k = n - 1; j <= k; j++, k--)
            {
                double ar = 0, ai = 0, br = 0, bi = 0;
                if (j < copy)
                {
                    ar = re[at + (j * inner)];
                    ai = complex ? im[at + (j * inner)] : 0;
                }

                if (k < copy)
                {
                    br = re[at + (k * inner)];
                    bi = complex ? im[at + (k * inner)] : 0;
                }

                if (ar != br || ai != -bi)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The whole array, resized to <paramref name="sizes"/>, equals its conjugate reflected through
    /// the origin along every dimension <paramref name="mirrored"/> marks — the symmetry of a
    /// multidimensional spectrum whose inverse is real. <paramref name="dims"/> is the stored shape
    /// and <paramref name="sizes"/> the transform's, both padded with ones to the same length; a
    /// dimension that is not mirrored keeps its stored size.
    /// </summary>
    public static bool Across(
        ReadOnlySpan<double> re, ReadOnlySpan<double> im,
        IReadOnlyList<int> dims, IReadOnlyList<int> sizes, IReadOnlyList<bool> mirrored)
    {
        int rank = sizes.Count;
        long total = 1;
        for (int d = 0; d < rank; d++)
        {
            total *= sizes[d];
        }

        if (total == 0)
        {
            return true;
        }

        bool complex = !im.IsEmpty;
        bool resized = false;
        for (int d = 0; d < rank; d++)
        {
            resized |= sizes[d] != dims[d];
        }

        if (!resized && mirrored[0])
        {
            // Unpadded: a column against its mirror column, read from both ends.
            int n1 = dims[0];
            int[] mirror = ColumnMirrors(dims, mirrored);
            for (int j = 0; j < mirror.Length; j++)
            {
                int a = j * n1;
                int b = mirror[j] * n1;
                if (re[a] != re[b] || (complex && im[a] != -im[b]))
                {
                    return false;
                }

                for (int i = 1; i < n1; i++)
                {
                    if (re[a + i] != re[b + n1 - i] || (complex && im[a + i] != -im[b + n1 - i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        var stride = new long[rank];
        long step = 1;
        for (int d = 0; d < rank; d++)
        {
            stride[d] = step;
            step *= dims[d];
        }

        var coord = new int[rank];
        for (long at = 0; at < total; at++)
        {
            bool hasA = true;
            bool hasB = true;
            long a = 0;
            long b = 0;
            for (int d = 0; d < rank; d++)
            {
                int c = coord[d];
                int m = mirrored[d] && c != 0 ? sizes[d] - c : c;
                if (c < dims[d])
                {
                    a += c * stride[d];
                }
                else
                {
                    hasA = false;
                }

                if (m < dims[d])
                {
                    b += m * stride[d];
                }
                else
                {
                    hasB = false;
                }
            }

            double ar = hasA ? re[(int)a] : 0;
            double ai = hasA && complex ? im[(int)a] : 0;
            double br = hasB ? re[(int)b] : 0;
            double bi = hasB && complex ? im[(int)b] : 0;
            if (ar != br || ai != -bi)
            {
                return false;
            }

            for (int d = 0; d < rank; d++)
            {
                if (++coord[d] < sizes[d])
                {
                    break;
                }

                coord[d] = 0;
            }
        }

        return true;
    }

    /// <summary>
    /// Makes a multidimensional spectrum exactly conjugate-symmetric through the origin of the
    /// dimensions <paramref name="mirrored"/> marks (the first always among them), as R2025b's
    /// <c>fft2</c> and <c>fftn</c> of a real input are: each element and its mirror become their
    /// symmetric part, and an element that is its own mirror loses its imaginary part. What is
    /// dropped is rounding alone.
    /// </summary>
    public static void Force(NumericBuffer re, NumericBuffer im, IReadOnlyList<int> dims, IReadOnlyList<bool> mirrored)
    {
        long total = 1;
        foreach (int size in dims)
        {
            total *= size;
        }

        if (total == 0)
        {
            return;
        }

        int n1 = dims[0];
        int[] mirror = ColumnMirrors(dims, mirrored);
        int columns = mirror.Length;
        int blocks = Math.Max(1, Math.Min(columns, ParallelKernels.MaxDegree * 4));
        bool threaded = total >= ParallelKernels.MemoryBoundThreshold && blocks > 1;
        ParallelKernels.ForBlocks(blocks, threaded, block =>
        {
            Span<double> r = re.AsSpan();
            Span<double> m = im.AsSpan();
            int first = (int)((long)columns * block / blocks);
            int last = (int)((long)columns * (block + 1) / blocks);
            for (int j = first; j < last; j++)
            {
                // Column j owns its rows above the middle and their partners below it in the
                // mirror column, so no two blocks write or read the same element.
                int a = j * n1;
                int b = mirror[j] * n1;
                for (int i = 1; i < (n1 + 1) / 2; i++)
                {
                    Pair(r, m, a + i, b + n1 - i);
                }

                SelfRow(r, m, a, b, j, mirror[j], 0);
                if (n1 % 2 == 0 && n1 > 1)
                {
                    SelfRow(r, m, a, b, j, mirror[j], n1 / 2);
                }
            }
        });

        GC.KeepAlive(re);
        GC.KeepAlive(im);
    }

    private static void SelfRow(Span<double> r, Span<double> m, int a, int b, int j, int mj, int row)
    {
        if (j < mj)
        {
            Pair(r, m, a + row, b + row);
        }
        else if (j == mj)
        {
            m[a + row] = 0;
        }
    }

    /// <summary>A pair replaced by its symmetric part, the arithmetic of <see cref="FftKernels.Symmetrize(Span{double}, Span{double}, int, int, int)"/>.</summary>
    private static void Pair(Span<double> r, Span<double> m, int a, int b)
    {
        double re = (r[a] + r[b]) * 0.5;
        double im = (m[a] - m[b]) * 0.5;
        r[a] = re;
        r[b] = re;
        m[a] = im;
        m[b] = -im;
    }

    /// <summary>
    /// For each column (every index past the first dimension, in storage order), the column it
    /// mirrors to: each mirrored dimension's index reflected, the others kept.
    /// </summary>
    private static int[] ColumnMirrors(IReadOnlyList<int> dims, IReadOnlyList<bool> mirrored)
    {
        int rank = dims.Count;
        long columns = 1;
        for (int d = 1; d < rank; d++)
        {
            columns *= dims[d];
        }

        var mirror = new int[columns];
        var coord = new int[rank];
        var stride = new int[rank];
        int step = 1;
        for (int d = 1; d < rank; d++)
        {
            stride[d] = step;
            step *= dims[d];
        }

        for (int j = 0; j < columns; j++)
        {
            int to = 0;
            for (int d = 1; d < rank; d++)
            {
                int c = coord[d];
                to += (mirrored[d] && c != 0 ? dims[d] - c : c) * stride[d];
            }

            mirror[j] = to;
            for (int d = 1; d < rank; d++)
            {
                if (++coord[d] < dims[d])
                {
                    break;
                }

                coord[d] = 0;
            }
        }

        return mirror;
    }

    /// <summary>
    /// One long contiguous slice, its pairs cut across the cores (d02's four million samples): the
    /// first pairs are read here first, since a spectrum that is not symmetric fails on them and
    /// should not pay for the threads.
    /// </summary>
    private static unsafe bool Contiguous(ReadOnlySpan<double> re, ReadOnlySpan<double> im)
    {
        int n = re.Length;
        bool complex = !im.IsEmpty;
        if (complex && im[0] != 0)
        {
            return false;
        }

        int probe = Math.Min(64, n / 2);
        for (int j = 1; j <= probe; j++)
        {
            if (re[j] != re[n - j] || (complex && im[j] != -im[n - j]))
            {
                return false;
            }
        }

        int failed = 0;
        fixed (double* pr = re)
        fixed (double* pi = im)
        {
            nint r = (nint)pr;
            nint m = (nint)pi;
            ParallelKernels.For(n / 2, ParallelKernels.MemoryBoundThreshold, null, (start, len) =>
            {
                if (Volatile.Read(ref failed) != 0)
                {
                    return;
                }

                var rs = new ReadOnlySpan<double>((void*)r, n);
                var ms = complex ? new ReadOnlySpan<double>((void*)m, n) : default;
                for (int j = start + 1; j <= start + len; j++)
                {
                    int k = n - j;
                    if (rs[j] != rs[k] || (complex && ms[j] != -ms[k]))
                    {
                        Interlocked.Exchange(ref failed, 1);
                        return;
                    }
                }
            });
        }

        return failed == 0;
    }

    private static bool AllZeroFirsts(ReadOnlySpan<double> im, ReduceKernels.Split split)
    {
        int inner = split.Inner;
        int count = split.Count;
        for (int s = 0; s < split.Slices; s++)
        {
            if (im[((s / inner) * inner * count) + (s % inner)] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
