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
    internal bool AnyClasses => _classes.Count > 0 || _builtinClasses.Count > 0;

    /// <summary>Records a class built from a file, replacing an earlier definition of the same name.</summary>
    internal JgsClass DefineClass(ClassdefStmt declaration, JgsEnvironment scope)
    {
        var built = new JgsClass(declaration, scope, this);
        _classes[declaration.Name] = built;
        return built;
    }

    /// <summary>
    /// The class the running code belongs to (U6, ADR 0203), or null for a script, a function
    /// file and the prompt: what decides whether a private or protected member may be reached.
    /// A handle made inside a class runs under the class's scope wherever it is called from, so
    /// it keeps the class's access, as R2025b's does.
    /// </summary>
    internal JgsClass? ContextClass() => AnyClasses ? CurrentFrame.ClassContext : null;

    private readonly HashSet<string> _classesBeingBuilt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JgsClass> _builtinClasses = new(StringComparer.Ordinal);

    /// <summary>
    /// The class a <c>classdef</c> header names after <c>&lt;</c> (U6): a mixin this build supplies,
    /// or a class file on the path, loaded now if this is its first mention. Null when nothing
    /// defines it. A class that reaches itself through its own superclasses is refused.
    /// </summary>
    internal JgsClass? ClassForSuper(string name, ClassdefStmt from)
    {
        if (BuiltinClass(name) is { } supplied)
        {
            return supplied;
        }

        if (!_classesBeingBuilt.Add(from.Name))
        {
            throw new JgsRuntimeException(from.Line, from.Column,
                $"Class '{from.Name}' inherits from itself through '{name}'.");
        }

        try
        {
            return ClassForLoad(name);
        }
        finally
        {
            _classesBeingBuilt.Remove(from.Name);
        }
    }

    /// <summary>The file a class was read from, as <c>which</c> names it; empty for a class this build supplies.</summary>
    internal string FileOfClass(JgsClass definition) => definition.Declaration.SourceId;

    /// <summary>A class this build supplies for a user class to inherit from (<c>matlab.mixin.Copyable</c>), or null.</summary>
    internal JgsClass? BuiltinClass(string name)
    {
        if (_builtinClasses.TryGetValue(name, out JgsClass? made))
        {
            return made;
        }

        if (JgsBuiltinClasses.DeclarationOf(name, this) is not { } declaration)
        {
            return null;
        }

        made = new JgsClass(declaration, NewFileScope(), this);
        _builtinClasses[name] = made;
        return made;
    }

    /// <summary>What asking for a method came to: there is one, there is none, or there is one the caller may not have.</summary>
    internal enum MethodAnswer
    {
        /// <summary>The class has no such method, as far as the caller can tell.</summary>
        Missing,

        /// <summary>The method, which the caller may call.</summary>
        Found,

        /// <summary>The method exists and the caller may not call it.</summary>
        Refused,
    }

    /// <summary>
    /// The method <paramref name="name"/> on <paramref name="definition"/> for the running code
    /// (U6). A public method costs one lookup; anything else asks which class is running.
    /// </summary>
    internal MethodAnswer FindMethod(
        JgsClass definition, string name, JgsClass? context, bool contextKnown, out ClassMethod? method)
    {
        if (!definition.TryMethod(name, out method))
        {
            return MethodAnswer.Missing;
        }

        if (method.Access.IsPublic && !definition.HasPrivateShadows)
        {
            return MethodAnswer.Found;
        }

        context = contextKnown ? context : ContextClass();
        if (definition.HasPrivateShadows)
        {
            definition.TryMethodFor(name, context, out method);
        }

        if (method!.Access.IsPublic)
        {
            return MethodAnswer.Found;
        }

        if (!definition.Visible(method, context))
        {
            return MethodAnswer.Missing;
        }

        return JgsClass.Allows(method.Access, method.Owner ?? definition, context, name) ? MethodAnswer.Found : MethodAnswer.Refused;
    }

    /// <summary>
    /// A write that goes on past <c>obj.field</c> - <c>obj.p(2) = v</c>, <c>obj.s.f = v</c> - reads
    /// the property and then sets it (U6, measured: the read is refused first), so it needs both
    /// accesses. When what the property holds is a handle, the write lands in the handle and the
    /// property is only read.
    /// </summary>
    private void RequirePassThrough(JgsValue owner, string field, Node at)
    {
        JgsObject instance = owner.AsObject;
        JgsClass definition = instance.Class;
        if (definition.Property(field) is not { } property || (property.GetAccess.IsPublic && property.SetAccess.IsPublic))
        {
            return;
        }

        RequireGetAccess(definition, property, field, at);
        if (property.SetAccess.IsPublic || (instance.Fields.TryGetValue(field, out JgsValue? held) && IsReference(held)))
        {
            return;
        }

        RequireSetAccess(definition, property, field, at.Line, at.Column);
    }

    /// <summary>Whether a value names something rather than holding it: a handle object, a graphics handle, a built-in handle class, a .NET or device object.</summary>
    private static bool IsReference(JgsValue value) =>
        value.Type == JgsType.External
        || (value.Type == JgsType.Object && value.AsObject.Class.IsHandle)
        || JgsBuiltins.IsHandleClass(value)
        || (value.Type == JgsType.Number && JgsHandleRegistry.TryGet(value, out _));

    /// <summary>R2025b's words for a name an object has no property, method or field of.</summary>
    private static JgsRuntimeException NoSuchMember(string field, JgsClass definition, Node at) =>
        new(at.Line, at.Column, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{field}' for class '{definition.Name}'.");

    /// <summary>R2025b's words for a property the caller may not read (the doubled quotes are R2025b's).</summary>
    internal static JgsRuntimeException GetProhibited(string field, JgsClass definition, int line, int col) =>
        new(line, col, "MATLAB:class:GetProhibited", $"No public property '{field}' for class ''{definition.Name}''.");

    /// <summary>
    /// Refuses a read of <paramref name="property"/> the running code may not make (U6): a
    /// superclass's private property is no property of the object at all, and any other is
    /// R2025b's <c>GetProhibited</c>.
    /// </summary>
    internal void RequireGetAccess(JgsClass definition, ClassProperty property, string field, Node at)
    {
        if (property.GetAccess.IsPublic)
        {
            return;
        }

        JgsClass? context = ContextClass();
        if (!definition.Visible(property, context, write: false))
        {
            throw NoSuchMember(field, definition, at);
        }

        if (!JgsClass.Allows(property.GetAccess, property.Owner ?? definition, context, field))
        {
            throw GetProhibited(field, definition, at.Line, at.Column);
        }
    }

    /// <summary>
    /// Refuses a write of <paramref name="property"/> the running code may not make (U6), in
    /// R2025b's two sentences: a property the caller can read is "read-only", one it cannot is
    /// "not supported". An immutable property is written by its class's constructor alone.
    /// </summary>
    internal void RequireSetAccess(JgsClass definition, ClassProperty property, string field, int line, int col)
    {
        if (property.SetAccess.IsPublic)
        {
            return;
        }

        JgsClass? context = ContextClass();
        JgsClass owner = property.Owner ?? definition;
        if (!definition.Visible(property, context, write: true))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                $"Unrecognized property '{field}' for class '{definition.Name}'.");
        }

        bool allowed = property.SetAccess.Kind == MemberAccessKind.Immutable
            ? ReferenceEquals(context, owner) && owner.IsConstructor(CurrentFrame.EnclosingFunction)
            : JgsClass.Allows(property.SetAccess, owner, context, field);
        if (!allowed)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:SetProhibited",
                JgsClass.Allows(property.GetAccess, owner, context, field)
                    ? $"Unable to set the '{field}' property of class ''{definition.Name}'' because it is read-only."
                    : $"Setting the '{field}' property of class ''{definition.Name}'' is not supported.");
        }
    }

    /// <summary>
    /// Reads <c>obj.name</c>: a property, or a method with the object already in its hand. The two are
    /// asked in that order because a property is data and a method is behaviour, and a class that has
    /// both under one name is a class that cannot say what it meant.
    /// </summary>
    private JgsValue ObjectMember(JgsValue target, string field, MemberExpr member, bool autoCall)
    {
        JgsObject instance = target.AsObject;
        RequireLive(instance, member.Line, member.Column);

        // A property with a get method reads as that method's answer (V6, #27, #28) - except inside
        // the method's own body, where obj.p is the storage - and a Dependent property has nothing
        // else to read as, so one without a get method is refused in R2025b's words.
        JgsClass definition = instance.Class;
        ClassProperty? declared = definition.Property(field);
        if (declared is not null)
        {
            RequireGetAccess(definition, declared, field, member); // U6: who may read it
        }

        if (declared is { Constant: false } property
            && ((definition.TryGetter(field, out _) && !InAccessor(definition.GetterTag(field))) || property.Dependent))
        {
            _readRanGetter = true;
            return definition.CallGetter(field, target, member.Line, member.Column);
        }

        if (instance.Fields.TryGetValue(field, out JgsValue? held))
        {
            return held;
        }

        if (instance.Class.TryConstant(field, out JgsValue constant))
        {
            return constant;
        }

        MethodAnswer answer = FindMethod(definition, field, null, false, out ClassMethod? method);
        if (answer == MethodAnswer.Refused)
        {
            throw JgsClass.Restricted(field, method!.Owner ?? definition, member.Line, member.Column);
        }

        if (answer == MethodAnswer.Found && method is not null)
        {
            if (method.Static)
            {
                // A static method read off an instance is the class's (R2025b allows the spelling).
                IJgsCallable unbound = definition.Callable(method);
                return autoCall && method.Function.Parameters.Count == 0
                    ? unbound.Call([], member.Line, member.Column)
                    : JgsValue.Function(unbound);
            }

            IJgsCallable body = field == "delete" && instance.Class.IsHandle
                ? new DestructorCall(instance) // obj.delete is delete(obj) (V6, #104)
                : instance.Class.Callable(method);

            var bound = new BoundMethod(body, target);

            // A bare `obj.area` means the answer, not the method — the same rule a dotted constant
            // already followed (M64). In callee position autoCall is off, so `obj.area(x)` hands the
            // arguments to the body rather than calling it empty and subscripting what came back.
            return autoCall
                ? bound.Call([], member.Line, member.Column)
                : JgsValue.Function(bound);
        }

        // Every handle class inherits delete and isvalid from handle; a class that wrote neither
        // still answers to obj.delete and obj.isvalid (V6, #104).
        if (instance.Class.IsHandle && field is "delete" or "isvalid")
        {
            var inherited = new BoundMethod(new InheritedHandleMethod(field, instance), target);
            return autoCall ? inherited.Call([], member.Line, member.Column) : JgsValue.Function(inherited);
        }

        throw NoSuchMember(field, definition, member);
    }

    /// <summary>
    /// Whether the innermost running function is the accessor <paramref name="tag"/> names (V6,
    /// #27): inside <c>get.p</c> a read of <c>obj.p</c> is the storage, and inside <c>set.p</c> a
    /// write of <c>obj.p</c> is a store. The rule is the accessor's own body's (measured in R2025b:
    /// a helper it calls goes through the accessor again, so a getter reading through a helper
    /// recurses), which is why it asks the frame and not the object.
    /// </summary>
    private bool InAccessor(string tag) => CurrentFrame.AccessorOf == tag;

    /// <summary>
    /// Whether the last property read ran a get method - set by <see cref="ObjectMember"/>, read by
    /// the call road so that <c>x = o.p(end)</c> reads the property once for <c>end</c> and once for
    /// the value, as R2025b does (<c>get;get</c>).
    /// </summary>
    private bool _readRanGetter;

    /// <summary>The <c>delete</c> and <c>isvalid</c> every handle class has without writing them.</summary>
    private sealed class InheritedHandleMethod(string name, JgsObject instance) : IJgsCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
        {
            if (name == "isvalid")
            {
                return JgsValue.Bool(!instance.Deleted && !instance.Destroying);
            }

            instance.MarkDeleted();
            JgsBuiltins.FireObjectBeingDestroyed(instance); // after the mark (V6, #106)
            JgsLifetime.ObjectDeleted(instance); // V10: the properties' contents go with the object
            return JgsValue.Null;
        }
    }

    /// <summary>
    /// Reads a dot whose left side is a class <em>name</em> rather than an instance. Answers false when
    /// the name is not a loaded class, or when a variable of that name holds data — a workspace
    /// variable outranks a class, the same way it outranks a function.
    /// </summary>
    private bool TryClassInFront(MemberExpr member, JgsEnvironment env, bool autoCall, out JgsValue value)
    {
        value = JgsValue.Null;
        if (member.Target is VariableExpr name)
        {
            return ClassNamed(name.Name, env) is { } definition
                && TryClassMember(definition, FieldName(member, env), member, autoCall, out value);
        }

        // matlab.apps.AppBase and matlab.apps.AppBase.loadobj(s): a class this build supplies,
        // named by its package (U7). Only a chain that starts at an unbound "matlab" is looked at.
        if (DottedName(member) is not { } dotted || !dotted.StartsWith("matlab.", StringComparison.Ordinal)
            || LookUp("matlab", env, out _))
        {
            return false;
        }

        if (BuiltinClass(dotted) is { } whole)
        {
            value = autoCall ? whole.ConstructorValue.AsCallable.Call([], member.Line, member.Column) : whole.ConstructorValue;
            return true;
        }

        int last = dotted.LastIndexOf('.');
        return BuiltinClass(dotted[..last]) is { } supplied
            && TryClassMember(supplied, dotted[(last + 1)..], member, autoCall, out value);
    }

    /// <summary>A chain of plain dots as one name (<c>a.b.c</c>), or null when a step is anything else.</summary>
    private static string? DottedName(MemberExpr member)
    {
        if (member.Field is not { } field)
        {
            return null;
        }

        return member.Target switch
        {
            VariableExpr root => root.Name + "." + field,
            MemberExpr inner when DottedName(inner) is { } head => head + "." + field,
            _ => null,
        };
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
        if (LookUp(name, env, out JgsValue bound) && bound.Type != JgsType.Function)
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
    /// The class a saved object names, loading its file if this is the first mention of it (V6,
    /// #111): <c>load</c>'s question, asked of the path alone — a variable of the same name does not
    /// come into it, because the name here came from a file rather than from the script. Null when
    /// no file on the path defines the class.
    /// </summary>
    internal JgsClass? ClassForLoad(string name)
    {
        if (!_classes.ContainsKey(name))
        {
            _ = TryResolveOnPath(name, out _);
        }

        return _classes.TryGetValue(name, out JgsClass? definition) ? definition : BuiltinClass(name); // matlab.apps.AppBase, named as text (U7)
    }

    /// <summary>
    /// Reads <c>ClassName.name</c>: a <c>Constant</c> property or a <c>Static</c> method. Answers false
    /// for anything else, so the caller can go on to the other meanings a dot has.
    /// </summary>
    private bool TryClassMember(
        JgsClass definition, string field, MemberExpr member, bool autoCall, out JgsValue value)
    {
        if (definition.Property(field) is { Constant: true } constant)
        {
            RequireGetAccess(definition, constant, field, member); // U6
        }

        if (definition.TryConstant(field, out value))
        {
            return true;
        }

        if (definition.TryMethod(field, out ClassMethod? method) && method.Static)
        {
            if (FindMethod(definition, field, null, false, out _) != MethodAnswer.Found)
            {
                throw JgsClass.Restricted(field, method.Owner ?? definition, member.Line, member.Column);
            }

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

        if (ResolveObjectTarget(member.Target, env) is not { } holder)
        {
            return false;
        }

        JgsObject instance = holder.AsObject;
        string field = FieldName(member, env);
        JgsClass definition = instance.Class;
        if (definition.Property(field) is not { } property)
        {
            throw definition.TryMethod(field, out _)
                ? new JgsRuntimeException(member.Line, member.Column,
                    $"'{field}' is a method of {definition.Name}, not a property, so it cannot be assigned to.")
                : new JgsRuntimeException(member.Line, member.Column, "MATLAB:noPublicFieldForClass",
                    $"Unrecognized property '{field}' for class '{definition.Name}'.");
        }

        if (property.Constant)
        {
            throw new JgsRuntimeException(member.Line, member.Column,
                $"{definition.Name}.{field} is Constant, so it belongs to the class and cannot be assigned to.");
        }

        RequireSetAccess(definition, property, field, member.Line, member.Column); // U6: who may write it

        // AbortSet (U6, measured): a write of a value isequal to the one held is no write - no set
        // method, no PreSet, no PostSet, and the held value keeps its class.
        if (property.AbortSet && instance.Fields.TryGetValue(field, out JgsValue? unchanged)
            && JgsBuiltins.IsEqualValues(unchanged, value))
        {
            return true;
        }

        // A property with a set method is written by that method (V6, #27, #28) - except inside the
        // method's own body, where obj.p = v is the store - after the declaration's checks, which
        // R2025b runs first (a refused value never reaches the set method). A handle class's set
        // method writes the object in place; a value class's returns the object the property was
        // set on, which goes back where the object was read from, as o = set.p(o, v) would. A
        // Dependent property with no set method is refused in R2025b's words.
        if ((definition.TrySetter(field, out _) || property.Dependent) && !InAccessor(definition.SetterTag(field)))
        {
            JgsValue verified = definition.Check(property, CopyForBinding(value), member.Line, member.Column);
            JgsValue set = definition.CallSetter(field, holder, verified, member.Line, member.Column);
            if (!ReferenceEquals(set, holder))
            {
                var outer = new AssignExpr(member.Target, TokenType.Assign, new PreEvaluated(set) { Line = member.Line, Column = member.Column })
                {
                    Line = member.Line,
                    Column = member.Column,
                };
                EvaluateAssign(outer, env);
            }

            return true;
        }

        StoreProperty(holder, definition, property, field, value, member.Line, member.Column);
        return true;
    }

    /// <summary>The store a property write ends in, once its access and its set method have had their say.</summary>
    private void StoreProperty(
        JgsValue holder, JgsClass definition, ClassProperty property, string field, JgsValue value, int line, int col)
    {
        // A SetObservable property with a listener raises PreSet before the write and PostSet after
        // it (V6, #108) — inside the statement, whose operands were read before it began (M5). An
        // object nobody listens to pays one null test.
        bool observed = property.Observable && holder.AsObject.HasPropertyListener(field);
        if (observed)
        {
            JgsBuiltins.FirePropertyEvent(holder, field, post: false);
        }

        // M7: the write gate. The entry holds this very wrapper (M2), so detaching here is what
        // gives the entry its own instance — nothing has to be written back.
        Dictionary<string, JgsValue> fields = holder.WritableFields();
        JgsValue stored = definition.Check(property, CopyForBinding(value), line, col);
        fields.TryGetValue(field, out JgsValue? replaced);
        fields[field] = stored;
        JgsLifetime.Stored(holder, replaced, stored); // V10: the property's lifetime moves with the write
        NoteTrackedStore(stored);
        if (definition.IsComponentContainer)
        {
            JgsComponentContainers.MarkOwed(holder.AsObject); // any write owes the component an update (U10)
        }
        if (observed)
        {
            JgsBuiltins.FirePropertyEvent(holder, field, post: true);
        }
    }

    /// <summary>
    /// Reads a property by name as <c>obj.name</c> would (U6): the access the running code has,
    /// the get method, the constant, the storage. What <c>get</c> on a
    /// <c>matlab.mixin.SetGet</c> object asks.
    /// </summary>
    internal JgsValue ReadProperty(JgsValue target, string field, int line, int col)
    {
        JgsObject instance = target.AsObject;
        JgsClass definition = instance.Class;
        var at = new VariableExpr(field) { Line = line, Column = col };
        ClassProperty property = definition.Property(field) ?? throw NoSuchMember(field, definition, at);
        RequireGetAccess(definition, property, field, at);
        if (!property.Constant && (definition.TryGetter(field, out _) || property.Dependent))
        {
            return definition.CallGetter(field, target, line, col);
        }

        return instance.Fields.TryGetValue(field, out JgsValue? held) ? held
            : definition.TryConstant(field, out JgsValue constant) ? constant
            : throw NoSuchMember(field, definition, at);
    }

    /// <summary>
    /// Writes a property of a handle object by name as <c>obj.name = value</c> would (U6): the
    /// access the running code has, AbortSet, the declaration's checks, the set method, the
    /// listeners. What <c>set</c> on a <c>matlab.mixin.SetGet</c> object asks.
    /// </summary>
    internal void WriteProperty(JgsValue target, string field, JgsValue value, int line, int col)
    {
        JgsObject instance = target.AsObject;
        JgsClass definition = instance.Class;
        ClassProperty property = definition.Property(field)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                $"Unrecognized property '{field}' for class '{definition.Name}'.");
        if (property.Constant)
        {
            throw new JgsRuntimeException(line, col,
                $"{definition.Name}.{field} is Constant, so it belongs to the class and cannot be assigned to.");
        }

        RequireSetAccess(definition, property, field, line, col);
        if (property.AbortSet && instance.Fields.TryGetValue(field, out JgsValue? unchanged)
            && JgsBuiltins.IsEqualValues(unchanged, value))
        {
            return;
        }

        if (definition.TrySetter(field, out _) || property.Dependent)
        {
            definition.CallSetter(field, target, definition.Check(property, CopyForBinding(value), line, col), line, col);
            return;
        }

        StoreProperty(target, definition, property, field, value, line, col);
    }

    /// <summary>
    /// The object a dotted write is aimed at, or null when the write is not about an object, as the
    /// entry's own wrapper rather than the bare instance: M7's gate lives on the wrapper, and under
    /// M2 the entry holds exactly this wrapper, so a detach through it lands in the entry. The
    /// object is found along any entry path — a bound name, a field, a cell slot, an element of a
    /// struct array, a property of another object (V6, #137, #138: <c>t.h.data = v</c> and
    /// <c>c{1}.data = v</c> on a handle held in a container) — each level made writable on the way,
    /// which is M3's detach at every level; a path that is not an entry is never evaluated on the
    /// chance that it is one. The name is read the way every other write reads it
    /// (<see cref="LookUp"/>), so a frame that declared it global finds the object in the global
    /// workspace (V4, ADR 0165). Nothing is walked until a class has been loaded.
    /// </summary>
    private JgsValue? ResolveObjectTarget(Expr expr, JgsEnvironment env)
    {
        if (!AnyClasses)
        {
            return null;
        }

        if (ResolveEntry(expr, env) is not { Type: JgsType.Object } held)
        {
            return null;
        }

        RequireLive(held.AsObject, expr.Line, expr.Column);
        return held;
    }

    /// <summary>
    /// Refuses a dot on a deleted handle object in MATLAB's words (V6, appendix A #104): a read, a
    /// write and a method call all go through here, so an alias of a deleted object is refused the
    /// same way the deleted name is. <c>isvalid</c> and <c>delete</c> take the object as an
    /// argument rather than through a dot, and are the two things still allowed of it.
    /// </summary>
    private static void RequireLive(JgsObject instance, int line, int column)
    {
        if (instance.Deleted)
        {
            throw new JgsRuntimeException(line, column, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
        }
    }

    /// <summary>
    /// The wrapper an entry path names, made writable at every level on the way (M3), or null
    /// where the path is not such an entry or names nothing yet.
    /// </summary>
    private JgsValue? ResolveEntry(Expr expr, JgsEnvironment env)
    {
        switch (expr)
        {
            case VariableExpr variable:
                return LookUp(variable.Name, env, out JgsValue bound) ? bound : null;

            case MemberExpr member:
            {
                if (ResolveEntry(member.Target, env) is not { } owner)
                {
                    return null;
                }

                string field = FieldName(member, env);
                if (owner.Type == JgsType.Object)
                {
                    RequirePassThrough(owner, field, member); // U6
                    return owner.WritableFields().TryGetValue(field, out JgsValue? property) ? property : null;
                }

                // A plain struct, or an event's data (V6, #108): evt.AffectedObject.a = 0 inside a
                // PostSet reaches the object the event carries. Every other class-named struct is
                // a value with rules of its own (a dictionary, a map) and is not walked into.
                if (owner.Type == JgsType.Struct && !owner.IsStructArray
                    && owner.ClassName is null or JgsBuiltins.EventDataClassName or JgsBuiltins.PropertyEventClassName)
                {
                    return owner.WritableStruct().TryGetValue(field, out JgsValue? held) ? held : null;
                }

                return null;
            }

            case BraceIndexExpr { Indices.Count: 1 } brace:
            {
                if (ResolveEntry(brace.Target, env) is not { Type: JgsType.Cell } owner)
                {
                    return null;
                }

                if (EvaluateIndexArgument(brace.Indices[0], owner.ArrayLength, env) is not { Type: JgsType.Number } index)
                {
                    return null;
                }

                int slot = (int)index.AsNumber - Dialect.IndexBase;
                JgsValue[] slots = owner.WritableCell();
                return slot >= 0 && slot < slots.Length ? slots[slot] : null;
            }

            case CallExpr { Arguments.Count: 1 } call when IsEntryPath(call.Callee, env)
                && TryElementOfEntry(call.Callee, call.Arguments[0], env, out JgsValue? ofCall, out _):
                return ofCall;

            case IndexExpr { Indices.Count: 1 } indexed when IsEntryPath(indexed.Target, env)
                && TryElementOfEntry(indexed.Target, indexed.Indices[0], env, out JgsValue? ofIndex, out _):
                return ofIndex;

            default:
                return null;
        }
    }

    /// <summary>
    /// The class method a call written <c>name(…, obj, …)</c> reaches on <paramref name="dominant"/>,
    /// the user object the resolver picked as dominant (the leftmost one). MATLAB dispatches a call
    /// on the class of its arguments: <c>area(c)</c> on a Circle is the class's own method, not the
    /// chart verb of the same name. This is the user-method layer of the search order (M145): below
    /// a bound name, a nested or local function and a private file, above the folders and the
    /// built-ins — the resolver asks it in that place rather than before the name is looked up.
    /// </summary>
    internal bool TryUserMethod(string name, JgsValue dominant, [NotNullWhen(true)] out IJgsCallable? callable) =>
        TryUserMethod(name, dominant, null, false, out callable);

    /// <summary>
    /// The same, asked on behalf of <paramref name="context"/> when it is known - the class a
    /// handle was made in, or the class whose own method a bare name found (U6) - rather than of
    /// whatever is running. A method the caller may not have comes back as a call that refuses in
    /// R2025b's words, so the refusal carries the call's place.
    /// </summary>
    internal bool TryUserMethod(
        string name, JgsValue dominant, JgsClass? context, bool contextKnown, [NotNullWhen(true)] out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.Type == JgsType.External)
        {
            return JgsBuiltins.TryDeviceMethod(name, dominant, out callable)
                || JgsBuiltins.TryExternalArrayMethod(this, name, dominant, out callable)
                || JgsBuiltins.TryLibMethod(this, name, dominant, out callable) || TryNetMethod(name, dominant, out callable);
        }

        if (dominant.Type != JgsType.Object)
        {
            return false;
        }

        JgsClass definition = dominant.AsObject.Class;
        MethodAnswer answer = FindMethod(definition, name, context, contextKnown, out ClassMethod? method);
        if (answer == MethodAnswer.Missing || method is null || method.Static)
        {
            return false;
        }

        if (answer == MethodAnswer.Refused)
        {
            callable = new RefusedMethod(name, method.Owner ?? definition);
            return true;
        }

        // A handle class's own delete is its destructor: once it has run, the object is deleted for
        // every alias (V6, #104), and a second delete runs nothing. The mark is made after the body
        // so the body may still read its own properties.
        callable = name == "delete" && definition.IsHandle
            ? new DestructorCall(dominant.AsObject)
            : definition.Callable(method);
        return true;
    }

    /// <summary>A method the caller may not call: calling it is R2025b's refusal, at the call.</summary>
    private sealed class RefusedMethod(string name, JgsClass owner) : IJgsCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            throw JgsClass.Restricted(name, owner, line, column);
    }

    /// <summary>
    /// A handle object's destructors - its class's <c>delete</c> and then each superclass's (U6,
    /// measured) - followed by the mark that ends the object.
    /// </summary>
    private sealed class DestructorCall(JgsObject instance) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => "delete";

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
        {
            JgsValue[] answers = CallMultiple(arguments, 1, line, column);
            return answers.Length > 0 ? answers[0] : JgsValue.Null;
        }

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
        {
            if (instance.Deleted || instance.Destroying)
            {
                return [];
            }

            // R2025b's order (U7, measured): the object stops being valid, its listeners hear
            // ObjectBeingDestroyed, and then the class's own delete runs - all three before the
            // properties go.
            instance.Destroying = true;
            try
            {
                JgsBuiltins.FireObjectBeingDestroyed(instance);
                foreach (IJgsCallable destructor in instance.Class.Destructors())
                {
                    destructor.Call(arguments.Count > 0 ? [arguments[0]] : [JgsValue.Object(instance)], line, column);
                }

                return [];
            }
            finally
            {
                instance.MarkDeleted();
                JgsLifetime.ObjectDeleted(instance); // V10: the properties' contents go with the object
            }
        }
    }

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
        if (left.Type == JgsType.Object && right.Type == JgsType.Object && right.AsObject.Class.Dominates(definition))
        {
            definition = right.AsObject.Class; // InferiorClasses (U6): the right operand's class said so
        }

        string? wanted = OperatorMethodName(op);
        if (wanted is null || !definition.TryMethod(wanted, out ClassMethod? method) || method.Static)
        {
            // Two handles are equal when they are the one object (V6, #140): the eq a handle class
            // has without defining one. A class that defines its own has decided otherwise above.
            if (wanted is "eq" or "ne"
                && left.Type == JgsType.Object && right.Type == JgsType.Object
                && left.AsObject.Class.IsHandle && right.AsObject.Class.IsHandle)
            {
                bool same = ReferenceEquals(left.AsObject, right.AsObject);
                result = JgsValue.Bool(wanted == "eq" ? same : !same);
                return true;
            }

            // A custom component against handles - f.Children(1) == c (U10, measured) - is its
            // area's handle against them.
            if (wanted is "eq" or "ne" && (left.Type == JgsType.Object) != (right.Type == JgsType.Object))
            {
                JgsValue component = left.Type == JgsType.Object ? left : right;
                if (JgsComponentContainers.TryEntry(component, out JgsHandleEntry? area))
                {
                    JgsValue handle = JgsHandleRegistry.For(area.Target);
                    result = ApplyBinary(op, left.Type == JgsType.Object ? handle : left, right.Type == JgsType.Object ? handle : right, at);
                    return true;
                }
            }

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

}
