using SkiaSharp;
using System.Text;

namespace JGraph.Imaging.Codecs;

public static partial class ImageCodec
{
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
        using var compressed = new MemoryStream();
        using (var zip = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            for (int r = 0; r < image.Height; r++)
            {
                zip.WriteByte(0);
                for (int c = 0; c < image.Width; c++)
                {
                    for (int channel = 0; channel < Math.Min(3,image.Channels); channel++) zip.WriteByte(ToByte(image[r, c, channel]));
                    if (alpha is not null)
                        zip.WriteByte(ToByte(alpha[r, c, 0]));
                    else if (image.Channels == 4) zip.WriteByte(ToByte(image[r,c,3]));
                }
            }
        }
        PngChunks.Write(file, "IDAT", compressed.ToArray());
        PngChunks.Write(file, "IEND", []);
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
