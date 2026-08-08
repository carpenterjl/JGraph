using JGraph.Numerics;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// The families that arrive with the distribution objects: the twelve MathWorks documents a
/// <c>prob.*Distribution</c> class for but no <c>*pdf</c>/<c>*cdf</c> function pair, plus the three
/// whose parameter is a whole vector rather than a fixed handful of numbers.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them is a <see cref="DistributionFamily"/>, which is the point. Waves C and D built
/// the density, the distribution function, the quantile, the moments, the draw, the fitter, the
/// likelihood and the confidence interval to work from that record alone; a family added here
/// inherits all of it without a line of new machinery. What <c>makedist('Nakagami', …)</c> gains over
/// <c>makedist('Normal', …)</c> is a record, not a code path.
/// </para>
/// <para>
/// Three of the objects cannot be a fixed record, because their parameter is a vector whose length is
/// decided by the caller — a multinomial's probabilities, a kernel fit's data, a piecewise-linear
/// distribution's breakpoints. Those are built per instance by <see cref="Multinomial"/>,
/// <see cref="Kernel"/> and <see cref="PiecewiseLinear"/>, which close over the vector and hand back a
/// record shaped like every other. Everything downstream stays unaware that the family was minted a
/// moment ago rather than looked up.
/// </para>
/// </remarks>
public static class ObjectFamilies
{
    private static readonly DistributionFamily[] Families = Build();

    private static readonly Dictionary<string, DistributionFamily> ByAlias = BuildIndex();

    /// <summary>Every family added for the distribution objects.</summary>
    public static IReadOnlyList<DistributionFamily> All => Families;

    /// <summary>Finds one of these families by any documented spelling of its name.</summary>
    public static DistributionFamily? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByAlias.TryGetValue(ContinuousFamilies.Normalize(name), out DistributionFamily? family)
            ? family
            : null;
    }

    // --- The three whose parameter is a vector ----------------------------------------------------

    /// <summary>
    /// A multinomial over the categories <c>1 … k</c>, with one probability each. The parameter
    /// vector <em>is</em> the probabilities, which is why this family is built rather than looked up.
    /// </summary>
    public static DistributionFamily Multinomial(int categories)
    {
        if (categories < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(categories), "A multinomial needs at least one category.");
        }

        var names = new string[categories];
        var positive = new bool[categories];
        for (int i = 0; i < categories; i++)
        {
            names[i] = $"p{i + 1}";
            positive[i] = true;
        }

        static double Mass(double x, double[] p)
        {
            int k = (int)Math.Round(x);
            return k == x && k >= 1 && k <= p.Length ? p[k - 1] : 0;
        }

        static double Cumulative(double x, double[] p)
        {
            double total = 0;
            for (int i = 0; i < p.Length && i + 1 <= x; i++)
            {
                total += p[i];
            }

            return Math.Min(1, total);
        }

        static double Quantile(double q, double[] p)
        {
            if (q is < 0 or > 1)
            {
                return double.NaN;
            }

            double total = 0;
            for (int i = 0; i < p.Length; i++)
            {
                total += p[i];
                if (q <= total + 1e-12)
                {
                    return i + 1;
                }
            }

            return p.Length;
        }

        static (double, double) Moments(double[] p)
        {
            double mean = 0;
            double second = 0;
            for (int i = 0; i < p.Length; i++)
            {
                mean += (i + 1) * p[i];
                second += (i + 1) * (double)(i + 1) * p[i];
            }

            return (mean, second - (mean * mean));
        }

        return new DistributionFamily(
            "Multinomial", "multinomial", ["multinomial"], names,
            Mass, Cumulative, Quantile, Moments,
            (r, p) => Quantile(r.NextDouble(), p), positive, Discrete: true);
    }

    /// <summary>
    /// A kernel density fit: the sum of one scaled kernel per observation. The parameter vector holds
    /// the bandwidth followed by the data, so that the family record still describes itself
    /// completely — a kernel distribution copied, truncated or asked for its quantile carries its
    /// sample with it.
    /// </summary>
    /// <param name="observations">How many data points the parameter vector carries after the width.</param>
    /// <param name="kernel">Which kernel, by MathWorks' name.</param>
    public static DistributionFamily Kernel(int observations, string kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        if (observations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(observations), "A kernel fit needs at least one observation.");
        }

        KernelShape shape = ShapeOf(kernel);

        var names = new string[observations + 1];
        var positive = new bool[observations + 1];
        names[0] = "width";
        positive[0] = true;
        for (int i = 0; i < observations; i++)
        {
            names[i + 1] = $"x{i + 1}";
        }

        double Density(double x, double[] p)
        {
            double width = p[0];
            double total = 0;
            for (int i = 1; i < p.Length; i++)
            {
                total += KernelDensity((x - p[i]) / width, shape);
            }

            return total / (width * (p.Length - 1));
        }

        double Cumulative(double x, double[] p)
        {
            double width = p[0];
            double total = 0;
            for (int i = 1; i < p.Length; i++)
            {
                total += KernelCumulative((x - p[i]) / width, shape);
            }

            return total / (p.Length - 1);
        }

        double Quantile(double q, double[] p) =>
            NumericInverse(x => Cumulative(x, p), q, Spread(p));

        (double, double) Moments(double[] p)
        {
            double width = p[0];
            double mean = 0;
            for (int i = 1; i < p.Length; i++)
            {
                mean += p[i];
            }

            mean /= p.Length - 1;

            // The kernel widens the sample: a smoothed distribution's variance is the sample's own
            // plus the kernel's, which is what makes std(pd) larger than std(data) and not equal to it.
            double spread = 0;
            for (int i = 1; i < p.Length; i++)
            {
                double d = p[i] - mean;
                spread += d * d;
            }

            spread /= p.Length - 1;
            return (mean, spread + (width * width * KernelVariance(shape)));
        }

        double Draw(Random random, double[] p)
        {
            int pick = random.Next(p.Length - 1) + 1;
            return p[pick] + (p[0] * KernelDraw(random, shape));
        }

        return new DistributionFamily(
            "Kernel", "kernel", ["kernel", kernel], names,
            Density, Cumulative, Quantile, Moments, Draw, positive);
    }

    /// <summary>
    /// A piecewise-linear distribution: a distribution function that rises linearly between the
    /// caller's breakpoints. The parameter vector is the breakpoints followed by their cumulative
    /// probabilities, in that order, so the record again carries everything it needs.
    /// </summary>
    /// <param name="breakpoints">How many breakpoints the first half of the parameter vector holds.</param>
    public static DistributionFamily PiecewiseLinear(int breakpoints)
    {
        if (breakpoints < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakpoints), "A piecewise-linear distribution needs at least two breakpoints.");
        }

        var names = new string[breakpoints * 2];
        var positive = new bool[breakpoints * 2];
        for (int i = 0; i < breakpoints; i++)
        {
            names[i] = $"x{i + 1}";
            names[breakpoints + i] = $"F{i + 1}";
        }

        int n = breakpoints;

        double Density(double v, double[] p)
        {
            for (int i = 0; i < n - 1; i++)
            {
                if (v >= p[i] && v <= p[i + 1])
                {
                    double run = p[i + 1] - p[i];
                    return run <= 0 ? 0 : (p[n + i + 1] - p[n + i]) / run;
                }
            }

            return 0;
        }

        double Cumulative(double v, double[] p)
        {
            if (v <= p[0])
            {
                return v < p[0] ? 0 : p[n];
            }

            if (v >= p[n - 1])
            {
                return 1;
            }

            for (int i = 0; i < n - 1; i++)
            {
                if (v <= p[i + 1])
                {
                    double run = p[i + 1] - p[i];
                    return run <= 0
                        ? p[n + i + 1]
                        : p[n + i] + ((v - p[i]) * (p[n + i + 1] - p[n + i]) / run);
                }
            }

            return 1;
        }

        double Quantile(double q, double[] p)
        {
            if (q is < 0 or > 1)
            {
                return double.NaN;
            }

            for (int i = 0; i < n - 1; i++)
            {
                if (q <= p[n + i + 1])
                {
                    double rise = p[n + i + 1] - p[n + i];
                    return rise <= 0 ? p[i] : p[i] + ((q - p[n + i]) * (p[i + 1] - p[i]) / rise);
                }
            }

            return p[n - 1];
        }

        (double, double) Moments(double[] p)
        {
            // Each segment is a trapezoid in density, whose moments are exact in closed form — so the
            // mean and the variance are read off the breakpoints rather than integrated.
            double mean = 0;
            double second = 0;
            for (int i = 0; i < n - 1; i++)
            {
                double weight = p[n + i + 1] - p[n + i];
                double a = p[i];
                double b = p[i + 1];
                if (weight <= 0)
                {
                    continue;
                }

                mean += weight * (a + b) / 2;
                second += weight * (((a * a) + (a * b) + (b * b)) / 3);
            }

            return (mean, second - (mean * mean));
        }

        double Draw(Random random, double[] p) => Quantile(random.NextDouble(), p);

        return new DistributionFamily(
            "PiecewiseLinear", "piecewiselinear", ["piecewiselinear"], names,
            Density, Cumulative, Quantile, Moments, Draw, positive);
    }

    // --- Shared numerics --------------------------------------------------------------------------

    /// <summary>
    /// A quantile found by bracketing and bisecting the distribution function, for the families whose
    /// inverse has no closed form. <paramref name="scale"/> sizes the first step outward, so the
    /// search brackets a distribution living near zero as readily as one living near a million.
    /// </summary>
    public static double NumericInverse(Func<double, double> cdf, double p, double scale, double centre = 0)
    {
        ArgumentNullException.ThrowIfNull(cdf);

        if (double.IsNaN(p) || p < 0 || p > 1)
        {
            return double.NaN;
        }

        if (p == 0 || p == 1)
        {
            double sign = p == 0 ? -1 : 1;
            double edge = centre;
            for (int i = 0; i < 200; i++)
            {
                double value = cdf(edge);
                if ((p == 0 && value <= 0) || (p == 1 && value >= 1))
                {
                    break;
                }

                edge += sign * scale * (1 << Math.Min(i, 30));
            }

            return edge;
        }

        double low = centre;
        double high = centre;
        double step = Math.Max(scale, 1e-12);
        for (int i = 0; i < 200 && cdf(low) > p; i++)
        {
            low -= step;
            step *= 2;
        }

        step = Math.Max(scale, 1e-12);
        for (int i = 0; i < 200 && cdf(high) < p; i++)
        {
            high += step;
            step *= 2;
        }

        for (int i = 0; i < 200; i++)
        {
            double middle = (low + high) / 2;
            if (middle == low || middle == high)
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

    /// <summary>A step size that suits the data a built family closed over.</summary>
    private static double Spread(double[] parameters)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        for (int i = 1; i < parameters.Length; i++)
        {
            low = Math.Min(low, parameters[i]);
            high = Math.Max(high, parameters[i]);
        }

        double range = high - low;
        return range > 0 ? range / 4 : Math.Max(Math.Abs(high), 1);
    }

    // --- Kernels ----------------------------------------------------------------------------------

    /// <summary>Which smoothing kernel a kernel distribution uses.</summary>
    private enum KernelShape
    {
        Normal,
        Box,
        Triangle,
        Epanechnikov,
    }

    private static KernelShape ShapeOf(string kernel) =>
        ContinuousFamilies.Normalize(kernel) switch
        {
            "normal" or "gaussian" => KernelShape.Normal,
            "box" or "uniform" or "rectangular" => KernelShape.Box,
            "triangle" or "triangular" => KernelShape.Triangle,
            "epanechnikov" => KernelShape.Epanechnikov,
            _ => throw new ArgumentException(
                $"'{kernel}' is not a smoothing kernel; the documented ones are normal, box, triangle and epanechnikov.",
                nameof(kernel)),
        };

    private static double KernelDensity(double u, KernelShape shape) =>
        shape switch
        {
            KernelShape.Normal => Math.Exp(-0.5 * u * u) / Math.Sqrt(2 * Math.PI),
            KernelShape.Box => Math.Abs(u) <= 1 ? 0.5 : 0,
            KernelShape.Triangle => Math.Abs(u) <= 1 ? 1 - Math.Abs(u) : 0,
            _ => Math.Abs(u) <= 1 ? 0.75 * (1 - (u * u)) : 0,
        };

    private static double KernelCumulative(double u, KernelShape shape)
    {
        switch (shape)
        {
            case KernelShape.Normal:
                return ContinuousDistributions.NormalCdf(u, 0, 1);

            case KernelShape.Box:
                return u <= -1 ? 0 : u >= 1 ? 1 : (u + 1) / 2;

            case KernelShape.Triangle:
                if (u <= -1) return 0;
                if (u >= 1) return 1;
                return u < 0
                    ? (1 + u) * (1 + u) / 2
                    : 1 - ((1 - u) * (1 - u) / 2);

            default:
                if (u <= -1) return 0;
                if (u >= 1) return 1;
                return 0.5 + (0.75 * (u - (u * u * u / 3)));
        }
    }

    /// <summary>The variance of the kernel itself, which is what a smoothed spread adds.</summary>
    private static double KernelVariance(KernelShape shape) =>
        shape switch
        {
            KernelShape.Normal => 1,
            KernelShape.Box => 1.0 / 3,
            KernelShape.Triangle => 1.0 / 6,
            _ => 1.0 / 5,
        };

    private static double KernelDraw(Random random, KernelShape shape) =>
        shape switch
        {
            KernelShape.Normal => ContinuousDistributions.StandardNormal(random),
            KernelShape.Box => (2 * random.NextDouble()) - 1,
            _ => NumericInverse(u => KernelCumulative(u, shape), random.NextDouble(), 0.5),
        };

    // --- The fixed families -----------------------------------------------------------------------

    private static Dictionary<string, DistributionFamily> BuildIndex()
    {
        var index = new Dictionary<string, DistributionFamily>(StringComparer.Ordinal);
        foreach (DistributionFamily family in Families)
        {
            foreach (string alias in family.Aliases)
            {
                index[ContinuousFamilies.Normalize(alias)] = family;
            }

            index[ContinuousFamilies.Normalize(family.Name)] = family;
            index[ContinuousFamilies.Normalize(family.Prefix)] = family;
        }

        return index;
    }

    private static DistributionFamily[] Build() =>
    [
        new("Birnbaum-Saunders", "bisa", ["birnbaumsaunders", "bisa"], ["beta", "gamma"],
            BirnbaumSaundersPdf, BirnbaumSaundersCdf, BirnbaumSaundersInv,
            p => (p[0] * (1 + (p[1] * p[1] / 2)),
                  p[0] * p[0] * p[1] * p[1] * (1 + (1.25 * p[1] * p[1]))),
            (r, p) => BirnbaumSaundersFrom(ContinuousDistributions.StandardNormal(r), p[0], p[1]),
            [true, true]),

        new("Burr", "burr", ["burr", "burrxii", "burrtype12"], ["alpha", "c", "k"],
            BurrPdf, BurrCdf, BurrInv, BurrStat,
            (r, p) => BurrInv(ContinuousDistributions.NonZeroUniform(r), p),
            [true, true, true]),

        new("Half Normal", "hn", ["halfnormal", "hn"], ["mu", "sigma"],
            (x, p) => x < p[0] ? 0 : 2 * ContinuousDistributions.NormalPdf(x, p[0], p[1]),
            (x, p) => x <= p[0] ? 0 : SpecialFunctions.Erf((x - p[0]) / (p[1] * Math.Sqrt(2))),
            (q, p) => q is < 0 or > 1 ? double.NaN
                : p[0] + (p[1] * Math.Sqrt(2) * SpecialFunctions.ErfInverse(q)),
            p => (p[0] + (p[1] * Math.Sqrt(2 / Math.PI)), p[1] * p[1] * (1 - (2 / Math.PI))),
            (r, p) => p[0] + Math.Abs(p[1] * ContinuousDistributions.StandardNormal(r)),
            [false, true]),

        new("Inverse Gaussian", "ig", ["inversegaussian", "ig", "wald"], ["mu", "lambda"],
            InverseGaussianPdf, InverseGaussianCdf,
            (q, p) => NumericInverse(x => InverseGaussianCdf(x, p), q, p[0] / 2, p[0]),
            p => (p[0], p[0] * p[0] * p[0] / p[1]),
            InverseGaussianSample, [true, true]),

        new("Logistic", "logi", ["logistic"], ["mu", "sigma"],
            (x, p) => LogisticDensity((x - p[0]) / p[1]) / p[1],
            (x, p) => 1 / (1 + Math.Exp(-(x - p[0]) / p[1])),
            (q, p) => q is < 0 or > 1 ? double.NaN : p[0] + (p[1] * Math.Log(q / (1 - q))),
            p => (p[0], p[1] * p[1] * Math.PI * Math.PI / 3),
            (r, p) => p[0] + (p[1] * Logit(r)),
            [false, true]),

        new("Log-Logistic", "logl", ["loglogistic", "logl"], ["mu", "sigma"],
            (x, p) => x <= 0 ? 0 : LogisticDensity((Math.Log(x) - p[0]) / p[1]) / (p[1] * x),
            (x, p) => x <= 0 ? 0 : 1 / (1 + Math.Exp(-(Math.Log(x) - p[0]) / p[1])),
            (q, p) => q is < 0 or > 1 ? double.NaN
                : q == 0 ? 0
                : q == 1 ? double.PositiveInfinity
                : Math.Exp(p[0] + (p[1] * Math.Log(q / (1 - q)))),
            LogLogisticStat,
            (r, p) => Math.Exp(p[0] + (p[1] * Logit(r))),
            [false, true]),

        new("Loguniform", "logu", ["loguniform", "logu"], ["Lower", "Upper"],
            (x, p) => x >= p[0] && x <= p[1] ? 1 / (x * Math.Log(p[1] / p[0])) : 0,
            (x, p) => x <= p[0] ? 0 : x >= p[1] ? 1 : Math.Log(x / p[0]) / Math.Log(p[1] / p[0]),
            (q, p) => q is < 0 or > 1 ? double.NaN : p[0] * Math.Pow(p[1] / p[0], q),
            LoguniformStat,
            (r, p) => p[0] * Math.Pow(p[1] / p[0], r.NextDouble()),
            [true, true]),

        new("Nakagami", "naka", ["nakagami", "naka"], ["mu", "omega"],
            NakagamiPdf,
            (x, p) => x <= 0 ? 0 : SpecialFunctions.GammaLower(p[0], p[0] * x * x / p[1]),
            (q, p) => q is < 0 or > 1 ? double.NaN
                : Math.Sqrt(SpecialFunctions.GammaInverse(p[0], q) * p[1] / p[0]),
            NakagamiStat,
            (r, p) => Math.Sqrt(ContinuousDistributions.SampleGamma(r, p[0], p[1] / p[0])),
            [true, true]),

        new("Rician", "rice", ["rician", "rice"], ["s", "sigma"],
            RicianPdf, RicianCdf,
            (q, p) => NumericInverse(x => RicianCdf(x, p), q, Math.Max(p[1], p[0] / 4)),
            RicianStat, RicianSample, [true, true]),

        new("t Location-Scale", "tls", ["tlocationscale", "tls"], ["mu", "sigma", "nu"],
            (x, p) => ContinuousDistributions.TPdf((x - p[0]) / p[1], p[2]) / p[1],
            (x, p) => ContinuousDistributions.TCdf((x - p[0]) / p[1], p[2]),
            (q, p) => p[0] + (p[1] * ContinuousDistributions.TInv(q, p[2])),
            p => (p[2] > 1 ? p[0] : double.NaN,
                  p[2] > 2 ? p[1] * p[1] * p[2] / (p[2] - 2) : double.PositiveInfinity),
            (r, p) => p[0] + (p[1] * ContinuousDistributions.StandardNormal(r)
                / Math.Sqrt(ContinuousDistributions.SampleGamma(r, p[2] / 2, 2) / p[2])),
            [false, true, true]),

        new("Triangular", "tri", ["triangular", "tri"], ["A", "B", "C"],
            TriangularPdf, TriangularCdf, TriangularInv,
            p => ((p[0] + p[1] + p[2]) / 3,
                  ((p[0] * p[0]) + (p[1] * p[1]) + (p[2] * p[2])
                   - (p[0] * p[1]) - (p[0] * p[2]) - (p[1] * p[2])) / 18),
            (r, p) => TriangularInv(r.NextDouble(), p),
            [false, false, false]),

        new("Stable", "stbl", ["stable", "stbl"], ["alpha", "beta", "gam", "delta"],
            (x, p) => StableDistribution.Pdf(x, p[0], p[1], p[2], p[3]),
            (x, p) => StableDistribution.Cdf(x, p[0], p[1], p[2], p[3]),
            (q, p) => StableDistribution.Inv(q, p[0], p[1], p[2], p[3]),
            p => StableDistribution.Moments(p[0], p[1], p[2], p[3]),
            (r, p) => StableDistribution.Sample(r, p[0], p[1], p[2], p[3]),
            [true, false, true, false]),
    ];

    // --- Birnbaum-Saunders ------------------------------------------------------------------------

    /// <summary>
    /// The standardized deviate a Birnbaum-Saunders observation corresponds to. Every one of the four
    /// functions below is this expression and a normal one, because the family <em>is</em> a normal
    /// seen through that change of variable.
    /// </summary>
    private static double BirnbaumSaundersDeviate(double x, double beta, double gamma) =>
        (Math.Sqrt(x / beta) - Math.Sqrt(beta / x)) / gamma;

    private static double BirnbaumSaundersPdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double z = BirnbaumSaundersDeviate(x, p[0], p[1]);
        double derivative = (Math.Sqrt(p[0] / x) + Math.Pow(p[0] / x, 1.5)) / (2 * p[1] * p[0]);
        return ContinuousDistributions.NormalPdf(z, 0, 1) * derivative;
    }

    private static double BirnbaumSaundersCdf(double x, double[] p) =>
        x <= 0 ? 0 : ContinuousDistributions.NormalCdf(BirnbaumSaundersDeviate(x, p[0], p[1]), 0, 1);

    private static double BirnbaumSaundersInv(double q, double[] p) =>
        q is < 0 or > 1 ? double.NaN
            : BirnbaumSaundersFrom(ContinuousDistributions.NormalInv(q, 0, 1), p[0], p[1]);

    /// <summary>The observation a standard normal deviate maps to — the change of variable, inverted.</summary>
    private static double BirnbaumSaundersFrom(double z, double beta, double gamma)
    {
        double half = gamma * z / 2;
        double root = half + Math.Sqrt((half * half) + 1);
        return beta * root * root;
    }

    // --- Burr type XII ----------------------------------------------------------------------------

    private static double BurrPdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double scaled = Math.Pow(x / p[0], p[1]);
        return p[2] * p[1] * scaled / (x * Math.Pow(1 + scaled, p[2] + 1));
    }

    private static double BurrCdf(double x, double[] p) =>
        x <= 0 ? 0 : 1 - Math.Pow(1 + Math.Pow(x / p[0], p[1]), -p[2]);

    private static double BurrInv(double q, double[] p) =>
        q is < 0 or > 1 ? double.NaN
            : q == 1 ? double.PositiveInfinity
            : p[0] * Math.Pow(Math.Pow(1 - q, -1 / p[2]) - 1, 1 / p[1]);

    /// <summary>
    /// A Burr's moments exist only while the tail is thin enough: the <em>r</em>th one needs
    /// <c>c·k &gt; r</c>. Below that the answer is infinite, and saying so beats returning the number a
    /// gamma function would produce out of its domain.
    /// </summary>
    private static (double Mean, double Variance) BurrStat(double[] p)
    {
        double mean = BurrMoment(p, 1);
        if (double.IsInfinity(mean))
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        double second = BurrMoment(p, 2);
        return (mean, double.IsInfinity(second) ? double.PositiveInfinity : second - (mean * mean));
    }

    private static double BurrMoment(double[] p, int order)
    {
        if (p[1] * p[2] <= order)
        {
            return double.PositiveInfinity;
        }

        double log = (order * Math.Log(p[0]))
            + SpecialFunctions.LogGamma(p[2] - (order / p[1]))
            + SpecialFunctions.LogGamma(1 + (order / p[1]))
            - SpecialFunctions.LogGamma(p[2]);
        return Math.Exp(log);
    }

    // --- Inverse Gaussian -------------------------------------------------------------------------

    private static double InverseGaussianPdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double deviation = x - p[0];
        return Math.Sqrt(p[1] / (2 * Math.PI * x * x * x))
            * Math.Exp(-p[1] * deviation * deviation / (2 * p[0] * p[0] * x));
    }

    /// <summary>
    /// The inverse Gaussian distribution function. Written as published it multiplies
    /// <c>exp(2λ/μ)</c> — which overflows for any well-separated parameters — by a normal tail that is
    /// correspondingly tiny. Folding the exponential into the scaled complementary error function
    /// cancels the two exactly, and what is left is a difference of squares that reduces to the same
    /// deviate the first term already uses.
    /// </summary>
    private static double InverseGaussianCdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double root = Math.Sqrt(p[1] / x);
        double below = root * ((x / p[0]) - 1);
        double above = root * ((x / p[0]) + 1);
        double tail = 0.5 * SpecialFunctions.ErfcScaled(above / Math.Sqrt(2)) * Math.Exp(-below * below / 2);
        return Math.Min(1, ContinuousDistributions.NormalCdf(below, 0, 1) + tail);
    }

    /// <summary>
    /// Michael, Schucany and Haas: a chi-square draw gives two candidate roots, and one coin flip
    /// weighted by the smaller root picks between them. It is exact rather than a rejection loop.
    /// </summary>
    private static double InverseGaussianSample(Random random, double[] p)
    {
        double mu = p[0];
        double lambda = p[1];
        double normal = ContinuousDistributions.StandardNormal(random);
        double squared = normal * normal;
        double first = mu + (mu * mu * squared / (2 * lambda))
            - (mu / (2 * lambda) * Math.Sqrt((4 * mu * lambda * squared) + (mu * mu * squared * squared)));
        return random.NextDouble() <= mu / (mu + first) ? first : mu * mu / first;
    }

    // --- Logistic and log-logistic ----------------------------------------------------------------

    /// <summary>
    /// The log-odds of one uniform draw, which is a standard logistic deviate. It has to be
    /// <em>one</em> draw: writing <c>log(u / (1 - u))</c> with a fresh <c>u</c> on each side asks the
    /// generator twice and produces a distribution that is neither logistic nor anything else.
    /// </summary>
    private static double Logit(Random random)
    {
        double u = ContinuousDistributions.NonZeroUniform(random);
        return Math.Log(u / (1 - u));
    }

    private static double LogisticDensity(double z)
    {
        double e = Math.Exp(-Math.Abs(z));
        return e / ((1 + e) * (1 + e));
    }

    /// <summary>
    /// A log-logistic's moments exist only while its log-scale is small enough: the mean needs
    /// <c>sigma &lt; 1</c> and the variance <c>sigma &lt; 1/2</c>.
    /// </summary>
    private static (double Mean, double Variance) LogLogisticStat(double[] p)
    {
        double sigma = p[1];
        if (sigma >= 1)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        double mean = Math.Exp(p[0]) * Math.PI * sigma / Math.Sin(Math.PI * sigma);
        if (sigma >= 0.5)
        {
            return (mean, double.PositiveInfinity);
        }

        double second = Math.Exp(2 * p[0]) * 2 * Math.PI * sigma / Math.Sin(2 * Math.PI * sigma);
        return (mean, second - (mean * mean));
    }

    private static (double Mean, double Variance) LoguniformStat(double[] p)
    {
        double span = Math.Log(p[1] / p[0]);
        double mean = (p[1] - p[0]) / span;
        double second = ((p[1] * p[1]) - (p[0] * p[0])) / (2 * span);
        return (mean, second - (mean * mean));
    }

    // --- Nakagami ---------------------------------------------------------------------------------

    private static double NakagamiPdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double mu = p[0];
        double omega = p[1];
        double log = Math.Log(2) + (mu * Math.Log(mu / omega)) - SpecialFunctions.LogGamma(mu)
            + (((2 * mu) - 1) * Math.Log(x)) - (mu * x * x / omega);
        return Math.Exp(log);
    }

    private static (double Mean, double Variance) NakagamiStat(double[] p)
    {
        double ratio = Math.Exp(SpecialFunctions.LogGamma(p[0] + 0.5) - SpecialFunctions.LogGamma(p[0]));
        double mean = ratio * Math.Sqrt(p[1] / p[0]);
        return (mean, p[1] - (mean * mean));
    }

    // --- Rician -----------------------------------------------------------------------------------

    private static double RicianPdf(double x, double[] p)
    {
        if (x <= 0)
        {
            return 0;
        }

        double variance = p[1] * p[1];
        double argument = x * p[0] / variance;

        // The exponential and the Bessel function each overflow long before their product does, so the
        // Bessel is taken in its scaled form and the e^argument it dropped is put back in the exponent.
        double scaled = BesselFunctions.I(0, argument, scaled: true);
        return x / variance * scaled
            * Math.Exp(argument - (((x * x) + (p[0] * p[0])) / (2 * variance)));
    }

    /// <summary>
    /// A Rician's square, in units of the noise variance, is a noncentral chi-square on two degrees of
    /// freedom — so its distribution function is one that wave C already wrote, and the Marcum Q this
    /// would otherwise need is not a second implementation of the same series.
    /// </summary>
    private static double RicianCdf(double x, double[] p) =>
        x <= 0 ? 0
            : ContinuousDistributions.NoncentralChi2Cdf(x * x / (p[1] * p[1]), 2, p[0] * p[0] / (p[1] * p[1]));

    private static (double Mean, double Variance) RicianStat(double[] p)
    {
        double variance = p[1] * p[1];
        double ratio = p[0] * p[0] / (2 * variance);
        double half = ratio / 2;

        // Laguerre one-half, written with the scaled Bessel functions so that a strong signal does not
        // overflow on its way to an answer of order s.
        double laguerre = ((1 + ratio) * BesselFunctions.I(0, half, scaled: true))
            + (ratio * BesselFunctions.I(1, half, scaled: true));
        double mean = p[1] * Math.Sqrt(Math.PI / 2) * laguerre;
        return (mean, (2 * variance) + (p[0] * p[0]) - (mean * mean));
    }

    /// <summary>
    /// Two independent normals about a mean of <c>s</c> in one coordinate, and the draw is their
    /// distance from the origin — which is the definition rather than an approximation to it.
    /// </summary>
    private static double RicianSample(Random random, double[] p)
    {
        double a = p[0] + (p[1] * ContinuousDistributions.StandardNormal(random));
        double b = p[1] * ContinuousDistributions.StandardNormal(random);
        return Math.Sqrt((a * a) + (b * b));
    }

    // --- Triangular -------------------------------------------------------------------------------

    private static double TriangularPdf(double x, double[] p)
    {
        double a = p[0];
        double b = p[1];
        double c = p[2];
        if (x < a || x > c || c <= a)
        {
            return 0;
        }

        return x < b
            ? 2 * (x - a) / ((c - a) * (b - a))
            : x > b ? 2 * (c - x) / ((c - a) * (c - b)) : 2 / (c - a);
    }

    private static double TriangularCdf(double x, double[] p)
    {
        double a = p[0];
        double b = p[1];
        double c = p[2];
        if (x <= a)
        {
            return 0;
        }

        if (x >= c)
        {
            return 1;
        }

        return x <= b
            ? (x - a) * (x - a) / ((c - a) * (b - a))
            : 1 - ((c - x) * (c - x) / ((c - a) * (c - b)));
    }

    private static double TriangularInv(double q, double[] p)
    {
        if (q is < 0 or > 1)
        {
            return double.NaN;
        }

        double a = p[0];
        double b = p[1];
        double c = p[2];
        double atPeak = (b - a) / (c - a);
        return q <= atPeak
            ? a + Math.Sqrt(q * (c - a) * (b - a))
            : c - Math.Sqrt((1 - q) * (c - a) * (c - b));
    }
}
