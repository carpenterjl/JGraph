using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>What happened, out in the interface, that a script callback may want to hear about.</summary>
public enum GraphicsEventKind
{
    /// <summary>An object (or the figure background) was clicked — MATLAB's <c>ButtonDownFcn</c>.</summary>
    ButtonDown,

    /// <summary>A legend row was clicked — the legend's <c>ItemHitFcn</c>.</summary>
    LegendItemHit,

    /// <summary>The user asked a figure window to close — the figure's <c>CloseRequestFcn</c>.</summary>
    CloseRequest,

    /// <summary>A figure window was resized — the figure's <c>SizeChangedFcn</c>.</summary>
    SizeChanged,

    /// <summary>A context-menu item was picked — the menu item's <c>MenuSelectedFcn</c>.</summary>
    MenuSelected,

    /// <summary>A context menu is about to open — the menu's <c>ContextMenuOpeningFcn</c>.</summary>
    ContextMenuOpening,

    /// <summary>A button on an axes toolbar was pressed — the toolbar's <c>SelectionChangedFcn</c>.</summary>
    ToolbarSelectionChanged,

    /// <summary>Something in the interface deleted an object — its <c>DeleteFcn</c>, delivered rather
    /// than run in place, because the deletion happened on a thread the interpreter must not run on.</summary>
    ObjectDeleted,

    /// <summary>A key went down over a figure — the figure's <c>KeyPressFcn</c>.</summary>
    KeyPress,

    /// <summary>A key came back up — the figure's <c>KeyReleaseFcn</c>.</summary>
    KeyRelease,

    /// <summary>The same press, told to the window rather than the figure — <c>WindowKeyPressFcn</c>.
    /// With no uicontrols in this build the two always fire together, in MATLAB's order.</summary>
    WindowKeyPress,

    /// <summary>The same release, told to the window — <c>WindowKeyReleaseFcn</c>.</summary>
    WindowKeyRelease,

    /// <summary>A mouse button went down anywhere in a figure — <c>WindowButtonDownFcn</c>.</summary>
    WindowButtonDown,

    /// <summary>A mouse button came back up — <c>WindowButtonUpFcn</c>.</summary>
    WindowButtonUp,

    /// <summary>The pointer moved over a figure — <c>WindowButtonMotionFcn</c>, coalesced, because a
    /// drag across the window is one question about where the pointer is now.</summary>
    WindowButtonMotion,

    /// <summary>The wheel turned over a figure — <c>WindowScrollWheelFcn</c>.</summary>
    WindowScrollWheel,
}

/// <summary>
/// One event waiting to be delivered to a script callback. The event names <em>what happened</em>;
/// which function runs is looked up from the target when the event is dispatched, not when it is
/// queued, so reassigning a callback between the click and the dispatch behaves the way assigning a
/// handler always does — the current one runs.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Target">The object whose callback answers this event.</param>
/// <param name="Clicked">The object the user actually hit, when the event is a click — what
/// <c>gco</c> should answer. Null for events that are not clicks.</param>
/// <param name="Button">The mouse button for a click: 1 left, 2 middle, 3 right; 0 otherwise.</param>
/// <param name="IntersectionPoint">Where a click landed in data coordinates, when that is known;
/// null stands for MATLAB's NaN row (a click with no data-space meaning, like the figure background).</param>
/// <param name="ContextObject">For a context-menu opening: the object that was right-clicked.</param>
/// <param name="Location">For a context-menu opening, or a window mouse event: where, in figure pixels.</param>
/// <param name="Character">For a key event: the character the key produced, or empty for a key that
/// produces none (an arrow, a function key).</param>
/// <param name="KeyName">For a key event: MATLAB's lowercase name for the key itself.</param>
/// <param name="Modifiers">For a key event: which of shift, control and alt were held, in that
/// order, lowercase, as MATLAB's cell of words.</param>
/// <param name="ScrollCount">For a wheel event: how many notches, negative for a turn away from the
/// user, in MATLAB's sign convention.</param>
public sealed record GraphicsEvent(
    GraphicsEventKind Kind,
    GraphObject Target,
    GraphObject? Clicked = null,
    int Button = 0,
    IReadOnlyList<double>? IntersectionPoint = null,
    GraphObject? ContextObject = null,
    (double X, double Y)? Location = null,
    string Character = "",
    string KeyName = "",
    IReadOnlyList<string>? Modifiers = null,
    int ScrollCount = 0);

/// <summary>
/// The queue between the interface and the interpreter. Interface threads only ever put events in;
/// the script thread takes them out at its own safe points (<c>drawnow</c>, <c>pause</c>,
/// <c>waitfor</c>, and between statements). That one rule is what lets a callback run script code
/// without two threads ever sharing the interpreter.
/// <para>
/// A host that can run callbacks announces itself with <see cref="InstallPump"/>; where none is
/// installed (a headless batch), events simply never arrive and <c>waitfor</c> keeps its
/// return-at-once contract, so a batch script never hangs waiting on a person who is not there.
/// </para>
/// </summary>
public static class ScriptEventQueue
{
    private static readonly object Gate = new();
    private static readonly List<GraphicsEvent> Events = new();
    private static Action? _pump;

    /// <summary>Whether a host that can deliver events is present. <c>waitfor</c> reads this to
    /// decide between really waiting and its headless return-at-once contract.</summary>
    public static bool PumpInstalled => _pump is not null;

    /// <summary>
    /// Announces (or, with null, withdraws) a host that delivers queued events. The pump is called,
    /// on whatever thread queued the event, each time one arrives; the host's job is to start an
    /// idle drain when no statement is running — a drain already running just finds the event.
    /// </summary>
    public static void InstallPump(Action? pump) => _pump = pump;

    /// <summary>How many events are waiting.</summary>
    public static int Count
    {
        get { lock (Gate) { return Events.Count; } }
    }

    /// <summary>
    /// Queues an event. With <paramref name="coalesce"/>, an event of the same kind for the same
    /// target is replaced in place — earliest position, newest data — so a resize drag queues one
    /// callback rather than a hundred, without reordering it past a click that came after it.
    /// </summary>
    public static void Enqueue(GraphicsEvent graphicsEvent, bool coalesce = false)
    {
        ArgumentNullException.ThrowIfNull(graphicsEvent);
        lock (Gate)
        {
            bool replaced = false;
            if (coalesce)
            {
                for (int i = 0; i < Events.Count && !replaced; i++)
                {
                    if (Events[i].Kind == graphicsEvent.Kind
                        && ReferenceEquals(Events[i].Target, graphicsEvent.Target))
                    {
                        Events[i] = graphicsEvent;
                        replaced = true;
                    }
                }
            }

            if (!replaced)
            {
                Events.Add(graphicsEvent);
            }
        }

        _pump?.Invoke();
    }

    /// <summary>Takes the oldest event, or answers false when the queue is empty.</summary>
    internal static bool TryDequeue(out GraphicsEvent graphicsEvent)
    {
        lock (Gate)
        {
            if (Events.Count == 0)
            {
                graphicsEvent = null!;
                return false;
            }

            graphicsEvent = Events[0];
            Events.RemoveAt(0);
            return true;
        }
    }

    /// <summary>
    /// Empties the queue and hands back what was in it. Stop uses this: cancelled work should not
    /// leave a click waiting to fire into the next statement, but a close request in the pile is a
    /// person asking for a window to go away, and the caller owes it an answer.
    /// </summary>
    public static IReadOnlyList<GraphicsEvent> Flush()
    {
        lock (Gate)
        {
            var taken = Events.ToArray();
            Events.Clear();
            return taken;
        }
    }

    /// <summary>Whether an undelivered event of this kind for this target is already waiting —
    /// how a second click on a close box is told apart from the first.</summary>
    public static bool IsPending(GraphicsEventKind kind, GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (Gate)
        {
            foreach (GraphicsEvent pending in Events)
            {
                if (pending.Kind == kind && ReferenceEquals(pending.Target, target))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
