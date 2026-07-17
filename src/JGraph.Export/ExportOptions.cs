using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Export;

/// <summary>
/// Options controlling a figure export. All sizes are in device-independent units (1/96 inch, the
/// same units the figure is laid out in on screen), so an export at the viewport size reproduces
/// exactly what the user sees; <see cref="Scale"/> supersamples raster output for print quality
/// without changing the layout.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>
    /// The figure size to lay out and render, in device-independent units. Null uses the figure
    /// model's own <c>Size</c>.
    /// </summary>
    public Size2D? Size { get; init; }

    /// <summary>
    /// Pixels per device-independent unit for raster formats (2.0 ≈ 192 DPI). Layout, fonts, and
    /// line widths are unaffected — the output is simply sharper. Ignored by vector formats.
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// The theme supplying chrome defaults (axis lines, tick labels, series palette) during the
    /// render; null uses the built-in light theme. Like everywhere else in JGraph, this does not
    /// restyle the model — colors stored on the figure (backgrounds, explicit styles) are used as-is,
    /// so call <see cref="ITheme.Apply"/> on the figure first to fully re-theme an export.
    /// </summary>
    public ITheme? Theme { get; init; }

    /// <summary>JPEG encoder quality in [1, 100].</summary>
    public int JpegQuality { get; init; } = 90;
}
