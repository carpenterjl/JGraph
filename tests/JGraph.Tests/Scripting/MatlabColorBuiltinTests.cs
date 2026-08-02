using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave D as a <c>.m</c> script sees it: the colour conversions on both images and colormaps,
/// the options that steer them, white balance, the difference metrics, and the indexed-image family.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabColorBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabColorBuiltinTests()
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
    public async Task ColorConversions_TakeAColormapAndAnswerWithOne()
    {
        await RunAsserting("""
            map = [1 1 1; 0 0 0; 1 0 0; 0.5 0.5 0.5];

            lab = rgb2lab(map);
            assert(isequal(size(lab), [4 3]));
            assert(abs(lab(1, 1) - 100) < 1e-6);      % white
            assert(abs(lab(1, 2)) < 1e-6);
            assert(abs(lab(1, 3)) < 1e-6);
            assert(abs(lab(2, 1)) < 1e-9);            % black
            assert(lab(3, 2) > 60);                   % red is strongly positive on a*

            back = lab2rgb(lab);
            assert(max(max(abs(back - map))) < 1e-8);
            """);
    }

    [Fact]
    public async Task Whitepoint_NamesTheStandardIlluminants()
    {
        await RunAsserting("""
            d65 = whitepoint('d65');
            assert(isequal(size(d65), [1 3]));
            assert(abs(d65(1) - 0.9504) < 1e-9);
            assert(abs(d65(2) - 1) < 1e-12);

            d50 = whitepoint('d50');
            assert(abs(d50(3) - 0.8249) < 1e-9);
            e = whitepoint('e');
            assert(abs(e(3) - 1) < 1e-12);

            % The bare call is the ICC profile connection space.
            icc = whitepoint;
            assert(abs(icc(1) - 31595/32768) < 1e-12);
            """);

        string message = await RunExpectingFailure("whitepoint('d99');");
        Assert.Contains("'d65'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RgbToXyz_TakesAColorSpaceAndAWhitePoint()
    {
        await RunAsserting("""
            % sRGB white under D65 is the D65 tristimulus itself.
            xyz = rgb2xyz([1 1 1]);
            assert(max(abs(xyz - whitepoint('d65'))) < 1e-9);

            % Asked for D50 instead, white lands on D50 — the adaptation step is not skipped.
            xyz50 = rgb2xyz([1 1 1], 'WhitePoint', 'd50');
            assert(max(abs(xyz50 - whitepoint('d50'))) < 1e-8);

            % A wider gamut puts the same encoded triple somewhere else.
            wide = rgb2xyz([0.8 0.2 0.4], 'ColorSpace', 'adobe-rgb-1998');
            narrow = rgb2xyz([0.8 0.2 0.4]);
            assert(max(abs(wide - narrow)) > 0.01);

            assert(max(abs(xyz2rgb(rgb2xyz([0.3 0.6 0.9])) - [0.3 0.6 0.9])) < 1e-9);
            """);

        string message = await RunExpectingFailure("rgb2xyz([1 1 1], 'ColorSpace', 'cmyk');");
        Assert.Contains("prophoto-rgb", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HsvAndTransmissionSpaces_RoundTripAndLandWhereExpected()
    {
        await RunAsserting("""
            hsv = rgb2hsv([1 0 0; 0 1 0; 0.5 0.5 0.5]);
            assert(abs(hsv(1, 1)) < 1e-12);
            assert(abs(hsv(2, 1) - 1/3) < 1e-12);
            assert(abs(hsv(3, 2)) < 1e-12);
            assert(max(max(abs(hsv2rgb(hsv) - [1 0 0; 0 1 0; 0.5 0.5 0.5]))) < 1e-12);

            % Studio swing: white is 235 and black 16, both over 255.
            y = rgb2ycbcr([1 1 1; 0 0 0]);
            assert(abs(y(1, 1) - 235/255) < 1e-9);
            assert(abs(y(2, 1) - 16/255) < 1e-9);
            assert(abs(y(1, 2) - 128/255) < 1e-9);
            assert(max(max(abs(ycbcr2rgb(y) - [1 1 1; 0 0 0]))) < 1e-5);

            % NTSC puts white entirely on luminance.
            yiq = rgb2ntsc([1 1 1]);
            assert(abs(yiq(1) - 1) < 1e-12);
            assert(abs(yiq(2)) < 1e-12);
            assert(max(abs(ntsc2rgb(rgb2ntsc([0.3 0.6 0.9])) - [0.3 0.6 0.9])) < 1e-9);
            """);
    }

    [Fact]
    public async Task Gamma_IsUndoneAndReappliedExactly()
    {
        await RunAsserting("""
            linear = rgb2lin([0.5 0.04 1]);
            assert(abs(linear(1) - 0.21404114) < 1e-7);
            assert(abs(linear(2) - 0.04/12.92) < 1e-12);
            assert(abs(linear(3) - 1) < 1e-12);
            assert(max(abs(lin2rgb(linear) - [0.5 0.04 1])) < 1e-10);

            % Adobe RGB's plain gamma is a different curve.
            adobe = rgb2lin(0.5*[1 1 1], 'ColorSpace', 'adobe-rgb-1998');
            assert(abs(adobe(1) - 0.5^(563/256)) < 1e-12);
            """);
    }

    [Fact]
    public async Task RgbToLightness_IsTheLStarChannelAlone()
    {
        await RunAsserting("""
            map = [1 1 1; 0 0 0; 0.5 0.5 0.5];
            L = rgb2lightness(map);
            lab = rgb2lab(map);
            assert(isequal(size(L), [3 1]));
            for k = 1:3
                assert(abs(L(k) - lab(k, 1)) < 1e-10);
            end
            """);
    }

    [Fact]
    public async Task Chromadapt_TurnsTheIlluminantGrey()
    {
        await RunAsserting("""
            illum = [0.9 0.75 0.5];
            neutral = chromadapt(illum, illum);
            assert(abs(neutral(1) - neutral(2)) < 1e-3);
            assert(abs(neutral(2) - neutral(3)) < 1e-3);

            % Every method has to agree on that much, however differently they get there.
            bradford = chromadapt(illum, illum, 'Method', 'bradford');
            vonkries = chromadapt(illum, illum, 'Method', 'vonkries');
            simple = chromadapt(illum, illum, 'Method', 'simple');
            assert(abs(bradford(1) - bradford(3)) < 5e-3);
            assert(abs(vonkries(1) - vonkries(3)) < 5e-3);
            assert(abs(simple(1) - simple(3)) < 5e-3);
            """);

        string message = await RunExpectingFailure("chromadapt([1 1 1], [1 1 1], 'Method', 'guess');");
        Assert.Contains("vonkries", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlluminantEstimators_AgreeThereIsARedCast()
    {
        await RunAsserting("""
            scene = zeros(64, 3);
            for k = 1:64
                grey = 0.2 + 0.6*k/64;
                scene(k, 1) = grey * 0.9;
                scene(k, 2) = grey * 0.6;
                scene(k, 3) = grey * 0.35;
            end

            grey = illumgray(scene);
            white = illumwhite(scene);
            pca = illumpca(scene);
            assert(isequal(size(grey), [1 3]));
            assert(grey(1) > grey(2) && grey(2) > grey(3));
            assert(white(1) > white(2) && white(2) > white(3));
            assert(pca(1) > pca(2) && pca(2) > pca(3));

            % The percentile argument is accepted and changes nothing about the ordering.
            trimmed = illumgray(scene, [5 5]);
            assert(trimmed(1) > trimmed(3));
            top = illumwhite(scene, 10);
            assert(top(1) > top(3));
            """);
    }

    [Fact]
    public async Task ColorDifference_MatchesPublishedValuesAndTakesItsOptions()
    {
        await RunAsserting("""
            % Sharma's test pair, given directly in L*a*b*.
            a = [50 2.6772 -79.7751];
            b = [50 0 -82.7485];
            assert(abs(imcolordiff(a, b, 'isInputLab', true) - 2.0425) < 1e-4);

            % CIE94 weighs the same pair differently and lands lower.
            cie94 = imcolordiff(a, b, 'isInputLab', true, 'Standard', 'CIE94');
            assert(abs(cie94 - 1.3950) < 1e-3);

            % CIE76 is plain Euclidean distance, so a pure lightness step is the step.
            assert(abs(deltaE([50 0 0], [60 0 0], 'isInputLab', true) - 10) < 1e-9);

            % Identical colours are zero however they are measured.
            assert(abs(deltaE([0.3 0.6 0.9], [0.3 0.6 0.9])) < 1e-12);
            assert(abs(imcolordiff([0.3 0.6 0.9], [0.3 0.6 0.9])) < 1e-12);

            assert(abs(colorangle([1 0 0], [0 1 0]) - 90) < 1e-9);
            assert(abs(colorangle([1 1 1], [0.2 0.2 0.2])) < 1e-9);
            """);

        string message = await RunExpectingFailure(
            "imcolordiff([1 0 0], [0 1 0], 'Standard', 'CIE2020');");
        Assert.Contains("CIEDE2000", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabAndXyzEncodings_RoundTripThroughTheirIntegerRanges()
    {
        await RunAsserting("""
            lab = [100 0 0; 50 -40 60; 0 0 0];

            eight = lab2uint8(lab);
            assert(eight(1, 1) == 255);
            assert(eight(1, 2) == 128);
            assert(eight(2, 2) == 88);      % -40 + 128
            assert(eight(3, 1) == 0);

            sixteen = lab2uint16(lab);
            assert(sixteen(1, 1) == 65280);
            assert(sixteen(1, 2) == 128*257);

            xyz = xyz2uint16([1 1 1; 0.5 0.5 0.5]);
            assert(xyz(1, 1) == 32768);
            assert(xyz(2, 1) == 16384);
            """);

        // A colormap has no class tag, so there is nothing to say which encoding it used.
        string message = await RunExpectingFailure("lab2double([255 128 128]);");
        Assert.Contains("uint8 or uint16", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexedImages_RoundTripThroughAPalette()
    {
        await RunAsserting("""
            I = [0 0.25; 0.5 1];

            [X, map] = gray2ind(I, 5);
            assert(isequal(size(X), [2 2]));
            assert(isequal(size(map), [5 3]));
            assert(X(1, 1) == 1);           % black is the first colormap row
            assert(X(2, 2) == 5);           % white is the last
            assert(abs(map(3, 1) - 0.5) < 1e-12);

            % An index plane and its map reconstruct the picture.
            G = ind2gray(X, map);
            assert(abs(G(2, 2) - 1) < 1e-9);
            assert(abs(G(1, 1)) < 1e-9);

            C = ind2rgb(X, map);
            assert(size(C, 3) == 3);
            """);
    }

    [Fact]
    public async Task RgbToInd_ReducesToAPaletteAndBack()
    {
        await RunAsserting("""
            % Four flat quadrants of two colours; a two-entry palette must reproduce them exactly.
            RGB = zeros(4, 4, 3);
            RGB(:, 1:2, 1) = 1;
            RGB(:, 3:4, 3) = 1;

            [X, map] = rgb2ind(RGB, 2, 'nodither');
            assert(isequal(size(X), [4 4]));
            assert(size(map, 1) == 2);
            assert(X(1, 1) ~= X(1, 4));

            back = ind2rgb(X, map);
            assert(abs(back(1, 1, 1) - 1) < 1e-9);
            assert(abs(back(1, 4, 3) - 1) < 1e-9);

            % A tolerance asks for a uniform grid instead of a fitted palette.
            [~, coarse] = rgb2ind(RGB, 0.5, 'nodither');
            assert(size(coarse, 1) >= 2);

            % And imapprox takes it down further.
            [Y, small] = imapprox(X, map, 1, 'nodither');
            assert(size(small, 1) == 1);
            assert(isequal(size(Y), [4 4]));
            """);
    }

    [Fact]
    public async Task Imsplit_AndCmap2gray_AndRgb2grayOnAColormap()
    {
        await RunAsserting("""
            RGB = zeros(2, 2, 3);
            RGB(:, :, 1) = 0.25;
            RGB(:, :, 2) = 0.5;
            RGB(:, :, 3) = 0.75;

            [R, G, B] = imsplit(RGB);
            assert(abs(R(1, 1) - 0.25) < 1e-9);
            assert(abs(G(2, 2) - 0.5) < 1e-9);
            assert(abs(B(1, 2) - 0.75) < 1e-9);

            % A colormap converted to grey has three equal columns.
            map = [1 0 0; 0 1 0; 0 0 1];
            gray = cmap2gray(map);
            assert(isequal(size(gray), [3 3]));
            assert(abs(gray(1, 1) - gray(1, 3)) < 1e-12);
            assert(gray(2, 1) > gray(1, 1));      % green weighs most

            % rgb2gray does the same thing when handed a colormap rather than a picture.
            assert(max(max(abs(rgb2gray(map) - gray))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Demosaic_ReconstructsAFlatFieldAndNamesBadAlignments()
    {
        await RunAsserting("""
            cfa = 0.4 * ones(6, 6);
            RGB = demosaic(cfa, 'rggb');
            assert(size(RGB, 3) == 3);
            assert(abs(RGB(3, 3, 1) - 0.4) < 1e-9);
            assert(abs(RGB(3, 3, 2) - 0.4) < 1e-9);
            assert(abs(RGB(3, 3, 3) - 0.4) < 1e-9);
            """);

        string message = await RunExpectingFailure("demosaic(zeros(4), 'rgbg');");
        Assert.Contains("'bggr'", message, StringComparison.Ordinal);
    }
}
