using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The class an array carries decides what its numbers mean. MATLAB has no image type, so
/// <c>imwrite(uint8(A))</c> writes 0-255 while <c>imwrite(A)</c> writes [0, 1] of the same array;
/// until this was read, every plain array was taken for [0, 1] and a <c>uint8</c> picture — the
/// array <c>getframe</c> hands back — saturated to white everywhere but its exact zeros.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabImageClassTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabImageClassTests()
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

    // --- imwrite reads the class the array carries ------------------------------------------------

    [Fact]
    public async Task ImageRoundtripSupportsNumericEqualityAndText()
    {
        await RunAsserting("""
            A = uint8([0 64;128 255]); imwrite(A, 'roundtrip.png'); B = imread('roundtrip.png');
            assert(isequal(A,B) && isequal(B,A) && isequaln(A,B));
            assert(~isequal(B,uint8([0 65;128 255])));
            assert(strcmp(mat2str(B),'[0 64;128 255]'));
            assert(strcmp(mat2str(B,'class'),"uint8([0 64;128 255])"));
            assert(isequal({A},{B}));
            A = uint16([0 1024;32768 65535]); imwrite(A,'sixteen.png'); B = imread('sixteen.png');
            assert(isequal(A,B)); assert(strcmp(mat2str(A),mat2str(B)));
            A = uint8(reshape(1:18,2,3,3)); imwrite(A,'rgb.png'); B = imread('rgb.png');
            assert(isequal(A,B));
            """);
    }

    [Fact]
    public async Task ImagescReversesHighLevelAxesAndRespectsHold()
    {
        await RunAsserting("""
            figure; imagesc([1 2;3 4]); assert(strcmp(get(gca,'YDir'),'reverse'));
            set(gca,'YDir','normal'); hold on; imagesc([4 3;2 1]);
            assert(strcmp(get(gca,'YDir'),'normal')); hold off;
            imagesc('CData',[1 2;3 4]); assert(strcmp(get(gca,'YDir'),'normal'));
            cb=colorbar(gca,'southoutside'); assert(strcmp(get(cb,'Location'),'southoutside'));
            colorbar(gca,'Location','westoutside','Direction','reverse');
            assert(strcmp(get(cb,'Location'),'westoutside'));
            """);
    }

    [Fact]
    public async Task ImwriteOfAUint8Grayscale_WritesTheSamplesAsGiven()
    {
        await RunAsserting("""
            imwrite(uint8([0 64; 128 255]), 'u8.png');
            R = imread('u8.png');
            assert(R(1, 1) == 0 && R(1, 2) == 64, 'the dark half of a uint8 image did not survive');
            assert(R(2, 1) == 128 && R(2, 2) == 255, 'the bright half of a uint8 image did not survive');
            """);
    }

    [Fact]
    public async Task ImwriteOfAUint8ColourArray_KeepsEveryChannel()
    {
        // Height-by-width-by-3 uint8 is exactly the shape getframe hands back, and the shape whose
        // every non-zero sample used to come out 255.
        await RunAsserting("""
            A = zeros(2, 3, 3);
            A(:, :, 1) = [10 20 30; 40 50 60];
            A(:, :, 2) = [70 80 90; 100 110 120];
            A(:, :, 3) = [130 140 150; 160 170 180];
            imwrite(uint8(A), 'rgb8.png');
            R = imread('rgb8.png');
            s = size(R);
            assert(numel(s) == 3 && s(3) == 3, 'the colour planes did not survive the round trip');
            assert(R(1, 1, 1) == 10 && R(1, 1, 2) == 70 && R(1, 1, 3) == 130, 'the first pixel is wrong');
            assert(R(2, 3, 1) == 60 && R(2, 3, 2) == 120 && R(2, 3, 3) == 180, 'the last pixel is wrong');
            """);
    }

    [Fact]
    public async Task ImwriteOfADoubleArray_StillScalesFromZeroToOne()
    {
        // The reading every array used to get, and the one a double array must keep getting.
        await RunAsserting("""
            imwrite([0 0.25; 0.5 1], 'dbl.png');
            R = imread('dbl.png');
            assert(R(1, 1) == 0 && R(2, 2) == 255, 'the ends of the double range moved');
            assert(abs(double(R(1, 2)) - 64) <= 1, 'a quarter should land near 64');
            assert(abs(double(R(2, 1)) - 128) <= 1, 'a half should land near 128');
            """);
    }

    [Fact]
    public async Task ImwriteOfADoubleColourArray_StillScalesFromZeroToOne()
    {
        await RunAsserting("""
            rgb = cat(3, ones(2, 2), zeros(2, 2), 0.5 * ones(2, 2));
            imwrite(rgb, 'dblrgb.png');
            R = imread('dblrgb.png');
            assert(R(1, 1, 1) == 255 && R(1, 1, 2) == 0, 'a double colour array lost its ends');
            assert(abs(double(R(1, 1, 3)) - 128) <= 1, 'a half-strength channel should land near 128');
            """);
    }

    [Fact]
    public async Task ImwriteOfAUint16Array_SpansSixteenBits()
    {
        // PNG preserves the native uint16 values, including samples between eight-bit levels.
        await RunAsserting("""
            imwrite(uint16([0 16384; 32768 65535]), 'u16.png');
            R = imread('u16.png');
            assert(isequal(R,uint16([0 16384;32768 65535])));
            assert(strcmp(class(R),'uint16'));
            """);
    }

    [Fact]
    public async Task ImwriteOfASingleArray_ReadsLikeADouble()
    {
        await RunAsserting("""
            imwrite(single([0 0.25; 0.5 1]), 'sng.png');
            R = imread('sng.png');
            assert(R(1, 1) == 0 && R(2, 2) == 255, 'single should be read as [0, 1] like double');
            assert(abs(double(R(2, 1)) - 128) <= 1, 'a half should land near 128');
            """);
    }

    [Fact]
    public async Task ImwriteOfALogicalMask_WritesBlackAndWhite()
    {
        // MATLAB writes a mask as a one-bit PNG and reads it back as a logical matrix (open item 273):
        // a matrix, so that comparisons, indexing and arithmetic take it as they take any logical.
        await RunAsserting("""
            imwrite([0 0.25; 0.5 1] > 0.3, 'mask.png');
            R = imread('mask.png');
            assert(islogical(R) && isequal(size(R), [2 2]), 'a mask should read back logical');
            assert(~R(1, 1) && ~R(1, 2), 'false should be black');
            assert(R(2, 1) && R(2, 2), 'true should be white');
            assert(isequal(R > 0, [false false; true true]), 'a comparison should take it');
            assert(sum(R(:)) == 2 && isequal(R(1:4), [false true false true]), 'and indexing');
            info = imfinfo('mask.png');
            assert(info.BitDepth == 1, 'the file should be one bit deep');
            """);
    }

    [Fact]
    public async Task ImwriteWithBitDepthOne_WritesAMaskOfTheNonzeroSamples()
    {
        // writepng's BitDepth 1: a sample is on when it is not zero, whatever the class (probe bd1_probe).
        await RunAsserting("""
            imwrite(uint8([0 1 2 3 128 254 255]), 'b1.png', 'BitDepth', 1);
            R = imread('b1.png');
            assert(islogical(R) && isequal(R, logical([0 1 1 1 1 1 1])), 'uint8 nonzero is on');
            imwrite([0 0.1 0.5 1 0.0001], 'b2.png', 'BitDepth', 1);
            assert(isequal(imread('b2.png'), logical([0 1 1 1 1])), 'double nonzero is on');
            imwrite(uint16([0 1 65535]), 'b3.png', 'BitDepth', 1);
            assert(isequal(imread('b3.png'), logical([0 1 1])), 'uint16 nonzero is on');
            """);
    }

    [Fact]
    public async Task ImreadAnswersAPlainArrayThatOperatorsAndAssignmentTake()
    {
        // Open item 292 (ADR 0356): a uint8, uint16 or RGB picture read back is R2025b's plain array,
        // so `G > 0`, saturating arithmetic, end, logical subscripts and element assignment work.
        await RunAsserting("""
            A = magic(6) * 7;
            imwrite(uint8(A), 'g8.png'); G = imread('g8.png');
            assert(isequal(G > 100, A > 100) && islogical(G > 100));
            assert(isa(G + 200, 'uint8') && max(max(G + 200)) == 255 && min(min(G - 250)) == 0);
            assert(isequal(G ./ 3, uint8(A / 3)) && G(end) == 77 && isequal(G(G > 240)', uint8([245 252])));
            G(1, 1) = 300; assert(G(1, 1) == 255 && isa(G, 'uint8'));
            assert(isequal(sum(imread('g8.png')), sum(A)) && mean(imread('g8.png'), 'all') == 129.5);
            imwrite(uint16(A) * 250, 'g16.png'); H = imread('g16.png');
            assert(isa(H, 'uint16') && isequal(H(1, :) + 60000, uint16([65535 61750 65535 65535 65535 65535])));
            imwrite(uint8(cat(3, A, 255 - A, A)), 'rgb.png'); C = imread('rgb.png');
            assert(isequal(size(C > 128), [6 6 3]) && isequal(sum(C, 3), A + 255));
            imwrite(uint8(A), 'alpha.png', 'Alpha', A / 252); [~, ~, al] = imread('alpha.png');
            assert(isa(al, 'uint8') && isequal(al > 128, A / 252 * 255 > 128));
            [~, map] = imread('g8.png'); assert(isequal(size(map), [0 0]));
            assert(isequal(size(im2mat(imread('g8.png'))), [6 6]));
            """);
    }

    [Fact]
    public async Task ImwriteBitDepthsTwoFourEightAndSixteenStoreR2025bsLevels()
    {
        // Open item 294 (ADR 0356): an integer class keeps its top bits, a logical its own 0 or 1, a
        // double rounds half away; imfinfo reads the depth from the file's header.
        await RunAsserting("""
            imwrite(uint8([0 63 64 127 128 191 192 255]), 'b2.png', 'BitDepth', 2);
            assert(isequal(imread('b2.png'), uint8([0 0 85 85 170 170 255 255])));
            assert(getfield(imfinfo('b2.png'), 'BitDepth') == 2);
            imwrite([0 0.16 0.17 0.5 0.84 1], 'b2d.png', 'BitDepth', 2);
            assert(isequal(imread('b2d.png'), uint8([0 0 85 170 255 255])));
            imwrite(uint8([8 9 25 26 127 128 246 247]), 'b4.png', 'BitDepth', 4);
            assert(isequal(imread('b4.png'), uint8([0 0 17 17 119 136 255 255])));
            assert(getfield(imfinfo('b4.png'), 'BitDepth') == 4);
            imwrite(logical([0 1 1 0]), 'l8.png', 'BitDepth', 8);
            assert(isequal(imread('l8.png'), uint8([0 1 1 0])));
            imwrite(logical([0 1 1 0]), 'l16.png', 'BitDepth', 16);
            assert(isequal(imread('l16.png'), uint16([0 1 1 0])), 'a logical is its own 0 or 1 at depth 16 too');
            imwrite(uint16([0 128 129 257 32768 65535]), 'u8.png', 'BitDepth', 8);
            assert(isequal(imread('u8.png'), uint8([0 0 0 1 128 255])));
            imwrite(uint8([0 1 128 255]), 'u16.png', 'BitDepth', 16);
            assert(isequal(imread('u16.png'), uint16([0 257 32896 65535])));
            imwrite([0.5 1.5 2.5 3.5 126.5 127.5] / 255, 'half.png');
            assert(isequal(imread('half.png'), uint8([1 2 3 4 127 128])));
            failed = '';
            try, imwrite(uint8(magic(3)), 'b3.png', 'BitDepth', 3); catch err, failed = err.identifier; end
            assert(strcmp(failed, 'MATLAB:imagesci:writepng:invalidGrayscaleBitDepth'));
            try, imwrite(uint8(ones(2, 2, 3)), 'b3.png', 'BitDepth', 4); catch err, failed = err.identifier; end
            assert(strcmp(failed, 'MATLAB:imagesci:writepng:invalidRgbBitDepth'));
            """);
    }

    [Fact]
    public async Task ImwriteRefusesAClassNoImageFormatStores()
    {
        // Silence would mean writing whatever a [0, 1] reading of 0-255 produces, which is white.
        await RunAsserting("""
            failed = false;
            try
                imwrite(int32([0 128; 200 255]), 'i32.png');
            catch err
                failed = ~isempty(strfind(err.message, 'int32'));
            end
            assert(failed, 'imwrite should refuse int32 by name');
            """);
    }

    // --- and so does every other builtin that takes a picture or plain numbers --------------------

    [Fact]
    public async Task AUint8MatrixThroughAnImagingBuiltin_KeepsItsClassAndItsUnits()
    {
        // imcomplement is the clearest case: in uint8 units the complement of 10 is 245, and under
        // the old [0, 1] reading it was 1 - 10.
        await RunAsserting("""
            U = uint8([10 20; 200 250]);
            C = imcomplement(U);
            assert(isa(C, 'uint8'), 'a uint8 argument should come back uint8');
            assert(C(1, 1) == 245 && C(2, 2) == 5, 'imcomplement worked in the wrong units');
            F = imfilter(U, [0 0 0; 0 1 0; 0 0 0]);
            assert(isa(F, 'uint8') && F(2, 2) == 250, 'an identity filter changed a uint8 matrix');
            """);
    }

    [Fact]
    public async Task ADoubleMatrixThroughAnImagingBuiltin_IsUntouchedAndUntagged()
    {
        await RunAsserting("""
            A = [0 0.25; 0.5 1];
            C = imcomplement(A);
            assert(~isa(C, 'uint8'), 'a double argument should not come back with an integer class');
            assert(abs(C(1, 1) - 1) < 1e-12 && abs(C(2, 2)) < 1e-12, 'imcomplement did not invert');
            """);
    }

    [Fact]
    public async Task AMaskOverAUint8Matrix_ComesBackAsZerosAndOnes()
    {
        // A mask is 0 or 1 whatever produced it: it must not be handed back in the picture's units.
        await RunAsserting("""
            U = uint8([10 20; 200 250]);
            M = imbinarize(U, 0.5);
            assert(M(1, 1) == 0 && M(1, 2) == 0, 'the dark samples should be below a half-scale level');
            assert(M(2, 1) == 1 && M(2, 2) == 1, 'a mask over uint8 should be ones, not 255s');
            """);
    }

    [Fact]
    public async Task AMaskOverAPlainMatrix_IsLogical_AndNotJustZerosAndOnes()
    {
        // The values were right long before the class was: a mask over a plain array was built out
        // of numbers, so it answered 'double' where MATLAB answers 'logical'. A logical array here
        // is one whose elements are booleans, which is what islogical and class both read.
        await RunAsserting("""
            U = uint8([10 20; 200 250]);
            M = imbinarize(U, 0.5);
            assert(isa(M, 'logical'), 'a mask over a plain matrix should be logical');
            assert(strcmp(class(M), 'logical'), ['class said ' class(M)]);
            assert(islogical(M), 'islogical should agree with class');
            assert(M(1, 1) == 0 && M(1, 2) == 0 && M(2, 1) == 1 && M(2, 2) == 1, 'the mask has the wrong values');
            assert(sum(M(:)) == 2, 'arithmetic over a logical mask should count its true elements');
            A = [1 2; 3 4];
            picked = A(M);
            assert(numel(picked) == 2 && picked(1) == 3 && picked(2) == 4, 'indexing with a mask should select, not index by position');
            product = M .* A;
            assert(product(1, 1) == 0 && product(2, 2) == 4, 'a logical mask should multiply as zeros and ones');
            N = ~M;
            assert(islogical(N) && N(1, 1) == 1 && N(2, 2) == 0, 'negating a mask should stay logical');
            assert(any(M(:)) && ~all(M(:)), 'any and all should read the mask');
            E = edge(double([0 0 1 1; 0 0 1 1; 0 0 1 1; 0 0 1 1]), 'sobel');
            assert(islogical(E), 'edge over a plain matrix should be logical too');
            """);
    }

    [Fact]
    public async Task IntlutTakesAUint8Array_NowThatOneCanSayWhatItsNumbersMean()
    {
        // intlut used to demand a picture value, because its table is indexed by the sample's own
        // integer value and a plain array was thought to carry no class saying what those are.
        await RunAsserting("""
            T = uint8(255 - (0:255));
            V = intlut(uint8([0 64; 128 255]), T);
            assert(isa(V, 'uint8'), 'intlut should answer in the class it was handed');
            assert(V(1, 1) == 255 && V(2, 2) == 0, 'the table was not applied to the ends');
            assert(V(1, 2) == 191 && V(2, 1) == 127, 'the table was indexed by the wrong values');
            refused = false;
            try
                intlut([0 0.5; 1 0.25], T);
            catch
                refused = true;
            end
            assert(refused, 'a double array has no integer samples to index a table with');
            """);
    }

    [Fact]
    public async Task GraythreshOfAUint8Matrix_StillAnswersBetweenZeroAndOne()
    {
        await RunAsserting("""
            U = uint8([10 20; 200 250]);
            level = graythresh(U);
            assert(level > 0 && level < 1, 'graythresh should answer a normalized level');
            lims = stretchlim(U);
            assert(numel(lims) == 2 && lims(1) >= 0 && lims(2) <= 1, 'stretchlim should answer in [0, 1]');
            """);
    }
}
