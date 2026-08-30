using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Schur family and the rank-one updates as scripts see them (M39). The decompositions are
/// checked by reassembly inside the script, because that is the property a caller depends on and it
/// does not fix an arbitrary choice of factor signs.
/// </summary>
[Collection("JG facade")]
public class MatlabSchurBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSchurBuiltinTests() => JG.Reset();

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
    public Task Schur_ReassemblesAndIsOrthogonal() => RunAsserting("""
        A = [4 -2 1; 3 6 -4; 2 1 8];
        [U, T] = schur(A);
        assert(norm(U*T*U' - A) < 1e-10);
        assert(norm(U'*U - eye(3)) < 1e-12);

        % One output is the triangular factor, not the orthogonal one.
        assert(norm(schur(A) - T) < 1e-12);
        """);

    [Fact]
    public Task Schur_KeepsAConjugatePairInATwoByTwoBlock() => RunAsserting("""
        % A rotation has no real eigenvalues, so its real Schur form cannot be triangular.
        theta = pi / 5;
        A = [cos(theta) -sin(theta); sin(theta) cos(theta)];
        [U, T] = schur(A);
        assert(norm(U*T*U' - A) < 1e-13);
        assert(abs(T(2, 1)) > 0.1);

        e = ordeig(T);
        assert(abs(real(e(1)) - cos(theta)) < 1e-13);
        assert(abs(abs(imag(e(1))) - sin(theta)) < 1e-13);
        assert(abs(imag(e(1)) + imag(e(2))) < 1e-14);
        """);

    [Fact]
    public Task Ordeig_ReadsTheDiagonalInBlockOrder() => RunAsserting("""
        T = [1 5 9; 0 2 6; 0 0 3];
        e = ordeig(T);
        assert(isequal(e, [1; 2; 3]));
        """);

    [Fact]
    public Task Ordschur_BringsTheSelectedEigenvaluesToTheTop() => RunAsserting("""
        A = [-3 1 0 2; 0 -1 4 1; 1 0 5 -2; 0 2 1 7];
        [U, T] = schur(A);
        e = ordeig(T);

        % Move everything in the left half plane to the top and check it moved.
        [US, TS] = ordschur(U, T, real(e) < 0);
        assert(norm(US*TS*US' - A) < 1e-9);
        assert(norm(US'*US - eye(4)) < 1e-11);

        f = ordeig(TS);
        assert(real(f(1)) < 0);

        % The region words say the same thing, and the trace is invariant either way.
        [~, TW] = ordschur(U, T, 'lhp');
        assert(abs(trace(TW) - trace(A)) < 1e-10);
        """);

    [Fact]
    public Task Cholupdate_AddsAndSubtractsARankOneTerm() => RunAsserting("""
        A = [4 1 0; 1 5 2; 0 2 6];
        R = chol(A);
        x = [1; 0.5; -0.25];

        R1 = cholupdate(R, x);
        assert(norm(R1'*R1 - (A + x*x')) < 1e-11);

        % '-' undoes it, so the round trip is the factor we started with.
        R2 = cholupdate(R1, x, '-');
        assert(norm(R2'*R2 - A) < 1e-10);
        """);

    [Fact]
    public Task Cholupdate_ReportsALostDowndateThroughItsSecondOutput() => RunAsserting("""
        A = [4 1; 1 5];
        R = chol(A);

        % Subtracting far more than the matrix holds cannot leave a definite result.
        [R1, p] = cholupdate(R, [100; 100], '-');
        assert(p ~= 0);

        % A successful update reports zero, which is how MATLAB says 'it worked'.
        [R2, q] = cholupdate(R, [1; 1]);
        assert(q == 0);
        assert(norm(R2'*R2 - (A + [1 1; 1 1])) < 1e-12);
        """);

    [Fact]
    public Task Qrupdate_MatchesRefactoringTheUpdatedMatrix() => RunAsserting("""
        A = [1 2; 3 4; 5 7];
        [Q, R] = qr(A);
        u = [1; 0; -1];
        v = [0.5; 2];

        [Q1, R1] = qrupdate(Q, R, u, v);
        assert(norm(Q1*R1 - (A + u*v')) < 1e-11);
        assert(norm(Q1'*Q1 - eye(3)) < 1e-11);
        assert(abs(R1(2, 1)) < 1e-11);
        assert(abs(R1(3, 1)) < 1e-11);
        """);

    [Fact]
    public Task Eig_NowAgreesWithTheSchurForm() => RunAsserting("""
        % eig reads its values off the Schur factorization, so the two agree, and both reproduce
        % the trace — which the previous eigenvalue path did not.
        A = [0 -1 2 1; 3 1 0 -2; 1 4 -1 0; 2 0 1 3];
        [U, T] = schur(A);
        assert(abs(sum(real(eig(A))) - trace(A)) < 1e-10);
        assert(abs(sum(real(ordeig(T))) - trace(A)) < 1e-10);

        % The product over the complex values is the determinant. prod does not take complex
        % arrays, so the multiplication is written out.
        e = eig(A);
        p = 1;
        for k = 1:4
            p = p * e(k);
        end
        assert(abs(real(p) - det(A)) < 1e-9);
        assert(abs(imag(p)) < 1e-9);
        """);
}
