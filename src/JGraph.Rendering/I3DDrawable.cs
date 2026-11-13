using JGraph.Maths.Transforms;

namespace JGraph.Rendering;

/// <summary>
/// Implemented by plot objects that draw themselves in a 3D axes. The figure renderer builds one
/// <see cref="Projection3D"/> per 3D axes (from its X/Y/Z ranges and camera angles) and passes it to
/// every 3D plot, so all plots in the axes share the same camera. 2D-only plots are skipped in a 3D
/// axes, and 3D-only plots are skipped in a 2D axes.
/// </summary>
public interface I3DDrawable
{
    /// <summary>Draws this object through <paramref name="projection"/> onto <paramref name="context"/>.</summary>
    void Render3D(IRenderContext context, Projection3D projection, RenderState state);

    /// <summary>
    /// Whether a 2D axes draws this object too, seen from straight above through a projection that
    /// fills the plot area: R2025b's <c>pcolor</c> is a surface in a flat axes, and so is a
    /// <c>surf</c> after <c>view(2)</c> (open item 167). False unless the object says so.
    /// </summary>
    bool DrawsInFlatAxes => false;
}
