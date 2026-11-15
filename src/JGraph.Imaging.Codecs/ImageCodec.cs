
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
/// uniform double. TIFF pages use the managed LibTiff decoder.
/// </remarks>
public static partial class ImageCodec
{
    /// <summary>Extensions <see cref="Read(string, int)"/> can decode, for error messages and completion.</summary>
    public static readonly string[] ReadableExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp", ".tif", ".tiff"];

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
        if (TiffCodec.IsTiff(path))
        {
            var decoded = TiffCodec.Read(path, frameIndex);
            if (decoded.Map is null) return (decoded.Image, decoded.Alpha);
            using ImageBuffer indexed = decoded.Image;
            var rgb = new ImageBuffer(indexed.Height, indexed.Width, 3) { Class = ImageClass.UInt8 };
            for (int p = 0; p < indexed.Height * indexed.Width; p++) for (int c = 0; c < 3; c++) rgb.Pixels[p*3+c] = decoded.Map[(int)Math.Round(indexed.Class.ToNative(indexed.Pixels[p])),c];
            return (rgb, decoded.Alpha);
        }
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
                $"{string.Join(", ", ReadableExtensions)}).");
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

        if (IsNativeSamplePng(bytes))
        {
            return Png16Reader.Read(bytes, width, height);
        }

        (ImageBuffer image, ImageBuffer? alpha) = Read8Bit(codec, options, width, height, path);
        if (IsOneBitGrayPng(bytes) && image.Channels == 1)
        {
            // Skia hands 1-bit grey back as 0 and 255; the samples are already 0 and 1, and the class
            // says what R2025b's imread says: logical (open item 273).
            image.Class = ImageClass.Logical;
        }

        return (image, alpha);
    }

    /// <summary>
    /// True when the bytes are a non-indexed PNG whose IHDR declares 8 or 16 bits per channel. The header is at a fixed
    /// offset — 8-byte signature, 4-byte length, "IHDR", 4-byte width, 4-byte height, then depth — so
    /// this needs no decoder, which matters because the decoder is what discards the extra bits.
    /// </summary>
    private static bool IsNativeSamplePng(byte[] bytes) =>
        bytes.Length > 25 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R' &&
        bytes[24] is 8 or 16 && bytes[25] != 3;

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

        if (EncodedChannels(path) is { } encodedChannels) grayscale = encodedChannels == 1;
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
        if (TiffCodec.IsTiff(path)) { TiffCodec.Write(path, image); return; }
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

        // A logical picture is a 1-bit greyscale PNG, as R2025b's writepng makes it, and so is any
        // greyscale picture written with BitDepth 1 (open item 273); the reader answers it as logical.
        // BitDepth 2 and 4 pack a greyscale picture's levels the same way (open item 294, ADR 0356).
        if (format == SKEncodedImageFormat.Png && image.Channels == 1 && alpha is null
            && (options.BitDepth is 1 or 2 or 4 || (options.BitDepth is null && image.Class == ImageClass.Logical)))
        {
            WritePackedGrayPng(path, image, options.BitDepth ?? 1);
            return;
        }

        int quality = Math.Clamp(options.JpegQuality ?? 95, 0, 100);
        if (format == SKEncodedImageFormat.Png)
        {
            Write8BitPng(path,image,alpha); return;
        }

        // Skia does not encode a BMP, so every BMP write failed; IndexedBmp writes what R2025b's
        // writebmp writes (open item 335, ADR 0373).
        if (format == SKEncodedImageFormat.Bmp)
        {
            IndexedBmp.Write(path, image);
            return;
        }

        // A greyscale JPEG is a one-component file, as R2025b's writer makes it, so it reads back as
        // one plane here and there (open item 335, ADR 0373); it was three components before.
        if (format == SKEncodedImageFormat.Jpeg && image.Channels == 1 && alpha is null)
        {
            WriteGrayJpeg(path, image, quality);
            return;
        }
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

    /// <summary>A one-plane picture as a one-component (greyscale) JPEG.</summary>
    private static void WriteGrayJpeg(string path, ImageBuffer image, int quality)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        int rowBytes = bitmap.RowBytes;
        byte[] samples = new byte[rowBytes * image.Height];
        ReadOnlySpan<double> pixels = image.Pixels;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                samples[(r * rowBytes) + c] = ToByte(pixels[(r * image.Width) + c]);
            }
        }

        GC.KeepAlive(image);
        System.Runtime.InteropServices.Marshal.Copy(samples, 0, bitmap.GetPixels(), samples.Length);
        using SKImage skImage = SKImage.FromBitmap(bitmap);
        using SKData? data = skImage.Encode(SKEncodedImageFormat.Jpeg, quality);
        if (data is null)
        {
            throw new InvalidDataException($"failed to encode image to '{path}'.");
        }

        using FileStream output = File.Create(path);
        data.SaveTo(output);
    }

    /// <summary>Writes native 16-bit PNG samples without Skia's silent 8-bit conversion.</summary>
    private static bool TryWrite16BitPng(string path, ImageBuffer image, ImageBuffer? alpha)
    {
        using FileStream output = File.Create(path);
        output.Write(PngChunks.Signature);
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), image.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), image.Height);
        header[8] = 16;
        header[9] = (byte)(image.Channels == 1 ? (alpha is null ? 0 : 4) : (alpha is null ? 2 : 6));
        PngChunks.Write(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed,
            System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            int channels = image.Channels + (alpha is null ? 0 : 1);
            byte[] row = new byte[checked(1 + image.Width * channels * 2)];
            for (int r = 0; r < image.Height; r++)
            {
                int at = 1; // PNG filter None.
                for (int c = 0; c < image.Width; c++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        ushort sample = ch < image.Channels
                            ? (ushort)PngLevel(image[r, c, ch], image.Class, 16)
                            : (ushort)PngLevel(alpha![r, c, 0], alpha.Class, 16);
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                            row.AsSpan(at, 2), sample);
                        at += 2;
                    }
                }
                zlib.Write(row);
            }
        }
        PngChunks.Write(output, "IDAT", compressed.ToArray());
        PngChunks.Write(output, "IEND", []);
        return true;
    }
    /// <summary>
    /// Writes a 1-, 2- or 4-bit greyscale PNG, 8 / <paramref name="bits"/> samples to a byte, the
    /// first pixel in the high bits, each row padded to a whole byte. At one bit a sample is on when
    /// it is above zero (writepng's <c>data ~= 0</c> after clamping to [0, 1]); at two and four it is
    /// <see cref="PngLevel"/>'s level.
    /// </summary>
    private static void WritePackedGrayPng(string path, ImageBuffer image, int bits)
    {
        using FileStream output = File.Create(path);
        output.Write(PngChunks.Signature);
        byte[] header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), image.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), image.Height);
        header[8] = (byte)bits;
        header[9] = 0;
        PngChunks.Write(output, "IHDR", header);
        int perByte = 8 / bits;
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed,
            System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] row = new byte[checked(1 + ((image.Width + perByte - 1) / perByte))];
            ReadOnlySpan<double> pixels = image.Pixels;
            for (int r = 0; r < image.Height; r++)
            {
                Array.Clear(row); // PNG filter None, and the padding bits.
                ReadOnlySpan<double> samples = pixels.Slice(r * image.Width, image.Width);
                for (int c = 0; c < samples.Length; c++)
                {
                    int level = bits == 1 ? (samples[c] > 0 ? 1 : 0) : PngLevel(samples[c], image.Class, bits);
                    row[1 + (c / perByte)] |= (byte)(level << (8 - (bits * ((c % perByte) + 1))));
                }

                zlib.Write(row);
            }
        }

        GC.KeepAlive(image);
        PngChunks.Write(output, "IDAT", compressed.ToArray());
        PngChunks.Write(output, "IEND", []);
    }

    /// <summary>
    /// True when the bytes are a 1-bit greyscale PNG (IHDR depth 1, colour type 0), which R2025b's
    /// imread answers as a logical matrix.
    /// </summary>
    private static bool IsOneBitGrayPng(byte[] bytes) =>
        bytes.Length > 25 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[14] == (byte)'D' && bytes[15] == (byte)'R' &&
        bytes[24] == 1 && bytes[25] == 0;

    /// <summary>
    /// An eight-bit sample for an encoder that takes bytes, rounding half away from zero as
    /// R2025b's conversion to uint8 does (open item 294).
    /// </summary>
    private static byte ToByte(double value) =>
        value > 0 ? (byte)Math.Min(255.0, Math.Round(value * 255.0, MidpointRounding.AwayFromZero)) : (byte)0;

    /// <summary>
    /// The level a PNG of <paramref name="bits"/> bits a sample stores for a normalized sample of
    /// class <paramref name="imageClass"/>, as R2025b's writepng chooses it (open item 294, ADR 0356,
    /// probes oi_image_values and extra_probe): an integer class keeps its top bits (a uint16 sample
    /// at depth 8 is <c>x &gt;&gt; 8</c>, a uint8 sample at depth 4 is <c>x &gt;&gt; 4</c>) and widens
    /// by repeating its bits (a uint8 sample at depth 16 is <c>x * 257</c>); a logical sample is its
    /// own 0 or 1 at any depth; a double or single sample is <c>round(x * (2^bits - 1))</c>, half
    /// away from zero, after clamping to [0, 1]. Depth 1 is not read here: it is on for any nonzero
    /// sample.
    /// </summary>
    internal static int PngLevel(double sample, ImageClass imageClass, int bits)
    {
        if (!(sample > 0))
        {
            return 0; // zero, below zero, and NaN
        }

        switch (imageClass)
        {
            case ImageClass.Logical:
                return 1;
            case ImageClass.UInt8:
            {
                int native = (int)Math.Min(255.0, Math.Round(sample * 255.0));
                return bits <= 8 ? native >> (8 - bits) : native * 257;
            }

            case ImageClass.UInt16:
            {
                int native = (int)Math.Min(65535.0, Math.Round(sample * 65535.0));
                return native >> (16 - bits);
            }

            default:
            {
                int top = (1 << bits) - 1;
                return (int)Math.Min(top, Math.Round(sample * top, MidpointRounding.AwayFromZero));
            }
        }
    }

    /// <summary>
    /// The bit depth imfinfo reports for a PNG file, read from its header: the depth of one sample
    /// times the colour samples a pixel has, not counting alpha (R2025b reports a greyscale file with
    /// alpha as 8 and an RGBA file as 24) and counting a palette index as one. Null for any other
    /// file.
    /// </summary>
    public static int? PngBitDepth(string path)
    {
        byte[] head = new byte[26];
        using (FileStream file = File.OpenRead(path))
        {
            if (file.Read(head, 0, head.Length) != head.Length)
            {
                return null;
            }
        }

        if (!head.AsSpan(0, 8).SequenceEqual(PngChunks.Signature) || head[12] != (byte)'I' || head[13] != (byte)'H'
            || head[14] != (byte)'D' || head[15] != (byte)'R')
        {
            return null;
        }

        return head[24] * (head[25] is 2 or 6 ? 3 : 1);
    }
}
