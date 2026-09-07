using System.Numerics;
using JGraph.Numerics.Optimization;

namespace JGraph.Signal;

/// <summary>The band shape of an IIR design (MATLAB's 'low' / 'high' / 'bandpass' / 'stop').</summary>
public enum FilterBandType
{
    LowPass,
    HighPass,
    BandPass,
    BandStop,
}

/// <summary>Which analogue prototype a classical IIR design starts from.</summary>
public enum PrototypeKind
{
    Butterworth,
    Chebyshev1,
    Chebyshev2,
    Elliptic,
    Bessel,
}

/// <summary>
/// The one pipeline behind <c>butter</c>, <c>cheby1</c>, <c>cheby2</c>, <c>ellip</c> and
/// <c>besself</c>, and the four minimum-order rules that go with them (M134).
/// </summary>
/// <remarks>
/// <para>
/// The five designs differ in exactly one line: which prototype they ask
/// <see cref="AnalogPrototypes"/> for. Everything after that is shared — pre-warp the cutoffs,
/// realise the prototype in state space, apply the band transform there, map to the unit circle,
/// and read the answer off in whichever of the four forms was asked for. Writing it once is not
/// tidiness: the state-space route is what keeps a high-order design's poles where they belong, and
/// five copies of it would be five chances to take the polynomial shortcut in one of them.
/// </para>
/// <para>
/// Two of the five know their zeros in closed form and three do not. Butterworth and Chebyshev
/// type I put every zero at the band edge — at <c>z = −1</c> for a lowpass, <c>z = +1</c> for a
/// highpass, both for a bandpass — so MATLAB writes them down and normalises the gain at one
/// frequency. Chebyshev type II, elliptic and Bessel have zeros that depend on the transform, so
/// theirs come back off the transformed system as transmission zeros. The two routes disagree in
/// the last bit or two and each name uses the one its own source uses.
/// </para>
/// </remarks>
public static class IirDesign
{
    /// <summary>The sample rate the digital pipeline works in: two, so that one is Nyquist.</summary>
    private const double SampleRate = 2;

    /// <summary>A design in every form at once; the caller reads off the one it was asked for.</summary>
    public readonly record struct Design(
        Complex[] Zeros, Complex[] Poles, double Gain, FrequencyTransforms.Block Block);

    /// <summary>
    /// The classical pipeline. <paramref name="cutoffs"/> holds one edge for a lowpass or highpass
    /// and two for a bandpass or bandstop, normalised to Nyquist unless
    /// <paramref name="analog"/> is set, in which case they are radians per second.
    /// </summary>
    public static Design Classical(
        PrototypeKind kind,
        int order,
        ReadOnlySpan<double> cutoffs,
        FilterBandType type,
        bool analog,
        double firstRipple,
        double secondRipple)
    {
        bool twoEdges = type is FilterBandType.BandPass or FilterBandType.BandStop;
        if (cutoffs.Length != (twoEdges ? 2 : 1))
        {
            throw new ArgumentException(
                $"A {Describe(type)} design needs {(twoEdges ? "two cutoff frequencies" : "one cutoff frequency")}.",
                nameof(cutoffs));
        }

        var u = new double[cutoffs.Length];
        for (int i = 0; i < cutoffs.Length; i++)
        {
            if (!analog && (cutoffs[i] <= 0 || cutoffs[i] >= 1))
            {
                throw new ArgumentException(
                    "Cutoff frequencies must lie strictly between 0 and 1 (1 = Nyquist).", nameof(cutoffs));
            }

            if (analog && cutoffs[i] <= 0)
            {
                throw new ArgumentException("Analogue cutoff frequencies must be positive.", nameof(cutoffs));
            }

            u[i] = analog
                ? cutoffs[i]
                : 2 * SampleRate * System.Math.Tan(System.Math.PI * cutoffs[i] / SampleRate);
        }

        if (twoEdges && u[1] <= u[0])
        {
            throw new ArgumentException("The upper cutoff must be greater than the lower cutoff.", nameof(cutoffs));
        }

        (Complex[] zeros, Complex[] poles, double gain) = kind switch
        {
            PrototypeKind.Butterworth => AnalogPrototypes.Butterworth(order),
            PrototypeKind.Chebyshev1 => AnalogPrototypes.Chebyshev1(order, firstRipple),
            PrototypeKind.Chebyshev2 => AnalogPrototypes.Chebyshev2(order, firstRipple),
            PrototypeKind.Elliptic => AnalogPrototypes.Elliptic(order, firstRipple, secondRipple),
            _ => AnalogPrototypes.Bessel(order),
        };

        FilterCoefficients.StateSpace ss = FilterCoefficients.ZpToSs(zeros, poles, gain);
        var block = new FrequencyTransforms.Block(ss.A, ss.B, ss.C, ss.D);

        double reference;
        if (!twoEdges)
        {
            reference = u[0];
            block = type == FilterBandType.LowPass
                ? FrequencyTransforms.LowpassToLowpass(block, reference)
                : FrequencyTransforms.LowpassToHighpass(block, reference);
        }
        else
        {
            double bandwidth = u[1] - u[0];
            reference = System.Math.Sqrt(u[0] * u[1]);
            block = type == FilterBandType.BandPass
                ? FrequencyTransforms.LowpassToBandpass(block, reference, bandwidth)
                : FrequencyTransforms.LowpassToBandstop(block, reference, bandwidth);
        }

        if (!analog)
        {
            block = FrequencyTransforms.BilinearBlock(block, SampleRate);
        }

        Complex[] finalPoles = FrequencyTransforms.Poles(block.A);
        Complex[] finalZeros;
        double finalGain;

        if (kind is PrototypeKind.Butterworth or PrototypeKind.Chebyshev1)
        {
            double flat = kind == PrototypeKind.Butterworth || order % 2 == 1
                ? 1
                : System.Math.Pow(10, -firstRipple / 20);
            (finalZeros, finalGain) = EdgeZeros(
                type, order, reference, analog, finalPoles, flat, kind == PrototypeKind.Butterworth);
        }
        else
        {
            (finalZeros, _, finalGain) =
                FilterCoefficients.SsToZp(block.A, block.B, block.C, block.D, 0);
        }

        return new Design(finalZeros, finalPoles, finalGain, block);
    }

    /// <summary>
    /// The zeros a Butterworth or type I Chebyshev design has by construction, and the gain that
    /// normalises the response at the band's reference frequency to <paramref name="flat"/>.
    /// </summary>
    /// <remarks>
    /// Every zero of these two designs sits where the response is meant to vanish, which after the
    /// bilinear map is <c>z = −1</c> for a lowpass, <c>z = +1</c> for a highpass, and both for a
    /// bandpass. Writing them down rather than reading them off the transformed system is what
    /// keeps a lowpass design's numerator exactly <c>(1 + z⁻¹)ⁿ</c> instead of nearly so.
    /// </remarks>
    private static (Complex[] Zeros, double Gain) EdgeZeros(
        FilterBandType type, int order, double reference, bool analog, Complex[] poles, double flat, bool flatBand)
    {
        if (analog)
        {
            switch (type)
            {
                case FilterBandType.LowPass:
                {
                    // An analogue lowpass has no zeros at all, so the gain is the whole numerator.
                    // A Butterworth's poles multiply to exactly ω₀ⁿ and MATLAB writes that rather
                    // than the product; a Chebyshev's do not, so its gain is the product itself.
                    if (flatBand)
                    {
                        return ([], System.Math.Pow(reference, order));
                    }

                    Complex product = Complex.One;
                    foreach (Complex p in poles)
                    {
                        product *= -p;
                    }

                    return ([], flat * product.Real);
                }

                case FilterBandType.HighPass:
                    return (new Complex[order], flat);

                case FilterBandType.BandPass:
                {
                    var centre = new Complex(0, reference);
                    Complex product = Complex.One;
                    foreach (Complex p in poles)
                    {
                        product *= centre - p;
                    }

                    return (new Complex[order], flat * (product / Complex.Pow(centre, order)).Real);
                }

                default:
                {
                    var zeros = new Complex[2 * order];
                    for (int i = 0; i < zeros.Length; i++)
                    {
                        zeros[i] = new Complex(0, reference * (i % 2 == 0 ? 1 : -1));
                    }

                    return (zeros, flat);
                }
            }
        }

        double omega = 2 * System.Math.Atan2(reference, 4);
        switch (type)
        {
            case FilterBandType.LowPass:
            {
                var zeros = new Complex[order];
                Array.Fill(zeros, -Complex.One);
                Complex product = Complex.One;
                foreach (Complex p in poles)
                {
                    product *= 1 - p;
                }

                return (zeros, flat * product.Real / System.Math.Pow(2, order));
            }

            case FilterBandType.HighPass:
            {
                var zeros = new Complex[order];
                Array.Fill(zeros, Complex.One);
                Complex product = Complex.One;
                foreach (Complex p in poles)
                {
                    product *= 1 + p;
                }

                return (zeros, flat * product.Real / System.Math.Pow(2, order));
            }

            case FilterBandType.BandPass:
            {
                var zeros = new Complex[2 * order];
                for (int i = 0; i < order; i++)
                {
                    zeros[i] = Complex.One;
                    zeros[order + i] = -Complex.One;
                }

                Complex at = Complex.Exp(Complex.ImaginaryOne * omega);
                Complex numerator = Complex.One;
                foreach (Complex p in poles)
                {
                    numerator *= at - p;
                }

                Complex denominator = Complex.One;
                foreach (Complex z in zeros)
                {
                    denominator *= at - z;
                }

                return (zeros, flat * (numerator / denominator).Real);
            }

            default:
            {
                var zeros = new Complex[2 * order];
                for (int i = 0; i < zeros.Length; i++)
                {
                    zeros[i] = Complex.Exp(Complex.ImaginaryOne * omega * (i % 2 == 0 ? 1 : -1));
                }

                Complex numerator = Complex.One;
                foreach (Complex p in poles)
                {
                    numerator *= 1 - p;
                }

                Complex denominator = Complex.One;
                foreach (Complex z in zeros)
                {
                    denominator *= 1 - z;
                }

                return (zeros, flat * (numerator / denominator).Real);
            }
        }
    }

    /// <summary>
    /// The coefficient form of a design: the denominator is the poles' polynomial, and the
    /// numerator is the zeros' polynomial <b>right-aligned</b> against it, which is what makes a
    /// design with fewer zeros than poles carry a delay rather than a lead.
    /// </summary>
    public static (double[] Numerator, double[] Denominator) ToTransferFunction(Design design)
    {
        double[] den = FilterCoefficients.RealPolynomial(design.Poles);
        double[] zeroPoly = FilterCoefficients.RealPolynomial(design.Zeros);
        var num = new double[den.Length];
        for (int i = 0; i < zeroPoly.Length; i++)
        {
            num[den.Length - zeroPoly.Length + i] = design.Gain * zeroPoly[i];
        }

        return (num, den);
    }

    /// <summary>The old two-output <c>butter</c>, kept as the shorthand the rest of the tree uses.</summary>
    public static (double[] B, double[] A) Butterworth(int order, ReadOnlySpan<double> cutoffs, FilterBandType type) =>
        ToTransferFunction(Classical(PrototypeKind.Butterworth, order, cutoffs, type, analog: false, 0, 0));

    // --- Minimum order -------------------------------------------------------------------------

    /// <summary>Which band a pair of passband and stopband edges describes.</summary>
    private enum EdgeShape
    {
        Lowpass = 1,
        Highpass = 2,
        Bandstop = 3,
        Bandpass = 4,
    }

    /// <summary>The order and natural frequency a minimum-order rule answers.</summary>
    public readonly record struct Selection(int Order, double[] Frequencies);

    /// <summary>
    /// <c>buttord</c>: the lowest order whose Butterworth design passes both specifications, and
    /// the 3 dB frequency that goes with it.
    /// </summary>
    public static Selection ButterworthOrder(
        ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        (EdgeShape shape, double[] wp, double[] ws) = Prepare("buttord", passband, stopband, rp, rs, analog);

        if (shape == EdgeShape.Bandstop)
        {
            wp[0] = MinimiseEdge(0, wp, ws, rs, rp, BandstopCost.Butterworth);
            wp[1] = MinimiseEdge(1, wp, ws, rs, rp, BandstopCost.Butterworth);
        }

        double wa = Selectivity(shape, wp, ws);
        int order = (int)System.Math.Ceiling(
            System.Math.Log10((System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1)
                / (System.Math.Pow(10, 0.1 * System.Math.Abs(rp)) - 1))
            / (2 * System.Math.Log10(wa)));

        double w0 = wa / System.Math.Pow(System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1, 1.0 / (2 * System.Math.Abs(order)));
        double[] wn = shape switch
        {
            EdgeShape.Lowpass => [w0 * wp[0]],
            EdgeShape.Highpass => [wp[0] / w0],
            EdgeShape.Bandstop => BandstopEdges(wp, w0),
            _ => BandpassEdges(wp, w0),
        };

        return new Selection(order, Denormalise(wn, analog));
    }

    /// <summary><c>cheb1ord</c>: the type I order, whose natural frequency is the passband edge itself.</summary>
    public static Selection Chebyshev1Order(
        ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        (int order, _, double[] wpAnalog, _, double[] wp, _, EdgeShape _) =
            ChebyshevOrder("cheb1ord", passband, stopband, rp, rs, analog);
        return new Selection(order, analog ? wpAnalog : wp);
    }

    /// <summary>
    /// <c>cheb2ord</c>: the type II order, whose natural frequency is the stopband edge — the band
    /// the ripple lives in is the band the specification pins.
    /// </summary>
    public static Selection Chebyshev2Order(
        ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        (int order, _, double[] wpAnalog, _, _, double[] ws, EdgeShape shape) =
            ChebyshevOrder("cheb2ord", passband, stopband, rp, rs, analog);

        if (analog)
        {
            double newWp = 1 / System.Math.Cosh(
                System.Math.Acosh(System.Math.Sqrt(
                    (System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1)
                    / (System.Math.Pow(10, 0.1 * System.Math.Abs(rp)) - 1))) / order);

            double[] wn = shape switch
            {
                EdgeShape.Lowpass => [wpAnalog[0] / newWp],
                EdgeShape.Highpass => [wpAnalog[0] * newWp],
                EdgeShape.Bandstop => PairedEdges(wpAnalog, newWp, stop: true),
                _ => PairedEdges(wpAnalog, newWp, stop: false),
            };

            return new Selection(order, wn);
        }

        return new Selection(order, ws);
    }

    /// <summary><c>ellipord</c>: the order the degree equation gives, which is the smallest of the four.</summary>
    public static Selection EllipticOrder(
        ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        (EdgeShape shape, double[] wp, double[] ws) = Prepare("ellipord", passband, stopband, rp, rs, analog);

        int order;
        if (shape == EdgeShape.Bandstop)
        {
            // The bandstop rule is written on the digital edges even for an analogue call, because
            // the substitution that turns a stopband into a lowpass is a trigonometric one.
            double[] p = analog ? Digitalise(passband) : [passband[0], passband[1]];
            double[] s = analog ? Digitalise(stopband) : [stopband[0], stopband[1]];

            double c = System.Math.Sin(System.Math.PI * (p[0] + p[1]))
                / (System.Math.Sin(System.Math.PI * p[0]) + System.Math.Sin(System.Math.PI * p[1]));
            double wpa = System.Math.Abs(System.Math.Sin(System.Math.PI * p[1]) / (System.Math.Cos(System.Math.PI * p[1]) - c));
            double s1 = System.Math.Sin(System.Math.PI * s[0]) / (System.Math.Cos(System.Math.PI * s[0]) - c);
            double s2 = System.Math.Sin(System.Math.PI * s[1]) / (System.Math.Cos(System.Math.PI * s[1]) - c);
            double wsa = System.Math.Min(System.Math.Abs(s1), System.Math.Abs(s2));
            order = EllipticDegreeOrder(wsa / wpa, rp, rs);
        }
        else
        {
            order = EllipticDegreeOrder(Selectivity(shape, wp, ws), rp, rs);
        }

        return new Selection(order, analog ? wp : [.. passband]);
    }

    /// <summary>The shared first half of <c>cheb1ord</c> and <c>cheb2ord</c>.</summary>
    private static (int Order, double Selectivity, double[] PassbandAnalog, double[] StopbandAnalog,
        double[] Passband, double[] Stopband, EdgeShape Shape) ChebyshevOrder(
        string name, ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        (EdgeShape shape, double[] wp, double[] ws) = Prepare(name, passband, stopband, rp, rs, analog);

        if (shape == EdgeShape.Bandstop)
        {
            wp[0] = MinimiseEdge(0, wp, ws, rs, rp, BandstopCost.Chebyshev);
            wp[1] = MinimiseEdge(1, wp, ws, rs, rp, BandstopCost.Chebyshev);
        }

        double wa = Selectivity(shape, wp, ws);
        int order = (int)System.Math.Ceiling(
            System.Math.Acosh(System.Math.Sqrt(
                (System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1)
                / (System.Math.Pow(10, 0.1 * System.Math.Abs(rp)) - 1))) / System.Math.Acosh(wa));

        return (order, wa, wp, ws, [.. passband], [.. stopband], shape);
    }

    /// <summary>The order the elliptic degree equation gives for a selectivity and two ripples.</summary>
    private static int EllipticDegreeOrder(double selectivity, double rp, double rs)
    {
        double wa = System.Math.Abs(selectivity);
        double epsilon = System.Math.Sqrt(System.Math.Pow(10, 0.1 * rp) - 1);
        double k1 = epsilon / System.Math.Sqrt(System.Math.Pow(10, 0.1 * rs) - 1);
        double k = 1 / wa;

        double kIntegral = AnalogPrototypes.CompleteIntegral(k);
        double kComplement = AnalogPrototypes.CompleteIntegral(System.Math.Sqrt((1 - k) * (1 + k)));
        double k1Integral = AnalogPrototypes.CompleteIntegral(k1);
        double k1Complement = AnalogPrototypes.CompleteIntegral(System.Math.Sqrt((1 - k1) * (1 + k1)));

        return (int)System.Math.Ceiling(kIntegral * k1Complement / (kComplement * k1Integral));
    }

    /// <summary>Which cost the bandstop edge search minimises.</summary>
    private enum BandstopCost
    {
        Butterworth,
        Chebyshev,
    }

    /// <summary>
    /// The passband edge of a bandstop specification is not given by the caller — a bandstop's two
    /// edges do not have to be symmetric about the centre, so MATLAB searches for the edge that
    /// makes the required order smallest and reports the design at that edge.
    /// </summary>
    private static double MinimiseEdge(int index, double[] wp, double[] ws, double rs, double rp, BandstopCost cost)
    {
        double low = index == 0 ? wp[0] : ws[1] + 1e-12;
        double high = index == 0 ? ws[0] - 1e-12 : wp[1];

        var trial = new double[2];
        double Cost(double value)
        {
            trial[0] = wp[0];
            trial[1] = wp[1];
            trial[index] = value;
            double wa = System.Math.Min(
                System.Math.Abs(ws[0] * (trial[0] - trial[1]) / ((ws[0] * ws[0]) - (trial[0] * trial[1]))),
                System.Math.Abs(ws[1] * (trial[0] - trial[1]) / ((ws[1] * ws[1]) - (trial[0] * trial[1]))));

            return cost == BandstopCost.Butterworth
                ? System.Math.Log10((System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1)
                    / (System.Math.Pow(10, 0.1 * System.Math.Abs(rp)) - 1)) / (2 * System.Math.Log10(wa))
                : System.Math.Acosh(System.Math.Sqrt(
                    (System.Math.Pow(10, 0.1 * System.Math.Abs(rs)) - 1)
                    / (System.Math.Pow(10, 0.1 * System.Math.Abs(rp)) - 1))) / System.Math.Acosh(wa);
        }

        return BoundedMinimizer.Minimize(Cost, low, high).Solution;
    }

    /// <summary>The worst-case selectivity a specification demands, which is what fixes the order.</summary>
    private static double Selectivity(EdgeShape shape, double[] wp, double[] ws)
    {
        double[] wa = shape switch
        {
            EdgeShape.Lowpass => [ws[0] / wp[0]],
            EdgeShape.Highpass => [wp[0] / ws[0]],
            EdgeShape.Bandstop =>
            [
                ws[0] * (wp[0] - wp[1]) / ((ws[0] * ws[0]) - (wp[0] * wp[1])),
                ws[1] * (wp[0] - wp[1]) / ((ws[1] * ws[1]) - (wp[0] * wp[1])),
            ],
            _ =>
            [
                ((ws[0] * ws[0]) - (wp[0] * wp[1])) / (ws[0] * (wp[0] - wp[1])),
                ((ws[1] * ws[1]) - (wp[0] * wp[1])) / (ws[1] * (wp[0] - wp[1])),
            ],
        };

        double smallest = double.PositiveInfinity;
        foreach (double value in wa)
        {
            smallest = System.Math.Min(smallest, System.Math.Abs(value));
        }

        return smallest;
    }

    private static double[] BandstopEdges(double[] wp, double w0)
    {
        double gap = wp[1] - wp[0];
        double root = System.Math.Sqrt((gap * gap) + (4 * w0 * w0 * wp[0] * wp[1]));
        double[] wn = [(gap + root) / (2 * w0), (gap - root) / (2 * w0)];
        return Sorted(System.Math.Abs(wn[0]), System.Math.Abs(wn[1]));
    }

    private static double[] BandpassEdges(double[] wp, double w0)
    {
        double gap = wp[1] - wp[0];
        double[] wn =
        [
            (w0 * gap / 2) + System.Math.Sqrt((w0 * w0 / 4 * gap * gap) + (wp[0] * wp[1])),
            (-w0 * gap / 2) + System.Math.Sqrt((w0 * w0 / 4 * gap * gap) + (wp[0] * wp[1])),
        ];

        return Sorted(System.Math.Abs(wn[0]), System.Math.Abs(wn[1]));
    }

    /// <summary>The two band edges <c>cheb2ord</c> derives from one, held together by their product.</summary>
    private static double[] PairedEdges(double[] wp, double newWp, bool stop)
    {
        double gap = wp[0] - wp[1];
        double first = stop
            ? (gap * newWp / 2) + System.Math.Sqrt((gap * gap * newWp * newWp / 4) + (wp[0] * wp[1]))
            : (gap / (2 * newWp)) + System.Math.Sqrt((gap * gap / (4 * newWp * newWp)) + (wp[0] * wp[1]));
        return [first, wp[0] * wp[1] / first];
    }

    private static double[] Sorted(double a, double b) => a <= b ? [a, b] : [b, a];

    /// <summary>Checks the edges and pre-warps them, exactly as <c>freqchk</c> and its callers do.</summary>
    private static (EdgeShape Shape, double[] Passband, double[] Stopband) Prepare(
        string name, ReadOnlySpan<double> passband, ReadOnlySpan<double> stopband, double rp, double rs, bool analog)
    {
        if (passband.Length != stopband.Length || passband.Length is not (1 or 2))
        {
            throw new ArgumentException(
                $"{name} needs Wp and Ws to be either both scalars or both two-element vectors.", nameof(passband));
        }

        if (!analog)
        {
            foreach (double w in passband)
            {
                CheckDigital(name, w);
            }

            foreach (double w in stopband)
            {
                CheckDigital(name, w);
            }
        }

        // No check that the stopband attenuation exceeds the passband ripple: MATLAB does not make
        // one either, and answers whatever the formula gives for a specification that asks for less
        // rejection than it tolerates ripple. Refusing here would be stricter than the thing being
        // matched, which is a divergence rather than a kindness.
        int shape = 2 * (passband.Length - 1);
        shape += passband[0] < stopband[0] ? 1 : 2;

        var wp = new double[passband.Length];
        var ws = new double[stopband.Length];
        for (int i = 0; i < wp.Length; i++)
        {
            wp[i] = analog ? passband[i] : System.Math.Tan(System.Math.PI * passband[i] / 2);
            ws[i] = analog ? stopband[i] : System.Math.Tan(System.Math.PI * stopband[i] / 2);
        }

        return ((EdgeShape)shape, wp, ws);
    }

    private static void CheckDigital(string name, double w)
    {
        if (w <= 0 || w >= 1)
        {
            throw new ArgumentException(
                $"{name}'s digital frequencies must lie strictly between 0 and 1 (1 = Nyquist).", nameof(w));
        }
    }

    /// <summary>An analogue edge turned back into a normalised digital one.</summary>
    private static double[] Digitalise(ReadOnlySpan<double> w)
    {
        var result = new double[w.Length];
        for (int i = 0; i < w.Length; i++)
        {
            result[i] = 2 * System.Math.Atan(w[i]) / System.Math.PI;
        }

        return result;
    }

    /// <summary>A pre-warped edge mapped back through the bilinear transform, unless the call was analogue.</summary>
    private static double[] Denormalise(double[] wn, bool analog)
    {
        if (analog)
        {
            return wn;
        }

        var result = new double[wn.Length];
        for (int i = 0; i < wn.Length; i++)
        {
            result[i] = 2 / System.Math.PI * System.Math.Atan(wn[i]);
        }

        return result;
    }

    private static string Describe(FilterBandType type) => type switch
    {
        FilterBandType.LowPass => "lowpass",
        FilterBandType.HighPass => "highpass",
        FilterBandType.BandPass => "bandpass",
        _ => "bandstop",
    };
}
