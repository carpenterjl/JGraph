using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting;

/// <summary>
/// The seam a figure window uses to hand a script's graphics callbacks their events. The window
/// knows a click happened and which object it landed on; this class knows whether that object was
/// given a callback, and puts the event on <see cref="ScriptEventQueue"/> for the script thread to
/// deliver — nothing here ever runs script code, so it is safe to call from any thread.
/// <para>
/// Where no live session exists the events still queue and simply never fire, which is the old
/// contract: a script that assigns a callback in a batch still finishes, its callback just never
/// runs.
/// </para>
/// </summary>
public static class ScriptGraphicsCallbacks
{
    /// <summary>
    /// Queues a legend's <c>ItemHitFcn</c> for a clicked series. Returns false when that legend was
    /// never given a callback, so the caller can treat the click as an ordinary one.
    /// </summary>
    public static bool NotifyLegendItemHit(AxesModel axes, PlotObject plot)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(plot);
        if (!HasCallback(axes.Legend, GraphicsEventKind.LegendItemHit))
        {
            return false;
        }

        ScriptEventQueue.Enqueue(new GraphicsEvent(
            GraphicsEventKind.LegendItemHit, axes.Legend, Clicked: plot));
        return true;
    }

    /// <summary>
    /// Reports a press over a figure, deciding whose <c>ButtonDownFcn</c> it is. MATLAB's rules:
    /// the hit object takes the click, unless its <c>PickableParts</c> is <c>'none'</c> — then the
    /// click acts as if the object were absent and lands on the axes; no axes (or none willing)
    /// means the figure background. Whatever is decided becomes <c>gco</c> (empty for the figure
    /// background), whether or not anything has a callback — a click is a click.
    /// </summary>
    /// <param name="figure">The figure the press landed in.</param>
    /// <param name="hit">The object the shared hit test resolved, or null for bare canvas.</param>
    /// <param name="axes">The axes under the pixel, if one was.</param>
    /// <param name="dataPoint">The click in that axes' data space, when that means anything.</param>
    /// <param name="button">1 left, 2 middle, 3 right — MATLAB's numbering.</param>
    public static void NotifyButtonDown(
        FigureModel figure, GraphObject? hit, AxesModel? axes, (double X, double Y)? dataPoint, int button)
    {
        ArgumentNullException.ThrowIfNull(figure);

        GraphObject? target = hit;
        if (target is not null
            && JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? picked)
            && picked.PickableParts == "none")
        {
            target = axes is { Selectable: true } && !ReferenceEquals(target, axes) ? axes : null;
        }

        JgsGraphicsCallbackState.RecordClick(target);
        GraphObject owner = target ?? figure;
        if (!HasCallback(owner, GraphicsEventKind.ButtonDown))
        {
            return;
        }

        // A flat axes names one point per pixel, and the caller has it. A 3D one names a line of
        // sight, which the hit test already worked out against the camera, so the click reports where
        // that line meets the plot box instead of a placeholder zero.
        IReadOnlyList<double>? intersection = null;
        if (target is not null && dataPoint is { } point)
        {
            intersection = axes is { Is3D: true }
                ? [axes.CurrentPoint.Front.X, axes.CurrentPoint.Front.Y, axes.CurrentPoint.Front.Z]
                : [point.X, point.Y, 0];
        }
        ScriptEventQueue.Enqueue(new GraphicsEvent(
            GraphicsEventKind.ButtonDown, owner, Clicked: target, Button: button,
            IntersectionPoint: intersection));
    }

    /// <summary>
    /// Reports that a figure's viewport was resized. Coalesced: a drag that fires this a hundred
    /// times queues one callback, in the position of the first — the callback reads the current
    /// size off the figure when it finally runs, which is the only size worth hearing about.
    /// </summary>
    public static void NotifySizeChanged(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (HasCallback(figure, GraphicsEventKind.SizeChanged))
        {
            ScriptEventQueue.Enqueue(
                new GraphicsEvent(GraphicsEventKind.SizeChanged, figure), coalesce: true);
        }
    }

    /// <summary>
    /// Reports a key going down or coming back up over a figure. The character the key produced is
    /// recorded on the figure first, because MATLAB's <c>CurrentCharacter</c> is what the callback
    /// reads when it runs, and both the figure's callback and the window's are queued — with no
    /// uicontrols in this build a figure has the focus whenever its window does, so the two are the
    /// same event told twice, in MATLAB's order.
    /// </summary>
    /// <param name="figure">The figure the key went to.</param>
    /// <param name="pressed">True for a press, false for a release.</param>
    /// <param name="character">The character produced, or empty for a key that produces none.</param>
    /// <param name="keyName">MATLAB's lowercase name for the key itself.</param>
    /// <param name="modifiers">Which of shift, control and alt were held.</param>
    public static void NotifyKey(
        FigureModel figure, bool pressed, string character, string keyName, IReadOnlyList<string> modifiers)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (pressed && !string.IsNullOrEmpty(character))
        {
            figure.CurrentCharacter = character;
        }

        GraphicsEventKind own = pressed ? GraphicsEventKind.KeyPress : GraphicsEventKind.KeyRelease;
        GraphicsEventKind window = pressed ? GraphicsEventKind.WindowKeyPress : GraphicsEventKind.WindowKeyRelease;
        foreach (GraphicsEventKind kind in (ReadOnlySpan<GraphicsEventKind>)[own, window])
        {
            if (HasCallback(figure, kind))
            {
                ScriptEventQueue.Enqueue(new GraphicsEvent(
                    kind, figure, Character: character, KeyName: keyName,
                    Modifiers: modifiers ?? []));
            }
        }
    }

    /// <summary>
    /// Reports a mouse button going down or up anywhere over a figure, whatever it landed on. This
    /// is the window's own account of the click, which runs beside — not instead of — the
    /// <c>ButtonDownFcn</c> of whatever was hit.
    /// </summary>
    public static void NotifyWindowButton(
        FigureModel figure, bool pressed, SelectionKind selection, (double X, double Y) pixel)
    {
        ArgumentNullException.ThrowIfNull(figure);
        figure.CurrentPointPx = new Point2D(pixel.X, pixel.Y);
        if (pressed)
        {
            figure.SelectionType = selection;
        }

        GraphicsEventKind kind = pressed
            ? GraphicsEventKind.WindowButtonDown
            : GraphicsEventKind.WindowButtonUp;
        if (HasCallback(figure, kind))
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(kind, figure, Location: pixel));
        }
    }

    /// <summary>
    /// Reports the pointer moving over a figure. The position is recorded whether or not anyone is
    /// listening, because <c>CurrentPoint</c> is a question a script may ask at any time; only the
    /// callback is conditional, and it coalesces, because a drag is one question about where the
    /// pointer is now rather than a hundred about where it has been.
    /// </summary>
    public static void NotifyWindowMotion(FigureModel figure, (double X, double Y) pixel)
    {
        ArgumentNullException.ThrowIfNull(figure);
        figure.CurrentPointPx = new Point2D(pixel.X, pixel.Y);
        if (HasCallback(figure, GraphicsEventKind.WindowButtonMotion))
        {
            ScriptEventQueue.Enqueue(
                new GraphicsEvent(GraphicsEventKind.WindowButtonMotion, figure, Location: pixel),
                coalesce: true);
        }
    }

    /// <summary>Reports the wheel turning over a figure — the figure's <c>WindowScrollWheelFcn</c>.</summary>
    /// <param name="figure">The figure the pointer was over.</param>
    /// <param name="notches">
    /// How far it turned: positive toward the user, which is MATLAB's sign for a scroll down.
    /// </param>
    public static void NotifyScrollWheel(FigureModel figure, int notches)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (HasCallback(figure, GraphicsEventKind.WindowScrollWheel))
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(
                GraphicsEventKind.WindowScrollWheel, figure, ScrollCount: notches));
        }
    }

    /// <summary>
    /// Where a host says what a figure's window really occupies on screen, chrome included — the
    /// only thing a figure's <c>OuterPosition</c> can honestly be. The WPF application installs one
    /// as it opens windows; a batch run leaves it null, and the outer bounds are then the drawable
    /// area, because headless there is no border to add.
    /// </summary>
    public static Func<FigureModel, Rect2D?>? WindowBoundsProvider { get; set; }

    /// <summary>The <c>uicontextmenu</c> assigned to this object, or null — what a right-click on
    /// it should show in place of the built-in menu.</summary>
    public static ContextMenuModel? ResolveContextMenu(GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry)
            && entry.ContextMenu is ContextMenuModel { BeingDeleted: false } menu
            ? menu
            : null;
    }

    /// <summary>
    /// Reports that a context menu is about to show. The opening callback rides the queue like
    /// everything else — the menu that is opening shows the state the model has now, and a callback
    /// that adjusts entries is in time for the next open. Running it before the menu shows would
    /// mean the window's thread waiting on the interpreter, which nothing is allowed to do.
    /// </summary>
    public static void NotifyContextMenuOpening(
        ContextMenuModel menu, GraphObject? contextObject, (double X, double Y) location)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (HasCallback(menu, GraphicsEventKind.ContextMenuOpening))
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(
                GraphicsEventKind.ContextMenuOpening, menu,
                ContextObject: contextObject, Location: location));
        }
    }

    /// <summary>Reports that a menu entry was picked — the entry's <c>MenuSelectedFcn</c>.</summary>
    public static void NotifyMenuSelected(MenuItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (HasCallback(item, GraphicsEventKind.MenuSelected))
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.MenuSelected, item));
        }
    }

    /// <summary>Whether the object currently has a callback for this kind of event — what an
    /// enqueuer checks so a click on an unscripted object costs nothing.</summary>
    public static bool HasCallback(GraphObject target, GraphicsEventKind kind)
    {
        ArgumentNullException.ThrowIfNull(target);
        return JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry) && kind switch
        {
            GraphicsEventKind.ButtonDown => entry.ButtonDownFcn is not null,
            GraphicsEventKind.LegendItemHit => entry.ItemHitFcn is not null,
            GraphicsEventKind.CloseRequest => entry.CloseRequestFcn is not null,
            GraphicsEventKind.SizeChanged => entry.SizeChangedFcn is not null,
            GraphicsEventKind.MenuSelected => entry.MenuSelectedFcn is not null,
            GraphicsEventKind.ContextMenuOpening => entry.ContextMenuOpeningFcn is not null,
            GraphicsEventKind.ObjectDeleted => entry.DeleteFcn is not null,
            GraphicsEventKind.KeyPress => entry.KeyPressFcn is not null,
            GraphicsEventKind.KeyRelease => entry.KeyReleaseFcn is not null,
            GraphicsEventKind.WindowKeyPress => entry.WindowKeyPressFcn is not null,
            GraphicsEventKind.WindowKeyRelease => entry.WindowKeyReleaseFcn is not null,
            GraphicsEventKind.WindowButtonDown => entry.WindowButtonDownFcn is not null,
            GraphicsEventKind.WindowButtonUp => entry.WindowButtonUpFcn is not null,
            GraphicsEventKind.WindowButtonMotion => entry.WindowButtonMotionFcn is not null,
            GraphicsEventKind.WindowScrollWheel => entry.WindowScrollWheelFcn is not null,
            _ => false,
        };
    }
}
