using System.Reflection;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// Reading, writing and calling .NET members for a script (interop plan, stages 1 and 2, ADRs 0174
/// and 0175): which overload a call picks, how the arguments cross, what comes back, and how a .NET
/// exception becomes a <c>NET.NetException</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Overloads.</b> The candidates are the public members of the name (<see cref="NetSignature"/>)
/// that take as many inputs as were given — trailing optional ones may be left off — and hand back
/// at least as many outputs as were asked for (a <c>void</c> method asked for one does not fit,
/// net_members). Each argument ranks each candidate's parameter by <see cref="NetConvert.Rank"/>; a
/// candidate some argument cannot reach drops out, and the lowest total wins, the first declared on
/// a tie. That reproduces every pair R2025b recorded (net_conversions). No candidate is R2025b's
/// <c>MATLAB:UndefinedFunction</c> for a call through the type and
/// <c>MATLAB:class:UndefinedMethod</c> for a call through an object, each in its own words.
/// </para>
/// <para>
/// <b>Outputs.</b> The return value, then each <c>ref</c> and <c>out</c> parameter's value after the
/// call, in declaration order (<see cref="NetSignature"/>).
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
    public static JgsValue Member(
        NetObject target, string name, bool autoCall, int line, int col, NetCatalog? session = null, int bareWanted = 1)
    {
        session?.SyncFolder();
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

        if (HasMethod(type, name, instance: true))
        {
            var bound = new NetCallable(type, name, target, session);
            return autoCall ? bound.CallBare(bareWanted, line, col) : JgsValue.Function(bound);
        }

        // A property with a setter and no getter (measured, net_members get_writeonly).
        if (type.GetProperty(name, Public | BindingFlags.Instance | BindingFlags.Static) is { } writeOnly
            && writeOnly.GetGetMethod() is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:GetProhibited",
                $"No public property '{name}' for class ''{NetNames.ShortName(type)}''.");
        }

        throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class '{target.ClassName}'.");
    }

    /// <summary>
    /// Reads <c>Type.name</c>: a static property or field, or a static method, called now when the
    /// mention is bare (asking for <paramref name="bareWanted"/> outputs: none for a statement).
    /// </summary>
    public static JgsValue StaticMember(
        Type type, string name, bool autoCall, int line, int col, NetCatalog? session = null, int bareWanted = 1)
    {
        session?.SyncFolder();
        if (ReadableProperty(type, name, BindingFlags.Static) is { } property)
        {
            return Get(property, null, line, col, session);
        }

        if (type.GetField(name, Public | BindingFlags.Static) is { } field)
        {
            return Read(field, null, line, col, session);
        }

        if (HasMethod(type, name, instance: false))
        {
            var group = new NetCallable(type, name, receiver: null, session);
            return autoCall ? group.CallBare(bareWanted, line, col) : JgsValue.Function(group);
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
            object? converted = PropertyValue(value, property.PropertyType, name, type, line, col);
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

            field.SetValue(field.IsStatic ? null : target.Target, PropertyValue(value, field.FieldType, name, type, line, col));
            return;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
            $"Unrecognized property '{name}' for class '{target.ClassName}'.");
    }

    /// <summary>
    /// <c>NET.setStaticProperty('Type.Name', value)</c>: the one way a script writes a static property or
    /// field — <c>Type.Name = v</c> is an ordinary assignment that makes a struct (measured, net_members).
    /// </summary>
    public static void SetStatic(Type type, string name, JgsValue value, int line, int col)
    {
        if (type.GetProperty(name, Public | BindingFlags.Static) is { } property && property.GetIndexParameters().Length == 0)
        {
            MethodInfo setter = property.GetSetMethod() ?? throw StaticNotAccessible(name, type, line, col);
            object? converted = PropertyValue(value, property.PropertyType, name, owner: null, line, col);
            try
            {
                setter.Invoke(null, [converted]);
            }
            catch (Exception fault) when (IsNetFault(fault))
            {
                throw Raise(fault, "PropertySet", line, col);
            }

            return;
        }

        if (type.GetField(name, Public | BindingFlags.Static) is { } field)
        {
            if (field.IsLiteral || field.IsInitOnly)
            {
                throw StaticNotAccessible(name, type, line, col);
            }

            field.SetValue(null, PropertyValue(value, field.FieldType, name, owner: null, line, col));
            return;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:NET:InvalidStaticPropName",
            $"Could not find static property or field '{name}' for class '{NetNames.ClassName(type)}'.");
    }

    private static JgsRuntimeException StaticNotAccessible(string name, Type type, int line, int col) =>
        new(line, col, "MATLAB:NET:InvalidStaticPropertyAccess",
            $"Static property '{name}' is not accessible for class '{NetNames.ClassName(type)}'.");

    /// <summary>R2025b's refusal of a write to a read-only property or field (net_members), quotes and all.</summary>
    private static JgsRuntimeException ReadOnly(string name, Type type, int line, int col) =>
        new(line, col, "MATLAB:class:SetProhibited",
            $"Unable to set the '{name}' property of class ''{NetNames.ShortName(type)}'' because it is read-only.");

    /// <summary>
    /// A value converted for a property or field of <paramref name="type"/>. A value the type does not
    /// take is refused as R2025b refuses a property write (net_members, probe2): a numeric property
    /// wants a scalar (<c>MATLAB:class:RequireScalar</c>) that is numeric or logical
    /// (<c>MATLAB:class:RequireNumeric</c>), and a string property text. An instance write's message
    /// names the property and its class; <c>NET.setStaticProperty</c>'s (no <paramref name="owner"/>)
    /// is the reason alone.
    /// </summary>
    private static object? PropertyValue(JgsValue value, Type type, string name, Type? owner, int line, int col)
    {
        string lead = owner is null ? "" : $"Error setting property '{name}' of class ''{NetNames.ShortName(owner)}'':\n";
        if (NetConvert.Rank(value, type) is null && type == typeof(string))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:NetConversion:StringConversion",
                lead + "Error converting an input type to the System.String type.");
        }

        if (NetConvert.Rank(value, type) is null && IsNumericTarget(type))
        {
            bool scalar = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.External
                || (value.Type == JgsType.String && value.AsString.Length == 1)
                || (value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 1);
            if (!scalar)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:RequireScalar", lead + "Value must be a scalar.");
            }

            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireNumeric", lead + "Value must be numeric or logical.");
        }

        return Argument(value, type, 1, name, line, col);
    }

    private static bool IsNumericTarget(Type type) =>
        !type.IsEnum && Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) is TypeCode.Boolean or TypeCode.Byte
            or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64
            or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

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

    /// <summary>Whether <paramref name="type"/> has a method a call can reach by <paramref name="name"/>.</summary>
    public static bool HasMethod(Type type, string name, bool instance) =>
        NetSignature.Group(type, name, instance).Length > 0;

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
    /// What <c>methods</c> answers for a .NET type, sorted as R2025b sorts it: the public methods,
    /// instance and static, operators included, an indexer under its name, the constructor under the
    /// type's short name, the operators' MATLAB names (<c>plus</c> for <c>op_Addition</c>), and the
    /// methods MATLAB gives every .NET object — <c>matlab.mixin.Scalar</c>'s, and <c>handle</c>'s for
    /// a reference type (measured, net_display).
    /// </summary>
    public static IEnumerable<string> MethodNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (NetSignature signature in NetSignature.All(type, instance: true))
        {
            names.Add(signature.Name);
        }

        if (HasConstructor(type))
        {
            names.Add(NetNames.ShortName(type));
        }

        names.UnionWith(NetMethodsListing.OperatorNames(type));
        names.UnionWith(NetMethodsListing.ScalarMethods);
        if (!type.IsValueType)
        {
            names.UnionWith(NetMethodsListing.HandleMethods);
        }

        return names.Order(StringComparer.Ordinal);
    }

    /// <summary>Whether <c>methods</c> lists a constructor for <paramref name="type"/>.</summary>
    internal static bool HasConstructor(Type type) =>
        !type.IsAbstract && (type.IsValueType || NetSignature.Constructors(type).Length > 0);

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
        session?.SyncFolder();
        string name = NetNames.ClassName(type);
        if (type.IsAbstract)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:abstract",
                $"Abstract classes cannot be instantiated. Class '{name}' is declared as Abstract.");
        }

        NetSignature[] constructors = NetSignature.Constructors(type);
        if (constructors.Length == 0 && !type.IsValueType)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        object? made;
        if (type.IsValueType && arguments.Count == 0 && !constructors.Any(static c => c.Inputs.Length == 0))
        {
            made = Activator.CreateInstance(type);
        }
        else
        {
            NetSignature chosen = Choose(constructors, arguments, wanted: 0)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:noMatchingConstructor",
                    $"No constructor '{name}' with matching signature found.");
            object?[] given = Arguments(chosen, arguments, line, col);
            try
            {
                made = ((ConstructorInfo)chosen.Method).Invoke(given);
            }
            catch (Exception fault) when (IsNetFault(fault))
            {
                throw Raise(fault, "CreateObject", line, col);
            }
        }

        return NetConvert.ToMatlab(made, type, line, col, session);
    }

    /// <summary>
    /// Calls the method group <paramref name="name"/> on a type or an object asking for
    /// <paramref name="wanted"/> outputs, and answers the outputs the chosen overload handed back (at
    /// most <paramref name="wanted"/>, and its first even when none were asked for).
    /// </summary>
    public static JgsValue[] Call(
        Type type, string name, NetObject? receiver, IReadOnlyList<JgsValue> arguments, int wanted, int line, int col,
        NetCatalog? session = null)
    {
        session?.SyncFolder();
        NetSignature chosen = Choose(NetSignature.Group(type, name, receiver is not null), arguments, wanted)
            ?? throw NoMatch(type, name, receiver is not null, line, col);
        return Invoke(chosen, receiver, arguments, wanted, "MethodInvoke", line, col, session);
    }

    /// <summary>
    /// <c>NET.invokeGenericMethod</c>: the generic methods <paramref name="name"/> of a type (static) or an
    /// object (either), closed over <paramref name="typeArguments"/>, the overload chosen as any call's is.
    /// R2025b's refusals (probe4): no generic method of that name is <c>NET:NoGenericMethod</c>; one
    /// whose arity, constraints or parameters do not fit is <c>NET:NoMatchingGenericMethod</c>.
    /// </summary>
    public static JgsValue[] CallGeneric(
        Type type, string name, NetObject? receiver, Type[] typeArguments, IReadOnlyList<JgsValue> arguments, int wanted,
        int line, int col, NetCatalog? session = null)
    {
        session?.SyncFolder();
        MethodInfo[] definitions = NetSignature.GenericDefinitions(type, name, receiver is not null);
        if (definitions.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:NET:NoGenericMethod", $"Could not find generic method '{name}'.");
        }

        var closed = new List<NetSignature>();
        foreach (MethodInfo definition in definitions)
        {
            if (definition.GetGenericArguments().Length != typeArguments.Length)
            {
                continue;
            }

            try
            {
                closed.Add(NetSignature.Of(definition.MakeGenericMethod(typeArguments)));
            }
            catch (ArgumentException)
            {
                // the type arguments break this definition's constraints
            }
        }

        NetSignature chosen = Choose([.. closed], arguments, wanted)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:NET:NoMatchingGenericMethod",
                $"Could not find generic method '{name}' with matching signature.");
        return Invoke(chosen, receiver, arguments, wanted, "MethodInvoke", line, col, session);
    }

    /// <summary>R2025b's refusal when no overload fits (measured, net_members and net_conversions).</summary>
    private static JgsRuntimeException NoMatch(Type type, string name, bool throughObject, int line, int col) =>
        throughObject
            ? new(line, col, "MATLAB:class:UndefinedMethod",
                $"No method '{name}' with matching signature found for class '{NetNames.ClassName(type)}'.")
            : new(line, col, "MATLAB:UndefinedFunction",
                $"No method '{NetNames.ClassName(type)}.{name}' with matching signature found.");

    /// <summary>Calls a signature already chosen: the arguments converted, the exception wrapped, the outputs brought back.</summary>
    private static JgsValue[] Invoke(
        NetSignature chosen, NetObject? receiver, IReadOnlyList<JgsValue> arguments, int wanted, string context, int line, int col,
        NetCatalog? session)
    {
        object?[] given = Arguments(chosen, arguments, line, col);
        object? answer;
        try
        {
            answer = chosen.Method.Invoke(chosen.IsStatic ? null : receiver!.Target, given);
        }
        catch (Exception fault) when (IsNetFault(fault))
        {
            throw Raise(fault, context, line, col);
        }

        int count = Math.Min(Math.Max(wanted, 1), chosen.OutputCount);
        var outputs = new JgsValue[count];
        int next = 0;
        if (chosen.Returns && next < count)
        {
            outputs[next++] = NetConvert.ToMatlab(answer, ((MethodInfo)chosen.Method).ReturnType, line, col, session);
        }

        foreach (int position in chosen.ByRef)
        {
            if (next == count)
            {
                break;
            }

            outputs[next++] = NetConvert.ToMatlab(given[position], chosen.Parameters[position].ParameterType.GetElementType()!, line, col, session);
        }

        return outputs;
    }

    /// <summary>Calls a two-operand operator method already chosen (<see cref="NetOperators"/>).</summary>
    public static JgsValue InvokeChosen(
        MethodInfo chosen, NetObject? receiver, IReadOnlyList<JgsValue> arguments, string context, int line, int col,
        NetCatalog? session = null)
    {
        ParameterInfo[] parameters = chosen.GetParameters();
        var given = new object?[parameters.Length];
        for (int i = 0; i < given.Length; i++)
        {
            given[i] = Argument(arguments[i], parameters[i].ParameterType, i + 1, parameters[i].Name ?? $"arg{i + 1}", line, col);
        }

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
    private static NetSignature? Choose(NetSignature[] candidates, IReadOnlyList<JgsValue> arguments, int wanted)
    {
        NetSignature? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (NetSignature candidate in candidates)
        {
            if (!candidate.Fits(arguments.Count, wanted))
            {
                continue;
            }

            double score = 0;
            for (int i = 0; i < arguments.Count && !double.IsNaN(score); i++)
            {
                double? rank = IsMissing(arguments[i]) && candidate.Parameters[candidate.Inputs[i]].IsOptional
                    ? 0
                    : NetConvert.Rank(arguments[i], candidate.InputType(i));
                score = rank is { } r ? score + r : double.NaN;
            }

            if (!double.IsNaN(score) && score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary><c>System.Reflection.Missing.Value</c>, which in an optional place stands for the parameter's default.</summary>
    private static bool IsMissing(JgsValue value) =>
        value.Type == JgsType.External && value.AsExternal is NetObject { Target: Missing };

    private static object?[] Arguments(NetSignature chosen, IReadOnlyList<JgsValue> arguments, int line, int col)
    {
        ParameterInfo[] parameters = chosen.Parameters;
        var given = new object?[parameters.Length];
        for (int i = 0; i < chosen.Inputs.Length; i++)
        {
            ParameterInfo parameter = parameters[chosen.Inputs[i]];
            given[chosen.Inputs[i]] = i >= arguments.Count || (IsMissing(arguments[i]) && parameter.IsOptional)
                ? (parameter.HasDefaultValue ? parameter.DefaultValue : Type.Missing)
                : Argument(arguments[i], chosen.InputType(i), i + 1, parameter.Name ?? $"arg{i + 1}", line, col);
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
/// a call or a handle, <c>obj.Describe</c> read without calling it, a type's constructor. It answers
/// several outputs — a <c>ref</c> or <c>out</c> parameter's value after the return value (stage 2).
/// </summary>
internal sealed class NetCallable : IJgsCallable, IJgsMultiCallable
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
            : NetInvoke.Call(_type, Method, _receiver, arguments, 1, line, column, _session)[0];

    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        _constructor
            ? [NetInvoke.Construct(_type, arguments, line, column, _session)]
            : NetInvoke.Call(_type, Method, _receiver, arguments, wanted, line, column, _session);

    /// <summary>
    /// A bare mention's call — <c>JGTest.Members.Increment</c>, <c>m.Bump;</c> — asking for
    /// <paramref name="wanted"/> outputs: none for a statement made of the mention alone, so a
    /// <c>void</c> method runs, and one anywhere else (measured, probe2). Answers the first output,
    /// or nothing when there is none.
    /// </summary>
    public JgsValue CallBare(int wanted, int line, int column)
    {
        JgsValue[] outputs = CallMultiple([], wanted, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }
}
