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
        // The samples are read as 0-65535, which is what this is about. The file is still eight bits
        // deep - Skia will not encode a 16-bit PNG, a divergence matlab-ipt-coverage.md already
        // records - so the values below are that range correctly scaled down, where R2024a reads the
        // same four samples back at their full width.
        await RunAsserting("""
            imwrite(uint16([0 16384; 32768 65535]), 'u16.png');
            R = imread('u16.png');
            assert(R(1, 1) == 0 && R(2, 2) == 255, 'the ends of the uint16 range moved');
            assert(abs(double(R(1, 2)) - 64) <= 1, 'a quarter of 65535 should land near 64 in eight bits');
            assert(abs(double(R(2, 1)) - 128) <= 1, 'a half of 65535 should land near 128 in eight bits');
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
        // MATLAB writes a mask as a one-bit PNG and reads it back logical; the same encoder floor
        // that costs the 16-bit path makes this eight bits, so false is 0 and true is 255.
        await RunAsserting("""
            imwrite([0 0.25; 0.5 1] > 0.3, 'mask.png');
            R = imread('mask.png');
            assert(R(1, 1) == 0 && R(1, 2) == 0, 'false should be black');
            assert(R(2, 1) == 255 && R(2, 2) == 255, 'true should be white');
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
