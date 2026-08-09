using JGraph.Statistics.Distributions;
using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Sampling;

/// <summary>
/// Drawing from a distribution described by what it should look like rather than by name: four
/// quantiles (the Johnson system) or four moments (the Pearson system).
/// </summary>
/// <remarks>
/// Both systems work the same way. A description arrives, arithmetic on it decides which member of
/// the system can meet it, and that member's own parameters fall out of the same arithmetic. Neither
/// is a fit in the likelihood sense — nothing is optimized and no data is involved — which is why the
/// answer is exact rather than approximate whenever the description is attainable at all.
/// </remarks>
public static class MomentMatching
{
    /// <summary>Which member of the Johnson system a description picked out.</summary>
    public enum JohnsonKind
    {
        /// <summary>Unbounded: a hyperbolic sine of a normal.</summary>
        SU,

        /// <summary>Bounded on both sides: a logistic transform of a normal.</summary>
        SB,

        /// <summary>Bounded below: a lognormal.</summary>
        SL,

        /// <summary>The normal itself, which the system reaches in the limit.</summary>
        SN,
    }

    /// <summary>A member of the Johnson system, ready to be evaluated at a normal deviate.</summary>
    /// <param name="Kind">Which member.</param>
    /// <param name="Gamma">The shape offset.</param>
    /// <param name="Eta">The shape scale.</param>
    /// <param name="Xi">The location.</param>
    /// <param name="Lambda">The scale.</param>
    public readonly record struct JohnsonCurve(
        JohnsonKind Kind, double Gamma, double Eta, double Xi, double Lambda)
    {
        /// <summary>The value this curve gives a standard normal deviate.</summary>
        public double At(double z)
        {
            double t = (z - Gamma) / Eta;
            return Kind switch
            {
                JohnsonKind.SU => Xi + (Lambda * Math.Sinh(t)),
                JohnsonKind.SB => Xi + (Lambda / (1 + Math.Exp(-t))),
                JohnsonKind.SL => Xi + (Lambda * Math.Exp(t)),
                _ => Xi + (Lambda * t),
            };
        }
    }

    /// <summary>
    /// The member of the Johnson system that passes through four quantiles, by the Slifker and Shapiro
    /// construction: the ratio of the two outer gaps to the square of the inner one decides the member,
    /// and the gaps themselves then give its four parameters.
    /// </summary>
    /// <param name="z">Four increasing standard normal deviates, symmetric about zero.</param>
    /// <param name="x">The values the distribution should take at them.</param>
    public static JohnsonCurve Johnson(double[] z, double[] x)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(x);
        if (z.Length != 4 || x.Length != 4)
        {
            throw new ArgumentException("The Johnson system is fitted through exactly four quantiles.", nameof(x));
        }

        for (int i = 1; i < 4; i++)
        {
            if (!(x[i] > x[i - 1]) || !(z[i] > z[i - 1]))
            {
                throw new ArgumentException("The four quantiles, and the deviates they sit at, must increase.", nameof(x));
            }
        }

        if (Math.Abs(z[0] + z[3]) > 1e-9 || Math.Abs(z[1] + z[2]) > 1e-9
            || Math.Abs(z[3] - (3 * z[2])) > 1e-9)
        {
            throw new ArgumentException(
                "The Johnson construction needs deviates at -3z, -z, z and 3z for one z.", nameof(z));
        }

        double spread = z[2];
        double m = x[3] - x[2];
        double n = x[1] - x[0];
        double p = x[2] - x[1];
        double ratio = m * n / (p * p);
        double middle = (x[2] + x[1]) / 2;

        if (Math.Abs(ratio - 1) < 1e-6)
        {
            // The boundary between bounded and unbounded is the lognormal, which is the one member with
            // a scale that cannot be separated from its location.
            double mp = m / p;
            double eta = 2 * spread / Math.Log(mp);
            double lambda = Math.Sign(mp - 1);
            double gamma = eta * Math.Log(Math.Abs(mp - 1) / (p * Math.Sqrt(mp)));
            double xi = middle - (lambda * p / 2 * ((mp + 1) / (mp - 1)));
            return new JohnsonCurve(JohnsonKind.SL, gamma, eta, xi, lambda);
        }

        if (ratio > 1)
        {
            double mp = m / p;
            double np = n / p;
            double eta = 2 * spread / Acosh((mp + np) / 2);
            double gamma = eta * Asinh((np - mp) / (2 * Math.Sqrt(ratio - 1)));
            double lambda = 2 * p * Math.Sqrt(ratio - 1) / ((mp + np - 2) * Math.Sqrt(mp + np + 2));
            double xi = middle + (p * (np - mp) / (2 * (mp + np - 2)));
            return new JohnsonCurve(JohnsonKind.SU, gamma, eta, xi, lambda);
        }

        double pm = p / m;
        double pn = p / n;
        double product = (1 + pm) * (1 + pn);
        double etaB = spread / Acosh(Math.Sqrt(product) / 2);
        double denominator = (pm * pn) - 1;
        double gammaB = etaB * Asinh((pn - pm) * Math.Sqrt(product - 4) / (2 * denominator));
        double lambdaB = p * Math.Sqrt(((product - 2) * (product - 2)) - 4) / denominator;
        double xiB = middle - (lambdaB / 2) + (p * (pn - pm) / (2 * denominator));
        return new JohnsonCurve(JohnsonKind.SB, gammaB, etaB, xiB, lambdaB);
    }

    /// <summary>The Pearson type a set of moments picks out, numbered as MathWorks numbers them.</summary>
    /// <param name="Type">The type number, 0 to 7.</param>
    /// <param name="Coefficients">The type's own parameters, in the order that type publishes them.</param>
    /// <param name="Quantile">The value at a given probability.</param>
    public readonly record struct PearsonCurve(
        int Type, double[] Coefficients, Func<double, double> Quantile);

    /// <summary>
    /// The member of the Pearson system with the given mean, standard deviation, skewness and kurtosis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which type it is follows from the two shape moments alone: squared skewness against kurtosis
    /// places a point on the Pearson diagram, and the type is which region it landed in. The location
    /// and scale are then applied afterwards, because no type's shape depends on them.
    /// </para>
    /// <para>
    /// Six of the seven types are a named distribution wearing a shift and a scale, so their quantile
    /// is that distribution's. The seventh — type four — is not any named distribution, and its
    /// quantile is computed by integrating its density and inverting the result, which is the same
    /// thing MathWorks documents doing.
    /// </para>
    /// </remarks>
    public static PearsonCurve Pearson(double mean, double deviation, double skewness, double kurtosis)
    {
        if (!(deviation > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(deviation), "The standard deviation must be above zero.");
        }

        double beta1 = skewness * skewness;
        double beta2 = kurtosis;
        if (beta2 <= beta1 + 1)
        {
            throw new ArgumentException(
                "No distribution has that pair of shape moments: the kurtosis must exceed the squared skewness by more than one.",
                nameof(kurtosis));
        }

        // The three coefficients of the Pearson differential equation, written in the standardized
        // variable so that the location and the scale can be put back afterwards:
        //     d(log p)/dx = -(c1 + x) / (c0 + c1 x + c2 x^2)
        // Every type below is that equation solved in the region the two shape moments landed in.
        double denominator = (10 * beta2) - (12 * beta1) - 18;
        if (Math.Abs(denominator) < 1e-12)
        {
            throw new ArgumentException(
                "That pair of shape moments sits on the boundary of the Pearson system, where no member of it exists.",
                nameof(kurtosis));
        }

        double c0 = ((4 * beta2) - (3 * beta1)) / denominator;
        double c1 = skewness * (beta2 + 3) / denominator;
        double c2 = ((2 * beta2) - (3 * beta1) - 6) / denominator;

        double[] moments = [mean, deviation, skewness, kurtosis];
        PearsonCurve Curve(int type, Func<double, double> standard) =>
            new(type, moments, p => mean + (deviation * standard(p)));

        if (Math.Abs(c1) < 1e-12 && Math.Abs(c2) < 1e-12)
        {
            return Curve(0, static p => ContinuousDistributions.NormalInv(p, 0, 1));
        }

        if (Math.Abs(c2) < 1e-12)
        {
            // Type 3. The quadratic degenerates to a line, and what is left is a gamma in
            // u = c0 + c1 x, with the sign of c1 deciding which way the tail points.
            double shape = c0 / (c1 * c1);
            double scale = c1 * c1;
            double sign = Math.Sign(c1);
            return Curve(3, p =>
                (ContinuousDistributions.GammaInv(sign > 0 ? p : 1 - p, shape, scale) - c0) / c1);
        }

        double discriminant = (c1 * c1) - (4 * c0 * c2);

        if (Math.Abs(discriminant) < 1e-12 * Math.Max(1, c1 * c1))
        {
            // Type 5. A repeated root, about which the solution is an inverse gamma.
            double root = -c1 / (2 * c2);
            double shape = (1 / c2) - 1;
            double scale = -(c1 + root) / c2;
            double sign = Math.Sign(scale);
            double size = Math.Abs(scale);
            return Curve(5, p =>
            {
                double gamma = ContinuousDistributions.GammaInv(sign > 0 ? 1 - p : p, shape, 1);
                return root + (sign * size / Math.Max(gamma, 1e-300));
            });
        }

        if (discriminant < 0)
        {
            double lambda = -c1 / (2 * c2);
            double width = Math.Sqrt(-discriminant) / (2 * Math.Abs(c2));
            double shape = 1 / (2 * c2);

            if (Math.Abs(c1) < 1e-12)
            {
                // Type 7. The symmetric case of type 4, which is a scaled t and needs no quadrature:
                // (1 + t^2)^-m is Student's density with 2m - 1 degrees of freedom.
                double freedom = (2 * shape) - 1;
                double factor = width / Math.Sqrt(freedom);
                return Curve(7, p => factor * ContinuousDistributions.TInv(p, freedom));
            }

            // Type 4. Not any named distribution — the arctangent in the exponent is what makes it its
            // own thing — so its quantile is its density integrated and then read backwards.
            double slant = (c1 + lambda) / (c2 * width);
            double Density(double x)
            {
                double t = (x - lambda) / width;
                return Math.Pow(1 + (t * t), -shape) * Math.Exp(-slant * Math.Atan(t));
            }

            return Curve(4, NumericQuantile(Density, lambda, width));
        }

        double smaller = (-c1 - Math.Sqrt(discriminant)) / (2 * c2);
        double larger = (-c1 + Math.Sqrt(discriminant)) / (2 * c2);
        if (smaller > larger)
        {
            (smaller, larger) = (larger, smaller);
        }

        // The two exponents the partial fractions leave behind, one per root.
        double gap = larger - smaller;
        double lowerPower = -(c1 + smaller) / (c2 * gap) * -1;
        double upperPower = (c1 + larger) / (c2 * gap) * -1;

        if (smaller < 0 && larger > 0)
        {
            // Types 1 and 2. The roots straddle the origin, so the support is the interval between them
            // and the solution is a beta stretched onto it; type 2 is the symmetric case of type 1.
            double alpha = lowerPower + 1;
            double beta = upperPower + 1;
            int type = Math.Abs(skewness) < 1e-12 ? 2 : 1;
            return Curve(type, p =>
                smaller + (gap * ContinuousDistributions.BetaInv(p, alpha, beta)));
        }

        // Type 6. Both roots on the same side of the origin, so the support runs from one of them out
        // to infinity, and the solution is a beta prime — a beta's odds rather than a beta. Which root
        // it runs from is decided by the exponents and not by the signs of the roots: the density has
        // to be integrable where it meets the root, and only the root whose exponent is above minus one
        // is one the density can start at.
        bool upward = upperPower > -1;
        double first = upward ? upperPower + 1 : lowerPower + 1;
        double second = -(lowerPower + upperPower + 1);
        if (!(first > 0 && second > 0))
        {
            throw new ArgumentException(
                "That pair of shape moments names a Pearson type six with no finite mean.", nameof(kurtosis));
        }

        double origin = upward ? larger : smaller;
        double direction = upward ? 1 : -1;
        return Curve(6, p =>
        {
            double s = ContinuousDistributions.BetaInv(upward ? p : 1 - p, first, second);
            return origin + (direction * gap * s / Math.Max(1 - s, 1e-300));
        });
    }

    /// <summary>
    /// The quantile of a density known only as a formula: integrate it over a grid wide enough to hold
    /// essentially all of it, normalize, and read the grid backwards.
    /// </summary>
    private static Func<double, double> NumericQuantile(Func<double, double> density, double centre, double scale)
    {
        const int Steps = 4001;
        double from = centre - (60 * scale);
        double to = centre + (60 * scale);
        double step = (to - from) / (Steps - 1);

        var grid = new double[Steps];
        var mass = new double[Steps];
        double total = 0;
        for (int i = 0; i < Steps; i++)
        {
            grid[i] = from + (i * step);
            if (i > 0)
            {
                total += GaussLegendre.Integrate(density, grid[i - 1], grid[i], 8);
            }

            mass[i] = total;
        }

        for (int i = 0; i < Steps; i++)
        {
            mass[i] /= total;
        }

        return p =>
        {
            if (p <= 0)
            {
                return grid[0];
            }

            if (p >= 1)
            {
                return grid[^1];
            }

            int low = 0;
            int high = Steps - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (mass[mid] <= p)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            double span = mass[high] - mass[low];
            double fraction = span > 0 ? (p - mass[low]) / span : 0;
            return grid[low] + (fraction * step);
        };
    }

    private static double Acosh(double value) => Math.Acosh(Math.Max(value, 1));

    private static double Asinh(double value) => Math.Asinh(value);
}
