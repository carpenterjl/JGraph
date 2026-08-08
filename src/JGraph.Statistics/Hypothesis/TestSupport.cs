using JGraph.Numerics;
using JGraph.Statistics.Distributions;

namespace JGraph.Statistics.Hypothesis;

/// <summary>Which alternative a test is measured against.</summary>
public enum Tail
{
    /// <summary>The quantity differs from its null value, in either direction.</summary>
    Both,

    /// <summary>The quantity is greater than its null value.</summary>
    Right,

    /// <summary>The quantity is less than its null value.</summary>
    Left,
}

/// <summary>
/// The arithmetic every hypothesis test in this folder shares: turning a statistic into a tail
/// probability, reading a probability off a table of published critical values, and the combinatorial
/// counting the exact small-sample tests are built out of.
/// </summary>
public static class TestSupport
{
    /// <summary>The tail probability of a standard normal statistic.</summary>
    public static double NormalTail(double z, Tail tail) => tail switch
    {
        Tail.Right => ContinuousDistributions.NormalCdf(-z, 0, 1),
        Tail.Left => ContinuousDistributions.NormalCdf(z, 0, 1),
        _ => 2 * ContinuousDistributions.NormalCdf(-Math.Abs(z), 0, 1),
    };

    /// <summary>The tail probability of a Student's t statistic.</summary>
    public static double StudentTail(double t, double df, Tail tail) => tail switch
    {
        Tail.Right => ContinuousDistributions.TCdf(-t, df),
        Tail.Left => ContinuousDistributions.TCdf(t, df),
        _ => 2 * ContinuousDistributions.TCdf(-Math.Abs(t), df),
    };

    /// <summary>
    /// The tail probability of a statistic whose null distribution is not symmetric — a variance ratio
    /// or a sum of squares — where the two-sided answer is twice the smaller tail rather than twice a
    /// tail of the absolute value.
    /// </summary>
    public static double AsymmetricTail(double cumulative, Tail tail) => tail switch
    {
        Tail.Right => 1 - cumulative,
        Tail.Left => cumulative,
        _ => Math.Min(1, 2 * Math.Min(cumulative, 1 - cumulative)),
    };

    /// <summary>Whether a p-value rejects at the given level, with the level itself checked.</summary>
    public static bool Rejects(double p, double alpha)
    {
        if (!(alpha > 0) || !(alpha < 1))
        {
            throw new ArgumentException("the significance level must lie strictly between 0 and 1.");
        }

        return p <= alpha;
    }

    /// <summary>The observations with every NaN dropped, which is what a test of a sample does with one.</summary>
    public static double[] Clean(IReadOnlyList<double> values) => DescriptiveStatistics.WithoutNaN(values);

    /// <summary>
    /// The differences of two paired samples, with any pair holding a NaN dropped whole — a pair is one
    /// observation, so half of it is no observation at all.
    /// </summary>
    public static double[] PairedDifferences(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException("a paired test needs the two samples to be the same length.");
        }

        var differences = new List<double>(x.Count);
        for (int i = 0; i < x.Count; i++)
        {
            double difference = x[i] - y[i];
            if (!double.IsNaN(difference))
            {
                differences.Add(difference);
            }
        }

        return [.. differences];
    }

    /// <summary>The natural logarithm of the binomial coefficient, which the exact tests count with.</summary>
    public static double LogChoose(int n, int k) =>
        k < 0 || k > n
            ? double.NegativeInfinity
            : SpecialFunctions.LogGamma(n + 1) - SpecialFunctions.LogGamma(k + 1) - SpecialFunctions.LogGamma(n - k + 1);

    /// <summary>
    /// The tie correction a rank test divides its variance by: the sum of <c>t³ − t</c> over the groups
    /// of equal values, which is zero when every observation is distinct.
    /// </summary>
    public static double TieAdjustment(IReadOnlyList<double> sortedValues)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        double adjustment = 0;
        int i = 0;
        while (i < sortedValues.Count)
        {
            int j = i;
            while (j + 1 < sortedValues.Count && sortedValues[j + 1] == sortedValues[i])
            {
                j++;
            }

            double run = j - i + 1;
            adjustment += (run * run * run) - run;
            i = j + 1;
        }

        return adjustment;
    }

    /// <summary>The ranks of the values, ties sharing their average rank, one-based.</summary>
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));

        var ranks = new double[n];
        int position = 0;
        while (position < n)
        {
            int last = position;
            while (last + 1 < n && values[order[last + 1]] == values[order[position]])
            {
                last++;
            }

            double shared = (position + last) / 2.0 + 1;
            for (int i = position; i <= last; i++)
            {
                ranks[order[i]] = shared;
            }

            position = last + 1;
        }

        return ranks;
    }
}

/// <summary>
/// A published table of critical values, read in either direction: a statistic in, an approximate
/// probability out, or a probability in and the value it is reached at out.
/// </summary>
/// <remarks>
/// The goodness-of-fit statistics whose null distribution depends on the parameters having been
/// estimated from the same data — Lilliefors' and Anderson–Darling's composite forms — have no closed
/// form and are published as tables. Interpolating linearly in the logarithm of the probability is what
/// makes the curve between two tabulated points smooth rather than kinked; outside the table the
/// answer is clamped to its ends and says so, because extrapolating a tail off five points invents
/// digits.
/// </remarks>
public sealed class CriticalValueTable
{
    private readonly double[] _probabilities;
    private readonly double[] _values;

    /// <summary>
    /// Builds the table from probabilities in descending order and the statistic values they
    /// correspond to, which are therefore ascending.
    /// </summary>
    public CriticalValueTable(double[] probabilities, double[] values)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(values);
        if (probabilities.Length != values.Length || probabilities.Length < 2)
        {
            throw new ArgumentException("a critical-value table needs at least two matching pairs.");
        }

        _probabilities = probabilities;
        _values = values;
    }

    /// <summary>The largest probability the table covers.</summary>
    public double LargestProbability => _probabilities[0];

    /// <summary>The smallest probability the table covers.</summary>
    public double SmallestProbability => _probabilities[^1];

    /// <summary>
    /// The probability of seeing a statistic at least as large as <paramref name="statistic"/>, clamped
    /// to the range the table covers.
    /// </summary>
    public double Probability(double statistic)
    {
        if (double.IsNaN(statistic))
        {
            return double.NaN;
        }

        if (statistic <= _values[0])
        {
            return LargestProbability;
        }

        if (statistic >= _values[^1])
        {
            return SmallestProbability;
        }

        for (int i = 1; i < _values.Length; i++)
        {
            if (statistic > _values[i])
            {
                continue;
            }

            double share = (statistic - _values[i - 1]) / (_values[i] - _values[i - 1]);
            double logProbability = Math.Log(_probabilities[i - 1])
                + (share * (Math.Log(_probabilities[i]) - Math.Log(_probabilities[i - 1])));
            return Math.Exp(logProbability);
        }

        return SmallestProbability;
    }

    /// <summary>The statistic value at which <paramref name="probability"/> is reached.</summary>
    public double Critical(double probability)
    {
        double wanted = Math.Clamp(probability, SmallestProbability, LargestProbability);
        if (wanted >= LargestProbability)
        {
            return _values[0];
        }

        if (wanted <= SmallestProbability)
        {
            return _values[^1];
        }

        for (int i = 1; i < _probabilities.Length; i++)
        {
            if (wanted < _probabilities[i])
            {
                continue;
            }

            double share = (Math.Log(wanted) - Math.Log(_probabilities[i - 1]))
                / (Math.Log(_probabilities[i]) - Math.Log(_probabilities[i - 1]));
            return _values[i - 1] + (share * (_values[i] - _values[i - 1]));
        }

        return _values[^1];
    }
}
