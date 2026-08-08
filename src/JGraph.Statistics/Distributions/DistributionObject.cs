using JGraph.Statistics.Quadrature;

namespace JGraph.Statistics.Distributions;

/// <summary>
/// A probability distribution as an object: a family, the parameter values it was given or fitted,
/// and the interval it was truncated to.
/// </summary>
/// <remarks>
/// <para>
/// The object adds exactly one thing to the family it wraps, and that thing is truncation. Everything
/// else — the density, the distribution function, the quantile, the draw, the likelihood — is the
/// family's, called through here so that a truncated distribution answers with the same words as an
/// untruncated one.
/// </para>
/// <para>
/// Truncation is not a modifier that can be applied to each answer separately. Conditioning on the
/// interval renormalizes the density, remaps the quantile, and — this is the part with real work in
/// it — destroys every closed form for the moments. So a truncated mean is integrated, over
/// probability rather than over the variable, which keeps the range of integration finite even when
/// the interval is not.
/// </para>
/// </remarks>
/// <param name="Family">The distribution family.</param>
/// <param name="Parameters">Its parameter values, in the family's own order.</param>
/// <param name="Lower">The lower truncation limit, or negative infinity.</param>
/// <param name="Upper">The upper truncation limit, or positive infinity.</param>
public sealed record DistributionObject(
    DistributionFamily Family, double[] Parameters, double Lower, double Upper)
{
    /// <summary>An untruncated distribution.</summary>
    public DistributionObject(DistributionFamily family, double[] parameters)
        : this(family, parameters, double.NegativeInfinity, double.PositiveInfinity)
    {
    }

    /// <summary>Whether either limit was actually set.</summary>
    public bool IsTruncated => !double.IsNegativeInfinity(Lower) || !double.IsPositiveInfinity(Upper);

    /// <summary>The probability the untruncated distribution puts inside the interval.</summary>
    public double Retained => IsTruncated
        ? Math.Max(Family.Cdf(Upper, Parameters) - LowerTail, 0)
        : 1;

    /// <summary>
    /// The probability below the lower limit. A discrete family keeps the mass sitting exactly on the
    /// limit, because the interval includes its own endpoints.
    /// </summary>
    private double LowerTail => double.IsNegativeInfinity(Lower)
        ? 0
        : Family.Discrete
            ? Family.Cdf(Lower, Parameters) - Family.Pdf(Lower, Parameters)
            : Family.Cdf(Lower, Parameters);

    /// <summary>The density, renormalized over the interval and zero outside it.</summary>
    public double Pdf(double x)
    {
        if (x < Lower || x > Upper)
        {
            return 0;
        }

        double retained = Retained;
        return retained <= 0 ? double.NaN : Family.Pdf(x, Parameters) / retained;
    }

    /// <summary>The distribution function, conditioned on the interval.</summary>
    public double Cdf(double x)
    {
        if (x < Lower)
        {
            return 0;
        }

        if (x > Upper)
        {
            return 1;
        }

        double retained = Retained;
        return retained <= 0 ? double.NaN
            : Math.Clamp((Family.Cdf(x, Parameters) - LowerTail) / retained, 0, 1);
    }

    /// <summary>The quantile, read off the part of the family the interval kept.</summary>
    public double Inv(double p)
    {
        if (p is < 0 or > 1)
        {
            return double.NaN;
        }

        if (!IsTruncated)
        {
            return Family.Inv(p, Parameters);
        }

        double retained = Retained;
        if (retained <= 0)
        {
            return double.NaN;
        }

        double answer = Family.Inv(LowerTail + (p * retained), Parameters);
        return Math.Clamp(answer, Lower, Upper);
    }

    /// <summary>
    /// One draw. Truncated, it is the quantile of a uniform — inversion rather than rejection, so
    /// that a distribution truncated to a tail holding a millionth of the mass costs one draw and not
    /// a million.
    /// </summary>
    public double Sample(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return IsTruncated ? Inv(random.NextDouble()) : Family.Sample(random, Parameters);
    }

    /// <summary>The mean and the variance.</summary>
    public (double Mean, double Variance) Moments()
    {
        if (!IsTruncated)
        {
            return Family.Stat(Parameters);
        }

        return Family.Discrete ? DiscreteMoments() : ContinuousMoments();
    }

    /// <summary>The mean.</summary>
    public double Mean() => Moments().Mean;

    /// <summary>The variance.</summary>
    public double Variance() => Moments().Variance;

    /// <summary>The standard deviation.</summary>
    public double Deviation() => Math.Sqrt(Variance());

    /// <summary>The median.</summary>
    public double Median() => Inv(0.5);

    /// <summary>The distance between the quartiles.</summary>
    public double InterquartileRange() => Inv(0.75) - Inv(0.25);

    /// <summary>The same distribution conditioned on <paramref name="lower"/> to <paramref name="upper"/>.</summary>
    public DistributionObject Truncate(double lower, double upper)
    {
        if (!(upper > lower))
        {
            throw new ArgumentException("A truncation interval needs an upper limit above its lower one.", nameof(upper));
        }

        return this with { Lower = lower, Upper = upper };
    }

    /// <summary>
    /// The negative log-likelihood of a sample under this distribution, with the truncation counted:
    /// conditioning on an interval divides every density by the mass the interval kept, which adds one
    /// term per observation and is the whole difference from the untruncated sum.
    /// </summary>
    public double NegativeLogLikelihood(in DistributionFitting.Sample sample)
    {
        double total = DistributionFitting.NegativeLogLikelihood(Family, Parameters, sample);
        if (!IsTruncated)
        {
            return total;
        }

        double retained = Retained;
        return retained <= 0 ? double.PositiveInfinity : total + (sample.Count * Math.Log(retained));
    }

    /// <summary>
    /// The moments of a truncated continuous distribution, integrated over probability rather than
    /// over the variable. Substituting the quantile turns an interval that may run to infinity into
    /// the unit interval, which is what lets one rule serve every family here.
    /// </summary>
    private (double Mean, double Variance) ContinuousMoments()
    {
        const int Nodes = 16;
        const int Panels = 60;

        // Graded rather than even, because the quantile is steepest at the ends: on a distribution
        // truncated to a half-line it runs off to infinity there, and an even mesh spends its nodes
        // in the flat middle while the tail it is missing is the part that moves the answer.
        double[] mesh = [0, 1e-4, 1e-3, 0.01, 0.1, 0.5, 0.9, 0.99, 0.999, 0.9999, 1];

        double Over(Func<double, double> f)
        {
            double total = 0;
            for (int i = 0; i + 1 < mesh.Length; i++)
            {
                total += GaussLegendre.Integrate(f, mesh[i], mesh[i + 1], Nodes, Panels);
            }

            return total;
        }

        double mean = Over(Inv);
        double second = Over(q =>
        {
            double x = Inv(q);
            return x * x;
        });

        return (mean, Math.Max(second - (mean * mean), 0));
    }

    /// <summary>
    /// The moments of a truncated discrete distribution, summed over the values the interval holds.
    /// A sum is exact where the quadrature above would be approximating a staircase.
    /// </summary>
    private (double Mean, double Variance) DiscreteMoments()
    {
        double from = Math.Ceiling(double.IsNegativeInfinity(Lower) ? Family.Inv(1e-12, Parameters) : Lower);
        double to = Math.Floor(double.IsPositiveInfinity(Upper) ? Family.Inv(1 - 1e-12, Parameters) : Upper);
        if (!(to >= from))
        {
            return (double.NaN, double.NaN);
        }

        // A bound rather than a promise: a discrete family spread over more than this many values has
        // a tail contributing less than the arithmetic below can represent anyway.
        const int Ceiling = 2_000_000;
        if (to - from > Ceiling)
        {
            to = from + Ceiling;
        }

        double mean = 0;
        double second = 0;
        for (double k = from; k <= to; k++)
        {
            double mass = Pdf(k);
            mean += k * mass;
            second += k * k * mass;
        }

        return (mean, Math.Max(second - (mean * mean), 0));
    }
}
