namespace JGraph.Core.Model;

/// <summary>
/// Describes how much work a change requires downstream. Values are ordered by increasing cost so
/// that a coalescing consumer can take the maximum of several invalidations.
/// </summary>
public enum InvalidationKind
{
    /// <summary>No visible change.</summary>
    None = 0,

    /// <summary>Appearance changed (color, line width, ...); a repaint is sufficient.</summary>
    Render = 1,

    /// <summary>Geometry changed (axis range, size, tick set); layout must be recomputed before repaint.</summary>
    Layout = 2,

    /// <summary>Underlying data changed; data bounds and auto-scaling must be recomputed.</summary>
    Data = 3,

    /// <summary>Objects were added to or removed from the tree; the scene must be rebuilt.</summary>
    Structure = 4,

    /// <summary>
    /// An app-building component changed (app-building plan, U1). Nothing is drawn by Skia, so the
    /// figure canvas does not repaint; the figure marks its components dirty for the next flush.
    /// </summary>
    Ui = 5,
}
