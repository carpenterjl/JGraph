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

        if (head is not VariableExpr root || !IsUnclaimed(root.Name, env))
        {
            return false;
        }

        chain.Reverse();
        string[] names = new string[chain.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = FieldName(chain[i], env);
        }

        if (!NetTypes.IsNamespace(root.Name))
        {
            // Nothing claims the head, so the name cannot be resolved; R2025b says so with the whole
            // dotted name (MATLAB:undefinedVarOrClass).
            throw Unresolved(root.Name + "." + string.Join(".", names), member);
        }

        (Type type, int next) = TypeInChain(root.Name, names, member);
        AnyNet = true;
        if (next == names.Length)
        {
            value = autoCall
                ? NetInvoke.Construct(type, [], member.Line, member.Column, NetTypes)
                : JgsValue.Function(NetCallable.Constructor(type, NetTypes));
            return true;
        }

        value = NetInvoke.StaticMember(type, names[next], next == names.Length - 1 ? autoCall : true, member.Line, member.Column, NetTypes);
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
                return NetInvoke.Member(net, field, autoCall, member.Line, member.Column, NetTypes);
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

        if (held.AsExternal is not NetObject net)
        {
            throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:class:SetProhibited",
                $"The properties of a {held.AsExternal.ClassName} cannot be set.");
        }

        NetInvoke.SetMember(net, FieldName(member, env), value, member.Line, member.Column);
        return true;
    }

    /// <summary>
    /// A .NET method reached by function syntax — <c>Describe(m)</c> — on the .NET object among the
    /// arguments: the object is the receiver, and the rest are the method's arguments.
    /// </summary>
    internal bool TryNetMethod(string name, JgsValue dominant, [NotNullWhen(true)] out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.Type != JgsType.External || dominant.AsExternal is not NetObject net
            || NetInvoke.Methods(net.Type, name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static).Length == 0)
        {
            return false;
        }

        callable = new NetFunctionSyntax(net.Type, name, dominant, NetTypes);
        return true;
    }

    /// <summary><c>Method(obj, args…)</c> for a .NET object: the object leaves the argument list and receives the call.</summary>
    private sealed class NetFunctionSyntax(Type type, string method, JgsValue receiver, NetCatalog session) : IJgsCallable
    {
        public string Name => method;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
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

            return NetInvoke.Call(type, method, (NetObject)receiver.AsExternal, rest, line, column, session);
        }
    }
}
