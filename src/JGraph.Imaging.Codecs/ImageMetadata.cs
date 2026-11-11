using SkiaSharp;
using System.Text;

namespace JGraph.Imaging.Codecs;

public static partial class ImageCodec
{
    /// <summary>Raw bytes a strip of rows holds before it is compressed: a strip is whole rows.</summary>
    private const int PngStripBytes = 1 << 17;

    /// <summary>
    /// Writes an 8-bit PNG (the every-row plan's I9, ADR 0330). The rows are cut into strips of
    /// about <see cref="PngStripBytes"/> raw bytes; each strip is quantised and deflated on its
    /// own thread and ends on a byte boundary (a sync flush), so the strips laid end to end are
    /// one deflate stream, as pigz makes one. Each strip is its own IDAT chunk. The pixels a
    /// reader gets are the ones the single-stream writer gave; the bytes of the file are not.
    /// </summary>
    private static void Write8BitPng(string path, ImageBuffer image, ImageBuffer? alpha)
    {
        using var file = File.Create(path);
        file.Write(PngChunks.Signature);
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;
        header[9] = image.Channels == 1 ? alpha is null ? (byte)0 : (byte)4 : alpha is null && image.Channels != 4 ? (byte)2 : (byte)6;
        PngChunks.Write(file, "IHDR", header);

        int width = image.Width;
        int height = image.Height;
        int channels = image.Channels;
        int colour = Math.Min(3, channels);
        bool coverage = alpha is not null || channels == 4;
        int stride = checked(1 + (width * (colour + (coverage ? 1 : 0))));
        int rowsPerStrip = Math.Clamp(PngStripBytes / stride, 1, height);
        int strips = ((height - 1) / rowsPerStrip) + 1;
        var compressed = new byte[strips][];
        var lengths = new int[strips];
        var sums = new uint[strips];
        var rawLengths = new int[strips];
        var checks = new uint[strips];

        JGraph.Numerics.ParallelKernels.ForBlocks(strips, strips > 1, strip =>
        {
            int r0 = strip * rowsPerStrip;
            int r1 = Math.Min(r0 + rowsPerStrip, height);
            int rawLength = (r1 - r0) * stride;
            byte[] raw = System.Buffers.ArrayPool<byte>.Shared.Rent(rawLength);
            try
            {
                ReadOnlySpan<double> pixels = image.Pixels;
                ReadOnlySpan<double> alphaPixels = alpha is null ? default : alpha.Pixels;
                int at = 0;
                for (int r = r0; r < r1; r++)
                {
                    raw[at++] = 0; // PNG filter None.
                    ReadOnlySpan<double> row = pixels.Slice(r * width * channels, width * channels);
                    if (channels == 1 && alpha is null)
                    {
                        for (int c = 0; c < row.Length; c++)
                        {
                            raw[at++] = ToByte(row[c]);
                        }

                        continue;
                    }

                    for (int c = 0; c < width; c++)
                    {
                        int b = c * channels;
                        for (int channel = 0; channel < colour; channel++)
                        {
                            raw[at++] = ToByte(row[b + channel]);
                        }

                        if (alpha is not null)
                        {
                            raw[at++] = ToByte(alphaPixels[(r * width) + c]);
                        }
                        else if (channels == 4)
                        {
                            raw[at++] = ToByte(row[b + 3]);
                        }
                    }
                }

                bool first = strip == 0;
                bool last = strip == strips - 1;
                using var sink = new MemoryStream((rawLength / 4) + 64);
                if (first)
                {
                    // The zlib header: deflate with a 32 KB window, default level, no dictionary.
                    sink.WriteByte(0x78);
                    sink.WriteByte(0x9C);
                }

                var deflate = new System.IO.Compression.DeflateStream(
                    sink, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);
                deflate.Write(raw, 0, rawLength);
                if (last)
                {
                    // Disposing ends the stream with its final block.
                    deflate.Dispose();
                    lengths[strip] = (int)sink.Length;
                }
                else
                {
                    // A sync flush ends this strip on a byte boundary with an empty stored block that
                    // is not the last; what disposing appends after it is a final block and is dropped.
                    deflate.Flush();
                    lengths[strip] = (int)sink.Length;
                    deflate.Dispose();
                }

                compressed[strip] = sink.GetBuffer();
                sums[strip] = Adler32(raw.AsSpan(0, rawLength));
                rawLengths[strip] = rawLength;
                if (!last)
                {
                    checks[strip] = PngChunks.Check("IDAT"u8, compressed[strip].AsSpan(0, lengths[strip]));
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(raw);
            }
        });
        GC.KeepAlive(image);
        GC.KeepAlive(alpha);

        uint sum = sums[0];
        for (int strip = 1; strip < strips; strip++)
        {
            sum = Adler32Combine(sum, sums[strip], rawLengths[strip]);
        }

        Span<byte> word = stackalloc byte[4];
        for (int strip = 0; strip < strips; strip++)
        {
            bool last = strip == strips - 1;
            ReadOnlySpan<byte> payload = compressed[strip].AsSpan(0, lengths[strip]);
            if (last)
            {
                // The zlib trailer rides on the last strip: the check over every raw byte.
                byte[] closing = new byte[payload.Length + 4];
                payload.CopyTo(closing);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(closing.AsSpan(payload.Length), sum);
                PngChunks.Write(file, "IDAT", closing);
                continue;
            }

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, (uint)payload.Length);
            file.Write(word);
            file.Write("IDAT"u8);
            file.Write(payload);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, checks[strip]);
            file.Write(word);
        }

        PngChunks.Write(file, "IEND", []);
    }

    private const uint AdlerBase = 65521;

    /// <summary>The Adler-32 check of <paramref name="bytes"/> on their own (seed 1), as zlib defines it.</summary>
    internal static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        uint a = 1;
        uint b = 0;
        while (bytes.Length > 0)
        {
            // 5552 is the most bytes the two sums can take before a 32-bit overflow.
            ReadOnlySpan<byte> run = bytes.Length > 5552 ? bytes[..5552] : bytes;
            foreach (byte value in run)
            {
                a += value;
                b += a;
            }

            a %= AdlerBase;
            b %= AdlerBase;
            bytes = bytes[run.Length..];
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// The Adler-32 of two runs laid end to end, from the check of each and the length of the
    /// second (zlib's <c>adler32_combine</c>).
    /// </summary>
    internal static uint Adler32Combine(uint first, uint second, long secondLength)
    {
        uint rem = (uint)(secondLength % AdlerBase);
        uint sum1 = first & 0xFFFF;
        uint sum2 = (rem * sum1) % AdlerBase;
        sum1 += (second & 0xFFFF) + AdlerBase - 1;
        sum2 += ((first >> 16) & 0xFFFF) + ((second >> 16) & 0xFFFF) + AdlerBase - rem;
        if (sum1 >= AdlerBase)
        {
            sum1 -= AdlerBase;
        }

        if (sum1 >= AdlerBase)
        {
            sum1 -= AdlerBase;
        }

        if (sum2 >= AdlerBase << 1)
        {
            sum2 -= AdlerBase << 1;
        }

        if (sum2 >= AdlerBase)
        {
            sum2 -= AdlerBase;
        }

        return sum1 | (sum2 << 16);
    }

    internal static int? EncodedChannels(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 25 && bytes.AsSpan(0, 8).SequenceEqual(PngChunks.Signature))
            return bytes[25] is 0 or 4 ? 1 : 3;
        if (bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216)
            return null;
        for (int i = 2; i + 3 < bytes.Length;)
        {
            if (bytes[i++] != 255)
                break;
            int marker = bytes[i++];
            if (marker is 218 or 217)
                break;
            int length = bytes[i] * 256 + bytes[i + 1];
            if (length < 2 || i + length > bytes.Length)
                break;
            if (marker is 192 or 193 or 194 && length >= 8)
                return bytes[i + 7] == 1 ? 1 : 3;
            i += length;
        }
        return null;
    }
    public static int FrameCount(string path)
    {
        if (TiffCodec.IsTiff(path))
            return TiffCodec.FrameCount(path);
        using SKData data = SKData.Create(path);
        using SKCodec? codec = SKCodec.Create(data);
        return Math.Max(1, codec?.FrameCount ?? 1);
    }
    /// <summary>Applies the encoded EXIF orientation, consuming the input only when replaced.</summary>
    public static ImageBuffer AutoOrient(string path, ImageBuffer source)
    {
        using SKData data = SKData.Create(path);
        using SKCodec? codec = SKCodec.Create(data);
        int orientation = codec is null ? 1 : (int)codec.EncodedOrigin;
        if (orientation <= 1 || orientation > 8)
            return source;
        bool transpose = orientation >= 5;
        var result = new ImageBuffer(transpose ? source.Width : source.Height, transpose ? source.Height : source.Width, source.Channels) { Class = source.Class };
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
        {
            (int sx, int sy) = orientation switch
            {
                2 => (source.Width - 1 - x, y),
                3 => (source.Width - 1 - x, source.Height - 1 - y),
                4 => (x, source.Height - 1 - y),
                5 => (y, x),
                6 => (y, source.Height - 1 - x),
                7 => (source.Width - 1 - y, source.Height - 1 - x),
                8 => (source.Width - 1 - y, x),
                _ => (x, y)
            };
            for (int c = 0; c < source.Channels; c++)
                result.Pixels[(y * result.Width + x) * source.Channels + c] = source.Pixels[(sy * source.Width + sx) * source.Channels + c];
        }
        source.Dispose();
        return result;
    }

    public static string[] JpegComments(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var comments = new List<string>();
        if (bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216)
            return [];
        for (int i = 2; i + 3 < bytes.Length;)
        {
            if (bytes[i++] != 255)
                break;
            int marker = bytes[i++];
            if (marker is 218 or 217)
                break;
            int length = bytes[i] * 256 + bytes[i + 1];
            if (length < 2 || i + length > bytes.Length)
                break;
            if (marker == 254)
                comments.Add(Encoding.UTF8.GetString(bytes, i + 2, length - 2));
            i += length;
        }
        return comments.ToArray();
    }

    public static void WriteJpegComments(string path, string[] comments)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216)
            throw new ArgumentException("Comment is supported for JPEG files.");
        using var result = new MemoryStream();
        result.Write(bytes, 0, 2);
        foreach (string comment in comments)
        {
            byte[] text = Encoding.UTF8.GetBytes(comment);
            if (text.Length > 65533)
                throw new ArgumentException("JPEG comment is too long.");
            int size = text.Length + 2;
            result.WriteByte(255);
            result.WriteByte(254);
            result.WriteByte((byte)(size >> 8));
            result.WriteByte((byte)size);
            result.Write(text);
        }
        result.Write(bytes, 2, bytes.Length - 2);
        File.WriteAllBytes(path, result.ToArray());
    }
}
