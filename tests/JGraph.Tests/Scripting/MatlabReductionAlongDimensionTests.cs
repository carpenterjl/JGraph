using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The reductions other than max/min along any dimension of any shape (M49). The two-dimensional
/// forms worked before; an N-D array was folded into rows first, so a reduction past the second
/// dimension went along the fold — <c>sum(A, 3)</c> summed the pages laid side by side rather than
/// through them. Expected values are MATLAB's own.
/// </summary>
[Collection("JG facade")]
public class MatlabReductionAlongDimensionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabReductionAlongDimensionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Sum_ReducesAlongEachOfAVolumesThreeDimensions() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        x = sum(b, 1);
        assert(isequal(size(x), [1 2 3]));
        assert(isequal(x, cat(3, [3 7], [11 15], [19 23])));
        y = sum(b, 2);
        assert(isequal(size(y), [2 1 3]));
        assert(isequal(y, cat(3, [4; 6], [12; 14], [20; 22])));
        z = sum(b, 3);
        assert(isequal(size(z), [2 2]));
        assert(isequal(z, [15 21; 18 24]));
        """);

    [Fact]
    public Task TheOtherScalarPerSliceReductions_FollowTheSameDimension() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(prod(b, 3), [45 231; 120 384]));
        assert(isequal(mean(b, 3), [5 7; 6 8]));
        assert(isequal(median(b, 3), [5 7; 6 8]));
        assert(isequal(mode(b, 3), [1 3; 2 4]));
        assert(isequal(std(b, 0, 3), [4 4; 4 4]));
        assert(isequal(variance(b, 0, 3), [16 16; 16 16]));
        assert(isequal(var(b, 0, 3), [16 16; 16 16]));
        """);

    /// <summary>
    /// M52: the dimension moves along one for the spread reductions, because MATLAB puts the weight in
    /// the slot every other reduction keeps it in. Before this, std(x, 1) asked for the population
    /// standard deviation and silently got a reduction along dimension 1 instead.
    /// </summary>
    [Fact]
    public Task TheSpreadReductions_ReadTheirSecondArgumentAsAWeight() => RunAsserting("""
        x = [2 4 4 4 5 5 7 9];
        assert(abs(var(x, 0) - 32 / 7) < 1e-12);
        assert(abs(var(x, 1) - 4) < 1e-12);
        assert(abs(std(x, 1) - 2) < 1e-12);
        assert(abs(std(x, []) - std(x)) < 1e-12);
        w = [1 1 1 1 1 1 1 1];
        assert(abs(var(x, w) - var(x, 1)) < 1e-12);
        m = [1 2; 3 4];
        assert(isequal(var(m, 0, 2), [0.5; 0.5]));
        assert(isequal(var(m, 1, 2), [0.25; 0.25]));
        assert(isequal(var(m, 0, 'all'), var(m(:), 0)));
        """);

    [Fact]
    public Task AnyAndAll_ReduceAlongADimensionToo() => RunAsserting("""
        m = cat(3, [1 0; 0 0], [0 0; 1 0]);
        assert(isequal(any(m, 3), [1 0; 1 0]));
        assert(isequal(all(m, 3), [0 0; 0 0]));
        assert(isequal(size(any(m, 1)), [1 2 2]));
        assert(isequal(any(m, 1), cat(3, [1 0], [1 0])));
        """);

    [Fact]
    public Task TheShapeKeepingReductions_WriteAWholeVectorBackAlongTheDimension() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        c = cumsum(b, 3);
        assert(isequal(size(c), [2 2 3]));
        assert(isequal(c, cat(3, [1 3; 2 4], [6 10; 8 12], [15 21; 18 24])));
        p = cumprod(b, 3);
        assert(isequal(size(p), [2 2 3]));
        assert(isequal(p, cat(3, [1 3; 2 4], [5 21; 12 32], [45 231; 120 384])));
        s = sort(b, 3);
        assert(isequal(s, b));
        """);

    [Fact]
    public Task Diff_ShortensOnlyTheDimensionItWalks() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        d = diff(b, 1, 3);
        assert(isequal(size(d), [2 2 2]));
        assert(isequal(d, cat(3, [4 4; 4 4], [4 4; 4 4])));
        assert(isequal(size(diff(b, 1, 1)), [1 2 3]));
        assert(isequal(size(diff(b, 1, 2)), [2 1 3]));
        assert(isequal(size(diff(b, [], 3)), [2 2 2]));
        """);

    [Fact]
    public Task DiffReadsItsSecondArgumentAsHowManyTimesToDifference() => RunAsserting("""
        A = [1 4 9 16];
        assert(isequal(diff(A), [3 5 7]));
        assert(isequal(diff(A, 2), [2 2]));
        assert(isequal(diff(A, 3), 0));
        assert(isequal(diff(A, 0), A));
        B = [1 2; 4 8; 9 27];
        assert(isequal(diff(B, 2), [2 13]));
        assert(isequal(diff(B, 2, 1), [2 13]));
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(size(diff(b, 2, 3)), [2 2]));
        assert(isequal(diff(b, 2, 3), zeros(2, 2)));
        """);

    [Fact]
    public Task DiffSaysWhatItWantedWhenTheOrderOrTheArgumentCountIsWrong() => RunAsserting("""
        ok = 0;
        try
            diff([1 2 3], 1.5);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'whole number'));
        end
        try
            diff([1 2 3], -1);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'zero or more'));
        end
        try
            diff([1 2 3], 1, 2, 3);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'at most three arguments'));
        end
        assert(ok == 3);
        """);

    [Fact]
    public Task WithNoDimension_ItReducesAlongTheFirstNonSingletonOne() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(sum(b), sum(b, 1)));
        assert(isequal(size(sum(b)), [1 2 3]));
        r = reshape(1:6, 1, 2, 3);
        assert(isequal(sum(r), sum(r, 2)));
        assert(isequal(size(sum(r)), [1 1 3]));
        assert(isequal(size(cumsum(r)), [1 2 3]));
        """);

    [Fact]
    public Task ADimensionPastTheLast_ChangesNothing() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(sum(b, 5), b));
        assert(isequal(cumsum(b, 4), b));
        A = [1 2; 3 4];
        assert(isequal(prod(A, 3), A));
        """);

    [Fact]
    public Task TheAllForm_StillReducesEverythingToOneValue() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(sum(b, 'all') == 78);
        assert(prod([1 2; 3 4], 'all') == 24);
        assert(mean(b, 'all') == 6.5);
        assert(any([0 0; 0 0], 'all') == 0);
        assert(all([1 1; 1 1], 'all') == 1);
        """);

    [Fact]
    public Task SortStillReportsWhereEachValueCameFrom() => RunAsserting("""
        A = [3 1; 2 4];
        [s, i] = sort(A);
        assert(isequal(s, [2 1; 3 4]));
        assert(isequal(i, [2 1; 1 2]));
        [s2, i2] = sort(A, 2);
        assert(isequal(s2, [1 3; 2 4]));
        assert(isequal(i2, [2 1; 1 2]));
        b = cat(3, [5 1; 7 3], [2 8; 4 6]);
        [s3, i3] = sort(b, 3);
        assert(isequal(size(s3), [2 2 2]));
        assert(isequal(s3, cat(3, [2 1; 4 3], [5 8; 7 6])));
        assert(isequal(i3, cat(3, [2 1; 2 1], [1 2; 1 2])));
        """);

    [Fact]
    public Task SortTakesMatlabsOwnOrderWords() => RunAsserting("""
        assert(isequal(sort([3 1 2], 'descend'), [3 2 1]));
        assert(isequal(sort([3 1 2], 'ascend'), [1 2 3]));
        A = [1 5 3; 8 2 9];
        [s, i] = sort(A, 2, 'descend');
        assert(isequal(s, [5 3 1; 9 8 2]));
        assert(isequal(i, [2 3 1; 3 1 2]));
        """);

    [Fact]
    public Task VectorsAndMatricesKeepEveryAnswerTheyAlreadyGave() => RunAsserting("""
        v = [1 2 3];
        assert(sum(v) == 6);
        assert(isequal(sum(v, 1), v));
        assert(sum(v, 2) == 6);
        assert(isequal(cumsum(v), [1 3 6]));
        assert(isequal(diff([1 4 9]), [3 5]));
        c = [1; 2; 3];
        assert(sum(c) == 6);
        assert(isequal(sum(c, 2), c));
        assert(isequal(cumsum(c), [1; 3; 6]));
        A = [1 2; 3 4];
        assert(isequal(sum(A), [4 6]));
        assert(isequal(sum(A, 2), [3; 7]));
        assert(isequal(cumsum(A, 2), [1 3; 3 7]));
        assert(isequal(diff(A), [2 2]));
        assert(sum([]) == 0);
        """);

    [Fact]
    public Task NonNumericArraysStayWithTheBuiltinThatKnowsThem() => RunAsserting("""
        % sort of a string array has no dimension to walk, so it keeps its own answer.
        assert(isequal(sort(["b" "a"]), ["a" "b"]));
        assert(isequal(sum([true false; true true]), [2 1]));
        """);

    [Fact]
    public Task ADimensionOfZeroOrLess_SaysSoRatherThanGuessing() => RunAsserting("""
        ok = false;
        try
            sum([1 2; 3 4], 0);
        catch err
            ok = ~isempty(strfind(err.message, 'positive whole number'));
        end
        assert(ok);
        """);
}
