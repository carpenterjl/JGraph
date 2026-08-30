using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The shapes and the domains three linear-algebra chips were opened over: <c>diag</c> and
/// <c>eig</c> answering a row where MATLAB answers a column, <c>norm</c>/<c>tril</c>/<c>triu</c>
/// refusing a complex argument outright, the economy <c>qr</c> handing back a permutation matrix
/// where MATLAB hands back a vector, and an infinite eigenvalue of a pencil losing its sign.
/// </summary>
/// <remarks>
/// Every expectation here was read off MATLAB R2024a on this machine before it was written down,
/// and the assertions run inside the scripts so that what is pinned is the value rather than the
/// way JGraph displays it. The orientation cases are asserted through <c>size</c> rather than
/// through <c>isequal</c> alone, because implicit expansion is exactly what makes a wrong
/// orientation survive a comparison: a row minus a column is a matrix, not an error.
/// </remarks>
[Collection("JG facade")]
public class ChipLinalgShapeTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ChipLinalgShapeTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task Asserts(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- diag -----------------------------------------------------------------------------------

    [Fact]
    public Task ADiagonalReadOutOfAMatrixIsAColumn() => Asserts("""
        A = [1 2 3; 4 5 6; 7 8 9];
        assert(isequal(size(diag(A)), [3 1]));
        assert(isequal(diag(A), [1; 5; 9]));
        assert(isequal(size(diag(A, 1)), [2 1]));
        assert(isequal(diag(A, 1), [2; 6]));
        assert(isequal(diag(A, -1), [4; 8]));
        assert(isequal(size(diag([1 2 3 4; 5 6 7 8])), [2 1]));
        assert(isequal(size(diag([1 2; 3 4; 5 6])), [2 1]));
        """);

    [Fact]
    public Task ADiagonalPastTheCornerIsTheEmptyColumn() => Asserts("""
        assert(isequal(size(diag([1 2; 3 4], 5)), [0 1]));
        assert(isequal(size(diag([1 2; 3 4], -5)), [0 1]));
        assert(isequal(size(diag([1 2; 3 4], 2)), [0 1]));
        assert(isequal(size(diag([])), [0 0]));
        assert(isequal(size(diag(zeros(2, 0))), [0 1]));
        """);

    [Fact]
    public Task AColumnBuildsADiagonalMatrixJustAsARowDoes() => Asserts("""
        assert(isequal(diag([1; 2; 3]), [1 0 0; 0 2 0; 0 0 3]));
        assert(isequal(diag([1 2 3]), diag([1; 2; 3])));
        assert(isequal(diag([1; 2], 1), [0 1 0; 0 0 2; 0 0 0]));
        assert(isequal(size(diag(5)), [1 1]));
        """);

    [Fact]
    public Task RoundTrippingADiagonalThroughItselfGivesTheMatrixBack() => Asserts("""
        A = [1 2; 3 4];
        assert(isequal(tril(A) + triu(A) - diag(diag(A)), A));
        assert(isequal(diag(diag(A)), [1 0; 0 4]));
        """);

    [Fact]
    public Task ADiagonalOfAComplexMatrixIsReadRatherThanRefused() => Asserts("""
        Z = [1+1i 2; 3 4-2i];
        assert(isequal(diag(Z), [1+1i; 4-2i]));
        assert(isequal(size(diag(Z)), [2 1]));
        assert(isequal(diag([1+1i 2i]), [1+1i 0; 0 2i]));
        assert(isequal(size(diag([1+1i 2i])), [2 2]));
        assert(isequal(diag(diag(Z)), [1+1i 0; 0 4-2i]));
        """);

    // --- eig ------------------------------------------------------------------------------------

    [Fact]
    public Task OneOutputOfEigIsAColumn() => Asserts("""
        assert(isequal(size(eig([1 2; 3 4])), [2 1]));
        assert(isequal(size(eig([2 0; 0 3])), [2 1]));
        assert(isequal(eig([2 0; 0 3]), [2; 3]));
        assert(isequal(size(eig([1i 1; 0 2i])), [2 1]));
        assert(isequal(size(eig(5)), [1 1]));
        """);

    [Fact]
    public Task TheWordChoosesTheShapeAndTheVectorIsStillAColumn() => Asserts("""
        A = [1 2; 3 4];
        assert(isequal(size(eig(A, 'vector')), [2 1]));
        assert(isequal(size(eig(A, 'matrix')), [2 2]));
        [V, D] = eig(A);
        assert(isequal(size(D), [2 2]));
        [V2, D2] = eig(A, 'vector');
        assert(isequal(size(D2), [2 1]));
        assert(isequal(size(V2), [2 2]));
        """);

    [Fact]
    public Task APencilAnswersInTheSameShapeASingleMatrixDoes() => Asserts("""
        S = [2 1; 1 3]; Q = [4 1; 1 2];
        assert(isequal(size(eig(S, Q)), [2 1]));
        assert(isequal(size(eig([1 2; 3 4], [2 0; 1 3], 'qz')), [2 1]));
        [V, D] = eig(S, Q, 'vector');
        assert(isequal(size(D), [2 1]));
        assert(isequal(size(eig(S, Q, 'matrix')), [2 2]));
        """);

    [Fact]
    public Task EigMinusAColumnIsAColumnRatherThanAnOuterDifference() => Asserts("""
        % The whole point of the orientation: this subtraction used to broadcast into a 2-by-2.
        e = eig([2 0; 0 3]);
        assert(isequal(size(e - [1; 1]), [2 1]));
        assert(isequal(e - [1; 1], [1; 2]));
        d = diag([1 2; 3 4]);
        assert(isequal(size(d - [1; 1]), [2 1]));
        """);

    // --- an eigenvalue at infinity --------------------------------------------------------------

    [Fact]
    public Task AnInfiniteEigenvalueOfAPencilKeepsTheSignOfItsNumerator() => Asserts("""
        assert(isequal(eig([2 0; 0 3], [1 0; 0 0]), [2; Inf]));
        assert(isequal(eig([-2 0; 0 3], [1 0; 0 0]), [-2; Inf]));
        assert(isequal(eig([-2 0; 0 -3], [0 0; 0 1]), [-Inf; -3]));
        assert(isequal(sort(eig([1 2; 3 4], zeros(2))), [-Inf; Inf]));
        assert(isequal(eig(-1, 0), -Inf));
        assert(isequal(eig(1, 0), Inf));
        """);

    [Fact]
    public Task APencilWithNoDirectionAtAllAnswersNotANumber() => Asserts("""
        assert(all(isnan(eig(zeros(2), zeros(2)))));
        assert(isnan(eig(0, 0)));
        """);

    [Fact]
    public Task PolyeigCarriesTheSignThroughToItsOwnSpectrum() => Asserts("""
        % MATLAB answers Inf -Inf Inf Inf for this quadratic; the order the QZ hands the pair back
        % in is the solver's, so what is pinned is that one of the four infinities is negative.
        e = polyeig(eye(2), [0 1; 1 0], [1 0; 0 0]);
        assert(isequal(size(e), [4 1]));
        assert(sum(e == Inf) == 3);
        assert(sum(e == -Inf) == 1);
        """);

    [Fact]
    public Task OrdeigAnswersTheSameColumnEigDoes() => Asserts("""
        % Same family, same shape: ordeig differs from eig in the ORDER it reports the values in,
        % which is what makes it a selection for ordschur, and in nothing else.
        T = [1 5 9; 0 2 6; 0 0 3];
        assert(isequal(ordeig(T), [1; 2; 3]));
        assert(isequal(size(ordeig(T)), [3 1]));
        A = [-3 1 0 2; 0 -1 4 1; 1 0 5 -2; 0 2 1 7];
        [U, S] = schur(A);
        e = ordeig(S);
        assert(isequal(size(e), [4 1]));
        [US, TS] = ordschur(U, S, real(e) < 0);
        assert(norm(US * TS * US' - A) < 1e-9);
        f = ordeig(TS);
        assert(real(f(1)) < 0);
        """);

    // --- norm -----------------------------------------------------------------------------------

    [Fact]
    public Task NormOfAComplexVectorReadsItsMagnitudes() => Asserts("""
        v = [1+1i 2 3-4i];
        assert(abs(norm(v) - 5.5677643628300215) < 1e-14);
        assert(abs(norm(v, 1) - 8.4142135623730958) < 1e-14);
        assert(abs(norm(v, Inf) - 5) < 1e-14);
        assert(abs(norm(v, -Inf) - 1.4142135623730951) < 1e-14);
        assert(abs(norm(v, 3) - 5.1403997115908755) < 1e-14);
        assert(abs(norm(v, 'fro') - 5.5677643628300215) < 1e-14);
        assert(abs(norm(3+4i) - 5) < 1e-14);
        """);

    [Fact]
    public Task NormOfAComplexMatrixIsItsLargestSingularValue() => Asserts("""
        Z = [1+1i 2; 3 4-2i];
        assert(abs(norm(Z) - 5.9063811806142104) < 1e-13);
        assert(abs(norm(Z, 2) - 5.9063811806142104) < 1e-13);
        assert(abs(norm(Z, 1) - 6.4721359549995796) < 1e-13);
        assert(abs(norm(Z, Inf) - 7.4721359549995796) < 1e-13);
        assert(abs(norm(Z, 'fro') - 5.9160797830996161) < 1e-13);
        assert(abs(norm([1+1i; 2]) - 2.44948974278318) < 1e-13);
        """);

    // --- tril and triu --------------------------------------------------------------------------

    [Fact]
    public Task TheTrianglesOfAComplexMatrixAreSelectedNotRefused() => Asserts("""
        Z = [1+1i 2; 3 4-2i];
        assert(isequal(tril(Z), [1+1i 0; 3 4-2i]));
        assert(isequal(tril(Z, -1), [0 0; 3 0]));
        assert(isequal(triu(Z), [1+1i 2; 0 4-2i]));
        assert(isequal(triu(Z, 1), [0 2; 0 0]));
        assert(isequal(tril(Z) + triu(Z) - diag(diag(Z)), Z));
        """);

    [Fact]
    public Task AComplexTriangleKeepsTheShapeItWasHanded() => Asserts("""
        W = [1+1i 2 3; 4 5-1i 6];
        assert(isequal(tril(W), [1+1i 0 0; 4 5-1i 0]));
        assert(isequal(triu(W, 1), [0 2 3; 0 0 6]));
        assert(isequal(size(tril(W)), [2 3]));
        assert(isequal(tril([1+1i 2+2i]), [1+1i 0]));
        assert(isequal(tril([1+1i; 2+2i]), [1+1i; 2+2i]));
        assert(isequal(tril(3+4i), 3+4i));
        """);

    // --- qr -------------------------------------------------------------------------------------

    [Fact]
    public Task TheEconomyZeroAsksForThePermutationAsAVector() => Asserts("""
        A = [1 2; 3 4; 5 6];
        [Q, R, p] = qr(A, 0);
        assert(isequal(size(p), [1 2]));
        assert(isequal(p, [2 1]));
        assert(isequal(size(Q), [3 2]));
        assert(isequal(size(R), [2 2]));
        assert(norm(A(:, p) - Q * R) < 1e-12);
        """);

    [Fact]
    public Task TheOtherQrFormsKeepThePermutationTheyAlwaysHad() => Asserts("""
        A = [1 2; 3 4; 5 6];
        [~, ~, P1] = qr(A);
        assert(isequal(size(P1), [2 2]));
        assert(isequal(P1, [0 1; 1 0]));
        [~, ~, p2] = qr(A, 'vector');
        assert(isequal(size(p2), [1 2]));
        [~, ~, P3] = qr(A, 'matrix');
        assert(isequal(size(P3), [2 2]));
        [~, ~, P4] = qr(A, 'econ');
        assert(isequal(size(P4), [2 2]));
        """);

    [Fact]
    public Task TheEconomyZeroPermutationFollowsTheWidthOfTheMatrix() => Asserts("""
        [~, ~, p] = qr([1 2 3; 4 5 6], 0);
        assert(isequal(size(p), [1 3]));
        assert(isequal(p, [3 1 2]));
        [~, ~, q] = qr([1; 2; 3], 0);
        assert(isequal(size(q), [1 1]));
        assert(isequal(q, 1));
        """);
}
