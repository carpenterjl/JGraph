using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave I as a <c>.m</c> script sees it: filter design, the spread-function/transfer-function
/// pair, the four deblurring methods, and Gabor filtering.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabDesignBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabDesignBuiltinTests()
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
    public async Task Freqspace_AnswersDifferentlyForOneOutputAndTwo()
    {
        await RunAsserting("""
            f = freqspace(8);
            assert(isequal(size(f), [1 4]));
            assert(abs(f(1)) < 1e-12);
            assert(abs(f(4) - 0.75) < 1e-12);

            [f1, f2] = freqspace(8);
            assert(isequal(size(f1), [1 8]));
            assert(abs(f1(1) + 1) < 1e-12);

            [f1, f2] = freqspace([4 6]);
            assert(length(f1) == 6);
            assert(length(f2) == 4);

            [x, y] = freqspace([4 6], 'meshgrid');
            assert(isequal(size(x), [4 6]));
            assert(isequal(size(y), [4 6]));
            assert(abs(x(1, 3) - x(4, 3)) < 1e-12);
            assert(abs(y(2, 1) - y(2, 5)) < 1e-12);
            """);
    }

    [Fact]
    public async Task Freqz2_MeasuresTheResponseOfADesignedFilter()
    {
        await RunAsserting("""
            h = fspecial('average', 5);
            [H, f1, f2] = freqz2(h, 16, 16);
            assert(isequal(size(H), [16 16]));
            assert(length(f1) == 16);
            assert(length(f2) == 16);

            % An averaging kernel sums to one, so it passes a flat field through untouched.
            middle = H(9, 9);
            assert(abs(f1(9)) < 1e-12);
            assert(abs(middle - 1) < 1e-10);

            % And the default is a sixty-four by sixty-four grid.
            assert(isequal(size(freqz2(h)), [64 64]));
            """);
    }

    [Fact]
    public async Task Fsamp2_MatchesTheResponseItWasGiven()
    {
        await RunAsserting("""
            [f1, f2] = freqspace(11, 'meshgrid');
            Hd = zeros(11, 11);
            Hd(sqrt(f1 .^ 2 + f2 .^ 2) < 0.5) = 1;

            h = fsamp2(Hd);
            assert(isequal(size(h), [11 11]));

            % Frequency sampling promises the response only at the sample points, and there it is exact.
            H = freqz2(h, 11, 11);
            assert(max(max(abs(real(H) - Hd))) < 1e-9);
            """);
    }

    [Fact]
    public async Task Ftrans2_TurnsAOneDimensionalFilterIntoATwoDimensionalOne()
    {
        await RunAsserting("""
            b = [1 2 3 4 3 2 1] / 16;
            h = ftrans2(b);
            assert(isequal(size(h), [7 7]));

            % A lowpass stays a lowpass: it still passes a flat field, and the corner of the plane is gone.
            H = freqz2(h, [3 3]);
            assert(abs(real(H(2, 2)) - sum(b)) < 1e-9);
            assert(abs(real(H(1, 1))) < 0.05);

            % Its own transform can be given instead of McClellan's, and a wider one grows the answer:
            % the Chebyshev recurrence convolves by the transform once per pair of taps.
            assert(isequal(size(ftrans2(b, ones(5, 5) / 25)), [13 13]));
            """);
    }

    [Fact]
    public async Task Fwind1AndFwind2_TaperTheSameDesign()
    {
        await RunAsserting("""
            [f1, f2] = freqspace(21, 'meshgrid');
            Hd = zeros(21, 21);
            Hd(sqrt(f1 .^ 2 + f2 .^ 2) < 0.4) = 1;

            win = 0.54 - 0.46 * cos(2 * pi * (0:10) / 10);
            h1 = fwind1(Hd, win);
            assert(isequal(size(h1), [11 11]));

            h2 = fwind1(Hd, win, win);
            assert(isequal(size(h2), [11 11]));

            h3 = fwind2(Hd, win' * win);
            assert(max(max(abs(h2 - h3))) < 1e-12);

            % A rotated window is circular, so the corners of the kernel are dead.
            assert(abs(h1(1, 1)) < 1e-12);
            assert(abs(h2(1, 1)) > 0);
            """);
    }

    [Fact]
    public async Task Convmtx2_MultipliesTheWayConv2Filters()
    {
        await RunAsserting("""
            H = [1 2; 3 4];
            X = magic(3);
            T = convmtx2(H, 3, 3);
            assert(isequal(size(T), [16 9]));

            Y = reshape(T * X(:), 4, 4);
            assert(max(max(abs(Y - conv2(X, H)))) < 1e-10);
            """);
    }

    [Fact]
    public async Task Psf2otfAndOtf2psf_AreEachOthersUndoing()
    {
        await RunAsserting("""
            psf = fspecial('gaussian', 5, 1.2);
            otf = psf2otf(psf, [16 16]);
            assert(isequal(size(otf), [16 16]));

            % A spread function that sums to one passes a flat field, which is the value at zero.
            assert(abs(otf(1, 1) - 1) < 1e-10);

            back = otf2psf(otf, [5 5]);
            assert(max(max(abs(back - psf))) < 1e-10);
            """);
    }

    [Fact]
    public async Task Psf2otf_RefusesASpreadFunctionBiggerThanThePicture()
    {
        string message = await RunExpectingFailure("psf2otf(ones(9, 9), [4 4])");
        Assert.Contains("psf2otf", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Edgetaper_LeavesTheMiddleAloneAndSoftensTheSeam()
    {
        await RunAsserting("""
            [~, rows] = meshgrid(1:48, 1:48);
            I = 0.1 + 0.8 * (rows - 1) / 47;
            J = edgetaper(I, fspecial('gaussian', 9, 2));

            assert(abs(J(24, 24) - I(24, 24)) < 1e-8);
            assert(sum(abs(J(1, :) - J(48, :))) < sum(abs(I(1, :) - I(48, :))));
            """);
    }

    [Fact]
    public async Task Deconvwnr_UndoesANoiselessBlur()
    {
        await RunAsserting("""
            [cols, ~] = meshgrid(1:32, 1:32);
            I = 0.2 + 0.3 * cols / 32;
            I(9:16, 9:24) = 0.9;
            psf = fspecial('gaussian', 7, 1.5);
            blurred = imfilter(I, psf, 'circular', 'conv');

            restored = deconvwnr(blurred, psf);
            assert(mean(mean((restored - I) .^ 2)) < 1e-8);

            % A stated noise-to-signal ratio holds the answer back rather than dividing straight through.
            guarded = deconvwnr(blurred, psf, 0.01);
            assert(mean(mean((guarded - I) .^ 2)) > mean(mean((restored - I) .^ 2)));
            """);
    }

    [Fact]
    public async Task Deconvreg_HandsBackTheMultiplierItSolvedFor()
    {
        await RunAsserting("""
            [cols, ~] = meshgrid(1:32, 1:32);
            I = 0.2 + 0.3 * cols / 32;
            I(9:16, 9:24) = 0.9;
            psf = fspecial('gaussian', 7, 1.5);
            blurred = imfilter(I, psf, 'circular', 'conv');

            [restored, lagra] = deconvreg(blurred, psf, 0);
            assert(abs(lagra - 1e-9) < 1e-15);
            assert(mean(mean((restored - I) .^ 2)) < 1e-4);

            % Claim there is noise and more of the data is held back, which needs a bigger multiplier.
            [~, bigger] = deconvreg(blurred, psf, 0.001);
            assert(bigger > lagra);
            """);
    }

    [Fact]
    public async Task Deconvlucy_SharpensAndKeepsThePictureNonNegative()
    {
        await RunAsserting("""
            [cols, ~] = meshgrid(1:32, 1:32);
            I = 0.2 + 0.3 * cols / 32;
            I(9:16, 9:24) = 0.9;
            psf = fspecial('gaussian', 7, 1.5);
            blurred = imfilter(I, psf, 'circular', 'conv');

            restored = deconvlucy(blurred, psf, 15);
            assert(mean(mean((restored - I) .^ 2)) < mean(mean((blurred - I) .^ 2)));
            assert(min(min(restored)) >= 0);

            % Only ever multiplying means the total brightness cannot move.
            assert(abs(sum(sum(restored)) - sum(sum(blurred))) < 1e-6 * sum(sum(blurred)));
            """);
    }

    [Fact]
    public async Task Deconvblind_HandsBackABlurThatStillSumsToOne()
    {
        await RunAsserting("""
            [cols, ~] = meshgrid(1:32, 1:32);
            I = 0.2 + 0.3 * cols / 32;
            I(9:16, 9:24) = 0.9;
            psf = fspecial('gaussian', 7, 1.4);
            blurred = imfilter(I, psf, 'circular', 'conv');

            [restored, found] = deconvblind(blurred, psf, 8);
            assert(isequal(size(found), [7 7]));
            assert(abs(sum(sum(found)) - 1) < 1e-8);
            assert(mean(mean((restored - I) .^ 2)) < mean(mean((blurred - I) .^ 2)));

            % Handed the right blur it stays on it rather than wandering off.
            assert(abs(found(4, 4) - psf(4, 4)) < 0.05 * psf(4, 4));
            """);
    }

    [Fact]
    public async Task Gabor_BuildsOneFilterOrABankOfThem()
    {
        await RunAsserting("""
            g = gabor(4, 90);
            assert(strcmp(class(g), 'gabor'));
            assert(g.Wavelength == 4);
            assert(g.Orientation == 90);
            assert(g.SpatialFrequencyBandwidth == 1);
            assert(g.SpatialAspectRatio == 0.5);

            bank = gabor([4 8], [0 45 90], 'SpatialAspectRatio', 0.7);
            assert(length(bank) == 6);
            assert(bank{1}.Wavelength == 4);
            assert(bank{4}.Wavelength == 8);
            assert(bank{4}.SpatialAspectRatio == 0.7);
            """);
    }

    [Fact]
    public async Task Imgaborfilt_AnswersLoudestForTheStripesItIsTunedTo()
    {
        await RunAsserting("""
            [cols, ~] = meshgrid(1:64, 1:64);
            stripes = 0.5 + 0.5 * cos(2 * pi * cols / 8);

            [tuned, phase] = imgaborfilt(stripes, 8, 0);
            assert(isequal(size(tuned), [64 64]));
            assert(isequal(size(phase), [64 64]));

            across = imgaborfilt(stripes, 8, 90);
            assert(tuned(32, 32) > 4 * across(32, 32));

            % A bank answers once per filter, stacked as pages.
            bankMag = imgaborfilt(stripes, gabor(8, [0 90]));
            assert(isequal(size(bankMag), [64 64 2]));
            assert(bankMag(32, 32, 1) > bankMag(32, 32, 2));
            """);
    }

    [Fact]
    public async Task Gabor_RefusesAWavelengthTooShortToSample()
    {
        string message = await RunExpectingFailure("gabor(1, 0)");
        Assert.Contains("gabor", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wavelength", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fsamp2_SaysWhatItNeededWhenGivenTheWrongCount()
    {
        string message = await RunExpectingFailure("fsamp2(ones(4, 4), ones(4, 4))");
        Assert.Contains("fsamp2", message, StringComparison.OrdinalIgnoreCase);
    }
}
