using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Hypothesis;

/// <summary>
/// <c>fishertest</c>: whether the two rows of a two-by-two table of counts have the same proportion,
/// answered by counting rather than approximating.
/// </summary>
/// <remarks>
/// With both margins held fixed, the top-left count has a hypergeometric distribution under the null,
/// and the probability of every possible table can be written down. That is the whole test: no
/// approximation, no minimum expected count, and an answer that is exact for a table of three
/// observations. What is approximate is the confidence interval for the odds ratio, which is the usual
/// normal interval on the logarithm — recorded as such rather than presented as exact.
/// </remarks>
public static class ContingencyTests
{
    /// <summary>The outcome of Fisher's exact test.</summary>
    /// <param name="P">The tail probability.</param>
    /// <param name="OddsRatio">The sample odds ratio, <c>ad/bc</c>.</param>
    /// <param name="Lower">The lower confidence limit for it.</param>
    /// <param name="Upper">The upper confidence limit.</param>
    public readonly record struct ExactTable(double P, double OddsRatio, double Lower, double Upper);

    /// <summary>Fisher's exact test of a two-by-two table.</summary>
    /// <param name="a">The top-left count.</param>
    /// <param name="b">The top-right count.</param>
    /// <param name="c">The bottom-left count.</param>
    /// <param name="d">The bottom-right count.</param>
    /// <param name="alpha">The level the interval is reported at.</param>
    /// <param name="tail">Which departure from independence to look for.</param>
    public static ExactTable Fisher(int a, int b, int c, int d, double alpha, Tail tail)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
        {
            throw new ArgumentException("a contingency table holds counts, which are never negative.");
        }

        int rowOne = a + b;
        int rowTwo = c + d;
        int columnOne = a + c;
        int total = rowOne + rowTwo;
        if (total == 0)
        {
            throw new ArgumentException("a contingency table of nothing has nothing to test.");
        }

        int lowest = Math.Max(0, columnOne - rowTwo);
        int highest = Math.Min(rowOne, columnOne);

        double logTotal = TestSupport.LogChoose(total, columnOne);
        double Probability(int k) =>
            Math.Exp(TestSupport.LogChoose(rowOne, k) + TestSupport.LogChoose(rowTwo, columnOne - k) - logTotal);

        double observed = Probability(a);
        double atMost = 0;
        double atLeast = 0;
        double asExtreme = 0;
        for (int k = lowest; k <= highest; k++)
        {
            double probability = Probability(k);
            if (k <= a)
            {
                atMost += probability;
            }

            if (k >= a)
            {
                atLeast += probability;
            }

            // Two-sided means every table no more likely than the one seen, which is the definition
            // that does not assume the distribution is symmetric — it is not.
            if (probability <= observed * (1 + 1e-7))
            {
                asExtreme += probability;
            }
        }

        double p = tail switch
        {
            Tail.Right => atLeast,
            Tail.Left => atMost,
            _ => asExtreme,
        };

        double odds = (double)a * d / ((double)b * c);
        double half = ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1)
            * Math.Sqrt((1.0 / a) + (1.0 / b) + (1.0 / c) + (1.0 / d));
        double lower = Math.Exp(Math.Log(odds) - half);
        double upper = Math.Exp(Math.Log(odds) + half);

        return new ExactTable(Math.Clamp(p, 0, 1), odds, lower, upper);
    }
}

/// <summary>
/// <c>sampsizepwr</c>: the three-way relationship between an effect, a sample size and the chance of
/// noticing the effect. Any two of them determine the third, and this solves for whichever was left
/// out.
/// </summary>
/// <remarks>
/// Power is what the whole family shares: the probability that the test rejects when the alternative
/// is true. Each test has its own expression for it, and everything else here is a search — the sample
/// size and the effect are both found by bracketing and bisecting the power curve rather than by
/// inverting a formula, because for the discrete test there is no formula to invert.
/// </remarks>
public static class SampleSize
{
    /// <summary>Which test the power is being computed for.</summary>
    public enum TestKind
    {
        /// <summary>A one-sample z test of a mean, with the standard deviation known.</summary>
        Z,

        /// <summary>A one-sample t test of a mean.</summary>
        T,

        /// <summary>A two-sample t test of two means.</summary>
        TwoSampleT,

        /// <summary>A chi-square test of a variance.</summary>
        Variance,

        /// <summary>An exact binomial test of a proportion.</summary>
        Proportion,
    }

    /// <summary>
    /// The chance that the test rejects at level <paramref name="alpha"/> when the truth is
    /// <paramref name="alternative"/>.
    /// </summary>
    /// <param name="kind">Which test.</param>
    /// <param name="parameters">
    /// The null-hypothesis parameters: the mean and standard deviation for the two t tests and the z,
    /// the standard deviation for the variance test, the proportion for the binomial one.
    /// </param>
    /// <param name="alternative">The value the parameter really takes.</param>
    /// <param name="n">The sample size — of the first sample, for the two-sample test.</param>
    /// <param name="alpha">The level.</param>
    /// <param name="tail">Which alternative is being tested.</param>
    /// <param name="ratio">How many times larger the second sample is, for the two-sample test.</param>
    public static double Power(
        TestKind kind, double[] parameters, double alternative, double n, double alpha, Tail tail, double ratio)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!(n >= 2))
        {
            return double.NaN;
        }

        switch (kind)
        {
            case TestKind.Z:
            {
                (double mean, double sigma) = TwoParameters(parameters, "the z test");
                double shift = (alternative - mean) / sigma * Math.Sqrt(n);
                return NormalPower(shift, alpha, tail);
            }

            case TestKind.T:
            {
                (double mean, double sigma) = TwoParameters(parameters, "the t test");
                double noncentrality = (alternative - mean) / sigma * Math.Sqrt(n);
                return StudentPower(noncentrality, n - 1, alpha, tail);
            }

            case TestKind.TwoSampleT:
            {
                (double mean, double sigma) = TwoParameters(parameters, "the two-sample t test");
                if (!(ratio > 0))
                {
                    throw new ArgumentException("the ratio of the two sample sizes must be above zero.");
                }

                double second = Math.Round(n * ratio);
                if (second < 2)
                {
                    return double.NaN;
                }

                double noncentrality = (alternative - mean) / sigma / Math.Sqrt((1 / n) + (1 / second));
                return StudentPower(noncentrality, n + second - 2, alpha, tail);
            }

            case TestKind.Variance:
            {
                if (parameters.Length != 1 || !(parameters[0] > 0) || !(alternative > 0))
                {
                    throw new ArgumentException("the variance test takes one positive standard deviation.");
                }

                double scale = alternative * alternative / (parameters[0] * parameters[0]);
                double df = n - 1;
                return tail switch
                {
                    Tail.Right => 1 - ContinuousDistributions.Chi2Cdf(
                        ContinuousDistributions.Chi2Inv(1 - alpha, df) / scale, df),
                    Tail.Left => ContinuousDistributions.Chi2Cdf(
                        ContinuousDistributions.Chi2Inv(alpha, df) / scale, df),
                    _ => ContinuousDistributions.Chi2Cdf(
                             ContinuousDistributions.Chi2Inv(alpha / 2, df) / scale, df)
                         + 1 - ContinuousDistributions.Chi2Cdf(
                             ContinuousDistributions.Chi2Inv(1 - (alpha / 2), df) / scale, df),
                };
            }

            default:
            {
                if (parameters.Length != 1 || !(parameters[0] > 0) || !(parameters[0] < 1))
                {
                    throw new ArgumentException("the proportion test takes one proportion strictly between 0 and 1.");
                }

                return ProportionPower(parameters[0], alternative, (int)Math.Round(n), alpha, tail);
            }
        }
    }

    /// <summary>The smallest sample size that reaches <paramref name="wanted"/> power.</summary>
    public static double SampleFor(
        TestKind kind, double[] parameters, double alternative, double wanted, double alpha, Tail tail, double ratio)
    {
        if (!(wanted > 0) || !(wanted < 1))
        {
            throw new ArgumentException("the power asked for must lie strictly between 0 and 1.");
        }

        // Double the sample size until the power is reached, then walk back one at a time. The power
        // of the exact binomial test is not monotone in n — it steps as the critical count moves — so
        // a bisection could land on a size whose neighbour below also works.
        const int Largest = 1_000_000;
        int high = 2;
        while (high < Largest && !(Power(kind, parameters, alternative, high, alpha, tail, ratio) >= wanted))
        {
            high *= 2;
        }

        if (high >= Largest)
        {
            throw new ArgumentException(
                "no sample size below a million reaches that power; the effect asked about may be too small.");
        }

        int low = Math.Max(2, high / 2);
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Power(kind, parameters, alternative, middle, alpha, tail, ratio) >= wanted)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        while (low > 2 && Power(kind, parameters, alternative, low - 1, alpha, tail, ratio) >= wanted)
        {
            low--;
        }

        return low;
    }

    /// <summary>
    /// The alternative value that a sample of <paramref name="n"/> would notice with the given power.
    /// Where the alternative may lie on either side, the one above the null is reported.
    /// </summary>
    public static double AlternativeFor(
        TestKind kind, double[] parameters, double wanted, double n, double alpha, Tail tail, double ratio)
    {
        if (!(wanted > 0) || !(wanted < 1))
        {
            throw new ArgumentException("the power asked for must lie strictly between 0 and 1.");
        }

        double centre = parameters[0];
        bool upwards = tail != Tail.Left;
        double step = kind switch
        {
            TestKind.Proportion => (1 - centre) / 2,
            TestKind.Variance => centre,
            _ => parameters[1],
        };

        // Bracket first: step away from the null until the power passes what was asked for, keeping
        // inside the range the parameter is allowed to take.
        double bound = double.NaN;
        for (int i = 0; i < 60; i++)
        {
            double candidate = upwards ? centre + step : centre - step;
            candidate = kind switch
            {
                TestKind.Proportion => Math.Clamp(candidate, 1e-9, 1 - 1e-9),
                TestKind.Variance => Math.Max(candidate, 1e-12),
                _ => candidate,
            };

            if (Power(kind, parameters, candidate, n, alpha, tail, ratio) >= wanted)
            {
                bound = candidate;
                break;
            }

            step = kind == TestKind.Proportion ? step / 2 * 3 : step * 2;
        }

        if (double.IsNaN(bound))
        {
            throw new ArgumentException(
                "no alternative reaches that power at this sample size; a larger sample is needed.");
        }

        double near = centre;
        for (int i = 0; i < 200; i++)
        {
            double middle = (near + bound) / 2;
            if (Power(kind, parameters, middle, n, alpha, tail, ratio) >= wanted)
            {
                bound = middle;
            }
            else
            {
                near = middle;
            }
        }

        return bound;
    }

    private static (double Mean, double Sigma) TwoParameters(double[] parameters, string what)
    {
        if (parameters.Length != 2 || !(parameters[1] > 0))
        {
            throw new ArgumentException($"{what} takes a mean and a standard deviation above zero.");
        }

        return (parameters[0], parameters[1]);
    }

    private static double NormalPower(double shift, double alpha, Tail tail) => tail switch
    {
        Tail.Right => ContinuousDistributions.NormalCdf(shift - ContinuousDistributions.NormalInv(1 - alpha, 0, 1), 0, 1),
        Tail.Left => ContinuousDistributions.NormalCdf(-shift - ContinuousDistributions.NormalInv(1 - alpha, 0, 1), 0, 1),
        _ => ContinuousDistributions.NormalCdf(
                 shift - ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1), 0, 1)
             + ContinuousDistributions.NormalCdf(
                 -shift - ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1), 0, 1),
    };

    private static double StudentPower(double noncentrality, double df, double alpha, Tail tail)
    {
        switch (tail)
        {
            case Tail.Right:
            {
                double critical = ContinuousDistributions.TInv(1 - alpha, df);
                return 1 - ContinuousDistributions.NoncentralTCdf(critical, df, noncentrality);
            }

            case Tail.Left:
            {
                double critical = ContinuousDistributions.TInv(alpha, df);
                return ContinuousDistributions.NoncentralTCdf(critical, df, noncentrality);
            }

            default:
            {
                double critical = ContinuousDistributions.TInv(1 - (alpha / 2), df);
                return 1 - ContinuousDistributions.NoncentralTCdf(critical, df, noncentrality)
                    + ContinuousDistributions.NoncentralTCdf(-critical, df, noncentrality);
            }
        }
    }

    /// <summary>
    /// The power of the exact binomial test: find the counts the test would reject at, then add up
    /// how likely they are under the alternative.
    /// </summary>
    private static double ProportionPower(double p0, double p1, int n, double alpha, Tail tail)
    {
        if (n < 1)
        {
            return double.NaN;
        }

        double half = tail == Tail.Both ? alpha / 2 : alpha;
        double power = 0;

        if (tail != Tail.Left)
        {
            // The smallest count whose upper tail under the null is within the level. A count below it
            // would reject too often, which is what makes the exact test conservative.
            int critical = n + 1;
            for (int k = n; k >= 0; k--)
            {
                if (1 - DiscreteDistributions.BinomialCdf(k - 1, n, p0) > half)
                {
                    break;
                }

                critical = k;
            }

            if (critical <= n)
            {
                power += 1 - DiscreteDistributions.BinomialCdf(critical - 1, n, p1);
            }
        }

        if (tail != Tail.Right)
        {
            int critical = -1;
            for (int k = 0; k <= n; k++)
            {
                if (DiscreteDistributions.BinomialCdf(k, n, p0) > half)
                {
                    break;
                }

                critical = k;
            }

            if (critical >= 0)
            {
                power += DiscreteDistributions.BinomialCdf(critical, n, p1);
            }
        }

        return Math.Clamp(power, 0, 1);
    }
}
