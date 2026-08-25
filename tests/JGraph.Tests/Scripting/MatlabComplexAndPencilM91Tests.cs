using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M91: the script-visible face of the z-routines and the pencil solvers. Complex <c>\</c> and
/// <c>/</c> exist now where they were refused, <c>[V, D] = eig(A)</c> and <c>[U, S, V] = svd(A)</c>
/// answer for a complex A, and <c>schur</c>/<c>qz</c>/<c>ordschur</c>/<c>eig(A, B)</c> ride the
/// provider. The assertions are residuals and shapes, not element values, because eigenvector
/// phase and Schur-block order are conventions the two backends need not share.
/// </summary>
[Collection("JG facade")]
public class MatlabComplexAndPencilM91Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabComplexAndPencilM91Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private void RunAsserting(string code) => RunAndRead(code);

    private string RunExpectingFailure(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.False(result.Success, "the script was expected to fail");
        return result.Message + _output.ErrorText;
    }

    // --- complex \ and / ------------------------------------------------------------------------

    [Fact]
    public void AComplexSquareSystemSolves()
    {
        RunAsserting("""
            A = [2+1i, 1; 0, 3-2i];
            b = [1; 1i];
            x = A\b;
            assert(max(abs(A*x - b)) < 1e-12);
            """);
    }

    [Fact]
    public void AComplexRightDivisionSolvesTheTransposedProblem()
    {
        RunAsserting("""
            A = [2+1i, 1; -1i, 3];
            y = [1, 2i] / A;
            assert(max(abs(y*A - [1, 2i])) < 1e-12);
            assert(isequal(size(y), [1 2]));
            """);
    }

    [Fact]
    public void AComplexVectorRightHandSideReadsAsAColumn()
    {
        RunAsserting("""
            A = [1, 1i; -1i, 2];
            b = [1+1i, 2];
            x = A\b;
            assert(isequal(size(x), [2 1]));
            assert(max(abs(A*x - [1+1i; 2])) < 1e-12);
            """);
    }

    [Fact]
    public void ASingularComplexSystemIsRefusedWithTheSingularMessage()
    {
        string message = RunExpectingFailure("""
            A = [1+1i, 2+2i; 2+2i, 4+4i];
            x = A \ [1; 2];
            """);
        Assert.Contains("singular", message);
    }

    [Fact]
    public void ARectangularComplexSystemNamesTheMissingSolver()
    {
        string message = RunExpectingFailure("""
            A = [1+1i; 2; 3];
            x = A \ [1; 2; 3];
            """);
        Assert.Contains("least-squares", message);
    }

    // --- complex eig ----------------------------------------------------------------------------

    [Fact]
    public void AComplexMatrixAnswersEigenvectorsNow()
    {
        RunAsserting("""
            A = [1+1i, 2; -1, 3i];
            [V, D] = eig(A);
            assert(isequal(size(V), [2 2]));
            assert(isequal(size(D), [2 2]));
            assert(max(max(abs(A*V - V*D))) < 1e-10);
            """);
    }

    [Fact]
    public void TheComplexVectorWordStillShapesTheValues()
    {
        RunAsserting("""
            A = [1+1i, 2; -1, 3i];
            [V, D] = eig(A, 'vector');
            assert(numel(D) == 2);
            assert(max(abs(A*V(:,1) - D(1)*V(:,1))) < 1e-10);
            assert(max(abs(A*V(:,2) - D(2)*V(:,2))) < 1e-10);
            """);
    }

    [Fact]
    public void TheComplexLeftEigenvectorsDiagonalizeFromTheLeft()
    {
        RunAsserting("""
            A = [1+1i, 2; -1, 3i];
            [V, D, W] = eig(A);
            assert(max(max(abs(W'*A - D*W'))) < 1e-8);
            """);
    }

    [Fact]
    public void TheSingleOutputComplexEigIsStillAColumn()
    {
        RunAsserting("""
            e = eig([1i, 1; 0, 2i]);
            assert(isequal(size(e), [2 1]));
            assert(max(abs(sort(imag(e)) - [1; 2])) < 1e-12);
            """);
    }

    // --- complex svd ----------------------------------------------------------------------------

    [Fact]
    public void TheComplexSvdIsFullSizedTheWayMatlabsIs()
    {
        RunAsserting("""
            Z = [1+1i, 2; 3-1i, 4i; 0, 1];
            [U, S, V] = svd(Z);
            assert(isequal(size(U), [3 3]));
            assert(isequal(size(S), [3 2]));
            assert(isequal(size(V), [2 2]));
            assert(max(max(abs(U*S*V' - Z))) < 1e-10);
            assert(max(max(abs(U'*U - eye(3)))) < 1e-10);
            assert(max(max(abs(V'*V - eye(2)))) < 1e-10);
            """);
    }

    [Fact]
    public void TheComplexEconomySvdCutsTheFactorsBack()
    {
        RunAsserting("""
            Z = [1+1i, 2; 3-1i, 4i; 0, 1];
            [U, S, V] = svd(Z, 'econ');
            assert(isequal(size(U), [3 2]));
            assert(isequal(size(S), [2 2]));
            assert(isequal(size(V), [2 2]));
            assert(max(max(abs(U*S*V' - Z))) < 1e-10);
            """);
    }

    [Fact]
    public void TheComplexSingularValuesStayAColumnAndAgreeWithTheFactors()
    {
        RunAsserting("""
            Z = [1+1i, 2; 3-1i, 4i];
            s = svd(Z);
            [~, S, ~] = svd(Z);
            assert(isequal(size(s), [2 1]));
            d = diag(S);
            assert(max(abs(s(:) - d(:))) < 1e-10);
            """);
    }

    // --- complex det / inv / product ------------------------------------------------------------

    [Fact]
    public void TheComplexDeterminantAndInverseStillAgree()
    {
        RunAsserting("""
            A = [2+1i, 1; 1i, 3];
            d = det(A);
            assert(abs(d - ((2+1i)*3 - 1i)) < 1e-12);
            B = inv(A);
            assert(max(max(abs(A*B - eye(2)))) < 1e-12);
            """);
    }

    // --- schur / ordschur / qz ------------------------------------------------------------------

    [Fact]
    public void TheSchurFormStillReassemblesItsMatrix()
    {
        RunAsserting("""
            M = [3 1 0; -1 2 1; 0 0 1];
            [U, T] = schur(M);
            assert(max(max(abs(U*T*U' - M))) < 1e-9);
            assert(max(max(abs(U'*U - eye(3)))) < 1e-10);
            assert(max(max(abs(tril(T, -2)))) < 1e-12);
            """);
    }

    [Fact]
    public void ReorderingTheSchurFormMovesTheChosenEigenvalueUp()
    {
        RunAsserting("""
            M = [4 1 0; 0 3 1; 0 0 1];
            [U, T] = schur(M);
            picks = abs(ordeig(T) - 1) < 1e-9;
            [US, TS] = ordschur(U, T, picks);
            assert(abs(TS(1,1) - 1) < 1e-9);
            assert(max(max(abs(US*TS*US' - M))) < 1e-9);
            """);
    }

    [Fact]
    public void TheQzFactorizationKeepsItsMatlabConvention()
    {
        RunAsserting("""
            A = [1 2; 3 4];
            B = [2 0; 1 3];
            [AA, BB, Q, Z] = qz(A, B);
            assert(max(max(abs(Q*A*Z - AA))) < 1e-9);
            assert(max(max(abs(Q*B*Z - BB))) < 1e-9);
            assert(max(max(abs(tril(BB, -1)))) < 1e-10);
            """);
    }

    // --- eig(A, B) ------------------------------------------------------------------------------

    [Fact]
    public void ThePencilEigenpairsSatisfyThePencil()
    {
        RunAsserting("""
            A = [1 2; 3 4];
            B = [2 0; 1 3];
            [V, D] = eig(A, B);
            assert(max(max(abs(A*V - B*V*D))) < 1e-9);
            """);
    }

    [Fact]
    public void TheDefinitePencilIsBNormalizedAndAscending()
    {
        RunAsserting("""
            A = [4 1; 1 3];
            B = [2 0; 0 1];
            [V, D] = eig(A, B);
            e = diag(D);
            assert(e(1) <= e(2));
            assert(max(max(abs(V'*B*V - eye(2)))) < 1e-9);
            assert(max(max(abs(A*V - B*V*D))) < 1e-9);
            """);
    }

    [Fact]
    public void ASingularBStillAnswersInfiniteEigenvaluesButRefusesVectors()
    {
        RunAsserting("""
            e = eig([1 0; 0 1], [1 0; 0 0]);
            assert(any(isinf(e)));
            """);

        string message = RunExpectingFailure("""
            [V, D] = eig([1 0; 0 1], [1 0; 0 0]);
            """);
        Assert.Contains("nonsingular", message);
    }

    // --- expm / logm / sqrtm --------------------------------------------------------------------

    [Fact]
    public void TheMatrixFunctionsRoundTrip()
    {
        RunAsserting("""
            X = [0.1 0.2; 0 0.3];
            assert(max(max(abs(logm(expm(X)) - X))) < 1e-8);
            S = sqrtm([4 1; 0 9]);
            assert(max(max(abs(S*S - [4 1; 0 9]))) < 1e-9);
            """);
    }
}
