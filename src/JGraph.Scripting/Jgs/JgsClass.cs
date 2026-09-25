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
    private readonly Dictionary<string, ClassMethod> _getters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassMethod> _setters = new(StringComparer.Ordinal);
    private readonly JgsValue _constructor;
    private Dictionary<string, JgsValue>? _constants;

    /// <summary>Builds the class from its parsed definition, over the environment its methods see.</summary>
    public JgsClass(ClassdefStmt declaration, JgsEnvironment scope, Interpreter interpreter)
    {
        Declaration = declaration;
        _scope = scope;
        _interpreter = interpreter;

        // Only a handle class may declare events (V6, #106): R2025b's refusal, made where the class
        // is defined, which is where a script that constructs it catches it.
        if (declaration.Events.Count > 0 && !declaration.IsHandle)
        {
            throw new JgsRuntimeException(declaration.Line, declaration.Column,
                $"The class '{declaration.Name}' may not define events because only subclasses of handle may define events.");
        }

        foreach (ClassMethod method in declaration.Methods)
        {
            // get.p and set.p are a property's accessors, not methods (V6, #27): they are kept by
            // the property, run on its reads and writes, and answer to no bare name. One for a
            // property the class does not declare is R2025b's refusal, made here where a script
            // that constructs the class catches it.
            if (method.AccessorProperty is { } accessed)
            {
                if (Property(accessed) is null)
                {
                    throw new JgsRuntimeException(declaration.Line, declaration.Column,
                        $"Cannot specify a {(method.IsGetter ? "get" : "set")} function for property '{accessed}' in class "
                        + $"'{declaration.Name}', because that property is not defined by that class.");
                }

                if (!(method.IsGetter ? _getters : _setters).TryAdd(accessed, method))
                {
                    throw new JgsRuntimeException(declaration.Line, declaration.Column,
                        $"Class '{declaration.Name}' defines the method '{method.Function.Name}' twice.");
                }

                continue;
            }

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
        foreach (ClassMethod method in _methods.Values)
        {
            scope.DeclareFunction(method.Function.Name, JgsValue.Function(Callable(method)));
        }

        // Last, so that the class's own name means the constructor and not the constructor's raw
        // body. Declaring it first let the loop above overwrite it, and `Money(3)` inside a method
        // then ran the body with no object to fill in — which quietly built a struct instead.
        scope.DeclareFunction(Name, _constructor);
    }

    /// <summary>The parsed <c>classdef</c> this class was built from.</summary>
    public ClassdefStmt Declaration { get; }

    /// <summary>The class name, which is what <c>class(obj)</c> answers.</summary>
    public string Name => Declaration.Name;

    /// <summary>Whether the header read <c>&lt; handle</c>.</summary>
    public bool IsHandle => Declaration.IsHandle;

    /// <summary>Whether a handle class wrote its own <c>delete</c> — a destructor V10 runs when the last holder goes.</summary>
    public bool HasDestructor => IsHandle && _methods.ContainsKey("delete");

    /// <summary>The interpreter this class was built by, which runs its destructors (V10).</summary>
    internal Interpreter Interpreter => _interpreter;

    /// <summary>Whether the header read <c>&lt; event.EventData</c> (V6, #106).</summary>
    public bool IsEventData => Declaration.IsEventData;

    /// <summary>The name every handle class's <c>delete</c> raises, without declaring it.</summary>
    public const string ObjectBeingDestroyed = "ObjectBeingDestroyed";

    /// <summary>The events the file declared, in order; a handle class also raises <see cref="ObjectBeingDestroyed"/>.</summary>
    public IReadOnlyList<string> Events => Declaration.Events;

    /// <summary>Whether <c>notify</c> and <c>addlistener</c> may name this event on the class (V6, #106).</summary>
    public bool HasEvent(string name) =>
        Declaration.Events.Contains(name, StringComparer.Ordinal) || (IsHandle && name == ObjectBeingDestroyed);

    /// <summary>The declared properties, in the order the file wrote them.</summary>
    public IReadOnlyList<ClassProperty> Properties => Declaration.Properties;

    /// <summary>The method names, in the order the file wrote them; a property's accessors are not methods.</summary>
    public IEnumerable<string> MethodNames =>
        Declaration.Methods.Where(static m => m.AccessorProperty is null).Select(static m => m.Function.Name);

    /// <summary>The <c>get.name</c> method, or false when the property reads from its storage (V6, #27).</summary>
    public bool TryGetter(string name, [NotNullWhen(true)] out ClassMethod? getter) => _getters.TryGetValue(name, out getter);

    /// <summary>The <c>set.name</c> method, or false when a write stores (V6, #27).</summary>
    public bool TrySetter(string name, [NotNullWhen(true)] out ClassMethod? setter) => _setters.TryGetValue(name, out setter);

    /// <summary>
    /// Whether a read or a write of the property is an accessor's doing rather than the storage's:
    /// it has a <c>get</c> or a <c>set</c> method, or is <c>Dependent</c> (and so has no storage).
    /// </summary>
    public bool HasAccessor(string name) =>
        _getters.ContainsKey(name) || _setters.ContainsKey(name) || Property(name) is { Dependent: true };

    /// <summary>The tag the frame of <c>get.name</c> carries (<see cref="JgsEnvironment.AccessorOf"/>).</summary>
    public string GetterTag(string name) => "get:" + Name + "." + name;

    /// <summary>The tag the frame of <c>set.name</c> carries.</summary>
    public string SetterTag(string name) => "set:" + Name + "." + name;

    /// <summary>
    /// Runs <c>get.name</c> on <paramref name="receiver"/> and answers what it returned (V6, #27,
    /// #28). A <c>Dependent</c> property with no get method is R2025b's refusal.
    /// </summary>
    public JgsValue CallGetter(string name, JgsValue receiver, int line, int col)
    {
        if (!TryGetter(name, out ClassMethod? getter))
        {
            throw new JgsRuntimeException(line, col,
                $"In class '{Name}', no get method is defined for dependent property '{name}'. "
                + "A dependent property needs a get method to access its value.");
        }

        JgsValue answer = Callable(getter).Call([receiver], line, col);
        if (answer.Type == JgsType.Null)
        {
            throw new JgsRuntimeException(line, col,
                $"The get function for property '{name}' in class '{Name}' returned no value.");
        }

        return answer;
    }

    /// <summary>
    /// Runs <c>set.name</c> on <paramref name="receiver"/> with <paramref name="value"/> and answers
    /// the object the property was set on: the receiver itself for a handle class, whose set method
    /// writes it in place, and the value class's returned object otherwise, which the caller stores
    /// where the receiver was read (measured: a value class's set method that returns no object is
    /// refused, a handle class's may return one, which is ignored).
    /// </summary>
    public JgsValue CallSetter(string name, JgsValue receiver, JgsValue value, int line, int col)
    {
        if (!TrySetter(name, out ClassMethod? setter))
        {
            throw new JgsRuntimeException(line, col,
                $"In class '{Name}', no set method is defined for dependent property '{name}'. "
                + "A dependent property needs a set method to assign its value.");
        }

        JgsValue returned = Callable(setter).Call([receiver, value], line, col);
        if (IsHandle)
        {
            return receiver;
        }

        if (returned.Type != JgsType.Object || !ReferenceEquals(returned.AsObject.Class, this))
        {
            throw new JgsRuntimeException(line, col,
                $"The set function for property '{name}' must return an instance of class '{Name}'.");
        }

        return returned;
    }

    /// <summary>
    /// What a property shows as in the object's display: the get method's answer where the property
    /// has one (measured: R2025b's display runs every getter, a <c>Dependent</c> property's included),
    /// the storage otherwise, and null for a property nothing can answer for.
    /// </summary>
    public JgsValue? DisplayValue(JgsObject instance, ClassProperty property)
    {
        string name = property.Spec.Name;
        if (_getters.ContainsKey(name))
        {
            return CallGetter(name, JgsValue.Object(instance), Declaration.Line, Declaration.Column);
        }

        return instance.Fields.TryGetValue(name, out JgsValue? held) ? held : null;
    }

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

    /// <summary>
    /// The callable a method name stands for — a user function over the class's own scope. An
    /// accessor's frame carries its tag, which is what lets its own body reach the storage.
    /// </summary>
    public IJgsCallable Callable(ClassMethod method) =>
        new UserFunction(method.Function, _scope, _interpreter)
        {
            AccessorOf = method.AccessorProperty is { } accessed
                ? (method.IsGetter ? GetterTag(accessed) : SetterTag(accessed))
                : null,
            OwnerClass = Name,
        };

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
            using Interpreter.FileContext classFile = _interpreter.EnterFile(Declaration.SourceId);
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
        if (IsEventData)
        {
            // event.EventData's two inherited properties, empty until notify fills them in (V6, #106).
            instance.Fields[JgsBuiltins.EventNameField] = JgsValue.Str(string.Empty);
            instance.Fields[JgsBuiltins.EventSourceField] = JgsMatrix.FromColumnMajor([], 0, 0);
        }

        JgsEnvironment defaults = DefaultWorkspace();
        foreach (ClassProperty property in Properties)
        {
            if (property.Constant || property.Dependent)
            {
                // A constant belongs to the class, so an instance does not carry a copy; a Dependent
                // property has no storage, and a default written on it is ignored (V6, #28, measured).
                continue;
            }

            JgsValue start = property.Spec.Default is { } expression
                ? _interpreter.EvaluateInContext(
                    expression, defaults, Declaration.SourceId, $"{Name}.{property.Spec.Name} default", line)
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

    /// <summary>
    /// The workspace a property default or a constant is evaluated in: a private, writable frame
    /// over the class's scope that is thrown away afterwards. R2025b was measured (M145, step 0): a
    /// function called from a default reads no variable of the constructing frame, can
    /// <c>assignin('caller', …)</c> without error, and nothing sees what it assigned. The class
    /// file's local functions are visible to the default itself and to nothing it calls.
    /// </summary>
    private JgsEnvironment DefaultWorkspace() => new(_scope) { IsCallBoundary = true };

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
                _interpreter.EvaluateInContext(
                    expression, DefaultWorkspace(), Declaration.SourceId, $"{Name}.{property.Spec.Name} default",
                    Declaration.Line),
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
internal sealed class JgsObject
{
    // How many entries hold this instance (M1). Zero and one both mean one holder.
    private int _holders;

    /// <summary>Creates an instance of <paramref name="definition"/>; one with a destructor counts as alive from here (V10).</summary>
    public JgsObject(JgsClass definition)
    {
        Class = definition;
        JgsLifetime.Register(this);
    }

    /// <summary>The holder count's storage (M1); zero means one holder.</summary>
    public ref int HolderSlot => ref _holders;

    /// <summary>V10 (ADR 0171): how many entries hold this instance, exactly — counted from birth for a handle, once scanned for a value.</summary>
    public int Exact;

    /// <summary>V10: whether every property value's hold was counted for this instance, so its death releases them.</summary>
    public bool Scanned;

    /// <summary>V10: whether the first wrapper over this instance has been made (the birth check is queued once).</summary>
    public bool Wrapped;

    /// <summary>V10: whether the properties' contents have been released — at <c>delete</c>, or at the last holder's going.</summary>
    public bool FieldsReleased;

    /// <summary>V10: whether this instance has left the count of live destructor-bearing objects.</summary>
    public bool DiedCounted;

    /// <summary>The class this is an instance of.</summary>
    public JgsClass Class { get; }

    /// <summary>What the instance's properties hold, keyed by name.</summary>
    public Dictionary<string, JgsValue> Fields { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether <c>delete</c> has been called on this handle object (V6, appendix A #104). Every
    /// alias sees the same instance, so every alias sees it deleted: <c>isvalid</c> answers false and
    /// a property read or write refuses with MATLAB's "Invalid or deleted object." The fields are
    /// kept — a destructor that ran has already read them, and nothing else may.
    /// </summary>
    public bool Deleted { get; private set; }

    /// <summary>Marks the instance deleted; a second <c>delete</c> is a no-op and runs no destructor.</summary>
    public void MarkDeleted() => Deleted = true;

    /// <summary>
    /// The listeners <c>addlistener</c> put on this instance, oldest first (V6, #106, #108). They
    /// live with the source: clearing the name that held one changes nothing, and <c>delete(lh)</c>
    /// is what ends one. Null until the first is added, so an object that nobody listens to costs
    /// nothing at a property write.
    /// </summary>
    public List<JgsListener>? Listeners { get; set; }

    /// <summary>Whether a live listener on this instance watches the named property's <c>PreSet</c> or <c>PostSet</c>.</summary>
    public bool HasPropertyListener(string property)
    {
        if (Listeners is null)
        {
            return false;
        }

        foreach (JgsListener listener in Listeners)
        {
            if (!listener.Deleted && listener.Watches(property))
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>The object the method was read off (M5's receiver scope holds a share of it).</summary>
    internal JgsValue Receiver => receiver;

    /// <summary>The method itself, whose output list bounds what a call may ask for (V9.3).</summary>
    internal IJgsCallable Method => method;

    /// <summary>The same method bound to <paramref name="held"/> — a receiver scope's share.</summary>
    internal BoundMethod WithReceiverValue(JgsValue held) => new(method, held);

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
