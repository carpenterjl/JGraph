using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Linear algebra at the script level (M36): the <c>\</c>, <c>/</c>, <c>.\</c> and matrix <c>^</c>
/// operators, and the inv/det/rank/trace/norm/eig/lu/qr/svd builtins. Expected numbers are MATLAB's;
/// decomposition tests verify the defining identities to avoid pinning sign conventions.
/// </summary>
[Collection("JG facade")]
public class MatlabLinearAlgebraBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabLinearAlgebraBuiltinTests() => JG.Reset();

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

    private async Task RunExpectingError(string code, string fragment)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains(fragment, _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public Task Backslash_SolvesSquareSystems() => RunAsserting("""
        A = [1 2; 3 4];
        x = A \ [5; 11];
        % x is a column, so the reference must be one too — a row would outer-expand (M41).
        assert(norm(x - [1; 2]) < 1e-9);
        assert(norm(A * x - [5; 11]) < 1e-9);
        """);

    [Fact]
    public Task Backslash_TallSystem_IsLeastSquares() => RunAsserting("""
        A = [1 1; 1 2; 1 3];
        x = A \ [6; 0; 0];
        assert(norm(x - [8; -3]) < 1e-9);
        """);

    [Fact]
    public Task Slash_IsRightDivision() => RunAsserting("""
        x = [5 11] / [1 2; 3 4];
        assert(norm(x - [6.5 -0.5]) < 1e-9);
        """);

    [Fact]
    public Task ScalarAndElementwiseBackslash_DivideTheOtherWay() => RunAsserting("""
        assert(2 \ 8 == 4);
        assert(isequal([1 2] .\ [4 6], [4 3]));
        """);

    [Fact]
    public Task MatrixPower_IsRepeatedMultiplication() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(A^2, [7 10; 15 22]));
        assert(isequal(A^0, eye(2)));
        assert(norm(A^-1 - inv(A), 'fro') < 1e-12);
        """);

    [Fact]
    public Task InvDetRankTrace_MatchMatlab() => RunAsserting("""
        assert(norm(inv([4 7; 2 6]) - [0.6 -0.7; -0.2 0.4], 'fro') < 1e-12);
        assert(abs(det(magic(3)) + 360) < 1e-9);
        assert(rank(magic(4)) == 3);
        assert(rank([1 2; 2 4]) == 1);
        assert(trace([1 2; 3 4]) == 5);
        """);

    [Fact]
    public Task Norm_CoversVectorAndMatrixForms() => RunAsserting("""
        assert(norm([3 4]) == 5);
        assert(norm([1 -2 3], 1) == 6);
        assert(norm([1 -2 3], inf) == 3);
        assert(abs(norm([1 2; 3 4]) - 5.464985704219043) < 1e-9);
        assert(abs(norm([1 2; 3 4], 'fro') - sqrt(30)) < 1e-12);
        assert(norm([1 2; 3 4], 1) == 6);
        assert(norm([1 2; 3 4], inf) == 7);
        """);

    [Fact]
    public Task Eig_SymmetricValuesAscend_AndPairsSatisfyTheDefinition() => RunAsserting("""
        e = eig([2 1; 1 2]);
        assert(norm(e - [1; 3]) < 1e-9);
        A = [2 1; 1 2];
        [V, D] = eig(A);
        assert(norm(A * V - V * D, 'fro') < 1e-8);
        """);

    [Fact]
    public Task Eig_RotationMatrix_GivesTheImaginaryPair() => RunAsserting("""
        e = eig([0 -1; 1 0]);
        assert(abs(abs(imag(e(1))) - 1) < 1e-8);
        assert(abs(real(e(1))) < 1e-8);
        """);

    [Fact]
    public Task Lu_FactorsReassembleTheMatrix() => RunAsserting("""
        A = [4 3; 6 3];
        [L, U, P] = lu(A);
        assert(norm(P * A - L * U, 'fro') < 1e-12);
        [L2, U2] = lu(A);
        assert(norm(L2 * U2 - A, 'fro') < 1e-12);
        """);

    [Fact]
    public Task Qr_FactorsReassembleTheMatrix() => RunAsserting("""
        A = [1 1; 1 2; 1 3];

        % qr(A) is the full factorization: Q is square, so it is a basis for the whole space and
        % R carries A's shape. qr(A, 0) is the economy form, where Q spans only A's range.
        [Q, R] = qr(A);
        assert(isequal(size(Q), [3 3]));
        assert(isequal(size(R), [3 2]));
        assert(norm(Q * R - A, 'fro') < 1e-12);
        assert(norm(Q' * Q - eye(3), 'fro') < 1e-12);

        [Qe, Re] = qr(A, 0);
        assert(isequal(size(Qe), [3 2]));
        assert(isequal(size(Re), [2 2]));
        assert(norm(Qe * Re - A, 'fro') < 1e-12);
        assert(norm(Qe' * Qe - eye(2), 'fro') < 1e-12);
        """);

    [Fact]
    public Task Svd_ValuesAndFactors_MatchMatlab() => RunAsserting("""
        s = svd([1 2; 3 4]);
        assert(abs(s(1) - 5.464985704219043) < 1e-9);
        assert(abs(s(2) - 0.365966190626258) < 1e-9);
        A = [1 2; 3 4];
        [U, S, V] = svd(A);
        assert(norm(U * S * V' - A, 'fro') < 1e-9);
        """);

    [Fact]
    public Task SingularMatrices_ReportClearErrors() => RunExpectingError(
        "inv([1 2; 2 4]);", "singular");

    [Fact]
    public Task NonIntegerMatrixPower_IsAnError() => RunExpectingError(
        "[1 2; 3 4] ^ 0.5;", "integer");

    [Fact]
    public Task CaretBetweenArrays_PointsAtTheElementwiseSpelling() => RunExpectingError(
        "[1 2] ^ [3 4];", ".^");
}
