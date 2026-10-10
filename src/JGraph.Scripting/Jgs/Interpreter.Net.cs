using System.Diagnostics.CodeAnalysis;
using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's half of the .NET interface (interop plan, stage 1, ADR 0174): a dotted name
/// that nothing else claims is looked up in the .NET type catalog, a type in front of a dot names its
/// static members without constructing it, and a .NET value answers a dot with its members.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where .NET sits in name resolution.</b> A dotted name is .NET's only when its head is bound to
/// nothing — no variable, no function in scope or built-in, no file on the path — and the head is a
/// namespace the catalog knows (<c>System</c>, or an added assembly's <c>JGTest</c>). Asking in that
/// order keeps the framework's index from being built for a script that never names .NET: an
/// ordinary struct read stops at the variable.
/// </para>
/// <para>
/// <b>The type in front of a dot</b> follows <see cref="TryClassInFront"/>'s rule for user classes:
/// <c>System.DateTime.Now</c> reads a static property of the type, and only a type with nothing after
/// it constructs — called with its arguments, or bare with none, which is what makes
/// <c>class(JGTest.Members)</c> answer <c>JGTest.Members</c> (R2025b, net_basics).
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    private NetCatalog? _netTypes;

    /// <summary>The .NET types this session can name: the framework's, and its added assemblies'.</summary>
    internal NetCatalog NetTypes => _netTypes ??= new NetCatalog();

    /// <summary>
    /// Whether this session has touched .NET, which is what makes a call of an unknown name worth
    /// evaluating: <c>Describe(m)</c> can still find a method on a .NET object among its arguments.
    /// </summary>
    internal bool AnyNet { get; private set; }

    /// <summary>Records that .NET values are about, from a built-in that made one.</summary>
    internal void NoteNet() => AnyNet = true;

    /// <summary>
    /// A .NET array's subscripts, evaluated in order: <c>:</c> as null, and an <c>end</c> anywhere in
    /// one refused, since a .NET object has no <c>end</c> (ADR 0177).
    /// </summary>
    private JgsValue?[] NetSubscripts(IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        var values = new JgsValue?[subscripts.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = EvaluateIndexArgument(subscripts[i], () => throw NetArrays.EndRefused(at.Line, at.Column), i, env);
        }

        return values;
    }

    /// <summary>
    /// The member expression a statement is made of alone (<c>m.Bump;</c>), whose bare call asks for
    /// no output; every other bare mention asks for one, so <c>x = m.Bump</c> is refused for a
    /// <c>void</c> method as <c>x = m.Bump()</c> is (measured, probe2; ADR 0175).
    /// </summary>
    private MemberExpr? _bareStatement;

    /// <summary>How many outputs a bare mention of <paramref name="member"/> asks for.</summary>
    private int BareWanted(MemberExpr member) => ReferenceEquals(member, _bareStatement) ? 0 : 1;

    /// <summary>Evaluates a statement that is one member expression, its bare call asking for nothing.</summary>
    private JgsValue EvaluateBareStatement(MemberExpr member, JgsEnvironment env)
    {
        MemberExpr? outer = _bareStatement;
        _bareStatement = member;
        try
        {
            return Evaluate(member, env);
        }
        finally
        {
            _bareStatement = outer;
        }
    }

    /// <summary>
    /// Reads a dotted name whose head is a .NET namespace: the constructor or a static member of the
    /// type it reaches, then any members after that. False when the head is bound to something else,
    /// so the ordinary readings of a dot go ahead.
    /// </summary>
    private bool TryNetInFront(MemberExpr member, JgsEnvironment env, bool autoCall, out JgsValue value)
    {
        value = JgsValue.Null;
        var chain = new List<MemberExpr>();
        Expr head = member;
        while (head is MemberExpr link)
        {
            chain.Add(link);
            head = link.Target;
        }

        if (head is not VariableExpr root)
        {
            return false;
        }

        // An imported head (ADR 0176): an explicit import above everything but a variable, a
        // wildcard's type or namespace below nested and local functions only.
        NetImported? imported = null;
        if (AnyImports && ImportFor(root.Name, env, head: true) is { } candidate)
        {
            Resolution held = _resolver.Lookup(root.Name, env);
            if (held.Layer != ResolutionLayer.Bound
                && (candidate.Explicit || held.Layer is not (ResolutionLayer.Nested or ResolutionLayer.Local)))
            {
                imported = candidate;
            }
        }

        if (imported is null && !IsUnclaimed(root.Name, env))
        {
            return false;
        }

        chain.Reverse();
        string[] names = new string[chain.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = FieldName(chain[i], env);
        }

        Type type;
        int next;
        if (imported is { Type: { } importedType, Method: null })
        {
            (type, next) = (importedType, 0);
        }
        else if (imported is { Namespace: { } space })
        {
            (type, next) = TypeInChain(space, names, member);
        }
        else if (imported is not null)
        {
            // An imported method in front of a dot: its answer is what the dot reads.
            return false;
        }
        else if (!NetTypes.IsNamespace(root.Name))
        {
            // A builtin known by its whole dotted name — a MATLAB class constructor such as
            // matlab.ui.layout.GridLayoutOptions (open item 48) — then any members after it.
            for (int upTo = names.Length; upTo >= 1; upTo--)
            {
                string dotted = root.Name + "." + string.Join(".", names[..upTo]);
                if (Globals.Builtins.TryGet(dotted, out JgsValue builtin) && builtin.Type == JgsType.Function)
                {
                    bool whole = upTo == names.Length;
                    value = whole && !autoCall ? builtin : builtin.AsCallable.Call([], member.Line, member.Column);
                    for (int i = upTo; i < names.Length; i++)
                    {
                        value = MemberOf(value, names[i], chain[i], i == names.Length - 1 ? autoCall : true);
                    }

                    return true;
                }
            }

            // Nothing claims the head, so the name cannot be resolved; R2025b says so with the whole
            // dotted name (MATLAB:undefinedVarOrClass).
            throw Unresolved(root.Name + "." + string.Join(".", names), member);
        }
        else
        {
            (type, next) = TypeInChain(root.Name, names, member);
        }

        AnyNet = true;
        if (next == names.Length)
        {
            value = autoCall
                ? NetInvoke.Construct(type, [], member.Line, member.Column, NetTypes)
                : JgsValue.Function(NetCallable.Constructor(type, NetTypes));
            return true;
        }

        bool last = next == names.Length - 1;
        value = NetInvoke.StaticMember(type, names[next], !last || autoCall, member.Line, member.Column, NetTypes,
            last ? BareWanted(member) : 1);
        for (int i = next + 1; i < names.Length; i++)
        {
            value = MemberOf(value, names[i], chain[i], i == names.Length - 1 ? autoCall : true);
        }

        return true;
    }

    /// <summary>
    /// Walks <paramref name="names"/> after a namespace head through deeper namespaces to the type they
    /// reach. Answers the type and the index of the first name after it; a chain that ends on a
    /// namespace, or names no type, is R2025b's <c>MATLAB:undefinedVarOrClass</c>.
    /// </summary>
    private (Type Type, int Next) TypeInChain(string head, IReadOnlyList<string> names, Node at)
    {
        string space = head;
        int i = 0;
        while (i < names.Count && NetTypes.IsNamespace(space + "." + names[i]))
        {
            space += "." + names[i];
            i++;
        }

        if (i == names.Count || NetTypes.TypeNamed(space + "." + names[i]) is not { } type)
        {
            throw Unresolved(i == names.Count ? space : space + "." + names[i], at);
        }

        return (type, i + 1);
    }

    private static JgsRuntimeException Unresolved(string dotted, Node at) =>
        new(at.Line, at.Column, "MATLAB:undefinedVarOrClass", $"Unable to resolve the name '{dotted}'.");

    /// <summary>
    /// Whether nothing but .NET could claim <paramref name="name"/>: no variable or function in scope,
    /// no built-in, and no file on the path.
    /// </summary>
    /// <remarks>The path is asked through its index, not the disk: this runs on every mention of a
    /// .NET name, and a probe per folder cost more than the call the name made.</remarks>
    private bool IsUnclaimed(string name, JgsEnvironment env) =>
        !_resolver.Lookup(name, env).Found && !_classes.ContainsKey(name)
        && !(FunctionPath is { } path && path.Index.Holds(name));

    /// <summary>
    /// The .NET reading of a whole dotted name given as text — <c>feval('System.Math.Max', …)</c>,
    /// <c>@System.Math.Max</c>, <c>which</c> and <c>exist</c>: a type, or a static member of one.
    /// </summary>
    internal bool TryNetName(string dotted, JgsEnvironment env, out Type? type, out string? member)
    {
        type = null;
        member = null;
        string[] parts = dotted.Split('.');
        if (parts.Length < 2 || !IsUnclaimed(parts[0], env) || !NetTypes.IsNamespace(parts[0]))
        {
            return false;
        }

        string space = parts[0];
        int i = 1;
        while (i < parts.Length && NetTypes.IsNamespace(space + "." + parts[i]))
        {
            space += "." + parts[i];
            i++;
        }

        if (i == parts.Length || NetTypes.TypeNamed(space + "." + parts[i]) is not { } found || parts.Length - i > 2)
        {
            return false;
        }

        type = found;
        member = parts.Length - i == 2 ? parts[i + 1] : null;
        AnyNet = true;
        return true;
    }

    /// <summary>Whether a dotted name is a .NET namespace this session knows.</summary>
    internal bool IsNetNamespace(string dotted, JgsEnvironment env) =>
        IsUnclaimed(dotted.Split('.')[0], env) && NetTypes.IsNamespace(dotted);

    /// <summary><c>@System.Math.Max</c>: a static method group, or a type's constructor.</summary>
    private bool TryNetHandle(string dotted, JgsEnvironment env, out JgsValue handle)
    {
        handle = JgsValue.Null;
        if (!TryNetName(dotted, env, out Type? type, out string? member))
        {
            return false;
        }

        handle = JgsValue.Function(member is null ? NetCallable.Constructor(type!, NetTypes) : new NetCallable(type!, member, receiver: null, NetTypes));
        return true;
    }

    /// <summary><c>?Name</c>: the metaclass of a .NET type or of a user class.</summary>
    private JgsValue EvaluateMetaClass(MetaClassExpr meta, JgsEnvironment env)
    {
        if (meta.Name.Contains('.', StringComparison.Ordinal) && TryNetName(meta.Name, env, out Type? type, out string? member)
            && member is null)
        {
            return JgsValue.External(new NetMetaClass(type!));
        }

        if (ClassNamed(meta.Name, env) is { } definition)
        {
            return JgsBuiltins.MetaClassOf(definition);
        }

        throw Unresolved(meta.Name, meta);
    }

    /// <summary><c>meta.class.fromName</c>: a .NET type's metaclass, a user class's, or an empty for neither.</summary>
    internal JgsValue MetaClassFromName(string name)
    {
        if (TryNetName(name, CurrentFrame, out Type? type, out string? member) && member is null)
        {
            return JgsValue.External(new NetMetaClass(type!));
        }

        return ClassForLoad(name) is { } definition ? JgsBuiltins.MetaClassOf(definition) : JgsEmpty.Zero();
    }

    /// <summary>A dot on an external value: a .NET object's member, a metaclass's name, an assembly's lists.</summary>
    private JgsValue ExternalMember(JgsValue target, string field, MemberExpr member, bool autoCall)
    {
        switch (target.AsExternal)
        {
            case NetObject net:
                return NetInvoke.Member(net, field, autoCall, member.Line, member.Column, NetTypes, BareWanted(member));
            case var lib when JgsBuiltins.IsLibValue(lib):
                return JgsBuiltins.LibMember(lib, field, member.Line, member.Column); // lib.pointer, libstruct (ADR 0182)
            case Devices.DeviceObject device:
                return device.Member(field, autoCall, BareWanted(member), member.Line, member.Column); // serialport (device classes plan)
            case IJgsExternalArray objects:
                return ExternalArrayMember(objects, field, member); // midimsg (device classes plan, stage D10b)
            case NetMetaClass meta when field == "Name":
                return JgsValue.Str(NetNames.ClassName(meta.Type));
            case NetAssemblyValue assembly when NetAssemblyValue.PropertyNames.Contains(field):
                return assembly.Property(field);
            default:
                throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:noSuchMethodOrField",
                    $"Unrecognized method, property, or field '{field}' for class '{target.AsExternal.ClassName}'.");
        }
    }

    /// <summary>
    /// Writes <c>obj.name = value</c> when <c>obj</c> is a variable holding a .NET object. A value
    /// type is written in the variable's own copy, which is the copy MATLAB gives each name.
    /// </summary>
    private bool TryAssignToNet(MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        if (member.Target is not VariableExpr holder || !LookUp(holder.Name, env, out JgsValue held)
            || held.Type != JgsType.External)
        {
            return false;
        }

        // s.BaudRate = v on a serialport (device classes plan).
        if (held.AsExternal is Devices.DeviceObject device)
        {
            device.SetProperty(FieldName(member, env), value,
                new Devices.DeviceCall { Target = device, Args = [], Line = member.Line, Column = member.Column }, ignoreCase: false);
            return true;
        }

        // p.Value = v on a lib.pointer, s.x = v on a libstruct (ADR 0182).
        if (JgsBuiltins.IsLibValue(held.AsExternal))
        {
            JgsBuiltins.LibAssign(Host, held.AsExternal, FieldName(member, env), value, member.Line, member.Column);
            return true;
        }

        if (held.AsExternal is not NetObject net)
        {
            throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:class:SetProhibited",
                $"The properties of a {held.AsExternal.ClassName} cannot be set.");
        }

        NetInvoke.SetMember(net, FieldName(member, env), value, member.Line, member.Column);
        return true;
    }

    /// <summary>
    /// Refuses <c>obj.Method(i) = v</c> on a variable holding a .NET object when <c>Method</c> is one of
    /// its methods — the indexer's <c>Item</c> is the case that matters. R2025b refuses the write
    /// rather than calling the indexer's setter (measured, net_members).
    /// </summary>
    private void RefuseNetTemporary(MemberExpr member, JgsEnvironment env)
    {
        if (member.Target is VariableExpr holder && LookUp(holder.Name, env, out JgsValue held)
            && held.AsExternalOrNull() is NetObject net && FieldName(member, env) is var name
            && NetInvoke.HasMethod(net.Type, name, instance: true))
        {
            throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:index:assignmentToTemporary",
                $"Assignment not supported because the result of method '{name}' is a temporary value.");
        }
    }

    /// <summary>
    /// A .NET method reached by function syntax — <c>Describe(m)</c> — on the .NET object among the
    /// arguments: the object is the receiver, and the rest are the method's arguments.
    /// </summary>
    internal bool TryNetMethod(string name, JgsValue dominant, [NotNullWhen(true)] out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.Type != JgsType.External || dominant.AsExternal is not NetObject net
            || !NetInvoke.HasMethod(net.Type, name, instance: true))
        {
            return false;
        }

        callable = new NetFunctionSyntax(net.Type, name, dominant, NetTypes);
        return true;
    }

    /// <summary><c>Method(obj, args…)</c> for a .NET object: the object leaves the argument list and receives the call.</summary>
    private sealed class NetFunctionSyntax(Type type, string method, JgsValue receiver, NetCatalog session) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => method;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column)[0];

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
        {
            var rest = new List<JgsValue>(arguments.Count);
            bool dropped = false;
            foreach (JgsValue argument in arguments)
            {
                if (!dropped && ReferenceEquals(argument.AsExternalOrNull(), receiver.AsExternal))
                {
                    dropped = true;
                    continue;
                }

                rest.Add(argument);
            }

            return NetInvoke.Call(type, method, (NetObject)receiver.AsExternal, rest, wanted, line, column, session);
        }
    }
}
