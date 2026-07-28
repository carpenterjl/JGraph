using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The function forms of the operators (M37). Each test compares the function against the operator it
/// stands for rather than against a hand-computed answer: the point of routing them through the
/// interpreter is that the two can never disagree, and that is what these assertions pin.
/// </summary>
[Collection("JG facade")]
public class MatlabOperatorFunctionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabOperatorFunctionTests() => JG.Reset();

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
    public Task Arithmetic_MatchesItsOperator() => RunAsserting("""
        a = [1 2 3];
        b = [4 5 6];
        assert(isequal(plus(a, b), a + b));
        assert(isequal(minus(a, b), a - b));
        assert(isequal(times(a, b), a .* b));
        assert(isequal(rdivide(a, b), a ./ b));
        assert(isequal(ldivide(a, b), a .\ b));
        assert(isequal(power(a, 2), a .^ 2));
        assert(isequal(uminus(a), -a));
        assert(isequal(uplus(a), +a));
        assert(plus(2, 3) == 5);
        """);

    [Fact]
    public Task MatrixForms_MatchTheirOperators() => RunAsserting("""
        A = [4 3; 6 3];
        B = [1 0; 0 1];
        assert(isequal(mtimes(A, B), A * B));
        assert(isequal(mpower(A, 2), A ^ 2));
        assert(isequal(mrdivide(A, B), A / B));
        assert(isequal(mldivide(B, A), B \ A));

        % The solve form is the reason mldivide exists: same answer as the operator.
        b = [1; 2];
        assert(isequal(mldivide(A, b), A \ b));
        """);

    [Fact]
    public Task Comparisons_MatchTheirOperators() => RunAsserting("""
        a = [1 2 3];
        b = [3 2 1];
        assert(isequal(eq(a, b), a == b));
        assert(isequal(ne(a, b), a ~= b));
        assert(isequal(lt(a, b), a < b));
        assert(isequal(le(a, b), a <= b));
        assert(isequal(gt(a, b), a > b));
        assert(isequal(ge(a, b), a >= b));
        """);

    [Fact]
    public Task Xor_IsTrueWhenExactlyOneSideIs() => RunAsserting("""
        assert(xor(true, false));
        assert(xor(false, true));
        assert(~xor(true, true));
        assert(~xor(false, false));
        assert(isequal(xor([1 1 0 0], [1 0 1 0]), [0 1 1 0]));
        """);

    [Fact]
    public Task Colon_BuildsTheSameRangeAsTheOperator() => RunAsserting("""
        assert(isequal(colon(1, 5), 1:5));
        assert(isequal(colon(1, 2, 9), 1:2:9));
        assert(isequal(colon(5, 1), 5:1));
        assert(isempty(colon(5, 1)));
        """);

    [Fact]
    public async Task TheOperatorForms_AreNotUserVariables()
    {
        // They are declared into the globals by the interpreter, before the workspace owner takes its
        // snapshot — so whos must not list them alongside what the user created.
        await using IScriptSession session = NewSession();
        await session.ExecuteAsync("x = 1;", sourceId: "", CancellationToken.None);

        _output.Mark();
        await session.ExecuteAsync("whos", sourceId: "", CancellationToken.None);
        Assert.Contains("x", _output.TextSinceMark, StringComparison.Ordinal);
        Assert.DoesNotContain("mldivide", _output.TextSinceMark, StringComparison.Ordinal);
    }
}
