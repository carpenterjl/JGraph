using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the option words the reductions answer for themselves — <c>'omitnan'</c>, <c>'reverse'</c>,
/// the output-class words, and max/min's <c>'linear'</c> — plus the NaN default those words are
/// measured against. Expected values are MATLAB's own.
/// </summary>
/// <remarks>
/// The NaN default moved here, deliberately. Math.Max and Math.Min propagate NaN, so a single missing
/// reading used to make the maximum of a whole column NaN; MATLAB's default is 'omitnan', because a
/// NaN is a reading that is absent rather than one that beats everything else. Scripts that fed NaN
/// data to max or min get different answers now, and better ones.
/// </remarks>
[Collection("JG facade")]
public class MatlabReductionOptionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabReductionOptionTests() => JG.Reset();

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
    public Task OmitNanDropsTheMissingReadingsFromEveryReduction() => RunAsserting("""
        x = [1 NaN 3 NaN 5];
        assert(isnan(sum(x)));
        assert(sum(x, 'omitnan') == 9);
        assert(mean(x, 'omitnan') == 3);
        assert(median(x, 'omitnan') == 3);
        assert(prod(x, 'omitnan') == 15);
        assert(abs(std(x, 0, 'omitnan') - 2) < 1e-12);
        assert(abs(var(x, 0, 'omitnan') - 4) < 1e-12);
        """);

    /// <summary>
    /// A mean omitting NaN divides by what is left, not by the original length — which is the whole
    /// reason omitting has to happen before the builtin sees the slice rather than after.
    /// </summary>
    [Fact]
    public Task OmittingShrinksTheDenominatorItDividesBy() => RunAsserting("""
        assert(mean([2 NaN 4], 'omitnan') == 3);
        assert(mean([2 0 4]) == 2);
        """);

    [Fact]
    public Task OmittingEverythingLeavesTheReductionOfNothing() => RunAsserting("""
        assert(sum([NaN NaN], 'omitnan') == 0);
        assert(prod([NaN NaN], 'omitnan') == 1);
        assert(isnan(mean([NaN NaN], 'omitnan')));
        assert(isnan(median([NaN NaN], 'omitnan')));
        """);

    [Fact]
    public Task IncludeNanAsksForThePropagatingAnswerBack() => RunAsserting("""
        assert(isnan(sum([1 NaN 3], 'includenan')));
        assert(isnan(mean([1 NaN 3], 'includenan')));
        """);

    [Fact]
    public Task OmitNanWorksAlongANamedDimension() => RunAsserting("""
        m = [1 NaN; 3 4];
        assert(isequal(sum(m, 1, 'omitnan'), [4 4]));
        assert(isequal(sum(m, 2, 'omitnan'), [1; 7]));
        assert(sum(m, 'all') ~= sum(m, 'all'));
        assert(sum(m, 'all', 'omitnan') == 8);
        """);

    /// <summary>
    /// A cumulative reduction cannot drop values — its answer is the length of its input — so it puts
    /// the identity in the NaN's place instead.
    /// </summary>
    [Fact]
    public Task TheCumulativeReductionsTreatNanAsTheirIdentity() => RunAsserting("""
        assert(isequal(cumsum([1 NaN 3], 'omitnan'), [1 1 4]));
        assert(isequal(cumprod([2 NaN 3], 'omitnan'), [2 2 6]));
        """);

    [Fact]
    public Task ReverseAccumulatesFromTheFarEnd() => RunAsserting("""
        assert(isequal(cumsum([1 2 3], 'reverse'), [6 5 3]));
        assert(isequal(cumprod([1 2 3], 'reverse'), [6 6 3]));
        assert(isequal(cumsum([1 2; 3 4], 1, 'reverse'), [4 6; 3 4]));
        """);

    [Fact]
    public Task TheOutputClassWordsAreTakenOrRefusedByName() => RunAsserting("""
        assert(sum([1 2 3], 'double') == 6);
        assert(sum([1 2 3], 'default') == 6);
        assert(mean([1 2 3], 'double') == 2);
        ok = 0;
        try
            sum([1 2 3], 'native');
        catch err
            ok = ~isempty(strfind(err.message, 'double'));
        end
        assert(ok == 1);
        """);

    /// <summary>
    /// An option word a name does not claim still rides along to the builtin underneath, which is what
    /// keeps <c>sort(A, 'descend')</c> working after the wrapper learned to take words at all.
    /// </summary>
    [Fact]
    public Task AWordTheReductionDoesNotClaimStillReachesTheBuiltin() => RunAsserting("""
        assert(isequal(sort([3 1 2], 'descend'), [3 2 1]));
        assert(isequal(sort([3 1; 2 4], 1, 'descend'), [3 4; 2 1]));
        """);

    [Fact]
    public Task MaxAndMinSkipMissingReadingsByDefault() => RunAsserting("""
        assert(max([1 NaN 3]) == 3);
        assert(min([1 NaN 3]) == 1);
        assert(max([NaN 2 NaN]) == 2);
        assert(isequal(max([1 NaN; NaN 4]), [1 4]));
        assert(isequal(max([1 2], [NaN 5]), [1 5]));
        assert(isequal(min([1 2], [NaN 5]), [1 2]));
        """);

    [Fact]
    public Task IncludeNanBringsThePropagationBackToMaxAndMin() => RunAsserting("""
        assert(isnan(max([1 NaN 3], [], 'includenan')));
        assert(isnan(min([1 NaN 3], [], 'includenan')));
        """);

    /// <summary>
    /// An all-NaN run has no winner, so the answer is NaN at the first position rather than an error.
    /// </summary>
    [Fact]
    public Task EverythingMissingAnswersMissing() => RunAsserting("""
        [m, i] = max([NaN NaN]);
        assert(isnan(m));
        assert(i == 1);
        """);

    /// <summary>
    /// 'linear' turns the second output from a position inside its slice into an index into the whole
    /// array, which is exactly the number that reads the extreme back out again.
    /// </summary>
    [Fact]
    public Task LinearIndexesIntoTheWholeArrayRatherThanIntoTheSlice() => RunAsserting("""
        a = [1 9 3; 7 2 8];
        [m, i] = max(a, [], 1);
        assert(isequal(i, [2 1 2]));
        [m2, k] = max(a, [], 1, 'linear');
        assert(isequal(k, [2 3 6]));
        assert(isequal(a(k), m2));
        [m3, k3] = min(a, [], 2, 'linear');
        assert(isequal(a(k3), m3));
        """);

    [Fact]
    public Task LinearNeedsTheReducingFormToMeanAnything() => RunAsserting("""
        ok = 0;
        try
            max([1 2 3], 'linear');
        catch err
            ok = ~isempty(strfind(err.message, 'reducing form'));
        end
        assert(ok == 1);
        """);
}
