using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.apps.AppBase</c>, the class every App Designer app is written under (U7 of the
/// app-building plan, ADR 0204). It is written from what R2025b's does, read and measured
/// (research C, fixture <c>u7_app</c>): five protected sealed methods the generated constructor and
/// <c>createComponents</c> call, and a <c>saveobj</c> and <c>loadobj</c> that refuse.
/// </summary>
/// <remarks>
/// What makes an app an app is its figure. <c>registerApp</c> gives the figure the app
/// (<c>RunningAppInstance</c>) and listens for the figure's going, which deletes the app; the
/// app's own generated <c>delete</c> deletes the figure. The figure and the callbacks
/// <c>createCallbackFcn</c> makes hold the app, which is why <c>clear app</c> leaves it running.
/// </remarks>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The class an App Designer app inherits from.</summary>
    public const string AppBase = "matlab.apps.AppBase";

    /// <summary>The property <c>registerApp</c> adds to an app's figure: the app.</summary>
    public const string RunningAppInstance = "RunningAppInstance";

    /// <summary>The property <c>registerApp</c> adds beside it: the file the app's class came from.</summary>
    public const string RunningInstanceFullFileName = "RunningInstanceFullFileName";

    private static readonly MemberAccess Protected = new(MemberAccessKind.Protected);

    /// <summary>
    /// The function <c>createCallbackFcn</c> answers, as R2025b writes it. The names in its body
    /// are captured from a scope made for each call, so the app is held by the handle.
    /// </summary>
    private static readonly AnonymousFnExpr CallbackWrapper = new(
        ["source", "event"],
        new CallExpr(Name("executeCallback"), [Name("ams"), Name("app"), Name("callback"), Name("requiresEventData"), Name("event")]))
    {
        Dialect = JgsDialect.Matlab,
    };

    private static VariableExpr Name(string name) => new(name);

    private static ClassdefStmt AppBaseDeclaration(Interpreter interpreter) =>
        new(AppBase, isHandle: true, [],
        [
            Method("createCallbackFcn", Protected, isSealed: true, (args, line, col) => CreateCallbackFcn(interpreter, args, line, col)),
            Method("runStartupFcn", Protected, isSealed: true, (args, line, col) => RunStartupFcn(interpreter, args, line, col), bindsAns: false),
            Method("registerApp", Protected, isSealed: true, (args, line, col) => RegisterApp(interpreter, args, line, col), bindsAns: false),
            Method("getRunningApp", Protected, isSealed: true, (args, line, col) => GetRunningApp(args, line, col)),
            Method("setAutoResize", Protected, isSealed: true, (args, line, col) => SetAutoResize(args, line, col), bindsAns: false),
            Method("saveobj", MemberAccess.Public, isSealed: false, (args, line, col) => SaveObj(interpreter, args, line, col)),
            new ClassMethod(new FnStmt("loadobj", ["s"], [], []) { Dialect = JgsDialect.Matlab }, Static: true)
            {
                Native = new BuiltinFunction("loadobj", static (_, line, col) => throw new JgsRuntimeException(line, col,
                    "MATLAB:appdesigner:appdesigner:LoadObjWarning",
                    $"Unable to load App Designer app object. Load not supported for {AppBase} objects.")),
            },
        ])
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = ["handle"],
        };

    private static JgsObject App(string verb, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < wanted)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > wanted)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        _ = verb;
        return args[0].AsObject;
    }

    /// <summary>
    /// <c>createCallbackFcn(app, @method, requiresEventData)</c>: the handle a component's callback
    /// property is given. Called with the component and its event, it calls the method with the
    /// app and the event, or with the app alone; the component is not passed on.
    /// </summary>
    private static JgsValue CreateCallbackFcn(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        _ = App("createCallbackFcn", args, 3, line, col);
        JgsEnvironment scope = interpreter.NewFileScope();
        scope.DeclareFunction("executeCallback", JgsValue.Function(new BuiltinFunction("executeCallback", ExecuteCallback) { BindsAnsAsStatement = false }));
        (string Name, JgsValue Value)[] captured =
        [
            ("ams", JgsMatrix.FromColumnMajor([], 0, 0)), ("app", args[0]), ("callback", args[1]), ("requiresEventData", args[2]),
        ];
        foreach ((string name, JgsValue value) in captured)
        {
            scope.Declare(name, interpreter.CopyForBinding(value));
        }

        return JgsValue.Function(AnonymousFunction.Create(CallbackWrapper, scope, interpreter));
    }

    /// <summary>What the handle from <c>createCallbackFcn</c> runs: <c>callback(app, event)</c>, or <c>callback(app)</c>.</summary>
    private static JgsValue ExecuteCallback(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 5 || args[2].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsCallbacks.Invoke(args[2].AsCallable, args[3].IsTruthy ? [args[1], args[4]] : [args[1]], line, col);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>runStartupFcn(app, @startupFcn)</c>: the startup function, called with the app. A figure
    /// whose <c>HandleVisibility</c> is <c>'callback'</c> is <c>'on'</c> for the length of the call,
    /// so that the startup function's <c>gcf</c> and <c>findobj</c> find it.
    /// </summary>
    private static JgsValue RunStartupFcn(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject app = App("runStartupFcn", args, 2, line, col);
        if (args[1].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                "Invalid argument at position 2. Value must be of type function_handle or be convertible to function_handle.");
        }

        JgsHandleEntry? figure = FigureOf(app);
        bool shown = figure is { HandleVisibility: "callback" };
        if (shown)
        {
            figure!.HandleVisibility = "on";
        }

        try
        {
            JgsCallbacks.Invoke(args[1].AsCallable, [args[0]], line, col);
        }
        finally
        {
            if (shown && figure!.HandleVisibility == "on")
            {
                figure.HandleVisibility = "callback";
            }
        }

        _ = interpreter;
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>registerApp(app, fig)</c>: the figure is given the app and the file its class came from,
    /// and the app is deleted when the figure is.
    /// </summary>
    private static JgsValue RegisterApp(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject app = App("registerApp", args, 2, line, col);
        if (!JgsHandleRegistry.TryGet(args[1], out JgsHandleEntry? entry) || entry.Target is not FigureModel)
        {
            // R2025b adds a property to whatever it is handed, and a number has none to add to.
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function 'addprop' for input arguments of type '{JgsBuiltins.ClassOf(args[1], JgsDialect.Matlab)}'.");
        }

        JgsLifetime.Pin(args[0]); // the figure holds the app for as long as there is a figure
        entry.AddedProperties ??= new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        entry.AddedProperties[RunningAppInstance] = args[0];
        entry.AddedProperties[RunningInstanceFullFileName] = JgsValue.Str(interpreter.FileOfClass(app.Class));
        if (interpreter.Host is { } host)
        {
            JgsBuiltins.AddGraphicsDestroyedHook(entry, host, () => interpreter.RunDestructor(app));
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>getRunningApp(app)</c>: the app of this class that is already running - the first figure
    /// whose <c>RunningAppInstance</c> is of the class - or an empty. A singleton app's
    /// constructor asks before it builds anything.
    /// </summary>
    private static JgsValue GetRunningApp(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsObject app = App("getRunningApp", args, 1, line, col);

        // Newest first, which is the order R2025b's findall lists figures in.
        foreach (GraphObject figure in JgsGraphicsProperties.RootFigures(hidden: true).Reverse())
        {
            if (JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry) && !figure.BeingDeleted
                && entry.AddedProperties is { } added && added.TryGetValue(RunningAppInstance, out JgsValue? running)
                && running.Type == JgsType.Object && !running.AsObject.Deleted
                && string.Equals(running.AsObject.Class.Name, app.Class.Name, StringComparison.Ordinal))
            {
                return running;
            }
        }

        return JgsMatrix.FromColumnMajor([], 0, 0);
    }

    /// <summary><c>setAutoResize(app, fig, value)</c>: the figure's <c>AutoResizeChildren</c>.</summary>
    private static JgsValue SetAutoResize(IReadOnlyList<JgsValue> args, int line, int col)
    {
        _ = App("setAutoResize", args, 3, line, col);
        JgsGraphicsProperties.Set(JgsHandleRegistry.Require(args[1], line, col), "AutoResizeChildren", args[2], line, col);
        return JgsValue.Null;
    }

    /// <summary><c>saveobj(app)</c>: an app is not saved. R2025b warns and stores an empty.</summary>
    private static JgsValue SaveObj(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        _ = App("saveobj", args, 1, line, col);
        const string id = "MATLAB:appdesigner:appdesigner:SaveObjWarning";
        string text = $"Unable to save App Designer app object. Save not supported for {AppBase} objects.";
        if (interpreter.Host is { } host)
        {
            host.Warnings.Record(id, text);
            if (host.Warnings.IsOn(id))
            {
                host.WriteWarning("Warning: " + text);
            }
        }

        return JgsMatrix.FromColumnMajor([], 0, 0);
    }

    /// <summary>The figure an app is registered with, or null before <c>registerApp</c>.</summary>
    private static JgsHandleEntry? FigureOf(JgsObject app)
    {
        foreach (GraphObject figure in JgsGraphicsProperties.RootFigures(hidden: true))
        {
            if (JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry)
                && entry.AddedProperties is { } added && added.TryGetValue(RunningAppInstance, out JgsValue? running)
                && running.Type == JgsType.Object && ReferenceEquals(running.AsObject, app))
            {
                return entry;
            }
        }

        return null;
    }
}
