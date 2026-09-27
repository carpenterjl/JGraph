using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Listeners on a .NET object's events (interop plan, stage 5, ADR 0178): <c>addlistener</c> and
/// <c>listener</c> on a .NET event or on <c>ObjectBeingDestroyed</c>, and the firing of both.
/// </summary>
/// <remarks>
/// A .NET listener is the <c>event.listener</c> a classdef source gets — the same struct, the same
/// <see cref="JgsListener"/>, the same <c>Enabled</c>, <c>Recursive</c>, <c>delete</c> and lifetimes
/// (an <c>addlistener</c> one survives <c>clear lh</c>, a <c>listener</c> one does not) — held by a
/// <see cref="NetEventSubscription"/> instead of the object's own list. Its callback receives the
/// sender and the event's arguments as .NET values of the declared types (<c>evt.Value</c> of a
/// <c>CustomArgs</c>), newest listener first, and a failing callback is the same warning (R2025b,
/// net_events_delegates). A queued event reaching a listener deleted or disabled since is dropped;
/// R2025b crashes there (probe5d, probe5e), a divergence.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>addlistener(netObj, 'Event', @cb)</c>: checked in R2025b's words (probe5a) — a deleted
    /// object, a fourth argument, a callback or event name of the wrong kind, an event the type does
    /// not have, a delegate of the wrong shape — then added to the event's subscription.
    /// </summary>
    private static JgsValue AddNetListener(
        string verb, JGraphScriptGlobals host, NetObject net, JgsValue sourceValue, IReadOnlyList<JgsValue> args, int line, int col)
    {
        net.Live(line, col);
        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        JgsValue callback = args[2];
        if (callback.Type != JgsType.Function || !IsTextScalar(args[1]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidInputArgumentTypeSizeOrValue",
                $"Invalid input argument for function '{verb}'.");
        }

        string eventName = TextOf(args[1]);
        NetEventSubscription subscription = NetEventSubscription.For(net, eventName, line, col)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:class:invalidEvent",
                $"Event '{eventName}' is not defined for class '{net.ClassName}'.");

        JgsValue listener = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [EventSourceField] = JgsValue.Cell([sourceValue]),
            [EventNameField] = JgsValue.Str(eventName),
            ["Callback"] = callback,
            ["Enabled"] = JgsValue.Bool(true),
            ["Recursive"] = JgsValue.Bool(false),
        });
        listener.SetClassName(ListenerClassName);
        var state = new JgsListener
        {
            Value = listener,
            NetEvent = subscription,
            SourceValue = sourceValue,
            Host = host,
            EventName = eventName,
            Callback = callback,
            FromListenerFunction = verb == "listener",
        };

        // As a classdef source's listener (V10, #107): an addlistener one is not counted, so it lives
        // with the source; a listener one is, so its last handle's going ends it.
        JgsLifetime.Unscan(listener);
        listener.AsStructArray.External = true;
        listener.AsStructArray.Scanned = state.FromListenerFunction;
        JgsLifetime.Pin(callback);
        ListenerStates.Add(listener.AsStructArray, state);
        subscription.Add(state);
        return listener;
    }

    /// <summary>
    /// A .NET event reached its listeners on the script thread (at once, or at a drain point): each
    /// runs, newest first, with the sender — the listener's own source value when it is the object
    /// listened to — and the event's arguments as the declared type answers them.
    /// </summary>
    internal static void FireNetEvent(NetEventSubscription subscription, object? sender, object? args)
    {
        JgsListener[] due = [.. Enumerable.Reverse(subscription.Listeners)];
        if (due.Length == 0)
        {
            return;
        }

        JgsValue data = NetConvert.ToMatlab(args, subscription.ArgsType);
        foreach (JgsListener listener in due)
        {
            if (listener.Deleted || !listener.Enabled || (listener.Depth > 0 && !listener.Recursive))
            {
                continue;
            }

            JgsValue source = listener.SourceValue.AsExternalOrNull() is NetObject held && ReferenceEquals(held.Target, sender)
                ? listener.SourceValue
                : NetConvert.ToMatlab(sender, typeof(object));
            RunListener(listener, source, data, $"for event {subscription.EventName} defined for class {subscription.ClassName}");
        }
    }

    /// <summary>
    /// <c>delete(netObj)</c>: its <c>ObjectBeingDestroyed</c> listeners run with an
    /// <c>event.EventData</c>, the object still valid inside them (probe5a: the callback reads
    /// <c>src.ToString()</c>), and then the handle is ended for every name it has (probe5h). The .NET
    /// object itself is left alone — no <c>Dispose</c> (net_members) — and a new wrapper of it is valid.
    /// </summary>
    internal static void DeleteNetObject(NetObject net, JgsValue value)
    {
        if (!net.IsHandle || net.Deleted || net.Target is null)
        {
            return;
        }

        FireNetObjectBeingDestroyed(net, value);
        net.Deleted = true;
    }

    private static void FireNetObjectBeingDestroyed(NetObject net, JgsValue value)
    {
        if (NetEventSubscription.Existing(net.Target!, NetEventSubscription.ObjectBeingDestroyed) is { Listeners.Count: > 0 } subscription)
        {
            JgsValue data = NewEventData(NetEventSubscription.ObjectBeingDestroyed, value);
            foreach (JgsListener listener in Enumerable.Reverse(subscription.Listeners).ToArray())
            {
                if (!listener.Deleted && listener.Enabled)
                {
                    RunListener(listener, listener.SourceValue, data,
                        $"for event {NetEventSubscription.ObjectBeingDestroyed} defined for class {subscription.ClassName}");
                }
            }
        }
    }
}
