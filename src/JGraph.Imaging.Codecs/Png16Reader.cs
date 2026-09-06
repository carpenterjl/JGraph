using System.Buffers.Binary;
using System.IO.Compression;

namespace JGraph.Imaging.Codecs;

/// <summary>Preserves 8/16-bit PNG samples, including RGB under fully transparent pixels.</summary>
internal static class Png16Reader
{
    internal static (ImageBuffer Image, ImageBuffer? Alpha) Read(byte[] file, int width, int height)
    {
        int colorType = file[25];
        int sampleBytes = file[24] / 8;
        double maximum = sampleBytes == 2 ? 65535 : 255;
        ImageClass imageClass = sampleBytes == 2 ? ImageClass.UInt16 : ImageClass.UInt8;
        int channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4,
            _ => throw new InvalidDataException("Invalid 16-bit PNG color type.") };
        int colors = colorType is 0 or 4 ? 1 : 3;
        using var compressed = new MemoryStream();
        byte[]? transparent = null;
        for (int at = 8; at <= file.Length - 12;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at, 4));
            if (length < 0 || length > file.Length - at - 12)
                throw new InvalidDataException("Truncated PNG chunk.");
            ReadOnlySpan<byte> name = file.AsSpan(at + 4, 4);
            ReadOnlySpan<byte> payload = file.AsSpan(at + 8, length);
            if (PngChunks.Check(name, payload) != BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 8 + length, 4)))
                throw new InvalidDataException("Invalid PNG chunk checksum.");
            if (name.SequenceEqual("IDAT"u8)) compressed.Write(payload);
            if (name.SequenceEqual("tRNS"u8)) transparent = payload.ToArray();
            at += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var image = new ImageBuffer(height, width, colors) { Class = imageClass };
        ImageBuffer? alpha = channels != colors || transparent is not null
            ? new ImageBuffer(height, width, 1) { Class = imageClass } : null;
        bool opaque = true;
        try
        {
            if (file[28] == 0) Pass(0, 0, 1, 1);
            else if (file[28] == 1)
            {
                Pass(0, 0, 8, 8); Pass(4, 0, 8, 8); Pass(0, 4, 4, 8);
                Pass(2, 0, 4, 4); Pass(0, 2, 2, 4); Pass(1, 0, 2, 2); Pass(0, 1, 1, 2);
            }
            else throw new InvalidDataException("Invalid PNG interlace method.");
            if (opaque) { alpha?.Dispose(); alpha = null; }
            return (image, alpha);
        }
        catch { image.Dispose(); alpha?.Dispose(); throw; }

        void Pass(int x0, int y0, int dx, int dy)
        {
            if (x0 >= width || y0 >= height) return;
            int columns = (width - x0 + dx - 1) / dx;
            int bpp = channels * sampleBytes;
            byte[] previous = new byte[checked(columns * bpp)];
            byte[] row = new byte[previous.Length];
            for (int y = y0; y < height; y += dy)
            {
                int filter = zlib.ReadByte();
                if (filter is < 0 or > 4) throw new InvalidDataException("Invalid PNG row filter.");
                zlib.ReadExactly(row);
                for (int i = 0; i < row.Length; i++)
                {
                    int a = i >= bpp ? row[i - bpp] : 0;
                    int b = previous[i];
                    int c = i >= bpp ? previous[i - bpp] : 0;
                    int predictor = filter switch { 1 => a, 2 => b, 3 => (a + b) / 2,
                        4 => Paeth(a, b, c), _ => 0 };
                    row[i] = unchecked((byte)(row[i] + predictor));
                }
                for (int column = 0; column < columns; column++)
                {
                    int x = x0 + column * dx;
                    int offset = column * bpp;
                    bool clear = transparent is not null && transparent.Length == colors * 2;
                    for (int ch = 0; ch < colors; ch++)
                    {
                        int sample = sampleBytes == 2 ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(offset + ch * 2, 2)) : row[offset + ch];
                        image[y, x, ch] = sample / maximum;
                        clear &= transparent is not null && transparent.Length >= ch * 2 + 2
                            && sample == BinaryPrimitives.ReadUInt16BigEndian(transparent.AsSpan(ch * 2, 2));
                    }
                    if (alpha is not null)
                    {
                        int sample = channels != colors
                            ? sampleBytes == 2 ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(offset + colors * 2, 2)) : row[offset + colors]
                            : clear ? 0 : (int)maximum;
                        alpha[y, x, 0] = sample / maximum;
                        opaque &= sample == maximum;
                    }
                }
                (row, previous) = (previous, row);
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
