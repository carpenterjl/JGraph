using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave H as a <c>.m</c> script sees it: the cosine transform, the Radon pair and the phantom,
/// quadtree decomposition through its sparse block map, and the two correlation searches.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabTransformBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabTransformBuiltinTests()
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
    public async Task Dct2_IsTheMatrixFormAndIdct2UndoesIt()
    {
        await RunAsserting("""
            A = magic(8) / 100;
            B = dct2(A);
            D = dctmtx(8);

            % The identity a script can check its own arithmetic against.
            assert(max(max(abs(B - D * A * D'))) < 1e-10);
            assert(max(max(abs(idct2(B) - A))) < 1e-10);

            % Orthonormal, so the transform preserves total energy.
            assert(abs(sum(sum(B .^ 2)) - sum(sum(A .^ 2))) < 1e-10);

            % A flat field has only a mean to describe.
            F = dct2(ones(4, 4) * 0.5);
            assert(abs(F(1, 1) - 2) < 1e-10);
            assert(abs(F(2, 3)) < 1e-10);
            """);
    }

    [Fact]
    public async Task Dct2_TakesASizeToPadOrCropTo()
    {
        await RunAsserting("""
            A = ones(4, 4);
            assert(isequal(size(dct2(A, 8, 6)), [8 6]));
            assert(isequal(size(dct2(A, [2 2])), [2 2]));
            assert(isequal(size(idct2(dct2(A), 4, 4)), [4 4]));
            """);
    }

    [Fact]
    public async Task Radon_ConservesTheTotalAndReportsItsBinCoordinates()
    {
        await RunAsserting("""
            I = zeros(16, 16);
            I(5:12, 6:11) = 1;

            [R, xp] = radon(I, [0 45 90]);
            assert(size(R, 2) == 3);
            assert(numel(xp) == size(R, 1));

            % A shadow weighs what the thing weighs, whichever way the light comes from.
            for k = 1:3
                assert(abs(sum(R(:, k)) - sum(I(:))) < 1e-8);
            end

            % The bins are unit-spaced and symmetric about the axis of rotation.
            assert(abs(xp(1) + xp(end)) < 1e-12);
            assert(abs(xp(2) - xp(1) - 1) < 1e-12);

            % No angle given means half a turn, one degree at a time.
            assert(size(radon(I), 2) == 180);
            """);
    }

    [Fact]
    public async Task Iradon_ReconstructsWhatRadonProjected()
    {
        await RunAsserting("""
            P = phantom('Modified Shepp-Logan', 64);
            theta = 0:179;
            R = radon(P, theta);
            [I, H] = iradon(R, theta, 'linear', 'Ram-Lak', 1, 64);

            assert(isequal(size(I), [64 64]));

            % The interior of the skull, the background outside it, and the total: the three things
            % a reconstruction has to get right before sharpness is worth discussing.
            assert(abs(I(33, 33) - 0.2) < 0.05);
            assert(abs(I(3, 3)) < 0.05);
            assert(abs(sum(I(:)) - sum(P(:))) < 0.03 * sum(P(:)));

            % The ramp passes almost nothing at zero frequency.
            assert(abs(H(1)) < 0.01 * max(H));
            """);
    }

    [Fact]
    public async Task Iradon_ReadsItsTrailingArgumentsByWhatTheyAre()
    {
        await RunAsserting("""
            P = phantom(32);
            theta = 0:5:175;
            R = radon(P, theta);

            % A word is an interpolation or a filter depending on which list it is in; a number at
            % most one is the frequency scaling, and a larger one is the output size.
            A = iradon(R, theta, 'nearest', 'Hann', 0.8, 40);
            assert(isequal(size(A), [40 40]));

            % Order does not matter, and neither does giving only some of them.
            B = iradon(R, theta, 40, 'Hann', 'nearest', 0.8);
            assert(max(max(abs(A - B))) < 1e-12);

            % A single increment stands for the whole sweep.
            C = iradon(R, 5, 'linear');
            assert(size(C, 1) == size(C, 2));
            """);
    }

    [Fact]
    public async Task Iradon_RefusesAFilterItDoesNotHave()
    {
        string message = await RunExpectingFailure("iradon(zeros(65, 4), [0 45 90 135], 'butterworth')");
        Assert.Contains("iradon", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shepp-Logan", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phantom_DrawsTheHeadAndHandsBackItsEllipses()
    {
        await RunAsserting("""
            [P, E] = phantom('Modified Shepp-Logan', 128);
            assert(isequal(size(P), [128 128]));
            assert(isequal(size(E), [10 6]));

            % A bright shell, a dimmer interior, nothing in the corner.
            assert(abs(P(65, 65) - 0.2) < 1e-10);
            assert(P(3, 3) == 0);
            assert(abs(max(P(65, :)) - 1) < 1e-10);

            % The original phantom shares the geometry and differs only in contrast.
            [~, E0] = phantom('Shepp-Logan', 8);
            assert(abs(E0(2, 1) + 0.98) < 1e-12);
            assert(max(max(abs(E0(:, 2:end) - E(:, 2:end)))) < 1e-12);

            % A table of your own ellipses draws just as well: one centred disc of radius a half.
            mine = [1 0.5 0.5 0 0 0];
            Q = phantom(mine, 64);
            assert(Q(32, 32) == 1);
            assert(Q(1, 1) == 0);

            % A bare size is the default phantom.
            assert(isequal(size(phantom(50)), [50 50]));
            """);
    }

    [Fact]
    public async Task Qtdecomp_SplitsWhereThePictureIsBusyAndTilesExactly()
    {
        await RunAsserting("""
            I = zeros(8, 8);
            I(:, 5:8) = repmat([1 0 1 0; 0 1 0 1], 4, 1);

            S = qtdecomp(I, 0);
            assert(issparse(S));
            assert(isequal(size(S), [8 8]));

            F = full(S);
            assert(F(1, 1) == 4);
            assert(F(5, 1) == 4);
            assert(F(1, 5) == 1);
            assert(F(2, 2) == 0);

            % Every entry is a block corner, and the blocks tile the square exactly.
            assert(sum(sum(F .^ 2)) == 64);
            """);
    }

    [Fact]
    public async Task Qtdecomp_HonoursTheBlockSizeLimits()
    {
        await RunAsserting("""
            I = reshape(1:64, 8, 8);

            % Nothing here is uniform, so only the floor stops the split.
            F = full(qtdecomp(I, 0, 2));
            assert(F(1, 1) == 2);
            assert(F(7, 7) == 2);

            % A ceiling below the picture's side splits the top levels without asking.
            C = full(qtdecomp(zeros(8, 8), 0, [1 4]));
            assert(C(1, 1) == 4);
            assert(C(5, 5) == 4);
            """);
    }

    [Fact]
    public async Task Qtdecomp_TakesYourOwnTestFunction()
    {
        await RunAsserting("""
            I = reshape(1:64, 8, 8) / 64;

            % The test is handed every block of one size at once, as pages of an array, and answers
            % once per page — so its answer has to be as long as size(blocks, 3) says.
            always = qtdecomp(I, @(blocks) true(size(blocks, 3), 1));
            F = full(always);
            assert(F(1, 1) == 1);
            assert(F(8, 8) == 1);
            assert(sum(sum(F .^ 2)) == 64);

            never = qtdecomp(I, @(blocks) false(size(blocks, 3), 1));
            G = full(never);
            assert(G(1, 1) == 8);
            assert(sum(sum(G .^ 2)) == 64);
            """);
    }

    [Fact]
    public async Task Qtdecomp_RefusesASizeItCannotHalveInto()
    {
        string message = await RunExpectingFailure("qtdecomp(zeros(12, 12))");
        Assert.Contains("qtdecomp", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("power of two", message, StringComparison.OrdinalIgnoreCase);

        string rectangular = await RunExpectingFailure("qtdecomp(zeros(8, 4))");
        Assert.Contains("square", rectangular, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QtgetblkAndQtsetblk_ReadAndWriteTheBlocksAQuadtreeFound()
    {
        await RunAsserting("""
            I = reshape(1:16, 4, 4)';
            S = qtdecomp(I, 100, [2 2]);
            [vals, r, c] = qtgetblk(I, S, 2);

            assert(isequal(size(vals), [2 2 4]));
            assert(numel(r) == 4);

            % Column-major order, one-based coordinates: the corners are (1,1) (3,1) (1,3) (3,3).
            assert(isequal(r(:)', [1 3 1 3]));
            assert(isequal(c(:)', [1 1 3 3]));
            assert(isequal(vals(:, :, 1), I(1:2, 1:2)));

            % Writing them back is the inverse of reading them.
            assert(isequal(qtsetblk(I, S, 2, vals), I));

            J = qtsetblk(I, S, 2, zeros(2, 2, 4));
            assert(all(all(J == 0)));
            """);
    }

    [Fact]
    public async Task Normxcorr2_PeaksAtTheTemplatesBottomRightCorner()
    {
        await RunAsserting("""
            A = zeros(20, 24);
            A(6:10, 8:13) = magic(6)(1:5, :) / 36;
            T = A(6:10, 8:13);

            C = normxcorr2(T, A);
            assert(isequal(size(C), [24 29]));

            [best, where] = max(C(:));
            assert(abs(best - 1) < 1e-8);

            [row, colIndex] = ind2sub(size(C), where);

            % Offset one is where only the template's last pixel overlaps the picture's first, so the
            % peak sits at the template's bottom-right corner in the picture.
            assert(row == 10);
            assert(colIndex == 13);

            % Every value is a correlation coefficient, so nothing may exceed one.
            assert(max(max(abs(C))) <= 1 + 1e-12);
            """);
    }

    [Fact]
    public async Task Normxcorr2_RefusesATemplateLargerThanThePicture()
    {
        string message = await RunExpectingFailure("normxcorr2(ones(9, 9), ones(4, 4))");
        Assert.Contains("normxcorr2", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Imregcorr_FindsATranslationAndHandsBackATransform()
    {
        await RunAsserting("""
            fixedImage = phantom(64);
            moving = zeros(64, 64);
            moving(8:64, 1:59) = fixedImage(1:57, 6:64);

            [tform, peak] = imregcorr(moving, fixedImage, 'translation');
            assert(strcmp(class(tform), 'affine2d'));
            assert(peak > 0.2);

            T = tform.T;
            assert(abs(T(1, 1) - 1) < 1e-12);
            assert(abs(T(2, 1)) < 1e-12);

            % The picture was pushed down seven and left five, so putting it back is up seven, right five.
            assert(abs(T(3, 1) - 5) < 1e-9);
            assert(abs(T(3, 2) + 7) < 1e-9);

            % And the transform actually lines the two up again.
            back = imwarp(moving, tform, 'OutputView', imref2d(size(fixedImage)));
            assert(mean(mean(abs(back - fixedImage))) < 0.05);
            """);
    }

    [Fact]
    public async Task Imregcorr_RecoversARotationAndCallsItRigid()
    {
        await RunAsserting("""
            fixedImage = phantom(96);
            moving = imrotate(fixedImage, -20, 'bilinear', 'crop');

            tform = imregcorr(moving, fixedImage, 'rigid');
            assert(strcmp(class(tform), 'rigid2d'));

            % The linear part is a rotation of about twenty degrees, undoing the turn.
            T = tform.T;
            angle = atan2(T(1, 2), T(1, 1)) * 180 / pi;
            assert(abs(abs(angle) - 20) < 3);
            assert(abs(sqrt(T(1, 1) ^ 2 + T(1, 2) ^ 2) - 1) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imregcorr_RefusesATransformTypeItDoesNotHave()
    {
        string message = await RunExpectingFailure(
            "imregcorr(ones(8, 8), ones(8, 8), 'transformType', 'projective')");
        Assert.Contains("imregcorr", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("similarity", message, StringComparison.OrdinalIgnoreCase);
    }
}
