using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// An APNG muxer: a PNG file whose frames are animated, and the only container here that carries a
/// full alpha channel from the first frame to the last.
/// </summary>
/// <remarks>
/// <para>
/// The thing this is built on is that APNG is not a new image format — it is an ordinary PNG with
/// three extra chunk types (<c>acTL</c>, <c>fcTL</c>, <c>fdAT</c>) threaded between the ones a
/// decoder already knows. So each frame is handed to Skia's own PNG encoder and what comes back is
/// taken apart: its <c>IHDR</c> settles the file's header once, and its compressed <c>IDAT</c>
/// payload is re-labelled as this frame's data. Nothing here compresses anything itself, which is
/// why a frame matches a still exported by the same renderer byte for byte.
/// </para>
/// <para>
/// A decoder that has never heard of APNG sees a plain PNG holding the first frame and ignores the
/// rest. That is why the format was designed this way, and why the first frame's data is written as
/// <c>IDAT</c> while every later one is <c>fdAT</c>.
/// </para>
/// <para>
/// Like the AVI muxer this writes forwards and patches backwards: the frame count in <c>acTL</c>
/// cannot be known until the last frame has arrived, so it is written as a placeholder and seeked
/// back to in <see cref="Close"/>. Nothing is buffered to learn it.
/// </para>
/// </remarks>
internal sealed class AnimatedPngEncoder : IVideoEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly FileStream _file;
    private readonly int _width;
    private readonly int _height;
    private readonly ushort _delayNumerator;
    private readonly ushort _delayDenominator;
    private readonly byte[] _scratch;

    private long _frameCountAt = -1;
    private uint _sequence;
    private int _frames;
    private bool _closed;

    /// <summary>Creates the file. The header is written with the first frame, which carries it.</summary>
    internal AnimatedPngEncoder(string path, int width, int height, double frameRate)
    {
        _width = width;
        _height = height;
        _scratch = new byte[(long)width * height * 4];

        // A frame's delay is a rational number of seconds. Thousandths hold every rate a script is
        // likely to ask for, and a fixed denominator keeps the arithmetic to one rounding.
        _delayDenominator = 1000;
        _delayNumerator = (ushort)Math.Clamp((int)Math.Round(1000.0 / frameRate), 1, ushort.MaxValue);

        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    /// <inheritdoc />
    public VideoSampleLayout Layout => VideoSampleLayout.Rgba32;

    /// <inheritdoc />
    public int FrameCount => _frames;

    /// <inheritdoc />
    public void WriteFrame(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (samples.Length != _scratch.Length)
        {
            throw new ArgumentException(
                $"a frame is {_scratch.Length} samples ({_height} by {_width}), but {samples.Length} were given.",
                nameof(samples));
        }

        byte[] png = EncodePng(samples);
        (byte[] header, byte[] data) = SplitPng(png);

        if (_frames == 0)
        {
            _file.Write(Signature);
            WriteChunk("IHDR", header);

            // acTL has to precede the first IDAT, and its frame count is the number this loop has
            // not finished counting — so it is a placeholder, patched when the file is closed.
            var control = new byte[8];
            _frameCountAt = _file.Position + 8;
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4), 0); // play forever
            WriteChunk("acTL", control);
        }

        WriteFrameControl();

        if (_frames == 0)
        {
            WriteChunk("IDAT", data);
        }
        else
        {
            // fdAT is IDAT with a sequence number in front of it, and that number shares one counter
            // with fcTL — so a frame after the first spends two.
            var payload = new byte[4 + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0), _sequence++);
            data.CopyTo(payload.AsSpan(4));
            WriteChunk("fdAT", payload);
        }

        _frames++;
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            if (_frames > 0)
            {
                WriteChunk("IEND", []);

                // The frame count, and the check over the chunk holding it, are known only now.
                _file.Position = _frameCountAt;
                Span<byte> count = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(count, (uint)_frames);
                _file.Write(count);
                PatchAnimationControlCrc();
            }

            _file.Flush();
        }
        finally
        {
            _file.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    /// <summary>The frame's placement and timing. Every frame here is the whole canvas.</summary>
    private void WriteFrameControl()
    {
        var fc = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(fc.AsSpan(0), _sequence++);
        BinaryPrimitives.WriteUInt32BigEndian(fc.AsSpan(4), (uint)_width);
        BinaryPrimitives.WriteUInt32BigEndian(fc.AsSpan(8), (uint)_height);
        BinaryPrimitives.WriteUInt32BigEndian(fc.AsSpan(12), 0); // x offset
        BinaryPrimitives.WriteUInt32BigEndian(fc.AsSpan(16), 0); // y offset
        BinaryPrimitives.WriteUInt16BigEndian(fc.AsSpan(20), _delayNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(fc.AsSpan(22), _delayDenominator);

        // APNG_DISPOSE_OP_NONE and APNG_BLEND_OP_SOURCE: every frame is whole and replaces the one
        // before it outright. Blending would composite a transparent pixel over an opaque one and
        // leave the previous frame showing through the hole this one meant to cut.
        fc[24] = 0;
        fc[25] = 0;
        WriteChunk("fcTL", fc);
    }

    /// <summary>One frame as an ordinary PNG, straight from the encoder the stills go through.</summary>
    private byte[] EncodePng(ReadOnlySpan<byte> rgba)
    {
        var info = new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        // Unpremultiplied and copied straight in: the caller's samples are already the colours that
        // were drawn beside the coverage they were drawn with, which is what PNG stores.
        rgba.CopyTo(_scratch);
        Marshal.Copy(_scratch, 0, bitmap.GetPixels(), _scratch.Length);
        bitmap.NotifyPixelsChanged();

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("failed to encode a video frame as PNG.");
        return data.ToArray();
    }

    /// <summary>
    /// The two things wanted out of an encoded PNG: its <c>IHDR</c> payload, which settles the size
    /// and colour type of the whole file, and every <c>IDAT</c> payload joined end to end, which is
    /// one zlib stream however many chunks the encoder chose to cut it into.
    /// </summary>
    private static (byte[] Header, byte[] Data) SplitPng(byte[] png)
    {
        byte[]? header = null;
        using var data = new MemoryStream();

        int at = Signature.Length;
        while (at + 8 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            string name = Encoding.ASCII.GetString(png, at + 4, 4);
            int payload = at + 8;
            if (name == "IHDR")
            {
                header = png.AsSpan(payload, length).ToArray();
            }
            else if (name == "IDAT")
            {
                data.Write(png, payload, length);
            }
            else if (name == "IEND")
            {
                break;
            }

            at = payload + length + 4;
        }

        return (header ?? throw new InvalidDataException("an encoded frame has no PNG header."),
            data.ToArray());
    }

    private void WriteChunk(string name, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)payload.Length);
        _file.Write(length);

        Span<byte> tag = stackalloc byte[4];
        Encoding.ASCII.GetBytes(name, tag);
        _file.Write(tag);
        _file.Write(payload);

        // A PNG chunk's check covers its name and its payload, and not its length.
        uint crc = Crc(Crc(0xFFFF_FFFFu, tag), payload) ^ 0xFFFF_FFFFu;
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        _file.Write(check);
    }

    /// <summary>Recomputes <c>acTL</c>'s check once the frame count inside it is the real one.</summary>
    private void PatchAnimationControlCrc()
    {
        // The stream is write-only, so the covered bytes are rebuilt rather than read back: four of
        // name, then the payload whose first word has just been settled.
        var chunk = new byte[12];
        Encoding.ASCII.GetBytes("acTL", chunk.AsSpan(0));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)_frames);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), 0);

        _file.Position = _frameCountAt + 8;
        uint crc = Crc(0xFFFF_FFFFu, chunk) ^ 0xFFFF_FFFFu;
        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        _file.Write(check);
        _file.Position = _file.Length;
    }

    private static uint Crc(uint running, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            running = CrcTable[(running ^ b) & 0xFF] ^ (running >> 8);
        }

        return running;
    }

    private static uint[] BuildCrcTable()
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
