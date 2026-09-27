using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// The listeners on one event of one .NET object (interop plan, stage 5, ADR 0178), and the one
/// handler that stands for all of them on the .NET side: R2025b adds a single handler to the event
/// however many listeners there are (<c>FiredHandlerCount</c> is 1 with two), and removes it when the
/// last listener goes — <c>delete(lh)</c>, or clearing the last handle to one <c>listener</c> made
/// (probe5a). <c>ObjectBeingDestroyed</c>, which is MATLAB's and not the object's, has listeners and
/// no handler; <c>delete(obj)</c> raises it.
/// </summary>
/// <remarks>
/// The handler is compiled over the event's own delegate type, which must have R2025b's standard shape
/// — <c>void (object sender, TArgs e)</c> with <c>TArgs</c> an <c>EventArgs</c>; any other is refused
/// with <c>MATLAB:NET:UnsupportedDelegateType</c>. Raised on the script thread (inside a .NET call the
/// script made) it runs the listeners at once; raised on another thread it queues them for the
/// script thread's next drain point (<see cref="NetCallbackQueue"/>) and returns.
/// </remarks>
internal sealed class NetEventSubscription
{
    /// <summary>The pseudo event every .NET handle has, raised by <c>delete</c>.</summary>
    public const string ObjectBeingDestroyed = "ObjectBeingDestroyed";

    private static readonly ConditionalWeakTable<object, Dictionary<string, NetEventSubscription>> Sources = new();

    private static readonly MethodInfo RaisedMethod =
        typeof(NetEventSubscription).GetMethod(nameof(Raised), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly object _target;
    private readonly EventInfo? _event;
    private readonly NetCallbackQueue _queue;
    private Delegate? _handler;

    private NetEventSubscription(object target, string name, EventInfo? info, Type argsType, string className)
    {
        _target = target;
        _event = info;
        EventName = name;
        ArgsType = argsType;
        ClassName = className;
        _queue = NetCallbackQueue.ForCurrentThread();
    }

    /// <summary>The event's name.</summary>
    public string EventName { get; }

    /// <summary>The type the event declares its arguments as, which is how a callback receives them.</summary>
    public Type ArgsType { get; }

    /// <summary>The source's class, as the warning for a failing callback names it.</summary>
    public string ClassName { get; }

    /// <summary>The listeners, oldest first.</summary>
    public List<JgsListener> Listeners { get; } = new();

    /// <summary>
    /// The subscription for <paramref name="name"/> on <paramref name="net"/>'s object, made on first
    /// use. Null when the event is not there to hear: <c>MATLAB:class:invalidEvent</c>; a delegate of
    /// another shape is refused here.
    /// </summary>
    public static NetEventSubscription? For(NetObject net, string name, int line, int col)
    {
        object target = net.Target!;
        Dictionary<string, NetEventSubscription> events = Sources.GetOrCreateValue(target);
        lock (events)
        {
            if (events.TryGetValue(name, out NetEventSubscription? known))
            {
                return known;
            }
        }

        NetEventSubscription made;
        if (name == ObjectBeingDestroyed)
        {
            made = new NetEventSubscription(target, name, null, typeof(object), net.ClassName);
        }
        else
        {
            if (net.Type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance) is not { } info)
            {
                return null;
            }

            MethodInfo invoke = info.EventHandlerType!.GetMethod("Invoke")!;
            ParameterInfo[] parameters = invoke.GetParameters();
            if (invoke.ReturnType != typeof(void) || parameters.Length != 2 || parameters[0].ParameterType != typeof(object)
                || !typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:NET:UnsupportedDelegateType",
                    ".NET events with nonstandard delegate definition are not supported in MATLAB.");
            }

            made = new NetEventSubscription(target, name, info, parameters[1].ParameterType, net.ClassName);
        }

        lock (events)
        {
            return events.TryAdd(name, made) ? made : events[name];
        }
    }

    /// <summary>The subscription <c>delete</c> raises on <paramref name="target"/>, when anyone listens to it.</summary>
    public static NetEventSubscription? Existing(object target, string name)
    {
        if (!Sources.TryGetValue(target, out Dictionary<string, NetEventSubscription>? events))
        {
            return null;
        }

        lock (events)
        {
            return events.TryGetValue(name, out NetEventSubscription? known) ? known : null;
        }
    }

    /// <summary>Adds a listener; the first one puts the handler on the .NET event.</summary>
    public void Add(JgsListener listener)
    {
        Listeners.Add(listener);
        if (_event is not null && _handler is null)
        {
            _handler = Handler();
            _event.AddEventHandler(_target, _handler);
        }
    }

    /// <summary>Takes a listener off; the last one takes the handler off the .NET event.</summary>
    public void Remove(JgsListener listener)
    {
        Listeners.Remove(listener);
        if (Listeners.Count == 0 && _event is not null && _handler is not null)
        {
            _event.RemoveEventHandler(_target, _handler);
            _handler = null;
        }
    }

    /// <summary>A handler of the event's delegate type that calls <see cref="Raised"/>.</summary>
    private Delegate Handler() => HandlerFactories.GetOrAdd(_event!.EventHandlerType!, HandlerFactory)(this);

    /// <summary>One compiled handler factory per delegate type (a compile costs hundreds of microseconds).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<NetEventSubscription, Delegate>> HandlerFactories = new();

    /// <summary>Drops the handler factories of event types a recompilation retired (ADR 0179).</summary>
    public static void Forget(Func<Type, bool> gone)
    {
        foreach (Type key in HandlerFactories.Keys.Where(gone))
        {
            HandlerFactories.TryRemove(key, out _);
        }
    }

    private static Func<NetEventSubscription, Delegate> HandlerFactory(Type delegateType)
    {
        ParameterInfo[] parameters = delegateType.GetMethod("Invoke")!.GetParameters();
        ParameterExpression subscription = Expression.Parameter(typeof(NetEventSubscription), "subscription");
        ParameterExpression sender = Expression.Parameter(typeof(object), "sender");
        ParameterExpression args = Expression.Parameter(parameters[1].ParameterType, "e");
        MethodCallExpression call = Expression.Call(subscription, RaisedMethod, sender, Expression.Convert(args, typeof(object)));
        LambdaExpression handler = Expression.Lambda(delegateType, call, sender, args);
        return Expression.Lambda<Func<NetEventSubscription, Delegate>>(Expression.Convert(handler, typeof(Delegate)), subscription).Compile();
    }

    /// <summary>What the handler does: the listeners now on the script thread, queued from any other.</summary>
    private void Raised(object? sender, object? args)
    {
        if (_queue.OnScriptThread)
        {
            _queue.RunInline(() =>
            {
                JgsBuiltins.FireNetEvent(this, sender, args);
                return 0;
            });
        }
        else
        {
            _queue.Post(() => JgsBuiltins.FireNetEvent(this, sender, args));
        }
    }
}
