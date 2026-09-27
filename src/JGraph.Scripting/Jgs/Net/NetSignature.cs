using System.Collections.Concurrent;
using System.Reflection;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// A .NET method or constructor as a MATLAB call sees it (interop plan, stage 2, ADR 0175): which of
/// its parameters are inputs, how many of them may be left off, and which outputs it hands back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inputs and outputs</b> (measured, net_members): every parameter but an <c>out</c> one is an
/// input, in declaration order; the outputs are the return value when there is one (<c>RetVal</c>),
/// then each <c>ref</c> and <c>out</c> parameter's value after the call, in declaration order.
/// <c>params T[]</c> is an ordinary array parameter: MATLAB passes an array there and refuses loose
/// arguments. Trailing optional parameters may be left off, and <c>System.Reflection.Missing.Value</c>
/// in an optional place stands for its default.
/// </para>
/// <para>
/// <b>Cached per type and name.</b> <see cref="Type.GetMethods()"/> copies its array on every call,
/// and a call's name walk asked for it once per mention (stage 1 finding 3). The groups are
/// immutable once built, so one process-wide cache serves every session.
/// </para>
/// </remarks>
internal sealed class NetSignature
{
    private const BindingFlags Public = BindingFlags.Public | BindingFlags.FlattenHierarchy;

    private static readonly ConcurrentDictionary<(Type Type, string Name, bool Instance), NetSignature[]> Groups = new();
    private static readonly ConcurrentDictionary<Type, NetSignature[]> Ctors = new();
    private static readonly ConcurrentDictionary<(Type Type, bool Instance), NetSignature[]> Everything = new();

    private NetSignature(MethodBase method, string name)
    {
        Method = method;
        Name = name;
        Parameters = method.GetParameters();
        var inputs = new List<int>();
        var byRef = new List<int>();
        int required = 0;
        for (int i = 0; i < Parameters.Length; i++)
        {
            ParameterInfo parameter = Parameters[i];
            bool isOut = parameter.ParameterType.IsByRef && parameter.IsOut && !parameter.IsIn;
            if (parameter.ParameterType.IsByRef)
            {
                byRef.Add(i);
            }

            if (!isOut)
            {
                inputs.Add(i);
                if (!parameter.IsOptional)
                {
                    required = inputs.Count;
                }
            }
        }

        Inputs = [.. inputs];
        ByRef = [.. byRef];
        Required = required;
        Returns = method is ConstructorInfo || ((MethodInfo)method).ReturnType != typeof(void);
    }

    /// <summary>The method or constructor.</summary>
    public MethodBase Method { get; }

    /// <summary>The name MATLAB calls it by: the method's, an indexer's property name, a constructor's type's short name.</summary>
    public string Name { get; }

    public ParameterInfo[] Parameters { get; }

    /// <summary>The positions in <see cref="Parameters"/> a call's arguments fill, in order.</summary>
    public int[] Inputs { get; }

    /// <summary>How many leading inputs a call must give; the rest are optional.</summary>
    public int Required { get; }

    /// <summary>The positions of the <c>ref</c> and <c>out</c> parameters, whose values come back after the return value.</summary>
    public int[] ByRef { get; }

    /// <summary>Whether the first output is a return value (true for a constructor).</summary>
    public bool Returns { get; }

    /// <summary>How many outputs a call can ask for.</summary>
    public int OutputCount => (Returns ? 1 : 0) + ByRef.Length;

    public bool IsStatic => Method.IsStatic;

    /// <summary>The type a caller's argument must reach for input <paramref name="index"/>: a <c>ref</c> parameter's element type.</summary>
    public Type InputType(int index)
    {
        Type type = Parameters[Inputs[index]].ParameterType;
        return type.IsByRef ? type.GetElementType()! : type;
    }

    /// <summary>Whether a call with <paramref name="given"/> arguments asking for <paramref name="wanted"/> outputs fits.</summary>
    public bool Fits(int given, int wanted) =>
        given >= Required && given <= Inputs.Length && wanted <= OutputCount;

    /// <summary>
    /// The methods named <paramref name="name"/> a call can reach on <paramref name="type"/>: the static
    /// ones, and the instance ones too when <paramref name="instance"/> — ordinary methods, the
    /// operators (<c>JGTest.Vector2.op_Addition</c>), and a default indexer's accessors under the
    /// indexer's name (<c>list.Item(0)</c>). Open generic methods are left out
    /// (<c>NET.invokeGenericMethod</c> calls them).
    /// </summary>
    public static NetSignature[] Group(Type type, string name, bool instance) =>
        Groups.GetOrAdd((type, name, instance), static key =>
            [.. All(key.Type, key.Instance).Where(s => s.Name == key.Name)]);

    /// <summary>Every callable method of <paramref name="type"/>, static ones and (when <paramref name="instance"/>) instance ones.</summary>
    public static NetSignature[] All(Type type, bool instance) =>
        Everything.GetOrAdd((type, instance), static key => Build(key.Type, key.Instance));

    /// <summary>The public constructors of <paramref name="type"/>.</summary>
    public static NetSignature[] Constructors(Type type) =>
        Ctors.GetOrAdd(type, static t =>
            [.. t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Select(c => new NetSignature(c, NetNames.ShortName(t)))]);

    private static NetSignature[] Build(Type type, bool instance)
    {
        BindingFlags scope = Public | BindingFlags.Static | (instance ? BindingFlags.Instance : 0);
        var found = new List<NetSignature>();
        foreach (MethodInfo method in AllMethods(type, scope))
        {
            if (method.ContainsGenericParameters || (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)))
            {
                continue;
            }

            found.Add(new NetSignature(method, method.Name));
        }

        if (instance)
        {
            foreach (PropertyInfo indexer in AllProperties(type))
            {
                if (indexer.GetIndexParameters().Length == 0)
                {
                    continue;
                }

                if (indexer.GetGetMethod() is { } get)
                {
                    found.Add(new NetSignature(get, indexer.Name));
                }

                if (indexer.GetSetMethod() is { } set)
                {
                    found.Add(new NetSignature(set, indexer.Name));
                }
            }
        }

        return [.. found];
    }

    /// <summary>
    /// The public methods of a type, an interface's inherited interfaces' included (an interface
    /// view names them all: <c>NET.explicitCast(x, 'System.Collections.IList')</c> reaches
    /// <c>ICollection.Count</c>'s accessors too).
    /// </summary>
    private static IEnumerable<MethodInfo> AllMethods(Type type, BindingFlags scope) =>
        type.IsInterface
            ? new[] { type }.Concat(type.GetInterfaces()).SelectMany(i => i.GetMethods(BindingFlags.Public | BindingFlags.Instance)).Concat(typeof(object).GetMethods(scope))
            : type.GetMethods(scope);

    private static IEnumerable<PropertyInfo> AllProperties(Type type) =>
        type.IsInterface
            ? new[] { type }.Concat(type.GetInterfaces()).SelectMany(i => i.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            : type.GetProperties(Public | BindingFlags.Instance);

    // --- methods -full ---------------------------------------------------------------------------

    /// <summary>
    /// This signature as <c>methods(x, '-full')</c> prints it (measured, net_display and
    /// probe_methods): <c>Static</c> for a static member, the outputs — none, one as
    /// <c>type name</c>, several in brackets — the name, and the inputs in parentheses when there are
    /// any, an instance method's receiver first as <c>Type this</c> and an optional one as
    /// <c>optional&lt;type&gt; name</c>.
    /// </summary>
    public string FullText(Type owner)
    {
        var outputs = new List<string>();
        if (Returns)
        {
            Type returned = Method is MethodInfo m ? m.ReturnType : owner;
            outputs.Add(Describe(returned) + " RetVal");
        }

        foreach (int i in ByRef)
        {
            outputs.Add(Describe(Parameters[i].ParameterType) + " " + Parameters[i].Name);
        }

        var inputs = new List<string>();
        if (!Method.IsStatic && Method is MethodInfo)
        {
            inputs.Add(NetNames.ClassName(owner) + " this");
        }

        foreach (int i in Inputs)
        {
            ParameterInfo parameter = Parameters[i];
            string described = Describe(parameter.ParameterType);
            inputs.Add((parameter.IsOptional ? "optional<" + described + ">" : described) + " " + parameter.Name);
        }

        string lead = outputs.Count switch
        {
            0 => "",
            1 => outputs[0] + " ",
            _ => "[" + string.Join(", ", outputs) + "] ",
        };
        string tail = inputs.Count == 0 ? "" : "(" + string.Join(", ", inputs) + ")";
        return (Method.IsStatic ? "Static " : "") + lead + Name + tail;
    }

    /// <summary>How a signature names a type: a primitive as its MATLAB class and <c>scalar</c>, anything else by its .NET name.</summary>
    public static string Describe(Type type)
    {
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
        }

        string? primitive = type.IsEnum ? null : Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "logical",
            TypeCode.Byte => "uint8",
            TypeCode.SByte => "int8",
            TypeCode.Int16 => "int16",
            TypeCode.UInt16 => "uint16",
            TypeCode.Int32 => "int32",
            TypeCode.UInt32 => "uint32",
            TypeCode.Int64 => "int64",
            TypeCode.UInt64 => "uint64",
            TypeCode.Single => "single",
            TypeCode.Double => "double",
            TypeCode.Char => "char",
            _ => null,
        };
        return primitive is null ? NetNames.ClassName(type) : primitive + " scalar";
    }
}
