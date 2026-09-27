using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;

namespace JGraph.NativeHost;

/// <summary>
/// <c>JGraph.NativeHost.exe --pipe NAME --parent PID</c>: started by JGraph on a session's first native
/// call, never by a user. It opens the pipe JGraph named, waits for JGraph to connect, and answers
/// requests until told to exit, until JGraph goes, or until native code ends it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    /// <summary>The stack of the thread that makes the calls: native code gets room to recurse.</summary>
    private const int CallStackSize = 16 * 1024 * 1024;

    private static FileStream? _nul;

    private static int Main(string[] args)
    {
        // A crash in native code must end this process at once, not wait on an error dialog.
        NativeMethods.SetErrorMode(NativeMethods.QuietErrorMode);

        string? pipeName = Argument(args, "--pipe");
        if (pipeName is null)
        {
            Console.Error.WriteLine("JGraph.NativeHost is started by JGraph for loadlibrary and calllib; it is not run by hand.");
            return 2;
        }

        // What native code prints goes nowhere, as in the MATLAB desktop (ADR 0180). The handles are
        // swapped before any library loads, so a library's C runtime picks up NUL when it starts.
        _nul = new FileStream("NUL", FileMode.Open, FileAccess.ReadWrite);
        nint nul = _nul.SafeFileHandle.DangerousGetHandle();
        NativeMethods.SetStdHandle(NativeMethods.StdInput, nul);
        NativeMethods.SetStdHandle(NativeMethods.StdOutput, nul);
        NativeMethods.SetStdHandle(NativeMethods.StdError, nul);

        if (int.TryParse(Argument(args, "--parent"), out int parentId))
        {
            WatchParent(parentId);
        }

        // The host serves the pipe and JGraph connects, so both ends are synchronous handles: an
        // overlapped one costs a wait on every read, which is most of a call's round trip.
        using var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
        pipe.WaitForConnection();
        var server = new HostServer(pipe);
        var calls = new Thread(server.Run, CallStackSize) { Name = "JGraph native calls" };
        calls.Start();
        calls.Join();
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    /// Ends the host when JGraph ends. The job object JGraph puts the host in does this already; this
    /// covers the moment before the host joins it.
    /// </summary>
    private static void WatchParent(int parentId)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(parentId);
        }
        catch (ArgumentException)
        {
            Environment.Exit(3); // JGraph is already gone
            return;
        }

        var watcher = new Thread(() =>
        {
            parent.WaitForExit();
            Environment.Exit(3);
        })
        {
            IsBackground = true,
            Name = "JGraph watcher",
        };
        watcher.Start();
    }
}
