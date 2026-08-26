using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M94 seen from a MATLAB script: the dimension reductions take the packed kernels, and nothing may
/// notice. Every test here runs its script twice — packing forced on, then forced off — and the
/// printed output must be byte-identical, at seventeen significant digits so a single flipped bit
/// shows. The scripts sweep the surface the fast path claims: every wrapped name, both layouts and
/// N-D, the option words, the extra slots, both outputs of the extremes — and the calls the fast
/// path must refuse identically, weights and odd arguments included.
/// </summary>
[Collection("JG facade")]
public class MatlabPackedReduceM94Tests : IDisposable
{
    public MatlabPackedReduceM94Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static void AssertParity(string code, bool expectSuccess = true)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.Equal(boxedOk, packedOk);
        if (expectSuccess)
        {
            Assert.True(boxedOk, boxedMessage);
        }
        else
        {
            Assert.False(boxedOk);
            Assert.Equal(boxedMessage, packedMessage); // the refusal must read the same too
        }

        Assert.Equal(boxedOut, packedOut);
    }

    /// <summary>Prints a value bit-faithfully: seventeen digits, the shape, whether it is logical,
    /// and the sign of zero — everything a representation change could smudge.</summary>
    private const string Show = """
        function show(v)
            fprintf('%dx%d L%d|', size(v, 1), size(v, 2), islogical(v));
            fprintf('%.17g ', v);
            fprintf('|');
            fprintf('%.17g ', 1 ./ v);
            fprintf('\n');
        end
        """ + "\n";

    [Fact]
    public void EveryScalarReductionAgreesOverEveryDimensionAndNanWord()
    {
        AssertParity(Show + """
            A = reshape([1.5 -2 3.25 NaN 5 -0.125 7.5 NaN 9 10.5 -11 12], 3, 4);
            names = {'sum', 'prod', 'mean', 'rms'};
            for k = 1:numel(names)
                f = str2func(names{k});
                show(f(A)); show(f(A, 1)); show(f(A, 2)); show(f(A, 3)); show(f(A, 5));
            end
            spreads = {'std', 'var', 'variance'};
            for k = 1:numel(spreads)
                f = str2func(spreads{k});
                % their first slot is the weight, so the dimension sits one along
                show(f(A)); show(f(A, 0, 1)); show(f(A, 0, 2)); show(f(A, 1, 3)); show(f(A, [], 5));
            end
            show(sum(A, 'omitnan')); show(sum(A, 2, 'omitnan')); show(sum(A, 'includenan'));
            show(mean(A, 2, 'omitnan')); show(rms(A, 'omitnan'));
            show(prod(A, 2, 'omitnan'));
            show(sum(A, 'all')); show(prod(A, 'all')); show(sum(A, 'all', 'omitnan'));
            show(sum(A, [1 2])); show(prod(A, [2 1]));
            """);
    }

    [Fact]
    public void AWhollyNanSliceAnswersEachNamesOwnIdentity()
    {
        AssertParity(Show + """
            B = [NaN 2; NaN 4];
            show(sum(B, 2, 'omitnan')); show(prod(B, 2, 'omitnan'));
            show(mean(B, 2, 'omitnan')); show(rms(B, 2, 'omitnan'));
            show(std(B, 0, 2, 'omitnan')); show(var(B, 1, 2, 'omitnan'));
            show(sum(NaN, 'omitnan')); show(mean([NaN NaN], 'omitnan'));
            """);
    }

    [Fact]
    public void TheWeightSlotOfStdAndVarStaysInEverySpelling()
    {
        AssertParity(Show + """
            A = reshape(1:12, 3, 4) + 0.5;
            show(std(A, 0)); show(std(A, 1)); show(std(A, [], 2));
            show(var(A, 0, 2)); show(var(A, 1, 1)); show(variance(A, 1));
            show(std(A, [1 2 4], 1));
            """);
    }

    [Fact]
    public void VecnormAgreesForEveryPIncludingTheInfiniteOne()
    {
        AssertParity(Show + """
            A = [3 -4 0.5; -1.25 12 2];
            show(vecnorm(A)); show(vecnorm(A, 1)); show(vecnorm(A, 2, 2));
            show(vecnorm(A, Inf)); show(vecnorm(A, Inf, 2)); show(vecnorm(A, 3.5, 1));
            v = [3 4]; show(vecnorm(v)); show(vecnorm(v', 1));
            """);
    }

    [Fact]
    public void TheTruthReductionsStayLogicalInShapeAndKind()
    {
        AssertParity(Show + """
            A = [0 1 NaN; 0 0 2];
            show(any(A)); show(any(A, 2)); show(all(A)); show(all(A, 2));
            show(any(A, 'all')); show(all(A, 'all')); show(any(A, [1 2]));
            M = A > 0;
            show(sum(M)); show(sum(M, 2)); show(any(M, 2)); show(all(M, 1));
            """);
    }

    [Fact]
    public void TheRunningReductionsAgreeInBothDirectionsAndBothNanReadings()
    {
        AssertParity(Show + """
            A = [1 NaN 3; -0 5 NaN];
            names = {'cumsum', 'cumprod', 'cummax', 'cummin'};
            for k = 1:numel(names)
                f = str2func(names{k});
                show(f(A)); show(f(A, 2)); show(f(A, 3));
                show(f(A, 'reverse')); show(f(A, 2, 'reverse'));
                show(f(A, 'omitnan')); show(f(A, 2, 'includenan'));
                show(f(A, 1, 'reverse', 'omitnan'));
            end
            c = [2 NaN -1]; show(cummax(c)); show(cummin(c, 'includenan'));
            show(cumsum(-0)); show(cummax([-0 -1]));
            """);
    }

    [Fact]
    public void DiffAgreesForEveryOrderIncludingTheOnesThatEmptyIt()
    {
        AssertParity(Show + """
            x = [1 4 9 16 25];
            show(diff(x)); show(diff(x, 2)); show(diff(x, 4)); show(diff(x, 0));
            show(diff(x, [], 2)); show(diff(x'));
            A = reshape([1 3 6 10 2 5 9 14], 4, 2);
            show(diff(A)); show(diff(A, 1, 2)); show(diff(A, 2, 1)); show(diff(A, 3, 1));
            d5 = diff(x, 5); fprintf('empty %d %dx%d\n', isempty(d5), size(d5, 1), size(d5, 2));
            d9 = diff(x, 9); fprintf('empty %d\n', isempty(d9));
            """);
    }

    [Fact]
    public void TheExtremesAgreeInValueAndPositionOverEveryForm()
    {
        AssertParity(Show + """
            A = [3 NaN 1; NaN 5 1; 7 2 NaN];
            show(max(A)); show(min(A)); show(max(A, [], 2)); show(min(A, [], 2));
            [m, i] = max(A); show(m); show(i);
            [m, i] = min(A, [], 2); show(m); show(i);
            [m, i] = max(A, [], 2, 'linear'); show(m); show(i);
            show(max(A, [], 'all')); show(min(A, [], 'all'));
            [m, i] = max(A, [], 'all'); show(m); show(i);
            show(max(A, [], [1 2]));
            show(max(A, [], 1, 'includenan')); show(min(A, [], 2, 'includenan'));
            T = [5 2; 5 5]; [m, i] = max(T); show(m); show(i); % ties go first
            N = [NaN NaN; NaN NaN]; [m, i] = min(N); show(m); show(i);
            F = [NaN 3 7]; show(max(F, [], 2, 'includenan')); % NaN opening the slice is spared
            """);
    }

    [Fact]
    public void NdArraysReduceAlongTheirHigherDimensions()
    {
        // The N-D value is shaped by reshape alone: the boxed lane's elementwise operators drop a
        // third dimension (a pre-existing boxed limitation, not this milestone's), so adding 0.25
        // AFTER reshaping would make the two lanes disagree about the input, not the reduction.
        AssertParity(Show + """
            C = reshape((1:24) + 0.25, 2, 3, 4);
            s3 = sum(C, 3); show(s3);
            m2 = mean(C, 2); fprintf('%.17g ', m2); fprintf('\n');
            [m, i] = max(C, [], 3); show(m); show(i);
            [m, i] = min(C, [], 2, 'linear'); show(m); show(i);
            cs = cumsum(C, 3); fprintf('%.17g ', cs(:, :, 4)); fprintf('\n');
            show(sum(C, [2 3]));
            M = reshape((1:24) > 23, 2, 3, 4); % the mask reshaped, for the same boxed reason as C
            show(any(M, [1 3])); show(all(M, [1 2]));
            """);
    }

    [Fact]
    public void ColumnsAndRowsKeepTheirOrientationThroughEveryFamily()
    {
        AssertParity(Show + """
            c = (1:5)' + 0.5;
            show(sum(c)); show(sum(c, 1)); show(sum(c, 2));
            show(cumsum(c)); show(cummax(c)); show(diff(c));
            [m, i] = max(c); show(m); show(i);
            r = 1:5; show(mean(r)); show(cumprod(r)); show(std(r));
            show(sum(5)); show(cumsum(5)); show(max(5));
            """);
    }

    [Fact]
    public void TheCallsTheFastPathRefusesFailIdentically()
    {
        // A weight the kernels do not take, and a stray argument the inner builtin refuses: both
        // must fall through and complain in exactly the boxed words.
        AssertParity(Show + """
            A = reshape(1:6, 2, 3);
            show(std(A, 2));
            """, expectSuccess: false);
        AssertParity("""
            A = reshape(1:6, 2, 3);
            b = sum(A, 1, 'native');
            """, expectSuccess: false);
    }

    [Fact]
    public void MediansAndModesStillTravelTheBoxedRoadWithTheSameAnswers()
    {
        AssertParity(Show + """
            A = [3 1 4; 1 5 9; 2 6 5];
            show(median(A)); show(median(A, 2)); show(mode(A));
            [s, i] = sort([3 1 2], 'descend'); show(s); show(i);
            """);
    }

    [Fact]
    public void AThreadedReductionAnswersTheHandComputedSum()
    {
        // Over the reduction threshold, so the kernels certainly split; the expected values are
        // closed forms that are exact in doubles, so this is an equality, not a tolerance.
        int columns = 70;
        int rows = (ParallelKernels.ReductionThreshold / columns) + 11;
        AssertParity($$"""
            r = {{rows}}; c = {{columns}};
            A = reshape(1:(r * c), r, c);
            s = sum(A, 1);
            assert(isequal(size(s), [1 c]));
            k = 17; base = (k - 1) * r;
            assert(s(k) == r * base + r * (r + 1) / 2);
            [m, i] = max(A, [], 2);
            assert(all(i == c));
            assert(m(3) == (c - 1) * r + 3);
            t = cumsum(ones(r, 1));
            assert(t(end) == r);
            fprintf('ok %.17g %.17g\n', sum(s), m(end));
            """);
    }
}
