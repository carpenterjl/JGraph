namespace JGraph.Deploy;

/// <summary>One image of an icon: its bytes as an <c>.ico</c> file holds them (a PNG, or a bitmap) and what its directory entry says.</summary>
/// <param name="Data">The image's bytes.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="ColorCount">The directory's colour count, 0 for 256 or more.</param>
/// <param name="Planes">The directory's plane count.</param>
/// <param name="BitCount">The directory's bits per pixel.</param>
public sealed record IconImage(byte[] Data, int Width, int Height, int ColorCount, int Planes, int BitCount)
{
    /// <summary>A PNG file as an icon image: its size from its header, 32 bits per pixel.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a PNG file.</exception>
    public static IconImage FromPng(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 24 || png[0] != 0x89 || png[1] != (byte)'P' || png[12] != (byte)'I' || png[13] != (byte)'H')
        {
            throw new InvalidDataException("The image is not a PNG file.");
        }

        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return new IconImage(png, width, height, 0, 1, 32);
    }
}

/// <summary>
/// The <c>.ico</c> file format: a directory of images, each a PNG or a bitmap. <c>mcc -r</c> takes
/// one; compiler.build writes one from its <c>ExecutableIcon</c> at 256, 128, 48, 32, 24 and 16
/// pixels, as R2025b's <c>processIcon</c> does, but with a 256-pixel image's size written as 0, which
/// is what the format means by it (R2025b's <c>makeico</c> writes 255).
/// </summary>
public static class IconFile
{
    /// <summary>The sizes compiler.build makes an icon at, largest first (R2025b's processIcon on Windows).</summary>
    public static readonly int[] BuildSizes = [256, 128, 48, 32, 24, 16];

    /// <summary>The images of the icon file at <paramref name="path"/>.</summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidDataException">The file is not an icon.</exception>
    public static IReadOnlyList<IconImage> Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 6 || BitConverter.ToUInt16(bytes, 0) != 0 || BitConverter.ToUInt16(bytes, 2) != 1)
        {
            throw new InvalidDataException("The file is not an icon.");
        }

        int count = BitConverter.ToUInt16(bytes, 4);
        if (count == 0 || bytes.Length < 6 + 16 * count)
        {
            throw new InvalidDataException("The file is not an icon.");
        }

        var images = new List<IconImage>(count);
        for (int i = 0; i < count; i++)
        {
            int at = 6 + 16 * i;
            long size = BitConverter.ToUInt32(bytes, at + 8);
            long offset = BitConverter.ToUInt32(bytes, at + 12);
            if (size == 0 || offset + size > bytes.Length)
            {
                throw new InvalidDataException("The file is not an icon.");
            }

            images.Add(new IconImage(
                bytes.AsSpan((int)offset, (int)size).ToArray(),
                bytes[at] == 0 ? 256 : bytes[at],
                bytes[at + 1] == 0 ? 256 : bytes[at + 1],
                bytes[at + 2],
                BitConverter.ToUInt16(bytes, at + 4),
                BitConverter.ToUInt16(bytes, at + 6)));
        }

        return images;
    }

    /// <summary>An icon file of <paramref name="images"/>, in their order.</summary>
    public static byte[] Write(IReadOnlyList<IconImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        using var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (IconImage image in images)
        {
            writer.Write((byte)(image.Width >= 256 ? 0 : image.Width));
            writer.Write((byte)(image.Height >= 256 ? 0 : image.Height));
            writer.Write((byte)image.ColorCount);
            writer.Write((byte)0);
            writer.Write((ushort)image.Planes);
            writer.Write((ushort)image.BitCount);
            writer.Write((uint)image.Data.Length);
            writer.Write((uint)offset);
            offset += image.Data.Length;
        }

        foreach (IconImage image in images)
        {
            writer.Write(image.Data);
        }

        writer.Flush();
        return buffer.ToArray();
    }
}
