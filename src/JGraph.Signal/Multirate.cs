using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// Changing a signal's sample rate: the two trivial rate changes, the polyphase engine underneath
/// the useful ones, and the three names that design a filter before they use it (M133).
/// </summary>
/// <remarks>
/// <para>
/// Raising a sample rate means inserting zeros and then removing the images the zeros create;
/// lowering one means removing the frequencies that would alias and then dropping samples. Both are
/// one filter and one stride, which is why they are one routine here — MATLAB calls it
/// <c>upfirdn</c>, and <c>interp</c>, <c>decimate</c> and <c>resample</c> are that routine with
/// three different filters in front of it.
/// </para>
/// <para>
/// The filters are the whole of the parity. <c>interp</c> designs a least-squares half-band-like
/// filter over a comb of bands, <c>decimate</c> a Chebyshev IIR or a windowed FIR, and
/// <c>resample</c> a Kaiser-windowed least-squares design at the larger of the two rates. All three
/// designs live in <see cref="PrototypeDesigns"/>, borrowed forward from M134; what is here is the
/// edge handling, which is where they differ from each other and from the textbook.
/// </para>
/// </remarks>
public static class Multirate
{
    /// <summary>
    /// <c>upsample</c>: one sample in, <paramref name="factor"/> out, the rest zero.
    /// </summary>
    public static double[] Upsample(ReadOnlySpan<double> x, int factor, int phase)
    {
        var y = new double[x.Length * factor];
        for (int i = 0; i < x.Length; i++)
        {
            y[(i * factor) + phase] = x[i];
        }

        return y;
    }

    /// <summary>
    /// <c>downsample</c>: every <paramref name="factor"/>-th sample, starting at
    /// <paramref name="phase"/>.
    /// </summary>
    public static double[] Downsample(ReadOnlySpan<double> x, int factor, int phase)
    {
        int count = phase >= x.Length ? 0 : ((x.Length - phase - 1) / factor) + 1;
        var y = new double[count];
        for (int i = 0; i < count; i++)
        {
            y[i] = x[phase + (i * factor)];
        }

        return y;
    }

    /// <summary>
    /// <c>upfirdn</c>: upsample, filter and downsample in one pass, so that no zero is ever
    /// multiplied and no sample is ever computed to be thrown away.
    /// </summary>
    /// <remarks>
    /// Written out naively this is three operations and <c>p·q</c> times more arithmetic than it
    /// needs. Written as a polyphase sum it is one loop: each output sample reaches back into the
    /// input at a stride of <c>p</c> and picks up only the taps that land on a real sample. That is
    /// the same arithmetic in the same order as the naive version, so the answers are bit-identical
    /// and only the cost differs.
    /// </remarks>
    public static double[] UpFirDown(ReadOnlySpan<double> x, ReadOnlySpan<double> h, int p, int q)
    {
        if (p < 1 || q < 1)
        {
            throw new ArgumentException("A rate change's factors are whole numbers above zero.", nameof(p));
        }

        long full = ((long)(x.Length - 1) * p) + h.Length;
        if (x.Length == 0 || h.Length == 0)
        {
            return [];
        }

        int count = (int)((full + q - 1) / q);
        var y = new double[count];

        for (int n = 0; n < count; n++)
        {
            long at = (long)n * q;
            double sum = 0;

            // The taps that land on a real input sample are those whose index is congruent to the
            // output position modulo the upsampling factor.
            long first = at % p;
            for (long m = first; m < h.Length && m <= at; m += p)
            {
                long index = (at - m) / p;
                if (index < x.Length)
                {
                    sum += h[(int)m] * x[(int)index];
                }
            }

            y[n] = sum;
        }

        return y;
    }

    /// <summary>
    /// <c>interp</c>: the sample rate raised by a whole factor, with the symmetric least-squares
    /// filter Oetken's method designs and MATLAB's reflected-endpoint startup.
    /// </summary>
    /// <remarks>
    /// The startup is the interesting half. The filter has a delay of <c>r·n</c> samples, so a plain
    /// run would put the answer that far late and would ring at the start. MATLAB filters a
    /// reflected copy of the signal's first few samples first, keeps the state that leaves behind,
    /// and runs the real signal from there; at the far end it does the same with the last few and
    /// splices the result on. The output is exactly <c>r</c> times as long as the input, with the
    /// original samples sitting at every <c>r</c>-th place.
    /// </remarks>
    public static (double[] Y, double[] B) Interpolate(ReadOnlySpan<double> x, int r, int n, double cutoff)
    {
        if ((2 * n) + 1 > x.Length)
        {
            throw new ArgumentException(
                $"Interpolation by this filter length needs at least {(2 * n) + 1} samples.", nameof(x));
        }

        int lx = x.Length;
        int rl = r * lx;
        int rn = r * n;
        double[] b = InterpolationFilter(r, n, cutoff);

        var y = new double[rl];
        for (int i = 0; i < lx; i++)
        {
            y[i * r] = x[i];
        }

        var head = new double[2 * rn];
        for (int i = 0; i < 2 * n && (i * r) < 2 * rn; i++)
        {
            head[i * r] = (2 * x[0]) - x[(2 * n) - i];
        }

        var state = new double[System.Math.Max(0, b.Length - 1)];
        DigitalFilter.Filter(b, [1.0], head, state);
        double[] body = DigitalFilter.Filter(b, [1.0], y, state);

        for (int i = 0; i < (lx - n) * r; i++)
        {
            y[i] = body[rn + i];
        }

        var tail = new double[2 * rn];
        for (int i = 0; i < 2 * n && (i * r) < 2 * rn; i++)
        {
            tail[i * r] = (2 * x[lx - 1]) - x[lx - 2 - i];
        }

        double[] after = DigitalFilter.Filter(b, [1.0], tail, state);
        for (int i = 0; i < rn; i++)
        {
            y[rl - rn + i] = after[i];
        }

        return (y, b);
    }

    /// <summary>
    /// The comb of bands <c>interp</c> designs against: a passband at the base rate and a stopband
    /// around each image, which is a narrower thing to ask for than a plain lowpass and so a better
    /// filter for the same length.
    /// </summary>
    private static double[] InterpolationFilter(int r, int n, double alpha)
    {
        double[] magnitude;
        double[] frequencies;

        if (alpha == 1)
        {
            magnitude = [r, r, 0, 0];
            frequencies = [0, 1.0 / (2 * r), 1.0 / (2 * r), 0.5];
        }
        else
        {
            int bands = r / 2;
            magnitude = new double[2 + (2 * bands)];
            magnitude[0] = r;
            magnitude[1] = r;

            frequencies = new double[(2 * bands) + 2];
            double reach = alpha / 2 / r;
            frequencies[1] = reach;
            int k = 0;
            for (int i = 2; i < (2 * bands) + 2; i += 2)
            {
                k++;
                frequencies[i] = ((double)k / r) - reach;
                frequencies[i + 1] = ((double)k / r) + reach;
            }

            if (frequencies[(2 * bands) + 1] > 0.5)
            {
                frequencies[(2 * bands) + 1] = 0.5;
            }
        }

        var doubled = new double[frequencies.Length];
        for (int i = 0; i < frequencies.Length; i++)
        {
            doubled[i] = 2 * frequencies[i];
        }

        return PrototypeDesigns.LeastSquares(2 * r * n, doubled, magnitude, []);
    }

    /// <summary>
    /// <c>decimate</c> with the default Chebyshev IIR: filtered without phase distortion and then
    /// sampled every <paramref name="r"/>-th place.
    /// </summary>
    public static double[] DecimateIir(ReadOnlySpan<double> x, int r, int order)
    {
        const double ripple = 0.05;
        double[] b = [];
        double[] a = [];
        int n = order;

        while (n > 0)
        {
            (b, a) = PrototypeDesigns.ChebyshevLowpass(n, ripple, 0.8 / r);
            if (!AllZero(b) && System.Math.Abs(MagnitudeDb(b, a, 0.8 / r) + ripple) <= 1e-6)
            {
                break;
            }

            n--;
        }

        if (n == 0)
        {
            throw new ArgumentException(
                "No Chebyshev design of this order meets the decimation band edge.", nameof(order));
        }

        int nd = x.Length;
        int nout = (int)System.Math.Ceiling(nd / (double)r);

        var b2 = new double[1, b.Length];
        var a2 = new double[1, a.Length];
        for (int i = 0; i < b.Length; i++)
        {
            b2[0, i] = b[i];
        }

        for (int i = 0; i < a.Length; i++)
        {
            a2[0, i] = a[i];
        }

        double[] filtered = FilterPasses.ZeroPhase(b2, a2, 1, x.ToArray(), nd, 1);
        int begin = r - ((r * nout) - nd);

        var y = new double[nout];
        for (int i = 0; i < nout; i++)
        {
            y[i] = filtered[begin - 1 + (i * r)];
        }

        return y;
    }

    /// <summary>
    /// <c>decimate</c> with a windowed FIR: run once forwards with a reflected startup, sampled at
    /// the filter's own group delay so that the answer lines up with the input.
    /// </summary>
    public static double[] DecimateFir(ReadOnlySpan<double> x, int r, int order)
    {
        int nd = x.Length;
        int nout = (int)System.Math.Ceiling(nd / (double)r);
        double[] b = PrototypeDesigns.WindowedLowpass(order, 1.0 / r);
        int taps = order + 1;

        var head = new double[taps];
        for (int i = 0; i < taps; i++)
        {
            head[i] = (2 * x[0]) - x[taps - i];
        }

        var state = new double[System.Math.Max(0, b.Length - 1)];
        DigitalFilter.Filter(b, [1.0], head, state);
        double[] body = DigitalFilter.Filter(b, [1.0], x, state);

        var tail = new double[2 * taps];
        for (int i = 0; i < 2 * taps; i++)
        {
            tail[i] = (2 * x[nd - 1]) - x[nd - 2 - i];
        }

        double[] after = DigitalFilter.Filter(b, [1.0], tail, state);

        int begin = (int)System.Math.Round(PrototypeDesigns.GroupDelayAtZero(b) + 1.25,
            MidpointRounding.AwayFromZero);

        var kept = new List<double>();
        int last = begin;
        for (int i = begin; i <= nd; i += r)
        {
            kept.Add(body[i - 1]);
            last = i;
        }

        int missing = nout - kept.Count;
        int from = r - (nd - last);
        for (int i = 0; i < missing; i++)
        {
            kept.Add(after[from - 1 + (i * r)]);
        }

        return [.. kept];
    }

    /// <summary>The magnitude of a filter's response at one frequency, in decibels.</summary>
    private static double MagnitudeDb(double[] b, double[] a, double f)
    {
        Complex top = Complex.Zero;
        for (int i = 0; i < b.Length; i++)
        {
            top += Complex.Exp(-Complex.ImaginaryOne * i * System.Math.PI * f) * b[i];
        }

        Complex bottom = Complex.Zero;
        for (int i = 0; i < a.Length; i++)
        {
            bottom += Complex.Exp(-Complex.ImaginaryOne * i * System.Math.PI * f) * a[i];
        }

        return 20 * System.Math.Log10(Complex.Abs(top / bottom));
    }

    /// <summary>Whether every coefficient is zero, which is how a failed design shows itself.</summary>
    private static bool AllZero(double[] values)
    {
        foreach (double v in values)
        {
            if (v != 0)
            {
                return false;
            }
        }

        return true;
    }

    // --- Rational resampling ------------------------------------------------------------------------

    /// <summary>
    /// <c>resample</c>'s filter: a Kaiser-windowed least-squares lowpass at the larger of the two
    /// rates, normalised so that the interpolation has unit gain.
    /// </summary>
    public static double[] ResampleFilter(int p, int q, int n, double beta)
    {
        if (n <= 0)
        {
            var flat = new double[p];
            Array.Fill(flat, 1.0);
            return flat;
        }

        int larger = System.Math.Max(p, q);
        double cutoff = 1.0 / 2 / larger;
        int length = (2 * n * larger) + 1;

        double[] h = PrototypeDesigns.LeastSquares(length - 1, [0, 2 * cutoff, 2 * cutoff, 1], [1, 1, 0, 0], []);
        double[] window = SignalWindows.Kaiser(length, beta);

        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            h[i] *= window[i];
            sum += h[i];
        }

        for (int i = 0; i < length; i++)
        {
            h[i] = p * h[i] / sum;
        }

        return h;
    }

    /// <summary>
    /// The filter padded so that the polyphase run's delay is a whole number of output samples, and
    /// that delay itself.
    /// </summary>
    /// <remarks>
    /// A filter of odd length has a delay of half its length less one, which is a whole number of
    /// input samples and almost never a whole number of output ones. Rather than interpolate,
    /// MATLAB pads the front of the filter with as many zeros as it takes for the delay to divide by
    /// the decimation factor, then pads the back with as many as it takes for the run to be long
    /// enough. The trim is then an integer offset, which is exact.
    /// </remarks>
    public static (double[] Padded, double[] Trimmed, int Delay) PadFilter(
        double[] h, int length, int lx, int p, int q)
    {
        double half = (length - 1) / 2.0;
        int leading = (int)System.Math.Floor(q - Modulo(half, q));

        var padded = new double[leading + h.Length];
        Array.Copy(h, 0, padded, leading, h.Length);
        half += leading;
        int delay = (int)System.Math.Floor(System.Math.Ceiling(half) / q);

        int trailing = 0;
        while (System.Math.Ceiling((((lx - 1.0) * p) + padded.Length + trailing) / q) - delay
            < System.Math.Ceiling(lx * (double)p / q))
        {
            trailing++;
        }

        var full = new double[padded.Length + trailing];
        Array.Copy(padded, full, padded.Length);

        var trimmed = new double[full.Length - leading - trailing];
        Array.Copy(full, leading, trimmed, 0, trimmed.Length);

        return (full, trimmed, delay);
    }

    /// <summary>
    /// <c>resample</c>: one column resampled by <paramref name="p"/> over <paramref name="q"/>.
    /// </summary>
    public static double[] Resample(ReadOnlySpan<double> x, int p, int q, double[] filter, int length)
    {
        if (p == 1 && q == 1)
        {
            return x.ToArray();
        }

        (double[] padded, _, int delay) = PadFilter(filter, length, x.Length, p, q);
        double[] run = UpFirDown(x, padded, p, q);

        int count = (int)System.Math.Ceiling(x.Length * (double)p / q);
        var y = new double[count];
        for (int i = 0; i < count; i++)
        {
            int at = delay + i;
            y[i] = at < run.Length ? run[at] : 0;
        }

        return y;
    }

    /// <summary>MATLAB's <c>mod</c>, which keeps the divisor's sign.</summary>
    private static double Modulo(double x, double y)
    {
        if (y == 0)
        {
            return x;
        }

        double remainder = x - (System.Math.Floor(x / y) * y);
        return remainder;
    }
}
