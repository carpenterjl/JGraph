using System.Buffers;

namespace JGraph.Numerics;

/// <summary>
/// The Stockham autosort transform for a length whose prime factors are 2, 3 and 5 — the road
/// between a power of two, which <see cref="FftKernels"/> walks in stages or factors, and an
/// awkward length, which it hands to Bluestein at three times the padded cost (ADR 0154).
/// </summary>
/// <remarks>
/// <para>
/// One pass per radix. A pass of radix r over a sub-length n′ (the length not yet reduced) with
/// stride s (the product of the radices already applied) reads, for every p below n′/r and every
/// q′ below s, the r points <c>x[q′ + s·(p + q·n′/r)]</c>, takes their r-point transform, turns
/// output q by the twiddle <c>e^{∓2πi·pq/n′}</c>, and writes it to <c>y[q′ + s·(r·p + q)]</c>.
/// After the last pass the answer is in natural order — the sort is in the write addresses, so
/// there is no bit reversal — and each pass reads one pair of planes and writes the other.
/// </para>
/// <para>
/// The twiddles are a plan: one table per pass, computed once per length and direction with the
/// angle reduced modulo n′ before the cosine is taken, held in the same cache as Bluestein's plans.
/// The pass's work items are independent — every (p, q′) owns its r outputs — so a threaded pass
/// and a serial one perform the same operations on the same numbers and answer the same bits.
/// </para>
/// </remarks>
internal static class MixedRadixFft
{
    private const double Cos3 = -0.5;
    private static readonly double Sin3 = Math.Sqrt(3.0) / 2.0;
    private static readonly double Cos5A = Math.Cos(2.0 * Math.PI / 5.0);
    private static readonly double Cos5B = Math.Cos(4.0 * Math.PI / 5.0);
    private static readonly double Sin5A = Math.Sin(2.0 * Math.PI / 5.0);
    private static readonly double Sin5B = Math.Sin(4.0 * Math.PI / 5.0);

    /// <summary>Work items a pass must hold before it is split across threads.</summary>
    private const int ThreadedItems = 1 << 14;

    /// <summary>
    /// True when <paramref name="n"/> is above one and made only of the factors 2, 3 and 5. A power
    /// of two is smooth too, but has roads of its own; the dispatcher asks about it first.
    /// </summary>
    public static bool IsSmooth(int n)
    {
        if (n < 2)
        {
            return false;
        }

        foreach (int f in stackalloc int[] { 2, 3, 5 })
        {
            while (n % f == 0)
            {
                n /= f;
            }
        }

        return n == 1;
    }

    /// <summary>The radices of one length, largest passes first: 4s, a 2 if the power of two is odd, then 3s, then 5s.</summary>
    internal static int[] Radices(int n)
    {
        var list = new List<int>();
        int twos = 0;
        while (n % 2 == 0)
        {
            n /= 2;
            twos++;
        }

        for (int i = 0; i < twos / 2; i++)
        {
            list.Add(4);
        }

        if (twos % 2 == 1)
        {
            list.Add(2);
        }

        while (n % 3 == 0)
        {
            n /= 3;
            list.Add(3);
        }

        while (n % 5 == 0)
        {
            n /= 5;
            list.Add(5);
        }

        if (n != 1)
        {
            throw new ArgumentException("the length is not 5-smooth.", nameof(n));
        }

        return list.ToArray();
    }

    /// <summary>The twiddles of one length and direction, one table per pass.</summary>
    internal sealed class Plan : FftKernels.FftPlan
    {
        internal Plan(int n, bool inverse)
            : base(n, inverse)
        {
            RadixList = Radices(n);
            TwiddleRe = new double[RadixList.Length][];
            TwiddleIm = new double[RadixList.Length][];
            double sign = inverse ? 1.0 : -1.0;
            long bytes = 0;
            int current = n;
            for (int pass = 0; pass < RadixList.Length; pass++)
            {
                int r = RadixList[pass];
                int m = current / r;
                var re = new double[m * (r - 1)];
                var im = new double[m * (r - 1)];
                for (int p = 0; p < m; p++)
                {
                    for (int q = 1; q < r; q++)
                    {
                        long turn = (long)p * q % current;
                        double angle = sign * 2.0 * Math.PI * turn / current;
                        re[(p * (r - 1)) + q - 1] = Math.Cos(angle);
                        im[(p * (r - 1)) + q - 1] = Math.Sin(angle);
                    }
                }

                TwiddleRe[pass] = re;
                TwiddleIm[pass] = im;
                bytes += 16L * re.Length;
                current = m;
            }

            Bytes = bytes;
        }

        internal int[] RadixList { get; }

        internal double[][] TwiddleRe { get; }

        internal double[][] TwiddleIm { get; }

        /// <summary>How many bytes a plan for this length holds, before it is built.</summary>
        internal static long BytesFor(int n)
        {
            long bytes = 0;
            foreach (int r in Radices(n))
            {
                int m = n / r;
                bytes += 16L * m * (r - 1);
                n = m;
            }

            return bytes;
        }
    }

    /// <summary>
    /// One transform of a 5-smooth length in place, unscaled either way. <paramref name="inside"/>
    /// lets a pass split its work items across threads.
    /// </summary>
    public static void Transform(Span<double> re, Span<double> im, int n, bool inverse, bool inside)
    {
        Plan plan = (Plan)FftKernels.PlanFor(n, inverse, mixed: true);
        var pool = ArrayPool<double>.Shared;
        double[] workRe = pool.Rent(n);
        double[] workIm = pool.Rent(n);
        try
        {
            Span<double> xr = re[..n];
            Span<double> xi = im[..n];
            Span<double> yr = workRe.AsSpan(0, n);
            Span<double> yi = workIm.AsSpan(0, n);
            int current = n;
            int stride = 1;
            bool inWork = false;
            for (int pass = 0; pass < plan.RadixList.Length; pass++)
            {
                int r = plan.RadixList[pass];
                int m = current / r;
                Pass(xr, xi, yr, yi, r, m, stride, inverse, plan.TwiddleRe[pass], plan.TwiddleIm[pass], inside);
                Span<double> tr = xr;
                Span<double> ti = xi;
                xr = yr;
                xi = yi;
                yr = tr;
                yi = ti;
                inWork = !inWork;
                current = m;
                stride *= r;
            }

            if (inWork)
            {
                xr.CopyTo(re);
                xi.CopyTo(im);
            }
        }
        finally
        {
            pool.Return(workRe);
            pool.Return(workIm);
            plan.Release();
        }
    }

    /// <summary>
    /// One pass: every (p, q′) pair is one work item; the items are cut into blocks along p when
    /// there are enough of those, else along q′, and the cut is a function of the shape alone.
    /// </summary>
    private static void Pass(
        ReadOnlySpan<double> xr, ReadOnlySpan<double> xi, Span<double> yr, Span<double> yi,
        int r, int m, int stride, bool inverse, double[] twRe, double[] twIm, bool inside)
    {
        long items = (long)m * stride;
        bool threaded = inside && items >= ThreadedItems;
        int blocks = threaded ? (int)Math.Min(64, items / (ThreadedItems / 4)) : 1;
        bool alongP = m >= blocks;
        int span = alongP ? m : stride;
        int per = (span + blocks - 1) / blocks;
        blocks = (span + per - 1) / per;

        // The spans cannot cross into the lambda; the block body works from pointers to the same
        // memory, pinned for the pass.
        unsafe
        {
            fixed (double* pxr = xr, pxi = xi, pyr = yr, pyi = yi, ptr = twRe, pti = twIm)
            {
                double* fxr = pxr, fxi = pxi, fyr = pyr, fyi = pyi, ftr = ptr, fti = pti;
                ParallelKernels.ForBlocks(blocks, threaded, block =>
                {
                    int from = block * per;
                    int to = Math.Min(span, from + per);
                    if (alongP)
                    {
                        Butterflies(fxr, fxi, fyr, fyi, r, m, stride, from, to, 0, stride, inverse, ftr, fti);
                    }
                    else
                    {
                        Butterflies(fxr, fxi, fyr, fyi, r, m, stride, 0, m, from, to, inverse, ftr, fti);
                    }
                });
            }
        }
    }

    private static unsafe void Butterflies(
        double* xr, double* xi, double* yr, double* yi,
        int r, int m, int stride, int pFrom, int pTo, int sFrom, int sTo, bool inverse,
        double* twRe, double* twIm)
    {
        switch (r)
        {
            case 2:
                Radix2(xr, xi, yr, yi, m, stride, pFrom, pTo, sFrom, sTo, twRe, twIm);
                break;
            case 3:
                Radix3(xr, xi, yr, yi, m, stride, pFrom, pTo, sFrom, sTo, inverse, twRe, twIm);
                break;
            case 4:
                Radix4(xr, xi, yr, yi, m, stride, pFrom, pTo, sFrom, sTo, inverse, twRe, twIm);
                break;
            case 5:
                Radix5(xr, xi, yr, yi, m, stride, pFrom, pTo, sFrom, sTo, inverse, twRe, twIm);
                break;
            default:
                throw new InvalidOperationException($"no radix-{r} butterfly.");
        }
    }

    private static unsafe void Radix2(
        double* xr, double* xi, double* yr, double* yi, int m, int s,
        int pFrom, int pTo, int sFrom, int sTo, double* twRe, double* twIm)
    {
        for (int p = pFrom; p < pTo; p++)
        {
            double w1r = twRe[p];
            double w1i = twIm[p];
            double* a0r = xr + (s * p);
            double* a0i = xi + (s * p);
            double* a1r = a0r + (s * m);
            double* a1i = a0i + (s * m);
            double* b0r = yr + (s * 2 * p);
            double* b0i = yi + (s * 2 * p);
            double* b1r = b0r + s;
            double* b1i = b0i + s;
            for (int q = sFrom; q < sTo; q++)
            {
                double ar = a0r[q];
                double ai = a0i[q];
                double br = a1r[q];
                double bi = a1i[q];
                b0r[q] = ar + br;
                b0i[q] = ai + bi;
                double dr = ar - br;
                double di = ai - bi;
                b1r[q] = (dr * w1r) - (di * w1i);
                b1i[q] = (di * w1r) + (dr * w1i);
            }
        }
    }

    private static unsafe void Radix3(
        double* xr, double* xi, double* yr, double* yi, int m, int s,
        int pFrom, int pTo, int sFrom, int sTo, bool inverse, double* twRe, double* twIm)
    {
        double sin3 = inverse ? Sin3 : -Sin3;
        for (int p = pFrom; p < pTo; p++)
        {
            double w1r = twRe[2 * p];
            double w1i = twIm[2 * p];
            double w2r = twRe[(2 * p) + 1];
            double w2i = twIm[(2 * p) + 1];
            double* a0r = xr + (s * p);
            double* a0i = xi + (s * p);
            double* a1r = a0r + (s * m);
            double* a1i = a0i + (s * m);
            double* a2r = a1r + (s * m);
            double* a2i = a1i + (s * m);
            double* b0r = yr + (s * 3 * p);
            double* b0i = yi + (s * 3 * p);
            double* b1r = b0r + s;
            double* b1i = b0i + s;
            double* b2r = b1r + s;
            double* b2i = b1i + s;
            for (int q = sFrom; q < sTo; q++)
            {
                double tr = a1r[q] + a2r[q];
                double ti = a1i[q] + a2i[q];
                double ur = a1r[q] - a2r[q];
                double ui = a1i[q] - a2i[q];
                b0r[q] = a0r[q] + tr;
                b0i[q] = a0i[q] + ti;
                double mr = a0r[q] + (Cos3 * tr);
                double mi = a0i[q] + (Cos3 * ti);

                // i·sin3·u = (−sin3·ui, sin3·ur)
                double nr = -sin3 * ui;
                double ni = sin3 * ur;
                double c1r = mr + nr;
                double c1i = mi + ni;
                double c2r = mr - nr;
                double c2i = mi - ni;
                b1r[q] = (c1r * w1r) - (c1i * w1i);
                b1i[q] = (c1i * w1r) + (c1r * w1i);
                b2r[q] = (c2r * w2r) - (c2i * w2i);
                b2i[q] = (c2i * w2r) + (c2r * w2i);
            }
        }
    }

    private static unsafe void Radix4(
        double* xr, double* xi, double* yr, double* yi, int m, int s,
        int pFrom, int pTo, int sFrom, int sTo, bool inverse, double* twRe, double* twIm)
    {
        // ω = e^{∓2πi/4} = ∓i: ω·t is (ti, −tr) forward and (−ti, tr) inverse.
        double sign = inverse ? 1.0 : -1.0;
        for (int p = pFrom; p < pTo; p++)
        {
            double w1r = twRe[3 * p];
            double w1i = twIm[3 * p];
            double w2r = twRe[(3 * p) + 1];
            double w2i = twIm[(3 * p) + 1];
            double w3r = twRe[(3 * p) + 2];
            double w3i = twIm[(3 * p) + 2];
            double* a0r = xr + (s * p);
            double* a0i = xi + (s * p);
            double* a1r = a0r + (s * m);
            double* a1i = a0i + (s * m);
            double* a2r = a1r + (s * m);
            double* a2i = a1i + (s * m);
            double* a3r = a2r + (s * m);
            double* a3i = a2i + (s * m);
            double* b0r = yr + (s * 4 * p);
            double* b0i = yi + (s * 4 * p);
            double* b1r = b0r + s;
            double* b1i = b0i + s;
            double* b2r = b1r + s;
            double* b2i = b1i + s;
            double* b3r = b2r + s;
            double* b3i = b2i + s;
            for (int q = sFrom; q < sTo; q++)
            {
                double t0r = a0r[q] + a2r[q];
                double t0i = a0i[q] + a2i[q];
                double t1r = a0r[q] - a2r[q];
                double t1i = a0i[q] - a2i[q];
                double t2r = a1r[q] + a3r[q];
                double t2i = a1i[q] + a3i[q];
                double t3r = a1r[q] - a3r[q];
                double t3i = a1i[q] - a3i[q];

                // ω·t3 = sign·i·t3 = (−sign·t3i, sign·t3r)
                double vr = -sign * t3i;
                double vi = sign * t3r;
                b0r[q] = t0r + t2r;
                b0i[q] = t0i + t2i;
                double c1r = t1r + vr;
                double c1i = t1i + vi;
                double c2r = t0r - t2r;
                double c2i = t0i - t2i;
                double c3r = t1r - vr;
                double c3i = t1i - vi;
                b1r[q] = (c1r * w1r) - (c1i * w1i);
                b1i[q] = (c1i * w1r) + (c1r * w1i);
                b2r[q] = (c2r * w2r) - (c2i * w2i);
                b2i[q] = (c2i * w2r) + (c2r * w2i);
                b3r[q] = (c3r * w3r) - (c3i * w3i);
                b3i[q] = (c3i * w3r) + (c3r * w3i);
            }
        }
    }

    private static unsafe void Radix5(
        double* xr, double* xi, double* yr, double* yi, int m, int s,
        int pFrom, int pTo, int sFrom, int sTo, bool inverse, double* twRe, double* twIm)
    {
        double s1 = inverse ? Sin5A : -Sin5A;
        double s2 = inverse ? Sin5B : -Sin5B;
        for (int p = pFrom; p < pTo; p++)
        {
            double w1r = twRe[4 * p];
            double w1i = twIm[4 * p];
            double w2r = twRe[(4 * p) + 1];
            double w2i = twIm[(4 * p) + 1];
            double w3r = twRe[(4 * p) + 2];
            double w3i = twIm[(4 * p) + 2];
            double w4r = twRe[(4 * p) + 3];
            double w4i = twIm[(4 * p) + 3];
            double* a0r = xr + (s * p);
            double* a0i = xi + (s * p);
            double* a1r = a0r + (s * m);
            double* a1i = a0i + (s * m);
            double* a2r = a1r + (s * m);
            double* a2i = a1i + (s * m);
            double* a3r = a2r + (s * m);
            double* a3i = a2i + (s * m);
            double* a4r = a3r + (s * m);
            double* a4i = a3i + (s * m);
            double* b0r = yr + (s * 5 * p);
            double* b0i = yi + (s * 5 * p);
            double* b1r = b0r + s;
            double* b1i = b0i + s;
            double* b2r = b1r + s;
            double* b2i = b1i + s;
            double* b3r = b2r + s;
            double* b3i = b2i + s;
            double* b4r = b3r + s;
            double* b4i = b3i + s;
            for (int q = sFrom; q < sTo; q++)
            {
                double t1r = a1r[q] + a4r[q];
                double t1i = a1i[q] + a4i[q];
                double t2r = a2r[q] + a3r[q];
                double t2i = a2i[q] + a3i[q];
                double t3r = a1r[q] - a4r[q];
                double t3i = a1i[q] - a4i[q];
                double t4r = a2r[q] - a3r[q];
                double t4i = a2i[q] - a3i[q];
                b0r[q] = a0r[q] + t1r + t2r;
                b0i[q] = a0i[q] + t1i + t2i;
                double m1r = a0r[q] + (Cos5A * t1r) + (Cos5B * t2r);
                double m1i = a0i[q] + (Cos5A * t1i) + (Cos5B * t2i);
                double m2r = a0r[q] + (Cos5B * t1r) + (Cos5A * t2r);
                double m2i = a0i[q] + (Cos5B * t1i) + (Cos5A * t2i);

                // n1 = i·(s1·t3 + s2·t4), n2 = i·(s2·t3 − s1·t4)
                double n1r = -((s1 * t3i) + (s2 * t4i));
                double n1i = (s1 * t3r) + (s2 * t4r);
                double n2r = -((s2 * t3i) - (s1 * t4i));
                double n2i = (s2 * t3r) - (s1 * t4r);
                double c1r = m1r + n1r;
                double c1i = m1i + n1i;
                double c4r = m1r - n1r;
                double c4i = m1i - n1i;
                double c2r = m2r + n2r;
                double c2i = m2i + n2i;
                double c3r = m2r - n2r;
                double c3i = m2i - n2i;
                b1r[q] = (c1r * w1r) - (c1i * w1i);
                b1i[q] = (c1i * w1r) + (c1r * w1i);
                b2r[q] = (c2r * w2r) - (c2i * w2i);
                b2i[q] = (c2i * w2r) + (c2r * w2i);
                b3r[q] = (c3r * w3r) - (c3i * w3i);
                b3i[q] = (c3i * w3r) + (c3r * w3i);
                b4r[q] = (c4r * w4r) - (c4i * w4i);
                b4i[q] = (c4i * w4r) + (c4r * w4i);
            }
        }
    }
}
