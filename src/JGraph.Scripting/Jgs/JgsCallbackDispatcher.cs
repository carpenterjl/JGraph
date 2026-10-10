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
    internal const int MaxDrainDepth = 64;

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

    /// <summary>
    /// The interpreter a callback written as text runs in (its base workspace), and the one that
    /// looks up a name leading a cell callback. The session or run sets it once the interpreter
    /// exists; a function-handle callback does not need it.
    /// </summary>
    public Interpreter? Interpreter { get; set; }

    static JgsCallbackDispatcher()
    {
        // Deletions announce themselves from wherever they happen. On the thread running the
        // statement they fire synchronously — MATLAB's DeleteFcn runs at the moment of deletion,
        // before the object is gone — and from any other thread (a window closing, the plot
        // browser) they queue for the script thread like every other interface event.
        GraphObjectLifecycle.Deleting += OnModelDeleting;
    }

    /// <summary>Whether callbacks are nested as deep as they are delivered: a wait started here
    /// would have nothing delivered to it, so the waiting verbs refuse instead of spinning (U4).</summary>
    public bool AtDrainLimit => _drainDepth >= MaxDrainDepth;

    /// <summary>
    /// Marks the running callback as waiting, for as long as the answer is held (U4). MATLAB
    /// documents that a callback inside <c>waitfor</c> — and so inside <c>uiwait</c> or a blocking
    /// dialog — can be interrupted whatever its <c>Interruptible</c> says: without that, a dialog
    /// opened from a callback that asked not to be interrupted could never be answered.
    /// </summary>
    public IDisposable Waiting()
    {
        _running.Add(true);
        return new WaitScope(this);
    }

    private sealed class WaitScope(JgsCallbackDispatcher owner) : IDisposable
    {
        public void Dispose()
        {
            if (owner._running.Count > 0)
            {
                owner._running.RemoveAt(owner._running.Count - 1);
            }
        }
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
        // A figure's and a container's children go in the order Children lists them — front first —
        // which is the order R2025b runs their DeleteFcns in (probe u2_tree).
        IEnumerable<GraphObject> children = JgsGraphicsProperties.DescendantsOf(parent);
        if (parent is IUiContainer)
        {
            children = children.Reverse();
        }

        foreach (GraphObject child in children)
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

        // Custom components' owed updates (U10) - the prompt's return is a drain too - and then what
        // scripts queued for their uihtml pages (U9b), so a page's answer to it can be heard in this drain.
        JgsComponentContainers.RunUpdates(_globals);
        JgsUiHtml.Flush(_globals);

        int budget = ScriptEventQueue.Count;
        for (int i = 0; i < budget; i++)
        {
            StatementToken.ThrowIfCancellationRequested();
            if (yieldRequested?.Invoke() == true || !ScriptEventQueue.TryDequeue(out GraphicsEvent next))
            {
                return;
            }

            // A scrollable grid's bars (U5): where it is scrolled to is the grid's own, and no
            // callback hears of it.
            if (next.Target is UiGridLayoutModel scrolledGrid)
            {
                if (next.UserValue is double[] { Length: 2 } offset && !scrolledGrid.BeingDeleted)
                {
                    scrolledGrid.ScrollX = offset[0];
                    scrolledGrid.ScrollY = offset[1];
                }

                continue;
            }

            // A uifigure component's event (U5): its value is written, and its callback's event
            // data made, before anything decides whether the callback runs.
            if (next.Target is UiComponentModel or UiTabGroupModel or UiToolModel
                && next.Kind is GraphicsEventKind.ComponentUser or GraphicsEventKind.ApplyUserValue)
            {
                GraphicsEvent? owed = JgsUiComponentEvents.Prepare(next);
                IReadOnlyList<GraphicsEvent> after = JgsUiComponentEvents.TakeFollowUps();
                if (owed is not null && next.Kind == GraphicsEventKind.ComponentUser)
                {
                    Deliver(owed);

                    // What is owed after it, in order (U8): a toggle tool's ClickedCallback.
                    foreach (GraphicsEvent then in after)
                    {
                        Deliver(then);
                    }
                }

                continue;
            }

            if (next.Kind == GraphicsEventKind.OverlayAnswered)
            {
                if (JgsUiComponentEvents.Answer(next) is { } closed)
                {
                    Run(closed.Figure, clicked: null, interruptible: true, closed.Callback, closed.EventData, "CloseFcn");
                }

                continue;
            }

            // A user's value is written before anything decides whether its callback runs: the
            // person typed it, and a busy or cancelled callback does not un-type it (U1).
            if (next.UserValue is not null)
            {
                GraphicsEvent? selection = ApplyUserValue(next);
                if (next.Kind == GraphicsEventKind.ApplyUserValue)
                {
                    continue;
                }

                next = next with { UserValue = null };

                // A button group hears of its new selection before the button's own callback runs.
                if (selection is not null && ScriptGraphicsCallbacks.HasCallback(selection.Target, selection.Kind))
                {
                    Deliver(selection);
                }
            }

            // A resized figure settles its containers before anything decides whether its own
            // callback runs: children are rescaled, and each container whose size changed is told.
            if (next is { Kind: GraphicsEventKind.SizeChanged, Target: FigureModel resized }
                && !JgsContainerResize.Apply(resized))
            {
                continue;
            }

            Deliver(next);
        }
    }

    /// <summary>Runs an event's callback if the running one may be interrupted; otherwise its own
    /// object's <c>BusyAction</c> decides whether it waits or is dropped.</summary>
    private void Deliver(GraphicsEvent next)
    {
        bool interruptible = _running.Count == 0 || _running[^1];
        if (!interruptible && !AlwaysInterrupts(next.Kind))
        {
            // The event may not run here. Its own object's BusyAction decides its fate — and a
            // 'queue' event goes to the back, not back to the front, or the drain would spin on
            // it for the rest of its budget.
            if (BusyActionQueues(next.Target))
            {
                ScriptEventQueue.Enqueue(next);
            }

            return;
        }

        Dispatch(next);
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
        if (ScriptUiTrace.Enabled)
        {
            ScriptUiTrace.Write($"dispatch: {graphicsEvent.Kind} on {graphicsEvent.Target.GetType().Name}");
        }

        // An object on its way out tells its ObjectBeingDestroyed listeners first, newest first,
        // and then runs its DeleteFcn (U7, measured in R2025b). The DeleteFcn is read before the
        // listeners run: one of them may finish the deletion - an app's delete deletes its figure -
        // and the callback is owed all the same.
        // A change the window made to a watched object: its PostSet listeners hear it now (open item 56).
        if (graphicsEvent.Kind == GraphicsEventKind.PropertyWatch)
        {
            if (JgsHandleRegistry.TryGetEntry(graphicsEvent.Target, out JgsHandleEntry? watched))
            {
                watched.Watch?.Check();
            }

            return;
        }

        bool resolved = TryResolve(graphicsEvent, out JgsHandleEntry? entry, out JgsValue callback);
        if (graphicsEvent.Kind == GraphicsEventKind.ObjectDeleted
            && JgsHandleRegistry.TryGetEntry(graphicsEvent.Target, out JgsHandleEntry? leaving))
        {
            JgsBuiltins.FireGraphicsDestroyed(leaving);
        }

        if (!resolved || entry is null)
        {
            // An event with no callback still reaches the listeners on it (open item 56).
            if (graphicsEvent.Kind != GraphicsEventKind.ObjectDeleted)
            {
                TellListeners(graphicsEvent, null);
            }

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
        JgsValue eventData = EventDataFor(graphicsEvent, source);
        Run(graphicsEvent.Target, graphicsEvent.Clicked, entry.Interruptible, callback,
            eventData, CallbackNameOf(graphicsEvent.Kind));
        TellListeners(graphicsEvent, eventData);
    }

    /// <summary>
    /// The listeners on the event a callback stands for (open item 56): a button's <c>ButtonPushedFcn</c>
    /// is its <c>ButtonPushed</c> event, a component's <c>ValueChangedFcn</c> its <c>ValueChanged</c>. They
    /// hear the callback's own event data, after the callback (the order is not recorded).
    /// </summary>
    private static void TellListeners(GraphicsEvent graphicsEvent, JgsValue? eventData)
    {
        if (!JgsHandleRegistry.TryGetEntry(graphicsEvent.Target, out JgsHandleEntry? entry) || entry.EventListeners is not { Count: > 0 })
        {
            return;
        }

        string callbackName = graphicsEvent.Kind == GraphicsEventKind.ComponentUser ? graphicsEvent.Action : CallbackNameOf(graphicsEvent.Kind);
        if (callbackName.EndsWith("Fcn", StringComparison.Ordinal))
        {
            JgsBuiltins.FireGraphicsEvent(entry, callbackName[..^3],
                eventData ?? EventDataFor(graphicsEvent, JgsHandleRegistry.For(graphicsEvent.Target)));
        }
    }

    /// <summary>
    /// Puts a user's value into the model, on this thread. A component deleted since the user acted
    /// takes nothing, which is what happened to its value in MATLAB too.
    /// </summary>
    /// <returns>The event a button group is owed when the write changed its selection.</returns>
    private static GraphicsEvent? ApplyUserValue(GraphicsEvent graphicsEvent)
    {
        if (graphicsEvent.Target is not UiControlModel { BeingDeleted: false } control)
        {
            return null;
        }

        GraphicsEvent? selection = null;
        switch (graphicsEvent.UserValue)
        {
            case string text:
                control.Text = UiText.Of(text);
                break;

            // A multi-line edit field's lines, in the shape its String had: a cell stays a cell,
            // and anything else is a character matrix, or a row when one line is left.
            case string[] lines:
                if (control.Text.Form == UiTextForm.Cell)
                {
                    control.Text = new UiText(UiTextForm.Cell, lines);
                }
                else if (lines.Length <= 1)
                {
                    control.Text = UiText.Of(lines.Length == 0 ? string.Empty : lines[0]);
                }
                else
                {
                    int width = lines.Max(static l => l.Length);
                    control.Text = new UiText(UiTextForm.CharMatrix, [.. lines.Select(l => l.PadRight(width))]);
                }

                break;

            // A press that turns on a button its group watches selects it. One that lets the
            // selected toggle button up writes its 0 and leaves the group's selection where it was,
            // which is what R2025b does (window session u3w_clicks): the group hears nothing.
            case double pressed when control.GroupManaged && control.Parent is UiButtonGroupModel group
                                     && UiButtonGroupModel.IsButton(control):
            {
                UiControlModel? old = group.SelectedObject;
                if (pressed == 0)
                {
                    control.Value = UiNumbers.Zero;
                }
                else if (ReferenceEquals(old, control))
                {
                    control.Value = new UiNumbers([1], 1, 1);
                }
                else
                {
                    group.Select(control);
                    selection = new GraphicsEvent(
                        GraphicsEventKind.GroupSelectionChanged, group, Clicked: control, ContextObject: old);
                }

                break;
            }

            case double number:
                control.Value = new UiNumbers([number], 1, 1);
                break;

            case double[] numbers:
                control.Value = new UiNumbers(numbers, numbers.Length == 0 ? 0 : 1, numbers.Length);
                break;
        }

        control.UserWriteSeq = graphicsEvent.UserSeq;
        return selection;
    }

    /// <summary>
    /// Runs a <c>CreateFcn</c> for a just-created object, synchronously — MATLAB's one moment for
    /// it. There is no event to queue: creation happens on the script thread by definition.
    /// </summary>
    public void FireCreateFcn(GraphObject target)
    {
        if (JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry)
            && entry.CreateFcn is { } callback)
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
            && entry.CloseRequestFcn is { } callback)
        {
            Run(figure, clicked: null, entry.Interruptible, callback,
                JgsUiEventData.WindowCloseRequest(JgsHandleRegistry.For(figure)), "CloseRequestFcn");
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
            JgsGraphicsCallbackValues.Invoke(Interpreter, callback, source, eventData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException ex)
        {
            _context.Output.WriteError(ScriptDiagnostic.For(ex, runSourceId: "").ToString());
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
            GraphicsEventKind.ToolbarSelectionChanged => entry.SelectionChangedFcn,
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
            GraphicsEventKind.ControlAction => entry.UiCallback,
            GraphicsEventKind.ComponentKeyPress => entry.KeyPressFcn,
            GraphicsEventKind.ComponentKeyRelease => entry.KeyReleaseFcn,
            GraphicsEventKind.GroupSelectionChanged => entry.SelectionChangedFcn,
            GraphicsEventKind.ComponentUser => entry.NamedCallbacks.GetValueOrDefault(graphicsEvent.Action),
            _ => null,
        };

        // Any of MATLAB's three forms: the slot stores nothing else (U1).
        if (found is null)
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
            // A figure's own ButtonDownFcn is told by a MouseData (u1w_keys), and so is a
            // component's (u3w_clicks); anything drawn, by a Hit.
            case GraphicsEventKind.ButtonDown when graphicsEvent.Target is FigureModel or UiObject:
                return JgsUiEventData.Make(JgsUiEventData.MouseDataClass, source, "ButtonDown");

            case GraphicsEventKind.ButtonDown:
            {
                double[] hit = graphicsEvent.IntersectionPoint is { Count: 3 } point
                    ? [point[0], point[1], point[2]]
                    : [double.NaN, double.NaN, double.NaN];
                return JgsUiEventData.Make(JgsUiEventData.HitClass, source, "Hit", new()
                {
                    ["Button"] = JgsValue.Number(graphicsEvent.Button),
                    ["IntersectionPoint"] = JgsValue.Array(
                        [JgsValue.Number(hit[0]), JgsValue.Number(hit[1]), JgsValue.Number(hit[2])]),
                });
            }

            case GraphicsEventKind.WindowButtonDown:
                return JgsUiEventData.Make(JgsUiEventData.WindowMouseDataClass, source, "WindowMousePress");

            case GraphicsEventKind.WindowButtonUp:
                return JgsUiEventData.Make(JgsUiEventData.WindowMouseDataClass, source, "WindowMouseRelease");

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

            case GraphicsEventKind.ToolbarSelectionChanged:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Source"] = source,
                    ["EventName"] = JgsValue.Str("SelectionChanged"),

                    // Which button was pressed. MATLAB names the previous selection as well; a
                    // toolbar here has no selection to have had, so what is reported is the press.
                    ["Selection"] = graphicsEvent.Clicked is { } pressed
                        ? JgsHandleRegistry.For(pressed)
                        : JgsValue.Array([]),
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

            case GraphicsEventKind.ControlAction:
                return JgsUiEventData.Action(source);

            // Made when the event left the queue, with the value as it then was (U5).
            case GraphicsEventKind.ComponentUser when graphicsEvent.Interim is JgsValue made:
                return made;

            // An axes toolbar button's press (U12), in R2025b's classes and field order.
            case GraphicsEventKind.ComponentUser when graphicsEvent.Target is AxesToolbarButtonModel button:
                return JgsUiEventData.ToolbarButton(button, source, graphicsEvent.Interim is true);

            case GraphicsEventKind.CloseRequest:
                return JgsUiEventData.WindowCloseRequest(source);

            // Measured on R2025b (U1): a DeleteFcn is told by a plain event.EventData.
            case GraphicsEventKind.ObjectDeleted:
                return JgsBuiltins.NewEventData("ObjectBeingDestroyed", source);

            // R2025b's key event data (u1w_keys): a KeyData for the figure's and the window's, a
            // UIClientComponentKeyEvent for a component's; the window's are named WindowKey….
            case GraphicsEventKind.KeyPress:
            case GraphicsEventKind.KeyRelease:
            case GraphicsEventKind.WindowKeyPress:
            case GraphicsEventKind.WindowKeyRelease:
            case GraphicsEventKind.ComponentKeyPress:
            case GraphicsEventKind.ComponentKeyRelease:
                return JgsUiEventData.Make(
                    graphicsEvent.Kind is GraphicsEventKind.ComponentKeyPress or GraphicsEventKind.ComponentKeyRelease
                        ? JgsUiEventData.ComponentKeyEventClass
                        : JgsUiEventData.KeyDataClass,
                    source,
                    graphicsEvent.Kind switch
                    {
                        GraphicsEventKind.WindowKeyPress => "WindowKeyPress",
                        GraphicsEventKind.WindowKeyRelease => "WindowKeyRelease",
                        GraphicsEventKind.KeyPress or GraphicsEventKind.ComponentKeyPress => "KeyPress",
                        _ => "KeyRelease",
                    },
                    new()
                    {
                        ["Character"] = JgsValue.Str(graphicsEvent.Character),
                        ["Modifier"] = JgsValue.Cell(
                            (graphicsEvent.Modifiers ?? []).Select(JgsValue.Str).ToArray()),
                        ["Key"] = JgsValue.Str(graphicsEvent.KeyName),
                    });

            case GraphicsEventKind.WindowScrollWheel:
                return JgsUiEventData.Make(JgsUiEventData.ScrollWheelDataClass, source, "WindowScrollWheel", new()
                {
                    ["VerticalScrollCount"] = JgsValue.Number(graphicsEvent.ScrollCount),
                    ["VerticalScrollAmount"] = JgsValue.Number(3),
                });

            // R2025b's SelectionChangedData: the button that was selected and the one that is.
            case GraphicsEventKind.GroupSelectionChanged:
                return JgsUiEventData.Make(JgsUiEventData.SelectionChangedDataClass, source, "SelectionChanged", new()
                {
                    ["OldValue"] = graphicsEvent.ContextObject is { BeingDeleted: false } old
                        ? JgsHandleRegistry.For(old)
                        : JgsMatrix.FromColumnMajor([], 0, 0),
                    ["NewValue"] = graphicsEvent.Clicked is { } picked
                        ? JgsHandleRegistry.For(picked)
                        : JgsMatrix.FromColumnMajor([], 0, 0),
                });

            // R2025b's SizeChangedData for a figure's and a container's alike (probe u2w_resize).
            case GraphicsEventKind.SizeChanged:
                return JgsUiEventData.Make(JgsUiEventData.SizeChangedDataClass, source, "SizeChanged");

            default:
                // The pointer's motion: what R2025b hands it is not recorded yet, and the callback
                // reads CurrentPoint and SelectionType meanwhile.
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
        GraphicsEventKind.ToolbarSelectionChanged => "SelectionChangedFcn",
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
        GraphicsEventKind.ControlAction => "Callback",
        GraphicsEventKind.ComponentKeyPress => "KeyPressFcn",
        GraphicsEventKind.ComponentKeyRelease => "KeyReleaseFcn",
        GraphicsEventKind.GroupSelectionChanged => "SelectionChangedFcn",
        GraphicsEventKind.ComponentUser => "component",
        _ => "callback",
    };
}
