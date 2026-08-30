using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M90: the script-visible face of the provider-backed orthogonal factorizations. <c>qr</c>,
/// <c>svd</c>, <c>eig</c>, <c>rank</c>, <c>norm(A, 2)</c>, <c>cond</c>, <c>null</c>, <c>orth</c> and
/// <c>pinv</c> now reach LAPACK through flat column-major storage; their shapes, orientations and
/// multi-output forms are asserted here — including the three that changed on purpose, because
/// LAPACK's conventions are MATLAB's and JGraph's were not: <c>[U, S, V] = svd(A)</c> is full-sized,
/// <c>s = svd(A)</c> is a column, and <c>qr</c> leaves an already-triangular column alone.
/// </summary>
[Collection("JG facade")]
public class MatlabSpectralProviderM90Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSpectralProviderM90Tests() => JG.Reset();

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

    // --- svd ------------------------------------------------------------------------------------

    [Fact]
    public void TheThreeOutputSvdIsFullSizedTheWayMatlabsIs()
    {
        // MATLAB's [U, S, V] = svd(A) is U m-by-m, S m-by-n and V n-by-n. JGraph answered the
        // economy factors until M90, which was a real difference and not a storage detail: U's extra
        // columns span the orthogonal complement of A's range and were simply absent.
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            [U, S, V] = svd(A);
            assert(isequal(size(U), [3 3]), 'U is square');
            assert(isequal(size(S), [3 2]), 'S has the shape of A');
            assert(isequal(size(V), [2 2]), 'V is square');
            assert(norm(U * S * V' - A, 'fro') < 1e-12);
            assert(norm(U' * U - eye(3), 'fro') < 1e-12);
            assert(norm(V' * V - eye(2), 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void TheEconomyFormCutsEveryFactorBackToTheSmallerDimension()
    {
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            [U, S, V] = svd(A, 'econ');
            assert(isequal(size(U), [3 2]));
            assert(isequal(size(S), [2 2]));
            assert(isequal(size(V), [2 2]));
            assert(norm(U * S * V' - A, 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void TheZeroFlagEconomizesATallMatrixAndLeavesAnyOtherShapeAlone()
    {
        // MATLAB's older spelling: svd(A, 0) is the economy decomposition for m > n and the full one
        // for every other shape, which is not the same rule as 'econ'.
        RunAsserting("""
            [U, S, V] = svd([1 2; 3 4; 5 6], 0);
            assert(isequal(size(U), [3 2]), 'a tall matrix economizes');
            [U, S, V] = svd([1 2 3; 4 5 6], 0);
            assert(isequal(size(U), [2 2]) && isequal(size(S), [2 3]) && isequal(size(V), [3 3]), ...
                'a wide one does not');
            """);
    }

    [Fact]
    public void TheSingularValuesComeBackAsAColumn()
    {
        RunAsserting("""
            s = svd([1 2; 3 4; 5 6]);
            assert(isequal(size(s), [2 1]), 'MATLAB answers a column here, and so did the complex branch');
            assert(s(1) > s(2), 'descending');
            """);
    }

    [Fact]
    public void AWideMatrixDecomposesWithTheFactorsTheOtherWayRound()
    {
        RunAsserting("""
            A = [1 2 3; 4 5 6];
            [U, S, V] = svd(A);
            assert(isequal(size(U), [2 2]) && isequal(size(S), [2 3]) && isequal(size(V), [3 3]));
            assert(norm(U * S * V' - A, 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void TheOutputCountDecidesWhatComesBackAndTheBracketsDoNot()
    {
        // MATLAB reads this off nargout: [s] = svd(A) is the values, [U, S] = svd(A) is the first
        // two factors, and the brackets themselves say nothing.
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            [s] = svd(A);
            assert(isequal(size(s), [2 1]), 'one output is the values however it is spelled');
            [U, S] = svd(A);
            assert(isequal(size(U), [3 3]) && isequal(size(S), [3 2]));
            """);
    }

    [Fact]
    public void SvdRefusesAnUnknownOption() =>
        Assert.Contains("econ", RunExpectingFailure("svd([1 2; 3 4], 'thin');"));

    // --- rank, norm, cond -----------------------------------------------------------------------

    [Fact]
    public void RankCountsTheSingularValuesAboveTheDefaultTolerance()
    {
        RunAsserting("""
            assert(rank([1 2; 3 4]) == 2);
            assert(rank([1 2; 2 4]) == 1, 'a repeated row is one dimension');
            assert(rank(zeros(3)) == 0);
            assert(rank([1 2; 2 4.0000001], 1e-3) == 1, 'an explicit tolerance overrides the default');
            """);
    }

    [Fact]
    public void TheMatrixTwoNormIsTheLargestSingularValue()
    {
        RunAsserting("""
            A = [3 0; 0 4];
            assert(abs(norm(A, 2) - 4) < 1e-12);
            assert(abs(norm(A) - 4) < 1e-12, 'two is the default for a matrix');
            assert(abs(norm(A, 1) - 4) < 1e-12);
            assert(abs(norm([1 -2; 3 4], 1) - 6) < 1e-12);
            assert(abs(norm([1 -2; 3 4], inf) - 7) < 1e-12);
            assert(abs(norm([1 -2; 3 4], 'fro') - sqrt(30)) < 1e-12);
            """);
    }

    [Fact]
    public void ConditionNumbersAgreeAcrossTheirNorms()
    {
        RunAsserting("""
            A = [2 0; 0 1];
            assert(abs(cond(A) - 2) < 1e-12);
            assert(abs(cond(A, 2) - 2) < 1e-12);
            assert(abs(cond(A, 1) - 2) < 1e-12);
            assert(abs(cond(A, inf) - 2) < 1e-12);
            assert(abs(cond(A, 'fro') - sqrt(5) * sqrt(1.25)) < 1e-9);

            % A singular matrix is enormously conditioned, and whether that comes out as Inf depends
            % on the backend: MATLAB's own cond divides the largest singular value by the smallest
            % and only answers Inf when the smallest is exactly zero, which LAPACK does not always
            % give for an exactly rank-deficient matrix — it finds 1e-16 here where the managed
            % Jacobi finds a clean nought. Both mean the same thing, and this asserts that meaning.
            assert(cond([1 2; 2 4]) > 1e15);
            assert(isinf(cond([1 2; 2 4], 1)), 'the norm-based forms detect the singularity exactly');
            """);
    }

    // --- null, orth, pinv -----------------------------------------------------------------------

    [Fact]
    public void TheNullSpaceOfAWideMatrixIsAsWideAsItShouldBe()
    {
        // One equation in three unknowns leaves a two-dimensional null space. Before M90 the economy
        // V had only one column for this matrix, so there was nothing there to report and null
        // answered empty — the decomposition had never been asked for the columns that hold it.
        RunAsserting("""
            N = null([1 2 3]);
            assert(isequal(size(N), [3 2]), 'two dimensions, not none');
            assert(norm([1 2 3] * N) < 1e-12, 'and every one of them is in the null space');
            assert(norm(N' * N - eye(2), 'fro') < 1e-12, 'orthonormal');
            """);
    }

    [Fact]
    public void TheRangeAndTheNullSpacePartitionTheDimensions()
    {
        RunAsserting("""
            A = [1 2 3; 4 5 6; 7 8 9];
            assert(size(orth(A), 2) == rank(A));
            assert(size(null(A), 2) == 3 - rank(A));
            assert(norm(orth(A)' * orth(A) - eye(rank(A)), 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void ThePseudoInverseSatisfiesTheMoorePenroseConditions()
    {
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            X = pinv(A);
            assert(isequal(size(X), [2 3]), 'the shape is the transpose of A');
            assert(norm(A * X * A - A, 'fro') < 1e-9);
            assert(norm(X * A * X - X, 'fro') < 1e-9);
            assert(norm((A * X)' - A * X, 'fro') < 1e-9);
            assert(norm((X * A)' - X * A, 'fro') < 1e-9);
            """);
    }

    [Fact]
    public void ThePseudoInverseOfARankDeficientMatrixDropsTheNegligibleDirections()
    {
        RunAsserting("""
            A = [1 2; 2 4];
            X = pinv(A);
            assert(norm(A * X * A - A, 'fro') < 1e-9);
            assert(abs(rank(X) - 1) < 1e-12, 'the inverse is as deficient as the matrix');
            """);
    }

    // --- qr ---------------------------------------------------------------------------------

    [Fact]
    public void EveryQrFormReassemblesTheMatrix()
    {
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            [Q, R] = qr(A);
            assert(isequal(size(Q), [3 3]) && isequal(size(R), [3 2]));
            assert(norm(Q * R - A, 'fro') < 1e-12);
            assert(norm(Q' * Q - eye(3), 'fro') < 1e-12);

            [Q, R] = qr(A, 0);
            assert(isequal(size(Q), [3 2]) && isequal(size(R), [2 2]));
            assert(norm(Q * R - A, 'fro') < 1e-12);

            [Q, R, P] = qr(A);
            assert(isequal(size(P), [2 2]));
            assert(norm(A * P - Q * R, 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void ThePairFormAppliesTheTransposeWithoutFormingTheFactor()
    {
        RunAsserting("""
            A = [1 2; 3 4; 5 6];
            b = [1; 0; 2];
            [C, R] = qr(A, b);
            assert(isequal(size(C), [3 1]) && isequal(size(R), [3 2]));

            % R \ C is the least-squares solution, which is what the form is for.
            [Qe, Re] = qr(A, 0);
            x = Re \ (Qe' * b);
            assert(norm(R(1:2, :) \ C(1:2) - x) < 1e-9);
            """);
    }

    [Fact]
    public void PivotingReportsItsOrderAsEitherAMatrixOrAVector()
    {
        RunAsserting("""
            A = [1 100; 2 200; 3 1];
            [Q, R, p] = qr(A, 'vector');
            assert(isequal(size(p), [1 2]));
            assert(norm(A(:, p) - Q * R, 'fro') < 1e-12);

            [Q, R, P] = qr(A, 'matrix');
            assert(isequal(size(P), [2 2]));
            assert(norm(A * P - Q * R, 'fro') < 1e-12);
            assert(abs(R(1, 1)) >= abs(R(2, 2)), 'pivoting orders the diagonal');
            """);
    }

    [Fact]
    public void AnAlreadyTriangularMatrixIsLeftAloneRatherThanNegated()
    {
        // LAPACK's reflector is the identity for a column already zero below the diagonal, so qr of
        // a triangular matrix returns it unchanged. JGraph's own kernel reflected every column
        // whether it needed it or not and answered −I here, which no MATLAB ever did.
        RunAsserting("""
            assert(max(max(abs(qr(eye(3)) - eye(3)))) == 0);
            R = qr([2 1; 0 3]);
            assert(max(max(abs(R - [2 1; 0 3]))) < 1e-12);
            """);
    }

    [Fact]
    public void QrRefusesAnUnknownOption() =>
        Assert.Contains("econ", RunExpectingFailure("qr([1 2; 3 4], 'thin');"));

    // --- eig ------------------------------------------------------------------------------------

    [Fact]
    public void ASymmetricSpectrumIsRealAndAscending()
    {
        RunAsserting("""
            S = [4 1; 1 3];
            e = eig(S);
            assert(e(1) < e(2), 'ascending, which is MATLAB''s symmetric order');
            assert(all(imag(e) == 0));
            [V, D] = eig(S);
            assert(norm(S * V - V * D, 'fro') < 1e-12);
            assert(norm(V' * V - eye(2), 'fro') < 1e-12, 'a symmetric matrix has orthonormal vectors');
            """);
    }

    [Fact]
    public void AConjugatePairComesBackPairedWithItsVectors()
    {
        RunAsserting("""
            A = [0 -1; 1 0];
            [V, D] = eig(A);
            assert(abs(real(D(1, 1))) < 1e-12 && abs(abs(imag(D(1, 1))) - 1) < 1e-12);
            assert(abs(D(1, 1) + D(2, 2)) < 1e-12, 'the pair sums to the trace, which is zero');
            assert(norm(A * V - V * D, 'fro') < 1e-12);
            """);
    }

    [Fact]
    public void TheThirdOutputIsTheLeftEigenvectors()
    {
        RunAsserting("""
            A = [1 2; 3 4];
            [V, D, W] = eig(A);
            assert(norm(A * V - V * D, 'fro') < 1e-9);
            assert(norm(W' * A - D * W', 'fro') < 1e-9);
            """);
    }

    [Fact]
    public void EveryEigenvectorHasUnitLength()
    {
        RunAsserting("""
            A = [1 2 0; 0 3 1; 4 0 5];
            [V, ~] = eig(A);
            for k = 1:3
                % Spelled out rather than through norm, which is real-only: a general matrix's
                % eigenvectors are complex whenever its eigenvalues are.
                assert(abs(sqrt(sum(abs(V(:, k)) .^ 2)) - 1) < 1e-9);
            end
            """);
    }

    [Fact]
    public void OneOutputStillGivesTheSameValuesAsTwo()
    {
        // The one-output form skips the eigenvectors entirely since M90; it must not skip any of the
        // answer with them.
        RunAsserting("""
            A = [1 2 0; 0 3 1; 4 0 5];
            e = eig(A);
            [~, D] = eig(A);
            paired = zeros(1, 3);
            for k = 1:3
                paired(k) = abs(e(k) - D(k, k));
            end
            assert(max(paired) < 1e-12, 'the same values in the same order');
            """);
    }

    [Fact]
    public void ABadlyScaledMatrixKeepsItsEigenvalues()
    {
        RunAsserting("""
            A = [1 1e6 0; 1e-6 1 1e-6; 0 1e6 1];
            exact = sort([1 - sqrt(2); 1; 1 + sqrt(2)]);
            assert(max(abs(sort(real(eig(A))) - exact)) < 1e-13, 'eig balances before it iterates');
            """);
    }

    // --- the two backends -----------------------------------------------------------------------

    [Fact]
    public void WhicheverBackendIsLiveAnswersTheSameProperties()
    {
        // The provider is chosen per process by JGRAPH_LINALG, and both CI lanes run this file. The
        // assertions are properties rather than values because a blocked LAPACK factorization and a
        // hand-rolled one agree on the answer, not on the sign of any one column of it.
        Assert.NotEmpty(LinalgProvider.StatusReport);
        RunAsserting("""
            A = [4 1 2; 1 5 3; 2 3 6];
            [V, D] = eig(A);
            assert(norm(A * V - V * D, 'fro') < 1e-9);
            [U, S, W] = svd(A);
            assert(norm(U * S * W' - A, 'fro') < 1e-9);
            [Q, R] = qr(A);
            assert(norm(Q * R - A, 'fro') < 1e-9);
            assert(rank(A) == 3);
            assert(abs(norm(A, 2) - max(svd(A))) < 1e-9);
            """);
    }
}
