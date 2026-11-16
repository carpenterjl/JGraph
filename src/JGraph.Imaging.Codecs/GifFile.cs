using System.Buffers.Binary;

namespace JGraph.Imaging.Codecs;

/// <summary>One frame of a GIF file, as imgifinfo describes it and gifread decodes it.</summary>
/// <param name="Left">The frame's left edge, zero-based.</param>
/// <param name="Top">The frame's top edge, zero-based.</param>
/// <param name="Width">The frame's width.</param>
/// <param name="Height">The frame's height.</param>
/// <param name="Indices">The frame's indices, row by row.</param>
/// <param name="Table">The colour table the frame uses: its own, the file's, or black and white.</param>
/// <param name="TableKind">1 the file's (global) table, 2 the frame's own (local), 3 the default.</param>
/// <param name="BitDepth">The bits of the table the frame uses.</param>
/// <param name="Transparent">The transparent index, or null when the frame has none.</param>
/// <param name="Disposal">The disposal method of the frame's graphic control block, or null without one.</param>
public sealed record GifFrame(
    int Left, int Top, int Width, int Height, byte[] Indices, double[,] Table, int TableKind, int BitDepth,
    int? Transparent, int? Disposal);

/// <summary>
/// GIF files read as R2025b's readgif reads them (open item 344, ADR 0380): the frames' indices and the
/// first frame's colour table, a later frame composed over the earlier ones by its disposal method and
/// transparent index, and recast onto the first frame's table when the frames' tables differ.
/// </summary>
/// <remarks>
/// readgif.m and imgifinfo.m are transcribed: the canvas is the frames' bounding box; the first frame is
/// placed as it is; a later frame's disposal method (none written means RestorePrevious) says what its
/// transparent pixels show (the background index when the file has a global table, the previous
/// frame, or the last frame not disposed). When not every frame uses the global table (or not every
/// one the default table), a frame is painted through its own table and matched back onto the first
/// frame's, where R2025b calls rgb2ind; the nearest colour is taken here (the first of equals), which
/// is rgb2ind's answer for a colour the table holds and not measured for one it does not.
/// </remarks>
public static class GifFile
{
    /// <summary>True when the bytes begin with a GIF signature.</summary>
    public static bool IsGif(byte[] bytes) =>
        bytes is { Length: >= 6 } && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F';

    /// <summary>The file's frames, with the background index when the file has a global table.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a GIF or hold no frame.</exception>
    public static (IReadOnlyList<GifFrame> Frames, int? Background) Parse(byte[] bytes)
    {
        if (!IsGif(bytes) || bytes.Length < 13)
        {
            throw new InvalidDataException("Not a GIF file.");
        }

        int packed = bytes[10];
        bool hasGlobal = (packed & 0x80) != 0;
        int globalBits = (packed & 7) + 1;
        int at = 13;
        double[,]? global = null;
        if (hasGlobal)
        {
            global = Table(bytes, ref at, globalBits);
        }

        int? background = hasGlobal ? bytes[11] : null;
        var frames = new List<GifFrame>();
        int? pendingTransparent = null;
        int? pendingDisposal = null;
        while (at < bytes.Length)
        {
            int separator = bytes[at++];
            if (separator == 0x3B)
            {
                break;
            }

            if (separator == 0x21)
            {
                if (at >= bytes.Length)
                {
                    break;
                }

                int label = bytes[at++];
                if (label == 0xF9 && at + 5 <= bytes.Length)
                {
                    int flags = bytes[at + 1];
                    pendingDisposal = (flags >> 2) & 7;
                    pendingTransparent = (flags & 1) != 0 ? bytes[at + 4] : null;
                }

                SkipBlocks(bytes, ref at);
                continue;
            }

            if (separator != 0x2C || at + 9 > bytes.Length)
            {
                break;
            }

            int left = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            int top = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 2));
            int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 4));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 6));
            int local = bytes[at + 8];
            at += 9;
            double[,] table;
            int kind;
            int bits;
            if ((local & 0x80) != 0)
            {
                bits = (local & 7) + 1;
                table = Table(bytes, ref at, bits);
                kind = 2;
            }
            else if (global is not null)
            {
                bits = globalBits;
                table = global;
                kind = 1;
            }
            else
            {
                bits = 1;
                table = new double[,] { { 0, 0, 0 }, { 1, 1, 1 } };
                kind = 3;
            }

            int minimum = at < bytes.Length ? bytes[at++] : 2;
            var data = new List<byte>();
            while (at < bytes.Length)
            {
                int size = bytes[at++];
                if (size == 0)
                {
                    break;
                }

                int take = Math.Min(size, bytes.Length - at);
                data.AddRange(bytes.AsSpan(at, take).ToArray());
                at += take;
            }

            byte[] indices = Lzw([.. data], minimum, width * height);
            if ((local & 0x40) != 0)
            {
                indices = Deinterlace(indices, width, height);
            }

            frames.Add(new GifFrame(left, top, width, height, indices, table, kind, bits, pendingTransparent, pendingDisposal));
            pendingTransparent = null;
            pendingDisposal = null;
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("The GIF file holds no image.");
        }

        return (frames, background);
    }

    /// <summary>
    /// readgif's answer for one frame (zero-based): the composed canvas of indices, as uint8, and the
    /// first frame's colour table.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The frame is past the file's last.</exception>
    public static (ImageBuffer Indices, double[,] Map) Read(byte[] bytes, int frame)
    {
        (IReadOnlyList<GifFrame> frames, int? background) = Parse(bytes);
        if (frame < 0 || frame >= frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), frame, $"The file has {frames.Count} frame(s).");
        }

        int height = frames.Max(static f => f.Top + f.Height);
        int width = frames.Max(static f => f.Left + f.Width);
        double[,] map = frames[0].Table;
        bool recast = !(frames.All(static f => f.TableKind == 1) || frames.All(static f => f.TableKind == 3));

        byte[] previous = new byte[height * width];
        Place(previous, width, frames[0], frames[0].Indices);
        byte[] undisposed = previous;
        for (int j = 1; j <= frame; j++)
        {
            GifFrame current = frames[j];
            byte[] canvas = new byte[height * width];
            Place(canvas, width, current, current.Indices);
            bool[]? transparent = null;
            if (current.Transparent is int t)
            {
                transparent = new bool[canvas.Length];
                for (int p = 0; p < canvas.Length; p++)
                {
                    transparent[p] = canvas[p] == t;
                }
            }

            if (recast)
            {
                Recast(canvas, current.Table, map);
            }

            int disposal = current.Disposal is int d and <= 3 ? d : 3;
            byte[] next = (byte[])previous.Clone();
            if (transparent is not null)
            {
                for (int p = 0; p < canvas.Length; p++)
                {
                    if (!transparent[p])
                    {
                        continue;
                    }

                    switch (disposal)
                    {
                        case 0:
                            canvas[p] = background is int b0 ? (byte)b0 : previous[p];
                            break;
                        case 1:
                            canvas[p] = previous[p];
                            break;
                        case 2:
                            if (background is int b2)
                            {
                                canvas[p] = (byte)b2;
                            }

                            break;
                        default:
                            canvas[p] = undisposed[p];
                            break;
                    }
                }
            }

            for (int r = current.Top; r < current.Top + current.Height; r++)
            {
                Array.Copy(canvas, (r * width) + current.Left, next, (r * width) + current.Left, current.Width);
            }

            if (disposal is 0 or 1)
            {
                undisposed = next;
            }

            previous = next;
        }

        var image = new ImageBuffer(height, width, 1) { Class = ImageClass.UInt8 };
        Span<double> pixels = image.Pixels;
        for (int p = 0; p < previous.Length; p++)
        {
            pixels[p] = previous[p] / 255.0;
        }

        GC.KeepAlive(image);
        return (image, map);
    }

    private static void Place(byte[] canvas, int canvasWidth, GifFrame frame, byte[] indices)
    {
        for (int r = 0; r < frame.Height; r++)
        {
            Array.Copy(indices, r * frame.Width, canvas, ((frame.Top + r) * canvasWidth) + frame.Left, frame.Width);
        }
    }

    /// <summary>
    /// <c>rgb2ind(ind2rgb(X, table), map)</c> for colours the map holds: each index painted through its
    /// table (clamped to the table's last row) and matched to the nearest map entry, the first of equals.
    /// </summary>
    private static void Recast(byte[] canvas, double[,] table, double[,] map)
    {
        var memo = new int[256];
        Array.Fill(memo, -1);
        int last = table.GetLength(0) - 1;
        for (int p = 0; p < canvas.Length; p++)
        {
            int index = canvas[p];
            if (memo[index] < 0)
            {
                int row = Math.Min(index, last);
                int best = 0;
                double bestDistance = double.MaxValue;
                for (int m = 0; m < map.GetLength(0); m++)
                {
                    double dr = map[m, 0] - table[row, 0];
                    double dg = map[m, 1] - table[row, 1];
                    double db = map[m, 2] - table[row, 2];
                    double distance = (dr * dr) + (dg * dg) + (db * db);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = m;
                    }
                }

                memo[index] = best;
            }

            canvas[p] = (byte)memo[index];
        }
    }

    private static double[,] Table(byte[] bytes, ref int at, int bits)
    {
        int entries = 1 << bits;
        var table = new double[entries, 3];
        for (int i = 0; i < entries; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                int position = at + (3 * i) + c;
                table[i, c] = position < bytes.Length ? bytes[position] / 255.0 : 0;
            }
        }

        at += 3 * entries;
        return table;
    }

    private static void SkipBlocks(byte[] bytes, ref int at)
    {
        while (at < bytes.Length)
        {
            int size = bytes[at++];
            if (size == 0)
            {
                return;
            }

            at += size;
        }
    }

    /// <summary>A frame's rows in their display order, from the four passes an interlaced frame stores.</summary>
    private static byte[] Deinterlace(byte[] indices, int width, int height)
    {
        var rows = new byte[indices.Length];
        int source = 0;
        foreach ((int start, int step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (int r = start; r < height; r += step)
            {
                Array.Copy(indices, source * width, rows, r * width, width);
                source++;
            }
        }

        return rows;
    }

    /// <summary>GIF's variable-width LZW decoded to <paramref name="count"/> indices (zeros past the data).</summary>
    private static byte[] Lzw(byte[] data, int minimum, int count)
    {
        var output = new byte[count];
        minimum = Math.Clamp(minimum, 2, 11);
        int clear = 1 << minimum;
        int end = clear + 1;
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];
        for (int code = 0; code < clear; code++)
        {
            suffix[code] = (byte)code;
            first[code] = (byte)code;
            prefix[code] = -1;
        }

        var stack = new byte[4097];
        int width = minimum + 1;
        int next = end + 1;
        int previous = -1;
        int written = 0;
        long bitBuffer = 0;
        int bitCount = 0;
        int at = 0;
        while (written < count)
        {
            while (bitCount < width && at < data.Length)
            {
                bitBuffer |= (long)data[at++] << bitCount;
                bitCount += 8;
            }

            if (bitCount < width)
            {
                break;
            }

            int code = (int)(bitBuffer & ((1 << width) - 1));
            bitBuffer >>= width;
            bitCount -= width;
            if (code == clear)
            {
                width = minimum + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
            {
                break;
            }

            int depth = 0;
            int walk;
            if (previous < 0)
            {
                if (code >= clear)
                {
                    break;
                }

                output[written++] = (byte)code;
                previous = code;
                continue;
            }

            if (code < next)
            {
                walk = code;
            }
            else if (code == next)
            {
                // The code being defined now: the previous string plus its own first byte.
                stack[depth++] = first[previous];
                walk = previous;
            }
            else
            {
                break;
            }

            while (walk >= 0 && depth < stack.Length)
            {
                stack[depth++] = suffix[walk];
                walk = prefix[walk];
            }

            byte head = stack[depth - 1];
            while (depth > 0 && written < count)
            {
                output[written++] = stack[--depth];
            }

            if (next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = head;
                first[next] = first[previous];
                next++;
                if (next == 1 << width && width < 12)
                {
                    width++;
                }
            }

            previous = code;
        }

        return output;
    }
}
