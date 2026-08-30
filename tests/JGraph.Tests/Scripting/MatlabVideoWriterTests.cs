using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M108: <c>VideoWriter</c>, and the two smaller gaps a morphing-surface script fell into on the way
/// to it — <c>axis</c>'s three-dimensional form and a surface's writable heights.
/// </summary>
/// <remarks>
/// The videos are written to real files in a temporary folder and their bytes are read back, because
/// the only interesting question about a muxer is whether what came out is the container it claims to
/// be. Nothing here decodes: a decoder would be a second implementation of the same guesses. The
/// structural checks — the RIFF wrapper, the stream header, the frame count in the file rather than
/// in the object — are what a reader would fail on, and they are checked directly.
/// </remarks>
[Collection("JG facade")]
public class MatlabVideoWriterTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "jgraph-video-" + Guid.NewGuid().ToString("N"));

    public MatlabVideoWriterTests()
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
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, static (_, _) => { }, _folder));

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

    /// <summary>A frame-writing loop, in the shape a script actually writes one.</summary>
    private static string WriteEight(string open) => $$"""
        v = VideoWriter({{open}});
        v.FrameRate = 10;
        open(v);
        for k = 1:8
            f = uint8(zeros(36, 48, 3));
            f(:, :, 2) = k * 20;
            writeVideo(v, f);
        end
        n = v.FrameCount;
        d = v.Duration;
        close(v);
        """;

    [Fact]
    public async Task AMotionJpegAviIsARiffFileWithOneVideoStream()
    {
        await RunAsserting(WriteEight("'clip.avi'") + "\ndisp(mat2str([n d]));");

        byte[] bytes = File.ReadAllBytes(Path_("clip.avi"));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("AVI ", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        // The declared length must match the file, which is the check that catches a size patched
        // over the wrong offset — the defect that made every AVI here unreadable while it looked fine.
        Assert.Equal((uint)(bytes.Length - 8), BitConverter.ToUInt32(bytes, 4));
        Assert.Contains("vids", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Contains("MJPG", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal(new[] { "[8 0.8]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ADeclaredListLengthAgreesWithWhatFollowsIt()
    {
        // The header lists are walked the way a reader walks them: every list says how long it is, and
        // the walk must land exactly on the next chunk. An off-by-four here is invisible to the eye
        // and fatal to every player.
        await RunAsserting(WriteEight("'walk.avi'"));

        byte[] bytes = File.ReadAllBytes(Path_("walk.avi"));
        int at = 12; // past 'RIFF', its size, and 'AVI '
        var seen = new List<string>();
        while (at + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
            uint size = BitConverter.ToUInt32(bytes, at + 4);
            seen.Add(id == "LIST" ? System.Text.Encoding.ASCII.GetString(bytes, at + 8, 4) : id);
            at += 8 + (int)size + ((int)size & 1);
        }

        Assert.Equal(at, bytes.Length); // the walk consumed the file exactly
        Assert.Equal(new[] { "hdrl", "movi", "idx1" }, seen);
    }

    [Fact]
    public async Task AnUncompressedAviCarriesEveryPixelItWasGiven()
    {
        await RunAsserting("""
            v = VideoWriter('raw.avi', 'Uncompressed AVI');
            open(v);
            writeVideo(v, uint8(zeros(4, 6, 3)));
            close(v);
            """);

        // 4 rows of 6 pixels is 18 bytes a row, padded to 20, times 4 rows = 80 bytes of frame.
        byte[] bytes = File.ReadAllBytes(Path_("raw.avi"));
        string text = System.Text.Encoding.ASCII.GetString(bytes);
        int frame = text.IndexOf("00db", StringComparison.Ordinal);
        Assert.True(frame > 0, "the frame chunk is missing.");
        Assert.Equal(80u, BitConverter.ToUInt32(bytes, frame + 4));
    }

    [Fact]
    public async Task TheIndexNamesEveryFrameAndPointsAtIt()
    {
        await RunAsserting(WriteEight("'indexed.avi'"));

        byte[] bytes = File.ReadAllBytes(Path_("indexed.avi"));
        string text = System.Text.Encoding.ASCII.GetString(bytes);
        int index = text.LastIndexOf("idx1", StringComparison.Ordinal);
        Assert.True(index > 0, "the index is missing.");
        Assert.Equal(8u * 16, BitConverter.ToUInt32(bytes, index + 4));

        // Every entry's offset is measured from the 'movi' code, so following one must land on a
        // chunk id rather than in the middle of a frame.
        int movi = text.IndexOf("movi", StringComparison.Ordinal);
        for (int i = 0; i < 8; i++)
        {
            uint offset = BitConverter.ToUInt32(bytes, index + 8 + (i * 16) + 8);
            Assert.Equal("00dc", System.Text.Encoding.ASCII.GetString(bytes, movi + (int)offset, 4));
        }
    }

    [Fact]
    public async Task AnMpeg4IsWrittenThroughTheSystemEncoder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Media Foundation is the Windows encoder; there is nothing to test elsewhere.
        }

        await RunAsserting(WriteEight("'clip.mp4', 'MPEG-4'") + "\ndisp(n);");

        byte[] bytes = File.ReadAllBytes(Path_("clip.mp4"));
        Assert.True(bytes.Length > 0, "the MP4 is empty.");

        // An MP4 opens with an 'ftyp' box, and its brand says what it is compatible with.
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
        Assert.Contains("mp4", System.Text.Encoding.ASCII.GetString(bytes, 8, 16));
        Assert.Equal(new[] { "8" }, _output.NormalLines);
    }

    [Fact]
    public async Task EveryPropertyMatlabPublishesIsAnswered()
    {
        await RunAsserting("""
            v = VideoWriter('props.mp4', 'MPEG-4');
            disp(v.Filename);
            disp(v.FileFormat);
            disp(v.VideoCompressionMethod);
            disp(v.VideoFormat);
            disp(mat2str([v.ColorChannels v.VideoBitsPerPixel v.FrameRate v.Quality v.FrameCount v.Duration]));
            disp(isempty(v.Height));
            disp(class(v));
            """);

        Assert.Equal(
            new[] { "props.mp4", "mp4", "H.264", "RGB24", "[3 24 30 75 0 0]", "true", "VideoWriter" },
            _output.NormalLines);
    }

    [Fact]
    public async Task QualityIsCarriedOnlyByTheProfilesThatThrowSomethingAway()
    {
        // MATLAB's property list differs by profile, and a script that reads Quality off an
        // uncompressed writer should be told it is not there rather than handed a number that
        // means nothing.
        await RunAsserting("""
            disp(isfield(VideoWriter('a.avi', 'Motion JPEG AVI'), 'Quality'));
            disp(isfield(VideoWriter('b.avi', 'Uncompressed AVI'), 'Quality'));
            disp(isfield(VideoWriter('c.avi', 'Indexed AVI'), 'Colormap'));
            disp(isfield(VideoWriter('d.avi', 'Grayscale AVI'), 'Colormap'));
            """);

        Assert.Equal(new[] { "true", "false", "true", "false" }, _output.NormalLines);
    }

    [Fact]
    public async Task AWriterIsAHandleSoOpenIsVisibleToTheCallersCopy()
    {
        // The whole reason it is a handle class: open(v) mutates the object the caller is holding.
        // As a value class the writer would be reopened-and-discarded and writeVideo would say the
        // file was never opened.
        await RunAsserting("""
            v = VideoWriter('handle.avi');
            w = v;
            open(v);
            writeVideo(w, uint8(zeros(4, 4, 3)));
            disp(w.FrameCount);
            disp(v.FrameCount);
            close(v);
            """);

        Assert.Equal(new[] { "1", "1" }, _output.NormalLines);
    }

    /// <summary>
    /// MATLAB's seven come first and in MATLAB's order. M112 added two after them that carry an alpha
    /// channel — see <see cref="MatlabTransparentVideoTests"/> — so the count is nine here; what this
    /// pins is that nothing was inserted among MATLAB's own, which a script indexing the table sees.
    /// </summary>
    [Fact]
    public async Task GetProfilesNamesAllSevenOfMatlabsFirst()
    {
        await RunAsserting("""
            p = VideoWriter.getProfiles();
            disp(numel(p));
            disp(p(6).Name);
            disp(p(6).FileExtensions{1});
            disp(p(7).Name);
            """);

        Assert.Equal(new[] { "9", "MPEG-4", ".mp4", "Uncompressed AVI" }, _output.NormalLines);
    }

    [Fact]
    public async Task AFrameArrayIsWrittenElementByElement()
    {
        // F(k) = getframe(fig) in the loop and one writeVideo after it — the other half of the
        // getframe idiom.
        await RunAsserting("""
            v = VideoWriter('array.avi');
            open(v);
            for k = 1:3
                F(k).cdata = uint8(ones(8, 8, 3) * ((k - 1) * 40));
                F(k).colormap = [];
            end
            writeVideo(v, F);
            disp(v.FrameCount);
            close(v);
            """);

        Assert.Equal(new[] { "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task AVideoTheScriptForgotToCloseIsStillFinished()
    {
        // The encoder belongs to the run, so the file is finished when the run ends rather than left
        // as a container with no index and no sizes.
        var engine = new MatlabScriptEngine();
        ScriptRunResult result = await engine.RunAsync(
            """
            v = VideoWriter('forgotten.avi');
            open(v);
            writeVideo(v, uint8(zeros(4, 4, 3)));
            """,
            new ScriptContext(_output, static (_, _) => { }, _folder),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        byte[] bytes = File.ReadAllBytes(Path_("forgotten.avi"));
        Assert.Equal((uint)(bytes.Length - 8), BitConverter.ToUInt32(bytes, 4));
        Assert.Contains("idx1", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Theory]
    [InlineData("writeVideo(VideoWriter('x.avi'), uint8(zeros(4,4,3)));", "must be open")]
    [InlineData("v = VideoWriter('x.avi'); open(v); writeVideo(v, uint8(zeros(4,4,3))); writeVideo(v, uint8(zeros(8,8,3)));", "every frame must be 4 by 4")]
    [InlineData("v = VideoWriter('x.avi'); v.FrameRate = 0; open(v);", "FrameRate to be positive")]
    [InlineData("v = VideoWriter('x.avi'); open(v); v.FrameRate = 7; writeVideo(v, uint8(zeros(4,4,3)));", "not allowed after open")]
    [InlineData("v = VideoWriter('x.avi'); open(v); writeVideo(v, ones(4,4,3) * 200);", "must be in the range 0 to 1")]
    [InlineData("v = VideoWriter('x.avi', 'Indexed AVI'); open(v); writeVideo(v, uint8(zeros(4,4)));", "Colormap is required")]
    [InlineData("v = VideoWriter('x.avi', 'Grayscale AVI'); open(v); writeVideo(v, uint8(zeros(4,4,3)));", "colour frame was given")]
    [InlineData("VideoWriter('x.avi', 'Nope');", "is not a profile")]
    [InlineData("VideoWriter('x.mj2', 'Archival');", "no encoder for")]
    public async Task ARefusalSaysWhatIsWrong(string code, string expected)
    {
        Assert.Contains(expected, await RunExpectingFailure(code));
    }

    [Fact]
    public async Task AnOddFrameIsRefusedByMpeg4RatherThanEncodedWrongly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Contains(
            "even frame width and height",
            await RunExpectingFailure(
                "v = VideoWriter('odd.mp4', 'MPEG-4'); open(v); writeVideo(v, uint8(zeros(5,7,3)));"));
    }

    [Fact]
    public async Task AMismatchedExtensionIsAppendedRatherThanRefused()
    {
        // MATLAB's own rule, odd as it reads: VideoWriter('clip.mp4', 'Motion JPEG AVI') writes
        // 'clip.mp4.avi'.
        await RunAsserting("""
            v = VideoWriter('clip.mp4', 'Motion JPEG AVI');
            disp(v.Filename);
            disp(v.FileFormat);
            """);

        Assert.Equal(new[] { "clip.mp4.avi", "avi" }, _output.NormalLines);
    }

    [Fact]
    public async Task AxisTakesTheThreeDimensionalAndColourLimitForms()
    {
        await RunAsserting("""
            surf(peaks(10));
            axis([-3 3 -2 2 -1 1]);
            disp(mat2str([xlim ylim zlim]));
            axis([-3 3 -2 2 -1 1 0 5]);
            disp(mat2str(clim));
            """);

        Assert.Equal(new[] { "[-3 3 -2 2 -1 1]", "[0 5]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AxisStillRefusesALengthMatlabDoesNotTake()
    {
        Assert.Contains("axis expects", await RunExpectingFailure("surf(peaks(10)); axis([1 2 3]);"));
    }

    [Fact]
    public async Task ASurfacesHeightsCanBeReplacedInPlace()
    {
        // The animation idiom, and the reason M108 exists: the shape is replaced and everything else
        // about the surface stands.
        await RunAsserting("""
            [X, Y] = meshgrid(1:4, 1:3);
            h = surf(X, Y, X .* 0, 'EdgeColor', 'none');
            set(h, 'ZData', X .* Y, 'CData', X .* Y);
            disp(mat2str(size(get(h, 'ZData'))));
            disp(max(max(get(h, 'ZData'))));
            disp(get(h, 'EdgeColor'));
            """);

        // EdgeColor reads back as [] rather than 'none' — the surface-colour divergence ADR 0072
        // recorded, unchanged by this milestone and asserted here so it stays visible.
        Assert.Equal(new[] { "[3 4]", "12", "[]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ACountedOutRulerIsCountedOutAgainWhenTheHeightsResize()
    {
        // MATLAB's XDataMode 'auto': surf(z) counts its rulers from the grid, so a differently-sized
        // ZData resizes the surface rather than being refused.
        await RunAsserting("""
            h = surf(peaks(12));
            set(h, 'ZData', peaks(20));
            disp(mat2str(size(get(h, 'ZData'))));
            disp(mat2str(size(get(h, 'XData'))));
            """);

        Assert.Equal(new[] { "[20 20]", "[1 20]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AGivenRulerMustGoOnMatchingTheHeights()
    {
        Assert.Contains(
            "one value per grid column",
            await RunExpectingFailure("""
                [X, Y] = meshgrid(1:4, 1:3);
                h = surf(X, Y, X .* 0);
                set(h, 'ZData', zeros(3, 9));
                """));
    }

    [Fact]
    public async Task AParametricSurfaceKeepsItsPositionGrids()
    {
        // Writing through Z alone on a parametric surface would throw its two position matrices away
        // and leave a rectilinear surface wearing the wrong shape.
        await RunAsserting("""
            [T, P] = meshgrid(linspace(0, pi, 6), linspace(0, 2*pi, 6));
            h = surf(sin(T) .* cos(P), sin(T) .* sin(P), cos(T));
            set(h, 'ZData', 2 * cos(T));
            disp(mat2str(size(get(h, 'XData'))));
            disp(max(max(get(h, 'ZData'))));
            """);

        Assert.Equal(new[] { "[6 6]", "2" }, _output.NormalLines);
    }
}
