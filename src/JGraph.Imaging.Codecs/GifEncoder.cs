using System.Buffers.Binary;
using JGraph.Imaging;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// A GIF writer, including the animated form (M72).
/// </summary>
/// <remarks>
/// <para>
/// Skia decodes GIF and does not encode it, so this is written out here rather than handed to it.
/// That is worth the two hundred lines: <c>imwrite(A, 'frames.gif', 'WriteMode', 'append')</c> in a
/// loop is how a MATLAB script saves an animation, and without a GIF encoder there was no way at all
/// to get a moving picture out of a script — <c>getframe</c> and <c>movie</c> could play one on the
/// screen and nothing could keep it.
/// </para>
/// <para>
/// Appending writes a whole frame — its own colour table and all — over the file's trailing
/// terminator byte and puts the terminator back. A GIF is a stream of self-describing blocks, so a
/// frame added that way is indistinguishable from one written in the first pass, and a script that
/// builds a hundred-frame animation never holds more than one frame in memory.
/// </para>
/// </remarks>
public static class GifEncoder
{
    private const byte Terminator = 0x3B;
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte GraphicControlLabel = 0xF9;
    private const byte ApplicationLabel = 0xFF;

    /// <summary>Writes <paramref name="image"/> as a new single-frame GIF, replacing any existing file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="image">The picture to write.</param>
    /// <param name="palette">
    /// An explicit colour table as MATLAB's <c>map</c> — an n-by-3 matrix of red, green and blue in
    /// [0, 1], at most 256 rows — or null to choose one from the picture's own colours.
    /// </param>
    /// <param name="delaySeconds">How long the frame is shown; only meaningful once more are appended.</param>
    /// <param name="loopCount">0 loops forever, which is what MATLAB's <c>Inf</c> means; n loops n times.</param>
    public static void Write(
        string path,
        ImageBuffer image,
        double[,]? palette = null,
        double delaySeconds = 0.5,
        int loopCount = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);

        (byte[] indices, byte[] table, int bits) = Quantize(image, palette);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);

        // Header and logical screen descriptor. The global table is this frame's, which later frames
        // do not have to share because each carries its own.
        file.Write("GIF89a"u8);
        WriteUInt16(file, image.Width);
        WriteUInt16(file, image.Height);
        file.WriteByte((byte)(0x80 | ((bits - 1) << 4) | (bits - 1))); // global table, this deep
        file.WriteByte(0); // background colour index
        file.WriteByte(0); // no pixel aspect ratio
        file.Write(table);

        // The Netscape application extension is the only place a GIF can say how often to repeat.
        file.WriteByte(ExtensionIntroducer);
        file.WriteByte(ApplicationLabel);
        file.WriteByte(11);
        file.Write("NETSCAPE2.0"u8);
        file.WriteByte(3);
        file.WriteByte(1);
        WriteUInt16(file, loopCount);
        file.WriteByte(0);

        WriteFrame(file, image.Width, image.Height, indices, table, bits, delaySeconds, localTable: false);
        file.WriteByte(Terminator);
    }

    /// <summary>
    /// Adds a frame to a GIF already on disk, over its trailing terminator. The file must have been
    /// written by <see cref="Write"/> (or be any well-formed GIF) and the frame must be the same size.
    /// </summary>
    public static void Append(string path, ImageBuffer image, double[,]? palette, double delaySeconds)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(image);

        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        if (file.Length < 14)
        {
            throw new InvalidDataException($"'{path}' is too short to be a GIF.");
        }

        // The size a viewer will show is fixed by the first frame, so a later frame of another size
        // would be silently cropped. Saying so is better than writing a file that plays wrong.
        file.Position = 6;
        Span<byte> screen = stackalloc byte[4];
        file.ReadExactly(screen);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(screen);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(screen[2..]);
        if (width != image.Width || height != image.Height)
        {
            throw new ArgumentException(
                $"an appended frame has to be {width}x{height}, the size of the first one, "
                + $"but this one is {image.Width}x{image.Height}.",
                nameof(image));
        }

        // Drop the terminator and write the new frame where it stood.
        file.Position = file.Length - 1;
        int last = file.ReadByte();
        file.Position = last == Terminator ? file.Length - 1 : file.Length;

        (byte[] indices, byte[] table, int bits) = Quantize(image, palette);
        WriteFrame(file, image.Width, image.Height, indices, table, bits, delaySeconds, localTable: true);
        file.WriteByte(Terminator);
        file.SetLength(file.Position);
    }

    /// <summary>One frame: how long to hold it, where it sits, its colours, and the compressed pixels.</summary>
    private static void WriteFrame(
        Stream file,
        int width,
        int height,
        byte[] indices,
        byte[] table,
        int bits,
        double delaySeconds,
        bool localTable)
    {
        // The delay is in hundredths of a second. Zero means 'as fast as possible', which most
        // viewers quietly turn into a tenth, so a frame asked to be brief is held to one hundredth.
        int hundredths = Math.Clamp((int)Math.Round(delaySeconds * 100), 1, 65535);
        file.WriteByte(ExtensionIntroducer);
        file.WriteByte(GraphicControlLabel);
        file.WriteByte(4);
        file.WriteByte(0x04); // dispose by restoring the background, no transparency
        WriteUInt16(file, hundredths);
        file.WriteByte(0); // transparent colour index, unused
        file.WriteByte(0);

        file.WriteByte(ImageSeparator);
        WriteUInt16(file, 0);
        WriteUInt16(file, 0);
        WriteUInt16(file, width);
        WriteUInt16(file, height);
        file.WriteByte(localTable ? (byte)(0x80 | (bits - 1)) : (byte)0x00);
        if (localTable)
        {
            file.Write(table);
        }

        WriteLzw(file, indices, bits);
    }

    /// <summary>
    /// The picture as one palette index per pixel, plus the table those indices point into and how
    /// many bits wide it is. A given map is used as written; otherwise the colours present are counted
    /// and, if there are more than 256, cut down on a uniform 6x6x6 cube plus a grey ramp.
    /// </summary>
    private static (byte[] Indices, byte[] Table, int Bits) Quantize(ImageBuffer image, double[,]? palette)
    {
        int count = image.Width * image.Height;
        var indices = new byte[count];

        if (palette is not null)
        {
            int entries = palette.GetLength(0);
            if (entries is < 1 or > 256 || palette.GetLength(1) != 3)
            {
                throw new ArgumentException(
                    "a GIF colour map is an n-by-3 matrix of red, green and blue in [0, 1], with at most "
                    + $"256 rows; this one is {entries}x{palette.GetLength(1)}.",
                    nameof(palette));
            }

            var chosen = new byte[entries * 3];
            for (int i = 0; i < entries; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    chosen[(i * 3) + c] = Level(palette[i, c]);
                }
            }

            // With a map given, a single-channel picture is already indices into it -- which is what
            // MATLAB's imwrite(X, map, ...) means -- and a colour one is matched to the nearest entry.
            if (image.Channels == 1)
            {
                ReadOnlySpan<double> samples = image.Pixels;
                double scale = image.Class == ImageClass.Double ? entries - 1 : image.Class.Scale();
                for (int i = 0; i < count; i++)
                {
                    indices[i] = (byte)Math.Clamp((int)Math.Round(samples[i] * scale), 0, entries - 1);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    indices[i] = Nearest(chosen, entries, Rgb(image, i));
                }
            }

            return (indices, Pad(chosen, entries, out int given), given);
        }

        // No map: gather the distinct colours. Most figures have far fewer than 256, and an exact
        // table for those is both smaller and truer than any quantization of them.
        var seen = new Dictionary<int, byte>(256);
        var exact = new List<byte>(768);
        bool fits = true;
        for (int i = 0; i < count && fits; i++)
        {
            (byte r, byte g, byte b) = Rgb(image, i);
            int key = (r << 16) | (g << 8) | b;
            if (seen.TryGetValue(key, out byte at))
            {
                indices[i] = at;
                continue;
            }

            if (seen.Count == 256)
            {
                fits = false;
                break;
            }

            at = (byte)seen.Count;
            seen[key] = at;
            exact.Add(r);
            exact.Add(g);
            exact.Add(b);
            indices[i] = at;
        }

        if (fits)
        {
            return (indices, Pad([.. exact], seen.Count, out int used), used);
        }

        // More than 256 distinct colours: the web cube for hue, and a grey ramp in what is left, which
        // is where a photograph's shadows and a figure's antialiased edges both live.
        var cube = new byte[256 * 3];
        int slot = 0;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    cube[slot++] = (byte)(r * 51);
                    cube[slot++] = (byte)(g * 51);
                    cube[slot++] = (byte)(b * 51);
                }
            }
        }

        for (int grey = 0; slot < 256 * 3; grey++)
        {
            byte level = (byte)Math.Clamp((grey * 255) / 39, 0, 255);
            cube[slot++] = level;
            cube[slot++] = level;
            cube[slot++] = level;
        }

        for (int i = 0; i < count; i++)
        {
            indices[i] = Nearest(cube, 256, Rgb(image, i));
        }

        return (indices, cube, 8);
    }

    /// <summary>One pixel as bytes, whatever the picture's channel count and class.</summary>
    private static (byte R, byte G, byte B) Rgb(ImageBuffer image, int pixel)
    {
        ReadOnlySpan<double> samples = image.Pixels;
        int plane = image.Width * image.Height;
        if (image.Channels == 1)
        {
            byte grey = Level(samples[pixel]);
            return (grey, grey, grey);
        }

        return (Level(samples[pixel]), Level(samples[plane + pixel]), Level(samples[(2 * plane) + pixel]));
    }

    private static byte Level(double sample) => (byte)Math.Clamp((int)Math.Round(sample * 255), 0, 255);

    /// <summary>The nearest table entry to one colour, by squared distance in RGB.</summary>
    private static byte Nearest(byte[] table, int entries, (byte R, byte G, byte B) colour)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < entries; i++)
        {
            int dr = table[i * 3] - colour.R;
            int dg = table[(i * 3) + 1] - colour.G;
            int db = table[(i * 3) + 2] - colour.B;
            int distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
                if (distance == 0)
                {
                    break;
                }
            }
        }

        return (byte)best;
    }

    /// <summary>
    /// A colour table padded out to the power of two a GIF requires, answering how many bits index it.
    /// A table of one colour still needs two entries, because the smallest code size a GIF allows is 2.
    /// </summary>
    private static byte[] Pad(byte[] table, int entries, out int bits)
    {
        bits = 1;
        while (1 << bits < Math.Max(entries, 2))
        {
            bits++;
        }

        bits = Math.Clamp(bits, 2, 8);
        var padded = new byte[(1 << bits) * 3];
        Array.Copy(table, padded, Math.Min(table.Length, padded.Length));
        return padded;
    }

    /// <summary>
    /// GIF's variable-width LZW, written out in the sub-blocks of at most 255 bytes the format wants.
    /// The dictionary starts at the table size and grows a bit at a time to twelve, and is cleared and
    /// begun again whenever it fills — which is the whole of the algorithm.
    /// </summary>
    private static void WriteLzw(Stream file, byte[] indices, int bits)
    {
        int minimumCode = Math.Max(bits, 2);
        int clear = 1 << minimumCode;
        int end = clear + 1;
        file.WriteByte((byte)minimumCode);

        var packer = new BlockPacker(file);
        var dictionary = new Dictionary<long, int>(4096);
        int next = end + 1;
        int width = minimumCode + 1;

        packer.Write(clear, width);
        if (indices.Length == 0)
        {
            packer.Write(end, width);
            packer.Flush();
            return;
        }

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int symbol = indices[i];
            long key = ((long)prefix << 8) | (uint)symbol;
            if (dictionary.TryGetValue(key, out int found))
            {
                prefix = found;
                continue;
            }

            packer.Write(prefix, width);
            if (next < 4096)
            {
                dictionary[key] = next++;
                if (next > 1 << width && width < 12)
                {
                    width++;
                }
            }
            else
            {
                packer.Write(clear, width);
                dictionary.Clear();
                next = end + 1;
                width = minimumCode + 1;
            }

            prefix = symbol;
        }

        packer.Write(prefix, width);
        packer.Write(end, width);
        packer.Flush();
    }

    private static void WriteUInt16(Stream file, int value)
    {
        Span<byte> pair = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(pair, (ushort)value);
        file.Write(pair);
    }

    /// <summary>
    /// Codes packed least-significant-bit first into GIF's length-prefixed sub-blocks. The two rules
    /// are inseparable — a code may straddle a byte and a byte may not straddle a block — so one type
    /// holds both.
    /// </summary>
    private sealed class BlockPacker(Stream file)
    {
        private readonly byte[] _block = new byte[255];
        private int _filled;
        private int _bits;
        private uint _accumulator;

        public void Write(int code, int width)
        {
            _accumulator |= (uint)code << _bits;
            _bits += width;
            while (_bits >= 8)
            {
                Emit((byte)(_accumulator & 0xFF));
                _accumulator >>= 8;
                _bits -= 8;
            }
        }

        public void Flush()
        {
            if (_bits > 0)
            {
                Emit((byte)(_accumulator & 0xFF));
                _accumulator = 0;
                _bits = 0;
            }

            EmitBlock();
            file.WriteByte(0); // the empty block that ends the image data
        }

        private void Emit(byte value)
        {
            _block[_filled++] = value;
            if (_filled == _block.Length)
            {
                EmitBlock();
            }
        }

        private void EmitBlock()
        {
            if (_filled == 0)
            {
                return;
            }

            file.WriteByte((byte)_filled);
            file.Write(_block, 0, _filled);
            _filled = 0;
        }
    }
}
