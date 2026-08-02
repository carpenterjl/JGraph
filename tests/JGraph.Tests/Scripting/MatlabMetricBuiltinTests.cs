using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave J as a <c>.m</c> script sees it: whole-picture statistics, the quality metrics, texture by
/// co-occurrence, reading values back out of a picture, and the display composites.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabMetricBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabMetricBuiltinTests()
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

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return result.Message + _output.ErrorText;
    }

    [Fact]
    public async Task Mean2AndStd2_SummarizeEverySample()
    {
        await RunAsserting("""
            A = [1 2; 3 4];
            assert(abs(mean2(A) - 2.5) < 1e-12);
            assert(abs(std2(A) - std(A(:))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Corr2_IsOneForAScaledCopyAndNaNForAFlatField()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:8, 1:8);
            A = X + Y;
            assert(abs(corr2(A, 2 * A + 5) - 1) < 1e-12);
            assert(isnan(corr2(ones(8), A)));
            """);
    }

    [Fact]
    public async Task Entropy_IsZeroForAFlatFieldAndPositiveForATexture()
    {
        await RunAsserting("""
            assert(entropy(zeros(16)) == 0);
            [X, Y] = meshgrid(1:32, 1:32);
            assert(entropy((X + Y) / 64) > 4);
            """);
    }

    [Fact]
    public async Task Immse_AndPsnr_AgreeWithEachOther()
    {
        await RunAsserting("""
            A = 0.5 * ones(8);
            B = 0.6 * ones(8);
            assert(abs(immse(A, B) - 0.01) < 1e-12);
            assert(abs(psnr(A, B) - 20) < 1e-9);
            assert(isinf(psnr(A, A)));

            % The peak is quoted in the picture's own units, so naming it explicitly moves the answer.
            assert(abs(psnr(A, B, 2) - (20 + 20 * log10(2))) < 1e-9);
            """);
    }

    [Fact]
    public async Task Psnr_OnAUint8Picture_MeasuresInGreyLevels()
    {
        await RunAsserting("""
            A = im2uint8(mat2im(0.5 * ones(8)));
            B = im2uint8(mat2im(0.6 * ones(8)));
            assert(strcmp(class(A), 'uint8'));

            % immse is quoted in the class's own units, so a uint8 pair answers in grey levels
            % squared — 128 against 153 is 625. psnr divides by the peak the class can hold, so it
            % lands on the same number whichever class the same picture is held in.
            assert(abs(immse(A, B) - 625) < 1e-9);
            assert(abs(immse(im2double(A), im2double(B)) - 625 / 255^2) < 1e-9);
            assert(abs(psnr(A, B) - psnr(im2double(A), im2double(B))) < 1e-9);
            """);
    }

    [Fact]
    public async Task Ssim_IsOneAgainstItselfAndReturnsAMapWhenAsked()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:32, 1:32);
            A = (X + Y) / 64;
            assert(abs(ssim(A, A) - 1) < 1e-9);

            [s, m] = ssim(A, A + 0.02);
            assert(s < 1 && s > 0.9);
            assert(isequal(size(m), [32 32]));
            """);
    }

    [Fact]
    public async Task Ssim_ReadsItsOptionsByName()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:32, 1:32);
            A = (X + Y) / 64;
            B = A + 0.05;

            % A wider dynamic range makes the stabilizing constants larger, which forgives the same
            % difference more — the option is not decoration.
            tight = ssim(A, B, 'DynamicRange', 0.5);
            loose = ssim(A, B, 'DynamicRange', 4);
            assert(loose > tight);

            wide = ssim(A, B, 'Radius', 4);
            assert(wide > 0 && wide <= 1);
            """);
    }

    [Fact]
    public async Task Multissim_RunsDownAPyramidAndRefusesAPictureTooSmall()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:64, 1:64);
            A = (X + Y) / 128;
            assert(abs(multissim(A, A) - 1) < 1e-8);

            [s, maps] = multissim(A, A, 'NumScales', 3);
            assert(abs(s - 1) < 1e-8);
            assert(numel(maps) == 3);
            assert(size(maps{2}, 1) == 32);
            """);

        string message = await RunExpectingFailure("multissim(zeros(8), zeros(8));");
        Assert.Contains("fewer scales", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiceAndJaccard_ScoreOverlapAndAnswerPerLabel()
    {
        await RunAsserting("""
            A = false(10); A(2:6, 2:6) = true;
            B = false(10); B(3:7, 3:7) = true;
            assert(abs(dice(A, B) - 0.64) < 1e-12);
            assert(abs(jaccard(A, B) - 16/34) < 1e-12);

            L1 = zeros(6); L1(1:3, :) = 1; L1(4:6, :) = 2;
            L2 = zeros(6); L2(1:3, :) = 1; L2(4:5, :) = 2;
            s = dice(L1, L2);
            assert(numel(s) == 2);
            assert(abs(s(1) - 1) < 1e-12);
            assert(s(2) < 1);
            """);
    }

    [Fact]
    public async Task Bfscore_ScoresOutlinesAndHandsBackPrecisionAndRecall()
    {
        await RunAsserting("""
            T = false(40); T(10:21, 10:21) = true;
            assert(abs(bfscore(T, T, 2) - 1) < 1e-12);

            M = false(40); M(16:27, 16:27) = true;
            [f, p, r] = bfscore(M, T, 2);
            assert(f < 0.6);
            assert(p >= 0 && p <= 1 && r >= 0 && r <= 1);
            assert(abs(f - 2 * p * r / (p + r)) < 1e-12);
            """);
    }

    [Fact]
    public async Task Graycomatrix_CountsPairsAndScalesThePicture()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:16, 1:16);
            A = (X + Y) / 32;

            [g, si] = graycomatrix(A, 'NumLevels', 4);
            assert(isequal(size(g), [4 4]));

            % One offset, one step right, on a 16-by-16 picture: 16 rows of 15 pairs.
            assert(abs(sum(g(:)) - 240) < 1e-12);
            assert(isequal(size(si), [16 16]));
            assert(min(si(:)) >= 1 && max(si(:)) <= 4);

            g3 = graycomatrix(A, 'Offset', [0 1; 1 0; 1 1]);
            assert(isequal(size(g3), [8 8 3]));

            gs = graycomatrix(A, 'NumLevels', 4, 'Symmetric', true);
            assert(isequal(gs, gs'));
            """);
    }

    [Fact]
    public async Task Graycoprops_ReadsTheFourStatisticsOffTheTable()
    {
        await RunAsserting("""
            g = graycomatrix(zeros(16), 'NumLevels', 4);
            s = graycoprops(g);
            assert(s.Contrast == 0);
            assert(abs(s.Energy - 1) < 1e-12);
            assert(abs(s.Homogeneity - 1) < 1e-12);
            assert(isnan(s.Correlation));

            one = graycoprops(g, 'Contrast');
            assert(one.Contrast == 0);
            """);

        string message = await RunExpectingFailure("graycoprops(graycomatrix(zeros(8)), 'Sharpness');");
        Assert.Contains("Homogeneity", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impixel_ReadsOneBasedPointsAndAnswersThreeColumns()
    {
        await RunAsserting("""
            A = zeros(4);
            A(2, 3) = 0.75;
            p = impixel(A, [3 1], [2 1]);
            assert(isequal(size(p), [2 3]));
            assert(abs(p(1, 1) - 0.75) < 1e-12);
            assert(abs(p(1, 1) - p(1, 3)) < 1e-12);
            assert(p(2, 1) == 0);
            """);
    }

    [Fact]
    public async Task Improfile_SamplesAlongALineAndSaysWhereItLooked()
    {
        await RunAsserting("""
            [X, ~] = meshgrid(1:16, 1:16);
            A = (X - 1) / 15;

            p = improfile(A, [1 16], [1 1], 16, 'bilinear');
            assert(numel(p) == 16);
            assert(abs(p(1)) < 1e-12);
            assert(abs(p(end) - 1) < 1e-12);

            [cx, cy, c] = improfile(A, [1 16], [1 1], 16);
            assert(abs(cx(1) - 1) < 1e-12);
            assert(abs(cy(1) - 1) < 1e-12);
            assert(numel(c) == 16);

            % With no count asked for, one sample per pixel of path length.
            q = improfile(A, [1 10], [1 1]);
            assert(numel(q) == 10);
            """);
    }

    [Fact]
    public async Task Imcontour_DrawsOnPictureAxesWithRowOneAtTheTop()
    {
        await RunAsserting("""
            [X, Y] = meshgrid(1:32, 1:32);
            imcontour((X + Y) / 64, 5);
            """);

        AxesModel axes = JG.Gca();
        Assert.True(axes.EqualAspect);
        Assert.True(axes.PrimaryYAxis.Inverted);
        Assert.NotEmpty(axes.Plots);
    }

    [Fact]
    public async Task Montage_TilesACellOfPicturesAndAStack()
    {
        await RunAsserting("""
            montage({zeros(8), ones(8), 0.5 * ones(8)});
            """);
        Assert.NotEmpty(JG.Gca().Plots);

        await RunAsserting("""
            stack = zeros(8, 8, 1, 4);
            montage(stack, 'Size', [2 2], 'BorderSize', 1);
            """);

        string message = await RunExpectingFailure("montage({zeros(4), ones(4), zeros(4)}, 'Size', [1 2]);");
        Assert.Contains("holds 2", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imfuse_AnswersAUint8CompositeAndTakesItsMethodPositionally()
    {
        await RunAsserting("""
            A = zeros(8); A(1:4, :) = 1;
            B = zeros(8); B(:, 1:4) = 1;

            C = imfuse(A, B);
            assert(strcmp(class(C), 'uint8'));
            assert(isequal(size(C), [8 8 3]));

            D = imfuse(A, B, 'diff');
            assert(isequal(size(D), [8 8]));

            M = imfuse(A, B, 'montage');
            assert(size(M, 2) == 16);

            % Naming the channels moves which picture drives which, so a red-only map differs.
            E = imfuse(A, B, 'falsecolor', 'ColorChannels', [1 2 0]);
            assert(~isequal(E, C));
            """);

        string message = await RunExpectingFailure("imfuse(zeros(4), ones(4), 'overlay');");
        Assert.Contains("falsecolor", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imshowpair_ShowsTheCompositeItDoesNotReturn()
    {
        await RunAsserting("""
            A = zeros(8); A(1:4, :) = 1;
            B = zeros(8); B(:, 1:4) = 1;
            imshowpair(A, B, 'blend');
            """);

        AxesModel axes = JG.Gca();
        Assert.NotEmpty(axes.Plots);
        Assert.True(axes.EqualAspect);
    }

    [Fact]
    public async Task IptPreferences_RememberWhatWasSetAndRefuseWhatIsNotOne()
    {
        await RunAsserting("""
            iptsetpref('ImshowBorder', 'tight');
            assert(strcmp(iptgetpref('ImshowBorder'), 'tight'));
            iptsetpref('ImshowBorder', 'loose');
            assert(strcmp(iptgetpref('ImshowBorder'), 'loose'));

            all = iptgetpref();
            assert(strcmp(all.ImshowBorder, 'loose'));
            """);

        string message = await RunExpectingFailure("iptgetpref('ImshowBorderStyle');");
        Assert.Contains("ImshowBorder", message, StringComparison.Ordinal);
    }
}
