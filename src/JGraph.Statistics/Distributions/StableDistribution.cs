namespace JGraph.Statistics.Distributions;

/// <summary>
/// The stable family, in the same four parameters MathWorks documents: a stability index
/// <c>alpha</c>, a skewness <c>beta</c>, a scale <c>gam</c> and a location <c>delta</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only three stable distributions have a density that can be written down — the normal, the Cauchy
/// and the Lévy — and the first two are answered in closed form here, both because it is exact and
/// because they are what the rest of the file is checked against. Everything else is computed from
/// Nolan's integral representation, which turns the divergent inversion integral into a
/// <em>finite</em> one over an angle, with an integrand that rises from zero to a single peak and
/// falls back. That shape is the whole reason the calculation is tractable: the quadrature is placed
/// around the peak, which is located first rather than hoped for.
/// </para>
/// <para>
/// The parameterization is the one whose characteristic function MathWorks prints — Nolan's
/// zero-parameterization, the one that stays continuous in the parameters as <c>alpha</c> passes
/// through one. Which parameterization is in force decides how the scale enters at <c>alpha = 1</c>,
/// and getting that wrong displaces every answer there by a constant while leaving the whole rest of
/// the family looking right — so it is checked by a test that walks the stability index across one.
/// </para>
/// </remarks>
public static class StableDistribution
{
    /// <summary>How close to one <c>alpha</c> may be before it is treated as one exactly.</summary>
    private const double UnitTolerance = 1e-10;

    /// <summary>Gauss-Legendre nodes per panel, and panels per segment of the cut interval.</summary>
    private const int Nodes = 12;

    private const int Panels = 6;

    /// <summary>The density at <paramref name="x"/>.</summary>
    public static double Pdf(double x, double alpha, double beta, double scale, double location)
    {
        Check(alpha, beta, scale);
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (alpha == 2)
        {
            return ContinuousDistributions.NormalPdf(x, location, scale * Math.Sqrt(2));
        }

        if (IsUnit(alpha) && beta == 0)
        {
            double z = (x - location) / scale;
            return 1 / (Math.PI * (1 + (z * z)) * scale);
        }

        return StandardPdf(Standardize(x, scale, location), alpha, beta) / scale;
    }

    /// <summary>The probability of not exceeding <paramref name="x"/>.</summary>
    public static double Cdf(double x, double alpha, double beta, double scale, double location)
    {
        Check(alpha, beta, scale);
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (alpha == 2)
        {
            return ContinuousDistributions.NormalCdf(x, location, scale * Math.Sqrt(2));
        }

        if (IsUnit(alpha) && beta == 0)
        {
            return 0.5 + (Math.Atan((x - location) / scale) / Math.PI);
        }

        return StandardCdf(Standardize(x, scale, location), alpha, beta);
    }

    /// <summary>The quantile, found by inverting the distribution function.</summary>
    public static double Inv(double p, double alpha, double beta, double scale, double location)
    {
        Check(alpha, beta, scale);
        if (alpha == 2)
        {
            return ContinuousDistributions.NormalInv(p, location, scale * Math.Sqrt(2));
        }

        if (IsUnit(alpha) && beta == 0)
        {
            return p is < 0 or > 1 ? double.NaN
                : location + (scale * Math.Tan(Math.PI * (p - 0.5)));
        }

        return ObjectFamilies.NumericInverse(
            x => Cdf(x, alpha, beta, scale, location), p, scale, location);
    }

    /// <summary>
    /// The mean and the variance, where they exist. A stable distribution has finite variance only
    /// when it is normal, and a finite mean only above <c>alpha = 1</c>; below that the integral
    /// defining the mean does not converge, and saying so is the answer.
    /// </summary>
    public static (double Mean, double Variance) Moments(double alpha, double beta, double scale, double location)
    {
        Check(alpha, beta, scale);
        double mean = alpha > 1
            ? location - (beta * scale * Math.Tan(Math.PI * alpha / 2))
            : double.NaN;
        double variance = alpha == 2 ? 2 * scale * scale : double.PositiveInfinity;
        return (mean, variance);
    }

    /// <summary>
    /// One draw, by the Chambers-Mallows-Stuck transformation of a uniform angle and an exponential
    /// radius. It is exact — no rejection, no inversion of a quadrature — which is what makes a
    /// sample of a stable distribution cheap where its density is not.
    /// </summary>
    public static double Sample(Random random, double alpha, double beta, double scale, double location)
    {
        ArgumentNullException.ThrowIfNull(random);
        Check(alpha, beta, scale);

        double u = Math.PI * (random.NextDouble() - 0.5);
        double w = -Math.Log(ContinuousDistributions.NonZeroUniform(random));

        if (IsUnit(alpha))
        {
            double half = (Math.PI / 2) + (beta * u);
            // The half-pi inside the logarithm is what puts the draw in the same parameterization the
            // density above uses. Every printing of this transformation that leaves it out produces a
            // sample displaced by a constant — visible only by comparing against the distribution
            // function, which is what the test beside it does.
            double y = 2 / Math.PI * ((half * Math.Tan(u))
                - (beta * Math.Log(Math.PI / 2 * w * Math.Cos(u) / half)));
            return (scale * y) + location;
        }

        double tangent = Math.Tan(Math.PI * alpha / 2);
        double shift = Math.Atan(beta * tangent) / alpha;
        double spread = Math.Pow(1 + (beta * beta * tangent * tangent), 1 / (2 * alpha));
        double standard = spread * Math.Sin(alpha * (u + shift)) / Math.Pow(Math.Cos(u), 1 / alpha)
            * Math.Pow(Math.Cos(u - (alpha * (u + shift))) / w, (1 - alpha) / alpha);
        return (scale * standard) - (beta * scale * tangent) + location;
    }

    // --- Standardizing ------------------------------------------------------------------------------

    private static bool IsUnit(double alpha) => Math.Abs(alpha - 1) < UnitTolerance;

    /// <summary>
    /// The standard variable this <paramref name="x"/> corresponds to: the ordinary shift and divide,
    /// at every stability index including one.
    /// </summary>
    /// <remarks>
    /// Worth saying out loud, because the older parameterization needs a term in the logarithm of the
    /// scale here and the two are easy to confuse. This one does not: the characteristic function
    /// MathWorks prints is exactly what a plain shift and divide of the standard case produces, and
    /// the logarithm belongs to converting <em>between</em> the parameterizations rather than to
    /// scaling within this one. Adding it moves every answer at <c>alpha = 1</c> off the limit its
    /// neighbours converge to, which is what the continuity test measures.
    /// </remarks>
    private static double Standardize(double x, double scale, double location) =>
        (x - location) / scale;

    private static void Check(double alpha, double beta, double scale)
    {
        if (!(alpha > 0) || alpha > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alpha), alpha, "A stable distribution's alpha lies in (0, 2].");
        }

        if (!(beta >= -1) || beta > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beta), beta, "A stable distribution's beta lies in [-1, 1].");
        }

        if (!(scale > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "A stable distribution's scale must be positive.");
        }
    }

    // --- The standard density and distribution function ---------------------------------------------

    private static double StandardPdf(double x, double alpha, double beta)
    {
        if (IsUnit(alpha))
        {
            return beta < 0 ? UnitPdf(-x, -beta) : UnitPdf(x, beta);
        }

        double zeta = -beta * Math.Tan(Math.PI * alpha / 2);
        if (x < zeta)
        {
            return StandardPdf(-x, alpha, -beta);
        }

        double theta0 = Math.Atan(-zeta) / alpha;
        if (Math.Abs(x - zeta) < 1e-12)
        {
            // The one point where the integral degenerates has a closed form, which is just as well:
            // the shifted variable is zero there and the power it is raised to is negative.
            return Numerics.SpecialFunctions.Gamma(1 + (1 / alpha)) * Math.Cos(theta0)
                / (Math.PI * Math.Pow(1 + (zeta * zeta), 1 / (2 * alpha)));
        }

        double shifted = Math.Log(x - zeta);
        double logWeight = alpha / (alpha - 1) * shifted;
        double logOutside = Math.Log(alpha) + (shifted / (alpha - 1))
            - Math.Log(Math.PI) - Math.Log(Math.Abs(alpha - 1));

        return Integrate(
            theta => LogKernel(theta, alpha, theta0), logWeight, -theta0, Math.PI / 2, logOutside);
    }

    private static double StandardCdf(double x, double alpha, double beta)
    {
        if (IsUnit(alpha))
        {
            return beta < 0 ? 1 - UnitCdf(-x, -beta) : UnitCdf(x, beta);
        }

        double zeta = -beta * Math.Tan(Math.PI * alpha / 2);
        if (x < zeta)
        {
            return 1 - StandardCdf(-x, alpha, -beta);
        }

        double theta0 = Math.Atan(-zeta) / alpha;
        if (Math.Abs(x - zeta) < 1e-12)
        {
            return ((Math.PI / 2) - theta0) / Math.PI;
        }

        double logWeight = alpha / (alpha - 1) * Math.Log(x - zeta);
        double before = alpha < 1 ? ((Math.PI / 2) - theta0) / Math.PI : 1;
        double sign = alpha < 1 ? 1 : -1;

        double area = Integrate(
            theta => LogKernel(theta, alpha, theta0), logWeight, -theta0, Math.PI / 2, logOutside: null);
        return Math.Clamp(before + (sign / Math.PI * area), 0, 1);
    }

    /// <summary>
    /// The logarithm of Nolan's <c>V</c> — the change of variable that turns the divergent inversion
    /// integral into a finite one over an angle.
    /// </summary>
    /// <remarks>
    /// It is the logarithm and not the value because <c>V</c> itself has no useful range: as the
    /// stability index approaches one it runs from <c>e^-6000</c> to <c>e^6000</c> across the
    /// interval, and it is always multiplied by a weight that runs the opposite way by the same
    /// amount. In logarithms the two cancel exactly; as numbers they are a zero times an infinity,
    /// and what comes back is a NaN.
    /// </remarks>
    private static double LogKernel(double theta, double alpha, double theta0)
    {
        double cosine = Math.Cos(theta);
        double sine = Math.Sin(alpha * (theta0 + theta));
        double top = Math.Cos((alpha * theta0) + ((alpha - 1) * theta));
        if (cosine <= 0 || sine <= 0 || top <= 0)
        {
            return alpha < 1 ? double.NegativeInfinity : double.PositiveInfinity;
        }

        double exponent = 1 / (alpha - 1);
        return (exponent * Math.Log(Math.Cos(alpha * theta0)))
            + (alpha * exponent * (Math.Log(cosine) - Math.Log(sine)))
            + Math.Log(top) - Math.Log(cosine);
    }

    // --- alpha = 1 --------------------------------------------------------------------------------

    private static double UnitPdf(double x, double beta)
    {
        double logWeight = -Math.PI * x / (2 * beta);
        return Integrate(
            theta => UnitLogKernel(theta, beta), logWeight,
            -Math.PI / 2, Math.PI / 2, logWeight - Math.Log(2 * Math.Abs(beta)));
    }

    private static double UnitCdf(double x, double beta)
    {
        double logWeight = -Math.PI * x / (2 * beta);
        double area = Integrate(
            theta => UnitLogKernel(theta, beta), logWeight, -Math.PI / 2, Math.PI / 2, logOutside: null);
        return Math.Clamp(area / Math.PI, 0, 1);
    }

    private static double UnitLogKernel(double theta, double beta)
    {
        double cosine = Math.Cos(theta);
        double half = (Math.PI / 2) + (beta * theta);
        if (cosine <= 0 || half <= 0)
        {
            return double.NegativeInfinity;
        }

        return Math.Log(2 / Math.PI) + Math.Log(half) - Math.Log(cosine)
            + (half * Math.Tan(theta) / beta);
    }

    // --- Quadrature -------------------------------------------------------------------------------

    /// <summary>
    /// The values the exponent is cut at. Above six the integrand has already vanished and below
    /// twenty-five it has already settled, so the ladder only needs to be dense across the transition.
    /// </summary>
    private static readonly double[] Ladder = [6, 3, 1.5, 0.5, 0, -0.5, -1.5, -3, -6, -12, -25];

    /// <summary>
    /// Integrates one of the two stable integrands from <paramref name="from"/> to
    /// <paramref name="to"/>: the density's, when <paramref name="logOutside"/> is given, and the
    /// distribution function's, when it is not.
    /// </summary>
    /// <remarks>
    /// Everything interesting happens where the exponent passes through one, and that place can be a
    /// sliver a millionth of a radian wide — panels spread evenly across the interval would step
    /// straight over it and return a confident zero. So the interval is cut at the angles where the
    /// exponent takes a fixed ladder of values, which brackets the transition however narrow it is.
    /// Bisection finds the cuts, which is enough because <c>V</c> is monotone: that is the property the
    /// whole representation is built on.
    /// </remarks>
    private static double Integrate(
        Func<double, double> logKernel, double logWeight, double from, double to, double? logOutside)
    {
        double Exponent(double theta)
        {
            double log = logKernel(theta);
            return double.IsInfinity(log) ? log : logWeight + log;
        }

        double Integrand(double theta)
        {
            double t = Exponent(theta);
            if (double.IsPositiveInfinity(t) || t > 700)
            {
                return 0;
            }

            double decayed = double.IsNegativeInfinity(t) ? 0 : Math.Exp(t);
            if (logOutside is not double outside)
            {
                return Math.Exp(-decayed);
            }

            double log = logKernel(theta);
            if (double.IsInfinity(log))
            {
                return 0;
            }

            double power = outside + log - decayed;
            return power < -745 ? 0 : Math.Exp(power);
        }

        var cuts = new List<double> { from, to };
        double atStart = Exponent(from);
        double atEnd = Exponent(to);
        bool rising = atEnd > atStart;

        foreach (double target in Ladder)
        {
            if (target <= Math.Min(atStart, atEnd) || target >= Math.Max(atStart, atEnd))
            {
                continue;
            }

            double low = from;
            double high = to;
            for (int i = 0; i < 200; i++)
            {
                double middle = (low + high) / 2;
                if (middle <= low || middle >= high)
                {
                    break;
                }

                if (Exponent(middle) < target == rising)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            cuts.Add((low + high) / 2);
        }

        cuts.Sort();

        double total = 0;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            if (cuts[i + 1] > cuts[i])
            {
                total += Quadrature.GaussLegendre.Integrate(Integrand, cuts[i], cuts[i + 1], Nodes, Panels);
            }
        }

        return total;
    }
}

