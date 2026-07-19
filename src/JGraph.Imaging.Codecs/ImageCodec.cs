using SkiaSharp;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// Decodes and encodes raster image files (PNG/JPEG/BMP) to and from <see cref="ImageBuffer"/>,
/// bridging SkiaSharp's byte-per-channel world to JGraph's [0, 1] double samples. This is the only
/// image-processing type that touches a native codec; the algorithms in <see cref="JGraph.Imaging"/>
/// stay codec-free.
/// </summary>
public static class ImageCodec
{
    /// <summary>
    /// Reads an image file. A file whose pixels are all neutral gray (R == G == B everywhere)
    /// decodes to a one-channel image, matching MATLAB's <c>imread</c> of a grayscale file; any
    /// alpha channel is dropped.
    /// </summary>
    /// <exception cref="IOException">The file is missing or cannot be opened.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a decodable image.</exception>
    public static ImageBuffer Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Open the stream ourselves so a missing/locked file raises a real IOException; SKBitmap.Decode
        // merely returns null for undecodable bytes, which we translate to InvalidDataException.
        using FileStream stream = File.OpenRead(path);
        using SKBitmap? bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
        {
            throw new InvalidDataException($"'{path}' is not a supported or valid image file.");
        }

        int width = bitmap.Width;
        int height = bitmap.Height;
        SKColor[] source = bitmap.Pixels; // row-major, top row first, straight (unpremultiplied) alpha

        bool grayscale = true;
        for (int i = 0; i < source.Length; i++)
        {
            SKColor color = source[i];
            if (color.Red != color.Green || color.Green != color.Blue)
            {
                grayscale = false;
                break;
            }
        }

        var image = new ImageBuffer(height, width, grayscale ? 1 : 3);
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
        return image;
    }

    /// <summary>
    /// Writes an image file. The format is chosen from the extension: <c>.png</c>, <c>.jpg</c>/
    /// <c>.jpeg</c>, or <c>.bmp</c>. Samples are clamped to [0, 1] and quantized to bytes.
    /// </summary>
    /// <param name="path">Destination path; its extension selects the format.</param>
    /// <param name="image">The image to encode.</param>
    /// <param name="jpegQuality">JPEG quality 0–100 (default 95); ignored for lossless formats.</param>
    /// <exception cref="ArgumentException">The extension is not a supported format.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="InvalidDataException">Encoding failed.</exception>
    public static void Write(string path, ImageBuffer image, int? jpegQuality = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);

        string extension = Path.GetExtension(path).ToLowerInvariant();
        SKEncodedImageFormat format = extension switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => throw new ArgumentException(
                $"unsupported image extension '{extension}' (use .png, .jpg/.jpeg, or .bmp)", nameof(path)),
        };

        int quality = Math.Clamp(jpegQuality ?? 95, 0, 100);
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        var colors = new SKColor[image.Width * image.Height];
        ReadOnlySpan<double> pixels = image.Pixels;
        if (image.Channels == 1)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                byte v = ToByte(pixels[i]);
                colors[i] = new SKColor(v, v, v);
            }
        }
        else
        {
            for (int i = 0; i < colors.Length; i++)
            {
                int b = i * 3;
                colors[i] = new SKColor(ToByte(pixels[b]), ToByte(pixels[b + 1]), ToByte(pixels[b + 2]));
            }
        }

        bitmap.Pixels = colors;
        GC.KeepAlive(image);

        using SKImage skImage = SKImage.FromBitmap(bitmap);
        using SKData? data = skImage.Encode(format, quality);
        if (data is null)
        {
            throw new InvalidDataException($"failed to encode image to '{path}'.");
        }

        using FileStream output = File.Create(path);
        data.SaveTo(output);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
