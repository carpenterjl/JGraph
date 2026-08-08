using JGraph.Numerics;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// M53 wave D: the discrete distribution kernels — binomial, Poisson, geometric, hypergeometric,
/// negative binomial and discrete uniform.
/// </summary>
/// <remarks>
/// <para>
/// Every distribution function here is written through a regularized incomplete beta or gamma
/// integral rather than as a sum of probabilities, because the sum loses the tail: a hundred terms
/// each rounded to the nearest double cannot tell <c>1e-18</c> from zero, and the tail is exactly
/// where a distribution function is asked for the answer that matters. The hypergeometric is the one
/// exception — it has no such closed form, so it is summed, from whichever end is shorter.
/// </para>
/// <para>
/// No quantile is written in closed form either. A discrete quantile is "the least value the variable
/// can take whose distribution function has reached the probability asked for", and that definition
/// searched directly is both exactly right and immune to a rounding error at a step, where a formula
/// inverted analytically and then rounded is off by one wherever the rounding falls the wrong way.
/// </para>
/// </remarks>
public static class DiscreteDistributions
{
    /// <summary>Where a search gives up and calls the answer infinite.</summary>
    private const double SearchCeiling = 1e15;

    // --- Binomial -----------------------------------------------------------------------------------

    /// <summary>The probability of exactly <paramref name="x"/> successes in n trials.</summary>
    public static double BinomialPdf(double x, double n, double p)
    {
        if (!IsCount(n) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x < 0 || x > n || x != Math.Floor(x))
        {
            return 0;
        }

        if (p == 0)
        {
            return x == 0 ? 1 : 0;
        }

        if (p == 1)
        {
            return x == n ? 1 : 0;
        }

        return Math.Exp(LogChoose(n, x) + (x * Math.Log(p)) + ((n - x) * double.LogP1(-p)));
    }

    /// <summary>The probability of at most <paramref name="x"/> successes.</summary>
    public static double BinomialCdf(double x, double n, double p)
    {
        if (!IsCount(n) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 0;
        }

        if (k >= n)
        {
            return 1;
        }

        // P(X ≤ k) = I_{1-p}(n-k, k+1): the identity that turns the sum into one integral.
        return SpecialFunctions.BetaRegularized(1 - p, n - k, k + 1);
    }

    /// <summary>The probability of more than <paramref name="x"/> successes.</summary>
    public static double BinomialUpper(double x, double n, double p)
    {
        if (!IsCount(n) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 1;
        }

        return k >= n ? 0 : SpecialFunctions.BetaRegularized(p, k + 1, n - k);
    }

    /// <summary>The least number of successes whose probability has reached <paramref name="p"/>.</summary>
    public static double BinomialInv(double p, double n, double probability) =>
        !IsCount(n) || !IsProbability(probability)
            ? double.NaN
            : Quantile(p, x => BinomialCdf(x, n, probability), 0, n);

    /// <summary>The mean and variance of a binomial.</summary>
    public static (double Mean, double Variance) BinomialStat(double n, double p) =>
        !IsCount(n) || !IsProbability(p)
            ? (double.NaN, double.NaN)
            : (n * p, n * p * (1 - p));

    /// <summary>One binomial draw.</summary>
    public static double BinomialSample(Random random, double n, double p)
    {
        if (!IsCount(n) || !IsProbability(p))
        {
            return double.NaN;
        }

        // Counting successes is exact and cheap while the trials are few; past that the count is read
        // off the distribution function instead, which costs a logarithmic number of integrals rather
        // than a linear number of coin flips.
        if (n <= 64)
        {
            int successes = 0;
            for (int i = 0; i < (int)n; i++)
            {
                if (random.NextDouble() < p)
                {
                    successes++;
                }
            }

            return successes;
        }

        return BinomialInv(random.NextDouble(), n, p);
    }

    // --- Poisson ------------------------------------------------------------------------------------

    /// <summary>The probability of exactly <paramref name="x"/> events.</summary>
    public static double PoissonPdf(double x, double lambda)
    {
        if (double.IsNaN(x) || double.IsNaN(lambda) || lambda < 0)
        {
            return double.NaN;
        }

        if (x < 0 || x != Math.Floor(x))
        {
            return 0;
        }

        if (lambda == 0)
        {
            return x == 0 ? 1 : 0;
        }

        return Math.Exp(-lambda + (x * Math.Log(lambda)) - SpecialFunctions.LogGamma(x + 1));
    }

    /// <summary>The probability of at most <paramref name="x"/> events.</summary>
    public static double PoissonCdf(double x, double lambda)
    {
        if (double.IsNaN(x) || double.IsNaN(lambda) || lambda < 0)
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 0;
        }

        return lambda == 0 ? 1 : SpecialFunctions.GammaUpper(k + 1, lambda);
    }

    /// <summary>The probability of more than <paramref name="x"/> events.</summary>
    public static double PoissonUpper(double x, double lambda)
    {
        if (double.IsNaN(x) || double.IsNaN(lambda) || lambda < 0)
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 1;
        }

        return lambda == 0 ? 0 : SpecialFunctions.GammaLower(k + 1, lambda);
    }

    /// <summary>The least count whose probability has reached <paramref name="p"/>.</summary>
    public static double PoissonInv(double p, double lambda) =>
        double.IsNaN(lambda) || lambda < 0
            ? double.NaN
            : Quantile(p, x => PoissonCdf(x, lambda), 0, double.PositiveInfinity);

    /// <summary>The mean and variance of a Poisson, which are the same number.</summary>
    public static (double Mean, double Variance) PoissonStat(double lambda) =>
        double.IsNaN(lambda) || lambda < 0 ? (double.NaN, double.NaN) : (lambda, lambda);

    // --- Geometric ----------------------------------------------------------------------------------

    /// <summary>
    /// The probability of exactly <paramref name="x"/> failures before the first success. MATLAB
    /// counts the failures, not the trials, so the support starts at zero rather than at one.
    /// </summary>
    public static double GeometricPdf(double x, double p)
    {
        if (!IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x < 0 || x != Math.Floor(x))
        {
            return 0;
        }

        return p == 0 ? 0 : p * Math.Pow(1 - p, x);
    }

    /// <summary>The probability of at most <paramref name="x"/> failures before the first success.</summary>
    public static double GeometricCdf(double x, double p)
    {
        if (!IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 0;
        }

        return p == 0 ? 0 : -double.ExpM1((k + 1) * double.LogP1(-p));
    }

    /// <summary>The probability of more than <paramref name="x"/> failures.</summary>
    public static double GeometricUpper(double x, double p)
    {
        if (!IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 1;
        }

        return p == 0 ? 1 : Math.Exp((k + 1) * double.LogP1(-p));
    }

    /// <summary>The least failure count whose probability has reached <paramref name="p"/>.</summary>
    public static double GeometricInv(double p, double probability) =>
        !IsProbability(probability)
            ? double.NaN
            : Quantile(p, x => GeometricCdf(x, probability), 0, double.PositiveInfinity);

    /// <summary>The mean and variance of a geometric.</summary>
    public static (double Mean, double Variance) GeometricStat(double p) =>
        !IsProbability(p) ? (double.NaN, double.NaN) : ((1 - p) / p, (1 - p) / (p * p));

    /// <summary>One geometric draw.</summary>
    public static double GeometricSample(Random random, double p)
    {
        if (!IsProbability(p))
        {
            return double.NaN;
        }

        if (p >= 1)
        {
            return 0;
        }

        return p == 0
            ? double.PositiveInfinity
            : Math.Floor(Math.Log(ContinuousDistributions.NonZeroUniform(random)) / double.LogP1(-p));
    }

    // --- Hypergeometric -----------------------------------------------------------------------------

    /// <summary>
    /// The probability of drawing exactly <paramref name="x"/> of the <paramref name="k"/> marked
    /// items in <paramref name="n"/> draws without replacement from a population of
    /// <paramref name="m"/>.
    /// </summary>
    public static double HypergeometricPdf(double x, double m, double k, double n)
    {
        if (!ValidHypergeometric(m, k, n) || double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x != Math.Floor(x) || x < Math.Max(0, n - (m - k)) || x > Math.Min(k, n))
        {
            return 0;
        }

        return Math.Exp(LogChoose(k, x) + LogChoose(m - k, n - x) - LogChoose(m, n));
    }

    /// <summary>The probability of drawing at most <paramref name="x"/> marked items.</summary>
    public static double HypergeometricCdf(double x, double m, double k, double n)
    {
        if (!ValidHypergeometric(m, k, n) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double lowest = Math.Max(0, n - (m - k));
        double highest = Math.Min(k, n);
        double top = Math.Floor(x);

        if (top < lowest)
        {
            return 0;
        }

        if (top >= highest)
        {
            return 1;
        }

        // Adding from whichever end is shorter keeps the running total away from the term that would
        // swamp the ones after it, and halves the work in the bargain.
        if (top - lowest <= highest - top)
        {
            double total = 0;
            for (double i = lowest; i <= top; i++)
            {
                total += HypergeometricPdf(i, m, k, n);
            }

            return Math.Min(1, total);
        }

        double upper = 0;
        for (double i = highest; i > top; i--)
        {
            upper += HypergeometricPdf(i, m, k, n);
        }

        return Math.Max(0, 1 - upper);
    }

    /// <summary>The least marked-item count whose probability has reached <paramref name="p"/>.</summary>
    public static double HypergeometricInv(double p, double m, double k, double n) =>
        !ValidHypergeometric(m, k, n)
            ? double.NaN
            : Quantile(p, x => HypergeometricCdf(x, m, k, n), Math.Max(0, n - (m - k)), Math.Min(k, n));

    /// <summary>The mean and variance of a hypergeometric.</summary>
    public static (double Mean, double Variance) HypergeometricStat(double m, double k, double n)
    {
        if (!ValidHypergeometric(m, k, n))
        {
            return (double.NaN, double.NaN);
        }

        double share = k / m;
        double mean = n * share;
        double variance = m <= 1 ? 0 : n * share * (1 - share) * ((m - n) / (m - 1));
        return (mean, variance);
    }

    // --- Negative binomial --------------------------------------------------------------------------

    /// <summary>
    /// The probability of exactly <paramref name="x"/> failures before the <paramref name="r"/>-th
    /// success. As with the geometric, MATLAB counts the failures.
    /// </summary>
    public static double NegativeBinomialPdf(double x, double r, double p)
    {
        if (!IsPositive(r) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x < 0 || x != Math.Floor(x))
        {
            return 0;
        }

        if (p == 1)
        {
            return x == 0 ? 1 : 0;
        }

        if (p == 0)
        {
            return 0;
        }

        return Math.Exp(
            SpecialFunctions.LogGamma(x + r) - SpecialFunctions.LogGamma(r) - SpecialFunctions.LogGamma(x + 1)
            + (r * Math.Log(p)) + (x * double.LogP1(-p)));
    }

    /// <summary>The probability of at most <paramref name="x"/> failures.</summary>
    public static double NegativeBinomialCdf(double x, double r, double p)
    {
        if (!IsPositive(r) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 0;
        }

        return p == 0 ? 0 : SpecialFunctions.BetaRegularized(p, r, k + 1);
    }

    /// <summary>The probability of more than <paramref name="x"/> failures.</summary>
    public static double NegativeBinomialUpper(double x, double r, double p)
    {
        if (!IsPositive(r) || !IsProbability(p) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        if (k < 0)
        {
            return 1;
        }

        return p == 0 ? 1 : SpecialFunctions.BetaRegularized(1 - p, k + 1, r);
    }

    /// <summary>The least failure count whose probability has reached <paramref name="p"/>.</summary>
    public static double NegativeBinomialInv(double p, double r, double probability) =>
        !IsPositive(r) || !IsProbability(probability)
            ? double.NaN
            : Quantile(p, x => NegativeBinomialCdf(x, r, probability), 0, double.PositiveInfinity);

    /// <summary>The mean and variance of a negative binomial.</summary>
    public static (double Mean, double Variance) NegativeBinomialStat(double r, double p) =>
        !IsPositive(r) || !IsProbability(p)
            ? (double.NaN, double.NaN)
            : (r * (1 - p) / p, r * (1 - p) / (p * p));

    /// <summary>
    /// One negative binomial draw, as a Poisson count whose rate is itself drawn from a gamma. That
    /// mixture is the negative binomial exactly, and unlike counting failures it costs the same
    /// whether the answer is two or two million.
    /// </summary>
    public static double NegativeBinomialSample(Random random, double r, double p)
    {
        if (!IsPositive(r) || !IsProbability(p))
        {
            return double.NaN;
        }

        if (p >= 1)
        {
            return 0;
        }

        if (p == 0)
        {
            return double.PositiveInfinity;
        }

        double rate = ContinuousDistributions.SampleGamma(random, r, (1 - p) / p);
        return ContinuousDistributions.SamplePoisson(random, rate);
    }

    // --- Discrete uniform ---------------------------------------------------------------------------

    /// <summary>The probability of any one of the integers 1 through <paramref name="n"/>.</summary>
    public static double DiscreteUniformPdf(double x, double n)
    {
        if (!IsCount(n) || n < 1 || double.IsNaN(x))
        {
            return double.NaN;
        }

        return x >= 1 && x <= n && x == Math.Floor(x) ? 1 / n : 0;
    }

    /// <summary>The probability of drawing at most <paramref name="x"/>.</summary>
    public static double DiscreteUniformCdf(double x, double n)
    {
        if (!IsCount(n) || n < 1 || double.IsNaN(x))
        {
            return double.NaN;
        }

        double k = Math.Floor(x);
        return k < 1 ? 0 : Math.Min(1, k / n);
    }

    /// <summary>The least integer whose probability has reached <paramref name="p"/>.</summary>
    public static double DiscreteUniformInv(double p, double n) =>
        !IsCount(n) || n < 1
            ? double.NaN
            : Quantile(p, x => DiscreteUniformCdf(x, n), 1, n);

    /// <summary>The mean and variance of a discrete uniform.</summary>
    public static (double Mean, double Variance) DiscreteUniformStat(double n) =>
        !IsCount(n) || n < 1
            ? (double.NaN, double.NaN)
            : ((n + 1) / 2, ((n * n) - 1) / 12);

    // --- Multinomial --------------------------------------------------------------------------------

    /// <summary>
    /// The probability of the count vector <paramref name="counts"/> under the category probabilities
    /// <paramref name="probabilities"/>. The counts must sum to a whole number of trials and the
    /// probabilities to one, which is the whole content of "multinomial".
    /// </summary>
    public static double MultinomialPdf(IReadOnlyList<double> counts, IReadOnlyList<double> probabilities)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(probabilities);

        double trials = 0;
        foreach (double count in counts)
        {
            if (double.IsNaN(count) || count < 0 || count != Math.Floor(count))
            {
                return double.NaN;
            }

            trials += count;
        }

        double mass = 0;
        foreach (double probability in probabilities)
        {
            if (double.IsNaN(probability) || probability < 0)
            {
                return double.NaN;
            }

            mass += probability;
        }

        if (Math.Abs(mass - 1) > 1e-9)
        {
            return double.NaN;
        }

        double logarithm = SpecialFunctions.LogGamma(trials + 1);
        for (int i = 0; i < counts.Count; i++)
        {
            if (counts[i] > 0 && probabilities[i] <= 0)
            {
                return 0;
            }

            logarithm -= SpecialFunctions.LogGamma(counts[i] + 1);
            if (counts[i] > 0)
            {
                logarithm += counts[i] * Math.Log(probabilities[i]);
            }
        }

        return Math.Exp(logarithm);
    }

    /// <summary>
    /// One multinomial draw: how many of <paramref name="trials"/> land in each category. Each
    /// category in turn is binomial in what is left, which is the definition unrolled.
    /// </summary>
    public static double[] MultinomialSample(Random random, double trials, IReadOnlyList<double> probabilities)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(probabilities);

        var counts = new double[probabilities.Count];
        double remainingTrials = trials;
        double remainingMass = 1;

        for (int i = 0; i < probabilities.Count - 1 && remainingTrials > 0; i++)
        {
            if (remainingMass <= 0)
            {
                break;
            }

            double share = Math.Min(1, probabilities[i] / remainingMass);
            counts[i] = BinomialSample(random, remainingTrials, share);
            remainingTrials -= counts[i];
            remainingMass -= probabilities[i];
        }

        if (probabilities.Count > 0)
        {
            counts[^1] = remainingTrials;
        }

        return counts;
    }

    // --- Shared -------------------------------------------------------------------------------------

    /// <summary>
    /// The least value in the support whose distribution function has reached <paramref name="p"/>,
    /// found by widening a bracket and then halving it.
    /// </summary>
    private static double Quantile(double p, Func<double, double> cdf, double lowest, double highest)
    {
        if (double.IsNaN(p) || p < 0 || p > 1)
        {
            return double.NaN;
        }

        if (p == 0)
        {
            return lowest;
        }

        if (p == 1)
        {
            return highest;
        }

        double upper = highest;
        if (double.IsPositiveInfinity(upper))
        {
            upper = Math.Max(1, lowest + 1);
            while (cdf(upper) < p)
            {
                upper *= 2;
                if (upper > SearchCeiling)
                {
                    return double.PositiveInfinity;
                }
            }
        }

        double lower = lowest;
        while (upper - lower > 0.5)
        {
            double middle = Math.Floor((lower + upper) / 2);
            if (middle <= lower)
            {
                break;
            }

            if (cdf(middle) >= p)
            {
                upper = middle;
            }
            else
            {
                lower = middle;
            }
        }

        return cdf(lower) >= p ? lower : upper;
    }

    /// <summary>The logarithm of "n choose k", which is where every count here gets its scale from.</summary>
    private static double LogChoose(double n, double k) =>
        SpecialFunctions.LogGamma(n + 1) - SpecialFunctions.LogGamma(k + 1) - SpecialFunctions.LogGamma(n - k + 1);

    private static bool IsCount(double n) => !double.IsNaN(n) && n >= 0 && n == Math.Floor(n) && !double.IsInfinity(n);

    private static bool IsProbability(double p) => !double.IsNaN(p) && p >= 0 && p <= 1;

    private static bool IsPositive(double x) => !double.IsNaN(x) && x > 0 && !double.IsInfinity(x);

    private static bool ValidHypergeometric(double m, double k, double n) =>
        IsCount(m) && IsCount(k) && IsCount(n) && k <= m && n <= m;
}
