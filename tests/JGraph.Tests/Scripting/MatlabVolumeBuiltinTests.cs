using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave K as a <c>.m</c> script sees it: a volume is a plain three-dimensional array, the sizes
/// come back with three numbers, coordinates are one-based, and an image is refused rather than read
/// as a stack of planes.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabVolumeBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabVolumeBuiltinTests()
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
    public async Task Medfilt3_KeepsTheVolumeShape_AndRemovesASpike()
    {
        await RunAsserting("""
            V = 0.4 * ones(5, 5, 5);
            V(3, 3, 3) = 1;
            F = medfilt3(V);
            s = size(F);
            assert(numel(s) == 3 && s(1) == 5 && s(3) == 5, 'medfilt3 changed the shape');
            assert(abs(F(3, 3, 3) - 0.4) < 1e-12, 'medfilt3 kept the spike');
            """);
    }

    [Fact]
    public async Task Imgaussfilt3_SpreadsWeightThroughThePlanes()
    {
        await RunAsserting("""
            V = zeros(5, 5, 5);
            V(3, 3, 3) = 1;
            B = imgaussfilt3(V, 1);
            assert(B(3, 3, 2) > 0, 'the blur did not reach the neighbouring plane');
            assert(abs(B(3, 3, 2) - B(3, 3, 4)) < 1e-12, 'the blur is lopsided through the stack');
            """);
    }

    [Fact]
    public async Task Imboxfilt3_OnAConstantVolume_IsThatConstant()
    {
        await RunAsserting("""
            V = 0.25 * ones(6, 6, 6);
            B = imboxfilt3(V, 3);
            assert(abs(B(4, 4, 4) - 0.25) < 1e-12, 'the box mean moved a constant');
            """);
    }

    [Fact]
    public async Task IntegralVolume_AndItsBoxFilter_AgreeWithTheDirectMean()
    {
        await RunAsserting("""
            V = reshape(1:(4*4*4), [4 4 4]);
            J = integralImage3(V);
            s = size(J);
            assert(s(1) == 5 && s(2) == 5 && s(3) == 5, 'the integral volume is the wrong size');
            B = integralBoxFilter3(J, 3);
            D = imboxfilt3(V, 3);
            assert(abs(B(1, 1, 1) - D(2, 2, 2)) < 1e-9, 'the two box means disagree');
            """);
    }

    [Fact]
    public async Task Fspecial3_BuildsKernelsOfTheDocumentedShapes()
    {
        await RunAsserting("""
            h = fspecial3('average', 3);
            assert(abs(sum(h(:)) - 1) < 1e-12, 'the average kernel is not normalized');
            g = fspecial3('gaussian', 5, 1);
            assert(abs(sum(g(:)) - 1) < 1e-12, 'the gaussian kernel is not normalized');
            l = fspecial3('laplacian');
            assert(abs(sum(l(:))) < 1e-12, 'the laplacian does not sum to zero');
            e = fspecial3('ellipsoid', [3 3 1]);
            se = size(e);
            assert(se(3) == 3, 'the ellipsoid ignored its third semi-axis');
            """);
    }

    [Fact]
    public async Task Fspecial3_NamesTheAlternatives_WhenTheTypeIsMisspelled()
    {
        string message = await RunExpectingFailure("h = fspecial3('gausian', 5);");

        Assert.Contains("'gaussian'", message);
    }

    [Fact]
    public async Task Imadjustn_StretchesOntoTheOutputWindow()
    {
        await RunAsserting("""
            V = zeros(2, 2, 2);
            V(:) = [0 0.25 0.5 1 0 0.25 0.5 1];
            A = imadjustn(V, [0.25 0.5], [0 1]);
            assert(abs(A(1, 1, 1)) < 1e-12, 'the low end did not clip');
            assert(abs(A(2, 2, 1) - 1) < 1e-12, 'the high end did not clip');
            """);
    }

    [Fact]
    public async Task Imhistmatchn_MovesAVolumeTowardsTheReference()
    {
        await RunAsserting("""
            V = 0.2 * ones(4, 4, 4);
            R = 0.8 * ones(4, 4, 4);
            M = imhistmatchn(V, R);
            assert(abs(M(2, 2, 2) - 0.8) < 0.05, 'the histogram did not move');
            """);
    }

    [Fact]
    public async Task Edge3_FindsTheFaceOfABoxAndNotItsInterior()
    {
        await RunAsserting("""
            V = zeros(12, 12, 12);
            V(4:9, 4:9, 4:9) = 1;
            BW = edge3(V, 'Sobel', 0.4);
            assert(BW(7, 7, 7) == 0, 'the interior was called an edge');
            assert(BW(4, 7, 7) == 1, 'the face was not found');
            """);
    }

    [Fact]
    public async Task Imgradientxyz_GivesThreeComponents_AndImgradient3TheAngles()
    {
        await RunAsserting("""
            V = zeros(4, 4, 4);
            for p = 1:4
                V(:, :, p) = p;
            end
            [Gx, Gy, Gz] = imgradientxyz(V);
            assert(abs(Gx(2, 2, 2)) < 1e-12, 'the x gradient should be flat');
            assert(Gz(2, 2, 2) > 0, 'the z gradient should climb');
            [Gmag, Gaz, Gel] = imgradient3(V);
            assert(Gmag(2, 2, 2) > 0, 'the magnitude should be positive');
            assert(abs(Gel(2, 2, 2) - 90) < 1e-9, 'the elevation should be straight up');
            """);
    }

    [Fact]
    public async Task Imresize3_TakesAScaleOrASize()
    {
        await RunAsserting("""
            V = rand(8, 8, 8);
            A = imresize3(V, 0.5);
            sa = size(A);
            assert(sa(1) == 4 && sa(2) == 4 && sa(3) == 4, 'the scale factor was not applied');
            B = imresize3(V, [4 6 8]);
            sb = size(B);
            assert(sb(1) == 4 && sb(2) == 6 && sb(3) == 8, 'the explicit size was not applied');
            """);
    }

    [Fact]
    public async Task Imrotate3_CropsOrGrows_AsAsked()
    {
        await RunAsserting("""
            V = ones(10, 10, 4);
            C = imrotate3(V, 45, [0 0 1], 'crop');
            sc = size(C);
            assert(sc(1) == 10 && sc(2) == 10, 'crop changed the size');
            L = imrotate3(V, 45, [0 0 1]);
            sl = size(L);
            assert(sl(1) > 10 && sl(2) > 10, 'loose did not grow the output');
            """);
    }

    [Fact]
    public async Task Imcrop3_TakesItsCuboidInOneBasedCoordinates()
    {
        await RunAsserting("""
            V = reshape(1:(6*6*6), [6 6 6]);
            C = imcrop3(V, [2 2 2 1 1 1]);
            s = size(C);
            assert(s(1) == 2 && s(2) == 2 && s(3) == 2, 'the cuboid extents are wrong');
            assert(C(1, 1, 1) == V(2, 2, 2), 'the cuboid origin is off by one');
            """);
    }

    [Fact]
    public async Task ObliqueSlice_ThroughAnAxisAlignedPlane_ReadsThatPlane()
    {
        await RunAsserting("""
            V = reshape(1:(6*6*6), [6 6 6]);
            [B, x, y, z] = obliqueslice(V, [3 3 4], [0 0 1]);
            assert(abs(z(1, 1) - 4) < 1e-9, 'the slice came from the wrong plane');
            assert(abs(B(3, 3) - V(3, 3, 4)) < 1e-6, 'the slice read the wrong samples');
            assert(size(x, 1) == size(B, 1), 'the coordinates do not match the slice');
            """);
    }

    [Fact]
    public async Task Bwlabeln_CountsCornerTouchingCubesByConnectivity()
    {
        await RunAsserting("""
            V = zeros(4, 4, 4);
            V(2, 2, 2) = 1;
            V(3, 3, 3) = 1;
            [~, six] = bwlabeln(V, 6);
            [L, twentysix] = bwlabeln(V, 26);
            assert(six == 2, 'six-connectivity joined a corner touch');
            assert(twentysix == 1, 'twenty-six-connectivity split a corner touch');
            assert(L(2, 2, 2) == L(3, 3, 3), 'the two voxels got different labels');
            """);
    }

    [Fact]
    public async Task Bwlabeln_SingleOutput_IsTheLabelVolumeNotThePair()
    {
        await RunAsserting("""
            V = zeros(4, 4, 4);
            V(2, 2, 2) = 1;
            L = bwlabeln(V);
            s = size(L);
            assert(numel(s) == 3, 'the single output was not the label volume');
            """);
    }

    [Fact]
    public async Task Bwmorph3_RemoveLeavesTheSurfaceOfASolidBox()
    {
        await RunAsserting("""
            V = zeros(9, 9, 9);
            V(3:7, 3:7, 3:7) = 1;
            S = bwmorph3(V, 'remove');
            assert(S(5, 5, 5) == 0, 'the interior survived');
            assert(S(3, 5, 5) == 1, 'the surface was removed');
            """);
    }

    [Fact]
    public async Task Bwselect3_KeepsOnlyTheRegionASeedLandsIn()
    {
        await RunAsserting("""
            V = zeros(8, 8, 8);
            V(2, 2, 2) = 1;
            V(7, 7, 7) = 1;
            S = bwselect3(V, 7, 7, 7);
            assert(S(2, 2, 2) == 0, 'an unseeded region survived');
            assert(S(7, 7, 7) == 1, 'the seeded region was dropped');
            """);
    }

    [Fact]
    public async Task Regionprops3_MeasuresACubeInOneBasedCoordinates()
    {
        await RunAsserting("""
            V = zeros(10, 10, 10);
            V(3:6, 3:6, 3:6) = 1;
            t = regionprops3(V, 'Volume', 'Centroid');
            assert(t.Volume(1) == 64, 'the voxel count is wrong');
            assert(abs(t.CentroidX(1) - 4.5) < 1e-9, 'the centroid is not one-based');
            """);
    }

    [Fact]
    public async Task Regionprops3_NamesTheAlternatives_ForAnUnknownProperty()
    {
        string message = await RunExpectingFailure("""
            V = zeros(4, 4, 4);
            V(2, 2, 2) = 1;
            t = regionprops3(V, 'Perimeter');
            """);

        Assert.Contains("SurfaceArea", message);
    }

    [Fact]
    public async Task Imsegkmeans3_SeparatesTwoLevels()
    {
        await RunAsserting("""
            V = zeros(4, 4, 4);
            V(:, :, 1:2) = 0.1;
            V(:, :, 3:4) = 0.9;
            [L, C] = imsegkmeans3(V, 2);
            assert(numel(C) == 2, 'the wrong number of centres came back');
            assert(L(1, 1, 1) ~= L(1, 1, 4), 'the two levels landed in one cluster');
            """);
    }

    [Fact]
    public async Task Superpixels3_NumbersItsSupervoxelsFromOne()
    {
        await RunAsserting("""
            V = rand(8, 8, 8);
            [L, n] = superpixels3(V, 8);
            assert(n >= 1, 'no supervoxels came back');
            assert(min(L(:)) == 1, 'the labels do not start at one');
            assert(max(L(:)) == n, 'the label count and the labels disagree');
            """);
    }

    [Fact]
    public async Task Multissim3_OfAVolumeAgainstItself_IsOne()
    {
        await RunAsserting("""
            V = rand(16, 16, 16);
            s = multissim3(V, V, 'NumScales', 3);
            assert(abs(s - 1) < 1e-9, 'a volume is not identical to itself');
            """);
    }

    [Fact]
    public async Task Padarray_WithAThreeElementSize_PadsAllThreeDimensions()
    {
        await RunAsserting("""
            V = ones(3, 3, 3);
            P = padarray(V, [1 1 1]);
            s = size(P);
            assert(s(1) == 5 && s(2) == 5 && s(3) == 5, 'the third dimension was not padded');
            assert(P(1, 1, 1) == 0, 'the pad is not zero');
            """);
    }

    [Fact]
    public async Task Bwareaopen_OnAVolume_RemovesTheSmallRegion()
    {
        await RunAsserting("""
            V = zeros(10, 10, 10);
            V(2, 2, 2) = 1;
            V(6:8, 6:8, 6:8) = 1;
            O = bwareaopen(V, 10);
            assert(O(2, 2, 2) == 0, 'the small region survived');
            assert(O(7, 7, 7) == 1, 'the large region was removed');
            """);
    }

    [Fact]
    public async Task Bwconncomp_OnAVolume_ReportsThreeSizesAndThreeDimensionalIndices()
    {
        await RunAsserting("""
            V = zeros(4, 4, 4);
            V(2, 2, 2) = 1;
            V(4, 4, 4) = 1;
            cc = bwconncomp(V);
            assert(cc.NumObjects == 2, 'the object count is wrong');
            assert(numel(cc.ImageSize) == 3, 'ImageSize is not three-dimensional');
            idx = cc.PixelIdxList{2};
            assert(V(idx(1)) == 1, 'the linear index does not point at the object');
            """);
    }

    [Fact]
    public async Task AVolumeFunction_RefusesAnImage_RatherThanReadingItsChannels()
    {
        string message = await RunExpectingFailure("""
            I = mat2im(rand(8, 8));
            F = medfilt3(I);
            """);

        Assert.Contains("colour, not depth", message);
    }

    [Fact]
    public async Task AVolumeFunction_AcceptsAPlainMatrix_AsOnePlane()
    {
        await RunAsserting("""
            A = 0.4 * ones(5, 5);
            A(3, 3) = 1;
            F = medfilt3(A);
            assert(abs(F(3, 3) - 0.4) < 1e-12, 'the single-plane form did not filter');
            """);
    }
}
