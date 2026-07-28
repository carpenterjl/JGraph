using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// MATLAB's matrix reduction semantics (M36): <c>sum(A)</c> reduces each column to a row vector,
/// <c>sum(A, 2)</c> each row to a column, <c>sum(A, 'all')</c> to one value — and the same for the
/// other reductions, cumulatives, sort, and the max/min family. Vectors keep behaving exactly as
/// before. Expected values are MATLAB's own.
/// </summary>
[Collection("JG facade")]
public class MatlabColumnwiseReductionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabColumnwiseReductionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession(IScriptEngine? engine = null) => Assert
        .IsAssignableFrom<IScriptRepl>(engine ?? new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Sum_ReducesColumnsRowsOrEverything() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(sum(A), [4 6]));
        assert(isequal(sum(A, 1), [4 6]));
        assert(isequal(sum(A, 2), [3; 7]));
        assert(sum(A, 'all') == 10);
        """);

    [Fact]
    public Task OtherScalarReductions_GoColumnwiseToo() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(prod(A), [3 8]));
        assert(isequal(mean(A), [2 3]));
        assert(isequal(median([1 2; 3 4; 5 6]), [3 4]));
        s = std([1 2; 4 5]);
        assert(abs(s(1) - sqrt(4.5)) < 1e-12 && abs(s(2) - sqrt(4.5)) < 1e-12);
        assert(isequal(any([0 0; 1 0]), [1 0]));
        assert(isequal(all([1 0; 1 1]), [1 0]));
        """);

    [Fact]
    public Task CumulativesAndDiff_KeepTheMatrixShape() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(cumsum(A), [1 2; 4 6]));
        assert(isequal(cumsum(A, 2), [1 3; 3 7]));
        assert(isequal(cumprod(A), [1 2; 3 8]));
        assert(isequal(diff(A), [2 2]));
        """);

    [Fact]
    public Task Sort_SortsEachColumn_WithIndices() => RunAsserting("""
        A = [3 1; 2 4];
        assert(isequal(sort(A), [2 1; 3 4]));
        [s, i] = sort(A);
        assert(isequal(s, [2 1; 3 4]));
        assert(isequal(i, [2 1; 1 2]));
        """);

    [Fact]
    public Task MaxAndMin_ColumnwiseAndElementwiseForms() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(max(A), [3 4]));
        assert(isequal(min(A), [1 2]));
        [m, i] = max(A);
        assert(isequal(m, [3 4]));
        assert(isequal(i, [2 2]));
        assert(isequal(max(A, [], 2), [2; 4]));
        assert(isequal(min(A, [], 2), [1; 3]));
        assert(isequal(max([1 5], [4 2]), [4 5]));
        assert(isequal(max(A, 2), [2 2; 3 4]));
        assert(isequal(min(A, 2), [1 2; 2 2]));
        """);

    [Fact]
    public Task Vectors_KeepTheirFlatBehavior() => RunAsserting("""
        v = [1 2 3];
        assert(sum(v) == 6);
        assert(isequal(sum(v, 1), v));
        assert(sum(v, 2) == 6);
        assert(prod(v) == 6);
        assert(max(v) == 3);
        assert(isequal(cumsum(v), [1 3 6]));
        """);

    [Fact]
    public async Task TheJgsDialect_IsUntouchedByColumnwiseSemantics()
    {
        // JGS documents flat reductions, and a matrix argument still reports its clear error
        // rather than silently adopting MATLAB's column convention.
        await using IScriptSession session = NewSession(new JgsScriptEngine());
        ScriptRunResult flat = await session.ExecuteAsync(
            "let s = sum([1, 2, 3]);", sourceId: "", CancellationToken.None);
        Assert.True(flat.Success, flat.Message + _output.ErrorText);

        ScriptRunResult matrix = await session.ExecuteAsync(
            "sum([[1, 2], [3, 4]]);", sourceId: "", CancellationToken.None);
        Assert.False(matrix.Success);
    }
}
