using System.Buffers.Binary;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// Windows BMP files written as R2025b's writebmp writes them, and indexed BMP files read with their
/// indices and palette kept, as R2025b's readbmp answers (open item 335, ADR 0373). Skia decodes a BMP
/// but does not encode one, which is why every BMP write failed before this.
/// </summary>
/// <remarks>
/// <para>
/// The writer follows writebmp.m: a logical greyscale picture is a 1-bit file with a black-and-white
/// palette; any other greyscale picture is an 8-bit file with a 256-entry palette (a grey ramp, or the
/// colour map an indexed write gives, padded with black); a three-plane picture is a 24-bit file. Rows
/// go bottom-up, padded to four bytes (to 32 pixels at one bit), with the header fields writebmp
/// writes (no resolution, the palette size in biClrUsed).
/// </para>
/// <para>
/// The reader takes an uncompressed 1-, 4- or 8-bit file (BI_RGB) with a BITMAPINFOHEADER or a later
/// header: the indices come back as an image of class uint8 (the index over 255, as
/// <see cref="IndexedPng"/> holds them), or logical for a 1-bit file, with the palette as an n-by-3
/// map in [0, 1]. Any other BMP is left to Skia.
/// </para>
/// </remarks>
public static class IndexedBmp
{
    /// <summary>True when the file is an uncompressed BMP of 1, 4 or 8 bits a pixel.</summary>
    public static bool IsIndexed(string path)
    {
        using FileStream file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[34];
        if (file.Read(header) != header.Length || header[0] != (byte)'B' || header[1] != (byte)'M')
        {
            return false;
        }

        int infoSize = BinaryPrimitives.ReadInt32LittleEndian(header[14..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(header[30..]);
        return infoSize >= 40 && bits is 1 or 4 or 8 && compression == 0;
    }

    /// <summary>The bits a pixel an uncompressed BMP stores, or null for a file that is not a BMP.</summary>
    internal static int? BitCount(byte[] bytes) =>
        bytes.Length >= 30 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M'
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28))
            : null;

    /// <summary>Reads an indexed BMP's indices and its palette.</summary>
    /// <exception cref="InvalidDataException">The file is truncated or not an indexed BMP.</exception>
    public static (ImageBuffer Image, double[,] Map) Read(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 54)
        {
            throw new InvalidDataException("Truncated BMP.");
        }

        int offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(10));
        int infoSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(14));
        int width = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(18));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(22));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(28));
        int used = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(46));
        if (bits is not (1 or 4 or 8) || width <= 0 || rawHeight == 0)
        {
            throw new InvalidDataException("Not an indexed BMP.");
        }

        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);
        int entries = used > 0 ? Math.Min(used, 1 << bits) : 1 << bits;
        int paletteAt = 14 + infoSize;
        entries = Math.Max(0, Math.Min(entries, (offset - paletteAt) / 4));
        var map = new double[entries, 3];
        for (int i = 0; i < entries; i++)
        {
            int at = paletteAt + (4 * i);
            map[i, 0] = file[at + 2] / 255.0;
            map[i, 1] = file[at + 1] / 255.0;
            map[i, 2] = file[at] / 255.0;
        }

        int stride = (((width * bits) + 31) / 32) * 4;
        if ((long)offset + ((long)stride * height) > file.Length)
        {
            throw new InvalidDataException("Truncated BMP.");
        }

        var image = new ImageBuffer(height, width, 1) { Class = bits == 1 ? ImageClass.Logical : ImageClass.UInt8 };
        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            int start = offset + (row * stride);
            for (int x = 0; x < width; x++)
            {
                int bit = x * bits;
                int index = (file[start + (bit / 8)] >> (8 - bits - (bit % 8))) & ((1 << bits) - 1);
                image[y, x, 0] = bits == 1 ? index : index / 255.0;
            }
        }

        return (image, map);
    }

    /// <summary>
    /// Writes a picture as writebmp does. With <paramref name="map"/> the one-plane picture holds
    /// indices (a double or single index is one-based, as writebmp's <c>uint8(X - 1)</c> reads it) and
    /// the map is the palette; without one a greyscale picture gets a grey ramp.
    /// </summary>
    /// <exception cref="ArgumentException">The picture or the map is not one writebmp takes.</exception>
    public static void Write(string path, ImageBuffer image, double[,]? map = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        int height = image.Height;
        int width = image.Width;
        int channels = image.Channels;
        if (channels is not (1 or 3))
        {
            throw new ArgumentException("a BMP picture has one plane or three.", nameof(image));
        }

        bool oneBit = channels == 1 && image.Class == ImageClass.Logical;
        int bits = channels == 3 ? 24 : oneBit ? 1 : 8;
        int paletteEntries = channels == 3 ? 0 : oneBit ? 2 : 256;
        if (map is not null && channels == 1)
        {
            if (map.GetLength(1) != 3 || map.GetLength(0) > 256 || (oneBit && map.GetLength(0) != 2))
            {
                throw new ArgumentException("a BMP palette has at most 256 RGB entries, and two for a logical picture.", nameof(map));
            }
        }

        int stride = (((width * bits) + 31) / 32) * 4;
        int offset = 14 + 40 + (4 * paletteEntries);
        int imageSize = stride * height;
        byte[] file = new byte[offset + imageSize];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), (ushort)bits);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(34), imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(46), paletteEntries);

        for (int i = 0; i < paletteEntries; i++)
        {
            double r, g, b;
            if (map is not null)
            {
                bool present = i < map.GetLength(0);
                r = present ? map[i, 0] : 0;
                g = present ? map[i, 1] : 0;
                b = present ? map[i, 2] : 0;
            }
            else
            {
                r = g = b = i / (double)(paletteEntries - 1);
            }

            int at = 54 + (4 * i);
            file[at] = PaletteByte(b);
            file[at + 1] = PaletteByte(g);
            file[at + 2] = PaletteByte(r);
        }

        bool floatingIndices = map is not null && image.Class is ImageClass.Double or ImageClass.Single;
        for (int y = 0; y < height; y++)
        {
            int start = offset + ((height - 1 - y) * stride);
            for (int x = 0; x < width; x++)
            {
                if (channels == 3)
                {
                    int at = start + (3 * x);
                    file[at] = SampleByte(image[y, x, 2]);
                    file[at + 1] = SampleByte(image[y, x, 1]);
                    file[at + 2] = SampleByte(image[y, x, 0]);
                }
                else if (oneBit)
                {
                    if (image[y, x, 0] != 0)
                    {
                        file[start + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    }
                }
                else
                {
                    file[start + x] = map is null ? SampleByte(image[y, x, 0])
                        : SaturatedByte(image.Class.ToNative(image[y, x, 0]) - (floatingIndices ? 1 : 0));
                }
            }
        }

        GC.KeepAlive(image);
        File.WriteAllBytes(path, file);
    }

    /// <summary><c>round(map * 255)</c>, a palette entry's byte.</summary>
    private static byte PaletteByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>
    /// A normalized sample's byte: a uint8 sample's own value, and <c>uint8(255 * x)</c> for a float
    /// (half away from zero, saturated, NaN to 0).
    /// </summary>
    private static byte SampleByte(double sample) => SaturatedByte(sample * 255.0);

    /// <summary><c>uint8(v)</c>: half away from zero, saturated, NaN to 0.</summary>
    private static byte SaturatedByte(double value) =>
        double.IsNaN(value) ? (byte)0 : (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
