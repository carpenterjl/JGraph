namespace JGraph.Core.Drawing;

/// <summary>
/// Looks alpha data up in an alphamap, the way <see cref="Colormap"/> looks color data up in a color
/// table (MATLAB <c>Alphamap</c>, <c>ALim</c> and <c>AlphaScale</c>). An alphamap is a plain list of
/// transparencies rather than a type of its own, because that is what MATLAB's is: a vector the
/// script may hand over whole.
/// </summary>
public static class AlphaSampler
{
    /// <summary>How many entries MATLAB's own alphamap has, and so how many the default ramp has.</summary>
    public const int DefaultLength = 64;

    /// <summary>
    /// The alphamap an axes uses when it has not been given one: an even ramp from fully clear to
    /// fully opaque, which is what makes alpha data read as "more means more solid".
    /// </summary>
    public static IReadOnlyList<double> DefaultMap { get; } = BuildRamp();

    private static double[] BuildRamp()
    {
        var ramp = new double[DefaultLength];
        for (int i = 0; i < DefaultLength; i++)
        {
            ramp[i] = (double)i / (DefaultLength - 1);
        }

        return ramp;
    }

    /// <summary>
    /// Samples an alphamap at <paramref name="t"/> (clamped to [0, 1]), interpolating between the two
    /// nearest entries. A non-finite input maps to the clear end, matching <see cref="Colormap"/>.
    /// </summary>
    public static double Sample(IReadOnlyList<double>? map, double t)
    {
        IReadOnlyList<double> table = map is { Count: > 0 } given ? given : DefaultMap;
        if (table.Count == 1)
        {
            return System.Math.Clamp(table[0], 0, 1);
        }

        if (double.IsNaN(t))
        {
            return System.Math.Clamp(table[0], 0, 1);
        }

        t = System.Math.Clamp(t, 0, 1);
        double scaled = t * (table.Count - 1);
        int index = (int)System.Math.Floor(scaled);
        if (index >= table.Count - 1)
        {
            return System.Math.Clamp(table[^1], 0, 1);
        }

        double low = System.Math.Clamp(table[index], 0, 1);
        double high = System.Math.Clamp(table[index + 1], 0, 1);
        return low + ((high - low) * (scaled - index));
    }

    /// <summary>Maps a value within [min, max] to a transparency, clamping out-of-range values to the ends.</summary>
    public static double Sample(IReadOnlyList<double>? map, double value, double min, double max)
    {
        double span = max - min;
        double t = System.Math.Abs(span) < double.Epsilon ? 0.5 : (value - min) / span;
        return Sample(map, t);
    }

    /// <summary>
    /// The same mapping with the axes' alpha scale applied (MATLAB <c>AlphaScale</c>): logarithmic
    /// spreads the decades evenly instead of the values. Limits that cannot be logged — zero or
    /// negative — fall back to the linear spread, and a non-positive value lands on the clear end.
    /// </summary>
    public static double Sample(IReadOnlyList<double>? map, double value, double min, double max, bool logScale)
    {
        if (!logScale || min <= 0 || max <= 0)
        {
            return Sample(map, value, min, max);
        }

        double low = System.Math.Log10(min);
        double high = System.Math.Log10(max);
        double span = high - low;
        double t = value <= 0 ? 0
            : System.Math.Abs(span) < double.Epsilon ? 0.5
            : (System.Math.Log10(value) - low) / span;
        return Sample(map, t);
    }

    /// <summary>
    /// The alphamap as a table of <paramref name="count"/> entries — the form MATLAB's <c>alphamap</c>
    /// answers in, and what lets a script read a map back whatever length it was given in.
    /// </summary>
    public static double[] Resample(IReadOnlyList<double>? map, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var table = new double[count];
        if (count == 1)
        {
            table[0] = Sample(map, 0.5);
            return table;
        }

        for (int i = 0; i < count; i++)
        {
            table[i] = Sample(map, (double)i / (count - 1));
        }

        return table;
    }
}
