namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's side of a class that inherits (U6 of the app-building plan, ADR 0203): what
/// <c>name@Superclass(…)</c> calls, written inside a method.
/// </summary>
/// <remarks>
/// The reference means one of two things, and the function it is written in says which. In a
/// constructor, with the constructor's own output before the <c>@</c>, it is the superclass's
/// constructor run on the object being built; anywhere else it is the superclass's method of that
/// name, which is how an override reaches the method it replaces. Either way the parser has put it
/// in the callee of a call, so the arguments and the outputs travel the ordinary road.
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>The callable <c>name@Superclass</c> stands for in <paramref name="env"/>.</summary>
    private JgsValue EvaluateSuperRef(SuperRefExpr reference, JgsEnvironment env)
    {
        JgsClass running = env.ClassContext
            ?? throw new JgsRuntimeException(reference.Line, reference.Column,
                $"'{reference.Name}@{reference.Superclass}' reaches a superclass, so it is written inside a class's method.");
        FnStmt? function = env.EnclosingFunction;
        bool constructing = running.IsConstructor(function) && function!.Outputs.Count == 1
            && function.Outputs[0] == reference.Name;

        // handle and event.EventData have a constructor that does nothing here, and no method a
        // subclass's override could be reaching for.
        if (reference.Superclass is "handle" or "event.EventData" && running.IsA(reference.Superclass))
        {
            return constructing
                ? JgsValue.Function(new SuperConstructorCall(null, env, reference.Name, this))
                : throw new JgsRuntimeException(reference.Line, reference.Column,
                    $"'{reference.Name}@{reference.Superclass}': '{reference.Superclass}' has no method '{reference.Name}' to call here.");
        }

        JgsClass super = running.Ancestor(reference.Superclass)
            ?? throw new JgsRuntimeException(reference.Line, reference.Column,
                $"'{reference.Superclass}' is not a superclass of '{running.Name}'.");
        if (constructing)
        {
            if (!running.Supers.Contains(super))
            {
                throw new JgsRuntimeException(reference.Line, reference.Column,
                    $"'{reference.Superclass}' is not a direct superclass of '{running.Name}', so its constructor is not "
                    + $"'{running.Name}''s to call.");
            }

            return JgsValue.Function(new SuperConstructorCall(super, env, reference.Name, this));
        }

        if (FindMethod(super, reference.Name, running, true, out ClassMethod? method) != MethodAnswer.Found
            || method is null || method.Abstract)
        {
            throw new JgsRuntimeException(reference.Line, reference.Column,
                $"'{reference.Superclass}' has no method '{reference.Name}' that '{running.Name}' can call.");
        }

        return JgsValue.Function(super.Callable(method));
    }

    /// <summary>
    /// <c>@obj.method</c>, when the first word of the dotted name is a variable holding an object:
    /// the anonymous function <c>@(varargin)obj.method(varargin{:})</c>, made where it is written,
    /// so it captures the object, keeps it alive, and runs with the access of the code that wrote
    /// it (measured in R2025b). Null for every other dotted name - a .NET method, a static method.
    /// </summary>
    private JgsValue? BoundMethodHandle(FunctionHandleExpr handle, JgsEnvironment env)
    {
        int dot = handle.Name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || !LookUp(handle.Name[..dot], env, out JgsValue head) || head.Type != JgsType.Object)
        {
            return null;
        }

        if (handle.Bound is null)
        {
            string[] parts = handle.Name.Split('.');
            Expr target = new VariableExpr(parts[0]) { Line = handle.Line, Column = handle.Column };
            for (int i = 1; i < parts.Length; i++)
            {
                target = new MemberExpr(target, parts[i], null) { Line = handle.Line, Column = handle.Column };
            }

            var rest = new BraceIndexExpr(
                new VariableExpr("varargin") { Line = handle.Line, Column = handle.Column },
                [new AllExpr { Line = handle.Line, Column = handle.Column }])
            {
                Line = handle.Line,
                Column = handle.Column,
            };
            handle.Bound = new AnonymousFnExpr(["varargin"], new CallExpr(target, [rest]) { Line = handle.Line, Column = handle.Column })
            {
                Line = handle.Line,
                Column = handle.Column,
                Dialect = JgsDialect.Matlab,
            };
        }

        return JgsValue.Function(AnonymousFunction.Create(handle.Bound, env, this));
    }

    /// <summary>
    /// A handle to a name no function answers when it is made, in a run that has classes (U6):
    /// <c>@Class.staticMethod</c>, or a bare name left to mean a method of whatever object the
    /// handle is called with.
    /// </summary>
    private bool TryMethodHandle(string name, JgsEnvironment env, out JgsValue handle)
    {
        handle = JgsValue.Null;
        int dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            if (ClassNamed(name[..dot], env) is not { } definition
                || !definition.TryMethod(name[(dot + 1)..], out ClassMethod? method) || !method.Static)
            {
                return false;
            }

            if (FindMethod(definition, method.Function.Name, env.ClassContext, true, out _) != MethodAnswer.Found)
            {
                throw JgsClass.Restricted(method.Function.Name, method.Owner ?? definition, 0, 0);
            }

            handle = JgsValue.Function(definition.Callable(method));
            return true;
        }

        handle = JgsValue.Function(new NamedHandle(
            name, ResolutionLayer.Builtin, new UndefinedFunction(name, this), null, null, _resolver)
        {
            Context = env.ClassContext,
        });
        return true;
    }

    /// <summary>
    /// A handle to <paramref name="name"/>, which nothing answers yet: what R2025b's
    /// <c>str2func</c> makes of any name (U11) - a GUIDE app's main function turns its first
    /// argument into a handle whether it names a callback or a property - so that only a call is
    /// refused.
    /// </summary>
    internal JgsValue UnansweredHandle(string name, JgsEnvironment env) =>
        JgsValue.Function(new NamedHandle(
            name, ResolutionLayer.Builtin, new UndefinedFunction(name, this), null, null, _resolver)
        {
            Context = AnyClasses ? env.ClassContext : null,
        });

    /// <summary>What a handle to an unanswered name calls when no object's method takes the call: R2025b's refusal.</summary>
    private sealed class UndefinedFunction(string name, Interpreter interpreter) : IJgsCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            throw new JgsRuntimeException(line, column, "MATLAB:UndefinedFunction",
                arguments.Count > 0
                    ? $"Undefined function '{name}' for input arguments of type '{JgsBuiltins.ClassOf(arguments[0], JgsDialect.Matlab)}'."
                    : interpreter.Undefined(name));
    }

    /// <summary>
    /// <c>obj@Superclass(…);</c> as a statement: the superclass's constructor or method, asked for
    /// nothing, with <c>ans</c> left alone (R2025b binds no <c>ans</c> for a superclass
    /// constructor call).
    /// </summary>
    private void ExecuteSuperCallStatement(CallExpr call, JgsEnvironment env)
    {
        var reference = (SuperRefExpr)call.Callee;
        IJgsCallable callee = EvaluateSuperRef(reference, env).AsCallable;
        JgsValue[] given = EvaluateAll(call.Arguments, env);
        if (callee is SuperConstructorCall constructing)
        {
            // The bare form means obj = obj@Super(…): what the superclass made of the object goes
            // back into the constructor's output by the ordinary assignment.
            var store = new AssignExpr(
                new VariableExpr(reference.Name) { Line = call.Line, Column = call.Column },
                TokenType.Assign,
                new PreEvaluated(constructing.Call(given, call.Line, call.Column)) { Line = call.Line, Column = call.Column })
            {
                Line = call.Line,
                Column = call.Column,
            };
            EvaluateAssign(store, env);
            return;
        }

        _ = callee is IJgsMultiCallable several
            ? several.CallMultiple(given, 0, call.Line, call.Column)
            : [callee.Call(given, call.Line, call.Column)];
    }

    /// <summary>
    /// A superclass's constructor run on the object a subclass's constructor is building: the
    /// object is read from the constructor's own output variable, and what the superclass made of
    /// it is the answer - which <c>obj = obj@Super(…)</c> assigns, and the bare
    /// <c>obj@Super(…);</c> assigns for it.
    /// </summary>
    private sealed class SuperConstructorCall(JgsClass? super, JgsEnvironment frame, string variable, Interpreter interpreter)
        : IJgsCallable
    {
        public string Name => super?.Name ?? "handle";

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
        {
            if (!frame.TryGet(variable, out JgsValue built) || built.Type != JgsType.Object)
            {
                throw new JgsRuntimeException(line, column,
                    $"'{variable}' no longer holds the object being built, so '{Name}' has nothing to construct.");
            }

            if (super is null)
            {
                if (arguments.Count > 0)
                {
                    throw new JgsRuntimeException(line, column, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                return built;
            }

            return super.RunConstructor(interpreter.CopyForBinding(built), arguments, line, column);
        }
    }
}
