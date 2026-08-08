using JGraph.Numerics;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// The density, distribution, quantile, moment and sampling functions of the continuous families the
/// Statistics Toolbox documents. Pure functions over plain doubles: the scripting layer does the
/// argument reading, the broadcasting and the size arguments.
/// </summary>
/// <remarks>
/// <para>
/// Every parameterization here is MATLAB's, which is not always the textbook's. The exponential is
/// given by its <em>mean</em> and not its rate; the gamma by shape and <em>scale</em>; the Weibull's
/// first argument is the scale and its second the shape, the opposite order to the way the density is
/// usually written; and <c>ev</c> is the smallest-extreme-value distribution, not the largest. Getting
/// any of these backwards produces answers that look plausible, so each one is stated on its function.
/// </para>
/// <para>
/// Out-of-domain arguments answer NaN rather than throwing, because these are elementwise functions
/// over arrays: one impossible parameter in a vector must not destroy the rest of the answer.
/// </para>
/// </remarks>
public static class ContinuousDistributions
{
    /// <summary>The Euler–Mascheroni constant, which is the mean of a standard Gumbel.</summary>
    public const double EulerMascheroni = 0.57721566490153286060651209008240243;

    private const double Sqrt2 = 1.4142135623730950488;
    private const double Log2Pi = 1.8378770664093454836;

    // --- Normal -----------------------------------------------------------------------------------

    /// <summary>The normal density at <paramref name="x"/> with mean and standard deviation.</summary>
    public static double NormalPdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu))
        {
            return double.NaN;
        }

        double z = (x - mu) / sigma;
        return Math.Exp((-0.5 * z * z) - (0.5 * Log2Pi)) / sigma;
    }

    /// <summary>The normal distribution function.</summary>
    public static double NormalCdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu))
        {
            return double.NaN;
        }

        // erfc rather than 1 + erf, so the left tail keeps its significant figures.
        return 0.5 * SpecialFunctions.Erfc(-(x - mu) / (sigma * Sqrt2));
    }

    /// <summary>The normal quantile.</summary>
    public static double NormalInv(double p, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(mu))
        {
            return double.NaN;
        }

        if (p == 0) return double.NegativeInfinity;
        if (p == 1) return double.PositiveInfinity;
        return mu - (sigma * Sqrt2 * SpecialFunctions.ErfcInverse(2 * p));
    }

    /// <summary>The mean and variance of a normal.</summary>
    public static (double Mean, double Variance) NormalStat(double mu, double sigma) =>
        sigma > 0 ? (mu, sigma * sigma) : (double.NaN, double.NaN);

    // --- Exponential ------------------------------------------------------------------------------

    /// <summary>
    /// The exponential density. The parameter is the distribution's <em>mean</em>, MATLAB's
    /// convention, not the rate — so a mean of 2 halves the density at the origin rather than doubling
    /// it.
    /// </summary>
    public static double ExponentialPdf(double x, double mu)
    {
        if (!(mu > 0) || double.IsNaN(x)) return double.NaN;
        return x < 0 ? 0 : Math.Exp(-x / mu) / mu;
    }

    /// <summary>The exponential distribution function, by mean.</summary>
    public static double ExponentialCdf(double x, double mu)
    {
        if (!(mu > 0) || double.IsNaN(x)) return double.NaN;
        return x <= 0 ? 0 : -double.ExpM1(-x / mu);
    }

    /// <summary>The exponential quantile, by mean.</summary>
    public static double ExponentialInv(double p, double mu)
    {
        if (!(mu > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        return -mu * Math.Log(1 - p);
    }

    /// <summary>The mean and variance of an exponential.</summary>
    public static (double Mean, double Variance) ExponentialStat(double mu) =>
        mu > 0 ? (mu, mu * mu) : (double.NaN, double.NaN);

    // --- Gamma ------------------------------------------------------------------------------------

    /// <summary>
    /// The gamma density with shape <paramref name="a"/> and <em>scale</em> <paramref name="b"/> —
    /// MATLAB's second argument is the scale, so the mean is <c>a*b</c> and not <c>a/b</c>.
    /// </summary>
    public static double GammaPdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        if (x < 0) return 0;
        if (x == 0) return a < 1 ? double.PositiveInfinity : a == 1 ? 1 / b : 0;

        double z = x / b;
        return Math.Exp(((a - 1) * Math.Log(z)) - z - SpecialFunctions.LogGamma(a)) / b;
    }

    /// <summary>The gamma distribution function, shape and scale.</summary>
    public static double GammaCdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        return x <= 0 ? 0 : SpecialFunctions.GammaLower(a, x / b);
    }

    /// <summary>The gamma quantile, shape and scale.</summary>
    public static double GammaInv(double p, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return 0;
        if (p == 1) return double.PositiveInfinity;
        return b * SpecialFunctions.GammaInverse(a, p);
    }

    /// <summary>The mean and variance of a gamma.</summary>
    public static (double Mean, double Variance) GammaStat(double a, double b) =>
        a > 0 && b > 0 ? (a * b, a * b * b) : (double.NaN, double.NaN);

    // --- Beta -------------------------------------------------------------------------------------

    /// <summary>The beta density on the unit interval.</summary>
    public static double BetaPdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        if (x < 0 || x > 1) return 0;
        if (x == 0) return a < 1 ? double.PositiveInfinity : a == 1 ? b : 0;
        if (x == 1) return b < 1 ? double.PositiveInfinity : b == 1 ? a : 0;

        return Math.Exp(((a - 1) * Math.Log(x)) + ((b - 1) * double.LogP1(-x)) - SpecialFunctions.LogBeta(a, b));
    }

    /// <summary>The beta distribution function.</summary>
    public static double BetaCdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        return SpecialFunctions.BetaRegularized(x, a, b);
    }

    /// <summary>The beta quantile.</summary>
    public static double BetaInv(double p, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return 0;
        if (p == 1) return 1;
        return SpecialFunctions.BetaInverse(p, a, b);
    }

    /// <summary>The mean and variance of a beta.</summary>
    public static (double Mean, double Variance) BetaStat(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return (double.NaN, double.NaN);
        double total = a + b;
        return (a / total, a * b / (total * total * (total + 1)));
    }

    // --- Chi-square -------------------------------------------------------------------------------

    /// <summary>The chi-square density, which is a gamma of shape v/2 and scale 2.</summary>
    public static double Chi2Pdf(double x, double v) => GammaPdf(x, v / 2, 2);

    /// <summary>The chi-square distribution function.</summary>
    public static double Chi2Cdf(double x, double v) => GammaCdf(x, v / 2, 2);

    /// <summary>The chi-square quantile.</summary>
    public static double Chi2Inv(double p, double v) => GammaInv(p, v / 2, 2);

    /// <summary>The mean and variance of a chi-square.</summary>
    public static (double Mean, double Variance) Chi2Stat(double v) =>
        v > 0 ? (v, 2 * v) : (double.NaN, double.NaN);

    // --- Student's t ------------------------------------------------------------------------------

    /// <summary>Student's t density.</summary>
    public static double TPdf(double x, double v)
    {
        if (!(v > 0) || double.IsNaN(x)) return double.NaN;

        double half = (v + 1) / 2;
        double log = SpecialFunctions.LogGamma(half) - SpecialFunctions.LogGamma(v / 2)
            - (0.5 * Math.Log(v * Math.PI)) - (half * double.LogP1(x * x / v));
        return Math.Exp(log);
    }

    /// <summary>Student's t distribution function.</summary>
    public static double TCdf(double x, double v)
    {
        if (!(v > 0) || double.IsNaN(x)) return double.NaN;
        if (double.IsNegativeInfinity(x)) return 0;
        if (double.IsPositiveInfinity(x)) return 1;

        // The regularized incomplete beta at v/(v + x²) is twice the tail beyond |x|, and taking it
        // from whichever side is small keeps the far tail from being computed as 1 minus almost 1.
        double tail = 0.5 * SpecialFunctions.BetaRegularized(v / (v + (x * x)), v / 2, 0.5);
        return x > 0 ? 1 - tail : tail;
    }

    /// <summary>Student's t quantile.</summary>
    public static double TInv(double p, double v)
    {
        if (!(v > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return double.NegativeInfinity;
        if (p == 1) return double.PositiveInfinity;
        if (p == 0.5) return 0;

        double tail = 2 * Math.Min(p, 1 - p);
        double w = SpecialFunctions.BetaInverse(tail, v / 2, 0.5);
        double x = Math.Sqrt(v * (1 - w) / w);
        return p > 0.5 ? x : -x;
    }

    /// <summary>
    /// The mean and variance of Student's t. The mean exists only above one degree of freedom and the
    /// variance only above two; below those MATLAB answers NaN rather than a number.
    /// </summary>
    public static (double Mean, double Variance) TStat(double v)
    {
        if (!(v > 0)) return (double.NaN, double.NaN);
        double mean = v > 1 ? 0 : double.NaN;
        double variance = v > 2 ? v / (v - 2) : double.NaN;
        return (mean, variance);
    }

    // --- F ----------------------------------------------------------------------------------------

    /// <summary>The F density with numerator and denominator degrees of freedom.</summary>
    public static double FPdf(double x, double v1, double v2)
    {
        if (!(v1 > 0) || !(v2 > 0) || double.IsNaN(x)) return double.NaN;
        if (x < 0) return 0;
        if (x == 0) return v1 < 2 ? double.PositiveInfinity : v1 == 2 ? 1 : 0;

        double log = ((v1 / 2) * Math.Log(v1 / v2)) + (((v1 / 2) - 1) * Math.Log(x))
            - (((v1 + v2) / 2) * double.LogP1(v1 * x / v2)) - SpecialFunctions.LogBeta(v1 / 2, v2 / 2);
        return Math.Exp(log);
    }

    /// <summary>The F distribution function.</summary>
    public static double FCdf(double x, double v1, double v2)
    {
        if (!(v1 > 0) || !(v2 > 0) || double.IsNaN(x)) return double.NaN;
        if (x <= 0) return 0;
        if (double.IsPositiveInfinity(x)) return 1;
        return SpecialFunctions.BetaRegularized(v1 * x / ((v1 * x) + v2), v1 / 2, v2 / 2);
    }

    /// <summary>The F quantile.</summary>
    public static double FInv(double p, double v1, double v2)
    {
        if (!(v1 > 0) || !(v2 > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return 0;
        if (p == 1) return double.PositiveInfinity;

        double w = SpecialFunctions.BetaInverse(p, v1 / 2, v2 / 2);
        return w >= 1 ? double.PositiveInfinity : v2 * w / (v1 * (1 - w));
    }

    /// <summary>
    /// The mean and variance of an F. The mean needs more than two denominator degrees of freedom and
    /// the variance more than four.
    /// </summary>
    public static (double Mean, double Variance) FStat(double v1, double v2)
    {
        if (!(v1 > 0) || !(v2 > 0)) return (double.NaN, double.NaN);

        double mean = v2 > 2 ? v2 / (v2 - 2) : double.NaN;
        double variance = v2 > 4
            ? 2 * v2 * v2 * (v1 + v2 - 2) / (v1 * (v2 - 2) * (v2 - 2) * (v2 - 4))
            : double.NaN;
        return (mean, variance);
    }

    // --- Uniform ----------------------------------------------------------------------------------

    /// <summary>The continuous uniform density on [a, b].</summary>
    public static double UniformPdf(double x, double a, double b)
    {
        if (double.IsNaN(x) || double.IsNaN(a) || double.IsNaN(b) || !(a < b)) return double.NaN;
        return x >= a && x <= b ? 1 / (b - a) : 0;
    }

    /// <summary>The continuous uniform distribution function.</summary>
    public static double UniformCdf(double x, double a, double b)
    {
        if (double.IsNaN(x) || double.IsNaN(a) || double.IsNaN(b) || !(a < b)) return double.NaN;
        if (x <= a) return 0;
        if (x >= b) return 1;
        return (x - a) / (b - a);
    }

    /// <summary>The continuous uniform quantile.</summary>
    public static double UniformInv(double p, double a, double b)
    {
        if (double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(a) || double.IsNaN(b) || !(a < b))
        {
            return double.NaN;
        }

        return a + (p * (b - a));
    }

    /// <summary>The mean and variance of a continuous uniform.</summary>
    public static (double Mean, double Variance) UniformStat(double a, double b) =>
        a < b ? ((a + b) / 2, (b - a) * (b - a) / 12) : (double.NaN, double.NaN);

    // --- Lognormal --------------------------------------------------------------------------------

    /// <summary>
    /// The lognormal density. Its two parameters describe the <em>logarithm</em> of the variable, so
    /// <c>mu</c> is not the mean of the data and <c>sigma</c> is not its standard deviation.
    /// </summary>
    public static double LognormalPdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu)) return double.NaN;
        return x <= 0 ? 0 : NormalPdf(Math.Log(x), mu, sigma) / x;
    }

    /// <summary>The lognormal distribution function.</summary>
    public static double LognormalCdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu)) return double.NaN;
        return x <= 0 ? 0 : NormalCdf(Math.Log(x), mu, sigma);
    }

    /// <summary>The lognormal quantile.</summary>
    public static double LognormalInv(double p, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(mu)) return double.NaN;
        return p == 0 ? 0 : Math.Exp(NormalInv(p, mu, sigma));
    }

    /// <summary>The mean and variance of a lognormal, on the data's own scale.</summary>
    public static (double Mean, double Variance) LognormalStat(double mu, double sigma)
    {
        if (!(sigma > 0)) return (double.NaN, double.NaN);
        double v = sigma * sigma;
        double mean = Math.Exp(mu + (v / 2));
        return (mean, double.ExpM1(v) * Math.Exp((2 * mu) + v));
    }

    // --- Weibull ----------------------------------------------------------------------------------

    /// <summary>
    /// The Weibull density with <em>scale</em> <paramref name="a"/> and <em>shape</em>
    /// <paramref name="b"/>. MATLAB puts the scale first, which is the reverse of the order the
    /// density is usually written in.
    /// </summary>
    public static double WeibullPdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        if (x < 0) return 0;
        if (x == 0) return b < 1 ? double.PositiveInfinity : b == 1 ? 1 / a : 0;

        double z = x / a;
        return Math.Exp(Math.Log(b / a) + ((b - 1) * Math.Log(z)) - Math.Pow(z, b));
    }

    /// <summary>The Weibull distribution function, scale then shape.</summary>
    public static double WeibullCdf(double x, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(x)) return double.NaN;
        return x <= 0 ? 0 : -double.ExpM1(-Math.Pow(x / a, b));
    }

    /// <summary>The Weibull quantile, scale then shape.</summary>
    public static double WeibullInv(double p, double a, double b)
    {
        if (!(a > 0) || !(b > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 1) return double.PositiveInfinity;
        return a * Math.Pow(-Math.Log(1 - p), 1 / b);
    }

    /// <summary>The mean and variance of a Weibull.</summary>
    public static (double Mean, double Variance) WeibullStat(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return (double.NaN, double.NaN);
        double g1 = SpecialFunctions.Gamma(1 + (1 / b));
        double g2 = SpecialFunctions.Gamma(1 + (2 / b));
        return (a * g1, a * a * (g2 - (g1 * g1)));
    }

    // --- Extreme value (smallest) -----------------------------------------------------------------

    /// <summary>
    /// The extreme value density. MATLAB's <c>ev</c> family is the type 1 distribution of the
    /// <em>smallest</em> extreme — its long tail runs to the left, not the right, and it is the
    /// distribution of the log of a Weibull variable negated.
    /// </summary>
    public static double ExtremeValuePdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu)) return double.NaN;
        double z = (x - mu) / sigma;
        return Math.Exp(z - Math.Exp(z)) / sigma;
    }

    /// <summary>The extreme value (smallest) distribution function.</summary>
    public static double ExtremeValueCdf(double x, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(mu)) return double.NaN;
        return -double.ExpM1(-Math.Exp((x - mu) / sigma));
    }

    /// <summary>The extreme value (smallest) quantile.</summary>
    public static double ExtremeValueInv(double p, double mu, double sigma)
    {
        if (!(sigma > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(mu)) return double.NaN;
        if (p == 0) return double.NegativeInfinity;
        if (p == 1) return double.PositiveInfinity;
        return mu + (sigma * Math.Log(-double.LogP1(-p)));
    }

    /// <summary>The mean and variance of an extreme value (smallest).</summary>
    public static (double Mean, double Variance) ExtremeValueStat(double mu, double sigma) =>
        sigma > 0
            ? (mu - (sigma * EulerMascheroni), sigma * sigma * Math.PI * Math.PI / 6)
            : (double.NaN, double.NaN);

    // --- Generalized extreme value ----------------------------------------------------------------

    /// <summary>
    /// The generalized extreme value density, shape then scale then location. A shape of exactly zero
    /// is the Gumbel limit, which is a separate formula rather than a value the general one reaches.
    /// </summary>
    public static double GeneralizedExtremeValuePdf(double x, double k, double sigma, double mu)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(k) || double.IsNaN(mu)) return double.NaN;

        double z = (x - mu) / sigma;
        if (k == 0)
        {
            return Math.Exp(-z - Math.Exp(-z)) / sigma;
        }

        double t = 1 + (k * z);
        if (t <= 0) return 0;

        // Written through u = t^(-1/k) rather than through t directly: the density is u^(k+1) e^(-u),
        // which stays finite where t^(-1-1/k) would be a large power of a number near zero.
        double u = Math.Pow(t, -1 / k);
        return Math.Pow(u, k + 1) * Math.Exp(-u) / sigma;
    }

    /// <summary>The generalized extreme value distribution function.</summary>
    public static double GeneralizedExtremeValueCdf(double x, double k, double sigma, double mu)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(k) || double.IsNaN(mu)) return double.NaN;

        double z = (x - mu) / sigma;
        if (k == 0)
        {
            return Math.Exp(-Math.Exp(-z));
        }

        double t = 1 + (k * z);
        if (t <= 0)
        {
            // Past the finite end of the support: everything below for a bounded upper tail (k < 0),
            // nothing at all below the bounded lower tail (k > 0).
            return k > 0 ? 0 : 1;
        }

        return Math.Exp(-Math.Pow(t, -1 / k));
    }

    /// <summary>The generalized extreme value quantile.</summary>
    public static double GeneralizedExtremeValueInv(double p, double k, double sigma, double mu)
    {
        if (!(sigma > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(k) || double.IsNaN(mu))
        {
            return double.NaN;
        }

        if (k == 0)
        {
            if (p == 0) return double.NegativeInfinity;
            if (p == 1) return double.PositiveInfinity;
            return mu - (sigma * Math.Log(-Math.Log(p)));
        }

        if (p == 0) return k > 0 ? mu - (sigma / k) : double.NegativeInfinity;
        if (p == 1) return k > 0 ? double.PositiveInfinity : mu - (sigma / k);
        return mu + (sigma * (Math.Pow(-Math.Log(p), -k) - 1) / k);
    }

    /// <summary>
    /// The mean and variance of a generalized extreme value. Neither exists for every shape: the mean
    /// needs a shape below one and the variance a shape below one half.
    /// </summary>
    public static (double Mean, double Variance) GeneralizedExtremeValueStat(
        double k, double sigma, double mu)
    {
        if (!(sigma > 0) || double.IsNaN(k) || double.IsNaN(mu)) return (double.NaN, double.NaN);

        if (k == 0)
        {
            return (mu + (sigma * EulerMascheroni), sigma * sigma * Math.PI * Math.PI / 6);
        }

        double g1 = k < 1 ? SpecialFunctions.Gamma(1 - k) : double.NaN;
        double g2 = k < 0.5 ? SpecialFunctions.Gamma(1 - (2 * k)) : double.NaN;
        double mean = k < 1 ? mu + (sigma * (g1 - 1) / k) : double.NaN;
        double variance = k < 0.5 ? sigma * sigma * (g2 - (g1 * g1)) / (k * k) : double.NaN;
        return (mean, variance);
    }

    // --- Generalized Pareto -----------------------------------------------------------------------

    /// <summary>
    /// The generalized Pareto density, shape then scale then threshold. A shape of zero is the
    /// exponential limit; a negative shape gives a support with a finite upper end.
    /// </summary>
    public static double GeneralizedParetoPdf(double x, double k, double sigma, double theta)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(k) || double.IsNaN(theta)) return double.NaN;

        double z = (x - theta) / sigma;
        if (z < 0) return 0;
        if (k == 0) return Math.Exp(-z) / sigma;

        double t = 1 + (k * z);
        if (t <= 0) return 0;
        return Math.Pow(t, (-1 / k) - 1) / sigma;
    }

    /// <summary>The generalized Pareto distribution function.</summary>
    public static double GeneralizedParetoCdf(double x, double k, double sigma, double theta)
    {
        if (!(sigma > 0) || double.IsNaN(x) || double.IsNaN(k) || double.IsNaN(theta)) return double.NaN;

        double z = (x - theta) / sigma;
        if (z <= 0) return 0;
        if (k == 0) return -double.ExpM1(-z);

        double t = 1 + (k * z);
        if (t <= 0) return 1;
        return 1 - Math.Pow(t, -1 / k);
    }

    /// <summary>The generalized Pareto quantile.</summary>
    public static double GeneralizedParetoInv(double p, double k, double sigma, double theta)
    {
        if (!(sigma > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(k) || double.IsNaN(theta))
        {
            return double.NaN;
        }

        if (k == 0)
        {
            return p == 1 ? double.PositiveInfinity : theta - (sigma * double.LogP1(-p));
        }

        if (p == 1)
        {
            return k > 0 ? double.PositiveInfinity : theta - (sigma / k);
        }

        return theta + (sigma * (Math.Pow(1 - p, -k) - 1) / k);
    }

    /// <summary>
    /// The mean and variance of a generalized Pareto; the mean needs a shape below one and the
    /// variance a shape below one half.
    /// </summary>
    public static (double Mean, double Variance) GeneralizedParetoStat(
        double k, double sigma, double theta)
    {
        if (!(sigma > 0) || double.IsNaN(k) || double.IsNaN(theta)) return (double.NaN, double.NaN);

        double mean = k < 1 ? theta + (sigma / (1 - k)) : double.NaN;
        double variance = k < 0.5 ? sigma * sigma / ((1 - k) * (1 - k) * (1 - (2 * k))) : double.NaN;
        return (mean, variance);
    }

    // --- Rayleigh ---------------------------------------------------------------------------------

    /// <summary>The Rayleigh density.</summary>
    public static double RayleighPdf(double x, double b)
    {
        if (!(b > 0) || double.IsNaN(x)) return double.NaN;
        if (x < 0) return 0;
        return x / (b * b) * Math.Exp(-x * x / (2 * b * b));
    }

    /// <summary>The Rayleigh distribution function.</summary>
    public static double RayleighCdf(double x, double b)
    {
        if (!(b > 0) || double.IsNaN(x)) return double.NaN;
        return x <= 0 ? 0 : -double.ExpM1(-x * x / (2 * b * b));
    }

    /// <summary>The Rayleigh quantile.</summary>
    public static double RayleighInv(double p, double b)
    {
        if (!(b > 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 1) return double.PositiveInfinity;
        return b * Math.Sqrt(-2 * double.LogP1(-p));
    }

    /// <summary>The mean and variance of a Rayleigh.</summary>
    public static (double Mean, double Variance) RayleighStat(double b) =>
        b > 0
            ? (b * Math.Sqrt(Math.PI / 2), (2 - (Math.PI / 2)) * b * b)
            : (double.NaN, double.NaN);

    // --- Noncentral chi-square --------------------------------------------------------------------

    /// <summary>
    /// How many terms of a Poisson mixture are summed before the remaining weight is negligible, and
    /// how small that remaining weight has to be.
    /// </summary>
    private const int MixtureTerms = 2000;
    private const double MixtureTolerance = 1e-14;

    /// <summary>
    /// The noncentral chi-square density. It is a Poisson mixture of central chi-squares: a draw from
    /// Poisson(δ/2) picks how many extra pairs of degrees of freedom the central chi-square gets.
    /// </summary>
    public static double NoncentralChi2Pdf(double x, double v, double delta) =>
        PoissonMixture(v, delta, (df, _) => Chi2Pdf(x, df));

    /// <summary>The noncentral chi-square distribution function.</summary>
    public static double NoncentralChi2Cdf(double x, double v, double delta) =>
        PoissonMixture(v, delta, (df, _) => Chi2Cdf(x, df));

    /// <summary>The noncentral chi-square quantile, found by searching its distribution function.</summary>
    public static double NoncentralChi2Inv(double p, double v, double delta)
    {
        if (!(v > 0) || !(delta >= 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return 0;
        if (p == 1) return double.PositiveInfinity;
        return SearchQuantile(x => NoncentralChi2Cdf(x, v, delta), p, 0, double.PositiveInfinity, v + delta);
    }

    /// <summary>The mean and variance of a noncentral chi-square.</summary>
    public static (double Mean, double Variance) NoncentralChi2Stat(double v, double delta) =>
        v > 0 && delta >= 0 ? (v + delta, 2 * (v + (2 * delta))) : (double.NaN, double.NaN);

    // --- Noncentral F -----------------------------------------------------------------------------

    /// <summary>
    /// The noncentral F density, the same Poisson mixture applied to the numerator's degrees of
    /// freedom — with the argument rescaled, since adding degrees of freedom to the numerator changes
    /// what an F of a given value means.
    /// </summary>
    public static double NoncentralFPdf(double x, double v1, double v2, double delta)
    {
        if (!(v1 > 0) || !(v2 > 0) || !(delta >= 0) || double.IsNaN(x)) return double.NaN;
        return PoissonMixtureOn(v1, delta, (df, _) => df / v1 * FPdf(x * v1 / df, df, v2));
    }

    /// <summary>The noncentral F distribution function.</summary>
    public static double NoncentralFCdf(double x, double v1, double v2, double delta)
    {
        if (!(v1 > 0) || !(v2 > 0) || !(delta >= 0) || double.IsNaN(x)) return double.NaN;
        return PoissonMixtureOn(v1, delta, (df, _) => FCdf(x * v1 / df, df, v2));
    }

    /// <summary>The noncentral F quantile.</summary>
    public static double NoncentralFInv(double p, double v1, double v2, double delta)
    {
        if (!(v1 > 0) || !(v2 > 0) || !(delta >= 0) || double.IsNaN(p) || p < 0 || p > 1) return double.NaN;
        if (p == 0) return 0;
        if (p == 1) return double.PositiveInfinity;
        return SearchQuantile(x => NoncentralFCdf(x, v1, v2, delta), p, 0, double.PositiveInfinity, 1);
    }

    /// <summary>The mean and variance of a noncentral F.</summary>
    public static (double Mean, double Variance) NoncentralFStat(double v1, double v2, double delta)
    {
        if (!(v1 > 0) || !(v2 > 0) || !(delta >= 0)) return (double.NaN, double.NaN);

        double mean = v2 > 2 ? v2 * (v1 + delta) / (v1 * (v2 - 2)) : double.NaN;
        double variance = v2 > 4
            ? 2 * (((v1 + delta) * (v1 + delta)) + ((v1 + (2 * delta)) * (v2 - 2)))
                / ((v2 - 2) * (v2 - 2) * (v2 - 4)) * (v2 / v1) * (v2 / v1)
            : double.NaN;
        return (mean, variance);
    }

    // --- Noncentral t -----------------------------------------------------------------------------

    /// <summary>
    /// The noncentral t distribution function. Unlike the other two noncentral families this one is
    /// not a mixture over degrees of freedom: it is the standard series in the incomplete beta, split
    /// into the even and odd halves that the two Poisson-like weight sequences multiply.
    /// </summary>
    public static double NoncentralTCdf(double x, double v, double delta)
    {
        if (!(v > 0) || double.IsNaN(x) || double.IsNaN(delta)) return double.NaN;

        // The series is written for a non-negative argument; the reflection turns the other case into
        // it, and is exact rather than approximate.
        if (x < 0)
        {
            return 1 - NoncentralTCdf(-x, v, -delta);
        }

        double y = x * x / ((x * x) + v);
        double half = delta * delta / 2;
        double logWeight = -half;
        double total = NormalCdf(-delta, 0, 1);

        double sum = 0;
        for (int j = 0; j < MixtureTerms; j++)
        {
            double even = Math.Exp(logWeight + (j * Math.Log(Math.Max(half, double.Epsilon))) - SpecialFunctions.LogGamma(j + 1));
            if (half == 0 && j > 0)
            {
                even = 0;
            }

            double odd = delta / Sqrt2 * Math.Exp(-half + (j * Math.Log(Math.Max(half, double.Epsilon)))
                - SpecialFunctions.LogGamma(j + 1.5));
            if (half == 0 && j > 0)
            {
                odd = 0;
            }

            double term = (even * SpecialFunctions.BetaRegularized(y, j + 0.5, v / 2))
                + (odd * SpecialFunctions.BetaRegularized(y, j + 1, v / 2));
            sum += term;

            if (j > half && Math.Abs(term) < MixtureTolerance)
            {
                break;
            }
        }

        double answer = total + (0.5 * sum);
        return Math.Clamp(answer, 0, 1);
    }

    /// <summary>
    /// The noncentral t density, read off its distribution function through the identity
    /// <c>f(x) = (v/x)(F(x; v+2, δ) − F(x; v, δ))</c>, with the value at the origin taken from the
    /// closed form the identity cannot reach.
    /// </summary>
    public static double NoncentralTPdf(double x, double v, double delta)
    {
        if (!(v > 0) || double.IsNaN(x) || double.IsNaN(delta)) return double.NaN;

        if (x == 0)
        {
            return Math.Exp(SpecialFunctions.LogGamma((v + 1) / 2) - SpecialFunctions.LogGamma(v / 2)
                - (0.5 * Math.Log(v * Math.PI)) - (delta * delta / 2));
        }

        return v / x * (NoncentralTCdf(x * Math.Sqrt((v + 2) / v), v + 2, delta) - NoncentralTCdf(x, v, delta));
    }

    /// <summary>The noncentral t quantile.</summary>
    public static double NoncentralTInv(double p, double v, double delta)
    {
        if (!(v > 0) || double.IsNaN(p) || p < 0 || p > 1 || double.IsNaN(delta)) return double.NaN;
        if (p == 0) return double.NegativeInfinity;
        if (p == 1) return double.PositiveInfinity;
        return SearchQuantile(x => NoncentralTCdf(x, v, delta), p,
            double.NegativeInfinity, double.PositiveInfinity, delta);
    }

    /// <summary>
    /// The mean and variance of a noncentral t. The mean needs more than one degree of freedom and the
    /// variance more than two.
    /// </summary>
    public static (double Mean, double Variance) NoncentralTStat(double v, double delta)
    {
        if (!(v > 0) || double.IsNaN(delta)) return (double.NaN, double.NaN);

        double mean = v > 1
            ? delta * Math.Sqrt(v / 2) * Math.Exp(SpecialFunctions.LogGamma((v - 1) / 2) - SpecialFunctions.LogGamma(v / 2))
            : double.NaN;
        double variance = v > 2
            ? (v * (1 + (delta * delta)) / (v - 2)) - (mean * mean)
            : double.NaN;
        return (mean, variance);
    }

    // --- Sampling ---------------------------------------------------------------------------------

    /// <summary>One standard normal draw, by the polar form of the Box–Muller transform.</summary>
    public static double StandardNormal(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        double u, v, s;
        do
        {
            u = (2 * random.NextDouble()) - 1;
            v = (2 * random.NextDouble()) - 1;
            s = (u * u) + (v * v);
        }
        while (s >= 1 || s == 0);

        return u * Math.Sqrt(-2 * Math.Log(s) / s);
    }

    /// <summary>
    /// One gamma draw of shape <paramref name="a"/> and scale <paramref name="b"/>, by Marsaglia and
    /// Tsang's squeeze. A shape below one is handled by drawing at shape a+1 and scaling down, which
    /// is what keeps the acceptance rate high where the density is unbounded at the origin.
    /// </summary>
    public static double SampleGamma(Random random, double a, double b)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!(a > 0) || !(b > 0)) return double.NaN;

        if (a < 1)
        {
            return SampleGamma(random, a + 1, b) * Math.Pow(NonZeroUniform(random), 1 / a);
        }

        double d = a - (1.0 / 3.0);
        double c = 1 / Math.Sqrt(9 * d);

        while (true)
        {
            double x = StandardNormal(random);
            double t = 1 + (c * x);
            if (t <= 0) continue;

            double vv = t * t * t;
            double u = NonZeroUniform(random);
            if (Math.Log(u) < (0.5 * x * x) + (d * (1 - vv + Math.Log(vv))))
            {
                return d * vv * b;
            }
        }
    }

    /// <summary>One beta draw, as the share one of two gamma draws takes of their total.</summary>
    public static double SampleBeta(Random random, double a, double b)
    {
        double x = SampleGamma(random, a, 1);
        double y = SampleGamma(random, b, 1);
        return x + y == 0 ? 0 : x / (x + y);
    }

    /// <summary>
    /// One noncentral chi-square draw, built the way the distribution is defined: a Poisson(δ/2) draw
    /// says how many extra pairs of degrees of freedom the central chi-square gets. The shorter
    /// construction — a central chi-square on v − 1 degrees of freedom plus a displaced squared normal
    /// — is only available above one degree of freedom, and this one is available everywhere.
    /// </summary>
    public static double NoncentralChi2Sample(Random random, double v, double delta)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!(v > 0) || !(delta >= 0)) return double.NaN;

        int extra = SamplePoisson(random, delta / 2);
        return SampleGamma(random, (v / 2) + extra, 2);
    }

    /// <summary>
    /// One Poisson draw. Knuth's product below the mean where the exponential does not underflow, and
    /// the counting inversion above it, which is enough for the mixture sizes these families reach.
    /// </summary>
    public static int SamplePoisson(Random random, double mean)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!(mean > 0)) return 0;

        if (mean < 500)
        {
            double limit = Math.Exp(-mean);
            double product = 1;
            int count = 0;
            do
            {
                product *= random.NextDouble();
                count++;
            }
            while (product > limit);

            return count - 1;
        }

        // Far out, the normal approximation is indistinguishable at the precision anything downstream
        // reads, and the product form would need thousands of draws per sample.
        return Math.Max(0, (int)Math.Round(mean + (Math.Sqrt(mean) * StandardNormal(random))));
    }

    /// <summary>A uniform draw that is never exactly zero, for the logarithms that follow it.</summary>
    public static double NonZeroUniform(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        double u;
        do
        {
            u = random.NextDouble();
        }
        while (u <= 0);

        return u;
    }

    // --- Shared machinery -------------------------------------------------------------------------

    /// <summary>
    /// Sums <paramref name="term"/> over a Poisson(δ/2) mixture that adds 2j to the degrees of
    /// freedom — the construction both noncentral chi-square functions share.
    /// </summary>
    private static double PoissonMixture(double v, double delta, Func<double, int, double> term)
    {
        if (!(v > 0) || !(delta >= 0) || double.IsNaN(delta)) return double.NaN;
        return Mixture(delta, j => term(v + (2 * j), j));
    }

    /// <summary>The same mixture, but adding the degrees of freedom to the numerator of an F.</summary>
    private static double PoissonMixtureOn(double v1, double delta, Func<double, int, double> term) =>
        Mixture(delta, j => term(v1 + (2 * j), j));

    /// <summary>
    /// The Poisson(δ/2) weighted sum itself. It stops once the weight still to come cannot move the
    /// answer, and never before passing the mixture's mode — the weights rise before they fall, so a
    /// small early term is not a reason to stop.
    /// </summary>
    private static double Mixture(double delta, Func<int, double> term)
    {
        double half = delta / 2;
        if (half == 0)
        {
            return term(0);
        }

        double sum = 0;
        double weightSoFar = 0;
        double logHalf = Math.Log(half);

        for (int j = 0; j < MixtureTerms; j++)
        {
            double weight = Math.Exp(-half + (j * logHalf) - SpecialFunctions.LogGamma(j + 1));
            sum += weight * term(j);
            weightSoFar += weight;

            if (j > half && 1 - weightSoFar < MixtureTolerance)
            {
                break;
            }
        }

        return sum;
    }

    /// <summary>
    /// The quantile of a distribution function that has no inverse in closed form: bracket the answer
    /// by stepping out from <paramref name="guess"/>, then bisect. Bisection rather than Newton
    /// because these distribution functions are series whose derivative is another series, and a flat
    /// tail sends Newton a very long way from the answer.
    /// </summary>
    private static double SearchQuantile(
        Func<double, double> cdf, double p, double lower, double upper, double guess)
    {
        double low = double.IsNegativeInfinity(lower) ? guess - 1 : lower;
        double high = double.IsPositiveInfinity(upper) ? Math.Max(guess, 1) + 1 : upper;

        int stepped = 0;
        while (cdf(low) > p && stepped++ < 200)
        {
            if (!double.IsNegativeInfinity(lower)) break;
            double span = Math.Max(1, high - low);
            high = low;
            low -= 2 * span;
        }

        stepped = 0;
        while (cdf(high) < p && stepped++ < 200)
        {
            if (!double.IsPositiveInfinity(upper)) break;
            double span = Math.Max(1, high - low);
            low = high;
            high += 2 * span;
        }

        for (int i = 0; i < 200; i++)
        {
            double middle = (low + high) / 2;
            if (middle <= low || middle >= high)
            {
                break;
            }

            if (cdf(middle) < p)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }
}
