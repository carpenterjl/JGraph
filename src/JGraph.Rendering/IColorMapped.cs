using JGraph.Core.Drawing;

namespace JGraph.Rendering;

/// <summary>
/// Implemented by plot objects whose values are colored through a <see cref="Colormap"/> (image,
/// surface, and contour plots). The colorbar renderer reads the first visible color-mapped plot in an
/// axes to draw its gradient strip and value scale, the same discovery pattern as
/// <see cref="ILegendItem"/>.
/// </summary>
public interface IColorMapped
{
    /// <summary>The colormap the plot colors its values through.</summary>
    Colormap Colormap { get; }

    /// <summary>The value range mapped onto the colormap (low end, high end).</summary>
    (double Min, double Max) ColorRange { get; }
}
