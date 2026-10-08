using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a custom component - an instance of a class written under
/// <c>matlab.ui.componentcontainer.ComponentContainer</c> - is joined to (app-building plan, U10,
/// ADR 0209): the area it stands for in its figure, the callbacks its <c>HasCallbackProperty</c>
/// events hold, and whether its <c>update</c> is owed.
/// </summary>
internal sealed class JgsComponentContainerState
{
    /// <summary>The object, as the value its constructor was handed.</summary>
    public required JgsValue Self { get; init; }

    /// <summary>The area in the figure.</summary>
    public required UiComponentContainerModel Model { get; init; }

    /// <summary>The area's handle entry, whose <see cref="JgsHandleEntry.Owner"/> is the object.</summary>
    public required JgsHandleEntry Entry { get; init; }

    /// <summary>The interpreter that made it, which runs its text and cell callbacks.</summary>
    public required Interpreter Interpreter { get; init; }

    /// <summary>The callback each <c>HasCallbackProperty</c> event holds, by event name; absent for none.</summary>
    public Dictionary<string, JgsValue> Callbacks { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether <c>update</c> is running, when a write marks nothing (R2025b, probe <c>u10_matrix</c>).</summary>
    public bool Updating { get; set; }

    /// <summary>The object.</summary>
    public JgsObject Owner => Self.AsObject;
}

/// <summary>
/// The custom components of the session (U10, ADR 0209). R2025b's rules, measured headless (probes
/// <c>u10_matrix</c>, <c>u10_more</c>):
/// <list type="bullet">
/// <item><c>setup</c> runs once, inside construction, with the parent already set and before the
/// other name-value arguments are applied;</item>
/// <item><c>update</c> is owed after construction and after any write to a property of the object -
/// its own, private or not, the same value again, an inherited one such as <c>Tag</c> or
/// <c>Position</c> - except a callback property; it runs once, however many writes, at the next
/// <c>drawnow</c> or <c>pause</c>, and a write inside it owes nothing;</item>
/// <item>a failing <c>update</c> is reported and the drain goes on; it is owed again only by the
/// next write;</item>
/// <item>a component can be given children only while its <c>setup</c> runs, and those are no one's
/// children: <c>Children</c> is empty and <c>findall</c> does not reach them.</item>
/// </list>
/// </summary>
internal static class JgsComponentContainers
{
    private static readonly ConditionalWeakTable<JgsObject, JgsComponentContainerState> States = new();
    private static readonly object Gate = new();
    private static readonly List<JgsComponentContainerState> Owed = [];

    /// <summary>What an object is joined to, when it is a live custom component.</summary>
    public static bool TryState(JgsObject instance, [NotNullWhen(true)] out JgsComponentContainerState? state) =>
        States.TryGetValue(instance, out state);

    /// <summary>The area's entry for a value that is a live custom component - how the object serves as a graphics handle.</summary>
    public static bool TryEntry(JgsValue value, [NotNullWhen(true)] out JgsHandleEntry? entry)
    {
        if (value.Type == JgsType.Object && !value.AsObject.Deleted && States.TryGetValue(value.AsObject, out JgsComponentContainerState? state)
            && !state.Model.BeingDeleted)
        {
            entry = state.Entry;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>The value a script is handed for an object: its custom component where the object is one's area, its handle otherwise.</summary>
    public static JgsValue ValueFor(GraphObject target) =>
        JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry) && entry.Owner is { Deleted: false } owner
            && States.TryGetValue(owner, out JgsComponentContainerState? state)
            ? state.Self
            : JgsHandleRegistry.For(target);

    /// <summary>
    /// The arguments with each custom component replaced by its area's handle: what a maker, which
    /// reads a parent as a handle, is handed (U10).
    /// </summary>
    public static IReadOnlyList<JgsValue> AsHandles(IReadOnlyList<JgsValue> args)
    {
        JgsValue[]? changed = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Object && TryEntry(args[i], out JgsHandleEntry? entry))
            {
                changed ??= [.. args];
                changed[i] = JgsHandleRegistry.For(entry.Target);
            }
        }

        return changed ?? args;
    }

    /// <summary>
    /// Refuses a component made in a custom component outside its <c>setup</c>, in R2025b's words
    /// (probe <c>u10_more</c>: <c>uibutton(c)</c> after construction).
    /// </summary>
    public static void RequireOpen(IUiContainer parent, UiObject child, int line, int col)
    {
        if (parent is UiComponentContainerModel { InSetup: false } area)
        {
            string word = JgsGraphicsCallbackValues.ClassWord(child);
            throw new JgsRuntimeException(line, col, $"MATLAB:ui:{word}:unknownInput", $"{area.ClassName} cannot be a parent of {word}.");
        }
    }

    /// <summary>Joins an object to its area, once its constructor has made the area.</summary>
    public static void Join(JgsComponentContainerState state)
    {
        States.AddOrUpdate(state.Owner, state);
        state.Entry.Owner = state.Owner;
    }

    /// <summary>Marks an object's <c>update</c> owed - nothing while its own <c>update</c> runs.</summary>
    public static void MarkOwed(JgsObject instance)
    {
        if (!States.TryGetValue(instance, out JgsComponentContainerState? state) || state.Updating)
        {
            return;
        }

        lock (Gate)
        {
            if (!Owed.Contains(state))
            {
                Owed.Add(state);
            }
        }
    }

    /// <summary>
    /// Runs every owed <c>update</c>, in the order the components came to owe one: what a
    /// <c>drawnow</c>, a <c>pause</c> and the prompt's return do first. One that fails is reported
    /// in R2025b's words and the rest still run.
    /// </summary>
    public static void RunUpdates(JGraphScriptGlobals? host = null)
    {
        // A session runs its own components' updates - the one whose host asks, or the one
        // running on this thread - and another session's wait for it (a test host runs several,
        // the app a Python console beside the prompt); a deleted component's are dropped. Nothing
        // runs once the session's run is being cancelled.
        host ??= JgsLifetime.Current?.Interpreter.Host;
        if (host is null)
        {
            return;
        }

        JgsComponentContainerState[] due;
        lock (Gate)
        {
            if (Owed.Count == 0)
            {
                return;
            }

            due = [.. Owed.Where(s => ReferenceEquals(s.Interpreter.Host, host) && !s.Interpreter.Cancellation.IsCancellationRequested)];
            Owed.RemoveAll(s => s.Owner.Deleted || due.Contains(s));
        }

        foreach (JgsComponentContainerState state in due)
        {
            JgsObject owner = state.Owner;
            if (owner.Deleted || owner.Destroying || state.Model.BeingDeleted
                || !owner.Class.TryMethod("update", out ClassMethod? update) || update.Abstract)
            {
                continue;
            }

            state.Updating = true;
            try
            {
                owner.Class.Callable(update).Call([state.Self], 0, 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure) when (failure is JgsException || ScriptExitException.Unwrap(failure) is null)
            {
                state.Interpreter.Host?.WriteErr(
                    "Unable to execute 'update' method.\n\nCaused by:\n    " + failure.Message.Replace("\n", "\n    ", StringComparison.Ordinal));
            }
            finally
            {
                state.Updating = false;
            }
        }
    }

    /// <summary>
    /// Runs the callback a <c>HasCallbackProperty</c> event's property holds, after the event's
    /// listeners (R2025b, probe <c>u10_matrix</c>): with the object and the event's data, as
    /// <c>gcbo</c>. A callback that fails is reported, not raised.
    /// </summary>
    public static void RunEventCallback(JgsObject source, string eventName, JgsValue data)
    {
        if (!States.TryGetValue(source, out JgsComponentContainerState? state)
            || !state.Callbacks.TryGetValue(eventName, out JgsValue? callback))
        {
            return;
        }

        using IDisposable running = JgsGraphicsCallbackState.Enter(state.Model, clicked: null);
        try
        {
            JgsGraphicsCallbackValues.Invoke(state.Interpreter, callback, state.Self, data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is JgsException || ScriptExitException.Unwrap(failure) is null)
        {
            state.Interpreter.Host?.WriteErr($"{failure.Message}\nError while evaluating {source.Class.Name} {eventName}Fcn.");
        }
    }

    /// <summary>
    /// <c>get(c)</c>, <c>get(c, name)</c> and <c>get(c, {names})</c> on a custom component: its
    /// properties as a dot reads them, each name matched without regard to case; <c>get(c)</c> is
    /// a struct of every listed one, in <c>properties</c>' order (probe <c>u10_matrix</c>).
    /// </summary>
    public static JgsValue Get(JgsComponentContainerState state, IReadOnlyList<JgsValue> args, int line, int col)
    {
        RequireLive(state, line, col);
        if (args.Count == 1)
        {
            var all = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (ClassProperty property in state.Owner.Class.ListedProperties)
            {
                all[property.Spec.Name] = JgsValue.Share(state.Interpreter.ReadProperty(state.Self, property.Spec.Name, line, col));
            }

            return JgsValue.Struct(all);
        }

        JgsValue One(JgsValue asked) =>
            state.Interpreter.ReadProperty(state.Self, PropertyNamed(state, asked, line, col), line, col);

        if (args[1].Type == JgsType.Cell)
        {
            return JgsValue.Cell([.. args[1].AsCell.Select(name => JgsValue.Share(One(name)))]);
        }

        return One(args[1]);
    }

    /// <summary>
    /// <c>set(c, name, value, …)</c> and <c>set(c, struct)</c> on a custom component: each write
    /// made as a dot would make it; a name it has no property of is R2025b's <c>set</c> refusal.
    /// </summary>
    public static JgsValue Set(JgsComponentContainerState state, IReadOnlyList<JgsValue> args, int line, int col)
    {
        RequireLive(state, line, col);
        var pairs = new List<(JgsValue Name, JgsValue Value)>();
        if (args.Count == 2 && args[1].Type == JgsType.Struct && !args[1].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[1].AsStruct)
            {
                pairs.Add((JgsValue.Str(name), value));
            }
        }
        else if (args.Count == 1)
        {
            return JgsValue.Cell([.. state.Owner.Class.ListedProperties.Where(static p => p.SetAccess.IsPublic).Select(static p => JgsValue.Str(p.Spec.Name))]);
        }
        else
        {
            if (args.Count % 2 == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:BadParamValuePairs", "Invalid parameter/value pair arguments.");
            }

            for (int i = 1; i + 1 < args.Count; i += 2)
            {
                pairs.Add((args[i], args[i + 1]));
            }
        }

        foreach ((JgsValue name, JgsValue value) in pairs)
        {
            state.Interpreter.WriteProperty(state.Self, PropertyNamed(state, name, line, col), value, line, col);
        }

        return JgsValue.Null;
    }

    /// <summary>The property a name given to <c>get</c> or <c>set</c> means: the listed one it spells, in any case.</summary>
    private static string PropertyNamed(JgsComponentContainerState state, JgsValue asked, int line, int col)
    {
        string typed = JgsBuiltins.IsTextScalar(asked) ? JgsBuiltins.TextOf(asked) : string.Empty;
        return state.Owner.Class.ListedProperties.FirstOrDefault(p => p.Spec.Name.Equals(typed, StringComparison.OrdinalIgnoreCase))?.Spec.Name
            ?? throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty", $"Unrecognized property {typed} for class {state.Owner.Class.Name}.");
    }

    private static void RequireLive(JgsComponentContainerState state, int line, int col)
    {
        if (state.Owner.Deleted || state.Owner.Destroying)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
        }
    }

    /// <summary>Forgets the owed updates - a fresh run, or a cleared figure registry.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Owed.Clear();
        }
    }
}
