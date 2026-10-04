using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a change of size tells a script (app-building plan, U2), as R2025b does it with a window on
/// screen (probe <c>u2w_resize</c>):
/// <list type="bullet">
/// <item>a figure whose size changed runs its <c>SizeChangedFcn</c>, once per change — not for a
/// move, and not for the same size again;</item>
/// <item>a container whose pixel size changed runs its own: because its figure was resized and it
/// is placed in normalized units, or because a script wrote its <c>Position</c> — and once when it
/// is first shown;</item>
/// <item><c>AutoResizeChildren</c> silences the <c>SizeChangedFcn</c> of the figure or container it
/// is on. Children placed in normalized units follow by themselves; children placed in absolute
/// units stay where they are (R2025b's own reflow of them when a container shrinks is not
/// reproduced — see ADR 0199).</item>
/// </list>
/// Headless nothing here runs, as in R2025b, where a figure with no window calls nothing: the
/// figure's event comes only from a window, and the containers are looked at only where a host is
/// delivering frames to one.
/// </summary>
internal static class JgsContainerResize
{
    /// <summary>
    /// Settles a figure after its window was resized and answers whether the figure's own
    /// <c>SizeChangedFcn</c> should run: only when its size really changed and
    /// <c>AutoResizeChildren</c> is off.
    /// </summary>
    public static bool Apply(FigureModel figure)
    {
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        (double Width, double Height)? before = entry.LastSize;
        (double Width, double Height) now = (figure.Size.Width, figure.Size.Height);
        entry.LastSize = now;
        if (before == now)
        {
            return false;
        }

        Settle(figure);
        return !figure.AutoResizeChildren && entry.SizeChangedFcn is not null;
    }

    /// <summary>
    /// Queues a <c>SizeChangedFcn</c> for each container of a shown figure whose pixel size is not
    /// the size it was last seen at, outermost first. Called when the figure is resized and at each
    /// flush of a figure a window is showing, which is where a script's own <c>Position</c> write
    /// to a container, and a container's first appearance, are caught.
    /// </summary>
    public static void Settle(FigureModel figure)
    {
        if (figure.Visible)
        {
            Settle(figure.Components);
        }
    }

    private static void Settle(IReadOnlyList<UiObject> components)
    {
        foreach (UiObject component in components.ToArray())
        {
            if (component is not UiContainerModel container)
            {
                continue;
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(container);
            Rect2D box = container.PixelPosition();
            (double Width, double Height) now = (box.Width, box.Height);
            if (entry.LastSize != now)
            {
                entry.LastSize = now;
                if (!container.AutoResizeChildren && entry.SizeChangedFcn is not null)
                {
                    ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.SizeChanged, container), coalesce: true);
                }
            }

            Settle(container.Components);
        }
    }
}
