using System.Dynamic;
using System.Text;
using JGraph.Deploy;
using JGraph.MATLAB.Exceptions;
using JGraph.Runtime;
using JGraph.Scripting.Jgs;

namespace JGraph.MATLAB.Types;

/// <summary>
/// What a running MATLAB answers through (R2025b's MathWorks.MATLAB.Types.MATLABProvider): a member
/// called on it calls the MATLAB function of that name, a <see cref="RunOptions"/> first saying how,
/// and named arguments passed as name-value pairs after the rest; a member read on it is a package,
/// a class or an enumeration's name to go on from.
/// </summary>
public abstract class MATLABProvider : DynamicObject, IDisposable
{
    /// <summary>
    /// The functions a client may call besides the component's own (k9client: call.eq): the
    /// operators the generated classes call, keyHash their GetHashCode calls (which R2025b refuses,
    /// leaving every generated GetHashCode failing - a divergence), and the engine's property access.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "eq", "ne", "lt", "gt", "le", "ge", "keyHash", GetPropertyName, SetPropertyName,
    };

    private const string GetPropertyName = "matlab.internal.engine.getProperty";

    /// <summary>The operators' functions, by the symbols R2025b's reports name them with.</summary>
    private static readonly Dictionary<string, string> Operators = new(StringComparer.Ordinal)
    {
        ["eq"] = "==", ["ne"] = "~=", ["lt"] = "<", ["gt"] = ">", ["le"] = "<=", ["ge"] = ">=",
    };
    private const string SetPropertyName = "matlab.internal.engine.setProperty";

    private protected MATLABProvider()
    {
    }

    /// <summary>Finalizes the instance.</summary>
    ~MATLABProvider()
    {
        Dispose(false);
    }

    /// <summary>Whether this instance has been disposed and can no longer be called.</summary>
    public bool Disposed { get; private set; }

    /// <summary>The component this instance calls into.</summary>
    internal abstract Component Component { get; }

    /// <summary>Disposes this instance; a later call refuses.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cleans up the instance.</summary>
    protected virtual void Dispose(bool disposing)
    {
        Disposed = true;
    }

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = new MATLABPackage(this, binder.Name);
        return true;
    }

    /// <inheritdoc/>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        result = Invoke(binder.Name, args, binder.CallInfo.ArgumentNames, method: false);
        return true;
    }

    /// <summary>Refuses a disposed instance as R2025b's does, naming MATLABProvider (ThrowIf(bool, object) would name the string's type).</summary>
    private void ThrowIfDisposed()
    {
        if (Disposed)
        {
            throw new ObjectDisposedException("MATLABProvider");
        }
    }

    /// <summary>Runs <paramref name="work"/> on the engine thread with the component live, and waits.</summary>
    internal T Engine<T>(Func<ComponentSession, T> work)
    {
        ThrowIfDisposed();
        return Component.RunOnEngine(work).GetAwaiter().GetResult();
    }

    /// <summary>An object array's elements, each as a value of its own.</summary>
    internal MatlabValue[] ObjectElements(MatlabValue value) => Engine(session =>
    {
        var held = (JgsValue)((RuntimeOpaque)value.Data).Held;
        return held.IsObjectArray
            ? held.AsObjectArray.Elements.Select(e => MatlabValue.Wrap(JgsValue.Object(e), session)).ToArray()
            : [value];
    });

    /// <summary>
    /// The options of a property access on a MATLABObject, whose failure R2025b throws without
    /// writing its report (k9client: object.missing), as the interface's explicit call does write it.
    /// </summary>
    private static RunOptions Quiet() => new(Console.Out, TextWriter.Null, nargout: 1);

    /// <summary>A property of a MATLAB object, read as R2025b reads it, through matlab.internal.engine.getProperty.</summary>
    internal object GetProperty(MATLABObject obj, string name) =>
        Invoke(GetPropertyName, [Quiet(), obj, name], [], method: false);

    /// <summary>A property of a handle object, written through matlab.internal.engine.setProperty.</summary>
    internal void SetProperty(MATLABObject obj, string name, object value) =>
        Invoke(SetPropertyName, [Quiet(), obj, name, value], [], method: false);

    /// <summary>
    /// Calls the MATLAB function <paramref name="name"/>: a <see cref="RunOptions"/> first (after the
    /// object, for a method) says how; the arguments the call named are passed as name-value pairs
    /// after the others. Answers the output for one asked for, a <see cref="FevalTaskResult"/> for
    /// any other count, or a task of either.
    /// </summary>
    internal object Invoke(string name, object[] args, IReadOnlyCollection<string> argumentNames, bool method)
    {
        ThrowIfDisposed();
        int named = argumentNames?.Count ?? 0;
        var positional = args.Take(args.Length - named).ToList();
        var options = new RunOptions();
        int first = method ? 1 : 0;
        if (positional.Count > first && positional[first] is RunOptions given)
        {
            options = given;
            positional.RemoveAt(first);
        }

        var inputs = new List<object>(positional);
        int k = args.Length - named;
        foreach (string argumentName in argumentNames ?? [])
        {
            inputs.Add(argumentName);
            inputs.Add(args[k++]);
        }

        // R2025b answers a negative count with its C++ runtime's "vector too long"; it is the count's fault.
        if (options.Nargout < 0)
        {
            throw new ArgumentOutOfRangeException("nargout", options.Nargout, "The number of outputs must not be negative.");
        }

        Task<object> task = Start(name, [.. inputs], options.Nargout, options.Output, options.Error, options.CancelToken, method);
        return options.Async ? task : task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Starts the call on the engine thread, the task R2025b answers (an unwrapped one, the call's
    /// exception set on it rather than thrown, so a wait rethrows it).
    /// </summary>
    private Task<object> Start(string name, object[] inputs, int nargout, TextWriter output, TextWriter error, CancellationToken token, bool method) =>
        Task.Run(() =>
        {
            var done = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (token.IsCancellationRequested)
            {
                done.SetCanceled(token);
                return done.Task;
            }

            Task<(object Value, Exception Failure)> work = Component.RunOnEngine(
                session => Execute(session, name, inputs, nargout, output, error, token, method), token);
            work.ContinueWith(
                t =>
                {
                    if (t.IsCanceled || t.Exception?.InnerException is OperationCanceledException)
                    {
                        done.TrySetCanceled();
                    }
                    else if (t.IsFaulted)
                    {
                        done.TrySetException(t.Exception!.InnerException!);
                    }
                    else if (t.Result.Failure is { } failure)
                    {
                        done.TrySetException(failure);
                    }
                    else
                    {
                        done.TrySetResult(t.Result.Value);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return done.Task;
        });

    /// <summary>The call itself, on the engine thread: the arguments converted, the name checked, the function run.</summary>
    private (object Value, Exception Failure) Execute(
        ComponentSession session, string name, object[] inputs, int nargout, TextWriter output, TextWriter error, CancellationToken token, bool method)
    {
        JgsValue[] arguments = [.. inputs.Select(a => Conversion.ToEngine(a, this, session))];
        if (!Callable(session, name, arguments, method))
        {
            return (null, new MATLABExecutionException($"Function {name} not found in the component."));
        }

        // The report names an operator by its symbol and a method as Class/method (k9client: call.eq, handle.increment.nargout1).
        string shown = Operators.TryGetValue(name, out string symbol) ? symbol
            : method && arguments.Length > 0 && arguments[0].Type == JgsType.Object ? JgsBuiltins.ClassOf(arguments[0], JgsDialect.Matlab) + "/" + name
            : name;
        JgsValue[] outputs;
        try
        {
            outputs = name switch
            {
                GetPropertyName => [session.Interpreter.ReadProperty(arguments[0], JgsBuiltins.TextOf(arguments[1]), 0, 0)],
                SetPropertyName => [SetEngineProperty(session, arguments)],
                _ => session.CallEngine(name, nargout, arguments, output, error, token),
            };
        }
        catch (JgsException failure) when (name is GetPropertyName or SetPropertyName)
        {
            return (null, Failed(ComponentSession.Deployed(failure), shown, error));
        }
        catch (DeployedException failure)
        {
            return (null, Failed(failure, shown, error));
        }

        MATLABArray[] wrapped = [.. outputs.Take(nargout).Select(o => MATLABArray.Wrap(this, MatlabValue.Wrap(o, session)))];
        return nargout == 1 && wrapped.Length == 1 ? (wrapped[0], null) : (new FevalTaskResult(wrapped), null);
    }

    /// <summary>Writes a property of an object, answering the object it is now: a copy for a value object, the same for a handle.</summary>
    private static JgsValue SetEngineProperty(ComponentSession session, JgsValue[] arguments)
    {
        JgsValue target = arguments[0].Type == JgsType.Object && !arguments[0].ObjectClass.IsHandle
            ? session.Interpreter.CopyForBinding(arguments[0])
            : arguments[0];
        session.Interpreter.WriteProperty(target, JgsBuiltins.TextOf(arguments[1]), arguments[2], 0, 0);
        return target;
    }

    /// <summary>
    /// Whether a client may call <paramref name="name"/> (k9client: call.missing, call.builtin): the
    /// component's functions and its classes' static methods and enumeration members (which R2025b
    /// refuses, though its generated classes call them - a divergence), a method of the object a
    /// method call is made on, and the few functions the generated classes need.
    /// </summary>
    private static bool Callable(ComponentSession session, string name, JgsValue[] arguments, bool method)
    {
        if (Allowed.Contains(name))
        {
            return true;
        }

        if (method && arguments.Length > 0 && session.Interpreter.TryUserMethod(name, arguments[0], out _))
        {
            return true;
        }

        if (method && name is "delete" or "isvalid" && arguments.Length > 0 && arguments[0].Type == JgsType.Object && arguments[0].ObjectClass.IsHandle)
        {
            return true;
        }

        IReadOnlyList<CtfFunction> functions = session.Deployment.Manifest.PublicFunctions;
        if (functions.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
        {
            return true;
        }

        int dot = name.LastIndexOf('.');
        return dot > 0 && functions.Any(f => string.Equals(f.Name, name[..dot], StringComparison.Ordinal))
            && session.Interpreter.ClassForLoad(name[..dot]) is not null;
    }

    /// <summary>
    /// A MATLAB error as R2025b hands it to its client: the report written to the call's error
    /// writer, and a MATLABExecutionException of the identifier, message and stack (k9client: error.*).
    /// </summary>
    private static MATLABExecutionException Failed(DeployedException failure, string name, TextWriter error)
    {
        string report = failure.Stack.Count == 0 && !failure.Report.StartsWith("Error using ", StringComparison.Ordinal)
            ? $"Error using {name}\n{failure.Report}"
            : failure.Report;
        if (failure.Identifier.Length > 0 && report.EndsWith("\n" + failure.Identifier, StringComparison.Ordinal))
        {
            report = report[..^(failure.Identifier.Length + 1)];
        }

        error.Write(report.TrimEnd('\n') + "\n\n\n");
        error.Flush();

        var text = new StringBuilder();
        if (failure.Identifier.Length > 0)
        {
            text.Append(failure.Identifier).Append(": ");
        }

        text.Append(failure.Message);
        foreach (DeployedStackFrame frame in failure.Stack)
        {
            string shown = frame.Name[(frame.Name.LastIndexOf('>') + 1)..].Replace('/', '.');
            text.Append("\nError in ").Append(shown).Append(" (line ").Append(frame.Line).Append(") in file: ").Append(frame.File);
        }

        return new MATLABExecutionException(text.ToString());
    }
}
