using SkiaSharp;

namespace JGraph.Export.Writers;

/// <summary>
/// Writes a baseline TIFF 6.0 file: little-endian, RGB (alpha dropped — figures are opaque),
/// uncompressed, one strip. Implemented by hand because Skia ships no TIFF encoder; baseline TIFF is
/// a frozen, fully specified subset every reader supports. Expects an
/// <see cref="SKColorType.Rgba8888"/> bitmap.
/// </summary>
internal static class TiffWriter
{
    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagPhotometricInterpretation = 262;
    private const ushort TagStripOffsets = 273;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagXResolution = 282;
    private const ushort TagYResolution = 283;
    private const ushort TagResolutionUnit = 296;

    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    private const int EntryCount = 12;

    public static void Write(Stream stream, SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Rgba8888)
        {
            throw new ArgumentException($"TIFF export requires an Rgba8888 bitmap, not {bitmap.ColorType}.", nameof(bitmap));
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        int stripByteCount = width * height * 3;

        // Layout: 8-byte header │ IFD │ BitsPerSample values │ X/Y resolution rationals │ strip.
        const uint ifdOffset = 8;
        const uint ifdSize = 2 + (EntryCount * 12) + 4;
        const uint bitsPerSampleOffset = ifdOffset + ifdSize;      // 3 × SHORT = 6 bytes
        const uint xResolutionOffset = bitsPerSampleOffset + 6;    // RATIONAL = 8 bytes
        const uint yResolutionOffset = xResolutionOffset + 8;
        const uint stripOffset = yResolutionOffset + 8;

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        // Header: little-endian marker, magic 42, offset of the first (only) IFD.
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(ifdOffset);

        // IFD — entries must be in ascending tag order.
        writer.Write((ushort)EntryCount);
        WriteEntry(writer, TagImageWidth, TypeLong, 1, (uint)width);
        WriteEntry(writer, TagImageLength, TypeLong, 1, (uint)height);
        WriteEntry(writer, TagBitsPerSample, TypeShort, 3, bitsPerSampleOffset);
        WriteEntry(writer, TagCompression, TypeShort, 1, 1);                    // none
        WriteEntry(writer, TagPhotometricInterpretation, TypeShort, 1, 2);      // RGB
        WriteEntry(writer, TagStripOffsets, TypeLong, 1, stripOffset);
        WriteEntry(writer, TagSamplesPerPixel, TypeShort, 1, 3);
        WriteEntry(writer, TagRowsPerStrip, TypeLong, 1, (uint)height);
        WriteEntry(writer, TagStripByteCounts, TypeLong, 1, (uint)stripByteCount);
        WriteEntry(writer, TagXResolution, TypeRational, 1, xResolutionOffset);
        WriteEntry(writer, TagYResolution, TypeRational, 1, yResolutionOffset);
        WriteEntry(writer, TagResolutionUnit, TypeShort, 1, 2);                 // inches
        writer.Write(0u); // no next IFD

        // Out-of-line values.
        writer.Write((ushort)8);
        writer.Write((ushort)8);
        writer.Write((ushort)8);
        writer.Write(96u);
        writer.Write(1u);
        writer.Write(96u);
        writer.Write(1u);

        // Strip: RGBA → RGB, top-down.
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int sourceRowBytes = bitmap.RowBytes;
        byte[] row = new byte[width * 3];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = pixels.Slice(y * sourceRowBytes, width * 4);
            for (int x = 0; x < width; x++)
            {
                row[x * 3] = source[x * 4];
                row[(x * 3) + 1] = source[(x * 4) + 1];
                row[(x * 3) + 2] = source[(x * 4) + 2];
            }

            writer.Write(row);
        }
    }

    /// <summary>Writes one 12-byte IFD entry; <paramref name="value"/> is inline or an offset.</summary>
    private static void WriteEntry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);

        if (type == TypeShort && count == 1)
        {
            // Inline SHORT values occupy the low two bytes of the value field.
            writer.Write((ushort)value);
            writer.Write((ushort)0);
        }
        else
        {
            writer.Write(value);
        }
    }
}
