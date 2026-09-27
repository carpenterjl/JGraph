using System.Text;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// One entry of a prototype file's <c>fcns</c> struct (ADR 0181): a function, or with the call type
/// <c>data</c> an exported variable. The types are MATLAB's shared-library type names, the ones a
/// prototype file R2025b writes holds: <c>int32</c>, <c>doublePtr</c>, <c>cstring</c>,
/// <c>stringPtrPtr</c>, a struct's or an enum's name, <c>FcnPtr</c>, and <c>error</c> for a type
/// nothing could name.
/// </summary>
/// <param name="Name">The export's name.</param>
/// <param name="CallType"><c>Thunk</c> or <c>cdecl</c> (both are the one x64 call), or <c>data</c>.</param>
/// <param name="Lhs">The return type, or null for <c>void</c>.</param>
/// <param name="Rhs">The parameter types; a varargs function ends in <c>error</c>.</param>
/// <param name="Alias">The name a script calls it by, when not <paramref name="Name"/>.</param>
/// <param name="ThunkName">The thunk R2025b would build for it; kept for the prototype file only.</param>
internal sealed record LibFunction(string Name, string CallType, string? Lhs, IReadOnlyList<string> Rhs, string? Alias, string? ThunkName)
{
    /// <summary>The declaration as the header wrote it, for the comment above it in a prototype file.</summary>
    public string? Declaration { get; init; }

    /// <summary>The name <c>calllib</c> and <c>libfunctions</c> know it by.</summary>
    public string CallName => string.IsNullOrEmpty(Alias) ? Name : Alias;

    /// <summary>Whether this is an exported variable rather than a function.</summary>
    public bool IsData => CallType == "data";
}

/// <summary>A struct member: its name and its MATLAB type (<c>int32#3</c> for an array of three).</summary>
internal readonly record struct LibMember(string Name, string Type);

/// <summary>A struct the prototype model records: its members in order, and its packing when not the default 8.</summary>
internal sealed record LibStruct(string Name, IReadOnlyList<LibMember> Members, int? Packing);

/// <summary>An enum the prototype model records: its members and their values, in order.</summary>
internal sealed record LibEnum(string Name, IReadOnlyList<(string Name, long Value)> Members);

/// <summary>
/// A library's interface as <c>loadlibrary</c> knows it (ADR 0181): the model of R2025b's prototype
/// file — <c>fcns</c>, <c>structs</c>, <c>enuminfo</c> — whichever road it came by. A header parsed by
/// <see cref="CHeaderParser"/> and a prototype file read by <see cref="PrototypeFile"/> both produce
/// one, <c>loadlibrary(…, 'mfilename', …)</c> writes one out, and <c>libfunctions</c> lists one.
/// </summary>
internal sealed class LibraryModel
{
    /// <summary>The functions and exported variables, in the header's order.</summary>
    public List<LibFunction> Functions { get; } = [];

    /// <summary>The structs, in the header's order.</summary>
    public List<LibStruct> Structs { get; } = [];

    /// <summary>The enums, in the header's order.</summary>
    public List<LibEnum> Enums { get; } = [];

    /// <summary>The struct named <paramref name="name"/>, or null.</summary>
    public LibStruct? Struct(string name) => Structs.Find(s => s.Name == name);

    /// <summary>The enum named <paramref name="name"/>, or null.</summary>
    public LibEnum? Enum(string name) => Enums.Find(e => e.Name == name);

    /// <summary>
    /// A type as <c>libfunctions -full</c> shows a parameter or a pointer output: an enum as its
    /// class <c>lib.&lt;name&gt;</c>, everything else by its name.
    /// </summary>
    public string ShowArgument(string type) => Enum(type) is not null ? "lib." + type : type;

    /// <summary>A return type as <c>libfunctions -full</c> shows it: a returned pointer is a <c>lib.pointer</c>.</summary>
    public string ShowReturn(string type) =>
        LibTypes.IsPointer(type) && type != "cstring" ? "lib.pointer" : ShowArgument(type);

    /// <summary>
    /// The line <c>libfunctions(lib, '-full')</c> prints for <paramref name="function"/>, R2025b's
    /// layout: the outputs (the return value, then every pointer argument's value after the call),
    /// the name, and the parameters, numbered <c>lhs1…</c> and <c>rhs1…</c>. One output is written
    /// bare and several in brackets; a function without parameters has no parentheses.
    /// </summary>
    public string Signature(LibFunction function)
    {
        var outputs = new List<string>();
        if (function.IsData)
        {
            outputs.Add("lib.pointer");
        }
        else
        {
            if (function.Lhs is { } returns)
            {
                outputs.Add(ShowReturn(returns));
            }

            outputs.AddRange(function.Rhs.Where(LibTypes.IsOutputArgument).Select(ShowArgument));
        }

        var text = new StringBuilder();
        if (outputs.Count == 1)
        {
            text.Append(outputs[0]).Append(" lhs1 ");
        }
        else if (outputs.Count > 1)
        {
            text.Append('[').AppendJoin(", ", outputs.Select((t, i) => $"{t} lhs{i + 1}")).Append("] ");
        }

        text.Append(function.CallName);
        if (!function.IsData && function.Rhs.Count > 0)
        {
            text.Append('(').AppendJoin(", ", function.Rhs.Select((t, i) => $"{ShowArgument(t)} rhs{i + 1}")).Append(')');
        }

        return text.ToString();
    }

    /// <summary>
    /// The size and alignment of a value of <paramref name="type"/> laid out as MSVC lays it out for
    /// x64: scalars at their own size, pointers at 8, an enum as an <c>int</c>, a struct by its
    /// members under its packing, and <c>T#n</c> as n of T. Null for a type with no layout
    /// (<c>error</c>, an unknown name).
    /// </summary>
    public (int Size, int Align)? Layout(string type)
    {
        int hash = type.LastIndexOf('#');
        if (hash > 0 && int.TryParse(type.AsSpan(hash + 1), out int count))
        {
            return Layout(type[..hash]) is { } element ? (element.Size * count, element.Align) : null;
        }

        if (LibTypes.ScalarSize(type) is { } scalar)
        {
            return (scalar, scalar);
        }

        if (LibTypes.IsPointer(type))
        {
            return (8, 8);
        }

        if (Enum(type) is not null)
        {
            return (4, 4);
        }

        if (Struct(type) is { } layout && StructLayout(layout) is { } fields)
        {
            return (fields.Size, fields.Align);
        }

        return null;
    }

    /// <summary>
    /// A struct's member offsets, size and alignment: each member at the next multiple of the lesser
    /// of its alignment and the packing, and the whole rounded up to the lesser of its widest
    /// member's alignment and the packing. Null when a member has no layout.
    /// </summary>
    public (int[] Offsets, int Size, int Align)? StructLayout(LibStruct type) => StructLayout(type, depth: 0);

    private (int[] Offsets, int Size, int Align)? StructLayout(LibStruct type, int depth)
    {
        if (depth > 32)
        {
            return null; // a struct that holds itself by value cannot be laid out
        }

        int packing = type.Packing ?? 8;
        var offsets = new int[type.Members.Count];
        int offset = 0;
        int widest = 1;
        for (int i = 0; i < type.Members.Count; i++)
        {
            string memberType = type.Members[i].Type;
            (int Size, int Align)? member = Struct(memberType) is { } inner
                ? StructLayout(inner, depth + 1) is { } nested ? (nested.Size, nested.Align) : null
                : Layout(memberType);
            if (member is not { } known)
            {
                return null;
            }

            int align = Math.Min(known.Align, packing);
            offset = (offset + align - 1) / align * align;
            offsets[i] = offset;
            offset += known.Size;
            widest = Math.Max(widest, align);
        }

        int size = Math.Max(1, (offset + widest - 1) / widest * widest);
        return (offsets, size, widest);
    }
}

/// <summary>MATLAB's shared-library type names, and what each one is.</summary>
internal static class LibTypes
{
    /// <summary>The size of a scalar type, or null for anything else.</summary>
    public static int? ScalarSize(string type) => type switch
    {
        "int8" or "uint8" or "bool" => 1,
        "int16" or "uint16" => 2,
        "int32" or "uint32" or "long" or "ulong" or "single" => 4,
        "int64" or "uint64" or "double" => 8,
        _ => null,
    };

    /// <summary>Whether <paramref name="type"/> is a number, <c>bool</c>, or Windows' 32-bit <c>long</c>.</summary>
    public static bool IsScalar(string type) => ScalarSize(type) is not null;

    /// <summary>Whether <paramref name="type"/> is passed as an address: <c>cstring</c>, or any <c>…Ptr</c>.</summary>
    public static bool IsPointer(string type) => type == "cstring" || type.EndsWith("Ptr", StringComparison.Ordinal);

    /// <summary>
    /// Whether an argument of <paramref name="type"/> comes back as an output of <c>calllib</c>: a
    /// <c>cstring</c> and every pointer, except a function pointer and the three-level pointers R2025b
    /// does not support (<c>jg_triple(doublePtrPtrPtr)</c> lists no output).
    /// </summary>
    public static bool IsOutputArgument(string type) =>
        type == "cstring"
        || (type.EndsWith("Ptr", StringComparison.Ordinal) && type != "FcnPtr" && !type.EndsWith("PtrPtrPtr", StringComparison.Ordinal));
}
