using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace JGraph.NativeHost;

/// <summary>
/// The wire between JGraph and its native host (ADR 0180). Every message is a frame: a four-byte
/// little-endian length and that many bytes. A request starts with its <see cref="HostOp"/>; a reply
/// starts with a status byte, 0 for success and 1 for a failure that carries a Win32 error code and
/// the system's text for it. The host answers requests one at a time, in order.
/// </summary>
public static class HostProtocol
{
    /// <summary>The version both ends must agree on; the host answers it to <see cref="HostOp.Hello"/>.</summary>
    public const int Version = 1;

    /// <summary>Reads and writes at least this large travel through the shared arena, not the pipe.</summary>
    public const int ArenaThreshold = 64 * 1024;

    /// <summary>The largest frame either end accepts; anything bigger goes through the arena.</summary>
    public const int MaxFrame = 16 * 1024 * 1024;

    /// <summary>A reply's status byte for success.</summary>
    public const byte Ok = 0;

    /// <summary>A reply's status byte for a failure (a Win32 code and a message follow).</summary>
    public const byte Failed = 1;

    /// <summary>
    /// Writes one frame, its length and its bytes in a single write: two writes wake the reader twice,
    /// and a wake is most of what a round trip costs.
    /// </summary>
    public static void WriteFrame(Stream stream, ReadOnlySpan<byte> payload)
    {
        byte[] frame = System.Buffers.ArrayPool<byte>.Shared.Rent(payload.Length + 4);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
            payload.CopyTo(frame.AsSpan(4));
            stream.Write(frame, 0, payload.Length + 4);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(frame);
        }
    }

    /// <summary>Reads one frame, or null when the other end has gone.</summary>
    public static byte[]? ReadFrame(Stream stream)
    {
        Span<byte> length = stackalloc byte[4];
        if (!Fill(stream, length))
        {
            return null;
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size < 0 || size > MaxFrame)
        {
            throw new InvalidDataException($"A native host frame of {size} bytes is not one the protocol makes.");
        }

        byte[] payload = new byte[size];
        return Fill(stream, payload) ? payload : null;
    }

    private static bool Fill(Stream stream, Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            int read = stream.Read(buffer);
            if (read == 0)
            {
                return false;
            }

            buffer = buffer[read..];
        }

        return true;
    }

    /// <summary>Writes a length-prefixed UTF-8 string, or a length of -1 for null.</summary>
    public static void WriteString(BinaryWriter writer, string? text)
    {
        if (text is null)
        {
            writer.Write(-1);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>Reads what <see cref="WriteString"/> wrote.</summary>
    public static string? ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return length < 0 ? null : Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    /// <summary>Writes a slot: its kind, and a struct's size.</summary>
    public static void WriteSlot(BinaryWriter writer, Slot slot)
    {
        writer.Write((byte)slot.Kind);
        if (slot.Kind == SlotKind.Struct)
        {
            writer.Write(slot.Size);
        }
    }

    /// <summary>Reads what <see cref="WriteSlot"/> wrote.</summary>
    public static Slot ReadSlot(BinaryReader reader)
    {
        var kind = (SlotKind)reader.ReadByte();
        return kind == SlotKind.Struct ? new Slot(kind, reader.ReadInt32()) : new Slot(kind);
    }
}

/// <summary>What a request asks of the host.</summary>
public enum HostOp : byte
{
    /// <summary>Answers the protocol version and the host's process id.</summary>
    Hello = 1,

    /// <summary>Loads a library (a path or a bare name for the system search); answers its module handle.</summary>
    Load,

    /// <summary>Frees a module handle.</summary>
    Free,

    /// <summary>Resolves an export by name; answers its address.</summary>
    Symbol,

    /// <summary>Allocates zeroed memory; answers its address.</summary>
    Alloc,

    /// <summary>Frees memory <see cref="Alloc"/> answered.</summary>
    Release,

    /// <summary>Answers bytes read from an address (through the arena when large).</summary>
    Read,

    /// <summary>Writes bytes to an address (from the arena when large).</summary>
    Write,

    /// <summary>Calls a function pointer with a signature of slots and the arguments' bytes.</summary>
    Call,

    /// <summary>Sets or removes environment variables.</summary>
    SetEnvironment,

    /// <summary>Moves the host's working folder.</summary>
    SetFolder,

    /// <summary>Answers the length of the NUL-terminated byte string at an address.</summary>
    StringLength,

    /// <summary>Opens the shared arena JGraph created, by name and size.</summary>
    MapArena,

    /// <summary>Ends the host.</summary>
    Exit,
}

/// <summary>
/// One argument or return position in a call's signature. Every C type JGraph knows lowers to one of
/// these: integers by width and sign, the two floats, a pointer, or a struct passed by value as its
/// bytes (which the host places by the x64 rule: in a register when 1, 2, 4 or 8 bytes long, by a
/// pointer to a copy otherwise).
/// </summary>
public enum SlotKind : byte
{
    /// <summary>No value (a return position only).</summary>
    Void,

    /// <summary>A signed 8-bit integer.</summary>
    Int8,

    /// <summary>An unsigned 8-bit integer (also C's <c>bool</c>).</summary>
    UInt8,

    /// <summary>A signed 16-bit integer.</summary>
    Int16,

    /// <summary>An unsigned 16-bit integer.</summary>
    UInt16,

    /// <summary>A signed 32-bit integer (also Windows' <c>long</c>).</summary>
    Int32,

    /// <summary>An unsigned 32-bit integer.</summary>
    UInt32,

    /// <summary>A signed 64-bit integer.</summary>
    Int64,

    /// <summary>An unsigned 64-bit integer (also <c>size_t</c>).</summary>
    UInt64,

    /// <summary>A 32-bit float.</summary>
    Single,

    /// <summary>A 64-bit float.</summary>
    Double,

    /// <summary>An address.</summary>
    Pointer,

    /// <summary>A struct by value, <see cref="Slot.Size"/> bytes long.</summary>
    Struct,
}

/// <summary>A signature position: a kind, and a struct's size in bytes.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Size">The struct's size, for <see cref="SlotKind.Struct"/> only.</param>
public readonly record struct Slot(SlotKind Kind, int Size = 0)
{
    /// <summary>The bytes an argument of this slot takes in a call request: 8, or a struct's size rounded up to 8.</summary>
    public int WireSize => Kind == SlotKind.Struct ? (Size + 7) & ~7 : 8;

    /// <summary>The bytes a reply carries for a return of this slot: none, 8, or a struct's size.</summary>
    public int ReturnSize => Kind switch
    {
        SlotKind.Void => 0,
        SlotKind.Struct => Size,
        _ => 8,
    };
}
