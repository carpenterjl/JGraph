using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// MATLAB's <c>vecdim</c> — a vector of dimensions where a reduction takes one — and the three
/// neighbours that took no dimension at all (M70.D).
/// <para>
/// M69's form probe recorded <c>sum(A, [1 2])</c> as a divergence and <c>stess_41.m</c> asserted the
/// refusal, so this milestone had to move the assertion as well as the behaviour. The reduction runs
/// one dimension at a time: each pass leaves the dimension it reduced a singleton rather than
/// dropping it, which is why the order of the vector cannot change the answer and why the result
/// keeps the trailing shape. Expected values are MATLAB's own.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabVectorOfDimensionsTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabVectorOfDimensionsTests() => JG.Reset();

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
    public Task AVectorOfDimensions_CollapsesEachOfThem() => RunAsserting("""
        A = magic(4);
        assert(sum(A, [1 2]) == 136);
        assert(prod([1 2; 3 4], [1 2]) == 24);
        assert(all([1 2; 3 4], [1 2]));
        assert(any([0 0; 0 5], [1 2]));
        assert(max(A, [], [1 2]) == 16);
        assert(min(A, [], [1 2]) == 1);
        """);

    [Fact]
    public Task TheOrderOfTheDimensions_CannotChangeTheAnswer() => RunAsserting("""
        b = reshape(1:24, 2, 3, 4);
        assert(isequal(sum(b, [1 2]), sum(b, [2 1])));
        assert(isequal(sum(b, [1 3]), sum(b, [3 1])));
        assert(isequal(max(b, [], [1 3]), max(b, [], [3 1])));
        """);

    [Fact]
    public Task ACollapsedDimensionStaysASingleton_SoTheShapeIsMatlabs() => RunAsserting("""
        b = reshape(1:24, 2, 3, 4);
        assert(isequal(size(sum(b, [1 2])), [1 1 4]));
        assert(isequal(sum(b, [1 2]), sum(sum(b, 1), 2)));
        assert(isequal(sum(b, [1 3]), sum(sum(b, 1), 3)));
        assert(sum(b, [1 2 3]) == 300);
        """);

    [Fact]
    public Task TheWholeArraySpellings_AgreeWithEachOther() => RunAsserting("""
        b = reshape(1:24, 2, 3, 4);
        assert(sum(b, 'all') == sum(b, [1 2 3]));
        assert(max(b, [], 'all') == max(b, [], [1 2 3]));
        assert(sum([1 NaN; 3 4], [1 2], 'omitnan') == 8);
        """);

    [Fact]
    public Task ADimensionNamedTwiceOrNotAWholeNumber_SaysSoRatherThanGuessing() => RunAsserting("""
        function ok = refuses(f, wanted)
        ok = false;
        try
            f();
        catch err
            ok = ~isempty(strfind(err.message, wanted));
        end
        end
        assert(refuses(@() sum(magic(4), [1 1]), 'twice'));
        assert(refuses(@() sum(magic(4), [0 1]), 'positive whole number'));
        assert(refuses(@() sum(magic(4), [1 2.5]), 'positive whole number'));
        """);

    [Fact]
    public Task TheRunningExtremes_WalkAColumnAndThenADimension() => RunAsserting("""
        M = [1 2 3; 4 5 6; 7 8 9];
        % Down the columns is the default, which is what cummax of a matrix means. Until M70 the
        % body underneath flattened the matrix and ran one sequence through the whole of it.
        assert(isequal(cummax(M), [1 2 3; 4 5 6; 7 8 9]));
        assert(isequal(cummax(M, 2), [1 2 3; 4 5 6; 7 8 9]));
        assert(isequal(cummin(M, 2), [1 1 1; 4 4 4; 7 7 7]));
        assert(isequal(cummin([3 1; 2 4], 1), [3 1; 2 1]));
        assert(isequal(cummax([1 5 3], 'reverse'), [5 5 3]));
        """);

    [Fact]
    public Task TheRunningExtremes_StepOverNaNAndTheRunningSumsDoNot() => RunAsserting("""
        % MATLAB splits the cumulative family here: cummax ignores NaN by default, cumsum keeps it.
        assert(isequal(cummax([1 NaN 3]), [1 1 3]));
        assert(isequal(cummin([3 NaN 1]), [3 3 1]));
        assert(isnan(sum(cumsum([1 NaN 3]))));
        assert(isequal(cumsum([1 NaN 3], 'omitnan'), [1 1 4]));
        """);

    [Fact]
    public Task VecnormTakesADimension_WithPWhereTheDimensionWouldOtherwiseSit() => RunAsserting("""
        M = [1 2 3; 4 5 6; 7 8 9];
        assert(isequal(vecnorm(M, 1, 2), [6; 15; 24]));
        assert(isequal(vecnorm(M, 1, 1), [12 15 18]));
        assert(abs(vecnorm([3 4], 2) - 5) < 1e-12);
        assert(isequal(vecnorm(M, Inf, 2), [3; 6; 9]));
        """);

    [Fact]
    public Task IssortedTakesADimension_AndStillAnswersOneLogical() => RunAsserting("""
        M = [1 2 3; 4 5 6; 7 8 9];
        assert(issorted([1 2 3]));
        assert(~issorted([3 2 1]));
        % One answer for the whole array, not one per slice — which is why this cannot go through
        % the column-wise wrapper the reductions above use.
        assert(islogical(issorted(M, 2)));
        assert(isscalar(issorted(M, 2)));
        assert(issorted(M, 2));
        assert(~issorted([3 1; 2 4], 2));
        assert(issorted([3 1; 2 4], 1) == false);
        """);
}
