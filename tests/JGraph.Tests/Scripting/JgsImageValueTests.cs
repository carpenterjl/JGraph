using JGraph.Api;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M24: the <c>JgsType.Image</c> carrier as seen from script — 1-based <c>img(r,c[,ch])</c> reads,
/// <c>size</c>/<c>numel</c>/<c>isequal</c>, the constant-size display label, iteration errors, and
/// run-end disposal. Images enter the script through the real <c>imread</c> path over a temp file.
/// </summary>
[Collection("JG facade")]
public sealed class JgsImageValueTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsImageValueTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, _) => { }, _directory), default);

    /// <summary>Writes a small RGB PNG whose byte-exact samples are easy to assert, returns its filename.</summary>
    private string WriteRgb(string name = "img.png")
    {
        using var image = new ImageBuffer(2, 3, 3);
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                image[r, c, 0] = ((r + c) * 51) / 255.0; // red ramp on byte boundaries (0, 51, 102, 153)
                image[r, c, 1] = 128 / 255.0;
                image[r, c, 2] = (255 - (c * 51)) / 255.0;
            }
        }

        ImageCodec.Write(Path.Combine(_directory, name), image);
        return name;
    }

    private string WriteGray(string name, int h, int w)
    {
        using var image = new ImageBuffer(h, w, 1);
        image[Math.Min(1, h - 1), 0, 0] = 51 / 255.0;
        ImageCodec.Write(Path.Combine(_directory, name), image);
        return name;
    }

    [Fact]
    public async Task Display_IsAConstantSizeLabel()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"print(imread('{file}'))");
        Assert.True(result.Success, result.Message);
        string text = _output.NormalText.Trim();
        Assert.Equal("image[2x3x3]", text);
        Assert.True(text.Length < 40); // never dumps pixels
    }

    [Fact]
    public async Task Display_GrayscaleOmitsChannelCount()
    {
        string file = WriteGray("g.png", 4, 5);
        ScriptRunResult result = await Run($"print(imread('{file}'))");
        Assert.True(result.Success, result.Message);
        Assert.Equal("image[4x5]", _output.NormalText.Trim());
    }

    [Fact]
    public async Task Indexing_ReadsZeroBasedSamples()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"""
            let img = imread('{file}');
            print(img(0, 0, 0))
            print(img[1, 2, 2])
            """);
        Assert.True(result.Success, result.Message);
        // Both spellings index alike: red at (0,0) = 0 ; blue at (1,2) = (255-102)/255 = 0.6
        Assert.Equal("0\n0.6", _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Indexing_GrayscaleAllowsTwoSubscripts()
    {
        string file = WriteGray("g.png", 2, 2);
        ScriptRunResult result = await Run($"print(imread('{file}')(1, 0))");
        Assert.True(result.Success, result.Message);
        Assert.Equal((51 / 255.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _output.NormalText.Trim());
    }

    [Fact]
    public async Task Indexing_RgbWithoutChannelIsAnError()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"print(imread('{file}')(0, 0))");
        Assert.False(result.Success);
        Assert.Contains("channel", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Indexing_OutOfRangeIsAnError()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"print(imread('{file}')(2, 0, 0))");
        Assert.False(result.Success);
        Assert.Contains("out of range", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Size_ReturnsHeightWidthChannels()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"print(size(imread('{file}')))");
        Assert.True(result.Success, result.Message);
        Assert.Equal("[2, 3, 3]", _output.NormalText.Trim());
    }

    [Fact]
    public async Task Numel_IsSampleCount()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"print(numel(imread('{file}')))");
        Assert.True(result.Success, result.Message);
        Assert.Equal("18", _output.NormalText.Trim()); // 2*3*3
    }

    [Fact]
    public async Task Isequal_ComparesPixels()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"""
            let a = imread('{file}');
            let b = imread('{file}');
            print(isequal(a, b))
            """);
        Assert.True(result.Success, result.Message);
        Assert.Equal("true", _output.NormalText.Trim());
    }

    [Fact]
    public async Task Size_WithADimensionReturnsThatDimensionOnly()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"""
            let img = imread('{file}');
            print(size(img, 1))
            print(size(img, 2))
            print(size(img, 3))
            print(size(img, 4))
            print(size([[1, 2, 3], [4, 5, 6]], 2))
            print(size([1, 2, 3, 4], 3))
            """);
        Assert.True(result.Success, result.Message);
        // Dimensions past a value's rank are 1, exactly as in MATLAB.
        Assert.Equal("2\n3\n3\n1\n3\n1", _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Reductions_WalkEverySampleWithoutBoxing()
    {
        // The gray fixture is all zeros except one 51/255 sample, so the reductions are exact.
        string file = WriteGray("g.png", 2, 5);
        ScriptRunResult result = await Run($"""
            let img = imread('{file}');
            print(max(img))
            print(min(img))
            print(sum(img) == max(img))
            print(mean(img) * 10 == sum(img))
            """);
        Assert.True(result.Success, result.Message);
        Assert.Equal($"{51 / 255.0:R}\n0\ntrue\ntrue",
            _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Isempty_DistinguishesEmptyValues()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"""
            print(isempty([]))
            print(isempty(''))
            print(isempty([1, 2]))
            print(isempty(0))
            print(isempty(imread('{file}')))
            """);
        Assert.True(result.Success, result.Message);
        Assert.Equal("true\ntrue\nfalse\nfalse\nfalse", _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Fprintf_WritesOnlyWhatTheFormatSays()
    {
        ScriptRunResult result = await Run("""
            fprintf('%s: %.2f, %.2f\n', 'Center', 1.5, 2.25);
            fprintf('no newline here')
            """);
        Assert.True(result.Success, result.Message);
        Assert.Equal("Center: 1.50, 2.25\nno newline here", _output.NormalText.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task ForLoop_OverAnImageIsAClearError()
    {
        string file = WriteRgb();
        ScriptRunResult result = await Run($"for p in imread('{file}') {{ print(p) }}");
        Assert.False(result.Success);
        Assert.Contains("iterate", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
