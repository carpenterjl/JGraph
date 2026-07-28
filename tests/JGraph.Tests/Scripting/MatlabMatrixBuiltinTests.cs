using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The matrix shape questions and the linear algebra M36 left out (M38). Each factorization is
/// checked by reassembling the matrix it came from, which is the property that actually matters and
/// which a transposed or mis-permuted factor cannot fake.
/// </summary>
[Collection("JG facade")]
public class MatlabMatrixBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMatrixBuiltinTests() => JG.Reset();

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

    [Fact]
    public Task ShapePredicates_ReadTheStructureTheyAreNamedFor() => RunAsserting("""
        L = [1 0 0; 2 3 0; 4 5 6];
        D = [1 0 0; 0 2 0; 0 0 3];
        S = [1 2; 2 1];
        assert(istril(L));
        assert(~istriu(L));
        assert(istriu(L'));
        assert(isdiag(D));
        assert(istril(D) && istriu(D));
        assert(issymmetric(S));
        assert(ishermitian(S));
        assert(~issymmetric([1 2; 3 4]));
        """);

    [Fact]
    public Task Bandwidth_MeasuresHowFarTheNonZerosReach() => RunAsserting("""
        A = [1 2 0; 3 4 5; 0 6 7];
        assert(bandwidth(A) == 1);
        assert(bandwidth(A, 'lower') == 1);
        assert(bandwidth(A, 'upper') == 1);
        [lo, up] = bandwidth([1 0 0; 2 3 0; 4 5 6]);
        assert(lo == 2);
        assert(up == 0);
        assert(isbanded(A, 1, 1));
        assert(~isbanded(A, 0, 1));
        """);

    [Fact]
    public Task TrilAndTriu_SplitAMatrixAtItsDiagonal() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 9];
        assert(isequal(tril(A) + triu(A) - diag(diag(A)), A));
        assert(isequal(tril(A, -1), [0 0 0; 4 0 0; 7 8 0]));
        assert(isequal(triu(A, 1), [0 2 3; 0 0 6; 0 0 0]));
        """);

    [Fact]
    public Task Cholesky_ReassemblesThePositiveDefiniteMatrix() => RunAsserting("""
        A = [4 2; 2 3];
        R = chol(A);
        assert(istriu(R));
        assert(norm(R' * R - A) < 1e-12);

        L = chol(A, 'lower');
        assert(istril(L));
        assert(norm(L * L' - A) < 1e-12);

        threw = false;
        try
            chol([1 2; 2 1]);   % indefinite: no Cholesky factor exists
        catch
            threw = true;
        end
        assert(threw);
        """);

    [Fact]
    public Task Ldl_ReassemblesASymmetricMatrix() => RunAsserting("""
        A = [4 2 1; 2 5 3; 1 3 6];
        [L, D] = ldl(A);
        assert(norm(L * D * L' - A) < 1e-12);
        assert(isdiag(D));

        [L2, D2, P] = ldl(A);
        assert(norm(P * A * P' - L2 * D2 * L2') < 1e-12);
        """);

    [Fact]
    public Task Hessenberg_IsASimilarityThatClearsTheSubdiagonal() => RunAsserting("""
        A = [1 2 3 4; 5 6 7 8; 9 10 11 12; 13 14 15 17];
        [Q, H] = hess(A);
        assert(norm(Q * H * Q' - A) < 1e-10);
        assert(norm(Q' * Q - eye(4)) < 1e-12);

        % Hessenberg form is zero below the first subdiagonal, which is bandwidth 1 downward.
        assert(bandwidth(H, 'lower') <= 1);
        """);

    [Fact]
    public Task Expm_IsTheMatrixExponentialNotTheElementwiseOne() => RunAsserting("""
        % A diagonal matrix is the case where the two agree, and it pins the scaling.
        D = expm([0 0; 0 1]);
        assert(abs(D(1)(1) - 1) < 1e-13);
        assert(abs(D(2)(2) - exp(1)) < 1e-13);

        % A nilpotent matrix has an exact answer: e^N = I + N.
        N = [0 1; 0 0];
        assert(norm(expm(N) - [1 1; 0 1]) < 1e-14);

        % e^A · e^-A is the identity for any A.
        A = [1 2; 3 4];
        assert(norm(expm(A) * expm(-A) - eye(2)) < 1e-9);
        """);

    [Fact]
    public Task SolversAndConditioning_AgreeWithTheBackslashOperator() => RunAsserting("""
        A = [4 3; 6 3];
        b = [1; 2];
        assert(norm(linsolve(A, b) - A \ b) < 1e-13);

        % rcond is 1 for the identity and drops toward 0 as a matrix nears singularity.
        assert(abs(rcond(eye(3)) - 1) < 1e-14);
        assert(rcond([1 2; 2 4.0001]) < 1e-4);
        assert(rcond([1 2; 2 4]) == 0);
        """);

    [Fact]
    public Task Subspaces_SpanWhatTheirNamesClaim() => RunAsserting("""
        A = [1 2; 2 4];   % rank 1, so a one-dimensional null space and range
        n = null(A);
        assert(norm(A * n) < 1e-12);
        assert(abs(norm(n) - 1) < 1e-12);

        q = orth(A);
        assert(abs(norm(q) - 1) < 1e-12);

        % The pseudoinverse satisfies A·A⁺·A = A even where A has no inverse.
        assert(norm(A * pinv(A) * A - A) < 1e-10);
        assert(norm(pinv([4 3; 6 3]) - inv([4 3; 6 3])) < 1e-10);
        """);

    [Fact]
    public Task CrossAndVecnorm_MatchTheirDefinitions() => RunAsserting("""
        assert(isequal(cross([1 0 0], [0 1 0]), [0 0 1]));
        assert(isequal(cross([0 1 0], [1 0 0]), [0 0 -1]));
        assert(vecnorm([3 4]) == 5);
        assert(vecnorm([3 4], 1) == 7);
        assert(vecnorm([3 4], Inf) == 4);
        assert(isequal(vecnorm([3 0; 4 5]), [5 5]));
        """);
}
