namespace JGraph.Maths.Sampling;

/// <summary>
/// How hard <see cref="AdaptiveSampler1D"/> should look at a curve before it accepts a straight line
/// as a fair drawing of it.
/// </summary>
/// <remarks>
/// Every threshold here is a fraction of the curve's own spread rather than an absolute number, so
/// the same settings sample a signal in volts and a signal in microvolts the same way.
/// </remarks>
public sealed record AdaptiveSamplerOptions
{
    /// <summary>The evenly spaced readings taken before any refinement, endpoints included.</summary>
    /// <remarks>
    /// This is the only pass that sees the whole domain at once, so it is also the pass that decides
    /// what "the curve's spread" means; everything after it looks at one interval at a time.
    /// </remarks>
    public int SeedCount { get; init; } = 33;

    /// <summary>How many rounds of refinement an interval may go through before it is left as it is.</summary>
    public int MaxRounds { get; init; } = 12;

    /// <summary>The ceiling on how many readings the sampler will take in total.</summary>
    public int MaxPoints { get; init; } = 2000;

    /// <summary>
    /// How far a reading may sit off the straight line drawn between its neighbours, as a fraction of
    /// the component's own spread, before the interval is split.
    /// </summary>
    public double Tolerance { get; init; } = 0.002;

    /// <summary>
    /// How many spreads away from the middle of the readings a value has to be before it counts as
    /// the curve leaving rather than a reading of it. Such a value is drawn as a gap.
    /// </summary>
    public double PoleFactor { get; init; } = 20.0;
}
