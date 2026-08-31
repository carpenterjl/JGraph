using System.Buffers.Binary;
using System.Text;
using JGraph.Imaging.Codecs;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// The reading half of the APNG work. Skia decodes a plain PNG and stops there — handed an animated
/// one it reports a single still, which is precisely what the format asks an unaware decoder to do —
/// so everything below is asserted against frames written by <c>AnimatedPngEncoder</c> through the
/// public <see cref="VideoEncoder"/>, and the pixels are compared to the ones that went in.
/// </summary>
public class AnimatedPngReaderTests : IDisposable
{
    private const int Width = 9;
    private const int Height = 7;

    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-apng").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ReadsBackTheSizeRateAndCountThatWereWritten()
    {
        string path = WriteAnimation(5, 20);

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);

        Assert.Equal(Width, reader.Width);
        Assert.Equal(Height, reader.Height);
        Assert.Equal(5, reader.FrameCount);
        Assert.Equal(0, reader.PlayCount); // for ever
        Assert.Equal(-1, reader.FrameIndex);
    }

    /// <summary>
    /// The one that matters: every sample of every frame survives the round trip. A muxer that
    /// mislabelled a chunk or a reader that joined two of them the wrong way round would still
    /// produce a file that opens, and would still show frame one.
    /// </summary>
    [Fact]
    public void EveryFrameComesBackTheColoursItWentInAs()
    {
        var written = new List<byte[]>();
        string path = WriteAnimation(6, 25, written);

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);

        for (int k = 0; k < written.Count; k++)
        {
            Assert.True(reader.Advance());
            Assert.Equal(k, reader.FrameIndex);
            Assert.Equal(written[k], reader.Pixels.ToArray());
        }

        Assert.False(reader.Advance());
        Assert.Equal(written[^1], reader.Pixels.ToArray()); // the last frame stays up
    }

    [Fact]
    public void AFrameIsShownForTheTimeTheFrameRateAsksFor()
    {
        string path = WriteAnimation(3, 25);

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);
        Assert.True(reader.Advance());

        Assert.Equal(40, reader.Delay.TotalMilliseconds, 3);
    }

    [Fact]
    public void RewindingPlaysTheSameFramesAgain()
    {
        var written = new List<byte[]>();
        string path = WriteAnimation(4, 20, written);

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);
        while (reader.Advance())
        {
        }

        reader.Rewind();
        Assert.Equal(-1, reader.FrameIndex);
        Assert.True(reader.Advance());
        Assert.Equal(written[0], reader.Pixels.ToArray());
    }

    /// <summary>
    /// The coverage is the point of the format here, so it is asserted on its own: a frame written
    /// with a hole in it reads back with the hole, not with black.
    /// </summary>
    [Fact]
    public void ACutOutComesBackAsACutOut()
    {
        string path = Path.Combine(_folder, "hole.apng");
        var frame = new byte[Width * Height * 4];
        for (int i = 0; i < frame.Length; i += 4)
        {
            frame[i] = 200;
            frame[i + 1] = 40;
            frame[i + 2] = 90;
            frame[i + 3] = 255;
        }

        frame[3] = 0;   // the top-left pixel is covered by nothing
        frame[7] = 128; // and the one beside it by half of something

        using (IVideoEncoder encoder =
            VideoEncoder.Create(path, VideoCodec.AnimatedPng, Width, Height, 10))
        {
            encoder.WriteFrame(frame);
        }

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);
        Assert.True(reader.Advance());

        Assert.Equal(0, reader.Pixels[3]);
        Assert.Equal(128, reader.Pixels[7]);
        Assert.Equal(255, reader.Pixels[11]);
    }

    /// <summary>
    /// The splash bar is charged in the artwork's own time, so a pass has to be able to say how long
    /// it lasts and how much of it has been shown. Both are in the file's delays, not in the frame
    /// count — a file whose frames run at different lengths still has to read as time.
    /// </summary>
    [Fact]
    public void APassKnowsHowLongItLastsAndHowFarThroughItIs()
    {
        string path = WriteAnimation(4, 20); // four frames, 50 ms each

        using AnimatedPngReader reader = AnimatedPngReader.Open(path);

        Assert.Equal(TimeSpan.FromMilliseconds(200), reader.Duration);
        Assert.Equal(TimeSpan.Zero, reader.Elapsed);

        for (int k = 1; k <= 4; k++)
        {
            Assert.True(reader.Advance());
            Assert.Equal(TimeSpan.FromMilliseconds(50 * k), reader.Elapsed);
        }

        Assert.False(reader.Advance());
        Assert.Equal(reader.Duration, reader.Elapsed); // the end of the pass, with nothing left to play

        reader.Rewind();
        Assert.Equal(TimeSpan.Zero, reader.Elapsed);
    }

    [Fact]
    public void APlainPngIsRefusedByName()
    {
        // A still PNG, made by taking a one-frame animation apart again: the same bytes without the
        // animation control chunk are exactly what an ordinary encoder writes.
        string animated = WriteAnimation(1, 10);
        byte[] still = StripAnimationChunks(File.ReadAllBytes(animated));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => AnimatedPngReader.Read(still));

        Assert.Contains("not animated", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingThatIsNotAPngIsRefusedByName()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => AnimatedPngReader.Read(Encoding.ASCII.GetBytes("GIF89a and then some")));

        Assert.Contains("PNG signature", error.Message, StringComparison.Ordinal);
    }

    private string WriteAnimation(int frames, double frameRate, List<byte[]>? keep = null)
    {
        string path = Path.Combine(_folder, $"a{frames}.apng");
        using IVideoEncoder encoder =
            VideoEncoder.Create(path, VideoCodec.AnimatedPng, Width, Height, frameRate);

        for (int k = 0; k < frames; k++)
        {
            var frame = new byte[Width * Height * 4];
            for (int p = 0; p < Width * Height; p++)
            {
                frame[(p * 4) + 0] = (byte)((p * 7) + (k * 31));
                frame[(p * 4) + 1] = (byte)((p * 13) + (k * 5));
                frame[(p * 4) + 2] = (byte)(255 - (p * 3) - k);
                frame[(p * 4) + 3] = (byte)(p % 5 == 0 ? 0 : 255 - (k * 2));
            }

            encoder.WriteFrame(frame);
            keep?.Add(frame);
        }

        encoder.Close();
        return path;
    }

    /// <summary>Drops <c>acTL</c>, <c>fcTL</c> and <c>fdAT</c>, leaving the still every decoder sees.</summary>
    private static byte[] StripAnimationChunks(byte[] png)
    {
        using var kept = new MemoryStream();
        kept.Write(png.AsSpan(0, 8));

        int at = 8;
        while (at + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            string name = Encoding.ASCII.GetString(png, at + 4, 4);
            if (name is not ("acTL" or "fcTL" or "fdAT"))
            {
                kept.Write(png.AsSpan(at, length + 12));
            }

            at += length + 12;
        }

        return kept.ToArray();
    }
}
