using System.Runtime.CompilerServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Events and listeners (V6, ADR 0167, appendix A #106 and #108): <c>addlistener</c>, <c>listener</c>,
/// <c>notify</c>, <c>events</c>, the <c>PreSet</c>/<c>PostSet</c> a <c>SetObservable</c> property
/// raises, and the <c>ObjectBeingDestroyed</c> every handle raises from <c>delete</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The objects.</b> A listener is a struct wearing <c>event.listener</c> (or
/// <c>event.proplistener</c>) and a handle, like a timer: its private half is a
/// <see cref="JgsListener"/> in a weak table beside the value, and the source object's own list
/// holds it — which is what makes it live with the source (R2025b: <c>clear lh</c> changes nothing,
/// <c>delete(lh)</c> ends it). The event a callback receives is a struct wearing
/// <c>event.EventData</c> (<c>EventName</c>, <c>Source</c>) or <c>event.PropertyEvent</c>
/// (<c>AffectedObject</c>, <c>Source</c> — a <c>matlab.metadata.Property</c> with a <c>Name</c> —
/// and <c>EventName</c>), or an instance of a class written <c>&lt; event.EventData</c>, whose two
/// inherited properties <c>notify</c> fills in.
/// </para>
/// <para>
/// <b>When a callback runs.</b> Inside <c>notify</c>, inside the property write, inside
/// <c>delete</c> — on the script thread, synchronously, which is what R2025b does
/// (<c>a;L;b;</c> around a <c>notify</c>; <c>pre;post;</c> around a set). A statement's operands
/// were read before the write began (M5), so a callback writing a global the caller holds leaves
/// the caller's read alone: <c>g + fire_zero(b)</c> is <c>[1 2 3]</c> with <c>g</c> <c>[7 2 3]</c>.
/// R2025b was recorded for the rest: listeners fire newest first; a callback that fails is
/// reported as a warning and the others still run; a listener is not re-entered underneath its
/// own callback unless <c>Recursive</c>; every indexed write into an observable property raises one
/// <c>PostSet</c> (<c>o.a(2) = 9</c>, <c>o.a(end + 1) = 4</c> — two); a set to the same value raises
/// it too; <c>delete</c> raises <c>ObjectBeingDestroyed</c> after the object is already invalid.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name an event listener answers to.</summary>
    internal const string ListenerClassName = "event.listener";

    /// <summary>The class name a property listener answers to.</summary>
    internal const string PropListenerClassName = "event.proplistener";

    /// <summary>The class name the default event data wears.</summary>
    internal const string EventDataClassName = "event.EventData";

    /// <summary>The class name a <c>PreSet</c>/<c>PostSet</c> event wears.</summary>
    internal const string PropertyEventClassName = "event.PropertyEvent";

    /// <summary>The class name a property event's <c>Source</c> wears.</summary>
    internal const string MetaPropertyClassName = "matlab.metadata.Property";

    /// <summary>The event data's two fields, which a class written <c>&lt; event.EventData</c> inherits.</summary>
    internal const string EventNameField = "EventName";

    /// <summary>See <see cref="EventNameField"/>.</summary>
    internal const string EventSourceField = "Source";

    private const string CallbackWarningId = "MATLAB:callback:error";

    private static readonly ConditionalWeakTable<JgsStructArray, JgsListener> ListenerStates = new();

    /// <summary>Whether a value is a listener of either kind, deleted or not.</summary>
    internal static bool IsListener(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is ListenerClassName or PropListenerClassName;

    /// <summary>Whether a value is a listener that <c>delete</c> has ended — what <c>isvalid</c> asks.</summary>
    internal static bool IsDeletedListener(JgsValue value) =>
        IsListener(value) && ListenerStates.TryGetValue(value.AsStructArray, out JgsListener? state) && state.Deleted;

    private static JgsListener ListenerStateOf(JgsValue listener, int line, int col) =>
        ListenerStates.TryGetValue(listener.AsStructArray, out JgsListener? state)
            ? state
            : throw new JgsRuntimeException(line, col,
                "this listener has lost track of its source — it was copied out of the run that made it.");

    /// <summary>Registers <c>addlistener</c>, <c>listener</c>, <c>notify</c> and <c>events</c>.</summary>
    internal static void RegisterEventBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(host);

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body, bool bindsAns = true) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = bindsAns,
            }));

        // Each verb that reaches script code is spelled out as a call, for the ownership audit.
        Define("addlistener", (args, line, col) => AddListener("addlistener", host, args, line, col));
        Define("listener", (args, line, col) => AddListener("listener", host, args, line, col));
        Define("notify", (args, line, col) => Notify(args, line, col), bindsAns: false);
        Define("events", (args, line, col) => EventNames(interpreter, args, line, col));
    }

    // --- addlistener ----------------------------------------------------------------------------

    /// <summary>
    /// <c>lh = addlistener(obj, 'Event', @cb)</c> and <c>addlistener(obj, 'prop' or {props}, 'PreSet' or
    /// 'PostSet', @cb)</c>: checked in R2025b's words, then put on the source's list. <c>listener</c>
    /// makes the same object (its exact lifetime is V10's, #107).
    /// </summary>
    private static JgsValue AddListener(
        string verb, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "Not enough input arguments.");
        }

        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "Too many input arguments.");
        }

        JgsValue sourceValue = args[0];
        if (sourceValue.Type == JgsType.Number)
        {
            throw new JgsRuntimeException(line, col, "Double input must be an HG handle");
        }

        if (sourceValue.Type != JgsType.Object || !sourceValue.AsObject.Class.IsHandle)
        {
            throw new JgsRuntimeException(line, col,
                $"First argument provided is not valid for {verb}. (Check its type or validity)");
        }

        JgsObject source = sourceValue.AsObject;
        if (source.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        JgsValue callback = args[^1];
        if (callback.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"Invalid input argument for function '{verb}'.");
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        JgsListener state;
        JgsValue listener;
        if (args.Count == 3)
        {
            if (!IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, $"Invalid input argument for function '{verb}'.");
            }

            string eventName = TextOf(args[1]);
            RequireEvent(source.Class, eventName, line, col);
            fields[EventSourceField] = JgsValue.Cell([sourceValue]);
            fields[EventNameField] = JgsValue.Str(eventName);
            fields["Callback"] = callback;
            fields["Enabled"] = JgsValue.Bool(true);
            fields["Recursive"] = JgsValue.Bool(false);
            listener = JgsValue.Struct(fields);
            listener.SetClassName(ListenerClassName);
            state = new JgsListener
            {
                Value = listener,
                Source = source,
                SourceValue = sourceValue,
                Host = host,
                EventName = eventName,
                Callback = callback,
            };
        }
        else
        {
            string[] properties = PropertyNamesArgument(verb, args[1], line, col);
            if (!IsTextScalar(args[2]))
            {
                throw new JgsRuntimeException(line, col, $"Invalid input argument for function '{verb}'.");
            }

            string kind = TextOf(args[2]);
            foreach (string property in properties)
            {
                if (source.Class.Property(property) is not { } declared)
                {
                    throw new JgsRuntimeException(line, col,
                        $"The name '{property}' is not an accessible property for an instance of class '{source.Class.Name}'.");
                }

                if (kind is not ("PreSet" or "PostSet"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Event '{kind}' is not defined for class '{source.Class.Name}'.");
                }

                if (!declared.Observable)
                {
                    throw new JgsRuntimeException(line, col,
                        $"While adding a {kind} listener, property '{property}' in class '{source.Class.Name}' is not defined to be SetObservable.");
                }
            }

            var metas = new JgsValue[properties.Length];
            for (int i = 0; i < metas.Length; i++)
            {
                metas[i] = MetaProperty(properties[i]);
            }

            fields["Object"] = JgsValue.Cell([sourceValue]);
            fields[EventSourceField] = JgsValue.Cell(metas);
            fields[EventNameField] = JgsValue.Str(kind);
            fields["Callback"] = callback;
            fields["Enabled"] = JgsValue.Bool(true);
            fields["Recursive"] = JgsValue.Bool(false);
            listener = JgsValue.Struct(fields);
            listener.SetClassName(PropListenerClassName);
            state = new JgsListener
            {
                Value = listener,
                Source = source,
                SourceValue = sourceValue,
                Host = host,
                EventName = kind,
                Properties = properties,
                Callback = callback,
            };
        }

        ListenerStates.Add(listener.AsStructArray, state);
        (source.Listeners ??= new List<JgsListener>()).Add(state);
        return listener;
    }

    /// <summary>The property or properties a four-argument <c>addlistener</c> names.</summary>
    private static string[] PropertyNamesArgument(string verb, JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            return [TextOf(value)];
        }

        if (value.Type == JgsType.Cell && value.AsCell.All(IsTextScalar) && value.AsCell.Length > 0)
        {
            return [.. value.AsCell.Select(TextOf)];
        }

        throw new JgsRuntimeException(line, col, $"Invalid input argument for function '{verb}'.");
    }

    private static void RequireEvent(JgsClass definition, string eventName, int line, int col)
    {
        if (!definition.HasEvent(eventName))
        {
            throw new JgsRuntimeException(line, col,
                $"Event '{eventName}' is not defined for class '{definition.Name}'.");
        }
    }

    /// <summary>A property event's <c>Source</c>: the property's metadata, of which the name is what a script reads.</summary>
    private static JgsValue MetaProperty(string name)
    {
        JgsValue meta = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Name"] = JgsValue.Str(name),
        });
        meta.SetClassName(MetaPropertyClassName);
        return meta;
    }

    /// <summary>The event a plain <c>notify</c> hands its listeners: its name and its source.</summary>
    private static JgsValue NewEventData(string eventName, JgsValue source)
    {
        JgsValue data = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [EventNameField] = JgsValue.Str(eventName),
            [EventSourceField] = source,
        });
        data.SetClassName(EventDataClassName);
        return data;
    }

    // --- notify ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>notify(obj, 'Event')</c> and <c>notify(obj, 'Event', data)</c>: every listener for the
    /// event runs before this returns, newest first. The data must be an instance of a class written
    /// <c>&lt; event.EventData</c>, whose <c>EventName</c> and <c>Source</c> are filled in here.
    /// </summary>
    private static JgsValue Notify(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "Too many input arguments.");
        }

        JgsValue sourceValue = args[0];
        if (sourceValue.Type != JgsType.Object || !sourceValue.AsObject.Class.IsHandle)
        {
            throw new JgsRuntimeException(line, col,
                $"Undefined function 'notify' for input arguments of type '{ClassOf(sourceValue, JgsDialect.Matlab)}'.");
        }

        JgsObject source = sourceValue.AsObject;
        if (source.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        if (!IsTextScalar(args[1]))
        {
            throw new JgsRuntimeException(line, col, "Invalid input argument for function 'notify'.");
        }

        string eventName = TextOf(args[1]);
        if (eventName == JgsClass.ObjectBeingDestroyed)
        {
            throw new JgsRuntimeException(line, col,
                $"Cannot notify listeners of event '{JgsClass.ObjectBeingDestroyed}' in class 'handle'.");
        }

        RequireEvent(source.Class, eventName, line, col);

        JgsValue data;
        if (args.Count == 3)
        {
            data = args[2];
            if (data.Type != JgsType.Object || !data.AsObject.Class.IsEventData)
            {
                throw new JgsRuntimeException(line, col, "Invalid input argument for function 'notify'.");
            }

            // The two inherited properties, written into the one object every listener is handed
            // (a handle: M7's gate never copies it).
            Dictionary<string, JgsValue> carried = data.WritableFields();
            carried[EventNameField] = JgsValue.Str(eventName);
            carried[EventSourceField] = sourceValue;
        }
        else
        {
            data = NewEventData(eventName, sourceValue);
        }

        FireEvent(source, eventName, data);
        return JgsValue.Null;
    }

    /// <summary>
    /// Runs the source's listeners for <paramref name="eventName"/>, newest first, each with the
    /// source and <paramref name="data"/>. Takes the listeners on the list when it starts, so one
    /// added by a callback waits for the next event; one deleted or disabled by an earlier callback
    /// is skipped; one whose callback is already on the stack is skipped unless <c>Recursive</c>.
    /// </summary>
    internal static void FireEvent(JgsObject source, string eventName, JgsValue data)
    {
        if (source.Listeners is not { Count: > 0 } listeners)
        {
            return;
        }

        var due = new List<JgsListener>();
        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            JgsListener listener = listeners[i];
            if (!listener.Deleted && !listener.IsProperty && listener.EventName == eventName)
            {
                due.Add(listener);
            }
        }

        foreach (JgsListener listener in due)
        {
            if (listener.Deleted || !listener.Enabled || (listener.Depth > 0 && !listener.Recursive))
            {
                continue;
            }

            RunListener(listener, listener.SourceValue, data,
                $"for event {eventName} defined for class {source.Class.Name}");
        }
    }

    /// <summary>
    /// The <c>PreSet</c> (<paramref name="post"/> false) or <c>PostSet</c> listeners on one property
    /// of one object, newest first, each with the property's metadata and an
    /// <c>event.PropertyEvent</c> whose <c>AffectedObject</c> is the object. Nothing runs, and
    /// nothing is allocated, on an object nobody listens to.
    /// </summary>
    internal static void FirePropertyEvent(JgsValue objectValue, string property, bool post)
    {
        JgsObject instance = objectValue.AsObject;
        if (instance.Listeners is not { Count: > 0 } listeners)
        {
            return;
        }

        string kind = post ? "PostSet" : "PreSet";
        List<JgsListener>? due = null;
        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            JgsListener listener = listeners[i];
            if (!listener.Deleted && listener.EventName == kind && listener.Watches(property))
            {
                (due ??= new List<JgsListener>()).Add(listener);
            }
        }

        if (due is null)
        {
            return;
        }

        JgsValue meta = MetaProperty(property);
        JgsValue evt = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["AffectedObject"] = objectValue,
            [EventSourceField] = meta,
            [EventNameField] = JgsValue.Str(kind),
        });
        evt.SetClassName(PropertyEventClassName);

        foreach (JgsListener listener in due)
        {
            if (listener.Deleted || !listener.Enabled || (listener.Depth > 0 && !listener.Recursive))
            {
                continue;
            }

            RunListener(listener, meta, evt, $"for the {instance.Class.Name} class {property} property {kind} event");
        }
    }

    /// <summary>
    /// <c>ObjectBeingDestroyed</c> from <c>delete</c>: raised after the object is marked deleted
    /// (R2025b: <c>isvalid(src)</c> is already false inside the callback), through every alias.
    /// </summary>
    internal static void FireObjectBeingDestroyed(JgsObject instance)
    {
        if (instance.Listeners is not { Count: > 0 } listeners)
        {
            return;
        }

        FireEvent(instance, JgsClass.ObjectBeingDestroyed,
            NewEventData(JgsClass.ObjectBeingDestroyed, listeners[0].SourceValue));
    }

    /// <summary>
    /// One callback, with MATLAB's two arguments. A failure is a warning in MATLAB's words, recorded
    /// for <c>lastwarn</c> and written to the error output, and the event goes on to the next
    /// listener; a cancellation always unwinds.
    /// </summary>
    private static void RunListener(JgsListener listener, JgsValue first, JgsValue data, string where)
    {
        listener.Depth++;
        try
        {
            JgsCallbacks.Invoke(listener.Callback.AsCallable, [first, data], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            WarnListenerFailure(listener.Host, where, failure.Message);
        }
        catch (Exception failure) when (ScriptExitException.Unwrap(failure) is null)
        {
            WarnListenerFailure(listener.Host, where, failure.Message);
        }
        finally
        {
            listener.Depth--;
        }
    }

    private static void WarnListenerFailure(JGraphScriptGlobals host, string where, string message)
    {
        string text = $"Error occurred while executing the listener callback {where}:\n{message}";
        host.Warnings.Record(CallbackWarningId, text);
        if (host.Warnings.IsOn(CallbackWarningId))
        {
            host.WriteErr("Warning: " + text);
        }
    }

    // --- events ---------------------------------------------------------------------------------

    /// <summary><c>events(obj)</c> or <c>events('Name')</c>: the class's events as a cell column, <c>ObjectBeingDestroyed</c> last for a handle.</summary>
    private static JgsValue EventNames(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("events", args, 1, line, col);
        JgsValue asked = args[0];
        JgsClass? definition = asked.Type == JgsType.Object ? asked.AsObject.Class : NamedClass(asked, interpreter);
        if (definition is null)
        {
            if (asked.Type == JgsType.Struct)
            {
                return EventColumn([]);
            }

            throw new JgsRuntimeException(line, col, $"events: a {asked.TypeName} has no events to list.");
        }

        IEnumerable<string> names = definition.Events;
        if (definition.IsHandle)
        {
            names = names.Append(JgsClass.ObjectBeingDestroyed);
        }

        return EventColumn(names);
    }

    /// <summary>A cell column of names, 0-by-1 when there are none (R2025b's shape for <c>events</c>).</summary>
    private static JgsValue EventColumn(IEnumerable<string> names)
    {
        JgsValue[] cells = [.. names.Select(JgsValue.Str)];
        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, 1);
        return column;
    }

    // --- the listener's properties ---------------------------------------------------------------

    /// <summary>One property written through M7's gate; a listener is a handle, so the gate never copies.</summary>
    private static void SetListenerField(JgsListener state, string name, JgsValue value) =>
        state.Value.WritableStruct()[name] = value;

    /// <summary><c>lh.Enabled</c>: a field read that refuses on a deleted listener and names an unknown property.</summary>
    internal static JgsValue GetListenerProperty(JgsValue listener, string field, int line, int col)
    {
        JgsListener state = ListenerStateOf(listener, line, col);
        if (state.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        if (listener.AsStruct.TryGetValue(field, out JgsValue? held))
        {
            return held;
        }

        throw new JgsRuntimeException(line, col,
            $"Unrecognized method, property, or field '{field}' for class '{listener.ClassName}'.");
    }

    /// <summary>
    /// <c>lh.Enabled = false</c>, <c>lh.Recursive = true</c>, <c>lh.Callback = @f</c>: checked in
    /// R2025b's words. The value arrives as the binding's share, and a handle's fields are written
    /// in place, so every alias reads the write.
    /// </summary>
    internal static void SetListenerProperty(JgsValue listener, string field, JgsValue value, int line, int col)
    {
        JgsListener state = ListenerStateOf(listener, line, col);
        if (state.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        if (!listener.AsStruct.ContainsKey(field))
        {
            throw new JgsRuntimeException(line, col,
                $"Unrecognized property '{field}' for class '{listener.ClassName}'.");
        }

        string shortName = listener.ClassName == ListenerClassName ? "listener" : "proplistener";
        switch (field)
        {
            case "Enabled":
            case "Recursive":
            {
                if (value.Type is not (JgsType.Bool or JgsType.Number))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Error setting property '{field}' of class '{shortName}': Value must be a scalar.");
                }

                bool flag = value.AsNumber != 0;
                if (field == "Enabled")
                {
                    state.Enabled = flag;
                }
                else
                {
                    state.Recursive = flag;
                }

                SetListenerField(state, field, JgsValue.Bool(flag));
                return;
            }

            case "Callback":
                if (value.Type != JgsType.Function)
                {
                    throw new JgsRuntimeException(line, col,
                        $"Error setting property 'Callback' of class '{shortName}': Value must be 'function_handle'.");
                }

                state.Callback = value;
                SetListenerField(state, field, value);
                return;

            case EventNameField:
                if (!IsTextScalar(value))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Error setting property 'EventName' of class '{shortName}': Value must be a character vector or a string scalar.");
                }

                state.EventName = TextOf(value);
                SetListenerField(state, field, JgsValue.Str(state.EventName));
                return;

            default:
                // Source and Object: R2025b accepts a write; the listener stays on the object it was made on.
                SetListenerField(state, field, value);
                return;
        }
    }

    /// <summary>
    /// <c>delete(lh)</c>: the listener leaves its source's list and is ended for every alias — its
    /// dots refuse and <c>isvalid</c> answers false. A second delete is nothing. Answers false for
    /// anything that is not a listener.
    /// </summary>
    internal static bool TryDeleteListener(JgsValue value, int line, int col)
    {
        if (!IsListener(value))
        {
            return false;
        }

        JgsListener state = ListenerStateOf(value, line, col);
        if (state.Deleted)
        {
            return true;
        }

        state.Deleted = true;
        state.Source.Listeners?.Remove(state);
        return true;
    }
}
