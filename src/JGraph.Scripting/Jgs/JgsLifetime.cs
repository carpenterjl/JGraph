using System.Runtime.CompilerServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// V10 (ADR 0171): exact lifetime for what has a destructor — a handle object whose class writes
/// <c>delete</c> (an <c>onCleanup</c> included), a listener made by <c>listener</c> — and for the
/// containers, snapshots and workspaces that hold one.
/// </summary>
/// <remarks>
/// <para>
/// M1's holder counts only over-state (a dead wrapper never decrements), so they cannot say when
/// the last reference to a handle went. This class keeps a second, <em>exact</em> count beside
/// them, moved only where a holder starts or stops holding: a variable bind, rebind or
/// <c>clear</c> (<see cref="JgsEnvironment"/>'s stores), a container slot, field or property
/// write (<see cref="Stored"/>), a container built by a factory (<see cref="Minted"/>), M3's
/// shallow detach (<see cref="Detached"/>), a frame's exit (<see cref="FrameExited"/>), and a
/// nested-function handle escaping its workspace. A count that reaches zero is not acted on at
/// once: the value may still be in flight — a callee's output on its way to the caller's name, a
/// temporary an enclosing statement is still reading — so the zero is queued as a <em>check</em>
/// at the block depth of the statement that was running, and examined at the next statement
/// boundary at that depth or shallower (<see cref="Drain"/>, from <c>Interpreter.Tick</c>). By
/// then the caller has bound what it kept, and what nobody kept is destroyed there: the
/// destructor runs on the script thread, between two statements, after the one that dropped the
/// last holder — as R2025b runs it (measured: <c>use_tag(DeleteLogger('T')); vlog('after')</c>
/// logs <c>usedT;T;after;</c>).
/// </para>
/// <para>
/// <b>What is walked.</b> A container's children are released only when the container was
/// <em>scanned</em>: built by a factory while something with a destructor was alive
/// (<see cref="AnyLive"/>) and holding such a thing, or written a tracked value through a store
/// hook, which scans the rest of it then. An unscanned container's children were never counted
/// for it, so its death releases nothing — a leak in the safe direction (a destructor that never
/// runs, today's behaviour) rather than a destructor that runs while a name still reaches the
/// object. Cells and boxed arrays carry the mark on their wrapper (<see cref="JgsValue.Tracked"/>),
/// a struct array and an object on their payload, so a numeric array is never walked.
/// </para>
/// <para>
/// <b>Order</b> (measured in R2025b): a workspace releases its variables in first-declaration
/// order (<c>z; a; m</c> destroy as <c>Z;A;M</c>, a rebound name keeps its slot); a container
/// releases its direct handles first, in index or field order, then its nested containers
/// (<c>{A, {B, C}, D}</c> destroys as <c>A;D;B;C</c>); a handle's <c>delete</c> runs before its
/// properties are released (<c>H;I;J</c>), and an explicit <c>delete(h)</c> releases them at once.
/// </para>
/// </remarks>
internal sealed class JgsLifetime
{
    // How many destructor-bearing things are alive across every run: the gate on the factory
    // scans, so a script that never makes one pays nothing for the bookkeeping.
    private static int s_live;

    // The tracker of the run on this thread — for the store hooks in JgsValue, which have no
    // environment in hand. Set by the interpreter's entry points; a stale one on a pooled thread
    // only ever queues checks that nothing drains, which is the safe direction.
    [ThreadStatic]
    private static JgsLifetime? t_current;

    // The exact count of a cell or boxed array's slots, kept only for a tracked one; Dead once
    // the slots' children have been released, so a second check of the same zero releases nothing.
    private static readonly ConditionalWeakTable<JgsValue[], StrongBox<int>> SlotCounts = new();
    private const int Dead = int.MinValue;

    private readonly Interpreter _interpreter;
    private readonly List<(object Payload, int Depth)> _pending = new();
    private bool _draining;

    public JgsLifetime(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <summary>Whether anything with a destructor is alive in any run — the gate on the factory scans.</summary>
    internal static bool AnyLive => Volatile.Read(ref s_live) > 0;

    /// <summary>The tracker of the run on this thread, or null outside a run.</summary>
    internal static JgsLifetime? Current
    {
        get => t_current;
        set => t_current = value;
    }

    /// <summary>Whether a check is queued — read at every statement boundary, so it is one count.</summary>
    internal bool HasPending => _pending.Count > 0;

    // --- what participates ---------------------------------------------------------------------

    /// <summary>
    /// Whether holding <paramref name="value"/> is a hold the count must see: a handle object (its
    /// instance is counted from birth), a scanned value object, a tracked cell or boxed array, a
    /// scanned struct array, an anonymous function whose snapshot captured something tracked, or
    /// a nested function's handle (which holds its workspace).
    /// </summary>
    internal static bool MayTrack(JgsValue value) => value.Type switch
    {
        JgsType.Object => value.AsObject.Class.IsHandle || value.AsObject.Scanned,
        JgsType.Cell or JgsType.Array => value.Tracked,
        JgsType.Struct => value.AsStructArray.Scanned,
        JgsType.Function => TracksCallable(value.AsCallable),
        _ => false,
    };

    private static bool TracksCallable(IJgsCallable callable) => callable switch
    {
        AnonymousFunction anonymous => anonymous.Tracked,
        UserFunction { IsNested: true } => true,
        NamedHandle named => TracksCallable(named.Captured),
        BoundMethod bound => MayTrack(bound.Receiver),
        _ => false,
    };

    /// <summary>A handle: released before the containers beside it (measured order).</summary>
    private static bool IsHandleLike(JgsValue value) =>
        (value.Type == JgsType.Object && value.AsObject.Class.IsHandle)
        || (value.Type == JgsType.Struct && JgsBuiltins.IsHandleClass(value));

    /// <summary>Whether a wrapper's payload has a count of its own (a handle's is on its instance).</summary>
    private static bool IsCountedContainer(JgsValue value) => value.Type switch
    {
        JgsType.Cell or JgsType.Array or JgsType.Struct => true,
        JgsType.Object => !value.AsObject.Class.IsHandle,
        _ => false,
    };

    // --- counting ------------------------------------------------------------------------------

    /// <summary>An entry (in <paramref name="env"/>, or a container when null) now holds <paramref name="value"/>.</summary>
    internal void Retain(JgsValue value, JgsEnvironment? env)
    {
        switch (value.Type)
        {
            case JgsType.Object:
                value.AsObject.Exact++;
                break;
            case JgsType.Struct:
                value.AsStructArray.Exact++;
                break;
            case JgsType.Cell or JgsType.Array:
            {
                StrongBox<int> box = SlotCounts.GetValue(value.Slots, static _ => new StrongBox<int>(0));
                if (box.Value != Dead)
                {
                    box.Value++;
                }

                break;
            }

            case JgsType.Function:
                RetainCallable(value.AsCallable, env);
                break;
        }

        value.Counted = true;
    }

    private void RetainCallable(IJgsCallable callable, JgsEnvironment? env)
    {
        switch (callable)
        {
            case AnonymousFunction anonymous:
                anonymous.Exact++;
                break;
            case UserFunction { IsNested: true } nested:
                // A handle held by the workspace's own frame (f = @read before f is returned)
                // is no escape: R2025b destroys the workspace at the frame's exit when nothing
                // outside holds it (measured), and keeps it while a caller's f does.
                if (env is null || !env.IsWithin(nested.Closure))
                {
                    nested.Closure.Escapes++;
                }

                break;
            case NamedHandle named:
                RetainCallable(named.Captured, env);
                break;
            case BoundMethod bound:
                if (MayTrack(bound.Receiver))
                {
                    Retain(bound.Receiver, env);
                }

                break;
        }
    }

    /// <summary>An entry let go of <paramref name="value"/>; a count that reaches zero queues a check.</summary>
    internal void Release(JgsValue value, JgsEnvironment? env)
    {
        switch (value.Type)
        {
            case JgsType.Object:
            {
                JgsObject instance = value.AsObject;
                if (instance.Exact > 0 && --instance.Exact == 0)
                {
                    Defer(instance);
                }

                break;
            }

            case JgsType.Struct:
            {
                JgsStructArray payload = value.AsStructArray;
                if (payload.Exact > 0 && --payload.Exact == 0)
                {
                    Defer(payload);
                }

                break;
            }

            case JgsType.Cell or JgsType.Array:
            {
                JgsValue[] slots = value.Slots;
                if (SlotCounts.TryGetValue(slots, out StrongBox<int>? box) && box.Value > 0 && --box.Value == 0)
                {
                    Defer(slots);
                }

                break;
            }

            case JgsType.Function:
                ReleaseCallable(value.AsCallable, env);
                break;
        }
    }

    private void ReleaseCallable(IJgsCallable callable, JgsEnvironment? env)
    {
        switch (callable)
        {
            case AnonymousFunction anonymous:
                if (anonymous.Exact > 0 && --anonymous.Exact == 0)
                {
                    Defer(anonymous);
                }

                break;
            case UserFunction { IsNested: true } nested:
                if (env is null || !env.IsWithin(nested.Closure))
                {
                    JgsEnvironment workspace = nested.Closure;
                    if (workspace.Escapes > 0 && --workspace.Escapes == 0 && workspace.Exited)
                    {
                        Defer(workspace);
                    }
                }

                break;
            case NamedHandle named:
                ReleaseCallable(named.Captured, env);
                break;
            case BoundMethod bound:
                if (MayTrack(bound.Receiver))
                {
                    Release(bound.Receiver, env);
                }

                break;
        }
    }

    /// <summary>
    /// Keeps <paramref name="value"/> alive for good: what a store outside the model takes —
    /// appdata, <c>guidata</c>, a timer's or an addlistener listener's property — where the
    /// count cannot see when the store lets go.
    /// </summary>
    internal static void Pin(JgsValue value)
    {
        if (MayTrack(value))
        {
            (Current ?? Find(value))?.Retain(value, null);
        }
    }

    // --- the hooks -----------------------------------------------------------------------------

    /// <summary>
    /// A factory built <paramref name="value"/> over fresh storage (a cell, a boxed array, a
    /// struct array, an object) while something with a destructor is alive. Holding a tracked
    /// child, it is scanned — its children counted for it — and, held by nobody yet, a check is
    /// queued so a temporary nobody binds releases them at its statement's end. A handle object
    /// with a destructor gets the check whatever it holds, so one nobody binds is destroyed then.
    /// </summary>
    internal static void Minted(JgsValue value)
    {
        JgsLifetime? tracker = Current;
        switch (value.Type)
        {
            case JgsType.Cell or JgsType.Array:
            {
                JgsValue[] slots = value.Slots;
                if (value.Tracked)
                {
                    return;
                }

                // Nothing with a destructor exists: only a handle's holders need counting, and
                // that test is the cheap one (a type per slot), so a numeric or text array
                // costs a walk of its slots and no more.
                if (!AnyLive ? !AnyHandleChild(slots) : !AnyChild(slots))
                {
                    return;
                }

                value.Tracked = true;
                SlotCounts.GetValue(slots, static _ => new StrongBox<int>(0));
                foreach (JgsValue child in slots)
                {
                    RetainChild(child, ref tracker, force: false);
                }

                tracker?.Defer(slots);
                return;
            }

            case JgsType.Struct:
            {
                JgsStructArray payload = value.AsStructArray;
                if (payload.Scanned || (!AnyLive ? !AnyHandleChild(payload) : !AnyChild(payload)))
                {
                    return;
                }

                payload.Scanned = true;
                foreach (Dictionary<string, JgsValue> element in payload.Elements)
                {
                    foreach (KeyValuePair<string, JgsValue> field in element)
                    {
                        RetainChild(field.Value, ref tracker, force: false);
                    }
                }

                tracker?.Defer(payload);
                return;
            }

            case JgsType.Object:
            {
                JgsObject instance = value.AsObject;
                if (instance.Wrapped)
                {
                    return;
                }

                instance.Wrapped = true;
                if (!AnyLive ? AnyHandleChild(instance.Fields) : AnyChild(instance.Fields))
                {
                    instance.Scanned = true;
                    foreach (KeyValuePair<string, JgsValue> field in instance.Fields)
                    {
                        RetainChild(field.Value, ref tracker, force: false);
                    }
                }

                if (instance.Scanned || instance.Class.HasDestructor)
                {
                    tracker ??= instance.Class.Interpreter.Lifetimes;
                    tracker.Defer(instance);
                }

                return;
            }
        }
    }

    // The walks below take the concrete storage (a slot array, a field dictionary) rather than
    // IEnumerable<JgsValue>, so a struct temporary's scan boxes no enumerator and asks the
    // dictionary for no value collection: the walk is what every struct built pays.
    private static bool AnyChild(JgsValue[] slots)
    {
        foreach (JgsValue child in slots)
        {
            if (child is not null && MayTrack(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyChild(Dictionary<string, JgsValue> fields)
    {
        foreach (KeyValuePair<string, JgsValue> field in fields)
        {
            if (field.Value is not null && MayTrack(field.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyChild(JgsStructArray payload)
    {
        foreach (Dictionary<string, JgsValue> element in payload.Elements)
        {
            if (AnyChild(element))
            {
                return true;
            }
        }

        return false;
    }

    // A handle object's holders are counted from its birth, whether or not anything with a
    // destructor exists yet (a destructor-bearing value may be stored into it later, and its
    // holders must be exact by then), so a container holding one is scanned whatever is alive.
    private static bool AnyHandleChild(JgsValue[] slots)
    {
        foreach (JgsValue child in slots)
        {
            if (child is { Type: JgsType.Object } && child.AsObject.Class.IsHandle)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyHandleChild(Dictionary<string, JgsValue> fields)
    {
        foreach (KeyValuePair<string, JgsValue> field in fields)
        {
            if (field.Value is { Type: JgsType.Object } && field.Value.AsObject.Class.IsHandle)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyHandleChild(JgsStructArray payload)
    {
        foreach (Dictionary<string, JgsValue> element in payload.Elements)
        {
            if (AnyHandleChild(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Undoes a factory scan of a builtin handle's struct — a listener, whose <c>Source</c> must
    /// not keep the source alive (R2025b destroys an object its own listeners still name) and
    /// whose contents are outside the model: the children's holds are given back, and the
    /// struct is left unscanned.
    /// </summary>
    internal static void Unscan(JgsValue value)
    {
        if (value.Type != JgsType.Struct || !value.AsStructArray.Scanned)
        {
            return;
        }

        JgsStructArray payload = value.AsStructArray;
        payload.Scanned = false;
        JgsLifetime? tracker = Current ?? Find(value);
        if (tracker is null)
        {
            return;
        }

        foreach (Dictionary<string, JgsValue> element in payload.Elements)
        {
            tracker.ReleaseValues(element);
        }
    }

    /// <summary>
    /// Counts a container's hold on <paramref name="child"/>. A container wrapper already counted
    /// (a slot's own wrapper, whose hold an earlier store assumed) is not counted twice unless
    /// <paramref name="force"/> — a detach shares the very same wrappers with the old payload.
    /// </summary>
    private static void RetainChild(JgsValue? child, ref JgsLifetime? tracker, bool force)
    {
        if (child is null || !MayTrack(child))
        {
            return;
        }

        if (!force && child.Counted && IsCountedContainer(child))
        {
            return;
        }

        tracker ??= Find(child);
        tracker?.Retain(child, null);
    }

    /// <summary>
    /// <paramref name="container"/>'s storage (a cell's slots, a struct's or value object's
    /// fields, a handle's property, a dictionary's value cell) had <paramref name="old"/> replaced
    /// by <paramref name="value"/> through M7's gate — the container is an entry's own wrapper,
    /// or the entry itself for a handle. A tracked value stored into a container nobody scanned
    /// scans the rest of it first, so every child's hold is counted before the container can die.
    /// </summary>
    internal static void Stored(JgsValue container, JgsValue? old, JgsValue value)
    {
        bool tracked = MayTrack(value);
        bool wasTracked = old is not null && MayTrack(old);
        if (!tracked && !wasTracked)
        {
            return;
        }

        JgsLifetime? tracker = Current ?? Find(value) ?? (old is null ? null : Find(old));
        if (tracker is null)
        {
            return;
        }

        if (tracked)
        {
            tracker.Scan(container, except: value);
            tracker.Retain(value, null);
        }

        if (wasTracked)
        {
            tracker.Release(old!, null);
        }
    }

    /// <summary>
    /// A container on the way to a store of a tracked value (<c>c</c> for <c>c{1}{2} = v</c>): it
    /// is scanned if it was not, so its own death reaches what was stored below it. The value
    /// itself is the inner container's to count, not this one's.
    /// </summary>
    internal static void OwnerOfTrackedStore(JgsValue owner, JgsValue value)
    {
        JgsLifetime? tracker = Current ?? Find(value);
        tracker?.Scan(owner, except: value);
    }

    /// <summary>
    /// A write through a temporary wrapper over one element of <paramref name="owner"/>, an
    /// entry's struct array (<c>s(k).f = v</c>): the array's own scan and count, since the
    /// temporary's are not the array's.
    /// </summary>
    internal static void StoredInto(JgsStructArray owner, JgsValue? old, JgsValue value)
    {
        bool tracked = MayTrack(value);
        bool wasTracked = old is not null && MayTrack(old);
        if (!tracked && !wasTracked)
        {
            return;
        }

        JgsLifetime? tracker = Current ?? Find(value) ?? (old is null ? null : Find(old));
        if (tracker is null)
        {
            return;
        }

        if (tracked)
        {
            if (!owner.Scanned)
            {
                owner.Scanned = true;
                foreach (Dictionary<string, JgsValue> element in owner.Elements)
                {
                    foreach (KeyValuePair<string, JgsValue> field in element)
                    {
                        JgsValue child = field.Value;
                        if (!ReferenceEquals(child, value) && MayTrack(child) && !(child.Counted && IsCountedContainer(child)))
                        {
                            tracker.Retain(child, null);
                        }
                    }
                }

                owner.Exact = 1; // an entry's own array, made writable on the way (M3)
            }

            tracker.Retain(value, null);
        }

        if (wasTracked)
        {
            tracker.Release(old!, null);
        }
    }

    /// <summary>A store's write-through of a tracked value: the container is scanned if it was not, and its count made exact.</summary>
    private void Scan(JgsValue container, JgsValue except)
    {
        switch (container.Type)
        {
            case JgsType.Cell or JgsType.Array:
            {
                if (container.Tracked)
                {
                    return;
                }

                container.Tracked = true;
                JgsValue[] slots = container.Slots;
                foreach (JgsValue child in slots)
                {
                    if (child is not null && !ReferenceEquals(child, except) && MayTrack(child)
                        && !(child.Counted && IsCountedContainer(child)))
                    {
                        Retain(child, null);
                    }
                }

                // After the gate the storage is this wrapper's alone (M3), and a write road only
                // writes through an entry: held once.
                SlotCounts.GetValue(slots, static _ => new StrongBox<int>(0)).Value = 1;
                container.Counted = true;
                return;
            }

            case JgsType.Struct:
            {
                JgsStructArray payload = container.AsStructArray;
                if (payload.Scanned)
                {
                    return;
                }

                payload.Scanned = true;
                foreach (Dictionary<string, JgsValue> element in payload.Elements)
                {
                    foreach (KeyValuePair<string, JgsValue> field in element)
                    {
                        JgsValue child = field.Value;
                        if (!ReferenceEquals(child, except) && MayTrack(child) && !(child.Counted && IsCountedContainer(child)))
                        {
                            Retain(child, null);
                        }
                    }
                }

                payload.Exact = 1;
                container.Counted = true;
                return;
            }

            case JgsType.Object:
            {
                JgsObject instance = container.AsObject;
                if (instance.Scanned)
                {
                    return;
                }

                instance.Scanned = true;
                foreach (KeyValuePair<string, JgsValue> field in instance.Fields)
                {
                    JgsValue child = field.Value;
                    if (!ReferenceEquals(child, except) && MayTrack(child) && !(child.Counted && IsCountedContainer(child)))
                    {
                        Retain(child, null);
                    }
                }

                // A handle's instance is counted from birth; a value object's storage is this
                // wrapper's alone after the gate, as a struct's is.
                if (!instance.Class.IsHandle)
                {
                    instance.Exact = 1;
                    container.Counted = true;
                }

                return;
            }
        }
    }

    /// <summary>
    /// M3 detached <paramref name="wrapper"/>: its old payload lost this holder and its new one,
    /// a shallow copy whose children the copy shares, holds every child once more. Nothing moves
    /// for a container that was never scanned.
    /// </summary>
    internal static void Detached(JgsValue wrapper, object oldPayload, object newPayload)
    {
        JgsLifetime? tracker = Current;
        switch (newPayload)
        {
            case JgsValue[] slots when wrapper.Tracked:
            {
                foreach (JgsValue child in slots)
                {
                    RetainChild(child, ref tracker, force: true);
                }

                SlotCounts.GetValue(slots, static _ => new StrongBox<int>(0)).Value = wrapper.Counted ? 1 : 0;
                if (wrapper.Counted && SlotCounts.TryGetValue((JgsValue[])oldPayload, out StrongBox<int>? old)
                    && old.Value > 0 && --old.Value == 0)
                {
                    tracker?.Defer(oldPayload);
                }

                return;
            }

            case JgsStructArray structs when ((JgsStructArray)oldPayload).Scanned:
            {
                structs.Scanned = true;
                foreach (Dictionary<string, JgsValue> element in structs.Elements)
                {
                    foreach (KeyValuePair<string, JgsValue> field in element)
                    {
                        RetainChild(field.Value, ref tracker, force: true);
                    }
                }

                structs.Exact = wrapper.Counted ? 1 : 0;
                var former = (JgsStructArray)oldPayload;
                if (wrapper.Counted && former.Exact > 0 && --former.Exact == 0)
                {
                    tracker?.Defer(former);
                }

                return;
            }

            case JgsObject instance when ((JgsObject)oldPayload).Scanned:
            {
                instance.Scanned = true;
                instance.Wrapped = true;
                foreach (KeyValuePair<string, JgsValue> field in instance.Fields)
                {
                    RetainChild(field.Value, ref tracker, force: true);
                }

                instance.Exact = wrapper.Counted ? 1 : 0;
                var former = (JgsObject)oldPayload;
                if (wrapper.Counted && former.Exact > 0 && --former.Exact == 0)
                {
                    tracker?.Defer(former);
                }

                return;
            }
        }
    }

    /// <summary>
    /// A call's frame ended. Its variables are released at the caller's next statement boundary
    /// — by then the caller has bound the outputs it kept — unless a nested function's handle
    /// escaped, in which case the workspace lives on until the last such handle is released.
    /// </summary>
    internal void FrameExited(JgsEnvironment frame)
    {
        frame.Exited = true;
        if (AnyLive || frame.NeedsRelease)
        {
            Defer(frame);
        }
    }

    /// <summary>
    /// <c>delete(obj)</c> ran on a handle: its properties' contents are released at once
    /// (measured: <c>delete(h)</c> on a holder logs <c>H;I;J</c> before the next statement), and
    /// the destructor-bearing count no longer counts it.
    /// </summary>
    internal static void ObjectDeleted(JgsObject instance)
    {
        if (instance.Class.HasDestructor && !instance.DiedCounted)
        {
            instance.DiedCounted = true;
            Interlocked.Decrement(ref s_live);
        }

        (Current ?? instance.Class.Interpreter.Lifetimes).ReleaseFields(instance);
    }

    /// <summary>A handle object with a destructor was made: it counts as alive until destroyed.</summary>
    internal static void Register(JgsObject instance)
    {
        if (instance.Class.HasDestructor)
        {
            Interlocked.Increment(ref s_live);
        }
    }

    // --- the queue -----------------------------------------------------------------------------

    private void Defer(object payload) => _pending.Add((payload, _interpreter.BlockDepth));

    /// <summary>
    /// Examines every queued check made at <paramref name="depth"/> or deeper — the statement
    /// that made it, or one it ran, has completed — and destroys what nothing holds. A destructor
    /// runs script code; what it queues at this depth is examined in the same pass.
    /// </summary>
    internal void Drain(int depth)
    {
        if (_draining)
        {
            return;
        }

        _draining = true;
        try
        {
            bool any = true;
            while (any)
            {
                any = false;
                for (int i = 0; i < _pending.Count; i++)
                {
                    (object payload, int at) = _pending[i];
                    if (at < depth)
                    {
                        continue;
                    }

                    _pending.RemoveAt(i);
                    i--;
                    any = true;
                    Check(payload);
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private void Check(object payload)
    {
        switch (payload)
        {
            case JgsObject instance:
                if (instance.Exact == 0)
                {
                    Destroy(instance);
                }

                break;
            case JgsStructArray structs:
                if (structs.Exact == 0 && !structs.Released)
                {
                    // A listener made by listener() ends with its last handle (#107); a builtin
                    // handle's contents are otherwise outside the model.
                    structs.Released = true;
                    if (!JgsBuiltins.OnListenerUnreferenced(structs) && !structs.External)
                    {
                        ReleaseElements(structs);
                    }
                }

                break;
            case JgsValue[] slots:
                if (SlotCounts.TryGetValue(slots, out StrongBox<int>? box) && box.Value == 0)
                {
                    box.Value = Dead;
                    ReleaseValues(slots);
                }

                break;
            case AnonymousFunction anonymous:
                if (anonymous.Exact == 0 && !anonymous.Released)
                {
                    anonymous.Released = true;
                    ReleaseWorkspace(anonymous.Captured);
                }

                break;
            case JgsEnvironment workspace:
                if (workspace.Exited && workspace.Escapes == 0 && !workspace.Destroyed)
                {
                    workspace.Destroyed = true;
                    ReleaseWorkspace(workspace);
                }

                break;
        }
    }

    private void Destroy(JgsObject instance)
    {
        if (instance.Class.IsHandle)
        {
            // A handle with a destructor, or with listeners, is destroyed in earnest: the delete
            // method runs, the object is marked, ObjectBeingDestroyed is raised. One with neither
            // is unobservable once nothing holds it, so only what it held is released — which
            // keeps a store road the count did not see from ending an object a name still reaches.
            if (!instance.Deleted && (instance.Class.HasDestructor || instance.Listeners is { Count: > 0 }))
            {
                _interpreter.RunDestructor(instance); // marks the object deleted and raises ObjectBeingDestroyed
            }

            if (instance.Class.HasDestructor && !instance.DiedCounted)
            {
                instance.DiedCounted = true;
                Interlocked.Decrement(ref s_live);
            }
        }

        ReleaseFields(instance);
    }

    /// <summary>Releases what an object's properties hold, once.</summary>
    private void ReleaseFields(JgsObject instance)
    {
        if (instance.FieldsReleased || !instance.Scanned)
        {
            return;
        }

        instance.FieldsReleased = true;
        ReleaseValues(instance.Fields);
    }

    private void ReleaseElements(JgsStructArray structs)
    {
        if (!structs.Scanned)
        {
            return;
        }

        foreach (Dictionary<string, JgsValue> element in structs.Elements)
        {
            ReleaseValues(element);
        }
    }

    /// <summary>
    /// Direct handles first, in order, then the containers beside them (measured order). A
    /// release only moves counts and queues checks, so the storage walked is never changed under
    /// the walk and needs no copy.
    /// </summary>
    private void ReleaseValues(JgsValue[] values)
    {
        foreach (JgsValue child in values)
        {
            if (child is not null && IsHandleLike(child) && MayTrack(child))
            {
                Release(child, null);
            }
        }

        foreach (JgsValue child in values)
        {
            if (child is not null && !IsHandleLike(child) && MayTrack(child))
            {
                Release(child, null);
            }
        }
    }

    /// <summary><see cref="ReleaseValues(JgsValue[])"/> over a struct element's or an object's fields, in field order.</summary>
    private void ReleaseValues(Dictionary<string, JgsValue> fields)
    {
        foreach (KeyValuePair<string, JgsValue> field in fields)
        {
            if (field.Value is not null && IsHandleLike(field.Value) && MayTrack(field.Value))
            {
                Release(field.Value, null);
            }
        }

        foreach (KeyValuePair<string, JgsValue> field in fields)
        {
            if (field.Value is not null && !IsHandleLike(field.Value) && MayTrack(field.Value))
            {
                Release(field.Value, null);
            }
        }
    }

    /// <summary>Releases a workspace's variables in first-declaration order (measured).</summary>
    private void ReleaseWorkspace(JgsEnvironment workspace)
    {
        foreach ((string _, JgsValue value) in workspace.Locals)
        {
            if (MayTrack(value))
            {
                Release(value, workspace);
            }
        }
    }

    /// <summary>
    /// The run ended: every queued check is examined, then the base workspace is destroyed, its
    /// variables released by name (measured in R2025b's <c>-batch</c> exit: <c>x</c>, <c>y</c>,
    /// <c>c</c> destroyed as <c>c;x;y</c>), and what that queues examined too.
    /// </summary>
    internal void RunEnded(JgsEnvironment baseWorkspace, IReadOnlyDictionary<string, JgsValue> pristine)
    {
        Drain(0);
        foreach ((string name, JgsValue value) in baseWorkspace.Locals.OrderBy(static p => p.Key, StringComparer.Ordinal).ToArray())
        {
            if ((!pristine.TryGetValue(name, out JgsValue? original) || !ReferenceEquals(original, value)) && MayTrack(value))
            {
                Release(value, baseWorkspace);
            }
        }

        Drain(0);
    }

    /// <summary>The tracker a value belongs to, through the interpreter that made it, or null.</summary>
    private static JgsLifetime? Find(JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Object:
                return value.AsObject.Class.Interpreter.Lifetimes;
            case JgsType.Function:
                return value.AsCallable switch
                {
                    AnonymousFunction anonymous => anonymous.Interpreter.Lifetimes,
                    UserFunction user => user.Interpreter.Lifetimes,
                    NamedHandle named => Find(JgsValue.Function(named.Captured)),
                    BoundMethod bound => Find(bound.Receiver),
                    _ => null,
                };
            case JgsType.Cell or JgsType.Array:
                foreach (JgsValue child in value.Slots)
                {
                    if (child is not null && MayTrack(child) && Find(child) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JgsType.Struct:
                foreach (Dictionary<string, JgsValue> element in value.AsStructArray.Elements)
                {
                    foreach (KeyValuePair<string, JgsValue> field in element)
                    {
                        if (MayTrack(field.Value) && Find(field.Value) is { } found)
                        {
                            return found;
                        }
                    }
                }

                return null;
            default:
                return null;
        }
    }
}
