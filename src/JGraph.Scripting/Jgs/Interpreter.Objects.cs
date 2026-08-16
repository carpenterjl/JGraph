using System.Diagnostics.CodeAnalysis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's side of user classes (M68): where a class file's definition is kept, how a dot on
/// an instance finds a property or a method, and how a call written <c>f(obj, …)</c> reaches the class's
/// own body instead of a builtin with the same name.
/// </summary>
/// <remarks>
/// Every entry point here is guarded by <see cref="AnyClasses"/>, which is false until a
/// <c>classdef</c> file has actually been loaded. A script that defines no classes — which is every
/// script written before this milestone — therefore pays one boolean test per call and nothing else.
/// </remarks>
internal sealed partial class Interpreter
{
    private readonly Dictionary<string, JgsClass> _classes = new(StringComparer.Ordinal);

    /// <summary>The classes loaded from <c>classdef</c> files, by name.</summary>
    internal IReadOnlyDictionary<string, JgsClass> Classes => _classes;

    /// <summary>Whether any class has been defined at all — the guard every dispatch site opens with.</summary>
    internal bool AnyClasses => _classes.Count > 0;

    /// <summary>Records a class built from a file, replacing an earlier definition of the same name.</summary>
    internal JgsClass DefineClass(ClassdefStmt declaration, JgsEnvironment scope)
    {
        var built = new JgsClass(declaration, scope, this);
        _classes[declaration.Name] = built;
        return built;
    }

    /// <summary>
    /// Reads <c>obj.name</c>: a property, or a method with the object already in its hand. The two are
    /// asked in that order because a property is data and a method is behaviour, and a class that has
    /// both under one name is a class that cannot say what it meant.
    /// </summary>
    private JgsValue ObjectMember(JgsValue target, string field, MemberExpr member, bool autoCall)
    {
        JgsObject instance = target.AsObject;
        if (instance.Fields.TryGetValue(field, out JgsValue? held))
        {
            return held;
        }

        if (instance.Class.TryConstant(field, out JgsValue constant))
        {
            return constant;
        }

        if (instance.Class.TryMethod(field, out ClassMethod? method))
        {
            if (method.Static)
            {
                throw new JgsRuntimeException(member.Line, member.Column,
                    $"'{field}' is a static method of {instance.Class.Name}, so it is called on the class: "
                    + $"{instance.Class.Name}.{field}(…).");
            }

            var bound = new BoundMethod(instance.Class.Callable(method), target);

            // A bare `obj.area` means the answer, not the method — the same rule a dotted constant
            // already followed (M64). In callee position autoCall is off, so `obj.area(x)` hands the
            // arguments to the body rather than calling it empty and subscripting what came back.
            return autoCall
                ? bound.Call([], member.Line, member.Column)
                : JgsValue.Function(bound);
        }

        throw new JgsRuntimeException(member.Line, member.Column,
            $"'{instance.Class.Name}' has no property or method '{field}'.");
    }

    /// <summary>
    /// Reads a dot whose left side is a class <em>name</em> rather than an instance. Answers false when
    /// the name is not a loaded class, or when a variable of that name holds data — a workspace
    /// variable outranks a class, the same way it outranks a function.
    /// </summary>
    private bool TryClassInFront(MemberExpr member, JgsEnvironment env, bool autoCall, out JgsValue value)
    {
        value = JgsValue.Null;
        return member.Target is VariableExpr name
            && ClassNamed(name.Name, env) is { } definition
            && TryClassMember(definition, FieldName(member, env), member, autoCall, out value);
    }

    /// <summary>
    /// The class a bare name stands for, loading its file if this is the first mention of it. Answers
    /// null when a variable holds the name, when no file carries it, or when the file is not a class.
    /// </summary>
    /// <remarks>
    /// The load has to happen here rather than through the ordinary name resolution, because
    /// <c>Circle.unit()</c> can be the very first mention of Circle in a script and evaluating the
    /// name to find out would <em>build an instance</em> — the constructor auto-calls bare. A name
    /// that is already bound to something is left alone, so this costs a file probe only for a name
    /// nothing else in the session has heard of.
    /// </remarks>
    private JgsClass? ClassNamed(string name, JgsEnvironment env)
    {
        if (env.TryGet(name, out JgsValue bound) && bound.Type != JgsType.Function)
        {
            return null; // a variable holding data outranks a class, as it does a function
        }

        if (!_classes.ContainsKey(name) && !env.Contains(name))
        {
            _ = TryResolveOnPath(name, out _);
        }

        return _classes.TryGetValue(name, out JgsClass? definition) ? definition : null;
    }

    /// <summary>
    /// Reads <c>ClassName.name</c>: a <c>Constant</c> property or a <c>Static</c> method. Answers false
    /// for anything else, so the caller can go on to the other meanings a dot has.
    /// </summary>
    private bool TryClassMember(
        JgsClass definition, string field, MemberExpr member, bool autoCall, out JgsValue value)
    {
        if (definition.TryConstant(field, out value))
        {
            return true;
        }

        if (definition.TryMethod(field, out ClassMethod? method) && method.Static)
        {
            IJgsCallable callable = definition.Callable(method);
            value = autoCall && method.Function.Parameters.Count == 0
                ? callable.Call([], member.Line, member.Column)
                : JgsValue.Function(callable);
            return true;
        }

        value = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Writes <c>obj.name = value</c>, checking the property's declared size, class and validators
    /// first. Answers false when the target is not an object, so the ordinary struct write goes ahead.
    /// </summary>
    private bool TryAssignToObject(MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        // `Circle.Sides = 3` names the class, and there is nothing there to write to: a Constant
        // belongs to the class and an ordinary property belongs to an instance. Saying so is the
        // point — without it the write fell through to the struct path and quietly made a *variable*
        // called Circle, which hid the class behind it for the rest of the run.
        if (member.Target is VariableExpr onClass && ClassNamed(onClass.Name, env) is { } named)
        {
            string named_ = FieldName(member, env);
            throw new JgsRuntimeException(member.Line, member.Column,
                named.IsConstant(named_)
                    ? $"{named.Name}.{named_} is Constant, so it belongs to the class and cannot be assigned to."
                    : $"'{named.Name}' is a class, so '{named.Name}.{named_}' cannot be assigned to; "
                      + "set the property on an instance of it.");
        }

        if (ResolveObjectTarget(member.Target, env) is not { } instance)
        {
            return false;
        }

        string field = FieldName(member, env);
        JgsClass definition = instance.Class;
        if (definition.Property(field) is not { } property)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                definition.TryMethod(field, out _)
                    ? $"'{field}' is a method of {definition.Name}, not a property, so it cannot be assigned to."
                    : $"'{definition.Name}' has no property '{field}'.");
        }

        if (property.Constant)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                $"{definition.Name}.{field} is Constant, so it belongs to the class and cannot be assigned to.");
        }

        instance.Fields[field] = definition.Check(property, CopyForBinding(value), member.Line, member.Column);
        return true;
    }

    /// <summary>
    /// The instance a dotted write is aimed at, or null when the write is not about an object. Only a
    /// bound variable and a dot off one are considered, for the reason the handle path gives: anywhere
    /// else the target would have to be evaluated on the chance that it is one.
    /// </summary>
    private JgsObject? ResolveObjectTarget(Expr expr, JgsEnvironment env) => expr switch
    {
        VariableExpr variable when env.TryGet(variable.Name, out JgsValue bound) && bound.Type == JgsType.Object =>
            bound.AsObject,

        // obj.inner.value = 3 — the object held by a property of another object. Evaluating the inner
        // dot hands back the instance itself rather than a copy, so the write lands where it was aimed.
        MemberExpr inner when ResolveObjectTarget(inner.Target, env) is { } owner
            && owner.Fields.TryGetValue(FieldName(inner, env), out JgsValue? nested)
            && nested.Type == JgsType.Object => nested.AsObject,

        _ => null,
    };

    /// <summary>
    /// The class method a call written <c>name(first, …)</c> should reach, if any. MATLAB dispatches a
    /// call on the class of its arguments, and it has to win over the builtin table: <c>area(c)</c> on
    /// a Circle is the class's own method, not the chart verb of the same name.
    /// </summary>
    private bool TryMethodDispatch(
        string name, IReadOnlyList<JgsValue> arguments, [NotNullWhen(true)] out IJgsCallable? callable)
    {
        callable = null;
        if (arguments.Count == 0 || arguments[0].Type != JgsType.Object)
        {
            return false;
        }

        JgsClass definition = arguments[0].AsObject.Class;
        if (!definition.TryMethod(name, out ClassMethod? method) || method.Static)
        {
            return false;
        }

        callable = definition.Callable(method);
        return true;
    }

    /// <summary>
    /// Whether a call expression could be a method call at all: a plain name that is not a variable
    /// holding data. A name bound to a function handle still qualifies — the object wins, and the
    /// handle is tried after.
    /// </summary>
    private bool CouldDispatchOnClass(CallExpr call, JgsEnvironment env) =>
        AnyClasses
        && Dialect.IsMatlab
        && call.Arguments.Count > 0
        && call.Callee is VariableExpr name
        && (!env.TryGet(name.Name, out JgsValue bound) || bound.Type == JgsType.Function);

    /// <summary>
    /// The method name MATLAB gives each operator. A class overloads an operator by defining a method
    /// under this name, which is why no new syntax is needed to overload one.
    /// </summary>
    private static string? OperatorMethodName(TokenType op) => op switch
    {
        TokenType.Plus => "plus",
        TokenType.Minus => "minus",
        TokenType.Star => "mtimes",
        TokenType.DotStar => "times",
        TokenType.Slash => "mrdivide",
        TokenType.DotSlash => "rdivide",
        TokenType.Backslash => "mldivide",
        TokenType.DotBackslash => "ldivide",
        TokenType.Caret => "mpower",
        TokenType.DotCaret => "power",
        TokenType.EqualEqual => "eq",
        TokenType.BangEqual => "ne",
        TokenType.Less => "lt",
        TokenType.LessEqual => "le",
        TokenType.Greater => "gt",
        TokenType.GreaterEqual => "ge",
        _ => null,
    };

    /// <summary>
    /// Applies an overloaded binary operator when either operand is an object, and refuses by name when
    /// the class has not defined one.
    /// </summary>
    /// <remarks>
    /// Refusing rather than falling through matters: an object reaching the numeric machinery below
    /// would be read for a number it does not have, and the message would be about arrays. The class
    /// decides what its operators mean, and a class that has not said so has not said so.
    /// </remarks>
    private bool TryOperatorOverload(TokenType op, JgsValue left, JgsValue right, Node at, out JgsValue result)
    {
        result = JgsValue.Null;
        if (left.Type != JgsType.Object && right.Type != JgsType.Object)
        {
            return false;
        }

        // The left operand chooses when it is an object, which is MATLAB's own precedence for two
        // classes of equal standing and the only sensible reading of `obj + 1`.
        JgsClass definition = left.Type == JgsType.Object ? left.AsObject.Class : right.AsObject.Class;
        string? wanted = OperatorMethodName(op);
        if (wanted is null || !definition.TryMethod(wanted, out ClassMethod? method) || method.Static)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"'{OperatorSymbol(op)}' is not defined for {definition.Name}"
                + (wanted is null ? "." : $"; give the class a '{wanted}' method to define it."));
        }

        result = definition.Callable(method).Call([left, right], at.Line, at.Column);
        return true;
    }

    /// <summary>
    /// Applies an overloaded unary operator — <c>uminus</c>, <c>uplus</c> or <c>not</c> — to an object.
    /// </summary>
    private bool TryUnaryOverload(TokenType op, JgsValue operand, Node at, out JgsValue result)
    {
        result = JgsValue.Null;
        if (operand.Type != JgsType.Object)
        {
            return false;
        }

        JgsClass definition = operand.AsObject.Class;
        string wanted = op switch
        {
            TokenType.Bang => "not",
            TokenType.Plus => "uplus",
            _ => "uminus",
        };

        if (!definition.TryMethod(wanted, out ClassMethod? method) || method.Static)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"'{OperatorSymbol(op)}' is not defined for {definition.Name}; "
                + $"give the class a '{wanted}' method to define it.");
        }

        result = definition.Callable(method).Call([operand], at.Line, at.Column);
        return true;
    }

    /// <summary>
    /// How an object displays: its own <c>disp</c> method when it has one, and the class name with its
    /// properties otherwise. The echo of a bare <c>obj</c> and an explicit <c>disp(obj)</c> therefore
    /// show the same thing, which is the point of asking here rather than at one of them.
    /// </summary>
    internal bool TryObjectDisplay(JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Object || !value.AsObject.Class.TryMethod("disp", out ClassMethod? method)
            || method.Static)
        {
            return false;
        }

        value.AsObject.Class.Callable(method).Call([value], line, col);
        return true;
    }

    /// <summary>
    /// Invokes a call whose arguments have already been evaluated. Reached only from the class-dispatch
    /// path, where the callee is known not to be a variable holding data, so none of the indexing
    /// meanings a call expression can have apply.
    /// </summary>
    private JgsValue InvokeWithArguments(CallExpr call, JgsValue[] arguments, JgsEnvironment env)
    {
        JgsValue[] answered = InvokeWithArguments(call, arguments, wanted: 1, env);
        return answered.Length > 0 ? answered[0] : JgsValue.Null;
    }

    /// <summary>
    /// The same, asking for <paramref name="wanted"/> outputs. Keeping the output count here is what
    /// stops <c>[a, b] = size(x)</c> answering once merely because some class happens to be loaded.
    /// </summary>
    private JgsValue[] InvokeWithArguments(CallExpr call, JgsValue[] arguments, int wanted, JgsEnvironment env)
    {
        JgsValue callee = EvaluateCallee(call.Callee, env);
        if (callee.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(call.Line, call.Column,
                $"Cannot call a {callee.TypeName}; it is not a function.");
        }

        _pendingCall = call;
        return callee.AsCallable is IJgsMultiCallable several
            ? several.CallMultiple(arguments, wanted, call.Line, call.Column)
            : [callee.AsCallable.Call(arguments, call.Line, call.Column)];
    }
}
