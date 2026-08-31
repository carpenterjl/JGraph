namespace JGraph.Numerics;

/// <summary>
/// The special functions of mathematical physics that a technical language is expected to have:
/// the gamma family, the error functions, the incomplete gamma and beta integrals, and the
/// digamma/polygamma derivatives.
/// </summary>
/// <remarks>
/// Everything here works to close to full double precision and is written in terms of two workhorses
/// — a Lanczos log-gamma and modified-Lentz continued fractions — rather than a table of
/// approximations per function, so accuracy is a property of two pieces of code instead of fifteen.
/// The inverses that remain here bracket their answer and bisect: slower than a Newton polish, but
/// there is no starting guess to go wrong on a hard case. The error functions are the one family
/// that left, to <see cref="ErrorFunctions"/>, and that file says why.
/// </remarks>
public static class SpecialFunctions
{
    /// <summary>ln(√(2π)), the constant in front of every Lanczos evaluation.</summary>
    private const double LogSqrtTwoPi = 0.918938533204672741780329736406;



    /// <summary>The smallest number the continued fractions may divide by without losing control.</summary>
    private const double Tiny = 1e-300;

    /// <summary>The relative accuracy every iteration here is driven to.</summary>
    private const double Epsilon = 1e-15;

    /// <summary>Lanczos coefficients for g = 7, n = 9 — good to about 15 digits over the half plane.</summary>
    private static readonly double[] Lanczos =
    [
        0.99999999999980993,
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7,
    ];

    /// <summary>Bernoulli numbers B₂, B₄, … — the Euler–Maclaurin tail of the polygamma series.</summary>
    private static readonly double[] EvenBernoulli =
    [
        1.0 / 6, -1.0 / 30, 1.0 / 42, -1.0 / 30, 5.0 / 66, -691.0 / 2730, 7.0 / 6,
    ];

    // --- Gamma ------------------------------------------------------------------------------------

    /// <summary>ln|Γ(x)|. Poles (zero and the negative integers) report +∞, as MATLAB does.</summary>
    public static double LogGamma(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x <= 0 && x == Math.Floor(x))
        {
            return double.PositiveInfinity;
        }

        if (x < 0.5)
        {
            // Reflection: the Lanczos sum only converges to the right of the critical line.
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1.0 - x);
        }

        double z = x - 1.0;
        double series = Lanczos[0];
        for (int i = 1; i < Lanczos.Length; i++)
        {
            series += Lanczos[i] / (z + i);
        }

        double t = z + 7.5;
        return LogSqrtTwoPi + ((z + 0.5) * Math.Log(t)) - t + Math.Log(series);
    }

    /// <summary>Γ(x). Negative integers and zero are poles and report ±∞ or NaN as the limit does.</summary>
    public static double Gamma(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x <= 0 && x == Math.Floor(x))
        {
            return double.NaN; // the two-sided limits disagree, so there is no value to report
        }

        if (x < 0.5)
        {
            // Reflection again, but on Γ itself so the sign survives: Γ(x)Γ(1-x) = π/sin(πx).
            return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1.0 - x));
        }

        // Γ of a whole number is a factorial, and multiplying it out is exact where exp(lnΓ) is only
        // close: gamma(3) has to be 2, not 2.0000000000000018.
        if (x == Math.Floor(x) && x <= 171)
        {
            double factorial = 1;
            for (int i = 2; i < (int)x; i++)
            {
                factorial *= i;
            }

            return factorial;
        }

        return Math.Exp(LogGamma(x));
    }

    /// <summary>ln B(a, b), the log beta function.</summary>
    public static double LogBeta(double a, double b) => LogGamma(a) + LogGamma(b) - LogGamma(a + b);

    /// <summary>B(a, b) — computed through logs so large arguments do not overflow on the way.</summary>
    public static double Beta(double a, double b) => Math.Exp(LogBeta(a, b));

    // --- Digamma and polygamma --------------------------------------------------------------------

    /// <summary>ψ(x), the logarithmic derivative of Γ.</summary>
    public static double Digamma(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x <= 0 && x == Math.Floor(x))
        {
            return double.PositiveInfinity;
        }

        if (x < 0)
        {
            // ψ(1-x) - ψ(x) = π·cot(πx).
            return Digamma(1.0 - x) - (Math.PI / Math.Tan(Math.PI * x));
        }

        // The asymptotic series only earns its accuracy well away from the origin, so walk there
        // first with ψ(x) = ψ(x+1) - 1/x and pay one division per step.
        double shifted = 0;
        while (x < 10)
        {
            shifted -= 1.0 / x;
            x += 1.0;
        }

        double inverseSquare = 1.0 / (x * x);
        double series = Math.Log(x) - (0.5 / x);
        double power = inverseSquare;
        series -= power / 12.0;
        power *= inverseSquare;
        series += power / 120.0;
        power *= inverseSquare;
        series -= power / 252.0;
        power *= inverseSquare;
        series += power / 240.0;
        power *= inverseSquare;
        series -= power / 132.0;
        return series + shifted;
    }

    /// <summary>
    /// ψ⁽ᵏ⁾(x), the k-th derivative of the digamma function. k = 0 is <see cref="Digamma"/>; higher
    /// orders go through the Hurwitz zeta identity ψ⁽ᵏ⁾(x) = (-1)^(k+1)·k!·ζ(k+1, x).
    /// </summary>
    public static double Polygamma(int k, double x)
    {
        if (k == 0)
        {
            return Digamma(x);
        }

        if (k < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "The order of a polygamma function is not negative.");
        }

        double factorial = 1;
        for (int i = 2; i <= k; i++)
        {
            factorial *= i;
        }

        double sign = (k % 2) == 0 ? -1.0 : 1.0; // (-1)^(k+1)
        return sign * factorial * HurwitzZeta(k + 1, x);
    }

    /// <summary>ζ(s, a) by Euler–Maclaurin: a partial sum, an integral, and a Bernoulli tail.</summary>
    private static double HurwitzZeta(double s, double a)
    {
        if (a <= 0 && a == Math.Floor(a))
        {
            return double.PositiveInfinity;
        }

        const int Split = 16; // where the direct sum hands over to the asymptotic tail
        double total = 0;
        for (int n = 0; n < Split; n++)
        {
            total += Math.Pow(a + n, -s);
        }

        double edge = a + Split;
        total += Math.Pow(edge, 1.0 - s) / (s - 1.0);
        total += 0.5 * Math.Pow(edge, -s);

        // Σ B₂ⱼ/(2j)! · (s)₍₂ⱼ₋₁₎ · (a+N)^(-s-2j+1): each term folds in two more factors of the
        // rising factorial, which is why the loop carries it rather than recomputing it.
        double rising = s;
        double powered = Math.Pow(edge, -s - 1.0);
        double denominator = 2.0;
        for (int j = 0; j < EvenBernoulli.Length; j++)
        {
            total += EvenBernoulli[j] / denominator * rising * powered;
            rising *= (s + (2 * j) + 1) * (s + (2 * j) + 2);
            powered /= edge * edge;
            denominator *= (2 * j + 3) * (2 * j + 4);
        }

        return total;
    }

    // --- Error functions --------------------------------------------------------------------------

    // The error functions moved to ErrorFunctions in M120 and are forwarded rather than reimplemented,
    // so that every caller here -- the confidence half-widths, the normal quantiles, the script
    // builtins -- reaches the same arithmetic through the same names it always used. They are the one
    // family in this file that a script calls a few million times in a row, and the continued fraction
    // the rest of the file is built on cost 143 times what MATLAB charges for the inverse.

    /// <summary>erf(x), the error function.</summary>
    public static double Erf(double x) => ErrorFunctions.Erf(x);

    /// <summary>erfc(x) = 1 - erf(x), evaluated so that the tail keeps its significant digits.</summary>
    public static double Erfc(double x) => ErrorFunctions.Erfc(x);

    /// <summary>
    /// exp(x^2)*erfc(x), the scaled complementary error function. It exists because erfc(30) is
    /// 2.6e-393 -- zero as a double -- while this is 0.0187, and the ratio is what most formulas want.
    /// </summary>
    public static double ErfcScaled(double x) => ErrorFunctions.ErfcScaled(x);

    /// <summary>The inverse error function: the y with erf(y) = <paramref name="value"/>.</summary>
    public static double ErfInverse(double value) => ErrorFunctions.ErfInverse(value);

    /// <summary>The inverse complementary error function: the y with erfc(y) = <paramref name="value"/>.</summary>
    public static double ErfcInverse(double value) => ErrorFunctions.ErfcInverse(value);

    // --- Incomplete gamma -------------------------------------------------------------------------

    /// <summary>P(a, x), the regularized lower incomplete gamma — MATLAB's <c>gammainc</c>.</summary>
    public static double GammaLower(double a, double x)
    {
        if (double.IsNaN(a) || double.IsNaN(x) || x < 0 || a <= 0)
        {
            return double.NaN;
        }

        if (x == 0)
        {
            return 0;
        }

        // The series converges quickly below the peak of the integrand and the continued fraction
        // above it; a + 1 is where they change places.
        return x < a + 1.0
            ? LowerGammaSeries(a, x)
            : 1.0 - (Math.Exp(-x + (a * Math.Log(x)) - LogGamma(a)) * UpperGammaFraction(a, x));
    }

    /// <summary>Q(a, x) = 1 - P(a, x), the regularized upper incomplete gamma.</summary>
    public static double GammaUpper(double a, double x)
    {
        if (double.IsNaN(a) || double.IsNaN(x) || x < 0 || a <= 0)
        {
            return double.NaN;
        }

        return x < a + 1.0
            ? 1.0 - LowerGammaSeries(a, x)
            : Math.Exp(-x + (a * Math.Log(x)) - LogGamma(a)) * UpperGammaFraction(a, x);
    }

    /// <summary>The x with P(a, x) = <paramref name="p"/> (or Q(a, x) = p when <paramref name="upper"/>).</summary>
    public static double GammaInverse(double a, double p, bool upper = false)
    {
        if (double.IsNaN(a) || double.IsNaN(p) || p < 0 || p > 1 || a <= 0)
        {
            return double.NaN;
        }

        if (p == (upper ? 1.0 : 0.0))
        {
            return 0;
        }

        if (p == (upper ? 0.0 : 1.0))
        {
            return double.PositiveInfinity;
        }

        Func<double, double> f = upper ? x => GammaUpper(a, x) : x => GammaLower(a, x);
        return Solve(p, f, 0, Bracket(f, p, a, upper), decreasing: upper);
    }

    /// <summary>Series for P(a, x), summing Γ(a)x^a e^-x · Σ xⁿ/(a(a+1)…(a+n)).</summary>
    private static double LowerGammaSeries(double a, double x)
    {
        double denominator = a;
        double term = 1.0 / a;
        double sum = term;
        for (int n = 0; n < 1000; n++)
        {
            denominator += 1.0;
            term *= x / denominator;
            sum += term;
            if (Math.Abs(term) < Math.Abs(sum) * Epsilon)
            {
                break;
            }
        }

        return sum * Math.Exp(-x + (a * Math.Log(x)) - LogGamma(a));
    }

    /// <summary>
    /// The continued fraction h in Q(a, x) = e^-x·x^a/Γ(a)·h, by modified Lentz. Returning h rather
    /// than Q is what lets erfcx cancel the exponential exactly instead of computing then undoing it.
    /// </summary>
    private static double UpperGammaFraction(double a, double x)
    {
        double b = x + 1.0 - a;
        double c = 1.0 / Tiny;
        double d = 1.0 / b;
        double h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = (an * d) + b;
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = b + (an / c);
            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1.0 / d;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1.0) < Epsilon)
            {
                break;
            }
        }

        return h;
    }

    // --- Incomplete beta --------------------------------------------------------------------------

    /// <summary>I_x(a, b), the regularized incomplete beta integral — MATLAB's <c>betainc</c>.</summary>
    public static double BetaRegularized(double x, double a, double b)
    {
        if (double.IsNaN(x) || double.IsNaN(a) || double.IsNaN(b) || x < 0 || x > 1 || a <= 0 || b <= 0)
        {
            return double.NaN;
        }

        if (x == 0 || x == 1)
        {
            return x;
        }

        double front = Math.Exp((a * Math.Log(x)) + (b * Math.Log(1.0 - x)) - LogBeta(a, b));

        // The continued fraction converges fast only on the shallow side of the distribution's
        // centre; the symmetry I_x(a,b) = 1 - I_(1-x)(b,a) puts every call on that side.
        return x < (a + 1.0) / (a + b + 2.0)
            ? front * BetaFraction(x, a, b) / a
            : 1.0 - (front * BetaFraction(1.0 - x, b, a) / b);
    }

    /// <summary>The x with I_x(a, b) = <paramref name="p"/>.</summary>
    public static double BetaInverse(double p, double a, double b)
    {
        if (double.IsNaN(p) || double.IsNaN(a) || double.IsNaN(b) || p < 0 || p > 1 || a <= 0 || b <= 0)
        {
            return double.NaN;
        }

        return p is 0 or 1 ? p : Solve(p, x => BetaRegularized(x, a, b), 0, 1);
    }

    /// <summary>Lentz's continued fraction for the incomplete beta integral.</summary>
    private static double BetaFraction(double x, double a, double b)
    {
        double sum = a + b;
        double plus = a + 1.0;
        double minus = a - 1.0;
        double c = 1.0;
        double d = 1.0 - (sum * x / plus);
        if (Math.Abs(d) < Tiny)
        {
            d = Tiny;
        }

        d = 1.0 / d;
        double h = d;
        for (int m = 1; m < 1000; m++)
        {
            int even = 2 * m;

            // The fraction alternates between two shapes of numerator, so each pass does both.
            double numerator = m * (b - m) * x / ((minus + even) * (a + even));
            d = 1.0 + (numerator * d);
            c = 1.0 + (numerator / c);
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1.0 / d;
            h *= d * c;

            numerator = -(a + m) * (sum + m) * x / ((a + even) * (plus + even));
            d = 1.0 + (numerator * d);
            c = 1.0 + (numerator / c);
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1.0 / d;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1.0) < Epsilon)
            {
                break;
            }
        }

        return h;
    }

    // --- Root finding -----------------------------------------------------------------------------

    /// <summary>
    /// The x in [<paramref name="low"/>, <paramref name="high"/>] with f(x) = <paramref name="target"/>,
    /// by bisection. Every inverse here is monotone, which makes bisection unconditionally correct —
    /// there is no starting guess for a badly conditioned case to spoil.
    /// </summary>
    private static double Solve(double target, Func<double, double> f, double low, double high, bool decreasing = false)
    {
        for (int i = 0; i < 200; i++)
        {
            double middle = (low + high) / 2.0;
            if (middle == low || middle == high)
            {
                return middle; // the interval is down to adjacent doubles
            }

            double value = f(middle);
            if (value == target)
            {
                return middle; // an exactly representable root, such as erfinv(0) = 0
            }

            bool below = decreasing ? value > target : value < target;
            if (below)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2.0;
    }

    /// <summary>An upper bound for the incomplete-gamma inverse, found by doubling until it holds.</summary>
    private static double Bracket(Func<double, double> f, double p, double a, bool upper)
    {
        double high = Math.Max(a, 1.0);
        for (int i = 0; i < 200; i++)
        {
            if (upper ? f(high) <= p : f(high) >= p)
            {
                return high;
            }

            high *= 2.0;
        }

        return high;
    }
}
