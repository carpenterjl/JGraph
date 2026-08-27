using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// The discrete Fourier transform over planar storage — the real parts in one span of doubles and
/// the imaginary parts in another, rather than one array of <see cref="System.Numerics.Complex"/>.
/// Three things follow from that shape, and between them they are the whole of M96a. A butterfly
/// becomes four multiplies and four adds on plain doubles, so several signals can be transformed
/// side by side in one SIMD register. A slice of a packed array can be read where it lies instead of
/// being boxed on the way in and unboxed on the way out. And a transform too large for cache can be
/// factored into two passes of shorter ones — the six-step decomposition — each of which is a batch,
/// and therefore vectorised by the same code that vectorises a batch someone asked for.
/// </summary>
/// <remarks>
/// <para>
/// The butterfly is the one the old radix-2 wrote, operand for operand: the same bit-reversal, the
/// same stage order, the same twiddle table read at the same stride, and the same
/// <c>(br·wr − bi·wi, bi·wr + br·wi)</c> spelling that <see cref="System.Numerics.Complex"/>'s own
/// multiply uses. Nothing is contracted into a fused multiply-add, and a
/// <see cref="System.Numerics.Vector{T}"/> multiply is the same IEEE multiply four at a time. So for
/// every length that takes the direct road the answer is bit-identical to the transform this
/// replaces, and the tests check that rather than assume it.
/// </para>
/// <para>
/// The factored road is a different arrangement of the same sum and therefore a different rounding —
/// the one deliberate divergence here. It is chosen by length alone, so it is the same choice on
/// every run and on every machine, and never depends on how many threads are working.
/// </para>
/// </remarks>
public static class FftKernels
{
    /// <summary>
    /// How many signals one tile carries side by side. Eight doubles is two AVX2 registers or one
    /// AVX-512 register, and a tile of eight signals of 2048 points is 256 KB of planes — a working
    /// set one core keeps in its own cache for all eleven stages.
    /// </summary>
    public const int Lanes = 8;

    /// <summary>
    /// Length above which one transform is factored into two passes of shorter ones instead of
    /// being walked stage by stage (32K points = 512 KB of planes). Below it the whole signal sits
    /// in cache and the direct road's stages cost nothing beyond their arithmetic; above it every
    /// stage is a round trip to memory, and there are log2(n) of them.
    /// </summary>
    public const int SixStepThreshold = 1 << 15;

    /// <summary>Length at or below which an awkward length is summed directly.</summary>
    private const int DirectLimit = 32;

    private static readonly ConcurrentDictionary<long, Twiddles> Tables = new();

    private static readonly ConcurrentDictionary<long, Correction> Corrections = new();

    /// <summary>True when <paramref name="n"/> is a positive power of two.</summary>
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>The smallest power of two greater than or equal to <paramref name="n"/> (at least 1).</summary>
    public static int NextPowerOfTwo(int n)
    {
        if (n <= 1)
        {
            return 1;
        }

        int p = 1;
        while (p < n)
        {
            p <<= 1;
        }

        return p;
    }

    /// <summary>
    /// Whether a transform of this length is factored rather than walked — which is also the answer
    /// to "does this length still round the way the transform before it rounded".
    /// </summary>
    public static bool IsFactored(int n) => IsPowerOfTwo(n) && n > SixStepThreshold;

    // --- the tile kernel ------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="lanes"/> transforms of length <paramref name="n"/> at once, in place, over
    /// interleaved planes: element <c>t</c> of signal <c>b</c> lives at <c>t·lanes + b</c>. The
    /// length must be a power of two, and the inverse is left unscaled — every caller either scales
    /// once at the end or is a pass of something larger that must not be scaled at all.
    /// </summary>
    public static void Stages(Span<double> re, Span<double> im, int n, int lanes, bool inverse)
    {
        if (n <= 1 || lanes <= 0)
        {
            return;
        }

        Twiddles w = TableFor(n, inverse);
        double[] wr = w.Re;
        double[] wi = w.Im;

        // The permutation the decimation-in-time tree needs, applied to whole lane blocks: the
        // signals move together because they are one transform run side by side.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                Swap(re, i * lanes, j * lanes, lanes);
                Swap(im, i * lanes, j * lanes, lanes);
            }
        }

        // The planes are walked through references rather than slices from here on. A butterfly is
        // four loads, six arithmetic operations and four stores; a bounds check on each of the eight
        // touches costs more than the arithmetic they guard, and the indices are the kernel's own —
        // derived from n and lanes, both already checked against the spans' length.
        ref double rr = ref MemoryMarshal.GetReference(re);
        ref double ri = ref MemoryMarshal.GetReference(im);
        int width = Vector<double>.Count;
        bool vector = lanes >= width && (lanes % width) == 0;

        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int stride = n / len;
            int reach = half * lanes;
            for (int i = 0; i < n; i += len)
            {
                int block = i * lanes;
                for (int k = 0; k < half; k++)
                {
                    int a = block + (k * lanes);
                    int b = a + reach;
                    double tr = wr[k * stride];
                    double ti = wi[k * stride];
                    if (vector)
                    {
                        var vtr = new Vector<double>(tr);
                        var vti = new Vector<double>(ti);
                        for (int q = 0; q < lanes; q += width)
                        {
                            var ur = Vector.LoadUnsafe(ref rr, (nuint)(a + q));
                            var ui = Vector.LoadUnsafe(ref ri, (nuint)(a + q));
                            var br = Vector.LoadUnsafe(ref rr, (nuint)(b + q));
                            var bi = Vector.LoadUnsafe(ref ri, (nuint)(b + q));
                            Vector<double> vr = (br * vtr) - (bi * vti);
                            Vector<double> vi = (bi * vtr) + (br * vti);
                            Vector.StoreUnsafe(ur + vr, ref rr, (nuint)(a + q));
                            Vector.StoreUnsafe(ui + vi, ref ri, (nuint)(a + q));
                            Vector.StoreUnsafe(ur - vr, ref rr, (nuint)(b + q));
                            Vector.StoreUnsafe(ui - vi, ref ri, (nuint)(b + q));
                        }
                    }
                    else
                    {
                        for (int q = 0; q < lanes; q++)
                        {
                            ref double ar = ref Unsafe.Add(ref rr, a + q);
                            ref double ai = ref Unsafe.Add(ref ri, a + q);
                            ref double hr = ref Unsafe.Add(ref rr, b + q);
                            ref double hi = ref Unsafe.Add(ref ri, b + q);
                            double ur = ar;
                            double ui = ai;
                            double vr = (hr * tr) - (hi * ti);
                            double vi = (hi * tr) + (hr * ti);
                            ar = ur + vr;
                            ai = ui + vi;
                            hr = ur - vr;
                            hi = ui - vi;
                        }
                    }
                }
            }
        }
    }

    private static void Swap(Span<double> plane, int a, int b, int lanes)
    {
        ref double p = ref MemoryMarshal.GetReference(plane);
        for (int q = 0; q < lanes; q++)
        {
            ref double x = ref Unsafe.Add(ref p, a + q);
            ref double y = ref Unsafe.Add(ref p, b + q);
            (x, y) = (y, x);
        }
    }

    /// <summary>
    /// One run of lanes moved whole. A tile of eight lanes is two vector registers, and the span
    /// copy this replaces cost more in call and length checking than the sixty-four bytes it moved
    /// — which mattered, because a factored transform makes one of these per element of its
    /// shorter side, twice.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyRun(ref double from, ref double to, int lanes)
    {
        int width = Vector<double>.Count;
        int q = 0;
        for (; q <= lanes - width; q += width)
        {
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref from, (nuint)q), ref to, (nuint)q);
        }

        for (; q < lanes; q++)
        {
            Unsafe.Add(ref to, q) = Unsafe.Add(ref from, q);
        }
    }

    /// <summary>
    /// A tile's lanes written out as <paramref name="lanes"/> rows of <paramref name="n"/>, each
    /// row <paramref name="stride"/> apart — the transpose a factored pass owes its successor,
    /// done while the tile is still in cache.
    /// </summary>
    private static void SpreadRows(ref double tile, ref double dst, int stride, int lanes, int n)
    {
        for (int j = 0; j < n; j++)
        {
            ref double row = ref Unsafe.Add(ref tile, j * lanes);
            ref double into = ref Unsafe.Add(ref dst, j);
            for (int l = 0; l < lanes; l++)
            {
                Unsafe.Add(ref into, l * stride) = Unsafe.Add(ref row, l);
            }
        }
    }

    /// <summary>The inverse of <see cref="SpreadRows"/>: rows gathered into a tile's lanes.</summary>
    private static void CollectRows(ref double src, ref double tile, int stride, int lanes, int n)
    {
        for (int j = 0; j < n; j++)
        {
            ref double row = ref Unsafe.Add(ref tile, j * lanes);
            ref double from = ref Unsafe.Add(ref src, j);
            for (int l = 0; l < lanes; l++)
            {
                Unsafe.Add(ref row, l) = Unsafe.Add(ref from, l * stride);
            }
        }
    }

    // --- tables ---------------------------------------------------------------------------------

    /// <summary>
    /// The n/2 distinct twiddles of one length, built once and kept. The angle is spelled the way
    /// the transform this replaces spelled it — <c>(±2.0 · π / n) · k</c>, one cosine and one sine
    /// each — because a table that rounds differently is a transform that answers differently.
    /// </summary>
    private sealed class Twiddles
    {
        public Twiddles(int n, bool inverse)
        {
            int half = n >> 1;
            Re = new double[Math.Max(half, 1)];
            Im = new double[Math.Max(half, 1)];
            double step = (inverse ? 2.0 : -2.0) * Math.PI / n;
            for (int k = 0; k < half; k++)
            {
                double angle = step * k;
                Re[k] = Math.Cos(angle);
                Im[k] = Math.Sin(angle);
            }
        }

        public double[] Re { get; }

        public double[] Im { get; }
    }

    private static Twiddles TableFor(int n, bool inverse) =>
        Tables.GetOrAdd(((long)n << 1) | (inverse ? 1L : 0L), _ => new Twiddles(n, inverse));

    /// <summary>
    /// The cross term a factored transform multiplies by between its two passes: exp(±2πi·m/n) for
    /// every m a pair of indices can make. All n of them would cost as much to store as the signal
    /// costs, so m is split as <c>q·n1 + r</c> and the factor read from two small tables and
    /// multiplied — n1 + n2 entries instead of n.
    /// </summary>
    private sealed class Correction
    {
        public Correction(int n, int n1, int n2, bool inverse)
        {
            LowRe = new double[n1];
            LowIm = new double[n1];
            HighRe = new double[n2];
            HighIm = new double[n2];
            double fine = (inverse ? 2.0 : -2.0) * Math.PI / n;
            for (int r = 0; r < n1; r++)
            {
                double angle = fine * r;
                LowRe[r] = Math.Cos(angle);
                LowIm[r] = Math.Sin(angle);
            }

            double coarse = (inverse ? 2.0 : -2.0) * Math.PI / n2;
            for (int q = 0; q < n2; q++)
            {
                double angle = coarse * q;
                HighRe[q] = Math.Cos(angle);
                HighIm[q] = Math.Sin(angle);
            }
        }

        public double[] LowRe { get; }

        public double[] LowIm { get; }

        public double[] HighRe { get; }

        public double[] HighIm { get; }
    }

    private static Correction CorrectionFor(int n, int n1, int n2, bool inverse) =>
        Corrections.GetOrAdd(
            ((long)n << 1) | (inverse ? 1L : 0L), _ => new Correction(n, n1, n2, inverse));

    // --- the factored transform -----------------------------------------------------------------

    /// <summary>
    /// One transform of a large power-of-two length, written as two passes of short ones. Reading
    /// the index as <c>t = i1 + n1·i2</c> and the answer's as <c>k = k2 + n2·k1</c> turns the sum
    /// into n1 transforms of length n2, a multiply by exp(±2πi·i1·k2/n), and n2 transforms of length
    /// n1 — and lays the second pass's input out so that its answer lands already in order.
    /// </summary>
    /// <remarks>
    /// Both passes are batches: the first reads <see cref="Lanes"/> neighbouring i1 at a time, the
    /// second that many neighbouring k2, and in each case the tile is a contiguous gather of runs.
    /// Nothing is transposed as a pass of its own; the transpose is the stride the gather already
    /// walks. The source and the destination must not overlap.
    /// </remarks>
    private static void Factored(
        NumericBuffer srcRe, NumericBuffer srcIm, int srcAt,
        NumericBuffer dstRe, NumericBuffer dstIm, int dstAt,
        int n, bool inverse, bool inside)
    {
        int p = System.Numerics.BitOperations.Log2((uint)n);
        int n2 = 1 << (p / 2);
        int n1 = n / n2;
        int lanes = Math.Min(Lanes, Math.Min(n1, n2));
        int shift = System.Numerics.BitOperations.Log2((uint)n1);
        int mask = n1 - 1;
        Correction c = CorrectionFor(n, n1, n2, inverse);

        // Every tile owns its own outputs, so a pass threads whenever the caller has no slices of
        // its own to hand out. It looked at first as though the smallest factored length — 64K
        // points, thirty-two tiles a pass — could not pay for waking the pool twice per transform,
        // and that was true while a tile still gathered its rows through span copies. Once the
        // gather became a run of vector moves the same batch of thirty-two transforms went from
        // 0.14 s to 0.095 s with threads on, so the threshold that had been put here came out.
        bool threaded = inside;

        // Pass one: an n2-point transform of every column, then the cross term, then written out
        // transposed — which costs nothing, because the write was going to be a stride either way.
        int tiles = n1 / lanes;
        ParallelKernels.ForBlocks(tiles, threaded, tile =>
        {
            int first = tile * lanes;
            double[] re = ArrayPool<double>.Shared.Rent(n2 * lanes);
            double[] im = ArrayPool<double>.Shared.Rent(n2 * lanes);
            try
            {
                Span<double> tr = re.AsSpan(0, n2 * lanes);
                Span<double> ti = im.AsSpan(0, n2 * lanes);
                ref double tre = ref MemoryMarshal.GetReference(tr);
                ref double tim = ref MemoryMarshal.GetReference(ti);
                ref double sre = ref MemoryMarshal.GetReference(srcRe.AsSpan());
                ref double sim = ref MemoryMarshal.GetReference(srcIm.AsSpan());
                for (int i2 = 0; i2 < n2; i2++)
                {
                    int from = srcAt + first + (n1 * i2);
                    CopyRun(ref Unsafe.Add(ref sre, from), ref Unsafe.Add(ref tre, i2 * lanes), lanes);
                    CopyRun(ref Unsafe.Add(ref sim, from), ref Unsafe.Add(ref tim, i2 * lanes), lanes);
                }

                Stages(tr, ti, n2, lanes, inverse);

                for (int k2 = 1; k2 < n2; k2++)
                {
                    int at = k2 * lanes;
                    for (int l = 0; l < lanes; l++)
                    {
                        // n1 is a power of two, so the split of m is a shift and a mask. It was a
                        // division and a remainder first, and that cost more than the transform
                        // this correction sits between: one integer division per element.
                        int m = (first + l) * k2;
                        int q = m >> shift;
                        int r = m & mask;
                        double fr = (c.LowRe[r] * c.HighRe[q]) - (c.LowIm[r] * c.HighIm[q]);
                        double fi = (c.LowIm[r] * c.HighRe[q]) + (c.LowRe[r] * c.HighIm[q]);
                        ref double xr = ref Unsafe.Add(ref tre, at + l);
                        ref double xi = ref Unsafe.Add(ref tim, at + l);
                        double vr = xr;
                        double vi = xi;
                        xr = (vr * fr) - (vi * fi);
                        xi = (vi * fr) + (vr * fi);
                    }
                }

                int row = dstAt + (first * n2);
                SpreadRows(
                    ref tre, ref Unsafe.Add(ref MemoryMarshal.GetReference(dstRe.AsSpan()), row),
                    n2, lanes, n2);
                SpreadRows(
                    ref tim, ref Unsafe.Add(ref MemoryMarshal.GetReference(dstIm.AsSpan()), row),
                    n2, lanes, n2);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(re);
                ArrayPool<double>.Shared.Return(im);
            }
        });

        // Pass two: an n1-point transform down each of those rows. The layout pass one wrote is
        // already the interleaved one this needs, and the answer lands where it belongs.
        int rows = n2 / lanes;
        ParallelKernels.ForBlocks(rows, threaded, tile =>
        {
            int first = tile * lanes;
            double[] re = ArrayPool<double>.Shared.Rent(n1 * lanes);
            double[] im = ArrayPool<double>.Shared.Rent(n1 * lanes);
            try
            {
                Span<double> tr = re.AsSpan(0, n1 * lanes);
                Span<double> ti = im.AsSpan(0, n1 * lanes);
                ref double tre = ref MemoryMarshal.GetReference(tr);
                ref double tim = ref MemoryMarshal.GetReference(ti);
                ref double dre = ref MemoryMarshal.GetReference(dstRe.AsSpan());
                ref double dim = ref MemoryMarshal.GetReference(dstIm.AsSpan());
                for (int i1 = 0; i1 < n1; i1++)
                {
                    int from = dstAt + (i1 * n2) + first;
                    CopyRun(ref Unsafe.Add(ref dre, from), ref Unsafe.Add(ref tre, i1 * lanes), lanes);
                    CopyRun(ref Unsafe.Add(ref dim, from), ref Unsafe.Add(ref tim, i1 * lanes), lanes);
                }

                Stages(tr, ti, n1, lanes, inverse);

                for (int i1 = 0; i1 < n1; i1++)
                {
                    int to = dstAt + (i1 * n2) + first;
                    CopyRun(ref Unsafe.Add(ref tre, i1 * lanes), ref Unsafe.Add(ref dre, to), lanes);
                    CopyRun(ref Unsafe.Add(ref tim, i1 * lanes), ref Unsafe.Add(ref dim, to), lanes);
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(re);
                ArrayPool<double>.Shared.Return(im);
            }
        });

        GC.KeepAlive(srcRe);
        GC.KeepAlive(srcIm);
        GC.KeepAlive(dstRe);
        GC.KeepAlive(dstIm);
    }

    // --- awkward lengths ------------------------------------------------------------------------

    /// <summary>
    /// Bluestein's chirp-z transform: a length that is not a power of two written as a circular
    /// convolution of length 2n−1, padded up to one that is. The chirp exponent is reduced modulo
    /// 2n in whole numbers so the phases stay accurate however long the signal is.
    /// </summary>
    private static void Bluestein(Span<double> re, Span<double> im, int n, bool inverse)
    {
        double sign = inverse ? 1.0 : -1.0;
        int m = NextPowerOfTwo((2 * n) - 1);
        var pool = ArrayPool<double>.Shared;
        double[] chirpRe = pool.Rent(n);
        double[] chirpIm = pool.Rent(n);
        double[] ar = pool.Rent(m);
        double[] ai = pool.Rent(m);
        double[] br = pool.Rent(m);
        double[] bi = pool.Rent(m);
        try
        {
            Array.Clear(ar, 0, m);
            Array.Clear(ai, 0, m);
            Array.Clear(br, 0, m);
            Array.Clear(bi, 0, m);
            long modulus = 2L * n;
            for (int j = 0; j < n; j++)
            {
                long j2 = (long)j * j % modulus;
                double angle = sign * Math.PI * j2 / n;
                chirpRe[j] = Math.Cos(angle);
                chirpIm[j] = Math.Sin(angle);
            }

            for (int j = 0; j < n; j++)
            {
                ar[j] = (re[j] * chirpRe[j]) - (im[j] * chirpIm[j]);
                ai[j] = (im[j] * chirpRe[j]) + (re[j] * chirpIm[j]);
            }

            br[0] = chirpRe[0];
            bi[0] = -chirpIm[0];
            for (int j = 1; j < n; j++)
            {
                br[j] = br[m - j] = chirpRe[j];
                bi[j] = bi[m - j] = -chirpIm[j];
            }

            PowerOfTwo(ar, ai, m, inverse: false);
            PowerOfTwo(br, bi, m, inverse: false);
            for (int j = 0; j < m; j++)
            {
                double xr = ar[j];
                double xi = ai[j];
                ar[j] = (xr * br[j]) - (xi * bi[j]);
                ai[j] = (xi * br[j]) + (xr * bi[j]);
            }

            PowerOfTwo(ar, ai, m, inverse: true);
            for (int k = 0; k < n; k++)
            {
                // The /m completes the unscaled inverse above; then the chirp comes back off.
                double xr = ar[k] / m;
                double xi = ai[k] / m;
                re[k] = (xr * chirpRe[k]) - (xi * chirpIm[k]);
                im[k] = (xi * chirpRe[k]) + (xr * chirpIm[k]);
            }
        }
        finally
        {
            pool.Return(chirpRe);
            pool.Return(chirpIm);
            pool.Return(ar);
            pool.Return(ai);
            pool.Return(br);
            pool.Return(bi);
        }
    }

    /// <summary>The sum written out, which at a handful of points is cheaper than anything clever.</summary>
    private static void Direct(Span<double> re, Span<double> im, int n, bool inverse)
    {
        var pool = ArrayPool<double>.Shared;
        double[] outRe = pool.Rent(n);
        double[] outIm = pool.Rent(n);
        try
        {
            double sign = inverse ? 1.0 : -1.0;
            double baseAngle = sign * 2.0 * Math.PI / n;
            for (int k = 0; k < n; k++)
            {
                double sr = 0;
                double si = 0;
                for (int t = 0; t < n; t++)
                {
                    double angle = baseAngle * k * t;
                    double wr = Math.Cos(angle);
                    double wi = Math.Sin(angle);
                    sr += (re[t] * wr) - (im[t] * wi);
                    si += (im[t] * wr) + (re[t] * wi);
                }

                outRe[k] = sr;
                outIm[k] = si;
            }

            outRe.AsSpan(0, n).CopyTo(re);
            outIm.AsSpan(0, n).CopyTo(im);
        }
        finally
        {
            pool.Return(outRe);
            pool.Return(outIm);
        }
    }

    /// <summary>
    /// A power-of-two length on plain arrays: walked when it fits in cache, factored when it does
    /// not. Serial — a caller that can afford threads asks for them through the buffer overload.
    /// </summary>
    private static void PowerOfTwo(double[] re, double[] im, int n, bool inverse)
    {
        if (n <= SixStepThreshold)
        {
            Stages(re.AsSpan(0, n), im.AsSpan(0, n), n, 1, inverse);
            return;
        }

        var pool = ArrayPool<double>.Shared;
        double[] dr = pool.Rent(n);
        double[] di = pool.Rent(n);
        try
        {
            Factored(
                ManagedBuffer.Adopt(re), ManagedBuffer.Adopt(im), 0,
                ManagedBuffer.Adopt(dr), ManagedBuffer.Adopt(di), 0,
                n, inverse, inside: false);
            dr.AsSpan(0, n).CopyTo(re);
            di.AsSpan(0, n).CopyTo(im);
        }
        finally
        {
            pool.Return(dr);
            pool.Return(di);
        }
    }

    // --- what callers use -----------------------------------------------------------------------

    /// <summary>
    /// One transform of length <paramref name="n"/>, read from one pair of planes and written to
    /// another. The two may be the same buffers at the same offset, in which case the transform is
    /// in place. <paramref name="inside"/> lets a caller with nothing else to hand the machine
    /// spend threads within this one transform.
    /// </summary>
    public static void Transform(
        NumericBuffer srcRe, NumericBuffer srcIm, int srcAt,
        NumericBuffer dstRe, NumericBuffer dstIm, int dstAt,
        int n, bool inverse, bool inside)
    {
        if (n <= 0)
        {
            return;
        }

        bool separate = !ReferenceEquals(srcRe, dstRe) || !ReferenceEquals(srcIm, dstIm)
            || srcAt != dstAt;
        if (IsFactored(n) && separate)
        {
            Factored(srcRe, srcIm, srcAt, dstRe, dstIm, dstAt, n, inverse, inside);
            Scale(dstRe, dstIm, dstAt, n, inverse);
            return;
        }

        if (separate)
        {
            srcRe.AsSpan(srcAt, n).CopyTo(dstRe.AsSpan(dstAt, n));
            srcIm.AsSpan(srcAt, n).CopyTo(dstIm.AsSpan(dstAt, n));
        }

        if (n > 1)
        {
            if (IsFactored(n))
            {
                var pool = ArrayPool<double>.Shared;
                double[] tr = pool.Rent(n);
                double[] ti = pool.Rent(n);
                try
                {
                    Factored(
                        dstRe, dstIm, dstAt,
                        ManagedBuffer.Adopt(tr), ManagedBuffer.Adopt(ti), 0,
                        n, inverse, inside);
                    tr.AsSpan(0, n).CopyTo(dstRe.AsSpan(dstAt, n));
                    ti.AsSpan(0, n).CopyTo(dstIm.AsSpan(dstAt, n));
                }
                finally
                {
                    pool.Return(tr);
                    pool.Return(ti);
                }
            }
            else if (IsPowerOfTwo(n))
            {
                Stages(dstRe.AsSpan(dstAt, n), dstIm.AsSpan(dstAt, n), n, 1, inverse);
            }
            else if (n <= DirectLimit)
            {
                Direct(dstRe.AsSpan(dstAt, n), dstIm.AsSpan(dstAt, n), n, inverse);
            }
            else
            {
                Bluestein(dstRe.AsSpan(dstAt, n), dstIm.AsSpan(dstAt, n), n, inverse);
            }
        }

        Scale(dstRe, dstIm, dstAt, n, inverse);
        GC.KeepAlive(srcRe);
        GC.KeepAlive(srcIm);
        GC.KeepAlive(dstRe);
        GC.KeepAlive(dstIm);
    }

    /// <summary>
    /// One transform of length <paramref name="n"/> in place over plain spans — the entry the boxed
    /// signal code uses, and the one a test can call without a buffer in sight.
    /// </summary>
    public static void Transform(Span<double> re, Span<double> im, int n, bool inverse)
    {
        if (n <= 0)
        {
            return;
        }

        if (n > 1)
        {
            if (IsPowerOfTwo(n))
            {
                if (IsFactored(n))
                {
                    var pool = ArrayPool<double>.Shared;
                    double[] sr = pool.Rent(n);
                    double[] si = pool.Rent(n);
                    try
                    {
                        re[..n].CopyTo(sr);
                        im[..n].CopyTo(si);
                        PowerOfTwo(sr, si, n, inverse);
                        sr.AsSpan(0, n).CopyTo(re);
                        si.AsSpan(0, n).CopyTo(im);
                    }
                    finally
                    {
                        pool.Return(sr);
                        pool.Return(si);
                    }
                }
                else
                {
                    Stages(re[..n], im[..n], n, 1, inverse);
                }
            }
            else if (n <= DirectLimit)
            {
                Direct(re, im, n, inverse);
            }
            else
            {
                Bluestein(re, im, n, inverse);
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
        }
    }

    /// <summary>
    /// <paramref name="lanes"/> transforms of length <paramref name="n"/> side by side over
    /// interleaved planes, scaled the way one transform would be. Only for lengths the direct road
    /// takes; a batch of factored transforms is a batch of one.
    /// </summary>
    public static void TransformBatch(
        Span<double> re, Span<double> im, int n, int lanes, bool inverse)
    {
        Stages(re, im, n, lanes, inverse);
        if (!inverse || n <= 1)
        {
            return;
        }

        int total = n * lanes;
        for (int i = 0; i < total; i++)
        {
            re[i] /= n;
            im[i] /= n;
        }
    }

    // --- one transform per slice of a packed array ----------------------------------------------

    /// <summary>
    /// A transform of every slice along one dimension, read from packed column-major storage and
    /// written to packed column-major storage: <paramref name="split"/> describes the source's
    /// geometry and <paramref name="n"/> the length each slice is padded or cut to, which is the
    /// length the answer's slices have. A null <paramref name="srcIm"/> says the input is real.
    /// </summary>
    /// <remarks>
    /// Two roads, chosen by length. A length the direct kernel takes is transformed
    /// <see cref="Lanes"/> slices at a time, which is what makes the butterflies vector work; a
    /// length that has to be factored, or one that is not a power of two, is transformed a slice at
    /// a time. Both roads gather a slice into a contiguous tile first, because a slice of a matrix
    /// steps by the row count and an FFT that reads through that stride pays for it log2(n) times.
    /// </remarks>
    public static void TransformAlong(
        NumericBuffer srcRe, NumericBuffer? srcIm,
        NumericBuffer dstRe, NumericBuffer dstIm,
        ReduceKernels.Split split, int n, bool inverse, bool symmetric)
    {
        int slices = split.Slices;
        if (slices <= 0 || n <= 0)
        {
            return;
        }

        if (IsPowerOfTwo(n) && !IsFactored(n) && slices > 1)
        {
            Batched(srcRe, srcIm, dstRe, dstIm, split, n, inverse, symmetric);
        }
        else
        {
            Singly(srcRe, srcIm, dstRe, dstIm, split, n, inverse, symmetric);
        }

        GC.KeepAlive(srcRe);
        GC.KeepAlive(srcIm);
        GC.KeepAlive(dstRe);
        GC.KeepAlive(dstIm);
    }

    /// <summary>Where slice <paramref name="s"/>'s first element sits in storage cut by
    /// <paramref name="inner"/> and <paramref name="count"/>.</summary>
    private static int Base(int s, int inner, int count) =>
        ((s / inner) * inner * count) + (s % inner);

    private static void Batched(
        NumericBuffer srcRe, NumericBuffer? srcIm,
        NumericBuffer dstRe, NumericBuffer dstIm,
        ReduceKernels.Split split, int n, bool inverse, bool symmetric)
    {
        int slices = split.Slices;
        int inner = split.Inner;
        int count = split.Count;
        int copy = Math.Min(n, count);
        int groups = ((slices - 1) / Lanes) + 1;
        bool threaded = (long)slices * n >= ParallelKernels.MemoryBoundThreshold;

        ParallelKernels.ForBlocks(groups, threaded, g =>
        {
            int first = g * Lanes;
            int lanes = Math.Min(Lanes, slices - first);
            double[] rented = ArrayPool<double>.Shared.Rent(2 * n * lanes);
            try
            {
                Span<double> tr = rented.AsSpan(0, n * lanes);
                Span<double> ti = rented.AsSpan(n * lanes, n * lanes);

                // Only what the gather will not reach has to be zeroed: the padding past the
                // slice's own length, and the whole imaginary plane when the input is real.
                tr[(copy * lanes)..].Clear();
                if (srcIm is null)
                {
                    ti.Clear();
                }
                else
                {
                    ti[(copy * lanes)..].Clear();
                }

                ref double tre = ref MemoryMarshal.GetReference(tr);
                ref double tim = ref MemoryMarshal.GetReference(ti);
                ref double sre = ref MemoryMarshal.GetReference(srcRe.AsSpan());
                ref double sim = ref (srcIm is null
                    ? ref Unsafe.NullRef<double>()
                    : ref MemoryMarshal.GetReference(srcIm.AsSpan()));

                // Neighbouring slices of a strided layout are neighbouring elements, so a whole
                // group's j-th element is one contiguous run and the gather is a copy. A
                // contiguous layout — a matrix transformed down its columns — is a transpose
                // instead, and a group of one is neither.
                bool run = (first % inner) + lanes <= inner;
                if (run)
                {
                    int at = Base(first, inner, count);
                    for (int j = 0; j < copy; j++)
                    {
                        int from = at + (j * inner);
                        CopyRun(ref Unsafe.Add(ref sre, from), ref Unsafe.Add(ref tre, j * lanes), lanes);
                        if (srcIm is not null)
                        {
                            CopyRun(ref Unsafe.Add(ref sim, from), ref Unsafe.Add(ref tim, j * lanes), lanes);
                        }
                    }
                }
                else if (inner == 1)
                {
                    int at = first * count;
                    CollectRows(ref Unsafe.Add(ref sre, at), ref tre, count, lanes, copy);
                    if (srcIm is not null)
                    {
                        CollectRows(ref Unsafe.Add(ref sim, at), ref tim, count, lanes, copy);
                    }
                }
                else
                {
                    for (int l = 0; l < lanes; l++)
                    {
                        int lane = Base(first + l, inner, count);
                        for (int j = 0; j < copy; j++)
                        {
                            Unsafe.Add(ref tre, (j * lanes) + l) = Unsafe.Add(ref sre, lane + (j * inner));
                            if (srcIm is not null)
                            {
                                Unsafe.Add(ref tim, (j * lanes) + l) = Unsafe.Add(ref sim, lane + (j * inner));
                            }
                        }
                    }
                }

                if (symmetric)
                {
                    for (int l = 0; l < lanes; l++)
                    {
                        Hermitian(tr, ti, n, lanes, l);
                    }
                }

                Stages(tr, ti, n, lanes, inverse);
                if (inverse && n > 1)
                {
                    int total = n * lanes;
                    for (int i = 0; i < total; i++)
                    {
                        tr[i] /= n;
                        ti[i] /= n;
                    }
                }

                if (symmetric)
                {
                    ti.Clear();
                }

                ref double dre = ref MemoryMarshal.GetReference(dstRe.AsSpan());
                ref double dim = ref MemoryMarshal.GetReference(dstIm.AsSpan());
                if (run)
                {
                    int to = Base(first, inner, n);
                    for (int j = 0; j < n; j++)
                    {
                        int into = to + (j * inner);
                        CopyRun(ref Unsafe.Add(ref tre, j * lanes), ref Unsafe.Add(ref dre, into), lanes);
                        CopyRun(ref Unsafe.Add(ref tim, j * lanes), ref Unsafe.Add(ref dim, into), lanes);
                    }
                }
                else if (inner == 1)
                {
                    int to = first * n;
                    SpreadRows(ref tre, ref Unsafe.Add(ref dre, to), n, lanes, n);
                    SpreadRows(ref tim, ref Unsafe.Add(ref dim, to), n, lanes, n);
                }
                else
                {
                    for (int l = 0; l < lanes; l++)
                    {
                        int lane = Base(first + l, inner, n);
                        for (int j = 0; j < n; j++)
                        {
                            Unsafe.Add(ref dre, lane + (j * inner)) = Unsafe.Add(ref tre, (j * lanes) + l);
                            Unsafe.Add(ref dim, lane + (j * inner)) = Unsafe.Add(ref tim, (j * lanes) + l);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(rented);
            }
        });
    }

    private static void Singly(
        NumericBuffer srcRe, NumericBuffer? srcIm,
        NumericBuffer dstRe, NumericBuffer dstIm,
        ReduceKernels.Split split, int n, bool inverse, bool symmetric)
    {
        int slices = split.Slices;
        int inner = split.Inner;
        int count = split.Count;
        int copy = Math.Min(n, count);

        // Threads go across slices when there are enough of them to fill the machine, and inside
        // one transform when there are not — the same choice the sort makes, for the same reason.
        bool overSlices = slices >= ParallelKernels.MaxDegree;
        ParallelKernels.ForBlocks(slices, overSlices, s =>
        {
            double[] rentedRe = ArrayPool<double>.Shared.Rent(n);
            double[] rentedIm = ArrayPool<double>.Shared.Rent(n);
            try
            {
                Span<double> tr = rentedRe.AsSpan(0, n);
                Span<double> ti = rentedIm.AsSpan(0, n);
                tr[copy..].Clear();
                if (srcIm is null)
                {
                    ti.Clear();
                }
                else
                {
                    ti[copy..].Clear();
                }

                Span<double> sr = srcRe.AsSpan();
                int at = Base(s, inner, count);
                if (inner == 1)
                {
                    sr.Slice(at, copy).CopyTo(tr);
                    if (srcIm is not null)
                    {
                        srcIm.AsSpan(at, copy).CopyTo(ti);
                    }
                }
                else
                {
                    Span<double> si = srcIm is null ? default : srcIm.AsSpan();
                    for (int j = 0; j < copy; j++)
                    {
                        tr[j] = sr[at + (j * inner)];
                        if (srcIm is not null)
                        {
                            ti[j] = si[at + (j * inner)];
                        }
                    }
                }

                if (symmetric)
                {
                    Hermitian(tr, ti, n, 1, 0);
                }

                var hostRe = ManagedBuffer.Adopt(rentedRe);
                var hostIm = ManagedBuffer.Adopt(rentedIm);
                int to = Base(s, inner, n);
                if (inner == 1)
                {
                    // The answer's slice is a contiguous run of the answer, so a factored transform
                    // writes straight into it and nothing is copied twice.
                    Transform(hostRe, hostIm, 0, dstRe, dstIm, to, n, inverse, !overSlices);
                    if (symmetric)
                    {
                        dstIm.AsSpan(to, n).Clear();
                    }

                    return;
                }

                Transform(hostRe, hostIm, 0, hostRe, hostIm, 0, n, inverse, !overSlices);
                Span<double> dr = dstRe.AsSpan();
                Span<double> di = dstIm.AsSpan();
                for (int j = 0; j < n; j++)
                {
                    dr[to + (j * inner)] = tr[j];
                    di[to + (j * inner)] = symmetric ? 0 : ti[j];
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(rentedRe);
                ArrayPool<double>.Shared.Return(rentedIm);
            }
        });
    }

    /// <summary>
    /// Forces one lane of a tile to be conjugate-symmetric, which is what <c>'symmetric'</c>
    /// asserts about a spectrum whose inverse is meant to come back real.
    /// </summary>
    private static void Hermitian(Span<double> re, Span<double> im, int n, int lanes, int lane)
    {
        im[lane] = 0;
        if (n % 2 == 0)
        {
            im[((n / 2) * lanes) + lane] = 0;
        }

        for (int i = 1; i < (n + 1) / 2; i++)
        {
            re[((n - i) * lanes) + lane] = re[(i * lanes) + lane];
            im[((n - i) * lanes) + lane] = -im[(i * lanes) + lane];
        }
    }

    private static void Scale(NumericBuffer re, NumericBuffer im, int at, int n, bool inverse)
    {
        if (!inverse || n <= 1)
        {
            return;
        }

        Span<double> r = re.AsSpan(at, n);
        Span<double> i = im.AsSpan(at, n);
        for (int k = 0; k < n; k++)
        {
            r[k] /= n;
            i[k] /= n;
        }
    }
}
