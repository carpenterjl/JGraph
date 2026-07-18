using JGraph.Core.Primitives;

namespace JGraph.Interaction;

/// <summary>
/// One entry of the plot surface's right-click menu — a UI-free model the host renders with its own
/// menu control. Built by <see cref="InteractionController.BuildContextMenu"/> from the active
/// mode's contributions plus the always-present items.
/// </summary>
public sealed record ContextMenuItem(string Header, Action? Invoke, bool IsChecked = false, bool IsSeparator = false)
{
    /// <summary>A separator line.</summary>
    public static ContextMenuItem Separator { get; } = new(string.Empty, null, IsSeparator: true);
}

/// <summary>
/// Implemented by interaction modes that contribute items to the plot surface's right-click menu
/// (zoom constraints, data-tip deletion). Items are rebuilt on every open, so state (checkmarks,
/// hit-dependent entries) is always current.
/// </summary>
public interface IContextMenuSource
{
    /// <summary>Appends this mode's items for a menu opened at <paramref name="pixel"/>.</summary>
    void AddContextMenuItems(InteractionController controller, Point2D pixel, IList<ContextMenuItem> items);
}
