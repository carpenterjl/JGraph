using System.Numerics;

namespace JGraph.Signal;

/// <summary>The band a cascade design is asked for.</summary>
public enum CascadeBand
{
    /// <summary>Lowpass.</summary>
    Lowpass,

    /// <summary>Highpass.</summary>
    Highpass,

    /// <summary>Bandpass.</summary>
    Bandpass,

    /// <summary>Bandstop.</summary>
    Bandstop,
}

/// <summary>Which classical prototype a cascade is built on.</summary>
public enum CascadeMethod
{
    /// <summary>Butterworth: the half-power frequency is the specification.</summary>
    Butterworth,

    /// <summary>Chebyshev type I: the passband edge and its ripple are the specification.</summary>
    Chebyshev1,

    /// <summary>Chebyshev type II: the stopband edge and its attenuation are the specification.</summary>
    Chebyshev2,

    /// <summary>Elliptic: passband edge, ripple and attenuation together.</summary>
    Elliptic,
}

/// <summary>Which band a minimum-order design is asked to meet exactly.</summary>
public enum MatchBand
{
    /// <summary>Meet the passband exactly and exceed the stopband.</summary>
    Passband,

    /// <summary>Meet the stopband exactly and exceed the passband.</summary>
    Stopband,

    /// <summary>Meet both, which only an elliptic design can do.</summary>
    Both,
}

/// <summary>
/// The classical IIR designs as MATLAB's <c>designfilt</c> builds them: a cascade of second-order
/// sections with a scale value per section, arrived at by closed form rather than by factorising a
/// transfer function (M135).
/// </summary>
/// <remarks>
/// <para>
/// M134 already designs all five classical filters, and designs them well — but it designs them as
/// zeros, poles and a gain, and a <c>digitalFilter</c>'s <c>Coefficients</c> is a second-order
/// section matrix. Those are not the same object seen two ways. Factorising a transfer function into
/// biquads leaves the section order and the distribution of gain free, and MATLAB's answers to both
/// come from neither <c>zp2sos</c> nor any of its ordering rules: they fall out of a closed form that
/// writes each biquad's coefficients directly from the prototype's angles. So the sections are
/// built here the way the reference builds them, one formula per band per method, and the pole-pair
/// order, the exact <c>2</c> in a lowpass numerator, and the per-section gain all follow.
/// </para>
/// <para>
/// The shape is the same every time. A digital specification becomes an analogue lowpass one — the
/// bilinear pre-warp for a lowpass or highpass, the band's own <c>c</c> parameter for a bandpass or
/// bandstop — and a minimum-order request first becomes an order and a re-stated edge. Then one
/// closed form turns that analogue specification into biquads that are already digital: the bilinear
/// transform is inside the formulae, not a step after them. A bandpass or bandstop section comes
/// from a fourth-order block that is split by finding its roots, which is the one place where the
/// answer depends on a root finder's ordering rather than on arithmetic.
/// </para>
/// </remarks>
public static class CascadeDesign
{
    /// <summary>A designed cascade: the sections row by row, and the scale value each carries.</summary>
    /// <remarks>
    /// The scale values are held apart from the sections, as the reference holds them. There is one
    /// per section, and sometimes one more: a design whose overall gain does not belong to any
    /// section puts it at the end, where <see cref="SecondOrderSections.ScaleSections"/> spreads it.
    /// </remarks>
    public readonly record struct Cascade(double[,] Sections, double[] ScaleValues);

    /// <summary>The digital specification a cascade design starts from.</summary>
    /// <param name="Band">Which band.</param>
    /// <param name="Method">Which prototype.</param>
    /// <param name="Order">The filter order, or zero to ask for the least order that meets the specification.</param>
    /// <param name="Edges">
    /// The frequencies the order-given specification names, normalised so that 1 is Nyquist: the
    /// half-power frequency for a Butterworth, the passband edge for a Chebyshev I or elliptic, the
    /// stopband edge for a Chebyshev II. For a minimum-order design these are the passband edges.
    /// </param>
    /// <param name="StopEdges">The stopband edges, which only a minimum-order design names.</param>
    /// <param name="PassbandRipple">The passband ripple in dB.</param>
    /// <param name="StopbandAttenuation">The stopband attenuation in dB.</param>
    /// <param name="Match">Which band a minimum-order design meets exactly.</param>
    public sealed record Request(
        CascadeBand Band,
        CascadeMethod Method,
        int Order,
        double[] Edges,
        double[] StopEdges,
        double PassbandRipple,
        double StopbandAttenuation,
        MatchBand Match);

    /// <summary>An analogue lowpass specification: an order, one frequency, and the two levels.</summary>
    /// <remarks>
    /// The one frequency means different things to different methods — a cutoff to a Butterworth, a
    /// passband edge to a Chebyshev I or elliptic, a stopband edge to a Chebyshev II — which is why
    /// it has no better name than <c>W</c>. That is also how the reference's four analogue
    /// specification classes differ from one another, and no more than that.
    /// </remarks>
    private readonly record struct AnalogSpec(int Order, double W, double Apass, double Astop);

    /// <summary>Designs the cascade the request asks for.</summary>
    public static Cascade Design(Request request)
    {
        double c = BandParameter(request);
        AnalogSpec analog = request.Order > 0 ? OrderGiven(request, c) : Minimum(request, c);
        Cascade cascade = Sections(request, analog, c);

        // A cascade always carries one more scale value than it has sections — the last one is the
        // gain that belongs to the filter rather than to any section, and it is one whenever no
        // design put anything there. Padding here rather than in each closed form is what keeps the
        // formulae readable and the list the length every reader of it expects.
        return cascade.ScaleValues.Length > cascade.Sections.GetLength(0)
            ? cascade
            : cascade with { ScaleValues = WithTrailing(cascade.ScaleValues, 1) };
    }

    /// <summary>The least order that meets a specification, and the frequency the order was found at.</summary>
    /// <remarks>
    /// This is what a minimum-order design settles on before any coefficient exists, and it is worth
    /// having on its own: a caller that only wants to know how long the filter will be — the
    /// convenience verbs do — should not have to design it to find out.
    /// </remarks>
    public static int MinimumOrder(Request request)
    {
        double c = BandParameter(request);
        return Minimum(request, c).Order;
    }

    // --- Digital specification to analogue specification ----------------------------------------

    /// <summary>
    /// The <c>c</c> parameter a bandpass or bandstop design turns on, or NaN for the bands that have
    /// none.
    /// </summary>
    /// <remarks>
    /// It is read from whichever pair of edges the specification set names — the half-power pair for
    /// a Butterworth, the passband pair for a Chebyshev I or elliptic, the stopband pair for a
    /// Chebyshev II — except that a minimum-order design always reads it from the passband pair,
    /// even a Chebyshev II one.
    /// </remarks>
    private static double BandParameter(Request request)
    {
        if (request.Band is not (CascadeBand.Bandpass or CascadeBand.Bandstop))
        {
            return double.NaN;
        }

        // Whatever pair the specification set names is the pair the parameter comes from, and
        // Edges always holds it: the half-power pair, the passband pair, or — for an order-given
        // Chebyshev II — the stopband pair.
        double f1 = request.Edges[0];
        double f2 = request.Edges[1];
        return System.Math.Sin(System.Math.PI * (f1 + f2))
            / (System.Math.Sin(System.Math.PI * f1) + System.Math.Sin(System.Math.PI * f2));
    }

    /// <summary>The analogue specification an order-given digital one maps to.</summary>
    private static AnalogSpec OrderGiven(Request request, double c)
    {
        double w = request.Band switch
        {
            CascadeBand.Lowpass => System.Math.Tan(System.Math.PI * request.Edges[0] / 2),
            CascadeBand.Highpass => Cotangent(System.Math.PI * request.Edges[0] / 2),
            // A Butterworth bandpass reads only the upper edge and keeps its sign; the other three
            // take the smaller of the two magnitudes, which is the edge that binds.
            CascadeBand.Bandpass => request.Method == CascadeMethod.Butterworth
                ? BandpassEdge(request.Edges[1], c)
                : System.Math.Min(
                    System.Math.Abs(BandpassEdge(request.Edges[0], c)),
                    System.Math.Abs(BandpassEdge(request.Edges[1], c))),
            _ => request.Method == CascadeMethod.Butterworth
                ? System.Math.Abs(BandstopEdge(request.Edges[1], c))
                : System.Math.Min(
                    System.Math.Abs(BandstopEdge(request.Edges[0], c)),
                    System.Math.Abs(BandstopEdge(request.Edges[1], c))),
        };

        return new AnalogSpec(request.Order, w, request.PassbandRipple, request.StopbandAttenuation);
    }

    /// <summary>The analogue specification a minimum-order request maps to, order and all.</summary>
    private static AnalogSpec Minimum(Request request, double c)
    {
        (double wp, double ws) = request.Band switch
        {
            CascadeBand.Lowpass => (
                System.Math.Tan(System.Math.PI * request.Edges[0] / 2),
                System.Math.Tan(System.Math.PI * request.StopEdges[0] / 2)),
            CascadeBand.Highpass => (
                Cotangent(System.Math.PI * request.Edges[0] / 2),
                Cotangent(System.Math.PI * request.StopEdges[0] / 2)),
            CascadeBand.Bandpass => (
                System.Math.Abs(BandpassEdge(request.Edges[1], c)),
                System.Math.Min(
                    System.Math.Abs(BandpassEdge(request.StopEdges[0], c)),
                    System.Math.Abs(BandpassEdge(request.StopEdges[1], c)))),
            _ => (
                System.Math.Abs(BandstopEdge(request.Edges[1], c)),
                System.Math.Min(
                    System.Math.Abs(BandstopEdge(request.StopEdges[0], c)),
                    System.Math.Abs(BandstopEdge(request.StopEdges[1], c)))),
        };

        bool doubled = request.Band is CascadeBand.Bandpass or CascadeBand.Bandstop;
        double rp = request.PassbandRipple;
        double rs = request.StopbandAttenuation;
        return request.Method switch
        {
            CascadeMethod.Butterworth => ButterworthOrder(wp, ws, rp, rs, request.Match, doubled),
            CascadeMethod.Chebyshev1 => Chebyshev1Order(wp, ws, rp, rs, request.Match, doubled),
            CascadeMethod.Chebyshev2 => Chebyshev2Order(wp, ws, rp, rs, request.Match, doubled),
            _ => EllipticOrder(wp, ws, rp, rs, request.Match, doubled),
        };
    }

    /// <summary>The bandpass map from a digital edge to the analogue lowpass one.</summary>
    private static double BandpassEdge(double f, double c) =>
        (c - System.Math.Cos(System.Math.PI * f)) / System.Math.Sin(System.Math.PI * f);

    /// <summary>The bandstop map, which is the bandpass one turned over.</summary>
    private static double BandstopEdge(double f, double c) =>
        System.Math.Sin(System.Math.PI * f) / (System.Math.Cos(System.Math.PI * f) - c);

    private static double Cotangent(double x) => 1 / System.Math.Tan(x);

    // --- Minimum order, one rule per prototype -------------------------------------------------

    private static AnalogSpec ButterworthOrder(
        double wp, double ws, double rp, double rs, MatchBand match, bool doubled)
    {
        double ratio = System.Math.Sqrt(
            (System.Math.Pow(10, rs / 10) - 1) / (System.Math.Pow(10, rp / 10) - 1));
        int n = Order(System.Math.Log(ratio) / System.Math.Log(ws / wp));

        // The cutoff is placed against whichever band is being met exactly; the other one is then
        // exceeded, which is what an integer order buys.
        double wc = match == MatchBand.Passband
            ? wp / System.Math.Pow(System.Math.Sqrt(System.Math.Pow(10, rp / 10) - 1), 1.0 / n)
            : ws / System.Math.Pow(System.Math.Sqrt(System.Math.Pow(10, rs / 10) - 1), 1.0 / n);
        return new AnalogSpec(doubled ? 2 * n : n, wc, rp, rs);
    }

    private static AnalogSpec Chebyshev1Order(
        double wp, double ws, double rp, double rs, MatchBand match, bool doubled)
    {
        double ratio = System.Math.Sqrt(
            (System.Math.Pow(10, rs / 10) - 1) / (System.Math.Pow(10, rp / 10) - 1));
        double w = ws / wp;
        int n = Order(Acosh(ratio) / Acosh(w));

        // Meeting the stopband exactly means restating the ripple the integer order actually gives.
        if (match == MatchBand.Stopband)
        {
            double cosine = System.Math.Cosh(n * Acosh(w));
            rp = 10 * System.Math.Log10(1 + ((System.Math.Pow(10, rs / 10) - 1) / (cosine * cosine)));
        }

        return new AnalogSpec(doubled ? 2 * n : n, wp, rp, rs);
    }

    private static AnalogSpec Chebyshev2Order(
        double wp, double ws, double rp, double rs, MatchBand match, bool doubled)
    {
        double ratio = System.Math.Sqrt(
            (System.Math.Pow(10, rs / 10) - 1) / (System.Math.Pow(10, rp / 10) - 1));
        double w = ws / wp;
        int n = Order(Acosh(ratio) / Acosh(w));

        if (match == MatchBand.Passband)
        {
            double cosine = System.Math.Cosh(n * Acosh(w));
            rs = 10 * System.Math.Log10(1 + ((System.Math.Pow(10, rp / 10) - 1) * cosine * cosine));
        }

        return new AnalogSpec(doubled ? 2 * n : n, ws, rp, rs);
    }

    private static AnalogSpec EllipticOrder(
        double wp, double ws, double rp, double rs, MatchBand match, bool doubled)
    {
        double wc = System.Math.Sqrt(wp * ws);
        double q = SelectivityNome(wp / wc);
        double d = (System.Math.Pow(10, 0.1 * rs) - 1) / (System.Math.Pow(10, 0.1 * rp) - 1);
        int n = Order(System.Math.Log10(16 * d) / System.Math.Log10(1 / q));

        switch (match)
        {
            case MatchBand.Passband:
                rs = 10 * System.Math.Log10(
                    ((System.Math.Pow(10, 0.1 * rp) - 1) / (16 * System.Math.Pow(q, n))) + 1);
                break;
            case MatchBand.Stopband:
                rp = 10 * System.Math.Log10(
                    (16 * System.Math.Pow(q, n) * (System.Math.Pow(10, 0.1 * rs) - 1)) + 1);
                break;
            default:
                break;
        }

        return new AnalogSpec(doubled ? 2 * n : n, wp, rp, rs);
    }

    /// <summary>
    /// The nome of the modulus a selectivity implies, by the series the reference uses rather than
    /// by the complete integrals.
    /// </summary>
    internal static double SelectivityNome(double wp)
    {
        double k = wp * wp;
        double k1 = System.Math.Sqrt(1 - (k * k));
        double root = System.Math.Sqrt(k1);
        double q0 = 0.5 * (1 - root) / (1 + root);
        return q0
            + (2 * System.Math.Pow(q0, 5))
            + (15 * System.Math.Pow(q0, 9))
            + (150 * System.Math.Pow(q0, 13));
    }

    private static int Order(double exact)
    {
        int n = (int)System.Math.Ceiling(exact);
        if (n <= 0)
        {
            throw new ArgumentException(
                "The design specifications cannot be met: the filter order cannot be determined.");
        }

        return n;
    }

    private static double Acosh(double x) => System.Math.Log(x + System.Math.Sqrt((x * x) - 1));

    // --- The closed forms ----------------------------------------------------------------------

    private static Cascade Sections(Request request, AnalogSpec analog, double c) =>
        request.Method switch
        {
            CascadeMethod.Butterworth => Butterworth(request.Band, analog, c),
            CascadeMethod.Chebyshev1 => Chebyshev1(request.Band, analog, c),
            CascadeMethod.Chebyshev2 => Chebyshev2(request.Band, analog, c),
            _ => Elliptic(request.Band, analog, c),
        };

    /// <summary>The cosines of the stable prototype poles' angles, which every classic form starts from.</summary>
    private static double[] CosineAngles(int order)
    {
        int half = order / 2;
        var cs = new double[half];
        for (int k = 1; k <= half; k++)
        {
            double theta = System.Math.PI / (2 * order) * (order - 1 + (2 * k));
            cs[k - 1] = System.Math.Cos(theta);
        }

        return cs;
    }

    /// <summary>The sines of the same angles, which the two Chebyshev forms also need.</summary>
    private static (double[] Cosines, double[] Sines) Angles(int order)
    {
        int half = order / 2;
        var cs = new double[half];
        var ss = new double[half];
        for (int k = 1; k <= half; k++)
        {
            double theta = System.Math.PI / (2 * order) * (order - 1 + (2 * k));
            cs[k - 1] = System.Math.Cos(theta);
            ss[k - 1] = System.Math.Sin(theta);
        }

        return (cs, ss);
    }

    /// <summary>An empty cascade of the right height, with the ones a section always carries.</summary>
    private static (double[,] Sections, double[] Scales) InitialiseLowHigh(int order)
    {
        int sections = (order + 1) / 2;
        int full = order / 2;
        var s = new double[sections, 6];
        for (int i = 0; i < sections; i++)
        {
            s[i, 0] = 1;
            s[i, 3] = 1;
        }

        // Every full section's numerator ends in one; a trailing first-order section's does not,
        // which is the only way the two are told apart later.
        for (int i = 0; i < full; i++)
        {
            s[i, 2] = 1;
        }

        var g = new double[sections];
        Array.Fill(g, 1.0);
        return (s, g);
    }

    /// <summary>
    /// The bandpass and bandstop skeleton: each fourth-order block's denominator split into two
    /// biquads by finding its roots, and its gain split by taking a square root.
    /// </summary>
    private static (double[,] Sections, double[] Scales) InitialiseBand(
        int order, double[] a1, double[] a2, double[] a3, double[] a4, double[] blockGain)
    {
        if (order % 2 != 0)
        {
            throw new ArgumentException("A bandpass or bandstop cascade needs an even order.");
        }

        int sections = order / 2;
        int full = 2 * (order / 4);
        var s = new double[sections, 6];
        for (int i = 0; i < sections; i++)
        {
            s[i, 0] = 1;
            s[i, 3] = 1;
        }

        var g = new double[sections];
        Array.Fill(g, 1.0);

        for (int k = 0; k < full; k += 2)
        {
            int block = k / 2;
            ((double p1, double q1), (double p2, double q2)) =
                QuarticFactors(a1[block], a2[block], a3[block], a4[block]);
            s[k, 4] = p1;
            s[k, 5] = q1;
            s[k + 1, 4] = p2;
            s[k + 1, 5] = q2;
        }

        for (int block = 0; block < full / 2; block++)
        {
            double half = System.Math.Sqrt(blockGain[block]);
            g[2 * block] = half;
            g[(2 * block) + 1] = half;
        }

        return (s, g);
    }

    /// <summary>
    /// A real quartic split into its two real quadratic factors, ordered by the magnitude of the
    /// roots each holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference splits this quartic by calling a general root finder and taking the roots two
    /// at a time in the order they came back. That order is a property of the eigensolver rather
    /// than of the polynomial, and this repository has two of those — so following the reference
    /// literally would make a bandpass cascade’s section order depend on which linear-algebra
    /// backend is loaded, which is not a difference a filter is allowed to have.
    /// </para>
    /// <para>
    /// So the roots are found here, by a fixed-point iteration that is the same arithmetic in every
    /// lane, and the two factors are ordered by the magnitude of the roots they hold, smallest first
    /// — the gentler section ahead of the sharper one, which is the order that keeps a cascade’s
    /// intermediate signals in range.
    /// Each factor is built from a root and its conjugate, so its coefficients are real by
    /// construction rather than by rounding a small imaginary part away.
    /// </para>
    /// </remarks>
    private static ((double P, double Q) First, (double P, double Q) Second) QuarticFactors(
        double a1, double a2, double a3, double a4)
    {
        Complex[] roots = QuarticRoots(a1, a2, a3, a4);

        // Each root is paired with whichever of the others is nearest its own conjugate, which for a
        // real quartic with two conjugate pairs is exactly the pairing that makes both factors real.
        var used = new bool[4];
        var factors = new List<(double P, double Q, double Magnitude)>();
        for (int i = 0; i < 4; i++)
        {
            if (used[i])
            {
                continue;
            }

            int partner = -1;
            double best = double.PositiveInfinity;
            for (int j = i + 1; j < 4; j++)
            {
                if (used[j])
                {
                    continue;
                }

                double distance = Complex.Abs(roots[j] - Complex.Conjugate(roots[i]));
                if (distance < best)
                {
                    best = distance;
                    partner = j;
                }
            }

            used[i] = true;
            used[partner] = true;
            double real = (roots[i].Real + roots[partner].Real) / 2;
            double magnitude = (Complex.Abs(roots[i]) + Complex.Abs(roots[partner])) / 2;
            factors.Add((-2 * real, magnitude * magnitude, magnitude));
        }

        factors.Sort(static (x, y) => x.Magnitude.CompareTo(y.Magnitude));
        return ((factors[0].P, factors[0].Q), (factors[1].P, factors[1].Q));
    }

    /// <summary>
    /// The four roots of a monic real quartic, by the Weierstrass iteration from fixed starting
    /// points — the same arithmetic in every lane, which is the point of it.
    /// </summary>
    private static Complex[] QuarticRoots(double a1, double a2, double a3, double a4)
    {
        Complex[] c = [Complex.One, a1, a2, a3, a4];
        var z = new Complex[4];
        Complex seed = new(0.4, 0.9);
        z[0] = Complex.One;
        for (int i = 1; i < 4; i++)
        {
            z[i] = z[i - 1] * seed;
        }

        for (int step = 0; step < 200; step++)
        {
            double moved = 0;
            for (int i = 0; i < 4; i++)
            {
                Complex numerator = Horner(c, z[i]);
                Complex denominator = Complex.One;
                for (int j = 0; j < 4; j++)
                {
                    if (j != i)
                    {
                        denominator *= z[i] - z[j];
                    }
                }

                if (denominator == Complex.Zero)
                {
                    continue;
                }

                Complex delta = numerator / denominator;
                z[i] -= delta;
                moved = System.Math.Max(moved, Complex.Abs(delta));
            }

            if (moved < 1e-16)
            {
                break;
            }
        }

        return z;
    }

    /// <summary>
    /// The larger-magnitude root of the monic quadratic <c>z² + a·z + b</c>, written so that the
    /// smaller one is best recovered as <c>b</c> divided by it.
    /// </summary>
    private static double LeadingRoot(double a, double b)
    {
        double discriminant = (a * a) - (4 * b);
        double root = System.Math.Sqrt(System.Math.Abs(discriminant));
        return a >= 0 ? (-a - root) / 2 : (-a + root) / 2;
    }

    private static Complex Horner(Complex[] coefficients, Complex x)
    {
        Complex sum = Complex.Zero;
        foreach (Complex value in coefficients)
        {
            sum = (sum * x) + value;
        }

        return sum;
    }

    /// <summary>
    /// The two numerator factors of a fourth-order block written onto its two sections, each against
    /// the denominator whose poles sit nearest its own zeros.
    /// </summary>
    /// <remarks>
    /// A cascade is the same filter however its zeros are shared out among its poles, but it is not
    /// the same arithmetic. A section that holds a pole pair close to the unit circle together with a
    /// zero pair on the far side of the band has a gain of thousands, and everything downstream of it
    /// — a <c>filtfilt</c> transient above all — carries that. Pairing each pole pair with the zero
    /// pair nearest it in angle is the standard remedy and the one <c>zp2sos</c> uses.
    /// </remarks>
    private static void PairByAngle(double[,] sections, int at, double first, double second)
    {
        double poleFirst = FactorAngle(sections[at, 4], sections[at, 5]);
        double poleSecond = FactorAngle(sections[at + 1, 4], sections[at + 1, 5]);
        double zeroFirst = FactorAngle(first, 1);
        double zeroSecond = FactorAngle(second, 1);

        bool straight = System.Math.Abs(poleFirst - zeroFirst) + System.Math.Abs(poleSecond - zeroSecond)
            <= System.Math.Abs(poleFirst - zeroSecond) + System.Math.Abs(poleSecond - zeroFirst);

        sections[at, 0] = 1;
        sections[at, 1] = straight ? first : second;
        sections[at, 2] = 1;
        sections[at + 1, 0] = 1;
        sections[at + 1, 1] = straight ? second : first;
        sections[at + 1, 2] = 1;
    }

    /// <summary>
    /// The angle of the roots of <c>z² + p·z + q</c>, or zero when they are real — which is all the
    /// pairing rule needs of them.
    /// </summary>
    private static double FactorAngle(double p, double q)
    {
        double magnitude = System.Math.Sqrt(System.Math.Abs(q));
        return magnitude == 0
            ? 0
            : System.Math.Acos(System.Math.Clamp(-p / (2 * magnitude), -1, 1));
    }

    /// <summary>The two lower coefficients of <c>(z − r₁)(z − r₂)</c>, taken as real.</summary>
    private static (double First, double Second) PairPolynomial(Complex r1, Complex r2) =>
        (-(r1 + r2).Real, (r1 * r2).Real);

    /// <summary>Grows the scale-value list by one, for the gain that belongs to no section.</summary>
    private static double[] WithTrailing(double[] g, double value)
    {
        var grown = new double[g.Length + 1];
        Array.Copy(g, grown, g.Length);
        grown[^1] = value;
        return grown;
    }

    // --- Butterworth ---------------------------------------------------------------------------

    private static Cascade Butterworth(CascadeBand band, AnalogSpec analog, double c)
    {
        if (band is CascadeBand.Lowpass or CascadeBand.Highpass)
        {
            (double[,] s, double[] g) = ButterworthLowpass(analog);
            if (band == CascadeBand.Highpass)
            {
                FlipSigns(s);
            }

            return new Cascade(s, g);
        }

        int n = analog.Order;
        double wc = analog.W;
        double[] cs = CosineAngles(n / 2);
        int blocks = cs.Length;
        var a1 = new double[blocks];
        var a2 = new double[blocks];
        var a3 = new double[blocks];
        var a4 = new double[blocks];
        var fog = new double[blocks];
        double wc2 = wc * wc;
        for (int i = 0; i < blocks; i++)
        {
            double wccs = wc * cs[i];
            double den = 1 - (2 * wccs) + wc2;
            if (band == CascadeBand.Bandpass)
            {
                a1[i] = 4 * c * (wccs - 1) / den;
                a2[i] = 2 * ((2 * c * c) + 1 - wc2) / den;
                a3[i] = -4 * c * (wccs + 1) / den;
            }
            else
            {
                a1[i] = 4 * c * wc * (cs[i] - wc) / den;
                a2[i] = 2 * ((2 * c * c * wc2) + wc2 - 1) / den;
                a3[i] = -4 * c * wc * (cs[i] + wc) / den;
            }

            a4[i] = (1 + (2 * wccs) + wc2) / den;
            fog[i] = wc2 / den;
        }

        (double[,] sections, double[] scales) = InitialiseBand(n, a1, a2, a3, a4, fog);
        FillBandNumerators(sections, n, band, c);
        if (n % 4 != 0)
        {
            OddBandTail(sections, scales, band, c, wc);
        }

        return new Cascade(sections, scales);
    }

    private static (double[,] Sections, double[] Scales) ButterworthLowpass(AnalogSpec analog)
    {
        int n = analog.Order;
        double wc = analog.W;
        (double[,] s, double[] g) = InitialiseLowHigh(n);
        int full = n / 2;
        double[] cs = CosineAngles(n);
        double wcsq = wc * wc;
        for (int i = 0; i < full; i++)
        {
            double wc2cs = 2 * wc * cs[i];
            double den = 1 - wc2cs + wcsq;
            s[i, 1] = 2;
            s[i, 4] = 2 * (wcsq - 1) / den;
            s[i, 5] = (1 + wc2cs + wcsq) / den;
            g[i] = wcsq / den;
        }

        if (n % 2 != 0)
        {
            s[full, 1] = 1;
            s[full, 4] = (wc - 1) / (wc + 1);
            g[full] = wc / (wc + 1);
        }

        return (s, g);
    }

    // --- Chebyshev type I ----------------------------------------------------------------------

    private static Cascade Chebyshev1(CascadeBand band, AnalogSpec analog, double c)
    {
        if (band is CascadeBand.Lowpass or CascadeBand.Highpass)
        {
            (double[,] s, double[] g) = Chebyshev1Lowpass(analog);
            if (band == CascadeBand.Highpass)
            {
                FlipSigns(s);
            }

            return new Cascade(s, g);
        }

        int n = analog.Order;
        double rp = analog.Apass;
        (double[] num, double[] a1, double w0) = Chebyshev1Coefficients(n / 2, analog.W, rp);
        int blocks = num.Length;
        var ai1 = new double[blocks];
        var ai2 = new double[blocks];
        var ai3 = new double[blocks];
        var ai4 = new double[blocks];
        var fog = new double[blocks];
        for (int i = 0; i < blocks; i++)
        {
            double den = 1 + a1[i] + num[i];
            if (band == CascadeBand.Bandpass)
            {
                ai1[i] = 4 * c * ((-a1[i] / 2) - 1) / den;
                ai2[i] = 2 * ((2 * c * c) + 1 - num[i]) / den;
                ai3[i] = -2 * c * (2 - a1[i]) / den;
            }
            else
            {
                ai1[i] = ((-2 * c * a1[i]) - (4 * c * num[i])) / den;
                ai2[i] = 2 * ((2 * c * c * num[i]) + num[i] - 1) / den;
                ai3[i] = 2 * c * (a1[i] - (2 * num[i])) / den;
            }

            ai4[i] = (1 + num[i] - a1[i]) / den;
            fog[i] = num[i] / den;
        }

        (double[,] sections, double[] scales) = InitialiseBand(n, ai1, ai2, ai3, ai4, fog);
        FillBandNumerators(sections, n, band, c);
        if (n % 4 != 0)
        {
            OddBandTail(sections, scales, band, c, w0);
            return new Cascade(sections, scales);
        }

        return new Cascade(sections, WithTrailing(scales, System.Math.Sqrt(System.Math.Pow(10, -rp / 10))));
    }

    private static (double[,] Sections, double[] Scales) Chebyshev1Lowpass(AnalogSpec analog)
    {
        int n = analog.Order;
        double rp = analog.Apass;
        (double[,] s, double[] g) = InitialiseLowHigh(n);
        int full = n / 2;
        (double[] num, double[] a1, double w0) = Chebyshev1Coefficients(n, analog.W, rp);
        for (int i = 0; i < full; i++)
        {
            double den = 1 + a1[i] + num[i];
            s[i, 1] = 2;
            s[i, 4] = 2 * (num[i] - 1) / den;
            s[i, 5] = (1 - a1[i] + num[i]) / den;
            g[i] = num[i] / den;
        }

        if (n % 2 != 0)
        {
            s[full, 1] = 1;
            s[full, 4] = (w0 - 1) / (w0 + 1);
            g[full] = w0 / (w0 + 1);
            return (s, g);
        }

        return (s, WithTrailing(g, System.Math.Sqrt(System.Math.Pow(10, -rp / 10))));
    }

    private static (double[] Num, double[] A1, double W0) Chebyshev1Coefficients(
        int order, double wp, double rp)
    {
        (double[] cs, double[] ss) = Angles(order);
        double a = 1.0 / order * Asinh(1 / System.Math.Sqrt(System.Math.Pow(10, rp / 10) - 1));
        double w0 = wp * System.Math.Sinh(a);
        var num = new double[cs.Length];
        var a1 = new double[cs.Length];
        for (int i = 0; i < cs.Length; i++)
        {
            double wi = wp * ss[i];
            num[i] = (w0 * w0) + (wi * wi);
            a1[i] = -2 * w0 * cs[i];
        }

        return (num, a1, w0);
    }

    // --- Chebyshev type II ---------------------------------------------------------------------

    private static Cascade Chebyshev2(CascadeBand band, AnalogSpec analog, double c)
    {
        if (band is CascadeBand.Lowpass or CascadeBand.Highpass)
        {
            (double[,] s, double[] g) = Chebyshev2Lowpass(analog);
            if (band == CascadeBand.Highpass)
            {
                FlipSigns(s);
            }

            return new Cascade(s, g);
        }

        int n = analog.Order;
        (double[] b0, double[] a1, double[] a0, double w0, double[] c0) =
            Chebyshev2Coefficients(n / 2, analog.W, analog.Astop);
        int blocks = b0.Length;
        var ai1 = new double[blocks];
        var ai2 = new double[blocks];
        var ai3 = new double[blocks];
        var ai4 = new double[blocks];
        var fog = new double[blocks];
        var bi1 = new double[blocks];
        var bi2 = new double[blocks];
        for (int i = 0; i < blocks; i++)
        {
            double den = 1 + a1[i] + a0[i];
            if (band == CascadeBand.Bandpass)
            {
                ai1[i] = 4 * c * (c0[i] - a0[i]) / den;
                ai2[i] = 2 * ((a0[i] * ((2 * c * c) + 1)) - 1) / den;
                ai3[i] = -4 * c * (c0[i] + a0[i]) / den;
            }
            else
            {
                ai1[i] = -4 * c * (1 - c0[i]) / den;
                ai2[i] = 2 * ((2 * c * c) + 1 - a0[i]) / den;
                ai3[i] = -4 * c * (1 + c0[i]) / den;
            }

            ai4[i] = (1 - a1[i] + a0[i]) / den;
            fog[i] = (1 + b0[i]) / den;

            double nden = 1 + b0[i];
            if (band == CascadeBand.Bandpass)
            {
                bi1[i] = -4 * c * b0[i] / nden;
                bi2[i] = 2 * ((b0[i] * ((2 * c * c) + 1)) - 1) / nden;
            }
            else
            {
                bi1[i] = -4 * c / nden;
                bi2[i] = 2 * ((2 * c * c) + 1 - b0[i]) / nden;
            }
        }

        (double[,] sections, double[] scales) = InitialiseBand(n, ai1, ai2, ai3, ai4, fog);

        // The numerator of a Chebyshev II band section carries its own zeros, so it is split the
        // same way the denominator was — but by a quadratic in the sum of a root and its reciprocal,
        // which is what makes the second section's middle coefficient a division rather than a root.
        int full = 2 * (n / 4);
        for (int k = 0; k < full; k += 2)
        {
            int block = k / 2;
            double first = LeadingRoot(-bi1[block], bi2[block] - 2);
            PairByAngle(sections, k, first, (bi2[block] - 2) / first);
        }

        if (n % 4 != 0)
        {
            OddBandTail(sections, scales, band, c, w0);
        }

        return new Cascade(sections, scales);
    }

    private static (double[,] Sections, double[] Scales) Chebyshev2Lowpass(AnalogSpec analog)
    {
        int n = analog.Order;
        (double[,] s, double[] g) = InitialiseLowHigh(n);
        int full = n / 2;
        (double[] b0, double[] a1, double[] a0, double w0, _) =
            Chebyshev2Coefficients(n, analog.W, analog.Astop);
        for (int i = 0; i < full; i++)
        {
            s[i, 1] = 2 * (1 - b0[i]) / (1 + b0[i]);
            double den = 1 + a1[i] + a0[i];
            s[i, 4] = 2 * (1 - a0[i]) / den;
            s[i, 5] = (1 - a1[i] + a0[i]) / den;
            g[i] = (1 + b0[i]) / den;
        }

        if (n % 2 != 0)
        {
            s[full, 1] = 1;
            s[full, 4] = (w0 - 1) / (w0 + 1);
            g[full] = w0 / (w0 + 1);
        }

        return (s, g);
    }

    private static (double[] B0, double[] A1, double[] A0, double W0, double[] C0) Chebyshev2Coefficients(
        int order, double ws, double rs)
    {
        (double[] cs, double[] ss) = Angles(order);
        double a = 1.0 / order * Asinh(System.Math.Sqrt(System.Math.Pow(10, rs / 10) - 1));
        double w0 = ws / System.Math.Sinh(a);
        var b0 = new double[cs.Length];
        var a0 = new double[cs.Length];
        var a1 = new double[cs.Length];
        var c0 = new double[cs.Length];
        for (int i = 0; i < cs.Length; i++)
        {
            double wi = ws / ss[i];
            b0[i] = 1 / (wi * wi);
            a0[i] = b0[i] + (1 / (w0 * w0));
            c0[i] = cs[i] / w0;
            a1[i] = -2 * c0[i];
        }

        return (b0, a1, a0, w0, c0);
    }

    // --- Elliptic ------------------------------------------------------------------------------

    private static Cascade Elliptic(CascadeBand band, AnalogSpec analog, double c)
    {
        int n = analog.Order;
        bool wide = band is CascadeBand.Bandpass or CascadeBand.Bandstop;
        int prototype = wide ? n / 2 : n;
        (double[,] sa, double[] ga) =
            EllipticAnalogSections(prototype, analog.W, analog.Apass, analog.Astop);

        if (!wide)
        {
            (double[,] s, double[] g) = EllipticLowpass(n, sa, ga);
            if (band == CascadeBand.Highpass)
            {
                FlipSigns(s);
            }

            return new Cascade(s, g);
        }

        return EllipticBand(band, n, sa, ga, c);
    }

    /// <summary>
    /// The analogue elliptic prototype as second-order sections, normalised the way the reference
    /// normalises it and then scaled to the passband edge.
    /// </summary>
    /// <remarks>
    /// The rows come out of <c>ellipap2</c>'s numerator and denominator tables read backwards, so
    /// each row is <c>[s², s, 1]</c> for the numerator and the same for the denominator; both are
    /// divided through by their constant term, which is why the two share their <c>s²</c>
    /// coefficient exactly and why a later formula can take either one for both. The first row is a
    /// first-order section or a bare gain, and it is rotated to the end because that is where the
    /// cascade wants it.
    /// </remarks>
    private static (double[,] Sections, double[] Gains) EllipticAnalogSections(
        int order, double wp, double rp, double rs)
    {
        (double[,] b, double[,] a) = AnalogPrototypes.EllipticSections(order, rp, rs);
        int rows = a.GetLength(0);
        // The reference starts this list one shorter than the section count and lets an odd order
        // grow it back by writing past the end; here it is allocated at its final length instead.
        var g = new double[System.Math.Max(rows - 1, 1)];
        Array.Fill(g, 1.0);
        if (order % 2 == 1)
        {
            g[0] = 1 / a[0, 1];
            double lead = a[0, 1];
            for (int j = 0; j < 3; j++)
            {
                a[0, j] /= lead;
            }
        }
        else
        {
            g[0] = b[0, 0];
        }

        if (order > 1)
        {
            for (int i = 1; i < rows; i++)
            {
                double da = a[i, 2];
                g[i - 1] /= da;
                for (int j = 0; j < 3; j++)
                {
                    a[i, j] /= da;
                }

                double db = b[i, 2];
                g[i - 1] *= db;
                for (int j = 0; j < 3; j++)
                {
                    b[i, j] /= db;
                }
            }
        }

        int keep = order % 2 == 0 && order > 1 ? rows - 1 : rows;
        int from = rows - keep;
        var sos = new double[keep, 6];
        for (int i = 0; i < keep; i++)
        {
            // Rotated by one so that the first-order row lands last, and each half reversed so that
            // the highest power of s comes first.
            int source = from + ((i + 1) % keep);
            for (int j = 0; j < 3; j++)
            {
                sos[i, j] = b[source, 2 - j];
                sos[i, j + 3] = a[source, 2 - j];
            }
        }

        double wc2 = wp * wp;
        for (int i = 0; i < keep; i++)
        {
            sos[i, 0] /= wc2;
            sos[i, 3] /= wc2;
            sos[i, 4] /= wp;
        }

        return (sos, g);
    }

    private static (double[,] Sections, double[] Scales) EllipticLowpass(
        int order, double[,] sa, double[] ga)
    {
        (double[,] s, double[] g) = InitialiseLowHigh(order);
        int sections = (order + 1) / 2;
        int full = order / 2;
        for (int k = 0; k < full; k++)
        {
            double den = sa[k, 3] + sa[k, 4] + sa[k, 5];
            double b0 = (sa[k, 0] + sa[k, 2]) / den;
            double b1 = 2 * (sa[k, 2] - sa[k, 0]) / den;
            double a1 = 2 * (sa[k, 5] - sa[k, 0]) / den;
            double a2 = (sa[k, 5] + sa[k, 3] - sa[k, 4]) / den;
            s[k, 1] = b1 / b0;
            s[k, 4] = a1;
            s[k, 5] = a2;
            g[k] = b0 * ga[k];
        }

        if (order % 2 != 0)
        {
            int last = sections - 1;
            double den = sa[last, 4] + sa[last, 5];
            s[last, 0] = 1;
            s[last, 1] = 1;
            s[last, 4] = (sa[last, 5] - sa[last, 4]) / den;
            g[last] = sa[last, 2] / den;
        }

        if (order == 1)
        {
            g[0] *= ga[0];
        }

        return (s, g);
    }

    private static Cascade EllipticBand(
        CascadeBand band, int order, double[,] sa, double[] ga, double c)
    {
        if (order == 2)
        {
            // A second-order bandpass or bandstop has no fourth-order block at all: the tail below
            // is the whole filter.
            var only = new double[1, 6];
            var scale = new double[1] { 1 };
            OddEllipticTail(only, scale, band, sa, c, 0);
            scale[0] *= ga[0];
            return new Cascade(only, scale);
        }

        int blocks = order / 4;
        var ai1 = new double[blocks];
        var ai2 = new double[blocks];
        var ai3 = new double[blocks];
        var ai4 = new double[blocks];
        var bi0 = new double[blocks];
        var bi1 = new double[blocks];
        var bi2 = new double[blocks];
        var fog = new double[blocks];
        for (int k = 0; k < blocks; k++)
        {
            double den = sa[k, 3] + sa[k, 4] + sa[k, 5];
            double lead = band == CascadeBand.Bandpass ? sa[k, 0] : sa[k, 5];
            double other = band == CascadeBand.Bandpass ? sa[k, 5] : sa[k, 3];
            ai1[k] = -((4 * c * lead) + (2 * c * sa[k, 4])) / den;
            ai2[k] = ((lead * (2 + (4 * c * c))) - (2 * other)) / den;
            ai3[k] = ((-4 * c * lead) + (2 * c * sa[k, 4])) / den;
            ai4[k] = (sa[k, 3] - sa[k, 4] + sa[k, 5]) / den;
            bi0[k] = (sa[k, 0] + sa[k, 2]) / den;

            double numeratorLead = band == CascadeBand.Bandpass ? sa[k, 0] : sa[k, 2];
            double numeratorOther = band == CascadeBand.Bandpass ? sa[k, 2] : sa[k, 0];
            bi1[k] = -4 * c * numeratorLead / den;
            bi2[k] = ((numeratorLead * (2 + (4 * c * c))) - (2 * numeratorOther)) / den;
            fog[k] = bi0[k] * ga[k];
        }

        (double[,] sections, double[] scales) = InitialiseBand(order, ai1, ai2, ai3, ai4, fog);

        for (int k = 0; k < 2 * blocks; k += 2)
        {
            int block = k / 2;
            ((double first, _), (double second, _)) = QuarticFactors(
                bi1[block] / bi0[block], bi2[block] / bi0[block], bi1[block] / bi0[block], 1);
            PairByAngle(sections, k, first, second);
        }

        if (order % 4 != 0)
        {
            OddEllipticTail(sections, scales, band, sa, c, sections.GetLength(0) - 1);
        }

        return new Cascade(sections, scales);
    }

    /// <summary>The first-order tail an elliptic band design grows when its order is not a multiple of four.</summary>
    private static void OddEllipticTail(
        double[,] sections, double[] scales, CascadeBand band, double[,] sa, double c, int at)
    {
        int last = sa.GetLength(0) - 1;
        double den = sa[last, 4] + sa[last, 5];
        if (band == CascadeBand.Bandpass)
        {
            sections[at, 0] = 1;
            sections[at, 1] = 0;
            sections[at, 2] = -1;
            sections[at, 3] = 1;
            sections[at, 4] = -2 * c * sa[last, 4] / den;
            sections[at, 5] = (sa[last, 4] - sa[last, 5]) / den;
        }
        else
        {
            sections[at, 0] = 1;
            sections[at, 1] = -2 * c;
            sections[at, 2] = 1;
            sections[at, 3] = 1;
            sections[at, 4] = -2 * c * sa[last, 5] / den;
            sections[at, 5] = (sa[last, 5] - sa[last, 4]) / den;
        }

        scales[at] = sa[last, 2] / den;
    }

    // --- Shared tails --------------------------------------------------------------------------

    /// <summary>Every full band section's numerator, which the band alone decides.</summary>
    private static void FillBandNumerators(double[,] sections, int order, CascadeBand band, double c)
    {
        int full = 2 * (order / 4);
        for (int i = 0; i < full; i++)
        {
            sections[i, 0] = 1;
            sections[i, 1] = band == CascadeBand.Bandpass ? 0 : -2 * c;
            sections[i, 2] = band == CascadeBand.Bandpass ? -1 : 1;
        }
    }

    /// <summary>
    /// The tail section a band design grows from the prototype's own first-order section, when the
    /// order leaves one over.
    /// </summary>
    private static void OddBandTail(
        double[,] sections, double[] scales, CascadeBand band, double c, double w)
    {
        int at = sections.GetLength(0) - 1;
        if (band == CascadeBand.Bandpass)
        {
            sections[at, 0] = 1;
            sections[at, 1] = 0;
            sections[at, 2] = -1;
            sections[at, 3] = 1;
            sections[at, 4] = -2 * c / (w + 1);
            sections[at, 5] = (1 - w) / (w + 1);
        }
        else
        {
            sections[at, 0] = 1;
            sections[at, 1] = -2 * c;
            sections[at, 2] = 1;
            sections[at, 3] = 1;
            sections[at, 4] = -2 * c * w / (w + 1);
            sections[at, 5] = -(1 - w) / (w + 1);
        }

        scales[at] = w / (w + 1);
    }

    /// <summary>
    /// A lowpass cascade turned into the highpass one: the odd-power coefficients change sign, which
    /// is the whole of <c>z → −z</c> on a biquad.
    /// </summary>
    private static void FlipSigns(double[,] sections)
    {
        for (int i = 0; i < sections.GetLength(0); i++)
        {
            sections[i, 1] = -sections[i, 1];
            sections[i, 4] = -sections[i, 4];
        }
    }

    private static double Asinh(double x) => System.Math.Log(x + System.Math.Sqrt((x * x) + 1));
}
