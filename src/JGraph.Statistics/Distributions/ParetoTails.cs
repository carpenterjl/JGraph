namespace JGraph.Statistics.Distributions;

/// <summary>
/// A distribution that is empirical in the middle and generalized Pareto in each tail.
/// </summary>
/// <remarks>
/// <para>
/// The point of the construction is that a sample says a lot about its middle and very little about
/// its tails: the largest observation is the largest observation, and nothing in the sample speaks to
/// what lies beyond it. So the middle is taken as it comes — the empirical distribution function,
/// interpolated — and each tail is replaced by the one family that extreme value theory says a tail
/// tends toward, fitted to the observations that fall in it.
/// </para>
/// <para>
/// The two boundaries are given as probabilities rather than as values, which is what makes the
/// construction scale-free: "the lowest tenth" means the same thing whatever the data measures.
/// </para>
/// </remarks>
public sealed class ParetoTails
{
    private readonly double[] _sorted;
    private readonly double _lowerProbability;
    private readonly double _upperProbability;
    private readonly double _lowerBoundary;
    private readonly double _upperBoundary;
    private readonly double[] _lowerParameters;
    private readonly double[] _upperParameters;
    private readonly bool _hasLower;
    private readonly bool _hasUpper;

    /// <summary>Fits the two tails of a sample, leaving the middle empirical.</summary>
    /// <param name="values">The sample.</param>
    /// <param name="lower">The probability below which the lower tail begins; zero for no lower tail.</param>
    /// <param name="upper">The probability above which the upper tail begins; one for no upper tail.</param>
    public ParetoTails(IReadOnlyList<double> values, double lower, double upper)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!(lower >= 0 && upper <= 1 && lower <= upper))
        {
            throw new ArgumentException(
                "The tail boundaries are probabilities, the lower one no greater than the upper.", nameof(lower));
        }

        _sorted = DescriptiveStatistics.WithoutNaN(values);
        if (_sorted.Length < 2)
        {
            throw new ArgumentException("A piecewise fit needs at least two observations.", nameof(values));
        }

        Array.Sort(_sorted);
        _lowerProbability = lower;
        _upperProbability = upper;
        _lowerBoundary = EmpiricalQuantile(lower);
        _upperBoundary = EmpiricalQuantile(upper);

        _hasLower = lower > 0;
        _hasUpper = upper < 1;

        // Each tail is fitted to its own exceedances, measured from the boundary — the lower tail after
        // reflecting, because a generalized Pareto only knows how to point one way.
        if (_hasLower)
        {
            var exceedances = new List<double>();
            foreach (double value in _sorted)
            {
                if (value < _lowerBoundary)
                {
                    exceedances.Add(_lowerBoundary - value);
                }
            }

            _lowerParameters = FitTail(exceedances);
        }
        else
        {
            _lowerParameters = [0, 1];
        }

        if (_hasUpper)
        {
            var exceedances = new List<double>();
            foreach (double value in _sorted)
            {
                if (value > _upperBoundary)
                {
                    exceedances.Add(value - _upperBoundary);
                }
            }

            _upperParameters = FitTail(exceedances);
        }
        else
        {
            _upperParameters = [0, 1];
        }
    }

    /// <summary>The value the lower tail ends at.</summary>
    public double LowerBoundary => _lowerBoundary;

    /// <summary>The value the upper tail begins at.</summary>
    public double UpperBoundary => _upperBoundary;

    /// <summary>The shape and scale fitted to the lower tail.</summary>
    public IReadOnlyList<double> LowerParameters => _lowerParameters;

    /// <summary>The shape and scale fitted to the upper tail.</summary>
    public IReadOnlyList<double> UpperParameters => _upperParameters;

    /// <summary>How many observations the sample held.</summary>
    public int Count => _sorted.Length;

    /// <summary>The distribution function.</summary>
    public double Cdf(double x)
    {
        if (_hasLower && x < _lowerBoundary)
        {
            double exceedance = _lowerBoundary - x;
            return _lowerProbability * (1 - GeneralizedParetoCdf(exceedance, _lowerParameters));
        }

        if (_hasUpper && x > _upperBoundary)
        {
            double exceedance = x - _upperBoundary;
            return _upperProbability
                + ((1 - _upperProbability) * GeneralizedParetoCdf(exceedance, _upperParameters));
        }

        return EmpiricalCdf(x);
    }

    /// <summary>The quantile.</summary>
    public double Inv(double p)
    {
        if (p is < 0 or > 1)
        {
            return double.NaN;
        }

        if (_hasLower && p < _lowerProbability)
        {
            double tail = 1 - (p / _lowerProbability);
            return _lowerBoundary - GeneralizedParetoInv(tail, _lowerParameters);
        }

        if (_hasUpper && p > _upperProbability)
        {
            double tail = (p - _upperProbability) / (1 - _upperProbability);
            return _upperBoundary + GeneralizedParetoInv(tail, _upperParameters);
        }

        return EmpiricalQuantile(p);
    }

    /// <summary>The density, which is a real density in the tails and a difference quotient in the middle.</summary>
    public double Pdf(double x)
    {
        if (_hasLower && x < _lowerBoundary)
        {
            return _lowerProbability * GeneralizedParetoPdf(_lowerBoundary - x, _lowerParameters);
        }

        if (_hasUpper && x > _upperBoundary)
        {
            return (1 - _upperProbability) * GeneralizedParetoPdf(x - _upperBoundary, _upperParameters);
        }

        double step = (_sorted[^1] - _sorted[0]) / Math.Max(_sorted.Length, 2) / 2;
        if (!(step > 0))
        {
            return double.NaN;
        }

        return (Cdf(x + step) - Cdf(x - step)) / (2 * step);
    }

    /// <summary>Which piece a value falls in: -1 the lower tail, 0 the middle, 1 the upper tail.</summary>
    public int Segment(double x) =>
        _hasLower && x < _lowerBoundary ? -1 : _hasUpper && x > _upperBoundary ? 1 : 0;

    /// <summary>
    /// A generalized Pareto fitted to a set of exceedances by maximum likelihood, or the exponential
    /// that is its shapeless limit when the search finds nothing better.
    /// </summary>
    private static double[] FitTail(List<double> exceedances)
    {
        if (exceedances.Count < 2)
        {
            double mean = exceedances.Count == 1 ? exceedances[0] : 1;
            return [0, Math.Max(mean, 1e-12)];
        }

        DistributionFamily family = ContinuousFamilies.Find("GeneralizedPareto")!;
        DistributionFitting.Sample sample = DistributionFitting.MakeSample(exceedances, null, null);

        // The threshold is zero by construction here — the exceedances were measured from it — so only
        // the shape and the scale are fitted, which is the pair the tail is described by.
        double[] fitted = DistributionFitting.MaximizeGiven(family, sample, [null, null, 0]);
        return [fitted[0], Math.Max(fitted[1], 1e-12)];
    }

    private static double GeneralizedParetoCdf(double x, double[] parameters) =>
        ContinuousDistributions.GeneralizedParetoCdf(x, parameters[0], parameters[1], 0);

    private static double GeneralizedParetoPdf(double x, double[] parameters) =>
        ContinuousDistributions.GeneralizedParetoPdf(x, parameters[0], parameters[1], 0);

    private static double GeneralizedParetoInv(double p, double[] parameters) =>
        ContinuousDistributions.GeneralizedParetoInv(p, parameters[0], parameters[1], 0);

    /// <summary>The empirical distribution function, linear between the observations.</summary>
    private double EmpiricalCdf(double x)
    {
        int n = _sorted.Length;
        if (x <= _sorted[0])
        {
            return 0;
        }

        if (x >= _sorted[^1])
        {
            return 1;
        }

        int index = Array.BinarySearch(_sorted, x);
        if (index >= 0)
        {
            return (index + 0.5) / n;
        }

        index = ~index;
        double low = _sorted[index - 1];
        double high = _sorted[index];
        double fraction = high > low ? (x - low) / (high - low) : 0;
        return ((index - 0.5) + fraction) / n;
    }

    /// <summary>The empirical quantile, the same interpolation read the other way.</summary>
    private double EmpiricalQuantile(double p)
    {
        int n = _sorted.Length;
        double position = (p * n) - 0.5;
        if (position <= 0)
        {
            return _sorted[0];
        }

        if (position >= n - 1)
        {
            return _sorted[^1];
        }

        int index = (int)Math.Floor(position);
        double fraction = position - index;
        return _sorted[index] + (fraction * (_sorted[index + 1] - _sorted[index]));
    }
}
