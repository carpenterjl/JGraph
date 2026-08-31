namespace JGraph.Numerics;

/// <summary>
/// The error function, its complement, the scaled complement, and the two inverses — evaluated by
/// approximation rather than by iteration.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in <see cref="SpecialFunctions"/> is written on two workhorses, a Lanczos
/// log-gamma and a modified-Lentz continued fraction, so that accuracy is a property of two pieces
/// of code instead of fifteen. That is the right trade for gamma and the incomplete integrals,
/// where a table of approximations per function would be a table of fifteen chances to be wrong.
/// It is the wrong trade here, and only here, because erf is the one family in the set that a
/// script calls a few million times in a row: <c>erfc</c> as Q(½, x²) is thirty divisions of a
/// continued fraction driven to 1e-15, and the same answer is a rational in fifteen flops.
/// </para>
/// <para>
/// The forward functions are W. J. Cody's rational Chebyshev approximations (<em>Math. Comp.</em>
/// 23, 1969), which is what nearly every library's erf is, in three intervals: a rational in x²
/// below 0.46875, a rational in x up to 4, and a rational in 1/x² beyond it. The coefficients here
/// were checked against a C library's own erf over forty thousand points before they were written
/// down, worst relative disagreement 4.6e-16 for erf and 9.3e-16 for erfc.
/// </para>
/// <para>
/// The inverses are a fitted first guess finished by one Halley step. The guess is a polynomial
/// this repository fitted for itself rather than one lifted from a paper — degree 14 in p² near
/// the middle and degree 14 in 1/√(-ln q) down the tail, worst relative error 3.6e-8 and 7.7e-8
/// measured on a dense grid, not on the nodes they were fitted at. Halley triples the digits, so
/// one step of it carries either guess past what a double can hold, with room to spare for a point
/// the fit was never asked about. The consequence worth naming is that <c>erfinv</c> is defined
/// here as the inverse of <em>this</em> library's erf and not of a table of its own: the refinement
/// calls <see cref="Erf"/>, so the two can never drift apart.
/// </para>
/// </remarks>
public static class ErrorFunctions
{
    /// <summary>1/√π, the leading term of the tail expansion.</summary>
    private const double OneOverSqrtPi = 5.6418958354775628695e-1;

    /// <summary>√π/2, the reciprocal derivative of erf at 0 — the scale of every Newton step below.</summary>
    private const double HalfSqrtPi = 8.8622692545275801365e-1;

    /// <summary>Where Cody's first interval ends and his second begins.</summary>
    private const double Threshold = 0.46875;

    /// <summary>Below this, x² rounds to zero and erf(x) is x·2/√π to every bit that exists.</summary>
    private const double TooSmallToSquare = 1.11e-16;

    /// <summary>
    /// Beyond this, erfc(x) is zero however carefully it is computed — the last argument with a
    /// non-zero answer is near 27.21, and this leaves room above it.
    /// </summary>
    /// <remarks>
    /// Cody's own limit is 26.543, which is where erfc leaves the <em>normal</em> doubles. That was
    /// the right place to stop on a machine that flushed everything below to zero and it is the
    /// wrong place on one with gradual underflow: erfc(27) is 5.24e-319, a subnormal, and MATLAB
    /// answers it. Reaching those last few hundred arguments is what
    /// <see cref="TimesNegativeExponentialOfSquare"/> halves its exponent for.
    /// </remarks>
    private const double ErfcUnderflows = 28.0;

    /// <summary>
    /// Above this the product erfcx(x)·e^{-x²} leaves the normal doubles, and the exponential has
    /// to be applied in two halves to get there without rounding twice in the subnormals.
    /// </summary>
    private const double ErfcLeavesTheNormals = 26.0;

    /// <summary>Beyond this, erfcx(x) is 1/(x√π) to full precision.</summary>
    private const double ErfcScaledIsItsLeadingTerm = 6.71e7;

    /// <summary>Beyond this, the reflection erfcx(-x) = 2e^{x²} - erfcx(x) overflows.</summary>
    private const double ErfcScaledOverflows = -26.628;

    /// <summary>Where <see cref="ErfInverse"/> stops inverting erf and starts inverting erfc.</summary>
    /// <remarks>
    /// Above it the erf curve is flat enough that the subtraction <c>1 - |p|</c> carries more
    /// information than <c>erf(y) - p</c> does, and the tail arm reads that subtraction directly.
    /// </remarks>
    private const double CentralLimit = 0.9;

    // --- Cody's coefficients ----------------------------------------------------------------------

    /// <summary>Numerator of the rational for erf on |x| ≤ 0.46875.</summary>
    private static readonly double[] A =
    [
        3.16112374387056560e00, 1.13864154151050156e02,
        3.77485237685302021e02, 3.20937758913846947e03,
        1.85777706184603153e-1,
    ];

    /// <summary>Denominator of the same.</summary>
    private static readonly double[] B =
    [
        2.36012909523441209e01, 2.44024637934444173e02,
        1.28261652607737228e03, 2.84423683343917062e03,
    ];

    /// <summary>Numerator of the rational for erfc on 0.46875 ≤ |x| ≤ 4.</summary>
    private static readonly double[] C =
    [
        5.64188496988670089e-1, 8.88314979438837594e00,
        6.61191906371416295e01, 2.98635138197400131e02,
        8.81952221241769090e02, 1.71204761263407058e03,
        2.05107837782607147e03, 1.23033935479799725e03,
        2.15311535474403846e-8,
    ];

    /// <summary>Denominator of the same.</summary>
    private static readonly double[] D =
    [
        1.57449261107098347e01, 1.17693950891312499e02,
        5.37181101862009858e02, 1.62138957456669019e03,
        3.29079923573345963e03, 4.36261909014324716e03,
        3.43936767414372164e03, 1.23033935480374942e03,
    ];

    /// <summary>Numerator of the rational for erfc on |x| &gt; 4, in 1/x².</summary>
    private static readonly double[] P =
    [
        3.05326634961232344e-1, 3.60344899949804439e-1,
        1.25781726111229246e-1, 1.60837851487422766e-2,
        6.58749161529837803e-4, 1.63153871373020978e-2,
    ];

    /// <summary>Denominator of the same.</summary>
    private static readonly double[] Q =
    [
        2.56852019228982242e00, 1.87295284992346047e00,
        5.27905102951428412e-1, 6.05183413124413191e-2,
        2.33520497626869185e-3,
    ];

    // --- The fitted first guesses -----------------------------------------------------------------

    /// <summary>
    /// erfinv(p)/p as a polynomial in p², for |p| ≤ <see cref="CentralLimit"/>. Ascending powers.
    /// </summary>
    private static readonly double[] CentralGuess =
    [
        8.86226940047648570e-01, 2.32005708534158472e-01, 1.28272112151968959e-01,
        6.12883488336703336e-02, 5.28571713469268234e-01, -5.03035486851232427e+00,
        3.60546648597186490e+01, -1.72886204687828354e+02, 5.78049049940766054e+02,
        -1.36032882623067030e+03, 2.24686560602271538e+03, -2.55066300652573227e+03,
        1.89696441527159641e+03, -8.33152235478171178e+02, 1.64177889169847759e+02,
    ];

    /// <summary>
    /// erfcinv(q)/w as a polynomial in 1/w, where w = √(-ln q), for q ≤ 0.1. Ascending powers.
    /// </summary>
    /// <remarks>
    /// w is the leading term of the answer, because erfc(y) ≈ e^{-y²}/(y√π) makes -ln q ≈ y² for
    /// large y — which is why the first coefficient below is 1 to four figures and the fit has
    /// only the slowly varying remainder left to describe.
    /// </remarks>
    private static readonly double[] TailGuess =
    [
        1.00010174047770217e+00, -1.56160300583323142e-02, -1.85829271808567453e+00,
        8.83366702849498076e+00, -4.93423096888453756e+01, 2.40200944223975171e+02,
        -8.90533254516585657e+02, 2.40273606001919461e+03, -4.51615639368659595e+03,
        5.39512536460503270e+03, -2.87194087718403944e+03, -1.95269465261123491e+03,
        4.61709565221040612e+03, -3.22753824015694454e+03, 8.47516726258326457e+02,
    ];

    // --- The forward functions --------------------------------------------------------------------

    /// <summary>erf(x), the error function.</summary>
    public static double Erf(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        return double.IsInfinity(x) ? Math.Sign(x) : Rational(x, Kind.Erf);
    }

    /// <summary>erfc(x) = 1 - erf(x), evaluated so that the tail keeps its significant digits.</summary>
    public static double Erfc(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            return x > 0 ? 0 : 2;
        }

        return Rational(x, Kind.Erfc);
    }

    /// <summary>
    /// exp(x²)·erfc(x), the scaled complementary error function. It exists because erfc(30) is
    /// 2.6e-393 — zero as a double — while this is 0.0187, and the ratio is what most formulas want.
    /// </summary>
    public static double ErfcScaled(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (double.IsInfinity(x))
        {
            return x > 0 ? 0 : double.PositiveInfinity;
        }

        return Rational(x, Kind.ErfcScaled);
    }

    // --- The inverses -----------------------------------------------------------------------------

    /// <summary>The inverse error function: the y with erf(y) = <paramref name="value"/>.</summary>
    public static double ErfInverse(double value)
    {
        if (double.IsNaN(value) || value < -1 || value > 1)
        {
            return double.NaN;
        }

        if (value == 1)
        {
            return double.PositiveInfinity;
        }

        if (value == -1)
        {
            return double.NegativeInfinity;
        }

        double magnitude = Math.Abs(value);

        // Past 0.9 the erf curve is flat, so inverting erfc instead keeps the digits the subtraction
        // 1 - |value| would otherwise throw away. The subtraction itself is exact: 1 - p for p in
        // [½, 1] is a difference of two numbers within a factor of two, which a double takes without
        // rounding at all.
        double answer = magnitude <= CentralLimit
            ? Central(magnitude)
            : Tail(1.0 - magnitude);

        return value < 0 ? -answer : answer;
    }

    /// <summary>The inverse complementary error function: the y with erfc(y) = <paramref name="value"/>.</summary>
    public static double ErfcInverse(double value)
    {
        if (double.IsNaN(value) || value < 0 || value > 2)
        {
            return double.NaN;
        }

        if (value == 0)
        {
            return double.PositiveInfinity;
        }

        if (value == 2)
        {
            return double.NegativeInfinity;
        }

        // erfc is symmetric about (0, 1), so the whole upper half is the lower half reflected — and
        // 2 - value is exact there for the same reason 1 - p is above.
        if (value > 1.0)
        {
            return -ErfcInverse(2.0 - value);
        }

        return value >= 1.0 - CentralLimit ? Central(1.0 - value) : Tail(value);
    }

    /// <summary>The y with erf(y) = p, for 0 ≤ p ≤ <see cref="CentralLimit"/>.</summary>
    private static double Central(double p)
    {
        double square = p * p;
        double guess = 0;
        for (int i = CentralGuess.Length - 1; i >= 0; i--)
        {
            guess = (guess * square) + CentralGuess[i];
        }

        double y = p * guess;

        // Halley on f(y) = erf(y) - p. With f'' = -2y·f' the whole second-derivative term collapses
        // to a single multiply: the Newton step u divided by (1 + u·y).
        double u = (Erf(y) - p) * HalfSqrtPi * Math.Exp(y * y);
        return y - (u / (1.0 + (u * y)));
    }

    /// <summary>The y with erfc(y) = q, for 0 &lt; q ≤ 1 - <see cref="CentralLimit"/>.</summary>
    private static double Tail(double q)
    {
        double w = Math.Sqrt(-Math.Log(q));
        double reciprocal = 1.0 / w;
        double guess = 0;
        for (int i = TailGuess.Length - 1; i >= 0; i--)
        {
            guess = (guess * reciprocal) + TailGuess[i];
        }

        double y = w * guess;

        // The refinement runs on ln erfc rather than erfc, written as -y² + ln erfcx(y) so that
        // nothing on the way is ever an underflowed erfc or an overflowed exp(y²) — which is what
        // lets the same two lines answer q = 1e-300 and q = 0.1. The derivative of that logarithm
        // is -(2/√π)/erfcx(y), and it is exactly the erfcx already in hand, so the step is free.
        double scaled = ErfcScaled(y);
        double residual = -(y * y) + Math.Log(scaled) - Math.Log(q);
        double u = -residual * HalfSqrtPi * scaled;
        return y - (u / (1.0 + (u * (y - (OneOverSqrtPi / scaled)))));
    }

    // --- Cody's kernel ----------------------------------------------------------------------------

    /// <summary>Which of the three the shared rational is being asked for.</summary>
    private enum Kind
    {
        Erf,
        Erfc,
        ErfcScaled,
    }

    /// <summary>
    /// The one rational that answers all three, chosen by interval. Written as a single routine
    /// because the three share every coefficient and differ only in what is done with the result:
    /// splitting them into three would be three copies of the same numbers.
    /// </summary>
    private static double Rational(double x, Kind kind)
    {
        double magnitude = Math.Abs(x);
        if (magnitude <= Threshold)
        {
            double square = magnitude <= TooSmallToSquare ? 0.0 : magnitude * magnitude;
            double numerator = A[4] * square;
            double denominator = square;
            for (int i = 0; i < 3; i++)
            {
                numerator = (numerator + A[i]) * square;
                denominator = (denominator + B[i]) * square;
            }

            // The first interval computes erf directly, so the other two are one subtraction away
            // and there is nothing to cancel: erf is at most 0.5 here.
            double erf = x * (numerator + A[3]) / (denominator + B[3]);
            return kind switch
            {
                Kind.Erf => erf,
                Kind.Erfc => 1.0 - erf,
                _ => Math.Exp(square) * (1.0 - erf),
            };
        }

        // Both outer intervals compute the SCALED complement, so erf and erfc pay for one
        // exponential at the end and erfcx pays for none — which is the point of keeping the three
        // together rather than writing erfcx as erfc times an exponential that has just cancelled.
        double result;
        if (magnitude <= 4.0)
        {
            double numerator = C[8] * magnitude;
            double denominator = magnitude;
            for (int i = 0; i < 7; i++)
            {
                numerator = (numerator + C[i]) * magnitude;
                denominator = (denominator + D[i]) * magnitude;
            }

            result = (numerator + C[7]) / (denominator + D[7]);
            if (kind != Kind.ErfcScaled)
            {
                result = TimesNegativeExponentialOfSquare(result, magnitude);
            }
        }
        else if (kind != Kind.ErfcScaled && magnitude >= ErfcUnderflows)
        {
            // erfc has gone under the smallest subnormal there is, so erf is exactly ±1. The guard
            // is not only an economy: past 1.1e307 the split below would square an infinity.
            result = 0.0;
        }
        else if (magnitude >= ErfcScaledIsItsLeadingTerm)
        {
            // 1/(x√π) is the whole of the expansion once 1/x² is below a double's resolution.
            result = OneOverSqrtPi / magnitude;
        }
        else
        {
            double inverseSquare = 1.0 / (magnitude * magnitude);
            double numerator = P[5] * inverseSquare;
            double denominator = inverseSquare;
            for (int i = 0; i < 4; i++)
            {
                numerator = (numerator + P[i]) * inverseSquare;
                denominator = (denominator + Q[i]) * inverseSquare;
            }

            result = inverseSquare * (numerator + P[4]) / (denominator + Q[4]);
            result = (OneOverSqrtPi - result) / magnitude;
            if (kind != Kind.ErfcScaled)
            {
                result = TimesNegativeExponentialOfSquare(result, magnitude);
            }
        }

        // result is now erfc(|x|), or erfcx(|x|) for the scaled kind. The three differ only in how
        // they read that: erf as one minus it, written so the subtraction cannot cancel.
        double complement = (0.5 - result) + 0.5;
        return kind switch
        {
            Kind.Erf => x < 0 ? -complement : complement,
            Kind.Erfc => x < 0 ? 2.0 - result : result,
            _ => x >= 0 ? result : ReflectScaled(x, result),
        };
    }

    /// <summary>erfcx(x) for negative x, which is 2e^{x²} minus erfcx(-x).</summary>
    private static double ReflectScaled(double x, double positiveSide)
    {
        if (x < ErfcScaledOverflows)
        {
            return double.PositiveInfinity;
        }

        double split = Math.Truncate(x * 16.0) / 16.0;
        double rest = (x - split) * (x + split);
        double doubled = Math.Exp(split * split) * Math.Exp(rest);
        return (doubled + doubled) - positiveSide;
    }

    /// <summary>
    /// <paramref name="scaled"/>·e^{-y²}, with the exponent split so it is computed from a number
    /// with few significant bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// y² for y near 4 has already lost three bits to rounding before <see cref="Math.Exp"/> sees
    /// it, and exp multiplies that error by y² again — about a part in 10¹⁴ at the far end of the
    /// second interval. Truncating y to a sixteenth makes t² exact, which leaves the rounded part
    /// in a second exponential whose argument is small enough that its own error does not matter.
    /// </para>
    /// <para>
    /// The multiplication belongs in here rather than at the call site because of where the answer
    /// ends up. Past y = 26 the exponential alone is a subnormal, so forming it first would round
    /// to a couple of dozen bits and then multiply that. Halving the exponent keeps both factors
    /// normal, and folding erfcx in between them means the one product that reaches the subnormals
    /// is the last one — a single rounding instead of two.
    /// </para>
    /// </remarks>
    private static double TimesNegativeExponentialOfSquare(double scaled, double y)
    {
        double split = Math.Truncate(y * 16.0) / 16.0;
        double rest = (y - split) * (y + split);
        if (y < ErfcLeavesTheNormals)
        {
            return scaled * Math.Exp(-split * split) * Math.Exp(-rest);
        }

        double half = Math.Exp(-split * split * 0.5) * Math.Exp(-rest * 0.5);
        return scaled * half * half;
    }
}
