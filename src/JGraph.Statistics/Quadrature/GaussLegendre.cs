namespace JGraph.Statistics.Quadrature;

/// <summary>
/// Gauss–Legendre nodes and weights, computed rather than tabulated.
/// </summary>
/// <remarks>
/// The multivariate distribution functions integrate smooth, bounded integrands over a box, which is
/// what this rule is best at, and they ask for several different node counts depending on how many
/// dimensions are left after the Cholesky transform. A table would have to hold every one of those
/// counts; Newton's method on the Legendre polynomial produces any of them to full double precision
/// in microseconds, and the rules are cached so a repeated call pays nothing.
/// </remarks>
public static class GaussLegendre
{
    private static readonly Dictionary<int, (double[] Nodes, double[] Weights)> Cache = [];

    /// <summary>
    /// The <paramref name="count"/>-point rule on the interval (−1, 1): the nodes, and the weights
    /// that integrate every polynomial of degree below 2·<paramref name="count"/> exactly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one point was asked for.</exception>
    public static (double[] Nodes, double[] Weights) Rule(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        lock (Cache)
        {
            if (Cache.TryGetValue(count, out (double[] Nodes, double[] Weights) cached))
            {
                return cached;
            }

            (double[] Nodes, double[] Weights) rule = Build(count);
            Cache[count] = rule;
            return rule;
        }
    }

    /// <summary>
    /// Integrates <paramref name="f"/> over [<paramref name="from"/>, <paramref name="to"/>] with the
    /// <paramref name="count"/>-point rule applied to each of <paramref name="panels"/> equal pieces.
    /// </summary>
    /// <remarks>
    /// Panels are what make a peaked integrand safe without abandoning the rule: an integrand that is
    /// flat over most of its range and sharp at one end is well resolved by three panels of the same
    /// order and badly resolved by one panel of three times the order.
    /// </remarks>
    public static double Integrate(Func<double, double> f, double from, double to, int count, int panels = 1)
    {
        (double[] nodes, double[] weights) = Rule(count);
        double width = (to - from) / panels;
        double total = 0;

        for (int p = 0; p < panels; p++)
        {
            double left = from + (p * width);
            double centre = left + (width / 2);
            double half = width / 2;
            double panel = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                panel += weights[i] * f(centre + (half * nodes[i]));
            }

            total += panel * half;
        }

        return total;
    }

    private static (double[] Nodes, double[] Weights) Build(int count)
    {
        var nodes = new double[count];
        var weights = new double[count];

        // The rule is symmetric, so only half the roots are found and the other half mirrored.
        for (int i = 0; i < (count + 1) / 2; i++)
        {
            // Tricomi's approximation to the i-th root, which Newton then polishes in three or four
            // steps for any order this code asks for.
            double x = Math.Cos(Math.PI * (i + 0.75) / (count + 0.5));
            double derivative = 0;

            for (int step = 0; step < 100; step++)
            {
                (double value, double slope) = LegendreAt(count, x);
                derivative = slope;
                double delta = value / slope;
                x -= delta;
                if (Math.Abs(delta) <= 1e-16 * (1 + Math.Abs(x)))
                {
                    break;
                }
            }

            nodes[i] = -x;
            nodes[count - 1 - i] = x;
            double weight = 2 / ((1 - (x * x)) * derivative * derivative);
            weights[i] = weight;
            weights[count - 1 - i] = weight;
        }

        return (nodes, weights);
    }

    /// <summary>The Legendre polynomial of the given degree and its derivative, by recurrence.</summary>
    private static (double Value, double Derivative) LegendreAt(int degree, double x)
    {
        double previous = 1;
        double current = x;
        for (int n = 2; n <= degree; n++)
        {
            double next = (((2 * n) - 1) * x * current - ((n - 1) * previous)) / n;
            previous = current;
            current = next;
        }

        if (degree == 0)
        {
            return (1, 0);
        }

        double derivative = degree * ((x * current) - previous) / ((x * x) - 1);
        return (current, derivative);
    }
}
