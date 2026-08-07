using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: two answers that were wrong for reasons of their own. <c>find(X, k)</c> read k as the index
/// base — a JGS escape hatch (ADR 0028) leaking into the MATLAB dialect, where k means "the first k".
/// And <c>class</c> answered <c>'double'</c> for a mask, because only <c>islogical</c> looked inside
/// an array; the two now read one helper and cannot disagree again.
/// </summary>
[Collection("JG facade")]
public class MatlabFindAndClassTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabFindAndClassTests() => JG.Reset();

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

    private ScriptRunResult RunJgs(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add((0, figure)), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Jgs);
    }

    [Fact]
    public Task FindKeepsTheFirstKMatches() => RunAsserting("""
        x = [0 5 0 7 9 0 2];
        assert(isequal(find(x), [2 4 5 7]));
        assert(isequal(find(x, 1), 2));
        assert(isequal(find(x, 2), [2 4]));
        assert(isequal(find(x, 99), find(x)));
        assert(isempty(find(x, 0)));
        """);

    [Fact]
    public Task FindTakesThemFromTheOtherEndOnRequest() => RunAsserting("""
        x = [0 5 0 7 9 0 2];
        assert(isequal(find(x, 1, 'last'), 7));
        assert(isequal(find(x, 2, 'last'), [5 7]));
        assert(isequal(find(x, 2, 'first'), [2 4]));
        """);

    /// <summary>
    /// The idiom the divergence broke: a mask with more than one match, where "the first one" and
    /// "all of them" are different answers and the old reading silently gave the second.
    /// </summary>
    [Fact]
    public Task TheFirstMatchOfSeveralIsOneNumber() => RunAsserting("""
        t = [70 90 95 88];
        first = find(t > 85, 1);
        assert(numel(first) == 1);
        assert(first == 2);
        """);

    [Fact]
    public Task FindSaysWhatItWantedWhenTheArgumentsAreWrong() => RunAsserting("""
        ok = 0;
        try
            find([1 0 1], -1);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'zero or more'));
        end
        try
            find([1 0 1], 1, 'middle');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'first'));
        end
        assert(ok == 2);
        """);

    [Fact]
    public Task TheSubscriptFormLimitsInStepAcrossAllItsOutputs() => RunAsserting("""
        a = [1 0 3; 0 5 0];
        [r, c] = find(a, 2);
        assert(isequal(r, [1; 2]));
        assert(isequal(c, [1; 2]));
        [r2, c2, v] = find(a, 2, 'last');
        assert(numel(r2) == 2);
        assert(numel(c2) == 2);
        assert(isequal(v, [5; 3]));
        """);

    /// <summary>
    /// Subscripts stand up the way linear indices already did — a row for a row vector, a column for
    /// anything else. The two forms of the same call used to disagree, which only stess_24 caught.
    /// </summary>
    [Fact]
    public Task TheSubscriptFormStandsUpTheSameWayTheIndexFormDoes() => RunAsserting("""
        [r, c] = find([0 1; 1 0]);
        assert(isequal(size(r), [2 1]));
        assert(isequal(r, [2; 1]) && isequal(c, [1; 2]));
        [r2, c2, v2] = find([0 3; 4 0]);
        assert(isequal(v2, [4; 3]));
        % A row vector keeps its orientation, both here and in the one-output form.
        [r3, c3] = find([0 3 0 5]);
        assert(isequal(size(r3), [1 2]));
        assert(isequal(r3, [1 1]) && isequal(c3, [2 4]));
        assert(isequal(size(find([0 1; 1 0])), [2 1]));
        """);

    /// <summary>
    /// The other side of the split: a JGS script that wrote the second argument meant the index base,
    /// and it still does. Both readings cannot be right, so each dialect keeps its own.
    /// </summary>
    [Fact]
    public void JgsStillReadsTheSecondArgumentAsTheIndexBase()
    {
        ScriptRunResult result = RunJgs("""
            let x = [0, 5, 0, 7]
            print(find(x))
            print(find(x, 1))
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        // The last two lines, because the let statement echoes ahead of them.
        string[] lines = _output.NormalText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("[1, 3]", lines[^2].Trim());  // 0-based, the JGS default
        Assert.Equal("[2, 4]", lines[^1].Trim());  // the same matches, numbered from 1
    }

    [Fact]
    public Task ClassAnswersLogicalForAMaskAndNotJustForOneBool() => RunAsserting("""
        assert(strcmp(class(true), 'logical'));
        assert(strcmp(class([1 2 3] > 2), 'logical'));
        assert(strcmp(class([true false]), 'logical'));
        assert(strcmp(class([1 2 3]), 'double'));
        assert(strcmp(class(uint8(7)), 'uint8'));
        """);

    /// <summary>
    /// Emptiness carries no type: every element of <c>[]</c> satisfies any test, so a naive check
    /// would call it a mask. MATLAB says <c>'double'</c>.
    /// </summary>
    [Fact]
    public Task AnEmptyArrayIsNotAMask() => RunAsserting("""
        assert(strcmp(class([]), 'double'));
        assert(~islogical([]));
        """);

    [Fact]
    public Task ClassAndIslogicalNowAgreeEverywhere() => RunAsserting("""
        v = {true, [1 2] > 1, [1 2], 'text', uint8(3), []};
        for k = 1:numel(v)
            assert(islogical(v{k}) == strcmp(class(v{k}), 'logical'));
        end
        assert(isa([1 2] > 1, 'logical'));
        """);
}
