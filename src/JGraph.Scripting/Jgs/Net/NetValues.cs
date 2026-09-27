using System.Reflection;
using System.Runtime.CompilerServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The payload of a <see cref="JgsType.External"/> value (interop plan, stage 1, ADR 0174): something
/// that lives outside the language and answers the few questions every value must.
/// </summary>
/// <remarks>
/// A new value kind rather than <see cref="JgsType.Object"/>, because code that reads
/// <see cref="JgsType.Object"/> goes on to read <see cref="JgsValue.AsObject"/>'s
/// <see cref="JgsClass"/>, which a .NET type does not have. A kind nothing knows falls into the
/// refusal each site already has for a value it does not handle, which is the safe direction.
/// </remarks>
internal interface IJgsExternal
{
    /// <summary>What <c>class</c> answers: <c>System.String</c>, <c>NET.Assembly</c>.</summary>
    string ClassName { get; }

    /// <summary>Whether a second name for the value is the same value (a .NET reference type).</summary>
    bool IsHandle { get; }

    /// <summary><c>isa</c>: the class itself, its bases and interfaces, and <c>handle</c> for a handle.</summary>
    bool IsA(string className);

    /// <summary>The value a new binding holds: the value itself for a handle, a copy for a value type.</summary>
    IJgsExternal CopyForBinding();

    /// <summary>How <c>disp</c> and the echo show the value, in JGraph's display layout.</summary>
    string Display();
}

/// <summary>
/// A .NET object held by a script: any instance, a boxed value type (copied on binding, as MATLAB
/// copies one), an array, an enum member, a <c>System.String</c>. A <c>Nullable&lt;T&gt;</c> a member
/// declared keeps its declared type here, because the runtime boxes it as a bare <c>T</c> or null
/// and MATLAB reports <c>System.Nullable&lt;System*Int32&gt;</c>.
/// </summary>
internal sealed class NetObject : IJgsExternal
{
    public NetObject(object? target, Type type, bool isView = false)
    {
        Target = target;
        Type = type;
        IsView = isView;
    }

    /// <summary>The object; for a Nullable, its value or null.</summary>
    public object? Target { get; }

    /// <summary>
    /// The type MATLAB reports: the runtime type, the Nullable a member declared, or the interface an
    /// interface view (<see cref="IsView"/>) sees the object through.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Whether this is <c>NET.explicitCast(obj, 'Interface')</c>'s view of an object (stage 2, ADR 0175):
    /// its members are the interface's, an explicit implementation's included, and its class is
    /// <c>NET.view.&lt;interface&gt;</c> (measured, net_members).
    /// </summary>
    public bool IsView { get; }

    /// <summary>The value type a Nullable wraps, or null when this is not a Nullable.</summary>
    public Type? NullableOf => Nullable.GetUnderlyingType(Type);

    /// <summary>
    /// <c>delete(obj)</c> ran on this handle (ADR 0178): every name for it — the one wrapper — is
    /// invalid, and its members, conversions and passing it to .NET refuse, while the .NET object
    /// itself is untouched and a new wrapper of it, handed back by .NET, is valid (probe5h).
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>
    /// This object, or R2025b's refusal when it was deleted, or JGraph's when <c>jgraph.net.compile</c>
    /// has since replaced the build that defined its type (ADR 0179).
    /// </summary>
    public NetObject Live(int line, int col) => Deleted
        ? throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.")
        : Net.NetCompiler.IsRetired(Type, out string assembly)
            ? throw new JgsRuntimeException(line, col, "JGraph:NET:AssemblyRecompiled",
                $"This {ClassName} object belongs to an earlier build of assembly '{assembly}', which was recompiled.")
            : this;

    public string ClassName => IsView ? "NET.view." + Net.NetNames.ClassName(Type) : Net.NetNames.ClassName(Type);

    public bool IsHandle => !Type.IsValueType;

    public bool IsA(string className)
    {
        if (className == "handle")
        {
            return IsHandle;
        }

        for (Type? t = Type; t is not null; t = t.BaseType)
        {
            if (Net.NetNames.ClassName(t) == className)
            {
                return true;
            }
        }

        return Type.GetInterfaces().Any(i => Net.NetNames.ClassName(i) == className);
    }

    public IJgsExternal CopyForBinding() =>
        IsHandle || Target is null ? this : new NetObject(RuntimeHelpers.GetObjectValue(Target), Type, IsView);

    public string Display() => Net.NetDisplay.Of(this);
}

/// <summary>
/// What <c>?System.String</c> and <c>meta.class.fromName</c> answer for a .NET type: R2025b's
/// <c>NET.NETInterfaceCustomMetaClass</c>, of which stage 1 carries the name.
/// </summary>
internal sealed class NetMetaClass(Type type) : IJgsExternal
{
    public Type Type { get; } = type;

    public string ClassName => "NET.NETInterfaceCustomMetaClass";

    public bool IsHandle => true;

    public bool IsA(string className) =>
        className is "NET.NETInterfaceCustomMetaClass" or "meta.class" or "handle";

    public IJgsExternal CopyForBinding() => this;

    public string Display() => $"{ClassName} with properties:\n    Name: {Net.NetNames.ClassName(Type)}";
}

/// <summary>
/// What <c>NET.GenericClass('System.Collections.Generic.List', 'System.Double')</c> answers: a closed
/// generic type held to be a type argument of <c>NET.createGeneric</c> or an element type of
/// <c>NET.createArray</c>, where a name in text cannot say it (stage 4, ADR 0177). R2025b shows it as
/// "GenericClass with no properties." (probe4).
/// </summary>
internal sealed class NetGenericClass(Type type) : IJgsExternal
{
    public Type Type { get; } = type;

    public string ClassName => "NET.GenericClass";

    public bool IsHandle => true;

    public bool IsA(string className) => className is "NET.GenericClass" or "handle";

    public IJgsExternal CopyForBinding() => this;

    public string Display() => "GenericClass with no properties.";
}

/// <summary>
/// What <c>NET.addAssembly</c> answers: a handle whose properties list the assembly's public types
/// by kind (measured in R2025b, net_assembly).
/// </summary>
internal sealed class NetAssemblyValue(Assembly assembly) : IJgsExternal
{
    /// <summary>The property names, in R2025b's order.</summary>
    public static readonly string[] PropertyNames =
        ["AssemblyHandle", "Classes", "Structures", "Enums", "GenericTypes", "Interfaces", "Delegates"];

    public Assembly Assembly { get; } = assembly;

    public string ClassName => "NET.Assembly";

    public bool IsHandle => true;

    public bool IsA(string className) => className is "NET.Assembly" or "handle";

    public IJgsExternal CopyForBinding() => this;

    public string Display() => "NET.Assembly handle with properties:\n    " + string.Join("\n    ", PropertyNames);

    /// <summary>One property's value: the assembly as a .NET object, or a column of type names.</summary>
    public JgsValue Property(string name)
    {
        // The top-level types in the assembly's order, then the nested ones: R2025b lists
        // JGTest.Outer+Inner last among the classes (probe3, ADR 0176).
        Type[] exported = Assembly.GetExportedTypes();
        Type[] types = [.. exported.Where(static t => !t.IsNested), .. exported.Where(static t => t.IsNested)];
        IEnumerable<Type> picked = name switch
        {
            "AssemblyHandle" => [],
            "Classes" => types.Where(static t => t.IsClass && !t.IsGenericTypeDefinition && !typeof(Delegate).IsAssignableFrom(t)),
            "Structures" => types.Where(static t => t.IsValueType && !t.IsEnum && !t.IsGenericTypeDefinition),
            "Enums" => types.Where(static t => t.IsEnum),
            "GenericTypes" => types.Where(static t => t.IsGenericTypeDefinition),
            "Interfaces" => types.Where(static t => t.IsInterface && !t.IsGenericTypeDefinition),
            "Delegates" => types.Where(static t => typeof(Delegate).IsAssignableFrom(t) && !t.IsGenericTypeDefinition),
            _ => throw new KeyNotFoundException(name),
        };

        if (name == "AssemblyHandle")
        {
            return JgsValue.External(new NetObject(Assembly, Assembly.GetType()));
        }

        JgsValue[] cells = picked.Select(static t => JgsValue.Str(t.IsGenericTypeDefinition
            ? t.FullName + "[" + string.Join(",", t.GetGenericArguments().Select(static a => a.Name)) + "]"
            : t.FullName!)).ToArray();
        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, cells.Length == 0 ? 0 : 1);
        return column;
    }
}
