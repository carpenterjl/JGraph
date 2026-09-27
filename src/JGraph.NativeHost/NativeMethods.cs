using System.Runtime.InteropServices;

namespace JGraph.NativeHost;

/// <summary>The Win32 calls the host makes for itself.</summary>
internal static partial class NativeMethods
{
    /// <summary><c>LOAD_WITH_ALTERED_SEARCH_PATH</c>: a path's own folder is searched for its dependencies.</summary>
    public const uint LoadWithAlteredSearchPath = 0x00000008;

    /// <summary>No critical-error, fault or open-file dialogs: a crash ends the process at once.</summary>
    public const uint QuietErrorMode = 0x0001 | 0x0002 | 0x8000;

    public const int StdInput = -10;
    public const int StdOutput = -11;
    public const int StdError = -12;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibraryEx(string path, nint reserved, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeLibrary(nint module);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint module, string name);

    [LibraryImport("kernel32.dll")]
    public static partial uint SetErrorMode(uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetStdHandle(int which, nint handle);
}
