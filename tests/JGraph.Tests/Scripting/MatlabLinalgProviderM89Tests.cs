using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M89: the script-visible face of the provider-backed factorizations. <c>\</c>, <c>/</c>,
/// <c>inv</c>, <c>det</c>, <c>lu</c>, <c>chol</c>, <c>rcond</c> and <c>linsolve</c> now reach LAPACK
/// through flat column-major storage instead of rectangles of boxed elements; the shapes, the
/// orientations, the error texts and the multi-output forms must be exactly what they were.
/// </summary>
[Collection("JG facade")]
public class MatlabLinalgProviderM89Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabLinalgProviderM89Tests() => JG.Reset();

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

    // --- the solve operators ------------------------------------------------------------------

    [Fact]
    public void SquareSolveReproducesTheRightHandSide()
    {
        RunAsserting("""
            A = [4 1 0; 1 3 1; 0 1 2];
            b = [1; 2; 3];
            x = A \ b;
            assert(isequal(size(x), [3 1]), 'a column in, a column out');
            assert(max(abs(A * x - b)) < 1e-12);
            """);
    }

    [Fact]
    public void SolveAcceptsAVectorRightHandSideEitherWayUp()
    {
        RunAsserting("""
            A = [2 0; 0 4];
            assert(max(abs(A \ [2 8] - [1; 2])) < 1e-12, 'a row rhs is read as a column');
            assert(max(abs(A \ [2; 8] - [1; 2])) < 1e-12);
            """);
    }

    [Fact]
    public void SolveWithSeveralRightHandSidesKeepsItsShape()
    {
        RunAsserting("""
            A = [4 1; 1 3];
            B = [1 0; 0 1];
            X = A \ B;
            assert(isequal(size(X), [2 2]));
            assert(max(max(abs(A * X - B))) < 1e-12);
            """);
    }

    [Fact]
    public void TallSolveIsTheLeastSquaresFit()
    {
        RunAsserting("""
            X = [ones(4,1) (0:3)'];
            y = [1; 3; 5; 7.4];
            c = X \ y;
            residual = y - X * c;
            assert(isequal(size(c), [2 1]));
            assert(max(abs(X' * residual)) < 1e-10, 'the residual is orthogonal to the design');
            """);
    }

    [Fact]
    public void WideSolveIsTheMinimumNormAnswer()
    {
        RunAsserting("""
            A = [1 1 1; 1 2 3];
            b = [3; 6];
            x = A \ b;
            assert(isequal(size(x), [3 1]));
            assert(max(abs(A * x - b)) < 1e-12, 'it is a solution');
            assert(max(abs(x - A' * ((A * A') \ b))) < 1e-10, 'and the shortest one');
            """);
    }

    [Fact]
    public void RightDivisionSolvesTheOtherSide()
    {
        RunAsserting("""
            X = [1 2; 3 4];
            B = [4 1; 1 3];
            A = X * B;
            Y = A / B;
            assert(isequal(size(Y), [2 2]));
            assert(max(max(abs(Y - X))) < 1e-12);
            """);
    }

    [Fact]
    public void SolveRefusesAMismatchedRightHandSide()
    {
        string message = RunExpectingFailure("[1 2; 3 4] \\ [1; 2; 3];");
        Assert.Contains("as many rows as the matrix", message, StringComparison.Ordinal);
    }

    [Fact]
    public void SolveRefusesASingularMatrix()
    {
        string message = RunExpectingFailure("[1 2; 2 4] \\ [1; 2];");
        Assert.Contains("singular", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarDivisionStillCollapsesToANumber()
    {
        string output = RunAndRead("""
            q = 6 \ 3;
            fprintf('%g %d\n', q, isscalar(q));
            """);

        Assert.Contains("0.5 1", output, StringComparison.Ordinal);
    }

    // --- inv, det, rcond ----------------------------------------------------------------------

    [Fact]
    public void InverseMatchesTheKnownAnswer()
    {
        RunAsserting("""
            A = [4 7; 2 6];
            Ai = inv(A);
            assert(max(max(abs(Ai - [0.6 -0.7; -0.2 0.4]))) < 1e-12);
            assert(max(max(abs(A * Ai - eye(2)))) < 1e-12);
            """);
    }

    [Fact]
    public void InverseRefusesASingularMatrix()
    {
        string message = RunExpectingFailure("inv([1 2; 2 4]);");
        Assert.Contains("singular", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterminantIsExactForSmallIntegerMatrices()
    {
        string output = RunAndRead("""
            fprintf('%g %g %g\n', det([1 2; 3 4]), det(magic(3)), det(eye(4)));
            """);

        Assert.Contains("-2 -360 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionEstimateAgreesWithLinsolvesSecondOutput()
    {
        RunAsserting("""
            A = [4 1; 1 3];
            b = [1; 2];
            [x, r] = linsolve(A, b);
            assert(max(abs(A * x - b)) < 1e-12);
            assert(abs(r - rcond(A)) < 1e-12, 'linsolve reports exactly rcond');
            assert(abs(rcond(eye(3)) - 1) < 1e-14);
            assert(rcond([1 2; 2 4]) == 0, 'an exactly singular matrix has no condition to report');
            assert(rcond([1 2; 2 4.0001]) < 1e-4);
            """);
    }

    // --- lu -----------------------------------------------------------------------------------

    [Fact]
    public void LuFactorsReassembleThePermutedMatrix()
    {
        RunAsserting("""
            A = [0 5 2; 3 1 4; 1 2 6];
            [L, U, P] = lu(A);
            assert(isequal(size(L), [3 3]) && isequal(size(U), [3 3]) && isequal(size(P), [3 3]));
            assert(max(max(abs(P * A - L * U))) < 1e-12);
            assert(istril(L) && istriu(U));
            assert(max(abs(diag(L) - 1)) == 0, 'L has an exact unit diagonal');
            """);
    }

    [Fact]
    public void LuWithTwoOutputsFoldsThePermutationIntoTheLowerFactor()
    {
        RunAsserting("""
            A = [0 5 2; 3 1 4; 1 2 6];
            [L, U] = lu(A);
            assert(max(max(abs(L * U - A))) < 1e-12);
            assert(istriu(U));
            """);
    }

    [Fact]
    public void LuWithOneOutputHoldsBothFactorsAtOnce()
    {
        RunAsserting("""
            A = [0 5 2; 3 1 4; 1 2 6];
            Y = lu(A);
            [L, U] = lu(A);
            assert(max(max(abs(Y - (L + U - eye(3))))) < 1e-12);
            """);
    }

    [Fact]
    public void LuAnswersItsPermutationAsAVectorWhenAsked()
    {
        RunAsserting("""
            A = [0 5 2; 3 1 4; 1 2 6];
            [L, U, p] = lu(A, 'vector');
            [L2, U2, P] = lu(A);
            assert(isequal(size(p), [1 3]));
            assert(max(max(abs(A(p, :) - L * U))) < 1e-12, 'the vector says which rows P moves');
            assert(isequal(P * A, A(p, :)));
            assert(max(max(abs(L - L2))) == 0 && max(max(abs(U - U2))) == 0);
            """);
    }

    [Fact]
    public void LuKeepsItsFourAndFiveOutputForms()
    {
        RunAsserting("""
            A = [0 5 2; 3 1 4; 1 2 6];
            [L, U, P, Q] = lu(A);
            assert(isequal(Q, eye(3)));
            [L2, U2, P2, Q2, D] = lu(A);
            assert(isequal(D, eye(3)));
            assert(max(max(abs(P2 * A * Q2 - L2 * U2 * D))) < 1e-12);
            """);
    }

    // --- chol ---------------------------------------------------------------------------------

    [Fact]
    public void CholeskyFactorsBothWaysUp()
    {
        RunAsserting("""
            A = [4 2; 2 3];
            R = chol(A);
            L = chol(A, 'lower');
            assert(istriu(R) && istril(L));
            assert(max(max(abs(R' * R - A))) < 1e-12);
            assert(max(max(abs(L * L' - A))) < 1e-12);
            assert(max(max(abs(R - L'))) < 1e-12, 'the two directions answer the same question');
            """);
    }

    [Fact]
    public void CholeskyReadsTheTriangleItIsAskedFor()
    {
        // MATLAB's chol reads the upper triangle by default and the lower one on request, so a
        // matrix whose two triangles disagree gets two different — and both correct — answers.
        RunAsserting("""
            A = [4 2; 1 3];
            R = chol(A);
            L = chol(A, 'lower');
            assert(max(max(abs(R' * R - [4 2; 2 3]))) < 1e-12, 'upper reads the 2');
            assert(max(max(abs(L * L' - [4 1; 1 3]))) < 1e-12, 'lower reads the 1');
            """);
    }

    [Fact]
    public void CholeskyReportsWhereItStopped()
    {
        RunAsserting("""
            [R, flag] = chol([4 2 1; 2 3 1; 1 1 -5]);
            assert(flag == 3);
            assert(isequal(size(R), [2 2]));
            assert(max(max(abs(R' * R - [4 2; 2 3]))) < 1e-12);
            """);
    }

    [Fact]
    public void CholeskyRefusesAnIndefiniteMatrixWithOneOutput()
    {
        string message = RunExpectingFailure("chol([1 0; 0 -1]);");
        Assert.Contains("positive definite", message, StringComparison.Ordinal);
    }

    // --- linsolve's triangular promises -------------------------------------------------------

    [Fact]
    public void LinsolveHonoursItsTriangularFlags()
    {
        RunAsserting("""
            b = [1; 2];
            L = [2 0; 1 3];
            U = [2 1; 0 3];
            assert(max(abs(L * linsolve(L, b, struct('LT', true)) - b)) < 1e-12);
            assert(max(abs(U * linsolve(U, b, struct('UT', true)) - b)) < 1e-12);
            A = [4 1; 1 3];
            assert(max(abs(A' * linsolve(A, b, struct('TRANSA', true)) - b)) < 1e-12);
            """);
    }

    [Fact]
    public void LinsolveRefusesASingularTriangle()
    {
        string message = RunExpectingFailure("linsolve([1 5; 0 0], [1; 1], struct('UT', true));");
        Assert.Contains("zero on its diagonal", message, StringComparison.Ordinal);
    }

    // --- the provider itself ------------------------------------------------------------------

    [Fact]
    public void LapackVersionReportsTheLiveBackend()
    {
        // The backend that is *current*, not the one that is merely available: with
        // JGRAPH_LINALG=managed the library still loads and the managed kernels still run, and the
        // whole point of the status line is that it says which.
        string output = RunAndRead("fprintf('%s\\n', version('-lapack'));");
        Assert.Contains(LinalgProvider.StatusReport, output, StringComparison.Ordinal);
        Assert.Contains(LinalgProvider.Current.IsNative ? "OpenBLAS" : "managed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WhicheverBackendIsLiveAnswersTheSameProperties()
    {
        // The provider axis is checked by running the whole suite under each `JGRAPH_LINALG` value,
        // not by switching backends inside one — `LinalgProvider.Use` is process-wide, and a test
        // that flipped it would be reaching into every other test running beside it. What belongs
        // here is what must hold of *whichever* backend is live: the factorizations reassemble their
        // matrices, and rcond keeps its promise. `rcond` is where the two deliberately differ —
        // LAPACK estimates where the managed kernels compute exactly (ADR 0089) — so it is held to
        // the bound both satisfy rather than to a shared value.
        RunAsserting("""
            n = 40;
            A = zeros(n);
            for i = 1:n
                for j = 1:n
                    A(i, j) = sin(0.7*i) + cos(1.3*j) + (i == j) * 2 * n;
                end
            end
            b = cos((1:n)') + 1;
            S = A' * A + n * eye(n);
            assert(norm(A * (A \ b) - b) / norm(b) < 1e-12);
            [L, U, P] = lu(A);
            assert(norm(P * A - L * U, 'fro') / norm(A, 'fro') < 1e-12);
            assert(norm(inv(A) * A - eye(n), 'fro') < 1e-10);
            R = chol(S);
            assert(norm(R' * R - S, 'fro') / norm(S, 'fro') < 1e-12);
            r = rcond([4 1; 1 3]);
            assert(r > 0.4 && r < 0.5, 'the estimate never overstates the conditioning, nor by much');
            """);
    }
}
