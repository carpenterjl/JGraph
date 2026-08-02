using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave L: the places where a picture and a plain array of numbers had drifted apart. A picture
/// can be sliced like a matrix, a matrix can be measured like a picture, and <c>cat</c> stacks planes
/// so the documented way of building a colour picture or a volume out of parts actually works.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabPictureAndMatrixTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabPictureAndMatrixTests()
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

    [Fact]
    public async Task AMaskFromAnImagingBuiltin_CanBeSlicedLikeAMatrix()
    {
        await RunAsserting("""
            I = zeros(20, 20);
            I(:, 11:end) = 1;
            BW = edge(I, 'canny', [0.1 0.3]);
            strip = BW(:, 9:12);
            s = size(strip);
            assert(s(1) == 20 && s(2) == 4, 'the slice came back the wrong size');
            assert(any(strip(:)), 'the step edge is not inside the slice');
            assert(~any(any(BW(:, 1:5))), 'a flat region reported an edge');
            """);
    }

    [Fact]
    public async Task SlicingAPicture_KeepsItsNativeScale_AndItsColumnOrder()
    {
        await RunAsserting("""
            imwrite([0 0.25; 0.5 1], 'scale.png');
            I = imread('scale.png');
            top = I(1, :);
            assert(numel(top) == 2, 'a row slice should have two samples');
            assert(top(1) == 0 && abs(double(top(2)) - 64) <= 1, 'the row read the wrong samples');
            corner = I(end, end);
            assert(corner == 255, 'end in a picture subscript missed the last sample');
            """);
    }

    [Fact]
    public async Task SlicingAColourPicture_AcrossChannels_ComesBackThreeDimensional()
    {
        await RunAsserting("""
            rgb = cat(3, ones(4, 4), zeros(4, 4), 0.5 * ones(4, 4));
            imwrite(rgb, 'colour.png');
            I = imread('colour.png');
            block = I(1:2, 1:2, :);
            s = size(block);
            assert(numel(s) == 3 && s(3) == 3, 'a channel slice lost its third dimension');
            assert(block(1, 1, 1) == 255 && block(1, 1, 2) == 0, 'the channels came back in the wrong order');
            """);
    }

    [Fact]
    public async Task CatAlongTheThirdDimension_StacksPlanes()
    {
        await RunAsserting("""
            A = cat(3, ones(2, 3), 2 * ones(2, 3), 3 * ones(2, 3));
            s = size(A);
            assert(numel(s) == 3, 'cat(3, ...) produced a two-dimensional result');
            assert(s(1) == 2 && s(2) == 3 && s(3) == 3, 'cat(3, ...) got the size wrong');
            assert(A(1, 1, 1) == 1 && A(2, 3, 2) == 2 && A(1, 2, 3) == 3, 'the planes are out of order');
            """);
    }

    [Fact]
    public async Task CatAlongTheThirdDimension_JoinsVolumesNotJustPlanes()
    {
        await RunAsserting("""
            A = cat(3, ones(2, 2), ones(2, 2));
            B = 5 * ones(2, 2, 3);
            J = cat(3, A, B);
            s = size(J);
            assert(s(3) == 5, 'joining a 2-deep and a 3-deep volume should give 5 planes');
            assert(J(1, 1, 2) == 1 && J(1, 1, 3) == 5, 'the join put the planes in the wrong order');
            """);
    }

    [Fact]
    public async Task CatAlongTheThirdDimension_RefusesMismatchedPlanes()
    {
        await RunAsserting("""
            failed = false;
            try
                cat(3, ones(2, 2), ones(3, 2));
            catch
                failed = true;
            end
            assert(failed, 'planes of different sizes should not join');
            """);
    }

    [Fact]
    public async Task CatBuildsAColourPicture_ThatTheImagingBuiltinsRead()
    {
        await RunAsserting("""
            rgb = cat(3, ones(4, 4), zeros(4, 4), zeros(4, 4));
            g = im2gray(rgb);
            s = size(g);
            assert(numel(s) == 2, 'im2gray should collapse the colour planes');
            assert(g(1, 1) > 0.2 && g(1, 1) < 0.4, 'pure red should weigh about 0.3 in grey');
            """);
    }

    [Fact]
    public async Task ThePointAndThresholdBuiltins_TakeAPlainMatrix()
    {
        await RunAsserting("""
            A = [0 0.25; 0.5 1];
            counts = imhist(A);
            assert(sum(counts) == 4, 'imhist did not count every sample of the matrix');
            level = graythresh(A);
            assert(level > 0 && level < 1, 'graythresh gave a level outside [0, 1]');
            lims = stretchlim(A);
            assert(numel(lims) == 2, 'stretchlim should give a low and a high');
            C = imcomplement(A);
            assert(abs(C(1, 1) - 1) < 1e-12 && abs(C(2, 2)) < 1e-12, 'imcomplement did not invert');
            D = imabsdiff(A, C);
            assert(abs(D(1, 1) - 1) < 1e-12, 'imabsdiff did not subtract');
            """);
    }

    [Fact]
    public async Task AMatrixIn_MeansAMatrixOut()
    {
        await RunAsserting("""
            A = [0 0.25; 0.5 1];
            B = imadjust(A, [0 1], [0 1]);
            s = size(B);
            assert(numel(s) == 2 && s(1) == 2, 'imadjust changed the shape of a matrix');
            assert(~isa(B, 'uint8'), 'a matrix argument should not come back with a class tag');
            M = imbinarize(A, 0.4);
            assert(M(2, 2) == 1 && M(1, 1) == 0, 'imbinarize thresholded the wrong way round');
            """);
    }

    [Fact]
    public async Task AdaptthreshSurface_FeedsImbinarize_AsPlainNumbers()
    {
        await RunAsserting("""
            A = 0.2 * ones(16, 16);
            A(5:12, 5:12) = 0.8;
            T = adaptthresh(A, 0.5, 'NeighborhoodSize', 5);
            s = size(T);
            assert(s(1) == 16 && s(2) == 16, 'the threshold surface should match the picture');
            M = imbinarize(A, T);
            assert(any(M(:)), 'the adaptive threshold selected nothing at all');
            """);
    }

    [Fact]
    public async Task AssigningIntoASubmatrix_WritesEveryPickedElement()
    {
        // The write path used to assume one column-major run of storage. It still must agree with the
        // read path element for element, whichever way the matrix was built.
        await RunAsserting("""
            A = zeros(4, 4);
            A(2:3, 2:3) = 5;
            assert(A(2, 2) == 5 && A(3, 3) == 5, 'the selection was not written');
            assert(A(1, 1) == 0 && A(4, 4) == 0, 'the write spilled outside the selection');
            A(1, :) = 7;
            assert(A(1, 4) == 7 && A(2, 4) == 0, 'a whole-row write went wrong');
            B = [1 2; 3 4];
            B(1:2, 1:2) = B(1:2, 1:2) + 10;
            assert(B(1, 1) == 11 && B(2, 2) == 14, 'a compound submatrix write went wrong');
            """);
    }
}
