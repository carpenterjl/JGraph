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

    /// <summary>
    /// Whether this plot is actually coloring anything through its colormap right now. A surface or an
    /// image always is; a 3D scatter or a patch only is when it was given per-point or per-face values,
    /// and until then it must not claim the axes' colorbar or answer for <c>caxis</c>. The default is
    /// true, which is what every plot that has no other mode wants.
    /// </summary>
    bool HasMappedData => true;
}
