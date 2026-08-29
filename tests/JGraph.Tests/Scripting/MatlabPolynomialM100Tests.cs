using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The polynomial and 1-D signal family (M100): <c>roots</c>, <c>poly</c>, <c>polyder</c>,
/// <c>polyint</c>, <c>polyvalm</c>, <c>conv</c>, <c>deconv</c>, <c>convn</c>, <c>nextpow2</c>,
/// <c>unwrap</c>, <c>cplxpair</c>, <c>polyarea</c>, <c>rectint</c> and <c>inpolygon</c>.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer and not JGraph's display
/// format. Every number here was read off MATLAB R2024a on this machine and holds exactly, with two
/// classes of exception, both recorded in ADR 0101: an answer that comes through <c>eig</c> carries
/// <c>eig</c>'s own OpenBLAS-versus-MKL difference in the last digits, and a negative zero out of the
/// eigensolver may land on the other sign.
/// </para>
/// <para>
/// The shape assertions are as important as the value ones. Most of what these names do that is
/// specific to MATLAB rather than to mathematics is decide which way round the answer comes out,
/// and <c>conv</c> alone has three different rules depending on its arguments.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabPolynomialM100Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPolynomialM100Tests() => JG.Reset();

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

    // --- roots --------------------------------------------------------------------------------------

    [Fact]
    public Task Roots_AnswersAColumnOfTheRootsHighestPowerFirst() => Asserts("""
        r = roots([1 -6 11 -6]);
        assert(isequal(size(r), [3 1]));
        assert(abs(r(1) - 3) < 1e-12);
        assert(abs(r(2) - 2) < 1e-12);
        assert(abs(r(3) - 1) < 1e-12);
        """);

    [Fact]
    public Task Roots_TakesATrailingZeroAsARootAtTheOriginAndLeadsWithIt() => Asserts("""
        r = roots([1 2 3 0 0]);
        assert(isequal(size(r), [4 1]));
        assert(r(1) == 0);
        assert(r(2) == 0);
        assert(abs(real(r(3)) + 1) < 1e-12);
        assert(abs(abs(imag(r(3))) - sqrt(2)) < 1e-12);
        """);

    [Fact]
    public Task Roots_DiscardsALeadingZeroRatherThanCountingARootForIt() => Asserts("""
        r = roots([0 0 1 -1]);
        assert(isequal(size(r), [1 1]));
        assert(abs(r - 1) < 1e-12);
        """);

    [Fact]
    public Task Roots_OfAConstantOrOfNothingIsTheEmptyColumn() => Asserts("""
        assert(isequal(size(roots([5])), [0 1]));
        assert(isequal(size(roots([])), [0 1]));
        assert(isequal(size(roots([0 0 0])), [0 1]));
        """);

    [Fact]
    public Task Roots_ReadsAColumnTheSameWayAsARow() => Asserts("""
        r = roots([1; -3; 2]);
        assert(isequal(size(r), [2 1]));
        assert(abs(r(1) - 2) < 1e-12);
        assert(abs(r(2) - 1) < 1e-12);
        """);

    [Fact]
    public Task Roots_AnswersAConjugatePairForAnIrreducibleQuadratic() => Asserts("""
        r = roots([1 0 1]);
        assert(isequal(size(r), [2 1]));
        assert(abs(imag(r(1)) - 1) < 1e-12);
        assert(abs(imag(r(2)) + 1) < 1e-12);
        assert(abs(real(r(1))) < 1e-12);
        """);

    [Fact]
    public Task Roots_TakesComplexCoefficients() => Asserts("""
        r = roots([1 1i]);
        assert(isequal(size(r), [1 1]));
        assert(abs(imag(r) + 1) < 1e-12);
        """);

    [Fact]
    public Task Roots_RefusesAMatrixAndANonFiniteCoefficient() => Refuses(
        "roots([1 2; 3 4]);", "MATLAB:roots:NonVectorInput");

    [Fact]
    public Task Roots_RefusesAnInfinity() => Refuses(
        "roots([1 Inf 2]);", "MATLAB:roots:NonFiniteInput");

    // --- poly ---------------------------------------------------------------------------------------

    [Fact]
    public Task Poly_ExpandsAVectorOfRootsIntoAMonicRow() => Asserts("""
        p = poly([1 2 3]);
        assert(isequal(size(p), [1 4]));
        assert(isequal(p, [1 -6 11 -6]));
        """);

    [Fact]
    public Task Poly_OfASquareMatrixIsItsCharacteristicPolynomial() => Asserts("""
        p = poly([1 2; 3 4]);
        assert(isequal(size(p), [1 3]));
        assert(p(1) == 1);
        assert(abs(p(2) + 5) < 1e-12);
        assert(abs(p(3) + 2) < 1e-12);
        """);

    [Fact]
    public Task Poly_ReadsAOneByOneAsAMatrixAndNotAsAOneElementRootVector() => Asserts("""
        assert(isequal(poly(5), [1 -5]));
        assert(isequal(poly([7]), [1 -7]));
        assert(isequal(poly([]), 1));
        """);

    [Fact]
    public Task Poly_AnswersRealWhenTheRootsAreClosedUnderConjugation() => Asserts("""
        p = poly([1+2i, 1-2i]);
        assert(isreal(p));
        assert(isequal(p, [1 -2 5]));
        """);

    [Fact]
    public Task Poly_KeepsTheImaginaryPartWhenTheRootsAreNot() => Asserts("""
        p = poly([1+1i, 2+2i]);
        assert(~isreal(p));
        assert(abs(p(2) - (-3-3i)) < 1e-12);
        assert(abs(p(3) - 4i) < 1e-12);
        """);

    [Fact]
    public Task Poly_DiscardsAnInfiniteRootRatherThanPoisoningTheExpansion() => Asserts("""
        assert(isequal(poly([1 Inf 2]), [1 -3 2]));
        """);

    [Fact]
    public Task Poly_RefusesSomethingNeitherVectorNorSquare() => Refuses(
        "poly([1 2 3; 4 5 6]);", "MATLAB:poly:InputSize");

    [Fact]
    public Task Poly_AndRootsAreInverseUpToOrdering() => Asserts("""
        p = poly(roots([1 0 0 0 1]));
        assert(abs(p(1) - 1) < 1e-12);
        assert(abs(p(5) - 1) < 1e-12);
        assert(all(abs(p(2:4)) < 1e-12));
        """);

    // --- polyder, polyint, polyvalm -----------------------------------------------------------------

    [Fact]
    public Task Polyder_DifferentiatesAndStripsTheLeadingZero() => Asserts("""
        assert(isequal(polyder([3 2 1]), [6 2]));
        assert(isequal(polyder([5]), 0));
        assert(isequal(polyder([0 0 5]), 0));
        assert(isequal(size(polyder([3 2 1])), [1 2]));
        """);

    [Fact]
    public Task Polyder_WithTwoArgumentsDifferentiatesTheProduct() => Asserts("""
        assert(isequal(polyder([1 2], [3 4 5]), [9 20 13]));
        """);

    [Fact]
    public Task Polyder_WithTwoOutputsDifferentiatesTheRatio() => Asserts("""
        [q, d] = polyder([1 2], [3 4 5]);
        assert(isequal(q, [-3 -12 -3]));
        assert(isequal(d, [9 24 46 40 25]));
        """);

    [Fact]
    public Task Polyder_AnswersARowEvenForAColumn() => Asserts("""
        k = polyder([1; 2; 3]);
        assert(isequal(size(k), [1 2]));
        assert(isequal(k, [2 2]));
        """);

    [Fact]
    public Task Polyint_AddsTheConstantOfIntegrationOnTheRight() => Asserts("""
        assert(isequal(polyint([3 2 1]), [1 1 1 0]));
        assert(isequal(polyint([3 2 1], 7), [1 1 1 7]));
        assert(isequal(polyint([]), 0));
        assert(isequal(polyint(4), [4 0]));
        """);

    /// <summary>
    /// MATLAB divides by a row and then concatenates, so a column polynomial broadcasts to a square
    /// and the concatenation fails. The failure is the documented behaviour, not an accident here.
    /// </summary>
    [Fact]
    public Task Polyint_RefusesAColumnTheWayMatlabsOwnConcatenationDoes() => Refuses(
        "polyint([3; 2; 1]);", "MATLAB:catenate:dimensionMismatch");

    [Fact]
    public Task Polyvalm_TakesEveryPowerAsAMatrixPower() => Asserts("""
        Y = polyvalm([1 2 3], [1 2; 3 4]);
        assert(isequal(Y, [12 14; 21 33]));
        """);

    [Fact]
    public Task Polyvalm_OfAConstantIsThatMultipleOfTheIdentity() => Asserts("""
        Y = polyvalm([5], [1 2; 3 4]);
        assert(isequal(Y, [5 0; 0 5]));
        """);

    [Fact]
    public Task Polyvalm_RefusesANonSquareMatrix() => Refuses(
        "polyvalm([1 2], [1 2 3]);", "MATLAB:polyvalm:NonSquareMatrix");

    [Fact]
    public Task Polyvalm_RefusesAMatrixWhereThePolynomialGoes() => Refuses(
        "polyvalm([1 2; 3 4], [1 2; 3 4]);", "MATLAB:polyvalm:InvalidP");

    // --- conv, deconv, convn ------------------------------------------------------------------------

    [Fact]
    public Task Conv_MultipliesTwoPolynomials() => Asserts("""
        assert(isequal(conv([1 2 3], [1 1]), [1 3 5 3]));
        assert(isequal(conv([1 2 3], [1 -1]), [1 1 1 -3]));
        """);

    /// <summary>
    /// The full shape follows whichever operand is longer, and the second one when they are the same
    /// length — which is why swapping the arguments can transpose the answer.
    /// </summary>
    [Fact]
    public Task Conv_TakesItsOrientationFromTheLongerOperand() => Asserts("""
        assert(isequal(size(conv([1 2 3], [1; 1])), [1 4]));
        assert(isequal(size(conv([1; 2; 3], [1 1])), [4 1]));
        assert(isequal(size(conv([1 2], [1; 1])), [3 1]));
        assert(isequal(size(conv([1; 2], [1 1])), [1 3]));
        """);

    [Fact]
    public Task Conv_OfAScalarFollowsTheOtherOperand() => Asserts("""
        assert(isequal(conv(2, [1 2 3]), [2 4 6]));
        assert(isequal(size(conv(2, [1 2 3])), [1 3]));
        """);

    /// <summary>A cut shape is measured against the first operand, so it follows the first alone.</summary>
    [Fact]
    public Task Conv_WithAShapeWordFollowsTheFirstOperandInstead() => Asserts("""
        assert(isequal(conv([1 2 3 4], [1 1], 'same'), [3 5 7 4]));
        assert(isequal(conv([1 2 3 4], [1 1], 'valid'), [3 5 7]));
        assert(isequal(size(conv([1; 2; 3; 4], [1 1], 'same')), [4 1]));
        assert(isequal(size(conv([1 2], [1 2 3], 'valid')), [1 0]));
        """);

    [Fact]
    public Task Conv_MultipliesComplexPolynomials() => Asserts("""
        w = conv([1 1i], [1 1]);
        assert(abs(w(1) - 1) < 1e-15);
        assert(abs(w(2) - (1 + 1i)) < 1e-15);
        assert(abs(w(3) - 1i) < 1e-15);
        """);

    [Fact]
    public Task Conv_RefusesAMatrixAndAnUnknownShape() => Refuses(
        "conv([1 2; 3 4], [1 1]);", "MATLAB:conv:AorBNotVector");

    [Fact]
    public Task Conv_RefusesAShapeWordItDoesNotKnow() => Refuses(
        "conv([1 2 3], [1 1], 'bogus');", "MATLAB:conv2:unknownShapeParameter");

    [Fact]
    public Task Deconv_DividesSoThatTheQuotientAndRemainderRebuildTheDividend() => Asserts("""
        [q, r] = deconv([1 3 3 1], [1 1]);
        assert(isequal(q, [1 2 1]));
        assert(isequal(r, [0 0 0 0]));

        [q2, r2] = deconv([1 0 0 1], [1 1 1]);
        assert(isequal(q2, [1 -1]));
        assert(isequal(r2, [0 0 0 2]));
        assert(isequal(conv([1 1 1], q2) + r2, [1 0 0 1]));
        """);

    [Fact]
    public Task Deconv_ByALongerDivisorGivesZeroAndTheDividendBack() => Asserts("""
        [q, r] = deconv([1 2 3], [1 1 1 1]);
        assert(isequal(q, 0));
        assert(isequal(r, [1 2 3]));
        """);

    [Fact]
    public Task Deconv_KeepsTheDividendsOrientation() => Asserts("""
        [q, r] = deconv([1; 3; 3; 1], [1 1]);
        assert(isequal(size(q), [3 1]));
        assert(isequal(size(r), [4 1]));
        """);

    [Fact]
    public Task Deconv_RefusesADivisorWhoseLeadingCoefficientIsZero() => Refuses(
        "deconv([1 2], [0 1]);", "MATLAB:deconv:ZeroCoef1");

    [Fact]
    public Task Convn_ConvolvesOverEveryDimensionAtOnce() => Asserts("""
        C = convn([1 2; 3 4], [1 1]);
        assert(isequal(size(C), [2 3]));
        assert(isequal(C, [1 3 2; 3 7 4]));
        """);

    [Fact]
    public Task Convn_CutsToTheFirstOperandsSizeForSameAndToWhatNoPaddingTouchedForValid() => Asserts("""
        assert(isequal(convn([1 2; 3 4], [1 1], 'same'), [3 2; 7 4]));
        assert(isequal(convn([1 2; 3 4], [1 1], 'valid'), [3; 7]));
        assert(isequal(size(convn(magic(3), [1 2; 3 4], 'valid')), [2 2]));
        """);

    [Fact]
    public Task Convn_OfTwoVectorsIsConv() => Asserts("""
        assert(isequal(convn([1 2 3], [1 1]), conv([1 2 3], [1 1])));
        """);

    [Fact]
    public Task Convn_WorksInThreeDimensions() => Asserts("""
        A = reshape(1:8, [2 2 2]);
        B = reshape([1 1 1 1], [2 1 2]);
        C = convn(A, B);
        assert(isequal(size(C), [3 2 3]));
        assert(isequal(C(:)', [1 3 2 3 7 4 6 14 8 10 22 12 5 11 6 7 15 8]));
        assert(isequal(size(convn(A, B, 'same')), [2 2 2]));
        """);

    // --- nextpow2, unwrap, cplxpair -----------------------------------------------------------------

    [Fact]
    public Task Nextpow2_AnswersTheExponentAndLeavesAnExactPowerAlone() => Asserts("""
        assert(isequal(nextpow2([0 1 2 3 4 5 1023 1024 1025]), [0 0 1 2 2 3 10 10 11]));
        """);

    [Fact]
    public Task Nextpow2_ReadsTheMagnitudeAndPassesTheNonFiniteThrough() => Asserts("""
        p = nextpow2([-8 -9 0.25 0.3 Inf -Inf NaN]);
        assert(isequal(p(1:4), [3 4 -2 -1]));
        assert(p(5) == Inf);
        assert(p(6) == Inf);
        assert(isnan(p(7)));
        """);

    [Fact]
    public Task Nextpow2_KeepsTheArraysShape() => Asserts("""
        assert(isequal(nextpow2([1 2; 3 4]), [0 1; 2 2]));
        assert(nextpow2(2^-1074) == -1074);
        """);

    /// <summary>
    /// Every step is measured against the record as it arrived. Measuring against an already
    /// corrected sample compounds the corrections, which turns a steady ramp into a runaway — the
    /// answers below are the ones that catch it.
    /// </summary>
    [Fact]
    public Task Unwrap_MeasuresEveryStepAgainstTheOriginalRecord() => Asserts("""
        q = unwrap([0 4 8 12]);
        assert(abs(q(1)) < 1e-12);
        assert(abs(q(2) - (4 - 2*pi)) < 1e-12);
        assert(abs(q(3) - (8 - 4*pi)) < 1e-12);
        assert(abs(q(4) - (12 - 6*pi)) < 1e-12);
        """);

    [Fact]
    public Task Unwrap_LeavesARecordAloneWhenNoStepReachesTheCutoff() => Asserts("""
        assert(isequal(unwrap([0 3.1 6.2 9.3]), [0 3.1 6.2 9.3]));
        assert(isequal(unwrap([0 4 8 12], 100), [0 4 8 12]));
        """);

    /// <summary>
    /// A step of exactly π is half a turn, and the turns are rounded half towards zero — so a record
    /// stepping by π stays as it is rather than being folded flat.
    /// </summary>
    [Fact]
    public Task Unwrap_RoundsAHalfTurnTowardsZeroSoAnExactlyPiStepSurvives() => Asserts("""
        q = unwrap([0 pi 2*pi]);
        assert(abs(q(2) - pi) < 1e-12);
        assert(abs(q(3) - 2*pi) < 1e-12);
        """);

    [Fact]
    public Task Unwrap_PassesOverANaNAndTreatsItsNeighboursAsAdjacent() => Asserts("""
        q = unwrap([0 3.1 NaN 9.3]);
        assert(q(1) == 0);
        assert(abs(q(2) - 3.1) < 1e-12);
        assert(isnan(q(3)));
        assert(abs(q(4) - (9.3 - 2*pi)) < 1e-12);
        """);

    [Fact]
    public Task Unwrap_RunsDownColumnsByDefaultAndAlongTheNamedDimension() => Asserts("""
        assert(isequal(size(unwrap([0 0; 3.1 3.1; 6.2 6.2])), [3 2]));

        down = unwrap([0 4; 8 12], [], 1);
        assert(abs(down(2,1) - (8 - 2*pi)) < 1e-12);

        across = unwrap([0 4; 8 12], [], 2);
        assert(abs(across(1,2) - (4 - 2*pi)) < 1e-12);
        assert(across(2,1) == 8);
        """);

    [Fact]
    public Task Unwrap_KeepsAScalarAndARowAsTheyWere() => Asserts("""
        assert(isequal(unwrap(5), 5));
        assert(isequal(size(unwrap(5)), [1 1]));
        assert(isequal(size(unwrap([0; 3.1; 6.2])), [3 1]));
        """);

    [Fact]
    public Task Cplxpair_PutsEachConjugatePairTogetherWithTheNegativeImaginaryPartFirst() => Asserts("""
        b = cplxpair([1+1i, 1-1i, 2]);
        assert(imag(b(1)) < 0);
        assert(imag(b(2)) > 0);
        assert(b(3) == 2);
        """);

    [Fact]
    public Task Cplxpair_OrdersPairsByRealPartAndPutsTheRealValuesLast() => Asserts("""
        b = cplxpair([2, 1-1i, 1+1i, 3, -1+2i, -1-2i]);
        assert(abs(b(1) - (-1-2i)) < 1e-12);
        assert(abs(b(2) - (-1+2i)) < 1e-12);
        assert(abs(b(3) - (1-1i)) < 1e-12);
        assert(abs(b(4) - (1+1i)) < 1e-12);
        assert(b(5) == 2);
        assert(b(6) == 3);
        """);

    /// <summary>
    /// Values sharing a real part form one group, and within it the pair furthest from the real axis
    /// comes first — which is the case a group of exactly two cannot distinguish.
    /// </summary>
    [Fact]
    public Task Cplxpair_OrdersOneGroupOutermostPairFirst() => Asserts("""
        b = cplxpair([1+2i 1-2i 1+1i 1-1i]);
        assert(abs(b(1) - (1-2i)) < 1e-12);
        assert(abs(b(2) - (1+2i)) < 1e-12);
        assert(abs(b(3) - (1-1i)) < 1e-12);
        assert(abs(b(4) - (1+1i)) < 1e-12);
        """);

    [Fact]
    public Task Cplxpair_SortsAPurelyRealSetAscending() => Asserts("""
        assert(isequal(cplxpair([3 1 2]), [1 2 3]));
        assert(isequal(cplxpair(3), 3));
        assert(isequal(size(cplxpair([])), [0 0]));
        """);

    [Fact]
    public Task Cplxpair_KeepsTheInputsOrientationAndTakesADimension() => Asserts("""
        assert(isequal(size(cplxpair([1+1i; 1-1i; 2])), [3 1]));
        assert(isequal(size(cplxpair([1+1i 1-1i 2])), [1 3]));

        m = cplxpair([1+1i 1-1i; 2+2i 2-2i], [], 2);
        assert(isequal(size(m), [2 2]));
        assert(imag(m(1,1)) < 0);
        assert(imag(m(2,1)) < 0);
        """);

    [Fact]
    public Task Cplxpair_RefusesAValueWithNoPartner() => Refuses(
        "cplxpair([1+1i, 2]);", "MATLAB:cplxpair:ComplexValuesPaired");

    [Fact]
    public Task Cplxpair_RefusesAToleranceOutsideItsRange() => Refuses(
        "cplxpair([1+1i, 1-1i], 2);", "MATLAB:cplxpair:WrongTolerance");

    // --- polyarea, rectint, inpolygon ---------------------------------------------------------------

    [Fact]
    public Task Polyarea_AnswersTheSameAreaWhicheverWayTheBoundaryWinds() => Asserts("""
        assert(abs(polyarea([0 1 1 0], [0 0 1 1]) - 1) < 1e-12);
        assert(abs(polyarea([0 0 1 1], [0 1 1 0]) - 1) < 1e-12);
        assert(abs(polyarea([0 4 4 0], [0 0 3 3]) - 12) < 1e-12);
        assert(abs(polyarea([0 1 0.5], [0 0 1]) - 0.5) < 1e-12);
        """);

    [Fact]
    public Task Polyarea_TakesAColumnPerPolygonAndAcceptsADimension() => Asserts("""
        a = polyarea([0 0; 1 0; 1 1; 0 1], [0 0; 0 0; 1 1; 1 1]);
        assert(isequal(size(a), [1 2]));
        assert(abs(a(1) - 1) < 1e-12);
        assert(a(2) == 0);
        assert(abs(polyarea([0 1 1 0], [0 0 1 1], 2) - 1) < 1e-12);
        """);

    [Fact]
    public Task Polyarea_RefusesMismatchedCoordinates() => Refuses(
        "polyarea([0 1], [0 1 2]);", "MATLAB:polyarea:XYSizeMismatch");

    [Fact]
    public Task Rectint_AnswersARowPerRectangleOfTheFirstSet() => Asserts("""
        out = rectint([0 0 2 2; 1 1 2 2], [1 1 2 2]);
        assert(isequal(size(out), [2 1]));
        assert(out(1) == 1);
        assert(out(2) == 4);
        assert(rectint([0 0 1 1], [5 5 1 1]) == 0);
        """);

    [Fact]
    public Task Rectint_TablesEveryPairing() => Asserts("""
        out = rectint([0 0 2 2], [1 1 2 2; 0 0 1 1; 3 3 1 1]);
        assert(isequal(size(out), [1 3]));
        assert(isequal(out, [1 1 0]));
        """);

    [Fact]
    public Task Inpolygon_CountsTheBoundaryAsInsideAndReportsItSeparately() => Asserts("""
        [in, on] = inpolygon([0.5 0 2 1 0.5], [0.5 0 2 0 1], [0 1 1 0], [0 0 1 1]);
        assert(islogical(in));
        assert(isequal(in, logical([1 1 0 1 1])));
        assert(isequal(on, logical([0 1 0 1 1])));
        """);

    [Fact]
    public Task Inpolygon_KeepsTheQueryPointsShape() => Asserts("""
        [in, on] = inpolygon([0.5 2; 0 1], [0.5 2; 0 1], [0 1 1 0], [0 0 1 1]);
        assert(isequal(size(in), [2 2]));
        assert(isequal(size(on), [2 2]));
        assert(isequal(in, logical([1 0; 1 1])));

        [c, ~] = inpolygon([0.5; 2], [0.5; 2], [0 1 1 0], [0 0 1 1]);
        assert(isequal(size(c), [2 1]));
        """);

    [Fact]
    public Task Inpolygon_TakesEveryCornerAndEveryEdgeMidpointAsOnTheBoundary() => Asserts("""
        [in, on] = inpolygon([0 1 1 0], [0 0 1 1], [0 1 1 0], [0 0 1 1]);
        assert(all(in));
        assert(all(on));

        [in2, on2] = inpolygon([0.5 1 0.5 0], [0 0.5 1 0.5], [0 1 1 0], [0 0 1 1]);
        assert(all(in2));
        assert(all(on2));
        """);

    [Fact]
    public Task Inpolygon_DoesNotCareWhetherTheBoundaryWasAlreadyClosedOrHowItWinds() => Asserts("""
        assert(inpolygon(0.5, 0.5, [0 1 1 0 0], [0 0 1 1 0]));
        assert(inpolygon(0.5, 0.5, [0 0 1 1], [0 1 1 0]));
        assert(inpolygon(0.5, 0.5, [0 1 1 0]', [0 0 1 1]'));
        """);

    /// <summary>Two loops separated by a NaN are disjoint, not joined by a phantom edge.</summary>
    [Fact]
    public Task Inpolygon_TakesNanSeparatedLoopsAsSeparatePolygons() => Asserts("""
        in = inpolygon([0.5 2.5 1.5], [0.5 0.5 0.5], ...
                       [0 1 1 0 NaN 2 3 3 2], [0 0 1 1 NaN 0 0 1 1]);
        assert(isequal(in, logical([1 1 0])));
        """);

    [Fact]
    public Task Inpolygon_AnswersFalseForAQueryThatIsNotANumber() => Asserts("""
        [in, on] = inpolygon([NaN 0.5], [0.5 NaN], [0 1 1 0], [0 0 1 1]);
        assert(~any(in));
        assert(~any(on));
        """);

    [Fact]
    public Task Inpolygon_RefusesAPolygonThatIsNotGivenAsVectors() => Refuses(
        "inpolygon(0.5, 0.5, [0 1; 1 0], [0 0; 1 1]);", "MATLAB:inpolygon:PolygonVecDef");

    // --- the names together -------------------------------------------------------------------------

    /// <summary>
    /// <c>conv</c> multiplies, <c>deconv</c> divides, and <c>roots</c> and <c>poly</c> invert each
    /// other — the round trip is the check that the coefficient convention is the same throughout.
    /// </summary>
    [Fact]
    public Task ThePolynomialNamesAgreeWithEachOtherOnARoundTrip() => Asserts("""
        a = [1 -3 2];
        b = [1 4];
        product = conv(a, b);
        [q, r] = deconv(product, b);
        assert(all(abs(q - a) < 1e-12));
        assert(all(abs(r) < 1e-12));

        back = poly(roots(a));
        assert(all(abs(back - a) < 1e-12));

        assert(abs(polyval(polyint(polyder(a)), 0) - 0) < 1e-12);
        """);
}
