namespace JGraph.Numerics;

/// <summary>
/// The Bessel functions of real order and real argument — <c>J</c>, <c>Y</c>, <c>I</c>, <c>K</c>,
/// the Hankel functions, and the Airy pair — together with their first derivatives.
/// </summary>
/// <remarks>
/// <para>
/// Everything here rests on two routines, <see cref="CylinderJy"/> and <see cref="CylinderIk"/>,
/// which use Steed's method: a continued fraction for the logarithmic derivative, a second fraction
/// (or, for small argument, Temme's series) for the normalization, and stable recurrence to carry
/// the answer to the order asked for. That combination holds close to full double precision over the
/// whole real order/argument plane, which the tractable single-formula approaches — a power series,
/// or a Hankel asymptotic expansion — do not: each of those leaves a band in the middle where most
/// of the digits are gone.
/// </para>
/// <para>
/// The <c>I</c>/<c>K</c> routine works in exponentially scaled terms internally and applies
/// e<sup>±x</sup> only at the end, so <c>besselk(0, 800)</c> is a small number rather than a
/// underflowed zero and its scaled form is exact.
/// </para>
/// </remarks>
public static class BesselFunctions
{
    /// <summary>The Euler–Mascheroni constant, which is where the Temme series starts.</summary>
    private const double EulerGamma = 0.577215664901532860606512090082;

    /// <summary>The smallest number the fractions may divide by without losing control.</summary>
    private const double Tiny = 1e-300;

    /// <summary>The relative accuracy every iteration here is driven to.</summary>
    private const double Epsilon = 1e-16;

    /// <summary>Iteration cap; the fractions converge in far fewer, and this only bounds the damage.</summary>
    private const int MaxIterations = 12000;

    /// <summary>
    /// Below this argument the normalizations come from Temme's series rather than a second
    /// continued fraction, because the fraction converges too slowly to be worth it there.
    /// </summary>
    private const double SeriesLimit = 2.0;

    // --- The public functions ---------------------------------------------------------------------

    /// <summary>J<sub>ν</sub>(x), the Bessel function of the first kind.</summary>
    public static double J(double nu, double x) => FirstKind(nu, x, wantY: false);

    /// <summary>Y<sub>ν</sub>(x), the Bessel function of the second kind (Weber's function).</summary>
    public static double Y(double nu, double x) => FirstKind(nu, x, wantY: true);

    /// <summary>
    /// I<sub>ν</sub>(x), the modified Bessel function of the first kind. With
    /// <paramref name="scaled"/> the result is e<sup>-|x|</sup>I<sub>ν</sub>(x), which stays finite
    /// where the function itself overflows.
    /// </summary>
    public static double I(double nu, double x, bool scaled = false) => Modified(nu, x, wantK: false, scaled);

    /// <summary>
    /// K<sub>ν</sub>(x), the modified Bessel function of the second kind. With
    /// <paramref name="scaled"/> the result is e<sup>x</sup>K<sub>ν</sub>(x), which stays finite
    /// where the function itself underflows to zero.
    /// </summary>
    public static double K(double nu, double x, bool scaled = false) => Modified(nu, x, wantK: true, scaled);

    /// <summary>
    /// The Hankel function H<sub>ν</sub><sup>(1)</sup>(x) = J + iY when <paramref name="kind"/> is 1,
    /// and H<sub>ν</sub><sup>(2)</sup>(x) = J − iY when it is 2.
    /// </summary>
    public static System.Numerics.Complex H(double nu, int kind, double x)
    {
        if (kind is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A Hankel function is of kind 1 or 2.");
        }

        return new System.Numerics.Complex(J(nu, x), kind == 1 ? Y(nu, x) : -Y(nu, x));
    }

    /// <summary>
    /// The Airy functions. <paramref name="kind"/> selects Ai (0), Ai′ (1), Bi (2), or Bi′ (3).
    /// With <paramref name="scaled"/>, Ai and Ai′ carry a factor e<sup>⅔x^{3/2}</sup> and Bi and Bi′
    /// a factor e<sup>−|Re ⅔x^{3/2}|</sup>, exactly as MATLAB defines the scaled forms.
    /// </summary>
    public static double Airy(int kind, double x, bool scaled = false)
    {
        if (kind is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The Airy kind is 0 (Ai), 1 (Ai'), 2 (Bi), or 3 (Bi').");
        }

        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        const double OneOverRoot3 = 0.5773502691896257645091488;
        double absolute = Math.Abs(x);
        double root = Math.Sqrt(absolute);
        double z = 2.0 / 3.0 * absolute * root;

        if (x == 0)
        {
            // Γ-function values at the origin; the scaling factor is 1 there either way.
            return kind switch
            {
                0 => 0.355028053887817239260,
                1 => -0.258819403792806798405,
                2 => 0.614926627446000735150,
                _ => 0.448288357353826357691,
            };
        }

        if (x > 0)
        {
            // Ai and Bi come from K and I of order ⅓ (the functions themselves) or ⅔ (the
            // derivatives). Working scaled means Ai stays representable past x = 104, where the
            // unscaled function underflows.
            double order = kind is 0 or 2 ? 1.0 / 3.0 : 2.0 / 3.0;
            CylinderIk(z, order, out double ri, out double rk, out _, out _, scaledOutput: true);

            // ri and rk are e^-z·I and e^z·K. Ai and Bi differ in their front factor: √(x/3) against
            // √x for the functions, and −x/√3 against x for the derivatives.
            if (kind is 0 or 1)
            {
                // Ai = front·K/π. MATLAB scales it by e^z, which is exactly the factor rk carries,
                // so the scaled answer is the one that needs no exponential at all.
                double scaledAi = (kind == 0 ? root * OneOverRoot3 : -x * OneOverRoot3) * rk / Math.PI;
                return scaled ? scaledAi : scaledAi * Math.Exp(-z);
            }

            // Bi = √(x/3)·(K/π + 2I/√3), and MATLAB scales it by e^-z — the reciprocal of Ai's
            // factor, because here it is the growing I term that has to be tamed. Written in the
            // scaled quantities the K half then carries e^-2z, which underflows to nothing for large
            // z, as it should: that term is negligible against I there.
            double bi = kind == 2 ? root : x;
            double scaledBi = bi * (Math.Exp(-2.0 * z) * rk / Math.PI + 2.0 * OneOverRoot3 * ri);
            return scaled ? scaledBi : bi * (Math.Exp(-z) * rk / Math.PI + 2.0 * OneOverRoot3 * Math.Exp(z) * ri);
        }

        // For negative argument both Airy functions oscillate, so they come from J and Y instead
        // and there is nothing exponential to scale away.
        {
            double order = kind is 0 or 2 ? 1.0 / 3.0 : 2.0 / 3.0;
            CylinderJy(z, order, out double rj, out double ry, out _, out _);

            return kind switch
            {
                0 => 0.5 * root * (rj - OneOverRoot3 * ry),
                1 => 0.5 * absolute * (OneOverRoot3 * ry + rj),
                2 => -0.5 * root * (ry + OneOverRoot3 * rj),
                _ => 0.5 * absolute * (OneOverRoot3 * rj - ry),
            };
        }
    }

    // --- Order and argument bookkeeping -------------------------------------------------------------

    /// <summary>
    /// J and Y for any real order, reflecting a negative order onto a positive one. The reflection
    /// formulas mix J and Y, so both are computed and one is picked — which costs nothing, since
    /// <see cref="CylinderJy"/> produces the pair anyway.
    /// </summary>
    private static double FirstKind(double nu, double x, bool wantY)
    {
        if (double.IsNaN(nu) || double.IsNaN(x))
        {
            return double.NaN;
        }

        bool wholeOrder = nu == Math.Floor(nu);

        if (x < 0)
        {
            // J_n(-x) = (-1)^n J_n(x) and likewise for Y at whole order. At fractional order the
            // answer is genuinely complex, which a real result cannot report.
            if (!wholeOrder)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x), x, "A Bessel function of fractional order is complex for negative argument.");
            }

            double reflected = FirstKind(nu, -x, wantY);
            return Math.Abs(nu) % 2 == 0 ? reflected : -reflected;
        }

        if (x == 0)
        {
            if (wantY)
            {
                return double.NegativeInfinity;
            }

            return nu == 0 ? 1.0
                : nu > 0 ? 0.0
                : wholeOrder ? 0.0 : double.PositiveInfinity;
        }

        if (nu >= 0)
        {
            CylinderJy(x, nu, out double j, out double y, out _, out _);
            return wantY ? y : j;
        }

        double positive = -nu;

        if (wholeOrder)
        {
            // J_-n = (-1)^n J_n, Y_-n = (-1)^n Y_n. The general reflection reduces to this, but
            // sin(nπ) is not exactly zero in floating point, so it is taken separately.
            CylinderJy(x, positive, out double jn, out double yn, out _, out _);
            double value = wantY ? yn : jn;
            return positive % 2 == 0 ? value : -value;
        }

        {
            CylinderJy(x, positive, out double j, out double y, out _, out _);
            double sin = Math.Sin(Math.PI * positive);
            double cos = Math.Cos(Math.PI * positive);
            return wantY ? j * sin + y * cos : j * cos - y * sin;
        }
    }

    /// <summary>I and K for any real order. K is even in its order; I is not, and picks up a K term.</summary>
    private static double Modified(double nu, double x, bool wantK, bool scaled)
    {
        if (double.IsNaN(nu) || double.IsNaN(x))
        {
            return double.NaN;
        }

        // K_-ν = K_ν exactly, so the order can be folded before anything else happens.
        double order = wantK ? Math.Abs(nu) : nu;
        bool wholeOrder = order == Math.Floor(order);

        if (x < 0)
        {
            if (wantK || !wholeOrder)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x), x, "A modified Bessel function is complex for negative argument except I at whole order.");
            }

            double reflected = Modified(order, -x, wantK: false, scaled);
            return Math.Abs(order) % 2 == 0 ? reflected : -reflected;
        }

        if (x == 0)
        {
            if (wantK)
            {
                return double.PositiveInfinity;
            }

            return order == 0 ? 1.0
                : order > 0 ? 0.0
                : wholeOrder ? 0.0 : double.PositiveInfinity;
        }

        if (order >= 0)
        {
            CylinderIk(x, order, out double i, out double k, out _, out _, scaled);
            return wantK ? k : i;
        }

        {
            // I_-ν = I_ν + (2/π)·sin(νπ)·K_ν. Both terms are wanted in the same scaling, and the
            // scaled forms differ by e^2x, so the reflection is done unscaled and rescaled after.
            double positive = -order;
            CylinderIk(x, positive, out double i, out double k, out _, out _, scaledOutput: true);
            double sum = i + 2.0 / Math.PI * Math.Sin(Math.PI * positive) * k * Math.Exp(-2.0 * x);
            return scaled ? sum : sum * Math.Exp(x);
        }
    }

    // --- Steed's method for J and Y -----------------------------------------------------------------

    /// <summary>
    /// J<sub>ν</sub>, Y<sub>ν</sub> and their derivatives for x &gt; 0 and ν ≥ 0.
    /// </summary>
    /// <remarks>
    /// The order is split as ν = μ + n with |μ| ≤ ½, where the normalizations are cheap; the answer
    /// is then carried back up to ν by recurrence, downward for J (stable in that direction) and
    /// upward for Y (stable in the other).
    /// </remarks>
    private static void CylinderJy(double x, double nu, out double j, out double y, out double jp, out double yp)
    {
        int steps = x < SeriesLimit ? (int)Math.Floor(nu + 0.5) : Math.Max(0, (int)(nu - x + 1.5));
        double mu = nu - steps;
        double mu2 = mu * mu;
        double xi = 1.0 / x;
        double xi2 = 2.0 * xi;
        double w = xi2 / Math.PI;

        double f = LogarithmicDerivativeJ(x, nu, xi2, steps, xi, out double startJ, out double startJp, out double muJ);

        double jMu, yMu, y1;

        if (x < SeriesLimit)
        {
            TemmeSeriesJy(x, mu, mu2, xi2, out yMu, out y1);
            double yMuPrime = mu * xi * yMu - y1;

            // The Wronskian J·Y' − J'·Y = 2/(πx) fixes the scale of J once Y is known.
            jMu = w / (yMuPrime - f * yMu);
        }
        else
        {
            SteedFraction(x, mu2, xi, out double p, out double q);
            double gamma = (p - f) / q;
            jMu = Math.Sqrt(w / ((p - f) * gamma + q));
            jMu = muJ < 0 ? -Math.Abs(jMu) : Math.Abs(jMu);
            yMu = jMu * gamma;
            y1 = mu * xi * yMu - yMu * (p + q / gamma);
        }

        double scale = jMu / muJ;
        j = startJ * scale;
        jp = startJp * scale;

        // Y goes up in order, which is the stable direction for it.
        for (int i = 1; i <= steps; i++)
        {
            double next = (mu + i) * xi2 * y1 - yMu;
            yMu = y1;
            y1 = next;
        }

        y = yMu;
        yp = nu * xi * yMu - y1;
    }

    /// <summary>
    /// The continued fraction for J′<sub>ν</sub>/J<sub>ν</sub>, then the downward recurrence to
    /// order μ. The unnormalized J and J′ at both ends come back with it, since the caller needs the
    /// starting pair to rescale and the μ pair to work out the scale.
    /// </summary>
    private static double LogarithmicDerivativeJ(
        double x, double nu, double xi2, int steps, double xi, out double startJ, out double startJp, out double muJ)
    {
        double h = Math.Max(nu * xi, Tiny);
        double b = xi2 * nu;
        double d = 0.0;
        double c = h;
        int sign = 1;

        for (int i = 1; i <= MaxIterations; i++)
        {
            b += xi2;
            d = b - d;
            if (Math.Abs(d) < Tiny) { d = Tiny; }

            c = b - 1.0 / c;
            if (Math.Abs(c) < Tiny) { c = Tiny; }

            d = 1.0 / d;
            double delta = c * d;
            h *= delta;

            // Each sign change in d is a sign change in the unnormalized J being tracked.
            if (d < 0) { sign = -sign; }

            if (Math.Abs(delta - 1.0) < Epsilon)
            {
                break;
            }
        }

        double value = sign * Tiny;
        double derivative = h * value;
        startJ = value;
        startJp = derivative;

        double factor = nu * xi;
        for (int l = steps; l >= 1; l--)
        {
            double next = factor * value + derivative;
            factor -= xi;
            derivative = factor * next - value;
            value = next;
        }

        muJ = value == 0 ? Epsilon : value;
        return derivative / muJ;
    }

    /// <summary>
    /// The second (complex) continued fraction of Steed's method, evaluated in real arithmetic. It
    /// returns p + iq, the logarithmic derivative of the Hankel function, which together with
    /// J′/J determines both normalizations.
    /// </summary>
    private static void SteedFraction(double x, double mu2, double xi, out double p, out double q)
    {
        double a = 0.25 - mu2;
        p = -0.5 * xi;
        q = 1.0;
        double br = 2.0 * x;
        double bi = 2.0;
        double factor = a * xi / (p * p + q * q);
        double cr = br + q * factor;
        double ci = bi + p * factor;
        double den = br * br + bi * bi;
        double dr = br / den;
        double di = -bi / den;
        double dlr = cr * dr - ci * di;
        double dli = cr * di + ci * dr;
        double temp = p * dlr - q * dli;
        q = p * dli + q * dlr;
        p = temp;

        for (int i = 2; i <= MaxIterations; i++)
        {
            a += 2 * (i - 1);
            bi += 2.0;
            dr = a * dr + br;
            di = a * di + bi;
            if (Math.Abs(dr) + Math.Abs(di) < Tiny) { dr = Tiny; }

            factor = a / (cr * cr + ci * ci);
            cr = br + cr * factor;
            ci = bi - ci * factor;
            if (Math.Abs(cr) + Math.Abs(ci) < Tiny) { cr = Tiny; }

            den = dr * dr + di * di;
            dr /= den;
            di /= -den;
            dlr = cr * dr - ci * di;
            dli = cr * di + ci * dr;
            temp = p * dlr - q * dli;
            q = p * dli + q * dlr;
            p = temp;

            if (Math.Abs(dlr - 1.0) + Math.Abs(dli) < Epsilon)
            {
                break;
            }
        }
    }

    /// <summary>Temme's series for Y<sub>μ</sub> and Y<sub>μ+1</sub> at small argument.</summary>
    private static void TemmeSeriesJy(double x, double mu, double mu2, double xi2, out double yMu, out double y1)
    {
        double half = 0.5 * x;
        double piMu = Math.PI * mu;
        double factor = Math.Abs(piMu) < Epsilon ? 1.0 : piMu / Math.Sin(piMu);
        double logHalf = -Math.Log(half);
        double e = mu * logHalf;
        double sinhc = Math.Abs(e) < Epsilon ? 1.0 : Math.Sinh(e) / e;

        GammaPair(mu, out double gam1, out double gam2, out double gammaPlus, out double gammaMinus);

        double ff = 2.0 / Math.PI * factor * (gam1 * Math.Cosh(e) + gam2 * sinhc * logHalf);
        double exponential = Math.Exp(e);
        double p = exponential / (Math.PI * gammaPlus);
        double q = 1.0 / (exponential * Math.PI * gammaMinus);
        double halfPiMu = 0.5 * piMu;
        double sinc = Math.Abs(halfPiMu) < Epsilon ? 1.0 : Math.Sin(halfPiMu) / halfPiMu;
        double r = Math.PI * halfPiMu * sinc * sinc;
        double c = 1.0;
        double d = -half * half;
        double sum = ff + r * q;
        double sum1 = p;

        for (int i = 1; i <= MaxIterations; i++)
        {
            ff = (i * ff + p + q) / (i * i - mu2);
            c *= d / i;
            p /= i - mu;
            q /= i + mu;
            double delta = c * (ff + r * q);
            sum += delta;
            sum1 += c * p - i * delta;

            if (Math.Abs(delta) < (1.0 + Math.Abs(sum)) * Epsilon)
            {
                break;
            }
        }

        yMu = -sum;
        y1 = -sum1 * xi2;
    }

    // --- Steed's method for I and K ------------------------------------------------------------------

    /// <summary>
    /// I<sub>ν</sub>, K<sub>ν</sub> and their derivatives for x &gt; 0 and ν ≥ 0. With
    /// <paramref name="scaledOutput"/> the results are e<sup>-x</sup>I and e<sup>x</sup>K.
    /// </summary>
    /// <remarks>
    /// The working is scaled throughout and the exponentials are applied once at the end, so a large
    /// argument neither overflows I nor underflows K on the way to an answer that is representable.
    /// </remarks>
    private static void CylinderIk(
        double x, double nu, out double i, out double k, out double ip, out double kp, bool scaledOutput)
    {
        int steps = (int)(nu + 0.5);
        double mu = nu - steps;
        double mu2 = mu * mu;
        double xi = 1.0 / x;
        double xi2 = 2.0 * xi;

        double f = LogarithmicDerivativeI(x, nu, xi2, steps, xi, out double startI, out double startIp, out double muI);

        double kMu, k1;

        if (x < SeriesLimit)
        {
            TemmeSeriesIk(x, mu, mu2, xi2, out kMu, out k1);

            // The series gives K itself; the rest of this routine works in e^x·K terms.
            double lift = Math.Exp(x);
            kMu *= lift;
            k1 *= lift;
        }
        else
        {
            SteedFractionK(x, mu, mu2, xi, out kMu, out k1);
        }

        double kMuPrime = mu * xi * kMu - k1;

        // The Wronskian I·K' − I'·K = -1/x fixes I's scale. Both sides are in scaled terms, and the
        // e^x·e^-x cancels, so this is the scaled I at order μ.
        double iMu = xi / (f * kMu - kMuPrime);

        double scale = iMu / muI;
        i = startI * scale;
        ip = startIp * scale;

        // K goes up in order; it grows there, which is the stable direction.
        for (int step = 1; step <= steps; step++)
        {
            double next = (mu + step) * xi2 * k1 + kMu;
            kMu = k1;
            k1 = next;
        }

        k = kMu;
        kp = nu * xi * kMu - k1;

        if (!scaledOutput)
        {
            double decay = Math.Exp(-x);
            double growth = Math.Exp(x);
            i *= growth;
            ip *= growth;
            k *= decay;
            kp *= decay;
        }
    }

    /// <summary>The continued fraction for I′<sub>ν</sub>/I<sub>ν</sub>, with the recurrence to μ.</summary>
    private static double LogarithmicDerivativeI(
        double x, double nu, double xi2, int steps, double xi, out double startI, out double startIp, out double muI)
    {
        double h = Math.Max(nu * xi, Tiny);
        double b = xi2 * nu;
        double d = 0.0;
        double c = h;

        for (int i = 1; i <= MaxIterations; i++)
        {
            b += xi2;
            d = 1.0 / (b + d);
            c = b + 1.0 / c;
            double delta = c * d;
            h *= delta;

            if (Math.Abs(delta - 1.0) < Epsilon)
            {
                break;
            }
        }

        double value = Tiny;
        double derivative = h * value;
        startI = value;
        startIp = derivative;

        double factor = nu * xi;
        for (int l = steps; l >= 1; l--)
        {
            double next = factor * value + derivative;
            factor -= xi;
            derivative = factor * next + value;
            value = next;
        }

        muI = value;
        return derivative / value;
    }

    /// <summary>
    /// The continued fraction for K at large argument, returning e<sup>x</sup>K<sub>μ</sub> and
    /// e<sup>x</sup>K<sub>μ+1</sub>. Keeping the e<sup>−x</sup> out of the arithmetic is what makes
    /// the scaled form exact rather than an underflowed zero multiplied back up.
    /// </summary>
    private static void SteedFractionK(double x, double mu, double mu2, double xi, out double kMu, out double k1)
    {
        double b = 2.0 * (1.0 + x);
        double d = 1.0 / b;
        double delh = d;
        double h = d;
        double q1 = 0.0;
        double q2 = 1.0;
        double a1 = 0.25 - mu2;
        double q = a1;
        double c = a1;
        double a = -a1;
        double s = 1.0 + q * delh;

        for (int i = 2; i <= MaxIterations; i++)
        {
            a -= 2 * (i - 1);
            c = -a * c / i;
            double next = (q1 - b * q2) / a;
            q1 = q2;
            q2 = next;
            q += c * next;
            b += 2.0;
            d = 1.0 / (b + a * d);
            delh = (b * d - 1.0) * delh;
            h += delh;
            double increment = q * delh;
            s += increment;

            if (Math.Abs(increment / s) < Epsilon)
            {
                break;
            }
        }

        h *= a1;
        kMu = Math.Sqrt(Math.PI / (2.0 * x)) / s;
        k1 = kMu * (mu + x + 0.5 - h) * xi;
    }

    /// <summary>Temme's series for K<sub>μ</sub> and K<sub>μ+1</sub> at small argument.</summary>
    private static void TemmeSeriesIk(double x, double mu, double mu2, double xi2, out double kMu, out double k1)
    {
        double half = 0.5 * x;
        double piMu = Math.PI * mu;
        double factor = Math.Abs(piMu) < Epsilon ? 1.0 : piMu / Math.Sin(piMu);
        double logHalf = -Math.Log(half);
        double e = mu * logHalf;
        double sinhc = Math.Abs(e) < Epsilon ? 1.0 : Math.Sinh(e) / e;

        GammaPair(mu, out double gam1, out double gam2, out double gammaPlus, out double gammaMinus);

        double ff = factor * (gam1 * Math.Cosh(e) + gam2 * sinhc * logHalf);
        double sum = ff;
        double exponential = Math.Exp(e);
        double p = 0.5 * exponential / gammaPlus;
        double q = 0.5 / (exponential * gammaMinus);
        double c = 1.0;
        double d = half * half;
        double sum1 = p;

        for (int i = 1; i <= MaxIterations; i++)
        {
            ff = (i * ff + p + q) / (i * i - mu2);
            c *= d / i;
            p /= i - mu;
            q /= i + mu;
            double delta = c * ff;
            sum += delta;
            sum1 += c * (p - i * ff);

            if (Math.Abs(delta) < Math.Abs(sum) * Epsilon)
            {
                break;
            }
        }

        kMu = sum;
        k1 = sum1 * xi2;
    }

    // --- The gamma quantities the Temme series needs -------------------------------------------------

    /// <summary>
    /// The four values Temme's series is written in terms of, for |x| ≤ ½:
    /// Γ₊ = 1/Γ(1+x), Γ₋ = 1/Γ(1−x), γ₂ = (Γ₋+Γ₊)/2, and γ₁ = (Γ₋−Γ₊)/2x.
    /// </summary>
    /// <remarks>
    /// γ₁ is the awkward one: written out, it is a difference of two numbers that both tend to 1,
    /// so it loses a digit for every decade x is below 1. Rewriting it as
    /// Γ₋ − Γ₊ = 2·e<sup>−s</sup>·sinh(d) with s and d the half sum and half difference of the two
    /// log-gammas removes the cancellation entirely — d ≈ −γx is itself computed as a sum of
    /// same-signed terms, and s, which does cancel, is only ever exponentiated to something near 1,
    /// where an absolute error of an ulp does not matter.
    /// </remarks>
    private static void GammaPair(double x, out double gam1, out double gam2, out double gammaPlus, out double gammaMinus)
    {
        if (x == 0)
        {
            gam1 = -EulerGamma;
            gam2 = 1.0;
            gammaPlus = 1.0;
            gammaMinus = 1.0;
            return;
        }

        double logPlus = SpecialFunctions.LogGamma(1.0 + x);
        double logMinus = SpecialFunctions.LogGamma(1.0 - x);

        gammaPlus = Math.Exp(-logPlus);
        gammaMinus = Math.Exp(-logMinus);
        gam2 = 0.5 * (gammaMinus + gammaPlus);

        double s = 0.5 * (logPlus + logMinus);
        double d = 0.5 * (logPlus - logMinus);
        gam1 = Math.Exp(-s) * Math.Sinh(d) / x;
    }
}
