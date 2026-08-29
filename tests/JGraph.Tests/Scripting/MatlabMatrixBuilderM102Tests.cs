using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The matrix builders and shape verbs of <c>elmat</c> (M102): <c>toeplitz</c>, <c>hankel</c>,
/// <c>blkdiag</c>, <c>compan</c>, <c>vander</c>, <c>hadamard</c>, <c>pascal</c>, <c>rosser</c>,
/// <c>wilkinson</c>, <c>invhilb</c>, <c>gallery</c>, <c>repelem</c>, <c>shiftdim</c>,
/// <c>ipermute</c> and <c>flipdim</c>.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer and not JGraph's display
/// format. Every number here was read off MATLAB R2024a on this machine.
/// </para>
/// <para>
/// Where a matrix has a defining property, the property is asserted and not only the entries: a
/// Hadamard matrix's columns are orthogonal, a binomial matrix squares to a multiple of the
/// identity, <c>pascal(n,2)</c> cubes to the identity, and an involutory matrix is its own inverse.
/// A table of numbers can be copied wrongly and still look right; a property cannot.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabMatrixBuilderM102Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMatrixBuilderM102Tests() => JG.Reset();

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

    // --- toeplitz and hankel ------------------------------------------------------------------------

    [Fact]
    public Task ToeplitzOfOneVectorIsSymmetric() => Asserts("""
        T = toeplitz([1 2 3 4]);
        assert(isequal(T, [1 2 3 4; 2 1 2 3; 3 2 1 2; 4 3 2 1]));
        assert(isequal(T, T'));
        """);

    [Fact]
    public Task ToeplitzTakesItsShapeFromBothArguments() => Asserts("""
        T = toeplitz([1 2 3], [1 4 5 6]);
        assert(isequal(size(T), [3 4]));
        assert(isequal(T, [1 4 5 6; 2 1 4 5; 3 2 1 4]));
        """);

    [Fact]
    public Task ToeplitzConjugatesBelowTheDiagonal() => Asserts("""
        % One argument is the ROW; the column is its conjugate, so a complex vector gives a
        % Hermitian matrix and not a symmetric one.
        T = toeplitz([1 2+3i 4]);
        assert(T(1,2) == 2+3i);
        assert(T(2,1) == 2-3i);
        assert(isequal(T, T'));
        """);

    [Fact]
    public Task ToeplitzGivesTheColumnTheCorner() => Asserts("""
        % The two arguments disagree about (1,1); MATLAB warns and keeps the column's.
        T = toeplitz([1 2 3], [9 4 5]);
        assert(T(1,1) == 1);
        """);

    [Fact]
    public Task HankelIsConstantAlongItsAntiDiagonals() => Asserts("""
        H = hankel([1 2 3 4]);
        assert(isequal(H, [1 2 3 4; 2 3 4 0; 3 4 0 0; 4 0 0 0]));
        H2 = hankel([1 2 3], [3 4 5 6]);
        assert(isequal(H2, [1 2 3 4; 2 3 4 5; 3 4 5 6]));
        """);

    [Fact]
    public Task HankelOfNothingIsNothing() => Asserts("""
        assert(isequal(size(hankel([])), [0 0]));
        """);

    // --- blkdiag, compan, vander --------------------------------------------------------------------

    [Fact]
    public Task BlkdiagLaysTheBlocksCornerToCorner() => Asserts("""
        B = blkdiag([1 2; 3 4], 5);
        assert(isequal(B, [1 2 0; 3 4 0; 0 0 5]));
        R = blkdiag(ones(2,3), 7*ones(1,2));
        assert(isequal(size(R), [3 5]));
        assert(R(3,4) == 7 && R(1,4) == 0);
        """);

    [Fact]
    public Task BlkdiagDropsAnEmptyBlockEntirely() => Asserts("""
        B = blkdiag([], [1 2]);
        assert(isequal(size(B), [1 2]));
        assert(isequal(B, [1 2]));
        assert(isequal(size(blkdiag()), [0 0]));
        """);

    [Fact]
    public Task CompanHasThePolynomialsRootsForEigenvalues() => Asserts("""
        A = compan([1 0 -7 6]);
        assert(isequal(A, [0 7 -6; 1 0 0; 0 1 0]));
        e = sort(eig(A));
        assert(max(abs(e(:)' - [-3 1 2])) < 1e-12);
        % A leading coefficient other than one divides through.
        assert(isequal(compan([2 4 6]), [-2 -3; 1 0]));
        % One coefficient is a polynomial of degree zero: no companion at all.
        assert(isequal(size(compan(5)), [0 0]));
        """);

    [Fact]
    public Task CompanRefusesAnythingThatIsNotAVector() => Refuses(
        "compan([1 2; 3 4]);", "MATLAB:compan:NeedVectorInput");

    [Fact]
    public Task VanderDescendsThePowersAcrossEachRow() => Asserts("""
        A = vander([1 2 3 4]);
        assert(isequal(A, [1 1 1 1; 8 4 2 1; 27 9 3 1; 64 16 4 1]));
        % Its shape follows the count and not the orientation.
        assert(isequal(vander([1;2;3]), vander([1 2 3])));
        assert(vander(7) == 1);
        """);

    // --- hadamard, pascal, rosser, wilkinson, invhilb ------------------------------------------------

    [Fact]
    public Task HadamardHasOrthogonalColumnsAtEveryOrderItReaches() => Asserts("""
        for n = [1 2 4 8 12 20 24 40]
            H = hadamard(n);
            assert(isequal(size(H), [n n]));
            assert(all(abs(H(:)) == 1));
            assert(isequal(H' * H, n * eye(n)));
        end
        assert(isequal(hadamard(4), [1 1 1 1; 1 -1 1 -1; 1 1 -1 -1; 1 -1 -1 1]));
        """);

    [Fact]
    public Task HadamardRefusesAnOrderItCannotReach() => Refuses(
        "hadamard(3);", "MATLAB:hadamard:InvalidInput");

    [Fact]
    public Task PascalsThreeFormsAreTheFactorisationTheyClaimToBe() => Asserts("""
        P = pascal(4);
        assert(isequal(P, [1 1 1 1; 1 2 3 4; 1 3 6 10; 1 4 10 20]));
        L = pascal(4,1);
        assert(isequal(L, [1 0 0 0; 1 -1 0 0; 1 -2 1 0; 1 -3 3 -1]));
        % The lower-triangular form is a square root of the symmetric one.
        assert(isequal(L * L', P));
        % The third form is a cube root of the identity, at both parities of n.
        for n = [4 5 6 7]
            C = pascal(n,2);
            assert(max(max(abs(C^3 - eye(n)))) < 1e-9);
        end
        assert(isequal(pascal(4,2), [-1 -1 -1 -1; 3 2 1 0; -3 -1 0 0; 1 0 0 0]));
        assert(isequal(size(pascal(0)), [0 0]));
        """);

    [Fact]
    public Task PascalRefusesAKindItDoesNotHave() => Refuses(
        "pascal(4,3);", "MATLAB:pascal:InvalidArg2");

    [Fact]
    public Task RosserAnswersWithoutBeingCalled() => Asserts("""
        A = rosser;
        assert(isequal(size(A), [8 8]));
        assert(isequal(A, A'));
        assert(A(1,1) == 611 && A(7,8) == -911);
        % Its point: a double eigenvalue at 1000, and two more a twentieth apart above it.
        e = sort(eig(A));
        assert(abs(e(4) - 1000) < 1e-9 && abs(e(5) - 1000) < 1e-9);
        assert(abs(e(7) - 1020) < 1e-9 && abs(e(8) - 1020.049018) < 1e-6);
        """);

    [Fact]
    public Task WilkinsonIsTridiagonalWithAValleyDownTheDiagonal() => Asserts("""
        W = wilkinson(7);
        % reshape and not a transpose: JGraph's diag answers a row where MATLAB answers a column,
        % which is a divergence of its own and older than this milestone.
        assert(isequal(reshape(diag(W), 1, 7), [3 2 1 0 1 2 3]));
        assert(all(diag(W,1) == 1) && all(diag(W,-1) == 1));
        % An even order puts the valley between two places, so the diagonal is half-integral.
        assert(isequal(reshape(diag(wilkinson(8)), 1, 8), [3.5 2.5 1.5 0.5 0.5 1.5 2.5 3.5]));
        assert(wilkinson(1) == 0);
        assert(isequal(size(wilkinson(0)), [0 0]));
        """);

    [Fact]
    public Task InvhilbIsTheExactInverseWhileTheIntegersStillFit() => Asserts("""
        H = invhilb(5);
        assert(isequal(H, H'));
        assert(H(1,1) == 25 && H(1,2) == -300 && H(2,2) == 4800);
        assert(all(all(H == round(H))));
        assert(max(max(abs(hilb(5) * H - eye(5)))) < 1e-9);
        assert(invhilb(1) == 1);
        """);

    [Fact]
    public Task InvhilbRefusesAClassItCannotHold() => Refuses(
        "invhilb(3,'int32');", "MATLAB:invhilb:notSupportedClass");

    [Fact]
    public Task ABuilderAnswersInTheClassItIsAsked() => Asserts("""
        assert(strcmp(class(hadamard(4,'int8')), 'int8'));
        assert(strcmp(class(pascal(3,'single')), 'single'));
        assert(strcmp(class(pascal(3,1,'int32')), 'int32'));
        assert(strcmp(class(rosser('single')), 'single'));
        assert(strcmp(class(invhilb(3,'single')), 'single'));
        assert(isequal(double(pascal(3,1,'int32')), pascal(3,1)));
        """);

    // --- repelem, shiftdim, ipermute, flipdim --------------------------------------------------------

    [Fact]
    public Task RepelemRepeatsEachElementWhereItStands() => Asserts("""
        assert(isequal(repelem([1 2 3], 3), [1 1 1 2 2 2 3 3 3]));
        % A count per element, rather than one for all of them.
        assert(isequal(repelem([1 2 3], [1 2 3]), [1 2 2 3 3 3]));
        % Orientation survives.
        assert(isequal(repelem([1;2], 2), [1;1;2;2]));
        assert(isequal(size(repelem([1 2 3], 0)), [1 0]));
        """);

    [Fact]
    public Task RepelemTakesOneCountPerDirection() => Asserts("""
        assert(isequal(repelem([1 2; 3 4], 2, 3), ...
            [1 1 1 2 2 2; 1 1 1 2 2 2; 3 3 3 4 4 4; 3 3 3 4 4 4]));
        % A vector of counts along one direction and a scalar along the other.
        assert(isequal(repelem([1 2; 3 4], [1 2], 2), [1 1 2 2; 3 3 4 4; 3 3 4 4]));
        % Past two directions, and over a cell.
        B = repelem(reshape(1:4,1,2,2), 2, 1, 2);
        assert(isequal(size(B), [2 2 4]));
        assert(isequal(B(:,:,1), [1 2; 1 2]) && isequal(B(:,:,3), [3 4; 3 4]));
        c = repelem({1,'a'}, 1, 2);
        assert(iscell(c) && numel(c) == 4 && strcmp(c{3}, 'a'));
        """);

    [Fact]
    public Task RepelemNeedsTheThreeInputFormForAMatrix() => Refuses(
        "repelem([1 2; 3 4], 2);", "MATLAB:repelem:twoInputNonVector");

    [Fact]
    public Task ShiftdimRotatesTheDirectionsAndComesRoundAgain() => Asserts("""
        A = reshape(1:6, 1, 2, 3);
        assert(isequal(shiftdim(A, 1), [1 3 5; 2 4 6]));
        % A shift as long as the rank is a shift of none, so four is the same as one.
        assert(isequal(shiftdim(A, 4), shiftdim(A, 1)));
        assert(isequal(shiftdim([1 2 3], 0), [1 2 3]));
        % A negative shift puts singleton directions in front instead of taking them away.
        assert(isequal(size(shiftdim([1 2 3], -2)), [1 1 1 3]));
        """);

    [Fact]
    public Task ShiftdimWithNoCountStripsTheLeadingSingletons() => Asserts("""
        [B, m] = shiftdim(reshape(1:6, 1, 1, 2, 3));
        assert(m == 2 && isequal(size(B), [2 3]));
        [B2, m2] = shiftdim([1 2 3]);
        assert(m2 == 1 && isequal(size(B2), [3 1]));
        % Nothing to strip, and nothing under the ones of a scalar to promote.
        [~, m3] = shiftdim([1 2; 3 4]);
        [~, m4] = shiftdim(5);
        assert(m3 == 0 && m4 == 0);
        """);

    [Fact]
    public Task IpermuteUndoesWhatPermuteDid() => Asserts("""
        A = reshape(1:6, 1, 2, 3);
        assert(isequal(size(ipermute(A, [2 3 1])), [3 1 2]));
        assert(isequal(ipermute(permute(A, [2 3 1]), [2 3 1]), A));
        assert(isequal(ipermute([1 2; 3 4], [2 1]), [1 3; 2 4]));
        """);

    [Fact]
    public Task FlipdimReversesAlongAnyDirection() => Asserts("""
        assert(isequal(flipdim([1 2 3; 4 5 6], 2), [3 2 1; 6 5 4]));
        % Past the second direction, where flip used to answer with the columns instead.
        A = reshape(1:8, 2, 2, 2);
        F = flipdim(A, 3);
        assert(isequal(F(:,:,1), A(:,:,2)) && isequal(F(:,:,2), A(:,:,1)));
        assert(isequal(flip(A, 3), F));
        % A direction with one place along it reverses to itself.
        assert(isequal(flipdim([1 2 3], 1), [1 2 3]));
        """);

    // --- gallery ------------------------------------------------------------------------------------

    [Fact]
    public Task GalleryAnswersItsTwoNumberedMatrices() => Asserts("""
        assert(isequal(gallery(3), [-149 -50 -154; 537 180 546; -27 -9 -25]));
        A = gallery(5);
        assert(isequal(size(A), [5 5]));
        assert(A(1,1) == -9 && A(5,5) == 24572);
        """);

    [Fact]
    public Task GalleryRefusesANumberItDoesNotHave() => Refuses(
        "gallery(4);", "MATLAB:gallery:invalidN");

    [Fact]
    public Task GalleryRefusesANameItDoesNotKnow() => Refuses(
        "gallery('nosuchmatrix', 3);", "MATLAB:gallery:invalidMatName");

    [Fact]
    public Task GalleryFamiliesHaveThePropertiesTheyAreNamedFor() => Asserts("""
        % binomial squares to a multiple of the identity.
        for n = [4 5 7]
            B = gallery('binomial', n);
            assert(max(max(abs(B*B - 2^(n-1)*eye(n)))) < 1e-6);
        end
        % invol is its own inverse.
        A = gallery('invol', 4);
        assert(max(max(abs(A*A - eye(4)))) < 1e-6);
        % chebspec with no boundary condition is nilpotent and kills the constant vector.
        C = gallery('chebspec', 5);
        assert(max(abs(C * ones(5,1))) < 1e-14);
        % orthog's positive kinds are orthogonal.
        for k = [1 2 4 5 6 7]
            Q = gallery('orthog', 5, k);
            assert(max(max(abs(Q'*Q - eye(5)))) < 1e-12);
        end
        % lehmer, minij and gcdmat are symmetric positive definite.
        for name = {'lehmer', 'minij', 'gcdmat'}
            M = gallery(name{1}, 5);
            assert(isequal(M, M'));
            assert(min(eig(M)) > 0);
        end
        % clement's eigenvalues are the integers it advertises.
        e = sort(eig(gallery('clement', 5)));
        assert(max(abs(e(:)' - [-4 -2 0 2 4])) < 1e-9);
        % sampling's eigenvalues are 0 .. n-1.
        s = sort(eig(gallery('sampling', 4)));
        assert(max(abs(s(:)' - [0 1 2 3])) < 1e-9);
        """);

    [Fact]
    public Task GalleryFamiliesMatchTheEntriesMatlabGives() => Asserts("""
        assert(isequal(gallery('frank', 5), [5 4 3 2 1; 4 4 3 2 1; 0 3 3 2 1; 0 0 2 2 1; 0 0 0 1 1]));
        assert(isequal(gallery('circul', 4), [1 2 3 4; 4 1 2 3; 3 4 1 2; 2 3 4 1]));
        assert(isequal(gallery('chow', 4), [1 1 0 0; 1 1 1 0; 1 1 1 1; 1 1 1 1]));
        assert(isequal(gallery('grcar', 5), ...
            [1 1 1 1 0; -1 1 1 1 1; 0 -1 1 1 1; 0 0 -1 1 1; 0 0 0 -1 1]));
        assert(isequal(gallery('jordbloc', 3), [1 1 0; 0 1 1; 0 0 1]));
        assert(isequal(gallery('minij', 4), [1 1 1 1; 1 2 2 2; 1 2 3 3; 1 2 3 4]));
        assert(isequal(gallery('moler', 4), [1 -1 -1 -1; -1 2 0 0; -1 0 3 1; -1 0 1 4]));
        assert(isequal(gallery('triw', 4), [1 -1 -1 -1; 0 1 -1 -1; 0 0 1 -1; 0 0 0 1]));
        assert(isequal(gallery('redheff', 6), ...
            [1 1 1 1 1 1; 1 1 0 1 0 1; 1 0 1 0 0 1; 1 0 0 1 0 0; 1 0 0 0 1 0; 1 0 0 0 0 1]));
        assert(isequal(gallery('riemann', 5), ...
            [1 -1 1 -1 1; -1 2 -1 -1 2; -1 -1 3 -1 -1; -1 -1 -1 4 -1; -1 -1 -1 -1 5]));
        assert(isequal(gallery('dramadah', 6), ...
            [1 1 0 1 0 0; 0 1 1 0 1 0; 0 0 1 1 0 1; 1 0 0 1 1 0; 1 1 0 0 1 1; 0 1 1 0 0 1]));
        assert(isequal(gallery('invhess', 5), ...
            [1 -1 -1 -1 -1; 1 2 -2 -2 -2; 1 2 3 -3 -3; 1 2 3 4 -4; 1 2 3 4 5]));
        assert(isequal(gallery('gearmat', 5), ...
            [0 1 0 0 1; 1 0 1 0 0; 0 1 0 1 0; 0 0 1 0 1; -1 0 0 1 0]));
        assert(isequal(gallery('leslie', 4), [1 1 1 1; 1 0 0 0; 0 1 0 0; 0 0 1 0]));
        assert(isequal(gallery('lauchli', 3), [1 1 1; sqrt(eps) 0 0; 0 sqrt(eps) 0; 0 0 sqrt(eps)]));
        assert(isequal(gallery('wilk', 21), wilkinson(21)));
        """);

    [Fact]
    public Task GalleryReadsAScalarAsTheRangeUpToIt() => Asserts("""
        assert(isequal(gallery('cauchy', 4), gallery('cauchy', 1:4)));
        assert(isequal(gallery('fiedler', 5), gallery('fiedler', 1:5)));
        assert(isequal(gallery('circul', 4), gallery('circul', 1:4)));
        assert(isequal(gallery('sampling', 4), gallery('sampling', 1:4)));
        """);

    [Fact]
    public Task GalleryTakesTheQuarterTurnsExactly() => Asserts("""
        % Roots of unity are turned in degrees, so a quarter turn is exactly i and not near it.
        S = gallery('smoke', 4);
        assert(S(1,1) == 1i && S(3,3) == -1i && S(2,2) == -1);
        assert(S(4,1) == 1);
        assert(gallery('smoke', 4, 1)(4,1) == 0);
        Q = gallery('orthog', 4, 3);
        assert(Q(2,2) == 0.5i);
        % And the Chebyshev grid, which is why chebspec's entries are the exact rationals.
        assert(isequal(gallery('chebspec', 4)(1,:), [19/6 -4 4/3 -1/2]));
        """);

    [Fact]
    public Task GalleryHandsBackItsSecondAndThirdOutputs() => Asserts("""
        [v, beta, s] = gallery('house', [3;1;2]);
        assert(abs(s + sqrt(14)) < 1e-14);
        H = eye(3) - beta * (v * v');
        % H is a reflection and it sends x onto the first axis.
        assert(max(max(abs(H'*H - eye(3)))) < 1e-14);
        assert(max(abs(H * [3;1;2] - [s;0;0])) < 1e-13);
        % x already on the axis gives back the identity, spelled as a zero vector.
        [vz, bz, sz] = gallery('house', [0;0;0]);
        assert(all(vz == 0) && bz == 1 && sz == 0);
        [A, d] = gallery('ipjfact', 3);
        assert(isequal(A, [2 6 24; 6 24 120; 24 120 720]));
        assert(d == 576);
        [W, b] = gallery('wilk', 3);
        assert(isequal(size(W), [3 3]) && isequal(b, [0;0;1]));
        """);

    [Fact]
    public Task GalleryTakesAClassNameAfterItsParameters() => Asserts("""
        assert(strcmp(class(gallery('lehmer', 4, 'single')), 'single'));
        assert(strcmp(class(gallery('binomial', 4, 'single')), 'single'));
        % 'single' is a class; every other trailing word belongs to the family. Rounding to it is
        % a real narrowing, so the two are close and not equal.
        assert(max(max(abs(double(gallery('lehmer', 4, 'single')) - gallery('lehmer', 4)))) < 1e-7);
        """);

    [Fact]
    public Task GalleryRefusesAnOddOrderForHanowa() => Refuses(
        "gallery('hanowa', 5);", "MATLAB:hanowa:OddN");

    [Fact]
    public Task GalleryRefusesTheFamiliesItCannotReproduce() => Asserts("""
        % A family drawn from a random stream would be a different matrix under the same name, so
        % it is refused rather than substituted; the message says which reason applies (ADR 0103).
        for name = {'rando', 'randsvd', 'qmult', 'cycol', 'wathen', 'poisson', 'tridiag'}
            failed = false;
            try
                gallery(name{1}, 4);
            catch err
                failed = true;
                assert(~isempty(strfind(err.message, 'not available here')));
            end
            assert(failed, name{1});
        end
        % condex's fourth kind is refused for its own reason, and it is the default.
        failed = false;
        try
            gallery('condex', 5);
        catch
            failed = true;
        end
        assert(failed);
        assert(isequal(size(gallery('condex', 5, 3)), [5 5]));
        """);
}
