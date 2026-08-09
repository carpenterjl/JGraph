using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// Copulas: the dependence between variables, separated from what each of them is on its own.
/// </summary>
/// <remarks>
/// <para>
/// A copula is a distribution on the unit cube whose margins are uniform, so every argument here is a
/// probability rather than a value. Two shapes of family are supported and they are shaped
/// differently on purpose: the elliptical pair — Gaussian and t — are parameterized by a whole
/// correlation matrix and work in any number of dimensions, while the three Archimedean families are
/// parameterized by one number and, as MathWorks documents, are bivariate.
/// </para>
/// <para>
/// Everything that has a closed form is written in closed form. The two that do not — Spearman's rank
/// correlation of an Archimedean family, and the parameter that produces a wanted rank correlation —
/// are computed by integrating and by bisecting the result, rather than read off an interpolation
/// table, which is what lets one rule serve all five families instead of three tables serving three.
/// </para>
/// </remarks>
public static class Copulas
{
    /// <summary>The copula families.</summary>
    public enum Family
    {
        /// <summary>The dependence of a multivariate normal.</summary>
        Gaussian,

        /// <summary>The dependence of a multivariate t, which is the same with heavier joint tails.</summary>
        T,

        /// <summary>Archimedean, with dependence concentrated in the lower tail.</summary>
        Clayton,

        /// <summary>Archimedean and symmetric.</summary>
        Frank,

        /// <summary>Archimedean, with dependence concentrated in the upper tail.</summary>
        Gumbel,
    }

    /// <summary>Whether a family takes one number rather than a correlation matrix.</summary>
    public static bool IsArchimedean(Family family) =>
        family is Family.Clayton or Family.Frank or Family.Gumbel;

    /// <summary>The interval a family's parameter lives in.</summary>
    public static (double Lower, double Upper) ParameterRange(Family family) => family switch
    {
        Family.Clayton => (0, 100),
        Family.Gumbel => (1, 100),
        Family.Frank => (-35, 35),
        _ => (-1, 1),
    };

    // --- The elliptical pair ------------------------------------------------------------------------

    /// <summary>
    /// The Gaussian or t copula's distribution function at a point of the unit cube: the elliptical
    /// distribution's own probability, at the point its margins map this one to.
    /// </summary>
    public static double EllipticalCdf(double[] u, double[,] correlation, double? df)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(correlation);

        int n = u.Length;
        var lower = new double[n];
        var upper = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (u[i] <= 0)
            {
                return 0;
            }

            lower[i] = double.NegativeInfinity;
            upper[i] = double.IsNaN(u[i]) ? double.NaN
                : u[i] >= 1 ? double.PositiveInfinity
                : df is { } freedom
                    ? ContinuousDistributions.TInv(u[i], freedom)
                    : ContinuousDistributions.NormalInv(u[i], 0, 1);
        }

        return df is { } nu
            ? Multivariate.TCdf(lower, upper, correlation, nu).Probability
            : Multivariate.NormalCdf(lower, upper, correlation).Probability;
    }

    /// <summary>
    /// The Gaussian or t copula's density: the elliptical density at the mapped point, divided by what
    /// the margins would have contributed on their own.
    /// </summary>
    public static double EllipticalPdf(double[] u, double[,] correlation, double? df)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(correlation);

        int n = u.Length;
        var x = new double[n];
        double marginal = 0;
        for (int i = 0; i < n; i++)
        {
            if (!(u[i] > 0 && u[i] < 1))
            {
                return double.NaN;
            }

            if (df is { } freedom)
            {
                x[i] = ContinuousDistributions.TInv(u[i], freedom);
                marginal += Math.Log(ContinuousDistributions.TPdf(x[i], freedom));
            }
            else
            {
                x[i] = ContinuousDistributions.NormalInv(u[i], 0, 1);
                marginal += Math.Log(ContinuousDistributions.NormalPdf(x[i], 0, 1));
            }
        }

        double joint = df is { } nu
            ? Multivariate.TPdf(x, correlation, nu)
            : Multivariate.NormalPdf(x, new double[n], correlation);

        return joint / Math.Exp(marginal);
    }

    /// <summary>One draw from a Gaussian or t copula, given a factor of its correlation matrix.</summary>
    public static double[] EllipticalSample(Random random, double[,] factor, double? df)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(factor);

        int n = factor.GetLength(0);
        double[] draw = df is { } freedom
            ? Multivariate.TSample(random, factor, freedom)
            : Multivariate.NormalSample(random, new double[n], factor);

        var u = new double[n];
        for (int i = 0; i < n; i++)
        {
            u[i] = df is { } nu
                ? ContinuousDistributions.TCdf(draw[i], nu)
                : ContinuousDistributions.NormalCdf(draw[i], 0, 1);
        }

        return u;
    }

    // --- The Archimedean three ----------------------------------------------------------------------

    /// <summary>The distribution function of a bivariate Archimedean copula.</summary>
    public static double ArchimedeanCdf(Family family, double u, double v, double alpha)
    {
        if (u <= 0 || v <= 0)
        {
            return 0;
        }

        u = Math.Min(u, 1);
        v = Math.Min(v, 1);

        switch (family)
        {
            case Family.Clayton:
                if (alpha == 0)
                {
                    return u * v;
                }

                return Math.Pow(Math.Max(Math.Pow(u, -alpha) + Math.Pow(v, -alpha) - 1, 0), -1 / alpha);

            case Family.Frank:
                if (alpha == 0)
                {
                    return u * v;
                }

                double top = Expm1(-alpha * u) * Expm1(-alpha * v);
                return -double.LogP1(top / Expm1(-alpha)) / alpha;

            case Family.Gumbel:
                if (alpha == 1)
                {
                    return u * v;
                }

                double a = Math.Pow(-Math.Log(u), alpha);
                double b = Math.Pow(-Math.Log(v), alpha);
                return Math.Exp(-Math.Pow(a + b, 1 / alpha));

            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Not an Archimedean family.");
        }
    }

    /// <summary>The density of a bivariate Archimedean copula.</summary>
    public static double ArchimedeanPdf(Family family, double u, double v, double alpha)
    {
        if (!(u > 0 && u < 1 && v > 0 && v < 1))
        {
            return double.NaN;
        }

        switch (family)
        {
            case Family.Clayton:
            {
                if (alpha == 0)
                {
                    return 1;
                }

                double inner = Math.Pow(u, -alpha) + Math.Pow(v, -alpha) - 1;
                if (inner <= 0)
                {
                    return 0;
                }

                return (1 + alpha) * Math.Pow(u * v, -alpha - 1) * Math.Pow(inner, (-1 / alpha) - 2);
            }

            case Family.Frank:
            {
                if (alpha == 0)
                {
                    return 1;
                }

                // The numerator carries 1 - e^-a rather than e^-a - 1: the two differ by a sign, and
                // the sign is invisible in the denominator because that one is squared.
                double denominator = Expm1(-alpha) + (Expm1(-alpha * u) * Expm1(-alpha * v));
                return -alpha * Expm1(-alpha) * Math.Exp(-alpha * (u + v)) / (denominator * denominator);
            }

            case Family.Gumbel:
            {
                if (alpha == 1)
                {
                    return 1;
                }

                double x = -Math.Log(u);
                double y = -Math.Log(v);
                double sum = Math.Pow(x, alpha) + Math.Pow(y, alpha);
                double root = Math.Pow(sum, 1 / alpha);
                return Math.Exp(-root) / (u * v)
                    * Math.Pow(sum, (2 / alpha) - 2)
                    * Math.Pow(x * y, alpha - 1)
                    * (1 + ((alpha - 1) / root));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Not an Archimedean family.");
        }
    }

    /// <summary>
    /// One draw from a bivariate Archimedean copula, by conditional inversion: draw the first margin
    /// uniformly, then draw the second from its distribution given the first.
    /// </summary>
    public static (double U, double V) ArchimedeanSample(Random random, Family family, double alpha)
    {
        ArgumentNullException.ThrowIfNull(random);
        double u = NonZero(random);
        double w = NonZero(random);

        switch (family)
        {
            case Family.Clayton when alpha != 0:
            {
                double power = Math.Pow(w * Math.Pow(u, alpha + 1), -alpha / (alpha + 1));
                return (u, Math.Pow(power + 1 - Math.Pow(u, -alpha), -1 / alpha));
            }

            case Family.Frank when alpha != 0:
            {
                // Solving dC/du = w for v: the generator's difference at v is w(e^-a - 1) over
                // e^-au - w(e^-au - 1), and v is read back off it. Writing that denominator with its
                // signs the other way round makes the logarithm's argument negative, which is a
                // not-a-number rather than a draw.
                double e = Math.Exp(-alpha * u);
                double v = -double.LogP1(w * Expm1(-alpha) / (e - (w * (e - 1)))) / alpha;
                return (u, Math.Clamp(v, 0, 1));
            }

            case Family.Gumbel when alpha != 1:
            {
                // No closed-form conditional quantile, but the conditional distribution function rises
                // from zero to one in the second margin, so bisecting it is exact to the tolerance and
                // needs nothing the density above does not already provide.
                double low = 1e-12;
                double high = 1 - 1e-12;
                for (int i = 0; i < 80; i++)
                {
                    double mid = (low + high) / 2;
                    if (ConditionalGumbel(u, mid, alpha) < w)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                return (u, (low + high) / 2);
            }

            default:
                return (u, w);
        }
    }

    /// <summary>The Gumbel copula's distribution function of the second margin given the first.</summary>
    private static double ConditionalGumbel(double u, double v, double alpha)
    {
        double x = -Math.Log(u);
        double y = -Math.Log(v);
        double sum = Math.Pow(x, alpha) + Math.Pow(y, alpha);
        double root = Math.Pow(sum, 1 / alpha);
        return Math.Exp(-root) * Math.Pow(x, alpha - 1) * Math.Pow(sum, (1 / alpha) - 1) / u;
    }

    // --- Rank correlation, both directions ----------------------------------------------------------

    /// <summary>Kendall's rank correlation of a copula with the given parameter.</summary>
    public static double KendallTau(Family family, double parameter) => family switch
    {
        Family.Gaussian or Family.T => 2 / Math.PI * Math.Asin(Math.Clamp(parameter, -1, 1)),
        Family.Clayton => parameter / (parameter + 2),
        Family.Gumbel => 1 - (1 / parameter),
        Family.Frank => parameter == 0 ? 0 : 1 - (4 / parameter * (1 - Debye(parameter, 1))),
        _ => double.NaN,
    };

    /// <summary>
    /// Spearman's rank correlation of a copula with the given parameter. The elliptical pair have a
    /// closed form; the Archimedean three are integrated, because twelve times the volume under the
    /// copula, less three, is the definition and needs no table.
    /// </summary>
    public static double SpearmanRho(Family family, double parameter)
    {
        if (family is Family.Gaussian or Family.T)
        {
            return 6 / Math.PI * Math.Asin(Math.Clamp(parameter, -1, 1) / 2);
        }

        const int Nodes = 24;
        const int Panels = 24;
        double volume = GaussLegendre.Integrate(
            u => GaussLegendre.Integrate(v => ArchimedeanCdf(family, u, v, parameter), 0, 1, Nodes, Panels),
            0,
            1,
            Nodes,
            Panels);

        return Math.Clamp((12 * volume) - 3, -1, 1);
    }

    /// <summary>
    /// The parameter that gives a family the wanted rank correlation — the inverse of the two functions
    /// above, in closed form where one exists and by bisection where one does not.
    /// </summary>
    public static double ParameterFor(Family family, double target, bool spearman)
    {
        if (family is Family.Gaussian or Family.T)
        {
            return spearman
                ? 2 * Math.Sin(Math.PI * Math.Clamp(target, -1, 1) / 6)
                : Math.Sin(Math.PI * Math.Clamp(target, -1, 1) / 2);
        }

        if (!spearman)
        {
            switch (family)
            {
                case Family.Clayton:
                    return 2 * target / (1 - target);
                case Family.Gumbel:
                    return 1 / (1 - target);
            }
        }

        (double low, double high) = ParameterRange(family);
        double Measure(double parameter) => spearman
            ? SpearmanRho(family, parameter)
            : KendallTau(family, parameter);

        // Every one of these correlations rises with the parameter, so the interval halves cleanly.
        for (int i = 0; i < 80; i++)
        {
            double mid = (low + high) / 2;
            if (Measure(mid) < target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// The parameter of an Archimedean family that makes a sample most likely, found by scanning its
    /// whole range and then refining around the best point found.
    /// </summary>
    /// <remarks>
    /// The scan is not caution for its own sake. A likelihood that has gone to zero cannot be told
    /// apart from another that has gone to zero, so a search that only ever compares two candidates
    /// can be handed two equally impossible ones and throw away the half of the range holding the
    /// answer. Looking everywhere first costs a few dozen evaluations and cannot make that mistake.
    /// </remarks>
    public static double FitArchimedean(Family family, double[] u, double[] v)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(v);

        (double low, double high) = ParameterRange(family);
        double Objective(double parameter)
        {
            double total = 0;
            for (int i = 0; i < u.Length; i++)
            {
                // A density that has overflowed is as useless as one that has underflowed, and it is
                // the more dangerous of the two: an infinite log-likelihood beats every finite one, so
                // an unguarded search would answer with whatever parameter first blew up.
                double logged = Math.Log(ArchimedeanPdf(family, u[i], v[i], parameter));
                total += double.IsFinite(logged) ? logged : -1e6;
            }

            return -total;
        }

        return Minimize(Objective, low, high);
    }

    /// <summary>
    /// The least of a function over an interval: a scan of the whole of it, then a golden-section
    /// search between the neighbours of the best point the scan found.
    /// </summary>
    private static double Minimize(Func<double, double> objective, double low, double high)
    {
        const int Steps = 60;
        double step = (high - low) / Steps;
        double best = low;
        double bestValue = double.PositiveInfinity;
        for (int i = 0; i <= Steps; i++)
        {
            double at = low + (i * step);
            double value = objective(at);
            if (value < bestValue)
            {
                bestValue = value;
                best = at;
            }
        }

        double left = Math.Max(best - step, low);
        double right = Math.Min(best + step, high);
        double phi = (Math.Sqrt(5) - 1) / 2;
        double a = right - (phi * (right - left));
        double b = left + (phi * (right - left));
        double fa = objective(a);
        double fb = objective(b);
        for (int i = 0; i < 100 && right - left > 1e-9 * Math.Max(1, Math.Abs(right)); i++)
        {
            if (fa < fb)
            {
                right = b;
                b = a;
                fb = fa;
                a = right - (phi * (right - left));
                fa = objective(a);
            }
            else
            {
                left = a;
                a = b;
                fa = fb;
                b = left + (phi * (right - left));
                fb = objective(b);
            }
        }

        return (left + right) / 2;
    }

    /// <summary>
    /// The correlation matrix of a Gaussian copula fitted to a sample: the ordinary correlation of the
    /// normal scores, which is what maximizing the Gaussian copula's likelihood comes to.
    /// </summary>
    public static double[,] FitElliptical(double[,] u, double? df)
    {
        ArgumentNullException.ThrowIfNull(u);
        int rows = u.GetLength(0);
        int columns = u.GetLength(1);
        var scores = new double[rows, columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                double p = Math.Clamp(u[i, j], 1e-12, 1 - 1e-12);
                scores[i, j] = df is { } freedom
                    ? ContinuousDistributions.TInv(p, freedom)
                    : ContinuousDistributions.NormalInv(p, 0, 1);
            }
        }

        var correlation = new double[columns, columns];
        for (int j = 0; j < columns; j++)
        {
            correlation[j, j] = 1;
            for (int k = j + 1; k < columns; k++)
            {
                double sum = 0;
                double left = 0;
                double right = 0;
                for (int i = 0; i < rows; i++)
                {
                    sum += scores[i, j] * scores[i, k];
                    left += scores[i, j] * scores[i, j];
                    right += scores[i, k] * scores[i, k];
                }

                double r = left > 0 && right > 0 ? sum / Math.Sqrt(left * right) : 0;
                correlation[j, k] = r;
                correlation[k, j] = r;
            }
        }

        return correlation;
    }

    /// <summary>
    /// The degrees of freedom of a t copula, by profiling the likelihood: for each candidate the
    /// correlation is re-estimated, because the two are not independent of each other.
    /// </summary>
    public static double FitDegreesOfFreedom(double[,] u)
    {
        ArgumentNullException.ThrowIfNull(u);
        int rows = u.GetLength(0);
        int columns = u.GetLength(1);

        double Objective(double df)
        {
            double[,] correlation = FitElliptical(u, df);
            double total = 0;
            var point = new double[columns];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    point[j] = u[i, j];
                }

                double logged = Math.Log(EllipticalPdf(point, correlation, df));
                total += double.IsFinite(logged) ? logged : -1e6;
            }

            return -total;
        }

        return Math.Exp(Minimize(logged => Objective(Math.Exp(logged)), Math.Log(1.1), Math.Log(200)));
    }

    // --- Small pieces ------------------------------------------------------------------------------

    /// <summary>
    /// The Debye function of the given order, which is what Frank's rank correlation is written in
    /// terms of and has no elementary form.
    /// </summary>
    public static double Debye(double x, int order)
    {
        if (x == 0)
        {
            return 1;
        }

        if (x < 0)
        {
            // The negative branch by the reflection the definition gives, which keeps one integral.
            return Debye(-x, order) + (order * -x / (order + 1.0));
        }

        double integral = GaussLegendre.Integrate(
            t => t == 0 ? Math.Pow(t, order - 1) : Math.Pow(t, order) / double.ExpM1(t), 0, x, 24, 24);
        return order * integral / Math.Pow(x, order);
    }

    private static double Expm1(double value) => double.ExpM1(value);

    private static double NonZero(Random random)
    {
        double u = random.NextDouble();
        return u <= 0 ? 1e-12 : u;
    }
}
