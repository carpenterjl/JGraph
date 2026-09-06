using System.Buffers.Binary;
using System.IO.Compression;

namespace JGraph.Imaging.Codecs;

/// <summary>Preserves PNG palette indices instead of expanding them into RGB.</summary>
public static class IndexedPng
{
    public static bool IsIndexed(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[29];
        return file.Read(header) == 29 && header[..8].SequenceEqual(PngChunks.Signature) && header[25] == 3;
    }

    public static void Write(string path, ImageBuffer image, double[,] map)
    {
        if (map.GetLength(0) is < 1 or > 256 || map.GetLength(1) != 3)
            throw new ArgumentException("PNG palettes require 1 to 256 RGB entries.");
        using var file = File.Create(path);
        file.Write(PngChunks.Signature);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;
        header[9] = 3;
        PngChunks.Write(file, "IHDR", header);
        byte[] palette = new byte[map.GetLength(0) * 3];
        for (int i = 0; i < map.GetLength(0); i++)
        for (int c = 0; c < 3; c++)
            palette[i * 3 + c] = (byte)Math.Round(Math.Clamp(map[i, c], 0, 1) * 255);
        PngChunks.Write(file, "PLTE", palette);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            for (int r = 0; r < image.Height; r++)
            {
                zip.WriteByte(0);
                for (int c = 0; c < image.Width; c++)
                {
                    double raw = image.Class.ToNative(image[r, c, 0]) - (image.Class is ImageClass.Double or ImageClass.Single ? 1 : 0);
                    zip.WriteByte((byte)Math.Clamp((int)raw, 0, map.GetLength(0) - 1));
                }
            }
        }
        PngChunks.Write(file, "IDAT", compressed.ToArray());
        PngChunks.Write(file, "IEND", []);
    }

    public static (ImageBuffer Image, double[,] Map, ImageBuffer? Alpha) Read(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int w = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(16)), h = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(20)), bits = file[24];
        if (bits is not (1 or 2 or 4 or 8))
            throw new InvalidDataException("Invalid indexed PNG depth.");
        using var compressed = new MemoryStream();
        byte[] palette = [], transparency = [];
        for (int at = 8; at <= file.Length - 12;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
            if (length < 0 || length > file.Length - at - 12)
                throw new InvalidDataException("Truncated PNG.");
            var tag = file.AsSpan(at + 4, 4);
            var payload = file.AsSpan(at + 8, length);
            if (PngChunks.Check(tag, payload) != BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 8 + length)))
                throw new InvalidDataException("PNG checksum mismatch.");
            if (tag.SequenceEqual("PLTE"u8))
                palette = payload.ToArray();
            if (tag.SequenceEqual("tRNS"u8))
                transparency = payload.ToArray();
            if (tag.SequenceEqual("IDAT"u8))
                compressed.Write(payload);
            at += length + 12;
        }
        double[,] map = new double[palette.Length / 3, 3];
        for (int i = 0; i < map.GetLength(0); i++)
        for (int c = 0; c < 3; c++)
            map[i, c] = palette[i * 3 + c] / 255.0;
        var image = new ImageBuffer(h, w, 1) { Class = ImageClass.UInt8 };
        ImageBuffer? alpha = transparency.Length == 0 ? null : new ImageBuffer(h, w, 1) { Class = ImageClass.UInt8 };
        compressed.Position = 0;
        using var zip = new ZLibStream(compressed, CompressionMode.Decompress);
        try
        {
            if (file[28] == 0)
                Pass(0, 0, 1, 1);
            else
            {
                Pass(0, 0, 8, 8);
                Pass(4, 0, 8, 8);
                Pass(0, 4, 4, 8);
                Pass(2, 0, 4, 4);
                Pass(0, 2, 2, 4);
                Pass(1, 0, 2, 2);
                Pass(0, 1, 1, 2);
            }
            return (image, map, alpha);
        }
        catch { image.Dispose(); alpha?.Dispose(); throw; }
        void Pass(int x0, int y0, int dx, int dy)
        {
            if (x0 >= w || y0 >= h)
                return;
            int columns = (w - x0 + dx - 1) / dx;
            byte[] row = new byte[(columns * bits + 7) / 8], previous = new byte[row.Length];
            for (int y = y0; y < h; y += dy)
            {
                int filter = zip.ReadByte();
                if (filter is < 0 or > 4)
                    throw new InvalidDataException("Invalid PNG filter.");
                zip.ReadExactly(row);
                for (int i = 0; i < row.Length; i++)
                {
                    int a = i > 0 ? row[i - 1] : 0, b = previous[i], c = i > 0 ? previous[i - 1] : 0, p = a + b - c;
                    int predictor = filter switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Math.Abs(p - a) <= Math.Abs(p - b) && Math.Abs(p - a) <= Math.Abs(p - c) ? a : Math.Abs(p - b) <= Math.Abs(p - c) ? b : c,
                        _ => 0
                    };
                    row[i] = unchecked((byte)(row[i] + predictor));
                }
                for (int x = 0; x < columns; x++)
                {
                    int index = (row[x * bits / 8] >> (8 - bits - x * bits % 8)) & ((1 << bits) - 1);
                    image[y, x0 + x * dx, 0] = index / 255.0;
                    if (alpha is not null)
                        alpha[y, x0 + x * dx, 0] = (index < transparency.Length ? transparency[index] : 255) / 255.0;
                }
                (row, previous) = (previous, row);
            }
        }
    }
}
