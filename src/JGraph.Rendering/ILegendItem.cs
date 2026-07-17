using JGraph.Core.Drawing;

namespace JGraph.Rendering;

/// <summary>
/// The visual key a plot contributes to a legend: a line sample, a marker sample, and/or a filled
/// swatch color, already resolved to concrete colors. A null line or marker means that element is
/// not part of the key.
/// </summary>
public readonly struct LegendKey
{
    public LegendKey(LineStyle? line, MarkerStyle? marker, Color? swatch)
    {
        Line = line;
        Marker = marker;
        Swatch = swatch;
    }

    public LineStyle? Line { get; }

    public MarkerStyle? Marker { get; }

    /// <summary>A filled swatch color (for bars/areas), or null when the line/marker sample suffices.</summary>
    public Color? Swatch { get; }
}

/// <summary>
/// Implemented by plot objects that appear in the legend. Rendering depends only on this interface,
/// so the object model of concrete plots stays in the Objects layer.
/// </summary>
public interface ILegendItem
{
    /// <summary>The legend label; an empty string excludes the item from the legend.</summary>
    string LegendLabel { get; }

    /// <summary>Builds the legend key, using <paramref name="seriesColor"/> where no explicit color is set.</summary>
    LegendKey GetLegendKey(Color seriesColor);
}
