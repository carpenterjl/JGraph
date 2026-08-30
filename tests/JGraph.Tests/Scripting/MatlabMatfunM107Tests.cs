using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The matrix-function leftovers (M107): the elimination, the plane rotation and the two
/// factorization updates over it, the two conversions between real and complex factorizations, the
/// eigenvalue conditioning, the three estimators, the Sylvester equation, the two least-squares
/// solvers, the polynomial eigenproblem, a general function of a matrix, the generalized singular
/// value decomposition, and the decomposition object.
/// </summary>
/// <remarks>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer rather than JGraph's
/// display format. Every number was read off MATLAB R2024a on this machine. Where a defining
/// property exists it is asserted instead of a value — a factorization reproducing its matrix, a
/// Sylvester solution satisfying its equation, a generalized decomposition rebuilding both of its
/// inputs — because those are the promises a caller actually relies on.
/// </remarks>
[Collection("JG facade")]
public class MatlabMatfunM107Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMatfunM107Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private void AssertRan(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    private async Task Asserts(string code)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, code));
    }

    private async Task Refuses(string code, string identifier)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, $"""
            caught = '';
            try
                {code}
            catch err
                caught = err.identifier;
            end
            assert(strcmp(caught, '{identifier}'), ['got: ' caught]);
            """));
    }

    // --- rref ------------------------------------------------------------------------------------

    [Fact]
    public Task TheEliminationAnswersTheTextbookMatrixExactly() => Asserts("""
        R = rref([1 2 3; 4 5 6; 7 8 9]);
        assert(isequal(R, [1 0 -1; 0 1 2; 0 0 0]));
        """);

    [Fact]
    public Task ARationalMatrixKeepsItsRatios() => Asserts("""
        % A third of a third of a third would otherwise come back as 0.33333333333333331.
        R = rref([1 2; 3 7]);
        assert(isequal(R, eye(2)));
        R2 = rref([0.5 0.25; 0.125 1]);
        assert(isequal(R2, eye(2)));
        R3 = rref([3 1 2; 6 2 5]);
        assert(isequal(R3, [1 1/3 0; 0 0 1]));
        """);

    [Fact]
    public Task ThePivotColumnsComeBackAsARow() => Asserts("""
        [R, jb] = rref([1 2 3; 4 5 6; 7 8 9]);
        assert(isequal(jb, [1 2]));
        assert(isequal(size(jb), [1 2]));
        [~, none] = rref(zeros(2, 3));
        assert(isempty(none));
        assert(isequal(size(none), [1 0]));
        """);

    [Fact]
    public Task ATolerantEliminationDeclaresASmallColumnNegligible() => Asserts("""
        assert(isequal(rref([1 2; 3 4], 10), [0 0; 0 0]));
        assert(isequal(rref([1 2; 1e-12 3], 1e-6), eye(2)));
        """);

    // --- planerot --------------------------------------------------------------------------------

    [Fact]
    public Task TheRotationPutsAColumnOnItsFirstAxis() => Asserts("""
        [G, y] = planerot([3; 4]);
        assert(isequal(G, [0.6 0.8; -0.8 0.6]));
        assert(isequal(y, [5; 0]));
        assert(norm(G * [3; 4] - y) < 1e-15);
        assert(norm(G' * G - eye(2)) < 1e-15);
        """);

    [Fact]
    public Task ARadiusIsTheLengthNormWouldHaveReported() => Asserts("""
        % .NET's own hypotenuse differs from a correctly rounded length in about an eighth of
        % pairs, and MATLAB's norm is correctly rounded, so this has to be exact and not close.
        for pair = [0.1 0.2; 7 24; 1 3; 0.3 0.7; 1e-200 1e-200]'
            [~, y] = planerot(pair);
            assert(y(1) == norm(pair));
        end
        """);

    [Fact]
    public Task AlreadyOnItsAxisTheRotationIsTheIdentity() => Asserts("""
        [G, y] = planerot([3; 0]);
        assert(isequal(G, eye(2)));
        assert(isequal(y, [3; 0]));
        [G0, y0] = planerot([0; 0]);
        assert(isequal(G0, eye(2)));
        assert(isequal(y0, [0; 0]));
        """);

    [Fact]
    public Task AComplexColumnRotatesOntoARealRadius() => Asserts("""
        [G, y] = planerot([3+1i; 4-2i]);
        assert(abs(y(1) - sqrt(30)) < 1e-15);
        assert(imag(y(1)) == 0);
        assert(y(2) == 0);
        assert(abs(real(G(1,1)) - 0.547722557505166) < 1e-15);
        assert(abs(imag(G(1,1)) + 0.182574185835055) < 1e-15);
        """);

    [Fact]
    public Task ARotationNeedsATwoElementColumn() =>
        Refuses("planerot([1 2]);", "MATLAB:planerot:InputSizeInvalid");

    // --- qrinsert / qrdelete ---------------------------------------------------------------------

    [Fact]
    public Task InsertingAColumnFactorsTheEnlargedMatrix() => Asserts("""
        A = [1 2; 3 4; 5 6; 7 8];
        [Q, R] = qr(A);
        x = [1; 0; 0; 1];
        [Q1, R1] = qrinsert(Q, R, 1, x);
        assert(norm(Q1 * R1 - [x A]) < 1e-14);
        assert(norm(Q1' * Q1 - eye(4)) < 1e-14);
        assert(norm(tril(R1, -1)) == 0);
        [Q2, R2] = qrinsert(Q, R, 3, x);
        assert(norm(Q2 * R2 - [A x]) < 1e-14);
        """);

    [Fact]
    public Task InsertingARowGrowsBothFactors() => Asserts("""
        A = [1 2; 3 4; 5 6; 7 8];
        [Q, R] = qr(A);
        [Q3, R3] = qrinsert(Q, R, 2, [9 10], 'row');
        assert(isequal(size(Q3), [5 5]));
        assert(isequal(size(R3), [5 2]));
        assert(norm(Q3 * R3 - [1 2; 9 10; 3 4; 5 6; 7 8]) < 1e-14);
        """);

    [Fact]
    public Task InsertingIntoNothingIsJustAFactorization() => Asserts("""
        [Q, R] = qrinsert(zeros(3, 0), zeros(0, 0), 1, [1; 2; 3]);
        [Qe, Re] = qr([1; 2; 3]);
        assert(norm(Q - Qe) < 1e-15);
        assert(norm(R - Re) < 1e-15);
        """);

    [Fact]
    public Task DeletingAColumnFactorsWhatIsLeft() => Asserts("""
        A = [1 2; 3 4; 5 6; 7 8];
        [Q, R] = qr(A);
        [Q4, R4] = qrdelete(Q, R, 1);
        assert(norm(Q4 * R4 - A(:, 2)) < 1e-14);
        assert(norm(Q4' * Q4 - eye(4)) < 1e-14);
        """);

    [Fact]
    public Task DeletingARowShrinksBothFactors() => Asserts("""
        A = [1 2; 3 4; 5 6; 7 8];
        [Q, R] = qr(A);
        [Q5, R5] = qrdelete(Q, R, 2, 'row');
        assert(isequal(size(Q5), [3 3]));
        assert(norm(Q5 * R5 - A([1 3 4], :)) < 1e-14);
        """);

    [Fact]
    public Task AnUpdateChecksItsIndexAndItsPiece() => Asserts("""
        A = [1 2; 3 4; 5 6; 7 8];
        [Q, R] = qr(A);
        ids = {};
        tries = {@() qrinsert(Q, R, 1), @() qrinsert(Q, R, 0, [1;0;0;1]), ...
                 @() qrinsert(Q, R, 9, [1;0;0;1]), @() qrinsert(Q, R, 1, [1;0;0;1], 'diag'), ...
                 @() qrdelete(Q, R, 5), @() qrdelete(Q(:,1:2), R(1:2,:), 1, 'row')};
        want = {'MATLAB:qrinsert:NotEnoughInputs', 'MATLAB:qrinsert:NegInsertionIndex', ...
                'MATLAB:qrinsert:InvalidInsertionIndex', 'MATLAB:qrinsert:InvalidInput5', ...
                'MATLAB:qrdelete:InvalidDelIndex', 'MATLAB:qrdelete:QNotSquare'};
        for k = 1:numel(tries)
            got = '';
            try
                tries{k}();
            catch err
                got = err.identifier;
            end
            assert(strcmp(got, want{k}), ['case ' num2str(k) ' gave ' got]);
        end
        """);

    // --- cdf2rdf / rsf2csf -----------------------------------------------------------------------

    [Fact]
    public Task AConjugatePairBecomesARealBlock() => Asserts("""
        V = [1 1; 1i -1i];
        D = [2+3i 0; 0 2-3i];
        [Vn, Dn] = cdf2rdf(V, D);
        assert(isreal(Dn));
        assert(isequal(Dn, [2 3; -3 2]));
        assert(norm(Vn - [sqrt(2) 0; 0 sqrt(2)]) < 1e-15);
        """);

    [Fact]
    public Task ARealDiagonalPassesThroughUnchanged() => Asserts("""
        [Vn, Dn] = cdf2rdf(eye(2), [2 0; 0 3]);
        assert(isequal(Vn, eye(2)));
        assert(isequal(Dn, [2 0; 0 3]));
        """);

    [Fact]
    public Task ADiagonalThatIsNoPairingIsRefused() =>
        Refuses("cdf2rdf(eye(2), [1i 0; 0 2]);", "MATLAB:cdf2rdf:invalidDiagonal");

    [Fact]
    public Task ARealSchurFormBecomesATriangularOne() => Asserts("""
        A = [1 -2 0; 3 4 1; 0 0 5];
        [U, T] = schur(A);
        [Uc, Tc] = rsf2csf(U, T);
        assert(abs(Tc(2,1)) + abs(Tc(3,1)) + abs(Tc(3,2)) == 0);
        assert(max(max(abs(Uc * Tc * Uc' - A))) < 1e-14);
        assert(max(max(abs(Uc' * Uc - eye(3)))) < 1e-14);
        assert(abs(Tc(1,1) - (2.5 + 1.93649167310371i)) < 1e-14);
        """);

    // --- condeig ---------------------------------------------------------------------------------

    [Fact]
    public Task ASymmetricMatrixHasPerfectlyConditionedEigenvalues() => Asserts("""
        s = condeig([4 1 0; 1 3 1; 0 1 5]);
        assert(isequal(size(s), [3 1]));
        assert(max(abs(s - 1)) < 1e-14);
        """);

    [Fact]
    public Task TheConditioningComesBackBesideTheFactorsItWasMeasuredFrom() => Asserts("""
        [X, D, s] = condeig([1 2; 3 4]);
        assert(max(abs(s - 1.0150384378451045)) < 1e-13);
        assert(norm([1 2; 3 4] * X - X * D) < 1e-13);
        assert(isequal(condeig([1 2; 3 4]), s));
        """);

    // --- normest / normest1 / condest --------------------------------------------------------------

    [Fact]
    public Task ThePowerIterationFindsAMagicSquaresNormExactly() => Asserts("""
        [e, cnt] = normest(magic(6));
        assert(e == 111);
        assert(cnt == 2);
        assert(normest(zeros(3)) == 0);
        assert(abs(normest(eye(5)) - 1) < 1e-15);
        """);

    [Fact]
    public Task ATighterToleranceDoesNotMoveASettledEstimate() => Asserts("""
        assert(normest(magic(6), 1e-12) == 111);
        assert(abs(normest(hilb(8)) - norm(hilb(8))) < 1e-6);
        """);

    [Fact]
    public Task ASmallMatrixHasItsOneNormReadOffExactly() => Asserts("""
        % At four rows or fewer the estimator does not iterate, so there is nothing random in it.
        assert(normest1([3 1; 1 3]) == norm([3 1; 1 3], 1));
        assert(normest1(magic(4)) == norm(magic(4), 1));
        [n, v, w, it] = normest1([4 1 0; 1 3 1; 0 1 5]);
        assert(n == 6);
        assert(isequal(it, [0 1]));
        assert(sum(abs(w)) == n);
        """);

    [Fact]
    public Task TheConditionEstimateMeetsTheConditionNumber() => Asserts("""
        assert(condest([3 1; 1 3]) == cond([3 1; 1 3], 1));
        assert(condest(eye(3)) == 1);
        assert(isinf(condest([1 2; 2 4])));
        [c, v] = condest([4 1 0; 1 3 1; 0 1 5]);
        assert(abs(sum(abs(v)) - 1) < 1e-15);
        assert(abs(c - cond([4 1 0; 1 3 1; 0 1 5], 1)) < 1e-10);
        """);

    [Fact]
    public Task ANonSquareMatrixHasNoConditionNumber() =>
        Refuses("condest([1 2 3; 4 5 6]);", "MATLAB:condest:NonSquareMatrix");

    // --- sylvester -------------------------------------------------------------------------------

    [Fact]
    public Task TheSylvesterSolutionSatisfiesItsEquation() => Asserts("""
        A = [1 2; 3 4]; B = [5 6; 7 8]; C = eye(2);
        X = sylvester(A, B, C);
        assert(norm(A * X + X * B - C) < 1e-14);
        assert(max(max(abs(X - [-1.2222222222222219 0.9444444444444442; ...
                                 0.86111111111111083 -0.58333333333333315]))) < 1e-13);
        """);

    [Fact]
    public Task ARectangularRightHandSideIsSolvedInItsOwnShape() => Asserts("""
        A = [1 2 0; 0 3 1; 1 0 2]; B = [4 1; 0 5]; C = [1 2; 3 4; 5 6];
        X = sylvester(A, B, C);
        assert(isequal(size(X), [3 2]));
        assert(norm(A * X + X * B - C) < 1e-13);
        """);

    [Fact]
    public Task AComplexSylvesterEquationIsSolvedInComplexArithmetic() => Asserts("""
        A = [1+1i 2; 0 3]; B = [1 0; 0 2]; C = ones(2);
        X = sylvester(A, B, C);
        assert(max(max(abs(A * X + X * B - C))) < 1e-14);
        assert(max(max(abs(X - [0.2-0.1i 0.18-0.06i; 0.25 0.2]))) < 1e-14);
        """);

    [Fact]
    public Task ASylvesterEquationChecksItsShapesAndItsNumbers() => Asserts("""
        want = {'MATLAB:sylvester:inputMustBeSquare', 'MATLAB:sylvester:inputMustBeCompatibleSize', ...
                'MATLAB:sylvester:inputWithNaNInf'};
        tries = {@() sylvester([1 2 3], 1, 1), @() sylvester([1 2;3 4], [1 2;3 4], [1 2 3]), ...
                 @() sylvester([Inf 0; 0 1], eye(2), eye(2))};
        for k = 1:3
            got = '';
            try
                tries{k}();
            catch err
                got = err.identifier;
            end
            assert(strcmp(got, want{k}), ['case ' num2str(k) ' gave ' got]);
        end
        """);

    // --- lsqminnorm ------------------------------------------------------------------------------

    [Fact]
    public Task TheShortestLeastSquaresSolutionIsThePseudoinversesOwn() => Asserts("""
        A = [1 2 3; 4 5 6; 7 8 9];
        x = lsqminnorm(A, [1; 2; 3]);
        assert(max(abs(x - pinv(A) * [1; 2; 3])) < 1e-13);
        assert(max(abs(A * x - [1; 2; 3])) < 1e-13);
        """);

    [Fact]
    public Task AWideSystemGetsTheSolutionOfSmallestLength() => Asserts("""
        x = lsqminnorm([1 2 3], 6);
        assert(isequal(size(x), [3 1]));
        assert(abs([1 2 3] * x - 6) < 1e-14);
        assert(abs(norm(x) - 6 / sqrt(14)) < 1e-14);
        """);

    [Fact]
    public Task AComplexSystemIsSolvedWithTheReflectorsConjugated() => Asserts("""
        % The reflector's scalar is conjugated on the way in and not on the way out; getting that
        % backwards leaves the real case perfect and the complex case quietly wrong.
        A = [1+1i 2; 2 4+2i];
        x = lsqminnorm(A, [1; 2]);
        assert(max(abs(A * x - [1; 2])) < 1e-14);
        assert(max(abs(x - [0.3-0.1i; 0.3-0.1i])) < 1e-14);
        d = lsqminnorm([1i 0; 0 2], [1; 2]);
        assert(max(abs(d - [-1i; 1])) < 1e-15);
        """);

    [Fact]
    public Task ARankToleranceIsAnAbsoluteThresholdOnTheDiagonal() => Asserts("""
        A = [1 2 3; 4 5 6; 7 8 9];
        assert(isequal(lsqminnorm(A, [1;2;3], 20), zeros(3, 1)));
        loose = lsqminnorm(A, [1;2;3], 1);
        assert(max(abs(loose - pinv(A) * [1;2;3])) < 1e-13);
        """);

    [Fact]
    public Task TheOptionAfterTheToleranceMustBeOneOfTwoWords() =>
        Refuses("lsqminnorm([1 2; 3 4], [1;2], 'maybe');", "MATLAB:lsqminnorm:InvalidWarn");

    // --- lscov -----------------------------------------------------------------------------------

    [Fact]
    public Task TheOrdinaryFitComesWithItsStandardErrors() => Asserts("""
        A = [1 1; 1 2; 1 3; 1 4]; b = [2; 3; 5; 6];
        [x, stdx, mse, S] = lscov(A, b);
        assert(max(abs(x - [0.5; 1.4])) < 1e-13);
        assert(max(abs(stdx - [0.38729833462074237; 0.14142135623730973])) < 1e-13);
        assert(abs(mse - 0.1) < 1e-13);
        assert(max(max(abs(S - [0.15 -0.05; -0.05 0.02]))) < 1e-13);
        assert(abs(sqrt(S(1,1)) - stdx(1)) < 1e-14);
        assert(abs(sqrt(S(2,2)) - stdx(2)) < 1e-14);
        """);

    [Fact]
    public Task AWeightVectorAndTheDiagonalCovarianceItStandsForDisagree() => Asserts("""
        A = [1 1; 1 2; 1 3; 1 4]; b = [2; 3; 5; 6];
        byWeight = lscov(A, b, [1; 2; 3; 4]);
        byMatrix = lscov(A, b, diag([1 2 3 4]));
        assert(max(abs(byWeight - [0.50000000000000144; 1.3999999999999997])) < 1e-13);
        assert(max(abs(byMatrix - [0.55172413793103359; 1.3793103448275863])) < 1e-13);
        """);

    [Fact]
    public Task TheOrthogonalAlgorithmAnswersWhatTheCholeskyOneDoes() => Asserts("""
        A = [1 1; 1 2; 1 3; 1 4]; b = [2; 3; 5; 6]; V = diag([1 2 3 4]);
        [x1, s1, m1] = lscov(A, b, V, 'orth');
        [x2, s2, m2] = lscov(A, b, V, 'chol');
        assert(max(abs(x1 - x2)) < 1e-13);
        assert(max(abs(s1 - [0.2986294495808412; 0.136305071558982])) < 1e-13);
        assert(abs(m1 - 0.04310344827586212) < 1e-14);
        """);

    [Fact]
    public Task SeveralRightHandSidesEachGetTheirOwnFitAndError() => Asserts("""
        A = [1 1; 1 2; 1 3; 1 4]; b = [2; 3; 5; 6];
        [x, stdx, mse] = lscov(A, [b b * 2]);
        assert(isequal(size(x), [2 2]));
        assert(max(max(abs(x - [0.5 1; 1.4 2.8]))) < 1e-13);
        assert(max(abs(mse - [0.1 0.4])) < 1e-13);
        """);

    [Fact]
    public Task ACovarianceThatIsNotSquareIsRefusedByItsShape() =>
        Refuses("lscov([1 1; 1 2], [1; 2], [1 2; 3 4]);", "MATLAB:lscov:InvalidCovMatSymV");

    // --- polyeig ---------------------------------------------------------------------------------

    [Fact]
    public Task TheQuadraticEigenproblemHasTwiceAsManyAnswersAsRows() => Asserts("""
        A0 = [1 0; 0 2]; A1 = [3 1; 1 4]; A2 = eye(2);
        e = polyeig(A0, A1, A2);
        assert(isequal(size(e), [4 1]));
        want = [-4.2143197433775388; -1.4608111271891127; -1; -0.32486912943335361];
        assert(max(abs(e - want)) < 1e-13);
        """);

    [Fact]
    public Task EveryEigenvectorMakesItsOwnPolynomialSingular() => Asserts("""
        A0 = [1 0; 0 2]; A1 = [3 1; 1 4]; A2 = eye(2);
        [X, e] = polyeig(A0, A1, A2);
        assert(isequal(size(X), [2 4]));
        for j = 1:4
            v = X(:, j);
            assert(abs(norm(v) - 1) < 1e-13);
            assert(max(abs((A0 + e(j) * A1 + e(j)^2 * A2) * v)) < 1e-13);
        end
        """);

    [Fact]
    public Task TheConditionOfEachPolynomialEigenvalueIsReported() => Asserts("""
        A0 = [1 0; 0 2]; A1 = [3 1; 1 4]; A2 = eye(2);
        [~, ~, s] = polyeig(A0, A1, A2);
        want = [0.46649858561507218; 5.5454192356383851; 5.8309518948453025; 0.79720402180574634];
        assert(max(abs(s - want) ./ want) < 1e-12);
        """);

    [Fact]
    public Task OneMatrixIsItsOwnEigenproblemAndTwoArePencils() => Asserts("""
        one = sort(polyeig([1 2; 3 4]));
        want = sort(eig([1 2; 3 4]));
        assert(max(abs(one(:) - want(:))) < 1e-13);
        pair = sort(polyeig([1 2; 3 4], eye(2)));
        other = sort(-eig([1 2; 3 4]));
        assert(max(abs(pair(:) - other(:))) < 1e-13);
        """);

    [Fact]
    public Task AskingForTheConditionOfOneMatrixIsRefused() =>
        Refuses("[X, e, s] = polyeig([1 2; 3 4]);", "MATLAB:polyeig:tooFewInputs");

    // --- funm ------------------------------------------------------------------------------------

    [Fact]
    public Task TheExponentialOfAMatrixAgreesWithExpm() => Asserts("""
        A = [1 1 0; 0 2 1; 0 0 3];
        assert(norm(funm(A, 'exp') - expm(A)) < 1e-13);
        assert(norm(funm(A, 'log') - logm(A)) < 1e-13);
        assert(norm(funm(diag([1 2 3]), 'exp') - diag(exp([1 2 3]))) < 1e-14);
        """);

    [Fact]
    public Task TheTrigonometricFunctionsOfAMatrixSatisfyTheirIdentity() => Asserts("""
        A = [1 1 0; 0 2 1; 0 0 3];
        C = funm(A, 'cos'); S = funm(A, 'sin');
        assert(norm(C * C + S * S - eye(3)) < 1e-13);
        Ch = funm(A, 'cosh'); Sh = funm(A, 'sinh');
        assert(norm(Ch * Ch - Sh * Sh - eye(3)) < 1e-12);
        assert(abs(C(1,1) - 0.54030230586814) < 1e-14);
        """);

    [Fact]
    public Task AUserFunctionIsAskedForItsDerivativesToo() => Asserts("""
        % A defective block has no eigenvector basis to lean on, so the series needs f', f'' ...
        F = funm([2 1; 0 2], @(x, k) cubeDerivative(x, k));
        assert(isequal(F, [8 12; 0 8]));
        plain = funm([1 1 0; 0 2 1; 0 0 3], @(x, k) exp(x));
        assert(norm(plain - expm([1 1 0; 0 2 1; 0 0 3])) < 1e-13);

        function out = cubeDerivative(x, k)
            if k == 0
                out = x.^3;
            elseif k == 1
                out = 3 * x.^2;
            elseif k == 2
                out = 6 * x;
            elseif k == 3
                out = 6 * ones(size(x));
            else
                out = zeros(size(x));
            end
        end
        """);

    [Fact]
    public Task CloseEigenvaluesShareABlockAndDistantOnesDoNot() => Asserts("""
        A = [1 1 1; 0 1.01 1; 0 0 5];
        [F, flag, out] = funm(A, 'exp');
        assert(flag == 0);
        assert(numel(out.ind) == 2);
        assert(isequal(out.ind{1}, [1 2]));
        assert(isequal(out.ind{2}, 3));
        assert(norm(F - expm(A)) < 1e-11);
        [~, ~, apart] = funm([1 1 0; 0 2 1; 0 0 3], 'cos');
        assert(numel(apart.ind) == 3);
        assert(isequal(apart.ord, [3 2 1]));
        """);

    [Fact]
    public Task AFunctionOfAMatrixNeedsASquareMatrix() =>
        Refuses("funm([1 2 3; 4 5 6], 'exp');", "MATLAB:funm:InputDim");

    // --- gsvd ------------------------------------------------------------------------------------

    [Fact]
    public Task TheGeneralizedDecompositionRebuildsBothMatrices() => Asserts("""
        A = [1 2; 3 4; 5 6]; B = [7 8; 9 10];
        [U, V, X, C, S] = gsvd(A, B);
        assert(isequal(size(U), [3 3]));
        assert(isequal(size(V), [2 2]));
        assert(isequal(size(X), [2 2]));
        assert(norm(U * C * X' - A) < 1e-13);
        assert(norm(V * S * X' - B) < 1e-13);
        assert(norm(U' * U - eye(3)) < 1e-13);
        assert(norm(V' * V - eye(2)) < 1e-13);
        """);

    [Fact]
    public Task TheTwoDiagonalsAreACosineAndASineOfTheSameAngle() => Asserts("""
        A = [1 2; 3 4; 5 6]; B = [7 8; 9 10];
        [~, ~, ~, C, S] = gsvd(A, B);
        c = [C(1,1) C(2,2)];
        s = [S(1,1) S(2,2)];
        assert(max(abs(c.^2 + s.^2 - 1)) < 1e-14);
        assert(max(abs(sort(c ./ s) - sort(gsvd(A, B)'))) < 1e-12);
        """);

    [Fact]
    public Task TheGeneralizedValuesAscendWhereOrdinaryOnesDescend() => Asserts("""
        sigma = gsvd([1 2; 3 4; 5 6], [7 8; 9 10]);
        assert(isequal(size(sigma), [2 1]));
        assert(sigma(1) < sigma(2));
        assert(max(abs(sigma - [0.37415322624049602; 6.5467556364426551])) < 1e-12);
        """);

    [Fact]
    public Task TheEconomyFormTrimsTheTallerFactor() => Asserts("""
        A = [1 2; 3 4; 5 6]; B = [7 8; 9 10];
        [U, ~, X, C, S] = gsvd(A, B, 0);
        assert(isequal(size(U), [3 2]));
        assert(norm(U * C * X' - A) < 1e-13);
        """);

    [Fact]
    public Task TwoMatricesMustShareTheirColumns() =>
        Refuses("gsvd([1 2; 3 4], [1 2 3]);", "MATLAB:gsvd:MatrixColMismatch");

    // --- decomposition ---------------------------------------------------------------------------

    [Fact]
    public Task ADecompositionSolvesWhatItsMatrixSolves() => Asserts("""
        A = [4 1 0; 1 3 1; 0 1 5]; b = [1; 2; 3];
        dA = decomposition(A);
        assert(strcmp(dA.Type, 'lu'));
        assert(isequal(dA.MatrixSize, [3 3]));
        assert(dA.IsReal);
        assert(dA.ScaleFactor == 1);
        assert(strcmp(class(dA), 'decomposition'));
        assert(norm((dA \ b) - (A \ b)) < 1e-15);
        assert(norm((b' / dA) - (b' / A)) < 1e-15);
        """);

    [Fact]
    public Task TheTypeIsChosenFromTheMatrixWhenItIsNotNamed() => Asserts("""
        upper = decomposition([1 2; 0 4]);
        wide = decomposition([1 2 3; 4 5 6]);
        identity = decomposition(eye(3));
        assert(strcmp(upper.Type, 'triangular'));
        assert(strcmp(wide.Type, 'qr'));
        assert(strcmp(identity.Type, 'triangular'));
        """);

    [Fact]
    public Task EveryTypeThatFitsTheMatrixAnswersTheSameSolution() => Asserts("""
        A = [4 1 0; 1 3 1; 0 1 5]; b = [1; 2; 3];
        want = A \ b;
        for name = {'lu', 'qr', 'cod', 'chol', 'ldl', 'banded', 'hessenberg'}
            d = decomposition(A, name{1});
            assert(strcmp(d.Type, name{1}));
            assert(norm((d \ b) - want) < 1e-13, name{1});
        end
        """);

    [Fact]
    public Task ScalingAndTransposingAreFreeAndCompose() => Asserts("""
        A = [4 1 0; 1 3 1; 0 1 5]; b = [1; 2; 3];
        dA = decomposition(A);
        assert((3 * dA).ScaleFactor == 3);
        assert(norm(((3 * dA) \ b) - ((3 * A) \ b)) < 1e-15);
        assert(norm(((-dA) \ b) + (A \ b)) < 1e-15);
        assert(norm(((dA / 2) \ b) - ((A / 2) \ b)) < 1e-15);
        assert((dA').IsConjugateTransposed);
        assert(norm(((dA') \ b) - (A' \ b)) < 1e-15);
        assert(norm(((2 * dA') \ b) - ((2 * A') \ b)) < 1e-15);
        """);

    [Fact]
    public Task RankBelongsToTheOrthogonalTypesAndConditionToTheRest() => Asserts("""
        A = [4 1 0; 1 3 1; 0 1 5];
        assert(rank(decomposition([1 2; 2 4], 'cod')) == 1);
        assert(abs(rcond(decomposition(A)) - rcond(A)) < 1e-15);
        assert(~isIllConditioned(decomposition(A)));
        assert(isIllConditioned(decomposition([1 2; 2 4], 'cod')));
        assert(rank([1 2; 2 4]) == 1);
        """);

    [Fact]
    public Task ADecompositionRefusesWhatItsTypeCannotHold() => Asserts("""
        want = {'MATLAB:decomposition:InvalidAForChol', 'MATLAB:decomposition:InvalidAForTriang', ...
                'MATLAB:decomposition:RankNotSupported', 'MATLAB:decomposition:RcondNotSupported', ...
                'MATLAB:decomposition:TransposeNotSupported', 'MATLAB:decomposition:mldivide', ...
                'MATLAB:decomposition:QRmldivideTransp'};
        A = [1 2; 3 4];
        tries = {@() decomposition(A, 'chol'), @() decomposition(A, 'triangular'), ...
                 @() rank(decomposition(A)), @() rcond(decomposition(A, 'qr')), ...
                 @() transposeOf(decomposition(A)), @() decomposition(A) \ [1 2], ...
                 @() decomposition([1 2; 3 4; 5 6], 'qr')' \ [1; 2]};
        for k = 1:numel(tries)
            got = '';
            try
                tries{k}();
            catch err
                got = err.identifier;
            end
            assert(strcmp(got, want{k}), ['case ' num2str(k) ' gave ' got]);
        end

        function out = transposeOf(d)
            out = d.';
        end
        """);

    [Fact]
    public Task ACopyOfADecompositionIsAValueAndNotAHandle() => Asserts("""
        A = [4 1 0; 1 3 1; 0 1 5];
        dA = decomposition(A);
        other = dA;
        other = 5 * other;
        assert(dA.ScaleFactor == 1);
        assert(other.ScaleFactor == 5);
        """);

    // --- the pairwise engine underneath ------------------------------------------------------------

    [Fact]
    public Task ASchurFormCanBeAskedForInComplexArithmetic() => Asserts("""
        A = [1 -2 0; 3 4 1; 0 0 5];
        [U, T] = schur(A, 'complex');
        assert(~isreal(T));
        assert(abs(T(2,1)) + abs(T(3,1)) + abs(T(3,2)) == 0);
        assert(max(max(abs(U * T * U' - A))) < 1e-14);
        """);
}
