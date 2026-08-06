using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52 wave E's audit findings: the four two-set operations (<c>union</c>, <c>intersect</c>,
/// <c>setdiff</c>, <c>setxor</c>), the outputs and option words <c>ismember</c> was missing, and the
/// three everyday names beside them — <c>mat2str</c>, <c>int2str</c> and <c>deal</c>.
/// </summary>
/// <remarks>
/// None of these existed. They could not show up as missing in either coverage table, because MATLAB
/// documents them with kind <c>function</c> and those tables track builtins and graphics functions —
/// which is the finding behind this file as much as the names are. Expected values are MATLAB's own.
/// </remarks>
[Collection("JG facade")]
public class MatlabSetOperationTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSetOperationTests() => JG.Reset();

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
    public Task UnionKeepsEveryValueInEitherSetOnce() => RunAsserting("""
        assert(isequal(union([1 2], [2 3]), [1 2 3]));
        assert(isequal(union([1 1 2], [2 2]), [1 2]));
        % 'stable' is A's order and then B's new values, which is the order they were first seen in.
        assert(isequal(union([5 1], [3 5], 'stable'), [5 1 3]));
        % ia names the values taken from A, ib those taken from B, so together they cover the answer.
        [c, ia, ib] = union([1 2 2], [2 3]);
        assert(isequal(c, [1 2 3]));
        assert(isequal(ia, [1; 2]));
        assert(isequal(ib, 2));
        """);

    [Fact]
    public Task IntersectKeepsWhatBothSetsHold() => RunAsserting("""
        assert(isequal(intersect([1 2 3], [2 3 4]), [2 3]));
        assert(isequal(intersect([3 1 2], [2 3], 'stable'), [3 2]));
        % Both index outputs are the same length here: every value is in both sets.
        [c, ia, ib] = intersect([1 2 3], [3 2]);
        assert(isequal(c, [2 3]));
        assert(isequal(ia, [2; 3]));
        assert(isequal(ib, [2; 1]));
        assert(isempty(intersect([1 2], [3 4])));
        """);

    [Fact]
    public Task SetdiffAndSetxorKeepWhatTheOtherSetDoesNot() => RunAsserting("""
        assert(isequal(setdiff([1 2 3], 2), [1 3]));
        assert(isequal(setdiff([3 1 2], 2, 'stable'), [3 1]));
        [c, ia] = setdiff([5 1 5 2], 2);
        assert(isequal(c, [1 5]));
        assert(isequal(ia, [2; 1]));
        assert(isequal(setxor([1 2], [2 3]), [1 3]));
        assert(isequal(setxor([2 1], [3 2], 'stable'), [1 3]));
        """);

    [Fact]
    public Task SetOperationsCompareWholeRowsAndWholeWords() => RunAsserting("""
        assert(isequal(union([1 2; 3 4], [1 2], 'rows'), [1 2; 3 4]));
        assert(isequal(intersect([1 2; 3 4], [3 4; 9 9], 'rows'), [3 4]));
        assert(isequal(setdiff([1 2; 3 4], [1 2], 'rows'), [3 4]));
        % A cell of text is a set like any other, and answers as a column the way unique does.
        u = union({'b', 'a'}, {'c'});
        assert(isequal(size(u), [3 1]));
        assert(strcmp(u{1}, 'a') && strcmp(u{3}, 'c'));
        """);

    [Fact]
    public Task ASetOperationAnswersARowOnlyWhenBothSetsWereRows() => RunAsserting("""
        assert(isequal(size(union([1 2], [2 3])), [1 3]));
        assert(isequal(size(union([1; 2], [2 3])), [3 1]));
        % A missing reading is in nothing, itself included, so it never survives an intersection.
        assert(isequal(intersect([1 NaN], [NaN 1]), 1));
        assert(numel(union([NaN NaN], [])) == 2);
        """);

    [Fact]
    public Task SetOperationsNameTheOptionsTheyKnow() => RunAsserting("""
        caught = '';
        try
            union([1 2], [3 4], 'legacy');
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'stable'));

        width = '';
        try
            setdiff([1 2; 3 4], [1 2 3], 'rows');
        catch err
            width = err.message;
        end
        assert(contains(width, 'columns'));

        mixed = '';
        try
            intersect({'a'}, [1 2]);
        catch err
            mixed = err.message;
        end
        assert(contains(mixed, 'same kind'));
        """);

    [Fact]
    public Task IsmemberSaysWhereAsWellAsWhether() => RunAsserting("""
        assert(isequal(ismember([2 5 1], [1 2 3]), [true false true]));
        [tf, loc] = ismember([2 5 1], [1 2 3]);
        assert(isequal(tf, [true false true]));
        assert(isequal(loc, [2 0 1]));
        % The earliest match is the one reported, not whichever one a search happened to land on.
        [~, first] = ismember(7, [7 7 7]);
        assert(first == 1);
        % The answer keeps the shape of what was asked about.
        assert(isequal(size(ismember([1 2; 3 4], [1 4])), [2 2]));
        """);

    [Fact]
    public Task IsmemberComparesWholeRowsWhenAsked() => RunAsserting("""
        [tf, loc] = ismember([3 4], [1 2; 3 4], 'rows');
        assert(tf && loc == 2);
        assert(~ismember([9 9], [1 2; 3 4], 'rows'));
        assert(isequal(ismember([3 4; 1 2], [1 2; 3 4], 'rows'), [true; true]));
        """);

    [Fact]
    public Task Mat2strWritesAValueTheLanguageCanReadBack() => RunAsserting("""
        assert(strcmp(mat2str(3.5), '3.5'));
        assert(strcmp(mat2str([1 2; 3 4]), '[1 2;3 4]'));
        assert(strcmp(mat2str([1 2 3]), '[1 2 3]'));
        assert(strcmp(mat2str(pi, 4), '3.142'));
        % A char row reads back as a char row, so the quotes are part of the answer.
        assert(strcmp(mat2str('abc'), '"abc"'));
        assert(strcmp(mat2str([true false]), '[true false]'));
        """);

    [Fact]
    public Task Int2strRoundsBeforeItWrites() => RunAsserting("""
        assert(strcmp(int2str(2.7), '3'));
        assert(strcmp(int2str(-2.5), '-3'));
        assert(strcmp(int2str(4), '4'));
        """);

    [Fact]
    public Task DealFillsEveryOutputAtOnce() => RunAsserting("""
        [a, b] = deal(1, 2);
        assert(a == 1 && b == 2);
        [p, q, r] = deal(7);
        assert(p == 7 && q == 7 && r == 7);
        assert(deal(5) == 5);

        caught = '';
        try
            [x, y, z] = deal(1, 2);
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'one each'));
        """);

    [Fact]
    public Task ACellsColonIsRefusedRatherThanCrashing() => RunAsserting("""
        % c{:} is a comma-separated list, which is not a value. Saying so is the fix: the colon used
        % to reach the index conversion as nothing at all and come back out as a crash.
        caught = '';
        try
            c = {1, 2};
            d = c{:};
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'one element at a time'));
        """);
}
