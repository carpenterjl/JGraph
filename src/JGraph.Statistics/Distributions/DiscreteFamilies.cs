namespace JGraph.Statistics.Distributions;

/// <summary>
/// The discrete families this toolbox implements, described the same way the continuous ones are so
/// that everything downstream — the elementwise builtins, the generic <c>pdf</c>/<c>cdf</c>/
/// <c>icdf</c>/<c>random</c> names, the moment functions — works on them without knowing they are
/// discrete.
/// </summary>
public static class DiscreteFamilies
{
    private static readonly DistributionFamily[] Families = Build();

    private static readonly Dictionary<string, DistributionFamily> ByAlias = BuildIndex();

    /// <summary>Every implemented discrete family.</summary>
    public static IReadOnlyList<DistributionFamily> All => Families;

    /// <summary>Finds a discrete family by any documented spelling of its name.</summary>
    public static DistributionFamily? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByAlias.TryGetValue(ContinuousFamilies.Normalize(name), out DistributionFamily? family)
            ? family
            : null;
    }

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
        new("Binomial", "bino", ["binomial"], ["n", "p"],
            (x, p) => DiscreteDistributions.BinomialPdf(x, p[0], p[1]),
            (x, p) => DiscreteDistributions.BinomialCdf(x, p[0], p[1]),
            (x, p) => DiscreteDistributions.BinomialInv(x, p[0], p[1]),
            p => DiscreteDistributions.BinomialStat(p[0], p[1]),
            (r, p) => DiscreteDistributions.BinomialSample(r, p[0], p[1]),
            [false, false], Discrete: true),

        new("Poisson", "poiss", ["poisson"], ["lambda"],
            (x, p) => DiscreteDistributions.PoissonPdf(x, p[0]),
            (x, p) => DiscreteDistributions.PoissonCdf(x, p[0]),
            (x, p) => DiscreteDistributions.PoissonInv(x, p[0]),
            p => DiscreteDistributions.PoissonStat(p[0]),
            (r, p) => ContinuousDistributions.SamplePoisson(r, p[0]),
            [true], Discrete: true),

        new("Geometric", "geo", ["geometric"], ["p"],
            (x, p) => DiscreteDistributions.GeometricPdf(x, p[0]),
            (x, p) => DiscreteDistributions.GeometricCdf(x, p[0]),
            (x, p) => DiscreteDistributions.GeometricInv(x, p[0]),
            p => DiscreteDistributions.GeometricStat(p[0]),
            (r, p) => DiscreteDistributions.GeometricSample(r, p[0]),
            [false], Discrete: true),

        new("Hypergeometric", "hyge", ["hypergeometric"], ["m", "k", "n"],
            (x, p) => DiscreteDistributions.HypergeometricPdf(x, p[0], p[1], p[2]),
            (x, p) => DiscreteDistributions.HypergeometricCdf(x, p[0], p[1], p[2]),
            (x, p) => DiscreteDistributions.HypergeometricInv(x, p[0], p[1], p[2]),
            p => DiscreteDistributions.HypergeometricStat(p[0], p[1], p[2]),
            (r, p) => DiscreteDistributions.HypergeometricInv(r.NextDouble(), p[0], p[1], p[2]),
            [false, false, false], Discrete: true),

        new("Negative Binomial", "nbin", ["negativebinomial", "nbin"], ["r", "p"],
            (x, p) => DiscreteDistributions.NegativeBinomialPdf(x, p[0], p[1]),
            (x, p) => DiscreteDistributions.NegativeBinomialCdf(x, p[0], p[1]),
            (x, p) => DiscreteDistributions.NegativeBinomialInv(x, p[0], p[1]),
            p => DiscreteDistributions.NegativeBinomialStat(p[0], p[1]),
            (r, p) => DiscreteDistributions.NegativeBinomialSample(r, p[0], p[1]),
            [true, false], Discrete: true),

        new("Discrete Uniform", "unid", ["discreteuniform", "unid"], ["n"],
            (x, p) => DiscreteDistributions.DiscreteUniformPdf(x, p[0]),
            (x, p) => DiscreteDistributions.DiscreteUniformCdf(x, p[0]),
            (x, p) => DiscreteDistributions.DiscreteUniformInv(x, p[0]),
            p => DiscreteDistributions.DiscreteUniformStat(p[0]),
            (r, p) => Math.Floor(r.NextDouble() * p[0]) + 1,
            [false], Discrete: true),
    ];
}
