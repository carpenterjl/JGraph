using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// Plays back an animated PNG: the reading half of <see cref="AnimatedPngEncoder"/>, and the only
/// way to get a moving picture with an alpha channel back out of a file here.
/// </summary>
/// <remarks>
/// <para>
/// Skia does not decode APNG — handed one, <c>SKCodec</c> reports a still image of one frame, which
/// is exactly what the format was designed to make an unaware decoder do. So the frames are taken
/// apart here and handed back to Skia one at a time: a frame's compressed data, wrapped in a header
/// of its own size, is an ordinary PNG, and decoding it is the PNG decoder's job rather than this
/// class's. Nothing here inflates anything.
/// </para>
/// <para>
/// It reads forwards and only forwards, because that is what the format is: a frame may be a patch
/// of the one before it, so the canvas is the state and <see cref="Advance"/> is the step. Seeking
/// backwards means <see cref="Rewind"/> and stepping again, which is what a loop does anyway.
/// </para>
/// </remarks>
public sealed class AnimatedPngReader : IDisposable
{
    private readonly byte[] _file;
    private readonly byte[] _header;                 // the file's IHDR payload, 13 bytes
    private readonly List<(string Name, byte[] Payload)> _preamble;
    private readonly List<Frame> _frames;
    private readonly TimeSpan[] _elapsed;            // time to the end of frame k-1, so _elapsed[0] is zero
    private readonly byte[] _canvas;
    private byte[]? _saved;                          // for APNG_DISPOSE_OP_PREVIOUS
    private int _index = -1;
    private bool _disposed;

    private AnimatedPngReader(
        byte[] file,
        byte[] header,
        List<(string, byte[])> preamble,
        List<Frame> frames,
        int width,
        int height,
        int playCount)
    {
        _file = file;
        _header = header;
        _preamble = preamble;
        _frames = frames;
        Width = width;
        Height = height;
        PlayCount = playCount;
        _canvas = new byte[(long)width * height * 4];

        _elapsed = new TimeSpan[frames.Count + 1];
        for (int k = 0; k < frames.Count; k++)
        {
            _elapsed[k + 1] = _elapsed[k] + frames[k].Delay;
        }
    }

    /// <summary>The canvas width in pixels. Every frame composites onto a canvas this size.</summary>
    public int Width { get; }

    /// <summary>The canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>How many frames the animation has.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>How many times the file asks to be played through. Zero means for ever.</summary>
    public int PlayCount { get; }

    /// <summary>The frame now on the canvas, or -1 before the first <see cref="Advance"/>.</summary>
    public int FrameIndex => _index;

    /// <summary>How long the frame now on the canvas should be shown for.</summary>
    public TimeSpan Delay => _index < 0 ? TimeSpan.Zero : _frames[_index].Delay;

    /// <summary>How long one pass through every frame lasts, by the delays the file asks for.</summary>
    public TimeSpan Duration => _elapsed[^1];

    /// <summary>
    /// How far into a pass the canvas stands: the delays of every frame already shown, so it is
    /// zero before the first <see cref="Advance"/> and <see cref="Duration"/> at the end of the
    /// last frame. <c>Duration - Elapsed</c> is what is left to play.
    /// </summary>
    public TimeSpan Elapsed => _elapsed[_index + 1];

    /// <summary>
    /// The canvas, in straight (not premultiplied) RGBA, row by row from the top. Valid until the
    /// next <see cref="Advance"/>, which draws over it.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _canvas;

    /// <summary>Reads a file's frame table. The bytes are held; nothing is decoded yet.</summary>
    /// <param name="path">The animated PNG to open.</param>
    public static AnimatedPngReader Open(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Reads a frame table from bytes already in hand.</summary>
    /// <param name="file">The whole file. It is kept, not copied.</param>
    /// <exception cref="InvalidDataException">The bytes are not an animated PNG.</exception>
    public static AnimatedPngReader Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < PngChunks.Signature.Length
            || !file.AsSpan(0, PngChunks.Signature.Length).SequenceEqual(PngChunks.Signature))
        {
            throw new InvalidDataException("that file does not begin with a PNG signature.");
        }

        byte[]? header = null;
        var preamble = new List<(string, byte[])>();
        var frames = new List<Frame>();
        int playCount = 0;
        bool animated = false;
        bool seenFrameData = false;

        int at = PngChunks.Signature.Length;
        while (at + 12 <= file.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            if (length > int.MaxValue || at + 12L + length > file.Length)
            {
                throw new InvalidDataException("a PNG chunk runs past the end of the file.");
            }

            string name = Encoding.ASCII.GetString(file, at + 4, 4);
            int payload = at + 8;
            int count = (int)length;

            switch (name)
            {
                case "IHDR":
                    header = file.AsSpan(payload, count).ToArray();
                    break;

                case "acTL":
                    animated = true;
                    playCount = count >= 8
                        ? (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(payload + 4)))
                        : 0;
                    break;

                case "fcTL":
                    frames.Add(Frame.Parse(file.AsSpan(payload, count)));
                    break;

                case "IDAT":
                    // The default image joins the animation only if a frame control came first;
                    // otherwise it is the still an unaware decoder shows and no frame at all.
                    if (frames.Count > 0 && !seenFrameData)
                    {
                        frames[0].Data.Add((payload, count));
                    }
                    else if (frames.Count == 0)
                    {
                        seenFrameData = true;
                    }

                    break;

                case "fdAT" when frames.Count > 0 && count >= 4:
                    seenFrameData = true;
                    frames[^1].Data.Add((payload + 4, count - 4));
                    break;

                case "IEND":
                    at = file.Length;
                    continue;

                default:
                    if (frames.Count == 0 && !seenFrameData && header is not null)
                    {
                        // Palettes, transparency tables, colour spaces: whatever a frame's own
                        // decode will need, carried across into every rebuilt frame.
                        preamble.Add((name, file.AsSpan(payload, count).ToArray()));
                    }

                    break;
            }

            at = payload + count + 4;
        }

        if (header is null)
        {
            throw new InvalidDataException("that PNG has no header chunk.");
        }

        if (!animated || frames.Count == 0)
        {
            throw new InvalidDataException("that PNG is not animated — it has no frame table.");
        }

        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        return new AnimatedPngReader(file, header, preamble, frames, width, height, playCount);
    }

    /// <summary>
    /// Composites the next frame onto the canvas and returns true, or returns false at the end of
    /// the animation, leaving the last frame where it is. <see cref="Rewind"/> starts it over.
    /// </summary>
    public bool Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_index + 1 >= _frames.Count)
        {
            return false;
        }

        // A frame's dispose op says what to do with the canvas *after* it has been shown, so it is
        // paid here, on the way to the next one.
        if (_index >= 0)
        {
            Frame previous = _frames[_index];
            switch (previous.Dispose)
            {
                case 1:
                    ClearRegion(previous);
                    break;
                case 2 when _saved is not null:
                    _saved.CopyTo(_canvas.AsSpan());
                    break;
                default:
                    break;
            }
        }

        _index++;
        Frame frame = _frames[_index];
        if (frame.Dispose == 2)
        {
            _saved ??= new byte[_canvas.Length];
            _canvas.CopyTo(_saved.AsSpan());
        }

        Composite(frame, Decode(frame));
        return true;
    }

    /// <summary>Clears the canvas and puts the animation back before its first frame.</summary>
    public void Rewind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(_canvas);
        _index = -1;
    }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private byte[] Decode(Frame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap? bitmap = SKBitmap.Decode(Rebuild(frame), info)
            ?? throw new InvalidDataException($"frame {_index} of that animated PNG could not be decoded.");
        return bitmap.GetPixelSpan().ToArray();
    }

    /// <summary>
    /// One frame as a PNG in its own right: the file's header with this frame's size written into
    /// it, everything the file said before the first frame, and this frame's data as <c>IDAT</c>.
    /// </summary>
    private byte[] Rebuild(Frame frame)
    {
        var header = (byte[])_header.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)frame.Height);

        using var png = new MemoryStream();
        png.Write(PngChunks.Signature);
        PngChunks.Write(png, "IHDR", header);
        foreach ((string name, byte[] payload) in _preamble)
        {
            PngChunks.Write(png, name, payload);
        }

        // The pieces are one zlib stream cut up, so they are joined before being labelled — a
        // decoder is entitled to assume each IDAT it is given is whole.
        int total = 0;
        foreach ((int _, int length) in frame.Data)
        {
            total += length;
        }

        var data = new byte[total];
        int to = 0;
        foreach ((int offset, int length) in frame.Data)
        {
            _file.AsSpan(offset, length).CopyTo(data.AsSpan(to));
            to += length;
        }

        PngChunks.Write(png, "IDAT", data);
        PngChunks.Write(png, "IEND", []);
        return png.ToArray();
    }

    private void ClearRegion(Frame frame)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            int row = ((frame.Y + y) * Width + frame.X) * 4;
            Array.Clear(_canvas, row, frame.Width * 4);
        }
    }

    private void Composite(Frame frame, byte[] pixels)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            int from = y * frame.Width * 4;
            int to = ((frame.Y + y) * Width + frame.X) * 4;

            if (frame.Blend == 0)
            {
                // APNG_BLEND_OP_SOURCE: the frame replaces what is under it, transparency and all.
                // This is the one an encoder writing whole frames uses, and the only one that can
                // cut a hole in what came before.
                pixels.AsSpan(from, frame.Width * 4).CopyTo(_canvas.AsSpan(to, frame.Width * 4));
                continue;
            }

            // APNG_BLEND_OP_OVER, in straight alpha: the usual over, undone back out of the
            // premultiplied arithmetic so the canvas stays the colours a PNG stores.
            for (int x = 0; x < frame.Width; x++)
            {
                int s = from + (x * 4);
                int d = to + (x * 4);
                int sa = pixels[s + 3];
                if (sa == 255)
                {
                    pixels.AsSpan(s, 4).CopyTo(_canvas.AsSpan(d, 4));
                    continue;
                }

                if (sa == 0)
                {
                    continue;
                }

                int da = _canvas[d + 3];
                int outA = sa + (da * (255 - sa) / 255);
                for (int c = 0; c < 3; c++)
                {
                    int over = (pixels[s + c] * sa) + (_canvas[d + c] * da * (255 - sa) / 255);
                    _canvas[d + c] = (byte)(outA == 0 ? 0 : over / outA);
                }

                _canvas[d + 3] = (byte)outA;
            }
        }
    }

    /// <summary>One entry of the frame table: where a frame goes, for how long, and what it does.</summary>
    private sealed class Frame
    {
        private Frame(int width, int height, int x, int y, TimeSpan delay, byte dispose, byte blend)
        {
            Width = width;
            Height = height;
            X = x;
            Y = y;
            Delay = delay;
            Dispose = dispose;
            Blend = blend;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int X { get; }

        internal int Y { get; }

        internal TimeSpan Delay { get; }

        internal byte Dispose { get; }

        internal byte Blend { get; }

        internal List<(int Offset, int Length)> Data { get; } = [];

        internal static Frame Parse(ReadOnlySpan<byte> fc)
        {
            if (fc.Length < 26)
            {
                throw new InvalidDataException("an animated PNG's frame control chunk is too short.");
            }

            int numerator = BinaryPrimitives.ReadUInt16BigEndian(fc[20..]);
            int denominator = BinaryPrimitives.ReadUInt16BigEndian(fc[22..]);

            // The format says a zero denominator means hundredths, and says nothing about a zero
            // delay — which every player treats as "as fast as it likes". A frame that is shown for
            // no time at all is not a frame anyone asked for, so it gets the shortest sane tick.
            double seconds = numerator / (double)(denominator == 0 ? 100 : denominator);
            return new Frame(
                (int)BinaryPrimitives.ReadUInt32BigEndian(fc[4..]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(fc[8..]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(fc[12..]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(fc[16..]),
                TimeSpan.FromSeconds(seconds > 0 ? seconds : 0.01),
                fc[24],
                fc[25]);
        }
    }
}
