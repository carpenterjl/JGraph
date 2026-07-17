using SkiaSharp;

namespace JGraph.Export.Writers;

/// <summary>
/// Writes an uncompressed 32-bit BMP (BITMAPINFOHEADER, BI_RGB). Implemented by hand because Skia
/// ships no BMP encoder; the format is a fixed 54-byte header plus bottom-up BGRA rows, which is why
/// it is safe to own. Expects a <see cref="SKColorType.Bgra8888"/> bitmap so rows copy straight out.
/// </summary>
internal static class BmpWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    /// <summary>Pixels per metre for 96 DPI (96 / 0.0254).</summary>
    private const int PixelsPerMetre = 3780;

    public static void Write(Stream stream, SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
        {
            throw new ArgumentException($"BMP export requires a Bgra8888 bitmap, not {bitmap.ColorType}.", nameof(bitmap));
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        int rowBytes = width * 4; // 32 bpp: rows are inherently 4-byte aligned, no padding
        int dataSize = rowBytes * height;

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        // BITMAPFILEHEADER
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(FileHeaderSize + InfoHeaderSize + dataSize);
        writer.Write(0);
        writer.Write(FileHeaderSize + InfoHeaderSize);

        // BITMAPINFOHEADER
        writer.Write(InfoHeaderSize);
        writer.Write(width);
        writer.Write(height);          // positive height = bottom-up row order
        writer.Write((ushort)1);       // planes
        writer.Write((ushort)32);      // bits per pixel
        writer.Write(0);               // BI_RGB (uncompressed)
        writer.Write(dataSize);
        writer.Write(PixelsPerMetre);
        writer.Write(PixelsPerMetre);
        writer.Write(0);               // palette colors
        writer.Write(0);               // important colors

        // Pixel rows, bottom-up.
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int sourceRowBytes = bitmap.RowBytes;
        for (int y = height - 1; y >= 0; y--)
        {
            writer.Write(pixels.Slice(y * sourceRowBytes, rowBytes));
        }
    }
}
