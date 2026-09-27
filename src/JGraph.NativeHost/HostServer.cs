using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace JGraph.NativeHost;

/// <summary>
/// The host's request loop: one request at a time, answered in order, on the thread that makes the
/// calls. A failure the host can describe (a library that will not load, an export that is not there)
/// is a reply; a failure inside native code ends the process, which is what JGraph watches for.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed unsafe class HostServer(Stream pipe)
{
    private readonly List<string> _libraryFolders = [];
    private string? _pathFromJGraph = Environment.GetEnvironmentVariable("PATH");
    private MemoryMappedFile? _arena;
    private MemoryMappedViewAccessor? _arenaView;
    private byte* _arenaBase;
    private long _arenaSize;

    /// <summary>Answers requests until JGraph says <see cref="HostOp.Exit"/> or goes away.</summary>
    public void Run()
    {
        while (HostProtocol.ReadFrame(pipe) is { } request)
        {
            using var reply = new MemoryStream();
            using var writer = new BinaryWriter(reply);
            bool exit = false;
            try
            {
                using var reader = new BinaryReader(new MemoryStream(request));
                var op = (HostOp)reader.ReadByte();
                writer.Write(HostProtocol.Ok);
                exit = op == HostOp.Exit;
                Answer(op, reader, writer);
            }
            catch (HostFailure failure)
            {
                reply.SetLength(0);
                writer.Write(HostProtocol.Failed);
                writer.Write(failure.Code);
                HostProtocol.WriteString(writer, failure.Message);
            }
            catch (Exception fault) when (fault is IOException or ArgumentException or InvalidDataException
                or UnauthorizedAccessException or EndOfStreamException or NotSupportedException or OutOfMemoryException)
            {
                reply.SetLength(0);
                writer.Write(HostProtocol.Failed);
                writer.Write(fault.HResult);
                HostProtocol.WriteString(writer, fault.Message);
            }

            writer.Flush();
            HostProtocol.WriteFrame(pipe, reply.GetBuffer().AsSpan(0, (int)reply.Length));
            if (exit)
            {
                return;
            }
        }
    }

    private void Answer(HostOp op, BinaryReader reader, BinaryWriter writer)
    {
        switch (op)
        {
            case HostOp.Hello:
                writer.Write(HostProtocol.Version);
                writer.Write(Environment.ProcessId);
                break;
            case HostOp.Load:
                writer.Write((long)Load(HostProtocol.ReadString(reader)!));
                break;
            case HostOp.Free:
                NativeMethods.FreeLibrary((nint)reader.ReadInt64());
                break;
            case HostOp.Symbol:
            {
                nint module = (nint)reader.ReadInt64();
                string name = HostProtocol.ReadString(reader)!;
                nint address = NativeMethods.GetProcAddress(module, name);
                if (address == 0)
                {
                    throw HostFailure.LastError();
                }

                writer.Write((long)address);
                break;
            }

            case HostOp.Alloc:
                writer.Write((long)NativeMemory.AllocZeroed((nuint)Math.Max(reader.ReadInt64(), 1)));
                break;
            case HostOp.Release:
                NativeMemory.Free((void*)reader.ReadInt64());
                break;
            case HostOp.Read:
            {
                var source = (byte*)reader.ReadInt64();
                long count = reader.ReadInt64();
                bool viaArena = reader.ReadBoolean();
                if (viaArena)
                {
                    RequireArena(count);
                    Buffer.MemoryCopy(source, _arenaBase, _arenaSize, count);
                }
                else
                {
                    writer.Write(new ReadOnlySpan<byte>(source, checked((int)count)));
                }

                break;
            }

            case HostOp.Write:
            {
                var target = (byte*)reader.ReadInt64();
                long count = reader.ReadInt64();
                bool viaArena = reader.ReadBoolean();
                if (viaArena)
                {
                    RequireArena(count);
                    Buffer.MemoryCopy(_arenaBase, target, count, count);
                }
                else
                {
                    reader.ReadBytes(checked((int)count)).CopyTo(new Span<byte>(target, (int)count));
                }

                break;
            }

            case HostOp.Call:
            {
                nint function = (nint)reader.ReadInt64();
                Slot result = HostProtocol.ReadSlot(reader);
                int count = reader.ReadInt32();
                var parameters = new Slot[count];
                int bytes = 0;
                for (int i = 0; i < count; i++)
                {
                    parameters[i] = HostProtocol.ReadSlot(reader);
                    bytes += parameters[i].WireSize;
                }

                byte[] arguments = reader.ReadBytes(bytes);
                writer.Write(CallCompiler.Call(function, result, parameters, arguments));
                break;
            }

            case HostOp.SetEnvironment:
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string name = HostProtocol.ReadString(reader)!;
                    string? value = HostProtocol.ReadString(reader);
                    if (string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase))
                    {
                        _pathFromJGraph = value;
                        ApplyPath();
                    }
                    else
                    {
                        Environment.SetEnvironmentVariable(name, value);
                    }
                }

                break;
            }

            case HostOp.SetFolder:
                Environment.CurrentDirectory = HostProtocol.ReadString(reader)!;
                break;
            case HostOp.StringLength:
            {
                var text = (byte*)reader.ReadInt64();
                long length = 0;
                while (text[length] != 0)
                {
                    length++;
                }

                writer.Write(length);
                break;
            }

            case HostOp.MapArena:
                MapArena(HostProtocol.ReadString(reader)!, reader.ReadInt64());
                break;
            case HostOp.Exit:
                break;
            default:
                throw new InvalidDataException($"There is no native host request {(byte)op}.");
        }
    }

    /// <summary>
    /// Loads a library. A path loads with its own folder searched for the libraries it depends on,
    /// and that folder joins <c>PATH</c> for any it loads later; a bare name takes the system search.
    /// </summary>
    private nint Load(string path)
    {
        bool rooted = Path.IsPathRooted(path);
        nint module = NativeMethods.LoadLibraryEx(path, 0, rooted ? NativeMethods.LoadWithAlteredSearchPath : 0);
        if (module == 0)
        {
            throw HostFailure.LastError();
        }

        if (rooted && Path.GetDirectoryName(path) is { Length: > 0 } folder
            && !_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _libraryFolders.Add(folder);
            ApplyPath();
        }

        return module;
    }

    /// <summary><c>PATH</c> is JGraph's, as last synced, and then every loaded library's folder.</summary>
    private void ApplyPath()
    {
        string joined = string.Join(';', new[] { _pathFromJGraph ?? string.Empty }.Concat(_libraryFolders).Where(static p => p.Length > 0));
        Environment.SetEnvironmentVariable("PATH", joined.Length == 0 ? null : joined);
    }

    private void MapArena(string name, long size)
    {
        _arenaView?.SafeMemoryMappedViewHandle.ReleasePointer();
        _arenaView?.Dispose();
        _arena?.Dispose();
        _arena = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        _arenaView = _arena.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
        byte* pointer = null;
        _arenaView.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _arenaBase = pointer + _arenaView.PointerOffset;
        _arenaSize = size;
    }

    private void RequireArena(long count)
    {
        if (_arenaBase is null || count > _arenaSize)
        {
            throw new InvalidDataException($"The shared arena ({_arenaSize} bytes) cannot carry {count} bytes.");
        }
    }
}

/// <summary>A failure the host reports with a Win32 code and the system's text for it.</summary>
internal sealed class HostFailure(int code, string message) : Exception(message)
{
    /// <summary>The Win32 error code.</summary>
    public int Code { get; } = code;

    /// <summary>The failure of the Win32 call that just failed.</summary>
    public static HostFailure LastError()
    {
        int code = Marshal.GetLastPInvokeError();
        return new HostFailure(code, Marshal.GetPInvokeErrorMessage(code).Trim());
    }
}
