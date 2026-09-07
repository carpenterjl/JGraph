using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// What a filter is, asked of its coefficients: its response at a set of frequencies, its delay,
/// its order, its norm, its symmetry, and whether it is stable (M134).
/// </summary>
/// <remarks>
/// <para>
/// Two things here are less obvious than they look. The first is that MATLAB computes a frequency
/// response over a uniform grid with a transform rather than by evaluating the polynomials — the
/// answers differ in the last bits, and every name in this file that takes a point count inherits
/// that choice. The second is that the phase functions do not evaluate the response on the grid
/// they were asked for: they evaluate it on a grid at least eight thousand points long, unwrap
/// there, and take every <c>k</c>-th value. Unwrapping is a comparison against π between
/// neighbours, so it is wrong whenever two neighbours are far apart, and the only defence is to
/// make them close.
/// </para>
/// <para>
/// The predicates are all one-liners over the roots, except that none of them takes roots when it
/// can avoid it. Stability is decided by the reflection coefficients rather than by the poles,
/// because Schur's rule answers the question exactly and a root finder only answers it to within
/// its own error — a pole at 1 + 1e-14 is a root finder's opinion, not a filter's.
/// </para>
/// </remarks>
public static class FilterAnalysis
{
    /// <summary>The tolerance MATLAB's symmetry and phase tests use: eps to the two-thirds.</summary>
    public static readonly double DefaultTolerance = System.Math.Pow(2.220446049250313e-16, 2.0 / 3.0);

    /// <summary>The shortest grid the phase functions unwrap on.</summary>
    private const int UnwrapThreshold = 8192;

    // --- The frequency response ----------------------------------------------------------------

    /// <summary>
    /// <c>freqz</c> over a uniform grid: <paramref name="count"/> points over half the circle, or
    /// over the whole of it. The response comes from a transform of the coefficients, which is
    /// what MATLAB does and is not the same in its last bits as evaluating the ratio.
    /// </summary>
    public static (Complex[] Response, double[] Frequencies) Response(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, bool whole, double? sampleRate)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "freqz needs at least one frequency point.");
        }

        int points = whole ? count : 2 * count;
        double fs = sampleRate ?? (2 * System.Math.PI);

        double[] num = b.ToArray();
        double[] den = a.Length == 0 ? [1] : a.ToArray();
        int n = System.Math.Max(num.Length, den.Length);
        Array.Resize(ref num, n);
        Array.Resize(ref den, n);

        Complex[] top = Transform(num, points);
        var response = new Complex[count];
        if (IsScalarDenominator(den))
        {
            double lead = den[0];
            for (int i = 0; i < count; i++)
            {
                response[i] = top[i] / lead;
            }
        }
        else
        {
            Complex[] bottom = Transform(den, points);
            for (int i = 0; i < count; i++)
            {
                response[i] = top[i] / bottom[i];
            }
        }

        return (response, UniformGrid(count, whole, fs));
    }

    /// <summary><c>freqz</c> at a given list of frequencies, which is evaluated rather than transformed.</summary>
    public static Complex[] ResponseAt(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> frequencies, double? sampleRate)
    {
        var h = new Complex[frequencies.Length];
        for (int i = 0; i < frequencies.Length; i++)
        {
            double omega = sampleRate is null
                ? frequencies[i]
                : 2 * System.Math.PI * frequencies[i] / sampleRate.Value;
            Complex s = Complex.Exp(new Complex(0, omega));
            Complex top = Evaluate(b, s);
            if (a.Length <= 1)
            {
                double lead = a.Length == 0 ? 1 : a[0];
                h[i] = top / lead / Complex.Exp(new Complex(0, omega * (b.Length - 1)));
                continue;
            }

            int n = System.Math.Max(a.Length, b.Length);
            double[] num = b.ToArray();
            double[] den = a.ToArray();
            Array.Resize(ref num, n);
            Array.Resize(ref den, n);
            h[i] = Evaluate(num, s) / Evaluate(den, s);
        }

        return h;
    }

    /// <summary>The frequency grid <c>freqz</c> uses, with its last point pinned exactly.</summary>
    public static double[] UniformGrid(int count, bool whole, double fs)
    {
        var w = new double[count];
        if (whole)
        {
            double delta = fs / count;
            for (int i = 0; i < count; i++)
            {
                w[i] = i * delta;
            }

            if (count % 2 == 1)
            {
                w[((count + 1) / 2) - 1] = (fs / 2) - (fs / (2 * count));
                if ((count + 1) / 2 < count)
                {
                    w[(count + 1) / 2] = (fs / 2) + (fs / (2 * count));
                }
            }
            else
            {
                w[count / 2] = fs / 2;
            }

            w[count - 1] = fs - (fs / count);
            return w;
        }

        double step = fs / 2 / count;
        for (int i = 0; i < count; i++)
        {
            w[i] = i * step;
        }

        w[count - 1] = (fs / 2) - step;
        return w;
    }

    /// <summary>The transform of a coefficient vector, folded rather than truncated when it is too long.</summary>
    private static Complex[] Transform(double[] coefficients, int points)
    {
        double[] source = coefficients;
        if (points < coefficients.Length)
        {
            // MATLAB wraps the coefficients round the transform's length rather than cutting them
            // off, which is what makes a short transform of a long filter alias instead of lie.
            source = new double[points];
            for (int i = 0; i < coefficients.Length; i++)
            {
                source[i % points] += coefficients[i];
            }
        }
        else if (points > coefficients.Length)
        {
            source = new double[points];
            Array.Copy(coefficients, source, coefficients.Length);
        }

        return Fft.Forward(source);
    }

    // --- Impulse and step ----------------------------------------------------------------------

    /// <summary><c>impz</c>: the response to a single one.</summary>
    public static double[] Impulse(ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count)
    {
        var x = new double[count];
        if (count > 0)
        {
            x[0] = 1;
        }

        return DigitalFilter.Filter(b, a.Length == 0 ? [1] : a, x);
    }

    /// <summary><c>stepz</c>: the response to a one that stays.</summary>
    public static double[] Step(ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count)
    {
        var x = new double[count];
        Array.Fill(x, 1);
        return DigitalFilter.Filter(b, a.Length == 0 ? [1] : a, x);
    }

    /// <summary>
    /// <c>impzlength</c>: how many samples of the impulse response are worth having, which is set by
    /// the slowest pole's decay or, for a pole on the circle, by five periods of its oscillation.
    /// </summary>
    public static int ImpulseLength(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        if (IsFir(b, a))
        {
            return System.Math.Max(1, b.Length);
        }

        int delay = 0;
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] != 0)
            {
                delay = i;
                break;
            }
        }

        Complex[] poles = FrequencyTransforms.Roots(ToComplex(a));
        double n;

        bool unstable = false;
        double worst = 0;
        foreach (Complex p in poles)
        {
            if (Complex.Abs(p) > 1.0001)
            {
                unstable = true;
                worst = System.Math.Max(worst, Complex.Abs(p));
            }
        }

        // Six decades of growth: an unstable filter's response is worth showing until it is a
        // million times what it started at, and no longer.
        n = unstable ? 6 / System.Math.Log10(worst) : StableLength(poles, tolerance, delay);
        n = System.Math.Max(a.Length + b.Length - 1, n);
        return (int)System.Math.Floor(n);
    }

    /// <summary>The decay length of a stable or marginally stable pole set.</summary>
    private static double StableLength(Complex[] poles, double tolerance, int delay)
    {
        var oscillating = new List<Complex>();
        var damped = new List<Complex>();
        foreach (Complex value in poles)
        {
            Complex p = value;
            if (Complex.Abs(p - 1) < 1e-5)
            {
                p = -p;
            }

            if (System.Math.Abs(Complex.Abs(p) - 1) < 1e-5)
            {
                oscillating.Add(p);
            }
            else
            {
                damped.Add(p);
            }
        }

        if (damped.Count == 0)
        {
            return 5 * LongestPeriod(oscillating);
        }

        int worst = 0;
        for (int i = 1; i < damped.Count; i++)
        {
            if (Complex.Abs(damped[i]) > Complex.Abs(damped[worst]))
            {
                worst = i;
            }
        }

        double decay = Multiplicity(damped, worst) * System.Math.Log10(tolerance)
            / System.Math.Log10(Complex.Abs(damped[worst]));

        return oscillating.Count == 0
            ? decay + delay
            : System.Math.Max(5 * LongestPeriod(oscillating), decay) + delay;
    }

    private static double LongestPeriod(List<Complex> poles)
    {
        double longest = 0;
        foreach (Complex p in poles)
        {
            longest = System.Math.Max(longest, 2 * System.Math.PI / System.Math.Abs(p.Phase));
        }

        return longest;
    }

    /// <summary>How many of a pole set sit on top of one of them, within a relative thousandth.</summary>
    private static int Multiplicity(List<Complex> poles, int index)
    {
        double threshold = 0.001;
        bool anyZero = false;
        foreach (Complex p in poles)
        {
            if (p == Complex.Zero)
            {
                anyZero = true;
                break;
            }
        }

        if (!anyZero)
        {
            threshold *= Complex.Abs(poles[index]);
        }

        int m = 0;
        foreach (Complex p in poles)
        {
            if (Complex.Abs(p - poles[index]) < threshold)
            {
                m++;
            }
        }

        return m;
    }

    /// <summary>
    /// <c>filternorm</c>: the energy of the impulse response, its peak gain, or any other norm of
    /// its coefficients.
    /// </summary>
    public static double Norm(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double p, double tolerance)
    {
        if (double.IsPositiveInfinity(p))
        {
            (Complex[] response, _) = Response(b, a, 1024, whole: false, sampleRate: null);
            double peak = 0;
            foreach (Complex h in response)
            {
                peak = System.Math.Max(peak, Complex.Abs(h));
            }

            return peak;
        }

        if (p != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "filternorm takes a norm of 2 or Inf.");
        }

        if (IsFir(b, a))
        {
            double sum = 0;
            foreach (double v in b)
            {
                sum += v * v;
            }

            return System.Math.Sqrt(sum);
        }

        double[] h2 = Impulse(b, a, ImpulseLength(b, a, tolerance));
        double energy = 0;
        foreach (double v in h2)
        {
            energy += v * v;
        }

        return System.Math.Sqrt(energy);
    }

    // --- Group delay --------------------------------------------------------------------------

    /// <summary>
    /// <c>grpdelay</c>: how long each frequency takes to get through, which is the derivative of the
    /// phase with the sign flipped.
    /// </summary>
    /// <remarks>
    /// A feed-forward filter uses Smith's identity — the delay is the ratio of two transforms, one
    /// of the coefficients and one of the coefficients weighted by their own index, which costs two
    /// transforms rather than a numerical derivative. A recursive one is split into second-order
    /// sections and each section's delay written in closed form, because the ratio identity is
    /// unstable wherever the response nearly vanishes.
    /// </remarks>
    public static (double[] Delay, double[] Frequencies) GroupDelay(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, bool whole)
    {
        double[] w = GroupDelayGrid(count, whole);
        return (GroupDelayAt(b, a, w), w);
    }

    /// <summary><c>grpdelay</c> at a given list of angular frequencies.</summary>
    public static double[] GroupDelayAt(ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> w)
    {
        (double[] bEq, double[] aEq) = FilterCoefficients.EqualLength(b, a.Length == 0 ? [1] : a);
        return IsFir(b, a) ? SmithDelay(bEq, aEq, w) : ShpakDelay(bEq, aEq, w);
    }

    /// <summary>The uniform grid of angular frequencies <c>grpdelay</c> answers on.</summary>
    private static double[] GroupDelayGrid(int count, bool whole)
    {
        double span = whole ? 2 * System.Math.PI : System.Math.PI;
        var w = new double[count];
        for (int i = 0; i < count; i++)
        {
            w[i] = span * i / count;
        }

        return w;
    }

    /// <summary>Smith's ratio-of-transforms delay, which is exact for a feed-forward filter.</summary>
    private static double[] SmithDelay(double[] b, double[] a, ReadOnlySpan<double> w)
    {
        int na = a.Length;
        var reversed = new double[na];
        for (int i = 0; i < na; i++)
        {
            reversed[i] = a[na - 1 - i];
        }

        double[] c = FilterCoefficients.Convolve(b, reversed);
        var weighted = new double[c.Length];
        for (int i = 0; i < c.Length; i++)
        {
            weighted[i] = c[i] * i;
        }

        var gd = new double[w.Length];
        for (int i = 0; i < w.Length; i++)
        {
            Complex s = Complex.Exp(new Complex(0, w[i]));
            Complex top = Evaluate(weighted, s);
            Complex bottom = Evaluate(c, s);
            gd[i] = bottom == Complex.Zero ? 0 : (top / bottom).Real;
            gd[i] -= na - 1;
        }

        // A linear-phase filter's delay is a constant, and saying so beats a ratio that loses its
        // figures wherever the response passes through zero.
        if (IsLinearPhase(b, a, 0))
        {
            int first = -1;
            int last = -1;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] != 0)
                {
                    last = i;
                    if (first < 0)
                    {
                        first = i;
                    }
                }
            }

            int length = first < 0 ? 1 : last - first + 1;
            double constant = System.Math.Max(first, 0) + ((length - 1) / 2.0);
            Array.Fill(gd, constant);
        }

        return gd;
    }

    /// <summary>Shpak's section-by-section delay, for a filter with feedback.</summary>
    private static double[] ShpakDelay(double[] b, double[] a, ReadOnlySpan<double> w)
    {
        var gd = new double[w.Length];
        var cw = new double[w.Length];
        var cw2 = new double[w.Length];
        var sw = new double[w.Length];
        var sw2 = new double[w.Length];
        for (int i = 0; i < w.Length; i++)
        {
            cw[i] = System.Math.Cos(w[i]);
            cw2[i] = System.Math.Cos(2 * w[i]);
            sw[i] = System.Math.Sin(w[i]);
            sw2[i] = System.Math.Sin(2 * w[i]);
        }

        FilterCoefficients.Zpk roots = FilterCoefficients.TfToZpk(b, a);
        SecondOrderSections.Cascade cascade = SecondOrderSections.FromRoots(
            roots.Zeros, roots.Poles, roots.Gains.Length == 0 ? 1 : roots.Gains[0].Real);
        for (int k = 0; k < cascade.Rows; k++)
        {
            double[] section = Row(cascade, k);
            Accumulate(gd, [section[0], section[1], section[2]], cw, sw, cw2, sw2, 1);
            Accumulate(gd, [section[3], section[4], section[5]], cw, sw, cw2, sw2, -1);
        }

        return gd;
    }

    private static double[] Row(SecondOrderSections.Cascade cascade, int row)
    {
        var section = new double[6];
        for (int j = 0; j < 6; j++)
        {
            section[j] = cascade.Sections[(j * cascade.Rows) + row];
        }

        return section;
    }

    /// <summary>One second-order section's contribution to the group delay.</summary>
    private static void Accumulate(
        double[] gd, double[] q, double[] cw, double[] sw, double[] cw2, double[] sw2, double sign)
    {
        double toler = 10 * 2.220446049250313e-16;

        if (q[1] == 0 && q[2] == 0)
        {
            return;
        }

        if (q[2] == 0)
        {
            if (System.Math.Abs(q[0] - q[1]) < toler || System.Math.Abs(q[0] + q[1]) < toler)
            {
                for (int i = 0; i < gd.Length; i++)
                {
                    gd[i] += sign * 0.5;
                }

                return;
            }

            double b1 = q[0];
            double b2 = q[1];
            double squared = (b1 * b1) + (b2 * b2);
            for (int i = 0; i < gd.Length; i++)
            {
                double u = b1 * sw[i];
                double v = (b1 * cw[i]) + b2;
                double du = b1 * cw[i];
                double dv = -b1 * sw[i];
                double u2v2 = squared + (2 * b1 * b2 * cw[i]);
                if (System.Math.Abs(u2v2) > System.Math.Pow(2.220446049250313e-16, 2.0 / 3.0))
                {
                    gd[i] += sign * (1 - (((v * du) - (u * dv)) / u2v2));
                }
            }

            return;
        }

        if (System.Math.Abs(q[0] - q[2]) < toler || System.Math.Abs(q[0] + q[2]) < toler)
        {
            for (int i = 0; i < gd.Length; i++)
            {
                gd[i] += sign;
            }

            return;
        }

        {
            double b1 = q[0];
            double b2 = q[1];
            double b3 = q[2];
            double constant = (b1 * b1) + (b2 * b2) + (b3 * b3);
            for (int i = 0; i < gd.Length; i++)
            {
                double u = (b1 * sw2[i]) + (b2 * sw[i]);
                double v = (b1 * cw2[i]) + (b2 * cw[i]) + b3;
                double du = (2 * b1 * cw2[i]) + (b2 * cw[i]);
                double dv = -((2 * b1 * sw2[i]) + (b2 * sw[i]));
                double u2v2 = constant
                    + (2 * ((b1 * b2) + (b2 * b3)) * cw[i])
                    + (2 * b1 * b3 * cw2[i]);
                gd[i] += sign * (2 - (((v * du) - (u * dv)) / u2v2));
            }
        }
    }

    /// <summary>
    /// A feed-forward filter's group delay at zero frequency, which is all <c>decimate</c> takes
    /// from <c>grpdelay</c>.
    /// </summary>
    public static double GroupDelayAtZero(ReadOnlySpan<double> b)
    {
        double weighted = 0;
        double total = 0;
        for (int i = 0; i < b.Length; i++)
        {
            weighted += i * b[i];
            total += b[i];
        }

        return total == 0 ? 0 : weighted / total;
    }

    // --- Phase --------------------------------------------------------------------------------

    /// <summary>
    /// <c>phasez</c>: the unwrapped phase, computed on a grid long enough that unwrapping is safe
    /// and then thinned back to the grid that was asked for.
    /// </summary>
    public static (double[] Phase, double[] Frequencies) Phase(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, bool whole)
    {
        int factor = UpsampleFactor(count, whole);
        (Complex[] h, double[] w) = Response(b, a, count * factor, whole, sampleRate: null);
        double[] unwrapped = UnwrapPhase(h);

        var phi = new double[count];
        var wOut = new double[count];
        for (int i = 0; i < count; i++)
        {
            phi[i] = unwrapped[i * factor];
            wOut[i] = w[i * factor];
        }

        if (b.Length == 1 && a.Length <= 1)
        {
            double lead = a.Length == 0 ? 1 : a[0];
            Array.Fill(phi, new Complex(b[0] / lead, 0).Phase);
        }

        return (phi, wOut);
    }

    /// <summary>How much finer than the answer's grid the phase is unwrapped on.</summary>
    private static int UpsampleFactor(int count, bool whole)
    {
        int factor = count < UnwrapThreshold ? (int)System.Math.Ceiling((double)UnwrapThreshold / count) : 1;
        return whole ? 2 * factor : factor;
    }

    /// <summary>
    /// The unwrapped angle, with points where the response nearly vanishes marked as absent — a
    /// zero on the unit circle has no phase, and interpolating one is how a phase plot picks up a
    /// jump that is not there.
    /// </summary>
    private static double[] UnwrapPhase(Complex[] h)
    {
        var phi = new double[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            phi[i] = Complex.Abs(h[i]) <= DefaultTolerance ? double.NaN : h[i].Phase;
        }

        double offset = 0;
        for (int i = 1; i < phi.Length; i++)
        {
            if (double.IsNaN(phi[i]) || double.IsNaN(phi[i - 1]))
            {
                continue;
            }

            double difference = phi[i] + offset - phi[i - 1];
            while (difference > System.Math.PI)
            {
                offset -= 2 * System.Math.PI;
                difference -= 2 * System.Math.PI;
            }

            while (difference < -System.Math.PI)
            {
                offset += 2 * System.Math.PI;
                difference += 2 * System.Math.PI;
            }

            phi[i] += offset;
        }

        return phi;
    }

    /// <summary><c>phasedelay</c>: the phase divided by the frequency, which is a delay in samples.</summary>
    public static (double[] Delay, double[] Frequencies) PhaseDelay(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, bool whole)
    {
        (double[] hz, double[] w, double[] phi) = ZeroPhase(b, a, count, whole);
        _ = hz;
        // The division at zero frequency is left alone: MATLAB divides there too, and answers the
        // infinity or the NaN that comes out rather than pretending the delay is nought.
        var delay = new double[count];
        for (int i = 0; i < count; i++)
        {
            delay[i] = -phi[i] / w[i];
        }

        return (delay, w);
    }

    /// <summary>
    /// <c>zerophase</c>: the amplitude response, which unlike the magnitude keeps its sign, and the
    /// continuous phase that goes with it.
    /// </summary>
    /// <remarks>
    /// A linear-phase filter has an exact answer: divide the response by the known linear phase and
    /// take the real part. Everything else has to have its sign guessed, and MATLAB guesses it from
    /// where the unwrapped phase jumps by nearly π — every such jump is a sign change, and the
    /// response at zero says which sign the first stretch has.
    /// </remarks>
    public static (double[] Amplitude, double[] Frequencies, double[] Phase) ZeroPhase(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, bool whole)
    {
        if (IsScalarDenominator(a.Length == 0 ? [1] : a) && IsLinearPhase(b, [1], DefaultTolerance))
        {
            return ExactZeroPhase(b, a.Length == 0 ? 1 : a[0], count, whole);
        }

        int factor = UpsampleFactor(count, whole);
        int fine = count * factor;
        (Complex[] h, double[] w) = Response(b, a, fine, whole, sampleRate: null);
        double[] phi = UnwrapPhase(h);
        double[] sign = EstimateSign(b, a, phi, whole, fine);
        double[] continuous = ContinuousPhase(sign, phi, out bool firstPositive);
        _ = firstPositive;

        var amplitude = new double[count];
        var phase = new double[count];
        var frequencies = new double[count];
        for (int i = 0; i < count; i++)
        {
            amplitude[i] = sign[i * factor] * Complex.Abs(h[i * factor]);
            phase[i] = continuous[i * factor];
            frequencies[i] = w[i * factor];
        }

        return (amplitude, frequencies, phase);
    }

    /// <summary>The exact amplitude response of a linear-phase feed-forward filter.</summary>
    private static (double[] Amplitude, double[] Frequencies, double[] Phase) ExactZeroPhase(
        ReadOnlySpan<double> b, double lead, int count, bool whole)
    {
        int last = -1;
        int first = -1;
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] != 0)
            {
                last = i;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        var trimmed = new double[last < 0 ? 0 : last + 1];
        for (int i = 0; i < trimmed.Length; i++)
        {
            trimmed[i] = b[i] / lead;
        }

        int leadingZeros = first < 0 ? 0 : first;
        int order = trimmed.Length <= 1 ? 0 : trimmed.Length - 1;
        int type = trimmed.Length <= 1 ? 1 : DetermineType(order, Symmetry(trimmed) == SymmetryKind.Symmetric);

        (Complex[] h, double[] w) = Response(trimmed.Length == 0 ? [0] : trimmed, [1], count, whole, sampleRate: null);
        double p = type is 3 or 4 ? System.Math.PI / 2 : 0;

        var amplitude = new double[count];
        var phase = new double[count];
        for (int i = 0; i < count; i++)
        {
            phase[i] = p - (w[i] * (order + leadingZeros) / 2.0);
            amplitude[i] = (h[i] * Complex.Exp(new Complex(0, -phase[i]))).Real;
        }

        return (amplitude, w, phase);
    }

    /// <summary>The sign of the amplitude response, one stretch at a time between phase jumps.</summary>
    private static double[] EstimateSign(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, double[] phi, bool whole, int fine)
    {
        int half = whole ? fine / 2 : fine;
        var jumps = new List<int> { 0 };
        for (int i = 1; i < half; i++)
        {
            if (System.Math.Abs(phi[i] - phi[i - 1]) > System.Math.PI * 170 / 180)
            {
                jumps.Add(i);
            }
        }

        for (int i = 0; i < half; i++)
        {
            if (double.IsNaN(phi[i]) && (i == 0 || !double.IsNaN(phi[i - 1])) && !jumps.Contains(i))
            {
                jumps.Add(i);
            }
        }

        jumps.Add(half);
        jumps.Sort();

        int at = 0;
        while (at < phi.Length && double.IsNaN(phi[at]))
        {
            at++;
        }

        double firstPositive;
        if (at == 0)
        {
            double top = 0;
            foreach (double v in b)
            {
                top += v;
            }

            double bottom = 0;
            foreach (double v in a)
            {
                bottom += v;
            }

            double h0 = bottom == 0 ? 0 : top / bottom;
            firstPositive = (System.Math.Sign(h0) / 2.0) + 0.5;
        }
        else
        {
            firstPositive = (System.Math.Sign(phi[at]) / 2.0) + 0.5;
        }

        var sign = new List<double>();
        for (int i = 0; i + 1 < jumps.Count; i++)
        {
            double value = System.Math.Pow(-1, i + 1 + firstPositive);
            for (int j = jumps[i]; j < jumps[i + 1]; j++)
            {
                sign.Add(value);
            }
        }

        if (!whole)
        {
            while (sign.Count < fine)
            {
                sign.Add(sign.Count == 0 ? 1 : sign[^1]);
            }

            return [.. sign];
        }

        var mirrored = new List<double>(sign);
        for (int i = sign.Count - 1; i >= 0; i--)
        {
            mirrored.Add(sign[i]);
        }

        while (mirrored.Count < fine)
        {
            mirrored.Add(mirrored.Count == 0 ? 1 : mirrored[^1]);
        }

        return [.. mirrored.GetRange(0, fine)];
    }

    /// <summary>The phase with the sign changes taken out of it, so that it runs continuously.</summary>
    private static double[] ContinuousPhase(double[] sign, double[] phi, out bool firstPositive)
    {
        var result = new double[phi.Length];
        double compensate = 0;
        for (int i = 0; i < phi.Length; i++)
        {
            if (i > 0)
            {
                compensate += System.Math.Abs(((sign[i] - 1) / 2) - ((sign[i - 1] - 1) / 2));
            }

            result[i] = phi[i] - (compensate * System.Math.PI);
        }

        firstPositive = sign.Length == 0 || sign[0] > 0;
        if (!firstPositive)
        {
            for (int i = 0; i < result.Length; i++)
            {
                result[i] += result[0] >= 0 ? -System.Math.PI : System.Math.PI;
            }
        }

        return result;
    }

    // --- The predicates -------------------------------------------------------------------------

    /// <summary>Whether a filter has no feedback: its denominator is a single non-zero coefficient.</summary>
    public static bool IsFir(ReadOnlySpan<double> b, ReadOnlySpan<double> a)
    {
        _ = b;
        ReadOnlySpan<double> c = a.Length == 0 ? [1] : a;
        int last = -1;
        for (int i = c.Length - 1; i >= 0; i--)
        {
            if (c[i] != 0)
            {
                last = i;
                break;
            }
        }

        if (last <= 0)
        {
            return true;
        }

        int nonZero = 0;
        foreach (double v in c)
        {
            if (v != 0)
            {
                nonZero++;
            }
        }

        return nonZero == 1;
    }

    /// <summary>
    /// <c>isstable</c>: whether every pole is inside the unit circle, decided by Schur's rule on the
    /// reflection coefficients rather than by finding the poles.
    /// </summary>
    public static bool IsStable(ReadOnlySpan<double> a)
    {
        int trailing = 0;
        while (trailing < a.Length && a[a.Length - 1 - trailing] == 0)
        {
            trailing++;
        }

        if (trailing == a.Length)
        {
            throw new ArgumentException("isstable needs a denominator that is not all zeros.", nameof(a));
        }

        int leading = 0;
        while (a[leading] == 0)
        {
            leading++;
        }

        int length = a.Length - trailing - leading;
        var a2 = new double[length];
        for (int i = 0; i < length; i++)
        {
            a2[i] = a[leading + i] / a[leading];
        }

        if (length == 1)
        {
            return true;
        }

        if (length == 2)
        {
            return System.Math.Abs(a2[1]) < 1;
        }

        if (System.Math.Abs(a2[^1]) >= 1)
        {
            return false;
        }

        double[] k;
        try
        {
            k = LatticeFilters.ReflectionCoefficients(a2);
        }
        catch (ArgumentException)
        {
            return false;
        }

        foreach (double value in k)
        {
            if (double.IsNaN(value) || System.Math.Abs(value) >= 1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>isminphase</c>: whether every zero is inside the unit circle too.</summary>
    public static bool IsMinimumPhase(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        if (!IsStable(a.Length == 0 ? [1] : a))
        {
            return false;
        }

        return MinimumPhaseNumerator(b, tolerance);
    }

    private static bool MinimumPhaseNumerator(ReadOnlySpan<double> b, double tolerance)
    {
        int last = -1;
        for (int i = b.Length - 1; i >= 0; i--)
        {
            if (b[i] != 0)
            {
                last = i;
                break;
            }
        }

        if (last < 0)
        {
            return true;
        }

        var trimmed = new double[last + 1];
        for (int i = 0; i <= last; i++)
        {
            trimmed[i] = b[i];
        }

        if (IsStable(trimmed))
        {
            return true;
        }

        Complex[] z = FrequencyTransforms.Roots(ToComplex(trimmed));
        foreach (Complex root in z)
        {
            if (Complex.Abs(root) > 1 + tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>ismaxphase</c>: every zero outside the circle, with the poles still inside it.</summary>
    public static bool IsMaximumPhase(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        ReadOnlySpan<double> den = a.Length == 0 ? [1] : a;
        int last = -1;
        for (int i = b.Length - 1; i >= 0; i--)
        {
            if (b[i] != 0)
            {
                last = i;
                break;
            }
        }

        var trimmed = new double[last < 0 ? 0 : last + 1];
        for (int i = 0; i < trimmed.Length; i++)
        {
            trimmed[i] = b[i];
        }

        // A zero at the origin cannot be outside the circle, and neither can a filter with more
        // poles than zeros be maximum phase at infinity.
        Complex[] roots = trimmed.Length > 1 ? FrequencyTransforms.Roots(ToComplex(trimmed)) : [];
        foreach (Complex root in roots)
        {
            if (root == Complex.Zero)
            {
                return false;
            }
        }

        if (den.Length > b.Length)
        {
            return false;
        }

        var reversed = new double[b.Length];
        for (int i = 0; i < b.Length; i++)
        {
            reversed[i] = b[b.Length - 1 - i];
        }

        return MinimumPhaseNumerator(reversed, tolerance) && IsStable(den);
    }

    /// <summary><c>isallpass</c>: the numerator is the denominator read backwards.</summary>
    public static bool IsAllPass(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        if (b.Length == 0 && a.Length == 0)
        {
            return true;
        }

        // Both ends are trimmed: a leading zero is a delay, which is all-pass, and a trailing one
        // is padding. What is left has to read the same backwards as the denominator reads forwards.
        double[] num = Trim(b, leading: true);
        double[] den = Trim(a, leading: true);
        if (num.Length == 0 || den.Length == 0 || (num.Length == 1 && num[0] == 0)
            || (den.Length == 1 && den[0] == 0))
        {
            return false;
        }

        if (den[0] == 0 || num[^1] == 0 || num.Length != den.Length)
        {
            return false;
        }

        double worst = 0;
        for (int i = 0; i < num.Length; i++)
        {
            worst = System.Math.Max(worst, System.Math.Abs((num[num.Length - 1 - i] / num[^1]) - (den[i] / den[0])));
        }

        return worst < tolerance;
    }

    /// <summary><c>islinphase</c>: the coefficients read the same forwards as backwards, up to a sign.</summary>
    public static bool IsLinearPhase(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        if (IsFir(b, a))
        {
            return Symmetry(b, tolerance) != SymmetryKind.None;
        }

        if (IsStable(a.Length == 0 ? [1] : a))
        {
            return false;
        }

        return Symmetry(b, tolerance) != SymmetryKind.None && Symmetry(a, tolerance) != SymmetryKind.None;
    }

    /// <summary><c>filtord</c>: the order, which is the effective length of the longer side less one.</summary>
    public static int Order(ReadOnlySpan<double> b, ReadOnlySpan<double> a) =>
        System.Math.Max(EffectiveLength(b), EffectiveLength(a)) - 1;

    /// <summary>How long a coefficient vector is once its trailing negligible terms are dropped.</summary>
    private static int EffectiveLength(ReadOnlySpan<double> coefficients)
    {
        double peak = 0;
        foreach (double v in coefficients)
        {
            peak = System.Math.Max(peak, System.Math.Abs(v));
        }

        if (peak == 0)
        {
            return 0;
        }

        for (int i = coefficients.Length - 1; i >= 0; i--)
        {
            if (coefficients[i] / peak != 0)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary><c>firtype</c>: which of the four linear-phase symmetries a feed-forward filter has.</summary>
    public static int FirType(ReadOnlySpan<double> b)
    {
        if (!IsLinearPhase(b, [1], DefaultTolerance))
        {
            throw new ArgumentException("firtype needs a linear-phase filter.", nameof(b));
        }

        double[] h = Trim(b);
        if (h.Length <= 1)
        {
            return 1;
        }

        return DetermineType(h.Length - 1, Symmetry(h) == SymmetryKind.Symmetric);
    }

    private static int DetermineType(int order, bool symmetric) =>
        symmetric ? (order % 2 == 1 ? 2 : 1) : (order % 2 == 1 ? 4 : 3);

    /// <summary>Which of the three symmetries a real coefficient vector has.</summary>
    public enum SymmetryKind
    {
        None,
        Symmetric,
        Antisymmetric,
    }

    /// <summary>Tests a coefficient vector for symmetry, with its leading and trailing zeros dropped.</summary>
    public static SymmetryKind Symmetry(ReadOnlySpan<double> b) => Symmetry(b, DefaultTolerance);

    /// <summary>Tests a coefficient vector for symmetry against a given tolerance.</summary>
    public static SymmetryKind Symmetry(ReadOnlySpan<double> b, double tolerance)
    {
        double[] h = Trim(b, leading: true);
        if (h.Length <= 1)
        {
            return SymmetryKind.Symmetric;
        }

        double even = 0;
        double odd = 0;
        for (int i = 0; i < h.Length; i++)
        {
            even = System.Math.Max(even, System.Math.Abs(h[i] - h[h.Length - 1 - i]));
            odd = System.Math.Max(odd, System.Math.Abs(h[i] + h[h.Length - 1 - i]));
        }

        return even <= tolerance ? SymmetryKind.Symmetric
            : odd <= tolerance ? SymmetryKind.Antisymmetric
            : SymmetryKind.None;
    }

    // --- Small shared pieces --------------------------------------------------------------------

    private static bool IsScalarDenominator(ReadOnlySpan<double> a)
    {
        for (int i = 1; i < a.Length; i++)
        {
            if (a[i] != 0)
            {
                return false;
            }
        }

        return a.Length > 0;
    }

    private static double[] Trim(ReadOnlySpan<double> values, bool leading = false)
    {
        int last = -1;
        int first = -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != 0)
            {
                last = i;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (last < 0)
        {
            return leading ? [0] : [];
        }

        int start = leading ? first : 0;
        var result = new double[last - start + 1];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = values[start + i];
        }

        return result;
    }

    private static Complex[] ToComplex(ReadOnlySpan<double> values)
    {
        var result = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = values[i];
        }

        return result;
    }

    private static Complex Evaluate(ReadOnlySpan<double> coefficients, Complex s)
    {
        Complex sum = Complex.Zero;
        foreach (double c in coefficients)
        {
            sum = (sum * s) + c;
        }

        return sum;
    }
}
