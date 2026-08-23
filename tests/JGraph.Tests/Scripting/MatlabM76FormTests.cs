using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The numeric forms M76 added at the script level: the pencil <c>eig</c> takes, the shapes and
/// words the decompositions accept, the transforms' length and dimension, <c>filter</c>'s carried
/// state, and the two geometry questions asked in space.
/// </summary>
/// <remarks>
/// Assertions live inside the scripts, as the neighbouring suites do, and are written as identities
/// rather than as expected matrices — a factorization has many correct answers differing in sign and
/// order, and pinning one of them tests the rounding rather than the algebra.
/// </remarks>
[Collection("JG facade")]
public class MatlabM76FormTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabM76FormTests() => JG.Reset();

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
        Assert.False(result.Success, "expected a refusal, got success");
        Assert.Contains(fragment, _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    // --- the crash that was not a refusal ---------------------------------------------------

    /// <summary>
    /// The wave's first finding: these two ended the process rather than the statement, because the
    /// numeric layer's <c>ArgumentException</c> passed every catch between it and <c>Main</c>.
    /// </summary>
    [Fact]
    public Task TheFormsThatUsedToEndTheProcess_AreOrdinaryCatchableErrors() => RunAsserting("""
        caught = 0;
        try
            qr([1 2 3; 4 5 6], 0);
            x = [1 2 3] * 0;   % unreachable if the line above threw
        catch
            caught = caught + 1;
        end

        try
            linsolve([1 2 3; 4 5 6], [1 2 3]);
        catch
            caught = caught + 1;
        end

        assert(caught == 1, 'qr of a wide matrix answers now, and linsolve refuses catchably');
        """);

    // --- eig --------------------------------------------------------------------------------

    [Fact]
    public Task EigOfAPencil_SatisfiesItsDefinition() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 10];
        B = [2 0 1; 0 3 0; 1 0 4];
        e = eig(A, B);
        assert(numel(e) == 3);
        assert(max(abs(sort(e) - sort(eig(B \ A)))) < 1e-8);

        [V, D] = eig(A, B);
        assert(max(max(abs(A * V - B * V * D))) < 1e-8);
        """);

    [Fact]
    public Task EigOfASingularPencil_AnswersInfinity() => RunAsserting("""
        e = eig(eye(2), [1 0; 0 0]);
        assert(sum(isinf(e)) == 1, 'one eigenvalue is at infinity');
        assert(abs(min(e) - 1) < 1e-9, 'and the other is 1');
        """);

    [Fact]
    public Task EigOfASymmetricDefinitePair_IsRealAndAscending() => RunAsserting("""
        A = [2 1; 1 3];
        B = [4 1; 1 5];
        [V, D] = eig(A, B);
        assert(max(max(abs(A * V - B * V * D))) < 1e-9);
        assert(max(max(abs(V' * B * V - eye(2)))) < 1e-9, 'the chol path normalizes against B');
        d = diag(D);
        assert(d(1) <= d(2));
        assert(isreal(d));
        """);

    [Fact]
    public Task EigLeftVectors_SatisfyTheirOwnDefinition() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 10];
        [V, D, W] = eig(A);
        assert(max(max(abs(A * V - V * D))) < 1e-8);
        assert(max(max(abs(W' * A - D * W'))) < 1e-8);
        """);

    [Fact]
    public Task EigWords_ChooseTheShapeAndTheAlgorithm() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(size(eig(A, 'matrix')), [2 2]));
        assert(numel(eig(A, 'vector')) == 2);
        assert(max(abs(sort(eig(A, 'nobalance')) - sort(eig(A)))) < 1e-12);

        B = [2 0; 0 3];
        assert(max(abs(sort(eig(A, B, 'qz')) - sort(eig(B \ A)))) < 1e-8);
        """);

    [Theory]
    [InlineData("eig([1 2; 3 4], 'sideways');", "is not one of")]
    [InlineData("eig([1 2; 3 4], 'chol');", "only one was given")]
    [InlineData("eig([1 2; 3 4], eye(2), 'balance');", "balances a single matrix")]
    [InlineData("[V, D] = eig(eye(2), [1 0; 0 0]);", "eigenvector")]
    public Task EigRefusesWhatItCannotMean(string code, string fragment) =>
        RunExpectingError(code, fragment);

    // --- qr, lu, chol, linsolve -------------------------------------------------------------

    [Fact]
    public Task QrOfAnyShape_ReproducesItsMatrix() => RunAsserting("""
        W = [1 2 3; 4 5 6];
        [Q, R] = qr(W);
        assert(max(max(abs(Q * R - W))) < 1e-12);
        assert(isequal(size(R), [2 3]));
        assert(max(max(abs(Q * Q' - eye(2)))) < 1e-12);

        T = [1 2; 3 4; 5 6];
        [Qe, Re] = qr(T, 0);
        assert(isequal(size(Qe), [3 2]) && isequal(size(Re), [2 2]));
        assert(max(max(abs(Qe * Re - T))) < 1e-12);
        """);

    [Fact]
    public Task QrWithPivoting_OrdersItsDiagonal() => RunAsserting("""
        A = [1 90 2; 0 40 3; 1 10 4];
        [Q, R, P] = qr(A);
        assert(max(max(abs(A * P - Q * R))) < 1e-10);
        d = abs(diag(R));
        assert(all(d(1:end-1) >= d(2:end) - 1e-12));

        [~, ~, p] = qr(A, 'vector');
        assert(isequal(sort(p), [1 2 3]));
        assert(p(1) == 2, 'the largest column comes first');
        """);

    [Fact]
    public Task QrOfAPair_SolvesTheLeastSquaresProblem() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 10];
        b = [1; 2; 3];
        [C, R] = qr(A, b);
        assert(max(abs(R \ C - A \ b)) < 1e-9);
        """);

    [Fact]
    public Task LuAnswersItsPermutationsBothWays() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 10];
        [L, U, P] = lu(A);
        assert(max(max(abs(P * A - L * U))) < 1e-12);

        [~, ~, p] = lu(A, 'vector');
        assert(isequal(sort(p), [1 2 3]));

        [L2, U2, P2, Q2, D2] = lu(A);
        assert(max(max(abs(P2 * A * Q2 - L2 * U2 * D2))) < 1e-12);
        """);

    [Fact]
    public Task CholReportsWhereItStopped() => RunAsserting("""
        [R, flag] = chol([4 2 1; 2 3 1; 1 1 -5]);
        assert(flag == 3, 'the leading 2-by-2 is definite and the third order is not');
        assert(isequal(size(R), [2 2]));

        [R2, f2] = chol([4 2; 2 3]);
        assert(f2 == 0);
        assert(max(max(abs(R2' * R2 - [4 2; 2 3]))) < 1e-12);

        [~, f3, P3] = chol([4 2; 2 3]);
        assert(f3 == 0 && isequal(size(P3), [2 2]));

        L = chol([4 2; 2 3], 'lower');
        assert(max(max(abs(L * L' - [4 2; 2 3]))) < 1e-12);
        """);

    [Fact]
    public Task LinsolveReadsItsOptionsAndReportsItsQuality() => RunAsserting("""
        A = [4 1; 1 3];
        b = [1; 2];
        [x, r] = linsolve(A, b);
        assert(max(abs(A * x - b)) < 1e-12);
        assert(abs(r - rcond(A)) < 1e-12);

        L = [2 0; 1 3];
        assert(max(abs(L * linsolve(L, b, struct('LT', true)) - b)) < 1e-12);
        assert(max(abs(A' * linsolve(A, b, struct('TRANSA', true)) - b)) < 1e-12);
        """);

    [Theory]
    [InlineData("qr([1 2; 3 4], 'bogus');", "is not one of")]
    [InlineData("lu([1 2; 3 4], 5);", "between 0 and 1")]
    [InlineData("chol([1 0; 0 -1]);", "positive definite")]
    [InlineData("linsolve([1 2; 3 4], [1; 2], struct('NOPE', true));", "is not one of")]
    [InlineData("linsolve([1 2; 3 4], [1; 2], 5);", "structure of options")]
    public Task TheDecompositionsRefuseByName(string code, string fragment) =>
        RunExpectingError(code, fragment);

    // --- transforms -------------------------------------------------------------------------

    [Fact]
    public Task FftOfAMatrix_WalksItsColumns() => RunAsserting("""
        A = [1 2; 3 4];
        assert(max(max(abs(fft(A) - [fft(A(:,1)) fft(A(:,2))]))) < 1e-12);
        assert(max(max(abs(fft(A, [], 2) - [fft(A(1,:)); fft(A(2,:))]))) < 1e-12);
        assert(isequal(size(fft(A, 5)), [5 2]));
        """);

    [Fact]
    public Task TheTransformsRoundTrip_IncludingTheTwoDimensionalOnes() => RunAsserting("""
        x = [1 2 3 4];
        A = [1 2; 3 4];
        assert(max(abs(ifft(fft(x)) - x)) < 1e-12);

        % ifft2(fft2(A)) could not be written before M76: the reader refused a complex matrix.
        assert(max(max(abs(ifft2(fft2(A)) - A))) < 1e-12);
        assert(max(max(abs(ifftn(fftn(A)) - A))) < 1e-12);
        assert(isequal(size(fftn(A, [4 4])), [4 4]));
        assert(isequal(size(fft2(A, 3, 3)), [3 3]));
        """);

    [Fact]
    public Task TheSymmetricInverse_IsReal() => RunAsserting("""
        y = fft([1 2 3 4]);
        assert(max(abs(imag(ifft(y, 'symmetric')))) == 0);
        assert(max(abs(ifft(y, 'symmetric') - [1 2 3 4])) < 1e-12);
        """);

    [Fact]
    public Task FftshiftMovesEveryDimensionOrTheNamedOne() => RunAsserting("""
        assert(isequal(fftshift([1 2 3 4]), [3 4 1 2]));
        assert(isequal(fftshift([1 2; 3 4]), [4 3; 2 1]));
        assert(isequal(fftshift([1 2; 3 4], 2), [2 1; 4 3]));
        assert(isequal(ifftshift(fftshift([1 2; 3 4])), [1 2; 3 4]));
        """);

    [Fact]
    public Task FilterCarriesItsStateBetweenCalls() => RunAsserting("""
        b = [1 0.5];
        a = [1 -0.3];
        x = [1 2 3 4];
        whole = filter(b, a, x);

        [y1, zf] = filter(b, a, x(1:2));
        y2 = filter(b, a, x(3:4), zf);
        assert(max(abs([y1 y2] - whole)) < 1e-12, 'filtering in pieces equals filtering whole');

        A = [1 2; 3 4];
        assert(max(max(abs(filter(b, a, A) - [filter(b,a,A(:,1)) filter(b,a,A(:,2))]))) < 1e-12);
        assert(max(max(abs(filter(b, a, A, [], 2) - [filter(b,a,A(1,:)); filter(b,a,A(2,:))]))) < 1e-12);
        """);

    [Fact]
    public Task CastCopiesAPrototypesClass() => RunAsserting("""
        assert(strcmp(class(cast(300.7, 'like', int8(0))), 'int8'));
        assert(cast(300.7, 'like', int8(0)) == 127, 'and saturates as that class does');
        assert(islogical(cast(1, 'like', true)));
        assert(strcmp(class(cast(2.5, 'like', single(0))), 'single'));
        assert(strcmp(class(cast(2.5, 'int16')), 'int16'));
        """);

    [Fact]
    public Task TheSparseConstructorsTakeTheirDocumentedShapes() => RunAsserting("""
        assert(isequal(size(speye([2 3])), [2 3]));
        assert(isequal(full(speye(2)), eye(2)));

        S = spalloc(3, 4, 5);
        assert(issparse(S) && nnz(S) == 0 && isequal(size(S), [3 4]));
        """);

    [Theory]
    [InlineData("filter([1 0.5], [1 -0.3], [1 2 3], [1 2 3]);", "one entry per filter delay")]
    [InlineData("ifft([1 2 3], 'sideways');", "symmetric")]
    [InlineData("fft([1 2 3], 2, 0);", "positive whole number")]
    [InlineData("cast(1, 'nope', 2);", "follows 'like'")]
    public Task TheTransformsRefuseByName(string code, string fragment) =>
        RunExpectingError(code, fragment);

    // --- geometry ---------------------------------------------------------------------------

    [Fact]
    public Task ConvhullAnswersInThePlaneAndInSpace() => RunAsserting("""
        x = [0 1 1 0 0.5];
        y = [0 0 1 1 0.5];
        assert(isequal(convhull(x, y), convhull([x' y'])));

        [~, area] = convhull(x, y);
        assert(abs(area - 1) < 1e-12);

        cx = [0 1 0 1 0 1 0 1];
        cy = [0 0 1 1 0 0 1 1];
        cz = [0 0 0 0 1 1 1 1];
        [faces, volume] = convhull(cx, cy, cz);
        assert(isequal(size(faces), [12 3]), 'a cube''s hull is twelve triangles');
        assert(abs(volume - 1) < 1e-12);
        assert(isequal(size(convhull([cx' cy' cz'])), [12 3]));
        """);

    [Fact]
    public Task SimplifyDecidesWhetherAPointOnAnEdgeIsKept() => RunAsserting("""
        x = [0 1 2 2 0];
        y = [0 0 0 2 2];
        assert(numel(convhull(x, y, 'Simplify', true)) == 5);
        assert(numel(convhull(x, y, 'Simplify', false)) == 6, 'the collinear point is a vertex');
        """);

    [Fact]
    public Task DelaunayAnswersInThePlaneAndInSpace() => RunAsserting("""
        x = [0 1 1 0 0.5];
        y = [0 0 1 1 0.5];
        assert(size(delaunay(x, y), 2) == 3);
        assert(isequal(delaunay(x, y), delaunay([x' y'])));

        cx = [0 1 0 1 0 1 0 1];
        cy = [0 0 1 1 0 0 1 1];
        cz = [0 0 0 0 1 1 1 1];
        T = delaunay(cx, cy, cz);
        assert(size(T, 2) == 4, 'a triangulation in space is made of tetrahedra');
        assert(size(T, 1) >= 5);
        """);

    [Theory]
    [InlineData("convhull([0 1 2], [0 1 2], 'Simplyfy', true);", "is not an option")]
    [InlineData("convhull([1 2 3; 4 5 6]);", "at least 4 points")]
    [InlineData("delaunay([0 1 2 3], [0 1 0 1], [0 0 0 0]);", "one plane")]
    public Task TheGeometryVerbsRefuseByName(string code, string fragment) =>
        RunExpectingError(code, fragment);
}
