using System.Buffers.Binary;
using System.Text;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M112: a video with nothing behind it. ADR 0113 let a still be exported onto no page; this is the
/// same page for a sequence, and the reason it needed anything new is that not one of MATLAB's seven
/// <c>VideoWriter</c> profiles carries an alpha channel — a cut-out written to any of them is
/// composited onto something on the way in, and the picture's shape is what is lost.
/// </summary>
/// <remarks>
/// The files are written for real and read back as bytes, in the spirit of
/// <see cref="MatlabVideoWriterTests"/>: what matters about a muxer is whether the thing on disk is
/// the container it says it is. The APNG checks walk the chunks the way a decoder does, because an
/// animated PNG that a decoder rejects still opens perfectly well as a still of its first frame —
/// which is exactly the failure that would otherwise go unnoticed.
/// </remarks>
[Collection("JG facade")]
public class MatlabTransparentVideoTests : IDisposable
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "jgraph-alpha-" + Guid.NewGuid().ToString("N"));

    public MatlabTransparentVideoTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file the encoder still holds is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(
            _output, static (_, _) => { }, _folder, resolvePath: null, new TestFigureFiles()));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "the script was expected to fail but did not.");
        return result.Message ?? string.Empty;
    }

    private string Path_(string name) => Path.Combine(_folder, name);

    /// <summary>A small cut-out and the loop that films it, in the shape a script writes one.</summary>
    private static string FilmACutOut(string open, int frames = 6) => $$"""
        fig = figure('Color', 'none', 'Position', [80 80 64 48]);
        [X, Y] = meshgrid(linspace(-3, 3, 12));
        surf(X, Y, sin(sqrt(X.^2 + Y.^2) + eps), 'EdgeColor', 'none');
        axis off
        v = VideoWriter({{open}});
        v.FrameRate = 10;
        open(v);
        for k = 1:{{frames}}
            view(30 + 12*k, 30);
            writeVideo(v, getframe(fig));
        end
        n = v.FrameCount;
        close(v);
        """;

    // --- the page a figure does not have ----------------------------------------------------------

    /// <summary>
    /// MATLAB accepts the word for a figure's <c>Color</c> and reads it back as the word, because no
    /// triplet means it. Verified against R2024a rather than recalled.
    /// </summary>
    [Fact]
    public async Task AFigureTakesTheWordNoneForItsColourAndReadsItBack()
    {
        await RunAsserting("""
            f = figure('Color', 'none');
            disp(get(f, 'Color'));
            set(f, 'Color', [1 0 0]);
            disp(mat2str(get(f, 'Color')));
            set(f, 'Color', 'none');
            disp(get(f, 'Color'));
            """);

        Assert.Equal(["none", "[1 0 0]", "none"], _output.NormalLines);
    }

    /// <summary>
    /// The capture is where transparency stops being a property and becomes data: with no page there
    /// is nothing behind the drawing, so the coverage is the only thing that says where the drawing
    /// is, and a fourth page is what carries it. An ordinary figure is unchanged at three.
    /// </summary>
    [Fact]
    public async Task GetframeAnswersAFourthPageOnlyWhenTheFigureHasNoPage()
    {
        await RunAsserting("""
            opaque = figure('Color', [1 1 1], 'Position', [80 80 40 30]);
            plot(1:10, (1:10).^2);
            a = getframe(opaque);
            disp(mat2str(size(a.cdata)));

            cut = figure('Color', 'none', 'Position', [80 80 40 30]);
            plot(1:10, (1:10).^2);
            axis off
            b = getframe(cut);
            disp(mat2str(size(b.cdata)));

            % The corner of a figure with no page is drawn on by nothing at all.
            disp(mat2str(double(b.cdata(1, 1, 4))));
            disp(class(b.cdata));
            """);

        Assert.Equal(["[30 40 3]", "[30 40 4]", "0", "uint8"], _output.NormalLines);
    }

    // --- the profiles that keep it ----------------------------------------------------------------

    /// <summary>
    /// MATLAB's seven are listed first and unchanged; the two that carry alpha are added after, which
    /// is why a script that only ever names MATLAB's sees exactly what it saw before.
    /// </summary>
    [Fact]
    public async Task TheAlphaProfilesAreListedAfterMatlabsSeven()
    {
        await RunAsserting("""
            p = VideoWriter.getProfiles();
            disp(mat2str(numel(p)));
            disp(p(8).Name);
            disp(p(8).VideoFormat);
            disp(p(9).Name);
            disp(p(9).VideoFormat);
            """);

        Assert.Equal(
            ["9", "Animated PNG", "RGBA", "Uncompressed AVI with Alpha", "RGBA"],
            _output.NormalLines);
    }

    /// <summary>A four-channel profile says four channels and thirty-two bits, and its file says so too.</summary>
    [Fact]
    public async Task AnAlphaAviIsThirtyTwoBitsAPixel()
    {
        await RunAsserting(
            FilmACutOut("'cut.avi', 'Uncompressed AVI with Alpha'")
            + "\ndisp(mat2str([n v.ColorChannels v.VideoBitsPerPixel]));");

        Assert.Equal(["[6 4 32]"], _output.NormalLines);

        byte[] bytes = File.ReadAllBytes(Path_("cut.avi"));
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal((uint)(bytes.Length - 8), BitConverter.ToUInt32(bytes, 4));

        // The bit count sits in the BITMAPINFOHEADER, fourteen bytes past the start of 'strf'.
        int strf = Encoding.ASCII.GetString(bytes).IndexOf("strf", StringComparison.Ordinal);
        Assert.Equal(32, BitConverter.ToUInt16(bytes, strf + 8 + 14));
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, strf + 8 + 16)); // BI_RGB, uncompressed
    }

    /// <summary>
    /// The whole file walked as a decoder walks it: signature, header, the animation control before
    /// the first data chunk, one placement chunk per frame, the first frame's data as an ordinary
    /// <c>IDAT</c> and the rest as <c>fdAT</c>, and every check word right.
    /// </summary>
    [Fact]
    public async Task AnAnimatedPngIsAWellFormedPngThatDeclaresItsFrames()
    {
        await RunAsserting(FilmACutOut("'cut.apng'") + "\ndisp(mat2str(n));");
        Assert.Equal(["6"], _output.NormalLines);

        byte[] bytes = File.ReadAllBytes(Path_("cut.apng"));
        Assert.Equal(PngSignature, bytes.Take(8).ToArray());

        List<(string Name, byte[] Payload)> chunks = Chunks(bytes);
        Assert.Equal(
            ["IHDR", "acTL", "fcTL", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "fcTL", "fdAT",
                "fcTL", "fdAT", "fcTL", "fdAT", "IEND"],
            chunks.Select(c => c.Name));

        // IHDR: colour type 6 is the one with an alpha channel, and eight bits deep.
        byte[] header = chunks[0].Payload;
        Assert.Equal(8, header[8]);
        Assert.Equal(6, header[9]);

        // acTL's frame count is written as a placeholder and patched at close, so this is the
        // assertion that a file closed after N frames really says N rather than nought.
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32BigEndian(chunks[1].Payload));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(chunks[1].Payload.AsSpan(4)));

        // The sequence numbers run 0, 1, 2, … across fcTL and fdAT together, which is the rule a
        // decoder enforces and the one an encoder is most likely to get wrong.
        var sequence = new List<uint>();
        foreach ((string name, byte[] payload) in chunks)
        {
            if (name is "fcTL" or "fdAT")
            {
                sequence.Add(BinaryPrimitives.ReadUInt32BigEndian(payload));
            }
        }

        Assert.Equal(Enumerable.Range(0, sequence.Count).Select(i => (uint)i), sequence);

        // Every frame is the whole canvas, replacing what it covers rather than blending into it.
        foreach ((string name, byte[] payload) in chunks.Where(c => c.Name == "fcTL"))
        {
            Assert.Equal(24, payload.Length - 2);
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(12))); // x
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(16))); // y
            Assert.Equal(100, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(20))); // 1/10 s
            Assert.Equal(1000, BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(22)));
            Assert.Equal(0, payload[24]); // dispose: none
            Assert.Equal(0, payload[25]); // blend: source
            _ = name;
        }
    }

    // --- what a profile without alpha is told -----------------------------------------------------

    /// <summary>
    /// A frame with a fourth page handed to a profile that cannot store it is refused by name. The
    /// alternative — dropping the page — writes a video of a rectangle where the script asked for a
    /// cut-out, which is the failure ADR 0113 refused for stills and this refuses for sequences.
    /// </summary>
    [Fact]
    public async Task AProfileThatCarriesNoAlphaRefusesAFourPageFrameByName()
    {
        string message = await RunExpectingFailure("""
            fig = figure('Color', 'none', 'Position', [80 80 40 30]);
            plot(1:10, 1:10);
            v = VideoWriter('flat.avi', 'Uncompressed AVI');
            open(v);
            writeVideo(v, getframe(fig));
            """);

        Assert.Contains("carries no transparency", message, StringComparison.Ordinal);
        Assert.Contains("Animated PNG", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, which has an answer rather than a refusal: an ordinary three-page frame
    /// written to an alpha profile is opaque, because that is the only thing a picture which never
    /// mentioned coverage can have meant. It is what lets one loop write both kinds to one file.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryFrameWrittenToAnAlphaProfileIsOpaque()
    {
        await RunAsserting("""
            v = VideoWriter('mixed.apng');
            v.FrameRate = 5;
            open(v);
            for k = 1:3
                f = uint8(zeros(12, 16, 3));
                f(:, :, 2) = k * 60;
                writeVideo(v, f);
            end
            close(v);
            """);

        byte[] bytes = File.ReadAllBytes(Path_("mixed.apng"));
        List<(string Name, byte[] Payload)> chunks = Chunks(bytes);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(chunks[1].Payload));
        Assert.Equal(6, chunks[0].Payload[9]);
    }

    /// <summary>
    /// A bare <c>.png</c> can only have meant the animated profile — a video writer asked for a still
    /// has said what it wants twice — and the file keeps the name it was given rather than growing a
    /// second extension.
    /// </summary>
    [Fact]
    public async Task ABarePngNameMeansTheAnimatedProfile()
    {
        await RunAsserting("""
            v = VideoWriter('named.png');
            disp(v.Filename);
            disp(v.VideoFormat);
            disp(mat2str(v.VideoBitsPerPixel));
            """);

        Assert.Equal(["named.png", "RGBA", "32"], _output.NormalLines);
    }

    /// <summary>The chunks of a PNG, with every check word verified as they are walked.</summary>
    private static List<(string Name, byte[] Payload)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        int at = PngSignature.Length;
        while (at + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            string name = Encoding.ASCII.GetString(png, at + 4, 4);
            byte[] payload = png.AsSpan(at + 8, length).ToArray();

            uint declared = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Crc(png.AsSpan(at + 4, 4 + length)), declared);

            chunks.Add((name, payload));
            at += 12 + length;
        }

        Assert.Equal(png.Length, at);
        return chunks;
    }

    /// <summary>PNG's check word, written out rather than borrowed, so the encoder is not its own judge.</summary>
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFF_FFFFu;
        foreach (byte b in bytes)
        {
            c ^= b;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }
        }

        return c ^ 0xFFFF_FFFFu;
    }
}
