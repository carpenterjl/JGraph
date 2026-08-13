namespace JGraph.Maths.Sampling;

/// <summary>
/// What most of a set of readings covers, and where their middle is — the two numbers every
/// tolerance and every pole test in this namespace is a fraction of.
/// </summary>
/// <remarks>
/// The outer twentieth at each end is left out of the spread. Without that, one reading taken beside
/// a pole passes itself off as the curve's range and nothing after it looks unusual by comparison,
/// which is the failure this measure exists to avoid.
/// </remarks>
internal static class ReadingSpread
{
    public static (double Spread, double Centre) Of(IEnumerable<double> values)
    {
        double[] finite = [.. values.Where(double.IsFinite).OrderBy(v => v)];
        if (finite.Length == 0)
        {
            return (1, 0);
        }

        double spread = Quantile(finite, 0.95) - Quantile(finite, 0.05);
        if (spread <= 0)
        {
            spread = finite[^1] - finite[0];
        }

        return (spread > 0 ? spread : 1, Quantile(finite, 0.5));
    }

    /// <summary>Whether a reading is far enough from the middle to be the curve leaving.</summary>
    public static bool RanAway(double value, double centre, double spread, double poleFactor) =>
        double.IsFinite(value) && System.Math.Abs(value - centre) > poleFactor * spread;

    private static double Quantile(double[] sorted, double fraction)
    {
        double position = fraction * (sorted.Length - 1);
        int lower = (int)System.Math.Floor(position);
        int upper = System.Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
