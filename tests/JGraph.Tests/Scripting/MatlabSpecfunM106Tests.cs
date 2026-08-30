using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Coordinates and elementary special functions (M106): the four coordinate conversions, the
/// elliptic integrals and functions, the exponential integral, the associated Legendre functions,
/// the two rational approximations, and the assignment problem — plus the repair to the pairwise
/// engine underneath that the conversions needed.
/// </summary>
/// <remarks>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer rather than JGraph's
/// display format. Every number was read off MATLAB R2024a on this machine, and where a defining
/// property exists it is asserted instead of a value — a conversion undone by its inverse, a
/// convergent's own ratio, a matching's total cost.
/// </remarks>
[Collection("JG facade")]
public class MatlabSpecfunM106Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSpecfunM106Tests() => JG.Reset();

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

    // --- the pairwise engine the conversions stand on --------------------------------------------

    [Fact]
    public Task ATwoArgumentBuiltinKeepsTheShapeItWasHanded() => Asserts("""
        assert(isequal(size(atan2(ones(2,3), ones(2,3))), [2 3]));
        assert(isequal(size(hypot(ones(2,3), 2)), [2 3]));
        assert(isequal(size(hypot(2, ones(2,3))), [2 3]));
        assert(isequal(size(atan2(ones(2,3,2), 1)), [2 3 2]));
        assert(isequal(size(idivide(int32(ones(2,3)*7), int32(2))), [2 3]));
        assert(isequal(size(bitand(uint8([1 2; 3 4]), uint8(1))), [2 2]));
        """);

    [Fact]
    public Task ATwoArgumentBuiltinExpandsTheWayTheOperatorsDo() => Asserts("""
        h = hypot([3;0], [0 4]);
        assert(isequal(size(h), [2 2]));
        assert(h(1,1) == 3 && h(1,2) == 5 && h(2,1) == 0 && h(2,2) == 4);
        assert(isequal(size(atan2([1;2;3], [10 20])), [3 2]));
        """);

    // --- cart2pol and pol2cart ------------------------------------------------------------------

    [Fact]
    public Task Cart2PolIsTheAngleAndTheDistance() => Asserts("""
        [th, r] = cart2pol(3, 4);
        assert(abs(th - 0.92729521800161219) < 1e-15 && r == 5);
        [th, r] = cart2pol([1 -1 0], [0 1 -2]);
        assert(isequal(th, [0 2.3561944901923448 -1.5707963267948966]));
        assert(isequal(r, [1 1.4142135623730951 2]));
        """);

    [Fact]
    public Task TheHeightIsAPassengerAndKeepsItsOwnSize() => Asserts("""
        [th, r, z] = cart2pol([1 2], [3 4], 5);
        assert(isequal(size(th), [1 2]) && isequal(size(z), [1 1]) && z == 5);
        [x, y, z] = pol2cart([0 pi/2], [1 2], 7);
        assert(z == 7 && isequal(size(z), [1 1]));
        """);

    [Fact]
    public Task PolarAndCartesianUndoEachOther() => Asserts("""
        x0 = [1 -2 0.5 -0.25]; y0 = [3 0.5 -1 2];
        [th, r] = cart2pol(x0, y0);
        [x1, y1] = pol2cart(th, r);
        assert(max(abs(x1 - x0)) < 1e-14 && max(abs(y1 - y0)) < 1e-14);
        """);

    [Fact]
    public Task AConversionExpandsAndKeepsAMatrixShape() => Asserts("""
        [th, r] = cart2pol([1;2;3], [10 20]);
        assert(isequal(size(th), [3 2]) && isequal(size(r), [3 2]));
        [th, r] = cart2pol(reshape(1:6,2,3), reshape(7:12,2,3));
        assert(isequal(size(th), [2 3]));
        """);

    [Fact]
    public Task AConversionCarriesTheNonFiniteThrough() => Asserts("""
        [th, r] = cart2pol([NaN Inf -Inf], [1 1 1]);
        assert(isnan(th(1)) && th(2) == 0 && abs(th(3) - pi) < 1e-15);
        assert(isnan(r(1)) && isinf(r(2)) && isinf(r(3)));
        """);

    // --- cart2sph and sph2cart ------------------------------------------------------------------

    [Fact]
    public Task SphericalIsTwoAnglesAndARadius() => Asserts("""
        [az, el, r] = cart2sph(1, 2, 3);
        assert(abs(az - 1.1071487177940904) < 1e-15);
        assert(abs(el - 0.93027401411547206) < 1e-15);
        assert(abs(r - 3.7416573867739413) < 1e-15);
        [x, y, z] = sph2cart(0.3, 0.4, 5);
        assert(abs(x - 4.3996158814062856) < 1e-14);
        assert(abs(y - 1.3609606764771571) < 1e-14);
        assert(abs(z - 1.9470917115432527) < 1e-14);
        """);

    [Fact]
    public Task SphericalAndCartesianUndoEachOther() => Asserts("""
        x0 = [1 -2 0.5]; y0 = [3 0.5 -1]; z0 = [-1 2 4];
        [az, el, r] = cart2sph(x0, y0, z0);
        [x1, y1, z1] = sph2cart(az, el, r);
        assert(max(abs(x1 - x0)) < 1e-14);
        assert(max(abs(y1 - y0)) < 1e-14);
        assert(max(abs(z1 - z0)) < 1e-14);
        """);

    [Fact]
    public async Task AConversionRefusesWhatItsOwnArithmeticWould()
    {
        await Refuses("cart2pol(1);", "MATLAB:minrhs");
        await Refuses("cart2pol(1+2i, 3);", "MATLAB:atan2:complexArgument");
        await Refuses("cart2sph(1+1i, 1, 2);", "MATLAB:atan2:complexArgument");
        await Refuses("cart2pol(int32(3), int32(4));", "MATLAB:UndefinedFunction");
        await Refuses("[a,b,c] = cart2pol(1, 2);", "MATLAB:unassignedOutputs");
    }

    // --- ellipke ---------------------------------------------------------------------------------

    [Fact]
    public Task EllipkeIsTheTwoCompleteIntegrals() => Asserts("""
        [K, E] = ellipke([0 0.5 1]);
        assert(abs(K(1) - pi/2) < 1e-15 && abs(E(1) - pi/2) < 1e-15);
        assert(abs(K(2) - 1.8540746773013717) < 1e-15);
        assert(abs(E(2) - 1.3506438810476753) < 1e-15);
        assert(isinf(K(3)) && E(3) == 1);
        """);

    [Fact]
    public Task EllipkeKeepsTheShapeAndTakesATolerance() => Asserts("""
        [K, E] = ellipke(reshape([0 0.1 0.25 0.5 0.75 0.9], 2, 3));
        assert(isequal(size(K), [2 3]) && isequal(size(E), [2 3]));
        [K, E] = ellipke(0.5, 1e-3);
        assert(abs(K - 1.8540488143993357) < 1e-15);
        assert(abs(E - 1.350625041650126) < 1e-15);
        [K, E] = ellipke([]);
        assert(isempty(K) && isempty(E));
        """);

    [Fact]
    public async Task EllipkeRefusesWhatIsOutsideItsDomain()
    {
        await Refuses("ellipke(-0.1);", "MATLAB:ellipke:MOutOfRange");
        await Refuses("ellipke(1.1);", "MATLAB:ellipke:MOutOfRange");
        await Refuses("ellipke(1i);", "MATLAB:ellipke:ComplexInputs");
        await Refuses("ellipke(0.5, -1);", "MATLAB:ellipke:NegativeTolerance");
        await Refuses("ellipke(0.5, [1 2]);", "MATLAB:ellipke:NegativeTolerance");
    }

    // --- ellipj ----------------------------------------------------------------------------------

    [Fact]
    public Task EllipjIsTheThreeJacobiFunctions() => Asserts("""
        [sn, cn, dn] = ellipj(0.7, 0.4);
        assert(abs(sn - 0.6283244887511652) < 1e-15);
        assert(abs(cn - 0.77795137176791895) < 1e-15);
        assert(abs(dn - 0.9176509874316241) < 1e-15);
        assert(abs(sn^2 + cn^2 - 1) < 1e-15);
        assert(abs(0.4*sn^2 + dn^2 - 1) < 1e-15);
        """);

    [Fact]
    public Task EllipjDegeneratesAtBothEndsOfTheRange() => Asserts("""
        [sn, cn, dn] = ellipj(1.2, 1);
        assert(abs(sn - tanh(1.2)) < 1e-15 && abs(cn - 1/cosh(1.2)) < 1e-15 && cn == dn);
        [sn, cn, dn] = ellipj(1.2, 0);
        assert(abs(sn - sin(1.2)) < 1e-15 && abs(cn - cos(1.2)) < 1e-15 && dn == 1);
        """);

    [Fact]
    public Task EllipjSpreadsAScalarAndKeepsTheOtherSize() => Asserts("""
        [sn, cn, dn] = ellipj([0.1 0.2 0.9], 0.5);
        assert(isequal(size(sn), [1 3]));
        [sn, cn, dn] = ellipj(0.3, [0.2 0.8]);
        assert(isequal(size(sn), [1 2]));
        assert(abs(sn(1) - 0.29467633568107182) < 1e-15);
        [sn, cn, dn] = ellipj(reshape([0 0.5 1 2 -1.5], 5, 1), 0.5);
        assert(isequal(size(sn), [5 1]));
        """);

    [Fact]
    public Task ALooseToleranceStopsTheLadderEarlyAndItShows() => Asserts("""
        [sn, cn, dn] = ellipj(2, 0.5, 1e-3);
        assert(abs(sn - 0.99466232535112309) < 1e-14);
        assert(abs(cn - -0.10318361559422416) < 1e-14);
        assert(abs(dn - 0.7108610477889109) < 1e-14);
        """);

    [Fact]
    public async Task EllipjRefusesWhatIsOutsideItsDomain()
    {
        await Refuses("ellipj(1, 1.5);", "MATLAB:ellipj:MOutOfRange");
        await Refuses("ellipj(1, -0.5);", "MATLAB:ellipj:MOutOfRange");
        await Refuses("ellipj([1 2], [1 2 3]);", "MATLAB:ellipj:InputSizeMismatch");
        await Refuses("ellipj(1);", "MATLAB:ellipj:NotEnoughInputs");
        await Refuses("ellipj(1i, 0.5);", "MATLAB:ellipj:ComplexInputs");
    }

    // --- expint ----------------------------------------------------------------------------------

    [Fact]
    public Task ExpintAnswersOnBothSidesOfTheDividingCurve() => Asserts("""
        y = expint([0.1 0.5 1 2 5 10 20 50]);
        assert(abs(y(1) - 1.8229239584193904) < 1e-15);
        assert(abs(y(3) - 0.21938393439552029) < 1e-15);
        assert(abs(y(5) - 0.0011482955912753329) < 1e-17);
        assert(abs(y(8) - 3.7832640295504606e-24) < 1e-38);
        assert(isreal(y));
        """);

    [Fact]
    public Task ExpintLeavesTheRealsOnTheNegativeAxis() => Asserts("""
        y = expint([-0.5 -1 -2]);
        assert(~isreal(y));
        assert(abs(real(y(2)) - -1.8951178163559368) < 1e-14);
        assert(abs(imag(y(2)) - -pi) < 1e-15);
        assert(isinf(expint(0)));
        """);

    [Fact]
    public Task ExpintFallsBetweenBothBranchesAtNaN() => Asserts("""
        assert(expint(NaN) == 0);
        assert(isreal(expint(NaN)));
        assert(isnan(expint(Inf)));
        y = expint([NaN Inf -Inf]);
        assert(y(1) == 0 && imag(y(1)) == 0);
        assert(isnan(real(y(2))) && imag(y(2)) == 0);
        assert(isnan(real(y(3))) && abs(imag(y(3)) + pi) < 1e-15);
        """);

    [Fact]
    public Task ExpintTakesAComplexArgumentAndKeepsTheShape() => Asserts("""
        y = expint([1+1i, 2-3i]);
        assert(abs(real(y(1)) - 0.00028162445198137373) < 1e-16);
        assert(abs(imag(y(1)) - -0.17932453503935891) < 1e-15);
        assert(abs(real(y(2)) - -0.024826207944199655) < 1e-15);
        assert(isequal(size(expint(reshape(1:6,2,3))), [2 3]));
        """);

    // --- legendre --------------------------------------------------------------------------------

    [Fact]
    public Task LegendreStacksEveryOrderDownTheFirstDimension() => Asserts("""
        P = legendre(2, [0 0.5 1]);
        assert(isequal(size(P), [3 3]));
        assert(abs(P(1,1) + 0.5) < 1e-15 && abs(P(3,1) - 3) < 1e-15);
        assert(abs(P(1,2) + 0.125) < 1e-15);
        assert(abs(P(2,2) + 1.299038105676658) < 1e-15);
        assert(P(1,3) == 1 && P(2,3) == 0 && P(3,3) == 0);
        """);

    [Fact]
    public Task LegendreOfDegreeNoughtKeepsTheArgumentsOwnShape() => Asserts("""
        assert(isequal(size(legendre(0, [0 0.5])), [1 2]));
        assert(isequal(legendre(0, [0 0.5]), [1 1]));
        assert(abs(legendre(0, 0.5, 'norm') - 1/sqrt(2)) < 1e-15);
        assert(isequal(size(legendre(2, [0.1;0.2;0.3])), [3 3]));
        assert(isequal(size(legendre(2, reshape(1:6,2,3)/10)), [3 2 3]));
        """);

    [Fact]
    public Task TheThreeScalingsDifferBySignAndSize() => Asserts("""
        u = legendre(3, 0.2);
        s = legendre(3, 0.2, 'sch');
        n = legendre(3, 0.2, 'norm');
        assert(abs(u(1) - -0.28000000000000014) < 1e-15);
        assert(abs(s(1) - -0.28000000000000014) < 1e-15);
        assert(abs(n(1) - -0.523832034148352) < 1e-15);
        assert(abs(u(4) - -14.109060918431107) < 1e-13);
        assert(abs(s(4) - 0.74361280247182415) < 1e-15);
        assert(abs(n(4) - 0.98370727353212151) < 1e-15);
        """);

    [Fact]
    public Task TheEndsOfTheIntervalKeepOnlyTheZerothOrder() => Asserts("""
        assert(isequal(legendre(4, 1), [1;0;0;0;0]));
        assert(isequal(legendre(4, -1), [1;0;0;0;0]));
        assert(isequal(legendre(5, -1), [-1;0;0;0;0;0]));
        """);

    [Fact]
    public Task ARunningProductKeepsAHighDegreeFinite() => Asserts("""
        P = legendre(150, 0.5);
        assert(all(isfinite(P)));
        assert(abs(P(1) - 0.067498298046742219) < 1e-15);
        assert(numel(P) == 151);
        """);

    [Fact]
    public async Task LegendreRefusesADegreeOrAnArgumentItCannotUse()
    {
        await Refuses("legendre(-1, 0.5);", "MATLAB:legendre:InvalidN");
        await Refuses("legendre(2.5, 0.5);", "MATLAB:legendre:InvalidN");
        await Refuses("legendre([1 2], 0.5);", "MATLAB:legendre:InvalidN");
        await Refuses("legendre(2, 1.5);", "MATLAB:legendre:InvalidX");
        await Refuses("legendre(2, 0.5, 'wibble');", "MATLAB:legendre:InvalidNormalize");
        await Refuses("legendre(2, []);", "MATLAB:nonLogicalConditional");
    }

    // --- rat and rats ----------------------------------------------------------------------------

    [Fact]
    public Task RatWritesTheContinuedFractionOut() => Asserts("""
        r = rat(pi);
        assert(ischar(r) && isequal(size(r), [1 18]));
        assert(strcmp(r, '3 + 1/(7 + 1/(16))'));
        assert(strcmp(rat(-pi), '-3 + 1/(-7 + 1/(-16))'));
        assert(strcmp(rat(exp(1)), '3 + 1/(-4 + 1/(2 + 1/(5 + 1/(-2 + 1/(-7)))))'));
        assert(strcmp(rat(0), '0'));
        """);

    [Fact]
    public Task RatAsksedForTwoOutputsAnswersARatio() => Asserts("""
        [N, D] = rat(pi);
        assert(N == 355 && D == 113);
        assert(abs(N/D - pi) < 1e-6);
        [N, D] = rat(0.75);
        assert(N == 3 && D == 4);
        [N, D] = rat(pi, 1e-2);
        assert(N == 22 && D == 7);
        """);

    [Fact]
    public Task ATermIsTheNearestWholeNumberAndMayBeNegative() => Asserts("""
        assert(strcmp(rat(2.5), '3 + 1/(-2)'));
        [N, D] = rat(2.5);
        assert(N == 5 && D == 2);
        """);

    [Fact]
    public Task RatOfAMatrixIsARowOfTextPerElementInStorageOrder() => Asserts("""
        r = rat([1 2; 3 4.5]);
        assert(isequal(size(r), [4 10]));
        assert(strcmp(r(1,:), '1         '));
        assert(strcmp(r(2,:), '3         '));
        assert(strcmp(r(3,:), '2         '));
        assert(strcmp(r(4,:), '5 + 1/(-2)'));
        """);

    [Fact]
    public Task RatCarriesTheNonFiniteThrough() => Asserts("""
        [N, D] = rat([Inf -Inf NaN 0]);
        assert(isequal(N, [1 -1 0 0]));
        assert(isequal(D, [0 0 0 1]));
        r = rat([Inf -Inf NaN 0]);
        assert(strcmp(r(1,:), 'Inf '));
        assert(strcmp(r(2,:), '-Inf'));
        assert(strcmp(r(3,:), 'NaN '));
        """);

    [Fact]
    public Task RatsWritesAColumnAlignedTableOfFractions() => Asserts("""
        s = rats(pi);
        assert(isequal(size(s), [1 14]));
        assert(strcmp(s, '    355/113   '));
        assert(strcmp(rats(pi, 8), ' 355/113 '));
        assert(strcmp(rats(123456789), '   123456789  '));
        t = rats([1 2; 3 4.5]);
        assert(isequal(size(t), [2 28]));
        assert(strcmp(t(2,:), '       3            9/2     '));
        assert(isempty(rats([])));
        """);

    [Fact]
    public async Task RatsRefusesAWidthThatIsNotANumber() =>
        await Refuses("rats(pi, NaN);", "MATLAB:nonaninf");

    // --- matchpairs ------------------------------------------------------------------------------

    [Fact]
    public Task MatchpairsPairsRowsWithColumnsInColumnOrder() => Asserts("""
        C = [10 5 8 9; 7 100 20 4; 5 3 6 2];
        M = matchpairs(C, 10);
        assert(isequal(size(M), [3 2]));
        assert(isequal(M, [3 1; 1 2; 2 4]));
        [M, uR, uC] = matchpairs(C, 10);
        assert(isempty(uR) && isequal(uC, 3));
        assert(isequal(size(uR), [0 1]));
        """);

    [Fact]
    public Task ACheapEnoughRefusalLeavesEverythingUnmatched() => Asserts("""
        C = [10 5 8 9; 7 100 20 4; 5 3 6 2];
        [M, uR, uC] = matchpairs(C, 1);
        assert(isempty(M) && isequal(size(M), [0 2]));
        assert(isequal(uR, [1;2;3]) && isequal(uC, [1;2;3;4]));
        """);

    [Fact]
    public Task APairWorthExactlyWhatLeavingBothOutCostsIsLeftOut() => Asserts("""
        [M, uR, uC] = matchpairs([2 5; 5 2], 1);
        assert(isempty(M));
        assert(isequal(uR, [1;2]) && isequal(uC, [1;2]));
        """);

    [Fact]
    public Task TheGoalMayBeAskedForByPrefixAndEitherWay() => Asserts("""
        C = [10 5 8 9; 7 100 20 4; 5 3 6 2];
        assert(isequal(matchpairs(C, 10, 'max'), [2 2]));
        assert(isequal(matchpairs(C, 10, 'MA'), [2 2]));
        assert(isequal(matchpairs(C, 10, 'min'), matchpairs(C, 10)));
        """);

    [Fact]
    public Task ARectangularCostLeavesTheSurplusSideOut() => Asserts("""
        A = [1 2; 3 4; 5 6];
        [M, uR, uC] = matchpairs(A, 100);
        assert(isequal(M, [1 1; 2 2]) && isequal(uR, 3) && isempty(uC));
        [M, uR, uC] = matchpairs(A', 100);
        assert(isequal(M, [1 1; 2 2]) && isempty(uR) && isequal(uC, 3));
        """);

    [Fact]
    public Task AForbiddenPairingIsNeverChosen() => Asserts("""
        M = matchpairs([1 Inf; Inf 2], 100);
        assert(isequal(M, [1 1; 2 2]));
        [M, uR, uC] = matchpairs([Inf Inf; Inf Inf], 1);
        assert(isempty(M) && isequal(size(M), [0 2]));
        assert(isequal(uR, [1;2]));
        """);

    [Fact]
    public Task TheMatchingCostsWhatNoOtherMatchingBeats() => Asserts("""
        C = [1 3 7 2; 4 2 9 6; 8 5 1 3; 2 7 4 5];
        [M, uR, uC] = matchpairs(C, 6);
        total = 0;
        for k = 1:size(M,1)
            total = total + C(M(k,1), M(k,2));
        end
        total = total + 6*numel(uR) + 6*numel(uC);
        best = Inf;
        p = perms(1:4);
        for k = 1:size(p,1)
            for mask = 0:15
                cost = 0;
                for j = 1:4
                    if bitget(mask, j)
                        cost = cost + 12;
                    else
                        cost = cost + C(p(k,j), j);
                    end
                end
                best = min(best, cost);
            end
        end
        assert(abs(total - best) < 1e-12);
        """);

    [Fact]
    public Task AnEmptyCostHasAnEmptyMatching() => Asserts("""
        [M, uR, uC] = matchpairs(zeros(0,3), 1);
        assert(isequal(size(M), [0 2]) && isempty(uR) && isequal(uC, [1;2;3]));
        [M, uR, uC] = matchpairs(zeros(0,0), 1);
        assert(isequal(size(M), [0 2]));
        """);

    [Fact]
    public async Task MatchpairsRefusesACostItCannotRead()
    {
        await Refuses("matchpairs([1 2], 1, 'wibble');", "MATLAB:matchpairs:InvalidOption");
        await Refuses("matchpairs([1 NaN], 1);", "MATLAB:matchpairs:NonFiniteCost");
        await Refuses("matchpairs([1 2], NaN);", "MATLAB:matchpairs:NonFiniteCostUnmatched");
        await Refuses("matchpairs([1 2], Inf);", "MATLAB:matchpairs:NonFiniteCostUnmatched");
        await Refuses("matchpairs([1 2], [1 2]);", "MATLAB:matchpairs:InvalidCostUnmatched");
        await Refuses("matchpairs(int32([1 2]), 1);", "MATLAB:matchpairs:InvalidCost");
        await Refuses("matchpairs([1 2]);", "MATLAB:minrhs");
    }
}
