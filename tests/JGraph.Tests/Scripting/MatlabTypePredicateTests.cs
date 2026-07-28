using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The type predicates and class reflection (M37): class/isa, the shape questions, the elementwise
/// masks, and the NaN-aware equality pair. Where JGraph's value model differs from MATLAB's (no
/// integer classes, no string arrays, vectors without orientation) the documented answer is asserted
/// here so the difference cannot drift.
/// </summary>
[Collection("JG facade")]
public class MatlabTypePredicateTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTypePredicateTests() => JG.Reset();

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
    public Task Class_NamesEveryKindOfValue() => RunAsserting("""
        assert(strcmp(class(1), 'double'));
        assert(strcmp(class([1 2 3]), 'double'));
        assert(strcmp(class(1 > 0), 'logical'));
        assert(strcmp(class('text'), 'char'));
        assert(strcmp(class({1, 'two'}), 'cell'));
        s.a = 1;
        assert(strcmp(class(s), 'struct'));
        assert(strcmp(class(@sin), 'function_handle'));
        """);

    [Fact]
    public Task Isa_AcceptsClassNamesAndTheCategoryWords() => RunAsserting("""
        assert(isa(1, 'double'));
        assert(isa(1, 'numeric'));
        assert(isa(1, 'float'));
        assert(~isa(1, 'integer'));
        assert(~isa(1, 'char'));
        assert(isa('text', 'char'));
        assert(isa(true, 'logical'));
        """);

    [Fact]
    public Task NumericPredicates_AnswerForADoubleOnlyWorkspace() => RunAsserting("""
        assert(isfloat(1));
        assert(isfloat([1 2 3]));
        assert(~isinteger(1));
        assert(isreal(1));
        assert(isreal([1 2 3]));
        assert(~isreal(3 + 4i));
        """);

    [Fact]
    public Task FiniteAndInfinite_ComeBackAsMasks() => RunAsserting("""
        assert(isequal(isfinite([1 Inf NaN]), [1 0 0]));
        assert(isequal(isinf([1 Inf -Inf]), [0 1 1]));
        assert(isequal(isnan([1 NaN 3]), [0 1 0]));
        assert(isequal(isfinite([1 Inf; NaN 4]), [1 0; 0 1]));
        """);

    [Fact]
    public Task ShapePredicates_UseTheDocumentedOrientation() => RunAsserting("""
        assert(isscalar(1));
        assert(~isscalar([1 2 3]));
        assert(isvector([1 2 3]));
        assert(~isvector([1 2; 3 4]));
        assert(ismatrix([1 2; 3 4]));

        % JGraph vectors carry no orientation, so a vector reads as a row and only a
        % single value is also a column.
        assert(isrow([1 2 3]));
        assert(iscolumn(5));
        assert(~iscolumn([1 2 3]));
        """);

    [Fact]
    public Task TextPredicates_CoverCharsCellsAndTheMissingStringType() => RunAsserting("""
        assert(ischar('text'));
        assert(isstr('text'));
        assert(~isstring('text'));
        assert(iscellstr({'a', 'b'}));
        assert(~iscellstr({'a', 2}));
        assert(isequal(isletter('a1b'), [1 0 1]));
        assert(isequal(isspace('a b'), [0 1 0]));
        """);

    [Fact]
    public Task Issorted_ReadsNonDecreasingOrder() => RunAsserting("""
        assert(issorted([1 2 2 5]));
        assert(~issorted([3 1 2]));
        assert(issorted(5));
        """);

    [Fact]
    public Task Isequal_TreatsNaNAsUnequal_AndIsequalnDoesNot() => RunAsserting("""
        assert(~isequal(NaN, NaN));
        assert(isequaln(NaN, NaN));
        assert(isequalwithequalnans(NaN, NaN));
        assert(~isequal([1 NaN], [1 NaN]));
        assert(isequaln([1 NaN], [1 NaN]));
        assert(isequal([1 2], [1 2]));
        """);

    [Fact]
    public Task LogicalAndCast_ConvertBetweenTheClassesThatExist() => RunAsserting("""
        assert(isequal(logical([0 2 -1]), [0 1 1]));
        assert(islogical(logical(1)));
        assert(isequal(cast([1 0], 'logical'), [1 0]));
        assert(isequal(cast(3, 'double'), 3));
        """);
}
