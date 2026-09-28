using System.Runtime.Versioning;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// Memory JGraph allocated in a native host for a <c>lib.pointer</c> or a libstruct (stage 9, ADR
/// 0182): the value a <c>libpointer</c> was made with, a struct's bytes, a C string array and its
/// strings. Every pointer made from it — <c>p + 1</c>, a pointer-to-pointer's cell — holds the block,
/// so the memory lives while anything can still reach it, and is freed after the last holder is
/// collected: the finalizer queues the free, and the host's next call or allocation makes it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeBlock
{
    private readonly long[] _children;

    private NativeBlock(NativeHostProcess process, long address, long size, long[]? children, object? keeps)
    {
        Process = process;
        Address = address;
        Size = size;
        _children = children ?? [];
        Keeps = keeps;
    }

    /// <summary>The host the memory is in.</summary>
    public NativeHostProcess Process { get; }

    /// <summary>The memory's address in <see cref="Process"/>.</summary>
    public long Address { get; }

    /// <summary>How many bytes it holds.</summary>
    public long Size { get; }

    /// <summary>A value the memory points into, kept alive with it: a pointer-to-pointer's target.</summary>
    public object? Keeps { get; }

    /// <summary>Allocates a block holding <paramref name="bytes"/>, owning <paramref name="children"/> and keeping <paramref name="keeps"/> alive too.</summary>
    public static NativeBlock Allocate(NativeHostProcess process, ReadOnlySpan<byte> bytes, long[]? children = null, object? keeps = null)
    {
        long address = process.Alloc(Math.Max(bytes.Length, 1));
        if (bytes.Length > 0)
        {
            process.Write(address, bytes);
        }

        return new NativeBlock(process, address, bytes.Length, children, keeps);
    }

    /// <summary>Whether <paramref name="count"/> bytes at <paramref name="address"/> lie inside the block.</summary>
    public bool Holds(long address, long count) => address >= Address && address + count <= Address + Size;

    ~NativeBlock()
    {
        Process.ReleaseLater(Address);
        foreach (long child in _children)
        {
            Process.ReleaseLater(child);
        }
    }
}

/// <summary>
/// A <c>lib.pointer</c> (stage 9, ADR 0182): an address in the session's native host, the
/// <c>DataType</c> it was made or returned as, and what its <c>Value</c> reads — an element type and a
/// size, which a pointer a library returned does not have until <c>setdatatype</c> or <c>reshape</c>
/// says (R2025b, shrlib_pointers). A pointer <c>libpointer</c> made owns its memory
/// (<see cref="Block"/>); one a library returned never frees what it points at.
/// </summary>
internal sealed class LibPointer(string dataType) : IJgsExternal
{
    /// <summary>What <c>properties</c>, <c>fieldnames</c> and <c>get</c> list, in R2025b's order.</summary>
    public static readonly string[] PropertyNames = ["Value", "DataType"];

    /// <summary>What <c>methods</c> lists: lib.pointer's own and the ones it inherits from handle (probe_shrlib_pointers2).</summary>
    public static readonly string[] MethodNames =
        ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "get", "gt", "isNull", "isvalid", "le",
         "listener", "lt", "ne", "notify", "plus", "reshape", "set", "setdatatype"];

    /// <summary>The type name: <c>doublePtr</c>, <c>stringPtrPtr</c>, <c>jg_pointPtr</c>, or empty for <c>libpointer()</c>.</summary>
    public string DataType { get; set; } = dataType;

    /// <summary>The host the address is in; null for a null pointer made without one.</summary>
    public NativeHostProcess? Process { get; set; }

    /// <summary>The address, zero for NULL.</summary>
    public long Address { get; set; }

    /// <summary>The memory JGraph allocated for it, which it keeps alive; null for a library's memory.</summary>
    public NativeBlock? Block { get; set; }

    /// <summary>
    /// What <c>Value</c> reads at the address: a scalar type, <c>cstring</c>, <c>string</c> (a C string
    /// array), or a struct's name; null when nothing says (a <c>voidPtr</c> a library returned).
    /// </summary>
    public string? Element { get; set; }

    /// <summary>The model a struct element's layout comes from.</summary>
    public LibraryModel? Model { get; set; }

    /// <summary>The size <c>Value</c> reads, when <see cref="Sized"/>.</summary>
    public int Rows { get; set; }

    /// <inheritdoc cref="Rows"/>
    public int Cols { get; set; }

    /// <summary>Whether the size is known: made with a value, or set by <c>reshape</c> or <c>setdatatype</c>.</summary>
    public bool Sized { get; set; }

    /// <summary><c>delete(p)</c> ran: every name for it is invalid.</summary>
    public bool Deleted { get; set; }

    /// <summary>Whether the pointer is NULL.</summary>
    public bool IsNull => Address == 0;

    public string ClassName => "lib.pointer";

    public bool IsHandle => true;

    public bool IsA(string className) => className is "lib.pointer" or "handle";

    public IJgsExternal CopyForBinding() => this;

    public string Display() => "libpointer";

    /// <summary>The Workspace pane's words for it: its type, and NULL or its size; never its memory.</summary>
    public string? Summary() =>
        Deleted ? "deleted"
        : IsNull ? (DataType.Length == 0 ? "NULL" : DataType + ", NULL")
        : Sized ? $"{DataType}, {Rows}×{Cols}"
        : DataType;

    public string Kind => "C library value";

    /// <summary>A second pointer to the same place, of the same type and size, holding the same memory.</summary>
    public LibPointer Alias() => new(DataType)
    {
        Process = Process,
        Address = Address,
        Block = Block,
        Element = Element,
        Model = Model,
        Rows = Rows,
        Cols = Cols,
        Sized = Sized,
    };
}

/// <summary>
/// A libstruct (stage 9, ADR 0182): an object of class <c>lib.&lt;struct&gt;</c> over a struct's bytes
/// in the native host, laid out as the library's model lays it out. Its fields are its properties; a
/// write goes straight into host memory and a call that is handed it writes the same memory, so the
/// object sees what the library did (R2025b, shrlib_structs). It holds its library loaded:
/// <c>unloadlibrary</c> refuses while one is alive.
/// </summary>
internal sealed class LibStructValue(SharedLibrary library, LibStruct type, NativeBlock block) : IJgsExternal
{
    /// <summary>The library whose model defines the struct.</summary>
    public SharedLibrary Library { get; } = library;

    /// <summary>The struct type.</summary>
    public LibStruct Type { get; } = type;

    /// <summary>The struct's bytes in the host.</summary>
    public NativeBlock Block { get; } = block;

    /// <summary>The model the layout comes from: the library's.</summary>
    public LibraryModel Model { get; } = OperatingSystem.IsWindows() ? library.Model : throw new PlatformNotSupportedException();

    /// <summary><c>delete(s)</c> ran: every name for it is invalid.</summary>
    public bool Deleted { get; set; }

    public string ClassName => "lib." + Type.Name;

    public bool IsHandle => true;

    public bool IsA(string className) => className == ClassName || className == "handle";

    public IJgsExternal CopyForBinding() => this;

    public string Display() => OperatingSystem.IsWindows() ? JgsBuiltins.LibStructDisplay(this) : ClassName;

    /// <summary>The Workspace pane's words for it: nothing past its class, since its fields live in the host.</summary>
    public string? Summary() => Deleted ? "deleted" : null;

    public string Kind => "C library value";

    /// <summary>What <c>methods</c> lists: handle's, the struct's constructor and <c>structsize</c> (probe_shrlib_pointers2).</summary>
    public IEnumerable<string> MethodNames => MethodNamesOf(Type.Name);

    /// <summary>The method names of the libstruct class <c>lib.&lt;name&gt;</c>: its constructor, <c>structsize</c>, and a handle's.</summary>
    public static IReadOnlyList<string> MethodNamesOf(string name) =>
        [.. new[] { "addlistener", "delete", "eq", "findobj", "findprop", "ge", "get", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "set", "structsize", name }
            .Order(StringComparer.OrdinalIgnoreCase)];
}
