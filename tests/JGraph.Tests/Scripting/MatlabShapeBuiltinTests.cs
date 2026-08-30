using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Matrix generation and shape builtins (M36): eye, diag, magic, logspace, reshape, cat and friends,
/// flips, permute/transpose forms, prod, ismember, dot, and MATLAB's square constructor shapes.
/// Every expected value here is what real MATLAB prints for the same input.
/// </summary>
[Collection("JG facade")]
public class MatlabShapeBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabShapeBuiltinTests() => JG.Reset();

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
    public Task Eye_BuildsIdentityMatrices() => RunAsserting("""
        assert(isequal(eye(3), [1 0 0; 0 1 0; 0 0 1]));
        assert(isequal(eye(2, 3), [1 0 0; 0 1 0]));
        assert(eye() == 1);
        """);

    [Fact]
    public Task Diag_BuildsAndExtractsDiagonals() => RunAsserting("""
        assert(isequal(diag([1 2 3]), [1 0 0; 0 2 0; 0 0 3]));
        assert(isequal(diag([1 2], 1), [0 1 0; 0 0 2; 0 0 0]));
        assert(isequal(diag(magic(3)), [8; 5; 2]));
        assert(isequal(diag(magic(3), 1), [1; 7]));
        assert(isequal(diag([1; 2; 3]), [1 0 0; 0 2 0; 0 0 3]));
        """);

    [Fact]
    public Task Magic_MatchesAllThreeConstructions() => RunAsserting("""
        assert(isequal(magic(3), [8 1 6; 3 5 7; 4 9 2]));
        assert(isequal(magic(4), [16 2 3 13; 5 11 10 8; 9 7 6 12; 4 14 15 1]));
        assert(isequal(magic(6), [35 1 6 26 19 24; 3 32 7 21 23 25; 31 9 2 22 27 20; 8 28 33 17 10 15; 30 5 34 12 14 16; 4 36 29 13 18 11]));
        """);

    [Fact]
    public Task Logspace_IsPowersOfTen_AndEndsAtPiWhenAsked() => RunAsserting("""
        assert(isequal(logspace(0, 2, 3), [1 10 100]));
        l = logspace(0, pi, 4);
        assert(abs(l(4) - pi) < 1e-12);
        assert(length(logspace(1, 2)) == 50);
        """);

    [Fact]
    public Task Ndims_IsTwoForEverythingFlat() => RunAsserting("""
        assert(ndims(5) == 2);
        assert(ndims([1 2 3]) == 2);
        assert(ndims([1 2; 3 4]) == 2);
        """);

    [Fact]
    public Task Reshape_ReadsAndFillsColumnMajor() => RunAsserting("""
        assert(isequal(reshape(1:6, 2, 3), [1 3 5; 2 4 6]));
        assert(isequal(reshape([1 2 3; 4 5 6], 3, 2), [1 5; 4 3; 2 6]));
        assert(isequal(reshape(1:6, [], 2), [1 4; 2 5; 3 6]));
        assert(isequal(reshape(1:6, 2, []), [1 3 5; 2 4 6]));
        """);

    [Fact]
    public async Task Reshape_RejectsAChangedElementCount()
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(
            "reshape(1:6, 4, 2);", sourceId: "", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("element count", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public Task Concatenation_WorksAlongBothDimensions() => RunAsserting("""
        assert(isequal(horzcat([1 2], [3]), [1 2 3]));
        assert(isequal(vertcat([1 2], [3 4]), [1 2; 3 4]));
        assert(isequal(cat(2, [1 2; 3 4], [5; 6]), [1 2 5; 3 4 6]));
        assert(isequal(cat(1, [1 2], [3 4]), [1 2; 3 4]));
        assert(strcmp(horzcat('ab', 'cd'), 'abcd'));
        """);

    [Fact]
    public Task Flips_ReverseTheRightDimension() => RunAsserting("""
        assert(isequal(flip([1 2 3]), [3 2 1]));
        assert(isequal(flipud([1 2; 3 4]), [3 4; 1 2]));
        assert(isequal(fliplr([1 2; 3 4]), [2 1; 4 3]));
        assert(isequal(flip([1 2; 3 4], 2), [2 1; 4 3]));
        assert(isequal(flip([1 2; 3 4]), [3 4; 1 2]));
        """);

    [Fact]
    public Task PermuteAndTransposeForms_MatchTheOperator() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(permute(A, [2 1]), A'));
        assert(isequal(permute(A, [1 2]), A));
        assert(isequal(transpose(A), [1 3; 2 4]));
        assert(ctranspose(1 + 2i) == 1 - 2i);
        assert(isequal(squeeze(A), A));
        """);

    [Fact]
    public Task ProdIsmemberDot_ComputeMatlabAnswers() => RunAsserting("""
        assert(prod([1 2 3 4]) == 24);
        assert(ismember(2, [1 2 3]));
        assert(isequal(ismember([1 5], [1 2 3]), [1 0]));
        assert(ismember('b', {'a', 'b'}));
        assert(dot([1 2 3], [4 5 6]) == 32);
        """);

    [Fact]
    public Task MatlabConstructors_BuildSquareMatricesFromOneSize() => RunAsserting("""
        assert(isequal(size(zeros(3)), [3 3]));
        assert(isequal(size(ones(2)), [2 2]));
        assert(isequal(size(rand(3)), [3 3]));
        assert(isequal(size(randn(2, 3)), [2 3]));
        r = rand();
        assert(r >= 0 && r < 1);
        assert(isequal(size(rand(2, 3)), [2 3]));
        """);
}
