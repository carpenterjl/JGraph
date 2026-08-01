using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave C as a <c>.m</c> script sees it: the resampling options <c>imresize</c> gained, the
/// direction <c>imrotate</c> turns, MATLAB's spatial <c>imcrop</c> rectangle, and the transform
/// objects — <c>affine2d</c> and friends as tagged structs, <c>fitgeotrans</c>, <c>imwarp</c>, and
/// the frames that decide where a warped picture lands.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabImageGeometryTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabImageGeometryTests()
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
    public async Task Imresize_SizesTheOutputByScaleSizeAndAspectRatio()
    {
        await RunAsserting("""
            A = zeros(9, 6);

            % A factor rounds up, so halving an odd dimension keeps the extra row.
            assert(isequal(size(imresize(A, 0.5)), [5 3]));
            assert(isequal(size(imresize(A, 2)), [18 12]));

            % Two numbers are the size to land on, not a factor.
            assert(isequal(size(imresize(A, [4 4])), [4 4]));

            % NaN asks for whatever keeps the aspect ratio.
            assert(isequal(size(imresize(A, [3 NaN])), [3 2]));
            assert(isequal(size(imresize(A, [NaN 12])), [18 12]));

            % The same, spelled as name-value options.
            assert(isequal(size(imresize(A, 'Scale', 0.5)), [5 3]));
            assert(isequal(size(imresize(A, 'OutputSize', [4 4])), [4 4]));
            """);
    }

    [Fact]
    public async Task Imresize_BicubicIsTheDefaultAndReproducesARamp()
    {
        await RunAsserting("""
            % A ramp along the columns; bicubic reconstructs a straight line exactly, so doubling it
            % must land back on the line wherever all four taps are real samples.
            A = repmat(1:8, 3, 1);
            B = imresize(A, [3 16]);
            for k = 4:13
                assert(abs(B(2, k) - (k/2 + 0.25)) < 1e-9);
            end

            % Naming the default explicitly changes nothing.
            assert(max(max(abs(B - imresize(A, [3 16], 'bicubic')))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imresize_AntialiasingIsWhatSeparatesBoxFromNearest()
    {
        await RunAsserting("""
            A = reshape(1:16, 4, 4);

            % 'box' halves by averaging each 2-by-2 block…
            B = imresize(A, 0.5, 'box');
            assert(abs(B(1,1) - (A(1,1) + A(1,2) + A(2,1) + A(2,2))/4) < 1e-12);

            % …'nearest' point-samples one pixel out of the four…
            C = imresize(A, 0.5, 'nearest');
            assert(C(1,1) == A(2,2));

            % …and turning antialiasing off makes 'box' the same as 'nearest'.
            D = imresize(A, 0.5, 'box', 'Antialiasing', false);
            assert(isequal(D, C));
            """);
    }

    [Fact]
    public async Task Imresize_TakesLanczosAndTheMethodOption()
    {
        await RunAsserting("""
            A = reshape(1:36, 6, 6);
            assert(isequal(size(imresize(A, 2, 'lanczos3')), [12 12]));
            assert(max(max(abs(imresize(A, 2, 'Method', 'lanczos2') - imresize(A, 2, 'lanczos2')))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imresize_NamesTheMethodsItKnows()
    {
        string message = await RunExpectingFailure("imresize(zeros(4), 2, 'sinc');");
        Assert.Contains("lanczos3", message, StringComparison.Ordinal);

        string twice = await RunExpectingFailure("imresize(zeros(4), 2, 'bicubic', 'Method', 'nearest');");
        Assert.Contains("twice", twice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imrotate_TurnsCounterClockwiseAndCropsOnRequest()
    {
        await RunAsserting("""
            A = [1 2; 3 4];

            % A quarter turn anticlockwise puts the top-right corner top-left.
            assert(isequal(imrotate(A, 90), [2 4; 1 3]));
            assert(isequal(imrotate(A, 180), [4 3; 2 1]));

            % 'loose' (the default) grows the frame; 'crop' keeps the input size.
            B = zeros(4, 8);
            assert(isequal(size(imrotate(B, 90)), [8 4]));
            assert(isequal(size(imrotate(B, 45, 'crop')), [4 8]));
            assert(isequal(size(imrotate(B, 45, 'bicubic', 'loose')), [9 9]));
            """);
    }

    [Fact]
    public async Task Imcrop_TakesASpatialRectangleAndReportsIt()
    {
        await RunAsserting("""
            A = reshape(1:100, 10, 10);

            % The rectangle spans pixel centres at both ends, so it yields height+1 by width+1.
            B = imcrop(A, [3 2 4 3]);
            assert(isequal(size(B), [4 5]));
            assert(B(1, 1) == A(2, 3));
            assert(B(4, 5) == A(5, 7));

            [C, rect] = imcrop(A, [3 2 4 3]);
            assert(isequal(size(C), [4 5]));
            assert(isequal(rect, [3 2 4 3]));

            % With no rectangle there is nothing to draw one on, so the whole image comes back.
            [D, whole] = imcrop(A);
            assert(isequal(size(D), [10 10]));
            assert(isequal(whole, [0.5 0.5 10 10]));
            """);
    }

    [Fact]
    public async Task Affine2d_IsAStructThatStillAnswersToClass()
    {
        await RunAsserting("""
            tform = affine2d([1 0 0; 0 1 0; 5 -2 1]);
            assert(strcmp(class(tform), 'affine2d'));
            assert(isequal(size(tform.T), [3 3]));
            assert(tform.T(3, 1) == 5);
            assert(tform.Dimensionality == 2);

            assert(strcmp(class(affine2d()), 'affine2d'));
            assert(strcmp(class(projective2d([1 0 0.001; 0 1 0; 0 0 1])), 'projective2d'));
            """);

        string message = await RunExpectingFailure("affine2d([1 0 0.5; 0 1 0; 0 0 1]);");
        Assert.Contains("projective2d", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rigid2d_KeepsRotationAndTranslationApartAndRefusesAScaling()
    {
        await RunAsserting("""
            th = 30;
            R = [cosd(th) sind(th); -sind(th) cosd(th)];
            tform = rigid2d(R, [10 20]);

            assert(strcmp(class(tform), 'rigid2d'));
            assert(isequal(tform.Translation, [10 20]));
            assert(abs(tform.Rotation(1, 2) - sind(th)) < 1e-12);
            assert(abs(tform.T(1, 1) - cosd(th)) < 1e-12);
            """);

        string message = await RunExpectingFailure("rigid2d([2 0; 0 2], [0 0]);");
        Assert.Contains("orthonormal", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imref2d_ReportsLimitsExtentsAndIntrinsicBounds()
    {
        await RunAsserting("""
            R = imref2d([4 6]);
            assert(strcmp(class(R), 'imref2d'));
            assert(isequal(R.ImageSize, [4 6]));
            assert(isequal(R.XWorldLimits, [0.5 6.5]));
            assert(isequal(R.YWorldLimits, [0.5 4.5]));
            assert(R.PixelExtentInWorldX == 1);
            assert(isequal(R.XIntrinsicLimits, [0.5 6.5]));

            % Scalars are the size of one pixel rather than a limit.
            S = imref2d([4 6], 2, 3);
            assert(isequal(S.XWorldLimits, [1 13]));
            assert(S.ImageExtentInWorldY == 12);

            T = imref2d([4 6], [0 12], [100 108]);
            assert(T.PixelExtentInWorldX == 2);
            assert(T.PixelExtentInWorldY == 2);
            """);
    }

    [Fact]
    public async Task Fitgeotrans_RecoversAMapAndTransformPointsRoundTrips()
    {
        await RunAsserting("""
            moving = [0 0; 1 0; 0 1; 2 3];
            fixedPoints = [1 2; 3 2; 1 5; 5 11];      % u = 2x + 1, v = 3y + 2

            tform = fitgeotrans(moving, fixedPoints, 'affine');
            assert(strcmp(class(tform), 'affine2d'));

            p = transformPointsForward(tform, [1 1; 2 2]);
            assert(abs(p(1, 1) - 3) < 1e-9);
            assert(abs(p(1, 2) - 5) < 1e-9);

            q = transformPointsInverse(tform, p);
            assert(max(max(abs(q - [1 1; 2 2]))) < 1e-9);

            % The separate-coordinate form gives back two outputs.
            [u, v] = transformPointsForward(tform, [0 1], [0 1]);
            assert(abs(u(2) - 3) < 1e-9);
            assert(abs(v(2) - 5) < 1e-9);
            """);
    }

    [Fact]
    public async Task Fitgeotrans_HandlesProjectiveAndNamesWhatItCannotDo()
    {
        await RunAsserting("""
            moving = [0 0; 100 0; 100 80; 0 80; 40 25];
            fixedPoints = zeros(5, 2);
            for k = 1:5
                x = moving(k, 1);
                y = moving(k, 2);
                w = 0.002*x - 0.001*y + 1;
                fixedPoints(k, 1) = (1.2*x - 0.15*y + 30) / w;
                fixedPoints(k, 2) = (0.1*x + 0.9*y + 12) / w;
            end

            tform = fitgeotrans(moving, fixedPoints, 'projective');
            assert(strcmp(class(tform), 'projective2d'));
            p = transformPointsForward(tform, [63 47]);
            w = 0.002*63 - 0.001*47 + 1;
            assert(abs(p(1) - (1.2*63 - 0.15*47 + 30)/w) < 1e-6);
            """);

        string message = await RunExpectingFailure(
            "fitgeotrans([0 0; 1 0; 0 1; 1 1], [0 0; 1 0; 0 1; 1 1], 'lwm');");
        Assert.Contains("'lwm'", message, StringComparison.Ordinal);

        string unknown = await RunExpectingFailure(
            "fitgeotrans([0 0; 1 0; 0 1], [0 0; 1 0; 0 1], 'rubbery');");
        Assert.Contains("nonreflectivesimilarity", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imwarp_ShiftsExactlyAndReportsWhereTheResultSits()
    {
        await RunAsserting("""
            A = reshape(1:25, 5, 5);
            tform = affine2d([1 0 0; 0 1 0; 2 1 1]);

            B = imwarp(A, tform, 'OutputView', imref2d(size(A)));
            assert(isequal(size(B), [5 5]));
            assert(abs(B(2, 3) - A(1, 1)) < 1e-12);
            assert(B(1, 1) == 0);

            % A fill value replaces the zeros where nothing was warped in.
            F = imwarp(A, tform, 'OutputView', imref2d(size(A)), 'FillValues', 9);
            assert(F(1, 1) == 9);

            % Without an output view the frame follows the transformed image.
            [C, RC] = imwarp(A, tform);
            assert(isequal(size(C), [5 5]));
            assert(isequal(RC.XWorldLimits, [2.5 7.5]));
            assert(isequal(RC.YWorldLimits, [1.5 6.5]));
            assert(abs(C(1, 1) - A(1, 1)) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imtranslate_ShiftsInPlaceOrGrowsToFit()
    {
        await RunAsserting("""
            A = [1 2; 3 4];

            B = imtranslate(A, [1 0]);
            assert(isequal(B, [0 1; 0 3]));

            C = imtranslate(A, [1 0], 'OutputView', 'full');
            assert(isequal(size(C), [2 3]));
            assert(isequal(C, [0 1 2; 0 3 4]));

            [D, RD] = imtranslate(A, [0 1], 'OutputView', 'full', 'FillValues', 7);
            assert(isequal(D, [7 7; 1 2; 3 4]));
            assert(isequal(RD.ImageSize, [3 2]));
            """);
    }

    [Fact]
    public async Task AffineOutputView_OffersTheThreeBoundsStyles()
    {
        await RunAsserting("""
            tform = affine2d([1 0 0; 0 1 0; 10 0 1]);

            centred = affineOutputView([4 6], tform);
            assert(isequal(centred.ImageSize, [4 6]));
            assert(isequal(centred.XWorldLimits, [10.5 16.5]));
            assert(isequal(centred.YWorldLimits, [0.5 4.5]));

            same = affineOutputView([4 6], tform, 'BoundsStyle', 'SameAsInput');
            assert(isequal(same.XWorldLimits, [0.5 6.5]));

            grow = affineOutputView([4 6], affine2d([2 0 0; 0 2 0; 0 0 1]), 'BoundsStyle', 'FollowOutput');
            assert(isequal(grow.ImageSize, [8 12]));
            """);

        string message = await RunExpectingFailure(
            "affineOutputView([4 4], affine2d(), 'BoundsStyle', 'Whatever');");
        Assert.Contains("FollowOutput", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impyramid_HalvesAndDoublesAndPreservesAFlatField()
    {
        await RunAsserting("""
            A = 0.4 * ones(5, 7);

            B = impyramid(A, 'reduce');
            assert(isequal(size(B), [3 4]));
            assert(abs(B(2, 2) - 0.4) < 1e-12);
            assert(abs(B(1, 1) - 0.4) < 1e-12);

            C = impyramid(B, 'expand');
            assert(isequal(size(C), [5 7]));
            assert(abs(C(3, 4) - 0.4) < 1e-12);
            """);

        string message = await RunExpectingFailure("impyramid(zeros(4), 'shrink');");
        Assert.Contains("'reduce'", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checkerboard_TilesSquaresAndGreysTheRightHalf()
    {
        await RunAsserting("""
            K = checkerboard(2, 1, 2);
            assert(isequal(size(K), [4 8]));
            assert(K(1, 1) == 0);
            assert(K(1, 3) == 1);
            assert(abs(K(1, 7) - 0.7) < 1e-12);
            assert(K(3, 1) == 1);

            % The defaults are ten pixels a square over four tiles each way.
            assert(isequal(size(checkerboard()), [80 80]));
            assert(isequal(size(checkerboard(5)), [40 40]));
            """);
    }
}
