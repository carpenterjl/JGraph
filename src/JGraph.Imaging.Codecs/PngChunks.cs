using System.Buffers.Binary;
using System.Text;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// The two things everything that reads or writes a PNG chunk needs: the file's signature, and the
/// check that covers a chunk's name and payload. Shared by <see cref="AnimatedPngEncoder"/>, which
/// writes chunks, and <see cref="AnimatedPngReader"/>, which rebuilds them.
/// </summary>
internal static class PngChunks
{
    /// <summary>The eight bytes every PNG file opens with.</summary>
    internal static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] Table = BuildTable();

    /// <summary>The CRC-32 a chunk carries: over its four-byte name, then its payload.</summary>
    internal static uint Check(ReadOnlySpan<byte> name, ReadOnlySpan<byte> payload) =>
        Accumulate(Accumulate(0xFFFF_FFFFu, name), payload) ^ 0xFFFF_FFFFu;

    /// <summary>Folds more bytes into a running check. The seed is <c>0xFFFFFFFF</c>.</summary>
    internal static uint Accumulate(uint running, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            running = Table[(running ^ b) & 0xFF] ^ (running >> 8);
        }

        return running;
    }

    /// <summary>Writes one whole chunk — length, name, payload, check — to <paramref name="to"/>.</summary>
    internal static void Write(Stream to, string name, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)payload.Length);
        to.Write(length);

        Span<byte> tag = stackalloc byte[4];
        Encoding.ASCII.GetBytes(name, tag);
        to.Write(tag);
        to.Write(payload);

        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, Check(tag, payload));
        to.Write(check);
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
