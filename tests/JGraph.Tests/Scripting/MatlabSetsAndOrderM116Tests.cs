using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M116: <c>unique</c>, the set operations, <c>sortrows</c> and <c>ismember</c> stop comparing boxed
/// values through a delegate and start reading the numbers. Every rule that made the old comparison
/// what it was is pinned here, because each of them is a place the new road could quietly disagree:
/// where a missing reading sorts and whether it is ever equal to itself, which of two equal values a
/// group is named by, how the two zeros compare, and whether a later key really only breaks a tie in
/// an earlier one.
/// </summary>
/// <remarks>
/// These are not new answers. Every assertion below passed before the change as well — which is the
/// point of writing them down now, since a faster road that answers something else is not the same
/// function and nothing else in the suite was watching these particular corners.
/// </remarks>
[Collection("JG facade")]
public class MatlabSetsAndOrderM116Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSetsAndOrderM116Tests() => JG.Reset();

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

    /// <summary>A missing reading is its own group, so the indices still rebuild the input exactly.</summary>
    [Fact]
    public Task EveryMissingReadingIsItsOwnValue() => RunAsserting("""
        x = [3 NaN 1 3 NaN 1];
        [c, ia, ic] = unique(x);
        assert(numel(c) == 4);
        assert(c(1) == 1 && c(2) == 3 && isnan(c(3)) && isnan(c(4)));
        % C = A(ia) and A = C(ic) are the property the index outputs exist for.
        assert(isequaln(c(:)', x(ia)));
        assert(isequaln(x, reshape(c(ic), size(x))));
        """);

    /// <summary>
    /// A group is named by its smallest index, or its largest when asked — not by whichever equal
    /// value the sort happened to leave in front, which is the one thing an unstable sort could move.
    /// </summary>
    [Fact]
    public Task AGroupIsNamedByItsFirstOrItsLastMember() => RunAsserting("""
        x = [5 2 5 2 5 2 9];
        [~, first] = unique(x, 'first');
        [~, lastOne] = unique(x, 'last');
        assert(isequal(first(:)', [2 1 7]));
        assert(isequal(lastOne(:)', [6 5 7]));
        % 'stable' reports the groups in the order they first turn up, whatever the sort did.
        [c, ia] = unique(x, 'stable');
        assert(isequal(c(:)', [5 2 9]));
        assert(isequal(ia(:)', [1 2 7]));
        """);

    /// <summary>
    /// The two zeros are told apart by the ordering and not by the equality: <c>unique</c> compares
    /// them the way a comparison does and keeps both, while <c>ismember</c> compares them the way
    /// <c>==</c> does and finds either in a set holding the other.
    /// </summary>
    [Fact]
    public Task TheTwoZerosAreOrderedApartAndMatchedTogether() => RunAsserting("""
        z = unique([0 -0 0]);
        assert(numel(z) == 2);
        assert(isequal(ismember([0 -0], [-0]), [true true]));
        assert(isequal(ismember([0 -0], [0]), [true true]));
        """);

    /// <summary>A large numeric set is answered by the same rules a small one is.</summary>
    [Fact]
    public Task MembershipOfALargeSetAnswersWhatAWalkAnswers() => RunAsserting("""
        % Past the size at which the set is sorted rather than walked, so both roads are exercised.
        big = [1:200, NaN, -0];
        small = [7 9];
        assert(isequal(ismember([7 9 8], small), [true true false]));
        assert(isequal(ismember([200 201 1], big), [true false true]));
        % A missing reading is a member of a set holding one, which is what == over doubles says here.
        assert(ismember(NaN, big));
        assert(ismember(0, big));
        % A set of text is compared as text, and takes the walk rather than the sorted keys.
        assert(ismember('b', {'a', 'b', 'c'}));
        assert(~ismember('q', {'a', 'b', 'c'}));
        """);

    /// <summary>
    /// Later keys break ties in earlier ones and nothing else, and rows equal in every key keep the
    /// order they arrived in — which is the whole claim a pass-per-key ordering has to make good.
    /// </summary>
    [Fact]
    public Task LaterKeysOnlyBreakTiesInEarlierOnes() => RunAsserting("""
        A = [1 2 10; 1 1 20; 2 9 30; 1 2 40; 2 9 50];
        [B, i] = sortrows(A, [1 2]);
        assert(isequal(i(:)', [2 1 4 3 5]));
        assert(isequal(B(:, 3)', [20 10 40 30 50]));
        % Reversing one key reverses that key alone; the arrival order still settles a full tie.
        [~, j] = sortrows(A, [1 2], {'ascend', 'descend'});
        assert(isequal(j(:)', [1 4 2 3 5]));
        % Descending on the first key does not disturb the second, nor the arrival order under it.
        [~, k] = sortrows(A, [-1 2]);
        assert(isequal(k(:)', [3 5 2 1 4]));
        """);

    /// <summary>
    /// A missing reading sorts behind everything ascending and in front of everything descending,
    /// and every one of them keeps the place it arrived in.
    /// </summary>
    [Fact]
    public Task MissingRowsSortToWhicheverEndTheDirectionPutsThem() => RunAsserting("""
        A = [2 1; NaN 2; 1 3; NaN 4];
        [~, up] = sortrows(A, 1);
        assert(isequal(up(:)', [3 1 2 4]));
        [~, down] = sortrows(A, 1, 'descend');
        assert(isequal(down(:)', [2 4 1 3]));
        """);

    /// <summary>The set operations answer over numbers what they answer over text.</summary>
    [Fact]
    public Task TheSetOperationsAnswerTheSameOverNumbersAndText() => RunAsserting("""
        a = [5 1 5 3 9];
        b = [3 5 7];
        assert(isequal(intersect(a, b), [3 5]));
        assert(isequal(setdiff(a, b), [1 9]));
        assert(isequal(union(a, b), [1 3 5 7 9]));
        assert(isequal(setxor(a, b), [1 7 9]));
        % A missing reading is in nothing, itself included.
        assert(isempty(intersect([NaN 1], [NaN 2])));
        % Text takes the road that compares strings, and answers the same shape of thing.
        assert(isequal(intersect({'b','a','b'}, {'a','c'}), {'a'}));
        """);

    /// <summary>
    /// <c>histc</c> gives its final edge a bin of its own holding exact hits, and every other bin
    /// takes its left edge and not its right — over edges that are evenly spread and edges that
    /// are not.
    /// </summary>
    [Fact]
    public Task TheCountsFallOnTheSameSideOfEveryEdge() => RunAsserting("""
        even = histc([0 0.5 1 1.5 2 2.5], 0:0.5:2);
        assert(isequal(even, [1 1 1 1 1]));
        odd = histc([0 0.05 0.2 3 90 91], [0 0.1 0.15 3 90]);
        assert(isequal(odd, [2 0 1 1 1]));
        % A reading past the last edge is in no bin at all, and neither is a missing one.
        assert(isequal(histc([-1 5 NaN], [0 1 2]), [0 0 0]));
        """);
}
