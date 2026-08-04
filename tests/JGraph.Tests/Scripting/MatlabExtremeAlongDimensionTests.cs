using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>max(A, [], dim)</c> and <c>min(A, [], dim)</c> along any dimension of any shape (M48). The
/// two-dimensional forms worked before; an N-D array was folded into rows first, so a reduction past
/// the second dimension silently reduced along the fold. Expected values are MATLAB's own.
/// </summary>
[Collection("JG facade")]
public class MatlabExtremeAlongDimensionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabExtremeAlongDimensionTests() => JG.Reset();

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
    public Task AMatrix_ReducesAlongTheDimensionItIsGiven() => RunAsserting("""
        A = [1 5 3; 8 2 9];
        assert(isequal(max(A, [], 1), [8 5 9]));
        assert(isequal(max(A, [], 2), [5; 9]));
        assert(isequal(min(A, [], 1), [1 2 3]));
        assert(isequal(min(A, [], 2), [1; 2]));
        """);

    [Fact]
    public Task TheIndexOutput_ReportsThePositionInsideEachSlice() => RunAsserting("""
        A = [1 5 3; 8 2 9];
        [m, i] = max(A, [], 2);
        assert(isequal(m, [5; 9]));
        assert(isequal(i, [2; 3]));
        [m, i] = min(A, [], 1);
        assert(isequal(m, [1 2 3]));
        assert(isequal(i, [1 2 1]));
        """);

    [Fact]
    public Task AVolume_ReducesAlongEachOfItsThreeDimensions() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        x = max(b, [], 1);
        assert(isequal(size(x), [1 2 3]));
        assert(isequal(x, cat(3, [2 4], [6 8], [10 12])));
        y = max(b, [], 2);
        assert(isequal(size(y), [2 1 3]));
        assert(isequal(y, cat(3, [3; 4], [7; 8], [11; 12])));
        z = max(b, [], 3);
        assert(isequal(size(z), [2 2]));
        assert(isequal(z, [9 11; 10 12]));
        assert(isequal(min(b, [], 3), [1 3; 2 4]));
        """);

    [Fact]
    public Task AVolumeIndex_CountsAlongTheReducedDimension() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        [m, i] = max(b, [], 3);
        assert(isequal(m, [9 11; 10 12]));
        assert(isequal(i, [3 3; 3 3]));
        [m, i] = min(b, [], 3);
        assert(isequal(m, [1 3; 2 4]));
        assert(isequal(i, [1 1; 1 1]));
        """);

    [Fact]
    public Task WithNoDimension_ItReducesAlongTheFirstNonSingletonOne() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(max(b), max(b, [], 1)));
        assert(isequal(size(max(b)), [1 2 3]));
        r = reshape(1:6, 1, 2, 3);
        assert(isequal(max(r), max(r, [], 2)));
        assert(isequal(size(max(r)), [1 1 3]));
        """);

    [Fact]
    public Task TheAllForm_ReducesEverythingAndIndexesLinearly() => RunAsserting("""
        A = [1 5 3; 8 2 9];
        assert(max(A, [], 'all') == 9);
        [m, i] = max(A, [], 'all');
        assert(m == 9 && i == 6);
        b = reshape(1:12, 2, 2, 3);
        assert(max(b, [], 'all') == 12);
        assert(min(b, [], 'all') == 1);
        """);

    [Fact]
    public Task ADimensionPastTheLast_ChangesNothing() => RunAsserting("""
        b = reshape(1:12, 2, 2, 3);
        assert(isequal(max(b, [], 5), b));
        v = [3 1 4];
        assert(isequal(max(v, [], 1), v));
        assert(isequal(max(v, [], 4), v));
        """);

    [Fact]
    public Task VectorsReduceAlongTheirOwnDirectionOnly() => RunAsserting("""
        v = [3 1 4 1 5];
        assert(isequal(max(v, [], 1), v));
        assert(max(v, [], 2) == 5);
        assert(max(v) == 5);
        c = [3; 1; 4];
        assert(max(c, [], 1) == 4);
        assert(isequal(max(c, [], 2), c));
        [m, i] = max(c);
        assert(m == 4 && i == 3);
        """);

    [Fact]
    public Task ChainedReductions_CollapseABlockStackToOneValuePerBlock() => RunAsserting("""
        % The qtdecomp idiom: one number per block of a stack of blocks.
        blocks = reshape(1:8, 2, 2, 2);
        q = squeeze(max(max(blocks, [], 1), [], 2));
        assert(isequal(q, [4; 8]));
        p = squeeze(min(min(blocks, [], 1), [], 2));
        assert(isequal(p, [1; 5]));
        """);

    [Fact]
    public Task TheElementwiseAndScalarForms_AreUnchanged() => RunAsserting("""
        assert(isequal(max([1 5 3], [4 2 2]), [4 5 3]));
        assert(isequal(min([1 5 3], 2), [1 2 2]));
        assert(max(7, 3) == 7);
        assert(min(7, 3) == 3);
        A = [1 2; 3 4];
        assert(isequal(max(A, 2), [2 2; 3 4]));
        """);

    [Fact]
    public Task TiesGoToTheFirstPosition() => RunAsserting("""
        A = [5 1; 5 1];
        [m, i] = max(A, [], 1);
        assert(isequal(m, [5 1]));
        assert(isequal(i, [1 1]));
        [m, i] = max([2 9 9 2], [], 2);
        assert(m == 9 && i == 2);
        """);
}
