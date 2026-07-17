namespace JGraph.Rendering;

/// <summary>
/// Implemented by plot objects that draw themselves. The figure renderer walks the object tree and
/// invokes this for each plot object, having already established the clip, coordinate mapper, and
/// resolved series color in the render state. Implementations must depend only on the abstract
/// <see cref="IRenderContext"/>, never on a concrete backend or on UI/interaction state.
/// </summary>
public interface IDrawable
{
    /// <summary>Draws this object onto <paramref name="context"/> using <paramref name="state"/>.</summary>
    void Render(IRenderContext context, RenderState state);
}
