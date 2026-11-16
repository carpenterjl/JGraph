using System.Buffers.Binary;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// What R2025b's imbmpinfo reads from a BMP header: the size, the bits a pixel, the compression, the
/// colour masks and the colour map (open item 344, ADR 0380).
/// </summary>
/// <param name="HeaderSize">The bitmap header's size: 12, 40, 56, 108 or 124.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height as stored: negative for a file whose rows go top-down.</param>
/// <param name="BitDepth">The bits a pixel the header gives.</param>
/// <param name="Compression">0 none, 1 RLE8, 2 RLE4, 3 bitfields, 4 24-bit RLE.</param>
/// <param name="FileSize">The file size the header gives.</param>
/// <param name="DataOffset">Where the pixels start.</param>
/// <param name="Map">The colour map read between the header and the pixels, or null when there is none.</param>
/// <param name="Masks">The red, green, blue and (for a 108- or 124-byte header) alpha masks of a bitfields file.</param>
public sealed record BmpHeader(
    int HeaderSize, int Width, int Height, int BitDepth, int Compression, long FileSize, long DataOffset,
    double[,]? Map, uint[] Masks)
{
    /// <summary>
    /// imbmpinfo's ColorType: indexed when the file holds a colour map, otherwise grayscale up to eight
    /// bits a pixel and truecolor above.
    /// </summary>
    public string ColorType => Map is not null ? "indexed" : BitDepth <= 8 ? "grayscale" : "truecolor";
}

/// <summary>
/// BMP files read as R2025b's readbmp reads them (open item 344, ADR 0380): imbmpinfo's header rules and
/// readbmpdata's samples, for every compression it takes: uncompressed 1-, 4-, 8-, 16-, 24- and 32-bit
/// files, RLE8 and RLE4 files (indices and their map), and 16- and 32-bit bitfields files (colours).
/// </summary>
/// <remarks>
/// <para>
/// The header follows imbmpinfo: a 12-byte (Windows 2.x) header has 16-bit sizes and a palette of three
/// bytes an entry; a 40- or 56-byte header reads three colour masks right after its first 40 bytes when
/// the file is bitfields, and otherwise a palette; a 108- or 124-byte header carries its masks inside.
/// The colour map holds as many entries as fit between the header and the pixels (biClrUsed is not
/// read), so a 24-bit file with a palette is indexed too. A 108-byte header on a 16- or 32-bit file
/// that is not bitfields is <c>invalidBMPmetadata</c>, a compression past 4 is
/// <c>unrecognizedCompression</c> and 24-bit RLE <c>unsupportedCompressionScheme</c>.
/// </para>
/// <para>
/// The samples follow readbmpdata: data short of the rows is filled with zeros after
/// <c>truncatedImageData</c>, a 1-bit file reads logical, and an RLE file is decoded into rows padded
/// to four (RLE8) or eight (RLE4) pixels, a run carrying on into the next row, with bmpdrle's warnings
/// (a write past the rows, a code past the data, no end-of-bitmap code, data after it). Three answers
/// are the true ones rather than R2025b's (the ADR's divergences): a bitfields sample is
/// <c>round(v * 255 / (2^n - 1))</c> where readbmpdata adds 0.5 before uint8 rounds again (a black
/// 16-bit pixel reads 1 there); a 32-bit bitfields file reads each colour through its mask where
/// readbmpdata orders the masks' bytes by sorting them with a zero alpha mask among them; and an RLE8
/// delta moves from where the last code left the cursor, as RLE4 does there and the BMP format says,
/// where R2025b's RLE8 decoder moves from the start of the row (after an encoded run) or one short
/// (after an absolute run).
/// </para>
/// </remarks>
public static class BmpFile
{
    private const string MissingEndCode = "MATLAB:imagesci:bmpdrle:missingEndCode";

    /// <summary>
    /// Reads a BMP file's header, or null when the bytes are not a BMP this reader takes (another
    /// signature, an OS/2 2.x or unknown header size, a negative width, a header cut short).
    /// </summary>
    /// <exception cref="ImageFileRefusal">The header is one imbmpinfo refuses.</exception>
    public static BmpHeader? ReadHeader(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < 26 || file[0] != (byte)'B' || file[1] != (byte)'M')
        {
            return null;
        }

        long fileSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(2));
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10));
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(14));
        if (headerSize == 12)
        {
            int width2 = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(18));
            int height2 = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(20));
            int bits2 = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(24));
            return new BmpHeader(12, width2, height2, bits2, 0, fileSize, offset, Palette(file, 26, offset, 3), []);
        }

        if (headerSize is not (40 or 56 or 108 or 124) || file.Length < 14 + 40)
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(18));
        int height = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(22));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(28));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(30));
        if (compression > 4)
        {
            throw new ImageFileRefusal("MATLAB:imagesci:imbmpinfo:unrecognizedCompression",
                $"Unrecognized compression value ({compression}).");
        }

        if (width < 0)
        {
            return null;
        }

        bool bitfields = compression == 3;
        if (headerSize is 40 or 56)
        {
            // readWin3xInfo: the masks sit right after the first 40 bytes, and a bitfields file has no map.
            if (bitfields)
            {
                return file.Length < 66 ? null
                    : new BmpHeader(headerSize, width, height, bits, 3, fileSize, offset, null,
                        [Mask(file, 54), Mask(file, 58), Mask(file, 62)]);
            }

            return new BmpHeader(headerSize, width, height, bits, (int)compression, fileSize, offset,
                Palette(file, 54, offset, 4), []);
        }

        if (file.Length < 14 + headerSize)
        {
            return null;
        }

        if (headerSize == 108 && bits is 16 or 32 && !bitfields)
        {
            throw new ImageFileRefusal("MATLAB:imagesci:imbmpinfo:invalidBMPmetadata",
                "BMP Version 4 file appears to be corrupt.");
        }

        // readWin4xInfo and readWin5xInfo read the masks inside the header and a map after it either way.
        uint[] masks = bitfields ? [Mask(file, 54), Mask(file, 58), Mask(file, 62), Mask(file, 66)] : [];
        return new BmpHeader(headerSize, width, height, bits, (int)compression, fileSize, offset,
            Palette(file, 14 + headerSize, offset, 4), masks);
    }

    /// <summary>
    /// Reads a BMP file's samples and colour map as readbmp does, or null when the file is not one this
    /// reader takes (the caller then decodes it another way). <paramref name="warnings"/> collects what
    /// R2025b warns as it reads.
    /// </summary>
    /// <exception cref="ImageFileRefusal">The file is one imbmpinfo or readbmpdata refuses.</exception>
    public static (ImageBuffer Image, double[,]? Map, BmpHeader Header)? Read(byte[] file, List<ImageFileWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (ReadHeader(file) is not { } header)
        {
            return null;
        }

        int width = header.Width;
        int height = Math.Abs(header.Height);
        bool topDown = header.Height < 0;
        ImageBuffer? image = header.Compression switch
        {
            0 or 3 => header.BitDepth switch
            {
                1 or 4 or 8 => Indexed(file, header, width, height, topDown, warnings),
                16 => Colour16(file, header, width, height, topDown, warnings),
                24 or 32 => Colour24Or32(file, header, width, height, topDown, warnings),
                _ => null,
            },
            1 => Rle(file, header, width, height, topDown, rle8: true, warnings),
            2 => Rle(file, header, width, height, topDown, rle8: false, warnings),
            _ => throw new ImageFileRefusal("MATLAB:imagesci:readbmpdata:unsupportedCompressionScheme",
                "24-bit RLE compression not supported."),
        };

        return image is null ? null : (image, header.Map, header);
    }

    /// <summary>The palette between <paramref name="start"/> and the pixels, or null when there is none.</summary>
    private static double[,]? Palette(byte[] file, long start, long offset, int entrySize)
    {
        long entries = (offset - start) / entrySize;
        if (offset < start)
        {
            entries = 0;
        }

        entries = Math.Min(entries, Math.Max(0, (file.Length - start) / entrySize));
        if (entries <= 0)
        {
            return null;
        }

        var map = new double[entries, 3];
        for (int i = 0; i < entries; i++)
        {
            long at = start + (entrySize * i);
            map[i, 0] = file[at + 2] / 255.0;
            map[i, 1] = file[at + 1] / 255.0;
            map[i, 2] = file[at] / 255.0;
        }

        return map;
    }

    private static uint Mask(byte[] file, int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));

    /// <summary>
    /// readFromFile: <paramref name="count"/> bytes from the pixels' offset, the missing ones zero after
    /// <c>truncatedImageData</c>.
    /// </summary>
    private static byte[] Data(byte[] file, BmpHeader header, long count, List<ImageFileWarning> warnings)
    {
        var data = new byte[Math.Max(0, count)];
        long available = Math.Max(0, Math.Min(count, file.Length - header.DataOffset));
        if (available > 0)
        {
            Array.Copy(file, header.DataOffset, data, 0, available);
        }

        if (available < count)
        {
            warnings.Add(new ImageFileWarning("MATLAB:imagesci:readbmpdata:truncatedImageData",
                "Invalid BMP file: truncated image data."));
        }

        return data;
    }

    /// <summary>A 1-, 4- or 8-bit file's indices (logical for one bit).</summary>
    private static ImageBuffer Indexed(byte[] file, BmpHeader header, int width, int height, bool topDown, List<ImageFileWarning> warnings)
    {
        int bits = header.BitDepth;
        int stride = (((width * bits) + 31) / 32) * 4;
        byte[] data = Data(file, header, (long)stride * height, warnings);
        var image = new ImageBuffer(height, width, 1) { Class = bits == 1 ? ImageClass.Logical : ImageClass.UInt8 };
        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            int start = row * stride;
            for (int x = 0; x < width; x++)
            {
                int bit = x * bits;
                int index = (data[start + (bit / 8)] >> (8 - bits - (bit % 8))) & ((1 << bits) - 1);
                image[y, x, 0] = bits == 1 ? index : index / 255.0;
            }
        }

        GC.KeepAlive(image);
        return image;
    }

    /// <summary>
    /// A 16-bit file: five bits a colour (bit replication to eight) without compression, or each mask's
    /// bits scaled to eight (<c>round(v * 255 / (2^n - 1))</c>) for a bitfields file.
    /// </summary>
    private static ImageBuffer Colour16(byte[] file, BmpHeader header, int width, int height, bool topDown, List<ImageFileWarning> warnings)
    {
        int stride = 4 * ((width + 1) / 2);
        byte[] data = Data(file, header, (long)stride * height, warnings);
        var image = new ImageBuffer(height, width, 3) { Class = ImageClass.UInt8 };
        bool bitfields = header.Compression == 3;
        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                uint v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((row * stride) + (2 * x)));
                for (int c = 0; c < 3; c++)
                {
                    double level;
                    if (bitfields)
                    {
                        level = MaskLevel(v, header.Masks[c]);
                    }
                    else
                    {
                        uint five = (v >> (10 - (5 * c))) & 31;
                        level = (five << 3) | (five >> 2);
                    }

                    image[y, x, c] = level / 255.0;
                }
            }
        }

        GC.KeepAlive(image);
        return image;
    }

    /// <summary>
    /// A 24-bit file (blue, green, red) or a 32-bit one: blue, green, red and a pad byte without
    /// compression, or each colour through its mask for a bitfields file.
    /// </summary>
    private static ImageBuffer Colour24Or32(byte[] file, BmpHeader header, int width, int height, bool topDown, List<ImageFileWarning> warnings)
    {
        int bytesPerPixel = header.BitDepth / 8;
        int stride = bytesPerPixel == 3 ? 4 * (((3 * width) + 3) / 4) : 4 * width;
        byte[] data = Data(file, header, (long)stride * height, warnings);
        var image = new ImageBuffer(height, width, 3) { Class = ImageClass.UInt8 };
        bool bitfields = bytesPerPixel == 4 && header.Compression == 3;
        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                int at = (row * stride) + (bytesPerPixel * x);
                if (bitfields)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
                    for (int c = 0; c < 3; c++)
                    {
                        image[y, x, c] = MaskLevel(v, header.Masks[c]) / 255.0;
                    }
                }
                else
                {
                    image[y, x, 0] = data[at + 2] / 255.0;
                    image[y, x, 1] = data[at + 1] / 255.0;
                    image[y, x, 2] = data[at] / 255.0;
                }
            }
        }

        GC.KeepAlive(image);
        return image;
    }

    /// <summary>One colour through its mask, as an eight-bit level: <c>round(v * 255 / (2^n - 1))</c>.</summary>
    private static double MaskLevel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint field = (value & mask) >> shift;
        int bits = System.Numerics.BitOperations.PopCount(mask >> shift);
        double top = Math.Pow(2, bits) - 1;
        return bits == 8 ? field : Math.Round(field * 255.0 / top, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// An RLE8 or RLE4 file's indices, as bmpdrle decodes them into rows of four (RLE8) or eight (RLE4)
    /// pixels: a run past a row's end carries on into the next row, an end of line starts the next row,
    /// a delta moves the cursor right and up, and a pixel no code reaches is zero. The data read is the
    /// header's file size less the pixels' offset.
    /// </summary>
    private static ImageBuffer Rle(byte[] file, BmpHeader header, int width, int height, bool topDown, bool rle8, List<ImageFileWarning> warnings)
    {
        int padded = rle8 ? 4 * ((width + 3) / 4) : 8 * ((width + 7) / 8);
        long count = header.FileSize - header.DataOffset;
        if (count <= 0)
        {
            count = Math.Max(0, file.Length - header.DataOffset);
        }

        byte[] input = Data(file, header, count, warnings);
        long total = (long)padded * height;
        var output = new byte[total];
        int outside = 0;
        long pos = 0;
        long y = 0;
        int i = 0;
        int n = input.Length;

        void Put(byte value)
        {
            if (pos >= 0 && pos < total)
            {
                output[pos] = value;
            }
            else if (outside++ < 100)
            {
                warnings.Add(new ImageFileWarning("MATLAB:imagesci:bmpdrle:indexExceedsOutputBufferLength",
                    "Apparent RLE decoding failure; BMP file may be corrupt."));
            }

            pos++;
        }

        void ShortInput()
        {
            warnings.Add(new ImageFileWarning("MATLAB:imagesci:bmpdrle:indexExceedsInputBufferLength",
                "Apparent RLE decoding failure; BMP file may be truncated."));
            warnings.Add(new ImageFileWarning(MissingEndCode, "No end-of-bitmap code; BMP file may be truncated."));
        }

        while (true)
        {
            if (i >= n)
            {
                warnings.Add(new ImageFileWarning(MissingEndCode, "No end-of-bitmap code; BMP file may be truncated."));
                break;
            }

            if (i + 1 >= n)
            {
                ShortInput();
                break;
            }

            int first = input[i];
            int second = input[i + 1];
            i += 2;
            if (first > 0)
            {
                for (int k = 0; k < first; k++)
                {
                    Put(rle8 ? (byte)second : (byte)(k % 2 == 0 ? second >> 4 : second & 15));
                }

                continue;
            }

            if (second == 0)
            {
                y++;
                pos = y * padded;
                continue;
            }

            if (second == 1)
            {
                if (i < n)
                {
                    warnings.Add(new ImageFileWarning("MATLAB:imagesci:bmpdrle:prematureEndCode",
                        "End-of-bitmap code encountered before end of data."));
                }

                break;
            }

            if (second == 2)
            {
                if (i + 1 >= n)
                {
                    ShortInput();
                    break;
                }

                long x = pos - (y * padded);
                y += input[i + 1];
                pos = (y * padded) + x + input[i];
                i += 2;
                continue;
            }

            // An absolute run of `second` pixels, its bytes padded to a whole number of 16-bit words.
            int bytes = rle8 ? second : (second + 1) / 2;
            bool cut = false;
            for (int k = 0; k < second; k++)
            {
                int at = i + (rle8 ? k : k / 2);
                if (at >= n)
                {
                    cut = true;
                    break;
                }

                Put(rle8 ? input[at] : (byte)(k % 2 == 0 ? input[at] >> 4 : input[at] & 15));
            }

            if (cut)
            {
                ShortInput();
                break;
            }

            i += bytes + (bytes & 1);
        }

        var image = new ImageBuffer(height, width, 1) { Class = ImageClass.UInt8 };
        for (int row = 0; row < height; row++)
        {
            int yImage = topDown ? row : height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                image[yImage, x, 0] = output[((long)row * padded) + x] / 255.0;
            }
        }

        GC.KeepAlive(image);
        return image;
    }
}
