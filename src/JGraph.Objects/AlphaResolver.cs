using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Objects;

/// <summary>
/// Resolves the three things an axes says about transparency — the limits alpha data is spread over
/// (MATLAB <c>ALim</c>), the map it is looked up in (<c>Alphamap</c>), and whether that spread is
/// logarithmic (<c>AlphaScale</c>) — into one settled lookup a plot can sample per cell.
/// Each plot resolves this once per render pass, never per sample, exactly as
/// <see cref="ColorScaleResolver"/> does for color.
/// </summary>
internal readonly struct AlphaLookup
{
    private readonly IReadOnlyList<double>? _map;
    private readonly double _min;
    private readonly double _max;
    private readonly bool _log;

    internal AlphaLookup(IReadOnlyList<double>? map, double min, double max, bool log)
    {
        _map = map;
        _min = min;
        _max = max;
        _log = log;
    }

    /// <summary>The transparency one alpha-data value stands for.</summary>
    internal double Sample(double value) => AlphaSampler.Sample(_map, value, _min, _max, _log);
}

internal static class AlphaResolver
{
    /// <summary>
    /// The lookup a plot should sample its alpha data through. Limits the axes has not pinned are the
    /// plot's own data extent, which is what makes an unpinned <c>ALim</c> spread each plot over the
    /// whole map — the same rule <c>CLim</c> follows for color.
    /// </summary>
    internal static AlphaLookup ResolveAlpha(this PlotObject plot, DataRange dataBounds)
    {
        AxesModel? axes = plot.Axes;
        DataRange limits = axes?.AlphaLimits ?? dataBounds;
        double min = limits.Min;
        double max = limits.Max;
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
        {
            (min, max) = (0, 1);
        }

        return new AlphaLookup(axes?.Alphamap, min, max, axes is { AlphaScale: ColorScaleType.Log });
    }

    /// <summary>The extent of a grid of alpha data, ignoring the values that are not numbers.</summary>
    internal static DataRange BoundsOf(double[,] alphaData)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double value in alphaData)
        {
            if (double.IsFinite(value))
            {
                min = System.Math.Min(min, value);
                max = System.Math.Max(max, value);
            }
        }

        return double.IsFinite(min) && double.IsFinite(max) ? new DataRange(min, max) : new DataRange(0, 1);
    }
}
