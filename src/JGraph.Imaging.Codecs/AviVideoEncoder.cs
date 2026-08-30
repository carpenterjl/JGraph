using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// An AVI muxer: RIFF chunks around either whole JPEGs (Motion JPEG) or raw DIB rows (uncompressed
/// RGB24 and the two 8-bit palette forms).
/// </summary>
/// <remarks>
/// <para>
/// AVI is written forwards and patched backwards. Four numbers cannot be known until the last frame
/// has been written — the file's length, the <c>movi</c> list's length, the frame count and the
/// largest frame — so each is written as a placeholder and seeked back to in <see cref="Close"/>.
/// That is what keeps the encoder streaming: nothing is buffered to learn a size that the file
/// itself can be asked for later.
/// </para>
/// <para>
/// The one thing worth knowing about AVI's layout is that a DIB is stored bottom-up with every row
/// padded to a multiple of four bytes, while a JPEG frame is stored exactly as the encoder produced
/// it. Both are true of the same container, which is why the row-packing lives behind the
/// compression choice rather than in the writer.
/// </para>
/// </remarks>
internal sealed class AviVideoEncoder : IVideoEncoder
{
    /// <summary>The AVI header's "this file has an index" flag.</summary>
    private const uint AviHasIndex = 0x0000_0010;

    /// <summary>The index entry flag marking a frame that can be decoded on its own.</summary>
    private const uint AviIndexKeyFrame = 0x0000_0010;

    private readonly FileStream _file;
    private readonly VideoCodec _codec;
    private readonly int _width;
    private readonly int _height;
    private readonly double _frameRate;
    private readonly int _quality;
    private readonly bool _indexed;
    private readonly int _bytesPerPixel;
    private readonly int _stride;
    private readonly string _frameChunkId;
    private readonly List<(uint Offset, uint Length)> _index = new();

    private long _riffSizeAt;
    private long _totalFramesAt;
    private long _maxBytesPerSecAt;
    private long _suggestedBufferAt;
    private long _streamLengthAt;
    private long _streamBufferAt;
    private long _moviSizeAt;
    private long _moviFourCcAt;
    private byte[]? _row;
    private uint _largestFrame;
    private bool _closed;

    /// <summary>Creates the file and writes everything up to the first frame.</summary>
    internal AviVideoEncoder(
        string path,
        VideoCodec codec,
        int width,
        int height,
        double frameRate,
        int quality,
        ReadOnlySpan<byte> palette)
    {
        _codec = codec;
        _width = width;
        _height = height;
        _frameRate = frameRate;
        _quality = quality;
        _indexed = VideoEncoder.IsIndexed(codec);

        // A DIB row is padded to a four-byte boundary; a JPEG frame has no rows to pad. A 32-bit
        // row is already on one, which is the quiet reason the alpha form costs nothing to store.
        _bytesPerPixel = _indexed ? 1 : codec == VideoCodec.UncompressedAvi32 ? 4 : 3;
        _stride = (((width * _bytesPerPixel) + 3) / 4) * 4;

        // 'dc' is a compressed frame and 'db' an uncompressed one. Players accept either for both,
        // but naming them honestly is what lets a stream dump say what it is looking at.
        _frameChunkId = codec == VideoCodec.MotionJpegAvi ? "00dc" : "00db";

        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeaders(palette);
    }

    /// <inheritdoc />
    public VideoSampleLayout Layout => _bytesPerPixel switch
    {
        1 => VideoSampleLayout.Indexed8,
        4 => VideoSampleLayout.Rgba32,
        _ => VideoSampleLayout.Rgb24,
    };

    /// <inheritdoc />
    public int FrameCount => _index.Count;

    /// <inheritdoc />
    public void WriteFrame(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        int wanted = _width * _height * _bytesPerPixel;
        if (samples.Length != wanted)
        {
            throw new ArgumentException(
                $"a frame is {wanted} samples ({_height} by {_width}), but {samples.Length} were given.",
                nameof(samples));
        }

        // The offset an index entry carries is measured from the 'movi' four-character code, so the
        // first frame's is 4 — the four bytes of that code itself.
        long chunkAt = _file.Position;
        WriteFourCc(_frameChunkId);
        long sizeAt = _file.Position;
        WriteUInt32(0);

        uint written = _codec == VideoCodec.MotionJpegAvi
            ? WriteJpegFrame(samples)
            : WriteDibFrame(samples);

        // Every RIFF chunk starts on an even byte.
        if ((written & 1) != 0)
        {
            _file.WriteByte(0);
        }

        long after = _file.Position;
        _file.Position = sizeAt;
        WriteUInt32(written);
        _file.Position = after;

        _index.Add(((uint)(chunkAt - _moviFourCcAt), written));
        _largestFrame = Math.Max(_largestFrame, written);
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
            WriteIndex();
            PatchSizes();
            _file.Flush();
        }
        finally
        {
            _file.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    /// <summary>
    /// The fixed part of the file: the RIFF wrapper, the main header, the one video stream's header
    /// and format, and the opening of the <c>movi</c> list the frames go into.
    /// </summary>
    private void WriteHeaders(ReadOnlySpan<byte> palette)
    {
        int paletteEntries = _indexed ? 256 : 0;
        int formatBytes = 40 + (paletteEntries * 4);

        // Both list lengths are known up front, because everything inside them is fixed-size. They
        // are summed a term at a time rather than as one constant: a list's length counts its own
        // four-character code and every child's eight-byte header, and leaving out one of those is
        // a file that opens, looks right, and cannot be read.
        int strlBytes = 4              // 'strl'
            + 8 + 56                   // strh
            + 8 + formatBytes;         // strf
        int hdrlBytes = 4              // 'hdrl'
            + 8 + 56                   // avih
            + 8 + strlBytes;           // LIST 'strl'

        WriteFourCc("RIFF");
        _riffSizeAt = _file.Position;
        WriteUInt32(0);
        WriteFourCc("AVI ");

        WriteFourCc("LIST");
        WriteUInt32((uint)hdrlBytes);
        WriteFourCc("hdrl");

        // avih: the main header.
        WriteFourCc("avih");
        WriteUInt32(56);
        WriteUInt32((uint)Math.Round(1_000_000.0 / _frameRate)); // microseconds per frame
        _maxBytesPerSecAt = _file.Position;
        WriteUInt32(0);
        WriteUInt32(0); // padding granularity
        WriteUInt32(AviHasIndex);
        _totalFramesAt = _file.Position;
        WriteUInt32(0);
        WriteUInt32(0); // initial frames
        WriteUInt32(1); // one stream
        _suggestedBufferAt = _file.Position;
        WriteUInt32(0);
        WriteUInt32((uint)_width);
        WriteUInt32((uint)_height);
        for (int i = 0; i < 4; i++)
        {
            WriteUInt32(0); // reserved
        }

        // LIST 'strl' — the single video stream.
        WriteFourCc("LIST");
        WriteUInt32((uint)strlBytes);
        WriteFourCc("strl");

        WriteFourCc("strh");
        WriteUInt32(56);
        WriteFourCc("vids");
        WriteFourCc(_codec == VideoCodec.MotionJpegAvi ? "MJPG" : "DIB ");
        WriteUInt32(0); // flags
        WriteUInt16(0); // priority
        WriteUInt16(0); // language
        WriteUInt32(0); // initial frames

        // The rate is a ratio, so a rate like 29.97 stays exact instead of being rounded to 30.
        (uint scale, uint rate) = RateRatio(_frameRate);
        WriteUInt32(scale);
        WriteUInt32(rate);
        WriteUInt32(0); // start
        _streamLengthAt = _file.Position;
        WriteUInt32(0);
        _streamBufferAt = _file.Position;
        WriteUInt32(0);
        WriteUInt32(uint.MaxValue); // quality: leave it to the codec
        WriteUInt32(0); // sample size — 0 for video
        WriteUInt16(0);
        WriteUInt16(0);
        WriteUInt16((ushort)_width);
        WriteUInt16((ushort)_height);

        // strf: a BITMAPINFOHEADER, and for the 8-bit forms the palette that reads it.
        WriteFourCc("strf");
        WriteUInt32((uint)formatBytes);
        WriteUInt32(40);
        WriteUInt32((uint)_width);
        WriteUInt32((uint)_height);
        WriteUInt16(1); // planes
        WriteUInt16((ushort)(_bytesPerPixel * 8));
        if (_codec == VideoCodec.MotionJpegAvi)
        {
            WriteFourCc("MJPG");
        }
        else
        {
            WriteUInt32(0); // BI_RGB
        }

        WriteUInt32((uint)(_stride * _height));
        WriteUInt32(0); // horizontal resolution
        WriteUInt32(0); // vertical resolution
        WriteUInt32((uint)paletteEntries);
        WriteUInt32((uint)paletteEntries);
        if (_indexed)
        {
            WritePalette(palette);
        }

        // LIST 'movi' — everything after this is frames.
        WriteFourCc("LIST");
        _moviSizeAt = _file.Position;
        WriteUInt32(0);
        _moviFourCcAt = _file.Position;
        WriteFourCc("movi");
    }

    /// <summary>
    /// The 256 palette entries, stored as Windows wants them — blue, green, red, and a reserved byte.
    /// A codec that needs one and was given none gets the grey ramp, which is what makes the
    /// grayscale profile a palette profile with nothing to declare.
    /// </summary>
    private void WritePalette(ReadOnlySpan<byte> palette)
    {
        for (int i = 0; i < 256; i++)
        {
            byte r, g, b;
            if (palette.Length >= (i + 1) * 3)
            {
                r = palette[i * 3];
                g = palette[(i * 3) + 1];
                b = palette[(i * 3) + 2];
            }
            else
            {
                r = g = b = (byte)i;
            }

            _file.WriteByte(b);
            _file.WriteByte(g);
            _file.WriteByte(r);
            _file.WriteByte(0);
        }
    }

    /// <summary>Writes one frame as a whole JPEG and answers how many bytes it took.</summary>
    private uint WriteJpegFrame(ReadOnlySpan<byte> rgb)
    {
        var info = new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        var colors = new SKColor[_width * _height];
        for (int i = 0; i < colors.Length; i++)
        {
            int at = i * 3;
            colors[i] = new SKColor(rgb[at], rgb[at + 1], rgb[at + 2], byte.MaxValue);
        }

        bitmap.Pixels = colors;
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData? data = image.Encode(SKEncodedImageFormat.Jpeg, _quality)
            ?? throw new InvalidDataException("failed to encode a video frame as JPEG.");

        data.SaveTo(_file);
        return (uint)data.Size;
    }

    /// <summary>
    /// Writes one frame as DIB rows — bottom-up, each padded to four bytes — and answers how many
    /// bytes it took. The caller's samples are top-down, which is why the loop counts backwards.
    /// </summary>
    private uint WriteDibFrame(ReadOnlySpan<byte> samples)
    {
        byte[] row = _row ??= new byte[_stride];
        for (int y = _height - 1; y >= 0; y--)
        {
            Array.Clear(row);
            ReadOnlySpan<byte> source =
                samples.Slice(y * _width * _bytesPerPixel, _width * _bytesPerPixel);
            if (_indexed)
            {
                source.CopyTo(row);
            }
            else
            {
                // A DIB pixel is blue, green, red — the reverse of the caller's — and a 32-bit one
                // then carries the coverage byte through untouched where the padding used to sit.
                for (int x = 0; x < _width; x++)
                {
                    int at = x * _bytesPerPixel;
                    row[at] = source[at + 2];
                    row[at + 1] = source[at + 1];
                    row[at + 2] = source[at];
                    if (_bytesPerPixel == 4)
                    {
                        row[at + 3] = source[at + 3];
                    }
                }
            }

            _file.Write(row, 0, _stride);
        }

        return (uint)(_stride * _height);
    }

    /// <summary>Writes the <c>idx1</c> table: where every frame is and how long it is.</summary>
    private void WriteIndex()
    {
        WriteFourCc("idx1");
        WriteUInt32((uint)(_index.Count * 16));
        foreach ((uint offset, uint length) in _index)
        {
            WriteFourCc(_frameChunkId);
            WriteUInt32(AviIndexKeyFrame);
            WriteUInt32(offset);
            WriteUInt32(length);
        }
    }

    /// <summary>Seeks back and fills in every size that could only be known at the end.</summary>
    private void PatchSizes()
    {
        long end = _file.Position;
        uint frames = (uint)_index.Count;

        // The 'movi' list runs from its four-character code to the start of the index.
        long moviEnd = end - 8 - (_index.Count * 16);
        Patch(_moviSizeAt, (uint)(moviEnd - _moviFourCcAt));
        Patch(_riffSizeAt, (uint)(end - _riffSizeAt - 4));
        Patch(_totalFramesAt, frames);
        Patch(_streamLengthAt, frames);
        Patch(_suggestedBufferAt, _largestFrame);
        Patch(_streamBufferAt, _largestFrame);
        Patch(_maxBytesPerSecAt, (uint)Math.Min(uint.MaxValue, Math.Round(_largestFrame * _frameRate)));
        _file.Position = end;
    }

    private void Patch(long at, uint value)
    {
        _file.Position = at;
        WriteUInt32(value);
    }

    /// <summary>
    /// A frame rate as the ratio AVI stores it. A whole number is written as it stands; anything else
    /// is scaled by a thousand, which holds every rate a script is likely to ask for — 29.97 and
    /// 23.976 included — without rounding them to the nearest integer.
    /// </summary>
    private static (uint Scale, uint Rate) RateRatio(double frameRate)
    {
        if (Math.Abs(frameRate - Math.Round(frameRate)) < 1e-9 && frameRate <= uint.MaxValue)
        {
            return (1u, (uint)Math.Round(frameRate));
        }

        double scaled = frameRate * 1000.0;
        return scaled <= uint.MaxValue
            ? (1000u, (uint)Math.Round(scaled))
            : (1u, (uint)Math.Round(frameRate));
    }

    private void WriteFourCc(string code)
    {
        Span<byte> bytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(code.AsSpan(), bytes);
        _file.Write(bytes);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _file.Write(bytes);
    }

    private void WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _file.Write(bytes);
    }
}
