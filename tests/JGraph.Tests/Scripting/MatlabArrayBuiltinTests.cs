using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The array statistics, function application, and moving windows (M38). The moving statistics are
/// pinned at the ends as well as the middle, since a shrinking window is where implementations
/// usually differ from MATLAB.
/// </summary>
[Collection("JG facade")]
public class MatlabArrayBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabArrayBuiltinTests() => JG.Reset();

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
    public Task Arrayfun_AppliesAFunctionToEveryElement() => RunAsserting("""
        assert(isequal(arrayfun(@(x) x^2, [1 2 3]), [1 4 9]));
        assert(isequal(arrayfun(@(a, b) a + b, [1 2], [10 20]), [11 22]));

        c = arrayfun(@(x) [x x], [1 2], 'UniformOutput', false);
        assert(iscell(c));
        assert(isequal(c{2}, [2 2]));
        """);

    [Fact]
    public Task Bsxfun_ExpandsAScalarAcrossTheOtherArray() => RunAsserting("""
        assert(isequal(bsxfun(@plus, [1 2 3], 10), [11 12 13]));
        assert(isequal(bsxfun(@times, [1 2 3], [2 2 2]), [2 4 6]));
        """);

    [Fact]
    public Task StructHelpers_MoveBetweenStructsAndCells() => RunAsserting("""
        s.a = 1;
        s.b = 2;
        assert(isequal(structfun(@(x) x * 10, s), [10 20]));

        c = struct2cell(s);
        assert(iscell(c));
        assert(c{1} == 1);

        back = cell2struct({7, 8}, {'x', 'y'});
        assert(back.x == 7);
        assert(back.y == 8);
        """);

    [Fact]
    public Task Accumarray_SumsIntoTheBinsItsSubscriptsName() => RunAsserting("""
        assert(isequal(accumarray([1 2 1 3], [10 20 30 40]), [40 20 40]));
        assert(isequal(accumarray([1 1 2], 1), [2 1]));
        assert(isequal(accumarray([1 2 1], [1 2 3], 4), [4 2 0 0]));
        assert(isequal(accumarray([1 1 2], [1 5 9], 2, @max), [5 9]));
        """);

    [Fact]
    public Task RunningExtremes_CarryTheBestSoFar() => RunAsserting("""
        assert(isequal(cummax([1 3 2 5 4]), [1 3 3 5 5]));
        assert(isequal(cummin([5 3 4 1 2]), [5 3 3 1 1]));
        assert(isequal(maxk([3 1 4 1 5], 2), [5 4]));
        assert(isequal(mink([3 1 4 1 5], 2), [1 1]));
        """);

    [Fact]
    public Task Histc_CountsIntoHalfOpenBins() => RunAsserting("""
        % Bins are [0,2), [2,4), and then exactly 4 — the last edge counts only its own value.
        assert(isequal(histc([0 1 2 3 4], [0 2 4]), [2 2 1]));
        assert(isequal(histc([5], [0 2 4]), [0 0 0]));
        """);

    [Fact]
    public Task ToleranceSets_TreatNearbyValuesAsOne() => RunAsserting("""
        assert(numel(uniquetol([1 1.0000001 2])) == 2);
        assert(numel(uniquetol([1 1.1 2])) == 3);
        assert(isequal(ismembertol([1 5], [1.0000001 2]), [true false]));
        assert(issortedrows([1 2; 1 3; 2 0]));
        assert(~issortedrows([2 0; 1 3]));
        """);

    [Fact]
    public Task RandomDraws_StayInsideTheRangeTheyWereGiven() => RunAsserting("""
        for k = 1:20
            v = randi(6);
            assert(v >= 1 && v <= 6);
            assert(v == floor(v));
        end

        r = randi([10 12], 1, 50);
        assert(numel(r) == 50);
        assert(min(r) >= 10 && max(r) <= 12);

        % A permutation contains 1..n exactly once, which sorting proves.
        p = randperm(6);
        assert(isequal(sort(p), 1:6));
        assert(numel(randperm(10, 3)) == 3);
        """);

    [Fact]
    public Task CircshiftAndRot90_MoveDataWithoutLosingAny() => RunAsserting("""
        assert(isequal(circshift([1 2 3 4], 1), [4 1 2 3]));
        assert(isequal(circshift([1 2 3 4], -1), [2 3 4 1]));
        assert(isequal(circshift([1 2 3 4], 4), [1 2 3 4]));

        A = [1 2; 3 4];
        assert(isequal(rot90(A), [2 4; 1 3]));
        assert(isequal(rot90(A, 4), A));
        assert(isequal(rot90(A, 2), [4 3; 2 1]));
        """);

    [Fact]
    public Task MovingStatistics_ShrinkTheirWindowAtTheEnds() => RunAsserting("""
        % A width-3 window over [1 2 3 4 5]: the ends see two values, the middle three.
        assert(isequal(movsum([1 2 3 4 5], 3), [3 6 9 12 9]));
        assert(isequal(movmean([1 2 3 4 5], 3), [1.5 2 3 4 4.5]));
        assert(isequal(movmax([1 5 2 4 3], 3), [5 5 5 4 4]));
        assert(isequal(movmin([1 5 2 4 3], 3), [1 1 2 2 3]));
        assert(isequal(movmedian([1 5 2 4 3], 3), [3 2 4 3 3.5]));
        % An even window covers the current element and the one before it, never the one after.
        assert(isequal(movprod([1 2 3 4], 2), [1 2 6 12]));
        assert(isequal(movsum([1 2 3 4], 2), [1 3 5 7]));

        % A window of 1 leaves the data alone, and its spread is zero.
        assert(isequal(movmean([4 7 1], 1), [4 7 1]));
        assert(isequal(movstd([4 7 1], 1), [0 0 0]));
        assert(abs(movvar([1 2 3], 3)(2) - 1) < 1e-14);
        assert(isequal(movmad([1 3], 2), [0 1]));
        """);
}
