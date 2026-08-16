using System.Diagnostics.CodeAnalysis;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A class defined by a <c>classdef</c> file (M68): what its instances hold, what they can be asked to
/// do, and whether two names for one of them mean one object or two.
/// </summary>
/// <remarks>
/// <para>
/// A class is built once per file and cached by <see cref="JgsFunctionPath"/> exactly as a function file
/// is, so editing a class file and running again picks the new definition up. The value the path hands
/// back for the name is the <em>constructor</em>: that is what makes <c>Circle(2)</c> work without the
/// interpreter learning a new kind of callee.
/// </para>
/// <para>
/// Properties are <see cref="ArgumentSpec"/>s. MATLAB writes a property line and an <c>arguments</c>
/// line with the same grammar and means the same thing by both, so they share a parser here and, more
/// usefully, a checker: <see cref="JgsBuiltins.CheckArgument"/> is what enforces a property's declared
/// size and class, and the same <c>mustBe…</c> validators run on a property write that would run on an
/// argument. A validator that was written for a function is therefore already written for a class.
/// </para>
/// </remarks>
internal sealed class JgsClass
{
    private readonly Interpreter _interpreter;
    private readonly JgsEnvironment _scope;
    private readonly Dictionary<string, ClassMethod> _methods = new(StringComparer.Ordinal);
    private readonly JgsValue _constructor;
    private Dictionary<string, JgsValue>? _constants;

    /// <summary>Builds the class from its parsed definition, over the environment its methods see.</summary>
    public JgsClass(ClassdefStmt declaration, JgsEnvironment scope, Interpreter interpreter)
    {
        Declaration = declaration;
        _scope = scope;
        _interpreter = interpreter;

        foreach (ClassMethod method in declaration.Methods)
        {
            // A file that defines the same method twice is a mistake worth naming: silently keeping
            // one of them is how a script comes to call a body nobody can find by reading.
            if (!_methods.TryAdd(method.Function.Name, method))
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column,
                    $"Class '{declaration.Name}' defines the method '{method.Function.Name}' twice.");
            }
        }

        // A class's methods see each other by bare name, and see the constructor by the class's name.
        // That is MATLAB's rule and it is the one that makes a helper method usable: `value(x)` inside
        // `plus` has to work for a plain number too, and dispatch cannot help there because a plain
        // number belongs to no class.
        _constructor = JgsValue.Function(
            new BuiltinFunction(Name, (args, line, col) => Construct(args, line, col)) { AutoCallsBare = true });
        foreach (ClassMethod method in declaration.Methods)
        {
            scope.Declare(method.Function.Name, JgsValue.Function(Callable(method)));
        }

        // Last, so that the class's own name means the constructor and not the constructor's raw
        // body. Declaring it first let the loop above overwrite it, and `Money(3)` inside a method
        // then ran the body with no object to fill in — which quietly built a struct instead.
        scope.Declare(Name, _constructor);
    }

    /// <summary>The parsed <c>classdef</c> this class was built from.</summary>
    public ClassdefStmt Declaration { get; }

    /// <summary>The class name, which is what <c>class(obj)</c> answers.</summary>
    public string Name => Declaration.Name;

    /// <summary>Whether the header read <c>&lt; handle</c>.</summary>
    public bool IsHandle => Declaration.IsHandle;

    /// <summary>The declared properties, in the order the file wrote them.</summary>
    public IReadOnlyList<ClassProperty> Properties => Declaration.Properties;

    /// <summary>The method names, in the order the file wrote them.</summary>
    public IEnumerable<string> MethodNames => Declaration.Methods.Select(static m => m.Function.Name);

    /// <summary>The constructor: calling it builds an instance. This is the value the class name holds.</summary>
    public JgsValue ConstructorValue => _constructor;

    /// <summary>The declared property of that name, or null.</summary>
    public ClassProperty? Property(string name)
    {
        foreach (ClassProperty property in Properties)
        {
            if (string.Equals(property.Spec.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>The method of that name, or false when the class has none.</summary>
    public bool TryMethod(string name, [NotNullWhen(true)] out ClassMethod? method) =>
        _methods.TryGetValue(name, out method);

    /// <summary>The callable a method name stands for — a user function over the class's own scope.</summary>
    public IJgsCallable Callable(ClassMethod method) =>
        new UserFunction(method.Function, _scope, _interpreter);

    /// <summary>
    /// The value of a <c>Constant</c> property. Constants belong to the class rather than to an
    /// instance, so they are evaluated once, on first use, and shared from then on.
    /// </summary>
    public bool TryConstant(string name, out JgsValue value)
    {
        _constants ??= BuildConstants();
        return _constants.TryGetValue(name, out value!);
    }

    /// <summary>Whether the class declares a constant of that name.</summary>
    public bool IsConstant(string name) => Property(name) is { Constant: true };

    /// <summary>
    /// Checks a value against a property's declared size, class and validators, answering the value as
    /// it should be stored. Runs on the defaults at construction and on every later write, because a
    /// property whose declaration is only honoured once is a declaration that stops being true.
    /// </summary>
    public JgsValue Check(ClassProperty property, JgsValue value, int line, int col)
    {
        JgsValue verified;
        try
        {
            verified = JgsBuiltins.CheckArgument(property.Spec, value, line, col, _interpreter.Globals);
            _interpreter.RunValidators(property.Spec.Validators, verified, _scope);
        }
        catch (JgsRuntimeException failure)
        {
            throw new JgsRuntimeException(line, col,
                $"{Name}.{property.Spec.Name}: {failure.Message}");
        }

        return verified;
    }

    /// <summary>Builds an instance with every property holding its default, checked as a write would be.</summary>
    public JgsObject NewDefault(int line, int col)
    {
        var instance = new JgsObject(this);
        foreach (ClassProperty property in Properties)
        {
            if (property.Constant)
            {
                continue; // a constant belongs to the class, so an instance does not carry a copy
            }

            JgsValue start = property.Spec.Default is { } expression
                ? _interpreter.EvaluateIn(expression, _scope)
                : JgsValue.Array([]);
            instance.Fields[property.Spec.Name] = Check(property, start, line, col);
        }

        return instance;
    }

    /// <summary>
    /// Builds an instance the way the file asked for. With no constructor the arguments are refused —
    /// there is nothing to do with them — and with one, the object the constructor's output names
    /// starts out fully defaulted, which is what lets a constructor set two properties and leave the
    /// rest alone.
    /// </summary>
    public JgsValue Construct(IReadOnlyList<JgsValue> arguments, int line, int col)
    {
        JgsObject instance = NewDefault(line, col);
        if (!TryMethod(Name, out ClassMethod? constructor))
        {
            if (arguments.Count > 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"'{Name}' has no constructor, so it takes no arguments.");
            }

            return JgsValue.Object(instance);
        }

        FnStmt declaration = constructor.Function;
        if (declaration.Outputs.Count != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"The constructor of '{Name}' must have exactly one output, the object it builds.");
        }

        string built = declaration.Outputs[0];
        var local = new JgsEnvironment(_scope) { IsCallBoundary = true };
        IReadOnlyList<string> parameters = declaration.Parameters;
        bool variadic = parameters.Count > 0 && parameters[^1] == "varargin";
        int fixedCount = variadic ? parameters.Count - 1 : parameters.Count;
        if (arguments.Count > fixedCount && !variadic)
        {
            throw new JgsRuntimeException(line, col,
                $"'{Name}' takes {fixedCount} argument(s) but got {arguments.Count}.");
        }

        for (int i = 0; i < fixedCount && i < arguments.Count; i++)
        {
            local.Declare(parameters[i], _interpreter.CopyForBinding(arguments[i]));
        }

        if (variadic)
        {
            var rest = new JgsValue[Math.Max(0, arguments.Count - fixedCount)];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = _interpreter.CopyForBinding(arguments[fixedCount + i]);
            }

            local.Declare("varargin", JgsValue.Cell(rest));
        }

        local.Declare("nargin", JgsValue.Number(arguments.Count));
        local.Declare("nargout", JgsValue.Number(1));
        local.Declare(built, JgsValue.Object(instance));

        _interpreter.ExecuteFunctionBody(declaration, local, line);
        if (!local.TryGet(built, out JgsValue result) || result.Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col,
                $"The constructor of '{Name}' finished without leaving a '{Name}' in '{built}'.");
        }

        return result;
    }

    private Dictionary<string, JgsValue> BuildConstants()
    {
        var constants = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (ClassProperty property in Properties)
        {
            if (!property.Constant)
            {
                continue;
            }

            if (property.Spec.Default is not { } expression)
            {
                throw new JgsRuntimeException(Declaration.Line, Declaration.Column,
                    $"{Name}.{property.Spec.Name} is Constant, so it must be given a value where it is declared.");
            }

            constants[property.Spec.Name] = Check(
                property,
                _interpreter.EvaluateIn(expression, _scope),
                Declaration.Line,
                Declaration.Column);
        }

        return constants;
    }
}

/// <summary>
/// One instance of a <see cref="JgsClass"/>: its class, and what its properties hold.
/// </summary>
/// <remarks>
/// The difference between a value class and a handle class is one line — whether
/// <see cref="Interpreter.CopyForBinding"/> clones this — which is the rule M64 already stated for
/// <c>containers.Map</c> against <c>dictionary</c>. Nothing else in the object model knows which kind
/// it is holding.
/// </remarks>
internal sealed class JgsObject(JgsClass definition)
{
    /// <summary>The class this is an instance of.</summary>
    public JgsClass Class { get; } = definition;

    /// <summary>What the instance's properties hold, keyed by name.</summary>
    public Dictionary<string, JgsValue> Fields { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A copy holding the same property values — what binding a second name to a value-class object
    /// means. The property values themselves are copied by the same rule, so a struct inside a value
    /// object is copied and a handle object inside one is not.
    /// </summary>
    public JgsObject Copy(Interpreter interpreter)
    {
        var clone = new JgsObject(Class);
        foreach ((string name, JgsValue value) in Fields)
        {
            clone.Fields[name] = interpreter.CopyForBinding(value);
        }

        return clone;
    }
}

/// <summary>
/// A method with its object already in hand: what <c>obj.area</c> stands for. Calling it puts the
/// object back at the front of the argument list, which is why <c>obj.area()</c> and <c>area(obj)</c>
/// reach the same body by different roads.
/// </summary>
internal sealed class BoundMethod(IJgsCallable method, JgsValue receiver) : IJgsCallable, IJgsMultiCallable
{
    /// <inheritdoc />
    public string Name => method.Name;

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        method.Call(WithReceiver(arguments), line, column);

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        method is IJgsMultiCallable multi
            ? multi.CallMultiple(WithReceiver(arguments), wanted, line, column)
            : [Call(arguments, line, column)];

    private JgsValue[] WithReceiver(IReadOnlyList<JgsValue> arguments)
    {
        var all = new JgsValue[arguments.Count + 1];
        all[0] = receiver;
        for (int i = 0; i < arguments.Count; i++)
        {
            all[i + 1] = arguments[i];
        }

        return all;
    }
}
