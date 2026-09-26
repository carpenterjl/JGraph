using System.Reflection;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// Reading, writing and calling .NET members for a script (interop plan, stage 1, ADR 0174): which
/// overload a call picks, how the arguments cross, what comes back, and how a .NET exception
/// becomes a <c>NET.NetException</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Overloads.</b> The candidates are the public members of the name that take exactly as many
/// arguments as were given. Each argument ranks each candidate's parameter by
/// <see cref="NetConvert.Rank"/>; a candidate some argument cannot reach drops out, and the lowest
/// total wins, the first declared on a tie. That reproduces every single-argument pair R2025b
/// recorded and <c>System.Math.Max(int32(3), 7)</c> answering the double overload; stage 2 fits
/// the several-argument rule, <c>params</c>, optional, <c>ref</c> and <c>out</c> parameters.
/// </para>
/// <para>
/// <b>Exceptions.</b> Whatever a member throws is caught here and raised as a <c>NET.NetException</c>:
/// identifier <c>MATLAB:NET:CLRException:&lt;context&gt;</c> (<c>MethodInvoke</c>, <c>CreateObject</c>,
/// <c>PropertyGet</c>, <c>PropertySet</c>), message <c>Message: …</c> / <c>Source: …</c> /
/// <c>HelpLink: …</c>, and the exception itself as <c>ExceptionObject</c> (measured, net_exceptions).
/// It must be caught here: a .NET <c>NullReferenceException</c> or <c>ArgumentException</c> that
/// escaped would read to the interpreter as a defect of its own.
/// </para>
/// </remarks>
internal static class NetInvoke
{
    private const BindingFlags Public = BindingFlags.Public | BindingFlags.FlattenHierarchy;

    /// <summary>The class name MATLAB gives the exception it raises for a .NET one.</summary>
    internal const string NetExceptionClass = "NET.NetException";

    // --- members -------------------------------------------------------------------------------

    /// <summary>
    /// Reads <c>target.name</c> on a .NET object: a property or field (an instance's or its type's),
    /// or a method, called now when the mention is bare (<paramref name="autoCall"/>) and handed back
    /// as a callable otherwise.
    /// </summary>
    public static JgsValue Member(NetObject target, string name, bool autoCall, int line, int col, NetCatalog? session = null)
    {
        if (target.NullableOf is { } under)
        {
            return NullableMember(target, under, name, line, col, session);
        }

        Type type = target.Type;
        if (ReadableProperty(type, name, BindingFlags.Instance | BindingFlags.Static) is { } property)
        {
            return Get(property, property.GetGetMethod()!.IsStatic ? null : target.Target, line, col, session);
        }

        if (type.GetField(name, Public | BindingFlags.Instance | BindingFlags.Static) is { } field)
        {
            return Read(field, field.IsStatic ? null : target.Target, line, col, session);
        }

        if (Methods(type, name, BindingFlags.Instance | BindingFlags.Static).Length > 0)
        {
            var bound = new NetCallable(type, name, target, session);
            return autoCall ? bound.Call([], line, col) : JgsValue.Function(bound);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class '{target.ClassName}'.");
    }

    /// <summary>Reads <c>Type.name</c>: a static property or field, or a static method.</summary>
    public static JgsValue StaticMember(Type type, string name, bool autoCall, int line, int col, NetCatalog? session = null)
    {
        if (ReadableProperty(type, name, BindingFlags.Static) is { } property)
        {
            return Get(property, null, line, col, session);
        }

        if (type.GetField(name, Public | BindingFlags.Static) is { } field)
        {
            return Read(field, null, line, col, session);
        }

        if (Methods(type, name, BindingFlags.Static).Length > 0)
        {
            var group = new NetCallable(type, name, receiver: null, session);
            return autoCall ? group.Call([], line, col) : JgsValue.Function(group);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:subscripting:classHasNoPropertyOrMethod",
            $"The class {NetNames.ClassName(type)} has no Constant property or Static method named '{name}'.");
    }

    /// <summary>Writes <c>target.name = value</c> on a property or field the object has.</summary>
    public static void SetMember(NetObject target, string name, JgsValue value, int line, int col)
    {
        Type type = target.Type;
        if (type.GetProperty(name, Public | BindingFlags.Instance | BindingFlags.Static) is { } property
            && property.GetIndexParameters().Length == 0)
        {
            MethodInfo setter = property.GetSetMethod()
                ?? throw ReadOnly(name, type, line, col);
            object? converted = Argument(value, property.PropertyType, 1, name, line, col);
            try
            {
                setter.Invoke(setter.IsStatic ? null : target.Target, [converted]);
            }
            catch (Exception fault) when (IsNetFault(fault))
            {
                throw Raise(fault, "PropertySet", line, col);
            }

            return;
        }

        if (type.GetField(name, Public | BindingFlags.Instance | BindingFlags.Static) is { } field)
        {
            if (field.IsLiteral || field.IsInitOnly)
            {
                throw ReadOnly(name, type, line, col);
            }

            field.SetValue(field.IsStatic ? null : target.Target, Argument(value, field.FieldType, 1, name, line, col));
            return;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
            $"Unrecognized property '{name}' for class '{target.ClassName}'.");
    }

    /// <summary>R2025b's refusal of a write to a read-only property or field (net_members), quotes and all.</summary>
    private static JgsRuntimeException ReadOnly(string name, Type type, int line, int col) =>
        new(line, col, "MATLAB:class:SetProhibited",
            $"Unable to set the '{name}' property of class ''{NetNames.ShortName(type)}'' because it is read-only.");

    /// <summary>A Nullable's two properties and its methods, read from the value it holds.</summary>
    private static JgsValue NullableMember(NetObject target, Type under, string name, int line, int col, NetCatalog? session) => name switch
    {
        "HasValue" => JgsValue.Bool(target.Target is not null),
        "Value" when target.Target is not null => NetConvert.ToMatlab(target.Target, under, line, col, session),
        "Value" => throw Raise(new InvalidOperationException("Nullable object must have a value."), "PropertyGet", line, col),
        "GetValueOrDefault" => NetConvert.ToMatlab(target.Target ?? Activator.CreateInstance(under), under, line, col),
        "ToString" => NetConvert.ToMatlab(target.Target?.ToString() ?? "", typeof(string), line, col),
        _ => throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class '{target.ClassName}'."),
    };

    /// <summary>A property that reads by name alone: public, with a getter, and not an indexer.</summary>
    public static PropertyInfo? ReadableProperty(Type type, string name, BindingFlags scope)
    {
        PropertyInfo? found = null;
        foreach (PropertyInfo property in type.GetProperties(Public | scope))
        {
            if (property.Name == name && property.GetIndexParameters().Length == 0 && property.GetGetMethod() is not null)
            {
                found ??= property; // the most derived comes first
            }
        }

        return found;
    }

    /// <summary>
    /// The public methods of a name MATLAB can call: ordinary methods, and a default indexer's
    /// accessors under the indexer's name (<c>list.Item(0)</c>). Open generic methods are left out
    /// (<c>NET.invokeGenericMethod</c> calls them).
    /// </summary>
    public static MethodInfo[] Methods(Type type, string name, BindingFlags scope)
    {
        var found = new List<MethodInfo>();
        foreach (MethodInfo method in type.GetMethods(Public | scope))
        {
            if (method.Name == name && !method.IsSpecialName && !method.ContainsGenericParameters)
            {
                found.Add(method);
            }
        }

        if ((scope & BindingFlags.Instance) != 0)
        {
            foreach (PropertyInfo indexer in type.GetProperties(Public | BindingFlags.Instance))
            {
                if (indexer.Name == name && indexer.GetIndexParameters().Length > 0)
                {
                    if (indexer.GetGetMethod() is { } get)
                    {
                        found.Add(get);
                    }

                    if (indexer.GetSetMethod() is { } set)
                    {
                        found.Add(set);
                    }
                }
            }
        }

        return [.. found];
    }

    private static JgsValue Get(PropertyInfo property, object? target, int line, int col, NetCatalog? session)
    {
        object? read;
        try
        {
            read = property.GetValue(target);
        }
        catch (Exception fault) when (IsNetFault(fault))
        {
            throw Raise(fault, "PropertyGet", line, col);
        }

        return NetConvert.ToMatlab(read, property.PropertyType, line, col, session);
    }

    // GetValue rather than the raw constant: an enum member's raw constant is its number, and the
    // member must come back as itself (System.DayOfWeek.Monday).
    private static JgsValue Read(FieldInfo field, object? target, int line, int col, NetCatalog? session) =>
        NetConvert.ToMatlab(field.GetValue(field.IsStatic ? null : target), field.FieldType, line, col, session);

    /// <summary>
    /// What <c>methods</c> lists for a .NET type, sorted as R2025b sorts it: the public methods,
    /// instance and static, an indexer under its name, the constructor under the type's short name,
    /// and the methods MATLAB gives every .NET object — <c>matlab.mixin.Scalar</c>'s, and
    /// <c>handle</c>'s for a reference type (measured, net_display).
    /// </summary>
    public static IEnumerable<string> MethodNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodInfo method in type.GetMethods(Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (!method.IsSpecialName && !method.ContainsGenericParameters)
            {
                names.Add(method.Name);
            }
        }

        foreach (PropertyInfo indexer in type.GetProperties(Public | BindingFlags.Instance))
        {
            if (indexer.GetIndexParameters().Length > 0)
            {
                names.Add(indexer.Name);
            }
        }

        if (!type.IsAbstract && (type.IsValueType || type.GetConstructors().Length > 0))
        {
            names.Add(NetNames.ShortName(type));
        }

        names.UnionWith(["end", "isempty", "isscalar", "length", "ndims", "numel", "size"]);
        if (!type.IsValueType)
        {
            names.UnionWith(["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"]);
        }

        return names.Order(StringComparer.Ordinal);
    }

    /// <summary>What <c>events</c> lists for a .NET type: its public events, then a handle's ObjectBeingDestroyed.</summary>
    public static IEnumerable<string> EventNames(Type type)
    {
        IEnumerable<string> names = type.GetEvents(Public | BindingFlags.Instance | BindingFlags.Static).Select(static e => e.Name);
        return type.IsValueType ? names : names.Append("ObjectBeingDestroyed");
    }

    // --- calls ---------------------------------------------------------------------------------

    /// <summary>Constructs <paramref name="type"/> from MATLAB arguments.</summary>
    public static JgsValue Construct(Type type, IReadOnlyList<JgsValue> arguments, int line, int col, NetCatalog? session = null)
    {
        string name = NetNames.ClassName(type);
        if (type.IsAbstract)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:abstract",
                $"Abstract classes cannot be instantiated. Class '{name}' is abstract or static.");
        }

        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0 && !type.IsValueType)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                $"'{name}' has no public constructor.");
        }

        object? made;
        if (type.IsValueType && arguments.Count == 0 && !constructors.Any(static c => c.GetParameters().Length == 0))
        {
            made = Activator.CreateInstance(type);
        }
        else
        {
            MethodBase chosen = Choose(constructors, arguments)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:noMatchingConstructor",
                    $"No constructor '{name}' with matching signature found.");
            object?[] given = Arguments(chosen, arguments, line, col);
            try
            {
                made = ((ConstructorInfo)chosen).Invoke(given);
            }
            catch (Exception fault) when (IsNetFault(fault))
            {
                throw Raise(fault, "CreateObject", line, col);
            }
        }

        return NetConvert.ToMatlab(made, type, line, col, session);
    }

    /// <summary>Calls the method group <paramref name="name"/> on a type or an object.</summary>
    public static JgsValue Call(Type type, string name, NetObject? receiver, IReadOnlyList<JgsValue> arguments, int line, int col, NetCatalog? session = null)
    {
        MethodInfo[] group = Methods(type, name, receiver is null ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
        if (Choose(group, arguments) is not MethodInfo chosen)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:UndefinedMethod",
                $"No method '{NetNames.ClassName(type)}.{name}' with matching signature found.");
        }

        return InvokeChosen(chosen, receiver, arguments, "MethodInvoke", line, col, session);
    }

    /// <summary>Calls a method already chosen: the arguments converted, the exception wrapped, the answer brought back.</summary>
    public static JgsValue InvokeChosen(
        MethodInfo chosen, NetObject? receiver, IReadOnlyList<JgsValue> arguments, string context, int line, int col,
        NetCatalog? session = null)
    {
        object?[] given = Arguments(chosen, arguments, line, col);
        object? answer;
        try
        {
            answer = chosen.Invoke(chosen.IsStatic ? null : receiver!.Target, given);
        }
        catch (Exception fault) when (IsNetFault(fault))
        {
            throw Raise(fault, context, line, col);
        }

        return chosen.ReturnType == typeof(void) ? JgsValue.Null : NetConvert.ToMatlab(answer, chosen.ReturnType, line, col, session);
    }

    /// <summary>The candidate every argument reaches with the lowest total rank; the first declared on a tie.</summary>
    private static MethodBase? Choose(IEnumerable<MethodBase> candidates, IReadOnlyList<JgsValue> arguments)
    {
        MethodBase? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (MethodBase candidate in candidates)
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length != arguments.Count)
            {
                continue;
            }

            double score = 0;
            for (int i = 0; i < parameters.Length && !double.IsNaN(score); i++)
            {
                score = NetConvert.Rank(arguments[i], parameters[i].ParameterType) is { } rank ? score + rank : double.NaN;
            }

            if (!double.IsNaN(score) && score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static object?[] Arguments(MethodBase chosen, IReadOnlyList<JgsValue> arguments, int line, int col)
    {
        ParameterInfo[] parameters = chosen.GetParameters();
        var given = new object?[parameters.Length];
        for (int i = 0; i < given.Length; i++)
        {
            given[i] = Argument(arguments[i], parameters[i].ParameterType, i + 1, parameters[i].Name ?? $"arg{i + 1}", line, col);
        }

        return given;
    }

    private static object? Argument(JgsValue value, Type type, int position, string name, int line, int col)
    {
        if (NetConvert.Rank(value, type) is null)
        {
            throw new JgsRuntimeException(line, col,
                $"Invalid input for argument {position} ({name}): a value of class '{JgsBuiltins.ClassOf(value, JgsDialect.Matlab)}' cannot be converted to '{NetNames.ClassName(type)}'.");
        }

        try
        {
            return NetConvert.ToNet(value, type, position, name, line, col);
        }
        catch (OverflowException overflow)
        {
            throw new JgsRuntimeException(line, col, $"Invalid input for argument {position} ({name}):\n{overflow.Message}");
        }
    }

    // --- exceptions ----------------------------------------------------------------------------

    /// <summary>Whether an exception came out of .NET code rather than out of the interpreter.</summary>
    private static bool IsNetFault(Exception fault) => fault is not JgsException;

    /// <summary>
    /// The <c>NET.NetException</c> for a .NET exception thrown in <paramref name="context"/> (empty
    /// for none): R2025b's identifier and message, with the exception as <c>ExceptionObject</c>.
    /// </summary>
    public static JgsRuntimeException Raise(Exception fault, string context, int line, int col)
    {
        Exception thrown = fault is TargetInvocationException { InnerException: { } inner } ? inner : fault;
        string identifier = context.Length == 0 ? "MATLAB:NET:CLRException" : "MATLAB:NET:CLRException:" + context;
        string message = $"Message: {thrown.Message}\nSource: {thrown.Source ?? "None"}\nHelpLink: {thrown.HelpLink ?? "None"}";
        JgsValue exception = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ExceptionObject"] = JgsValue.External(new NetObject(thrown, thrown.GetType())),
            ["identifier"] = JgsValue.Str(identifier),
            ["message"] = JgsValue.Str(message),
            ["cause"] = JgsBuiltins.NoCausesValue(),
            ["stack"] = JgsBuiltins.StackValue([]),
        });
        exception.SetClassName(NetExceptionClass);
        return new JgsRuntimeException(line, col, identifier, message) { Carried = exception };
    }
}

/// <summary>
/// A .NET method group or constructor a script holds as a function: <c>System.Math.Max</c> named in
/// a call or a handle, <c>obj.Describe</c> read without calling it, a type's constructor.
/// </summary>
internal sealed class NetCallable : IJgsCallable
{
    private readonly Type _type;
    private readonly NetObject? _receiver;
    private readonly bool _constructor;
    private readonly NetCatalog? _session;

    /// <summary>A method group of <paramref name="type"/>: static when <paramref name="receiver"/> is null.</summary>
    public NetCallable(Type type, string method, NetObject? receiver, NetCatalog? session = null)
    {
        _type = type;
        _receiver = receiver;
        _session = session;
        Method = method;
        Name = receiver is null ? NetNames.ClassName(type) + "." + method : method;
    }

    private NetCallable(Type type, NetCatalog? session)
    {
        _type = type;
        _session = session;
        _constructor = true;
        Method = NetNames.ShortName(type);
        Name = NetNames.ClassName(type);
    }

    /// <summary>A type's constructor.</summary>
    public static NetCallable Constructor(Type type, NetCatalog? session = null) => new(type, session);

    /// <summary>The member name (the type's short name for a constructor).</summary>
    public string Method { get; }

    public string Name { get; }

    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        _constructor
            ? NetInvoke.Construct(_type, arguments, line, column, _session)
            : NetInvoke.Call(_type, Method, _receiver, arguments, line, column, _session);
}
