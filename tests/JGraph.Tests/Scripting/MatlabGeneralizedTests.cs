using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M66 wave D: <c>balance</c> and the generalized Schur pair <c>qz</c> and <c>ordqz</c>.
/// </summary>
/// <remarks>
/// A factorization is tested by its relations rather than by its entries, and here that is the only
/// honest way to test it: the Schur basis of a matrix is not unique — any sign flip of a column gives
/// another correct one — so an entry-by-entry expectation would be asserting one arbitrary choice.
/// What is not arbitrary is that <c>Q·A·Z</c> is <c>AA</c>, that <c>Q</c> and <c>Z</c> are orthogonal,
/// and that <c>BB</c> is upper triangular, and those are what these check.
/// </remarks>
[Collection("JG facade")]
public class MatlabGeneralizedTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabGeneralizedTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Error(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return result.Message + _output.ErrorText;
    }

    // --- balance --------------------------------------------------------------------------------

    [Fact]
    public void BalanceHandsBackASimilarityAndTheMatrixItProduces()
    {
        Assert.Equal("1\n", RunAndRead("""
            A = [1 1e6 0; 1e-6 1 1e-6; 0 1e6 1];
            [T, B] = balance(A);
            fprintf('%d\n', max(max(abs(T \ A * T - B))) < 1e-9);
            """));
    }

    [Fact]
    public void BalancingRecoversAccuracyTheScalingHadCostThem()
    {
        // The point of the whole routine. This matrix's own scaling is worth five digits of its
        // eigenvalues, and balancing is what buys them back: 1 ± sqrt(2) come out exactly, both from
        // the balanced matrix and from the scaled one it came from — because since M90 eig balances
        // before it iterates, exactly as LAPACK's own driver does. Before that it did not, and this
        // test asserted the deficiency: that eig(A) came out five digits worse than eig(B).
        Assert.Equal("1 1\n", RunAndRead("""
            A = [1 1e6 0; 1e-6 1 1e-6; 0 1e6 1];
            [~, B] = balance(A);
            exact = sort([1 - sqrt(2), 1, 1 + sqrt(2)]);
            scaled = max(abs(sort(real(eig(A))) - exact));
            balanced = max(abs(sort(real(eig(B))) - exact));
            fprintf('%d %d\n', scaled < 1e-9, balanced < 1e-9);
            """));
    }

    [Fact]
    public void AMatrixThatIsAlreadyBalancedIsLeftAlone()
    {
        Assert.Equal("1 1 1\n", RunAndRead("""
            C = [2 1; 1 3];
            [T, B] = balance(C);
            fprintf('%d %d %d\n', max(max(abs(B - C))) < 1e-12, T(1,1) == 1, T(2,2) == 1);
            """));
    }

    [Fact]
    public void OneOutputIsTheBalancedMatrix()
    {
        Assert.Equal("1\n", RunAndRead("""
            A = [1 1e6 0; 1e-6 1 1e-6; 0 1e6 1];
            [~, expected] = balance(A);
            fprintf('%d\n', max(max(abs(balance(A) - expected))) < 1e-12);
            """));
    }

    // --- qz --------------------------------------------------------------------------------------

    [Fact]
    public void QzProducesAGeneralizedSchurFormOfThePencil()
    {
        Assert.Equal("1 1 1 1 1\n", RunAndRead("""
            A = [1 2 3; 4 5 6; 7 8 10];
            B = [2 0 1; 0 3 0; 1 0 4];
            [AA, BB, Q, Z] = qz(A, B);
            fprintf('%d %d %d %d %d\n', ...
                max(max(abs(Q * A * Z - AA))) < 1e-9, ...
                max(max(abs(Q * B * Z - BB))) < 1e-9, ...
                max(max(abs(Q * Q' - eye(3)))) < 1e-9, ...
                max(max(abs(Z * Z' - eye(3)))) < 1e-9, ...
                abs(BB(2,1)) + abs(BB(3,1)) + abs(BB(3,2)) < 1e-12);
            """));
    }

    [Fact]
    public void TheDiagonalRatiosAreThePencilsEigenvalues()
    {
        Assert.Equal("1\n", RunAndRead("""
            A = [1 2 3; 4 5 6; 7 8 10];
            B = [2 0 1; 0 3 0; 1 0 4];
            [AA, BB] = qz(A, B);
            fprintf('%d\n', max(abs(sort(diag(AA) ./ diag(BB)) - sort(eig(B \ A)))) < 1e-8);
            """));
    }

    [Fact]
    public void ASingularSecondMatrixIsFactoredRatherThanRefused()
    {
        // Until M76 this was a refusal: the pencil has an eigenvalue at infinity, and the old
        // construction — the Schur form of B⁻¹A — could not be formed at all. The QZ iteration can,
        // and the same four relations hold for it as for any other pencil.
        Assert.Equal("1 1 1 1 1\n", RunAndRead("""
            A = [1 2; 3 4];
            B = [1 1; 1 1];
            [AA, BB, Q, Z] = qz(A, B);
            fprintf('%d %d %d %d %d\n', ...
                max(max(abs(Q * A * Z - AA))) < 1e-9, ...
                max(max(abs(Q * B * Z - BB))) < 1e-9, ...
                max(max(abs(Q * Q' - eye(2)))) < 1e-9, ...
                max(max(abs(Z * Z' - eye(2)))) < 1e-9, ...
                abs(BB(2,1)) < 1e-12);
            """));
    }

    [Fact]
    public void ASingularSecondMatrixPutsAZeroOnTheOtherDiagonal()
    {
        // Which is how an infinite eigenvalue is said: the ratio's denominator is exactly zero,
        // rather than a small number standing in for one.
        Assert.Equal("1\n", RunAndRead("""
            [~, BB] = qz([1 2; 3 4], [1 1; 1 1]);
            fprintf('%d\n', min(abs(diag(BB))) == 0);
            """));
    }

    [Fact]
    public void TheComplexFormIsRefusedByName()
    {
        Assert.Contains("2-by-2 block", Error("qz([1 2; 3 4], eye(2), 'complex');"));
    }

    // --- ordqz -----------------------------------------------------------------------------------

    [Fact]
    public void OrdqzMovesTheSelectedEigenvalueToTheFront()
    {
        Assert.Equal("1 1\n", RunAndRead("""
            A = [1 2; 3 4];
            [AA, BB, Q, Z] = qz(A, eye(2));
            before = diag(AA) ./ diag(BB);
            [A2, B2, Q2, Z2] = ordqz(AA, BB, Q, Z, [false true]);
            after = diag(A2) ./ diag(B2);
            fprintf('%d %d\n', abs(after(1) - before(2)) < 1e-8, ...
                max(max(abs(Q2 * A * Z2 - A2))) < 1e-9);
            """));
    }

    [Fact]
    public void ARegionWordIsRefusedInFavourOfASelection()
    {
        Assert.Contains("logical vector", Error("""
            [AA, BB, Q, Z] = qz([1 2; 3 4], eye(2));
            ordqz(AA, BB, Q, Z, 'lhp');
            """));
    }

    [Fact]
    public void TheSelectionMustCoverEveryEigenvalue()
    {
        Assert.Contains("one entry per eigenvalue", Error("""
            [AA, BB, Q, Z] = qz([1 2; 3 4], eye(2));
            ordqz(AA, BB, Q, Z, [true]);
            """));
    }
}
