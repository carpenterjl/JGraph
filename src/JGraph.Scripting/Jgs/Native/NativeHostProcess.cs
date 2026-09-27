using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGraph.NativeHost;

namespace JGraph.Scripting.Jgs.Native;

/// <summary>
/// JGraph's end of one native host process (interop plan, stage 7, ADR 0180): it starts the host,
/// puts it in the job object, and turns each request into a frame on the pipe and each reply into a
/// value or a <see cref="NativeHostFailure"/>. When the host dies mid-request — native code crashed
/// it, or a cancel killed it — the request ends in <see cref="NativeHostExitedException"/>, and this
/// object is dead for good; the session starts another on its next native call.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class NativeHostProcess : IDisposable
{
    /// <summary>The host's file, beside JGraph's own assemblies.</summary>
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "JGraph.NativeHost.exe");

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static int _generations;

    private readonly Process _process;
    private readonly PipeStream _pipe;
    private readonly object _gate = new();
    private char[] _environment;
    private int _environmentVersion;
    private string? _folder;
    private MemoryMappedFile? _arena;
    private MemoryMappedViewAccessor? _arenaView;
    private byte* _arenaBase;
    private long _arenaSize;
    private volatile bool _cancelled;
    private NativeHostExitedException? _death;
    private bool _disposed;

    private NativeHostProcess(Process process, PipeStream pipe, char[] environment, int environmentVersion)
    {
        _process = process;
        _pipe = pipe;
        _environment = environment;
        _environmentVersion = environmentVersion;
        Generation = Interlocked.Increment(ref _generations);
        ProcessId = process.Id;
    }

    /// <summary>The host's process id.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// Which host this is, counted across the process. A library handle or a pointer remembers the
    /// generation it came from, so one from a host that has since died is known to be invalid.
    /// </summary>
    public int Generation { get; }

    /// <summary>Whether the host can still take requests.</summary>
    public bool IsAlive => _death is null && !_disposed;

    /// <summary>How the host died, once it has.</summary>
    public NativeHostExitedException? Death => _death;

    /// <summary>Starts a host and waits until it has answered its first request.</summary>
    public static NativeHostProcess Start()
    {
        string executable = ExecutablePath;
        if (!File.Exists(executable))
        {
            throw new NativeHostFailure(2, $"The native library host '{executable}' is missing; reinstall JGraph.");
        }

        string name = $"JGraph.NativeHost.{Environment.ProcessId}.{Guid.NewGuid():N}";

        // The host inherits the environment as it is now; later changes are sent before each call.
        int environmentVersion = EnvironmentBlock.Version;
        char[] environment = EnvironmentBlock.Current();
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(name);
        info.ArgumentList.Add("--parent");
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new NativeHostFailure(0, "The native library host did not start.");
        }
        catch (System.ComponentModel.Win32Exception fault)
        {
            throw new NativeHostFailure(fault.NativeErrorCode, $"The native library host did not start: {fault.Message}");
        }

        NativeJob.Adopt(process);

        // The host serves the pipe; JGraph connects once it is there, watching that the host has
        // not died on the way. Both ends are synchronous handles (an overlapped read costs a wait).
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                pipe.Connect(50);
                break;
            }
            catch (TimeoutException)
            {
                if (process.HasExited || clock.Elapsed > StartTimeout)
                {
                    string why = process.HasExited
                        ? $"it exited first ({NativeHostExitedException.Describe(process.ExitCode)})"
                        : $"it did not answer within {StartTimeout.TotalSeconds:0} s";
                    Kill(process);
                    pipe.Dispose();
                    throw new NativeHostFailure(0, $"The native library host did not start: {why}.");
                }
            }
        }

        var host = new NativeHostProcess(process, pipe, environment, environmentVersion);
        BinaryReader hello = host.Exchange(Request(HostOp.Hello), default);
        int version = hello.ReadInt32();
        if (version != HostProtocol.Version)
        {
            host.Dispose();
            throw new NativeHostFailure(0, $"The native library host speaks protocol {version}, not {HostProtocol.Version}; reinstall JGraph.");
        }

        return host;
    }

    // --- Requests ---------------------------------------------------------------------------------

    /// <summary>Loads a library by path (or bare name, for the system search); answers its module handle.</summary>
    public long Load(string path)
    {
        using MemoryStream request = Request(HostOp.Load, out BinaryWriter writer);
        HostProtocol.WriteString(writer, path);
        return Exchange(request, default).ReadInt64();
    }

    /// <summary>Frees a module handle <see cref="Load"/> answered.</summary>
    public void Free(long module)
    {
        using MemoryStream request = Request(HostOp.Free, out BinaryWriter writer);
        writer.Write(module);
        Exchange(request, default);
    }

    /// <summary>Resolves an export; a missing one is a <see cref="NativeHostFailure"/> with code 127.</summary>
    public long Symbol(long module, string name)
    {
        using MemoryStream request = Request(HostOp.Symbol, out BinaryWriter writer);
        writer.Write(module);
        HostProtocol.WriteString(writer, name);
        return Exchange(request, default).ReadInt64();
    }

    /// <summary>Allocates <paramref name="size"/> zeroed bytes in the host.</summary>
    public long Alloc(long size)
    {
        ReleaseDropped();
        using MemoryStream request = Request(HostOp.Alloc, out BinaryWriter writer);
        writer.Write(size);
        return Exchange(request, default).ReadInt64();
    }

    /// <summary>Frees memory <see cref="Alloc"/> answered.</summary>
    public void Release(long address)
    {
        using MemoryStream request = Request(HostOp.Release, out BinaryWriter writer);
        writer.Write(address);
        Exchange(request, default);
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<long> _dropped = new();

    /// <summary>
    /// Queues memory <see cref="Alloc"/> answered to be freed by the next <see cref="Call"/>
    /// (stage 9, ADR 0182). A finalizer calls this: it runs on the finalizer thread, which must not
    /// touch the pipe, so the free waits for the script thread's next call into native code.
    /// </summary>
    public void ReleaseLater(long address)
    {
        if (address != 0 && IsAlive)
        {
            _dropped.Enqueue(address);
        }
    }

    /// <summary>How many frees <see cref="ReleaseLater"/> has queued that no call has made yet.</summary>
    public int PendingReleases => _dropped.Count;

    /// <summary>Frees what <see cref="ReleaseLater"/> queued.</summary>
    private void ReleaseDropped()
    {
        while (IsAlive && _dropped.TryDequeue(out long address))
        {
            Release(address);
        }
    }

    /// <summary>Reads <paramref name="count"/> bytes at <paramref name="address"/>.</summary>
    public byte[] Read(long address, int count)
    {
        byte[] bytes = new byte[count];
        Read(address, bytes);
        return bytes;
    }

    /// <summary>Reads host memory at <paramref name="address"/> into <paramref name="into"/>.</summary>
    public void Read(long address, Span<byte> into)
    {
        bool viaArena = into.Length >= HostProtocol.ArenaThreshold;
        lock (_gate)
        {
            if (viaArena)
            {
                EnsureArena(into.Length);
            }

            using MemoryStream request = Request(HostOp.Read, out BinaryWriter writer);
            writer.Write(address);
            writer.Write((long)into.Length);
            writer.Write(viaArena);
            BinaryReader reply = Exchange(request, default);
            if (viaArena)
            {
                new ReadOnlySpan<byte>(_arenaBase, into.Length).CopyTo(into);
            }
            else
            {
                reply.BaseStream.ReadExactly(into);
            }
        }
    }

    /// <summary>Writes <paramref name="bytes"/> to host memory at <paramref name="address"/>.</summary>
    public void Write(long address, ReadOnlySpan<byte> bytes)
    {
        bool viaArena = bytes.Length >= HostProtocol.ArenaThreshold;
        lock (_gate)
        {
            if (viaArena)
            {
                EnsureArena(bytes.Length);
                bytes.CopyTo(new Span<byte>(_arenaBase, bytes.Length));
            }

            using MemoryStream request = Request(HostOp.Write, out BinaryWriter writer);
            writer.Write(address);
            writer.Write((long)bytes.Length);
            writer.Write(viaArena);
            if (!viaArena)
            {
                writer.Write(bytes);
            }

            Exchange(request, default);
        }
    }

    /// <summary>The length of the NUL-terminated byte string at <paramref name="address"/>.</summary>
    public long StringLength(long address)
    {
        using MemoryStream request = Request(HostOp.StringLength, out BinaryWriter writer);
        writer.Write(address);
        return Exchange(request, default).ReadInt64();
    }

    /// <summary>
    /// Calls <paramref name="function"/>. <paramref name="arguments"/> holds each parameter's bytes,
    /// <see cref="Slot.WireSize"/> apiece; the answer is the return's (<see cref="Slot.ReturnSize"/>).
    /// A cancel of <paramref name="cancel"/> while the call runs kills the host: native code cannot be
    /// interrupted any other way.
    /// </summary>
    public byte[] Call(long function, Slot result, IReadOnlyList<Slot> parameters, ReadOnlySpan<byte> arguments, CancellationToken cancel)
    {
        ReleaseDropped();
        using MemoryStream request = Request(HostOp.Call, out BinaryWriter writer);
        writer.Write(function);
        HostProtocol.WriteSlot(writer, result);
        writer.Write(parameters.Count);
        foreach (Slot parameter in parameters)
        {
            HostProtocol.WriteSlot(writer, parameter);
        }

        writer.Write(arguments);
        BinaryReader reply = Exchange(request, cancel);
        return reply.ReadBytes(result.ReturnSize);
    }

    /// <summary>
    /// Brings the host's working folder and environment up to date with JGraph's before a call:
    /// native code that opens a relative path or reads <c>getenv</c> sees what a MATLAB user expects,
    /// the folder <c>cd</c> moved to and every <c>setenv</c> (and every change .NET made). An unchanged
    /// state costs a compare and no request.
    /// </summary>
    public void Sync(string? folder)
    {
        char[]? changedBlock = null;
        List<(string Name, string? Value)>? changes = null;
        int version = EnvironmentBlock.Version;
        if (version != _environmentVersion)
        {
            EnvironmentBlock.Compare(_environment, ref changedBlock, ref changes);
            _environmentVersion = version;
        }

        if (changes is { Count: > 0 })
        {
            using MemoryStream request = Request(HostOp.SetEnvironment, out BinaryWriter writer);
            writer.Write(changes.Count);
            foreach ((string name, string? value) in changes)
            {
                HostProtocol.WriteString(writer, name);
                HostProtocol.WriteString(writer, value);
            }

            Exchange(request, default);
        }

        if (changedBlock is not null)
        {
            _environment = changedBlock;
        }

        if (folder is not null && !string.Equals(folder, _folder, StringComparison.OrdinalIgnoreCase))
        {
            using MemoryStream request = Request(HostOp.SetFolder, out BinaryWriter writer);
            HostProtocol.WriteString(writer, folder);
            try
            {
                Exchange(request, default);
                _folder = folder;
            }
            catch (NativeHostFailure)
            {
                // The folder is gone or unreachable; the host stays where it was, as a .NET call does.
            }
        }
    }

    // --- The wire ---------------------------------------------------------------------------------

    private static MemoryStream Request(HostOp op) => Request(op, out _);

    private static MemoryStream Request(HostOp op, out BinaryWriter writer)
    {
        var stream = new MemoryStream();
        writer = new BinaryWriter(stream);
        writer.Write((byte)op);
        return stream;
    }

    private BinaryReader Exchange(MemoryStream request, CancellationToken cancel)
    {
        lock (_gate)
        {
            if (_death is { } dead)
            {
                throw dead;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            cancel.ThrowIfCancellationRequested();

            byte[]? reply;
            CancellationTokenRegistration registration = cancel.CanBeCanceled
                ? cancel.Register(static state => ((NativeHostProcess)state!).Cancel(), this)
                : default;
            try
            {
                HostProtocol.WriteFrame(_pipe, request.GetBuffer().AsSpan(0, (int)request.Length));
                reply = HostProtocol.ReadFrame(_pipe);
            }
            catch (Exception fault) when (fault is IOException or ObjectDisposedException or InvalidOperationException)
            {
                reply = null;
            }
            finally
            {
                registration.Dispose();
            }

            if (reply is null)
            {
                throw Died();
            }

            var reader = new BinaryReader(new MemoryStream(reply, writable: false));
            if (reader.ReadByte() == HostProtocol.Failed)
            {
                int code = reader.ReadInt32();
                throw new NativeHostFailure(code, HostProtocol.ReadString(reader) ?? string.Empty);
            }

            return reader;
        }
    }

    /// <summary>A cancel during a request: native code cannot be interrupted, so the host is ended.</summary>
    private void Cancel()
    {
        _cancelled = true;
        Kill(_process);
    }

    /// <summary>The host went away mid-request: find out why, and stay dead.</summary>
    private NativeHostExitedException Died()
    {
        if (!_process.WaitForExit(5000))
        {
            Kill(_process);
            _process.WaitForExit(5000);
        }

        int exitCode = _process.HasExited ? _process.ExitCode : -1;
        _death = new NativeHostExitedException(_cancelled, exitCode);
        ReleaseResources();
        return _death;
    }

    private void EnsureArena(long size)
    {
        if (_arenaBase is not null && size <= _arenaSize)
        {
            return;
        }

        long capacity = Math.Max(1L << 20, (long)System.Numerics.BitOperations.RoundUpToPowerOf2((ulong)size));
        string name = $"JGraph.NativeHost.Arena.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var arena = MemoryMappedFile.CreateNew(name, capacity, MemoryMappedFileAccess.ReadWrite);
        MemoryMappedViewAccessor view = arena.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.ReadWrite);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

        using MemoryStream request = Request(HostOp.MapArena, out BinaryWriter writer);
        HostProtocol.WriteString(writer, name);
        writer.Write(capacity);
        try
        {
            Exchange(request, default);
        }
        catch
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
            view.Dispose();
            arena.Dispose();
            throw;
        }

        ReleaseArena();
        _arena = arena;
        _arenaView = view;
        _arenaBase = pointer + view.PointerOffset;
        _arenaSize = capacity;
    }

    private void ReleaseArena()
    {
        if (_arenaView is not null)
        {
            _arenaView.SafeMemoryMappedViewHandle.ReleasePointer();
            _arenaView.Dispose();
        }

        _arena?.Dispose();
        _arenaView = null;
        _arena = null;
        _arenaBase = null;
        _arenaSize = 0;
    }

    private void ReleaseResources()
    {
        ReleaseArena();
        _pipe.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception fault) when (fault is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It is already gone.
        }
    }

    /// <summary>
    /// Ends the host: asks it to exit, and kills it if it does not within a moment. Every library it
    /// loaded and every allocation it made goes with it.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_death is null)
            {
                try
                {
                    Exchange(Request(HostOp.Exit), default);
                }
                catch (Exception fault) when (fault is NativeHostExitedException or NativeHostFailure or IOException)
                {
                    // Ending anyway.
                }

                if (!_process.WaitForExit(2000))
                {
                    Kill(_process);
                }

                ReleaseResources();
            }

            _disposed = true;
            _process.Dispose();
        }
    }
}

/// <summary>A request the host refused: a Win32 error code and the system's text for it.</summary>
internal sealed class NativeHostFailure(int code, string message) : Exception(message)
{
    /// <summary>The Win32 error code (127 for an export that is not there, 126 for a missing module).</summary>
    public int Code { get; } = code;
}

/// <summary>
/// The host died during a request: native code crashed it (<see cref="ExitCode"/> says how), or a
/// cancel killed it. Every library it had loaded is unloaded and every pointer into it is invalid.
/// </summary>
internal sealed class NativeHostExitedException(bool cancelled, int exitCode)
    : Exception(cancelled ? "The native library host was stopped." : $"The native library host stopped ({Describe(exitCode)}).")
{
    /// <summary>Whether a cancel (Stop, Ctrl+C) ended it rather than native code.</summary>
    public bool Cancelled { get; } = cancelled;

    /// <summary>The host's exit code: an NTSTATUS exception code when native code crashed it.</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>The sentence a script sees, for a crash during <paramref name="during"/> (<c>calllib('lib', 'fn')</c>).</summary>
    public string Sentence(string during) => Cancelled
        ? $"The native library host was stopped during {during}. Every library it had loaded is unloaded, and every lib.pointer into it is invalid."
        : $"The native library host stopped ({Describe(ExitCode)}) during {during}. Every library it had loaded is unloaded, and every lib.pointer into it is invalid.";

    /// <summary>An exit code in words: <c>exception 0xC0000005, access violation</c>, or <c>exit code 3</c>.</summary>
    public static string Describe(int exitCode)
    {
        uint code = unchecked((uint)exitCode);
        if (code < 0x80000000)
        {
            return $"exit code {exitCode}";
        }

        string? name = code switch
        {
            0xC0000005 => "access violation",
            0xC00000FD => "stack overflow",
            0xC0000094 => "integer division by zero",
            0xC0000095 => "integer overflow",
            0xC000008E => "floating-point division by zero",
            0xC000001D => "illegal instruction",
            0xC0000096 => "privileged instruction",
            0xC0000409 => "stack buffer overrun",
            0xC0000374 => "heap corruption",
            0xC0000602 => "fail fast",
            0x80000003 => "breakpoint",
            0xE0434352 => "unhandled .NET exception",
            _ => null,
        };
        return name is null ? $"exception 0x{code:X8}" : $"exception 0x{code:X8}, {name}";
    }
}
