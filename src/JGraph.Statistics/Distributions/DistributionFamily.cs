namespace JGraph.Statistics.Distributions;

/// <summary>
/// One distribution family, described well enough that the generic names —
/// <c>pdf('Normal', …)</c>, <c>cdf</c>, <c>icdf</c>, <c>random</c>, <c>mle</c> — can work from the
/// description alone rather than from a switch that has to be extended in five places at once.
/// </summary>
/// <param name="Name">The name MathWorks documents, as it should be echoed back.</param>
/// <param name="Prefix">The stem of the dedicated names: <c>norm</c> for <c>normpdf</c>.</param>
/// <param name="Aliases">
/// Every spelling accepted for <paramref name="Name"/>, lower-cased and without spaces or hyphens.
/// </param>
/// <param name="ParameterNames">The parameters in the order every function here takes them.</param>
/// <param name="Pdf">The density.</param>
/// <param name="Cdf">The distribution function.</param>
/// <param name="Inv">The quantile.</param>
/// <param name="Stat">The mean and variance.</param>
/// <param name="Sample">One draw.</param>
/// <param name="PositiveParameters">
/// Which parameters must stay above zero. A fitter searches these on a logarithmic scale, which is
/// what keeps an unconstrained simplex from proposing a negative standard deviation.
/// </param>
/// <param name="Discrete">
/// Whether the family lives on the integers. Everything elementwise — the density, the distribution
/// function, the quantile, the moments, the draw — is called identically either way; what the flag
/// decides is which fitters apply and how an <c>'upper'</c> tail is worded.
/// </param>
public sealed record DistributionFamily(
    string Name,
    string Prefix,
    string[] Aliases,
    string[] ParameterNames,
    Func<double, double[], double> Pdf,
    Func<double, double[], double> Cdf,
    Func<double, double[], double> Inv,
    Func<double[], (double Mean, double Variance)> Stat,
    Func<Random, double[], double> Sample,
    bool[] PositiveParameters,
    bool Discrete = false)
{
    /// <summary>How many parameters the family takes.</summary>
    public int ParameterCount => ParameterNames.Length;
}

/// <summary>
/// The continuous families this toolbox implements, and the lookup the generic distribution names
/// use.
/// </summary>
public static class ContinuousFamilies
{
    private static readonly DistributionFamily[] Families = Build();

    private static readonly Dictionary<string, DistributionFamily> ByAlias = BuildIndex();

    /// <summary>Every implemented continuous family.</summary>
    public static IReadOnlyList<DistributionFamily> All => Families;

    /// <summary>
    /// Finds a family by any documented spelling of its name. Matching ignores case, spaces and
    /// hyphens, because MathWorks documents <c>'Generalized Extreme Value'</c> and accepts
    /// <c>'gev'</c> for the same thing.
    /// </summary>
    public static DistributionFamily? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByAlias.TryGetValue(Normalize(name), out DistributionFamily? family) ? family : null;
    }

    /// <summary>Strips the punctuation and case that separate two spellings of the same name.</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<char> buffer = stackalloc char[name.Length];
        int length = 0;
        foreach (char c in name)
        {
            if (c is ' ' or '-' or '_')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    private static Dictionary<string, DistributionFamily> BuildIndex()
    {
        var index = new Dictionary<string, DistributionFamily>(StringComparer.Ordinal);
        foreach (DistributionFamily family in Families)
        {
            foreach (string alias in family.Aliases)
            {
                index[Normalize(alias)] = family;
            }

            index[Normalize(family.Name)] = family;
            index[Normalize(family.Prefix)] = family;
        }

        return index;
    }

    private static DistributionFamily[] Build() =>
    [
        new("Normal", "norm", ["normal", "gaussian"], ["mu", "sigma"],
            (x, p) => ContinuousDistributions.NormalPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NormalCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NormalInv(x, p[0], p[1]),
            p => ContinuousDistributions.NormalStat(p[0], p[1]),
            (r, p) => p[0] + (p[1] * ContinuousDistributions.StandardNormal(r)),
            [false, true]),

        new("Exponential", "exp", ["exponential"], ["mu"],
            (x, p) => ContinuousDistributions.ExponentialPdf(x, p[0]),
            (x, p) => ContinuousDistributions.ExponentialCdf(x, p[0]),
            (x, p) => ContinuousDistributions.ExponentialInv(x, p[0]),
            p => ContinuousDistributions.ExponentialStat(p[0]),
            (r, p) => -p[0] * Math.Log(ContinuousDistributions.NonZeroUniform(r)),
            [true]),

        new("Gamma", "gam", ["gamma"], ["a", "b"],
            (x, p) => ContinuousDistributions.GammaPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.GammaCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.GammaInv(x, p[0], p[1]),
            p => ContinuousDistributions.GammaStat(p[0], p[1]),
            (r, p) => ContinuousDistributions.SampleGamma(r, p[0], p[1]),
            [true, true]),

        new("Beta", "beta", ["beta"], ["a", "b"],
            (x, p) => ContinuousDistributions.BetaPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.BetaCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.BetaInv(x, p[0], p[1]),
            p => ContinuousDistributions.BetaStat(p[0], p[1]),
            (r, p) => ContinuousDistributions.SampleBeta(r, p[0], p[1]),
            [true, true]),

        new("Chi-square", "chi2", ["chisquare", "chi2", "chisq"], ["v"],
            (x, p) => ContinuousDistributions.Chi2Pdf(x, p[0]),
            (x, p) => ContinuousDistributions.Chi2Cdf(x, p[0]),
            (x, p) => ContinuousDistributions.Chi2Inv(x, p[0]),
            p => ContinuousDistributions.Chi2Stat(p[0]),
            (r, p) => ContinuousDistributions.SampleGamma(r, p[0] / 2, 2),
            [true]),

        new("T", "t", ["t", "student", "studentt"], ["v"],
            (x, p) => ContinuousDistributions.TPdf(x, p[0]),
            (x, p) => ContinuousDistributions.TCdf(x, p[0]),
            (x, p) => ContinuousDistributions.TInv(x, p[0]),
            p => ContinuousDistributions.TStat(p[0]),
            (r, p) => ContinuousDistributions.StandardNormal(r)
                / Math.Sqrt(ContinuousDistributions.SampleGamma(r, p[0] / 2, 2) / p[0]),
            [true]),

        new("F", "f", ["f"], ["v1", "v2"],
            (x, p) => ContinuousDistributions.FPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.FCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.FInv(x, p[0], p[1]),
            p => ContinuousDistributions.FStat(p[0], p[1]),
            (r, p) => ContinuousDistributions.SampleGamma(r, p[0] / 2, 2) / p[0]
                / (ContinuousDistributions.SampleGamma(r, p[1] / 2, 2) / p[1]),
            [true, true]),

        new("Uniform", "unif", ["uniform", "unif"], ["a", "b"],
            (x, p) => ContinuousDistributions.UniformPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.UniformCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.UniformInv(x, p[0], p[1]),
            p => ContinuousDistributions.UniformStat(p[0], p[1]),
            (r, p) => p[0] + (r.NextDouble() * (p[1] - p[0])),
            [false, false]),

        new("Lognormal", "logn", ["lognormal", "logn"], ["mu", "sigma"],
            (x, p) => ContinuousDistributions.LognormalPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.LognormalCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.LognormalInv(x, p[0], p[1]),
            p => ContinuousDistributions.LognormalStat(p[0], p[1]),
            (r, p) => Math.Exp(p[0] + (p[1] * ContinuousDistributions.StandardNormal(r))),
            [false, true]),

        new("Weibull", "wbl", ["weibull", "wbl"], ["a", "b"],
            (x, p) => ContinuousDistributions.WeibullPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.WeibullCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.WeibullInv(x, p[0], p[1]),
            p => ContinuousDistributions.WeibullStat(p[0], p[1]),
            (r, p) => p[0] * Math.Pow(-Math.Log(ContinuousDistributions.NonZeroUniform(r)), 1 / p[1]),
            [true, true]),

        new("Extreme Value", "ev", ["extremevalue", "ev"], ["mu", "sigma"],
            (x, p) => ContinuousDistributions.ExtremeValuePdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.ExtremeValueCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.ExtremeValueInv(x, p[0], p[1]),
            p => ContinuousDistributions.ExtremeValueStat(p[0], p[1]),
            (r, p) => ContinuousDistributions.ExtremeValueInv(r.NextDouble(), p[0], p[1]),
            [false, true]),

        new("Generalized Extreme Value", "gev", ["generalizedextremevalue", "gev"], ["k", "sigma", "mu"],
            (x, p) => ContinuousDistributions.GeneralizedExtremeValuePdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.GeneralizedExtremeValueCdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.GeneralizedExtremeValueInv(x, p[0], p[1], p[2]),
            p => ContinuousDistributions.GeneralizedExtremeValueStat(p[0], p[1], p[2]),
            (r, p) => ContinuousDistributions.GeneralizedExtremeValueInv(
                ContinuousDistributions.NonZeroUniform(r), p[0], p[1], p[2]),
            [false, true, false]),

        new("Generalized Pareto", "gp", ["generalizedpareto", "gp"], ["k", "sigma", "theta"],
            (x, p) => ContinuousDistributions.GeneralizedParetoPdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.GeneralizedParetoCdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.GeneralizedParetoInv(x, p[0], p[1], p[2]),
            p => ContinuousDistributions.GeneralizedParetoStat(p[0], p[1], p[2]),
            (r, p) => ContinuousDistributions.GeneralizedParetoInv(r.NextDouble(), p[0], p[1], p[2]),
            [false, true, false]),

        new("Rayleigh", "rayl", ["rayleigh", "rayl"], ["b"],
            (x, p) => ContinuousDistributions.RayleighPdf(x, p[0]),
            (x, p) => ContinuousDistributions.RayleighCdf(x, p[0]),
            (x, p) => ContinuousDistributions.RayleighInv(x, p[0]),
            p => ContinuousDistributions.RayleighStat(p[0]),
            (r, p) => ContinuousDistributions.RayleighInv(r.NextDouble(), p[0]),
            [true]),

        new("Noncentral Chi-square", "ncx2", ["noncentralchisquare", "ncx2"], ["v", "delta"],
            (x, p) => ContinuousDistributions.NoncentralChi2Pdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NoncentralChi2Cdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NoncentralChi2Inv(x, p[0], p[1]),
            p => ContinuousDistributions.NoncentralChi2Stat(p[0], p[1]),
            (r, p) => ContinuousDistributions.NoncentralChi2Sample(r, p[0], p[1]),
            [true, false]),

        new("Noncentral F", "ncf", ["noncentralf", "ncf"], ["v1", "v2", "delta"],
            (x, p) => ContinuousDistributions.NoncentralFPdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.NoncentralFCdf(x, p[0], p[1], p[2]),
            (x, p) => ContinuousDistributions.NoncentralFInv(x, p[0], p[1], p[2]),
            p => ContinuousDistributions.NoncentralFStat(p[0], p[1], p[2]),
            (r, p) => ContinuousDistributions.NoncentralChi2Sample(r, p[0], p[2]) / p[0]
                / (ContinuousDistributions.SampleGamma(r, p[1] / 2, 2) / p[1]),
            [true, true, false]),

        new("Noncentral T", "nct", ["noncentralt", "nct"], ["v", "delta"],
            (x, p) => ContinuousDistributions.NoncentralTPdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NoncentralTCdf(x, p[0], p[1]),
            (x, p) => ContinuousDistributions.NoncentralTInv(x, p[0], p[1]),
            p => ContinuousDistributions.NoncentralTStat(p[0], p[1]),
            (r, p) => (ContinuousDistributions.StandardNormal(r) + p[1])
                / Math.Sqrt(ContinuousDistributions.SampleGamma(r, p[0] / 2, 2) / p[0]),
            [true, false]),
    ];
}
