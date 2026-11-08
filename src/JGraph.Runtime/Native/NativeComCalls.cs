using System.Runtime.InteropServices;
using JGraph.Deploy;
using JGraph.Runtime.Com;

namespace JGraph.Runtime.Native;

/// <summary>
/// What jgrt.dll's COM objects do in the engine (compiler and SDK plan, stages K17c and K17d;
/// <c>native/jgrt/src/com.cpp</c>): an object of a coclass made for a class factory (an add-in's
/// component, or one of the COM utility library's classes), a member of a served object invoked by
/// name with its VARIANT arguments, and an object let go when its last COM reference is released.
/// The other way, jgrt wraps an engine object as a COM object (<see cref="Wrap"/>), reads one back
/// (<see cref="Unwrap"/>) and loads a DLL's type library as a module (<see cref="LoadModule"/>).
/// </summary>
/// <remarks>
/// Nothing thrown may leave an entry point: a failure is 0 or a failed HRESULT with its source and
/// description handed back as CoTaskMem strings, which jgrt turns into the client's IErrorInfo; a
/// silent one hands back neither, and the client gets the bare HRESULT.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class NativeComCalls
{
    private const int Fail = unchecked((int)0x80004005);

    private static delegate* unmanaged<nint, nint, Guid*, nint> _wrap;
    private static delegate* unmanaged<nint, nint> _unwrap;
    private static delegate* unmanaged<char*, nint> _loadModule;

    /// <summary>Finds jgrt's functions in <paramref name="module"/>.</summary>
    internal static void Bind(nint module)
    {
        _wrap = (delegate* unmanaged<nint, nint, Guid*, nint>)NativeLibrary.GetExport(module, "jgrtComWrap");
        _unwrap = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(module, "jgrtComUnwrap");
        _loadModule = NativeLibrary.TryGetExport(module, "jgrtComLoadModule", out nint load) ? (delegate* unmanaged<char*, nint>)load : null;
    }

    /// <summary>Whether jgrt is bound, so served objects can be wrapped for a COM client.</summary>
    internal static bool IsBound => _wrap != null;

    /// <summary>jgrt's COM object for the served object <paramref name="handle"/> under <paramref name="interfaceId"/> of <paramref name="module"/>'s type library; 0 when the library lacks it.</summary>
    internal static nint Wrap(nint handle, nint module, Guid interfaceId) => _wrap == null ? 0 : _wrap(handle, module, &interfaceId);

    /// <summary>The served object's handle behind a COM object of jgrt's, or 0 for any other object.</summary>
    internal static nint Unwrap(nint comObject) => _unwrap == null || comObject == 0 ? 0 : _unwrap(comObject);

    /// <summary>jgrt's module for the DLL at <paramref name="path"/> (its type library read), or 0 when jgrt is not bound or the DLL carries none.</summary>
    internal static nint LoadModule(string path)
    {
        if (_loadModule == null)
        {
            return 0;
        }

        fixed (char* p = path)
        {
            return _loadModule(p);
        }
    }

    /// <summary>
    /// The class factory's CreateInstance: the object of <paramref name="className"/> of the library
    /// <paramref name="library"/> in the DLL at <paramref name="dllPath"/> - the COM utility library's
    /// class (stage K17d), else the add-in's component - or 0 and why.
    /// </summary>
    [UnmanagedCallersOnly]
    internal static nint Create(char* dllPath, nint module, char* library, char* className, char** error)
    {
        try
        {
            string libraryName = library == null ? "" : new string(library);
            string name = className == null ? "" : new string(className);
            ComServed made = libraryName == ExcelComGuids.UtilityLibraryName
                ? ExcelComUtilityLibrary.Create(name, module)
                : ExcelComponent.Open(new string(dllPath), module);
            return ComObjects.Attach(made);
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
            if (answer is RuntimeValue value)
            {
                answer = ExcelConversion.ToClient(value, target.FlagsForResults, target);
            }

            ExcelVariants.Write(answer, result);
            return 0;
        }
        catch (ComCallException refused)
        {
            if (!refused.IsSilent)
            {
                Say(source, description, refused.ErrorSource, refused.Message);
            }

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
