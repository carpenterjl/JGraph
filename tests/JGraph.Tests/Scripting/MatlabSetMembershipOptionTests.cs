using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the option surfaces of the set and selection builtins — <c>unique</c>'s occurrence and set
/// order and its index outputs, <c>sort</c>'s missing-value and comparison rules, <c>maxk</c>/
/// <c>mink</c> and <c>histc</c> along a dimension, <c>circshift</c> per dimension, the tolerance pair,
/// and <c>randi</c>'s output class. Expected values are MATLAB's own.
/// </summary>
/// <remarks>
/// One answer changed rather than appeared: <c>sort</c> put NaN first, because
/// <c>double.CompareTo</c> does. MATLAB sorts it last ascending and first descending — a missing
/// reading is at the end of the list either way — so <c>sort([1 NaN 2])</c> now answers
/// <c>[1 2 NaN]</c>.
/// </remarks>
[Collection("JG facade")]
public class MatlabSetMembershipOptionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSetMembershipOptionTests() => JG.Reset();

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

    // --- unique ---------------------------------------------------------------------------------

    /// <summary>
    /// The two identities the index outputs exist for. A script that groups by <c>ic</c> and names the
    /// groups out of <c>C</c> is relying on both of them at once.
    /// </summary>
    [Fact]
    public Task TheIndexOutputsRebuildBothTheValuesAndTheInput() => RunAsserting("""
        x = [3 1 3 2 1];
        [c, ia, ic] = unique(x);
        assert(isequal(c, [1 2 3]));
        assert(isequal(x(ia)', c'));
        assert(isequal(c(ic), x));
        """);

    [Fact]
    public Task IndexOutputsComeBackAsColumnsWhateverTheInputWas() => RunAsserting("""
        [~, ia, ic] = unique([3 1 3 2 1]);
        assert(isequal(size(ia), [3 1]));
        assert(isequal(size(ic), [5 1]));
        """);

    [Fact]
    public Task StableKeepsTheValuesWhereTheyFirstAppeared() => RunAsserting("""
        x = [3 1 3 2 1];
        assert(isequal(unique(x, 'stable'), [3 1 2]));
        assert(isequal(unique(x, 'sorted'), [1 2 3]));
        [c, ia] = unique(x, 'stable');
        assert(isequal(x(ia)', c'));
        """);

    [Fact]
    public Task LastAsksForTheOtherOccurrenceOfEachValue() => RunAsserting("""
        x = [3 1 3 2 1];
        [~, first] = unique(x, 'first');
        [~, last] = unique(x, 'last');
        assert(isequal(first', [2 4 1]));
        assert(isequal(last', [5 4 3]));
        """);

    [Fact]
    public Task RowsComparesWholeRowsLeftToRight() => RunAsserting("""
        a = [1 2; 3 4; 1 2];
        [c, ia, ic] = unique(a, 'rows');
        assert(isequal(c, [1 2; 3 4]));
        assert(isequal(ia', [1 2]));
        assert(isequal(ic', [1 2 1]));
        assert(isequal(c(ic, :), a));
        """);

    /// <summary>
    /// A missing reading is not evidence that two rows agree, so each NaN is its own value — which is
    /// also what keeps <c>C(ic)</c> exact.
    /// </summary>
    [Fact]
    public Task EachMissingValueIsDistinctAndSortsLast() => RunAsserting("""
        c = unique([3 NaN 1 NaN]);
        assert(numel(c) == 4);
        assert(isequal(c(1:2), [1 3]));
        assert(isnan(c(3)) && isnan(c(4)));
        """);

    [Fact]
    public Task TheAnswerIsAColumnUnlessTheInputWasARow() => RunAsserting("""
        assert(isequal(size(unique([3 1 2])), [1 3]));
        assert(isequal(size(unique([1; 2; 1])), [2 1]));
        """);

    [Fact]
    public Task UniqueSaysWhatItWantedWhenTheOptionsAreWrong() => RunAsserting("""
        ok = 0;
        try
            unique([1 2], 'stabel');
        catch err
            ok = ok + ~isempty(strfind(err.message, "'stable'"));
        end
        try
            unique([1 2], 'stable', 'last');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'first appeared'));
        end
        assert(ok == 2);
        """);

    // --- sort -----------------------------------------------------------------------------------

    [Fact]
    public Task MissingReadingsSortToTheEndOfTheList() => RunAsserting("""
        s = sort([1 NaN 2]);
        assert(isequal(s(1:2), [1 2]));
        assert(isnan(s(3)));
        d = sort([1 NaN 2], 'descend');
        assert(isnan(d(1)));
        assert(isequal(d(2:3), [2 1]));
        """);

    [Fact]
    public Task MissingPlacementOverridesWhichEndTheyLandAt() => RunAsserting("""
        f = sort([1 NaN 2], 'MissingPlacement', 'first');
        assert(isnan(f(1)));
        l = sort([1 NaN 2], 'descend', 'MissingPlacement', 'last');
        assert(isnan(l(3)));
        assert(isequal(l(1:2), [2 1]));
        """);

    [Fact]
    public Task ComparisonMethodAbsOrdersByMagnitude() => RunAsserting("""
        assert(isequal(sort([-3 1 -2], 'ComparisonMethod', 'abs'), [1 -2 -3]));
        assert(isequal(sort([-3 1 -2], 'ComparisonMethod', 'real'), [-3 -2 1]));
        assert(isequal(sort([-3 1 -2]), [-3 -2 1]));
        """);

    /// <summary>
    /// A complex array has no natural order by value, so <c>'abs'</c> is the one that means something:
    /// magnitude, then angle. Sorting it at all is new — it used to be refused.
    /// </summary>
    [Fact]
    public Task ComplexNumbersOrderByRealPartOrByMagnitude() => RunAsserting("""
        z = [3+4i, 1, -5];
        assert(isequal(sort(z), [-5, 1, 3+4i]));
        assert(isequal(sort(z, 'ComparisonMethod', 'abs'), [1, 3+4i, -5]));
        """);

    [Fact]
    public Task SortStillTakesItsOrderWordAndItsDimension() => RunAsserting("""
        assert(isequal(sort([3 1 2], 'descend'), [3 2 1]));
        assert(isequal(sort([3 1; 2 4], 2, 'descend'), [3 1; 4 2]));
        assert(isequal(sort([3 1; 2 4], 1, 'MissingPlacement', 'last'), [2 1; 3 4]));
        """);

    // --- maxk and mink --------------------------------------------------------------------------

    [Fact]
    public Task TheKLargestComeBackWithWhereTheyCameFrom() => RunAsserting("""
        [b, i] = maxk([3 1 4 1 5], 2);
        assert(isequal(b, [5 4]));
        assert(isequal(i, [5 3]));
        [s, j] = mink([3 1 4 1 5], 2);
        assert(isequal(s, [1 1]));
        assert(isequal(j, [2 4]));
        """);

    [Fact]
    public Task TheKLargestFollowTheDimensionTheyAreAskedFor() => RunAsserting("""
        m = [1 9; 7 2];
        assert(isequal(maxk(m, 1), [7 9]));
        assert(isequal(maxk(m, 1, 2), [9; 7]));
        assert(isequal(maxk(m, 2), [7 9; 1 2]));
        """);

    /// <summary>A missing reading is never among the largest, so it sinks to the back of either end.</summary>
    [Fact]
    public Task MissingReadingsAreNeverAmongTheLargest() => RunAsserting("""
        b = maxk([3 NaN 5], 2);
        assert(isequal(b, [5 3]));
        s = mink([3 NaN 1], 2);
        assert(isequal(s, [1 3]));
        """);

    [Fact]
    public Task AskingForMoreThanThereAreGivesWhatThereIs() => RunAsserting("""
        assert(isequal(maxk([2 1], 5), [2 1]));
        assert(isempty(maxk([2 1], 0)));
        """);

    // --- circshift ------------------------------------------------------------------------------

    [Fact]
    public Task CircshiftMovesAlongTheDimensionItIsGiven() => RunAsserting("""
        assert(isequal(circshift([1 2 3 4], 1), [4 1 2 3]));
        assert(isequal(circshift([1 2 3 4], -1), [2 3 4 1]));
        m = [1 9; 7 2];
        assert(isequal(circshift(m, 1), [7 2; 1 9]));
        assert(isequal(circshift(m, 1, 2), [9 1; 2 7]));
        """);

    /// <summary>
    /// A vector of amounts is the one-dimension form repeated, which is why there is no separate rule
    /// for it: shifting rows then columns is two rotations, not a new operation.
    /// </summary>
    [Fact]
    public Task AnAmountPerDimensionShiftsEachOfThemInTurn() => RunAsserting("""
        m = [1 9; 7 2];
        assert(isequal(circshift(m, [1 1]), circshift(circshift(m, 1, 1), 1, 2)));
        assert(isequal(circshift(m, [1 0]), circshift(m, 1, 1)));
        """);

    [Fact]
    public Task NamingADimensionTakesOneAmount() => RunAsserting("""
        ok = 0;
        try
            circshift([1 2 3], [1 1], 2);
        catch err
            ok = ~isempty(strfind(err.message, 'single amount'));
        end
        assert(ok == 1);
        """);

    // --- histc ----------------------------------------------------------------------------------

    [Fact]
    public Task HistcCountsPerSliceWhenGivenADimension() => RunAsserting("""
        assert(isequal(histc([1 2 3 4], [0 2 4]), [1 2 1]));
        assert(isequal(histc([1 2 3 4], [0 2 4], 2), [1 2 1]));
        assert(isequal(histc([1 2; 3 4], [0 2 4], 1), [1 0; 1 1; 0 1]));
        """);

    // --- uniquetol and ismembertol --------------------------------------------------------------

    [Fact]
    public Task UniquetolReportsWhichValuesItKeptAndWhereEachWent() => RunAsserting("""
        [c, ia, ic] = uniquetol([1 1.0000001 2]);
        assert(isequal(c, [1 2]));
        assert(isequal(ia', [1 3]));
        assert(isequal(ic', [1 1 2]));
        """);

    /// <summary>
    /// The default tolerance is relative: it means "this many significant figures", scaled by the
    /// largest magnitude in the data. <c>'DataScale'</c> is how a script says what that scale is
    /// instead of letting the data decide.
    /// </summary>
    [Fact]
    public Task DataScaleReplacesTheScaleTheDataWouldHaveSet() => RunAsserting("""
        assert(numel(uniquetol([1 1.05 2], 0.1)) == 2);
        assert(numel(uniquetol([1 1.05 2], 0.1, 'DataScale', 1)) == 2);
        assert(numel(uniquetol([1 1.05 2], 0.01, 'DataScale', 1)) == 3);
        """);

    [Fact]
    public Task ByRowsComparesWholeRowsWithinTheTolerance() => RunAsserting("""
        b = [1 2; 1.0000001 2; 5 6];
        [c, ia, ic] = uniquetol(b, 'ByRows', true);
        assert(isequal(c, [1 2; 5 6]));
        assert(isequal(ia', [1 3]));
        assert(isequal(ic', [1 1 2]));
        """);

    /// <summary>
    /// <c>'OutputAllIndices'</c> turns the first index output from one member per group into every
    /// member of it, which is the form a script wants when it is going to average the group.
    /// </summary>
    [Fact]
    public Task OutputAllIndicesReportsEveryMemberOfEachGroup() => RunAsserting("""
        [c, ia] = uniquetol([1 1.0000001 2], 'OutputAllIndices', true);
        assert(numel(c) == 2);
        assert(iscell(ia));
        assert(numel(ia) == 2);
        assert(isequal(ia{1}', [1 2]));
        assert(isequal(ia{2}', 3));
        """);

    [Fact]
    public Task IsmembertolSaysWhichMemberEachValueMatched() => RunAsserting("""
        [lia, locb] = ismembertol([1 5], [1.0000001 2]);
        assert(isequal(lia, [true false]));
        assert(isequal(locb, [1 0]));
        """);

    // --- randi ----------------------------------------------------------------------------------

    /// <summary>
    /// A trailing class name says what the draws come back as. It is read off the end before the shape
    /// arguments are, so a size can never be mistaken for a word or the other way round.
    /// </summary>
    [Fact]
    public Task RandiAnswersInTheClassItIsAskedFor() => RunAsserting("""
        rng(0);
        r = randi(10, 1, 4, 'int32');
        assert(strcmp(class(r), 'int32'));
        assert(isequal(size(r), [1 4]));
        assert(strcmp(class(randi(10, 1, 4)), 'double'));
        assert(strcmp(class(randi(1, 1, 4, 'logical')), 'logical'));
        assert(strcmp(class(randi(10, 1, 4, 'like', uint8(3))), 'uint8'));
        """);

    [Fact]
    public Task RandiRefusesAClassItDoesNotHave() => RunAsserting("""
        ok = 0;
        try
            randi(5, 1, 2, 'int9');
        catch err
            ok = ~isempty(strfind(err.message, 'int9'));
        end
        assert(ok == 1);
        """);
}
