using System.Runtime.InteropServices;
using SkiaSharp;

namespace JGraph.Imaging.Codecs;

/// <summary>Options for <see cref="ImageCodec.Write(string, ImageBuffer, CodecWriteOptions?)"/>.</summary>
/// <param name="JpegQuality">JPEG quality 0–100 (default 95); ignored by lossless formats.</param>
/// <param name="BitDepth">8 or 16 bits per channel for PNG; null follows the image's own class.</param>
/// <param name="Alpha">A one-channel opacity image the size of the picture, or null for opaque.</param>
public sealed record CodecWriteOptions(int? JpegQuality = null, int? BitDepth = null, ImageBuffer? Alpha = null);

/// <summary>
/// Decodes and encodes raster image files to and from <see cref="ImageBuffer"/>, bridging SkiaSharp's
/// integer-channel world to JGraph's [0, 1] double samples. This is the only image-processing type
/// that touches a native codec; the algorithms in <see cref="JGraph.Imaging"/> stay codec-free.
/// </summary>
/// <remarks>
/// Decoding stamps the <see cref="ImageBuffer.Class"/> the file's bit depth implies — <c>uint8</c> for
/// an ordinary file, <c>uint16</c> for a 16-bit PNG — so a script sees MATLAB's classes rather than a
/// uniform double. TIFF is absent because Skia carries no TIFF codec; the read error names the
/// formats that do work.
/// </remarks>
public static class ImageCodec
{
    /// <summary>Extensions <see cref="Read(string, int)"/> can decode, for error messages and completion.</summary>
    public static readonly string[] ReadableExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp"];

    /// <summary>
    /// Reads an image file. A file whose pixels are all neutral gray (R == G == B everywhere) decodes
    /// to a one-channel image, matching MATLAB's <c>imread</c> of a grayscale file; the alpha channel
    /// is dropped (use <see cref="ReadWithAlpha"/> to keep it).
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="frameIndex">0-based frame for multi-frame formats such as GIF.</param>
    /// <exception cref="IOException">The file is missing or cannot be opened.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a decodable image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The frame index is past the end of the file.</exception>
    public static ImageBuffer Read(string path, int frameIndex = 0)
    {
        (ImageBuffer image, ImageBuffer? alpha) = ReadWithAlpha(path, frameIndex);
        alpha?.Dispose();
        return image;
    }

    /// <summary>
    /// Reads an image and its opacity plane. <c>Alpha</c> is null when the file is fully opaque, which
    /// is what MATLAB's third <c>imread</c> output does.
    /// </summary>
    public static (ImageBuffer Image, ImageBuffer? Alpha) ReadWithAlpha(string path, int frameIndex = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);

        // Read the bytes ourselves so a missing or locked file raises a real IOException, and so the
        // PNG header can be inspected for a 16-bit depth Skia's default decode would quietly discard.
        byte[] bytes = File.ReadAllBytes(path);
        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec is null)
        {
            throw new InvalidDataException(
                $"'{path}' is not a supported or valid image file (readable formats: " +
                $"{string.Join(", ", ReadableExtensions)}; TIFF is not supported).");
        }

        int frames = Math.Max(1, codec.FrameCount);
        if (frameIndex >= frames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex,
                $"'{Path.GetFileName(path)}' has {frames} frame(s).");
        }

        var options = new SKCodecOptions(frameIndex);
        int width = codec.Info.Width;
        int height = codec.Info.Height;

        return Is16BitPng(bytes) && TryRead16Bit(codec, options, width, height, out var deep)
            ? deep
            : Read8Bit(codec, options, width, height, path);
    }

    /// <summary>
    /// True when the bytes are a PNG whose IHDR declares 16 bits per channel. The header is at a fixed
    /// offset — 8-byte signature, 4-byte length, "IHDR", 4-byte width, 4-byte height, then depth — so
    /// this needs no decoder, which matters because the decoder is what discards the extra bits.
    /// </summary>
    private static bool Is16BitPng(byte[] bytes) =>
        bytes.Length > 25 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R' &&
        bytes[24] == 16;

    /// <summary>
    /// Decodes into 16 bits per channel. Returns false when Skia declines the colour type, in which
    /// case the caller falls back to the 8-bit path and the file simply reads as <c>uint8</c>.
    /// </summary>
    private static bool TryRead16Bit(
        SKCodec codec, SKCodecOptions options, int width, int height,
        out (ImageBuffer Image, ImageBuffer? Alpha) result)
    {
        result = default;
        var info = new SKImageInfo(width, height, SKColorType.Rgba16161616, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult decoded = codec.GetPixels(info, bitmap.GetPixels(), options);
        if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return false;
        }

        ReadOnlySpan<ushort> samples = MemoryMarshal.Cast<byte, ushort>(bitmap.GetPixelSpan());
        int pixelCount = width * height;
        if (samples.Length < pixelCount * 4)
        {
            return false;
        }

        bool grayscale = true;
        for (int i = 0; i < pixelCount && grayscale; i++)
        {
            int b = i * 4;
            grayscale = samples[b] == samples[b + 1] && samples[b + 1] == samples[b + 2];
        }

        var image = new ImageBuffer(height, width, grayscale ? 1 : 3) { Class = ImageClass.UInt16 };
        Span<double> pixels = image.Pixels;
        for (int i = 0; i < pixelCount; i++)
        {
            int src = i * 4;
            if (grayscale)
            {
                pixels[i] = samples[src] / 65535.0;
            }
            else
            {
                int dst = i * 3;
                pixels[dst] = samples[src] / 65535.0;
                pixels[dst + 1] = samples[src + 1] / 65535.0;
                pixels[dst + 2] = samples[src + 2] / 65535.0;
            }
        }

        GC.KeepAlive(image);
        result = (image, ExtractAlpha(samples, pixelCount, width, height, ImageClass.UInt16));
        return true;
    }

    private static ImageBuffer? ExtractAlpha(
        ReadOnlySpan<ushort> samples, int pixelCount, int width, int height, ImageClass imageClass)
    {
        bool opaque = true;
        for (int i = 0; i < pixelCount && opaque; i++)
        {
            opaque = samples[(i * 4) + 3] == ushort.MaxValue;
        }

        if (opaque)
        {
            return null;
        }

        var alpha = new ImageBuffer(height, width, 1) { Class = imageClass };
        Span<double> px = alpha.Pixels;
        for (int i = 0; i < pixelCount; i++)
        {
            px[i] = samples[(i * 4) + 3] / 65535.0;
        }

        GC.KeepAlive(alpha);
        return alpha;
    }

    private static (ImageBuffer Image, ImageBuffer? Alpha) Read8Bit(
        SKCodec codec, SKCodecOptions options, int width, int height, string path)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult decoded = codec.GetPixels(info, bitmap.GetPixels(), options);
        if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new InvalidDataException($"'{path}' could not be decoded ({decoded}).");
        }

        SKColor[] source = bitmap.Pixels; // row-major, top row first, straight (unpremultiplied) alpha

        bool grayscale = true;
        bool opaque = true;
        for (int i = 0; i < source.Length; i++)
        {
            SKColor color = source[i];
            if (color.Red != color.Green || color.Green != color.Blue)
            {
                grayscale = false;
            }

            if (color.Alpha != byte.MaxValue)
            {
                opaque = false;
            }

            if (!grayscale && !opaque)
            {
                break;
            }
        }

        var image = new ImageBuffer(height, width, grayscale ? 1 : 3) { Class = ImageClass.UInt8 };
        Span<double> pixels = image.Pixels;
        if (grayscale)
        {
            for (int i = 0; i < source.Length; i++)
            {
                pixels[i] = source[i].Red / 255.0;
            }
        }
        else
        {
            for (int i = 0; i < source.Length; i++)
            {
                SKColor color = source[i];
                int b = i * 3;
                pixels[b] = color.Red / 255.0;
                pixels[b + 1] = color.Green / 255.0;
                pixels[b + 2] = color.Blue / 255.0;
            }
        }

        GC.KeepAlive(image);

        ImageBuffer? alpha = null;
        if (!opaque)
        {
            alpha = new ImageBuffer(height, width, 1) { Class = ImageClass.UInt8 };
            Span<double> ap = alpha.Pixels;
            for (int i = 0; i < source.Length; i++)
            {
                ap[i] = source[i].Alpha / 255.0;
            }

            GC.KeepAlive(alpha);
        }

        return (image, alpha);
    }

    /// <summary>
    /// Writes an image file. The format is chosen from the extension: <c>.png</c>, <c>.jpg</c>/
    /// <c>.jpeg</c>, <c>.bmp</c>, or <c>.webp</c>. Samples are clamped to [0, 1] and quantized.
    /// </summary>
    /// <param name="path">Destination path; its extension selects the format.</param>
    /// <param name="image">The image to encode.</param>
    /// <param name="jpegQuality">JPEG quality 0–100 (default 95); ignored for lossless formats.</param>
    public static void Write(string path, ImageBuffer image, int? jpegQuality = null) =>
        Write(path, image, new CodecWriteOptions(jpegQuality));

    /// <summary>Writes an image file with full options: quality, PNG bit depth, and an alpha plane.</summary>
    /// <exception cref="ArgumentException">The extension is not a supported format, or alpha is the wrong size.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="InvalidDataException">Encoding failed.</exception>
    public static void Write(string path, ImageBuffer image, CodecWriteOptions? options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);
        options ??= new CodecWriteOptions();

        string extension = Path.GetExtension(path).ToLowerInvariant();
        SKEncodedImageFormat format = extension switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".bmp" => SKEncodedImageFormat.Bmp,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new ArgumentException(
                $"unsupported image extension '{extension}' (use .png, .jpg/.jpeg, .bmp, or .webp)", nameof(path)),
        };

        ImageBuffer? alpha = options.Alpha;
        if (alpha is not null && (alpha.Height != image.Height || alpha.Width != image.Width))
        {
            throw new ArgumentException("the alpha plane must be the same size as the image.", nameof(options));
        }

        // 16-bit output is a PNG-only affair, and only worth it when the samples carry that much: a
        // uint8 image widened to 16 bits would just be the same 256 levels in a bigger file.
        int bitDepth = options.BitDepth ?? (image.Class == ImageClass.UInt16 ? 16 : 8);
        if (bitDepth == 16 && format == SKEncodedImageFormat.Png &&
            TryWrite16BitPng(path, image, alpha))
        {
            return;
        }

        int quality = Math.Clamp(options.JpegQuality ?? 95, 0, 100);
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        var colors = new SKColor[image.Width * image.Height];
        ReadOnlySpan<double> pixels = image.Pixels;
        ReadOnlySpan<double> alphaPixels = alpha is null ? default : alpha.Pixels;
        for (int i = 0; i < colors.Length; i++)
        {
            byte a = alpha is null ? byte.MaxValue : ToByte(alphaPixels[i]);
            if (image.Channels == 1)
            {
                byte v = ToByte(pixels[i]);
                colors[i] = new SKColor(v, v, v, a);
            }
            else
            {
                int b = i * 3;
                colors[i] = new SKColor(ToByte(pixels[b]), ToByte(pixels[b + 1]), ToByte(pixels[b + 2]), a);
            }
        }

        bitmap.Pixels = colors;
        GC.KeepAlive(image);
        GC.KeepAlive(alpha);

        using SKImage skImage = SKImage.FromBitmap(bitmap);
        using SKData? data = skImage.Encode(format, quality);
        if (data is null)
        {
            throw new InvalidDataException($"failed to encode image to '{path}'.");
        }

        using FileStream output = File.Create(path);
        data.SaveTo(output);
    }

    /// <summary>
    /// Encodes a PNG at 16 bits per channel. Returns false when the encoder will not produce one, so
    /// the caller falls back to 8 bits rather than failing the write.
    /// </summary>
    /// <remarks>
    /// Measured on SkiaSharp 2.88.8: this always returns false. The 16-bit colour type is accepted and
    /// the pixels are copied, but the PNG encoder writes a depth-8 IHDR anyway — so the check below is
    /// on the encoded bytes, not on whether the calls succeeded. Trusting the return codes produced a
    /// file that claimed 16 bits and held 8, which is worse than not offering the option. The code
    /// stays because the check is what makes it safe: if a future Skia encodes 16 bits, it starts
    /// working with no further change.
    /// </remarks>
    private static bool TryWrite16BitPng(string path, ImageBuffer image, ImageBuffer? alpha)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba16161616, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        IntPtr destination = bitmap.GetPixels();
        if (destination == IntPtr.Zero)
        {
            return false;
        }

        int pixelCount = image.Width * image.Height;
        var scratch = new ushort[pixelCount * 4];
        ReadOnlySpan<double> pixels = image.Pixels;
        ReadOnlySpan<double> alphaPixels = alpha is null ? default : alpha.Pixels;
        for (int i = 0; i < pixelCount; i++)
        {
            int dst = i * 4;
            if (image.Channels == 1)
            {
                ushort v = ToUShort(pixels[i]);
                scratch[dst] = scratch[dst + 1] = scratch[dst + 2] = v;
            }
            else
            {
                int b = i * 3;
                scratch[dst] = ToUShort(pixels[b]);
                scratch[dst + 1] = ToUShort(pixels[b + 1]);
                scratch[dst + 2] = ToUShort(pixels[b + 2]);
            }

            scratch[dst + 3] = alpha is null ? ushort.MaxValue : ToUShort(alphaPixels[i]);
        }

        GC.KeepAlive(image);
        GC.KeepAlive(alpha);

        var raw = new byte[scratch.Length * sizeof(ushort)];
        Buffer.BlockCopy(scratch, 0, raw, 0, raw.Length);
        Marshal.Copy(raw, 0, destination, raw.Length);

        using SKImage skImage = SKImage.FromBitmap(bitmap);
        using SKData? data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return false;
        }

        // Only accept the result if the encoder really wrote a 16-bit IHDR. Nothing else reports the
        // downconversion, so without this the file would be tagged uint16 and hold 8 bits.
        byte[] encoded = data.ToArray();
        if (!Is16BitPng(encoded))
        {
            return false;
        }

        File.WriteAllBytes(path, encoded);
        return true;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    private static ushort ToUShort(double value) => (ushort)Math.Clamp((int)Math.Round(value * 65535.0), 0, 65535);
}
