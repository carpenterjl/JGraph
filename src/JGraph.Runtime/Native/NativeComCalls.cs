using System.Runtime.InteropServices;
using JGraph.Runtime.Com;

namespace JGraph.Runtime.Native;

/// <summary>
/// What jgrt.dll's COM objects do in the engine (compiler and SDK plan, stage K17c;
/// <c>native/jgrt/src/com.cpp</c>): an add-in's component object opened for a class factory, a
/// member of a served object invoked by name with its VARIANT arguments, and an object let go when
/// its last COM reference is released. The other way, jgrt wraps an engine object as a COM object
/// (<see cref="Wrap"/>) and reads one back (<see cref="Unwrap"/>).
/// </summary>
/// <remarks>
/// Nothing thrown may leave an entry point: a failure is 0 or a failed HRESULT with its source and
/// description handed back as CoTaskMem strings, which jgrt turns into the client's IErrorInfo.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class NativeComCalls
{
    private const int Fail = unchecked((int)0x80004005);

    private static delegate* unmanaged<nint, nint, Guid*, nint> _wrap;
    private static delegate* unmanaged<nint, nint> _unwrap;

    /// <summary>Finds jgrt's functions in <paramref name="module"/>.</summary>
    internal static void Bind(nint module)
    {
        _wrap = (delegate* unmanaged<nint, nint, Guid*, nint>)NativeLibrary.GetExport(module, "jgrtComWrap");
        _unwrap = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(module, "jgrtComUnwrap");
    }

    /// <summary>jgrt's COM object for the served object <paramref name="handle"/> under <paramref name="interfaceId"/> of <paramref name="module"/>'s type library; 0 when the library lacks it.</summary>
    internal static nint Wrap(nint handle, nint module, Guid interfaceId) => _wrap == null ? 0 : _wrap(handle, module, &interfaceId);

    /// <summary>The served object's handle behind a COM object of jgrt's, or 0 for any other object.</summary>
    internal static nint Unwrap(nint comObject) => _unwrap == null || comObject == 0 ? 0 : _unwrap(comObject);

    /// <summary>The class factory's CreateInstance: the component object of the add-in DLL, or 0 and why.</summary>
    [UnmanagedCallersOnly]
    internal static nint Create(char* dllPath, nint module, char** error)
    {
        try
        {
            ExcelComponent component = ExcelComponent.Open(new string(dllPath), module);
            return ComObjects.Attach(component);
        }
        catch (Exception failure)
        {
            if (error != null)
            {
                *error = (char*)Marshal.StringToCoTaskMemUni(failure.Message);
            }

            return 0;
        }
    }

    /// <summary>A member invoked: S_OK with the result written, or a failed HRESULT with its source and description.</summary>
    [UnmanagedCallersOnly]
    internal static int Invoke(nint handle, char* member, int kind, int argc, Variant** argv, Variant* result, char** source, char** description)
    {
        ComServed? target = ComObjects.Of(handle);
        if (target is null)
        {
            Say(source, description, "JGraph Excel add-in", "The object is no longer served by the JGraph Runtime.");
            return Fail;
        }

        try
        {
            object? answer = target.Invoke(new string(member), (ComInvokeKind)kind, new ComArguments(argv, argc));
            ExcelVariants.WriteResult(answer, result, target);
            return 0;
        }
        catch (ComCallException refused)
        {
            Say(source, description, refused.ErrorSource, refused.Message);
            return refused.Code;
        }
        catch (Exception failure)
        {
            // The boundary with COM: whatever failed is the call's error, never the process's end.
            Say(source, description, target.ProgId, failure.Message);
            return Fail;
        }
    }

    /// <summary>The last COM reference to a served object went.</summary>
    [UnmanagedCallersOnly]
    internal static void Release(nint handle)
    {
        try
        {
            ComObjects.Release(handle);
        }
        catch (Exception)
        {
            // A component whose session has already ended; nothing more to let go.
        }
    }

    private static void Say(char** source, char** description, string from, string text)
    {
        if (source != null)
        {
            *source = (char*)Marshal.StringToCoTaskMemUni(from);
        }

        if (description != null)
        {
            *description = (char*)Marshal.StringToCoTaskMemUni(text);
        }
    }
}
