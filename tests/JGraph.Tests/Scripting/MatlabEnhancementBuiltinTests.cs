using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave E as a <c>.m</c> script sees it: the enhancement and denoising builtins, their option
/// words, the extra outputs each hands back, and the three shapes a picture can arrive in.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabEnhancementBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabEnhancementBuiltinTests()
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
    public async Task Histeq_TakesALevelCountOrATargetHistogramAndReportsItsMapping()
    {
        await RunAsserting("""
            [x, y] = meshgrid(0:15, 0:15);
            I = (x + 16*y) / 255;

            [J, T] = histeq(I);
            assert(numel(T) == 256);
            assert(all(diff(T) >= 0));                 % the mapping never turns back
            assert(isequal(size(J), size(I)));

            % A ramp matched against 256 equal levels is already equalized, so the mapping is the
            % identity and the picture comes back unchanged.
            [K, T2] = histeq(I, ones(1, 256));
            assert(max(max(abs(K - I))) < 1e-12);
            assert(abs(T2(1)) < 1e-12);
            assert(abs(T2(256) - 1) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imhistmatch_MovesOnePictureOntoAnothersDistribution()
    {
        await RunAsserting("""
            [x, y] = meshgrid(0:23, 0:23);
            dark = 0.1 + 0.2 * (x + y) / 46;
            bright = 0.7 + 0.2 * (x + y) / 46;

            [J, hgram] = imhistmatch(dark, bright, 64);
            assert(numel(hgram) == 64);
            assert(mean(J(:)) > 0.6);

            smooth = imhistmatch(dark, bright, 64, 'Method', 'polynomial');
            assert(mean(smooth(:)) > 0.5);

            msg = '';
            try
                imhistmatch(dark, bright, 64, 'Method', 'spline');
            catch err
                msg = err.message;
            end
            assert(contains(msg, 'polynomial'));
            """);
    }

    [Fact]
    public async Task Adapthisteq_ReadsEveryOptionAndNamesTheAlternativesWhenItCannot()
    {
        await RunAsserting("""
            [x, y] = meshgrid(0:31, 0:31);
            I = 0.48 + 0.04 * sin(x/5) .* cos(y/4);

            plain = adapthisteq(I);
            assert(isequal(size(plain), [32 32]));
            assert(std(plain(:)) > std(I(:)));

            tuned = adapthisteq(I, 'NumTiles', [4 4], 'ClipLimit', 0.5, 'NBins', 128, ...
                                'Distribution', 'rayleigh', 'Alpha', 0.5, 'Range', 'original');
            assert(min(min(tuned)) >= min(min(I)) - 1e-9);
            assert(max(max(tuned)) <= max(max(I)) + 1e-9);
            """);

        string message = await RunExpectingFailure("adapthisteq(zeros(8), 'Distribution', 'poisson');");
        Assert.Contains("rayleigh", message);

        string unknown = await RunExpectingFailure("adapthisteq(zeros(8), 'NumTils', 4);");
        Assert.Contains("NumTiles", unknown);
    }

    [Fact]
    public async Task Imflatfield_DividesOutTheShadingAndHonoursAMask()
    {
        await RunAsserting("""
            [x, ~] = meshgrid(0:95, 0:95);
            shaded = 0.6 * (0.5 + 0.5 * x / 95);

            flat = imflatfield(shaded, 8);
            middle = flat(25:70, 25:70);
            assert(std(middle(:)) < 1e-6);

            % A mask leaves everything outside it exactly as it was.
            mask = zeros(96, 96);
            mask(1:48, :) = 1;
            partial = imflatfield(shaded, 8, mask);
            assert(max(max(abs(partial(60:90, :) - shaded(60:90, :)))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Decorrstretch_TakesColourPlanesAndItsOptions()
    {
        await RunAsserting("""
            % Three bands that nearly agree — what the stretch exists to pull apart.
            [x, y] = meshgrid(0:19, 0:19);
            base = 0.4 + 0.2 * sin((x + y) / 5);
            rgb = zeros(20, 20, 3);
            rgb(:, :, 1) = base;
            rgb(:, :, 2) = base + 0.01 * sin(y / 3);
            rgb(:, :, 3) = base + 0.01 * cos(x / 3);

            out = decorrstretch(rgb);
            assert(isequal(size(out), [20 20 3]));

            % Covariance mode, explicit targets and a final linear stretch all have to be accepted.
            tuned = decorrstretch(rgb, 'Mode', 'covariance', 'TargetMean', [0.5 0.5 0.5], ...
                                  'TargetSigma', [0.2 0.2 0.2], 'Tol', 0.01);
            assert(isequal(size(tuned), [20 20 3]));

            % SampleSubs restricts the statistics to the named pixels.
            sampled = decorrstretch(rgb, 'SampleSubs', {1:20, 1:20});
            assert(isequal(size(sampled), [20 20 3]));
            """);

        string message = await RunExpectingFailure(
            "decorrstretch(zeros(4, 4, 3), 'SampleSubs', {1:3, 1:2});");
        Assert.Contains("as many row indices", message);
    }

    [Fact]
    public async Task Imsharpen_SteepensAnEdgeAndKeepsTheImageClass()
    {
        await RunAsserting("""
            step = zeros(16, 16);
            step(:, 9:16) = 0.4;
            step = step + 0.3;

            sharp = imsharpen(step, 'Radius', 1.5, 'Amount', 1, 'Threshold', 0);
            assert(sharp(8, 8) < step(8, 8));
            assert(sharp(8, 9) > step(8, 9));

            % The class tag survives the round trip through an image value.
            picture = im2uint8(mat2gray(step));
            assert(strcmp(class(imsharpen(picture)), 'uint8'));
            """);
    }

    [Fact]
    public async Task EdgePreservingFilters_TakeAPlainMatrixAndGiveOneBack()
    {
        await RunAsserting("""
            step = zeros(21, 21);
            step(:, 11:21) = 0.6;
            step = step + 0.2;

            b = imbilatfilt(step, 0.001, 2);
            assert(isequal(size(b), [21 21]));
            assert(abs(b(11, 10) - 0.2) < 0.05);       % the edge is still an edge

            g = imguidedfilter(step);                  % one argument: guided by itself
            assert(isequal(size(g), [21 21]));

            g2 = imguidedfilter(step, step, 'NeighborhoodSize', [3 3], 'DegreeOfSmoothing', 1e-12);
            assert(max(max(abs(g2 - step))) < 1e-6);

            d = imdiffusefilt(step, 'NumberOfIterations', 3, 'GradientThreshold', 0.1, ...
                              'Connectivity', 'minimal', 'ConductionMethod', 'quadratic');
            assert(isequal(size(d), [21 21]));
            """);

        string message = await RunExpectingFailure(
            "imdiffusefilt(zeros(8), 'Connectivity', 'sideways');");
        Assert.Contains("maximal", message);
    }

    [Fact]
    public async Task Imdiffuseest_FeedsItsOwnEstimateStraightIntoImdiffusefilt()
    {
        await RunAsserting("""
            step = zeros(24, 24);
            step(:, 13:24) = 0.6;
            step = step + 0.2;

            [gradThresh, numIter] = imdiffuseest(step);
            assert(numIter == 5);
            assert(numel(gradThresh) == 5);
            assert(all(diff(gradThresh) < 0));         % each pass conducts across less

            out = imdiffusefilt(step, 'GradientThreshold', gradThresh, 'NumberOfIterations', numIter);
            assert(isequal(size(out), [24 24]));
            """);
    }

    [Fact]
    public async Task Imnlmfilt_EstimatesItsOwnSmoothingAndReportsIt()
    {
        await RunAsserting("""
            flat = 0.6 * ones(16, 16);
            [B, estDoS] = imnlmfilt(flat, 'SearchWindowSize', 7, 'ComparisonWindowSize', 3);
            assert(abs(estDoS) < 1e-12);               % a flat picture carries no noise
            assert(max(max(abs(B - 0.6))) < 1e-9);

            given = imnlmfilt(flat, 'DegreeOfSmoothing', 0.05, 'SearchWindowSize', 5);
            assert(isequal(size(given), [16 16]));
            """);

        string message = await RunExpectingFailure("imnlmfilt(zeros(8), 'SearchWindowSize', 8);");
        Assert.Contains("odd", message);
    }

    [Fact]
    public async Task Imreducehaze_AndImlocalbrighten_HandBackTheirTransmissionMaps()
    {
        await RunAsserting("""
            hazy = zeros(24, 24, 3);
            for k = 1:3
                hazy(:, :, k) = 0.6;
            end
            hazy(1:6, :, :) = 1;

            [B, T] = imreducehaze(hazy, 0.9, 'Method', 'simpledcp', 'ContrastEnhancement', 'none');
            assert(isequal(size(B), [24 24 3]));
            assert(isequal(size(T), [24 24]));
            assert(min(min(T)) >= 0 && max(max(T)) <= 1);

            boosted = imreducehaze(hazy, 0.8, 'Method', 'approxdcp', ...
                                   'ContrastEnhancement', 'boost', 'BoostAmount', 0.2, ...
                                   'AtmosphericLight', [1 1 1]);
            assert(isequal(size(boosted), [24 24 3]));

            [u, v] = meshgrid(0:19, 0:19);
            dim = 0.05 + 0.2 * (u + v) / 38;
            [C, T2] = imlocalbrighten(dim, 1, 'AlphaBlend', true);
            assert(isequal(size(C), [20 20]));
            assert(isequal(size(T2), [20 20]));
            assert(mean(C(:)) >= mean(dim(:)) - 1e-9);
            """);

        string message = await RunExpectingFailure("imreducehaze(zeros(8, 8, 3), 0.5, 'Method', 'deep');");
        Assert.Contains("simpledcp", message);
    }

    [Fact]
    public async Task Fibermetric_AndMaxhessiannorm_FindABrightFibre()
    {
        await RunAsserting("""
            bar = zeros(41, 41);
            bar(20:22, :) = 1;

            n = maxhessiannorm(bar, 3);
            assert(n > 0);
            assert(abs(maxhessiannorm(0.5 * ones(20))) < 1e-12);

            V = fibermetric(bar, 3, 'StructureSensitivity', 0.5 * n);
            assert(isequal(size(V), [41 41]));
            assert(V(21, 21) > 0.5);
            assert(V(4, 21) < 0.05);

            dark = fibermetric(bar, 3, 'ObjectPolarity', 'dark', 'StructureSensitivity', 0.5 * n);
            assert(dark(21, 21) < 1e-9);

            % The default is a whole range of widths.
            spread = fibermetric(bar);
            assert(isequal(size(spread), [41 41]));
            """);

        string message = await RunExpectingFailure("fibermetric(zeros(8), 3, 'ObjectPolarity', 'shiny');");
        Assert.Contains("bright", message);
    }

    [Fact]
    public async Task Imnoise_KnowsEveryDocumentedKindAndComplainsAboutTheRest()
    {
        await RunAsserting("""
            I = 0.5 * ones(32, 32);

            % Gaussian takes a mean and then a variance, in that order.
            shifted = (imnoise(I, 'gaussian', 0.2, 0));
            assert(abs(mean(shifted(:)) - 0.7) < 1e-9);

            % Speckle is multiplicative, so black stays black.
            dark = (imnoise(zeros(16, 16), 'speckle', 0.05));
            assert(max(max(dark)) < 1e-12);

            % Poisson takes no strength: the picture is the rate.
            shot = imnoise(I, 'poisson');
            assert(isequal(size(shot), [32 32]));

            % A variance of zero anywhere means no noise there.
            v = zeros(32, 32);
            v(:, 17:32) = 0.01;
            local = (imnoise(I, 'localvar', v));
            assert(max(max(abs(local(:, 1:16) - 0.5))) < 1e-12);
            right = local(:, 17:32);
            assert(std(right(:)) > 0.02);

            % The curve form maps intensity to variance.
            curve = (imnoise(I, 'localvar', [0 1], [0 0]));
            assert(max(max(abs(curve - 0.5))) < 1e-12);
            """);

        string message = await RunExpectingFailure("imnoise(zeros(4), 'rayleigh');");
        Assert.Contains("speckle", message);
    }

    [Fact]
    public async Task TheFilteringFamily_NowTakesAnImageAnArrayOfPlanesOrAMatrix()
    {
        await RunAsserting("""
            % An h-by-w-by-3 array is what MATLAB calls an RGB image, and it has to survive a filter
            % as one — three channels in, three channels out.
            rgb = zeros(8, 8, 3);
            rgb(:, :, 1) = 0.2;
            rgb(:, :, 2) = 0.5;
            rgb(:, :, 3) = 0.8;

            blurred = imgaussfilt(rgb, 1);
            assert(isequal(size(blurred), [8 8 3]));
            assert(abs(blurred(4, 4, 2) - 0.5) < 1e-9);

            sharp = imsharpen(rgb);
            assert(isequal(size(sharp), [8 8 3]));

            % A one-value-per-pixel answer is a plain matrix whatever went in.
            V = fibermetric(rgb, 3);
            assert(isequal(size(V), [8 8]));
            """);
    }
}
