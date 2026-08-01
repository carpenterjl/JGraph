using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M46 wave B as a <c>.m</c> script sees it: the filtering and block-processing builtins, the
/// name-value options they gained, and the identities between them that a MATLAB user relies on —
/// <c>imfilter('conv')</c> agreeing with <c>conv2</c>, <c>col2im</c> undoing <c>im2col</c>,
/// <c>integralBoxFilter</c> agreeing with <c>imboxfilt</c>.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabFilterBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabFilterBuiltinTests()
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
    public async Task Padarray_PadsWithAValue_AWord_AndInOneDirection()
    {
        await RunAsserting("""
            A = [1 2; 3 4];

            B = padarray(A, [1 1]);
            assert(isequal(size(B), [4 4]));
            assert(B(1, 1) == 0);
            assert(B(2, 2) == 1);

            C = padarray(A, [1 1], 'replicate');
            assert(C(1, 1) == 1);
            assert(C(4, 4) == 4);

            D = padarray(A, [1 0], 7, 'post');
            assert(isequal(size(D), [3 2]));
            assert(D(1, 1) == 1);
            assert(D(3, 2) == 7);

            E = padarray(A, [1 1], 'circular');
            assert(E(1, 1) == 4);
            """);
    }

    [Fact]
    public async Task Imfilter_ConvSameAgreesWithConv2()
    {
        await RunAsserting("""
            A = magic(6);
            h = fspecial('gaussian', 5, 1.2);
            assert(max(max(abs(imfilter(A, h, 'conv', 'same') - conv2(A, h, 'same')))) < 1e-12);

            % 'full' is bigger by the kernel extent, and correlating with a symmetric kernel is the
            % same as convolving with it.
            assert(isequal(size(imfilter(A, h, 'full')), [10 10]));
            assert(max(max(abs(imfilter(A, h) - imfilter(A, h, 'conv')))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imfilter_BoundaryWordsAndPadValueChangeTheEdgeOnly()
    {
        await RunAsserting("""
            A = ones(4, 4);
            h = ones(3, 3) / 9;

            zeroPadded = imfilter(A, h);
            assert(abs(zeroPadded(1, 1) - 4/9) < 1e-12);       % five of nine taps fall outside
            assert(abs(zeroPadded(2, 2) - 1) < 1e-12);

            replicated = imfilter(A, h, 'replicate');
            assert(abs(replicated(1, 1) - 1) < 1e-12);

            filled = imfilter(A, h, 10);
            assert(abs(filled(1, 1) - (4 + 50)/9) < 1e-12);
            """);
    }

    [Fact]
    public async Task Conv2_SeparableFormMatchesTheOuterProduct()
    {
        await RunAsserting("""
            A = magic(5);
            u = [1 2 1];
            v = [1 0 -1];
            assert(max(max(abs(conv2(u, v, A, 'same') - conv2(A, u' * v, 'same')))) < 1e-12);
            assert(isequal(size(conv2(u, v, A)), [7 7]));
            """);
    }

    [Fact]
    public async Task Fspecial_MotionAndUnsharpSumCorrectly()
    {
        await RunAsserting("""
            m = fspecial('motion', 7, 0);
            assert(abs(sum(m(:)) - 1) < 1e-10);
            assert(isequal(size(m), [1 7]));

            u = fspecial('unsharp', 0.2);
            assert(abs(sum(u(:)) - 1) < 1e-12);
            assert(isequal(size(u), [3 3]));

            % A non-square average is a documented shape, and it still sums to one.
            a = fspecial('average', [2 5]);
            assert(isequal(size(a), [2 5]));
            assert(abs(sum(a(:)) - 1) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imgaussfilt_PreservesAConstantAndAcceptsItsOptions()
    {
        await RunAsserting("""
            A = 0.5 * ones(9, 9);
            B = imgaussfilt(A, 2);
            assert(max(max(abs(B - 0.5))) < 1e-12);

            C = imgaussfilt(A, [1 3], 'FilterSize', [5 7], 'Padding', 'symmetric');
            assert(max(max(abs(C - 0.5))) < 1e-12);

            D = imgaussfilt(A, 1, 'FilterDomain', 'spatial');
            assert(max(max(abs(D - 0.5))) < 1e-12);
            """);
    }

    [Fact]
    public async Task Imboxfilt_IsTheLocalMean_AndItsNormalizationFactorScalesIt()
    {
        await RunAsserting("""
            A = ones(7, 7);
            assert(max(max(abs(imboxfilt(A, 3) - 1))) < 1e-12);

            sums = imboxfilt(A, 3, 'NormalizationFactor', 1);
            assert(abs(sums(4, 4) - 9) < 1e-12);
            """);
    }

    [Fact]
    public async Task IntegralBoxFilter_AgreesWithImboxfiltOverTheValidRegion()
    {
        await RunAsserting("""
            A = magic(8);
            direct = imboxfilt(A, 3);
            fromTable = integralBoxFilter(integralImage(A), 3);

            assert(isequal(size(fromTable), [6 6]));
            for r = 1:6
                for c = 1:6
                    assert(abs(direct(r + 1, c + 1) - fromTable(r, c)) < 1e-10);
                end
            end
            """);
    }

    [Fact]
    public async Task IntegralImage_IsOneLargerInEachDirection_AndRotatedIsWiderStill()
    {
        await RunAsserting("""
            A = ones(4, 5);
            J = integralImage(A);
            assert(isequal(size(J), [5 6]));
            assert(J(5, 6) == 20);

            R = integralImage(A, 'rotated');
            assert(isequal(size(R), [5 7]));
            """);
    }

    [Fact]
    public async Task Ordfilt2_SpansMinimumMedianAndMaximum()
    {
        await RunAsserting("""
            A = [1 2 3; 4 5 6; 7 8 9];
            domain = ones(3, 3);
            lo = ordfilt2(A, 1, domain, 'symmetric');
            mid = ordfilt2(A, 5, domain, 'symmetric');
            hi = ordfilt2(A, 9, domain, 'symmetric');

            assert(lo(2, 2) == 1);
            assert(mid(2, 2) == 5);
            assert(hi(2, 2) == 9);
            """);
    }

    [Fact]
    public async Task NeighbourhoodStatistics_MeasureWhatTheyClaimTo()
    {
        await RunAsserting("""
            A = [1 2 3; 4 5 6; 7 8 9];

            r = rangefilt(A);
            assert(r(2, 2) == 8);           % 9 - 1 over the full neighbourhood

            s = stdfilt(A);
            assert(abs(s(2, 2) - sqrt(60/8)) < 1e-10);

            flat = 0.5 * ones(11, 11);
            e = entropyfilt(flat);
            assert(abs(e(6, 6)) < 1e-12);   % one value, no information

            m = modefilt([1 1 2; 1 2 2; 2 2 2]);
            assert(m(2, 2) == 2);
            """);
    }

    [Fact]
    public async Task Wiener2_ReportsTheNoiseItEstimated()
    {
        await RunAsserting("""
            A = 0.25 * ones(9, 9);

            % Stating the noise power means it comes straight back, and a region that is genuinely
            % flat is left where it was.
            [J, noise] = wiener2(A, [3 3], 0.01);
            assert(abs(noise - 0.01) < 1e-12);
            assert(max(max(abs(J(3:7, 3:7) - 0.25))) < 1e-12);

            % Estimating it instead gives a positive power, since the zero-padded border reads as a step.
            [~, estimated] = wiener2(A, [3 3]);
            assert(estimated > 0);

            % A single output is the filtered array alone, not the pair.
            K = wiener2(A);
            assert(isequal(size(K), [9 9]));
            """);
    }

    [Fact]
    public async Task Im2colAndCol2im_RoundTripDistinctBlocks()
    {
        await RunAsserting("""
            A = reshape(1:16, 4, 4);
            B = im2col(A, [2 2], 'distinct');
            assert(isequal(size(B), [4 4]));
            assert(isequal(col2im(B, [2 2], [4 4], 'distinct'), A));

            S = im2col(A, [2 2], 'sliding');
            assert(isequal(size(S), [4 9]));
            """);
    }

    [Fact]
    public async Task Bestblk_SplitsIntoTwoOutputs()
    {
        await RunAsserting("""
            s = bestblk([500 200], 100);
            assert(isequal(s, [100 100]));      % 100 divides both exactly

            [mb, nb] = bestblk([500 300], 100);
            assert(mb == 100);
            assert(nb == 100);

            % A prime dimension has no divisor near the limit, so the fallback keeps the last block big.
            prime = bestblk([101 101], 100);
            assert(prime(1) == 51);
            """);
    }

    [Fact]
    public async Task Nlfilter_AppliesTheFunctionToEveryNeighbourhood()
    {
        await RunAsserting("""
            A = ones(5, 5);
            B = nlfilter(A, [3 3], @(x) sum(x(:)));
            assert(B(3, 3) == 9);       % the interior sees a full window
            assert(B(1, 1) == 4);       % the corner is zero-padded
            """);
    }

    [Fact]
    public async Task Colfilt_SlidingAndDistinctBothCoverTheWholeArray()
    {
        await RunAsserting("""
            A = ones(4, 4);

            sliding = colfilt(A, [3 3], 'sliding', @(x) sum(x, 1));
            assert(isequal(size(sliding), [4 4]));
            assert(sliding(2, 2) == 9);

            distinct = colfilt(A, [2 2], 'distinct', @(x) 2 * x);
            assert(isequal(size(distinct), [4 4]));
            assert(distinct(1, 1) == 2);
            """);
    }

    [Fact]
    public async Task Blockproc_HandsTheFunctionABlockStruct()
    {
        await RunAsserting("""
            A = reshape(1:16, 4, 4);
            B = blockproc(A, [2 2], @(b) b.data * 2);
            assert(isequal(B, A * 2));

            % The struct carries the block's place in the image, 1-based under this dialect.
            corners = blockproc(A, [2 2], @(b) b.location(1) * ones(2, 2));
            assert(corners(1, 1) == 1);
            assert(corners(3, 1) == 3);
            """);
    }

    [Fact]
    public async Task Blockproc_TrimsTheBorderItAdded()
    {
        await RunAsserting("""
            A = ones(4, 4);
            B = blockproc(A, [2 2], @(b) b.data, 'BorderSize', [1 1]);
            assert(isequal(size(B), [4 4]));
            """);
    }

    [Fact]
    public async Task Edge_ReportsItsThresholdAndAcceptsAPair()
    {
        await RunAsserting("""
            I = zeros(16, 16);
            I(:, 9:16) = 1;

            [BW, level] = edge(I, 'sobel');
            assert(level > 0);
            assert(sum(BW(:)) > 0);

            [~, pair] = edge(I, 'canny', [0.1 0.3]);
            assert(numel(pair) == 2);
            assert(abs(pair(1) - 0.1) < 1e-12);
            assert(abs(pair(2) - 0.3) < 1e-12);

            % A single output is the edge map alone.
            only = edge(I, 'canny');
            assert(islogical(only));
            """);
    }

    [Fact]
    public async Task Edge_DirectionKeepsOneOrientation()
    {
        await RunAsserting("""
            I = zeros(16, 16);
            I(:, 9:16) = 1;                      % one vertical step

            vertical = edge(I, 'sobel', 0.5, 'vertical');
            horizontal = edge(I, 'sobel', 0.5, 'horizontal');
            assert(sum(vertical(:)) > 0);
            assert(sum(horizontal(:)) == 0);
            """);
    }

    [Fact]
    public async Task Imgradient_AcceptsComponentsAsWellAsAMethod()
    {
        await RunAsserting("""
            I = reshape(1:9, 3, 3);
            [Gx, Gy] = imgradientxy(I, 'central');
            [mag, dir] = imgradient(Gx, Gy);

            assert(abs(mag(2, 2) - sqrt(Gx(2, 2)^2 + Gy(2, 2)^2)) < 1e-12);
            assert(numel(dir) == 9);
            """);
    }

    [Fact]
    public async Task UnknownFilterOption_NamesTheOnesThatWork()
    {
        string message = await RunExpectingFailure("imboxfilt(ones(5), 3, 'Paddin', 'replicate');");
        Assert.Contains("Paddin", message);
        Assert.Contains("NormalizationFactor", message);
    }

    [Fact]
    public async Task Medfilt2_SymmetricPaddingKeepsTheCornersFromDarkening()
    {
        await RunAsserting("""
            A = ones(5, 5);
            zeroPadded = medfilt2(A, [3 3]);
            symmetric = medfilt2(A, [3 3], 'symmetric');

            assert(zeroPadded(1, 1) == 0);      % five of nine taps are zero, so the median is zero
            assert(symmetric(1, 1) == 1);
            """);
    }
}
