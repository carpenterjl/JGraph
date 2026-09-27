using System.Linq.Expressions;
using System.Reflection;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// .NET delegates made from function handles (interop plan, stage 5, ADR 0178): <c>JGTest.Unary(@f)</c>,
/// <c>System.Action(@f)</c>, <c>NET.createGeneric('System.Func', {…}, @f)</c>, and a function handle
/// passed where a method takes a delegate.
/// </summary>
/// <remarks>
/// <para>
/// The delegate is compiled from an expression over the delegate type's own <c>Invoke</c> signature,
/// so it is a delegate of exactly that type, which .NET can combine, remove and invoke like any
/// other. Its body hands the arguments to a <see cref="Bridge"/>: the inputs — every parameter but an
/// <c>out</c> one — are converted as a member of their declared type answers them, and the handle is
/// asked for the return value (when not <c>void</c>) and then one output per <c>ref</c> or <c>out</c>
/// parameter, as a method call answers them (probe5b/5c: <c>@(a) deal(a + 1, 7)</c> for
/// <c>void RefOut(ref double a, out double b)</c>; an anonymous <c>error</c> asked for one output is
/// <c>MATLAB:maxlhs</c>). What comes back converts as a property write converts it
/// (<see cref="NetInvoke.DelegateResult"/>).
/// </para>
/// <para>
/// <b>Threads.</b> Invoked on the script thread — by a .NET call the script made, or by
/// <c>d.Invoke(x)</c> — the handle runs at once. Invoked on any other thread, the call waits in the
/// script thread's <see cref="NetCallbackQueue"/> for a drain point, and the invoking thread blocks
/// until it has run (R2025b: <c>ApplyLater</c>'s task completes at <c>pause</c>, not during a busy loop).
/// </para>
/// <para>
/// <b>Errors.</b> A script error in the handle (or in converting its answer) leaves the delegate
/// wrapped in <see cref="NetScriptFault"/>, crosses whatever .NET code invoked it, and comes out of the
/// script's .NET call as itself (<see cref="NetInvoke.Raise"/>): R2025b reports the callback's own
/// <c>MException</c>, not a <c>NET.NetException</c>.
/// </para>
/// </remarks>
internal static class NetDelegates
{
    private static readonly MethodInfo BridgeCall = typeof(Bridge).GetMethod(nameof(Bridge.Call))!;

    /// <summary>Whether a script can make <paramref name="type"/> from a function handle: a concrete delegate type.</summary>
    public static bool IsDelegateType(Type type) =>
        typeof(Delegate).IsAssignableFrom(type) && type != typeof(Delegate) && type != typeof(MulticastDelegate)
        && !type.ContainsGenericParameters;

    /// <summary>
    /// One compiled factory per delegate type: compiling an expression costs hundreds of microseconds,
    /// which a handle passed to <c>Invoker.Apply</c> in a loop paid on every call (355 µs, ADR 0178).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<Bridge, Delegate>> Factories = new();

    /// <summary>Drops the factories of delegate types a recompilation retired (ADR 0179).</summary>
    public static void Forget(Func<Type, bool> gone)
    {
        foreach (Type key in Factories.Keys.Where(gone))
        {
            Factories.TryRemove(key, out _);
        }
    }

    /// <summary>A delegate of <paramref name="delegateType"/> that calls <paramref name="callback"/>.</summary>
    public static Delegate Create(Type delegateType, IJgsCallable callback)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke")!;
        var bridge = new Bridge(callback, invoke, NetCallbackQueue.ForCurrentThread(), NetNames.ClassName(delegateType));
        JgsLifetime.Pin(JgsValue.Function(callback)); // the delegate holds the handle for as long as .NET does
        return Factories.GetOrAdd(delegateType, Factory)(bridge);
    }

    /// <summary>Compiles <c>bridge =&gt; (the delegate's parameters) =&gt; bridge.Call(…)</c> for one delegate type.</summary>
    private static Func<Bridge, Delegate> Factory(Type delegateType)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke")!;
        ParameterInfo[] parameters = invoke.GetParameters();
        ParameterExpression bridgeParameter = Expression.Parameter(typeof(Bridge), "bridge");
        ParameterExpression[] declared = parameters.Select(static p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        ParameterExpression values = Expression.Variable(typeof(object[]), "values");
        var body = new List<Expression>
        {
            Expression.Assign(values, Expression.NewArrayInit(typeof(object), declared.Select(static (p, i) =>
                (Expression)Expression.Convert(p, typeof(object))))),
        };

        ParameterExpression answer = Expression.Variable(typeof(object), "answer");
        body.Add(Expression.Assign(answer, Expression.Call(bridgeParameter, BridgeCall, values)));
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType.IsByRef)
            {
                body.Add(Expression.Assign(declared[i],
                    Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(i)), declared[i].Type)));
            }
        }

        if (invoke.ReturnType != typeof(void))
        {
            body.Add(Expression.Convert(answer, invoke.ReturnType));
        }

        BlockExpression block = invoke.ReturnType == typeof(void)
            ? Expression.Block(typeof(void), [values, answer], body)
            : Expression.Block(invoke.ReturnType, [values, answer], body);
        LambdaExpression made = Expression.Lambda(delegateType, block, declared);
        return Expression.Lambda<Func<Bridge, Delegate>>(Expression.Convert(made, typeof(Delegate)), bridgeParameter).Compile();
    }

    /// <summary>What a compiled delegate calls: the handle, on the script thread, with MATLAB's values.</summary>
    internal sealed class Bridge(IJgsCallable callback, MethodInfo invoke, NetCallbackQueue queue, string delegateName)
    {
        private readonly ParameterInfo[] _parameters = invoke.GetParameters();

        /// <summary>
        /// Called with the delegate's arguments (an <c>out</c> one as its default); answers the return
        /// value and leaves each <c>ref</c> and <c>out</c> parameter's new value in <paramref name="values"/>.
        /// </summary>
        public object? Call(object?[] values) =>
            queue.OnScriptThread
                ? queue.RunInline(() => Run(values))
                : queue.Call(() => Run(values), "a " + delegateName);

        private object? Run(object?[] values)
        {
            try
            {
                var inputs = new List<JgsValue>(_parameters.Length);
                var byRef = new List<int>();
                for (int i = 0; i < _parameters.Length; i++)
                {
                    ParameterInfo parameter = _parameters[i];
                    Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
                    if (parameter.ParameterType.IsByRef)
                    {
                        byRef.Add(i);
                    }

                    if (!parameter.IsOut)
                    {
                        inputs.Add(NetConvert.ToMatlab(values[i], type));
                    }
                }

                bool returns = invoke.ReturnType != typeof(void);
                int wanted = (returns ? 1 : 0) + byRef.Count;
                JgsValue[] outputs = callback is IJgsMultiCallable several
                    ? several.CallMultiple(inputs, wanted, 0, 0)
                    : [callback.Call(inputs, 0, 0)];

                int next = 0;
                object? answer = null;
                if (returns)
                {
                    answer = NetInvoke.DelegateResult(Output(outputs, next++), invoke.ReturnType, 0, 0);
                }

                foreach (int i in byRef)
                {
                    values[i] = NetInvoke.DelegateResult(Output(outputs, next++), _parameters[i].ParameterType.GetElementType()!, 0, 0);
                }

                return answer;
            }
            catch (Exception fault) when (fault is JgsException or OperationCanceledException || ScriptExitException.Unwrap(fault) is not null)
            {
                throw new NetScriptFault(fault);
            }
        }

        private static JgsValue Output(JgsValue[] outputs, int at) =>
            at < outputs.Length
                ? outputs[at]
                : throw new JgsRuntimeException(0, 0, "MATLAB:maxlhs", "Too many output arguments.");
    }
}

/// <summary>
/// A script's own failure — an error, Stop, <c>exit</c> — carried out of a delegate through the .NET
/// code that invoked it, to be rethrown as itself where the script's .NET call returns (ADR 0178).
/// </summary>
internal sealed class NetScriptFault(Exception script) : Exception(script.Message, script);
