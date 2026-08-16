using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M66 wave C: reading a sparse matrix without expanding it, solving with it, and the orderings and
/// incomplete factorizations that make a large one worth having.
/// </summary>
/// <remarks>
/// The four verbs the wave unblocked — subscript, <c>find</c>, transpose and backslash — are all the
/// same complaint: each of them silently threw the sparsity away or refused outright, so choosing
/// sparse storage bought a matrix that could be built and multiplied and almost nothing else. The
/// assertions below check the answers, and where a sparse answer must <em>stay</em> sparse they check
/// that too, because a correct dense answer is the failure this wave is about.
/// </remarks>
[Collection("JG facade")]
public class MatlabSparseCompletionTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSparseCompletionTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    // --- reading ---------------------------------------------------------------------------------

    [Fact]
    public void ASubscriptReadsAnEntryWithoutExpandingAnything()
    {
        Assert.Equal("2 3 0 4\n", RunAndRead("""
            S = sparse([1 0 2; 0 3 0; 4 0 5]);
            fprintf('%g %g %g %g\n', S(1,3), S(2,2), S(1,2), S(3));
            """));
    }

    [Fact]
    public void ASubscriptOutsideTheMatrixSaysSo()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            "S = sparse([1 0; 0 1]); S(3,1);", context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success);
        Assert.Contains("outside a 2x2 matrix", result.Message + _output.ErrorText);
    }

    [Fact]
    public void FindAnswersFromTheEntriesItStores()
    {
        // Column by column, which is the order the storage is in and the order MATLAB reports.
        Assert.Equal("5 1 3 2 1 3 1 4 3 2 5\n", RunAndRead("""
            S = sparse([1 0 2; 0 3 0; 4 0 5]);
            [i, j, v] = find(S);
            fprintf('%d %g %g %g %g %g %g %g %g %g %g\n', numel(i), i, v);
            """));
    }

    [Fact]
    public void FindWithOneOutputGivesLinearPositions()
    {
        Assert.Equal("5 1 9\n", RunAndRead("""
            S = sparse([1 0 2; 0 3 0; 4 0 5]);
            k = find(S);
            fprintf('%d %g %g\n', numel(k), k(1), k(end));
            """));
    }

    [Fact]
    public void ATransposedSparseMatrixIsStillSparse()
    {
        // It used to come back dense, so a single quote quietly undid the storage decision.
        Assert.Equal("1 3 3 2\n", RunAndRead("""
            S = sparse([1 0 2; 0 3 0; 4 0 5]);
            T = S';
            fprintf('%d %d %d %g\n', issparse(T), size(T, 1), size(T, 2), T(3,1));
            """));
    }

    // --- solving ---------------------------------------------------------------------------------

    [Fact]
    public void BackslashSolvesThroughTheSparseFactorization()
    {
        Assert.Equal("1 1 1 1\n", RunAndRead("""
            A = sparse([2 1 0; 1 3 1; 0 1 2]);
            x = A \ [3; 5; 3];
            fprintf('%g %g %g %d\n', x, max(abs(x - (full(A) \ [3; 5; 3]))) < 1e-10);
            """));
    }

    [Fact]
    public void SeveralRightHandSidesAreSolvedAtOnce()
    {
        Assert.Equal("2 2 1\n", RunAndRead("""
            A = sparse([2 0; 0 4]);
            X = A \ [2 4; 8 4];
            fprintf('%d %d %d\n', size(X, 1), size(X, 2), X(1,1));
            """));
    }

    // --- constructors ----------------------------------------------------------------------------

    [Fact]
    public void SpeyeIsSparseAndNzmaxCountsWhatIsStored()
    {
        Assert.Equal("1 3 3 3 1 5\n", RunAndRead("""
            E = speye(3);
            S = sparse([1 0 2; 0 3 0; 4 0 5]);
            fprintf('%d %d %d %d %g %d\n', issparse(E), size(E, 1), size(E, 2), nnz(E), E(2,2), nzmax(S));
            """));
    }

    // --- orderings --------------------------------------------------------------------------------

    [Fact]
    public void EveryOrderingIsAPermutation()
    {
        // What an ordering must be, and the only part of it a script can check for itself — which is
        // why it is the part asserted, with the quality of each ordering recorded in ADR 0066 instead.
        Assert.Equal("1 1 1\n", RunAndRead("""
            B = sparse([1 1 0 0; 1 1 1 0; 0 1 1 1; 0 0 1 1]);
            function tf = isperm(p, n)
                tf = numel(p) == n && numel(unique(p)) == n && min(p) == 1 && max(p) == n;
            end
            fprintf('%d %d %d\n', isperm(symrcm(B), 4), isperm(amd(B), 4), isperm(dissect(B), 4));
            """));
    }

    [Fact]
    public void DmpermPutsANonzeroOnEveryDiagonalItCan()
    {
        Assert.Equal("2 1 1 0\n", RunAndRead("""
            fprintf('%g %g %g %g\n', dmperm(sparse([0 1; 1 0])), dmperm(sparse([1 1; 0 0])));
            """));
    }

    // --- structure and incomplete factorizations ---------------------------------------------------

    [Fact]
    public void TheEliminationTreeChainsATridiagonalMatrix()
    {
        // Each column's elimination touches the next one and no other, so the tree is a path and its
        // postorder is the identity — the simplest shape the algorithm can produce, and the one that
        // says the walk up the tree is working.
        Assert.Equal("2 3 0 1 2 3\n", RunAndRead("""
            C = sparse([4 1 0; 1 4 1; 0 1 4]);
            [parent, post] = etree(C);
            fprintf('%g %g %g %g %g %g\n', parent, post);
            """));
    }

    [Fact]
    public void SymbfactCountsTheFactorWithoutFormingIt()
    {
        Assert.Equal("2 2 1\n", RunAndRead("""
            fprintf('%g %g %g\n', symbfact(sparse([4 1 0; 1 4 1; 0 1 4])));
            """));
    }

    [Fact]
    public void OnAPatternThatCannotFillTheIncompleteFactorsAreExact()
    {
        // A tridiagonal matrix's Cholesky factor is bidiagonal — inside the pattern already there —
        // so dropping the fill drops nothing, and both factorizations reproduce the matrix exactly.
        // That is the case where "incomplete" can be checked against something.
        Assert.Equal("1 1 1 1\n", RunAndRead("""
            C = sparse([4 1 0; 1 4 1; 0 1 4]);
            L = ichol(C);
            [Lu, U] = ilu(C);
            fprintf('%d %d %d %d\n', issparse(L), max(max(abs(full(L * L') - full(C)))) < 1e-9, ...
                issparse(U), max(max(abs(full(Lu * U) - full(C)))) < 1e-9);
            """));
    }

    [Fact]
    public void IcholNeedsAPositiveDefiniteMatrix()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            "ichol(sparse([-1 0; 0 1]));", context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success);
        Assert.Contains("positive definite", result.Message + _output.ErrorText);
    }
}
