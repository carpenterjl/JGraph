namespace JGraph.Objects;

/// <summary>
/// Per-frame scratch buffers for plot objects that rebuild geometry on every render.
/// </summary>
/// <remarks>
/// Projected points, pixel-space contour vertices and the like change with the camera and the axes
/// ranges, none of which raise a plot's own <c>Invalidated</c>, so there is no hook that could
/// invalidate them correctly and they are never treated as valid across frames. Keeping the arrays
/// is still worth it — refilling one costs nothing and reallocating megabytes per frame of a rotate
/// drag costs a great deal. Rendering the same model from two threads is already unsafe, but shared
/// scratch would turn that into torn geometry rather than a stale bound, so a caller that loses the
/// race takes freshly allocated arrays for that pass instead.
/// </remarks>
internal static class RenderScratch
{
    /// <summary>Hands back the cached buffer, or a fresh one when another render owns it.</summary>
    internal static T[] Rent<T>(ref T[]? cached, int length, bool exclusive)
    {
        if (!exclusive)
        {
            return new T[length];
        }

        if (cached is null || cached.Length < length)
        {
            cached = new T[length];
        }

        return cached;
    }
}
