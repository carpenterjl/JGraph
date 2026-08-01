using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46: the imaging surface as a <c>.m</c> script sees it — 1-based subscripts reading native-scale
/// samples, <c>class</c> answering the image's numeric class, and the bracketed multiple-output forms.
/// </summary>
/// <remarks>
/// Two of these pin bugs that predate the milestone. <c>img(1, 1)</c> used to read the pixel one row
/// and one column in, because image subscripting never consulted the dialect's index base; and
/// <c>L = bwlabel(BW)</c> used to evaluate to the whole <c>[labels, count]</c> pair, because the
/// imaging builtins reach extra outputs through a JGS array that MATLAB has no way to destructure.
/// The JGS-side behaviour is unchanged and is pinned by <see cref="JgsImageBuiltinTests"/>.
/// </remarks>
[Collection("JG facade")]
public sealed class MatlabImageBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabImageBuiltinTests()
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

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>A 2x2 gray ramp: 0, 64, 128, 255 read row by row.</summary>
    private string WriteRamp(string name = "ramp.png")
    {
        using var image = new ImageBuffer(2, 2, 1);
        image[0, 0, 0] = 0.0;
        image[0, 1, 0] = 64 / 255.0;
        image[1, 0, 0] = 128 / 255.0;
        image[1, 1, 0] = 1.0;
        ImageCodec.Write(Path.Combine(_directory, name), image);
        return name;
    }

    [Fact]
    public async Task ImageSubscripts_AreOneBasedAndReadNativeSamples()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            assert(I(1, 1) == 0);
            assert(I(1, 2) == 64);
            assert(I(2, 1) == 128);
            assert(I(2, 2) == 255);
            assert(I(end, end) == 255);
            """);
    }

    [Fact]
    public async Task Class_AnswersTheImagesNumericClass()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            assert(strcmp(class(I), 'uint8'));
            assert(isa(I, 'uint8'));
            assert(isa(I, 'numeric'));
            assert(isa(I, 'integer'));

            D = im2double(I);
            assert(strcmp(class(D), 'double'));
            assert(D(2, 1) == 128 / 255);

            BW = imbinarize(I, 0.4);
            assert(strcmp(class(BW), 'logical'));
            assert(islogical(BW));
            """);
    }

    [Fact]
    public async Task Im2uint8_RescalesADoubleImageBackToTheIntegerRange()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            back = im2uint8(im2double(I));
            assert(strcmp(class(back), 'uint8'));
            assert(back(2, 1) == 128);
            assert(back(2, 2) == 255);
            """);
    }

    [Fact]
    public async Task Arithmetic_QuotesConstantsInTheImagesOwnClassAndSaturates()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            brighter = imadd(I, 50);
            assert(brighter(1, 1) == 50);
            assert(brighter(1, 2) == 114);
            assert(brighter(2, 2) == 255);       % saturates rather than wrapping

            darker = imsubtract(I, 50);
            assert(darker(1, 1) == 0);
            assert(darker(2, 1) == 78);

            half = immultiply(I, 0.5);           % a multiplier is dimensionless
            assert(half(2, 2) == 128);
            """);
    }

    [Fact]
    public async Task Bwlabel_SingleOutputIsTheLabelImage_AndTheBracketFormGivesTheCount()
    {
        await RunAsserting("""
            m = zeros(3, 3);
            m(1, 1) = 1;
            m(3, 3) = 1;
            BW = mat2im(m);

            L = bwlabel(BW);
            assert(strcmp(class(L), 'double'));  % a label map, not the [labels, count] pair
            assert(L(1, 1) == 1);
            assert(L(3, 3) == 2);

            [L2, n] = bwlabel(BW);
            assert(n == 2);
            assert(L2(3, 3) == 2);
            """);
    }

    [Fact]
    public async Task Imhist_SecondOutputIsTheBinLocationsInTheImagesClass()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            [counts, x] = imhist(I);
            assert(numel(counts) == 256);
            assert(sum(counts) == 4);
            assert(x(1) == 0);
            assert(x(256) == 255);
            """);
    }

    [Fact]
    public async Task Graythresh_SecondOutputIsTheEffectivenessMetric()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            [level, em] = graythresh(I);
            assert(level >= 0 && level <= 1);
            assert(em >= 0 && em <= 1);
            assert(abs(otsuthresh(imhist(I)) - level) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imbinarize_Adaptive_ThresholdsAgainstALocalSurface()
    {
        await RunAsserting("""
            % A ramp defeats one global level: every column would fall on the same side of it.
            [X, ~] = meshgrid(linspace(0, 1, 16), 1:16);
            I = mat2im(X);
            BW = imbinarize(I, 'adaptive', 'Sensitivity', 0.5);
            assert(islogical(BW));

            T = adaptthresh(I, 0.5, 'Statistic', 'mean');
            assert(strcmp(class(T), 'double'));
            same = imbinarize(I, T);
            assert(isequal(sum(same), sum(BW)));
            """);
    }

    [Fact]
    public async Task UnknownOption_NamesTheOnesThatWork()
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(
            "I = mat2im(zeros(4, 4)); BW = imbinarize(I, 'adaptiv');",
            sourceId: "",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("adaptiv", result.Message, StringComparison.Ordinal);
        Assert.Contains("Sensitivity", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imfinfo_ReportsTheFileWithoutTheScriptOpeningIt()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            info = imfinfo('{file}');
            assert(info.Width == 2);
            assert(info.Height == 2);
            assert(strcmp(info.Format, 'PNG'));
            assert(info.BitDepth == 8);
            assert(strcmp(info.ColorType, 'grayscale'));
            """);
    }

    [Fact]
    public async Task Imlincomb_And_Imabsdiff_CombineImages()
    {
        await RunAsserting("""
            A = mat2im(0.25 * ones(2, 2));
            B = mat2im(0.75 * ones(2, 2));
            assert(abs(imabsdiff(A, B)(1, 1) - 0.5) < 1e-12);
            assert(abs(imlincomb(0.5, A, 0.5, B)(1, 1) - 0.5) < 1e-12);
            assert(abs(imdivide(B, mat2im(0.5 * ones(2, 2)))(1, 1) - 1.0) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imshow_TakesADisplayRangeInTheImagesClass()
    {
        string file = WriteRamp();
        await RunAsserting($"""
            I = imread('{file}');
            imshow(I, [0 128]);
            imshow(I, []);
            imshow(I);
            """);

        Assert.NotEmpty(_figures);
    }

    [Fact]
    public async Task Whos_ReportsTheImageClass()
    {
        string file = WriteRamp();
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(
            $"I = imread('{file}');", sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains(result.Variables, v => v.Name == "I" && v.Type.Contains("uint8", StringComparison.Ordinal));
    }
}
