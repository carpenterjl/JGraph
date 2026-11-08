using System.Runtime.InteropServices;
using JGraph.Runtime.Native;

namespace JGraph.Runtime.Com;

/// <summary>How a COM client invoked a member (DISPATCH_METHOD and its kin).</summary>
internal enum ComInvokeKind
{
    Method = 1,
    PropertyGet = 2,
    PropertyPut = 4,
    PropertyPutRef = 8,
}

/// <summary>
/// An error a COM call reports to its client: an HRESULT, and through IErrorInfo the source (the
/// add-in's versioned ProgID, as R2025b's add-ins name themselves: <c>src=[k17xl.K17Xl.1_0]</c>) and
/// the description.
/// </summary>
internal sealed class ComCallException(int hresult, string source, string description) : Exception(description)
{
    /// <summary>E_FAIL, what an R2025b add-in answers for every error of a call (k17_com: err.*).</summary>
    public const int Fail = unchecked((int)0x80004005);

    /// <summary>DISP_E_MEMBERNOTFOUND.</summary>
    public const int MemberNotFound = unchecked((int)0x80020003);

    /// <summary>DISP_E_TYPEMISMATCH.</summary>
    public const int TypeMismatch = unchecked((int)0x80020005);

    /// <summary>DISP_E_BADINDEX, what an MWStruct answers for an index or a field it does not have (k17_com_extra: x.struct.item_*).</summary>
    public const int BadIndex = unchecked((int)0x8002000B);

    /// <summary>E_INVALIDARG.</summary>
    public const int InvalidArgument = unchecked((int)0x80070057);

    /// <summary>The HRESULT the call returns.</summary>
    public int Code { get; } = hresult;

    /// <summary>The error's source, the versioned ProgID.</summary>
    public string ErrorSource { get; } = source;

    /// <summary>Whether the client gets the HRESULT alone, with no error info (the system's own text for the code), as R2025b's value classes answer a bad index.</summary>
    public bool IsSilent { get; private init; }

    /// <summary>An error with no error info: the bare HRESULT.</summary>
    public static ComCallException Silent(int hresult) => new(hresult, "", "") { IsSilent = true };
}

/// <summary>
/// An object the JGraph Runtime serves to COM clients through the generic objects of jgrt.dll
/// (compiler and SDK plan, stage K17c; <c>native/jgrt/src/com.cpp</c>): the add-in's component and
/// its flags. jgrt's object holds a handle to this one and forwards each member call by name
/// (<see cref="Invoke"/>); when this object is handed to a client, jgrt wraps it under the
/// interface <see cref="InterfaceId"/> of the add-in's type library, and the same wrapper is
/// handed out again while it lives (k17_com: flags.sameobject).
/// </summary>
/// <remarks>
/// A wrapper's life is its COM references'. A client's references go when its garbage collector
/// says so, on a thread of its own, so a wrapper this side had cached could die between a check
/// and a use. An object with an owner (the component's flags, the flags' two parts) therefore
/// keeps one reference of its own on its wrapper, released when the owner is released
/// (<see cref="Released"/>); an object with none (a clone a client asked for) caches no wrapper,
/// and its handle goes when the client's last reference does.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal abstract class ComServed(nint module, string progId)
{
    private readonly List<ComServed> _children = [];

    /// <summary>jgrt's module for the add-in DLL this object belongs to, whose type library declares the interfaces.</summary>
    public nint Module { get; } = module;

    /// <summary>The add-in's versioned ProgID, which every error names as its source.</summary>
    public string ProgId { get; } = progId;

    /// <summary>The interface of the add-in's type library this object is served under.</summary>
    public abstract Guid InterfaceId { get; }

    /// <summary>The flags a <see cref="RuntimeValue"/> this object answers from <see cref="Invoke"/> is converted under; null for MathWorks' defaults.</summary>
    public virtual ExcelFlags? FlagsForResults => null;

    /// <summary>The object this one belongs to, which keeps its wrapper alive; null for one a client owns alone.</summary>
    public ComServed? Owner { get; private set; }

    /// <summary>jgrt's COM object serving this one, with this side's own reference on it; 0 when none is cached.</summary>
    internal nint Wrapper { get; set; }

    /// <summary>The handle jgrt's object holds, while one does.</summary>
    internal GCHandle Handle { get; set; }

    /// <summary>
    /// A member called by a client: a method, or a property read or written. Answers a
    /// <see cref="RuntimeValue"/>, a <see cref="ComServed"/> object, a string, an int, a bool or null
    /// for the result, and throws <see cref="ComCallException"/> for what the client should hear.
    /// </summary>
    public abstract object? Invoke(string member, ComInvokeKind kind, ComArguments arguments);

    /// <summary>Makes <paramref name="child"/> this object's, so its wrapper lives as long as this one is served.</summary>
    protected void Adopt(ComServed child)
    {
        child.Owner = this;
        if (!_children.Contains(child))
        {
            _children.Add(child);
        }
    }

    /// <summary>jgrt's object let this one's last reference go: the children's wrappers are let go too, then <see cref="OnReleased"/>.</summary>
    internal void Released()
    {
        foreach (ComServed child in _children)
        {
            ComObjects.DropWrapper(child);
        }

        OnReleased();
    }

    /// <summary>What an object does when its last COM reference goes (a component ends its hold on its session).</summary>
    protected virtual void OnReleased()
    {
    }

    /// <summary>An error of this object's, E_FAIL unless another code is given.</summary>
    protected ComCallException Fail(string description, int code = ComCallException.Fail) => new(code, ProgId, description);

    /// <summary>A member the interface does not have (DISP_E_MEMBERNOTFOUND), which a client with the type library never asks for.</summary>
    protected ComCallException NoMember(string member) =>
        new(ComCallException.MemberNotFound, ProgId, $"The object has no member '{member}'.");
}

/// <summary>The engine's side of jgrt's object handles: each served object pinned while jgrt's object holds it.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ComObjects
{
    private static readonly object Gate = new();

    /// <summary>Pins <paramref name="served"/> for jgrt and answers the handle jgrt's object keeps.</summary>
    public static nint Attach(ComServed served)
    {
        lock (Gate)
        {
            if (!served.Handle.IsAllocated)
            {
                served.Handle = GCHandle.Alloc(served);
            }

            return GCHandle.ToIntPtr(served.Handle);
        }
    }

    /// <summary>The object behind <paramref name="handle"/>, or null for a handle that is not one of ours.</summary>
    public static ComServed? Of(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        try
        {
            return GCHandle.FromIntPtr(handle).Target as ComServed;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>jgrt's object let its last reference go: the pin is freed and the object told.</summary>
    public static void Release(nint handle)
    {
        ComServed? served = Of(handle);
        if (served is null)
        {
            return;
        }

        lock (Gate)
        {
            served.Wrapper = 0;
            if (served.Handle.IsAllocated)
            {
                served.Handle.Free();
            }

            served.Released();
        }
    }

    /// <summary>
    /// jgrt's COM object for <paramref name="served"/>, with one reference for the caller: the one
    /// cached while the object's owner is served, else a new one, cached with a reference of this
    /// side's own when the object has an owner.
    /// </summary>
    public static nint Wrap(ComServed served)
    {
        lock (Gate)
        {
            if (served.Wrapper != 0)
            {
                Marshal.AddRef(served.Wrapper);
                return served.Wrapper;
            }

            nint handle = Attach(served);
            nint wrapper = served.Module == 0 ? 0 : NativeComCalls.Wrap(handle, served.Module, served.InterfaceId);
            if (wrapper == 0)
            {
                served.Handle.Free();
                throw new ComCallException(ComCallException.Fail, served.ProgId, served.Module == 0
                    ? $"The JGraph Runtime's COM utility library ({JGraph.Deploy.ExcelAddIn.UtilityName}) was not found beside jgrt.dll, so a {served.GetType().Name.Replace("Excel", "MW", StringComparison.Ordinal)} object cannot be handed to a COM client."
                    : $"The type library has no interface {served.InterfaceId:B} for a {served.GetType().Name}.");
            }

            if (served.Owner is not null)
            {
                Marshal.AddRef(wrapper);
                served.Wrapper = wrapper;
            }

            return wrapper;
        }
    }

    /// <summary>Caches <paramref name="wrapper"/>, a COM object of jgrt's a client holds for <paramref name="served"/>, as its wrapper (one reference taken), when none is cached yet.</summary>
    public static void Remember(ComServed served, nint wrapper)
    {
        lock (Gate)
        {
            if (served.Wrapper == 0 && wrapper != 0 && NativeComCalls.Unwrap(wrapper) != 0)
            {
                Marshal.AddRef(wrapper);
                served.Wrapper = wrapper;
            }
        }
    }

    /// <summary>Lets go of this side's reference on <paramref name="served"/>'s cached wrapper, which may end it (and release the object in turn).</summary>
    public static void DropWrapper(ComServed served)
    {
        nint wrapper;
        lock (Gate)
        {
            wrapper = served.Wrapper;
            served.Wrapper = 0;
        }

        if (wrapper != 0)
        {
            Marshal.Release(wrapper);
        }
    }
}
