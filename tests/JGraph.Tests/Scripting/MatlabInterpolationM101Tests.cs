using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The piecewise-polynomial and interpolation family (M101): <c>spline</c>, <c>pchip</c>,
/// <c>makima</c>, <c>ppval</c>, <c>mkpp</c>, <c>unmkpp</c>, <c>interp1q</c>, <c>interpft</c>,
/// <c>interpn</c>, and the forms <c>interp1</c>, <c>interp2</c> and <c>interp3</c> gained with them.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer and not JGraph's display
/// format. Every number here was read off MATLAB R2024a on this machine. The tolerances are not
/// decoration: a spline's slopes come out of a tridiagonal system solved in a different order from
/// MATLAB's, so the answers agree to a few units in the last place and not to the bit, which
/// ADR 0102 records.
/// </para>
/// <para>
/// The shape assertions carry as much weight as the value ones. A <c>pp</c> structure is six fields
/// and five of them are shape; whether a set of query vectors means a list of points or a whole
/// grid turns on their orientation and not on their size; and a refinement answers in the
/// orientation it was handed, which the one direction it runs along cannot record.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabInterpolationM101Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabInterpolationM101Tests() => JG.Reset();

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

    // --- spline -------------------------------------------------------------------------------------

    [Fact]
    public Task Spline_WithoutQueryPointsAnswersThePiecewisePolynomial() => Asserts("""
        pp = spline([0 1 2 3 4], [0 1 8 27 64]);
        assert(strcmp(pp.form, 'pp'));
        assert(isequal(pp.breaks, [0 1 2 3 4]));
        assert(isequal(size(pp.coefs), [4 4]));
        assert(pp.pieces == 4);
        assert(pp.order == 4);
        assert(pp.dim == 1);
        % Not-a-knot through samples of a cubic is that cubic, so the second piece is (x-1)^3
        % written out: 1, 3, 3, 1.
        assert(max(abs(pp.coefs(2, :) - [1 3 3 1])) < 1e-12);
        """);

    [Fact]
    public Task Spline_IsExactOnTheCubicItWasSampledFrom() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        assert(max(abs(spline(x, y, [0.5 1.5 2.5 3.5]) - [0.125 3.375 15.625 42.875])) < 1e-12);
        % Its breaks are a row whichever way the samples were handed over.
        pp = spline(x(:), y(:));
        assert(isequal(size(pp.breaks), [1 5]));
        """);

    [Fact]
    public Task Spline_TakesTheShapeOfTheQueryAndSortsWhatItWasGiven() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        assert(isequal(size(spline(x, y, [0.5; 1.5])), [2 1]));
        assert(isequal(size(spline(x, y, [1 2; 3 4])), [2 2]));
        % Unsorted samples name the same curve as sorted ones.
        assert(abs(spline([2 0 1 3 4], [8 0 1 27 64], 0.5) - 0.125) < 1e-12);
        """);

    [Fact]
    public Task Spline_WithTwoExtraValuesClampsTheSlopeAtEachEnd() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        pp = spline(x, [0 y 0]);
        % The first piece now starts flat, because the first extra value is the slope there.
        assert(abs(pp.coefs(1, 3)) < 1e-12);
        assert(max(abs(spline(x, [0 y 0], [0.5 1.5]) - [0.017857142857142794 3.910714285714286])) < 1e-12);
        """);

    [Fact]
    public Task Spline_ReadsAMatrixOfValuesAlongItsLastDimension() => Asserts("""
        x = [0 1 2 3 4];
        Y = [0 1 8 27 64; 1 3 2 5 4];
        pp = spline(x, Y);
        assert(pp.dim == 2);
        assert(isequal(size(pp.coefs), [8 4]));
        v = spline(x, Y, [0.5 1.5 2.5]);
        assert(isequal(size(v), [2 3]));
        assert(abs(v(1, 1) - 0.125) < 1e-12);
        assert(abs(v(2, 1) - 3.046875) < 1e-12);
        """);

    [Fact]
    public Task Spline_OfTwoSamplesIsTheLineThroughThem() => Asserts("""
        assert(isequal(spline([1 2], [3 7], [1.25 1.75]), [4 6]));
        """);

    [Fact]
    public Task Spline_ExtrapolatesTheEndPieceRatherThanStopping() => Asserts("""
        x = [0 1 2 3 4];
        v = spline(x, x .^ 3, [-1 5]);
        assert(abs(v(1) + 1) < 1e-10);
        assert(abs(v(2) - 125) < 1e-10);
        """);

    // --- pchip and makima ---------------------------------------------------------------------------

    [Fact]
    public Task Pchip_GivesUpExactnessToStopTheCurveOvershooting() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        v = pchip(x, y, [0.5 1.5 2.5 3.5]);
        assert(max(abs(v - [0.28125 3.4399038461538458 15.640453296703296 42.888392857142861])) < 1e-10);
        % Where a spline is exact on the cubic, pchip is deliberately not.
        assert(abs(v(1) - 0.125) > 1e-6);
        """);

    [Fact]
    public Task Makima_IsAThirdCubicAgainAndNeitherOfTheOtherTwo() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        v = makima(x, y, [0.5 1.5 2.5 3.5]);
        assert(max(abs(v - ...
            [0.0056818181818181213 3.6639610389610389 15.63583467094703 43.073428721910112])) < 1e-10);
        pp = makima(x, y);
        % The slope at the first sample is the weighted mean the modified Akima rule gives, -1.5,
        % and it is a different number from either of the other two rules.
        assert(abs(pp.coefs(1, 3) + 1.5) < 1e-12);
        """);

    [Fact]
    public Task Makima_DoesNotFlattenWhereThreeSamplesHappenToLineUp() => Asserts("""
        % Plain Akima weights both secants by zero here and has to pick something; the modification
        % adds half their magnitude, which leaves the flat data flat and a real turn unflattened.
        assert(isequal(makima([1 2 3 4 5], [2 2 2 2 2], [1.5 3.5]), [2 2]));
        assert(abs(makima([1 2 3 4], [1 4 9 16], 2.5) - 6.2395833333333339) < 1e-12);
        """);

    [Fact]
    public Task Pchip_AndMakima_BothCarryOnPastTheirLastSample() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        assert(abs(pchip(x, y, 5) - 113.21428571428572) < 1e-9);
        assert(abs(makima(x, y, 5) - 100.98735955056179) < 1e-9);
        """);

    // --- mkpp, unmkpp and ppval ---------------------------------------------------------------------

    [Fact]
    public Task Mkpp_ReshapesWhateverItIsHandedIntoOneRowPerPiece() => Asserts("""
        pp = mkpp([0 1 2], [1 2 3; 4 5 6]);
        assert(pp.pieces == 2);
        assert(pp.order == 3);
        assert(pp.dim == 1);
        assert(isequal(pp.coefs, [1 2 3; 4 5 6]));
        % Each piece is read in its own local variable, so the second starts again from its break.
        assert(isequal(ppval(pp, [0.25 1.25 2.5 -1]), [3.5625 7.5 22.5 2]));
        """);

    [Fact]
    public Task Mkpp_WithADimensionAnswersThatManyNumbersAtEachPoint() => Asserts("""
        pp = mkpp([0 1 2], [1 2; 3 4; 5 6; 7 8], 2);
        assert(pp.dim == 2);
        assert(pp.pieces == 2);
        assert(pp.order == 2);
        assert(isequal(ppval(pp, 0.5), [2.5; 5.5]));
        assert(isequal(size(ppval(pp, [0.5 1.5])), [2 2]));
        """);

    [Fact]
    public Task Unmkpp_TakesAPiecewisePolynomialApartIntoItsFiveParts() => Asserts("""
        pp = mkpp([0 1 2], [1 2; 3 4; 5 6; 7 8], 2);
        [breaks, coefs, L, order, dim] = unmkpp(pp);
        assert(isequal(breaks, [0 1 2]));
        assert(isequal(coefs, [1 2; 3 4; 5 6; 7 8]));
        assert(L == 2);
        assert(order == 2);
        assert(dim == 2);
        """);

    [Fact]
    public Task Ppval_TakesTheShapeOfTheQueryAndCarriesOnPastTheBreaks() => Asserts("""
        pp = spline([0 1 2 3 4], [0 1 8 27 64]);
        assert(isequal(size(ppval(pp, [1; 2; 3])), [3 1]));
        assert(isequal(size(ppval(pp, [1 2; 3 3.5])), [2 2]));
        v = ppval(pp, [-1 5]);
        assert(abs(v(1) + 1) < 1e-10);
        assert(abs(v(2) - 125) < 1e-10);
        """);

    [Fact]
    public Task Ppval_RefusesAStructThatIsNotAPiecewisePolynomial() =>
        Refuses("ppval(struct('a', 1), 1);", "MATLAB:nonExistentField");

    [Fact]
    public Task Unmkpp_RefusesAnythingThatIsNotAStructure() =>
        Refuses("unmkpp([1 2 3]);", "MATLAB:unmkpp:InputArrayNotPP");

    // --- interp1's piecewise form and its cubic family -----------------------------------------------

    [Fact]
    public Task Interp1_WithPpHandsBackTheCurveInsteadOfReadingIt() => Asserts("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        pp = interp1(x, y, 'linear', 'pp');
        assert(pp.order == 2);
        assert(pp.pieces == 4);
        % Each straight piece is a slope and the value it starts from.
        assert(isequal(pp.coefs, [1 0; 7 1; 19 8; 37 27]));
        """);

    [Fact]
    public Task Interp1_NearestAsAPiecewisePolynomialBreaksHalfwayBetweenTheSamples() => Asserts("""
        x = [0 1 2 3 4];
        pp = interp1(x, x .^ 3, 'nearest', 'pp');
        assert(isequal(pp.breaks, [0 0.5 1.5 2.5 3.5 4]));
        assert(pp.order == 1);
        assert(pp.pieces == 5);
        assert(isequal(pp.coefs, [0; 1; 8; 27; 64]));
        assert(isequal(ppval(pp, [0.4 0.6 3.6]), [0 1 64]));
        """);

    [Fact]
    public Task Interp1_WithPpRefusesTheThreeMethodsThatHaveNoPiecewiseForm() => Asserts("""
        x = [1 2 3 4 5];
        y = [1 3 2 5 4];
        ids = {};
        for m = {'previous', 'next', 'makima'}
            try
                interp1(x, y, m{1}, 'pp');
                ids{end + 1} = 'none';
            catch err
                ids{end + 1} = err.identifier;
            end
        end
        assert(strcmp(ids{1}, 'MATLAB:interp1:ppGriddedInterpolantPrevious'));
        assert(strcmp(ids{2}, 'MATLAB:interp1:ppGriddedInterpolantNext'));
        assert(strcmp(ids{3}, 'MATLAB:interp1:ppAkima'));
        """);

    [Fact]
    public Task Interp1_WithPpNeedsExactlyFourArguments() =>
        Refuses("interp1([1 2 3], [1 2 3], 'pp');", "MATLAB:interp1:ppOutput");

    [Fact]
    public Task Interp1_CubicIsCubicConvolutionAndNotTheShapePreservingOne() => Asserts("""
        x = [0 1 2 3 4];
        w = [1 3 2 5 4];
        % 'cubic' and 'v5cubic' name the same kernel, which is not what pchip computes.
        assert(interp1(x, w, 2.3, 'cubic') == interp1(x, w, 2.3, 'v5cubic'));
        assert(abs(interp1(x, w, 2.3, 'cubic') - 2.732) < 1e-12);
        assert(interp1(x, w, 2.3, 'cubic') ~= interp1(x, w, 2.3, 'pchip'));
        % Cubic convolution reproduces a cubic on an even grid, which is worth pinning because it is
        % the property that tells the kernel apart from a wrong one.
        assert(abs(interp1(x, x .^ 3, 2.5, 'cubic') - 15.625) < 1e-12);
        """);

    [Fact]
    public Task Interp1_CubicHasNoExtrapolationToOfferAndSaysSo() => Asserts("""
        x = [0 1 2 3 4];
        w = [1 3 2 5 4];
        % The kernel is written over a cell and there is no cell outside the samples, so 'extrap'
        % answers NaN here where the three slope-based cubics carry their end piece on.
        assert(isnan(interp1(x, w, 5, 'cubic')));
        assert(isnan(interp1(x, w, 5, 'cubic', 'extrap')));
        assert(~isnan(interp1(x, w, 5, 'makima')));
        assert(~isnan(interp1(x, w, 5, 'pchip')));
        """);

    // --- interp1q -----------------------------------------------------------------------------------

    [Fact]
    public Task Interp1q_ReadsColumnsAndFillsOutsideWithNaN() => Asserts("""
        x = [0; 1; 2; 3; 4];
        y = [0; 1; 8; 27; 64];
        v = interp1q(x, y, [0.5; 1.5; 5; -1]);
        assert(isequal(size(v), [4 1]));
        assert(v(1) == 0.5);
        assert(v(2) == 4.5);
        assert(isnan(v(3)));
        assert(isnan(v(4)));
        % A matrix of values gives one column of answers per column of them.
        assert(isequal(interp1q(x, [y 2 * y], [0.5; 1.5]), [0.5 1; 4.5 9]));
        """);

    [Fact]
    public Task Interp1q_RefusesAnythingThatIsNotAColumn() =>
        Refuses("interp1q([1 2 3], [1 2 3], [1.5 2.5]);", "MATLAB:catenate:dimensionMismatch");

    // --- interpft -----------------------------------------------------------------------------------

    [Fact]
    public Task Interpft_ReadsARecordAtMoreOrFewerPlacesOverTheSamePeriod() => Asserts("""
        y = interpft([1 2 3 4], 8);
        assert(isequal(size(y), [1 8]));
        assert(max(abs(y - [1 1.0857864376269049 2 2.5 3 3.914213562373095 4 2.5])) < 1e-12);
        % Reading at as many places as were recorded gives the record back.
        assert(max(abs(interpft([1 2 3 4], 4) - [1 2 3 4])) < 1e-12);
        % Fewer places folds the high frequencies back rather than dropping them.
        assert(max(abs(interpft([1 2 3 4], 2) - [1 3])) < 1e-12);
        """);

    [Fact]
    public Task Interpft_KeepsItsOrientationAndAnswersRealForARealRecord() => Asserts("""
        assert(isequal(size(interpft([1 2 3 4]', 8)), [8 1]));
        assert(isreal(interpft([1 2 3 4], 8)));
        assert(isequal(size(interpft([1 2; 3 4; 5 6], 6)), [6 2]));
        % Along a direction it is only one sample long, the record is a constant.
        assert(isequal(interpft([1 2 3 4], 3, 1), [1 2 3 4; 1 2 3 4; 1 2 3 4]));
        """);

    [Fact]
    public Task Interpft_TakesTheDirectionItWasToldTo() => Asserts("""
        y = interpft([1 2; 3 4; 5 6], 4, 2);
        assert(isequal(size(y), [3 4]));
        assert(max(max(abs(y - [1 1.5 2 1.5; 3 3.5 4 3.5; 5 5.5 6 5.5]))) < 1e-12);
        """);

    // --- interp2, interp3 and interpn ---------------------------------------------------------------

    [Fact]
    public Task Interp2_WithNothingButSamplesRefinesTheGridOnce() => Asserts("""
        assert(isequal(interp2([1 2; 3 4]), [1 1.5 2; 2 2.5 3; 3 3.5 4]));
        assert(isequal(size(interp2([1 2; 3 4], 2)), [5 5]));
        assert(isequal(interp2([1 2; 3 4], 0), [1 2; 3 4]));
        assert(isequal(interp2([1 2; 3 4], 1, 'nearest'), [1 2 2; 3 4 4; 3 4 4]));
        """);

    [Fact]
    public Task Interp2_ReadsFourMethodsAndRefusesTheFifthByName() => Asserts("""
        x = 1:5;
        V = [1 3 2 5 4; 2 1 6 3 7; 5 4 3 8 2; 3 9 1 4 6; 7 2 8 1 5];
        assert(abs(interp2(x, x, V, 2.3, 3.7, 'linear') - 5.73) < 1e-12);
        assert(interp2(x, x, V, 2.3, 3.7, 'nearest') == 9);
        assert(abs(interp2(x, x, V, 2.3, 3.7, 'cubic') - 6.6325432500000012) < 1e-10);
        assert(abs(interp2(x, x, V, 2.3, 3.7, 'spline') - 5.570278484375) < 1e-12);
        refused = '';
        try
            interp2(x, x, V, 2.3, 3.7, 'makima');
        catch err
            refused = err.message;
        end
        assert(contains(refused, 'makima'));
        """);

    [Fact]
    public Task Interp2_FillsOutsideWithNaNUnlessTheMethodIsSplineOrItWasTold() => Asserts("""
        x = 1:5;
        V = [1 3 2 5 4; 2 1 6 3 7; 5 4 3 8 2; 3 9 1 4 6; 7 2 8 1 5];
        assert(isnan(interp2(x, x, V, 9, 9, 'linear')));
        assert(isnan(interp2(x, x, V, 9, 9, 'cubic')));
        assert(interp2(x, x, V, 9, 9, 'linear', -7) == -7);
        assert(~isnan(interp2(x, x, V, 9, 9, 'spline')));
        % A stated value beats the spline's own continuation.
        assert(interp2(x, x, V, 9, 9, 'spline', 0) == 0);
        """);

    [Fact]
    public Task Interp2_TellsAListOfPointsFromAGridByOrientationAndNotBySize() => Asserts("""
        V = [1 2; 3 4];
        % A row of x against a column of y names every combination of them.
        assert(isequal(interp2(V, [1.5 2], [1.5; 2]), [2.5 3; 3.5 4]));
        % Two arrays of the same shape are a list of points, read one at a time.
        assert(isequal(interp2(V, [1.5 2], [1.5 2]), [2.5 4]));
        """);

    [Fact]
    public Task Interp2_RefusesQueryArraysThatAreNeitherOfThose() =>
        Refuses(
            "interp2([1 2; 3 4], [1.5 2 1], [1.5 2]);",
            "MATLAB:griddedInterpolant:InputMixSizeErrId");

    [Fact]
    public Task Interp2_RefusesAMethodThatNamesNothing() =>
        Refuses(
            "interp2([1 2; 3 4], 1.5, 1.5, 'quadratic');",
            "MATLAB:griddedInterpolant:BadInterpTypeErrId");

    [Fact]
    public Task Interp2_NeedsTwoSamplesAlongEveryDirection() =>
        Refuses(
            "interp2([1 2; 3 4], 1, 2, 3, 4);",
            "MATLAB:griddedInterpolant:DegenerateGridErrId");

    [Fact]
    public Task Interp3_RefinesAndReadsTheSameWayTwoDimensionsDo() => Asserts("""
        V = reshape(1:27, 3, 3, 3);
        assert(isequal(size(interp3(V)), [5 5 5]));
        assert(isequal(size(interp3(V, 2)), [9 9 9]));
        assert(interp3(V, 1.5, 1.5, 1.5) == 7.5);
        [X, Y, Z] = meshgrid(1:3, 1:3, 1:3);
        F = X + 10 * Y + 100 * Z;
        assert(interp3(X, Y, Z, F, 1.5, 2.5, 1.5) == 176.5);
        assert(interp3(X, Y, Z, F, 5, 5, 5, 'linear', -7) == -7);
        assert(isnan(interp3(X, Y, Z, F, 5, 5, 5, 'linear')));
        assert(abs(interp3(X, Y, Z, F, 5, 5, 5, 'spline') - 555) < 1e-9);
        """);

    [Fact]
    public Task Interp3_RefusesACallThatMatchesNoForm() =>
        Refuses("interp3(reshape(1:8, 2, 2, 2), 1.5, 1.5);", "MATLAB:interp3:nargin");

    [Fact]
    public Task Interpn_NumbersItsDirectionsTheWayTheArrayDoesAndNotTheWayMeshgridDoes() => Asserts("""
        x = 1:5;
        V = [1 3 2 5 4; 2 1 6 3 7; 5 4 3 8 2; 3 9 1 4 6; 7 2 8 1 5];
        % interp2 takes x first and y second; interpn takes them in the array's own order, so the
        % same reading is asked for with the two swapped.
        assert(interpn(x, x, V, 3.7, 2.3, 'linear') == interp2(x, x, V, 2.3, 3.7, 'linear'));
        assert(interpn(x, x, V, 3.7, 2.3, 'spline') == interp2(x, x, V, 2.3, 3.7, 'spline'));
        """);

    [Fact]
    public Task Interpn_WorksOutHowManyDirectionsItHasFromWhatItWasHanded() => Asserts("""
        A = [1 2 3; 4 5 6];
        assert(isequal(size(interpn(A)), [3 5]));
        assert(isequal(size(interpn(A, 2)), [5 9]));
        assert(interpn(A, 1.5, 2.5) == 4);
        assert(interpn([1 2], [1 2 3], A, 1.5, 2.5) == 4);
        assert(interpn(reshape(1:8, 2, 2, 2), 1.5, 1.5, 1.5) == 4.5);
        % A refinement answers in the orientation it was handed.
        assert(isequal(size(interpn([1 4 9 16], 2)), [1 13]));
        """);

    [Fact]
    public Task Interpn_ReadsCrossedVectorsAsAGridAndMatchingOnesAsAList() => Asserts("""
        A = [1 2 3; 4 5 6];
        assert(isequal(interpn([1 2], [1 2 3], A, [1.5; 2], [2.5 3]), [4 4.5; 5.5 6]));
        assert(isequal(interpn([1 2], [1 2 3], A, [1.5 2], [2.5 3]), [4 6]));
        assert(interpn([1 2], [1 2 3], A, 5, 5, 'linear', -3) == -3);
        """);

    [Fact]
    public Task Interpn_RefusesAGridVectorOfTheWrongLength() =>
        Refuses(
            "interpn([1 2 3], [1 2], 1.5);",
            "MATLAB:griddedInterpolant:CompVecValueMismatchErrId");
}
