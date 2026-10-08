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
/// A class that names superclasses (U6, ADR 0203) is built over theirs: it holds every property,
/// method and event an instance has, its own and the inherited ones, each still pointing at the
/// class that declared it - whose file its body, its default and its validators run in. Nothing
/// outside this class walks a hierarchy.
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
    private readonly Dictionary<string, ClassMethod> _own = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassMethod> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassMethod> _getters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassMethod> _setters = new(StringComparer.Ordinal);
    private readonly List<ClassProperty> _properties = [];
    private readonly Dictionary<string, ClassProperty> _propertyByName = new(StringComparer.Ordinal);
    private readonly List<ClassEvent> _events = [];
    private readonly List<JgsClass> _supers = [];
    private readonly List<JgsClass> _lineage = [];
    private readonly List<string> _superclassNames = [];
    private readonly JgsValue _constructor;
    private Dictionary<string, JgsValue>? _constants;

    /// <summary>The methods every handle class has without writing them, as <c>methods</c> lists them.</summary>
    internal static readonly string[] HandleMethodNames =
    [
        "addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify",
    ];

    /// <summary>Builds the class from its parsed definition, over the environment its methods see.</summary>
    public JgsClass(ClassdefStmt declaration, JgsEnvironment scope, Interpreter interpreter)
    {
        Declaration = declaration;
        _scope = scope;
        _interpreter = interpreter;
        _lineage.Add(this);

        ResolveSuperclasses(declaration);

        // Only a handle class may declare events (V6, #106): R2025b's refusal, made where the class
        // is defined, which is where a script that constructs it catches it.
        if (declaration.Events.Count > 0 && !IsHandle)
        {
            throw new JgsRuntimeException(declaration.Line, declaration.Column,
                $"The class '{declaration.Name}' may not define events because only subclasses of handle may define events.");
        }

        foreach (ClassProperty property in declaration.Properties)
        {
            property.Owner = this;
            if (!_propertyByName.TryAdd(property.Spec.Name, property))
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column,
                    $"Class '{declaration.Name}' defines the property '{property.Spec.Name}' twice.");
            }

            _properties.Add(property);
        }

        AddCallbackProperties(declaration);

        foreach (ClassMethod method in declaration.Methods)
        {
            method.Owner = this;

            // get.p and set.p are a property's accessors, not methods (V6, #27): they are kept by
            // the property, run on its reads and writes, and answer to no bare name. One for a
            // property the class does not declare is R2025b's refusal, made here where a script
            // that constructs the class catches it.
            if (method.AccessorProperty is { } accessed)
            {
                if (!_propertyByName.ContainsKey(accessed))
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
            if (!_own.TryAdd(method.Function.Name, method))
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column,
                    $"Class '{declaration.Name}' defines the method '{method.Function.Name}' twice.");
            }

            _methods[method.Function.Name] = method;
        }

        foreach (ClassEvent declared in EventsOf(declaration))
        {
            declared.Owner = this;
            _events.Add(declared);
        }

        Inherit();

        // A class's methods see each other by bare name, and see the constructor by the class's name.
        // That is MATLAB's rule and it is the one that makes a helper method usable: `value(x)` inside
        // `plus` has to work for a plain number too, and dispatch cannot help there because a plain
        // number belongs to no class.
        // The constructor is told how many outputs were asked for (U7): App Designer's ends with
        // `if nargout == 0, clear app, end`, which is how an app run as a statement leaves no ans.
        _constructor = JgsValue.Function(
            new BuiltinFunction(Name, (args, line, col) => Construct(args, line, col))
            {
                AutoCallsBare = true,
                TakesOutputCount = true,
                MultiOutput = (args, wanted, line, col) =>
                    ConstructAsked(args, wanted, line, col) is { } made ? [made] : [],
            });
        foreach (ClassMethod method in _own.Values)
        {
            if (!method.Abstract)
            {
                scope.DeclareFunction(method.Function.Name, JgsValue.Function(Callable(method)));
            }
        }

        // Last, so that the class's own name means the constructor and not the constructor's raw
        // body. Declaring it first let the loop above overwrite it, and `Money(3)` inside a method
        // then ran the body with no object to fill in — which quietly built a struct instead.
        scope.DeclareFunction(Name, _constructor);

        // What runs under this scope runs as the class (U6): its methods, the local functions of
        // its file, and every handle made in either.
        scope.OwnClass = this;
    }

    /// <summary>The parsed <c>classdef</c> this class was built from.</summary>
    public ClassdefStmt Declaration { get; }

    /// <summary>The class name, which is what <c>class(obj)</c> answers.</summary>
    public string Name => Declaration.Name;

    /// <summary>Whether the class is a handle class: its header names <c>handle</c>, or a superclass is one.</summary>
    public bool IsHandle { get; private set; }

    /// <summary>Whether a handle class, or one of its superclasses, wrote its own <c>delete</c> — a destructor V10 runs when the last holder goes.</summary>
    public bool HasDestructor => IsHandle && _methods.ContainsKey("delete");

    /// <summary>The interpreter this class was built by, which runs its destructors (V10).</summary>
    internal Interpreter Interpreter => _interpreter;

    /// <summary>Whether the class is an event's data: its header names <c>event.EventData</c> (V6, #106), or a superclass's does.</summary>
    public bool IsEventData { get; private set; }

    /// <summary>
    /// Whether instances are custom components (U10, ADR 0209): the class is
    /// <c>matlab.ui.componentcontainer.ComponentContainer</c> or inherits from it, so each instance
    /// stands for an area in a figure.
    /// </summary>
    public bool IsComponentContainer { get; private set; }

    /// <summary>The name every handle class's <c>delete</c> raises, without declaring it.</summary>
    public const string ObjectBeingDestroyed = "ObjectBeingDestroyed";

    /// <summary>The events the class has, its own first and then its superclasses'; a handle class also raises <see cref="ObjectBeingDestroyed"/>.</summary>
    public IReadOnlyList<string> Events => [.. _events.Select(static e => e.Name)];

    /// <summary>The events <c>events</c> lists: the ones anyone may listen to, less the hidden ones (U6, measured).</summary>
    public IEnumerable<string> ListedEvents =>
        _events.Where(static e => e.ListenAccess.IsPublic && !e.Hidden).Select(static e => e.Name);

    /// <summary>The declared event of that name, the class's own or an inherited one, or null.</summary>
    public ClassEvent? Event(string name) => _events.Find(e => e.Name == name);

    /// <summary>Whether <c>notify</c> and <c>addlistener</c> may name this event on the class (V6, #106).</summary>
    public bool HasEvent(string name) => Event(name) is not null || (IsHandle && name == ObjectBeingDestroyed);

    /// <summary>
    /// Every property an instance has, the class's own first in the order the file wrote them and
    /// then each superclass's (U6, measured: <c>properties</c> of a subclass lists its own first).
    /// </summary>
    public IReadOnlyList<ClassProperty> Properties => _properties;

    /// <summary>The properties a listing shows: the ones anyone may read, less the hidden ones (U6, measured).</summary>
    public IEnumerable<ClassProperty> ListedProperties => _properties.Where(static p => p.GetAccess.IsPublic && !p.Hidden);

    /// <summary>
    /// The method names <c>methods</c> lists: the public ones that are not hidden, the class's own
    /// first and then the inherited ones, then the ones every handle class has (U6, measured). A
    /// property's accessors are not methods, and a superclass's constructor is not inherited.
    /// </summary>
    public IEnumerable<string> MethodNames
    {
        get
        {
            IEnumerable<string> listed = _methods.Values
                .Where(static m => m.Access.IsPublic && !m.Hidden)
                .Select(static m => m.Function.Name);

            // A class that wrote no constructor still has one (measured: methods lists 'U6Doc').
            if (!_own.ContainsKey(Name) && !Declaration.Abstract)
            {
                listed = listed.Append(Name[(Name.LastIndexOf('.') + 1)..]); // matlab.apps.AppBase lists 'AppBase'
            }

            // In R2025b's order (U7, measured): by character code, so the constructor's capital leads.
            return (IsHandle ? listed.Concat(HandleMethodNames).Distinct(StringComparer.Ordinal) : listed)
                .Order(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The methods a listing shows, the class's own and the inherited ones, public and not hidden;
    /// not the ones every handle class has, which <see cref="MethodNames"/> adds by name (open item 14).
    /// </summary>
    internal IEnumerable<ClassMethod> ListedMethods => _methods.Values.Where(static m => m.Access.IsPublic && !m.Hidden);

    /// <summary>Whether the class wrote no constructor and still has one (it is not abstract).</summary>
    internal bool HasImplicitConstructor => !_own.ContainsKey(Name) && !Declaration.Abstract;

    /// <summary>The constructor's name as a listing shows it: the class name after its last dot.</summary>
    internal string ConstructorName => Name[(Name.LastIndexOf('.') + 1)..];

    /// <summary>The classes named after <c>&lt;</c> that are classes here: user classes and mixins, not <c>handle</c>.</summary>
    public IReadOnlyList<JgsClass> Supers => _supers;

    /// <summary>
    /// What <c>superclasses</c> answers: each superclass followed by its own, <c>handle</c> where
    /// it appears, each name once (U6, measured: <c>{'U6MixA'; 'handle'; 'U6MixB'}</c>).
    /// </summary>
    public IReadOnlyList<string> SuperclassNames => _superclassNames;

    /// <summary>Whether no instance can be made: the header said <c>(Abstract)</c>, or a method or property still is.</summary>
    public bool IsAbstract =>
        Declaration.Abstract || _methods.Values.Any(static m => m.Abstract) || _properties.Exists(static p => p.Abstract);

    /// <summary>
    /// Whether some method of this class has the name of a superclass's private method, so that
    /// the superclass's own code still means its own (<see cref="TryMethodFor"/>). False for
    /// nearly every class, which is what keeps a public call at one lookup.
    /// </summary>
    public bool HasPrivateShadows { get; private set; }

    /// <summary>Whether this class is <paramref name="other"/> or inherits from it.</summary>
    public bool IsSubclassOf(JgsClass other) => _lineage.Contains(other);

    /// <summary>The superclass of that name, at any depth, or null.</summary>
    public JgsClass? Ancestor(string name) => _lineage.Find(level => !ReferenceEquals(level, this) && level.Name == name);

    /// <summary>Whether an instance <c>isa</c> the named class: this one, a superclass, <c>handle</c>.</summary>
    public bool IsA(string name) =>
        string.Equals(Name, name, StringComparison.Ordinal) || _superclassNames.Contains(name, StringComparer.Ordinal)
        || (IsComponentContainer && JgsBuiltinClasses.ComponentContainerAncestors.Contains(name, StringComparer.Ordinal));

    /// <summary>Whether the class or a superclass says <paramref name="other"/> is inferior to it (<c>InferiorClasses</c>; measured: by exact name).</summary>
    public bool Dominates(JgsClass other) => Declaration.InferiorClasses.Contains(other.Name, StringComparer.Ordinal);

    /// <summary>The <c>get.name</c> method, or false when the property reads from its storage (V6, #27).</summary>
    public bool TryGetter(string name, [NotNullWhen(true)] out ClassMethod? getter) =>
        OwnerOf(name)._getters.TryGetValue(name, out getter);

    /// <summary>The <c>set.name</c> method, or false when a write stores (V6, #27).</summary>
    public bool TrySetter(string name, [NotNullWhen(true)] out ClassMethod? setter) =>
        OwnerOf(name)._setters.TryGetValue(name, out setter);

    /// <summary>
    /// Whether a read or a write of the property is an accessor's doing rather than the storage's:
    /// it has a <c>get</c> or a <c>set</c> method, or is <c>Dependent</c> (and so has no storage).
    /// </summary>
    public bool HasAccessor(string name) =>
        TryGetter(name, out _) || TrySetter(name, out _) || Property(name) is { Dependent: true };

    /// <summary>The tag the frame of <c>get.name</c> carries (<see cref="JgsEnvironment.AccessorOf"/>).</summary>
    public string GetterTag(string name) => "get:" + OwnerOf(name).Name + "." + name;

    /// <summary>The tag the frame of <c>set.name</c> carries.</summary>
    public string SetterTag(string name) => "set:" + OwnerOf(name).Name + "." + name;

    /// <summary>The class that declares the property, which keeps its accessors and its constant; this class for a name it does not have.</summary>
    private JgsClass OwnerOf(string property) =>
        _propertyByName.TryGetValue(property, out ClassProperty? declared) && declared.Owner is { } owner ? owner : this;

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

        if (returned.Type != JgsType.Object || !returned.AsObject.Class.IsSubclassOf(this))
        {
            throw new JgsRuntimeException(line, col,
                $"The set function for property '{name}' must return an instance of class '{Name}'.");
        }

        return returned;
    }

    /// <summary>
    /// What a property shows as in the object's display: the get method's answer where the property
    /// has one (measured: R2025b's display runs every getter, a <c>Dependent</c> property's included),
    /// the class's constant, the storage otherwise, and null for a property nothing can answer for.
    /// </summary>
    public JgsValue? DisplayValue(JgsObject instance, ClassProperty property)
    {
        string name = property.Spec.Name;
        if (TryGetter(name, out _))
        {
            return CallGetter(name, JgsValue.Object(instance), Declaration.Line, Declaration.Column);
        }

        if (property.Constant)
        {
            return TryConstant(name, out JgsValue constant) ? constant : null;
        }

        return instance.Fields.TryGetValue(name, out JgsValue? held) ? held : null;
    }

    /// <summary>The constructor: calling it builds an instance. This is the value the class name holds.</summary>
    public JgsValue ConstructorValue => _constructor;

    /// <summary>The property of that name, the class's own or an inherited one, or null.</summary>
    public ClassProperty? Property(string name) => _propertyByName.GetValueOrDefault(name);

    /// <summary>
    /// The method of that name, the class's own or an inherited one, or false when the class has
    /// none. A superclass's private method is found too: whether the caller may have it is
    /// <see cref="Visible(ClassMethod, JgsClass?)"/>'s question.
    /// </summary>
    public bool TryMethod(string name, [NotNullWhen(true)] out ClassMethod? method) =>
        _methods.TryGetValue(name, out method);

    /// <summary>The method this class's own file wrote under that name, or false.</summary>
    public bool TryOwnMethod(string name, [NotNullWhen(true)] out ClassMethod? method) => _own.TryGetValue(name, out method);

    /// <summary>
    /// The method <paramref name="name"/> means to code running as <paramref name="context"/> (U6):
    /// the context's own private method when the object is one of its subclasses' - a private
    /// method is not overridden - and the class's method otherwise.
    /// </summary>
    public bool TryMethodFor(string name, JgsClass? context, [NotNullWhen(true)] out ClassMethod? method)
    {
        if (context is not null && !ReferenceEquals(context, this) && IsSubclassOf(context)
            && context._own.TryGetValue(name, out ClassMethod? theirs) && theirs.Access.Kind == MemberAccessKind.Private)
        {
            method = theirs;
            return true;
        }

        return _methods.TryGetValue(name, out method);
    }

    /// <summary>
    /// Whether a method exists at all for code running as <paramref name="context"/> (U6,
    /// measured): a superclass's private method is no method of the subclass, so outside its own
    /// class a call of it on a subclass's object is an undefined function rather than a refusal.
    /// </summary>
    public bool Visible(ClassMethod method, JgsClass? context) =>
        method.Access.Kind != MemberAccessKind.Private || ReferenceEquals(method.Owner, this) || ReferenceEquals(method.Owner, context);

    /// <summary>The same question of a property: private to a superclass, it is no property of this class.</summary>
    public bool Visible(ClassProperty property, JgsClass? context, bool write) =>
        (write ? property.SetAccess.Kind != MemberAccessKind.Private || property.GetAccess.Kind != MemberAccessKind.Private
               : property.GetAccess.Kind != MemberAccessKind.Private)
        || ReferenceEquals(property.Owner, this) || ReferenceEquals(property.Owner, context);

    /// <summary>
    /// Whether code running as <paramref name="context"/> - a class, or null for a script, a
    /// function file or the prompt - may reach a member with <paramref name="access"/> that
    /// <paramref name="owner"/> declares under <paramref name="member"/> (U6, measured in R2025b):
    /// private is the class alone; protected is the class and its subclasses, and a superclass
    /// that declares the same member (a method it calls on itself and a subclass overrides); a
    /// list is the class, the classes it names and their subclasses - not the class's own.
    /// </summary>
    public static bool Allows(MemberAccess access, JgsClass owner, JgsClass? context, string member)
    {
        switch (access.Kind)
        {
            case MemberAccessKind.Public:
                return true;
            case MemberAccessKind.Private:
            case MemberAccessKind.Immutable:
                return ReferenceEquals(context, owner);
            case MemberAccessKind.Protected:
                return context is not null
                    && (context.IsSubclassOf(owner) || (owner.IsSubclassOf(context) && context.Declares(member)));
            default:
                return context is not null
                    && (ReferenceEquals(context, owner) || access.Classes!.Any(context.IsA));
        }
    }

    private bool Declares(string member) => _methods.ContainsKey(member) || _propertyByName.ContainsKey(member);

    /// <summary>
    /// The callable a method name stands for — a user function over the scope of the class that
    /// defines it, or a built-in class's own body. An accessor's frame carries its tag, which is
    /// what lets its own body reach the storage.
    /// </summary>
    public IJgsCallable Callable(ClassMethod method)
    {
        JgsClass owner = method.Owner ?? this;
        if (method.Native is { } native)
        {
            return native;
        }

        if (method.Abstract)
        {
            throw new JgsRuntimeException(Declaration.Line, Declaration.Column,
                $"The method '{method.Function.Name}' of class '{owner.Name}' is abstract, so there is nothing to call.");
        }

        return new UserFunction(method.Function, owner._scope, _interpreter)
        {
            AccessorOf = method.AccessorProperty is { } accessed
                ? (method.IsGetter ? owner.GetterTag(accessed) : owner.SetterTag(accessed))
                : null,
            OwnerClass = owner.Name,
            Owner = owner,
        };
    }

    /// <summary>
    /// The <c>delete</c> methods a handle object's destruction runs, the class's own first and then
    /// each superclass's (U6, measured: <c>delMid;delBase</c>). A subclass's destructor does not
    /// replace its superclass's; both run.
    /// </summary>
    public IEnumerable<IJgsCallable> Destructors()
    {
        foreach (JgsClass level in _lineage)
        {
            if (level._own.TryGetValue("delete", out ClassMethod? destructor) && !destructor.Abstract)
            {
                yield return level.Callable(destructor);
            }
        }
    }

    /// <summary>
    /// The value of a <c>Constant</c> property. Constants belong to the class that declares them
    /// rather than to an instance, so they are evaluated once, on first use, and shared from then on.
    /// </summary>
    public bool TryConstant(string name, out JgsValue value)
    {
        if (Property(name) is not { Constant: true } property)
        {
            value = JgsValue.Null;
            return false;
        }

        if (property.Owner is { } owner && !ReferenceEquals(owner, this))
        {
            return owner.TryConstant(name, out value);
        }

        _constants ??= BuildConstants();
        return _constants.TryGetValue(name, out value!);
    }

    /// <summary>Whether the class has a constant of that name.</summary>
    public bool IsConstant(string name) => Property(name) is { Constant: true };

    /// <summary>
    /// Checks a value against a property's declared size, class and validators, answering the value as
    /// it should be stored. Runs on the defaults at construction and on every later write, because a
    /// property whose declaration is only honoured once is a declaration that stops being true. The
    /// validators run in the file of the class that declares the property.
    /// </summary>
    public JgsValue Check(ClassProperty property, JgsValue value, int line, int col)
    {
        JgsClass owner = property.Owner ?? this;
        JgsValue verified;
        try
        {
            verified = JgsBuiltins.CheckArgument(property.Spec, value, line, col, _interpreter.Globals, _interpreter);
            using Interpreter.FileContext classFile = _interpreter.EnterFile(owner.Declaration.SourceId);
            using Interpreter.DialectContext classCode = _interpreter.EnterDialect(owner.Declaration.Dialect); // V11
            _interpreter.RunValidators(property.Spec.Validators, verified, owner._scope);
        }
        catch (JgsRuntimeException failure)
        {
            // A property typed with a graphics class refuses in R2025b's words (U7, measured).
            if (failure.Identifier is "MATLAB:graphics:CannotConvertDoubleToHandle" or "MATLAB:validation:UnableToConvert")
            {
                throw new JgsRuntimeException(line, col, failure.Identifier,
                    $"Error setting property '{property.Spec.Name}' of class '{owner.Name}'. {failure.Message}");
            }

            // The validator's identifier travels with the refusal (U6): a script branches on it.
            throw new JgsRuntimeException(line, col, failure.Identifier,
                $"{Name}.{property.Spec.Name}: {failure.Message}");
        }

        return verified;
    }

    /// <summary>The value a property starts with: its default, evaluated in its own class's file and checked as a write would be.</summary>
    public JgsValue DefaultOf(ClassProperty property, int line, int col)
    {
        JgsClass owner = property.Owner ?? this;
        JgsValue start = property.Spec.Default is { } expression
            ? _interpreter.EvaluateInContext(
                expression, owner.DefaultWorkspace(), owner.Declaration.SourceId, $"{owner.Name}.{property.Spec.Name} default",
                line, owner.Declaration.Dialect)
            : JgsMatrix.FromColumnMajor([], 0, 0); // MATLAB's [], 0-by-0 (U7, measured)
        return Check(property, start, line, col);
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

        foreach (ClassProperty property in _properties)
        {
            if (property.Constant || property.Dependent || property.Abstract)
            {
                // A constant belongs to the class, so an instance does not carry a copy; a Dependent
                // property has no storage, and a default written on it is ignored (V6, #28, measured).
                continue;
            }

            instance.Fields[property.Spec.Name] = DefaultOf(property, line, col);
        }

        return instance;
    }

    /// <summary>
    /// Builds an instance the way the file asked for. With no constructor the arguments go to the
    /// one superclass there is, or are refused; with one, the object the constructor's output names
    /// starts out fully defaulted, which is what lets a constructor set two properties and leave the
    /// rest alone.
    /// </summary>
    public JgsValue Construct(IReadOnlyList<JgsValue> arguments, int line, int col) =>
        ConstructAsked(arguments, 1, line, col)!;

    /// <summary>
    /// <see cref="Construct"/>, told how many outputs the call asked for. Null when it asked for
    /// none and the constructor cleared its own output, which is the one way to make nothing.
    /// </summary>
    private JgsValue? ConstructAsked(IReadOnlyList<JgsValue> arguments, int wanted, int line, int col)
    {
        if (_own.TryGetValue(Name, out ClassMethod? constructor) && !constructor.Access.IsPublic
            && !Allows(constructor.Access, this, _interpreter.ContextClass(), Name))
        {
            throw Restricted(Name, this, line, col);
        }

        if (IsAbstract)
        {
            bool declares = Declaration.Abstract || _own.Values.Any(static m => m.Abstract)
                || Declaration.Properties.Any(static p => p.Abstract);
            throw new JgsRuntimeException(line, col, "MATLAB:class:abstract",
                declares
                    ? $"Abstract classes cannot be instantiated. Class '{Name}' defines abstract methods and/or properties."
                    : $"Abstract classes cannot be instantiated. Class '{Name}' inherits abstract methods or properties but "
                      + "does not implement them. Use matlab.metadata.abstractDetails to see the list of methods and "
                      + $"properties that '{Name}' must implement if you do not intend the class to be abstract.");
        }

        return RunConstructor(JgsValue.Object(NewDefault(line, col)), arguments, wanted, line, col);
    }

    /// <summary>
    /// Runs this class's constructor on <paramref name="built"/>, an object of this class or of a
    /// subclass that is being made (U6). A superclass the body does not call by name -
    /// <c>obj@Super(…)</c> - is constructed first with no arguments, as R2025b does; a class with
    /// no constructor hands its arguments to its one superclass (measured: <c>U6Cube(5)</c>).
    /// </summary>
    internal JgsValue RunConstructor(JgsValue built, IReadOnlyList<JgsValue> arguments, int line, int col) =>
        RunConstructor(built, arguments, 1, line, col)!;

    private JgsValue? RunConstructor(JgsValue built, IReadOnlyList<JgsValue> arguments, int wanted, int line, int col)
    {
        // A built-in class's constructor written in C# (U10: ComponentContainer's), handed the
        // object being made and the arguments.
        if (_own.TryGetValue(Name, out ClassMethod? native) && native.Native is { } body && native.Function.Outputs.Count == 1)
        {
            body.Call([built, .. arguments], line, col);
            return built;
        }

        if (!_own.TryGetValue(Name, out ClassMethod? constructor) || constructor.Native is not null)
        {
            if (arguments.Count > 0 && _supers.Count != 1)
            {
                throw _supers.Count == 0
                    ? new JgsRuntimeException(line, col, $"'{Name}' has no constructor, so it takes no arguments.")
                    : new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            foreach (JgsClass super in _supers)
            {
                built = super.RunConstructor(built, arguments, line, col);
            }

            return built;
        }

        FnStmt declaration = constructor.Function;
        if (declaration.Outputs.Count != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"The constructor of '{Name}' must have exactly one output, the object it builds.");
        }

        string output = declaration.Outputs[0];
        IReadOnlyList<string> parameters = declaration.Parameters;
        bool variadic = parameters.Count > 0 && parameters[^1] == "varargin";
        int fixedCount = variadic ? parameters.Count - 1 : parameters.Count;
        if (arguments.Count > fixedCount && !variadic)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        foreach (JgsClass super in _supers)
        {
            if (declaration.SuperRefs is not { } references
                || !references.Any(r => r.Name == output && r.Superclass == super.Name))
            {
                built = super.RunConstructor(built, [], line, col);
            }
        }

        var local = new JgsEnvironment(_scope) { IsCallBoundary = true, Function = declaration };

        // The constructor's parameters bind under the class file's dialect (V11), as a function's do.
        using (_interpreter.EnterDialect(declaration.Dialect))
        {
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
        }

        local.Declare("nargin", JgsValue.Number(arguments.Count));
        local.Declare("nargout", JgsValue.Number(wanted));
        local.Declare(output, built);

        _interpreter.ExecuteFunctionBody(declaration, local, line);
        if (!local.TryGet(output, out JgsValue result) || result.Type != JgsType.Object)
        {
            // Asked for nothing and left nothing: `clear app` at the end of an app's constructor.
            if (wanted == 0 && !local.DeclaresLocally(output))
            {
                return null;
            }

            throw new JgsRuntimeException(line, col,
                $"The constructor of '{Name}' finished without leaving a '{Name}' in '{output}'.");
        }

        return result;
    }

    /// <summary>Whether <paramref name="function"/> is one of the methods this class's own file declares.</summary>
    internal bool DeclaresMethod(FnStmt function) =>
        _own.TryGetValue(function.Name, out ClassMethod? method) && ReferenceEquals(method.Function, function);

    /// <summary>Whether <paramref name="function"/> is this class's constructor - the one place an immutable property is written.</summary>
    internal bool IsConstructor(FnStmt? function) =>
        function is not null && _own.TryGetValue(Name, out ClassMethod? constructor) && ReferenceEquals(constructor.Function, function);

    /// <summary>R2025b's refusal of a method the caller may not have (U6).</summary>
    internal static JgsRuntimeException Restricted(string method, JgsClass owner, int line, int col) =>
        new(line, col, "MATLAB:class:MethodRestricted", $"Cannot access method '{method}' in class '{owner.Name}'.");

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
        foreach (ClassProperty property in Declaration.Properties)
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
                    Declaration.Line, Declaration.Dialect),
                Declaration.Line,
                Declaration.Column);
        }

        return constants;
    }

    /// <summary>The events a declaration carries: with their access when the parser read it, public otherwise.</summary>
    private static IEnumerable<ClassEvent> EventsOf(ClassdefStmt declaration) =>
        declaration.EventSpecs.Count > 0
            ? declaration.EventSpecs
            : declaration.Events.Select(static name => new ClassEvent(name, MemberAccess.Public, MemberAccess.Public, false));

    /// <summary>
    /// Finds the classes the header names (U6): loads each from the path, refuses one that is
    /// missing, Sealed or closed to this class, and settles whether the class is a handle class.
    /// The refusals are R2025b's, raised where the class is first used.
    /// </summary>
    private void ResolveSuperclasses(ClassdefStmt declaration)
    {
        bool anyHandle = false;
        bool anyValue = false;
        foreach (string named in declaration.Superclasses)
        {
            if (named is "handle" or "event.EventData")
            {
                anyHandle = true;
                IsEventData |= named == "event.EventData";
                AddSuperclassNames([named]);
                if (named == "event.EventData")
                {
                    AddSuperclassNames(["handle"]);
                }

                continue;
            }

            // A class MATLAB ships and this build does not is said to be that, rather than "not
            // found on the path": the script is right and the path is not at fault.
            JgsClass super = _interpreter.ClassForSuper(named, declaration)
                ?? throw (named.StartsWith("matlab.", StringComparison.Ordinal) || named is "dynamicprops" or "hgsetget"
                    ? new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:InvalidSuperClass",
                        $"'{declaration.Name} < {named}': '{named}' is a MATLAB class this build does not supply. A class may "
                        + $"inherit from another class file, 'handle', 'event.EventData', '{JgsBuiltinClasses.Copyable}' "
                        + $"and '{JgsBuiltinClasses.SetGet}'.")
                    : new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:InvalidSuperClass",
                        $"The specified superclass '{named}' contains a parse error, cannot be found on MATLAB's search path, "
                        + "or is shadowed by another file with the same name."));
            if (super.Declaration.Sealed)
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:sealed",
                    $"Class '{super.Name}' is Sealed and may not be used as a superclass.");
            }

            if (super.Declaration.AllowedSubclasses is { } allowed && !allowed.Contains(declaration.Name, StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:SubclassNotAllowed",
                    $"Class '{declaration.Name}' could not be instantiated because it may not inherit from class '{super.Name}'. "
                    + $"Class '{declaration.Name}' is not specified as an allowed subclass of class '{super.Name}'. "
                    + $"Remove class '{super.Name}' from the list of superclasses of class '{declaration.Name}'.");
            }

            anyHandle |= super.IsHandle;
            anyValue |= !super.IsHandle && !super.Declaration.HandleCompatible;
            HasPrivateShadows |= super.HasPrivateShadows;
            IsEventData |= super.IsEventData;
            _supers.Add(super);
            foreach (JgsClass level in super._lineage)
            {
                if (!_lineage.Contains(level))
                {
                    _lineage.Add(level);
                }
            }

            AddSuperclassNames([super.Name, .. super._superclassNames]);
        }

        if (anyHandle && anyValue)
        {
            throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:inconsistentSuperclasses",
                "If a class defines superclasses, all or none must be handle classes.");
        }

        IsHandle = anyHandle;
        IsComponentContainer = declaration.Name == JgsBuiltinClasses.ComponentContainer || _supers.Exists(static s => s.IsComponentContainer);
    }

    /// <summary>
    /// The properties a custom component's <c>events (HasCallbackProperty)</c> block makes (U10,
    /// probe <c>u10_matrix</c>): for each event a public, Dependent <c>NameFcn</c>, listed after the
    /// class's own properties, holding a callback as a graphics callback property does. On a
    /// class that is no component the attribute is R2025b's refusal.
    /// </summary>
    private void AddCallbackProperties(ClassdefStmt declaration)
    {
        foreach (ClassEvent declared in declaration.EventSpecs)
        {
            if (!declared.HasCallbackProperty)
            {
                continue;
            }

            if (!IsComponentContainer)
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:UnrecognizedAttribute",
                    "Illegal attribute 'HasCallbackProperty'.");
            }

            string name = declared.Name + "Fcn";
            var property = new ClassProperty(new ArgumentSpec(name, null, null, [], null), Constant: false, Dependent: true) { Owner = this };
            if (!_propertyByName.TryAdd(name, property))
            {
                throw new JgsRuntimeException(declaration.Line, declaration.Column,
                    $"Class '{declaration.Name}' defines the property '{name}' twice.");
            }

            _properties.Add(property);
            string eventName = declared.Name;
            _getters[name] = JgsBuiltinClasses.CallbackAccessor(this, "get." + name, eventName, write: false);
            _setters[name] = JgsBuiltinClasses.CallbackAccessor(this, "set." + name, eventName, write: true);
        }
    }

    private void AddSuperclassNames(IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (!_superclassNames.Contains(name, StringComparer.Ordinal))
            {
                _superclassNames.Add(name);
            }
        }
    }

    /// <summary>
    /// Takes what the superclasses have (U6): their properties after this class's own, their
    /// methods where this class defines none of the name, their events. The refusals are R2025b's:
    /// a property a superclass already has, a method a superclass sealed, a method two
    /// superclasses both define.
    /// </summary>
    private void Inherit()
    {
        ClassdefStmt declaration = Declaration;
        foreach (JgsClass super in _supers)
        {
            foreach (ClassProperty inherited in super._properties)
            {
                if (_propertyByName.TryGetValue(inherited.Spec.Name, out ClassProperty? held))
                {
                    if (ReferenceEquals(held, inherited) || !ReferenceEquals(held.Owner, this))
                    {
                        continue; // the same property by two roads, or an earlier superclass's
                    }

                    if (inherited.Abstract)
                    {
                        continue; // this class supplies what the superclass asked for
                    }

                    throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:RedefinedProperty",
                        $"Cannot define property '{inherited.Spec.Name}' in class '{Name}' because the property has already "
                        + $"been defined in the superclass '{inherited.Owner?.Name ?? super.Name}'.");
                }

                _propertyByName[inherited.Spec.Name] = inherited;
                _properties.Add(inherited);
            }

            foreach ((string name, ClassMethod inherited) in super._methods)
            {
                if (inherited.Owner is { } definer && name == definer.Name)
                {
                    continue; // a constructor is not inherited
                }

                if (_own.TryGetValue(name, out ClassMethod? mine))
                {
                    if (inherited.Sealed && inherited.Access.Kind != MemberAccessKind.Private && name != "delete")
                    {
                        throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:methodOverrideSealed",
                            $"Method '{name}' in class '{Name}' conflicts with the sealed method in superclass "
                            + $"'{inherited.Owner?.Name ?? super.Name}'. Overriding a sealed method is not supported.");
                    }

                    HasPrivateShadows |= inherited.Access.Kind == MemberAccessKind.Private && !ReferenceEquals(mine, inherited);
                    continue;
                }

                if (_methods.TryGetValue(name, out ClassMethod? earlier))
                {
                    if (ReferenceEquals(earlier, inherited) || name == "delete" || inherited.Abstract)
                    {
                        continue; // one method by two roads; every destructor runs; a body beats a signature
                    }

                    if (!earlier.Abstract)
                    {
                        throw new JgsRuntimeException(declaration.Line, declaration.Column, "MATLAB:class:methodAmbiguous",
                            $"Method '{name}' defined in class '{Name}' has 2 or more conflicting definitions.");
                    }
                }

                _methods[name] = inherited;
            }

            foreach (ClassEvent inherited in super._events)
            {
                if (!_events.Exists(e => e.Name == inherited.Name))
                {
                    _events.Add(inherited);
                }
            }
        }
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
    /// The object is on its way out (U7, measured in R2025b): <c>isvalid</c> already answers
    /// false, to an <c>ObjectBeingDestroyed</c> listener and to the class's own <c>delete</c>
    /// alike, and both still read its properties. A <c>delete</c> that arrives meanwhile - an
    /// app's figure deleting the app that is deleting the figure - finds nothing left to do.
    /// </summary>
    public bool Destroying { get; set; }

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
