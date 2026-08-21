using JGraph.Core.Model;

namespace JGraph.Objects;

/// <summary>
/// Answers whether a plot's axes spreads its colormap logarithmically (MATLAB <c>ColorScale</c>).
/// Each color-mapped plot resolves this once per render pass — never per sample — and hands it to
/// <see cref="JGraph.Core.Drawing.Colormap.Sample(double, double, double, bool)"/>.
/// </summary>
internal static class ColorScaleResolver
{
    internal static bool LogColorScale(this PlotObject plot) =>
        plot.Axes is { ColorScale: ColorScaleType.Log };
}
