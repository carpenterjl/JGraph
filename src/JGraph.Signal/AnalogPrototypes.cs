using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The five analogue lowpass prototypes every classical IIR design starts from — Butterworth,
/// Chebyshev type I and II, elliptic and Bessel — each as zeros, poles and a gain with a cutoff of
/// one radian per second (M134).
/// </summary>
/// <remarks>
/// <para>
/// A prototype is the whole of a design's shape. <c>butter</c>, <c>cheby1</c>, <c>cheby2</c>,
/// <c>ellip</c> and <c>besself</c> differ only in which of these they call; everything after that —
/// the band transform, the bilinear map, the choice of output form — is one shared pipeline. So the
/// prototypes are pinned here to the last figure and the pipeline is written once, in
/// <see cref="IirDesign"/>.
/// </para>
/// <para>
/// Four of the five are formulas. The Bessel prototype is not: MATLAB carries a table of poles to
/// twenty-five significant figures for each order up to twenty-five, because the Bessel polynomial's
/// roots cannot be found from its coefficients at that accuracy — the coefficients of a
/// twenty-fifth-order Bessel polynomial span twenty-five orders of magnitude and a root finder run
/// on them loses most of its figures. The table here is that table, transcribed.
/// </para>
/// <para>
/// The elliptic prototype is the one MATLAB does not publish — its <c>ellipap</c> is compiled. What
/// is here is the standard construction through the Landen descending transformation: the degree
/// equation fixes the modulus, the zeros are the reciprocals of the Jacobi <c>cd</c> at the odd
/// half-points, and the poles are the same points displaced along the imaginary axis by the amount
/// that puts the passband ripple where it was asked for. It agrees with MATLAB's to the last few
/// bits, which is what the fixture pins.
/// </para>
/// </remarks>
public static class AnalogPrototypes
{
    /// <summary>How many terms the theta-function series carries; seven is past convergence.</summary>
    private const int SeriesTerms = 7;

    /// <summary>
    /// <c>buttap</c>: <paramref name="order"/> poles evenly spaced on the left half of the unit
    /// circle, listed as conjugate pairs with the real pole, when there is one, last.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) Butterworth(int order)
    {
        CheckOrder(order, "buttap");

        // MATLAB walks the odd angles 1, 3, … up to n − 1, which is one index per conjugate pair.
        int pairs = 0;
        for (int i = 1; i <= order - 1; i += 2)
        {
            pairs++;
        }

        var poles = new Complex[order];
        if (order % 2 == 1)
        {
            poles[^1] = -1;
        }

        for (int k = 1; k <= pairs; k++)
        {
            double angle = (System.Math.PI * ((2 * k) - 1) / (2 * order)) + (System.Math.PI / 2);
            var p = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
            poles[(2 * k) - 2] = p;
            poles[(2 * k) - 1] = Complex.Conjugate(p);
        }

        return ([], poles, NegativeProduct(poles).Real);
    }

    /// <summary>
    /// <c>cheb1ap</c>: the Butterworth circle squashed onto an ellipse, so the passband ripples by
    /// <paramref name="rippleDb"/> decibels and the stopband falls faster.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) Chebyshev1(int order, double rippleDb)
    {
        CheckOrder(order, "cheb1ap");
        CheckRipple(rippleDb, "cheb1ap", "Rp");

        double epsilon = System.Math.Sqrt(System.Math.Pow(10, 0.1 * rippleDb) - 1);
        double mu = System.Math.Asinh(1 / epsilon) / order;

        Complex[] poles = Ellipse(order, System.Math.Sinh(mu), System.Math.Cosh(mu));

        double gain = NegativeProduct(poles).Real;
        if (order % 2 == 0)
        {
            gain /= System.Math.Sqrt(1 + (epsilon * epsilon));
        }

        return ([], poles, gain);
    }

    /// <summary>
    /// <c>cheb2ap</c>: the type I poles inverted, which moves the ripple into the stopband and puts
    /// zeros on the imaginary axis where the stopband touches <paramref name="attenuationDb"/>.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) Chebyshev2(int order, double attenuationDb)
    {
        CheckOrder(order, "cheb2ap");
        CheckRipple(attenuationDb, "cheb2ap", "Rs");

        double delta = 1 / System.Math.Sqrt(System.Math.Pow(10, 0.1 * attenuationDb) - 1);
        double mu = System.Math.Asinh(1 / delta) / order;

        // The zeros are cosines at the odd half-points, skipping the middle one when the order is
        // odd — an odd-order type II filter has one fewer zero than it has poles.
        int m = order % 2 == 1 ? order - 1 : order;
        var raw = new double[m];
        int at = 0;
        for (int i = 1; i <= (2 * order) - 1; i += 2)
        {
            if (order % 2 == 1 && i > order - 2 && i < order + 2)
            {
                continue;
            }

            raw[at++] = System.Math.Cos(i * System.Math.PI / (2 * order));
        }

        // Antisymmetrising against the reversed list is what makes each pair exactly reciprocal.
        var zeros = new Complex[m];
        for (int i = 0; i < m; i++)
        {
            double value = (raw[i] - raw[m - 1 - i]) / 2;
            zeros[i] = Complex.ImaginaryOne / value;
        }

        // MATLAB then reads them off in the order 1, m, 2, m − 1, … so that each conjugate pair is
        // adjacent; the column-major flattening of a two-row index matrix is what does it.
        var ordered = new Complex[m];
        for (int i = 0; i < m / 2; i++)
        {
            ordered[2 * i] = zeros[i];
            ordered[(2 * i) + 1] = zeros[m - 1 - i];
        }

        Complex[] poles = Ellipse(order, System.Math.Sinh(mu), System.Math.Cosh(mu));
        for (int i = 0; i < poles.Length; i++)
        {
            poles[i] = 1 / poles[i];
        }

        double gain = (NegativeProduct(poles) / NegativeProduct(ordered)).Real;
        return (ordered, poles, gain);
    }

    /// <summary>
    /// <c>ellipap</c>: the prototype that ripples in both bands, and so falls off faster than any
    /// other design of the same order.
    /// </summary>
    /// <remarks>
    /// The construction is the classical one. The two ripples fix a selectivity <c>k1</c>; the
    /// degree equation turns that and the order into a modulus <c>k</c>, which is where the
    /// transition band ends; the zeros sit at the reciprocals of the Jacobi <c>cd</c> function at
    /// the odd half-points of the quarter period, and the poles at the same points shifted along
    /// the imaginary axis by the displacement that puts the passband ripple at <c>Rp</c>. Every one
    /// of those steps is a Landen descending transformation, which converges quadratically and is
    /// why seven steps are past enough.
    /// </remarks>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) Elliptic(int order, double rippleDb, double attenuationDb)
    {
        CheckOrder(order, "ellipap");
        CheckRipple(rippleDb, "ellipap", "Rp");
        CheckRipple(attenuationDb, "ellipap", "Rs");

        if (rippleDb == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rippleDb), "ellipap needs a passband ripple greater than zero.");
        }

        double ep = System.Math.Sqrt(System.Math.Pow(10, 0.1 * rippleDb) - 1);
        double es = System.Math.Sqrt(System.Math.Pow(10, 0.1 * attenuationDb) - 1);
        double k1 = ep / es;
        double k = Degree(order, k1);

        int pairs = order / 2;
        bool odd = order % 2 == 1;

        Complex v0 = -Complex.ImaginaryOne * ArcSn(Complex.ImaginaryOne / ep, k1) / order;

        // The half-points are walked from the band edge inwards — the pole nearest the axis is the
        // one belonging to the largest half-point, and MATLAB lists that pair first, with the
        // conjugate ahead of the root itself.
        var zeros = new Complex[2 * pairs];
        var poles = new Complex[order];
        for (int i = pairs; i >= 1; i--)
        {
            double u = (double)((2 * i) - 1) / order;
            Complex zeta = Cd(u, k);
            Complex zero = Complex.ImaginaryOne / (k * zeta);
            Complex pole = Complex.ImaginaryOne * Cd(u - (Complex.ImaginaryOne * v0), k);

            int at = 2 * (pairs - i);
            zeros[at] = Complex.Conjugate(zero);
            zeros[at + 1] = zero;
            poles[at] = Complex.Conjugate(pole);
            poles[at + 1] = pole;
        }

        if (odd)
        {
            poles[^1] = Complex.ImaginaryOne * Sn(Complex.ImaginaryOne * v0, k);
            poles[^1] = new Complex(poles[^1].Real, 0);
        }

        double gain = (NegativeProduct(poles) / NegativeProduct(zeros)).Real;
        if (!odd)
        {
            gain /= System.Math.Sqrt(1 + (ep * ep));
        }

        return (zeros, poles, gain);
    }

    /// <summary>
    /// <c>besselap</c>: the prototype whose group delay is flattest at the origin, read from the
    /// table MATLAB carries rather than computed from the Bessel polynomial's coefficients.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) Bessel(int order)
    {
        CheckOrder(order, "besselap");
        if (order > 25)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order), "besselap supports orders up to 25; the pole table stops there.");
        }

        (double Real, double Imaginary)[] table = BesselPoles[order - 1];
        var poles = new Complex[order];
        for (int i = 0; i < order; i++)
        {
            poles[i] = new Complex(table[i].Real, table[i].Imaginary);
        }

        return ([], poles, 1);
    }

    /// <summary>
    /// The same elliptic prototype written as second-order sections rather than as roots, which is
    /// the form a cascade design needs (M135).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each row is a monic-in-<c>s⁰</c> quadratic <c>[1, −2·Re(1/r), |1/r|²]</c> built from a zero
    /// or a pole, listed from the band edge inwards. Ahead of them sits one more row: the passband
    /// gain for an even order, the real first-order pole for an odd one. Writing the sections from
    /// the reciprocals rather than from the roots is what keeps the constant term exact, and it is
    /// what the reference does.
    /// </para>
    /// <para>
    /// The modulus comes from the product form of the degree equation rather than from the nome,
    /// which is the branch the reference takes for every selectivity above a millionth. The two
    /// agree to the last figures; taking the same one removes the question.
    /// </para>
    /// </remarks>
    internal static (double[,] Numerators, double[,] Denominators) EllipticSections(
        int order, double rippleDb, double attenuationDb)
    {
        CheckOrder(order, "ellipap");
        double gp = System.Math.Pow(10, -rippleDb / 20);
        double ep = System.Math.Sqrt(System.Math.Pow(10, rippleDb / 10) - 1);
        double es = System.Math.Sqrt(System.Math.Pow(10, attenuationDb / 10) - 1);
        double k1 = ep / es;
        double k = DegreeByProduct(order, k1);
        if (k >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(order), "The elliptic filter order is too large for the given specification.");
        }

        int pairs = order / 2;
        bool odd = order % 2 == 1;
        Complex v0 = -Complex.ImaginaryOne * ArcSnByCd(Complex.ImaginaryOne / ep, k1) / order;

        var b = new double[pairs + 1, 3];
        var a = new double[pairs + 1, 3];
        for (int i = 1; i <= pairs; i++)
        {
            double u = (double)((2 * i) - 1) / order;
            Complex zero = Complex.ImaginaryOne / (k * Cd(u, k));
            Complex pole = Complex.ImaginaryOne * Cd(u - (Complex.ImaginaryOne * v0), k);
            Complex rz = 1 / zero;
            Complex rp = 1 / pole;
            b[i, 0] = 1;
            b[i, 1] = -2 * rz.Real;
            b[i, 2] = Magnitude(rz);
            a[i, 0] = 1;
            a[i, 1] = -2 * rp.Real;
            a[i, 2] = Magnitude(rp);
        }

        if (odd)
        {
            Complex p0 = Complex.ImaginaryOne * Sn(Complex.ImaginaryOne * v0, k);
            b[0, 0] = 1;
            a[0, 0] = 1;
            a[0, 1] = -(1 / p0).Real;
        }
        else
        {
            b[0, 0] = gp;
            a[0, 0] = 1;
        }

        return (b, a);
    }

    /// <summary>
    /// The inverse Jacobi <c>sn</c> written the reference's way — as one minus the inverse
    /// <c>cd</c> — rather than by inverting the ascending transformation directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are the same function and disagree in the eleventh figure, which is enough to move a
    /// designed pole in the eleventh. The difference is which quadratic each descending step solves:
    /// this one carries the previous modulus into the square root and divides by <c>1 + v</c>
    /// afterwards, where <see cref="ArcSn"/> solves the step's own quadratic. Since a cascade's
    /// coefficients are compared against MATLAB's to ten figures, the arithmetic has to be the same
    /// arithmetic and not merely the same identity.
    /// </para>
    /// <para>
    /// The result is folded back into the fundamental rectangle at the end, as the reference folds
    /// it: the real part modulo four and the imaginary part modulo twice the quarter-period ratio,
    /// each brought to the half-interval around zero.
    /// </para>
    /// </remarks>
    private static Complex ArcSnByCd(Complex w, double k) => 1 - ArcCd(w, k);

    /// <summary>The inverse of <see cref="Cd"/>, in quarter periods.</summary>
    private static Complex ArcCd(Complex w, double k)
    {
        double[] v = Landen(k);
        for (int n = 0; n < v.Length; n++)
        {
            double previous = n == 0 ? k : v[n - 1];
            Complex root = Complex.Sqrt(1 - (w * w * previous * previous));
            w = w / (1 + root) * (2 / (1 + v[n]));
        }

        Complex u = 2 / System.Math.PI * Complex.Acos(w);
        if (w == Complex.One)
        {
            u = Complex.Zero;
        }

        double ratio = CompleteIntegral(ComplementaryModulus(k)) / CompleteIntegral(k);
        return new Complex(SymmetricRemainder(u.Real, 4), SymmetricRemainder(u.Imaginary, 2 * ratio));
    }

    /// <summary>The remainder brought into the half-interval around zero rather than the full one.</summary>
    private static double SymmetricRemainder(double x, double y)
    {
        // MATLAB's rem truncates towards zero, which is not what IEEERemainder does.
        double z = x - (y * System.Math.Truncate(x / y));
        if (System.Math.Abs(z) > y / 2)
        {
            z -= y * System.Math.Sign(z);
        }

        return z;
    }

    /// <summary>The absolute square of a complex number, written as MATLAB's <c>abs(x)^2</c> is.</summary>
    private static double Magnitude(Complex value)
    {
        double m = Complex.Abs(value);
        return m * m;
    }

    /// <summary>
    /// The degree equation solved through the product of Jacobi sines, which is the branch the
    /// reference takes whenever the selectivity is not vanishingly small.
    /// </summary>
    internal static double DegreeByProduct(int order, double k1)
    {
        if (k1 < 1e-6)
        {
            return Degree(order, k1);
        }

        // Both square roots are written as the reference writes them, √(1 − x²) rather than the
        // factored √((1−x)(1+x)) used elsewhere here. The factored form is the better one, and that
        // is exactly why it cannot be used: for an odd order the complement comes out close enough
        // to one that the two forms part in the eleventh figure, and a modulus is what every pole
        // of the design is a function of.
        double kc = System.Math.Sqrt(1 - (k1 * k1));
        double product = 1;
        for (int i = 1; i <= order / 2; i++)
        {
            product *= Sn((double)((2 * i) - 1) / order, kc).Real;
        }

        double kp = System.Math.Pow(kc, order) * System.Math.Pow(product, 4);
        return System.Math.Sqrt(1 - (kp * kp));
    }

    // --- The elliptic machinery ----------------------------------------------------------------

    /// <summary>
    /// The modulus that solves the degree equation <c>n·K'(k)/K(k) = K'(k₁)/K(k₁)</c>, found through
    /// the nome rather than by iterating on the integrals themselves.
    /// </summary>
    internal static double Degree(int order, double k1)
    {
        double q1 = Nome(k1);
        double q = System.Math.Pow(q1, 1.0 / order);
        return ModulusFromNome(q);
    }

    /// <summary>The nome <c>q = exp(−π·K'/K)</c>, which is what the degree equation is linear in.</summary>
    private static double Nome(double k)
    {
        double kp = ComplementaryModulus(k);
        return System.Math.Exp(-System.Math.PI * CompleteIntegral(kp) / CompleteIntegral(k));
    }

    /// <summary>The modulus a nome belongs to, by the theta-function series.</summary>
    private static double ModulusFromNome(double q)
    {
        double numerator = 0;
        double denominator = 0;
        for (int m = 1; m <= SeriesTerms; m++)
        {
            numerator += System.Math.Pow(q, m * (m + 1));
            denominator += System.Math.Pow(q, m * m);
        }

        double ratio = (1 + numerator) / (1 + (2 * denominator));
        return 4 * System.Math.Sqrt(q) * ratio * ratio;
    }

    /// <summary>
    /// The complete elliptic integral of the first kind at modulus <paramref name="k"/> (not at the
    /// parameter <c>m = k²</c>), by the arithmetic–geometric mean.
    /// </summary>
    internal static double CompleteIntegral(double k)
    {
        if (k >= 1)
        {
            return double.PositiveInfinity;
        }

        double a = 1;
        double b = ComplementaryModulus(k);
        for (int i = 0; i < 60; i++)
        {
            double nextA = (a + b) / 2;
            double nextB = System.Math.Sqrt(a * b);
            if (System.Math.Abs(nextA - a) <= 1e-17 * System.Math.Abs(nextA))
            {
                break;
            }

            a = nextA;
            b = nextB;
        }

        return System.Math.PI / (2 * a);
    }

    /// <summary>√(1 − k²), computed so that a modulus close to one keeps its figures.</summary>
    private static double ComplementaryModulus(double k) =>
        System.Math.Sqrt((1 - k) * (1 + k));

    /// <summary>The descending Landen sequence of moduli, which is what every Jacobi call rides on.</summary>
    /// <remarks>
    /// The sequence runs until the modulus falls below the machine epsilon, and how long that takes
    /// depends entirely on where it starts. From a half it is two or three steps; from within a
    /// rounding error of one — which is where a seventy-decibel stopband puts the complementary
    /// modulus — it is nearer sixty, because the first steps only double the distance from one
    /// before the convergence becomes quadratic. A fixed step count is therefore not a
    /// simplification but a wrong answer for exactly the demanding specifications.
    /// </remarks>
    private static double[] Landen(double k)
    {
        if (k == 0 || k == 1)
        {
            return [k];
        }

        var v = new List<double>();
        double current = k;
        while (current > DoubleSpacing)
        {
            double ratio = current / (1 + System.Math.Sqrt(1 - (current * current)));
            current = ratio * ratio;
            v.Add(current);
        }

        return [.. v];
    }

    /// <summary>The spacing of doubles at one, which is where the Landen descent stops.</summary>
    private const double DoubleSpacing = 2.220446049250313e-16;

    /// <summary>Jacobi <c>cd(u·K, k)</c> with <paramref name="u"/> measured in quarter periods.</summary>
    private static Complex Cd(Complex u, double k) =>
        Ascend(Complex.Cos(u * System.Math.PI / 2), k);

    /// <summary>Jacobi <c>sn(u·K, k)</c> with <paramref name="u"/> measured in quarter periods.</summary>
    private static Complex Sn(Complex u, double k) =>
        Ascend(Complex.Sin(u * System.Math.PI / 2), k);

    /// <summary>The ascending Landen recursion that lifts a circular function back to a Jacobi one.</summary>
    private static Complex Ascend(Complex w, double k)
    {
        double[] v = Landen(k);
        for (int n = v.Length - 1; n >= 0; n--)
        {
            w = (1 + v[n]) * w / (1 + (v[n] * w * w));
        }

        return w;
    }

    /// <summary>The inverse of <see cref="Sn"/>, again in quarter periods.</summary>
    /// <remarks>
    /// Each descending step inverts the Gauss transformation
    /// <c>w = (1+v)·w₁/(1 + v·w₁²)</c>, and the root of that quadratic is written with its
    /// conjugate multiplied through — the form that subtracts loses every figure as <c>v</c> falls
    /// to nothing, which after seven steps is most of them. The normalised argument is the same at
    /// every modulus, because <c>K(k) = (1 + v)·K(v)</c> cancels the scaling exactly, so there is no
    /// product to carry.
    /// </remarks>
    private static Complex ArcSn(Complex w, double k)
    {
        double[] v = Landen(k);
        foreach (double modulus in v)
        {
            Complex root = Complex.Sqrt(((1 + modulus) * (1 + modulus)) - (4 * modulus * w * w));
            w = 2 * w / (1 + modulus + root);
        }

        return 2 * Complex.Asin(w) / System.Math.PI;
    }

    // --- Shared shapes -------------------------------------------------------------------------

    /// <summary>
    /// The <paramref name="order"/> points of the Butterworth circle stretched onto an ellipse with
    /// the given semi-axes, symmetrised against their own reversal so each pair is exact.
    /// </summary>
    private static Complex[] Ellipse(int order, double semiReal, double semiImaginary)
    {
        var raw = new Complex[order];
        for (int i = 0; i < order; i++)
        {
            double angle = (System.Math.PI * ((2 * i) + 1) / (2 * order)) + (System.Math.PI / 2);
            raw[i] = new Complex(System.Math.Cos(angle), System.Math.Sin(angle));
        }

        var poles = new Complex[order];
        for (int i = 0; i < order; i++)
        {
            double real = (raw[i].Real + raw[order - 1 - i].Real) / 2;
            double imaginary = (raw[i].Imaginary - raw[order - 1 - i].Imaginary) / 2;
            poles[i] = new Complex(semiReal * real, semiImaginary * imaginary);
        }

        return poles;
    }

    /// <summary>Π(−r), which is a prototype's gain before the ripple patch.</summary>
    private static Complex NegativeProduct(ReadOnlySpan<Complex> roots)
    {
        Complex product = Complex.One;
        foreach (Complex r in roots)
        {
            product *= -r;
        }

        return product;
    }

    private static void CheckOrder(int order, string name)
    {
        if (order < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(order), $"{name} needs an order of at least 1.");
        }
    }

    private static void CheckRipple(double value, string name, string argument)
    {
        if (double.IsNaN(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{name}'s {argument} must be nonnegative.");
        }
    }

    /// <summary>
    /// The Bessel prototype's poles for orders one to twenty-five, to the twenty-five figures
    /// MATLAB's <c>besselap</c> carries. They are not computable from the polynomial at this
    /// accuracy, which is why both engines carry the same table.
    /// </summary>
    private static readonly (double Real, double Imaginary)[][] BesselPoles =
    [
[(-1, 0)],
        [(-0.8660254037844386467637229, 0.4999999999999999999999996), (-0.8660254037844386467637229, -0.4999999999999999999999996)],
        [(-0.9416000265332067855971980, 0), (-0.7456403858480766441810907, -0.7113666249728352680992154), (-0.7456403858480766441810907, 0.7113666249728352680992154)],
        [(-0.6572111716718829545787781, -0.8301614350048733772399715), (-0.6572111716718829545787788, 0.8301614350048733772399715), (-0.9047587967882449459642637, -0.2709187330038746636700923), (-0.9047587967882449459642624, 0.2709187330038746636700926)],
        [(-0.9264420773877602247196260, 0), (-0.8515536193688395541722677, -0.4427174639443327209850002), (-0.8515536193688395541722677, 0.4427174639443327209850002), (-0.5905759446119191779319432, -0.9072067564574549539291747), (-0.5905759446119191779319432, 0.9072067564574549539291747)],
        [(-0.9093906830472271808050953, -0.1856964396793046769246397), (-0.9093906830472271808050953, 0.1856964396793046769246397), (-0.7996541858328288520243325, -0.5621717346937317988594118), (-0.7996541858328288520243325, 0.5621717346937317988594118), (-0.5385526816693109683073792, -0.9616876881954277199245657), (-0.5385526816693109683073792, 0.9616876881954277199245657)],
        [(-0.9194871556490290014311619, 0), (-0.8800029341523374639772340, -0.3216652762307739398381830), (-0.8800029341523374639772340, 0.3216652762307739398381830), (-0.7527355434093214462291616, -0.6504696305522550699212995), (-0.7527355434093214462291616, 0.6504696305522550699212995), (-0.4966917256672316755024763, -1.002508508454420401230220), (-0.4966917256672316755024763, 1.002508508454420401230220)],
        [(-0.9096831546652910216327629, -0.1412437976671422927888150), (-0.9096831546652910216327629, 0.1412437976671422927888150), (-0.8473250802359334320103023, -0.4259017538272934994996429), (-0.8473250802359334320103023, 0.4259017538272934994996429), (-0.7111381808485399250796172, -0.7186517314108401705762571), (-0.7111381808485399250796172, 0.7186517314108401705762571), (-0.4621740412532122027072175, -1.034388681126901058116589), (-0.4621740412532122027072175, 1.034388681126901058116589)],
        [(-0.9154957797499037686769223, 0), (-0.8911217017079759323183848, -0.2526580934582164192308115), (-0.8911217017079759323183848, 0.2526580934582164192308115), (-0.8148021112269012975514135, -0.5085815689631499483745341), (-0.8148021112269012975514135, 0.5085815689631499483745341), (-0.6743622686854761980403401, -0.7730546212691183706919682), (-0.6743622686854761980403401, 0.7730546212691183706919682), (-0.4331415561553618854685942, -1.060073670135929666774323), (-0.4331415561553618854685942, 1.060073670135929666774323)],
        [(-0.9091347320900502436826431, -0.1139583137335511169927714), (-0.9091347320900502436826431, 0.1139583137335511169927714), (-0.8688459641284764527921864, -0.3430008233766309973110589), (-0.8688459641284764527921864, 0.3430008233766309973110589), (-0.7837694413101441082655890, -0.5759147538499947070009852), (-0.7837694413101441082655890, 0.5759147538499947070009852), (-0.6417513866988316136190854, -0.8175836167191017226233947), (-0.6417513866988316136190854, 0.8175836167191017226233947), (-0.4083220732868861566219785, -1.081274842819124562037210), (-0.4083220732868861566219785, 1.081274842819124562037210)],
        [(-0.9129067244518981934637318, 0), (-0.8963656705721166099815744, -0.2080480375071031919692341), (-0.8963656705721166099815744, 0.2080480375071031919692341), (-0.8453044014712962954184557, -0.4178696917801248292797448), (-0.8453044014712962954184557, 0.4178696917801248292797448), (-0.7546938934722303128102142, -0.6319150050721846494520941), (-0.7546938934722303128102142, 0.6319150050721846494520941), (-0.6126871554915194054182909, -0.8547813893314764631518509), (-0.6126871554915194054182909, 0.8547813893314764631518509), (-0.3868149510055090879155425, -1.099117466763120928733632), (-0.3868149510055090879155425, 1.099117466763120928733632)],
        [(-0.9084478234140682638817772, -95506365213450398415258360.0E-27), (-0.9084478234140682638817772, 95506365213450398415258360.0E-27), (-0.8802534342016826507901575, -0.2871779503524226723615457), (-0.8802534342016826507901575, 0.2871779503524226723615457), (-0.8217296939939077285792834, -0.4810212115100676440620548), (-0.8217296939939077285792834, 0.4810212115100676440620548), (-0.7276681615395159454547013, -0.6792961178764694160048987), (-0.7276681615395159454547013, 0.6792961178764694160048987), (-0.5866369321861477207528215, -0.8863772751320727026622149), (-0.5866369321861477207528215, 0.8863772751320727026622149), (-0.3679640085526312839425808, -1.114373575641546257595657), (-0.3679640085526312839425808, 1.114373575641546257595657)],
        [(-0.9110914665984182781070663, 0), (-0.8991314665475196220910718, -0.1768342956161043620980863), (-0.8991314665475196220910718, 0.1768342956161043620980863), (-0.8625094198260548711573628, -0.3547413731172988997754038), (-0.8625094198260548711573628, 0.3547413731172988997754038), (-0.7987460692470972510394686, -0.5350752120696801938272504), (-0.7987460692470972510394686, 0.5350752120696801938272504), (-0.7026234675721275653944062, -0.7199611890171304131266374), (-0.7026234675721275653944062, 0.7199611890171304131266374), (-0.5631559842430199266325818, -0.9135900338325109684927731), (-0.5631559842430199266325818, 0.9135900338325109684927731), (-0.3512792323389821669401925, -1.127591548317705678613239), (-0.3512792323389821669401925, 1.127591548317705678613239)],
        [(-0.9077932138396487614720659, -82196399419401501888968130.0E-27), (-0.9077932138396487614720659, 82196399419401501888968130.0E-27), (-0.8869506674916445312089167, -0.2470079178765333183201435), (-0.8869506674916445312089167, 0.2470079178765333183201435), (-0.8441199160909851197897667, -0.4131653825102692595237260), (-0.8441199160909851197897667, 0.4131653825102692595237260), (-0.7766591387063623897344648, -0.5819170677377608590492434), (-0.7766591387063623897344648, 0.5819170677377608590492434), (-0.6794256425119233117869491, -0.7552857305042033418417492), (-0.6794256425119233117869491, 0.7552857305042033418417492), (-0.5418766775112297376541293, -0.9373043683516919569183099), (-0.5418766775112297376541293, 0.9373043683516919569183099), (-0.3363868224902037330610040, -1.139172297839859991370924), (-0.3363868224902037330610040, 1.139172297839859991370924)],
        [(-0.9097482363849064167228581, 0), (-0.9006981694176978324932918, -0.1537681197278439351298882), (-0.9006981694176978324932918, 0.1537681197278439351298882), (-0.8731264620834984978337843, -0.3082352470564267657715883), (-0.8731264620834984978337843, 0.3082352470564267657715883), (-0.8256631452587146506294553, -0.4642348752734325631275134), (-0.8256631452587146506294553, 0.4642348752734325631275134), (-0.7556027168970728127850416, -0.6229396358758267198938604), (-0.7556027168970728127850416, 0.6229396358758267198938604), (-0.6579196593110998676999362, -0.7862895503722515897065645), (-0.6579196593110998676999362, 0.7862895503722515897065645), (-0.5224954069658330616875186, -0.9581787261092526478889345), (-0.5224954069658330616875186, 0.9581787261092526478889345), (-0.3229963059766444287113517, -1.149416154583629539665297), (-0.3229963059766444287113517, 1.149416154583629539665297)],
        [(-0.9072099595087001356491337, -72142113041117326028823950.0E-27), (-0.9072099595087001356491337, 72142113041117326028823950.0E-27), (-0.8911723070323647674780132, -0.2167089659900576449410059), (-0.8911723070323647674780132, 0.2167089659900576449410059), (-0.8584264231521330481755780, -0.3621697271802065647661080), (-0.8584264231521330481755780, 0.3621697271802065647661080), (-0.8074790293236003885306146, -0.5092933751171800179676218), (-0.8074790293236003885306146, 0.5092933751171800179676218), (-0.7356166304713115980927279, -0.6591950877860393745845254), (-0.7356166304713115980927279, 0.6591950877860393745845254), (-0.6379502514039066715773828, -0.8137453537108761895522580), (-0.6379502514039066715773828, 0.8137453537108761895522580), (-0.5047606444424766743309967, -0.9767137477799090692947061), (-0.5047606444424766743309967, 0.9767137477799090692947061), (-0.3108782755645387813283867, -1.158552841199330479412225), (-0.3108782755645387813283867, 1.158552841199330479412225)],
        [(-0.9087141161336397432860029, 0), (-0.9016273850787285964692844, -0.1360267995173024591237303), (-0.9016273850787285964692844, 0.1360267995173024591237303), (-0.8801100704438627158492165, -0.2725347156478803885651973), (-0.8801100704438627158492165, 0.2725347156478803885651973), (-0.8433414495836129204455491, -0.4100759282910021624185986), (-0.8433414495836129204455491, 0.4100759282910021624185986), (-0.7897644147799708220288138, -0.5493724405281088674296232), (-0.7897644147799708220288138, 0.5493724405281088674296232), (-0.7166893842372349049842743, -0.6914936286393609433305754), (-0.7166893842372349049842743, 0.6914936286393609433305754), (-0.6193710717342144521602448, -0.8382497252826992979368621), (-0.6193710717342144521602448, 0.8382497252826992979368621), (-0.4884629337672704194973683, -0.9932971956316781632345466), (-0.4884629337672704194973683, 0.9932971956316781632345466), (-0.2998489459990082015466971, -1.166761272925668786676672), (-0.2998489459990082015466971, 1.166761272925668786676672)],
        [(-0.9067004324162775554189031, -64279241063930693839360680.0E-27), (-0.9067004324162775554189031, 64279241063930693839360680.0E-27), (-0.8939764278132455733032155, -0.1930374640894758606940586), (-0.8939764278132455733032155, 0.1930374640894758606940586), (-0.8681095503628830078317207, -0.3224204925163257604931634), (-0.8681095503628830078317207, 0.3224204925163257604931634), (-0.8281885016242836608829018, -0.4529385697815916950149364), (-0.8281885016242836608829018, 0.4529385697815916950149364), (-0.7726285030739558780127746, -0.5852778162086640620016316), (-0.7726285030739558780127746, 0.5852778162086640620016316), (-0.6987821445005273020051878, -0.7204696509726630531663123), (-0.6987821445005273020051878, 0.7204696509726630531663123), (-0.6020482668090644386627299, -0.8602708961893664447167418), (-0.6020482668090644386627299, 0.8602708961893664447167418), (-0.4734268069916151511140032, -1.008234300314801077034158), (-0.4734268069916151511140032, 1.008234300314801077034158), (-0.2897592029880489845789953, -1.174183010600059128532230), (-0.2897592029880489845789953, 1.174183010600059128532230)],
        [(-0.9078934217899404528985092, 0), (-0.9021937639390660668922536, -0.1219568381872026517578164), (-0.9021937639390660668922536, 0.1219568381872026517578164), (-0.8849290585034385274001112, -0.2442590757549818229026280), (-0.8849290585034385274001112, 0.2442590757549818229026280), (-0.8555768765618421591093993, -0.3672925896399872304734923), (-0.8555768765618421591093993, 0.3672925896399872304734923), (-0.8131725551578197705476160, -0.4915365035562459055630005), (-0.8131725551578197705476160, 0.4915365035562459055630005), (-0.7561260971541629355231897, -0.6176483917970178919174173), (-0.7561260971541629355231897, 0.6176483917970178919174173), (-0.6818424412912442033411634, -0.7466272357947761283262338), (-0.6818424412912442033411634, 0.7466272357947761283262338), (-0.5858613321217832644813602, -0.8801817131014566284786759), (-0.5858613321217832644813602, 0.8801817131014566284786759), (-0.4595043449730988600785456, -1.021768776912671221830298), (-0.4595043449730988600785456, 1.021768776912671221830298), (-0.2804866851439370027628724, -1.180931628453291873626003), (-0.2804866851439370027628724, 1.180931628453291873626003)],
        [(-0.9062570115576771146523497, -57961780277849516990208850.0E-27), (-0.9062570115576771146523497, 57961780277849516990208850.0E-27), (-0.8959150941925768608568248, -0.1740317175918705058595844), (-0.8959150941925768608568248, 0.1740317175918705058595844), (-0.8749560316673332850673214, -0.2905559296567908031706902), (-0.8749560316673332850673214, 0.2905559296567908031706902), (-0.8427907479956670633544106, -0.4078917326291934082132821), (-0.8427907479956670633544106, 0.4078917326291934082132821), (-0.7984251191290606875799876, -0.5264942388817132427317659), (-0.7984251191290606875799876, 0.5264942388817132427317659), (-0.7402780309646768991232610, -0.6469975237605228320268752), (-0.7402780309646768991232610, 0.6469975237605228320268752), (-0.6658120544829934193890626, -0.7703721701100763015154510), (-0.6658120544829934193890626, 0.7703721701100763015154510), (-0.5707026806915714094398061, -0.8982829066468255593407161), (-0.5707026806915714094398061, 0.8982829066468255593407161), (-0.4465700698205149555701841, -1.034097702560842962315411), (-0.4465700698205149555701841, 1.034097702560842962315411), (-0.2719299580251652601727704, -1.187099379810885886139638), (-0.2719299580251652601727704, 1.187099379810885886139638)],
        [(-0.9072262653142957028884077, 0), (-0.9025428073192696303995083, -0.1105252572789856480992275), (-0.9025428073192696303995083, 0.1105252572789856480992275), (-0.8883808106664449854431605, -0.2213069215084350419975358), (-0.8883808106664449854431605, 0.2213069215084350419975358), (-0.8643915813643204553970169, -0.3326258512522187083009453), (-0.8643915813643204553970169, 0.3326258512522187083009453), (-0.8299435470674444100273463, -0.4448177739407956609694059), (-0.8299435470674444100273463, 0.4448177739407956609694059), (-0.7840287980408341576100581, -0.5583186348022854707564856), (-0.7840287980408341576100581, 0.5583186348022854707564856), (-0.7250839687106612822281339, -0.6737426063024382240549898), (-0.7250839687106612822281339, 0.6737426063024382240549898), (-0.6506315378609463397807996, -0.7920349342629491368548074), (-0.6506315378609463397807996, 0.7920349342629491368548074), (-0.5564766488918562465935297, -0.9148198405846724121600860), (-0.5564766488918562465935297, 0.9148198405846724121600860), (-0.4345168906815271799687308, -1.045382255856986531461592), (-0.4345168906815271799687308, 1.045382255856986531461592), (-0.2640041595834031147954813, -1.192762031948052470183960), (-0.2640041595834031147954813, 1.192762031948052470183960)],
        [(-0.9058702269930872551848625, -52774908289999045189007100.0E-27), (-0.9058702269930872551848625, 52774908289999045189007100.0E-27), (-0.8972983138153530955952835, -0.1584351912289865608659759), (-0.8972983138153530955952835, 0.1584351912289865608659759), (-0.8799661455640176154025352, -0.2644363039201535049656450), (-0.8799661455640176154025352, 0.2644363039201535049656450), (-0.8534754036851687233084587, -0.3710389319482319823405321), (-0.8534754036851687233084587, 0.3710389319482319823405321), (-0.8171682088462720394344996, -0.4785619492202780899653575), (-0.8171682088462720394344996, 0.4785619492202780899653575), (-0.7700332930556816872932937, -0.5874255426351153211965601), (-0.7700332930556816872932937, 0.5874255426351153211965601), (-0.7105305456418785989070935, -0.6982266265924524000098548), (-0.7105305456418785989070935, 0.6982266265924524000098548), (-0.6362427683267827226840153, -0.8118875040246347267248508), (-0.6362427683267827226840153, 0.8118875040246347267248508), (-0.5430983056306302779658129, -0.9299947824439872998916657), (-0.5430983056306302779658129, 0.9299947824439872998916657), (-0.4232528745642628461715044, -1.055755605227545931204656), (-0.4232528745642628461715044, 1.055755605227545931204656), (-0.2566376987939318038016012, -1.197982433555213008346532), (-0.2566376987939318038016012, 1.197982433555213008346532)],
        [(-0.9066732476324988168207439, 0), (-0.9027564979912504609412993, -0.1010534335314045013252480), (-0.9027564979912504609412993, 0.1010534335314045013252480), (-0.8909283242471251458653994, -0.2023024699381223418195228), (-0.8909283242471251458653994, 0.2023024699381223418195228), (-0.8709469395587416239596874, -0.3039581993950041588888925), (-0.8709469395587416239596874, 0.3039581993950041588888925), (-0.8423805948021127057054288, -0.4062657948237602726779246), (-0.8423805948021127057054288, 0.4062657948237602726779246), (-0.8045561642053176205623187, -0.5095305912227258268309528), (-0.8045561642053176205623187, 0.5095305912227258268309528), (-0.7564660146829880581478138, -0.6141594859476032127216463), (-0.7564660146829880581478138, 0.6141594859476032127216463), (-0.6965966033912705387505040, -0.7207341374753046970247055), (-0.6965966033912705387505040, 0.7207341374753046970247055), (-0.6225903228771341778273152, -0.8301558302812980678845563), (-0.6225903228771341778273152, 0.8301558302812980678845563), (-0.5304922463810191698502226, -0.9439760364018300083750242), (-0.5304922463810191698502226, 0.9439760364018300083750242), (-0.4126986617510148836149955, -1.065328794475513585531053), (-0.4126986617510148836149955, 1.065328794475513585531053), (-0.2497697202208956030229911, -1.202813187870697831365338), (-0.2497697202208956030229911, 1.202813187870697831365338)],
        [(-0.9055312363372773709269407, -48440066540478700874836350.0E-27), (-0.9055312363372773709269407, 48440066540478700874836350.0E-27), (-0.8983105104397872954053307, -0.1454056133873610120105857), (-0.8983105104397872954053307, 0.1454056133873610120105857), (-0.8837358034555706623131950, -0.2426335234401383076544239), (-0.8837358034555706623131950, 0.2426335234401383076544239), (-0.8615278304016353651120610, -0.3403202112618624773397257), (-0.8615278304016353651120610, 0.3403202112618624773397257), (-0.8312326466813240652679563, -0.4386985933597305434577492), (-0.8312326466813240652679563, 0.4386985933597305434577492), (-0.7921695462343492518845446, -0.5380628490968016700338001), (-0.7921695462343492518845446, 0.5380628490968016700338001), (-0.7433392285088529449175873, -0.6388084216222567930378296), (-0.7433392285088529449175873, 0.6388084216222567930378296), (-0.6832565803536521302816011, -0.7415032695091650806797753), (-0.6832565803536521302816011, 0.7415032695091650806797753), (-0.6096221567378335562589532, -0.8470292433077202380020454), (-0.6096221567378335562589532, 0.8470292433077202380020454), (-0.5185914574820317343536707, -0.9569048385259054576937721), (-0.5185914574820317343536707, 0.9569048385259054576937721), (-0.4027853855197518014786978, -1.074195196518674765143729), (-0.4027853855197518014786978, 1.074195196518674765143729), (-0.2433481337524869675825448, -1.207298683731972524975429), (-0.2433481337524869675825448, 1.207298683731972524975429)],
        [(-0.9062073871811708652496104, 0), (-0.9028833390228020537142561, -93077131185102967450643820.0E-27), (-0.9028833390228020537142561, 93077131185102967450643820.0E-27), (-0.8928551459883548836774529, -0.1863068969804300712287138), (-0.8928551459883548836774529, 0.1863068969804300712287138), (-0.8759497989677857803656239, -0.2798521321771408719327250), (-0.8759497989677857803656239, 0.2798521321771408719327250), (-0.8518616886554019782346493, -0.3738977875907595009446142), (-0.8518616886554019782346493, 0.3738977875907595009446142), (-0.8201226043936880253962552, -0.4686668574656966589020580), (-0.8201226043936880253962552, 0.4686668574656966589020580), (-0.7800496278186497225905443, -0.5644441210349710332887354), (-0.7800496278186497225905443, 0.5644441210349710332887354), (-0.7306549271849967721596735, -0.6616149647357748681460822), (-0.7306549271849967721596735, 0.6616149647357748681460822), (-0.6704827128029559528610523, -0.7607348858167839877987008), (-0.6704827128029559528610523, 0.7607348858167839877987008), (-0.5972898661335557242320528, -0.8626676330388028512598538), (-0.5972898661335557242320528, 0.8626676330388028512598538), (-0.5073362861078468845461362, -0.9689006305344868494672405), (-0.5073362861078468845461362, 0.9689006305344868494672405), (-0.3934529878191079606023847, -1.082433927173831581956863), (-0.3934529878191079606023847, 1.082433927173831581956863), (-0.2373280669322028974199184, -1.211476658382565356579418), (-0.2373280669322028974199184, 1.211476658382565356579418)],
    ];
}
