using System.Diagnostics.CodeAnalysis;
using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Delivers queued graphics events to script callbacks, always on the script thread. There are two
/// ways in: a running statement reaches a drain point (<c>drawnow</c>, <c>pause</c>,
/// <c>waitfor</c>, <c>getframe</c>) and calls <see cref="Drain"/> in place, or the session is idle
/// and the host starts a pump run that does the same thing with full statement ceremony around it.
/// Either way the interpreter re-enters itself on one thread, which it was built to do — the one
/// thing this class never does is run script code on the thread the event came from.
/// <para>
/// MATLAB's scheduling words are honoured here and nowhere else. Whether an event may run at a
/// drain point depends on the <em>running</em> callback's <c>Interruptible</c>; what happens to an
/// event that may not run depends on its own object's <c>BusyAction</c> — <c>'queue'</c> waits,
/// <c>'cancel'</c> is discarded at that moment. Both are read when the question is asked, on this
/// thread, never snapshotted on the thread that queued the event: the queue-side thread cannot know
/// what will be running when the event is finally considered. Close requests, resizes and
/// deletions run regardless of <c>Interruptible</c>, as MATLAB documents.
/// </para>
/// </summary>
internal sealed class JgsCallbackDispatcher
{
    /// <summary>Nested drains stop at this depth and leave events queued — a resize storm inside a
    /// waitfor inside a callback degrades to waiting, not to a blown stack.</summary>
    private const int MaxDrainDepth = 64;

    private readonly JGraphScriptGlobals _globals;
    private readonly ScriptContext _context;

    /// <summary>The Interruptible of each callback currently on the call stack, innermost last.
    /// Only the script thread touches it. Empty means a plain statement (or nothing) is running,
    /// and a plain statement counts as interruptible.</summary>
    private readonly List<bool> _running = new();

    private int _drainDepth;

    public JgsCallbackDispatcher(JGraphScriptGlobals globals, ScriptContext context)
    {
        _globals = globals;
        _context = context;
    }

    static JgsCallbackDispatcher()
    {
        // Deletions announce themselves from wherever they happen. On the thread running the
        // statement they fire synchronously — MATLAB's DeleteFcn runs at the moment of deletion,
        // before the object is gone — and from any other thread (a window closing, the plot
        // browser) they queue for the script thread like every other interface event.
        GraphObjectLifecycle.Deleting += OnModelDeleting;
    }

    /// <summary>The live dispatcher, installed by the session that owns the interpreter. Builtins
    /// reach their drain point through this; null (a one-shot or batch run) makes draining a no-op.</summary>
    public static JgsCallbackDispatcher? Current { get; private set; }

    public static void Install(JgsCallbackDispatcher? dispatcher) => Current = dispatcher;

    /// <summary>The cancellation token of the statement currently running, so <c>pause</c> and
    /// <c>waitfor</c> wake on Stop. The session sets it as each statement begins.</summary>
    public CancellationToken StatementToken { get; set; } = CancellationToken.None;

    /// <summary>The managed id of the thread running the current statement or pump, or null between
    /// runs. This is how a deletion knows whether it happened <em>inside</em> script execution
    /// (fire the DeleteFcn now, nested) or outside it (queue it for the script thread).</summary>
    public int? StatementThreadId { get; set; }

    private static void OnModelDeleting(GraphObject target)
    {
        if (Current is not { } dispatcher)
        {
            return;
        }

        // Parent-first, exactly once each: the deleted object's own callback runs while its
        // children are still reachable, then each descendant's. TryBeginDeleting marks every
        // descendant, so when their collections empty afterwards nothing announces them again.
        dispatcher.DeliverDeletion(target);
        DeliverDescendantDeletions(dispatcher, target);
    }

    private static void DeliverDescendantDeletions(JgsCallbackDispatcher dispatcher, GraphObject parent)
    {
        foreach (GraphObject child in JgsGraphicsProperties.DescendantsOf(parent))
        {
            if (GraphObjectLifecycle.TryBeginDeleting(child))
            {
                dispatcher.DeliverDeletion(child);
                DeliverDescendantDeletions(dispatcher, child);
            }
        }
    }

    private void DeliverDeletion(GraphObject target)
    {
        if (!ScriptGraphicsCallbacks.HasCallback(target, GraphicsEventKind.ObjectDeleted))
        {
            return;
        }

        var deleted = new GraphicsEvent(GraphicsEventKind.ObjectDeleted, target);
        if (StatementThreadId == Environment.CurrentManagedThreadId)
        {
            Dispatch(deleted);
        }
        else
        {
            ScriptEventQueue.Enqueue(deleted);
        }
    }

    /// <summary>
    /// Delivers what the queue holds, from the script thread, honouring the scheduling words. Takes
    /// at most the events present when it starts, so a callback that queues more work yields back to
    /// its caller instead of chasing its own tail; the next drain point picks the new events up.
    /// </summary>
    /// <param name="yieldRequested">Asked between events; answering true ends the drain early with
    /// the rest left queued — how a pump run steps aside for the user's own statement.</param>
    public void Drain(Func<bool>? yieldRequested = null)
    {
        if (_drainDepth >= MaxDrainDepth)
        {
            return;
        }

        int budget = ScriptEventQueue.Count;
        for (int i = 0; i < budget; i++)
        {
            StatementToken.ThrowIfCancellationRequested();
            if (yieldRequested?.Invoke() == true || !ScriptEventQueue.TryDequeue(out GraphicsEvent next))
            {
                return;
            }

            bool interruptible = _running.Count == 0 || _running[^1];
            if (!interruptible && !AlwaysInterrupts(next.Kind))
            {
                // The event may not run here. Its own object's BusyAction decides its fate — and a
                // 'queue' event goes to the back, not back to the front, or this loop would spin on
                // it for the rest of the budget.
                if (BusyActionQueues(next.Target))
                {
                    ScriptEventQueue.Enqueue(next);
                }

                continue;
            }

            Dispatch(next);
        }
    }

    /// <summary>Whether any event is waiting — what an idle host checks before starting a pump run.</summary>
    public static bool HasPendingEvents => ScriptEventQueue.Count > 0;

    /// <summary>
    /// Runs one event's callback, nested in whatever is already running. A callback that dies takes
    /// only itself: the error is reported the way a failed statement is, and the statement (or drain)
    /// that was interrupted carries on. Stop is the exception — cancellation always unwinds.
    /// </summary>
    private void Dispatch(GraphicsEvent graphicsEvent)
    {
        if (!TryResolve(graphicsEvent, out JgsHandleEntry? entry, out JgsValue callback))
        {
            // A close request whose callback vanished between the click and its delivery still
            // means the window should close — the cancelled close was standing in for this moment.
            if (graphicsEvent is { Kind: GraphicsEventKind.CloseRequest, Target: FigureModel figure })
            {
                int number = JG.GetFigureNumber(figure);
                if (number > 0)
                {
                    _globals.CloseFigure(number);
                }
            }

            return;
        }

        JgsValue source = JgsHandleRegistry.For(graphicsEvent.Target);
        Run(graphicsEvent.Target, graphicsEvent.Clicked, entry.Interruptible, callback,
            EventDataFor(graphicsEvent, source), CallbackNameOf(graphicsEvent.Kind));
    }

    /// <summary>
    /// Runs a <c>CreateFcn</c> for a just-created object, synchronously — MATLAB's one moment for
    /// it. There is no event to queue: creation happens on the script thread by definition.
    /// </summary>
    public void FireCreateFcn(GraphObject target)
    {
        if (JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry)
            && entry.CreateFcn is { Type: JgsType.Function } callback)
        {
            Run(target, clicked: null, entry.Interruptible, callback, JgsValue.Array([]), "CreateFcn");
        }
    }

    /// <summary>
    /// Runs a figure's <c>CloseRequestFcn</c> in place of the close — <c>close(fig)</c>'s manner of
    /// asking. The callback decides: <c>closereq</c> or <c>delete</c> closes, returning without
    /// either vetoes, and an error vetoes too (the figure stays, as MATLAB documents).
    /// </summary>
    public void FireCloseRequest(FigureModel figure)
    {
        if (JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry)
            && entry.CloseRequestFcn is { Type: JgsType.Function } callback)
        {
            Run(figure, clicked: null, entry.Interruptible, callback, JgsValue.Array([]), "CloseRequestFcn");
        }
    }

    /// <summary>The shared callback ceremony: gcbo scoped to the target, MATLAB's two arguments,
    /// errors reported the way a failed statement is — a dying callback takes only itself.</summary>
    private void Run(
        GraphObject target, GraphObject? clicked, bool interruptible,
        JgsValue callback, JgsValue eventData, string callbackName)
    {
        JgsValue source = JgsHandleRegistry.For(target);
        _running.Add(interruptible);
        _drainDepth++;
        using IDisposable scope = JgsGraphicsCallbackState.Enter(target, clicked);
        try
        {
            callback.AsCallable.Call([source, eventData], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException ex)
        {
            _context.Output.WriteError(
                new ScriptDiagnostic(ex.Line, ex.Column, ex.Message, IsError: true).ToString());
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is null)
        {
            _context.Output.WriteError($"A {callbackName} callback failed: {ex.Message}");
        }
        finally
        {
            _drainDepth--;
            _running.RemoveAt(_running.Count - 1);
        }
    }

    /// <summary>
    /// The callback an event should run, read from its object <em>now</em> — reassigning a callback
    /// between the click and the dispatch behaves the way reassigning a handler always does. An
    /// object that has since been deleted, or was never given this callback, answers nothing and the
    /// event is quietly dropped, which is what happened to the click in MATLAB too.
    /// </summary>
    private static bool TryResolve(
        GraphicsEvent graphicsEvent, [NotNullWhen(true)] out JgsHandleEntry? entry, out JgsValue callback)
    {
        callback = default!;
        if (!JgsHandleRegistry.TryGetEntry(graphicsEvent.Target, out entry))
        {
            return false;
        }

        JgsValue? found = graphicsEvent.Kind switch
        {
            GraphicsEventKind.ButtonDown => entry.ButtonDownFcn,
            GraphicsEventKind.LegendItemHit => entry.ItemHitFcn,
            GraphicsEventKind.CloseRequest => entry.CloseRequestFcn,
            GraphicsEventKind.SizeChanged => entry.SizeChangedFcn,
            GraphicsEventKind.MenuSelected => entry.MenuSelectedFcn,
            GraphicsEventKind.ContextMenuOpening => entry.ContextMenuOpeningFcn,
            GraphicsEventKind.ObjectDeleted => entry.DeleteFcn,
            GraphicsEventKind.KeyPress => entry.KeyPressFcn,
            GraphicsEventKind.KeyRelease => entry.KeyReleaseFcn,
            GraphicsEventKind.WindowKeyPress => entry.WindowKeyPressFcn,
            GraphicsEventKind.WindowKeyRelease => entry.WindowKeyReleaseFcn,
            GraphicsEventKind.WindowButtonDown => entry.WindowButtonDownFcn,
            GraphicsEventKind.WindowButtonUp => entry.WindowButtonUpFcn,
            GraphicsEventKind.WindowButtonMotion => entry.WindowButtonMotionFcn,
            GraphicsEventKind.WindowScrollWheel => entry.WindowScrollWheelFcn,
            _ => null,
        };

        if (found is not { Type: JgsType.Function })
        {
            return false;
        }

        callback = found;
        return true;
    }

    /// <summary>MATLAB's documented exceptions: a deletion, a close request or a resize interrupts
    /// even a callback that asked not to be interrupted.</summary>
    private static bool AlwaysInterrupts(GraphicsEventKind kind) => kind is
        GraphicsEventKind.ObjectDeleted or GraphicsEventKind.CloseRequest or GraphicsEventKind.SizeChanged;

    private static bool BusyActionQueues(GraphObject target) =>
        !JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry) || entry.BusyActionQueues;

    /// <summary>The second argument the callback receives — each kind's documented shape.</summary>
    private static JgsValue EventDataFor(GraphicsEvent graphicsEvent, JgsValue source)
    {
        switch (graphicsEvent.Kind)
        {
            case GraphicsEventKind.ButtonDown:
            {
                double[] hit = graphicsEvent.IntersectionPoint is { Count: 3 } point
                    ? [point[0], point[1], point[2]]
                    : [double.NaN, double.NaN, double.NaN];
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str("Hit"),
                    ["Button"] = JgsValue.Number(graphicsEvent.Button),
                    ["IntersectionPoint"] = JgsValue.Array(
                        [JgsValue.Number(hit[0]), JgsValue.Number(hit[1]), JgsValue.Number(hit[2])]),
                });
            }

            case GraphicsEventKind.LegendItemHit:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Peer"] = graphicsEvent.Clicked is { } peer
                        ? JgsHandleRegistry.For(peer)
                        : JgsValue.Array([]),
                });

            case GraphicsEventKind.MenuSelected:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str("Action"),
                });

            case GraphicsEventKind.ContextMenuOpening:
            {
                (double x, double y) = graphicsEvent.Location ?? (double.NaN, double.NaN);
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str("ContextMenuOpening"),
                    ["ContextObject"] = graphicsEvent.ContextObject is { } context
                        ? JgsHandleRegistry.For(context)
                        : JgsValue.Array([]),
                    ["Location"] = JgsValue.Array([JgsValue.Number(x), JgsValue.Number(y)]),
                });
            }

            case GraphicsEventKind.KeyPress:
            case GraphicsEventKind.KeyRelease:
            case GraphicsEventKind.WindowKeyPress:
            case GraphicsEventKind.WindowKeyRelease:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str(
                        graphicsEvent.Kind is GraphicsEventKind.KeyPress or GraphicsEventKind.WindowKeyPress
                            ? "KeyPress"
                            : "KeyRelease"),
                    ["Character"] = JgsValue.Str(graphicsEvent.Character),
                    ["Key"] = JgsValue.Str(graphicsEvent.KeyName),
                    ["Modifier"] = JgsValue.Cell(
                        (graphicsEvent.Modifiers ?? []).Select(JgsValue.Str).ToArray()),
                });

            case GraphicsEventKind.WindowScrollWheel:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str("WindowScrollWheel"),
                    ["VerticalScrollCount"] = JgsValue.Number(graphicsEvent.ScrollCount),
                    ["VerticalScrollAmount"] = JgsValue.Number(3),
                });

            default:
                // CloseRequest, SizeChanged, ObjectDeleted and the window button events carry no
                // event data in MATLAB — the callback reads CurrentPoint and SelectionType instead.
                return JgsValue.Array([]);
        }
    }

    private static string CallbackNameOf(GraphicsEventKind kind) => kind switch
    {
        GraphicsEventKind.ButtonDown => "ButtonDownFcn",
        GraphicsEventKind.LegendItemHit => "ItemHitFcn",
        GraphicsEventKind.CloseRequest => "CloseRequestFcn",
        GraphicsEventKind.SizeChanged => "SizeChangedFcn",
        GraphicsEventKind.MenuSelected => "MenuSelectedFcn",
        GraphicsEventKind.ContextMenuOpening => "ContextMenuOpeningFcn",
        GraphicsEventKind.ObjectDeleted => "DeleteFcn",
        GraphicsEventKind.KeyPress => "KeyPressFcn",
        GraphicsEventKind.KeyRelease => "KeyReleaseFcn",
        GraphicsEventKind.WindowKeyPress => "WindowKeyPressFcn",
        GraphicsEventKind.WindowKeyRelease => "WindowKeyReleaseFcn",
        GraphicsEventKind.WindowButtonDown => "WindowButtonDownFcn",
        GraphicsEventKind.WindowButtonUp => "WindowButtonUpFcn",
        GraphicsEventKind.WindowButtonMotion => "WindowButtonMotionFcn",
        GraphicsEventKind.WindowScrollWheel => "WindowScrollWheelFcn",
        _ => "callback",
    };
}
